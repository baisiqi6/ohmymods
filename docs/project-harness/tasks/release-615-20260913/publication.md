# v6.1.5 已发布

正式发布：https://github.com/baisiqi6/ohmymods/releases/tag/v6.1.5
完整包：https://github.com/baisiqi6/ohmymods/releases/download/v6.1.5/KingdomEnhancedMod_v6.1.5_IL2CPP.zip

已读回GitHub稳定Latest，非draft/非prerelease。新标签固定源码提交`4aebcd0eaf4cac044576c03340844655da083c15`，未移动6.0或其他旧标签，也未修改master。

本版新增默认开启的本岛八职业/骑士五风格人数HUD（F5人口页独立开关），并修复剑风数组/Span构建异常；保留6.0功能。人数当前支持单机及主机，客机明确提示不可用；狮鹫网络登记问题仍待定位，作用范围与长期实战/联机边界见玩家说明。

ZIP SHA256 `487089ec4b8f1d87659328799a8e768920f7323b0bbdfeb3496b4fed2f7084e8`，39,501,007字节、313条目。DLL `2ce32e3438a9e129c64407572c48b94a171c49c7eb2707d2dcdbd2b3337fc519`，484,864字节，assembly6.1.5.0/plugin6.1.5/build6.1.5。
从精确提交的clean worktree构建，0警告/0错误，11套源码回归通过。ZIP CRC、重复条目、安全路径、完整allowlist和manifest/DLL一致性通过；306个引导/运行库/config文件与6.0包逐文件哈希一致。独立源码/文档与完整产物审核通过，远端上传后size/digest与本地一致。

对应功能版本已有人数实机截图与剑风13点构建证据；本次Cecil对比1309个方法，仅Plugin.Init的版本/build日志及其编译器生成的日志容量常量改变，其余方法体一致。正式DLL含人口两类型与SetPosition路径，不含临时截图类型或Dispose hook。

用户游戏仍在运行，本次没有打断进度，没有替换当前本机DLL，也没有执行6.1.5精确DLL启动；不把此前相同行为的验证误记为新版本实机运行。本机仍为人数HUD开发构建FFDD0E89，退出游戏后可安装正式完整包。发布过程没有改动用户存档、金库或个人配置。
