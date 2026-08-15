# Current Task Plan

- Item: `ghost-squads-013`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-16-ghost-squads-013`
- Goal: 把希腊 Cerberus 坐骑的一次召唤扩展为四支独立亡灵小队，共4名骑士与16名弓箭手；两支希腊队保留主动向外作战/距离回收，两支北境队保留跟随君主/原生30秒生命周期。
- Authority: 用户明确授权实现；只改 IL2CPP 发布线。游戏运行中不得部署或替换 DLL。
- Scope: 保留原生希腊1+4并补一支希腊1+4、两支北境1+4；北境完整行为克隆池固定使用30130/30131并写入原生30秒Duration，不新增RPC，不修改Mono。
- Validation: 修订源码提交0cd629e已推送；干净构建0 warning/0 error并部署独立副本，构建/部署DLL SHA-256均为024ADAC7...64A39。正在刷新候选包；运行时须验证20名单位、四个独立编队、两套AI、各自回收与联机池同步。
- Plan: `docs/project-harness/tasks/ghost-squads-013/plan.md`
