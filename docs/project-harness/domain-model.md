# ohmymods — 领域决策记录

### D9. 钱袋扩容 = 数字容量 + 视觉上限，无物理容器（2026-08-12）
- 用户初始设想"钱袋有长宽明确的容器碰撞空间，调大空间+放大 UI = 扩容"——源码核实不成立：
  容量是 `Wallet.TotalCapacity` 数字（全库零写入），拾取靠金币×玩家物理碰撞重叠 + 点击
  OverlapCircle，钱袋（CurrencyBag）是 HUD 视觉对象（挂 InterfaceCamera），无 Collider。
- 实现映射：容量 1000→2000（ChangeCurrencyBag postfix）、视觉堆叠上限 300→600
  （BagCurrency.Reset prefix）、UI 放大 1.3x（CurrencyBag.Awake postfix）。
- 遗留：拾取范围（吸金半径）未动——若要"吸金更猛"再改金币 collider/maxCoinPickupDistance。

### D10. 商店左右方向使用显式值，并规范化旧队列（2026-08-13）
- IL2CPP 的 `QueueNewShopForPlacement` 可选 `Nullable<Side>` 包装不能省略，也不依赖 helper 往返；
  Ninja/ShieldShop 左右四类均显式构造 Left/Right。
- 旧存档可能保留空值或错误方向；按 `shopType` 直接覆盖 `shopSide`，禁止先读空 nullable getter。
- 只修改尚在队列中的条目，不移动已放置商店，不绕过 `CanShopFit`、科技年龄、地形或排斥区。
- 不在 `ShopPlanner.Start` postfix 强制触发规划；原生 `OnLevelLoaded`/core routine 负责消费旧队列。

### D11. IL2CPP 设置面板优先使用默认 IMGUI skin（2026-08-15）
- 游戏静态 `Zpix` 不满足新版 IMGUI/TextCore 动态字体转换契约；直接注入会在每次 Repaint
  产生两类字体错误和完整堆栈，旧日志中各出现 17,482 次。
- 决策：不再 `Resources.LoadAll<Font>("")`，不注入 `Zpix`，固定复用 Unity 默认 `GUI.skin`。
- 权衡：中文 glyph 覆盖由游戏默认 IMGUI 字体决定，可能显示方框；英文配置名、数值、控件和
  F5/Ctrl+F10 保持可操作。完整中文字体作为未来独立资源任务，不以持续报错为代价。
- 日志等级只表达诊断重要性：钱包容量保障与旧商店 side 幂等覆写继续每次执行，但重复成功信息
  降为 Debug；不得通过跳过业务写入来“消除日志”。

### D12. 宽灌木使用三个独立忍者伏击锚点（2026-08-15）
- 一个原生 `HidingSpot` 只有一个 `_hider`；直接允许多人共享会让多个 Ninja 重叠、占用和禁用通知
  失去一一对应关系，因此不改写 `IsOccupied/SetHider` 契约。
- 每个 Greece 实际 thicket 创建 Left/Center/Right 三个命名子对象，local x 为
  `-1.1/0/+1.1`，各挂一个原生 `HidingSpot`。Kingdom 仍按各自 world x 排序，Ninja 仍逐槽执行
  未占用与城墙外过滤。
- 父 thicket 禁用时三个组件各自 `OnDisable` 注销并通知占用者；池复用时只对不在 sided list 的
  锚点清旧 hider 并重新登记，已登记的当前 occupant 不得被误清。
- 兼容前一候选在 thicket 根添加的单槽：根槽可作为中心只补左右；若三命名槽已存在，则只禁用并
  注销旧根槽，严格保持总数为 3。

### D13. 狂战士六次序列以公开 Promote 结果为提交点（2026-08-15）
- 私有 `Worker.TryPickupBerserkerTool` 的 Harmony hook 实机未命中：用户招募大量狂战士，普通与
  Leader pool 均注册成功，但 `slot 1..6` 日志为 0。该 hook 从序列设计中移除。
- 稳定入口改为已由 Hammer 交替实机证明命中的
  `Character.Promote(DroppableTool,IUnitController)`；只接受 world-authority、active Worker、active
  且尚未 pickedUp 的普通 BerserkerTool。
- Worker 原生路径在 Promote 正常返回后没有其他失败分支，因此 Postfix 的角色 tag + effective
  prefab identity 匹配就是成功提交点。异常、不匹配或 Leader pool 缺失均恢复 Holder 映射且不推进。
- 与锤子交替的门禁和映射键互斥：Hammer 为 Peasant + pickedUp + `Hammer` 并改 `Worker`；本序列为
  Worker + !pickedUp + `BerserkerTool` 并仅第六次临时改 `Berserker`。

### D14. GitHub 只保存可发布工程，不保存反编译参考源码（2026-08-15）
- 私有远端为 `https://github.com/baisiqi6/ohmymods`；用户授权每次项目改动完成并验证后执行
  commit + push。未完成或仍在游戏运行中的中间状态不冒充已验收发布版。
- `game-source/`、根 `Assembly-CSharp/` 与 `ktc-il.txt` 属于本机逻辑参考资料，只在开发机保留，
  加入 `.gitignore`，且不得出现在 GitHub 可达历史中。
- 发布 ZIP 可以保留为版本产物；若后续持续更新导致 Git 历史膨胀，优先迁移到 GitHub Releases，
  不重新把反编译资料或运行日志塞进仓库。

### D15. 忍者伏击点统一登记，不按载体写优先级（2026-08-15）
- Greece 的伏击容量为：每个成熟宽灌木 3 槽、每棵 `PayableTree` 1 槽、每个 `BeggarCamp` 5 槽；
  每个槽仍是一个原生单占用 `HidingSpot`，不允许多人共享同一组件。
- 三类槽全部登记到 `Kingdom` 同一侧列表。原生注册会按 world x 朝城墙方向排序，`Ninja.GetHidingSpot`
  再选择第一个墙外且未占用的槽，因此自然实现“离墙最近优先”，不添加灌木/树/帐篷类型权重。
- 树用 `PayableTree.OnEnable` 精确补点；帐篷复用已实机命中的 `BeggarCamp.Awake`；灌木继续使用
  `World.AddThicket`。父物体禁用、砍伐或摧毁时，子槽通过原生 `OnDisable` 注销并通知占用者。
- 只在 Greece world-authority 创建这些本地 AI 锚点。客户端不运行忍者选点，不需要同步空锚点对象。


## 关键决策（ADR 精简版）

### D1. 架构边界：IL2CPP 单主线，Mono 冻结
- **IL2CPP 2.4.0 发布线**：BepInEx 6 + Il2CppInterop + HarmonyX，工程位于 `il2cpp/`。
- **Mono 2.1.0 历史/自用线**：UMM + Harmony v1.2，根目录 `Main.cs + Patch_*.cs`；默认不维护、不作为发布门禁。
- 两端共享业务意图但不共享二进制/API 假设；2.1.0 反编译仅作逻辑说明书，2.4.0 签名以 interop 壳为准。

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
- 时机：对象池实例完成网络注册后，由 world authority 幂等装备。禁止在 OnEnable 直接发送 RPC；
  该时点 `parentHeaderRef` 尚未建立，会造成 NRE 与半提交状态。

### D6. 本地生命周期用 OnEnable，网络动作等待注册完成
- 对象池游戏：Pool.Spawn 复用对象走 SetActive(true)，只有 OnEnable 每次出生都触发，
  Awake/Start 只在首次创建跑一次。
- OnEnable 适用于缩放登记等纯本地初始化；涉及 CRPC 的动作必须延迟到 NetworkPostbox 注册完成后。
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
