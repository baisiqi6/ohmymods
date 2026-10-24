# Gameplay readiness（职业激活）单次 native 验证 — 2026-10-25

- Operator 批次：Worker E（gameplay-recon，teammate）只读对比 Codex-era `next-profession-input-routing-case`/`profession-input-routing-controller` 等模板并派生 host-only case 工具；Operator 审查/登记/执行；候选为 visual-family 批次 Android net6 Release `b1c9b623e7ba609c2348d67829f18be95abbb7b323988a176d8f857b2e8aecfa`（2,281,472 B），cfg `16b686…`（activation4，58 keys），gameplay addon V3 0.0.11 按 b1c9 重编 `27e8945ea861d9b7627810e8c8a2bb1b09c355b632704dffd4bb123ab8e77fd5`（51,200 B；receipt `1f7dff93…`）。
- 模板场景：own-AVD `-read-only` overlay；byte-verified Main/cfg 对 + V3 observer；一次 `am start`；V3 120s/128 事件观察；Root 最多 2 次一次性 swipe（27s move + 条件 400ms pay），Root 0 PNG、controller 恰 1 baseline PNG、controller 自身 0 输入；一次性 gate/window/lease，不 retry/replay。
- 源审批：`dsh-gameplay-profession-activation-review-20261025[-r2]/report-public.md`（Operator self-review，marker `APPROVE_DSH_GAMEPLAY_PROFESSION_ACTIVATION_SOURCE_ONLY`）。

## r1：ADB 日志快照超时，不可报（工具层失败，不据此得产品结论）

- case `controlled-candidate135-gameplay-profession-activation-isolated-20261025-single1`；plan `ede93628…f604ad69`；gate `ec1d78e1…e5531b` 消费一次；window `16:47:20–17:12:20Z`。
- Main/cfg/V3 均 byte-verified 安装；客机实读候选 hash、app133 行出现；controller 在场景加载期间一次 2s `Latest.log` snapshot `exec-out` 超时，`case.adb` 抛 `RuntimeError: ADB command failed/unknown; preserve evidence, no blind restore`；`state=inconclusive`、`evidenceReportable=false`、`elapsed 132.8s`；cleanup stop/reap 正常。
- 证据：`result.json` `f5f129aff96b…61625e`、`observed.log` `618ab1d79526…ebefd5b`、spent gate `ec1d78e1…e5531b`；没有产品结论（PROFESSION/BANK 行虽在日志中出现，但未进 controlled 观察窗）。

## r2：同候选/同 cfg/同 addon，仅把日志快照超时 2s→8s（仍受绝对 deadline 限制），fresh 单次 case

- case `controlled-candidate135-gameplay-profession-activation-isolated-20261025-r2-single1`；plan **`7d72dadf1d21b27f0cbcfba3c529f790852f4f7d75cc17bf714b97d6df77c73e`**（canonical `plan.md` 已同步）；旧 plan `ede93628…` 正常 revise/approve；gate `9f837b80…fd2966b` 消费一次；window `16:58:11–17:23:11Z`；lease `18:20:47Z`（覆盖 window end +15min）。
- controller exit `0`、`state=evidence-observed`、`evidenceReportable=true`、`controlValid=true`、`controlReason=mainHash=candidate;app133=133;initContract=exact`；`elapsed 250.9s`、world 186.3s、observation 到 V3 `S1_INPUT_STOP reason=time-budget elapsed=120.14 events=11`；baseline PNG 1、gestureObserved=true、floatGuiSeen=true。
- Root postflight（`fb620014…b431`）：`fresh8EqualResumeOriginal/fresh8BeforeAfterAndNowEqual=true`、controller+qemu absent、own ports/AVD clear、`resourceHandbackReady=true`；root inputs **1**（`01-move.intent.json`）、root PNGs 0、controller PNG 1、`rootInputContractHeld=true`；bankMarkerCount=13、professionMarkerCount=12、readinessMarkerCount=2；`nativeFullGameplayAcceptance=false`（快照非激活/付款/存档证明）。

### 同段事实（fresh-segment.log）

| 事实 | 计数/值 |
|---|---|
| 候选 Main 实读 | `SHA256 Hash: 'B1C9B623…ACFA'` 1 次；旧 `5460EFD9…` 0 次 |
| 职业/弓弩 combat hooks | `ANDROID_PROFESSION_COMBAT_REGISTERED owners=133 sharedSource=true` 1 次 |
| V3 契约 | `contract=s1-input-surface-0.0.11` 1；`S1_READY_INIT` 1；`S1_INPUT_SURFACE` 1；`S1_INPUT_STOP` 1 |
| PROFESSION_SNAPSHOT / FLAGS / OWNER / WALLET / READINESS | 2 / 2 / 6 / 2 / 2 |
| PROFESSION_FLAGS（首末两点） | `enabled=true infiniteMoney=true musketeerEnabled=true heavyShieldEnabled=true heroArcherEnabled=true` |
| PROFESSION_WALLET | `nativeInfiniteMoney=true cfgInfiniteMoney=true consistent=true` |
| BANK_INIT_SNAPSHOT / SLOT / ATLAS_SHEET | 1 / 8 / 4（`poolsLength=8 syncIdsLength=8 texturesLength=4 spriteSheetsLength=4 bank=ok atlas=ok`，managed refs only，非 native-alive/visual-usable） |

### Root 输入事实（1 次 move，实际路由成功）

- `adb shell input swipe 128 540 → 1152 540 27000ms`（unity 64,90→576,90；screen 640x360，png 1280x720，sx=sy=2，generic `_touchHandler` route，activeRect=Rect.zero；touch square intersects=false，basis=managed Layout/touch square）。
- pre：`x=-76.0, coins=0.0, payState=None/0, selectedPayable=null`；post：`x=-20.934, coins=11.0`，`deltaX=55.066`，`campDistanceAfter=8.934`。
- outcome=`DISCRETE_SIDE_EFFECT_STOP`（move 期间 coins 0→11 的离散 side effect，action2 pay 被禁止；`root-input.py` 因此 exit 1，`payAttempted=false`，无 retry）；`rootInputsObserved=1, rootPNGs=0`。

## 边界（不得扩大）

- 本条只证明：候选/配置 byte-verified 装载与 Melon hash、职业/弓弩 combat hooks 注册（owners=133）、四开关与 InfiniteMoney 运行时一致、3 个 shop owner 注册行、银行初始化 8 池/4 atlas managed refs 事实、V3 0.0.11 世界输入表面存在、一次真实 swipe 被路由并改变玩家位置/拾取金币（`DISCRETE_SIDE_EFFECT_STOP`），以及资源/清理/主机保护不变量。
- 不证明：付款/OnPay/钱包消费、商店实际 placement、城堡创建、角色/画面/职业视觉、保存/读档/换岛/池重建、付费枪 park/unpark、手机/多人/长跑；`BANK_*` 行是 managed-refs-only，不是 native 池存活/可用；`PROFESSION_*` 是当前世界 readback 事实，不是激活完成。
- 已知缺口（Worker F 全量 API sweep）：P1 `Physics2D.GetIgnoreCollision`（wall-pierce 静默失效）与 P2 `Animator.GetFloat(string)` 等 8 条引擎成员缺失，修复分支已产出（P1/P2：`dsh/engine-api-gaps-wallpierce-20261025` @ `e90423f`；P3–P5 在同批处理中），尚未集成/建新候选/复验；新候选会再次改变 Main hash 与 addon binding，届时需整链派生。

## 证据

| 证据 | SHA256 |
|---|---|
| r2 result | `e7aa656ac2e53f5d5f5dbbe6a4de70f1b30a489dbf7cacedb802b915a5f4c65a` |
| r2 fresh/observed log | `bfba84b8a2c2e4e23a13438bea27607cd55be4ea1bc4137b02fcdd291c5ec605` |
| r2 root postflight | `fb620014ebbbf5bd79ff2379e2ff19cbfe183ac25b13c3794e375ec696b7b431` |
| r2 move result | `18b577b6032903603d7f3cccb7be355e1a6a44f52a8aa066982023cba4b9ea97` |
| r2 spent gate | `9f837b8067fad08fec0f0e6e1bba1329617ea80f976b1a2e7034ac017fd2966b` |
| r1 result | `f5f129aff96b5ac7bf249e27305f2610e5a1798a7de0d68f055273fb1f61625e` |
| r1 fresh log | `618ab1d7952678923d0dde5299cc9bbb56c1645c2167a4656adeece68ebefd5b` |
| r1 spent gate | `ec1d78e19908af4e20de1f4e1e04d7d4b8bd2c891b3d516c26b217e94ae5531b` |

## 下一步

1. 集成 Worker G（P1/P2，`e90423f`）与 Worker H（P3–P5）修复，构建新候选（G-only 产物 `d5223bec…`），跑全量受影响 managed 套件与 Android 编译闸。
2. 候选变化后按既有链重建 V3 addon binding / review / pins / gameplay case（不能只改单个常量），用新候选重跑本 readiness case 作为回归。
3. 之后才是职业/银行玩法正例（付款、placement、读档/换岛）与视觉/手机/多人/长跑实机验收；Issue #232/#262 保持 open，PR #263 保持 Draft。
