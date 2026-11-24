# 盾店付款门 source-linked 回归

依赖 Python 3 和 .NET 8 SDK。测试仅编译当前产品函数及固定旧函数，使用 native/付款边界 stub，不加载游戏或存档，不含第三方包。

从仓库根执行：

```sh
python3 tests/heavy-shield-payment-gates/generate.py
dotnet run --project tests/heavy-shield-payment-gates/Host.csproj -c Release -- reduce
dotnet run --project tests/heavy-shield-payment-gates/Host.csproj -c Release -- edge
dotnet run --project tests/heavy-shield-payment-gates/Host.csproj -c Release -- compare 275 3000
dotnet run --project tests/heavy-shield-payment-gates/Host.csproj -c Release -- alloc
```

`reduce` 验证公共查询从5/5变1/1；`edge`覆盖短路、Busy续结/失效、异常和跨调用重算；`compare`比较逐点结果及各操作流，可换seed；`alloc`只量纯host预分配场景的托管分配。

重现旧版RED（reduce应exit1：5/5而非1/1）：

```sh
python3 tests/heavy-shield-payment-gates/generate.py --candidate-source tests/heavy-shield-payment-gates/BaselineUpdatePaymentGates.cs.txt
dotnet run --project tests/heavy-shield-payment-gates/Host.csproj -c Release -- reduce
python3 tests/heavy-shield-payment-gates/generate.py
```

完成后重新生成正常产品候选；勿修改产品源码来制造RED。

各流单独记录，不是完整统一交错trace；setter target早于RHS的原时序另由源码/产品IL审查。付款状态、对象池和Unity行为由独立原生审计补证，host通过不表示购买/保存实机或FPS收益通过。生成文件仅是可重建输出，不提交。
