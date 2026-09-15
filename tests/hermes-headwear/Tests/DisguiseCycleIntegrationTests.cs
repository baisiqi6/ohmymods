using KingdomEnhancedMod;
using UnityEngine;

namespace HermesHeadwearTests
{
    internal static class DisguiseCycleIntegrationTests
    {
        internal static void Run()
        {
            Case.Run("cycle allocated only once across repeated init", () =>
            {
                Random.SetSequence(0, 31);
                var fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll);
                NativeFlow.ConvertByHermes(fx.Troll);
                Check.Equal(1, HermesHeadwearCycle.Calls, "same generation allocated once");
                Check.True(PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "party hat protects");
            });
            Case.Run("miss and disabled do not advance cycle", () =>
            {
                Random.SetSequence(99);
                NativeFlow.ConvertByHermes(TrollFixture.Create().Troll);
                ModConfig.HermesHeadwearEnabled.Value = false;
                NativeFlow.ConvertByHermes(TrollFixture.Create().Troll);
                Check.Equal(0, HermesHeadwearCycle.Calls, "no successful roll");
            });
            Case.Run("cycle failure preserves native conversion and no disguise", () =>
            {
                Random.SetSequence(0);
                HermesHeadwearCycle.Succeed = false;
                var fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 7, true, -1);
                Check.Equal(7, fx.Troll._trollHealth, "native health");
                Check.True(fx.Troll._toughTroll, "native tough");
                Check.Equal(-1, fx.Troll._maskIndex, "native mask");
                Check.True(!PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "failed allocation no costume");
                NativeFlow.ConvertByHermes(fx.Troll);
                Check.Equal(1, HermesHeadwearCycle.Calls, "failed receipt still frozen");
            });
            Case.Run("all 44 displayed choices qualify including party hats", () =>
            {
                for (int i = 0; i < 44; i++)
                {
                    Random.SetSequence(0, i);
                    var fx = TrollFixture.Create();
                    NativeFlow.ConvertByHermes(fx.Troll);
                    Check.True(PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "choice " + i);
                }
            });
            Case.Run("hidden costume loses protection without reallocating", () =>
            {
                Random.SetSequence(0, 43);
                var fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll);
                HermesHeadwearVisuals.Clear(fx.Troll);
                Check.True(!PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "visual not displayed");
                Check.Equal(1, HermesHeadwearCycle.Calls, "read-only query");
            });
            Case.Run("configuration and authority guard protection", () =>
            {
                Random.SetSequence(0, 0);
                var fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll);
                ModConfig.HermesHeadwearEnabled.Value = false;
                Check.True(!PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "feature off");
                ModConfig.HermesHeadwearEnabled.Value = true;
                NetworkBigBoss.HasWorldAuth = false;
                Check.True(!PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "client does not decide");
                Check.Equal(1, HermesHeadwearCycle.Calls, "guards cannot allocate");
            });
            Case.Run("pool retirement discards disguise receipt", () =>
            {
                Random.SetSequence(0, 12);
                var fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll);
                PatchDivine_HermesHeadwear.HandleNativeResetAndDespawn(fx.Troll);
                Check.True(!PatchDivine_HermesHeadwear.HasDisguiseHeadwear(fx.Troll), "old life no protection");
                Check.Equal(1, HermesHeadwearCycle.Calls, "retirement does not allocate");
            });
        }
    }
}
