using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 6 (unknown codes and layouts), client side: how Core k (both app
    /// modes) handles bot messages it does not know. A future bot may send new SpixiMessage codes, new bot actions or
    /// a longer BotInfo; this pins what today's clients do with them. The member injects each message as if the bot
    /// sent it (SimClient `inject`, parsed synchronously; every event it causes precedes its `injected` event).
    ///
    /// Characterized facts (asserted), each between the inject command and its `injected` event:
    ///   a. Positive control: an injected chat from the bot is received and stored, from = the bot.
    ///   b. Unknown SpixiMessageCode (245): accepted by the final `default:` of receiveData (C CoreStreamProcessor.cs
    ///      :1589-1590) and passed on as `other` type "245". 245 lies in Core k's reserved custom range 0xF0-0xFF
    ///      (C SpixiMessage.cs:78) but is still an undefined enum value.
    ///   c. Unknown bot action (99): onBotAction falls through its switch and returns false (C :2735), so receiveData
    ///      returns null: `rejected` botAction, action "99".
    ///   d. Core-k info with 8 trailing bytes: parsed; bytes after hideParticipantAddresses are ignored (C BotInfo.cs
    ///      :70-74), so the info is handled.
    ///   e. HAZARD: the legacy (Core f6) info layout plus 1 trailing byte: Core k sees bytes left after userCount, reads
    ///      randomId with 0xAB as its length, hits EndOfStream (C BotInfo.cs:70-74); the exception is caught at
    ///      C :1593 and receiveData returns null: `rejected` botAction, action "info", and no `info`. A legacy bot that
    ///      ever appends one field to BotInfo makes every Core k client drop its info.
    ///   f. The member is still live afterwards (a post is acked).
    ///
    /// Self-test `unknown` (L5, L18): tests/smoke/break-core.ps1 patches the CLIENT Core k so that an undefined code
    /// (receiveData's default) or action (after onBotAction's switch) calls Environment.FailFast. Both cases must
    /// fail with CLIENT-CRASHED-ON-UNKNOWN[case], thrown only when the positive control passed (the injection path
    /// works), the member's process then exited or crashed while waiting for an `injected` event (with up to
    /// HarnessConfig.CrashTimeout more for the death to be recorded: Windows Error Reporting can slow FailFast), and
    /// its stderr carries the patch's FailFast message "harness self-test 'unknown'" or it exited with the FailFast
    /// code COR_E_FAILFAST (the marker is tied to the patch, not to any death).
    /// </summary>
    public sealed class UnknownCodesTests
    {
        /// <summary>COR_E_FAILFAST (0x80131623): the documented exit code of Environment.FailFast on Windows .NET.</summary>
        private const int FailFastExitCode = unchecked((int)0x80131623);   // -2146232797

        private readonly ITestOutputHelper output;

        public UnknownCodesTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "unknown")]
        [InlineData("store")]
        [InlineData("redesign")]
        public async Task Unknown_Codes(string app)
        {
            string caseLabel = $"unknown:{app}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            SimMember member = await run.StartMemberAsync("member", app);
            ManagedProcess p = member.Process;
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
            int channel = await member.JoinAsync(run.Bot, "general");

            // a. Positive control (no marker: the injection path itself must work first).
            string text = $"injected chat {tag}";
            int controlMark = member.Inject("chat", ("channel", channel), ("text", text));
            OutputLine controlDone = await member.WaitInjectedAsync("chat", controlMark);
            OutputLine? chat = p.Between(controlMark, controlDone).FirstOrDefault(l => l.Ev == "received" && l.Str("text") == text);
            Assert.True(chat != null, "positive control: the injected chat was not received");
            Assert.True(chat!.Bool("stored") == true, $"positive control: the injected chat was not stored: {chat.Text}");
            Assert.Equal(run.Bot.Address, chat.Str("from"));

            // Waits for `injected` after the positive control: a dead process is the marker.
            async Task<List<OutputLine>> InjectCaseAsync(string label, string kind, params (string Key, object? Value)[] fields)
            {
                int mark = member.Inject(kind, fields);
                try
                {
                    OutputLine done = await member.WaitInjectedAsync(kind, mark);
                    return p.Between(mark, done);
                }
                catch (HarnessTimeoutException e)
                {
                    bool dead = p.Snapshot().Any(l => l.Ev is "exited" or "crashed");
                    if (!dead)
                    {
                        // A dying process may not have reached `exited` yet (WER can slow FailFast on Windows).
                        try
                        {
                            await p.WaitFor(l => l.Ev is "exited" or "crashed", HarnessConfig.CrashTimeout, "'exited' or 'crashed'", failOnExit: false, from: mark);
                            dead = true;
                        }
                        catch (HarnessTimeoutException) { }
                    }
                    if (!dead)
                    {
                        throw new HarnessTimeoutException($"no 'injected' for {label} and the member process is alive; not a crash. {e.Message}");
                    }
                    List<OutputLine> log = p.Snapshot();
                    // The patch fired: its FailFast message on stderr, OR the FailFast exit code (stderr may not be captured).
                    bool patchFired = log.Any(l => l.Stream == "stderr" && l.Text.Contains("harness self-test 'unknown'", StringComparison.Ordinal))
                        || log.Any(l => l.Ev == "exited" && l.Int("code") == FailFastExitCode);
                    // Two `crashed` events can exist (SimClient's own, with `error`, and the W12 synthetic one): prefer the one with `error`.
                    OutputLine? death = log.LastOrDefault(l => l.Ev == "crashed" && !l.IsNull("error"))
                        ?? log.LastOrDefault(l => l.Ev == "crashed")
                        ?? log.LastOrDefault(l => l.Ev == "exited");
                    if (patchFired)
                    {
                        throw new HarnessTimeoutException($"{Markers.Tag(Markers.ClientCrashedOnUnknown, caseLabel)}: the positive control passed, then " +
                            $"the member's process died on the injected {label} with the patch's FailFast message: {death?.Text}. {e.Message}");
                    }
                    throw new HarnessTimeoutException($"the member's process died on the injected {label}, but not by the 'unknown' patch " +
                        $"(no FailFast message on stderr, exit code not COR_E_FAILFAST); not a clean unknown-code failure: {death?.Text}. {e.Message}");
                }
            }

            // b. Unknown SpixiMessageCode.
            List<OutputLine> b = await InjectCaseAsync("code 245", "unknown-code", ("code", 245), ("channel", channel));
            Assert.True(b.Any(l => l.Ev == "other" && l.Str("type") == "245"), "unknown code 245 was not passed on as 'other'");

            // c. Unknown bot action.
            List<OutputLine> c = await InjectCaseAsync("action 99", "unknown-action", ("action", 99));
            Assert.True(c.Any(l => l.Ev == "rejected" && l.Str("type") == "botAction" && l.Str("action") == "99"),
                "unknown bot action 99 was not rejected");

            // d. Core-k info with trailing bytes.
            string nameD = "harness-trailing-" + tag;
            List<OutputLine> d = await InjectCaseAsync("info-k + 8 trailing bytes", "info-k", ("serverName", nameD),
                ("randomId", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()), ("hide", false), ("trailing", 8));
            Assert.True(d.Any(l => l.Ev == "info" && l.Str("serverName") == nameD), "a Core-k info with trailing bytes was not handled");

            // e. Legacy layout + 1 trailing byte (hazard, characterized).
            string nameE = "harness-legacy-" + tag;
            List<OutputLine> e5 = await InjectCaseAsync("info-legacy + 1 trailing byte", "info-legacy", ("serverName", nameE), ("trailing", 1));
            Assert.True(e5.Any(l => l.Ev == "rejected" && l.Str("type") == "botAction" && l.Str("action") == "info"),
                "the legacy info with a trailing byte was not rejected");
            Assert.False(e5.Any(l => l.Ev == "info" && l.Str("serverName") == nameE), "the legacy info with a trailing byte was handled");

            // f. Still live.
            await member.PostAndWaitAckAsync(channel, $"after unknown {tag}");

            Assert.True(member.Healthy(), "the member reported an error, drop or exit");
            run.Pass();
        }
    }
}
