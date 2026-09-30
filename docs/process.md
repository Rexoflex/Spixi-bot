# Process — FORGE for engineering

How every session and every batch runs on this repo. Adapted from the FORGE method for working
with Claude (Feed context · Outcome · Reverse interview · Generate then grade · Export the win;
source: a YouTube video Damir shared, `youtube.com/watch?v=YbrrHwwBv1o`; the steps were taken from its description, the video itself was not reviewed), plus the engineering
lessons from the Spixi frontend rework (`docs/examples/`). Short on purpose.

## F · Feed context

A session starts with context, not with a task.

- Read `CLAUDE.md` → the newest `docs/handoff-*.md` → the `DECISIONS.md` rows it names.
- The examples in `docs/examples/` show the expected standard of a verdict, a handoff and a review
  brief. **Examples beat adjectives:** copy their shape, not their words.
- Verify the handoff's claims against the tree before relying on them (a status row records what a
  session found, not what the code is).

## O · Outcome, not task

Every session prompt (`docs/templates/session-prompt.md`) answers three questions before any work:

1. **Who is it for?** (member, admin, self-hoster, BE engineer)
2. **What should they be able to do afterwards?**
3. **How will we know it worked?** → acceptance criteria written as tests or measurements.
   Not "fix delivery" but "a member offline for 1 h receives all 50 missed messages on reconnect;
   integration test `Delivery_ReconnectGap_NoLoss` proves it".

## R · Reverse interview

Before building, Claude asks the questions it needs, as clickable options where possible, one
round at a time, and points out anything vague or contradictory. **No build starts until Damir says
"go".** Answers that are decisions become `DECISIONS.md` rows at once.

## G · Generate, then grade

### G1 · Designs: three versions, graded

For any design choice with real alternatives (architecture, storage, protocol, permissions,
rollout): write **three genuinely different options**, grade each against the rubric below, be
harsh, pick the winner, then fix the winner's named weaknesses. The result is a `DECISIONS.md`
row that links the grading doc. (Code is not written three times; code is graded by G2.)

**Design rubric** (score 1–5, weights in brackets):

| Criterion | Question |
|---|---|
| Correctness & safety (×3) | Does it meet the threat model and the success criteria without new risk? |
| Member impact (×2) | Compatibility with store + redesign apps; migration cost to members. |
| Long-term maintainability (×2) | Simplicity, test-ability, fit with the maintained Ixian stack. |
| Self-hosting (×1) | Safe defaults, packaging, no hidden dependency on Ixian servers. |
| Delivery speed (×1) | Fits the 4–6 week target with strict gates. |
| Extensibility (×1) | Closed mode, agents, media, integrations later without rework. |

### G2 · Code: every batch passes the gates

| Gate | Rule |
|---|---|
| Build | `dotnet build` warnings-as-errors for new code; CI green on every push. |
| Unit + property tests | Behavioural only. New logic has tests before or with the code. |
| Contract tests | Store + redesign client behaviour pinned (research D). |
| Integration | Local multi-node run (bot + simulated clients) for delivery, sync, moderation. |
| Mutation | Stryker.NET on storage, permissions, parsers, delivery policy. Report-only until the .NET 10 trial run passes (D-028), then break threshold 60, rising. |
| Fuzz | SharpFuzz on every wire parser before a release. |
| Security | Threat-model check for the batch; CodeQL + dependency review in CI. |
| Adversarial review | The #46 loop (below) until CLEAN. |
| Docs | DECISIONS rows, status log, handoff updated in the same PR. |

### The adversarial review loop (#46, proven in Spixi)

1. **Auditors:** 2–4 read-only Opus sub-agents with **disjoint** scopes, findings with `file:line`.
2. **Verify:** each finding is checked against the source before it is fixed (reviewers are wrong
   sometimes).
3. **Fix:** fix agents with disjoint file scopes and explicit cross-file contracts.
4. **Break-my-verdict:** a FRESH reviewer attacks the fixes, not the original work.
5. Loop 3–4 until a round finds **0 MAJOR**. Write the verdict into the brief that ordered it.
6. Rules learned: a test written from the author's own list of cases is not yet a test; when a
   reviewer finds the same class of defect twice, question the design, not the patch.

## E · Export the win

At the end of every session:

- **Handoff** (`docs/templates/handoff.md`): state, what changed, what is next, open questions.
- **Status log:** one entry in `docs/status-log.md`. `CLAUDE.md` §6 changes only if the next start
  point changes.
- **Lessons:** anything that would have saved time → `docs/lessons.md`. If a procedure repeated
  twice, propose it as a skill.

## Definition of ready (before a batch starts)

- Its outcome and acceptance tests are written (template).
- Any design choice it needs has a graded doc and a DECISIONS row.
- Every research claim a DECISIONS row relies on was re-read at source by someone other than its author.
- BE approvals it needs are requested (roadmap "BE critical path").

## Working rules

| Topic | Rule |
|---|---|
| PR size | One outcome per PR; aim for < 600 changed lines excluding tests and fixtures. |
| Branch protection | `rework/bot` on the fork: required CI checks, no force-push, merge by Damir only. |
| Versions | Semantic versions tagged `xsbc-<major>.<minor>.<patch>` (continues the legacy scheme); release notes list protocol changes. |
| Decision rows | Include the alternatives considered and the consequences, not only the choice. |
| Security gate | Before every upstream PR: an introduced-vs-inherited sweep over the delta (does this exposure exist in the legacy baseline `a5a3442`? no → fix before the PR). |
| Incidents | The launch week has a named on-call person and the rollback runbook from D4. |
| Review independence | In-session Opus reviewers by default (D-010); security, migration and protocol batches also get one review in a separate session. |

## Definition of done (per batch)

A batch is done when every G2 gate is green, the review verdict is CLEAN, the PR is merged by Damir,
and the handoff names the next step.
