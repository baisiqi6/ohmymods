<!-- shop-territory-installed-20260925 -->
2026-09-25 23:55 Windows PC/Codex：用户确认退出后，检测无Kingdom进程，核最新候选/受保护源码/旧DLL摘要，按已复审安装关口备份并安装累计94CC219A（build=9.14.24-choreo-shopland-20260925）至既定E盘测试副本。SHA256=9E56BF4BB873DC68F7D65A5492D52196D76CB5D8E9F2A7E6231C512F636411C4，旧8E994A98备份后缀20260925-235547.bak；存档与配置前后hash一致，未启动游戏。含商店全领地选址与武士收势/固定往返目标/仅白残影修复，实机待验。回执tasks/shop-territory-placement-20260925/receipts/install.json，未commit/push/publish。
<!-- shop-territory-installed-20260925 -->
<!-- shop-territory-placement-20260925 -->
2026-09-25 Windows PC/Codex：按用户要求火铳铺与共用英雄驿站覆盖整个intact领地选址，取消±30/0.75固定采样；完整原生预留边界仅划分候选，超集不减去，double阈值与内部float/原生终检，无固定epsilon/对象截断，保留已有店/支付/枪架/暂停/地面。67+18+60+35检查、实际2.4接口与完整build0W0E、独立GLM5.3/max复审APPROVE。累计候选94CC219A / build=9.14.24-choreo-shopland-20260925包含未安装的DED188D7武士收势/回程/去拖尾修复，受保护源码hash保持；游戏PID45240仍运行，尚未安装，E盘仍8E994A98。旧DED候选另有备份，不要按旧路径误装。实机生成/密集选址耗时与武士观感待验，未commit/push/publish；证据tasks/shop-territory-placement-20260925。
<!-- shop-territory-placement-20260925 -->
<!-- samurai-choreo-settle-20260925 -->
2026-09-25 12:04 Windows PC/Codex：用户实机确认上一候选真实往返与白影；本轮修复原生Land收势、删除独立追随从突进、每帧固定目标/越点判定、删除连续trail写点。六套328/0，主build0W0E，GLM5.3/max最终APPROVE。候选DED188D7 / build=9.14.24-choreo-settle-20260925已准备，游戏仍运行，尚未安装。旧8E994A98任务内已备份；未commit/push。证据见tasks/samurai-slash-guard-20260925/live-feedback；新版实机/联机待验，同问题返修不重复计版本。
<!-- samurai-choreo-settle-20260925 -->
<!-- samurai-choreo-whitefix-20260925 -->
2026-09-25 Windows PC / Codex 按zcode-handoff接手，在独立树ohmymods-wt-choreo-fix、win/samurai-choreo-hard-validity@67d39b9实现燕返硬失效门和真实packed白剪影；六套回归合计355/0（216/31/24/32/43/9），主构建0W0E。运动与视觉GLM5.3/max独立复审均APPROVE。候选build=9.14.24-choreo-whitefix-20260925、MD5 8E994A98已于11:09闭游戏安装E副本，旧DLL已.bak备份，存档指纹不变；未启动游戏，实机/联机待验。未commit/push。任务设计/审查/测试/候选hash见tasks/samurai-slash-guard-20260925。原主树progress/checklist有既有合并冲突，不触碰；该续作未纳入Coordinate assignment，不裸改其状态或把旧samurai条目改done。
<!-- samurai-choreo-whitefix-20260925 -->
<!-- identity-restore-instability-20260919 -->
2026-09-19/20 玩家反馈"进游戏/换岛时骑士火枪弩手等MOD角色有时不出现、重进抽奖式"两轮对抗审查诊断：第一轮四类机制（确认延迟自愈/身份精确匹配fail-closed/跨岛运输未完成/弩手身份不持久重选）；第二轮按"非自愈+抽奖"新事实深挖，审查发现头号嫌疑=H4a吸收态（一次性sidecar写失败或legacy失配→拒写会话→原生自动保存推进→hash永不再命中→该岛永久unresolved，解释抽奖变永久坏），musketeer读IoError静默不查备份是唯一真逐启动抽奖路径，H1(v8→v9时钟漂移)首启定局，H3竞态排除。修复菜单：吸收态再基线化（证据门，需用户拍板）/musketeer读失败日志+备份回退+MatchKind日志（低风险）/人工claim工具。收集清单六项已定（含ModSave目录.tmp残留=写失败直接证据）。诊断阶段未改代码，回执tasks/identity-restore-instability-20260919/。
<!-- identity-restore-instability-20260919 -->

<!-- mac-trial-945-20260919 -->
2026-09-19 按用户侧聊草稿起草 Mac 端 v9.4.5 试跑任务书（tasks/mac-trial-945-20260919/worker-brief.md）：三阶段止步、只验证不修改、ZIP单文件白名单、回执汇入本目录。对抗审查5必改全落：grep串改消息体子串（原串对不上真实日志字节）、ZIP禁整包解压防Windows be.752/dotnet/doorstop污染、冒烟举例君主速度实为默认开改真实默认关项、BepInEx门槛#754→#755（官方builds证据：Unity6 IL2CPP支持是#755/PR#1284，#754仅Doorstop）、审查记录落盘；建议项采纳（ZIP sha256钉死/interop联网判定/Runtime+DetourProviderType记录/Gatekeeper提示）。等待Mac回执，doing。
<!-- mac-trial-945-20260919 -->

<!-- identity-hardening-20260920 -->
2026-09-20 身份恢复A+B加固闭游戏安装E盘4FB8613C（build=9.4.5-identity-hardening-20260920，累积候选）：A=musketeer读IoError查备份+告警/kind日志/IO类写重试一次/Missing日志；B=knight吸收态再基线（known-mismatch精确触发+严格子集证据门+新epoch+全一致携带+绑定更新），musketeer不做B（证据门不可支持）。设计过对抗审查（3硬伤：锚点错位/musketeer不可行/绑定未更新），休息日deepseek-flash两轮+operator复跑七套件全绿45/42/16/34/9/48/303、构建0W0E。VERSIONING+0.0.4→9.5.12。实机自愈验证与玩家档取证待做，doing。
<!-- identity-hardening-20260920 -->

<!-- banner-rowgap-20260919 -->
2026-09-19 修复举旗编队火枪手行空隙（用户报告"像又站了四位弓箭手"）：根因Gap槽+原生尾Gap+多船累积（1船4.7×/0船5.7×/2船13.9×弓手步），对抗审查否决原方案定稿中插+Squire槽+Archer行距，边界恒1步、弓手侧坐标=基线、fleet块-0.875、未满员收紧贴弓手侧。休息日OMP deepseek-flash max两轮（二轮修boats=0测试镜像typo），operator复跑unit42/0、e2e23/0、双构建0W0E，闭游戏安装E盘A1777BBB（累积候选），前一已备份，存档hash保持。实机目测/收旗还原/镜像/联机待验，doing。
<!-- banner-rowgap-20260919 -->

<!-- shop-claimlock-20260918 -->
2026-09-18 上午修复火铳铺认领窗口双重锁（平民拾取前买不了第二把）：根因RackCount/Reconcile双链对claimed枪fail-closed+新枪0.5s布放瞬态，原生侧Peasant.SetDroppableTarget认领无超时（对抗审查确认，含不修边界enemyClaimer/pickedUp保持）。三处最小修复+测试翻转2断言新增6+8接线检查，复跑PASS60/35、构建0W0E，闭游戏安装E盘4F502247（累积穿墙候选），前一候选已备份。派工过程教训：上午第一顺位死板外派zcode.cmd（两次Model creation failed）再明示换OMP glm-5.3 max，用户澄清后协议已改：operator为ZCode时第一顺位=内置subagent，不外派。实机连买/认领取消/敌人抢枪/联机待验，doing。版本考古：对比v9.0.0/v9.4.5发布worktree证实该锁为v9.4.5侧枪架重做引入的回归，v9.0.0的RackCount本就把认领中枪按占位计数（玩家反馈正确），本修复即恢复该语义；同日用户指明2026-09-18为休息日，协议补记：用户指明休息日按休息时段路由（OMP DeepSeek Flash），拿不准先问不默认周一至周五。
<!-- shop-claimlock-20260918 -->

<!-- features-overview-20260918 -->
2026-09-18 上午按用户反馈（速览喜欢但太少、要发QQ群、不指向其他文档）把总览扩为 v2 自足群发版（270行）：角色图鉴（英雄/火枪/五骑士/弩手/忍者/狂战士/税收助手全量）、世界经营（特种塔重建/舰队/法杖/Cerberus）、守家阵型、修复要点节（两类措辞规则）、自足安装步骤。发现源扩至13份MOD_V*旧说明+il2cpp文件清单（用户抱怨的缺项大半在V2-V7.6.5旧说明），产出coverage-matrix对账矩阵。两轮派工方案审查+两轮内容审查（v1抓农田币事实错误，v2抓弩手守位4-7与面板1040两处旧值漂移），三处修正+软化措辞已执行，泄漏grep零命中，默认速览逐条镜像GUIDE。候选节已删（发群=公开）。v1记录：同日早先 release/MOD_FEATURES_OVERVIEW_ZH.txt（30秒速览/新角色/机制/默认开关镜像GUIDE/限制≤12行/候选隔离节/深入阅读）。工作日上午时段派本地 ZCode CLI 0.16.5（edit+attachment任务书，allowlist=新文件+任务目录；0.16.5移除--max-turns去参重派；实际模型UNVERIFIED），worker产出provenance溯源表并自行发掘核验历史功能（农田币/盾墙图腾/大蛇缰绳/弩箭×1.25/Hermes钱袋/主船容量/北境钱包等）。派工方案与内容各过一轮对抗审查：方案审查抓出三txt未覆盖存量功能需代码核验路径等4缺陷；内容审查26条抽查抓出农田币事实错误（源码陈旧注释误导，git 6ba2b71已否决12s留捡设计）并修正+两建议采纳。VERSIONING§四同步清单已补入该文件；是否随包发布下轮定。回执tasks/features-overview-20260918/。同日上午OMP经FlClash 7890代理更新至18.2.5，deepseek-flash目录images=yes已修正（issue #11602），协议规则10与L34例外维持"以官方文档+模型事件核验"口径无需再改。
<!-- features-overview-20260918 -->

<!-- protocol-adversarial-review-20260918 -->
2026-09-18 用户新增协作规则并已写入 collaboration-protocol.md 与 AGENTS.md 摘要：operator（ZCode）每个实质决策执行前须经 GLM 5.3 max 强度对抗性 subagent 三问审查（正确/最优/新bug及修复），修正后复审、审查落记录、紧急止损窄豁免、不可逆关口不豁免；素材制作可用 OMP DeepSeek Flash max（可对抗交互/双审）。（同日用户指正：官方现役 deepseek-flash 即 V4.1 Flash、原生多模态，旧 vision 别名已下线，本机 OMP images 标志滞后；规则10与L34例外已按官方文档修订并经对抗审查复审。）本次修订本身按新规执行了完整流程：方案先过对抗审查（verdict 需修订后执行，7条意见），按意见修订后送复审通过；两轮审查回执落 tasks/protocol-updates-20260918/review-log.md。
<!-- protocol-adversarial-review-20260918 -->

<!-- hero-arrow-pierce-20260918 -->
2026-09-18 夜间接替codex会话（9-17 23:38用量超限中断，最后一条用户消息未答）后完成：①确认今晚正式9.4.5会话零Error/Exception、火枪猎鹿伤害提交正例（对敌伤害无成功日志属设计，不能出击杀回执）；②英雄金箭65%显示缩放（PPU 32/0.65纯显示）+英雄箭矢无视城墙碰撞（HeroArcherWallPierce，IgnoreCollision墙对成对Apply/Restore，零新钩子）。夜间OMP deepseek-flash max三轮，operator复跑主线+interop双构建0W0E、新套件gold89/missing13、旧artemis三模式153/19/19全PASS，独立review PASS WITH NOTES。已闭游戏备份安装E盘候选2482F0D3/build9.4.5-hero-arrow-pierce-20260918，正式0F8C1FC8备份在任务receipts，用户存档hash保持，未启动游戏、未commit/push/publish。实机穿墙/0.65观感/trail/密集齐射/联机待验，doing。
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

## 2026-09-15 — 正式发布8.0.0并同步本机

2026-09-15 正式v8.0.0已发布Latest，tag/source dd5c86f，ZIP 1095ceb2 / DLL 80522bf1。精确提交clean canonical构建0W0E，修正发布工程缺firstpass Input的引用偏差，2675方法与此前8.0候选全同/两PNG保持；68项目重跑通过（56run/4xUnit392/392/8compile）。正式313项/306runtime逐字节匹配7.6.5、CRC/独立审核/远端digest/Latest与tag读回通过。用户明确要求发布后已闭游戏备份同步E独立副本8.0正式DLL，原生存档/个人cfg/ModSave附加文件hash全保持，未启动游戏；此前本机7DD候选已含最新玩法，只版本标记未统一。旧版本与master不改，英雄/围巾/联机等待实机项仍doing。

## 2026-09-15 — 8.0.0本地完整包

2026-09-15 8.0.0本地完整包已完成：release/KingdomEnhancedMod_v8.0.0_IL2CPP.zip，39642577bytes/314项/SHA 571d278b，DLL 9C63FF5D。117项冻结源码按真实2.4构建0W0E，2674旧方法与已装7DD53270相同，仅Init版本/build文字改变，两PNG保持；68项目全过（56run/4真xunit392/392/8compile），唯一旧interop测试缺ImageConversion引用已补并复跑。306运行依赖与正式7.6.5逐字节一致，CRC/白名单/双manifest/5docs/独立ZIP复核通过。包明确local-not-published与uncommitted-snapshot，BaseCommit仅基底；未commit/tag/push/release/安装/启动游戏。当前游戏仍7DD53270的7.6.5候选，canonical源码版本8.0.0；各功能实机/联机doing不变。

## 2026-09-15 — 双尾长围巾

2026-09-15 双尾长围巾候选 7DD53270 / 7.6.5-hero-twin-scarf-20260915：颈肩31帧只改围巾连接小区域；双尾主体3–4/末2–3素材像素截面、双平涂折面、轻微宽度变化，56顶点/84索引每链且复用缓冲。副链物理锚(-1,+3)，可见根0/1下移2/1px连接颈环，火红副尾与红主尾区分；2/1.5px整数地面包络、drag.9和Chain动力学原文保持。31槽动作/.9/置前/金箭/战斗保留。相关回归、真实网格离线预览、2.4/普通完整构建、IL/neck像素审计及独立review通过；已闭游戏备份安装，save/config哈希保持，未启动游戏/提交/发布。实机观感待验，旧急转折地瞬跳和半透明接缝叠色边界仍记录，英雄doing/在线关闭。

## 2026-09-15 — 飘带跑动展开与停步回落

2026-09-15 英雄飘带候选 1278D0D5 / 7.6.5-hero-cloth-flight-20260915：按用户授权改善站立下垂、跑动逐渐扬起、停步缓落。仅改布料纯链模拟的有符号速度包络、有界拖曳/抬升与空气阻尼；保持8节点/30Hz、波纹/段长/地面/Reset和无逐帧分配。取消新增flow镜像，修转向立停永久前折；既有低速贴地折向瞬跳尚存，未称全程无跳。31槽native动画、肩锚/0.9/置前/贴图/战斗不变。68cloth（两项语义适配）/45flight/72turn/33view/57nativevisual/73pose/65runtime、同输入预览、真实2.4与普通构建0W0E、2664旧方法保持/3授权修改/嵌入图逐字节一致及独立review通过；已闭游戏备份安装，save/config哈希保持，未启动游戏，未提交/发布。真实游戏观感仍待验，英雄整体doing、在线仍关闭。

## 2026-09-15 — 英雄原生动作候选安装

2026-09-15 本机 EFD44621 / 7.6.5-hero-native-animation-20260915：用户澄清英雄持续可见但姿势轮廓跳变。重制31帧位（18张不同图，6走/6跑），固定头身像素与脚锚、保留腾空高度；仅跟随原生Animator current state/normalizedTime，转场不预取next，LateUpdate单帧去重；未知/停用/非法采样归还原生并保留自有状态。双红飘带仍为独立动态网格，肩锚随躯干起伏；整体0.9及英雄置前保留。73姿势/57真实接线stub/65runtime/68cloth/33cloth-view/146combat通过，真实2.4及普通构建0W0E，2622旧方法不变/13授权改动/5私有旧时钟方法移除，独立review通过。闭游戏备份FAE6后安装，save/config不变，未启动/提交/发布；实机姿势衔接、特殊动作回退、跨世界与像素观感待验，英雄整体仍doing、在线仍关闭。

## 2026-09-14 — 发布7.6.5

2026-09-14正式v7.6.5已发布Latest，tag/source648ddf0，ZIP64e8176f / DLLe1e5947c，已在游戏关闭时同步本机正式DLL，save/config哈希保持，未启动游戏。作者像素火焰+坐骑无限体力默认off，保留7.5全功能。29测试项目（28run+真实xunit75/75）+1interopbuild、0W0E/1945方法仅版本日志/2292API/独立完整包审核/远端digest通过。旧7.5保留，口误7.1.5未创建；实际观感/开启长骑/技能/联机边界仍待。见tasks/release-765-20260914/publication.md。

## 2026-09-14 — 坐骑无限体力开关

2026-09-14本机7614E05C/build7.5.0-infinite-stamina-20260914：F5便捷首卡坐骑无限体力，默认off/全部world/本机控制骑乘者，覆盖移动+3技能体力旁路，不改CD/饱食/速度。25case254assert/29全回归+真实interop编译/0W0E/27API/1917其他方法保持/独立review；4long入口实际FF25，Stamina get/set原字节，正确E PID30448可见暂停t6.93。作者像素火焰保留，原配置值保持；公开7.5未改，新开关实际长跑/各坐骑技能/两机待验。见tasks/infinite-stamina-20260914/acceptance.md。

## 2026-09-14 — 授权作者像素火焰

2026-09-14本机34182546/build7.5.0-author-pixel-impact-20260914：经授权采用作者真实DLL PixelFireAnimator，3层5x5像素网格+共享1x1白图，替换7.5光晕；44FX/28全回归/0W0E/95API/1884无关方法保持/独立review通过，真实命中36v/150indices/SpritesDefault/Point成功。PID26760正确E可见暂停t0.35，save9404B148和配置值保持；公开7.5未变，无新commit/release。观感确认与真实切world/两机仍待，见tasks/author-impact-20260914/acceptance.md。

## 2026-09-14 — 发布7.5

2026-09-14正式v7.5.0已发布Latest，tag/source338ee89，ZIP7dfdbf61 / DLL2b1e5f0a；28clean回归/0W0E/1912方法仅版本日志变化/2242API/独立包审核/远端digest通过。本机DLL319418A0与配置保持，未重启或安装；游戏自行结束，存档自然更新不回滚。功能实战/头饰真实Save→Reload/跨world/两机等仍按原任务doing追踪。见tasks/release-750-20260914/publication.md。

## 2026-09-14 — 恢复当前岛战斗

2026-09-14当前岛战斗已恢复：保留最新A3806BCD城镇，5局部标量+原生单马保存83863274；132弓手35工人4舰船及其他岛保持。自然夜袭15正样本/最多125敌人；移除临时工具后PID16808原生重载蛇Moving/木马Inactive，t12.87暂停。主DLL319418A0不变；勿回滚旧A380/A588/3DE，后续用户进度优先。见tasks/restore-current-battle-20260914/acceptance.md。

## 2026-09-14 — 返回普通希腊岛测试

用户要求从最终决战返回普通岛，已安全改4过渡标量10→9/Boats/新增舰队0，所有原岛JSON保持。正确E可见PID27604实机返岛：工人35、弓手132（各带回12），舰队4；原生自动保存A588D870，currentLand9/carryFalse，已Esc暂停。银行自然5709→5791，不回滚。DLL319418A0未动。原档备份完整；**之后不要恢复旧3DE17864决战过渡存档**。详见tasks/return-normal-island-20260914/acceptance.md。误启动旧PID24720及隐藏PID29848已关闭，只留正确可见游戏。

## 2026-09-14 — 立即销毁生命周期隐患

已仅安装E独立副本319418A0/build6.1.5-destroy-lifecycle-20260914，保留46AD1CF9全部其他修复。三处DestroyImmediate仍在旧源码，现移除：弩手可复用marker与显式身份/冷却账本、pool原生招募前清旧包、隐藏保留、global关闭owned恢复；商店回调只登记，安全Tick观察真实world/planner/slot/object及callback后状态，再注销/停用/延迟销毁，unknown不当empty，退避不丢清理。

实际2.4 RemoveShop自带对象匹配，旧2.1资料的二次注销推论不适用，不新增商店hook。root补PendingPoolHandoff：真实Knight新owner共享SO/skin/12/1.15时旧账本退休不撤新包。31+49新增/旧26套回归、0W0E/285API/独立review与精确约110秒启动通过；Archer/Pool新hook真实FF25、旧Hermit短getter原字节保持，save/config/bank5709保持。

OMP Flash max两worker初轮write模式阻止shell，已恢复同session隔离执行并真跑；不采信未执行的测试声明。玩家17次错误无调用栈，三处风险整改不等于闪退根因已找到或闪退消失。真实拾弓/城堡修复/完整换岛读档/联机待验，doing；见tasks/destroy-lifecycle-20260914/acceptance.md。未提交发布、未改用户存档或Mono/D Steam。

## 2026-09-14 — 赫尔墨斯转化随机跨世界头饰

已实现并仅安装E独立副本46AD1CF9/build6.1.5-hermes-headwear-20260914。F5战斗默认on，30%新转化随机五世界30面具+14周年头饰；未中保留原样，纯视觉，不改MaskIndex/tough/战斗。GUID JSON扩展稳定保存，Save/GetID同快照桥接，组件尾部与注册后追加RPC由主机同步，主客需同版。原帽消失未确认根因，本次不宣称修复。

OMP Flash max双worker实现、内置reviewer审查；55core+27visual+原24套回归通过，0W0E、337API、旧1661/1666方法完全不变。实际44资源/42原生JSON内存往返及112秒精确候选运行通过；Save/GetID/SpawnMask均FF25无fallback。构造器hook曾被IL2CPP后台拒绝，已撤销，失败候选2F47未最终安装。save/config/bank5709保持、probe移除，无提交发布或Mono/D Steam改动。

实际法杖遭遇、动画遮挡、保存重读换岛和两机一致性仍待，harness保持doing。离屏相机图全黑不作证明；既有盾牌NRE仍在。完整证据见tasks/hermes-headwear-20260914/acceptance.md。

## 2026-09-14 — 弓箭散射、射速与命中特效

已实现并仅安装正确E独立测试副本：1F111CD5 / build=6.1.5-archer-options-20260914。

F5「弓箭」页，三个独立默认off开关：散射总箭数1–5（含原生主箭，默认3）；射速1–2倍（默认1.5、步长0.25）；纯视觉火焰命中特效。按已说明假设全部世界可手动启用；银行和原缩放仍Greek-only。战斗修改由单机/主机权威执行；特效当前仅单机/主机画面，不含新客机FX RPC。大规模齐射可能因预算少发额外箭，不删除/挪动原生箭。

参考作者DLL仅Mono.Cecil只读分析；没有执行或安装该DLL。两个实现worker的provider-native session均核验deepseek/deepseek-v4-flash max，各仅改自己的新production文件，canonical由root整合；root最后修复异常回执ID读取保留、spawn后立刻登记及缺body保留ledger，新增3项回归。内置reviewer完成多轮独立code/native复核，最终要求已落实。

24套测试通过：既有21套、combat75、visual36、实际scope27。完整IL2CPP强制Rebuild0警告0错误；278实际Unity可达方法无unstripping失败。1580个旧方法中1575完全不变，5处变化仅Plugin标记、ModConfig.Init、ModPanel.Update/DrawControls/.cctor；无旧方法删除、全部旧Harmony属性保持。六个native目标均核对实际2.4唯一长入口，Shoot.MoveNext复用既有target，未新增短getter/Dispose钩子。实际Harmony PatchSorter次序+模拟normal/exception共3项通过（非.NET8 managed detour实测）。

精确候选受控启动PID27652约110秒，chainloader及RunningGame成功；隐士短getter17原字节保持。save 3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A保持，配置逐字节恢复，银行5709→5709。只见既有NpcShieldUser.SetShieldEnabled NRE，本任务无新错误。测试结束先恢复基线00FA75CA，再原子安装同一候选并核对源清单。未commit/push/更新公开版本，未写D盘Steam，不修改Mono，不回滚用户存档。

仍待真实玩法验证：主动开启后的散射轨迹与命中火焰观感、射速倍率手感/死地增益/弩手转职、密集弓箭手持续负载、换岛读档和两机同步。此次实机启动使用默认关闭的新开关，不能声称已完成以上正例；checklist保持doing。作者逐命中新建Mesh的实现改为16槽三层LineRenderer复用，视觉为同类效果复刻，并非逐像素一致。

证据：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/archer-options-20260914。build.txt、test-results/archer-combat.trx、clean-test-results.json、unity-audit.txt、native-methods.json、native-disassembly.txt、harmony-order.txt、worker-receipts.json、verification.json、permanent-receipt.json、installation.json、task.diff。早期独立.NET8进程尝试旧MonoMod managed detour导致CLR自检失败，未执行游戏/改变游戏状态；随后改为实际PatchSorter纯排序核验。最终游戏IL2CPP启动单独通过。


## 2026-09-14 — 银行与税收小队仅当前希腊

本机已安装2B27CCC0 / build=6.1.5-greek-bank-scope-20260914。银行工作参数、共享金库、税收小队与银行采购仅当前Greek；其他世界原版银行，自定义金库栏隐藏，日历保留。

银行32、真实助手/银行/采购直链17、采购96+新增2（旧代码2例扣100→96，修复后100不变）及其余14组共17组回归全过；IL2CPP构建0W0E；437实际Unity可达方法未见unstripping stub。1376旧方法中1314完全相同，62变化含6移除，57新增，最终1427；仅既有Banker.Update增加managed prefix，原生目标集合不变，其余Harmony属性一致。此前缩放核心/钱袋/隐士/鹿方法保持。独立内置reviewer最终无阻断。

精确DLL受控PID25924约110秒正常启动并有Running标志；Greek固定助手控制器解析成功，无新增错误，仍有既知NpcShieldUser盾牌NRE（独立遗留）。加载完成后原隐士短getter及既有生命周期入口核对通过。测试恢复旧DLL后安装同一候选；存档3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A、配置逐字节、金库5709→5709保持。

本轮实机证据为Greek启动与控制器注册，不冒称已经见证银行家存取款或真实其他世界往返；非Greek原版行为、联机主客切换和显示仍待正常游玩确认。未commit/push/更新公开6.1.5，不写D盘Steam。 见tasks/greek-bank-scope-20260914/acceptance.md。

## 2026-09-14 — 当前希腊世界限定缩放

自定义缩放统一按当前世界判断：希腊使用原设定，移植到希腊的其他世界角色同样生效；其他世界只归还本补丁实际写入前的原值，不统一强写1。未知加载期保留请求及所有权，关闭/重新开启、世界往返、池复用、暂停Mover、客户端样式同步、外部写入与异常重试均纳入处理。银行助手和弩矢模板中性、实例应用；钱袋按实例基线，金币仅在证明由本补丁容器缩放导致继承偏差时登记间接写入并恢复。

68核心/角色直链、9钱袋直链、22猫（含于13组既有回归）及其余12组既有回归全通过；build0W0E；实际358Unity方法未见unstripping stub；独立审查覆盖原26个setter方法。DLL旧1329方法中1293完全一致，35修改、1删除（旧全局钱袋基准产生的cctor）、48新增，合计1376；原生Harmony发现属性与目标不变。健康Mover事务值类型无每帧事务对象分配；失败缓存重试退避30帧。

精确DLL 494A879E 受控PID13972运行约112秒并进入正常运行，真实日志确认CurrencyBag原y=1→Greek y=2，弩矢生命周期成功注册。加载完成后核对原隐士getter17字节与4处已有原生入口；无新增错误，仍存在20次已知盾牌NRE。测试结束恢复原运行状态，再安装同一候选。存档SHA 3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A、配置逐字节、金库5709→5709保持。

跨世界往返和非1原值恢复已有生产直链回归及原生路径审查；本轮没有在真实游戏中建立幕府/北境新档并完成来回切换，也没有实际联机目视验收。当前希腊存档的角色种类有限，不把其日志外推为所有世界、所有单位实战均已验证。公开6.1.5仍旧包，本轮未commit/push。

用户在本轮末明确指定后续worker必须本机OMP DeepSeek Flash thinking=max，不使用内置subagent；已写入AGENTS及协作约定。前面的实现工作发生在该新指令之前；收到后没有新增内置worker。本机OMP18.1.19模型目录与实际session 01a09ba9-6757-7717-b142-ce068caa6f55确认provider=deepseek/model=deepseek-v4-flash/thinking=max，已用read/write限定工具完成本轮验收材料核查。若OMP后续故障，先诊断，不自动回退。

## 2026-09-14 — 希腊普通野鹿密度与补充速度3倍

按用户选择将希腊普通野鹿的生成密度调为3倍、实际补充间隔调为1/3，单只原生掉落3金币不变。只处理主机/单机当前gameLayer中、prefab含Deer且不是季节Critter/Steed/Hind的控制器；保留树林面积、冬季、最小区域、原生生成池/网络。参数仅在原Update调用中借用并恢复，外部接管不覆盖；恢复失败字段保留待恢复记录，下一次先清理再决定是否放大，避免9倍叠乘。

build0W0E；27组生产直链回归通过，时间/密度模型验证9秒原版3次/增强9次、容量10/30后停止；实际2.4资源确认Forest控制器关联掉3金币Deer，Update6c5990唯一长函数与World831b50父层级已审计；Unity API审计通过；1317个旧方法体完全一致，仅改Plugin构建日志并新增鹿生成补丁。独立源审通过。精确DLL受控PID26144自然运行约110秒并进入RunningGame，原隐士getter17原字节仍完整，人口控制器新入口已读回，无新增BepInEx Error（仍有20次已知盾牌NRE）。

E已安装4113B3DD / build=6.1.5-deer-population-20260913，存档/config保持，金库5709→5709。保留Critter原版大小与隐士闪退热修。公开6.1.5资产不变，未commit/push。

本次存档场景未出现普通鹿生成器的成功日志，无法宣称已观察到实机鹿数量增加；生成规则由实际原生契约与生产回归验证，正常进入有树林的区域后再观察。

## 2026-09-13 — 兔子等小动物恢复原版大小

按用户要求取消兔子等Critter小动物的y=1.8缩放：完整删除Critter.OnEnable补丁及缩放登记，由游戏原版控制大小，不强写1或添加新钩子。鹿0.55、工匠、猫、盾牌和隐士热修保留。编译0警告0错误；与已安装6A9A4546比较，1317个方法体完全一致，仅删除Critter方法与更新构建日志。E已安装92D4E207 / build=6.1.5-native-critters-20260913，存档/config/bank逐项保持。下次正常启动生效，本轮未启动游戏，未把静态验证冒称目视验收；未commit/push或更新公开6.1.5。

## 2026-09-13 — 隐士短函数闪退本机热修

本机已安装6.1.5-hermit-policy-20260913 / DLL 6A9A4546103390368BA0E7C20B69CA3265319B642D48AB5AB682D7B811E1BF04。完全移除隐士短getter detour，保留隐士独立敌方拾取策略保护。24专属回归（含16组临时世界恢复组合）+原有11组回归、编译0W0E、151实际Unity方法审计、1306原方法体不变与独立源审均通过。原scope丢Original的P2已修复。

精确最终DLL受控进程PID11300运行约150秒，正常进入Playing，没有重现80000003/+4f0752闪退；进程内短getter仍为完整原始17字节8B414C85C07503B001C383F8060F94C0C3，较长OnEnable/OnDisable入口字节已记录。存档SHA保持3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A，配置逐字节恢复，金库5709→5709。测试结束停止的仅Operator自启进程；随后安装同一哈希DLL，未启动或打断用户进程。

本次最终读档有20次已知SetShieldEnabled NRE，无其他BepInEx Error。该栈尚不能区分shield与parentHeader空引用，后续在已有长入口有限采样分类；不要新hook共享5字节HasShield getter。当前场景没有出现隐士保护成功日志，因此防绑架正例依据生产直链回归与实际字段契约，尚未完成隐士遭遇Troll的实战验证。150秒未复现不等于长期或联机稳定性验收。公开6.1.5资产仍不含此热修，未覆盖旧资产、commit或push。

## 2026-09-13 — 02:11真实闪退，暂缓本机升级

用户FFDD人数HUD构建在木马上升进入MtOlympus后退出。WER80000003+dump指向GameAssembly4f0752，位于被隐士防绑架detour修改的17字节CanBePickedUpByEnemy末尾；从+14错位执行的xchg esp,eax/rol bl/int3与寄存器签名高度吻合。独立review同意首嫌为回跳边界错误，但动态trampoline未捕获，尚未做单变量复现。新场景还有两次NpcShieldUser.SetShieldEnabled NRE，不能混为已确定致命点。6.1.5仍含相同钩子，本机安装暂缓；本轮仅只读取证，未改存档/金库/配置/Mod，也未启动游戏。见tasks/crash-20260913-0211/plan.md，本机详细DIAGNOSIS保留原始证据索引。

## 2026-09-13 — 6.1.5发布准备

用户授权发布当前人数HUD和剑风修复；版本/玩家文档同步中，clean源码与完整包验证后发布。运行中的用户游戏不打断。

## 2026-09-13 — 常驻职业与五世界骑士人数

默认左上角八职业与骑士总数/五风格，F5→人口独立开关，原生名册事件缓存与每秒活性/风格采样。30+81回归、build0W0E、177实际Unity API与独立review通过，原生截图确认中文与布局及22骑士五风格完整。E FFDD0E89 build=6.0.0-population-hud-20260913，临时截图代码已移除，save/config/bank恢复；公开6.0.0未更新。实际2.4客户端无权威不登记名册，因此只支持单机/主机，客机明确提示暂不可用。见tasks/population-hud-20260913/acceptance.md。

## 2026-09-13 — 剑风IL2CPP数组异常本地热修

已将SetPositions临时数组/Span路径改为13次SetPosition，保留原几何/复用/清理；完整构建、31回归、109实际Unity API与独立review通过。实际E游戏已完成13点SetPosition剑风构建，成功日志出现，无原异常。 E DLL 3399c131 build=6.0.0-wind-arc-interop-20260913；存档/config/bank原值恢复。公开6.0.0未覆盖，未commit/push；Griffin网络登记未定位，目视/联机仍待。见tasks/wind-arc-interop-20260913/acceptance.md。

## 2026-09-07 — 回冲共用前冲伤害，已本机部署

按用户新要求，前/回冲burst共用CanHit/HitScan；每motion同Damageable只命中一次，同帧多个敌正常受伤，普通跑步不伤害，伤害回调失效后不再操作旧动作。76case、独立review、0W/0E与60Unity方法审计通过，E DLL2F089BA8...已受控启动51.4秒并恢复场景，存档hash未变。dev build=4.5.0-samurai-return-hit-20260907，实战待反馈；希腊铁砧能力仅解释机制、未实施。

## 2026-09-07 — 幕府主动返队候选

用户选择先做幕府返队，混编小队推迟，希腊铁砧增益仅研究。候选采用单MotionLease互斥攻击/返队，>10触发<=4退出，正常距离单次7格/.6s保护回冲；极远跑步尾段有3s总截止，卡住.5s退出/2s退避/同目标3次失败封顶。首版ZCode的重复协程、无敌残留和共享扫描加频问题被测试拦下，未集成。备用源4E69DF...通过48case、0W/0E、3hook与58Unity方法审计及独立review。已部署E DLL53E9A1DE...并通过64.3秒受控启动/场景恢复，存档hash未变；实际战斗/联机仍待反馈。银行HUD/default候选保留，公开4.5.0包不变。

## 2026-09-06 — 时间条银行余额与人口默认值

HUD新增独立金色银行栏，同主面板读取主银行家存款，0.5秒缓存，未就绪显示—；布局扩为900逻辑像素，大额自动缩字，季节进度保留在日历区域。默认值统一120秒/4人，已有玩家配置不自动迁移；用户明确同意本机60/4同步120/4，已备份应用。ZCode实现/独立review PASS，构建0W/0E与175方法interop检查通过；已部署6C6BA423...并通过53.3秒受控加载，存档hash一致、无HUD绘制异常日志，真实视觉待确认。build=4.5.0-hudbank-20260906，公开4.5.0包保持发布时内容。

## 2026-09-06 — v4.5.0已正式发布

完整包已生成到release并发布为GitHub Latest：https://github.com/baisiqi6/ohmymods/releases/tag/v4.5.0。clean source commit `c2032185a5bc213f085a5831abedbbaacfab8b36`，ZIP SHA256 `658c68074e6a618e0eb78b4a8b6a7618b77bbddf71260dd90e25774f43539e06`，远端digest一致；E本机DLL为`aa4b1959c16e1dbbdcc5546437f559725e029ba4386b456a55ad6c5b09e3bd8a`。包内DLL已受控启动并恢复场景，存档哈希未变；源码版本/启动build戳一致，另外补了幕府关闭Mod后的回收清理门控。用户已反馈一般游玩正常；联机等专项检查继续doing。发布记录见tasks/release-450-20260906/publication.md。

## 2026-09-06 — v4.5.0发布准备

用户正常游玩反馈后授权发布当前版本为4.5。只更新版本元数据，保留8390AF已测候选全部游戏逻辑；同步完整玩家说明并将现有骑士/弩手回归移入tests。canonical构建0W/0E；日历95702断言、骑士27场景、弩手9场景1462断言通过。发布审核/clean-worktree包/远端digest核验待完成；未宣称全部联机已测。

## 2026-09-06 14:58 — 启动崩溃修复已部署并通过受控加载

- 实际 GameAssembly 审计确认新 Slash.Dispose 钩子与1098个空方法槽共用原生地址，包含启动回调；完全移除这处钩子的单变量构建越过原闪退位置。其余新增注册在Assembly-CSharp表内没有同类地址折叠。
- 永久修复用每骑士一个强引用lease，2秒scaled心跳用于允许接管；先终止旧枚举器再移除记录，__state.Retired覆盖原生body内OnDisable重入后的状态覆写。无扫描/计时器/释放历史环。ZCode两轮方案未通过复核且未集成，备用worker实现经独立review PASS。
- 候选与canonical源码一致、各自build0W/0E；27项managed回归通过，24条剩余新hook静态绑定核验通过。E部署保留已经实际测试的隔离构建DLL `8390AF2755151D52CEF31FD3DEBCDA79F11BC251543925C083D8B3E8D9B5D7E9`。50.6秒受控启动抵达RunningGame/场景恢复和ClockDiag，DeadlandsAnimObserver注册成功，无新崩溃转储；存档前后SHA256一致。最高采样私有提交2.34GB，最低系统剩余commit19.62GB。未做战斗/联机/UI视觉验收，功能checklist继续doing。
- 原骑士强化与弩手守位/塔射程均保留。此次修复有原生地址和启动对照证据；后续系统内存耗尽及cdd蓝屏仍不能宣布由此钩子单独导致。详见tasks/startup-crash-20260906/incident.md。公开release ZIP未更新，SteamD/G未写入。

## 2026-09-06 14:25 — 启动闪退后已回退，根因待查

- 用户实测今日候选启动闪退并报告后续蓝屏。3份用户转储/事件同coreclr.dll+0x1d1fdd访问冲突，首发14:13早于14:14项目检查PowerShell。14:18系统2004记录commit42.55/42.78GB、PowerShell8.28GB；随后0x3B/cdd蓝屏，不能混为单一已证实因果。
- E盘已原子回退A9B115D201889A56C045A14F859C03E1EB662374651334B723C562E0471CEC04（今日三骑士强化/弩手站位射程之前），故障DLL和日志保留；存档未动，未自动启动复现。此前两项“已部署”记录为历史，现候选撤回。canonical仍保留候选源码，禁止把直接build/deploy误当回退版。
- 独立复核无确定源码根因；Slash.Dispose原生地址共用/折叠是优先待证假设，managed wrapper检查不能证明原生detour安全。先验证回退版启动，再做隔离诊断；功能状态保持doing。详见tasks/startup-crash-20260906/incident.md。

## 2026-09-06 — 弩手守位与塔射程已部署，待实机

- 独立弩手夜間守墙稳定分散到墙内4..7，原生SetGoal与原5s/3s巡检统一规则；不再与8..18通用后拉争用。排除逃跑/骑士/编队/塔位/玩家操控/登船；窄领地边界收缩与停步推离纠偏已覆盖。
- 塔弩手射程按自身原值1.5倍（通常12→18），地面12及伤害/冷却/弩矢不变。进出塔、Strip、池禁用清理，双端支持；修复禁用先清塔标志导致scanner18残留。
- ZCode GLM-5.3 worker + operator集成 + 独立crossbow_defense_review通过；0W/0E，9场景1462断言、4个native事件hook和26个Unity可达方法检查通过。14:09 E测试副本双退出检查、备份、原子替换/哈希核验完成；DLL FA7D697B1A918322598ADF54DBED7FC224906489D0736F172A9BD551C7B5D52E，373760 bytes。实机站位/弹道改善、18范围射击和读档联机待验证，公开ZIP未更新。详见tasks/crossbow-defense-20260906/acceptance.md。

## 2026-09-06 — 三风格骑士强化已部署，待实机

- 中世纪射程/前向命中范围1.5倍与缓存短剑风；北境自身容量和留币阈值2倍（含神像，不赠币/不动玩家钱包）；死地骑士及当前随从攻击节奏2倍、移速1.5倍，叠加当前弩手参数。
- 两路ZCode GLM-5.3 worker、独立scout/回归subagent及ZCode最终复核。Operator修复首击剑风、对象池溢出容量、消失目标速度残留、动画外部改速覆盖；16组托管回归通过，canonical构建0W/0E，21个实际原生hook/176个Unity可达方法审计通过。
- 13:53双退出检查、备份和原子部署E副本完成，DLL SHA256=2109204D33FA011914B45ECCC8D807AA4E16DEA328B6561CB86C493CE684D7C6，369152 bytes。未修改公开ZIP或存档；原面板/HUD热修保留。实机伤害/动画/速度、换岛读档和联机仍待确认，checklist保持doing。详见tasks/knight-powers-20260906/acceptance.md。

## 2026-09-04 — v4.0.0 正式大版本发布门禁

- 2026-09-05 00:11 已部署“非北境随从误换北境外观”修复。游戏退出后从权威发布包提取 DLL，E 盘目标 SHA256=714B51D3B9532A897FCA6D6F6D0D37B8F5E6C7BA6307419D423FB71A300B1D00，旧版备份和原子替换回滚副本均核验；待用户重新启动后观察五种骑士风格随从是否恢复各自外观。

- 2026-09-05 用户实测发现非北境骑士随从被统一换成北境外观。E盘日志证据：withKnight=88、norse=84；根因是共享 `NpcShieldUser` 组件被误当作北境身份。修复改用原生对象池资产/实例指针核对，普通风格随从保持自身外观；独立 reviewer APPROVED，Debug 构建 0W/0E。游戏已退出，待部署和五种风格实机回归。

- 23:55 部署纠正：之前 E 盘仍为 06f98f9 构建，遗漏最终随从场景保护，旧“发布包与 E DLL 一致”记录不成立。用户退出后已从权威仓库最终发布包直接提取并原子部署 DLL，SHA256=1DB33FD50487AC392E17CE5E0CB121581B2B57C092F3B066A426D76D9A315C73；新备份、回滚副本与原 DLL 哈希一致。未重编译、未改包/tag/配置/存档；仅部署核验完成，待玩家启动实测。详见 tasks/release-400-20260904/deployment-final-20260904.md。

- 已发布：独立 reviewer 在补齐 Norse 加载恢复、KnightStyle 池复用和 TowerSpots 延迟上下文门禁后 APPROVED。源码 tag v4.0.0 指向 0b71a527；clean Debug build 0W/0E；313 项发布包和 E 盘 DLL 哈希已核对，备份保留。联机、权威迁移、换岛/读档与守家边界继续由用户实测。

- 用户明确指定本次为 v4.0.0，上一正式版为 v3.5.0；最初准备的 v3.5.1 编号已撤销，尚未创建对应标签或Release。历史开发记录保留原始时间/版本证据，不代表当前公开版本。
- 当前版本标识已统一至4.0.0，包含北境守家与加载恢复、弩炮弹速、塔基避障/局部高度、巨魔卸载清理、工具分配空候选放行、只读时钟诊断及农田币/银行余额/农舍猫增量。
- 已完成独立审查、禁部署构建、checklist 校验、干净 worktree 打包和最终哈希审计。存档、备份、反编译源码、旧 ZIP 和 worker-wt-20260901 均未加入 Git。
- 用户授权退出门禁后的E盘备份部署及path-scoped commit/push、v4.0.0 tag、GitHub Release Latest；G盘、Steam目录和存档不修改。联机、权威迁移、换岛/读档与守家边界持续验证，未逐项完成的事项不置done。

## 2026-08-31（二） — 盾墙雕像+农田币+银行余额（3.5.1-dev2 部署）

- shieldwall-totem-028 worker 交付：PayableBorder.Setup postfix 挂图腾（原生级联复刻，
  双保险幂等）+ TrySpawnShieldWall prefix 绕希腊 biome 门（方法体逐字复刻，其他 biome 零影响）
  + prefab 双兜底（Kingdom 字段→Resources 按名）+ 镜像豁免（GetFormation+IsShieldWall 精确
  判据）。interop 元数据 pwsh 反射全量复核。
- 农田币+银行余额 worker 交付（重大偏差修正）：农田币无专属 DropType（落 Wildlife 桶，与
  狩猎/宝箱/银行取款混装）——改精确来源标记法（Drop postfix 检查 dropper 归属 Farmland），
  12s 独立成熟期；发现并同步任务书遗漏的第三个过滤点+银行领域豁免；面板顶部实时银行存款。
- 已知待实测：shieldWalls fake-null 判活、希腊 Kingdom.prefab 序列化态、Instantiate<Formation>
  interop 泛型首调、联机。
- 编译 0W/0E，build=3.5.1-dev2 已部署 E 盘（SHA 前16=d2af6b2f27a1ce56）。

## 2026-08-31 — v3.5.0 发布（北境小队特色波）

- 用户拍板版本号 3.5.0（内容量超小版本、未到 v4.0，节奏均匀：3.0 大波→3.1 打磨→3.5 特色波）。
- 终验全过后发布：csproj/戳 3.5.0-release；文档五件套（worker 起草，含北境小队两条已知提醒：
  随从不还原、存量重掷一次）；包 313 项/39.4MB，commit 0115b9a，DLL SHA256 9f43d6e9…。
- GitHub Release v3.5.0 已创建并 Latest：https://github.com/baisiqi6/ohmymods/releases/tag/v3.5.0
- 交付：北境小队（五风格/真北境随从近战/1.15）、塔位增密2x、农舍猫1.2、碰撞永久关+白天
  散开、蛇远程吐怪修复、助手容量100+、扫描缓存、滑块步进。norse-squad-027/tower-spots-029 closed。
- 群公告：release/MOD_V3.5版本更新说明.txt 待用户贴群+传ZIP。

## 2026-08-29（七） — 农舍猫移植（3.2.0-dev7）

- 用户确认移植 Mono 线遗留的"北境猫"（IL2CPP 迁移时被落下的功能）。worker 交付
  PatchWorld_FarmCats.cs：北境 BiomeData 双机制解析 prefab；每农舍幂等补到 3 只驯化猫
  （domesticated/farmHouse interop 直写——元数据 dump 实证 setter 暴露）；坑26 反查计数
  （FindObjectsOfType<Cat>+farmHouse 匹配，不枚举 kingdom.cats）；原生 Persistent 存档语义
  + 幂等重放双保险；RegisterObject(Dynamic) 照原生池化配方；联机 fail-closed。
- 已知限制：会话中途新买农舍下次加载才补（与 Mono 版对齐）。
- 编译 0W/0E，build=3.2.0-dev7 已部署 E 盘（SHA 前16=47518350900f010b）。
- v3.2.0 七件套集齐待终验：北境小队/塔位增密/白天散开/碰撞永久关/扫描缓存/蛇远程吐怪/农舍猫。

## 2026-08-29（六） — 蛇缰绳回归修复：远程吐怪（3.2.0-dev6）

- 用户实锤夜间不出怪——排除滑块（探针佐证 arrows=0 早于拖拽）后定位：蛇+100 使吐怪门的
  IsAny(6步)恒false，最终岛夜怪主力（嘴部传送门波次）全灭。修：TryRemoteProximityWave
  复刻原生门去掉距离项（冷却门共用 _lastAttackTime 防叠加；嘴部门 activeInHierarchy 作
  状态代理；10s巡检拆5x2s子节拍匹配10s冷却默认值）。顺带：老滑块5%步进吸附。
- 坑31沉淀：挪单位审全部行为门的距离条件。
- 编译 0W/0E，build=3.2.0-dev6 已部署 E 盘（SHA 前16=1f66c17353e5f60a）。

## 2026-08-29（五） — 扫描缓存治理帧率尖刺（3.2.0-dev5）

- 用户实测 dev4：四项全过（塔位增密/无天弹/白天散开/北境）；新反馈帧率波动——探针数据
  avg 6.7-7.0ms（≈145fps）健康，max 37-56ms 单帧尖刺=体感不稳。元凶=5 套监督协程各自
  FindObjectsOfType 叠帧+GC。
- 修：UnitScanCache 共享缓存（Archer/Knight 3s、marker 5s、Serpent 10s，过期才真扫、
  同帧去重、世界边界 InvalidateAll 保守清空）；四消费文件接线+Operator 补 NorseSquad
  最后一处（一行）。RecomputeOnLoad 有意保持直扫（25%守恒需精确快照）。
- 编译 0W/0E，build=3.2.0-dev5 已部署 E 盘（SHA 前16=6f13930e00f4e79b）。
  待实测：拉高怪物数量后夜间 maxFrame 尖刺是否收窄。

## 2026-08-29（四） — tower-spots-029 箭塔基底增密（3.2.0-dev4）

- 用户需求"生成倍数"+确认现有世界即生效（场景资产点位非种子生成）。实现：读档+5s 按原生
  间距中位数/multiplier 补放 towerLocationPrefab（自包含 PayableUpgrade，零接线；原生塔毁
  回退=运行时实例化先例；注册 RegisterObject(SemiStatic) 与原生逐字等价；NotBuildable 点检；
  联机 fail-closed）。默认 2x cfg-only——用户拍板不进面板（已建塔虽不丢但空点会随存档棘轮，
  布局参数非运行时可调）。
- reviewer 拦下 P0：间距估计混入自产点位=每读档密度×2 到 3 步地板+外沿逐档外推（比配置棘轮
  更糟的自增强回路）。修：双层集合——参考集（估计/anchor/朝向）只收未购原生基底（name 非
  KEM+level==0），占用集全量防贴脸；守卫移到就绪检查后。验证判据：同一存档连读 3 次 added=0。
- 编译 0W/0E，build=3.2.0-dev4 已部署 E 盘（SHA 前16=1ac64d0f296d101a，含北境1.15/白天
  散开/碰撞永久关）。

## 2026-08-29（三） — 白天拥挤重分配（原生狩猎公式复用，3.2.0-dev2）

- 用户方案落地：碰撞永久关后无物理散开，白天密度过大时用原生站位分配散开——
  pairwise 重叠探测（同层1.5/半身位0.55，复活为散开触发器）命中贴叠的自由弓箭手
  → 按原生狩猎公式重掷（GetBorderSide+side×Random(borderHuntRange)，Archer.cs:597
  逐字复刻）→ SetGoal 走过去。随从/塔位/夜间规则不碰；20s/单位防抖；白天窗口互斥。
- worker 推演递归安全（SetGoal prefix 链白天放行）。编译 0W/0E，build=3.2.0-dev2
  已部署 E 盘（SHA 前16=5451a5e1b2bb18f9）。

## 2026-08-29（二） — 友军碰撞永久关闭（用户拍板：同源根治，去恢复分支）

- 用户连环追问点破过度设计：墙外推挤与白天天弹同源（友军物理碰撞），条件式恢复
  （worker 已落盘的 CrowdStillOverlapped 探测）仍是给"恢复动作"叠补丁+新增扫描成本。
- 拍板 always-off：ToggleFriendlyCollision 简化为一次性全层对 Ignore（任何时刻首拍执行，
  全局矩阵跨场景存活），删除恢复分支/拥挤探测/相关字段；夜间镜像与深定位保留为
  目标分配型残留的治理（非碰撞型）。代价=拥挤时单位视觉互穿（用户知情接受）。
- 顺带清理 worker 条件版残留引用。编译 0W/0E，build=3.2.0-dev1 已部署 E 盘
  （SHA 前16=2fa400a95371e870，含北境小队）。

## 2026-08-29 — v3.1.0 发布（版本线重划：攒批=3.1.0，北境=3.2.0）

- 用户拍板版本线：3.0.1 攒批概念取消，已验证批次发 v3.1.0，北境小队留 v3.2.0。
- 发布执行：release/3.1.0 工作树取 cdc3442（北境合并前快照）+版本号/戳+V3.1 文档五件套
  （worker 起草，含 Cerberus 不受坐骑滑块影响的核实标注）。包 313 项 39.4MB，
  commit f26e185，DLL SHA256 c8cd2070…。
- GitHub Release v3.1.0 已创建并 Latest：https://github.com/baisiqi6/ohmymods/releases/tag/v3.1.0
- 主线（agent/post-release-candidate，含北境 3.1.0-dev1）继续为 v3.2.0 线；北境五点实测
  为发布门槛；盾墙雕像 028 在其后。
- 群公告：release/MOD_V3.1版本更新说明.txt 待用户贴群。

## 2026-08-26（三） — norse-squad-027 北境小队完成（3.1.0-dev1 部署）

- 全链：worker初版（五风格/窗口技巧转化/三层装盾兜底/巡检）→ Operator接线
  （ReRegisterModPools希腊门控前）→ reviewer深审（窗口并发比Worker先例更窄/池恢复三路/
  联机主机侧先例/Persistent语义静态成立）→ 三修（Q1跨队回收北境皮helper+消费点收口：
  回收随从永不上弩手包/缩放回1.0；巡检per-archer try；装盾日志每世界一条）。
- 机制沉淀：近战钥匙=NpcShieldUser（无组件永远Ranged，Archer.cs:770早退）；程序化装盾
  SetShieldEnabled(true,0)无需商店；读档回队走ArcherData.PersistentLink不经盾门。
- 盾墙雕像立项 shieldwall-totem-028（用户指认PayableShieldWallActivator=北境1币守家雕像，
  与夜间目标镜像的豁免交互已记录）。北境小队留v3.1.0；3.0.1攒批仍待用户验收。
- 编译 0W/0E，build=3.1.0-dev1 已部署 E 盘（SHA 前16=916c79ceb63bf108）。

## 2026-08-26（二） — 3.0.1-dev4：神器/坐骑CD倍率滑块进控制面板

- 用户需求：两滑块进 Ctrl+F10 面板，最短缩到原版1/5。落地：ModConfig Cooldown 组两项
  （Staff 默认0.375=现状11.25s/Steed 默认1.0=原生），ModPanel 两组 HorizontalSlider
  （0.2~1.0，5%步进吸附+Approximately 防抖），SettingChanged 即时重算在场对象。
- 坐骑侧 worker 实证偏离：SteedAbility 在 2.4.0 无 OnEnable 声明（反射 interop 核实），
  硬挂会拖垮 PatchAll——改为基类 Activate 读取点前缀（12 个消费 CD 的派生类全调
  base.Activate，5 个不读 CD 的空实现天然不命中）；原生值按实例缓存幂等不叠乘；
  三头犬 GhostSteed 固定 profile 排除防叠乘。
- 神器侧：11.25 常量改 30×倍率（默认等价现状）。
- 编译 0W/0E，build=3.0.1-dev4 已部署 E 盘（SHA 前16=cc69e0d4e6c825f2）。

## 2026-08-26 — 3.0.1-dev 批次：幕府0.95/狗1.3/弩手靠后/死地锚点6.5/天亮碰撞缓冲

- v3.0.0 后调参与修复批次（均未发布，攒 v3.0.1）：幕府骑士 0.95（表{0.95,1.05,0.95,0.9}）；
  狗 y=1.3（Dog.OnEnable+Registry 试用值）；弩手夜间站位靠后（贴墙→ParabolaCast 擦墙强制
  高抛——用户实锤，IntegrityPass 巡检 depth<3.5 重定位 4~8 步深处，塔位豁免）；死地随从锚点
  4.2→6.5（弩手射程12站深不打折，普通随从维持4.2 保射程8，per-style API）；天亮碰撞恢复延迟
  到 8 点（夜间重叠堆叠+天亮瞬间恢复=物理弹开人山/骑头顶——用户实锤，晨间散场缓冲 2.5h）。
- build=3.0.1-dev3 已部署 E 盘（SHA 前16=c5c804d810e418e0）。

## 2026-08-25（十三） — V3.0.0 发布

- 用户终验通过（"可以了，好像没什么问题了"）。打包 313 项/39.4MB，泄漏检查修正
  （Mono.Cecil.Pdb.dll 误杀），BUILD-MANIFEST commit=42016b6，DLL SHA256
  =2008184b727adf5f…（与 E 盘部署一致）。
- GitHub Release v3.0.0 已创建并标记 Latest：
  https://github.com/baisiqi6/ohmymods/releases/tag/v3.0.0
- 交付内容：弩手（3:1/死地士兵皮+旗帜色/伤2程12间隔x2体型1.15/平直快弹+拖尾/死地随从弩手化）、
  骑士四风格（确定性哈希/随从联动翻牌治理/体型表0.95-1.05-1.0-0.9）、昼夜布阵（白天独占踱步/
  夜间紧凑/墙外三因三治=关碰撞全层对+目标镜像塔位豁免+深定位兜底）、蛇缰绳墙+60、
  巨魔反制5%/法杖11.25s。
- 群公告：release/MOD_V3版本更新说明.txt 待用户贴群。
- 后续队列：knight-squad-023（需用户5规则确认）/samurai战斗特化/norse-squad-027/错峰齐射。

## 2026-08-25（十二） — 塔位天上走+碰撞层全对三修（发版前最后严重 bug）

- 用户实锤"箭塔弓箭手在天上走"：塔守位 x 落墙外窄带，目标镜像把塔上弓箭手目标改写成
  墙内地面 x，mover 在塔高度横走。修：镜像+深定位双加 inGuardSlot/y>2.5 豁免。
- 诊断立功：colliders=[10,17]——友军碰撞体跨两层，只关 10-10 留 17 层推挤。修：
  ToggleFriendlyCollision 升级为采样层集合全对 Ignore（自对+互对），日志列全部层对。
- 编译 0W/0E，build=3.0.0-release 部署 E 盘（SHA 前16=2008184b727adf5f）。

## 2026-08-25（十一） — 夜间墙外三因齐治 + V3.0.0 待发版

- collision off(layer=10)后 side=R 仍 outside=15@墙+1.5~2.0 窄带（side=L 清零）——非推挤，
  是原生守位目标分配在贴墙外。目标镜像落地：SetGoal(float,float) prefix 类型分发
  （骑士白天散布/弓箭手夜间镜像），窄带目标镜像到墙内0.7~3.0步；worker 纠正任务书符号错
  （墙内=wall−side×depth）。层自检诊断（collider layers distinct 列表）待数据。
- 夜间墙外三因三治：推挤→关碰撞；分配墙外→目标镜像；滞留→深定位兜底。
- 玩家文档五件套完成并入库；打包脚本路径修正（release-notes 在仓库根）。
- 最终构建 build=3.0.0-release 部署 E 盘（SHA 前16=9850dfee518dec60），待用户终验后打包发版。

## 2026-08-25（十） — 夜间关闭友军碰撞（用户治本方案）落地

- 用户提出"取消/调高密度上限"——实锤游戏无此数值，友军互挤本质=同层 2D 物理碰撞。
  落地 ToggleFriendlyCollision：入夜 IgnoreLayerCollision(units层,self,on)（层值运行时
  采样自 active Archer，代码不写死），黎明恢复。状态跨世界不复位（全局矩阵跨场景存活，
  per-world 复位会永久关死——worker 自查出的坑）。代价=夜间拥挤视觉重叠（用户知情）。
- 三层防线成型：关碰撞（治本）→ 编队锚点归位（随从）→ 深处重定位（兜底，退化为稀有 no-op）。
- 编译 0W/0E，build=3.0.0-release 重新部署 E 盘（SHA 前16=2126acb988bafd1b）。

## 2026-08-25（九） — 瞬移拉锯治本（深处重定位）+ v3.0.0 版本切换

- 用户实锤抽象 bug：硬地板瞬移钳回 vs 推挤密度上限挤出 = "瞬移回来又走出去均匀分布"。
  治本：Part2 瞬移删除，改 mover.SetGoal(wall−side×Random(8,18), walkSpeed) 深处重定位
  ——弓箭手自行步行进墙内深处（低密度不再被挤）；原生守位拉回再被挤则 3s 循环缓步
  （走动观感，预期兜底）。教训沉淀：与原生推挤系统打架要用"目标"而非"位置"。
- 版本切换：csproj 3.0.0、构建戳 3.0.0-release（用户拍板 v3.0.0 一次发，两头部功能
  对标 v1→v2 跨度）；打包脚本入库 scripts/package_release.py（带泄漏检查+MANIFEST，
  不再用临时目录）。玩家文档 worker 起草中。
- 编译 0W/0E，build=3.0.0-release 已部署 E 盘（SHA 前16=f71a4570b46dc530）。

## 2026-08-25（八） — knightstyle11：死地随从弩手化+夜间硬地板+弩矢观感

- 死地随从弩手化：无标记轻量包（SO/射程12/间隔×2/1.15），ApplyFollowerSkinTo 统一路径
  消费，换队/离队 Restore 幂等；worker 自查堵两洞（真弩手上塔误拆包→IsCrossbowman 防御；
  死地世界皮判重饿死包→巡检按风格独立调用）。
- 夜间硬地板：随从清零后普通弓箭手仍被推挤挤出墙外——NightParkedFollowerSweep 扩全单位
  （随从优先重发跟队 continue，其余 depth<-0.5 钳回墙内0.6步，_guardSide 中性按近墙侧判）。
- 弩矢观感（用户实锤弩矢与箭无区别；平直失败真凶=ParabolaCast 被自家墙挡选高抛解）：
  SO._arrowOriginOffset=(2.5,1.0) 出膛前移过墙沿→原生选低弹道解；初速×2（包络32/索敌12，
  站桩狙击旧行为）；弩矢 _alwaysDrawTrail+尾0.25s 常显拖尾+体型0.85。死地随从共享 SO 自动受益。
- 编译 0W/0E，build=2.2.0-knightstyle11 已部署 E 盘（SHA 前16=c9ec84848a8449f8）。

## 2026-08-25（七） — knightstyle10：随从换皮翻牌治本 + 死地骑士1.05

- knightstyle9 日志：墙外随从清零（两侧 outside=0，原31/24）；dayIndex 独占踱步运行；
  幕府之谜解——夜间 curTop=archer_soldier_greece×56+archer_soldier×20，56=dead28+shog8+gree20
  全停原生希腊皮：原生 ConvertToSoldier（跟队例程重入）每~10s 刷回世界皮 vs 我们5s重写=翻牌，
  视觉长期停留原生皮；中世纪幸存因基底 archer_soldier 不在刷回目标集合。
- 治本：Archer.ConvertToSoldier/ConvertToHunter 双 postfix（私有按名补丁）——原生转换同栈
  立即重涂风格皮（ApplyFollowerSkinTo 统一写入路径）；离队时 RemoveFromKnight 先置 _knight=null
  （源码927-938核实）故猎人皮正确保留。StyleFollowersByLookup 降级为5s兜底。
- 死地骑士缩放 0.95→1.05（用户拍板，Operator 直改常数：表 {0.95,1.05,1,0.9}）。
- 编译 0W/0E，build=2.2.0-knightstyle10 已部署 E 盘（SHA 前16=186eff6f22ddeebf）。

## 2026-08-25（六） — knightstyle9：每风格缩放+幕府诊断+夜间滞留纠偏

- 用户三项反馈落地：
  1) 每风格骑士缩放表：中世纪 0.95/死地幕府 1.0/希腊 0.9（表驱动，Strip 恒回 1）；
     中世纪随从 y=1.05（随从换队每轮幂等重算自动跟随新骑士风格，离队回 1，弩手 1.15 不碰）。
  2) 幕府随从未换皮：加双诊断——解析终态一次性快照（请求名=解析对象名，暴露重名/错配）
     + follower diag 追加 per-style 目标分布与当前控制器 top2。待数据定位。
  3) 夜间滞留墙外随从（怪物来才逃跑回来）：3s 巡检发现墙外随从重发原生跟队目标
     （Archer.cs:486 同参），走锚点钳制路径回墙内。读档滞留态 ≤3s 自愈。
- 编译 0W/0E，build=2.2.0-knightstyle9 已部署 E 盘（SHA 前16=7efb029fc58d2374）。

## 2026-08-25（五） — knightstyle8：夜间随从锚点拉回墙内 + 白天独占踱步位

- lineup 实锂数据：side=R 42 带内 followers=40 outside=24 样本 x=墙+3.0~3.6；骑士自身
  r3@0.6..r7@1.8 贴墙内侧——随从编队（前排≈锚点前4步）以贴墙骑士为锚，前排越墙。
  v2.1.0 压缩贴墙+19队满编放大了原生编队半宽的越界。
- 修1：Mover.SetGoal(GameObject,...) prefix——夜间 Archer+Formation+有骑士命中时，
  锚点拉回 wall−side×4.2（前排≈墙内2/后排≈墙内6，全在弓射程8内）；骑士 rank 不动。
- 修2：白天踱步改每骑士独占索引（dayIndex×1.2±0.5，两侧≈12步带）；worker 发现并修掉
  dayIndex=0 落点必在拦截带内的无限重入（_inDaySpreadRedirect 静态重入保护）。
- follower diag 夜间 styled 皮肤确认（44 跟队中士兵皮在身）。
- 编译 0W/0E，build=2.2.0-knightstyle8 已部署 E 盘（SHA 前16=7bf10080ea1ad1fc）。

## 2026-08-25（四） — knightstyle7：白天骑士散布（Assemble目标拦截）+回滚rank分治

- 好消息先记：knightstyle6 实测 styled=76/skippedFamily=0——随从队籍换皮确认生效。
- 白天聚堆根因更正：Knight.Assemble（Knight.cs:662-680）全员同点（banner+3 或 wall+4）±1
  随机、每10s重走——与 rank 无关。上一版 rank 昼夜分治基于错误假设，已回滚（且其白天
  铺开的 rank 会被黄昏墙前列队消费，破坏夜间紧凑）。
- 新方案：Mover.SetGoal(float,float) prefix——Knight 缓存判定（非骑士零影响）+ isDaytime +
  目标落 dayZone±1.6 带内才命中；newX=dayZone−side×rank×0.75±1（19骑士散在~5.25带内，
  原生10s重走自动变各自踱步）。夜间墙前目标出带不受影响。
- 追加：夜间弓箭手 lineup 诊断放宽（knightstyle6 整夜零输出——_guardSide 过滤太严；
  改纯位置归属[-6,10]，≥5触发）。
- 编译 0W/0E，build=2.2.0-knightstyle7 已部署 E 盘（SHA 前16=60133c8487098cc1）。

## 2026-08-25（三） — knightstyle6：随从换皮改队籍判定（diag 实锤断点修复）

- follower diag 硬数据：archers=131 withKnight=76 inStates=76 styled=0 skippedFamily=76，
  样本 controller=archer_greece——原生随从白天分散时穿猎人皮，"当前∈士兵族才写"条件
  永不命中。worker 改为队籍判定：_knight 指向已风格化骑士即覆盖（猎人皮/士兵皮/北境款
  一律），离队原生 ConvertToHunter 自动恢复。IsSoldierFamilyController 删除，北境款解析
  保留无消费点。diag 行结构保留，skippedFamily 语义更新为幂等跳过计数。
- 人口注意：131 弓箭手/19 骑士满编 76 随从。knight spread 日志确认触发（count=19），
  用户"没散开"观察时间待确认（夜间压缩态=正确行为；白天为 1..19 单列纵队）。
- 编译 0W/0E，build=2.2.0-knightstyle6 已部署 E 盘（SHA 前16=3d8518f0779094b3）。

## 2026-08-25（二） — knightstyle5：骑士 rank 昼夜分治（修白天聚堆）

- 用户实锤：骑士白天聚堆看不到随从。根因：GetTargetPos=守位−side×(_distanceFromWall×rank)
  （Knight.cs:656），白天 ShouldAssemble（isDaytime&&isSafe，Knight.cs:950）就按 rank 集合；
  v2.1.0 紧凑列队每 3s 不分昼夜压缩到 7 档，19 骑士 3 个一叠。
- worker 修：RemapKnightRanks 昼夜分治——夜间（17.5-5.5）压缩 1..7（v2.1.0 语义不变），
  白天 SpreadSide 按 instanceID 稳定排序铺开 1..N 全宽（每人独立档，幂等无抖动）。
- 编译 0W/0E，build=2.2.0-knightstyle5 已部署 E 盘（SHA 前16=72c651970ffc0a58）。
  预期日志：白天 "knight ranks spread for daytime: count=N"，黄昏后 "compressed to cap=7"。

## 2026-08-25 — knightstyle4：随从管线+夜间站位双诊断部署

- knightstyle3 实测：随从反查零报错（枚举器问题终结）但用户观感仍未换皮；另报新问题
  "守家时部分弓箭手站在城墙外"+"白天也拥挤"。夜间探针抓到 287ms 单帧尖刺（t=0.5h，
  均值6.7ms）与"卡挺久"量级吻合。
- worker 加双诊断（只记录不改行为）：[KnightStyle] follower diag 每60s输出管线计数
  （archers/withKnight/inStates/styled/skippedFamily/skippedOther+样本控制器名）；
  [DefenseSpacing] archer lineup 夜间每侧一次输出墙外弓箭手按身份分类
  （xbow/followers/plain）+坐标采样。下次运行三问题一次定位。
- 编译 0W/0E，build=2.2.0-knightstyle4 已部署 E 盘（SHA 前16=59565a71d56a0fc4）。

## 2026-08-24（十三） — knightstyle3：随从反查重写 + 天亮顿挫排查

- knightstyle2 实测随从仍失效：堆栈实锤纯读快照段的 HashSet Enumerator.MoveNext 就抛
  InvalidOperationException——Il2Cpp 非泛型枚举器对 HashSet 运行时不可靠（reviewer 疑点成真），
  非写入副作用问题。worker 重写：删除全部枚举器代码，StyleFollowersByLookup 用
  FindObjectsOfType<Archer> + archer._knight 反向归属（不碰 _archers），IntegrityPass 两段化。
- 天亮卡顿排查（用户报告"每天天刚亮卡挺久"）：mod 无任何天亮触发逻辑（三监督协程均固定
  5/10s 节奏）；Player.log 的 83688 资产大卸载（85ms）在读档时刻非每日；首要嫌疑=原生每日
  自动存档（序列化+gzip 整个世界），被地图扩展与大人口放大。dawn 帧率探针已由 worker 加入
  PatchPerformance_NightVolley.cs（5.5-7.0h 独立累计器，[DefensePerf] dawn: 行），下次运行量化。
- 编译 0W/0E，build=2.2.0-knightstyle3 已部署 E 盘（SHA 前16=48440425768e31cb）。

## 2026-08-24（十二） — knightstyle2：随从联动枚举并发修复 + 北境小队立项

- knightstyle1 实测：骑士四风格生效（19 只 styled），随从全灭——根因=枚举 _archers 循环内写
  控制器触发原生重入改集合，HashSet 版本检查抛 InvalidOperationException（LogErrorOnce 去重
  后每轮静默失败）。worker 修复：StyleFollowers 两段式快照（纯读枚举收集托管 List 再写入，
  范式对齐原生 Knight.cs:312 的 new List 快照）。
- 北境裁决（用户拍板）：不进四风格随机池，作独立任务 norse-squad-027 完整引入——北境随从是
  预制体级差异（Archer_norselands 带 NpcShieldUser 盾牌组件+近战逻辑），需走跨生物群系替换
  基建（Holder+同步池+坑14全链审计），先例=希腊 Worker/Peasant 换北境预制体。
- 编译 0W/0E，build=2.2.0-knightstyle2 已部署 E 盘（SHA 前16=86940f886cfbae3a）。

## 2026-08-24（十一） — knight-style-026 骑士随机风格（knightstyle1 部署）

- 需求：招募骑士随机中世纪/死地/幕府/希腊四形象，随从士兵联动对应形象，希腊骑士 y=0.9；
  存量骑士读档自动补风格（用户确认）。
- 全程 subagent 协作通道：worker 初版 764 行（Squire 过滤/HashSet 非泛型枚举器绕法/
  NetID 退化+收敛/Knight.OnEnable 字符串补丁兜池复用）→ reviewer 一审 must-fix 一项：
  北境随从联动静默失效（原生士兵皮 archer_soldier_norselands 不在判定集）→ worker 二轮
  800 行（北境款"只识别不选中"/重算分支补 StyleFollowers/注释修正）。
- reviewer 关键背书：弩手互斥三层闭合（IsAvailableForJob 排除→无 marker→巡检不碰）；
  哈希收敛时序源码实锤（Pool.AttemptSpawnSync 先 Register 后 SetActivate，单机/双端首
  1-2 轮即 NetID 真值）；希腊 0.9 上船与船 scale 归一化无冲突。
- 编译 0W/0E，build=2.2.0-knightstyle1 已部署 E 盘（SHA 前16=66c307e2d27d41ca）。

## 2026-08-24（十） — 协作流程纠偏 + 直写增量补审收口（balance2）

- 用户指出流程漂移：皮肤/弹道/缩放/蛇缰绳迭代/平衡调整由 Operator 直写，未走 worker/reviewer。
  纠偏：worker 与 reviewer 改用本 agent subagent（GLM max）通道。
- 补审：reviewer subagent 对全部直写增量交叉审核 = approve（0 must-fix）。关键佐证：
  State 常量表补全（diag state=7=Stunned，{1,2}=Idle/Moving 硬编码正确且失败开放）；
  ConvertToHunter 权威端无条件重掷衣色（Strip 不还原衣色的污染面≤原生同型）；
  Range=v²/g 与双门控自洽；borderIntact 语义、坑11/25 合规均逐行核对。
- worker subagent 落地三项加固：LeashBodyToAnchor 加 !IsAny() 行为判据（状态编号无关双保险）、
  陈旧注释统一墙+60 口径、worldRight 钳制 catch 一次性警告；弩矢乘数 1.2247→1.224745。
- 编译 0W/0E，build=2.2.0-balance2 已部署 E 盘。

## 2026-08-24（九） — balance1：巨魔反制 10%→5% + 法杖冷却减半

- 友好巨魔被反制概率：DesignateFromStableIdentity 的 hash % 10 == 0 → % 20 == 0（确定性
  判定，长期≈5%），头注释同步。
- 神器权杖冷却：EnhancedCooldownSeconds 22.5 → 11.25（用户拍板"现行减半"），关闭 mod
  恢复原版 30 秒逻辑不变。
- 编译 0W/0E，build=2.2.0-balance1 已部署 E 盘（SHA 前16=3ffc851b135ebcd1，含 serpent6
  蛇墙+60）。

## 2026-08-24（八） — serpent6：大蛇推到墙+60（问题定性修正）

- serpent5 日志：锚点 159.1/蛇体 164.9/状态值 7 —— 缰绳全部生效。用户澄清真问题：
  不是蛇离墙视觉近，是白天狩猎小兵游走进蛇的攻击圈。定性从"离墙距离"改为
  "离狩猎活动范围距离"，拍板墙+60（蛇警戒 6+咬击 8 只覆盖墙+46 外，狩猎不走那么远；
  世界右界 336 余量充足）。
- serpent6 = 常量 60，编译 0W/0E；游戏运行中，退出哨兵自动部署。

## 2026-08-24（七） — serpent5：大蛇30+弩手缩放漂移诊断+Player.log报错诊断

- 用户反馈：大蛇仍偏近（拍板墙+30）；地面弩手"有的高有的低"（塔位假人缩放不需要，已撤销
  ——那轮只编译未部署）。
- Player.log 报错诊断（BepInEx 日志零错误，报错全在 Unity Player.log）：
  1) Curl error 7 连 127.0.0.1:7890 —— 游戏遥测/PlayFab 走系统代理（代理没开），无害；
  2) Game:Awake 的 LogErrorFormat 打的是版本信息（2.4.0 自己用 error 级别打 info），无害；
  3) Invalid NetID from CRPCStamp on 'CastleShieldShop(Clone)' x6 —— 狂战士商店槽位改写的
     存档恢复戳校验抱怨（TryPopObjectsToScene），商店功能正常，历史已知权衡，跟踪不阻断。
- 弩手高度不一致：日志零错误+Apply全走完 → 漂移必有更晚写入者。怀疑动画器 scale 曲线
  （评估晚于 Mover.Update postfix 守卫，守卫永远输）。IntegrityPass 加只统计不改的漂移诊断
  （drifted 计数+样本动画器位置/控制器名），下轮实测定位后决定改子节点缩放还是别的方案。
- 额外收获：DefensePerf 血月数据到手 arrows=39 avgFrame=10.2ms maxFrame=56.3ms t=3.4h
  （staggered-volley-024 决策输入，avg健康、max尖刺支持分批方向）。
- serpent5 编译0W/0E；游戏运行中，部署哨兵等待退出（stamp=2.2.0-serpent5）。

## 2026-08-24（六） — 大蛇缰绳 serpent2 实测返修（serpent3）

- 用户实测：蛇仍在墙边。日志实锤 [SerpentLeash] anchor pushed wall=129.1 anchor=143.1
  worldRight=4.78e19 —— 锚点确实推了，但两个问题：
  1) worldBounds.right 垃圾值：Sided<float> 泛型结构体经 interop marshal 损坏（坑26候选），
     F1 钳制失效。改用 GroundCollider 复刻原生公式 ground.x+size.x/2-8（World.cs OnLevelLoaded）。
  2) 读档蛇保留存档位置（fromSave 不瞬移），原生回巢=Moving 慢速爬行（10s+ 窗口），
     用户看到的就是爬行窗口里的蛇。新增 LeashBodyToAnchor：休息态（fsm.Current∈{Idle=1,
     Moving=2}，State 私有嵌套类按源码数值比较）且位置<目标位时直接 RepX 归位——与原生
     UpdatePosition 的 transform.x 写法等价；充电/攻击/下潜不碰。supervisor 改立即首扫。
- 编译0W/0E，build=2.2.0-serpent3 已部署 E 盘（SHA 前16=d7275fcc481decda）。

## 2026-08-24（五） — 大蛇缰绳 reviewer 收口（F1+F2 落地，serpent2）

- reviewer(kimi-k3)一审 must-fix 两项（核心随迁论证1-5全部逐条背书）：
  F1 targetX 缺上界——墙+14可越可玩陆域右界，破坏 Submerged 跟随点不变量与头部弱点可达
  → 钳制 min(墙+14, worldBounds.right-10)，日志补记 worldRight；
  F2 OnEnable postfix 首次激活空转——_mtOlympusGate 懒加载（TryFindGate 私有，
  仅 DistanceToGate/GatePosition getter 填充），蛇先瞬移到未右移锚点再慢爬10-20s
  → 复刻懒加载（FindWithTag+GetComponent 写回字段，私有方法不进 interop 坑25先例），
  附带消除客户端木马锁 UI 分歧窗口。
- nit 顺手：新世界重置 _loggedLeash；注释补"墙毁不回拉有意为之"。
- 编译0W/0E，build=2.2.0-serpent2 已部署 E 盘（SHA 前16=099558ffde986292）。
  git push 因本机代理(127.0.0.1:7890)不通暂缓，代理恢复后补推。

## 2026-08-24（四） — 大蛇离墙缰绳（用户需求：最终岛大蛇刷新太贴墙）

- 问题：希腊最终岛城墙推近奥林匹斯山门后，大蛇休息位=SerpentAnchor 落在墙外很近处，白天墙边
  小兵被它的警戒扫描(warn=6)/咬击(ShouldAttack→DynamicTargetChomp)覆盖。
- 侦查实锤：休息位由锚点唯一决定（关卡加载瞬移+Moving回巢 SetGoal 都指向锚点）；原生已有
  冲锋线限制 GetMinChargePositionX=max(锚点-0.55,墙+_minChargeTargetDistanceFromBorder(4)+warn(6))，
  但墙近山门时锚点本身就是贴墙的，调该字段无效。
- 修法：PatchWorld_SerpentLeash.cs——锚点右推到 墙+14（只向右幂等；OnEnable postfix 即时 +
  10s 协程复扫应对城墙右扩）。随迁论证：冲锋线锚点项主导→扫描只够到墙+8，墙边白天安全，主动
  推进部队照常触发冲锋；IsBlockingGate=蛇与锚点相对距离(<=8)不变；弱点锚点按 worldBounds
  均分与蛇锚点无关。
- build=2.2.0-serpent1 已部署 E 盘（SHA 前16=18bb7b3f2eb31d9f）。reviewer 审核中+游戏内待实测。

## 2026-08-24（三） — 弩手本体缩放 y×1.15（用户拍板）

- Apply：y 绝对值 1.15 + ScaleRegistryHolder.Register（复用现有 Mover.Update postfix 每帧
  y 守卫，坑11 只动 y）；Strip：先 Unregister 再回 y=1（顺序反了会被守卫顶回；注册按
  gameObject ID 键控，池复用不撤会把普通弓箭手错误守卫在 1.15）。
- build=2.2.0-xbow4 已部署 E 盘（SHA 前16=3f38ad4d49922630）。

## 2026-08-24（二） — 弩手弹道二次简化：只做 1.5 倍射程（用户拍板）

- 用户实测反馈高抛线 + 拍板简化：放弃平直弹道改造。实锤原因：守城时弩手在墙后，原生
  BestShot 的 ParabolaCast 避障发现直线路径被自家墙挡住会主动选高抛解越墙——平直参数
  在主场景（守城）根本展示不出来。
- 最终弹道：射程×1.5 = 初速×√1.5（Range=v²/g），重力/prefab gravityScale 全部原生不动；
  弹道形状与普通弓箭一致，SO 内部 Range=12 与 shootRange/扫描器自然一致（原 Range≈36
  站桩狙击的特设逻辑随之消失，更原生）。其余不变（伤害2/冷却×2/士兵皮肤/旗帜色/弩矢外观）。
- build=2.2.0-xbow3 已部署 E 盘（SHA 前16=028514ec48af0704）。游戏内验收仍待实测。

## 2026-08-24 — 弩手皮肤修正：死地士兵（用户指正）

- 用户指正：弩手皮肤应为死地骑士小队随从（士兵姿态），非死地猎人。侦查实锤原生机制：Archer.ConvertToSoldier
  （入队/EnterGuardSlot上塔/OnEmbarkStart上船三路调用）= 动画控制器换成 soldierAnimator 的 biome 换皮
  + 权威端旗帜色染衣（CoatOfArms 主/副色）；ConvertToHunter（离队/下塔/死亡清理）反向。资产确认
  archer_soldier_deadlands 控制器+全套士兵动画（idle/walk/run/shoot/shoot_prep）在 2.4.0 resources.assets。
- 冲突核实结论（答用户"白天打猎会不会冲突"）：不冲突——行为（_knight==null 猎人例程）与控制器（外观）解耦，
  原生塔上弓箭手就是"猎人行为+士兵皮肤"；两套控制器由同一 Archer.cs 驱动，触发器接口一致。
- 实现：常量改 archer_soldier_deadlands；Apply/IntegrityPass 加 ApplyBannerColors（复刻原生染衣块，
  直接写 outfitColor 属性带 spriteFX 刷新，_isWearingBannerColor 幂等标记与原生共用）；Strip 改走原生
  biome swap（GetAssetSwapForThis(hunterAnimator)，顺带修掉跨世界恢复错控制器的隐患）。
- build=2.2.0-xbow2 已部署 E 盘（SHA 前16=7b4201b19b340679）。游戏内验收仍待实测。

## 2026-08-23（夜） — crossbowman-021 弩手实现完成（编译+review 通过，待实机）

- V2.x ①号任务开工：Operator 侦查实锤全部挂点（Promote(DroppableTool) postfix / ActiveArrowAttack 可写但四路重置 / ArrowAttack 共享 SO 必须 clone / Range=v²/g 三参数不独立 / 索敌=shootRange 扫描器+SO Range 双门控 / 伤害在 Arrow prefab hitDamage / Bolt 非 Arrow 子类仅取外观 / IsAvailableForJob=骑士招募排除点）。
- Operator 数值裁决：保留用户弹道参数（初速×1.5/重力×0.5→SO Range≈36 站桩狙击不冒进），交战距离 12 由 shootRange 硬约束——弹道观感与射程解耦。
- 协作：OMP worker（deepseek-v4-flash）3 轮（初版 766 行→冷却膨胀/塔位扫描器/弩矢缩小 3 修→reviewer 必修 3 项），OMP reviewer（kimi-k3）2 轮 must-fix→收口。轮次要点：
  1) worker 修复：Apply 幂等（already 门防间隔×2 叠加）、Strip 塔位恢复 towerShootRange、弩矢 0.65 缩放；
  2) reviewer 阻断：CrossbowmanMarker 缺 ClassInjector 显式注册（任务书"自动注册"说法是我的错误，9/9 先例全显式注册）→ EnsureMarkerRegistered 前置到全部 5 接触点；Q1 syncID 30130 段会被 Castle 爬升分配器撞车→31000；Q2 读档重算会把骑士小队成员转弩手→跳过 _knight!=null（不计分母）；
  3) reviewer 二轮再拦一处漏网（RecomputeOnLoad 的 GetComponent 早于注册，读档主路径全静默中止）→ Operator 直接补一行收口（reviewer 预授权），坑 25 沉淀。
- 交付：il2cpp/PatchRoles_Crossbowman.cs（824 行），编译 0W/0E，build=2.2.0-xbow1，已部署 E 盘测试副本（DLL SHA-256 前16=d1e4895ce4c0690e，哈希核对一致）。
- 待实机验收：4 出 1 弩（deadlands 皮肤）/射程 12 平直快弹/伤害 2/骑士不招募/读档 25% 守恒日志/骑士小队成员不转弩。

## 2026-08-23 — V2.1.0 发布：骑士小队夜战紧凑列队

- 问题与实测：骑士夜晚墙后列队深度=rank×1.0（r15@15步实锤），后排小队侍从在射程8外整晚划水；弓箭手在2.4.0已被官方紧凑化（s=0.11实测，无需干预）。
- 排障长跑：2.4.0的GetWallTargetPos/GetTargetPos为死代码、Director.Update钩不住（AOT内联，性能探针同因无声）；最终模式=World.OnLevelLoaded协程宿主+字段直写（rank重映射1..7，幂等守卫防抖）。
- reviewer两轮：首轮changes_requested（remap被一次性日志标志锁死→自然成长/雇佣损员/换岛三场景静默失效；跨世界标志不重置）——用户验证场景恰好掩盖了此bug；修复后approved（每拍幂等remap+maxRank守卫+HasWorldAuth门控+新世界重置五标志）。
- 发布物：v2.1.0 tag+Latest，DLL 217,600字节 SHA-256=EA156F87…，build=2.1.0-release2，manifest commit 1037a04；四份玩家文档+群公告（MOD_V2.1版本更新说明.txt）；E盘同步。V2.0.0本地包曾被打包脚本误覆盖，已从GitHub资产还原。

## 2026-08-22 — V2.0.0 正式发布

- 发布内容：弩箭塔+火焰塔重建（火焰塔门控=燃料满+无工匠）、Cerberus四队亡灵（含边界驻守修复）、
  银行助手链式顺吸+积压扩容、旗帜小船编队、小船恢复、忍者伏击/狂战士进阶/隐士防绑架转正、
  主船扩容、巨魔平衡、性能优化全套。
- 发布门禁：未验证的队列深度监督器主动摘除（PatchWorld_DefenseSpacing.cs 移除，保留历史待V2.x）；
  reviewer 发布审查 APPROVED（六项PASS）；构建号 build=2.0.0-release。
- 发布物：ZIP 312项泄漏检查CLEAN，DLL 211,968 bytes SHA-256=
  `B92601D9109D27B389E3C70AF021531F9D48DCE90679245AC5F8ED84A7B4AA78`，manifest commit 66c3017。
  四份玩家文档补入火焰塔重建。E盘测试副本同步发布版DLL。
- 幽灵驻守获实机日志确认（holding多例）；弩箭塔重建获用户全流程确认。
- 队列紧凑化（弓箭手排队过深）未进本版：2.4.0站位重构为守位字段制，深度字段直写方案待心跳数据验证，
  连同错峰射击设计一起排入V2.x。

## 2026-08-17 — V2.0.0 发布打包完成（GitHub 草稿待实测后公开）

- 用户拍板打 V2.0 正式版。csproj 版本 2.4.0→2.0.0（Mod 自身版本号，游戏兼容仍 2.4.0 IL2CPP），
  重建 0W/0E，DLL 204,800 bytes、SHA-256=`BFAF0AC6D623055ED870A836BBFEECBC32B23089B1A9026F4CB401444450E823`。
- 玩家侧文档全部重写为 V2.0 口径（不再用"测试候选"措辞，未验证边界诚实标注在"持续观察"节）：
  `MOD_UPDATE_AND_FIX_LOG_ZH.txt`（V2.0 更新说明：银行助手重做/四队亡灵/箭塔重建/旗帜编队/小船恢复/
  忍者伏击转正/狂战士进阶转正/隐士防绑架/主船扩容/巨魔平衡/性能优化+修复清单+比例汇总）、
  `MOD_USER_GUIDE_ZH.txt`（V2.0 使用指南+新功能FAQ）、`MOD_CAPABILITIES_AND_ROADMAP_ZH.txt`、
  `release-notes-il2cpp.md`（兼作包内 INSTALL.md）。提交`0f77f61`。
- 发布 ZIP `KingdomEnhancedMod_v2.0.0_IL2CPP.zip` 从 E 盘基座重打包：312 项（doorstop/winhttp/dotnet/
  BepInEx core+unity-libs+config(cfg)+plugins(新DLL)+四份文档+manifest），39,345,832 bytes；
  泄漏检查 CLEAN（无日志/缓存/interop/备份/SKIDROW 痕迹）；manifest=commit 0f77f61、dirty=false。
- 按既有决策（避免 Git 历史膨胀）ZIP 移出版本库：`git rm --cached` 旧 v2.4.0 ZIP + gitignore `release/*.zip`，
  提交`33c2c59`；新 ZIP 只作为 GitHub Release 资产。
- GitHub 草稿 release 已建（v2.0.0 标签占位、资产=V2.0 ZIP、说明=V2.0 更新速览），待用户实测
  银行助手顺吸/箭塔重建/幽灵驻守后一键公开发布。E 盘测试副本已同步部署 2.0.0 发布 DLL（备份
  `before-v2.0.0-release.bak`）。

## 2026-08-17 — bank-assistants-005：捡币改链式顺吸（reviewer 两轮通过，待部署）

- 用户反馈：助手逐枚"定位→走→捡→停→等下个扫描节拍"卡顿严重，跟不上扔币节奏；原生银行家是
  批量认领+Wallet接触吸附一路连吸。根因三层：结算后`Target=null`+动画归零停死；下一枚分配只在
  `ScanAndDispatch`节拍（0.5s）；单收集者+逐枚0.22精确定位。
- 修复三件套（只改`PatchEconomy_BankAssistants.cs`，助手无Wallet/仅权威侧/Deposit原子入账等
  不变量全保）：**链式目标**——每枚结算当帧`TryChainNextTarget`接最近未认领成熟币（认领失败退让
  次近候选），奔跑动画全程不停；**顺路扫吸**——移动中`SWEEP_RADIUS=0.35`内成熟币走与目标币完全
  相同的认领→`CanCommitPickup`→`SetFake/pickedUp`→`DepositFromAssistant`→池回收事务，多币认领
  原策略用独立`SweepPolicies`字典按币记录回滚；**积压扩容**——`_collectorIndex`单值改
  `ActiveCollector[4]`集合，目标活跃数=1+成熟币/8上限4，轮转补位。`SCAN_INTERVAL`0.5→0.3。
- 委派链：worker=OMP deepseek-v4-flash thinking=max；reviewer=GLM5.3 subagent（kimi K3当月
  配额403耗尽按协作规范回落）——首轮**changes_requested**揪出两个真bug：①`AssignNextTarget`
  单候选穿透（最近币被村民原生认领时每0.3s回家瞬移循环最长20s）②`SelectNextCollectors`轮转
  跳位（3-4并发只激活3个）。Operator各≤10行修复后复核**approved**；另按WARN加了联机门禁
  预检（防client未追上时O(N²)空转帧尖峰）。经济原子性/认领一致性/状态机首轮即全PASS。
- 独立构建0 warning/0 error；DLL 204,800 bytes、SHA-256=
  `7E1E9B80BE388FB763F349F08025FD7D02DAE5E0B4CAC9580AD2D363B143A161`；checklist validator
  0 warning。待用户退出后部署E盘实测：沿币串一路跑一路吸无停顿、积压≥8出第二助手、
  主银行家行为不变。

## 2026-08-16 — special-tower-rebuild-018：交互不出现根因=源prefab解析到基座资产（候选集修复待部署）

- 用户实测驻守工匠修复版（A05A6551）仍无重建交互；23:50会话日志仅开局一条
  `Ready source=Tower Ballista`，全程无Blocked——CanSelect从未作用于重建payable。
- 根因实证：存档（`Release/global-v35`，gzip解压grep）已建弩箭塔prefabPath=
  `Prefabs/Buildings and Interactive/greece/Tower Ballista_greece`（2座），而补丁在
  PoolManager.Init前缀经Tower6基座模板route+GetAssetSwap解析到**基座资产**`Tower Ballista`
  ——组件加错资产，真实建造/恢复实例全来自`_greece`变体（坑24：PayableManager只对已注册
  payable调CanSelect，故静默无日志）。上轮"驻守工匠阻断"修复非主因，保留。
- 修复：EnsurePrefabLayout改候选集——GetAssetSwap结果（try/catch）+`Resources.LoadAll<Ballista>`
  按名含"Tower Ballista"扫描，安全检查（无FireTower/OilFireArcherTower/TowerKnight）通过的
  全部候选幂等配置（HashSet+biome重置）；Ready日志列出全部源名，跳过候选汇总输出。惰性克隆
  （FastSpawn→FastClone→Instantiate(_prefab)）保证Init时配好即遗传给恢复实例。
- worker=OMP deepseek-v4-flash thinking=max；Operator逐行审查（46行删除全属旧单源段，网关/
  token/prepare逻辑零改动）；独立构建0 warning/0 error。本增量未启用独立reviewer（仅prefab解析、
  不触付款/RPC契约，协议规则2裁量）。坑24沉淀；checklist validator 0 warning。禁部署Debug构建DLL为
  201,216 bytes、SHA-256=`CC3EC9F59C70D2218228B50FE7A17D02A7C8AB112C1D01F03483B63B0B2ADD8D`；
  修改后源码SHA-256=`C1CE18B2B88E0B2B0174F0EF591E1AD202F888342CC2C480C4E8F367D2741159`。
- 待用户退出游戏后部署E盘副本；实测验收点：启动日志`Ready sources=[... Tower Ballista_greece ...]`、
  已建弩箭塔出现18金币提示、付款回六级塔。

## 2026-08-16 — ghost-squads-013：希腊幽灵leash处决改边界驻守候选编译通过（待部署）

- 用户报告希腊亡灵小队一直向外冲锋、超距即集体死亡。根因为原生设计：`WarriorGhostLeaderGreece`/
  `WarriorGhostGreece` 的 `StartDeathCountdown` 即"离召唤者超`_maxPlayerDistance`处决"，而其冲锋AI
  无敌人时每秒向营火反方向推进，站桩玩家必然看到小队冲出边界自杀（D22，非mod引入，四队扩展放大了暴露面）。
- 修复=边界驻守+定时消亡：Prefix拦截两个Greece类的`StartDeathCountdown`（mod关闭/无世界权威走原版），
  监督协程每0.5s检查，`|dx|>=上限−1`时`ForceStop()+Pause(0.75)`钉住驻守（砍击/射箭照常，玩家回接近
  自动恢复冲锋）；60s到期`KillUnit()`补消耗机制，否则`HasGhosts`门会锁死技能。Summoner丢失/异常/
  启动失败均兜底，不留永生幽灵。北境行为与GhostSquads既有逻辑零改动。
- 委派链（新协作规范首次执行）：worker=OMP `deepseek-v4-flash` thinking=max（沙箱只读无法自建，
  AST+逐符号语义核对后由Operator独立构建0 warning/0 error）；reviewer首选OMP `kimi-code/k3`因
  当月配额403耗尽，按备选顺位回落GLM5.3 subagent thinking=max，结论approved——核对Mover暂停
  Max语义下0.5s+0.75s钉住节奏数学无间隙、`yield break`在try-with-catch内合法、KillUnit回收链完整、
  联机parity；两个非阻塞观察项（弓箭手Shoot收尾UnPause的有界抖动、HelsHead若接希腊prefab同样驻守）。
- 新增仅`il2cpp/PatchDivine_GhostLeashHold.cs`（源码SHA-256=
  `8CFC3101AB89831A5411E63579CBDCE65284153DAB28775D89BA4EF46436A478`）；禁部署Debug构建DLL为
  200,192 bytes、SHA-256=`06AE5B2D4DF9D55CF533225FB00E4385C0E693D27C0DD23B31FB0C1D0EE86ADF`。
  同步D22、checklist validator 0 warning。用户退出后已于23:58部署E盘独立副本（旧DLL备份
  `KingdomEnhancedMod.dll.before-ghost-leash-hold-20260816-2358.bak`），待用户实机反馈
  （驻守距离、60s消亡、北境不变、HasGhosts解锁）。

## 2026-08-16 — special-tower-rebuild-018：驻守工匠交互修复静态通过

- 实机日志只有`Ready source=Tower Ballista target=Tower6 price=18 biome=5`而没有`Rebuilding`；根因是旧候选
  把正常常驻工匠视为阻断。现已移除人数门禁，不清actor、不改职业/钱包/存档；旧塔由原生Pay销毁后，
  当前与排队工匠在下一次工作循环观察Unity-null并走原生清理。
- bolt可失败回收已移到离线最终CanPay成功之后、TransactionComplete之前。失败取消并退币；成功用同帧
  payable/player/world/scene token进入原生Pay。部分回收不会重新挂失活bolt，而是归一Reloading/currentWork0。
- 在线首版整体fail closed，避免付款RPC批准后的主客分叉；本地分屏仍支持。新增按实例与原因变化、30秒
  限流的阻断诊断。worker构建0 warning/0 error，reviewer最终APPROVED；源码SHA-256=
  `C7FF31FFD1E6D025D63CCD615AB582D9B2A3A7E88C57C784B42374B461CA3F78`，禁部署DLL SHA-256=
  `113BE01ED8F8ABAAD52571DCEF74829A14CB7B7B4F5210191AE8F804CF0D6696`（196,608 bytes）。
- 代码与首轮文档已由提交`1f9f988`推送；从干净提交重建0 warning/0 error。确认游戏进程为0后只部署
  E盘独立副本，构建/部署DLL均为196,608 bytes、SHA-256=
  `A05A6551061C48DE4ADB20BCC6290D1948638F27C06DB6B19D5026F48E82514E`；原192,000-byte DLL已备份为
  `KingdomEnhancedMod.dll.before-special-tower-worker-20260816-2225.bak`。当前ZIP未刷新。

## 2026-08-16 — special-tower-rebuild-018：首版已部署，刷新ZIP待生成

- 用户退出后确认游戏进程为0；从已推送提交`703be83`重新构建0 warning/0 error，并只部署E盘独立
  测试副本。构建/部署DLL均为181,760 bytes、SHA-256=
  `947131C76EF465B35AC21862E273E29D87AB0A8C2D97136E9CA15062F97E9CBD`，覆盖前旧DLL已保留备份。
- 本轮仍只开放安全空闲Ballista付费重建为当前biome原生六级普通箭塔；Fire/OilFire/Knight/
  Berserker/Baker/Mead来源继续fail closed。静态与部署门禁通过，保存往返、跨岛、分屏和联机待实测。

## 2026-08-16 — 刷新测试候选：友好巨魔闭环与视觉比例

- 用户退出游戏后确认 `KingdomTwoCrowns` 进程为0；当前IL2CPP源码重新构建0 warning/0 error，
  只覆盖E盘独立测试副本。构建与部署DLL均为174,080 bytes，SHA-256=
  `116972F641D20C2801F3113C12F7B94B6DEF23B33F29684786994321071A5749`；旧DLL另存为非DLL扩展名备份。
- 本次刷新候选收录已获实机核心闭环的友好巨魔反制：7次友好目标注入、6次真实原生伤害，六个目标
  均降至0生命；仍保留关闭恢复、Squid/CrownStealer、换岛和联机边界为待回归项。
- 同包收录Dead Lands银行助手绝对y=1.25（北境仍1.2）与火焰塔隐士绝对y=1.25；只改视觉y，
  不改变经济、调度、朝向、Passenger/Roaming或网络逻辑。玩家说明已从“下一候选/待部署”更新为
  “本次刷新候选已包含”，待干净提交后生成直装ZIP并校验三方DLL哈希。

## 2026-08-16 — friendly-troll-balance-008：反制追击修复静态通过，待部署

- 最新实机日志已经确认反制单位的稳定 10% 指定阶段生效，本轮共观察到 9 个被指定的 TrollWeak；但没有出现候选注入或原生伤害证据，因此当前只能确认“标记成功”，不能确认“已经攻击友好巨魔”。
- 新增一次性四阶段诊断：友好巨魔登记、反制巨魔进入原生目标查询、友好目标被临时注入、友好巨魔收到该反制巨魔的原生伤害。每阶段按实例或稳定身份去重，不做每帧日志、不全场扫描，也不改变概率、AI、目标、伤害、RPC 或对象池。
- 伤害诊断只订阅活动 FriendlyTroll 自己的 OnReceiveDamage，并在回池、失活、组件指针变化时精确解绑；未使用全局 Damageable 热路径 Harmony patch。worker 构建 0 warning / 0 error，独立 reviewer 静态 APPROVED；提交 `045994d` 已推送。确认进程为0后已只部署独立测试副本，构建/部署 DLL SHA-256 均为`33C23C6C780B26550453C4320D4C35B980B4E391BF8802C757EF2A40FD2C34C5`（167,936 bytes）；release zip 未刷新，待实测四阶段日志。
- 实机四阶段结果定位到设计缺口：41个友好巨魔与8个反制巨魔都正确进入登记/索敌，但原生查询半径只有2，注入与伤害均为0。新修订增加单一0.25秒中央追击器，只在普通行走态和2～10格外圈内朝最近友好巨魔移动；2格内完全交回原版冲撞。当前8×41规模约每秒1,312次简单距离比较，无全场扫描、LINQ、RPC或新池。源码SHA-256=`12700B854332A2CB8F12A21BD8669731321C5AD2358C6F9CFE1626A99375574E`；提交`3ee2be7`已推送。确认进程为0后只部署独立副本，构建/部署DLL均为`8F122777143698C2FD0F51D0BE1E388849C4802C1DE299F93A2CA2918AAB72BF`（172,032 bytes）；release zip未刷新，待实测。

## 2026-08-16 — save-repair-017：当前存档火焰塔隐士Passenger恢复已原子执行

- 用户授权修复当前Call of Olympus存档中未生成的火焰塔隐士。原生证据确认Fire=index6、
  Passenger=5，且规范Passenger状态为`player=0/land=0`；用户同意只将campaign/currentReign两份
  Fire `position`从0改为5，不创建第六座小屋、不插入Hermit/CRPC/NetID对象。
- 游戏退出后锁定输入817,111 bytes / SHA-256=`C3A8CEF5B3B59B0C4A763235B138381ED6327ABAAA2311F95530624AC17E55E8`。
  全campaign 11,665对象中无Dynamic/non-Dynamic netID980、无三种Fire名称、无既有Passenger。
- 专用默认dry-run脚本经worker/reviewer逐行审查；真实dry-run得到candidate SHA-256=
  `5C43780197C30F2B2F843D7139A5281A76CD836C9295F9307310F9A24FEE0DFE`。reviewer明确`APPLY_APPROVED`
  后原子执行，新备份保持原输入hash，最终源818,055 bytes且hash与candidate一致。
- 写后独立复读：before两份Fire均0/0/0，after均5/0/0；归一两处position后整root DeepEquals=True，
  reviewer最终`EXECUTION_APPROVED`。尚待首次读档携带、放下变Roaming、火焰塔升级与重复读档/换岛验收。

## 2026-08-16 — 隐士视觉与友好巨魔追击微调已部署，待实机

- 希腊弩箭塔隐士按 `HermitType.Ballista` 精确设为 y=1.20，骑士塔隐士按
  `HermitType.Knight` 精确设为 y=1.05；两者沿用现有 OnEnable/ScaleRegistry/OnDestroy 生命周期，
  只改变 y，保留 x 朝向、z、能力与其他隐士。
- 友好巨魔只把原生追击速度从 2 提高到 3（1.5倍），把索敌距离从 10 提高到 20（2倍）。
  冲撞速度、冲撞距离、伤害、冷却、Squid/CrownStealer 筛选与约10%反制巨魔机制均不变。
  每个对象池实例从原 profile 计算目标值；关闭 Mod 与回池前恢复，避免重复累乘。
- 独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error。用户退出后确认游戏进程为0，
  只覆盖独立测试副本；构建/部署 DLL SHA-256 均为
  `8571E740D8CD4C94E5552D13B7CD1AC5D3124FF863733191257A864B4E92FB94`（164,352 bytes）。未写Steam、
  未重打zip；待实测两类隐士朝向、巨魔3/20、关闭恢复与回池复用。

## 2026-08-16 — crash-unload-016：出航卸载栈溢出首修候选

- 02:45 与 02:54 两次崩溃的 `Player.log` / `Player-prev.log` 均以同一末链结束：旧岛保存完成后进入
  `Managers.PrepareUnload`，level 层级禁用触发持盾 Worker 的 `NpcShieldUser.SetShieldEnabled(false)`，
  随后在 `pickupShieldSound -> AudioPool/AudioEmitter.ResetAndPlay` 出现 disabled audio source，Windows WER
  均记录 `0xc00000fd` 栈溢出。相同 StackHash 在稀疏工具分配部署前已经出现，不能归因于工具优化。
- 最窄首修只在 Mod 启用且 `PrepareUnload` 同步作用域内，临时屏蔽带盾 Worker 的收盾音效；原生盾牌状态、
  子物体、事件、再生、编队、碰撞力和 RPC 全部继续执行。正常/异常路径分别由 Postfix/Finalizer 幂等恢复，
  下一场景还会无条件清除任何陈旧作用域；关闭 Mod 时完整走原版。
- 当前源码已禁部署构建 0 warning / 0 error，DLL SHA-256=`ACC466D928534F7620F7610A9C20590F301FAA617DA33EF96B53DBAEDD21D0A9`。
  这是高置信、可逆的因果候选，不是已完成的运行时证明。用户退出后已确认游戏进程消失，并已只覆盖
  独立测试副本；构建与部署 DLL SHA-256 均为
  `ACC466D928534F7620F7610A9C20590F301FAA617DA33EF96B53DBAEDD21D0A9`（162,816 bytes）。
- 运行时门禁保持：高人口岛连续至少两次完整出航并进入新岛、无新增 WER 栈溢出、卸载摘要
  `suppressed > 0` 且不再出现对应 disabled-audio 末链；平时拾盾/破盾声音与联机盾牌状态不得回归。

## 2026-08-16 — tool-assignment-015：先部署零行为探针，再决定稀疏替换

- 高人口岛的原生工具分配每约3秒运行一次，并以注册居民数构建大矩阵；当居民远多于工具时，
  这是清理多余人口后仍值得优先处理的周期性性能尖峰。
- 2.4虽公开化`DroppableRegistrar.ReassignClaimers`包装器，但原生内部调用可能绕过Harmony thunk。
  因此第一阶段只加入完全放行原版的Prefix/Postfix探针，每个Registrar最多记录前4次居民数、工具数、
  调用间隔和原算法耗时；不写目标、不写claim、不替换算法，也不会持续刷屏。
- 探针源码提交`20c457b`已推送。用户退出后从该提交Debug重建0 warning/0 error，并只部署独立测试副本；
  构建/部署DLL SHA-256均为`BDC91E72BF5B287E4BF3DD8BDEEB3CCF57B6B2C32D03FA742074024855F3E723`。
  实机日志连续命中：582 carriers、7～8 droppables，后三次间隔约3秒，原版耗时约9～10毫秒。
- 第二阶段现已实现：只在carriers不少于128且eligible tools不超过四分之一时，用原生评分缓存与补丁私有
  JobAssigner求解工具×居民小矩阵；目标仍经居民自身接口两阶段更新。全局JobAssigner、其他工作系统、
  资格与claim协议不改。与Horn隐士y=1.15一起提交为`147ea44`并推送；用户退出后从该提交重建
  0 warning/0 error并只部署独立测试副本，构建/部署DLL SHA-256均为
  `2CE66091A760E0ECE0455B5B2599371156CB295EDC8E2522FEE7F292D72ADF09`。
- 本轮一次切岛闪退由Windows记录为`coreclr.dll / 0xc00000fd`栈溢出，末尾位于场景卸载；探针4次后已停止且
  没有相关异常，当前不能归因于探针。后续必须重复切岛；若复现则先停发并单独定位。

## 2026-08-16 — role-qol-001：号角隐士 y=1.15

- 2.4枚举确认号角隐士为`HermitType.Horn`，与Horse马厩隐士是两个独立类型。
- 沿用Baker/Horse现有生命周期，只在Horn启用时绝对设置localScale.y=1.15并登记ScaleRegistry，
  OnDestroy精确注销；x朝向、z、能力、其他隐士及存档均不改。已随上方候选部署独立副本，待观感确认。

## 2026-08-16 — ability-cooldowns-014：两项30秒冷却微调为22.5秒

- 2.4资源实读确认 HermesStaff 基础冷却为30秒且每只转化目标附加值为0；Cerberus 召唤冷却也为30秒，
  并在最后一名亡灵消失后才开始计时。本候选统一缩短25%，目标均为22.5秒。
- 只修改冷却配置：法杖控制范围/上限/永久性与四支亡灵小队的数量、希腊/北境行为、持续时间、
  回收、对象池和RPC均不改变。源码提交`14ebb6f`已推送；游戏退出后干净Debug重建0 warning/0 error并只部署
  独立测试副本；最终刷新后的构建/部署DLL SHA-256均为
  `0744BC6B6A55D1792EB95391988D9D9400091255F7D934AD1DF1D16437BA037F`。刷新候选包已通过结构、UTF-8、
  源码排除与三方DLL哈希门禁；功能保持doing，等待用户实机计时。

## 2026-08-16 — 希腊北境外观居民统一 y=1.125

- 用户确认希腊世界的北境外观居民 y=1.05 视觉上过于接近原尺寸，要求与真正北境居民统一。
- 普通 `Peasant_norselands` 与乞丐晋升生成的 `WarriorPeasant` 两条路径现在都绝对设置 y=1.125，继续只保留 x 朝向与 z，不改角色行为、转职、配色或网络逻辑。
- 游戏当前运行中，因此本轮只进行源码/文档修改与禁部署构建；提交推送后等待退出再部署独立副本和刷新候选包。

## 2026-08-16 — 主银行家提款目标 39 → 100

- 当前2.4资源的`playerMaxCoins=39`与Mod钱包容量2000不匹配；原生提款量为`min(ceil(国库*0.25)+银行家随身金币, playerMaxCoins-玩家金币)`，并以每0.15秒1枚生成物理金币。
- 为避免直接提高到2000导致最长约5分钟持续吐币和大量物理对象，本候选只把Enabled状态下的目标提高到100；25%比例、逐枚节奏、账本与助手逻辑不改。
- WorkProfile新增原`playerMaxCoins`捕获，Mod Disabled时与扫描/速度参数一并恢复，避免同一Banker实例残留增强值。Debug构建0 warning/0 error，独立静态审查APPROVED；等待干净提交重建、独立副本部署与提款实测。

## 2026-08-15 — fleetboat-recovery-009：候选已部署，等待实机验证

- 用户提供的异常存档显示四个 `GodIsland*` 神像交付任务均 completed，但 carryForward 小船数为 0、
  所有岛无 FleetBoatSaveData，且最近载入为死亡换君主后的 sailingIn。已在游戏未运行时备份原始
  `global-v35` 至 `Release/KEM-backups/global-v35.before-fleetboat-recovery-20260815-162933`，
  767,054 bytes，SHA-256=`1D50D6CE1B0DD49D30F85C0BB8B57BB88C0AFE599FC718B18F705E93D7359822`。
- 新增 `PatchWorld_FleetBoatRecovery`：只在非 challenge 的 Greece campaign/scene 与 world-authority
  生效；ApplyToScene 前捕获 carry 所有权目标，原生完整返回后按 active 优先、否则 standby 的唯一表示
  计算缺口。四个 GodIsland 交付任务给出 0～4 所有权下限，绝不把 active/standby/carry 相加。
- standby 与 active 严格互斥恢复；riverless/前置不完整或首个生成失败时才在零 active 状态回退 standby，
  active 部分成功后不混写 standby。生成只复用当前 biome 原生同步池，无新 RPC/syncID/sidecar。
- worker 与独立 reviewer 静态 APPROVED；源码提交 `7710977` 已推送。随后从干净提交重新 Debug 构建，
  0 warning / 0 error，并在确认游戏未运行后只覆盖独立测试副本。构建与部署 DLL SHA-256 均为
  `774F5ACFF413C76493456596ADE35D905C58CC9299F054747266FF2CF09607F3`。当前公开 zip 未刷新，
  运行时任务保持 doing，等待异常档首次恢复、重复读档、换岛和死亡重生门禁。
- 用户首次载入后体感未看到四艘船，但只读证据确认恢复已实际发生：日志唯一摘要为
  `expected=4 active=0 standby=0 carry=0 desired=4 missing=4 recovered=4 mode=spawned-from-zero`，之后无
  FleetBoat/unknown pool/duplicate syncID/RPC异常；20:08 autosave 的当前第3岛含4个 `FleetBoatSaveData`，
  BoatNumber=1～4、CurrentState=Idle，位置 x约38.31～41.26。当前不重复补船，避免制造8艘；先沿登陆点、
  水道和左右外墙确认视觉位置，必要时下一候选只加一次延迟位置诊断。
- 随后换岛日志确认原生 carry-forward 已生成4艘、恢复补丁未补船，但新岛四个 Idle 实例最终停在完全相同的
  x=37.96。死亡前旧正常档证明四船本应保持同一侧并按 BoatNumber 约1单位错开，问题是原生换岛生成后没有完成横向归位。
- 第二阶段已实现并经独立 reviewer 静态 APPROVED：ApplyToScene 后由单批次runner等待2～4艘船全部 Idle、编号唯一、
  原生side/base及Mover/FSM有效，再仅调用一次原生 `UpdateBase(true)`。不改side、状态、坐标、数量、任务、standby、
  carryForward、对象池或RPC；活动/编队/航行状态只等待或超时。提交`e643d9f`已推送；从干净提交重建0 warning/0 error，
  并在游戏未运行时只部署独立测试副本。构建/部署DLL SHA-256=`8A829791422A575A4157DC036F943DC7446FE8C98600D080BA686A57E5A6F039`，待实机。

## 2026-08-15 — role-qol-001：马厩隐士 y=1.10 已部署独立副本

- 2.1/2.4 双端核对确认吹笛解锁、用于马厩升级的隐士是 `HermitType.Horse`（标签
  `HermitHorsekeeper`），不是 `HermitType.Horn`。沿用既有缩放守护：Horse OnEnable 绝对设置 y=1.10，
  保留x/z；OnDestroy精确注销，Baker仍为1.15，其他类型零写入。
- worker实现与独立reviewer静态APPROVED；源码提交 `82333a1` 已推送。用户退出后从该干净提交重新
  Debug构建0 warning/0 error，并只覆盖独立测试副本；构建/部署DLL SHA-256均为
  `BAF335AF932260819F01AAC3F9C93D4B3C4E1F22FF0FDA58075A8DE339E435D6`。未打包或启动游戏，
  等待Horse=1.10、Horn/其他隐士不变的观感验证，任务保持doing/review_approved。

## 2026-08-15 — candidate-package-007：友好巨魔与视觉微调候选已刷新

- 用户退出游戏后，从干净提交 `b875c10ca421fe96106c83dfac913c1bd4778f9f` 重新构建并仅部署到
  独立测试副本；Debug 构建 0 warning / 0 error。构建、独立副本和 zip 内 DLL SHA-256 三方均为
  `E8B06EC90772390262F5D3B1325059097391EBE0D04E6CC5E479BE66DBECB8BD`。
- 刷新后的 zip SHA-256=`4C44CDCC79B4CF30E58EE6CA20087692B797FA629055D02D61CACC436744832C`，
  40,565,824 bytes / 312 entries；manifest commit 与构建提交一致、Dirty=false。插件 DLL 恰 1、
  root dotnet 187、BepInEx/dotnet 0、版本顶层目录 0、required entries 无缺失、反编译源码条目 0，
  包内 20 个常规文本项及 `.doorstop_version` 严格 UTF-8 通过；独立 reviewer 最终 APPROVED。
- 本包新增包含：友好巨魔只排除 Squid 并恢复 CrownStealer 为正常目标、约 10% TrollWeak 反制单位、
  Dead Lands/北境银行助手 y=1.2、希腊普通居民及乞丐晋升居民 y=1.05。静态审查已通过；战斗 canary、
  实际冲撞与视觉观感仍是运行时门禁，相关功能任务继续保持 doing。Steam、共享存档、Mono 未修改。

## 2026-08-15 — 视觉微调：Dead Lands 助手 y=1.2、希腊居民 y=1.05

- Dead Lands 银行助手从上一测试包的 y=1.25 调回绝对 y=1.2，与北境助手一致；只改双方确定性
  prefab 的 y，欧洲/幕府比例、x 朝向、收币调度和经济逻辑均不变。
- 希腊世界的普通 Peasant（包括映射使用的北境外观）与乞丐晋升得到的 WarriorPeasant 统一为
  绝对 y=1.05；真正北境世界的 Peasant_norselands 仍保持 y=1.125。只改 y 并继续登记现有
  ScaleRegistry，不改变转职、配色、网络或行为。
- 初始源码构建 DLL SHA-256=`E13F6836F79DBEE630FC3ED3FCB3CC2848B3CE6A7C3015C0102EDF7F13A0A02A`；
  游戏退出后已随上方综合候选重新构建、部署并打包，等待实机观感确认。

## 2026-08-15 — friendly-troll-balance-008：候选已部署，待实机验证

- 友好巨魔选敌现只精确排除长期悬空的 Squid；旧的 CrownStealer 排除已删除，未使用当前高度阈值。
  过滤发生在公开 StateMachine 推进中、候选枚举之前，并在正常/异常路径逐项恢复敌人集合；已有 Squid
  目标也会被清空。全局状态机入口对非友好巨魔仅做 O(1) 字典旁路。
- 普通 TrollWeak 中约 10% 按存档/岛屿/统治期与动态 NetID 的稳定哈希成为反制单位；只有世界权威端
  在其原生目标查询期间临时加入 active FriendlyTroll，随后恢复目标缓存。未新增 RPC、序列化、pool、
  prefab、碰撞体或全场扫描。概率是大量同步池槽的长期平均，同统治期复用同一槽保持相同结果。
- 独立 reviewer 静态 APPROVED；初始源码构建 DLL SHA-256=
  `084981C255AE05EA7EBB9A3F8199E2D3B8DEDE6EB321A7F5F05BB0FEF6317F50`。游戏退出后已随上方综合候选
  重新构建、部署并打包；待验证两个公开 IL2CPP hook canary、真实冲撞伤害、CrownStealer 与普通 Troll
  边界。税收助手调度零改动。

## 2026-08-15 — bank-assistants-005：Dead Lands 助手 y=1.25

- Dead Lands 外观对应固定 controller index 2，本轮在双方确定性 prefab 构建时把 localScale.y 绝对设为
  1.25；北境 index 3 继续为 1.2。两者都继承 source x/z，朝向逻辑仍只改 x，不会对象池累乘。
- 调度与经济逻辑零改动：助手按欧洲→幕府→Dead Lands→北境轮转，不随机；同一时刻严格只有一个
  collector。满载回城是同步传送/收尾，完成后若仍有成熟金币才轮到下一位，不存在返程期间并发收币。
- 独立 reviewer APPROVED；Debug构建0 warning/0 error，构建、独立副本与刷新后zip内DLL SHA-256均为
  `9E71AFF5B155EF6D50DCD9EB0CFBA1098824382CF2C0547FEE431D485F8376BB`。刷新后zip SHA-256=
  `7F736F339F22AFBC7FCD00659863167753A91B566643CD4818F6401CCFB42ADC`，结构与UTF-8门禁均通过；
  待实机观感确认。

## 2026-08-15 — candidate-package-007：综合测试候选包已生成

- 重新打包当前综合候选，包含酿酒师 y=1.15、银行助手行为版、主船原生兵种扩容及此前候选改动；
  包内文档已统一说明“本测试候选包已包含、仍待实机门禁、尚未转为公开稳定能力”。
- 最终 zip SHA-256=`952FB1ECF3EEE011FA2AF8FC0956D13069D24EA5777C31EAA980692497D2087F`，
  40,558,266 bytes / 312 entries；manifest commit=`8ea703b1c9f4ed045608cc0b1594b773e849cfbb`、Dirty=false。
  构建、独立副本、包内 DLL SHA-256 三方均为
  `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`。
- 插件 DLL 恰 1、root dotnet 187、BepInEx/dotnet 0、版本顶层目录 0、required entries 无缺失、
  20 个文本项严格 UTF-8 全通过；独立 reviewer APPROVED。Steam、共享存档、Mono 未修改。

## 2026-08-15 — role-qol-001：酿酒师隐士 1.15 倍候选已构建

- 当前版本的酿酒师外观对应 `HermitType.Baker`。新补丁只在该隐士启用或对象池复用时，把 y 轴
  绝对设为 1.15，并登记到现有缩放守护机制；x 朝向、z、能力和其他隐士均不修改，也没有资源扫描或累乘。
- 为避免真实销毁后的 Unity instanceID 在同一进程复用并把 1.15 误套给其他单位，缩放注册表新增
  单对象注销；只在酿酒师 OnDestroy 时移除，OnDisable 不移除，保持对象池复用语义。
- IL2CPP Debug 构建 0 warning / 0 error，DLL SHA-256=
  `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`，独立 reviewer 静态
  APPROVED。游戏退出后已部署独立副本，构建/部署哈希一致；等待游戏内外观/对象池复用验证，不进入当前正式 zip。

## 2026-08-15 — boat-capacity-006：主船原生兵种扩容（进行中）

- 用户最终要求仅调整大船：独立弓箭手保持 4，工匠 8、骑士/侍从小队 6、长矛兵/重装步兵 8、
  农民保持 3；奥林匹斯小船保持原生容量。
- 2.4.0 静态核对确认原生五类已有登船所有者与乘客组件，容量字段可在主船注册前安全调整；
  狂战士与忍者没有该原生接口，因此不能只加两个数字。当前按高风险跨岛/联网任务设计为两类独立
  轻量适配器，必须在网络组件注册前进入 prefab，并完整复用原生登船、换岛存档与下船链。
- 深入核对发现狂战士/忍者不仅缺乘客接口，还缺上船 AI 分支，原生跨岛清单也只硬编码五类；
  用户判断收益不高并明确取消这两类登船。此前未完成的 adapter、RPC 与 sidecar 方案已全部撤销，
  不会进入构建或存档。
- 最终最小补丁仅在 `Boat.OnEnable` 原生注册调用期间临时写四个容量，注册完成或异常时恢复原字段，
  避免同一主船对象在关闭 Mod 后继续残留增强值。Debug 构建 0 warning / 0 error，
  DLL SHA-256=`DF1B21214D487F7AFEBFCD2E606301B1B4CB8BA40ED773BAE2DC58594A0B5772`；
  独立 reviewer 静态 APPROVED；代码提交 `c27d244` 已推送候选分支并进入现有 Draft PR #1，尚待
  独立副本实测。小船、Mono、Steam、共享存档和当前正式 zip 均未修改。

## 2026-08-15 — 银行助手系统（进行中）

- 用户进一步明确主银行家的固定活动区是左右从城堡向外数第二道墙之间，而不是全部领地或第一道墙。
  最新实现用两侧有序墙列表的 index 1 定义该区；第二墙未同时就绪时对称回退第一墙，再回退有效外墙，
  并让主银行家的扫描前后距离精确止于当前管辖边界。该区外（包括外层领地内）的玩家金币满 3 秒后归助手。
- 当前收集助手对同批后续金币采用 6 单位阈值：首枚或远目标才近距传送，6 单位内直接跑去，避免逐枚闪现；
  北境外观助手仅把 y 设为 1.2。独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error，
  DLL SHA-256=`51BFFEEF87FCC6846AF4FB253270DD0F6FE50C814DF7EBD3596A5320F8C8013B`。独立副本当前运行中，
  因此本轮尚未部署，等待安全退出后覆盖并实测。
- 四套外观与四助手生成已在独立副本确认。用户随后调整产品契约：主银行家应保持增强移速、安全期全天工作并
  覆盖扩张后的全部墙内区域；四名助手空闲时不应僵站，同一批墙外金币也不应四人一起行动。
- 当前行为版已实现唯一 collector 与批次轮转：只有当前助手会近距传送并连续收取成熟墙外金币，其余三名在
  城堡附近的独立墙内走廊巡逻，到端点分别停留 2/3/4/5 秒。主银行家保留原生状态机，walk=1.95、run=3.6，
  scanner 每 1 秒工作且低频跟随左右墙更新；墙外认领门禁不变。夜间不隐藏，运行中重新启用时仅在领地安全
  才立即出现，避免攻城时主动开门。独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error，
  构建与独立测试副本 DLL SHA-256 均为
  `91B6FDB52831BAA15B14E54B047F989E3B7639FC3DCE856A0522F4472AF41B62`。待游戏内实测。
- 首次候选已部署独立副本。实机日志确认四名助手池和实例都成功生成，但旧的动画资源入口无法取得
  `banker`、`banker_bamboo`、`banker_deadlands`、`banker_norselands` 四套控制器，代码又统一回退到
  当前世界控制器，造成四名助手外观相同。调度器还会在成熟列表第一枚金币暂不可认领时放弃后续候选，
  固定 800 距离也不覆盖所有加宽地图。
- 修复版沿 `BiomeHolder` 的世界风格替换表取得控制器 direct reference，并要求四套 exact name 与实例 ID
  全部唯一；缺失时整套助手 fail closed，不再复制相同外观。扫描改为统一掉落列表的全岛范围、逐候选尝试，
  临时原生认领不再重置 3 秒观察计时；诊断日志按状态去重，只保留首次分配和首次入账事件。
- 双端在资源稍晚就绪时都会以 2 秒退避重试固定池注册，客户端随后仍立即退出，不生成助手、不认领金币、
  不写国库。修复版 Debug 构建 0 warning / 0 error，DLL SHA-256=
  `F3BAB6CB492335D23E9CA3D958315545EE679100B0460EE511E8B160E5B99409`，独立 reviewer 静态 APPROVED；
  自动部署因当前桌面无 E 盘写权限而被拒绝，旧测试 DLL `0203BC71...` 保持不变；待 operator 手动部署
  独立副本复测。Steam、共享存档与当前正式 zip 均未修改。
- 2.4.0 资源实证：游戏只有一个完整 `Banker` prefab，通过 `banker`、`banker_bamboo`、
  `banker_deadlands`、`banker_norselands`、`banker_greece` 五套动画控制器形成五种世界外观。
- 用户拍板采用“1 名主银行家 + 4 名助手”：希腊外观主银行家继续独占国库、利息、提款、城堡门和
  存档；另外四套外观只作为无 `Banker` 行为的收币助手，避免固定 NetID 903 冲突和重复计息。
- 新任务 `bank-assistants-005` 已完成候选代码与 Debug 构建；边界为 world-authority 单写、金币单目标认领、统一低频扫描、
  墙外落地 3 秒后才分配；墙内金币继续留给主银行家/原生单位。为避免换岛时在途金币丢失，成功拾取即原子记入主银行家，满载/无目标回城只作
  视觉与容量节奏；同时修正旧共享账本可能覆盖日息/回滚提款的同步时点。四名助手使用轻量同步外观，
  不复制完整银行家、钱包或持久化身份；中央扫描频率为 2 Hz。最新构建 0 warning / 0 error，
  首次部署 DLL SHA-256=`0203BC714DCE13A20B6E9F753FF8D21E13083316DA936085F41DC758869A164C`。
  该首次版本的外观与调度回归已由上方修复版取代；候选仍不属于当前正式 zip 能力。

## 2026-08-15 — role-qol-001：狂战士公开 Promote 修复获用户实机验收

- 用户使用最新综合候选再次招募狂战士，确认当前招募序列没有问题；这证明从未命中的私有
  `Worker.TryPickupBerserkerTool` 迁移到公开 `Character.Promote(DroppableTool,IUnitController)` 后，
  1–5 普通、第 6 名长柄斧队长的循环已在游戏内生效。
- 本次运行的构建/独立副本 DLL SHA-256 均为
  `6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`。换岛延续和完整退出后重置
  尚未单独留证，可作为后续回归项，不再视为当前已发现缺陷。
- `role-qol-001` 仍保持 doing，因为同一组合任务中的隐士防绑架尚未取得游戏内命中证据；不把
  狂战士通过自动扩写为隐士也通过。

## 2026-08-15 — ninja-runtime-003：最新综合候选已部署并获用户体验验收

- 核对发现用户上一轮实际加载的是旧 DLL `88CE41D4...`，因此灌木半间距尚未生效；游戏退出后已将
  最新构建仅部署到独立测试副本。构建产物与测试副本 DLL SHA-256 均为
  `6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`。
- 用户重新运行后确认本轮忍者行为没有明显问题、整体逻辑自洽，灌木三槽
  `-0.55/0/+0.55` 的视觉间距合适。该反馈作为当前综合候选的游戏内体验验收证据。
- 仍未逐项留证的边界是树被砍、帐篷摧毁后的占用解绑，以及灌木跨侧池复用；任务暂保持 doing，
  后续回归补齐这些边界后再关闭并重打正式 zip。Steam 与共享存档未修改。

## 2026-08-15 — ninja-runtime-003：灌木三槽间距按实机观感减半

- 用户反馈当前忍者表现良好，且实机能看到灌木左右两个独立蹲守位置；但原 local x=`±1.1` 视觉上
  过宽，左右忍者接近灌木边缘。按用户要求改为 `-0.55/0/+0.55`，容量仍为 3、每槽仍单占用，
  不改变树 1 槽、乞丐帐篷 5 槽或原生近墙选择顺序。
- IL2CPP Debug 构建 0 warning/0 error，DLL SHA-256=
  `6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`；`git diff --check` 通过。
  当时尚未部署；随后已在游戏退出后只更新独立测试副本，并由用户确认新间距合适。正式 zip、
  Steam 与共享存档未修改。

## 2026-08-15 — ninja-runtime-003：伏击点扩展为灌木 3 / 树 1 / 乞丐帐篷 5

- 用户补充：若墙外未砍树，成熟灌木可能不足；Greece 忍者还应能在树下与乞丐帐篷蹲守。
  静态核对原生选择逻辑：`Kingdom.RegisterHidingSpot` 会把同侧列表按靠城墙方向排序，
  `Ninja.GetHidingSpot` 选择第一个墙外且未占用的槽，所以三种载体统一登记即可自然实现
  “谁离城墙近且没人就选谁”，无需自定义类型优先级。
- `PatchRoles_Ninja` 已抽出通用奇数槽锚点：成熟宽灌木最初使用 local x=`-1.1/0/+1.1` 三槽，
  后续按实机观感收紧为 `-0.55/0/+0.55`；
  每棵 Greece `PayableTree` 增加中心一槽；每个 Greece `BeggarCamp` 增加
  local x=`-2/-1/0/+1/+2` 五槽。每槽仍是原生单占用，父灌木禁用、树砍伐或帐篷摧毁时
  由原生 `OnDisable` 注销并通知占用 Ninja；仅 world-authority 创建。
- 帐篷只复用已实机命中的 `BeggarCamp.Awake` 一次补槽，未叠加同帧权限/Start 入口，避免新组件
  在原生 `Start` 前被手工登记、随后二次登记。树走 `PayableTree.OnEnable`，池复用时仅在 sided list
  缺失才清旧占用并补登记。
- IL2CPP Debug 构建 0 warning/0 error，DLL SHA-256=
  `68149B124362F823B265BD7A0CF25B3B390B4C566026269FADB3E0182AE0C55A`；checklist validator 与
  `git diff --check` 通过。本轮未部署、未启动游戏、未修改 Steam/共享存档/正式 zip；上一轮 reviewer
  approval 不覆盖新增树/帐篷范围，任务保持 doing，待静态复核与独立副本实测。

## 2026-08-15 — 玩家更新日志与 Git 归档审计

- 新增 `release/MOD_UPDATE_AND_FIX_LOG_ZH.txt`，以第一次正式发布包为基线，用玩家可理解的语言
  汇总首发后的忍者战斗修复、设置面板/终端降噪，以及三槽草丛、角色缩放、5+1 狂战士和隐士
  防绑架候选能力；明确区分“日志已确认”和“仍待实机”，不把当前候选误写成正式 zip 已包含。
- `pack-il2cpp.ps1` 已将该 TXT 加入未来候选包的复制与必备条目门禁；本次未运行打包脚本，
  当前正式 zip 未修改。使用说明、能力路线图与安装说明同步为每个成熟宽灌木三个错开忍者伏击位。
- Git 审计时，当前分支 `master` 的最后一次本地提交为 `02037fb`（2026-08-13 20:31 +08:00）；
  首发最终 zip 的生成时间以及此后全部候选修改均晚于该提交，说明此前并非每次更新后都有提交。
- 用户已明确授权以后每次项目改动完成后 commit + push。已创建私有 GitHub 仓库
  `https://github.com/baisiqi6/ohmymods` 并配置为 `origin`；首次 push 被历史中的 123.77 MB
  `ktc-il.txt` 拒绝。按用户补充要求，`game-source/`、`Assembly-CSharp/` 与 `ktc-il.txt` 只保留
  本机并加入 `.gitignore`，首次上传前从可推送历史中移除，不上传反编译参考源码。
- 清理后的 `master` 与 `agent/post-release-candidate` 已推送成功，草稿 PR 为
  `https://github.com/baisiqi6/ohmymods/pull/1`。`master` 保存首发前历史基线，候选分支保存当前全部
  改动；PR 保持 Draft，直到三个 doing 项的游戏内门禁通过。历史中的旧发布 ZIP 约 67.85 MB，
  GitHub 仅给出大文件警告，未阻断；后续正式包优先考虑转入 GitHub Releases，避免 Git 历史膨胀。

## 2026-08-15 — log-hygiene-004：候选已部署，待实机验证

- 旧 `Player.log` 约 39 MB，主要由设置面板注入静态 `Zpix` 后触发的 IMGUI/TextCore 字体转换
  级联造成：`Unable to find a font file` 与 `Unable to load font face` 各 17,482 次。
  已删除 `Resources.LoadAll<Font>("")`、`TryLoadCjkFont` 和 `_skin.font=Zpix`，改为复用 Unity
  默认 `GUI.skin`；F5/Ctrl+F10、英文配置名、数值与全部控件保持，中文 glyph 可能降级为方框。
- 钱包容量保障和四类左右商店队列的幂等业务写入保持不变，仅将重复成功日志从 Info 降为 Debug。
  PlayFab/证书、原生商店选址、游戏 uGUI BestFit 和卸载音频警告不做屏蔽。
- 独立 reviewer 静态 APPROVED；operator 复建 Debug 0 warning/0 error，构建与独立副本 DLL
  SHA-256 均为 `EC651F6C43C06E1BA41ED7A16BE6BD8E01EBC44C2EF3939EA95021BF60E9CEF3`。
  游戏未运行，Steam、共享存档和当前发布 zip 均未修改；待完整重启后打开面板/切场景复核新日志。
- 随后的新运行日志为 74 KB：两类 `Unable to find/load font ... Zpix` 均为 0，钱包/商店重复 Info
  也为 0；仅剩游戏原生 TextMesh/BestFit 静态字体提示。尚未取得用户对中文显示和控件操作的明确
  口头验收，因此保持 doing，不提前关闭。
- 后续合并三槽灌木与狂战士 Promote 修复后，当前构建/独立副本 DLL SHA-256 已更新为
  `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`；字体实现未变。

## 2026-08-15 — ninja-runtime-003：对象池运行通过，三槽灌木已构建待部署

- 用户实测忍者攻击数次后停住、敌人不再攻击、天亮不恢复钓鱼形态。最新独立副本
  `Player.log` 给出直接因果链：`ThrowingStar` 池缺失导致 `Ninja.ThrowStar()` NRE；
  `Smokebomb` 池缺失导致 `Ninja.SmokebombRoutine()` NRE，并向上中断 `Ninja.Behaviour`。
  根因是跨 biome 迁移只注册了 Ninja/ToolNinja 主池，遗漏随角色使用的投射物和烟雾池。
- 原版 Ninja 并不按竹子名称选点，而是读取 Kingdom 的 `HidingSpot` 列表，再只接受城墙外且未占用的点。
  希腊 Grass 本身不带 HidingSpot；当前设计只在实际生成的成熟 thicket 实例上幂等补 HidingSpot，
  保留原生城墙过滤、单点占用、禁用解绑和昼夜状态机，不给每片 Grass 增加组件。
- 忍者夜行攻击形态 y=1.1、白天钓鱼形态 y=1.0，以及希腊银行家 y=1.075 已按现有
  `ScaleRegistryHolder` 实现，只写 localScale.y。对象池、草丛伏击和缩放最终独立 reviewer 静态
  APPROVED；Debug 构建 0 warning/0 error。构建与独立副本 DLL SHA-256 均为
  `EC651F6C43C06E1BA41ED7A16BE6BD8E01EBC44C2EF3939EA95021BF60E9CEF3`（仅叠加
  log-hygiene-004 的面板/日志降噪，忍者实现未变）；Steam、共享存档和当前发布 zip均未修改，
  等待用户执行完整战斗/昼夜/草丛日志门禁。
- 新一轮独立副本日志已运行候选 `EC651F...`：ThrowingStar/Smokebomb 注册成功，相关
  `Pool not found`、`NullReferenceException` 均为 0；字体大刷屏也为 0。用户要求一个宽灌木可让
  多名忍者错开蹲守，已扩展为 Left/Center/Right 三个独立子锚点（local x=-1.1/0/+1.1），仍保持
  一槽一人。三槽实现获独立 reviewer 静态 APPROVED，operator Debug 构建 0 warning/0 error，
  三槽实现与随后狂战士 Promote 修复合并后，operator Debug 构建 0 warning/0 error；游戏退出后
  已仅部署独立副本，构建与部署 DLL SHA-256 均为
  `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。

## 2026-08-15 — role-qol-001：候选已部署，待实机验证

- 新增狂战士招募序列：只统计 world-authority 下工匠使用普通 `BerserkerTool` 最终成功的转职，
  第 1–5 名为普通狂战士，第 6 名为 `BerserkerLeader`，随后循环。临时 Holder 映射由
  Postfix/Finalizer 恢复；购买、失败、读档/对象池生成及 `BerserkerLeaderTool` 升级不计数。
  序号按用户批准设计在当前进程内跨岛延续，完整退出后重置，不写 PlayerPrefs。
- 新增隐士防绑架：仅将隐士的 `Droppable.CanBePickedUpByEnemy()` 结果改为 false，同时覆盖 Troll
  的选目标和最终抓取门禁；不修改伤害、移动、乘骑、其他 NPC/物品或网络状态。已被抓住的隐士
  不会被主动释放。
- 最终独立 reviewer 静态 APPROVED；已随 ninja-runtime-003 候选构建部署独立副本，构建与部署 DLL
  SHA-256=`EC651F6C43C06E1BA41ED7A16BE6BD8E01EBC44C2EF3939EA95021BF60E9CEF3`（仅叠加
  log-hygiene-004 的面板/日志降噪）。未启动、未打包；必须以
  `slot 1..6` 和首次 `Prevented an enemy from kidnapping a hermit` 日志证明两个 IL2CPP hook 实机命中后
  才能关闭任务。
- 用户随后实测招募了大量狂战士但没有二级队长；同一 `LogOutput.log` 已确认普通 Berserker 与
  BerserkerLeader pool 都注册成功，但 `Berserker recruitment slot` 为 0。根因不是第六次 prefab，
  而是私有 `Worker.TryPickupBerserkerTool` 的原生内部调用绕过 Harmony thunk，序列从未进入。
- 已删除私有 helper hook/context，迁移到 Hammer 路径已证明命中的公开
  `Character.Promote(DroppableTool,IUnitController)`；用 active Worker + active、未拾取的普通
  BerserkerTool 收窄，且仅返回 tag/effective prefab 匹配后推进。独立 reviewer 静态 APPROVED，
  operator Debug 构建 0 warning/0 error；游戏退出后已仅部署独立副本，构建与部署 DLL SHA-256
  均为 `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。

## 2026-08-13 — 当前权威状态（取代下方同日早期记录）

- 最终 IL2CPP 发布包已生成：钱包偏移 X=+3.70/Y=-1.50；Debug 构建 0 warning/0 error；构建、
  独立副本、zip 内 DLL SHA-256 三方一致为
  `1D989035EDC066D3671E64A59330F8D205DAD83DD41F1A8BDBC91838CDE299CD`。加入中文使用说明，以及面向玩家的
  当前能力、骑士小队等未来计划与共创邀请 TXT 后，最终 zip SHA-256=
  `30E3853FCC43BE62C4D8944FD652D1A2DB4E96FD05AFF0E75D038C1E13563690`，40,532,301 bytes；
  目录结构、单份根 dotnet runtime、UTF-8 安装/使用/未来计划说明与构建 manifest 门禁通过。Steam 正式目录未修改。
- 首发门禁收口：用户确认钱包扩容可用并要求沿用原版物理溢出，不再以“2000 停止拾取”为验收；
  北境原生 Worker 判别/盾牌回归与神器法杖超过原版 5 秒仍不恢复均通过。双人分屏由用户明确降级为
  发布后反馈观察项，不再阻断首发。钱包最终 UI 偏移为 X=+3.70、Y=-1.50；进入最终打包。
- 用户实测确认：Hammer 拾取卡顿完全消失；每个乞丐帐篷约 6 秒补员、5 人停止；狂战士商店出现。
  忍者商店仍未出现。新 `Player.log` 证明 NinjaLeft/NinjaRight 均已入队并反复尝试摆放，但两者都从
  同一个右侧边界开始搜索，说明旧队列中的 NinjaLeft side 已损坏。已启动 `ninja-placement-002`：
  显式写入 Left/Right、修复存档既有队列并重新规划；暂不绕过原生 CanShopFit 或降低科技门槛。
- `ninja-placement-002` 已获最终 reviewer APPROVED；修复覆盖 Ninja/Shield 左右四种新旧队列，IL2CPP
  Debug 构建 0 warning/0 error。构建与独立副本 DLL SHA-256 均为
  `06EA69A3DC0A9F339661B729FD361586697FF67C02B65560BB8C987F5AF4C7F7`；等待用户实机确认左右搜索区间。
- 第二轮实机复测仍未出现忍者商店；新日志定位到旧空 `shopSide` 的 IL2CPP 生成 getter在
  `Nullable<Side>(IntPtr)`/`CreateGCHandle` 直接 NRE，且 Start 阶段手动 Trigger 早于 core 初始化。
  第三轮已改为按类型直接 setter 覆盖四类 side、完全不读旧 getter，并移除过早 Trigger；reviewer
  APPROVED，Debug 构建 0 warning/0 error，SHA-256=`6E3537383F26E3F897ACEB955040779BB18A9CE128A0D8B97C61DD5ED9E87701`。
  游戏退出后已部署第三轮 DLL；构建与独立副本 SHA-256 均为
  `6E3537383F26E3F897ACEB955040779BB18A9CE128A0D8B97C61DD5ED9E87701`，等待复测。
- 第三轮实机通过：LogOutput 记录两次 sided-shop 规范化且无 Error/Exception，用户确认忍者商店出现；
  `ninja-placement-002` 关闭。Player.log 仅剩 NinjaRight 受原生选址条件限制继续排队，不属于队列方向故障。
- 运行时 hotfix-002：Hammer 卡顿定位为每次转职同步 `Resources.LoadAll<Character>`，已改为每世界初始化缓存；
  忍者商店 NRE 定位为 IL2CPP `Nullable<Side>` 默认 null 解包，左右商店现显式传 Side 并在 ShopPlanner.Start 后补建；
  删除希腊全商店 CreateItem 接管，恢复已注册 sync pool 的原生产出。每个乞丐帐篷临时设
  `spawnInterval=1f/maxBeggars=5`，原生扫描段使实际约 6 秒补一个。
- hotfix-002 已获 reviewer APPROVED，IL2CPP Debug 构建 0 warning/0 error；构建、独立测试副本、候选 zip
  内 DLL SHA-256 均为 `95C0F2DE6CD7285BC639D6691287F70DA99CCA1476D71E6702F21F12C6F57944`，已进入实机复测。
- 用户确认后续只打开 IL2CPP 版本做端到端验证；Mono 降级为冻结历史/自用线，不再是发布门禁。
- 独立副本 20:26 日志仍有 7 组 `NpcShieldUser.SetShieldEnabled` NRE；根因是 Worker.OnEnable
  早于 CRPC/NetworkPostbox 注册完成。下方“异常 32→0”只代表更早一轮问题，不代表当前候选通过。
- 当前发布 zip 不是候选：包内 DLL 为旧构建（54,784 bytes，SHA-256
  `5C045D73CDD9D91A9675C8B19F468D2B52EB23497208F8A778A6D098C0BEEB19`），且同时包含根
  `dotnet/` 与重复的 `BepInEx/dotnet/`。旧的 7.6MB/39MB 描述均为 historical/superseded。
- **历史门禁（已取代）**：本轮早期曾把容量 2000、双人分屏和北境世界判别全部列为首发门禁；
  当前以本节顶部的最终收口为准——容量采用用户确认的原版物理溢出语义，北境验证已通过，分屏降为发布后观察。
  Steam 正式目录与共享存档仍禁止自动修改。
- 安全说明：历史“无反作弊/零封号、风险实质为零”不是发布保证。联机/平台风险不能用绝对表述；
  玩家应只在接受 mod 风险的环境中使用，并保持双方版本一致。
- 盾牌/锤子修复已获独立 reviewer APPROVED；IL2CPP Debug 构建 0 warning/0 error，候选 DLL
  SHA-256=`48A022CA45B14050031CA8F339543D4EEDD5A1CD5D044DB2F21EDBC3D2854CC6`。候选 zip 的
  根目录结构、单份 dotnet runtime 与 DLL 哈希门禁通过；构建、独立副本、zip 内 DLL 三方哈希一致，
  已进入独立副本实机验收阶段。

## 2026-08-12 — 2.1.0 两个 bug 修复（乞丐拾取 + 友好巨魔永久控制）

## 2026-08-13 — Steam 实机验证与修复

- 发布包两个坑：① 漏打 dotnet\ 运行时目录（67MB，doorstop 配置指向 dotnet\coreclr.dll，缺失→静默失败无日志）
  ② BepInEx 6 IL2CPP 首次启动生成 interop 后需二次启动才加载插件（已知机制）。zip 重打 39MB 含 dotnet。
- Steam 2.4.0 r23488 实机：插件加载成功、patch 激活（Holder 加角色/Worker 替换/sync 池注册）。
- NRE×32 修复：NpcShieldUser.Awake 在希腊 Worker 裸加组件时 damageable null→订阅 NRE→AddComponent 回滚→
  EnsurePickupCapability 每 OnEnable 死循环。修复：Awake prefix 分流（无 Damageable→安全版跳过订阅）+
  EquipShieldIfNorselands shield null 防御（希腊 worker 装备盾牌 NRE）。验证：Il2CppException 32→0。
- 封号评估：KTC 无 VAC/无反作弊/单机，社区 BepInEx mod 多年零封号——风险实质为零。

## 2026-08-13 — IL2CPP 迁移（Steam 2.4.0 发布线，用户拍板）

- 决策：发布受众 = Steam 正版玩家 → BepInEx 6 + Il2CppInterop 迁移（scope.md 更新为执行中）。
- M0 骨架：il2cpp/KingdomEnhancedMod.csproj + Plugin + ModConfig（BepInConfig 替代 UMM Settings），零错误部署验证。
- 三 worker 并行迁移：经济域（CurrencyBag/Banker/ShopPlanner/SidedShop）、角色域（Holder/Castle/Knight/Character/Worker/World/BeggarCamp）、
  世界战斗域（Mover/Construction/Level/Kingdom/EnemyManager/Artemis/HermesStaff/FriendlyTroll）——全部零错误零警告。
- 关键漂移：Mover"漂移"是 get_type_members.py 正则 bug 误报（unsafe 方法漏报），实际无漂移；其余漂移（BagCurrency.Reset→ResetVisuals、
  Wallet 多币种、ShopType 重排、Level.GenerateInternal+seed 等）已适配并记录待冒烟验证（notes-*.md 共 14 项）。
- Mono 侧池修复经 HotfixReviewer 抓 P0（syncID=119 跨biome冲突每帧 NRE）+P1（根因误判：真根因是读档恢复先于 InitPools）
  → 重写为 SpawnGO 池缺失兜底，部署 GOG 2.1.0。
- 实机验证：E:/QQ 2.4.0 加载 KingdomEnhancedMod v2.4.0 成功，零错误零异常。
- 发布包：release/KingdomEnhancedMod_v2.4.0_IL2CPP.zip（7.6MB，doorstop+BepInEx core+插件+配置+安装说明，开箱即用）。
- 待办：MigrationReviewer 交叉审核中；14 项待决策需游戏内冒烟验证。

- 乞丐拾取：根因链 扔金币→乞丐捡→Promote("Peasant")→UpgradeTransitionFX→Sparkles 池缺失
  （2.1.0 InitPools 只注册当前 biome 池资产）→NRE→拾取中断。修复：RegisterAllBiomePools
  全 biome 池去重补注册（Patch_PoolManager）。
- 友好巨魔：2.0.1 ShouldRevertToTroll 恒 false（原生永久），2.1.0 改为 `_expirationTime <= Time.time`
  （_duration=5f）——补 prefix 强制 false 实现永久控制（Patch_HermesStaff）。
- checklist feature-002/003 已登记；HotfixReviewer 交叉审核中。

## 2026-08-12 — 赫尔墨斯钱袋三件套（精细化改造第一项）

- 解锁：开局强制 `ChangeCurrencyBag(Hermes, 0/1)`（Patch_CurrencyBag，OnGameStartHandler postfix）。
- 扩容：`ChangeCurrencyBag` postfix 按类型设 `Player.wallet.TotalCapacity`（Hermes 2000 / Bag 1000，
  每局重设幂等——TotalCapacity 非持久字段）。
- UI：`BagCurrency.Reset` prefix 视觉堆叠上限 300→600；`CurrencyBag.Awake` postfix 整体放大 1.3x
  （金币堆子物体继承）。
- 机制澄清（防后人重踩）：游戏**没有"钱袋容器碰撞空间"**——容量是数字（Wallet.TotalCapacity），
  拾取靠金币×玩家物理碰撞重叠 + 点击 OverlapCircle，钱袋是 HUD 视觉对象。
- 待用户实测：钱包 2000 上限、堆叠 600、视觉放大效果。


# ohmymods — 进展

## 2026-08-12 — arch-002 收尾（命名对齐 + Probe 裁剪 + 文档同步）

- 命名对齐：Patch_Shop.cs → Patch_ShopPlanner.cs、Patch_Enemy.cs → Patch_EnemyManager.cs
  （Main.cs 注册名同步更新，maint-002/003 done）。
- Patch_Probe.cs 已删除，不再注册（maint-002 done）。
- build.bat 通配化（`for %%F in (Main.cs Patch_*.cs)`）+ 编译成功自动部署到 Mods/MyMod（maint-003 done）。
- 文档同步（Worker B）：architecture.md 模块清单按最终态重写（商店注册为 Prefix 全量替换）、
  domain-model.md 关闭 R3/R4 + 新增 D8（速度倍率 SetGoal 入口/地图幂等/银行家补员删除原因/Enabled 契约统一）、
  biome-asset-system.md / unit-spawning.md 的商店注册描述改 Prefix、unit-spawning.md 自洽方案标注废弃
  （指向 patch-patterns.md 坑10）、MOD开发文档.md 归档到 docs/legacy/。
- 剩余：Mover.Update 双 postfix 合并、Main.OnGUI 反射缓存（Worker A）。

## 2026-08-12 — GOG 2.1.0 迁移完成（Mono 最后版本）

- **注入方案**：UMM 21.0.32 自带 winhttp（旧 UnityDoorstop）不识别 Unity 2022.3.51f1 →
  改用 BepInEx 5.4.23.3 的 winhttp（x86）+ `[General] target_assembly=` 格式配置指向
  UnityModManager.dll（详见 runbook "注入方案"）。
- **API 差异修复（4 处）**：
  1. `Pool.syncID` int→short（Patch_Castle 显式转换）
  2. `EquipShield NRE`：NpcShieldUser.Awake 在 HasWorldAuth 未就绪时提前 return → regenWait
     为 null → 装备前反射补初始化
  3. `Worker.OnTriggerEnter2D` 新增 npcShieldUser==null 早退 → 希腊工人无法拾取
     BerserkerTool → 狂战士商店卡死；OnEnable 补组件+回填字段（EnsurePickupCapability）
  4. 其余 21 项 patch 目标 2.1.0 验证全部存在，零 not found
- 2.1.0 反编译源码入库 `game-source/Assembly-CSharp-2.1.0/`。

## 2026-08-12 — 架构交叉审查 + P0/P1 修复

- ArchReviewer（kimi K3）审查结论：**无需框架级升级**（单 DLL + Patch 类 + harness 骨架对 19 patch 规模合适）。
- **P0 修复**：① Patch_Mover 速度倍率写错字段（_moveSpeed 被 _goalSpeed Lerp 覆盖，从不生效）→ 改 patch SetGoal/SetGoalSpeed/SetGoalNoHaglet 入口缩放 speed 参数，幂等无累积；② Patch_Kingdom 地图倍率非幂等（Init+每岛加载指数放大 4→8→16→32）→ 基准值缓存幂等设置。
- **P1 修复**：③ Main.Enabled 契约统一（Patch_PoolManager/SidedShop/WorkerScale 补检查）；④ 银行家"5 个"补员删除——Banker.Awake 硬编码 NetID 903 唯一，克隆无法注册网络且与去重自相矛盾（每 120 帧 Instantiate/Destroy 刷屏）；共享银行增强保留。
- Info.json GameVersion 1.1.4→2.0.1、Version→1.1.0。
- 剩余 arch-002：Probe 裁剪、命名对齐、文档同步、双 postfix 合并、build.bat 通配化+部署脚本化、OnGUI 反射缓存。

## 2026-08-12 — kingdom-mod skill 迁入

- 原 `.omp/skills/kingdom-mod/`（6 文件）全部迁入 `docs/project-harness/game-logic-map/`。
- 链接改为相对路径；功能清单更新到当前状态（狂战士 hack 已退役、Patch_Mover 确认为速度倍率、新增坑 11/12）。
- 原 skill 已删除；`maint-001` 核实完成（Patch_Mover 是玩家速度倍率，保留）。

## 2026-08-12 — harness 实例化

### 已完成（核心功能全部就绪）
- 狂战士/忍者：希腊世界商店原生生成（槽位劫持 12/13），hack 退役。
- 北境形象：Worker/Peasant 的 tagCharacterPairs 替换 + sync 池注册。
- 北境工匠出生带盾（SetShieldEnabled，绕过无盾牌商店的缺口）。
- 单位缩放：y 轴守护机制（OnEnable 登记 + Mover.Update postfix 恢复），
  北境工匠 1.175 / 北境居民 1.125 / 希腊工匠 1.075 / 狂战士 1.2 / 鹿 0.55 / 小动物 1.8。
- 性能清理：删除每帧 FindObjectsOfType 兜底（ScaleAllWorkers），零每帧扫描。
- 地图扩展、希腊猫生成。

### 验证状态
- 每次改动后 build.bat 编译通过（csc.exe，C# 5）。
- 游戏内实测：盾牌可见 ✓、缩放生效 ✓（多轮调参 1.3→1.175 / 1.2→1.125）。
- 待测：清理后的完整回归（狂战士/忍者购买、读档恢复、缩放一致性）。

### 风险
- R1：存档携带 localScale.y（Serializer 写完整 transform）——卸载 mod 后旧档尺寸可能不符。
- R2：狂战士（Berserker）无缩放登记，转化后回 1.0（当前意图）。
- R3：Patch_Mover.cs 旧方案遗留待清理。
- R4：Patch_Probe.cs 调试日志待裁剪。

### 下一步（按 checklist）
1. maint-001 ✅（Patch_Mover 已核实为玩家速度倍率并修复，见 D8）。
2. maint-002 ✅（Patch_Probe.cs 已删除，arch-002）。
3. maint-003 ✅（build.bat 通配化 + 自动部署，arch-002）。
4. 完整回归测试。
## 2026-08-15 — population-performance-010：硬上限与旧档清理静态通过

- 当前异常岛只读统计为1,132名角色，其中Worker 458、Peasant 301、Beggar 158；全岛只有2个BeggarCamp与2座面包房。原生营地只重算附近乞丐，面包房又会清除camp引用，导致旧的“附近最多5人”不断释放名额并积累人口。
- 新候选保留原生营地协程，但在world-authority协调器健康时由中央调度按稳定营地归属约6秒补1、每营地硬上限5；去面包房或走远不再释放该营地名额。异常、失权和Mod关闭都有原生参数fallback。
- ApplyToScene稳定后只由authority建立一次scene清理批次，每帧最多同步回收1名超额普通Beggar；settler、面包房/进食、控制、被抓、inert/石化、DespawnOnLoad及pool/header不安全对象一律保护，受保护者超过5时允许可解释残余。
- worker与独立reviewer静态APPROVED；Debug构建0 warning/0 error，当前候选DLL SHA-256=`085D84C644D6C48E046A87AAD5BE3BFCFB6154929EE8B47914533DCDF572D9DA`。游戏已退出，等待干净提交重建、存档备份、独立副本部署与实机。
- 后续工具分配优化另开切片：原版每3秒会让约922个carrier参与近方阵匹配；必须先做放行原版的hook探针，确认命中后才考虑稀疏反向矩阵，禁止全局替换JobAssigner。
## 2026-08-15 — save-repair-011：land 7 乞丐158→10已原子修复

- 人口候选首次实机确实命中，但摘要为`before=158 assigned=158 protected=158 removed=0 residual=148`；运行时安全身份门禁过严，因此内置旧档清理不能算通过。用户随后明确授权对当前异常存档做一次性直接修复。
- 强校验脚本经worker/reviewer多轮审查与真实dry-run后APPLY_APPROVED：锁定输入SHA/长度/schema/campaign/land/对象数/精确营地prefab与坐标；只删除无外部引用的普通Beggar；非目标root/island与幸存对象内容/顺序DeepEquals；同卷File.Replace并有已验证backup/rollback。
- dry-run和Apply均为`before=158 removed=148 after=10 groups=5/5`。原始备份`global-v35.before-direct-beggar-prune-20260815-232403`为751,068 bytes/SHA=`68D4F779DA3CFA45A659D2082B2B15F135777699EC4A309F1F6AEAE14C724B16`；最终存档748,730 bytes/SHA=`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`。
- 写后独立复读确认version16/currentCampaign1/currentLand7/objects2046/Beggar10/左右5+5，临时文件0。等待独立副本实际读档；若失败必须退出不保存并恢复上述备份。

## 2026-08-15 — save-repair-012：land 7 普通居民删除350名

- 纠正此前人口统计：真正带WorkerData的工匠只有14名；421个名称以Worker开头的对象实际是Peasant prefab的对象池残留名。用户确认应删除350名普通居民，不删除真正工匠。
- 从383名组件/profile完全一致、未被抓/石化/inert、钱包全0、无外部引用的希腊Peasant中，按确定性createOrder加载顺序保留最低33并删除最高350；createOrder不表示年龄且未重编号。
- worker/reviewer逐行审查、真实dry-run与独立只读内存复算后APPLY_APPROVED。原始备份`global-v35.before-direct-peasant-prune-20260815-235118`为748,730 bytes/SHA=`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`；最终存档728,071 bytes/SHA=`63884D91421A7B74AD0049C8FB00BFD3E910857F05005490B2704E856FE93FED`。
- 写后独立复读确认land7 objects1696、Worker14、Peasant383（Greek288/Norse95）、Beggar10/左右5+5，临时文件0。等待独立副本实际读档和卡顿体感；失败时退出不保存并恢复本任务备份。
## 2026-08-16 — ghost-squads-013：改为保留两套原生行为

- 2.4资源确认 Cerberus 原生只生成1名希腊亡灵骑士与4名弓箭手；原生协程只有一个共享队长引用，不能仅把数量字段改成4/16，否则成员归队关系错误。
- 北境神器亡灵与希腊坐骑亡灵只共享编队接口，实际AI不同。用户明确要求保留差异：最终仍为四个独立
  1+4编队，但两支希腊队主动向外作战并按距离回收，两支北境队跟随君主并按原生30秒Duration消亡。
- 两个北境完整行为克隆池固定预留syncID30130/30131，主客同序注册；不新增RPC，不改原生冷却、雾效
  或首队配置。第一版“北境仅视觉”部署包已被该决定取代。
- 修订源码提交 `0cd629e` 已推送；从该干净提交重建0 warning / 0 error并部署独立副本，构建/部署
  DLL SHA-256均为`024ADAC72A4D2D76B63827C19C6D9511105CDDBA977EBE196D2FF62354564A39`。正在刷新候选包。
## 2026-08-16 — friendly-troll-balance-008：解除无敌后反制攻击实机闭环通过

- 最新独立副本日志出现54次友好巨魔登记与12个反制弱巨魔查询，但候选注入、真实伤害仍为0；没有相关异常。
- 当前存档只读复核发现54个 `Troll_friendly` 的 Damageable 全部保持 `invulnerable=true`。原生索敌与受伤入口
  均会拒绝无敌目标，因此此前的稳定标记与10格追击虽已运行，仍不可能进入原生冲撞伤害闭环。
- 新修复只在 world-authority、Playing、活动实例和原生指针一致时通过公开属性解除无敌；以
  `currentAtFirstCapture || isInvulnerableInitially` 保存可逆基线，关闭Mod和正常回池前恢复。加载、卸载、
  失权与失活对象不写；联机header/catch-up未就绪时只挂起一次，随后用公开属性补发一次，不扩RPC协议。
- worker禁部署构建0 warning / 0 error，独立reviewer最终APPROVED；源码SHA-256=`73934E38B3C1DB59CA27C14C9FF3F64F310C7F9E6697DC6A6E64AAF691D32542`，
  Debug DLL SHA-256=`BDF1FB4415E05E8F9596D19A024D020210ECCCEE7B6291D72D68CECBA9A4AB4B`。
  源码与记录提交 `0495a68` 已推送。再次确认游戏进程为0后只部署E盘独立测试副本；构建/部署DLL均为
  173,568 bytes且SHA-256一致。G盘当前未挂载，未写G盘；release zip未刷新。下一步实测真实注入与伤害。
- 20:03后的当前E盘实机日志给出完整闭环：`friendly-active=56`、`counter-query=8`、
  `friendly-injected=7`、`native-damage=6`。六次原生Troll伤害的`hpAfterEvent=0`，证明反制弱巨魔已把
  友好巨魔选为目标并实际击杀，而不只是追到附近或写日志。
- 同一BepInEx日志没有Exception/Error/unknown pool/duplicate sync/RPC异常；Player.log也没有
  NullReference、StackOverflow、ArgumentException或Pool/RPC相关异常。Player.log另有原生Stats保存路径的
  `gse orca` LogError栈，与FriendlyTroll目标/伤害链无关。本任务核心攻击能力通过，Disabled恢复、联机catch-up/
  authority迁移、换岛与Squid/CrownStealer边界仍待回归。
## 2026-08-16 — 视觉微调：Dead Lands助手与Fire隐士 y=1.25

- Dead Lands银行助手对应固定controller index 2，本轮由绝对y=1.20改为1.25；北境index 3仍为1.20。
  只写prefab初始y，继承原x朝向/z，调度、经济、同步与对象池逻辑零改。
- 火焰塔隐士按`HermitType.Fire`精确设为绝对y=1.25，沿用现有OnEnable/ScaleRegistry/OnDestroy生命周期；
  乘骑时原生会临时写回单位缩放，注册器会继续恢复目标y，需作为实机观感门禁。
- 原生2.4资源确认Fire可离岛且可上船，因此已拥有的Passenger/Roaming状态正常读档、放下与换岛不依赖Cabin。
  但其`lostOnCrownLost=true`，失冠/死亡换君主会按原生规则转为CoinLocked/land0；当前又没有Fire小屋，
  所以不能承诺死亡后仍可重新获得，后续需单独决定是否修复这一所有权缺口。
- 两处代码经worker构建与独立reviewer最终APPROVED；初始Debug候选DLL SHA-256=
  `7A75716A8748A09497314C7DAE32B1B760B81A1416520C7509C5BD958E691208`（174,080 bytes）。随后已随综合候选
  提交、推送，并在确认游戏退出后部署E盘独立副本；当前综合部署DLL为181,760 bytes、SHA-256=
  `947131C76EF465B35AC21862E273E29D87AB0A8C2D97136E9CA15062F97E9CBD`，实机观感仍待验证。

## 2026-08-16 — special-tower-rebuild-018：首版安全重建已编译

- 完成态Ballista/Fire/Knight/OilFire/Berserker等资源均无原生PayableUpgrade，不能靠改nextPrefab实现互换；直接替换还会绕过next NetID、Persistent、隐士和驻员清理链。
- 用户接受“两步重建”：先把旧专精塔付费恢复为当前世界六级普通箭塔，再携目标隐士走原版专精。首版只开放安全空闲的Ballista来源，目标价格从运行时原生六级塔读取；重建本身不消耗隐士、不计特种塔统计。
- 新补丁在PoolManager建池前为当前biome安全Ballista prefab确定性追加原生PayableUpgrade和无状态marker；Disabled仍保留CRPC/Persistent组件布局，只关闭选择/付款。最终付款前回收已装填bolt，再完整执行原生Pay。
- Fire/OilFire/TowerKnight/Baker/Mead因库存、隐藏驻员或同级PayableShield生命周期未审清，首版保持fail closed。禁部署Debug构建0 warning/0 error；随后已提交、推送并在游戏退出后部署独立测试副本，实机门禁仍待完成。
- 当前源码SHA-256=`DB882F8A43BC56A58C901B7101535738B2288E0114119046D6414B45BD755023`，Debug DLL SHA-256=`D41870F063085B1852410393ACE358B42D8A81334948F5ED787D2C166B52A0A7`；checklist validator 0 warning、相关文本严格UTF-8通过。

## 2026-08-16 — fleetboat-formation-019：动态同侧小船编队候选已编译

- 2.4 Player Formation资源确认原生只有一个FleetBoat槽且该类型间距为0；候选实现只在world-authority举旗时按原生Side与完整可加入门禁快照0～4艘小船，多船间距绝对设为1。
- 原生`TryRecruit`、`UnregisterUnit`与`OnDisable`生命周期保持主导；空船槽即时封为Gap，解除旗帜后在单位清空时恢复该Player独立捕获的原版数组。协调器仅每0.5秒检查最多4个预留槽，不改FleetBoat Side/FSM/RPC/容量。
- 禁部署Debug构建0 warning / 0 error；源码SHA-256=`8550F056A982A7FAD570EBBC77929F65C99957F1F86993BC9D5D19DF66CEFCDF`，独立reviewer已APPROVED。游戏进程为0后，192,000-byte DLL SHA-256=`3595BEB72A7CD30871FD778F7F7FCCFBD6ED6AF36C9181AC1BFF634DBD54B3F3`已只部署到E盘独立副本，并保留部署前备份；仍需1/2/4船、N=0、离队、分屏/联机及native Hook canary回归。

## 2026-09-01 — v3.5.1 crash audit：FriendlyTroll 生命周期递归修复

- 审计同步到远端 `64ce116`（v3.5.1-dev4）；E 盘独立副本仍运行 dev3，未覆盖运行时 DLL。日志确认 `ErrorLog.log` 存在 `Troll.OnDestroy` 递归导致的 `Stack overflow`，与此前小岛/切岛闪退风险一致。
- 仅移除 FriendlyTroll 补丁内三个私有 `OnDestroy` Harmony 钩子（Troll、Squid、追踪协调器），保留公开 `OnDisable` 生命周期清理；追踪 tick 增加惰性 registry prune，覆盖卸载时缺失回调而不改变目标、伤害、RPC 或对象池逻辑。
- worker 构建 0 warning/0 error，独立 reviewer `REVIEW_APPROVED`。最终源码 SHA-256=`98C8BF44BFB47843DB111D17EB5B9FD4246798492FBB438008DDEE02B29698E5`，重新构建 Debug DLL（300,544 bytes）SHA-256=`8CD2A9F74D0229ABEFF87357F2B8DE8659556F28108F0AA63946650D8BE68EE4`。待游戏退出门禁复核后部署 E 盘，并实测切岛/卸载无 StackOverflow。
- 同轮审计确认 FleetBoatBerth 的 `timeout-unsafe-state` 是有界等待后不写状态；SpecialTower 已做全层级诊断，当前没有可证明的低风险额外改动。FriendlyTroll identity/header 缺失仍按 fail-closed 处理，避免错误指定普通巨魔。

## 2026-09-01 — v3.5.1 known-bugs audit：农民停工、夜怪迟到、空白塔基与守家图腾

- 本轮不改存档、不覆盖运行中的 E 盘 DLL；ZCode 0.16.5 已确认，但 headless CLI 因用户 CLI 配置缺少 model provider 被安全阻断，四个隔离 worktree 仍保留用于后续 worker 接入。
- `PatchPerformance_ToolAssignment` 的稀疏替换在 `eligibleDroppables==0` 时会把全部 carrier 的目标清成 null；新增放行原生分配的 fail-open 门，避免昼夜切换期间农民/农夫被整体清空到闲置聚集。
- `PatchWorld_TowerSpots` 改用最近原生塔基的地表 Y（全岛中位数仅作回退），并仅规范化补放根对象 active 状态；新增一次性 renderer 健康日志，用于区分“无视觉子树”和“地形/Z遮挡”，不强开子渲染器。
- `PatchPerformance_NightVolley` 新增限频 `[ClockDiag]` 只读采样（Director.currentTime/IsNight、Kingdom.isDaytime、CurrentIslandDays、Time.timeScale），用于验证农民、夜怪、存档异常是否同源时钟失配；不写入任何原生时间状态。当前 Debug 构建0 warning/0 error，候选 DLL SHA-256=`06CD4A49BDB281658C49BA880BD47FEA1B87ED4692D517D08E174EC5028175B7`（302,080 bytes）。
- 游戏退出后已部署到 E 盘独立副本，部署 hash 与候选完全一致；旧 DLL 已备份为 `KingdomEnhancedMod.dll.before-known-bugs-20260901-230425.bak`（300,544 bytes/SHA=`70DAD3B55C0BB23FCFAE3C00B5CAD4E6557FD35FA40E881DA4BEE2698FE342F9`）。守家图腾与蛇吐怪暂未做无证据的行为放宽，下一步先采样 `[ClockDiag]`、`[TowerSpots] visual health` 及图腾付款路径，再决定最小修复。

## 2026-09-02 — 弩炮弹速、守家编队与塔基避障修复候选

- 实机 `[ClockDiag]` 显示昼夜状态按原生顺序推进，农民停工/夜怪迟到不是全局时钟损坏；守家雕像左右两侧均已正确挂载。混用外观的根因是原生 `Knight.CanJoinFormation` 对盾墙编队不限制北境风格，而普通骑士随从又没有 `NpcShieldUser+HasShield`，导致队长能守家、随从不能进入原生 Shield 近战状态。
- 希腊盾墙现在只在原结果为 true 时进一步排除非北境风格 Knight；不改随从、盾牌、职业或其它编队。北境带盾弓手仍由原生 `Archer.TryRecruit` 进入 `AttackMode.Shield`，该状态包含近身攻击，不要求动画状态名必须叫 Melee。
- 弩炮塔原生瞄准和发射共同读取 `BoltData.ShootForce`；通过 getter postfix 将其从默认20统一放大到25（×1.25），避免只改发射速度导致瞄准解与真实弹道不一致，也不修改自定义弩手。
- 补放塔基现在复用 `ScatteredObject.AvoidOverlapWith` 的非 Tower 标签避障、同层/非船过滤和原生 overlap region；仍由自有塔间距实现增密。注册前补 `FixedTransform.Fix()`。既有未建 `KEM_TowerSpot` 若与建筑重叠，只在离线 world-authority、level0、SemiStatic/Persistent/CRPC 完整门禁下按 Deregister→DontPersist→Disable→Destroy 退役；原生塔基、已建塔和检查异常均不删除。
- worker 静态核对与独立 reviewer 最终 `REVIEW_APPROVED`；Debug 构建0 warning/0 error。确认游戏进程为0后已部署 E 盘独立副本，DLL为306,688 bytes、SHA-256=`0C874B1DE6CBAF97C8AD686EBFA7A3733FF28DD2BB08B9017D4F1BCB1DD090A2`；部署前备份为 `KingdomEnhancedMod.dll.before-bolt-shield-tower-20260902-210000.bak`，SHA-256=`06CD4A49BDB281658C49BA880BD47FEA1B87ED4692D517D08E174EC5028175B7`。待实测弩炮弹道、1币守家成员/近战、重叠塔基清理与存读档。

## 2026-09-03 — norse-follower-load-restore：读档后立即原生替换北境随从

- 复核确认旧逻辑只有 `Archer.AssignJob` 和 5s `PatrolPass` 会调用真实 `Archer_norselands` `Promote`；`KnightStyle.ApplyFollowerSkinTo` 另有只改 `RuntimeAnimatorController` 的换皮路径。因此读档后的守家窗口可能出现“北境动画、无盾组件”的混合状态。
- 新增 `World.OnLevelLoaded` 安全恢复协程：下一帧登记场上 Archer，等待 Knight 风格状态、Holder/Pool/CRPC 就绪后，在 world-authority 侧立即把北境风格骑士名下的非真实 `Archer_norselands` 随从换成原生 prefab，并调用原生 `SetShieldEnabled`；风格尚未解析、池/RPC 未就绪时最多 24 次×0.25s 有界重试，普通风格随从移出队列，客户端不写。
- 新增 `TryGetResolvedStyleIndex` 区分“尚未解析”与“非北境”，避免恢复协程过早丢弃候选；新增一次性 `load restore summary`（converted/shieldReady/pending）诊断；保留原有 AssignJob/5s巡检作为后续招募和池复用兜底。Debug 构建0 warning/0 error，DLL 309,760 bytes、SHA-256=`EFBB0B0F98F4719280363ABAEBDBB8768758B3F8BE190F85009758DFA0A4DA26`。确认游戏进程为0后已部署 E盘；上一版已备份为 `KingdomEnhancedMod.dll.before-norse-load-restore-20260903-205511.bak`，最新部署前版本另备份为 `KingdomEnhancedMod.dll.before-norse-load-restore-diag-20260903-205643.bak`。待实测“读档即替换、盾牌/AttackMode.Shield、再次读档幂等”。
- 2026-09-05：用户实测仍有塔基视觉重叠。隔离 worker 定位为补放网格只按 Payable 交互半径避障，未覆盖塔基 Renderer/ScatteredObject 视觉宽度，且历史 KEM 塔基彼此重叠时原逻辑不会回收。commit `2e85449` 已将视觉 bounds 与原生区域取并集、以视觉宽度抬高最小间距，并按 X 稳定顺序回收合法的自有未建造重叠基底；worker/reviewer `REVIEW_APPROVED`，Debug 0W/0E。待游戏退出后部署 E 盘测试副本并观察 `[TowerSpots] scan ... generatedUnbuilt=... retired=...`。
- 2026-09-05：针对高人口岛每隔数秒掉帧和大蛇吐怪晚到，完成性能/行军补偿候选：3秒、5秒巡检错峰；白天弓箭手拥挤检测改为可复用空间分桶；税收助手扫描间隔改为0.6秒且无候选时只做最小所有权清理；大蛇嘴部首波在夜幕前按额外距离（秒→游戏小时换算）有限预置，普通传送门不变。worker 初版遇 ZCode provider 缺失后按授权回退，reviewer `REVIEW_APPROVED`，主树 Debug 0W/0E；待实机验证吐怪提前量、夜间波次和卡顿是否改善。
- 2026-09-05 部署：游戏进程确认退出后，commit `7836358` 构建的 DLL（316,928 bytes，SHA-256 `7349F7017AC7687F832A5A634A1248041873D85961B7E562DB3FD17EDD805966`）已部署到 E 盘独立测试副本；部署前备份 `KingdomEnhancedMod.dll.before-perf-serpent-20260905-005305-677.bak`，备份哈希与原 DLL `714B51D3...` 一致，目标哈希与构建哈希一致。未改存档/G盘。

- 2026-09-06 00:31：权限恢复，面板/人口滑块/常驻日历HUD已同步主仓库并部署E独立副本；游戏退出、备份/回滚及目标hash核验通过，SHA256=A86D036741EF61F6301485ACABAFA20864BAEC37D790C660291D2509F1F0858C。HUD默认关闭，F5→世界开启；0W/0E编译、95702日历断言与独立审查完成，实机仍待验证。

## 2026-09-06 — 面板与日历HUD的IMGUI兼容热修
- 用户反馈F5面板无法调出。E测试副本日志有358次`NotSupportedException: Method unstripping failed`，堆栈明确到`GUI.DrawTexture(Rect,Texture)`→`ModPanel.DrawPanel`→`OnGUI`；快捷键并非根因。
- Cecil遍历实际目标interop发现全部DrawTexture重载最终转到throw桩，HUD三处调用同样潜伏此问题。ZCode worker独立快照实现缓存GUIStyle背景+原生GUI.Box绘制，面板失败关闭并只记录一次，样式缓存成功后才提交；Operator修正Texture2D类型和命名工厂委托以通过net6/IL2CPP编译。
- Worker session=`sess_d9becb8c-8fbf-4b15-8131-3080deb46483`，ZCode0.16.5；本轮native `model.sdk.stream.completed`证明bigmodel/GLM-5.3，max仅请求、无独立effort证明。候选构建0W/0E；175个可达Unity包装方法检查无unstripping失败桩，仍需实机视觉验收。
- 01:36热修部署完成：独立复核通过，HUD readiness覆盖全部样式；canonical0W/0E、175方法interop检查、allowlist/diff通过。E副本DLL SHA256=A9B115D201889A56C045A14F859C03E1EB662374651334B723C562E0471CEC04（342528 bytes），退出门禁/备份/原子替换/目标hash核验完成；用户实机视觉与交互待复验，release压缩包未更新。


## 2026-09-06 — Coordinate 接入与发布材料审计（准备中）
- 在隔离工作树补齐 EXharness 标准实例工具，新增 `coordinate-release-audit-20260906`。既有游戏条目保持不变。
- 当前只完成 initial file half，尚未向服务器 record，尚未执行发布材料审计或完成 Gate F。
- 未运行游戏、构建、部署或存档操作；原 Operator 的游戏开发继续独立进行。


### 2026-09-06 — Coordinate 静态审计结果待审
- Windows native MCP 完成 initial record 与同参数幂等重放；win-omp 已完成唯一 result.md 产出。
- Operator 复算 27 个输入 Git blob 的 SHA-256/字节数全部一致；结果结论仍待独立审查。未运行游戏构建、游戏测试、DLL 部署或存档操作。

### 2026-09-06 — Coordinate 正常 completion 文件半部已完成
- 独立 K3 结果审查 APPROVE；此前待审记录中的问题已修正，报告与当前 closeout packet 的 SHA 绑定见本任务 review-r3.md。checklist review.summary 保留了首轮修正指令，最终裁决以 decision=approved 与 review-r3.md 为准。
- Windows 原生 MCP 授权 receipt `2ef032bb-a167-4f5f-8c42-c650a7347d40`；本机使用正常 MCP preflight/claim/apply 完成文件半部，重复执行后 checklist 字节和 mtime 均不变。
- 既有 43 项未改变；报告输入 27 份 hash/size 复算通过。未执行游戏、构建、DLL 部署或存档操作。
- 本提交为 done 文件读回 gate；服务端 consume、PR 合入与原 Windows 目录安全同步仍待后续独立核验，本段不宣称 Gate F 已完成。

## 2026-09-07 希腊骑士火焰附魔（实施中）
用户确认持续8秒、冷却15秒并授权实施。实际敌人触发自己及触发快照随从；复用原生BuffData克隆、Buffable到期/RPC，不改共享资产或新建ID，不缩短已有更长附魔。ZCode worker 首版审查发现计数与RPC资格问题，已纠正中；独立API/测试/review并行。当前游戏部署仍2F089BA8幕府回冲伤害候选，新希腊功能尚未部署。见tasks/greek-fire-20260907/plan.md。

2026-09-07 Greek本轮收口：源码BF7254...，70/70与独立review PASS；本机部署EA5D1001...（build4.5.0-greek-fire-20260907），受控启动恢复暂停场景通过，存档hash一致；实际交战/联机待玩家游玩。前文“尚未部署”为过程记录。

## 2026-09-11 跨风格补员排查
用户确认白天回城附近弓手仍不补。实际2.4各style共用10s/10range原生流程；9/9存档22骑士各4随从，旧日志481行中280行弩箭诊断，没有现场补员原因。开发撤除旧逐箭日志+有界原生事件/现有缓存名册汇总，待实战证据再对症修复；不猜改规则。见tasks/squad-refill-20260911/plan.md。

2026-09-11 补员诊断本轮收口：本机8BA3448F...已部署并启动通过，存档hash相同；36/36测试与独立review通过。移除逐箭旧日志；新诊断有界且复用已有数组，无新场景扫描。待玩家实际缺员日志后修根因，checklist保持doing。

2026-09-11 22:28首轮玩家诊断：22骑士名册88/88、alive88且mismatch0；中世纪3+幕府4随从距骑士>10（读档早期快照，不能判持续走散）。无Fetch事件，当前暂停。尚未复现缺员，保持doing。

2026-09-11 22:35第二轮：sample2人数88/88、mismatch0、far10全0；北境明确补员3→4并转换成功。另发现Greek随从fire资源/poolguard失败（recipients1）、中世纪剑风sortingLayerName/ReadOnlySpan缺失异常，已记second-observation待修。本次未写游戏。

## 2026-09-11 武士视觉与两错误
用户批准浅白45/25/10残影/.2s淡出及Greek随从fire、Medieval剑风修复。数字sortingLayerID避开已坏字符串shim；实际Norse Archer prefab fireSO=NULL，准备补当前世界安全实例资源并双端一致。ZCode视觉worker、资产worker和独立测试/review进行中；游戏运行不部署。

2026-09-11 23:03三项修复本机部署7F0CBE8D...完成；170case/实际build0W0E/163Unity/review通过，启动日志确认Greek缺fireSO恢复成功，存档hash相同。武士残影及中世纪剑风实际视觉待下一轮，客机残影信号尚未实现；详见combat-visuals-20260911/acceptance.md。

## 2026-09-11 出征随从留墙
确认守墙NightFollowerAnchor将原生动态Object跟随改固定Position，原生follow等待不重发，NightParked又反复拉回；五style共用。改为ref临时offset保留Object/Wait，加ownedoffset归还及实际守墙/Assemble任务门，隔离worker/test/review中。见tasks/expedition-follow-20260911/plan.md。

### 2026-09-11 23:31 出征跟随本机修复就绪
已部署C1763E2F（4.5.0-expedition-follow-20260911），保留动态Object/Wait、临时守墙offset可靠归还及任务门。49/49独立回归、review、实际依赖构建与受控启动通过，存档hash一致；day61暂停场景不等于实际出征验收，任务doing。详细证据见tasks/expedition-follow-20260911/acceptance.md。

### 2026-09-11 23:59 希腊舰队配队/武士退速/四猫
E已部署F32CBE8C（4.5.0-fleet-retreat-cats-20260911）。希腊FleetBoat一对一Greek预留并取同侧可用min；仅武士原生防御后退3x；猫目标4含旧mod猫收敛。154tests+review+实际build与暂停startup通过，存档一致；实际登船/战斗/猫调整/联机待验收。残影接线无静态缺陷，未获得真实画面证据。详见tasks/fleet-retreat-cats-20260912/acceptance.md。

### 2026-09-12 v5.0.0发布准备
用户明确要求发布。将4.5后已集成改动与当前玩家说明封装为5.0.0，从clean提交构建并验证精确ZIP内嵌DLL，待发布读回；功能实战/联机边界继续doing。

### 2026-09-12 v5.0.0发布完成
正式Latest已发布，tag abf4eb3，完整ZIP DD7C26FA...、E内嵌DLL0549C166...；7套repo tests/clean build/独立包审查/精确启动/远端digest及tag核验通过。release任务done，实际战斗/船/猫/残影/联机等功能doing不变。详见tasks/release-500-20260912/publication.md。

### 2026-09-12 塔基已定位特殊升级塔漏检
5.0当前存档同址KEM_TowerSpot156.64与Tower Knight_greece；特殊塔无Tower标签/组件但有WB，原占位扫描漏。隔离修复加入建筑/施工占用并保护已付款对象，游戏运行暂未部署。

### 2026-09-12 00:59 塔基修复本机已部署
73B11F7D（5.0.0-tower-overlap-20260912），特殊WB与施工占位、实际template bounds、unknown不删/不生、付款保护已补；46tests/review/build/启动通过。day63暂停未执行5秒清旧，需下次游戏确认；公开5.0不变。另5.0实战日志已证残影ready/退速3x/猫retired8且存档当前岛16猫，记录为运行分支证据。

### 2026-09-12 重叠升级塔/追击/守家Greek本机热修
6DE8F6CB，build5.0.0-combat-home-towers-20260912。288tests、实际build/API/nativehook核验及受控暂停启动通过；存档一致。实际读档5秒清塔、狂战士完成跳劈后返程、Ninja黎明返程与守家火箭仍待实战。公开5.0不变，doing。

### 2026-09-12 税收官自动补货本机部署
A309B08E / 5.0.0-auto-restock-20260912：四职业独立目标，金库付款，现存+全店待领+预留防超买，最多2税收官采购；满店等待，默认off/15。事件维护角色/库存，无稳定期全体枚举。127tests/build/API/native hooks/review/暂停启动通过，save一致。实际开启投币/出货/转职/联机待测，doing；公开5.0未变。

### 2026-09-12 午后：特殊塔下普通已升级塔仍残留
用户实机证实Ballista/FireTower下残留两座，首次清理只移除Knight处Tower0。临时只读诊断5F26FF5D已105tests/build/API/review通过但未部署，等待用户保存退出E游戏。A309仍在运行；原删除规则和存档未手动修改。见tasks/tower-residual-20260912/probe-status.md。

### 2026-09-12 13:53 HUD像素改版与两处塔残留修复
2BD2055C已集成源码并部署E；顶部中央紧凑时间/金库条，左上人口暂不加。查明ExitGuardSlot触发同步重新补岗，改同Kingdom同步暂缓分配+停用后立刻注销空岗；109tests/负对照/review/native/build通过，实际清Tower4@166.66和Tower2@176.68两座，保留Ballista/Fire。受控save不变。HUD目视与正常保存再读待验收；AutoRestock实际采购未确认。未commit/push/release。

### 2026-09-12 14:17 全天采购与读档银行家引用修复
用户截图忍者4/15与狂战士7/10等待；真实卡点是kingdom.banker为空而控制器已绑定现有903银行家、4助手全空闲。共享精确控制器/银行家/世界判定替代错误nonnull假定，派工与扣款都修。夜间门按用户要求删除。42+51+49验证与独立review通过，实际忍者2金币/狂战士3金币采购成功、狂战士8活体+2待领达10；存档与测试bank恢复。654051E9已部署保留HUD/塔，未commit/push/release。

### 2026-09-12 15:55 无边框HUD、面包补员与采购进出场完成
8929889C已部署/源码同步。206检查、309Unity路径与review通过，正确E实机面包购买及出现→跑近→走开日志通过，用户确认HUD文字完整/边框消失。Peasant本机启用15；原四项保持。受控两次save不变，bank均恢复5423。误启动旧同名拷贝一次已停止，不作为验收；正确可见进程完整路径核验。未commit/push/release。

### 2026-09-12 自动补货双倍费用
BC504C61已安装/4源码同步；五职业含面包原价2倍投币和扣款、单份原生出货、卡片明示双倍。服务回归/63bank/226Unity/build/review通过；实机忍者4、狂战士6金币并完成离店。保存/配置/金库恢复，无公开发布。见tasks/restock-double-cost-20260912/acceptance.md。

### 2026-09-12 猫缩放1.25
B33D56B8已安装，唯一行为差异CatScaleY1.2→1.25；build0W0E、DLL常量与安装哈希核验通过，save/bank未动。见tasks/cat-scale-125-20260912/acceptance.md。

### 2026-09-12 v6.0.0发布准备
用户授权发布，整理5.0之后的自动补货/双倍费用/HUD/塔位/猫1.25等修改，从clean提交构建正式包并读回GitHub验证。

### 2026-09-12 6.0发布前追加交战锚点修复
用户指出随从追武士使返队距离阈值失效，并要求停驻攻门/大蛇保阵位。6.0仅本地8d372f9未推送，修复/审查/实机后继续发布，见samurai-defense-anchor-20260912。

### 2026-09-13 交战阵位源码修复通过
夜守家动态offset/Charge到站锚点、原生超距归位及加载期Original保留已整合。199随从/123运动、负对照、build/API/native、独立review通过；进入6.0精确包实机检查。

### 2026-09-13 v6.0.0正式发布
356078c固定源码，ZIP416b6c79 / DLLcdf5f61f；clean构建、10套回归、完整runtime/包检查、精确DLL自然夜间锚点与原生退速观测、GitHubLatest/tag/digest读回通过。两项发布前P2已修；真实攻门/大蛇/联机等边界继续跟踪。见tasks/release-600-20260912/publication.md。

## 2026-09-13 — v6.1.5正式发布

标签4aebcd0，ZIP487089ec / 正式DLL2ce32e34；人数HUD与剑风修复。clean构建/11回归/完整包/独立审核及远端Latest与digest通过。1309方法仅启动版本日志变化。用户游戏运行中，本机仍FFDD0E89，本次未替换/启动6.1.5，存档配置不动。见tasks/release-615-20260913/publication.md。


## 2026-09-14 三个可选便捷开关

本机独立E副本已安装B8200382 / build=6.1.5-optional-qol-20260914；F5「便捷」页三个独立开关，默认off，全部世界可启用。长按0.6秒后投币间隔为原1/4（下限0.03秒且不放慢原值），安全商品同店可连续购买，首段及金币落槽完成检查保持原生。灌木生成间距减半，关闭只快速枯萎extra，真实回收/归还完成前锁住再次开启。森林自然消退等待缩为1/3，新调用生效。银行和缩放Greek-only、兔子原生大小、普通鹿3倍及隐士热修保持。

原17组回归全过；新增购买25、植被31行为场景、实际all-world scope17检查全过（强制Rebuild，防copy2旧时间戳误用旧产物）；IL2CPP强制Rebuild 0W0E；257实际Unity可达方法无unstripping stub。1427旧方法中1422完全不变，仅Plugin标记、ModConfig.Init、ModPanel.Update/DrawControls/.cctor变化，无旧方法移除；全部旧Harmony属性保留。新增5个native目标均核对实际2.4唯一长入口，World.AddThicket复用既有目标；未回引Critter或隐士短getter。

精确DLL受控启动PID18232约110秒，Running标志与chainloader成功。第一次候选因Harmony识别Prefix辅助函数而在4.9秒内自动停止并恢复基线；改为非约定命名后重新跑全部新回归、构建及启动。存档3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A保持，配置逐字节恢复，金库5709→5709；原隐士getter17原字节保持。既有NpcShieldUser.SetShieldEnabled错误独立遗留，未由本任务修复。

待正常游玩验收：真实按键长按/松手/满货架与两机联机回包；灌木生长、关闭枯萎和季节/换岛/读档；连续砍树及森林实际消退。因此checklist保持doing，启动不等于以上玩法已实测。植物世界修改仅world authority执行；客户端开关不代替主机的世界设置。未commit/push/更新公开6.1.5，未写D盘Steam，不恢复旧存档。


## 2026-09-14 农民与攻城弹药补货

本机正确E独立测试副本已安装00FA75CA / build=6.1.5-restock-supplies-20260914。F5自动补货新增农民·镰刀、投石车·火药桶、希腊火焰塔·弹药，各自开关与1–200全岛阈值，默认off/15；现有Peasant面包保留。自动采购仍仅Greek、同一银行与税收官订单队列、两单全局上限；沿用原价2倍（火药桶10、火塔弹药4），手动原价不变。左上常驻人数HUD新增两弹药数量，单机/主机所有world可读，不受自动开关影响。

21组回归全部通过：服务119、职业/商店缓存84、HUD32、弹药36；含旧bank32/真实助手17、长按25、植被31、scope17及其他既有套件。完整IL2CPP强制Rebuild 0W0E；310实际Unity可达方法无unstripping stub。1531旧方法中1481完全不变，变化仅本任务允许类；全部旧Harmony属性保留，仅新增PayableManager Add/Remove两个唯一长native目标（656/608B）。银行、缩放、兔子、鹿、隐士与三个QoL原有热修保持。独立内置review最终无阻断。

精确候选受控启动PID29096约110秒，Chainloader及RunningGame成功。原生弹药缓存日志：[Info   :KingdomEnhancedMod] [SiegeAmmoCounts] ready=true sites=0 barrels=0 fireJars=0。当前自动加载场景无弹药站点，已验证可信空库存与HUD启动，不能据此声称真实采购成功。存档3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A、配置逐字节与金库5709→5709保持；隐士短getter17原字节未改。仅既有NpcShieldUser.SetShieldEnabled错误，未新增本任务错误。测试结束先恢复基线再安装同一候选。

待正常游玩验证：真实农民购镰刀/拾取，投石车购买滚桶→装填→发射，火塔购买→消耗→再次补给、多站点均衡、换岛读档与两机联机同步、HUD实屏观感。checklist保持doing；未commit/push/更新公开版本，未写D盘Steam，未回滚旧存档。原价/双倍提问未获回复，按已说明的现有双倍规则实现，可按后续用户回复调整。


2026-09-14：新增希腊随从火矢范围爆发实现任务，半径0.25/1点/同轮散射去重，现有开关默认关闭。OMP worker+独立reviewer，本机候选，尚未安装或发布；见tasks/greek-fire-impact-20260914/plan.md。


2026-09-14本机5CED0D25/build7.6.5-greek-impact-20260914：希腊style3骑士火焰窗口的随从火矢，半径0.25/1点Fire/直接目标排除/同轮散射去重，沿用F5弓箭ImpactEnabled默认off与作者像素动画。窄TryDamage保留原生直接伤害且不写原生字段；73核心+45FX+75散射及30测试项目/1interopbuild、0W0E/2354API/1937无关方法保持/独立review通过。闭游戏安装到正确E，旧DLL已备份，save/config哈希保持，未启动游戏或发布；实机/压力/联机仍待。见tasks/greek-fire-impact-20260914/acceptance.md。


2026-09-14本机9F1F61A0/build7.6.5-samurai-diag-20260914：武士冲刺被动诊断[SamuraiDiag]，armed/start/trail/visual-ready/skipped/first-update/stop/tail-cleared/end，突发8次后每游戏秒补1次完整链、每链最多12行；无新native钩子或扫描。123冲刺+15视觉/日志+9诊断测试、0W0E/2374API/1941无关方法保持/独立review通过；闭游戏安装，save/config保持，未启动/未发布。保留希腊火矢0.25所有更改，真实冲刺日志待玩家游玩。见tasks/samurai-diag-20260914/acceptance.md。


2026-09-14 骑士身份任务进行中：采用独立 sidecar GUID/style + 完整原生岛快照映射，原版不会覆盖附加文件；原版重存若快照失配则安全重新建档。新招募补少、主机 nonce 握手、池复用隔离。实现/review中，未部署，细则见 tasks/knight-identity-20260914/plan.md。


2026-09-14本机9881275D/build7.6.5-knight-identity-20260914：骑士GUID/style独立附加档，精确完整岛快照匹配；旧档首次迁移、新招募补少，registered后nonce主客确认，池复用/坏slot/容量/版本保护。37项目回归、36archive+25runtime+48network+9整合断言、0W0E/2609API/2004无关方法保持/两名review通过。游戏关闭时原子安装，旧9F1F备份，当前save57E54166和configA3E3A0B8保持；未启动/提交/发布。真实保存读档与联机待验，原版重存失配可重新建身份，匿名换岛逐人延续未实现。
详见 tasks/knight-identity-20260914/acceptance.md。


2026-09-14本机1D63533A/build7.6.5-runtime-log-fixes-20260914：火塔满仓明确等待消耗并核真实可用槽，保留原CanPay/扣款；Rewired关闭后只跳Menu.SetMenuInput(false)失效输入写，保留其余退出清理。120补货+36弹药+14容量+8菜单、2interop编译、0W0E/2617API/2242无关方法保持/独立review通过。闭游戏安装备份9881275D，save57E54166/configA3E3A0B8保持，未启动/提交/发布；保留骑士独立档及所有旧修复，实际退出/补货与附加档保存读档仍待验。
见 tasks/runtime-log-fixes-20260914/acceptance.md。


2026-09-14本机C6B71AA6/build7.6.5-medieval-scatter-20260914：散射限当前中世纪style0骑士随从攻击敌人，打猎/其他类型单发；F5中世纪随从散射总箭1～3，只有额外箭淡金实例色，池复用恢复，6byte版本初始化由主机决定。44项目通过（88combat/16policy/44tint及实际interop）、0W0E/2651API/2246无关方法保持/独立review通过。闭游戏备份1D63533A后安装，save57E54166/configA3E3A0B8保持；未启动/提交/发布。主客需同版本，真实颜色/回收/狩猎/联机及旧身份档验证仍待。
见 tasks/medieval-scatter-20260914/acceptance.md。


2026-09-14本机7D3D2926/build7.6.5-hermes-disguise-cycle-20260914：友好巨魔戴原生面具/44款新增头饰免新主动选敌及反制追击，伤害不变/已起手允许完成；默认30%保留，命中后44款顺序循环，独立本机全局游标原子落盘。48项目通过（48保护/26轮换/62头饰整合及interop）、0W0E/2671API/2281无关方法保持/独立review通过；闭游戏备份C6B71AA6安装，save57E54166/configA3E3A0B8保持，未启动/提交/发布。真实战斗/AOE/头饰观感/保存读档/换岛/联机仍待。
见 tasks/hermes-disguise-cycle-20260914/acceptance.md。


2026-09-14本机63105375/build7.6.5-hermes-quota-20260914：用户明确将头饰独立随机30%改稳定配额，累计每10新转换3顶（4/7/10），v2 credit文件只读迁移v1 next8，已有actor不重分配；新增有界被动[HermesHeadwearDiag]用于显示调查。51项目/26quota+36真整合+16diag+62core+27visual、0W0E/2715API/2287旧方法保持/review通过。闭游戏备份7D3D后安装，save57E54166/configAD2CB236及v1游标保持，v2未预写；未启动/提交/发布。头饰显示根因仍未确认，真实10只3顶/外观/联机待验。
见 tasks/hermes-headwear-visible-20260914/acceptance.md；后续英雄弓手美术准备授权见同任务plan。


2026-09-14本机444E1611/build7.6.5-knight-load-seed-20260914：修首次迁移只在原生Save才写骑士附加档，现加载冻结来源、完整名单确定后由既有巡检整批写附加档，不保存重进也可恢复；保留精确快照/同life/双向唯一、Squire明确排除、失败等待/拒绝。53项目通过（seed34/runtime26含22名无Save真实接线重载/网络整合9）、0W0E/2750API/2340旧方法保持/review通过。闭游戏备份63105375安装，save57E54166/configAD2CB236及已有MOD附加文件保持；未启动/提交/发布。真实连续两次启动未Save验证仍待，头饰显示调查与英雄弓手美术准备仍排队。
见 tasks/knight-load-seed-20260914/acceptance.md。


2026-09-14英雄弓手美术准备：用户已要求开始，后明确火红披风/飘带并加长随风飘；内置image_gen经4稿得到深蓝绿兜帽+长火红双飘带静态候选concept-04。OMP Flash max按实际clip引用修正重名混帧，121原版Sprite/15clip及裁剪/32画布参考重建/8x共242组像素核验通过，独立review纠正技术规格。仅美术与文档，IL2CPP源码哈希保持，已装444E1611不动；正式原创像素动画、风摆、玩法和游戏验证未实现。
见 tasks/hero-archer-art-20260914/acceptance.md。


2026-09-14英雄弓手第05稿：用户指出第04稿身形过瘦、腿长且真人比例，要求贴原版卡通风格；内置image_gen以实际原版s8941作比例参考，修成矮壮、大头短肢的候选，长火红双飘带保留。当前候选concept-05-cartoon-proportions.png，提示词concept-05-prompt.txt；仍是静态设计待用户确认，不是生产sprite/动画或游戏验证，未改DLL/源码/存档。


2026-09-14 英雄弓手第06稿：用户认为第05稿头过大，要求弓借鉴游戏神器。内置 image_gen 缩小头/兜帽，保留矮壮身体与长红双飘带；参考实际 resources.assets 的 artemis_bow_reward（pathID 9005）形状预览，改金色反曲卷梢弓及象牙白弦。当前 concept-06-artemis-bow.png，准确提示词 concept-06-prompt.txt。仅静态外观候选，未实现动画/玩法，未动 DLL、源码或存档。


2026-09-14 用户明确确认："可以，这最后一版不错"。第06稿 concept-06-artemis-bow.png 的外观方向已确认，锁定当前头身比例、深蓝绿兜帽、神器风格金色反曲弓及长火红双飘带。后续像素与动画制作以此为基准；此确认仅为外观定稿，正式 Sprite、动画、玩法和游戏验证仍未完成。


2026-09-14 英雄弓箭手实现中：用户确认继续实现、第06稿外观、全世界F5开关/每侧至多1普通弓手转英雄/关闭恢复、射速1.5/索敌射程1.25/总3箭/火焰半径0.25额外1点。另明确授权本地脚本清背景、切帧、对齐脚点及统一像素尺寸；用户指出生成动作图不够像素风，当前已整理48x32帧、PPU32、18色/二值alpha的16帧草稿，仍待动画一致性和实机观感验证。OMP Flash max隔离worker负责runtime/combat，内置reviewer独立审核。游戏/DLL/存档暂不动，旧修复保留。


2026-09-14 本机 DE72F01F / 7.6.5-hero-archer-20260914：第06稿原创英雄弓手，F5默认off/全部世界单机/每侧1名/关闭恢复，1.5射速(与旧取max)、1.25私有SO弹道范围、总3箭、0.25/1点范围伤害同volley去重；16帧真像素+独立双红飘带，原生风有限映射、行波、重力触地、跑后飘/转向重基。7新组及旧88/73/50回归、普通+真实2.4构建0W0E、2357旧方法保持/27授权修改、review通过。闭游戏备份444E1611安装，save/config保持，未启动/提交/发布；游戏观感/战斗/切岛待验，在线英雄同步未实现且整项关闭，不称联机完成。


2026-09-15 英雄神器金箭实现中：用户要求英雄射出的箭使用神器弓金箭；纯外观，覆盖主箭、额外散射箭及打猎主箭，沿现有英雄开关/单机范围，不修改原生Artemis追踪/伤害/碰撞。已核真实资源Sprite9867 artemis_bow_arrow 30x5/PPU32/pivot0.5，原生Renderer79915/材质42 Highlight金色；OMP Flash max隔离worker实现有界生成scope与池归还，Operator负责资源/集成/验证。


2026-09-15 本机 301FF296 / 7.6.5-hero-artemis-arrow-20260915：英雄全部箭使用原生 Artemis 30x5 金箭贴图与实例金色（主箭、散射、打猎），沿现有开关/单机门；材质/碰撞/伤害/弹道不变。发射作用域限定资格、有界128外观回执、池复用/关闭/换世界归还，仅接管RGB并保留alpha。144+14+14+14行为、88散射/146英雄/85希腊/50特效回归通过，普通及真实2.4构建0W0E，2587旧方法保持/3授权集成改动，独立review通过。闭游戏备份DE72后安装，save/config不变，未启动/提交/发布；实际箭头尺寸/发射位置/池复用观感待游戏验证，英雄联机仍关闭。


2026-09-15 用户更新 worker 分时规则：北京时间周一至周五09–12点本地ZCode优先/OMP备选，GLM 5.3 max；14–18点内置subagent；其余时间OMP DeepSeek Flash max。边界为左闭右开，工作日暂按周一至周五；跨时段当前任务先完成，后续派发重新判定。Reviewer约定不变。已同步AGENTS与collaboration-protocol，未调用worker或修改游戏。


2026-09-15 本机 758B5990 / 7.6.5-hero-sorting-20260915：新增公共HeroVisualPriority，英雄身体位于GameLayer参考深度-0.05，双飘带随后0.002且同body排序，使已知普通角色（含Greek z0）重叠时英雄优先；可能盖住场景贴图，UI代码不改。仅改MOD自有表现，不动角色原生位置/碰撞/伤害。失败预检/部分写回滚与整组撤销、非有限/倾斜/shear/非单位z缩放门；未来英雄可复用。29深度+33布料+65生命周期通过，普通与真实2.4构建0W0E、2627旧方法保持/3授权修改、独立review通过。检测游戏已关闭后备份301FF296安装，save/config不变，未启动/提交/发布；真实遮挡及此前未见英雄根因仍待实测，原有特殊动作/联机未完成。


2026-09-15 用户明确确认英雄显示优先策略：英雄优先，允许盖住重叠场景贴图，界面不变；与758B5990已装候选一致，后续英雄职业复用此规则。


2026-09-15 本机 FAE6FBDD / 7.6.5-hero-walk-scale-20260915：用户实测英雄移动像滑行且要求整体0.9。修正goal=false错误Idle路径，优先原生Animator Speed（暂停为0）、读取失败/非有限才回退指令速度，abs左右均可；ModPanel既有Update直接Sync并沿用frame去重，未认定原LateUpdate失效。自有body/双cloth/肩锚XY绝对0.9，保留flip与-.05/.002置前，不改actor/碰撞/战斗/金箭/素材。缩放失败整组撤销；sprite赋值成功后每VisualState最多16首见帧日志[HeroArcherMotion]。40新检查、13281动画、65生命周期/68布料逻辑/33布料显示/29深度通过，普通与真实2.4构建0W0E、2626旧方法保持/8授权修改+1私有MotionOf签名替换、独立review通过。闭游戏备份758B5990安装，save/config不变，未启动/提交/发布；现场滑行是否完全解决、0.9观感仍待实测，特殊动作/联机仍未完成。


2026-09-15 英雄动画研究：用户实测FAE6移动闪烁，要求先学习原版动作拆分/衔接并沉淀文档。本轮只读研究，不改生产代码/DLL/配置/存档、不操作游戏。实际2.4引擎Unity6000.0.61f1（非旧Mono2022.3）；提取8控制器及15clip离散PPtr时间轴164keys，补AnyState/default/layers/raw，3run各2脚步事件；Greek准备26keys仅6distinct。原版base archer7状态、Speed门1/.005、Idleness与Prepare可被移动退出；原run保留1–2px离地，旧草稿bbox贴底30抹去高度差。45首见帧/16selected12actors只能证实推进/当选记录，不能定闪烁频率或根因。已写game-logic-map/character-animation-production.md和可复现脚本/对照图，后续按原生状态与时间轴、完整生命周期和像素动作规格重做原型；英雄整体仍doing，当前闪烁未修复。


### 2026-09-15 英雄商店设计调查

2026-09-15 英雄商店进入设计：用户要求原创商店刷新领地中段。已检查英雄自动选举、原生PayableComponent和独立存档方案，生成首张木石驿站/红旗/金弓草图。每岛一座/8金币升级现有弓手/面板改控商店仍为待用户回复的建议，不是已确认规则。Payable owner接口注入及CRPCHeader生命周期须实测；付费身份必须与临时战斗资格分离。本轮未改玩法代码、DLL、配置或存档，正式8.0保持；任务doing，见tasks/hero-shop-20260915/plan.md。


### 2026-09-15 英雄驿站候选与后续边界

2026-09-15 英雄驿站当前岛候选完成，未安装/发布：8币训练现有弓手、每侧固定1席直到确认死亡；关闭/塔/丢弓转职/临时停用保留购买，真实回池不串人。独立sidecar精确快照+pin基线，修原生落盘失败回退锁槽、原版重存未知身份静默丢失、新岛初始化及OnEnable误解绑；未知身份保留名额并暂停收费。原创512x80四帧商店贴图，原英雄2PNG/围巾/金箭保留。build0W0E、87购买/81效果/31商店/146+85xUnit、actualinterop及独立93回归通过；DLL审计2665旧方法不变/10集成修改，候选SHA 70d53395. 跨岛名额范围待用户选择，运输身份桥和实机验收未完成；E盘仍80522bf1正式8.0.0，未操作游戏或用户数据。


### 2026-09-15 英雄地面岗位与旗帜状态

2026-09-15 英雄驿站地面/旗帜候选 a6b5d90b / build=8.0.0-hero-shop-flags-ground-20260915：已购启用英雄不接箭塔岗位，已在塔位通过原生Exit退出；普通补位、骑士任务和关闭恢复原生保留。用户拒绝文字牌，现改空挂点/原生投币→完整金弓红旗占位→确认死亡破旗，Reserved保留完整旗。旧装饰旗转为真实占位旗，无文字/运行字体；破旗仅会话反馈不影响持久购买。109购买状态/31塔/81效果/31商店回归、完整和实际interop构建0W0E、独立2.4原生/像素/代码review通过；2665旧方法保持/10集成修改、原英雄2PNG保持。未安装/启动/发布，E仍80522bf1正式8.0。跨岛名额范围未答、运输桥和实机未完成；最新候选在operator hero-shop-20260915/candidate-ground-flags。


### 2026-09-15 英雄驿站本机接入

2026-09-15 用户确认接入后，已在游戏关闭时备份并安装a6b5d90b英雄驿站候选到正确E盘独立副本，build=8.0.0-hero-shop-flags-ground-20260915。备份原正式80522bf1并核对，22份原生存档/配置/附加档hash保持，未启动游戏。可测试当前岛8币购买、无文字占位旗/死亡破旗、英雄不上箭塔；109购买/31塔/81效果/31商店及构建/独立审核证据沿用精确候选。这是本机候选安装，不是再次发布；公开8.0.0不变。跨岛名额范围/身份运输仍未完成，实际投币、显示、死亡与撤塔仍待实机。


### 2026-09-15 英雄驿站运行接口修复

2026-09-15 英雄驿站实际未出现已定位并修候选f8c25095 / build=8.0.0-hero-shop-owner-interop-20260915。a6b5实际日志类型注册后InvalidProgramException；根因为本机ClassInjector对out enum生成ldobj LockReason&非法Invoker，注册/Marshal成功直到首次JIT才失败。只改自有IsLocked为ABI等价IntPtr输出桥，精确WriteInt32(NotLocked21)，保留原interface预检并核写回，增加首异常阶段/完整栈。7真实production指针/JIT/guard断言、31商店、完整和actualinterop0W0E、独立复现与review通过；2862方法不变/4方法改动，旧IsLocked换签名+编译器闭包编号变化，全部4PNG保持。修复候选已就绪；用户游戏运行中，尚未替换DLL；未启动游戏/公开发布，preflight passed+ready及实际投币待验证。跨岛仍未完成。


### 2026-09-15 英雄驿站运行接口修复

2026-09-15 英雄驿站实际未出现已定位并修候选f8c25095 / build=8.0.0-hero-shop-owner-interop-20260915。a6b5实际日志类型注册后InvalidProgramException；根因为本机ClassInjector对out enum生成ldobj LockReason&非法Invoker，注册/Marshal成功直到首次JIT才失败。只改自有IsLocked为ABI等价IntPtr输出桥，精确WriteInt32(NotLocked21)，保留原interface预检并核写回，增加首异常阶段/完整栈。7真实production指针/JIT/guard断言、31商店、完整和actualinterop0W0E、独立复现与review通过；2862方法不变/4方法改动，旧IsLocked换签名+编译器闭包编号变化，全部4PNG保持。已闭游戏备份安装正确E盘，用户存档/配置hash保持；未启动游戏/公开发布，preflight passed+ready及实际投币待验证。跨岛仍未完成。


### 2026-09-15 英雄驿站地面基准

2026-09-15 英雄驿站沉地修复已闭游戏备份安装E盘：d005c4e1 / build=8.0.0-hero-shop-grounding-20260915。前版f8c25095已真实通过owner预检、ready与purchasecompleted，但用户截图下半部被地面挡。实际94商店资源rootY0.875/0.88、rootbody bottompivot0/PPU32，旧自有root错用GameLayer.y。现仅取当前world活动Bow/Hammer/Scythe、根body可用且pivot0的worldY，主体/旗/币槽一起抬升，缺参考延后；保留自有pivot2px及底边约1px草沿，不改PNG/横向选址/z/付款。Core39/Invoker7/actualinterop与完整build0W0E、独立资源/代码review通过；对f8审计2868旧方法全同，仅Create和buildstamp改变、2新增方法、4PNG保持。备份/安装hash与全部用户数据hash通过，未启动/提交/发布；新高度仍待用户截图实测，跨岛仍待。



<!-- hero-shop-stability-20260915 -->
2026-09-15 英雄驿站暂停闪烁修复：cfe8e50a / build=8.0.0-hero-shop-stability-20260915。用户确认主要开关暂停菜单整座消失；旧TryContext只接受Playing导致Menu清理重建。现仅精确Playing/Menu、同kingdom/layer/当前Postbox/header且对象有效时保留；Menu阻止付款，首次暂停有待付币时复用原生取消路径，成功后标记，恢复复用原对象；其他状态和未知context立即清理并记录原因。Core54、Invoker7、实际2.4 interop与完整构建通过（后两者0警告0错误），生产接线审计和独立审核通过。对d005审计2861方法保持、8改变、8新增、3移除（含Clear签名与闭包编号调整），4PNG保持，地面基准与owner ABI保留。已在游戏关闭后备份安装正确E盘，存档和配置hash保持；未启动游戏/提交/发布。暂停保留、半途投币暂停、恢复付款与实际高度仍待实机验收，跨岛身份运输仍未完成。
<!-- hero-shop-stability-20260915 -->
