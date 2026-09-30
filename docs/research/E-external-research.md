# E — External research: self-hostable Spixi group-chat server ("bot")

Date: 2026-10-01. Scope: design references and toolchain for a C#/.NET 10 group-chat server on the Ixian P2P transport. Uses the official Ixian channel, open communities, closed groups, and AI-agent channels.
Version facts marked **(verified 2026-10)** were checked against NuGet or the vendor's docs on that date. Everything else comes from stable specs; the links point there.

---

## 0. TL;DR recommendations

| Area | Recommendation |
|---|---|
| Roles | Integer **power levels** per address (Matrix-style). Each action has a required level. Use three named presets (Member 0 / Moderator 50 / Admin 100) plus Owner. No bitsets in v1. |
| Moderation v1 | Ban, kick, timeout/mute, delete message, pins, slow mode, read-only (announcement) mode, join approval, invite links with expiry and use limits, new-member restrictions, and an append-only moderation log. |
| History | A per-room monotonic `seq` (uint64). Opaque cursors. `GET_HISTORY(room, before_seq?, limit≤100)` returns messages plus `prev_cursor`/`has_more`. A separate `SYNC(since_seq)` delta call serves reconnects. |
| Delivery | At-least-once plus a client-generated idempotency ID (`client_msg_id`, deduped per sender). Delivery rate comes from aggregate counters and histograms, never per-user rows. |
| Storage | **RocksDB** (already in the Ixian stack) with column families and big-endian `(room, seq)` keys. Use `WriteBatch` for atomic message+index writes. Use the WAL, and `sync=true` for acked writes. SQLite in WAL mode is the fallback if query flexibility matters more. |
| Tests | xUnit v3 on Microsoft.Testing.Platform (MTP), CsCheck, SharpFuzz, Stryker.NET, coverlet.MTP, BenchmarkDotNet, Verify, and Testcontainers. Use a custom load harness (NBomber needs a paid licence for org use). |
| CI | GitHub Actions: build and test, coverage, Stryker gate, CodeQL, dependency review, Dependabot, SBOM, artifact attestation, deterministic builds with NuGet lock files. |
| Packaging | A chiseled `runtime:10.0-noble-chiseled` image, compose file, hardened systemd unit, JSON file + env config, a built-in `--healthcheck` subcommand, RocksDB checkpoint backups, and an OpenTelemetry to Prometheus exporter. |
| Push | A tiny, documented HTTP push-gateway contract modelled on the Matrix push gateway / UnifiedPush. It sends **IDs only, no content**. |
| Media | Encrypt on the client (AES-256 + HMAC or AES-GCM). The server stores opaque blobs with a TTL. Key, digest and blob ID travel inside the E2E message. |
| .NET 10 | .NET 8 support **ends 2026-11-10**. .NET 10 LTS runs to Nov 2028. The main runtime trap for a console server is the **SIGTERM default-handler removal**. |

---

## 1. Community chat feature expectations

### What the incumbents offer

| Feature | Telegram groups/channels | Discord | Matrix rooms | Signal / WhatsApp |
|---|---|---|---|---|
| Roles/permissions | Owner + admins with granular rights ([chatAdminRights](https://core.telegram.org/constructor/chatAdminRights)); default member rights ([chatBannedRights](https://core.telegram.org/constructor/chatBannedRights)) | Roles with a permission bitset, role hierarchy, per-channel overwrites ([docs](https://discord.com/developers/docs/topics/permissions)) | Integer power levels per user + per-event thresholds ([spec](https://spec.matrix.org/latest/client-server-api/#mroompower_levels)) | Admin / member only ([Signal groups](https://support.signal.org/hc/en-us/articles/360050427692-Manage-a-group)) |
| Ban / kick | Yes (ban with `until_date`) | Yes | `m.room.member` ban/kick, `ban`/`kick` levels | Remove member |
| Mute / timeout | Per-user restrictions with expiry | **Timeout** up to 28 days ([docs](https://support.discord.com/hc/en-us/articles/4413305239191-Time-Out-FAQ)) | Emulated by lowering the user's level below `events_default` | No |
| Slow mode | Yes, 10 s – 1 h ([slow mode](https://telegram.org/blog/permissions-groups-undo#slow-mode)) | Per-channel rate limit | No native feature | No |
| Read-only / announcements | Channels; "only admins can post" | Announcement channels | `events_default: 50` | "Only admins can send" (Signal); Community announcement group (WhatsApp, [help](https://faq.whatsapp.com/495856382464992)) |
| Pins | Yes | Yes | `m.room.pinned_events` | Signal: pinned messages (recent) |
| Welcome/rules | Bots / pinned message | Rules screening / onboarding | Room topic + bots | Group description |
| Invite links | Multiple links, expiry, usage limit, join-request flag ([exportChatInvite](https://core.telegram.org/method/messages.exportChatInvite)) | Links with expiry + max uses | `join_rules`: public / invite / knock / restricted | Link + optional admin approval |
| Join approval | Join requests ([approval](https://core.telegram.org/api/invites#join-requests)) | Membership screening | `knock` join rule | Admin approval on link (Signal) |
| Anti-spam | Aggressive anti-spam for large groups ([blog](https://telegram.org/blog/translations-anti-spam#aggressive-anti-spam)), restrictions on new members | AutoMod, verification levels (account age, time on server) ([AutoMod](https://support.discord.com/hc/en-us/articles/4421269296535-AutoMod-FAQ)) | Mjolnir/Draupnir bots, server ACLs | Minimal |
| Audit / mod log | Admin log ("Recent actions", 48 h) ([channels.getAdminLog](https://core.telegram.org/method/channels.getAdminLog)) | Audit log ([docs](https://discord.com/developers/docs/resources/audit-log)) | The room state history is the log | No |
| Reports | Report message | Report to Trust & Safety + in-server | `/rooms/{id}/report/{eventId}` ([spec](https://spec.matrix.org/latest/client-server-api/#reporting-content)) | Report spam |

### Ranking for a v1 community channel

**Must-have (v1)**
1. Roles: owner / admin / moderator / member, with a hierarchy (you cannot act on an equal or higher role).
2. Ban (with an optional expiry), kick, and delete any message.
3. Timeout/mute with an expiry. This is the cheapest de-escalation tool, and Discord and Telegram both lean on it.
4. **Read-only / announcement mode.** The official Ixian channel needs this.
5. Pins, plus a welcome/rules message shown on join.
6. Invite links with expiry, a max-uses limit, revocation, and an optional "requires approval" flag.
7. Join approval queue (closed groups).
8. Slow mode (per room, N seconds).
9. **New-member restrictions:** no links or media for the first N minutes or messages. Per-address rate limits. This covers most spam at low cost.
10. An append-only **moderation log**: who, what, target, reason, when. It is visible to mods.
11. User reports to moderators (message ID + reason). This goes into the mod queue, not to a central service.

**Nice-to-have (v2+)**: keyword/regex AutoMod, per-topic sub-channels (Telegram topics / Discord channels), scheduled messages, polls, verification levels, shared ban lists across servers (Matrix policy lists, [MSC2313](https://github.com/matrix-org/matrix-spec-proposals/pull/2313)), and custom roles beyond the presets.

**AI-agent channels:** treat an agent as an ordinary address with a role. Add a per-role **rate limit** and a per-room "bots may post" flag. Do not add a separate privileged API.

---

## 2. Role model references → recommendation

| Model | How it works | Pros | Cons |
|---|---|---|---|
| **Matrix power levels** ([spec](https://spec.matrix.org/latest/client-server-api/#mroompower_levels)) | `users: {id: int}`, `users_default`, `events: {type: int}`, `events_default`, `state_default`, plus `ban`/`kick`/`redact`/`invite` thresholds. A user may act if `level ≥ required`. A user can only change levels below their own. | Simple, totally ordered, and easy to audit. The hierarchy comes for free. | Coarse. Two roles with the same level but different rights are not possible. |
| **Discord bitsets** ([docs](https://discord.com/developers/docs/topics/permissions)) | Roles carry a 64-bit permission mask. The member mask is the OR of the member's roles. Channel overwrites allow/deny bits. Position sets the hierarchy. | Very expressive. | The complexity (overwrite resolution order) is a frequent source of bugs and admin confusion. |

**Recommendation: Matrix-style integer levels, keyed by Ixian address.**
```
room.levels = {
  users: { "<addr>": 100, ... },   users_default: 0,
  actions: { send: 0, send_media: 0, send_links: 0, pin: 50, delete_any: 50,
             kick: 50, ban: 50, timeout: 50, approve_join: 50, invite_link: 50,
             set_levels: 100, edit_room: 100 },
  new_member: { restrict_minutes: 10, restrict_actions: ["send_media","send_links"] }
}
```
- Hierarchy rule: an actor may target an address only if `actor.level > target.level`. The actor may set a level only up to `actor.level − 1`. The owner is the exception.
- Read-only room is `actions.send = 50`.
- A timeout is a **separate** record: `(addr, until)`. Do not lower the user's level to mute them. This is simpler to expire and to audit.
- Store the level changes in the moderation log, so the log shows the full history.
- If needed later, you can add the bitset as "capabilities" without breaking the ordering.

---

## 3. History sync patterns → pagination protocol

| System | Initial sync | Backfill |
|---|---|---|
| Matrix `/sync` ([spec](https://spec.matrix.org/latest/client-server-api/#get_matrixclientv3sync)) | Returns per room the latest `timeline.limit` events, `limited: true` when there is a gap, and `prev_batch` token. The `since` token gives incremental deltas. Filters support `lazy_load_members`, which sends only the senders of the returned events ([spec](https://spec.matrix.org/latest/client-server-api/#lazy-loading-room-members)). | `/rooms/{id}/messages?from=<prev_batch>&dir=b&limit=N` → `chunk`, `end` token ([spec](https://spec.matrix.org/latest/client-server-api/#get_matrixclientv3roomsroomidmessages)). Simplified Sliding Sync ([MSC4186](https://github.com/matrix-org/matrix-spec-proposals/pull/4186)) replaces heavy initial syncs with windowed room lists. |
| Telegram ([offsets](https://core.telegram.org/api/offsets), [updates](https://core.telegram.org/api/updates)) | Maintains a `pts` state and calls `updates.getDifference` for gaps. | `messages.getHistory(offset_id, add_offset, limit, max_id, min_id)`. The message ID is the cursor. |
| Discord ([docs](https://discord.com/developers/docs/resources/message#get-channel-messages)) | Gateway events | `GET /channels/{id}/messages?before=<snowflake>&limit≤100` |

Common lessons:
1. Use **ID-based cursors, not offsets.** New messages do not shift the page.
2. Keep **two separate paths**: "catch up since X" (forward, small) and "load older" (backward, on scroll).
3. Cap `limit` on the server (Discord 100). Tell the client when there is a **gap** (Matrix `limited`), so it does not stitch a false continuous history.
4. Send only the member profiles for the senders in the page (lazy-loading members).

**Recommended protocol (Ixian stream messages):**
```
HISTORY_REQ  { room, before_seq?: u64, limit: u16 (server clamps to ≤100, default 50) }
HISTORY_RESP { room, msgs: [ {seq, id, sender, ts, kind, body...} ] (desc or asc — pick one, document it),
               oldest_seq, has_more: bool,
               members: { addr -> {nick, avatar_hash, level} }   // only senders in this page
               }
SYNC_REQ     { room, since_seq }          // on reconnect
SYNC_RESP    { msgs[...] up to cap, latest_seq, gap: bool }   // gap=true → client drops local tail and calls HISTORY_REQ
```
- `seq` is a per-room, dense, monotonic uint64 that the server assigns in the same write batch as the message. The global message `id` stays separate (it is the client idempotency key, §4).
- Deletions and edits appear as tombstone/edit events with their **own** `seq`, so `SYNC` carries them. Keep the original row as a tombstone in history, so paging stays stable.
- The first open is `HISTORY_REQ{limit:50}` with no `before_seq`. Scrolling sends `before_seq = oldest_seq`. Reconnect sends `SYNC_REQ{since_seq = latest_local}`.
- Rate-limit history requests per address. Deep scroll-back is the most expensive read.

---

## 4. Delivery guarantees & metrics without PII

**Patterns**
- **At-least-once + idempotency.** The client generates `client_msg_id` (a UUIDv7 or 128-bit random value) and retries until it gets an ack. The server dedupes on `(sender, client_msg_id)` inside a bounded window (for example 24 h, stored in a TTL'd column family) and returns the **same** `seq` on a duplicate. Precedents: Matrix `PUT /send/{type}/{txnId}` ([spec](https://spec.matrix.org/latest/client-server-api/#transaction-identifiers)) and Telegram `random_id` ([messages.sendMessage](https://core.telegram.org/method/messages.sendMessage)). The broader theory: [Kleppmann, DDIA ch. 11](https://dataintensive.net/) and the [Stripe idempotency keys](https://stripe.com/blog/idempotency) write-up.
- **Acks at two layers:**
  1. `ACCEPTED{client_msg_id, seq}`: the server has stored it durably. Send this only after the RocksDB write with `sync=true`.
  2. Fan-out delivery ack per recipient session, `DELIVERED{seq}`, **cumulative** ("I have everything ≤ seq"), in the manner of TCP / Matrix read receipts. It is cheap and needs no per-message rows.
- Offline recipients do not need a server-side per-recipient queue when history plus `SYNC(since_seq)` exists. The client's own cursor is the queue. This removes most per-user state.

**Measuring delivery without per-user PII**
| Metric (OpenTelemetry / Prometheus) | Type | Notes |
|---|---|---|
| `chat_messages_accepted_total{room_class}` | counter | `room_class` = official/open/closed/agent, **never** a room ID or address as a label |
| `chat_messages_duplicate_total` | counter | Shows how many retries happen |
| `chat_fanout_attempts_total`, `chat_fanout_acked_total` | counters | Delivery rate = acked / attempts over a window |
| `chat_delivery_latency_seconds` | histogram | accept → first cumulative ack covering it, measured in memory per session and emitted as an observation only |
| `chat_sync_gap_total` | counter | Clients that fell off the retained window |
| `chat_active_sessions` | gauge | Current connections |
- Keep labels to low-cardinality enums. Prometheus explicitly warns against unbounded label values such as user IDs ([naming/labels](https://prometheus.io/docs/practices/naming/#labels), [instrumentation](https://prometheus.io/docs/practices/instrumentation/#do-not-overuse-labels)).
- To count **unique** active members without storing addresses, use a HyperLogLog sketch over a keyed hash (HMAC with a daily-rotated in-memory key). Emit only the estimate. Background: [Flajolet et al.](https://algo.inria.fr/flajolet/Publications/FlFuGaMe07.pdf)
- Do not log message bodies or full addresses. Log a truncated or hashed address at most.

---

## 5. Storage for an append-heavy log in .NET

| Option | Crash safety | Write path | Reads | Ops burden | Fit |
|---|---|---|---|---|---|
| **RocksDB** (LSM) | WAL; `WriteOptions.Sync=true` for durable acks; atomic `WriteBatch` across column families ([WAL](https://github.com/facebook/rocksdb/wiki/Write-Ahead-Log), [WriteBatch](https://github.com/facebook/rocksdb/wiki/Basic-Operations#atomic-updates)) | Excellent for append; sequential keys → cheap compaction | Range scans by key prefix; no ad-hoc queries | Tuning knobs; native lib per RID (see note) | **Best** — already in Ixian stack |
| **SQLite (WAL mode)** | ACID; WAL + `synchronous=NORMAL` safe against app crash, `FULL` against power loss ([WAL](https://www.sqlite.org/wal.html), [pragma synchronous](https://www.sqlite.org/pragma.html#pragma_synchronous)) | Good; single writer | SQL, easy search/moderation queries | Near-zero; one file; `VACUUM INTO` / online backup | Strong alternative |
| Custom append-only segment log + index | You own fsync, torn-write detection, recovery | Fastest | Only what you build | High; bugs = data loss | Not worth it |
| Microsoft FASTER Log ([docs](https://microsoft.github.io/FASTER/docs/fasterlog-basics/)) | Commit points | Very fast | Scans only | Niche; less maintained than Garnet | No |

**Recommendation: RocksDB**, because the stack and its native binaries already use it (the team already had the Mac Catalyst packaging trouble). Design:
- Column families: `msg` (key = `room_id(16B) | seq(8B BE)` → serialized message), `msgid` (`sender|client_msg_id` → seq, for dedupe, with a TTL via compaction filter or periodic sweep), `room_meta`, `members`, `modlog` (`room|ts|seq`), `media_meta`.
- Big-endian `seq` gives correct lexicographic order. "Older than" is then a reverse iterator from `room|seq-1`. Use a prefix extractor on `room_id` with prefix bloom filters ([prefix seek](https://github.com/facebook/rocksdb/wiki/Prefix-Seek)).
- Assign `seq` from an in-memory per-room counter. Recover the counter at startup by seeking the last key per room. Persist it in the same `WriteBatch`.
- Retention: delete-range per room `[room|0, room|cutoff)` ([DeleteRange](https://github.com/facebook/rocksdb/wiki/DeleteRange)). Compaction reclaims the space. Consider FIFO/TTL only for the dedupe CF.
- Backups: **Checkpoint** (hard-link snapshot, consistent) or BackupEngine ([checkpoints](https://github.com/facebook/rocksdb/wiki/Checkpoints)).
- Choose SQLite instead if self-hosters must run ad-hoc queries (moderation search, exports) and throughput per server stays modest. SQLite's single-writer model with WAL handles thousands of inserts per second on commodity disks.

---

## 6. Testing toolchain for .NET 10 (versions verified 2026-10)

| Tool | Version / status | Use | Rationale |
|---|---|---|---|
| **xUnit v3** | 3.2.x; native **Microsoft.Testing.Platform** (MTP) ([xUnit MTP](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)) | Unit/integration runner | Parallel by default, one package, native MTP; recommended for new projects in a 2026 comparison ([startdebugging](https://startdebugging.net/2026/08/xunit-v3-vs-nunit-vs-mstest-in-2026/)). NUnit 4.6 / MSTest 4.3 also fine; NUnit's MTP bridge needs manual pinning. |
| **CsCheck** | 4.9.1 (2026-09), net8.0+, no deps, Apache-2.0 ([NuGet](https://www.nuget.org/packages/CsCheck)) | Property + **model-based** + **linearizability** tests | Best fit for a message store and a state machine (seq assignment, dedupe, permission rules). Its concurrency testing checks parallel runs against sequential linearizations ([repo](https://github.com/AnthonyLloyd/CsCheck)). |
| FsCheck | 3.4.0 (2026-08) + `FsCheck.Xunit.v3` ([NuGet](https://www.nuget.org/packages/FsCheck)) | Alternative PBT | Mature, but has an F#-flavoured API and an FSharp.Core dependency. Pick one; CsCheck is preferred. |
| **SharpFuzz** | 2.3.0 (2026-06), .NET 5–10, MIT ([NuGet](https://www.nuget.org/packages/SharpFuzz)) + [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) | Fuzz the **wire decoders** (stream message parser, history/sync requests, media headers) | AFL/libFuzzer coverage-guided. Used by the .NET runtime itself. Run nightly, not per-PR. |
| **Stryker.NET** | 4.16.0 (2026-07); MTP runner improvements in 4.15 ([releases](https://github.com/stryker-mutator/stryker-net/releases)). .NET 10 not called out explicitly → **verify on a spike first** | Mutation testing | Set `thresholds: {high: 80, low: 60, break: 60}` in `stryker-config.json`. CI fails below `break` ([config docs](https://stryker-mutator.io/docs/stryker-net/configuration/#thresholds)). On PRs use `--since:main` to mutate only the changed files. |
| **coverlet.MTP** | 8.0.1 ([NuGet](https://packages.nuget.org/packages/coverlet.MTP)) or `Microsoft.Testing.Extensions.CodeCoverage` ([docs](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-code-coverage)) | Coverage | Use the MTP-native package with xUnit v3 ([xUnit guide](https://xunit.net/docs/getting-started/v3/code-coverage-with-mtp)). The old `coverlet.collector` is VSTest-only. |
| **BenchmarkDotNet** | ([docs](https://benchmarkdotnet.org/)) | Micro-benchmarks (serializer, key encoding, fan-out) | The de facto standard. Keep results as CI artifacts; do not gate on them (too noisy on shared runners). |
| **Verify** | ([repo](https://github.com/VerifyTests/Verify)) | Snapshot tests for wire-format payloads and config output | Catches accidental protocol drift. Snapshot the **binary→JSON** rendering of messages. |
| **Testcontainers for .NET** | 4.13–4.14 ([NuGet](https://www.nuget.org/profiles/Testcontainers)) | Multi-node integration (N server containers + a test-client container) | Real network, clean state per test class. Also useful to run the chiseled image under test. |
| Load / soak | **NBomber 6.6 is free for personal use only; organisations need a paid licence** ([licence](https://nbomber.com/docs/getting-started/license)). It supports any protocol. k6 has no native way to drive the custom Ixian TCP protocol without a Go extension. | Load/soak | **Recommend a small in-repo C# harness** (a console app that spins N simulated Ixian clients, uses `System.Threading.Channels`, and reports HdrHistogram-style latency through the same OTel metrics). Use NBomber only if the licence is acceptable. |

---

## 7. CI/CD on GitHub Actions — minimal but strict

```
on: [pull_request, push: main, tags: v*]
jobs:
  build-test:  (matrix: ubuntu-latest, windows-latest; optional macos for client libs)
    - actions/checkout (fetch-depth 0 for Stryker --since)
    - actions/setup-dotnet with global.json + cache: true (keyed on packages.lock.json)
    - dotnet restore --locked-mode
    - dotnet build -c Release -warnaserror
    - dotnet test (MTP) --report-trx --coverage  → upload TRX + cobertura
    - test report: dorny/test-reporter or GitHub job summary
  mutation (PR: --since:main, nightly: full) → stryker --break-at 60, publish HTML report
  codeql: github/codeql-action (language csharp, build-mode none or autobuild)
  dependency-review: actions/dependency-review-action (fail on high severity / disallowed licences)
  fuzz (nightly, time-boxed 30 min per target), soak (nightly/weekly)
  release (tag):
    - dotnet publish (deterministic), docker buildx (multi-arch amd64/arm64)
    - SBOM: anchore/sbom-action or microsoft/sbom-tool
    - actions/attest-build-provenance + actions/attest-sbom (Sigstore-backed)
    - push image by digest, create GitHub release with checksums
```
Sources and rationale:
- `setup-dotnet` NuGet caching requires lock files ([setup-dotnet](https://github.com/actions/setup-dotnet#caching-nuget-packages)). Use `RestorePackagesWithLockFile` + `--locked-mode` ([NuGet lock files](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies)).
- .NET 10 `dotnet restore` now **audits transitive packages** by default ([.NET 10 breaking changes](https://learn.microsoft.com/dotnet/core/compatibility/10)). Treat NU1901–NU1904 as errors.
- CodeQL for C# ([docs](https://docs.github.com/code-security/code-scanning)); Dependency review ([action](https://github.com/actions/dependency-review-action)); Dependabot for `nuget`, `docker`, `github-actions` ecosystems ([config](https://docs.github.com/code-security/dependabot/dependabot-version-updates/configuration-options-for-the-dependabot.yml-file)). Pin actions by SHA.
- Artifact attestations / SLSA provenance ([GitHub docs](https://docs.github.com/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds)); SBOM attestation walkthrough for .NET ([Andrew Lock](https://andrewlock.net/creating-sbom-attestations-in-github-actions)). Self-hosters verify with `gh attestation verify`.
- Reproducible builds: `<Deterministic>true</Deterministic>`, `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` in CI, SourceLink, `global.json` SDK pin ([deterministic builds](https://github.com/dotnet/reproducible-builds), [SourceLink](https://learn.microsoft.com/dotnet/standard/library-guidance/sourcelink)).
- Gates to require on `main`: build, tests, coverage floor (e.g. 70 % lines on the core lib, not on the whole solution), Stryker `break` threshold, CodeQL with no new high findings, and dependency review.

---

## 8. Self-hosting packaging

| Item | Recommendation | Source |
|---|---|---|
| Image | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` (non-root, **no shell**). Use `-extra` if you need ICU/tzdata. Use `runtime-deps:10.0-noble-chiseled` for self-contained/AOT builds. Note that .NET 10 default images are Ubuntu-based. | [distroless doc](https://github.com/dotnet/dotnet-docker/blob/main/documentation/distroless.md), [.NET 10 changes](https://learn.microsoft.com/dotnet/core/compatibility/10) |
| Build | Multi-stage (`sdk:10.0` → chiseled). Exec-form `ENTRYPOINT`. Or `dotnet publish /t:PublishContainer` with no Dockerfile. | [SDK containers](https://learn.microsoft.com/dotnet/core/docker/publish-as-container) |
| Health check | Chiseled images have no curl/sh. Ship a **`spixi-bot healthcheck`** subcommand that checks a local status socket or file, and use `HEALTHCHECK CMD ["dotnet","bot.dll","healthcheck"]`. Report: DB open, P2P connected to ≥1 node, last write age. | (design note) |
| Compose | One service, a named volume for `/data`, a read-only root FS, `cap_drop: [ALL]`, `restart: unless-stopped`, and the config file bind-mounted read-only | [Compose spec](https://docs.docker.com/reference/compose-file/services/) |
| systemd | `Type=notify` + `Microsoft.Extensions.Hosting.Systemd` (`UseSystemd()`), `DynamicUser=yes` or a dedicated user, `StateDirectory=spixi-bot`, `ProtectSystem=strict`, `ProtectHome=yes`, `NoNewPrivileges=yes`, `PrivateTmp=yes`, `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX`, `MemoryDenyWriteExecute` **no** (the JIT needs W^X exceptions) | [systemd.exec](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html), [.NET systemd hosting](https://learn.microsoft.com/dotnet/core/extensions/workers#deploy-as-a-linux-systemd-service) |
| Config | `appsettings.json` → `/etc/spixi-bot/config.json` → env vars (`SPIXIBOT__Room__SlowModeSeconds`). Secrets (wallet key) come from a file path (`*_FILE` convention), never from env. | [.NET configuration](https://learn.microsoft.com/dotnet/core/extensions/configuration-providers#environment-variable-configuration-provider) |
| Backups | A `spixi-bot backup --out /backup/…` subcommand that runs a RocksDB **Checkpoint** online, plus a documented restore. Include the config file but **exclude keys unless encrypted**. | [RocksDB checkpoints](https://github.com/facebook/rocksdb/wiki/Checkpoints) |
| Upgrades | Keep a schema version key in the DB. The app runs idempotent forward migrations at start, after it takes an automatic checkpoint. It refuses to start on a **newer** schema. Document "N → N+1 only". | (pattern: EF Core migrations bundles, [docs](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#bundles)) |
| Observability | OpenTelemetry .NET metrics + traces. Prometheus exporter on `127.0.0.1:9464` (off by default) or OTLP. Built-in `System.Runtime` meters. Structured logs to stdout/journald. | [OTel .NET](https://opentelemetry.io/docs/languages/dotnet/), [Prometheus exporter](https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/src/OpenTelemetry.Exporter.Prometheus.HttpListener), [.NET built-in metrics](https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics-runtime) |
| Signals | **Register SIGTERM yourself** (see §11) or use the Generic Host (`UseConsoleLifetime`). Otherwise `docker stop` / `systemctl stop` kills the process without flushing RocksDB. | [.NET 10 SIGTERM change](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/10.0/sigterm-signal-handler) |

**Recommendation:** use the Generic Host (`Host.CreateApplicationBuilder`) even for a console server. It solves signals, config layering, systemd notify, logging and DI in one step.

---

## 9. Push notifications for self-hosted servers

**How the references work**
- **Matrix:** the homeserver POSTs to the pusher's gateway URL `/_matrix/push/v1/notify`. The body carries `notification{event_id, room_id, counts{unread}, prio, devices[{app_id, pushkey, data}]}`, with an optional `format: "event_id_only"` that strips content. The gateway returns `{rejected: [pushkeys]}`, and the homeserver deletes those pushers ([push gateway API](https://spec.matrix.org/latest/push-gateway-api/)). **Sygnal** is the reference gateway. It holds the APNs/FCM credentials for one app ([Sygnal](https://github.com/matrix-org/sygnal)).
- **UnifiedPush / ntfy:** the app registers with a "distributor", which returns an **endpoint URL**. The app server POSTs a 1–4096 B body to that URL. `201` means OK; `404`/`410` means drop the endpoint; `429` means throttle ([UnifiedPush server spec](https://unifiedpush.org/developers/spec/server/), [definitions](https://unifiedpush.org/developers/spec/definitions/)). ntfy can be self-hosted and also acts as a Matrix-to-UnifiedPush gateway ([ntfy](https://docs.ntfy.sh/)). Payload encryption follows Web Push [RFC 8291](https://www.rfc-editor.org/rfc/rfc8291).

**The iOS constraint:** only the holder of the app's APNs key can push to the Spixi iOS app. Self-hosters therefore **cannot** run a fully independent gateway for the official app. They can only proxy through the Ixian central gateway, or use UnifiedPush on Android. This is the same reason Matrix servers point at the app vendor's Sygnal.

**Recommended contract ("Spixi Push Gateway v1")**
```
POST {gateway}/v1/notify          (HTTPS, gateway may require a bearer token issued per server)
{
  "server": "<bot address>",               // sender identity, for rate-limit/abuse control
  "notifications": [
    { "pushkey": "<opaque token from client>", "app_id": "io.ixian.spixi",
      "room": "<opaque room hash>", "kind": "message|mention|call",
      "counts": { "unread": 3 }, "prio": "high|low",
      "collapse_key": "<room hash>" }        // NO sender name, NO body
  ]
}
→ 200 { "rejected": ["<pushkey>", ...] }      // server must delete rejected pushkeys
→ 429 Retry-After                             // server backs off per gateway
```
- Content-free by design: the client wakes up and fetches over P2P. This matches Matrix `event_id_only` and the #919 iOS push-gate direction in the Spixi app (sender-only payload).
- The client registers its `pushkey` + `gateway URL` with the bot over the P2P channel. The bot stores it per session with a TTL and deletes it on `rejected`.
- Self-hosters point clients at `https://push.ixian.io` (central, APNs/FCM) or at their own UnifiedPush endpoint (Android). Document both. Publish the gateway's source so the central gateway can be audited.

---

## 10. Encrypted media relay (offline recipients)

| System | Design |
|---|---|
| **Signal** | The client encrypts the attachment with a random key (AES-256-CBC + HMAC-SHA256, padded) and uploads the ciphertext to a CDN via a pre-authorized upload form. The message carries `{cdn_key, key, digest, size}`. The server never sees the key. The blob expires after about 30 days if nobody fetches it ([Signal protocol attachments, libsignal-service AttachmentCipher](https://github.com/signalapp/Signal-Android/tree/main/libsignal-service/src/main/java/org/whispersystems/signalservice/api/crypto)) |
| **Matrix** | The client encrypts with AES-256-CTR (JWK key, IV) and uploads to the media repo, which returns `mxc://` content. The encrypted event carries `file{url, key, iv, hashes.sha256, v:"v2"}` ([spec: sending encrypted attachments](https://spec.matrix.org/latest/client-server-api/#sending-encrypted-attachments)). Media download is authenticated since spec v1.11 ([authenticated media](https://spec.matrix.org/latest/client-server-api/#content-repository)). Thumbnails are encrypted separately. |

**Design notes for the bot**
- The client encrypts with AES-256-GCM (or Signal's CBC+HMAC), using a random key per file. It uploads **ciphertext only**, in chunks (resumable), to the bot's media store. The E2E chat message carries `{blob_id, key, sha256, size, mime, thumb{...}}`.
- In a **group served by the bot**, message content is readable by the bot anyway unless the design adds group E2E. State plainly which model applies. For closed groups, keep media opaque even if text is not.
- The server enforces a size cap, a per-address quota, a per-room retention TTL, and content-addressed dedupe on the **ciphertext** hash (which only dedupes identical re-sends).
- Downloads are authorized by room membership, checked at fetch time. Serve with range support, so voice notes can stream.
- The thumbnail/blurhash goes inside the message (a small, encrypted preview), so offline recipients do not trigger a fetch until the user taps. This also avoids the IP-leak class the Spixi app already treats as security-relevant (#82).
- Delete the blob when every member's cumulative `DELIVERED` seq has passed it and the TTL has elapsed, **or** on TTL alone. Do not keep per-recipient fetch records.

---

## 11. .NET 8 → .NET 10 migration (console server)

**Support dates:** .NET 8 LTS and .NET 9 STS both end support on **2026-11-10**. **.NET 10 LTS runs until November 2028** ([.NET blog](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)). The server must be on .NET 10 before the November 2026 Patch Tuesday.

**Breaking changes that matter here** ([full list](https://learn.microsoft.com/dotnet/core/compatibility/10)):
| Change | Impact | Action |
|---|---|---|
| **No default SIGTERM/SIGHUP handler** ([doc](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/10.0/sigterm-signal-handler)) | `ProcessExit` no longer runs on `docker stop`/`systemctl stop`. RocksDB is not flushed or closed. | Use the Generic Host, or `PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; shutdownCts.Cancel(); })` |
| `BufferedStream.WriteByte` no longer flushes implicitly | Custom framing code may stall | Flush explicitly after each frame |
| `System.Linq.AsyncEnumerable` moved in-box | Conflicts with `System.Linq.Async` package | Remove the package, or alias it |
| Default trace-context propagator = W3C | Only affects tracing interop | None |
| OpenSSL ≥ 1.1.1 required; `DOTNET_OPENSSL_VERSION_OVERRIDE` / `DOTNET_ICU_VERSION_OVERRIDE` env vars renamed | Old distros / custom env | Check the systemd unit env |
| `dotnet restore` audits transitive deps; HTTP feeds now error | Builds may fail on vulnerable transitive packages or `http://` feeds | Fix or suppress explicitly; use HTTPS feeds only |
| CLI writes non-command output to stderr | CI scripts that parse stdout | Review scripts |
| Container default images → Ubuntu | Base-image assumptions | Pin `-noble-chiseled` explicitly |
| API obsoletions (SYSLIB warnings) | Build warnings, become errors with `-warnaserror` | Fix, or suppress per ID |
| System.Text.Json property-name conflict validation | Serialized DTOs with colliding names now throw | Covered by the snapshot tests (Verify) |

Migration steps: pin `global.json` to SDK 10.0.x → `TargetFramework net10.0` → update Microsoft.Extensions.* to 10.x → run the full test and fuzz suite → soak for 24 h against a .NET 8 baseline (memory, GC pauses, latency histogram) → ship. Confirm the RocksDB native package loads on the net10.0 RIDs you ship (linux-x64, linux-arm64) before anything else. The Spixi app history shows native-binding loading is the fragile part.

---

## Open items to verify during implementation
- Stryker.NET 4.16 on a net10.0 test project with xUnit v3/MTP. It is not stated explicitly in the release notes; a one-hour spike settles it.
- NBomber licence cost versus the in-house harness.
- Whether Ixian's RocksDB wrapper exposes `Checkpoint`, `DeleteRange` and prefix extractors. If not, those are small P/Invoke additions.
