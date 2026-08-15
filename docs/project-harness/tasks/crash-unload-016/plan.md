# crash-unload-016 — 出航卸载阶段栈溢出首修

## 模式与目标

- 模式：high-risk。该问题涉及重复进程崩溃、独立副本部署与跨岛恢复验证。
- 目标：消除高人口岛出航时在旧岛 `PrepareUnload` 阶段发生的 `0xc00000fd` 栈溢出，同时保持盾牌状态、编队清理、碰撞力恢复和联机 RPC 原生语义。
- 当前结论是“高置信实验性首修”，不是已完成的根因证明；只有连续跨岛实测通过后才能关闭。

## 只读证据

- 2026-08-16 02:45 与 02:54 两次崩溃均以相同末链结束：保存完成后进入 `Managers.PrepareUnload`，旧岛层级禁用触发 `NpcShieldUser.OnDisable -> SetShieldEnabled(false) -> pickupShieldSound.Play -> AudioPool/AudioEmitter.ResetAndPlay`，随后出现 disabled audio source 警告并以栈溢出退出。
- Windows WER 对两次事件均记录 `0xc00000fd`；同类 StackHash 在稀疏工具分配实现部署前已出现，因此不能把新工具分配优化当作根因。
- 原生卸载先完成 `EmbarkableRegistrar.PrepareUnload`，再递归禁用 level。盾牌禁用期间播放音效会在正在禁用的 `world.gameLayer` 下激活或重挂音频池对象，和末链及错误高度吻合。
- 当前异常岛存档只读统计为 25 个 `NpcShieldUser`，其中 15 个北境 Worker 处于持盾状态；希腊 Worker 的裸组件均为无盾，不直接触发这条非早退路径。

## 首修范围

1. 仅在 Mod 总开关启用时，为公开 `Managers.PrepareUnload` 建立同步卸载作用域：Prefix 进入，Postfix 正常退出，Finalizer 异常兜底；恢复必须幂等，下一场景初始化再清理任何遗留标志。总开关关闭时完整走原版。
2. 只在该作用域中的 `NpcShieldUser.SetShieldEnabled(false)` 调用（实际来源为旧岛禁用时的 `OnDisable`）临时保存并置空 `pickupShieldSound`。
3. 让原生 `SetShieldEnabled(false)` 完整继续执行；不得抑制 `hasShield`、盾牌子物体、事件、再生协程、编队注销、碰撞力恢复或 RPC。
4. Postfix 与 Finalizer 都幂等恢复原音效引用；普通拾盾、破盾和非卸载禁用的声音完全不变。
5. 每次卸载只记录开始/完成与抑制数量摘要，不逐单位刷日志。

## 明确非目标

- 不回滚或修改稀疏工具分配、人口协调器、FleetBoat、存档、银行系统或召唤系统。
- 首阶段不修改 `EmbarkableRegistrar`。它在高人口卸载时存在重复重分配开销，但当前证据不指向递归栈溢出；若首修仍复现，再依据新日志单独立项。
- 不 patch 私有 `SendShieldEnabled` thunk，不新增池、RPC、协议字段或永久状态。
- 不触碰 Steam 正式目录，不在游戏运行时替换 DLL，不把本次候选先标为稳定发布版。

## 验证与退出条件

1. IL2CPP Debug 禁部署构建 0 warning / 0 error，`git diff --check` 和 checklist validator 通过。
2. 独立 reviewer 静态确认卸载作用域、音效引用恢复和原生盾牌清理/RPC 均无回归。
3. 仅部署独立测试副本；日志应出现卸载作用域摘要且 `suppressed > 0`，不再出现对应 disabled-audio 末链。
4. 从高人口岛连续至少两次完整出航并真正进入新岛；期间无新增 WER `0xc00000fd`。
5. 非卸载场景验证拾盾/破盾声音正常，盾状态与联机 RPC 没有改变。
6. 在完成第 3～5 项前，本任务保持 `doing`，只能称“候选已部署、待实机”，不得标记完成或稳定。

## 当前状态

- worker 已完成最窄补丁；总开关门禁的首轮 reviewer P1 已修复。
- 独立 reviewer 最终静态 `APPROVED`；本地复建 0 warning / 0 error。
- 当前候选 DLL SHA-256：`ACC466D928534F7620F7610A9C20590F301FAA617DA33EF96B53DBAEDD21D0A9`。
- 独立测试副本仍有游戏进程在运行，因此尚未部署；运行时退出条件仍全部保留。

## 回滚

- 回滚仅移除本任务新增的卸载音效作用域补丁并重新构建、部署独立副本；不回退用户存档或其他已验收功能。
- 若仍崩溃，保留新 `Player.log` / `Player-prev.log` / WER 时间与末链，停止重复猜测，再决定是否审查 `EmbarkableRegistrar` 的卸载期重分配。
