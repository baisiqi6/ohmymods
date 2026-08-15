# Current Task Plan

- Item: `ability-cooldowns-014`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-16-ability-cooldowns-014`
- Goal: 把 HermesStaff 与 Cerberus 亡灵召唤的2.4.0原生30秒冷却统一微调为22.5秒。
- Authority: 用户明确授权实现；只改 IL2CPP 发布线。游戏运行中不得部署或替换 DLL。
- Scope: 只改冷却；法杖控制数量/永久性及亡灵数量/行为/持续时间/回收/RPC/池均不变，不修改Mono。
- Validation: 资源实读确认两者均为30秒；目标22.5秒。初始禁部署Debug构建0 warning/0 error；等待提交、干净重建、独立副本部署与候选包刷新。
- Plan: `docs/project-harness/tasks/ability-cooldowns-014/plan.md`
