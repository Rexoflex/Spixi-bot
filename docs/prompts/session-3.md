# Session 3 prompt (paste as the first message)

```
Read CLAUDE.md, then docs/handoff-2026-10-01-s2.md, then DECISIONS D-019, D-030, D-031, D-041,
then docs/design/harness.md and docs/test-strategy.md §4.
Already decided: base C (D-020), v1 scope (D-021), new address (D-034, lean), B0 = operator check only (D-041),
harness = out-of-process client driver (D-031, locks after the spike),
BE asks deferred (D-042): plan nothing that needs a BE answer; store-mode Core = k until BE-04 (harness W3).
Work directly in the connected Spixi-bot folder; run git with --no-optional-locks (L17).
Compile/tests run in CI only (D-019): I push, CI reports.

## Outcome (O)
For: me (owner) and later the BE reviewer.
After this session: the harness spike exists as a PR into rework/bot and proves itself in CI.
We know it worked when:
  - a legacy CI job builds the bot with .NET 8 + Core f6fb55b on Linux and runs on rework/bot + PRs
  - tests/SimClient (Core 097341a, .NET 10) and tests/Harness exist, minimal glue only (harness.md W10)
  - Join_Post_Receive passes in CI, and fails when the bot stops relaying chat (W8, L5)
  - F4 (bot without DLT) and W9 (direct join) are recorded as verified or not; D-031 locked or revised

## Scope
In: the spike above. Out: B1a characterization scenarios beyond Join_Post_Receive; any bot code change.

## Reverse interview (R)
Before any build: re-read the source the spike touches, then ask me clickable questions one round at a time.
Point out anything vague or contradictory. Ask whether each piece is worth doing. Do not start until I say "go".

## Grade (G)
Code: CI green, review loop (in-session Opus) until CLEAN.

## Export (E)
Handoff, status log, lessons, DECISIONS rows, commit message, and skill proposals.
```
