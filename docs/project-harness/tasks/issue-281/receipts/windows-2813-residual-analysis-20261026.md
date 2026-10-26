# Windows .281.3 首航额外错误：静态调查

本轮只读分析已留存日志、冻结候选源码和实际原生二进制，没有启动/操作游戏、连接 Windows、修改正常存档/配置或替换 DLL。**已定位两类错误的产生机制，具体触发来源仍待最小取证；没有实施修复。** 此记录不撤销此前首岛存读档回路的窄范围通过，也不将其扩大为整轮无错误。

## 已证事实与未定部分

| 错误 | 已证机制、影响 | 尚不能定案 |
|---|---|---|
| 2 次 `Collection was modified` | 当前原生 `Knight.<EmbarkArchers>d__248.MoveNext` 跨 0.2 秒 yield 保留真实 `Knight._archers` 的 HashSet 枚举器；期间成功 Add/Remove 会改变版本，下一次 MoveNext 抛错并中止该协程。即使 Remove 后 Add 回同一骑士、人数不变，也能失效。剩余随从的登船目标可能未分配；不能据此推算丢兵数或存档损坏。 | Norse 的 Promote/SetKnight 恢复链能到达同一集合，且时间窗重叠，但没有现场 knight/set/actor/version 记录，不能证明是本次写者。原生移岗、禁用、抓走、石化等写者也已列出。新火枪手禁船 gate 无直接集合写入，拒绝后原生调用也没有必然 Remove 分支；同窗日志不构成归责。 |
| 50 条 `Array Out of Bounds: Increase Size (Forest...)` | 唯一匹配点是 `World.Update → ConstArray<Forest>.Add`，容器是 `World.boarViableForests` 的固定 **8 格野猪候选缓存**，不是动态 `World.forests` 注册表。填充筛选区域宽度 >6 的 Forest；满后主动报错并拒绝本项，没有越界写入或自动增长。 | 已观察 MapWidth 新增 27 块、坐骑设施插入 8 块，但缺现场 Forest 区域数/候选数及生成阶段，不能区分有效几何超容量、合并前临时超量或生成链异常。50 条不能倒算对象数或帧数；文字来自缓存首项，不是被拒绝项。 |

第二实例没有再次出航或覆盖同一 fresh generation 路径。原生保存会恢复 `hasSpawnedBoarThisSeason`，为 true 时可跳过上述候选填充；本次实际值未取证。两类错误的冷读零计数均不能视为已修。

## 当前版本原生证据

- Windows 2.4.3 / Unity 6000.0.66f2：GameAssembly SHA256 `cb3f28d73766c9ffb035512161bea1ad82835b0684330ce12f5d83da365e2342`；metadata `b10a5c6e7ae16c5526c93d6c81cf5b821ef4e62ecbe4c8d7a23a7c8f93ed01c8`。
- Knight iterator `1805C65C0`；实际集合字段 `+0x168`；版本比较 `1815FE691`；独占的 Knight.AddArcher `1805B2020` / RemoveArcher `1805B8420`。HashSet 泛型实现共享，不能直接 detour。Archer.OnEmbarkComplete 为 3205 个 metadata 方法共址的 RET，也不能用作诊断 hook。
- World 缓存字段 `+0xA8`；ctor `18083C16E` 传 size=8；Update 唯一 Add callsite `18083B77F`；共享 Add body `181481860` 的满容量分支记录后返回。不能 hook 此泛型共享实现来补偿。
- 当前 Mac 2.4.0 同包的 x86_64 切片也静态确认 ctor 容量 8；仅说明此设计不是 2.4.3 首次出现，不代替 Windows 2.4.0、Mac ARM64 或运行时对照。
- 已安装 `.281.3` 的 282 个冻结源文件逐项 hash 匹配；没有上述 World 容器直接或反射写者。工作树新增的宫廷宽度例外未误当作已安装逻辑。interop DLL 没有作为原方法实现证据。

## 最小后续证据

若进入下一诊断阶段，先单独审查具体接线，不先吞异常、扩容、截断候选或增加逐帧补偿：

1. 仅跟踪有活跃登船枚举器的骑士，记录 exact set/version/current actor/frame；在已核独占的 Add/Remove 边界记录该集合的变化，关联 Norse load/assign/patrol 原因。无标签的原生调用保持 unknown。使用有界条数，不全场刷日志。
2. 在既有世界生成/加载边界及首次候选填充记录去重 Forest 数、各 region 宽度、合格数、缓存容量、原生 phase 和 boar 状态，区分稳定超容量、临时阶段超量及重复注册。按当前二进制核接线，避开共址短方法。

本阶段没有新产品构建/实机验收；PR #282 保持 Draft，Issue #281 开放，未合并/发版/重新安装。

## 留证与独立复核

原始有效日志和存读档边界见[前次回执](windows-2813-native-loop-20261025.md)。私有证据目录：既有 `transport-contract/residual-investigation-20261026T065555Z/`，包含 native maps、原样汇编、xref、源码/日志时序和两份报告。原始日志与玩家身份不上传。

- `knight-report.md`：Astra 独立原生分析；具体写者未知。
- `forest-native-conclusion.md` SHA256 `ebd0e860310ca2610fb6bc270bdca573abce7ce669271667ce04a8ddfdf94bf2`；`forest-source-timing.md` SHA256 `42d92c2624e371ae0854729c2e3cc5e3cae850c85ad4c4dd4c346b36fdce7d2b`。
- 同阶段方案审查：公开模型事件核验 `zhipu-coding-plan / glm-5.3`、请求 thinking=max，APPROVED；记录在 `plan-review.jsonl` / `plan-review-verdict.md`。结论另由 Astra 交叉核对，两份报告无阻断性事实问题，并明确禁止将候选来源或冷读零日志写成已根治。
- 此文件与进展条目是既定只读阶段的事实归档，没有新修法、数据写入或安装决策，复用同阶段审查，不为机械读写另开审批。
