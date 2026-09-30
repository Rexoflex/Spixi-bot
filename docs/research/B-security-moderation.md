# B — Spixi-Bot security & moderation audit (read-only, threat-model input)

Date: 2026-10-01 · Auditor: Claude (read-only; no repo files modified)

## 0. Scope, versions, and a build-blocking fact

| Item | Value |
|---|---|
| Bot | `Spixi-bot` @ `a5a3442` (2025-10-21, `xsbc-0.6.2-PR1`), .NET 8, imports `..\..\Ixian-Core\IXICore.projitems` (`SpixiBot.csproj`) |
| Core the bot was built against | **`f6fb55b` (xcore-0.9.7a, 2025-10-20)** — all Core line numbers below marked `@f6fb55b` refer to this commit (extracted with `git archive`) |
| Core checkout HEAD | `1ff5435` (2026-08-25). **The bot does NOT compile against HEAD**: `PendingTransactions.addPendingLocalTransaction` and `PendingTransaction.messageId` (used at `StreamProcessor.cs:471`, `SpixiBotTransactionInclusionCallbacks.cs:533-535`, `Node.cs:621-623`) were removed in `2113f2f` (2026-03-11). The payment linkage must be re-designed during the rework anyway (see §5). |
| Client (store) | `Spixi-upstream` @ `0e85a4b` (2026-06-16); protocol logic lives in Core `Streaming/CoreStreamProcessor.cs` — identical at `097341a` and HEAD `1ff5435` (diff empty). Client refs marked `CSP@097341a`. |
| Client (redesign) | same Core file; app layer `Spixi-redesign/Spixi/Network/StreamProcessor.cs` |

Legend: **VERIFIED** = read in source, path traced. **LIKELY** = source strongly implies, not executed. **HYPOTHESIS** = needs a testnet repro or BE confirmation.

### 0.1 Quick-scan hints — verdicts

| # | Hint | Verdict | Evidence |
|---|---|---|---|
| 1 | Bans/kicks only set a status, never enforced in onChat / botGetMessages | **CONFIRMED, and worse** | Status only written: `APIServer.cs:235,245`. `onChat` (`StreamProcessor.cs:377-420`) never reads `status`. `botGetMessages` → `Messages.sendMessages` (`Messages.cs:196-220`) has no membership/status check. Live relay `NetworkServer.forwardMessage(code,msg)` broadcasts to **every connected endpoint with a presence** (`NetworkServer.cs:427-457 @f6fb55b`) — banned, kicked, left, and never-joined. A kicked user is re-normalised by any `requestAdd` (`StreamProcessor.cs:653-660`: only `banned` is preserved). `sb_delUser` deletes the record → effectively an unban. |
| 2 | `QuotaManager.exceededQuota` always false; `addActivity` never stores | **CONFIRMED** | Every branch returns `false` (`QuotaManager.cs:35-58`); `addActivity` builds a `Quota` but never inserts it into `quotas` (`QuotaManager.cs:61-99`); `addValidPayment` only mutates existing entries (none exist). `exceededQuota` is never called anywhere. |
| 3 | `serverPassword` never enforced | **CONFIRMED, plus leaked** | Only read/written by the API (`APIServer.cs:137,173-176`); stored plaintext in `Data/settings.dat`; returned in clear by `sb_settings`. No network path consults it. |
| 4 | "TODO Verify signature for all relevant messages" | **CONFIRMED** | `StreamProcessor.cs:27`. Only `requestAdd`/`requestAdd2` verify a signature (`:89,:105`), and even those don't bind it to the endpoint (§1.3). |
| 5 | `message.sender` may differ from the connected endpoint wallet | **CONFIRMED — impersonation** | See §1.2. Bot never compares `message.sender` with `endpoint.presence.wallet`; relays the raw bytes (`:418`); the client does not verify bot-relayed chat signatures. |
| 6 | `isAdmin` may NRE on unknown users | **CONFIRMED (benign crash-wise)** | `Node.users.getUser(...)` returns null (`BotUsers.getUser`) → `.getPrimaryRole()` NRE at `StreamProcessor.cs:347`; same in `getMessagePrice:364`, `sendInfo:560`, `enableNotifications:480`. Caught by the catch-all at `NetworkProtocol.cs:303-306`, so the message is silently dropped. Real consequence: an unknown user's **first chat is lost** (`onChat` asks for nick then NREs in `getMessagePrice(message.sender)` at `:399`). Also `isAdmin` NREs if a user's role points at a deleted group (`getGroup` returns null → `group.admin`). |
| 7 | Any contact request auto-accepted | **CONFIRMED, and membership needs no request at all** | `:95-96`, `:111-112`. Moreover any endpoint that sends `nick`/`avatar`/`getInfo` is inserted as a `normal` user via `setPubKey(..., limit:false)` (`:139,:144,:432`) without ever sending `requestAdd`. |
| 8 | HTTP API on localhost:8501 with optional basic auth | **CONFIRMED — and it is a full hot-wallet API** | Default bind `http://localhost:8501/` (`Node.cs:238-241`); auth off when no `addApiUser` (`GenericAPIServer.cs:529-538 @f6fb55b`). Exposes Core generic methods incl. `addtransaction`, `getwalletbackup`, `loadwallet`, `sign`, `shutdown` (`GenericAPIServer.cs:229-489 @f6fb55b`). All methods accept **GET query strings** (`:161-187`) → CSRF. Stored XSS in the admin UI (§2.3). |
| 9 | `sb_saveAvatar` writes files | **CONFIRMED (fixed paths, unbounded size)** | `APIServer.cs:101-111` writes `avatar.jpg` + `html/avatar.jpg` in CWD from an uncapped base64 param; no response object → falls through to generic handler. Paths are constant (no traversal); risk = admin-API/CSRF-driven content & size. |
| 10 | Payment verification doesn't check payer == message sender | **CONFIRMED, plus 3 more payment flaws** | §5: no payer check, amount counts all outputs, txid replay, sender-field price bypass, and a likely maintenance-thread crash. |
| 11 | Bot logs member wallet addresses at info | **CONFIRMED** | `StreamProcessor.cs:30` (every S2 message), Core `NetworkServer.forwardMessage` logs target address at info (`NetworkServer.cs:389 @f6fb55b`), `Config.cs:185` logs **every config line including `addApiUser = user:password`**. |

---

## 1. Identity & authentication

### 1.1 Transport identity (the connected endpoint) is NOT authenticated for client nodes — VERIFIED (Core)
- Hello handling `CoreNetworkProtocol.processHelloMessageV6 @f6fb55b`: reads `addr` and `pubkey`, sets `endpoint.serverPubKey = pubkey` (`:231`), checks only `Address(pubkey) == addr` (`:257-262`). For `node_type == 'C'` the signature check is **commented out** (`:274-287`, "TODO: verify if the client is connectable, then … check if signature verifies"). No challenge/response for 'C'. `endpoint.presence = new Presence(addr, pubkey, …)` (`:422`). Same code at Core HEAD (`CoreNetworkProtocol.cs:281-294`), and the TODO dates to 2019 (`d4ae043`).
- There is no transport encryption/handshake that binds the key (no session key in `Network/`).
- **Consequence:** anyone who knows a victim's *public key* can connect to the bot as the victim. Public keys are handed out by the bot itself to anyone (`getPubKey` `StreamProcessor.cs:116-128`; `getUsers`/`getUser` bot actions carry `publicKey`, `:436-442`, `:533-554`).
- Everything in the bot keyed on `endpoint.presence.wallet` is therefore spoofable: `isAdmin` for `msgDelete` (`:309`), the `whiteList` gate (`NetworkProtocol.cs:38`), nick/avatar ownership (`:138-156`), `enableNotifications`, and which endpoint receives unicast replies.
- Duplicate identities: Core accepts multiple endpoints claiming the same wallet (no check); the one-connection-per-IP check at `NetworkServer.cs:559 @f6fb55b` compares `IPAddress` with `==` (reference equality — `IPAddress` does not overload `==`) → **LIKELY ineffective**. `forwardMessage(address, …)` delivers to the *first* matching endpoint (`NetworkServer.cs:394-411`), so a spoofer who connects first receives the victim's unicast traffic; one who connects second can **reflect** amplified responses (history, avatars, user lists) onto the real victim's link (HYPOTHESIS — depends on list order).

### 1.2 Message-level sender (`StreamMessage.sender`) is free-form and never checked — VERIFIED
- The bot accepts `message.sender` as given and relays the **raw inbound bytes** to every client (`onChat` → `NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message)` `StreamProcessor.cs:418`; paid path `:494`; reactions `:340`). No comparison with `endpoint.presence.wallet` anywhere.
- Bot-internal uses of the attacker-controlled field: price lookup `getMessagePrice(message.sender, …)` (`:399`, `:461`) → **pricing bypass** (§5); `onLeave(message.sender)` (`:190-191`) → **anyone can mark any member as `left`**, removing them from `getUsers` lists and push (`:282-298`); stored `msg.sender` is the ownership key for self-delete (`:309`).
- Signature coverage (`StreamMessage.getBytes_v1(checksum)` @f6fb55b `:371-447`): `version, id, type, encryptionType, sender, recipient, data, timestamp`. So a *verified* signature would bind sender, recipient (= bot), content, id and timestamp — the primitives for a fix exist; nobody checks them.

### 1.3 Clients trust the bot's relay without verifying chat — VERIFIED (client side)
- `CSP@097341a:667-677`: a message whose `recipient` is a bot friend is treated as a group relay; `group_sender_address = message.sender` (attacker-controlled).
- Chat signature verification for bot relays is **commented out** (`CSP@097341a:969-986`, since `d007cb3` 2025-04-24). App layer adds the message with that sender: `Spixi-upstream/Spixi/Network/StreamProcessor.cs:411`, `Spixi-redesign/Spixi/Network/StreamProcessor.cs:616`.
- **Result: any member can post a chat that every client renders as authored by any other member (incl. the owner/admin), with that member's nick and avatar.** CRITICAL for a "primary social channel" and for AI-agent channels.
- Types that ARE verified client-side for bot relays: `nick`, `avatar`, `msgReaction` (against the bot-supplied pubkey of `group_sender_address`, `CSP:1004-1094`, `:1368-1398`) — sound because `Address = hash(pubkey)`, so the bot cannot map a victim address to a forged key. `msgDelete` is verified against the **bot's** key (`CSP:1349-1354`) — sound; the bot signs its delete notices (`StreamProcessor.cs:322`).
- **Not verified at all client-side:** `botAction` (`CSP:1338-1347` → `onBotAction` `:2645-2736`) — channel list, `info` (incl. the `admin` flag, server name → shown as nick), `user` records (whose `nickData` is parsed by `BotContact.setNick` **without signature check**), `getPayment`. The bot does not sign bot actions (`StreamProcessor.cs:498-513`). Anyone able to deliver an S2 message with `sender = bot` to a client can forge these. Delivery requires a path to the client (the bot/operator itself; possibly any peer if the client accepts inbound S2 — the client does not check `endpoint == message.sender` in `receiveData`) — HYPOTHESIS for BE.
- Mitigating note: `onGetPayment` in the client is a `return;` stub (`CSP:2738-2741`), so a forged `getPayment` cannot move funds today.

### 1.4 Can a client forge bot-origin messages that others trust?
| Message | Can a member forge it via the bot? | Why |
|---|---|---|
| Chat attributed to another member | **Yes** | §1.2 + §1.3 |
| `msgDelete` visible to others | No (as forged bot msg) — but **yes in effect**: connect as the admin (§1.1) or as the message's `sender` field, send `msgDelete`, and the bot itself signs and broadcasts a genuine delete (`:301-328`) | transport spoof |
| `botAction` replies | Not through the bot (it only relays chat/reaction). Directly to a client: HYPOTHESIS (see §1.3) | unsigned |
| Reactions as another member | No (client verifies); replay of a real signed reaction is deduped by id | — |
| Nick/avatar of another member | Rollback only: attacker fetches victim's old signed nick blob (`getNick`), uploads it as their own `nick` → bot serves it for the attacker's address; the embedded `sender` is the victim so clients update the victim's nick to the old value (LIKELY, minor) | nick stored as raw signed StreamMessage (`:140`, `BotContact.setNick`) |

### 1.5 `requestAdd` signature is checked but not bound to the endpoint — VERIFIED
`:89-97`, `:105-113` verify that `spixi_msg.data`/`pub_key` signs the message and matches `message.sender`, then accept **`endpoint.presence.wallet`**. A captured requestAdd from anyone can be replayed from any endpoint; no freshness. Irrelevant today (auto-accept), critical for closed mode.

---

## 2. Authorization

### 2.1 Role storage
- `BotContact.role` is a string `"<idx>;"` (`BotContact.cs` Core). `getPrimaryRole` parses the first index; 0 = default group. `hasRole` uses `Contains(index + ";")` → `"11;"` matches role `1` (bug); `removeRole` discards `string.Replace` result (no-op). Groups (`BotGroup`: name, index, `messageCost`, `admin` bool) in `Data/groups.dat`; users in `Data/contacts.dat`.
- Only way to assign a role: API `sb_setUserGroup` (`APIServer.cs:251-266`). Admin = the user's primary group has `admin=true`, else the **default group's** `admin` flag (`StreamProcessor.cs:343-358`). If the operator ticks "admin" + "default" on the same group (both exposed in the UI, `APIServer.cs:375-392`), **every newcomer is an admin**.
- `BotUsers.getOwner()` = `contacts.First()` (Core HEAD `BotUsers.cs:148-154`) — "owner is whoever was stored first"; `setUser(limit:true)` evicts `First()`. Not used by the bot today; **must not** become the owner source in the rework.

### 2.2 Every admin-capable path
| Path | Gate | Issue |
|---|---|---|
| `msgDelete` (network) `StreamProcessor.cs:301-328` | `isAdmin(endpoint.wallet) || msg.sender == endpoint.wallet` | Both inputs spoofable (§1.1, §1.2). Invalid channel → KeyNotFound (caught). |
| `leave` (network) `:190,:282-298` | none — uses `message.sender` | Anyone can "leave" anyone (MAJOR). |
| `enableNotifications` `:474-482` | endpoint wallet | Hard-wired to `false` (`:478` commented), so inert; NRE on empty data/unknown user. |
| `sb_*` API (`APIServer.cs`) + all Core generic methods | HTTP Basic if configured, else none; optional IP allow-list | No CSRF protection; GET works; admin UI XSS (§2.3). |
| `sb_banUser`/`sb_kickUser` | API | NRE if address unknown → error response; status has no effect (§3). |

### 2.3 Admin UI stored XSS → wallet theft — VERIFIED chain
Member sends `SpixiMessageCode.nick` with HTML → `Node.users.setNick` (`:140`) → `BotContact.getNick()` → `sb_getUsers` (`APIServer.cs:212`) → `settings.js:151` `childEl…("nick")[0].innerHTML = result[key]["nick"]` (also `:148,:159,:162`). An `<img src=x onerror=…>` nick executes in the admin page's origin (the same origin as the Core wallet API) → `fetch('/getwalletbackup')`, `/addtransaction?...`. **CRITICAL** (remote, unauthenticated attacker → operator's funds/keys).

### 2.4 Where owner-by-address fits
- Config-defined `owners = [IXI addresses]` + persisted `admins` set, never "first contact". Every privileged network action must be a **signed StreamMessage** with `sender ∈ owners/admins`, `Address(pubkey)==sender`, signature verified over the checksum form, `recipient == bot`, `|timestamp - now| < window`, and `id` not seen (replay cache). Transport identity alone is insufficient until Core authenticates 'C' hello (§1.1).
- Admin actions should become explicit bot actions (kick/ban/mute/role) — the enum already reserves `kickUser`/`banUser` (Core `SpixiBotAction.cs`); clients currently no-op them (`CSP:2726-2732`).

---

## 3. Moderation enforcement gaps

| Gap | Evidence | Effect |
|---|---|---|
| Banned/kicked users can still post | `onChat` has no status check (`:377-420`) | Ban is cosmetic. |
| Banned/kicked/non-members still **receive** live traffic | `forwardMessage(code,msg)` to all endpoints with presence (`NetworkServer.cs:427-457 @f6fb55b`) | Closed mode impossible without gating fan-out. |
| Anyone can pull full history | `botGetMessages` → `sendMessages` with no membership check; `last_message_id=null` → up to 10,000 msgs/channel (`Messages.cs:196-220`, `Config.maxMessagesPerChannel`) | Privacy; amplification (§4). |
| User list/record to anyone | `getUsers`/`getUser` (`:436-442`); `getUser` returns banned users too, incl. role, status, `sendNotification` | Member enumeration. |
| Kick = "rejoin by re-adding" | `sendAcceptAdd` resets non-banned to `normal` (`:657-660`) | — |
| Delete user = unban | `sb_delUser` removes record; next packet re-creates as `normal` | — |
| Forced "leave" of others | `onLeave(message.sender)` | Griefing; victim disappears from lists. |
| Deleted messages | `removeMessage` nulls `data` in memory/file (`Messages.cs:172-183`); clients that already have it keep it; delete propagates only as a signed notice | Expected; document. |
| Whitelist | `Config.whiteList` is never populated (no config key); only check is at hello with spoofable wallet (`NetworkProtocol.cs:38`) | Dead feature. |

---

## 4. Abuse / DoS

| Vector | Evidence | Severity |
|---|---|---|
| **Message size ≤ 50 MB**, stored in RAM ×10,000/channel, rewritten to disk **in full on every message** | `CoreConfig.maxMessageSize = 50000000 @f6fb55b`; `Messages.addMessage` → `writeMessagesToFile` under `lock(messages)` (`Messages.cs:135-158`, `:83-133`) | CRITICAL (OOM/disk/IO) |
| Fan-out amplification | each accepted chat is sent to every connected endpoint (`:418`) | MAJOR |
| History amplification + lock hold | `sendMessages` loops up to 10,000 sends **while holding `lock(messages)`** (`Messages.cs:198-219`); each `sendData` does an O(n) duplicate scan of the send queue (`NetworkRemoteEndpoint.cs:815-831 @f6fb55b`) and drops from index 10 past 10,000 → blocks all chat processing; LIKELY CPU spike | MAJOR |
| Unbounded users (Sybil) | `setPubKey(..., limit:false)` (`:139,:144,:432,:655`); keys are free to mint; each insert rewrites the whole `contacts.dat` (`BotUsers.writeContactsToFile`) → O(n²) | MAJOR |
| Avatars: 500 KB/address on disk, unbounded addresses | `:143-156` → `Data/Avatars/<addr>.raw`; stored blob is the whole StreamMessage | MAJOR (disk) |
| Avatar check order | `message.data.Length < 500000` before `message.data == null` (`:145-147`) → NRE on null (caught); the "clear avatar" branch is dead | MINOR |
| Nick size unbounded | `setNick(message.getBytes())` (`:140`) — up to 50 MB; persisted, re-served on every `getNick` and embedded in every `user` bot action | MAJOR |
| `getNick`/`getAvatar`/`getPubKey`/`getUsers` to anyone | `:116-136`, `:436-442`; tiny request → large response; with §1.1 spoofing, reflectable onto a victim's link (HYPOTHESIS) | MAJOR |
| `pendingMessages` grows forever | never pruned (`:17`, TODOs `:402-403`); the store client never pays (`CSP:2738-2741`) so every message in a paid group leaks full content into RAM; unsynchronised `List` across threads | MAJOR (when cost > 0) |
| Push loop can block forever | `while (!sendPushMessage(...)) Sleep(1000)` (`PushNotifications.cs:77-82`); iterates `Node.users.contacts` without lock | MINOR (feature mostly off) |
| Connection limits | `maximumStreamClients = 10000` (`Config.cs:53`); per-IP limit likely ineffective (§1.1) | MINOR |
| Maintenance-thread crash via payment | §5.5 | CRITICAL (if confirmed) |
| API server single-threaded | `apiLoop` handles requests serially (`GenericAPIServer.cs:563-623 @f6fb55b`) | MINOR |

---

## 5. Payments

Flow: `onChat` computes price from **`message.sender`**'s group (`:399`); if > 0, stores the message in `pendingMessages` and sends unsigned `getPayment` (`:404-411`). Client replies `botAction payment` with a `StreamTransaction{messageID, tx}`; bot checks only `toList.Keys.First() == bot` and `tx.amount >= price` (`:444-472`), then `broadcastGetTransaction` + `addPendingLocalTransaction(tx, null, messageID)`. Confirmation either by TIV (`SpixiBotTransactionInclusionCallbacks.cs:521-542`) or by "seen from 2 nodes" (`Node.cs:619-626`) → `confirmMessage` relays.

| # | Flaw | Evidence | Severity |
|---|---|---|---|
| 5.1 | **Price bypass**: set `message.sender` to any existing member in a free/admin group; the bot prices by that field, relays for free | `:399` | MAJOR |
| 5.2 | **Payer not checked**: `tx.pubKey`/`fromList` never compared to the message sender/endpoint → any tx paying the bot (someone else's, a donation, observed on chain) can be claimed | `:444-472` | MAJOR |
| 5.3 | **Amount counts all outputs**: `Transaction.amount` = sum of the whole `toList` (`Transaction.fromBytesV7 @f6fb55b`, `amount -= fee` after summing outputs); only the *first* output must be the bot → 1 dust to bot + big output to self passes | `:447`, `:462` | MAJOR |
| 5.4 | **Replay**: pending de-dup is only by txid while pending (`PendingTransactions.addPendingLocalTransaction @f6fb55b:42-53`); after TIV verification the tx is removed (`…Callbacks.cs:537`) and the same tx can be submitted for another messageID and re-verified. LIKELY; testnet repro needed | — | MAJOR |
| 5.5 | **Likely remote crash**: payment is added with `relayNodeAddresses = null` (`:471`); `processPendingTransactions` does `foreach (var address in entry.relayNodeAddresses)` after 40 s (`Node.cs:610-617`) **before** the "confirmed" check → NRE on the `performMaintenance` thread, which has no try/catch (`Node.cs:403-415`) → unhandled thread exception terminates the .NET process. Trigger: any payment tx not TIV-verified within 40 s (e.g., an unsigned/never-broadcast tx — the bot never checks the tx signature or broadcasts it itself, `:468-470`). HYPOTHESIS (high confidence) | `Node.cs:610-617` | CRITICAL |
| 5.6 | "Confirmed" = seen from ≥2 relays, not included in a block | `Node.cs:619-626` | MINOR (0-conf acceptance) |
| 5.7 | Pending list growth / no expiry | `:17`, `:402-403` | MAJOR |
| 5.8 | Store client never pays (`onGetPayment` stub) → paid groups are unusable end-to-end today | `CSP:2738-2741` | info |

Where funds are held: the bot's single hot wallet `Data/ixian.wal` (encrypted; password typed at console, `Node.cs:107-229`). Reachable through the unauthenticated/CSRF-able API (`addtransaction`, `getwalletbackup` returns the encrypted wallet for offline cracking, `GenericAPIServer.cs:1539-1547 @f6fb55b`).

---

## 6. Privacy

- **Group traffic is plaintext end to end.** Clients send to bots with `encryptionType = none` (+ signature) (`CSP:314-318`, `:2839-2842`, `:2306-2310`); the Ixian transport has no encryption layer. The operator, any on-path observer, and **any endpoint that merely connects to the bot's port** (§3) can read all live messages.
- At rest (bot host): `Data/Messages/<ch>/messages.ixi` (all messages, plaintext), `Data/contacts.dat` (addresses, pubkeys, nick blobs, roles, notification flag), `Data/Avatars/*.raw`, `activity/` DB; `Data/settings.dat` incl. `serverPassword` in clear.
- Logs (info level): per-message sender wallet (`StreamProcessor.cs:30`), forward target wallet (Core `NetworkServer.cs:389`), presence updates (`NetworkProtocol.cs:368`), config lines incl. **API credentials** (`Config.cs:185`). With `maxLogCount=10 × maxLogSize=50MB` this is a durable member-activity timeline.
- Member enumeration by anyone: `getUsers`/`getUser`/`getPubKey`; `BotInfo.userCount` (`:589`). No blind mode for bot channels (client-side `hideParticipantAddresses` exists only for client-hosted groups).
- Push: `sendPushMessage` posts `tag=<member address>` to `https://ipn.ixian.io/v1/push.php` (`PushNotifications.cs` `sendPushMessage`) → the push service learns bot membership and activity timing. Currently new opt-ins are forced to `false` (`:478`), but **legacy `contacts.dat` records may still have `sendNotification=true`** → still pushed. HYPOTHESIS for BE to confirm deployed state.
- Avatars/nicks are served to anyone and never verified by the bot (clients verify signatures for nick/avatar).

## 7. File system

| Input | Path | Verdict |
|---|---|---|
| Member address → avatar file | `Path.Combine(avatarPath, address.ToString()+".raw")` (BotUsers @f6fb55b:207, :285) | Safe (Base58 of bytes). Content & count unbounded (§4). |
| Channel index → messages dir | `Path.Combine(base, channel.ToString())` (`Messages.cs:85`) | Safe (int; channels only via API). |
| `sb_saveAvatar` | fixed `avatar.jpg`, `html/avatar.jpg` (`APIServer.cs:101-111`) | Fixed path; unbounded size; CSRF-able. |
| Core `loadwallet?file=` | arbitrary path (`GenericAPIServer.cs:1559+ @f6fb55b`) | Admin API only, but CSRF/XSS-reachable. |
| Core `/resources/<path>` | `"html" + segments` (`GenericAPIServer.cs:713-752 @f6fb55b`) | HYPOTHESIS: traversal via encoded `..` depends on `HttpListener` URL normalisation — test `/resources/..%2F..%2FData%2Fixian.wal`. |
| `settings.dat` values | written `key = value` per line (`Settings.cs:469-476`) | Newline injection via `sb_setOption` values (MINOR; admin/CSRF). |

## 8. Self-hosting safety — unsafe defaults

| Default | Where | Risk |
|---|---|---|
| API has **no auth** unless `addApiUser` set | `GenericAPIServer.cs:529-538 @f6fb55b` | Local users/containers/any browser page (CSRF) control the wallet. |
| All API methods via GET; no CSRF token/Origin check; JSON-RPC POST with `text/plain` also parsed | `GenericAPIServer.cs:139-187` | Drive-by `addtransaction`/`sb_*` from any website the operator visits (browser Private-Network-Access may partially block — HYPOTHESIS). |
| Windows opens the admin UI in the default browser on start | `Node.cs:246-249` | Normalises browsing to it. |
| Docs invite `apiBind` (e.g. `http://+:8501/`) with no TLS | `Config.cs:137` | Plain-HTTP Basic auth on the internet. |
| Auto-accept everyone; no quotas; message cost 0 | §3, §4 | Open spam target. |
| `maxMessagesPerChannel=10000`, 50 MB messages | `Config.cs:65` | Resource exhaustion. |
| "admin"+"default" group combo allowed | `APIServer.cs:375-392` | Everyone admin. |
| Credentials logged | `Config.cs:185` | Secret leakage in `ixian.log`. |
| `whiteList`, `serverPassword`, quotas look like controls but do nothing | §0.1 | False sense of security. |
| Build pins Core by sibling path, not commit | `SpixiBot.csproj` | Self-hosters build against arbitrary Core → HEAD doesn't build. |

---

## 9. Threat list (STRIDE-ish)

| # | STRIDE | Threat | Asset | Current state (file:line) | Sev | Recommended control |
|---|---|---|---|---|---|---|
| T1 | S | Post chat as any member/owner/agent | Message authenticity | Bot relays raw bytes without `sender==endpoint` (`StreamProcessor.cs:377-420`); client skips chat sig verify (`CSP@097341a:969-986`; app `StreamProcessor.cs:411` upstream / `:616` redesign) | CRITICAL | Bot: require `message.sender == endpoint.presence.wallet` AND verify signature with the stored pubkey (`Address(pk)==sender`) for every relayed type; client (BE): re-enable verification for bot relays, fetch pubkey via `getUser` before rendering |
| T2 | S | Connect as any address (incl. admin/owner, allow-listed) knowing only its pubkey | All identity-based authz | 'C' hello signature check commented out (`CoreNetworkProtocol.cs:274-287 @f6fb55b`, same at HEAD) | CRITICAL | Core: challenge-response signature for 'C' hellos. Until then bot authz must rely on per-message signatures + freshness (T12) |
| T3 | T/E | Stored XSS in admin UI via nick → steal wallet / send funds | Hot wallet, admin | `settings.js:151` innerHTML of `sb_getUsers` nick | CRITICAL | `textContent` everywhere; CSP header; serve admin UI from a separate origin from wallet API; cap/strip nick |
| T4 | E | CSRF against localhost API (GET + text/plain POST) | Hot wallet, bot config | `GenericAPIServer.cs:139-187 @f6fb55b`; no auth by default `:529-538` | CRITICAL | Default-deny: generate random API token on first run; require custom header + Origin/Host check; POST-only for mutations; disable wallet-spending methods on the bot build |
| T5 | D | Memory/disk/IO exhaustion via 50 MB messages, full-file rewrite per message | Availability | `Messages.cs:83-158`; `CoreConfig.maxMessageSize` 50 MB | CRITICAL | Per-type size caps (chat ~8–16 KB), append-only store/DB, per-user rate limits, total storage budget |
| T6 | D | Remote crash of bot via payment botAction (null relay list NRE on maintenance thread) | Availability | `StreamProcessor.cs:471` + `Node.cs:610-617`, no try in `:403-415` (HYPOTHESIS, high confidence) | CRITICAL | Guard null; try/catch per loop iteration; verify tx signature & structure before accepting |
| T7 | E/I | Banned/kicked/non-members still receive live and historical traffic | Confidentiality, moderation | `NetworkServer.forwardMessage` to all (`@f6fb55b:427-457`); `Messages.sendMessages` no check (`Messages.cs:196-220`) | CRITICAL (for closed mode) / MAJOR (public) | Member-set gated fan-out and history; drop/`bye` banned endpoints; history window policy |
| T8 | E | Banned/kicked users keep posting; kick undone by re-request; delete = unban | Moderation | `onChat` `:377-420`; `sendAcceptAdd` `:653-660`; `sb_delUser` | MAJOR | Enforce status on every inbound type; persistent ban list keyed by address independent of contact record; kick = timed ban |
| T9 | T | Anyone can mark any member as `left` | Membership integrity | `onLeave(message.sender)` `:190-191,:282-298` | MAJOR | Use authenticated sender (T1/T12) |
| T10 | E | Delete any message by spoofing admin or message `sender` field | Content integrity | `onMsgDelete` `:301-328` | MAJOR | Authz on verified signed sender; store authenticated author, not the free-form field |
| T11 | T/R | Payment bypass/fraud: sender-field pricing, no payer check, multi-output amount, txid replay | Operator revenue, spam cost | `:399,:444-472`; `Transaction.amount` semantics | MAJOR | Price by authenticated sender; require `tx.pubKey == sender`; sum only outputs to bot; bind txid→messageId permanently (spent-set); verify inclusion via TIV only; expire pending |
| T12 | S/R | Replay of signed messages (requestAdd, chat, admin actions) | Integrity | No freshness/id cache besides per-channel message id dedup (`:395`) | MAJOR | Timestamp window + per-sender id replay cache; include channel & bot address in signed payload (already covered by checksum form) |
| T13 | D | Sybil user/avatars/nick bloat; O(n²) contacts rewrite | Availability, disk | `setPubKey(...,false)` `:139,:144,:432`; `BotUsers.writeContactsToFile`; avatars `:143-156` | MAJOR | Only create users after accepted join; caps on nick (≤64 chars) & avatar (post-decode image validation, ≤256 KB); DB instead of flat files; per-IP/address rate limits |
| T14 | D | Amplification (history/users/avatars/nick) incl. reflection onto victim via duplicate-identity routing | Availability of members | `:116-136,:162-164,:436-442`; `NetworkServer.forwardMessage(addr)` first-match | MAJOR | Rate-limit requests per endpoint; page history (e.g. 50/req); respond on the requesting endpoint object, not by address lookup; reject duplicate wallets |
| T15 | D | History send holds global lock with O(n²) queue scans | Availability | `Messages.cs:198-219`; `NetworkRemoteEndpoint.cs:815-831 @f6fb55b` | MAJOR | Snapshot under lock, send outside; paged history |
| T16 | I | Plaintext group traffic on the wire and at rest; any connector can read | Confidentiality | `CSP:314-318,:2839-2842`; no transport crypto | MAJOR (public channel: accepted; closed mode: CRITICAL) | Closed mode needs gated fan-out (T7) at minimum; long-term: group key/E2E or transport encryption (BE) |
| T17 | I | Member enumeration & pubkey/nick harvesting by anyone | Privacy | `getUsers/getUser/getPubKey` `:116-128,:436-442` | MAJOR | Members-only; optional blind mode (derived per-bot pseudonymous addresses) |
| T18 | I | Logs contain member addresses per message and API credentials | Privacy, secrets | `StreamProcessor.cs:30`; `Config.cs:185`; Core `NetworkServer.cs:389` | MAJOR | Downgrade to trace; never log config values for secret keys; hash/truncate addresses |
| T19 | I | Push server learns membership/activity | Privacy | `PushNotifications.cs:101-106` `tag=<address>`; legacy `sendNotification=true` | MINOR | Opaque per-device tokens; keep opt-in off until redesigned |
| T20 | I | `serverPassword` shown in clear and unenforced | Secrets | `APIServer.cs:137`, `settings.dat` | MINOR | Remove or implement (as join secret hashed), never return it |
| T21 | E | Default group can be admin → all users admin | Authz | `StreamProcessor.cs:343-358`, `APIServer.cs:375-392` | MINOR | Forbid admin default group; owners/admins as explicit address lists |
| T22 | T | Role string bugs (`hasRole` substring, no-op `removeRole`) | Authz | Core `BotContact.cs` | MINOR | Replace with typed role set |
| T23 | D | Unknown-user NREs drop first message; enableNotifications NRE | Availability/UX | `:347,:364,:399,:476-480` | MINOR | Null checks; create user on authenticated join only |
| T24 | T | Nick rollback via replayed signed nick blob | Integrity | `:140`, `sendNickname :677-692` | MINOR | Bot validates `nickMsg.sender == endpoint` and signature before storing; include timestamp monotonicity |
| T25 | S | Forged `botAction` to clients (info/admin flag, users with unsigned nickData, channels) | Client trust | Bot doesn't sign (`:498-513`); client doesn't verify (`CSP:1338-1347,:2645-2736`) | MAJOR (HYPOTHESIS on delivery path) | Sign all bot-origin messages; clients verify with bot pubkey and require endpoint==bot |
| T26 | T | Build/version drift: bot unbuildable on current Core; payment API gone | Maintainability/security patches | §0 | MAJOR | Pin Core commit (submodule/tag); port payments to new `PendingTransactions` |
| T27 | I | `/resources` path traversal (encoded `..`) | Wallet file | `GenericAPIServer.cs:713-752 @f6fb55b` | HYPOTHESIS | Canonicalise and assert under `html/`; test |

---

## 10. Open questions for the Ixian BE engineer

1. **'C' hello auth (T2):** Is the missing client signature/challenge check in `processHelloMessageV6` deliberate (NAT'd clients can't be verified by `getFullAddress`)? Can we add a challenge-response signature over a server nonce (independent of IP) for 'C' nodes? What breaks?
2. **Client chat verification (T1):** Why was bot-relay chat signature verification disabled in `d007cb3` (2025-04-24)? Is the blocker only "pubkey not yet known → message pending"? Would you accept verifying against `friend.users.getUser(group_sender).publicKey` with a pending queue + `requestBotUser`?
3. **Delivery path for forged bot messages (T25):** Do Spixi clients accept inbound S2 connections from arbitrary peers, or route S2 via relays that could inject `sender=bot` messages? Should `receiveData` require `endpoint.presence.wallet == message.sender` (or == bot) for non-relayed types?
4. **Duplicate identities:** Should `NetworkServer` reject a second endpoint with the same wallet (or replace the old one)? Is the `remoteIP.Address == clientEndpoint.Address` reference comparison (`NetworkServer.cs:559 @f6fb55b`) known?
5. **Payments:** What is the intended replacement for `addPendingLocalTransaction(tx, null, messageId)` after `2113f2f`? Is TIV the only acceptable confirmation? Should bot-message payments carry the message id in the tx `data` field so the payment is cryptographically bound to the message?
6. **Relayed payload encryption:** Any plan for group keys / E2E for bot channels, or transport encryption between client and bot? For closed mode, is "bot operator can read" acceptable?
7. **Push:** Is `ipn.ixian.io` still accepting bot pushes? How many deployed bots have legacy `sendNotification=true` users? What does the push service log (tag = raw address)?
8. **Blind mode:** Can `hideParticipantAddresses` / `GroupChat.DeriveGroupAddress` be reused for bot channels (bot rewrites sender to derived address)? Client-side support exists for client-hosted groups only.
9. **Max sizes:** Is there a protocol-level per-SpixiMessage-type size limit we can rely on, or must the bot enforce its own (chat, nick, avatar, reaction)?
10. **Replay protection:** Is there an agreed freshness window for `StreamMessage.timestamp`? Any existing per-sender id cache in Core we should reuse?
11. **Official vs self-hosted:** For the Ixian primary channel, should the bot's wallet be separated from the API process (watch-only on the bot, spending key offline), given the API's wallet methods?
12. **Client handling of kick/ban bot actions** (`CSP:2726-2732` are no-ops): what UX is intended when a member is kicked/banned — should the client stop sending, hide the chat, or show a notice?
