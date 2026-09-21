<!-- peer-operators-github-20260921 -->
Mac Codex 与 Windows ZCode 为平级 Operator；接收需求方负责推进汇报，各自调度执行工具。开工前查 GitHub Issue/PR、认领问题/基线/分支/修改范围，交付关联 PR，冲突先协调；Mac x64、Mac ARM64、Windows 验收分别记录。参见 `docs/project-harness/collaboration-protocol.md` 与 `docs/project-harness/current/peer-operator-coordination.md`。
<!-- /peer-operators-github-20260921 -->

<!-- identity-hardening-20260920 -->
2026-09-20 身份恢复可靠性加固A+B（用户批准）并闭游戏安装E盘，build=9.4.5-identity-hardening-20260920，DLL 4FB8613C，前一候选A1777BBB已备份，存档hash保持，未commit/push/publish。背景=玩家报告"进游戏/换岛MOD角色有时不出现、重进抽奖式"两轮诊断（tasks/identity-restore-instability-20260919）：头号嫌疑H4a吸收态（一次性sidecar写失败/失配会话拒写→原生自动保存推进→hash永不复中→该岛永久unresolved）、H2 musketeer读IoError静默不查备份（唯一真逐启动抽奖）、H1 v8→v9 legacy时钟漂移首启定局、H3竞态排除。A（knight+musketeer）：musketeer IoError也查备份+一次性告警（原零日志）、load-match补kind=exact|legacy、两store写失败仅IO类真实250ms重试一次、Missing分支补Info。B（仅knight，musketeer证据门不可支持且会悬空已付职业生涯故不做）：ApplyCapture锚点重排（在CanFlushSeed/Owners早退前）、Kind精确known-mismatch触发、严格子集证据门（当前盘骑士uniqueID⊆该context全部Epochs并集，从island.objects+RecordIsKnight枚举不依赖Owners）、新建epoch写kind2基线旧快照全留、同uniqueID历史收据全一致才携带否则丢弃走fresh、成功后RememberBinding+ConfirmContext(false)防重复建epoch、容量耗尽fail-closed。设计经对抗审查10必改（触发锚点错位/musketeer不可行/绑定未更新三硬伤被抓）。休息日OMP deepseek-flash max两轮（二轮修测试场景语义：再基线JSON与旧快照逐字节同致跨epoch多命中conflict，biome++前进内容修复），operator复跑七身份套件全绿（runtime45/0、archive42/0、stable16/0、load-seed34/0、integration9、network48/0、musketeer303）+主线构建0W0E。VERSIONING第七节+0.0.4累计9.5.12。已知边界：严格子集门活性上限（失败写入后至首次自愈保存前新招募骑士则该次自愈关闭）、musketeer RecoveredBackup永久只读、跨epoch同JSON多命中conflict。实机吸收态自愈/玩家档验证待做，doing。
<!-- identity-hardening-20260920 -->

<!-- banner-rowgap-20260919 -->
2026-09-19 修复举旗编队火枪手行与弓箭手之间空隙过大并闭游戏安装E盘，build=9.4.5-banner-rowgap-20260919（累积穿墙/0.65/商店认领锁候选），DLL A1777BBB，前一候选4F502247已备份，存档hash保持，未commit/push/publish。根因（对抗审查逐场景核算）：行槽Gap类型前置编队尾+行距取原生Gap值(.34375)+原生2个尾部Gap槽(.6875)+多船间距，火枪手↔弓手间隙1船4.7×/0船5.7×/2船13.9×弓手步。修法（审查否决原"删尾Gap+前置"方案后定稿中插）：行槽中插到第一个弓手槽前、槽型Squire（七实现者类型表均不含=零认领风险）、覆写spacing[Squire]=Archer间距(.21875)，边界恒1步（任意船数/占用/镜像），满员弓手/长矛坐标=基线，fleet块刚体平移-0.875，未满员按原生压实语义收紧贴弓手侧=用户"复用弓箭手队列逻辑"要求。休息日规则派OMP deepseek-flash max两轮（实现+boats=0测试镜像typo修复，生产零改），operator复跑unit42/0、e2e23/0、主线与interop构建0W0E。实机0/1/2船举旗目测/未满员收紧/收旗还原/换边镜像/联机待验，doing。
<!-- banner-rowgap-20260919 -->

<!-- shop-claimlock-20260918 -->
2026-09-18 上午修复火铳铺"平民拾取完成前买不了第二把枪"并闭游戏安装E盘，build=9.4.5-shop-claimlock-20260918（累积含前夜穿墙/0.65候选），DLL4F502247，前一候选2482F0D3已备份至任务receipts，存档hash保持，未commit/push/publish。根因（对抗审查确认，含原生侧证据Peasant.SetDroppableTarget直接赋值friendlyClaimer无超时）：认领→拾取窗口双重锁——RackCount遇claimed枪返回满容量3、Reconcile遇claimed置_rackLayoutReady=false，均使CanPurchase为假；次要窗口为新枪≤0.5s布放确认。修法三处最小协同集：①Reconcile claimed项跳过不置失败；②RackCount谓词改"unclaimed且未落架才fail-closed"，claimed枪按占位计数（架上最多3把含认领中，与HUD/补货口径统一）；③TryCreateGun成功后_nextRackLayoutAt=0下一帧立即布放。不修边界保持：enemyClaimer/pickedUp仍整体fail-closed、StockRestores门不动。派工按上午第一顺位但操作者死板外派zcode.cmd两次Model creation failed后明示改OMP zhipu-coding-plan/glm-5.3 max（模型事件核验），同日用户澄清路由边界并已写入协议：operator为ZCode时第一顺位由内置subagent直接满足，不再外派。测试：tests/musketeer-side-rack翻转2条旧断言+新增6条Check+8条接线检查，operator复跑PASS 60断言+35接线、主线构建0W0E。实机认领窗口连买/认领取消/敌人抢枪锁店/联机待验，doing。
<!-- shop-claimlock-20260918 -->

<!-- hero-arrow-pierce-20260918 -->
2026-09-18 夜间接替codex会话（用量超限中断）后完成英雄箭矢两项改动并闭游戏备份安装E盘，build=9.4.5-hero-arrow-pierce-20260918，候选DLL2482F0D3，正式9.4.5原DLL0F8C1FC8已备份至任务receipts，用户存档17文件hash前后D2FCA830保持，未启动游戏、未commit/push/publish，公开9.4.5不变。①英雄金箭显示缩为当前65%：自建Sprite PPU改32/0.65≈49.2308（世界宽0.609375），rect/纹理/pivot/transform/碰撞体/弹道全不动，池复用换回原生sprite天然还原。②英雄箭矢无视城墙碰撞：新文件HeroArcherWallPierce复用ArrowVisuals发射作用域，OnSpawn英雄分支外观写入成功后Apply（快照墙碰撞体逐对Physics2D.IgnoreCollision true，TTL5s+world/layer/scene换代、256对上限、单对异常隔离、fail-closed），ResetArrow前缀无条件Restore逐对归还false且从不重扫；零新增Harmony钩子/配置项/逐帧扫描，OnEnable时刻不读arrow.archer。夜间规则OMP deepseek-flash max三轮（实现/测试修复/artemis套件并入），operator复跑：主线+interop双构建0W0E、新套件gold89/missing13、旧artemis套件三模式153/19/19全ALL PASS（0.65取代PPU32契约已同步断言），独立内置review PASS WITH NOTES（3条minor留档：外观归还与穿墙归还不同步窗口、回执表格字节数未回填、注释级差异）。测试侧曾有两类假失败已修：Physics2D计数器箭/墙两侧语义混用；dotnet run -p:属性增量复用旧程序集（已加INFRA退出码2硬失败）。实机穿墙效果/0.65观感/trail粗细/密集齐射帧耗时/联机仍待验，doing不等于玩法验收完成。回执见tasks/hero-arrow-pierce-20260917/。
<!-- hero-arrow-pierce-20260918 -->

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

<!-- tax-collector-batch-20260915 -->
2026-09-15 税收助手连续收币候选已闭游戏备份安装正确E盘：084acec8 / build=8.0.0-tax-collector-batch-20260915。用户希望每趟约20枚。旧实际容量至少100，少量回家来自成熟快照暂时耗尽就立即收工。现每趟20枚，已有收获且未满时断流原地等4.2秒（3秒成熟+2次0.6秒扫描），deadline不被空扫描不断延长，续收后重置；空手无目标立即退出。场上零币CleanupNoCandidates与接链统一策略，保留认领清理门；第20枚回家后立即终止本帧扫币，不因携带计数归零再吃第21枚。只在希腊authority生效，补货租用/归还、演员替换、回池/失权/离场清等待；原入账事务不改，回家不重复入账。当前英雄保存恢复、动作、商店及4PNG保持。针对回归、实际2.4完整构建与独立复核通过，详情见本任务receipts；DLL方法审计限制银行助手与build标记，无新增Hook类型。全部原生存档/附加档/配置hash保持，未启动游戏/提交/发布。实际连续扔20枚、停扔等待回家、正常补货与暂停仍待游戏验证，公开8.0.0不变。
<!-- tax-collector-batch-20260915 -->

<!-- hero-save-restore-20260915 -->
2026-09-15 英雄购买读档恢复候选已闭游戏备份安装正确E盘：ad57eb75 / build=8.0.0-hero-save-restore-20260915。根因确认：旧scope使用IslandSaveData.realStartDateTime.Ticks，但该字段未进入原生存档，每次读档重建，已付记录存在却被新scope漏取。英雄附加档升schema2：稳定文件/战役/挑战/land上下文与opaque epoch，legacy来源逐快照保留，精确回退搜索未归属旧scope，v2已确认空记录优先于legacy付费，冲突/身份不明锁槽不收费。新指纹仅排除三个顶层游玩计时字段，完整人物/钱包/建筑和其他字段仍参与；原生异步落盘的时钟漂移是旧最新快照不匹配的候选原因，未称唯一确因。当前旧v1无精确匹配，因此另用冻结原生SHA/精确岛JSON、实际购买与保存日志、唯一NPC记录作一次本机MOD侧修复：保留93c12776与9265e66e两笔原购买及全部旧快照，未改原生进度/生命值/金币。243购买/回退/日期变化/真实fixture绑定回归、9真实岛指纹检查、完整实际2.4构建0警告0错误，独立复核通过。DLL审计2904旧方法保持、27改变、98新增、42签名或闭包替换移除，修改限英雄持久化与build文字，Harmony类型和4PNG保持。备份与安装摘要核对完成，原生存档及其他配置保持；未启动游戏/提交/发布，公开8.0.0不变。实机下次读档找回两英雄仍待验证，跨岛运输仍待，旧透明遮挡/邻居跳动不因此宣称修好。
<!-- hero-save-restore-20260915 -->

<!-- hero-live-fixes-20260915 -->
2026-09-15 英雄实机反馈修复候选已闭游戏备份安装正确E盘：50d04ef3 / build=8.0.0-hero-live-fixes-20260915。用户确认cfe暂停修复已好，真实日志有多次pause/resume且无重建。此轮旗帜与建筑同sortingOrder，z-.001不变，避免晚画旗受透明FX深度干扰；实际PowerFire ZWrite1无discard为候选遮挡源，未把候选当实机定因。英雄本人walk/run基准×1.5、射程/私有弹道克隆×2；Prepare用既有cadence Last prefix内真实临时prep值，进入前锁窗；perfect跳过原生Shoot时由真实放箭事件播放0.18秒自有释放，首帧强制可见后推进，移动/新Prepare/未知动作让位。按用户最新纠正改为斜上举弓，动作层10/18/22度连贯抬起与收势；仅23..29七槽改手臂/金弓，头脚、其他槽、双围巾和三张其他PNG保持。夜间有原生goto8守位目标证据才向外，8→1保留守位；射击/移动交还原生朝向，未知life或恢复失败保留责任，Clear/Forget等待清账再删life。邻近跳动尚未定因，新增真实setup触发最多2邻居×12批只读位置/缩放/外形诊断，每上下文3次会话，不逐帧扫描全场。构建与实际interop0警告0错误，独立复核通过；103视觉/90姿势/106移速/155守位诊断/96runtime/146战斗/109购买/54商店/31塔位检查通过。DLL对cfe审计2847方法保持/23授权修改/103新增/7替换移除，Harmony类型集保持，准备窗口实际接线核验。存档配置全部hash保持；未启动游戏/提交/公开发布，公开8.0.0不变。新遮挡效果、斜举弓/释放、夜守向外/射程速度仍待实机，普通人物跳动待本轮日志；跨岛运输仍待。
<!-- hero-live-fixes-20260915 -->

<!-- hero-shop-stability-20260915 -->
2026-09-15 英雄驿站暂停闪烁修复：cfe8e50a / build=8.0.0-hero-shop-stability-20260915。用户确认主要开关暂停菜单整座消失；旧TryContext只接受Playing导致Menu清理重建。现仅精确Playing/Menu、同kingdom/layer/当前Postbox/header且对象有效时保留；Menu阻止付款，首次暂停有待付币时复用原生取消路径，成功后标记，恢复复用原对象；其他状态和未知context立即清理并记录原因。Core54、Invoker7、实际2.4 interop与完整构建通过（后两者0警告0错误），生产接线审计和独立审核通过。对d005审计2861方法保持、8改变、8新增、3移除（含Clear签名与闭包编号调整），4PNG保持，地面基准与owner ABI保留。已在游戏关闭后备份安装正确E盘，存档和配置hash保持；未启动游戏/提交/发布。暂停保留、半途投币暂停、恢复付款与实际高度仍待实机验收，跨岛身份运输仍未完成。
<!-- hero-shop-stability-20260915 -->



# ohmymods — Agent 必读

> 双架构项目：**IL2CPP 2.4.0 + BepInEx 6 是 Steam 发布主线**；
> **Mono 2.1.0 + UMM 是自用兼容线**。仓库：`C:/Users/ADMIN/Projects/ohmymods`。
> 本文件是给 agent（含新 session）的强制速查，详细文档在 `docs/project-harness/`。

## 必守规则（踩过的坑）

1. **先分流架构**：`il2cpp/` 是唯一发布与端到端验收主线（.NET 8 / BepInEx 6 / Il2CppInterop / HarmonyX）；
   仓库根 `Main.cs + Patch_*.cs` 是冻结的 Mono 历史/自用线。除非用户明确要求，不修改、不构建、不部署 Mono。
2. **[Mono-only] C# 5 语法**：无字符串插值/null 条件运算符；csc.exe 编译；Harmony v1.2（`HarmonyInstance`）。
3. **[Mono-only] 编译命令**：bash 里 csc 全量（引用 `E:/Kingdom Two Crowns/KingdomTwoCrowns_Data/Managed/`
   下 Assembly-CSharp/UnityEngine*/netstandard + `UnityModManager/` 下 UnityModManager/0Harmony-1.2），
   源文件用 `for f in Main.cs Patch_*.cs` 通配收集；`build.bat` 是 cmd 通配版（编码问题，bash 里别跑 bat）。
4. **[IL2CPP] 编译**：在 `il2cpp/` 执行
   `C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug`。Debug 默认会复制到开发环境；只验证编译时应禁用/覆盖 `BepInExPluginsPath`。
5. **部署边界**：IL2CPP 只允许写独立测试副本；`D:/Steam/steamapps/common/Kingdom Two Crowns` 禁止测试和写入。
   Mono 产物是根目录 `MyMod.dll`，部署到 `E:/Kingdom Two Crowns/Mods/MyMod/`。
6. **[Mono-only] 注入方案**（重要，别重踩）：BepInEx 5.4.23.3 的 winhttp（x86）+ `[General] target_assembly=`
   格式 doorstop_config.ini 指向 UnityModManager.dll。**UMM 21.0.32 自带 winhttp 不识别 Unity
   2022.3.51f1**。备份在 `E:/mod-dev/winhttp_bepinex5_x86.dll`。详见 runbook "注入方案"。
7. **源码参考**：`game-source/Assembly-CSharp-2.1.0/`（逻辑说明书，只读）；业务逻辑地图
   `docs/project-harness/game-logic-map/`（写 patch 前先查）。
8. **对象池游戏**：每次复用的本地初始化优先 OnEnable；但网络 RPC 必须等 sync 注册完成，禁止在 OnEnable 直接发送。
9. **单位缩放只动 y 轴**：x 是朝向符号（±1），`Mover.cs` velocity.x *= localScale.x，动 x 改速度。
10. **反射 GetMethod 前确认方法存在**：Worker/Peasant 没有 Start 方法（静默失败教训）。
11. **跨 biome 角色/工具**：Holder 注册 + sync 池（EnsurePoolForCharacter），否则 Pool.Spawn 崩/联机 desync。
12. **Resources.Load 找不到子目录资源**：必须 LoadAll + 名字匹配。
13. **2.1.0 差异**：Pool.syncID 是 short；Worker.OnTriggerEnter2D 在 npcShieldUser==null 时早退
    （希腊工人要补组件才能捡 BerserkerTool）；NpcShieldUser.Awake 可能提前 return（regenWait 要补初始化）。
14. **跨 biome 对象池依赖链必须完整审计**：角色本体 → 转职工具 → 攻击投射物 → 技能特效 →
    死亡/撤退特效 → 召唤物 → 主客同步池 → 每次 `PoolManager.InitPools` 后重注册。不能只验证角色能出生；
    必须实际触发攻击、技能、死亡/撤退、换岛/读档和池重建。详见 `game-logic-map/patch-patterns.md` 坑 18。

## 文档同步（每次改动必做，缺了会忘）

- **版本标准（用户2026-09-17授权）**：发布前先读根目录 `VERSIONING.md`。修复/既有行为优化每完整条目按体量累计 patch +1～5，新增功能按体量累计 minor +1～3；同一问题多轮返修不重复计数，基于最近正式版本核差异，记录本次增量账目。不要擅自因“更新很多”跳 major；玩法待验不因发布而置done。

- `docs/project-harness/harness-checklist.json`：活跃状态机；历史只进 `archive/`。改完运行 EXharness validator。
- `docs/project-harness/progress.md`：进展摘要。
- `docs/project-harness/game-logic-map/patch-patterns.md`：新坑编号与对象池依赖审计规则持续追加。
- `docs/project-harness/domain-model.md`：关键决策（当前到 D9）。
- 未获用户明确授权不要 commit；验收必须有对应证据，标有“待实测”的项目不得置为 done。

## 协作规范（collaboration-protocol.md 摘要）

- **2026-09-18 用户澄清（路由边界）**：operator 本身是 ZCode 时，"本地 ZCode"第一顺位**由其内置 subagent 直接满足**（GLM 5.3 max 继承主配置），不要外派 zcode CLI 或 OMP 去跑 GLM；外派仅用于需要隔离进程/可恢复会话等内置 subagent 覆盖不了的形态。时段规则是调用偏好不是硬性仪式；DeepSeek Flash（现役 deepseek-flash，多模态）仍是夜间默认。
- **2026-09-18 用户新增 operator 对抗审查规则**：ZCode 作为 operator 的每一个实质决策（任务分解/契约定稿/侦查诊断方向/worker 异常处置/修复方案/测试范围/任务级验收裁决/安装发布回滚关口/版本账目）执行前，都必须由一个 GLM 5.3 max 强度的对抗性 subagent 审查三问——是否正确、是否最优、是否引入新 bug 及如何解决；主 agent 非 GLM 5.3 max 时改派本地 ZCode/OMP 请求 GLM 5.3 max 并核验实际模型，不静默换。修正后须复审（僵持两轮走规则 6 裁决记录）；每次审查落一条记录（任务 events/receipts，含模型证据），无记录视为未审查；边界情形默认按决策处理，跳过需留一行理由；仅停止失控进程/恢复备份/撤销危险写盘类紧急止损可先执行后同会话补审，安装/发布/tag 等不可逆关口绝不豁免。素材/美术制作可用 OMP DeepSeek Flash max（截至 2026-09-18 官方现役 id `deepseek-flash`=V4.1 Flash 原生多模态，deepseek-v4-flash / vision-exp 是已下线别名不再用，deepseek-v4-pro 不支持图像；本机 OMP 的 images 标志可能滞后，看图任务以官方文档+实际模型事件核验，被陈旧标志挡住时更新/修正 OMP 须先获用户授权，未授权期改走内置 subagent 或 text-only；可与 operator 对抗交互或双审互评）。细则与委派模板见 collaboration-protocol.md。
- **2026-09-15 用户最新 worker 规则（覆盖此前固定 OMP / 禁用内置 worker 的约定）**：按北京时间 Asia/Shanghai（UTC+8）派发时刻选择。工作日暂按周一至周五，不自动引入节假日调休表；09:00≤时间<12:00 使用本地 ZCode 第一顺位、OMP 第二顺位，均请求 GLM 5.3、thinking=max；14:00≤时间<18:00 使用当前主 agent 的内置 subagent（未指定模型，继承主 agent 配置）；其余时间（含12–14点、夜间和周末）使用本机 OMP DeepSeek Flash、thinking=max。Reviewer 仍可用独立内置 subagent，不受这份 worker 时段限制。
- Operator 先侦查+分解；功能实现派 **worker**、重大功能/架构用 **reviewer** 交叉审核。
- 新建或恢复一个工作轮次前读取北京时间；已运行的有界任务完成当前轮次再按新时段派发，不强行杀进程。上午 ZCode 不可用时可用第二顺位 OMP，但模型仍为 GLM 5.3 max；指定模型不可用时先诊断并说明，不静默换模型。实际模型ID/max支持及运行事件按 `C:/Users/ADMIN/.codex/skills/invoke-coding-agents/SKILL.md` 核验，不把显示名当作已验证路由。
- worker 只建自己的 Patch_XXX.cs，**不改 Main.cs/build.bat**（Operator 统一注册）；
  跨 slice 契约由 Operator 在委派前定死。
- 默认只做 IL2CPP 构建与独立副本端到端验证。只有用户明确要求维护 Mono，或任务直接修改根目录
  Mono 源码时，才追加 Mono 验证。验收证据必须是编译输出、日志或游戏内现象。
- 委派时在任务书里写明：源码位置、契约、验收、语法约束（IL2CPP 主线现代 C#；
  仅 Mono 任务才限 C# 5）、模型要求。

## 常用路径

### 2026-09-06 启动事故临时门禁
- 2026-09-15 英雄驿站沉地修复已闭游戏备份安装E盘：d005c4e1 / build=8.0.0-hero-shop-grounding-20260915。前版f8c25095已真实通过owner预检、ready与purchasecompleted，但用户截图下半部被地面挡。实际94商店资源rootY0.875/0.88、rootbody bottompivot0/PPU32，旧自有root错用GameLayer.y。现仅取当前world活动Bow/Hammer/Scythe、根body可用且pivot0的worldY，主体/旗/币槽一起抬升，缺参考延后；保留自有pivot2px及底边约1px草沿，不改PNG/横向选址/z/付款。Core39/Invoker7/actualinterop与完整build0W0E、独立资源/代码review通过；对f8审计2868旧方法全同，仅Create和buildstamp改变、2新增方法、4PNG保持。备份/安装hash与全部用户数据hash通过，未启动/提交/发布；新高度仍待用户截图实测，跨岛仍待。
- 2026-09-15 英雄驿站实际未出现已定位并修候选f8c25095 / build=8.0.0-hero-shop-owner-interop-20260915。a6b5实际日志类型注册后InvalidProgramException；根因为本机ClassInjector对out enum生成ldobj LockReason&非法Invoker，注册/Marshal成功直到首次JIT才失败。只改自有IsLocked为ABI等价IntPtr输出桥，精确WriteInt32(NotLocked21)，保留原interface预检并核写回，增加首异常阶段/完整栈。7真实production指针/JIT/guard断言、31商店、完整和actualinterop0W0E、独立复现与review通过；2862方法不变/4方法改动，旧IsLocked换签名+编译器闭包编号变化，全部4PNG保持。已闭游戏备份安装正确E盘，用户存档/配置hash保持；未启动游戏/公开发布，preflight passed+ready及实际投币待验证。跨岛仍未完成。
- 2026-09-15 英雄驿站实际未出现已定位并修候选f8c25095 / build=8.0.0-hero-shop-owner-interop-20260915。a6b5实际日志类型注册后InvalidProgramException；根因为本机ClassInjector对out enum生成ldobj LockReason&非法Invoker，注册/Marshal成功直到首次JIT才失败。只改自有IsLocked为ABI等价IntPtr输出桥，精确WriteInt32(NotLocked21)，保留原interface预检并核写回，增加首异常阶段/完整栈。7真实production指针/JIT/guard断言、31商店、完整和actualinterop0W0E、独立复现与review通过；2862方法不变/4方法改动，旧IsLocked换签名+编译器闭包编号变化，全部4PNG保持。修复候选已就绪；用户游戏运行中，尚未替换DLL；未启动游戏/公开发布，preflight passed+ready及实际投币待验证。跨岛仍未完成。
- 2026-09-15 用户确认接入后，已在游戏关闭时备份并安装a6b5d90b英雄驿站候选到正确E盘独立副本，build=8.0.0-hero-shop-flags-ground-20260915。备份原正式80522bf1并核对，22份原生存档/配置/附加档hash保持，未启动游戏。可测试当前岛8币购买、无文字占位旗/死亡破旗、英雄不上箭塔；109购买/31塔/81效果/31商店及构建/独立审核证据沿用精确候选。这是本机候选安装，不是再次发布；公开8.0.0不变。跨岛名额范围/身份运输仍未完成，实际投币、显示、死亡与撤塔仍待实机。
- 2026-09-15 英雄驿站地面/旗帜候选 a6b5d90b / build=8.0.0-hero-shop-flags-ground-20260915：已购启用英雄不接箭塔岗位，已在塔位通过原生Exit退出；普通补位、骑士任务和关闭恢复原生保留。用户拒绝文字牌，现改空挂点/原生投币→完整金弓红旗占位→确认死亡破旗，Reserved保留完整旗。旧装饰旗转为真实占位旗，无文字/运行字体；破旗仅会话反馈不影响持久购买。109购买状态/31塔/81效果/31商店回归、完整和实际interop构建0W0E、独立2.4原生/像素/代码review通过；2665旧方法保持/10集成修改、原英雄2PNG保持。未安装/启动/发布，E仍80522bf1正式8.0。跨岛名额范围未答、运输桥和实机未完成；最新候选在operator hero-shop-20260915/candidate-ground-flags。
- 2026-09-15 英雄驿站当前岛候选完成，未安装/发布：8币训练现有弓手、每侧固定1席直到确认死亡；关闭/塔/丢弓转职/临时停用保留购买，真实回池不串人。独立sidecar精确快照+pin基线，修原生落盘失败回退锁槽、原版重存未知身份静默丢失、新岛初始化及OnEnable误解绑；未知身份保留名额并暂停收费。原创512x80四帧商店贴图，原英雄2PNG/围巾/金箭保留。build0W0E、87购买/81效果/31商店/146+85xUnit、actualinterop及独立93回归通过；DLL审计2665旧方法不变/10集成修改，候选SHA 70d53395. 跨岛名额范围待用户选择，运输身份桥和实机验收未完成；E盘仍80522bf1正式8.0.0，未操作游戏或用户数据。
- 2026-09-15 正式v8.0.0已发布Latest，tag/source dd5c86f，ZIP 1095ceb2 / DLL 80522bf1。精确提交clean canonical构建0W0E，修正发布工程缺firstpass Input的引用偏差，2675方法与此前8.0候选全同/两PNG保持；68项目重跑通过（56run/4xUnit392/392/8compile）。正式313项/306runtime逐字节匹配7.6.5、CRC/独立审核/远端digest/Latest与tag读回通过。用户明确要求发布后已闭游戏备份同步E独立副本8.0正式DLL，原生存档/个人cfg/ModSave附加文件hash全保持，未启动游戏；此前本机7DD候选已含最新玩法，只版本标记未统一。旧版本与master不改，英雄/围巾/联机等待实机项仍doing。
- 2026-09-15 8.0.0本地完整包已完成：release/KingdomEnhancedMod_v8.0.0_IL2CPP.zip，39642577bytes/314项/SHA 571d278b，DLL 9C63FF5D。117项冻结源码按真实2.4构建0W0E，2674旧方法与已装7DD53270相同，仅Init版本/build文字改变，两PNG保持；68项目全过（56run/4真xunit392/392/8compile），唯一旧interop测试缺ImageConversion引用已补并复跑。306运行依赖与正式7.6.5逐字节一致，CRC/白名单/双manifest/5docs/独立ZIP复核通过。包明确local-not-published与uncommitted-snapshot，BaseCommit仅基底；未commit/tag/push/release/安装/启动游戏。当前游戏仍7DD53270的7.6.5候选，canonical源码版本8.0.0；各功能实机/联机doing不变。
- 2026-09-15 双尾长围巾候选 7DD53270 / 7.6.5-hero-twin-scarf-20260915：颈肩31帧只改围巾连接小区域；双尾主体3–4/末2–3素材像素截面、双平涂折面、轻微宽度变化，56顶点/84索引每链且复用缓冲。副链物理锚(-1,+3)，可见根0/1下移2/1px连接颈环，火红副尾与红主尾区分；2/1.5px整数地面包络、drag.9和Chain动力学原文保持。31槽动作/.9/置前/金箭/战斗保留。相关回归、真实网格离线预览、2.4/普通完整构建、IL/neck像素审计及独立review通过；已闭游戏备份安装，save/config哈希保持，未启动游戏/提交/发布。实机观感待验，旧急转折地瞬跳和半透明接缝叠色边界仍记录，英雄doing/在线关闭。
- 2026-09-15 英雄飘带候选 1278D0D5 / 7.6.5-hero-cloth-flight-20260915：按用户授权改善站立下垂、跑动逐渐扬起、停步缓落。仅改布料纯链模拟的有符号速度包络、有界拖曳/抬升与空气阻尼；保持8节点/30Hz、波纹/段长/地面/Reset和无逐帧分配。取消新增flow镜像，修转向立停永久前折；既有低速贴地折向瞬跳尚存，未称全程无跳。31槽native动画、肩锚/0.9/置前/贴图/战斗不变。68cloth（两项语义适配）/45flight/72turn/33view/57nativevisual/73pose/65runtime、同输入预览、真实2.4与普通构建0W0E、2664旧方法保持/3授权修改/嵌入图逐字节一致及独立review通过；已闭游戏备份安装，save/config哈希保持，未启动游戏，未提交/发布。真实游戏观感仍待验，英雄整体doing、在线仍关闭。
- 2026-09-15 本机 EFD44621 / 7.6.5-hero-native-animation-20260915：用户澄清英雄持续可见但姿势轮廓跳变。重制31帧位（18张不同图，6走/6跑），固定头身像素与脚锚、保留腾空高度；仅跟随原生Animator current state/normalizedTime，转场不预取next，LateUpdate单帧去重；未知/停用/非法采样归还原生并保留自有状态。双红飘带仍为独立动态网格，肩锚随躯干起伏；整体0.9及英雄置前保留。73姿势/57真实接线stub/65runtime/68cloth/33cloth-view/146combat通过，真实2.4及普通构建0W0E，2622旧方法不变/13授权改动/5私有旧时钟方法移除，独立review通过。闭游戏备份FAE6后安装，save/config不变，未启动/提交/发布；实机姿势衔接、特殊动作回退、跨世界与像素观感待验，英雄整体仍doing、在线仍关闭。
- 2026-09-15 英雄动画研究：用户实测FAE6移动闪烁，要求先学习原版动作拆分/衔接并沉淀文档。本轮只读研究，不改生产代码/DLL/配置/存档、不操作游戏。实际2.4引擎Unity6000.0.61f1（非旧Mono2022.3）；提取8控制器及15clip离散PPtr时间轴164keys，补AnyState/default/layers/raw，3run各2脚步事件；Greek准备26keys仅6distinct。原版base archer7状态、Speed门1/.005、Idleness与Prepare可被移动退出；原run保留1–2px离地，旧草稿bbox贴底30抹去高度差。45首见帧/16selected12actors只能证实推进/当选记录，不能定闪烁频率或根因。已写game-logic-map/character-animation-production.md和可复现脚本/对照图，后续按原生状态与时间轴、完整生命周期和像素动作规格重做原型；英雄整体仍doing，当前闪烁未修复。
- 2026-09-15 本机 FAE6FBDD / 7.6.5-hero-walk-scale-20260915：用户实测英雄移动像滑行且要求整体0.9。修正goal=false错误Idle路径，优先原生Animator Speed（暂停为0）、读取失败/非有限才回退指令速度，abs左右均可；ModPanel既有Update直接Sync并沿用frame去重，未认定原LateUpdate失效。自有body/双cloth/肩锚XY绝对0.9，保留flip与-.05/.002置前，不改actor/碰撞/战斗/金箭/素材。缩放失败整组撤销；sprite赋值成功后每VisualState最多16首见帧日志[HeroArcherMotion]。40新检查、13281动画、65生命周期/68布料逻辑/33布料显示/29深度通过，普通与真实2.4构建0W0E、2626旧方法保持/8授权修改+1私有MotionOf签名替换、独立review通过。闭游戏备份758B5990安装，save/config不变，未启动/提交/发布；现场滑行是否完全解决、0.9观感仍待实测，特殊动作/联机仍未完成。
- 2026-09-15 本机 758B5990 / 7.6.5-hero-sorting-20260915：新增公共HeroVisualPriority，英雄身体位于GameLayer参考深度-0.05，双飘带随后0.002且同body排序，使已知普通角色（含Greek z0）重叠时英雄优先；可能盖住场景贴图，UI代码不改。仅改MOD自有表现，不动角色原生位置/碰撞/伤害。失败预检/部分写回滚与整组撤销、非有限/倾斜/shear/非单位z缩放门；未来英雄可复用。29深度+33布料+65生命周期通过，普通与真实2.4构建0W0E、2627旧方法保持/3授权修改、独立review通过。检测游戏已关闭后备份301FF296安装，save/config不变，未启动/提交/发布；真实遮挡及此前未见英雄根因仍待实测，原有特殊动作/联机未完成。
- 2026-09-15 本机 301FF296 / 7.6.5-hero-artemis-arrow-20260915：英雄全部箭使用原生 Artemis 30x5 金箭贴图与实例金色（主箭、散射、打猎），沿现有开关/单机门；材质/碰撞/伤害/弹道不变。发射作用域限定资格、有界128外观回执、池复用/关闭/换世界归还，仅接管RGB并保留alpha。144+14+14+14行为、88散射/146英雄/85希腊/50特效回归通过，普通及真实2.4构建0W0E，2587旧方法保持/3授权集成改动，独立review通过。闭游戏备份DE72后安装，save/config不变，未启动/提交/发布；实际箭头尺寸/发射位置/池复用观感待游戏验证，英雄联机仍关闭。
- 2026-09-14 本机 DE72F01F / 7.6.5-hero-archer-20260914：第06稿原创英雄弓手，F5默认off/全部世界单机/每侧1名/关闭恢复，1.5射速(与旧取max)、1.25私有SO弹道范围、总3箭、0.25/1点范围伤害同volley去重；16帧真像素+独立双红飘带，原生风有限映射、行波、重力触地、跑后飘/转向重基。7新组及旧88/73/50回归、普通+真实2.4构建0W0E、2357旧方法保持/27授权修改、review通过。闭游戏备份444E1611安装，save/config保持，未启动/提交/发布；游戏观感/战斗/切岛待验，在线英雄同步未实现且整项关闭，不称联机完成。
- 2026-09-14 用户明确确认："可以，这最后一版不错"。第06稿 concept-06-artemis-bow.png 的外观方向已确认，锁定当前头身比例、深蓝绿兜帽、神器风格金色反曲弓及长火红双飘带。后续像素与动画制作以此为基准；此确认仅为外观定稿，正式 Sprite、动画、玩法和游戏验证仍未完成。
- 2026-09-14 英雄弓手第06稿：用户认为第05稿头过大，要求弓借鉴游戏神器。内置 image_gen 缩小头/兜帽，保留矮壮身体与长红双飘带；参考实际 resources.assets 的 artemis_bow_reward（pathID 9005）形状预览，改金色反曲卷梢弓及象牙白弦。当前 concept-06-artemis-bow.png，准确提示词 concept-06-prompt.txt。仅静态外观候选，未实现动画/玩法，未动 DLL、源码或存档。
- 2026-09-14英雄弓手第05稿：用户指出第04稿身形过瘦、腿长且真人比例，要求贴原版卡通风格；内置image_gen以实际原版s8941作比例参考，修成矮壮、大头短肢的候选，长火红双飘带保留。当前候选concept-05-cartoon-proportions.png，提示词concept-05-prompt.txt；仍是静态设计待用户确认，不是生产sprite/动画或游戏验证，未改DLL/源码/存档。
- 2026-09-14英雄弓手美术准备：用户已要求开始，后明确火红披风/飘带并加长随风飘；内置image_gen经4稿得到深蓝绿兜帽+长火红双飘带静态候选concept-04。OMP Flash max按实际clip引用修正重名混帧，121原版Sprite/15clip及裁剪/32画布参考重建/8x共242组像素核验通过，独立review纠正技术规格。仅美术与文档，IL2CPP源码哈希保持，已装444E1611不动；正式原创像素动画、风摆、玩法和游戏验证未实现。 见 tasks/hero-archer-art-20260914/acceptance.md。
- 2026-09-14本机444E1611/build7.6.5-knight-load-seed-20260914：修首次迁移只在原生Save才写骑士附加档，现加载冻结来源、完整名单确定后由既有巡检整批写附加档，不保存重进也可恢复；保留精确快照/同life/双向唯一、Squire明确排除、失败等待/拒绝。53项目通过（seed34/runtime26含22名无Save真实接线重载/网络整合9）、0W0E/2750API/2340旧方法保持/review通过。闭游戏备份63105375安装，save57E54166/configAD2CB236及已有MOD附加文件保持；未启动/提交/发布。真实连续两次启动未Save验证仍待，头饰显示调查与英雄弓手美术准备仍排队。 见 tasks/knight-load-seed-20260914/acceptance.md。
- 2026-09-14本机63105375/build7.6.5-hermes-quota-20260914：用户明确将头饰独立随机30%改稳定配额，累计每10新转换3顶（4/7/10），v2 credit文件只读迁移v1 next8，已有actor不重分配；新增有界被动[HermesHeadwearDiag]用于显示调查。51项目/26quota+36真整合+16diag+62core+27visual、0W0E/2715API/2287旧方法保持/review通过。闭游戏备份7D3D后安装，save57E54166/configAD2CB236及v1游标保持，v2未预写；未启动/提交/发布。头饰显示根因仍未确认，真实10只3顶/外观/联机待验。 见 tasks/hermes-headwear-visible-20260914/acceptance.md。
- 2026-09-14本机7D3D2926/build7.6.5-hermes-disguise-cycle-20260914：友好巨魔戴原生面具/44款新增头饰免新主动选敌及反制追击，伤害不变/已起手允许完成；默认30%保留，命中后44款顺序循环，独立本机全局游标原子落盘。48项目通过（48保护/26轮换/62头饰整合及interop）、0W0E/2671API/2281无关方法保持/独立review通过；闭游戏备份C6B71AA6安装，save57E54166/configA3E3A0B8保持，未启动/提交/发布。真实战斗/AOE/头饰观感/保存读档/换岛/联机仍待。 见 tasks/hermes-disguise-cycle-20260914/acceptance.md。
- 2026-09-14本机C6B71AA6/build7.6.5-medieval-scatter-20260914：散射限当前中世纪style0骑士随从攻击敌人，打猎/其他类型单发；F5中世纪随从散射总箭1～3，只有额外箭淡金实例色，池复用恢复，6byte版本初始化由主机决定。44项目通过（88combat/16policy/44tint及实际interop）、0W0E/2651API/2246无关方法保持/独立review通过。闭游戏备份1D63533A后安装，save57E54166/configA3E3A0B8保持；未启动/提交/发布。主客需同版本，真实颜色/回收/狩猎/联机及旧身份档验证仍待。 见 tasks/medieval-scatter-20260914/acceptance.md。
- 2026-09-14本机1D63533A/build7.6.5-runtime-log-fixes-20260914：火塔满仓明确等待消耗并核真实可用槽，保留原CanPay/扣款；Rewired关闭后只跳Menu.SetMenuInput(false)失效输入写，保留其余退出清理。120补货+36弹药+14容量+8菜单、2interop编译、0W0E/2617API/2242无关方法保持/独立review通过。闭游戏安装备份9881275D，save57E54166/configA3E3A0B8保持，未启动/提交/发布；保留骑士独立档及所有旧修复，实际退出/补货与附加档保存读档仍待验。 见 tasks/runtime-log-fixes-20260914/acceptance.md。
- 2026-09-14本机9881275D/build7.6.5-knight-identity-20260914：骑士GUID/style独立附加档，精确完整岛快照匹配；旧档首次迁移、新招募补少，registered后nonce主客确认，池复用/坏slot/容量/版本保护。37项目回归、36archive+25runtime+48network+9整合断言、0W0E/2609API/2004无关方法保持/两名review通过。游戏关闭时原子安装，旧9F1F备份，当前save57E54166和configA3E3A0B8保持；未启动/提交/发布。真实保存读档与联机待验，原版重存失配可重新建身份，匿名换岛逐人延续未实现。 见 tasks/knight-identity-20260914/acceptance.md。
- 2026-09-14本机9F1F61A0/build7.6.5-samurai-diag-20260914：武士冲刺被动诊断[SamuraiDiag]，armed/start/trail/visual-ready/skipped/first-update/stop/tail-cleared/end，突发8次后每游戏秒补1次完整链、每链最多12行；无新native钩子或扫描。123冲刺+15视觉/日志+9诊断测试、0W0E/2374API/1941无关方法保持/独立review通过；闭游戏安装，save/config保持，未启动/未发布。保留希腊火矢0.25所有更改，真实冲刺日志待玩家游玩。见tasks/samurai-diag-20260914/acceptance.md。
- 2026-09-14本机5CED0D25/build7.6.5-greek-impact-20260914：希腊style3骑士火焰窗口的随从火矢，半径0.25/1点Fire/直接目标排除/同轮散射去重，沿用F5弓箭ImpactEnabled默认off与作者像素动画。窄TryDamage保留原生直接伤害且不写原生字段；73核心+45FX+75散射及30测试项目/1interopbuild、0W0E/2354API/1937无关方法保持/独立review通过。闭游戏安装到正确E，旧DLL已备份，save/config哈希保持，未启动游戏或发布；实机/压力/联机仍待。见tasks/greek-fire-impact-20260914/acceptance.md。
- 2026-09-14正式v7.6.5已发布Latest，tag/source648ddf0，ZIP64e8176f / DLLe1e5947c，已在游戏关闭时同步本机正式DLL，save/config哈希保持，未启动游戏。作者像素火焰+坐骑无限体力默认off，保留7.5全功能。29测试项目（28run+真实xunit75/75）+1interopbuild、0W0E/1945方法仅版本日志/2292API/独立完整包审核/远端digest通过。旧7.5保留，口误7.1.5未创建；实际观感/开启长骑/技能/联机边界仍待。见tasks/release-765-20260914/publication.md。
- 2026-09-14本机7614E05C/build7.5.0-infinite-stamina-20260914：F5便捷首卡坐骑无限体力，默认off/全部world/本机控制骑乘者，覆盖移动+3技能体力旁路，不改CD/饱食/速度。25case254assert/29全回归+真实interop编译/0W0E/27API/1917其他方法保持/独立review；4long入口实际FF25，Stamina get/set原字节，正确E PID30448可见暂停t6.93。作者像素火焰保留，原配置值保持；公开7.5未改，新开关实际长跑/各坐骑技能/两机待验。见tasks/infinite-stamina-20260914/acceptance.md。
- 2026-09-14本机34182546/build7.5.0-author-pixel-impact-20260914：经授权采用作者真实DLL PixelFireAnimator，3层5x5像素网格+共享1x1白图，替换7.5光晕；44FX/28全回归/0W0E/95API/1884无关方法保持/独立review通过，真实命中36v/150indices/SpritesDefault/Point成功。PID26760正确E可见暂停t0.35，save9404B148和配置值保持；公开7.5未变，无新commit/release。观感确认与真实切world/两机仍待，见tasks/author-impact-20260914/acceptance.md。
- 2026-09-14正式v7.5.0已发布Latest，tag/source338ee89，ZIP7dfdbf61 / DLL2b1e5f0a；28clean回归/0W0E/1912方法仅版本日志变化/2242API/独立包审核/远端digest通过。本机DLL319418A0与配置保持，未重启或安装；游戏自行结束，存档自然更新不回滚。功能实战/头饰真实Save→Reload/跨world/两机等仍按原任务doing追踪。见tasks/release-750-20260914/publication.md。
- 2026-09-14当前岛战斗已恢复：保留最新A3806BCD城镇，5局部标量+原生单马保存83863274；132弓手35工人4舰船及其他岛保持。自然夜袭15正样本/最多125敌人；移除临时工具后PID16808原生重载蛇Moving/木马Inactive，t12.87暂停。主DLL319418A0不变；勿回滚旧A380/A588/3DE，后续用户进度优先。见tasks/restore-current-battle-20260914/acceptance.md。
- 2026-09-14用户已返回普通希腊岛9：有效存档A588D870（游戏自动保存，carryFalse），原3DE17864是决战过渡10的旧档，后续测试不得回滚它。DLL仍319418A0，正确E PID27604可见且暂停；实机35工人/132弓手/4舰船，银行自然5791。见tasks/return-normal-island-20260914/acceptance.md。
- 2026-09-14立即销毁风险：E319418A0/build6.1.5-destroy-lifecycle-20260914；弩手显式可复用身份+pool前归还/共享SO新Knight接管，Castle两处改安全阶段精确对象队列。31+49新增、旧26套/0W0E/285API/独立review/约110秒启动通过，Archer/Pool真实FF25、save/config/bank5709保持。实际拾弓/旧店/换岛读档联机仍待，玩家闪退根因未确定。2.4RemoveShop已保护对象，不加注销hook；见tasks/destroy-lifecycle-20260914/acceptance.md。
- 2026-09-14法杖随机头饰：E 46AD1CF9/build6.1.5-hermes-headwear-20260914；F5战斗默认on/30%新转化44款纯外观，GUID组件JSON+Save/GetID薄桥，注册后RPC且caught-up再发，主客同版。55core+27visual+原24suite/0W0E/337API/review/112秒启动通过，44资源与42原生JSON内存往返、Save/GetID/SpawnMask真实FF25无fallback，save/config/bank5709保持。真实转化/动画遮挡/保存读档换岛/两机仍待；原帽消失未确认根因，不宣称修复。见tasks/hermes-headwear-20260914/acceptance.md。
- 2026-09-14弓箭可选增强：E 1F111CD5 / build=6.1.5-archer-options-20260914；F5弓箭散射1–5默认3、射速1–2默认1.5、独立host画面火焰FX，全world全off。24suite（combat75/FX36/scope27）+0W0E/278API/native/独立review/110秒启动通过，save/config/bank保持。主动开启实战/联机未验，见tasks/archer-options-20260914/acceptance.md。
- 2026-09-14农民/弹药补货：E 00FA75CA / build=6.1.5-restock-supplies-20260914；F5自动补货新增Farmer/桶/火塔ammo独立off15目标，自动Greek-only同队列2x价，HUD所有world主机两ammo真实数。21suite（119服务/84计数/32HUD/36弹药）+0W0E/310API/native/review/110秒启动通过，当前场景无ammo站点，真实购买/发射/联机待。save/config/bank5709保持，见tasks/restock-supplies-20260914/acceptance.md。
- 2026-09-14可选便捷：E B8200382 / build=6.1.5-optional-qol-20260914；F5便捷三个默认off/所有世界开关（长按加速连买、灌木双密关闭枯萎锁、森林等待÷3）。25购买+31植被+17scope/原17suite/0W0E/257API/独立review/110秒启动通过，旧1422方法与全部旧hook保持，save/config/bank5709保持。真实玩法与联机待；见tasks/optional-qol-20260914/acceptance.md。
- 2026-09-14银行仅Greek：E 2B27CCC0 / build=6.1.5-greek-bank-scope-20260914；ledger/profile/税收助手/采购最终门与HUD隔离，32bank+17真助手+98采购及17组总回归/build0W0E/437API/独立review/110秒Greek启动通过，save/config/bank5709保持。真实存取款/跨world/联机待，见tasks/greek-bank-scope-20260914/acceptance.md。worker用OMP Flash max，reviewer用户已允许内置。
- 2026-09-14全部缩放限当前Greek：E 494A879E / build=6.1.5-greek-scale-scope-20260914；原值owned恢复/未知延后/模板中性/币父污染，68core+9wallet+13既有suite/build0W0E/358API/review与Greek112秒启动通过；1293旧方法不变/无新增nativehook，save/config/bank5709保持。真实别world往返/联机目视仍待，见tasks/greek-scale-scope-20260914/acceptance.md。后续worker用OMP DeepSeekFlash max。
- 2026-09-14普通鹿3倍刷新：E 4113B3DD / build=6.1.5-deer-population-20260913，希腊host当前Forest鹿生成密度×3/补充间隔÷3，原3金币/池/季节保留；27回归/build/native/API/review/110秒受控启动通过。当前场景未命中生成器，森林正例待观察。save/config/bank保持，含兔子原版大小和隐士热修，见tasks/deer-spawn-20260913/acceptance.md。
- 2026-09-13小动物原版大小：E 92D4E207 / build=6.1.5-native-critters-20260913，删除Critter1.8的整个hook/登记；1317其他方法一致，保留6A9A隐士热修，build0W0E/安装hash与save/config/bank保持。下次启动生效，本轮未启动；见tasks/critter-native-scale-20260913/acceptance.md。
- 2026-09-13隐士闪退本机热修：E 6A9A4546 / build=6.1.5-hermit-policy-20260913，已完全撤去短getter detour，长生命周期独立enemy policy保留防绑架；24+11组回归/151API/独立审查/精确DLL自然150秒无原闪退、getter17原字节通过。save/config/bank5709保持。公开6.1.5仍旧钩子，不要覆盖本机新DLL。盾牌NRE与隐士遭遇Troll/长时间/联机仍待，见tasks/crash-20260913-0211/acceptance.md。
- 2026-09-13 02:11用户FFDD构建真实闪退：GameAssembly+4f0752/80000003；隐士防绑架短getter的+14错位执行与CPU签名高度吻合，尚待单变量确认。新岛盾牌NRE另查。**暂缓本机安装6.1.5（同钩子）**，不要回滚用户最新存档或未经复核重启测试。见tasks/crash-20260913-0211/plan.md。
- 2026-09-13正式v6.1.5已发布Latest；tag4aebcd0，ZIP487089ec / 正式DLL2ce32e34，人数HUD+剑风修复，11回归/clean完整包/独立审核/远端digest通过。用户游戏运行中，本机仍FFDD0E89未替换或新版本启动；待退出后安装。单机/主机人数、客机提示不可用；狮鹫仍待定位。见tasks/release-615-20260913/publication.md。
- 2026-09-13本机人数HUD：E FFDD0E89 / build=6.0.0-population-hud-20260913；左上八职业+骑士五风格，F5人口独立开关默认on。30+81回归/build0W0E/177Unity/review及原生截图通过；临时capture已移除，save/config/bank保持。单机/主机有效、客机提示不可用；公开6.0不变。见tasks/population-hud-20260913/acceptance.md。
- 2026-09-13本机剑风热修：E DLL 3399c131 / build=6.0.0-wind-arc-interop-20260913；SetPositions数组/Span改13点SetPosition，31回归/build0W0E/109Unity/review通过。实际E游戏已完成13点SetPosition剑风构建，成功日志出现，无原异常。save/config/bank保留，公开6.0.0不变。见tasks/wind-arc-interop-20260913/acceptance.md。
- 2026-09-13正式v6.0.0已发布并为Latest；tag/source356078c，ZIP416b6c79 / E正式DLLcdf5f61f。双倍采购、HUD/塔位/猫1.25及交战锚点修复，10套回归/clean完整包/自然夜间锚点与退速观测/远端digest通过；用户状态保留。完整攻门/大蛇/联机与全世界隔离边界仍追踪。见tasks/release-600-20260912/publication.md。
- 2026-09-12最新本机：B33D56B8 build=5.0.0-cat-scale-125-20260912；希腊农舍猫y缩放1.2→1.25，其他逻辑保留。build0W0E、DLL常量与安装哈希通过，save/bank未动，公开5.0不变。见tasks/cat-scale-125-20260912/acceptance.md。
- 2026-09-12最新本机：BC504C61 build=5.0.0-restock-double-cost-20260912；五职业原价双倍投币/扣款，手动价保留，实机忍者4/狂战士6且离店成功。服务回归/63bank/226Unity/build/review通过，save/config/bank5408恢复；公开5.0不变。其他世界旧全局补丁隔离仍未完成。见tasks/restock-double-cost-20260912/acceptance.md。
- 2026-09-12 15:55最新本机：8929889C build=5.0.0-peasant-hud-motion-20260912；无边框HUD默认字体用户确认、Peasant面包采购本机开启15、税收官附近出现跑近付款/走开返城。206检查/309Unity/nativehook/review与正确E实机采购位置日志通过；save与bank5423恢复。公开5.0不变，见tasks/peasant-bread-hud-20260912/acceptance.md。
- 2026-09-12 14:17最新本机：654051E9 build=5.0.0-restock-allhours-20260912；允许全天采购，修读档kingdom.banker=null误拒绝税收官借调/扣款。42service+51counts+49integration/build212Unity/review通过；FF9D真实忍者2金币/狂战士3金币购买成功，最终只改全天帮助文案。save与bank5423恢复，HUD/塔修复保留；公开5.0不变。见tasks/auto-restock-live-20260912/acceptance.md。
- 2026-09-12 13:53最新本机：2BD2055C build=5.0.0-hud-tower-refill-20260912；时间/金库像素条（左上人口暂空）+同步补岗重入修复/停用后注销空岗。109tests、144Unity路径、native唯一hook、review和E实际removed=2通过save一致。正常保存再读/HUD目视仍待；自动补货采购仍未证实。公开5.0不变。见tasks/hud-pixel-style-20260912与tower-residual-20260912/acceptance.md。
- 2026-09-12最新本机：A309B08E build=5.0.0-auto-restock-20260912；四职业税收官金库采购/事件库存缓存/独立阈值页，默认off/15。127tests/build326Unity路径/4nativehooks/review/暂停启动通过save一致；真正启用采购及联机待测，公开5.0不变。见tasks/auto-restock-20260912/acceptance.md。
- 2026-09-12最新本机：6DE8F6CB build=5.0.0-combat-home-towers-20260912；同址升级普通重复塔安全撤员清理、Berserker12/2原生返程、B/N黎明不追撤怪、Greek守家自身scanner兜底。288tests/build/API/hook/暂停启动通过、save一致；实机清塔与战斗待确认，公开5.0仍0549。见tasks/combat-home-towers-20260912/acceptance.md。
- 2026-09-12 00:59最新本机热修：73B11F7D... build=5.0.0-tower-overlap-20260912；修特殊升级塔无Tower标签漏检/旧空KEM重叠，46tests/review/build/启动通过。当前day63暂停未实机清旧，需恢复运行5秒并避开付款/选择保护。公开5.0 ZIP和DLL0549仍未更新，未commit/push新修复。详见tasks/tower-overlap-20260912/acceptance.md。
- 2026-09-12正式v5.0.0已发布并为GitHub Latest；tag/source abf4eb3，ZIP DD7C26FA...（39458470 bytes/313entries），E正式DLL0549C166...（5.0.0/build5.0.0）。clean构建、7套repo回归、精确包启动与远端digest均通过。下方F32及更早为历史本机版本；功能实战/联机边界仍doing。见tasks/release-500-20260912/publication.md。
- 2026-09-11 23:59最新本机：F32CBE8C...，build=4.5.0-fleet-retreat-cats-20260911；希腊舰队仅新Greek配队min/仅武士defensive retreat3x/每farm4猫含旧mod回收。154tests+review+启动通过，存档一致；实际登船/退速/猫/残影/联机待验收。见tasks/fleet-retreat-cats-20260912/acceptance.md，下方C176及更早均历史。
- 2026-09-11 23:31最新本机：C1763E2F...，build=4.5.0-expedition-follow-20260911；修复守墙把随从动态Object跟随降为固定Position，改临时offset及任务门/可靠归还。49tests/review/启动通过，存档一致；实际出征/联机待验收，详见tasks/expedition-follow-20260911/acceptance.md。下方7F及更早均历史本机基线。
- 2026-09-11 23:03最新本机：7F0CBE8D...，build=4.5.0-combat-visuals-20260911；本机武士浅白残影45/25/10+.2s，Greek空fireSO恢复，Medieval sortingLayerID修复。170tests/review/启动通过，日志实证fire资源补齐；视觉实战待验收，新客机残影同步未做。见tasks/combat-visuals-20260911/acceptance.md。
- 2026-09-11 最新本机：8BA3448F...，build=4.5.0-squad-refill-diag-20260911；撤旧逐箭日志，新增有界补员事件和既有缓存名册采样（最多36行），36tests/review/启动通过。补员根因尚待现场日志，不得宣称已修。见tasks/squad-refill-20260911/acceptance.md。
- 2026-09-07 最新本机：EA5D1001...，build=4.5.0-greek-fire-20260907（希腊火8s/15s+火buff换队弩包保护+既有银行HUD/1204/幕府回冲伤害）；70case、独立review、受控启动通过，实战/联机待观察。见tasks/greek-fire-20260907/acceptance.md。
- 2026-09-07 21:46本机最新：2F089BA8...，build=4.5.0-samurai-return-hit-20260907（回冲也有正常伤害，前/回共用命中）；76case及受控启动通过，实战待反馈。旧53E9无伤害版仅历史；见tasks/samurai-return-20260907/hit-update-acceptance.md。
- 2026-09-07最新本机：53E9A1DE...，build=4.5.0-samurai-return-20260907（主动返队+银行HUD+120/4）；21:31受控启动通过，实战待反馈。下方6C6/AA4B是历史候选/发布基线。见tasks/samurai-return-20260907/acceptance.md。
- 最新本机迭代：16:11已部署6C6BA423...，build=4.5.0-hudbank-20260906（银行HUD、人口默认120/4；本机明确授权同步120/4），受控启动通过。公开4.5.0包仍为AA4B1959...，下条是发布基线记录。见tasks/hud-bank-defaults-20260906/acceptance.md。
- E测试副本现为正式v4.5.0包内DLL AA4B1959...（15:56已通过受控启动），发布commit c203218，canonical同步强root取消守卫和幕府禁用清理修复。此前8390AF/A9/DB部署为历史；不得重新引入共享Dispose钩子。发布记录见`docs/project-harness/tasks/release-450-20260906/publication.md`；联机等边界仍待实测。
- 检查IL2CPP编译器生成的Dispose等短方法时，managed wrapper名字唯一不等于原生地址唯一；未核实地址折叠前不要把其detour视为已安全验收。
- WindowsPowerShell5.1不要把`Get-Content -Raw`/原始Provider对象直接放入高Depth的ConvertTo-Json；改用`[IO.File]::ReadAllText`与显式纯值投影，输出有界。事故进程有8.28GB提交量；不要重跑该脚本压测。

| 项 | 路径 |
|---|---|
| IL2CPP 开发环境 | `E:/QQ/QQ下载文件/Kingdom Two Crowns (1)/Kingdom Two Crowns` |
| IL2CPP 独立测试副本 | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091` |
| Steam 正式版（勿动） | `D:/Steam/steamapps/common/Kingdom Two Crowns` |
| Mono 自用环境 | `E:/Kingdom Two Crowns/`（GOG 2.1.0 x86） |
| 旧游戏（2.0.1 x64） | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Call.of.Olympus-P2P` |
| Player.log | `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Player.log` |
| IL2CPP 日志 | `<测试副本>/BepInEx/LogOutput.log` |
| 共享存档 | `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35` |
| 反编译 2.1.0 | `E:/Kingdom Two Crowns/Assembly-CSharp/`（源）+ `game-source/Assembly-CSharp-2.1.0/`（库内） |
