# Issue #232 G1 — 职业共享源码与生命周期接入

本阶段基准 `6fbf46c91590fd96f20022ca8a742535fdcaad37`，独立分支 `codex/android-profession-archery`。保留 Android 0.0.30 已安装成果；这是源码接入阶段，未替换 Main/APK、未启用新付费职业，不把本阶段当作整个 #232 完成。G0/PR233 与共享保存未发布观察根因修复 #235/PR236 已合并；#235 正常受管完成，不能继续把旧“待合并”段落当现行状态。

## 责任与复用

产品显式输入由66源变为173源，真实解析184个引用、嵌入21项原资源。复用共享职业身份、保存、人口、角色/工具/投射物与视觉源码；原 Android PopulationCounts 重复实现删除，人口只消费共享13职业计数与原骑士读数，沿现有中文、滚动控件与小浮球展示。CompileInventory 不再将已有真实人口源误列为平台替代。不是把源码数量换算成移植率。

原 MelonPreferences 初始化增加9项真实条目（共40）：三个职业开关默认OFF、弩手四档比例、散射/射速/冲击开关默认OFF、箭数1..3与射速1..2；复用 CrossbowRatioPolicy 与已有加载归一化/回调/单次Save机制。未为尚未完成的职业增加玩家启用控件。

AndroidProfessionHooks 按已审PC TypeDef顺序显式解析52个G1 owner，完整解析后再逐个ClassProcessor接线，保留原attributes、__state、priority和真实producer；不PatchAll、扫描程序集或按native target去重。Probe接真实CombatTargetLife初始化、PrepareWindowObserver、身份/商店/弓弩Update和视觉LateUpdate生命周期；退出只移除所拥有的观察入口和人口只读缓存，不清付费身份、未知writer或task lease。实际原生安装仍未执行。

G1选源不包含普通希腊火像素视觉 PatchArcher_Impact；一次误接其Tick的真实CS0234已保留并撤回该G2接线。后续弓箭命中/视觉完整组再复用真实模块，不补空Tick。原共享PrepareWindowObserver警告通过真实赋值消除，没有压警告。

## 类型绑定根因修复

共享 PatchRoles_Worker.cs 首次进入Android产品后，同namespace真实ScaleRegistryHolder遮蔽了旧文件级同名using alias。实际IL证明旧四处Register及Hermit OnDestroy的Unregister都转入Holder；其Register会进入额外组件创建路径，不能因内部又转发GreekScaleScope便称等价。原四条直接调用断言真实失败；追加第五条永久解绑断言后旧DLL为1259/5。

Main明确交接唯一文件 PatchRoles_ScaleAdjustments.cs 的alias与原五调用；正常plan revise/approve后才落入W。唯一RoleScaleRegistry别名在ANDROID映GreekScaleScope、PC映历史真实Holder。G0前父提交及Android早期适配前的PC源码证明PC一直调用真实Holder。本修复只消除命名产生处的歧义，没有新驱动、扫描、重试或下游纠正器。新别名在真实编译输入中无类型重名；永久五callee断言覆盖未来再次误绑定，无另添重复absence测试。

原共享文件SHA `1ddc29f77b1dacaff0c4ea7df6d3274925932c52b3c910ec4106d572805a80ec` → `2f58b328ad19217687e8859104af3da7c0c3be835618f6e31814f1c749ae68ca`。FrozenSources保留此文件，仅更新这一条实际hash，其余74条旧pin逐字保持；没有全表重pin或共享树排除。

## 验证分层

| 层面 | 实际结果与限制 |
|---|---|
| 最终工作树Android产品 | SDK10.0.401，Debug/Release均0W0E；实际MSBuild回读173 Compile/184 ReferencePath/21资源。Release SHA `8f89c60ce5e68ec06aadc6b26df9df5427c715b768d580ba712e0e195027bbbc`，1939456 bytes，未安装 |
| 最终工作树host | Adapter1264/0，包含原四Register与第五Unregister、真实Holder/原FarmCats边界；Population106/0，两个实际项目编译均0W0E |
| 配置迁移 | 20个独立进程case通过：原31完整契约/旧cfg、新9项默认与已有值、比例/箭数/射速/非有限值、重复Initialize和原Money回调。后续唯一第五metadata断言及单源hash更新未改变配置代码；不宣称20case已全部换新binary重跑 |
| PC共享源保持 | 同SDK、同真实252源/161显式refs/37资源，net8与net6分别fresh before/after0W0E，仅alias文件Compile输入替换。现成strict comparer均MATCH，type/member/attrs/IL/resources差异0、allowdiff0；五callee前后仍真实Holder，不代替Windows运行 |
| R候选与最终W | 独立候选完整Adapter1264/0；最终W与该候选再次strict比较MATCH，零type/member/attrs/IL/resource差异，不声称PE/MVID相同 |
| 旧staff host | fresh HEAD 6fb 基线与候选均0W/1E，唯一 MobilePlayerMenu.cs(22,31) CS0246 MobileModPanel完全相同；只证明该可见阻塞原已存在，不记staff通过。未补fake panel或扩修旧UI闭包 |
| 审查 | 产品source、配置测试、exact计划及alias增量独立审查已完成，原P2在精确候选闭合；最终W/sourcepin回归单独复核。实际GLM5.3/max决策模型证据保存，错误的PC历史假设已凭git原文明确撤回 |
| runtime/ABI | 具名64 native target的静态唯一地址不证明Harmony ABI、Task/Return布局、Action回调/GC、实际writer或保存事务。memory-only诊断已准备，前次AVD在addon安装/游戏启动前guest shell阻塞而停止，未产生运行结果；#213仍OPEN |

正常canonical修订计划SHA `14fcaa6fee9e330334e072b5f5758a8b7f27a9dab56041f8781234bc2c9427da`，doing/running、phase ready与原assignment/lease保持。173其它rows/order、157其它plans及外方lease、本机checklist保持，validator0、doctor0E0W；独占窗口已归还Main。原始命令、私有路径、输入/输出pins、红绿日志与回执在本机 `.local/tasks/android-profession-archery-20261014`，不提交游戏、原始玩家日志或设备资料。

## 后续依赖与余量

先补真实native Task/Return与保存边界证据，再分组启用：普通弓弩cadence/scatter/impact与完整弹丸/特效池；火铳身份/商店/枪弹视觉；英雄与盾卫的运行时、商店和跨世界池。Paid-life、原生保存、对象池复用和完整中文触屏控件随所属依赖组一起验证。Main #234 的盾卫修订及 PR226库存归其所有，未合并的Draft不导入；闭合后按真实hunk/schema/owner重核。银行/自动补货与额外角色联动保持单列，不扩进本G1。

本轮减少了重复人口实现、配置接线及共享编译缺口，但原生保存、跨世界池与完整启用仍是主要成本，不能按173源或零编译错误下调成接近完成。当前职业/弓弩剩余按3个完整启用组加跨组原生/保存/池验证，条件宽估约40–100工程小时；全Android余量暂维持既有约6–8依赖风险组、70–200工程小时。若实际ABI或模拟器阻塞需改方案再重估；手机/平板、多人和长期自然玩法的验收日历另列，这不是工期承诺。

本G1部分PR不使用关闭整个#232的完成关系。全部范围交齐、必要审查验证和对应PR全合并后才正常关单；源码、host、安装与玩家验收分别记录。内部源码前置不新增对外完整功能条目，依据VERSIONING暂不另计版本，不发布游戏包。

## 2026-10-15 接续：合并基线及真实返回值内存验证

主 Operator 的相关合并与登记收尾已完成，Android 分支接续到 `ee153aca3f2b70aee5f73de46af4362b08e332b8`（审树 `f3ce2fe9eeac6eaa92dddf779c8f05df82493fc5`）；G1 源码 PR238 已合并。新增公共差异按真实输入核对：从173到183 Compile，184 ReferencePath与21资源保持，新增2个性能、3个库存/照明、5个盾卫共享 helper，52个G1 owner及4+1缩放callee不变。Debug/Release均0 errors/1 warning（Android未启用旗帜的 `_nextActivation` CS0169），两实际产物分别Adapter1264/0、Banner metadata15/0；未安装新版Android Main/APK。Main NativeKeyReader/原生旗帜及#233/#236边界保留，未借合并自动启用。

私有原生诊断已在原API35 ARM64独立AVD的一次新SwiftShader病例完成：真实 `Task.FromResult<SaveLoadResult>` 的IsCompleted/Result、`Routine.Return<SaveLoadResult>` 构造值与修改后生产同源raw-reader，136/392/72共九项actual equal及 `ABI_MEMORY_RESULT done`，scene main、managed callback thread1、静态roots6。非作者独立结果核对通过。原0.0.30 Main、31项配置及Release路径/字节值保持；诊断Addon已移除，game0及owned QEMU退出有独立证据。本次cfg/Release没有差异，未触发恢复push分支；恢复逻辑的该分支不由本病例签字。它仅更新此前三值Task/Return内存与AOT未知项，不证明实际writer/原saver callback/协程copy-back、GC或线程安全、Harmony接入、保存事务、池重建、跨biome攻防、手机多人或图形通过。

前两次失败完整保留：旧read的 `exec-out sh -c` 重复quote导致错误文本被当文件字节；改为原生 `shell -T` 后实际设备Main/cfg/四refs/完整备份读回通过。之后run对1.9GB APK复核只给3秒，而prepare同一完整校验实际10.51秒（30秒预算），在Addon push和game/native前超时；当时host/get-state仍正常，不能写成GPU/ADB挂死。仅修同类两处校验预算3→30，整轮75/85/90/95界限不变，无fallback或自动重复病例；原始错误与各次spent gate/PID/claim/backup保留。后续正确病例实际九值通过，不无痕改写旧失败。

最新正常登记计划 `af8f8ca8adc3061823e2b6c7450d46fa08b907d65bc4d77eaf6ffdc4f37b9763`，原identity/operation/doing/ready/running保持，正常lease至17:34:34Z；177其他rows/order、161其他plans及本机checklist保持，validator0/doctor0E0W。正常登记本身不授native；单病例权来自其独立实际GLM5.3/max条件裁决与精确source/input核验。回执与原始数据仅在本机 `.local/tasks/android-profession-archery-20261014/g1-apk-budget-20261015` 及相应private case，不提交APK/原始玩家或设备数据。

下一依赖组核对两个真实私有保存协程对象的Return副本写回与 `Action<SaveLoadResult>` typed native bridge；仍复用同源raw-reader与既有测试机制。真实writer/交易和完整角色、工具、投射物、技能/死亡/撤退特效、召唤物、Holder/sync池及InitPools重建后重新注册继续按真实依赖核对。Main相关未合并草稿的旧边界只代表此前时点，现已闭合的#226/#234等以本次真实handoff为准；不把它们再当待合并阻塞。#232全范围尚未交齐，保持开放，玩家实机状态独立记录。


### 2026-10-15 Android 职业/弓弩 G2 公共接线

保留原架构和已合并成果，唯一显式注册表由52基础入口扩为85（52基础+32战斗视觉+像素火矢1），按当前正常PC产物5fb9c5f8…实际TypeDef RID子序排列；原52相对顺序保持，全数组解析在任何classprocessor之前，同target不同priority/state的责任保留。旧52注册表删除，避免重复实现/注册。FireArrowInternal实际4个owner，含MusketeerFire First800。弩矢Init池重注册与转职/复用/防御/skin、Hero/Greek箭/火铳战斗视觉同行。直接链接现有PatchArcher_Impact.cs并在GreekImpact.Tick后驱动其Tick；真实类名并非PixelImpact，无新renderer或共享PC源码改动。未追加choice24、无据Holder/PoolFix强制重建或在线英雄扩权。

实际任务分支SDK10 net10 Debug/Release均0错误、1个既有Banner CS0169警告；184 Compile/184 ReferencePath/21资源；Adapter1264/0与Banner15/0各两配置通过。两个非作者源码审查无mustFix，实际W产物与R已审候选类型/成员/属性/IL/资源严格MATCH（不声称PE/MVID相同）。实际W Debug=e873d657abd182baebc7343ec76707c7c2cb570d52d92265aaba34c5fef911bf，Release=40bee8ebe48b28c8a4534403e126d2df7201ee23e71d87193e8a4b1bd3e56f62；source-only，不是安装/原生/玩法验收。

保存公共依赖另完成两组私有内存病例：原9点Task/Return，与新增27点两保存state.return值副本修改隔离/显式回写24点+真实typed enumAction三收值，分别独立溯源（27不包含Task9）；main场景下27全部actual equal+唯一done，callback计数1/2/3。原Main0.0.30、31键cfg、APK与Release完整读回保持，Addon移除、game0与ownedQEMU退出分别核证；未触发cfg/Release变更恢复push，不宣称该分支已测。生产writer/缓存/ref-out-state/Harmony/原回调/线程GC/保存事务/池和玩法仍未验。

离线资源盘点纠正旧推断：APK内22147个Unity entries、3118个Resources映射确实可读；5 selected条目含51个MonoBehaviour通用base及Script引用，custom serialized字段/完整依赖树尚未解析，不能说资源输入缺失，也不能拿模拟器黑块证明缺资源。4保存target当前APK匹配字节中的Task.Run/ContinueWith及Directory/FileStream call-site只属provisional离线证据，未证明实际线程/函数extent/detour。

#232完整源码范围尚未交齐，保持进行中；后续真实保存门/回调/物理事务、完整Holder/sync与工具/投射物/技能死亡撤退FX/召唤物/InitPools、换岛读档攻防、G2-linked-role24与触屏功能页按依赖继续。玩家实机单独记录，不为关单冒称完成。本批未安装Main/APK、未启用新的原生功能、未修改正式游戏/共享存档或Mono。单次私有过程、哈希及回执位于本机`.local/tasks/android-profession-archery-20261014/g1-savebridge-memory-20261015`与`g2-combat-source-20261015`。


### 2026-10-15 私有原生 writer ABI：成功写入与实际失败位分别核对

G2源码PR240已合并至851a495；当前184源码/184引用/21资源与85owner注册范围保持，仍未安装新版Main或启用新职业玩法。这一轮只临时安装私有诊断Addon，在原API35 ARM64独立AVD执行一次真实写入，原0.0.30 Main、31键cfg、Release、运行库与APK保留。诊断和case脚本的源审拒绝历史及修订完整保留；修复接口工厂exact-class误拒、失败终态未封、同步失败后续写、异常归因、复用旧文件oracle、typed参数、超时截止与结果解析/恢复边界，优先修产生处，未复制游戏writer逻辑或伪造Global/owner/Task/enum。静态strong roots保留managed wrapper，本例native对象可调用；不证明跨GC压力存活或pinning。

实际单病例结果分面：async真实SaveFileToDiskAsync返回Task，P4观测结果72（Save|Success），私有新文件80B/SHA e26bf241b7a94c339f38efe9292c2ce94ed57cef895f0928300b7197b16e5a4a；sync经真实factory返回IEnumerator、assignable核对后TryCast exact state，仅一次MoveNext=false、生产同源raw-return=72，另一新文件79B/SHA 53c988da6c0ce97c1d2b7fe9093b6337a52d6bc31faf04495d24a25b25edc66b。softstop后host原件、前后inventory/hash与日志逐面一致；没有整体native成功聚合。P4与terminal的Stopwatch为不同观察时点，不是Task精确耗时基准。

physical file-as-folder面真实返回648=Save8|Failure128|Subsystem512，不含Busy256，sentinel53B字节保持、未产生目标文件。原诊断仅认可136，故原件仍记录unexpected-result-648及唯一failure terminal；不改成原运行done，也不把它归为游戏存档bug。当前枚举与同原生库已选body的648 literal另作只读事实核对，不证明具体异常因果/执行分支，更不证明生产GateFail/Busy/owner lease/pairedbackup/Global回调。

原件恢复逐字readback、Mods恢复原清单、临时Addon移除、checked game0及owned QEMU reaped退出分别留证。cfg/Release本次与原字节一致，未实际触发变更恢复push；原始日志、私有文件树及失败记录保留，本次单用途gate已spent，不重跑。运行在模拟器而非手机验收，不外推线程/GC压力、detour/ref-out-state、保存事务、完整跨biome池或玩法。

正常计划ce4950a4e590dff72bfe530747b42b50070162453ba0b8da57e50491b8c8c5cb已typed revise/approve；同owner/session/operation与doing-ready-running保持，lease19:10:47Z至21:10:47Z，177其它rows/order、161其它plans、primary未拥有改动及本机checklist保持，validator0/doctor0E0W。独立source与actual-result审查分别记录；源检查、normal登记、运行恢复和玩家验收互不代持。完整#232源码/功能范围未交齐继续开放，手机/平板/多人/长期状态如实待验。

下一批按真实依赖准备合并后的85Main+受控40键prefs分组加载，先查实际Hook安装/原生状态与复用池，再分组启用普通弓弩及职业生命周期。原架构及共享源码保留；不因已知游戏原生失败附加位再添失败扫描、重试或强制池重建。单次过程、哈希与回执仅保存在本机`.local/tasks/android-profession-archery-20261014/g2-private-writer-runtime-20261015`，不发布APK/原始玩家或设备数据。


### 2026-10-15 85入口实际加载与成对变更恢复

保留原架构，在原API35 ARM64独立SwiftShader AVD仅执行一次无UI受控启动。实际加载已合并85入口Main40bee8…（2,033,664B）与受控40键cfgac31…；原31键875B前缀逐字节保留，6个职业/弓手开关false、弩兵比例25%、连射数量1、射速倍率1。源码确认比例0会被归一为25%，故不能称所有职业关闭；未改共享比例策略或伪造总开关。

本次日志重建后有当前epoch/stat/PID证据，85注册、禁止自动扫描、晚FarmCats注册、graphics与组件创建按严格顺序出现；观察23.9899s、supervisor36.6409s。这里只证明注册段及启动组件，未观察main场景，不证明每handler detour/ABI、9项配置运行值或实际战斗/转职/保存/池/玩家验收。source与host/source审查不能代持这些未验面。独立实际结果审核对1,832条原始命令与5,729文件台账；完整日志保留两个WARNING（Il2Cpp v0.7.3.0依赖与scene build index不可用），选定errorLines为空不等于无警告或未来版本兼容已证。

首次实际触发新版Main与cfg的变更回滚：四次install/restore写入均ledger+byte-readback完成，设备恢复原Main5460…（297,472B）/31键cfg9758…，Release原字节零写、Mods清单、四运行库、APK及checked game0分别核验。各写+readback约0.05–0.12s属此病例合并阶段时间，不是纯传输带宽/跨设备保证；不因它扩大3s/2s或45/85/90/95界限。

退出原记录仍保留ownedStop=false/Identity changed before signal：收尾期间QEMU的ps外观转为括号形式，助手未取得退出证明。后来独立只读ps确认own PID32354已消失、5582/5583无监听，作为分离的后验退出证据；未记录Root reaped，不把原错误改成原件成功，也不把它归为游戏问题。单用途gate已spent，原件、红例和备份保存，不重跑。

当前canonical正常计划951226…，原identity/operation/doing-ready-running与lease23:09:36保持，177其他rows/order、161其他plans/primary未拥有改动/localchecklist保护，validator0/doctor0E0W。Main短窗口已归还。源码阶段根因修计时/身份/未知Release覆盖/日志时序及夹具时钟问题，拒绝与作者deadline原件保留；完整232尚未交齐继续开放。下一组按真实依赖接入触屏职业/弓弩控件和实际scene/战斗/池生命周期验证；不发布APK或冒称玩家通过。
