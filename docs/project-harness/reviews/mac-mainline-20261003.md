# Mac累计候选审阅与验证分层

> **Agent provenance:** `Mac Max / Codex` · role=`Maintainer Operator` · acting_for=`ohmymods Mac Operator`

本批恢复希腊公共银行的存档/战役归属、英雄驿站的购买与保存一致性，并新增承载16种跨世界坐骑的探索岛及完整卷轴资源地图。代码基于共享发布线 `release/v9.5.13@28345ac380feecba65acda6319a86169be9890e5`，包含此前已接收而未进入远端的Windows候选支持文件。默认 `master` 属于旧历史，不能用作此批业务基底。

这是 #29 的新整合阶段，关联 #51/#85/#97/#98；不自动关闭任何功能Issue。相邻的 #99 实玩Bug、#100固定墙基银行家范围与 #101宫廷代码保持独立，本PR排除它们的增量。此前已安装26f286d6包含#99，不能把它当作本PR对应制品；本PR精确DLL尚未安装，当前任务不合并、不部署、不发布或修改玩家数据。

## 逐项状态

| 范围 | Issue / Coordinate记录 | 源码 | 验证 | 安装与实机边界 |
|---|---|---|---|---|
| 接收的盾卫候选 | #51 / issue-51 | 职业、盾具店、持久化、耐久4及职业互斥接线已接收 | ARM完整组合编译；继承专项测试按原回执保留 | 包含在正常ARM组合；Mac领盾、耐久/退职、读档与联机尚未专项验收 |
| 银行公共余额 | #97 / issue-97 | 同存档/战役共享、跨战役隔离、原生保存边界和现有经济责任保持 | 本head state28、capture5、recovery5；既有55核心与七继承回归另有历史回执 | 7dcdbf52曾确认启动加载；26f286d6仅安装/check-only，玩家跨岛提款/隔离完整现场待验 |
| 英雄购买/责任 | #85 / issue-85 | 不上船、旧付款保留与新购买/异步保存门协同 | 本head core47、lands24、carry22；bank-combo22 | 包含在正常ARM组合；反馈问题的完整实际购买/保存闭环待验 |
| 跨世界坐骑、扩岛与地图 | #98 / issue-98 | 16原生获取/能力依赖、physical11/UI10、原生地理关系及全纸域坐骑两行布局 | 本head portable纯函数2950；此前真实船标耦合源/坐标检查在282/300宽=.60，314/400=.70，16完整无碰撞 | 正常组合已安装；本次最终地图像素/点击和land11完整往返、骑乘技能、保存重载尚待用户 |
| 缩放稳定性与农舍猫接收修订 | #29；既有greek-scale-scope-20260914 / fleet-retreat-cats-20260912记录 | 继承候选与相关测试收录；不是#99的身高+5%增量 | 原接收/测试回执与本完整ARM构建分开 | 不把本次源码接收当作新实机完成，既有owner不变 |
| 实玩Bug / 新银行家范围 | #99 / #100 | 原维护会话独立PR；本PR不包含其增量 | 各自留证 | 后续按Issue→PR→授权合并→原安装边界推进 |
| 原创宫廷 | #101 / issue-101 | 独立候选模块和素材，不进入本PR；拟physical12，文件表/配置须主线协调 | 部分模块与隔离原生证据 | 正常ARM未接入；一次支付和部分保存不等于完整访问往返，待履行支付责任保留 |

## 当前精确候选验证

代码冻结commit `07c524924c23f7a0b39cd2339df2f915e767d19c`；后续提交仅同步测试与本验证文档。实际Mac ARM参考程序集161项，Standalone .NET6程序集使用已安装.NET8 SDK构建：0 warnings / 0 errors。验证包装器assembly version为10.8.38，仓库项目Version保持原10.8.35，本PR不做正式版本发布。产物SHA256 `bac224fb76a59e34c90d5780780556ae3ac63475eccde64ce2bc3d0c42c006da`，未安装。

对已验且无#99的fc82基底进行IL/locals/EH范围比较：6475方法保持，33张PNG逐字节相同，331个全部Harmony目标及既有原生扩岛钩子保持；变化仅限最终地图允许类型。代码逻辑字节保持已接纳输入，仅统一文本换行、移除两处无语义尾空格。逻辑提交分组服务审阅；仅最终head承诺本次构建与测试，不声称每个中间commit已单独验证。

本head另复跑旧哥布林bridge197、visuals23/0（含作者manifest）；地图pure2950/0，fixture仅保留10个原生岛簇坐标/尺寸与必要图标索引/尺寸元数据，不包含游戏bitmap、源码dump、玩家文件或私人会话。源/坐标夹具检查不等同真实Unity屏幕、原生航行或玩法闭环。state fixture以scope-local `DOTNET_ROLL_FORWARD=Major` 在现有.NET8 runtime运行，不安装额外runtime。

## 可复跑回归

```sh
dotnet run --project tests/native-map-icons/MapIconPlanTests.csproj -c Release
dotnet run --project tests/coin-courier-runtime-bridge/CoinCourierRuntimeBridgeTests.csproj -c Release
dotnet run --project tests/coin-courier-visuals/Regression.csproj -c Release
DOTNET_ROLL_FORWARD=Major dotnet run --project tests/shared-bank-state/StateFixture.csproj -c Release
dotnet run --project tests/shared-bank-regressions/capture/Capture.csproj -c Release
dotnet run --project tests/shared-bank-regressions/recovery/Recovery.csproj -c Release
dotnet run --project tests/hero-purchase-consistency/bank-combo/Proof.csproj -c Release
dotnet run --project tests/hero-purchase-consistency/Proof.csproj -c Release -- core /tmp/ohmymods-review-core
dotnet run --project tests/hero-purchase-consistency/Proof.csproj -c Release -- lands /tmp/ohmymods-review-lands
dotnet run --project tests/hero-purchase-consistency/Proof.csproj -c Release -- carry /tmp/ohmymods-review-carry
```

原生参考程序集来自各维护者已验证的2.4/BepInEx运行环境；Mac ARM64、Mac x64、Windows与联机验收各自独立。这里只记录本次列出的验证，不声称全仓所有测试通过。GitHub CI/正式PR review仍由当前head的真实远端状态决定。

## 协作与范围

共同PR仅在Coordinate正式绑定issue-29；同一PR不绑定多个task。功能task保留各自原owner/branch，用related_prs与本公开文档引用它，不能把共同审阅分支冒充原任务分支。公共AGENTS、全局checklist/events/progress及历史handoff没有复制到此PR，避免覆盖 #16/#76；远端运行的canonical checklist仍以Coordinate受控读回为准。

后续改动先查询Issue/PR及Coordinate，再登记文件范围；发现重叠先确定唯一writer。阶段变化继续更新source/tests/install/live和精确head、制品/验证回执，内部调度、凭据、玩家数据仅保留在私有过程材料。

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
| `tests/native-scale-timing/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/README.md` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/Stubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/Tests.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/crossbow-ownership/LifecycleStubs.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/crossbow-ownership/Ownership.csproj` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/crossbow-ownership/Program.cs` | 接收支持文件 | Windows接收输入 |
| `tests/native-scale-timing/crossbow-ownership/README.md` | 接收支持文件 | Windows接收输入 |
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
