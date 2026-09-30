# Closing check, round 4: Spixi-Bot rework, session 1 (over the round-3 fixes)

Reviewer: fresh read-only reviewer, 2026-10-01. Source: bot `SpixiBot` (`Network/StreamProcessor.cs`), Core `f6fb55b`
(`git show`).

## Verdict: **CLEAN**. 0 MAJOR open, 0 new MAJOR.

| | Count |
|---|---|
| R3-M1 (MAJOR) | RESOLVED |
| R3-m1…m4 (MINOR) | 4 RESOLVED |
| R3-n1…n5 + carried n-8 (NIT) | 6 RESOLVED |
| New MAJOR / MINOR | 0 / 0 |
| New NIT | 2 |

## Findings

| Item | Status | Evidence / note |
|---|---|---|
| R3-M1: B0 verifies `leave` | RESOLVED | roadmap.md:21, D-029, threat-model.md:47 all list chat/nick/avatar/reaction/delete/`leave` with `sender == connection` and a signature check against `endpoint.presence.pubkey`. roadmap.md:21 also warns against `serverPubKey`. Source: legacy `sendLeave` signs when `friend.bot` (Core f6fb55b `CoreStreamProcessor.cs:2121-2140`). A client that leaves is already a joined bot contact, so honest leaves arrive signed. The bot's unchecked `onLeave(message.sender)` (bot `StreamProcessor.cs:190-191`) is the point where B0 adds the check. The scope is honest: "closes posting-as-others and forced-leave; does not close reading-as-others (T2)". |
| R3-M1: "public keys to members only" | RESOLVED (dropped) | D-029 drops it, with the reason (one unsigned `getInfo` makes a member). No B0 text still promises it. |
| R3-m1 member definition | RESOLVED | Made moot by the drop above. |
| R3-m2 log-only first | RESOLVED | roadmap.md:21 (log-only, count failures per type, test with store + redesign apps, then enforce), D-029, threat-model.md:47. The acceptance column names the log-only phase report. |
| R3-m3 estimate wording | RESOLVED | roadmap.md:10 and :13, D-032, handoff:23 and :41 all say "≈ 8 weeks at full pace, ~6 only with the cuts". process.md:51 and D-009 keep the original 4–6 week *target*. roadmap.md:8 explains the gap, so this does not contradict. |
| R3-m4 type lists | RESOLVED | D-022, threat-model S1/S1b, base-options §0 and ERRATA:8 give the same signed list (chat, nick {5}, avatar {6}, reaction, delete, `leave`, `requestAdd`/`requestAdd2` {0}, `kickUser`/`banUser`) and the same 6 unsigned types. All of them mark the list as not exhaustive (D2 builds the full table). |
| R3-n1 revision number | RESOLVED | roadmap.md:3 and base-options.md:82 now say "revision 3". |
| R3-n2 status log | RESOLVED | status-log.md covers rounds 2, 3 and 4. |
| R3-n3 legend | RESOLVED | The DECISIONS.md:3-5 legend now defines "amended" and "target ✅ · method 🟡". The D-014 value "✅ target · 🟡 measurement" is a small variant, which is acceptable. |
| R3-n4 B1b pin | RESOLVED | roadmap.md:24 says "the pinned Core release tag (D-030)". |
| R3-n5 ERRATA commit | RESOLVED | ERRATA header and row 8 name HEAD `1ff5435`. |
| n-8 FORGE citation | RESOLVED | process.md:5 now says that only the video description was used and that the video itself was not reviewed. |
| R4-n1 (new NIT) | OPEN | The round counts disagree. base-options.md:3 says "after two adversarial review rounds", base §0 row says "Two review rounds", and ERRATA:8 says "three review rounds". Align them, or drop the count. |
| R4-n2 (new NIT) | OPEN | threat-model S3 (:33) still lists "pubkeys … members only" as a v1 control. D-029 says that gate adds no security while membership is open. The S3 "(Limit: see §5)" note does not mention this. Add one clause: for v1, the pubkey gate matters only for banned or kicked users and for closed mode. |

I checked all 10 listed docs for new contradictions and found none beyond R4-n1 and R4-n2.
