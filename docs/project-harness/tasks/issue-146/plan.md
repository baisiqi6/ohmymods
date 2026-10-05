# Android B1：密灌木移植

状态：方向已获 APPROVE_DENSE_B1_PLAN_AFTER_COMMON_UI；本文件整理为当前准确基线与明确 worker 合同，精确绑定后登记任务。UI #144 必须先完成 PR #145 合并与 canonical 正常代码收尾。本计划不代表已实现或设备验收。

## 基线与范围

- B0 #142 已正常 done/closed，PR #143 合并为301d25fb；公共 UI #144 提交4be268ee，主程序0.0.20/5fc712f4、最终私有 APK f9702e2b已完成有限模拟器验证。
- 完整盘点原冻结230源码/54配置/33声明资源；已核上游增量和公共UI后为234个PC源码/57配置/34声明资源。不重复全量盘点。下一源基线取正常合并后的 release/v9.5.13，先核相关文件与4be输入相同。
- 本批只处理密灌木、一个真实配置及公共植被页接线；下一实验版本0.0.21。不绑定尚未确认时序的 Night 功能、墙基任务或其他角色/经济功能。
- 复用 il2cpp/PatchWorld_OptionalVegetation.cs，不复制其约1100行算法。沿已有 OptionalQoLScope、Probe.OnUpdate 驱动与当前公共 UI。

## 已证修复点

有界原生证据在 dense-thickets-recon 与 world-ecology-batch-recon。已核原生 CanSpawnThicket 的间距消费、GrassUpdate 的 Stage7→Spawn/Remove 链、容器类型、Remove 的原生守卫及 SpriteRendererFX.set_alpha 写入 Renderer.color 的颜色通道；未核全对象池复位、所有 prefab 组件或长周期行为，不能宣称手机发生池污染。

共享源 StartFade 在 FX 成功时跳过 CaptureSprites，因而没有 BaseColors/Applied 的颜色凭据；统一 RestoreOwnedColors 到回收入口也无法归还没有产生的责任。仅提前 Capture 又会把 FX 写入视为 Foreign 而撤权。

Android 兼容处理选择裁掉该可选 FX 路径，复用已有 CaptureSprites→ApplyFallbackFade→RestoreOwnedColors→原生 Grass.RemoveThicket 路径，确保自有颜色写入之前已有凭据。PC 路径保持原语义，不借移植扩大 PC 修复范围。

#if !ANDROID 裁剪四项：FxFade 字段、StartFade FX 分支、TryStartFxFade 函数、回收阶段 FX elapsed 等待支路。Android StartFade 无条件 Capture，现有淡出与整色交还保留；最终 Android DLL 不留 SpriteRendererFX/FadeOut 引用。不增加 FX setter hook、颜色镜像或重试层。

## 源码与状态责任

- 共享源三处 ConfigEntry<bool> 局部声明改 var，移除不再使用的 BepInEx.Configuration using；PC 推断仍是原类型。所需游戏别名使用真实 Android interop，仅在平台边界声明。
- 保留 Held/Pending/token/Written、Owned/Records、完整 RGBA lastApplied 凭据、Foreign 写者撤权与 AddThicket 新 life 撤销旧颜色写权。部分写失败、原生拒绝回收和旧世界离场都有独立责任，不能因原生没有 exception 就删除其恢复边界。
- Android 默认 OFF 且 Held/Pending/Owned/Records 全空时，在 BeginCanSpawn、OnThicketAdded、nativeRemove 前后入口先用同一纯托管谓词返回，位置先于 Unity null 判定、Pointer 和字段读取。有旧责任时 OFF 仍清账；不新增状态镜像。
- Tick 已有 DrainPending/ObserveDenseConfig；空责任时保持零 native 访问，不重复加无用途守卫。只在既有 Probe.OnUpdate 调一次 Tick，不新建 Update 组件或协程 owner。
- 原生集合仍走现有 _grassWithThicket 的真实 HashSet<Grass> 判定；首次自然运行机会核实实际类型，未知时原机制不写，不另建集合 fallback 或全场扫描。

## 配置与公共 UI

第16项 MelonPreferences entry：DenseThicketsEnabled，默认 false，初始化只读载入不 Save，READY 行追加 denseThickets。旧15项键、默认值和行为保持。

持久化只在真实写入者处发生：共享 TrySetDenseThickets 的两个 entry 赋值点，以及 ObserveDenseConfig 的 forced-off 赋值点，在 ANDROID 下仅 old!=new 时调用一次已有 ModConfig.Save。PC 原 setter 行为不改。

次数契约：载入0、同值0、拒开启且已false为0、实际改变1、拒开启但纠正旧true→false为1、Observe强制false为1。UI只调用一次现 TrySet 请求，不再 Save；保存失败由 loader 原日志报告，不保证已保存，不重试。

植被页增加“密灌木”公共 Toggle/Info 行，帮助准确说明间距减半、关闭只回收额外实例和回收中暂不能重开。只用既有 MobileModPanel.Toggle/Step/Info，内容高度自动测量累计；FloatLayout 固定外框320×480×Scale、PanelGesture、命中面与小浮球全部不改。不恢复固定行坐标或高度表。OFF 空状态展示不触 CurrentWorld。

Operator在Probe统一注册已有三个精确目标：World.CanSpawnThicket(Grass) prefix/postfix/finalizer=1/1/1，World.AddThicket(Grass) postfix=0/1/0，Grass.RemoveThicket() prefix/postfix=1/1/0；25→28个显式目标，旧接入形状保持。无PatchAll、无GrassUpdate/Spawn新增hook。

## 实现角色与必要验证

功能实现依AGENTS按fresh北京时间选择Worker；Operator改Probe入口/版本和正常任务文档，Worker处理共享源、配置、植被页、项目与相关测试。独立Reviewer先审修复位置和责任，再审实现。当前PC/Mono部署、native存档编辑、GitHub/canonical写入不归Worker。

- 实际Android SDK10 Rebuild 0W0E，一次strict adapter；旧623检查/43pins按真实源码变化机械更新、不skip；新增16项配置、28目标形状、Save次数矩阵、公共末行可滚达与旧15项保持。
- 复跑相关PC optional-vegetation 31检查和新的typed ANDROID host（直接编译生产源）。覆盖OFF空状态native getter抛错仍零访问、scalar setter已写后抛、Pending在OFF归还、嵌套不再次减半、stale token、原生exception、完整RGBA凭据、回收前还色、Foreign写者不覆写、同ptr/id新life撤权，及FX存在时Android也不启动它。
- PC实际baseline/candidate均取真实项目版本与34资源，确认共享可执行行为/metadata保持；var与#if的平台分流不能改变PC。只编译不运行部署，Mono不构建，不重无关99套。
- 源码独审后按批一次APK打包/安装关口，复用已有Patcher与内容/CRC/签名审计，仅Main入包。模拟器先fresh备份当前Main5fc/配置/原生档，同签名更新不clearData，首启前保持原字节。
- 有限设备观察：自然原档的World调用、UI OFF→ON→OFF、ON/OFF冷读回、实际有额外实例时的清理和拒重开。日志只在首次有效应用/owned事件记必要类型、原间距→半间距与归还责任；不逐帧诊断、不manual Invoke、改elapsed、作弊生成或为验收新建整战役。没有自然extra/回收机会则该项如实pending，不编造玩法完成。
- 未观察的真实Remove/入池RGBA完整往返/长周期、手机、同时多指、联机仍待验；本批不升级原15项完整玩法状态。

## 正常交付与余量

UI144正常代码收尾后新建GitHub Issue、精确plan批准与assignment；依赖144，分支前缀codex/。源码/设备声明分别审查后正常PR（base release/v9.5.13）、attach、同伴复核合并，正常CLI记录代码任务完成与实机待验；fresh前后保全其他任务对象、顺序和101最新完整计划，doctor通过。不手改checklist/DB。

本批预计4–8工程小时，必要自然观察的日历不确定。完整移植仍按8–12功能批、约80–200工程小时宽范围规划（配置接线已经包含，不重复计时）；下一实际功能批结束再收窄。手机与联机验收日历单列。

## 正常任务关联

GitHub Issue: https://github.com/baisiqi6/ohmymods/issues/146
Canonical task: issue-146
Canonical plan: docs/project-harness/tasks/issue-146/plan.md
Dependency: issue-144（正常done/closed已独立回读）
Core plan SHA256: fe0250d7f21be3f9b3ef23120ecf01632dc55e0521a95b08d5e3f2e1f80c368b
