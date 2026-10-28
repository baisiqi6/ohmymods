# EXharness 入口与本地 Git 治理（2026-10-28）

用户已明确要求登记三个 skill 入口并清理 Git 资产；本轮由 Mac 主 Operator 负责。

- 三个官方入口已在 EXharness `main@6266856` 逐项读回：`long-running-project-harness`、`git-workspace-governance`、`invoke-coding-agents`。AGENTS 只登记链接与职责，保留项目规范优先和未安装时的现有流程；不复制 skill 全文、不更改全局安装。
- 对本机 70 棵既有工作树完成只读体检。已用非 force refspec 将未检出的本地集成分支从 `9ea703f` 快进到 fresh `origin/release/v9.5.13@d7e10fe`；主 checkout 的分支、HEAD 和工作区内容保持。
- 主 checkout 的 8 处 tracked 改动与 2 个 untracked 文档已作最小私有备份：53,126 字节 unstaged binary patch、空 staged patch及两份文档。独立临时 index 恢复检查和文件 SHA 回读通过；未复制依赖、构建产物、游戏或玩家数据，未 reset/stash。
- 用户另已确认退役四棵旧树及对应本地分支：`musketeer-legacy-compensation-20261013`、`shield-gait-art-20261014`、`test-resource-policy-20261018`、`mount-map-lifecycle-20261019`。四棵均干净、HEAD 被远端集成主线包含、无 local-only 提交，ignored 只有可重建产物；当前进程及已安装构建输入清单核验未发现依赖。
- **实际退役尚未执行成功**：四次正常受管归档均返回 `Another active task still uses this worktree.`，因此保留四棵和全部对应分支，未换用强制删除或其它入口绕过检查。远端分支未改。仅“进程 cwd 没有命中”不能替代应用层任务归属核验；需先解除实际占用再重试。
- 在途 PR、Android/DSH 树、未提交内容、独有 ignored 证据/配置，以及当前安装/性能候选依赖的源码位置均保留。具体本机路径、清单、备份 SHA 和模型事件留在私有任务目录。

验证范围：官方链接读回、Git 包含/状态核验、最小备份恢复检查及文档差异审查。不涉及 Mod 源码、游戏构建、DLL 安装或存档改写；Windows 性能验收仍独立待办。Issue #303 在退役阻断解除前保持开放。
