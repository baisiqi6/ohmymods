# Review Request — Packet / reviewed-source / gate-window for zero-extra-hook control case

- 日期：2026-10-23
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 工作树 HEAD：`2249dbfb2dfaa1af85364526687b2d7b313d0997`；分支 `dsh/android-economy-262-20261023`；PR #263 OPEN/Draft
- Source 批准：`APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`，报告 SHA256 `e6d1f80fd6a09d3121ff107126659022ff95f38f3109853d8b3650c1edeee624`；controller source SHA `c21d740f…7ee99c`。
- 本请求只审查 packet/reviewed-source/source-review/gate-window 草案；不请求 Native/产品构建/合并/register/gate 生效。

## 1. Builder 结果与冻结产物

`build_packet.py` 运行成功（exit 0），rootHostTests=11；输出目录 `/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/packet`。

| 产物 | SHA256 | Bytes |
|---|---|---|
| `reviewed-source/manifest.json` | `26dbd6f037413aae9b09214909379f9f192d83c500e765927e0235854f356cf4` | 10524 |
| `source-review.json` | `c855d0f1a9e7cc8e09186a183ecf02d35f98ab536e49629f27a4b53562c03cf9` | 14654 |
| `runtime-plan-draft.json` | `e7a1ddf278a07900082746cf33a9fdeab798e808d582c2eed37cc9f2df7b5f52` | 7782 |
| `runtime-gate.md` | `382b305863e52c83d9c13bc64d0e09e66f1654526dd064675630eb2d02fd52a5` | 1208 |
| `native-window-approved.json` | `52ddb691c27c52dbbdeca50e1b0f4caab5014deaf8009468a5d891b7ebced766` | 668 |
| `packet-build-receipt.json` | `3ff4c7e2e386a50ba5f6ca94035c9b7149ff3ebe96568ddf7ea9d8373ca8b79f` | 725 |

`reviewed-source/` 共 21 条 manifest 记录：20 个引用/快照文件 + 1 个仅按 path/hash 引用的 sealed fixture（`global-v35.day32.original.gz`，`35faea07…`，copied=false）；manifest 内每条 snapshot 与源 hash 已由 builder 断言一致。

## 2. 绑定核对（已交叉验证）

- 候选：Main `c1e43d48…25d9445c6`、cfg `16b68641…d93d324c`、addon(0.0.1) `609efed2…932b74f8`、fixture `35faea07…4580fb5`；original Main `5460efd9…` 不变。
- `runtime-plan-draft.json` 的 `gateSHA256`/`sourceReviewSHA256`/`resourceWindowSHA256` 分别等于 `runtime-gate.md`/`source-review.json`/`native-window-approved.json` 的实际 SHA256（已独立重算）。
- `source-review.json` 的 `independentReviewSHA256` 等于现行批准报告 SHA `e6d1f80f…`，`sourceSHA256` 等于新 controller `c21d740f…`，`sourceManifestSHA256` 等于 manifest `26dbd6f0…`，rootInputs=0/PNGs=0/controllerBaselinePNGs=1/controllerOwnInputs=0。
- `taskIdentity`：issue=232、owner=`mac-dsh-ohmymods-android-operator`、session=`dsh-android-profession-archery-20261023`、operation=`a7459a58-3446-4856-aaa3-5f02e1115de2`（保留原 task operation）。
- hostProtection：normalDLL `25d9ffef…f4c156`、localChecklist `efb52c89…`；hostDisks 6 个 AVD 镜像路径；gate 不含旧 2e55 文案，candidate 为 c1e43。

## 3. 运行窗（draft）

builder 按生成时点写出的 draft window：`windowId=android232-candidate-control-20261023-1745`，startsAt=`2026-10-23T17:45:49.174164+00:00`，startupDeadline=`2026-10-23T17:47:49.174163+00:00`，endsAt=`2026-10-23T18:05:49.174164+00:00`，maximumCases=1，overlayOnly=True。

若本阶段审查/受管登记超过该窗口，执行前会用同一 builder 重新生成有效窗口；届时仅 `native-window-approved.json` 时间、`runtime-plan-draft.json` 的 `resourceWindowSHA256` 与 plan 自身 SHA 变化，gate/source-review/reviewed-source/controller 字节不变。**不会**用过期窗口执行；重新生成后会先报新的 window/plan hash 再登记。

## 4. 已做/未做

- 已做：builder exit0、11/11 host tests、全部 pin 校验；manifest/source-review/plan/gate/window 交叉 hash 验证；无 register/gate/构建/ADB/Native。
- 未做：Coordinate plan revise/approve、受管 gate/window 生效、preflight/VM/Native、产品构建/安装/合并；产品源码零改动。
- 判定边界：`evidenceReportable`/exit0 仅表示采集完备，不等于加载成功；限定结论仍须同一可信 fresh segment 的候选归属/609e controlValid/真实 READY 正常 STOP/无同船 NRE + 同段产品 InitPools returned 日志；UNKNOWN/缺失/截断/漂移不补绿。

## 5. 请求审查范围与下一步

请审查：packet 产物结构、reviewed-source manifest/snapshot 闭包、source-review.json 绑定、runtime-plan-draft 与 gate/window 绑定、draft window 处理方式；是否有必须修正项。

如批准，请把结果写入同任务目录（建议 `review-result-packet-zero-extra-hook-control-20261023-codex.md`）并给出批准/修改条目；随后我按现有 Coordinate lifecycle 走 issue-232 plan revise/approve 与单次 gate、有效窗口，最后执行唯一一次 Native。

## 6. 未验/未知

受管 plan/phase/lease/登记、有效窗口与 gate、运行 preflight/VM 启动、c1e43+609e 实际加载、产品 InitPools returned、玩法/支付/存档/手机/多人/长期均未验。
