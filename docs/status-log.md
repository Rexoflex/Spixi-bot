# Status log

One entry per session, newest last. `CLAUDE.md` §6 only points at the latest handoff.

## Session 1 — 2026-10-01 — process + research + base design

- FORGE process set up: `CLAUDE.md`, `DECISIONS.md` (D-001…), `docs/process.md`, templates,
  examples, lessons.
- Research audit (5 parallel Opus agents, source-verified): delivery & storage (A), security &
  moderation (B), QuIXI evaluation (C), client contract (D), external best practice (E).
- Base designs graded, threat model, test strategy — see `docs/design/`, `docs/threat-model.md`,
  `docs/test-strategy.md`.
- Adversarial review round 1: NOT CLEAN (13 MAJOR, 13 MINOR, 4 NIT) → rev 2 of design, threat model,
  test strategy, roadmap, process; research errata.
- Round 2: 0 old MAJOR open, 1 new MAJOR (B0 check insufficient) → rev 3. Round 3: 1 MAJOR (B0 missed
  `leave`) → fixed; round-4 check CLEAN → `docs/reviews/session-1-verdict.md`.
- No code changed. Cloud cannot reach nuget.org → no build here (D-019).

## Session 2 — 2026-10-01 — B0 dropped to a check, BE asks, harness graded

- Reverse interview: D-021 confirmed, D-027 amended (no migration), D-035…D-040 recorded, then superseded by D-041 when
  Damir asked whether B0 was worth doing: no B0 code, a ~10-minute operator check, "we moved" post at cutover.
- `docs/be-asks.md` (32 asks by blocking batch), `docs/design/harness.md` (D1: out-of-process driver wins, 41/50),
  `docs/design/b0-check.md`; roadmap rev 4, threat model, test strategy synced.
- Review: round 1 NOT CLEAN (3 MAJOR: admin-page XSS in the check, API open to local callers without a login, harness
  join path) → fixed; round 2 CLEAN (0 MAJOR). No code changed.

## Session 3 — 2026-10-01 — harness spike proves itself in CI

- Reverse interview (3 rounds): measure startup first, permanent self-test job, no wallet pool, Core pinned by SHA,
  both app modes as two cross cases, stop-and-report if F4/W9 fail, ≤ 8 CI pushes, CI read in the browser.
- PR #1 on the fork (`spike/harness` → `rework/bot`), 8 pushes: legacy build (Linux + Windows), start smoke,
  `tests/SimClient` (Core k, .NET 10, store|redesign), `tests/Harness` (xUnit v3), `Join_Post_Receive`.
- Findings: the old bot cannot run on Linux (registry; settings-save recursion hangs the API) → harness on Windows
  (D-043); F4 refuted — the bot needs a block header, interim source = testnet seeds (D-044); W9, W10 verified.
- Green: Join_Post_Receive passes in both cross cases; the self-test removes the relay and both cases fail with
  `W8-RELAY-MISSING[case]`. D-031 locked (A, 39/50). Review: round 1 NOT CLEAN (3 MAJOR) → fixed; round 2 CLEAN.
- No bot code changed (CI-only patches exist for Linux start and the W8 break).