# Current Task Plan

- Item: `bank-assistants-005`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-bank-assistants-005`
- Goal: 希腊保留唯一原版主银行家，并用其他四套世界外观建立纯收币助手。
- Authority: 只有 world-authority 认领/回收金币并提交国库；助手不带 Banker/Persistent/Wallet，客户端只接收同步对象。
- Scope: 4 个同步助手池、统一 2 Hz 扫描、墙外玩家金币 3 秒成熟、近目标传送+短跑、成功拾取即时权威入账、满载/无目标回城不重复入账；墙内金币不分给助手，主银行家恢复 2.4.0 原生近距扫描/速度/作息，并修正共享账本的日息/提款同步时点。
- Validation: 首次独立副本实测发现四套控制器加载失败并统一回退、调度可能被首个暂不可认领金币阻塞；修复版独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error；当前桌面无权覆盖 E 盘测试 DLL，等待 operator 手动部署复测，不触碰 Steam、共享存档或正式 zip。
- Plan: `docs/project-harness/tasks/bank-assistants-005/plan.md`
