# Roadmap — v1

**Revision 4** (session 2). D-020 and D-021 are accepted. Changes since revision 3: B0 is an operator check, no code
(D-041); D1 graded (`docs/design/harness.md`); B2 has no `messages.ixi` migration (D-027 amended). Each batch = one
PR through the gates. **Scope moves, gates do not.**

## Honest estimate

The original target was 4–6 weeks (D-009). With the review's findings (legacy characterization
before the port, a harness that needs process isolation, four design items, BE on the critical path, CI-only
builds), the full list below takes **about 8 weeks at full pace; about 6 weeks only with the cuts** (D-032). To stay near 6 weeks, cut in this order:
B6 metrics dashboard → push gateway contract → timeouts (ban/kick stay). **This is the only slip rule.** If a batch still slips after the cuts, the remaining refactoring stops and the remaining fixes land in place behind the same tests (option A′); the gates stay.

Per-batch estimates (working days, including CI round trips and the review loop): B0 0 (operator check) · D1 2 · B1a 4 · B1b 4 · D2 2 · D4 2 · B2 4 · B3 4 · B4 3 · D3 1 · B5 4 · B6 3 · B7 5 → **≈ 38 days ≈ 7½–8 weeks at full pace**, fewer with parallel design items. Dates are set when Damir accepts D-032.

**Runtime:** .NET 8 support ends 2026-11-10, inside this plan. B1a runs on .NET 8 by design (legacy characterization). The **old production bot** stays on its current .NET 8 build until it is shut down 2 weeks after cutover (D-038, D-041) — accepted risk.

## Batches

| Batch | Outcome (who can do what, how we know) | Key tests / evidence |
|---|---|---|
| **B0 · Old-bot operator check** (5 min, no code, D-041) | The operator confirms the API is on localhost with a login, nobody opens the admin web page, all message costs are 0, and the bot wallet holds only small change. Not fixed on the old bot (accepted until shutdown): posting as others, forced leave, deletes via an impersonated admin connection. Freeze = an admin posts "we moved" at cutover; the bot stops 2 weeks later. | Check result recorded under BE-02 (`docs/be-asks.md`); steps in `docs/design/b0-check.md`. |
| **D1 · Design: harness** ✅ graded (session 2) | Out-of-process client driver (`simclient`, one member per process, client Core, `--app store\|redesign`), spike first. Graded A 41 · B 31 · C 27. | `docs/design/harness.md`; D-031. |
| **B1a · Characterize legacy** ✅ (session 6, D-050) | Today's behaviour (bot `a5a3442` + Core `f6fb55b` + .NET 8) is recorded as contract scenarios (research D §8 items 1–7). Traffic captures dropped (Core k removed NetDump); wire snapshots (W7) became the B1b wire diff. | 14 cases + 12 self-tests green in CI (D-045, D-047). |
| **B1b · Port** (planned, graded: `docs/design/b1b-port.md`, D-049) | The bot builds on .NET 10 and the pinned Core release tag v0.9.8k (`core.pin`, D-051); every difference from B1a is listed and accepted or fixed. PR A port (CI on Linux + Windows, wire diff vs legacy), PR B cleanup + nightly build against Core HEAD. | B1a suite green on the ported bot; wire diff = expected rows only. |
| **D2 · Design: delivery semantics** | One graded design for: S1 per message class, ack timing (after a durable write), rejected-message rule vs the client cursor, gap-race gating, replies on the requesting connection, first-sync cap N (≤ the client's dedup window, 50) and the unknown-cursor rule. | Graded G1 doc + contract cases. |
| **D4 · Design: cutover, address & leave flow** (moved before B2) | Confirms the new address (D-034) and the "we moved" post; the optional history import; how the pilot runs without two nodes sharing one wallet; reverse export for rollback; who holds the wallet key; leaver handling (`leaveConfirmed` vs gating the leaver's one persistent connection, costed as redial churn × leavers). | Graded G1 doc; rollback drill plan. |
| **B2 · Storage** | No acknowledged message is lost in a crash; no migration (new address, D-034/D-027); optional history import is designed in D4. | Crash injection; properties: gap-free, ordered. |
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
| Who operates production + who holds the wallet key; the 5-minute old-bot check (BE-01…03) | B0 check, D4 | **now** |
| Which Core commit the store app ships with | B1a contract fidelity | before B1a |
| Approval for behaviour changes on the wire (kick/ban semantics, `leaveConfirmed`, first-sync cap) | B3, B5, D4 | at D2/D3/D4 review |
| Production data copy, only if D4 adds the optional history import | D4 import test | before D4 ends |
| Push gateway status (`/v1` vs `/v2`, still accepting bot pushes?) | B6 | before B6 |
| Upstream PR review at each milestone (after B1b, B4, B7) | merge to `ixian-platform` | per milestone |

Not blocking v1 (BE backlog): Core client-transport challenge (T2) · client verification of relayed chat (T1) and bot
actions (T25) · the `getClient` recursion fix · the **reaction cursor bug** (client sets its history cursor to the reaction's *target* id → next sync may replay far back; server mitigation evaluated in D2) · paging/ack/reply-to codes · payments redesign · Core log redaction.
