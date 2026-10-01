# Session 3 · review round 2 — harness spike fixes

Scope: the round-1 fixes (same files). **Verdict: CLEAN** (0 MAJOR). Notes:

| ID | Note | Disposition |
|---|---|---|
| r1 | The ack gate assumes ack id = message id (round 1 accepted any ack) | Evidence: run 36847424722 shows the poster's ack `48bedcc0…` = the received message id; on timeout the test now lists every ack id it saw |
| r2 | A second channel list without a reconnect could still replay history | Known limit, recorded in `harness.md` §7 |
| r3 | Connect count included connects after delivery | Fixed: counts only before `received` |
| r4 | Doc comment moved onto the wrong member | Fixed |
| r5 | Smoke artifact kept an unredacted log | Fixed: stdout, stderr and logs are redacted before upload |
| r6 | `SimMember.DisposeAsync` became dead code | Fixed: members are disposed first (they get `quit`), then all processes |
