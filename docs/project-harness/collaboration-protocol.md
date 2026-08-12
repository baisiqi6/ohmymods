# 协作模式规范（Collaboration Protocol）

> 约定日期：2026-08-12。本项目的固定协作流程，所有 session/agent 遵守。
> 不调用外部 coding agent CLI（Claude Code/Qoder 等）——**只用当前 agent 的 subagent**。

## 角色与模型顺位

| 角色 | 职责 | 第一顺位 | 第二顺位 |
|------|------|----------|----------|
| **Operator** | 主控：分解任务、审查 worker 产出、裁决采纳、交叉审核 reviewer 结论 | 当前主 agent | — |
| **Worker** | 实现：按任务规范编码、自测 | deepseek V4 flash（thinking=max） | GLM 5.2（thinking=max）、minimax m3（thinking=high） |
| **Reviewer** | 审查：验证验收标准、找越界/缺陷/测试缺口 | kimi K3（thinking=max） | GLM 5.2（thinking=max） |

> 模型参数（thinking=max/high）在委派任务时随 prompt 指定；subagent 实际运行模型由
> harness 环境决定，本表是**意图约定**。

## 流程

### 常规任务（ordinary）

```
Operator 分解任务 → Worker（第一顺位）实现
    → Operator 审查产出（必做，不跳过）
    → 通过 → 合入
    → 有疑问/高风险 → 并行启用第一/第二顺位 Reviewer 交叉审核
        → Operator 裁决采纳或打回重做
```

### 重大任务（重大功能、架构升级、重构、迭代）

```
Operator 分解为 2-3 个独立 slice
    → 并行启用 2-3 个 Worker 各自实现（每 slice 一个，明确接口契约）
    → Operator 逐个审查
    → 并行启用 Reviewer 第一+第二顺位交叉审核
    → Operator 与 Reviewer(s) 交叉采纳（不一致时 Operator 裁决，理由写回）
```

## 规则

1. **Worker 先审后收**：任何 worker 产出必须先经 Operator 审查，不得直接合入。
2. **Reviewer 只在必要时启用**：常规小改动不强制 reviewer；涉及行为契约/架构/性能/联机
   风险时启用（必要时 = 第一顺位，仍有疑问 = 加第二顺位）。
3. **并行度**：重大任务 2-3 个 worker 并行；每个 worker 只碰自己的 slice，跨 slice 契约
   由 Operator 在委派前定死。
4. **源码参考**：游戏逻辑一律查 `game-source/Assembly-CSharp/`（带注释版），
   版本标注见 `game-source/README.md`。worker 不知道源码位置时先读它。
5. **验收证据**：worker/reviewer 结论必须带可验证证据（编译输出、日志、游戏内现象），
   不接受口头自述。
6. **冲突裁决**：Operator 与 reviewer 结论冲突时，Operator 裁决，但必须记录理由
   （写入任务 plan 或 events）。
7. **checklist 同步**：任务完成更新 `harness-checklist.json`，进展写 `progress.md`。

## 委派模板（worker）

```
任务：<slice 描述>
源码参考：game-source/Assembly-CSharp/<相关文件>（版本 2.0.1 mono）
契约：<输入/输出/接口签名，由 Operator 定死>
验收：<可观察结果 + 验证命令>
约束：跳过构建/测试以外的仪式；C# 5 语法；Harmony 1.2；不引入依赖
模型：deepseek V4 flash thinking=max（或第二顺位）
```

## 委派模板（reviewer）

```
审查对象：<worker 产出/任务 id>
重点：<验收标准逐条核对 / 越界检查 / 风险点>
输出：approved / changes_requested（附证据）/ blocked
模型：kimi K3 thinking=max（或第二顺位 GLM 5.2）
```
