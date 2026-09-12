using System;
using System.Linq;
using KingdomEnhancedMod;
using static AutoRestockTests.Program;

namespace AutoRestockTests;
internal static class DoubleCost
{
    internal static void Run()
    {
        foreach (int role in Enumerable.Range(0, 5))
        foreach (int nativePrice in new[] { 1, 2, 3, 100 })
            Program.Run($"double_cost_role_{role}_native_{nativePrice}", () =>
            {
                var e = NewEnv(); Role(role, true, 1);
                var shop = e.MakeShop(role, nativePrice);
                int total = nativePrice * 2;
                e.Banker._stashedCoins = total;
                shop.TransactionCompleteF = s => {
                    Eq(s.Price, nativePrice, "native transaction observes unchanged price");
                    Eq(e.Banker._stashedCoins, 0, "full doubled debit precedes native purchase");
                    Eq(Pool.SpawnCalls, total, "all doubled coins precede native purchase");
                };
                e.Tick(); Eq(Reserved, 1, "exact doubled balance starts order");
                Ok(PumpUntil(e, () => shop.TransactionCompleteCalls == 1 && Reserved == 0, 60), "paid and departed");
                Eq(PatchEconomy_Banker.SpendAmounts.Single(), total, "one full doubled debit");
                Eq(PatchEconomy_Banker.TrySpendCalls, 1, "only one atomic spend attempt");
                Eq(Pool.SpawnCalls, total, "animation matches debit");
                Eq(shop.Items, 1, "one native stock item");
                Eq(shop.Price, nativePrice, "shop price remains native");
                string success = KingdomEnhancedPlugin.Instance.LogSource.Infos.Single(s => s.Contains("补货成功"));
                Has(success, $"price={total} nativePrice={nativePrice} coins={total}", "auditable success log");
                for (int i = 0; i < 20; i++) e.Frame(.2f);
                Eq(Spend, 1, "departure and later scans cannot repeat debit");
                Eq(shop.TransactionCompleteCalls, 1, "departure cannot repeat native transaction");
            });

        foreach (int price in new[] { int.MinValue, -1, 0, 101, int.MaxValue })
            Program.Run($"double_cost_invalid_native_{price}", () =>
            {
                var e = NewEnv(); Role(0, true, 1); var shop = e.MakeShop(0, price);
                e.Banker._stashedCoins = int.MaxValue; e.Tick();
                Eq(Reserved, 0, "invalid native price never reserved");
                Eq(Pool.SpawnCalls, 0, "invalid native price never animates");
                Eq(Spend, 0, "invalid native price never debited");
                Eq(shop.TransactionCompleteCalls, 0, "invalid native price never purchased");
            });

        foreach (int price in new[] { 1, 2, 3, 100 })
            Program.Run($"double_cost_native_affordable_total_unaffordable_{price}", () =>
            {
                var e = NewEnv(); Role(0, true, 1); var shop = e.MakeShop(0, price);
                e.Banker._stashedCoins = price * 2 - 1; e.Tick();
                Eq(Reserved, 0, "one short of doubled cost blocks order");
                Has(PatchEconomy_AutoRestock.GetSummary(0), "金币不足", "truthful insufficient summary");
                Eq(Pool.SpawnCalls, 0, "unaffordable order emits no coin");
                Eq(Spend, 0, "unaffordable order never debits");
                Eq(shop.TransactionCompleteCalls, 0, "unaffordable order never buys");
            });

        Program.Run("double_cost_concurrent_held_budget_excludes_unaffordable_second", () =>
        {
            var e = NewEnv(); Role(0, true, 1); Role(1, true, 1);
            var first = e.MakeShop(0, 2); var second = e.MakeShop(1, 3);
            e.Banker._stashedCoins = 9; e.Tick();
            Eq(Reserved, 1, "4 held leaves 5, below second total 6");
            Has(PatchEconomy_AutoRestock.GetSummary(1), "金币不足", "held double budget reflected");
            Ok(PumpUntil(e, () => first.TransactionCompleteCalls == 1 && Reserved == 0), "first purchase completes");
            Eq(e.Banker._stashedCoins, 5, "first consumes 4");
            Eq(second.TransactionCompleteCalls, 0, "second never overspends");
            e.Banker._stashedCoins = 6; e.Tick();
            Ok(PumpUntil(e, () => second.TransactionCompleteCalls == 1 && Reserved == 0), "second starts when total affordable");
            Eq(PatchEconomy_Banker.SpendAmounts.ToArray(), new[] { 4, 6 }, "separate doubled commits");
        });

        Program.Run("double_cost_balance_drain_during_coins_cancels_without_more_coins", () =>
        {
            var e = NewEnv(); Role(0, true, 1); var shop = e.MakeShop(0, 3);
            e.Banker._stashedCoins = 6; e.Tick(); Arrive(e); e.Frame(.16f);
            Eq(Pool.SpawnCalls, 1, "first coin emitted");
            e.Banker._stashedCoins = 5; e.Frame(.21f);
            Eq(Reserved, 0, "balance below total cancels during animation");
            Eq(Pool.SpawnCalls, 1, "no further coin after failed balance gate");
            Eq(Spend, 0, "cancelled order does not debit");
            Eq(shop.TransactionCompleteCalls, 0, "cancelled order does not buy");
        });
    }
}
