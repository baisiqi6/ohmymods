# 对抗审查结论：CHANGES_REQUESTED

方向正确、证据链完整，四条边界均可批，但有 4 处必须修正的实施细节（A 的 Land 适用面、B 的手段选择、B 的"完成"谎报、C 的死代码残留），修正后无需二审架构、只需按约束执行。以下按三问逐条给证据与约束。

## 证据核对（先立地基）

- 控制器契约与代码/日志三方互证通过：`knight-powerslash-contract.json` 中 PowerSlash `m_NameID=3768084642`（=signed -526882654），唯一出口是 trigger EventID `137525990`（=StringToHash("Land")，同时是 Land 态短名），`HasExitTime=false`；Land `m_ExitTime=1` 自动回 state 0；AnyState→PowerSlash `duration=0`、`CanTransitionToSelf=true`。实机 `KnightAnimatorDiag` 参数表含 `PowerSlash,Land`。
- 日志证实现行 stuck-pose 网整体失效：`default-capture hash=-526882654`（把 PowerSlash 当"平静姿态"捕获）、全程零 `slash-capture`/`slash-adopt`、零 `heal`。结构性原因：0 时长 AnyState 转移对逐帧 `IsInTransition` 采样不可见 → `LeaseSawTransition` 永假 → `SlashHash` 永空 → 探针 disarm，且 `CaptureSlashAtFinish` 同门拒绝。这不是参数问题，是方案对不上控制器。
- 日志证实第三段突进：`dash=11 mode=return ... end failure=True kind=Return` + 随后 walk（knight -90516，day，follower 距 12.45）——即 BeginReturn 追随从冲刺（上限 10.5、DashSpeed 18）。这是"回撤很远甚至大后方"唯一有现场证据的制造者；`night-wall-queue x77.37/follower87.39` 是夜间原生 GoToWall 排队（非冲刺）。往返本体距离良好：turn 距 home 6.87..7.02。
- 连续拖尾来源证实：行程内 `trail enabled=True lifetime=1 points=35..63`（mod 自己的 enable+钉值），残影系统（ghostSlots=8, whiten-baked）独立且用户已验收。

## A（姿态契约）— 正确、最优；两处约束必改

正确性：直接 `Play` 已验证状态 + 完成发 Land 是唯一贴合该控制器的做法；等待捕获类方案已证明结构性死路。优于现状（ResetTrigger 不构成出口 → 姿态永久滞留）。

**必改 1（Land 的适用面）**：不要只在"完成"发。hard-invalid 关闭（config/authority/style/mover-replaced 等存活类）时骑士同样停在 PowerSlash 姿态——用户抱怨的正是这个。约束：**每次 Finale 都尝试 Land**，门为：animator 可读 && `GetCurrentAnimatorStateInfo(0)` 命中 PowerSlash（fullPath 或 shortName）&& 非 dead（Die 的 AnyState 会先夺态，状态检查自然挡住，但保留显式子集判断更稳）。不得新增 RPC；姿态不同步缺口维持原状并留档。
**必改 2（腿起点 hygiene）**：每腿 Play 前先 `ResetTrigger(Land)`（残留 Land 会在下一 update 立即吃掉 PowerSlash→Land 出口）。turn 侧保留现有 `ResetTrigger(PowerSlash)+Play` 顺序。
**其余约束**：`HasState(0, Animator.StringToHash("Base Layer.PowerSlash"))` 按控制器指针缓存（沿用 SlashByController 形态）；Play 用 fullPath 哈希，不用短名（跨层歧义），不硬编码 3768084642。HasState 失败 → 保守回退：腿用 SetTrigger(PowerSlash)、不发 Land、不 Play。旧捕获/修复机制（TryCaptureSlashPose/CaptureSlashAtFinish/AdoptPeerSlash/SlashByController/ProbeStuckPose/Heal/TryCaptureDefaultPose 及全部相关字段常量）**整体删除**——对本控制器是死代码，保留只会误导。注意 PowerSlash `m_Loop=true`、clip=charge：双腿重放会重放动画事件（如 OnAnimSlash 音效）——可接受甚至是期望，写入说明即可。

新 bug 风险：低。中程被 AnyState 抢态（Block 等）→ 状态门使 Land 不发，骑士归原生解析，正确。

## B（行程两端冻结）— 目标正确；手段与叙事两处必改

**必改 1（不建议新增 Mover.Update prefix）**：协程 `yield return null` 在全部 Update 之后、LateUpdate 之前恢复——把现有 0.3s 重申改为**每帧重申**即可保证：原生 Knight.Update 的偷写最晚在同帧协程段被覆盖，Mover.Update 消费前目标恒为我方（残余暴露 ≤1 个被消费帧、幅度 ≤runSpeed×dt≈0.1 格，可忽略）。`SetGoalNoHaglet` 不在任何 SetGoal patch 路径上（DefenseSpacing/ShopQueue 只挂 SetGoal(float,float)），零干扰。新增全局 Mover.Update prefix 收益为零却引入：与 Deadlands prefix（`Mover_Update_DeadlandsSpeed_Patch`，__state 提升/恢复）和 Worker postfix 的顺序耦合、全单位每帧入口、以及"修 velocity"的越权写。**结论：改每帧重申，不加 hook**。若后续实机日志仍证明有偷写被消费，再按以下硬门补 prefix：void 返回永不跳过原方法、活跃 token 的 Mover 指针集合 O(1) 门（Start/Finale/OnDisable 三收口维护）、只写 goal 不碰 `_moveSpeed`/velocity。
**必改 2（真阻挡不可谎报完成）**：现代码 home 腿 deadline 到期也以 `step="complete"` 走静默关闭（`LogChoreoClose` 对 complete 直接 return）——超时与到达不可区分且无证据行。约束：home 腿区分 `arrived`/`home-deadline`，outbound 超时记 `out-deadline`；异常关闭行取消 6s 节流、预算提到 ≥24 行，字段 `home/outGoal/turnX/endX/elapsed/reason`。
**端点惯性建议（被问及）**：腿间**不要 Stop**——turn 只重设目标，Mover 的加速斜坡+rubber-band 减速天然处理反向（现场 overshoot ≤0.15）；Finale 保留现行值纪律 Stop（仅目标仍属己方时），**不得 ForceStop/直写 velocity**（原生 Update 从不清速度，偏离即瞬跳）。1.2s/腿+3s 帽、hard 子集、幂等 Finale、OnDisable 路径全部保持。
**到达判定**：按腿起方向做越点判定（`dir>0 ? x>=goal-.25 : x<=goal+.25`），方向在腿起冻结。两端点已在 token 冻结（ChoreoHomeX/固定 7），保持。
**推断边界（必须写进任务记录）**：无任何轨迹证据表明 goal 偷写造成过长回撤；有证据的只有 mode=return 追随冲刺与夜间原生 GoToWall。B 是加固不是本案主修复，不得在验收里宣称 B 修复了用户投诉。

## C（取消追随从冲刺）— 正确；要求干净切除

正确性：这是唯一有现场证据的"第三段"，用户裁定固定两段，删它是主修复。最优性：保留 Return 半套（invulnerable burst/backoff/escalation/entry-hit）没有存在理由。
**约束——clean cutover，不是旁路**：删除 `MotionKind.Return` 全链：BeginReturn、AdvanceReturn、CanHit/HitScan(m)、IsReturning、EnemyFacing、Failures/RetryAt/BackoffSeconds/WalkAfterFailures/RetryStep、MaxRange/DashTimeout（仅 burst 使用）、MotionLease 压缩为 Walk（Kind 字段可删）。Tick 分支简化为 `distance > FollowLeash → BeginWalk`（保留 timeScale/pause 门）。`SuppressesSlash = IsChoreoActive` 单腿。夜间行为不变（walk 遇 NightGuard 即 Finish 交原生 GoToWall，night-wall-queue 的 `a.Motion==null` 门自然放行）。行为变化（失去回程无敌/白burst）是用户裁定的直接后果，记录即可。
**测试**：ReturnLadder 中断言 burst 语义的用例（escalation、0.6s 窗、invulnerable burst、entry-frame hit、10.5 上限、"return interruption"系列）随特性删除；其中 defend 真实安全契约的（效果归还值纪律、外部目标永不被 Stop、替换 mover 零写入、config-off OnDisable）**改锚到 Walk 保留**，不降格为烟雾测试。

## D（去拖尾）— 正确；最小方案=纯删除

**最小有效**：删除 mod 的 trail enable+time 钉值与 `ChoreoOldTrail(OldTrailTime)` 字段（C 之后唯一写点就是 choreo），**不加任何抑制**。依据已知 native 写点：OnEnable 只写 false（Knight.cs:146-148）、SetRetreating 不碰 trail（635-656 验证）、网络 ReadExtras 是客户端应用路径（本 mod host-authority）、2.4 Slash 协程在行程内被 ShouldSlash 压制——行程窗口内无已知 native 使能者。持续 style2 抑制会与原生/网络化写点长期打架并波及武士自身原生攻击表现，超出了用户诉求，否决。保留 `LogTrailState` 只读诊断：下一轮实机日志若出现行程内 native enable，再加"活动窗口抑制+快照归还"（门=token 身份，绝不被 isRetreating 软旗挡；这也满足"不影响其他骑士"）。残影系统 SamuraiDashVisuals 零改动。测试翻转：行程内 trail 不得被使能、lifetime 不得被写、既有外部 true 原样存活、ghost Begin/End 每行程仍各一次。

## 被点名核对的四项

1. **固定两段**：A/B 保两腿、C 删第三段 ✓。2. **native 落地 trigger**：Land 契约三方互证通过（见地基）✓。3. **客户端/Hard invalid/end cleanup**：Land 受状态门+硬子集约束、Finale 幂等/OnDisable 收口保持、姿态不同步缺口维持并留档 ✓。4. **Deadlands Mover.Update 兼容**：按本审查不新增 patch → 零交互；若坚持加，用上列硬门（另注：Deadlands 提升仅作用于其 ByMover 注册的 deadlands 单位，与 style2 互斥，无数值复合）✓。

## 测试矩阵（tests/samurai-motion，直链真生产）

- 两腿契约：开程 ResetTrigger(Land)+Play(fullPath,0,0)；turn 顺序 reset→play→方向/缩放/命中轮重置；complete 关闭恰发一次 Land。
- Land 门：态非 PowerSlash（被 Block 抢走）不发；animator 不可读不发；config/style 关闭且仍处 PowerSlash 时**发**（姿态回收）。
- 未知控制器：HasState=false → 回退 trigger 路径、无 Land、行程照常关闭。
- 距离：越点帧仍判到达；home 仅容差内判完成；home-deadline 关闭行带 reason≠complete；out-deadline 仍 turn。
- 每帧重申：逐帧偷写后目标恒我方；外部 pause 闭锁不关闭；外部目标永不被 Stop。
- C：>10 → 仅 Walk（Position@StationX、runSpeed、无无敌/无 trail/无命中）；到达/失 follower/失目标/黄昏四收口；夜交原生。
- D：见上。
- 回归：夜系列/打断系列/清理纪律按新锚点全过；被删套件列名留档。Stubs 需补 `Animator.HasState`（默认 true + 可注入 false）。

收尾义务：类头大注释重写（捕获网/撤退梯叙述已不实）、`docs/project-harness` 状态与 VERSIONING patch 账目按规则同步。
