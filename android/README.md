# android/ — Kingdom Two Crowns Android 移植探针（源码）

目标运行时：Unity 6000.0.61f1（Android arm64）+ Il2CppInterop 1.5.1 + LemonLoader 部署的
MelonLoader 0.7.3（net6 loader 目录）。产物 `OhMyMods.AndroidProbe.dll` 沿用旧部署名，
避免旧 deployment refresh 策略残留第二个 mainMod。

## 构建

    <dotnet10>/dotnet build -c Release \
        -p:GameInteropDir=<INTEROP_DIR> \
        -p:LoaderRuntimeDir=<LOADER_NET6_DIR> \
        -p:SupportModuleDir=<SUPPORT_MODULES_DIR> \
        android/OhMyMods.AndroidProbe.csproj

- `GameInteropDir`：用户本机自有的目标 APK 经 Il2CppInterop 生成的互操作目录
  （`Assembly-CSharp.dll`、`UnityEngine.*.dll`、`Il2Cppmscorlib.dll`、`Il2CppSystem.Core.dll` 等；
  Issue #119 起还必须有 `UnityEngine.UI.dll` 与 `UnityEngine.UIModule.dll`，缺失即 MSBuild error）。
- `LoaderRuntimeDir`：loader 部署中的 net6 运行时目录（`MelonLoader.dll`、`0Harmony.dll`、
  `Il2CppInterop.Runtime.dll` 等），例如 LemonLoader 部署层
  `assets/LemonLoader/runtime/loader/net6`。
- `SupportModuleDir`：loader 部署中的 `Dependencies/SupportModules` 目录（`Il2Cpp.dll`，提供
  `MelonLoader.Support.MonoEnumeratorWrapper`，即 `AndroidCoroutine` 协程桥的唯一外部依赖）。
- 三个参数必填。目录不存在或缺关键 DLL 时构建直接给出 MSBuild error；工程不会自动下载、
  生成或安装任何依赖，也不会把游戏/loader 二进制复制进输出（`Private=false`）。
- 仓库不分发原版 APK、loader（含 winhttp/native host）或 interop 二进制；本目录只含 mod 源码。

## 源码构成

| 文件 | 用途 |
|---|---|
| `Probe.cs` | MelonLoader 入口、显式 Harmony 注册、浮球组件与触摸守卫 |
| `MobileUiInputSurface.cs` | Issue #119 原生 UGUI 命中面（新增）：两个透明 `Image` 命中区与 IMGUI 球/展开面板同矩形，由 `ProbeTicker` 生命周期驱动；不改原生菜单/输入标志 |
| `FloatInput.cs` `TouchClaims.cs` `FloatLayout.cs` | 浮球触摸归属与面板几何（Home 484 / World 562 / Player 406 / Population 376，绘制与触摸同源） |
| `MobileCalendar.cs` `CalendarSnapshot.cs` | 日历显示（开关直读 `CalendarEnabled` entry，切换时保存） |
| `MobilePopulation.cs` `PopulationCounts.cs` | 当前岛人口（只读缓存） |
| `MobilePlayerConfig.cs` | 设置唯一来源：单个 `[OhMyMods.Android]` MelonPreferences 分节共 9 个 entry（速度、无限体力、Hold、日历、敌人数量、威胁成长、无限货币、农舍猫 stocking、快速建造；切换即保存）。`InfiniteMoney` 的原生写不在本文件：经 `Initialize(Action<bool>)` 注入，由 `Probe.cs` 传 lambda 接线 |
| `MobilePlayerMenu.cs` | Player 页 UI（speed / stamina / hold / back；中性文案 "Device settings"） |
| `MobileWorldMenu.cs` | World 页 UI（enemies / threat growth / infinite money / stock cats / fast build / back；六行 98/176/254/332/410/488，面板 562）。猫开关只影响以后关卡加载时的补齐，不删除已有猫；快速建造开关只影响后续每次原生 `InitializeBuild` 调用（前缀在 `_hasStarted` 早退之前写 rate，关闭不热还原已写入实例的 rate）；Back 只置 `Layout.WorldPage=false` |
| `PatchWorld_Mover.cs` `PatchRide_InfiniteStamina.cs` | 速度倍率与无限体力补丁（显式注册；旧 lease 补偿已删） |
| `OptionalQoLScope.cs` | 本机世界/层/场景闸门 |
| `GlobalAliases.cs` | 把共享桌面源的裸游戏类型名映射到 Android interop 的 `Il2Cpp.*` |
| `AndroidCoroutine.cs` | BepInEx `WrapToIl2Cpp` 的 Android 薄桥：loader `MonoEnumeratorWrapper` + 原 owner 的 `StartCoroutine`（无全局 owner、无 GC 守卫） |
| `HoldBridges.cs` | `ModPanel.IsShown`、`KingdomEnhancedPlugin.Instance.LogSource` 薄桥（`LogWarning/LogInfo/LogError(object)`） |
| `AssemblyInfo.cs` | `[assembly: MelonLoader.HarmonyDontPatchAll]` |
| `../il2cpp/PatchPlayer_HoldPurchase.cs` | 未修改链接的生产源（长按续买，默认关闭） |
| `../il2cpp/PatchWorld_EnemyManager.cs` | 未修改链接的生产源（`AddEnemies` 数量倍率、`GetEnemies` 三个成长天数倍率前缀；默认 1x 不介入） |
| `../il2cpp/PatchWorld_FarmCats.cs` `../il2cpp/FarmCatMovement.cs` `../il2cpp/GreekScaleScope.cs` | 链接的生产源（农舍猫 + 移动驱动 + 缩放作用域；相对桌面版仅 2 处 `#if` 平台边界，见下） |
| `../il2cpp/PatchWorld_Construction.cs` | 未修改链接的生产源（`ConstructionBuildingComponent.InitializeBuild` public 前缀：`Enabled && FastBuild` 时写 `_autoBuildRate=50f`；默认 OFF 不介入） |

## 功能范围与状态

- 浮球拖动/展开、日历、人口、速度、无限体力：沿用上一轮设备验证过的 0.0.8 语义；
  速度倍率的旧 ThreadStatic lease/reentrancy 补偿已按原生诊断删除（三入口原生地址互异、
  9/9 maxDepth=1），`ScalePlayerSpeed` 是唯一注册入口。
- 设置（唯一来源）：loader 标准 `UserData/MelonPreferences.cfg` 的 `[OhMyMods.Android]`
  分节，不 SetFilePath 自写路径、不每帧读回、不重试、不镜像状态。
  键与默认值：`SpeedMultiplier=1`、`InfiniteSteedStamina=false`、`HoldPurchaseEnabled=false`、
  `CalendarEnabled=false`、`EnemyCountMultiplier=1`、`EnemyTimelineSpeed=1`、
  `InfiniteMoney=false`、`FarmCatsEnabled=false`、`FastBuild=false`。加载边界各一次：速度 clamp 1–5；两个 float 倍率有限值 clamp
  1–5，NaN/Infinity（手工编辑 cfg 的非法输入）回 1 并各 Warning 一次——`Math.Clamp(NaN,…)`
  会返回 NaN，故显式判非有限；只改内存，不回写。切换时恰好一次
  `Category.SaveToFile(printmsg:false)`；失败由 loader 自身 `MelonLogger.Error` 输出真实
  异常（实际 IL 已核），UI 不承诺“已保存”，9 个 entry 都不另存镜像
  （`Enabled` 是无 UI、不持久化的会话总开关）。`InfiniteMoney` 的原生静态开关只在初始化
  与每次切换各写一次（entry 改 → apply → save），不订阅事件、不每帧写；`ModConfig`
  本体零 `Il2Cpp.*` 引用，写入由 `Probe.cs` 的 lambda 承担。`FastBuild` 只有值翻转 +
  日志 + 一次 save，不订阅事件、不做配置镜像/读回/retry。冷启动 `ANDROID_SETTINGS_READY`
  行按同序追加 `cats=<bool>`、`fastBuild=<bool>`。
- 长按续买（Hold purchase）：共享桌面源链接编译，默认 OFF（不写钱包/库存）。
- 敌人参数（C2）：`../il2cpp/PatchWorld_EnemyManager.cs` 未修改链接；两个前缀分别缩放
  `AddEnemies` 的数量 multiplier 与 `GetEnemies` 的三个成长天数 int（`Mathf.RoundToInt`
  半值取偶），默认 1x 直接放行；不生成额外波次、不改昼夜时钟/排期，也不是 #111/#94 的修复。
  UI 档位步进先在加载值上 `MathF.Floor` 到整数档再进一（1.5→2、4.5→5、5→1），合法载入的
  小数（如 4.5）不会被推成 5.5；载入值原样保留，不在运行期加 clamp/守卫。
- 无限货币：只写原生 `Wallet.InfiniteMoney` 静态开关（native get/set 为纯静态读写、setter
  无副作用、消费点在 `RemoveCurrencyAfterMoving`；见 `native-world-recon/`），不送钱、
  不改余额/币对象/扣款实现；不同货币类型与联机效果未验。
- 快速建造（`FastBuild`，默认 OFF，最小适配）：链接未修改的 `PatchWorld_Construction.cs`，
  在每次原生 `ConstructionBuildingComponent.InitializeBuild()` 调用前（先于其 `_hasStarted`
  早退）写 `_autoBuildRate=50f`，仅当会话 `Enabled && FastBuild`。写点语义是“每次
  InitializeBuild 调用”，不是只在首次真正初始化、也不是付款时才运行；不承诺 2 秒建成时长。
  关闭开关不热还原已写入实例的 rate（无 rate 镜像/缓存/重试/新 driver）；不改原生
  `AllAutoBuild`/progress/付款流程。其它 rate 写入者与对象池复用语义尚未核验。精确候选已安装隔离设备，
  狭范围原生初始化、正常付款建造及同岛文件重载结果见末节。
- 农舍猫（`FarmCatsEnabled`，默认 OFF）：共享源 `PatchWorld_FarmCats.cs` /
  `FarmCatMovement.cs` / `GreekScaleScope.cs` 以链接方式编译，平台边界仅 2 处 `#if`——
  `GreekScaleScope.Tick` 的 `ScaleRegistryHolder.RetryPendingCreation()` 只在非 Android 编译
  （Android 的 driver 创建由 `Probe.OnUpdate` 直接驱动 `Tick`/`MaintainRegisteredY`），
  `EnsureCatScale` 在 Android 直接调 `GreekScaleScope.Register`（非 Android 仍走
  `ScaleRegistryHolder.Register`）；PC 侧产物两方法 IL/局部变量/EH 与 baseline 逐指令等价
  （机器比较），Android 产物内不含任何 `ScaleRegistryHolder` 假面。开关只决定后续
  `World.OnLevelLoaded` 是否调度补齐（`Schedule`），不删除/不回收已有猫、不热生成；
  生成/回收/存档沿用原生 `Pool.SpawnOrInstantiate<Cat>` + `Persistent` 生命周期与
  联机 fail-closed 语义（详见共享源注释）。`AndroidCoroutine.WrapToIl2Cpp` 用 loader
  `MonoEnumeratorWrapper` 包 `IEnumerator` 并交回**原 owner**的 `StartCoroutine`。
- 已由 Operator 集成（`Probe.cs`）：
  1. `OnInitializeMelon` 最早处 `KingdomEnhancedPlugin.Initialize()` +
     `ModConfig.Initialize(lambda)`；lambda 是 `Il2Cpp.Wallet.InfiniteMoney` 的唯一写入点
     并打 `ANDROID_MONEY_FLAG`（先于所有 patch 注册与 UI 读取）；
  2. Hold 四个 handler 的显式注册（`private static`；`HarmonyDontPatchAll` 抑制自动扫描）；
  3. Mover 三入口以 `ScalePlayerSpeed` 为唯一 prefix，`LogHookCounts` 输出
     `ANDROID_HOOK_COUNTS`（冷启动计数验收信号）；
  4. 敌人两个目标：`AddEnemies` 6 参类型数组、`GetEnemies` 5 参类型数组（必须带数组消歧
     2 参重载），注册 `AddEnemies_Prefix` / `GetEnemies_Prefix`（`public static`）；
  5. `Probe.OnUpdate` 调 `PatchPlayer_HoldPurchase.Tick()`（非必需路径，退化安全）；
     Player 页 = `MobilePlayerMenu.Draw`，World 页 = `MobileWorldMenu.Draw`，
     Home 第 4 行为 `World` 入口（`Layout.WorldPage`），第 5 行为 `Close`；
  6. 农舍猫注册：`World.OnLevelLoaded` postfix（仅 `Enabled && FarmCatsEnabled` 时调
     `PatchWorld_FarmCats.Schedule`）与 `Cat.OnEnable/OnDisable/Update` 三个 handler
     的显式注册（handler 由链接源 `FarmCatMovement` 提供）；
  7. `Probe.OnUpdate` 调 `GreekScaleScope.Tick()`、`OnLateUpdate` 调
     `GreekScaleScope.MaintainRegisteredY()`（Android 下 Tick 不再有 driver 创建重试段）；
  8. `ProbeTicker.OnGUI` 在 layout/绘制处理后调用
     `MobileUiInputSurface.Sync(gameObject, Layout, UiReady)`，`CancelGesture` 调 `Hide()`，
     `OnDestroy` 调 `Dispose()`。命中面是普通 CLR owner 类（非 MonoBehaviour driver、无新
     Harmony、无全场 Find/常驻 retry）：`UiReady=false`/非 RunningGame 只隐藏不创建，
     首次就绪时惰性创建 root（Canvas + GraphicRaycaster + 两个透明 `Image`），同步球区
     `(X-TouchSize/2, Y-TouchSize/2, TouchSize, TouchSize)` 与展开面板
     `(PanelX, PanelY, PanelWidth, PanelHeight)`，屏幕左上坐标转 UGUI 顶左锚点
     `anchoredPosition (x, -y)`。该层在 API35 ARM64 的原生 Rewired UGUI 菜单上已验证命中与排序（见末节 0.0.12）。
  9. 快速建造注册：`Il2Cpp.ConstructionBuildingComponent.InitializeBuild()`（无参）唯一注册到共享
     `PatchWorld_Construction.Prefix`，冷启动 `ANDROID_HOOK_COUNTS` 应含该 target 的
     `parameters=0 prefixes=1 postfixes=0 finalizers=0` 与 `ANDROID_FAST_BUILD_HOOK_INSTALLED
     sharedSource=true perInitializeBuildCall=true`；reported 入口由 15 增至 16（主 Mod 单独冷启动已逐行复核各一份）。
- 独立 Android API35 ARM64 模拟器已验：四设置首次默认值、界面切换、配置读回和进程
  重启恢复；隔离配置的保存故障可见日志；去 lease 后三速度入口九组原生 API 对照及
  2x/5x 正常触屏移动；体力 ON/OFF 的原生消耗与速率归还。诊断插件仅用于采证。
- 实际触屏长按投石器商店一次完成三笔原生 2 币购买，余额 16→10，三次 `PerformPay`；
  满架停止，临时间隔 0.0375→0.15 归还。初次触屏付款进入 Transaction，后续续买进入
  Holding；没有为测试改钱包或库存。原共享续买距离上限 0.5 保持，需靠近
  `PlayerPayPoint()`，其位置可能与商店图像中心不同。
- 本轮（World settings / 敌人参数 / 无限货币）的设备前源码阶段记录：真实
  interop/loader 引用编译、适配层 host 测试与产物元数据检查通过（命令见下节）；
  当时 World 页、敌人注册与货币消费尚未执行设备验证。后续窄范围结果见末节
  “0.0.10 隔离设备验证”；真实首夜入参和完整波型仍未验。

## 与桌面（Windows / Unity 2022 / 2.4.0 线）的 API 差异

- Android interop 把全局命名空间游戏类型放到 `Il2Cpp.` 前缀（桌面 BepInEx interop 无前缀）；
  由 `GlobalAliases.cs` 的 global alias 映射（`BiomeHolder`/`Mover`/`Character`/`Kingdom`/`Game`
  因与 adapted 文件的本地 alias 同名（CS1537）改由 global namespace import `global using Il2Cpp;`
  解析，显式 alias 仍优先），冲突的本地 alias 已按 CS1537 规则移除。
- `Player.PayState` 含 4–6 三个中间值（`StateKeyDown/StateKeyHoldDetected/StateKeyHold`）：
  **不是 Android 新增**——桌面 2.4.0 interop 同样包含（`recon/evidence/desktop-paystate.json`，
  撤回旧“Android 新增”表述）；本次真实触屏采购观察到 0/1/2/3，不能据此声称所有
  输入和交易路径都不会进入 4–6。
- `Payable.forceBlockPayment` 以 property 暴露（桌面反汇编中为 +0x100 偏移）。
- Android interop 成员不带 `[Token]`/`[Address]` 特性；运行时身份需 `il2cpp_method_get_token`。
- loader 的 `MelonLoader.dll` 自带顶层 `Harmony` 命名空间：本引用集下裸 `Harmony` 报 CS0118，
  须全限定 `HarmonyLib.Harmony`。

## 验证边界（截至 2026-10-03）

- 已做：`net10.0` + 真实 interop/loader/SupportModules 引用编译 0 warning / 0 error（含新增链接的
  `PatchWorld_EnemyManager.cs` 与农舍猫 3 源）；共享 hold-purchase 行为套件（链接未修改生产源，
  71 场景）全过；直接相关 PC 回归（`tests/farm-cats` 22、`tests/greek-scale-scope` 68、
  `tests/greek-scale-adapters` 10，链接同一批未修改源）全过；适配层测试（Home 484 底行命中、
  World 484 底行命中、Player 406、Population 376；ModConfig 8 entry/默认值/加载边界 clamp 与
  NaN·Infinity 回退/切换仅保存一次/ToggleFarmCats 每次恰好一次保存/Initialize seam 恰好三次
  apply；敌人前缀 1x/2x/5x、关闭态放行、ties-to-even 取整与 ref 输入输出；产物元数据含 Hold 4 +
  World 2 + 农舍猫 4 handler、协程桥与 `MonoEnumeratorWrapper`/`IEnumerator` ctor 引用、
  linked 三源、无 `ScaleRegistryHolder` 假面）全过；
  loader 0.7.3 `CreateEntry` 吸入已存值与 `SaveToFile` 失败语义有实际 IL 证据
  （本任务 `settings-implementation/evidence/`）。
- 已做（Issue #122 源码阶段，取代上述 0.0.12 的 484/8-entry 数字）：真实 interop/loader/
  SupportModules 引用编译 0 warning / 0 error（新增链接 `PatchWorld_Construction.cs`，
  `Compile Include` 仅一份）；适配层 host 默认 231 passed / 0 failed、`--seed-fast-build`、
  `--oldcfg`、`--seed-speed=9` 各 177、4.5 小数档 185，全部 0 failed；产物元数据确认 linked
  `PatchWorld_Construction`（public static `Prefix`，单参数 `Il2Cpp.ConstructionBuildingComponent`）
  与真实 `set__autoBuildRate` member 引用。详见 `construction-implementation/`。
- 已验（Issue #122）：私有精确候选安装、原生 InitializeBuild 入口与正常付款建造、
  ON/OFF 不热应用/还原、配置冷读回及单 main 16 个 reported 各一份（见末节）。
  全部施工类型、对象池复用、其它 rate 写入者、手机/联机/跨岛仍未验；不公开分发 APK。
- 已做（农舍猫块，源码/构建/PC 门）：两个 `#if` 边界与基线快照的机器 diff 只含边界行；
  PC candidate wrapper（SDK8、net6、无 ANDROID 定义）与 baseline 的 `GreekScaleScope.Tick` /
  `PatchWorld_FarmCats.EnsureCatScale` 方法体（指令+操作数+局部变量+EH）逐项等价，PC 产物两方法
  仍调用 `ScaleRegistryHolder`；Android 产物 `EnsureCatScale` 调 `GreekScaleScope.Register`、
  `Schedule` 调 `AndroidCoroutine::WrapToIl2Cpp` → 原 owner `MonoBehaviour::StartCoroutine`；
  4 个 Harmony 目标（`Cat.OnEnable/OnDisable/Update`、`World.OnLevelLoaded`）在安装前用
  LibCpp2IL 对 Android arm64 元数据机械核对：原生指针互异且无重复地址（无折叠）。
- 已做（设备，协程桥先行探针）：loader 0.7.3 `MonoEnumeratorWrapper` 的 ALC 解析、
  普通/嵌套/原生等待步进、owner 失活后停止（独立诊断插件 `runtime.log`，normalSteps=4、
  owner 失活 10→10 ticks）；主动销毁 owner 的独立探针也通过（请求时 10、延迟销毁后 11、
  再观察 20 帧仍为 11，`destroy-runtime.log`）。不推断强制停止会调用托管 Dispose/finally；
  这些探针只证明协程通道，不等于农舍猫块已上机。
- 已做：独立模拟器内的实际 APK 打包/安装、上述触屏与原生 API 对照、设置恢复与保存
  故障。原游戏文件逐文件保持，仅改加载入口并重新签名；APK 只包含主 Mod，测试助手
  不随包交付。具体候选与私有日志在维护者本机记录，不分发游戏。
- 未验：手机/平板兼容、特殊坐骑技能、所有商店类型与弹药续买、跨岛运输、完整身份
  存档恢复、联机；农舍猫的生成/回收/池化 AOT（`SpawnOrInstantiate<Cat>` 运行期可用性）、
  其中原生泛型的无池 Instantiate 分支和临时 CatSaveData 回放已在下文验证；
  池缓存复用、真实 `Persistent` 文件读档、`Pool.InitPools` 重建与真实希腊农舍仍未验；后续功能需按
  依赖移植。原版资源或图形故障不能当移植通过，host stub 也不能代替设备证据。当前代码
  交付范围不代表完整 Mod 或所有玩法验收完成。

## 测试

适配层（先构建测试工程与产物，再直接执行已构建的测试程序；每行末尾都接产物路径）：

    <dotnet10>/dotnet run -c Release --project android/tests/AdapterTests.csproj \
        -- android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll
    <dotnet10>/dotnet android/tests/bin/Release/net10.0/AdapterTests.dll --seed-speed=9 \
        android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll

加载边界矩阵（各场景一次运行，期望 0 failed）：`--seed-speed=0|3`、`--oldcfg`、
`--seed-fast-build`、`--seed-enemy-count=NaN|Infinity|0.5|9`、`--seed-enemy-timeline=NaN|Infinity|0.5|9`、
`--seed-enemy-count=4.5 --seed-enemy-timeline=4.5`
（`NaN`/`Infinity` 走非有限回退 1 + Warning，`0.5`/`9` 走有限 clamp 1–5，`--oldcfg`
只种入旧四键、验证新键取默认且不回写；`--seed-fast-build` 验证已有 FastBuild 键载入 true；
4.5 小数档验证载入原样、UI 步进 4.5→5→1 各一次 save）。

共享行为套件（仓库根 `tests/hold-purchase`，链接同一份未修改生产源，只执行不修改）：

    <dotnet8>/dotnet run -c Release --project tests/hold-purchase/HoldPurchaseTests.csproj

直接相关 PC 回归（同样只链接未修改/边界源，不拉起无关套件）：`tests/farm-cats`、
`tests/greek-scale-scope`、`tests/greek-scale-adapters`。

当前基线（0.0.13，含 Issue #122 快速建造适配）：默认 231 passed / 0 failed、seeded 档 177
（`--seed-fast-build`/`--oldcfg`）、4.5 小数档 185，全部 0 failed。相比 0.0.12（217/168/176）
新增检查：FastBuild 声明与默认值、seeded 键载入/缺键回退、单次切换单次 save 与日志、
World 562 几何与 Back 复原快照、产物 linked `PatchWorld_Construction`（public static
`Prefix(ConstructionBuildingComponent)` 与真实 `set__autoBuildRate` 引用）。共享源 SHA-256
冻结为 27 条路径、22 条 actual 校验：`FloatLayout.cs`/`MobilePlayerConfig.cs`/
`MobileWorldMenu.cs`/`Probe.cs`/`OhMyMods.AndroidProbe.csproj` 为本次有意改动不参与断言，
新增冻结 `MobileUiInputSurface.cs` 与 `il2cpp/PatchWorld_Construction.cs`，
`tests/AdapterTests.csproj` 恢复实际校验。host 测试只检查适配层与产物元数据，不伪造运行期
猫池/存档/透明命中结果，也不虚构 UnityRuntime。

## 0.0.10 隔离设备验证

API35 ARM64 模拟器确认三个新设置由实际界面切换并在进程重启后恢复。
原生蓝图 API 对照：常量曲线 2 在数量 1/2/5 倍时返回 2/4/10 个蓝图；成长入参
2/4/6 变为 2/4/6、4/8/12、10/20/30。诊断只构建蓝图，没有调用生成或排期；
这不是所有实际夜袭或平台的验收。界面触点 Began 到 Ended 被过滤，原生移动/付款
输入为零；正常场内滑动仍可移动。

原生 Coins 消费：无限货币开启时 6 币城堡购买取得付款回执，六次
RemoveCurrencyAfterMoving 各自保持 8→8；关闭时另一笔 9 币原生购买取得回执，
同钱包 16→7、九次各扣 1。使用原生收入和实际触屏付款，没有测试写余额或库存。
不同 Currency 类型、手机/平板、完整波次、跨岛/身份保存及联机仍未验。

测试期间一次钱包下降和移动无结果的末尾快照为原生 Loss；不能据此认定 UI 透传。
后续独立触点与正常移动对照未复现该缺陷，未增加付款禁用或输入补偿。历史观察
的具体掉币来源未采证；本段只报告以上已测消费入口。临时诊断插件采证后移出 Mods。

本阶段 0.0.11 设备验证：主 Mod 单独冷启动正常，15 个已报告入口各一份；农舍猫默认
OFF、实际触屏 ON 后冷启动读取 true，再经触屏恢复 OFF。真实 Holder.catPrefab 指向
Cat_norselands，原生 Persistent path 为 Prefabs/Characters/norselands/Cat_norselands。
临时独立诊断在 Menu、单机权威且未保存时同步执行两次 SpawnOrInstantiate<Cat>：
希腊当前无该猫的原生池，实际走 Instantiate 分支；每次 cats 0→1→0、registeredPersistents
881→882→881，均经原生 DespawnOrDestroy 清理。B 先回读 false/red 的相异前态，
原生 ApplyData(CatSaveData) 恢复 true/white/null farmhouse；最终同 kingdom 登记数回基线。
这证明泛型接口、原生登记/注销与临时内存 payload 回放，不能代替文件读档、池缓存、
真正农舍 stocking、运动/FSM 或 y=1.25 验收（当前岛 farmhouses=0）。诊断未随 APK 打包。

## 0.0.12 原生菜单触摸隔离验证（Issue #119）

0.0.11 的 actor 手势隔离没有覆盖原生 UGUI 菜单；一次面板触摸会同时点击底层菜单。
0.0.12 沿用原生 EventSystem/GraphicRaycaster，在浮球和展开面板的两个同源矩形上放透明
Image 命中面。没有屏幕全覆盖层、原生菜单状态写入或新增 Harmony；既有触摸归属机制
继续负责玩家移动/付款。只同步布局改变的矩形值，失焦/非 RunningGame 隐藏，owner 销毁时
归还命中层，无新增驱动、周期场景扫描、镜像或重试。

隔离 API35 ARM64 模拟器确认：原生 Menu Canvas order=2，Mod order=32760、同 sortingLayerID=0，
alpha=0 的 Image 确实命中且排在底层按钮前；Home 的 World/Player 按钮不会再打开底层
Campaign/Resume。面板外 Campaign 与收起后的 Resume 仍可点击。拖动到屏幕右侧后，
面板矩形跟随布局，悬浮球四个矩形角均由原生 RaycastAll 命中 Ball。失焦后的首次采样中
命中层已 inactive，返回后同一 owner 恢复；冷启动 Loading 阶段没有命中层，Playing 阶段
仅一个 owner 根层。

同一 Playing 会话中，普通右滑使原生玩家 x=-12→-8.94；打开浮球、Player、Back、Close
四次实际触摸均保持 x=-8.94、coins=2，随后普通左滑到 x=-12.10。该结果证明本轮 UI
与普通移动路径，没有代替真实购买或完整连续手势轨迹。只读诊断采证后已移出 Mods；
主 Mod 单独冷启动，0.0.12 及 15 个已报告入口各一份，旧设置保持。诊断 DLL 未进 APK/PR。

手机/平板、多指、分辨率变化、真实跨岛与文件读档切换仍待验，未称全部平台或场景通过。
支持模块 Il2Cpp 的依赖发现警告随后由 loader 成功加载解决，未改 Optional 来隐藏兼容边界。

## 0.0.13 快速建造隔离设备验证（Issue #122）

API35 ARM64 主 Mod 单独启动 0.0.13，16 个已报告入口各一份，FastBuild 默认 OFF。
OFF 的三次原生关卡初始化中 rate=1→1；原生 AllAutoBuild=false。实际浮球 World 新行
可触屏切换 ON/OFF，已有这三个实例保持 rate=1，未热应用或批量重写。

Playing 中正常滑动取得原生收入，coins=2→8。在 ON 下通过正常付款建第一层墙，
coins=8→7；原生 InitializeBuild 前 rate=0、started=false、current=0/10，之后
rate=50、started=true、current=1/10，下一快照达到10/10并有实际墙对象。
全程 AllAutoBuild=false；没有写钱包、库存、建造进度或调用测试用初始化/完成入口。

触屏关闭后同一墙仍 rate=50。随后正常付款升级第二层墙，coins=7→4，新的原生调用
rate=0→0、current=0/30，待建脚手架存在；再次开启不热改变这次已初始化的rate=0。
通过原生 Save 按钮保存并重启，配置读取 FastBuild=true，实际同岛的未完成墙重新加载，
原生初始化 rate=0→50、current=0→1/30，后续快照达到30/30，coins仍4。
这是一次同岛原生文件重载与建造恢复结果，没有代替跨岛、全部施工类型或池复用验收。

最终经界面恢复 OFF、移出只读诊断插件并冷启动，设置读回 false；仅主 Mod、16 个已报告
入口各一份，无 ERROR。临时 observer 只读采样，未进 APK/PR。callMs 是初始化方法调用
耗时，不是建造时长；快照只能证明采样时刻，未据此宣称固定两秒或性能/全部平台通过。
