# 决策对抗审查回执：identity-restore-hardening-20260920 任务书（A+B）

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent（继承主配置）。完整审查报告存于会话，本文件录 verdict 与必改（全部已并入 worker-brief-v2）。

verdict：修订后派发。10 条必改：
1. B 限定 knight 侧（musketeer 证据门不可支持：unresolved 时预约 NativeId 恒空无法枚举当前盘个体；唯一可写变体会永久悬空已付职业生涯=资损）；musketeer 只做 A，unknown-paid 保持。
2. B 触发锚点在 ApplyCapture :987 CanFlushSeed/:989 Owners 早退之前（否则典型吸收态永不触发）；当前盘个体从 island.objects+RecordIsKnight 枚举；历史并集=该 context.Epochs 按 uniqueID。
3. 触发 Kind 精确=="known-mismatch"，排除 legacy-pending/conflict。
4. 再基线一律新建 epoch（Active epoch 满 8 快照会逐出最老，违反"旧快照保留"）；成功后必须 RememberBinding(newEpoch)+ConfirmContext(false) 防同会话重复建 epoch。
5. 携带身份规则：同 uniqueID 历史收据全一致才携带，否则丢弃走 fresh（绝不猜）。
6. "本会话重绑"降为可选；验收=下一会话 load-match kind=exact。
7. A3 重试仅限 IoFailure 类 Failed（Refused* 不重试）；musketeer 重试亦出现于 load 栈需注明。
8. A1 musketeer RecoveredBackup 永久只读记为已知边界（无 RecoverMainFromBackup 等价物）。
9. allowlist 补 VERSIONING.md（跨模块可靠性 +0.0.4）；零 schema 变更（用既有 v2/kind2）。
10. 测试补 6 项（零 owner 吸收态门可触发/绑定更新回归/容量耗尽 fail-closed/收据不一致丢弃/Refused 不重试/musketeer unknown-paid 守卫）。
已知活性边界（须告知用户）：严格子集门下，失败写入后、首次自愈保存前新招募的骑士会使门永久拒绝该次自愈（自愈窗口关闭），属设计取舍。
