using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// B1a characterization, research D §8 item 4 (reactions and deletes, moderation by admin), against the legacy bot.
    /// Member A posts, member B reacts and deletes. B joins first, so A's join cascade (getUsers) lists B: A's Core
    /// verifies a relayed group message with the group sender's key from that list; an unknown sender is
    /// rejected (C CoreStreamProcessor.cs:1377-1385).
    ///
    /// Characterized facts (asserted):
    ///   1. Reaction relay: the bot stores the reaction and relays the reactor's own message to every connected
    ///      member (StreamProcessor.cs onMsgReaction :330-341, forwardMessage without exclusion). A's Core accepts it
    ///      (`reaction` target M1 from B). B's own echo is rejected: B's Core already added it locally (app rule
    ///      addReaction first), Core k Friend.addReaction finds a duplicate (FriendMessage.cs:282) and
    ///      handleMsgReaction returns false.
    ///   2. A delete by a member who is neither admin nor author does nothing (onMsgDelete :309: isAdmin or sender).
    ///   3. An author delete: the bot acks it (channel -1, :79-81, before the switch), removes M1, makes its OWN
    ///      signed msgDelete (bot sender, new id, :313-322), stores it and relays it to every member (:324-326).
    ///      Both members' Cores accept it (`deleted` target M1, id != M1).
    ///   4. Admin by group: sb_newGroup(admins, admin=1) + sb_setUserGroup → the bot sends B a fresh info with
    ///      admin=true (APIServer.cs:251-266 → sendInfo :556-591, group.admin :584-587). Core k replaces its stored
    ///      botInfo only when settingsGeneratedTime changed (C :2678); every bot settings save stamps a new
    ///      generatedTime (bot Settings.cs:109, sb_newGroup saves at APIServer.cs:393), so the asserted fact is the
    ///      rule: storedAdmin == (storedGeneratedTime == generatedTime). An admin may then delete A's message M2
    ///      (isAdmin :343-358) and A gets `deleted` M2.
    ///
    /// Self-tests (L5, L18), CI matrix `variant`:
    ///   reaction:    the break removes the reaction relay (:340). Both cases must fail with REACTION-MISSING[case],
    ///                thrown only when B's Core added the reaction (so it was sent), B's later probe was acked (the
    ///                bot processed the reaction first: one queue per client, Core f6fb55b NetworkQueue.cs:258-297)
    ///                and A, healthy, received that probe (A's link is up; the bot writes to A's connection in order,
    ///                so a relayed reaction would precede the probe).
    ///   delete:      the break removes the delete relay (:326). Both cases must fail with DELETE-MISSING[case],
    ///                thrown only when the bot processed A's delete (A's later probe was acked: the bot handles one
    ///                client's messages in arrival order) and B proved its link (probe acked + own echo).
    ///   delete-sign: the break removes the signature of the bot's delete (:322; scoped, the statement occurs 4×).
    ///                Both cases must fail with DELETE-UNVERIFIED[case]: B got the delete (proof it arrived) but its
    ///                Core rejected it.
    ///   admin:       the break makes isAdmin return false (:357). Both cases must fail with
    ///                ADMIN-DELETE-NOT-RELAYED[case] (not "…-MISSING": DELETE-MISSING[case] would be a substring of it,
    ///                and CI matches markers with Select-String -SimpleMatch, L18), thrown only when B's info said
    ///                admin=true, the bot processed B's delete (B's later probe was acked) and A proved its link.
    /// </summary>
    public sealed class ReactDeleteTests
    {
        private readonly ITestOutputHelper output;

        public ReactDeleteTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [Trait("scenario", "react-delete")]
        [InlineData("store", "redesign")]
        [InlineData("redesign", "store")]
        public async Task React_Delete(string aApp, string bApp)
        {
            string caseLabel = $"react-delete:{aApp}->{bApp}";
            await using ScenarioRun run = await ScenarioRun.StartAsync(output, caseLabel);
            SimMember a = await run.StartMemberAsync("a", aApp);
            SimMember b = await run.StartMemberAsync("b", bApp);
            string tag = Guid.NewGuid().ToString("N").Substring(0, 8);

            int channel = await b.JoinAsync(run.Bot, "general");
            Assert.Equal(channel, await a.JoinAsync(run.Bot, "general"));
            // Precondition of fact 1: A's roster has B (the getUsers answer precedes the channel list, in order).
            await a.Process.WaitForEvent("user", HarnessConfig.DeliveryTimeout, l => l.Str("address") == b.Address);

            // 1. A posts M1; B gets and stores it (B's Core needs M1 locally to add a reaction to it).
            int bMark = b.Process.Mark();
            string m1Text = $"m1 {tag}";
            string m1 = await a.PostAndWaitAckAsync(channel, m1Text);
            OutputLine m1AtB = await b.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == m1Text, bMark);
            Assert.True(m1AtB.Bool("stored") == true, "B's Core did not store M1");

            // 2. B reacts to M1.
            int aMark = a.Process.Mark();
            bMark = b.React(channel, m1, "like:");
            OutputLine reacted = await b.WaitCommandEventAsync("reacted", l => l.HexIs("id", m1), bMark);
            Assert.True(reacted.Bool("localAdded") == true, "B's Core did not add the reaction locally, so the app rule sent nothing (U :1045-1058, R :2521-2540)");
            string p1 = $"p1 {tag}";
            await b.PostAndWaitAckAsync(channel, p1);
            await a.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == p1, aMark);
            OutputLine? reaction = a.Process.Since(aMark).FirstOrDefault(l => l.Ev == "reaction" && l.HexIs("target", m1) && l.Str("from") == b.Address);
            if (reaction == null)
            {
                OutputLine? rejected = a.Process.Since(aMark).FirstOrDefault(l => l.Ev == "rejected" && l.Str("type") == "msgReaction");
                if (rejected != null)
                {
                    throw new HarnessTimeoutException($"A's Core rejected the relayed reaction (roster or signature), so the relay ran; not a missing relay: {rejected.Text}");
                }
                if (!a.Healthy())
                {
                    throw new HarnessTimeoutException("A has no reaction and is not healthy; not a clean relay failure.");
                }
                throw new HarnessTimeoutException($"{Markers.Tag(Markers.ReactionMissing, caseLabel)}: B's reaction to M1 was processed by the bot " +
                    "(B's later probe was acked) and A received that probe, but A never got the reaction.");
            }
            Assert.Equal("like:", reaction.Str("reaction"));
            Assert.Equal(channel, reaction.Int("channel"));
            // B's own echo (forwardMessage has no exclusion): its Core already holds the reaction, so it rejects it.
            Assert.True(b.Process.Since(bMark).Any(l => l.Ev == "rejected" && l.Str("type") == "msgReaction"),
                "B did not get (and reject) its own reaction echo before the ack of its next post");

            // 3. Non-admin, non-author delete: the bot refuses silently (onMsgDelete :309).
            aMark = a.Process.Mark();
            bMark = b.Delete(channel, m1);
            await b.WaitCommandEventAsync("delete_sent", l => l.HexIs("id", m1), bMark);
            string p2 = $"p2 {tag}";
            await b.PostAndWaitAckAsync(channel, p2);
            await a.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == p2, aMark);
            Assert.False(a.Process.Since(aMark).Any(l => IsDeleteOf(l, m1)), "the bot relayed a delete of M1 by a member who is neither admin nor author");
            Assert.False(b.Process.Since(bMark).Any(l => IsDeleteOf(l, m1)), "the bot sent B a delete of M1 although B is neither admin nor author");

            // 4. Author delete.
            bMark = b.Process.Mark();
            aMark = a.Delete(channel, m1);
            await a.WaitCommandEventAsync("delete_sent", l => l.HexIs("id", m1), aMark);
            // The bot handles one client's messages in arrival order (Core f6fb55b NetworkQueue.cs:258-297), so the ack
            // of a later probe proves the delete was handled. The delete's own ack (channel -1) is not used:
            // `delete_sent` reports the target id, not the delete message's own id, so that ack cannot be matched
            // (review R2 n1). A has no queued cascade here; the probe is used for symmetry with step 5.
            await a.PostAndWaitAckAsync(channel, $"after-delete-a {caseLabel} {tag}");
            OutputLine bDelete;
            try
            {
                bDelete = await b.Process.WaitFor(l => IsDeleteOf(l, m1), HarnessConfig.DeliveryTimeout, "'deleted' or rejected msgDelete of M1", from: bMark);
            }
            catch (HarnessTimeoutException e)
            {
                bool bLinked = b.Healthy() && await b.TryProveLinkAsync(channel, $"probe-b {caseLabel} {tag}");
                if (bLinked)
                {
                    throw new HarnessTimeoutException($"{Markers.Tag(Markers.DeleteMissing, caseLabel)}: the bot processed A's delete of its own M1, " +
                        $"B's link is proven, but B got no delete. {e.Message}");
                }
                throw new HarnessTimeoutException($"B got no delete (B healthy={b.Healthy()}, B link proven={bLinked}); not a clean delete failure. {e.Message}");
            }
            if (bDelete.Ev == "rejected")
            {
                throw new HarnessTimeoutException($"{Markers.Tag(Markers.DeleteUnverified, caseLabel)}: the bot's delete of M1 reached B but B's Core rejected it: {bDelete.Text}");
            }
            Assert.False(bDelete.HexIs("id", m1), "the delete carries M1's own id; expected the bot's own delete message (:313-322)");
            Assert.Equal(channel, bDelete.Int("channel"));
            OutputLine aDelete = await a.Process.WaitFor(l => IsDeleteOf(l, m1), HarnessConfig.DeliveryTimeout, "A's 'deleted' (echo) of M1", from: aMark);
            Assert.True(aDelete.Ev == "deleted", $"A's Core rejected the bot's delete of A's own M1: {aDelete.Text}");

            // 5. Admin by group.
            await run.Bot.Api("sb_newGroup", ("group", "admins"), ("cost", "0"), ("admin", "1"), ("default", "0"));
            bMark = b.Process.Mark();
            await run.Bot.Api("sb_setUserGroup", ("address", b.Address), ("role", "admins"));
            OutputLine adminInfo = await b.Process.WaitForEvent("info", HarnessConfig.DeliveryTimeout, l => l.Bool("admin") == true, bMark);
            long? generated = adminInfo.Long("generatedTime");
            long? storedGenerated = adminInfo.Long("storedGeneratedTime");
            Assert.True(generated != null && storedGenerated != null, $"info without generatedTime/storedGeneratedTime: {adminInfo.Text}");
            bool replaced = generated == storedGenerated;
            Assert.True(adminInfo.Bool("storedAdmin") == replaced,
                $"Core k stores a new botInfo only when settingsGeneratedTime changed (C :2678): replaced={replaced}, but {adminInfo.Text}");
            b.Process.Note($"admin info: generatedTime {generated}, stored {storedGenerated} (botInfo replaced={replaced})");
            // A replaced botInfo makes Core send getGroups/getUsers/getChannels (+ botGetMessages per channel), each
            // acked with channel -1 and possibly still queued (C PendingMessageProcessor.cs:313-315). B's link must
            // still work after it; the delete below is synced by a later probe, not by a -1 ack.
            Assert.True(await b.TryProveLinkAsync(channel, $"sync-b {caseLabel} {tag}"), "B's link failed after the admin info");

            int bM2Mark = b.Process.Mark();
            string m2Text = $"m2 {tag}";
            string m2 = await a.PostAndWaitAckAsync(channel, m2Text);
            await b.Process.WaitForEvent("received", HarnessConfig.DeliveryTimeout, l => l.Str("text") == m2Text, bM2Mark);
            aMark = a.Process.Mark();
            bMark = b.Delete(channel, m2);
            await b.WaitCommandEventAsync("delete_sent", l => l.HexIs("id", m2), bMark);
            // In-order handling: the ack of B's later probe proves the bot handled the delete (a -1 ack could belong to
            // a queued botGetMessages of the admin-info cascade).
            await b.PostAndWaitAckAsync(channel, $"after-delete-b {caseLabel} {tag}");
            OutputLine aAdminDelete;
            try
            {
                aAdminDelete = await a.Process.WaitFor(l => IsDeleteOf(l, m2), HarnessConfig.DeliveryTimeout, "'deleted' or rejected msgDelete of M2", from: aMark);
            }
            catch (HarnessTimeoutException e)
            {
                bool aLinked = a.Healthy() && await a.TryProveLinkAsync(channel, $"probe-a {caseLabel} {tag}");
                if (aLinked)
                {
                    throw new HarnessTimeoutException($"{Markers.Tag(Markers.AdminDeleteNotRelayed, caseLabel)}: B's info says admin, the bot processed B's delete " +
                        $"of A's M2 and A's link is proven, but A got no delete. {e.Message}");
                }
                throw new HarnessTimeoutException($"A got no delete of M2 (A healthy={a.Healthy()}, A link proven={aLinked}); not a clean admin failure. {e.Message}");
            }
            Assert.True(aAdminDelete.Ev == "deleted", $"A's Core rejected the bot's admin delete of M2: {aAdminDelete.Text}");

            Assert.True(a.Healthy() && b.Healthy(), "a member reported an error, drop or exit");
            run.Pass();
        }

        /// <summary>A delete of <paramref name="hexId"/>: accepted (`deleted` target) or rejected (`rejected` msgDelete, data = the id).</summary>
        private static bool IsDeleteOf(OutputLine l, string hexId) =>
            (l.Ev == "deleted" && l.HexIs("target", hexId))
            || (l.Ev == "rejected" && l.Str("type") == "msgDelete" && l.HexIs("data", hexId));
    }
}
