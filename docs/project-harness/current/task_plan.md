# Current Task Plan

- Item: `tool-assignment-015`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-16-tool-assignment-015`
- Goal: 先用零行为改动的有限日志探针证明2.4工具分配入口真实命中并测量基线，再决定是否启用高人口稀疏反向分配。
- Authority: 用户明确授权开始此前讨论的性能优化；只改IL2CPP发布线。游戏运行中不得部署或替换DLL。
- Scope: 当前只实现`DroppableRegistrar.ReassignClaimers` Prefix/Postfix探针；不写工具目标/claim，不patch全局JobAssigner，不实现通用AI或Animator LOD。
- Validation: 2.1与2.4调用链/interop签名已核对；探针提交20c457b已推送，从该提交重建0 warning/0 error并只部署独立副本，构建/部署DLL SHA-256均为BDC91E72...F3E723。运行时必须连续记录约3秒命中后才进入替换算法。
- Plan: `docs/project-harness/tasks/tool-assignment-015/plan.md`
