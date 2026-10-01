using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 2 (history cursor), against the legacy bot.
    ///
    /// After every `channel` the client's Core sends botGetMessages(channel, cursor), cursor = the id of the last
    /// message the client added on that channel, its own posts included (C CoreStreamProcessor.cs:2660-2673,
    /// FriendList.cs:343). The bot resends every stored message after FindLastIndex(cursor); an unknown cursor gives
    /// -1, so the whole channel is replayed (bot Messages.cs:196-220; research D §4).
    ///
    /// Scenario (one bot, a poster in redesign mode, a reader in the app mode under test):
    ///   A. The poster posts m1..m3 (each acked). The reader joins with no cursor and gets m1..m3 in order.
    ///   B. The poster posts m4; the reader gets it live (cursor := m4).
    ///   C. Known cursor: the reader refreshes, posts sentinel s1, and waits for the bot's ack of s1. The bot
    ///      processes one member's requests in order (one high-priority queue thread, Core f6fb55b
    ///      NetworkQueue.cs:258-297, :319), so the ack of s1 comes after the answer to the earlier botGetMessages.
    ///      Fact: that answer replays nothing.
    ///   D. Unknown cursor: the reader sets an id the bot never stored, refreshes, and gets the whole channel
    ///      (m1..m4, s1) in order.
    ///   E. Still responsive: the poster posts m5 and the reader gets it live.
    /// "refresh" replays the app's new-connection cascade on the open connection (SimClient README, divergence).
    ///
    /// Self-test `cursor` (L5, L18): the CI break makes the bot ignore the cursor (Messages.cs:208, always -1).
    /// Both cases must fail at step C with CURSOR-IGNORED[history:app], thrown only after the refresh reached the
    /// channel list and the sentinel was acked.
    /// </summary>
    public sealed class HistoryCursorTests
    {
        private readonly ITestOutputHelper output;

        public HistoryCursorTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "history")]
        [InlineData("store")]
        [InlineData("redesign")]
        public async Task History_Cursor(string readerApp)
        {
            string caseLabel = $"history:{readerApp}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            SimMember poster = await run.StartMemberAsync("poster", "redesign");
            SimMember reader = await run.StartMemberAsync("reader", readerApp);
            ManagedProcess r = reader.Process;
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);

            int channel = await poster.JoinAsync(run.Bot, "general");

            // A. History before the reader exists; the reader joins with no cursor.
            var history = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                string m = $"m{i} {tag}";
                await poster.PostAndWaitAckAsync(channel, m);
                history.Add(m);
            }
            int joinMark = r.Mark();
            Assert.Equal(channel, await reader.JoinAsync(run.Bot, "general"));
            await r.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == history[^1], joinMark);
            // The member may ask twice during the join (getInfo on connect and after acceptAddBot, both answered with
            // a channel list), so a second replay pass can follow; each pass is in order, so first arrivals are too.
            Assert.Equal(history, ReceivedTexts(r, joinMark, history).Distinct().ToList());

            // B. Live message; the reader's cursor becomes its id.
            string m4 = $"m4 {tag}";
            int liveMark = r.Mark();
            await poster.PostAndWaitAckAsync(channel, m4);
            await r.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == m4, liveMark);
            history.Add(m4);

            // C. Known cursor → nothing replayed. The bound is the refresh's own channel event: any late answer to
            // an earlier request (e.g. the join's second cascade) arrives before it (in-order processing, above).
            int knownMark = await RefreshAsync(reader);
            string s1 = $"s1 {tag}";
            int s1Mark = r.Mark();
            await reader.PostAndWaitAckAsync(channel, s1);
            List<string> replayed = ReceivedTexts(r, knownMark, history);
            if (replayed.Count > 0)
            {
                throw new HarnessTimeoutException($"{Markers.Tag(Markers.CursorIgnored, caseLabel)}: with cursor = the last live message the bot " +
                    $"replayed {replayed.Count} older message(s): [{string.Join(", ", replayed)}].");
            }
            history.Add(s1);
            // The bot acks s1 before it relays it (StreamProcessor.cs:76 before :418): take the echo before step D.
            await r.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == s1, s1Mark);

            // D. Unknown cursor → the whole channel, in order.
            int cursorMark = reader.SetCursor(channel, Guid.NewGuid().ToString("N"));
            await r.WaitFor(l => l.Ev == "cursor_set", HarnessConfig.DeliveryTimeout, "'cursor_set' event",
                failIf: l => l.Ev == "error" && l.Str("where") == "command", from: cursorMark);
            int unknownMark = await RefreshAsync(reader);
            await r.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == s1, unknownMark);
            Assert.Equal(history, ReceivedTexts(r, unknownMark, history));

            // E. Still responsive after the full replay.
            string m5 = $"m5 {tag}";
            int afterMark = r.Mark();
            await poster.PostAndWaitAckAsync(channel, m5);
            await r.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == m5, afterMark);

            Assert.True(poster.Healthy() && reader.Healthy(), "a member reported an error, drop or exit");
            run.Pass();
        }

        /// <summary>
        /// Refresh and wait until the bot's channel list arrived again (Core then sent botGetMessages). Returns the
        /// position of that channel event: the replay that answers this refresh comes after it.
        /// </summary>
        private static async Task<int> RefreshAsync(SimMember reader)
        {
            int mark = reader.Refresh();
            await reader.Process.WaitForEvent("refresh_sent", HarnessConfig.DeliveryTimeout, from: mark);
            OutputLine ch = await reader.Process.WaitForEvent("channel", HarnessConfig.DeliveryTimeout, l => l.Str("name") == "general", mark);
            return reader.Process.Snapshot().IndexOf(ch);
        }

        /// <summary>Texts of `received` events after <paramref name="from"/> that belong to <paramref name="known"/>, in arrival order.</summary>
        private static List<string> ReceivedTexts(ManagedProcess p, int from, List<string> known) =>
            p.Since(from).Where(l => l.Ev == "received" && known.Contains(l.Str("text") ?? "")).Select(l => l.Str("text")!).ToList();
    }
}
