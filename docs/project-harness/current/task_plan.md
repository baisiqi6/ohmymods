# Current Task Plan

- Item: `save-repair-011`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-save-repair-011`
- Goal: 对当前campaign1/land7异常存档做一次性、有指纹约束的乞丐人口修复，左右营地各保留5名并删除148名无引用普通乞丐。
- Authority: 用户已明确授权直接存档修复；仅在游戏退出、即时备份、严格校验和原子替换条件下执行，存档不入Git。
- Scope: 仅当前global-v35中land7的Beggar对象数组；其他角色、岛屿、任务、网络与经济数据零改动。
- Validation: worker与reviewer最终APPLY_APPROVED；dry-run与Apply均为158→10、删除148、5/5。备份751068 bytes/SHA=68D4F779...724B16；最终748730 bytes/SHA=2C681C5C...88F87A；独立复读land7 objects=2046、Beggar=10、临时文件0。等待游戏内读档与硬cap验证。
- Plan: `docs/project-harness/tasks/save-repair-011/plan.md`
