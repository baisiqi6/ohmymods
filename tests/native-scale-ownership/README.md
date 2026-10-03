# Native scale timing regression

The project directly compiles the complete production `GreekScaleScope.cs`,
`PatchRoles_Worker.cs`, and `PatchRoles_KnightStyle.cs`. Unity, Harmony and the
unrelated career/combat/persistence dependencies are managed boundary stubs.
No Unity native call or game process is used.

The tests drive the real holder LateUpdate and ConvertToSoldier/ConvertToHunter
postfix methods. Native direction and RecvAbsFace scale writes use the exact
`(direction, 1, 1)` values verified from actual 2.4 offline disassembly. Checks
cover late reset repair, signed X/Z retention, current follower constants and
effective Norse style, leaving a squad, true crossbow marker protection, root and
Mover identity, scope transitions, activation and embark gates, pending setter
transactions, reentrant snapshot replacement, driver creation retry and the
existing deferred worker registration sequence.

The allocation check measures this helper's warmed managed traversal using
stubs. It does not claim that actual Il2CppInterop wrappers allocate zero bytes,
nor that this LateUpdate runs after every other late callback in Unity.

Use `-p:BepInExPluginsPath=` on builds and direct all generated bin/obj output to
an independent evidence directory. Execute the resulting managed Tests.dll.

`crossbow-ownership/Ownership.csproj` separately links the same complete three
production files plus the complete real `CrossbowmanLifecycle.cs`, including its
real marker fields, identity reader, Apply/Strip and enable/disable/pool handlers.
It covers the temporarily inactive Selected career, Fire SO, pending residue,
actual failed pool cleanup handoff, ordinary reused markers and unreadable marker
ownership. Its boundary logger records deliberate native faults; each case
requires either no unexpected error or exactly the intended lifecycle fault.
The original 40-case Program remains unchanged and the extra project is excluded
from its default compile glob.
