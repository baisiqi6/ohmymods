# 自动审查阻断与保留状态

2026-09-17 本任务 /root/knight_context_worker（弩手slice）与 /root/musketeer_hud_worker（英雄slice）分别返回：

> This request was blocked by our safety systems. Reason: Potentially unintended activity.

返回没有给出具体工具动作或更细理由。两分支均不重试、不转派、不由root手工实现被阻断的修改。已向用户说明自动审查阻断。其他原先独立的人口slice不接手这两个分支。

弩手三个生产文件mtime均在本轮之前，未落地代码修改。确认的只读事实：诊断缺Greek active门，非Greek原样y=1也可能报相对1.15的漂移；Mover正常朝向逻辑会写y=1，单次巡检不足以证明帧末缩放失效。拟议end-of-frame诊断未实施，不能声称修复。

英雄worker在阻断前添加了HeroArcherVisuals.MovementEvidence及ReportNativeEvent调用。Root将部分文件归档hero-visuals-blocked-partial.cs.txt并仅撤销这一新增helper及调用，恢复原报告行为。测试GameStubs/UnityStubs可能保留兼容诊断字段，未进入生产DLL；没有恢复或执行被拒的新增诊断。后续DLL审计必须证明Hero相关所有旧方法与基线一致、无MovementEvidence。

英雄只读事实：实际2.4资源Walk→Run Speed>1、Run→Walk Speed<1，零转换时长；日志hero walk=.975/run=2.4，靠近速度1阈值。worker核得实际2.4 Mover.Update→PassthroughAnimationOverride把水平速度绝对值写入Speed。没有切态当时速度/目标参数日志，不能把阈值附近候选当作已确认唯一根因；没有改移速或强制状态/帧。
