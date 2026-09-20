# 内容对抗审查回执：MOD_FEATURES_OVERVIEW_ZH.txt

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent（继承主配置）；模型证据=主 agent 为 GLM 5.3 max + 继承配置。

## 审查范围与结论

- 26 条溯源抽查（覆盖全部七节，对照 provenance.md 与现行 txt/源码）：25 条一致。
- 历史陷阱六项（射程 2×/驿站 8 币/9 项补货/Critter 原版大小/散射默认 3/头饰配额制）全部写对。
- 候选节措辞精确、正文零泄漏（grep 证实）。
- 默认开关速览逐条镜像 USER_GUIDE 第二节，无数值改写。
- worker 两个待裁决项均建议保留（北境钱包[跨世界]标签与 GUIDE 口径一致；狂战士缰绳/伤害可靠性有玩家可感知效果，一句带过合宜）。

## verdict：修正后通过

必改（已由 operator 执行）：
1. 农田币句事实错误（worker 被源码陈旧注释误导）：FARM_COIN_MATURITY_SECONDS=3f 与普通币 3f 相同，无"多等留给玩家捡"；历史 12s 设计已被用户否决（git 6ba2b71）。已改为"农田与普通掉落的金币同样按 3 游戏秒成熟后由助手收取"，provenance 行同步更正。

建议（已采纳执行）：
2. 钱袋"UI 视觉放大适配"补"仅当前希腊生效"（GreekScaleScope 门）。
3. 速览"替你猎鹿捡钱"改"替你猎鹿（鹿按原生规则掉钱）"，避免误读为拾取金币。

另记录：provenance 个别行号漂移（BerserkerLeash :7-8→:11 等），不影响实质。

## worker 身份与派发记录

- 派发：2026-09-18（周五）约 10:25 北京时间，命中工作日上午时段，第一顺位本地 ZCode CLI（C:/Users/ADMIN/AppData/Roaming/npm/zcode.cmd，0.16.5，--mode edit --json --no-color；首次派发因 0.16.5 移除 --max-turns 失败，去掉该参数重派成功）。
- 模型证据：请求配置 GLM 5.3 / max；headless 无 --model 且无 runtime model 事件，实际模型记 UNVERIFIED（按 invoke-coding-agents/references/zcode.md 三层事实口径）。
- 改动范围核验：git 派发前后快照 diff 仅新增 release/MOD_FEATURES_OVERVIEW_ZH.txt 与任务目录文件，allowlist 干净。

---

# v2 扩版内容对抗审查（2026-09-18 上午追加）

- 被审：v2 自足群发版（270 行）。审查者同前（内置 subagent，GLM 5.3 max 继承主配置）。
- 42 项新增条目逐一对照源码/基准核验：40 项一致；2 处数值失真均为 V3.1 旧值漂移——弩手守位 4～8→4～7（CrossbowDefense DepthMin/Max=4/7）、面板 880×820→最高约 1040×820（ModPanel.cs:104-105）。已改。
- 修复节措辞：早期修复机制逐一确认仍在源码；近两版项用"改进/优化"合规；建议软化"均已修复"为"均已按历史版本修复记录处理"已采纳。
- 泄漏复查：候选关键词与内部痕迹零出现；默认速览与 GUIDE 第二节逐条一致；覆盖矩阵抽查三份（V4.0.0/V6.1.5/V9.4.5）对账全部成立。
- verdict：修正后通过 → 三处修正已由 operator 执行完毕，文档定稿。
- 附注：银行提款 39/25% 有 2.4 实测记录支撑（progress.md:948），勿按 2.1.0 反编译 30/0.333 误改；忍者间距收紧 e3de41d 已含于 v9.4.5 标签，审查确认 il2cpp/ 与 v9.4.5 零差异。
