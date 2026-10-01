# SimClient

One simulated Spixi member per process, for the bot harness (D-031, `docs/design/harness.md`).
Compiled against the **client** Core (`097341a`, xcore-0.9.8k), never together with the bot's Core (F2).

```
dotnet build tests/SimClient -c Release -p:ClientCorePath=<Core 097341a checkout>
dotnet tests/SimClient/bin/Release/net10.0/SimClient.dll --app store|redesign --data <dir> [--name n] [--wallet-password pw]
```

Control: one JSON command per stdin line (`join`, `post`, `refresh`, `set-cursor`, `react`, `delete`, `leave`,
`inject`, `quit`). Output: one JSON
event per stdout line (`ready`, `fatal`, `connected`, `hello_rejected`, `hello_attempts`, `join_sent`, `accepted`,
`info`, `channel`, `user`, `bot_action`, `posted`, `ack`, `received` (with `self` and `stored`), `refresh_sent` (with the stored `cursors`),
`cursor_set`, `reaction`, `deleted`, `rejected`, `reacted`, `delete_sent`, `leave_sent`, `injected`, `dropped`, `other`,
`sent`, `expired`, `stream_error`, `error`, `crashed`, `bye`).

| Command | Fields | Rule | Event |
|---|---|---|---|
| `react` | `channel`, `id` (hex), `reaction` (e.g. `like:`) | `AppRules.React`: Core `addReaction` first, send only if it stored (U SingleChatPage :1045-1058, R :2521-2541) | `reacted` {id, channel, localAdded} |
| `delete` | `channel`, `id` (hex) | `AppRules.Delete`: `sendMsgDelete`; no local delete for a bot (U :1034-1043, R :2494-2519) | `delete_sent` {id, channel} |
| `leave` | — | `AppRules.Leave`: store = pendingDeletion + save + sendLeave (U ContactDetails :122-146); redesign = sendLeave in try/catch + removeFriend (R SContacts :70-119) | `leave_sent` {app, removed, sent} |
| `inject` | `kind` + kind fields | a message "from the bot" (see Known divergence) | `injected` {kind, id} after every event Core emits synchronously while handling it |

`inject` kinds: `chat` {channel, text}; `unknown-code` {code, channel}; `unknown-action` {action};
`info-k` {serverName, randomId (hex or null), hide, trailing} (Core k BotInfo layout); `info-legacy` {serverName,
trailing} (the bot's Core f6fb55b layout). Info kinds copy the other fields from the stored botInfo and set
`settingsGeneratedTime` = stored + 1; `trailing` appends that many 0xAB bytes. For `info-legacy`, `trailing` must be
1..172: Core k (`BotInfo.cs:70-74`) reads the first 0xAB as the randomId length (one-byte varint 171,
`Utils/IxiVarInt.cs:157-160`), then 171 bytes, then the hide bool. With 1..171 trailing bytes the randomId read hits
EndOfStream, with 172 the bool read does; with 173 or more the parse succeeds and the info is accepted. `inject` needs exactly one joined bot
with botInfo and a completed hello, else `error` {where:"inject"}.

New or changed events: `info` keeps `defaultChannel` (stored) and adds the parsed `serverName`, `randomId`, `hide`,
`userCount`, `admin`, `generatedTime` and the stored `nickname`, `storedAdmin`, `storedGeneratedTime`.
`reaction` {id, target, reaction, from, channel} and `deleted` {id, target, channel} report a relayed reaction or
delete that Core accepted. `rejected` {type, action, id, data, channel, from} reports an unencrypted message that
Core returned null for (not for stream `error` messages, which stay `stream_error`); unknown codes print as numbers.
Core logs go to `<data>/ixian.log`, never to stdout. The process exits with `Environment.Exit` after `quit` or
stdin EOF, because Core's client threads are foreground threads.

## App rules (W4)

`AppRules.cs` holds every rule copied from the apps, each with its `file:line` in U (store, `0e85a4b`) or
R (redesign, `5d48669`). Both modes use Core k until BE-04 names the store app's Core (W3, hypothesis).

## Glue (W10) — what is real and what is a no-op

| Piece | Real | No-op |
|---|---|---|
| `SimNode` (IxianNode) | `hello`, `helloData` (incl. getInfo to a bot on every connect; saves the bot's endpoint for `inject`), `s2data` | blocks, transactions, names, every other protocol code |
| `SimStreamProcessor` | Core `receiveData`; events for acceptAddBot, botAction channel/info/user (others as `bot_action`), chat (incl. whether Core stored it or deduped it), msgReceived, msgReaction, msgDelete; `rejected` when Core returns null | files, VoIP, mini-apps, funds, typing, avatars, nick UI, notifications, non-bot contact flows |
| `SimPendingMessageProcessor` | Core pending queue and retries | push server (off); hooks only emit events |
| `SimLocalStorageCallbacks` | Core LocalStorage writes | UI callback |
| Startup | handler, wallet, client managers, stream processor, storage, friend list, presence, queue | TIV, block storage, API server, push, mini-apps, update check |

## Known divergence (W9)

The app connects to a bot only after a presence update; the harness has no presence network. SimClient sets
bot mode before the contact request, pins `online`/`updatedStreamingNodes`/`relayNode = null`, connects by
host and address, retries the connection every 2.5 s like the app until the bot's hello arrives, and re-pins
every 30 s and after a stream error. See `Program.Join`. Verified in CI run 36848444730.

`refresh` (B1a history scenario) replays the app's new-connection cascade (getInfo → info → getChannels →
channel → botGetMessages with the stored cursor) on the open connection instead of a TCP reconnect. The bot keeps
no per-connection state for botGetMessages (`Messages.cs:196-220` reads only the cursor), so the bot sees the
same requests. `set-cursor` overwrites the stored cursor to model an id the bot does not know.

`inject` has no TCP crossing: the harness cannot make the old bot send an unknown code, an unknown bot action or
a Core-k-layout info. SimClient builds the StreamMessage as the bot's `sendBotAction` does (bot
`Network/StreamProcessor.cs:498-512`: type info, encryption none, unsigned) and passes its bytes to
`SimNode.parseProtocolMessage(s2data, bytes, endpoint)` with the bot's real `RemoteEndpoint` (saved at its helloData).
That is the same entry a wire message takes after the socket read, so Core `receiveData` and the app switch run
unchanged; only the network transport is skipped.

`injected` is a sync point only for what Core does synchronously inside `receiveData` (for example `info`,
`rejected`, `other`). The bot's answers to requests Core sends during that call (getGroups, getUsers, getChannels
after an accepted info) arrive later on the network thread and may be printed after `injected`.

`inject` runs Core `receiveData` on the command thread, in parallel with the network thread that handles the bot's
real messages. Core does not lock the botInfo replacement (Core k `CoreStreamProcessor.cs:2679-2684`), so a real
`info` and an injected one can race. Scenarios must not `refresh` (or otherwise make the bot send `info`) while an
`inject` runs.

`leave` differs by app on purpose: the store app keeps the bot friend with `pendingDeletion = true` until the bot's
`leaveConfirmed` (Core then refuses every bot action from it, which shows as `rejected`); the redesign removes the
friend at once (#567), and SimClient stops re-pinning it.

## Bot prerequisite (F4 refuted)

The old bot answers every client hello with "bye: not ready" until its TIV has a block header (Core f6fb55b
`CoreNetworkProtocol.cs:509-514`). With no reachable seed that never happens; the harness therefore starts the
bot with Core's testnet seeds (`HARNESS_BOT_SEED=testnet`, the default). D-044: the harness waits for the bot's
block height (API `blockheight` > 0) before it starts members; if it stays 0 the test fails as
`INFRA-TESTNET-UNREACHABLE`, not as a bot bug. A frozen header fixture is built only if the testnet flakes.
