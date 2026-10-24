# Code-range 收口矩阵 — issue #232 / #262（2026-10-25）

- 分支：`codex/android-economy-262`，产品 HEAD `9c81e63`（P1–P5 集成），docs HEAD 见 `progress.md`；候选 Main **`6b14879dee633f67a3e1853058579e1c7143310986bc866f7a4b040298a532b3`**（2,281,472 B；208 Compile/176 ReferencePath/25 资源；`noDeployment=true`）；gameplay observer V3 0.0.11 重绑候选 `6e3f5599…`；609e NativeSceneControlProbe 未变。
- 引擎 ICall/API 静态面：候选全量 MemberRef 3,920；25 条引擎 native 分发中 17 已注册，**可达缺口 P1–P5 全部修复并集成**（`442faca` P1 `Physics2D.GetIgnoreCollision`/P2 `Animator.GetFloat(string)`；`b78b03a`+`9c81e63` P3–P5 数组/NonAlloc/Bounds.ClosestPoint 路径）；复扫仅剩 **P6–P8**（DeadlandsPowers/DeadlandsFollowerCapture 诊断路径，当前 Android 安装集不可达）→ **决策：不修，保留记录**，将来移植 DeadlandsPowers/采集面板时再评估（见 `android-engine-api-gaps-20261025.md`）。已知缺失族（forceRenderingOff/PropertyBlock/flipY/unscaledTimeAsDouble/GetPixels32/LineRenderer.useWorldSpace 等）在候选 IL/字节中 0 引用。
- 全量 managed 回归（当前 HEAD）：heavy-shield PC 1144 / Android 6；hero-arrow-pierce 177（missing 13）；crossbowman-wallpierce 34 tests/311 断言（missing 34）；musketeer PC 121 / Android 122；wall-engineers PC 260 / Android 297；auto-restock 159；auto-restock-counts 86；coin-courier-visuals 33；bank-teleport 17；teleport-fx 15；greek-bank 73；bank-assistant-atlas 37；banker-fixed-foundations 31；hero-native-visuals 161/164；hero-scarf 41/49；cloth-view 38；greek-impact xunit 88；hero-archer/greek xunit 85——全部通过。

## Issue #232（职业身份/弓弩/引擎兼容）

| 要求 | 状态 | 证据 |
|---|---|---|
| Android 架构（LemonLoader/MelonLoader/Il2CppInterop、PC 语义不变） | ✅ 代码+审查 | worktree/分支、各 fix commit、PC 分支逐字保留审查 |
| 引擎 ICall/API 缺口（视觉族 + 物理/动画读） | ✅ 修复+集成+复扫 | `d1d6ef2`/`0845682`/`442faca`/`b78b03a`/`9c81e63`；复扫 P1–P5 归零 |
| 候选构建（Android net6、无部署、输入绑定） | ✅ | `6b14879d…`；208/176/25；`noDeployment=true` |
| 启动/世界加载/PoolManager.InitPools | ✅ native 事实 | r2/r3/r5/r6/r7 同段 `worldReady=true`、`[AndroidPoolLifecycle] native InitPools returned`、无同船 NRE |
| 职业激活 readiness（owners=133、四开关、钱包一致、BankInit） | ✅ native 事实 | r2/r3：`ANDROID_PROFESSION_COMBAT_REGISTERED owners=133`、PROFESSION 2/2/6/2/2、BANK 1/8/4 |
| HeroArcher/Musketeer/HeavyShield 视觉与接入 | ✅ native 事实 | visual-family r3：HeroArcher visual=True/attach 2 次、零缺失 ICall；新候选 r5–r7 无 MissingICall |
| 真实付费激活/付款、付费枪 park/unpark、画面验收 | ⏳ 未验证 | 需要商店 placement/付费输入场景或带 sidecar 的已保存世界（当前 fresh/fixture 世界均无付费正例） |
| 原生保存/读档/换岛/池重建（身份往返） | 🟢 保存+读档两run已验 / 换岛·池重建未验 | r9 自然保存写出新 global-v35 `e6e6e81d…`+4 sidecar；r12 以该存档+sidecar 为输入读档：首 READY/观察末/stop sha 一致、`[HeroRecruitment] loaded:paid:ctx/epoch` 与 `[Musketeer] loaded:records=12` token 匹配、postflight `loaded-identity-matched`、cleanup 经 reap 证明（`save-load-roundtrip-20261025.md`）。不是同进程 reload；**读档后 InitPools+Archer/助手/CrossbowBolt 池重注册已在 r12 同段观测**；换岛触发的池重建仍未验 |
| 手机/平板/多人/长跑 | 🚫 玩家实机单列 | 不阻塞代码范围 |

## Issue #262（经济/8 助手/9 类自动补货）

| 要求 | 状态 | 证据 |
|---|---|---|
| 17 份共享源接入、唯一 AutoRestock.Tick、两组件注入 | ✅ 代码+审查 | PR #263 diff；`PatchEconomy_BankAssistants`、Android pool lifecycle 接入点 |
| 18 个 AutoRestock 配置（9 bool 默认 false + 9 target 默认 15、载入 1..200、原 40 键/legacy 保持） | ✅ 代码+host tests | `android/MobilePlayerConfig.cs:161-190`；auto-restock 159、counts 86 |
| owner 表 133/141（106 + 25 新 + 2 challenge delete） | ✅ native 事实 | `ANDROID_PROFESSION_COMBAT_REGISTERED owners=133 sharedSource=true`（r2/r3 同段） |
| 8 助手 30120..30127 池 / 4 atlas / 903 Banker authority | ✅ native 事实（池/收集） | r5/r6/r7：`[BankAssistants] Authority spawned deterministic 8-assistant pool`、`first assignment/submission accepted=1`、`live banker=1` |
| 银行保存 hooks（IslandSave Begin/Postfix/Finalizer、Marker、campaign/challenge delete、SharedBankNative） | ✅ 代码+静态审查 | `il2cpp/CoinCourierPersistence.Hooks.cs` 全站点；save/load 实机写入仍未验证 |
| 经济 UI 子页/配置保存 | ✅ 代码+host tests（未跑真实手势） | `android/Patch_EconomyAutoRestockMenu.cs`；菜单/动作 host tests 绿 |
| 真实补货扣款正例 | 🟡 部分 | 3 角色真实扣款：工人 price=6/native3（r5）、农夫 price=8/native4（r6/r7）、投石车油桶 price=10/native5（r6/r7），均零 Root 输入、postflight `paid-purchase-observed` |
| 其余 6 类补货正例 | 🟡 部分/inconclusive | 狂战士 对象池/订单；村民 无目标；火塔罐 世界无火塔；火枪手 r7 火铳铺已放置但 `火枪人数或身份尚未确认`（fixture 世界无 identity baseline/sidecar，属世界前提，不判失败） |
| 保存/读档/换岛/池重建（银行账本往返） | 🟢 native 存档+prefs 内容级已验 / 换岛·池重建未验 | r9 保存 + r12 读档（输入=回读 sha 一致）；r13 probe v0.0.2 内容级回读：CoinCourier `7b28c96c…`、SharedBank `5691a131…`、HeroRights `62a0f6a3…` 与解码 manifest 完全一致；MusketeerRights 差异经源码核因为 load 时 `ConfirmBaseline` 设计内重确认（非失败）。读档后 InitPools+池重注册已观测；换岛仍缺输入/世界前提 |
| 手机/平板/多人/长跑 | 🚫 玩家实机单列 | 不阻塞代码范围 |

## 已知测试基建限制（非产品失败）

- `tests/shared-bank-regressions/{capture,recovery}` 已修复：补入 `il2cpp/MainBankerFixedDomain.cs` 与共享测试边界 `tests/shared-bank-regressions/WallSpotsBoundary.cs`（真实 `MarkerPrefix="KEM_WallSpot"` 常量）后分别 **5 passed / 5 passed**，覆盖银行保存 capture/recovery host 路径。
- `tests/shared-bank-regressions/hotpath` 仍不能 host 编译（其本地精简 stub 缺 `Il2CppInterop.Runtime.InteropTypes`/`Wallet.SuckCurrency`/`Managers.OnLevelLoaded` 等较新成员）；属性能/热路径套件，Android 构建与本次正确性验证不受影响，如实记录不虚报为通过。
- iOS? 无。

## 关闭/合并门（当前不满足）

1. 代码范围：源码+审查+静态/构建/测试已完成；引擎可达缺口已修。
2. 必要运行：232 的付费激活/付费枪/保存-读档-换岛-池重建、262 的其余补货正例与银行账本保存往返仍未完成——需要（a）更后期、带 mod sidecar 的 Android 存档世界，或（b）扩展输入/状态契约（保存菜单/换岛/商店 placement），或（c）明确把剩余归入玩家实机验收并记录边界。
3. PR #263 仍 Draft，未合并；#232/#262 保持 open。玩家实机/手机/多人/长跑单列，不作为代码关单前置。

## 结论

当前分支**已知可达的 Android 兼容缺口修复完毕**（P1–P5 修复+复扫归零，P6–P8 不可达决策记录），构建/审查/host 全绿；剩余是**世界/输入前提受限的 gameplay 正例**，不是新的源码兼容缺口。收口需要 #232/#262 计划中的必要运行或用户对范围的明确选择。
