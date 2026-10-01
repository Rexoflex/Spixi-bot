# Session 5 prompt (paste as the first message)

```
Read CLAUDE.md, then docs/handoff-2026-10-01-s4.md, then DECISIONS D-045, D-046 (and D-031, D-042, D-043, D-044),
then docs/design/harness.md §4 (W7, W12) and §7, docs/research/D-client-contract.md §1, §3, §7, §8, and
docs/test-strategy.md §4.
Already decided: base C (D-020), v1 scope (D-021), harness = out-of-process client driver (D-031), legacy harness on
Windows (D-043), header source = testnet seeds + infra guard (D-044, built), B1a layout + one self-test job per break
(D-045), no AI attribution in commits (D-046), BE asks deferred (D-042): plan nothing that needs a BE answer;
store-mode Core = k until BE-04 (W3).
Work directly in the connected Spixi-bot folder; run git with --no-optional-locks (L17).
Compile/tests run in CI only (D-019): I push, CI reports; read CI through annotations and the built-in browser (L21).

## Outcome (O)
For: me (owner) and later the BE reviewer.
After this session: B1a covers the rest of research D §8 that needs no bot change.
We know it worked when:
  - reactions and deletes (§8 item 4), unknown codes / extra BotInfo bytes (item 5) and info variants (item 7) run
    green in CI on both apps
  - W12 crash handling exists and the leave path (item 6) is characterized (store expects `crashed`)
  - each new scenario fails on its own deliberate break in its own self-test job (L5, L18, L23)

## Scope
In: §8 items 4, 5, 6 (after W12), 7. Out: any bot code change; store-Core fidelity (BE-04); W7 wire snapshots unless cheap.

## Reverse interview (R)
Before any build: re-read the source the scenarios touch, then ask me clickable questions one round at a time.
Point out anything vague or contradictory. Run an adversarial review against the Core source before the first push (L22).
Do not start until I say "go". Budget: say how many CI pushes you expect, and stop and report at the limit.

## Grade (G)
Code: CI green, review loop (in-session Opus) until CLEAN.

## Export (E)
Handoff, status log, lessons, DECISIONS rows, commit message (no AI attribution, D-046), session-6 prompt, and skill proposals.
```
