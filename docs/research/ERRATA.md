# Research errata

Corrections to the session-1 research reports (A–E), found by the round-1 review
(`docs/reviews/session-1-round-1.md` … `-round-3.md`). The reports are kept as written; read them with these corrections.
Core line numbers refer to HEAD `1ff5435` unless marked `f6fb55b`.

| Report | Claim | Correction | Evidence |
|---|---|---|---|
| D §0, A §1a | "All client→bot traffic is `none` and signed." | **False as a generalisation.** Signed: chat, nick {5}, avatar {6}, reaction, delete, `leave`, `requestAdd`/`requestAdd2` {0} and the `kickUser`/`banUser` bot actions. **Not signed: `getInfo`, `botGetMessages`, `getChannels`, `getUsers`, `getGroups`, `getUser`**. From the review rounds, not an exhaustive audit: D2 starts with a complete per-type table. | Core HEAD `1ff5435` `CoreStreamProcessor.cs:2553-2584` vs `:314-318` (same logic at `f6fb55b`) |
| B §0 | Payment callback lines `SpixiBotTransactionInclusionCallbacks.cs:533-535`. | The file has 52 lines; the real lines are `:24-26`. | review m3 |
| C §0 / design rev 1 | "~1.5 kLOC server logic" vs "4 kLOC". | The bot has 4,056 C# lines in total; ~1.5 kLOC is the server-specific part (StreamProcessor, Messages, APIServer, PushNotifications). Both numbers are correct for what they measure; the design doc now says which. | `wc -l` |
| C §2 | "Core's receive path rejects bot traffic." | Only for a **known** non-bot friend (the check is inside `if (friend != null)`). The conclusion (QuIXI cannot host a channel today) still holds. | Core `CoreStreamProcessor.cs:764-781` |
| A §9 / B T6 | Payment crash. | Reachable only while a paid message is pending; a free channel (cost 0) cannot trigger it. Still a hypothesis until run. | `StreamProcessor.cs:453-459` |
| A / B | "Delete ids break the history cursor." | Deletes are fine: the cursor moves to the tombstone id, which the bot stores. The **reaction** case is real and client-side (cursor set to the target id). | Core `CoreStreamProcessor.cs:1665-1690` |

Rule going forward (process §Definition of ready): a research claim that a DECISIONS row relies on is re-read at
source by someone other than its author.
