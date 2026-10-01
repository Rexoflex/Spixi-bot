# SHA map — fork history rewrite (D-046, 2026-10-01)

`rework/bot` was rewritten with `git filter-branch --msg-filter` to remove AI attribution lines. Code, authors and
dates are unchanged (the trees are identical: `git diff 266ff99 59c1f74` is empty). Older docs cite the OLD short
SHAs; use this table. GitHub still serves the old commits by SHA and under PR #1/#2 (`refs/pull/*`).

| Old | New | Subject |
|---|---|---|
| `266ff99` | `59c1f74` | Merge pull request #2 from Rexoflex/b1a/characterization |
| `0b3d65b` | `8bddf2e` | B1a push 2: review fixes (link proofs for markers) + session-4 docs |
| `4de5a2d` | `fb0d24c` | B1a push 1: D-044 infra guard + join/relay/history characterization |
| `1b8a098` | `c876970` | Merge pull request #1 from Rexoflex/spike/harness |
| `976b3f5` | `914e304` | D-044 locked: testnet seeds + infra guard; mainnet rejected |
| `b98772c` | `aa6520b` | Session 3: harness spike docs - D-031 locked, D-043, D-044 |
| `01a3f35` | `e21ec02` | Harness spike 8: review fixes (W8 proof isolates the relay, per-case markers) |
| `a6a5875` | `fb96a81` | Harness spike 7: harness on testnet seeds; self-test proves W8 |
| `5d9f8db` | `d2556cf` | Harness spike 6: join retries like the app; testnet-seed measurement |
| `e759576` | `02cd58e` | Harness spike 5: SimClient exits explicitly; step time limits |
| `c8b0aba` | `a5acecc` | Harness spike 4: SimClient client manager needs >= 3 neighbours |
| `9de0b19` | `f94c40f` | Harness spike 3: Windows-only harness, start-up diagnostics |
| `4946b9f` | `ef11b94` | Harness spike 2: start fixes per OS, RocksDB pin, failure annotations |
| `b573e00` | `e87b26e` | Harness spike: legacy CI build + smoke, SimClient, Join_Post_Receive |
| `0e5cbeb` | `2820f9b` | Session 2: B0 to operator check, harness graded, BE asks deferred |
| `2c220c2` | `c45d317` | Record base C, switch-over leaning to a new address (D-034), deep-link join |
| `880cf1f` | `80628c2` | Record Damir's decisions: 8 weeks full scope, crash rule, operator |
| `ca50bfb` | `6927f1c` | Session 1: FORGE process, research audit, graded base design |
