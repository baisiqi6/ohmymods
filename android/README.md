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
| `FloatInput.cs` `TouchClaims.cs` `FloatLayout.cs` | 浮球触摸归属与面板几何：固定外框 min(320,可用宽) × min(480,可用高) × Scale（与页面/卡片高度无关），`Resize(width,height,left,top,right,bottom)` 接收 native safeArea 转 top-left 原点的安全区绝对边界，浮球与面板都夹在安全区内；内容视口/scrollMax/`HitPanelBody`（球优先）为纯几何，绘制与触摸同源 |
| `MobileCalendar.cs` `CalendarSnapshot.cs` | 日历显示（开关直读 `CalendarEnabled` entry，切换时保存） |
| `MobilePopulation.cs` `PopulationCounts.cs` | 当前岛人口（只读缓存；#144 起面板行仅中文展示八个原生角色与骑士，Tick/日志采样不变） |
| `MobileModPanel.cs` `PanelGesture.cs` | 公共面板容器与纯手势状态机（Issue #144）：唯一 hotControl 手势、固定导航（主页/玩家/世界/生成/人口）与关闭、内容视口裁剪与滚动；手势/滚动单点由 `PanelGesture` 持有（任意方向位移 sticky 取消点击、`End` 按实际抬手归账、滚轮与拖拽共用同一 ScrollY） |
| `MobilePlayerConfig.cs` | 设置唯一来源：单个 `[OhMyMods.Android]` MelonPreferences 分节共 17 个 entry（速度、无限体力、Hold、坐骑技能冷却、法杖基础冷却、日历、敌人数量、威胁成长、无限货币、农舍猫 stocking、快速建造、额外船容量、新岛地图长度、快速森林退缩、普通鹿数量、密灌木、远距夜袭出发补偿；除密灌木外切换即保存，夜袭补偿同值不保存）。密灌木的 UI 请求还经共享 `TrySetDenseThickets` 一次，保存只发生在共享源的三个真实写入点（每次真实变更一次），本文件不落盘、不镜像。`InfiniteMoney` 的原生写不在本文件：经 `Initialize(Action<bool>)` 注入，由 `Probe.cs` 传 lambda 接线 |
| `MobilePlayerMenu.cs` | Player 页 presentation（5 行：君主移动速度 / 坐骑无限体力 / 长按连续购买 / 坐骑技能冷却 / 法杖神器冷却），全部经 `MobileModPanel.Step/Toggle` 公共路径登记 tap rect 并绘制；中文名与 PC 面板同源，冷却帮助行说明“下次调用生效、只缩放基础/该次冷却、原生目标追加不缩放” |
| `MobileWorldMenu.cs` | World 页 presentation（每波怪物数量 / 怪物时间线推进 / 无限金币 / 植被与野生动物入口 / 快速建造 / 船只额外乘员 / 远距夜袭出发补偿）。植被入口只置 `VegetationPage=true`（World 保持打开）；快速建造只影响后续每次原生 `InitializeBuild`（关闭不热还原已写入实例的 rate）；船容量只作用于以后新初始化的船（关闭不删除/不热改已登记的 slots） |
| `MobileGenerationMenu.cs` | Island-generation 页 presentation（地图大小一行）：只改新岛生成读取的 `MapSizeMultiplier`（1→2→3→4→5→1，合法小数先落下一整数档，如 4.5→5）；不调用任何原生游戏 API |
| `MobileVegetationMenu.cs` | Vegetation & Wildlife 页 presentation（农舍猫补给 / 森林快速消退 / 普通鹿数量 / 密灌木 / 密灌木状态（仅开启或回收中显示）/ 返回世界）。猫只影响以后关卡载入时的补齐/瘦身（不删除已有猫）；森林只作用于之后进入原生 `FadeAndRemove` 的调用（关闭不回收进行中的淡出）；鹿只作用于之后自然进入原生 `PopulationController.Update` 的调用（Greek 普通鹿目标/补充 ×3；关闭不回收已有动物、不改存档）；密灌木行是唯一请求入口：只调一次共享 `TrySetDenseThickets`（真实启停与额外实例回收由主线程 Tick 推进，保存只在共享源三个真实写入点各一次；帮助说明间距减半、关闭只回收额外实例、回收中暂不能重开），默认 OFF 且无自有责任时状态行条件短路、展示不触 `CurrentWorld`；返回只清 `VegetationPage`（World 保持打开） |
| `PatchWorld_Mover.cs` `PatchRide_InfiniteStamina.cs` `PatchRide_SteedCooldown.cs` | 速度倍率、无限体力与坐骑技能冷却调用级倍率补丁（显式注册；冷却四个消费者共享一个 Finalizer，无 PC instanceID 缓存/扫场） |
| `PatchDivine_StaffCooldown.cs` | 法杖基础冷却（`StaffCooldownMultiplier`，默认 1，0.2–1）薄适配：`HermesStaff._StartAbilityRoutine_d__17.MoveNext`（仅 `__1__state==0`）与 `ItemOfPower.CanCancel`（仅实际 TryCast 到 HermesStaff）各一个 prefix + 共享 Finalizer，在当次原生读取窗口内借用 `_itemCooldown` 基础值；每转化目标附加时间、min 截断、扫描与已排程 `_nextActivationTime` 全部保持原生；不复制桌面的 profile 写入/`OriginalAbilityRanges` 原值字典/SettingChanged 扫场 |
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
| `../il2cpp/MapWidthPlanner.cs` | 链接的共享纯规划器（Plan/AssignSeams + `MapWidthCandidate/MapWidthPlan`，零 Unity/native 依赖），未修改、逐字节与 PC 同一份（SHA-256 冻结） |
| `../il2cpp/ModPanelStyles.cs` `../il2cpp/ModPanelControls.cs` `../il2cpp/ImGuiCompat.cs` | 与 PC 同一份的共享 UI 源（Issue #144）：调色板 + 事务式皮肤副本/样式工厂（PC 传 scale=1、Android 传 `Layout.Scale`；不写全局 GUI 状态，持有者设/恢复）、面板底板/宽卡（PC 原几何）/窄端行（换行标题/值徽章/帮助行实测高度）纯绘制、`GUI.DrawTexture` 绕行 |
| `../il2cpp/MapWidthTerrain.cs` `../il2cpp/PatchWorld_Level.cs` | 链接的共享地图长度适配源：相对桌面版仅文件头 `#if ANDROID` 的 `using X = Il2Cpp.X;` 类型别名（Level/LevelBlock/LevelLayout 等），其余逐字相同；Android 下别名编译进实际 interop 类型（见构建/测试节） |
| `../il2cpp/PatchWorld_FastForestRecede.cs` | 链接的共享快速森林退缩源（`ForestItem.FadeAndRemove` 的 ref delay 前缀：显式正 delay ÷3，否则 `removeDelay × Random(0.5,1.5) ÷ 3`，有限正值才提交；默认 OFF 先于一切 item/native 访问）。相对桌面同一份共用，仅文件头 `#if ANDROID` 的 `Forest`/`ForestItem` 类型别名；故障一条 warning 保留原生等待参数，不建扫描/缓存/重试 |
| `../il2cpp/PatchWorld_DeerPopulation.cs` | 链接的共享普通鹿数量源（`PopulationController.Update` 的 prefix/postfix/finalizer：Greek 普通鹿、非 biome critters、真实 prefab Deer 非 Steed/Hind、当前 gameLayer 场景成员的 controller 才在单次调用内借用三个 seasonal density ×3 与 `_actualUpdateInterval` ÷3，调用结束按 exact float 等值归还；native 时钟/季节/地区/ceil/生成/池/掉落全保持）。相对桌面同一份共用：`#if ANDROID` 别名头 + 平台 opt-in 门（`ModConfig.Enabled && DeerPopulationEnabled`，默认 OFF 且无自有状态时在 `controller.Pointer` 之前零 interop 返回）+ 同次 Eligible 采集的 scope 证据（scopeGoId/sceneHandle/childOf/prefabDeer/prefabSteed/prefabHind，一次性日志）；PC 预处理输出与 IL/metadata 零差异 |

| `../il2cpp/PatchWorld_OptionalVegetation.cs` | 链接的共享密灌木源（`World.CanSpawnThicket(Grass)` 窗口内临时把 `thicketSpacing` 减半、`World.AddThicket(Grass)` 后置登记额外实例、`Grass.RemoveThicket()` 前整色交还/后作废登记；关闭只回收登记在案的额外实例，先按逐层 RGBA 凭据淡出再调原生回收，回收中拒绝再次开启）。相对桌面同一份共用：`#if ANDROID` 别名头（`Grass`）；默认 OFF 且 `Held/Pending/Owned/Records` 全空时四个 hook 入口在 Unity null/Pointer/字段读取之前由同一纯托管谓词零 native 返回（有旧责任时 OFF 仍清账）；FX 四项（`FxFade` 字段、StartFade FX 分支、`TryStartFxFade`、FX elapsed 等待支路）被 `#if !ANDROID` 裁剪，Android 无条件走 CaptureSprites→ApplyFallbackFade→RestoreOwnedColors，产物零 `SpriteRendererFX`/`FadeOut` 引用；三个真实配置写入点各自在真实变更时调用一次 `ModConfig.Save`。PC 预处理输出与行为不变 |

## 功能范围与状态

- 浮球拖动/展开、日历、人口、速度、无限体力：沿用上一轮设备验证过的 0.0.8 语义；
  速度倍率的旧 ThreadStatic lease/reentrancy 补偿已按原生诊断删除（三入口原生地址互异、
  9/9 maxDepth=1），`ScalePlayerSpeed` 是唯一注册入口。
- 设置（唯一来源）：loader 标准 `UserData/MelonPreferences.cfg` 的 `[OhMyMods.Android]`
  分节，不 SetFilePath 自写路径、不每帧读回、不重试、不镜像状态。
  键与默认值：`SpeedMultiplier=1`、`InfiniteSteedStamina=false`、`HoldPurchaseEnabled=false`、
  `SteedCooldownMultiplier=1`、`StaffCooldownMultiplier=1`、`CalendarEnabled=false`、`EnemyCountMultiplier=1`、`EnemyTimelineSpeed=1`、
  `InfiniteMoney=false`、`FarmCatsEnabled=false`、`FastBuild=false`、`BoatCapacityEnabled=false`、
  `MapSizeMultiplier=1`、`FastForestRecedeEnabled=false`、`DeerPopulationEnabled=false`、`DenseThicketsEnabled=false`。加载边界各一次：速度 clamp 1–5；两个敌人与地图长度 float 倍率有限值 clamp
  1–5、坐骑与法杖基础冷却倍率有限值 clamp 0.2–1（两行共用同一档位 helper 1→0.8→0.6→0.4→0.2→1，
  载入中间值落到下一较低 20% 档，如 0.5→0.4；地图长度与敌人倍率同一 `MathF.Floor` 步进 1→2→3→4→5→1，4.5→5）；NaN/Infinity（手工编辑 cfg 的非法输入）回 1 并各 Warning 一次——`Math.Clamp(NaN,…)`
  会返回 NaN，故显式判非有限；只改内存，不回写。切换时恰好一次
  `Category.SaveToFile(printmsg:false)`；失败由 loader 自身 `MelonLogger.Error` 输出真实
  异常（实际 IL 已核），UI 不承诺“已保存”，16 个 entry 都不另存镜像（密灌木的保存只经共享源的三个真实写入点，每次真实变更一次）
  （`Enabled` 是无 UI、不持久化的会话总开关）。`InfiniteMoney` 的原生静态开关只在初始化
  与每次切换各写一次（entry 改 → apply → save），不订阅事件、不每帧写；`ModConfig`
  本体零 `Il2Cpp.*` 引用，写入由 `Probe.cs` 的 lambda 承担。`FastBuild` 与
  `BoatCapacityEnabled` 只有值翻转 + 日志 + 一次 save，不订阅事件、不做配置镜像/读回/retry，
  也不因开关切换扫描或热改已登记状态。冷启动 `ANDROID_SETTINGS_READY`
  行按同序追加 `cats=<bool>`、`fastBuild=<bool>`、`cooldown=<float>`、`boat=<bool>`、`map=<float>`、`staff=<float>`、`forestRecede=<bool>`、`deerPopulation=<bool>`、`denseThickets=<bool>`。
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
- 法杖基础冷却（`StaffCooldownMultiplier`，默认 1，0.2–1，Android 专用薄适配）：`PatchDivine_StaffCooldown.cs`
  只在两个已审真实消费点各挂一个 prefix + 共享 Finalizer——`HermesStaff._StartAbilityRoutine_d__17.MoveNext`
  （仅 `__1__state==0` 单趟调用）与 `ItemOfPower.CanCancel`（仅实际 TryCast 到 HermesStaff 的实例；
  其他 7 个 ItemOfPower 神器不介入）。每次非默认调用只借用当次：`applied = 进入时当前 _itemCooldown × 倍率`，
  调用结束后按 exact float 等值把 current==applied 的字段还回 original；不同值保留并记一条 Warning，
  同值外部写无法区分（不声明写入者身份）。只缩放基础值：每转化目标附加时间、min 截断、扫描与已排程
  `_nextActivationTime` 都不改动，也不假设 30 秒 prefab 值（Android 序列化值未知）；CanCancel 借同倍率
  基础值后，原生比较保持 m=1 的附加项窗口（`a > elapsed`），不用原值阈值额外挤压。`Enabled` 关闭或倍率 1
  在读取 state/owner/TryCast 与字段之前返回（零 native 接触；配置在两 hook 共享前置里只读一次）。
  两个 prefix 的全部失败并入单一错误边界并只内联清理一次；`End` 单 try/catch 内至多一次归还，
  Finalizer 始终原样返回传入的原生 exception（不重试、不补偿）；日志走既有 HoldBridges LogSource
  桥、同 key 只记一次。不移植桌面的 Awake/CanActivate/TriggerItemAbility profile 写入、
  `OriginalAbilityRanges` 原值字典与 SettingChanged 扫场。未验边界：未知第三方订阅者若在 MoveNext
  借用窗口内再查 CanCancel，该次判定会用 applied×倍率 的阈值（归还链仍闭合、字段无损），出现实机
  证据前不预建 owner/静态栈守卫；`ItemBasedRulerAbility.Activate` 的 selected Hermes 虚路由不调 base、
  不读 c4、不启动 Signal（Activate/c4 链对其不生效）。真实法杖激活、取消窗口、附加项与手机/联机未验。
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
- 新岛地图长度（`MapSizeMultiplier`，默认 1）：共享源 `MapWidthPlanner.cs`（未修改）+ `MapWidthTerrain.cs` /
  `PatchWorld_Level.cs`（仅文件头 `#if ANDROID` 别名）以链接方式编译。`Probe` 显式注册两个入口：
  `Level.GenerateInternal(LevelConfig,int)` 的 prefix/postfix/finalizer（打开/归还同步 scope，
  Finalizer 原样返回传入异常）与 `LevelLayout.GetBlocks()` 的 postfix（只在该 scope 内、首次调用时
  规划一次：按本次返回列表逐块原生 `GetWidth` 的总宽与倍率快照，从 `layout.blocks` 的精确名模板
  （Forest/Clearing 大小块）核验组件组合/持久化路径/池戳后，规划并追加完整地块到合法接缝；
  失败/歧义/无接缝时原列表一个字节不改，仅告警）。倍率只在新岛生成时读取：不改变已生成岛、
  不追溯/不重生成、不复活旧 `minLevelWidth` 补偿、不新增扫描/驱动/缓存；默认 1x 或会话关闭时
  零副作用。host 行为、真实 interop 编译与 PC 等价见测试节；同一模拟器的普通 Greek 空槽新岛自然生成已验证一次，详见末节；未覆盖全部世界/跨岛/真机。
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
- 普通鹿数量（`DeerPopulationEnabled`，默认 OFF）：共享源 `../il2cpp/PatchWorld_DeerPopulation.cs`
  以链接方式编译（`#if ANDROID` 别名头 + 平台 opt-in 门，PC 输出零差异，见末节 #140）。默认 OFF
  且无自有状态时在 `controller.Pointer` 之前返回（零 interop）；开启后仅对 Greek 普通鹿
  （`useBiomeCritters=false`、真实 prefab 组件 Deer 且无 Steed/Hind、controller 与当前 gameLayer
  同场景且是其成员、native `playingOrInMenuWithClient`、`HasWorldAuth`）在单次原生
  `PopulationController.Update` 调用内借用三个 seasonal density ×3 与 `_actualUpdateInterval` ÷3，
  postfix/finalizer 按 exact float 等值归还进入值；native 时钟、季节选择、区域下限、
  `ceil(width×density)` 目标、生成/池、掉落、`_elapsedTime`/`_targetDensity`/序列化
  `updateInterval`/缩放全部保持原生。恢复机制保留既有 Active/Pending/token/Written/identity
  边界（partial setter 失败仅归还自己已借 bits、重入不还外层借据、stale finalizer 不清新调用）；
  首次有效应用记一条含 prefab 与 scope 证据的日志，失败 warning 一生一次。关闭不删除/不回收
  已有动物、不改原生存档；配置改变只影响未来自然 Update。
- 密灌木（`DenseThicketsEnabled`，默认 OFF，Issue #146）：共享源 `../il2cpp/PatchWorld_OptionalVegetation.cs`
  以链接方式编译（`#if ANDROID` 别名头 + FX 四项裁剪 + 默认 OFF 无债入口谓词 + 三个真实写入点
  各自单次 Save；PC 预处理输出与行为不变，见末节 #146）。开启时只在 `World.CanSpawnThicket`
  单次调用窗口内把 `world.thicketSpacing` 临时减半（调用结束与 finalizer 各尝试交还一次，
  失败转 Pending 由下次进入或 Tick 有界重试；未交还的值绝不当新基线）；额外灌木仍由原生
  `Grass.SpawnThicket` 生成，本 mod 只在 `AddThicket` 后登记归属。关闭只回收登记在案的额外实例：
  逐层 RGBA 凭据淡出（Android 无 FX 支路；逐层写入前登记 attempted intent、成功才转正为
  lastApplied，写后抛/读不可得时责任保留，下一次淡出/回收按 attempted/旧 Applied/基色三候选定性）
  → 整色还原 → 原生 `Grass.RemoveThicket`，以
  `grass._thicket` 是否真实交还为清完判据；回收批次未完成时拒绝再次开启并把外部强设的 true
  纠正回关闭。默认 OFF 且无自有责任时四个 hook 入口零 native 访问；有旧责任时 OFF 仍清账。
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
  12. 法杖基础冷却注册：`HermesStaff._StartAbilityRoutine_d__17.MoveNext()`（嵌套类型，无参；按声明
     父类名报告）与 `ItemOfPower.CanCancel()`（无参）两个目标，各 `prefix=RoutinePrefix/CanCancelPrefix`
     + 共用 `finalizer=Finalizer`，并有 `ANDROID_STAFF_BASE_COOLDOWN_HOOKS_INSTALLED consumers=2
     currentFieldRelative=true additiveTimeNative=true`；冷启动 `ANDROID_HOOK_COUNTS` 由既有 21 个
     unique 增至 23（旧 21 行的字段与形状不变，仅嵌套 target 显示 `HermesStaff._StartAbilityRoutine_d__17.MoveNext`）。
     0.0.17 增加 `staff_base_cooldown` feature marker。
  13. 普通鹿数量注册：`Il2Cpp.PopulationController.Update()`（无参）唯一注册到共享
     `PopulationController_Update_DeerPopulation_Patch` 的 `Prefix`/`Postfix`/`Finalizer`，
     冷启动 `ANDROID_HOOK_COUNTS` 应含该 target 的 `parameters=0 prefixes=1 postfixes=1
     finalizers=1` 与 `ANDROID_DEER_POPULATION_HOOK_INSTALLED sharedSource=true nativeSpawn=true
     enabled=<bool>`；reported 入口由 24 增至 25（旧 24 行形状不变）。
     0.0.19 增加 `deer_population` feature marker。
  14. 密灌木注册（Issue #146）：`Il2Cpp.World.CanSpawnThicket(Grass)` prefix/postfix/finalizer、
     `Il2Cpp.World.AddThicket(Grass)` postfix、`Il2Cpp.Grass.RemoveThicket()` prefix/postfix 三个目标
     注册到共享 `PatchWorld_OptionalVegetation` 的对应 wrapper，`Probe.OnUpdate` 调共享 `Tick()` 一次。
     冷启动 `ANDROID_HOOK_COUNTS` 应含 `CanSpawnThicket` 的 `parameters=1 prefixes=1 postfixes=1
     finalizers=1`、`AddThicket` 的 `parameters=1 prefixes=0 postfixes=1 finalizers=0`、
     `RemoveThicket` 的 `parameters=0 prefixes=1 postfixes=1 finalizers=0`，以及
     `ANDROID_DENSE_THICKETS_HOOKS_INSTALLED sharedSource=true nativeSpawn=true nativeRemove=true
     enabled=<bool>`；reported 入口由 25 增至 28（旧 25 行形状不变）。0.0.21 增加 `dense_thickets`
     feature marker。
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
- 已做（Issue #134 源码阶段，取代上述 #130 的适配层 299 与 28/23 冻结数字）：真实
  interop/loader/SupportModules 引用编译 0 warning / 0 error（-t:Rebuild，新增链接
  `il2cpp/MapWidthPlanner.cs`（未修改）、`il2cpp/MapWidthTerrain.cs`、`il2cpp/PatchWorld_Level.cs`
  （后两个仅文件头 `#if ANDROID` 别名）与 `android/MobileGenerationMenu.cs`，各一份）；map-width
  host 套件同一份生产源两种编译模式（PC 全局类型 / `-p:DefineConstants=ANDROID` 的 `Il2Cpp.*`
  别名 stub）各 49 passed / 0 failed；适配层 host 默认 364 passed / 0 failed、seeded 280
  （`--seed-map=NaN|Infinity|0.5|9`、`--oldcfg`）、`--seed-map=4.5` 284，全 0 failed；产物元数据
  确认两个新 hook 类型（private static；`Prefix(out Frame)` / `Postfix(Level, Frame)` /
  `Finalizer(Exception, Frame)` / `Postfix(LevelLayout, ref List<LevelBlock>)`）、共享
  `MapWidthPlanner`/`MapWidthScope` 与 `Il2Cpp.Level`…`Il2Cpp.Tile` 类型/消费成员引用；PC 侧
  baseline（293a 快照）/候选（当前 il2cpp 快照）隔离构建（SDK8/net6、不部署）：全程序集 6704
  方法（指令/局部变量/EH/MaxStack/InitLocals/特性/签名）与资源逐字节零差异，唯一源码差异是两
  文件的 `#if ANDROID` 头（PC 未定义编译掉）。真实设备的新岛生成/地图长度效果未验（需安装关口后
  走正常原生生成流程）。
- 已做（Issue #136 源码阶段，取代上述 #134 的适配层 364 与 34/30/4 冻结数字）：真实
  interop/loader/SupportModules 引用编译 0 warning / 0 error（-t:Rebuild，新增
  `android/PatchDivine_StaffCooldown.cs`，`Compile Include` 仅一份）；staff host 场景工程
  `android/tests/staff` 70 passed / 0 failed（30×0.2 排程读 6 且 6+40=46、12×0.6 float32 7.2、
  CanCancel 借值保持 m=1 附加项窗口（elapsed 35/45 两例对照）、默认与关闭在 state/owner/TryCast
  读取前返回且抛错 getter 零触达、非 Hermes 仅一次身份检查零借值、state≠0 单次读取不触 owner、
  getter/写前/写后失败与 cleanup 读/写失败的单次清理与原生 exception identity、日志调用抛错不
  外抛、窗口内不同值保留/同值不可区分、NaN 不归还、菜单 staff 行 410 / Back 488 / 面板 562 与点击
  单次 save；host doubles，不冒充 Unity/Harmony/IL2CPP）；适配层 host 默认 408 passed / 0 failed、
  `--seed-staff-cooldown=NaN|Infinity|0.1|9` 各 309、`=0.5` 311、`--seed-cooldown=0.5` 311、
  4.5 档 317、`--oldcfg` 309、`--seed-map=4.5` 313，全部 0 failed；产物元数据确认 2 prefix +
  共享 Finalizer（internal static、无 Harmony 特性、`(_StartAbilityRoutine_d__17, out Borrow)` /
  `(Il2Cpp.ItemOfPower, out Borrow)` / `(Exception, Borrow)` 签名）、`Il2Cpp.HermesStaff`/
  `Il2Cpp.ItemOfPower`/嵌套 `_StartAbilityRoutine_d__17` 类型与 `get/set__itemCooldown`、
  `get___1__state`、`get___4__this` 成员引用；Probe 源级注册检查（0.0.17、两个 handler 名、
  nested 父名输出、21+2=23 unique）与含 Probe 的 35 路径冻结（31 actual、4 有意改动）；PC 与根
  Mono/共享 `il2cpp` 源零改动（git diff 仅 android 6 文件 + 新文件/测试/任务文档）。真实法杖激活、
  取消窗口、附加项、手机/联机未验（需安装关口后自然可用时；不为验收造能力/改字段）。
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
`--seed-cooldown=NaN|Infinity|0.1|0.5|9`、`--seed-staff-cooldown=NaN|Infinity|0.1|0.5|9`、
`--seed-map=NaN|Infinity|0.5|4.5|9`、`--seed-enemy-count=4.5 --seed-enemy-timeline=4.5`
（`NaN`/`Infinity` 走非有限回退 1 + Warning，`0.5`/`9` 走有限 clamp 1–5、两个冷却 `0.1`→0.2、`9`→1，
`--oldcfg` 只种入旧四键、验证新键取默认且不回写；`--seed-fast-build` 验证已有 FastBuild 键载入 true；
4.5 小数档验证载入原样、UI 步进 4.5→5→1 各一次 save；`--seed-map=4.5` 验证地图长度载入原样且
4.5→5→1 各一次 save；`--seed-cooldown=0.5`/`--seed-staff-cooldown=0.5` 验证载入原样且
步进 0.5→0.4 一次 save）。

坐骑冷却场景（host doubles，链接实际生产 `PatchRide_SteedCooldown.cs`；断言消费值与
getter/setter/cleanup 次数，不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet10>/dotnet run -c Release --project android/tests/cooldown/CooldownTests.csproj

船容量场景（host doubles，链接实际生产 `PatchWorld_BoatCapacity.cs` + 共享
`il2cpp/BoatCapacityProfile.cs`；断言四个 max 的消费值、进入值归还与 getter/setter/cleanup
次数，不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet10>/dotnet run -c Release --project android/tests/boat/BoatTests.csproj

法杖基础冷却场景（host doubles，链接实际生产 `PatchDivine_StaffCooldown.cs`、`MobilePlayerMenu.cs`、
`MobilePlayerConfig.cs` 与真实 HoldBridges 日志桥；断言排程消费值、CanCancel 窗口对比、默认/关闭在
state/owner/TryCast 前的零触达、失败边界的原生 exception identity 与单次清理、以及 Player 菜单
staff 行 410 / Back 488 / 面板 562 与点击单次 save；不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet10>/dotnet run -c Release --project android/tests/staff/StaffTests.csproj

地图长度套件（host stub，链接实际生产 `MapWidthPlanner.cs` / `MapWidthTerrain.cs` /
`PatchWorld_Level.cs`；同一套用例跑两种编译模式——PC 全局类型与 `-p:DefineConstants=ANDROID`
下的 `Il2Cpp.*` 别名模式，验证别名头可编译且行为一致，不复制算法、不冒充原生运行）：

    <dotnet8>/dotnet run -c Release --project tests/map-width/MapWidthTests.csproj
    <dotnet8>/dotnet build -c AndroidAlias -p:DefineConstants=ANDROID tests/map-width/MapWidthTests.csproj
    <dotnet8>/dotnet tests/map-width/bin/AndroidAlias/net8.0/MapWidthTests.dll

普通鹿套件（host doubles，链接实际生产 `PatchWorld_DeerPopulation.cs`；PC 模式跑 27 case；
typed ANDROID host 用 `Il2Cpp.*` 别名与 Android 平台 config（`Setting<bool>` 会话开关 +
默认 OFF 的 `MelonPreferences_Entry<bool>` DeerPopulationEnabled、Greek=5、GreeceBiomeIndex 符号）
跑同一 harness 加 5 个平台用例——默认 OFF 空字典零 interop（controller.Pointer 也被计数与
ThrowInterop 拦截，覆盖最早 native 访问）、Greek5 应用与 scope 日志、OFF 下 owned pending 只归还
自己 bits、owned active 重入保持、以及 ON+ThrowInterop 的 negative control（最早 interop 读失败
被 caught、一次 warning、零字段写）；不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet8>/dotnet run -c Release --project tests/deer-population/Regression.csproj
    <dotnet8>/dotnet run -c Release --project tests/deer-population/android-host/DeerPopulationAndroidHost.csproj

密灌木套件（host doubles，链接实际生产 `PatchWorld_OptionalVegetation.cs` 与真实
`android/OptionalQoLScope.cs`，`ANDROID` 定义下 FX 支路真实裁剪；PC 模式跑既有 31 个行为/守卫
用例，typed ANDROID host 跑 22 个平台用例——原 14 个：OFF 空状态全部 getter 抛错仍零 native、
setter 已写后抛的租约归还、双失败 Pending 由 OFF 入口结清、嵌套不二次减半、stale finalizer 不
覆写新 lease、原生 exception 经 finalizer 原样返回、三层 RGBA 凭据淡出与回收前整色还原、非
HashSet 后备 fail-closed、FX 存在也不启动、外部第三色不被覆写、同 ptr/id 新生命撤旧凭据、原生
删除 prefix 先还色、TrySet 保存次数矩阵 same0/changed1/reject0/correction1/observe1；颜色写入
责任 8 个：写后抛异常仍整色归还、成功淡出后 before-write 错误仍还旧色、写后抛叠加暂时读失败
保留责任并恢复、未决期间外部色让位不被覆盖、未决期间同 ptr/id 新生命撤权、多层故障各自独立
收敛、写后抛接 before-write 串行仍保留第一次落地色（命中即转正）、pending 下回收/原生放回基色
仍可续淡（base 候选仅限 pending）；不冒充 Unity/Harmony/IL2CPP 运行）：

    <dotnet8>/dotnet run -c Release --project tests/optional-vegetation/Regression.csproj
    <dotnet8>/dotnet run -c Release --project tests/optional-vegetation/android-dense-host/DenseThicketsAndroidHost.csproj

共享行为套件（仓库根 `tests/hold-purchase`，链接同一份未修改生产源，只执行不修改）：

    <dotnet8>/dotnet run -c Release --project tests/hold-purchase/HoldPurchaseTests.csproj

直接相关 PC 回归（同样只链接未修改/边界源，不拉起无关套件）：`tests/farm-cats`、
`tests/greek-scale-scope`、`tests/greek-scale-adapters`。

历史 R3 快照（0.0.20，Issue #144 原生 GUI 兼容修复前；最终结果见末节）：适配层默认
591 passed / 0 failed（R2 修复后为 585；R3 新增 PanelGesture 帧末 `ClampToMax`、退化视口 subset、
人口日志英文 payload/展示中文名约束等 6 项）；`--oldcfg` 476、`--seed-forest` 479、`--seed-boat` 479、
`--seed-map=4.5` 480、`--seed-staff-cooldown=0.5`/`--seed-cooldown=0.5` 478、
`--seed-enemy-count=4.5 --seed-enemy-timeline=4.5` 484 为 R2 运行值（R3 未重跑矩阵）；
其余加载边界模式（`--seed-speed=0|3|9`、`--seed-fast-build`、各倍率 NaN/Infinity/0.1/0.5/9）全部 0 failed（R2）；
PC 既有 deer 套件 27/0 与 typed ANDROID host 32/0 为 #140 阶段证据，本轮未重跑（相关共享源未变动）。
#144 新增检查：固定外框/安全区 `Resize`/极小屏外框与视口界限、内容视口 last-card 可达、球优先与真实
重叠点（`PanelX` 随拖动后 X 重算）、PanelGesture 点击恰好一个动作、任意方向位移 sticky 取消、
cancel/release-outside/未拖动抬手归账零动作、滚轮与拖拽共用单调 ScrollY 与夹取、chrome 按压取消点击
不滚动、四页公共路径（`panel.Step/Toggle/Info`、无 GUI.Button/自持坐标）、15 设置各仅一页（中文标题
唯一计数）、导航/chrome/中文化契约、csproj 实际 Compile 列表、产物 `MobileModPanel`（sealed、
Draw(FloatLayout)/CancelGesture/Dispose/Toggle/Step/Info 形状）与 `PanelGesture` 类型。
共享源 SHA-256 冻结为 43 条路径、43 条 strict 校验（无 intentionallyChanged skip；本批机械重钉
`FloatLayout/MobileCalendar/MobilePopulation/四个 MobileMenu/Probe/FloatInput/csproj/AdapterTests.csproj`，
新增 `MobileModPanel.cs`/`PanelGesture.cs`/`il2cpp/ModPanelStyles.cs`/`il2cpp/ModPanelControls.cs`/
`il2cpp/ImGuiCompat.cs` 为实际校验项）。host 测试只检查适配层与产物元数据，不伪造 Unity/Harmony/IL2CPP
运行结果、真实触屏手感或字体观感。

Issue #146 批：冻结路径 44 条（新增 `il2cpp/PatchWorld_OptionalVegetation.cs` 为实际校验项，
重钉 `Probe.cs`（Root 接入 0.0.21/3 目标/Tick）、`MobilePlayerConfig.cs`、`MobileVegetationMenu.cs`、
`OhMyMods.AndroidProbe.csproj`、`AdapterTests.csproj`），strict 适配层默认 676/0、`--oldcfg` 568/0、
0 skip；新断言含 16 entry/READY 字段/25→28 目标形状/无 `SpriteRendererFX`·`FadeOut` 引用负面检查/
颜色写入回执字段（逐层 `Attempted`/`PendingIntent`）/公共密灌木行与状态短路/末行顺序。

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
PR129 已合并（72a2b745），Issue127 与代码任务已按正常入口 done/closed；无公开APK、tag/release或PC/手机部署。

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


## 0.0.16 新岛地图长度隔离设备验证（Issue #134）

精确 Main `232de3ba…` / 私有 APK `c82864b2…` 通过源码独立审查和安装关口。原生存档与
完整偏好在安装后、首次启动前逐字节同备份；此结论只适用于该时刻。21 个唯一方法与参数数量
目标读回，旧19形状保持，无 ERROR，既有 loader Warning 保留，Mods 只有主 DLL。
真实触屏 Home→Island generation，Length 1→2→3→4→5→1；另选2后冷启动读回2，旧11项保持。
Generation 250 的 Back 实际点击回到 Mod Home。

原生 Campaign UI 显示 Save Slot 2 为空，完整 Release/UserData 再备份后，仅通过正常
New Game→Call of Olympus→Normal→Start 创建该空槽。实际场景 `blocks_greece`，共享补丁
日志 baseline/layoutTotal=252、slider=2、target/plannedWidth=504、addedWidth=252、
addedBlocks=13、seams=2、candidates=4；原生生成结束后读回 edges=[-304,200]、width=504。
这是一次普通 Greek 新岛自然生成，证明实际候选准入、GetBlocks 结果传递与最终原生宽度；
没有 prefab fixture、原生 Invoke、测试帮手或存档字段编辑。正常右滑退出开场，能显示地形与玩家。

界面恢复1后通过原生 Save 保存并冷启，loader读回1，画面恢复同一新岛起点，日志没有再次
MapWidth 规划；没有独立重测冷重载后的 edges，不能据此宣称保存后全图宽度已验。原生战役
界面仍有 Slot 1 与新 Slot 2，未覆盖占用槽；整个原生文件因正常创建/保存而改变，不称最终
字节保持。最终12项设置（新倍率1、旧11保持）、21注册、主 DLL、无ERROR，游戏已关闭。

全部倍率地形连续性/地标、其它 biome、跨岛、池复用、原生异常与嵌套路径、手机/平板和联机
均仍待验。私有原始日志、偏好、截图与全量备份见 task 的 device-evidence 索引，不进公开包。


## Issue #136 法杖基础冷却的隔离设备边界

精确 Main `7364422b…` / 私有 APK `99074ca1…` 通过独立源码审查和安装关口后，更新到
同一 API35 ARM64 MoltenVK 模拟器。首次启动前完整偏好与原生存档逐字节同备份；此结论
只适用于该时刻。原22313包成员（含AndroidManifest）保持，包内仅主Mod，无诊断插件。

冷启0.0.17，23个唯一type/arity目标读回；新增完整父名
`HermesStaff._StartAbilityRoutine_d__17.MoveNext:0` 与 `ItemOfPower.CanCancel:0`
各1prefix/0postfix/1finalizer，旧21形状保持，无ERROR，既有loader两项Warning仍记录。
Player562实际触屏 Staff base cooldown 行与 Back（物理约450/528）通过；倍率
1→0.8→0.6→0.4→0.2→1全档保存，旧12项值保持，Back返回Mod Home。另选0.8冷启，
loader和界面均读回0.8；偏好十进制表示按float32比较。UI恢复1并再冷启读回1，
最后仅主DLL、游戏已停止。浮球48/72命中与默认收起保持，未加新输入层。

本轮只验证注册、菜单与设置持久化，没有自然法杖技能使用/取消样本，不能据此宣称
基础冷却、每目标附加时间、真实取消窗口或异常清理ABI已在设备验证。未知订阅者窗口内
二次查询、池复用、跨岛/存读档、手机/平板、联机和全部AOT仍待验。原生档案没有字段编辑；
首次启动前字节保持不延伸为全部冷启动后的终态字节承诺。无公开APK/loader/game/tag发布。


## Issue #138 快速森林退缩的源码阶段

本轮是源码与设置阶段：第 14 个 entry `FastForestRecedeEnabled`（默认 OFF，切换恰好一次
`SaveToFile`，READY 行输出 `forestRecede=<bool>`）；World 页第 332 行改为 Vegetation 入口，
Vegetation 页（面板 328）三行：Stock cats（复用原文案与 `ToggleFarmCats`，只影响以后关卡
加载，不删除已有猫）/ Fast forest recede / Back（只清 `VegetationPage`，回到仍打开的 World 页）。
功能本体是共享源 `il2cpp/PatchWorld_FastForestRecede.cs`（`#if ANDROID` 下仅 `Forest`/`ForestItem`
两个类型别名，PC 与 Android 编译同一份）：`ForestItem.FadeAndRemove` 前缀只改本次调用的
ref delay —— 显式正 delay ÷3；否则 `removeDelay × Random(0.5,1.5) ÷ 3`；仅在有限正值时提交。
默认关闭时 entry gate 先于一切 item/native 访问（零 native 访问）；controlsForestSize /
removedByForest / 当前 `Managers.world` + gameLayer 场景/active/child 门全部保留，视差 item
（同场景非 gameLayer 子孙）仍可命中；native 字段、协程、淡出/火焰/销毁/森林边界全部原生。

边界（保留自有 source 语义，不新增守卫/缓存/镜像/重试）：prefix 发生在原生权威/inactive
早退之前，被拒绝或 inactive 的调用可能比纯原生多消耗一次 RNG；预计算结果非有限/非正时
不提交 ref，交回原生后 native else 分支会再掷一次 `Random`（0 参数路径的既有风险）；
访问失败只提示一次 warning（单 bool，一生一次）且本次保留原生等待参数。

证据（源码阶段，无设备/无 APK 声明）：真实 interop SDK10 `-t:Rebuild` 0W0E（Main SHA-256
`e8677420…`）；PC 既有 optional-vegetation 套件 31/0（链接新共享源）、真实 `android/OptionalQoLScope`
typed alias host 10/0、适配层默认 452/0 与 `--oldcfg` 347/0、`--seed-forest` 351/0；PC 快照
baseline/candidate 构建 0W0E，语义比较仅允许列出的 forest 迁移（10 allowed / 0 unexpected、
8 PNG 资源逐字节同、无 closure 改名；MVID 不同，不称 PE 字节一致）。

未验：实际砍树后的等待时长、销毁与森林边界更新、完整森林 cycle/池复用、树源对象池
生命周期、手机/平板与联机（含权威/inactive 早退时多消耗一次 RNG 的实际表现）。后者需在
安装关口后的设备实测中按自然砍树可观察时才记录，不用 Invoke/fixture 或存档编辑达成。

Issue #138 的模拟器验证：Main 0.0.18（e8677420…）、24 个显式目标，旧 23 个接入形状及旧 13 项配置保持。World→Vegetation 三行页面、开关 OFF→ON→OFF、ON/OFF 冷读回、返回 World/Home、浮球收起与原生 Options 重叠点击隔离已观察；Stock cats 可见但未切换。安装首启前 prefs/native 整字节保持仅证明该时刻，终态已恢复森林开关 OFF 并停止测试游戏。尚未用正常砍树测实际三分之一等待，完整森林退缩/淡出/销毁、换岛/池、手机/平板和联机均待验。私有 APK 不分发。


## Issue #140 普通鹿数量与补充频率的源码阶段

本轮是源码阶段：第 15 个 entry `DeerPopulationEnabled`（默认 OFF，切换恰好一次 `SaveToFile`，
READY 行输出 `deerPopulation=<bool>`）；Vegetation 页升级为 "Vegetation & Wildlife"（面板 406），
四行：Stock cats 98 / Fast forest recede 176 / Deer population 254（Greek only: target/refill x3，第二行按 1280x720 实机宽度测量改写避免裁切）/
Back 332（只清 `VegetationPage`，回到仍打开的 World 页）。功能本体是共享源
`il2cpp/PatchWorld_DeerPopulation.cs`（相对桌面仅 `#if ANDROID` 别名头 + Android 平台 opt-in 门，
PC 预处理输出与编译 IL/metadata 零差异）：`PopulationController.Update` 的 prefix/postfix/finalizer
在单次调用内借用三个 seasonal density ×3 与 `_actualUpdateInterval` ÷3，成功/异常/部分失败路径都
按 exact float 等值归还进入值并保留既有 Active/Pending/token/Written/identity 边界；门为 Greek
普通鹿、非 biome critters、真实 prefab Deer 非 Steed/Hind、controller 与当前 gameLayer 同场景且是其
成员、native playingOrInMenuWithClient 与 `HasWorldAuth`；默认 OFF 且无自有状态时在读取
`controller.Pointer` 之前返回（零 interop），有 owned 状态仍沿原 pointer/token/identity 清理路径
只归还自己已借 bits。首次有效应用记一条含原→applied 四字段与同次 Eligible 采集 scope 证据
（scopeGoId/sceneHandle/childOf/prefabDeer/prefabSteed/prefabHind）的日志；新增 scope 证据字段
不新增 native 读取（复用同次 Eligible 已读值，日志阶段不重新 GetComponent/IsChildOf/取 scene），
既有 `prefab.name` 读取与 PC LogApplied 原样保留、创建来源仍 Unknown；失败 warning 一生一次。
native 时钟/季节/区域/ceil 目标/生成/池/掉落与 `_elapsedTime`/`_targetDensity`/serialized
`updateInterval`/prefab/币/缩放保持原生。

证据（源码阶段，无设备/无 APK 声明）：真实 interop SDK10 `-t:Rebuild` 0W0E（Main SHA-256
`8189f33f…`）；PC 既有 deer 套件 27/0、typed ANDROID host 32/0；适配层默认 509/0 与全部加载
边界模式（oldcfg 400、seed-forest/boat 403、seed-map=4.5 404、staff/cooldown 分数档 402 等）0 failed；
PC 快照 baseline/candidate 两个 net6 构建 0W0E，语义比较 0 diffs（无允许表，不称 PE 字节一致）。

未验：实际 3x population/refill 的数量效果（需长周期自然游玩）、真实 controller 命中时的
natural apply 日志（无匹配存档则如实 pending）、换岛/读档/池复用、手机/平板与联机（含客户端/
权威变化与 native 重入）。上述需在安装关口后的设备实测按自然观察记录，不用 Invoke/fixture/
存档编辑达成。本篇只证明共享源双平台编译、host 行为边界、真实 interop 产物形状与 PC 快照
语义等价，不冒充 Unity/Harmony/IL2CPP 运行结果或真实玩法验收。


Issue #140 的模拟器验证（最终短文案候选 Main 0.0.19 `12840083…` / APK `aeca1e71…`）：25 个显式目标，旧24形状与旧14设置保持、onlyMain、无ERROR与两条既有WARNING；首冷cfg仍14键是load不写盘，新鹿entry默认false；第一次UI切换后15键。Vegetation406短文案ON/OFF完整显示、OFF→ON→OFF与ON/OFF冷读回、BackWorld/Home/收起，以及一次Home覆盖原生Options点击隔离已观察。首次正常应用和ON冷启各有一次真实普通Deer输入日志（0.027→0.081、两季冬密度0.013→0.039、当前interval÷3、当前scene/layer/真实组件门）；这不代表长期数量、补充节奏或归还/异常路径全部验收。终态DeerOFF/游戏停止，装前首启动prefs/native字节保持；before/final native快照相等仅两个时点，不承诺全流程无保存。源码独审与文案修复独审/精确安装关口通过，设备claims独审及正常代码交付收尾待做；真实长期3x、完整池/换岛/读档、手机/平板与联机仍待验。见 tasks/issue-140/device-evidence.json；私有APK不分发。

## 0.0.20 公共面板（Issue #144）

PC 面板的调色板/样式与卡片绘制提取为共享源（`il2cpp/ModPanelStyles.cs`、`il2cpp/ModPanelControls.cs`；
`GUI.DrawTexture` 桩绕行沿用 `il2cpp/ImGuiCompat.cs`），PC `ModPanel` 只做样式/卡片调用替换：原字号、
几何、ConfigEntry 回调、Slider 的 `Event.current` 门控与 Update/LateUpdate 全部原位。Android 新增
`MobileModPanel`（唯一 hotControl 手势、固定外框 min(320,可用宽) × min(480,可用高) × Scale、安全区
裁剪、内容视口滚动、中文导航“主页/玩家/世界/生成/人口”与关闭）与纯手势状态机 `PanelGesture`；
四个 MobileMenu 与 MobilePopulation 只做 presentation，经 `panel.Toggle/Step/Info` 公共路径登记 tap rect
并绘制（无 GUI.Button、无自持坐标）；15 个可用设置各只在一条页面出现一次；卡片不使用 GUI.Button 抢
capture；日历行改中文日期/季节串、人口八角色+骑士仅中文展示（Tick/时钟驱动与日志采样不变）；
浮球 48/72、默认折叠、贴边拖动与 `HitPanelBody` 球优先保持，触摸过滤仍覆盖完整面板矩形。

Android 原生 GUI 的实际兼容差异集中在共享 UI 边界：该游戏裁掉了 RectOffset 的整对象赋值、
GUIStyleState.background 的 getter/setter，以及旧 CalcSize/DrawTexture 入口。样式改为写自己的
RectOffset 四个字段；Android 值/导航样式从原生 label 派生，纯色背板经真实 native Label、矩阵
缩放和显式 viewport 求交绘制；尺寸测量使用真实 CalcSizeWithConstraints 的零约束。
纯色绘制与命中共用一份 IntersectRect。PC 原生 Button/Slider 与宽卡绘制保持原分支；无字体扫描、
API 假面、每帧重建或追加补偿路径。

最终源码：真实 interop SDK10 `-t:Rebuild` 0W0E，Main `5fc712f4…` / 151040B；strict 适配层
623/0、43 个 source pins、0 skip。PC SDK8 Release 编译 0W0E（`419d2aa3…`）：6796 个共有方法
中 6791 保持，5 个具名纯 UI 方法变化、增加 1 个纯数学 helper 和 3 个纯 UI 类型；34 个资源的
名称和字节保持。历史中间候选及实际失败保留在任务采证中；旧 seed 矩阵结果不冒充最终候选重跑。

模拟器验证：Android API35 ARM64、Unity 6000.0.61f1，最终测试 APK `f9702e2b…` 内嵌/实际加载
同一 Main，25 个旧接入形状和 15 项配置保持，只有主 Mod，无 ERROR。中文、深色金色卡片、固定
导航/关闭、内容裁剪、末卡可滚入视野、水平/垂直/回原点及拖出面板的取消点击、浮球两侧拖拽、
Calendar 原回调、原生菜单叠放输入隔离、外侧操作及失焦取消后新点击恢复均有具名记录。
实际 640×360 GUI / 1280×720 屏幕下，球直径 24/48、触控方 36/72、面板 160×240/320×480；
不是整屏遮罩。设置已恢复，测试游戏已停止。原生档与配置的命名快照相等只证明那些时点；
GUI.skin 四组边距/overflow 的两点采样一致不证明任意时刻的所有属性。见
`docs/project-harness/tasks/issue-144/device-evidence.json` 与 `implementation.md`。

未验：真实 Android 手机/平板、多指同时在面板外操作、全部 DPI/旋转/文字尺度及联机。PC 只编译
与语义审计、未运行或部署，Mono 未构建。本 UI 批不替代已有 15 项功能的完整玩法验收；私有测试
APK 不分发，不作正式版本发布。后续功能统一经 panel.Toggle/Step/Info 接入。

## Issue #146 密灌木（0.0.21 源码阶段；最终 0.0.22 观察另列）

本轮是源码与构建/测试阶段：第 16 个 entry `DenseThicketsEnabled`（默认 OFF，加载只读不 Save，
READY 追加 `denseThickets=<bool>`）；功能本体为共享源 `il2cpp/PatchWorld_OptionalVegetation.cs`
（相对桌面：`#if ANDROID` 别名头（`Grass`）、FX 四项 `#if !ANDROID` 裁剪、默认 OFF 无债的同一纯
managed 入口谓词、三个真实配置写入点各自仅真实变更一次 `ModConfig.Save`）；PC 全量编译与 IL/
metadata 机械比较由 Root 统一执行。植被页新增“密灌木”行与“密灌木状态”行（仅开启或回收中显示，
OFF 空状态条件短路、展示不触 `CurrentWorld`）；UI 只调一次共享 `TrySetDenseThickets`，本页不落盘。
Operator 在 `Probe.cs` 注册 25→28 个显式目标并每帧调共享 `Tick()` 一次（0.0.21 两个 version 标记；
本 worker 未改该文件）。
验证（本批实测）：SDK10 真实 interop `-t:Rebuild` 0W0E，Main `30be6ab2…`/164352B
（颜色 handoff R2 修复后；修复前候选 `c7dcf696…`/`0c1e1788…` 的构建/测试证据保留在
`D/dense-implementation/`）；
strict 适配层默认 676/0、`--oldcfg` 568/0（44 条 source pins、0 skip；产物负面断言确认无
`SpriteRendererFX`/`BaseSpriteFX`/`FadeOut` 引用，且共享回执含逐层 `Attempted`/`PendingIntent`）；
typed ANDROID 密灌木 host 22/0（原 14 + 颜色写入责任 8；先红后绿的原始失败与日志见
`D/dense-implementation/color-fix/` 与 `color-handoff-r2/`）；PC 既有 31 个 optional-vegetation
用例 31/0；PC 预处理等价
自查仅含“去 `BepInEx.Configuration` using + 三处局部 `var` + 注释”三类差异。原始失败与日志见
`D/dense-implementation/`（worker 报告、`worker-color-fix-report.md`、`worker-color-handoff-r2-report.md`、
`logs/`、`color-fix/logs/`、`color-handoff-r2/logs/`）。
未验（如实保留）：设备安装、自然额外实例的真实清理与“回收中暂不能重开”、ON/OFF 冷读回尚未实机
观察；真实 `Grass.RemoveThicket` 入池后的 RGBA 完整往返、长周期、手机/多指/联机均待验；本批不
升级原 15 项功能的完整玩法状态，也不代表发布。

### 最终 0.0.22：公共长状态排版与有限模拟器观察

最终 Main `1480d71aad532bbc1e20fb7ed5153ad0114d9c217fd6a67886edd5b5a85df5fe`/164864B，
真实 SDK10 Rebuild 0W0E、strict 676/0 与 oldcfg 568/0、44 pins 0 skip。密灌木算法与已审 R2
逐字相同；公共 Android Info 行仅在非交互、非空长值挤占标题时上下排列，测量/绘制/帮助行
共用一份 metrics，短信息、交互卡与空值保持原路径。PC 实际 SDK8 Release 的 1290 类型、
6811 共有方法及 34 资源均零差异；没有 PC 运行部署或 Mono 构建。

私有 APK `be803edd…`同签名保留数据安装，包/installed base 与运行 Main 已核对，只有 Main。
模拟器 API35 ARM64 的 28 不同目标/参数数目组合及旧 25 形状保持、0 ERROR。实际 ON 的原生输入
间距 6→3、额外登记；OFF 六登记清理、清理期拒重开与随后登记清空已观察。完整长状态
“回收中 6 个额外实例”、短信息、开关、滚动后返回世界/收球通过；最终 OFF 冷读回、旧 15 值
保持、16 项配置、仅最终 Main/游戏停止。中间 30be/7ed 的 ON 冷读回与五实例清理单列。

上述最终观察更新此前“源码阶段未安装”的历史状态；日志中的登记清空不单独证明真实原生
Remove/RGBA 入池往返，调用结束后的间距未直接仪表读取。长期/全部 biome/换岛、手机、多指、
联机及旧 15 功能完整玩法仍待验。两条启动警告与归属/拒重开提示分开记录，不分发原游戏、
loader、interop 或实验 APK。详情见 `docs/project-harness/tasks/issue-146/implementation.md`。

## Issue #148 普通远距夜袭出发补偿（0.0.23 源码与有限模拟器验证）

默认关闭的第17项真实 `NightDepartureEnabled` 接入现有世界页。复用 PC 的单份
`PatchWorld_NightDeparture.cs` 和 `NightDepartureTiming.cs`，只在原 scope 建立与提交复核处
增加 Android 设置门；每次调用仍建立 disabled mask，关闭的内层调用不会继承外层资格。
保持原生 `GetTimesOfDayForDay`、异常交回、身份/世界层/日序/权限/当前时间复核；最多提前两游戏小时，
原生波次、门、到达参数、数量与速度保持。PC 原行为不变，没有周期映射副本或新增扫描/重试。

公共开关“远距夜袭出发补偿”只在真实变更时保存一次，同值零次，加载不保存；旧16项设置保持。
显式注册两个真实长入口的完整 patch 集：`Director.ScheduleWaveToday(Wave,Side,float)`
为 prefix 1 / postfix 0 / finalizer 1，`EnemyManager.GetWaveTravelTime(Wave,float,int)`
为 0 / 1 / 0，旧28保持至30；原来的小浮球、样式、滚动与触摸路径保持。

Main0.0.23/`bea349d7…` 实际 SDK10 Rebuild 0W0E；strict默认697/0、旧配置589/0、46 pins/0skip；
同生产源码 Android host 默认/预置ON/旧16模式各133/0（同一套断言的三个模式，不相加称独立场景），
PC原回归117/0。实际PC SDK8 Release234源码/34资源、1290共同类型/6811方法零语义差异，
没有运行或部署PC、没有构建Mono。源码已独立批准；私有APK`83066b5d…`已通过CRC/签名/22313原项逐字保持审计，
同签名保数据更新。启动前旧Main/配置/原生档逐字保持；loader自动刷新为Mainbea34，没有单Main手推。
模拟器实际30唯一(name+arity)目标/旧28形状保持，公共世界页末行中文/滚动/ON与OFF、ON冷读回与
最终OFF冷读回通过，17项字典保留原16值。最终仅Main、Night=false、游戏停止，具名loader日志无ERROR。
409.44秒有限自然观察包含具名cold gap，没有出现有效scope样本；首个自然排期的E/D/current与
补偿决策及真正远距夜袭效果继续待验，不称已验证。原生档SHA只在具名安装前与最终读回相同，
不宣称游戏从未自动落盘。内部周期映射作为原生API内部实现
保持UNKNOWN；不以UI/注册/宿主全绿代替玩法，也不人为改时、生成波次或新建战役。手机、
多指、联机与长期行为待验，旧Dense的NativeRemove/RGBA入池边界不升级。
