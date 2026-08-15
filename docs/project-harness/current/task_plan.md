# Current Task Plan

- Item: `candidate-package-007`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-candidate-package-007`
- Goal: 生成包含当前综合候选改动的根目录直装 IL2CPP 测试包。
- Authority: 只读取已构建 DLL 和既有 BepInEx/runtime 发行文件，不修改游戏逻辑、Steam、Mono 或共享存档。
- Scope: 重建 zip、校验根目录结构、required entries、UTF-8 文档、manifest 与构建/部署/zip DLL 三方哈希。
- Validation: 当前构建与独立副本 DLL SHA-256 均为 `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`；待打包脚本与独立 reviewer 完成。
- Plan: `docs/project-harness/tasks/candidate-package-007/plan.md`
