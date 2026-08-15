# Current Task Plan

- Item: `bank-assistants-005`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-bank-assistants-005`
- Goal: 将 Dead Lands 银行助手视觉高度绝对设为 1.25，北境保持 1.2，并保持现有单 collector 轮转调度。
- Authority: 缩放在主客双方相同 prefab 构建路径确定性执行；只有 world-authority 运行收币、轮转与国库写入。
- Scope: 仅调整 Dead Lands prefab localScale.y；不改 x/z、控制器、同步池、经济提交、单collector或round-robin逻辑。
- Validation: 独立 reviewer APPROVED；Debug构建0 warning/0 error，构建、独立副本与刷新后zip内DLL SHA-256均为 `9E71AFF5B155EF6D50DCD9EB0CFBA1098824382CF2C0547FEE431D485F8376BB`；zip SHA-256=`7F736F339F22AFBC7FCD00659863167753A91B566643CD4818F6401CCFB42ADC`。待实机观察Dead Lands比例。
- Plan: `docs/project-harness/tasks/bank-assistants-005/plan.md`
