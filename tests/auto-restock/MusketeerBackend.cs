// Only the game/identity/shipment boundary is fake. The service AND the dedicated
// production Shop bank adapter are linked and run together, including debit ordering.
using System;
using UnityEngine;
namespace KingdomEnhancedMod
{
    internal static class MusketeerAccess
    {
        internal static bool InWorld(Component target) => target?.gameObject != null
            && target.gameObject.activeInHierarchy && target.transform.IsChildOf(Managers.Inst.world.gameLayer);
    }
    internal static class MusketeerIdentity
    {
        internal static bool Ready = true;
        internal static int Live, Guns;
        internal static bool TryGetRestockCounts(out int live, out int guns)
        { live = Live; guns = Guns; return Ready; }
    }
    internal static partial class MusketeerShop
    {
        internal const int Price = 4;
        private static Payable _payable;
        private static Kingdom _kingdom;
        private static string _status;
        internal static string StatusText => _status;
        private static void Log(string text) { }
        private static readonly MusketeerShopPayment Payment = new();
        internal static void ArmReceiptForTest(long payer) => Payment.Arm(payer);
        internal static bool ReceiptArmedForTest(long payer) => Payment.IsArmed(payer);
        internal static bool Exists = true, BowReady = true, Saving, SpawnFails, SpawnThrows;
        internal static int Rack, SpawnCalls, BalanceAtSpawn;
        internal static bool CanPurchase() => Exists && BowReady && !Saving && Rack < 3
            && ModConfig.MusketeerEnabled.Value && Managers.Inst.game.state == Game.State.Playing;
        private static bool Pending(Player player) => player != null &&
            (player.selectedPayable == _payable || player._completingPayable == _payable);
        private static bool TryCreateGun(out string reason)
        {
            SpawnCalls++; BalanceAtSpawn = BankAssistantCoordinator.MainBanker._stashedCoins;
            reason = "native spawn failed";
            if (SpawnThrows) throw new InvalidOperationException(reason);
            if (SpawnFails) return false;
            MusketeerIdentity.Guns++; Rack++; reason = ""; return true;
        }
        internal static void Setup(Payable payable, Kingdom kingdom)
        {
            _payable = payable; _kingdom = kingdom; Exists = BowReady = true;
            Saving = SpawnFails = SpawnThrows = false; Rack = SpawnCalls = 0;
            Payment.Clear(); MusketeerIdentity.Ready = true;
            MusketeerIdentity.Live = MusketeerIdentity.Guns = 0;
        }
    }
}
