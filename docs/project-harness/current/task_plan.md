# Current Task Plan

- Item: `bank-assistants-005`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-bank-assistants-005`
- Goal: 希腊保留唯一原版主银行家，并用其他四套世界外观建立纯收币助手。
- Authority: 只有 world-authority 认领/回收金币并提交国库；助手不带 Banker/Persistent/Wallet，客户端只接收同步对象。
- Scope: 4 个同步助手池、统一 2 Hz 扫描、墙外玩家金币 3 秒成熟、单一助手连续收取一批金币、近目标传送+短跑、成功拾取即时权威入账；其他三名助手在墙内分散巡逻并于端点停留。墙内金币不分给助手，主银行家保持原生状态机与墙内边界，同时使用增强移速、动态覆盖左右城墙的扫描范围和安全期全天工作；共享账本按真实日息/存取时点同步。
- Validation: 四套控制器与四助手生成的首次回归已通过；本轮单收集者、巡逻停顿、主银行家墙内动态扫描、增强移速及安全重新出现逻辑获独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error。构建与独立测试副本 DLL SHA-256 均为 `91B6FDB52831BAA15B14E54B047F989E3B7639FC3DCE856A0522F4472AF41B62`，待游戏内实测；未触碰 Steam、共享存档或正式 zip。
- Plan: `docs/project-harness/tasks/bank-assistants-005/plan.md`
