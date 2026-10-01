using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 5 (BotInfo variants), against the legacy bot and the client Core k.
    ///
    /// The bot writes BotInfo in the Core f6fb55b layout (version, serverName, …, userCount; core-f6
    /// Streaming/Bot/BotInfo.cs:74-88) from its settings (StreamProcessor.cs sendInfo :589, serverName from
    /// sb_setOption, APIServer.cs:162-166). Core k reads randomId and hideParticipantAddresses only when bytes
    /// remain after userCount (C BotInfo.cs:70-74), stores the info when settingsGeneratedTime changed and sets the
    /// bot's nickname to serverName (C CoreStreamProcessor.cs:2676-2683).
    ///
    /// Characterized facts (asserted):
    ///   1. Legacy info: serverName = the configured name, the nickname follows it, randomId absent (null),
    ///      hide false.
    ///   2. A Core-k-layout info (injected, as a new bot would send it: randomId, hide=true, generatedTime + 1) is
    ///      parsed with randomId and hide, stored (storedGeneratedTime = generatedTime), the nickname follows the new
    ///      serverName, and Core asks the real bot for its channels again (C :2706; the `channel` answer follows).
    ///
    /// Self-test `servername` (L5, L18): the break replaces the serverName in sendInfo by "" (:589, inline). Both
    /// cases must fail with INFO-SERVERNAME-MISSING[case], thrown only when the info arrived after acceptance (the
    /// link is up) and the member is healthy.
    /// </summary>
    public sealed class InfoVariantsTests
    {
        private readonly ITestOutputHelper output;

        public InfoVariantsTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "info")]
        [InlineData("store")]
        [InlineData("redesign")]
        public async Task Info_Variants(string app)
        {
            string caseLabel = $"info:{app}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            string name = "harness-bot-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await run.Bot.Api("sb_setOption", ("serverName", name));

            SimMember member = await run.StartMemberAsync("member", app);
            ManagedProcess p = member.Process;
            int mark = p.Mark();
            int channel = await member.JoinAsync(run.Bot, "general");

            // 1. Legacy layout. The handshake fact is the answer after acceptance (see JoinHandshakeTests).
            OutputLine accepted = await p.WaitForEvent("accepted", HarnessConfig.JoinTimeout, from: mark);
            OutputLine info = await p.WaitForEvent("info", HarnessConfig.DeliveryTimeout, from: p.IndexOf(accepted));
            if (string.IsNullOrEmpty(info.Str("serverName")))
            {
                if (member.Healthy())
                {
                    throw new HarnessTimeoutException($"{Markers.Tag(Markers.InfoServerNameMissing, caseLabel)}: the bot answered getInfo " +
                        $"after acceptance with an empty serverName (configured '{name}'): {info.Text}");
                }
                throw new HarnessTimeoutException($"info with an empty serverName, and the member is not healthy; not a clean serverName failure: {info.Text}");
            }
            Assert.Equal(name, info.Str("serverName"));
            Assert.Equal(name, info.Str("nickname"));
            Assert.True(info.IsNull("randomId"), $"the legacy info carried a randomId: {info.Text}");
            Assert.True(info.Bool("hide") == false, $"the legacy info is not hide=false: {info.Text}");

            // 2. Core k layout, injected. First drain in-flight replies: the bot handles one client's requests in order
            // (Core f6fb55b NetworkQueue.cs:258-297), so once this probe is acked every earlier answer (e.g. the join
            // cascades' channel lists) has arrived, and the `channel` wait below can only match the answer to the
            // getChannels that the injected info caused (C :2706).
            await member.PostAndWaitAckAsync(channel, $"drain {caseLabel} {Guid.NewGuid():N}");
            string injectedName = "harness-injected-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string randomId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            int injectMark = member.Inject("info-k", ("serverName", injectedName), ("randomId", randomId), ("hide", true), ("trailing", 0));
            OutputLine injected = await member.WaitInjectedAsync("info-k", injectMark);
            OutputLine? kInfo = p.Between(injectMark, injected).FirstOrDefault(l => l.Ev == "info" && l.Str("serverName") == injectedName);
            Assert.True(kInfo != null, "the injected Core-k info was not handled as an info");
            Assert.True(kInfo!.HexIs("randomId", randomId), $"randomId not parsed: {kInfo.Text}");
            Assert.True(kInfo.Bool("hide") == true, $"hide not parsed: {kInfo.Text}");
            Assert.Equal(injectedName, kInfo.Str("nickname"));
            Assert.True(kInfo.Long("generatedTime") != null && kInfo.Long("storedGeneratedTime") == kInfo.Long("generatedTime"),
                $"the injected info was not stored: {kInfo.Text}");
            // Core sent getChannels to the real bot after it handled the info (C :2706); the bot answers.
            await p.WaitForEvent("channel", HarnessConfig.DeliveryTimeout, l => l.Str("name") == "general", p.IndexOf(kInfo));

            Assert.True(member.Healthy(), "the member reported an error, drop or exit");
            run.Pass();
        }
    }
}
