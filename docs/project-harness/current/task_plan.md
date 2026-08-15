# Current Task Plan

- Item: `fleetboat-recovery-009`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-fleetboat-recovery-009`
- Goal: 根据四个已完成的奥林匹斯神像交付任务，幂等恢复死亡换君主后丢失的 FleetBoat 所有权。
- Authority: 仅 Call of Olympus world-authority；不改任务、存档压缩数据或奖励动画，不新增 RPC/syncID/sidecar。
- Scope: ApplyToScene 完成后的 active/standby/carryForward 唯一来源判定、riverless standby 恢复、当前 biome 原生同步池生成与一次性诊断。
- Validation: worker 实现与独立 reviewer 静态 APPROVED；源码提交 `7710977` 已推送，从该干净 HEAD 重建 0/0 并部署独立副本，构建/部署 DLL SHA-256=`774F5ACFF413C76493456596ADE35D905C58CC9299F054747266FF2CF09607F3`。最新运行日志确认 expected=4/recovered=4/spawned-from-zero，随后 autosave 的当前第3岛含4个 FleetBoatSaveData（编号1至4，Idle，x约38.31至41.26）；实例恢复已通过，仍待玩家视觉确认及重复读档、换岛、死亡重生门禁。
- Plan: `docs/project-harness/tasks/fleetboat-recovery-009/plan.md`
