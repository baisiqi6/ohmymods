# crossbowman-021 — 弩手（死地换皮弓箭手）设计定稿

> 状态：**实现完成待实机**（2026-08-24）。`il2cpp/PatchRoles_Crossbowman.cs`（~880 行），
> 编译 0W/0E，OMP worker 3 轮 + reviewer（kimi-k3）2 轮收口，E 盘 build=2.2.0-xbow2。
> 皮肤修正（2026-08-24，用户指正）：`archer_deadlands`（死地猎人）→
> **`archer_soldier_deadlands`（死地士兵=骑士小队随从/塔位/上船同款姿态）** + 原生
> ConvertToSoldier 同款旗帜色染衣（主/副色随机）；Strip 走原生 biome swap 恢复猎人。
> 士兵皮肤与猎人行为不冲突：原生 Archer 本就在两套控制器间来回转（塔/船→士兵），
> 行为由 `_knight==null` 例程驱动；打猎用 idle/walk/run/shoot 士兵动画集齐全。
> 实现偏差记录（2026-08-24 二次简化，用户拍板）：弹道放弃平直改造（墙后 ParabolaCast 必走
> 高抛解，平直展示不出来）——**只做射程×1.5**：初速×√1.5（Range=v²/g）、重力原生不动，
> 弹道形状与普通弓箭一致；读档重算跳过骑士小队成员（不计入 25% 分母，reviewer Q2 裁决）；
> 弩矢=Arrow 克隆+Bolt sprite×0.65 缩放（Bolt 类塞不进 Arrow 字段）。验收清单见 checklist。
> 会话压缩安全：本文档自包含，实现者只需再读 AGENTS.md 与本文。

## 背景与关键事实（已侦查实锤）

- "死地弩手"**不是独立兵种**：2.1.0 源码 / 2.4.0 interop / 存档均无 Crossbowman/Arbalest 类。
- 真身是 **`archer_deadlands` 动画换皮**（Archer 类 + deadlands 动画控制器，与 `archer_norselands`、
  `banker_deadlands` 同一套 BiomeSwapData 换皮机制；resources.assets 中 6 处命中）。
- 弓箭手职业链：`Character.Professions["Bow"] → Archer`（居民捡弓转职，与锤子→工匠同构）。

## 设计定稿（用户拍板）

| 项 | 定案 |
|---|---|
| 获取方式 | **3:1 交替**：每成功转职 4 个弓箭手，第 4 个为弩手（挂在弓转职入口，交替工匠同款模式） |
| 计数器 | 游戏进程内跨岛延续、完整退出重置（狂战士进阶序列同款惯例） |
| 本体 | 不建新兵种/新池/新商店：Archer 实例 + `archer_deadlands` 动画 + 参数覆盖 |
| 射程 | 12 步（基础弓 8） |
| 装填冷却 | ×2（慢装填） |
| 单发伤害 | **×2**（用户定稿；DPS 与弓近似，"重锤慢打"节奏） |
| 弹道 | 初速 ×1.5、重力 ×0.5（平直快弹，像弩） |
| 投射物 | **独立弩矢**：克隆 ArrowAttack 配置 + 缩小版原生弩炮弹矢（BallistaBolt 家族）做外观，
  独立同步池（先例：忍者 ThrowingStar syncID=41，PatchRoles_Ninja.cs） |
| 骑士侍从 | **不可被骑士招募**：挂进 FetchArchersForJob 候选过滤——骑士编队的
  overrideShootCooldown 会覆盖射击节奏，抹掉弩手身份；弩手永远是独立守墙远程位 |
| 读档持久化 | 皮肤不进原生存档。读档完成后按场上弓箭手排序**每第 4 个重新换皮**——
  弩手数量守恒（25%），具体哪几只可能轮换（用户已接受，零存档写入零兼容风险） |
| 行为 | 守城/上船/被独角兽增益等全部原生（它就是弓箭手） |

## 开工前侦查清单（实现者第一课）

1. **弓转职入口**：确认居民捡弓 → Character.Promote 的确切调用链与可挂钩点
   （参考 PatchRoles_Berserker.cs 挂 Promote 的先例）。
2. **ArrowAttack ScriptableObject 字段**：Range/伤害/投射物引用/速度与重力的实际位置
   （Archer.ActiveArrowAttack 可写；部分箭道参数可能在 Arrow prefab 的 Rigidbody/Launchable 上）。
3. **BallistaBolt prefab**：路径、缩放、池注册方式（缩小做弩矢外观）。
4. **动画换皮时机**：转职后组件初始化时序（银行助手 TryResolveControllers 的控制器解析+赋值先例，
   PatchEconomy_BankAssistants.cs）。
5. **读档重算挂点**：世界加载完成事件（World.OnLevelLoaded 协程宿主先例，PatchWorld_DefenseSpacing.cs）。

## 风险与边界

- 联机：animator trigger 名与原版换皮资产兼容（原生资产）；伤害/射程为权威端行为；
  弩矢走同步池。
- 伤害 ×2 的平衡落点在 ArrowAttack 的伤害字段，实现时确认可调；不可调则降级为
  "双发结算"（同一目标连吃两次箭伤）——实现时定。
- 交替计数从第 1 个弓算起：1弓 2弓 3弩 4弓…（第 4 个为弩手；用户表述"每3出1"按 25% 理解）。
