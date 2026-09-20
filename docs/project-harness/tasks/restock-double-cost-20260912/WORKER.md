# 双倍自动补货实现

执行身份：本机原生 Codex subagent fallback；未使用或宣称 GLM。

## 改动

- `build/PatchEconomy_AutoRestock.cs`：订单保存 `NativePrice`，统一通过 `AutoRestockCost` 派生 `TotalCost = NativePrice * 2`；扫描余额门、已有订单预留预算、进行中的余额检查、金币动画目标和最终唯一扣款均使用总费用。原生价验证仍为 1..100，店价变化仍取消并下一轮重读。不写店铺价格，一单仍只调用一次原生 `TransactionComplete()`。
- 成功日志保留 `price=` 表示实际总扣款，并添加 `nativePrice=` 与 `coins=`；保留每角色一次限制。
- `build/PatchEconomy_Banker.cs`：仅将自动补货扣款上限从 100 调整为 200，其余守卫与提交逻辑不变。
- `service-tests/*.cs`：保留原有 65 个检查，按双倍费用更新金额及计时断言；新增 31 个检查，覆盖五职业各自原价 1/2/3/100、总价与动画一致、原生交易仅一次且价格未改、成功日志、无效原价、只够原价不足总费、并发预留预算及投币中余额变少。原有最终付款前检查仍实际驱动至最后一枚金币之后；余额不足案例改为仍够原价但不足双倍。旧店价变化测试增加取消无扣款、重读后金币数和原价不改断言。
- banker stub 上限同步至 200，仅作为服务测试依赖，不等于验证真实 banker。

## 验证

执行 `C:/Users/ADMIN/dotnet8/dotnet.exe run --project service-tests/AutoRestockTests.csproj`：96 passed，0 failed。项目直接链接本任务 `build/PatchEconomy_AutoRestock.cs`，游戏对象、协调器、银行依赖为 stub。

保留并通过取消不扣款、扣后原生异常不退款不重试、离店不重复扣款、运动/世界切换/玩家占用/价格增长相关旧覆盖。

## 边界与待验证

未改 ModPanel/Plugin、canonical 仓库，未启动游戏、部署或完整构建主项目，未委派、提交或推送。

Operator 负责真实 banker 源码验收、完整构建及游戏内验证。模拟测试不证明真实游戏支付/出货/保存行为。
