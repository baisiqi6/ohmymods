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
14. **跨 biome 对象池依赖链必须完整审计**：角色本体 → 转职工具 → 攻击投射物 → 技能特效 →
    死亡/撤退特效 → 召唤物 → 主客同步池 → 每次 `PoolManager.InitPools` 后重注册。不能只验证角色能出生；
    必须实际触发攻击、技能、死亡/撤退、换岛/读档和池重建。详见 `game-logic-map/patch-patterns.md` 坑 18。

## 文档同步（每次改动必做，缺了会忘）

- `docs/project-harness/harness-checklist.json`：活跃状态机；历史只进 `archive/`。改完运行 EXharness validator。
- `docs/project-harness/progress.md`：进展摘要。
- `docs/project-harness/game-logic-map/patch-patterns.md`：新坑编号与对象池依赖审计规则持续追加。
- `docs/project-harness/domain-model.md`：关键决策（当前到 D9）。
- 未获用户明确授权不要 commit；验收必须有对应证据，标有“待实测”的项目不得置为 done。

## 协作规范（collaboration-protocol.md 摘要）

- Operator 先侦查+分解；功能实现派 **worker**、重大功能/架构用 **reviewer** 交叉审核。
- 委派通道（2026-08-16 起）：第一顺位走本机 **OMP CLI**（非交互 `omp -p`）——worker 用
  `deepseek-v4-flash` thinking=max，reviewer 用 `kimi-code/k3` thinking=max；
  备选回落本 agent subagent（GLM 5.3 thinking=max）。调用纪律见
  `~/.agents/skills/invoke-coding-agents/`，模型标识以 `omp models` 实时输出为准。
- worker 只建自己的 Patch_XXX.cs，**不改 Main.cs/build.bat**（Operator 统一注册）；
  跨 slice 契约由 Operator 在委派前定死。
- 默认只做 IL2CPP 构建与独立副本端到端验证。只有用户明确要求维护 Mono，或任务直接修改根目录
  Mono 源码时，才追加 Mono 验证。验收证据必须是编译输出、日志或游戏内现象。
- 委派时在任务书里写明：源码位置、契约、验收、语法约束（IL2CPP 主线现代 C#；
  仅 Mono 任务才限 C# 5）、模型要求。

## 常用路径

### 2026-09-06 启动事故临时门禁
- 2026-09-13正式v6.1.5已发布Latest；tag4aebcd0，ZIP487089ec / 正式DLL2ce32e34，人数HUD+剑风修复，11回归/clean完整包/独立审核/远端digest通过。用户游戏运行中，本机仍FFDD0E89未替换或新版本启动；待退出后安装。单机/主机人数、客机提示不可用；狮鹫仍待定位。见tasks/release-615-20260913/publication.md。
- 2026-09-13本机人数HUD：E FFDD0E89 / build=6.0.0-population-hud-20260913；左上八职业+骑士五风格，F5人口独立开关默认on。30+81回归/build0W0E/177Unity/review及原生截图通过；临时capture已移除，save/config/bank保持。单机/主机有效、客机提示不可用；公开6.0不变。见tasks/population-hud-20260913/acceptance.md。
- 2026-09-13本机剑风热修：E DLL 3399c131 / build=6.0.0-wind-arc-interop-20260913；SetPositions数组/Span改13点SetPosition，31回归/build0W0E/109Unity/review通过。实际E游戏已完成13点SetPosition剑风构建，成功日志出现，无原异常。save/config/bank保留，公开6.0.0不变。见tasks/wind-arc-interop-20260913/acceptance.md。
- 2026-09-13正式v6.0.0已发布并为Latest；tag/source356078c，ZIP416b6c79 / E正式DLLcdf5f61f。双倍采购、HUD/塔位/猫1.25及交战锚点修复，10套回归/clean完整包/自然夜间锚点与退速观测/远端digest通过；用户状态保留。完整攻门/大蛇/联机与全世界隔离边界仍追踪。见tasks/release-600-20260912/publication.md。
- 2026-09-12最新本机：B33D56B8 build=5.0.0-cat-scale-125-20260912；希腊农舍猫y缩放1.2→1.25，其他逻辑保留。build0W0E、DLL常量与安装哈希通过，save/bank未动，公开5.0不变。见tasks/cat-scale-125-20260912/acceptance.md。
- 2026-09-12最新本机：BC504C61 build=5.0.0-restock-double-cost-20260912；五职业原价双倍投币/扣款，手动价保留，实机忍者4/狂战士6且离店成功。服务回归/63bank/226Unity/build/review通过，save/config/bank5408恢复；公开5.0不变。其他世界旧全局补丁隔离仍未完成。见tasks/restock-double-cost-20260912/acceptance.md。
- 2026-09-12 15:55最新本机：8929889C build=5.0.0-peasant-hud-motion-20260912；无边框HUD默认字体用户确认、Peasant面包采购本机开启15、税收官附近出现跑近付款/走开返城。206检查/309Unity/nativehook/review与正确E实机采购位置日志通过；save与bank5423恢复。公开5.0不变，见tasks/peasant-bread-hud-20260912/acceptance.md。
- 2026-09-12 14:17最新本机：654051E9 build=5.0.0-restock-allhours-20260912；允许全天采购，修读档kingdom.banker=null误拒绝税收官借调/扣款。42service+51counts+49integration/build212Unity/review通过；FF9D真实忍者2金币/狂战士3金币购买成功，最终只改全天帮助文案。save与bank5423恢复，HUD/塔修复保留；公开5.0不变。见tasks/auto-restock-live-20260912/acceptance.md。
- 2026-09-12 13:53最新本机：2BD2055C build=5.0.0-hud-tower-refill-20260912；时间/金库像素条（左上人口暂空）+同步补岗重入修复/停用后注销空岗。109tests、144Unity路径、native唯一hook、review和E实际removed=2通过save一致。正常保存再读/HUD目视仍待；自动补货采购仍未证实。公开5.0不变。见tasks/hud-pixel-style-20260912与tower-residual-20260912/acceptance.md。
- 2026-09-12最新本机：A309B08E build=5.0.0-auto-restock-20260912；四职业税收官金库采购/事件库存缓存/独立阈值页，默认off/15。127tests/build326Unity路径/4nativehooks/review/暂停启动通过save一致；真正启用采购及联机待测，公开5.0不变。见tasks/auto-restock-20260912/acceptance.md。
- 2026-09-12最新本机：6DE8F6CB build=5.0.0-combat-home-towers-20260912；同址升级普通重复塔安全撤员清理、Berserker12/2原生返程、B/N黎明不追撤怪、Greek守家自身scanner兜底。288tests/build/API/hook/暂停启动通过、save一致；实机清塔与战斗待确认，公开5.0仍0549。见tasks/combat-home-towers-20260912/acceptance.md。
- 2026-09-12 00:59最新本机热修：73B11F7D... build=5.0.0-tower-overlap-20260912；修特殊升级塔无Tower标签漏检/旧空KEM重叠，46tests/review/build/启动通过。当前day63暂停未实机清旧，需恢复运行5秒并避开付款/选择保护。公开5.0 ZIP和DLL0549仍未更新，未commit/push新修复。详见tasks/tower-overlap-20260912/acceptance.md。
- 2026-09-12正式v5.0.0已发布并为GitHub Latest；tag/source abf4eb3，ZIP DD7C26FA...（39458470 bytes/313entries），E正式DLL0549C166...（5.0.0/build5.0.0）。clean构建、7套repo回归、精确包启动与远端digest均通过。下方F32及更早为历史本机版本；功能实战/联机边界仍doing。见tasks/release-500-20260912/publication.md。
- 2026-09-11 23:59最新本机：F32CBE8C...，build=4.5.0-fleet-retreat-cats-20260911；希腊舰队仅新Greek配队min/仅武士defensive retreat3x/每farm4猫含旧mod回收。154tests+review+启动通过，存档一致；实际登船/退速/猫/残影/联机待验收。见tasks/fleet-retreat-cats-20260912/acceptance.md，下方C176及更早均历史。
- 2026-09-11 23:31最新本机：C1763E2F...，build=4.5.0-expedition-follow-20260911；修复守墙把随从动态Object跟随降为固定Position，改临时offset及任务门/可靠归还。49tests/review/启动通过，存档一致；实际出征/联机待验收，详见tasks/expedition-follow-20260911/acceptance.md。下方7F及更早均历史本机基线。
- 2026-09-11 23:03最新本机：7F0CBE8D...，build=4.5.0-combat-visuals-20260911；本机武士浅白残影45/25/10+.2s，Greek空fireSO恢复，Medieval sortingLayerID修复。170tests/review/启动通过，日志实证fire资源补齐；视觉实战待验收，新客机残影同步未做。见tasks/combat-visuals-20260911/acceptance.md。
- 2026-09-11 最新本机：8BA3448F...，build=4.5.0-squad-refill-diag-20260911；撤旧逐箭日志，新增有界补员事件和既有缓存名册采样（最多36行），36tests/review/启动通过。补员根因尚待现场日志，不得宣称已修。见tasks/squad-refill-20260911/acceptance.md。
- 2026-09-07 最新本机：EA5D1001...，build=4.5.0-greek-fire-20260907（希腊火8s/15s+火buff换队弩包保护+既有银行HUD/1204/幕府回冲伤害）；70case、独立review、受控启动通过，实战/联机待观察。见tasks/greek-fire-20260907/acceptance.md。
- 2026-09-07 21:46本机最新：2F089BA8...，build=4.5.0-samurai-return-hit-20260907（回冲也有正常伤害，前/回共用命中）；76case及受控启动通过，实战待反馈。旧53E9无伤害版仅历史；见tasks/samurai-return-20260907/hit-update-acceptance.md。
- 2026-09-07最新本机：53E9A1DE...，build=4.5.0-samurai-return-20260907（主动返队+银行HUD+120/4）；21:31受控启动通过，实战待反馈。下方6C6/AA4B是历史候选/发布基线。见tasks/samurai-return-20260907/acceptance.md。
- 最新本机迭代：16:11已部署6C6BA423...，build=4.5.0-hudbank-20260906（银行HUD、人口默认120/4；本机明确授权同步120/4），受控启动通过。公开4.5.0包仍为AA4B1959...，下条是发布基线记录。见tasks/hud-bank-defaults-20260906/acceptance.md。
- E测试副本现为正式v4.5.0包内DLL AA4B1959...（15:56已通过受控启动），发布commit c203218，canonical同步强root取消守卫和幕府禁用清理修复。此前8390AF/A9/DB部署为历史；不得重新引入共享Dispose钩子。发布记录见`docs/project-harness/tasks/release-450-20260906/publication.md`；联机等边界仍待实测。
- 检查IL2CPP编译器生成的Dispose等短方法时，managed wrapper名字唯一不等于原生地址唯一；未核实地址折叠前不要把其detour视为已安全验收。
- WindowsPowerShell5.1不要把`Get-Content -Raw`/原始Provider对象直接放入高Depth的ConvertTo-Json；改用`[IO.File]::ReadAllText`与显式纯值投影，输出有界。事故进程有8.28GB提交量；不要重跑该脚本压测。

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
