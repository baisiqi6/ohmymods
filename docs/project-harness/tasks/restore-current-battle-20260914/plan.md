# Restore current island battle — 2026-09-14

User selected preserving current town while restoring serpent, built horse and night attacks. This follows the return-normal-island task; that earlier acceptance did not test battle state.

Baseline: latest global-v35 A3806BCD (3205 objects), not earlier A588/3DE saves. Backup in operator restore-current-battle-20260914/private-before-global-v35. Five scalar edits only: quest9 step5→4, secured9 false, kingdom unsafe, serpent11→1, tail5→1. Correct ordinary-island horse restored through construction ProjectTemplate, native registration and native save. No carry replay, no population/fleet duplication. Main DLL319418A0 preserved. No commit/release.

Worker: local OMP deepseek/deepseek-v4-flash max, process19876 session artifacts in operator task sessions. Worker source frozen; root integrates safety fixes into separate runtime directory. Built-in reviewer checks target context, async single save, correct prefab, persistent one horse.

Acceptance: build/API and exact-five-edit audit; correct visible E Build22992091 process; native successful save contains exactly one ordinary horse; active serpent and real natural night enemy wave; helper removed only after game exits; reload verification; leave visible game paused. Record actual vs untested observations separately.
