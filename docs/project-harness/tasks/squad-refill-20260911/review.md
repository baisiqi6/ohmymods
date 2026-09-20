# Independent final review

Verdict: PASS. No remaining P0-P2 finding in the reviewed diagnostic change.

Reviewed source hashes:
- PatchRoles_SquadRefillDiag.cs: 4180F6C3246F178D354F12734088FF11C3CC8BA6CC8E4C7A35EF0ED3475A6731
- SquadRosterSnapshot.cs: 90D6D750FD16E485D67D431F0261DD77EA513228D57D3774B0EF170CBEF4D98D

Confirmed closure of the prior review:
- Candidate's Unity object and pointer reads are inside its exception boundary.
- Extra candidate inspection is capped at 256 per selected Fetch. Further calls immediately mark truncation and return; incomplete effectiveReserve is explicitly unknown-partial.
- Roster warning output is independently protected against logger exceptions, preserving the surrounding real follower patrol.

The Fetch prefix/finalizer frame saves and restores nested context, including unsampled child calls. Native exceptions and availability results are not changed. Shared event budget is 30 lines, with a 3-second global gate and 30-second per-style Fetch gate. No extra scene/resource scan, assignment, or RPC is introduced.

Roster snapshots reuse the already acquired knight/archer arrays. At most five samples, 120 scaled seconds apart, inspect at most 64 knights and 1024 archers. Records explicitly identify cached observations and truncation; no roster repair is performed. Counts releases retained owner references in finally. The KnightStyle diff only threads the existing knights array into the existing follower pass and invokes Observe. The obsolete CrossbowmanTowerDiag source is absent from the isolated build.

Limitations: cached mismatch/far counts are diagnostic clues, not proof of stale native membership. Event counters with validators or truncation are incomplete. readMs measures snapshot reading/formatting before LogInfo, not logger or total frame cost. Diagnostic budgets end automatically and can miss later events. Actual native hook execution and in-game symptom reproduction still require Operator-controlled validation.

This reviewer only read source and evidence and wrote this report. No production/game/configuration code was changed, built, deployed or run by this reviewer. Operator reports actual dependency build succeeded with zero warnings/errors; final regression tests are independently owned.
