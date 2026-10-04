# CompileInventory（B0 诊断工程）

对 Android 线全量共享源码做“API 面”静态编译盘点：用真实 Android interop + loader 引用，把
**尚存的桌面/BepInEx 平台边界引用**与**尚未移植的 ModConfig 成员**逐条列出来。诊断专用：
不被 `android/OhMyMods.AndroidProbe.csproj` 引用、不打包、不加载、不运行。

## 构建

```sh
dotnet build android/compat/CompileInventory.csproj -c Debug \
  -p:GameInteropDir=<interop 目录> \
  -p:LoaderRuntimeDir=<loader net6 目录> \
  -p:SupportModuleDir=<SupportModules 目录>
```

- 源：`../../il2cpp/**/*.cs`（显式排除 9 个平台替代原件；Android 由 `android/` 对应文件顶替，
  映射权威：`D/full-android-compat-inventory/feature-groups.json` `replacedOriginals`）
  + `../*.cs`（android 顶层 24 个实际适配文件；`tests/`、`compat/`、`obj/`、`bin/` 等子目录不匹配）
  + `DiagnosticGlobalAliases.cs`（诊断专用 Rewired alias，不进 main）+ 构建时生成的
  `MyPluginInfo.g.cs`（`GenerateMyPluginInfo` 用 XmlPeek 读真实工程属性 AssemblyName/Version →
  WriteLinesToFile 到本项目独立 obj（`$(IntermediateOutputPath)`）；单一来源、随真实属性变化，
  不在仓库里手抄常量）。
- 引用：GameInteropDir 全部真实 Android interop + loader 6 个 + support 1 个；三参数必填、
  无默认路径。可选 `-p:DesktopMetadataDir=<BepInEx/core>` 仅诊断对照（编译期引入
  BepInEx.Core + BepInEx.Unity.IL2CPP 两个 DLL 解析平台边界名；非 Android 运行时组件）。
- 33 个声明资源：`CollectDeclaredResources` 用 XmlPeek 从 `il2cpp/KingdomEnhancedMod.csproj`
  读取 EmbeddedResource Include 列表；LogicalName 按本项目当前既有的 `<RootNamespace>.<文件名>.<扩展名>`
  命名规则派生，并与原声明列表做数量守卫（**不是任意 LogicalName 自动透传**——该限定规则已复核
  33/33 与原声明一致；它与上面“源版本属性自动生成”是两件独立的事），不维护第二份资源清单。

## 预期结果（当前源码树；均为已命名 seam，不要为凑绿而 stub/删文件）

- **default（无 DesktopMetadataDir）**：声明层 6 个错误，全部为已命名 BepInEx 平台边界——
  `CoinCourierRuntime.cs`（BepInEx.Configuration、ConfigEntry<>）、
  `PatchExtensionIsland_Progression.cs`（BepInEx.Unity.IL2CPP.Hook、INativeDetour）、
  `PatchWorld_OptionalVegetation.cs`、`AutoRestockCounts.cs`（BepInEx.Configuration）。
  声明层有错时编译停在该层，不再列出 body 层错误（故 default 看不到配置成员缺口）。
- **optin（DesktopMetadataDir）**：107 = CS0117×92（ModConfig 缺失成员：91 调用点 / 44 个名字
  + 1 个 CrossbowmanRole）+ CS0121×15（WrapToIl2Cpp 混合扩展伪影，default 引用下不出现）。
  S3（`Logger.LogDebug`）与 Coatsink/Harmony 名称层已由 `android/GlobalAliases.cs` 的
  `Coatsink = Il2CppCoatsink`、六处 `#if ANDROID using Il2CppCoatsink.Common;`、两处
  `HarmonyLib.Harmony` 全限定关闭。

## 盘点口径（供批次规划）

230 源 / 54 Bind / 33 声明资源 / 16 功能组；余量估算 8–12 批合计 80–200 工程小时（含配置接线
24–60h）；phone / MP / 长期运行验收日历单列（未验证）。本工程只证明静态 API 面，
不证明运行时行为，也不代表可玩包。
