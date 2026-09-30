# Break-my-verdict review, round 3: Spixi-Bot rework, session 1 (over the rev-3 fixes)

Reviewer: fresh adversarial reviewer (read-only), 2026-10-01. Scope: the N-M1 fix, the round-2 must-fix list and the
MINOR/NIT items, and new contradictions from rev 3. Source: bot `SpixiBot@a5a3442`, Core `f6fb55b` and HEAD `1ff5435`
(`git show`), Spixi-upstream `0e85a4b`, Spixi-redesign `5d48669`.

## Verdict: **NOT CLEAN**. 1 MAJOR is open (a residual of N-M1: B0 leaves forged `leave` open). The fix is one clause.

| | Count |
|---|---|
| Round-2 MAJOR (N-M1) | **PARTIAL**: the chat, nick, avatar, reaction and delete control is correct and can be built. One residual MAJOR remains (R3-M1). |
| New MAJOR | 0 (R3-M1 is counted as the N-M1 residual) |
| Round-2 MINOR m-1…m-12 | 11 RESOLVED · 1 PARTIAL (m-2) |
| Round-2 NIT n-1…n-8 | 7 RESOLVED · 1 OPEN (n-8, FORGE citation) |
| New MINOR | 4 |
| New NIT | 5 |

---

## 1 · Is N-M1 fixed? (B0 control vs legacy source)

| Check | Result | Evidence |
|---|---|---|
| (a) The pubkey is bound to the connection's address at hello, on the path the legacy bot uses | **HOLDS.** The bot's own `hello` case calls `CoreProtocolMessage.processHelloMessageV6(endpoint, reader, false)`. That function rejects the hello unless `addr == Address(pubkey)`, then sets `endpoint.presence = new Presence(addr, pubkey, …)`. The client *signature* check is still commented out (T2), so the key is bound to the address but ownership of the key is not proven. B0 must use `endpoint.presence.pubkey`. `endpoint.serverPubKey` is assigned *before* the address check, so it holds a stale value when the hello fails. | bot `Network/NetworkProtocol.cs:30-46`; Core f6fb55b `Network/CoreNetworkProtocol.cs:231` (serverPubKey set), `:256-262` (address check), `:274-285` (sig commented), `:422` (presence); `Presence/Presence.cs:39-42` |
| (b) Honest clients sign chat, nick, avatar, reaction and delete to a bot | **HOLDS** on both Core versions. At f6fb55b: `sendChatMessage`, `sendNickname` (id {5}), `sendAvatar` (id {6}), `sendMsgDelete` and `sendReaction` all call `sign()` when `friend.bot`. At HEAD, all of them go through `sendSpixiMessage`, which signs when `friend.bot`. Both apps call these paths. The checksum covers id, type, sender, recipient, data and timestamp, and the bot relays the raw bytes, so verification sees the bytes the client signed. | Core f6fb55b `Streaming/CoreStreamProcessor.cs:1420-1441, 1509-1536, 1538-1560, 2032-2050, 2075-2093`; HEAD `:314-318`, `:2201-2209`; `StreamMessage.cs:498-533`; upstream `SingleChatPage.xaml.cs:646,1035,1057`; redesign `SingleChatPage.xaml.cs:1661,2484,2539` |
| (b′) Edge case | Not proven safe: a message queued before `friend.bot` becomes true (first nick right after `requestAdd2`) is sent unsigned and RSA-encrypted. For nick, the bot's `requestNickname` re-request fixes it. Other types are not checked. See R3-m2 (log-only first). | Core f6fb55b `:1524-1533`; bot `StreamProcessor.cs:384-388` |
| (b″) Types B0 does **not** list | **FLAW → R3-M1.** `leave` is signed by honest clients but handled with **no check at all**: `onLeave(message.sender)` trusts the sender field. Anyone, on their **own** connection with no key and no T2, can mark any member `left`. That member then drops out of the member list (`sendUsers`) and push notifications. | bot `StreamProcessor.cs:190-191, 282-299, 547`; `PushNotifications.cs:69`; Core f6fb55b `:2122-2140` (sign at `:2136`), HEAD `:2916-2921` via `sendSpixiMessage` (leave signed) |
| (c) "Public keys to members only" can be built without breaking honest clients | **Feasible, with a caveat.** Honest clients send `getInfo` on every connect and `requestAdd2` on join. Both make the sender a `BotContact` (`sendAcceptAdd` sets `normal`). They ask for keys only through `getUser`, via `requestBotUser`, when a relayed nick, avatar or reaction needs a key. `getPubKey` is sent only from commented-out code. So a gate on "is a contact" does not hurt honest clients. **Caveat:** clients drop relayed nick, avatar and reaction when they cannot get the key. If "member" means `status == normal`, a forged `leave` (R3-M1) makes the victim's own client drop other members' nicks, avatars and reactions. The gate also adds **no** security in an open channel, because anyone becomes a contact with one unsigned `getInfo` (R3-m1). | upstream/redesign `Network/NetworkProtocol.cs:111`; bot `StreamProcessor.cs:431-433, 440-441, 116-127, 653-660`; Core f6fb55b `:570-580, 630-640, 930-940` (client drops), `:526-537` (chat verify commented) |
| Wording: what B0 closes and does not close | **RESOLVED.** Roadmap, D-029 and threat-model §4/§5 say that B0 closes posting-as-others and does not close reading-as-others (T2). | roadmap.md:21; DECISIONS.md D-029; threat-model.md:47, :67-71 |

**R3-M1 (MAJOR, residual of N-M1):** B0 leaves an impersonated state change open: forged `leave` needs no key. The
threat model also promises "unsigned requests change nothing" (B4), and the pubkey gate turns the forged leave into a way
to break the victim's client. **Fix:** in roadmap.md:21, D-029 and threat-model.md:47, add `leave` to the list of types
B0 verifies (sender == connection plus a valid signature with `endpoint.presence.pubkey`). Define "member" for the pubkey
gate as "is a `BotContact`" and say what value it has (R3-m1). Name `endpoint.presence.pubkey`, not `serverPubKey`.

## 2 · Round-2 items

| Item | Status | Where / residual |
|---|---|---|
| Must-fix 1 = N-M1 | **PARTIAL** | See §1 (R3-M1). |
| Must-fix 2 = m-1 verdict path | **RESOLVED** | All four refer to the exact path `docs/reviews/session-1-verdict.md`: base-options.md:3, DECISIONS.md D-033, handoff:10, status-log.md:14. The file is written after this round, as intended. |
| Must-fix 3 = m-2 classification | **PARTIAL** | threat-model S1 (avatar {6}) and S1b (6 unsigned types) are fixed. ERRATA now says "not exhaustive; D2 starts with the full table". Residual: base §0:19 and ERRATA:8 list signed types without delete, `leave` and `requestAdd*`. D-022 still names only `getInfo`/`botGetMessages` as unsigned (R3-m4). |
| Must-fix 4 = m-3/m-4 | **RESOLVED** | D-025, S9 and base §0:22 now describe one persistent connection and the redial cost. D4 is in the roadmap before B2 (roadmap.md:26). |
| Must-fix 5 = m-5/m-12 | **RESOLVED** | D-008 is amended and D-015 is "target ✅ · method 🟡". "Who operates" is now asked "before B0" (roadmap.md:42). |
| m-6 .NET 8 end of support | RESOLVED | roadmap.md:15, D-008 |
| m-7 legacy CI job | RESOLVED | test-strategy.md:49 |
| m-8 pin rule | RESOLVED | D-030 (newest tag both apps use, submodule, moved by PR). Small wording drift, see R3-n4. |
| m-9 slip rule / estimates | RESOLVED; new contradiction | One slip rule (roadmap.md:11), per-batch days add up to 40, slow mode is removed. But the "6–8 weeks for the full list" range conflicts with other docs (R3-m3). |
| m-10 reaction cursor | RESOLVED | roadmap.md:50 (BE backlog + D2) |
| m-11 instrumented pilot | RESOLVED | roadmap.md:33 (owner, harness-only fallback) |
| n-1 last send at expiry | RESOLVED | threat-model S1, base §0:23 |
| n-2 batch count | RESOLVED | handoff:23 "9 batches" |
| n-3 D-021 B0 | RESOLVED | D-021 "B0–B7" |
| n-4 D-004 options | RESOLVED | D-004 lists A′/B/C/E |
| n-5 D-010 | RESOLVED | D-010 amended |
| n-6 alternatives/consequences | RESOLVED (from D-034 on) | DECISIONS header |
| n-7 IPs in CLAUDE rule 7 | RESOLVED | CLAUDE.md:29-30 |
| n-8 | PARTIAL | test-strategy.md:20 now says "the setup D1 chooses" ✅. The FORGE video citation (process.md:5) is still unverifiable. |

## 3 · New findings (rev 3)

| # | Sev | Finding | Evidence | Fix |
|---|---|---|---|---|
| R3-m1 | MINOR | "Member public keys stop going to non-members" is listed as a control, but membership is open. One unsigned `getInfo` creates a contact. The gate stops only never-joined, banned, kicked and left connections. It does not reduce T2 (the key is also in every hello the member sends to other nodes). "Member" is not defined. | bot `StreamProcessor.cs:431-433`; roadmap.md:21 | Define member = `BotContact` (any status except banned/kicked). State the limited value. |
| R3-m2 | MINOR | B0 enforces signatures on production with only "manual checks + a short script". There is no evidence that no honest traffic is dropped (store and redesign apps; queued-before-`bot` messages; v0 vs v1 messages). | roadmap.md:21; test-strategy.md:49 | B0: first ship log-only (count failures per type, no addresses), then enforce. Test with both apps on a test bot. |
| R3-m3 | MINOR | The estimate is stated three ways. roadmap.md:10 says "6–8 weeks **for the full list**". roadmap.md:13 and D-032 say "≈ 8 weeks full, ~6 only with the cuts". handoff:41 says "Accept 6–8 weeks". handoff:23 says "~8 weeks". | as cited | Use D-032's wording everywhere. |
| R3-m4 | MINOR | Classification drift. D-022 lists only `getInfo`/`botGetMessages` as unsigned (S1b lists 6). base §0:19 and ERRATA:8 leave `leave`, delete and `requestAdd*` out of the signed list, and §0 marks the row **V** (verified). This omission is how R3-M1 got through. | DECISIONS.md D-022; base-options.md:19; ERRATA.md:8 | Align with S1b, add the missing signed types, or mark the row "partial". |
| R3-n1 | NIT | roadmap.md:3 still says "Revision 2", and base-options.md:82 says "roadmap (revision 2)". The content is rev 3. | — | Bump. |
| R3-n2 | NIT | status-log records only round 1 → rev 2. Round 2 and rev 3 are missing. | status-log.md:13-14 | Add a line. |
| R3-n3 | NIT | Status values outside the legend: "✅ goal · amended", "✅ amended", "✅ target · 🟡 measurement/method". | DECISIONS.md D-008, D-010, D-014, D-015 | Add "amended" and split status to the legend. |
| R3-n4 | NIT | B1b says "a pinned **current** Core", while D-030 pins the newest tag both apps ship with (which may be older than HEAD). | roadmap.md:24 vs D-030 | Say "the D-030 pin". |
| R3-n5 | NIT | ERRATA evidence cites `CoreStreamProcessor.cs` line numbers without naming the commit (they are HEAD lines; f6fb55b differs). | ERRATA.md:8,11,13 | Add `@1ff5435`. |

No contradiction found in: D4 position (roadmap order D2 → D4 → B2, BE path, D-025, handoff), batch count (9), the
pin rule vs the CI jobs (the legacy job exists; build uses the pin), the B0 operator dependency (roadmap, D-029,
handoff §3), the .NET 8 risk (roadmap:15 = D-008), or the slip rule (roadmap:11 = base §3 = D-020).

## Must-fix before committing

1. **R3-M1:** in roadmap.md:21, D-029 and threat-model.md:47, add `leave` to B0's verified types. Name
   `endpoint.presence.pubkey` as the key. Define "member" for the pubkey gate (R3-m1).
2. R3-m3: one estimate wording (D-032) in roadmap.md:10 and handoff:41.
3. Recommended in the same patch: R3-m2 (log-only first) and R3-m4 (D-022 / §0 / ERRATA lists).

After item 1, a short check of those three lines is enough to reach CLEAN.
