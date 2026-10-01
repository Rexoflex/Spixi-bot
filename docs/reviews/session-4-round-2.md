# Session 4 — review round 2 (post-CI, Opus; CI run 36856754230 green)

Verdict: NOT CLEAN (1 blocking MINOR) — all fixed in push 2.

| # | Sev | Finding | Fix |
|---|---|---|---|
| 1 | MINOR (blocking) | `INFRA-TESTNET-UNREACHABLE` also attached when the bot API never answered (bad auth, lost port) — label without proof (L18) | infra label only after the API answered `result: 0`; otherwise a plain failure |
| 2 | MINOR | `ACK-MISSING` proved poster health, not its link | requires the poster's own echo |
| 3 | MINOR | `W8-RELAY-MISSING` proved receiver health, not its link | requires an acked probe post from the receiver |
| 4 | MINOR | `CURSOR-IGNORED` assumed the reader sent cursor = m4 | `refresh_sent.cursors`; steps C and D assert the cursor |
| 5 | NIT | failed post waited 20 s | `WaitPostedAsync` with `failIf` |
| 6 | NIT | tautological `connected < accepted` assert | removed |
| 7 | NIT | "connected once ⇒ live relay" too strong | comment softened |
| 8 | NIT | dead `BotProcess.DisposeAsync`; hand-built marker tag | removed; `Markers.Tag` |
| 9 | NIT | "~1 s" header comment stale | 1.1–4.1 s |
| 10 | NIT | README missing `--wallet-password` | added |

Checked clean: workflow marker logic (build failure / zero tests cannot pass a self-test), needle uniqueness, log
redaction (`addApiUser`), only fresh testnet addresses in public logs.
