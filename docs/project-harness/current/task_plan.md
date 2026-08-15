# Current Task Plan

- Item: `fleetboat-recovery-009`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-fleetboat-recovery-009`
- Goal: 根据四个已完成的奥林匹斯神像交付任务，幂等恢复死亡换君主后丢失的 FleetBoat 所有权。
- Authority: 仅 Call of Olympus world-authority；不改任务、存档压缩数据或奖励动画，不新增 RPC/syncID/sidecar。
- Scope: ApplyToScene 完成后的 active/standby/carryForward 唯一来源判定、riverless standby 恢复、当前 biome 原生同步池生成与一次性诊断。
- Validation: worker 实现与独立 reviewer 静态 APPROVED，当前 build 0/0；提交后从干净 HEAD 重建并部署独立副本，游戏内 0/2/4 船、重复读档、换岛和死亡重生门禁仍由用户实测。
- Plan: `docs/project-harness/tasks/fleetboat-recovery-009/plan.md`
