# Session 6 — review round 3 (fresh break-my-verdict reader over plan rev 3)

Verdict: **CLEAN** — 0 MAJOR, 3 MINOR, 3 NIT; all fixed in plan rev 4. Every round-2 fix verified against source
(queue data arrays are per message, k `NetworkRemoteEndpoint.cs:1116`; status only set via the API `/shutdown` path;
s2data handlers touch no store closed earlier, so the join cannot deadlock; Preferences `walletpass`; counts 18/15,
grading 41/36/39).

| # | Sev | Finding | Fix (rev 4) |
|---|---|---|---|
| m1 | MINOR | Queue stopped before the network stops; `TryAdd` after `CompleteAdding` throws into k `parseLoop` (logs client IP, drops endpoint, `:690-697`) | queue stopped after `NetworkServer`/`NetworkClientManager` (`Node.cs:377-380`); hook never throws (false + count); A2t case |
| m2 | MINOR | C10: legacy `false` at `Node.cs:295` is f6's `compacted`; k `start(0,null,false)` would disable pruning | `pruneBlocks = true` on both paths |
| m3 | MINOR | No CI job runs `Node.stop` (smoke and harness kill the bot) | Linux stop smoke in `port-build` (`kill -INT`, "Spixi Bot stopped.", 30 s); systemd `TimeoutStopSec` |
| n1 | NIT | H11 "stdin close" is not a bot stop path | removed |
| n2 | NIT | §6.2 `crashed` row asked for an unneeded change | "no change expected" |
| n3 | NIT | Preferences also hold `uid` (shared `device_id`) | noted in §10.5 |
