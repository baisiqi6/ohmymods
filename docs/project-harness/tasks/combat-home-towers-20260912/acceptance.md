# 本机修复验收

用户2026-09-12要求的三项已实现并部署E独立副本；公开5.0包/tag/Release未改变，未commit/push。

## 行为
同址普通塔清理允许level0和升级后无KEM名称的普通塔，但必须有当前场景独立已完成的typed隐士塔见证，x差<=0.1/y差<=0.5。原来的骑士/弩/火/面包/OilFire特殊塔及其祖孙结构和marker均保护。真实存档三对坐标156.64/166.66/176.68支持该规则。付款/选择/施工对象保护；原生撤出弓手并确认slot引用与层级脱离后才注销持久化/网络并停用销毁普通塔。旧KEM路径共享相同付款、特殊结构、脚手架关联与单位保护，避免绕过。只用WorldLoaded原有5秒scaled延迟，不直接编辑save。

狂战士离稳定防守/合法骑士跟随位置超过12单位请求返回，到合法原生目标2内释放；当前跳劈通过自身scanner不再提供目标自然结束，未在Attack强切状态或改drag/伤害/动画。归位目标跟随原生wall/side更新与follow offset；保留工具任务和followTarget。Berserker及Ninja在白天排除Enemy.shouldRetreat；Ninja没有新增12距离限制，只在Chase安全边界清目标并交回native返dojo/伏击路径。池/权限/世界/暂停/手控与scanner实际归属校验，1/3个自有scanner缓存有界整理，不改静态物理结果。失效旧scanner不再删除活跃单位新scanner的返程状态。

Greek骑士守墙不是现有直接禁用条件；ownFollower的旧_shootingTarget失效时，新增自身GetAll缓存兜底，最多64条并重验归属/scanner/currentRange。保持8秒/15秒/资源/RPC/生命等门，无队籍塔弓手仍不是任何骑士的随从。

## 验证与部署
288项真实源码CompileLink托管回归通过：Greek143（原79+新增64；同套基线119/143），Tower101（含旧46），Chase44（含原生GoToState排队而非同步切状态的fixture）。具体hash和call-order证据在各tests目录；tests工程与stub可复跑原位置为C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/combat-home-towers-20260912。这些证明决策、缓存与调用顺序，不代替IL2CPP战斗现象。

最终实际依赖构建0警告/0错误；182条可达Unity方法无unstrip失败桩；8个声明hook实际wrapper验证（旧WorldLoaded+7新追击），7新hook在当前GameAssembly方法指针表均sameSlots1。独立review发现并修复旧清理绕过及追击归位/任务/scanner替换问题；Operator最终核验与具体review证据另附。

DLL 6DE8F6CB3DA98BE1338A9CB9A9DE08B92A8CFD59D5E61DBC353D7C532573D341，build=5.0.0-combat-home-towers-20260912。部署前确认游戏退出及原DLL73B11F7D基线；原子备份/替换。受控启动2026-09-12T01:44:47.8900213+08:00至2026-09-12T01:45:29.1184722+08:00，仅自建PID41936，停止原因：world time diagnostic reached; stop before continued play。save前后1D1CA083FC05769C7462292D710969C9E1DEAC6D55DE772F08316F9ADF02C4C9一致。详细日志与内存/响应采样在startup.json。

## 未完成的实机验收
受控启动在暂停读档恢复后停止，未恢复5秒延迟、未实际拆塔或打仗，不能宣称本次已删除存档里的普通塔或已经目视确认狂战士/忍者返回。玩家下次读档离开重叠塔处、恢复游戏运行约5秒才会执行清理；选中/付款/施工保护可使本轮跳过，需下一次load再查。正常游戏保存才持久化删除。原生弓手下塔生存、特殊塔保留、实际追击/守家火焰和联机仍待实战，harness保持doing。
