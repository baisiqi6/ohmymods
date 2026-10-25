# Review Request — R5 incremental: full-access ps 恢复后的环境漂移与续窗登记能力

- 日期：2026-10-24（R5 增量）
- 发起：DSH Android Operator（`mac-dsh-ohmymods-android-operator` / session `dsh-android-profession-archery-20261023`）
- 审查：Codex 原会话（增量 Reviewer）
- 背景：用户已把本会话设为完全访问；实测 `ps -p 85922 -o uid=,lstart=,args=` 退出 0，**上一轮 `Operation not permitted: ps` 阻塞已解除**。
- 当前工作树 HEAD `59ae6a0`；上一 packet 批准报告 SHA `0787c9d7…f062`；source 批准报告 SHA `e6d1f80f…e624` 绑定旧 controller `c21d740f…`。
- 单次窗口已过期（endsAt `2026-10-23T18:13:16Z`），gate 未消费；canonical plan 仍为 `a8e4b771…a613d2e`；lease 已于 `19:48:30Z` 过期。资源侧：ADB PID 85922、AVD device/inode 与 R3 冻结一致，**RESOURCE_BINDING 无需改**。

## 1. 发现的漂移

- 受保护 normal Mac 包在 2026-10-23 12:14 被其它任务更新：`/Applications/ohmymods/arm64/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll` 现为 `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf`，9,100,288 B；安装清单 `package-manifest.json`（SHA256 `22cdc4d8…e303`）与 `SHA256SUMS`（SHA256 `78b6ce67…c312`）均已记录该 hash，来源 `input-root`。旧冻结 pin `25d9ffef…` 已不再匹配，controller/preflight 会按精确漂移拒绝。
- localChecklist `efb52c89…` 未变；AVD/ADB 资源身份未变。

## 2. R5 修改（仅两处，base/helper 原文件与 hash 不变）

1. `zero-extra-hook-control-controller/controller.py`：`HOST_PROTECTION` 的 normalDLL pin 更新为当前受保护包 `055368a3…9fcaf`，localChecklist pin 不变；未放宽守卫、未自动刷新。
2. `zero-extra-hook-control-controller/register.py`：删除硬编码旧 plan SHA，改为使用必填 `--old-plan-sha`（同一处 preconditions、上传、revise/approve、readback 断言与回执，仅把 OLD 来源改为参数）；`BASE` 更新为当前代码基线 `59ae6a0`。用途：允许在已登记 plan `a8e4b771…` 上做**新的**续窗/重登记，不复用旧登记回执、不裸改 JSON/DB。

## 3. 受影响 hash

| 文件 | SHA256 |
|---|---|
| `zero-extra-hook-control-controller/controller.py` | `e10a2d547c16f6fb5b8cd8e0ce5bb5ed5f8218ef4c5c29236e2d999e7f58c70e` |
| `zero-extra-hook-control-controller/register.py` | `18ae5257c5e2b5cc8578995bde2e632e84824e475a3e0adbd0d14616befa6fce` |
| `next-zero-extra-hook-control-case-20261023/source-diff.patch` | `f87527b8107705e2cf078ad08f07c29d2b22abda47e82e77866fb5373e45138c`（26522 B） |

未变：test_controller.py `b96f874c…`、build_packet.py `185fc848…`、root-launch.py `e666aa4d…`、root-postflight.py `fda86837…`、root-normal-readback.py `9e161287…`；候选/cfg/F/addon/资源身份与 hash 闭包不变。

## 4. 验证与边界

- `python3 -m py_compile` 通过；派生 host tests **11/11** 通过（host protection 断言自动使用新 pin）。
- 只读 plan dry-run：`validate_plan_semantics` + `verify_plan_files` 全部通过（新 normalDLL pin 匹配当前安装包）。
- 未运行 builder/register/gate/VM/Native/产品构建；未改产品源码。上轮两次 host-only 拒绝原件保留（host 目录复用、ps sandbox），均无 VM/设备写入且 gate 未消费。

## 5. 批准后的受管续窗路径（拟）

1. R5 source 批准 → 按 O1 保留旧 packet 证据、移开 generated、用原 builder 重生成；确认新 packet 的 manifest/source-review/gate/receipt/plan/window 新 hash。
2. 对重组 packet 发增量 packet 审查（因 controller/register/report/manifest/gate 均变）。
3. 刷新 issue-232 lease（assignment accept 同一 owner/session/operation）；生成新的 shared/native window；用更新后的 register.py 以 `--old-plan-sha a8e4b771…` 重新登记（新 plan 绑定新 window，不复用旧回执）。
4. 启动唯一 Native 对照；执行前重核保护文件/AVD/ADB/端口；按 R2 同段规则判定，exit0 不等于加载成功。

## 6. 请求与未验

请只增量复审 R5 两处 source 修改与受影响 hash；R1–R4 已接受内容、资源身份及未变证据复用。若批准，请更新 source 报告首行 `APPROVE_ZERO_EXTRA_HOOK_CONTROL_SOURCE_ONLY`、`sourceSHA256` 指向新 controller `e10a2d54…` 并保留 case/objective/verdict/mustFix/unverified。

未验：packet 重建/续窗登记、新窗口/lease 生效、preflight/VM/Native、产品 InitPools 返回、玩法/保存/支付/手机/多人/长期。
