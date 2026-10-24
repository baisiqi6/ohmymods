# 原生保存 → 读档两运行往返（r9 save + r12 load）— 2026-10-25

- 目的：#232/#262“原生保存/读档”必要运行的首个**两运行**往返证据（保存写入 → 同一存档+身份 sidecar 作为输入 → 正常启动读档并回读）。明确不是同进程 `Game.Reload()` 证据。
- 全部为单次 gate/window/lease 的原生案例，零 Root 输入；own-AVD `-read-only` overlay；候选 Main `6b14879dee633f67a3e1853058579e1c7143310986bc866f7a4b040298a532b3`；609e NativeSceneControlProbe（zero-Harmony）；cfg restock7 `feefad79…`（58 keys，保留理由：身份/银行账本的 load/restore 门依赖同一 feature cfg）。

## Run S（保存）：r9 save-watch2

- plan **`9ffb0dba1ea7c1146f344527c88c132e88c0793e95282249f956a7ed98d18559`**；gate 消费一次；observer STOP 后 game 继续运行、有界 save-watch 600s。
- `saveWatch=completed/observed/reason=file-changed/watchSeconds=134.6/sampleMode=stat+sha`；`Release/global-v35` 由 fixture `35faea07…`（260,337 B）→ **`e6e6e81d7564cc722c57c5bd6862e1c850c0b948b265a2ea1def79958c61cd61`（263,366 B）**；stop 前捕获该文件 + `ModSave/{hero-identities.v1.json(9d90a109…), hero-identities.v1.json.bak(352a864c…), musketeer-identities.v2.json(7e53e045…), musketeer-pair-global-v35.json(e7583f6c…)}`（inventory `d96b9750…`）。
- postflight `saveClassification=observed-captured`；`MissingIl2CppInternalCallException` 0；零 Root/PNG；资源交还通过。

## Run L（读档）：r12 save-load3

- 输入冻结：r9 捕获的 `global-v35`（`e6e6e81d…`/263,366，设备原件 `7ba5f975…`/82,978）作为 `nativeFixture`；4 个 ModSave sidecar 作为 `modArchiveSeed`（安装到 `files/MelonLoader/UserData/KingdomEnhancedMod/ModSave/`，目标必须不存在/等于 seed/等于冻结 expectedBaseline，否则 abort；本 run hero v1 记录 `replaced-expected-baseline` 352a864c→9d90a109，其余 3 个 pushed，全部 readback 一致）。
- plan **`89a36c64ecad20726abd5fdb4c40084d078cd5f9c0f7591a7dbedaf6369b0c43`**；gate 消费一次；window 30min；零 Root 输入；正常 boot→READY→观察 120s→STOP；cleanup 证明走 `qemuExitProvenByReap=true`（deep 修复：zombie/已退出 PID 分类 + reaper 后进程缺席/端口空闲/AVD 目录无 holder 全验，`closeReason=reap-cleanup-verified`）。
- 结果：controller exit0、`state=evidence-observed`、`evidenceReportable=true`、`controlValid=true`；world READY（island8/castle/walls7）；`loadVerdict=loaded`、`loadReason=load-evidence-present`；首 READY / 观察末 / stop 三处设备 `global-v35` sha 均 = 输入 **`e6e6e81d…`**、`autosaveDuringWindow=false`、零 `loadMissingIcallLines`；`loadMarkers` 含 `[HeroRecruitment] loaded:paid:ctx=12b7195d:epoch=a16de6cc:hk=2:v1=False:seats=2:bound=2`、`[MusketeerSync] staged:05cfd649:4bd85972:0c15e368`、`[Musketeer] loaded:exact:records=12:bound=10:unbound=0:pending=0:stock=0:conflict=False`，与冻结 seed token 匹配（`loadIdentityMatched=true`）；postflight `loadClassification=loaded-identity-matched`、`cleanupUnknown=false`；零 MissingICall、零 Root/PNG、资源交还通过。
- 同段其它：再次取得农夫/投石车油桶两次真实扣款（与 r6/r7/r9 一致）。

## 结论与边界

- **保存写入 → 读档加载两运行往返成立**：r9 写出的新存档文件与 4 个 mod 身份 sidecar 在 r12 被字节级导入，游戏正常 boot 后读档保持同一 `global-v35`（首 READY/观察末/stop 三次 hash 一致，无窗口内重写），mod load 日志回读了保存时的英雄/火枪手身份 token（HeroRecruitment ctx/epoch、Musketeer loaded records=12/bound=10），且保存文件/seed 的输入=回读 SHA 一致。
- **不证明**：同进程 `Game.Reload()` 到达/时序；换岛/池重建；银行账本**内容级**回读（native prefs key 内容需后续 probe v2，当前只证 native save 文件往返 + mod 身份 sidecar 回读）；付费/角色/画面/手机/多人/长跑验收。
- 工具迭代如实保留：r10（seed 目标已存在 → prepare abort，未 launch）、r11（load 事实成功但 cleanup zombie 误判 + postflight 缺 `import re` → evidenceReportable=false）；r12 修复后通过。旧 gate 均已消费，不重放。

## 证据哈希

- r9 save：result `adddbc90…19c97c`、observed `9f2bfede…e0add19b`、postflight `b4dfdda9…23ac744`、spent gate `5df10273…486ad19`、captured global-v35 `e6e6e81d…c61cd61`、inventory `d96b9750…40356572`。
- r12 load：result `4ccbd554…732c81a`、observed `c2099b10…5253f44`、postflight `2231162f…b43ad5`、spent gate `546810e9…10eb2a`、canonical readback `b81da11b…494582`。
- 输入 manifest v2 `c978dc03…`；计划链 r9 `9ffb0dba…` → r10 `b80f5893…`（tool abort）→ r11 `b119b26b…`（load facts/cleanup 误判）→ r12 `89a36c64…`（clean pass）。
