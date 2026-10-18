namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界扩展商店的静态计划（PC 2.4.0；槽位依据见
/// docs/project-harness/game-logic-map/shop-system.md 与 castle-upgrade.md）。
///
/// 原生商店入队白名单（Mono 2.1.0 已验证；2.4.0 仅枚举值重排，全部按名引用）：
///   NinjaLeft/Right 仅 biome 1；ShieldShopLeft/Right 仅 biome 3；
///   Pike 0/2/3/5；ChangeRuler 2；ChangeItem 3/5；Workshop 0/1/2/5。
/// 因此本计划的槽位分配：
///   Europe(0)    : Ninja 7/8 + Berserker 12/13（ShieldShop 槽空闲）
///   Shogun(1)    : Berserker 12/13（原生 Ninja 7/8 保持）
///   DeadLands(2) : Ninja 7/8 + Berserker 12/13
///   Norse(3)     : Ninja 7/8（原生 Berserker 与 ShieldShop 保持，不覆盖）
///   Greece(5)    : Ninja 7/8 + Berserker 12/13（既有 mod 行为）
/// 其它 biome 不扩张。狂战士 prefab 的 shopType=Pike，必须经 PayableSidedShop.Awake
/// 临时改写为 ShieldShop 才会自注册到 12/13；北欧因此不参与改写。
///
/// Android 分线不在本任务验收范围：仅保留既有 Greek 行为，其它 biome 返回 none。
/// </summary>
internal static class WorldShopPlan
{
    internal const int EuropeBiomeIndex = 0;
    internal const int ShogunBiomeIndex = 1;
    internal const int DeadLandsBiomeIndex = 2;
    internal const int NorseBiomeIndex = 3;

    internal readonly struct Plan
    {
        internal readonly bool Ninja;
        internal readonly bool Berserker; // 通过 ShieldShop 12/13 槽位复用
        internal readonly bool Greece;    // 沿用既有希腊清理/额外池语义

        internal Plan(bool ninja, bool berserker, bool greece)
        {
            Ninja = ninja;
            Berserker = berserker;
            Greece = greece;
        }

        internal bool Any => Ninja || Berserker;
    }

    internal static Plan Get(int biomeIndex, int greeceBiomeIndex)
    {
        if (biomeIndex == greeceBiomeIndex) return new Plan(true, true, true);
#if ANDROID
        // Android 线保持既有 Greek-only 行为；扩展世界未在 Android 验收。
        return default;
#else
        switch (biomeIndex)
        {
        case EuropeBiomeIndex:
        case DeadLandsBiomeIndex:
            return new Plan(true, true, false);
        case ShogunBiomeIndex:
            return new Plan(false, true, false);
        case NorseBiomeIndex:
            return new Plan(true, false, false);
        default:
            return default;
        }
#endif
    }
}
