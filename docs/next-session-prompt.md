# Next-session prompt (paste as the first message)

```
Read CLAUDE.md, then docs/handoff-2026-10-01.md, then DECISIONS D-014, D-020…D-033.
Work in the cloud clone; compile/tests run in CI or on my machine (D-019).

## Outcome (O)
For: me (owner) and the production operator.
After this session: every open 🟡 row that blocks B0/B1a is decided, the B0 hotfix is specified
(diff + operator checklist, log-only first), the BE asks are written and ready to send, and
design item D1 (harness) is graded so B1a can start.
We know it worked when:
  - DECISIONS has my answers for D-020, D-021, D-032, D-014 wording, and who operates B0 (D-029)
  - docs/design/b0-hotfix.md exists with the exact code change per checked message type,
    the log-only counters, and a rollback step
  - docs/be-asks.md lists every BE question with the batch it blocks
  - docs/design/harness.md grades three options and names the winner (D-031 updated)

## Scope
In: the four items above. Out: any rework code beyond the B0 spec.

## Reverse interview (R)
Before any build: re-read the source the scope touches, then ask me clickable questions one round
at a time. Point out anything vague or contradictory. Do not start until I say "go".

## Grade (G)
Designs: three options, rubric in docs/process.md, fix the winner's weaknesses.
Review: in-session Opus loop until CLEAN; B0 is security → also one separate-session review.

## Export (E)
Handoff, status log, lessons, DECISIONS rows, commit message, and skill proposals.
```
