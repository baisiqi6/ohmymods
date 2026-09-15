using System;
using KingdomEnhancedMod;

namespace FriendlyTrollDisguiseTests
{
    /// <summary>
    /// 后缀重选：仅当原生返回“受保护的友好巨魔”时才只读候选表复算；
    /// 覆盖最近后备、原 ignore/condition 规则、严格 range、顺序 tie、无合法目标、
    /// 普通结果保持、表只读、客户端不改写、读取失败兜底。
    /// </summary>
    internal static class ReselectTests
    {
        private static Damageable ReselectPriority(TargetCacher cache, Damageable nativeResult,
            float pos, float range,
            TargetCacher.SearchConditionDelegate condition = null,
            TargetCacher.SearchConditionDelegate ignore = null)
        {
            return FriendlyTrollDisguise.ReselectClosestTarget(cache, nativeResult, pos, range,
                condition, ignore, false);
        }

        internal static void Run()
        {
            Case.Run("普通原生结果原样返回（不读表、不调委托）", () =>
            {
                var cache = new TargetCacher();
                Damageable native = Fixture.Target(10f);
                cache._trollPriorityTargets.Add(Fixture.Target(1f));
                Fixture.ResetListCounters(cache);

                int conditionCalls = 0;
                int ignoreCalls = 0;
                Damageable result = ReselectPriority(cache, native, 0f, 20f,
                    d => { conditionCalls++; return true; },
                    d => { ignoreCalls++; return false; });

                Check.Same(native, result, "普通结果必须保持不动");
                Check.Equal(0, conditionCalls, "普通结果不该触发候选扫描");
                Check.Equal(0, ignoreCalls, "普通结果不该触发候选扫描");
                Check.Equal(0, cache._trollPriorityTargets.Reads, "普通结果不该读候选表");
                Check.Equal(0, cache._trollPriorityTargets.Writes, "候选表不该被写");
            });

            Case.Run("未伪装友好巨魔当选 → 保持不动（仍可被敌人选中）", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit plain = Fixture.Friendly(-1, x: 8f);
                cache._trollPriorityTargets.Add(Fixture.Target(2f));
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, plain.Damageable, 0f, 20f);

                Check.Same(plain.Damageable, result, "未伪装友好巨魔不属于受保护目标");
                Check.Equal(0, cache._trollPriorityTargets.Reads, "不该扫描候选表");
            });

            Case.Run("原生返回受保护巨魔 → 重选最近普通候选", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(2, maskRenderer: true, x: 3f);
                cache._trollPriorityTargets.Add(disguised.Damageable);
                Damageable near = Fixture.Target(6f);
                Damageable far = Fixture.Target(9f);
                cache._trollPriorityTargets.Add(far);
                cache._trollPriorityTargets.Add(near);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f);

                Check.Same(near, result, "应选最近的非受保护候选");
                Check.Equal(0, cache._trollPriorityTargets.Writes, "候选表不该被写");
                Fixture.AssertNativeFieldsUntouched(disguised, "重选");
            });

            Case.Run("受保护候选被跳过（即使更近）", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit nativeResult = Fixture.Friendly(1, maskRenderer: true, x: 2f);
                FriendlyUnit otherDisguised = Fixture.Friendly(-1, x: 4f);
                PatchDivine_HermesHeadwear.DisguiseHeadwear = true;
                cache._trollPriorityTargets.Add(nativeResult.Damageable);
                cache._trollPriorityTargets.Add(otherDisguised.Damageable);
                Damageable ordinary = Fixture.Target(7f);
                cache._trollPriorityTargets.Add(ordinary);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, nativeResult.Damageable, 0f, 20f);

                Check.Same(ordinary, result, "受保护候选（含头饰委托命中的）都应跳过");
                Check.Equal(0, cache._trollPriorityTargets.Writes, "候选表不该被写");
            });

            Case.Run("严格 distance < range：恰好等于 range 不算", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(3, maskRenderer: true, x: 1f);
                Damageable edge = Fixture.Target(10f);
                cache._trollPriorityTargets.Add(edge);
                Fixture.ResetListCounters(cache);

                Damageable atRange = ReselectPriority(cache, disguised.Damageable, 0f, 10f);
                Check.Null(atRange, "距离恰好等于 range 的候选不算（原生 num = range 水位）");

                Damageable inside = ReselectPriority(cache, disguised.Damageable, 0f, 10.5f);
                Check.Same(edge, inside, "距离严格小于 range 的候选应命中");
            });

            Case.Run("顺序 tie：同距离时列表中先出现者胜", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true, x: 1f);
                Damageable first = Fixture.Target(5f);
                Damageable second = Fixture.Target(5f);
                cache._trollPriorityTargets.Add(disguised.Damageable);
                cache._trollPriorityTargets.Add(first);
                cache._trollPriorityTargets.Add(second);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f);

                Check.Same(first, result, "严格小于才替换 → 先出现者保持");
            });

            Case.Run("ignore 委托保留（被忽略的更近候选跳过）", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(4, maskRenderer: true, x: 0.5f);
                Damageable ignored = Fixture.Target(2f);
                Damageable allowed = Fixture.Target(8f);
                cache._trollPriorityTargets.Add(ignored);
                cache._trollPriorityTargets.Add(allowed);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f,
                    condition: null,
                    ignore: d => ReferenceEquals(d, ignored));

                Check.Same(allowed, result, "ignore 命中的候选必须跳过");
            });

            Case.Run("condition 委托保留（条件不满足的更近候选跳过）", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(4, maskRenderer: true, x: 0.5f);
                Damageable rejected = Fixture.Target(2f);
                Damageable accepted = Fixture.Target(8f);
                cache._trollPriorityTargets.Add(rejected);
                cache._trollPriorityTargets.Add(accepted);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f,
                    condition: d => ReferenceEquals(d, accepted));

                Check.Same(accepted, result, "condition 未通过的候选必须跳过");
            });

            Case.Run("原生求值顺序：ignore 先于距离，condition 后于距离", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(4, maskRenderer: true, x: 0.5f);
                Damageable outOfRange = Fixture.Target(50f);
                cache._trollPriorityTargets.Add(outOfRange);
                Fixture.ResetListCounters(cache);

                int ignoreCalls = 0;
                int conditionCalls = 0;
                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 10f,
                    condition: d => { conditionCalls++; return true; },
                    ignore: d => { ignoreCalls++; return false; });

                Check.Null(result, "表内没有合法候选");
                Check.Equal(1, ignoreCalls, "原生先问 ignore（越界候选也要问）");
                Check.Equal(0, conditionCalls, "原生在距离判定之后才问 condition");
            });

            Case.Run("无合法候选 → null（绝不交出受保护巨魔）", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(5, maskRenderer: true, x: 1f);
                cache._trollPriorityTargets.Add(disguised.Damageable);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f);

                Check.Null(result, "表里只有受保护者 → 无目标");

                var empty = new TargetCacher();
                Fixture.ResetListCounters(empty);
                Check.Null(ReselectPriority(empty, disguised.Damageable, 0f, 20f), "空表 → 无目标");
            });

            Case.Run("候选表只读：内容与顺序不变", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(2, maskRenderer: true, x: 1f);
                Damageable a = Fixture.Target(4f);
                Damageable b = Fixture.Target(6f);
                cache._trollPriorityTargets.Add(disguised.Damageable);
                cache._trollPriorityTargets.Add(a);
                cache._trollPriorityTargets.Add(b);
                Fixture.ResetListCounters(cache);

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f);
                Check.Same(a, result, "应选 a");

                Check.Equal(3, cache._trollPriorityTargets.Count, "元素数不变");
                Check.Same(disguised.Damageable, cache._trollPriorityTargets[0], "顺序不变[0]");
                Check.Same(a, cache._trollPriorityTargets[1], "顺序不变[1]");
                Check.Same(b, cache._trollPriorityTargets[2], "顺序不变[2]");
                Check.Equal(0, cache._trollPriorityTargets.Writes, "不写候选表");
            });

            Case.Run("低优先级路径只读低优先表", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(2, maskRenderer: true, x: 1f);
                Damageable decoy = Fixture.Target(1.5f);
                Damageable candidate = Fixture.Target(9f);
                cache._trollPriorityTargets.Add(decoy);
                cache._trollLowPriorityTargets.Add(candidate);
                Fixture.ResetListCounters(cache);

                Damageable result = FriendlyTrollDisguise.ReselectClosestTarget(cache,
                    disguised.Damageable, 0f, 20f, null, null, true);

                Check.Same(candidate, result, "低优先路径应读 _trollLowPriorityTargets");
                Check.Equal(0, cache._trollPriorityTargets.Reads, "不应读优先表");
                Check.True(cache._trollLowPriorityTargets.Reads > 0, "应读低优先表");
                Check.Equal(0, cache._trollLowPriorityTargets.Writes, "不写低优先表");
            });

            Case.Run("客户端（无世界权威）不改写结果", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true, x: 1f);
                cache._trollPriorityTargets.Add(Fixture.Target(2f));
                Fixture.ResetListCounters(cache);
                NetworkBigBoss.HasWorldAuth = false;

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f);

                Check.Same(disguised.Damageable, result, "客户端保持原生结果");
                Check.Equal(0, cache._trollPriorityTargets.Reads, "客户端不该扫描候选表");
            });

            Case.Run("ModConfig.Enabled=false 不改写结果", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true, x: 1f);
                cache._trollPriorityTargets.Add(Fixture.Target(2f));
                Fixture.ResetListCounters(cache);
                ModConfig.Enabled.Value = false;

                Damageable result = ReselectPriority(cache, disguised.Damageable, 0f, 20f);

                Check.Same(disguised.Damageable, result, "开关关闭保持原生结果");
                Check.Equal(0, cache._trollPriorityTargets.Reads, "开关关闭不该扫描候选表");
            });

            Case.Run("原生结果为 null → null", () =>
            {
                var cache = new TargetCacher();
                Fixture.ResetListCounters(cache);
                Check.Null(ReselectPriority(cache, null, 0f, 20f), "空结果原样返回");
                Check.Equal(0, cache._trollPriorityTargets.Reads, "不该扫描候选表");
            });

            Case.Run("cache 为 null → null（已确认受保护者的兜底）", () =>
            {
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true);
                Check.Null(FriendlyTrollDisguise.ReselectClosestTarget(null, disguised.Damageable,
                    0f, 20f, null, null, false), "无候选表 → 无目标");
            });

            Case.Run("候选表为 null → null", () =>
            {
                var cache = new TargetCacher();
                cache._trollPriorityTargets = null;
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true);
                Check.Null(ReselectPriority(cache, disguised.Damageable, 0f, 20f),
                    "候选表缺失 → 无目标");
            });

            Case.Run("候选表读取抛错 → null", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true);
                cache._trollPriorityTargets.Add(Fixture.Target(3f));
                Fixture.ResetListCounters(cache);
                cache._trollPriorityTargets.ThrowOnRead = true;

                Check.Null(ReselectPriority(cache, disguised.Damageable, 0f, 20f),
                    "读取失败绝不能退回受保护巨魔");
            });

            Case.Run("condition 委托抛错 → null", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true);
                cache._trollPriorityTargets.Add(Fixture.Target(3f));
                Fixture.ResetListCounters(cache);

                Check.Null(ReselectPriority(cache, disguised.Damageable, 0f, 20f,
                    condition: d => throw new InvalidOperationException("condition boom")),
                    "委托抛错后必须返回 null");
            });

            Case.Run("ignore 委托抛错 → null", () =>
            {
                var cache = new TargetCacher();
                FriendlyUnit disguised = Fixture.Friendly(0, maskRenderer: true);
                cache._trollPriorityTargets.Add(Fixture.Target(3f));
                Fixture.ResetListCounters(cache);

                Check.Null(ReselectPriority(cache, disguised.Damageable, 0f, 20f,
                    ignore: d => throw new InvalidOperationException("ignore boom")),
                    "ignore 抛错后必须返回 null");
            });
        }
    }
}
