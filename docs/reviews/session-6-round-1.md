# Session 6 — review round 1 (B1b plan rev 1, three parallel Opus auditors, against Core f6/k and bot source)

Verdict: **NOT CLEAN** — 13 MAJOR, 31 MINOR, 17 NIT. All MAJOR verified (lead re-checked A1, C1, C2, C4 in source;
the others by their cited evidence) and fixed in plan rev 2; two needed Damir (devices for the first test; separate
review session) and were answered in the session.

| Auditor | Scope | MAJOR | MINOR | NIT |
|---|---|---|---|---|
| A | §2–§4 vs Core k / bot (+ sweep of all 13 `.cs`) | 2 | 9 | 9 |
| B | §5–§7, §9 vs harness / CI / bot | 4 | 8 | 4 |
| C | §0–§1, §8, §10–§11 vs DECISIONS / roadmap / threat model / redesign app | 7 | 14 | 4 |

## MAJOR findings and fixes

| # | Finding | Evidence | Fix in rev 2 |
|---|---|---|---|
| A1 | C9 never calls `prepareStorage(false)`: `TIV.start` → `getHighestBlockInStorage` enumerates `pathBase/0000`, which does not exist on a fresh dir → crash | k `TransactionInclusion.cs:143`, `RocksDBStorage.cs:1474,1274-1282`; redesign `Node.cs:312,346,751` | C9: prepare before start, stop after `tiv.stop()`, new header folder |
| A2 | §4 omitted `getNick`/`getAvatar` (bot-made, sent from `onChat`) → blocking wire diff would go red | `StreamProcessor.cs:384-388,766,783` | rule = "every SpixiMessage the bot builds" |
| B1 | Queue bound "drop-oldest" is new behaviour; f6 used the high queue, unbounded, with receive-loop throttling | f6 `NetworkRemoteEndpoint.cs:360-368,674-720`, `NetworkQueue.cs:149-178,258-297` | A2: bounded + 1 s backpressure, then drop-new with counted warning; H1/H2 re-cited |
| B2 | Legacy job had no defined source for the legacy bot; `patch-linux-start.ps1` removal broke the legacy smoke job | `harness.yml:24,51-55` | legacy normal job checks out `c44651c` + Core f6; legacy build+smoke jobs removed |
| B3 | M2 not observable: the D-044 guard stops cases before any member starts | `ScenarioRun.cs:45-47`, `BotProcess.cs:149-159` | M2 dropped, moved to the D-044 revisit |
| B4 | M5 not observable: k ignores the `TryWrite` result, no log | k `NetworkRemoteEndpoint.cs:859` | M5 dropped; H3 says so |
| C1 | k pin `097341a` is not tag v0.9.8k; the tag is `1ff5435` (D-030 asks for a release tag) | `git show-ref --tags` | pin = `1ff5435`; compiled Core identical (only UnitTests csproj differs); D-051 corrected |
| C2 | Phones cannot read `ixian.cfg` (relative to the working directory) → no phone on testnet | redesign `Config.cs:108,148,254` | Damir: two Windows desktop instances now; phones wait for a testnet app build (D-052 amended) |
| C3 | Testnet app without a separate data folder uses the real mainnet wallet and contacts | redesign `Config.cs:22,229,258-277` | §10.5 requires own `dataFolderPath` + new-address check |
| C4 | Testnet ports are 16235 / 8601, not 15235 / 8501 | bot `Config.cs:19,24,277-280` | §10.3 corrected |
| C5 | Security gate (CodeQL + dependency review) and warnings-as-errors for new code missing | process.md G2; test-strategy §4 | dependency review in PR A; CodeQL P6 (recorded in D-049); `SpixiBot.Common` warnings-as-errors |
| C6 | New logic (queue) without tests | process.md G2 | `SerialWorkQueue<T>` in `SpixiBot.Common` + unit tests (A2t), Stryker trial |
| C7 | Introduced-vs-inherited sweep missing before the first upstream PR | process.md; H14 | sweep row in §11.2, done in A9 |

## MINOR / NIT (all fixed in rev 2 unless noted)

A: H2 evidence re-cited; §4 re-serialization rows; C1 filter by own addresses (k TIV would rebroadcast third-party tx); C7 caller `NetworkProtocol.cs:323` with one owner; C19 keepAlive2 processed flag; H19 `pii == null` NRE; L2 null guards; H9 reason; C10 keeps legacy start heights; C11 `transactionVerified` → `updateStatus`; C17 H removed; §2 counts; csproj line cites; unchanged-list nits; maxInventoryItems row; C12 note; tolerance strengthened (readers changed only in v0.9.8f); C20 as a rule exception.
B: payment-stub reasons (≥ v0.9.8 clients ignore getPayment; older: H); needle rule reworded + pre-push check; wire diff on shape sets, one-sided rows warn, "same source, not same binary"; `unknown` self-test on Linux (exit 134, H); Leave on Linux (134 + stderr, Tier-0 note); Linux job count 15; RocksDB Linux load H; queue drops after stop; acceptance wording; dispose 5 s note; presence read at run time.
C: D-049/D-050/D-030 text aligned; PR B items renamed P1–P8; G1 framing + "one PR" scores (39); mutation trial owned; log-sweep row; §10 operational gaps (address from log, password ≥ 10, systemd, externalIp, API user vs CSRF, S11 testnet exception, open join); "before B4 and B5"; wire-rule statement; separate-session review decided (no, D-049); legacy source (= B2). **Recorded for the export, not in the plan:** stale self-test count in CLAUDE.md §6 and D-047 ("11"; the matrix has 12); be-asks BE-05/06/07 status; the doc list to update (CLAUDE.md §4/§6, roadmap, test-strategy, harness.md W7, threat-model).
