# Greek Fire independent regression

2026-09-12. Final result: **143 passed, 0 failed** against the actual `../build/PatchRoles_GreekFire.cs` Compile Link. No production implementation was copied into the test logic. `PatchRoles_GreekFireAssets.cs` also compiles directly from canonical. The retained crossbow method fixture is checked verbatim against canonical by `run.ps1` before every run.

| Source | SHA256 | Result |
| --- | --- | --- |
| Canonical baseline | `3607BA796CF1D0D384124C9F075761C427EB1310DF3ABAD0F65BF1B9E907E1A4` | 119 passed, 24 failed |
| Final build candidate | `90B2002DCB801007565DFC2866E75C7BCF8FFF1E02824D55295BD98475E60287` | 143 passed, 0 failed |
| Canonical asset helper | `958CE9EBC92774C42833DE7C7BE78647A486E8700E085B56413A615EF9A65117` | Linked in both runs |

The original 79 tests remain green. The additional 64 cover home-wall stationary owned followers, invalid/stale shooting targets, closest invalid followed by a valid buffer entry, foreign and unassigned archers, native pointer aliases, death/inert/grabbed/embarked states, ownership changes during native GetAll, scanner replacement, active attack replacement, movement during scan, authority loss, finite values, exact active projectile range, null arrays/elements/scanners, count vs length, stale tail entries, hard 64-candidate boundary, and .25-second polling with unchanged shared scene cache interval. Native buff durations and successful cooldown still run through the existing suite and an additional scanner-triggered timing case.

Initial worker source `B5453776BA0FFB368E25609B95B35B09A4A5BFF42C2F546CCBC44F02180CC33A` failed compilation with CS0136 because the fallback local reused `scanner`. The operator's build candidate resolves that compile failure and additionally revalidates eligibility, ownership, scanner pointer and current range after GetAll. Worker source was not edited by this test agent.

Run with PowerShell: `& ./combat-home-towers-20260912/greek-tests/run.ps1`. `receipt.json` records source paths, before/after stable hashes, counts and exit codes; `baseline.log` and `candidate.log` contain all individual results. The script runs the same suite against both source versions and verifies that compile-linked sources do not change during each run.

Boundary: these are .NET 8 managed tests with generic Unity/IL2CPP/native API stubs. They test production decision flow, call ordering and the stated buffer contract; they do not prove native scanner refresh internals, native RPC transport, rendered fire arrows, actual IL2CPP execution, or in-game saves. No game was started and no canonical, worker, save, config, Git or network state was changed by this agent.
