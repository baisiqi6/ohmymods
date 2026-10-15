# Issue #232 — Android 职业身份与弓弩移植：G1 源码接入

负责：Mac Max / Codex / Android Operator。GitHub https://github.com/baisiqi6/ohmymods/issues/232。
当前实施基准 `6fbf46c91590fd96f20022ca8a742535fdcaad37`；分支 `codex/android-profession-archery`，工作树保留本任务已实现且未提交的 G1 源码改动。保留 Android 0.0.30 与 LemonLoader/MelonLoader + Il2CppInterop/Harmony 原架构；本阶段不替换已安装 Main/APK。

## 已完成与现有证据

G0 公共源码隔离已合并 PR233/6ca5dc2，详见 g0-source-foundation.md；#235/PR236 未发布观察撤销已合并为当前基准，GitHub及正常受管代码收尾已完成。旧计划和诊断记录仍在Git历史及本机R，不把过去168源/297refs/23CS0117当当前结果。

私有真实职业候选172源/184真refs/21具名资源、原31+9真实MelonPreferences共40项，Debug/Release零错误、原PrepareWindowObserver未接CS0649保留。v2契约108相关owner/64精确target：G1最小必装52、G2职业/弓弩32、额外联动角色24；当前工作树产品已接入173源（172共享/平台源加显式owner目录）、40真实prefs、13共享人口类别，Debug/Release 0W0E；完整host回归发现旧Android缩放别名被同namespace真类型遮蔽，原4条失败仍保留。编译及源码接线不代表运行。

64个target当前method-definition/map地址均非零且唯一；不能代替函数体/inline/Harmony ABI/运行。真实scene曾观察到DotNetAgent/dummyMode=true，但不等于writer命中。native Task/Return私有诊断已构建，尚未运行：新AVD在Addon安装/启动游戏前guest shell阻塞，已exact PID停止并保留证据，#213继续OPEN。

## 本次批准申请范围：G1 源码实现、编译和必要host审查

1. 正式产品工程复用上述真实共享源集合、184实际引用与21资源，不带PC Bepmetadata、不引第二配置/角色/保存实现。原Android PopulationCounts编译入口替换为PC共享PopulationCounts，接共享13职业真实读数与中文触屏人口展示，保留现有通用控件/小浮球；不重新扫描场景或建第二职业表。
2. 将私有已审计的9个真实配置接口落到MobilePlayerConfig原机制：三个职业bool默认OFF；弩手比例默认.25、档位.25/.5/.75/1；散射/射速/冲击bool默认OFF、箭数3且1..3、射速1.5且1..2。原31 key/type/default/归一化/回调与单次Save机制保持，优先复用CrossbowRatioPolicy.Normalize和现有SanitizeMultiplier。未完成职业的启用控件与实际打包在对应完整功能组一起交付，不伪装成可玩的按钮。
3. 沿v2精确52个G1 owner实现显式注册源码：Harmony逐个CreateClassProcessor，保留真实attributes/priority/prefix/postfix/finalizer/__state；private nested owner仅按精确已知full type解析，禁止PatchAll/程序集扫描或按target去重。不全局提前注入12种已有lazy组件，不改原注入机制。
4. G1保持真实save/marker/prepare/load/campaign边界，paid-life与pickup/boarding/promotion/drop/ReplaceBy保护，Heavy Installed六owner与真实CombatTargetLife初始化完整。完整保留Pool_FastSpawn_CrossbowmanLifecycleScope与Archer_OnEnable_CrossbowmanLifecycle两个owner（Heavy fresh-life真实producer），不抽单行新adapter；ArcherOptions.OnEnable→Hero身份边界同组保留。关闭职业/隐藏UI不能撤销已有付费权益保护。
5. Probe统一接现有初始化、Update/LateUpdate与确有归属的退出生命周期：CombatTargetLife.Initialize、PrepareWindowObserver=HeroArcherVisuals.RecordPrepareWindow，沿PC实际身份/商店/HeavyIntegration与弓弩驱动顺序复用；不新建后台时钟、扫描重试、统一重置付费身份或清未知writer/task lease。源码接线不等于原生安装执行；本阶段所有原生执行仍须下述对应关口。
6. Worker仅实现自有Android Patch_ProfessionHooks.cs等契约文件/私有candidate；Operator统一Probe/工程/配置/人口接入。共享il2cpp逻辑只读消费；唯一例外是 `il2cpp/PatchRoles_ScaleAdjustments.cs` 的平台类型别名修复：将文件级同名 ScaleRegistryHolder alias 改为唯一 RoleScaleRegistry，ANDROID 映 GreekScaleScope、else 映真实 ScaleRegistryHolder，并替换该文件原有四 Register 与一 Unregister 调用。原文件基线SHA256 `1ddc29f77b1dacaff0c4ea7df6d3274925932c52b3c910ec4106d572805a80ec`。PC在G0前父提交b5c2becffbad9a342b4ce214b953a8617475a662及更早Android适配前一直使用真实Holder，Android旧66源无真Holder，G1新增Compile后发生遮蔽；修改只恢复原Android直接scope绑定，保留PC原调用。不得扩大为驱动重构、提前注入或新补偿；PR226库存与#234盾卫runtime/耐久/维修/schema/owner仍由Main独占。未闭合时不复制Draft或整文件overlay；后续合并基准只按真实hunk/接口变化重核。不新增自动采购/银行/经济或额外24联动角色玩法。

## 验证与执行边界

当前申请只允许上述源码实现、受控编译、必要host验证与独立审查；不允许实际加载此新Main/新APK、native Hook安装或paid功能启用。G1未运行的ABI、真实保存端点与Action/GC/Harmony行为必须用真实环境验证；不得以null/常量/fake Installed或第二账本占位。环境卡点保留具体机制，不为编译补假逻辑；实现与ABI诊断准备可以并行。

对真实变更运行必要测试，旧31配置与原功能行为保持，保留四条原Register精确IL断言并新增Hermit OnDestroy直接GreekScaleScope.Unregister永久断言，五处逐点核最终callee与红绿结果；唯一alias须在全部实际Compile输入中无同名type声明。PC以同SDK/TFM/refs/资源/全源码的fresh before/after严格metadata/IL/resources零差异对照，控制编译禁部署；Android真实引用Debug/Release及最终资源/owner集合逐项核对。新增接线消除PrepareWindowObserver缺口后不压警告。独立review先核首次状态责任、paid保护/lifecycle完整、UI中文与共享控件，再核实现；构建/host/静态native/实际安装/玩法分层记录。

G2按完整依赖组继续弓弩cadence/scatter/impact及池回收、火铳手动店和弹丸/枪视觉、英雄/重盾运行时与完整资源池，必要相关小功能合并交付。不在整个移植完成前拖延公共UI整理；用户浮球与PC视觉中文约定持续有效。

整个#232所有范围代码完成必要审查验证、相关PR全部合并后再正常关单/完成receipt；只有G0或本G1源接入不关闭整批。玩家手机/平板/联机、换岛读档、池重建/攻击技能死亡撤退未覆盖如实pending，后续实机新问题另开Issue。重要任务只经正常Coordinate plan revise/approve/assignment入口，原owner/session维持，非232任务/plans/order/foreignleases保持，validator/doctor回读；不裸改JSON/DB。

R=.local/tasks/android-profession-archery-20261014；目前只申请G1 source阶段，不把memory-only probe批准扩成产品接线或运行许可。当前进度写受影响progress.md，单次过程写本任务记录，可复用发现才入逻辑地图。阶段余量按真实依赖和验证难度重新估算，不用文件数或编译成功换算完成比例。
