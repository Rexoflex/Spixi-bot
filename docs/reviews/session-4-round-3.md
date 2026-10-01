# Session 4 — review round 3 (Opus, over the round-2 fixes)

Verdict: **CLEAN.** All ten round-2 fixes correct; normal run unchanged (new waits only in failure branches); every
self-test still reaches its marker (`seed-none`: the API answers 0 before the network starts, `Node.cs:244` before
`:270`); cursor formats match (Core k `hashToString` lowercase; Guid "N" round-trips).

| # | Sev | Finding | Fix |
|---|---|---|---|
| 1 | NIT | `BotSeed` comment still said "~1 s" | 1.1–4.1 s |
| 2 | NIT | `refresh_sent.cursors` read before Core re-reads at `channel`; comment overclaimed | comment corrected |
