# 本次运行日志只读核对

本次游戏进程从 2026-09-17 19:34:39 启动，运行于既定 E 盘独立测试副本。取证时仍运行，logs/BepInEx-snapshot.log 与 Player-snapshot.log 是读取时冻结副本，不代表完整结束会话。

已确认：加载 build=9.0.0-combat-reliability-20260917；CombatTargetLifeMarker 注册成功。火枪职业 loaded:exact records=23 / bound=23 / unbound=0 / pending=0 / conflict=False，左右11/12；后续出现 saved:records=23:bound=23。两名已付费英雄也恢复绑定。商店有暂停保留、恢复复用日志。

伤害相关：冻结 BepInEx 日志未出现 CombatDamage faulted、CombatTargetLife 降级或物理查询 unavailable 报告。这只证明所取日志没有这些错误，不证明每颗弹丸实际扣血、List饱和路径已触发、完整AoE生命周期或帧耗时已验收。

明确待查记录：

- Player 日志 11 次 `Invalid NetID from CRPCStamp on 'CastleShieldShop(Clone)'!`，调用来自 CRPCStamp.Persistent.IBehaviour.ApplyData / 读档还原。对象为城堡盾牌店，不能当成自有火铳铺报错，也不能仅凭日志认定原因或已修复。
- 两次 `Fixing fallen through object`：Archer P41 [6A5] 与 Archer P43 [6A7]。引擎 World 检测后将对象移回地面上方，栈处于显示菜单相关流程。确认发生位置异常，但 Archer 基类名字不足以判断普通弓手、英雄或火枪职业，也不能认定上次伤害修改是原因。本轮0.9只改变自有渲染child，不动角色物理或世界坐标。
- 英雄原生日志仍存在短时间 Stand/Walk 切换与相位归零；本轮没有新增该功能诊断或修改，不把缩放当作动作修复。
- 启动时一条 FriendlyTrollBalance identity/header unavailable，代码是当次反制小怪身份缺证据时保守不指定；后面有4条成功指定。无法证明警告对象和后续成功者是同一只，不宣称全部自动恢复。
- Player 另有1条 Unknown Character Tag: Archer（Holder.GetCharacterByTag），1条暂停时音频淡出提示、10条Zpix静态字体BestFit警告、5条字体覆盖提示、2条Spart Farticles缺失脚本提示和事件响应容量自动增长提示。它们与伤害异常不是同一证据；启动品牌/版本等也使用LogError打印，不能机械按LogError计数当崩溃。

未见新异常堆栈明确指向当前CombatDamage或弹丸可靠性实现；根因未确认的问题如实保留，未自动修改原生存档或全局缩放。
