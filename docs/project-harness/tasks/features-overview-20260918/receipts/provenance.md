# provenance：MOD_FEATURES_OVERVIEW_ZH.txt 条目→来源对照

来源缩写：CAP=release/MOD_CAPABILITIES_AND_ROADMAP_ZH.txt（行号按当前工作区版本）；
GUIDE=release/MOD_USER_GUIDE_ZH.txt；LOG=release/MOD_UPDATE_AND_FIX_LOG_ZH.txt；
SRC=il2cpp/ 源码核验（文件:行）。历史材料仅作发现线索，数值一律以现行 txt 或源码为准。

## 30 秒速览 / 新角色 / 战斗 / 经济 / HUD（主体条目）

| 概述条目 | 来源 |
|---|---|
| 英雄驿站 8 金币训练、每侧 1 名、确认死亡释放、不上箭塔 | CAP:9；GUIDE:28 |
| 英雄移速/射速 1.5、射程 2 倍、对敌 3 箭、半径 0.25/邻近 1 点、无长期灼烧 | CAP:10；GUIDE:29 |
| 英雄外观（兜帽/金弓/金箭/双尾围巾/持弓姿势/31 帧槽/0.9/回原生） | CAP:11；GUIDE:30 |
| 火铳铺 4 金币、侧枪架最多 3 把、旧档迁移、居民拾取转职 | CAP:12；GUIDE:23；LOG:8 |
| 火枪伤害 2、射程 1.5 倍、装填较慢、首敌阻挡、两侧守位 | CAP:12；GUIDE:23 |
| 火枪外观 0.9、五帧红色枪口焰 50ms/0.25s | CAP:12；GUIDE:24；LOG:9 |
| 白天猎普通鹿、掉钱原生、不伤小动物/坐骑、希腊鹿直线瞄准 | CAP:12；GUIDE:26；LOG:14 |
| 举旗最多 4 名火枪手后排、不凭空生成、读档先收旗 | CAP:12；GUIDE:27；LOG:13 |
| 伤害可靠性共用同步原生流程 | CAP:13；GUIDE:25；LOG:12 |
| 英雄/火枪仅单机、联机含主机关闭 | CAP:14；GUIDE:35 |
| 五种骑士风格列举（剑风/死地/北境持盾/幕府冲刺/希腊火焰）+弩手/忍者/狂战士 | CAP:17；GUIDE:59 |
| 骑士身份附加档+精确快照、不重种、Squire 排除 | CAP:18；GUIDE:63 |
| 中世纪散射默认关、总 1–3 默认 3、额外箭淡金、打猎不触发 | CAP:19；GUIDE:31 |
| 弓箭手射速 1–2 倍默认 1.5、英雄取 max | CAP:19；GUIDE:32 |
| 希腊火矢爆发默认关、半径 0.25/邻近 1 点替代灼烧 | CAP:20；GUIDE:33 |
| Hermes 头饰默认开、累计配额每 10 次 3 顶（第 4/7/10）、44 款轮换、免新锁定 | CAP:22；GUIDE:38-39 |
| 希腊银行、四税收助手、每趟约 20 枚、断流等 4.2 秒 | CAP:25；GUIDE:56 |
| 自动补货 9 项清单、默认关/15/1–200、2 倍价、最多两单、仅白天、火枪 8/手动 4 | CAP:26-27；GUIDE:49-52 |
| 人口 HUD 默认开、火枪剥离、单机/主机、客机提示 | CAP:28；GUIDE:53 |
| 时间/银行 HUD 默认关、金库栏仅希腊 | CAP:28；GUIDE:19,53 |
| 鹿 ×3/间隔 ÷3/单只 3 金币 [希腊，单机+主机] | CAP:30；GUIDE:58 |
| 农舍 4 猫 1.25、Critter 原版大小 | CAP:30；GUIDE:58 |
| 地图/塔位/怪物时间线/君主速度/快速建造/舰队/特殊塔重建保留 | CAP:31；GUIDE:19-20,59 |
| 便捷四项默认关、无限体力本机坐骑、长按 0.6s→1/4（桶 5/弹药 2）、灌木双密、森林 ÷3 | CAP:32；GUIDE:42-46；LOG:10 |
| 默认开关速览逐条 | GUIDE:17-20（第二节原文镜像）；尾段弓箭页/头饰/便捷/补货默认=GUIDE:22,38,42,49 |
| 已知限制各条 | CAP:34-39；GUIDE:62-72 |

## 历史材料发现、经源码核验收录的条目

| 概述条目 | 核验来源（SRC） | 历史线索 |
|---|---|---|
| 农田金币成熟 3 游戏秒，与普通掉落币一致，由助手收取 [希腊，随税收助手] | PatchEconomy_BankAssistants.cs:46 FARM_COIN_MATURITY_SECONDS=3f（与 :32 普通币 3f 相同）；:1172 按农田来源币应用。审查更正：历史 12s"留玩家捡"设计已按用户决策改为 3s（git 6ba2b71），:42-45 常量注释为陈旧描述 | worker-brief 提示 + 内容审查更正 |
| 盾墙图腾：希腊城墙旁 1 金币、北境持盾队结阵冲锋、付一次一队 | PatchWorld_ShieldWallTotem.cs:100-101,173-174 希腊 biome 门；:408-410 单机语义；头注释付费语义 | worker-brief 提示 |
| 大蛇缰绳：休息位推离城墙 100、远程吐怪节奏保持 | PatchWorld_SerpentLeash.cs:39 MinDistanceFromWall=100f；:208 HasWorldAuth（单机/主机）；头注释吐怪修复 | worker-brief 提示 |
| 弩箭塔弩箭速度 ×1.25 [全部世界，随总开关] | PatchWorld_BallistaBolt.cs:13 ShootForceMultiplier=1.25f；:18 ModConfig.Enabled 门 | 自行发掘（文件清单） |
| 赫尔墨斯钱袋解锁+扩容（2000/1000）+UI 适配 [随总开关] | PatchEconomy_CurrencyBag.cs:32-36 常量；:66 等 ModConfig.Enabled 门；OnGameStart 解锁两名玩家 | 自行发掘 |
| 主船容量 8 工人/6 骑士/8 长枪兵/3 农民、弓手原生四槽 [随总开关] | PatchWorld_BoatCapacity.cs:31-35；头注释"main Boat only / Archer native four-slot" | 自行发掘 |
| 北境骑士钱包容量 ×2、纳税阈值 ×2（不动玩家钱包） | PatchRoles_MedievalNorsePowers.cs:15-17 头注释（实现 :143,:205 写 _originalWallet） | 自行发掘 |
| 中世纪劈砍范围 ×1.5+剑风 | PatchRoles_MedievalNorsePowers.cs:13,40 SlashRangeMultiplier=1.5f | 剑风见 CAP:17/GUIDE:59 |
| 死地攻击节奏 ×2、移动 ×1.5 | PatchRoles_DeadlandsPowers.cs 头注释 | CAP:17"死地攻击/移动强化" |
| 北境随从转真北境持盾弓箭手 | PatchRoles_NorseSquad.cs 头注释（NpcShieldUser+程序装盾+5s 巡检） | CAP:17"北境持盾随从" |
| Cerberus 召唤增援 +1 希腊队/+2 北境队 [希腊，单机+主机] | PatchDivine_GhostSquads.cs:48 希腊门；:238 ModConfig.Enabled+HasWorldAuth；头注释编制 | 自行发掘 |
| 狂战士自主追击有界（缰绳 12） | PatchCombatChaseGuard.cs:7-8 BerserkerLeash=12f | 自行发掘 |
| 幕府冲刺返队（含返队命中） | PatchRoles_SamuraiPowerDash.cs 存在；CAP:17/GUIDE:59"幕府冲刺返队"；AGENTS 2026-09-07 返队伤害记录 | 现行 txt+源码存在性 |

## 历史数值陷阱核对（均按现行口径书写）
- 英雄射程写 2 倍（非历史 1.25）：CAP:10/GUIDE:29。获取写驿站 8 金币（非自动选举）：CAP:9。
- 自动补货写 9 项（非历史 8）：CAP:26/GUIDE:49。
- Critter 写"保持原版大小"：CAP:30/GUIDE:58。
- 中世纪散射默认总 3 箭：CAP:19/GUIDE:31。
- Hermes 头饰写累计配额每 10 次 3 顶（非 30% 概率）：CAP:22/GUIDE:38。

## 候选节
- ~~按任务书措辞逐字写入第七节~~（v2 任务书：整节删除，正文零候选内容；grep 自检通过）

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# v2 增补（2026-09-18 上午，worker-brief-v2）

来源缩写新增：V*＝release/MOD_V*版本更新说明.txt（仅作发现线索，数值以现行 txt/SRC 为准）。

## 本轮从版本说明发掘、新收录的条目（全部经现行源码或现行三文档核验）

| 概述条目 | 核验来源 | 发现线索 |
|---|---|---|
| 弩手获取：每第 4 名（3:1 循环）、读档校准比例、不入骑士队 | PatchRoles_Crossbowman.cs 头注释（"每第 4 个（3:1 交替）"）；:76-83 数值单一来源注释 | V3 第一节 |
| 弩手数值：伤害 2/完美×2、地面射程 12、塔位 ×1.5（12→18）、装填 ×2 | PatchRoles_Crossbowman.cs:78 BoltHitDamage=2（注释"perfect 自动 ×2 = 4"）、:53 塔位射程、:48 间隔 ×2 | V3/V4.5 第二节 |
| 弩手观感：死地士兵+旗帜色染衣+1.15 体型、初速 ×2 平直弹道、常显光痕拖尾 | PatchRoles_Crossbowman.cs:17（初速 ×2）、:76（y 缩放 1.15） | V3 第一节 |
| 忍者夜间伏击：灌木 3/树 1/帐篷 5、同侧最近有空位选点、白天钓鱼 | PatchRoles_Ninja.cs THICKET_ANCHOR(3)/TREE(1)/BEGGAR_CAMP(5) 锚点常量 | V2 第五节 |
| 狂战士第 6 次出队长（长柄斧） | PatchRoles_Berserker.cs 头注释 + LeaderSlot=6 | V2 第五节 |
| 忍者/狂战士招募价：原生 2/3 金币，自动补货 4/6 | GUIDE:51（"忍者 4、狂战士 6"）+ V6 第一节原生价 | V6 第一节 |
| 狂战士白天不追撤退敌人 | V6 第四节；现行 PatchCombatChaseGuard/PatchRoles_Berserker 存在 | V6 第四节 |
| 主银行家只收第二道墙之间、提款持币目标 39→100、25% 比例逐枚吐币 | PatchEconomy_Banker.cs:44 ENHANCED_PLAYER_PAYOUT_TARGET=100 | V2 第一节 |
| 助手一路吸收不停顿、约 8 枚起增派最多四人、远处币先传送、单次入账 | PatchEconomy_BankAssistants.cs 头注释（原子入账/四人池）+ ACTIVE_SCALING_STEP=8f +:20 四名 synced 对象 | V2 第一节 |
| 幕府冲刺：CD 3s/行程 7/冲刺无敌/残影 3 道 45/25/10%、返队有伤害 | PatchRoles_SamuraiPowerDash.cs:14（Cooldown=3f, MaxRange=7f, FollowLeash=10f）；残影 V5 第三节（现行限制：客机无同步，见已知限制） | V4.5/V5 |
| 希腊火 8 秒持续/15 秒冷却、作用骑士及当次随从 | PatchRoles_GreekFire.cs:12-13（Duration=8f, Cooldown=15f） | V5 第二节 |
| 北境骑士钱包容量 ×2/纳税阈值 ×2 | provenance v1 行（PatchRoles_MedievalNorsePowers.cs:15-17）+ V4.5 第一节"留币上限" | V4.5 |
| 骑士五风格随机各 1/5、随从换装、读档不换脸、老档自动补 | CAP:17；PatchRoles_KnightStyle.cs 存在 | V3/V3.5 |
| 北境随从收编保持北境形态 | PatchRoles_NorseSquad.cs 头注释（v1 行）+ V3.5 第一节 | V3.5 |
| Cerberus 四队编制（2 希腊推进驻守+2 北境 30s）、CD 22.5s | PatchDivine_GhostSquads.cs 头注释（+1 希腊+2 北境、Norse 30s、22.5s） | V2 第二节 |
| 赫尔墨斯法杖转化上限 32、扫描 ×2、CD 默认 11.25s | PatchDivine_HermesStaff.cs:33-36（32、×2）+ GUIDE:20（37.5%） | V3 第五节/V4.5 |
| 特种箭塔重建：六级塔约 18 币、弩塔工匠在岗可、火塔需燃料满无工匠、弩箭回收、在线不开放 | PatchWorld_SpecialTowerRebuild.cs 头注释（Ballista idle / FireTower fuel-full+no-worker 条件）；"约 18 金币"仅 V2 有、未在源码核到具体金额→正文写"约 18 金币"沿用 V2 表述（历史价格类，标注待 operator 裁决） | V2 第三节 |
| 舰队配对：希腊小队×可用船取小、最多 4 艘、错开停靠；死亡换君主恢复小船 | PatchWorld_FleetBoatFormation.cs + PatchWorld_FleetBoatRecovery.cs 存在；V5 第一节配队规则 | V2/V5 |
| 大蛇 100 步+夜里照常吐怪+boss 原味 | PatchWorld_SerpentLeash.cs:35（100f）+头注释吐怪修复 | V3/V3.5 |
| 友好巨魔：速度 1.5/索敌 ×2/不追悬空怪、约 5% 反制 | PatchDivine_FriendlyTroll.cs:19-20（MovementMultiplier=1.5f、TargetRangeMultiplier=2f）、:11/:818（确定性 5% TrollWeak 反制）——已源码核验，无待裁决 | V2/V3 |
| 钱袋 UI 放大右移 | PatchEconomy_CurrencyBag.cs:35-37（×2.0、+3.70/-1.50） | V2 第六节 |
| 隐士防绑架（无伤害免疫） | PatchRoles_Hermit.cs 存在；V7.5 第一节实现调整 | V2/V7.5 |
| 弩箭塔弹速 ×1.25 不叠加 | PatchWorld_BallistaBolt.cs（v1 行）+ V4.0 第二节 | V4.0 |
| 夜间列队墙后约 7 步/白天踱步位 1.2 步/≤7 骑士原版/挤出 3 秒走回/弩手墙内 4–8 步/塔位豁免 | PatchWorld_DefenseSpacing.cs 存在；数值 7/1.2/4–8 来自 V2.1/V3/V3.1/V4.5 历史说明，机制文件现行存在 → 机制句收录、具体步数保留历史值（行为类历史值，任务书允许"确认机制仍在"后正面书写） | V2.1/V3/V3.1/V4.5 |
| 友军碰撞拥挤处理（天亮不叠人山） | PatchWorld_DefenseSpacing/PatchWorld_Mover 存在 + V3.5 第四节 | V3.1/V3.5 |
| 幕府随从不跟出墙、出征阵位不漂移 | SquadFollowGuard.cs 存在 + V6 第三节 | V6 |
| 乞丐滑块 1–120/1–20、仅影响后续补员 | GUIDE:18 | V4.5 第三节 |
| 跨存档金库账本仍需备份提醒 | CAP:36 身份边界；V2 注意事项"跨存档银行余额未隔离"为旧全局银行时代表述→现行 GreekBankScope 后改写为"长期一致性仍在验证"（保守措辞） | V2 注意事项 |

## 修复节条目的源码核验点（任务书要求逐条确认机制仍在）

| 修复节条目 | 核验 |
|---|---|
| 大蛇离墙+夜间吐怪 | PatchWorld_SerpentLeash.cs 现行存在，MinDistanceFromWall=100f |
| 天亮不叠人山/友军碰撞/挤出走回 | PatchWorld_DefenseSpacing.cs、PatchWorld_Mover.cs 现行存在 |
| 读档守家偏移 | SquadFollowGuard.cs（读档顺序/偏移归还）现行存在 |
| 塔基不重叠/只清理重叠空塔基 | PatchWorld_TowerSpots.cs 现行存在 |
| 特种塔旁旧塔清理 | SpecialTowerDuplicateCleanup.cs 现行存在 |
| 高人口卡顿（工具找人/帐篷上限/扫描归一/错峰） | PatchPerformance_ToolAssignment/Population.cs、PatchShared_ScanCache.cs、PatchRoles_BeggarCamp.cs 现行存在 |
| 航行闪退/小船叠位/幽灵技能报错/日志暴涨 | PatchPoolFix.cs、PatchWorld_FleetBoatFormation.cs、PatchPerformance_NightVolley.cs 等现行存在；此类为历史修复，机制文件均在 |
| 隐士钩子调整/弩手商店销毁生命周期 | PatchRoles_Hermit.cs、CrossbowmanLifecycle.cs、ShopCleanupQueue.cs 现行存在 |
| 剑风排序与对象回收报错 | PatchRoles_MedievalNorsePowers.cs + PatchArcher 系列现行存在 |
| 面板重做/HUD 字体 | ModPanel.cs、CalendarHud.cs、PopulationHud.cs 现行存在 |
| 随从皮肤打架/跨世界动画混用 | PatchRoles_KnightStyle.cs、PatchRoles_Character.cs 现行存在 |
| 近两版（资源缓存/伤害流程/白天补货）→按"改进了"措辞 | PatchEconomy_Shops.cs、CombatDamage.cs、PatchEconomy_AutoRestock.cs 现行存在 |

## 待 operator 裁决（写入 worker-result-v2.md）
1. 特种塔重建"约 18 金币"：源码不硬编码价格（复制原生六级 PayableUpgrade 展示与档案，见 PatchWorld_SpecialTowerRebuild.cs:434-437 注释"不硬编码观测到的资源价"）；正文沿用 V2 的"约 18 金币（六级塔费用）"近似表述并带"约"字。
2. 银行"跨存档余额不隔离"旧警告：现行希腊隔离后改写为保守的"长期一致性仍在验证"（三文档现行口径一致）。
