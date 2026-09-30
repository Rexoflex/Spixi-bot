# Review brief — <batch name>

Work order for the adversarial loop (docs/process.md). The VERDICT is written back into §5 of this
file when the loop closes.

## 1 · What the batch is
| Item | What landed | Where (files) |
|---|---|---|

Not built, and why: <list>

## 2 · How to run
- Repo / commit range: `<base>..<head>`
- Build + tests: <commands>; known failures: <none / list>

## 3 · Auditor scopes (disjoint)
| Auditor | Scope | Focus |
|---|---|---|
| A | | correctness, edge cases |
| B | | security, threat-model rows |
| C | | tests: does each test fail when the behaviour breaks? (mutate) |

## 4 · Non-negotiables and accepted dials
- Must hold: <invariants>
- Accepted (do not re-open): <list with DECISIONS rows>

## 5 · Verdict
<PASS / NOT CLEAN> · rounds: <n> · MAJOR/MINOR/NIT per round · fixes landed · residuals with owners.
