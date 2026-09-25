已收集证据。现在正在验证 `clean-cutover` 声称（没有残留的 `capture`/`Return`/`trail` 写入）以及 `diff` 的文件范围。
# 实现审查结论：CHANGES_REQUESTED（仅生产 snapshot；一处窄域必改 + 两处次要）

审查对象：`review-snapshot/PatchRoles_SamuraiPowerDash.cs`（1020 行，固定版）。`production.diff` 确认单文件、无新增 HarmonyPatch 类型（仍只有 `Knight.Update` Prefix/Postfix、`Knight.OnDisable`、`Knight.ShouldSlash`），grep 确认旧网零残留（无 `BeginReturn/AdvanceReturn/MotionKind/IsReturning/EnemyFacing/Backoff/RetryAt/MaxRange/DashTimeout/CaptureSlash/AdoptPeer/ProbeStuck/Heal/TryCaptureDefault/LeaseSawTransition/default-capture` 命中，命中项全为注释或新名 `PowerSlashByController`）。证据：controller-audit.json 与 knight-powerslash-contract.json 三方互证（PowerSlash shortHash 3768084642=signed −526882654、loop=true、唯一出口 trigger `Land`=137525990、Land exitTime=1 回 Stand、AnyState 入 0s）；whitefix-live.log 为**旧版 8E994A98**（build `9.14.24-choreo-whitefix`，含 `default-capture` 行）——只作根因证据，不作新版实测。

## 目标逐项核对（主路径全部达标）

- **回来不卡出刀姿势**：complete / home-deadline / hard-invalid（config、authority、style、mover-replaced、tick-cap、on-disable）全部走同一 `Finale`→`LandSlashPose`（snapshot:808-856, 686-700），门=PoseAuthority（同 animator 指针+同 controller 指针+isActiveAndEnabled）+非 dead+当前态命中 PowerSlash（short/full 双哈希）。死/被夺态/换 controller 不踩 ✓。设计审查四条必改全部落实：每 Finale 尝试 Land ✓、每腿 Play 前 `ResetTrigger(Land)`（:1028）✓、HasState 按控制器指针缓存且 probe 失败不缓存下次重试（:608-621）✓、Play 用 fullPath 不硬编码资源值 ✓。
- **两段固定、无第三段**：Return burst 全链删除（grep 证），>10 距离只剩 `BeginWalk` 普通 `_runSpeed`（:960-967）；walk 存续期 Tick 提前 return 不开新程（:427-434），CD 3s 从触发帧起算。旧日志 dash=11 `mode=return` 在新代码无路径可达。
- **取消白拖尾**：snapshot 零 trail 写（grep `\.enabled\s*=|\.time\s*=|\.emitting\s*=|ForceStop|velocity` 无命中）；`LogTrailState` 只读，外部启用的 trail 原样存活；`SamuraiDashVisuals` 定格残影未动 ✓。
- **行程纪律**：每帧重申（协程 try 内 `ChoreoGoal`，:735）、无 Mover.Update hook ✓；腿间不 Stop、无 ForceStop/直写 velocity ✓；越点判到达、方向腿起冻结（:737-741）✓；home-deadline 与 complete 严格区分、out-deadline 记 `TurnTimedOut` 落关闭行、预算 24 无节流、字段 home/outGoal/turnX/endX/elapsed 齐全（:901-917）✓；hard 子集 `ChoreoInvalidClause` 与触发门分离，wall-line 早切根因修正 ✓。

## F1（必改，窄域）：同帧 Play→Finale 窗口里 Land 的状态门读到 Play 前的旧态

`Animator.Play` 在 Update 相发出、下一动画更新才生效；此前 `GetCurrentAnimatorStateInfo(0)` 仍返回旧态（如 Stand）。`LandSlashPose` 的门（:1055）在“本帧刚 Play、Finale 同帧跟进”时看到非 PowerSlash → 不发 Land → 帧末 Play 生效，`m_Loop=true` 的 charge 循环态无人退出 → **永久卡出刀姿势，即用户本轮投诉的症状本身**。

最小反例（可执行路径）：
1. `StartChoreo`：`SlashLegStart` 已 `Play`（:1029）→ `ChoreoGoal` → `k.StartCoroutine(...)` 抛出（IL2CPP wrap/JIT 失败——本机有 InvalidProgramException 前科，此 catch 正为此存在）；
2. catch → `Finale(a, token, "start-failed")`（:597-601）：effects/goal 正确归还，但 `LandSlashPose` 状态门读到 Play 前的 Stand → return；
3. 帧末动画更新执行 Play → 武士以循环出刀姿永久滞留，旗标已清、压制已撤，仅靠夜间 Retreat/Die/Block 等偶然 AnyState 才可能自救。

同窗口第二入口：开程帧 `ChoreoGoal`/`SetGoalNoHaglet` 抛出 → Tick catch → `Finale("tick-exception")`（:470-474）。turn-interrupt 名义同窗口但实际不可达（invalid 子句需在 TurnChoreo 前后同步翻转，行程内骑士无敌）。

最小修正（~4 行，不破坏“绝不覆盖他人态”承诺）：
- `ChoreoToken` 增 `internal int LastPlayFrame = -1;`
- `SlashLegStart` authority 分支 Play 后记 `token.LastPlayFrame = Time.frameCount;`
- `LandSlashPose` 门改为：`bool inState = …; bool playPending = Time.frameCount == token.LastPlayFrame; if (!inState && !playPending) return;`
  `playPending` 为真时自上次 Play 起无动画更新运行，他人态不可能介入，补发 Land 安全；死门/authority 门不变。

## F2（次要，一致性硬ening）：两处 `ResetTrigger(PowerSlash)` 写在当前 animator 上，不受 controller 身份门约束

`Finale`（:838）与 `TurnChoreo`（:1168）的 `k._animator.ResetTrigger(PowerSlash)` 无 PoseAuthority/Same 门。若 controller 中程被换（且未先触发 style hard-invalid，实际近不可达），会清掉新 controller 上原生自设的 PowerSlash 触发器——与类头"no later pose write ever lands on the new controller"的承诺矛盾。最小修正：以 `Same(k._animator, token.Animator)`（或仅 `!token.PoseKnown` 的 fallback 路径，Play 路径本就从不设置该触发器）为门，authority 成立时本就同指针，零行为损失。

## F3（需确认的安全场景，不算缺陷）：GO 反激活时的 OnDisable 可能排不上 Land

GO 反激活触发的 `Knight.OnDisable` 回调期间 `animator.isActiveAndEnabled` 已为 false → `LandSlashPose` 早退，池化回收的骑士 animator 携带未消费的 PowerSlash 当前态/挂起 Play 入池。若原生池重生不 SetTrigger(Spawn) 类 AnyState 重置，则复生卡姿。建议：实测一次池化往返（或查 Knight.OnEnable 是否重置动画）；若需代码，仅 on-disable 路径放宽 `isActiveAndEnabled`（对禁用 animator 的 SetTrigger 会排队、复启时消费）。另注：turn 的无条件 re-Play 理论上可压过中程被夺的 Block 态——骑士行程内无敌、随后 Land→Stand 自愈，纯表现级，接受即可。

## 三问

- **正确**：主路径与全部设计必改逐条落实，证据链（契约 JSON↔代码↔旧日志根因）闭合；唯 F1 防御路径半清理，属新引入缺陷。
- **最优**：每帧重申而非 Mover hook、纯删除而非抑制、Land 三重门、`SetGoalNoHaglet` 避开补丁路径，均与设计审查一致，无多余抽象。`PowerSlashByController` 指针复用理论脏读自限（Play 失败仅告警、Land 仍受状态门），不值得加代码。
- **新 bug**：F1（实害、窄域）、F2（近不可达的契约违例）、F3（待确认池边界）；未发现越权写、双写者、重入或泄漏路径；Finale 幂等/token 身份/OnDisable 收口/协程无 finally 的三层兜底核验通过。

## 边界声明

本轮不构成最终验收：新版 LogChoreoClose 行（endX/turnTimedOut）尚无实机样本；live-metrics.json 为旧版数据且其自注“End X 缺失”诚实；测试迁移在途，由 Operator 收口时提供正式测试/构建与 snapshot 差异，F1 修正后走 continuity 复审即可，无需架构再审。未发现被否决方案（捕获网/独立 burst/全局 Mover hook）的回潮。
