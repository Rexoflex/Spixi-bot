# Break-my-verdict review, round 2 — Spixi-Bot rework, session 1 (over the rev-2 fixes)

Reviewer: fresh adversarial reviewer (read-only), 2026-10-01. Scope: the rev-2 revisions listed in the brief.
Source used for verification: bot `SpixiBot@a5a3442`, Core HEAD `1ff5435` and `f6fb55b` (`git show`),
Spixi-upstream, Spixi-redesign. Research reports were not accepted as evidence.

## Verdict: **NOT CLEAN** — 1 new MAJOR (the B0 hotfix does not close T1, but the plan says it does)

| | Count |
|---|---|
| Round-1 MAJOR remaining (not resolved and not properly scheduled) | **0** (10 resolved, 3 partially resolved with MINOR residuals) |
| New MAJOR | **1** |
| MINOR (new + residual) | 12 |
| NIT | 8 |

The revision is good work. Every round-1 MAJOR is either fixed or turned into a graded design item (D1–D4) that is
ordered before the code that depends on it. One new MAJOR remains: the B0 hotfix copies round 1's suggested
`sender == connection` check, and that check alone does not stop impersonation (see N-M1). This is a one-clause fix.

---

## PART 1 — Blind grading (done before reading §2)

Inputs read: base-options §0, §1 and the rubric in process §G1 only.

| Criterion (weight) | A′ | B | C | E | Reasons (one line each) |
|---|---|---|---|---|---|
| Correctness & safety (×3) | 3 | 2 | 4 | 4 | A′: fixes land inside 800-line handlers, so risk stays high. B: large port onto a client node with no inbound path, so a new attack surface. C/E: small tested layers; S1 in one adapter. |
| Member impact (×2) | 5 | 3 | 5 | 2 | A′/C: same address, same data, no rejoin. B: identity kept only if the wallet and the data move. E: every member re-joins and loses history. |
| Maintainability (×2) | 2 | 3 | 4 | 4 | A′: the legacy structure stays. B: fits a maintained stack, but that stack's member model is per-`Friend` and bus factor 1. C/E: layers plus tests. |
| Self-hosting (×1) | 3 | 4 | 3 | 3 | B: has REST/MQ/packaging already. Others: packaging is future work either way. |
| Delivery speed (×1) | 4 | 1 | 3 | 3 | A′ fastest; B is two projects of work. |
| Extensibility (×1) | 2 | 4 | 4 | 4 | A′ has no seams. |
| **Total (max 50)** | **32** | **27** | **40** | **34** | |

Comparison with the author's §2 (A′ 33 · B 30 · C 39 · E 35): **no disagreement is larger than 1 point on any cell**
(differences of exactly 1: A′ maintainability 2 vs 3 and self-hosting 3 vs 2; B correctness 2 vs 3; C member impact
5 vs 4 and self-hosting 3 vs 4; E self-hosting 3 vs 4). **The winner does not change: C.** The margin over the
runner-up is 6 points here vs 4 in §2. I re-checked the author's arithmetic and both sensitivity rows (speed ×2:
37/31/42/38; member impact ×3: 38/33/43/37): all correct.

One note on the author's scores: A′ self-hosting 2 vs C 4 is not justified by §1. Both options ship the same config
and packaging work. The number does not change the result.

---

## PART 2 — Round-1 MAJOR status

| # | Status | Evidence (revised doc) | Residual |
|---|---|---|---|
| M1 S1 drops honest clients | **RESOLVED** (scheduled) | threat-model.md:30 (per-class replay rules, ack-and-ignore for duplicates); D-022 (DECISIONS.md:29); roadmap D2 (roadmap.md:21) is before B2/B3/B4; the named honest-client cases are in B4 (roadmap.md:24) and in test-strategy.md:20. | The fixed-id list omits `avatar` id `{6}` (MINOR m-2). |
| M2 "all traffic is signed" | **RESOLVED** (rule), enumeration **PARTIAL** | S1b (threat-model.md:31): unsigned → read-only, replies go on the requesting connection; accepted risk rewritten (threat-model.md:67-71); ERRATA.md:8. | The "classify every inbound type with `file:line`" step is not done. Four more unsigned types exist (MINOR m-2). |
| M3 admin ignores signed bot actions | **RESOLVED** (scheduled) | D-023 (DECISIONS.md:30); D3 (roadmap.md:25) is before B5. Verified: both apps call `sendBotAction(…, true)` (upstream `SingleChatPage.xaml.cs:912,922`; redesign `:2008,2018`, `ContactDetails.xaml.cs:796,812`). | — |
| M4 `leaveConfirmed` ghost | **RESOLVED** (scheduled) | D-025 (DECISIONS.md:32), S9 (threat-model.md:39), D4 (roadmap.md:27), with the options and the measurement request. | The mechanism is mis-stated, and one D4 option is not "cheap" (MINOR m-3). |
| M5 delivery proxy | **RESOLVED** | base-options §5 (numerator, denominator and window defined; hand-off rate named separately); D-014 (DECISIONS.md:21); D-016 (DECISIONS.md:23). | The pilot needs instrumented app builds, which are not on any critical path (MINOR m-11). |
| M6 loss mechanisms not in scope | **RESOLVED** (scheduled) | D-014 "no acknowledged message is lost"; D2 covers ack timing, rejected messages vs the cursor, gap-race gating and requesting-connection replies; B3 tests (roadmap.md:23). | — |
| M7 harness infeasible | **RESOLVED** (scheduled) | D-031; test-strategy §3 (three options, app-layer rules cite app `file:line`); D1 is before B1a; the store Core commit is asked before B1a (roadmap.md:38). Static state verified: `IxianNode.cs:114` `static class IxianHandler`, `FriendList.cs:26`, `CoreStreamProcessor.cs:73-74`. | The contract row (test-strategy.md:20) already names out-of-process clients before D1 is graded (NIT). |
| M8 B1 changes before it pins | **PARTIAL** — the core defect is fixed | B1a/B1b split (roadmap.md:19-20); D-030. | The rule for choosing the pin (HEAD vs the app-matching tags j/k, and submodule vs tag) is still not recorded. There is no CI job for the legacy .NET 8 build that B0/B1a need (MINOR m-7, m-8). |
| M9 pilot/rollback infeasible | **RESOLVED** (scheduled) | D4 (roadmap.md:27): address strategy, no shared wallet, reverse export, key custody; test-strategy.md:43 (a real copy, not scrubbed). | D4 is ordered too late relative to B2/B3/B5 (MINOR m-4). D-015 is still ✅ locked unchanged (MINOR m-5). |
| M10 strawman / missing options | **RESOLVED** | base-options §1 (A′, E, and the two non-base candidates with reasons); sensitivity rows; the nightly Core HEAD build (D-030, test-strategy.md:54); this blind grading. | — |
| M11 no hotfix track | **RESOLVED as a track**, **but its content is wrong** | B0 (roadmap.md:17), D-029, threat-model.md:47. | See **N-M1**: the listed chat control does not stop T1. |
| M12 unrealistic roadmap | **PARTIAL** | Honest range 6–8 weeks, a cut order, a BE critical-path table (roadmap.md:6-11, 34-45); D-032. | No per-batch estimates, no dates ("ask by" is relative), no budget for CI round trips or loop cost. The slip strategies disagree, and the cut order names items that are in no batch (MINOR m-9). |
| M13 no handoff | **RESOLVED** | `docs/handoff-2026-10-01.md`, `docs/templates/review-verdict.md`, status-log entry, CLAUDE §6 pointer. The commit is Damir's. | Four docs point to a verdict file that does not exist (MINOR m-1). |

---

## PART 3 — New defects introduced by the revisions

### N-M1 (MAJOR, new) — B0's "bot-side `sender == connection` check" does not stop impersonation, and the plan says production stops being exposed

- **Claim:** roadmap.md:17 says "Production stops being exposed … bot-side `sender == connection` check on relayed chat".
  D-029 (DECISIONS.md:36) says the same, and threat-model.md:47 lists B0 as the control for T1 "live in production today".
- **Mechanism:** under T2, an attacker connects as the victim (the hello only checks `Address(pubkey) == addr`; the
  signature check is commented out, Core `CoreNetworkProtocol.cs:265-293`). Then `endpoint.presence.wallet` **is**
  the victim, so `sender == connection` passes, and the legacy bot relays the forged chat. The attacker needs only
  the victim's public key, and the legacy bot hands every member's public key to anyone who asks:
  `sendUsers` → `sendUser(…, bc.getBytes(false))` (bot `Network/StreamProcessor.cs:533-553`), and
  `BotContact.getBytes` writes `publicKey` (Core `f6fb55b` `Streaming/Bot/Users/BotContact.cs:141+`).
  Clients do not verify relayed signatures (Core HEAD `CoreStreamProcessor.cs:966-987`). So after B0, anyone can
  still post as any member, **including the owner**, with a few lines of client code. The check stops only the
  laziest spoof (a forged `sender` field on the attacker's own connection).
- **Why it is MAJOR:** it is a security control for a live CRITICAL. The plan tells Damir and the operator that the
  exposure is closed when it is not. The next session is told to write the B0 spec from D-029 (handoff.md:32).
- **Fix (still no client change, still small):** in B0, also verify `message.verifySignature(pubkey)` on relayed
  chat, and on nick/avatar/reaction/delete. Use the stored pubkey for that address, or `endpoint.presence.pubkey`,
  which the hello binds to the address. Honest clients always sign to bots (`CoreStreamProcessor.cs:314-318`
  `sendSpixiMessage`, `:2306-2310` nick, `:2349-2352` avatar). The attacker has the public key but not the private
  key, so the forged message fails. Also stop sending pubkeys to non-members in B0, or say why not. Reword
  roadmap.md:17, D-029 and threat-model.md:47 so that they describe what B0 closes and what it does not.

### New or residual MINOR

| # | Claim | Problem | Evidence | Fix |
|---|---|---|---|---|
| m-1 | "Verdict: `docs/reviews/session-1-verdict.md`" | The file does not exist. Round 1 is `session-1-round-1.md` and this file is `session-1-round-2.md`. D-033 is ✅ and cites the missing file. | base-options.md:3, DECISIONS.md:40, handoff-2026-10-01.md:10, status-log.md:14 | Point all four to the real files, or create the combined verdict from the template. |
| m-2 | The fixed-id list "`nick` {5}, `requestAdd`/`requestAdd2` {0}" and the unsigned list "`getInfo`, `botGetMessages`" | Both lists are incomplete. `avatar` is signed with the fixed id `{6}` and is kept in the pending queue (Core `CoreStreamProcessor.cs:2346-2358`). `getChannels` `{12}`, `getUsers` `{13}`, `getGroups` `{14}` and `getUser` (id = target address) are **unsigned** (`:2464-2474`, `:2586-2635`; `sendBotAction` signs only when `sign=true`, `:2843-2847`). §0 and ERRATA present the lists as complete, so a reader infers that everything else is signed, which repeats the round-1 over-generalisation in a smaller form. | threat-model.md:30-31, base-options.md:19, ERRATA.md:8 | Add the full per-type table with `file:line` (the M2 fix asked for it), as D2's first input. Add `{6}` to the fixed-id rule. |
| m-3 | D-025 / §0: a leaver "reconnects every 2.5 s forever"; option "never send + reject left addresses cheaply at hello" | Inaccurate. `connectToBotNodes` runs every 2.5 s (upstream `Meta/Node.cs:293-311,362,375`), but `connectTo` returns early when a connection exists (Core `NetworkClientManagerBase.cs:351-386`). Today the leaver holds **one persistent connection**. The "reject at hello" option turns that into a **new TCP connection plus hello every 2.5 s per leaver, forever**, which is the opposite of cheap. D4 would grade the options on a wrong cost model. | DECISIONS.md:32, base-options.md:22 | Restate the mechanism. In D4, cost the reject-at-hello option as redial churn (connections/s × number of leavers), next to an option that keeps the connection but gates it (S3). |
| m-4 | Ordering: D4 comes after B2, B3 and B5 | D4 decides same address vs E (a migration is needed only if the address stays, D-020) and `leaveConfirmed`/hello behaviour for leavers (which lands in the adapter and delivery built in B3). B2's migration work and B3's hello path may be rebuilt. The BE ask for the production copy is "before B2" while the decision that needs it is D4. | roadmap.md:22-27, DECISIONS.md:27 | Move D4, or at least its address and leave-flow decisions, before B2. |
| m-5 | Locked rows that the revision contradicts | D-008 ✅ ".NET 10 is the first engineering step" — now B0 and B1a run on .NET 8 first (D-030). D-015 ✅ keeps the pilot "on a copy of production data" as locked, although round 1 found it infeasible and D4 now designs it. | DECISIONS.md:15, :22 | Amend D-008 (the order is B0 → B1a → B1b). Mark D-015 "target ✅ · method 🟡 (D4)". |
| m-6 | .NET 8 end of support (2026-11-10, D-008) vs the 6–8 week plan | 6–8 weeks from 2026-10-01 is 12–26 November. Production (with B0) runs on an unsupported runtime for up to about 2 weeks, and B0 itself is a .NET 8 build. Not mentioned anywhere. | DECISIONS.md:15, :39 | State it as an accepted risk, or schedule a B0-on-.NET 10 fallback. |
| m-7 | CI pipeline | All jobs build with the .NET 10 SDK at the pinned Core. B0 and B1a need a .NET 8 + Core `f6fb55b` job ("tests green on legacy"). | test-strategy.md:47-56 vs roadmap.md:17,19 | Add a legacy job (it can be removed after cutover). |
| m-8 | M8 residual: the pin rule | D-030 says "pin for merges" but not which commit (HEAD vs the app-matching `0.9.8j/k`), why, or how (submodule or tag). | DECISIONS.md:37, roadmap.md:20 | Add it to D-030 or to the B1b outcome. |
| m-9 | M12 residual: slip strategy | base-options says "if B3 slips, fall back to A′" (:63, :76) and D-020 says "C degrades into A′". The roadmap's cut order is B6 dashboard → push → "slow mode/read-only" → timeouts. Slow mode and read-only are in **no batch**. There are no per-batch estimates, no dates, and no budget for CI round trips or the #46 loop (D-019). | roadmap.md:6-11, base-options.md:63,76 | One slip rule in one place. Remove items that are not in scope. Add per-batch estimates. |
| m-10 | m1 residual: the reaction cursor | ERRATA correctly says the reaction case is real and client-side (Core `CoreStreamProcessor.cs:1686-1690` sets the cursor to the target id). It is not in D2, not in the BE critical path and not in the BE backlog, so it is dropped. | ERRATA.md:13, roadmap.md:44-45 | Add it to the BE backlog, or to D2 (server mitigation). |
| m-11 | The ground truth needs "a pilot cohort of instrumented real apps" | That is a special app build distributed to pilot users. It needs app changes (store and redesign builds) and a BE or app owner. It is not on the critical path, and it is the **only** launch evidence for delivery ≥ 99% (D-016). | base-options.md §5, DECISIONS.md:23, roadmap.md:34-42 | Add it to the critical-path table with an owner, or define a harness-only fallback for the launch gate. |
| m-12 | B0 "in days" | B0 needs the operator (D-029 says so), but the critical path lists "who operates" only "before B2". | roadmap.md:40 vs :17 | Move "who operates + wallet custody" to "before B0". |

### NIT

| # | Note | Evidence |
|---|---|---|
| n-1 | "Clients retry for up to 5 days": after the 5-day expiry, Core still sends the message **once more** (`PendingMessageProcessor.cs:322-333`; the apps remove it in `onMessageExpired`). The S1 window must allow that last send. | threat-model.md:30, base-options.md:23 |
| n-2 | The handoff says "8 batches"; there are 9 (B0, B1a, B1b, B2–B7). | handoff-2026-10-01.md:23 |
| n-3 | D-021 says "roadmap B1–B7"; B0 is missing. | DECISIONS.md:28 |
| n-4 | D-004 still says "three designs (evolve/QuIXI/hybrid)"; the grading has four (A′/B/C/E). | DECISIONS.md:11 |
| n-5 | D-010 ✅ says "in-session" only. process.md now adds a separate-session review for security, migration and protocol batches. Amend D-010. | DECISIONS.md:17, process.md "Review independence" |
| n-6 | process.md now requires alternatives and consequences in each decision row, but DECISIONS.md has no such fields and no row follows the rule. | process.md "Decision rows" |
| n-7 | CLAUDE rule 7 omits IPs; S6 includes them. | CLAUDE.md:29-30, threat-model.md:36 |
| n-8 | test-strategy.md:20 names "separate processes (D1)" before D1 is graded (it prejudges the graded choice). The FORGE video citation is still unverifiable (round-1 N2). | test-strategy.md:20, process.md:3-5 |

### Spot-checks of new or changed claims against source

| Claim | Result | Evidence |
|---|---|---|
| Fixed ids `nick` {5}, `requestAdd` {0} | **HOLDS, incomplete** (avatar {6}) | Core HEAD `CoreStreamProcessor.cs:2304`, `:2502`; `:2346` avatar |
| Clients retry up to 5 days with the original timestamp | **HOLDS approximately** (n-1) | `PendingMessageProcessor.cs:322-333`, `:590`; `CoreConfig.cs:227`; upstream `SpixiPendingMessageProcessor.cs:24-34` |
| Core static state forbids bot + clients in one process | **HOLDS** | `IxianNode.cs:114`, `FriendList.cs:26`, `CoreStreamProcessor.cs:73-74` |
| S1b: `getInfo` creates users today | **HOLDS** | bot `StreamProcessor.cs:431-432` → Core `BotUsers.setPubKey` adds a contact and writes the file (`f6fb55b` `:175-193`; HEAD `BotUsers.cs:194-210`) |
| D-025: a leaver reconnects every 2.5 s | **MIS-STATED** (m-3) | upstream `Meta/Node.cs:293-311,375`; Core `NetworkClientManagerBase.cs:351-386` (dedupe) |
| ERRATA: callbacks file 52 lines | HOLDS | `wc -l Meta/SpixiBotTransactionInclusionCallbacks.cs` = 52 |
| ERRATA: ~1.5 kLOC server-specific | HOLDS | StreamProcessor 796 + Messages 222 + APIServer 427 + PushNotifications 128 = 1,573; total 4,056 |
| ERRATA: rejection only for a known friend | HOLDS | Core `CoreStreamProcessor.cs:764-781` |
| ERRATA: payment crash needs a pending paid message | HOLDS | bot `StreamProcessor.cs:453-459` |
| ERRATA: deletes don't break the cursor; reactions do | HOLDS | Core `CoreStreamProcessor.cs:1665-1690` |
| §0 "the bot acks before it processes" (marked L) | HOLDS (can be V) | bot `StreamProcessor.cs:61-83` (ack) before `:85` (dispatch) |
| B0 `sender == connection` stops impersonation | **FALSE** (N-M1) | see N-M1 |

---

## PART 4 — Round-1 MINOR / NIT quick pass

| Item | Status |
|---|---|
| m1 cursor fix | Delete dropped ✅; reaction case orphaned → m-10 |
| m2 capped sync N / unknown cursor | ✅ in D2 |
| m3 research accuracy | ✅ ERRATA + definition-of-ready rule |
| m4 Core logs addresses/IPs | ✅ S6 |
| m5 decision hygiene | ✅ D-014/D-016 (new drift: m-5) |
| m6 mutation gate | ✅ aligned in process, test-strategy, D-028 |
| m7 join measurement | ✅ base §5 |
| m8 moderation limit | ✅ base §5, threat-model §5 |
| m9 wallet secret | ✅ S11, B6 |
| m10 process completeness | ✅ mostly (n-6: no alternatives/consequences fields) |
| m11 store vs redesign | ✅ S9, D-025 |
| m12 snapshot vs reader | ✅ test-strategy.md:19 |
| m13 confidence column | ✅ base §0 |
| N1 / N3 / N4 | ✅ |
| N2 | open (n-8) |

---

## Must-fix before committing

1. **N-M1:** add signature verification (against the address-bound pubkey) to B0 for relayed chat and the other
   signed member types. Stop or justify pubkey disclosure to non-members. Correct roadmap.md:17, D-029 and
   threat-model.md:47 so that they say what B0 does and does not close.
2. **m-1:** fix the four references to the missing `session-1-verdict.md` (D-033 is ✅ on a missing file).
3. **m-2:** complete the signed/unsigned and fixed-id classification (avatar `{6}`; the `getChannels`/`getUsers`/
   `getGroups`/`getUser` bot actions are unsigned) in §0, ERRATA and S1/S1b.
4. **m-4 / m-3:** move D4's address and leave-flow decisions before B2. Correct the leaver mechanism (one persistent
   connection; reject-at-hello creates a redial every 2.5 s).
5. **m-5 / m-12:** amend D-008 and D-015. Move "who operates" to before B0.

After item 1 is fixed, a round 3 over that fix only is enough to reach CLEAN. The MINORs can land in the same patch.
