# Examples — what "good" looks like

Three documents from the Spixi frontend rework (Damir's project, same owner). They are here for
their **shape**, not their content. When you write a handoff, a review brief or a verdict for the
bot, match these.

| File | Type | Why it is a good example |
|---|---|---|
| `spixi/handoff-2026-09-30e.md` | Handoff | Starts with *read this first*, gives exact state (HEAD, gates, what is uncommitted), then a table per work item with **mechanism first** ("find the mechanism, then build") and where to look. Separates "not ours" (BE) and "accepted" so nothing is re-litigated. |
| `spixi/opus-review-brief-session-ad.md` | Review brief | A table of every item that landed **with the files it touched**, an explicit "not built, and why" list, how to run it, and a place where the verdict is written back (§6). The reviewer never has to guess scope. |
| `spixi/opus-review-verdict-session-h.md` | Review verdict | Leads with the verdict and the numbers (MAJOR/MINOR/NIT per round, gates, mutation kills). Each MAJOR states the **mechanism, the evidence, the fix and the lesson**, including where the auditor's own first fix was wrong. |

What to copy: verdict-first writing, evidence per claim, tables over prose, explicit scope
boundaries, lessons stated as rules.

What not to copy: Spixi-specific numbering (`#NNN` → here `D-NNN`), and long status lines. The bot
keeps history in `docs/status-log.md`, not in `CLAUDE.md`.
