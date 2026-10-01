# Test strategy — Spixi Bot

Session 1, 2026-10-01. Tool choices from `docs/research/E-external-research.md` §6–7 (versions
checked 2026-10-01). Gates are enforced in CI (`docs/process.md` §G2).

## 1 · Principles

- **Behaviour, not text:** every test runs code and asserts an outcome (lesson L3).
- **Characterize before changing:** today's wire behaviour is pinned before any handler moves.
- **Generate cases:** property tests for invariants, not only hand-picked examples (L4).
- **Prove the tests:** mutation testing on the modules that matter (L5).

## 2 · Test pyramid

| Level | What | Tool | Examples |
|---|---|---|---|
| Unit | Domain rules, permission levels, limits, storage records | xUnit v3 | `Ban_BlocksNextPost`, `Moderator_CannotKickAdmin` |
| Property | Invariants over random sequences | CsCheck | "a banned address never appears in fan-out", "history is gap-free and ordered", "an optional history import preserves every id (only if D4 adds it)" |
| Snapshot | Exact wire bytes of every message the bot sends (from the bot's own Core) | Verify | `BotInfo`, `channel`, `user`, history batch; **reader tests** parse them with the Core versions the apps ship |
| Contract | Store + redesign client behaviour, using Core's own client code in the setup D1 chooses | xUnit + harness | join, post, history, react, delete, leave (per D-025), nick twice, leave + re-join, 2-day bot outage |
| Integration | Bot + N simulated clients over real sockets on localhost | harness (+ Testcontainers for multi-node) | reconnect gap, 10k-history join, ban mid-stream, restart |
| Crash/durability | Kill the process during writes, reopen, verify | harness | "no acknowledged message is lost" |
| History import (only if D4 adds it) | Import a real copy of the old bot's `messages.ixi` (access-controlled) into the new bot | harness | ids byte-exact, order; no migration of the live bot (new address, D-034) |
| Fuzz | Every wire parser | SharpFuzz | no crash, no hang, bounded memory |
| Mutation | Storage, permissions, parsers, delivery policy | Stryker.NET | report-only until the .NET 10 trial run passes (D-028), then break threshold 60, rising |
| Load / soak | 1,000 simulated members, 24 h | own load generator (NBomber needs a paid org licence) | p95 join, delivery counters, memory flat |
| Benchmarks | Hot paths | BenchmarkDotNet | append, fan-out, catch-up |

## 3 · The harness (design item D1, before B1a)

Constraint: Core keeps process-wide static state (`IxianHandler`, `FriendList.friends`, `CoreStreamProcessor`), so
the bot and simulated clients **cannot share one process**. D1 grades three options:

| Option | Fidelity | Cost |
|---|---|---|
| Out-of-process clients (one small client process per simulated member, driven by the test) | High (real Core client code) | Process count limits the soak size |
| AssemblyLoadContext isolation (separate Core load per client) | High | Complex; static state and native RocksDB must behave |
| Protocol replayer (recorded client traffic replayed at the bot) | Medium (no client logic) | Cheap, scales to 1,000+ for load |

Likely answer: out-of-process clients for contract tests, replayer for load. App-layer behaviour that differs between
store and redesign (leave flow, cursor handling, message window) is re-implemented in the client driver, and each
re-implemented rule cites the app `file:line` it copies. Fixtures: recorded legacy traffic (B1a), and a real
production copy under access control for the optional history import (scrubbing would break stored signatures).

**Session 2:** D1 is graded in `docs/design/harness.md` — winner: out-of-process client driver (A 41 · B 31 · C 27); the B7 soak uses a separate thin load tool. That doc supersedes the "likely answer" above.

## 4 · CI pipeline (GitHub Actions)

| Job | Runs | Blocks merge |
|---|---|---|
| legacy (until B1b) | .NET 8 + Core `f6fb55b`: the B1a characterization suite (no B0 build, D-041) | yes for B1a |
| build | .NET 10 SDK, Core checked out at the pinned commit, warnings-as-errors for new code, deterministic build | yes |
| test | unit, property, snapshot, contract, integration (short) | yes |
| mutation | Stryker on changed core modules | report-only until the .NET 10 trial run passes (D-028), then blocking at threshold 60 |
| security | CodeQL, dependency review, Dependabot | yes on high |
| fuzz (nightly) | SharpFuzz on parsers | alerts |
| core-head (nightly) | build + tests against Core HEAD | no — alerts on drift (S10) |
| soak (manual/nightly) | load generator | release gate |
| release | SBOM, signed artifacts, provenance | — |

## 5 · Success-criteria measurements

See `docs/design/base-options.md` §5. Each criterion has a named test or a harness report that the
release gate reads.
