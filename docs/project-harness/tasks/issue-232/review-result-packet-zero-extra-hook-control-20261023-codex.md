# APPROVE_ZERO_EXTRA_HOOK_CONTROL_PACKET_DRAFT_ONLY

- case: `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`
- objective: `zero-extra-hook-load-control`
- verdict: `approved_packet_draft_with_window_refresh_instruction_correction`
- product/source mustFix: `[]`；窗口刷新时须采用下文 O1 更正后的操作说明。
- runtime/register/gate effective verified: `false`；本报告不让登记、gate、窗口或 Native 自动生效。

审查 HEAD `ac0b08eb956e951ed71716780990ed414fe1ac27`；请求 SHA256 `b3b1bf0245cce7901bc4fcd908a1896ae8bad8907da51edf4473b64fb582c2d8`。审前及写本报告前 worktree clean、HEAD 一致；本报告是唯一新增 Reviewer 文档。复用 R1–R4 源码批准及仍有效证据，不改现行 source 报告 `e6d1f80fd6a09d3121ff107126659022ff95f38f3109853d8b3650c1edeee624`，避免破坏本 packet 的报告绑定。

## 冻结 packet 核对通过

| 产物 | 实际 SHA256 |
|---|---|
| reviewed-source/manifest.json | `26dbd6f037413aae9b09214909379f9f192d83c500e765927e0235854f356cf4` |
| source-review.json | `c855d0f1a9e7cc8e09186a183ecf02d35f98ab536e49629f27a4b53562c03cf9` |
| runtime-plan-draft.json | `e7a1ddf278a07900082746cf33a9fdeab798e808d582c2eed37cc9f2df7b5f52` |
| runtime-gate.md | `382b305863e52c83d9c13bc64d0e09e66f1654526dd064675630eb2d02fd52a5` |
| native-window-approved.json | `52ddb691c27c52dbbdeca50e1b0f4caab5014deaf8009468a5d891b7ebced766` |
| packet-build-receipt.json | `3ff4c7e2e386a50ba5f6ca94035c9b7149ff3ebe96568ddf7ea9d8373ca8b79f` |

六产物 SHA/bytes 与请求一致。manifest 的 21 条源路径唯一，20 份 snapshot 均逐字节 hash 匹配源文件，快照路径均在本 case reviewed-source 内；唯一非复制项是封存 fixture F35fae，path/hash/bytes 与 plan 一致。manifest 集合精确等于 source-review pins + 四个 observerSourceBindings + fixture，未缺项或混入额外源。source-review 绑定现行 R4 报告、controller `c21d740f21d92a7d96fe4cd359e30c2b289ca52fc9a76e987096fdf2467ee99c` 与该 manifest；addon/root receipt/五引用列表一致。plan 的 gate/sourceReview/window SHA 分别匹配实际文件，taskIdentity 保持 DSH owner/session 与原 operation `a7459a58-3446-4856-aaa3-5f02e1115de2`，c1e43/16b/609e/F、保护集合与输入预算均匹配已审源码。

根 Reviewer 实际只读调用现有 `validate_plan_semantics`、`validate_source_review`、`verify_pin_entries`、`verify_plan_files`、`validate_native_window`，均通过；未导入/执行 builder/register/root-launch。未重跑未变化 host tests；R4 根 Reviewer 实际执行的 11/11 结果继续适用。builder 的 rootHostTests 字段本身是测试数量，不单独充当新的测试执行证据。

## O1：续窗操作说明必须更正，无需修改 builder 或重开源码审查

请求第3节所述“同一 builder 重新生成窗口”不能解释为直接重跑：当前 `build_packet.py:77–78` 对 reviewed-source 使用 `mkdir(exist_ok=False)`，该目录已存在，直接重跑会在更新窗口前拒绝。本项是续窗操作说明问题，当前冻结 packet 的结构/绑定有效；不需要新增工具、放宽 mkdir 或再次审查未变化源码。

若确需在**尚未登记、gate 未消费且没有执行开始**时续窗，沿既有机制处理：先保留本轮生成证据及 hash，移开本 case 自有的 generated reviewed-source 目录，再保持源码/四份脚本/输入不变，用已批准 builder 重新生成；核对新的 window/plan hash，并确认 manifest/source-review/gate/receipt 仍等于本报告冻结值。只有这些未变化值都保持时，才能称变化仅限 window 与 plan。新 window/plan 在登记前记录并绑定，不能复用旧 plan 登记回执。如已存在登记/消费/执行或效果未知，先按真实受管状态处理，不覆盖原证据或据此重投。此处是具体操作更正，不要求为了该说明再额外开启一轮方案/源码审批。

## 运行边界与未验

本 draft 为 UTC startsAt `2026-10-23T17:45:49.174164+00:00`、startupDeadline `17:47:49.174163+00:00`、endsAt `18:05:49.174164+00:00`。启动截止早于窗口结束；不能等到 endsAt 才认定窗口不适用。实际执行须满足既有 `startsAt <= now <= startupDeadline`、到 endsAt 至少余750秒、lease 覆盖 endsAt 后15分钟，且消费前/Popen 前原检查仍通过。窗口/资源身份变化不延长旧单次许可。

本轮只核验窗口结构，未将时段、lease 或资源空闲认定为当前运行许可；检查时 `normal/receipt.json`、`shared-window-approved.json` 与 spent gate 均不存在。后续真实 canonical owner/session/operation/phase/lease、精确 plan 登记、有效共享/Native 窗口及 gate 仍按既有受管流程落实；不得裸改 checklist/DB、放宽前置断言或把本草案批准当成这些条件已满足。不新增逐工具审批。

未执行登记、gate 消费、产品构建、ADB/AVD/游戏/Native、合并或安装。c1e43+609e 实际加载与产品 InitPools 返回未知；R2 的同段候选归属/609e controlValid/READY后正常STOP/无同船NRE + 产品 InitPools 日志判定不变，采集 exit0/evidenceReportable 不代表加载、保存、支付或玩法验收。

Reviewer：`mac-ohmymods-codex-desktop`；根 Reviewer 核对全部实际产物与只读验证，另有一个内置只读 subagent 检查闭包、窗口和 O1 操作差异。O1 采用本报告的具体操作修正，当前 packet 草案无需新增源码修改。
