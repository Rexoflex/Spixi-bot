# A — Spixi-Bot delivery path & storage audit (read-only)

**Scope.** Bot `Spixi-bot@a5a3442` (xsbc-0.6.2-PR1, 2025-10-21) compiled against Ixian-Core **`f6fb55b`** (xcore-0.9.7a). Core HEAD `1ff5435` (xcore-0.9.8k). Client = Spixi `Spixi-upstream@0e85a4b` (0.9.22, 2026-06-16; pairs with Core `95fc725`, which differs from HEAD by 32 cosmetic lines — client analysis below uses Core HEAD). Redesign branch has the same bot paths (`Spixi-redesign/Spixi/Meta/Node.cs:465,614`).

**Path conventions.** `BOT/` = `Spixi-bot/SpixiBot/`. `CORE7a/` = Core at `f6fb55b` (what the bot actually runs). `CORE/` = Core HEAD (what the client runs). `APP/` = `Spixi-upstream/Spixi/`.

Severity: **MAJOR** = loses/duplicates messages, blocks scale, or corrupts data · **MINOR** = degrades performance/robustness · **INFO** = fact needed for the redesign. Anything not proven by reading code is marked *(hypothesis)*.

---

## 1. Message delivery path

### 1a. Client → bot

| Sev | Finding | Evidence |
|---|---|---|
| INFO | **Clients connect by direct TCP to the bot's public IP:port.** Bot announces a `'C'` presence with `IxianHandler.publicIP:Config.serverPort` (default 15235, testnet 16235). Client sets `friend.relayNode = presence.addresses[0].address` and connects straight to it. No S2/relay hop exists for bots. | `BOT/Meta/Node.cs:261`, `BOT/Meta/Config.cs:16-19`; `APP/Network/NetworkProtocol.cs:477,515`; `CORE/Network/NetworkClientManagerBase.cs:975-1005` (send only to an existing outbound connection matching hostname/wallet) |
| INFO | Client redials **every bot every 2.5 s** from its main loop, while the app is running. | `APP/Meta/Node.cs:293-311,362,374` |
| MAJOR | The bot connection is **not pinned**. The client keeps max 6 stream connections (`maxConnectedStreamingNodes = 6`). Above that it sends `bye` "for shuffling" to the first non-pinned client, which can be the bot. The next loop redials, and each redial runs the full catch-up handshake (§2). | `APP/Meta/Config.cs:84`; `CORE/Network/NetworkClientManagerBase.cs:168-186`; only the primary S2 is pinned `APP/Network/NetworkProtocol.cs:87` |
| INFO | Client→bot messages are sent **unencrypted and signed** (`encryptionType=none`) and never go through the push server (`send_to_server=false`). They sit in the client's pending queue and are retried every 2.5 s until a `msgReceived` comes back. Retry is paused while `friend.bot && !friend.online`. | `CORE/Streaming/CoreStreamProcessor.cs:124-137,314-318`; `CORE/Streaming/PendingMessageProcessor.cs:223-226,590` |
| INFO | Bot side: TCP recv → `NetworkQueue` → **one single high-priority thread** → `ProtocolMessage.parseProtocolMessage` → `StreamProcessor.receiveData`. The same thread handles hello, keepalives and presence. Every bot action (file writes, history dumps) is serialised on it. | `CORE7a/Network/NetworkQueue.cs:280-297,361-395`; `BOT/Network/NetworkProtocol.cs:136-140` |
| MAJOR | **The bot sends `msgReceived` BEFORE processing.** The ack goes out at the top of `receiveData`. Any later failure still leaves the client with its message marked delivered and removed from its pending queue: `onChat` throws, channel 0 is dropped, an NRE fires in `getMessagePrice` for an unknown sender, the message is parked unpaid, or the bot crashes before the file write. → **silent loss.** | `BOT/Network/StreamProcessor.cs:62-83` (ack) vs `:377-420` (processing); client removes pending on ack `CORE/Streaming/CoreStreamProcessor.cs:486-517` |
| MINOR | The bot sends `msgReceived` for almost every control message too (getInfo, getChannels, getAvatar… go to the `default:` branch with channel −1). This is extra traffic per request. | `BOT/Network/StreamProcessor.cs:79-81` |
| MAJOR | **No verification of the claimed sender.** `onChat` stores and relays the message with whatever `message.sender` the client put in it. It never compares that to `endpoint.presence.wallet` or checks the signature (`// TODO Verify signature`). The client-side bot signature check is also commented out. → Anyone can impersonate any member. | `BOT/Network/StreamProcessor.cs:27,377-420`; `CORE/Streaming/CoreStreamProcessor.cs:966-986` |
| MAJOR | **Bans/kicks are not enforced.** `status=banned/kicked` is only set by the API. `onChat`, fanout and history ignore it. The only check is in `sendAcceptAdd` (so a re-add does not un-ban) and in `sendUsers`/push. | `BOT/API/APIServer.cs:230-247`; `BOT/Network/StreamProcessor.cs:657-660,547` |

### 1b. Bot → members

| Sev | Finding | Evidence |
|---|---|---|
| INFO | **`NetworkServer.forwardMessage(code, bytes)` without a recipient** loops over `connectedClients` under `lock(connectedClients)`. It skips only endpoints with `presence == null` (and an optional `exclude_address`, which the bot never passes). It enqueues to **every** inbound connection. It does **not** check `isConnected`/`helloReceived`, bot membership, channel, or banned/left status. It **echoes back to the sender**. It always returns `false`. Semantics are unchanged at HEAD. | `CORE7a/Network/NetworkServer.cs:427-458`; HEAD `CORE/Network/NetworkServer.cs:431-460` |
| MAJOR | **`forwardMessage(address, …)` with a non-connected recipient simply returns `false`.** Nothing is relayed through S2, queued, or retried. The bot ignores the return value: `StreamProcessor.sendMessage` always returns `true`. Every targeted bot→user message is fire-and-forget: acceptAddBot, nick, avatar, info, channels, users, history, getPayment, leaveConfirmed. | `CORE7a/Network/NetworkServer.cs:381-422`; `BOT/Network/StreamProcessor.cs:607-623` |
| INFO | Live fanout sites: free chat relays the **original raw bytes** (`onChat` → `forwardMessage(s2data, raw_message)`). Reactions, delete tombstones and confirmed paid messages are re-serialised with `getBytes()`. | `BOT/Network/StreamProcessor.cs:418,326,340,494` |
| MAJOR | **Per-endpoint send queue silently drops messages (bot's Core 7a).** When a queue holds >10 000 items, `addMessageToSendQueue` does `RemoveAt(10)`, which throws away an arbitrary queued message. A history dump of 10k msgs plus a second channel's dump, or plus live traffic, overflows it. At HEAD the queue is a bounded channel written with `TryWrite` (`FullMode.Wait`), so it drops the **newest** message instead. Neither version reports the drop. | `CORE7a/Network/NetworkRemoteEndpoint.cs:804-831`, `CORE7a/Meta/CoreConfig.cs:73`; `CORE/Network/NetworkRemoteEndpoint.cs:134,171-174,859,1381-1389` |
| MINOR | Send-queue de-dup: a message with the same `(code, CRC32(bytes))` already queued is dropped. It is harmless for unique ids. It costs an O(queue) scan per enqueue, so O(n²) for a 10k dump. | `CORE7a/Network/NetworkRemoteEndpoint.cs:804-822` |
| MAJOR | **Core 7a caps sends at ~1 000 protocol messages/s per connection** (after every 100 msgs it sleeps until 100 ms have passed). This is the floor for history delivery (§3). HEAD removed the send throttle. | `CORE7a/Network/NetworkRemoteEndpoint.cs:580-591`; HEAD `CORE/Network/NetworkRemoteEndpoint.cs:521-524` |
| MINOR | Core 7a logs at INFO level **twice per targeted send** (`">>>> Preparing to forward to {addr}"` + `">>>> Forwarding message"`), and once per recipient per broadcast. A 10k history dump = 20k log lines with a Base58 encode each. HEAD moved the targeted ones to trace. The broadcast line is still INFO at HEAD. | `CORE7a/Network/NetworkServer.cs:390,406,431,448`; `CORE/Network/NetworkServer.cs:393,412,433` |
| INFO | **Connection cap.** `Config.maximumStreamClients = 10000` is copied into **both** `maximumServerMasterNodes` and `maximumServerClients`. `acceptConnection` checks the sum → **20 000 sockets**. Beyond that the socket gets `bye(rejected, "Too many clients already connected.")` and is closed. The client redials every 2.5 s, so it hammers the bot. The practical limit is lower *(hypothesis)*: each endpoint runs 3 async loops that poll with `Task.Delay(10)` when idle (≈300 wakeups/s per idle client in 7a), plus OS `nofile` limits. | `BOT/Meta/Config.cs:53`, `BOT/Meta/Node.cs:66-67`; `CORE7a/Network/NetworkServer.cs:547-556`, `CORE7a/Network/NetworkRemoteEndpoint.cs:193-207,592-595` |
| INFO | The "one connection per IP" guard compares `IPAddress` objects with `==`. `IPAddress` does not overload `==`, so this is reference equality and should never match *(hypothesis — verify live)*. Upside: users behind the same CGNAT are not rejected. Downside: there is no per-IP connection limit. Same at HEAD. | `CORE7a/Network/NetworkServer.cs:559-568`; `CORE/Network/NetworkServer.cs:562` |
| MINOR | The whole broadcast and each targeted send (a linear scan of up to 20k clients) run under `lock(connectedClients)`. That blocks `acceptConnection`/`handleDisconnectedClients`. A history dump holds `lock(messages)` **and** takes `lock(connectedClients)` once per message: 10k × N address compares. | `CORE7a/Network/NetworkServer.cs:392-419,433-455`; `BOT/Messages/Messages.cs:196-219` |

## 2. Offline members / catch-up

| Sev | Finding | Evidence |
|---|---|---|
| MAJOR | **No store-and-forward on the bot.** An offline member receives nothing (no queue, no S2 relay). The only recovery is pull-based history on reconnect. Push is effectively off (§8). | §1b; `BOT/Messages/Messages.cs:196-220` |
| INFO | **When the client asks for history:** each new TCP connection to a bot (`helloData` from a `'C'` endpoint that is a bot friend) → `getInfo` → bot `info` → client **always** sends `getChannels` → bot sends one `channel` action per channel → client sends one `botGetMessages(channel, lastReceivedMessageIds[channel])` per channel. The request is not periodic and not tied to opening a chat. It happens on every (re)connect: app start, the 2.5 s redial after a drop or shuffle, and network change. `botGetMessages` is not added to pending: if it is lost, there is no retry until the next reconnect. | `APP/Network/NetworkProtocol.cs:105-112`; `CORE/Streaming/CoreStreamProcessor.cs:2553-2566,2658-2707` |
| INFO | **Bot response:** under `lock(messages)`, `FindLastIndex(id == last)`, then one `s2data` per stored item after it. It skips originals whose `data==null` (deleted), but **includes reaction records and delete tombstones**. If `last` is null or **not found** → the **entire channel (≤10k)** is resent. | `BOT/Messages/Messages.cs:196-220` |
| MAJOR | **Gap race on reconnect.** Live broadcasts reach a client as soon as its `presence` is set after hello. But the client reads `lastReceivedMessageIds` only when the `channel` reply arrives, three round trips later. Any live message processed in that window moves `lastId` past the unseen gap. Messages between the old `lastId` and that live message are **never fetched**. *(High-confidence hypothesis from code order; needs a live test.)* | `CORE/Streaming/Friends/FriendList.cs:343` (every accepted message sets lastId) vs `CORE/Streaming/CoreStreamProcessor.cs:2664-2672` |
| MAJOR | **Same class, other causes:** any live message dropped by a queue (§1b) or missed during a brief disconnect is lost permanently once a later message advances `lastId`. The protocol has no per-member sequence or ack. | as above |
| MAJOR | **`lastId` often points to an id the bot does not have → full 10k re-dump on every reconnect (duplicates):** (a) `handleMsgReaction` sets `lastId = rm.msgId`, the **reacted-to** (older) message, not the reaction's own id. (b) The client's own sent messages set `lastId` too. If one is an unpaid pending message (never stored by the bot), a message the bot dropped after acking (§1a), or one older than the 10k window, `FindLastIndex` returns −1 → full resend. | `CORE/Streaming/CoreStreamProcessor.cs:1689`; `CORE/Streaming/Friends/FriendList.cs:343`; `BOT/Messages/Messages.cs:205-210` |
| INFO | Duplicates are de-duplicated on the client by id (`messages.Find(id)`), but only after full parsing. | `CORE/Streaming/Friends/FriendList.cs:252-262` |
| MINOR | **Client per-message cost for each history item** (runs on the client's single NetworkQueue thread): `new StreamMessage` + `SpixiMessage` parse; O(n) linear `Find` over the channel's in-memory list (O(n²) for a dump); `friend.metaData.setLastReceivedMessageIds` + **`saveMetaData()` = synchronous temp-file write + move, for every message**; a second `saveMetaData()` if the chat is not open; `requestWriteMessages` (debounced); a WebView `insertMessage`; `requestBotUser` for every unknown sender (repeated per message until the reply lands); a local notification check. | `APP/Network/StreamProcessor.cs:431-435`; `APP/Meta/Node.cs:755-807`; `CORE/Streaming/Friends/FriendList.cs:206-231,252,343-347`; `CORE/Streaming/Friends/Friend.cs:1084-1100` |
| MAJOR | **Client receive throttle cliff:** if the client's global `NetworkQueue` holds >5 000 (>10 000) items, **every** connection's recv loop sleeps 500 ms (1 s) after each read. A 10k dump arriving faster than the client can do the per-message file writes above can drop throughput to ~1–2 msg/s for all connections *(hypothesis — measure)*. | `CORE/Network/NetworkRemoteEndpoint.cs:347-363` |

## 3. First-join cost

**Sequence and message counts** (C channels, H_c stored items per channel, U users, A users with avatars):

| Step | Client → bot | Bot → client |
|---|---|---|
| Add | `requestAdd2` | `acceptAddBot` (v0, signed) + bot `avatar` (reads `avatar.jpg` from disk each time) + `msgReceived` |
| Hello on connect | `getInfo` | `info` (+`msgReceived`) — `BOT/Network/StreamProcessor.cs:431-434,556-591` |
| Info is new | `getGroups` (**bot ignores it**, no case), `getUsers` **only if `userCount < 500`**, `getChannels` | U × `user` (≈1.2 KB each: nickData = the user's full signed nick StreamMessage + 4096-bit pubkey) · C × `channel` — `CORE/Streaming/CoreStreamProcessor.cs:2676-2706`; `BOT/Network/StreamProcessor.cs:538-554` |
| Per `user` with avatar | `getAvatar` (added to pending → retried every 2.5 s until acked) | stored avatar StreamMessage (≤500 KB, `File.ReadAllBytes` per request) — `BOT/Network/StreamProcessor.cs:713-735` |
| Per channel | `botGetMessages(null)` | H_c items (≤10 000 incl. reactions & tombstones) |

**Sizes (estimate).** A stored v1 chat item ≈ 16 B id + 2×~46 B addresses + ~512 B RSA-4096 signature + ~20 B framing + text ≈ **0.65 KB + text**. A 10k channel ≈ 7–10 MB on the wire. Keys: `CORE7a/Meta/ConsensusConfig.cs:84`, serialization `CORE7a/Streaming/StreamMessage.cs:371-446`.

**Bot-side crypto per message:** **none.** History items are the stored client bytes, re-serialised with `getBytes()` (a new allocation per item per recipient, plus CRC32). The bot signs only its own control messages (acceptAdd, nick, avatar, delete tombstone). No encryption: every `sendMessage` uses `encryptionType=none` and the encrypt call is commented out. `BOT/Network/StreamProcessor.cs:607-623`.

**Why 10–20 s** *(hypothesis — needs a timed trace)*:
1. **Send-throttle floor (bot Core 7a): ≥ (U + ΣH + A)/1000 s.** A 10k channel alone takes ≥10 s. This is the dominant, deterministic term.
2. Client work per item (the §2 list) is on a single thread and does two synchronous file writes per item. If the queue passes 5 000, the receive-throttle cliff kicks in.
3. **Client user list is O(U²) disk:** each `user` action calls `BotUsers.setUser`, which rewrites the whole `contacts.dat` (`CORE/Streaming/Bot/Users/BotUsers.cs:271-284`, instantiated in `CORE/Streaming/Friends/Friend.cs:259`). For U = 499 that is ~500 rewrites of up to ~300 KB.
4. Avatar fetches: A requests, up to 500 KB each, read from disk per request.
5. Everything on the bot shares one thread with the full-file rewrites from other members' chat (§5). A join during busy traffic waits behind them.

**Throttling:** there is no application-level throttle or pagination in the bot. The only throttles are in Core (the send cap above and the recv cliff on both sides: `CORE7a/Network/NetworkRemoteEndpoint.cs:357-380`). Scale risk: after a bot restart, or when the reaction-id bug triggers, every member pulls the full 10k at once. That is ~10 MB of re-serialised copies **per member**, held in its send queue *(hypothesis: memory spike ≈ members × 10 MB)*.

## 4. Receipts

| Sev | Finding | Evidence |
|---|---|---|
| INFO | **The client never sends `msgReceived` or `msgRead` for bot messages.** The generic ack is skipped when `friend.bot` is true. Chat acks are gated by `!friend.bot`. Read receipts are gated by `!friend.bot`. | `CORE/Streaming/CoreStreamProcessor.cs:916`; `APP/Network/StreamProcessor.cs:412-415,422-442`; `APP/Pages/Chat/SingleChatPage.xaml.cs:1521-1527` |
| INFO | The bot also drops any `msgReceived`/`msgRead` it does receive (`return`). | `BOT/Network/StreamProcessor.cs:166-176` |
| INFO | **Measurable today without a protocol change:** (a) the `forwardMessage(addr)` bool shows whether the recipient was connected at enqueue time. The bot throws it away today. (b) The `last_message_id` in each `botGetMessages` works as a **per-member, per-channel cumulative "caught up to" watermark** at every reconnect. It is noisy because of the reaction/own-message `lastId` bugs (§2). Neither of these proves the message was processed on the device. | `BOT/Network/StreamProcessor.cs:162-164,621` |
| INFO | **What real delivery measurement needs:** a client change. Either send a batched `botAck(channel, lastId)` periodically or on each receive burst, or drop the `!friend.bot` gates and have the bot handle `msgReceived` (1 message per member per message is expensive — prefer a cumulative ack). Fix the client `lastId` semantics at the same time. Setting `requireRcvConfirmation` on the bot side does nothing: clients ignore it for bots. | as above |

## 5. Storage

| Sev | Finding | Evidence |
|---|---|---|
| INFO | **Format:** `Messages/<channelIndex>/messages.ixi` = `int32 version(0) · int32 count · [int32 len · StreamMessage.getBytes(network)]*`. Deleted messages are stored with a 0-length data field. | `BOT/Messages/Messages.cs:83-133` |
| MAJOR | **Whole-channel rewrite on every change, on the single network thread, under `lock(messages)`:** new chat (`addMessage`), reaction (`addMessage`), paid confirmation, and delete. A **delete rewrites twice**: `removeMessage` writes, then the tombstone `addMessage` writes again. At 10k × ~0.7–1 KB ≈ 7–10 MB per write, a few messages/s means tens of MB/s of disk and stalls every member's processing, including keepalives *(throughput ceiling estimate: tens of msgs/s total)*. | `BOT/Messages/Messages.cs:151,180`; `BOT/Network/StreamProcessor.cs:311,324,339,415,493` |
| MAJOR | **Not crash-safe.** `FileMode.Create` truncates in place (no temp file + rename, no fsync; `Flush` only reaches the OS). A crash or power loss mid-write leaves a truncated file. The loader keeps whatever parsed before the exception. The next write then persists the loss. Any non-`IOException` during the write escapes the handler after the file was truncated. Error format strings are wrong (`{0}` twice). The same pattern is used for contacts, channels, groups and settings. | `BOT/Messages/Messages.cs:93-131,60-79`; `CORE7a/Streaming/Bot/Users/BotUsers.cs:45-89`, `Channels/BotChannels.cs:33-77`, `Groups/BotGroups.cs:33-77`; `BOT/Meta/Settings.cs:86-93` |
| MAJOR | **10k cap counts reactions and delete tombstones.** Each is its own record with a unique id, and reaction toggles are not merged. Reaction spam evicts real history. Eviction is `RemoveAt(0)` (O(n)) when `Count > 10000`, per channel. Deleted originals stay in the list as data-less entries and still count. | `BOT/Messages/Messages.cs:143-150`, `BOT/Meta/Config.cs:65` |
| MINOR | **Everything is in RAM:** `Dictionary<int, List<StreamMessage>>` holds up to 10k parsed messages per channel (≈10–15 MB/channel est.). Lookups are linear `Find(id.SequenceEqual)`, 2–3 per incoming chat. `getMessage` for a channel index that does not exist logs, then throws `KeyNotFoundException`. | `BOT/Messages/Messages.cs:15,143,160-170` |
| MINOR | `sendMessage` mutates the stored object (`requireRcvConfirmation=false` for v1 messages) during history replay. That changed value gets persisted on the next rewrite. | `BOT/Network/StreamProcessor.cs:617-620` |
| INFO | **Users/contacts (`contacts.dat`, Core `BotUsers`):** the full file is rewritten on each new user (`setPubKey`, only when new), **every `nick` message** (`setNick` stores the user's whole signed StreamMessage), each avatar set, leave, enableNotifications, ban and kick. The bot passes `limit=false`, so there is no 500 cap on the bot. nickData ~0.6 KB + pubkey ~0.55 KB ≈ 1.2 KB/user → 10k users ≈ 12 MB per rewrite. `userCount` sent to clients = `contacts.Count` **including left and banned**. Once it reaches 500, clients stop fetching the user list at all and fall back to per-sender `getUser` lookups. | `CORE7a/Streaming/Bot/Users/BotUsers.cs:175-252`; `BOT/Network/StreamProcessor.cs:139-156,287-288,480-481,589`; `CORE/Streaming/CoreStreamProcessor.cs:2686-2702` |
| MINOR | **Avatars:** `Avatars/<address>.raw` = the full avatar StreamMessage (≤500 KB), written with `File.WriteAllBytes` (not atomic) and read from disk on every request, with no cache. Bug: the `data == null` check comes after `data.Length`, so a null avatar throws an NRE instead of clearing it. The bot's own avatar is `avatar.jpg`, read from the CWD on every `requestAdd`. | `BOT/Network/StreamProcessor.cs:143-156,713-760`; `CORE7a/Streaming/Bot/Users/BotUsers.cs:195-224`; `BOT/Meta/Node.cs:637-644` |
| INFO | Channels and groups (`channels.dat`, `groups.dat`) are written in full on every admin change. That is rare, but it uses the same non-atomic pattern. | `CORE7a/Streaming/Bot/Channels/BotChannels.cs:190-216`, `Groups/BotGroups.cs:169-195` |
| INFO | **Core HEAD differences:** `BotUsers` → `OrderedDictionary` + a v1 file header + `getOwner()`. `BotInfo` gains `randomId`/`hideParticipantAddresses` and a **new constructor signature**. `NetworkClientManager.sendToClient`/`broadcastProtocolMessage` drop the `helper_data` argument. → **The bot does not compile against HEAD as-is** (`BOT/Network/StreamProcessor.cs:589`, `BOT/Meta/Node.cs:460,614`). | `git diff f6fb55b 1ff5435 -- Streaming/Bot Network` |

## 6. Size limits

| Sev | Finding | Evidence |
|---|---|---|
| INFO | Protocol-level max for any message, `s2data` included: **50 000 000 B**, enforced on receive (7a and HEAD). | `CORE7a/Meta/CoreConfig.cs:77`, `CORE7a/Network/NetworkRemoteEndpoint.cs:1188-1190,1397`; `CORE/Meta/CoreConfig.cs:83` |
| MAJOR | **The bot enforces no chat size limit.** A free bot will store, rewrite to disk on every later change, and broadcast a ~50 MB message. Price scales `messageCost × len / 1000` only when cost > 0. **Quotas are dead code:** `addActivity` never inserts the new `Quota`, `exceededQuota` returns `false` on every path, and nothing calls it anyway → there is **no rate limit**. | `BOT/Network/StreamProcessor.cs:374,399`; `BOT/Network/QuotaManager.cs:35-58,71-78` |
| INFO | Client-side limits (Core HEAD): `maxChatMessageSize = 64 000` chars, `maxMessageIdSize = 16` (ChatStreamMessage only). A bigger message stored by the bot is rejected by HEAD clients on every replay. Avatars < 500 000 B on both sides. | `CORE/Meta/CoreConfig.cs:235-240`, `CORE/Streaming/Friends/FriendList.cs:192`; `APP/Network/StreamProcessor.cs:486` |

## 7. Threading / locking hazards

| Sev | Finding | Evidence |
|---|---|---|
| MINOR | `Messages.sendMessages` holds `lock(messages)` while enqueuing up to 10k sends, each taking `lock(connectedClients)` plus a linear client scan. Nothing else can store or relay while a dump runs. Lock order is always messages → connectedClients: **no deadlock found**. | `BOT/Messages/Messages.cs:196-220` |
| MAJOR (paid bots) | `pendingMessages` (`List<StreamMessage>`) has **no lock**. It is mutated on the network thread (`onChat` Add/Find, payment Find) and on the maintenance thread (1 s loop) / TIV callback thread (`confirmMessage` Find/Remove). That can corrupt the list or throw. Two threads can both confirm the same message → double broadcast (the store de-dups by id). | `BOT/Network/StreamProcessor.cs:17,405-411,453,486-496`; `BOT/Meta/Node.cs:405-414,617-623`; `BOT/Meta/SpixiBotTransactionInclusionCallbacks.cs:15-30` |
| MAJOR (paid bots) | **Pending paid messages:** unbounded, never expire (even when the tx is dropped as expired), not persisted (lost on restart after the client got its `msgReceived`). A payment for a message the bot no longer holds is logged and ignored. The user may have paid for nothing *(hypothesis on who broadcasts the tx)*. "Confirmed" = the tx was seen from ≥2 nodes, not included in a block. Every unpaid chat on a paid bot = one permanent RAM entry (DoS). | `BOT/Network/StreamProcessor.cs:399-412,453-459`; `BOT/Meta/Node.cs:601-624` |
| MINOR | `QuotaManager`: `exceededQuota`/`addValidPayment` read and write the dictionary without the lock (moot, since it is always empty). | `BOT/Network/QuotaManager.cs:35-58,102-123` |
| MINOR | The push thread iterates `Node.users.contacts` and `NetworkServer.connectedClients` **without locks**. A concurrent join throws "collection modified". The outer catch then discards that cycle, after `sendPushNotification` was already reset. | `BOT/Network/PushNotifications.cs:62-77` |
| MINOR | `BotContact.status`/`sendNotification` are mutated from API/network threads with no synchronisation. | `BOT/API/APIServer.cs:235,245`; `BOT/Network/StreamProcessor.cs:480` |

## 8. Push notifications

| Sev | Finding | Evidence |
|---|---|---|
| INFO | **Trigger:** `Messages.addMessage` sets the bool `sendPushNotification = true` for new chat/paid messages only (reactions and deletes pass `false`). | `BOT/Messages/Messages.cs:152-155` |
| MAJOR | **Effectively disabled:** the `enableNotifications` action always stores `false`. The `true` branch is commented out. `BotContact.sendNotification` defaults to `false`. Only contacts persisted `true` by an older build ever get pushes. | `BOT/Network/StreamProcessor.cs:474-482`; `CORE7a/Streaming/Bot/Users/BotContact.cs:33` |
| MAJOR | **Cadence:** the loop checks the flag, then sleeps **30 min** (`pushNotificationInterval`). → At most one push per member per 30 min, with up to 30 min latency, regardless of volume. | `BOT/Network/PushNotifications.cs:54-98`; `BOT/Meta/Config.cs:55` |
| INFO | **Recipients:** contacts with `status==normal && sendNotification` that are **not** currently connected. There is a 100 ms sleep between users (10k users ≈ 17 min per cycle). | `BOT/Network/PushNotifications.cs:65-89` |
| INFO | **Payload:** `POST {pushServiceUrl}/push.php` form `tag=<member address>&data=&pk=&push=True&fa=` → **no content, no sender (`fa` empty), no bot identity.** On the redesign's Android push gate, an empty `fa` = `ShowRaw` (it cannot be tied to the bot or muted per chat). | `BOT/Network/PushNotifications.cs:101-126`; `Spixi-redesign/Spixi/Platforms/Android/SPushService.cs:616-700` |
| INFO | **Server URL:** hard-coded `readonly` `https://ipn.ixian.io/v1`. The Spixi app uses **`/v2`**. | `BOT/Meta/Config.cs:48`; `APP/Meta/Config.cs:36` |
| MAJOR | **Failure behaviour:** `while (!sendPushMessage(...)) Thread.Sleep(1000);` → if the IPN is down or returns anything but `"OK"`, the thread **retries the same member forever** and never reaches the others. It uses a new `HttpClient` per call and blocks on `.Result` with no timeout set. | `BOT/Network/PushNotifications.cs:79-82,108-124` |

## 9. Other causes of lost / late / duplicate messages or slow performance

| Sev | Finding | Evidence |
|---|---|---|
| MAJOR | Chat in **channel 0** is acked, then silently dropped. Channel indices start at 1 on the bot, but a client or bug sending channel 0 gets a false "delivered". | `BOT/Network/StreamProcessor.cs:379-382` |
| MAJOR | `onChat` → `getMessagePrice(message.sender)` → `Node.users.getUser(sender).getPrimaryRole()` throws an NRE when the claimed sender is not a known user. This happens on a race with `requestAdd`, and on spoofed senders. The exception is swallowed at `BOT/Network/NetworkProtocol.cs:303-306` after the ack → silent loss. | `BOT/Network/StreamProcessor.cs:360-375,399` |
| MINOR | Duplicate-work loop: the bot echoes each chat back to its sender, who parses it and discards it as "already in message list". | §1b; `CORE/Streaming/Friends/FriendList.cs:259-262` |
| MINOR | Client `requestBotUser` fires per message from an unknown sender with no in-flight de-dup. During a history dump from a big bot (userCount ≥500, so no user list) this multiplies the `getUser` round trips. | `CORE/Streaming/Friends/FriendList.cs:223-227` |
| MINOR | Core 7a `NetworkQueue` uses `List.RemoveAt(0)`, and the high-priority queue has no size cap. The recv throttle (>5k queued → 500 ms sleeps on **all** connections) applies to the bot too, so one slow disk rewrite can back-pressure every member. | `CORE7a/Network/NetworkQueue.cs:280-297,361-395`; `CORE7a/Network/NetworkRemoteEndpoint.cs:357-380` |
| INFO | The bot drops every client after 10 s of silence (`pingTimeout`). Mobile clients that are backgrounded or suspended disconnect → no live delivery until they reconnect and run the catch-up (with the §2 gap race). | `CORE7a/Meta/CoreConfig.cs:81,85`; `CORE7a/Network/NetworkRemoteEndpoint.cs:498-505` |
| INFO | Paid messages are relayed only after the payment confirms (≥2 node sightings, polled every 1 s). They are appended at the end of the channel **out of chronological order**. | `BOT/Meta/Node.cs:617-623`; `BOT/Network/StreamProcessor.cs:486-496` |

---

## Verified / Corrected / Refuted — quick-scan claims

| # | Claim | Verdict | Notes / evidence |
|---|---|---|---|
| 1 | Live fanout only to directly-connected clients | **Verified (+ worse)** | `forwardMessage(code,bytes)` sends to every inbound connection with presence, including non-members, banned users and the sender. No channel filter, no relay, no queue. `CORE7a/Network/NetworkServer.cs:427-458` |
| 2 | History via `botGetMessages` resends every stored message one by one | **Verified (+ detail)** | One `s2data` per item after `lastId`. Includes reactions and delete tombstones. **Full ≤10k resend when `lastId` is unknown**, which the client's `lastId` bugs trigger often. Capped at ~1k msg/s by Core 7a. `BOT/Messages/Messages.cs:196-220` |
| 3 | Channel file rewritten entirely on every message/reaction/delete | **Verified (+ detail)** | A delete rewrites **twice**. The write is not atomic and has no fsync. It runs on the single network thread. `BOT/Messages/Messages.cs:151,180`; `BOT/Network/StreamProcessor.cs:311,324` |
| 4 | Paid messages sit in an unbounded in-memory list | **Verified (+ worse)** | No lock, no expiry, not persisted, and already acked to the sender. `BOT/Network/StreamProcessor.cs:17,62-83,410` |
| 5 | Bot sends set `requireRcvConfirmation=false` | **Corrected** | It is set only when `msg.version >= 1`. Bot-originated messages are v0 (default ctor), so they keep `true`. Live relays forward the raw client bytes unchanged. Only history replays of v1 client messages are flipped, and that mutates the stored copy. The flag is moot: clients never ack bot messages. `BOT/Network/StreamProcessor.cs:617-620`; `CORE7a/Streaming/StreamMessage.cs:101-105`; `CORE/Streaming/CoreStreamProcessor.cs:916` |
| 6 | Push notifications disabled and loop every 30 min | **Verified (nuanced)** | Opt-in is hard-wired to `false`, so only legacy contacts with `true` get pushes. The loop sleeps 30 min. The payload is empty and has no sender. It targets IPN `/v1` (the app uses `/v2`). A failing IPN makes it retry forever and block the thread. `BOT/Network/StreamProcessor.cs:474-481`; `BOT/Network/PushNotifications.cs:54-126` |

## Open questions (BE engineer / live test)

1. **Timed join trace:** capture bot and client logs for a fresh join on a bot with a known H/U/A. Split the time between the bot send throttle (Core 7a), client per-message `saveMetaData` I/O, the recv-throttle cliff, and the user-list O(U²) rewrites.
2. **Gap race (§2):** reproduce by posting live messages while a member reconnects. Confirm the messages between the old `lastId` and the first live message are never shown.
3. **Reaction `lastId` bug:** confirm that reacting to an older message makes the next reconnect re-download the whole channel (count `s2data` frames).
4. Is the `IPAddress ==` per-IP guard really inert? Test two clients from one public IP.
5. Practical concurrent-connection ceiling on the bot host (Core 7a idle polling, `ulimit -n`, memory per endpoint). What is the CPU cost of a 10k-member broadcast?
6. Is IPN `/v1/push.php` still served, and does it accept `fa=""`? What does the iOS/Android client display for such a push?
7. Payment path: who broadcasts the client's payment tx? If the bot restarts between `getPayment` and `payment`, is the user charged for a message the bot discarded?
8. Should the rework move to Core HEAD? HEAD removes the send throttle (faster joins) but changes the overflow semantics to drop-newest and breaks compilation (`BotInfo` ctor, `sendToClient` signatures).
9. Confirm the mobile behaviour: does iOS/Android keep the bot TCP connection while backgrounded, or is every foreground a full reconnect + catch-up?
10. Intended semantics for banned/kicked/left members (fanout, history, posting). None of these are enforced today.
