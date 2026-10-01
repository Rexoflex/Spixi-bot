# Session 2 · review round 1

2026-10-01. Three read-only Opus auditors with disjoint scopes (process §G #46 loop). Findings were verified at source
before fixing (`file:line` below are the auditors', re-checked).

| Auditor | Scope | Verdict |
|---|---|---|
| R1 security | B0 decision: `b0-check.md` (then `b0-hotfix.md`), D-041/D-038, threat model §4/§5, BE-01…03 | NOT CLEAN — 2 MAJOR, 6 MINOR, 4 NIT |
| R2 design | `harness.md`, D-031 | NOT CLEAN — 1 MAJOR, 5 MINOR, 4 NIT |
| R3 consistency | `be-asks.md` completeness; cross-doc consistency | NOT CLEAN — 1 MAJOR (same as R1-M1), 9 MINOR, NITs |

## MAJOR findings and fixes

| # | Finding (verified) | Fix |
|---|---|---|
| R1-M1 / R3-M1 | The operator check sent the operator to the admin web page (cost step), which renders member nicks with `innerHTML` (`html/js/bot/settings.js:151`) on the same origin as the wallet API (`sign`, `getwalletbackup`, `addtransaction`, `shutdown`; Core `f6fb55b` `GenericAPIServer.cs:229,249,361,381`). T3 was claimed covered but was not. | `b0-check.md` rev 2: rule "nobody opens the admin web page until shutdown"; costs via `curl sb_getGroups`/`sb_updateGroup` (`APIServer.cs:398-424`); T3-by-rule listed as accepted; D-041, threat model, BE-02 updated. |
| R1-M2 | With no `addApiUser`, every caller is allowed (`GenericAPIServer.cs:529-538`), methods are plain GETs, no Origin check: a localhost bind alone does not stop local pages/processes. The login was added only if the bind was public. | Login always (no `:` in the password, `Config.cs:206-210`), `disableWebStart = 1` (`Node.cs:246-249`), 401 test; local API misuse listed as accepted. |
| R2-M1 | The app reaches a bot via presence (`relayNode`), not `connectTo`; `friend.bot` is false until `acceptAddBot`; a headless client needs app glue (`IxianNode` 12 abstract members, `PendingMessageProcessor`, stream processor, TIV). The grading credited "real presence binding". | `harness.md` rev 2: F3 corrected, F5 added, W9 (direct join = known divergence, stub seed fallback), W10 (glue), speed re-estimated (~3 days), D-031 locks only after the spike. |

## MINOR / NIT — fixed

Password-with-`:` trap; Windows Host-header check; key misuse vs funds wording; rollback incl. `groups.dat`/`settings.dat`;
shutdown via API and spoofed nick/avatar/reaction added to accepted risks; `apiBind` is config-file only; default
port written; two ports + `-i` + `--disableWebStart` in the fixture; pre-generated wallet pool; store-mode crash on
`leaveConfirmed` as an expected event; A′ (compile app source) and "headless app" rejected with reasons; timeouts tied
to `CoreConfig.cs:95`; B maintainability 4→3 (B 31); CI needs both SDKs; BE asks: data copy (BE-32), leaver count
(BE-31), `getGroups`/`msgReport` and A Q10 in BE-26, "we measure" list for A Q1/Q2/Q5, acks/paging FYI, moot reasons
corrected, B1b asks re-headed; D-008/D-034/D-038/D-039 amendment notes; D-002 wording; `store\|redesign` escaped;
roadmap revision line, "legacy hotfix", D4 row; base-options session-2 notes; `b0-hotfix.md` renamed `b0-check.md`.

Deferred to export: `docs/next-session-prompt.md` and the Project copies (rewritten in the session-2 handoff).

## Next

Round 2: a fresh break-my-verdict reviewer attacks the fixes.
