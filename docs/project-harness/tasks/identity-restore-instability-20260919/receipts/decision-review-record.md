# 决策对抗审查回执：identity-restore-instability-20260919 诊断（两轮）

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent ×2（继承主配置）。

## 第一轮（四类机制 A-D）：诊断可呈报（含修订）
量化修正（弩手15s最大窗/骑士典型1.5s最坏6.5s/暂停时钟放大器/联机客机10-30s）；引用修正；补第4类（弩手身份不持久换人、火枪铺默认off重装、联机门）。

## 第二轮（玩家新事实：非自愈、重进抽奖式）：诊断可呈报（含重大修订）
- **头号嫌疑 H4a 吸收态（operator 漏，审查发现）**：一次性 sidecar 写失败（File.Replace 被杀软/锁拒）或 legacy 失配会话 → `save-preserve-unresolved` 全程拒写（KnightIdentityRuntime.cs:1022-1027）→ 原生自动保存推进盘 JSON → 存储 hash 永不再命中 → 该 context 永久 unresolved。解释"抽奖→一直坏"演变。
- H1（v8→v9 legacy 时钟漂移）成立但对同档重进是确定性的（首启定局）；v8 scopeKey 掺 realStartDateTime 使每会话新 scope（128上限）放大。
- H2：musketeer 读 IoError 静默且不查备份（MusketeerArchive.cs:400-414）=唯一真逐启动抽奖路径；knight 侧有告警+主备护栏。
- H3（加载竞态）基本不成立（Resolve 纯函数）；单机/联机切换是未覆盖交替源。
- 修复优先级：①H4a 吸收态再基线化（证据门防回滚误吞，放宽 fail-closed 需用户拍板）②低风险快赢：musketeer 读失败日志+IoError 查备份、load-match 补 MatchKind ③H1 只能人工 claim 工具 ④收集清单六项（失败+成功启动日志、ModSave 目录清单含 .tmp-* 残留、版本史、联机/渡过自动保存日、sidecar 本体、land 编号）。

本任务为诊断阶段，未实施任何代码改动；修复立项待用户选择。
