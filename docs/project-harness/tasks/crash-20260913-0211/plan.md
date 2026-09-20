# 02:11闪退：敌方拾取短函数错位入口

用户报告游戏闪退，要求查因；先保留最新日志/转储，不继续安装6.1.5。当前本机仍FFDD人数HUD构建。

已完成Windows事件、转储寄存器、实际2.4方法token/机器码及源码钩子对应检查。最强假设：Droppable.CanBePickedUpByEnemy短函数detour错误进入+14，xchg esp,eax/rol bl/int3解释RSP=4与+18故障；动态trampoline页缺失，随后已完成下述单变量对照。盾牌SetShieldEnabled NRE另行跟踪，不能直接当致命点。

验证边界：单变量移除detour与原机器码恢复已经验证；真实隐士遭遇Troll、长期运行与联机仍待。盾牌异常需先区分shield/header缺失，不仅凭日志相邻认定Greek来源。不要只关闭Mod开关当作移除detour，不覆盖用户当前存档；需要测试时先采集新的save/config/bank基线。

12:15用户再次启动后发生相同故障：GameAssembly+4f0752/80000003、RSP=4。受控单变量版本仅撤去隐士getter的两处Harmony注解，1309个方法体完全不变；同存档运行110秒越过原崩溃点，实际进程内17字节恢复原始机器码。盾牌NRE仍存在，没有触发相同闪退。存档、配置、金库5709及原DLL已恢复。

永久修复已完成本机热修：完全撤去短getter hook，使用实际2.4已审计的较长Droppable.OnEnable/OnDisable生命周期，在单机/主机只把隐士自身允许敌拾取的CurrentEnemyPolicy变为Nobody。保存本代原值；开关关闭只归还仍属补丁的值；失权或上下文暂缺休眠不写；禁拾取策略和外部接管值保留；池复用/离场退休。复用ModPanel.Tick处理少量隐士，不扫描全场，不改玩家拾取策略/RPC/伤害/骑乘/升级。

验收：生产代码直链回归、完整IL2CPP构建、实际2.4成员与新hook审计、独立review、同最新存档受控自然运行且短getter仍原始17字节。实机保护日志与稳定运行必须分别记录；长期实战/联机边界不凭短测宣称完成。测试前新采save/config/bank，保护用户正常进程。正式6.1.5公开资产不覆盖，本机使用明确hotfix构建戳。

原始证据和详细DIAGNOSIS位于本机任务目录crash-20260913-0211与hermit-crash-20260913，不进入公开提交。盾牌异常独立定位，不通过吞日志消除表象。

最终结果：E已安装6A9A4546 / build=6.1.5-hermit-policy-20260913，24专属回归与原11组回归、实际API/源码复核、精确DLL自然150秒运行及原17字节验证通过，存档/config/bank5709保持。完整验收和剩余边界见acceptance.md。公开6.1.5资产与git远端不变。
