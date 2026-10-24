# APPROVE_ICALL_SIGNATURE_SOURCE_ONLY

- provenance: OPERATOR SELF-REVIEW (DSH Android Operator `mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`). 用户 2026-10-24 授权 Operator 全权推进、无需等待 Codex；本报告是 Operator 对本次诊断工具与产品日志改动的自审，不是 Codex 独立批准。
- case: `controlled-candidate133-icall-signature-isolated-20261024-single1`
- objective: `icall-signature-probe`
- verdict: `approved_exact_icall_signature_source_only`
- sourceSHA256: `5103f11872ab94656e13615d727340db4083380bb6a7e328ccf1ca6d99548bd6`
- mustFix: `[]`
- unverified: packet rebuild/registration/window/lease, preflight/VM/Native, actual runtime ICall Signature values, producer fixes, gameplay/save/payment/phone/multiplayer/long-running behavior.

范围：
1. 产品源码（commit `60fa5ec`）仅在两个既有 catch 的 `#if ANDROID` 分支追加 `MissingIl2CppInternalCallException.Signature` 与 `Message` 记录；PC 分支原日志文本逐字不变，无新增 catch/重试/兜底、无异常边界改动。
2. 诊断工具由已执行的 zero-extra-hook case 最小派生：case/objective/gate/review verdict、候选 Main（新 Release DLL `aac037c0c06f81b5609be686f968196dd619903b94fc22bf6e52be11e140c54b`）与非目标文案更新；cfg16b、609e addon、F35fae、AVD/ADB RESOURCE_BINDING、HOST_PROTECTION（normalDLL `055368a3…`、checklist `efb52c89…`）不变；base/helper 原文件与 hash 不变。
3. 构建：Android net6 Release，`BepInExPluginsPath=` 禁部署；207 Compile/176 ReferencePath/25 EmbeddedResource 全部构建前后 hash 保持，产物 2,276,352 B；构建回执 `icall-signature-20261024/root-build/{release-binding.json,receipt.json}`。
4. host tests 11/11 通过（候选 pin/资源绑定/保护 pin/终端事实），py_compile 通过。

边界：本报告只批准 source/工具与诊断日志改动；不批准 Native 运行结果、不宣布 ICall 根因或修复。运行仍走单次 gate/window/lease、自有 AVD/overlay，采集 exit0/evidenceReportable 不等于加载成功；跑后按 R2 同段规则与原始日志判定。
