# BE asks — questions for the Ixian BE engineer

Session 2, 2026-10-01 (D-039). One list, grouped by **the batch each answer blocks**, most urgent first. Sources:
research A–D open questions and the roadmap "BE critical path"; questions made moot by later decisions are listed
at the end so nobody asks them again. Each answer that is a decision becomes a `DECISIONS.md` row.

Status: ⬜ not sent · 📤 sent (date) · ✅ answered (row / doc).

**Deferred (D-042, 2026-10-01):** the BE engineer is busy. Nothing is sent now. The asks go out as one batch with the
first upstream PR (after B1b), or earlier only if a batch is truly blocked. Until then each batch uses the stated
assumption (e.g. store Core = k, harness W3) and marks it as a hypothesis.

## 0 · Now — ready to paste (blocks: the B0 check, D-041)

> Hi — three things for the old Spixi bot (the community channel), before we start the rework:
>
> 1. **Who runs the production bot, and who holds its wallet key?** We plan you as operator.
> 2. **A 10-minute check on that server** — please follow `docs/design/b0-check.md` §2 exactly (it has backups and
>    two password traps). Short version:
>    - `ixian.cfg`: no `apiBind` / `apiAllowIp` except localhost; add `addApiUser = user:password` (no `:` or `=` in
>      the password; the bot logs config lines at start, so clean the log) and `disableWebStart = 1`; restart; check that the API answers 401 without the login.
>    - Please **do not open the bot's admin web page** until it is shut down — it has a stored-XSS bug through member
>      nicks that runs with your login. Use `curl` instead.
>    - Set every group's message cost to 0 (`sb_getGroups` / `sb_updateGroup` via curl).
>    - Move the bot wallet's IXI, except small change, to a cold wallet.
> 3. **Which exact build runs in production?** Bot commit (we assume `a5a3442`) and Ixian-Core commit (we assume
>    `f6fb55b`).
>
> Context: we are not patching the old bot. At cutover an admin posts "we moved" and the old bot stops 2 weeks later.

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-01 | Operator and wallet-key holder for production | Someone must run the check and the shutdown | B0 check, D4 | D-002, roadmap | ⬜ |
| BE-02 | The check above (`b0-check.md` §2) | The API can spend, sign and export the wallet; reachable remotely (if bound publicly), by local code (no login by default) and via the admin page's stored XSS | B0 check | D-041 | ⬜ |
| BE-03 | Production bot + Core commits | Characterization (B1a) must match what members use today | B0 check, B1a | roadmap | ⬜ |

## 1 · Before B1a / B1b (characterize legacy, then port)

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-04 | Which Ixian-Core commit is in **store Spixi 0.9.22**? (Our guess: xcore-0.9.8j, `95fc725`.) Is there a build record? | The harness builds a store-mode client with that Core (harness W3) | B1a store fidelity | D §9 Q1 | ⬜ |
| BE-05 | Is there an unpushed Spixi-Bot branch already on Core ≥ 2026-05 (the `BotInfo` ctor change)? | Avoids duplicate porting work in B1b | B1b | C §8 Q2 | ⬜ |
| BE-06 | Core pin rule: which **release tag** do both apps build against, and will you tag releases we can pin? | D-030 pins merges to that tag | B1b | D-030 | ⬜ |
| BE-07 | Can a bot serve clients with **no reachable DLT seed** (testnet, isolated)? Anything that waits on TIV/sync first? | The test harness runs the bot isolated (harness F4) | B1a spike | harness.md | ⬜ |

## 2 · Before D2 (delivery semantics) → B3

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-08 | Approval to cap the first sync at N messages (N ≤ the client's dedup window, 50) | Today a new member gets up to 10 000 messages, one frame each | D2, B3 | roadmap | ⬜ |
| BE-09 | Should `botGetMessages` carry a sequence/timestamp instead of the last id? The own-message / reaction-target cursor plus `FindLastIndex = -1` replays the whole channel. | Gap race and full replays | D2 (later protocol change) | D §9 Q6, A Q3 | ⬜ |
| BE-10 | **Reaction cursor bug** (client sets the cursor to the reaction's target id): will the client fix ship, or must the bot mitigate? | Next sync may replay far back | D2 | roadmap | ⬜ |
| BE-11 | `FriendType.Normal` author nulling in Core k (`5643e5b`): will `&& !friend.bot` land? Should the new bot re-serve history to heal rows stored without an author? | Redesign stores no author for bot rooms | D2 | D §9 Q7 | ⬜ |
| BE-12 | Agreed freshness window for `StreamMessage.timestamp`? Any per-sender id cache in Core to reuse? | Replay rules for fixed-id types (S1) | D2, B4 | B §10 Q10 | ⬜ |
| BE-13 | Protocol-level size limits per message type, or must the bot enforce its own? | S4 hard limits | D2, B4 | B §10 Q9 | ⬜ |
| BE-14 | Roster cap 500 + one `getUser` per unknown sender: OK to add a batched `getUsers(addresses[])` action later? | Join cost in big rooms | D2 | D §9 Q8 | ⬜ |
| BE-15 | Mobile: does iOS/Android keep the bot TCP connection in the background, or is every foreground a full reconnect + catch-up? | Sizes catch-up load and the join target | D2, B3 | A Q9 | ⬜ |
| BE-16 | Expected scale of the official channel (members, messages/day)? | Soak and storage sizing | B3, B7 | C §8 Q4 | ⬜ |

## 3 · Before D4 (cutover, new address, leave flow)

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-17 | **Deep link** for one-tap join: who adds a URL scheme to Spixi (none today) → pre-filled add-contact with a confirm? Timeline? | D-034 mitigation for the new address | D4, B7 | D-034 | ⬜ |
| BE-18 | Do the apps accept history messages that name the **old** bot as recipient if served by the new bot? | Optional history import (D-027 amended) | D4 | D-034 | ⬜ |
| BE-19 | `StreamClientManager.getClient` recursion: can the one-line fix ship? Until then, send `leaveConfirmed` (one store-app crash after leave) or not? | D-025 leave flow | D4 | D §9 Q2 | ⬜ |
| BE-20 | Who holds the **new** bot's wallet key, and on which host does it run? | Operations and rollback | D4, B6 | roadmap | ⬜ |
| BE-21 | Should `NetworkServer` reject or replace a second connection with the same wallet? Is the `IPAddress ==` reference comparison (`NetworkServer.cs:559 @f6fb55b`) known? | Hijacked catch-up, per-IP guard | D4, B4 | B §10 Q4, A Q4 | ⬜ |
| BE-31 | Production access to **count leavers that stay connected** (store app deletes the bot before `leaveConfirmed`) | Costs the leave options in D-025 | D4 | D-025 | ⬜ |
| BE-32 | A **copy of the old bot's data** (`messages.ixi`, `contacts.dat`), access-controlled — only if D4 adds the optional history import | Import test | D4 | roadmap, D-027 | ⬜ |

## 4 · Before B4 (identity & limits)

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-22 | 'C' hello has no signature/challenge check (`processHelloMessageV6`). Deliberate? Can a challenge over a server nonce be added? What breaks? | T2: anyone with a member's pubkey can connect as that member | B4 (accepted risk until fixed), closed mode | B §10 Q1 | ⬜ |
| BE-23 | Why was client verification of relayed chat disabled (`d007cb3`)? OK to verify against the roster pubkey with a pending queue? | T1 at the client | later (client) | B §10 Q2 | ⬜ |
| BE-24 | Can anything other than the direct bot connection deliver `sender = bot` messages to a client? Should the bot sign its botActions? | T25 forged bot actions | B4 | B §10 Q3, D §9 Q3 | ⬜ |
| BE-25 | `acceptAddBot` is accepted from any friend with `handshakeStatus ≤ 1`, unsigned. Intended? | Trust model | B4 | D §9 Q4 | ⬜ |

## 5 · Before D3 / B5 (admin & moderation)

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-26 | Approval for server-side `kickUser`/`banUser` semantics for banned/kicked/left members (block post, fan-out, history). What should the client show? (Its handlers are no-ops, `CSP:2726-2732`.) Are `getGroups` and `msgReport` meant to be implemented server-side too? | Apps already send them | D3, B5 | B §10 Q12, D §9 Q10, C §8 Q5, A Q10 | ⬜ |
| BE-27 | Should the bot honour `enableNotifications` (forced false today)? | Mute state and badges | B5/B6 | D §9 Q9 | ⬜ |

## 6 · Before B6 (operations)

| # | Ask | Why | Blocks | Source | Status |
|---|---|---|---|---|---|
| BE-28 | Push gateway: `/v1` vs `/v2`, still accepting bot pushes, `fa=""` handling, what it logs (tag = raw address?) | D-017 push design, S6 | B6 | B §10 Q7, A Q6 | ⬜ |
| BE-29 | Core logs addresses and IPs at info level: OK to add redaction in Core, or should the bot filter? | S6 | B6 | threat model S6 | ⬜ |

## 7 · Milestones

| # | Ask | Blocks | Status |
|---|---|---|---|
| BE-30 | Review the upstream PR after B1b, B4 and B7 | merge to `ixian-platform` | ⬜ |

## 8 · Later — not blocking v1 (send as FYI)

Batched delivery acks (D-016) · history paging / ack / reply-to codes (roadmap backlog) · Payments redesign (B §10 Q5, A Q7, D §9 Q5) · E2E / group keys for bot channels (B §10 Q6, C §8 Q3) · blind mode via
derived addresses (B §10 Q8) · Core `GroupChat` / P2P groups vs bot channels (C §8 Q1, Q9; D §9 Q12) · QuIXI MQ into
Core (C §8 Q6) · separating the bot's spending key from the API process (B §10 Q11) · moving to Core HEAD throttle
semantics (A Q8, handled by D-030).

## 8b · We measure these ourselves (no BE ask)

| Research question | Where |
|---|---|
| Timed join trace (A Q1) | B1a characterization, B3 join p95 |
| Reproduce the gap race (A Q2) | B1a, D2 contract case |
| Connection ceiling / 10k broadcast cost (A Q5) | B7 soak |

## 9 · Moot — do not ask

| Question | Why moot |
|---|---|
| Keep the same bot address/IP? (C §8 Q7) | New address is the plan (D-034, assumed by D-041); ask again only if D4 reverses it |
| Keep paid messages? (C §8 Q10, D §9 Q5 first half) | Disabled in v1 (D-024, 🟡); the redesign question stays in §8 |
| `getUser` NRE for unknown addresses (D §9 Q11) | We fix it in the rework (B4) |
| Who owns CI? (C §8 Q8) | Our fork's GitHub Actions run our gates (D-019, D-030); upstream CI is the BE's — FYI only |
