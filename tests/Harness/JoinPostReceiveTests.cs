using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// The harness spike scenario (harness.md W1, W8; D-031): two members join one bot; one posts; the other
    /// must receive it with the poster as author. Run as the two cross cases (session 3): store posts →
    /// redesign receives, and redesign posts → store receives.
    ///
    /// W8 / L5: CI job `harness-self-test` deletes the relay line in the bot (StreamProcessor.cs onChat,
    /// `NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message)`) and expects this test to fail
    /// with the marker <see cref="RelayMissingMarker"/>.
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
                await poster.Process.WaitForEvent("posted", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text);

                OutputLine got;
                try
                {
                    got = await receiver.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text);
                }
                catch (HarnessTimeoutException e)
                {
                    throw new HarnessTimeoutException($"{RelayMissingMarker}: the receiver never got the post. {e.Message}");
                }
                Assert.Equal(poster.Address, got.Str("from"));
                Assert.Equal(channel, got.Int("channel"));

                // Recorded, not asserted in the spike: the bot acks chat before relaying (StreamProcessor.cs:75-77).
                try
                {
                    await poster.Process.WaitForEvent("ack", HarnessConfig.DeliveryTimeout);
                    output.WriteLine("poster got the bot's msgReceived ack");
                }
                catch (HarnessTimeoutException e)
                {
                    output.WriteLine("NOTE: " + e.Message);
                }
                Report(runDir, processes, bot, failed: false);
            }
            catch (Exception)
            {
                Report(runDir, processes, bot, failed: true);
                throw;
            }
            finally
            {
                if (receiver != null) await receiver.DisposeAsync();
                if (poster != null) await poster.DisposeAsync();
                if (bot != null) await bot.DisposeAsync();
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
            string all = text.ToString();
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
