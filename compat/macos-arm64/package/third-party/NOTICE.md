# 第三方组件、修改与源码

此包是 ohmymods 的固定游戏构建兼容版本，不代表 BepInEx、Il2CppInterop 或其他上游发布的正式 Mac ARM64 支持。游戏本体、游戏 metadata、Unity 库及从游戏生成的 interop 不随包分发。

各许可证全文在 `licenses/`；组件来源及下载内容 SHA 在 `upstream-downloads.json`；附带源码的精确清单在 `source-inventory.json`。包内二进制实际 SHA 以发行 manifest 为准。此说明不更改各上游许可证，亦不将 ohmymods 自有代码重新授权为某一第三方许可证。

| 组件 | 固定上游来源 | 许可文本 | 本包修改 |
| --- | --- | --- | --- |
| BepInEx Core/Preloader/Unity.Common/Unity.IL2CPP | `BepInEx/BepInEx` `5b766a3b7f6c164d4798924a93f3acf4db769d06`，6.0.0-be.788 | BepInEx.txt（LGPL-2.1） | Preloader.Core 的 Console writer 兼容；Unity.IL2CPP 的 trampoline、入口解析、架构判断与 near hook 错误检查 |
| Il2CppInterop Runtime/Common/Generator/HarmonySupport | `BepInEx/Il2CppInterop` `dbda1cb353b0f4253345dc45136d170b9e50a5a0`，1.5.3 | Il2CppInterop.txt、GPL-3.0.txt | 仅 Runtime：Mac 模块定位、固定构建注入 ABI 适配 |
| Cpp2IL.Core / LibCpp2IL / StableNameDotNet / WasmDisassembler | `SamboyCoding/Cpp2IL` `558ddd98642010897d54316b51fbaa7889fda093` | Cpp2IL.txt（MIT） | 前两项修补 Mac 固定输入解析；后两项未修改 |
| Dobby | `BepInEx/Dobby` `888d971214900374edbca6206fad6ded8a2c1311` | Dobby.txt（Apache-2.0） | ARM64 4 字节 near 分支、最近可用页选择、固定源码补丁；编译路径映射为公开相对路径 |
| UnityDoorstop | `NeighTools/UnityDoorstop` `33dab9a6733862eb81869ff08431d9478b28784b`，4.5.0 | Doorstop.txt（LGPL-2.1）；Doorstop-plthook-*.txt（BSD） | be.788 包内 Universal dylib，未修改 |
| .NET CoreCLR / Microsoft.NETCore.App osx-arm64 | `dotnet/runtime` `f1dd57165bfd91875761329ac3a8b17f6606ad18`，6.0.36 | dotnet-runtime.txt、dotnet-runtime-notices.txt | 未修改 |
| Microsoft.Extensions.* / Microsoft.Bcl.AsyncInterfaces | `dotnet/runtime` `4822e3c3aa77eb82b2fb33c9321f923cf11ddde6`，6.0.0 | dotnet-extensions.txt、dotnet-extensions-notices.txt | 未修改；不能与 runtime pack 混标成 6.0.36 |
| AsmResolver.* | `Washi1337/AsmResolver` `124e161a9bc0ebb83b4d97276ae3da3c9cfe2fc5` | AsmResolver.txt（MIT） | 未修改 |
| AssetRipper.CIL | `AssetRipper/AssetRipper.CIL` `06216e69825ccd6536dc17e9ca81c4d539ce9321` | AssetRipper.CIL.txt（MIT） | 未修改 |
| AssetRipper.Primitives | `AssetRipper/AssetRipper.Primitives` `d759b3db98bf2a1cb8c2181faf5d405610aac585` | AssetRipper.Primitives.txt（MIT） | 未修改 |
| Gee.External.Capstone | `ds5678/Capstone.NET` `b90e380c14e857865c94a355954236e678bd557c`，2.3.2 | Capstone.NET.txt（MIT） | 未修改 |
| Disarm | `SamboyCoding/Disarm` `8574010015c8c56bb7faad7e407c50b0abe9cc72` | Disarm.txt（MIT） | 未修改 |
| Iced | `icedland/iced` v1.21.0 | Iced.txt（MIT） | 未修改 |
| Mono.Cecil / Rocks / Pdb / Mdb | `jbevain/cecil` 0.11.4 | Mono.Cecil.txt（MIT） | 未修改 |
| MonoMod.Utils / RuntimeDetour | `MonoMod/MonoMod` v22.07.31.01 | MonoMod.txt（MIT） | 未修改 |
| MonoMod.Backports / ILHelpers | `MonoMod/MonoMod` `a1b82852b2574742776af08818487b90b0bfab93`，1.1.2 / 1.1.0 | MonoMod.Backports.txt、MonoMod.ILHelpers.txt（MIT） | 未修改；ILHelpers 已逐字节匹配 NuGet 1.1.0 的 net6.0 DLL |
| 0Harmony（HarmonyX） | `BepInEx/HarmonyX` v2.10.2 | HarmonyX.txt（MIT） | 未修改 |
| SemanticVersioning | `adamreeve/semver.net` 2.0.2 | SemanticVersioning.txt（MIT） | 未修改 |

## 对应源码与修改方式

`sources/` 包含 BepInEx、Il2CppInterop、Doorstop、Dobby 的固定上游源码，以及 ohmymods Mac 补丁/helper 源码。各源码目录保留原许可证。BepInEx 和 Il2CppInterop 源仓库内的四个第三方/生成 DLL 已从本源码归档排除，具体路径写在 inventory 中；它们不是本包修改的组件源码，也不作为运行二进制分发。需要从源码构建上游时，可从相同 commit 的官方源码仓库获取对应编译引用，或使用自己的合法游戏生成引用。完整官方归档的 URL 与 SHA 同时保留，排除情况不冒称为原始归档逐字节副本。

Mac 补丁目录保留原输入 SHA 守卫、工具源码和测试；重建顺序见上一级 `REBUILD.md`。Cecil 补丁是对明确版本程序集的确定范围修改，原版源代码和修改工具同时提供。动态库及托管库位于普通文件目录，可在遵循各自许可的前提下替换、修改和重新构建；启动器的 SHA 清单用于验证发行组合，没有签名/加密限制。自建兼容组合应同步更新自己维护的清单并重新验证，不能把发行版验收结果套用到新组合。

首次启动会由加载器从 `https://unity.bepinex.dev/libraries/6000.0.61.zip` 获取 Unity 基础库。该内容及生成的 interop 留在玩家本地，不包含在本发行 ZIP 或源码包中。

## Dobby 内含第三方源码

`builtin-plugin/SymbolResolver/macho/shared-cache/dyld_cache_format.h` 为 Copyright (c) 2006-2015 Apple Inc.，适用 Apple Public Source License 2.0，全文另附 `licenses/Apple-APSL-2.0.txt`。该文件被本次 ARM64 `dobby` 目标实际引用；随包保留完整原文件及版权头，没有修改它，不能将它重标为 Apache-2.0。许可正文来自 Apple 官方源码仓库，Git blob 和包内 SHA 在下载记录中。

Dobby 源码选集排除了本目标完全未引用的 `SupervisorCallMonitor/XnuInternal`、`ExecMemory/substrated` 与两项遗留 `UnifiedInterface/semaphore` 文件，逐路径排除清单已写入 inventory。此源码选集针对本包 ARM64 `dobby` 目标，不宣称支持原仓库所有可选插件或 iOS 目标；完整上游归档仍由原 URL/hash 指向。
