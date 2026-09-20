# 当前岛屿职业与骑士人数HUD

用户要求在游戏画面上像时间和季节一样显示各职业、各世界骑士数量。默认打开独立人数HUD，F5→人口可关闭；不依赖时间HUD或自动补货开关。

显示范围：当前岛屿存活角色，职业按实际组件分类为工匠、弓箭手、农民、长枪兵、忍者、狂战士、无业村民、乞丐；弓箭手包含随从与塔岗，忍者钓鱼外观不改分类。骑士显示总数及中世纪/死地/幕府/希腊/北境分组，解析未完成时标待识别。库存/订单不作为人数，未知NPC不靠名称猜职业。

实际2.4客户端边界：Character.OnEnable(0x980030)调用HasWorldAuth(0x62e1a0)，只有权威分支调用Kingdom.AddCharacter(0x5997c0)，非权威分支禁用并返回。因此本轮统计支持单机/联机主机；客机明确提示人数暂不可用，不把空名册显示为0，不为了显示新增网络同步或客户端全场扫描。host/client切换必须清旧数、重新seed。

实现：独立PopulationCounts缓存与PopulationHud显示，复用ModPanel的Update/OnGUI入口。现有Kingdom.AddCharacter/RemoveCharacter hook额外只标脏，不增加native detour。初次/上下文变化/增删事件才重建Kingdom原生名册缓存；稳态每秒从缓存读active/death/骑士风格，避免每帧或全场FindObjects扫描；加载与换岛清除旧数据。

布局：左上角、顶部时间条下方，无背景/边框、两列紧凑文本、默认字体链和1px阴影，屏幕缩放与时间HUD一致。数据读取、绘制、输入互不吞异常。

分工：worker仅隔离PopulationCounts/PopulationHud与对应测试，Operator负责配置与现有入口接线、实际2.4 API核验、完整IL2CPP构建及独立E副本验收；reviewer只读核对最终diff。保留刚修复的剑风互操作路径。

验收：分类/去重/死活与场景/角色变化/未解析风格/disable/独立开关与缓存频率回归；实际游戏编译/API检查、启动日志和游戏截图核对显示。不写Steam目录，不commit/push或替换公开6.0资产。

本机测试：只在没有用户游戏进程时替换E DLL并备份；受控运行有时限与内存守卫，测试采购暂关，退出后核对存档未变并恢复配置/金库。允许通过Computer Use查看游戏窗口和F5设置做可逆的显示验收。
