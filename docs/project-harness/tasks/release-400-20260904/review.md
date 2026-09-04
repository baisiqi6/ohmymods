# v4.0.0 publication gate — APPROVED FOR BUILD/PACKAGE

## Operator findings

- `PatchRoles_NorseSquad.cs:263-285`: the coroutine captures a World reference but
  after yielding only checks object existence and HasWorldAuth. It does not recheck
  Mod Enabled, Game.Playing, Managers.world identity or the current gameLayer before
  replacing units. Disabling the mod during its pending window still allows mutation.
- `PatchRoles_NorseSquad.cs:86,267,309`: pending objects are stored in a shared static
  dictionary. A new load clears/refills the same dictionary; the old coroutine lacks
  a generation/current-world guard. Publication requires session-local ownership and
  invalidation before further scene mutation.
- `PatchRoles_NorseSquad.cs:334-336`: GameObject.name is used as prefab identity.
  Name is not authoritative pool provenance. Native World.ImproveCharacterNameReadability
  preserves the prefab substring, so that method alone does not prove repeated
  conversion; do not overclaim a demonstrated loop. Verify native pool origin and
  no duplicate conversion in a focused regression.

## Review process evidence

OMP session 01a06c9d-99a9-7000-8331-a27a17a1cd9f requested/observed kimi-code/k3,
resolvedModelIsFallback=false, read-only tools. Bash approval refusal respected.
Initial 12-minute and resumed 5-minute windows ended without a final verdict;
exit code 0 is NOT treated as approval. Internal fallback release400_safety review
was requested, with no source/deployment authority.

Internal independent reviewer `/root/release400_safety` returned CHANGES_REQUESTED
on the current-world/readiness gates and cross-load shared queue. An isolated
worker at `work/release400-safety-fix-20260904` is repairing only the two Norse/knight
files. No provider model substitution is claimed: the configured GLM fallback is
unavailable in the internal runtime; the internal agent uses its inherited model.

The same reviewer also found a TowerSpots delayed-context blocker: the old coroutine
could instantiate an unregistered base after a world change or authority loss. The
isolated TowerSpots worker added fail-closed current-world/gameLayer/Playing/auth/
Postbox gates immediately before retirement, Instantiate, and registration; it leaves
the per-world guard unconsumed when readiness fails. This change is awaiting the
combined build and final re-review.

Final independent review verdict: `APPROVED` after the KnightStyle stale-state fix
(`state.Knight.Pointer` match and `!NeedsRederive`) was applied. Norse load restore,
TowerSpots delayed context, shield-wall filtering, FriendlyTroll cleanup, ClockDiag,
ToolAssignment and BallistaBolt have no remaining static release blockers. Runtime
online, authority migration, island/load and shield-wall boundary testing remains
explicitly pending user acceptance.

## Build and mutation receipt

- Version: 4.0.0, Debug, no auto-deploy; build 0 warnings / 0 errors.
- Built DLL: 309760 bytes, D9A9AA2324F6A4FC4D4C4413DCFABDA4133240B1FCAE3FD7B3C84F27F7BA877A.
- E-copy DLL unchanged: EFBB0B0F98F4719280363ABAEBDBB8768758B3F8BE190F85009758DFA0A4DA26.
- No commit/push/tag/GitHub Release/package or DLL deployment performed this turn.
- Existing backup from previous deployment is retained. Saves, G drive and Steam
  installation untouched. User asked release only after gates pass; gate is closed.
