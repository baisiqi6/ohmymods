# ohmymods — Agent 必读

> 双架构项目：**IL2CPP 2.4.0 + BepInEx 6 是 Steam 发布主线**；
> **Mono 2.1.0 + UMM 是自用兼容线**。仓库：`C:/Users/ADMIN/Projects/ohmymods`。
> 本文件是给 agent（含新 session）的强制速查，详细文档在 `docs/project-harness/`。

## 必守规则（踩过的坑）

1. **先分流架构**：`il2cpp/` 是唯一发布与端到端验收主线（.NET 8 / BepInEx 6 / Il2CppInterop / HarmonyX）；
   仓库根 `Main.cs + Patch_*.cs` 是冻结的 Mono 历史/自用线。除非用户明确要求，不修改、不构建、不部署 Mono。
2. **[Mono-only] C# 5 语法**：无字符串插值/null 条件运算符；csc.exe 编译；Harmony v1.2（`HarmonyInstance`）。
3. **[Mono-only] 编译命令**：bash 里 csc 全量（引用 `E:/Kingdom Two Crowns/KingdomTwoCrowns_Data/Managed/`
   下 Assembly-CSharp/UnityEngine*/netstandard + `UnityModManager/` 下 UnityModManager/0Harmony-1.2），
   源文件用 `for f in Main.cs Patch_*.cs` 通配收集；`build.bat` 是 cmd 通配版（编码问题，bash 里别跑 bat）。
4. **[IL2CPP] 编译**：在 `il2cpp/` 执行
   `C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug`。Debug 默认会复制到开发环境；只验证编译时应禁用/覆盖 `BepInExPluginsPath`。
5. **部署边界**：IL2CPP 只允许写独立测试副本；`D:/Steam/steamapps/common/Kingdom Two Crowns` 禁止测试和写入。
   Mono 产物是根目录 `MyMod.dll`，部署到 `E:/Kingdom Two Crowns/Mods/MyMod/`。
6. **[Mono-only] 注入方案**（重要，别重踩）：BepInEx 5.4.23.3 的 winhttp（x86）+ `[General] target_assembly=`
   格式 doorstop_config.ini 指向 UnityModManager.dll。**UMM 21.0.32 自带 winhttp 不识别 Unity
   2022.3.51f1**。备份在 `E:/mod-dev/winhttp_bepinex5_x86.dll`。详见 runbook "注入方案"。
7. **源码参考**：`game-source/Assembly-CSharp-2.1.0/`（逻辑说明书，只读）；业务逻辑地图
   `docs/project-harness/game-logic-map/`（写 patch 前先查）。
8. **对象池游戏**：每次复用的本地初始化优先 OnEnable；但网络 RPC 必须等 sync 注册完成，禁止在 OnEnable 直接发送。
9. **单位缩放只动 y 轴**：x 是朝向符号（±1），`Mover.cs` velocity.x *= localScale.x，动 x 改速度。
10. **反射 GetMethod 前确认方法存在**：Worker/Peasant 没有 Start 方法（静默失败教训）。
11. **跨 biome 角色/工具**：Holder 注册 + sync 池（EnsurePoolForCharacter），否则 Pool.Spawn 崩/联机 desync。
12. **Resources.Load 找不到子目录资源**：必须 LoadAll + 名字匹配。
13. **2.1.0 差异**：Pool.syncID 是 short；Worker.OnTriggerEnter2D 在 npcShieldUser==null 时早退
    （希腊工人要补组件才能捡 BerserkerTool）；NpcShieldUser.Awake 可能提前 return（regenWait 要补初始化）。

## 文档同步（每次改动必做，缺了会忘）

- `docs/project-harness/harness-checklist.json`：活跃状态机；历史只进 `archive/`。改完运行 EXharness validator。
- `docs/project-harness/progress.md`：进展摘要。
- `docs/project-harness/game-logic-map/patch-patterns.md`：新坑编号追加（当前到坑 14）。
- `docs/project-harness/domain-model.md`：关键决策（当前到 D9）。
- 未获用户明确授权不要 commit；验收必须有对应证据，标有“待实测”的项目不得置为 done。

## 协作规范（collaboration-protocol.md 摘要）

- Operator 先侦查+分解；功能实现派 **worker**（deepseek V4 flash thinking=max）；
  重大功能/架构用 **reviewer**（kimi K3 thinking=max）交叉审核。
- worker 只建自己的 Patch_XXX.cs，**不改 Main.cs/build.bat**（Operator 统一注册）；
  跨 slice 契约由 Operator 在委派前定死。
- 默认只做 IL2CPP 构建与独立副本端到端验证。只有用户明确要求维护 Mono，或任务直接修改根目录
  Mono 源码时，才追加 Mono 验证。验收证据必须是编译输出、日志或游戏内现象。
- 委派时在任务书里写明：源码位置、契约、验收、C# 5 约束、模型要求。

## 常用路径

| 项 | 路径 |
|---|---|
| IL2CPP 开发环境 | `E:/QQ/QQ下载文件/Kingdom Two Crowns (1)/Kingdom Two Crowns` |
| IL2CPP 独立测试副本 | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091` |
| Steam 正式版（勿动） | `D:/Steam/steamapps/common/Kingdom Two Crowns` |
| Mono 自用环境 | `E:/Kingdom Two Crowns/`（GOG 2.1.0 x86） |
| 旧游戏（2.0.1 x64） | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Call.of.Olympus-P2P` |
| Player.log | `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Player.log` |
| IL2CPP 日志 | `<测试副本>/BepInEx/LogOutput.log` |
| 共享存档 | `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35` |
| 反编译 2.1.0 | `E:/Kingdom Two Crowns/Assembly-CSharp/`（源）+ `game-source/Assembly-CSharp-2.1.0/`（库内） |
