# Base options — designs, graded (G1)

Session 1, 2026-10-01, **revision 3** after four adversarial review rounds (`docs/reviews/session-1-verdict.md`).
Inputs: `docs/research/A`–`E` + `ERRATA.md`. Rubric: `docs/process.md` §G1. Status: 🟡 recommendation,
awaiting Damir (and BE for the protocol items). Decision row: D-020.
*Session 2:* Damir accepted C (D-020); the switch-over leans to a new address (D-034), so there is no migration; B0 is an operator check, no code (D-041).

## 0 · Facts that constrain every option

Confidence: **V** = verified at source by two readers · **L** = likely (one reader, strong evidence) ·
**H** = hypothesis (needs a build, a run or BE).

| Fact | Conf. | Evidence |
|---|---|---|
| The only implementation of the bot **server** is Spixi-Bot (4,056 C# lines in total). Core holds the shared types, the file stores and the **client** side. | V | C §4 |
| Spixi-Bot does **not compile** against Core HEAD (`BotInfo` ctor, removed `addPendingLocalTransaction`, `ActivityStorage` ctor). | V | review V1 |
| QuIXI is a client node (port 0). It cannot host a channel today. | V | review V2 |
| Core does **not authenticate client connections** (hello signature check commented out). Anyone who knows a member's public key can connect as that member. The public key itself is bound to the address. | V | review V3, V6 |
| The bot does not check `sender` on relayed chat, and clients do not verify relayed chat signatures → any member can post as another. | V | review V4, B T1 |
Signed by clients when sent to a bot: chat, nick {5}, avatar {6}, reaction, delete, `leave`, `requestAdd`/`requestAdd2` {0} and the `kickUser`/`banUser` bot actions. **Not signed: `getInfo`, `botGetMessages`, `getChannels`, `getUsers`, `getGroups`, `getUser`**. Fixed message ids: `nick` {5}, `avatar` {6}, `requestAdd`/`requestAdd2` {0}. (From the review rounds, not an exhaustive audit — D2 starts with a complete per-type table.) | V | review V5, M2; round 2 m-2 |
| Both apps already send **signed `kickUser`/`banUser` bot actions** from their admin menu; the bot ignores them. | V | review M3 |
| Paid messages do not work on either client (`getPayment` is a stub). | V | review V8 |
| `leaveConfirmed` makes the store app crash once, **after** it has deleted the bot. Not sending it leaves the leaver holding one persistent invisible connection (the client redial loop does not open a second one while one exists). | V | review V7, M4; round 2 m-3 |
| Clients keep and retry unsent messages for up to 5 days with the original timestamp, then send once more at expiry. | V | review V10; round 2 n-1 |
| Core is built on process-wide static state → a bot and simulated clients cannot share one process. | V | review M7 |
| The bot acknowledges a message **before** it processes it. | V | A §1a; bot `StreamProcessor.cs:61-85` |
| A reconnecting member can lose the messages sent between connect and its history request (gap race). | H | A §2 (needs a live test) |
| The payment path can crash the bot (null relay list), only while a paid message is pending. | H | B T6 |
| Bus factor 1 upstream (one maintainer for bot, Core, QuIXI). | V | C §6 |

## 1 · The options

| # | Option | What it is |
|---|---|---|
| **A′** | Characterize, then patch in place | Pin today's behaviour with tests first, then fix each defect where it is; extract code only where tests cover it. |
| **B** | Rebuild on QuIXI | Port the server into QuIXI (inbound listener, bot receive path, history, moderation, admin, push); reuse its REST/MQ/wallet API. |
| **C** | Layered rewrite in this repo ("strangler") | Characterize first, then move the server into layers one handler at a time: protocol adapter · domain core · storage · delivery · operations · (later) integration bridge. |
| **E** | New service, new address | Option C built as a fresh bot identity; the legacy bot is frozen; members join the new bot. No data migration. |

Two further candidates were considered and are **not base options**:
- **Core P2P groups (`FriendType.Group`) for closed groups** — a closed-mode strategy, not a base. It is encrypted and
  authenticated, but it relays through the owner's device and has no history for late joiners (members get nothing
  while the owner is offline). Graded later in the closed-mode design; it may replace D-026.
- **Hotfix legacy + shadow-run the new bot** — a rollout strategy. Adopted in part in the cutover design; the B0 code
  hotfix was later dropped (D-041).

## 2 · Grading (1–5, weights ×3 ×2 ×2 ×1 ×1 ×1, max 50)

| Criterion | A′ | B | C | E |
|---|---|---|---|---|
| Correctness & safety (×3) | 3 | 3 | 4 | 4 |
| Member impact (×2) | 5 | 3 | 4 | 2 |
| Maintainability (×2) | 3 | 3 | 4 | 4 |
| Self-hosting (×1) | 2 | 4 | 4 | 4 |
| Delivery speed (×1) | 4 | 1 | 3 | 3 |
| Extensibility (×1) | 2 | 4 | 4 | 4 |
| **Total** | **33** | **30** | **39** | **35** |
| Sensitivity: speed ×2 | 37 | 31 | **42** | 38 |
| Sensitivity: member impact ×3 | 38 | 33 | **43** | 37 |

Harsh notes:
- **A′ vs C differ mainly in the order of work**, not in the first month: both start with characterization and
  the same fixes. C wins on the long run because the fixes land in small, tested layers instead of 800-line handlers.
  If the schedule slips, C degrades gracefully into A′ (stop extracting, keep patching behind tests).
- **B** buys integrations with two projects of work, and its member model (one `Friend` + files per member) does not
  fit a channel of thousands.
- **E** avoids migration risk, but every member must re-join and lose history. Keep it as the fallback if the
  cutover design (below) finds migration unsafe. *(Session 2: E's switch-over is now the plan, D-034.)*
- **Blind second grading** (round-2 reviewer, before reading these scores): A′ 32 · B 27 · **C 40** · E 34. No cell differs by more than 1 point; the winner is the same.

## 3 · Recommendation: C, with its weaknesses fixed

| Weakness of C | Fix |
|---|---|
| Rewrite risk | Characterize the **legacy** build first (B1a), then change it; one handler per PR; recorded traffic replayed against old and new. |
| Schedule | One slip rule, in `docs/roadmap.md`: cut in the listed order, then fall back to A′ for what remains. |
| Unauthenticated client transport (Core) | Authorize on per-message signatures (threat-model S1, per message class); BE ask for Core challenge-response. |
| Drift from Core | Pin the Core commit for merges **and** run a nightly non-blocking build against Core HEAD, so drift is seen early instead of hidden by the pin. |
| Payments | Out of v1 (D-024). |

## 4 · Proposed v1 scope

See `docs/roadmap.md` (revision 3). Design items that must be graded **before** their code: harness isolation,
delivery semantics (with S1 per message class), admin surface, cutover and rollback, `leaveConfirmed`.

## 5 · Success-criteria measurements (honest version)

| Criterion (D-014) | What we measure | Ground truth |
|---|---|---|
| Delivery ≥ 99% | Two numbers, never mixed: **server hand-off rate** (counters) and **delivery** = share of messages that exist in the member's local log. Denominator: every message × every member who connects within 24 h of it; members who stay offline longer are reported separately. | Harness clients compare local log vs bot log; a pilot cohort of instrumented apps does the same. Until clients ack (D-016), launch is judged on the ground-truth numbers only. |
| Join ≤ 2 s | From `requestAdd` sent to "last history item stored + member list ready" at the client, p95. | Harness plus a timed real-app join in the pilot (the app's own per-message disk I/O is part of the cost). |
| Moderation | After a ban: the next post from that address is rejected, it receives no fan-out and no history. | Integration tests. **Limit:** until Core authenticates client connections, a banned person who knows another member's public key can still read by connecting as that member. |
| Zero data loss | *(Session 2: no migration — new address, D-034/D-027; applies to an optional history import only.)* Migration keeps every message id, byte-exact, in order; **no acknowledged message is lost** in a crash (ack only after a durable write). | Migration test on a real copy under access control; crash-injection tests. |
