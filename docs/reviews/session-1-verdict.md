# Review verdict — Session 1 (process, research, base design)

**VERDICT: PASS (CLEAN at round 4)** · rounds: 4 · docs only, no code.

| Round | MAJOR | MINOR | NIT | Result | Report |
|---|---|---|---|---|---|
| 1 | 13 | 13 | 4 | NOT CLEAN | `session-1-round-1.md` |
| 2 (over rev 2) | 0 old open · 1 new | 12 | 8 | NOT CLEAN | `session-1-round-2.md` |
| 3 (over rev 3) | 1 (leftover) | 4 new | 5 | NOT CLEAN | `session-1-round-3.md` |
| 4 (closing check) | 0 | 0 | 2 (fixed) | **CLEAN** | `session-1-round-4.md` |

Blind second grading (round 2): A′ 32 · B 27 · **C 40** · E 34 vs author 33 · 30 · 39 · 35 — same winner, no cell > 1 point apart.

## The MAJORs that mattered most

| # | Mechanism | Fix | Lesson |
|---|---|---|---|
| R1-M1/M2 | The security rule S1 assumed "all client traffic is signed" and "ids are unique". False: six request types are unsigned, three state types use fixed ids, clients retry for 5 days with the original timestamp. S1 as written would have dropped honest clients. | S1 per message class; unsigned = read-only; complete per-type table is D2's first input. | Classify per message type with `file:line` before writing a rule (lessons L13). |
| R1-M3 | Admin via text commands ignored the signed `kickUser`/`banUser` actions both apps already send. | Admin surface = design item D3, existing actions as the lead candidate. | Check what the clients already do before designing a new path. |
| R1-M4 | "Never send `leaveConfirmed`" swapped a one-time crash for a permanent invisible connection. | Graded in D4 with the real costs. | A "safe" omission can have its own failure mode. |
| R1-M5/M6 | The delivery proxy would have counted lost messages as delivered; v1 missed ack-before-process and the gap race. | Hand-off rate ≠ delivery; ground truth for launch; "no acknowledged message lost". | Name a proxy as a proxy. |
| R1-M11 → R2-N-M1 → R3-M1 | Production is exposed today; the first hotfix check (`sender == connection`) did not stop impersonation (Core does not authenticate the connection), and the second missed `leave`. | B0 verifies the signature against `endpoint.presence.pubkey` on chat, nick, avatar, reaction, delete and `leave`; log-only first. | A control for a live CRITICAL needs its own adversarial pass; two rounds were needed. |

## Residuals (owned)

| Item | Owner | Where |
|---|---|---|
| Reading-as-others (Core T2) stays open after B0 | BE | roadmap BE backlog, threat-model §5 |
| Per-type signed/unsigned table is from reviews, not an exhaustive audit | D2 | D-022 |
| FORGE source = a video Damir shared; only its description was used | — | process.md header |
