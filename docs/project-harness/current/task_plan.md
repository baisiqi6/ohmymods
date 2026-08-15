# Current Task Plan

- Item: `ghost-squads-013`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-16-ghost-squads-013`
- Goal: 把希腊 Cerberus 坐骑的一次召唤扩展为四支独立亡灵小队，共4名骑士与16名弓箭手；其中两队采用北境亡灵外观，但四队均保留希腊坐骑召唤行为。
- Authority: 用户明确授权实现；只改 IL2CPP 发布线。游戏运行中不得部署或替换 DLL。
- Scope: 保留原生希腊1+4并补三支独立1+4；两套北境视觉池固定使用30130/30131，不新增RPC，不直接使用北境神器AI，不修改Mono。
- Validation: 静态自审与Debug构建0 warning/0 error；源码提交c81adf1已推送，干净提交构建/独立副本DLL SHA-256均为16A0206C...8AE56。正在刷新候选包；运行时仍须验证20名单位、四个独立编队、2希腊+2北境外观、回收与联机池同步。
- Plan: `docs/project-harness/tasks/ghost-squads-013/plan.md`
