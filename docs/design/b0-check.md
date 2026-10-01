# B0 · Old bot — operator check (no code)

Session 2, 2026-10-01. **D-041 replaces the code hotfix** (D-029, D-035–D-037, D-040 are superseded). Reason
(Damir): the old bot is little used and is switched off 2 weeks after the new bot launches, so a code hotfix is
wasted effort. Revision 2 after review round 1 (`docs/reviews/session-2-round-1.md`).

## 1 · Why the check is still needed

The old bot holds a hot wallet, and its HTTP API can spend from it (`addtransaction`), sign with the bot key (`sign`),
export the encrypted wallet (`getwalletbackup`) and stop the bot (`shutdown`) — Core `f6fb55b`
`API/GenericAPIServer.cs:229,249,361,381`. Three ways to reach that API:

| Path | Today | After the check |
|---|---|---|
| Remote caller | Only if `apiBind` was set to a public address (default is localhost, `SpixiBot/Meta/Node.cs:238-240`) | Closed (step 1) |
| Any local process or browser page, with **no login set** (no `addApiUser` = everyone allowed, `GenericAPIServer.cs:529-538`; plain GET calls, no Origin check) | Open | Closed by a login (step 2) |
| **The admin web page itself** shows member nicks with `innerHTML` (`html/js/bot/settings.js:151`); any member can set a script as nick → it runs with the operator's login (research B T3) | Open whenever someone opens the page | Closed by rule: nobody opens the page (step 3) |

## 2 · The check (operator, ~10 minutes, once)

Before you start: copy `ixian.cfg` (bot working directory, `Config.cs:31`) and `groups.dat`, `settings.dat` (data
directory, default `Data`) to a backup.
API port: 8501 (testnet 8601) unless `apiPort` or `-a` says otherwise (`Meta/Config.cs:23-24,194,306`).

| # | Step | Pass when |
|---|---|---|
| 1 | In `ixian.cfg`: remove any `apiBind` that is not `http://localhost:<apiPort>/`; replace any `apiAllowIp` lines with exactly `apiAllowIp = 127.0.0.1` and `apiAllowIp = ::1` (with none set, every IP is allowed; `Config.cs:200-204`). These keys exist only in the config file. | Only localhost remains. |
| 2 | Add `addApiUser = <user>:<password>` with a password from `openssl rand -hex 24` — it must **not contain `:` or `=`** (a `:` silently disables the login, a `=` silently cuts the password; `Config.cs:173-179,206-210`). Run `chmod 600 ixian.cfg`. The bot logs every config line verbatim at start (`Config.cs:185`), so after the restart remove that line from `ixian.log` (or rotate the log) and `chmod 600` the logs. Add `disableWebStart = 1` (stops the bot opening a browser on Windows, `Node.cs:246-249`). Restart the bot. | `curl http://localhost:<apiPort>/status` without the login returns 401; with `-u user` (curl asks for the password, so it stays out of shell history) it answers. From another machine `curl http://<public-ip>:<apiPort>/status` and `curl -H "Host: localhost:<apiPort>" http://<public-ip>:<apiPort>/status` both fail. Note the server OS. |
| 3 | **Rule until shutdown: nobody opens the bot's admin web page.** All admin work uses `curl` with the login. | Agreed by everyone with server access. |
| 4 | Costs: `curl -u user http://localhost:<apiPort>/sb_getGroups`. For each group with `cost` > 0: `curl -u user -G http://localhost:<apiPort>/sb_updateGroup --data-urlencode "origGroup=<name>" --data-urlencode "group=<name>" -d cost=0 -d admin=<1 if admin was True, else 0> -d default=0` (`SpixiBot/API/APIServer.cs:398-424`; `default=0` leaves the default group unchanged, `admin` must be copied or it changes). | Every group shows cost 0 (keeps the payment path unreachable, research B T6). |
| 5 | Move all IXI above small change from the bot wallet to a cold wallet. | Balance ≈ fees only. This limits **funds** at risk; it does not stop misuse of the key itself. The new bot gets a new key (D-034), so the old key is retired at shutdown. |
| 6 | Report the result (date, OS, pass/fail per step) in reply to the BE asks. | Recorded under BE-02. |

**Rollback:** stop the bot (it rewrites `groups.dat`/`settings.dat` itself), restore the three backed-up files, start it. Step 5 (moving funds) is not rolled back.

## 3 · Cutover and shutdown (no code)

1. On cutover day an admin posts a normal chat message in the old channel: "We moved — join the new channel:
   <new address / QR / deep link when it exists>". The old bot keeps relaying normally; there is no read-only mode.
2. The old bot keeps running for **2 weeks** (D-038), so members who open the app late still see the message.
3. Then the operator stops it and keeps its data directory as a read-only backup (optional history import, D4).

## 4 · Accepted risk until shutdown (~10 weeks)

Not fixed on the old bot (D-041; threat model §5):

| Risk | Where |
|---|---|
| Anyone can **post as another member** (relayed chat has no sender check) | `StreamProcessor.cs:377-420` |
| Anyone can **force a member to leave** | `onLeave(message.sender)`, `:190-191,282-299` |
| Anyone who connects as an admin's address can **delete messages** (Core does not authenticate client hello, T2) | `:301-328` |
| Spoofed **nick, avatar, reaction** relays (clients reject them against the roster pubkey, so the effect is a blanked nick, not a fake one) | `:138-156,330-341`; research D §3 |
| Local code on the server can still use the API with the login (shutdown, sign, spend small change); the login sits in `ixian.cfg` (mode 600) | `GenericAPIServer.cs`; `Config.cs:185` |
| The admin page's stored XSS (T3) — controlled only by step 3's rule | `settings.js:151` |

Trigger to reopen: any impersonation report in the community channel → revisit D-035 (signature checks, ~2 days).
Moderating in that case uses `sb_banUser`/`sb_kickUser` via `curl`, never the web page.
