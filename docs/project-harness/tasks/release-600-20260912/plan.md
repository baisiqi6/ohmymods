# v6.0.0发布

用户明确要求发布6.0，授权必要版本更新、提交、标签、推送和GitHub正式Release。收录5.0之后本机已集成IL2CPP变更，保留Mono冻结与既有功能验证边界。整个Mod的世界隔离尚未完成，公开说明直述。

从精确提交的clean detached worktree构建；仅用scripts/package_release.py生成完整包。核验6.0.0 assembly/plugin/build/tag/manifest、ZIP CRC/文件范围、内嵌DLL、安装与启动。发布后读回stable Latest、tag提交及asset大小/digest。旧tag与旧包不改。原始日志、存档、个人配置和私有会话不提交/分发。只在E独立游戏无用户进程时测试；存档/配置/金库保留。
