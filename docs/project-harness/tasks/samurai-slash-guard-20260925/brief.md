# 任务：武士燕返推倒重做——目标无关的编舞式往返（brief v1，待对抗审查）

用户 2026-09-25 终裁（原话要义）："该解耦的解耦，该推翻的推翻。武士就是固定播放动画，冲刺过去，留下残影又冲刺回来，CD 好了之后范围内有敌人又执行这一套，完全不需要判定敌人到底是哪个最近、哪个死了哪个没死。你是不是做了太多没用的判断，设计得不够简单和本质？"

## 背景与根因（已实机+源码双证）

DCFF5470 实机日志：13 次出程 12 次在 0.1–0.52s 内死于"原生 Slash() 第一行 Mover.Pause(0.5f)"（Knight.Update:417 每帧 `if(ShouldSlash()) StartCoroutine(Slash())`；我方 ShouldSlash 前缀只压 Return 族放行了 RoundTrip）→ ValidMotion 的 `_pauseTimeout<=0` 子句判失效 → finally Finish(Handoff) → 无转身无回程，武士裸站怪堆（无敌当场归还）。用户目击=冲出去不回来+白光看不见（burst 只有 0.1–0.5s，残影 1s 淡出+夜间+人堆）。
深层病根（用户点破）：现行 RoundTrip 是"目标耦合租约"——持有 Follower 引用、随从生死/皮帶/目标被抢全是分支，每条分支都是缝。补丁式修 Slash 压制只是堵最新一个洞。

## 新架构（编舞式，目标无关）

**触发**（Tick 前缀内，沿用现有扫描门但只取三样：存在性/方向/范围）：
- Eligible（保留：风格==2、存活、非 grabbed/inert/玩家控制/登船等**身份门**——这些不是目标逻辑）
- `Time.time >= NextAttack` 且 `ScanClosestEnemy(knight) != null` 且 1.5 ≤ |dx| ≤ MaxRange
- 取 `side = Sign(dx)`（只用于方向，不保留目标引用）；置 `NextAttack = Time.time + Cooldown(3s)`
- `homeX = 当前 x`（回家点=出发点，天然是夜间列队槽附近；不再需要 HomeXOf/StationX 随从分支）

**往返（单协程整段持有，无相位状态机、无租约对象参与 Tick 推进）**：
1. 快照并生效战斗效果：invulnerable=true（记旧值）、trail 钉 1s（记旧值，沿用值纪律归还）、视觉 token Begin（残影 8 槽 recipe C）
2. 出程：触发 PowerSlash；`SetGoal(homeX + side×7, DashSpeed)`；循环至 |x−goal|<0.25 或 1.2s 超时（每 0.1s HitScan 扫走廊，逐敌去重/程）；循环内每 0.3s 重申一次目标（对抗原生 GoToWall 3s 改写的简单反制，无所有权语义）；任一帧 !Eligible（被 grab/石化/死亡）→ 跳到 4
3. 转身回家：`Play(SlashHash,0,0)`（无捕获回退 SetTrigger）；`SetGoal(homeX, DashSpeed)`；同款循环（HitScan 同款去重/程）
4. 终幕（try/finally 保证）：按值纪律归还 invulnerable/trail（沿用 RestoreCombatEffects）；视觉 token End（残影自然淡出）
- 每骑士活跃旗标（HashSet 或字段，try 置位/finally 清除）供三类消费方：①ShouldSlash 前缀压制（扩展为"活跃旗标 OR IsReturning"——堵死 Pause 写手）；②夜间列队 HasActiveMotion（沿用，防 redirect 抢目标）；③Tick 防重入（NextAttack 已cover，旗标兜底）
- **OnDisable 后缀必须收尾**：Unity 停协程不跑 finally（对象失效类停止），OnDisable 路径要归还效果+清旗标+视觉 Clear（沿用现有 OnDisable postfix 挂点扩展）——这是本项目踩过的"永久无敌"bug 类（两租约链式交接被否决的教训同源），P0 级要求
- 取证随船：协程异常终止/提前退出时预算化一行日志（原因+相位+elapsed）；PR#67 的 shader 降级日志已在发布线，同轮实机可判残影材质

**残影临时改 2s**（用户要求便于观察）：SamuraiDashVisuals 淡出时长 1s→2s（常量单点）；8 槽/recipe C/透明度阶梯不动；ready 日志同步。

## 删除面（该推翻的）

RoundTrip 相关：Follower 持有与 RefreshFollower/ValidFollower（**仅指往返路径**；Return/Walk 阶梯的随从语义不属于本任务）、AttackLeash/PullPending、ValidMotion/OwnGoal 对往返的所有权判定（改"到点或超时"）、TurnTransition/AdvanceRoundTrip/DegradeHome 的 Tick 推进与三相位枚举、StationX/HomeXOf 往返分支、OutAbortCooldownAnchor（无中止路径）。
保留面：Return/Walk 阶梯（白天皮带断裂走路回家，独立功能不动）、卡姿捕获网锚点（Begin/Finish 语义保留，挂到协程起止）、HitScan 走廊伤害+逐敌去重（这是"砍到谁算谁"的伤害实现，非目标跟踪）、ShouldSlash 的 Return 族压制、夜间列队模块本身、诊断骨架。

## 边界

- 文件：il2cpp/PatchRoles_SamuraiPowerDash.cs、il2cpp/SamuraiDashVisuals.cs（仅 2s 常量+日志）、tests/samurai-motion/、tests/samurai-visuals/
- 不动：PatchRoles_SamuraiNightFormation.cs（HasActiveMotion 消费方接口保持兼容）、Return/Walk 族生产逻辑、其他模块
- 基线=release/v9.5.13@45b3f79
- 联机口径不变（主机权威 AI，本地门）

## 测试面（判别优先）

1. 编舞完整性：敌人中程死亡/全程存活/无后续扫描——往返两腿都完整走完、回家点=homeX、效果归还恰好一次
2. 触发门：CD 内不触发、无敌不触发、|dx|<1.5/超 MaxRange 不触发、方向=sign(dx)
3. 原生对抗：出程中 ShouldSlash 被压（真前缀）、GoToWall 改写后 0.3s 内重申目标（桩模拟中途改 goal，断言重申）、协程结束后 ShouldSlash 放行
4. 中断安全：出程/回程中被 grab→效果归还+旗标清除+无永久无敌；OnDisable 路径同款断言；连发两轮无效果泄漏（值纪律）
5. 夜间列队：活跃旗标期间 HasActiveMotion=true（redirect 不抢）
6. 残影：2s 常量断言（t+1.9 仍亮/t+2.1 已灭）、8 槽语义不变
7. 红验证：临时删 ShouldSlash 压制→用例 3 红；临时删 OnDisable 收尾→用例 4 红

## v2 修订（2026-09-25 审查回执并入；审查者 agent_20f763b6，APPROVE WITH NOTES 无 P0）

事实修正并入：旗标消费方为**四类**（+BeforeNativeUpdate 夜墙队列门 :209；ActiveCutLeases 被 FrameWatch 引用须按旗标重实现否则编译红）；效果归还现状=OnDisable postfix→Finish（:1323-1330）照搬安全；StationX/Distance/RefreshFollower/ValidFollower 系 Return/Walk 阶梯共享符号只删 RoundTrip 调用点与 HomeTarget；触发在 Update **postfix** Tick（:1342）勿新建 patch 类；在飞采样 TryCaptureSlashPose 是实证捕获主源必须保留挂点；残影头注释与 trail 生命周期声明同步；基线测试数 223。

绑定约束（B1-B9）：
- **B1 终幕 Stop**：效果值归还+token End+RestoreFacing+**mover 仍持我值目标则 Stop**（照抄 OwnGoal 语义仅用于终幕）
- **B2 转身**：ResetTrigger→Play(SlashHash,0,0)/SetTrigger 回退→RetargetFacing(行进向)+SetDirection+localScale y 修复→SetGoal(homeX,DashSpeed)→入帧 HitScan→TurnLog；旗标期全程压制 ShouldSlash（前缀=旗标 OR IsReturning）
- **B3 触发带 [1.5, 8.2]**（=固定7+扫描半径1.2，裁定：保用户"固定 7"模型；出程恒 7 格不追远敌）；测试 9.0/10.5 敌零触发断言
- **B4 旗标四处**：ShouldSlash 前缀、HasActiveMotion(=旗标||Motion)、Tick 防重入、BeforeNativeUpdate 夜墙队列门；Tick 骨架 `if(旗标)return; if(Motion!=null){Return/Walk;return;}`
- **B5 兜底**：协程启动 try/catch 失败即终幕；Tick 硬上限 ~3s 强制终幕+预算日志（原因+elapsed）——替代删除的 3.5s 租约看门狗，封"启动失败/无声死亡→永久无敌"
- **B6 循环每迭代**：!Eligible 或 !Same(k._mover,快照)→终幕；HitScan/重申仅 timeScale>0
- **B7 快照存 ActorState**（OldInvulnerable/OldTrail/OldTrailTime/homeX/side/StartX/旗标）；出程循环逐迭代 TryCaptureSlashPose；OnDisable postfix 同一终幕+清旗标+Actors.Remove
- **B8 测试**：先产出存活/重写/删除清单（预计 55-70 例重写/删除、~150 存活）；翻转 pause/外目标/换 mover 三族断言（pause→迟滞不终幕、纯外目标→0.3s 重申夺回、换 mover→终幕零写入）；新增 NativeSlashAttempt 桩（真前缀+放行时 _pauseTimeout=0.5 写断言=根因判别器）；红验证三件=删压制→前缀放行红、删 OnDisable 收尾→泄漏红、删 0.3s 重申→夺回失效红
- **B9 杂项**：保留触发门 timeScale>0&&_pauseTimeout<=0+IsDamagedBy 复查；旗标用 ActorState 字段非 HashSet；残影 Lifetime 1→2s+头注释+ready 日志同步；NextAttack 双语义（触发成功+Cooldown/落空+ScanInterval）
