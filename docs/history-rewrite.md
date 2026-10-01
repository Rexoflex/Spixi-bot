# History rewrite — remove AI attribution from fork commits (D-046)

Scope: `rework/bot` on the fork (`Rexoflex/Spixi-bot`). `master` on the fork has no such lines. The upstream
(`ixian-platform`) never received these commits. Run this **after PR #2 is merged**.

What changes: commit messages lose `Claude-Session:`, `Co-Authored-By: Claude …` and "Generated with" lines. Code,
authors and dates stay. Every rewritten commit gets a new SHA; `docs/sha-map.md` maps old → new (Claude writes it
after the rewrite).

What does not change (GitHub limits): PR #1 and PR #2 keep their original commits under `refs/pull/*`, and old
commits stay reachable by SHA until GitHub garbage-collects them. Full removal needs a request to GitHub Support
(ask them to remove cached views/refs) or a re-created fork.

**Done 2026-10-01:** `rework/bot` `266ff99` → `59c1f74` (forced update), 18 commits rewritten, trees identical,
0 attribution lines left. Map: `docs/sha-map.md`.

The filter script: `strip-trailers.sh` (session-4 scratch, `_to_delete/s4/`; kept out of the repo).
