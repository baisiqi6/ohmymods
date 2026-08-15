# Current Task Plan

- Item: `population-performance-010`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-population-performance-010`
- Goal: 修复BeggarCamp与Baker导致的营地人口泄漏，并安全清理当前岛超额乞丐。
- Authority: 仅world-authority生成/清理；不直接修改压缩存档，客户端只接原生同步；游戏运行时不替换DLL。
- Scope: 本次只交付真实每营地5人硬cap与分帧安全清理；帧时诊断、工具反向分配和Idle LOD另开任务，禁止混入本次候选。
- Validation: 当前land7只读统计为2158对象/1132角色，其中Worker458、Peasant301、Beggar158；其他岛仅52～137角色。A+B已实现，worker与独立reviewer静态APPROVED，Debug构建0 warning/0 error，等待干净提交重建、备份、独立副本部署与实测。
- Plan: `docs/project-harness/tasks/population-performance-010/plan.md`
