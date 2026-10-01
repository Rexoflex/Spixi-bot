# Session 4 — review round 1 (pre-push, Opus, against Core k and f6 source)

Verdict: NOT CLEAN — fixed before push 1 (`4de5a2d`).

| # | Sev | Finding | Fix |
|---|---|---|---|
| 1 | MAJOR | `Join_Handshake` waited for `user` after the second info; the `user` answer comes from the pre-accept cascade (SimNode getInfo on connect → bot setPubKey creates a normal member, `StreamProcessor.cs:432`; Core k sends getUsers for a non-accepted bot, `CoreStreamProcessor.cs:2676-2700`) → both normal join cases would time out | wait from the join mark; class comment documents both cascades |
| 2 | MINOR | History step C bound = refresh send mark; a late second join replay of m4 could count as "replayed" | bound = index of the refresh's own `channel` event (in-order processing, `NetworkQueue.cs:258-297`) |
| 3 | MINOR | s1 echo (ack before relay, `:76` before `:418`) could land inside step D's window | wait for the s1 echo after step C |
| 4 | MINOR | xUnit v3 runs classes in parallel; port race (`FreePortPair`) | `AssemblyInfo.cs` disables parallelization |
| 5 | NIT | failed `set-cursor` waited 20 s | `failIf` on command error |
| 6 | NIT | HttpClient/log tail skipped when the header guard throws | header wait moved to `ScenarioRun` after `Bot` is assigned |
