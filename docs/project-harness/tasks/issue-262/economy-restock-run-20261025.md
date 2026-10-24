# Economya runtime: fixture world + restock cfg 单次 Native（首次真实补货扣款证据）— 2026-10-25

- Operator 批次：Worker I（teammate，DeepSeek flash）host-only 派生 fixture+restock loader-control case；Operator 审查/登记/执行。
- 组合：候选 Main `6b14879dee633f67a3e1853058579e1c7143310986bc866f7a4b040298a532b3`（2,281,472 B，含 P1–P5 引擎修复）；restock3 cfg `f0df327ecf5060e1bc672b5d5946976a3eeadfe29b599a1175a028a960e83d09`（58 keys，仅 Workers/Archers/Ninjas AutoRestock 开=true、target 15，其余 55 键与 controlled58 基线逐字节相同，derivation 有据）；探针 `609efed2…`（NativeSceneControlProbe，zero-Harmony）；native fixture `35faea071dd59332d61e80e9136ffa2c190f8bbb76f00e4e21cca169f4580fb5`（day32，260,337 B）。
- 场景：own-AVD `-read-only` overlay；Main/cfg byte-verified pair、609e、fixture 各一次安装；一次 am start；**零 Root 输入、零 Root/controller PNG**；world READY 后观察至 609e STOP；单次 gate/window/lease，不 retry/replay。
- case `controlled-candidate136-loader-restock-isolated-20261025-r5-single1`；plan **`bfca313ca22138f3fc39abc7c447b065e6bd30431dd031b77b05be8922299725`**（canonical `plan.md` 已同步）；旧 plan `48b96012…` 正常 revise/approve；gate `e5998b5b…079a3` 消费一次；window `17:47:18–18:07:18Z`；lease `19:24:40Z`（覆盖 end+15min）。
- 审批：`dsh-loader-restock-review-20261025-r5/report-public.md`（首行 `APPROVE_DSH_ECONOMY_RESTOCK_SOURCE_ONLY`，Operator self-review）；source-pins 25 pins。

## 运行事实

| 项 | 值 |
|---|---|
| controller / evidenceReportable | exit `0` / `true`；state `evidence-observed`；elapsed 241.5s |
| 候选/控制 | `mainHashState=candidate`、`controlValid=true`（mainAssemblyPresent=true;loader=melon;hash=candidate）、READY+STOP 各 1、UNKNOWN 0 |
| 世界 | `islandLand=8 campaignLand=8 gameLand=8`、`castleBuilt=true wallsCount=7`、Playing/2 |
| 经济子系统 | `[BankAssistants] Authority spawned deterministic 8-assistant pool` 1 次；`[Economy] Banker.Awake_Prefix … live=1` 1 次；BankAssistant marker 6 |
| AutoRestock 配置事实 | 3 开启（工人/弓箭手/忍者）、6 关闭；target 15；工人 summary 先 现有14·采购1·采购中 → 后 现有15·已达标 |
| **真实补货扣款** | `[AutoRestock] 工人 补货成功 price=6 nativePrice=3 coins=6`（10:49:09）；`[RestockMotion] role=工人 phase=approach/arrived/walked-out` 3 行（仅下单/移动，非支付证明） |
| 负向 | `MissingIl2CppInternalCallException`/`sig=` 0；`ANDROID_ECONOMY_AUTORESTOCK` 0（UI-only 不应在非 UI 路径出现）；restock fault markers 0 |
| 资源/清理 | postflight `fullOriginal/sourceThree=true`、reap proven、own ports/AVD clear、rootInputs 0/rootPNGs 0；controller baseline PNG 0（capture 阶段一次 2s ADB snapshot 超时，非数据证据；loader-control 分类不因此改判） |

## 结论与边界

- **首次在 Android 实机世界取得真实经济扣款证据**：`[AutoRestock] 工人 补货成功 …` 来自产品在 native `Payable.TransactionComplete()` 之后、且 shop role 另做 `GetItemCount()+1` 验证的日志点；postflight `restockVerdict=paid-purchase-observed`、`restockPurchaseIsPaymentProof=true`。同一 run 证明：fixture day32 世界在本候选下正常装载（castle/walls/island8）、live banker + 8 助手池、AutoRestock 运行时开关/计数/采购/扣款链路真实工作、且零缺失 ICall。
- 边界：只覆盖 Workers 角色的一次采购与本次世界/银行/池初始化；Archers/Ninjas 本次已达标无需采购；其余 6 类（Berserkers/Peasants/Farmers/CatapultBarrels/FireTowerAmmo/Musketeers）本次 cfg 关闭/世界无对应店，未验证；未验证保存/读档/换岛/池重建、原生交易异常、手机/多人/长跑、画面与 UI 手势；baseline PNG 捕获失败（2s ADB snapshot 超时）记录为证据限制，不影响 paid-purchase 事实。
- 证据：result `7dac367b…62cee1`、fresh/observed `6b64a034…c4969a`、postflight `4fcc9d35…ce0bee`、spent gate `e5998b5b…079a3`、canonical readback `7fe7f079…f1d0ec8914`、postrun F 仅 READ（`35faea07…`）。

## 下一步

1. 分组补齐其余补货角色（弹药/火铳，按 issue-262 验收顺序），用同 fixture loader-control 机制 + 对应 cfg 变体。
2. 原生保存/读档/换岛与池重建批次（需要新的 fixture/动作契约）。
3. #232 职业激活付款正例、付费枪 park/unpark 与画面观察；#262/#232 保持 open，PR #263 保持 Draft。

## 追加观察与下一组前提（2026-10-25 同 run）

- 同 run 另有 `[BankAssistants] first assignment helper=1 … age=3.52s` → `first submission helper=1 … accepted=1`：8 助手池中至少一个助手完成了真实“领币→交银行”闭环（与 paid purchase 同段），但这不是原生银行账本持久化/保存验收。
- r5 世界（day32 fixture）里 AutoRestock 只能看到 Workers 采购；Archers/Ninjas 已达标，其余 6 类（狂战士/无业村民/农夫/投石车油桶/火塔罐/火枪手）在该世界无对应店铺/塔（log 无 MusketeerShop/HeroShop/fire tower/catapult 等），因此下一组“弹药/火铳/全部助手”不能靠简单换 cfg 取得正例，需要另一份包含相应店铺/塔的 fixture 世界或新场景，否则只能记录为 world-precondition-missing（inconclusive）。
- 保存/读档/换岛/池重建仍需新的动作/状态契约（当前 loader-control 零输入模型只覆盖首世界观察）。

## r6：restock6 诊断（其余 6 类）— 又取得农夫/投石车油桶两次真实扣款

- 组合：候选 `6b14879d…` + restock6 cfg `b56d5a8749106cb2ba68aea42d1515aad3105218ee0b3ac0c0e051fed296978e`（58 keys；相对 controlled58 的 15 个 changedKeys = 6 个补货开关 true + 9 个 target=200；W/A/N=false；其余 43 键逐字节保持；derivation 有据）+ fixture `35faea07…` + 609e，零 Root 输入。
- case `controlled-candidate136-loader-restock6-isolated-20261025-r6-single1`；plan **`6cbd2a19…`**；旧 plan `bfca313c…` 正常 revise/approve；gate 消费一次；window `18:03:39–18:23:39Z`。
- 结果：controller exit0、`evidenceReportable/controlValid=true`、READY+STOP、world `islandLand=8/castleBuilt=true/wallsCount=7`、`[BankAssistants] Authority spawned deterministic 8-assistant pool`、live banker=1；elapsed 226.7s；controller baseline PNG 1。
- 真实扣款（2 次）：`[AutoRestock] 农夫 补货成功 price=8 nativePrice=4 coins=8`（RestockMotion 3 相；现有 15→17）；`[AutoRestock] 投石车油桶 补货成功 price=10 nativePrice=5 coins=10`（弹药 16→17；RestockMotion 3 相）；postflight `restockVerdict=paid-purchase-observed`、`restockPurchaseIsPaymentProof=true`、`worldPreconditionMissing=false`。
- 其余角色事实（world/配置前提，非产品失败）：狂战士 “对象池未就绪/订单已满”；无业村民 “无可用目标/订单已满”；火塔罐 “无可用目标”（该世界无火塔）；火枪手 “请先开启火铳铺”；Workers/Archers/Ninjas 已关闭（前批已达标）。零 `MissingIl2CppInternalCallException`/`sig=`、零 Root 输入、资源/原盘交还通过。
- 判读：day32 fixture 世界确实存在 工人/农夫/投石车油桶 店铺并完成真实扣款；火塔/火枪手/狂战士/村民的采购需要不同世界或额外开关（火枪手 gate=`请先开启火铳铺`），不能据此判失败。覆盖边界：3 个角色（工人/农夫/投石车油桶）真实采购；其余 6 类未全部取得正例；保存/读档/换岛/池重建仍未验证。

## r7：火铳铺开启诊断 — 店铺成功放置，火枪手自动采购仍 inconclusive

- 配置：候选/探针/fixture 同 r5/r6；cfg `feefad796591f2043a671085d84d55735f74f3bbac40f19fc680ac0ce82c2fc1`（58 keys；restock6 父本 + `MusketeerEnabled=true`；相对 controlled58 changedKeys=16、其余 42 键保持；9 targets=200）。
- case `controlled-candidate136-loader-restock7-isolated-20261025-r7-single1`；plan **`1a3070ea…`**；旧 plan `6cbd2a19…` 正常 revise/approve；gate 消费一次；window `18:17:19–18:37:19Z`。
- 结果：controller exit0、`evidenceReportable/controlValid=true`、READY+STOP、world `islandLand=8/castleBuilt=true/wallsCount=7`；`[MusketeerShop] owner preflight passed`、`ready x=20.21 native-payment=registered price=4`、`native bow prefab cached` — 火铳铺在 day32 世界成功放置；此前 r6 的 `请先开启火铳铺` gate 消失。Elapsed 227.6s。
- 同 run 再次取得真实扣款：`[AutoRestock] 农夫 补货成功 price=8 nativePrice=4 coins=8`、`[AutoRestock] 投石车油桶 补货成功 price=10 nativePrice=5 coins=10`；postflight 非火枪手部分 `paid-purchase-observed`。
- **火枪手自动采购 inconclusive**：唯一 role=火枪手 行为日志为 `火枪人数或身份尚未确认`（shop ready 之前 1s），之后无该角色 order/purchase 行，`musketeerAutoPurchaseCreated=false`、`musketeerPurchaseFact=false`。本候选 role-8 分支无通用 `补货成功` 行，零输入下的判据应为 `[MusketeerShop] purchase … result=created`（本 run 未出现）。该状态是身份/人数 gate（MusketeerSync 已 `owner:global-v`、Musketeer loaded records=12 bound=10 stock=0），不能判失败，也还不能判功能通过。
- 零 `MissingIl2CppInternalCallException`/`sig=`；资源/原盘交还通过；零 Root 输入。
- 下一步：源码级分析 `TryGetRestockCounts`/MusketeerIdentity 在 stock=0 且 shop 后放置时是否会重试/确认身份（判断是设计前提还是 Android 特有问题）；或用已有火枪手/枪架的存档世界验证 role-8 自动采购。其余仍未验证：狂战士池前提、火塔世界、保存/读档/换岛/池重建。

### 证据哈希（r6/r7）

- r6：result `0aa3c620…b6b91b`、observed `f5be7c9d…c774821e`、postflight `63b4f406…1db87ac3`、spent gate `8b2b0cc3…d6fe17e`、canonical readback `41c1836e…07ab`。
- r7：result `75a9d6c1…d35259`、observed `662142b4…8885eec`、postflight `6a68c8c6…cf18dd`、spent gate `0253b186…cdeac7`、canonical readback `3c6d5945…8058b8c`。
