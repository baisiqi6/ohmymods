# android/ — Kingdom Two Crowns Android 移植探针（源码）

目标运行时：Unity 6000.0.61f1（Android arm64）+ Il2CppInterop 1.5.1 + LemonLoader 部署的
MelonLoader 0.7.3（net6 loader 目录）。产物 `OhMyMods.AndroidProbe.dll` 沿用旧部署名，
避免旧 deployment refresh 策略残留第二个 mainMod。

## 构建

    <dotnet10>/dotnet build -c Release \
        -p:GameInteropDir=<INTEROP_DIR> \
        -p:LoaderRuntimeDir=<LOADER_NET6_DIR> \
        android/OhMyMods.AndroidProbe.csproj

- `GameInteropDir`：用户本机自有的目标 APK 经 Il2CppInterop 生成的互操作目录
  （`Assembly-CSharp.dll`、`UnityEngine.*.dll`、`Il2Cppmscorlib.dll`、`Il2CppSystem.Core.dll` 等）。
- `LoaderRuntimeDir`：loader 部署中的 net6 运行时目录（`MelonLoader.dll`、`0Harmony.dll`、
  `Il2CppInterop.Runtime.dll` 等），例如 LemonLoader 部署层
  `assets/LemonLoader/runtime/loader/net6`。
- 两个参数必填。目录不存在或缺关键 DLL 时构建直接给出 MSBuild error；工程不会自动下载、
  生成或安装任何依赖，也不会把游戏/loader 二进制复制进输出（`Private=false`）。
- 仓库不分发原版 APK、loader（含 winhttp/native host）或 interop 二进制；本目录只含 mod 源码。

## 源码构成

| 文件 | 用途 |
|---|---|
| `Probe.cs` | MelonLoader 入口、显式 Harmony 注册、浮球组件与触摸守卫 |
| `FloatInput.cs` `TouchClaims.cs` `FloatLayout.cs` | 浮球触摸归属与面板几何（Home 484 / Player·World 406 / Population 376，绘制与触摸同源） |
| `MobileCalendar.cs` `CalendarSnapshot.cs` | 日历显示（开关直读 `CalendarEnabled` entry，切换时保存） |
| `MobilePopulation.cs` `PopulationCounts.cs` | 当前岛人口（只读缓存） |
| `MobilePlayerConfig.cs` | 设置唯一来源：单个 `[OhMyMods.Android]` MelonPreferences 分节共 7 个 entry（速度、无限体力、Hold、日历、敌人数量、威胁成长、无限货币；切换即保存）。`InfiniteMoney` 的原生写不在本文件：经 `Initialize(Action<bool>)` 注入，由 `Probe.cs` 传 lambda 接线 |
| `MobilePlayerMenu.cs` | Player 页 UI（speed / stamina / hold / back；中性文案 "Device settings"） |
| `MobileWorldMenu.cs` | World 页 UI（enemies / threat growth / infinite money / back；Back 只置 `Layout.WorldPage=false`） |
| `PatchWorld_Mover.cs` `PatchRide_InfiniteStamina.cs` | 速度倍率与无限体力补丁（显式注册；旧 lease 补偿已删） |
| `OptionalQoLScope.cs` | 本机世界/层/场景闸门 |
| `GlobalAliases.cs` | 把共享桌面源的裸游戏类型名映射到 Android interop 的 `Il2Cpp.*` |
| `HoldBridges.cs` | `ModPanel.IsShown`、`KingdomEnhancedPlugin.Instance.LogSource` 薄桥（`LogWarning/LogInfo/LogError(object)`） |
| `AssemblyInfo.cs` | `[assembly: MelonLoader.HarmonyDontPatchAll]` |
| `../il2cpp/PatchPlayer_HoldPurchase.cs` | 未修改链接的生产源（长按续买，默认关闭） |
| `../il2cpp/PatchWorld_EnemyManager.cs` | 未修改链接的生产源（`AddEnemies` 数量倍率、`GetEnemies` 三个成长天数倍率前缀；默认 1x 不介入） |

## 功能范围与状态

- 浮球拖动/展开、日历、人口、速度、无限体力：沿用上一轮设备验证过的 0.0.8 语义；
  速度倍率的旧 ThreadStatic lease/reentrancy 补偿已按原生诊断删除（三入口原生地址互异、
  9/9 maxDepth=1），`ScalePlayerSpeed` 是唯一注册入口。
- 设置（唯一来源）：loader 标准 `UserData/MelonPreferences.cfg` 的 `[OhMyMods.Android]`
  分节，不 SetFilePath 自写路径、不每帧读回、不重试、不镜像状态。
  键与默认值：`SpeedMultiplier=1`、`InfiniteSteedStamina=false`、`HoldPurchaseEnabled=false`、
  `CalendarEnabled=false`、`EnemyCountMultiplier=1`、`EnemyTimelineSpeed=1`、
  `InfiniteMoney=false`。加载边界各一次：速度 clamp 1–5；两个 float 倍率有限值 clamp
  1–5，NaN/Infinity（手工编辑 cfg 的非法输入）回 1 并各 Warning 一次——`Math.Clamp(NaN,…)`
  会返回 NaN，故显式判非有限；只改内存，不回写。切换时恰好一次
  `Category.SaveToFile(printmsg:false)`；失败由 loader 自身 `MelonLogger.Error` 输出真实
  异常（实际 IL 已核），UI 不承诺“已保存”，7 个 entry 都不另存镜像
  （`Enabled` 是无 UI、不持久化的会话总开关）。`InfiniteMoney` 的原生静态开关只在初始化
  与每次切换各写一次（entry 改 → apply → save），不订阅事件、不每帧写；`ModConfig`
  本体零 `Il2Cpp.*` 引用，写入由 `Probe.cs` 的 lambda 承担。
- 长按续买（Hold purchase）：共享桌面源链接编译，默认 OFF（不写钱包/库存）。
- 敌人参数（C2）：`../il2cpp/PatchWorld_EnemyManager.cs` 未修改链接；两个前缀分别缩放
  `AddEnemies` 的数量 multiplier 与 `GetEnemies` 的三个成长天数 int（`Mathf.RoundToInt`
  半值取偶），默认 1x 直接放行；不生成额外波次、不改昼夜时钟/排期，也不是 #111/#94 的修复。
  UI 档位步进先在加载值上 `MathF.Floor` 到整数档再进一（1.5→2、4.5→5、5→1），合法载入的
  小数（如 4.5）不会被推成 5.5；载入值原样保留，不在运行期加 clamp/守卫。
- 无限货币：只写原生 `Wallet.InfiniteMoney` 静态开关（native get/set 为纯静态读写、setter
  无副作用、消费点在 `RemoveCurrencyAfterMoving`；见 `native-world-recon/`），不送钱、
  不改余额/币对象/扣款实现；不同货币类型与联机效果未验。快速建造（C1）不在本阶段：
  rate 残留与 prefab 重建语义未核验完，不带半成品搬入，`_autoBuildRate` 相关桥接不落地。
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
     Home 第 4 行为 `World` 入口（`Layout.WorldPage`），第 5 行为 `Close`。
- 独立 Android API35 ARM64 模拟器已验：四设置首次默认值、界面切换、配置读回和进程
  重启恢复；隔离配置的保存故障可见日志；去 lease 后三速度入口九组原生 API 对照及
  2x/5x 正常触屏移动；体力 ON/OFF 的原生消耗与速率归还。诊断插件仅用于采证。
- 实际触屏长按投石器商店一次完成三笔原生 2 币购买，余额 16→10，三次 `PerformPay`；
  满架停止，临时间隔 0.0375→0.15 归还。初次触屏付款进入 Transaction，后续续买进入
  Holding；没有为测试改钱包或库存。原共享续买距离上限 0.5 保持，需靠近
  `PlayerPayPoint()`，其位置可能与商店图像中心不同。
- 本轮（World settings / 敌人参数 / 无限货币）：产物按真实 interop/loader 引用编译通过，
  适配层 host 测试与产物元数据检查全过（命令见下节）。**尚未**在设备上验证：World 页
  触摸/绘制与 484 底行、两个敌人 target 的注册计数与首夜入参、货币开关的原生付款观察。
  这些设备关口另行执行并单独记录；本段不宣称 0.0.10 已在设备跑过上述功能。

## 与桌面（Windows / Unity 2022 / 2.4.0 线）的 API 差异

- Android interop 把全局命名空间游戏类型放到 `Il2Cpp.` 前缀（桌面 BepInEx interop 无前缀）；
  由 `GlobalAliases.cs` 的 12 个 global alias 映射，冲突的本地 alias 已按 CS1537 规则移除。
- `Player.PayState` 含 4–6 三个中间值（`StateKeyDown/StateKeyHoldDetected/StateKeyHold`）：
  **不是 Android 新增**——桌面 2.4.0 interop 同样包含（`recon/evidence/desktop-paystate.json`，
  撤回旧“Android 新增”表述）；本次真实触屏采购观察到 0/1/2/3，不能据此声称所有
  输入和交易路径都不会进入 4–6。
- `Payable.forceBlockPayment` 以 property 暴露（桌面反汇编中为 +0x100 偏移）。
- Android interop 成员不带 `[Token]`/`[Address]` 特性；运行时身份需 `il2cpp_method_get_token`。
- loader 的 `MelonLoader.dll` 自带顶层 `Harmony` 命名空间：本引用集下裸 `Harmony` 报 CS0118，
  须全限定 `HarmonyLib.Harmony`。

## 验证边界（截至 2026-10-03）

- 已做：`net10.0` + 真实 interop/loader 引用编译 0 warning / 0 error（含新增链接的
  `PatchWorld_EnemyManager.cs`）；共享 hold-purchase 行为套件（链接未修改生产源，
  71 场景）全过；适配层测试（Home 484 底行命中、Player/World 406、Population 376；
  ModConfig 7 entry/默认值/加载边界 clamp 与 NaN·Infinity 回退/切换仅保存一次/
  Initialize seam 恰好三次 apply；敌人前缀 1x/2x/5x、关闭态放行、ties-to-even 取整与
  ref 输入输出；产物元数据含 Hold 4 + World 2 handler 及 Enemy/Wave/Wallet 绑定）全过；
  loader 0.7.3 `CreateEntry` 吸入已存值与 `SaveToFile` 失败语义有实际 IL 证据
  （本任务 `settings-implementation/evidence/`）。
- 已做：独立模拟器内的实际 APK 打包/安装、上述触屏与原生 API 对照、设置恢复与保存
  故障。原游戏文件逐文件保持，仅改加载入口并重新签名；APK 只包含主 Mod，测试助手
  不随包交付。具体候选与私有日志在维护者本机记录，不分发游戏。
- 未验：手机/平板兼容、特殊坐骑技能、所有商店类型与弹药续买、跨岛运输、完整身份
  存档恢复、联机；后续功能仍需按依赖移植。原版资源或图形故障不能当移植通过，host
  stub 也不能代替设备证据。当前代码交付范围不代表完整 Mod 或所有玩法验收完成。

## 测试

适配层（先构建测试工程与产物，再直接执行已构建的测试程序；每行末尾都接产物路径）：

    <dotnet10>/dotnet run -c Release --project android/tests/AdapterTests.csproj \
        -- android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll
    <dotnet10>/dotnet android/tests/bin/Release/net10.0/AdapterTests.dll --seed-speed=9 \
        android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll

加载边界矩阵（各场景一次运行，期望 0 failed）：`--seed-speed=0|3`、`--oldcfg`、
`--seed-enemy-count=NaN|Infinity|0.5|9`、`--seed-enemy-timeline=NaN|Infinity|0.5|9`、
`--seed-enemy-count=4.5 --seed-enemy-timeline=4.5`
（`NaN`/`Infinity` 走非有限回退 1 + Warning，`0.5`/`9` 走有限 clamp 1–5，`--oldcfg`
只种入旧四键、验证新键取默认且不回写；4.5 小数档验证载入原样、UI 步进 4.5→5→1 各一次 save）。

共享行为套件（仓库根 `tests/hold-purchase`，链接同一份未修改生产源，只执行不修改）：

    <dotnet8>/dotnet run -c Release --project tests/hold-purchase/HoldPurchaseTests.csproj

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
