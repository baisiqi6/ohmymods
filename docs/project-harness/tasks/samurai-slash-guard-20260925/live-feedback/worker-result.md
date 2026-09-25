# worker-result：choreo-hard-validity 第二轮（姿态收势 / 固定往返 / 去拖尾）

Worker：OMP zhipu-coding-plan/glm-5.3 thinking=max（上午路由）。基线=8E994A98 候选（67d39b9+上轮未提交修复），未重置；本轮全部改动叠加在工作树上。未 commit/push/merge/reset/clean/远端/MCP/Coordinate/子代理/安装 DLL/启动游戏/写玩家存档；yolo 仅用于非交互 bash 审批。

## 一、生产变更（仅 il2cpp/PatchRoles_SamuraiPowerDash.cs）

### A 姿态契约（真实 2.4 controller）
- 新增 `PowerSlash/PowerSlashFullPath(=StringToHash("Base Layer.PowerSlash"))/Land(=StringToHash("Land"))` 哈希与 `PowerSlashByController`（controller 指针 → HasState 结论缓存，沿用 SlashByController 形态；探针异常不缓存，下次行程重试）。
- `TryOpenSlashPose`（行程开头取姿态权威：animator+controller 指针）、`PoseAuthority`（同一可读 animator/controller 身份门，被替换即失效）、`SlashLegStart`（每腿 `ResetTrigger(Land)` + `Play(fullPath,0,0)`；无验证/被替换→退回 `SetTrigger(PowerSlash)`，永不 Play/Land）、`LandSlashPose`（终幕收势：姿态权威 && 非死亡 && `GetCurrentAnimatorStateInfo(0)` 命中 PowerSlash（短名或全路径）→ `SetTrigger(Land)` 恰一次）。
- 出程=StartChoreo 开权威后 SlashLegStart；turn=先 `ResetTrigger(PowerSlash)`（保留原顺序）再 SlashLegStart；**每次 Finale（complete/tick-cap/hard-invalid:*/on-disable/exception/start-failed/out|home-deadline）都走 LandSlashPose**——config/style/mover-replaced 等存活类硬失效也收势，死亡/别的状态/替换控制器不覆盖。不新增 RPC；姿态不同步缺口维持原状（类头注明）。
- PowerSlash m_Loop=true（clip=charge）：两腿重放会重放动画事件（斩击音效）两次——按审查记录为预期。
- **整体删除**旧捕获/修复网：TryCaptureSlashPose/CaptureSlashAtFinish/AdoptPeerSlash/RememberControllerSlash/CountCaptureMiss/TryCaptureDefaultPose/ProbeStuckPose/ClearEpisode/RecoverStuckPose/Heal/HealLog/ReadAnimator/TryPoseHash/PoseHash 及全部相关字段常量（Default capture 不复存在——default-capture=-526882654 即 PowerSlash 的毒化证据）。

### B 行程两端冻结与到点判别
- 协程内目标**每帧重申**（删 .3s cadence）：yield null 在全部 Update 后恢复，原生偷写至多存活一个被消费帧。不新增 Mover.Update hook、不 ForceStop、不写 velocity/position（按审查 B必改1）。
- 到点=腿起冻结方向的**越点判定**（`dir>0 ? x>=goal-.25 : x<=goal+.25`，home 腿 dir 在 turn 时按实际位置冻结）；大 dt 跳过 goal+容差仍判到达。
- 超时不谎报：home 腿超时 `step="home-deadline"`（≠complete）关闭；出程超时记 `token.TurnTimedOut` 并当场记一行 `out-deadline`，仍转身回原 home（超时事实保留）。腿间不 Stop（turn 只重设目标）；Finale 保留 OwnGoal 才 Stop 的值纪律。1.2s/腿+3s Tick 帽不变。
- `LogChoreoClose`：删 6s 节流，预算 12→24，字段 knight/step/phase/elapsed/home/outGoal/turnX/endX/turnTimedOut。推断边界照审查写进代码注释：每帧重申只缩小暴露窗口，不宣称完全阻止 Update 消费或这就是现场唯一根因；无轨迹证据证明 goal 偷写造成过长回撤（有证据的只有 mode=return 追随冲刺与夜间 GoToWall）。

### C 删除追随从冲刺（clean cutover）
- 删除 `MotionKind.Return` 全链：BeginReturn/AdvanceReturn/CanHit/HitScan(MotionLease)/IsReturning/EnemyFacing/HoldFacing/RestoreCombatEffects/RestoreEffects/LogMotionEnd/EndReason/MotionKind 及 Failures/RetryAt/BackoffSeconds/WalkAfterFailures/RetryStep/MaxRange/DashTimeout 常量。`SuppressesSlash = IsChoreoActive`（walk 永不压制）。
- MotionLease 压缩为 walk-only（Actor/Mover/Trail/GoalX/GoalSpeed/NextGoal/Retired/HasGoal）。Tick 简化：`distance > FollowLeash → BeginWalk`（保留 timeScale/pause 门；夜间 distance>10 仍交原生队列）。walk=纯步速回队：无效果/无敌/命中/视觉/朝向租约/期限，只有到站、失随从、失所有权、黄昏四收口。
- 行为变化（用户裁定直接后果，记录）：失去回程无敌、白 burst、防御朝向租约、3 秒回合上限（回程保护已不存在，无边界需求）。

### D 去拖尾（纯删除）
- 删除 StartChoreo 的 `trail.enabled=true`/`time=SamuraiTrailLifetime` 钉值、`ChoreoOldTrail/ChoreoOldTrailTime` 快照与归还、常量 SamuraiTrailLifetime。保留只读 `LogChoreoTrail` 诊断。外部已启用的 trail 原样存活（不替别人关特效）。SamuraiDashVisuals 零改动。已知 native 写点（OnEnable 只写 false、SetRetreating 不碰、网络路径 host-authority 外）在行程窗口内无使能者——若下轮实机日志出现行程内 native enable，再按审查预案加"活动窗口抑制+快照归还"。

### 其他
- 类头大注释整体重写（捕获网/撤退梯叙述已不实）；SpeedParam（无引用）删除；`ChoreoStartX`（与 home 重复）删除。
- ⚠️ 交接给 operator：`il2cpp/SamuraiDashVisuals.cs:12` 注释仍提及已删除的 `PatchRoles_SamuraiPowerDash.SamuraiTrailLifetime`（该文件本轮禁改，仅注释陈旧，无编译影响）；docs/project-harness 状态与 VERSIONING patch 账目按收尾义务留给 operator（不在本 worker 修改边界内）。

## 二、测试变更（tests/samurai-motion/Program.cs、Stubs.cs）

- Stubs：Animator 增 `HasState(int,int)`（默认 true，`OnHasState` 可注入 false）与 `SetTriggers/ResetTriggers` 哈希级记录（"set"/"reset" ops 无法区分 Land/PowerSlash）。Test() 重置：删 HealLogs 反射，SlashByController→PowerSlashByController。
- 新增组：
  - `PoseContractRegressions`（8 用例）：两腿 Play(fullPath,0,0)+先清 Land（顺序 reset→reset→play）；complete 收势恰一次 Land；别的状态/不可读动画机不发；config/style 硬失效仍发；死亡不发；替换控制器不发 Land 不重放（退触发器）；未知控制器退触发器且全程无 Play 无 Land；HasState 按控制器指针缓存（共享实例命中、异族自探、负结论生效）。
  - `ChoreoEndpointRegressions`（5 用例）：大 dt 越过 goal+容差仍到达（双腿）；受阻 home 腿以 home-deadline 关闭且行带 home/outGoal/turnX/endX/elapsed；受阻出程记 out-deadline+turnTimedOut=True 仍回原 home；移动/死亡随从不改变回家目标；逐帧偷写后目标恒我方且外部目标永不被 Stop。
  - trail 两用例：行程内零写（enable/lifetime 全程不动，ghost Begin/End 各一次）；外部 true+.7s 全程存活。
- `ReturnLadder`→`WalkHomeRegressions` 重写（walk-only 语义）；`FixedDashRegressions` trail 两用例重写；`RoundTripPhases` turn 姿态断言改 Play 序、trail 断言翻转；`RoundTripInterruptions`/`RoundTripNight`/Main 内 reassert 用例改"下一协程帧夺回"；`AssertStartedReturn` 改"walk 不压制原生斩击"。

### 迁移/删除表（安全断言不降格）
**迁到 Walk**（外部目标不被停/禁用清理/失 follower/夜间交接/暂停门/值纪律全保留）：failed-burst→同帧开程、live-goal-while-blocked、hit-stun 重开、foreign facing 存活、interrupt 循环(8)、夜间交接、dusk 交接、preexisting 旗标、外部目标三类、follower 失效(4)、最近随从跟踪、paused 无开程、disable+late-cleanup、cache 不逐帧(2)、Stuck→blocked-walk 无重试环、hysteresis band、Finish Stop 回调重入、长/短 gap 步速收口。
**迁到 Choreo**（效果归还/值纪律/视觉 token）：Two consecutive trips value discipline、trail 外部值存活、one protected motion（trail 断言翻转）、OnDisable×2、token Begin/End。
**删除（Return burst 专属语义，用户新契约取代）**：escalation/退休梯、backoff 升级、双侧防御朝向、Ahead 重申、10.5 行程边界、回程命中(3)、receding-follower 3s 上限（无敌保护已不存在，无边界需求）、burst 期限外部目标过渡、return running 视觉尾、`returning=true` 共享循环的 5×半侧、`StuckRecaptureRegressions`(4)、`StuckPoseRepair`(12)、`RunAttackLease` helper——后三者的安全关切（未知控制器/异态/死亡/替换）已在 PoseContractRegressions 重锚。
计数：216→186（净删 30；删除≈46 用例体，新增/重写 16）。

## 三、验证证据（receipts/）

| 项 | 结果 |
|---|---|
| samurai-motion 基线（改动前） | 216/0，exit 0 |
| samurai-motion 最终 | **186/0，exit 0**（motion-green.log） |
| 红验证 A：去 Land 收势 | 2 FAIL（complete 收势、config 硬失效收势），exit 1（red-land-closeout.log） |
| 红验证 B：恢复 0.3s 重申 | 5 FAIL（夜偷写/逐帧偷写/外目标/中断/黄昏），exit 1（red-0.3s-reassert.log） |
| 红验证 C：恢复绝对值到点 | 1 FAIL（大 dt 越点），exit 1（red-abs-arrival.log） |
| 每次红验证后恢复生产文件并复跑 | 186/0 恢复 |
| samurai-retreat | 43/0，exit 0（无共享提取影响，未动桩） |
| samurai-night-formation | 24/0，exit 0 |
| samurai-visuals | 31/0，exit 0（SamuraiDashVisuals 未动） |
| samurai-diagnostics | 9/0，exit 0 |
| 真实 IL2CPP 构建 `dotnet build -c Debug -p:BepInExPluginsPath=`（il2cpp 目录，空复制路径） | **0 警告 0 错误，exit 0**（il2cpp-build.log） |

API 面（2.4 interop wrapper 核验）：`Animator.HasState(int,int)`/`Play(int,int,float)`/`SetTrigger/ResetTrigger(int)`/`GetCurrentAnimatorStateInfo(int)` 存在（work/feedback2/Animator.interop.cs）；未用 ForceStop（按审查禁用），仅既有 `Stop()`。

## 四、残留实机边界（doing，不宣称已验）

1. 实机收势观感：Land 触发后 PowerSlash→Land→Stand 链在真实控制器上的落点；两腿重放的斩击音效双次是否被用户接受（契约内预期）。
2. 每帧重申 vs 原生 GoToWall 在真实 Update/Mover 消费顺序下的净效果——只缩小暴露窗口（无轨迹证据指向本案）。
3. 姿态不同步缺口（客户端看不到 Land）维持原状；无新 RPC。
4. 行程内 native trail 使能者未观测到，但下轮实机日志若出现（trail-state 只读行可查）需按预案补活动窗口抑制。
5. walk 专属：失去回程无敌/防御朝向的实机手感；blocked walk 长期持有目标（无期限，设计如此）。
6. SamuraiDashVisuals.cs:12 注释陈旧与 docs/VERSIONING 账目——operator 收尾。

Operator补记：最终加入F1 pending Play与全部动画写入身份门回归，并纠正stub的short/full hash语义；修前185/4，修后189/0，实际构建0W0E。另5套独立31/24/32/43/9全绿。旧186仅worker交付时计数；本轮同问题返修不另计版本增量。
