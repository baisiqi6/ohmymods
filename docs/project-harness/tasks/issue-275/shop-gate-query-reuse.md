# #275：盾店付款门查询复用

每次 UpdatePaymentGates 会检查五个购买点。空闲点重复读取同一弓具/对象池可用性和全店 Busy 状态；现在仅在一次同步调用的连续 idle 点间惰性复用这两项。原字段赋值目标先于 RHS 固定，五点逐点写回；Busy 点继续调用原 CanPay，前后清掉局部结果，逐 kind 的身份/额度校验保留。没有跨帧缓存、降频维护、新 detour 或保存格式改动。

## 验证

- 测试从旧函数 fixture 与当前产品源逐字提取并编译；native/支付依赖使用边界 stub，不能代替原生行为。
- 旧实现 RED：五点场景 BowUsable/AnyBusy 各5次而不是期望1次，真实 exit1，不是编译失败。新实现各1次；付款值、写序和 Reserve/CanPay 各自序列保持。
- 156项边界断言、26对场景及8次跨调用检查通过；seed275的3000场景含471异常，独立seed911的5000场景含797异常，均0 mismatch、0 query increase。分别记录各操作流，未称测试覆盖所有跨流交错时序；target-before-RHS由源码和产品IL检查补证。
- .NET8 Release纯host预分配依赖下，20万入口原/新均0B托管分配；实际net6产品捕获结构为4 bool value type、入口无newobj、helper为byref直接call。不是Unity费用或整游戏GC保证。
- 当前Windows2.4.3累计源码闭包的诊断/非诊断独立NoDeploy构建均0W0E。公共源码补回原Busy语义注释后，Windows私有closure重建0W0E，与已审候选对照980316语义记录零差异（仅MVID）；另对完整公共源码使用Mac2.4.0实际interop做独立NoDeploy编译0W0E。最初复用的旧Mac包装器遗漏嵌套PalaceSource，失败记录保留；修正私有验证glob后通过，未为此改产品源码。richCecil各300条预期差异完全限定在原UpdatePaymentGates、两个local helper及4bool捕获结构/owner顺序；其他方法、引用和38项资源语义保持。原raw比较exit1及物理RVA/MVID差异均保留并分类，不称整程序集无差异。
- Windows2.4.3实际interop的 forceBlockPayment setter为非虚直接native bool store；当前field_offset导出与pool名字查询原生边界已核。允许跨这一不派发付款回调的数据store复用，不能推广到任意setter/回调。未将所有Unity getter和runtime初始化强称完全纯函数。

## 边界

原诊断报告中ShieldShop约0.970ms/frame为包含抢占和探针的壁钟值，不能当可全部省掉的CPU。当前只证明局部查询次数下降与上述条件下付款门结果保持；未安装此优化、未验新版本购买/迁址/存读档或FPS收益。Mac/Android Native及联机均未验，不复用Windows证据。原生方法地址仅用于本次版本核对，不新增地址detour。

弓手顺序调整本轮保持原样：ShouldPlayerControl的当前原生实现已核，但TimerTarget中的Haglet.started虚callee和Unity引擎调用闭包尚未完全说明；不根据大量倍率1样本就跳过未核清的路径。

Refs #275。更大范围的人口增长掉帧仍在调查，本文及此单点修复不关闭该Issue。原始玩家数据、机器路径与诊断会话仅留私有任务证据，不提交。
