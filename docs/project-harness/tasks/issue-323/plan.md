# Issue #323：内嵌资源测试包联网冷启动

## 目标与范围

保留既有 Android 移植成果、唯一主 Mod 和小浮球。私有一体化测试包应可在正常联网时启动，玩家无需手动禁网。用户已明确排除升级留档支持与验收；测试数据备份仅用于回滚。PC、冻结 Mono、支付授权、公开商业本体分发和全功能/多人/DLC验收均不在本轮范围。

代码起点为 `release/v9.5.13@cb9b7f9d56f9dc5a03b55f34d66c78910b73346f`，专项分支 `codex/android-embedded-startup-323`。实际游戏是 Android 2.4.0 / code23485 / Unity6000.0.61f1，不能按旧下载文件名宣称2.4.2。

## 根因与修复点

当前 ARM64 `ProgramDirector` 启动状态机在 Unity 判断网络可达后，因 `AssetPacksPresent` 为假而选择外部资源获取分支并等待；无VPN的真正离线启动可进入世界。固定本体承接此前离线可玩及内嵌资源基线，22147个文件的数量本身不证明所有DLC/关卡可用，本轮只验证原资源内容保持。

打包时仅将 VA `0x26306f0` 的 `tbz w0,#0,0x2635068`（`c04b0236`）替换为 `b 0x26306f4`（`01000014`）。该继续点也是原离线/Editor路径，前一个资源getter调用保留。ELF可执行PT_LOAD映射至文件偏移 `0x262c6f0`。不改全局PAD ready、联网判断、整个协程或授权逻辑，不新增运行时hook、扫描、重试或兜底。

曾考虑短getter hook；固定loader的Dobby原审计表明写入只有4B，不能称其必然越界。本轮打包期单指令方案无需新增该运行时依赖。原先手工断网只是诊断/临时操作，本轮不把它包装成新自动禁网机制。

## 冻结输入与实现

| 输入 | SHA256 |
| --- | --- |
| 原私有APK | `ef575e6c4fd14a473efd6aeacca61bd22ca4d401ca18721e61f918428d833a4e` |
| 原libil2cpp.so | `774871434d39fe5145f05c9dc51101d973f0bf7dc63bba0f062d7c0848104157` |
| 原global-metadata.dat | `ef1cc0cce412d3f3402cbfcb5a5e2ef1ca0d6ff47c4a90a705909f8301deae19` |

`scripts/prepare_android_embedded_startup.py INPUT OUTPUT` 仅接受上述固定输入，进一步核对ELF映射与原指令。未知版本、split、缺资源或漂移输入拒绝，不开放任意指纹/偏移选项。全部校验后才写同目录临时ZIP；去掉旧META-INF签名且不复制APK signing block；独占原子发布新未签名文件，失败清临时文件，输入与既有输出保持。随后复用现有LemonLoader Patcher的production deployment、签名和对齐流程，携唯一主DLL `6d480d05d3f6a714b654fe29e4bba553dcd006d519802bdd08135bf6d111d18a`，不携诊断probe、测试配置或UserData。

## 验收与回滚

- Host正反例：指纹、ELF/指令漂移、缺entry、路径重叠、既有输出、实际写出故障及并发发布拒绝；原件保持、临时文件清理、旧签名移除。
- 真实输入：解压后的native只有4个授权字节变化；不是整个APK只变化4B。除旧签名/native之外的原entry内容CRC/长度保持，全部ZIP CRC通过；最终原游戏资源保持，加载器/其他native与已跑通的loader包比较。
- 最终包：production唯一main、实际mainSHA、原INTERNET权限保留、同测试证书有效签名及16KiB ZIP对齐；新interop绑定修改后的native。
- 平板：先核对当前测试包与最新files备份逐SHA，再fresh安装；全程不手动push DLL/config/runtime。真正联网的首启和二次冷启动需进入世界，核对实际main、SettingsReady与职业注册owners=133、InitPools、浮球/中文UI/基本移动；二次不重复部署或覆盖用户设置。不把WiFi开但未连接视为联网。
- 如果失败，保存实际包/网络/日志，再恢复既有已跑通包和本轮匹配数据备份；不扩展补丁去遮蔽其他独立错误。网络状态恢复原态。存档升级留档、长时间玩法和未覆盖功能不宣称已验。

## 受管登记

主Operator已确认文件写窗无冲突。2026-11-23T07:51:46Z，Coordinate `task create-files` 在专项树成功登记 `android-embedded-startup-323`，operation `caa576d9-7987-4b18-a7e4-319de8c4681c`，validator 0 warning；文件写窗已释放。当前仅file半边，不代表服务端record、assignment或完成；精确提交部署后沿同operation补record，standing profile无serverassignment权限。本轮本地测试沿用户直接授权，不借旧任务operation或runtime gate。

## 审查、验证与当前状态

- Kimi `kimi-code/k3.1-pro` / max独立决策链复审APPROVE，公开runtime事件确认无fallback。修订明确资源结论边界、4B解压内容范围、原子输出与旧签名清理。
- gpt-6.1-sol/high Worker按北京时间工作日下午派发，先记录预期红例再实现。Root与独立gpt-6-astra/high各自运行12项host tests全通过；源码独审APPROVE，复核真实控制流与打包边界。命令：`python3 -B -m unittest discover -s tests/android-embedded-startup -v`。
- 真实中间APK为 `106f2fd0…`，native为 `8e4b334b…`，仅4B变化；所有其他原entry CRC/size保持，22147游戏数据文件保持，旧签名移除及ZIP CRC已核验。
- 最终私有候选APK `94b41bea78b96cd897c060ba532ad8e5da074b68f003548a06c6a5f34d495e92`；同证书签名、对齐、CRC、唯一main与production、其他native/loader同既有可玩基线、INTERNET权限均已核验。Kimi exact候选安装决策已APPROVE，安装前独立重算候选/回滚/现场包SHA并校验最新259文件备份后fresh安装成功。首启没有手动push DLL/config/runtime，production自动部署installed1；实际mainSHA匹配、SettingsReady、职业注册owners133、main/InitPools/blocks、浮球和中文面板、日历开关、基本移动及保存已观察。二次联网冷启约19秒main、21秒InitPools，未重复部署，用户配置与本轮保存文件SHA保持，Melon主日志Exception计数0（Unity系统日志仍有下述Ghost异常）。两次均保持真实WiFi及FlClash VPN连接。

根因原件位于项目 `.local/tasks/android-startup-delay-20261003/`；本次静态证据、host日志、审查和打包回执沿 `.local/tasks/android-profession-archery-20261014/device-startup-20261122/{startup-resource-audit,issue-323}/`。原件、设备标识、原始日志、APK与私钥不进入Git。只交本任务脚本、测试、plan/checklist和受影响进展；必要审查验证后正常commit/push/PR，代码合并、受管收尾与实机结果分别记录。

## 本轮运行边界与剩余

- 源码的133计数是职业战斗注册owners，不是设置总数；SettingsReady另有真实日志。此前过程中的简写已按实际marker纠正。
- APKVision第三方更新提示在两次启动均出现，取消后游戏可进入世界；首轮由用户点击Cancel，二轮由Operator取消。没有下载或更新，也未删改第三方SDK。本轮解决资源等待，不把第三方弹窗消失写成完成。
- 既有Melon依赖提示和Scene build indices warning仍保留；实际主Mod及Il2Cpp支持模块已加载。Melon Latest.log未观察到先前商店NRE，但补查Unity系统logcat发现两轮各6条 `Kingdom.GetGhost → GhostHintTrigger.Behaviour` NRE及多条Ghost池缺失，发生在本次InitPools returned之前；不阻止当前已观察的世界加载与游玩，起因与是否由本补丁引入尚未定案。该问题已独立登记[#325](https://github.com/baisiqi6/ohmymods/issues/325)待查，不能以Melon主日志为零宣称系统无异常。长时战斗/职业/联机/DLC未逐项重验，升级留档明确不支持。
- 原OEM安装失败页属于旧session；确认当前唯一session并返回后重开原生确认完成安装，未重投安装事务。该过程留本机回执。
- Host、源码审查、打包、签名、安装、两轮实测已分别留证；结项裁决首轮批准仅固定启动范围与正常Git流程；补查Unity Ghost证据后增量复核APPROVE，仅固定启动范围+正常Git交付+有限私有测试候选保留；不沿用“未发现新增产品bug”表述。未宣称合并/关单或managed done。

用户随后报告原生左上角×打开菜单未暂停（2026-11-23），确认为原生菜单而非Mod关闭键；与资源等待不同，另行定位，不能把本批启动通过写成暂停或全功能通过。
