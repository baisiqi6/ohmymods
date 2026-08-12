# ohmymods — 领域决策记录

## 关键决策（ADR 精简版）

### D1. 用 UMM + Harmony v1.2，不用 BepInEx
项目已装 UnityModManager（doorstop 指向 UnityModManager.dll）。Harmony v1.2 是 UMM 捆绑版本。
编译用 Framework csc.exe，C# 5 语法上限。

### D2. 商店系统接管狂战士/忍者生成，退役 hack
- 决策：`SpawnBerserkersInGreece`/`ReplaceWithBerserker`/忍者 hack 注释保留备查，
  `DelayedBerserkerSpawn` 只留猫生成。
- 理由：商店原生生成（购买→拾取→转化）是游戏自洽路径，hack 每局刷是临时的。
- 结果：无每局重复生成，存档/读档/联机行为更一致。

### D3. 单位缩放只做 y 轴
- 约束：`localScale.x` 是朝向符号（±1），且 `Mover.cs:405 velocity.x *= localScale.x`——
  动 x 会改变移动速度。
- 决策：缩放只体现在 y（视觉高度）。等比放大（宽高都变）需要父物体包装，会改变碰撞体
  world size 和渲染层级，风险高，暂不做。
- 效果：北境工匠 1.175 与希腊工匠 1.075 视觉齐平；北境居民 1.125 与希腊居民 1.0 齐平
  （模型原始高度不同，系数为对齐补偿，实测调参得出）。

### D4. 缩放守护用 ConditionalWeakTable
- 决策：`UnitScaleRegistry` 用弱引用 key，单位销毁自动清理；池复用 OnEnable 覆盖登记；
  转化（ReplaceBy 创建新对象）不影响旧登记。零泄漏、零手动清理。
- 替代方案（字典+OnDisable 清理）被否：Worker 类没有 OnDisable，手动清理易漏。

### D5. 北境工匠出生带盾（SetShieldEnabled）
- 根因：希腊 12/13 槽位被狂战士商店占用，盾牌商店不存在。
- 决策：`NpcShieldUser.SetShieldEnabled(true)` 直接装备，绕过购买流程。
- 依据：TryPickUpShield 内部就是调 SetShieldEnabled；shield 是 prefab 序列化引用，实例化即用。
- 时机：OnEnable（池回收时 OnDisable 自动卸盾，复用重新装备，正好互补）。

### D6. hook 点全部用 OnEnable
- 对象池游戏：Pool.Spawn 复用对象走 SetActive(true)，只有 OnEnable 每次出生都触发，
  Awake/Start 只在首次创建跑一次。
- 教训：v1 挂 Start 完全没触发（Worker/Peasant 类根本没有 Start 方法！），
  v2 挂 OnEnable 一次性设置被 Mover 覆盖，v3（当前）OnEnable 登记 + Mover postfix 每帧守护。

### D7. Holder 替换 vs Promote_Prefix 的分工
- `Patch_Holder`：覆盖所有 GetCharacterByTag 路径（初始/读档/招募/降级）。
- `Patch_Character.Promote_Prefix`：只处理"乞丐→Peasant"转化瞬间（希腊），替换为北境 WarriorPeasant。
- 两者互补不冲突，都是 biome=5 才生效。

### D8. 架构审查决策（2026-08-12，ArchReviewer P0/P1 修复）
- **速度倍率走 SetGoal 入口**：`Mover.Update` 每帧用 `_goalSpeed` Lerp 重算 `_moveSpeed`
  （Mover.cs:190），在 Update postfix 写 `_moveSpeed` 会被下一帧 Lerp 覆盖——速度倍率从不生效
  （P0）。改为 patch 所有设置 `_goalSpeed` 的方法（`SetGoal` x2 / `SetGoalNoHaglet` / `SetGoalSpeed`），
  prefix 里把 `speed` 参数乘倍率；只对 Player 生效（ConditionalWeakTable 缓存 Mover→Player 身份），
  幂等（每次设置目标只乘一次，无累积放大）。上限 15f 封顶。
- **地图倍率幂等**：原实现每次乘倍率——`Kingdom.Init` + 每次岛屿 `OnLevelLoaded` 都触发，
  `minKingdomExtents` 从不重置（源码仅字段初始化 =4f）→ 逐岛指数放大（4→8→16→32）（P0）。
  改为首次调用记录原生基准值 `_vanillaMinExtents`，之后恒为 `base * multiplier`；当前值
  ≤ 基准 + 0.01 时重新记录（防重置后基准错位）。
- **银行家补员删除**："补员到 5 个"不可实现——`Banker.Awake` 硬编码
  `NetworkPostbox.RegisterObject(903, Dynamic)`（Banker.cs:54），NetID 903 网络层唯一，
  克隆走 Awake 必 duplicate key 崩溃；不走 Awake 则无 FSM 无法工作。且原实现与
  `Awake_Prefix` 去重自相矛盾（每 120 帧 Instantiate/Destroy 循环 + 日志刷屏）。
  共享银行增强（ShouldHide false / coinScanRange 300 / 瞬移收币）保留，单银行家即可。
- **Enabled 契约统一（P1）**：所有 patch 入口统一检查 `Main.Enabled` 再执行
  （Patch_PoolManager / Patch_SidedShop / Patch_WorkerScale 补齐缺失检查），关闭 mod 后零副作用。

## 已知风险/开放问题

- R1：存档会序列化 localScale.y（Serializer.cs:1935 写完整 localScale），y=1.175 会入档。
  读档恢复自洽（还是 1.175），但存档文件携带 mod 缩放值；卸载 mod 后旧档单位尺寸可能不符预期。
- R2：WarriorPeasant→Berserker 转化瞬间继承 y=1.2，Berserker 无登记，Mover 会覆盖回 1.0——
  狂战士最终是标准大小（1.0），这是当前意图，若要改需给 Berserker 加登记。
- R3（已关闭 2026-08-12）：`Patch_Mover.cs` 已核实为玩家移动速度倍率（Main.speedMultiplier），
  并经 D8 修复为 SetGoal 入口（P0），保留。见 checklist core-015 / maint-001。
- R4（已关闭 2026-08-12）：Patch_Probe.cs 已删除（checklist maint-002 done），探测代码不进入发布版。
