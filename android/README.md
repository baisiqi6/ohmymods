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
| `FloatInput.cs` `TouchClaims.cs` `FloatLayout.cs` | 浮球触摸归属与面板几何（Home 484 / World 640 / Player 484 / Population 376，绘制与触摸同源） |
| `MobileCalendar.cs` `CalendarSnapshot.cs` | 日历显示（开关直读 `CalendarEnabled` entry，切换时保存） |
| `MobilePopulation.cs` `PopulationCounts.cs` | 当前岛人口（只读缓存） |
| `MobilePlayerConfig.cs` | 设置唯一来源：单个 `[OhMyMods.Android]` MelonPreferences 分节共 11 个 entry（速度、无限体力、Hold、坐骑技能冷却、日历、敌人数量、威胁成长、无限货币、农舍猫 stocking、快速建造、额外船容量；切换即保存）。`InfiniteMoney` 的原生写不在本文件：经 `Initialize(Action<bool>)` 注入，由 `Probe.cs` 传 lambda 接线 |
| `MobilePlayerMenu.cs` | Player 页 UI（speed / stamina / hold / steed cooldown / back；冷却文案相对当次 currentCD 的下一次技能调用，不承诺回溯或 prefab 倍率；中性文案 "Device settings"） |
| `MobileWorldMenu.cs` | World 页 UI（enemies / threat growth / infinite money / stock cats / fast build / extra boat crew / back；七行 98/176/254/332/410/488/566，面板 640）。猫开关只影响以后关卡加载时的补齐，不删除已有猫；快速建造开关只影响后续每次原生 `InitializeBuild` 调用（前缀在 `_hasStarted` 早退之前写 rate，关闭不热还原已写入实例的 rate）；船容量开关只作用于以后新初始化的船（`Boat.OnEnable` 借用窗口），关闭不删除/不热改已登记的 slots；Back 只置 `Layout.WorldPage=false` |
| `PatchWorld_Mover.cs` `PatchRide_InfiniteStamina.cs` `PatchRide_SteedCooldown.cs` | 速度倍率、无限体力与坐骑技能冷却调用级倍率补丁（显式注册；冷却四个消费者共享一个 Finalizer，无 PC instanceID 缓存/扫场） |
| `PatchWorld_BoatCapacity.cs` | 主船乘员容量（`BoatCapacityEnabled`，默认 OFF）薄适配：`Boat.OnEnable` 一个 prefix + 一个 Finalizer，在原生 `Embarkable::RegisterUnitSlots` 消费窗口内借用四个 max 字段（目标值来自共享 `BoatCapacityProfile`），调用结束后按 exact int 等值逐字段归还；不写 slots/船位置/航海/存档/native gate，不给登记早退或权限门加补偿 |
| `OptionalQoLScope.cs` | 本机世界/层/场景闸门 |
| `GlobalAliases.cs` | 把共享桌面源的裸游戏类型名映射到 Android interop 的 `Il2Cpp.*` |
| `AndroidCoroutine.cs` | BepInEx `WrapToIl2Cpp` 的 Android 薄桥：loader `MonoEnumeratorWrapper` + 原 owner 的 `StartCoroutine`（无全局 owner、无 GC 守卫） |
| `HoldBridges.cs` | `ModPanel.IsShown`、`KingdomEnhancedPlugin.Instance.LogSource` 薄桥（`LogWarning/LogInfo/LogError(object)`） |
| `AssemblyInfo.cs` | `[assembly: MelonLoader.HarmonyDontPatchAll]` |
| `../il2cpp/PatchPlayer_HoldPurchase.cs` | 未修改链接的生产源（长按续买，默认关闭） |
| `../il2cpp/PatchWorld_EnemyManager.cs` | 未修改链接的生产源（`AddEnemies` 数量倍率、`GetEnemies` 三个成长天数倍率前缀；默认 1x 不介入） |
| `../il2cpp/PatchWorld_FarmCats.cs` `../il2cpp/FarmCatMovement.cs` `../il2cpp/GreekScaleScope.cs` | 链接的生产源（农舍猫 + 移动驱动 + 缩放作用域；相对桌面版仅 2 处 `#if` 平台边界，见下） |
| `../il2cpp/PatchWorld_Construction.cs` | 未修改链接的生产源（`ConstructionBuildingComponent.InitializeBuild` public 前缀：`Enabled && FastBuild` 时写 `_autoBuildRate=50f`；默认 OFF 不介入） |
| `../il2cpp/BoatCapacityProfile.cs` | 链接的共享策略常量（Workers=8 / Knights=6 / Pikemen=8 / Farmers=3；纯常量、无方法/状态），PC 与 Android 编译同一份 |

## 功能范围与状态

- 浮球拖动/展开、日历、人口、速度、无限体力：沿用上一轮设备验证过的 0.0.8 语义；
  速度倍率的旧 ThreadStatic lease/reentrancy 补偿已按原生诊断删除（三入口原生地址互异、
  9/9 maxDepth=1），`ScalePlayerSpeed` 是唯一注册入口。
- 设置（唯一来源）：loader 标准 `UserData/MelonPreferences.cfg` 的 `[OhMyMods.Android]`
  分节，不 SetFilePath 自写路径、不每帧读回、不重试、不镜像状态。
  键与默认值：`SpeedMultiplier=1`、`InfiniteSteedStamina=false`、`HoldPurchaseEnabled=false`、
  `SteedCooldownMultiplier=1`、`CalendarEnabled=false`、`EnemyCountMultiplier=1`、`EnemyTimelineSpeed=1`、
  `InfiniteMoney=false`、`FarmCatsEnabled=false`、`FastBuild=false`、`BoatCapacityEnabled=false`。加载边界各一次：速度 clamp 1–5；两个敌人 float 倍率有限值 clamp
  1–5、坐骑冷却倍率有限值 clamp 0.2–1（UI 档位 1→0.8→0.6→0.4→0.2→1，载入中间值落到下一较低
  20% 档，如 0.5→0.4）；NaN/Infinity（手工编辑 cfg 的非法输入）回 1 并各 Warning 一次——`Math.Clamp(NaN,…)`
  会返回 NaN，故显式判非有限；只改内存，不回写。切换时恰好一次
  `Category.SaveToFile(printmsg:false)`；失败由 loader 自身 `MelonLogger.Error` 输出真实
  异常（实际 IL 已核），UI 不承诺“已保存”，11 个 entry 都不另存镜像
  （`Enabled` 是无 UI、不持久化的会话总开关）。`InfiniteMoney` 的原生静态开关只在初始化
  与每次切换各写一次（entry 改 → apply → save），不订阅事件、不每帧写；`ModConfig`
  本体零 `Il2Cpp.*` 引用，写入由 `Probe.cs` 的 lambda 承担。`FastBuild` 与
  `BoatCapacityEnabled` 只有值翻转 + 日志 + 一次 save，不订阅事件、不做配置镜像/读回/retry，
  也不因开关切换扫描或热改已登记状态。冷启动 `ANDROID_SETTINGS_READY`
  行按同序追加 `cats=<bool>`、`fastBuild=<bool>`、`cooldown=<float>`、`boat=<bool>`。
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
- 坐骑技能冷却（`SteedCooldownMultiplier`，默认 1，0.2–1，Android 专用薄适配）：`PatchRide_SteedCooldown.cs`
  在四个真实消费点各挂一个 prefix + 共享 Finalizer——`SteedAbility.Activate`、
  `BuffUnitsSteedAbility.Activate`、`GlideMovementSteedAbility.Activate`、
  `SpeedBoostSteedAbility.Deactivate`。每次非默认调用只借用当次：`applied = 进入时当前
  _cooldown × 倍率`，调用结束后按 exact float 等值条件（非原子 CAS、非 bit 等值、无 epsilon）
  把 current==applied 的字段还回 original；不同值保留并记一条 Warning，同值外部写无法区分
  （不声明写入者身份），已排程的 `_nextActivationTime` 不追溯，下一调用取新的当前值。
  `Enabled` 关闭或倍率 1 时不读/不写字段、不分配借用状态、不置 owner；`SummonGhostSteedAbility`
  只在 base 入口用真实 interop TryCast 排除（独立 profile），其余三类由注册目标类型确定。
  四个 prefix 的全部失败（代理读取、owner 判定、配置读取、字段读写）并入单一错误边界并只内联
  清理一次；`End` 的读取、exact float 等值判断、至多一次归还与日志同处单一 try/catch/finally，
  Finalizer 始终原样返回传入的原生 exception（不重试、不补偿）。未移植 PC 的
  instanceID→(Native,LastApplied) 缓存与 SettingChanged 扫场（其原生来源/生命周期
  未证且不需要）。Glide 体力不足的原生 `time+3` 路径不消费 `_cooldown`，保持 3 秒。
- 主船乘员容量（`BoatCapacityEnabled`，默认 OFF，Android 专用薄适配）：`PatchWorld_BoatCapacity.cs`
  只在 `Boat.OnEnable` 一个 prefix + 一个 Finalizer。原生 OnEnable 在本窗口内读 maxKnights →
  maxWorkers → `_archerPositions` 长度 →（`CanPikemenAndFarmersEmbarkMainBoat` 门后）maxPikemen →
  maxFarmers，逐项交给 `Embarkable::RegisterUnitSlots`；补丁先完整读取四个当前 max（不用 ctor
  默认 3 冒充），全部成功才发布借用状态，每字段先记 attempt 责任位再写共享 profile 目标值
  （Workers=8 / Knights=6 / Pikemen=8 / Farmers=3）。成功与原生异常路径都只由 Finalizer 内联一次
  End：按 exact int 等值逐字段归还进入值，不同值保留并记 Warning（不识别写入者、同值外部写不可
  区分），单字段失败只记 Error、不阻其它字段、不重试；prefix 自身写失败时内联清理一次并清空
  state，Finalizer 无债可还。OFF 或会话关闭零读零写零 state；不写 slots/船位置/航海/存档/native
  gate，不给登记早退或权限门加补偿，也不热改已登记 slots；Pike/Farmer 的原生权限门保持。
  弓箭手容量是 `_archerPositions` 数组长度，本补丁不映射也不改；真实登船/航海玩法未验。
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
  10. 坐骑冷却注册：四个目标——`SteedAbility.Activate`、`BuffUnitsSteedAbility.Activate`、
     `GlideMovementSteedAbility.Activate`（无参）与 `SpeedBoostSteedAbility.Deactivate`（无参），
     各自 `prefix=BasePrefix/BuffPrefix/GlidePrefix/SpeedPrefix` + 共用 `finalizer=Finalizer`
     （先于既有体力注册，使 base/Glide 的最终 shape 为 `2 prefixes/1 postfix/1 finalizer`；
     冷启动 `ANDROID_HOOK_COUNTS` 记录 Buff/Speed 一次，reported 入口 18 个 unique）。
     HarmonyX 2.10.2 的 `__state` 局部量按 patch 类 FullName 分配，因此同一 target 上的体力
     前缀与本类前缀各自持有 state，共享 Finalizer 只会拿到本类的 `Borrow`。
  11. 船容量注册：`Il2Cpp.Boat.OnEnable()`（无参）唯一注册到共享 `PatchWorld_BoatCapacity.Prefix` +
     `Finalizer`，冷启动 `ANDROID_HOOK_COUNTS` 应含该 target 的 `parameters=0 prefixes=1 postfixes=0
     finalizers=1` 与 `ANDROID_BOAT_CAPACITY_HOOK_INSTALLED sharedPolicy=true nativeSlotInitialization=true`；
     reported 入口由 18 增至 19 个 unique。
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
- 已做（Issue #127 源码阶段，取代上述 0.0.12/#122 的 Player 406/9-entry/231 数字）：真实
  interop/loader/SupportModules 引用编译 0 warning / 0 error（-t:Rebuild，新增
  `android/PatchRide_SteedCooldown.cs`，`Compile Include` 仅一份）；cooldown host 场景工程
  `android/tests/cooldown` 100 passed / 0 failed（base 10×0.5→消费 5/稳态 10、Buff 内 base 不二乘、
  早退保 5、原生异常 identity + 单次清理 + owner 复位、prefix getter/setter 写入前后失败状态消耗
  无 retry、cleanup 读/写/代理读取失败时 exception identity 与 attempt 计数、prevOwner 先于可抛
  pointer 写入捕获且不清零外层 owner、无 outer owner 时 owner 判定短路不读代理、跨对象 base、
  倍率 1 零读零写零 marker、窗口内改配置、外部新 baseline 下次取新值、窗口内不同值保留/同值不可区分、
  Ghost 用 TryCast 而非托管 is 排除、Glide 固定 3 秒、Speed 消耗/非激活零 next 写、Buff/Speed
  协程首 yield 同步快照；并含首候选负向锚点：旧 End 在 cleanup 代理读取失败时会让异常逃逸、
  替换原生 exception，修后同一测试通过）；适配层 host 默认 268 passed / 0 failed、
  seeded 199、4.5 档 207、`--seed-cooldown=0.5` 201，全部 0 failed（修后候选实跑）；产物元数据
  确认 4 prefix + 共享 Finalizer（internal static、无 Harmony 特性、`(SteedAbility, out Borrow)` /
  `(Exception, Borrow)` 签名）与 `get/set__cooldown`、五个坐骑 interop 类型引用。真实技能
  触发、手机/联机未验（当前自然坐骑 `playerSteedAbilities=0`，不制造能力/身份）。
- 已做（Issue #130 源码阶段，取代上述 0.0.12/#122/#127 的 World 562/10-entry/268 数字）：真实
  interop/loader/SupportModules 引用编译 0 warning / 0 error（-t:Rebuild，新增
  `android/PatchWorld_BoatCapacity.cs` 与链接 `il2cpp/BoatCapacityProfile.cs`，各一份）；boat host
  场景工程 `android/tests/boat` 55 passed / 0 failed（第 2/4 getter 失败零写零 state、原生窗口读
  8/6/8/3 后归还各进入值、第 2 写者写前/后失败仅 attempt 字段有责、原生异常 identity、cleanup 单字段
  读写失败互不阻塞不重试、窗口内外部不同值保留/同值不可区分、途中配置切换仍按捕获清理、默认 OFF
  零触达；host doubles，不冒充 Unity/Harmony/IL2CPP）；适配层 host 默认 299 passed / 0 failed、
  seeded 档 225、4.5 档 233、`--seed-cooldown=0.5` 227、`--seed-boat` 228、`--oldcfg` 225，全部 0 failed；
  产物元数据确认 1 prefix + 1 Finalizer（internal static、无 Harmony 特性、`(Boat, out Borrow)` /
  `(Exception, Borrow)` 签名）、`Il2Cpp.Boat` 与 `get/set_maxWorkers`…`maxFarmers` 成员引用、共享
  profile 四个 int 常量（8/6/8/3）；PC 侧 baseline（72a2 固定快照）/候选（当前 il2cpp 快照）隔离构建
  （SDK8/net6、不部署）：全程序集逐方法比较（指令/局部变量/EH）零差异，唯一新增
  `BoatCapacityProfile` 四常量（8/6/8/3、无方法），8 个内嵌资源逐字节相同，`Boat_MainCapacity_Patch`
  五方法的 MetaDump 输出逐字节一致（const 内联后进入值与基线一致）。真实船容量/登船/航海未验。
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
`--seed-cooldown=NaN|Infinity|0.1|0.5|9`、`--seed-enemy-count=4.5 --seed-enemy-timeline=4.5`
（`NaN`/`Infinity` 走非有限回退 1 + Warning，`0.5`/`9` 走有限 clamp 1–5、冷却 `0.1`→0.2、`9`→1，
`--oldcfg` 只种入旧四键、验证新键取默认且不回写；`--seed-fast-build` 验证已有 FastBuild 键载入 true；
4.5 小数档验证载入原样、UI 步进 4.5→5→1 各一次 save；`--seed-cooldown=0.5` 验证载入原样且
步进 0.5→0.4 一次 save）。

坐骑冷却场景（host doubles，链接实际生产 `PatchRide_SteedCooldown.cs`；断言消费值与
getter/setter/cleanup 次数，不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet10>/dotnet run -c Release --project android/tests/cooldown/CooldownTests.csproj

船容量场景（host doubles，链接实际生产 `PatchWorld_BoatCapacity.cs` + 共享
`il2cpp/BoatCapacityProfile.cs`；断言四个 max 的消费值、进入值归还与 getter/setter/cleanup
次数，不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet10>/dotnet run -c Release --project android/tests/boat/BoatTests.csproj

共享行为套件（仓库根 `tests/hold-purchase`，链接同一份未修改生产源，只执行不修改）：

    <dotnet8>/dotnet run -c Release --project tests/hold-purchase/HoldPurchaseTests.csproj

直接相关 PC 回归（同样只链接未修改/边界源，不拉起无关套件）：`tests/farm-cats`、
`tests/greek-scale-scope`、`tests/greek-scale-adapters`。

当前基线（0.0.14，含 Issue #127 坐骑冷却适配 + core fix 候选）：适配层默认 268 passed / 0 failed、
seeded 档 199（`--seed-speed=9`/`--seed-fast-build`/`--oldcfg`/`--seed-cooldown=NaN`）、
4.5 小数档 207、`--seed-cooldown=0.5` 201，全部 0 failed；cooldown 场景工程 100 passed / 0 failed。相比 0.0.13（231/177/185）新增检查：
SteedCooldownMultiplier 声明/default 1/加载边界 clamp 0.2–1 与 `--seed-cooldown` 各档、
`CycleSteedCooldown` 1→0.8→…→1 逐档值与单次 save/日志、Player 484 几何与底行命中、
READY 行 `cooldown=<float>`、产物 4 prefix + 共享 Finalizer 的 internal static 形状、
`(SteedAbility, out Borrow)`/`(Exception, Borrow)` 签名、无 Harmony 特性、`get/set__cooldown`
与五个坐骑 interop 类型引用。共享源 SHA-256 冻结为 28 条路径、23 条 actual 校验：
`FloatLayout.cs`/`MobilePlayerConfig.cs`/`MobilePlayerMenu.cs`/`Probe.cs`/
`OhMyMods.AndroidProbe.csproj` 为本次有意改动不参与断言，`MobileWorldMenu.cs` 与
`PatchRide_SteedCooldown.cs` 为新冻结的实际校验项，`tests/AdapterTests.csproj`、
`MobileUiInputSurface.cs` 与 `il2cpp/PatchWorld_Construction.cs` 继续实际校验。
host 测试只检查适配层与产物元数据，不伪造运行期猫池/存档/透明命中结果，也不虚构
UnityRuntime。

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


## 0.0.14 坐骑冷却注册与设置验证（Issue #127）

独立源码与调用回归审查通过；实际 API35 ARM64 模拟器安装主 Mod `2f4aed15…`，私有 APK
`27c6df32…`，原游戏22313条目保持，包内无诊断插件。安装完成且未启动游戏时，完整偏好与
原生文件字节均与备份一致。冷启动18个唯一type+参数数目标：base/Glide两前缀、一后缀、
一Finalizer，Buff/Speed一前缀、一Finalizer；其余14项数量保持，无ERROR。

真实浮球 Player 中冷却档位按1→0.8→0.6→0.4→0.2→1切换并保存；再选0.8后冷重启，
loader读回0.8且界面显示0.8，原九项设置保持。保存文件的0.800000011920929是对应float32
的十进制表示，比较按float32位值；不把Python双精度相等失败当配置精度错误。
最终通过UI恢复1，纯主Mod再冷启动，18项注册及旧九项设置复核保持，无ERROR，随后关闭游戏。

当前自然玩家普通马没有能力（既有只读查询player能力数0、场景能力总0）；本轮没有创建、
解锁或激活技能。上述证明是加载注册与真实UI设置持久化，不能证明真实技能冷却、Finalizer
状态运行期注入或新版本协程首yield时序。手机/平板、有技能坐骑、对象池、跨岛和联机仍待验。
仅代码交付，待对应PR正常合并及任务入口收尾；无公开APK、tag/release或PC/手机部署。

## Issue #130 船员容量的设备边界

精确 0.0.15 候选 Main `aa099c9f…` / 私有 APK `5508739d…` 在同一 Android API35 ARM64
MoltenVK 模拟器安装成功。安装后启动前完整偏好及原生存档与备份逐字节相同。19 个唯一
方法及参数数量的钩子读回（Boat.OnEnable 为 1 prefix / 0 postfix / 1 finalizer），旧18形状保持，
日志无 ERROR；加载器既有场景索引/支持模块等 Warning 仍保留，没有把无 ERROR 写成无 Warning。
World 640 七行实际显示，Boat物理点击 (180,528)、Back (180,606) 与命中矩形相符。
真实 UI OFF→ON、冷启动 loaded=true、UI ON→OFF、最终冷启 loaded=false，原十项偏好保持。
菜单中 Back 回 Mod Home，原生 Campaign 页面未因该点击打开；收起球后原生 Options 正常打开。
最终仅主 Mod 在 Mods，游戏已停止。22313 原包成员逐字节保持（含 AndroidManifest），仅
加载入口及测试重签的签名清单变化；助手不打包、不公开分发。

本次没有新船初始化或登船样本，没有证明 native 借用、槽位物化、真实人数、职业权限门、航行、
池复用、全部外部消费者、手机/平板或联机。源码及设置可交付，玩法继续单列待验。
