# APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY

- provenance: OPERATOR SELF-REVIEW (DSH Android Operator `mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`). 用户于 2026-10-24 明确要求 Operator 自主决策、无需等待 Codex；本报告是 Operator 对 R5 增量的自审，不是 Codex/独立 Reviewer 的批准。Codex 历史 R4 批准报告原样保留于 `report-public-codex-r4-20261023.md`，仅覆盖其当时所见 controller c21d740f 范围。
- case: `controlled-candidate133-zero-extra-hook-control-isolated-20261023-single1`
- objective: `zero-extra-hook-load-control`
- verdict: `approved_exact_zero_extra_hook_control_source_only`
- sourceSHA256: `e10a2d547c16f6fb5b8cd8e0ce5bb5ed5f8218ef4c5c29236e2d999e7f58c70e`
- mustFix: `[]`
- unverified: packet rebuild, renewal registration, window/lease, preflight/VM/Native, product InitPools return, gameplay/save/payment/phone/multiplayer/long-running behavior.

R5 自审范围（仅增量）：
1. `zero-extra-hook-control-controller/controller.py` 的 `HOST_PROTECTION` normalDLL pin 更新为当前安装的正常包 `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf`（9,100,288 B）；当前正常包安装清单 `package-manifest.json` SHA256 `22cdc4d8ef247168706c52a2cdf1389b714089ff88aa3abbdf01afef6899e303`、`SHA256SUMS` SHA256 `78b6ce677d5b6de42121578f82a0ff74525b9a21deac19cdb177cb9f0f88c312` 均记录该 hash；localChecklist pin `efb52c89…` 未变。
2. `register.py` 不再硬编码旧 plan SHA，改为必填 `--old-plan-sha`（用于已登记 plan `a8e4b771…` 上的续窗/重登记）；其余前置、上传、plan revise/approve、readback、validator 断言语义不变；`BASE` 更新为代码基线 `59ae6a0`。
3. 历史 base/helper 原文件与 hash 不变，未放宽守卫、未自动刷新/重试；RESOURCE_BINDING（ADB 85922、AVD device 16777234 与 9 路径 inode）复检未变，本增量不改。

验证（Operator 实际执行）：派生 host tests 11/11 通过；7 个派生 .py py_compile 通过；只读 plan dry-run 的 `validate_plan_semantics` + `verify_plan_files` 全部通过（新 normalDLL pin 与当前安装包匹配）；用户授予完全访问后 `ps -p 85922 -o uid=,lstart=,args=` 实际退出 0，上一轮 sandbox ps 阻塞已解除。未运行 builder/register/gate/VM/Native，未改产品源码。

边界：`evidenceReportable`/exit0 只表示采集完备，不等于加载成功；限定结论仍须同一可信 fresh segment 的正确候选归属 + 609e INIT/controlValid + 真实 READY 后正常 STOP + 无同船 NRE，并联合产品 `[AndroidPoolLifecycle] native InitPools returned` 日志；缺失/UNKNOWN/截断/漂移不补绿。
