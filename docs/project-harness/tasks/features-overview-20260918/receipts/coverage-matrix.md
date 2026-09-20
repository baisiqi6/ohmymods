# coverage-matrix：V* 版本说明 × il2cpp 文件清单 → MOD_FEATURES_OVERVIEW_ZH.txt 对账

规则：每份 MOD_V* 说明的每个章节要么映射到 overview 条目（§节名），要么进排除清单并给理由。
il2cpp/ 143 个 .cs 文件同理过一遍（按功能簇归组）。overview = 2026-09-18 v2 重写版。

缩写：速览=§一；图鉴=§二；世界=§三；战斗=§四；便捷HUD=§五；修复=§六；限制=§七；安装=§八。

## 一、15 份 MOD_V* 版本说明逐章节对账

### MOD_V2（129 行）
| V2 章节 | 映射 |
|---|---|
| 一、银行助手系统（主银行家墙内/助手墙外 3 秒/一路吸收/8 枚增派四人/传送/单次入账/提款 39→100） | 图鉴·税收助手与银行家（逐点收录，数值经 PatchEconomy_Banker.cs:44、BankAssistants 头注释核验） |
| 二、Cerberus 四队（2 希腊驻守不自爆+2 北境 30s、CD 22.5s） | 世界·Cerberus（PatchDivine_GhostSquads.cs 头注释核验） |
| 三、特种箭塔重建（弩塔/火塔条件、~18 币、弩箭回收、在线不开放） | 世界·特种箭塔重建（条件经 PatchWorld_SpecialTowerRebuild.cs 头注释核验；价格"约18"沿用历史观测，见 provenance 待裁决1） |
| 四、小船系统（城墙旗帜编队 4 艘错开/死亡换君主恢复） | 世界·希腊舰队小船 |
| 五、转正功能（忍者伏击/狂战士第6队长/隐士防绑架/主船容量/友好巨魔平衡） | 图鉴·忍者与狂战士、图鉴·狂战士缰绳、世界·主船容量、世界·友好巨魔、战斗·隐士防绑架 |
| 六、重要修复清单（航行闪退/日志暴涨/高人口卡顿/叠船/幽灵技能/钱包UI/体型微调） | 修复节（逐条映射；"体型微调"→世界·希腊缩放条目概括，不入修复节流水） |
| 注意事项（联机同版本/重建在线不可用/余额不隔离/游戏更新失效/卸载） | 限制节 + 安装节（"余额不隔离"改写为现行口径"长期一致性仍在验证"，见 provenance 待裁决2） |
| 升级方法/反馈 | 安装节（最小步骤化，不逐条复制） |

### MOD_V2.1（40 行）
| 章节 | 映射 |
|---|---|
| 守家站位紧凑化（墙后约 7 步/战死重排/≤7 名原版/白天不受影响） | 战斗·守家与阵型 |
| 升级方法 | 安装节 |

### MOD_V3（136 行）
| 章节 | 映射 |
|---|---|
| 一、弩手（3:1、伤害2/完美×2、射程12、装填×2、造型、不入骑士队、读档校准） | 图鉴·弩手（PatchRoles_Crossbowman.cs 逐项核验） |
| 二、骑士随机四风格（读档不换脸/联机一致/随从换装/死地随从拿弩彩蛋） | 图鉴·骑士五风格（见排除：死地随从弩） |
| 三、白天踱步位 1.2 步/夜晚紧凑/挤出 3 秒走回 | 战斗·守家与阵型 |
| 四、大蛇搬家 60 步 | 排除：历史中间值，现值为 100 步（V3.1 定稿，SerpentLeash.cs:35）；overview 写 100 |
| 五、平衡与修复（反制 10%→5%、法杖 22.5→11.25、读档守家错乱、皮肤翻牌、复用清污） | 世界·友好巨魔（5%）、世界·法杖 CD、修复节 |
| 六/七/八、升级/注意事项/反馈 | 安装节、限制节 |

### MOD_V3.1（86 行）
| 章节 | 映射 |
|---|---|
| 一、CD 滑块（法杖默认 37.5%/坐骑默认 100%、20–100%） | 便捷HUD·默认开关速览 |
| 二、面板变大 | 修复节（面板重做句） |
| 三、守家布阵（弩手后挪 4–8 步/死地小队 6.5 步/天亮友军碰撞推迟 8 点/挤出走回） | 图鉴·弩手守位、战斗·守家与阵型（死地小队 6.5 步细节合并入"窄地形收缩/中后段"概括） |
| 四、数值微调（幕府 0.95/狗 1.3/大蛇 100） | 世界·大蛇缰绳 100 步；幕府/狗体型→排除：并入"希腊自定义缩放"现行口径（GUIDE:57），不单列历史数值 |
| 五、塔位豁免/复用清污 | 战斗·塔位豁免；修复节 |
| 六、升级 | 安装节 |

### MOD_V3.5（102 行）
| 章节 | 映射 |
|---|---|
| 一、北境骑士小队（五风格 1/5、真北境持盾、1.15、收编保持、老档重掷） | 图鉴·骑士五风格北境段（1.15 体型并入弩手/北境条目不重复列） |
| 二、箭塔基底增密 2 倍（1–4） | 世界·世界选项 |
| 三、农舍猫回归（3 只 1.2） | 排除：历史中间值；现值 4 只 1.25（GUIDE:58），overview 写现值 |
| 四、友军碰撞永久取消（取舍说明） | 战斗·守家与阵型 |
| 五、大蛇夜里照常吐怪 | 世界·大蛇缰绳 |
| 六、助手容量大增/扫描缓存/滑块步进 | 图鉴·助手段（容量→现行为每趟 20 目标口径）；修复节（扫描归一） |
| 七、升级 | 安装节 |

### MOD_V4.0（85 行）
| 章节 | 映射 |
|---|---|
| 一、希腊用北境守家雕像（盾墙队长限北境/读档恢复） | 世界·盾墙图腾（+V2 源流） |
| 二、弩炮塔弹速 +25% | 世界·弩箭塔弹速（战斗节写"×1.25"，同一机制） |
| 三、塔基不与建筑重叠/只清重叠空塔基 | 修复节、世界·世界选项 |
| 四、友好巨魔飞行怪/反制消耗/记录清理/农民工具回退/时钟诊断 | 世界·友好巨魔；诊断类→排除（内部查错，非玩家可感知） |
| 五、农田币 12→3 秒/助手收农田币/面板余额/猫 3→6 | 图鉴·助手段（农田币 3 秒，源码 FARM_COIN_MATURITY_SECONDS=3f 核验）；面板余额→便捷HUD·时间与银行；猫 6→排除（现值 4） |
| 六、升级 | 安装节 |

### MOD_V4.5（53 行）
| 章节 | 映射 |
|---|---|
| 一、五风格战斗差异（中世纪 1.5 剑风/死地 2×·1.5×/北境钱包/幕府冲刺 3s·7 格无敌） | 图鉴·骑士五风格（逐项收录） |
| 二、弩手守家（墙内 4–7 步/塔内射程 +50%） | 图鉴·弩手守位、弩手数值（塔位 ×1.5，源码核验） |
| 三、法杖 32 上限/扫描 2 倍、乞丐滑块、面板重做 | 世界·法杖、便捷HUD·默认速览、修复节 |
| 四、常驻时间与季节 HUD | 便捷HUD·时间与银行 |
| 五、稳定性（启动闪退/冲刺清理/扫描错峰） | 修复节 |
| 安装段（完整包含载、不多套一层） | 安装节 |

### MOD_V5.0（44 行）
| 章节 | 映射 |
|---|---|
| 一、出征跟随修复/希腊舰队配队 | 战斗·幕府随从跟队与出征阵位（V6 收敛版）、世界·希腊舰队小船 |
| 二、希腊火 8s/15s/随从火箭资源补齐 | 图鉴·骑士希腊段（源码核验） |
| 三、幕府返队伤害/残影/退速 3× | 图鉴·幕府段（残影客机限制→限制节） |
| 四、时间面板加银行/乞丐 120·4 新默认/猫 6→4/移除逐箭诊断 | 便捷HUD、默认速览、世界·农舍猫（现值 4）；诊断→排除 |
| 验证段/安装段 | 限制节兜底句、安装节 |

### MOD_V6.0（47 行）
| 章节 | 映射 |
|---|---|
| 一、自动补货页（目标/2 倍价/两单/忍者 4 狂战士 6/面包/读档银行家修复） | 世界·经济自动化、图鉴·忍者狂战士价格 |
| 二、HUD 字体/重复塔清理 | 修复节 |
| 三、战斗（随从不跟出墙/出征阵位/狂战士归位/白天不追撤退敌人/火兜底/猫 1.25） | 战斗·守家与阵型、图鉴·狂战士、世界·农舍猫 |
| 范围说明（尚未只影响希腊） | 限制节末条 |
| 安装段 | 安装节 |

### MOD_V6.1.5（31 行）
| 章节 | 映射 |
|---|---|
| 一、人口 HUD（九职业+五风格+待识别/每秒/单机主机/客机提示） | 便捷HUD·人口 HUD |
| 二、剑风 ObjectCollected 修复/狮鹫不宣称 | 修复节、限制节 |
| 三、保留清单/联机客机 | 概括入对应功能条目 |

### MOD_V7.5（61 行）
| 章节 | 映射 |
|---|---|
| 一、隐士稳定性 | 修复节（"实现已调整，移除原生钩子"句） |
| 二、Critter 原版大小 | 世界·世界选项（"保持原版大小"） |
| 三、鹿 3 倍 | 世界·世界选项 |
| 四、范围统一（银行/缩放仅希腊） | 图鉴/世界各条目范围标签；"人数/日历仍跨世界"→限制节末条 |
| 五、便捷三项 | 便捷HUD·便捷页 |
| 六、补货新增三项+弹药计数 | 世界·经济自动化（9 项）、便捷HUD·人口 HUD（两类弹药） |
| 七、弓箭三项（散射 1–5→后收敛/射速/特效） | 战斗·弓箭页（散射现值 1–3，见排除注） |
| 八、头饰 30% | 图鉴/战斗·头饰（现行为累计配额制，V8 改） |
| 九、弩手商店销毁生命周期 | 修复节 |
| 验证段 | 限制节兜底 |

注：V7.5 散射"1–5 支"为当时全弓手散射的旧形态；现行（V8 起）为中世纪随从散射 1–3，overview 按现行写。

### MOD_V7.6.5（26 行）
| 章节 | 映射 |
|---|---|
| 一、像素火焰特效 | 战斗·弓箭命中火焰特效 |
| 二、坐骑无限体力 | 便捷HUD·便捷页 |
| 安装/验证段 | 安装节、限制节 |

### MOD_V8.0（52 行）
| 章节 | 映射 |
|---|---|
| 一、英雄（当时射程 1.25/开关自动选） | 排除：历史形态；现行为驿站 8 币/射程 2 倍（V9 起），overview 按现行 |
| 二、火矢爆发（半径细节/1.33 倍受击宽） | 战斗·弓箭页（技术细节句"半径 0.25/邻近 1 点"收录；胶囊宽度推导→排除，过于技术化不适合发群说明） |
| 三、中世纪随从散射 | 战斗·弓箭页 |
| 四、骑士身份附加档 | 图鉴·骑士五风格身份段、限制节 |
| 五、头饰配额制（第 4/7/10、44 款、保护） | 战斗·法杖头饰 |
| 六、稳定性与保留 | 修复节（火塔满仓等待/退出清理/诊断类排除） |
| 安装备份段（三处备份） | 安装节 |

### MOD_V9.0（58 行）
| 章节 | 映射 |
|---|---|
| 一、英雄驿站 8 币 | 图鉴·英雄 |
| 二、火铳铺与火枪手 | 图鉴·火枪手 |
| 三、火枪人数与第 9 项补货 | 便捷HUD·人口 HUD、世界·经济自动化 |
| 四、助手 20 枚/4.2 秒、骑士身份批次、乞丐诊断 | 图鉴·助手段、图鉴·身份段；诊断→排除 |
| 五、弓箭与便捷保留 | 各功能条目 |
| 六、存档与已知限制 | 限制节 |
| 安装段 | 安装节 |

### MOD_V9.4.5（59 行）
| 章节 | 映射 |
|---|---|
| 一、独立侧枪架 | 图鉴·火枪手获取 |
| 二、外观 0.9 与五帧红焰 | 图鉴·火枪手特色 |
| 三、长按扩展弹药 | 便捷HUD·长按连续购买 |
| 四、白天补货 | 世界·经济自动化 |
| 五、伤害可靠性 | 战斗·其他战斗项（"改进/更稳"措辞，不写彻底解决）；修复节末组 |
| 六、举旗火枪后排 | 图鉴·火枪手特色 |
| 七、白天猎鹿 | 图鉴·火枪手特色 |
| 安装/验证段 | 安装节、限制节 |

## 二、il2cpp/ 文件清单对账（143 个 .cs，按功能簇）

| 文件簇 | 映射/排除 |
|---|---|
| HeroArcher*（24 个：Animation/ArrowVisuals/Cloth/Combat/GuardFacing/Motion/Movement/NativePose/Range/Runtime/TowerPolicy/Visuals/Shop/Banner/Recruitment*/VisualPriority/WallPierce/Network/LiveDiagnostics/ClothPhysics/ScarfGeometry） | 图鉴·英雄（WallPierce=未发布候选→零出现；Network/LiveDiagnostics→排除：内部/联机未开放） |
| Musketeer*（19 个） | 图鉴·火枪手（Diagnostics 类→排除） |
| PatchRoles_Knight/KnightStyle/KnightAnimatorDiag/SquadRefillDiag | 图鉴·五风格；*Diag→排除（内部诊断） |
| PatchRoles_Crossbowman + CrossbowDefense + CrossbowmanLifecycle | 图鉴·弩手 |
| PatchRoles_Ninja / Berserker | 图鉴·忍者与狂战士 |
| PatchRoles_MedievalNorsePowers / DeadlandsPowers / NorseSquad / GreekFire / GreekFireAssets / SamuraiPowerDash + SamuraiDashVisuals/RetreatSpeed/Diagnostics | 图鉴·五风格各段（Diagnostics→排除） |
| PatchEconomy_Banker / BankAssistants / GreekBankScope | 图鉴·税收助手与银行家 |
| PatchEconomy_AutoRestock / Shops / CurrencyBag | 世界·经济自动化、钱袋 |
| MusketeerRestock / FireTowerRestockCapacity / SiegeAmmoCounts / AutoRestockCounts | 世界·经济自动化、人口 HUD 弹药 |
| PatchWorld_FleetBoatFormation / BoatCapacity / FleetBoatRecovery | 世界·船只 |
| PatchWorld_SpecialTowerRebuild / SpecialTowerDuplicateCleanup | 世界·特种塔重建、修复节 |
| PatchWorld_TowerSpots | 世界·世界选项、修复节 |
| PatchWorld_SerpentLeash / ShieldWallTotem / DeerPopulation / FarmCats / OptionalVegetation | 世界·大蛇/盾墙/鹿/猫/便捷灌木 |
| PatchWorld_BallistaBolt | 战斗·弩箭塔弹速 |
| PatchWorld_DefenseSpacing / Mover / Kingdom / Level / EnemyManager | 战斗·守家与阵型、世界选项（怪物数量/时间线） |
| PatchDivine_GhostSquads / HermesStaff / FriendlyTroll / Artemis / HermesHeadwear*（5 个） | 世界·Cerberus/法杖/友好巨魔、战斗·头饰（Codec/Diagnostics→排除） |
| PatchDivine_GhostLeashHold | 世界·Cerberus 驻守行为（随队编制条目） |
| PatchArcher_Options / Impact / GreekImpact + ArcherOptionsScope / MedievalScatterPolicy / ScatterArrowTint | 战斗·弓箭页 |
| CombatDamage / CombatTargetLife | 战斗·伤害流程句 |
| PatchPlayer_HoldPurchase / OptionalQoLScope / PatchRide_InfiniteStamina / SteedCooldown | 便捷HUD·便捷页、默认速览 |
| PopulationHud / PopulationCounts / PopulationGrounding / CalendarHud / CalendarSnapshot | 便捷HUD（Grounding=诊断→排除） |
| PatchRoles_BeggarCamp / Hermit / Holder / Castle / Character / Worker / World | 图鉴（乞丐滑块）、战斗·隐士防绑架、跨世界工具支持（Holder→排除：纯技术支撑） |
| PatchPerformance_NightVolley / Population / ToolAssignment / PatchShared_ScanCache | 修复节·性能组 |
| PatchPoolFix / ShopCleanupQueue / SquadFollowGuard / SquadRosterSnapshot / PatchLifecycle_MenuInput / UnloadShieldAudio | 修复节（池/商店/跟随/退出清理）；RosterSnapshot→排除（内部缓存） |
| ModConfig / ModPanel / ImGuiCompat / KingdomEnhancedPlugin | 面板与配置入口（默认速览数据源；工程文件→排除） |
| KnightIdentity*（5 个） | 图鉴·身份段、限制节（Network→排除细节） |

排除理由汇总：内部诊断/日志类（*Diag、Grounding、时钟昼夜记录）、纯技术支撑（Holder 注册、池 ID、编解码、快照缓存）、未发布候选（HeroArcherWallPierce 及 65% 缩箭——正文零出现）、历史中间值（大蛇 60、猫 3/6、散射 1–5、英雄 1.25/自动选、头饰 30% 独立随机）均以现行值为准。

## 三、结论
15 份 V 说明全部章节均有映射或带理由排除；143 个源文件全部归簇处理。overview 覆盖三文档全量玩家可感知功能 + V 说明全部现行有效条目 + 文件清单发掘项（弩箭 ×1.25、钱袋、主船容量、北境钱包、Cerberus、狂战士缰绳及本轮新增忍者伏击/狂战士队长/银行家提款/幕府冲刺细节/希腊火 8s15s/特种塔重建细节/守家布阵数值/友好巨魔数值/隐士防绑架/幕府退速 3×）。
