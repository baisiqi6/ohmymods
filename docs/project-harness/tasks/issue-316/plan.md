# Windows support collector implementation plan — Issue #316

**Goal:** 玩家双击懒人包根目录的“一键收集排查资料.cmd”，自动收齐允许范围内的日志与存档，获得可手动发送的 ZIP。

**Architecture:** 使用 Windows PowerShell 5.1 与 .NET Framework 内建文件流/ZipArchive；工具与游戏插件独立。现有 Windows 打包入口显式纳入相同三份工具文件，同时能生成独立补充工具 ZIP。

**Tech stack:** CMD (ASCII/CRLF), PowerShell 5.1 (UTF-8 BOM), .NET ZipArchive, Python stdlib package tests.

## Authority and baseline

- User request: 在最新 Windows 懒人包里加入一键收集日志与存档工具，玩家自行发送维护者。
- Issue: https://github.com/baisiqi6/ohmymods/issues/316 ; Owner Mac Max / Codex.
- Base release/v9.5.13@cb467d4c1a23474f9678794bb42c3f12cfd4b5cf. Existing public Windows11.1.0 tag 4ca7898e.
- Branch codex/windows-support-collector-316. Source/host/Windows fixture/review/PR are separate evidence. No game operation, installation, merge or release asset replacement.
- Ordinary bounded tooling task; this plan is the single specification. No checklist transition or new runtime framework. Private evidence: .local/tasks/windows-support-collector-20261031/.
- Brief design presented in chat. CMD+PS5.1 selected over extra EXE runtime and log-only collection, matching the user's requested one-click/manual-send workflow. GLM5.3 max r2 approves this contract; initial requests/changes retained in private evidence.

## Contract

1. Root files: 一键收集排查资料.cmd, OhMyMods-CollectDiagnostics.ps1, 排查资料工具说明.txt. Store under release/support-tools/windows; source manifest pins file sizes/SHA256. PS1 content UTF8 BOM; CMD ASCII CRLF uses powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0OhMyMods-CollectDiagnostics.ps1". Preserve exit code and pause. Bypass is only this process; no Set-ExecutionPolicy.
2. Default game root is script directory with game EXE or BepInEx marker. Current-user Unity root is UserProfile/AppData/LocalLow/noio/KingdomTwoCrowns. Default output is GetFolderPath(LocalApplicationData)/OhMyMods/Diagnostics, not a synced Desktop. Always show actual ZIP path; open result folder. No admin, extra installation or network.
3. Optional fixture roots GameRoot/UserDataRoot/OutputRoot require all three when any is specified, and must not overlap. NoOpen may be used alone. Invalid roots never fall back to real saves. Do not scan parents/drives for missing game.
4. Source allowlist: BepInEx/LogOutput.log and optional LogOutput.log.1/.old; Unity Player.log/Player-prev.log; entire Unity Release tree; BepInEx/config/BepInEx.cfg and KingdomEnhancedMod.cfg; complete BepInEx/config/KingdomEnhancedMod tree including ModSave. Optional root BUILD-MANIFEST.txt, RELEASE_MANIFEST.json and ohmymods-win243-preview.json. EXE and plugin DLL only file version/hash metadata, never copied. Preserve relative layout in ZIP, do not capture other Mod configs or game/runtime binaries.
5. Only read/copy sources; no save request, shutdown/kill, repair, deletion, config write, registry/credentials or upload. No Compress-Archive. Explicit UTF8 ZipArchive entries, streaming FileShare.ReadWrite|Delete, bounded copying/hash. Max128MiB/file, 512MiB aggregate, 4096 files, depth16; test limits may decrease but never exceed defaults. Reject reparse/escape and output overlap; report enumerator/read/length/size failures.
6. Unique new output per run, never overwrite prior ZIPs. Partial collection still produces report and usable ZIP. No promised atomic paired backup: record start/end UTC, PS/tool versions, game-running observation, live-best-effort/changed flags, per-file relative path/status/size/hash/before-after metadata. Serialize explicit primitive DTOs only, depth <=8, no Provider objects or Get-Content -Raw to high-depth JSON. Reopen final ZIP and verify captured file hashes. Clean up only exclusively owned temp output if needed.
7. Exit0 complete, exit2 partial or live/changed snapshot, exit1 no usable report. Missing optional files do not alone make partial; absence/failure of main logs/save categories must be visible. Completion and README tell player ZIP contains logs/config/saves and may contain account identifiers; send privately to maintainers. Tool does not upload or prevent external sync software configured by user.

## Ownership and implementation

- Worker: only three release/support-tools/windows leaf files plus tests/windows-support-collector (PS5.1 synthetic suite and README). Never Main.cs/build.bat/il2cpp/AGENTS or existing packager.
- Operator: package_release.py integration, pinned support manifest, tests/windows243-package fixture updates, this plan and verification/progress. Same load_support_tools bytes feed main and add-on ZIP, tests compare each tool SHA, not overall ZIPs.
- Existing runtime-lock435 files, game fingerprints, plugin/DLL/version strings and historical published assets remain. Future release version decision is outside task; this is source capability plus local add-on artifact.
- Dispatch at Asia/Shanghai 2026-10-31 Saturday: OMP deepseek/deepseek-flash max; public model identity verified. Decision review OMP zhipu-coding-plan/glm-5.3 max. Independent final reviewer separate.

## Steps and acceptance

- [x] Read current source/runbook/collaboration, check existing Issue/PR semantic overlap; no active collector found. Create/read back Issue316. Isolated fresh base and current WindowsPS5.1 endpoint verified.
- [x] Baseline python3 -m unittest discover -s tests/windows243-package -v: 13 pass.
- [x] Decision r2 AP; source/data authority and encoding/ExecutionPolicy/cloud-output boundary fixed.
- [x] Tests first: missing-tool RED; implement collector; package support entries and hash rejection tests RED then green.
- [x] Run synthetic WindowsPS5.1 suite only under fresh TEMP roots: Chinese/spaces, ZIP/manifest equality, source hashes unchanged, missing/locked files, shared writer, reparse and bounds, invalid/overlapping roots, unavailable output and repeat no-overwrite. Do not start fake global game-named processes while another performance task may run.
- [x] Run Python packaging regression and support add-on equivalence/tamper tests. No unrelated IL2CPP rebuild needed (no product code changes).
- [x] Independent fixed-input source and native fixture review, address findings and reverify relevant cases; decision acceptance review.
- [ ] Commit/push own source, open linked PR, provide verified tool ZIP and clear extraction location. Do not call this a fix of the player's save error.

## Reviewed implementation refinements

R3 final verification is in verification.md. Aggregate actual reads include incomplete attempts and metadata; directory visits cap at twice MaxFiles. Copy changes compare pre/copied/post size. Existing ancestors are checked for reparse before IO; non-adversarial aliases/TOCTOU are not an atomic guarantee. Tests use only a child-process-local presence probe, with both false/true assertions. Normal ZIP name decoding and EFS are verified after the Framework-specific encoding correction. The accepted P3 for advanced drive-relative overrides does not affect the no-argument entry; all fixtures use fully qualified roots. No immutable published package was overwritten. All implementation/review/Windows evidence is distinct from game-save diagnosis.

Source-boundary note: eight intended player-README usage lines are part of this PR. Old runtime/plugin bytes and published assets remain unchanged. See verification.md for the accepted advanced-root/SUBST/TOCTOU and best-effort snapshot limitations.
