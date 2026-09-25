已阅读 Production code、frozen diff、stubs、worker receipt 和 operator log。正在对剩余的审查点进行最终验证 grep：确保 choreo paths 中没有残留的 full-gate 调用、test count 以及 consumer-site inventory。
## 审查结论：**APPROVE**

范围：`il2cpp/PatchRoles_SamuraiPowerDash.cs`（1483 行终态）+ `tests/samurai-motion/Program.cs`，以冻结 diff (`motion-final.diff`) 为准；证据为本树 `motion-worker-result.md` + `operator-motion.log`（216/0，基线 198/0）。

### 三问

**正确** ✔
- 硬失效子句集合与 handoff 逐项一致（token/owner/gone/disabled/inactive/config/authority/style/mover/body/inert/grabbed/dead，`ChoreoInvalidClause` :998-1015），无增删。
- 6 处消费点全部收敛到单一函数：Tick:878、协程三处 :1116/:1125/:1138、命中三处（经 `ChoreoValid`）:1212/:1218/:1223。grep 证实 `Eligible` 仅存于夜队列 :216、撤退阶梯 `ValidMotion`:280、Tick 掉落块 :885 与触发门 :890——后两者在 Choreo 存活时不可达（:883 注释成立）。
- Tick 硬帽绕过软门但硬失效可收尾：:877-882 硬门在 `else if` 才查 3s 帽，软旗无法抢先 return；`hard cap ... died under a soft flag` 测试（Program.cs:748）用 `StopOwnerSilently`（无 finally）+`isRetreating=true` 实锤该路径。
- 双写者消除：软失效不再 Remove 状态（旧 `Finale("ineligible")` 路径整体删除），协程存活期间 Tick 在 Choreo 块内必然 return。
- 重入安全：Finale 幂等（token 身份）；Tick 硬失效→同帧协程 break→`ChoreoAlive` false 静默；OnDisable :1445-1447 先 Finale 再 Remove；实例 id 复用由 "owner" 子句闭合且对旧骑士归还效果（token 持旧引用）、新骑士零继承（专测覆盖）。
- `TurnChoreo` 顺序核对：animator→HitObjects.Clear→RetargetFacing→SetDirection→ChoreoGoal→**命中扫描最后**——命中回调内 OnDisable/Finale 不会造成 Finale 后再写 facing/goal。
- mover 替换：:862-869 先 `Finale("mover-replaced")` 再重置 `ObservedMover`；`ChoreoOwnGoal` 的值纪律（位置+速度双近似）保证不误停池复用后新主人自设的目标。
- 命中不被软旗静默：回调内翻 `isRetreating` 后第二目标仍落伤害、行程存活（专测），每腿去重边界保持（1→2 不超）。
- Finale 收尾日志独立 try（:1246-1248）：日志异常不可能跳过值纪律归还——正是永久无敌类的最后缝隙。
- Return/Walk 语义零改动：`ValidMotion`/`AdvanceReturn`/`BeginWalk`/`HitScan`/夜队列全部原样，仅 Tick 内编排顺序调整且仅在 Choreo 存活时改变行为。无动画反写、无新 hook/配置/扫描。

**最优** ✔ 单一谓词函数 + 子句名进有界日志（`step=hard-invalid:dead` 可现场定位），比布尔 `IsValid` + 散落检查可诊断性强；diff 最小（+77/−22）；触发门与撤退阶梯保持完整门不妥协，边界划分与用户裁定严格对齐。

**新 bug** — 未发现 P0-P2。逐一排查过：destroyed 对象字段读（均在 try 内，"gone"子句可达）、timeScale=0 期间 `Time.time` 冻结导致帽/腿窗冻结（既有语义）、自然完成 ~2.4s 对 3s 帽留 0.6s 余量（Tick Postfix 先于协程恢复，不会抢close）、`ChoreoGoal`/facing 写均在 Finale 之前、`StartChoreo` 只从触发块（Motion==null 保证）进入。

### P3（不阻塞）

1. **P3-1（日志装饰）** `ChoreoInvalidClause`:1000-1003 — `Same(a.Owner, k)` 先于 `k == null` 守卫，null/已销毁 k 记为 "owner" 而非 "gone"。无行为差异（两者都硬收尾）。最小修正：把 `k == null || k.gameObject == null` 提到 owner 判定前；可不做。
2. **P3-2（回执账目）** `motion-worker-result.md` §2 记 214/0，终态树 216/0：diff 中 "A retreat onset during an outbound hit callback…" 与 "Retreating first becoming true on the home leg…" 两条不在该回执改动表内（回执落后一个迭代）。代码无影响——operator-motion.log 216/0 是终态树的权威证据；建议回执补一行说明。
3. **P3-3（已授权边界，仅留档）** `isStationary`/embark 中途翻真时行程按腿窗/总帽在当前位置附近收尾（可能未到家）——handoff 明示承受；style 瞬时 rederive 失败硬收尾、暂停期帽走缩放时间均为既有语义（回执限制 §5.6/§5.7 已如实记录）。

### 测试判别力

红验证（旧 `Eligible` 恢复→198 绿/16 红，且恰好全部是新契约用例、契约保持项在红态仍绿）证明新用例判别的是门语义翻转而非镜像实现；断言对象全为可观测结果（回家坐标、命中数、goalMode、效果归还、日志子句），非源码文本。6 条旧契约翻转全部可映射到用户裁定（软旗不切两腿/队列不静默命中/回调内控制不截断命中帧），无越权新增需求。

### 不可宣称项（与本轮一致）

主工程与视觉文件联合编译、实机两腿一气呵成/墙外命中/联机——均未验，按 brief 留给 Operator 统一执行；本 APPROVE 仅覆盖冻结 diff 的逻辑与套件证据。
