# 协作模式规范（Collaboration Protocol）

> **2026-09-18 用户新增 operator 对抗审查规则（逐字引用）**："然后对于我们的zcode作为operator的协作规范，再加一条，就是你的每一步的决策都要有一个subagent作为对抗性审查者审查你的决策是否正确，是否最优，是否引入新的bug并如何解决，这个subagent也是GLM 5.3 max的强度，然后对于素材的制作，你可以像我们之前的协作规范一样使用deep seek flash max，因为它有多模态，可能比你直接设计要好，或者你们也对抗性交互，双审等等。"
> 执行解释见「operator 决策对抗审查」一节与规则 9/10；本条为最新用户规则，与 2026-09-15 worker 分时规则并行生效，冲突时以本条为准。

> **2026-09-18 用户澄清（时段路由的适用边界）**："而且你是zcode你为什么要用OMP去使用glm-5.3，这么死板吗？你不应该用你的subagent吗？"——operator 本身是 ZCode 时，"本地 ZCode"第一顺位**由其内置 subagent 直接满足**（GLM 5.3、thinking=max，继承主配置，同 Reviewer 待遇）；无需外派 zcode CLI 或 OMP 去跑 GLM。外派 CLI 仅在需要隔离进程、可恢复外部会话等内置 subagent 覆盖不了的形态时使用。时段规则是**调用偏好，不是硬性仪式**；同日另一条澄清：DeepSeek Flash（现役 deepseek-flash，多模态）仍是夜间/非上午的默认 worker，但用户不要求为模型名而牺牲合理性。
> **2026-09-18 用户澄清（休息日判定）**："今天不是休息吗，我记得协作规范里面不是说的工作日吗"——分时表的"工作日"槽位只在**实际工作日**生效；用户指明当日为休息日（法定节假日/调休，本例 2026-09-18）时，当日按"其余时间"路由（本机 OMP DeepSeek Flash、thinking=max）。协议不自动内置节假日表，以用户指明为准；拿不准时先问用户当日是否工作日，不要默认周一至周五全是工作日。

> **2026-09-15 用户最新 worker 规则（覆盖此前固定 OMP / 禁用内置 worker 的约定）**：按北京时间 Asia/Shanghai（UTC+8）派发时刻选择。工作日暂按周一至周五，不自动引入节假日调休表；09:00≤时间<12:00 使用本地 ZCode 第一顺位、OMP 第二顺位，均请求 GLM 5.3、thinking=max；14:00≤时间<18:00 使用当前主 agent 的内置 subagent（未指定模型，继承主 agent 配置）；其余时间（含12–14点、夜间和周末）使用本机 OMP DeepSeek Flash、thinking=max。Reviewer 仍可用独立内置 subagent，不受这份 worker 时段限制。

> 本项目协作流程，后续 session/agent 以本条最新用户规则为准。
> 时段按新任务派发/下一工作轮次选择，当前运行的有界任务先完成，不因跨整点中断。指定路由不可用先诊断并说明；上午可从 ZCode 切到 OMP，但仍用 GLM 5.3 max，不静默换模型。

> **2026-08-13 约定——IL2CPP 单主线**：IL2CPP 2.4.0 是唯一发布与端到端验收目标；
> Mono 2.1.0 降级为冻结的历史/自用线。默认只要求 IL2CPP `dotnet8 build` 与独立副本实测。
> 只有用户明确要求维护 Mono，或任务直接修改 Mono 源码时，才追加 Mono 编译/行为验证。

## 角色与模型顺位

| 角色 | 职责 | 默认选择 | 备选/限制 |
|------|------|----------|----------|
| **Operator** | 主控：分解任务、审查 worker 产出、裁决采纳、交叉审核 reviewer 结论 | 当前主 agent | 每个实质决策先过决策对抗审查（规则 9） |
| **Worker** | 实现：按任务规范编码、自测 | 按下表北京时间时段选择 | 上午 ZCode → OMP，保持 GLM 5.3 max |
| **Reviewer** | 审查：验证验收标准、找越界/缺陷/测试缺口 | 可用独立内置 subagent | 也可用 OMP，先核验模型和额度 |
| **决策对抗审查者** | 事前审查 operator 每个实质决策（三问逐字：是否正确、是否最优、是否引入新 bug 及如何解决） | 主 agent 为 GLM 5.3 max 时用内置 subagent；否则改派本地 ZCode/OMP 请求 GLM 5.3、thinking=max，按 invoke-coding-agents 技能核验实际模型事件 | 不受 worker 时段路由限制；不可用先诊断说明，不静默换模型；分歧按规则 6 裁决并记录 |

### Worker 分时规则（北京时间）

| 时间 | Worker | 模型 / 强度 |
|---|---|---|
| 周一至周五 09:00–12:00（不含12:00） | 本地 ZCode 第一顺位，OMP 第二顺位 | GLM 5.3 / max |
| 周一至周五 14:00–18:00（不含18:00） | 当前主 agent 的内置 subagent | 继承主 agent 配置 |
| 其余时间（含午间12–14点及周末） | 本地 OMP | DeepSeek Flash / max |

派发记录注明北京时间、命中时段、CLI及实际模型。工作日暂按周一至周五；若用户后续指定节假日/调休规则再更新。派发前按 invoke-coding-agents 技能实时核验本地 CLI、模型ID和 max 支持，启动后核验公开模型事件。此表是调用偏好，不代表 GLM 5.3 路由已经实际验证；不要启动空任务来消耗额度验证规范。午后内置 worker 不强行指定 GLM，也不改变 reviewer 既有权限。

> OMP 模型标识以 `omp models` 实时输出为准，不硬编码历史路由；调用前先核验。
> （例外：目录的**能力标志**如 images 可能滞后于官方能力——实例：2026-09-18 deepseek-flash 被标 images=no 而官方已原生多模态。冲突时按规则 10 以官方文档+实际模型事件为准；模型 id 是否可传仍以 `omp models` 为准。）
> 委派 prompt 附带 OMP 参数：`--model=... --thinking=max --mode=json --cwd=<worktree>`，
> worker 用 `--approval-mode=write`（仅限授权 worktree），reviewer 用 `--approval-mode=always-ask`。

## 流程

### 常规任务（ordinary）

```
Operator 分解任务（实质决策）
    → 决策对抗审查（subagent 三问；通过才派发，修正后须复审）
    → Worker（第一顺位）实现
    → Operator 审查产出（必做，不跳过）
    → 通过 → 合入
    → 有疑问/高风险 → 并行启用第一/第二顺位 Reviewer 交叉审核
        → Operator 裁决采纳或打回重做
```

### 重大任务（重大功能、架构升级、重构、迭代）

```
Operator 分解为 2-3 个独立 slice（实质决策）
    → 决策对抗审查（同上）
    → 并行启用 2-3 个 Worker 各自实现（每 slice 一个，明确接口契约）
    → Operator 逐个审查
    → 并行启用 Reviewer 第一+第二顺位交叉审核
    → Operator 与 Reviewer(s) 交叉采纳（不一致时 Operator 裁决，理由写回）
```

## operator 决策对抗审查（2026-09-18 用户规则的执行解释）

**决策类别**（非穷尽枚举，兜底见下）：任务分解与 slice 切分；任务书/契约定稿；侦查与诊断方向选择；worker 异常处置（卡死、被拒、转派）；修复方案选择；测试与回归范围选择；任务级验收裁决（置 done、发布判定——不含 operator 对产出的每次中间审阅）；安装/发布/回滚关口；版本账目判定。

- 纯机械执行（读文件、按既定命令跑验证）不构成决策；**边界情形默认按决策处理**；判定为非决策而跳过审查的，在任务 plan/events 留一行理由。
- 时点：执行前。安装/发布/tag 等不可逆关口**绝不豁免**。
- 修正后必须复审（至少针对修改点）；operator 与审查者僵持最多 2 轮后走规则 6 裁决并记录。
- 紧急止损窄豁免：仅限停止失控进程、恢复备份、撤销危险写盘类动作，可先执行、同会话内补审并记录。
- 记录：每次决策审查落一条记录（任务 id、被审决策、审查者与模型证据、verdict、修正内容），复用任务 events 的 REVIEW 事件形态或任务 receipts；无记录视为未审查。
- 批量与成本：**决策链 = 同任务同阶段内相互依赖的决策序列**，可合并一次审查（建议按侦查定案/实现方案/验收裁决/发布关口分批），但每个决策都必须被覆盖；审查任务书有界，复审不重复派发无新信息的审查，不启动空任务消耗额度。
  （流程图只画派发段示意；验收裁决、安装/发布/回滚类决策同样须在执行前过审，见上文类别与时点。）

## 规则

1. **Worker 先审后收**：任何 worker 产出必须先经 Operator 审查，不得直接合入。
2. **Reviewer 只在必要时启用**：常规小改动不强制 reviewer；涉及行为契约/架构/性能/联机
   风险时启用（必要时 = 第一顺位，仍有疑问 = 加第二顺位）。
   （本条只管 **worker 产出的事后审查**；operator 决策的**事前**对抗审查见第 9 条，无小任务豁免。）
3. **并行度**：重大任务 2-3 个 worker 并行；每个 worker 只碰自己的 slice，跨 slice 契约
   由 Operator 在委派前定死。
4. **源码参考**：游戏逻辑一律查 `game-source/Assembly-CSharp/`（带注释版），
   版本标注见 `game-source/README.md`。worker 不知道源码位置时先读它。
5. **验收证据**：worker/reviewer 结论必须带可验证证据（编译输出、日志、游戏内现象），
   不接受口头自述。
6. **冲突裁决**：Operator 与 reviewer（含决策对抗审查者）结论冲突时，Operator 裁决，但必须记录理由
   （写入任务 plan 或 events）。
7. **验证分流**：IL2CPP 是默认且唯一必测端；Mono 只在用户明确要求或直接触及 Mono 源码时验证。
8. **checklist 同步**：任务完成更新 `harness-checklist.json`，进展写 `progress.md`；
   “待实测/审核中”的 item 不得置为 done。
9. **operator 决策事前对抗审查（2026-09-18 用户规则）**：每个实质决策（类别与细则见
   「operator 决策对抗审查」节）执行前必须经一个 GLM 5.3 max 强度的对抗性 subagent
   审查三问——是否正确、是否最优、是否引入新 bug 及如何解决；修正后复审、僵持两轮走
   规则 6；每次审查落记录（含模型证据）；紧急止损窄豁免除外。主 agent 非 GLM 5.3 max
   时改派本地 ZCode/OMP 请求 GLM 5.3 max 并核验实际模型，不可用先诊断说明。
10. **素材/美术制作路由（2026-09-18 用户规则；同日依官方文档事实修订）**：可用 OMP
   DeepSeek Flash（thinking=max）承担素材设计与制作（用户授权"可以"使用，不强制改线）。
   截至 2026-09-18 官方现役模型 id 为 `deepseek-flash`（=V4.1 Flash，原生多模态、支持
   图像输入）；`deepseek-v4-flash` / `deepseek-v4-flash-vision-exp` 已下线，仅为临时
   兼容别名，不再选用；`deepseek-v4-pro` 不支持图像。本机 OMP 的能力标志（images 等）
   可能滞后于官方能力：看图任务以 DeepSeek 官方文档与实际模型事件核验为准。若 OMP 因
   陈旧标志拒绝附图：更新 OMP 或修正其模型目录属**系统变更，须先获用户授权**，并在
   核验附图真实可用后再派发；未授权期间看图素材任务改走内置 subagent 或降为 text-only
   派发，不因等系统变更硬阻塞素材线。可与 operator 对抗性交互迭代或双审（双方独立
   产出后互评择优）。纯脚本/程序化素材管线（如 draw_preview.py 类）仍按 worker 分时路由。

## 委派模板（worker）

```
任务：<slice 描述>
源码参考：game-source/Assembly-CSharp-2.1.0/<相关文件>（Mono 2.1.0 参考版）；
il2cpp 补丁主线在 il2cpp/（.NET 8 / BepInEx 6 / Il2CppInterop / HarmonyX，现代 C# 语法）
契约：<输入/输出/接口签名，由 Operator 定死>
验收：<可观察结果 + 验证命令>
约束：跳过构建/测试以外的仪式；IL2CPP 主线不引入新依赖；仅当任务触及根目录 Mono 源码时
才追加 C# 5 语法 + Harmony 1.2 约束
派发北京时间：<Asia/Shanghai 时间及星期>
命中时段：<工作日上午 / 工作日下午 / 其余>
Worker：<上午 ZCode→OMP GLM 5.3 max；下午内置 subagent；其余 OMP DeepSeek Flash max>
实际模型证据：<公开 runtime/model event；不可用先诊断，不静默替换>
```

## 委派模板（reviewer）

```
审查对象：<worker 产出/任务 id>
重点：<验收标准逐条核对 / 越界检查 / 风险点>
输出：approved / changes_requested（附证据）/ blocked
模型：可用独立内置 subagent；若使用外部 CLI，另行核验实际模型。Reviewer 不受 worker 时段路由限制。
```

## 委派模板（决策对抗审查，2026-09-18 起 operator 每个实质决策必用）

```
被审决策：<决策内容、背景与备选（任务 id、选项、理由）>
审查三问（逐字）：这个决策是否正确？是否最优？是否引入新的bug并如何解决？
输出：verdict（通过 / 修正后通过 / 打回）+ 三问逐条结论 + 证据（文件/行号/实测）
模型：GLM 5.3 / thinking=max（主 agent 为 GLM 5.3 max 时用内置 subagent；
      否则改派本地 ZCode/OMP 请求 GLM 5.3 max 并按 invoke-coding-agents 核验实际模型事件）
记录：落任务 events（REVIEW 形态）或 receipts，含模型证据与修正内容
```
