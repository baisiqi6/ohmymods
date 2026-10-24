# Zero-extra-hook control native run — 2026-10-24

## 范围与授权

- Case: `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`；objective `zero-extra-hook-load-control`。
- 候选：Main `c1e43d48…25d9445c6`、cfg `16b68641…d93d324c`、0.0.1 zero-Harmony addon `609efed2…932b74f8`、fixture `35faea07…4580fb5`。
- 审查链：R1–R4 与 packet 草案由 Codex 增量审查；用户 2026-10-24 指示 Operator 自主决策后，R5（正常包 protection pin `055368a3…` + renewal 用 `--old-plan-sha`）与 packet 重组为 DSH Operator 自审，Codex R5 独立审查未取得（旧 Codex R4 报告原样保留，不覆盖其结论）。
- 运行门：单次 gate `84f77370…a53d9`（消费一次）；window `2026-10-24T06:29:04–06:49:04Z`，启动时剩余 1149s；lease `08:24:19Z`；run 前重核 ADB 85922/UID502/lstart、AVD device 16777234/inode、端口与保护文件；full-access 下 `ps` 正常。

## 运行事实

| 项 | 值 |
|---|---|
| controller exit / evidenceReportable | `0` / `true`；state `evidence-observed` |
| utcStart→utcEnd | `2026-10-24T06:29:19Z` → `06:33:15Z`（elapsed 249.3s） |
| candidate 归属 | Melon 加载 `c1e43…`；`mainHashState=candidate`；`controlValid=true` |
| 609e INIT/READY/STOP | INIT 1 条、STATE 2、READY 1、STOP 1、UNKNOWN 0；`observerStopSeen=true` |
| world | `Playing/2`、`playable=true`、island/campaign/game=`8`、`land8NormalLoad=true`，无同船 NRE |
| 产品 InitPools | 同段 `[AndroidPoolLifecycle] native InitPools returned pm=0x6F159FCCC0 count=109 active=True` |
| 其它观察 | `ANDROID_PROFESSION_COMBAT_REGISTERED owners=133 sharedSource=true`；BankAssistants 解析 8 控制器并 spawn deterministic 8-assistant pool，首 assignment/submission accepted=1；Crossbowman 注册 KEM_CrossbowBolt syncID=31000 |
| 已知警告 | `[HeavyShieldRuntime] attach held: MissingIl2CppInternalCallException`、`[CoinCourierTeleportFx] effect group create failed: MissingIl2CppInternalCallException`（运行 Signature 仍未知，未修复） |
| 输入/PNG | root inputs 0、root PNGs 0、controller own inputs 0、baseline PNG 1（1280×720） |
| postrun F | READ 260337B/SHA `35faea07…`（READ not SAVE）；无 Save/Pay/RPC |
| 清理 | QEMU exit proven、reap proven、cleanupUnknown=false；未用 restore 层；无 emulator/qemu 残留，5582/5583 空闲，ADB 85922 仍在 |

## 限定结论（R2 同段规则）

同一可信 fresh segment 满足：候选归属正确、609e `controlValid=true`、真实 READY 后正常 STOP、无同船 NRE，并出现同段产品 `native InitPools returned`。因此可限定判断：**本候选在本场景启动期未复现旧的同船 NRE，且原生 InitPools 自然返回 count=109**。

明确不证明：九自定义池正确/完整注册（本 case 无 pool-phase 观察）、Native 兼容或原始异常传播、银行/职业完整玩法、支付/保存/读档/换岛/池重建、手机/平板/多人/长期；不能以 READY/exit0/evidenceReportable 替代上述验收。旧 0.0.6 false/unknown 与 N9/N11 失败记录不重写、不套用到本例。

## 证据（本机）

| 证据 | 路径 / SHA256 |
|---|---|
| result | `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/actual/result.json` / `1b23f0931aafe2b0818e7a77acdf99f96213378946bb9f53224eb9374fbadd2c` |
| fresh/observed log | `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/actual/fresh-segment.log` / `7b08b2fcb54c6540129adbb373b14c9f66ed17ded68fb6cc0eb2f786e90bee27` |
| root postflight | `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/actual/root-independent-postflight.json` / `519ca38020bdbce730cd693ee74dd55d2805449f948a554161d0ae6f8dc11d14` |
| canonical readback | `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet/normal/root-independent-readback.json` / `ecd734fcd33ebc8225f61d43da5ecafee57a0b7855bad0d3ebf7d11300805667` |
| spent gate | `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet/runtime-gate.md.spent-controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1` / `84f773707e8b5d6a27e05c553a86bcaec7f3dfd59ea61e81d11aa8af449a53d9` |
| baseline PNG | `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/actual/loader-control-baseline.png` / `22a67a2c343bee5f7e5899f701a85b3875649e0f7e4466712ffdc1539dfd1fd0` |

Repo 记录：plan 更新为 `0f6e69a5…`（本 run 实际执行计划）；R5 Operator 自审报告副本见同目录 `review-result-source-r5-operator-self-review-20261024.md`（`80d440af…`）。
