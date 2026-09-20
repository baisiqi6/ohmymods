# 伤害可靠性与计算复用

用户明确要求修复前次审查的漏伤/开销风险，并检查把火枪弹丸接入共同伤害流程。当前baseline0b1ad4e1（侧架+ammo hold），游戏运行时不得替换DLL，不覆盖存档配置，不启动/commit/push/publish。

原生Arrow.TryDamage和火枪已经共用Damageable.ReceiveDamage；本轮复用这个原生扣血/护盾/事件入口，用一个很小的同步提交helper统一自有子弹与附加AoE，绝不新建延迟伤害队列或把全场原生箭拦入额外管线，也不将子弹套成会重力/燃烧/反弹的原生Arrow。

固定跨slice API（namespace KingdomEnhancedMod）：`internal enum CombatDamageResult { Skipped, Submitted, Faulted }`；`internal static class CombatDamage { internal static CombatDamageResult Submit(Damageable target, int damage, GameObject source, DamageSource kind); }`。只做authority/当前有效存活目标等共同只读门，并最终调用原生ReceiveDamage；保留原生护盾/无敌/事件，捕获一次异常标Faulted，不重试可能已部分执行的伤害，不把Submitted当HP必减。调用方先消费本弹/记本次AoE去重，然后提交；一个目标回调异常不能阻塞无关弹丸/后续有效目标。普通原生直伤不改。

1. life worker：GreekImpact目标去重增加可靠目标生命标识，优先仅给实际被AoE处理的目标添加无Update的自有生命周期marker，真实GO停用/启用区分新life；不可用时间/位置/HP猜新life。不新增短getter/native detour或全场扫，不写HP/原生存档/全局换皮；初始化注册可暴露CombatTargetLife.Initialize()由root接入Plugin.Init。新增CombatDamage.cs与CombatTargetLife.cs，自有AoE调用共同Submit，异常隔离，保留半径.25、额外1、直接目标排除与同life同齐射一次、既有32目标规则。
2. bullet worker：只改MusketeerCombat，复用共同Submit；原生Linecast结果列表可复用并由Unity扩容（实际2.4 interop先核），去掉256结果饱和直接吞弹的路径，最近有效地面目标仍阻挡，不能假设结果有序/穿过前排免疫。复用弹丸记录而非每发new；移除delta>.1只推进.1造成的墙钟慢弹，完整dt按射程/寿命钳制并扫掠，保持暂停/世界/视觉生命周期。不可把伤害值、射程/射速/普通箭上限更改。物理API真的失败仍明确保守处理，不能伪造命中。
3. fx worker：作者公式中同一Time.time的36组Perlin输入跨层跨效果完全相同，缓存一次只算72次noise，保留相位/三层/色彩/寿命/像素形状。Unity Mathf.Round使用ties-even，可等价MathF.Round减少native往返；只在量化顶点确实改变时上传；bounds用证明覆盖所有局部顶点的固定包围盒，避免逐帧重算。不得靠降低伤害查询频率或删特效冒充优化，不改PNG。测试对比原公式像素/动画输出与调用次数，不把stub耗时当实机FPS。

三个worker为本机OMP deepseek/deepseek-flash thinking=max，隔离快照且不重叠源码allowlist；已实时核OMP18.1.19/models/max并无工具smoke证明真实provider/model。内部reviewer独立审方案和最终差异；root整合共享模块、实际2.4全build、针对回归与静态调用量比较、方法资源审计及现授权本机候选交付。真正高密度战斗/组件注入生命周期/主客机伤害与帧耗时仍需游戏验证，禁止宣称已实测。

Primary API evidence: Unity2022.3 Physics2D.Linecast results List自动扩容并可复用：https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics2D.Linecast.html 。Mathf.Round ties-to-even：https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mathf.Round.html 。只采用这些官方API说明，不复制第三方实现。
