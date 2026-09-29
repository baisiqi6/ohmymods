Narrow regression linked directly to `il2cpp/DeadlandsFollowerCapture.cs` (issue-79 slice 1). Run
`dotnet run -c Release --project Regression.csproj`.

Covers the bounded bookkeeping only: eligibility/exclusions, nearest bindable selection without
rescan, native primary viewport 0 (primary-viewport-unresolved without scanning; no Camera.main
or alternate anchor), frame dedup, 8s/1024/30s limits, pause without sample consumption, identity
and qualification stop reasons, and same-frame disable/enable invalidation. Also covers observer
registration gating, controller-keyed clip-name refresh, full ≤32 name export, event order/cap,
same-controller raw-phase backjumps, distinct short/full state hashes and state speed fields,
null sprite accounting, session metadata reset, zero Unity writes, no reads after stop, and CSV
write-failure handling. The 0.5 large-backjump threshold does not detect every short restart.

The stubs model the Unity/game boundaries the capture touches and count reads/writes. Passing them
does not establish real IL2CPP detours, actual Animator phase/curve behavior or what the game renders;
on-site recording is scheduled separately by the operator.
