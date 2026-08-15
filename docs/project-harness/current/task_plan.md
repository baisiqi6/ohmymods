# Current Task Plan

- Item: `fleetboat-recovery-009`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-fleetboat-recovery-009`
- Goal: 幂等恢复死亡换君主后丢失的 FleetBoat 所有权，并修复换岛后四艘原生carry船最终停在同一x坐标的编队初始化异常。
- Authority: 仅 Call of Olympus world-authority；不改任务、存档压缩数据或奖励动画，不新增 RPC/syncID/sidecar。
- Scope: ApplyToScene 完成后的 active/standby/carryForward 唯一来源判定、riverless standby 恢复、当前 biome 原生同步池生成；第二阶段只对已有安全状态FleetBoat保留原生side并调用UpdateBase按BoatNumber展开，不增删/瞬移船。
- Validation: 第一阶段已静态批准并部署；运行日志/存档确认数量恢复为4。换岛后日志为active=4/carry=4/missing=0，证明原生生成且补丁未重复补船；新岛autosave四船均Idle且x=37.96。死亡前v16/v13旧档四船在同一侧但x依次为-68.58/-67.62/-66.58/-65.62，间距约1，证明正常契约是保留一侧并按BoatNumber展开，而非奇偶分左右。第二阶段已实现并获独立reviewer静态APPROVED；从干净提交e643d9f重新Debug构建0 warning/0 error并仅部署独立副本，构建/部署DLL SHA-256=8A829791422A575A4157DC036F943DC7446FE8C98600D080BA686A57E5A6F039。待实机确认同侧约1单位间距。
- Plan: `docs/project-harness/tasks/fleetboat-recovery-009/plan.md`
