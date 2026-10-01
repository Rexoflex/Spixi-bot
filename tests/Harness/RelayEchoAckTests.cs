using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 3 (relay, own echo, msgReceived), against the legacy bot. Grown from
    /// the session-3 spike Join_Post_Receive (D-031, harness.md W1/W8). Two members join one bot; one posts.
    ///
    /// Characterized facts (asserted):
    ///   1. Relay: the receiver gets the post with the poster as author, on the channel it was posted to, by the
    ///      live relay (the receiver connected once, so it was not a history replay), and Core stores it.
    ///   2. msgReceived: the bot acks the poster with the message id AND the message channel
    ///      (StreamProcessor.cs:75-77; research D §1a).
    ///   3. Own echo: the bot relays the post to every connected member, the poster included (Core f6fb55b
    ///      NetworkServer.forwardMessage(code, data) has no exclusion when called without one, :427-444; the bot
    ///      calls it so, StreamProcessor.cs:418). The poster's Core finds the id already stored and does not store
    ///      it again (Core k FriendList.cs:258-266).
    ///
    /// Self-tests (L5, L18), CI matrix `variant`:
    ///   relay: the break removes the relay statement (:418). Both cases must fail with W8-RELAY-MISSING[case],
    ///          thrown only when the bot acked this post and the receiver stayed healthy.
    ///   ack:   the break removes the chat ack (:76). Both cases must fail with ACK-MISSING[case], thrown only when
    ///          the receiver got the relay (so the bot processed the post) and the poster stayed healthy.
    /// </summary>
    public sealed class RelayEchoAckTests
    {
        private readonly ITestOutputHelper output;

        public RelayEchoAckTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "relay")]
        [InlineData("store", "redesign")]
        [InlineData("redesign", "store")]
        public async Task Relay_Echo_Ack(string posterApp, string receiverApp)
        {
            string caseLabel = $"relay:{posterApp}->{receiverApp}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            SimMember poster = await run.StartMemberAsync("poster", posterApp);
            SimMember receiver = await run.StartMemberAsync("receiver", receiverApp);

            int channel = await receiver.JoinAsync(run.Bot, "general");
            int posterChannel = await poster.JoinAsync(run.Bot, "general");
            Assert.Equal(channel, posterChannel);

            string text = $"harness {caseLabel} {System.Guid.NewGuid():N}";
            int mark = poster.Post(channel, text);
            OutputLine posted = await poster.Process.WaitForEvent("posted", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text, mark);
            string? postedId = posted.Str("id");
            Assert.False(string.IsNullOrEmpty(postedId), "posted event without a message id");
            bool Acked() => poster.Process.Since(mark).Any(l => l.Ev == "ack" && l.Str("id") == postedId);

            // 1. Relay. The bot acks before it relays (:75-77 runs before onChat). If the receiver times out, an ack
            // for THIS id proves the post reached the bot, so the timeout isolates the relay (review R1 M1, m5).
            OutputLine got;
            try
            {
                got = await receiver.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text);
            }
            catch (HarnessTimeoutException e)
            {
                if (Acked() && receiver.Healthy())
                {
                    throw new HarnessTimeoutException($"{Markers.Tag(Markers.RelayMissing, caseLabel)}: the bot acked the post but the healthy receiver never got it. {e.Message}");
                }
                throw new HarnessTimeoutException($"receiver got nothing (acked={Acked()}, receiver healthy={receiver.Healthy()}); not a clean relay failure. {e.Message}");
            }
            Assert.Equal(poster.Address, got.Str("from"));
            Assert.Equal(channel, got.Int("channel"));
            Assert.Equal(postedId, got.Str("id"));
            Assert.True(got.Event?.GetProperty("stored").GetBoolean() == true, "the receiver's Core did not store the relayed message");

            // Delivery must be the live relay, not a history replay after a reconnect (botGetMessages follows every
            // channel list, Core CoreStreamProcessor.cs:2660-2673): the receiver connected exactly once.
            int receiverConnects = receiver.Process.Snapshot().Count(l => l.Ev == "connected" && l.At <= got.At);
            Assert.True(receiverConnects == 1, $"receiver connected {receiverConnects} times; a reconnect could deliver by history replay (review R1 m1)");

            // 2. msgReceived. The receiver has the relay, so the bot processed the post: a missing ack is the ack alone.
            OutputLine ack;
            try
            {
                ack = await poster.Process.WaitForEvent("ack", HarnessConfig.DeliveryTimeout, l => l.Str("id") == postedId, mark);
            }
            catch (HarnessTimeoutException e)
            {
                string seen = string.Join(", ", poster.Process.Since(mark).Where(l => l.Ev == "ack").Select(l => l.Str("id")));
                if (poster.Healthy())
                {
                    throw new HarnessTimeoutException($"{Markers.Tag(Markers.AckMissing, caseLabel)}: the bot relayed the post but never acked it to the healthy poster; acks seen: [{seen}]. {e.Message}");
                }
                throw new HarnessTimeoutException($"no ack and the poster was not healthy; acks seen: [{seen}]. {e.Message}");
            }
            Assert.Equal(channel, ack.Int("channel"));

            // 3. Own echo, deduped by id.
            OutputLine echo = await poster.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == text, mark);
            Assert.Equal(postedId, echo.Str("id"));
            Assert.True(echo.Event?.GetProperty("self").GetBoolean() == true, "the echo is not marked as the poster's own message");
            Assert.True(echo.Event?.GetProperty("stored").GetBoolean() == false, "the poster's Core stored its own echo a second time");

            Assert.True(poster.Healthy() && receiver.Healthy(), "a member reported an error, drop or exit");
            run.Pass();
        }
    }
}
