# 出征跟随：本机实现、部署与启动检查

用户报告的随从留墙存在明确源码原因：夜间守墙prefix把原生Object跟随改成固定Position，且墙外巡检会继续拉回；出征保持队籍，原生follow等待不因此重发目标。这是守墙策略覆盖原生跟随的问题，五风格共用，与换皮机制无关。旧prefix回归对照复现固定目标；2.1参考解释跟随等待逻辑，实际2.4互操作接口与原生wrapper已核实。尚未对玩家当时的单位做运行时状态追踪，不将全部卡住现象归为此因。

## 改动
Object SetGoal原调用与返回Wait始终保留，守墙只临时改Formation offset，正确计入朝向。明确夜间普通守墙、原生follow2、所属骑士目标匹配且没有高优先任务才允许调整。骑士离墙、出征或配置关闭时，既有3秒巡检归还自有offset；目标/身份/速度/offset被外部改写则不抢写。编队、登船、手控、逃跑和暂停受保护。昼间骑士排位要求实际Assemble；普通弓手镜像不再干涉骑士随从；墙外巡检只处理确认的守墙者。

保留原生招募、攻击、Wait与FSM；不新增全场扫描、传送或强行解暂停。世界切换清私有凭据。旧Mover目标不持久化，重启读档由原生队籍重建跟随，无须推断旧静态目标并接管其他移动。

## 证据
ZCode优先worker session sess_cf61fc46-6335-4928-90e7-36e0a212d283；worker初版未直接验收，operator完成守卫/凭据修正和接线，独立测试与审查最终通过。49/49回归，0失败；实际依赖构建0警告0错误；34可达Unity方法未见unstrip桩；既有Object SetGoal hook真实wrapper与ref Single参数核验通过。没有新增native hook地址。

最终源码SHA256：
- SquadFollowGuard.cs：282E5566C452373FD0ADE8EA491443D8DF74EE9A8C815592523082A1B397A752
- PatchWorld_DefenseSpacing.cs：840A009C51484D164F03667C13BCDD7B724B4B0E1B3106D8B99D8F04C254802C

部署DLL：C1763E2F6CE97DD651DC6493CF204B2FF3BD9A686BE16E3ADA6CC2A6A6CD1E61，build=4.5.0-expedition-follow-20260911。用户明确保存退出后，仅E独立副本备份并原子替换旧7F0CBE8D。启动2026-09-11T23:30:59.1649816+08:00至2026-09-11T23:31:40.2634315+08:00，脚本自建PID 12372，恢复第61天暂停场景后停止，仅停止自己创建的进程。采样峰值私有内存2.17GiB，最低系统提交余量7.71GiB，所有采样responding=true。

存档前后SHA256均为0019A23A1693ECD2F992C9FCF27068A046FC47EBDB4F3B33040163D946B5E985。BepInEx日志无Error/异常，日间Assemble门实际触发；没有发生夜间守墙/出征，不宣称SquadFollowGuard实际出征已验收。Player.log仍有启动前就存在的AppID/用户日志、Unknown Character Tag: Archer、Spart Farticles missing script、CastleShieldShop Invalid NetID等，逐项在部署前Player.log找到了相同消息，不能称整个Player.log零报错。

## 后续验收
任务保持doing。玩家需在白天和夜间让各风格骑士从墙下出征，观察随从是否随骑士前进、停止/回城正常，并补联机、登船和手控边界。49项managed测试不能替代真实IL2CPP路径和战斗时序验收。启动检查没有改存档、配置或公开4.5.0 ZIP；此前视觉/Greek火资源/补员诊断/银行HUD/120秒4人等改动保留。未commit/push/发布。
