# 决策对抗审查回执：features-overview-20260918 派工方案

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent（继承主配置）；模型证据=主 agent 为 GLM 5.3 max + 继承配置。

## 被审决策

用户要求一份"到目前为止 MOD 实现了什么、玩家能玩到什么"的汇总功能说明。拟议：新建 release/MOD_FEATURES_OVERVIEW_ZH.txt（玩家视角摘要层，不改三份现行文档），上午时段派本地 ZCode CLI（edit 模式+窄 allowlist+attachment 任务书），operator 审读+对抗 review。

## 审查 verdict：修订后执行（4 处实质缺陷，5 条修订）

1. 防漂移三措施：文件头定位声明（数值不得与现行三 txt 相悖）；VERSIONING §四同步清单由 operator 补入新文件名；worker 产出 provenance.md 溯源对照表作为抽查底稿。
2. 结构修订：文件头加 30 秒速览节（替代拆双文件）；范围用内联标签不按世界重组；不写版本历史线；限制节 ≤12 行。
3. 重大发现：三份现行 9.4.5 txt 不覆盖农田币成熟加速（PatchEconomy_BankAssistants.cs）、盾墙图腾（PatchWorld_ShieldWallTotem.cs）、大蛇缰绳（PatchWorld_SerpentLeash.cs）等仍在源码的功能——任务书加入"历史线索→il2cpp 代码核验→收录/待裁决"路径。
4. 准确性：候选表述精确为"英雄金箭显示 65%（本体仍 0.9）"；点名历史数值陷阱（射程1.25→2×、自动选举→驿站8币、8项→9项补货、critter 只能写原版大小、散射默认3、头饰是累计配额）。
5. 派发记录：北京时间 2026-09-18（周五）约 10:25，命中工作日上午时段；本地 ZCode CLI `C:/Users/ADMIN/AppData/Roaming/npm/zcode.cmd` 版本 0.16.5；请求配置 GLM 5.3 / max，实际模型证据按 zcode.md 三层事实记录（headless 无 --model，无 runtime model 事件则记 UNVERIFIED）；不用隔离 worktree 的偏离理由=HEAD 落后发布 tag、worktree 拿不到 9.4.5 release 文档，护栏改为派发前后 git 快照（receipts/git-pre-dispatch.txt 已拍，510 行）+ 验收时 diff 只含 allowlist 新增路径。
6. 收尾（operator 验收后自行完成）：VERSIONING §四补文件名、harness-checklist/progress 同步、本回执即为审查记录。

审查者确认：完成上述修订后即可派发，无需再次全量复审。

---

# 修订版方案对抗审查（v2 扩版，2026-09-18 上午追加）

用户反馈：速览喜欢但内容太少、很多功能没介绍到、不需要指向其他文档（目的是发 QQ 群）。拟议扩为自足群发说明。审查 verdict：修订后执行，五条必改（均已在 worker-brief-v2.md 落实）：
1. 发现源扩全量：13 份 MOD_V* 说明+LOG+CAP/GUIDE+il2cpp 文件清单（用户抱怨的缺项大半在 V2–V7.6.5 旧说明）；覆盖矩阵+对账规则进 brief。
2. 修复节两类表述规则（早期修复须源码确认后正面表述；近两版待实测项只写优化/改进）+节末统一兜底句。
3. 安装指引扩为 3–5 行自足步骤（含备份三处）。
4. worker 新开会话（避免 v1 定位指令残留锚定），v1 两条定位指令在 brief v2 显式作废。
5. 验收加 grep 泄漏检查（候选关键词+内部痕迹）与默认值镜像检查。
operator 侧收尾：VERSIONING §四该文件描述由"摘要+导航层"改为自足口径（验收后执行）。审查者同前（内置 subagent，GLM 5.3 max 继承主配置）。
