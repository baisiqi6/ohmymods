using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 「伪装中的友好巨魔」判定与目标重选。
///
/// 语义（用户合同）：转化的友好巨魔只要戴了原生面具、跨世界面具、周年派对面具或帽，
/// 就不被敌人主动选中/追击。<b>只改选择</b>：范围 / 碰撞 / 既有伤害逻辑一律不动，
/// 无敌 / 生命 / 护甲 / 伤害均不写。所有世界生效，只随 <see cref="ModConfig.Enabled"/>，
/// 无额外面板开关。
///
/// 两条独立资格路径（互不覆盖）：
/// <list type="bullet">
/// <item>原生面具：<c>_maskIndex</c> 0..5（五世界组 × 6 款）且 <c>_mask</c> renderer
/// enabled + active + sprite 非 null。</item>
/// <item>自有 44 款头饰：由 Operator 侧 <c>PatchDivine_HermesHeadwear.HasDisguiseHeadwear</c>
/// 返回“当前世代有效且已显示”。自有帽会把原生 <c>_mask.enabled</c> 置 false
/// （见 <see cref="HermesHeadwearVisuals.OnNativeMaskSpawned"/>），此时第一条路径失效、
/// 第二条路径仍为 true——所以是「或」而不是「与」。</item>
/// </list>
///
/// 硬约束（勿违反）：
/// <list type="bullet">
/// <item>不改 <c>MaskIndex</c> / 任何原生字段，不 spawn / destroy 原生对象。</item>
/// <item>不缓存资格：池复用、开关切换、换岛（world/scene 换代）后必须当帧重算。
/// 每次调用都是纯读取 + 现算。</item>
/// <item>读取失败（null / 已销毁 / interop 抛错）一律回退 false（未伪装 → 保持原行为）。</item>
/// <item>主机裁决：<see cref="NetworkBigBoss.HasWorldAuth"/> 为假（客户端）绝不改写结果。</item>
/// </list>
/// </summary>
internal static class FriendlyTrollDisguise
{
    /// <summary>原生面具索引上界（含）。<c>-1</c> = 无面具；<c>&gt;=6</c> = 非原生面具来源。</summary>
    internal const int MaxNativeMaskIndex = 5;

    /// <summary>
    /// 友好巨魔当前是否被伪装（不应被敌人主动选中/追击）。
    /// 现算、无缓存；任何读取失败回退 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// Operator 建议接线（本 worker 不改旧文件）：
    /// <list type="number">
    /// <item><c>PatchDivine_FriendlyTroll.IsPursuitTarget</c>：<c>if (IsProtected(friendly.Troll)) return false;</c>
    /// —— 堵住 5% 反制 TrollWeak 的转向/追击。</item>
    /// <item><c>PatchDivine_FriendlyTroll.PriorityTargetPatch.Prefix</c> 注入循环：
    /// <c>if (FriendlyTrollDisguise.IsProtected(entry.Troll)) continue;</c>
    /// —— 伪装中的友好巨魔不再被注入 <c>_trollPriorityTargets</c>。</item>
    /// </list>
    /// </remarks>
    internal static bool IsProtected(FriendlyTroll troll)
    {
        try
        {
            if (troll == null) return false;
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return false;
            if (!NetworkBigBoss.HasWorldAuth) return false;
            // IsCurrent 覆盖 activeInHierarchy + 同一 gameLayer（本岛/本世代），换岛滞留在此被挡。
            if (!OptionalQoLScope.IsCurrent(troll)) return false;
            if (!troll.enabled) return false;
            if (!IsDisguised(troll)) return false;

            // 存活检查放最后：Damageable 读取会产生 interop 包装，只在确认伪装后才付这个代价。
            return IsLive(troll);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="TargetCacher.GetClosestPriorityTargetWithinRange"/> /
    /// <see cref="TargetCacher.GetClosestLowPriorityTargetWithinRange"/> 后缀用：
    /// 仅当原生返回值本身是「受保护的友好巨魔」时才重选。
    /// </summary>
    /// <remarks>
    /// 只读对应原生候选表（绝不增删），完全按原生规则复算：
    /// 严格 <c>distance &lt; range</c>（初始水位 = range，故恰好等于 range 的候选不算）、
    /// 列表顺序即 tie-break（严格小于才替换 → 先出现的更近者胜）、
    /// <c>ignore</c> → 距离 → <c>condition</c> 的原生求值顺序，只额外跳过受保护候选。
    /// 无合法候选时返回 <c>null</c>（宁可无目标，也不把伪装中的友好巨魔交出去）。
    /// 普通原生结果（非受保护友好巨魔）原样返回，不读表、不改写。
    /// </remarks>
    internal static Damageable ReselectClosestTarget(TargetCacher cache, Damageable nativeResult,
        float pos, float range, TargetCacher.SearchConditionDelegate conditionDelegate,
        TargetCacher.SearchConditionDelegate ignoreDelegate, bool lowPriority)
    {
        if (nativeResult == null) return null;

        try
        {
            if (ModConfig.Enabled == null || !ModConfig.Enabled.Value
                || !NetworkBigBoss.HasWorldAuth)
                return nativeResult;
            if (!IsProtectedDamageable(nativeResult)) return nativeResult;
        }
        catch
        {
            return nativeResult;
        }

        // 到这里 nativeResult 已确认是受保护友好巨魔：任何后续失败都只能返回 null。
        try
        {
            if (cache == null) return null;
            Il2CppSystem.Collections.Generic.List<Damageable> candidates = lowPriority
                ? cache._trollLowPriorityTargets : cache._trollPriorityTargets;
            if (candidates == null) return null;

            Damageable best = null;
            float bestDistance = range;
            int count = candidates.Count;
            for (int i = 0; i < count; i++)
            {
                Damageable candidate = candidates[i];
                if (candidate == null) continue;
                // interop 生成的委托代理派生自 Il2CppSystem.MulticastDelegate（不是
                // System.MulticastDelegate），C# 不认它是委托类型：必须显式 Invoke。
                if (ignoreDelegate != null && ignoreDelegate.Invoke(candidate)) continue;

                float distance = Mathf.Abs(candidate.transform.position.x - pos);
                if (!(distance < bestDistance)) continue;
                if (conditionDelegate != null && !conditionDelegate.Invoke(candidate)) continue;
                if (IsProtectedDamageable(candidate)) continue;

                bestDistance = distance;
                best = candidate;
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 原生面具路径：索引 0..5、renderer enabled + active、且 renderer 确实挂在本 troll 下
    /// （池对象归属边界）、sprite 非 null。
    /// </summary>
    private static bool HasVisibleNativeMask(FriendlyTroll troll)
    {
        int index = troll._maskIndex;
        if (index < 0 || index > MaxNativeMaskIndex) return false;

        SpriteRenderer mask = troll._mask;
        if (mask == null || !mask.enabled) return false;

        GameObject maskObject = mask.gameObject;
        if (maskObject == null || !maskObject.activeInHierarchy) return false;

        // 归属边界：_mask 是池对象，字段里的旧引用可能已被别的 troll 租走（本 troll 只是
        // 残留指针）。只有挂在本 troll 层级下的 renderer 才算这具身体的伪装显示。
        Transform maskTransform = mask.transform;
        Transform trollTransform = troll.transform;
        if (maskTransform == null || trollTransform == null
            || !maskTransform.IsChildOf(trollTransform))
            return false;

        Sprite sprite = mask.sprite;
        return sprite != null && sprite.Pointer != IntPtr.Zero;
    }

    /// <summary>伪装路径合并：原生面具（或）自有 44 款头饰。自有帽隐藏原生 mask 时后者仍成立。</summary>
    private static bool IsDisguised(FriendlyTroll troll)
    {
        if (HasVisibleNativeMask(troll)) return true;
        return PatchDivine_HermesHeadwear.HasDisguiseHeadwear(troll);
    }

    /// <summary>活的 Damageable：非 null、未死、所在 GameObject active。</summary>
    private static bool IsLive(FriendlyTroll troll)
    {
        Damageable damageable = troll.GetComponent<Damageable>();
        if (damageable == null || damageable.isDead) return false;

        GameObject damageableObject = damageable.gameObject;
        return damageableObject != null && damageableObject.activeInHierarchy;
    }

    /// <summary>
    /// 候选 Damageable 是否属于受保护的友好巨魔。
    /// 关联方式与 <c>PatchDivine_FriendlyTroll</c> 的 <c>entry.Damageable = friendly.GetComponent&lt;Damageable&gt;()</c>
    /// 反向一致：同一个 GameObject 上取回 FriendlyTroll。
    /// </summary>
    private static bool IsProtectedDamageable(Damageable damageable)
    {
        if (damageable == null) return false;
        GameObject damageableObject = damageable.gameObject;
        if (damageableObject == null) return false;

        FriendlyTroll friendly = damageableObject.GetComponent<FriendlyTroll>();
        return friendly != null && IsProtected(friendly);
    }

    /// <summary>
    /// 优先级目标重选后缀。注册顺序无关的正确性来源是
    /// <see cref="HarmonyPriority"/> <c>Last</c>：排在既有
    /// <c>PatchDivine_FriendlyTroll.PriorityTargetPatch</c> 之后跑，因此此刻看到的候选表
    /// 已经是注入收尾（Deregister）后的原表。
    /// </summary>
    [HarmonyPatch(typeof(TargetCacher), nameof(TargetCacher.GetClosestPriorityTargetWithinRange))]
    private static class PriorityReselectPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(TargetCacher __instance, float pos, float range,
            TargetCacher.SearchConditionDelegate conditionDelegate,
            TargetCacher.SearchConditionDelegate ignoreDelegate, ref Damageable __result)
        {
            __result = ReselectClosestTarget(__instance, __result, pos, range,
                conditionDelegate, ignoreDelegate, false);
        }
    }

    /// <summary>低优先级目标重选后缀（原生无 ignore 参数）。</summary>
    [HarmonyPatch(typeof(TargetCacher), nameof(TargetCacher.GetClosestLowPriorityTargetWithinRange))]
    private static class LowPriorityReselectPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(TargetCacher __instance, float pos, float range,
            TargetCacher.SearchConditionDelegate conditionDelegate, ref Damageable __result)
        {
            __result = ReselectClosestTarget(__instance, __result, pos, range,
                conditionDelegate, null, true);
        }
    }
}
