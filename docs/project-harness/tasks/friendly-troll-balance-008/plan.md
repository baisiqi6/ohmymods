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

## 当前交接

- 用户已明确拍板上述目标筛选与平均 10% 平衡方案，税收助手调度保持不变。
- 代码已完成：通过公开 `StateMachine.StepCoroutine` 精确限定 FriendlyTroll FSM，在候选枚举前临时移除 active Squid；正常返回与异常路径均恢复。反制 TrollWeak 通过公开 TargetCacher 查询临时注入 active FriendlyTroll，随后逐项恢复。
- 初版已由独立 reviewer 静态 APPROVED，并曾部署测试候选。2026-08-16 实机日志已证明约 10% 的稳定标记阶段生效，观察到 9 个被指定的 TrollWeak；但当时没有出现目标注入或真实伤害证据，因此不能把“可攻击友好巨魔”判为运行时通过。
- 为区分设计链路的断点，新增四阶段一次性诊断：`friendly-active`（友好巨魔登记）、`counter-query`（被指定 Troll 进入原生目标查询）、`friendly-injected`（临时注入候选）和 `native-damage`（友好巨魔的原生受伤事件确认来自该 Troll）。诊断只订阅活动友好巨魔自身事件，回池、失活和指针变化时精确解绑；不修改目标结果、伤害、概率、RPC、对象池或原生集合恢复。
- 诊断修订经 worker 构建与独立 reviewer 静态 APPROVED；IL2CPP Debug 构建 0 warning / 0 error，源码 SHA-256=`8C5ED0A5BEA423ED46E5B4FA0319A51C16FD7F4820823BB4CEFABE36819897CB`，DLL SHA-256=`33C23C6C780B26550453C4320D4C35B980B4E391BF8802C757EF2A40FD2C34C5`。游戏已退出，下一步仅部署独立测试副本；现有 release zip 不刷新。任务继续保持 doing，等待四阶段日志与真实冲撞证据。
- 2026-08-16 追加追击速度与索敌范围微调：只写 `_runSpeed` 和 `_maxAttackDistance`，以每实例
  捕获的原 profile 得到 2→3、10→20；StateMachine 作用域负责启停切换，ResetAndDespawn 回池前
  恢复，未改 charge/伤害/RPC。独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error；
  用户退出后已只部署独立测试副本，构建/部署 DLL SHA-256 均为
  `8571E740D8CD4C94E5552D13B7CD1AC5D3124FF863733191257A864B4E92FB94`；尚未实机或打包。
