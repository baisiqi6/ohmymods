# Issue #206 — 静态字体刷屏与角色/保存性能

2026-10-10 Mac IL2CPP 2.4.0；源码基线 c6f7524d（PR #202），任务分支 `codex/font-performance-20261010`。本任务不修改 Mono、Android、PR #204 火枪兵实现或存档 schema。代码提交与 PR 合并、安装和玩家实测分别记录；本文件是测量证据，不宣称完整卡顿已解决。

## 根因与修复

原生菜单三个 Text 使用静态 Zpix 但开启 BestFit。写入字体/激活/修改 BestFit 时维护 `wanted && dynamic`，在静态字体赋值之前关闭不支持的组合，保留动态字体意图、显式关闭、池生命周期和重入语义。原 font、fontSize、style、min/max 和布局不改，没有每帧盘点或日志过滤。ARM64 当前三个目标地址独立；这一审计不证明其他平台的 detour 安全。

盾卫的支付点和绑定入口重复检查 Native hooks。原 Harmony.GetPatchInfo 每次创建公开 Patches 快照及五组列表/只读包装，成为主要托管分配源。新 helper 只缓存当前库 API 的能力和 delegate，每次重新查询注册表及当前补丁数组，保留六个目标的 DeclaringType/prefix/finalizer 和 Demote postfix 检查；卸载立即返回 false。缺少 API 时仍走原实时快照，查询异常 fail-closed，不缓存 Installed 或 payload。

持久化比较保留 owner/key/prefs guards；固定 Native key 仅持有一次，结果每次 fresh UTF16 ordinal 比较并释放临时 handle，线程绑定与销毁释放有明确责任。它避免未来 payload 增大时的整串托管复制；实际旧值只有约 641–693 UTF16 单元，不能把约 31KB/帧全部归因于它。

三个 canonical hash 只把 MemoryStream.ToArray 替换成有效 Length 的 Span，格式、域、排序、哈希和文件事务不改。MusketeerArchive.Encode 的 ToArray 保留；没有共享或缓存游戏 JSON，也没有异步化文件写入。

## 实测

独立副本诊断包 D 与 E 使用相同存档和 Preferences 前像。当前游戏 2.4.0 / Unity6000.0.61f1，GameAssembly SHA256 `738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`；ARM64 由实际进程采样确认。计时包含嵌套 inclusive 数据，父子不可相加。完整 Playing 窗口加权：D 2184 帧，E 2189 帧；焦点和动作并非严格实验室同构。

| 指标 | 原查询 D | 新查询 E |
|---|---:|---:|
| 盾卫更新托管分配/调用 | 31635.99 B | 1137.92 B |
| 整个 ModPanel 托管分配/调用 | 35344.05 B | 4519.54 B |
| 盾卫更新 inclusive | 0.20339 ms | 0.15094 ms |
| ModPanel inclusive | 2.35760 ms | 2.26542 ms |
| 游戏整帧平均 | 27.481 ms | 27.447 ms |

盾卫分配下降约 96.4%，Mod 分配下降约 87.2%；整帧耗时无有意义改善，不能声称帧率提升。更早未修复菜单窗口 B 有 467 条 BestFit 警告，修复后的 C/D/E 为零，三个菜单控件 size/style/rect 读回保持。不同长度窗口不能作警告速率对比。

E 自动保存同步 scope 140.835ms，保存退出同步 scope 118.62ms；自动保存的两次 Native JSON 序列化合计约29ms。实际同 scope JSON 序列比较一致，但诊断显示 missing-consumer/partial（未出现火枪兵消费者），不升级为完整 paired save、冷读档或异步保存全耗时。原游戏自然退出 exit0/200.66秒。Host 同输入 canonical=1,588,891B 测试每次 hash 少约1.59MB分配；CPU时间18.23/18.35ms无明显收益。

剩余角色 CPU 在原窗口约 HeroActorLoop1.7–1.9ms/帧（约77访问），HeroVisualSync1.1–1.3ms/帧（包含 guards），GreekMaintainY0.89–0.96ms/帧。这些是热点观察，尚不足以证明全部卡顿的因果，不减少身份/外观更新频率或跳过原业务检查。

## 验证与安装边界

完整累计净产品 net6 与 net8 构建均0 warnings/0 errors。公开 `tests/font-performance/` 直接编译产品源码：字体策略61、独立回归21、NativeReader假 adapter生命周期/线程/异常109、实际 Harmony查询92和诊断20、三个实际hash消费者47项均通过。Host 假 adapter零分配不冒充 Native结果，实际Harmony测试不创建Native detour。各产物、累计保全、最终Native诊断候选和安装脚本分别独立审查。

诊断E DLL SHA256 `461341a7f2bcc2bdc6e49201b01c40bb42c2540ac282d94631afd38ba52a0c9a`，298编译输入/161引用；净产品 DLL SHA256 `9ef503f2ecd79fee6b341d0139c35248899d31c2971fca5036b08c6f17cea9a1`，294输入/161引用。移除本任务四个计时/字体盘点/保存等值诊断模块及接线；产品 helper 保留默认关闭的有界 opt-in shadow，原 FrameWatch 保持。净包本轮未另做Native运行，不能用E候选证明同一DLL已运行。

原正常游玩DLL99c0b472的累计源287→292文件，恰好五个新增helper、七个既有文件最小接线、无删除；原宫廷可见入口私有注册、资源、盾卫、钻石库存HUD和8+8双坐骑岛保持。净包保留原私有10.9.38候选标签，本次不创建正式release/tag，不以版本标签代替哈希身份。

测试过程事故：原游戏退出后的一次CUA窗口读取意外触发新的LaunchServices进程，导致第一次恢复仅logs失败；已核验自身新PID/parent/start/command后关闭并保留late postimages，随后从E原始前像恢复。最终 normal/game/save/prefs/logs完整内容清单全部一致、无游戏进程。此事实保留在私有原始回执；退出后禁止任何app/AX/screenshot读取，只用shell收尾。此前外部worker越界创建tmp编译探针也已停止并限制工具，历史tmp前态未知，不擅删未知目录。

本任务私有证据位于 `.local/tasks/player-performance-repair-20261010/`，不提交玩家存档、JSON payload、个人Preferences或完整日志。安装仅在game关闭、最终GLM关口及独审通过后替换正常DLL和SHA256SUMS单项；fresh备份normal/save/prefs/logs，game只读完整清单，前后其余所有内容保持。安装结果和对应PR另写下节；Windows、完整保存、全场景付款、联机与净包玩家体验仍待验。

## 交付状态

GLM5.3/max 实际 stop verdict APPROVE_PERFORMANCE_DELIVERY，公开测试与安装脚本独审条件门均AP。2026-10-10 22:17:49 UTC 已安装正常候选9ef503f2（完整SHA见上），原99c0b472和SHA256SUMS已fresh备份；回执只改正常DLL和SHA256SUMS条目，489运行环境文件/71游戏文件/2共享存档文件参与清单核对，存档、配置、游戏及日志内容保持，launcher --check-only exit0、gameStarted=false。净包首启、全场景及玩家体验待验。

源码通过独立任务分支和Draft stacked PR交付；PR #202仍Draft/Open，Issue #206在本任务PR未合并前保持开放。canonical checklist无本Issue独立条目，未直接新增或变更任务状态；EXharness validator通过0 warnings。安装回执中sourceHead字段是PR202的source base c6f7524d，新的payload身份以candidate-materials的294输入/161引用和净包全SHA为准；源码提交身份由此任务分支及PR记录。
