# 开发者重建与替换说明

玩家双击启动不需要 SDK。本文面向修改加载链的开发者；它记录本包使用的精确上游与补丁顺序，不把历史实验归档称为通用 ARM64 安装器。新的编译器或源码目录可能改变调试元数据/MVID，从源码构建得到的 DLL 不保证逐字节相同；输入、代码变化和实际平台验收要分别记录。

## 11.0.0 Mac ARM64 重建基线（2026-10-25）

### 来源与允许差异

- 玩法源码基线：`fa54574426a528f135e79b2f9d9bf4c1c9a80e15`（Mac 正常安装合并提交）。审查别名 `c7579228c2e634e6b2cd5ccd4f011a07b6672238` 的整个 `il2cpp/` tree 与之一致，不另立发布基线。
- 623 个冻结输入（343 C# + 5 props + 275 Assets）与该提交逐字节一致；规范化闭包 SHA-256 为
  `7705451ca7b4d6022436b2e72f0ec81f17a82d994cb10d8b16c78c1e3b5914ff`。
- 已安装基线为 default-net6.0 DLL，SHA-256 `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf`，9,100,288 bytes；该 SHA 只标识已安装基线，**不是 11.0.0 重建产物**。
- 允许差异仅限版本/来源元数据：`il2cpp/KingdomEnhancedMod.csproj` 的 `<Version>11.0.0</Version>`、生成 `PluginInfo.PLUGIN_VERSION=11.0.0`、启动日志 `build=11.0.0-mac-arm64`、AssemblyVersion/FileVersion/InformationalVersion 11.0.0、包名/manifest/SHA256SUMS。GUID 与插件名不变；玩法逻辑、宏和依赖保持；下述出处元数据处理是明确列出的唯一资源例外。
- 不得从主仓 HEAD 或未安装候选借入源码，包括 #250、#276、#282、#284、#287、#292、#293；不得加入 `ANDROID`、`PALACE_PAID_CAPTURE_DIAGNOSTICS` 或任何 PALACE_* 测试宏；不得借正式 11 升级 BepInEx / HarmonyX / Il2CppInterop、重新生成 interop 或替换游戏构建。

### 发布出处元数据处理

源码提交 `e94342e38c922f593b6152be3b43bec0d9e3cf5a` 在上述已安装基线之上，只更新版本戳，并把三个宫廷 JSON 中 18 处作者机路径改为相对出处引用。相对 `.local/tasks/...` 仅在作者机可解析，不是运行时文件依赖。图片、布局、帧、调色数值、保存与收费逻辑均不变。

精确资源差为 `Palace400R12/palette.json`、`registry.json`、`offline-approval.json`、`SHA256.json` 及 `PalaceWorldFull/architecture/manifest.json` 五项；其余 270 项逐字节相同，含全部 267 PNG、`Palace400R12/layout.json`、`PalaceWorldFull/actors/manifest.json` 和一份 MD。重算完整性链与 `BootstrapIndexSha256`，没有关闭校验。批准记录保留原审查/日期/状态，新增 `releaseMetadataRevision` 说明本次重算及原 semantic 摘要，不宣称新增 Native 验收。

- 原始 11 DLL：`73e76e2886cc37b82826996fac2e8a9e47119623c930cce3d5434b69f9144982`。
- 仅清理 CodeView 路径后的发行 DLL：`8b47537731c0c166ac22634729f73a29d4f7cde0733ed7105015f0561d380b85`（9,100,288 bytes）。
- `SourceRevisionId` 明确绑定上述源码提交；最终 tag 可能另含包文档、锁表和脱敏 pin，必须保持相同 `il2cpp/` tree。
- 精确下载 ZIP SHA 及最终 tag commit 记录在包外 `RELEASE_MANIFEST.json` / `SHA256SUMS`，不在 ZIP 内自引用。

### 构建契约

- SDK 8.0.425；TargetFramework `net6.0`、Configuration `Release`；`AssemblyName=KingdomEnhancedMod`、`LangVersion=latest`、`unsafe=true`。
- 显式 `DefineConstants=IL2CPP;BIE;BIE6`；`EnableDefaultCompileItems=false`、`EnableDefaultEmbeddedResourceItems=false`。
- 资源入口 `EmbeddedAssets.props` + `PalaceAssets.props`（后者导入 `PalaceCapitalAssets.props`）；161 个本地引用（34 core + 127 interop），Private=false。
- 业务 profile 固定 `DedicatedR7V1`（28 项：18 true / 10 false）；不读环境变量，不打开 QA/诊断/legacy 分支。
- `BepInExPluginsPath` 为空、`NoDeploy=true`、独立 OutputPath/obj、`UseSharedCompilation=false`。实际 Mac 生成项目是保留在仓库外的 default-net6.0 wrapper；不得直接用 tracked Windows csproj 替代，也不得从 wrapper 所在父 repo 自动借入来源标签。

包内 `tools/mod-build-net6.csproj` 是实际包装项目的路径参数化副本，161 个显式引用名单/哈希见 `tools/mod-references-v11.0.0.json`。使用该源码提交的 `il2cpp/`，以及同游戏生成的 interop 和包内 core；引用不匹配时先停，不从其它版本补 DLL。`PluginInfo` 固定三常量来自包内 `.cs.txt`，复制为新私有目录的 `.cs`，不会被构建器覆盖。

```sh
cp tools/PluginInfo-v11.0.0.cs.txt "$BUILD_DIR/PluginInfo.cs"
dotnet build tools/mod-build-net6.csproj -c Release --nologo -nodeReuse:false \
  -p:ModSourceDir="$SOURCE_DIR/il2cpp" \
  -p:PluginInfoPath="$BUILD_DIR/PluginInfo.cs" \
  -p:ReferenceRoot="$REFERENCE_DIR/BepInEx" \
  -p:ReleaseOutputDir="$BUILD_DIR/bin" -p:ReleaseObjDir="$BUILD_DIR/obj" \
  -p:BepInExPluginsPath= -p:NoDeploy=true
```

`BUILD_DIR` 须为新建的私有构建目录，先创建它与临时目录；SDK home、NuGet、TMP 也指向该目录，并设置 `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`、`DOTNET_CLI_USE_MSBUILD_SERVER=0`。SDK 8.0.425 隐式框架引用锁为 net6 6.0.36；源码路径变化允许改变 PDB/MVID，须逐方法/资源比较，不能冒用原 raw SHA 调用脱敏器。未知构建输入须另行审查并钉新 SHA。

### 产物核对

- 构建应为 0 warning / 0 error；输出 DLL 的 AssemblyVersion/FileVersion 为 11.0.0.0，InformationalVersion 含 11.0.0；启动日志为 `build=11.0.0-mac-arm64`。
- 逐项核对 275 个嵌入资源的 logical name/bytes、161 个引用、623 个输入闭包；默认构建不得出现诊断宏或 `diagnostic` 变体。
- 发布关口必须填入并复核：新 Mod DLL SHA-256 与 size、`input-lock.json` 的 `mod_dll_sha256`、`package-manifest.json` 的 `version`（11.0.0）、`SHA256SUMS`、最终 ZIP SHA-256、tag/release 与 asset 名单。未填前不得把本包称为 11.0.0 已发布。
- 不得复用历史 DLL/ZIP SHA（如 `162046...`、`566525...`、`055368...` 等）作为 11.0.0 产物；历史值只用于对账来源。

## 原始输入

- BepInEx 原始分发：`https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.788%2B5b766a3.zip`。仅使用其中托管 core 与 Universal Doorstop；其中 x64 原生 Dobby/CoreCLR 不能用于 ARM64。原输入 SHA 由各 patcher 校验，未知输入直接拒绝。
- ARM64 CoreCLR 为 .NET 6.0.36 osx-arm64。`dotnet/.version` 与 `Microsoft.NETCore.App.deps.json` 对应 `f1dd57165bfd91875761329ac3a8b17f6606ad18`。附加 Microsoft.Extensions/Bcl DLL 是 6.0.0，来源在第三方清单中单列。
- Dobby 基线 `888d971214900374edbca6206fad6ded8a2c1311`。所有源码归档来源和 SHA 见 `third-party/source-inventory.json`；BepInEx、Il2CppInterop、Doorstop 的精确原始源码也在该目录中。
- 历史（v9.5.13 / v9.14.24）：v9.5.13 Mac 预览包玩法源码为 `1088b9cd981efb32aa3afb03e91eea66ad1a451b`；测试收口提交 `7fde1e555cda95e3acea73b1c6455116c6abd611` 不修改该版生产 `il2cpp/` 树。v9.14.24 Mac 预览包玩法源码基线为 Windows 正式 `v9.14.24` 的 `28425cca45f7087a63c0205cb04e4e5756884874`。这些来源与 DLL 收据不能沿用到 11.0.0；11.0.0 见上一节。Mac 原补丁归档仍固定 `9027335541b64621a0b8cf277bb07974bdc518b6`。

若要从零构建上游 BepInEx/Interop，可解压对应源码、按其中项目文件恢复依赖后构建；源仓库的外部编译引用 DLL 未随本包重复分发。精确排除清单、完整官方源归档 URL 与 SHA 见 inventory。不要用本机另一个更新版本的上游 checkout 冒充本发行库来源。

## 托管修补链

工具需要 .NET SDK 8；三个 runtime helper 目标框架为 net6.0。从 `third-party/sources/ohmymods-mac-compat-9027335-source.tar.gz` 解压得到 `compat/`。在新的工作目录中复制源码再构建，原始分发和游戏安装仅作只读输入。

归档 ARM64 工具的项目引用历史实验位置。可在**构建副本**的 `.csproj` 中把该历史引用改为 `$(BepInExCoreDir)/Mono.Cecil.dll`，然后每次 `dotnet build` 传 `-p:BepInExCoreDir=<core-dir>`。这是引用路径参数化，不改变 patcher 的输入 SHA/IL 形状守卫。x64 目录下项目已支持此属性。不要移除任何输入守卫来适配另一版本。

先构建：

- `compat/macos-x64/tools/mac-shim`：Debug，产生 `OhMyMods.MacCompatibility.dll`；引用原始 Iced。
- `compat/macos-arm64/tools/entry-shim` 与 `injection-shim`：Release，产生 `OhMyMods.Arm64Entry.dll`、`OhMyMods.Arm64Injection.dll`。
- 下列 patcher 项目：Release；每个工具在其目录的 `bin/Release/net8.0/` 下生成可用 `dotnet <工具.dll>` 执行的程序。

以下路径名是工作目录内的输入/输出变量，所有输出都应新建，不覆盖原始 DLL。按表中顺序调用对应 patcher 的程序参数：

| 工具源码目录 | 程序参数（依序） | 结果 |
| --- | --- | --- |
| x64/tools/cpp2il-patcher | 原始 Cpp2IL.Core.dll、原始 LibCpp2IL.dll、输出 Cpp2IL.Core.dll、输出 LibCpp2IL.dll | Mach-O/metadata 固定输入兼容 |
| x64/tools/interop-patcher | 原始 Il2CppInterop.Runtime.dll、MacCompatibility helper、`runtime-module.dll` | Mac 模块定位 |
| arm64/tools/injection-patcher | `runtime-module.dll`、Arm64Injection helper、最终 Il2CppInterop.Runtime.dll | 固定 ARM64 注入目标/ABI |
| arm64/tools/patcher | 原始 BepInEx.Preloader.Core.dll、最终同名 DLL | Console writer 兼容 |
| arm64/tools/trampoline-patcher | 原始 BepInEx.Unity.IL2CPP.dll、`trampoline.dll` | typed trampoline |
| arm64/tools/entry-patcher | `trampoline.dll`、Arm64Entry helper、`entry.dll` | runtime_invoke 入口 |
| arm64/tools/thunk-patcher | `entry.dll`、`thunk.dll` | x86 thunk 架构门 |
| arm64/tools/near-patcher | `thunk.dll`、最终 BepInEx.Unity.IL2CPP.dll | native near Hook 返回值检查 |

归档目录名以上表的 x64/arm64 简写代表 `compat/macos-x64` / `compat/macos-arm64`。`evidence/managed-inputs-and-stages.json` 记录阶段 SHA，`candidate-manifest.json` 记录历史最终 DLL。发行时只对自有四个 DLL 的 CodeView PDB 路径做结构化清理；IL、资源、MVID 不变，新 SHA 与历史 SHA 不混用。清理工具在 `tools/sanitize_codeview.py`，它只接受明确锁定的原始四个输入；自建不同字节的组件需要维护自己的清单与验收，不能绕过工具守卫冒用原包回执。

## 原生 Dobby

在干净固定源码副本应用 `compat/macos-arm64/patches/dobby-macos-arm64.patch`。本包该副本与此前通过游戏验收的源码逐文件一致，仅通过编译参数去除本机路径。

```sh
map="-ffile-prefix-map=$src=dobby-source -fdebug-prefix-map=$src=dobby-source -ffile-prefix-map=$out=dobby-build -fdebug-prefix-map=$out=dobby-build"
TZ=UTC SOURCE_DATE_EPOCH=1789905600 cmake -S "$src" -B "$out" \
  -DCMAKE_SYSTEM_NAME=Darwin -DCMAKE_SYSTEM_PROCESSOR=arm64 \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0 -DDOBBY_DEBUG=OFF \
  -DDOBBY_GENERATE_SHARED=ON \
  -DCMAKE_C_FLAGS="$map" -DCMAKE_CXX_FLAGS="$map" -DCMAKE_ASM_FLAGS="$map"
cmake --build "$out" --target dobby
```

本次新库 SHA：`8fbfdd239f9c1a7f277643a46e46f13d8e7e61a8997d58b5451104dbdbd404d6`。编译器为 AppleClang 21.0.0.21000101；不同工具链不承诺相同 SHA。五组短函数布局、43 项原生调用检查、15 项最近页策略检查已通过；11,503 条指令助记符序列相同，差异为 87 处字符串地址相关的 adrp/add 操作数。最终包启动验收仍单列，不能仅凭这组测试声称可玩。

## Mod 与发行打包

Mod 对锁定 core 与玩家本地合法游戏生成的 interop 引用编译，目标 net6.0、定义 `IL2CPP;BIE;BIE6`。11.0.0 使用 275 个 EmbeddedResource（`EmbeddedAssets.props` 38 + `PalaceAssets.props` 145 + `PalaceCapitalAssets.props`/`KEM_Palace400R12` 92），逻辑名与加载路径必须与冻结基线一致。历史预览包曾使用 13 张 EmbeddedResource；该数字不代表 11.0.0。游戏及其生成程序集不是源码/发行包内容。11.0.0 的 Mod DLL SHA 尚未生成；必须以自身构建输出、input-lock 的 `mod_dll_sha256` 和包 manifest 三者逐字节对账，不能复用历史 SHA（如 `162046...`、`566525...`）。

`input-lock.game.source_tag` 的 `1088b9c...` 记录首次锁定上述游戏四指纹时所用的仓库基线，**不是 11.0.0 Mod 源码提交**。11.0.0 源码来源为 `fa545744...`，验收结论以 `VALIDATION.md` 的 11.0.0 记录与对应 GitHub Release 为准。

`build_package.py` 从明确的 staging、input-lock 和第三方材料清单构建 ZIP，不从游戏安装目录整体复制。运行参数和清单结构见 README。ZIP 固定条目时间、顺序和权限；任何源文件、构建设置或组件变化都需要新 hash、重新审查及相应平台验证。包中普通动态库可被替换，源码和修补工具公开；替换者需同步维护自己的 SHA 清单。
