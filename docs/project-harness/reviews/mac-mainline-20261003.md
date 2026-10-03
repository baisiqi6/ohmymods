# Mac累计候选审阅与验证分层

> **Agent provenance:** `Mac Max / Codex` · role=`Maintainer Operator` · acting_for=`ohmymods Mac Operator`

本批恢复希腊公共银行的存档/战役归属和英雄购买/保存一致性，并新增承载16种跨世界坐骑的探索岛及完整卷轴资源地图。最新地图修订针对玩家反馈：为探索岛制作自然海岸轮廓，世界地图与单岛页共用同一轮廓及等比布局，切回世界地图时在布局就绪后显示，并随新岛布局重新安放礁石。

本轮冻结共享基底为 `release/v9.5.13@14e6f64adc4d9b182af0ef4ce9c3e0b510b30789`，已承接PR#102/#99、#103/#100、#106/#104、#107/#99及#109/#108。本PR合入该base后，仅追加尚未发布的累计接收输入与本次地图修订。默认 `master` 属于历史线，不作为本批业务基底。本PR保持Draft；合并、安装和正式发布分别验收。

## 逐项状态

| 范围 | Issue / Coordinate记录 | 源码与验证 | 安装与实机边界 |
|---|---|---|---|
| 接收的盾卫候选 | #51 / issue-51 | 职业、盾具店、持久化、耐久4及互斥接线已接收；完整ARM组合构建通过 | 包含在正常ARM组合；领盾、退职/耐久、读档与联机专项待验 |
| 银行公共余额 | #97 / issue-97 | 同存档/战役共享、跨战役隔离及原生保存边界；state28、capture5、recovery5既有回执保留 | 正常组合已有安装；玩家跨岛提款/战役隔离完整现场待验 |
| 英雄购买/责任 | #85 / issue-85 | 不上船、旧付款责任与新购买/异步保存门协同；core47、lands24、carry22及组合22既有回执保留 | 正常组合已有安装；实际购买/保存完整闭环待验 |
| 跨世界坐骑、扩岛与地图 | #98 / issue-98 | physical11/UI10与16种原生坐骑接线；本轮自然海岸、全图/详情一致、切页就绪显示及礁石布局见下表 | 旧地图组合曾安装；本轮精确候选尚未安装，最终像素/点击、land11往返/技能/重载待验 |
| 缩放稳定性与农舍猫接收修订 | #29及既有专项记录 | 继承候选与专项测试收录；旧缩放/弩手测试迁至native-scale-ownership，新PR#107测试保留native-scale-timing | 原owner不变；源码接收不视为新实机完成 |
| 实玩Bug、固定墙基与传送效果 | #99 / #100 / #104 | PR#102/#103/#106/#107/#109（含#108弩手后排）均已合并，作为共享base保留 | 维护会话已安装累计d08f4009；本次没有启动游戏或安装地图候选 |
| 原创宫廷 | #101 / issue-101 | 独立候选与素材，未纳入本PR；独立测试目录已有248文件安装及250文件图表静态校验 | 正常ARM未接入；隔离入口尚未实际启动，完整访问往返待验，既有支付责任保留 |

## 当前精确候选验证

以下当前结果只对应最终提交所用源输入，不把旧head的验证替代新候选。

| 验证层 | 当前结果 |
|---|---|
| 实际ARM构建 | 161个真实Mac ARM参考程序集，Standalone .NET6 Release：0 warnings / 0 errors；产品源码提交 `e0f1283db8db5f7d792848826ec61f3ae88cf0b6`，审阅DLL SHA256 `fa11f7b2f1409ef483b1b6dadc477313bb58e44bc6146422bd4260bcb40efa82`。包装器Version10.8.38，仓库Version10.8.35保持，本PR不作正式版本发布 |
| 地图纯函数与轮廓 | 公开源链接布局2984/0、岸线/租约78/0；新轮廓228×84、等比2.7143，三行16图标逐完整矩形验证岸内与碰撞；坐标导出/Y方向自检通过，legacy反例按预期失败 |
| 地图调用层与失败恢复 | 公开源链接168/0；独立核心165/0及26条额外失败/真实船标几何探针通过。owner换代等待预算、临时释放失败、四图幂等屏障及队列满恢复责任均有复现后回归 |
| 组合范围审计 | 相对2026-10-03 13:40UTC已装正常基线d08f4009：6570方法IL/locals/EH保持，28个授权地图方法改变、107新增；33原有PNG逐字节保持，新增岸线PNG SHA256 `bd29b0fd3dd2783adab7ed450907b1578f9a48197c882eef65dd7cdf8baa81bf`；334 Harmony目标与460 handler元数据保持 |
| 共享base冲突处理 | 新平民高度19、旧缩放ownership109（40场景）及弩手ownership117（17场景）通过；两套测试独立保留；承接#109的弩手unit12、integration9及火枪e2e38场景通过；两个新project引用既有盾卫边界文件，原场景/断言与生产源保持 |
| 独立审查 | 独立核心源审PASS；公开测试包的privacy/portable输入及可选报告素材SHA绑定分别复核。图形预览为模板/坐标渲染验证，实机效果未验 |
| 安装/实机 | 本轮精确DLL尚未安装；预览来自真实素材/坐标的代码渲染，属于源级布局验证，未声称Unity游戏画面或玩法验收 |

本轮海岸源PNG来自生成工具；冷启动准备生成同一像素轮廓、透明掩码及白色选中边，随后全图/详情共用缓存。资源使用实际图标尺寸和透明海岸约束；原有10岛保留同一整体缩放/平移。切页使用既有MapTimelineMenu.Update接线，在测量与提交完成前控制世界地图子树可见性；原生滚动、点击、reveal颜色、船标及状态语义保持。公开夹具仅包含必要坐标/尺寸/索引事实与本项目素材，不包含原生游戏bitmap、源码dump、玩家文件或私人会话。

继承专项回执继续有效于其原输入范围：fixed-foundations30、coin-courier-economy55、greek-bank-scope35、greek-bank-assistants-scope73、英雄/银行组合22；哥布林bridge203、visuals23、height19、lifecycle11。此前反射Harness补HarmonyX第三个ref实参，仅修测试桥，全部原场景/断言保留。只有本轮明确重跑的项目才列为当前候选结果。

## 可复跑回归

```sh
dotnet run --project tests/native-map-icons/MapIconPlanTests.csproj -c Release
dotnet run --project tests/native-map-shore-art/ShoreArtTests.csproj -c Release
dotnet run --project tests/native-map-runtime/RuntimeProbe.csproj -c Release
dotnet run --project tests/native-scale-timing/Tests.csproj -c Release
dotnet run --project tests/native-scale-ownership/Tests.csproj -c Release
dotnet run --project tests/native-scale-ownership/crossbow-ownership/Ownership.csproj -c Release
```

原生参考程序集来自维护者已验证的2.4/BepInEx环境；Mac ARM64、Mac x64、Windows及联机验收各自独立。这里只记录列出的验证，不声称全仓所有测试通过。GitHub CI/正式PR review以当前远端head为准。

## 协作与范围

共同PR在Coordinate正式绑定issue-29；功能task保留原owner/branch，通过related_prs与本文引用共同PR。#99/#100/#104的独立PR与合并事实分别保留；#101宫廷仍是独立候选。公共AGENTS及全局checklist/events/progress历史没有复制到本PR，canonical checklist由Coordinate受控读回。

本轮地图产品修改限定于四个地图类和一张新PNG，验证实际IL/locals/EH、资源与Harmony元数据范围。阶段更新记录source/tests/install/live及精确head/制品/回执，不能用源码、模拟测试或静态入口检查替代真实游戏验收。

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
| `il2cpp/Assets/KEM_MapExtensionIsland.png` | 本项目新岸线源素材，由生成工具制作；运行时统一准备像素轮廓与选中边 |
| `il2cpp/MapExtensionIslandArt.cs` | 自有纹理/轮廓与borrowed Image租约、失败恢复及生命周期 |
| `il2cpp/MapMountIcons.cs` | 全图/详情共享轮廓、三行岸内图标、显示提交与礁石布局 |
| `il2cpp/MapResourceIconPlan.cs` | 等比岛形及透明岸线/实际blocker完整矩形布局 |
| `il2cpp/ExtensionIslandMap.cs` | 正确视图/owner范围的地形与轮廓节点接缝 |
| `tests/native-map-runtime/` | 5个公开输入，含调用层合成边界、PNG解析和168项直源回归 |
| `tests/native-map-shore-art/` | 6个公开输入，含岸线/租约、方向及布局报告；只嵌入本项目PNG |
| `tests/native-map-icons/` | 6个便携输入，保留4份精简数值fixture和2984项检查 |

正常ARM的d08f4009为维护会话此前安装并check-only的基线。本轮精确地图DLL未安装，未启动游戏、写玩家档或作正式发布；源级等待遮罩测试不代替真实Unity切页录像。原生船标模板32×32、scale1、中心(-48,18)及只更换sprite的动画已只读核对；真实存档中的活动/选中状态仍按原生运行。公开包不包含私有SVG工具、原生位图或玩家截图。
