角色：有界 worker，OMP deepseek/deepseek-v4-flash thinking=max。仅实现燕返硬失效修复。
cwd C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix，branch win/samurai-choreo-hard-validity，baseline67d39b9。Windows Operator已按用户交接接手既有samurai-slash-guard-20260925；Issue21 OPEN、武士PR均已合并，无其他未合并武士PR；Mac Issue29仅平台整合，业务归Windows。
允许读取本树、C:/Users/ADMIN/projects/ohmymods/game-source（旧2.0.1/2.1源码只是参考）、task handoff以及本任务设计审查回执。
允许修改：il2cpp/PatchRoles_SamuraiPowerDash.cs；tests/samurai-motion/{Program.cs,Stubs.cs}；需要共享抽取桩最小同步时 tests/samurai-retreat/ 和 tests/samurai-night-formation/；本任务目录 motion-worker-result.md。
禁止修改其他文件（另一个worker负责SamuraiDashVisuals及其测试）；禁止commit/push/merge/reset/clean/部署/启动游戏/接触玩家存档/修改Main.cs或build.bat/派子代理。不要修改AGENTS或全局配置。不编译主项目（Operator统一编译）；只跑相关独立测试。
请先读docs/project-harness/tasks/samurai-slash-guard-20260925/zcode-handoff.md，以及 design-review.md（Operator放入后才派本worker）。本轮要求优先于旧历史准则。
契约：完整Eligible的启动与Return语义原样；活动Choreo只按真正硬失效收尾。Tick、ChoreoRoutine、ChoreoHitScan必须共用一致的存续资格，不能漏任何调用；保持mover/owner身份/效果快照归还、3s硬上限、1.2s腿窗、OnDisable和异常收尾。isRetreating/isCharging/_shouldCharge/formation/FSM等原生行为改变不打断完整两腿，也不让命中失效。不得通过全局删Eligible子句或SetRetreating(false)解决。具体设计review中需要的最小修正必须落实。
新增有界日志明确硬失效子句，保留现预算，异常日志不能自身掩盖收尾。动画APRetreat先保留观测，不无证据操控animSync。
测试：新增夜间出腿/回腿中置isRetreating=true + 每帧Tick仍两腿到出发点、命中继续且每腿去重；出发前retreat仍拒绝；其他允许行为旗标；真正失效dead/inert/grabbed/disabled/style/world auth/mover replacement恢复效果；paused/cap/OnDisable既有回归保留。
必须做root cause红验证：临时把旧Eligible恢复至活动路径，新用例应失败，记录结果，然后恢复新代码并绿。不删除真实旧契约断言来凑绿。
最终输出中文，含改动路径/行号、测试与红验证命令和计数、已知限制；落motion-worker-result.md。遇事实不明或越界先在最终报告指出，停止依赖工作。
