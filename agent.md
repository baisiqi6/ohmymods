# ohmymods — Agent 必读

> 项目：Kingdom Two Crowns: Call of Olympus (GOG 2.1.0 Mono) 的 UMM + Harmony v1.2 mod。
> 仓库：`C:/Users/ADMIN/Projects/ohmymods`。本文件是给 agent（含新 session）的强制速查，
> 详细文档在 `docs/project-harness/`。

## 必守规则（踩过的坑）

1. **C# 5 语法**：无字符串插值/null 条件运算符；csc.exe 编译；Harmony v1.2（`HarmonyInstance`）。
2. **编译命令**：bash 里 csc 全量（引用 `E:/Kingdom Two Crowns/KingdomTwoCrowns_Data/Managed/`
   下 Assembly-CSharp/UnityEngine*/netstandard + `UnityModManager/` 下 UnityModManager/0Harmony-1.2），
   源文件用 `for f in Main.cs Patch_*.cs` 通配收集；`build.bat` 是 cmd 通配版（编码问题，bash 里别跑 bat）。
3. **部署**：编译后 `cp MyMod.dll "E:/Kingdom Two Crowns/Mods/MyMod/MyMod.dll"`。
4. **注入方案**（重要，别重踩）：BepInEx 5.4.23.3 的 winhttp（x86）+ `[General] target_assembly=`
   格式 doorstop_config.ini 指向 UnityModManager.dll。**UMM 21.0.32 自带 winhttp 不识别 Unity
   2022.3.51f1**。备份在 `E:/mod-dev/winhttp_bepinex5_x86.dll`。详见 runbook "注入方案"。
5. **源码参考**：`game-source/Assembly-CSharp-2.1.0/`（当前目标版本，只读）；业务逻辑地图
   `docs/project-harness/game-logic-map/`（写 patch 前先查）。
6. **对象池游戏**：单位生命周期 hook 一律用 **OnEnable**（Awake/Start 只在首次创建触发）。
7. **单位缩放只动 y 轴**：x 是朝向符号（±1），`Mover.cs` velocity.x *= localScale.x，动 x 改速度。
8. **反射 GetMethod 前确认方法存在**：Worker/Peasant 没有 Start 方法（静默失败教训）。
9. **跨 biome 角色/工具**：Holder 注册 + sync 池（EnsurePoolForCharacter），否则 Pool.Spawn 崩/联机 desync。
10. **Resources.Load 找不到子目录资源**：必须 LoadAll + 名字匹配。
11. **2.1.0 差异**：Pool.syncID 是 short；Worker.OnTriggerEnter2D 在 npcShieldUser==null 时早退
    （希腊工人要补组件才能捡 BerserkerTool）；NpcShieldUser.Awake 可能提前 return（regenWait 要补初始化）。

## 文档同步（每次改动必做，缺了会忘）

- `docs/project-harness/harness-checklist.json`：状态机。**改完必须 `python -c json.load` 校验**
  （手改 JSON 已破坏结构 3 次——优先用 python 脚本改，别用 edit 直接戳）。
- `docs/project-harness/progress.md`：进展摘要。
- `docs/project-harness/game-logic-map/patch-patterns.md`：新坑编号追加（当前到坑 14）。
- `docs/project-harness/domain-model.md`：关键决策（当前到 D9）。
- 每次改动 git commit；验收必须游戏内实测（用户实测反馈后关"待实测"）。

## 协作规范（collaboration-protocol.md 摘要）

- Operator 先侦查+分解；功能实现派 **worker**（deepseek V4 flash thinking=max）；
  重大功能/架构用 **reviewer**（kimi K3 thinking=max）交叉审核。
- worker 只建自己的 Patch_XXX.cs，**不改 Main.cs/build.bat**（Operator 统一注册）；
  跨 slice 契约由 Operator 在委派前定死。
- 验收证据：编译输出/日志/游戏内现象，不接受口头自述。
- 委派时在任务书里写明：源码位置、契约、验收、C# 5 约束、模型要求。

## 常用路径

| 项 | 路径 |
|---|---|
| 游戏 | `E:/Kingdom Two Crowns/`（GOG 2.1.0 x86） |
| 旧游戏（2.0.1 x64） | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Call.of.Olympus-P2P` |
| Player.log | `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Player.log` |
| 反编译 2.1.0 | `E:/Kingdom Two Crowns/Assembly-CSharp/`（源）+ `game-source/Assembly-CSharp-2.1.0/`（库内） |
