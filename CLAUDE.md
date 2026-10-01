# CLAUDE.md — Spixi Bot rework

Orientation for any AI or human working on this repo. **Keep this file to one screen of rules plus
"where we are now". History goes to `docs/status-log.md`, never here.** (Lesson from Spixi: a
300 KB CLAUDE.md is slow, expensive and goes stale.)

## 1 · Who, for whom, what

| | |
|---|---|
| **Owner** | Damir (Ixian). Reviews every batch, owns product decisions, commits and pushes. |
| **Protocol / Core reviewer** | The Ixian BE engineer. Any change to the wire protocol, Ixian-Core or the Spixi client needs his approval. |
| **Users** | The Ixian community (the official channel) · people who download Spixi and try it · later: self-hosters running open communities, closed groups and AI-agent channels. |
| **Goal** | Rework Spixi-Bot into Ixian's primary social channel: messages arrive, joining is fast, moderation works, admins are managed by IXI address, self-hosting is safe by default. |
| **Standard** | A senior engineering team's work. Research before design, designs graded before build, tests before change, CI blocks merges. Not vibe-coded. |

## 2 · Non-negotiable rules

1. **Verify at the source before building.** A claim in a doc, a handoff or an audit is a hypothesis
   until the code or a measurement confirms it. Cite `file:line` or a test name.
2. **Measure before fixing performance or delivery problems.** No blind fixes.
3. **Every significant decision gets a row in `DECISIONS.md` when it is made** (architecture,
   protocol, naming, scope). Provisional rows are 🟡; superseded rows are marked, never deleted.
4. **Tests are behavioural.** A test runs code and asserts a result. No test may pass by reading
   source text (Spixi lesson: grep-based pins were defeated by comments and spelling).
5. **CI is the gate.** A red build blocks the merge. Gates: see `docs/process.md` §G.
6. **Compatibility:** the current store Spixi app and the redesign must keep working. Contract tests
   pin both (`docs/research/D-client-contract.md`). A protocol change needs BE approval first.
7. **Security:** follow `docs/threat-model.md`. The bot never holds member funds. No member
   address, IP, message content, public key or secret reaches logs or metrics. Safe defaults for self-hosters.
8. **One logical unit per PR**, into `rework/bot` on the fork. Upstream PR to `ixian-platform` per
   milestone.
9. **Language rule (Damir):** chat replies to Damir are written in ASD-STE100 Simplified Technical
   English (short sentences, active voice, one instruction per sentence). Docs, code comments and
   commit messages are exempt.
10. **No AI attribution in commits or PRs (D-046):** no `Claude-Session:` link, no `Co-Authored-By: Claude`
    line, no "Generated with" footer.

## 3 · How we work — FORGE for engineering

Every session follows `docs/process.md`: **F**eed context → **O**utcome, not task → **R**everse
interview → **G**enerate designs, then grade (rubric + tests + adversarial review) → **E**xport the
win (skills, templates, lessons). Session prompts use `docs/templates/session-prompt.md`.

## 4 · Repo layout

| Path | What |
|---|---|
| `SpixiBot/` | The bot (legacy .NET 8 code, `a5a3442`) |
| `../Ixian-Core/` | Shared Core, compiled in (`SpixiBot.sln`). Bot was built against Core `f6fb55b`; it does **not** compile against Core HEAD (see research B §0). |
| `DECISIONS.md` | Decision log — read before changing anything |
| `docs/process.md` | The workflow, gates and rubric |
| `docs/templates/` | Session prompt, batch rubric, handoff, review brief, review verdict |
| `docs/examples/` | Reference-quality docs from the Spixi project (examples beat adjectives) |
| `docs/research/` | Session-1 audits A–E (source-verified, `file:line`) |
| `docs/design/` | Base designs and their grading |
| `docs/roadmap.md` | v1 batches, design items, BE critical path |
| `docs/reviews/` | Adversarial review rounds and verdicts |
| `docs/threat-model.md` | Threats, controls, security rules |
| `docs/test-strategy.md` | Test pyramid, harness, CI gates |
| `docs/status-log.md` | Session-by-session history |
| `docs/handoff-*.md` | The latest handoff = where the next session starts |
| `docs/prompts/session-N.md` | Session prompts, numbered; the highest number is the next one to paste |

## 5 · Environment notes

- The cloud workspace cannot reach nuget.org or dot.net (organization egress policy). **Compile and
  tests run in CI (GitHub Actions) or on Damir's machine.** Research and docs work in the cloud.
- Windows clone: `C:\Users\Damir\Claude\Projects\Spixi-bot`, Core beside it at `..\Ixian-Core`.
- Git: `origin` = `Rexoflex/Spixi-Bot` (fork), `upstream` = `ixian-platform/Spixi-bot`. Work on
  `rework/bot`.

## 6 · Where we are now

Session 5 (2026-10-01): B1a items 4–7 (reactions/deletes, info variants, unknown codes, leave) + W12 green in CI,
14 cases, 11 self-tests (PR #3, D-047, D-048). B1a contract scenarios are complete. **Next session: read
`docs/handoff-2026-10-01-s5.md` first, then paste `docs/prompts/session-6.md`.**
