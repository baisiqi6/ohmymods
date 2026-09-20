# 英雄驿站InvalidProgram修复验收

2026-09-15 英雄驿站实际未出现已定位并修候选f8c25095 / build=8.0.0-hero-shop-owner-interop-20260915。a6b5实际日志类型注册后InvalidProgramException；根因为本机ClassInjector对out enum生成ldobj LockReason&非法Invoker，注册/Marshal成功直到首次JIT才失败。只改自有IsLocked为ABI等价IntPtr输出桥，精确WriteInt32(NotLocked21)，保留原interface预检并核写回，增加首异常阶段/完整栈。7真实production指针/JIT/guard断言、31商店、完整和actualinterop0W0E、独立复现与review通过；2862方法不变/4方法改动，旧IsLocked换签名+编译器闭包编号变化，全部4PNG保持。已闭游戏备份安装正确E盘，用户存档/配置hash保持；未启动游戏/公开发布，preflight passed+ready及实际投币待验证。跨岛仍未完成。

## 证据

- receipts/first-live-failure/LogOutput.log：用户真实启动错误与候选build，配置为开启。
- receipts/owner-interop/invoker-repro.cs及txt：独立原故障复现；静态wrapper JIT对照全部通过。
- tests/hero-shop/Invoker.csproj：链接实际WriteUnlocked源码，旧ref enum注册后JIT失败、新IntPtr两路径和4字节/布尔返回哨兵通过。
- receipts/owner-interop/build.txt、interop-build.txt、shop-tests.txt、invoker-tests.txt。
- receipts/owner-fix-dll-audit.json：精确DLL对a6b5逐方法及资源对照。

## 工作范围

OMP18.1.19 DeepSeekV4Flash/max进行了初始有界依赖调查，同session恢复接收独立复现。调查后停止外部进程，未留下后台worker；其无生产修改。Operator采纳最小参数桥实现并集成，独立reviewer只读审核通过。没有全局替换/patch Il2CppInterop，不改原生compressed save，不启动关闭用户游戏。

## 后续

已闭游戏备份安装正确E盘，用户存档/配置hash保持。实际接口调用、商店出现/投币还须游戏验证，任务仍doing。
