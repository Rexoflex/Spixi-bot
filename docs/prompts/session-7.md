# Session 7 prompt (paste as the first message)

```
Read CLAUDE.md, then docs/handoff-2026-10-01-s6.md, then DECISIONS D-049, D-050, D-051, D-052 (and D-019, D-042,
D-043, D-044, D-045, D-046, D-047, D-048), then docs/design/b1b-port.md (all), docs/lessons.md L22, L25, L29–L33.
Precondition: rework/bot = origin, HEAD "Session 6 close: B1b plan graded (review CLEAN), B1a closed"; no
.git/objects/maintenance.lock in ../Ixian-Core.
Already decided: everything in D-049–D-052 (two PRs; payment stub; non-blocking serial s2data queue in
SpixiBot.Common with unit tests; suite on the ported bot, Linux normal + 12 self-tests, Windows normal; legacy normal
job from c44651c + wire diff; core.pin = 1ff5435; dependency review in PR A, CodeQL + nightly in PR B; no
separate-session review). BE asks deferred (D-042). No AI attribution (D-046).
Work directly in the connected Spixi-bot folder; git with --no-optional-locks -c maintenance.auto=false; never fetch.
Compile/tests run in CI only (D-019): I push, CI reports; read CI through the built-in browser.

## Outcome (O)
For: me (owner), later the BE reviewer.
After this session: PR A (B1b port) is open and green.
We know it worked when:
  - CI: ported build + start smoke Linux + Windows, Linux stop smoke ("Spixi Bot stopped." within 30 s)
  - harness normal 14/14 on Linux and Windows against the ported bot
  - all 12 self-tests on Linux fail every case with their own marker
  - legacy normal green; wire-diff shows only the b1b-port.md §4 rows (warnings listed)
  - SpixiBot.Common.Tests green; dependency review no high finding
  - b1b-port.md tables "as built"; M1, M3, M4 recorded; introduced-vs-inherited sweep written

## Scope
In: PR A items A0–A9 (b1b-port.md §5). Out: PR B items P1–P8, any wire change of ours, BE asks, B2+ fixes.

## Reverse interview (R)
Before any build: re-read the files A0–A9 touch, write the owner contract (§5.1), then ask me clickable questions,
one round at a time (e.g. library/test project names and layout, Stryker trial config, push budget). Point out
anything vague or contradictory. Do not start until I say "go".

## Grade (G)
Code: process.md G2 gates; adversarial review loop until CLEAN BEFORE the first push (L22); pre-push needle check.

## Export (E)
Handoff, status log, lessons, DECISIONS rows (from D-053), commit message (no AI attribution), session-8 prompt
(PR B), skill proposals.
```
