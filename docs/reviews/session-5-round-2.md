# Session 5 — review round 2 (over the round-1 fixes, Opus, against Core k / f6 / bot source)

Verdict: **CLEAN** (1 NIT, fixed).

Verified: probes after deletes keep in-order proof and do not leak into later windows (unique texts; resends of a delete find no message, `StreamProcessor.cs:303-307`); 24 marker tags in `harness.yml` pairwise non-substring and equal to `Markers.Tag` output; drain probe makes the post-inject `channel` unambiguous; crash proof (FailFast text or 0x80131623 can only come from the patch; Core k has no FailFast); `break-core.ps1` anchors match once in CRLF and LF; compile-read of the four test files and helpers clean.

| # | Sev | Finding | Fix |
|---|---|---|---|
| 1 | NIT | step 4 comment gave the wrong reason for not using the -1 ack (the ack carries the delete's own id; `delete_sent` only reports the target) | comment corrected |
