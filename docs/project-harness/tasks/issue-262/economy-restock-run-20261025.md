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
