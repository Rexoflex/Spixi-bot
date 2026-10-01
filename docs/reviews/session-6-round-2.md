# Session 6 — review round 2 (fresh break-my-verdict reader over plan rev 2)

Verdict: **NOT CLEAN** — 3 MAJOR, 9 MINOR, 5 NIT. All round-1 MAJOR fixes verified correct (pin `1ff5435` = tag
v0.9.8k, diff vs `097341a` only in the UnitTests csproj; `c44651c` holds the legacy bot, path `legacy/…` resolves;
C9 crash path; §10 ports/paths; counts). Lead re-checked M2 (`status = stopping/stopped` set only in
`IxianNode.cs:293,304`) and M3 (redesign `Meta/Node.cs:406-410`). Fixed in plan rev 3; D-049 and D-052 updated.

| # | Sev | Finding | Fix |
|---|---|---|---|
| M1 | MAJOR | A2 "1 s backpressure like legacy" is false at k: no socket throttle for s2data, the raw channel drops silently (`NetworkRemoteEndpoint.cs:171,323-363,339`), and blocking the pool-thread parse loop (`:205,700-708`) can starve the pool and miss pongs (`CoreConfig.cs:87,91`) | A2 never blocks: drop-new when full, counted; H3 extended; D-049 text |
| M2 | MAJOR | Queue stop keyed on `IxianHandler.status`, which the bot's stop path never sets | `Node.stop` calls `CompleteAdding()` + join before stores close; H11 corrected; A2t test reworded |
| M3 | MAJOR | §10.5 isolation incomplete: wallet password in MAUI Preferences, shared across `dataFolderPath` | separate Windows user / VM, same password for both test wallets, H on Preferences scope; D-052 amended |
| m1 | MINOR | C9 cited the redesign stop order wrongly (it stops storage before `tiv.stop()`) | citation fixed |
| m2 | MINOR | k TIV rebroadcasts incoming pending entries too | H21 row (L) |
| m3 | MINOR | H9 "visible": f6 also set a stub header early | row corrected (sync vs async only) |
| m4 | MINOR | `break-bot.ps1` cites `:47-58`, `:75` do not exist | re-cited `:29-40`, `:49,:56` |
| m5 | MINOR | `unknown` on Linux: `patchFired` already accepts stderr text (`UnknownCodesTests.cs:101-102`) | row corrected |
| m6 | MINOR | dependency-review fails on push events | `if: github.event_name == 'pull_request'` |
| m7 | MINOR | systemd: SIGTERM not handled, relative paths | `KillSignal=SIGINT`, `WorkingDirectory` |
| m8 | MINOR | FluentCommandLineParser swap vs the DLL start check (`Program.cs:29`) | P1 updates the check |
| m9 | MINOR | H1 overstated what the queue restores | H1 says s2data-to-s2data only |
| n1–n5 | NIT | ownership labels C3/C4; C5 callers; `updateStatus` arity; legacy job stays `net8.0`; first-start header resync | all fixed |
