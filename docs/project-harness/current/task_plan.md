# Current Task Plan

- Item: `ninja-runtime-003`
- Status: `doing`
- Owner: `codex`（实现/日志审计）与 `user`（游戏内行为验收）
- Session: `codex-2026-08-15-ninja-runtime-003`
- Root cause: Greece 缺少 `ThrowingStar` / `Smokebomb` 池，NRE 中断 `Ninja.Behaviour`。
- Scope: 依赖池、成熟草丛伏击点、夜行忍者 y=1.1、希腊银行家 y=1.075。
- Review/deploy: 对象池候选已运行且相关错误为 0；三槽灌木与狂战士公开 Promote 修复均 final static reviewer APPROVED。游戏退出后已仅部署独立副本，构建/部署 SHA-256=`88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。
- Next: 用户实测三忍者同灌木占位、禁用/复用、昼夜恢复与两项缩放；同时验证狂战士 slot 1..6/第7及隐士。
- Safety boundary: do not start the game, modify Steam, or touch the shared save; do not repackage before runtime acceptance.
