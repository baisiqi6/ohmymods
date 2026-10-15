# Android 世界建筑点位批（草案，尚未注册/实现）

基线：remote release/v9.5.13 ea5aa89f0df103967b01ff6bc647b72a5e5b9199；Android Main0.0.28；#212已merged/closed/normal done，#213诊断PR216merged但根因未定，OPEN/todo/released。Primary 2026-10-11T13:40:26Z明确将下一组canonical窗口继续交Android Root，canonical168/10plan、本机119、根7未归属改动保护。用户明确自主继续，按依赖/风险合批、共享/原生/根因/无冗余、实机如实pending。

## 范围/真实依赖

同一world layout组：共享PatchWorld_TowerSpots.cs（1632行）+PatchWorld_WallSpots.cs（986行）及塔基已调用的SpecialTowerDuplicateCleanup.cs（495行）。使用真实Android namespace/interop、现有AndroidCoroutine、logger、PC原生Instantiate/GetAssetSwap/RegisterObject/SemiStatic/Payable/Persistent链，不重写几何/采样/回收/存档算法。DuplicateCleanup在塔基倍率<=1早退之前已有调用，必须完整包含其Kingdom.DistributeTowerArchers prefix事务作用域；不是只接滑块。#131/132当前GH CLOSED，仅历史线索，当前源码含背景墙排除及结构幅度钳制，按实际API/编译/必要body与测试确认，不重开/顺手改算法。

最小公共类型拆分：把PatchWorld_SpecialTowerRebuild.cs的SpecialTowerRebuildMarker类型/注释/constructor原文移动到单个il2cpp/SpecialTowerRebuildMarker.cs。PC原命名/类型/constructor/注册调用保持。Android只链接这类型与上述3共享文件，不链接SpecialTowerRebuild的拆建、付款、资产和其它hooks；Probe在本组启用注册前显式注入marker类型，使用现有ClassInjector机制，无新注册器/镜像。其余共享依赖以独立read recon为准；发现实质耦合先修正契约而非fakeCfg/Holder/角色。

Android真实配置：WorldLayoutEnabled=false（整组显式hook注册门，下次重进应用，UI不承诺即时撤销；与既有RoleScale一致），TowerSpotMultiplier=2（PC默认，范围1..4）、WallSpotMultiplier=1（PC默认1..4）、TowerLadderCleanup=false、WallSpotDiagnostics=false（真实持久化维护/诊断选择，非仅编译空面）。总25→30prefs；init不save、每真实UI变化一次Save、旧25不变；倍率非有限/范围clamp复用既有模式。TowerLadderCleanup共享已确认唯一writer自动false：ANDROID分支在实际true→false一次完成时调用ModConfig.Save一次，其余无shared算法修改；PC预处理IL保持。隐藏maintenance字段沿PC不新增普通玩家技术流，不写假entry或恒false代理。WallSpotDiagnostics在本组仅覆盖Tower源已有快照/退役日志；不带入PC独立PatchWorld_WallSpotDiagnostics的MoveBorders等hooks，不宣称整套PC诊断包接入。

公共UI在现有岛屿生成页复用Step/Toggle、PC暗金样式中文、小浮球/滚动；增加“建筑点位调整”开关（重进应用）与PC“箭塔基底密度”“墙基密度”，帮助沿PC重载/边界扩张语义，并注明单机；不固定大遮罩/GUI.Button/第二面板。

统一Probe显式3handlers到2native targets：World.OnLevelLoaded现有FarmCats handler保持，加共享Tower+Wall postfix；Kingdom.DistributeTowerArchers增加共享cleanup prefix；该target新增唯一1（51→52，roleON59）只在layoutEnabled冷启动true注册。World共用target只在全部handlers安装后报告一次。默认false不启动这组协程/扫描/订阅/删除/生成，也不注册cleanup prefix；toggle只更新下一次启动选择，当前已注册职责继续到退出，不添加热unpatch/重建/周期补偿。整组OFF只停止后续布局与清理，原生Persistent保存的已有点位继续存在，重进也不删除；要回到原生密度须保持整组ON、两倍率均1后沿原重载/边界路径退役可删未购KEM点，已建/支付/施工保护保持。UI帮助必须讲清此点，不承诺OFF重进即还原场景。Marker仅本组启用时注入。此注册条件须实际native安全核验后才能安装/游戏启用。

## 实现分工/验证

先独立source依赖审和实际GLM5.3/max三问通过冻结plan，再GH Issue与normal登记/plan approve/accept/独立branch-worktree；保护其它168对象相对顺序/10plans/local119，normal validator。Worker依周日北京时间OMP deepseek/deepseek-flash max，只共享TowerSpots必要ANDROID真实配置writer+本批测试；Root统一marker提取、config/menu/project/Probe注册。无Main/build.bat修改，worker不commit/push/设备/SSH。

最小native recon只2目标metadata精确owner/signature/pointer/collision/FDE/PT_LOAD以及实际interop消费API，静态不运行；若body对注册/持久化/cleanup teardown缺必要证据，另审定精确导出span总量不追callee/全库扫描。重复目标World早已注册不等于新handler正确，DistributeTowerArchers短地址若折叠必须停止注册方案。不得旧Mono2.1替代Android2.4事实。

必要验证：真实SDK10 Android完整构建0W0E；PC SDK8纯build禁BepInExPluginsPath及语义方法/37资源对比（marker原type/body相同，其余PC原IL保持）；真实30prefs old25保留/defaultOFF与启用/数值cycle/Save次数；typedmetadata精确3handlers与旧51/role7保持、强pins追加4共享源，不重复目标报告；本组几何/持久化/清理/事务边界复用已有测试或纯策略边界，不搭假游戏完整场景证明native。控制流testdoubles与真实native事实分别标注。rootcause/冗余审：保留已有各占用门的实际职责，不引入新场景扫描/重试/秒级守卫；若实际发现旧PCbug，修产生处且清旧补偿并单列范围。

安装/启用前必要审查与实际GLM关口。只独占API35 host/MoltenVK原AVD，fresh备份cfg/native/Main/Release，私有保data nativeproducer签名包，≤180s单窗口/必要少量输入、准确零PID退出；遇同hosthung停止设备臂，不扩大盲重试/软件GPU矩阵。记录真实off/on注册/菜单/值写回，当前自然可见塔/墙点位存在才能记录实例/买建/读档，缺样本pending。长期稳定、自然建造/回收/换岛/读档、phone/MP单列，不能构建绿代替。单机支持范围沿PC，online原跳过保持。

独审+GLM交付门后own commit/push/PR正常merge，Issue代码范围与normalreceipt完成后关闭；#213不顺带关闭，独立真实验收待项保留。只更新相关README/progress/task机制，私有APK/assets/log不上传。预计本批4–10工程小时；当前尚余约7–9依赖风险组/75–210工程小时（本批交付后才减去该组，不能预付完成），身份/存档/资产/联机仍高风险，天然/真机等待另计。估算随真实失败与依赖更新，不预付全部功能无新增bug保证。
