# Purchased hero archers do not take tower guard jobs

The only production file in this slice is `il2cpp/HeroArcherTowerPolicy.cs`.
Operator integration: call `HeroArcherTowerPolicy.Observe(archer)` at the beginning of the existing
`HeroArcherRuntime.Observe`, before its ordinary hero combat eligibility checks. Its Harmony classes
are assembly-discovered; no `Main`/plugin change is required if the normal assembly-wide patch path is used.

Eligibility is `HeroArcherRuntime.Enabled && HeroRecruitment.IsPurchased`, deliberately not `IsHero`.
Turning the feature off restores native job eligibility. Turning it back on releases a purchased unit's
already assigned tower through `Archer.ExitGuardSlot`; no MOD writes to guard, collision, transform,
scanner or wallet fields. A slot that currently names somebody else is never evicted.

## Actual 2.4 read-only native audit

All listed addresses are from the permitted E-drive 2.4 IL2CPP test copy. `audit_native.py` reproduces
method-table RVA/span/uniqueness checks and records the DLL hash in `native-audit.json`.

| Method | RVA | Adjacent-method span | Assembly-CSharp same-address slots |
|---|---|---|---|
| Archer.IsAvailableForJob | 0x4b2880 | 320 | 1 |
| Archer.AssignJob | 0x4af340 | 544 | 1 |
| Archer.SetGuardSlot | 0x4b5d20 | 144 | 1 |
| Archer.EnterGuardSlot | 0x4b0f40 | 1184 | 1 |
| Archer.ExitGuardSlot | 0x4b13e0 | 1008 | 1 |
| GuardSlot.ExitArcher | 0x57f660 | 320 | 1 |
| Kingdom.DistributeTowerArchers | 0x59da10 | 832 | 1 |
| Kingdom.FetchArchersForJob | 0x59de90 | 1104 | 1 |

Disassembly verified `FetchArchersForJob` calls `IsAvailableForJob` at 0x59e052 and `AssignJob` at
0x59e281. Actual `AssignJob` contains inlined slot assignment, without a call to `SetGuardSlot`.
Therefore availability and final assignment both need gates, as do explicit set/enter paths.
None is a short getter or a shared Assembly-CSharp method-table address. Availability/assignment/entry
prologues include RIP-relative initialization guards; a native detour must relocate whole instructions.
Static uniqueness is not proof of an installed detour's safety.

`ExitGuardSlot` calls `GuardSlot.ExitArcher` at 0x4b1454. The latter calls `Kingdom.AddGuardSlot`
at 0x57f76f; this can synchronously redistribute tower archers. The four hero-specific gates reject the
hero during that nested operation, while ordinary replacements remain eligible. This slice never
changes `SpecialTowerDuplicateCleanup` or its existing distribution suppression scope.

## Validation

`dotnet run --project tests/hero-tower-policy/Tests.csproj`: **31 assertions**, executing the production
policy and Harmony method bodies against native-boundary stubs. Covers hero/ordinary/knight/unknown
job decisions, explicit set/enter, off/on, already assigned release, normal synchronous replacement,
reentrant observation, stale links, native failure throttling, and bounded stale-record cleanup.

`dotnet build tests/hero-tower-policy/Interop.csproj -p:BaseIntermediateOutputPath=obj/interop/ -p:OutputPath=bin/interop/`:
**0 warnings / 0 errors**, references actual E-drive 2.4 interop and Harmony assemblies. This is compilation,
not a Unity execution test. No game was started, no candidate was installed, no save/config was changed.

Native exit failure is retried at most three times for one unchanged assignment, spaced by one second;
the feature toggle or a different assignment allows a new attempt. Unknown ownership or unreadable
job type remains native behavior. These boundaries avoid repeated per-frame release and foreign-slot edits.
