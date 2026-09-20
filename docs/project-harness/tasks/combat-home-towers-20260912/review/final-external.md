三个目标文件、两份设计/研究文档、以及 2.1 native 参考（Berserker/Mover/Scanner/StateMachine/Archer/GuardSlot）均已交叉核对完毕。以下为独立只读最终审查报告。

---

# 独立最终审查报告（combat-home-towers-20260912）

审查对象：`../build/PatchCombatChaseGuard.cs`、`../build/SpecialTowerDuplicateCleanup.cs`、`../build/PatchWorld_TowerSpots.cs`。对照 2.1 native 参考（`C:/Users/ADMIN/projects/ohmymods/game-source/Assembly-CSharp-2.1.0`）与 `../research/chase.md`、`../review/tower-design.md`。

## 一、控制流与 native 语义核对（关键结论）

**PatchCombatChaseGuard.cs — 全部核心假设与 2.1 native 一致：**

- AttackRoutine（Berserker.cs:303-365）确为 `while (GetClosest()!=null)` 无距离限制，追击经 `mover.SetGoal(closest,...)`；leash 只在 `fsm==Attack` 时触发（`CheckDistance`），且从不打断 Attack——补丁只在 Rage/其他状态用 `GoToState`，并有 `b.fsm.Current != desired` 防重入守卫（PatchCombatChaseGuard.cs:324），不会每帧重置 GotoWall 协程。符合"完成当前跳劈后返回"。
- FSM 转换图核实：Rage(2)/Attack(3) 无出边转换，仅协程自退出（Attack→Rage2），Rage nextState=2 自环；补丁 postfix 在非 Attack 时转 GotoWall1/FollowTarget4 正是设计要求的出路。Scanner 过滤使 ShouldRage/ShouldAttack 的 `GetAny()` 返回 false，`GetClosest()` 当帧返回 null，while 自然结束——链路成立。
- Mover 语义核实：`SetGoal(float)` → `GoalMode.Position` + `_goalObject=null`（Mover.cs:328-331），`SetGoal(GameObject,...)` → `GoalMode.Object`；`GetGoal()` 返回已解析的 `_goalPosition`（含 Distance-clamp 与 FollowTargetRoutine 的随机偏移解析值）。`UpdateHome` 的两个分支（GotoWall Position / FollowTarget Object+`_goalObject==follow`）与释放判据（实际 native goal 2 以内）与 native 完全对应；wall 目标含 ±1 随机（Berserker.cs:194），补丁用 `GetGoal().x` 而非公式复算，正确。
- followTarget 类型为 Knight、OnEnable 清 null、TryRecruit/Disband 写入——`UpdateHome` 以 FollowKey 指针变化检测招募/解散并重置状态（"native 招募/解散优先"），不清 followTarget，符合合同。
- 生命周期：OnEnable/OnDisable 成对 Release+Ensure，Pointer+InstanceId+World/RootKey 四重校验防池复用/换世界串写；`Filter` 内先分类后压缩、全量再验（含 `_filtered.Pointer`、`_numFiltered==original`）后提交，异常不会半改缓存；count0/超界 clamp；NaN/Infinity 经 `Finite`/`SaneHome` 拒绝不形成永久 latch（HomeKnown 只置 false，不锁 Returning）。
- 忍者：仅白天 `isDaytime && Enemy.shouldRetreat` 过滤三自有 scanner（`Owns` 精确指针匹配，Fisher 的 `_wildlifeScanner` 不会进索引）；只在 `latestGoto==ChaseEnemies` 时清 targetEnemy 并 Goto 原生返 dojo/伏击位，Ambush/Fighting 短动作不被抢；无新增距离限制。符合合同。
- 2.1 与 2.4 差异（按要求明示）：2.1 `Scanner.Refresh()` 无参、`_filtered` 为纯 C# 数组；2.4 为 `Refresh(bool)` + Il2CppReferenceArray（research 机器码证据 RVA 0x742ae0）。补丁用 `new Type[]{typeof(bool)}` 显式指定重载，正确。此差异只影响补丁目标选择，不影响语义假设。

**SpecialTowerDuplicateCleanup.cs — 拆除序列与 tower-design.md 第 29-37 行最小安全序列逐条对应：**

- 免变更 plan（身份/完工/付款/精确 SemiStatic header/同址 witness |dx|≤0.1、|dy|≤0.5）→ 快照 GuardSlot/Archer 对（`isArcherPresent` 无 archer = 未知即停；archer 必须仍指回本 root 的 slot）→ root 仍 active 注册时逐个原生 `ExitGuardSlot()`（native Archer.cs:1384-1422 确认：ExitArcher 清 slot 引用、恢复物理/拾币、reparent 到 gameLayer、末尾才清 `_guardSlot`——补丁的"slot.archer==null 不是成功证据"四重验证与此完全吻合）→ 释放验证（非子级、无残留 slot/archer）→ 全门重跑 → 精确 header `DeregisterObject` → `DontPersistInstance(true)` → `SetActive(false)` → `Destroy`，绝不用 `Tower.DestroyTower`。任一失败即 -1/-2 停整个 pass、保留剩余。
- witness 判据用真实类型组件（TowerKnight/Ballista/FireTower/Baker/OilFireArcherTower/Marker），绝不按名称；候选自身层级含特殊组件/marker 即保护；祖先/后代互不 witness；多特殊塔同址全保留（witness 只授权删普通塔）。符合用户合同。
- level==0 候选走更严路径（需 PayableUpgrade、无施工、付款清）；已升级普通塔（level≥1 带 WorkableBuilding+CBC）需显式完工证据（UnderConstruction/NeedsMoreWork/_scaffolding/Scaffolding 全清）——"即使已升级也可删、施工未知保留"成立。

**PatchWorld_TowerSpots.cs — 新旧路径叠加核查：**

- `RemoveDuplicates` 先行、旧 KEM 空基回收（`RetireOverlappingGeneratedBases`）随后。旧路径仅触及 `KEM_TowerSpot` 名 + level==0 + PayableUpgrade + 无任何 GuardSlot/Archer/特殊组件/marker/施工组件 + 付款清（`CanRetireGeneratedBase` :751-802 + `IsProtectedHierarchy` + `IsPaymentClear` + `HasAssociatedScaffolding`）的对象——新 helper 保留的任何对象（无 marker 的已升级塔、有守卫/施工/付款冲突者）结构上进不了旧路径，"旧空 KEM 路径不能绕过保护"成立。
- 幂等根基：参考集只取未购买原生基底（tag Tower + level==0 + 无 KEM 名），补放点/已建塔绝不入参考集，中位数与 outermost 不随读档漂移；占用集全量（tag Tower + WorkableBuilding + 活跃 Scaffolding 及其 Building 指针关联）。
- 每次删除前全量重验、异常 fail-closed（Overlap Unknown 不删也不放）、延迟协程捕获 world 与当前 `Managers.Inst` 双指针校验、per-world 守卫仅在完整 mutation pass 后消费（瞬时未就绪不吞世界）。

## 二、发现的问题

**P0：无。P1：无。P2：无。** 未发现具有可达触发路径的真实缺陷。

**P3（不阻塞，均为退化方向、可自愈/依赖前提）：**

1. **PatchWorld_TowerSpots.cs:93-94 静态裸指针守卫的地址复用陈旧**：`_expandedWorld/_expandedLayer` 持 IntPtr 而非强引用，IL2CPP GC 回收旧 World/gameLayer 后新对象复用同一地址时，两个指针同时撞中的加载会被误判"已扩过"而跳过本次补放。后果仅是该岛少放点，下次读档指针再变即恢复；cleanup 路径不受影响。触发概率低（需双指针同址）。
2. **PatchWorld_TowerSpots.cs:334 `LayerMask.GetMask("NotBuildable")` 在层名缺失时返回 0**：mask=0 的 `OverlapPoint` 永远无命中 → `TryPlaceX` 恒真，可能放出原生 `onlyInBuildableRegion` 锁死（买不了）的点。原生 PayableUpgrade 使用同一层名（IsLockedForReason），实际游戏中层存在，故为防御性缺口而非可达 bug。
3. **命名用 `x.ToString("F1")`（PatchWorld_TowerSpots.cs:988）受当前 culture 影响**（逗号小数位系统生成 "KEM_TowerSpot_12,3"）：判定只依赖 `StartsWith(MarkerPrefix)`，逻辑不受影响，仅日志/名字美观问题。

## 三、Verdict

**PASS（通过）。** 三个文件的控制流、native 生命周期、拆除顺序、新旧路径隔离与用户合同全部一致；2.1 参考（无参 `Scanner.Refresh`、FSM 转换图、Mover goal 语义、Archer.ExitGuardSlot 拆除序列）与 2.4 实际 API（`Refresh(bool)` 等，research 已证）的差异均已正确处理。无 P0-P2 问题；上述 3 条 P3 为可选加固项，不构成发布阻塞。

SHA：按指示省略（本审查仅用 Read/Grep/Glob，无法求 hash），由 Operator 补充。