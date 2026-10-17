# Issue248：Windows 2.4.3 独立兼容适配

## 独立目标与基线

用户授权继续Windows2.4.3适配，全部先隔离测试。本需求与2.4.0懒人包钱袋报告Issue247完全独立，不混入其未合并候选、不互相替代验收。

源码release/v9.5.13@1f9c6753ef2a87ba994d38c96dc49b111927551c；独立分支codex/windows243-compat-20261017。目标游戏2.4.3r23537，x64，Unity6000.0.66f2。GameAssembly SHA256 cb3f28d73766c9ffb035512161bea1ad82835b0684330ce12f5d83da365e2342；metadata SHA256 b10a5c6e7ae16c5526c93d6c81cf5b821ef4e62ecbe4c8d7a23a7c8f93ed01c8。

## 实施与验收

- [x] 只读核对版本/PE架构/哈希，部分新方法映射已取；尚未执行游戏或安装loader。
- [ ] 真实隔离Windows存档：优先现成可靠机制；缺少时准备专用标准测试账户身份脚本，精确审核后才执行。不同游戏目录/环境变量不等于存档隔离；不交换ADMIN存档，不改其ACL，不启动或修改服务配置，不切当前桌面，不授整盘权限。仅本任务新用户与候选目录可写，密码仅内存。
- [ ] 精确PID/Owner/Profile及禁止访问原存档负测通过后，另核验Unity实际persistentDataPath；失败有界停止，不擅自改系统策略/他人账户。
- [ ] 给定本体保持只读，复制到新隔离运行目录；纯净loader/interop/cache，先无Mod生成，再验证未修改的公开Mod基线，按2.4.3真实发现修复。
- [ ] 新interop编译与hook绑定/短方法地址折叠分别核验，不把interop stub当原实现。
- [ ] 原生启动、角色/商店购买/保存读档分别验收；图形/自然尺度窗口、所有功能、联机等未覆盖部分如实列明，菜单或batchmode启动不代表完整兼容。
- [ ] 必要审查与验证后提交、push、PR至release/v9.5.13；本轮不自动合并、正常安装或发版。

## 护栏与证据

Steam正式目录禁止写入和测试；正常游玩版、原存档、既有配置/账户、宫廷101与Android232不改。本任务独立源码树、工作记录、loader、缓存、存档和产物。代码、自动测试、独立审查、隔离安装和原生实测分开记录。原始游戏、日志与私有机器资料不提交。


## 2026-10-17 准备阶段进展

已只读提取六类161方法，对应当前维护源码9个Hook名称。Mover、角色、商店和主要保存方法未充分覆盖，仍Unknown；短方法入口及原生地址折叠还需补证。当前维护源码与公开10.8.35包不同，后续先测原公开DLL，再测维护候选。

用户已明确授权专用普通本地测试账户、测试后停用。该账户已创建并核验标准角色；后续独立回读确认停用、无遗留测试子进程、原LocalLow目录17文件SHA保持。身份预检child快速退出但没有JSON，尚不能证明真实child token/profile/原档权限隔离；正在保留原启动配方捕获stdout/stderr。所有游戏/loader启动仍未执行。

目标游戏包内配置另外说明公共Documents下Steam存储路径；只读确认其中已有两个文件。这是潜在共享入口，不代表已确认游戏实际读写行为。后续不能仅凭独立用户及LocalLow路径通过就宣布全部存储隔离；启动前还需核验该入口的访问边界。原配置及共享目录未修改。

## 2026-10-17 静态兼容阶段

- 专用测试账户已创建；三轮身份预检均未取得可靠 child entry/token/profile 证据，最后一轮独立收尾已停用账户、清空自有测试进程。停止继续叠加 PowerShell 变体；原生游戏、loader、Mod 未启动或安装。最新只读回读确认原 LocalLow 17 文件与公共存储 2 文件 SHA 全保持。
- 当前隔离阻断：游戏包配置提示 Public Documents Steam 路由；尚未在实际测试 token 下证明该路由不可写。独立账户与 LocalLow 分离不足以认定全部存储隔离。原公共目录 ACL/内容保持；restricted-token 报告仅为机制候选，未执行。
- 已在 Mac 离线按固定 Cpp2IL/Interop 实发字节，使用真实 2.4.3 PE 路径完成默认静态 xref/GC writebarrier 生成：无 unstrip 101 DLL、官方 6000.0.66 base 包探索集 131 DLL，两套均 exit 0。只读解析/指令解码没有运行目标 native。生成 map 各 158715 非零地址记录，xref 缓存各 33160432 字节。
- 六类型 161 方法样本：160 常规名 + 1 编译器名称 sanitize 后对应，无缺失。十个重点方法的参数与 RVA 已取，在本轮发出 map 内各地址无别名；不是全部原生符号或 detour 安全审计。
- 正确 IL2CPP 宏下两套 net8 纯编译均有 19 条错误，集中于三个 GlobalSaveData 协程类型编号变化（Save 89→92；DeleteCampaign 91→94；DeleteChallenge 94→97）。早期测试 wrapper 漏宏导致的 BaseUnityPlugin 错误已撤回，不能据此修改产品基类。
- 独立审查已核新类型具备现钩子所需字段形状；编译期按明确版本映射的最小修复正在私有镜像验证。旧版字段形状、版本与引用 SHA 匹配、双向错配拒绝、240 防回归均列为门槛。仍未证明 state0 时序、失败返回 marshalling 与新版本原生保存体语义；编译通过不替代实机。

私有证据位于本机任务目录 `windows243-compat-20261017` 的 `identity-r3-attempt1`、`offline-interop/attempt2`、`compat-binding`；过程产物/原游戏/存档/机器身份不入仓。独立审查记录分别绑定生成文件 SHA、编译日志与模型事件。后续 PR 保持 Draft，不合并、正常安装或发版，Issue 保持开放。

## 编译绑定交付结果

四文件已集成按明确版本选择的typed协程别名，默认240、显式243，未知值在编译前拒绝。最终源码在243官方base库unstrip引用及真实Win240引用下均0警告0错误；固定manifest双向错配拒绝、未知版本拒绝、16个alias/Android组合、Courier54项与Hero667断言通过。两套旧银行相关夹具缺PatchWorld_WallSpots，原HEAD基线同样失败，保持未验而不修stub凑绿。无unstrip的243组缺完整Unity API，只作结构产物。详细证据与原生未验边界见[verification.md](verification.md)。

这只是#248的编译绑定slice；原生隔离、loader、保存往返仍待完成，不能关闭Issue或把候选安装到正常版。
