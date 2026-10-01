using System;
using System.IO;
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

        public static async Task<SimMember> StartAsync(string runDir, string name, string app)
        {
            string data = Path.Combine(runDir, name);
            Directory.CreateDirectory(data);
            var m = new SimMember(name, app, data);
            OutputLine ready = await m.Process.WaitForEvent("ready", HarnessConfig.StartTimeout).ConfigureAwait(false);
            m.Address = ready.Str("address") ?? throw new InvalidOperationException("ready without address");
            return m;
        }

        /// <summary>Join and wait until the bot's channel list arrives (the app needs the channel locally to store
        /// chat, Core Friend.cs:899-907). Returns the index of the named channel.</summary>
        public async Task<int> JoinAsync(BotProcess bot, string channelName)
        {
            Process.Send(new { cmd = "join", host = bot.Host, address = bot.Address });
            await Process.WaitForEvent("connected", HarnessConfig.JoinTimeout).ConfigureAwait(false);
            await Process.WaitForEvent("accepted", HarnessConfig.JoinTimeout).ConfigureAwait(false);
            OutputLine ch = await Process.WaitForEvent("channel", HarnessConfig.JoinTimeout, l => l.Str("name") == channelName).ConfigureAwait(false);
            return ch.Int("index") ?? throw new InvalidOperationException("channel event without index");
        }

        public void Post(int channel, string text) => Process.Send(new { cmd = "post", channel, text });

        public ValueTask DisposeAsync()
        {
            try { Process.Send(new { cmd = "quit" }); } catch (Exception) { }
            return Process.DisposeAsync();
        }
    }
}
