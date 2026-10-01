# B1b · Port to .NET 10 + Core v0.9.8k — plan and grading

Session 6, 2026-10-01. Batch B1b (`docs/roadmap.md`). Decisions: D-049 (shape), D-050 (wire diff, B1a closed),
D-051 (Core pin file, nightly), D-052 (first hands-on test). **Revision 3** (after review rounds 1–2,
`docs/reviews/session-6-round-1.md`, `-round-2.md`). Status: plan 🟡 until the review verdict (§12) is CLEAN; no code is written in
session 6.

Notation: bot = the legacy working tree (`a5a3442` code, HEAD `c44651c`); **f6** = Core `f6fb55b` (tag `v0.9.7a`,
version string xcore-0.9.7a, the legacy build pin); **k** = Core tag **`v0.9.8k` = `1ff5435`** (the B1b pin, D-051).
`1ff5435` differs from `097341a` (version string xcore-0.9.8k, SimClient's client Core since session 3) only in
`UnitTests/Ixian-UnitTests.csproj`, so every compiled Core file is identical; Core `file:line` below are read at
`097341a` and hold for the pin. Sources: three read-only auditors (session 6) + review round 1 + spot checks by the
session lead; every row cites `file:line`. **H** = hypothesis (not built, not run). PR B items are named **P1–P8**
(not B1…, which are roadmap batches).

## 0 · Outcome

| For | After B1b they can | We know it when |
|---|---|---|
| Damir | Run the bot on .NET 10 + Core k on Linux and Windows, with the B1a suite green against it | PR A CI run: ported `normal` green on Linux and Windows; all 12 self-tests fail with their marker on the ported bot (Linux); queue unit tests green |
| BE reviewer | See every behaviour difference from the legacy bot, each accepted or fixed | §3 + §4 complete; the PR A wire-diff report has no unexplained row |
| Damir (first test) | Start a test bot on a Linux VPS from a written path | §10 path exists; PR B merged (admin page works, Linux start proven on the CI ubuntu runner) |

Out of scope: wire changes on purpose, BE asks, any B2–B7 fix (spoofing, bans, storage, delivery). Fixes that Linux
start needs are in scope (§3.3).

**Wire rule (CLAUDE.md §2.6).** Core k adds bytes to every bot-made message (§4). This is not a protocol change by us:
it is Core k's own writer, the readers from f6 to k accept it (§4), the production bot is not replaced before B7, and
the BE engineer reviews it with the batch (D-042). It is diffed in CI on every PR A push.

## 1 · Inputs that are already decided

| Item | Value | Row |
|---|---|---|
| Target | .NET 10 (`net10.0`); .NET 8 support ends 2026-11-10 | D-008 |
| Core pin | tag `v0.9.8k` = `1ff5435`, in a `core.pin` file read by CI; release-tag rule of D-030 kept | D-030 → D-051 |
| Shape | PR A port, then PR B cleanup, both into `rework/bot` | D-049 |
| Payments | stub with the same visible result | D-049 (1) |
| s2data threading | bot-side serial queue | D-049 (2) |
| Suite | ported bot: Linux `normal` + 12 self-tests, Windows `normal`; legacy `normal` job in PR A only | D-049, D-050 |
| W7 / captures | wire diff in PR A; captures dropped | D-050 |
| Harness | out-of-process SimClient on Core k (W3); testnet seeds + infra guard | D-031, D-044 |
| Review | in-session loop only (no separate session): the byte additions come from Core k and go to BE with the batch | D-049 |
| Commits | no AI attribution | D-046 |

Core HEAD today = the pin (`1ff5435`). The nightly HEAD job (PR B) guards the future.

## 2 · Why the bot does not compile against k (research B §0, extended)

Research B §0 named the payment linkage (`PendingTransactions.addPendingLocalTransaction`, `PendingTransaction.messageId`,
removed in Core `2113f2f`, an ancestor of k). Research C §0 named the `BotInfo` and `ActivityStorage` constructors.
The session-6 read found 24 break groups (§3.1) in 6 bot `.cs` files (`Node`, the TIV callbacks, `NetworkProtocol`,
`StreamProcessor`, `Config`, `Program`; `PushNotifications` is **H**) plus the csproj. A full sweep of all 13 `.cs`
files in review round 1 found no further compile break.

## 3 · Port delta (bot vs Core k)

### 3.1 Compile breaks → PR A

| # | Bot `file:line` | Core change (f6 → k) | Fix in PR A |
|---|---|---|---|
| C1 | `Meta/Node.cs:19` | new abstract `addIncomingTransaction(Transaction)` (`Meta/IxianNode.cs:64`) | override; accept only transactions that involve the bot's own addresses (`IxianHandler.isMyAddress`, as the redesign `Network/NetworkProtocol.cs:384-400`), then `PendingTransactions.addIncomingTransaction` (`Transaction/PendingTransactions.cs:45`). Unfiltered, k TIV would rebroadcast third-party transactions every 60 s (`TransactionInclusion.cs:1094-1157`), which legacy never did. Even filtered, k TIV rebroadcasts every pending entry incl. the bot's own incoming payments (it never checks `PendingTransaction.outgoing`, `PendingTransactions.cs:29,45-56`) — see H21 |
| C2 | `Node.cs:455` | `addTransaction(tx, relays, ext, requestId, force)` (`IxianNode.cs:65`) | new signature, as `tests/SimClient/Glue/SimNode.cs` |
| C3 | `Node.cs:463`, `Network/StreamProcessor.cs:471` | `addPendingLocalTransaction` removed (`2113f2f`) | `Node.cs:463` → `addOutgoingTransaction` (`PendingTransactions.cs:58`); `StreamProcessor.cs:471` → removed by the payment stub (§5 A3); `Node.cs:463` is rewritten with C2 (A1) |
| C4 | `Node.cs:621`, `Meta/SpixiBotTransactionInclusionCallbacks.cs:24-26` | `PendingTransaction.messageId` removed | removed with C5 and C11 (A1); the StreamProcessor side by the payment stub (A3) |
| C5 | `Node.cs:581-636` | `pendingTransactions` is a `Dictionary` (`PendingTransactions.cs:43`); TIV resends and expires itself (`TransactionInclusion.cs:1094`) | delete the bot's `processPendingTransactions` loop and its callers (`Node.cs:413` in `performMaintenance`, callbacks `:49`) (otherwise duplicate work and `transactionData2` sends) |
| C6 | `Node.cs:605` | `ActivityStatus.Error` removed (`Activity/Activity.cs:57`) | goes with C5 |
| C7 | `Node.cs:535,564`; caller `Network/NetworkProtocol.cs:323` | `ActivityObject` ctor changed (`Activity.cs:215`) | delete `Node.addTransactionToActivityStorage`; `C1` inserts into activity storage once (Core `IxianHandler.addTransactionToActivityStorage`, `IxianNode.cs:486`); remove the call at `NetworkProtocol.cs:323` (one owner: A1, §5) |
| C8 | `Node.cs:235` | `ActivityStorage(path, cache, maxBlocks, RocksDBOptimizations, minDiskSpace)` (`Activity/ActivityStorage.cs:958`) | pass `RocksDBOptimizations.Servers` and a config min-disk value |
| C9 | `Node.cs:92` | `TransactionInclusion(IStorage, callbacks, TIVBlockVerificationMode)` (`TransactionInclusion.cs:123`) | create a `RocksDBStorage` for headers (`Storage/RocksDBStorage.cs:1190`) in a **new** folder (`Data/testnet-headers-k` / `Data/headers-k`, not the legacy BlockHeaderStorage folders); call `prepareStorage(false)` **before** `tiv.start` (else `getHighestBlockInStorage` throws `DirectoryNotFoundException` on `pathBase/0000`, `RocksDBStorage.cs:1474`; folder made in `prepareStorageInternal`, `:1274-1282`) and `stopStorage()` after `tiv.stop()` in `Node.stop` (prepare as the redesign `Meta/Node.cs:312,346`; the redesign stops storage at `:751` **before** `tiv.stop()` at `:758` — the plan's order is the safer one); mode `Minimal`. With C10, an existing wallet resyncs headers from block 0 on its first ported start (harmless under D-034, new address) |
| C10 | `Node.cs:291,295` | `start(ulong height, byte[]? checksum, bool prune)` (`TransactionInclusion.cs:130`) | keep legacy behaviour: new wallet → mainnet `CoreConfig.bakedBlockHeight/Checksum`, testnet 0/null (as f6 `TransactionInclusion.cs:79-89`); existing wallet → 0/null (as `Node.cs:295`) |
| C11 | `SpixiBotTransactionInclusionCallbacks.cs:10` | interface gained `transactionVerified/Rejected/Expired/CannotVerify`, `blockReorg` (`TransactionInclusion.cs:69-77`); `receivedTIVResponse` gone | `receivedBlockHeader` keeps the warm-up exit (`:45-47`); `transactionVerified` → `activityStorage.updateStatus(txid, Final, 0)` (legacy `:17-32`; k needs the `blockHeight` argument, `ActivityStorage.cs:1335`); the others no-op with a comment |
| C12 | `Node.cs:333,649` | `BlockHeaderStorage` deleted | `IxianHandler.getTimeSinceLastBlock()` (`IxianNode.cs:75`; measures network time minus block timestamp, legacy measured time since the last header arrived: a slow testnet shows "stalled" earlier — **L**); `blockStorage.getBlock(n)` (`Storage/IStorage.cs:304`) |
| C13 | `Node.cs:345,496`; `Program.cs:149,159,268,274` | `forceShutdown { get; private set; }` (`IxianNode.cs:126`) | `IxianHandler.requestShutdown()` (`:286`); `Node.shutdown()` must not call back into `IxianHandler.shutdown()` (`:291-296`) |
| C14 | `Node.cs:171,323,476,486`; callbacks `:37`; `NetworkProtocol.cs:200` | `balances` is `Dictionary<Address,Balance>` (`IxianNode.cs:120`) | indexer / `.Values` / `TryGetValue`; drop the `getWallet`/`getWalletBalance` overrides (base is virtual, `:85,:93`) |
| C15 | `Node.cs:460,614` | `NetworkClientManager.sendToClient` lost `helper_data` (`Network/NetworkClientManager.cs:104`) | drop the trailing `null` |
| C16 | `NetworkProtocol.cs:167` | `RemoteEndpoint.sendData(code, data, long msgId, prio)` (`Network/NetworkRemoteEndpoint.cs:745`) | drop `helper_data` |
| C17 | `NetworkProtocol.cs:231` | `compactBlockHeaders1` renamed `blockHeaders4 = 50` (`Network/Network.cs:153-154`, commit `72f3a24`) | rename the case; payload unchanged (the k-era redesign parses `blockHeaders4` exactly as legacy did, redesign `NetworkProtocol.cs:340-370`) |
| C18 | `NetworkProtocol.cs:320` | `TransactionInclusion.receivedNewTransaction` removed | `IxianHandler.addIncomingTransaction(tx)` (C1, filtered) |
| C19 | `NetworkProtocol.cs:375,404-477` | `broadcastGetKeepAlives(List<InventoryItemKeepAlive2>)` (`Network/CoreNetworkProtocol.cs:1007`); type 5 `keepAlive2` decoded (`Inventory/InventoryCache.cs:355`); keepAlive2 items are tracked under `InventoryItemKeepAlive2.getHash(lastSeen, addr, device)` | handle `keepAlive2`; in `handleKeepAlivePresence` mark `setProcessedFlag(keepAlive2, InventoryItemKeepAlive2.getHash(…))` (as redesign `NetworkProtocol.cs:496`), else every item is re-requested (`InventoryCache.cs:155`); announce own presence with `InventoryItemKeepAlive2` (`NetworkProtocol.cs:114`) |
| C20 | `Network/StreamProcessor.cs:589` | `BotInfo` ctor has no `serverName`, adds `random_id`, `hide` (`Streaming/Bot/BotInfo.cs:33`) | `new BotInfo(0, null, false, desc, …)` then `bi.serverName = Node.settings.getOption("serverName", "Bot");` (keeps the `servername` needle text once, §6.3) |
| C21 | `Meta/Config.cs:235` | `CoreConfig.walletNotifyCommand` removed (`b4112bb`) | drop the option (unused) |
| C22 | `SpixiBot.csproj:39`; `Program.cs:25-31` | Core k uses `Mono.Nat` (`UPnP/UPnP.cs:14`) | package `Mono.Nat` 3.0.4; drop the `Open.Nat.dll` start check (else `Environment.Exit(-1)` on every OS) |
| C23 | `SpixiBot.csproj:40` | k calls `DbOptions.SetWALTtlSeconds` (`ActivityStorage.cs:194`, `RocksDBStorage.cs:227`) | RocksDB **10.4.2.64152** (proven in SimClient CI on Windows; 11.1.2 lacks it). **H:** 62171 also lacks it; **H:** the Linux native library loads (first proof: the Linux `port-build` smoke) |
| C24 | `SpixiBot.csproj:22-25` | `net8.0` | `net10.0`; CS8632 warnings stay warnings in the bot project until P2 |

Also needed for the build/Linux start, outside Core: `Node.cs:257,384`, `Program.cs:262` dereference
`statsConsoleScreen`, which L2 may leave null → null guards (owner A1/A6, §5).

Unchanged at k (checked): `GenericAPIServer.start/stop/processRequest/sendResponse/JsonResponse/JsonError` (signatures);
`NetworkServer.forwardMessage` both overloads (`Network/NetworkServer.cs:385,431`; fan-out unchanged: every endpoint
with a presence, sender included; the two targeted-forward log lines moved from info to trace, `:389,412`);
`processHelloMessageV6` (acceptance unchanged; internal `sendBye` default `removeAddressEntry=false`,
`CoreNetworkProtocol.cs:43`); `StreamMessage` binary codec (public API lost `realSender`, unused by the bot);
`SpixiBotAction`, `ReactionMessage`, `BotGroups`, `BotChannels`; `BotUsers.contacts` (`OrderedDictionary`, every use
compiles); `PresenceList.init` (new optional arg); `WalletStorage`; `Node.cs:327` `broadcastProtocolMessage(…, null)`
(the `null` now binds to `skipEndpoint`).

### 3.2 Behaviour changes without a compile error

Disposition: **A** = fixed in PR A · **L** = listed and accepted · **P** = PR B · **later** = named roadmap batch.

| # | Change | Evidence | Visible to | Disposition |
|---|---|---|---|---|
| H1 | **s2data bypasses `NetworkQueue`**: handlers run on each endpoint's parse thread, in parallel. In f6, s2data arrived with priority `auto` and went to the **high** queue: one thread shared with hello/bye/keepAlive, no bound, never dropped; when the global queue held > 5,000 / 10,000 items every receive loop slept 500 / 1,000 ms | k `CoreNetworkProtocol.cs:157-162`; f6 `NetworkRemoteEndpoint.cs:360-368,674-720`, f6 `NetworkQueue.cs:149-178,258-297` | members: order across members; races in `Messages`, `BotUsers`, `pendingMessages` | **A** — bot serial queue (§5 A2). It restores s2data-to-s2data order only: in f6, hello/keepAlive/presence handlers ran on the same thread; at k they run on `NetworkQueue` threads in parallel with the bot queue (**L**) |
| H2 | Receive-side dedup is gone. f6 dropped an s2data only if one with the same code, checksum **and endpoint** was still waiting in the high queue | f6 `NetworkQueue.cs:259-264`; k path H1 | a quick resend of a reaction can be relayed twice | **L** (D2 owns dedup) |
| H3 | Send queue: s2data in the high queue; full (10,000) → `TryWrite` drops the **new** message silently (result ignored); duplicates only warned | `NetworkRemoteEndpoint.cs:134,762-776,842-859,1381-1388` | a full-history replay under backlog can lose its newest messages with no log | **L**; not measurable without a Core change; D2/B3 own replay paging. Receive side too: k has no socket-level backpressure for s2data (the receive loop throttles only on `NetworkQueue`'s count, `NetworkRemoteEndpoint.cs:323-363`), and a full per-endpoint raw channel (10,000, `:171`) drops new messages of every code silently (`TryWrite` result ignored, `:339`) |
| H4 | Every SpixiMessage the bot builds ends with 2 bytes (empty `groupAddress`, `groupSenderAddress`) | `Streaming/SpixiMessage.cs:161-162`; `WriteIxiBytes(null)` = varint 0 (`Utils/IxiUtils.cs:245-251`) | readers: f6 … v0.9.8e stop after `channel`; v0.9.8f … k read the fields only when bytes remain (`SpixiMessage.cs:122-126`; changed only in `8bf3d5a` = v0.9.8f) | **L** (wire diff, §4) |
| H5 | Inbound SpixiMessage with junk after `channel` → parse error → `type=0, data=null, channel=0` | `SpixiMessage.cs:122-138` | a malformed post becomes chat/channel 0, acked with channel 0 and dropped (`StreamProcessor.cs:75-76,379`) | **L**; parser tests in roadmap B2 |
| H6 | `BotInfo.getBytes` always writes `randomId` + `hide` | `BotInfo.cs:95-96` | readers: k reads them; f6 … v0.9.8e ignore; matches D-048 | **L** (wire diff); keep `hide=false` while `randomId` is null (k derives sender addresses from `randomId` when hide is true, `CoreStreamProcessor.cs:342-365`, **H**) |
| H7 | `BotUsers` file version 1 (+ reserved bool) | `Streaming/Bot/Users/BotUsers.cs:65-67,121-125` | rollback: the legacy bot misreads a v1 `contacts.dat` | **L** (one-way; new address, D-034; D4 notes it) |
| H8 | `BotUsers.setPubKey(limit:true)` over 500 removes the **just-added** user | `BotUsers.cs:204` | none (the bot passes `false`) | **L** |
| H9 | TIV header store in RocksDB; an empty store gives a stub header `{blockNum = start height}`, so `getLastBlock()` is non-null from `tiv.start` on | `TransactionInclusion.cs:143-159` | none new: f6 also set a stub header at the start height once the TIV thread ran (f6 `TransactionInclusion.cs` ~`:133-140`, read by `Node.cs:468-471`); k sets it synchronously in `start` | **A**: start TIV before `NetworkServer.beginNetworkOperations()` (`Node.cs:267` vs `:291`), because `InventoryCacheClient.handleBlock` → `tiv.requestNewBlockHeaders` dereferences `lastBlockHeader!` (`:1086`), null before start. Sync vs async stub is **L** |
| H10 | `subscribeToEvents` no longer pushes tx/balance events | `CoreNetworkProtocol.cs:1312`, `Network/NetworkEvents.cs:24-28` | only paid groups | **L** (payments stubbed) |
| H11 | `readProtocolMessage` drops everything while status is stopping/stopped | `CoreNetworkProtocol.cs:110-114` | messages during shutdown are lost — only on the API `/shutdown` path: the bot's own stop (Ctrl-C, ESC, stdin close; `requestShutdown`, C13) never sets the status (only `IxianHandler.shutdown()` does, `IxianNode.cs:293,304`) | **L**; the bot queue is stopped explicitly in `Node.stop` (§5 A2) |
| H12 | `GenericAPIServer.start()` throws when the listener cannot start (was: log + forceShutdown) | `API/GenericAPIServer.cs:168-186` | operator: busy port → unhandled exception at start | **P4** |
| H13 | API errors return HTTP 500 | `GenericAPIServer.cs:760` | admin page: `sb_saveAvatar` has no response → 500 → avatar does not refresh | **P4** |
| H14 | New default API methods reachable through the bot API (`resumenetworkoperations`, `getactivity`, `extendAddress`, `resolveExtendedAddress`, `getBlockHeader`, `pendingTransactionSendResponse`) | `GenericAPIServer.cs:309-594` | threat model | **A** (docs): introduced-vs-inherited sweep (§11.2); allow-list fix in roadmap B6 if the sweep says "inherited class" |
| H15 | Core k `html/` lost Bootstrap 3 and moved jQuery 3.2.1 → 4.0.0 | Core `html/` diff; bot `html/settings.html:9,15-16` | operator: admin page loses styles and tabs | **P3** |
| H16 | Keep-alive: interval check halved, extra address arg gone; PoW checks skipped in warm-up | `Presence/PresenceList.cs` diff, `Presence/KeepAlive.cs:262` (not re-verified in r1) | presence discovery by real apps (not covered by the harness, W9) | **L** + first-test walk row (§10) |
| H17 | `ActivityStorage` min-disk check (`IOException` below it), recreates old-version DBs | `ActivityStorage.cs:286-290,990-993` | operator | **L**; config value set in A1 |
| H18 | `ReadIxiBytes` zero-length fix | `Utils/IxiUtils.cs` ~`:109` | none | **L** |
| H19 | `handleInventory2` dereferences `pii.processed` without a null check; k `InventoryCache.add` returns null for a disabled type, and the k client cache disables `blockSignature2` | `NetworkProtocol.cs:415,429`; `Inventory/InventoryCacheClient.cs:25` | an NRE aborts the rest of the batch (keep-alive and tx fetches, `:477-479`) | **A**: `if (pii == null) continue;` (as redesign `NetworkProtocol.cs:707-711`) |
| H20 | `CoreConfig.maxInventoryItems` 500 → 10,000 | `Meta/CoreConfig.cs:185` | none in tests | **L** |
| H21 | k TIV rebroadcasts every pending transaction every 60 s, incoming ones included (no `outgoing` check) | `TransactionInclusion.cs:1094-1157`, `PendingTransactions.cs:29,45-56` | network only: the bot's own incoming payments (none in practice, payments stubbed) | **L** |

### 3.3 Linux start (D-043 consequence) → PR A

| # | `file:line` | Problem | Fix |
|---|---|---|---|
| L1 | `Program.cs:50,204` | `checkVCRedist()` reads the registry → `PlatformNotSupportedException` | `if (Platform.onWindows()) checkVCRedist();` (P1 deletes it) |
| L2 | `Program.cs:142`; `Meta/StatsConsoleScreen.cs:23,25,68,74` | `Console.Clear`/cursor calls throw on redirected output on Windows, and the stats thread has no try/catch | skip `StatsConsoleScreen` and `Clear` when `Console.IsOutputRedirected`; try/catch in `threadLoop`; null guards at `Node.cs:257,384`, `Program.cs:262` |
| L3 | `Meta/Settings.cs:109 ↔ :144` | `saveSettings` → `setOption` → `saveSettings` (re-entrant lock); Windows ends on the file lock, Linux hangs (D-043) | write `generatedTime` into the dictionary inside the existing lock; one save. Same visible result on Windows: one save, a new `generatedTime` (L28) |
| L4 | `Node.cs:119-127,148-158` | the wallet password prompt needs a TTY; `--walletPassword` is testnet-only and must be ≥ 10 characters, else the bot prompts and hangs | **L** for B1b (harness and first test are testnet); mainnet source → roadmap B6 |

## 4 · Expected wire diff (D-050)

SimClient emits an event `wire` {`code` (SpixiMessageCode), `action` (bot action code or null), `len`, `hex`} for
every bot message it receives. `tests/smoke/wire-diff.ps1` reduces each event to a **shape** per (code, action):
SpixiMessage framing (type, data length class, channel, bytes after `channel`) and, for `botAction info`, every BotInfo
field kind and the trailing bytes. Payload contents (ids, keys, timestamps, signatures, texts) are not compared. It
compares the **set** of shapes per (code, action) between the legacy and the ported job: a shape that differs → listed
as expected (rows below) or **fail**; a (code, action) seen on one side only (timing: retries, `getNick`/`getAvatar`)
→ **warning**. Output: a table "legacy | ported | expected?" as a CI annotation and an artifact; the PR quotes it in
this section "as built" (artifacts expire).

| Message | Legacy | Ported (expected) | Why |
|---|---|---|---|
| every SpixiMessage the bot builds (incl. `acceptAddBot`, `msgReceived`, `msgDelete`, `leaveConfirmed`, `botAction`, `pubKey`, `nick`, `avatar`, `getNick`, `getAvatar`) | ends after `channel` | +2 bytes `00 00` | H4 |
| `botAction info` (inner BotInfo) | ends after `userCount` | + `randomId` (`00`, null) + `hide` (`00`) | H6, D-048 |
| relayed member chat | raw member bytes (`StreamProcessor.cs:418`) | unchanged | raw forward |
| relayed reaction (`:340`), history replay (`Messages.cs:217` → `msg.getBytes()`), paid `confirmMessage` (`:494`) | re-serialized StreamMessage | unchanged | the StreamMessage binary codec is identical at f6 and k |
| BotInfo `serverName` | configured name | configured name (C20) | the `servername` self-test proves it |

## 5 · PR A — port (target ~3 days incl. CI and review)

### 5.1 Work items

| # | Item | Files (owner for a parallel build, L25) |
|---|---|---|
| A0 | `core.pin` = `1ff5435ab89de45f960d111f5fc853516b877061` (tag v0.9.8k); CI reads it for the bot's Core checkout. SimClient keeps its own `CORE_CLIENT_SHA` (`097341a`, the apps' Core, W3; same compiled sources) | `core.pin`, `.github/workflows/harness.yml` (CI owner) |
| A1 | Node + TIV + storage: C1, C2, C5–C15 (Node parts), `NetworkProtocol.cs:323` (C7), H9 start order, `statsConsoleScreen` null guards in `Node.cs` | `Meta/Node.cs`, `Meta/SpixiBotTransactionInclusionCallbacks.cs`, the `:323` line (owner 1) |
| A2 | **Serial s2data queue.** A generic `SerialWorkQueue<T>` (no Core dependency) in a small library `src/SpixiBot.Common` (nullable enabled, warnings-as-errors); `Node.parseProtocolMessage` (`Node.cs:499`) enqueues `(code, data, endpoint)` for `s2data`, other codes pass through. One background thread, FIFO (= legacy s2data order). **Never blocks the caller:** bound 10,000; `TryAdd` without waiting; when full the **new** item is dropped and a counted warning is logged (counts only, no address, D-012). Why not wait: k has no socket-level backpressure for s2data (H3), the caller is a pool-thread parse loop (`NetworkRemoteEndpoint.cs:205,700-708`), and holding it would stall that endpoint's other traffic and can starve the pool (2 s pong, 10 s `pingTimeout`, `CoreConfig.cs:87,91`; worst on a 1-vCPU host) — so legacy's socket throttle cannot be copied. Exceptions caught per item (as `NetworkProtocol.cs:303-306`). **Stop:** `Node.stop` calls `CompleteAdding()` and joins the thread with a timeout **before** `activityStorage` and the C9 header store are stopped (the bot's stop path never sets `IxianHandler.status`, H11); items left are dropped and counted. Handlers read `endpoint.presence` when they run, as legacy did | `src/SpixiBot.Common/**`, the hook and the stop call in `Node.cs` (owner 1 for `Node.cs`; owner 4 for the library) |
| A2t | Unit tests (xUnit v3) for `SerialWorkQueue<T>`: FIFO order; full → drop-new without blocking, with a warning count; a throwing handler does not stop the thread; `CompleteAdding` ends the thread and counts the dropped rest. First .NET 10 Stryker trial on this library (D-028, report-only) | `tests/SpixiBot.Common.Tests/**` (owner 4) |
| A3 | **Payment stub.** A priced post still goes to `pendingMessages` and gets `getPayment`, and is never delivered (as today); the bot's `payment` bot-action case logs (counts only) and drops, with no tx call; tx linkage removed (C3 in StreamProcessor, C4); start-up warning if any group has `messageCost > 0`. Why "never delivered" is the legacy result: store/redesign clients on ≥ v0.9.8 ignore `getPayment` (`onGetPayment` returns, Core `4dee25d`); pre-0.9.8 clients paid, but the bot never broadcast the tx (`StreamProcessor.cs:469` commented out) and its pending entry had `relayNodeAddresses = null` (NRE at `Node.cs:612`), so `confirmMessage` (`:486-496`) never ran (**H** for old clients) | `Network/StreamProcessor.cs` (owner 2) |
| A4 | Network: C14 (protocol part), C16, C17, C18, C19, C20, H19 | `Network/NetworkProtocol.cs` (except `:323`), `Network/StreamProcessor.cs` (owner 2) |
| A5 | Build: C21–C24, `PushNotifications.cs` unused `using System.Security.Policy` (**H** compile); project reference to `SpixiBot.Common` | `SpixiBot.csproj`, `Meta/Config.cs`, `Network/PushNotifications.cs` (owner 3) |
| A6 | Linux start: L1–L3; C13 in `Program.cs`; null guard `Program.cs:262` | `Program.cs`, `Meta/StatsConsoleScreen.cs`, `Meta/Settings.cs` (owner 3) |
| A7 | SimClient `wire` event + `wire-diff.ps1` (§4) | `tests/SimClient/**`, `tests/smoke/wire-diff.ps1` (owner 4) |
| A8 | CI (§6.1): new jobs; **remove** the `legacy build + smoke` jobs and `tests/smoke/patch-linux-start.ps1`; dependency-review job | `.github/workflows/harness.yml`, `tests/smoke/**` except A7 (CI owner) |
| A9 | Docs: tables "as built"; introduced-vs-inherited sweep (§11.2); threat-model rows (H14, §10 S11 testnet exception); re-cite comments in code touched by A1–A6 and the `break-bot.ps1` header cites | `docs/**`, comments (lead) |

Contract between owners (written before the build, L25): the `s2data` hook and `SerialWorkQueue<T>` API, the `wire`
event fields, the needle rule (§6.3), and no change outside the owned files.

Pre-push check (lead): run `break-bot.ps1` for all 10 bot breaks against the ported tree in a scratch copy, to prove
each needle (and the `delete-sign` scope) still matches exactly once.

### 5.2 Acceptance (PR A)

- CI: ported build + start smoke Linux + Windows; ported `normal` green on Linux and Windows (14 cases); 12 self-tests
  on Linux, each red with its marker in every case; legacy `normal` (Windows) green; `wire-diff` has only §4 rows
  (warnings allowed, listed); `SpixiBot.Common.Tests` green; dependency review with no high finding.
- M1, M3, M4 measured and written into §7.
- Review CLEAN (§12 loop) before the first push (L22).

## 6 · How the B1a suite runs against the ported bot

### 6.1 CI layout (PR A)

| Job | OS | Bot | Purpose | Blocks |
|---|---|---|---|---|
| `port-build` | Linux, Windows | ported, .NET 10, Core from `core.pin` | build + start smoke (`bot-smoke.ps1`, no own console on Linux) + `SpixiBot.Common.Tests` | yes |
| `harness normal` | Linux, Windows | ported | 14 cases; Windows keeps `-OwnConsole` until P7 | yes |
| `harness <break>` × 12 | Linux | ported | self-tests `relay, ack, info, cursor, seed-none, reaction, delete, delete-sign, admin, servername, unknown, leave` | yes |
| `legacy normal` | Windows | legacy, .NET 8, Core f6 | wire baseline + proof the suite still passes on legacy | yes |
| `wire-diff` | Linux | — | compares the `wire` events of `legacy normal` and `harness normal (Windows)` | yes (shape diff); warnings for one-sided rows |
| `dependency-review` | Linux | — | `actions/dependency-review-action` (new packages: Mono.Nat, RocksDB); `if: github.event_name == 'pull_request'` (the workflow also runs on push to `rework/bot`, where the action fails) | yes on high |

18 jobs (+1 dependency review); 15 on Linux. **Legacy source:** the `legacy normal` job checks out the bot at the
pinned pre-port commit `c44651c` into `legacy/Spixi-Bot` with Core f6 at `legacy/Ixian-Core` (the csproj imports
`..\..\Ixian-Core`), and takes SimClient and the harness from the PR head (so it has the `wire` event). The ported
jobs keep two Core checkouts (`Ixian-Core` from `core.pin` for the bot, `Ixian-Core-k` from `CORE_CLIENT_SHA` for
SimClient), because `break-core.ps1 unknown` patches only the client Core (`harness.yml:184`). The `legacy normal` job keeps its bot dir at `bin/Release/net8.0`. Both SimClient builds
come from the same source and the same Core, not the same binary.

### 6.2 Harness changes

| Change | Where | Why |
|---|---|---|
| Bot dir `net10.0` for the ported jobs | `harness.yml:203`, `HarnessConfig.cs:8` comment (`:66` is in the removed legacy smoke job) | TFM |
| Linux bot start in redirect mode (no own console), events from stdout or `ixian.log` | `BotProcess.cs:55-66` | D-043 no longer applies to the ported bot. On dispose the bot ignores stdin, so each case waits 5 s and then kills it (`ManagedProcess.cs:418-423`): logs may be cut — note in the artifact README |
| `crashed` on Linux: exit 134 (SIGABRT) with stderr `Stack overflow` | `ManagedProcess.cs:224-235` (reads stderr already); accept `code == 134` + text; **H** (M4); runners must not set `DOTNET_TieredCompilation` (the overflow depends on Tier-0, `LeaveTests.cs:25-29`) | the store `Leave` case runs SimClient on Linux |
| `unknown` self-test on Linux: FailFast exits with 134 | no change expected: `patchFired` already accepts the stderr text "harness self-test 'unknown'" (`UnknownCodesTests.cs:101-102`); **H** that the text reaches stderr on Linux | the self-test moves to Linux |
| Comments citing f6 lines | `RelayEchoAckTests.cs:16-18` (→ `NetworkServer.cs:431-461`), `HistoryCursorTests.cs:21-22` (the bot's own queue now serializes s2data, A2), `BotProcess.cs:19,111-115` | correctness of the record |

### 6.3 Break needles

`break-bot.ps1` (table `:29-40`, needle check `:49,:56`) patches by exact text and fails loudly when a needle is missing or not unique, so drift is CI red,
not a false pass. Needles (all verified unique in the current tree): `relay` `StreamProcessor.cs:418`, `ack` `:76`,
`info` `:433`, `cursor` `Messages.cs:208`, `reaction` `:340`, `delete` `:326`, `delete-sign` `:322` (scope
`public static void onMsgDelete(` at `:301`; the text also occurs at `:672,:708,:757`), `admin` `:357`, `servername`
`:589`, `leave` `:191`. **Rule:** after A1–A6 and after every P-item, each needle text (and the `delete-sign` scope)
occurs exactly once in its file; C20 rewrites line `:589` but keeps the needle text once. Line numbers shift (A3);
re-citing them is part of A9. Any commit that edits a needle line updates `break-bot.ps1` in the same commit and
re-runs every self-test.

## 7 · Measurements (filled in by PR A)

| # | Question | How | Decides |
|---|---|---|---|
| M1 | Does the ported bot start on Linux and pass `normal`? | CI `harness normal (Linux)` | D-043 consequence closed for the new bot |
| M3 | Wire diff = §4 only? | `wire-diff` job | BE review input |
| M4 | Linux store `Leave` still yields `crashed` with `stackOverflow=true`? | CI Linux `normal` | W12 on Linux |

Dropped after review r1: **M2** (does the ported bot serve clients with no seed?) is not observable — the D-044 guard
stops every case before a member starts (`ScenarioRun.cs:45-47`); it moves to the session that revisits D-044.
**M5** (send-queue drops) is not observable without a Core change (H3).

## 8 · PR B — cleanup (target ~1½ days), behind the green suite

| # | Item |
|---|---|
| P1 | Dead code + deps: `System.Diagnostics.PerformanceCounter`, `checkVCRedist`, `--netdump`/`networkDumpFile`, the remaining payment code (keep the stub's visible result), `MACCATALYST` define (`csproj:15-17`), `FluentCommandLineParser` → `-netstandard` (**H** same namespace) together with the `"FluentCommandLineParser.dll"` entry of the start check (`Program.cs:29`; else `Exit(-1)` if the DLL name differs). (walletNotify, the resend loop and the Open.Nat check go already in PR A: they block the build) |
| P2 | `<Nullable>annotations</Nullable>`; zero CS8632; warnings-as-errors for the bot project (process.md G2) |
| P3 | Admin UI: Bootstrap 3 css/js + jQuery 3 in `SpixiBot/html/` (H15); copy target uses `$(MSBuildProjectDirectory)` (not `$(SolutionDir)`) |
| P4 | API: `sb_saveAvatar` returns a response (H13); API start failure → clear log + shutdown (H12); `-c` NRE (`Config.cs:339`) |
| P5 | Re-cite every remaining `file:line` in tests, scripts and docs from f6 to k |
| P6 | Nightly `core-head` job (Linux, report-only, build + `normal`) (D-051); CodeQL (C#) job |
| P7 | Remove the `legacy normal` and `wire-diff` jobs; drop `-OwnConsole` on Windows if `normal` passes without it; update `dotnet.yml` (upstream legacy workflow: Core from `core.pin`, `setup-dotnet 10.0.x`, `dotnet build`, `ixian-platform/Ixian-Core`) |
| P8 | Self-tests re-run; needles per §6.3 |

Acceptance: CI as PR A minus the legacy/wire-diff jobs, plus CodeQL with no high finding and a green nightly dispatch;
zero warnings in bot code; admin page check (tabs + styles) with a screenshot in the PR.

## 9 · Risks

| Risk | Likelihood | Effect | Control |
|---|---|---|---|
| Hidden compile errors (no local compiler, D-019) | high | extra CI pushes | four focused reviewers before push 1 (L22, L25); budget 4 pushes for PR A |
| Serial queue changes timing vs legacy | low | flaky scenarios | event waits only (W2); legacy also had one thread for s2data |
| Testnet header source flakes | medium | red infra runs | D-044 guard labels them |
| Store app Core older than v0.9.8f (BE-04) | unknown | store fidelity | H4/H6 are safe for every reader from f6 to k (§3.2); older than f6 not verified (**H**) |
| Needles drift | medium | CI red | §6.3 rule + pre-push check |
| Rollback to the legacy bot on the same data | low | corrupted `contacts.dat` (H7) | the port runs on a new data dir / new address (D-034) |
| RocksDB native load on Linux | low | Linux start fails | first proof in `port-build` (C23) |
| .NET 8 EOL 2026-11-10 | — | old production bot | accepted (D-038, D-041) |

## 10 · First hands-on test path (D-052)

1. **When:** after PR B merges.
2. **Network:** testnet. Nothing in it touches production: the bot runs with `-t`, and every app instance uses a fresh
   data folder (step 5).
3. **Host:** a small Linux VPS (x64, 1 vCPU, 1–2 GB RAM, public IPv4, a current Ubuntu LTS — "Linux start proven" means
   on the CI ubuntu runner, so the VPS start is itself a check). .NET 10 runtime, the PR B build, its own folder, run
   under systemd (restart on failure) with `WorkingDirectory` = the build folder (`Program.cs:33-41` and `activity/`,
   `Node.cs:235`, are relative to it) and `KillSignal=SIGINT` (the bot stops cleanly only on SIGINT, `Program.cs:155`). Testnet ports: bot **16235** open (`Config.cs:19,277-280`); API **8601**
   (`Config.cs:24`) bound to localhost and reached only by an SSH tunnel, with an API user set (`addApiUser`), because
   a browser on an unauthenticated tunnel is open to CSRF (threat-model T4). Note: `Config.cs:185` logs every config
   line, including `addApiUser` (research B §0.1 #11) — accepted for a testnet test wallet; set `externalIp` if the VPS
   cannot detect its public IP (`Config.cs:213`).
4. **Start:** `--walletPassword` with ≥ 10 characters (`Node.cs:119-127`; shorter → interactive prompt → hang). The
   password on the command line breaks threat-model S11 — accepted as a **testnet-only exception** (new threat-model
   row in A9). Setup: default group (cost 0) + channel through the admin page (or `sb_*`, W11); `serverName` set. The
   bot address is the "Public Node Address" line in `ixian.log` (`Node.cs:218`); members add it by address (no deep
   link, D-042).
5. **Apps:** Damir alone, **one redesign desktop instance on each of 2 Windows PCs + 1 macOS** (D-052 update; phones later): each started from its exe folder (the
   app reads `ixian.cfg` relative to the working directory, redesign `Meta/Config.cs:108,148,254`) with
   `networkType = testnet` and its own `dataFolderPath` (`Config.cs:229`; only headers/activity are per-network,
   `:258-277`). **Isolation:** the app also keeps the wallet password and other settings in MAUI `Preferences`
   (`walletpass`, redesign `Meta/Node.cs:406-410`; written at onboarding, `Pages/Launch/LaunchPage.xaml.cs:581,694,1034`),
   which belong to the app identity, not to `dataFolderPath`. So the test runs under a **separate Windows user account
   or a VM with no real Spixi install**, and both test wallets use the **same password** (the second onboarding
   overwrites the stored one). Check that each instance shows a new address. **H:** an unpackaged Windows build honours
   `ixian.cfg` from the exe folder, two instances can run at once, and the Preferences scope is per user. Phones and
   the store app cannot read `ixian.cfg` (working directory not writable) → they wait for a testnet build of the app
   (Spixi-side work, outside B1b).
6. **Walk sheet** (one row per B1a scenario + discovery): the app finds the bot by address via presence (H16, not
   covered by the harness, W9) · join · post/receive both ways · history after reconnect · reaction · delete (own,
   admin) · info/name shown · leave. (The store-app leave crash, D-047/W12, is out of this walk: no store app.)
7. **Who:** no outside people before roadmap B4 **and** B5 (spoofing is open, research B §1; bans and kicks are not
   enforced, research B §0.1 #1). Note: any testnet user who learns the address can join; "Damir alone" is a choice,
   not enforced.
8. Result → a short report + walk verdict; new DECISIONS rows if behaviour differs from the harness.

## 11 · Grading

### 11.1 Approach (G1, rubric `docs/process.md`)

The three options below differ in how the same port is delivered. The real technical choices (serial queue vs locks
vs nothing; payment stub vs full port vs refuse) were decided in the session-6 interview (D-049) and are accepted
dials, not re-graded here.

| Criterion (weight) | **Chosen: port PR + cleanup PR** | Minimal port only | One PR, port + cleanup |
|---|---|---|---|
| Correctness & safety (×3) | 4 — each change behind a green suite; serial queue keeps legacy order | 4 — same, but the broken admin page stays | 3 — a red run has two candidate causes |
| Member impact (×2) | 4 — two Core-k byte additions, listed and diffed | 4 | 4 |
| Maintainability (×2) | 5 — nullable, no dead code, k citations | 3 — dead code and f6 citations stay | 5 — same end state |
| Self-hosting (×1) | 4 — Linux start, working admin page, nightly drift alert | 3 — admin page broken | 4 |
| Delivery speed (×1) | 3 — ~4½ days, two reviews | 4 — ~3 days | 4 — one review, but likelier rework |
| Extensibility (×1) | 4 | 3 | 4 |
| **Total (/50)** | **41** | 36 | **39** |

Weaknesses of the winner and their fixes: two review loops cost time → PR B reviewers get a narrow brief (diff + needle
rule); needle drift → §6.3 rule + pre-push check; the legacy job lives only in PR A → its wire table is quoted in §4
"as built".

### 11.2 Batch rubric (planned evidence, `docs/templates/batch-rubric.md` + process.md G2)

| Gate | PR A | PR B |
|---|---|---|
| Outcome met | ported `normal` 14/14 Linux + Windows; wire diff = §4 | same suite green; admin page check |
| Build (CI) | .NET 10 + `core.pin`, Linux + Windows; new code (`SpixiBot.Common`) warnings-as-errors; legacy bot code keeps CS8632 warnings until P2 (recorded in D-049); deterministic build: default SDK `Deterministic=true`, not separately proven (**H**) | warnings-as-errors for the bot project |
| Unit + property tests | `SpixiBot.Common.Tests` (A2t) — the only new logic | n/a |
| Contract tests (store + redesign) | B1a suite, both app modes | same |
| Integration (multi-node) | harness scenarios | same + nightly HEAD |
| Mutation | first .NET 10 Stryker trial on `SpixiBot.Common` (report-only, D-028) | report-only |
| Fuzz | n/a (no parser changed by us; H5 noted for roadmap B2) | n/a |
| Security (CodeQL + dependency review) | dependency review (new packages) blocks on high; CodeQL deferred to P6 (needs a build config; recorded in D-049) | CodeQL blocks on high |
| Threat model check | rows for H14 and the §10 S11 testnet exception; **introduced-vs-inherited sweep** (process.md, before the first upstream PR): one row per new k API method (H14) — read-only or state-changing, and whether its class of exposure was already present (research B §0.1 #8: full hot-wallet API on localhost, auth optional); "introduced" → allow-list before the upstream PR | none new |
| No address/content in logs | A2 logs counts only; sweep of Core f6 → k `Logging.*` lines on paths the bot uses (keepAlive2, H3 duplicate warning, `NetworkServer.cs:389,412` now trace) listed in A9; existing bot leaks unchanged (roadmap B6) | none new |
| Adversarial review | CLEAN (§12), in-session (D-049) | CLEAN |
| DECISIONS rows | D-049–D-052 (+ any from M1, M3, M4) | as needed |
| Docs | this file "as built", status log, handoff | citations re-done |
| Compat impact | safe extension from Core k (H4, H6) — BE review with the batch (D-042) | none |

## 12 · Review

Session 6: plan review against Core k and the bot source, loop until CLEAN. Brief and verdict:
`docs/reviews/session-6-brief.md`; rounds `docs/reviews/session-6-round-*.md`. Verdict: *pending*.
