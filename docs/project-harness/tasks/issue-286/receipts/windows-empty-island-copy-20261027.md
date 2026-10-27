# Windows 当前岛空登记恢复：仅副本候选

用户已确认 Windows F5 为“存档身份待核验，暂停购买”，并明确本战役当前坐骑岛从未成功购买盾卫、没有未领取盾具。当前实际 `.281.3` DLL 为 `63d43ff17ff601d1c0f95f26beeafdde21ddffae4e73419d640119c8060f32ae`，与 Mac 对应已装版本的 18 个盾卫源文件一致；不能描述为 Mac 已完成、只有 Windows 缺适配。

## 输入与唯一修改

正常游戏在调查后产生新保存，因此候选改用重新稳定双读的四文件副本，不沿用旧调查进度。源 global SHA256 为 `540923f18fd553e934cfa3429c32030e25d7cd6814cca20bf0147fd0d39c2240`，原配对为 `21271e7c4e8781308cb3bb9e915c1f9f1fe20b78794e8c7a2f358e02b6581810`。真实 slot1/challenge0/currentLand11，3915 个原生对象；盾卫 schema4 只有 land8 的 Soldier/PaidTool 两条责任，盾模已付、两侧扩位未付，没有 land11 行。

工具复制并核定实际装版的 10 个纯生产 codec/fingerprint 源，默认 dry-run，显式 write-copy 只准写本任务全新副本目录。依赖用户的本岛无购买声明，仅添加 challenge0/land11/hashKind2/canonical/claims=[]；不搬用 land8 权益，不发兵、不退款、不清 Unknown、不改原生岛对象。

global 解压内容只替换唯一 HeavyShield value token；前 19,297,846 bytes 和后 31,284 bytes 完全保持。删除唯一新行后盾卫文档与原文档语义完全相同，原 land8 LegacyKind1、两责、全部收据及其它 prefs/原生岛/Msk 原生 archive 保留。paired backup 使用生产 Save 从新 global 的原 archive 重建，没有手改一个 hash 冒充配对。

## 离线验证与独立审查

- 纯工具 .NET8 编译零警告/错误，29 项检查通过；另实跑既有目录重放和 symlink 祖先输出拒绝。
- 独立 Astra 复核原始输入、10 份装版源及数据边界；重跑原 29 检查、记录 25 项审查检查。生产 TryRecover 对作者新 global+新 pair 成功，两向新旧混配均拒绝；生产 Save 复建作者 pair 逐字节一致。审查结论仅为冻结 copy-only 候选 APPROVED。
- .NET10 重压缩与作者 .NET8 的 gzip 字节不同，但解压内容完全相同。同运行时重复输出确定；不能混用不同运行时的 global/pair，也不能声称跨运行时压缩 hash 一致。

冻结作者候选：

| 文件 | SHA256 |
|---|---|
| global-v35 | `f9eaadff3be92b5e21921e2f0a39384f002b2fbff53be1960bde9a5e6e184a4b` |
| musketeer-pair-global-v35.json | `49c55043e523adcd562ce1994fda89ec2f2ef1124123fea7f2ba32b288c25e85` |

离线 canonical `2f6ff1f3d1fa79fe373b1eea9bfb599196abd16e64f6f3688b11d58d117785e4` 是生产 fingerprint 对此副本计算的值，**不是已观察到的 Windows Native canonical**。尚未证明修复后 F5 就绪、现场购买、领取转职、换岛或冷重进；未获这些结果不能记为购买已修。

## 当前保护与下一步

没有改写正常存档、配置、DLL/运行包或 Steam，没有启动/结束/输入游戏，也没有恢复旧玩家进度。此回执与审查均不授权正常回写。后续 Native 窗口须继续使用副本或另行明确的数据写入安排；任何实际目标若已变化，先重取当前匹配输入并重建/复核，不能拿本次旧快照覆盖新进度。

PR #287 对以后 virgin generation 的预防与本次旧岛副本登记是不同范围；其合并/安装状态不由本回执改变。原始玩家数据、工具精确 case 参数及审查日志只保留本机 `shield-purchase-windows-20261027/readonly-20261027T110732Z/windows-recovery/`（`result.md`、`tool-diff.patch`、`review/verdict.md`、`targeted-tests.json`、`final-integrity.json`）。
