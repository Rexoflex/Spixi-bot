# Roadmap — v1

**Revision 3** (after the session-1 review). 🟡 until Damir accepts D-020/D-021. Each batch = one PR through the
gates. **Scope moves, gates do not.**

## Honest estimate

The original target was 4–6 weeks (D-009). With the review's findings (legacy hotfix, legacy characterization
before the port, a harness that needs process isolation, four design items, BE on the critical path, CI-only
builds), the full list below takes **about 8 weeks at full pace; about 6 weeks only with the cuts** (D-032). To stay near 6 weeks, cut in this order:
B6 metrics dashboard → push gateway contract → timeouts (ban/kick stay). **This is the only slip rule.** If a batch still slips after the cuts, the remaining refactoring stops and the remaining fixes land in place behind the same tests (option A′); the gates stay.

Per-batch estimates (working days, including CI round trips and the review loop): B0 2 · D1 2 · B1a 4 · B1b 4 · D2 2 · D4 2 · B2 4 · B3 4 · B4 3 · D3 1 · B5 4 · B6 3 · B7 5 → **≈ 40 days ≈ 8 weeks at full pace**, fewer with parallel design items. Dates are set when Damir accepts D-032.

**Runtime:** .NET 8 support ends 2026-11-10, inside this plan. B0 and B1a run on .NET 8 by design (legacy characterization); production will run the B0 build on an unsupported runtime for up to ~2 weeks. Accepted risk unless Damir prefers a B0 re-build on .NET 10 (small, behaviour-neutral port of the hotfix only).

## Batches

| Batch | Outcome (who can do what, how we know) | Key tests / evidence |
|---|---|---|
| **B0 · Legacy hotfix** (days, needs the operator) | Closes the worst live exposure while the rework runs: API needs a token and listens on loopback only; admin UI off; message cost 0; **relayed chat, nick, avatar, reaction, delete and `leave` are accepted only if `sender == connection` AND the signature verifies with `endpoint.presence.pubkey`** (the key Core binds to the address at hello; not `serverPubKey`, which is set before that check). Honest clients sign all of these to bots. Rolled out **log-only first** (count failures per message type, test with store + redesign), then enforcing. **Still open after B0:** anyone who knows a member's public key can connect as that member and **read** what the member receives (Core T2). | Log-only phase report; manual checks with both apps; operator deploys. |
| **D1 · Design: harness** | Choose how bot and clients run in tests (out-of-process clients, AssemblyLoadContext isolation, or a replayer of recorded traffic) and how app-layer behaviour (store vs redesign leave flow, cursors) is reproduced. | Graded G1 doc. |
| **B1a · Characterize legacy** | Today's behaviour (bot `a5a3442` + Core `f6fb55b` + .NET 8) is recorded: traffic captures, wire snapshots, contract scenarios. | Recorded fixtures; tests green on legacy. |
| **B1b · Port** | The bot builds on .NET 10 and the pinned Core release tag (D-030); every difference from B1a is listed and accepted or fixed. CI on Linux; nightly build against Core HEAD. | B1a suite green or diff explained. |
| **D2 · Design: delivery semantics** | One graded design for: S1 per message class, ack timing (after a durable write), rejected-message rule vs the client cursor, gap-race gating, replies on the requesting connection, first-sync cap N (≤ the client's dedup window, 50) and the unknown-cursor rule. | Graded G1 doc + contract cases. |
| **D4 · Design: cutover, address & leave flow** (moved before B2) | Same address or new (option E) — this decides whether B2 needs a migration; how the pilot runs without two nodes sharing one wallet; reverse export for rollback; who holds the wallet key; leaver handling (`leaveConfirmed` vs gating the leaver's one persistent connection, costed as redial churn × leavers). | Graded G1 doc; rollback drill plan. |
| **B2 · Storage** | No acknowledged message is lost in a crash; history migrates byte-exact. | Crash injection; migration on a real copy (access-controlled); properties: gap-free, ordered. |
| **B3 · Delivery & join** | A new member can read and post within 2 s; a reconnecting member gets what it missed. | Join p95; gap-race test; unknown-cursor test. |
| **B4 · Identity & limits** | Nobody can post as someone else; no input can exhaust memory or disk; unsigned requests change nothing. | S1/S1b tests incl. honest-client cases (nick twice, leave + re-join, 2-day outage); fuzz. |
| **D3 · Design: admin surface** | Graded: existing signed `kickUser`/`banUser` bot actions (apps already send them) vs text commands vs operator CLI, and how promote/demote/timeout are done. | Graded G1 doc. |
| **B5 · Moderation & roles** | Owner (by address, config) and admins can moderate; it takes effect on the next message. | Permission-level properties; ban gates post/fan-out/history. |
| **B6 · Operations** | A self-hoster starts a safe bot from a config file and an injected secret; the operator sees hand-off and delivery numbers with no address or IP in logs. | No-address/IP log test; metrics; push off + gateway contract. |
| **B7 · Pilot & launch** | The community channel runs on the new bot with a tested rollback. | Soak 24 h / 1,000 clients; instrumented pilot cohort (a debug build of the redesign app that exports its local message log — owner: us, Spixi side); if that build is not ready, the launch gate uses harness ground truth only and says so; rollback drill. |

After v1: closed mode (needs T2 or a bot challenge; compare with Core P2P groups) · history paging codes · delivery
acks · encrypted media relay (images, voice) · integration bridge for bots and AI agents · reply-to · payments.

## BE critical path

| Need | Blocks | Ask by |
|---|---|---|
| Who operates production + who holds the wallet key (Damir/BE) | B0, B2, D4 | **before B0** |
| Which Core commit the store app ships with | B1a contract fidelity | before B1a |
| Approval for behaviour changes on the wire (kick/ban semantics, `leaveConfirmed`, first-sync cap) | B3, B5, D4 | at D2/D3/D4 review |
| Production data copy (if D4 keeps the address) | B2 migration test, B7 | before B2 |
| Push gateway status (`/v1` vs `/v2`, still accepting bot pushes?) | B6 | before B6 |
| Upstream PR review at each milestone (after B1b, B4, B7) | merge to `ixian-platform` | per milestone |

Not blocking v1 (BE backlog): Core client-transport challenge (T2) · client verification of relayed chat (T1) and bot
actions (T25) · the `getClient` recursion fix · the **reaction cursor bug** (client sets its history cursor to the reaction's *target* id → next sync may replay far back; server mitigation evaluated in D2) · paging/ack/reply-to codes · payments redesign · Core log redaction.
