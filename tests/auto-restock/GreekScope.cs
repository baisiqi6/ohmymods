using KingdomEnhancedMod;
using static AutoRestockTests.Program;

namespace AutoRestockTests;

internal static class GreekScope
{
    internal static void Run()
    {
        Program.Run("foreign_world_never_assigns_or_spends", () =>
        {
            HardReset();
            var e = new Env();
            ModConfig.AutoRestockWorkersEnabled.Value = true;
            e.Banker._stashedCoins = 100;
            e.AddAssistants(2);
            e.MakeShop(0, 2);
            GreekBankScope.IsActive = false;
            for (int i = 0; i < 80; i++) e.Frame();
            Eq(e.Banker._stashedCoins, 100, "foreign treasury remains native");
            Has(PatchEconomy_AutoRestock.GetSummary(0), "希腊", "foreign summary reports scope");
            Eq(AutoRestockCounts.RefreshCalls, 0, "foreign never enters procurement planning");
        });
        Program.Run("leaving_greek_cancels_in_flight_purchase_before_debit", () =>
        {
            HardReset();
            var e = new Env();
            ModConfig.AutoRestockWorkersEnabled.Value = true;
            e.Banker._stashedCoins = 100;
            e.AddAssistants(2);
            e.MakeShop(0, 2);
            e.Tick();
            GreekBankScope.IsActive = false;
            for (int i = 0; i < 80; i++) e.Frame();
            Eq(e.Banker._stashedCoins, 100, "leaving scope cannot complete a stale purchase");
            Has(PatchEconomy_AutoRestock.GetSummary(0), "希腊", "stale order summary hidden");
        });
    }
}
