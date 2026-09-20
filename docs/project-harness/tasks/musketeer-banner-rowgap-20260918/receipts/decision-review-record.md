# 决策对抗审查回执：musketeer-banner-rowgap-20260918 诊断与修法

按 collaboration-protocol.md 规则 9 落盘。审查者：operator 主 agent（ZCode，GLM-5.3）内置 subagent（继承主配置）；模型证据=主 agent 为 GLM 5.3 max + 继承配置。

## 被审决策

用户报告举旗编队火枪手行与弓箭手之间空隙过大（"像又站了四位弓箭手"）的根因诊断与修复方案（operator 原提"删尾部2个原生Gap槽+行前置+行距改Archer值"）。

## 审查 verdict：换方案（operator 原方案被否决，采纳审查给出的中插设计）

- 诊断数学确认（含补充场景）：现行间隙 1船=4.71×弓手步、0船=5.7×、2船=13.9×；根因=行前置到编队尾+行距取Gap值+原生2个尾部Gap槽+多船时船间距1.0×船数。
- 原方案否决理由：2+船时间距仍=.21875+1.0×船数（用户诉求未解决）；0船残留fleet槽Gap；把"fleet后恰2个Gap"的资产结构焊死进代码。
- 采纳的中插设计：行插到第一个Archer槽前、Squire行槽（七实现者类型表均不含Squire、Recruit无分支、无UniqueType查询者=零认领风险）、spacing[Squire]=BaselineSpacing[Archer]覆写（与船间距覆写同层，还原时整表还原）、startOffset补偿式不变。几何已逐场景核算：边界恒=1×.21875（任意船数/占用）；弓手/长矛槽坐标满员时精确=基线；fleet块刚体平移−.875。
- 关键语义：空Squire行槽不占位=原生压实语义（Formation.cs:324），未满员时队列收紧贴弓手侧（NextFreeMusketeerSlot高位填保证）——正是用户"复用弓箭手队列逻辑接上"的语义。
- 删原生Gap槽路径被审查评估为原生侧安全但非必要（中插不删任何槽，风险面归零）。

派发：2026-09-18（用户指明休息日）→ 按休息时段路由 OMP deepseek-flash、thinking=max（协议 2026-09-18 休息日裁定）；deepseek-flash 现役=V4.1 Flash 原生多模态（OMP 18.2.5 目录已修正）。
