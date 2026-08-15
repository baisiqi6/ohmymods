# Current Task Plan

- Item: `tool-assignment-015`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-16-tool-assignment-015`
- Goal: 在探针已证明2.4入口真实命中后，以原生评分和独立solver实现高人口稀疏反向工具分配。
- Authority: 用户明确授权开始此前讨论的性能优化；只改IL2CPP发布线。游戏运行中不得部署或替换DLL。
- Scope: 只替换高人口且工具稀疏的`DroppableRegistrar.ReassignClaimers`；复用原生评分与角色目标接口，不patch全局JobAssigner，不实现通用AI或Animator LOD。并加入Horn隐士y=1.15视觉微调。
- Validation: 探针实机为582 carriers/7～8 droppables、约9～10ms且连续约3秒命中。稀疏实现与Horn缩放提交147ea44已推送，从该提交重建0 warning/0 error并只部署独立副本；构建/部署DLL SHA-256均为2CE66091...2ADF09，等待切岛与工具行为实测。
- Plan: `docs/project-harness/tasks/tool-assignment-015/plan.md`
