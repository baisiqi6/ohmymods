# 原生保存写入单次验证（save-watch r9）— 2026-10-25

- 目的：为 #232/#262 的“原生保存/读档”必要运行取得**保存真的写盘**的 Android 实机证据；读档回读是后续 load-run（独立窗口），本 run 不宣称读档/同进程 reload。
- 组合：候选 Main `6b14879dee633f67a3e1853058579e1c7143310986bc866f7a4b040298a532b3`；cfg restock7 `feefad79…`（58 keys）；609e NativeSceneControlProbe（zero-Harmony）；fixture F35fae `35faea07…`（260,337 B）；own-AVD `-read-only` overlay；**零 Root 输入/零 Root PNG**；单次 am start；observer STOP 后游戏继续运行、有界 save-watch。
- 链：Worker J（teammate，DeepSeek flash）host-only 派生 controller/packet/review（r8 工具故障 → r9 修复）；Operator 审查/登记/执行。

## 工具故障与修复（如实保留）

- **r8**（plan `9c847ad2…`，gate 已消费一次）：save-watch baseline 后 0.22s `state=aborted/reason=not-run`，`error=ADB command failed/unknown`；无 capture；观察/资源链正常（world READY、probe STOP、fixture 未变、qemu/端口释放）。根因：`case.shell()` 对 argv `shlex.join` 把 `&&` 变成字面量 → 设备执行 `stat … '&&' sha256sum …` exit 1；`common.Case.adb` 默认 `check=True` 在 fallback 前抛错；fallback 未 latch `statOnly` → watch 循环首轮再探 sha 再抛 → abort。
- **r9**（plan **`9ffb0dba1ea7c1146f344527c88c132e88c0793e95282249f956a7ed98d18559`**；旧 plan `9c847ad2…` 正常 revise/approve；gate `5df10273…` 消费一次；window 45min；零 Root）：探针改为一条正确引用 `adb shell -T "stat -c '%s %Y' <f> && sha256sum <f>"` + `check=False`；sha 不可用 → 永久 stat-only latch + 同样本回退；watch 单次 stat/sha 失败 → bounded inconclusive（不 abort、不重试）。host tests 21/21 绿。
- r9 登记前还发现并修正派生链的 old-plan 常量笔误（`9c847ad2d288…` 66 字符 → 真实 canonical `9c847ad288…` 64 字符），修正后 packet/review/pins 重建；两次尝试均在 gate 写入 canonical 前被本地断言拦截，canonical 未被错误计划污染。

## r9 运行事实（保存写入）

| 项 | 值 |
|---|---|
| controller/state | exit0；`state=evidence-observed`；`evidenceReportable=true`；`controlValid=true`（mainAssemblyPresent=true;loader=melon;hash=candidate）；elapsed 360.4s |
| world | READY：`islandLand=8 campaignLand=8 gameLand=8 castleBuilt=true wallsCount=7`；probe STOP `reason=time-budget` |
| save-watch | `state=completed verdict=observed reason=file-changed watchSeconds=134.646 sampleMode=stat+sha`；12 samples 全部 `stat+sha`；无 transport 错误 |
| 设备文件变化 | `Release/global-v35`：before fixture **`35faea07…`**（260,337 B）→ after **`e6e6e81d7564cc722c57c5bd6862e1c850c0b948b265a2ea1def79958c61cd61`**（263,366 B，mtime 更新）；`changedFromFixture=true` |
| stop 前捕获 | `actual/save-capture/`：`Release/global-v35`（SHA 与 inventory 一致）+ `ModSave/hero-identities.v1.json`（`9d90a109…`）、`hero-identities.v1.json.bak`（`352a864c…`）、`musketeer-identities.v2.json`（`7e53e045…`）、`musketeer-pair-global-v35.json`（`e7583f6c…`）；inventory `d96b9750…` |
| 同段其它 | 再次取得 `[AutoRestock] 农夫 补货成功 price=8 nativePrice=4 coins=8`、`[AutoRestock] 投石车油桶 补货成功 price=10 nativePrice=5 coins=10`；`[MusketeerShop] ready x=20.21 price=4`；`musketeerAutoPurchaseCreated=false`（火枪手自动采购仍 inconclusive，与 r7 相同） |
| 负向/资源 | `MissingIl2CppInternalCallException`=0、`sig=`=0；零 Root 输入/PNG；postflight `fullOriginal=true`、`ownResourceClear=true`、`reapProven=true`、controller PNG 1、`saveClassification=observed-captured` |

## 结论与边界

- **首次取得 Android 2.4 原生保存写入证据**：在零用户输入下，游戏在 observer STOP 后约 135s 内（首个世界约 2.5min）写出新的 `global-v35`（字节变化、尺寸/mtime 变化、hash 实读），同一次保存中产品的 `MusketeerPersistence/HeroNativeRights` 等保存钩子写出了 4 个 ModSave 身份 sidecar；整个写入过程在 owned stop 之前完成捕获，读回 SHA 与 inventory 一致，资源/原盘交还通过。
- 这证明的是**保存写入路径与保存钩子执行**（原生 `IslandSaveData.Save`/`SerializeObjects` 链 + mod capture/sidecar），不是读档回读、不是同进程 reload、不是池重建、不是玩家/画面/手机/多人验收。
- 去重门：原生 SaveAsync 在状态未变时可能返回成功而不写盘；本次观察到真实字节变化，dedupe 不适用（`dedupePossible=false`）。
- 下一步：load-run 用该 captured `global-v35`+`ModSave/*` 作为输入，验证读档加载同一身份/账本；之后再补池重建/换岛/手机等边界。

## 证据哈希

- result `adddbc90…19c97c`；observed.log `9f2bfede…e0add19b`；postflight `b4dfdda9…23ac744`；spent gate `5df10273…486ad19`；save-capture inventory `d96b9750…40356572`；captured global-v35 `e6e6e81d…c61cd61`；canonical readback `dac09adc…243d170`。
- r8 失败证据（只读保留）：`next-save-watch-case-20261025-r8/actual/result.json`、raw trace `00672/00676.json`；r8 plan `9c847ad2…` 已正常被 r9 revise 覆盖，gate 已消费不重放。
