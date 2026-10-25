# Issue #281 — Windows 2.4.3 出航灰图与保存拒绝

基线：`release/v9.5.13@98259aff338bb9e5ee1b666e4f5c67da24eed6fe`；分支 `codex/windows243-voyage-repair-20261024`。仅 IL2CPP，未修改 Mono、Steam 或其他任务改动。阶段一交付菜单修复和只读诊断，**保存语义修复仍未完成，Issue 保持开放**。

## 当前证据与责任

- Windows 原生 `Menu.set_interactable` 同时写内部标志与 root CanvasGroup；`Hide` 裸写内部标志，不恢复 root。原补丁关闭自己打开的菜单时只 Hide，遗留 root=false；航行直接 ShowMap 后全岛不可点。旧 fixture 只有一层状态，原21项通过未覆盖该错。
- 独立实际日志首错为 `save-missed-row → CaptureFault → sync-reject → _ShowSaveFailurePrompt`，发生于来源岛8出航保存、目标岛生成前。原生 writer 被保护拒绝，磁盘 global 与此前相同；不是已证实的磁盘写失败或坐骑生成失败。
- 2.4.3 PE 证明 Save 枚举 registeredPersistents 并检查 `persistObject && _persistInstance && (activeInHierarchy || persistInactive)`。出航会注销部分船子对象；carryForward 保存人数/工具计数，不足以证明已付费 career 身份承接。缺失对象尚未知，不能跳过、降 reservation 或绕过 writer 保护。

## 阶段一实现

**菜单**：在实际 gate 写入边界分别捕获内部标志和 root 组原值，绑定 Menu/CanvasGroup 的原生身份。owned-close 只返还自己修改的同一 root 组，内部保持 false，再 Hide；user-owned shown 先恢复内部标志、再 root，hidden 只还 root。异常保留同实例责任，不增加 setter-true/re-gate 补偿链，不强开所有按钮。真实 partial-Hide 反例先红后绿；不外推面板仍开时被联机/脚本强制切入 SailingAway 的交错。

**保存诊断**：仅 `VOYAGE_SAVE_DIAGNOSTICS && !ANDROID` 编译，复用现有 Begin/GetID/Apply 边界；不主动调用会写缓存的 GetID，不新 detour，不改变 Capture/Archive/Stage/Fault/writer 结果。记录 exact career、原生 Persistent 资格和已有 GetID 观察；读取失败明确 unknown/incomplete。每进程最多4个 capture、40行、64KiB自有诊断消息，首个缺失对象优先。船父链/完整职业交接关联尚未实现，不把诊断不完整当作“不存在”。

## 验证与审查

- 独立 host：panel **37/37**；identity default **659**、diagnostic **779**（包含 targeted **127**），均通过，0W0E。read-fault 反例已修，不影响原拒绝与存档字节。
- 完整 2.4.0 / 2.4.3、default / diagnostic 四种 net6 私有编译均 **0W0E**。2.4.3 default 的27个保存相关方法与未改基线 normalized IL 相同；默认产物不含诊断类型。
- Android host probe 构建0 errors，AdapterTests **1265/0**；未改 frozen pin，未启动 AVD/设备，不称设备玩法通过。
- 阶段方案/增量决策：OMP `zhipu-coding-plan/glm-5.3` / max；实现：OMP `deepseek/deepseek-flash` / max；独立代码复核通过；当前 PE 保存路径另由 `gpt-6-astra` 只读核对。精确源码/产物哈希见 receipts。

## Windows 手动诊断窗口

2026-10-24 用户明确允许“用当前正常版手动测，先备份，允许游戏读写当前存档”，替代本轮“正常存档不变”的数据边界；测试账户登录仍暂停，不切桌面/扩权。

最小候选以实际已安装 `10.9.39-win243-preview.1` 冻结源为底，仅替换上述两文件、新增诊断文件及版本信息，保留旧资源与2.4.3适配。先核对无游戏进程、二进制版本与路径、完整备份 Release/配置/旧DLL并校验，再经本轮安装决策审查替换独立 E: 玩家包。主 Operator 不启动/操控游戏、不写或自动恢复正常存档；用户手动出航，首个错误即停，按新进程出生时间/路径和候选哈希只读抓日志。当前候选不能保证测试进度能保存。

待办：抓 exact missing career，确认原生资格变化与职业承接契约，再提交有依据的保存修复；随后验证首次出航、坐骑生成、保存退出、新进程重进。未合并、不关 Issue；安装与实机另记，不把 host 结果当 Windows 实机结果。

## 2026-10-24 诊断安装回执

最小 Windows 诊断候选 `10.9.39-win243-diag.281.1` 已安装，SHA `16a0ddb5d508997595019cc14d5bb6faa96908522977cd7cd3ca75f6ec2cc4e7`。25个存档/配置文件共11979487 bytes先完整备份并逐项复验，写后数据保持，未自动恢复或启动游戏。安装中 stdin 大块传输与PS5.1空备份参数两次失败均保留原件；旧DLL/数据未改，改用SCP与显式原子备份路径后新apply成功。审批、脚本增量和安装回执分开绑定；见[安装摘要](receipts/windows-diagnostic-install.json)。已接上只读新进程采集，用户手动出航尚待完成，不称保存问题已修或Windows实测通过。

## 2026-10-25 首错收敛

已用实际Windows诊断DLL取得来源岛8保存首错，首missing unit在begin时已active但原生不持久/未注册；主档未写，Hero/Knight附档有快照轮转，原件完整保留。详见[现场记录](receipts/windows-first-failure-20261025.md)。原生当前主船递归排除已静态核验，尚缺本root归属；不跳过保护或猜职业运输。只读boat-scope增量已完成本机准备：default659/diag857、四种interop0W0E及默认IL/资源等价；源经Astra增量复核，候选未安装。旧“具体career尚未知”仅代表10/24，已由本次观测替代；尚未闭合完整出航身份交接。
