# Session 3 · review round 1 — harness spike (in-session Opus, adversarial)

Scope: `.github/workflows/harness.yml`, `tests/smoke/*`, `tests/SimClient/**`, `tests/Harness/**`, `docs/design/harness.md`.
State reviewed: after CI run 36848444730 (green: Join_Post_Receive passes, self-test fails with the marker).
**Verdict: NOT CLEAN** — 3 MAJOR, 7 minor, 8 nit.

| ID | Finding | Disposition |
|---|---|---|
| M1 | The W8 marker was attached to any receiver timeout, incl. a poster that never reached the bot or a crashed receiver → the L5 self-test could pass for the wrong reason | Fixed: the test first waits for the bot's ack of **this** post id; the marker is thrown only if the receiver stayed healthy |
| M2 | The self-test accepted the marker from one case only | Fixed: per-case markers `W8-RELAY-MISSING[store->redesign]` / `[redesign->store]`; the job requires both |
| M3 | `harness.md` no longer matched the code (F4 "hypothesis", stub-seed rule, wallet pool, Linux CI, self-hosting score) | Fixed in docs: rev 3 + §7; score 41 → 39; D-031 locked with D-043/D-044 |
| m1 | A reconnect could deliver the post by history replay, not relay | Fixed: the receiver must have exactly one `connected` before delivery |
| m2 | A bot whose start threw was never disposed | Fixed: every registered process is disposed; dispose is idempotent |
| m3 | Copying `bin/` could carry an old log, wallet, config or data | Fixed: the copy skips `Data/`, `activity/`, root `ixian*.log` / `ixian*.cfg` |
| m4 | Default seed `none` always fails; doc comment on the wrong member | Fixed: default `testnet`, comments moved |
| m5 | Any ack was accepted as "the post's ack" | Fixed with M1 (ack id = posted id) |
| m6 | Free-port race (two ports could collide) | Fixed: both listeners held, then released |
| m7 | `addApiUser = harness:<pw>` in public annotations and logs | Fixed: redacted in reports, annotations and artifacts |
| n1 | "no spaces" comment not enforced | Fixed: own-console args are checked |
| n2 | Log tail can split a multi-byte UTF-8 char | Deferred, recorded in `harness.md` §7 (bot log is ASCII) |
| n3 | Dead Linux patch step and SimClient smoke in the harness job | Fixed: removed; the `legacy` job stays as build + start evidence |
| n4 | Event lists in code/README incomplete | Fixed |
| n5 | Stale text (job name, start mode, F4) | Fixed |
| n6 | Waits ignored `error`/`fatal` | Fixed: `fatal`/`crashed` end any wait; a join `error` ends the join wait |
| n7 | A failing report could hide the test result | Fixed: `SafeReport` |
| n8 | Store mode not marked as hypothesis | Fixed: W3 note in the test output |
