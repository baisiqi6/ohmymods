# Renderer ICall producer 修复 + 单次 Native 复验 — 2026-10-24

- Operator 批次（用户 2026-10-24 全权授权；Worker 为内置 subagent DeepSeek flash max，Operator 负责集成/审查/构建/单次 Native）。
- 产品修复 commit（已推送 `codex/android-economy-262`）：`c219546`（HeavyShield renderer/time compat + 共享 `AndroidUnityCompat.cs`）、`a0b0226`（注释修正）、`d52e6ac`（CoinCourierTeleportFx Android mesh ribbon）、`b1abc95`（Operator 追加的同段诊断 info），HEAD `b1abc95057ce460839dc3b5c2c037f3cf94eed49`。Worker 分支/报告：`.local/tasks/android-profession-archery-20261014/dsh-workers-20261024/{heavyshield,coincourier}/result.md`。
- 构建：Android net6 Release `OhMyMods.AndroidProbe.dll` `da2240ee5a60d824f7d24782b66b4159265663ee303dee8022d311eeb633c27e`（2,280,960 B；208 Compile/176 ReferencePath/25 EmbeddedResource，构建前后输入 hash 保持；`BepInExPluginsPath=`、`noDeployment=true`）。
- 回执：`renderer-icall-fix-20261024/root-build/{release-binding.json,receipt.json}`。
- 自审：`resume-20261019` 派生批次外的 `dsh-renderer-fix-review-20261024[-r2]/report-public.md`（Operator self-review，非 Codex 独立批准）。

## r1：单次 case 因 QEMU 崩溃不可报（不据此得产品结论）

- case `controlled-candidate134-renderer-icall-fix-isolated-20261024-single1`；plan `af705d11…2fb927`；gate 消费一次；window `08:47:25–09:07:25Z`。
- 候选已按 byte-verified pair 安装且 MelonLoader 实读 `SHA256 Hash: 'DA2240EE…'`；fresh log 到 “2 Mods loaded.”（01:48:43 设备时间）后无任何 mod 日志，ADB 随后 `error: closed`；macOS 记录 `qemu-system-aarch64-headless` SIGSEGV in `android::crashreport::CrashReporter::GenerateDumpAndDie`（hang detector），emulator.log 记录 QEMU2 main loop/CPU0-3 hanging 15s。
- controller `state=inconclusive`、`observationReason=log-stat-unknown`、`controlReason=init-missing`、`evidenceReportable=false`、`elapsed=119.2s`、READY 未出现；cleanup reap `returnCode=-11`。
- 证据（原样保留，不重跑同一 gate）：`actual/result.json` `b71a2cef…4b70`、`observed.log` `9f9807f3…c06`、`emulator.log`、崩溃报告 `~/Library/Logs/DiagnosticReports/qemu-system-aarch64-headless-2026-10-24-014919.ips`。
- 判定：该失败发生在客机加载两 mod 后、任何产品日志/世界观测之前，且为宿主 QEMU 崩溃；可作为环境失败，不支持任何产品修复结论，也不作为修复被证伪的证据。

## r2：同源同候选的干净单次复验（可用于同段判定）

- case `controlled-candidate134-renderer-icall-fix-isolated-20261024-r2-single1`；plan **`af6338720b2c16e15666dc930bd303ca9e233d133b50c930b87b96903e2fb927`**（canonical `plan.md` 已同步）；旧 plan `af705d11…` 正常 revise/approve；gate `c3c8164d…7038e` 消费一次；window `08:56:22–09:16:22Z`，lease `10:30:03Z`（覆盖 window end +15min）；r2 源审批报告 `ba38e24d…5ebd2`。
- controller exit `0`、`state=evidence-observed`、`evidenceReportable=true`，`elapsed 239.5s`、observation 181.1s（time-budget）；candidate Main 归属 `da2240ee…`、`controlValid=true`、`init/ready/stop` 各 1、`Playing/2`、island/campaign/game=`8`、无同船 NRE、UNKNOWN 0；同段产品 `[AndroidPoolLifecycle] native InitPools returned count=109`；baseline PNG 1。
- Root postflight（`12802f17…70990`）：`fullOriginal=true`、`sourceThree=true`、reap proven、`ownResourceClear=true`、root inputs 0/root PNGs 0、controller PNG 1、pairOk；postrun F 仅 READ（`35faea07…`，260337B）。

### 两处 ICall 失败的同段证据

- 负向：`observed.log` 中 `attach held`、`effect group create failed`、`MissingIl2CppInternalCallException`、`sig=`、`tick failed`、`register failed`、`register rollback`、`InvalidOperationException`、`material or white texture unavailable` 全部 **0** 次。
- 正向：
  - `[HeavyShieldVisual] android: per-instance MaterialPropertyBlock not copied (…)` 恰 1 次（01:58:11），且同秒 `Il2CppInterop Registered mono type …HeavyShieldVisualDriver`——证明 HeavyShield attach 已越过 `forceRenderingOff` 前置，完成自有 renderer 注册与 CopyLook（PropertyBlock 受限路径）；无后续 attach/tick 异常日志。
  - `[CoinCourierTeleportFx] android mesh dashes ready count=16` 恰 1 次（01:58:14），紧邻旧失败同触发点 `[BankAssistants] first assignment …` / `first submission … accepted=1`——证明 teleport mesh 组在旧 `useWorldSpace` 抛错处成功建成。
  - `[NorseSquad] load restore summary` 仍为 `converted=0 shieldReady=0 pending=86 attempts=24`（与旧诊断批次一致；该计数不用于本批 attach 结论）。

## 边界（不得扩大）

- 本条只证明：同段启动/加载与两处旧 ICall 触发路径（HeavyShield attach 视觉注册、CoinCourier teleport mesh 创建）实际执行且不再抛缺失 ICall；不证明盾体/线条画面观感、SetHideStatus/第三方 enabled 写冲突、受击 tint（PropertyBlock 未复制）、dash 透明 tie-break、相机非正交差异、保存/读档/支付/玩法/手机/多人/长跑。
- Android hide 原语为共享 `Renderer.enabled`（PC 独占 `forceRenderingOff` 的语义差已记录在 source review 与 worker 报告）；本批未观察到相关异常日志，但需要后续 gameplay/实机观察确认。
- 同族 `HeroArcherVisuals`/`MusketeerVisuals`/`MusketeerGunVisuals`/`HeroArcherCloth` 的缺失成员仍未修，进入其功能路径会重复同类异常；另列后续批次。
- `AnimationCurve.Evaluate`、Mesh/MeshFilter/Renderer/Material/Texture2D 等新用 ICall 在 r2 的 CreateSlot 路径均未报缺失签名（负向计数 0），属同段正面事实，但不等于长时间/多次池复用/换岛读档后仍稳定。

## 下一步

1. 继续职业/银行补货/存量玩法批次，观察这两处修复在 detach/池回收/读档/换岛后的行为与画面。
2. 单独批次迁移 HeroArcher/Musketeer 同族 renderer 缺失；补 PropertyBlock 缺失的观感契约（如受击 tint 需要）。
3. 视觉/手机/多人/长跑仍为玩家实机验收；Issue #232/#262 保持 open，PR #263 保持 Draft，不合并/不关单。
