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

重新盘点时把 `-p:BaseIntermediateOutputPath` / `-p:MSBuildProjectExtensionsPath` / `-p:OutputPath`
指向私有目录，避免写产品树下的 obj/bin。

- 源：`../../il2cpp/**/*.cs`（显式排除 9 个平台替代原件；Android 由 `android/` 对应文件顶替，
  映射权威：`D/full-android-compat-inventory/feature-groups.json` `replacedOriginals`）
  + `../*.cs`（Android 顶层适配文件；`tests/`、`compat/`、`obj/`、`bin/` 等子目录不匹配）
  + `DiagnosticGlobalAliases.cs`（诊断专用 Rewired alias，不进 main）+ 构建时生成的
  `MyPluginInfo.g.cs`（`GenerateMyPluginInfo` 用 XmlPeek 读真实工程属性 AssemblyName/Version →
  WriteLinesToFile 到本项目独立 obj（`$(IntermediateOutputPath)`）；单一来源、随真实属性变化，
  不在仓库里手抄常量）。
- 引用：GameInteropDir 全部真实 Android interop + loader 6 个 + support 1 个；三参数必填、
  无默认路径。可选 `-p:DesktopMetadataDir=<BepInEx/core>` 仅诊断对照（编译期引入
  BepInEx.Core + BepInEx.Unity.IL2CPP 两个 DLL 解析平台边界名；非 Android 运行时组件）。
- 资源：直接 `<Import Project="$(Il2CppRoot)/EmbeddedAssets.props" />`，与真实工程、Mac wrapper
  同用 `il2cpp/EmbeddedAssets.props`；Include 与 LogicalName 以该 props 为唯一权威（路径按 props
  自身 `MSBuildThisFileDirectory` 解析），不再 XmlPeek 原 csproj 直接声明、不再派生命名、不维护
  第二份资源清单。资源只嵌入本诊断程序集，**不进 Android runtime**：不打包、不加载、不参与产品
  构建（compilation-only）。

## 当前实测结果（Issue #186，2026-10-08）

- **default（无 DesktopMetadataDir）**：5 个唯一错误、0 warning：CS0234×3、CS0246×2。
  `CoinCourierRuntime.cs` 与 `AutoRestockCounts.cs` 的 Configuration / ConfigEntry，及
  `PatchExtensionIsland_Progression.cs` 的 Hook / INativeDetour；停在声明阶段，不能据此认为
  后续方法体或原生接口全部兼容。
- **optin（DesktopMetadataDir，仅编译对照）**：118 个唯一错误、1 warning。
  CS0117×99 = 95 个 ModConfig 引用（42 个成员）＋3 个 `ImGuiCompat.DrawTexture` 引用＋
  1 个 `PopulationCounts.CrossbowmanRole`；CS0121×19 是两个元数据引用集的
  `WrapToIl2Cpp` 混合扩展歧义。唯一 warning 为 CS0649 `PrepareWindowObserver` 未赋值。
  该模式额外引入两个 BepInEx DLL，严格不进 Android 产品；118 不是118个原生API缺口。
- 三个图片引用位于 `CalendarHud`（2）和 `KnightStylePanel`（1），因 Android 条件分支未提供
  该接口；现有 Android 纯色 native 绘制已提供，完整图片/裁剪须在公共 UI 兼容批单独适配验证。
  弩手分类缺口属于 PopulationCounts 平台替代与后续弩手批的依赖，不补空常量凑绿。
- 原 B0 的 Logger.LogDebug 与 Coatsink/Harmony 名称适配保留。本批不顺手修新的平台或配置缺口。
- 修复前（Issue #162 资源迁移后）本工程在 PrepareForBuild 中止：
  `CompileInventory: no EmbeddedResource items found ...`（0 warning / 1 error）。
  改为公共 Import 后该错误不再产生；上述两模式实际进入源码编译阶段。
- 完整 raw 日志、输入/引用 pins、错误去重分类、MSBuild 求值与37项资源证明见本机私有任务
  `.local/tasks/android-port-resume-20261008/compat-refresh/`；公共记录见
  `docs/project-harness/tasks/issue-186/implementation.md`。
- ModConfig 成员缺口按后续 feature 批接入，不为凑绿 stub/删文件；本工程只证明静态 API 面，
  不证明运行时行为，也不代表可玩包。

## 本批现状数字（Issue #186 实际评估 pin）

- 资源：37 项 `EmbeddedResource`，逐项 path / LogicalName / SHA256 与真实 PC 工程实际评估集相同
  （本批 `validation-summary.json` 和 `canonical-resource-proof.json`；原 PC 工程 direct EmbeddedResource=0，资源全部来自
  Import 的 `EmbeddedAssets.props`）。
- 源集：PC 237 源 − 9 具名平台替代 = 共享 228；Android 顶层 26；诊断 alias 1；生成 1；
  合计 256 个项目源码项（不含 SDK 自动 AssemblyInfo；预求值255个现有文件＋1个待生成）。
  XML `Compile Include/Exclude` 不变，255个非生成项实际路径相同；MyPluginInfo生成槽位保持，
  其绝对路径随私有obj目录变化。两模式都实际生成该文件，三常量与原属性一致。
- PC 配置57 Bind；当前 Android Main47源码=26平台＋21共享源、20真实entry、35注册目标均未改。
  完整盘点扣除9具名平台原件，其余228共享源全部保留，不以stub或删失败源码造绿。
- 数字是编译表面，不是完整 runtime 依赖或完成率；phone / MP / 长期运行验收单列（未验证）。

## 当前剩余规划

按已完成的人口批与当前237源全范围，规划剩余8–10组、约80–210工程小时，含实现、兼容、
必要审查和模拟器验证；范围假设与各组账目见本任务implementation。公共图片/HUD→角色与宠物→
弓弩墙塔→跨世界单位/神器→坐骑身份→英雄/盾卫/火枪资产→银行送币与补货→联调整理。
这是宽区间规划；当前42配置成员、7份新PC源码与图片接口缺口用于分组，不直接换算完成率。
未经验证的池/身份/存档责任与原生API可改变估算；手机/联机/长期验收日历另列。

## 历史测量（当时口径，均非当前值）

- B0 / Issue #142：冻结 230 源 / 54 Bind / 33 声明资源；default 声明层 6 错；optin 107 =
  CS0117×92（91 调用点 / 44 名字 + 1 个 CrossbowmanRole）+ CS0121×15。
- Issue #144：合并后曾记录 232/234 源 / 57 Bind / 34 声明资源（来源
  `D/common-ui-inventory-delta-summary.md`）、Android 顶层 24→26（新增 `MobileModPanel.cs` /
  `PanelGesture.cs`）；optin 曾记录 124 = CS0117×105（104 调用点 / 47 名字 + 1）+ CS0121×19
  （新增 13 个 setting 调用点与 4 个 mixed 调用，均为既有 root cluster，不构成 native API 新结论）。
  该批与 #146 均未重跑本 diagnostic（Main 真编译 0W0E 另由 android 测试矩阵覆盖）。
- Issue #146：`PatchWorld_OptionalVegetation.cs` 移除 `BepInEx.Configuration`（三处
  `ConfigEntry<bool>` 改 `var`）并已由 Android Main 链接编译。
- Issue #162：资源清单迁入共享 `EmbeddedAssets.props`（官方与 Mac wrapper 同 Import）；ShopV2
  批新增 3 项后资源为 37；此后本工程 XmlPeek 直读原 csproj 声明的方式失效。
- 余量估算 8–12 批 / 80–200 工程小时（含配置接线 24–60h）与 65–180h / 6–10 组均为旧宽口径规划，
  仅作历史参考；本批新估算由 Root 实测后更新，不以编译错误数代替百分比或 runtime 完成度。
