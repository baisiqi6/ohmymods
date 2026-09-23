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

## 下一版整合内部候选（2026-09-23，尚未发布）

Mac 整合任务为 [Issue #29](https://github.com/baisiqi6/ohmymods/issues/29) / [Draft PR #30](https://github.com/baisiqi6/ohmymods/pull/30)。首轮候选从 `0d359ee5ac8d9cedc62a2fe1d53bc19d87790ef3` 整合 Windows PR #13、#18、#20、#22、#26（内含 #24）、#28，整合提交为 `571c626c13a5e1e3fa6a01866cb83237fcc3d3b2`。只有 `ModPanel.cs` 卡片计数发生文本冲突：保留 #26 便捷页的 5 张与 #28 骑士页的 3 张。候选源码随 Windows PR #28 的后续修复变化后，必须重建和重验，不能沿用下述内部 ZIP。

本机编译包装器逐项解析为 149 个 Compile 输入（148 个 `il2cpp/*.cs` + `PluginInfo.cs`）与 13 张 EmbeddedResource，构建 0 warning / 0 error。内部 Mod DLL SHA-256 `c80a67bc06ce5c49e1f565a14c243e82f686f47f36bfa944f0b648848305b976`；完全重编后 SHA 相同。内部 ZIP SHA-256 `ea2c8df49673d9ff05c633322350897be38de53e02fba1c65f450875a48888b0`，两次打包逐字节相同，解压的 Mod DLL 与构建输出相同，274 个条目不含游戏、存档、interop 或缓存。启动器完整合成测试 304/0，内部 ZIP 的 `--check-only` 退出 0。

在**整合后的**源码上实跑：火枪手 runtime 110/0、举旗 48/0 + e2e 24/0、税收官 25/0、武士 motion 144/0 + retreat 43/0 + visuals 15/0、宠物防抓 32/0 + 隐士 24/0、骑士面板 22/0、身份网络 48/0 + 稳定上下文 16/0 + 集成 9 断言。两项 interop 检查以本机已生成的 ARM64 interop 指定 `IL2CPP_DEPS`，均 0W0E。身份 runtime 57/1、archive 45/2 的三条文件锁相关用例在**原发布基线同一台 Mac**也逐项失败，记录为既有平台测试差异，不计为全绿或本次新增回归。

从内部 ZIP 全新解压后，首次联网取得 Unity 基础库、生成 interop；日志证实 1 个插件加载、Mod 加载、面板对象建立与 Chainloader 完成。约 68 秒时操作员向本次启动的进程组发送 SIGTERM，退出码 143，游戏进程无残留。共享游戏数据未改变，偏好文件从启动前备份恢复并核对字节，游戏 `.app` 的完整文件清单未改变。已知 HarmonyX 堆栈修复权限提示与上一版相同；未见新的 Mod 加载错误。**未操作 F5 面板或具体玩法、未实测身份 schema v4 存读档、换岛、联机；Mac x64 与 Windows 不在本记录范围。**

首轮独立审查指出 #28 无 binding 时面板仍可能应用后无法持久化，已回原 PR 由 Windows owner 修复；首轮内部 ZIP 不包含该修复。三份包内说明已先补充本轮功能、历史/当前来源边界和 schema v4 降级步骤；最终版本号、DLL SHA 与发行验收仍须在对应 Release 确认。

随后仅因上述说明及 input-lock 变化构建了内部 `integration.2` 包，Mod DLL 仍为本节记录的 `c80a67bc...`。本节的 `integration.1` ZIP SHA 只标识第一份历史内部候选；ZIP 自身不能把其最终 SHA 写入包内文件，否则会形成自引用。每份后续 ZIP 的精确 SHA 均在包外验收文件和发布页记录。此节不作为最终发行回执。

## #28 修复后的历史内部源码候选（未发布）

Windows PR #28 的 `cbf182a8f2dddf76c902ddfb3a1748ed50e7f525` 修复了刷新时无身份 binding 仍可应用的问题：禁用应用并显示原因，新增判别用例。Mac 整合源码提交 `a2c93929b8dedd259afefde69e6309e7aed5993c` 已包含该提交；GLM 5.3 max 只读独立复审认为可接入，留有异常路径提示文案与测试覆盖两项非阻塞 P2 建议。本机骑士面板 23/0；完整整合回归中火枪手 runtime 110/0、举旗 48/0 + e2e 24/0、税收官 25/0、武士 motion 144/0 + retreat 43/0 + visuals 15/0、宠物防抓 32/0 + 隐士 24/0、身份网络 48/0 + 稳定上下文 16/0 + 集成 9 断言。两项 ARM64 interop 编译 0W0E；本机 Mod 编译 0W0E，非增量重编字节相同，内部 DLL SHA-256 `83f41179fdd92dce4989e2a26edcd5e011c1d64087428d3d51cf9382e1bb5697`。

身份 runtime 57/1、archive 45/2 仍是上节所列的同三条 macOS 文件锁断言，与原发布基线的失败一致。该记录时点的源码尚未重新打包、从包受控启动或验收 F5/玩法；旧 `integration.1/.2` 的 ZIP 与冷启动回执不能代表后续源码。最终版本号、玩家包 SHA 与平台验收以新的包外发行回执为准。

## v9.14.24 Mac ARM64 预览发行边界

本版从 Windows `v9.14.24` tag 的 `28425cca45f7087a63c0205cb04e4e5756884874` 构建。七个功能 PR 已在 Windows 发布分支合并；Mac PR #30 只增加本包说明，生产 `il2cpp/` 和测试文件相对该 tag 无差异。此版还包含 v9.14.24 补入的新长火铳图集、枪与弹丸资源，不能复用此前 `9.14.23` 或 `integration.1/.2/.3` 内部 DLL 和 ZIP。

Mac 本地包装器明确编入本版源码和 13 张嵌入资源，以 `9.14.24` 插件版本/日志戳构建，0 warning / 0 error；非增量重编得到相同 DLL SHA-256 `566525d59fb2b505832e118476866c18a061033ccd22fa50bcbef6b8d4ac7b73`。三张更新的火枪资源与 DLL 中嵌入字节逐项一致（`MusketeerAtlas.png` `f6470634305d28c259bb5b138de86803c64ad859653529d08d13dcdee3be6572`、`MusketeerGun.png` `dc4ba2b0373b5c66b698f93514ff2b3f1a20cd5d5c761c3ec9a3dc0c12639ddb`、`MusketeerBullet.png` `d4ce678a8e470f4c782ac882a38df8a646e18006baa9a28d17ffedbdda45a08b`）。Mac 本机火枪 runtime 111/0、骑士面板 23/0、宠物防抓 32/0，包/启动器合成测试 304/0。其余功能在上一整合候选上的回归见上节；本版直接变更的是火枪表现与版本戳，不把旧套件结果冒充本版全量复跑。身份 runtime/archive 的三条 macOS 文件锁断言在旧发布基线同样失败，不能称全绿。

本版内部候选 ZIP SHA-256 `b065e52254389bfc646034bfe68182bf7bb7b0248197fcc4690862aec478d005`，274 个条目，不含游戏、存档、config/cache/interop；全新解压 `--check-only` 校验包文件与游戏四指纹通过。受控启动 90 秒的日志确认 `v9.14.24 build=9.14.24`、Mod/面板和 Chainloader 加载完成，未出现新图集尺寸或弹丸解码错误；操作员按计划结束进程，退出码 143，无残留游戏进程。共享存档未改变，运行时变动的偏好文件由启动前备份恢复并逐字节核对，游戏 `.app` 的 106 项文件清单未改变。HarmonyX stack-trace-fix 权限提示与旧候选相同；另有 TrollWeak 身份/header 不可用时 fail-closed 告警。**没有实际操作 F5、长火铳射击观感、骑士面板存读档、换岛或联机；Mac x86_64 和 Windows 不在此 Mac 回执范围。**

公开预览 ZIP 的精确 SHA-256、tag、最终包锁及验收结论放在包外发行回执和对应 GitHub Release；ZIP 无法在自身内容里可靠记录自身最终 SHA。玩家升级前须备份旧包 `BepInEx/config/KingdomEnhancedMod/ModSave/` 整个目录和游戏存档，schema v4 的轮换 `.bak` 不能保证降级可用。
