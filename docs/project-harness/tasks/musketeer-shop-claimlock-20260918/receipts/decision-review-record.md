# 决策对抗审查回执：musketeer-shop-claimlock-20260918 诊断与修法

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent（继承主配置）；模型证据=主 agent 为 GLM 5.3 max + 继承配置。

## 被审决策

用户报告 bug（买第二把枪常被"平民拾取完成前"锁住）的根因诊断与修复方案。

## 审查结论：修订后派发

- 诊断**确认正确**（全部行号核对）：认领窗口双重锁（RackCount :799 返回满 + Reconcile :66 置 _rackLayoutReady=false）；原生侧补强证据=Peasant.SetDroppableTarget（Peasant.cs:377-388）直接赋值 friendlyClaimer 且无超时，窗口=平民走过去全程（toolSpotRange=20）；"有时候"无其他可信解释。投币中途被锁不吞钱（OnPay :419 复查+退款 :425-427）。
- 修法确认为**最小协同集**（比三个替代方案都优）；补强证据：修复后商店口径与 LiveCounts/补货计数口径（friendly-claimed 算架上枪）统一，MusketeerRestock 复用同门同受益。
- 三点修订（已并入 worker-brief）：①同步改 tests/musketeer-side-rack 两条将被翻转的旧断言（:28/:42）为新契约；②划不修边界（enemyClaimer/pickedUp 整体 fail-closed 与 StockRestores 门不动）；③验收补记：平民消失后认领滞留从"整店锁死"降级为"占1槽"，属改善。
- 逐项风险评估：_rackLayoutReady 语义变化无外溢（全仓仅 4 接触点）；claimed 未落架占槽计数正确（spawn 即精确槽位）；_nextRackLayoutAt=0 无竞态（单线程，marked 成功后置 0）；新边界"3把全认领锁第4把"物理合理。

派发：2026-09-18（周五）约 11:30 北京时间，工作日上午时段，本地 ZCode CLI 第一顺位（0.16.5，edit 模式）；实际模型证据按三层口径记 UNVERIFIED（headless 无 runtime model 事件）。

---

# 版本考古补充（2026-09-18，回答玩家反馈"9.0.0 没有这个问题"）

对比发布 worktree 证据：
- **v9.0.0**（release-900/clean，MusketeerShop.cs:787-796）：RackCount 是简单计数——遍历已注册枪，`!pickedUp && InWorld && StockSlot>=0` 即 count++，**认领中的枪照常计数**，无 claim-lock、无布放回执门。买一把被认领后 count=1<3，可立即买第二把。
- **v9.4.5**（release-945/clean，:795-801）：侧枪架重做（musketeer-side-rack-20260917 候选，随 v9.4.5 发布）引入"认领即满容量"+"未落架即满容量"+Reconcile claimed 置 not-ready 三重 fail-closed——**本 bug 确为 v9.4.5 引入的回归**，玩家反馈正确。
- 本次修复的"claimed 按占位计数"语义与 v9.0.0 的正确行为一致（恢复而非新设计），同时保留 v9.4.5 对真异常（unclaimed 未落架/身份不可读/敌人认领/已拾取）的 fail-closed。
