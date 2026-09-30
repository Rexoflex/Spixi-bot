# Session prompt — template

Copy, fill in, paste as the first message of a session.

```
Read CLAUDE.md, then docs/handoff-<latest>.md, then DECISIONS rows <list>.

## Outcome (O)
For: <member | admin | self-hoster | BE engineer>
After this session they can: <one sentence>
We know it worked when: <acceptance criteria — each one a test name or a measurement>
  - <criterion 1 → test/measurement>
  - <criterion 2 → test/measurement>

## Scope
In: <items>
Out (do not touch): <items>
Constraints: <compat, security, protocol approvals>

## Reverse interview (R)
Before any build: read the code the scope touches, then ask me the questions you need, as
clickable options, one round at a time. Point out anything vague or contradictory. Do not start
building until I say "go".

## Grade (G)
Design choices with real alternatives: three options, graded with the rubric in docs/process.md,
then fix the winner's weaknesses. Code: every gate in docs/process.md §G2, then the adversarial
review loop until CLEAN.

## Export (E)
End with: handoff (docs/templates/handoff.md), status-log entry, lessons, DECISIONS rows,
commit message, and skill proposals for any procedure we repeated.
```
