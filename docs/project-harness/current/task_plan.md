# Current Task Plan

- Item: `save-repair-012`
- Status: `doing`
- Owner: `codex`
- Session: `codex-2026-08-15-save-repair-012`
- Goal: 对当前campaign1/land7做一次性、有指纹约束的普通居民减员，从383名零币、无引用、安全希腊Peasant中删除createOrder最高的350名。
- Authority: 用户已明确授权直接存档修复；仅在游戏退出、即时备份、严格校验和原子替换条件下执行，存档不入Git。
- Scope: 仅删除350名`Prefabs/Characters/Peasant`；真正Worker、北境Peasant、携币/被抓/异常/有引用单位以及所有非目标数据零改动。
- Validation: worker/reviewer最终APPLY_APPROVED；dry-run与Apply均为Worker14不变、Peasant733→383、Greek638→288、Norse95不变、删除350、Beggar10/5+5不变。备份748730 bytes/SHA=2C681C5C...88F87A；最终728071 bytes/SHA=63884D91...93FED；临时文件0。等待游戏内读档与性能体感验证。
- Plan: `docs/project-harness/tasks/save-repair-012/plan.md`
