# Issue136 Android 法杖基础冷却：原生消费窗口适配（C6）

依赖：PR135 实际 merge b3e120b78a69a155b24bac2fa14338dd373cb53e，canonical issue-134 已正常 done/closed，独立核其它150完整对象/order151与doctor0errors0warnings；本任务通过 normal Issue136/planapprove/deps134/accept后实施。源工作树 /Users/Admin/.codex/worktrees/android-construction/ohmymods，下一分支 codex/android-staff-cooldown。peer101 Shared 服务、未合并 PR128 墙基/塔基不并发修改。

范围：Android 0.0.17 增加 StaffCooldownMultiplier 基础冷却倍率，默认1，有限值0.2–1，非法一次告警回1，加载不回写。新次法杖使用按当次当前基础值缩放；每转化目标附加时间保持原生计算，已排程 nextActivationTime 不追溯。桌面 HermesStaff 的32数量、范围×2、永久转化仍在完整移植后续块，不随本冷却切片打包，也不静默删除需求。

原生事实：_itemCooldown 声明于 ItemOfPower，当前 prefab/序列化值未知，不假设30秒。HermesStaff._StartAbilityRoutine_d__17.MoveNext 的 state0 单趟执行，读取 __4__this 法杖并排程 Time.time + currentBase + addPerTroll×min(scanCount,max)，没有 yield。CanActivate只比较nextActivationTime。ItemOfPower.CanCancel的真实已审52字节方法比较 remaining>base；取消判断须在自己的读取窗口消费同倍率基础值，保持原生附加项窗口，不用原值阈值额外挤压。ItemOfPower.CooldownRemaining虚槽13在OnCooldownStarted回调中读取正在借用的base，第二次结果传 SignalCooldownOver虚槽22，等待参数由原生协程持有；只保证原生公式，不能无条件断言等于mB（还受附加项/两个Time读差值约束）。8字节非虚 get_ItemCooldown永不挂钩。

Rulers.ItemBasedRulerAbility.Activate 实际覆写slot21/native2644820，无baseActivate调用/无c4读/Signal工厂；普通已审Hermes虚路由不需要改通用RulerAbility缓存。c4缓存是实例Instantiate/SetActive后当前itemCD快照，非已证prefab原值；族外显式base调用和完整第三方订阅者闭包仍未验。不以地址唯一/interop公开形状冒充全AOT/detour/runtime证明。

实现：Android专用 PatchDivine_StaffCooldown.cs，保留原生体，只挂两个精确方法的Prefix+单Finalizer（MoveNext0、ItemOfPower.CanCancel0）。routine仅state0捕获Hermes owner，CanCancel只实际TryCastHermes；旧其它神器不介入。默认1/会话禁用零native字段读写/零Borrow。两个prefix共享一个字段Borrow/End，进入当次original，applied=original×m，写之前登记state；部分setter异常prefix内清账一次并消耗state，Finalizer不重复。唯一归还按current==applied的exactfloat条件一次写回original；不同值保留并告警、同值外部写来源不判；getter/setter/logging清理异常可见且不得覆盖原nativeException对象，无retry/待恢复表/永久原值镜像/SettingsChanged扫场/新driver/协程包装。无需坐骑Buffowner或静态栈：已审native回调走未挂的CooldownRemaining，没有已证MoveNext↔CanCancel嵌套；未知第三方回调内额外查询可能发生当次二乘（归还链仍闭合），如实待验，出现实机证据后再诊断责任窗口，不预建守卫。

UI/配置：共13entry，复用现存Steed冷却步进helper（小幅Android私有提取可接受，避免第二份数字步进）；1→.8→.6→.4→.2→1，中间值.5→.4，每次切换单Save。Player新增Staff base CD row410，Back488，Panel562；既有Speed/Stamina/Hold/Steed四行保持，Home562/World640/Generation250/Pop376/球48与72触摸命中、默认收起和原UGUI层复用，无新滚动/菜单框架。Probe只负责精确注册、版本/feature marker、日志/counts与入口；两个新descriptor包括父类型和nested名避免同名歧义，旧21形状保持→23。Worker负责功能源、config/menu/FloatLayout/project/tests/docs，Operator入口接线，Reviewer独立。

验证：实际Android SDK10 +当前interop/loader 构建0W0E；直接编译生产源的host真实行为模拟：30×.2+40=46（非14），另一current12×.6=7.2，nativeCancel阈值附加窗，默认/非Hermes/state-1零访问；partial setter/getter/cleanup throw/external different value/native exception identity/one attempt无retry。不能将host模拟视为native状态注入、实际技能/异常路径、第三方重入或联机验收。既有adapter默认/旧cfg/非法&中间值、13entry/Player562/23descriptor/shared-source冻结无deadhash等必要回归；PC与根Mono源码全部不改，冻结hash即可，不为零PC差异新增全套构建。

后续独立源码审查+GLM精确安装门；私有APK original22313 members保全/签名/native入口正常变化/单main、不含诊断插件或游戏/loader分发。原API35ARM64 MoltenVK AVD no clear/wipe 更新，完整prefs/native/UserData备份，首次启动前bytesame，23unique/旧21shape、旧12prefs；触屏全档/.8冷读回/恢复1冷启/无ERROR+既有Warning/最终停止。真实法杖激活仅正常流程自然可用时测试，不能为验收解锁/Invoke/编辑字段/造fixture；否则基础CD/取消窗口/附加项/池/手机/联机等保留待验。

收尾：源码审查/设备claims/精确delivery→scoped源码commit/PR base release/v9.5.13 attach→正常merge/代码Issue闭合/normalcanonical packet-reviewed markdone/doctor0errors0warnings，完整其它151对象/order保全；真实玩法另列。不发布APK/loader/game/tag/release，不PC/手机部署，不改已付存档或peer101来源。后续继续依赖块，不逐项等人类授权。
