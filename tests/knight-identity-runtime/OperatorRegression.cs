using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests;

internal static class OperatorRegression
{
    internal static void Run()
    {
        foreach (string invalidation in new[] { "inactive", "dead", "squire" })
            Case.Run("balance_ignores_" + invalidation + "_knights_in_cached_world", () =>
            {
                using Fixture fixture = new Fixture();
                for (int style = 0; style < 5; style++)
                {
                    var unit = NativeSim.NewKnight(41000 + style);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, style, 0, Styles.All, out _), "seed each style");
                    if (style != 4) continue;
                    if (invalidation == "inactive") unit.Go.activeInHierarchy = false;
                    if (invalidation == "dead") unit.Knight._damageable.isDead = true;
                    if (invalidation == "squire") unit.Go.tag = "Squire";
                }
                var recruit = NativeSim.NewKnight(41010);
                KnightIdentityRuntime.OnEnable(recruit.Knight);
                KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, -1, 0, Styles.All, out int selected), "recruit allocated");
                Check.Equal(4, selected, "the missing live style must be selected, without relying on the next Poll");
            });
    }
}
