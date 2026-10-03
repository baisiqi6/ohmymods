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
| `FloatInput.cs` `TouchClaims.cs` `FloatLayout.cs` | 浮球触摸归属与面板几何（Player 页高 406） |
| `MobileCalendar.cs` `CalendarSnapshot.cs` | 日历显示（开关直读 `CalendarEnabled` entry，切换时保存） |
| `MobilePopulation.cs` `PopulationCounts.cs` | 当前岛人口（只读缓存） |
| `MobilePlayerConfig.cs` | 设置唯一来源：单个 `[OhMyMods.Android]` MelonPreferences 分节（速度默认 1、无限体力/Hold/日历默认 OFF；切换即保存） |
| `MobilePlayerMenu.cs` | Player 页 UI（speed / stamina / hold / back；中性文案 "Device settings"） |
| `PatchWorld_Mover.cs` `PatchRide_InfiniteStamina.cs` | 速度倍率与无限体力补丁（显式注册；旧 lease 补偿已删） |
| `OptionalQoLScope.cs` | 本机世界/层/场景闸门 |
| `GlobalAliases.cs` | 把共享桌面源的裸游戏类型名映射到 Android interop 的 `Il2Cpp.*` |
| `HoldBridges.cs` | `ModPanel.IsShown`、`KingdomEnhancedPlugin.Instance.LogSource` 薄桥 |
| `AssemblyInfo.cs` | `[assembly: MelonLoader.HarmonyDontPatchAll]` |
| `../il2cpp/PatchPlayer_HoldPurchase.cs` | 未修改链接的生产源（长按续买，默认关闭） |

## 功能范围与状态

- 浮球拖动/展开、日历、人口、速度、无限体力：沿用上一轮设备验证过的 0.0.8 语义；
  速度倍率的旧 ThreadStatic lease/reentrancy 补偿已按原生诊断删除（三入口原生地址互异、
  9/9 maxDepth=1），`ScalePlayerSpeed` 是唯一注册入口。
- 设置（本段新增持久化）：唯一来源是 loader 标准 `UserData/MelonPreferences.cfg` 的
  `[OhMyMods.Android]` 分节，不 SetFilePath 自写路径、不每帧读回、不重试、不镜像状态。
  键与默认值：`SpeedMultiplier=1`（加载边界一次 clamp 1–5）、`InfiniteSteedStamina=false`、
  `HoldPurchaseEnabled=false`、`CalendarEnabled=false`。切换时恰好一次
  `Category.SaveToFile(printmsg:false)`；失败由 loader 自身 `MelonLogger.Error` 输出真实
  异常（实际 IL 已核），UI 不承诺“已保存”，四个 entry 也不另存会话 bool 镜像
  （`Enabled` 是无 UI、不持久化的会话总开关）。
- 长按续买（Hold purchase）：共享桌面源链接编译，默认 OFF（不写钱包/库存）。
- 已由 Operator 集成（`Probe.cs`）：
  1. `OnInitializeMelon` 最早处 `KingdomEnhancedPlugin.Initialize()` +
     `ModConfig.Initialize()`（先于所有 patch 注册与 UI 读取）；
  2. Hold 四个 handler 的显式注册（`private static`；`HarmonyDontPatchAll` 抑制自动扫描）；
  3. Mover 三入口以 `ScalePlayerSpeed` 为唯一 prefix，`LogHookCounts` 输出
     `ANDROID_HOOK_COUNTS`（冷启动计数验收信号）；
  4. `Probe.OnUpdate` 调 `PatchPlayer_HoldPurchase.Tick()`（非必需路径，退化安全）；
     Player 页 = `MobilePlayerMenu.Draw`。
- 独立 Android API35 ARM64 模拟器已验：四设置首次默认值、界面切换、配置读回和进程
  重启恢复；隔离配置的保存故障可见日志；去 lease 后三速度入口九组原生 API 对照及
  2x/5x 正常触屏移动；体力 ON/OFF 的原生消耗与速率归还。诊断插件仅用于采证。
- 实际触屏长按投石器商店一次完成三笔原生 2 币购买，余额 16→10，三次 `PerformPay`；
  满架停止，临时间隔 0.0375→0.15 归还。初次触屏付款进入 Transaction，后续续买进入
  Holding；没有为测试改钱包或库存。原共享续买距离上限 0.5 保持，需靠近
  `PlayerPayPoint()`，其位置可能与商店图像中心不同。

## 与桌面（Windows / Unity 2022 / 2.4.0 线）的 API 差异

- Android interop 把全局命名空间游戏类型放到 `Il2Cpp.` 前缀（桌面 BepInEx interop 无前缀）；
  由 `GlobalAliases.cs` 的 10 个 global alias 映射，冲突的本地 alias 已按 CS1537 规则移除。
- `Player.PayState` 含 4–6 三个中间值（`StateKeyDown/StateKeyHoldDetected/StateKeyHold`）：
  **不是 Android 新增**——桌面 2.4.0 interop 同样包含（`recon/evidence/desktop-paystate.json`，
  撤回旧“Android 新增”表述）；本次真实触屏采购观察到 0/1/2/3，不能据此声称所有
  输入和交易路径都不会进入 4–6。
- `Payable.forceBlockPayment` 以 property 暴露（桌面反汇编中为 +0x100 偏移）。
- Android interop 成员不带 `[Token]`/`[Address]` 特性；运行时身份需 `il2cpp_method_get_token`。
- loader 的 `MelonLoader.dll` 自带顶层 `Harmony` 命名空间：本引用集下裸 `Harmony` 报 CS0118，
  须全限定 `HarmonyLib.Harmony`。

## 验证边界（截至 2026-10-03）

- 已做：`net10.0` + 真实 interop/loader 引用编译 0 warning / 0 error；共享 hold-purchase
  行为套件（链接未修改生产源，71 场景）全过；适配层测试（FloatLayout 406 触摸边界、
  ModConfig 单一 category/默认值/加载边界 clamp/切换仅保存一次、产物元数据）全过；
  loader 0.7.3 `CreateEntry` 吸入已存值与 `SaveToFile` 失败语义有实际 IL 证据
  （本任务 `settings-implementation/evidence/`）。
- 已做：独立模拟器内的实际 APK 打包/安装、上述触屏与原生 API 对照、设置恢复与保存
  故障。原游戏文件逐文件保持，仅改加载入口并重新签名；APK 只包含主 Mod，测试助手
  不随包交付。具体候选与私有日志在维护者本机记录，不分发游戏。
- 未验：手机/平板兼容、特殊坐骑技能、所有商店类型与弹药续买、跨岛运输、完整身份
  存档恢复、联机；后续功能仍需按依赖移植。原版资源或图形故障不能当移植通过，host
  stub 也不能代替设备证据。当前代码交付范围不代表完整 Mod 或所有玩法验收完成。

## 测试

适配层（默认场景；另跑两个加载边界场景，先构建再直接执行已构建的测试程序）：

    <dotnet10>/dotnet run -c Release --project android/tests/AdapterTests.csproj \
        -- android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll
    <dotnet10>/dotnet android/tests/bin/Release/net10.0/AdapterTests.dll --seed-speed=9 \
        android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll
    <dotnet10>/dotnet android/tests/bin/Release/net10.0/AdapterTests.dll --seed-speed=0 \
        android/bin/Release/net10.0/OhMyMods.AndroidProbe.dll

共享行为套件（仓库根 `tests/hold-purchase`，链接同一份未修改生产源，只执行不修改）：

    <dotnet8>/dotnet run -c Release --project tests/hold-purchase/HoldPurchaseTests.csproj
