using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 1 (join handshake), against the legacy bot.
    ///
    /// Two cascades run (pre-session review R1 item 1). (a) On the new connection SimNode sends getInfo, like the
    /// app (R/U NetworkProtocol.cs:109-112), before the contact request. The bot answers it for a member it does not
    /// know yet: setPubKey creates the member with status normal (StreamProcessor.cs:432; Core f6 BotUsers.cs:175-189,
    /// BotContact.cs:35), and info carries userCount = 1. Core k handles that info although the friend is not
    /// accepted yet: botInfo was null, so it sends getGroups, getUsers and getChannels (C CoreStreamProcessor.cs
    /// :2676-2708). The `user` answer comes from this cascade. (b) The app sends requestAdd2 (AppRules.AddBotContact);
    /// the bot answers acceptAddBot + its avatar (:100-114); Core sends nick + getInfo again (C :2072-2098); the bot
    /// answers info (:431-434); same settingsGeneratedTime and userCount, so Core sends only getChannels; the bot
    /// answers one `channel` per channel (:520-531). The bot does not handle getGroups (no answer, research D §1b).
    /// Every `channel` makes Core send botGetMessages (C :2660-2673).
    ///
    /// Characterized facts (asserted): connected → accepted → info → channel "general", info.defaultChannel is the
    /// seeded channel, and the bot's user list contains the joining member. Not asserted: the bot's avatar (a fresh
    /// bot may have none, Node.getAvatarBytes) and the absence of a getGroups answer (absence is not observable
    /// without a fixed wait, W2).
    ///
    /// Self-test `info` (L5, L18): the CI break removes `sendInfo` from the getInfo handler (StreamProcessor.cs:433).
    /// Both cases must fail with JOIN-INFO-MISSING[join:app], which is thrown only after the bot accepted the member
    /// and the member stayed healthy.
    /// </summary>
    public sealed class JoinHandshakeTests
    {
        private readonly ITestOutputHelper output;

        public JoinHandshakeTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "join")]
        [InlineData("store")]
        [InlineData("redesign")]
        public async Task Join_Handshake(string app)
        {
            string caseLabel = $"join:{app}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            SimMember member = await run.StartMemberAsync("member", app);
            ManagedProcess p = member.Process;

            int mark = member.SendJoin(run.Bot);
            OutputLine connected = await p.WaitFor(l => l.Ev == "connected", HarnessConfig.JoinTimeout, "'connected' event",
                failIf: l => l.Ev == "error" && l.Str("where") == "join", from: mark);
            OutputLine accepted = await p.WaitForEvent("accepted", HarnessConfig.JoinTimeout, from: mark);

            // SimNode sends getInfo as soon as the connection is up (like the app), which can be before the bot
            // knows the member; Core sends it again after acceptAddBot (C :2095). The handshake fact is the answer
            // AFTER acceptance, so the wait starts at the accepted event.
            int acceptedAt = p.Snapshot().IndexOf(accepted);
            OutputLine info;
            try
            {
                info = await p.WaitForEvent("info", HarnessConfig.DeliveryTimeout, from: acceptedAt);
            }
            catch (HarnessTimeoutException e)
            {
                if (member.Healthy())
                {
                    throw new HarnessTimeoutException($"{Markers.Tag(Markers.JoinInfoMissing, caseLabel)}: the bot accepted the member " +
                        $"but never answered getInfo. {e.Message}");
                }
                throw new HarnessTimeoutException($"no info, and the member was not healthy; not a clean getInfo failure. {e.Message}");
            }

            int infoAt = p.Snapshot().IndexOf(info);
            OutputLine channel = await p.WaitForEvent("channel", HarnessConfig.DeliveryTimeout, l => l.Str("name") == "general", infoAt);
            // The bot's user list (the getUsers answer of cascade (a)) contains the joining member.
            _ = await p.WaitForEvent("user", HarnessConfig.DeliveryTimeout, l => l.Str("address") == member.Address, mark);

            // Order of the cascade, by position in the member's event log.
            var log = p.Snapshot();
            Assert.True(log.IndexOf(connected) < log.IndexOf(accepted), "accepted before connected");

            // The seeded channel is the bot's default channel (BotProcess.SeedDefaultsAsync, default=1).
            Assert.Equal(channel.Int("index"), info.Int("defaultChannel"));

            // Only one channel was seeded: the bot never lists a channel the harness did not create.
            Assert.All(log.Where(l => l.Ev == "channel"), l => Assert.Equal("general", l.Str("name")));

            Assert.True(member.Healthy(), "member reported an error, drop or exit during the handshake");
            run.Pass();
        }
    }
}
