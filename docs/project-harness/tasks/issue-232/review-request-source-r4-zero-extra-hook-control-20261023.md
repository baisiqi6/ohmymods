# Review Request — R4 incremental: current protected normal DLL pin for zero-extra-hook control case

- 日期：2026-10-23（R4 增量）
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 上一 source 批准：`APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`，报告 `resume-20261019/zero-extra-hook-control-review/report-public.md` SHA256 `a870f87709ccf663b7f00e2784a8efa4cb454b42755222c11c46c38c41743563`；审 HEAD `717716d`、请求 SHA `09cb7063…2bc2`。
- 本轮只修 packet 生成阶段暴露的 host protection 陈旧 pin，并冻结受影响 hash；R1/R2/R3 已接受内容与其余 5 个派生文件字节不变。本请求不请求 Native/构建/合并/gate。

## 发现（builder 尝试时）

在 HEAD `717716d` 运行 `build_packet.py` 时，生成到 `verify_plan_files(plan)` 被拒绝：

`RuntimeError: host protection pin mismatch: normalDLL`

失败日志：`/Users/Admin/projects/ohmymods/.local/tasks/android-profession-archery-20261014/g19-s1-runtime-preparation-20261018/next-zero-extra-hook-control-case-20261023/builder-refusal-hostprotection-20261023.log`，SHA256 `4daebf0d0817c2754c6c876628dec1e17ae685324b39de1014fd1bff71c4152e`；未保留被拒的 packet 产物（部分目录已清理，四份 packet 脚本未动）。

根因：派生 controller 继承的基类 `HOST_PROTECTION` 把 normalDLL 冻结为旧 `8345f086…`（2026-10-18 正常包字节），当前受保护 normal DLL 实际为 `25d9ffef5ad20436bf49c8f7f7c7fdb395e16a86f2bae63900ee6df0a8f4c156`（与 2026-10-21 环境快照 priorFullSHA 一致）。localChecklist pin `efb52c89…` 仍匹配。

## 修复

在派生入口 `zero-extra-hook-control-controller/controller.py` 覆盖 `HOST_PROTECTION`（并定义 `NORMAL_DLL_SHA256`），冻结当前正常包 DLL `25d9ffef…` 与不变的本机 checklist `efb52c89…`；历史 base/helper 原文件与 hash 不变，未复制 helper、未放宽守卫、未加入自动刷新/重试；实际字节漂移仍按原 `verify_plan_files`/`_read_resource_binding` 拒绝。

## 受影响 hash（其余不变）

| 文件 | SHA256 |
|---|---|
| `zero-extra-hook-control-controller/controller.py` | `c21d740f21d92a7d96fe4cd359e30c2b289ca52fc9a76e987096fdf2467ee99c` |
| `zero-extra-hook-control-controller/test_controller.py` | `b96f874c250db10d669944d61214f48e983ec1c6c9b25beabf2181f2f5f31b60` |
| `next-zero-extra-hook-control-case-20261023/source-diff.patch` | `474931502d4647a3a1ae6539a79673a2fd689dd3b421b62f2385539f0ba08d4d`（24532 B） |

未变：register.py `d405af3c…`、build_packet.py `185fc848…`、root-launch.py `e666aa4d…`、root-postflight.py `fda86837…`、root-normal-readback.py `9e161287…`；候选/cfg/F/addon 与 hash 闭包均未变。

## 验证

- `test_effective_resource_binding_and_drift_refusal` 扩展为同时核对 `_F.HOST_PROTECTION` 生效与实际文件 SHA；**11/11 host tests 通过**；7 个派生 .py py_compile 通过。
- 只读 plan dry-run：用当前派生常量构造 plan，`validate_plan_semantics` 与 `verify_plan_files` 均通过（addon/fixture/RuntimeRefs/hostProtection/hostDisks 全部 pin 匹配）；未运行 builder、未生成 packet 产物。
- 未做：packet/reviewed-source/source-review.json/register/gate/构建/ADB/Native/合并；产品源码零改动。

## 请求

请只增量复审 R4 的 host protection 当前 pin 与上表受影响 hash；R1/R2/R3 及未变证据复用。若批准，请更新报告首行 `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`、`sourceSHA256` 指向新 controller 并保留 case/objective/verdict/mustFix/unverified；随后我重新运行 builder 生成 packet 并提交下一阶段审查。
