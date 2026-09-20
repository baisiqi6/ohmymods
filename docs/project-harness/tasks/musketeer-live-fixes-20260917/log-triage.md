# 本次日志核对
- 三次 Character.DropItem trampoline NullReference：已撤掉新增Nullable参数detour，移到既有Droppable.Drop显式source；运行时实测尚待。
- 购买后333.3ms长帧窗口：代码确认逐次Resources.LoadAll，已移除。原日志没有逐阶段耗时，不能量化唯一贡献；新purchase stage-ms用于实测。
- KnightIdentity load-mismatch与22身份重新seed：确认仍依赖非持久realStartDateTime，稳定上下文/旧档兼容修复进行中。
- Crossbowman scale drift一次：诊断警告，未从本次日志定位具体写入方，保留记录。
- FriendlyTrollBalance一次header未就绪：保持失败关闭，不虚构网络身份，尚无后续异常证据。
- Il2CppInterop Class::Init substitute：引擎兼容回退警告，游戏初始化成功，未改框架或全局设置。
- 用户已确认居民实际拾枪转职；日志gun->unit及saved records4/bound4。
