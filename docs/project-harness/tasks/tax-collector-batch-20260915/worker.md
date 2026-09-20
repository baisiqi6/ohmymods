# Worker: tax assistant continuous coin batch

你是已被主Operator派发的实现worker，不是Operator；不要再次委派worker，不需要配置OMP、读技能或核时钟。只按本文实现。工具命令不可用就跳过执行交主Operator，不准修改任何全局/项目工具配置，不准创建探测文件，不准调用网络或xd://。本次只启用read/edit/write，直接读项目源码与测试然后改允许文件。

北京时间22:08工作日，用户偏好OMP deepseek/deepseek-v4-flash thinking=max。Cwd C:/Users/ADMIN/projects/ohmymods。使用invoke-coding-agents技能协议。仅允许改il2cpp/PatchEconomy_BankAssistants.cs、tests/greek-bank-assistants-scope/**和本目录worker-result.md。不改其他源码、贴图、版本、harness、存档、游戏DLL；不commit/push/部署/启动游戏。

用户报告连续扔币，税收助手每次瞬移来只收几枚就回家，希望每趟20左右。已查当前GetAssistantCapacity最少100，真实原因TryChainNextTarget成熟快照断流立即TeleportHome。维持希腊authority/worldscope/网络RPC注册/入账事务/补货租用门禁全部不变。

实现最小方案：每趟目标20枚（单独TripTarget20，实际容量原helper逻辑不动），所有完成检查一致（ScanAndAssign、TryChainNextTarget、SelectNextCollectors）；成功拾币仍恰好一次入账，20th停止继续扫，不多收一枚，不重复入账。活跃且已收>0、未到20，找不到下一合法成熟币时静止等候一次有界gap（COIN_MATURITY_SECONDS + 2*SCAN_INTERVAL=4.2秒）。首次断流建立deadline，后续scan不可不断延长；新成功拾取/新目标恢复后清deadline，下次断流重新等待。继续正常0.6秒scan，不新增场景扫描/钩子/逐帧日志。空手且没有目标不等待；玩家停扔后deadline到期回家/清carried/deactivate。Time.time用现有暂停与authority门禁，不跨世界等待。Restock借用、回池、角色替换/关闭/世界切换/回家均清新wait字段以防残留。网络未就绪/失authority不能以等待突破原门禁。

确认Scan生命周期：当前UpdateMovingAssistants只处理有target；无target等待可由已有ScanAndAssign周期TryChain推进，无需新Update hook。接链返回false时Sweep停止，active保持true等待；上层不可把false一律当deactivate。

独立review补充：ScanAndDispatch count<=0走CleanupNoCandidates而不走TryChain，此路径也要共用纯等待处理，不要在Cleanup里从旧MatureBuffer重新Assign。RollbackOwnedClaimPolicies失败原清理门不绕过。UpdateIdlePatrols原已有ActiveCollector跳过，等待保活跃即可。ResetAll新deadline清理需在失authority/Unknown提前return前，类似RestockReserved重置。20th触发回家清Carried为0后必须立即终止当前Sweep和外部target处理，否则会多吃21st；检查现有false返回链。

在现有测试添加有意义生产逻辑回归：短暂快照空不回家、4.2s未持续延长、有下一枚则继续、20币目标及扫币边界、最终无币回家、0币立即释放、waiting借用补货/失authority/关功能/跨world清理不串下次、原bank事务/其他world无改。Operator运行命令若worker工具不获批准，不需用户permission。

完成后停，输出具体修改和未实机边界。保留当前ad57存档fix和所有hero改动。
