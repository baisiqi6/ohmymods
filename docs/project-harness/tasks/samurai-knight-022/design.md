# samurai-knight-022 — 武士骑士 + 突进斩 设计定稿

> 状态：设计已获用户确认（2026-08-23），排在 knight-squad-023 之后实现。
> 会话压缩安全：本文档自包含。

## 背景与关键事实（已穷尽侦查）

- **Samurai prefab 存在于 2.4.0 资源**：`Prefabs/Characters/Samurai` + `Samurai (Pool)` 池条目
  + 攻防动画（samurai_attack01-04 / defend01-03）——与 `Knight_greece`/`Knight_norselands` 平行的
  皮肤变体，**由 Knight 类驱动**（无独立 C# 类，Samurai 类名 interop 0 命中）。
- **原生无任何"自动/免费冲锋或突进"**（穷尽证据，用户记忆为多年前的体验，现版已无）：
  - `_shouldCharge = true` 全代码库仅 1 处（Knight.DoCharge 内）；
  - `DoCharge()` 唯一调用方 = `PayableBorder.Pay()`（城墙旗帜付 2 金币）；
  - 骑士雕像效果 = 砍击冷却缩短 + 税收上限（Knight.cs:894/811-826）；
  - Statue 类的 charges/deadlandsCharge 是雕像投币槽点亮动画；
  - FreeCharge/AutoCharge/chargeUnlock/Dash/Lunge 命名 0 命中；2.1.0↔2.4.0 Knight 方法清单一致。
- **白光视觉 = `character.Inspire(duration)`**（DamageInvulnerability buff 的视觉；
  独角兽 BuffUnitsSteedAbility 与狂战士战吼白光同源）。

## 设计定稿（用户拍板）

### 获取：铁剑骑士交替

- 骑士转职（铁剑/Armor 链）时**普通骑士 ↔ 武士骑士交替**产生（交替工匠同款模式）。
- 武士 = 完整角色 prefab（Knight 类 + Samurai 皮肤）→ 建独立同步池
  （EnsurePoolForCharacter 先例），**皮肤随存档天然持久**（prefab 路径不同，同北境工匠）。

### 突进斩（自动触发被动，无需操作）

| 项 | 定案 |
|---|---|
| 触发 | 武士扫描 6 步内有敌人 且 自身冷却就绪 |
| 冷却 | 每武士独立约 10 秒 |
| 白光 | `character.Inspire(0.8s)`——原生白光视觉 + 0.8 秒免伤 |
| 突进 | 朝目标方向高速冲刺固定 4-5 步（不锁定追击、不冲进怪堆深处） |
| 路径伤害 | 突进期间对身前判定框内敌人各结算一次砍击伤害（每敌只吃一次） |
| 收尾 | 白光自然消退，进冷却 |

### 边界

- 保留骑士全部原生行为：守墙列队（V2.1 的 rank 压缩同样适用）、旗帜冲锋、上船、撤退。
- 仅 world-authority 执行；位置经 PositionSync 原生同步；伤害走原生 ReceiveDamage。
- 白天夜间都可用（激进人设）。

## 可复用模式（全部本仓库已验证）

- Slash 判定框模式：WarriorGhostLeader.Slash（OverlapAreaNonAlloc + 每敌一次）。
- 监督协程宿主：World.OnLevelLoaded postfix + WrapToIl2Cpp（PatchWorld_DefenseSpacing /
  PatchDivine_GhostLeashHold）。
- 速度脉冲：Mover.SetSpeedMultiplier / SetGoal；免伤白光：character.Inspire。
- 角色池注册：PatchRoles_Holder 的 EnsurePoolForCharacter。

## 开工前侦查清单

1. 铁剑骑士转职链的确切入口（Armor→Knight 的 Promote 路径）与交替挂钩点。
2. Samurai prefab 的引用方式（Resources.LoadAll 按名唯一匹配，GhostSquads 先例）。
3. 突进期间与原生 FSM（Charge/Stand/GoToWall 状态）的互斥处理——突进中暂停原生 AI 还是
   作为协程并行（倾向：突进协程内 ForceStop mover + 完成后恢复，参照幽灵钉住语义）。
