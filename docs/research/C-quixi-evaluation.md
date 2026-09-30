# C — QuIXI vs Spixi-Bot: capability evaluation for the reworked group-chat bot

Read-only evaluation, 2026-10-01. Repos: QuIXI `fd2e0dc` (qxc-0.9.4a, shallow clone, 20 commits visible), Spixi-Bot `a5a3442` (xsbc-0.6.2-PR1, full history, 111 commits; local branch `rework/bot` = `master` = `origin/development`, 0 extra commits), Ixian-Core `1ff5435`.
Paths below are relative to `/home/claude/work/`. **H:** marks a hypothesis (not proven by a build or a run). No `dotnet` exists in this container, so no build was run.

---

## 0. Summary (one screen)

| Question | Answer |
|---|---|
| What is QuIXI? | A headless **client node** (same class as the Spixi app: presence type `'C'`, no listening port, reached via S2 relays) that exposes Core's streaming as a **GET REST API + publish-only MQ bridge** (MQTT / RabbitMQ). |
| Can QuIXI host a bot channel today? | **No.** It can be a *member* of a Spixi-Bot channel and of a Core P2P group (and relays as *owner* only if it created the group, which it cannot do via its API). It has no `botGetMessages` history serving, no channel/role/payment/quota logic, no `acceptAddBot` reply, and Core's receive path rejects the unencrypted-signed messages Spixi clients send to a bot. |
| What is Spixi-Bot? | A **directly-reachable server node** (presence `'C'` **with** `serverPort` 15235 + `enableNetworkServer`), implementing the bot/channel server side: history per channel, channels, groups (roles/price/admin), paid messages, nick/avatar directory, push (IPN), web admin UI + `sb_*` REST. |
| Does Spixi-Bot build against current Core? | **Almost certainly not** (H, by source): at least two signatures changed under it (`BotInfo` ctor 2026-05-17, `ActivityStorage` ctor 2026-04-08). Its CI points at the old `ProjectIxian` org. |
| Where does the shared bot model live? | In **Core** (`Streaming/Bot/*`, namespace `IXICore.SpixiBot`): wire/data types + file stores + the **client** side of the bot protocol. The **server** side (history, relay, pricing, admin) lives only in Spixi-Bot (~4 kLOC). |
| Tests / CI | QuIXI: 0 tests, CI builds only (and CI pins .NET 8 while the project targets net10 → CI broken, H). Spixi-Bot: 0 tests, CI commented-out test step, stale org. Core: 18 test files incl. `TestGroupChat.cs` (29 tests), CI runs `dotnet test`. |
| Maintenance | Single active maintainer (`IxiAngel`) across all three in 2025–26. Core and QuIXI active (last commits 2026-08); Spixi-Bot last touched 2025-10-21, historically sporadic. |

---

## 1. QuIXI architecture

| Aspect | Fact | Evidence |
|---|---|---|
| Node type | Client node, presence type `'C'`, port 0 (no inbound listener) | `QuIXI/QuIXI/Meta/Node.cs:191` `PresenceList.init(IxianHandler.publicIP, 0, 'C', …)`; `Config.cs:33` `serverPort = 0` |
| Network connection | Outbound only: `StreamClientManager` (S2 relays, max 6) + `NetworkClientManager` (DLT/relay-sector nodes, max 3) | `Node.cs:79-80, 236-243`; `Config.cs:25-26` |
| Block data | Headers-only TIV (`TIVBlockVerificationMode.Minimal`), RocksDB, pruning on | `Node.cs:67, 88, 184`; `Config.cs:83-84` |
| Stream capabilities (default) | `Incoming \| AppProtocols \| GroupCapabilites` | `Config.cs:78` |
| Storage | RocksDB (headers + activity), Core `LocalStorage` (`data/…`, `Chats/<addr>/<channel>`), Core `FriendList` (`accounts/<addr>/account.ixi`, `meta.ixi`), `PeerStorage` | `Node.cs:67-95`; `Ixian-Core/Streaming/Storage/LocalStorage.cs:98,546`; `Ixian-Core/Streaming/Friends/Friend.cs:1063-1102` |
| Wallet | Core `WalletStorage(Config.walletFile)` | `Node.cs:564` |
| Config | `ixian.cfg` key=value + CLI flags (apiPort, apiBind, apiAllowIp, addApiUser, mqDriver/Host/Port, streamCapabilities, walletNotify, folders …) | `Config.cs:18-86, 118-166`; `README.md` "Configuration" |
| REST (QuIXI-specific), all GET/query-string | `contacts`, `addContact`, `acceptContact`, `removeContact`, `sendChatMessage`, `sendChatStreamMessage`, `sendSpixiMessage` (raw `SpixiMessageCode` + hex data + pending/push flags), `sendAppData` (appId or protocolId), `getLastMessages` | `QuIXI/QuIXI/API/APIServer.cs:19-62` |
| REST inherited from Core `GenericAPIServer` | ~60 wallet/node methods: `addtransaction`, `createrawtransaction`, `sendrawtransaction`, `mywallet`, `gettotalbalance`, `activity2`, `generatenewaddress`, `getwalletbackup`, `sign`/`verify`, name registration, `status`, `pl`, … | `Ixian-Core/API/GenericAPIServer.cs` (method table) |
| API auth | Basic auth users (`addApiUser`) + IP allow-list + bind list; default bind `http://localhost:<port>/` | `Node.cs:199-204`; `GenericAPIServer.cs:151` |
| MQ drivers | None (Dummy), Memory, MQTT (MQTTnet), RabbitMQ (fanout exchange `Ixian`) | `Node.cs:109-130`; `MQ/Drivers/*`; `RabbitMqQueue.cs:34,66` |
| MQ direction | **Publish-only** from the node; no code subscribes to inbound commands (all commands come via REST) | only `PublishAsync` outside `MQ/`; no `SubscribeAsync` callers |
| MQ security | MQTT: plain TCP, no credentials, no TLS, clean session, fixed client id `Ixian` | `MQ/Drivers/MqttQueue.cs:19-29` |
| MQ topics | Chat, ChatStream, Nick, SentFunds, RequestFunds, RequestFundsResponse, MsgRead/Received/Delete/Reaction/Typing, File*(5), Avatar, App*(8), AppProtocols/GetAppProtocols/AppProtocolData, AcceptAddBot, BotAction, LeaveConfirmed, RequestAdd2, AcceptAdd2, Transaction*/TransactionStatusUpdate, FriendStatusUpdate, MessageSent, MessageExpired, BlockHeader, BlockReorg | `MQ/IMessageQueue.cs:6-66`; `Network/StreamProcessor.cs:50-290`; `Network/ICPendingMessageProcessor.cs:14-26`; `Meta/ICTransactionInclusionCallbacks.cs:24-134` |
| Payload format | JSON (`JsonStreamMessageSerializer`), Newtonsoft | `Node.cs:118-124`; `MQ/Serializers/JsonStreamMessageSerializer.cs` |
| Target framework | `net10.0`, Nullable enabled, ImplicitUsings off | `QuIXI.csproj:5-8` |
| Dependencies | BouncyCastle 2.6.2, FluentCommandLineParser-netstandard, Mono.Nat, MQTTnet 5.1, RocksDB 10.4.2, Newtonsoft 13.0.4, RabbitMQ.Client 7.2.1; Core via shared `IXICore.projitems` (sibling checkout) | `QuIXI.csproj:23-33` |
| Tests | **None** (no test project) | repo tree |
| CI | GitHub Actions build on push; **pins .NET 8 while csproj is net10** → build should fail (H) | `.github/workflows/dotnet.yml:32` vs `QuIXI.csproj:5` |
| Size | 21 C# files, **3,879 lines**; biggest: `NetworkProtocol.cs` 787, `Node.cs` 686, `Config.cs` 559, `APIServer.cs` 447 | `wc -l` |
| Quality signals | Heavy copy-paste param validation in `APIServer.cs`; one NRE bug: `friend.getMessage(...)` dereferenced **before** the `friend == null` check (`APIServer.cs:248` vs `:264`); `// TOOD Add multi addresses` (`:361`); 12 TODOs total; examples are bash + `mosquitto_sub` + `curl` | `APIServer.cs`, `Examples/RasPi/*` |
| AI example | LM Studio bridge: `mosquitto_sub -t "Chat/#"` → LLM → `curl` `/sendChatMessage` | `Examples/RasPi/LMStudio/ixiMessageHandler.sh:31`, `helpers.sh:6,48` |
| License | MIT (2025 Ixian Platform) | `LICENSE` |

---

## 2. Group support in QuIXI

Core has **two different "group" models**, both reusing the `IXICore.SpixiBot` data types:

| Model | Who relays | Transport | History for late joiners | Roles/paid | Where |
|---|---|---|---|---|---|
| **Bot channel** (`friend.bot`, `FriendType.Normal` + bot mode) | The bot server (Spixi-Bot) | Clients open **direct** connection to bot IP; messages **unencrypted, signed** | Yes: client sends `botGetMessages(lastId)` per channel | Yes (BotGroup cost/admin) | Core client side: `CoreStreamProcessor.cs:2645-2830`; server: Spixi-Bot |
| **P2P private group** (`FriendType.Group`, since Core 2026-05-17 "Private Group Chat capabilities") | Sender fans out to each member; **owner relays** when sender lacks a member contact or in blind mode (`hideParticipantAddresses`) | Normal encrypted S2 streaming | **No** (pending/offline only) | No roles/pricing used (`BotInfo` reused for randomId/blind flag) | `Streaming/GroupChat.cs:13-140`; `CoreStreamProcessor.cs:321-403` (send), `:785-898` (receive/relay), `:1572-1582` (`createGroup`) |

QuIXI status per capability:

| Capability | QuIXI today | Evidence |
|---|---|---|
| Member of a **Spixi-Bot channel** | Yes, via Core client code (receives `botAction` info/channel/user, auto-requests history, auto-pays `getPayment`). Publishes `BotAction`, `AcceptAddBot`, `LeaveConfirmed` to MQ | `Network/StreamProcessor.cs:155-163, 256-258`; Core `CoreStreamProcessor.cs:2660-2690, 2721-2790` |
| Member of a **Core P2P group** | Yes: `GroupCapabilites` advertised; `createGroup` from a contact → `GroupChat.JoinGroup`; `sendChatMessage` to a group address fans out via `sendGroupSpixiMessage`; MQ `Chat` carries sender via `group_sender_address` stored with message | `Config.cs:78`; Core `CoreStreamProcessor.cs:281-285, 1572`; `StreamProcessor.cs:32,193` |
| **Owner/relay** of a P2P group | Relay code is in Core and would run automatically if QuIXI owned the group, but QuIXI has **no endpoint to create a group** (`createGroup` only reachable via raw `sendSpixiMessage` + no local `GroupChat.CreateGroup` call) | `APIServer.cs` (no create); Core `GroupChat.cs:27` unused by QuIXI |
| **Bot server** (host channels, relay to many, store history, roles, pay) | **No.** Missing pieces below | — |

What's missing in QuIXI (+Core) to host a bot channel:

1. **Inbound listener** so clients can connect directly (`enableNetworkServer`, non-zero `serverPort`, presence with port) — QuIXI is port 0 (`Config.cs:33`, `Node.cs:191`).
2. **Receive path for bot-server traffic.** Core's `receiveData` rejects unencrypted messages from a non-bot friend except requestAdd/acceptAddBot (`CoreStreamProcessor.cs:767-778`) — i.e. the signed-plaintext messages Spixi clients send to a bot (`sendSpixiMessage` forces `none`+sign when `friend.bot`, `:311-315`). Core also needs a `Friend` per sender; a community of thousands of members as full `Friend` records (each with `accounts/<addr>/…` files) is the wrong shape (H: scaling). Spixi-Bot bypasses all of this with its own `StreamProcessor.receiveData` (`Spixi-bot/SpixiBot/Network/StreamProcessor.cs:25-198`) keyed on `BotUsers`, not `FriendList`.
3. **Server handlers:** `requestAdd/requestAdd2 → acceptAddBot`, `getPubKey`, `getNick`, `getAvatar`, `botGetMessages`, `botAction getInfo/getChannels/getUsers/getUser/payment/enableNotifications`, `msgDelete` (admin/author), `msgReaction`, `leave` (Spixi-Bot `StreamProcessor.cs:87-193, 422-484`). QuIXI logs `botGetMessages` as unknown (`StreamProcessor.cs:288-289`; topic commented at `MQ/IMessageQueue.cs:30`).
4. **Channel history store** + relay-to-all-connected (`Messages.cs`, `NetworkServer.forwardMessage`).
5. **Roles/pricing/payment verification** (`getMessagePrice`, pending-unpaid queue, `confirmMessage` on tx inclusion).
6. **Admin surface** (groups/channels/users/settings/avatar) and **push** to offline members.

---

## 3. Spixi-Bot architecture

| Aspect | Fact | Evidence |
|---|---|---|
| Node type | Server-ish client: presence `'C'` **with** `serverPort` (default 15235), `IxianHandler.enableNetworkServer = true`, `NetworkServer.beginNetworkOperations()`; up to 10,000 stream clients | `Spixi-bot/SpixiBot/Meta/Node.cs:56, 261, 267`; `Meta/Config.cs:16, 53` |
| Inbound handling | Own `NetworkProtocol` hello → optional **whitelist** (closed community) → `s2data` → own `StreamProcessor.receiveData` (not Core's) | `Network/NetworkProtocol.cs:30-42, 132-134` |
| Transport of chat | Relays the **raw signed client message** to **all connected clients**: `NetworkServer.forwardMessage(s2data, raw)` (broadcast) | `Network/StreamProcessor.cs:418, 494` |
| History | In-memory list per channel, cap 10,000; **whole file rewritten on every message** (`Data/Messages/<ch>/messages.ixi`); delete = null `data` | `Messages/Messages.cs:83-157, 172-182`; `Config.cs:65` |
| Bot protocol (server side) | `botGetMessages` (history from lastId); `botAction`: getChannels, getInfo, getUsers, getUser, payment, enableNotifications. Sends: `info`, `channel`, `user`, `getPayment`. `kickUser`/`banUser` action codes exist in Core but server never sends them | `StreamProcessor.cs:162-180, 422-591`; codes `Ixian-Core/Streaming/Bot/SpixiBotAction.cs:19-35` |
| Channels / groups (roles) | `BotChannels`, `BotGroups` (name, messageCost, admin), `BotUsers` (nick, pubkey, role, avatar, status) — **all Core classes** | `Node.cs:94-101` |
| Paid messages | Price = `group.messageCost * len/1000`; unpaid msg parked in `pendingMessages` (in-memory, no expiry — `// TODO` `:402-403`); client pays via `botAction payment` → `PendingTransactions.addPendingLocalTransaction` → `confirmMessage` on inclusion | `StreamProcessor.cs:360-420, 444-472, 486-496`; `Meta/SpixiBotTransactionInclusionCallbacks.cs` |
| Push | Every 30 min, POST `https://ipn.ixian.io/v1/push.php` for opted-in, offline users — but server **forces `sendNotification=false`** (`//send_notifications = true`) so push is effectively off | `Network/PushNotifications.cs:55-115`; `StreamProcessor.cs:474-481`; `Config.cs:48, 55` |
| Moderation | Ban/kick only via admin REST (sets `status`); **no status check in `onChat`** → banned/kicked users can still post while connected (H: from reading `StreamProcessor.cs:377-420`, no check found). `QuotaManager.exceededQuota` is never called; `serverPassword` stored but never enforced | `API/APIServer.cs:235, 245`; `grep` results |
| Admin surface | Web UI (`html/settings.html`) + `sb_*` REST: settings, setOption, get/new/del/updateGroup, getUsers, delUser, banUser, kickUser, setUserGroup, get/new/del/updateChannel, saveAvatar; plus Core `GenericAPIServer` wallet methods | `API/APIServer.cs:26-103` |
| Channel 0 | Chat on channel 0 is ignored | `StreamProcessor.cs:379-382` |
| .NET | `net8.0` | `SpixiBot.csproj:22` |
| Dependencies | BouncyCastle 2.6.2, FluentCommandLineParser 1.4.3, Open.Nat 2.1.0, RocksDB 10.4.2.62171, Newtonsoft 13.0.4, System.Diagnostics.PerformanceCounter 9.0.10; Core shared project | `SpixiBot.csproj:29-36` |
| Build compat with Core HEAD | **Broken (H, by source):** `new BotInfo(0, name, desc, cost, …)` (10 args) vs Core ctor `(short, byte[]?, bool, string, IxiNumber, long, bool, int, int, bool, long)` — changed 2026-05-17 and `serverName` is no longer a ctor param; `new ActivityStorage(path, 32<<20, 0)` vs Core 5-arg ctor (2026-04-08) | `StreamProcessor.cs:589`; `Node.cs:235`; `Ixian-Core/Streaming/Bot/BotInfo.cs:33`; `Ixian-Core/Activity/ActivityStorage.cs:958` |
| Tests | **None** | repo tree |
| CI | Windows + MSBuild; checks out **`ProjectIxian/Ixian-Core`** (old org) with `ref: ${{ github.event.push.ref }}` (H: not a valid context → default branch); test step commented out | `.github/workflows/dotnet.yml:25-27, 41-42` |
| Build scripts | `rebuild.sh` (dotnet clean/restore/build Release); README still documents Mono/VS2017/`nuget restore`+`msbuild` (stale) | `rebuild.sh`; `README.md:10-51` |
| Size | 13 C# files, **4,056 lines**; 18 TODOs; large dead commented blocks (`StreamProcessor.cs:200-266, 609-650`) | `wc -l` |
| License | MIT (2019 Ixian) | `LICENSE` |

---

## 4. Core vs bot-repo split

| Piece | Lives in | Reused by QuIXI base? | Reused by Spixi-Bot base? |
|---|---|---|---|
| Wire types: `SpixiMessage` codes (`botGetMessages=20`, `botAction=32`, `createGroup=52`), `SpixiBotAction` codes, `BotInfo`, `BotChannel`, `BotGroup`, `BotContact` | Core `Streaming/SpixiMessage.cs:20-76`, `Streaming/Bot/*` | yes | yes |
| File stores `BotUsers` (`contacts.dat`), `BotGroups` (`groups.dat`), `BotChannels` (`channels.dat`) | Core `Streaming/Bot/{Users,Groups,Channels}` | yes | yes (already) |
| **Client** side of bot protocol (process info/channel/user/getPayment/kick/ban; `sendGetMessages`, `sendBotAction`, auto-pay) | Core `CoreStreamProcessor.cs:2466-2830` | already active | not used (server) |
| P2P group model (`GroupChat` derive/create/join/validate, owner relay, blind mode) + 29 unit tests | Core `Streaming/GroupChat.cs`, `CoreStreamProcessor.cs:321-403,785-898`, `UnitTests/TestGroupChat.cs` | already active (member) | not used |
| Streaming/Friend/LocalStorage/PendingMessageProcessor/IPN offline push | Core | already active | not used (own processor) |
| **Server** side: history, relay-to-all, pricing, payment check, admin, push loop | **Spixi-Bot only** (~1.5 kLOC in `StreamProcessor.cs`, `Messages.cs`, `APIServer.cs`, `PushNotifications.cs`) | would have to be written or ported | present |

Conclusion: Core owns the **data model and the client**; the **server** is only in Spixi-Bot and bypasses Core's `CoreStreamProcessor`. Either base reuses the Core types/stores; neither gets a server from Core.

---

## 5. Migration (keep identity + data)

| Asset | Format / owner | Portable to a rebuilt bot? |
|---|---|---|
| Wallet `Data/ixian.wal` (Spixi-Bot default name `Config.walletFile="ixian.wal"`, dir `Data`) | Core `WalletStorage` | **Yes** — QuIXI also uses `WalletStorage` (`Node.cs:564`), path configurable. Bot address/identity preserved. |
| Members `Data/contacts.dat` + `Data/Avatars/<addr>.raw` | Core `BotUsers` binary (`writeContactsToFile`); Spixi-Bot stores nick **as raw message bytes** (`save_nick_as_string=false`, `Node.cs:95`) | **Yes** with the same class and flag. Note Core group mode uses `save_nick_as_string=true` (`Friend.cs:259`) — different nick encoding, don't mix. |
| Roles `Data/groups.dat`, channels `Data/channels.dat` | Core `BotGroups` / `BotChannels` binary | **Yes** |
| History `Data/Messages/<ch>/messages.ixi` | Spixi-Bot own format: `int32 version(0)`, `int32 count`, then `[int32 len][StreamMessage bytes]` | Yes if the reader is kept; it is ~40 lines (`Messages.cs:40-81`). Messages are signed originals, so they remain verifiable. |
| Settings `Data/settings.dat` | Spixi-Bot `Settings` key/value | Yes (small, `Meta/Settings.cs`) |
| Headers / activity | RocksDB, rebuildable | Not needed (resync) |
| Protocol compatibility | Existing Spixi clients know the bot by address + direct IP/port; `BotInfo` wire format appended `randomId`/`hideParticipantAddresses` tail-optionally (`BotInfo.cs:66-70`) | A rebuilt server must keep: `acceptAddBot`, signed-plaintext messages, `botGetMessages`, `botAction` codes, reachable `serverPort`. Any change to these needs a Spixi app release. |

QuIXI's own contact/message stores (`accounts/…/account.ixi`, `Chats/<addr>/<ch>`) are a **different** layout from Spixi-Bot's; a QuIXI-based bot would either adopt the Spixi-Bot files or migrate them.

---

## 6. Maintenance signals

| Repo | Commits | Recent cadence | Authors | Notes |
|---|---|---|---|---|
| Ixian-Core | 1,276 | 2025: 181, 2026 (to 08-25): 134 | 2026: IxiAngel only | Group chat added 2026-05-17; CI runs tests (`.github/workflows/dotnet.yml`, net10) |
| QuIXI | shallow (20 visible) | 2025-12 → 2026-08-10, ~monthly bursts; last: qxc-0.9.4a | IxiAngel | Features added 2026: E2E payments (03-31), chatStream (06-01), group chat params (06-13). Actively positioned for "AI Agents" (`README.md`) |
| Spixi-Bot | 111 | 2019: 6, 2020: 77, 2021: 8, 2022: 7, 2023: 1, 2024: 3, 2025: 9, 2026: 0 | MarkoAT 95, Marko 5, IxiAngel 7, firestorm40 4 | Last commit 2025-10-21 ("updated to latest IXI Core"); since then Core broke its build. README describes Mono/VS2017. No explicit deprecation note found. |

Bus factor = 1 (IxiAngel) for all three in the last 12 months.

---

## 7. The three designs

### (1) Evolve Spixi-Bot in place
| | |
|---|---|
| Work items | Fix compile vs Core HEAD (BotInfo, ActivityStorage, others H); move to net10; fix CI (org, ref, Linux, tests); replace whole-file history rewrite with append/RocksDB; enforce ban/kick/status in `onChat` + relay; wire `QuotaManager`, `serverPassword`; pending-payment expiry; re-enable push; channel-0 + moderation actions from admin clients (`kickUser`/`banUser` botActions); tests; optional REST/MQ for integrations |
| Risks | Old, untested code with dead blocks; separate receive path from Core (drift as Core evolves — already happened); broadcast relay to all connected clients (no per-channel or per-status filter) is a privacy/scale concern (H); single maintainer |
| Self-hosters / integrations | Keeps wire-compat and data 1:1; web admin already exists; **no** MQ, no generic bot/AI hook — integrations must be added |

### (2) Rebuild on QuIXI
| | |
|---|---|
| Work items | Add inbound server (`enableNetworkServer`, port, whitelist); write a bot-server receive path (Core's rejects bot-client traffic, `CoreStreamProcessor.cs:767-778`) keyed on `BotUsers` not `FriendList`; port history, pricing/payment, admin, push; add `botGetMessages`; REST/MQ topics for channel events; admin UI; tests |
| Risks | Largest rewrite (effectively porting Spixi-Bot's server into QuIXI); QuIXI's model = "one node, many 1:1 friends", a poor fit for thousands of members; MQ has no auth/TLS (`MqttQueue.cs:24-28`), REST is GET/query-string; QuIXI itself has no tests and a mis-pinned CI |
| Self-hosters / integrations | One binary for bots, IoT and AI agents; REST + MQTT/RabbitMQ out of the box; wallet API already there |

### (3) Hybrid
Variants: (a) Spixi-Bot core stays the group server, gains a QuIXI-style REST/MQ event bridge (publish channel events, accept "post as bot"/moderation via REST); (b) bot logic (commands, AI replies, moderation policy) runs as an **MQ/REST consumer** outside the server; (c) share the MQ layer (`QuIXI/QuIXI/MQ/*`, ~580 lines, self-contained) by moving it to Core or a shared project.
| | |
|---|---|
| Work items | All of design (1)'s "must-fix" items + port/extract the MQ module + define a channel event schema + a small authenticated command API; consumer SDK/examples (AI agent = LM Studio pattern) |
| Risks | Two surfaces to secure (MQ has no auth today); shared-module refactor touches Core or adds a third project; still inherits Spixi-Bot's untested code |
| Self-hosters / integrations | Wire-compat with existing Spixi clients + integration hooks; AI agents live out-of-process (crash/abuse isolation, language-agnostic) |

Grading-relevant facts, not a recommendation: the only existing implementation of the bot **server** protocol is Spixi-Bot; the only existing integration surface (REST + MQ + AI example) is QuIXI; Core already holds all shared types and the client, so both options pay the same "server-side" cost that differs mainly in *where* it lives.

---

## 8. Open questions for the Ixian BE engineer

1. Is Core's P2P `FriendType.Group` (2026-05) meant to **replace** bot channels for closed groups, leaving Spixi-Bot only for large/public channels?
2. Is there an unpushed Spixi-Bot branch already ported to Core ≥ 2026-05 (BotInfo ctor change)? Local `rework/bot` = `master`.
3. Should a bot server use Core's `CoreStreamProcessor` (encrypted, Friend-based) or keep the plaintext-signed direct-connection model? Any plan to encrypt bot channels?
4. Intended scale of the official community channel (members, msgs/day)? Current relay broadcasts to every connected client and rewrites the history file per message.
5. Are ban/kick meant to block posting server-side? (No check found in `onChat`.) Why is push forced off (`StreamProcessor.cs:478`)?
6. Is `QuIXI/MQ` intended to move into Core (shared by bot and QuIXI)? Plans for MQ auth/TLS and non-GET REST?
7. Must the rebuilt bot keep the **same address and IP/port** for existing Spixi installs, or can clients be migrated via a new invite?
8. QuIXI CI pins .NET 8 vs net10 project — known? Who owns CI for Spixi-Bot (still `ProjectIxian`)?
9. Is `botGetMessages`-style history planned for P2P groups (late joiners currently get nothing)?
10. Paid messages: keep per-message IXI pricing for the official channel, or drop it?
