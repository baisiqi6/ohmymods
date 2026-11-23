# #327 Android原生菜单不暂停

## 当前状态与验收边界

当前已安装基于用户原始FullDLC输入4b40…的一体化私有候选970d…，仍为Android2.4.0/code23485、唯一主Mod6d480…。输入profile与ELF目标唯一映射增量已独立source/host审查通过，15测试通过；真实打包、签名、资源及设备安装回读均有私有回执。基础游玩和浮球已触达，但新存档教程尚未完成，正在由用户操作；Root暂停设备输入。尚未在新候选上运行v1暂停采样或完成二次冷启动验收，#327保持开放。

接续条件：用户完成教程并交还输入后，正常保存退出、确认无PID、备份当前数据，再使用已审查的精确v1 addon做一次同PID菜单对照。菜单打开后应采到timeScale=0且scaled time不推进，继续后恢复；日志缺失、身份未知或异常均不补绿。通过后再检查无诊断包的重复菜单与冷启动。每次诊断完清除精确addon、保留主Mod和最新进度，不恢复旧快照。输入及fresh数据同时变化的结论不得冒充单handler因果、官方干净本体或完整Android功能验收。

本批正常交付打包工具增量、计划与真实边界，PR先Draft且仅关联#327，不自动关单。服务端record/assignment仍缺精确部署与有效入口，受管状态保留pending；不借GitHub交付宣称managed done。

工具源码提交269fc5c已push，草稿[PR #328](https://github.com/baisiqi6/ohmymods/pull/328)已创建并远端关联#327；发布前Root复跑15测试、checklist validator 0 warnings及diff检查通过。未合并、未关Issue，等待用户完成新候选教程后继续有界实测。

## 调查起点与历史证据

用户确认屏幕左上×打开原生继续/保存菜单后，居民/坐骑仍实际走动，非Mod关闭按钮，非仅水面/动画。调查起点私有APK94b41bea…、主Mod6d480d05…，Android2.4.0/code23485；#323已由PR326合并5ab6f663，联网启动通过，Ghost残留另#325。PC/Mono、联网状态、全局暂停强制写入均非本轮默认范围。

唯一计划在本路径。专项分支codex/android-native-pause-327复用已清洁且完成交付的专项工作树，旧323分支及全部产物保留。先核实际208Compile闭包和native：独立gpt-6.1-sol/high审计208/208 SHA匹配，没有Mod显式timeScale或Game.state写入，PC PanelFocus未编入Android。原生Menu协程保存时标，只有HasWorldAuth且无IsClientPresent才置0；菜单激活在条件门之后，菜单出现不证明暂停。恢复亦由协程负责。静态证据不能定此次根因。

下一步优先复用现有MelonLoader临时只读诊断插件，不改或替换主DLL，不重包安装，不修改时标/游戏状态/联网/设置。先在原生菜单开/关边界和下一帧/约1秒后采timeScale、scaled/unscaled时间、Game.state、Menu身份/可见性、HasWorldAuth/IsClientPresent/IsOnline/CaughtUp，必要时只读单个已知对象位置；不扫描世界。若无法安全采原生协程内写入点，先A/E/F分类并明确无法区分同帧写0再恢复，之后有依据再加精确观测，不盲hook全部Time函数。

诊断源/构建/运行材料沿项目.local/tasks/android-profession-archery-20261014/device-startup-20261122/native-pause-audit/。读取/报告不能伪称修复或实机通过。临时插件独立命名，host编译只写私有输出；审查/本次gate后，设备先最新保存/数据及Mod/config备份，核对实际APK/主DLL，加入唯一诊断addon，执行一次自然×→稳定1秒→继续，保存同段日志/必要画面，删除精确hash匹配addon后重启，恢复一主Mod。无手工覆盖用户配置或当前游戏进度；不清理旧证据。运行不支持此最小采样则如实停止，不临时扩大权限/重投。

依据首次偏离点决定修复责任，优先复用原生暂停/恢复；禁止每帧强制timeScale=0、全局网络伪造、重复暂停状态机或补偿链。源码根因修复前先最小诊断，后续实现由Worker、独立审查、必要host与设备验证，再正常Git交付。这里只读审计已完成，运行诊断及根因仍待，当前未修复。Coordinate仅沿既有file/record流程，不借旧operation；服务端缺精确部署/assignment入口时保持pending，不能伪done。

## 用户新增线索与验证顺序调整

用户随后指出当前仍在新手引导，未完成指引且昼夜时间也未前进。保留原“居民实际走动”的观察，但不能直接认定暂停bug或正常设计。优先在同一APK/主Mod/网络/配置条件下完成当前教程，再用同一原生×→继续对比单位位置与时钟；不为此先构建/安装诊断插件或强制暂停。教程结束后若暂停正常，仅记录阶段性行为，不加修复。仍异常才按已列最小只读字段采样，当前只读诊断实现与运行均未开始。

只读教程补充已核到：Director的playing/TimeStopper/ClockSpeedModifier可独立冻结昼夜，不写Unity timeScale；因此时钟不推进不是全局已暂停的证据。尚未证明本次教程触发这些门或跳过Menu暂停。MobileCalendar在Game.Menu缓存、整数小时显示亦不能代测。教程完成状态getter可能写Stats，未来只读采样不得额外调用。等待同包自然完成教程的行为对照，产品未改。

2026-11-23：用户确认已完成教程仍不暂停；Root在无输入的同一PID下观察原生菜单持续显示、NPC位置改变，教程不能作为唯一原因。恢复既定最小只读状态采样方向，未构建/部署任何插件。

## 教程后最小运行诊断

原生正常退出并确认无PID/文件稳定后，最新207文件全量backup逐SHA通过。临时v1只读addon（source291e3d12…、DLL5c1de2d0…）编译0W0E，独立gpt-6-astra/high source/host审查12项pin及Cecil IL通过；未加native hook、产品setter或配置写入。一次×→1秒→继续采到：菜单state4/shown/active为真，HasWorldAuth真、IsClientPresent假、IsOnline假，timeScale仍1；下一帧和1秒后scaled time继续递增。不能再只归因教程/纯unscaled动画/采样时的暂停资格门未满足，仍不能区分未写0与同帧覆盖。原生时间getter在opened首帧deltaTime曾0，这只是线索，不能单独证明写0。

v1已正常退出、删除精确hash匹配插件并重启核实际只有原主Mod；主DLL6d480…、用户配置SHA保持，没有恢复任何旧snapshot覆盖新进度。原始case日志/图片和cleanup回执在after-tutorial-20261123。

后续优先无hook只读定位setter实际目标（sampler-v2独立材料），Kimi方案R2已批准并明确缓存槽本身及代码入口的映射/对齐/归属门。Time.set_timeScale原生VA0x49efe00、body56B，缓存RVA0x5318ed0属于固定libil2cpp数据PT_LOAD4的BSS；未知归属即unknown停、不找替代来源、不写或主动resolve。新实现需独立source/host审查，尚未在设备运行。发生过场景重新加载/人物配色变化，当前已询问是否有人或工具并行操作；未确认前停止自动点击，不把它无据归罪用户或新插件。

sampler-v2 source/host独审gpt-6-astra/high已APPROVE（北京时间17:59派发）：25项pin、Cecil IL与冻结文件一致，17项真实共享RangeProof纯测试通过。新DLL dc3e7d51…尚未部署，缓存槽/目标所有原生读取门及TOCTOU边界保留。当前已恢复仅原主Mod；下一次设备输入/部署等待是否存在另一控制者的现场信息，不把等待当授权或假定无人操作。

## 2026-11-23 教程后入口改写定位（v2/v3）

用户明确确认当前无其他控制者，继续设备诊断。v2只读实际缓存目标为libunity.so+0x51f2bc，首16B中仅首指令被改：原始e80f1ffc→运行时ARM64直接B，其余12B相同；libil2cpp的setter wrapper入口不变。缓存槽仍经固定PT_LOAD4/BSS与映射门验证；这证明运行时拦截存在，尚不证明是哪个组件或其语义。

v3仅追加一次直接B的目标解码和dladdr归属，与同段映射只读元数据核对，不读取目的地代码/追下一跳。夜间OMP DeepSeek Flash/max Worker与独立source/host Reviewer完成，真实模型事件fallback=false；Kimi/max决策审查通过；共享纯测试30通过、host构建0W0E。唯一设备case实际目标归属libmod.so+0x13d30c（origin=verified_read、r-xp）。同一菜单打开段仍timeScale=1、HasWorldAuth=true、IsClientPresent=false，1秒内scaled time继续递增；正常继续亦记录。不能据模块名归罪APKVISION或Loader；两个APK ZIP均无libmod.so条目，来源仍需定位。

本次观察原生保存、退出确认Yes并等待无PID，删除精确SHA be93fc2f…的v3 addon；设备只保留未改主Mod6d480d05…，不恢复旧快照。当前游戏停止，不让未暂停世界空转。材料：既有native-pause-audit/branch-case-20261123/{run-result.json,case-melon.log,cleanup-receipt.json}，源/host材料sampler-v3。产品未修复；Git/服务端受管状态不伪done。

候选补充：原包assets/pp嵌套插件APK自带libmod.so，SHA9b0a2ed5…；FDE证实目标13d30c..13d534、552B。补齐40B尾部仅restore/RET，静态候选在所有已知路径保留传入s0，无固定1写入，不能直接定它为覆盖者。运行映像hash与实际调用仍未证。当前只读run-as本package被拒not debuggable，未更改权限或绕过；后续查候选实际producer与登记点。静态objdump曾机械输出上限窗口越过首RET，raw保留、采用内容截到FDE边界；后续严格先FDE限定stop-address、不先输出上限再裁剪。Worker关于“去B只读对照”的建议未采纳：诊断从未加hook，无开关；去B是mutation且不能按其建议排除libmod。

同一夜间DeepSeek/max只读Worker接续静态producer：当前94b41 APK的pp与libmod逐SHA同旧候选。第二函数FDE13d534..13daf8/1476B：flag21f4d8=0无前置谓词调用21f520(s0=1)，非0调用(s0=3)，再调用21f528原函数；登记点证明它是独立handler，目标生命周期/执行频率仍未知，不能命名Update。安装器8444B超过8192上限，只读命中窗口928B，没有读全文。可见固定1/3生产者候选，但runtime字节/flag/指针和实际执行未闭合；产品未改。下一候选是沿现有无hook采样框架加精确libmod身份/FDE字节指纹/已知BSS槽只读采样，方案审查中，不私自改B或追加时间钳制。

## v4设计与实现步骤（现有诊断阶段）

目标为运行候选身份/指针缺口，非产品修复。选择沿现有无hook采样器增量精确读取；未选盲改B/时标补偿或新全局追踪器，前者改变现象、后者扩大范围。设计检查与writing-plans沿本唯一计划，不另建spec/plan。用户既有自主推进授权继续有效，不逐项索权。

- [ ] Worker在私有sampler-v4派生必要源/host工具；新增共享RangeProof的case专用部分文件页BSS证明，先真实pure red→green；原CacheRead不削弱。Kimi三项修订落实：fileEnd21f490到220000内槽与匿名BSS区分；9×56 PHDR/完整数组门；匿名pointer owner未知不提前停止菜单边界。
- [ ] host0W0E/原测试+新增范围拒绝、实际Cecil/源与DLL pins；独立夜间DeepSeek/max Reviewer核真实源/IL/SHA，Root复核后才一次设备运行。
- [ ] 同94b41 APK/6d480主Mod/配置、最新stable backup；fresh START0.0.4，baseline有效后短时×→稳定1秒→继续；unknown即停止不重投。正常保存退出无PID，精确移除addon、配置/主保持与当前进度备份，不还原旧快照。

固定增量仅libmod模块path、PT_NOTE buildid、两FDE函数一次字节hash和已知flag/四槽metadata，不追callee/匿名code；cached_initial不冒充每sample重验。buildid/代码匹配仍非整文件hash或执行证明。原始runtime计划/结果与完整模型证据沿既有native-pause-audit，服务端record仍pending。

v4首版597bd55e…在预部署复核被hold：任务书e_phoff40未写进制，源码把它当decimal40；合成fixture也同误而90绿。真实568B ELF+9PHDR输入回归出现91PASS/1FAIL，修正明确0x40后92PASS/0FAIL，0W0E；旧红例保留、不当旧绿为验收。已补PHDR指针等式/checked绝对索引，重复owner检测不再不可达，删无消费者aliases。新唯一DLL5869686f…仍未部署，独立fresh DeepSeek/max source/host审查中。最新208文件存档backup逐SHA复用有效，不复制第三份；设备一度安全锁屏，用户已确认解锁保持亮屏。实测脚本已只编译检查、未运行：source批准需绑定同DLL SHA，一case不重放、同PID归属、opened遇unknown不继续UI，读取完成立即退出清理。

v4独立source/host审查APPROVE_SOURCE_ONLY：实际5869 DLL、64/64方法IL与独立构建一致，92 pure tests、真实fixture与冻结证据复核、既有CacheRead IL未改。一次设备启动在baseline即UNKNOWN：Unity51f2bc首字51730b16解码到0x7bf0000000，dladdr没有模块归属；未读该落点字节、未执行预定×→继续case，也不重投争取有利地址布局。可能存在近跳转跳板仅是假设，不能直接称匿名stub或追读。已关闭SDK弹窗以清理，观察原生保存退出/无PID，精确移除5869 addon，仅原6d480主Mod和配置SHA保持；不还原旧快照。planned诊断目标未达、产品未改、Issue开放。后续考虑已知SDK模块的原生加载器身份定位作为明确新诊断目标，保留B归属UNKNOWN，不猜读未知代码，需按同阶段方案审查后实现。

v5方案已由同一独立Kimi/max批准：已知libmod模块身份与当前B落点归属是独立目标；首次原生loader唯一模块查找+原完整image/槽读取门，缓存后fresh maps+dladdr已知函数交叉验证，不重枚举/重绑。B或slot归属未知非terminal且明示UNKNOWN，真实安全读取/身份失败仍terminal；必须pure测试该状态区分，不能overall假绿。v5仅私有增量、同Core/Main/config，无新hook/未知code读取；尚未实现/部署。

## 输入层对照优先（Kimi APPROVE）

新重核用户原始FullDLC为4b40edd3e7eefb6df43e6777d566bda366179b5e245dd8f20e87810b0b56732d/1877856794B，缺当前平板基包的pp/hh/cd；cd在当前明确声明host hh、plugin pp。两包libAPKVISION和av相同、FakeActivity在classes5/6，不能混用DEX序号；缺资产不证明无remote plugin或官方clean。v5私有实现已完成8507692c…/106pure tests/0W0E，未独审/未部署，保留无需等待运行。

优先直接用户原始上传包+现有Lemon/6d主Mod/同4B启动适配的整组输入实验，不先编辑SDK handler或加timeScale补偿。唯一计划仍本文件；过程材料在既有device-startup-20261122/issue-327-input-base/。先只读核manifest/cert/components/permissions/DEX/core/game资源；现有helper最小增补精确outer profile并维持旧ef575支持，同一patch目标唯一mapping，集中host tests和独立源码审查后才能真实打包。本阶段不安装，冻结实际candidate/hash/signature/资源/Main/v1兼容性后再审安装门；旧插件私有提取残留必须处理，若采用既有授权的备份后干净安装，则结论限定整组包/数据状态变化，不是单handler因果，也不恢复旧snapshot或宣称升级支持。

原始输入批次source已独立APPROVE_SOURCE_ONLY，15 tests；helper541528…/tests319361…冻结。真实unsigned bf142508…仅改file262c6f0四字节为01000014、核心8e4b…，其它entry CRC/size与manifest字节保持、旧signature移除。复用生产Patcher后实际候选970d050be9b58aa98f6cf90d6770d02d101c391793302f15cd6611fe2f85831f/1910098979B：production仅主6d480、runtime/interop与旧可玩包一致，22147game资源保持；pp/hh/cd和libmod载荷无、av与原始上传包同；签名bc5de…、zipCRC、16KiB对齐通过。candidate实际com.rawfury.kingdom2crowns/2.4.0/code23485/Application及20组件/12权限与原始包一致。仍未安装；已提交具体candidate与备份后干净卸载/安装/基础游玩+原v1菜单采样的独立Kimi关口，不能借source批准提前执行。

具体候选970d…经Kimi独立重hash/ZIP/core/Main/签名/对齐校验后APPROVE_INSTALL_AND_CONTROL_CASE。卸载前指定Pad型号/serial、无PID、现94b41/唯一6d主/noaddon与最新209备份全部逐SHA成立；单次干净卸旧后安装970d，首次启动前设备base SHA读回一致。没有恢复旧snapshot/私有SDK数据。生产payload首次自动部署6d主且日志1Mod、133 sharedowners，默认money/hold等false；渲染场景与基本移动、原生招募/弓锤购买/生火、砍树/建墙可操作，小浮球开关与calendar设置保存触达成功。曾出现可跳过的首次云档登录和DLC推广，已走本地跳过/关闭，无账户创建登录或支付。

当前新存档仍处day1/4h引导；已原生完成营火、两招募、弓/锤、砍树及额外一侧木墙，但未把它认作教程完成。未加v1、未测暂停；为避免触控定位反复，已请求用户补完剩余建造引导并停营地，Root停止屏幕输入，独立进行文档/Git收尾。用户回复后按既定门正常SaveQuit/noPID、最新backup，再精确v1采样；不能改TutorialCompleted/Stats/存档或拿教程期行为代验收。最新data/config与旧状态不同明确记录，calendar由新UI从false启用，不是恢复旧配置。
