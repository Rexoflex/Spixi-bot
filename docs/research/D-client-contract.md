# D — Bot ↔ client contract, as the CLIENTS use it

Read-only research, 2026-10-01. Purpose: contract tests for the reworked Spixi group-chat bot against
(a) the store app and (b) the redesign.

| Tag | Tree | Ref |
|---|---|---|
| **U** | Spixi store app | `Spixi-upstream` @ `0e85a4b` (tag spixi-0.9.22) |
| **R** | Spixi redesign | `Spixi-redesign` @ `5d48669` |
| **B** | Bot | `Spixi-bot` @ `a5a3442` (2025-10-21) |
| **C** | Ixian-Core | `Ixian-Core` @ `1ff5435` (= `097341a` / xcore-0.9.8k for all Streaming code) |

**Which Core each client runs.** Both apps import `../../Ixian-Core/IXICore.projitems` (U `Spixi.csproj:271`, R `Spixi.csproj:383`), so the Core is whatever was checked out at build time.
- R builds against `097341a` (0.9.8k) — documented in R `docs/prelaunch-audit-handoff.md:51`.
- **HYPOTHESIS:** U 0.9.22 was built against xcore-0.9.8j. Tag spixi-0.9.22 = 2026-06-16 17:05 and v0.9.8j = 2026-06-16 17:46. U also calls the `ChatStreamMessage` overloads, which need Core ≥ `1f36c7e` (post-0.9.8h).
- `CoreStreamProcessor.cs` is **byte-identical** between j and k. Only `FriendList.cs` differs (commit `5643e5b`, see §6). So every `C` line number below holds for both clients.

⚠ **The bot does not compile against current Core.** `B StreamProcessor.cs:589` calls the old 10-arg `BotInfo(version, serverName, …)`. Core `8bf3d5a` (2026-05-17) replaced it with an 11-arg ctor that has **no serverName** (`C Streaming/Bot/BotInfo.cs:33`). The rework must set `bi.serverName` explicitly, or clients set the bot's nickname to `""` (`C CoreStreamProcessor.cs:2682`).

---

## 0. Transport facts every test must assume

| Fact | Evidence |
|---|---|
| The client talks to the bot **directly**. The bot presents as node type `'C'`. Bot-bound messages are never sent via S2/push (`send_to_server=false`). | B `Meta/Node.cs:261`; C `CoreStreamProcessor.cs:124-137`; C `PendingMessageProcessor.cs:486-488` |
| The client reconnect loop runs every 2.5 s (`connectToBotNodes`). | U `Meta/Node.cs:293-311,362,375`; R `Meta/Node.cs:470` |
| **Every new connection** to a `'C'` friend that is a bot sends `getInfo`. That starts the whole resync cascade (§4). | U/R `Network/NetworkProtocol.cs:106-112` |
| All client→bot traffic is `encryptionType=none` and signed with the wallet key. | C `CoreStreamProcessor.cs:314-318` (sendSpixiMessage), `:2306-2310`, `:2348-2352`, `:2826-2849` |
| The bot's own messages are `none`. Only acceptAddBot, nick, avatar and msgDelete are signed. botAction is **not** signed. | B `StreamProcessor.cs:498-513` (no sign), `:672`, `:708`, `:757`, `:322` |
| The bot broadcasts relays with `NetworkServer.forwardMessage(code, bytes)`. That goes to **every** connected client, including the sender, non-members and banned users. | B `:326,340,418,494`; C `Network/NetworkServer.cs:431-456` |
| A relayed member message is the member's **original StreamMessage**: `sender`=member, `recipient`=bot. The bot does not rewrite it. | B `:418` (`raw_message`), `:494`, `Messages.cs:217` |
| Client routing: `FriendList.getFriend(message.recipient)` is a bot ⇒ `group_sender_address = message.sender` and `sender_address = bot`. | C `CoreStreamProcessor.cs:667-677` |
| If a message is addressed to a friend that is **not** a bot, the client drops it. If it is addressed to a non-friend that is not me, the client also drops it. That is the non-member case. | C `:678-688` |
| A client never sends `msgReceived` or `msgRead` to a bot. It also sends no receipt for bot-relayed chat. The bot therefore gets no delivery information. | C `:916`; U `Network/StreamProcessor.cs:412,422`; U `SingleChatPage.xaml.cs:1521`; R `SingleChatPage.xaml.cs:3604` |
| SpixiMessage envelope: `int type · int len · data · [int channel] · [IxiBytes groupAddress · IxiBytes groupSenderAddress]`. The tail fields are optional on read. Core-k writers always emit channel and two empty IxiBytes. | C `Streaming/SpixiMessage.cs:95-135,141-163`; `Utils/IxiUtils.cs:224-256` (0-length ⇒ null) |

---

## 1. Message-code inventory

Legend: **C→B** = client to bot, **B→C** = bot to client. "Relay" = the bot forwards a member's original message.

### 1a. SpixiMessageCode

| Code | Dir | Client sends when (file:line) | Payload (class · serialization) | Client on receipt | Bot does |
|---|---|---|---|---|---|
| `requestAdd2` (40) | C→B | The user adds the bot address (contact request / "Join community"). `sendContactRequest` sets id `{0}`, `none`, signed. U `HomePage.xaml.cs:600`, R `HomePage.xaml.cs:2284`, C `:2481-2507` | `byte version(1) · IxiBytes pubkey` | n/a | Checks `Address(pubkey)==sender` and the signature. Then sends `acceptAddBot` + its own `avatar`. B `:100-114` |
| `requestAdd` (3) | C→B | Legacy clients only (payload = raw pubkey). Neither U nor R sends it. | raw pubkey | n/a | Same as above. B `:87-98` |
| `acceptAddBot` (19) | B→C | — | data = AES key (the bot sends **null**). id `{1}`, `none`, signed. | Accepted **unencrypted from a non-bot friend** (whitelist C `:766-779`). `handleAcceptAddBot`: if `handshakeStatus>1` it returns false. Otherwise it sets `aesKey=data`, calls `setBotMode()`, sets `handshakeStatus=3`, sends `nick` + `getInfo` (C `:2072-2098`). **Signature NOT verified.** App adds an "accepted/connected" row and calls `convertToBot` (U `:366-378`, R `:567-579`). | B `:653-675` |
| `nick` (2) | C→B | Right after acceptAddBot (`sendNickname`, id `{5}`, signed). C `:2093,2294-2321` | UTF-8 nick | **Bot's own nick:** verified vs the bot pubkey, then `setNickname(bot)`. **Relayed member nick** (the bot answers `getNick` with the stored original StreamMessage): if the member is not in the roster or has no pubkey ⇒ `requestBotUser` + **drop**. Otherwise it is verified vs the **roster** pubkey (C `:1004-1047`). App: `updateGroupChatNicks` (U `:480-491`, R `:738-749`). | Stores the whole StreamMessage as `nickData`; sets pubkey from `endpoint.serverPubKey`. B `:138-141` |
| `getNick` (1) | B→C / C→B | The client never sends it to a bot (callers are only in handleRequestAdd, C `:1779,1850`). | data = address, or 1 byte = "self" | C `:990-1002` → sends own `nick` | Sent to unknown chat senders, id `{3}` (B `:384-388,763-777`). On receipt: returns the member's stored nickData, or the bot's own name (B `:677-711`). |
| `avatar` (24) | both | After join the bot sends its own (id `{6}`). The client sends its own only on request. | image bytes (<500 000) | Same verify pattern as `nick` (C `:1068-1094`). App: resize 128 → `FriendList.setAvatar(bot, …, group_sender)` (U `:388-399`, R `:590-601`). | Stores the raw StreamMessage if <500 000 B. B `:143-156` |
| `getAvatar` (25) | both | For a roster user with `hasAvatar` and no local file. id `[2]+addr`, **retried** (pending). C `:2710-2717,2428-2459` | `Address.addressWithChecksum` | C `:1049-1066` → sends own avatar | Sends the stored member avatar StreamMessage, or its own. B `:713-760` |
| `getPubKey` (26) / `pubKey` (27) | C→B / B→C | **Never sent.** The only caller is in a commented block (C `:969-983`). | address / pubkey | `handlePubKey`: bot-only, `users.setPubKey` (C `:956-965,1697-1714`) | Answers when the user is known (B `:116-128`) |
| `chat` (0) | both | `onSend` → `sendChatMessage(friend, fm, selectedChannel)`. The StreamMessage id = the local FriendMessage id. **Retried until `msgReceived`.** U `SingleChatPage.xaml.cs:610-647`, R `:1586…`, C `:2201-2209` | UTF-8 text, SpixiMessage.channel = bot channel | Core does **no signature check** (C `:966-987`). `Node.addMessageWithType(message.id, standard, bot, spixi.channel, text, false, group_sender, message.timestamp)` (U `:410-416`, R `:613-626`). **The channel must exist** locally, or the message is dropped (C `Friend.cs:899-907`, `FriendList.cs:247-252`). An own echo is deduped by id. | Drops channel 0 (B `:379-382`). Sends `msgReceived(id, channel)` (B `:75-77`). Unknown sender ⇒ getNick + getAvatar. Dedups by id. Paid ⇒ `getPayment`. Else store + broadcast raw (B `:377-420`). |
| `chatStream` (53) | B→C (relay) | Neither client sends it to a bot (R `:1615` avoids it on purpose). | `ChatStreamMessage` (IxiBytes id · IxiBytes text · varint seq · byte isStream) | Handled for bots: append/replace by `(MessageId, Sequence)` (U `:418-445`, R `:628-657`, C `FriendList.cs:256-326`) | **Not handled** (default: warn, B `:195-197`) |
| `botGetMessages` (20) | C→B | After every received `channel` action: `sendGetMessages(bot, channel.index, lastReceivedMessageIds[channel])`. Not pending, no fixed id. C `:2660-2673,2553-2566` | data = last message id (null ⇒ everything). SpixiMessage.channel = channel | n/a | Resends every stored non-deleted message after `FindLastIndex(id)`, one s2data each. **No cap** (store holds up to 10 000/channel). B `Messages.cs:196-220`, `Config.cs:65` |
| `botAction` (32) | both | see §1b | `SpixiBotAction`: `short action · int len · data` (C `Bot/SpixiBotAction.cs`) | C `:1338-1347` → `onBotAction` (C `:2645-2736`). Returns false ⇒ the message is dropped. App-level `onBotAction` handles only kick/ban (U `:790-808`, R `:1283-1302`) | B `:422-484` |
| `msgReceived` (9) | B→C | never C→B | msg id; channel = message channel (chat) or **-1** (all other codes) | `handleMsgReceived`: removes the pending message. Single-byte ids `{0..14}` are handshake/ignored (C `:486-608`). Channel -1 ⇒ `setMessageReceived(-1)` ⇒ "channel -1 does not exist" error log (harmless noise). App: UI tick update (U `:447-457`, R `:659-697`) | B returns without action (`:166-170`) |
| `msgRead` (8) | — | Never sent to bots | — | — | ignored (B `:172-176`) |
| `msgDelete` (33) | both | Context menu "deleteMessage" (U `:1034-1043`, R `:2494-2519`). The client does **not** delete locally for bots; it waits for the echo. C `:2852-2857` | msg id; channel | The bot's echo is **signature-verified vs the bot pubkey** (C `:1349-1366`). `friend.deleteMessage` + the cursor moves to the delete-message's id (C `:1665-1676`). App UI: U uses channel **0** (`:459-462`); R resolves the channel (`:699-713`). | Allowed if admin or author. Blanks `data`, stores a signed bot `msgDelete` (sender=recipient=bot, GUID id), broadcasts. B `:301-328`, `Messages.cs:172-183` |
| `msgReaction` (34) | both | "like" (U `:1045-1058`, R `:2521-2540`). R also "tip:<txid>" in bot rooms (R `:2123-2170`, #348 W8); U blocks tips for bots (U `:942-947`). C `:2882-2887` | `ReactionMessage`: `int len · msgId · string reaction` | Relayed: needs the member pubkey in the roster, else `requestBotUser` + **drop** (not retried). Verified vs the roster pubkey. `addReaction` + cursor := **target** msgId (C `:1368-1398,1678-1695`) | Relays the original if the target exists. Also **stores the reaction as a history message** (B `:330-341`) |
| `msgTyping` (35) | — | Core `sendTyping` returns early for bots (C `:2889-2895`) | — | Dropped for bots (C `:1417-1422`) | not handled |
| `msgReport` (39) | C→B | Context menu "report" in bot rooms; the client also deletes locally (U `:1026-1032`, R `:2464-2470`, C `:2859-2880`) | msg id; channel | — | **Not handled** (default warn) |
| `leave` (37) | C→B | U: sets `pendingDeletion`, then `sendLeave` (U `SingleChatPage:293-313`, `ContactDetails:122-132`). R: `sendLeave`, then **immediate `removeFriend`** (R `SContacts.cs:70-119`, #567). C `:2916-2922` | null | n/a | Marks the user `left`, sends `leaveConfirmed` (B `:282-299`) |
| `leaveConfirmed` (38) | B→C | — | null | U: `pendingDeletion` ⇒ `removeFriend`, then `StreamClientManager.getClient(...)`, which **recurses infinitely ⇒ StackOverflow, process death** (C `:1400-1415`; C `Network/StreamClientManager.cs:118-121`, present in j and k). R: the friend is already removed ⇒ the confirm is dropped as unknown. | — |
| `requestFunds`/`sentFunds`/`transaction*`, `appRequest*`, `appData`, `appProtocol*`, `file*`, `requestCall`… | — | Blocked for bots in both UIs: send/request IXI (U `:372-404`), files (U `:651-656`), apps (U `:834-838`); same in R | — | Would be processed generically if received | not handled |
| `createGroup`, `keys*`, `acceptAdd*`, `open/closeSecureConnection` | — | Not used with bots | — | — | — |

### 1b. SpixiBotActionCode (inside `botAction`)

| Action (value) | Dir | Client trigger | Payload | Client on receipt (C `onBotAction`) | Bot |
|---|---|---|---|---|---|
| `getInfo` (0) | C→B | After acceptAddBot (C `:2095`) and **on every new connection** (U/R `NetworkProtocol.cs:109-112`). id `{11}`, not pending (C `:2568-2584`, `:127-136`) | null | — | `setPubKey`; send `info` (B `:431-434`) |
| `info` (1) | B→C | — | `BotInfo`: `short ver · string serverName · string description · string cost · long settingsGeneratedTime · bool admin · int defaultGroup · int defaultChannel · [bool sendNotification] · [long userCount] · [IxiBytes randomId · bool hideParticipantAddresses]` | If `settingsGeneratedTime` changed (or first time): **replace botInfo** (incl. the client's local mute flag), set the bot nickname = `serverName`, send `getGroups`, and send `getUsers` if `userCount<500`. Else, if `userCount` changed: update and maybe `getUsers`. **Always** send `getChannels` (C `:2676-2708`) | `sendInfo` (B `:556-591`) |
| `getChannels` (2) | C→B | Every `info` (C `:2586-2602`, id `{12}`) | null | — | one `channel` per channel (B `:520-531`) |
| `channel` (3) | B→C | — | `BotChannel`: `int index · string name` | `channels.setChannel(name, ch)`, then **immediately `botGetMessages(index, cursor)`** (C `:2660-2674`). Channels are never deleted locally (TODO C `:2705`) | `sendChannel` (B `:515-518`) |
| `getUsers` (4) | C→B | If `userCount<500` (C `:2604-2620`, id `{13}`) | null | — | one `user` per `normal` user (B `:538-554`) |
| `user` (5) | B→C | — | `BotContact(nick_as_string=false)`: `int len · nickData (whole signed StreamMessage) · int len · pubkey · [string role · bool hasAvatar · bool sendNotification · short status]` (tail try/caught) | `users.setUser` (cap 500, oldest evicted, C `BotUsers.cs:271-281`); `getAvatar` if `hasAvatar` and no local file. **nickData signature not checked** (C `BotContact.cs:58-75`). A null pubkey throws ⇒ caught, dropped | `sendUser(bc.getBytes(false))` (B `:533-536`) |
| `getUser` (13) | C→B | For **each** message from a sender not in the roster (C `FriendList.cs:217-229`; `:1017,1081,1379`). id `[3]+addr`, not pending, **no dedup** (C `:2462-2479`) | `addressWithChecksum` | — | `sendUser(getUser(addr))`. An unknown address ⇒ `getUser` null ⇒ **NRE in the bot** (B `:440-442`) |
| `getGroups` (6) | C→B | After every info change (C `:2622-2638`, id `{14}`) | null | — | **not handled** |
| `group` (7) | — | — | — | **no case** ⇒ ignored | never sent |
| `getPayment` (8) | B→C | — | `StreamTransactionRequest`: `int len · msgId · string cost` | `onGetPayment` = **`return;` stub** in j and k (C `:2738-2742`). Returns true; no action | Sent on each paid chat (B `:399-412`) |
| `payment` (9) | C→B | Never (stubbed) | `StreamTransaction`: `int len · msgId · int len · tx` | — | Validate, then `addPendingLocalTransaction`; on inclusion `confirmMessage` ⇒ store + broadcast (B `:444-472,486-496`) |
| `kickUser` (10) / `banUser` (11) | C→B (admin UI) and B→C (would be) | Admin menu (U `:909-926`, R `:2008,2018`), signed | addressWithChecksum | Core returns true. App adds a local "kicked"/"banned" row: U on **channel 0** (dropped if 0 is not a bot channel), R on `defaultChannel` (U `:800-806`, R `:1290-1300`) | **not handled** (kick/ban do nothing server-side) |
| `enableNotifications` (12) | C→B | Mute toggle (U `:248-259`, R `:2033-2037`), signed; also sets the local `botInfo.sendNotification` | 1 byte | — | **Always stores false** (`//send_notifications = true;` B `:474-482`) |

---

## 2. Bot detection, storage, unknown codes

**Detection.** `Friend.bot` is a flag with a private setter. It is set only by `setBotMode()` (C `Friend.cs:198,250-254`), which `handleAcceptAddBot` calls. It is persisted in the friend file (C `Friend.cs:307,330`). `FriendType` stays **Normal** (there is no Bot type, C `Friend.cs:151-157`). R `Utils.hidesParticipants` returns false for bots because `type != Group` (R `Utils.cs:90-96`).

**Storage (per bot friend, C `Friend.cs:250-265`).** `accounts/<bot>/contacts.dat` (BotUsers, 500-cap) · `groups.dat` · `channels.dat` · message history per channel (LocalStorage) · `metaData.botInfo` + `metaData.lastReceivedMessageIds[channel]` (C `Friend.cs:26-27,132-143`). Every received message calls `saveMetaData` + `requestWriteMessages` (C `FriendList.cs:343-349`).

**Unknown codes.**

| Case | Behaviour (both clients) | Evidence |
|---|---|---|
| Unknown `SpixiMessageCode` int from the bot | The enum cast succeeds. Core `default:` returns the response. The app switch has no default ⇒ **silently ignored**. No receipt (bot), no log, no crash. | C `:1589-1590`; U `:229-536`; R same |
| Unknown code **with `groupAddress` set** | Routed to the group path ⇒ "invalid message for group" / `ValidateAndGetGroup` fails ⇒ dropped | C `:784-832` |
| Unknown `SpixiBotActionCode` | `onBotAction` returns false ⇒ `receiveData` returns null ⇒ dropped silently | C `:2733-2735,1339-1342` |
| Malformed SpixiMessage | The ctor catches and sets `type=0` (**chat**), data null. U then throws in `GetString(null)`, caught in the outer catch. R `safeString` stores an **empty chat row** (R `:613-616,1310-1320`) | C `SpixiMessage.cs:126-134` |
| Chat for an unknown channel | Dropped with a warn | C `Friend.cs:903-907`, `FriendList.cs:247-252` |
| Chat > 64 000 chars | `addMessageWithType` throws ⇒ caught ⇒ dropped | C `FriendList.cs:192-195`, `CoreConfig.cs:235` |

---

## 3. Validation and authorship

| Message from bot room | Signature checked? | Key used | Evidence |
|---|---|---|---|
| `acceptAddBot` | **No** | — | C `:1324-1336`, `:2072-2098` |
| `botAction` (info/channel/user/getPayment/kick/ban) | **No** | — | C `:1338-1347` |
| Relayed `chat` / `chatStream` | **No** (the check is commented out) | — | C `:966-987` |
| Relayed `nick` / `avatar` / `msgReaction` | Yes | member pubkey **from the bot-supplied roster** | C `:1015-1024,1079-1088,1377-1386` |
| Bot's own `nick`/`avatar` | Yes, if the pubkey is known | bot pubkey | C `:1007-1014,1071-1078` |
| Bot `msgDelete` | Yes | bot pubkey | C `:1350` |
| BotContact.nickData (inside `user`) | No | — | C `BotContact.cs:58-75` |

**Author of a relayed message = `StreamMessage.sender`** (C `:673`). It is stored as `FriendMessage.senderAddress` and `senderNick` (roster lookup at receive time, C `FriendList.cs:207-231`). The client trusts the bot fully for authorship, the roster (pubkeys, nicks, roles) and all botActions. **HYPOTHESIS:** any party that can deliver an s2data with `sender`=bot to the client could inject botActions or forged chat, because these are unsigned and unencrypted. Today the client only receives bot traffic over direct connections, which limits this. Members' original chat messages ARE signed by the client (C `:314-318`), so verification is technically possible.

⚠ **Core-k author loss.** `FriendList.addMessageWithType` sets `set_sender_address = null` when `friend.type == Normal` (C `FriendList.cs:239-243`, commit `5643e5b`, not in j). Bot rooms are Normal ⇒ the redesign's Core **stores no author**. R works around this app-side by restoring the address (R `Meta/Node.cs:1095-1123`). The dedup "sent by a different user" check is then skipped (C `FriendList.cs:275-281`). Known issue: R `docs/be-cutover-brief.md:192` [Q1-ESC]; the one-word fix is `&& !friend.bot`.

---

## 4. Fetch cadence and history processing

**Cascade (both clients).** New connection ⇒ `getInfo` ⇒ `info` ⇒ always `getChannels` (+ `getGroups`, + `getUsers` if `<500`, only when `settingsGeneratedTime` changed) ⇒ one `channel` per channel ⇒ one `botGetMessages(ch, cursor)` per channel. Nothing runs on a timer apart from the 2.5 s reconnect attempt. Opening the chat does **not** send anything to the bot. U/R `onLoad` only **waits** for `botInfo` + `defaultChannel` (U `:510-521` Thread.Sleep up to 5 s on the UI thread; R `:1312` moved off the UI thread by #619).

**Cursor.** `lastReceivedMessageIds[ch]` = id of the last message the client **added**, which includes **its own sent messages** (C `FriendList.cs:343`), the target id of a reaction (C `:1689`) and the id of a bot delete-message (C `:1669`). If the bot cannot find that id (own paid/unrelayed message, channel-0 message, pruned beyond 10 000, bot DB reset), `FindLastIndex=-1` ⇒ **the bot replays the whole channel (≤10 000 messages)** (B `Messages.cs:205-218`).

**Per-message cost (client).** One s2data ⇒ full `receiveData`. Per message: roster lookup (+ `getUser` if unknown), dedup `Find` over the cached list, `saveMetaData` (file write), `requestWriteMessages`, UI push if the chat is open (one `EvaluateJavaScriptAsync` per message; R batches only its own `loadMessages`, #801), notification check. Dedup covers only the **in-memory window**: the default 100, and R replaces it with 50 when loading (`Config.messagesToLoad=50`, C `Friend.cs:899-916`). **HYPOTHESIS:** a replay of older messages outside the window creates duplicates. See R `docs/bot-group-load-freeze.md` §2-3 and BE Q9.

**Client caps.** Roster 500 (C `BotUsers.cs:202,278`). `getUsers` only if `userCount<500`. No cap on the replay size. No rate limit on `getUser`.

---

## 5. Paid bot

| Step | Store (U) | Redesign (R) |
|---|---|---|
| Display | `setChatMode(3, cost, "chat-message-cost-bar")`. Bar shown if the string `cost != "0.00000000"` (U `SingleChatPage:523-526`; U `html/js/chat.js:152-176`) | Numeric parse; shown if >0 (R `SingleChatPage:1341-1351`; R `src/shells/chat.html:4748-4759`) |
| Price | `cost * len / 1000`. The client balance check uses `str.Length` (chars) (U `:618-635`, C `Friend.cs:944-947`). `payableDataLen` = SpixiMessage byte length. The bot uses `message.data.Length` (B `:360-375,399`) | same (R `:1586-1600`) |
| On `getPayment` | **No-op stub** (C `:2738-2742`) | same |
| Result | The bot already sent `msgReceived` (the message shows as delivered). It keeps the message in memory `pendingMessages` forever. **The message is never relayed.** Paid rooms are non-functional on both clients today. | same |
| `rec.paid` badge | shown if `transactionId != ""`. This is never set. | same (R `chat.html:1877`) |

---

## 6. Store vs redesign — differences that matter to the bot

| Area | U (store) | R (redesign) | Test impact |
|---|---|---|---|
| Core version | j (hypothesis) | k (`097341a`) | Author nulling (§3) only in R's Core. R restores it app-side. |
| Leave | `pendingDeletion`, waits for `leaveConfirmed` ⇒ **Core StackOverflow crash** | sendLeave, then immediate removeFriend. `leaveConfirmed` is ignored. The leave notice is best-effort (the pending queue is purged when the friend is gone, C `PendingMessageProcessor.cs:209-221`). | Sending `leaveConfirmed` **kills U**. R may never deliver `leave`. |
| Tips in bot rooms | blocked | `msgReaction "tip:<txid>"` via the bot + a direct tx to the member (#348) | The bot must relay arbitrary reaction strings |
| kick/ban local row | channel 0 | defaultChannel | UI only |
| App-level channel for receipts/delete/reaction UI | literal 0 | wire channel + resolver | UI only; the Core data layer is the same |
| Mute | local botInfo.sendNotification + `enableNotifications` | same flag drives badge/push/mute (R `SNotificationPrefs.cs:284-324`) | The bot's `info.sendNotification` (always false today) **overwrites** the user's choice on every `settingsGeneratedTime` change, and starts rooms muted |
| setChatMode | 6 args | 7th = hideParticipantAddresses (tip gate, R `:2123-2137`) | `hideParticipantAddresses=true` on a bot disables tips in R only |
| Malformed/null chat | exception, dropped | empty row stored | — |
| Typing | Core drops | Core drops (the C21 typist is for groups) | none |
| Reply-to | none | planned via `ChatStreamMessage.ReplyToId` (Core patch pending, R `docs/be-cutover-ixian-core-reply-carrier.md`) | Future: a trailing field on ChatStreamMessage |

---

## 7. Safe extension points vs breaking changes

### Safe (old readers ignore them, both clients)

| Change | Evidence |
|---|---|
| New `SpixiBotActionCode` values (≥14) sent B→C | Unknown ⇒ `return false`, dropped silently (C `:2733-2735`) |
| New `SpixiMessageCode` values sent B→C **without `groupAddress`**. Prefer 0xF0-0xFF "reserved for custom apps" | Core default + app switch without default (C `:1589`; U/R switch) |
| Trailing fields after `hideParticipantAddresses` in `BotInfo` | Conditional reader stops there (C `BotInfo.cs:47-74`). ⚠ *Session 5 (characterized, `Unknown_Codes`):* only **after** hide. Bytes after `userCount` without randomId+hide are read as `IxiBytes randomId · bool` (C `BotInfo.cs:70-74`); junk there throws `EndOfStream`, Core catches it (`:1593`) and **drops the whole info** — no nickname/botInfo update and no `getChannels`. A writer must emit randomId + hide before any new field (D-048). |
| Trailing fields after `name` in `BotChannel` | Reader reads 2 fields (C `BotChannel.cs:28-38`) |
| Trailing fields after `status` in `BotContact` | Tail wrapped in try/catch (C `BotContact.cs:101-115`) |
| Trailing bytes after `groupSenderAddress` in the SpixiMessage envelope (**must** write channel + both empty IxiBytes first) | Reader stops after the two IxiBytes (C `SpixiMessage.cs:117-126`) |
| Trailing bytes in `ChatStreamMessage`, `ReactionMessage`, `SpixiBotAction` | Fixed readers, no length check (C `ChatStreamMessage.cs:34-59`, `ReactionMessage.cs:28-39`, `SpixiBotAction.cs:45-63`) |
| Relaying `chatStream` (edits/streaming) | Both clients handle it for bots. The id is inside the payload; `seq` rules apply (U `:418`, R `:628`, C `FriendList.cs:256-326`) |
| Relaying reactions with any reaction string (`like:`, `tip:<txid>`, emoji) | `addReaction` stores any key |
| Sending `pubKey` proactively for members | `handlePubKey` stores it (C `:1697-1714`) |
| Pagination **server-side** (the bot sends fewer messages) | The client has no expectation of completeness |

### Breaking or dangerous

| Change | Why | Evidence |
|---|---|---|
| Sending `leaveConfirmed` to U clients | Core recursion ⇒ StackOverflow | C `:1408`, `StreamClientManager.cs:118-121` |
| Renumbering/reusing existing codes or action values; changing any existing field order/type | Positional binary readers | all readers above |
| Setting `groupAddress`/`groupSenderAddress` on bot→client messages | Switches to the group-chat path ⇒ dropped | C `:784-832` |
| Changing the `acceptAddBot` shape/flow (e.g., non-null data, expecting encryption) | `aesKey=data`; all bot traffic assumes `none` | C `:2087`, `PendingMessageProcessor.cs:373-433` |
| Encrypting bot→client traffic | The client has no keys for bots (chacha null) | same |
| Rewriting relayed messages (new id / sender=bot) | The author = `sender`; dedup and cursor = StreamMessage id; own-echo detection = sender is mine | C `:673`, `FriendList.cs:207-216,256-265` |
| Chat on a channel the client has not received via `channel` | dropped | C `Friend.cs:903-907` |
| Removing/renaming a channel | The client never deletes channels; the old index stays in the selector | C `:2705` TODO |
| Changing `settingsGeneratedTime` casually | Wipes the local mute, forces getGroups/getUsers | C `:2678-2691` |
| `userCount ≥ 500` | No roster fetch ⇒ per-sender `getUser` storm | C `:2687,2699`, `FriendList.cs:225-228` |
| Omitting `serverName` (new Core ctor) | Bot nickname becomes "" | C `:2682`, `BotInfo.cs:33` |
| `defaultChannel` not in the channel list | U/R `onLoad` waits, then pops "bot not ready" | U `:510-521` |
| Not answering `msgReceived` for chat | The client re-sends forever (pending queue) | C `:486-517` |
| Relying on payment (`getPayment`) | The client stub never pays; the message is stuck | C `:2738-2742` |

---

## 8. Contract-test checklist (derived)

*Status (session 4, D-045):* items 1–3 characterized green in CI (`Join_Handshake`, `History_Cursor`,
`Relay_Echo_Ack`, run 36856754230). *Session 5 (D-047):* item 4 `React_Delete`, item 5 `Unknown_Codes`, item 6
`Leave` (store → `crashed`, W12), item 7 `Info_Variants`; items 5 and 7 inject bot messages in SimClient (the legacy
bot never sends them). Also characterized: a reaction moves the client cursor to the **target** id (C `:1689`); the
reactor's own echo is dropped (no duplicate, `FriendMessage.cs:282`); the bot answers a non-admin, non-author delete with
nothing (ack only).

1. Join handshake: `requestAdd2` → `acceptAddBot` (+avatar) → client `nick` + `getInfo` → `info` → `getChannels`/`getGroups`/`getUsers` → `channel`×N → `botGetMessages`×N.
2. The replay honours the cursor; an unknown cursor ⇒ full replay. Assert the client stays responsive.
3. Relay: author = original sender. An own echo is deduped. `msgReceived(id, channel)` goes back to the sender.
4. Reactions and deletes: an admin/author delete propagates, and the signed bot `msgDelete` verifies.
5. Unknown action code / unknown message code / BotInfo with extra trailing bytes ⇒ no crash on both clients.
6. Never send `leaveConfirmed` until the U crash is fixed. Verify R leaves are delivered.
7. `info` with and without `randomId/hideParticipantAddresses`; with `serverName` set.

---

## 9. Open questions for the Ixian BE engineer

1. Which exact Ixian-Core commit shipped in store Spixi 0.9.22 (j `95fc725`?)? Is there a record in the release build?
2. Can the Core `StreamClientManager.getClient` recursion be fixed (one line) and shipped? Until it ships, should the bot **stop sending `leaveConfirmed`** to protect store clients?
3. Is the unsigned/unverified bot→client channel (acceptAddBot, botAction, relayed chat) an accepted trust model? Should the reworked bot sign botActions, and should clients verify relayed chat against roster pubkeys (the commented code at C `:969-983`)?
4. `acceptAddBot` is accepted from any friend with `handshakeStatus ≤ 1` and flips it to bot mode, with no signature check. Is this intended?
5. Paid bots: `onGetPayment` is a stub. Should the rework drop `cost>0` rooms, or will Core implement payment? What length unit is canonical for the price (chars vs SpixiMessage bytes)?
6. Cursor design: should `botGetMessages` carry a timestamp/sequence instead of the last message id? The own-message/reaction-target/delete-id cursor plus `FindLastIndex=-1` gives full 10 000-message replays.
7. `FriendType.Normal` author nulling in Core k (`5643e5b`): will `&& !friend.bot` land, and should the bot re-serve history to heal rows stored without an author?
8. The roster 500-cap plus `getUser` per unknown sender with no dedup: is a batched `getUsers(addresses[])` action acceptable? It would be a new action code; old clients never send it.
9. Should the bot honour `enableNotifications` (currently forced false)? Clients use `info.sendNotification` as the mute state and badge gate.
10. Are `kickUser`/`banUser`/`getGroups`/`msgReport` meant to be implemented server-side? Clients already send them.
11. Is the bot's `getUser` NRE for unknown addresses (B `:440-442`) known? A single request can hit it.
12. Is a migration to Core's newer `GroupChat` model planned for bots? If so, clients route on `groupAddress`, and that is a breaking change for this contract.
