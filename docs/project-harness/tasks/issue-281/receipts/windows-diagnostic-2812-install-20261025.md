# 2026-10-25 — Windows diag.281.2 配套安装（保存语义仍未修）

用户已结束上一轮错误游戏。重新只读核对游戏进程0，25个save/config与首错后副本逐字相同；现有444项固定游戏/运行输入全匹配。只在E:独立2.4.3玩家包更换诊断DLL与受管启动JSON，不写Steam、Mac包、正常配置或正常存档，不启动或操纵游戏。

- 版本 `10.9.39-win243-diag.281.2`，源码增量提交 `98a36032bc9418651af082ef20d8d030e9289c1b`；仍保持已安装Windows的旧冻结基线及2.4.3适配，不是Mac v11全量源码升级。
- DLL `16a0ddb5…` → `a3d49aabd6652db244be21be301c522871c3a39dfce537d3fc34d6606f14d83c`，4527104 bytes。
- 受管JSON `82cc9e04…` → `1e09caee5396754a6ad8877fe84d9a0037b3e13d102f7c51813dda4900f48ede`；版本、来源摘要、单个DLL payload及pluginUpdate更新，previousUpdate完整保留旧出处，原guard和其他443项保持。
- 旧DLL、旧清单、25项完整save/config及现场日志备份：`E:\ohmymods-backups\issue281\install-20261025T171655Z`；独立读回原件、每份数据copy的SHA/size均符。没有自动恢复玩家数据。

新冻结脚本 `b12c565528fcbb9c1b6560f0c7fc6f6f30ada2e00ea12bdc91b49ebb6d3cd9c8`，TTL至18:01UTC，一次case，不复用旧单DLL/单清单脚本。原worker两次有界运行exit1，产出的有效脚本/静态检查由Operator接收并补齐三函数增量，原件保留，没有冒称worker独立完成。独审R2、实际OMP `zhipu-coding-plan/glm-5.3`（请求max、公开模型事件核验）具体执行关口通过；实际Windows PS5.1 Parser0、系统PowerShell微软签名Valid、19/19本地针对性静态/模型检查。构建和产品测试沿用未变化的281.2准备证据，没有为纯安装重跑玩法测试。

实际两文件File.Replace及原启动器固定`-CheckOnly`完成：exit0/PASS/新版本，子进程60秒有界，无游戏启动。成功后独立只读核对：新DLL/清单SHA一致、原PS1/Cmd保持、444项固定输入匹配；25个当前数据文件与前像及完整备份全部相同，游戏进程0。CLI stderr包含PSHOST information序列化，实际exit0和receipt均成功，不把这些信息当错误。

安装只增加当前主船归属、严格子级/self、同根持久组件与登船状态的只读诊断。**没有修复保存语义、没有关闭CaptureFault、没有恢复或删除职业记录**。用户仍需按原`ohmymods-win243-preview.cmd`进入同档、开船去第一岛，首次错误停住收集新进程日志；船归属闭合和职业交接修复、实际保存退出冷重进尚未完成。PR #282仍Draft、Issue #281开放，不合并、不发布Windows Release。
