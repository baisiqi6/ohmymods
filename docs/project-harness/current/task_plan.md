# Current Task Plan

- Item: `role-qol-001`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-role-qol-001`
- Goal: 在已验收的狂战士 5+1 序列和隐士防绑架基础上，将酿酒师隐士稳定缩放为 1.15 倍。
- Authority: 缩放是双方确定性的纯视觉状态，不修改战斗、乘骑、建筑、存档或网络权威逻辑。
- Scope: 仅按 `HermitType.Baker` 判别；OnEnable/对象池复用时绝对设置 y=1.15 并登记现有缩放注册器，保留 x/z、朝向与其他隐士尺寸。
- Validation: 独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error，构建与独立副本部署 DLL SHA-256 均为 `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`。待实机观察酿酒师外观与对象池复用；不触碰 Steam、共享存档或正式 zip。
- Plan: `docs/project-harness/tasks/role-qol-001/plan.md`
