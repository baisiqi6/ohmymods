# Issue199 本轮主线集成验证（2026-10-15）

储存钻石已在正常Mac累计包911b254a中，本次将PR201对接release基线5d931e0并完成源码收尾。产品保持只读currentCampaign.storedGems，不计钱包；CalendarHud自动合并保留当前WallEngineerStatusText。HUD测试链接真实WallEngineerRules，删除同名fixture，不添加替代formatter，原断言保留并补6条格式/装填断言。Android仅更新共用helper一条冻结hash，其余74条保留；MobileCalendar/Hud仍直接消费该helper的标题与值。

实际验证：公共完整net8/net6均0警告0错误；储存口径55断言、HUD59断言；Android真实interop Debug/Release均0警告0错误；完整Adapter对实际Release DLL为1264通过、0失败（含冻结源码与metadata绑定）。所有构建禁自动部署。初次调用用了错误的输出/loader路径，已保留失败日志；改用实际输出DLL和既有loader-payload目录后通过，未修改产品绕过。

本机证据：`.local/tasks/release-consolidation-20261015/pr201/verification.json`及同目录日志；兼容修复的独审见`workers/review-calendar-compat.md`。未运行游戏、未改正常安装，未声称Windows或Android设备验收。按用户授权，必要源码验证/独审与PR合并后关闭代码任务；后续玩家问题另提Issue/PR。
