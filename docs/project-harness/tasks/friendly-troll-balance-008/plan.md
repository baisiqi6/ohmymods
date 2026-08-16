# friendly-troll-balance-008 — 友好巨魔选敌修复与 10% 反制巨魔

## 目标

- 友好巨魔在选敌阶段跳过长期悬空且无法被其地面冲撞触及的 `Squid`。
- `CrownStealer` 保持正常有效目标，因为其俯冲/扑击与接地阶段存在真实命中机会。
- 普通弱巨魔中约 10% 在实际生成完成时被确定为“反友好巨魔”，可用原生冲撞伤害攻击附近友好巨魔，形成永久转化单位的消耗压力。
- 将友好巨魔的原生追击移动速度提高到当前值的 1.5 倍，并把原生索敌距离提高到当前值的 2 倍。

## 不变量与安全边界

- 仅 IL2CPP 2.4.0；Mono 冻结。税收助手、银行、船只和其他战斗单位不得改动。
- 飞行过滤只按精确类型 `Squid`；禁止按当前 y、高度阈值或跳跃状态判断，禁止继续排除 `CrownStealer`。
- 必须在候选枚举阶段排除 Squid，不能只在“最近目标已选出”后返回无效，否则最近 Squid 会导致重复重选与 2 秒游荡饥饿。
- 只对 `EnemyType.TrollWeak` 生成反制标记；ToughTroll、其他敌人、由友好巨魔恢复出的特殊路径不自动计入，除非后续实证要求。
- 指定概率是长期平均 10%，不是“每十只严格一只”。使用显式稳定 32 位哈希，不使用每帧随机、`UnityEngine.Random`、`string.GetHashCode()` 或 Unity instanceID。
- 哈希输入为存档槽/挑战/岛屿/统治期/岛屿创建时间与动态网络 NetID；`hash % 10 == 0` 为反制巨魔。NetID 在这里代表稳定的同步池槽身份，因此同一统治期内复用同一槽会重复相同结果；约 10% 指大量同步槽的长期平均，不承诺每次对象池复用都重新抽取。
- 读档和 authority 迁移由相同身份重算；缺存档上下文或网络 header 时 fail closed。为保持兼容，本任务不扩展 Troll 的 RPC 或序列化协议。
- 反制巨魔只在 world-authority 执行 AI 决策；客户端只接收原生位置、冲撞与伤害同步。不得新增 prefab 或对象池。
- 友好巨魔资源已允许 `DamageSource.Troll`；不得扩大碰撞体、修改跳跃轨迹或重写伤害系统。
- 原生永久化前的 `Troll_friendly` 仍保留临时召唤物的 `invulnerable=true`。Mod 启用且处于
  world-authority/Playing 时必须通过原生公开属性把活动友好巨魔切为可受伤；不得改写 HP、
  `damagedBy`、碰撞、存档 JSON 或 RPC 协议。
- 每个 Damageable 原生指针必须捕获可逆基线 `currentAtFirstCapture || isInvulnerableInitially`，
  以防 Mod 状态被原生存档为 false 后重启时丢失原版 true 基线。关闭 Mod 与正常游戏内回池前恢复；
  Loading、SailAway、PrepareUnload、失权和失活对象禁止写入，由原生 OnDisable Reset 收尾。
- 首次公开 setter 可能早于 CRPC header/客户端 catch-up。每实例只保留一次 pending；双方可创建场景协调器，
  但仅 authority 在 header、RPC index 与 catch-up 全部就绪后以同一公开 setter 补发一次，禁止调用私有发送 thunk。
- 2.4 实际原值为 `_runSpeed=2`、`_maxAttackDistance=10`，目标绝对结果为 3/20；不得修改
  `_chargeSpeed`、冲撞距离、伤害或冷却。每个池实例必须捕获原 profile 后按倍数重算，禁止在
  Init/状态机热路径累乘；关闭 Mod 或回池前恢复原值。
- 只有被标记的弱巨魔能把 active、未死亡的 FriendlyTroll 纳入原生冲锋范围目标；普通 90% 巨魔必须保持原版选敌。目标登记/临时注入必须在 Postfix/Finalizer 恢复，不能污染全局 TargetCacher。
- 对象池 OnDisable/真正销毁/换岛时清理本次激活 registry；友好巨魔 active 列表由 Init、ApplyData、DeserializeFromData 登记并惰性清理，不得每个巨魔全场 FindObjectsOfType。

## 实现前证据

- 2.1/2.4 `FriendlyTroll` 原生目标枚举来自 `Managers.enemies.AllEnemies`，距离比较只考虑水平距离；本任务开始前的补丁只排除 `CrownStealer`，因此长期悬空的 `Squid` 仍会被选中。
- `Squid` 具有 `_flyAltitude`/`FlyUp` 等长期飞行行为；`CrownStealer` 有接地和扑击状态，二者不应同类处理。
- `Troll_friendly` 的 Damageable 已允许 Troll 伤害源，新增平衡只需要安全目标选择，不需要新伤害类型、池或 prefab。

## 验收

1. 独立 reviewer 确认 Squid 在候选枚举前被排除，CrownStealer 无排除路径，且无高度启发式。
2. 生成大量 TrollWeak 时标记结果来自稳定 10% 哈希；普通 Troll、池预热、读档恢复不会每帧重抽；同一统治期的同步池槽复用保持相同结果。
3. 只有被标记的 TrollWeak 会在原生冲锋范围内攻击友好巨魔；普通弱巨魔继续原版行为，友好巨魔能受到原生 Troll 冲撞伤害。
4. IL2CPP Debug 构建 0 warning / 0 error，`git diff --check` 与 harness validator 通过。
5. 独立副本实测：友好巨魔不再追 Squid 转圈；会在 CrownStealer 俯冲/接地时保留攻击机会；至少观察一只反制巨魔攻击并可能杀死友好巨魔。
6. 联机/权威迁移、读档、换岛与对象池复用无普通巨魔误标、目标缓存污染、未知 RPC/Pool 或相关异常。
7. 友好巨魔追击速度为原版 1.5 倍、索敌距离为原版 2 倍；冲撞动作不变，关闭 Mod 恢复原值，
   回池重生不会继续累乘。
8. 当前存档中的活动友好巨魔从原生无敌切换为可受 Troll 伤害；日志最终出现
   `friendly-injected` 与 `native-damage`。关闭 Mod、正常回池、换岛卸载及联机 catch-up 不产生重复 RPC、
   残留可伤状态或卸载期写入。

## 当前交接

- 用户已明确拍板上述目标筛选与平均 10% 平衡方案，税收助手调度保持不变。
- 代码已完成：通过公开 `StateMachine.StepCoroutine` 精确限定 FriendlyTroll FSM，在候选枚举前临时移除 active Squid；正常返回与异常路径均恢复。反制 TrollWeak 通过公开 TargetCacher 查询临时注入 active FriendlyTroll，随后逐项恢复。
- 初版已由独立 reviewer 静态 APPROVED，并曾部署测试候选。2026-08-16 实机日志已证明约 10% 的稳定标记阶段生效，观察到 9 个被指定的 TrollWeak；但当时没有出现目标注入或真实伤害证据，因此不能把“可攻击友好巨魔”判为运行时通过。
- 为区分设计链路的断点，新增四阶段一次性诊断：`friendly-active`（友好巨魔登记）、`counter-query`（被指定 Troll 进入原生目标查询）、`friendly-injected`（临时注入候选）和 `native-damage`（友好巨魔的原生受伤事件确认来自该 Troll）。诊断只订阅活动友好巨魔自身事件，回池、失活和指针变化时精确解绑；不修改目标结果、伤害、概率、RPC、对象池或原生集合恢复。
- 诊断修订经 worker 构建与独立 reviewer 静态 APPROVED；IL2CPP Debug 构建 0 warning / 0 error，源码 SHA-256=`8C5ED0A5BEA423ED46E5B4FA0319A51C16FD7F4820823BB4CEFABE36819897CB`，DLL SHA-256=`33C23C6C780B26550453C4320D4C35B980B4E391BF8802C757EF2A40FD2C34C5`。提交 `045994d` 已推送；确认游戏进程为 0 后只部署独立测试副本，部署 DLL 哈希与构建一致。现有 release zip 未刷新。任务继续保持 doing，等待四阶段日志与真实冲撞证据。
- 诊断实机确认断点：41 个友好巨魔已登记，8 个被指定 TrollWeak 均进入原生 `range=2.00` 查询，但候选注入和伤害都是 0。原设计只让已经贴近的目标进入冲撞查询，没有让反制巨魔主动接近，因此不足以实现“主动攻击”。
- 修订为权威端单一中央协调器，每 0.25 秒仅遍历反制巨魔×活动友好巨魔；只在原生普通行走状态、距离位于 `(当前 chargeRange, 10]` 时，以该巨魔原生 runSpeed 朝最近友好巨魔移动。进入原生 chargeRange 后完全零写，由既有 TargetCacher 注入、Jump、碰撞与伤害接管。携带战利品、撤退、石化、受控、死亡、暂停、寻路、冲锋与失权/卸载状态均不干预。
- worker 构建与独立 reviewer 最终静态 APPROVED；源码 SHA-256=`12700B854332A2CB8F12A21BD8669731321C5AD2358C6F9CFE1626A99375574E`，Debug DLL SHA-256=`8F122777143698C2FD0F51D0BE1E388849C4802C1DE299F93A2CA2918AAB72BF`，0 warning / 0 error。当前实测规模为 8×41，每秒约 1,312 次简单距离比较；无 LINQ、场景扫描、新 RPC 或对象池。提交 `3ee2be7` 已推送；进程为0时只部署独立副本，构建/部署哈希一致。release zip 未刷新，等待 `query→injected→native-damage` 实机闭环。
- 2026-08-16 追加追击速度与索敌范围微调：只写 `_runSpeed` 和 `_maxAttackDistance`，以每实例
  捕获的原 profile 得到 2→3、10→20；StateMachine 作用域负责启停切换，ResetAndDespawn 回池前
  恢复，未改 charge/伤害/RPC。独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error；
  用户退出后已只部署独立测试副本，构建/部署 DLL SHA-256 均为
  `8571E740D8CD4C94E5552D13B7CD1AC5D3124FF863733191257A864B4E92FB94`；尚未实机或打包。
- 最新实机再次确认：54 个友好巨魔登记、12 个反制巨魔进入查询，但注入与伤害仍为 0；只读存档复核
  54 个 `Troll_friendly` 的 Damageable 全部为 `invulnerable=true`。原生 `IsDamagedBy` 与
  `ReceiveDamage` 都会在无敌时拒绝，故这是追击之后仍无法攻击的决定性阻断，而非概率或性能问题。
- 最窄修复已完成并通过 worker 构建与独立 reviewer 最终 APPROVED：只在 authority/Playing/活动且指针一致时将友好巨魔切为可受伤；
  保存可逆原版基线，Disabled/正常回池恢复；早于 catch-up 的 setter 只挂起一次并在原生网络门禁就绪后
  用公开属性补发一次。未改 HP、伤害掩码、攻击、对象池、协议或存档。源码 SHA-256=
  `73934E38B3C1DB59CA27C14C9FF3F64F310C7F9E6697DC6A6E64AAF691D32542`，Debug DLL SHA-256=
  `BDF1FB4415E05E8F9596D19A024D020210ECCCEE7B6291D72D68CECBA9A4AB4B`。提交 `0495a68`
  已推送；确认进程为0后已只部署E盘独立副本，构建/部署hash一致。G盘当前未挂载，未写G盘；
  release zip未刷新，等待 `query→injected→native-damage` 实机闭环。
- 最新实机已完成核心闭环：56个友好巨魔登记、8个反制弱巨魔查询、7个目标注入、6次原生Troll伤害；
  六个受击目标事件后HP均为0。相关日志无Exception/Error、unknown pool、duplicate sync或RPC异常。
  “反制巨魔能主动攻击并消耗永久友好巨魔”已通过；任务保持doing，仅等待Disabled恢复、联机同步/切权、
  换岛卸载以及Squid/CrownStealer边界回归。
