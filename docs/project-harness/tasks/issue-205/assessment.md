# Issue #205：BepInEx.Android 独立对照结果

2026-10-10，Android Operator，Mac ARM64。范围来自用户明确授权：保留既有移植，用独立环境验证同一游戏 APK 与候选加载器，再评估适配收益。本报告交付的是受限评估；Issue #196 仍暂停，没有切换框架或新增玩法实现。

## 结论

**本轮不切换。** 同一新模拟器中原版已进入原生关卡教程，NextBep 候选项目发布的 Launcher 容器路线在本次离线条件下未跑通。卡点发生在 Android 游戏启动阶段，尚未取得 BepInEx 托管入口证据。因此不能据此判定 BepInEx 核心、Harmony、组件或配置机制本身失败，也不能断言该游戏永远无法接入 BepInEx.Android。

本轮实际证明可删除的移植代码为 **0 行 / 0 文件**。静态 API 比对发现 62 物理行加载器辅助代码有替换潜力；另有 132 行配置与入口接线可能改写，合计触及 194 行旧源码。它们不是净节省量，新的接线、配置迁移和生命周期处理仍需代码及验证。

## 输入与隔离

- 游戏：Android 2.4.0，versionCode 23485，Unity 6000.0.61f1，ARM64 IL2CPP；APK SHA256 `4b40edd3e7eefb6df43e6777d566bda366179b5e245dd8f20e87810b0b56732d`。
- 候选：[NextBep/BepInEx.Android.Launcher](https://github.com/NextBep/BepInEx.Android.Launcher)，源码 `7943be19a80f959648f3fbf667f0d5e24e1330d5`，CI artifact `11466306634`，APK SHA256 `6222c4e81afceb2e7e2da909afa9c6b0d102cde44f01e30ad357507f2cc09ff1`。workflow `37584214602` 整体为 failure；artifact 存在不代表 CI 通过。
- 本轮使用 NextBep fork，不把它等同于 BepInEx 上游的 Android 支持承诺。核心仓库源码快照与实际 DLL API 已检查，但 bundled DLL 与该源码 commit 的精确构建对应关系未独立证明。
- 与先前同项目 artifact 对照：classes.dex、libmain、libfusion、BepInEx 与 .NET runtime ZIP 均逐字相同；源码差异仅 README。不将该候选描述成已修复启动问题的新版本，也不称整个 APK 字节相同。
- 全新 AVD `ohmymods_android35_bepinex_compare`，唯一测试 serial `emulator-5584`，API 35 / Google Play ARM64，4 CPU / 4 GiB，Vulkan / MoltenVK。独立 userdata；只在此 AVD 关闭网络，未改宿主网络。
- 安装后的两个 base.apk 均读回 SHA256 校验。未启动旧 AVD、未操作真实设备、未改源 APK、PC、Mono 或 Steam 目录。测试结束后双包及全部 `:game` PID 归零，随后关闭本次 AVD。
- 冻结旧 Android 来源 HEAD `3f2cb37f15a15edcb8c34d30c23b6fc29fe10463`，15 份已记录源码哈希在收尾时全部匹配。旧 AVD 18 项大小、mtime、inode 和已记录的小文件哈希也匹配；未对大 userdata 做全量哈希，不宣称逐字证明全部旧磁盘。新报告分支基线 `9ea703f8cedca98849390913f53b7d84982fc020` 的 PC 改动不计入加载器收益。

## 原版基线与候选窗口

原版通过自己的 `eagle.simple.sdks.manage.FakeActivity` 启动，在模式、难度和君主选择后进入关卡。124.30 秒截图为故事过场，280.55 秒截图可见角色、森林、地形和水面，仍有原生教程遮罩。这证明关卡画面可以加载；没有测试移动、战斗、存读档或长期稳定性。共 5 次游戏菜单输入和 1 次单独留证的 Android 全屏提示关闭，未做玩法数据修改。287.42 秒完成停止。

首次原版调用发生在 AVD boot waiter 尚未完成时，`am start` 失败。该调用顺序错误的 receipt、日志和截图保留，不计作游戏失败。修正为先等待 boot 完成并在 runner 内检查 `sys.boot_completed == 1`，经单独审查后只补测一次；没有通过反复重试掩盖原始失败。

候选使用实际导出的 `ShortcutLauncherActivity`，参数 `shortcut_package=com.rawfury.kingdom2crowns`、`shortcut_modpack=ComparisonNoHook`。零 Hook 诊断件沿用旧 BasePlugin 源码，仅按实际候选引用重编译：初次 net6 因引用 System.Runtime 10 产生 CS1705，原始失败保留；改为 net10 后 0 warning / 0 error。它只记录加载标记、框架描述和 PID，未改游戏数据、注册组件或触发 Hook。DLL SHA256 `e6a6811a577132d6538d04a0f4686b860fe30d6545340d521fefe264eabf6ab9`，modpack 与实际 active `BepInEx/plugins/` 路径均读回匹配。

候选完成运行时解压、游戏资源复制及 Java Hook 安装后出现以下事实：

1. 离线条件导致 Unity 基础库下载 `UnknownHostException`，候选禁用自动下载；unstripped libunity 下载也失败。日志宣告回退游戏原版 libunity，但 staged `active.cfg` 仍为 `useOriginalLibUnity=false`。未到 native 加载阶段，不能证明实际回退行为或认定此差异导致后续异常。
2. `FakeActivity.getOriginalActivity(Native Method)` 抛 RuntimeException，消息为不透明编码。保留完整调用栈；不猜测具体 SDK 检查，不称其为日志首个错误或唯一根因。
3. 25.74、49.74、65.14、91.30、144.93 秒均为相同黑屏 PNG，焦点在候选 `StubActivity`。零 UI 输入；180 秒 watchdog 执行停止，180.23 秒确认全部相关 PID 已消失。最终截图是停止后的桌面，不用作游戏画面证据。
4. 运行中通过 `run-as` 成功读取 `:game` maps，可见游戏 libbridge，未见 libmain、libfusion、libunity、libil2cpp 或 libcoreclr 映射；LogOutput.log 与 interop 目录不存在。最终 logcat 没有诊断插件加载标记。本轮未见先前试验的 ART SIGSEGV，不把旧崩溃混入当前结果。

因此本轮只确认**该候选容器路线在上述输入、离线条件和窗口内没有跑通**。BepInEx BasePlugin、Harmony 实际调用、组件 Update/OnGUI、配置非默认值保存与冷启读回均未抵达，状态为 unknown / 未验证，不能写成通过或这些 API 已失败。

## 适配代码盘点

按冻结产品 csproj 的 literal Compile Include 解析、真实路径去重：56 份源码，共 12,666 物理行，其中 Android 本地 27 份 / 3,251 行，PC 共享链接 29 份 / 9,415 行。计数含空白、注释和预处理指令；方法定位是正则 locator，不称为 AST 精确函数 LOC。排除测试、文档和生成 interop。

| 旧源码范围 | 物理行 | 判断 |
|---|---:|---|
| `android/AssemblyInfo.cs:1–16` | 16 | 插件元数据与显式注册策略可替换；仍须保持单一 Harmony 注册路径。 |
| `android/AndroidCoroutine.cs:1–12` | 12 | 实际候选有 WrapToIl2Cpp；native owner、停止与异常等价性未验。 |
| `android/HoldBridges.cs:16–49` | 34 | 日志桥有替换入口；浮球展开态契约继续保留。 |
| `android/MobilePlayerConfig.cs:23–47,54–124,335–338` | 100 | 配置 API 可接 ConfigEntry / ConfigFile；其中业务默认、范围和初始化职责仍需保留。 |
| `android/Probe.cs:2,6–8,11–13,20–21,184,198–219` | 32 | 入口与回调接线可改写；Tick、注册次序和 UI/native 生命周期仍需实现。 |

前三项 62 行为毛替换候选，五项 194 行为旧源码触及范围，均非净删量。21 份共享源码的 Android 条件体计 829 行，但包含真实业务，不能当作纯加载器适配或可省代码。旧全量编译错误数、未接配置键数也不参与收益计数。

移动面板、中文样式、小浮球、触屏、UGUI hit surface、贴图裁剪、日历与人口展示，以及 Android 游戏签名、对象池、存档、原生字段责任和 Harmony 目标仍需保留。GlobalAliases 是否可省取决于该游戏实际生成 namespace，目前没有候选 interop 输出，未验证。无消费方的 ModDataPaths 单列为现有未用代码，不能把它的清理归功于更换加载器。

## 交付与后续界限

本批仅报告及证据摘要，不修改产品、不删除补偿、不切换加载器。原始 APK、候选 runtime、interop、用户存档、全量设备日志与模型事件留在本机私有任务目录，不提交仓库；关键私有证据 SHA256 见 [evidence-manifest.json](evidence-manifest.json)。

静态范围与运行事实由独立 reviewer 检查；GLM 5.3 max 公开模型事件确认报告关口。受管 Task 登记、共享 progress 及 Issue 状态由 Primary 按 normal 入口处理；未登记或 PR 未合并不宣称完成 canonical 任务，不裸改 checklist/DB。玩家实机及联机均未验。

后续若继续验证，应把“保持原游戏 Application/Activity 的同进程 BepInEx bootstrap”作为独立工程选项：先核实际 native 入口、可构建依赖和 Android linker/IL2CPP 生命周期，再测试零 Hook 插件；成功后才验证 Hook、组件、配置及冷启。它尚未实现，也没有可靠工期或净省代码估计。本轮不重试旧字符串恢复、SDK 绕行或复制现行 loader payload 冒充 BepInEx。
