# v5.0.0 发布计划
用户明确要求发布5.0，授权本轮必要版本更新、commit、tag、push及GitHub正式Release。发布范围是4.5后已集成IL2CPP改动和玩家文档；Mono保持冻结。

只使用scripts/package_release.py，从精确发布commit的clean detached worktree构建并打包。核验5.0.0 assembly/plugin/build/tag/manifest/ZIP和完整runtime/引导/文档，包内不含个人配置、存档、日志、interop缓存、游戏二进制或worker副本。新包实际内嵌DLL仅部署E独立测试副本并做受控启动；存档前后hash一致。发布后读回tag、latest状态、asset大小与digest核对。旧tag/旧包不覆盖。

对应回归和审查已完成；真实登船/战斗/残影/猫/联机等doing边界不因发布置done，发布说明明确范围。原始日志、私有worker sessions及本机运行回执留在本机，不随源代码推送。
