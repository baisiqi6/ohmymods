# Mac ARM64 发行准备验证记录

本文件区分既有可玩安装、隔离首次启动与最终发行包。未完成的条目不因其他平台或单元测试通过而变成通过。

## 已有候选

- 玩法源码：v9.5.13，`1088b9cd981efb32aa3afb03e91eea66ad1a451b`。
- 测试修复：PR #11 合并 `7fde1e555cda95e3acea73b1c6455116c6abd611`，不改变生产 il2cpp 树。
- 既有 Mod DLL：`162046f77e025d90b3d6c9b5ea25d92835cb0609c26563885898ae93dfc9fb8a`。
- 用户反馈一轮基础游玩未见问题；这不是存读档、所有职业、首次弩矢/池复用/碰撞归还、联机等逐项通过。
- 固定游戏 GameAssembly：`738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`（KTC 2.4.0 r23485 / Unity 6000.0.61f1 Universal）。未知构建拒绝。

## 2026-09-22 全冷实验（通过，尚非最终 ZIP）

新建包含空格的隔离目录，游戏使用本机副本；启动前无游戏进程，共享用户数据先备份。复制锁定 core、ARM64 runtime、Doorstop、当前 Mod；没有 interop、unity-libs、dummy 或 cache。BepInEx 配置使用此前验证配置的逐字节副本，Mod 个人配置不复制。原可玩安装不变。

日志证明从 `https://unity.bepinex.dev/libraries/6000.0.61.zip` 下载，ZIP SHA 为 `015e91fb92ac0ed92ea1c3119153869740ffb983f8a1b005fa28db46506e5882`；Cpp2IL 完成并生成 interop，生成目录最终 130 个文件；随后加载 KingdomEnhancedMod 9.5.13 并完成 Chainloader。进程采样为 ARM64，游戏画面进入场景。

完成启动后继续观察 60 秒，整个实验约 112.8 秒，测试监督器向自己的进程组发送 SIGTERM 后退出码 143，确认没有残留游戏进程。此退出是受控结束，不记录为崩溃。正常运行可能写入用户存档，未自动回滚。原安装的 core/runtime/Mod/BepInEx.cfg 与实验前输入清单仍一致。

LibCpp2IL 对 Universal 文件固定优先分析 x64 slice；本次 ARM64 进程亦然。ScanMethodRefs=false 仅关闭生成阶段的方法交叉引用扫描，不等于关闭所有运行时 xref。本结果只覆盖锁定的 Universal 构建，不宣称任意 ARM64-only 游戏也能生成并运行。

## 原生库路径整理（通过静态与合成回归）

源码与此前已验收 ARM64 Dobby 修补源逐文件一致。仅增加编译路径映射，去除机器绝对路径，固定构建日期。新 SHA：`8fbfdd239f9c1a7f277643a46e46f13d8e7e61a8997d58b5451104dbdbd404d6`。

- 五组邻接短函数布局及未知 Commit 检查通过。
- 原生调用/恢复 43 项检查通过。
- 最近可用页 15 项检查通过。
- 新旧 11,503 条指令的助记符序列一致；87 处 adrp/add 操作数差异对应字符串布局/地址变化。
- Mach-O 为 ARM64，依赖仅系统 libc++/libSystem，未残留本机私有路径。

这些检查不能替代新 SHA 的最终包启动；上一节全冷实验使用的是此前 native SHA。

## 最终 ZIP 的验收范围

- 精确 staged 二进制、输入锁定和第三方许可/对应源码完整性复核。
- 启动器路径、配置保留、并发、错误提示与 ZIP 内容/可执行位测试。
- 从最终 ZIP 解压的全新目录、发行默认配置、无缓存首次启动。
- 同一包第二次启动与配置保留升级。
- 模拟下载隔离属性后，通过实际 Finder 双击检查首次启动流程；需要用户系统批准时单独记录。
- PR/base/tag/版本与发布说明对齐、上传后 SHA/可下载性读回。

最终 ZIP 的结果单独记录在发行附属 `mac-arm64-acceptance.json` 中，以该文件记录的 ZIP SHA256 对账；本文不充当最终发行回执，避免把实验与发行产物混同。Mac x64/Rosetta、Windows 各自验收；首版 Mac 包仅声明 ARM64 范围。

## 发行目标核对补充

用户随后询问 DMG 实验版。已只读挂载其已有 DMG，并核对 GameAssembly、游戏可执行文件、Info.plist 与 global-metadata.dat 四项 SHA；四项均与上述已验收独立副本一致，随后卸载镜像。因此当前验证线继续面向这份固定构建，包不包含 DMG 或游戏本体。

另对本机当前 Steam 安装只读比较：GameAssembly 为 `1d50bbfa2bda430cead8d93e5c063ebf232531ee715656511018fc0d5d51662e`，metadata 为 `852c0c9dbced717fe987ab860b9a3daa50470869760f40ca91e8a7582787ecdb`，均与当前目标不同，ARM64 的 __TEXT 布局/内容也不同。不能仅添加 hash 或关闭守卫来声称兼容；本包不声明支持该 Steam 构建。Steam 适配留作独立后续范围。

## 打包脚本与调试信息整理

启动器/打包器完整回归由 Worker 与 Operator 分别实跑 166 项通过；随后配置 section 尾注及括号内空白的最小修订，定向 17 项通过，独立复审批准。四个自有托管 DLL 的 CodeView 路径清理工具独立 31 项通过；仅 PDB 定位字段改变，字段外所有字节、CLR 元数据、资源、MVID、GUID/age 均保持。原始/整理后 SHA 和定位区间见 third-party/metadata-sanitization-receipts.json。
