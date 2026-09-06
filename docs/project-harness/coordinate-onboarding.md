# Coordinate 接入约定

`docs/project-harness/harness-checklist.json` 和任务 plan 是本项目的文件 authority；Coordinate 管理执行 job、lease、event 和 completion receipt。已有游戏条目保留其原状态，不因注册 workspace 自动成为已采纳或已验收的 managed task。

`scripts/harness/` 来自公开 EXharness `8449a797b3a15d66f024cf030bf46b44a6423fdf` 模板，仅替换项目名、harness root 和脚本目录深度；MIT notice 随文件保留。配置不自动运行游戏 build、test 或 deploy。

在项目根目录可用 Python 验证与查看状态：

```powershell
python scripts/harness/validate_checklist.py docs/project-harness/harness-checklist.json
python scripts/harness/session_init.py
```

也可在已有 Bash 环境使用 `bash scripts/harness/harnessctl`。Windows 的 Python 解释器和 Coordinate `[mcp]` 环境由本机安装配置提供，不在版本库保存个人环境路径或 credential。

重要新任务使用 Coordinate `task create-files`，部署已提交文件后由 Remote MCP `task_create_record` 登记；不得直接编辑 JSON 绕过 split-operation。scope、principal 和部署读回位置由受限的 Operator 配置管理，不进入本仓库。

review/closeout 可通过现有 Python 入口生成 packet，review-result 需绑定 reviewer 实际读取的 packet SHA。正常跨主机完成使用 `coordinate assignment mark-done-files` 的 Remote MCP transport，必须在线 preflight/claim/apply；禁止把 repair-only 当正常完成。未部署的当前文件、过期 receipt 或 fingerprint 不匹配都应停止并报告。

本次接入审计 task 为 `coordinate-release-audit-20260906`。其报告只验已提交的发布材料一致性，不替代游戏实测。其它游戏任务的开发与最终接受仍由原 Operator 和用户负责。

Git attributes 将 harness 的 Markdown/JSON/JSONL 和实例脚本固定为 LF，保证 Windows 文件与服务器 Git 读回的 canonical bytes 一致；不改变游戏源码的换行约定。Python cache 不进入版本库。
