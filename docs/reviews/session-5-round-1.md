# Session 5 — review round 1 (pre-push, three parallel Opus reviewers, against Core k, Core f6, bot and U/R source)

Verdict: no MAJOR; 7 MINOR, 8 NIT (merged below) → fixed before push 1.
Reviewers: A = the four scenarios end to end · B = SimClient compile-read + glue behaviour · C = breaks + CI.

| # | Sev | Finding | Fix |
|---|---|---|---|
| 1 | MINOR (A) | `React_Delete` step 5: the channel -1 ack after B's delete could belong to a queued botGetMessages of the admin-info cascade (Core k `PendingMessageProcessor.cs:313-315` sends only the first queued item at once); "the bot handled the delete" was not proven | the deleter posts a probe and waits for its ack (in-order handling per client); same in step 4 |
| 2 | MINOR (A) | `Info_Variants`: the `channel` wait after the injected info could match the answer to the real post-accept info (Core k `:2706` sends getChannels on every info) | drain probe (`PostAndWaitAckAsync`) before the inject |
| 3 | MINOR (A, C) | `Unknown_Codes`/`Leave` store: one 20 s window decides crash vs alive; Windows Error Reporting can delay the exit after FailFast/StackOverflow | extra `CrashTimeout` (30 s) wait for `exited`/`crashed` |
| 4 | MINOR (A, C) | `DELETE-MISSING[..]` is a substring of `ADMIN-DELETE-MISSING[..]`; `Select-String -SimpleMatch` would accept the admin line for the `delete` self-test (L18) | renamed `ADMIN-DELETE-NOT-RELAYED`; no marker in `harness.yml` is a substring of another |
| 5 | MINOR (A) | CLIENT-CRASHED-ON-UNKNOWN accepted any death | requires stderr `harness self-test 'unknown'` or exit code 0x80131623 (COR_E_FAILFAST) |
| 6 | MINOR (B) | README said every event caused by an inject precedes `injected`; the bot's answers to the requests Core sends arrive later | wording: only what Core emits synchronously |
| 7 | MINOR (B) | two `crashed` events after an unhandled exception (SimClient's own + the W12 exit synthesis); the marker text used the last | prefer the one with `error` |
| 8 | MINOR (C) | five new files are untracked; ~100 files show CRLF-only changes in the VM's view | commit instructions name the files explicitly |
| 9 | NIT | store crash relies on the Tier-0 JIT of the self-recursive tail call (`StreamClientManager.cs:118-121`) | docstring |
| 10 | NIT | 245 lies in Core k's reserved range 0xF0-0xFF (`SpixiMessage.cs:78`) | docstring |
| 11 | NIT | info-legacy `trailing` must be 1..172 (0xAB is varint 171) | README |
| 12 | NIT | inject runs `receiveData` on the command thread; botInfo replacement is not locked (`:2679-2684`) | README |
| 13 | NIT | R leave citation `:97-100` → `:97-105` | fixed |
| 14 | NIT | reaction moves the cursor to the target id (`:1689`) | noted for research D |
| 15 | NIT | failure annotation keeps only the tail of long output | accepted (artifact has all) |

Corrections found while building (not reviewer findings): the brief's `storedAdmin == false` was wrong — every bot settings save stamps a new `generatedTime` (`Settings.cs:109`; `sb_newGroup` saves, `APIServer.cs:393`), so the admin info normally replaces the stored botInfo; the test asserts the rule `storedAdmin == (storedGeneratedTime == generatedTime)`. The `sign` statement occurs 4×, not 2×; `break-bot.ps1` scope mode handles it.
