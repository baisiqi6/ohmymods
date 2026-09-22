# 开发者重建与替换说明

玩家双击启动不需要 SDK。本文面向修改加载链的开发者；它记录本包使用的精确上游与补丁顺序，不把历史实验归档称为通用 ARM64 安装器。新的编译器或源码目录可能改变调试元数据/MVID，从源码构建得到的 DLL 不保证逐字节相同；输入、代码变化和实际平台验收要分别记录。

## 原始输入

- BepInEx 原始分发：`https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-macos-x64-6.0.0-be.788%2B5b766a3.zip`。仅使用其中托管 core 与 Universal Doorstop；其中 x64 原生 Dobby/CoreCLR 不能用于 ARM64。原输入 SHA 由各 patcher 校验，未知输入直接拒绝。
- ARM64 CoreCLR 为 .NET 6.0.36 osx-arm64。`dotnet/.version` 与 `Microsoft.NETCore.App.deps.json` 对应 `f1dd57165bfd91875761329ac3a8b17f6606ad18`。附加 Microsoft.Extensions/Bcl DLL 是 6.0.0，来源在第三方清单中单列。
- Dobby 基线 `888d971214900374edbca6206fad6ded8a2c1311`。所有源码归档来源和 SHA 见 `third-party/source-inventory.json`；BepInEx、Il2CppInterop、Doorstop 的精确原始源码也在该目录中。
- ohmymods 玩法源码固定为 `1088b9cd981efb32aa3afb03e91eea66ad1a451b`（v9.5.13）；测试收口提交 `7fde1e555cda95e3acea73b1c6455116c6abd611` 不修改生产 `il2cpp/` 树。Mac 原补丁归档固定 `9027335541b64621a0b8cf277bb07974bdc518b6`。

若要从零构建上游 BepInEx/Interop，可解压对应源码、按其中项目文件恢复依赖后构建；源仓库的外部编译引用 DLL 未随本包重复分发。精确排除清单、完整官方源归档 URL 与 SHA 见 inventory。不要用本机另一个更新版本的上游 checkout 冒充本发行库来源。

## 托管修补链

工具需要 .NET SDK 8；三个 runtime helper 目标框架为 net6.0。从 `third-party/sources/ohmymods-mac-compat-9027335-source.tar.gz` 解压得到 `compat/`。在新的工作目录中复制源码再构建，原始分发和游戏安装仅作只读输入。

归档 ARM64 工具的项目引用历史实验位置。可在**构建副本**的 `.csproj` 中把 `/Applications/ohmymods-arm64-lab/BepInEx/core/Mono.Cecil.dll` 改为 `$(BepInExCoreDir)/Mono.Cecil.dll`，然后每次 `dotnet build` 传 `-p:BepInExCoreDir=<原始core绝对路径>`。这是引用路径参数化，不改变 patcher 的输入 SHA/IL 形状守卫。x64 目录下项目已支持此属性。不要移除任何输入守卫来适配另一版本。

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

Mod 使用上述 tag 的 `il2cpp/*.cs` 与八个原项目 PNG EmbeddedResource，对玩家本地合法游戏生成的 interop、已锁定 core 引用编译，目标 net6.0、定义 `IL2CPP;BIE;BIE6`、版本 9.5.13。游戏及其生成程序集不是源码/发行包内容。当前已验收 Mod 原 DLL SHA 是 `162046f77e025d90b3d6c9b5ea25d92835cb0609c26563885898ae93dfc9fb8a`；发行前 CodeView 清理后的 SHA 单列在包 manifest 中。

`build_package.py` 从明确的 staging、input-lock 和第三方材料清单构建 ZIP，不从游戏安装目录整体复制。运行参数和清单结构见 README。ZIP 固定条目时间、顺序和权限；任何源文件、构建设置或组件变化都需要新 hash、重新审查及相应平台验证。包中普通动态库可被替换，源码和修补工具公开；替换者需同步维护自己的 SHA 清单。
