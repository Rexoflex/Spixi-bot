# ⑥ — THE OPUS ADVERSARIAL REVIEW OF SESSIONS A–H + THE GATE SWEEP (Session H, 2026-08-31)

**Order:** `docs/prompt-session-h.md` §⑥ (the #46 rule; Damir: "delegate the adversarial
loop to Opus"). **Scope:** the whole Sessions A–G delta (`c2831c2a` → `47d955e8`,
DECISIONS #642–#722) PLUS the never-reviewed Session H working tree. **Protocol:** three
read-only Opus auditors with disjoint scopes → verify → fix → a FRESH break-my-verdict
Opus reviewer over the fixes → round 2 over the fixes-for-fixes → CLEAN. The
security-handover gate sweep ran as a fourth parallel agent
(`docs/security-gate-sweep-642-722.md`, with the Session H resolutions appended).

**VERDICT: PASS.** Round totals: **3 MAJOR · 14 MINOR · 12 NIT** found; every MAJOR and
MINOR fixed or explicitly logged (B's three unprovable suspicions, the two Core-gated
rows); round 2 over the fixes: **0 MAJOR · 0 MINOR · 1 NIT** (landed).
Closing gates: smoke **3891 / the 3 known** · locales 787 ALL CLEAN · cs-syntax 140 + 1 ·
mutations: 8/8 (Session H pins, auditor C) + 4/4 (the fix round) + the reviewer's
re-kills — no survivor left standing.

## The three MAJORs — every one a lesson already in the canon, recurring

**A-MAJOR-1 (auditor A, JS): Session H reintroduced the L8 class on the surfaces it
slowed down.** The in-shell exit slide (220 ms) nulled the takeover handles and told C#
`homeoverlay:0` at t=0 — so a second hardware back inside the window found no route and
fell through to `base.OnBackButtonPressed()`, **backgrounding the app with the cover
still on glass** (contacts, wallet Receive, wallet Send). `SpixiContentPage.cs:1476`
wrote this exact defect down for the native stage; the guard was never extended.
FIX: `homeOverlayLevel()` counts a `.c-subslide--out` cover as level 2; the exit's
remove callback re-syncs (so 0 lands when the cover is GONE, and the OS-bar repaint
snaps back to the pixel it was aligned to); a second back during the exit ABORTS the
slide (settle → instant finish) — the native L8 grammar. ★ The round-1 reviewer then
found the SAME hole on the FOURTH surface (settings sub-levels: back #2 during the exit
ran `exitSettings()` from under a live screen) — fixed with the same arm in `onBack`.
**When a reviewer finds the same class twice, the design is the suspect (#658): the
lesson here is that a surface which GAINS an exit animation must inherit the L8
double-back guard in the same batch, not in the next review.**

**C-MAJOR-1 (auditor C, gates): a stale `Raw/html` passed the suite green.** Mutation A
stripped the slide wiring from the BUILT shells (sources intact — "somebody forgot
build-shells", the #285/#287/#288 class, hit three times before) → **BASELINE OK, 3875,
zero pins flipped.** The suite verified src/ structurally and the shipped artifacts only
at enumerated values. FIX: `build-shells --check` — every DEFAULT shell rebuilt in
memory, nothing written, exit 1 naming each drifted artifact — run BY the suite as a
gate (the i18n-overflow-audit shape). ⚠ Auditor C's own first prototype of the gate
passed vacuously (the flag fell through to the key parser and it compared ZERO shells);
the landed version filters the flag and compares the six externals unconditionally, and
the reviewer re-killed it twice.

**C-MAJOR-2 (auditor C, gates): the icons registries had no equality gate.** Session G
shipped `icons.js` ahead of `icons.iife.js` (external-link in one, not the other — the
#710 pin was red in every clean clone, hidden by an uncommitted local file). Only
per-glyph enumerations existed — the open-ended-list shape #658 ruled against; mutation
C dropped `trash` from the iife + shipped copy and five destructive rows shipped an
empty `<svg>` with the suite green. FIX: a structural gate (equal key sets AND equal
bodies across icons.js ≡ icons.iife.js ≡ shipped spixi.icons.js) + a read-back in
generate-icons that exits 1 on a partial write.

## The rest, compressed

**Auditor B (C#, 0 MAJOR · 8 MINOR · 6 NIT):** signSend peer-scoped on the two
locked surfaces (`expectedRecipient` in SPayments — HomePage stays unscoped by design) ·
Downloads/AppDetails reset their back-mirror on load (#337 lesson) and align
`OnBackButtonPressed` with `routeShellBack` (`&& pageLoaded`) · ContactDetails
`ixian:leave` bare prefix → Equals+Ordinal · [EXCERPTDIAG] capped at 12 lines/process ·
the false "returned true ⇒ posted" invariant in SpixiPendingMessageProcessor documented
(Core's OfflinePushMessages skips a removed friend and still returns true → **BE row
CORE-7**) · the L10 comment's "every push site passes 0" premise corrected
(ContactNewPage:166 passes 4000 — behaviour unchanged) · loadMembers' lock-contention
note (BotUsers writes the roster to disk under the same lock — Core, BE) ·
resolveMessageChannel's conditional coverage written above the method · groupDelivered
computed lazily · the drifted page-count comments replaced with pointers. LOGGED, not
fixed: S-1 (stale roster replay via messageQueue on a reload — gated by reload paths
that do not exist today), S-2 (a boot-race window nobody could construct), S-3
(receipt-storm doubling — needs a device measurement).

**Auditor C's remaining rows:** `extract-strings --check` now MEANS check (sweep ↔
en-us.json, both directions, exit 1 — a new English fallback can no longer ship in 13
locales with every gate green) · build-shells writes carry the bundle's NUL /
lone-surrogate / short-write gate (#255/#262 both shipped NUL debris before) · the
settings pane-branch negative pin brace-matches its own closer (the first cut went
vacuous at any nested `} else {`) · SHELLS/DEFAULT membership derived, the bundle-order
pin derived from actual importers.

**Auditor A's remaining rows:** a third ⊕ tap during the tray's 300 ms exit re-opens
instead of no-opping · `rebuildHub` settles an in-flight slide (a backup-stamp poll
could vanish a sliding screen) · the exit gets a transparent tap shield for its 220 ms
(the pointer-dead dying layer passed first-frame taps through to the revealed view —
"tap a contact row, open an unrelated conversation").

**The gate sweep (`docs/security-gate-sweep-642-722.md`):** zero new HTML sinks /
network fetches / WebView settings / filesystem ops in the whole A–H delta; the
strongest row is a REMOVAL (L1 deleted the legacy money pages whose confirm never named
the destination). Four OURS items — all resolved in-session (see the doc's appended
resolutions): the transfer-id log line now logs length only; the in-app privacy summary
no longer claims "no personal data" (and, per the round-1 reviewer, no longer OVERSELLS
the OneSignal disclosure on Windows/Catalyst, where no push provider exists);
privacy-policy §4.4 records P1 as shipped + the P2 opt-out, platform-scoped. The
diagnostics stay armed until Damir's walk; each is pinned as a SET. `ixian:call` staying
`Equals` (the one property keeping `ixian:callback` out of the ungated hang-up) is
pinned.

## What this loop says about the gates

Auditor C's summary line deserves keeping verbatim: *"the pins are not decorative; the
layer above them is thin."* Every Session H pin aimed at went red under mutation,
including two semantic mutations that left the pinned literals intact — but three
mutations that ship a visibly broken APP survived, all in the same blind spot: the suite
proved src/ and trusted the artifacts. The three new gates (--check for shells, the
registry equality, --check for strings) close the artifact half structurally.
