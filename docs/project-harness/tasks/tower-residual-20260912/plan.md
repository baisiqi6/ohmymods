# 弩炮/喷火塔下已升级普通塔残留

用户2026-09-12反馈前次清理未解决，并确认残留位于Ballista/FireTower下面。本次运行实证仅Tower0@156.64在TowerKnight下被移除；当前存档Tower4@166.66与Ballista独立、Tower1@176.68与FireTower独立，CBC存档建造值120/30分别等于实际资产阈值120/30。所有四对象独立SemiStatic ID、相同Level/GameLayer且y0。不能把清理一座及静态测试等同全部实机修复。

先确认实际跳过条件。现有代码多处静默return/stop，日志不含原因，不能据猜测放宽删除保护。准备仅只读、单次、同址pair范围的初始化诊断；既有世界加载hook，暂停时可读取，绝不调用拆除/退出岗位/注册/持久化/存档写入。实际游戏对象观察后再作针对性修复与原有101例回归、独立review、API核验、退出游戏后的E部署。用户当前正在运行PID36596，不替换运行中DLL，不操作用户进程或强退游戏。A309B08E为当前部署基线；公开5.0和Mono不变，不commit/push。

2026-09-12 13:53：现场诊断与修复已完成，根因为native退出时同步补岗；采取精确Kingdom同步抑制及停用后注销空岗，review/tests/runtime removed=2通过。详情acceptance.md；余下普通游玩保存再读验收。
