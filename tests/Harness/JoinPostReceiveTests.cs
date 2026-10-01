using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// The harness spike scenario (harness.md W1, W8; D-031): two members join one bot; one posts; the other
    /// must receive it with the poster as author. Run as the two cross cases (session 3): store posts →
    /// redesign receives, and redesign posts → store receives.
    ///
    /// W8 / L5: the CI job `harness` with variant `self-test` deletes the relay line in the bot
    /// (StreamProcessor.cs onChat, `NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message)`) and
    /// expects BOTH cases to fail with <see cref="RelayMissingMarker"/>[case]. The marker is thrown only when the
    /// bot acked the post and the receiver stayed healthy, so it means "relay missing" and nothing else.
    /// </summary>
    public sealed class JoinPostReceiveTests
    {
        public const string RelayMissingMarker = "W8-RELAY-MISSING";

        private readonly ITestOutputHelper output;

        public JoinPostReceiveTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("store", "redesign")]
        [InlineData("redesign", "store")]
        public async Task Join_Post_Receive(string posterApp, string receiverApp)
        {
            string runDir = Path.Combine(Path.GetTempPath(), "spixibot-harness", $"jpr-{posterApp}-{receiverApp}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(runDir);
            var processes = new List<ManagedProcess>();
            BotProcess? bot = null;
            SimMember? poster = null, receiver = null;
            try
            {
                bot = await BotProcess.StartAsync(runDir, processes);
                await bot.SeedDefaultsAsync();

                poster = SimMember.Start(runDir, "poster", posterApp);
                processes.Add(poster.Process);
                receiver = SimMember.Start(runDir, "receiver", receiverApp);
                processes.Add(receiver.Process);
                await poster.WaitReadyAsync();
                await receiver.WaitReadyAsync();

                int channel = await receiver.JoinAsync(bot, "general");
                int posterChannel = await poster.JoinAsync(bot, "general");
                Assert.Equal(channel, posterChannel);

                string text = $"harness {posterApp}->{receiverApp} {Guid.NewGuid():N}";
                poster.Post(channel, text);
                OutputLine posted = await poster.Process.WaitForEvent("posted", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text);
                string? postedId = posted.Str("id");
                Assert.False(string.IsNullOrEmpty(postedId), "posted event without a message id");

                // The bot acks a chat before it relays it (StreamProcessor.cs:75-77, onChat :377-420). An ack for
                // THIS message id proves the post reached the bot, so a later receive timeout isolates the relay
                // (review R1 M1, m5).
                // Run 36847424722 showed the poster's ack id equal to the message id (ack 48bedcc0… = received 48bedcc0…).
                try
                {
                    await poster.Process.WaitForEvent("ack", HarnessConfig.DeliveryTimeout, l => l.Str("id") == postedId);
                }
                catch (HarnessTimeoutException e)
                {
                    string seen = string.Join(", ", poster.Process.Snapshot().Where(l => l.Ev == "ack").Select(l => l.Str("id")));
                    throw new HarnessTimeoutException($"bot never acked the post {postedId}; acks seen: [{seen}] (review R2 r1). {e.Message}");
                }

                string caseName = $"{posterApp}->{receiverApp}";
                OutputLine got;
                try
                {
                    got = await receiver.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text);
                }
                catch (HarnessTimeoutException e)
                {
                    bool receiverHealthy = !receiver.Process.Snapshot().Any(l => l.Ev is "exited" or "crashed" or "fatal" or "dropped" or "error");
                    if (receiverHealthy)
                    {
                        // One marker per case; the self-test job requires both (review R1 M2).
                        throw new HarnessTimeoutException($"{RelayMissingMarker}[{caseName}]: the bot acked the post but the healthy receiver never got it. {e.Message}");
                    }
                    throw new HarnessTimeoutException($"receiver got nothing, and it was not healthy (see its exited/crashed/fatal/dropped/error events); not a clean relay failure. {e.Message}");
                }
                Assert.Equal(poster.Address, got.Str("from"));
                Assert.Equal(channel, got.Int("channel"));

                // Delivery must be the live relay, not a history replay after a reconnect (botGetMessages follows
                // every channel list, Core CoreStreamProcessor.cs:2660-2673): the receiver connected exactly once.
                int receiverConnects = receiver.Process.Snapshot().Count(l => l.Ev == "connected" && l.At <= got.At);
                Assert.True(receiverConnects == 1, $"receiver connected {receiverConnects} times; a reconnect could deliver by history replay (review R1 m1)");

                if (posterApp == "store" || receiverApp == "store")
                {
                    output.WriteLine("NOTE W3: store mode runs on Core k until BE-04 names the store app's Core (hypothesis).");
                }
                SafeReport(runDir, processes, bot, failed: false);
            }
            catch (Exception)
            {
                SafeReport(runDir, processes, bot, failed: true);
                throw;
            }
            finally
            {
                // Members first (they get 'quit' and exit cleanly), then every started process, including a bot
                // whose StartAsync threw (review R1 m2). Dispose is idempotent.
                if (receiver != null) await receiver.DisposeAsync();
                if (poster != null) await poster.DisposeAsync();
                foreach (ManagedProcess p in processes)
                {
                    await p.DisposeAsync();
                }
                bot?.DisposeHttp();
            }
        }

        private void SafeReport(string runDir, List<ManagedProcess> processes, BotProcess? bot, bool failed)
        {
            try
            {
                Report(runDir, processes, bot, failed);
            }
            catch (Exception e)
            {
                // A logging error must not hide the test result (review R1 n7).
                output.WriteLine("report failed: " + e.Message);
            }
        }

        /// <summary>W2: the full event log of every process goes to the test output and to HARNESS_ARTIFACTS.</summary>
        private void Report(string runDir, List<ManagedProcess> processes, BotProcess? bot, bool failed)
        {
            var text = new System.Text.StringBuilder();
            foreach (ManagedProcess p in processes)
            {
                text.Append(p.Dump());
            }
            if (bot != null && failed)
            {
                text.Append(bot.LogTail());
            }
            // The bot logs its config lines verbatim, including the API login (Config.cs:185). The password is a
            // random throwaway, but logs and annotations are public: redact it (review R1 m7, threat model S6).
            string all = Regex.Replace(text.ToString(), @"(addApiUser'?\s*=\s*'?)[^'\s]+", "$1<redacted>");
            output.WriteLine(all);
            string? art = HarnessConfig.ArtifactsDir;
            if (art != null)
            {
                Directory.CreateDirectory(art);
                File.WriteAllText(Path.Combine(art, Path.GetFileName(runDir) + (failed ? ".FAILED" : "") + ".log"), all);
            }
        }
    }
}
