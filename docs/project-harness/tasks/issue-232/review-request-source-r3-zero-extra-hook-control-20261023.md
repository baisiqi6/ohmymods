# Review Request — R3 incremental: real resource identity for zero-extra-hook control case

- 日期：2026-10-23（R3 增量）
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 上一 source 审查：HEAD `7ee48fd`、请求 SHA256 `44b6d252…86d2f1`、diff SHA256 `e1ae2460…8676e25`；报告 `resume-20261019/zero-extra-hook-control-review/report-public.md` SHA256 `1ac21757d0811b8eefa29f6de9b4523c9c06bd39b0deb3e478105b94899389ba`，结论 CHANGES_REQUESTED / mustFix R3。
- 本轮只修 R3 并冻结受影响 hash；R1/R2 已接受内容与其余 5 个派生文件字节不变。本请求不请求 Native/构建/合并/gate。

## R3 修复

在派生入口 `zero-extra-hook-control-controller/controller.py` 新增本 case 的 `RESOURCE_BINDING`，经既有 `_OVERRIDES` 回绑逻辑同时设置到 `_L` 与 `_F`，使既有 `_read_resource_binding/_check_own_resources/_check_adb` 精确核验使用本 case 身份；历史 `profession-fixture-load-controller/controller.py` 与 `loader-control-controller/controller.py` 原文件与 hash 不变，未复制 helper、未放宽守卫、未加入自动重绑/扫描/重试，未重启 ADB。

冻结值（2026-10-23 只读现场）：

- AVD 目录/ini/config 及 6 images 的 `device=16777234`；inode 分别为 avdDir 57994989、ini 57994990、config 57994991、userdata 58007451、userdata.qcow2 58007475、cache 58007453、cache.qcow2 58007474、encryptionkey 58007452、encryptionkey.qcow2 58007476；config SHA256 `bc8eb0a037eb504566de6f3377228f10462cb1b983ecbcc9c313b6097d6acfc8`。
- ADB：PID 85922、UID 502、lstart `Fri Oct 23 09:12:11 2026`、args `adb -L tcp:5037 fork-server server --reply-fd 4`（`uidStartArgs` 字段，沿用 base 的 `_parse_uid_identity` 格式）。

## 受影响 hash（其余不变）

| 文件 | SHA256 |
|---|---|
| `zero-extra-hook-control-controller/controller.py` | `144006ab030540a04db990c859097381fe42004db7cee0b1488f6d941e5ed7e5` |
| `zero-extra-hook-control-controller/test_controller.py` | `0db1e4c526b14087f41b326296a9d00624156a26cb98aec0473e59da84742479` |
| `next-zero-extra-hook-control-case-20261023/source-diff.patch` | `5ae81e6ff0089d8a5ef3c36aac24826a815cf96d0a14f7f32420e37caea06d56`（23520 B） |

未变：register.py `d405af3c…`、build_packet.py `185fc848…`、root-launch.py `e666aa4d…`、root-postflight.py `fda86837…`、root-normal-readback.py `9e161287…`；候选/cfg/F/addon 与 hash 闭包均未变。

## 验证

- 派生 `test_controller.py` 新增 `test_effective_resource_binding_and_drift_refusal`：确认 `_F.RESOURCE_BINDING` 与 case 常量一致、实际 `_read_resource_binding()` 对真实路径 stat 匹配、对模拟 `device` 漂移仍抛错拒绝；**11/11 host tests 通过**。
- 7 个派生 .py py_compile 通过。
- `build_packet.py` 仍 fail-closed（报告首行非 `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`，exit 1，未生成 packet）。
- 未做：builder/packet/register/gate/构建/ADB/Native/合并；产品源码零改动。

## 请求

请只增量复审 R3 派生绑定与上表受影响 hash；R1/R2 及其余冻结内容复用。若批准，请更新报告首行为 `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY` 并保留 case/objective/verdict/sourceSHA256/mustFix/unverified 字段；随后我运行 builder 生成 packet 并提交下一阶段审查。
