# Bounded local worker: hero shop InvalidProgramException

北京时间2026-09-15周二19:33；请求OMP deepseek/deepseek-v4-flash thinking=max，按用户晚间规则。不再委派agent。

项目 C:/Users/ADMIN/projects/ohmymods，IL2CPP2.4 .NET6 BepInEx6。先读AGENTS。
实际运行正确E盘游戏，PID27996（不得操作）。当前a6b5d90b候选。日志：已注册 KingdomEnhancedMod.HeroShopOwner 后 [HeroShop] unavailable: InvalidProgramException Common Language Runtime detected an invalid program。Config已启用，商店没ready。主agent读取原生wrapper/依赖，与你并行。

目标：定位HeroShop创建中实际invalid IL/interop调用，并实现最小可靠修复及证据。允许修改仅 il2cpp/HeroShop.cs、tests/hero-shop/**（不改HeroShopBannerVisuals/Recruitment），独立记录 docs/project-harness/tasks/hero-shop-20260915/interop-fix-worker-result.md。主agent负责buildstamp/集成与审核。允许读repo与实际E盘游戏interop/core库及operator目录，不写游戏或存档。不reset/clean/commit/push/deploy、不启动关闭游戏、不读取密钥、不更改CLI配置。

不能把编译通过当成JIT或Il2Cpp运行成功。先检查installed Il2CppInterop生成ref/out enum IsLocked桥是否有invalid program，_owner.Initialize delegate转化，interface get_OnPay/CanPay/IsLocked预检，LoadArt/AddComponent/native注册各子调用。可用Cecil/本机ilspy或读已装包，必要 isolated RuntimeHelpers.PrepareMethod 验证，但不要加载并执行游戏native方法/私自附加注入到游戏。主agent手边Cecil E:/mod-dev/KingdomMod/deps/KTC-ModDevLibs/BIE6_IL2CPP/core/Mono.Cecil.dll。实际E游戏目录 E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091。

可增强有界初始化分阶段日志，完整异常ToString包含stack，仅首失败记录；不每帧输出或无限重试刷屏。如果可离线复现具体bad wrapper或ClassInjector动态桥，请用证据修，不盲目换PayableShop/覆盖共享全局IL2CPP库。不允许删掉安全CanPay/IsLocked或RPC预检当修复，更不能空owner吞币。务必保留8币、名额、退款幂等、清理、旗帜接入。最小设计若需新文件先写提议到result说明，不超范围。

测试：现Core31与actualInterop，必要增加真实JIT/相关native动态桥生成复现（只离线模拟，别触真实GameAssembly入口）。构建必须 -p:BepInExPluginsPath= 并输出isolated bin/worker修复，不部署。你改的生产代码主agent不会同时写。约20分钟以内有界，优先尽快写result中间结论，不要仅沉默思考；技术证据不足明确边界与诊断方案。
