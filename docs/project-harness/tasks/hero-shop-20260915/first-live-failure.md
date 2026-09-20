# 首次实际运行：英雄驿站未创建

2026-09-15 19:28:46 用户启动正确E盘2.4游戏，PID27996；随后反馈驿站未出现。

- LogOutput line16：build=8.0.0-hero-shop-flags-ground-20260915；已安装a6b5d90b候选。
- 配置HeroArcherEnabled=true，模组Enabled=true。
- 已有HeroRecruitment loaded:991d64a3:0。
- line116：Il2CppInterop Registered mono type KingdomEnhancedMod.HeroShopOwner。
- line117：[HeroShop] unavailable: InvalidProgramException Common Language Runtime detected an invalid program。
- 没有HeroShop ready日志，因此不是用户没找到成功创建的商店。

此前actualinterop仅编译验证不覆盖游戏内接口执行，这次失败不允许记为实机通过。日志初版只包含异常类型/消息，缺栈与阶段；修复需补首失败栈/阶段并保持防刷屏。

晚间worker按用户规则：本机OMP18.1.19、deepseek/deepseek-v4-flash、thinking=max，model_change显示非fallback；session 01a0a4d8-0172-7127-a71e-1d4ed58c0d16。允许仅HeroShop与tests/hero-shop，禁止部署/游戏操作。

运行期不替换DLL；主agent未关闭游戏。核查中用户游戏自行退出，后续仍须安装前再次核对。运行日志保存在receipts/first-live-failure/，不能覆盖用户存档或擅自重启。

## 离线复现已确认

独立reviewer复刻已装ClassInjector metadata29 CreateInvoker动态IL：ref enum参数被发射为`ldobj LockReason&`，CreateDelegate/Marshal.GetFunctionPointerForDelegate都成功，RuntimeHelpers.PrepareDelegate才抛InvalidProgramException。因此“类型注册成功，首次接口调用失败”的日志时序一致。

局部IntPtr reason桥对照JIT与受保护内存调用成功，写回准确4字节且相邻哨兵不变。修复只动自有HeroShopOwner接口实现，不改全局运行库。仍必须保留原生IPayableComponentOwner.IsLocked(out LockReason)真实预检，并用哨兵验证锁原因正确写回；不能只删预检避开错误。

主agent同时对已安装静态HeroShop/owner/native接口包装、Sprite.Create与ImageConversion.LoadImage做RuntimeHelpers.PrepareMethod，均通过。以上测试都未调用游戏native、未加载GameAssembly。证据见operator hero-shop-20260915/interop-fix/jit-probe及reviewer-invoker-probe。

OMP原轮因需注入新复现证据而明确停止PID28160，使用同session恢复，并传入interop-fix-followup.md。不是把暂时安静当故障，也没有启动第二个并发写同文件worker。
