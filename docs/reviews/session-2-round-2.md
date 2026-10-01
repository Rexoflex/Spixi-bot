# Session 2 · review round 2 (break-my-verdict)

2026-10-01. One fresh read-only Opus reviewer attacked the round-1 fixes (`session-2-round-1.md`).

**Verdict: CLEAN — 0 MAJOR**, 6 MINOR, 3 NIT. Confirmed at source: API routing and `/status`; Basic auth enabled when
users exist (Core `f6fb55b` `GenericAPIServer.cs:571`); `sb_getGroups` formats; `sb_updateGroup` with
`group == origGroup` keeps the index; no other member-data-to-HTML path; harness totals A 41 · B 31 · C 27; D-008,
D-034, D-038, D-039, D-041 coherent.

| # | Finding (verified) | Fix |
|---|---|---|
| m1 | The bot logs every config line verbatim (`Config.cs:185`) → the API password lands in `ixian.log` | Step 2: clean/rotate the log, `chmod 600` config and logs; §4 row updated |
| m2 | Config lines are split on `=` (`Config.cs:173-179`) → a password with `=` is cut | "no `:` or `=`", `openssl rand -hex 24`; mirrored in BE §0 |
| m3 | `curl -u user:pass` leaks to shell history / `ps` | `curl -u user` (prompt) |
| m4 | No fix if the Host-header test reaches the bot; with no `apiAllowIp` every IP is allowed | Step 1 sets `apiAllowIp = 127.0.0.1` and `::1` |
| m5 | W9 recipe incomplete: pending sends need `online` + fresh `updatedStreamingNodes`, errors reset them | W9 rewritten with the pinning rule and citations |
| m6 | D-031 cited F5 for presence; BE §0 lacked backup/traps | D-031 → F3/W9 + F5/W10; BE §0 says "follow §2 exactly" |
| NIT | `ixian.cfg` location, stop before rollback, URL-encode group names | Fixed in `b0-check.md` |

The #46 loop stops here (a round with 0 MAJOR). D-031 still locks only after the B1a spike (its own condition).
