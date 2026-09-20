# 剑风 IL2CPP 顶点写入修复

用户授权：先修复日志里能定位的问题。仅处理本 Mod 的 Medieval/Norse 剑风构建 ObjectCollectedException。

玩家5.0日志堆栈进入 LineRenderer.SetPositions → Il2CppSystem.Span ctor；已发布6.0仍含相同调用。实际2.4 wrapper核验：SetPosition(int,Vector3)直接native invoke，不经Span/临时IL2CPP数组。

实现：预设13点positionCount，逐点SetPosition，维持全部坐标/材质/排序/伤害/生命周期。保留失败清理；添加一次成功日志用于实机验证。不推测修改Griffin网络登记，不改Steam目录，不发布或提交新版本。

worker仅修改隔离源码与knight-tests；Operator核查diff、原调用失败对照/新调用通过、实际游戏API审计、完整IL2CPP构建。E独立副本受控验证前检查无用户游戏，备份DLL/config/save/bank，限时与内存守卫，仅停止自启进程；恢复配置和未保存测试的银行值。公开6.0.0包保持原内容。

验收：回归覆盖13点完整坐标/复用与中途失败清理重试；实际E运行抵达成功构建日志且无原异常。屏幕效果和联机不以managed stub或日志代替目视验收。
