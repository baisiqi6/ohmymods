# 决策对抗审查回执：mac-trial-945-20260919 任务书起草

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent（继承主配置）；模型证据=主 agent 为 GLM 5.3 max + 继承配置。

## 被审决策

按用户（经侧聊转达）提供的草稿，起草给 Mac 端 agent 的 v9.4.5 试跑任务书并落盘。

## 审查 verdict：修订后可用（5 必改已全部执行，4 建议项已采纳）

必改（均已改）：
1. **三条 grep 串全部无法匹配真实日志字节**（原写 `[BepInEx] Loading...`/`[Info :...]`，实际前缀是 `:   ` 三空格对齐）——改为消息体固定子串；`Enabled=True` 放宽为 `Enabled=`+人工核对并记录（False 本身是异常信号）。
2. **ZIP 使用改为单文件白名单**（只复制 `KingdomEnhancedMod.dll`），明列禁入项（Windows be.752 core / x64 dotnet 运行时 / winhttp / doorstop / unity-libs / config），禁止整包解压——防 Windows 残留污染 Mac 侧制造假失败。
3. **冒烟举例错误**：君主移动速度默认 2×且生效门槛 Enabled&&Multiplier>1，是默认开不是默认关——换成散射/长按购买/无限体力/补货等真实默认关项。
4. **BepInEx 门槛事实修正**：用户草稿"≥#754"有误——经 builds.bepinex.dev 核实，Unity 6 IL2CPP metadata v23-106 支持是 **#755（2026-03-07，PR #1284）**；#754 只是 Doorstop 4.5.0。精确取 #754 会失败。任务书改为 ≥#755 并附"Cpp2IL 反复回退期间直接取最新 bleeding+记录构建号"。此修正改变用户草稿数字，依据为官方 builds 站点证据，特此留痕。
5. 流程落账：本回执即审查记录；harness-checklist/progress 已登记。

建议项（已采纳）：ZIP SHA256 钉死（8961fd17...）供 Mac 校验；阶段一补 interop 联网下载失败按环境问题判定（防误判裸加载器失败）；回执加 Mac Runtime 版本与 DetourProviderType；Gatekeeper/quarantine 一行提示（并入"按官方 macOS 指南安装"）。

## 其他核对结论

用户草稿技术要点（三阶段止步/两侧 be 版本差异预期/禁重编译/成功标志/异常栈记录/存档备份/不公开日志）逐条忠实承载，无擅自更改方案（除第 4 项有证据的事实修正）。插件目标框架实为 net6.0（ProjectSettings.shared.props），与 be.754+ runtime 兼容无风险，任务书已注明"AGENTS 的 .NET 8 指构建 SDK"。ZIP 完整性与发布回执逐字节一致（DLL 0F8C1FC8）。

回执等待中：Mac 侧试跑结果由用户转交后汇入本目录，任务保持 doing。
