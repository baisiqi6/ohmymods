# 火铳铺购买卡顿 · perf worker 结果（2026-09-17）

范围：只改 `il2cpp/MusketeerShop.cs` + 新增 `il2cpp/MusketeerShopAssets.cs`，新增 `tests/musketeer-shop-perf/**`。
未动 MusketeerIdentity / 素材 PNG / 配置 / 用户存档 / 未构建未安装未启动游戏。

## 0. 结论（先给判定）

- **已移除的确定性负担（代码级实锤）**：`TryCreateGun` 原来每次购买都调用 `FindNativeBow()` →
  `Resources.LoadAll<DroppableTool>("")`（把 Resources 里全部工具资产同步加载/遍历），再对每个候选做
  `GetPoolByPrefabName`。该调用现在**完全不存在**（`FindNativeBow` 已删除），支付路径不再有任何
  `Resources.*` 枚举或资产加载；发现只发生在 `Tick` 的有界重试里。
- **卡顿归因分级**：
  - *proven（静态）*：安装版（50aa3037）的每次成功购买都执行上述 LoadAll —— 计划书已确认该热路径，
    本文件改动前代码里也确实没有其他候选；receipt 记录了安装时 `MusketeerShop.cs` SHA `3f7b9850…`，
    但本 worker 无法在本机重算该 SHA，安装版与改动前源码逐字节一致以 receipt + Operator 确认为准。
  - *hypothesis（未逐阶段实测）*：日志 `[DefensePerf] … maxFrame=333.3ms t=19.8h` 紧跟在 t=149.313 的
    `[MusketeerShop] purchase completed` 之后，与该 LoadAll 的量级吻合；但 DefensePerf 是**窗口统计**，
    不是购买阶段计时，因此**不能**据此宣称 333ms 已被证明就是 LoadAll。旧代码没有任何购买逐阶段耗时。
  - *新证据通道*：本次改后每笔购买输出有界 `purchase stage-ms total=… rack=… spawn=… identity=…`，
    安装后即可用真实数字定位剩余耗时（尤其 identity 的附加档磁盘读）。
- **另一个已处理的每次"选中/购买"负担**：旧 `ObserveVisualSelection` 每次选中变化会 dump 14 个店铺
  renderer + 18 个人物 renderer（日志中可见 `visual-selection=… snapshot=…` 共 4 次 × 约 32 行），
  现已默认关闭（`VisualSelectionDiagnostics = false`），保留其他直接错误日志。

## 0.1 review 修复（2026-09-17 第二轮，perf-review-fix）

Operator 复核发现并确认了三个阻断，全部已修：

1. **真实接线缺陷（致命，已修）**：`TryDiscoverBow` 捕获的是 `tool.gameObject.Pointer`（GO 指针），
   而 `BowUsable` 校验的是 `prefab.Pointer`（`DroppableTool` **组件**包装器指针）——两者是不同 native
   指针，`MatchesPrefab` 永远 false → **任何真实购买都过不了付款门**。修法：
   - 新增唯一身份来源 `TryPrefabIdentity(prefab)`（一律取 `prefab.gameObject.Pointer` +
     `gameObject.GetInstanceID()`），捕获（`TryCaptureBow`）与校验（`BowUsable`）都只经它；
   - `TryCaptureBow` 捕获后立刻调用**支付路径同款校验** `BowUsable(in context)` 做自检；失败则丢弃捕获、
     `DeferRetry` 保持 1.5s 限速（绝不逐帧重扫），并给出原因 `弓具身份自检未通过`。
2. **actual-interop 缺引用（已修）**：`MusketeerShopInteropCheck.csproj` 补 `UnityEngine.Physics2DModule`
   （`gun._rigidbody`/Rigidbody2D 成员需要）。
3. **无原生弓商店时不再永久停售（已修）**：新增有界回退 `TryDiscoverBowFallback()`：
   `GetPoolByPrefabName("ToolBow")` → `pool.prefab.GetComponent<DroppableTool>()` → **同一套
   `ValidateBowCandidate`**（Persistent 契约、非场上实例、同名池且 `pool.prefab.Pointer` 严格相等）。
   `ToolBow` 是已核实的**原生资产名与原生池键**（resources.assets 唯一弓具 `ToolBow` id 19747、
   原生池 `ToolBow (Pool)` id 36589），不是猜测/自定义路径；无 `Resources.LoadAll`、无资产枚举。
   付款路径依旧只读缓存，池不可用照样**先拦付款**。

用户口径确认：卡顿发生在**最后一枚币支付、枪具出现的时刻**（不是居民拾取）——`purchase stage-ms`
测量窗口正是 `OnPay → TryCreateGun`（含 `spawn`/`identity`），与触发点一致；阶段计时保持不变。

## 1. 改了什么（精确清单）

### `il2cpp/MusketeerShopAssets.cs`（新增，纯逻辑，无 Unity 依赖）

`MusketeerBowCachePolicy`：缓存状态策略。
- `Context{WorldReady, PoolManager, World, Biome, BiomeIndex}`：捕获时记录的运行世界身份。
- `Capture(prefabPointer, prefabGoId, name, poolPointer, in context)`：**fail-closed**，任一身份缺失就不绑定。
- `TryUse(in context, resolvePool)`：只有 WorldReady 且 PoolManager/GameLayer/biome/biomeIndex 与捕获完全一致，
  且 `resolvePool(name)` 仍解析到**捕获时的同一个 Pool 实例**才可用；任何不一致 = 不可用。
- `TryBeginAttempt(now)`：未绑定时每 `1.5s` 至多一次发现尝试（`DiscoveryAttempts` 计数供测试断言）。
- `DeferRetry(now, seconds)`：只向前推下一次允许时间、绝不回退；自检失败路径使用（不计为发现尝试）。
- `Invalidate()`：丢弃捕获；重试时钟保留，连续失败仍受窗口限速（不会退化成逐帧全场扫描）。
- `MatchesPrefab(pointer, goId)`：包装器仍必须是同一 native prefab（指针 + Unity instanceID，防地址复用）。

### `il2cpp/MusketeerShop.cs`（唯一既有文件改动）

1. **删除 `FindNativeBow()`（含 `Resources.LoadAll`）**。支付路径不再发现/加载资产。
2. **新增 Tick-only 缓存维护**：
   - `MaintainBowCache()`（在 `Outcome.Active` 每帧调用）：先只读验证，失效则丢弃；`TryBeginAttempt` 限速；
     失败日志 60s 一次；成功一次一行。
   - `TryDiscoverBow()`：优先有界（`PayableScanLimit=4096`）遍历当前世界 `payables.AllPayables`，接受
     原生 `PayableShop.itemPrefab`（`CreateItem()` 同一引用）且为当前 world 子物体者；没有弓商店时回退到
     `TryDiscoverBowFallback()`——按**已核实原生池键** `ToolBow` 取 `pool.prefab` 的 `DroppableTool`。
     两条路径共用同一套校验与捕获。
   - `ValidateBowCandidate()`（两条路径共用）：`tag=="Bow"`；`Persistent.persistObject/path` 完整；
     `MusketeerAccess.InWorld(tool.gameObject)` 为真 → **拒绝"场上实例当 prefab"**；
     `GetPoolByPrefabName(name)` 必须存在且 `pool.prefab.Pointer` 与该 prefab 的 **GameObject 指针相同**。
   - `TryPrefabIdentity()`：**唯一身份来源**（GameObject 指针 + instanceID），`TryCaptureBow`/`BowUsable` 共用。
   - `TryCaptureBow()`：捕获后立即以支付路径同款 `BowUsable` 自检；失败丢弃捕获并 `DeferRetry`（保持限速）。
   - `ResolveBowPoolPointer()`：静态只读委托（`BowPoolResolver`，**无逐帧分配**）；池重建后同名解析到
     新实例（指针不同）→ 捕获失效。
   - `ReadBowContext()`：读 `Managers.pools` / `world.gameLayer` / `BiomeHolder.Inst+BiomeIndex`，
     读不到 = fail-closed（缓存不可用）。
3. **付款门先于收币**：`CanPurchase()` 末尾新增 `return BowReady();`（在 `_payable.forceBlockPayment`、
   owner `CanPay` 两条原生门上都生效）。缓存不可用时原生交易根本不会被发起/完成，不会出现"先收 4 币再退款"。
4. **`TryCreateGun()` 重写**：使用缓存 `_bowPrefab`（不再发现）；保持原样的 `rack3 / 单次 OnPay 收据 /
   身份登记 / 失败退款`语义；失败清理逻辑不变。
5. **有界阶段计时**（`Stopwatch`）：
   - 阶段：`rack`（CopyGuns/槽位计算）、`spawn`（Pool.Spawn + 属性设置）、`identity`（`TryRegisterPaidGun`）、`total`。
   - 输出策略：前 12 笔始终记录，之后仅当 `total ≥ 8ms` 记录；失败也记录（带原因）。无逐帧日志。
6. **关闭旧层级诊断**：`VisualSelectionDiagnostics = false`（静态只读开关；`ObserveVisualSelection`/
   `DescribeRenderers` 保留但默认不调用），其他直接错误日志（stage/clear/error）保留。
7. `_status`：缓存未就绪时显示 `火铳铺：等待原生弓具（未收费）`；就绪时仍显示 identity 状态。

**明确未动**：`MusketeerIdentity`（含 `TryRegisterPaidGun` 内的附加档 freshness/`ReadArchive` 检查）、
`MusketeerShopPayment` 收据语义、`HeroShopRetention` 暂停/保留策略、PNG 与 512×80/4×128×80 帧规格。

## 2. 购买路径仍存在的其他分配/耗时嫌疑（本轮只测量，不重写）

| 位置 | 性质 | 处置 |
|---|---|---|
| `MusketeerIdentity.TryRegisterPaidGun` → `MusketeerPersistence.ReadArchive()` | 每次登记一次**磁盘读 + JSON 反序列化**（identity 模块所有，禁止移除） | 归入 `identity` 阶段；由新日志给出真实毫秒 |
| `Pool.Spawn` 首次 `Instantiate`（`FastClone`） | 首次池实例化；之后走缓存 | 归入 `spawn` 阶段 |
| `MusketeerIdentity.CopyGuns` + 槽位遍历 | 小表线性遍历（≤ 从业数） | 归入 `rack` 阶段 |
| `CanPurchase`（每帧）内的 `RackCount`/`Observe` | 既有行为，未扩大 | 未改 |
| 旧 `visual-selection` 层级 dump（32 行/次） | 选中即触发（购买前窗口） | 已默认关闭 |

## 3. 证据

### 3.1 冻结日志（`docs/project-harness/tasks/musketeer-live-fixes-20260917/LogOutput.log`）

- 4 笔购买：`t=112.316`、`t=116.510`、`t=118.893`、`t=149.313` 各一行 `[MusketeerShop] purchase completed`；
  期间 `[Musketeer] stock-gun-registered` / `gun->unit`、`[Musketeer] saved:records=4:bound=4`。
- 购买前后存在 `[MusketeerShop] visual-selection=… snapshot=…` + 约 32 行 `visual shop/player1 name=…`
  （第 3、4 次快照；共上限 4 次）。
- 紧接 t=149.313 的购买之后：`[DefensePerf] arrows=0 avgFrame=20.8ms maxFrame=333.3ms t=19.8h`；
  另见 `dawn: avgFrame=26.0ms maxFrame=268.7ms t=6.0h`。**窗口统计 ≠ 购买阶段耗时**（见 §0）。
- 冻结日志不支持逐阶段归因：旧代码没有购买阶段计时行。

### 3.2 原生资产/池（`docs/project-harness/tasks/musketeer-20260916/native-tool-shops.json`，UnityPy dump）

- `resources.assets` 里 Bow 工具**只有** `ToolBow`（id 19747）与其原生池 prefab `ToolBow (Pool)`（id 36589）；
  商店为按世界区分的 `ShopBow / _greece / _deadlands / _norselands / _bamboo / _springEdict / _Party`。
- 因此 `ShopBow_greece.itemPrefab` 与弓池 prefab 指向同一 `ToolBow` 资产，`pool.prefab.Pointer` 严格相等成立；
  name→pool 规则与原生 `IslandSaveData.TryCreateOrFind`（`Resources.Load` → `GetPoolByPrefabName(name)`）
  一致（scout.md §1.2 记录）。

### 3.3 代码事实（2.1.0 只读参考 + 2.4 interop 既有核对）

- `PoolManager.GetPoolByPrefabName(name)` → `cachedNamePoolPairs[pool.prefab.name]`；`InitPools` 每次清空并
  重新 `Instantiate` 池实例，`Pool.Init` 重新注册 → **池重建后指针变化 = 旧捕获必须失效**（policy 已按此校验）。
- `PayableShop.CreateItem` = `Pool.Spawn(this.itemPrefab, …)`；`PayableShop.itemPrefab` 的 2.4 interop 存在性
  已有记录（`PatchEconomy_Shops.cs` 头注释）。
- `Pool.SpawnGO` 生成前做 `BiomeData.GetAssetSwapForThis`；本方案传入的就是原生店的 `itemPrefab`，
  与原生购买走同一条池/换皮路径。

## 4. 测试（新增 `tests/musketeer-shop-perf/`）

### 4.1 `PerfTests.csproj` + `Program.cs`（生产 `MusketeerShopAssets.cs` 原样编译，纯策略断言，可运行）

> 本 worker 无 shell，**未运行**；以下命令由 Operator 执行（任何 `FAIL:` 都会异常退出）。

覆盖（共 30+ 断言，输出 `PASS n musketeer shop-perf policy assertions`）：
1. 未绑定不可服务、读取不触发发现；gate 组合 `HasBinding && TryUse` 拒绝付款；
2. 有界重试：窗口内拒绝、边界放行、计数正确；29.9s/0.1s 步进只允许 20 次（1/1.5s）；
3. 前置条件出现后捕获可用；500 次复用购买**不增加 DiscoveryAttempts**（支付路径零发现）；
4. 世界/PoolManager/biome/biomeIndex/上下文不可读任一变化 → 捕获不可用（**不会用陈旧 prefab**）；
5. 池重建（同名→新池）或池消失 → 不可用；原池恢复后才重新匹配；
6. 显式失效后可立即重新发现，但连续失败仍限速；重新捕获恢复资格（**安全重试**）；
7. `Capture` fail-closed（0 指针/空名/0 池/上下文未就绪都不绑定）→ gate 继续拒绝（**不可用先拒付**）；
8. 1000 次冷上下文读取 = 零发现（**无逐帧全场扫描**）；`MatchesPrefab` 指针/instanceID 校验；
9. 身份来源不变式：GO 指针捕获↔GO 指针校验通过；组件指针↔GO 指针**混用必被拒**（本轮真实缺陷回归）；
10. `DeferRetry` 只前推窗口、不回退、不计为发现尝试（自检失败不会变成逐帧重扫）。

运行（Operator）：
```
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/musketeer-shop-perf/PerfTests.csproj
```
预期末行 `PASS <n> musketeer shop-perf policy assertions`（n 由脚本自计并打印）；任何 `FAIL:` 都会以异常退出。

### 4.2 `actual-interop/MusketeerShopInteropCheck.csproj` + `Stubs.cs`（真实 2.4 API 形状硬核对，只编译不运行）

把 `MusketeerShop.cs` + `MusketeerShopAssets.cs` + `MusketeerShopRules.cs` + `HeroShop.cs`（`HERO_SHOP_CORE_ONLY`）
对着 `E:\…\Build.22992091\BepInEx\interop` 编译（已按 Operator 反馈补齐 `UnityEngine.Physics2DModule`
引用——`gun._rigidbody`/Rigidbody2D 成员需要）；核对本轮新增/使用的原生成员：
`PayableShop.itemPrefab`、`Pool.prefab`、`Pool.Pointer`、`PoolManager.GetPoolByPrefabName`、
`PayableManager.AllPayables`、`Transform.IsChildOf`、`Droppable.TryCast<DroppableTool>()`、
`GameObject.tag/name/GetInstanceID()`、`BiomeHolder.Inst/BiomeIndex`、`Persistent.persistObject/path`。
协作 API（MusketeerIdentity/MusketeerAccess/ModConfig/KingdomEnhancedPlugin）用同形替身。
**未运行过**（worker 无 shell）：需要 Operator 构建：
```
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/musketeer-shop-perf/actual-interop/MusketeerShopInteropCheck.csproj -c Debug
```

## 5. 未完成的运行时证据（如实列出）

1. **实机阶段耗时**：新 `purchase stage-ms` 行只有安装并游玩后才能给出；当前无法证明 333ms 卡顿的构成
   （LoadAll vs identity 磁盘读 vs Pool 首实例化）。
2. **池重建/换世界的运行时行为**：策略逻辑与静态绑定已测，但"真实池重建后旧捕获被拒且重新发现成功"
   需要实机（换岛/读档）验证。
3. **无原生弓商店的世界**：已加回退（原生 `ToolBow` 池 prefab，池键已核实，同一套校验）；但"回退路径在
   实机里真正被走到并成功出货"仍待验证（当前存档有弓商店，优先路径先命中）。
4. **identity 附加档每次购买的磁盘耗时**：由 `identity` 阶段暴露，但优化属 identity 模块职责，本轮未动。
5. `identity review` 缺口、跨岛/联机等待项与本轮无关，仍按原任务记录。

## 6. 边界与失败模式（明示）

- **缓存不可用 = 禁止付款**（先拒付、后不退款路径），不会重复收币；`CanPlayerPurchase`/`CanPurchase`/
  `TryCreateGun` 三层各自独立复核缓存。
- **严格池绑定**：`pool.prefab.Pointer != itemPrefab.Pointer` 或 name 无池 → 视为不可用（宁可不卖也不
  用错池/旧池生成）。已有资产 dump 支持该等式在当前游戏中成立。
- **无 Resources/资产枚举回退**：回退只走**已注册的原生 `ToolBow` 池**（名字即原生池键，取
  `pool.prefab` 资产，同一套 `ValidateBowCandidate`）；连该池都不存在时功能暂停出售（状态栏可读原因），
  仍按任务书"cache unavailable disables purchase"fail-closed。
- **PNG/512×80 四帧规格未动**；`MusketeerShop.png` 美术改动由 Operator 负责。
- 未提交/未发布；E 盘安装与游戏启动由 Operator 决定。
