# Lessons — rules that saved (or would have saved) time

Carried from the Spixi frontend rework, plus lessons from this repo. One line each, newest last.
Source numbers are Spixi DECISIONS rows unless marked `D-`.

| # | Lesson | Origin |
|---|---|---|
| L1 | Verify a premise in the code before building on it; handoff and audit claims are hypotheses. | Spixi #215, #297, #660 |
| L2 | Measure before fixing a performance or delivery problem. | Spixi #294 |
| L3 | A test that reads source text proves nothing; comments and spellings defeat it. Test behaviour. | Spixi #771, #798 |
| L4 | A test written from the author's own list of cases is not yet a test. Derive cases, or generate them (property tests). | Spixi #798 |
| L5 | Mutate before believing a test: change the code and confirm the test fails. | Spixi #802 and many |
| L6 | When a reviewer finds the same class of defect twice, question the design, not the patch. | Spixi #658 |
| L7 | A verdict not written back into the brief that ordered it cannot be found later. | Spixi #660 |
| L8 | A status row records what a session found, not what the tree is. | Spixi #906 |
| L9 | A comment that states an invariant the code does not enforce is a defect. | Spixi #772 |
| L10 | Keep CLAUDE.md short; history belongs in a log file. | Spixi, 2026-10 |
| L11 | `grep -c $'\0'` matches every line; check NUL bytes with `tr -dc '\000' \| wc -c`. | Spixi Session S; re-hit 2026-10-01 |
| L12 | Research agents with disjoint scopes + a verification step found problems a quick scan missed (unauthenticated client transport, sender spoofing, a likely crash). | D-session-1 |
| L13 | Classify per message type, with `file:line`, before writing a security rule; "all X are signed" was false and a whole rule rested on it. | session-1 review R1-M1/M2 |
| L14 | A control for a live critical exposure needs its own adversarial pass: the first two B0 versions still left impersonation open. | session-1 review R2/R3 |
| L15 | Ask "is this worth doing at all?" before specifying a fix for a system that is being retired; it cut B0 from ~3–4 days to a 10-minute check. | session 2, D-041 |
| L16 | An operator procedure is inside the threat model: the first "safe" check sent the operator into the stored-XSS admin page. | session 2 review R1-M1 |
| L17 | From the session VM run git with `--no-optional-locks`; an index.lock the VM cannot delete blocks GitHub Desktop. | session 2 |
