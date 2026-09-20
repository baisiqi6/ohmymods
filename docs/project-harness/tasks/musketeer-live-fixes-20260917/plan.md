# 火铳手实机反馈与双商店重绘
2026-09-17 用户授权：购买卡顿定位修复，明确复用原生弓手/居民拾取，火铳手左右均衡；英雄驿站奇幻风，火枪铺西式风；检查当前日志并修可定位的问题。
基线已装50aa3037，保留既有其他修复。只改IL2CPP及本任务素材/测试/文档，不改用户存档/配置，不提交发布，不启动游戏。运行时不替换DLL；构建禁自动部署，完成候选和审核后按已授权范围闭游戏安装。
日志实证：4笔职业记录已保存bound4；有gun->unit转职；购买窗口maxFrame333.3ms与每次Resources.LoadAll全工具遍历相吻合，但尚无逐阶段耗时，不能把嫌疑当成已测唯一根因；3次Character.DropItem trampoline NRE需核实托管桥/nullable与原生来源，不吞全局异常掩盖。
13:21北京时间（工作日午间）派发OMP DeepSeekFlash max。性能worker只改Shop；防守worker只写新MusketeerDefense及测试；Drop异常worker只改MusketeerIdentity和相关边界测试。Operator负责美术、集成、构建、回归、部署；独立reviewer只读审核。所有worker禁配置修改/二次委派。
