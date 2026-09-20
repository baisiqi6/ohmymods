<!-- release-945-20260917 -->
2026-09-17 用户授权发布并沉淀累计版本规则，v9.4.5现为GitHub Latest。VERSIONING.md记录修复patch+1～5/功能minor+1～3、按完整条目去重，本批自公开9.0累计+0.4.0功能/+0.0.5修订。源码676f7204父公开21f7ffa，仅推新tag，原HEAD/index/branch保持。ZIP8961fd17 / DLL0f8c1fc8，582选集、精确commit实际2.4构建0W0E、99项目(76run/18compile/5xUnit434)通过。3725方法保持，仅Init版本/build文字变化；751type/4070field/3726method元数据及8PNG核对，314包项含VERSIONING、306运行依赖逐字节同9.0、CRC/隐私/独立审核与远端digest/Latest/tag读回通过。已闭游戏备份同步正式包DLL至E独立副本，28份用户存档/附加档/配置hash保持，未启动游戏。包括独立枪架/.9/五帧红焰/弹药长按/白天补货/伤害可靠性/4火枪后排与白天猎鹿。实机队形/鹿命中回防/密集战斗/跨岛与相关联机仍待，历史英雄步态/穿地/弩手缩放未称修复，功能doing状态保留。
<!-- release-945-20260917 -->

<!-- musketeer-banner-deer-20260917 -->
2026-09-17 本机已闭游戏备份安装E9D4971E/build9.0.0-musketeer-banner-deer-20260917，28份用户数据hash保持，未启动游戏。玩家举旗原4重步+4弓/0..4船保持，单一数组owner补最多4已购火枪后排，原生入队/退出/关闭回收，临时类型dirty门、部分失败及seatless actor+同life lease回执已补齐。白天只猎普通Deer，兔/小动物不伤不挡、坐骑排除、原生掉钱与伤害2/射速/射程保持；Greek鹿y.55越顶按实际body确定单次直线瞄准、地面裁剪、即时身份及射手同life终检，敌弹语义保持。被动鹿日志每world最多8条，无新全场扫描/存档schema。OMP Flash max file-only两slice；279回归(99+41+22+91+26)、两个actual2.4接口/完整build0W0E、独立review和DLL审计通过，3627旧方法保持/8PNG全保持，唯一新增Archer.TryRecruit长入口已核。含五帧红焰/.9/白天补货所有旧修复；不commit/push/publish，公开9.0.0不变。首次测试先收旗再举旗；实际队形/鹿命中掉钱/猎后回防与换岛重载仍待实机，doing不等于玩法验收完成。
<!-- musketeer-banner-deer-20260917 -->

<!-- musketeer-red-muzzle-20260917 -->
2026-09-17 侧边转交红色枪口喷焰，用户继而要求四五帧预览。最终5帧各50ms，Fire总.25秒保持，射速/装填/弹丸/伤害不改。现有可编辑像素source派生：root draw_preview.py只替换Fire口部红焰，worker负责67帧表/anchor/既有测试；备用worker生成器未使用。atlas尺寸672x192/56x32/PPU32/pivot31,2/.9保持，Fire36..40，Reload41/Lower53/Retreat59；new0..35及new41..66映射old40..65逐像素保持，身体枪手脚及x55空边保护，旧十字移除无叠层。预览候选bca6aaeb / build=9.0.0-musketeer-red-muzzle-20260917；79runtime、actual2.4接口/完整build0W0E、67anchor/9clip与像素保护及独立review通过；仅Atlas资源变化，其他7PNG和daylight/伤害功能保持。用户后续采用预览后，已闭游戏备份安装，28份原生档/附加档/配置hash保持。未启动游戏、改存档配置、commit/push/publish，公开9.0.0保持。五帧红焰.gif为素材预览非游戏录像，实机观感/低帧率采样待验，任务doing。
<!-- musketeer-red-muzzle-20260917 -->

<!-- restock-daylight-20260917 -->
2026-09-17 自动补货统一仅原生Kingdom.isDaytime白天生效，覆盖全部9类；夜间停止规划/借人/动画币/自动扣款，未付订单撤回释放，已付只收尾不退款/二次；保留已扣款不确定目标故障回执，天亮在原cadence重算缺口。调度/订单/native回调边界和TrySpendForAutoRestock最终commit前复核day，手动购买/普通银行/收税保持。面板显示白天补货及等待天亮，原Greek-only/host/双倍价/阈值库存门保持，无新增hook/扫描/配置项。候选96566def / build=9.0.0-restock-daylight-20260917；220针对回归、actual2.4完整build0W0E、方法/8PNG审计和独立复核通过。已闭游戏备份安装既定E独立副本，28份原生档/附加档/配置hash保持，未启动游戏。保留火枪0.9、伤害可靠性及其余功能；未commit/push/publish，公开9.0.0不变。真实跨昼夜撤单/日出补货/余额与相关主客机仍待实机，doing不等于玩法验收完成。
<!-- restock-daylight-20260917 -->

<!-- musketeer-scale-20260917 -->
2026-09-17 用户要求火枪手当前外观缩为0.9：共用AppearanceScale，仅自有sprite child绝对(.9,.9,1)、枪口localXY同系数，脚点/朝向保持；不改actor/物理/速度/动画时钟/伤害/射程/节奏/全局缩放/商店工具弹丸大小/持久化，8PNG与上一combat-reliability保持。候选63ea50ad / build=9.0.0-musketeer-scale-20260917；79runtime回归、actual2.4接口/完整build0W0E、范围审计和独立复核通过。已闭游戏备份安装既定E独立副本，28份原生档/附加档/配置hash保持，未启动游戏。本次19:34启动日志23名火枪loaded exact/bound23且saved23，marker注册正常、未见伤害故障日志，但不能当逐弹/高密度/帧耗时验收。Player另有城堡盾牌店InvalidNetID11次、2名Archer穿地被引擎搬回、英雄快速Stand/Walk，根因与具体职业未确认；仅记录，不把0.9当修复。未commit/push/publish，公开9.0.0不变，0.9观感/枪口实机待验收，保持doing。
<!-- musketeer-scale-20260917 -->

<!-- combat-reliability-20260917 -->
2026-09-17 伤害可靠性候选：火枪弹丸与Greek/英雄额外AoE共用同步CombatDamage→原生ReceiveDamage，保留原生护盾/无敌/事件，异常不重试且隔离后续目标。弹丸32→256数组饱和改完整List最近有效命中，记录池化/long租约/重入门/回调world门/完整dt射程寿命扫掠；保持伤害2与原射速射程。AoE无Update自有marker真实GO生命周期区分新life，缺证据不猜；64有界快照提交前完整复核，提交32上限与.25半径/额外1/直击排除保持。marker不写hideFlags、不实现保存接口，非Unity助手HideFromIl2Cpp，注册仅一次避免故障热循环。作者FX同time共享72次noise（满16效果原3456），ties-even量化不变、未变顶点不上传、固定bounds；8PNG保持。候选284b78a0 / build=9.0.0-combat-reliability-20260917；392针对检查、actual2.4接口/完整构建0W0E、方法资源审计与独立复核通过。已闭游戏备份安装既定E独立副本，28份原生档/附加档/配置hash保持，未启动游戏。未commit/push/publish，公开9.0.0不变。真实marker消息/List AOT/血量护盾/密集战斗帧耗时与相关联机仍待实机，不能声称零开销/零漏伤。原生箭直伤、商店/长按弹药购买等保持；不涉及此前blocked英雄/弩手诊断，旧动作/位置异常未称修复。
<!-- combat-reliability-20260917 -->

<!-- hold-purchase-ammo-20260917 -->
2026-09-17 现有所有世界长按连续购买开关加入投石车5币火药桶与希腊火塔2币弹药。只识别PayableWorkshopBarrel或同GO活动FireTower精确拥有的PayableComponent，火塔AI disabled不误排合法客机。弹药会话捕获owner并在加速/续买/等待回执/PerformPay/Tick复核，owner替换结束旧hold；复用原生扣款/容量/设施就绪/钱包/距离/暂停及PerformPay成功回执，客机本地Completed不视成功。默认off和首次正常/.6秒后加速节奏保持，不新增setting、hook、全场扫描或弹药库存写；只同步原面板/配置帮助文案。候选0b1ad4e1，build=9.0.0-hold-purchase-ammo-20260917。针对行为回归、actual2.4接口及完整构建、方法资源审计和独立复核通过；8PNG和侧枪架等其他玩法保持。已闭游戏备份安装既定E独立副本，28份原生档/附加档/配置hash保持；未启动游戏。未commit/push/publish，公开9.0.0不变；真正连续购桶/满塔停止和主客机回执仍待实机，不把模拟测试当实测。
<!-- hold-purchase-ammo-20260917 -->

<!-- musketeer-side-rack-20260917 -->
2026-09-17 火铳铺独立侧架候选：保留西式店与枪匠四帧原像素，右侧空木板由imagegen生成并按已授权本地裁切近邻缩放拼接；176x80四帧、pivot64,2、PPU32，板36x32，真枪x2.8125/y.25/.5/.75。实际2.4居民/工具碰撞资产发现旧预览最高枪不可达，已压低，未改碰撞体。占地center+.75/half2.75，付款仍店中央；整数stockSlot分配，无效重复拒绝收费；仅同世界完整身份就绪、playing、非保存且未领取/未友敌认领的确证架枪一次重锚，掉枪-1不搬，重摆维护复用既有名册0.5s；日常购买检查最多3架枪且不读取全体Unit原生状态，不新增全场扫描或因无关历史Unit未知锁手动商店。候选6e2d89f1，build=9.0.0-musketeer-side-rack-20260917。代码/针对回归、actual2.4构建、资源/方法审计和独立复核通过。仅侧架3生产文件与build标记/商店PNG变更，其余7PNG及其他玩法保持。已闭游戏备份同步既定E独立副本，28份原生档/附加档/配置hash保持；未启动游戏。未commit/push/publish，公开9.0.0不变；实际最高层领取、多枪同时接触、旧档重锚/美术观感待实机，保持doing。不涉及此前blockedHero/Crossbow诊断。
<!-- musketeer-side-rack-20260917 -->

<!-- release-900-20260917 -->
2026-09-17 用户授权新版9.0，正式发布v9.0.0为Latest。源码21f7ffa，父公开v8 dd5c86f，仅推新tag；原分支/HEAD/index保持。ZIP43702cb6 / DLL641e79a9，精确提交实际2.4构建0W0E、91项目(72run/4xUnit393/393/15compile)通过。3587方法保持，仅Init版本文字变化、8PNG保持；313包项/306运行依赖逐字节同8、CRC/隐私/独立审核及远端digest/Latest/tag读回通过。已闭游戏备份同步E独立副本9.0正式DLL，28份存档/附加档/配置hash保持，未启动游戏。已知英雄走跑抖动/乞丐穿地/弩手缩放均未修，人口仅诊断；18火枪手静态保存核对不是实机重载，火枪完整身份/跨岛/联机等边界保持待验，相关功能任务不置done。
<!-- release-900-20260917 -->

<!-- runtime-anomalies-20260917 -->
2026-09-17 按用户要求定位英雄走跑抖动、乞丐穿地和弩手漂移。实际2.4动画阈值Speed1且hero walk .975/run2.4，但没有切态速度现场，根因未确认；弩手单次巡检不能排除原生Mover重写y1的暂态，诊断无Greek门有误报风险，尚未修。英雄/弩手分支被自动安全审查拒绝Potentially unintended activity，未重试/转派；英雄未完成新增诊断已归档撤回，弩手未改。人口真实资产17↔0碰撞有效、友军ignore10/17不含地面0，未确认穿地源头；仅新增现0.5s维护内事件只读日志，三类各12条/上下文，无物理/位置/人数改动。已闭游戏备份安装人口诊断候选135825e1（8.0.0-population-ground-diagnostics-20260917），28份存档/附加档/配置hash保持。17诊断/103英雄回退/37骑士与Dropinterop回归、actual2.4全构建与DLL审计通过；8PNG、全部Hero/Crossbow方法保持，无新Harmony hook。未启动/提交/发布。三项异常均未宣称修复；下一轮日志用于穿地定位，其他两项修改受自动审查阻断。
<!-- runtime-anomalies-20260917 -->

<!-- hero-center-grip-20260917 -->
2026-09-17 英雄持弓纠正：下垂手臂握中央弓把、腿侧横持；用户继而明确弓弦应在上，故整体翻转为弦上弧下且握点不移。0..21改手/弓，原人体不重画但允许武器前景遮挡腰/大腿上缘；头/步态/双围巾保持，23..29战斗保持，22/30中位按连续性核验。已闭游戏备份安装069527b6到既定E盘，28份存档/附加档/配置hash保持。素材几何/区域保护核验、2.4构建和DLL资源审计通过，仅HeroArcherAtlas和build文字变化。未启动/提交/发布，实机观感待验。既有火枪HUD/补货保持；原生Walk/Run频繁归零仍待定位，未因换图宣称解决。
<!-- hero-center-grip-20260917 -->

<!-- musketeer-save-check-20260917 -->
2026-09-17 15:08后只读核对13:44结束的旧musketeer-20260916会话：最终18条火枪手/18绑定，生产指纹匹配当前原生岛，18GUID/nativeId唯一且原生各一次，保存已确认，下一次实际加载未验。当前15:06安装EC80保持，日志不能当新版实测。新增关注英雄8.556秒102次Walk/Run且相位归零，另6名Beggar穿地被引擎拉回、1次弩手缩放漂移；DropItem/骑士旧问题已有后续代码修订但新版仍待测。只读无游戏/用户档写入，见tasks/musketeer-save-check-20260917/findings.md。
<!-- musketeer-save-check-20260917 -->

<!-- musketeer-hud-restock-20260917 -->
2026-09-17 火枪手加入职业HUD与阈值补货：HUD单独计已确认火枪手并从普通弓手剥离，补货role8保留旧索引，独立默认off/目标15/范围1–200；沿现希腊单机金库与助手队列，手动4/自动8，活体+可用已买枪+在途订单覆盖目标。自定义铺独立自动入口，保留手动玩家凭据与库存/世界/暂停门；未知身份不作0消费，确定回池损失仅会话证据，旧unbound跨读档仍可能阻断。用户追加英雄日常持弓再改为贴身近竖拿，保留缩头与22..30战斗/中位。已闭游戏备份安装ec80bf3c到既定E盘，28份存档/附加档/配置hash保持。针对回归、actual2.4构建、方法/资源审计和本轮独立复核通过；仅英雄持弓图集授权改动，其余七PNG与既有其他功能保持。未启动/提交/发布，公开8.0.0不变。实机HUD/补货/余额/拾枪/竖拿观感仍待验，旧火枪fullarchive终审缺口保持。
<!-- musketeer-hud-restock-20260917 -->

<!-- hero-relaxed-carry-20260917 -->
2026-09-17 英雄放松携弓/小幅缩头：基于当前58362d47最终31槽图集做局部像素编辑，站走跑改放低携弓，全动作头部同步稍收窄缩短，颈部围巾/脚点/腿步态/斜向上战斗弓/整体0.9保持。运行时原生动作采样无需改动；只允许HeroArcherAtlas.png与build文字改变。已闭游戏备份安装候选2c7fe54f到既定E盘，28份存档/附加档/配置hash保持。像素检查、针对回归、完整2.4构建、DLL/资源审计与独立复核通过；未启动游戏/提交/发布。观感和实机衔接仍待用户验收，任务doing。
<!-- hero-relaxed-carry-20260917 -->

<!-- musketeer-live-fixes-20260917 -->
2026-09-17 双商店/火铳实机反馈候选已闭游戏备份安装正确E盘：58362d47 / build=8.0.0-musketeer-live-fixes-20260917。购买移除逐次LoadAll，付费前校验本世界原生Bow池缓存并增加有界分段耗时；333ms卡顿贡献仍待实测。撤Character.DropItem Nullable桥接，复用Droppable.Drop显式source。火铳职业子集均衡，转职/读档完成后合并一次分配，原生fresh列表避普通单位depth，支持无墙回退，延期事件绑定world。骑士身份改稳定context/epoch与窄时钟归一化，精确legacy匹配保留GUID/类型；失配保历史不重种，失败load不提交binding，真实新生成另建epoch，升级输出先回读校验。不能宣称恢复无法匹配的旧类型。双店为奇幻弓匠/西式枪匠及局部待机帧，地面与暂停保留；只有双店PNG更换。代码回归、实际2.4构建、授权DLL/资源审计和新增切片独立复核通过；安装前后存档/附加档/配置hash保持。未启动游戏/提交/发布，公开8.0.0不变。真实购买卡顿、2/2守位、掉枪、店主观感和骑士重进仍待验收；旧火铳完整身份终审及跨岛/联机缺口保持。
<!-- musketeer-live-fixes-20260917 -->

<!-- musketeer-20260916 -->
2026-09-16 用户明确要求继续安装后，已在确认游戏关闭时备份并安装50aa3037到正确E盘独立测试副本，build=8.0.0-musketeer-20260916。新旧DLL与备份hash核对通过，26份原生存档/附加档/配置hash保持，未启动游戏/提交/发布。身份最终复审缺口仍如实保留，不视为审查通过；运行时/商店复核与既有回归证据沿用精确候选。所有世界单机F5弓箭页“火铳铺”默认关闭，4币购买枪、居民拾取转职。实机玩法、跨岛身份运输及联机仍未完成，任务保持doing。见tasks/musketeer-20260916/receipts/install.json。
<!-- musketeer-20260916 -->

2026-09-15 正式v8.0.0已发布Latest，tag/source dd5c86f，ZIP 1095ceb2 / DLL 80522bf1。精确提交clean canonical构建0W0E，修正发布工程缺firstpass Input的引用偏差，2675方法与此前8.0候选全同/两PNG保持；68项目重跑通过（56run/4xUnit392/392/8compile）。正式313项/306runtime逐字节匹配7.6.5、CRC/独立审核/远端digest/Latest与tag读回通过。用户明确要求发布后已闭游戏备份同步E独立副本8.0正式DLL，原生存档/个人cfg/ModSave附加文件hash全保持，未启动游戏；此前本机7DD候选已含最新玩法，只版本标记未统一。旧版本与master不改，英雄/围巾/联机等待实机项仍doing。

发布与本机正式版本已一致；测试入口仍为E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns.exe，不能启动旧副本。未替用户打开游戏，下一步根据用户实测反馈读取日志，不擅自回退存档或在运行时换DLL。公开地址见 tasks/release-800-20260915/publication.md。
