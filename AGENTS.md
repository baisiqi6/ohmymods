# ohmymods — Agent 必读

> 双架构项目：**IL2CPP 2.4.0 + BepInEx 6 是 Steam 发布主线**；
> **Mono 2.1.0 + UMM 是自用兼容线**。仓库：`C:/Users/ADMIN/Projects/ohmymods`。
> 本文件是给 agent（含新 session）的强制速查，详细文档在 `docs/project-harness/`。

## 根因修复与审查准则

1. **优先修复错误的产生处**：先查错误状态的来源、配置、资源注册、分配算法和生命周期交接。
   不默认用周期扫描、反复覆写、静默默认值、扩大异常捕获、无限重试或自动重建掩盖内部缺陷。
   使用已有扫描而非新增扫描，并不能自动证明修复点正确。
2. **结论与证据相匹配**：区分已观察事实、待验证假设与尚未知的部分。修改前说明触发条件、
   首次偏离预期的位置、相关写入者和选取修复点的依据；根因未确认时先做能区分假设的最小诊断。
   注释、历史总结和 agent 自述只能作为线索，不能互相引用来证明根因。旧版源码不能代替当前版本证据。
3. **明确状态写入责任**：多个系统反复改写同一状态时，先明确哪个系统负责最终值、何时交接。
   “同帧改回来”“O(1) 开销小”“已有同类守卫”都不能单独证明持续覆盖方案合理。
   第三方代码不可修改时，可以在明确的生命周期或扩展边界接入，但须证明时序与状态责任正确。
4. **验证错误不再产生**：覆盖实际触发错误的路径，证明修复后在下游纠正器介入前已满足约束，
   并在相关重分配、转职、对象池复用或加载事件后仍成立。不能只验证新加的 clamp、fallback 或 retry。
   构建、自动测试、代码审查和游戏实测各自证明什么要分开记录，测试全绿不等于根因确认。
5. **审查并清理旧补偿**：根因修复时检查同一问题的旧镜像、扫描、重试和守卫，逐项说明删除、
   替换或保留的依据；经隔离验证后移除多余机制，不未经核验一口气删除仍有独立用途的保护。
6. **Reviewer 先审修复点，再审实现**：先判断为什么需要该补丁、是否在正确位置解决问题，
   再检查实现遗漏和边界。对争议诊断提供现象与原始证据，不能预设 reviewer 必须支持作者结论。
7. **允许有依据的故障保护**：外部暂时故障、明确的兼容需求或不可修改的第三方边界可以使用
   有界、可观察的保护；说明具体故障、正确性约束、恢复或退出条件。钱、身份、存档等副作用
   不能靠猜测结果或超时后盲目重做。内部缺陷的临时缓解必须标明局限并保留根因修复事项。

这些准则不要求每个小改动新建多份文档或增加审批轮次。简单问题用几句话交代证据；复杂问题
在对应任务记录中展开。检查的是证据与设计，不是材料数量或是否使用了“根治”一词。

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

## 文档与状态同步

- 按本次改动的实际影响同步对应文档，不为凑齐流程改写无关记录或新建重要节点。
- `docs/project-harness/harness-checklist.json`：任务状态权威。受管项目通过 Coordinate/harnessctl 的对应入口修改，不直接手改 JSON；改后运行 EXharness validator。
- `docs/project-harness/progress.md`：实际进展、验证边界和下一步；历史记录进入 `archive/`。
- `docs/project-harness/game-logic-map/patch-patterns.md`：出现新的可复用机制或技术坑时更新；对象池依赖审计要求继续有效。
- `docs/project-harness/domain-model.md`：记录影响领域模型的关键决策；没有新决策时不机械追加。
- 未获用户明确授权不要 commit；验收必须有对应证据，标有“待实测”的项目不得置为 done。

- **版本标准（用户2026-09-17授权）**：发布前先读根目录 `VERSIONING.md`。修复/既有行为优化每完整条目按体量累计 patch +1～5，新增功能按体量累计 minor +1～3；同一问题多轮返修不重复计数，基于最近正式版本核差异，记录本次增量账目。不要擅自因“更新很多”跳 major；玩法待验不因发布而置done。

## 协作规范（collaboration-protocol.md 摘要）

完整规则与最新约定见 [协作协议](docs/project-harness/collaboration-protocol.md)。

- **2026-09-18 用户澄清（路由边界）**：operator 本身是 ZCode 时，"本地 ZCode"第一顺位**由其内置 subagent 直接满足**（GLM 5.3 max 继承主配置），不要外派 zcode CLI 或 OMP 去跑 GLM；外派仅用于需要隔离进程/可恢复会话等内置 subagent 覆盖不了的形态。时段规则是调用偏好不是硬性仪式；DeepSeek Flash（现役 deepseek-flash，多模态）仍是夜间默认。
- **2026-09-18 用户新增 operator 对抗审查规则**：ZCode 作为 operator 的每一个实质决策（任务分解/契约定稿/侦查诊断方向/worker 异常处置/修复方案/测试范围/任务级验收裁决/安装发布回滚关口/版本账目）执行前，都必须由一个 GLM 5.3 max 强度的对抗性 subagent 审查三问——是否正确、是否最优、是否引入新 bug 及如何解决；主 agent 非 GLM 5.3 max 时改派本地 ZCode/OMP 请求 GLM 5.3 max 并核验实际模型，不静默换。修正后须复审（僵持两轮走规则 6 裁决记录）；每次审查落一条记录（任务 events/receipts，含模型证据），无记录视为未审查；边界情形默认按决策处理，跳过需留一行理由；仅停止失控进程/恢复备份/撤销危险写盘类紧急止损可先执行后同会话补审，安装/发布/tag 等不可逆关口绝不豁免。素材/美术制作可用 OMP DeepSeek Flash max（截至 2026-09-18 官方现役 id `deepseek-flash`=V4.1 Flash 原生多模态，deepseek-v4-flash / vision-exp 是已下线别名不再用，deepseek-v4-pro 不支持图像；本机 OMP 的 images 标志可能滞后，看图任务以官方文档+实际模型事件核验，被陈旧标志挡住时更新/修正 OMP 须先获用户授权，未授权期改走内置 subagent 或 text-only；可与 operator 对抗交互或双审互评）。细则与委派模板见 collaboration-protocol.md。
- **2026-09-15 用户最新 worker 规则（覆盖此前固定 OMP / 禁用内置 worker 的约定）**：按北京时间 Asia/Shanghai（UTC+8）派发时刻选择。工作日暂按周一至周五，不自动引入节假日调休表；09:00≤时间<12:00 使用本地 ZCode 第一顺位、OMP 第二顺位，均请求 GLM 5.3、thinking=max；14:00≤时间<18:00 使用当前主 agent 的内置 subagent（未指定模型，继承主 agent 配置）；其余时间（含12–14点、夜间和周末）使用本机 OMP DeepSeek Flash、thinking=max。Reviewer 仍可用独立内置 subagent，不受这份 worker 时段限制。
- Operator 先侦查+分解；功能实现派 **worker**、重大功能/架构用 **reviewer** 交叉审核。
- 新建或恢复一个工作轮次前读取北京时间；已运行的有界任务完成当前轮次再按新时段派发，不强行杀进程。上午 ZCode 不可用时可用第二顺位 OMP，但模型仍为 GLM 5.3 max；指定模型不可用时先诊断并说明，不静默换模型。实际模型ID/max支持及运行事件按 `C:/Users/ADMIN/.codex/skills/invoke-coding-agents/SKILL.md` 核验，不把显示名当作已验证路由。
- worker 只建自己的 Patch_XXX.cs，**不改 Main.cs/build.bat**（Operator 统一注册）；
  跨 slice 契约由 Operator 在委派前定死。
- 默认只做 IL2CPP 构建与独立副本端到端验证。只有用户明确要求维护 Mono，或任务直接修改根目录
  Mono 源码时，才追加 Mono 验证。验收证据必须是编译输出、日志或游戏内现象。
- 委派时在任务书里写明：源码位置、契约、验收、语法约束（IL2CPP 主线现代 C#；
  仅 Mono 任务才限 C# 5）、模型要求。

## 本文件与项目记录的分工

- 本文件保留跨任务有效的工作准则、架构/操作边界、必要技术约束和路径。不要为缩短篇幅删掉有效规则。
- 单次修复过程、候选版本、安装时间、DLL/ZIP 哈希、测试回执和美术迭代放到对应 `docs/project-harness/tasks/`。
  当前进展写 `progress.md`，任务状态写 checklist；历史记录进入 archive。
  发版前核对根目录 `VERSIONING.md`，发布说明归入 `release/`。
- 可复用的技术机制与坑点写入 `game-logic-map/`，注明适用版本与证据；特定任务的用户决定保留在该任务中，
  不把它扩展为所有任务的全局禁令，也不因归档而取消仍有效的用户要求。
- 历史记录中的“最新本机”“根因实锤”“已修复”只代表当时记载。恢复任务时核验现状；已推翻的结论
  应注明撤回原因和替代证据，不能无痕改写历史，也不能让错误结论继续作为现行依据。
- 本次迁出的原文见 [AGENTS 历史记录](docs/project-harness/archive/agents-history-20260927.md)。
  按任务需要查阅，不要求每轮启动加载整份历史。

## 持续有效的工具与注入约束

- 检查IL2CPP编译器生成的Dispose等短方法时，managed wrapper名字唯一不等于原生地址唯一；未核实地址折叠前不要把其detour视为已安全验收。
- WindowsPowerShell5.1不要把`Get-Content -Raw`/原始Provider对象直接放入高Depth的ConvertTo-Json；改用`[IO.File]::ReadAllText`与显式纯值投影，输出有界。事故进程有8.28GB提交量；不要重跑该脚本压测。

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
