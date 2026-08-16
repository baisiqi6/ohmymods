# ohmymods — 领域决策记录

### D18. FleetBoat 所有权只认一种生命周期表示（2026-08-15）
- 奥林匹斯四艘小船的永久所有权下限来自四个实际发船的交付任务：`GodIslandAthena`、
  `GodIslandArtemis`、`GodIslandHephaestus`、`GodIslandHermes`；不重置任务，也不重放 IdolCollector 奖励。
- 原生 `PopulateCarryForward` 明确使用 `active.Count > 0 ? active.Count : standby`，所以 active、standby、
  carryForward 是同一所有权在不同场景阶段的替代表示，禁止求和。`ApplyToScene` Prefix 捕获 present carry
  只用于计算 desired，Postfix 在原生完成并清 carry 后只认 active 或 standby 其中一个 materialized 来源。
- 恢复严格为 `desired=max(completedQuests,capturedCarry)`（上限 4）与 materialized 的缺口；不删除多余船。
  standby 非零时只提升 standby；active 非零时只补 active，部分生成失败后留到下次 Apply 重试；两者均零且
  riverless/生成前置不完整时才写 standby。首个生成失败、最终 active 仍为零时整批回退 standby。
- 只在非 challenge 的 Greece campaign/scene 且 world-authority 执行；生成必须复用当前 biome
  `fleetBoatPrefab` 已存在的原生同步池。不新增 RPC、syncID、sidecar，不直接改写压缩存档。

### D19. Cerberus 四队保留希腊/北境各自行为（2026-08-16）
- Cerberus 原生资源为1名 `WarriorGhostLeaderGreece` +4名 `WarriorGhostGreece`，且两个生成协程共享
  单一队长引用。直接把数量改为4/16会破坏四支小队的归属关系，因此保留原生首队，补三支各自持有局部
  leader引用的完整1+4编队。
- 北境神器亡灵并非希腊亡灵的纯换皮：北境基类跟随玩家并按固定时长消失，希腊子类主动向外作战并按
  离玩家距离回收。用户明确选择保留差异；两支北境队完整克隆唯一精确北境行为prefab，并由坐骑写入
  2.4 HelsHead资源的原生30秒Duration；另外两支继续使用希腊具体行为组件。
- 北境行为克隆使用固定同步池30130/30131，主客双方在池初始化阶段同构注册；通用动态池分配器显式跳过
  此区间。原生北境弓箭手资源的syncID与2.4 FleetBoat资源存在冲突，因此禁止直接跨世界注册该原生池。
- 补充单位仍登记到原 `SummonGhostSteedAbility` 的active列表并调用各自原生编队、Summoner和死亡倒计时；
  不扩展能力RPC/序列化，也不接管原生首队、冷却、雾效或回收入口。

### D20. 专精塔重建先回六级普通塔，再走原版隐士专精（2026-08-16）

- 完成态Ballista、Fire、OilFire、Knight/Berserker等没有原生下一阶`PayableUpgrade`，不能只扩展
  `nextPrefab`图；直接special→special还会绕过next NetID、乘客、Persistent、驻员与库存清理。
- 决策采用两步事务：安全空闲的专精源先按运行时原生六级塔profile付费恢复为当前biome六级普通塔；
  之后玩家携目标隐士完成第二次原生专精。重建不退款、不消耗隐士、不重复记专精统计。
- 首版只开放Ballista，因为其唯一额外库存是可验证回收的池化bolt。Fire/OilFire/TowerKnight及商店塔
  均等待各自teardown证据，不以“看起来没人”代替库存、隐藏GuardSlot或同级PayableShield审计。
- 注入的原生PayableUpgrade必须在主客pool/CRPC注册前确定性加入；总开关关闭仍保留布局、只禁交互。
  这是网络确定性优先于“Disabled时物理移除组件”的例外，兼容代价是增强存档可能多一份原生
  PayableUpgradeData。
- Ballista当前与排队工匠不作为付款阻断；旧塔由原生Pay销毁后，Worker在下一次工作循环通过Unity-null
  工作对象走原生Reset/LostWork。不得直接清`_currentActors`或调用native-private Worker重置入口。
- bolt准备必须发生在离线最终`CanPay=true`之后、`TransactionComplete`之前；失败令CanPay为false并由原生
  取消/退币，成功token只允许同玩家、同对象、同world/scene、同帧Pay消费。在线在没有批准前对称事务前
  整体fail closed，禁止在已发送Pay RPC后做单端可失败清理。

### D21. 城墙旗帜按原生 Side 动态展开 FleetBoat 编队（2026-08-16）

- 小船的所有权、数量、迁侧、战斗与返航继续完全由原生 `FleetBoat` 管理；举旗补丁只读取当时的
  `FleetBoat.Side`，把该侧0～4艘当前可加入的小船登记进玩家原生 `Formation`，不写Side、FSM或存档。
- 原PlayerFormation只有一个FleetBoat槽且该类型间距为0。动态编队必须成对重建等长
  `unitTypes/units`；两艘以上只在克隆的`UnitSpacing`中把FleetBoat绝对设为1，避免船体叠放。
- 原生FleetBoat后续招募不可靠地按旗帜侧过滤，因此本轮未占用或中途释放的预留船槽必须立即改为
  Gap；`UnregisterUnit`是即时主路径，0.5秒协调器仅作小范围兜底，不能替代native Hook实机证明。
- 阵列profile按Player Formation实例隔离；活动期间不热缩，原生OnDisable完成逐船离队且units全空后
  才恢复原始一船槽。只有world-authority扩展和招募，客户端继续依赖原生状态与PositionSync。

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
  `-0.55/0/+0.55`，各挂一个原生 `HidingSpot`。最初候选的 `±1.1` 经用户实测视觉过宽，左右忍者
  接近灌木边缘，因此间距减半。Kingdom 仍按各自 world x 排序，Ninja 仍逐槽执行
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

### D16. 一个主银行家 + 四名轻量银行助手（2026-08-15）
- 状态：最新候选已构建并获静态 reviewer APPROVED；因独立副本当前正在运行，尚未部署本轮 DLL、实机或进入正式 zip。
- 2.4.0 只有一个完整 `Banker` prefab，并通过欧洲、幕府、Dead Lands、北境、希腊五套动画控制器
  形成世界外观。希腊外观保留给唯一主银行家；另外四套只用于空对象构建的轻量助手，禁止携带
  `Banker`、`Wallet`、`Persistent` 或固定 NetID 903。
- 主银行家保留原生状态机，使用增强移动速度、安全期全天工作与 1 秒扫描；管辖区固定为左右从城堡向外数
  第二道墙之间。两侧第二墙未同时就绪时对称回退第一墙，再回退有效外墙，任何阶段都不混用左右不同墙层。
  认领入口拒绝管辖区外玩家金币；助手中央调度器处理该区外落地满 3 秒的玩家普通金币，因此外层领地内
  的金币也归助手。四名助手共享一次 2 Hz 预分配扫描，不各自全场搜索。
- 每枚币只允许一个助手认领，同一批次只由一名助手连续收取。首枚或距离超过 6 单位的目标会近距传送；
  6 单位内的后续金币直接跑去，不重复全位置传送。物理币完成权威拾取标记后立即记入唯一主账本，回城
  只表示容量/交付节奏，不再次入账。空闲助手在墙内短程巡逻并停顿；Dead Lands 与北境助手均只把
  视觉 y 绝对设为 1.2。
- 四个助手池使用固定且预留的同步 ID，主客双方在运行时生成前确定性注册；通用跨世界池分配器显式
  跳过该区间。客户端只接收生成、位置和动画，不分配目标、不认领金币、不修改国库。
- 旧共享存款继续保留，但不再用协程返回时的伪完成时点同步。日息前先载入主余额、原生计息一次后
  保存；真实存入和提款由主银行家余额变化写回，并以节流方式落盘。

### D17. 友好巨魔只排除 Squid，反制身份绑定稳定同步槽（2026-08-15）
- `Squid` 是长期悬空且地面冲撞不可达的敌人；`CrownStealer` 会俯冲和接地，仍是合法目标。禁止用
  当前 y 或统一“飞行怪”标签过滤，以免误伤跳跃、扑击中的地面敌人。
- 候选过滤挂公开 `StateMachine.StepCoroutine`，先用 FSM 指针 O(1) 判断是否属于 FriendlyTroll；
  只有命中后才临时从敌人集合移除 active Squid，并在 Postfix/Finalizer 逐项恢复。旧的私有
  `IsTargetValid` 补丁不再作为正确性依赖。
- 约 10% 的 `TrollWeak` 由存档槽、挑战、岛屿、统治期、岛屿创建时间与动态 NetID 的显式稳定哈希
  指定。NetID 代表同步池槽，同一统治期内复用该槽保持相同结果；这是无自定义 RPC/协议扩展下的
  安全边界，概率是大量槽位的长期平均，而非每次对象池复用独立抽样。
- 只有世界权威端、只有被标记的活动弱巨魔，在原生优先目标查询作用域内临时登记 active FriendlyTroll；
  查询后逐项恢复。普通巨魔、伤害掩码、冲撞物理、对象池和客户端 AI 均不修改。


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
- 效果：北境工匠 1.175 与希腊工匠 1.075 视觉齐平；北境居民、希腊世界的普通 Peasant 与
  WarriorPeasant 统一为 1.125
  （模型原始高度不同，系数为对齐补偿，实测调参得出）。酿酒师隐士按 `HermitType.Baker`
  判别并绝对设为 y=1.15；号角隐士按`HermitType.Horn`独立判别并同样绝对设为y=1.15；
  马厩隐士`HermitType.Horse`保持y=1.10。三者均保留x朝向和z，不影响其他隐士。

### D4. 缩放守护用 ConditionalWeakTable
- 决策：`UnitScaleRegistry` 用弱引用 key，单位销毁自动清理；池复用 OnEnable 覆盖登记；
  转化（ReplaceBy 创建新对象）不影响旧登记。零泄漏、零手动清理。
- IL2CPP 发布线的现有 `ScaleRegistryHolder` 以 instanceID 登记，因此酿酒师在真正 OnDestroy 时
  精确注销；OnDisable 不注销，保证对象池复用期间仍由 OnEnable 重写并维持目标值。
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
  该段属于 2026-08-12 的历史决策；其“单银行家跨图扫描/夜间不休息”部分已由 D16 取代。
  当前保留唯一真实 Banker，跨图收币由四名无 Banker 行为的轻量助手承担。
- **Enabled 契约统一（P1）**：所有 patch 入口统一检查 `Main.Enabled` 再执行
  （Patch_PoolManager / Patch_SidedShop / Patch_WorkerScale 补齐缺失检查），关闭 mod 后零副作用。

## 已知风险/开放问题

- R1：存档会序列化 localScale.y（Serializer.cs:1935 写完整 localScale），y=1.175 会入档。
  读档恢复自洽（还是 1.175），但存档文件携带 mod 缩放值；卸载 mod 后旧档单位尺寸可能不符预期。
- R2：WarriorPeasant→Berserker 转化瞬间继承居民 y=1.125，Berserker 无登记，Mover 会覆盖回 1.0——
  狂战士最终是标准大小（1.0），这是当前意图，若要改需给 Berserker 加登记。
- R3（已关闭 2026-08-12）：`Patch_Mover.cs` 已核实为玩家移动速度倍率（Main.speedMultiplier），
  并经 D8 修复为 SetGoal 入口（P0），保留。见 checklist core-015 / maint-001。
- R4（已关闭 2026-08-12）：Patch_Probe.cs 已删除（checklist maint-002 done），探测代码不进入发布版。
