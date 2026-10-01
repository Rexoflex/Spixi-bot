# D1 · Test harness — three options, graded

Session 2, 2026-10-01. Design item D1 (`docs/roadmap.md`), rubric from `docs/process.md` §G1.
Decision row: D-031 (updated by this doc). Needed before B1a (characterize legacy). Revision 2 after review round 1
(`docs/reviews/session-2-round-1.md`). **Revision 3 after the session-3 spike (§7).**

## 1 · The problem

B1a must record what the legacy bot does today, as the two apps see it, and later batches must prove they did
not break it. That needs the bot and simulated clients running together in CI. Four source facts shape the choice:

| # | Fact | Evidence |
|---|---|---|
| F1 | Core keeps process-wide static state: `IxianHandler` is a static class; `NetworkServer.connectedClients`, `FriendList.friends` and `StreamClientManager` are static. One process = one node identity. | Core `f6fb55b` `Meta/IxianNode.cs:84`, `Network/NetworkServer.cs:47`, `Streaming/Friends/FriendList.cs:26`, `Network/StreamClientManager.cs:22` |
| F2 | Core is a **shared project compiled into each app**, not a DLL. The bot (B1a) needs Core `f6fb55b`; the apps run Core j/k (streaming code = `1ff5435`). Two Core versions cannot live in one assembly. | `SpixiBot/SpixiBot.csproj:44`; research D §0 |
| F3 | Core can open a direct connection by host and address (`connectTo`). **But the app does not join that way:** it connects to a bot only after a presence update has filled `relayNode`, else it calls `fetchFriendsPresence`; and `friend.bot` stays false until `acceptAddBot`, so the first `requestAdd2` goes out with `send_to_server=true`. | Core HEAD `Network/StreamClientManager.cs:95`; U `Meta/Node.cs:293-310`, `Network/NetworkProtocol.cs:476-483`; C `Friend.cs:198,250-253`, `CoreStreamProcessor.cs:122-161` |
| F5 | A headless client needs app glue: an `IxianNode` subclass (12 abstract members), a `PendingMessageProcessor` subclass, a stream processor (the app's is MAUI-bound, 809 lines) and TIV callbacks. | C `Meta/IxianNode.cs:59-73`, `PendingMessageProcessor.cs:36`; U `Network/StreamProcessor.cs:15`, `Meta/Node.cs:97` |
| F4 | The bot starts isolated (testnet flag, a chosen seed, a test wallet password). **Refuted by the session-3 spike:** with no reachable seed it answers every client hello with "bye: not ready", because hello needs a TIV block header (`getLastBlock()`, Core f6fb55b `CoreNetworkProtocol.cs:509-514`); 12 attempts in 30 s, CI run 36847424722. With Core's testnet seeds the header arrives in ~1 s and clients are served. | `Meta/Config.cs:273,312,330`; `Meta/Node.cs:270`; §7 |

App-layer behaviour (leave flow, history cursor, 2.5 s reconnect, `getInfo` on every connect) lives in the apps, not
in Core (research D §0, §4, §6). Any option must reproduce it explicitly.

## 2 · Options

**A · Out-of-process client driver.** A small console app (`simclient`) compiled against the **client** Core. One
process = one member. The test (xUnit) starts the bot process and N `simclient` processes, sends commands as JSON
lines on stdin (`join`, `post`, `react`, `delete`, `leave`, `reconnect`) and reads events as JSON lines on stdout
(`received`, `ack`, `channel`, `user`). The driver re-implements the app rules in two modes, `--app store` and
`--app redesign`, each rule citing the app `file:line` it copies.

**B · In-process bot with a fake transport.** The test project compiles the bot code with Core `f6fb55b` and calls
`StreamProcessor.receiveData(bytes, endpoint)` directly with a fake `RemoteEndpoint` whose `sendData` records the
output. Test messages are built with `StreamMessage`/`SpixiMessage` and test keys. No sockets, no client Core.

**C · Recorded-traffic replayer.** Real store and redesign apps talk to a test bot with `--netdump`
(`Config.cs:121`); the byte streams are saved; a replayer opens sockets and plays them back at a fresh bot. Load =
many replays in parallel.

Also rejected before grading:

- **Run the real app headless** — MAUI apps do not run headless on Linux CI.
- **A′ · compile the app's own `StreamProcessor`/`NetworkProtocol` into the driver with MAUI shims** — higher fidelity
  in theory, but those files are wired to pages, UI state and app storage (U `Network/StreamProcessor.cs`, 809 lines);
  the shims would be larger than the few app rules the bot depends on and would drift with every UI change. The rules
  are copied instead, each with a citation (W4); revisit if the copied-rule list grows past ~10.
- **AssemblyLoadContext isolation** (the session-1 candidate). By F2 each simulated client
would need its own copy of a whole client assembly plus native RocksDB per context; static state in Core and
third-party code is not guaranteed to isolate. Same fidelity as A at far higher complexity.

## 3 · Grading (1–5, weights from the rubric, max 50)

| Criterion (weight) | A out-of-process | B in-process fake | C replayer |
|---|---|---|---|
| Correctness & safety (×3) | **5** — real client Core, real sockets, real bot-side hello and presence binding (the app's presence *discovery* is bypassed, F3/W9); the S1 tests in B4 can use a real second identity | 3 — real handlers, but no hello, no presence binding (`CoreNetworkProtocol.cs:256`), no socket throttles; a fake endpoint can hide exactly the T2 behaviour the threat model cares about | 3 — real bytes in, but it cannot react to replies (cursor ids, new message ids), so most delivery scenarios cannot be expressed |
| Member impact (×2) | **5** — pins store and redesign behaviour with their own Core; reader tests use the client parser | 2 — wire compatibility only through snapshots; nothing runs client code | 3 — real client input; client-side reading is not checked |
| Maintainability (×2) | 3 — process orchestration, two driver builds, timing, app glue (F5) | 3 — fast and deterministic, but the shim depends on `RemoteEndpoint` internals that change in B1b, so it is rewritten once | 2 — recordings go stale with every protocol change; re-recording needs phones |
| Self-hosting (×1) | 2 *(rev 3, spike)* — plain `dotnet` processes, but the legacy bot runs only on Windows and needs the public testnet for a block header (§7); was 4 | 4 | 3 — needs a recording rig |
| Delivery speed (×1) | 2 — the most up-front work (driver, glue, protocol, fixture), **~3 days** to the first test | **5** — ~1 day | 3 — rig + recordings + replayer |
| Extensibility (×1) | 4 — new codes = new driver commands; agents/closed mode can reuse the driver | 3 | 2 |
| **Total** | **39** *(rev 3; was 41)* | 31 | 27 |

**Winner: A.** It is the only option that runs the code the members run (Core client code, real sockets), which is the point of B1a (D-006,
contract tests). B wins on speed and determinism; C on realistic input. Both strengths are folded into A below.

## 4 · Fixing A's weaknesses

| Weakness | Fix |
|---|---|
| W1 Slow start | B1a starts with a **spike**: one scenario `Join_Post_Receive` (two drivers, one bot). It also settles F4 (bot without DLT). The driver grows one command per batch, never ahead of need. |
| W2 Flaky timing | No sleeps. Every wait is "until event X or timeout"; the timeout is at least 3× the larger reconnect interval (Core 2 s, `CoreConfig.cs:95`; app loop 2.5 s, research D §0) and is reported with the event log. Each test gets a fresh bot data directory, a free port and fresh test wallets. |
| W3 Two Core versions (F2) | The driver is built twice: Core k (`097341a`, redesign) and the store app's Core (BE ask Q1; research D's hypothesis is j). Until BE answers, both modes use k and store fidelity is marked **hypothesis** in the test names' report. |
| W4 App rules are re-implemented | Each rule cites the app `file:line` (U `0e85a4b`, R `5d48669`). A rule without a citation fails review. When an app changes, the citation shows what to recheck. |
| W5 Soak size (one process per member) | Contract and integration tests need tens of members, which processes handle. The B7 soak (1,000 members) uses a separate thin load tool that speaks the wire format with Core primitives only; whether hello can be built without Core statics is checked when B7 is planned. Until then the soak report states the member count it really reached. |
| W6 B's speed for pure logic | Not by faking the legacy transport. From B2 on, the new domain core is plain code with plain unit and property tests (test-strategy §2); the harness is only for wire-level behaviour. |
| W7 Wire bytes the apps must read | Snapshot tests: the bot side writes the exact bytes it sends to files; reader tests in the driver project (client Core) parse them. This is the only crossing between the two Core versions, and it is a file, not an assembly. |
| W8 Proof the harness itself works (L5) | The spike is accepted only when a deliberate break in the bot (stop relaying chat) makes `Join_Post_Receive` fail. |
| W9 App join path (F3) | Known divergence: the driver adds the bot friend, calls `setBotMode()` (C `Friend.cs:250`), opens `connectTo(host, botAddress)` before `requestAdd2`, and pins `online = true`, `updatedStreamingNodes = now`, `relayNode = null` so `PendingMessageProcessor` sends directly (C `PendingMessageProcessor.cs:437-438`, `CoreConfig.cs:120`, `NetworkClientManagerBase.cs:984`); it re-pins at least every 300 s and after any `error` from the bot (which resets them, `CoreStreamProcessor.cs:719-724`). Extra `fetchFriendsPresence` calls are expected noise. Marked as a divergence in the report; the app's presence path is covered in the B7 pilot with real apps. If the spike shows the bot rejects this, the fixture runs a stub seed that answers presence. |
| W10 App glue (F5) | The driver implements the minimum glue (node subclass, pending-message processor, stream processor that emits events) and nothing else; every glue method that is a no-op says so in a comment and in the driver README. |
| W11 Ports, IP and startup | The fixture always starts the bot with `-p <free port> -a <free port> -i 127.0.0.1 --disableWebStart` and a test config (`Config.cs:16,23`, `Node.cs:72,246-249`). *Rev 3 (spike):* **no wallet pool** — keygen measured at 0.4–1.4 s per member in CI, so each bot and SimClient makes a fresh testnet wallet; a fresh copy of the bot output per test (the bot reads DLLs and writes `Data/`, `activity/`, logs relative to its working folder); a default group (cost 0) and channel are seeded through the API (a fresh bot has none; chat on channel 0 is dropped, `StreamProcessor.cs:379`). |
| W12 Store-mode crash on `leaveConfirmed` | The store app recurses in `StreamClientManager.getClient` (C `:118-121`, research D). Out-of-process isolation keeps the test alive; the harness reports a driver exit as a `crashed` event and the store `leave` test expects it. |

## 5 · Shape (for B1a)

```
tests/
  SimClient/              console app, Core k (and store Core later), --app store|redesign
  Harness/                xUnit v3 fixture: start bot process, start drivers, event waits, logs on failure
  Contract.Tests/         scenarios from research D §8 (join, post, history, react, delete, leave, nick twice, …)
  Snapshots/              recorded wire bytes + reader tests (run inside SimClient's build)
```

CI (*rev 3:* **Windows** for the harness, both .NET 8 and .NET 10 SDKs installed): the `legacy` job builds the bot with
.NET 8 + Core `f6fb55b` on Linux and Windows and runs a start smoke; the `harness` job builds `SimClient` with .NET 10 +
the client Core and runs the scenarios against the bot, plus a `self-test` variant with the relay removed (W8). The bot
and the clients never share a process. Why Windows: the legacy bot cannot start on Linux (registry call,
`Program.cs:50/204`) and, once patched, its API hangs on the first settings save (`Settings.cs:109/144` recursion);
on Windows it runs unpatched in its own console window (`Console.Clear` throws on a redirected stdout,
`Program.cs:142`), and the harness reads its `ixian.log`.

## 6 · Open points

- *Rev 3:* F4 refuted, W9 and W10 verified (§7). The pre-committed fallback "a minimal stub seed" is replaced by an
  explicit choice for session 4: the bot needs a block header, and the spike gets it from the public testnet seeds. A
  frozen header fixture (no network) or a stub seed are the candidates. D-031 is locked on option A with these
  consequences (D-043, D-044).
- Store app Core commit — BE ask (blocks store fidelity, not the spike).
- Core's own tests use MSTest (`UnitTests/Ixian-UnitTests.csproj`); our tests use xUnit v3 (test-strategy). No conflict:
  separate projects.

## 7 · Spike results (session 3, PR #1 on the fork, branch `spike/harness`)

| Item | Result | Evidence |
|---|---|---|
| Legacy build | ✅ .NET 8 + Core `f6fb55b` builds on Linux and Windows | CI run 36840465376 onward |
| Start on Linux | ❌ `PlatformNotSupportedException` in `checkVCRedist()` (`Program.cs:50`, called `:204`); with a CI-only patch it starts, but the API hangs on the first settings save (`Settings.saveSettings` ↔ `setOption`, `Settings.cs:109/144`) | runs 36840465376, 36841419911 |
| Start on Windows | ❌ redirected: `IOException` in `Console.Clear()` (`Program.cs:142`); ✅ in its own console window, listening in ~4 s, API 200 | runs 36840465376, 36841419911 |
| F4 bot without DLT | ❌ refuted: "bye: not ready" to every hello (12× in 30 s) without a block header; ✅ with testnet seeds | run 36847424722 |
| W9 direct join | ✅ connect by host + address, setBotMode + pins, retry every 2.5 s like the app; acceptAddBot, channel list, relay all work | run 36847424722 |
| W10 minimal glue | ✅ `SimNode`, `SimStreamProcessor`, `SimPendingMessageProcessor`, no-op callbacks are enough for join/post/receive | run 36847424722 |
| Join_Post_Receive | ✅ store→redesign and redesign→store; author and channel asserted; poster acked | runs 36848444730, 36850085277 |
| W8 / L5 | ✅ relay line removed in CI → both cases fail with `W8-RELAY-MISSING[case]`, only after the bot acked the post and the receiver stayed healthy | run 36848444730 (single marker), run 36850085277 (per case) |
| Keygen time | 0.4–1.4 s per wallet → no pool | SimClient `ready.keygenMs` |
| Core k quirks met | RocksDB 11.1.2 lacks `SetWALTtlSeconds` (pin 10.4.2.64152, as the redesign); `NetworkClientManagerStatic` needs ≥ 3 neighbours; client threads are foreground (explicit `Environment.Exit`) | runs 36840465376, 36844069755, 36844824431 |

*Session 4 (B1a, D-045):* D-044 guard in `BotProcess.WaitForHeaderAsync`; scenarios `Join_Handshake`,
`Relay_Echo_Ack` (grown from `Join_Post_Receive`), `History_Cursor` in `tests/Harness` (Contract.Tests deferred); one
self-test job per break (`relay`, `ack`, `info`, `cursor`, `seed-none`), all proven in CI run 36856754230. New
divergence: SimClient `refresh` replays the new-connection cascade without a TCP reconnect (README).

Known limits: store mode runs on Core k (W3, hypothesis until BE-04); CI depends on the public Ixian testnet for a
header; the receive check rules out replay after a reconnect, not a second channel list without reconnect (review R2
r2); the log tail reads text, not bytes (review R1 n2; the bot log is ASCII).
