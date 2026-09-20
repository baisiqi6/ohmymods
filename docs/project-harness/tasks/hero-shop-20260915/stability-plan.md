# 英雄驿站整店闪现：存续与付款条件分离

用户确认是整座驿站消失重现。旧f8与当前d005均在同岛同x多次出现ready而无异常，但旧实现不记录Clear原因，不能断言所有闪动均为同一原因。

用户进一步确认：“主要在打开／关闭暂停菜单时”。这与已定位的Menu导致Clear路径吻合，首要修复目标明确。

确定代码缺陷：HeroShop.Tick在TryContext失败立即Clear；该TryContext混入Game.State.Playing，只要同世界进入Game.State.Menu暂停就删除已创建shop，恢复游玩再建。原生Game.Run Menu状态仍保留world，当前影像不应该因此重建。不得改PNG/闪烁动画掩盖整店生命周期问题。

修复范围：只改变HeroShop存续判断，sameworld已存在Menu保留shop/flags/header，暂停付款并原生取消未完成交易一次；暂停中不创建。真正关闭开关、切世界、失去authority或online时仍清理。保留地面rootY修复、IntPtr owner接口、购买/附加存档/旗帜/塔策略。

添加有界pause/resume/clear(reason)事件日志（只有实体存在/状态转变时输出），用于排查其他可能的重建来源。不逐帧场景扫描或日志。

独立实际2.4只读核验：Game.playingOrInMenu RVA0x53C0E0接受state4(Menu)，InPlayableStateOrMenu RVA0x53BB70还额外包含ChallengeEnd。修复应明确限定Playing2/Menu4，不以宽getter代替，不增加getter钩子。保留现有同kingdom/layer/postbox/header身份核验，菜单中取消pending只有成功后才标已暂停。

OMP18.1.19 DeepSeekV4Flash/max按用户夜间规则负责HeroShop/tests；Operator集成构建与安装，独立内置reviewer只读审查。当前游戏PID18784在运行，不能替换DLL。所有实机验证如实区分：d005已记录rootY0.875/ready/purchase，尚无用户高度新图确认；本修复待下一次暂停/恢复实测。
