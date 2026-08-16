# worker 任务书：银行助手捡金币改为链式顺吸（bank-assistants-005 后续）

## 用户问题（原话归纳）

助手捡金币是"定位一枚→走过去→捡→停下→等下一次定位再捡"，每枚之间观感卡顿、速度慢，
跟不上玩家扔币节奏。原生银行家是碰到金币即吸收、沿一串金币一路跑一路吸无停顿。
期望：助手也像原生银行家一样顺滑连收。

## 当前实现的三个根因（Operator 已核实，`il2cpp/PatchEconomy_BankAssistants.cs`）

1. `UpdateMovingAssistants`（876-942 行）：每枚金币结算成功后 `helper.Target = null;
   SetAnimationSpeed(helper, 0f)` —— 动画停死。
2. 下一枚的分配只发生在 `ScanAndDispatch`（538-547 行，每 `SCAN_INTERVAL=0.5s` 一次）里
   `if (collector.Target == null)` 分支 —— 每枚金币之间必然卡 0~0.5s。
3. 一次只有 `_collectorIndex` 一个收集者；且必须走到距金币 `PICKUP_DISTANCE=0.22` 才结算，
   没有"顺路吸收"。

原生参考（只读 `game-source/Assembly-CSharp-2.1.0/Banker.cs`）：`ClaimCoins()` 批量
`TryFriendlyClaim` 范围内全部金币并设 `OnlyClaimer`，`GrabCoin()` 只负责朝目标跑，
真正的拾取由 Wallet 接触吸附（`CanGrabCoins` 常开），所以一串金币不停顿。
（注意：我们的助手按架构约束**禁止带 Wallet**——见 236 行不变量检查——顺吸必须由
协调器自己做，不能给助手加 Wallet。）

## 修复设计（Operator 锁死）

只改 `il2cpp/PatchEconomy_BankAssistants.cs` 的协调器部分（`BankAssistantCoordinator`），
保持全部架构不变量：助手无 Wallet/Persistent/Banker 组件、仅 world-authority 扫描认领结算、
经济入账仍走 `PatchEconomy_Banker.DepositFromAssistant` 原子入口、主银行家内域/
助手外域分工不变、成熟期 3s 不变、池与 prefab 构建不动。

### 1. 链式目标（核心）

- 把"最新成熟币快照"留存为静态字段（`ScanAndDispatch` 每 0.3s 刷新一次，`SCAN_INTERVAL`
  0.5→0.3 允许改），成员含 coin 引用与首次观测时间。
- 新增 `AssignNextTarget(helper)`：从快照里选**未被认领且距离助手最近**（`|coin.x−actor.x|`
  最小，并列按 x 再按 instanceID 决确定性）的合法金币，走现有 `TryAssign` 的认领/瞬移/朝向
  逻辑（瞬移规则不变：`CarriedCoins==0 || 距离>6` 才瞬移；链式中途不瞬移）。
- `UpdateMovingAssistants` 每枚结算成功后**当帧**调用 `AssignNextTarget(helper)`：
  - 成功 → 保持 `Moving=true`、动画速度维持 `ASSISTANT_RUN_SPEED`，**不得出现任何停顿帧**；
  - 失败（快照空/全被认领/在线门禁）→ 该助手收工：动画归零，`CarriedCoins>0` 则走现有
    `TeleportHomeAndDeposit` 回家清账。
- `ScanAndDispatch` 里原有的分配分支改为：只给"没有目标且属于活跃收集者集合"的助手补分配。

### 2. 顺路扫吸（sweep）

- 新增常量 `SWEEP_RADIUS = 0.35f`。
- 每帧对每个**带目标的活跃收集者**（移动中）遍历快照：对未被认领、且
  `|coin.x − actor.x| ≤ SWEEP_RADIUS` 的成熟金币，直接执行与现有结算块完全相同的
  认领+提交路径（`TryFriendlyClaim` → `CanCommitPickup` 全部门禁 → `SetFake/pickedUp`
  → `DepositFromAssistant` → 池回收 → `Claims/Observed` 清理 → `CarriedCoins++`），
  **结算后同样接 `AssignNextTarget` 链式逻辑**。
- 快照为空时跳过遍历；只在 authority 侧执行。这是"一路边跑边吸"的实现，替代逐枚精确定位。
- 原有"走到目标 0.22 内结算"的路径保留（目标币本身仍这样结算，顺吸只处理路过的其他币）。

### 3. 积压扩容（多助手）

- `_collectorIndex` 单值改为活跃集合（如 `bool[] ActiveCollector` 或等价），选中顺序沿用
  `_nextCollectorIndex` 轮转。
- 每次扫描后计算目标活跃数：`1 + MatureBuffer.Count / 8`，上限 4（常量
  `ACTIVE_SCALING_STEP = 8f`）。多助手时各自 `AssignNextTarget` 天然按"离谁近归谁"分区，
  不需要显式分区算法；`UpdateIdlePatrols` 跳过所有活跃者。
- `CarriedCoins >= capacity` 的助手照旧回家清账并退出活跃集合；下次扫描按积压量再补位。
- 激活数量发生变化（如 1→2）时记一条限流 LogInfo（同数值 30s 内不重复），形如
  `[BankAssistants] active collectors=2 (mature=9)`。

### 4. 其余约束

- `EnsureFourActors`/`ResetAll`/`HandlePoolRebuild`/`OnDestroy`/`FlushUncreditedCoins` 中对
  `_collectorIndex` 的引用全部适配新集合语义（含"收集者 Actor 消失时清其活跃位"）。
- 现有一次性诊断日志（first assignment/first submission/scan 汇总行）保留，scan 汇总行
  追加 `collectors=<活跃数>`。
- 所有在线门禁（`HasClientCaughtUp`/`parentHeaderRef` 检查）、`CanCommitPickup` 的每一项、
  域检查（主银行家内域的币不碰）、失败回滚（`accepted != 1` 时恢复 `pickedUp/fake` 并
  释放认领）一行都不能弱化。
- 只改这一个文件；现代 C#；不引入依赖。禁止 commit/push/部署。
- 你的沙箱无法跑 dotnet 构建：做 AST 语法自查 + 逐符号语义核对，构建由 Operator 执行。

## 验收（汇报必须包含）

1. 改动后的关键方法完整代码（AssignNextTarget、UpdateMovingAssistants、顺吸路径、
   活跃集合管理、ScanAndDispatch 分配段）；
2. 逐条对照本任务书 3 个设计点 + 第 4 节约束的落实说明；
3. `git status --porcelain` 输出（应只有本文件与 PatchEconomy_BankAssistants.cs 变更）；
4. 明确声明：未动池/prefab 构建与架构不变量、未弱化任何门禁/回滚/在线检查。
