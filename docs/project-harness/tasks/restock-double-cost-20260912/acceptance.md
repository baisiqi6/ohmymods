# 自动补货双倍费用验收

已安装 `BC504C618C1EA422FF753477BEA0EAC7B48322253CE037615A6FF82F6CF7BCC5`，build=5.0.0-restock-double-cost-20260912。五职业含面包，原生价1..100对应2..200总费；预算预留、余额判断、金币动画和唯一扣款统一双倍，一单仍只执行一次原生TransactionComplete。Order.NativePrice独立保留原生价变化取消机制，未写shop.Price。设置卡显示“双倍金库付款”。已付离店不再扣款、未付取消不扣款，异常策略保持原样。

完整IL2CPP构建0警告0错误（使用dotnet8 SDK，项目原有net6.0目标）；服务回归结果见service-results.txt；63实际bank/lease提取断言通过，226可达Unity方法无unstrip失败桩。无新增native hook。独立review PASS，见REVIEW.md。

实机受控PID38536，2026-09-12T23:15:05.6628294+08:00至2026-09-12T23:16:05.3288685+08:00：忍者price=4/nativePrice=2/coins=4，狂战士price=6/nativePrice=3/coins=6，均有付款后walked-out。日志无Error/Fatal。这里证明发出的动画金币数量，未声称人工目视。此次仅为测试临时提高两职业目标并禁用其他采购，运行后配置逐字节恢复。

用户先保存并退出PID41004；测试以新保存状态为基准，save=7289D36982FFD7CB07B70140B03353EE8B923221321FE4ED3BFAF18295AEC669前后相同，bank 5408→5398恢复5408。受控进程已停止。

OMP deepseek-v4-flash provider元数据已核实，但其进程KnownFolder空导致NuGet path1=null，未产出实现，root停止后采用本机subagent。OMP kimi-code/k3因月配额403无法审查，独立本机subagent完成review；没有冒称GLM。无commit/push/公开发布，公开5.0仍原版本。

既有边界：其他世界仍受部分旧全局补丁影响，本次未做世界隔离；联机/长期全场景未穷尽；原生CoinsSpent统计仍由原生购买按店价计入，本次改变实际金库消耗与动画，不额外改该统计。
