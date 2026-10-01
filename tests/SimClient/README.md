# SimClient

One simulated Spixi member per process, for the bot harness (D-031, `docs/design/harness.md`).
Compiled against the **client** Core (`097341a`, xcore-0.9.8k), never together with the bot's Core (F2).

```
dotnet build tests/SimClient -c Release -p:ClientCorePath=<Core 097341a checkout>
dotnet tests/SimClient/bin/Release/net10.0/SimClient.dll --app store|redesign --data <dir> [--name n]
```

Control: one JSON command per stdin line (`join`, `post`, `quit`). Output: one JSON event per stdout line
(`ready`, `fatal`, `connected`, `hello_rejected`, `hello_attempts`, `join_sent`, `accepted`, `info`, `channel`,
`posted`, `ack`, `received`, `dropped`, `other`, `sent`, `expired`, `stream_error`, `error`, `crashed`, `bye`).
Core logs go to `<data>/ixian.log`, never to stdout. The process exits with `Environment.Exit` after `quit` or
stdin EOF, because Core's client threads are foreground threads.

## App rules (W4)

`AppRules.cs` holds every rule copied from the apps, each with its `file:line` in U (store, `0e85a4b`) or
R (redesign, `5d48669`). Both modes use Core k until BE-04 names the store app's Core (W3, hypothesis).

## Glue (W10) — what is real and what is a no-op

| Piece | Real | No-op |
|---|---|---|
| `SimNode` (IxianNode) | `hello`, `helloData` (incl. getInfo to a bot on every connect), `s2data` | blocks, transactions, names, every other protocol code |
| `SimStreamProcessor` | Core `receiveData`; events for acceptAddBot, botAction channel/info, chat, msgReceived | files, VoIP, mini-apps, funds, typing, avatars, nick UI, notifications, non-bot contact flows |
| `SimPendingMessageProcessor` | Core pending queue and retries | push server (off); hooks only emit events |
| `SimLocalStorageCallbacks` | Core LocalStorage writes | UI callback |
| Startup | handler, wallet, client managers, stream processor, storage, friend list, presence, queue | TIV, block storage, API server, push, mini-apps, update check |

## Known divergence (W9)

The app connects to a bot only after a presence update; the harness has no presence network. SimClient sets
bot mode before the contact request, pins `online`/`updatedStreamingNodes`/`relayNode = null`, connects by
host and address, retries the connection every 2.5 s like the app until the bot's hello arrives, and re-pins
every 30 s and after a stream error. See `Program.Join`. Verified in CI run 36848444730.

## Bot prerequisite (F4 refuted)

The old bot answers every client hello with "bye: not ready" until its TIV has a block header (Core f6fb55b
`CoreNetworkProtocol.cs:509-514`). With no reachable seed that never happens; the harness therefore starts the
bot with Core's testnet seeds (`HARNESS_BOT_SEED=testnet`, the default). Session 4 replaces this network
dependency with a frozen header fixture.
