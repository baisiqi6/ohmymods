# Mac累计候选审阅与验证分层

> **Agent provenance:** `Mac Max / Codex` · role=`Maintainer Operator` · acting_for=`ohmymods Mac Operator`

本批恢复希腊公共银行的存档/战役归属和英雄购买/保存一致性，并新增承载16种跨世界坐骑的探索岛及完整卷轴资源地图。最新修订针对玩家反馈中的齐行、贴岸与岛内空位：取消三行编队，16种坐骑在岛顶面内自然错落，世界地图与单岛页采用同一可放置边界。位置保持确定，重复打开同一状态不重新洗牌。

本轮冻结共享基底为 `release/v9.5.13@14e6f64adc4d9b182af0ef4ce9c3e0b510b30789`，已承接PR#102/#99、#103/#100、#106/#104、#107/#99及#109/#108。本PR仅追加尚未发布的累计接收输入与地图修订。默认 `master` 属于历史线；本PR保持Draft，合并、安装和正式发布分别验收。

## 逐项状态

| 范围 | Issue / Coordinate记录 | 源码与验证 | 安装与实机边界 |
|---|---|---|---|
| 接收的盾卫候选 | #51 / issue-51 | 职业、盾具店、持久化、耐久4及互斥接线已接收；完整ARM组合构建通过 | 正常组合已包含；领盾、退职/耐久、读档与联机专项待验 |
| 银行公共余额 | #97 / issue-97 | 同存档/战役共享、跨战役隔离及原生保存边界；state28、capture5、recovery5原回执保留 | 正常组合已有安装；玩家跨岛提款/战役隔离完整现场待验 |
| 英雄购买/责任 | #85 / issue-85 | 不上船、旧付款责任与新购买/异步保存门协同；core47、lands24、carry22及组合22原回执保留 | 正常组合已有安装；实际购买/保存完整闭环待验 |
| 跨世界坐骑、扩岛与地图 | #98 / issue-98 | physical11/UI10与16种原生坐骑接线；自然海岸、切页就绪显示及礁石布局保留，本轮追加岛顶面错落放位 | 最新精确地图候选未安装；最终Unity像素/点击、land11往返/技能/重载待验 |
| 缩放稳定性与农舍猫接收修订 | #29及既有专项记录 | 继承候选与专项测试收录；旧缩放/弩手测试迁至native-scale-ownership，新PR#107测试保留native-scale-timing | 原owner保持；源码接收不视为新实机完成 |
| 实玩Bug、固定墙基与传送效果 | #99 / #100 / #104 | PR#102/#103/#106/#107/#109（含#108弩手后排）均已合并，作为共享base保留 | 维护会话此前安装正常累计d08f4009；本次未启动游戏或安装地图候选 |
| 原创宫廷 | #101 / issue-101 | 独立候选，未纳入本PR；专用入口校验/环境修订及真实MOD加载、隔离Filer回读、来源原生保存已有专项证据 | 正常ARM未接入；入口携带检查因mount-extra Hold暂停试玩，完整宫廷访问/存读档/返程未完成，已付责任保留 |

## 当前精确候选验证

当前结果绑定最终错落修订输入；旧自然海岸候选的结果只在原输入范围保留。

| 验证层 | 当前结果 |
|---|---|
| 实际ARM构建 | 161个真实Mac ARM参考程序集，Standalone .NET6 Release：0 warnings / 0 errors；产品源码提交 `cd4a66ecb796172687b4d0aeaaa1cf57a3d14816`，审阅DLL SHA256 `28865425746ee148554d151f6dbd48287b14951390547e39b7351c96b1c01f9a`。包装器Version10.8.38，仓库Version10.8.35保持，本PR不作正式版本发布 |
| 地图纯函数与岛面 | 公开源链接布局2983/0、岸线/租约101/0、调用层182/0；新岸线仍228×84、等比2.7143。完整16项在world300×200、400×260及detail均为scale=.60，整矩形落在实际可放置面内，避让原尺寸船标与其他图标 |
| 错落与边界独立复核 | 额外72/0：实际PNG得到6845像素可放置面，完整alpha为11053；1像素洞/外溢及.002px越界拒绝，1e-3px边界对齐容差单独说明。重复输入、平移、逆序身份、太小框/巨障碍/19项溢出/空集诚实退让通过 |
| 组合范围审计 | 相对此前已审自然海岸DLL fa11f7b2：6663方法IL/locals/EH保持，27授权方法改变、12新增；34张PNG逐字节保持，334 Harmony目标与460 handler元数据保持。MapWorldLayout仅IconAreaOf的精确扩岛辅助签名例外，原十岛路径不调用 |
| 源级视觉 | 三个视图共48/48完整图标框位于实际岛顶面、无互叠/船标重叠；世界右半8/16、详情9/16。自然错落与右侧利用独立目测通过；剪影预览由生产坐标/实际mask生成，属于模板渲染，非Unity或当前玩家存档截图 |
| 公共包与安装边界 | 17个公开输入无私有SVG/native bitmap/玩家文件/host路径依赖；可选--asset须与实际嵌入PNG匹配，缺失/失配失败且不写facts。本轮精确DLL未安装，目标玩法与联机仍待验 |

本轮仅修改三个地图生产文件。原岸线/选中描边继续使用完整alpha；新增的PlacementMask使用保守岛顶面与alpha交集，并内缩2个prepared像素，剔除崖面和岸缘。布局采用固定normalized锚点与有界确定性修正，不在运行期随机生成。取消扩岛的整条空状态车道，继续按真实障碍避让；全图与详情实际调用都明确保持.60下限，失败交付整组空结果。原有10岛仍保留同一整体缩放/平移，既有owner、四Image屏障、失败恢复和Sprite租约保持。

此前自然海岸/切页显示/礁石修订的独立核心165/0及额外26条失败/真实船标几何探针仍按原候选留档。本轮保留原S2–S10/S12生命周期测试体，复跑182/0。私有详情mask图的原SVG展示视口曾裁切；独立检查副本仅扩大展示视口，全部业务坐标、bitmap及其余XML树保持不变，仅调整展示视口并移除工程标签，已完整检查该叠图，未修改业务源码。

继承专项回执继续有效于其原输入范围：fixed-foundations30、coin-courier-economy55、greek-bank-scope35、greek-bank-assistants-scope73、英雄/银行组合22；哥布林bridge203、visuals23、height19、lifecycle11。共享base已有新平民高度19、旧缩放ownership109（40场景）、弩手ownership117（17场景）及#109弩手unit12/integration9、火枪e2e38的原回执，两套缩放测试分别保留。只有本轮明确复跑项目列为当前候选结果。

## 可复跑回归

```sh
dotnet run --project tests/native-map-icons/MapIconPlanTests.csproj -c Release
dotnet run --project tests/native-map-shore-art/ShoreArtTests.csproj -c Release
dotnet run --project tests/native-map-runtime/RuntimeProbe.csproj -c Release
```

原生参考程序集来自维护者已验证的2.4/BepInEx环境；Mac ARM64、Mac x64、Windows及联机验收分别记录。本轮独立源码与源级视觉审查通过；GitHub CI/正式PR review以远端当前head为准。

## 协作与范围

共同PR在Coordinate正式绑定issue-29；功能task保留原owner/branch，通过related_prs与本文引用共同PR。#99/#100/#104的独立PR与合并事实分别保留；#101仍独立且完整访问待验。公共AGENTS及全局checklist/events/progress历史没有复制到本PR，canonical checklist通过Coordinate受控接口维护。

阶段记录明确source/tests/install/live与精确head/制品，不能用源码、模板预览或静态入口检查替代真实游戏验收。

## 改动文件索引

接收来源按输入SHA或原始接收树对齐判定；“后续/混合”表示可能含继承接缝，不能仅依据整文件所属commit认定所有行来自同一实现者。

| 文件 | 审阅分组 | 输入出处 |
|---|---|---|
| `il2cpp/Assets/HeavyShieldGreekShopLayers.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldGreekShopLayersCoarse.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldPaidShield.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldPaidShieldCoarse.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldShopAtlas.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldShopAtlasCoarse.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldShopLayers.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldShopLayersCoarse.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldSoldierAtlas.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Assets/HeavyShieldSoldierAtlasCoarse.png` | 接收支持文件 | Windows接收输入 |
| `il2cpp/CombatTargetLife.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/FarmCatMovement.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/GreekScaleScope.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldActorVisuals.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldBlockPolicy.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldCareerExclusion.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldCombat.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldContracts.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldForgeArchive.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldIdentity.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldIntegration.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldNativeHooks.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldPersistence.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldPromotionBridge.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldQuota.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldRuntime.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldSaveData.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldShopPayment.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeavyShieldShopShell.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/HeroArcherTowerPolicy.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/KingdomEnhancedPlugin.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/MusketeerHooks.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/MusketeerIdentity.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PatchRoles_Crossbowman.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PatchRoles_HeavyShieldArt.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PatchRoles_KnightStyle.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PatchWorld_DefenseSpacing.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PatchWorld_FarmCats.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/Patch_MusketeerFormation.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PopulationCounts.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/PopulationHud.cs` | 接收支持文件 | Windows接收输入 |
| `il2cpp/SquadFollowGuard.cs` | 接收支持文件 | Windows接收输入 |
| `tests/coin-courier-persistence/CoinCourierPersistenceTests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/coin-courier-persistence/actual-interop/ActualInterop.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/coin-courier-runtime-bridge/CoinCourierRuntimeBridgeTests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/crossbowman-wallpierce/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/expedition-follow/Regression.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/farm-cat-movement/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/farm-cat-movement/README.md` | 接收支持文件 | Windows接收输入 |
| `tests/farm-cat-movement/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/farm-cat-movement/Tests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/farm-cats/Regression.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/farm-cats/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/greek-scale-adapters/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/greek-scale-scope/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/HeavyShieldArtTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/README.md` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/evidence/atlas-evidence.json` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/evidence/shop-layer-composite.png` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/test_loader.py` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-art/verify_atlases.py` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-boundary/DisabledHeavyShieldApis.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-forge-archive/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-forge-archive/Tests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-identity/DurabilityCompatibility.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-identity/HeavyShieldIdentityTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-identity/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-identity/R2Regression.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-identity/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-integration/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-integration/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-integration/Tests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-policy/HeavyShieldPolicyTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-policy/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-promotion/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-promotion/PromotionLedgerTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-quota/HeavyShieldQuotaTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-quota/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-runtime/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-runtime/RuntimeTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-runtime/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-runtime/audit-native-demote.ps1` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-runtime/native-demote-calls.txt` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-save/DurabilityCompatibility.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-save/HeavyShieldSaveTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-save/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/ArtRegression.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/README.md` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/Tests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/evidence/atlas-evidence.json` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/evidence/shop-layer-composite.png` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-runtime/run_atlas_regression.py` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-shop-shell/test_contract.py` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-visuals/HeavyShieldVisualsTests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/heavy-shield-visuals/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/hero-archer/combat/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/hero-archer/greek/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/hero-archer/runtime/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/hero-tower-policy/Interop.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/hero-tower-policy/Program.cs` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/hero-tower-policy/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-defense/Integration.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-formation/MusketeerFormationTests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-formation/PolicyTests.cs` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-formation/e2e/FormationPipelineTests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-formation/interop-check/MusketeerFormationInteropCheck.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-identity/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/musketeer-restock/Tests.csproj` | 接收支持文件 | 已接纳后续/混合输入 |
| `tests/native-scale-ownership/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/README.md` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/Tests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/crossbow-ownership/LifecycleStubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/crossbow-ownership/Ownership.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/crossbow-ownership/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-ownership/crossbow-ownership/README.md` | 接收支持文件 | Windows接收输入 |
| `tests/population-hud/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/population-hud/Regression.csproj` | 接收支持文件 | Windows接收输入 |
| `il2cpp/CoinCourierPersistence.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/HeroArcherRuntime.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/HeroBoardingPolicy.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/HeroNativeRights.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/HeroRecruitment.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/HeroShop.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/MusketeerPersistence.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/PatchEconomy_Banker.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/PatchRoles_Worker.cs` | 银行/英雄及继承接缝 | Windows接收输入 |
| `il2cpp/SharedBankNative.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/SharedBankState.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-economy/CoinCourierEconomyTests.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-economy/Harness.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-economy/NativeCases.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-economy/NativeStubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-economy/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-economy/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-persistence/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-persistence/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/coin-courier-runtime-bridge/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-assistants-scope/Harness.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-assistants-scope/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-assistants-scope/Regression.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-assistants-scope/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-scope/GreekBankScopeRegression.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-scope/Harness.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-scope/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/greek-bank-scope/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Proof.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Stubs/Game.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Stubs/Host.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Stubs/Interop.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Stubs/Slices.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/Stubs/Unity.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/Harness.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/HeroStubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/NativeStubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/NuGet.Config` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/Proof.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/bank-combo/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/Adjacent.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/Astra.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/NuGet.Config` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/Proof.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/R1.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/R2.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/R3.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/hero-purchase-consistency/omp-r1/Recovery.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/musketeer-defense/IntegrationStubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/musketeer-identity/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/musketeer-identity/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/README.md` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/capture/Capture.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/capture/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/hotpath/HotPath.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/hotpath/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/hotpath/Stubs.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/recovery/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-regressions/recovery/Recovery.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-state/Program.cs` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `tests/shared-bank-state/StateFixture.csproj` | 银行/英雄及继承接缝 | 已接纳后续/混合输入 |
| `il2cpp/Assets/KEM_MapRainbowPony.png` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/Assets/KEM_MapSantaReindeer.png` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/Assets/KEM_MapSpookyhorse.png` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/CrossWorldMountData.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/CrossWorldMountDependencies.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/CrossWorldMountRuntime.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/ExtensionIslandMap.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/ExtensionIslandMapPlan.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/ExtensionIslandPlan.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/ExtensionIslandRuntime.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/KingdomEnhancedMod.csproj` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/MapCustomIconAssets.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/MapCustomIconPlan.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/MapIconSources.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/MapMountIcons.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/MapResourceIconPlan.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/ModConfig.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/ModPanel.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/PatchExtensionIsland_Map.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/PatchExtensionIsland_Progression.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/PatchExtensionIsland_Runtime.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `il2cpp/PatchRide_CrossWorldMount.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-adapter/Adapter.csproj` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-adapter/NuGet.Config` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-adapter/Program.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-adapter/Stubs.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-repro/NuGet.Config` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-repro/Program.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-repro/Repro.csproj` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts-repro/Stubs.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts/CrossWorldMountTests.csproj` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/cross-world-mounts/Program.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/native-map-icons/MapIconPlanTests.csproj` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/native-map-icons/Program.cs` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/native-map-icons/fixtures/custom-icons.json` | 坐骑/扩岛/地图接线 | 已接纳后续/混合输入 |
| `tests/native-map-icons/fixtures/native-map-analysis.json` | 地图便携夹具 | 已验原生坐标/尺寸事实的最小子集 |
| `tests/native-map-icons/fixtures/layout-manifest.json` | 地图便携夹具 | 已验原生坐标/尺寸事实的最小子集 |
| `tests/native-map-icons/fixtures/icon-rects.json` | 地图便携夹具 | 已验原生坐标/尺寸事实的最小子集 |

## 本轮地图修订与公开测试包

| 文件或范围 | 内容 |
|---|---|
| `il2cpp/Assets/KEM_MapExtensionIsland.png` | 此前生成的自有岸线源素材，本轮字节保持；运行时准备像素轮廓与选中边 |
| `il2cpp/MapExtensionIslandArt.cs` | 岛顶面PlacementMask与原岸线/描边分离，原borrowed Image租约和释放保持 |
| `il2cpp/MapMountIcons.cs` | 全图/详情使用实际PlacementMask、固定错落布局与.60下限，保留显示提交和礁石布局 |
| `il2cpp/MapResourceIconPlan.cs` | 有界确定性错落放位、完整矩形面内验证及扩岛IconAreaOf辅助方法 |
| `il2cpp/ExtensionIslandMap.cs` | 此前已审的view/owner地形节点接缝，本轮保持 |
| `tests/native-map-runtime/` | 5个公开输入，调用层、PNG解析和182项直源回归 |
| `tests/native-map-shore-art/` | 6个公开输入，岸线/租约、方向、实际mask导出与布局报告101项检查 |
| `tests/native-map-icons/` | 6个便携输入，4份精简数值fixture和2983项检查 |

原生船标模板32×32、scale1、中心(-48,18)及只更换sprite的动画已只读核对并沿用；真实活动/选中状态仍按原生运行。公开包不包含私有SVG工具、原生位图或玩家截图。维护会话此前安装的正常ARM d08为历史基线，本轮精确地图DLL未安装、未启动游戏、未写玩家档或正式发布。
