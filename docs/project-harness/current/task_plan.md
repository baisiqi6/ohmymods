# Current Task Plan

- Item: `candidate-package-007`
- Status: `done`
- Owner: `codex`
- Session: `codex-2026-08-15-candidate-package-007`
- Goal: 生成包含当前综合候选改动的根目录直装 IL2CPP 测试包。
- Authority: 只读取已构建 DLL 和既有 BepInEx/runtime 发行文件，不修改游戏逻辑、Steam、Mono 或共享存档。
- Scope: 重建 zip、校验根目录结构、required entries、UTF-8 文档、manifest 与构建/部署/zip DLL 三方哈希。
- Validation: 独立 reviewer APPROVED。最终 zip SHA-256=`952FB1ECF3EEE011FA2AF8FC0956D13069D24EA5777C31EAA980692497D2087F`，40,558,266 bytes / 312 entries；构建、独立副本与包内 DLL SHA-256 均为 `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`，结构与 UTF-8 门禁全部通过。
- Plan: `docs/project-harness/tasks/candidate-package-007/plan.md`
