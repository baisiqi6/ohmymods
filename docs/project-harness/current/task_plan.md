# Current Task Plan

- Item: `bank-assistants-005`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-bank-assistants-005`
- Goal: 希腊保留唯一原版主银行家，并用其他四套世界外观建立纯收币助手。
- Authority: 只有 world-authority 认领/回收金币并提交国库；助手不带 Banker/Persistent/Wallet，客户端只接收同步对象。
- Scope: 4 个同步助手池、统一 2 Hz 扫描、主银行家仅管理左右从城堡向外数第二道墙之间、该区域外的玩家金币 3 秒成熟后由单一助手连续收取；同批后续金币距助手不超过 6 单位则直接跑，超出才近距传送。其余助手墙内巡逻并于端点停留；北境助手 y=1.2。主银行家保留增强移速、安全期全天工作与第二道墙区动态扫描；共享账本按真实日息/存取时点同步。
- Validation: 本轮已获独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error，DLL SHA-256=`51BFFEEF87FCC6846AF4FB253270DD0F6FE50C814DF7EBD3596A5320F8C8013B`。独立测试副本当前正在运行，故尚未覆盖 DLL；不触碰 Steam、共享存档或正式 zip。
- Plan: `docs/project-harness/tasks/bank-assistants-005/plan.md`
