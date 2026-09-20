# worker-brief：v9.4.5 macOS 试跑（Mac 端 agent · 只验证不修改）

角色：Mac 端试跑 agent（验证者）。**只验证、不修改 MOD 代码**；除安装 BepInEx 外不改游戏文件。
产出：回执（格式见下），由用户转交 operator 汇入本任务目录 `docs/project-harness/tasks/mac-trial-945-20260919/`。
三阶段推进，**任一阶段失败即止步收证**，不做未授权的修复尝试。

## 目标

验证 Windows 产物（v9.4.5 插件 DLL）能否在 macOS 版游戏上加载运行。

## 阶段〇 · 前置检查（任一不满足即止步回报）

1. 游戏为 2.4.0 且 IL2CPP：`<Game>.app/Contents/Resources/Data/` 下存在 `il2cpp_data`（或等价产物）。
2. 记录主程序架构（`file`/`lipo`：x86_64 / arm64 / universal）——**BepInEx Mac 构建只有 x64**（已核实官方产物列表无 macos-arm64），Apple Silicon 需强制 Rosetta 打开（显示简介→"使用 Rosetta 打开"）。
3. 记录芯片型号、macOS 版本。

## 材料

- `KingdomEnhancedMod_v9.4.5_IL2CPP.zip`（GitHub Release v9.4.5 附件，SHA256
  `8961fd17aaad665f4b4d4d0b7c8c661a17ca43b1fbf444893155b4795d6b3e9d`，Mac 侧下载后先校验）。
- BepInEx 6 **macOS x64 bleeding edge ≥ #755**（Unity 6 IL2CPP metadata v23-106 支持自 2026-03-07 #755 起；直接取最新 bleeding 并在回执记录构建号——#779-#785 期间 Cpp2IL 有过反复回退，避免精确卡某个旧号。Windows 侧为 be.752，两侧加载器版本不同属预期；插件目标框架 net6.0，DLL 本身平台无关）。
- 源码参考：GitHub 公开仓库 tag `v9.4.5`（只读；**第一轮禁止在 Mac 上重新编译**）。

**ZIP 使用规则（单文件白名单）**：从 ZIP 中**只复制一个文件**——`BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll`——放进 Mac 的 `BepInEx/plugins/KingdomEnhancedMod/`。
**禁止**把 ZIP 整包解压进游戏目录或复制其中任何其他内容（`BepInEx/core` 是 Windows be.752、`dotnet/` 是 Windows x64 运行时、`winhttp.dll`/`doorstop*` 是 Windows 注入件、`BepInEx/unity-libs` 与 `BepInEx/config` 会污染 Mac 侧——任何一项混入都会制造假失败）。Mac 侧 BepInEx/加载器/运行时/配置全部使用 Mac 版 BepInEx 自带的。

## 阶段一 · 裸加载器

按 BepInEx 官方 macOS 指南安装（含 Gatekeeper/quarantine 处理），启动游戏，确认 `BepInEx/LogOutput.log` 生成且无致命错误。
注意：Mac 首次启动时 Il2CppInterop 生成 interop 程序集需**联网**下载 Unity 基础库（`UnityBaseLibrariesSource` 默认指向 unity.bepinex.dev）——若下载/生成失败，先记录网络状态，按**环境问题**止步回报，不作为 MOD 或加载器失败的结论。失败即止。

## 阶段二 · 加载 MOD

把 `KingdomEnhancedMod.dll` 复制进 Mac 的 `BepInEx/plugins/KingdomEnhancedMod/`，启动。**成功标志**（用消息体子串 grep，不依赖日志前缀对齐）：
- `Loading [KingdomEnhancedMod 9.4.5]`
- `Plugin KingdomEnhancedMod v9.4.5 build=9.4.5 loading...`
- `Plugin KingdomEnhancedMod loaded. Enabled=`（命中后核对值为 True；若为 False 本身即异常信号，记录）
- 全程 **0 Error / 0 Exception**（Warning 如 `Class::Init signatures exhausted` 不算失败）。

重点记录（如有）：ClassInjector/Il2CppInterop 类型注册异常（本 MOD 有注册的自定义组件）、Harmony 原生 detour 异常的**完整栈**。Windows 侧历史坑提示（仅供观察，不预置结论、不自行处理）：ClassInjector 对 out enum 参数曾产生 InvalidProgramException（Windows 已用 IntPtr 输出桥规避）；x64 Mac 的 HarmonyX native detour 行为未验证过。回执顺带记录 `BepInEx/config/BepInEx.cfg` 中 `DetourProviderType` 的值与 Mac 侧 .NET Runtime 版本（日志头部）。

## 阶段三 · 功能冒烟（阶段二通过后）

- F5 / Ctrl+F10 面板可开关；
- 人口 HUD（默认开）显示；
- 开 2~3 个**真实默认关**功能（例：弓箭页散射、便捷页长按连续购买/坐骑无限体力、自动补货任一项）确认交互正常、日志干净。
不求全功能，求稳定加载 + 核心面板。冒烟清单结果逐条记录。

## 回执格式（发回给用户转 operator）

1. 止步阶段（〇/一/二/三/全部通过）；
2. 环境五项：芯片型号 / macOS 版本 / 游戏架构与版本 / BepInEx 构建号 / Mac 侧 Runtime 版本；
3. `BepInEx/LogOutput.log` 关键段或全文；
4. 异常栈（如有，完整）；
5. 冒烟清单结果；
6. `DetourProviderType` 值。

## 边界

- 除安装 BepInEx 外不改游戏文件；不反编译、不重打包。
- 动存档前备份 `~/Library/Application Support/noio/`（本试跑优先用全新存档档位，不动现有进度）。
- 日志与存档不公开转发（含个人数据）。
- 不在 Mac 上修改/编译 MOD 源码；发现的问题记录回执，由 Windows 主线决策修复。

## 停止条件

任一阶段失败即止步；需要越界操作（改游戏本体、重新编译、动用户存档、改 MOD 配置文件除外的 BepInEx 调试项）时停止并回报。
