# Session 6 prompt (paste as the first message)

```
Read CLAUDE.md, then docs/handoff-2026-10-01-s5.md, then DECISIONS D-047, D-048 (and D-008, D-019, D-030, D-042,
D-043, D-045), then docs/roadmap.md (B1a, B1b), docs/design/harness.md §4 (W3, W7) and §7, docs/test-strategy.md §4,
and research B §0 (why the bot does not compile against Core HEAD).
Already decided: base C (D-020), v1 scope (D-021), harness = out-of-process client driver (D-031), legacy harness on
Windows (D-043), header source = testnet seeds + guard (D-044), one self-test job per break (D-045), B1a items 1–7
characterized (D-045, D-047), no AI attribution in commits (D-046), BE asks deferred (D-042): plan nothing that needs a
BE answer; store-mode Core = k until BE-04 (W3).
Work directly in the connected Spixi-bot folder; run git with --no-optional-locks (L17).
Compile/tests run in CI only (D-019): I push, CI reports; read CI through the built-in browser (L21).
Use parallel agents where they help quality (build on a written contract, independent reviewers; L25).

## Outcome (O)
For: me (owner) and later the BE reviewer.
After this session: B1b is planned and graded, and B1a is closed or has a short, agreed remainder.
We know it worked when:
  - a graded B1b plan exists: target .NET 10, the Core pin (hypothesis k 097341a, D-030/D-042), how the bot's API
    breaks are fixed (BotInfo serverName, research B §0), Linux start, and how the B1a suite runs against the ported bot
  - the decision on W7 wire snapshots and traffic captures is recorded (do now / fold into B1b / drop)
  - the "first hands-on test" path is written down (network, where the test bot runs, who joins)

## Scope
In: B1b plan + grading, B1a close-out decision, first-test path. Out: the port itself (next session, after "go"), any
wire change, BE asks.

## Reverse interview (R)
Before any plan: re-read the source the port touches (bot vs Core k API differences), then ask me clickable questions
one round at a time. Point out anything vague or contradictory. Do not start until I say "go".

## Grade (G)
Plan: rubric `docs/templates/batch-rubric.md`; adversarial review against Core k and bot source until CLEAN.

## Export (E)
Handoff, status log, lessons, DECISIONS rows, commit message (no AI attribution, D-046), session-7 prompt, and skill proposals.
```
