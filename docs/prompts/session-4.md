# Session 4 prompt (paste as the first message)

```
Read CLAUDE.md, then docs/handoff-2026-10-01-s3.md, then DECISIONS D-031, D-043, D-044 (and D-042),
then docs/design/harness.md §4–§7, docs/research/D-client-contract.md §1, §4, §8, and docs/test-strategy.md §4.
Already decided: base C (D-020), v1 scope (D-021), harness = out-of-process client driver (D-031, locked),
legacy harness on Windows (D-043), BE asks deferred (D-042): plan nothing that needs a BE answer;
store-mode Core = k until BE-04 (W3).
Work directly in the connected Spixi-bot folder; run git with --no-optional-locks (L17).
Compile/tests run in CI only (D-019): I push, CI reports; read CI through annotations and the built-in browser (L21).

## Outcome (O)
For: me (owner) and later the BE reviewer.
After this session: B1a characterization grows on a harness whose header source is settled.
We know it worked when:
  - D-044 is decided (frozen header fixture · stub seed · keep testnet), and CI follows that decision
  - the next research-D §8 scenarios (join handshake, relay/echo/msgReceived, history cursor) run green in CI
  - each new scenario fails on its own deliberate break, proven in the self-test job (L5, L18)

## Scope
In: D-044, B1a scenarios from research D §8 items 1–3 (more if cheap). Out: any bot code change; leave/leaveConfirmed
(needs W12 crash handling first); store-Core fidelity (BE-04).

## Reverse interview (R)
Before any build: re-read the source the scenarios touch, then ask me clickable questions one round at a time.
Ask first whether the header fixture is worth doing now. Point out anything vague or contradictory.
Do not start until I say "go". Budget: say how many CI pushes you expect, and stop and report at the limit.

## Grade (G)
Code: CI green, review loop (in-session Opus) until CLEAN.

## Export (E)
Handoff, status log, lessons, DECISIONS rows, commit message, session-5 prompt, and skill proposals.
```
