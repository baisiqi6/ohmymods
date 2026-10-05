# Android 远距普通夜袭出发补偿 — 完整产品计划候选

硬前置：Issue146 对应 PR147 正常合并与 GitHub CLOSED、canonical done/closed；peer101短窗口显式归还后 fresh 完整对象/计划读回。在前置与本文件精确批准均满足前，只准备，不建 Issue、不接受 assignment、不改 W。

## 事实与复用

复用现 `NightDepartureTiming.cs` 纯浮点规划器与 `PatchWorld_NightDeparture.cs` 的有限调用 scope；只读查询原生当前日与次日 `GetTimesOfDayForDay`。Android 声明/指定原生体已独审：调度 day 入参同族、四 day 属性入口尾跳同一读路径、参数原样传给原生周期 API；实际 return 与 TimesOfDay.dawnStart/eveningStart Single 声明均存在。完整周期映射/不同调用间数值恒等不因此获证；已批准停止深挖 ref 重载，原生内部映射仍为 UNKNOWN，作为原生 API 的内部实现保留，不复制算法。

两个调用都在现 prefix 的 try/catch 内，MaxValue+1 与规划器有限值检查保持；身份/世界层/scene/波/side/day/current/authority 复核保持，不能因属性入口别名撤掉前后复核。规则只改本次 GetWaveTravelTime 的返回，最多提前两游戏小时；保留原门、同一波、arrival、数量、速度、晨退、事件与原生存档，不重建排期、无新扫描/缓存/mirror/retry。

## 平台与分批启用

共享代码仅精确 ANDROID 类型别名、必要 namespace/log 桥、配置门与有界观察点适配。PC 条件编译后的现控制流/字段/资源必须由实际 DLL 对比保持。不能复刻 scope 或规划器，也不 hook 三个薄 day getter。

拟新增第17项真实 `NightDepartureEnabled` 默认 false，作为 Android 分批启用门；PC 现总 Enabled 下的默认语义保持。门放在现 Arm 与提交前复核位置，OFF 调用仍压 disabled frame 避免继承外层资格，不能在 prefix 提前跳过 push 破坏嵌套屏蔽。第17项只在 scope 建立与提交复核处读取，不新增每帧轮询或事件订阅；设置唯一来自真实 entry，不有事件镜像；公共 World 页新增“远距夜袭出发补偿”开关，说明“仅之后的普通夜袭排期、上限两游戏小时、实际远距效果待验”。用现 Toggle/Info，保存真实变更一次，不另建 UI 架构。

Root 在 Probe 统一两版本为实验0.0.23、两个长入口显式注册，须覆盖两个共享 patch 类定义的全部 patch 方法集，等价原 PC PatchAll 应用面；strict 对每 target 的 prefix/postfix/finalizer 精确计数断言（具体方法集以生产类与实际签名为准），旧28保持至30；别名按 actual interop exact type，不凭 PC 注释/RVA。源主线与 Mono、PC 安装/游戏全部不碰。

## 必要源码与设备验证

直接编译同一生产策略/scope 的相关原 PC 套件与 typed ANDROID namespace 编译；覆盖 OFF disabled mask、regular-only、嵌套/overflow/stale token/原生异常身份、scene/day/side/wave失配、单次消费、finite/near/dusk/late/next-day-daytime/补偿上限等实际合同。共用已有 host，不能为凑绿 fake API 或镜像生产流程。

一次真实 Main Rebuild0W0E、strict默认与oldcfg及17entry/30目标/UI单次Save/源码pins；17项真实配置矩阵明确载入0次Save、实际变更1次、同值0次，旧16项不回归；完整 PC baseline/candidate 类型/方法/资源零差异。独立源码审查后一次包/CRC/签名/保数据安装关口；模拟器首先新开关UI与冷读回/旧16值保持/30旧28shape/0ERROR。

首次自然有效排期的现 scope 内最多一组有界日志记录实际 E/D/current 和补偿决策；不人为改时间、手动 Invoke/spawn、造新战役或为日志新扫描/helper。没有普通远距排期样本则真实时序效果继续pending，不以参数UI/注册绿替代玩法。结束 Night=false、旧16基线、仅Main、游戏停止；NativeRemove/RGBA等146 pending不升级。

正常 GitHub 代码交付仍 release/v9.5.13、前缀 codex/，用户已授权自主实现/审查/必要验证/正常合并与任务同步；角色/经济/身份大批继续按依赖推进，手机/多指/联机/长期实机单列。资源与原 APK、loader、interop 不分发。

本批初估4–8工程小时；当前剩余按功能依赖估计7–11批、70–185工程小时（包含本批）。这是规划宽区间，依据已交付B0、公共UI及Dense的适配/stripped-API诊断成本，假设不出现新的架构级阻塞。角色、资源与战斗、身份持久化、经济与助手为剩余主体；配置接线包含在每批内，不按未映射key线性估算或重复计时。手机、多指、联机、长周期玩家验收的日历另列。

## 正常任务关联

GitHub Issue: https://github.com/baisiqi6/ohmymods/issues/148
Canonical task: issue-148
Canonical plan: docs/project-harness/tasks/issue-148/plan.md
Dependency: issue-146（正常done/closed已独立回读）
Core plan SHA256: 48b4ee3eb87ce63843fcd9ab6fdb6124280088347b5bec462c9eab0734cf5773
