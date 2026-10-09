# ohmymods — Agent 入口

`il2cpp/` 是唯一发布与端到端验收主线：IL2CPP 2.4.0、.NET 8、BepInEx 6、Il2CppInterop、HarmonyX。
Mac 开发入口 `~/projects/ohmymods`；Windows `C:/Users/ADMIN/Projects/ohmymods`。

## 工作边界

- 根目录 `Main.cs + Patch_*.cs` 是冻结的 Mono 2.1.0 / UMM 历史自用线；除非用户明确要求，不修改、不构建、不部署。直接触及 Mono 源码的任务须追加 Mono 验证。
- IL2CPP 测试和部署只写独立测试副本；禁止测试或写入 Steam 正式目录 `D:/Steam/steamapps/common/Kingdom Two Crowns`。Mac 各环境共用游戏、共享存档，启动测试前按工作区规范备份，不并行启动。
- 正常开发完成必要审查与验证后，自行 commit、push、创建或更新 PR，无需逐次请求用户授权；只提交本任务拥有的改动，使用独立任务分支。
- 恢复任务先核验当前源码、游戏版本、运行环境和证据。历史“已修复/已验收”只代表当时结果；已推翻的结论注明原因，不无痕改写或继续当作现行依据。

## 修复与实现

- 修复、诊断定案和代码审查前必读 [根因修复与审查准则](docs/development/root-cause-repair-and-review.md)：先查错误产生处和状态写入责任；根因未明先做区分假设的最小诊断。验证错误在下游补偿介入前已不再产生，审查并清理旧补偿。
- 写 patch 前查 [业务逻辑地图](docs/project-harness/game-logic-map/)。`game-source/Assembly-CSharp-2.1.0/` 只读，旧版源码只能提供线索，不能代替当前版本证据。
- 池复用的本地初始化优先 `OnEnable`；网络 RPC 必须等 sync 注册完成，禁止在 `OnEnable` 直接发送。
- 单位缩放只动 y；x 是朝向符号，`Mover` 用 `localScale.x` 参与速度计算。反射前确认方法存在，Worker/Peasant 没有 `Start`。
- 跨 biome 角色/工具必须注册 Holder 和 sync 池（`EnsurePoolForCharacter`）。完整审计角色、工具、投射物、技能/死亡/撤退特效、召唤物及主客同步池；每次 `PoolManager.InitPools` 后重注册，实测攻击、技能、死亡/撤退、换岛、读档和池重建，见 [patch-patterns 坑 18](docs/project-harness/game-logic-map/patch-patterns.md#18-跨-biome-功能必须审计完整对象池依赖链)。
- `Resources.Load` 按名找不到子目录资源时，用 `LoadAll` 加名字匹配。IL2CPP 的 `Dispose` 等短方法可能共用原生地址；managed 名字唯一不能证明 detour 安全，须核实原生地址折叠。
- Windows PowerShell 5.1 禁止把 `Get-Content -Raw` 或原始 Provider 对象直接送入高 Depth `ConvertTo-Json`；使用 `[IO.File]::ReadAllText` 与显式纯值投影、有界输出，不重跑曾造成 8.28GB 提交量的事故脚本压测。

## 按任务必读

| 任务 | 文档与要求 |
|---|---|
| 构建、部署、日志或 Windows 路径 | [runbook](docs/project-harness/runbook.md)；Debug 构建默认复制到开发环境，纯编译验证必须禁用/覆盖 `BepInExPluginsPath`。 |
| Mac 构建、启动、安装或清理 | [Mac 工作区规范](docs/development/macos-workspace.md)；运行根目录 `/Applications/ohmymods/`，`.local/`、`game-source/` 仅保留本机，旧 `work` 链接只供历史记录解析。 |
| Mono 维护 | [runbook 的 Mono 章节](docs/project-harness/runbook.md#mono-自用线)，含 C# 5、Harmony 1.2、bash 编译与 doorstop 注入限制。 |
| 委派、模型选择或 ZCode operator 决策 | [协作协议](docs/project-harness/collaboration-protocol.md)；读取北京时间，按协议派发并核验实际模型事件，不静默换模型。 |
| 商店素材制作与接入 | [商店素材准则](docs/development/shop-material-standard.md)；人物独立动画层，按同世界成年居民比例接地，逐帧核验层序、脚线、footprint 和付款点。 |
| 发版与版本账目 | 根目录 [VERSIONING.md](VERSIONING.md)；按最近正式版核差异，修复/优化按完整条目 patch +1～5，新增功能 minor +1～3；同一问题返修不重复计数，不因改动多擅自跳 major。 |

## 协作与交付

- Mac 与 Windows 是平级维护者；任务先在 GitHub Issue 对齐目标、基线和范围，平台验收分别记录。一端通过不能代替另一端通过。
- Operator 先侦查与分解，功能实现派 worker，重大功能/架构交叉审查。跨 slice 契约由 Operator 先定；worker 只建自己的 `Patch_XXX.cs`，不改 `Main.cs/build.bat`，Operator 统一注册。任务书注明源码、契约、验收、语法约束与模型要求。
- ZCode operator 的实质决策按协作协议执行 GLM 5.3 max 对抗审查；修正后复审并留模型证据。具体时段、休息日、外派例外和素材路由以协议为准。
- 构建、自动测试、代码审查、安装和游戏实测分别留证。代码范围完成必要审查与验证、对应 PR 全部合并后关闭 Issue；玩家实机不作为关单前置，后续实机新问题另开 Issue/PR。未交齐或未合并时保持开放。
- `docs/project-harness/harness-checklist.json` 是任务状态权威；通过 Coordinate/harnessctl 对应入口修改，不裸改 JSON/DB，修改后运行 EXharness validator。
- 只同步受本次改动影响的文档：当前进展和验证边界写 `progress.md`，单次过程与哈希/回执写 `tasks/`，历史进 `archive/`，发布说明进 `release/`；可复用机制与坑点写 `game-logic-map/`，注明适用版本与证据；领域决策写 `domain-model.md`。不为凑流程新增材料，特定任务决定不泛化成全局禁令。

入口保留跨任务有效的约束；专项文档承载完整细则。简化时迁移并保留有效要求，不因归档取消用户约定。历史原文见 [AGENTS 历史记录](docs/project-harness/archive/agents-history-20260927.md)，按需查阅。
