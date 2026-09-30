# Batch rubric — template

Fill in per PR. Every row must be ✅ or carry a written reason. Paste into the PR description.

| Gate | Result | Evidence |
|---|---|---|
| Outcome met | ✅ / ❌ | acceptance tests: <names> |
| Build (CI) | | run link |
| Unit + property tests | | new tests: <names> |
| Contract tests (store + redesign) | | |
| Integration (multi-node) | | scenario names |
| Mutation score (touched modules) | <score> / threshold <n> | Stryker report |
| Fuzz (parsers touched) | n/a / <hours> no crash | |
| Threat model check | | rows affected in docs/threat-model.md |
| No address/content in logs or metrics | | log/metric test name |
| Adversarial review | CLEAN after <n> rounds | verdict: <doc §> |
| DECISIONS rows | | D-<nnn> |
| Docs (status log, handoff) | | |
| Compat impact | none / safe extension / needs BE | |
