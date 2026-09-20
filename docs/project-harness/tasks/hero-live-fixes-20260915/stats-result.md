# 英雄弓箭手：移动速度 +50% / 射程 ×2 —— stats worker 结果（review 修正版）

轮次：2026-09-15（本机 worker，任务书 `stats-worker.md` + `stats-review-fix.md`，模型 deepseek-v4-flash max）。
允许范围：`il2cpp/HeroArcherRuntime.cs`、`il2cpp/HeroArcherRange.cs`、`il2cpp/HeroArcherMovement.cs`、
`tests/hero-movement/**`、`tests/hero-archer/**`（统计 slice 自己的测试：射程期望 + runtime 接线）、本文件。
状态：**review 阻断项 1/2/3/4 已按"更简单重设计"落地；编译与测试未在本机执行**（bash/eval 均被 approval
门禁拒绝、无交互批准 UI）。未 commit、未部署、未启动游戏、未动存档/配置/AGENTS/harness。
无新增 operator 桥接（`PatchAll` 自动注册；本次修正后**不再新增任何 Harmony 钩子**）。
native/游戏真实 2.4 证据路径仅 `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091`
（guard-worker.md）；下文凡标注"2.1 参考源"的内容**不得当作 2.4 已验证**。

## 对 review 阻断项的逐条处理

### 1) Mover.Update 帧内提升：删除（含租约/pending/重入要求）

初版 prefix/finalizer 的作用域改写已整体移除（不再有 `Mover_Update_HeroSpeed_Patch`、`HeroMoveBoost`、
`HeroMoveMath`、DL 优先级契约）。**结构上不存在**：嵌套/重入 Update 双重提升、半写后丢回执、
「下一帧又乘一次」、Pending 未归还仍提升、「关掉开关就不归还」等所有条目 1 指出的问题——
新设计没有任何逐帧临时值可被重入叠加。原条目 1 要求的等效保证现由认领路径承载（见第 2 节与测试表）：
有界租约（≤2 条，native pointer + GO InstanceID）、每字段所有权回执先于写入登记、写失败保留回执、
`RetryCleanup` 在任何开关门之前服务、Pending 未归还不提升、日志/容量有界、销毁/池复用身份核对。

### 2) 所有权证明：改为「不做作用域计算值归还」

接受「包络不是所有权证明」的反例（savedGoal=2/savedMove=1，提升 3/1.5 后原生 `SetSpeedToGoal(2)`
不改 goalMode，现值 2 落在 [1.2,3] 内会被误 ÷1.5）。**不再对 Update 重算出的 `_moveSpeed` 做任何推测归还**：
改为认领 actor 自有字段 `Archer.walkSpeed/runSpeed`（×1.5），归还是精确 CAS（现值 == 我们写入值才写回原值），
第三方写入一律原样保留。反例以回归测试保留：`ThirdPartyWritePreserved`（写入 2.0 → 归还后仍是 2.0，
无任何 ÷1.5/包络运算），并额外钉住"再次认领以现值为新基线（2.0→3.0）"。

### 3) 门控顺序 / 有限性

`AnyInstalled`/`TryGetInstalledHeroByGo`（连同 range 文件里的只读查询）已删除——没有逐帧身份门了。
`HeroArcherMovement.TryScale` 同时校验输入与**输出**：非有限、≤0、或 ×1.5 后溢出为 ±Inf 一律 fail-closed
（测试 `MathScale` 含 `3.4e38f` 溢出用例）。

### 4) 测试覆盖面（映射到新语义）

| review 要求 | 新测试 |
|---|---|
| 重入 | `AttachIsIdempotent`（100 次重复安装仍是 ×1.5 一次） |
| 半写 | `HalfWriteKeepsReceipts`（run 写失败 → walk 回执保留、重试不二次放大、修好后认领、归还两字段） |
| 归还抛错 + 下一帧 | `RestoreFailureKeepsPending`（pending 保留 → 期间拒绝提升 → RetryCleanup 下一帧完成归还） |
| 关闭后重试 | `ClearKeepsPending` + `RetryCleanup` 无安装态也服务（runtime 在 `Enabled` 门**之前**调用） |
| 回调值落在旧包络内 | `ThirdPartyWritePreserved`（见第 2 节） |
| 普通 actor 精确匹配 | `PerActorIsolation`、`IdentityGuards`（无关 actor 的认领/归还不互相波及） |
| mover/GO 身份 | 认领键 = native pointer + GO InstanceID（`IdentityGuards` 覆盖 GO id 不可读 fail-closed、销毁对象不盲写） |
| 共享 SO 变更 | 只写 actor 实例字段；不触碰任何 ScriptableObject/共享资产（代码面即此） |

## 需求与实现

### 射程 1.25 → 2（未受 review 影响，保持不变）

- `il2cpp/HeroArcherRange.cs` L37-40：`RangeFactor = 2f`；`SpeedFactor = 1.4142135623730951f (√2)`
  （克隆 SO 的两个 magnitude，`Range = force²/-gravity` 使弹道上限同步 ×2）。
- `il2cpp/HeroArcherRuntime.cs` L273-274：`RangeMultiplier = 2f`（注释要求与 `RangeFactor` 同步）。
- 头注释同步：`HeroArcherRange.cs` L8、`HeroArcherRuntime.cs` L6/L26、`HeroArcherRange.cs` L480。

### 移动 ×1.5：actor 字段认领（`il2cpp/HeroArcherMovement.cs`，无钩子）

- `TryScale`：有限性/正性/输出有限性；`SpeedMultiplier = 1.5f`。
- `Reconcile(archer)`：runtime 当选时与每帧 Tick 调用。死地随从 → `Restore`（让位给 DL 的同倍率 Mover 提升）；
  否则 `AttachCore` 幂等安装/维持。
- `AttachCore`：身份（pointer+GO id）→ 空闲槽出租（≤`MaxHeroes`=2，满则一次性日志）→ 逐字段
  `ClaimField`（回执先登记：`Original/Written/Owned`；字段 == Written → 幂等返回；第三方改过/上次写失败 →
  以现值为新基线；写失败保留回执）→ 首次成功记一条证据行。
- `Restore`/`RetryCleanup`/`Clear`/`PendingCleanupCount`/`ClaimedCount`：与 `HeroArcherRange` 同款语义
  （Pending 保留、重试、Clear 不丢回执、销毁对象放弃义务而不盲写）。
- `DeadlandsCovers`：逐条镜像 `Mover_Update_DeadlandsSpeed_Patch` 准入（`GameplayActive` → `ByMover` 注册 →
  非骑士 → `IsDeadlandsFollower` 且注册的 Archer 就是本人）；探针异常按「让位」处理。

**覆盖证据（2.1 参考源，非 2.4 已验证）**：2.1.0 `Archer.cs` 的地面移动指令全部取自两个 actor 字段——
L459/465（编队行走 `runSpeed`；`Formation.overrideMoveSpeed` 默认 -1f 且 2.1.0 全源码无赋值时才覆盖）、
L486/L502/L515/L542/L551/L557/L605/L608（`runSpeed`）、L592/L598（边界狩猎 `runSpeed`/`walkSpeed`）、
L2034（玩家控制路径，英雄资格已排除）。已知边界：若 2.4 某编队把 `overrideMoveSpeed` 设为正值，
该编队行走不受本提升覆盖（需实机/guard worker 侧核对）。

### runtime 接线（`il2cpp/HeroArcherRuntime.cs`）

- 头注释新增移动职责（L10-11）。
- `Tick()` L507-508：`HeroArcherMovement.RetryCleanup()` 与 range 的重试并排、**在 `Enabled` 门之前**。
- `Tick()` 英雄分支 L595-596：`HeroArcherMovement.Reconcile(archer)`。
- `PromoteHero()` L710：当选后 `Reconcile`（与每帧调用幂等）。
- `RestoreClaim()` L734：退役漏斗统一 `Restore`（死亡/role/side/world/换 life/关闭/池复用全覆盖）。
- `ClearInternal()` L680-681：`HeroArcherMovement.Clear()`。

## 改动文件与锚点

| 文件 | 锚点 |
|---|---|
| `il2cpp/HeroArcherMovement.cs`（486 行，无 Harmony） | `TryScale` / `Reconcile` / `Attach`/`AttachCore` / `Restore` / `RetryCleanup` / `ClaimedCount` / `Clear` / `DeadlandsCovers` / `Identity` / `Rent` / `ClaimField` / `Cleanup` / `ReleaseField` / 有界日志 |
| `il2cpp/HeroArcherRange.cs` | L8/L25 头注释；L37-40 常量；L480 扫描器注释；**删除**此前新增的只读安装态查询与头注释第 5 条 |
| `il2cpp/HeroArcherRuntime.cs` | L10-11 职责；L273-274 常量；L507-508/L595-596/L680-681/L710/L734 接线 |
| `tests/hero-movement/Tests.csproj` | 编译 production `HeroArcherMovement.cs` + 替身（不再编译 range） |
| `tests/hero-movement/Stubs.cs` | Unity/Actor/跨模块替身（含速度字段读/写抛错开关、DL 探针替身） |
| `tests/hero-movement/Program.cs` | 11 组用例（见下） |
| `tests/hero-archer/combat/HeroRangeTests.cs` | L9/L74 注释；L148-151 扫描器镜像 16f；L447-451 改用 `RangeFactor` |
| `tests/hero-archer/runtime/Program.cs` | 射程期望 10f→16f、18.75f→30f；新增 `HeroMovementSpeedClaim` 接线断言 |
| `tests/hero-archer/runtime/Tests.csproj` / `Stubs.cs` | 编译 production `HeroArcherMovement.cs`；替身补 `Mover.Pointer`、`Archer.runSpeed`、`LogWarning`、`PatchRoles_DeadlandsPowers` 边界 |

## 验证

### 本 session 实际完成的（静态证据）

- 完全重读并逐条比对：`Mover.Update` 速度数学（2.1 参考源）、`Archer._mover = Require.Component<Mover>(this)`
  （L157，同 GO）、`Archer.walkSpeed/runSpeed` 的全部 2.1.0 读点（见上）。
- 2.4 面：本 slice 引用的 interop 成员均为既有生产补丁已用或同机制成员——
  `Archer.walkSpeed` 读（`PatchWorld_DefenseSpacing.cs` L824/965、`PatchRoles_CrossbowDefense.cs` L224）、
  `Archer.runSpeed` 读（`PatchWorld_DefenseSpacing.cs` L915）、同类型字段写（Banker `walkSpeed/runSpeed`
  在 `PatchEconomy_Banker.cs` L399-401，以及英雄 `shootRange` 写）；`Archer._mover.Pointer` 见
  `SquadFollowGuard.cs`/DL 注册表。**"Archer.runSpeed 写"是本 slice 首次引入**，需 operator 用真实 2.4 interop
  重编确认 0W0E（见命令 4）。
- 反例复核：`TryRestore` 类逻辑已不存在（无推测归还）；`ClaimField` 幂等分支经重新推导修正
  （初稿会在重复安装时把已提升值再乘一次 → 已修 + `AttachIsIdempotent` 覆盖）。
- 影响面：`AnyInstalled|TryGetInstalledHeroByGo|HeroMoveBoost|Mover_Update_HeroSpeed_Patch|HeroMoveMath`
  全仓库 0 引用（grep 通过）；`tests/**` 中其余 `shootRange` 期望均为 crossbow/无关 slice。
- 替身面逐条核对：production `HeroArcherMovement.cs` 引用的每个符号在
  `tests/hero-movement/Stubs.cs` 与 `tests/hero-archer/runtime/Stubs.cs` 均有定义。

### 未执行（环境阻塞：bash + eval 均提示 "requires approval but no interactive UI available"）

请 operator 依次执行（预期输出已给出；全部只读验证，不部署、不启动游戏）：

1. `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-movement/Tests.csproj`
   → 逐条 `PASS`，末行 `RESULT: <N> passed, 0 failed`（N ≈ 97），退出码 0。
2. `C:/Users/ADMIN/dotnet8/dotnet.exe test --project tests/hero-archer/combat/Tests.csproj` → 全过（射程 ×2）。
3. `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-archer/runtime/Tests.csproj`
   → `ALL WIRING TESTS PASSED (...)`（含新增移动认领接线断言）。
4. `cd il2cpp && C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=` → **0 警告 0 错误**
   （本次首次写入 `Archer.walkSpeed/runSpeed`，以真实 interop 编译为准）。
5. 建议补跑：`tests/knight-powers/Regression.csproj`、`tests/hero-walk-scale/Tests.csproj`（只读回归）。

### 实机待验（install 候选后由 operator 执行；本 worker 不部署/不启动）

1. 已购英雄（F5 `HeroArcherEnabled` 开、单机）行走/奔跑 ≈1.5× 于同屏普通弓手；日志应出现**一条**
   `[HeroMove] hero movement ×1.5 active actor=... walk=... run=...`；关闭/死亡/换人/换 world/池复用后立即回到原速。
2. 夜晚随从/编队行走路径同样提速（若某编队带 `overrideMoveSpeed > 0`，该路径按原速——需实机观察是否存在）。
3. 射程 ×2：索敌/开火距离明显更远，命中/弹道无异常；关闭后回到原版。
4. 死地世界（DL 模块开、英雄同时是死地随从）：英雄仍为 ×1.5（由 DL 提供），绝不出现 ×2.25。
5. 原生动画：Animator `Speed=|velocity.x|` 同比变大，walk→run 阈值可能更早跨过（原生耦合，本 slice 不改动画门）。

## 残留风险与边界

- `Formation.overrideMoveSpeed > 0` 的编队行走路径不受覆盖（2.1 参考源默认 -1 且无赋值；2.4 未核）。
- `Archer.walkSpeed/runSpeed` 的**写**在本 slice 首次出现；读取与同机制写入都有既有先例，但需真实 interop 编译确认。
- Deadlands 让位镜像 DL 当前准入表达式；若 DL 改判定而不改本文件，可能出现「双方都让位」或理论叠加，
  镜像处已注明来源。
- 认领只在 runtime 退役漏斗 + Clear 兜底归还；若未来新增绕过 `RestoreClaim` 的退役路径，会留下 Pending
  （`PendingCleanupCount` 可观测、`RetryCleanup` 可服务），但不会污染其它 actor（身份核对 + CAS）。
- 移动提速仅单机生效（`HeroArcherRuntime.Enabled` 含 `HeroArcherNetwork.AllowsLocalHero`），与英雄其余 slice 一致。
- 文档同步（`harness-checklist.json`/`progress.md`/`patch-patterns.md`/`domain-model.md`）超出允许范围，由 operator 更新。

## 需 operator 处理的一项越界修改

`il2cpp/ModConfig.cs` L62-63 的 F5 文案仍是旧值：`"英雄射速1.5倍、射程1.25倍、..."`。
该文件不在允许写入面，**请把"射程1.25倍"改为"射程2倍"**（不改任何默认值）。
