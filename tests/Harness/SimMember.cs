using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Harness
{
    /// <summary>One SimClient process = one member (harness.md §2 A). Fresh data folder and wallet each time.</summary>
    internal sealed class SimMember : IAsyncDisposable
    {
        public ManagedProcess Process { get; }
        public string App { get; }
        public string Address { get; private set; } = "";

        private SimMember(string name, string app, string dataDir)
        {
            App = app;
            Process = new ManagedProcess($"{name}({app})", "dotnet",
                new[] { HarnessConfig.SimClientDll, "--app", app, "--data", dataDir, "--name", name },
                Path.GetDirectoryName(HarnessConfig.SimClientDll)!);
        }

        /// <summary>Starts the process. Call <see cref="WaitReadyAsync"/> next; the caller registers the process
        /// for the failure report first, so a start-up crash is in the report (CI run 36841419911).</summary>
        public static SimMember Start(string runDir, string name, string app)
        {
            string data = Path.Combine(runDir, name);
            Directory.CreateDirectory(data);
            return new SimMember(name, app, data);
        }

        public async Task WaitReadyAsync()
        {
            OutputLine ready = await Process.WaitForEvent("ready", HarnessConfig.StartTimeout).ConfigureAwait(false);
            Address = ready.Str("address") ?? throw new InvalidOperationException("ready without address");
        }

        /// <summary>Join and wait until the bot's channel list arrives (the app needs the channel locally to store
        /// chat, Core Friend.cs:899-907). Returns the index of the named channel.</summary>
        public async Task<int> JoinAsync(BotProcess bot, string channelName)
        {
            SendJoin(bot);
            // A join "error" (e.g. no hello within 30 s) ends the wait at once with its reason (review R1 n6).
            await Process.WaitFor(l => l.Ev == "connected", HarnessConfig.JoinTimeout, "'connected' event",
                failIf: l => l.Ev == "error" && l.Str("where") == "join").ConfigureAwait(false);
            await Process.WaitForEvent("accepted", HarnessConfig.JoinTimeout).ConfigureAwait(false);
            OutputLine ch = await Process.WaitForEvent("channel", HarnessConfig.JoinTimeout, l => l.Str("name") == channelName).ConfigureAwait(false);
            return ch.Int("index") ?? throw new InvalidOperationException("channel event without index");
        }

        /// <summary>Sends the join command only; the join handshake scenario waits for each step itself.</summary>
        public int SendJoin(BotProcess bot) => Process.Send(new { cmd = "join", host = bot.Host, address = bot.Address });

        public int Post(int channel, string text) => Process.Send(new { cmd = "post", channel, text });

        /// <summary>Replays the app's new-connection cascade (getInfo → … → botGetMessages with the stored cursor).</summary>
        public int Refresh() => Process.Send(new { cmd = "refresh" });

        public int SetCursor(int channel, string hexId) => Process.Send(new { cmd = "set-cursor", channel, id = hexId });

        /// <summary>
        /// Posts and waits for the bot's ack of THIS message id. Returns the id. The ack proves the bot processed
        /// the post (StreamProcessor.cs:75-77 runs before onChat), and because the bot handles one client's
        /// s2data in arrival order (one high-priority queue thread, Core f6fb55b NetworkQueue.cs:258-297, :319),
        /// it also proves every earlier request from this member was processed first.
        /// </summary>
        public async Task<string> PostAndWaitAckAsync(int channel, string text)
        {
            int mark = Post(channel, text);
            OutputLine posted = await WaitPostedAsync(text, mark).ConfigureAwait(false);
            string id = posted.Str("id") ?? throw new InvalidOperationException("posted event without a message id");
            await Process.WaitForEvent("ack", HarnessConfig.DeliveryTimeout, l => l.Str("id") == id, mark).ConfigureAwait(false);
            return id;
        }

        /// <summary>Waits for the `posted` event of <paramref name="text"/>; a failed post command ends the wait at once.</summary>
        public Task<OutputLine> WaitPostedAsync(string text, int from) =>
            Process.WaitFor(l => l.Ev == "posted" && l.Str("text") == text, HarnessConfig.DeliveryTimeout, "'posted' event",
                failIf: l => l.Ev == "error" && l.Str("where") == "command", from: from);

        /// <summary>
        /// L18 link proof: posts <paramref name="text"/>, waits for the bot's ack of it AND for its own echo (the bot
        /// relays to every member, the poster included, StreamProcessor.cs:418). Both directions of this member's
        /// connection work. False on a timeout; never throws a marker.
        /// </summary>
        public async Task<bool> TryProveLinkAsync(int channel, string text)
        {
            int mark = Process.Mark();
            try
            {
                await PostAndWaitAckAsync(channel, text).ConfigureAwait(false);
                await Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text, mark).ConfigureAwait(false);
                return true;
            }
            catch (HarnessTimeoutException)
            {
                return false;
            }
        }

        /// <summary>App rule `react` (SimClient README): local addReaction, then sendReaction only if it was added. Emits `reacted`.</summary>
        public int React(int channel, string hexId, string reaction) => Process.Send(new { cmd = "react", channel, id = hexId, reaction });

        /// <summary>App rule `delete` for a bot: sendMsgDelete, no local delete. Emits `delete_sent`.</summary>
        public int Delete(int channel, string hexId) => Process.Send(new { cmd = "delete", channel, id = hexId });

        /// <summary>App rule `leave` (store: pendingDeletion + sendLeave; redesign: sendLeave + removeFriend). Emits `leave_sent`.</summary>
        public int Leave() => Process.Send(new { cmd = "leave" });

        /// <summary>
        /// Injects a message as if the bot sent it (SimClient `inject`, parsed synchronously by the member's Core).
        /// Every event the injection caused comes before its `injected` event (<see cref="WaitInjectedAsync"/>).
        /// </summary>
        public int Inject(string kind, params (string Key, object? Value)[] fields)
        {
            var command = new Dictionary<string, object?> { ["cmd"] = "inject", ["kind"] = kind };
            foreach ((string key, object? value) in fields)
            {
                command[key] = value;
            }
            return Process.Send(command);
        }

        /// <summary>Waits for the `injected` event of <paramref name="kind"/>; an `error` after the command ends the wait at once.</summary>
        public Task<OutputLine> WaitInjectedAsync(string kind, int from) =>
            WaitCommandEventAsync("injected", l => l.Str("kind") == kind, from);

        /// <summary>
        /// Waits for the event a command emits after its action (`reacted`, `delete_sent`, `leave_sent`, `injected`).
        /// An `error` event after the command (a failed command) ends the wait at once.
        /// </summary>
        public Task<OutputLine> WaitCommandEventAsync(string ev, Func<OutputLine, bool>? and, int from) =>
            Process.WaitFor(l => l.Ev == ev && (and == null || and(l)), HarnessConfig.DeliveryTimeout, $"'{ev}' event",
                failIf: l => l.Ev == "error", from: from);

        /// <summary>No sign that the process cannot go on (exit, crash, fatal, a dropped message or an error).</summary>
        public bool Healthy() => !Process.Snapshot().Any(l => l.Ev is "exited" or "crashed" or "fatal" or "dropped" or "error");

        public ValueTask DisposeAsync()
        {
            try { Process.Send(new { cmd = "quit" }); } catch (Exception) { }
            return Process.DisposeAsync();
        }
    }
}
