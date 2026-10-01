using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 7 (leave), against the legacy bot.
    ///
    /// The app's leave: store (U ContactDetails.xaml.cs onRemove :122-132) marks the bot pendingDeletion, saves and
    /// sends leave; redesign (R Utils/SContacts.cs leaveGroup :70-119) sends leave and removes the bot contact at
    /// once. Bot side (StreamProcessor.cs): the leave gets an ack with channel -1 (:79-81) before the switch, then
    /// onLeave (:191, :282-299) marks the member `left`, saves and sends leaveConfirmed. sendUsers lists only members
    /// with status normal (:538-554), so a later joiner no longer sees the leaver.
    ///
    /// Characterized facts (asserted):
    ///   1. The bot processes the leave (sync: store gets the ack; redesign already removed the bot, so its Core
    ///      rejects the ack, `rejected` msgReceived).
    ///   2. A late joiner's user list (its first-cascade getUsers is answered before getChannels, so the list is
    ///      complete when JoinAsync returns) contains the joiner and not the leaver.
    ///   3. store: leaveConfirmed on a pendingDeletion bot makes Core k remove the friend and look up its stream client
    ///      (C CoreStreamProcessor.cs:1400-1415); StreamClientManager.getClient(Address, bool) calls itself
    ///      (C Network/StreamClientManager.cs:118-121): the store-mode process dies of a stack overflow (W12 synthetic
    ///      `crashed`, stackOverflow=true). This is a client Core k defect, characterized here, not a bot fact. The
    ///      overflow relies on the first (Tier-0) JIT compile of the self-recursive tail call getClient (C :118-121):
    ///      Tier-0 code makes no tail call, so the stack grows. With DOTNET_TieredCompilation=0 (optimized JIT at
    ///      once) the call may become a loop and the process would hang instead (this wait then times out).
    ///      redesign: the bot is no longer a friend, so leaveConfirmed is rejected and the process lives on.
    ///
    /// Self-test `leave` (L5, L18): the break removes the onLeave call (:191). Both cases must fail with
    /// LEAVE-IGNORED[case], thrown only when the bot processed the leave (fact 1 sync) and the late joiner's user
    /// list arrived (it lists the joiner itself).
    /// </summary>
    public sealed class LeaveTests
    {
        private readonly ITestOutputHelper output;

        public LeaveTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "leave")]
        [InlineData("store")]
        [InlineData("redesign")]
        public async Task Leave(string app)
        {
            string caseLabel = $"leave:{app}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            SimMember leaver = await run.StartMemberAsync("leaver", app);
            ManagedProcess lp = leaver.Process;
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            int channel = await leaver.JoinAsync(run.Bot, "general");
            // An acked post empties the pending queue, so the leave goes out at once (C PendingMessageProcessor.cs:313-315).
            await leaver.PostAndWaitAckAsync(channel, $"before leave {tag}");

            // 1. Leave, and sync on the bot's ack of it.
            int mark = leaver.Leave();
            OutputLine sent = await leaver.WaitCommandEventAsync("leave_sent", null, mark);
            Assert.True(sent.Bool("removed") == (app == "redesign"), $"leave_sent.removed does not match the {app} app rule: {sent.Text}");
            await lp.WaitFor(l => (l.Ev == "ack" && l.Int("channel") == -1) || (l.Ev == "rejected" && l.Str("type") == "msgReceived"),
                HarnessConfig.DeliveryTimeout, "the bot's ack of the leave (ack or rejected msgReceived)", from: mark);

            // 2. Late joiner.
            SimMember joiner = await run.StartMemberAsync("joiner", app);
            await joiner.JoinAsync(run.Bot, "general");
            await joiner.Process.WaitForEvent("user", HarnessConfig.DeliveryTimeout, l => l.Str("address") == joiner.Address);
            if (joiner.Process.Snapshot().Any(l => l.Ev == "user" && l.Str("address") == leaver.Address))
            {
                throw new HarnessTimeoutException($"{Markers.Tag(Markers.LeaveIgnored, caseLabel)}: the bot acked the leave, but its user list " +
                    "for a later joiner still contains the leaver.");
            }

            // 3. What the leaver's Core does with leaveConfirmed.
            if (app == "store")
            {
                // DeliveryTimeout for leaveConfirmed to arrive + CrashTimeout for the death to be recorded (WER, see HarnessConfig).
                OutputLine crash = await lp.WaitFor(l => l.Ev == "crashed" && l.Str("source") == "exit", HarnessConfig.DeliveryTimeout + HarnessConfig.CrashTimeout,
                    "synthetic 'crashed' (W12) after leaveConfirmed", failOnExit: false, from: mark);
                Assert.True(crash.Bool("stackOverflow") == true, $"the store-mode leaver died, but not of a stack overflow: {crash.Text}");
            }
            else
            {
                await lp.WaitForEvent("rejected", HarnessConfig.DeliveryTimeout, l => l.Str("type") == "leaveConfirmed", mark);
                Assert.False(lp.Snapshot().Any(l => l.Ev is "exited" or "crashed"), "the redesign-mode leaver exited after leaveConfirmed");
            }

            // The dead store-mode process is expected; the report and dispose handle an exited process.
            Assert.True(joiner.Healthy(), "the late joiner reported an error, drop or exit");
            run.Pass();
        }
    }
}
