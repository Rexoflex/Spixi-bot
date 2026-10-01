# Review brief — session 6, B1b plan (`docs/design/b1b-port.md`)

## 1 · What the batch is
| Item | What landed | Where |
|---|---|---|
| B1b plan | port delta (compile breaks, behaviour changes, Linux start), wire diff, PR A/PR B plan, CI layout, measurements, risks, first-test path, grading | `docs/design/b1b-port.md` |
| Decisions | D-049 shape, D-050 wire diff + B1a closed, D-051 pin file + nightly, D-052 first test; D-030 marked amended | `DECISIONS.md` |

Not built: no code (session 6 is plan only; compile/tests run in CI only, D-019).

## 2 · How to check
Read-only. Bot = working tree `SpixiBot/` (legacy `a5a3442` code). Core: `git --no-optional-locks -c maintenance.auto=false show f6fb55b:<path>` / `097341a:<path>` in `../Ixian-Core`. Never run `git fetch` or anything that writes.

## 3 · Auditor scopes (disjoint)
| Auditor | Scope | Focus |
|---|---|---|
| A | plan §2–§4 vs Core k and bot source | every row true? every fix compiles and keeps behaviour? missed breaks (sweep all 13 bot .cs files) |
| B | plan §5–§7, §9 vs harness/CI/bot | serial queue design, payment stub fidelity, CI layout, harness on Linux, needles, wire-diff design, measurements |
| C | plan §0–§1, §8, §10–§11 vs DECISIONS D-008…D-052, roadmap, test-strategy, threat-model, research B/D | decisions honoured, nothing lost or contradicted, first-test safety, grading honest |

## 4 · Non-negotiables and accepted dials
- Must hold: no wire change on purpose; B1a suite semantics kept; no BE ask needed; every claim cites file:line; H marked.
- Accepted (do not re-open): D-049 (two PRs, payment stub, serial queue, suite layout), D-050, D-051, D-052, D-042, D-043, D-044, D-046.

## 5 · Verdict
**PASS — CLEAN after 3 rounds** (plan rev 4, `docs/design/b1b-port.md`).

| Round | Reader | MAJOR | MINOR | NIT |
|---|---|---|---|---|
| 1 | three disjoint auditors (A port delta, B mechanics, C decisions/safety) | 13 | 31 | 17 |
| 2 | fresh break-my-verdict reader | 3 | 9 | 5 |
| 3 | fresh break-my-verdict reader | 0 | 3 | 3 |

MAJORs (r1): C9 missing `prepareStorage` (crash on a fresh dir) · wire-diff list incomplete · invented queue bound ·
no legacy source for the baseline job · two unobservable measurements · pin not the release tag (`1ff5435`) · phones
cannot run testnet · test instances not isolated from the real data folder · wrong testnet ports · security gate,
tests for new logic and the introduced-vs-inherited sweep missing. (r2): queue backpressure would block pool threads ·
queue stop keyed on a status the bot never sets · app Preferences shared across data folders.
Every finding was verified against source before fixing (lead re-checked the pin, C9, ports, the status setter and
Preferences). Two needed Damir (test devices → two Windows desktop instances; no separate-session review).
Same class twice: "copy legacy behaviour" claims that did not hold at k (r1 B1 bound, r2 M1 backpressure) → design
changed (non-blocking queue) rather than patched.
Recorded, not fixed in the plan (export items): stale self-test count "11" in CLAUDE.md §6 / D-047 (matrix has 12);
be-asks BE-05/06/07 status; docs to update (CLAUDE.md §4/§6, roadmap, test-strategy, harness.md W7, threat-model).
Residual H (proven in PR A CI or the first test): RocksDB Linux native load; Linux stack-overflow stderr; unpackaged
Windows app honours `ixian.cfg`, two instances, Preferences per user; old (< v0.9.8) clients' payments.
