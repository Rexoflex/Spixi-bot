# Break-my-verdict review — Spixi-Bot rework, session 1

Reviewer: fresh adversarial reviewer (read-only). Date: 2026-10-01.
Scope: `CLAUDE.md`, `DECISIONS.md` (D-001…D-028), `docs/process.md`, `docs/templates/*`,
`docs/examples/README.md`, `docs/lessons.md`, `docs/status-log.md`, `docs/design/base-options.md`,
`docs/threat-model.md`, `docs/test-strategy.md`, `docs/roadmap.md`. The claims were checked against SOURCE:
bot `Spixi-bot/SpixiBot@a5a3442`, Core HEAD `1ff5435`, Core `f6fb55b` (`git show`), QuIXI, Spixi-upstream, and
Spixi-redesign. The research docs were NOT accepted as evidence.

## Verdict: **NOT CLEAN**

| Severity | Count |
|---|---|
| MAJOR | 13 |
| MINOR | 13 |
| NIT | 4 |

The research base is strong, and most headline facts hold at source (see §1). The security control that
everything rests on (S1, "authorship = signature") is **not implementable as written**: honest clients would be
dropped, or looped forever. The admin design ignores a signed admin path that both apps already ship. The
success-criteria proxies can report success for the exact loss mechanisms the research found. The test harness,
the B1 ordering, the pilot and the rollback are not feasible as described. Session 1 also broke its own process:
there is no handoff, and the next-session pointer leads nowhere.

---

## 1 · Load-bearing claims spot-verified at source (10)

| # | Claim (where) | Result | Evidence |
|---|---|---|---|
| V1 | Bot does not compile vs Core HEAD (CLAUDE §4, base §0) | **HOLDS** (all 3 breaks) | Core HEAD `Streaming/Bot/BotInfo.cs:33` has an 11-arg ctor with no `server_name`, and the bot calls the 10-arg form at `SpixiBot/Network/StreamProcessor.cs:589`. HEAD `Transaction/PendingTransactions.cs` has no `addPendingLocalTransaction`/`messageId` (f6fb55b `:24,:42`), and the bot uses them at `StreamProcessor.cs:471`, `Meta/Node.cs:463,621-623`, `SpixiBotTransactionInclusionCallbacks.cs:24-26`. HEAD `Activity/ActivityStorage.cs:958` takes 5 args, and the bot passes 3 (`Meta/Node.cs:235`). (The HEAD `GenericAPIServer.cs:1188` callers sit inside a `/* */` block that opens at `:1155`, so Core itself still builds.) |
| V2 | QuIXI cannot host a channel (base §0) | **HOLDS** (today) | `QuIXI/Meta/Node.cs:191` passes port `0` to `PresenceList.init`. `Config.cs:296` parses `serverPort` but nothing uses it. No `NetworkServer` start. Core `CoreStreamProcessor.cs:764-781` rejects unencrypted non-handshake messages from a **known** non-bot friend (see N1). |
| V3 | Client transport identity unauthenticated (T2) | **HOLDS** | HEAD `Network/CoreNetworkProtocol.cs:238` sets `endpoint.serverPubKey = pubkey`, and `:265-270` only checks `Address(pubkey)==addr`. For `'C'` the signature check is commented out (`:282-293`). |
| V4 | Clients don't verify relayed chat signatures (T1) | **HOLDS** | HEAD `CoreStreamProcessor.cs:966-987`: the verification is commented out, and the message is returned as-is. |
| V5 | Client signs chat to a bot | **HOLDS for chat, FALSE as a generalisation** | `sendSpixiMessage` signs when `friend.bot` (`CoreStreamProcessor.cs:314-318`). But `sendGetMessages` (`:2553-2565`) and `sendGetBotInfo` (`:2567-2584`) are **not signed** (see M2). |
| V6 | Bot can obtain members' pubkeys reliably | **HOLDS** | The hello binds `Address(pubkey)==addr` (V3), so a stored pubkey is correct for its address even under T2. `requestAdd2` also carries the pubkey. |
| V7 | `leaveConfirmed` crashes the store app | **HOLDS, and the consequence is misread** | HEAD `Network/StreamClientManager.cs:118-121` is self-recursive. The same line is in tags v0.9.8, 0.9.8f, 0.9.8i and 0.9.8j (introduced `f279c7a`, 2026-04-05). But `FriendList.removeFriend` (`:429-459`) persists the removal **before** the crash (`CoreStreamProcessor.cs:1405-1408`). See M4. |
| V8 | `getPayment` is a stub in clients | **HOLDS** | `CoreStreamProcessor.cs:2738-2742` (`return;`). |
| V9 | History resend up to 10k | **HOLDS** | `SpixiBot/Messages/Messages.cs:196-219` (`FindLastIndex`, then everything after it), and `Meta/Config.cs:65` = 10000. |
| V10 | Pending client messages keep the original timestamp and retry for days | **HOLDS (new, matters for S1)** | `PendingMessageProcessor.cs:320-333`: an "expired" message is still sent after `onMessageExpired`. `CoreConfig.cs:227` expiry = 5 days. Signature and timestamp are fixed at creation. |

---

## 2 · MAJOR findings

### M1 — S1 ("id not seen, timestamp in window, otherwise drop") breaks honest clients
- **Claim:** threat-model S1 and D-022: every relayed or state-changing message must have a valid signature,
  a timestamp inside the window and an id not seen before; otherwise drop.
- **Why it is wrong:**
  - (a) **Fixed ids.** Clients send state-changing messages with constant ids: `nick` id `{5}`
    (`CoreStreamProcessor.cs:2304`, signed at `:2306-2310`), `requestAdd`/`requestAdd2` id `{0}` (`:2502`, `:2531`),
    and `getInfo` `{11}` (`:2581`). A per-sender "id not seen" cache would drop the second nick change and every
    re-join after a leave. It would also drop the `nick` that every `acceptAddBot` triggers after the first.
  - (b) **Stale but honest timestamps.** Pending messages keep their original signed timestamp. They are retried
    every 2.5 s, paused while the bot is offline, and sent even after the 5-day expiry (V10). After any bot outage
    — for example, **cutover day** — the queued messages arrive "stale". A short window drops them. A window of
    five days or more makes the replay defence depend entirely on a ≥5-day persistent id cache.
  - (c) **"Drop" is undefined with respect to the ack.** If the bot drops without `msgReceived`, the client retries
    forever (research D §7, "Not answering msgReceived"). If it acks and drops, the message is silently lost. The
    client has also already set `lastReceivedMessageIds` to its own message id (`FriendList.cs:343` path), so the
    next reconnect finds no cursor and replays the full history (see M6).
- **Fix:**
  - Rewrite S1 per message class. Use a replay cache on `(sender, id, checksum)`, applied only to random-id types
    (chat, reaction, delete, admin actions). For fixed-id types, require a timestamp that increases per `(sender, type)`.
  - Set the window from measured retry behaviour, not from intuition.
  - Specify ack semantics for every drop reason: **ack-and-ignore** for duplicates, and a documented choice for
    policy rejections.
  - Add these cases as named contract tests (nick change twice, leave then re-join, bot outage of 2 days, then reconnect).

### M2 — The premise "all client→bot traffic is signed" is false. S1/S3 are scoped on it
- **Evidence:** `sendGetMessages` (`CoreStreamProcessor.cs:2553-2565`) and `sendGetBotInfo` (`:2567-2584`) never call
  `sign`. Research D §0 ("All client→bot traffic is `none` and signed") and A §1a are over-generalised.
  Today `getInfo` is **state-changing**: the bot calls `setPubKey` and creates a user record (research B §0.1 #7,
  `StreamProcessor.cs:431-432`).
- **Why it matters:**
  - S1 as written would reject `getInfo`. The whole join and resync cascade would stop.
  - Membership-gated history (S3) can only rest on connection identity for `botGetMessages`, which T2 makes
    spoofable. So "banned/kicked get nothing" is false for any attacker who knows a member's pubkey. The bot
    itself hands pubkeys to anyone (B §1.1).
  - The accepted-risk text in threat-model §5 understates this. It also omits that a T2 impersonator who
    connects first **receives the victim's unicast replies** (history, command replies). That is the first-match
    `forwardMessage(address)`, research B §1.1. This is a targeted delivery DoS that S1 does not stop.
- **Fix:**
  - Correct research D §0 and A §1a.
  - Classify every inbound type as signed or unsigned, with `file:line`. Make unsigned types read-only.
  - Answer requests on the requesting endpoint object, not by an address lookup. Put this in B3.
  - Rewrite threat-model §5 so that it says "a banned member can still read" and "a member's catch-up can be hijacked".

### M3 — The admin design ignores the signed admin path both apps already ship (D-023)
- **Claim:** D-023 and base §4 item 7: admins use signed plain-text chat commands (`/ban` …), "so today's apps work".
- **Evidence:**
  - The store app sends **signed** `kickUser`/`banUser` bot actions from its admin menu:
    `Spixi-upstream/Spixi/Pages/Chat/SingleChatPage.xaml.cs:909-926` (`sendBotAction(..., true)`).
  - The redesign does the same at `SingleChatPage.xaml.cs:2008,2018` and `ContactDetails.xaml.cs:796,812`.
  - Signing happens at `CoreStreamProcessor.cs:2844-2847`. The menu is gated by the bot's `info.admin` flag.
  - The bot ignores both actions today (research D §1b). The protocol path exists and carries a checksummed target
    address, so no one has to type a 50-character address.
- **Text commands add new hazards:**
  - (a) The admin's own `/ban` message becomes the admin's `lastReceivedMessageIds` (`FriendList.cs:343`). If the bot
    does not store it in the channel log, every reconnect replays the full (or capped) history.
  - (b) Any bot→single-member reply (for example, "banned X") is a chat id that the other members' logs do not hold.
    It has the same cursor problem for the recipient.
  - (c) Targeting by nick is ambiguous and spoofable.
- **Fix:** grade three admin surfaces as G1 options: existing bot actions, text commands, and an operator CLI.
  Implement `kickUser`/`banUser` server-side as the primary path. If text commands stay, specify how they are stored
  and how they interact with the cursor.

### M4 — D-025 "never send `leaveConfirmed`" swaps a one-time crash for a permanent ghost
- **Evidence:**
  - On `leaveConfirmed`, the store app runs `FriendList.removeFriend` first (`CoreStreamProcessor.cs:1405`). That call
    deletes the history, the avatar and the friend file (`FriendList.cs:429-459`). Only then does the app recurse into
    the crash (`:1406`). So today: the app crashes once, the leave is complete, and it stays gone after restart.
  - Without `leaveConfirmed`, the store app keeps the friend with `pendingDeletion=true` (`SingleChatPage.xaml.cs:303-307`).
    It hides the friend from the list (`HomePage.xaml.cs:1071`).
  - `connectToBotNodes` keeps redialling **every bot friend every 2.5 s**, with no `pendingDeletion` filter
    (`Spixi-upstream/Spixi/Meta/Node.cs:293-311`). Each connection runs the full `getInfo`→history cascade.
    So every store user who ever left becomes a permanent invisible client. It costs the bot load and the phone battery
    and data, it receives fan-out (unless S3 catches `left`), and the leave never completes.
  - Also, the bot cannot tell a store client from a redesign client. The rule "never to **store** clients"
    (base §4 item 8, roadmap B6) is not implementable. It can only be "never to anyone".
- **Fix:** turn D-025 into a graded decision with the real trade-off. Candidate options:
  - send `leaveConfirmed` (a known single crash, the leave completes);
  - never send it, and reply to `left` addresses with `bye(rejected)` at hello so the redial is cheap;
  - never send it, and ask BE for a hotfix.
  Measure how many `left` users reconnect today.

### M5 — The delivery proxy (base §5, D-016) reports success for the failures the research found
- **Why:**
  - (a) With the history cursor used as a "caught up to" watermark (the D-016 option), the **gap race** (research A §2)
    advances the cursor *past* messages the member never received. The proxy then counts those messages as
    **delivered**.
  - (b) "Handed to a connected member's socket" counts an enqueue to Core's send queue. Core HEAD's bounded channel
    drops the newest message when full and says nothing (research A §1b, `NetworkRemoteEndpoint.cs:171-174,1381-1389`).
    TCP acceptance is not processing on the device either (unknown channel, >64k characters, and dedup all drop on the
    client).
  - (c) The denominator is undefined. Which members? All of them, including dormant members? Only members who
    connect within 24 h? Is a member offline for 25 h counted?
  - (d) "From aggregate counters" still needs per-member, per-message state to compute. That state is not designed,
    and it touches the privacy rule D-012.
- **Owner impact:** "delivery ≥ 99%" is a ✅ locked launch gate (D-014), measured by a 🟡 proxy that can pass while
  members see missing messages.
- **Fix:**
  - Define numerator, denominator and window exactly.
  - Name the proxy "server-side hand-off rate" and never call it delivery.
  - Add a separate **ground-truth** measurement: harness clients plus a pilot cohort of instrumented real apps that
    compare their local log against the bot log.
  - Keep D-016 open. Do not let D-014 be reported against the proxy.

### M6 — v1 scope omits the known loss mechanisms and adds new ones
- **Omitted:**
  - (a) **Ack before processing** (research A §1a, `SpixiBot/Network/StreamProcessor.cs:62-83`). This is *the*
    silent-loss path. "A crash loses at most the last message" (D-014) still loses a message the client was told was
    delivered. The criterion must be "a crash loses no acked message", with the ack sent after a durable append.
  - (b) The **gap race** (A §2). A server-only fix is possible (hold live fan-out to a connection until its catch-up
    has been served). It is not in the scope list (base §4 item 4) or in B3.
  - (c) Replying on the requesting endpoint (M2).
- **New drop paths:** v1 adds S1 failures, size caps, rate limits and bans. Each one creates an own-message id that
  the bot does not store. That id becomes the client's cursor, and it triggers a full replay (M1c). No batch designs
  the combined rule of ack, cursor and storage for rejected messages.
- **Fix:** add a "Delivery semantics" design item (G1, three options) before B3. It must cover ack timing, rejected
  messages, the cursor policy for an unknown cursor, and fan-out gating during catch-up.

### M7 — The test harness is not feasible as described (test-strategy §3)
- **Evidence:**
  - Core is built on process-wide static state: `IxianHandler` is a `static class` (`Meta/IxianNode.cs:114`),
    `FriendList.friends` is static (`Streaming/Friends/FriendList.cs:26`), and `CoreStreamProcessor` holds static
    processors and capabilities (`:73-74`). A bot and N "simulated clients built on Core's client stream code" cannot
    share one process.
  - The 1,000-client soak and the multi-client contract tests need one process per client, or AssemblyLoadContext
    isolation with separate Core builds. Neither is designed or estimated.
  - The store-vs-redesign differences the contract must pin are mostly **app-layer**, not Core: the leave flow
    (`pendingDeletion` in U `SingleChatPage.xaml.cs:303-307`; immediate `removeFriend` in R), the channel-0 vs resolver
    UI, R's author-restore workaround, and R's `messagesToLoad=50`. A Core-only simulated client does not run them.
  - The store app's Core commit is itself a hypothesis (research D, BE Q1).
- **Fix:**
  - Write a harness design doc with three options: out-of-process clients, ALC isolation, or a protocol-level
    replayer built from recorded traffic. Estimate each.
  - State which app-layer behaviours are re-implemented and how their fidelity is proven.
  - Get the store's Core commit from BE before calling anything a "store contract test".

### M8 — B1 changes the system before it pins it
- **Claim:** B1 = harness + .NET 10 + Core HEAD + characterisation tests, "today's behaviour is pinned".
- **Why it is wrong:**
  - Today's behaviour is bot `a5a3442` + Core `f6fb55b` + .NET 8.
  - Core HEAD changes behaviour, not just signatures: the send throttle is gone, queue overflow is drop-newest instead
    of `RemoveAt(10)`, and a missing `serverName` produces an empty bot nick (research A §1b, D §0).
  - Characterisation tests written after the port pin the port, not production. That breaks test-strategy §1
    ("Characterize before changing") and D-008 ("behind the new test harness").
- **Fix:**
  - Split B1: B1a records traffic and snapshots from the legacy build (.NET 8, `f6fb55b`). B1b ports to Core HEAD and
    .NET 10 and diffs against B1a.
  - Record the Core pin strategy (submodule or tag) and the rule for choosing the pin as a DECISIONS row.
  - Explain why HEAD is chosen over the app-matching j/k.

### M9 — The pilot and the rollback cannot work as written (D-015, roadmap B7, base §4 item 10)
- **Why:**
  - (a) A "small group of real users on a copy of production data" **on the same bot address** means two nodes with
    one wallet and two presences. Clients connect to whichever presence they resolve. On a **different address**,
    members must re-join, local history and cursors are keyed per bot address, and the copy's history ids collide.
    Neither path is designed.
  - (b) Rollback after cutover: the new bot writes RocksDB. Rolling back to the legacy bot loses every message posted
    after cutover unless there is a reverse export. That export is not planned.
  - (c) "Scrubbed" production data (addresses and content replaced) breaks every stored signature. The migration test
    then cannot prove that the stored originals stay byte-exact, and clients reject nick and avatar blobs whose
    signatures no longer verify.
  - (d) Access to production data and custody of the bot wallet depend on the operator. D-002 leaves the operator
    open, which means a hidden BE dependency.
- **Fix:** a cutover design doc covering the address strategy, the reverse migration or dual-write window,
  byte-exact migration tests on a real copy under access control, and who holds the key.

### M10 — Base-option grading: A is a strawman and different options are missing
- **A is a strawman.** A = "add tests around the patched code" (tests *after*), and it scores Maintainability 2 and
  Correctness 2. A reasonable A′ ("characterise first, then patch in place, extract only where tested") is most of C
  minus the re-layering. It would plausibly score within a few points of C with faster delivery. A and C differ mainly
  in the order of work, and the rubric does not show that.
- **Missing option 1:** use Core's **P2P `FriendType.Group`** model for closed groups (research C §2:
  encrypted S2, authenticated keys, owner relay, `TestGroupChat.cs` with 29 tests) and limit the bot to public
  channels. That removes the need for D-026 "closed mode waits for T2". It is a real scope and strategy fork, and no
  option explores it.
- **Missing option 2:** D-004 explicitly allows **a new bot address**, yet all three options keep the address and the
  protocol. A "new service/new address, legacy frozen, members migrate" option was never graded.
- **Missing option 3:** "hotfix legacy + shadow-run new". It is also missing.
- **Rigour:** one author assigned the scores. There is no sensitivity check (does C still win if Delivery speed is
  ×2?), and no second grader.
- **C's own argument conflicts with itself.** Its maintainability score cites "drift caught by CI against a pinned
  Core". Pinning is what let the current bot drift silently (it effectively pinned `f6fb55b` by sibling checkout).
  Pinning hides drift; scheduled builds against Core HEAD detect it.
- **Fix:** add A′ and the Group/new-address option. Have a second grader score blind. Show the sensitivity analysis.
  Add a "track Core HEAD nightly (non-blocking)" CI job next to the pin.

### M11 — Live CRITICALs in production get no hotfix track
- **Evidence:** research B rates these CRITICAL and live now:
  - T3: admin-UI stored XSS → theft of the hot wallet (`settings.js:151`);
  - T4: CSRF on an unauthenticated wallet API by default;
  - T1: impersonation of any member, including the owner.
  The roadmap reaches them in B4 (week 3), B5 (week 3–4) and B6 (week 4–5).
- **Fix:** add a B0 "legacy hotfix" track that ships in days on the running .NET 8/`f6fb55b` build:
  - set API auth and bind the API to loopback;
  - disable or remove the admin UI;
  - make the bot-side `sender == endpoint.presence.wallet` check (a few lines, no client change);
  - set cost to 0 (T6 is only reachable when a paid message is pending: `StreamProcessor.cs:453-459`).
  This buys safety while the rework follows its gates.

### M12 — The 4–6 week roadmap is not realistic, and BE sits on the critical path
- **No local build or test in the working environment** (D-019). Every red-green cycle and every mutation run is a CI
  round trip. The roadmap does not budget for it.
- **The loop cost is not budgeted.** Seven batches each carry the #46 loop (3–4 rounds in Spixi), mutation testing,
  and fuzzing "before a release".
- **B1 alone** holds harness isolation (M7), legacy characterisation (M8), the Core port, .NET 10 and CI. That is not
  one week.
- **"BE asks … none blocks v1" is contradicted by the plan itself:**
  - CLAUDE §2 rule 6 requires BE approval for protocol-behaviour changes. v1 stops `leaveConfirmed` and gives
    `kick`/`ban` semantics.
  - D-011 routes every milestone through an upstream PR that BE reviews.
  - The store Core version (M7), production data and wallet custody (M9), and the push gateway `v1`/`v2` status are
    all BE-held.
- **Fix:** put a critical-path table with BE dependencies and dates into the roadmap, and re-estimate B1. Say plainly
  what is cut first if week 3 slips.

### M13 — Session 1 did not export its own win; the next session has no entry point
- **Evidence:**
  - CLAUDE §6: "Next session: read the newest `docs/handoff-*.md` first". No handoff file exists (`ls docs/handoff*`
    → none). process.md §E and the template require one.
  - CLAUDE §4 lists a "review verdict" template. `docs/templates/` has no verdict template, only the rubric, handoff,
    review brief and session prompt.
  - None of the work is committed (`git status`: `CLAUDE.md`, `DECISIONS.md` and `docs/` are untracked).
  - The status log does not record any of this.
- **Fix:** write `docs/handoff-2026-10-01.md` with the open questions from this review. Add the missing template or
  remove the reference. Have Damir commit.

---

## 3 · MINOR findings

| # | Claim | Why it is wrong or weak | Evidence | Fix |
|---|---|---|---|---|
| m1 | "History cursor fix (reaction/delete ids)" is a server-only v1 item (base §4.4, B3) | Delete is not a bug: the cursor moves to the tombstone id, which the bot stores, and only when the delete succeeds. The reaction bug is client-side (the cursor is set to the *target* id). No server-only fix is designed, and the one likely server fix (reordering the replay tail) is only partial. | `CoreStreamProcessor.cs:1665-1675`, `:1686-1690` | Drop "delete". Design the reaction case, or file it as a BE ask. |
| m2 | "Capped first sync is server-only and safe" | Mostly safe for `null` cursors: out-of-window reaction targets fail `addReaction`, so the cursor does not regress. But N is undefined. It must be ≤ the client's in-memory dedup window (50 in R, 100 by default), or later replays duplicate. The **unknown-cursor** case (a returning member) is unspecified: capping it silently loses the gap, and not capping keeps the 10k replay. | research D §4; `FriendList.cs:252-262` | Fix N ≤ 50. Write the unknown-cursor rule as a decision. |
| m3 | Research is "source-verified" (CLAUDE §4, lessons L12) | Errors exist: B §0 cites `SpixiBotTransactionInclusionCallbacks.cs:533-535`, but the file has 52 lines (real `:24-26`). D §0 "all signed" is wrong (M2). Base §0 says "~1.5 kLOC server logic", but C says ~4 kLOC (`find SpixiBot -name '*.cs' \| xargs wc -l` = 4056). | as cited | Add a process step: every research claim cited in a DECISIONS row is re-read at source by someone other than its author. |
| m4 | S6 / D-012: "no address in logs" is a bot change | Core itself logs addresses and IPs at INFO: `CoreNetworkProtocol.cs:1201,1208`, the bye paths in `NetworkServer.cs` (`getFullAddress()`), and "forward to everyone". IP addresses are not mentioned in S6 at all. | as cited | Add a log-redaction sink, or a Core change as a BE ask. Add IPs to S6. |
| m5 | Decision-log hygiene | D-016 still says "decide in the base design" (🟡), but base §5 decided the proxy without updating the row. D-014 is ✅ locked while its measurement is 🟡. | DECISIONS.md | Mark D-014 "locked target, provisional measurement", and update D-016. |
| m6 | Mutation "blocks merge" | test-strategy §4 and process §G2 make Stryker a blocking gate. D-028 says it gets a trial run before it becomes one. | — | Choose one; align all three documents. |
| m7 | "Join ≤ 2 s" measurement | It is measured on a simulated client, not the app. Research A §3 names the client's per-message `saveMetaData` I/O as a dominant cost. "First history batch" is gameable with a small N. The roster/`getUser` storm is excluded. | research A §3 | Define the end as "last history item persisted + roster ready". Add a real-app timing on the pilot. |
| m8 | "Moderation takes effect on the next message" | This is true for posting under the banned address. A banned user can keep reading by impersonating a member connection (T2 + M2). | — | Say so in the criterion text for Damir. |
| m9 | One-command container deploy (D-002) | The wallet password is typed at the console (research B §5, `Meta/Node.cs:107-229`). A non-interactive secret design (injection, rotation, hot-wallet exposure) is not in the threat model or the roadmap. | — | Add a threat-model row and a B6 item. |
| m10 | Process completeness vs a senior team | No definition of ready. No PR size limit. No branch protection on the fork (who can merge?). No versioning or release/tag policy (the legacy uses `xsbc-x.y.z`). No incident runbook or on-call for the launch week. DECISIONS rows have no alternatives or consequences fields. There is no security-handover gate before the upstream PR (the Spixi project needed one). The #46 loop is orchestrated and adjudicated by the authoring session (D-010), although Spixi learned to run it in a separate session. | process.md | Add these as short sections or template fields. |
| m11 | S9 "never to store clients" | The bot cannot distinguish store from redesign clients. | — | The rule is "to no one" (and see M4). |
| m12 | Snapshot tests "for both Core versions the apps ship" | The bot's wire bytes come from the *bot's* Core, not the apps'. Compatibility is about the apps' *readers*. | test-strategy §2 | Snapshot the bot's output once; run the reader tests against the app Core versions. |
| m13 | Base §0 is titled "Facts" | It mixes verified facts with hypotheses. B marks the payment crash T6 as HYPOTHESIS, the store Core version is a hypothesis, and so is the per-IP guard. The owner cannot tell them apart. | base-options §0 | Add a confidence column (VERIFIED / LIKELY / HYPOTHESIS). |

## 4 · NITs

| # | Note | Evidence |
|---|---|---|
| N1 | Research C's "Core rejects bot traffic" is only true for a *known* non-bot friend. The check is inside `if (friend != null)`. | `CoreStreamProcessor.cs:764-781` |
| N2 | The FORGE source citation (a YouTube id) cannot be verified and is not load-bearing. | process.md header |
| N3 | "One upstream PR per milestone": milestone is not defined. | D-011 |
| N4 | T6 (the payment crash) needs a pending paid message (`StreamProcessor.cs:453-459`). A free official channel cannot trigger it. That lowers its urgency; say so. | — |

---

## 5 · Answers to the specific questions

1. **S1 without client changes?** Signature, pubkey and the sender binding are all available: the chat is signed
   (V5), and the pubkey is bound to the address at hello (V6). No honest client relays through a third party: bot
   rooms are not `FriendType.Group`, and pending messages go to `relayNode` = the bot. Nick, avatar and reactions are
   signed via `sendSpixiMessage`. **But S1 as written drops honest clients** (M1: fixed ids, stale retries, the
   undefined ack). It also cannot apply to unsigned `getInfo`/`botGetMessages` (M2). Implementable after M1/M2 are fixed.
2. **Capped first sync:** safe for new joiners with N ≤ 50. The returning-member / unknown-cursor case is unspecified
   (m2).
3. **Signed in-chat admin commands "work with today's apps":** they send and are signed. But they poison the command
   author's cursor, and they duplicate a signed admin path the apps already have (M3).
4. **Grading:** the totals add up (28/30/39), but see M10.
5. **Roadmap:** see M8, M11, M12. The .NET 10 port is misplaced relative to characterisation.

## 6 · Top 5 fixes, in priority order

1. **Add a B0 legacy hotfix** (API auth + loopback, admin UI off, the bot-side `sender == endpoint` check). Production
   is exposed today (M11).
2. **Rewrite S1 and write the delivery-semantics design** as one graded G1 item before any B3/B4 code: the per-class
   replay rules, ack after a durable append, the rule for rejected messages vs the cursor, gap-race fan-out gating,
   and replies on the requesting endpoint (M1, M2, M6).
3. **Replace text-command admin with the existing signed `kickUser`/`banUser` bot actions**, and re-decide D-025 with
   the real trade-off (M3, M4).
4. **Split B1:** characterise the legacy build first, design the harness isolation and app-layer fidelity, and get the
   store Core commit from BE. Then re-estimate the roadmap, with BE on the critical path (M7, M8, M12).
5. **Make the success criteria honest:** separate "server hand-off rate" from delivery ground truth, fix denominators,
   change the crash criterion to "no acked message lost", and design the cutover and rollback (address strategy,
   reverse migration). Write the handoff (M5, M9, M13).
