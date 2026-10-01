# Threat model — Spixi Bot

Session 1, 2026-10-01. The full source-verified threat list (T1–T27, STRIDE, `file:line`) is in
`docs/research/B-security-moderation.md` §9. This document is the **control plan**: what the
rework enforces, where, and when. Update it in every batch that touches a threat.

## 1 · Assets

| Asset | Why it matters |
|---|---|
| Bot wallet (hot key on the server) | Bot identity; the Core API can move funds |
| Message authenticity | Members must trust who wrote what |
| Moderation state (roles, bans) | Community safety |
| Member privacy (addresses, pubkeys, nicks, activity) | Profiling, harassment |
| Availability | "Messages are not delivered" is the main complaint |
| Operator config/secrets | API tokens, push credentials |

## 2 · Trust boundaries

1. **Client ↔ bot (S2 TCP):** untrusted input. Transport identity is **not authenticated** in Core
   for client nodes (T2). The bot must treat the connection's wallet as a hint, never as proof.
2. **Bot ↔ admin API (HTTP localhost):** today no auth by default + CSRF-able (T4).
3. **Bot ↔ push gateway (HTTPS):** leaks membership/activity if tags are raw addresses (T19).
4. **Bot ↔ disk:** plaintext history and member data at rest (T16, accepted for public channels).

## 3 · Security rules (every batch)

| # | Rule | Threats |
|---|---|---|
| S1 | **Authorship = signature, per message class** (design item "delivery semantics" fixes the details before B3/B4 code). For every **signed** type: `Address(pubkey) == sender`, valid signature, `sender == connection wallet`. Replay: random-id types (chat, reaction, delete, admin actions) use a cache on `(sender, id, checksum)`; fixed-id types (`nick` {5}, `avatar` {6}, `requestAdd`/`requestAdd2` {0}) require a timestamp that increases per `(sender, type)`. The time window is set from measured client retry behaviour (clients retry for up to 5 days with the original timestamp, and send once more after expiry). The full per-type signed/unsigned table with `file:line` is D2's first input. **Every drop reason has a defined ack rule** (duplicate → ack and ignore; policy rejection → documented). | T1, T9, T10, T12, T24 |
| S1b | **Unsigned types are read-only.** `getInfo`, `botGetMessages`, `getChannels`, `getUsers`, `getGroups` and `getUser` are unsigned: they may never create or change state (today `getInfo` creates users). Replies go to the **requesting connection object**, never to an address lookup. | T13, T14, review M2 |
| S2 | **Privileged actions need a signed command** from an address with enough permission level; never trust the connection alone. | T2, T8, T21 |
| S3 | **Membership gates everything:** fan-out, history, member list, pubkeys, nicks — members only; banned/kicked get nothing. This protects against banned/kicked addresses and (later) closed mode; it adds nothing while anyone can join with one unsigned `getInfo`, which is why it is not a control for the old bot. (Limit: see §5.) | T7, T8, T14, T17 |
| S4 | **Hard limits on every input:** per-type size caps (chat, nick, avatar, reaction), per-address and per-connection rate limits, history page size, storage budget. | T5, T13, T14, T15 |
| S5 | **The bot never holds member funds.** Paid messages are out of v1. | T6, T11 |
| S6 | **No address, IP, content, pubkey or secret in logs or metrics.** Aggregates only (D-012). Core also logs addresses and IPs at info level → a redaction sink in the bot, plus a BE ask. | T18, T19 |
| S7 | **Safe defaults for self-hosters:** API token generated on first run, API bound to loopback, POST-only mutations, Origin check, wallet-spending API methods disabled, push off, no default admin group. | T4, T20, T21 |
| S8 | **Admin UI renders untrusted data as text only** (no `innerHTML`), or the UI is off. | T3 |
| S9 | **`leaveConfirmed` is a graded decision** (D-025, in D4): sending it crashes the store app once, after the leave has completed; not sending it leaves the leaver holding one persistent invisible connection. Rejecting leavers at hello would turn that into a new connection every 2.5 s per leaver. The bot cannot tell store from redesign clients. | review M4, round 2 m-3 |
| S10 | **Pin the Core commit for merges; build nightly against Core HEAD** (non-blocking) to see drift early. | T26 |
| S11 | **The wallet secret never sits in a file or command line in plain text.** Non-interactive start uses an injected secret (env/secret store) with documented rotation; the bot wallet holds only what it needs. | review m9 |

## 4 · Plan per threat (v1 unless marked)

| Threat | Control | When | Owner |
|---|---|---|---|
| T1, T3, T4 live in production today (old bot) | **B0 operator check, no code** (D-041, `docs/design/b0-check.md`): API on localhost with a login and `disableWebStart`; **nobody opens the admin web page** (T3); cost 0; wallet holds only small change. T1, forced leave and T3-by-rule stay as accepted risks on the old bot until its shutdown (§5). | now (5 min) | operator |
| T1 sender spoofing | S1 in the protocol adapter | v1 | us |
| T1 client-side verification | Clients verify relayed chat signatures | later | BE |
| T2 unauthenticated client transport | S2 in v1; Core challenge-response | v1 / later | us / BE |
| T3 admin UI XSS | S8 | v1 | us |
| T4 API CSRF / no auth | S7 | v1 | us |
| T5, T13–T15 DoS | S4 + append storage + sends outside locks | v1 | us |
| T6 payment crash | Payments disabled (S5); guard maintenance loop | v1 | us |
| T7–T10 moderation | S3 + permission levels + persistent ban list | v1 | us |
| T11 payment fraud | Redesign with BE (txid bound to message id) | later | us + BE |
| T12 replay | S1 freshness + id cache | v1 | us |
| T16 plaintext | Accepted for public channels; closed mode needs transport/E2E | closed mode | BE |
| T17 enumeration | S3; blind mode later | v1 / later | us |
| T18–T19 logs/push privacy | S6; opaque push tokens | v1 | us |
| T20–T23 legacy authz/NRE | Remove `serverPassword` or hash it; typed roles; null checks | v1 | us |
| T25 forged bot actions to clients | Bot signs its messages; clients verify | later | BE |
| T27 `/resources` traversal (hypothesis) | Test; canonicalise path | v1 | us |

## 5 · Accepted risks (documented for self-hosters)

- **Old bot until its shutdown (D-041, ~10 weeks):** anyone can post as another member, force a member to leave, or delete messages by connecting as an admin; local code on the server can use the API with the login; the admin page's stored XSS (T3) is held only by the rule that nobody opens it. Full list: `docs/design/b0-check.md` §4. Accepted because the old bot is little used and is switched off; any impersonation report reopens D-035.
- The bot operator can read all channel messages (no E2E for bot channels today).
- Until Core authenticates client connections (T2, BE), someone who knows a member's public key can **connect** as
  that member: they **receive what that member receives** (so a banned person can still read by borrowing another
  member's identity), and they can **hijack that member's catch-up** replies if they connect first. They cannot
  **post** as that member (S1). Closed mode is therefore not offered until T2 or a bot-level challenge exists.
