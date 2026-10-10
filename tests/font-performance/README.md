# 字体与性能回归

这些 CLI 测试直接编译 `il2cpp/` 产品源文件，不修改游戏或注册 Native detour。需要 .NET 8 SDK；hook-query 同时编译 net6/net8，并对测试过的 Harmony DLL 哈希设关口。`BepInExRoot` 指向已生成 interop 的 BepInEx 6 独立环境（core 与 interop 子目录）。

```sh
dotnet run --project tests/font-performance/font-policy/Tests.csproj
dotnet run --project tests/font-performance/font-regression/Tests.csproj
dotnet run --project tests/font-performance/save-hash/Tests.csproj
dotnet run --project tests/font-performance/native-key/Tests.csproj -p:BepInExRoot=/path/to/BepInEx
dotnet run --project tests/font-performance/hook-query/Tests.csproj -f net8.0 -p:BepInExRoot=/path/to/BepInEx
KEM_PERF_DIAG=1 dotnet run --project tests/font-performance/hook-query/Tests.csproj -f net8.0 -p:BepInExRoot=/path/to/BepInEx
```

font-policy/regression 覆盖静态、动态字体切换，显式 false、池复用、重入和失效对象；native-key 的假 adapter 覆盖 fresh UTF16 读取、fail-closed、线程与资源释放，其零分配断言只适用于测试 adapter。hook-query 使用实际 Harmony public PatchInfo 注册表，在测试自有 MethodBase 上增删数组，覆盖即时卸载、重装、缺失补丁、兼容 fallback、异常与并发诊断预算，不调用 Harmony.Patch。save-hash 用旧 ToArray 算法作独立 oracle，比较三个实际消费者的 canonical hash、长度/容量、Unicode、域和排序规则。

CLI 通过不替代 IL2CPP Native ABI、游戏行为、联机或玩家实测。
