# Continuity 复审：APPROVE（最终生产文件 + 测试/构建证据 + 既定安装门方案）

## F1 核验（通过）

`il2cpp/PatchRoles_SamuraiPowerDash.cs`（冻结版）与 `pose-correction.diff` b-side 逐段一致：
- `ChoreoToken.LastPlayFrame`（:484），`SlashLegStart` 成功 `Play` 后记 `Time.frameCount`（:675）；
- `LandSlashPose` 门改为 `if (!inState && token.LastPlayFrame != Time.frameCount) return;`（:704）——同帧 pending Play 也补 Land，且 `inState` 判定保留 short/full 双哈希；前置 `PoseAuthority`（同原 animator+controller+active）与非 dead 门不变。与我上轮给出的最小修正逐字等价，无范围蔓延。

## F2 核验（通过，且含 Operator 独立补抓）

- 新 `SamePoseTarget`（:636-648，身份门不含 PoseKnown）统一约束**全部**动画写：`SlashLegStart` 入口 `!SamePoseTarget → return`（:669）——换后的 animator/controller 现在**零写**（旧 fallback 对当前 animator `SetTrigger` 的泄漏是 Operator 自己抓出的第三处，比我列的更完整）；`TurnChoreo` 原无门 `ResetTrigger(PowerSlash)` 块整体删除，reset 移入腿 helper 统一门内；`Finale` 的 reset 也限定原目标（:879）。
- `ControllerKey` 改为初始 unknown 也快照（:619），触发器回退只对**同一个原 controller** 生效，中程更换零写。grep 全文件确认 `SetTrigger/ResetTrigger/Play` 仅存在于这三处门内。

## 测试证据核验（通过）

- 红绿判别力成立：`operator-pose-boundary-red.log` = 185/4，四红恰为 F1/F2 边界（同帧 start-failure 补 Land、替换 controller 零触发器写、替换 animator 零动画写、初始 unknown 换后失写）；`operator-motion-final.log` = 189/0，同名同行号全绿——同测试集对修前/修后代码的真实判别。
- 账目闭合：worker 186（operator 在最终代码上复跑 `operator-samurai-motion.log`=186/0）+ 3 新增 + 1 强化既有（"replaced controller" 加触发器计数断言）= 189；worker-result.md 保留迁移表与三组独立红验证（去 Land 2 红、0.3s 重申 5 红、绝对值到点 1 红，各恢复后 186/0）。
- 桩保真修正落实：`Stubs.cs` Play 分离 `shortNameHash/fullPathHash`、更新 `normalizedTime`、`OnPlay` 可注入延迟（:127-136），消除旧桩把 fullPath 当 short 的假绿源。
- 其余五套 31/24/32/43/9 全 0 failed（diagnostics 的 `ALL PASS: 9` 格式不同已核）；`operator-build-final.log` 0 警告 0 错误。

## F3 核验（按证据关闭代码项，实机项保留）

`animator-pool-flags.json`：四个 knight 相关 Animator `m_KeepAnimatorStateOnDisable` 全 false——禁用即重置动画状态，池化滞留 PowerSlash 的结构性风险被引擎 flag 关闭；不给禁用 animator 排 trigger 的决定正确（无证据风险不写代码）。序列化 flag ≠ 运行验收，池复生留待实机，边界声明诚实。

## 残留分级

- **P0/P1：无**。
- **P2（不阻塞安装与验收门）**：① 实机 Land 收势观感与两腿双斩击音（契约内预期）待用户验收；② 真实池复生一次往返待实机；③ 新 `LogChoreoClose` 行（endX/turnTimedOut）尚无实机样本；④ 每帧重申只缩小偷写暴露窗口（代码注释已如实声明）；⑤ 收尾欠账非本文件：`SamuraiDashVisuals.cs:12` 陈旧注释、docs/VERSIONING patch 账目、receipts 里建议一行标注 `operator-samurai-motion.log`(186) 是 worker 套件在最终代码上的复跑以免与红日志混淆。

## 安装门确认

条件与仓库规则一致：游戏未退出则等待（不杀进程）、旧 8E994A98 DLL 备份入任务 receipts、仅写既定 E 独立测试副本、禁触 Steam 目录、装前后存档/配置 hash 清单保持、未获授权不 commit/push/publish。用户已按 handoff 授权迭代安装——安装及其 hash 证据落 receipts 后，实机验收（收势、两段、无拖尾、无第三段）仍为 doing，不因安装置 done。

**最终裁决：APPROVE。**
