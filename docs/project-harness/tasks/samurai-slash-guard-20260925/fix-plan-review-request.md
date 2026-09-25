角色：独立对抗 reviewer，limited-fresh 设计审查。模型 GLM-5.3 thinking=max。
目标：玩家要求风格2武士 CD就绪且1.5..8.2格有敌时，固定冲出7格，再反斩冲回出发点；全程无敌，八道白色定格残影2秒。目标生死不影响行程。
cwd/基线：C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix @67d39b9。允许读取此树及 C:/Users/ADMIN/projects/ohmymods/game-source、C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/samurai-slash-guard-20260925/zcode-handoff.md。允许修改：无。禁止 edit/write/commit/push/merge/deploy/启动游戏/写存档/派子代理。
请独立读实际代码：il2cpp/PatchRoles_SamuraiPowerDash.cs、SamuraiDashVisuals.cs；tests/samurai-motion、samurai-visuals 的生产链接与桩；原生 Knight.GoToWall/SetRetreating/Update（旧版本仅参考，标清）。
拟议方案（尚未执行）：
1. 保留完整 Eligible 作启动/旧 Return 门；ChoreoRoutine和Tick存续用硬失效子集（enabled/active、ModConfig、HasWorldAuth、style2、character非空非inert非grabbed、damageable活、mover同实例）；原生isRetreating/isCharging/_shouldCharge/GetFormation/FSM变动不使正在执行的两腿结束。保证Tick不先通过Eligible杀死Choreo；3秒总预算、每腿1.2秒、OnDisable/异常收尾仍有效。
2. step=ineligible 增加具体失效原因，日志预算不扩大。撤退动画暂不主动反写，先查确切行为风险；不得扩展成全局SetRetreating(false)。
3. 白影通过烘焙RGB255/原alpha texture+Sprite实现，保持rect/pivot/PPU/flip/排序；源贴图不可读时RenderTexture/Blit/ReadPixels兜底，finally归还active RT并释放临时资源。缓存必须有明确上限/销毁，避免每帧整atlas读回，保持原贴图/材质零修改。注意atlas/tight/packed sprite、材质mainTexture与SpriteRenderer绑定、失败的回退不能谎称白影成功。
4. 测试需验证真实循环中撤退翻真仍完整两腿，触发门仍拒绝撤退，硬失效正确清理。白影测试验证RGB255/alpha原值、缓存复用、失败资源归还。做临时退回旧门/旧贴图的红验证。
三问逐一回答：是否正确？是否为足够简单的最优局部方案？是否引入新bug？给唯一 APPROVE/CHANGES_REQUESTED/BLOCKED，P0-P3 finding含代码定位、最小反例和最小修正。不要为审查制造假想问题。无法证明的游戏效果标UNVERIFIED。
