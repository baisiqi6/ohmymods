# Visual family renderer-ICall migration + single native validation — 2026-10-25

- Operator 批次（用户 2026-10-24 全权授权；Worker 为 Agent Teams teammate，DeepSeek flash，Operator 审查/集成/构建/单次 Native）。
- 产品修复 commit（已推送 `codex/android-economy-262`）：`d1d6ef2`（HeroArcherVisuals + HeroArcherCloth）、`0845682`（MusketeerVisuals + MusketeerGunVisuals）；base `834854a`，HEAD `08456821c506873aeabd558e7da92b7c2d238d00`。Worker 报告：`.local/tasks/android-profession-archery-20261014/dsh-workers-20261024/{heroarcher,musketeer}/result.md`。
- 构建：Android net6 Release `OhMyMods.AndroidProbe.dll` `b1c9b623e7ba609c2348d67829f18be95abbb7b323988a176d8f857b2e8aecfa`（2,281,472 B；208 Compile/176 ReferencePath/25 EmbeddedResource，构建前后输入 hash 保持；`BepInExPluginsPath=`、`noDeployment=true`）。
- 回执：`visual-family-20261025/root-build/{release-binding.json,receipt.json}`；自审：`dsh-visual-family-review-20261025/report-public.md`（Operator self-review，非 Codex 独立批准）。
- case `controlled-candidate135-visual-family-isolated-20261025-single1`；plan **`b36e707c5b4574f0aea82f75227ccbd33358bc356fd9173d3d2d6a69d0e38efa`**（canonical `plan.md` 已同步）；旧 plan `af633872…` 正常 revise/approve；gate `1315e24c…b744` 消费一次；window `11:51:50–12:11:50Z`、lease `13:28:32Z`（覆盖 window end +15min）。

## 修复内容

- `HeroArcherVisuals`：`forceRenderingOff` 读写全部改经 `AndroidUnityCompat.IsHidden/Hide/Show`（Android 隐藏写共享 `Renderer.enabled` + 读回确认）；`nativeHidden` Android 映射 `!enabled && !HidNative`；`CopyRendererLook` Android `own.enabled = HidNative || native.enabled`；PropertyBlock 复制显式跳过 + 首次 info；新增 Android-only relinquish（第三方把共享 enabled 改回可见时经既有 `RemoveByKey` 撤自有表现、交还原生，走 runtime 既有补挂载，不新增 retry）。PC 剥离 `#if ANDROID` 后仅 4 条等价替换。
- `HeroArcherCloth`：PC `MaterialPropertyBlock` tint 逐字保留；Android alpha 烘进已有 mesh vertex colors alpha（基准数组、仅 alpha 变化原地重提交、零逐帧分配、NaN fail-closed）；PC 可执行变化 0 条。
- `MusketeerVisuals` / `MusketeerGunVisuals`：Apply/ApplyNativePose/ApplyFrame/Release/CopyRendererLook 与 Gun CreateEntry/SetParked/Restore/Sync 全部迁移到同一 compat；`NativeHidden` helper；Gun visible 改由 `Owned`/`Parked` 凭据（PC 分支逐字原式）消除 parked/owned 每帧 Restore 抖动；PropertyBlock Android 显式跳过 + 首次 info；未新增 relinquish（保持与 PC 相同的第三方清隐藏行为窗口，作为已知语义差记录）。
- 两 Worker 均未修改共享 `AndroidUnityCompat.cs`、未新增 il2cpp 源文件；Operator 复跑 13 组 managed 套件（hero 161/164/41/49/38、musketeer 121/122、heavy-shield 1144/5、coin-courier 33、teleport 17/15、greek-bank 73）全部 exit 0。

## r3 单次 Native（同 r2 场景的启动/加载回归 + 视觉族触发）

- controller exit `0`、`state=evidence-observed`、`evidenceReportable=true`，`elapsed 239.2s`、observation 176.1s（time-budget）；候选归属 `b1c9b623…`、`controlValid=true`、init/ready/stop 各 1、`Playing/2`、island/campaign/game=8、无同船 NRE、同段 `[AndroidPoolLifecycle] native InitPools returned count=109`；baseline PNG 1。
- Root postflight（`33a75b78…ced8`）：`fullOriginal/sourceThree=true`、reap proven、`ownResourceClear=true`、root inputs 0/root PNGs 0、controller PNG 1、pairOk；postrun F 仅 READ（`35faea07…`，260337B）。

### 修复/回归证据（observed.log，同一段）

- 负向全部 0 次：`MissingIl2CppInternalCallException`、`sig=`、`attach held`、`effect group create failed`、`renderer copy failed`、`apply failed`、`cloth build failed`、`leisure atlas unavailable`、`register failed`、`tick failed`、`hide/show write unconfirmed`、`belongs to another owner`、`gun visibility write unconfirmed`、`relinquish`、`material or white texture unavailable`。
- 正向：
  - `[HeavyShieldVisual] android: per-instance MaterialPropertyBlock not copied` 1 次（04:53:37）——HeavyShield attach 视觉路径继续正常；
  - `[HeroArcherVisuals] android: per-instance MaterialPropertyBlock not copied` 1 次（04:53:37）+ `[HeroArcherVisualLife] attach actor=-83832 life=1 action=Run` / `attach actor=-79856 life=1 action=Walk`（attach 2 次、detach 0 次，观察窗内无 relinquish/抖动）+ `[HeroArcher] selected … visual=True`（r2 同场景为 visual=False）——HeroArcher body+cloth 在 Android 首次实际 attach 成功；
  - `[HeroArcherNative] … native=1 forceoff=1` 持续行——compat 的共享 enabled 隐藏位已实际生效；
  - `[MusketeerVisual] android: per-instance MaterialPropertyBlock not copied` 1 次（04:53:35）——Musketeer 视觉 Apply/CopyLook 实际执行；
  - `[CoinCourierTeleportFx] android mesh dashes ready count=16` 1 次（04:53:42）——上一批 mesh ribbon 修复继续正常。
- `[NorseSquad] load restore summary` 仍 `converted=0 shieldReady=0 pending=86 attempts=24`（与上一批一致；该计数不用于本批 attach 结论）。

## 边界（不得扩大）

- 本条只证明：同段启动/加载 + 三个视觉族的 attach/隐藏路径实际执行且无缺失 ICall、无写确认失败、无 relinquish 抖动；不证明画面观感（英雄 body/cloth 渲染、cloth alpha、受击 tint、双重 sprite、Gun park/unpark 贴图）、游戏 SetHideStatus 写频率、保存/读档/换岛/池复用、支付/玩法/手机/多人/长跑。
- 已知语义差（source review 与 worker 报告）：共享 `enabled` 与游戏写入不可区分；三特性 PropertyBlock tint 不复制；HeroArcher 有 Android-only relinquish（潜在抖动窗口）、Musketeer 无 relinquish（与 PC 同窗口）；Gun visible 以 Owned 为代理、`SetParked` 对“游戏 disabled renderer”可能抛 belongs-to-another-owner。
- Worker C 附带的 slice 外发现：`CharacterLeisureAtlas.TryLoad` 仍用 `Texture2D.GetPixels32`，若 Android 缺失会退化 leisure 外观（本批日志未出现 `leisure atlas unavailable`），另列后续小批次/实机确认。

## 下一步

1. 设计/执行 gameplay 场景批次（职业弓弩激活、火铳手招募射击、银行八助手/九补货、Rack 付费枪 park/unpark、换岛读档与池重建），把这些“已实现待验证”落到行为证据。
2. PropertyBlock tint 观感契约（受击闪白等）单列决策；避免在 compat 路径静默补齐。
3. 同族 `CharacterLeisureAtlas` 审计、HeroArcher/Musketeer 边界场景（塔/登船/过场/暂停）实机观察。
4. 画面/手机/多人/长跑仍为玩家实机验收；Issue #232/#262 保持 open，PR #263 保持 Draft，不合并/不关单。
