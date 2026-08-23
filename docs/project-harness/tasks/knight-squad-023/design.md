# knight-squad-023 — 骑士小队（大骑士 + 2 小骑士 + 4 弓箭手）设计草案

> 状态：源自用户最初构想（首次正式发布后提出）+ 路线图；排在 crossbowman-021 之后、
> samurai-knight-022 之前实现（武士可复用本任务的编队基础设施）。
> 详细规则实现前需与用户过一遍本草案确认。

## 构想（用户原话归纳）

- 大骑士（拿到铁剑晋升的骑士）像原版吸引弓箭手随从一样，从附近**现有居民**中招募 2 人，
  让他们转职为**一阶段小骑士**——不是凭空生成人口，居民不足时不无中生有。
- 每支小队目标编制：**1 大骑士 + 2 小骑士 + 4 弓箭手**。
- 小骑士行军/待命时跟随大骑士，有稳定队列站位；遇敌用骑士原生战斗逻辑
  （攻击/举盾/防御/受伤/撤退），不做没有自主战斗能力的装饰随从。

## 原生可依托的机制（已确认存在）

- 骑士随从列表：`Knight._archers` + `FetchArchersForJob`（原生拉弓箭手当随从的机制——
  弓箭手侧有 `HasKnight()/IsKnightSoldier` 归属判定）。
- Formation 系统：类型槽 + 成员槽 + UnitSpacing（坑 22：三件套必须作为一个事务改；
  FleetBoatFormation 已验证）。
- 一阶段骑士 = **Squire**（盾牌骑士，`Professions["Shield"]→Squire`）——小骑士本体就用
  Squire，拿盾转职与原生完全一致。
- 骑士小队的跟随偏移：`knightFollowDistance` / Formation.GetXPosForUnit。

## 需要实现前定死的规则（下次与用户确认）

1. 大骑士怎么触发招募（晋升瞬间自动？还是持续每 10 秒尝试拉满，像原生拉弓箭手那样）。
2. 弓箭手 4 名：自动从独立弓箭手里拉（原生机制）还是要玩家操作。
3. 成员死亡：小骑士死了补不补（居民足够时自动补 vs 不补）；大骑士死了小队散伙还是
   归入城墙序列。
4. 换岛/读档：小队归属的持久化（KnightData 有随从持久化吗——侦查确认）。
5. 与 V2.1 rank 压缩的交互：小队整体算一个 rank 位还是各占位。

## 开工前侦查清单

1. Knight._archers 的持久化与网络同步方式（KnightData 序列化内容）。
2. FetchArchersForJob 全链 + Archer 加入/离开骑士的 RPC。
3. Squire prefab 与转职链（Shield 工具 → Squire）。
4. Formation 编队中混编 Knight/Squire/Archer 的原生先例（unitTypes 枚举有 Squire 槽位）。
