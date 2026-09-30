# Opus #46 review brief — Session AD (the ours/his cutover rows)

Work order for the adversarial loop over the Session AD delta. The VERDICT is written back
into this file (§6) when the loop closes — a verdict not in the brief that ordered it is a
verdict nobody can find (#660).

## 1 · What the batch is

Damir's ask (2026-09-23, DECISIONS #927): "finalize the C# verbs if easy to do and just do them
all in a session". The rule: a row is OURS when the fix is app-side C# (an `ixian:` handler, a
`sendUiCommand` push, app storage) or a shell; HIS only when the fix is in Ixian-Core storage or
wire. Ixian-Core is FROZEN at `097341a` (sibling at `/home/claude/Ixian-Core`, read-only).

Built (all app-side, ZERO Core changes):

| row | what landed | where |
|---|---|---|
| CH6 | `addChat` pushes the excerpt KIND (11th arg); the shell's reverse-map + 14 `*SL{}` carriers deleted | `HomePage.getFriendMessageHelper(friend, out excerptKind)` · `home.html excerptFromPush` |
| C1/C2 | `addPaymentRequest` +4 args (kind · statusEnum · fiat · insufficient); updaters carry the enum | `SingleChatPage.insertMessage`, `paymentFiatFor`, `paymentInsufficient` · `chat.html` |
| S11 | `ixian:landtab:<id>` verb → `HomePage.landOnTab` → `landOnTab` push; the `spixi.landtab` storage handshake + [LANDTAB] probe deleted | `SettingsPage`, `HomePage`, `home.html`, `settings.html` |
| #274 | the `spixi.settings.view` stash deleted (no reload survives it since #334) | `settings.html` |
| C16 | premise refuted (Core persists the remote delete); the real gap = the LOCAL delete's chats row → `UIHelpers.refreshChatRow` on both paths; `spixi.exdel` retired (boot sweep) | `UIHelpers`, `SingleChatPage`, `chat.html`, `home.html` |
| W11 | `requestFundsResponse` store mutation + write moved OUT of the chat-page gate | `StreamProcessor.receiveData` |
| S2 | `BackupPage.recordBackup/lastBackupTimestamp/pushBackupStatus` → `setLastBackup` push; C# gates the nudge; the `spixi.backup.last` stamp + poll deleted from 3 shells; `SpixiContentPage.onRepresentedNative` (new virtual) | `BackupPage`, `SettingsPage`, `HomePage.displayBackupReminder`, `SpixiContentPage`, `settings.html`, `settings_backup.html`, `home.html` |
| S9 | `ixian:dev` on SettingsPage, gated on the `devMode` preference; cap `dev` | `SettingsPage`, `settings.html` |
| CH4 | favorites = app preference (`SChatPrefs.cs`, NEW); `ixian:favchat:<addr>:on\|off` + `setChatFavorite` echo; the unread TOTAL mute-aware at 3 sites (`unreadTotalForBadge`) | `SChatPrefs.cs`, `HomePage`, `SingleChatPage`, `SettingsPage`, `home.html`, `chats-row-menu.js`, `chats-shell.js` |
| C6 | tip token = `tip:<amount>` (was `tip:System.Byte[]`); tip TOTAL summed → 4th `addReactions` arg | `SingleChatPage` (`updateReactions`, `UiBatch.addReactions`), `chat.html` |
| C21 | typist address rides the typing event → `showUserTyping(who, nick)`; never under `hidesParticipants` | `StreamProcessor`, `SingleChatPage.showTyping`, `chat.html` |
| CH8 | `addChatReaction` 5th arg = reactor address (never blind); nick falls back to the contact | `HomePage.updateChatReaction`, `home.html` |
| CI2 | `addPaymentActivity` +2 args (four-state status · in/out) | `ContactDetails`, `contact_details.html` |
| C4 | `addCall` 9th arg `callActive`; `VoIPManager.endVoIPSession` one branch (the duplicate-id arm was dead) | `SingleChatPage`, `VoIPManager`, `chat.html` |
| CO3/CO4 | `onRequestResult("0", reason)` on 3 refusals; `onValidAddress(address)` echo | `ContactNewPage`, `contact_new.html`, `contacts-page.js` |
| C17/CO1 | `addContact` +2 args (relation · kind); `setRelation` push + `ixian:undorequest` on ContactDetails | `HomePage`, `ContactDetails`, `home.html`, `contact_details.html` |
| A4 | `MiniAppManager.fetchWithReason` (+15 s HttpClient timeout); `showUrlError(reason)` from both hosts | `MiniAppManager`, `AppNewPage`, `HomePage`, `app_new.html`, `home.html` |
| i18n-C# | the two hardcoded English strings | `HomePage`, `LockPage` |
| CH3 | media purge: `:media` suffix on the 3 delete verbs; `SContacts.collectReceivedMedia` (read-only, BEFORE) + `purgeFiles` (AFTER success); `TransferManager.isInsideDownloadsRoot` | `HomePage`, `SContacts`, `TransferManager`, `home.html` |
| sweep | `[STARTDIAG]` retired; `getChatPages`/`getChatPage` dead #284 branch deleted; iOS `clearRemoteNotifications` dead body deleted | `App.xaml.cs`, `HomePage`, `Utils.cs`, `Platforms/iOS/SPushService.cs` |

NOT built, recorded: A3 (no trust model to back a flag) · A5b (already fixed #840) · CO5 (Core
has no getter) · CH3 mark-read (ruled out 2026-08-27) · C10 (verify-first: the tree already
links the fulfilling payment under the request's id and Core refuses the duplicate) · batch 4's
CI6/C22/A8/FC1/C12/NT1(b)/PV1 (not reached).

★ NO wallet-password work (L8, L2, MAJOR #8) — untouched by order. CORE-10 untouched.

## 2 · How to run

* Repo: `/home/claude/spixi-ad` (a git-init'd snapshot; `git diff` = the whole session delta).
  Built shells under `Spixi/Resources/Raw/html/` are regenerated. `Spixi/Meta/SChatPrefs.cs` is
  NEW (untracked). Ixian-Core beside it at `/home/claude/Ixian-Core` @ `097341a`.
* ⚠ NOTHING here compiles C#. Read for compile errors as a first-class finding: a wrong
  overload, a missing `using`, a nullable warning-as-error, a `ref` on a property.
* The suite: `node scripts/smoke-test.mjs` (~5 min). The Session AD pins are the block titled
  `★ Session AD — the ours/his cutover rows`. Scratch harnesses `scripts/_ad-batch1.tmp.mjs` /
  `_ad-batch3.tmp.mjs` boot the BUILT shells through `executeUiCommand` (`node <file>`).
* Mutation runner: `node scripts/_ad-mut.tmp.mjs <root> "<block title>"` runs ONE suite
  block against any tree root.

## 3 · Auditor scopes (disjoint, READ-ONLY — findings with file:line, no edits)

| auditor | scope | first questions |
|---|---|---|
| A · C# chat + money | `SingleChatPage.xaml.cs` (C1/C2/C6/C21/C4/CH4), `StreamProcessor.cs` (W11/C21), `UIHelpers.cs` (C16), `VoIPManager.cs`, `SChatPrefs.cs`, `HomePage.xaml.cs` (CH6/CH4/CH8/S11/CH3 handlers), `SContacts.cs`, `TransferManager.cs` | Does W11 sign or broadcast anything? Can `:media` delete a file OUTSIDE Downloads, or an OUTGOING file? Can the tip total or the enum be spoofed by a peer into a wrong sign/state? Does `unreadTotalForBadge` run under the right lock? Does any new log line carry an address or a URL token? Any compile error? |
| B · C# settings/contacts/apps + platform | `SettingsPage.xaml.cs` (S9/S2/landtab), `BackupPage.xaml.cs`, `SpixiContentPage.cs` (`onRepresentedNative`), `ContactDetails.xaml.cs` (CI2/C17/undorequest), `ContactNewPage.xaml.cs` (CO3/CO4), `AppNewPage.xaml.cs` + `MiniAppManager.cs` (A4), `LockPage.xaml.cs`, `Platforms/iOS/SPushService.cs` (#490), `Utils.cs` (getChatPages), `App.xaml.cs` (STARTDIAG) | Does `ixian:dev` open DevPage for a user without devMode? Is `recordBackup` written only when a file really reached the sheet? Does the #797 cancel-first grammar hold on every touched `onNavigating` branch (`ixian:undorequest` on ContactDetails runs UN-fenced by design — is that safe)? Does `landOnTab` accept an arbitrary id? Any compile error (the `onRepresentedNative` override modifier, the tuple returns, `TaskCanceledException`)? |
| C · shells + components + PINS | `home.html`, `chat.html`, `settings.html`, `settings_backup.html`, `contact_details.html`, `contact_new.html`, `app_new.html`, `src/bridge/contacts-page.js`, `chats-row-menu.js`, `chats-shell.js`, and EVERY pin the session added or re-based in `scripts/smoke-test.mjs` (grep `Session AD`) | Which pin passes vacuously (#771 — a raw-text read satisfied by a comment; #798 — a list where a walk was owed)? Which re-based negative can be satisfied by re-adding the old mechanism under another spelling? Does an OLD exe (10-arg addChat, 14-arg addPaymentRequest, 4-arg addChatReaction, 5-arg addContact, 3-arg addReactions, no `setLastBackup`) still render correctly — the shells must degrade, never throw? Does the ESLint no-undef gate still pass? |

Then ONE fresh break-my-verdict reviewer over the FIXES.

## 4 · Non-negotiables (re-verify, do not assume)

* ★ #221: the conversation WebView stays walled — no new cross-pane JS, no shared context.
* Money: nothing is signed or broadcast from WebView-composed data. W11 records a txid the
  PAYER sent; C2's `insufficient` is a display hint, the native review page decides.
* No `spixi.*` key ADDED. Keys REMOVED: `spixi.landtab`, `spixi.settings.view`,
  `spixi.backup.last`, `spixi.exdel.*` (boot sweep). Preferences ADDED (C#, not WebView):
  `fav.<addr>`, `lastBackupTimestamp`.
* New verbs: `ixian:landtab:<id>` (SettingsPage) · `ixian:favchat:<addr>:on|off` (HomePage) ·
  `ixian:dev` (SettingsPage) · `ixian:undorequest` (ContactDetails) · the `:media` suffix on
  `removehistory`/`removecontact`/`leavegroup`. Every one cancels the navigation (#797).
* No WebView-supplied string reaches a filesystem op. CH3 deletes paths C# WROTE (`fm.filePath`),
  re-checked against the Downloads root.
* Log lines: no address, no URL token, no message text (`Utils.logSafe` / type-only).

## 5 · Accepted dials (do not re-litigate)

* C2 excludes the FEE from `insufficient` — the native review page prices it.
* CH4 pins stay FE-local (`spixi.pins`); only favorites moved to a preference.
* CH3 purges RECEIVED files only; outgoing source files are the user's, anywhere on the device.
* C21 names the typist in non-blind rooms only; a 1:1 pill carries no name.
* S2 pushes the timestamp only ("dirty-since count" has no signal).
* The `mutechat` warn line that prints the URL token is PRE-EXISTING (not this batch); the
  new `favchat` twin does not print it.

## 6 · Verdict (2026-09-23, three rounds on Opus — DECISIONS #929)

**NOT CLAIMED CLEAN.** Round 3 came back with 0 MAJOR, 1 code MINOR and 6 pin holes; all are
fixed and mutation-killed, but no fourth reviewer has read the round-3 fixes (the loop's own
rule). Damir's F5 + walk is the next reader.

| round | reviewer | result |
|---|---|---|
| r1 | 3 disjoint auditors (A · B · C) + verify | **3 MAJOR** — W11's row lookup PEER-WRITABLE by id · S9's DevPage never showed (an overlay page's own `PushModalAsync`) · C17 dead action on a legacy Unknown-state contact — + ~20 MINOR/pin findings (`setCapDev` inverted · C17 guard unused · CH3 walk `break`-able · CH6 belt threshold · S2 order · C2 early return · Q12 via const · Session AD pins spelled from the author's list). ALL FIXED. |
| r2 | fresh break-my-verdict over the r1 fixes | **2 MAJOR** — CH3's other-contact walk on the UI thread + a read failure deleting a shared file · the re-root accepting ANY recorded parent — + MINORs (`FileShare.Delete` · CH8 nick gating · CH4 `pendingDeletion` + fav cleanup + OS badge · A4 one deadline · S2 `recordBackup` after the await · C2 unverified balance). ALL FIXED. |
| r3 | fresh break-my-verdict over the r2 fixes | **0 MAJOR · 1 code MINOR** (the re-root trusted a parent NAME whose leaf the peer controls → `hasDotSegment` refuses any `.`/`..` segment on the RAW path) · **6 pin holes**, each a one-token mutation left green (W11 predicate substring-matched → canonical EQUALS · txid validation guardable → exact try shape · CH3 owner skip matched inside `!other…` → exact line · re-root gate matched un-negated → the negated chain · C17 `status = "fail"` in the success arm → the arm sets "ok" and only there · C2 `anyVerified = true` → the initial value) · 5 NITs (`IsPathRooted` before `GetFullPath` — a #772 claim made true · the storage walk's spaced spelling · `pushBackupStatus` fenced once · CH4 Core-side removals + `readMessagesRaw` zero-slot + S9 `",dev"` — logged as dials). ALL FIXED except the three logged. |

Also found by the AFTER run itself: `#574 ①`'s pin went RED on this batch's own `// CH4` comment
(a 600-char RAW window, #771) → re-based to a branch-sliced, comment-stripped property.

**Mutations:** r3's 13 through the real pin text, zero survivors (plus the r1/r2 kills recorded
in #929). **Non-negotiables re-verified in every round:** #221 wall untouched · nothing signed or
broadcast from WebView data · no `spixi.*` key added, four families retired · every new verb
cancels the navigation · no WebView string reaches a filesystem op · no address/path/URL token
in any new log line.

**Suite:** BEFORE `4817 / the 2 KNOWN` → AFTER `see the closing line of the Session AD row in
CLAUDE.md` (the run that reads this tree, not a prediction).
