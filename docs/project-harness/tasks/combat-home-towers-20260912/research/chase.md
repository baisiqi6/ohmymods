# Berserker返程与Berserker/Ninja黎明不追撤退怪

## 已收敛的实施合同

- 仅Berserker（包括同类isLeader变体）加追击距离：自主攻击时距稳定home超过12进入返回；距合法原生返程目标2以内解除。12/2为本轮明确接受的设计值，不是原生资产测量值。
- Ninja不增加12距离限制。仅处理白天追撤退怪，保留其原生伏击、隐身、追击时限和返程周期。
- 白天条件使用当前Kingdom.isDaytime，敌人条件使用实际Enemy.shouldRetreat。两者同时成立才排除该目标。不是“白天禁止战斗”；未撤退敌人、近身威胁仍可走原生选敌。
- 不操作玩家手控、失效/死亡/抓取、非当前world/layer的单位。Ninja的_isFisher变体不属于此追击修复。外部任务关系不被清空。
- 不改enemy状态，不移动目标，不传送友军，不手动复位rigidbody/动画/伤害状态。只筛自有敌人扫描缓存及调用安全时机的原生返回状态。

## 已证原生机制与followTarget

2.1参考根：`C:/Users/ADMIN/Documents/Codex/2026-08-13/wo/work/ohmymods-reference-backup-20260815/game-source/Assembly-CSharp-2.1.0/`。

Berserker.cs:305–367 AttackRoutine是持续`while(enemyScanner.GetClosest()!=null)`，没有home距离限制。ShouldRage与ShouldAttack只判断GetAny，后者仅进入攻击前执行，不能中断现有while。FSM实际2.4枚举已验：Idle0、GotoWall1、Rage2、Attack3、FollowTarget4、GrabDroppable5。

没有BerserkerLeader类；实际同一Berserker类型的isLeader字段区分leader。也没有实际EnemyMove类型。当前resources.assets GO21370 Berserker_norselands与GO21371 Berserker_leader_norselands均挂Berserker，没有Knight组件。

**followTarget实际类型是Knight**。Berserker.OnEnable先清null；Knight.TryRecruitAdditionalFollowers（参考1324–1349）才为骑士招募额外Berserker；Berserker.TryRecruit(Knight)写followTarget。没有普通狂战士默认跟狂战士leader的源码或资产证据。因此不能假设整个狂战士群体都默认followTarget非空，也不能把它误当Berserker类型。

有合法Knight followTarget时仍应过滤白天撤退敌人；返程以该骑士及已有native跟随偏移为home，转FollowTarget4，绝不清followTarget。无followTarget时home使用Kingdom.GetGuardPosition(side).Value减返回Key方向的distanceFromWall，转GotoWall1。FollowTargetRoutine.cs原生SetGoal包含distanceFromFollowTarget随机偏移；解锁必须认可实际resolved Mover goal/已到达，而非死等距骑士中心2以内。不要每次检查重新调用Random改变原生跟随间隔。

## 跳劈必须自然退出

Berserker AttackRoutine在跳跃时保存drag、置drag0、置bumpForce0，等待离地/落地，再DoDamageArea、RestoreBumpForce、恢复drag、Stop和落地动画。仅看到IsTouchingGround=true也不能证明协程已执行清理。

实际2.4 `_AttackRoutine_d__90.MoveNext` RVA0x5d8970，完整1920bytes，pointer-table sameSlots1。机器码证据：VA0x1805d8a8c调用Scanner.GetClosest(0x742580)，VA0x1805d8dd6调用Character.IsTouchingGround(0x97fc50)，VA0x1805d8e88调用Character.RestoreBumpForce(0x981aa0)，随后恢复native刚体drag。

因此返回请求先使自己的scanner不再提供追击目标，让当前一次跳劈/近战和协程清理完成。**不在fsm=Attack3时强制GoToState**；原生while自然结束转Rage2后，actor Update再转GotoWall1或FollowTarget4。Berserker没有公开“正在跳劈”事务字段；Character.IsTouchingGround、Mover.rigidbody.velocity、nextLeapTime可辅助诊断，不能用它们代替完整阶段边界。Mover.JumpTo直接设置rigidbody.velocity，没有IsJumping开关可可靠清除。

## Ninja返回契约

原生Ninja.Behaviour每次priority都重新FindClosestEnemy；goto64追击timeChaseEnemies耗尽后又Goto64，故20秒不是整个追逐的全局上限。

已验实际静态常量名称：ReturningToDojo1、InDojo2、PositioningForAmbush4、HidingForAmbush8、Ambush16、Fighting32、ChaseEnemies64、Smokebomb128、GuardKingdom256、Fishing512。实现应使用实际Ninja同名常量，不硬编码旧版本数字。

- ReturningToDojo：GetDojoPos→原生Mover.SetGoal，找不到dojo回campfire。
- PositioningForAmbush：保留有效_currentHidingSpot，否则原生GetHidingSpot重新分配；无spot转GuardKingdom，走边界内0.5–2单位后隐藏。
- Fighting32会SetHider(null)并清_currentHidingSpot，不可自行抢回已被其他忍者使用的旧spot。
- OnGoto原生回调会Stop mover、归还facing、离开Chase64清IsFiring、离开隐藏清IsHiding、非Ambush16恢复damageSource。

白天被拒的retreat目标应清targetEnemy；若当前ChaseEnemies，可通过behaviour.Goto(IsAmbushTime?PositioningForAmbush:ReturningToDojo)结束追逐，交回上述原生路径。只在Chase64这样无slash子流程的阶段主动转向；Ambush16/Fighting32先让当前短攻击结束。Ninja不使用自创返程Mover循环、不增加home距离锁。过滤持续存在，所以不是清targetEnemy一帧后又选回同撤退敌人；若有其他未撤退敌人，native priority仍可转回防御。

## 扫描过滤为什么需要一个窄Scanner钩子

只补Berserker.ShouldAttack不能拦AttackRoutine的直接GetClosest；只补Ninja.FindClosestEnemy也漏Ambush16直接_frontScanner.GetClosest、Hiding8的GetCount/IsAny及_rangeAttackScanner投星目标。

建议单个**Scanner.Refresh(bool)** postfix，仅对Berserker.enemyScanner和Ninja._frontScanner/_behindScanner/_rangeAttackScanner生效。实际2.4 GetClosest/GetAny机器码都直接call Refresh(0x742ae0)，包括缓存未到刷新时限的调用。无需强制额外物理扫描、修改刷新率或扫描全场单位。

实际字段类型：observer是UnityEngine.Transform；_filtered是Il2CppReferenceArray<GameObject>；_numFiltered是int；additionalRequirements是Scanner.ObjectCondition。不能把_filtered当静态Collider2D结果，也不能修改静态_sharedResults。保留原生所有已执行的tag/层/死活/extra-condition筛选，只从其结果中移除新增规则明确拒绝的对象。

身份合同：scanner与actor当前自有字段Pointer完全一致；actor实例Pointer+InstanceID+world/layer匹配，active且主机有权限；observer必须是该actor根transform。可由actor OnEnable/Update懒登记最多1/3个scanner到小型索引，不能通过全场Find或按名字猜owner。OnDisable/世界改变清索引，旧世界不写。Ninja不能误识别Fisher._wildlifeScanner。

索引合同：先验证数组非空、count>=0且不超过Length（超界应安全clamp或整次fail closed并只报一次）；只访问[0,min(count,Length))。先完成资格分类再压缩，避免谓词异常时半改缓存；保留项按原顺序向前搬到writeIndex，清除旧有效尾部，最后提交_numFiltered=writeIndex。原生count0应原样保持；不注入目标、不扩大count，不用数组Length代表有效结果。位置/home坐标须finite；NaN/Infinity不能引发永久return latch或数组破坏。

可替代的actor-only方案是SetExtraCondition组合原callback，但需完整rooted delegate生命周期与pool/world撤销，成本和风险更高；本轮优先一个有严格scanner身份门的Refresh postfix。

## 实际2.4 API与native地址

所有以下入口在当前GameAssembly.dll的Assembly-CSharp method-pointer表均sameSlots=1。仅表示本程序集表内地址不共享，不代表运行时detour已实测。具体版本可调用interop名称，不能硬编码RVA作为通用运行时地址。

| 入口 | token decimal | RVA |
| --- | ---: | --- |
| Berserker.OnEnable | 100664536 | 0x5cd4a0 |
| Berserker.OnDisable | 100664537 | 0x5cd190 |
| Berserker.Update | 100664538 | 0x5ce5e0 |
| Berserker.OnChangeState | 100664540 | 0x5cd130 |
| Berserker.ShouldGoToWall | 100664542 | 0x5ce230 |
| Berserker.ShouldRage | 100664549 | 0x5ce4c0 |
| Berserker.ShouldAttack | 100664552 | 0x5ce1b0 |
| Ninja.OnEnable | 100674096 | 0x644830 |
| Ninja.OnDisable | 100674097 | 0x644700 |
| Ninja.FindClosestEnemy | 100674100 | 0x643ba0 |
| Ninja.Update | 100674106 | 0x645dc0 |
| Ninja.GetHidingSpot | 100674121 | 0x644050 |
| Scanner.Refresh(bool) | 100678384 | 0x742ae0 |
| Scanner.GetClosest() | 100678386 | 0x742580 |
| Scanner.GetClosest(out GameObject) | 100678387 | 0x742730 |
| Scanner.GetAny | 100678388 | 0x7424b0 |
| Enemy.HandleOnDayStart | 100668486 | 0x5056d0 |

Berserker.ShouldRage与ShouldAttack在参考源码语义相同，但实际方法有独立静态初始化标记，未折叠到同地址。首选方案不需给两者各加补丁。Scanner.Refresh实际参数是bool，区别于2.1无参数参考；Harmony必须显式指定该bool重载。

实际public interop还包括：Berserker.fsm/mover/character/damageable/followTarget/side/distanceFromWall/isLeader/_unitController；Ninja.behaviour/targetEnemy/_currentHidingSpot/_character/_damageable/_isFisher/_chaseEnemyTime、继承Fisher._mover/side；Enemy.shouldRetreat/retreatAtDawn；Kingdom.isDaytime/GetGuardPosition/GetBorderSide；IHaglet.Goto(int)，IHagletCallable.latestGoto。Enemy.isAttacking是活跃袭击生命周期状态，不是“此刻挥拳”的可靠判定，不作为近身攻击标志。

## 必要测试与边界

需真实helper CompileLink测试：Berserker12外请求返程、Attack3空中不强跳、自然回Rage后原生GotoWall/Follow、2内解锁且不中途重选；合法followTarget不清、不永久等待骑士中心；白天retreat筛除但夜间/非retreat保留；Ninja仅黎明筛选无12限制，三个自有scanner都覆盖，Fisher/其他scanner不变；count0/负数/超界/NaN安全；池复用/旧world/权限丢失不写。

实际构建须引用E副本interop检查泛型数组和IHaglet接口；需要之后独立核验Harmony注册、跳劈落地恢复、忍者隐藏/返dojo、联机权威和本轮既有任务不被抢。当前研究阶段没有启动游戏、部署或改存档。
