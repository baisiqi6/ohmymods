# Current Task Plan

- Item: `ninja-runtime-003`
- Status: `doing`
- Owner: `codex`（实现/日志审计）与 `user`（游戏内行为验收）
- Session: `codex-2026-08-15-ninja-runtime-003`
- Root cause: Greece 缺少 `ThrowingStar` / `Smokebomb` 池，NRE 中断 `Ninja.Behaviour`。
- Scope: 依赖池、成熟草丛伏击点、夜行忍者 y=1.1、希腊银行家 y=1.075。
- Review/deploy: 对象池候选已运行且相关错误为 0；三槽灌木上一轮获 final static reviewer APPROVED。最新综合候选已仅部署独立副本，构建/部署 SHA-256=`6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`；用户确认忍者当前行为无明显问题、逻辑自洽，灌木 `±0.55` 间距观感合适。
- Next: 补充树砍伐、帐篷摧毁解绑与跨侧池复用的边界回归；同时验证狂战士 slot 1..6/第7、隐士和设置面板，之后重打候选包。
- Safety boundary: do not start the game, modify Steam, or touch the shared save; do not repackage before runtime acceptance.
