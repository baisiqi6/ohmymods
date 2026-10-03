"""Source guardrails supplement (not replace) the production runtime/receipt suite."""

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SHELL = (ROOT / "il2cpp" / "HeavyShieldShopShell.cs").read_text(encoding="utf-8")
MANIFEST = json.loads((ROOT / "artifacts" / "heavy-shield-approved" / "atlas-manifest.json").read_text(encoding="utf-8"))


def method_body(signature):
    start = SHELL.index(signature)
    opening = SHELL.index("{", start)
    depth = 1
    for index in range(opening + 1, len(SHELL)):
        depth += (SHELL[index] == "{") - (SHELL[index] == "}")
        if depth == 0:
            return SHELL[opening + 1:index]
    raise AssertionError(f"unclosed method: {signature}")


class ShopShellContract(unittest.TestCase):
    def test_manifest_layer_ranges_and_ground_pivot(self):
        layers = MANIFEST["atlases"]["shopLayers"]
        self.assertEqual(layers["cell"], [96, 60])
        self.assertEqual(layers["frameCount"], 14)
        sequences = layers["sequences"]
        self.assertEqual([(name, sequences[name]["first"], sequences[name]["count"])
                          for name in ("rear", "fixtures", "merchant", "front")],
                         [("rear", 0, 1), ("fixtures", 1, 6), ("merchant", 7, 6), ("front", 13, 1)])
        self.assertEqual(MANIFEST["shopGroundTopLeftPixel"], [48, 56])
        for name in sequences:
            self.assertIn(f'TrySequenceFrame(atlas, "{name}"', SHELL)
        self.assertIn("HeavyShieldArt.TryGetShopLayerSprite", SHELL)
        self.assertIn("HeavyShieldArt.TryGetGreekShopLayerSprite", SHELL)
        self.assertIn("biome.BiomeIndex == BiomeHolder.GreeceBiomeIndex", SHELL)

    def test_native_receipt_and_fixed_point_pricing(self):
        create = method_body("private static void CreatePoint(int index, float halfWidth)")
        self.assertIn("point.Payable.Price = point.Payment.Price", create)
        self.assertIn("point.Payable.Currency = point.Payment.Currency", create)
        self.assertIn("point.Payable.priceIncrease = 0", create)
        self.assertIn("add_OnTransactionStartedCallback", create)
        pay = method_body("internal static void OnPay(int index, long life, Player player)")
        self.assertIn("_completingPayable", pay)
        self.assertIn("Player.PayState.Completed", pay)
        self.assertIn("_floatingCurrency[i].CurrencyType", pay)
        self.assertIn("payment.Complete", pay)
        self.assertNotRegex(pay, r"\.(?:Price|Currency)\s*=")
        can_pay = method_body("internal static bool CanPay(int index, long life, Player player)")
        self.assertNotIn("TryReserve", can_pay)
        self.assertIn("ValidatePurchase", can_pay)
        self.assertIn("CanReservePurchase", method_body("private static bool CanStart(Point point, Player player)"))
        self.assertIn("CarrierPreflightReady", SHELL)
        self.assertNotIn("wallet.AddCurrency", SHELL)
        self.assertNotIn("Professions", SHELL)

    def test_registration_precedes_activation_and_cleanup_matches_header(self):
        create = method_body("private static void CreatePoint(int index, float halfWidth)")
        self.assertLess(create.index("ownerInterface.IsLocked"), create.index("RegisterObject"))
        self.assertLess(create.index("RegisterObject"), create.index("PointHeaderMatches(point)"))
        root_create = method_body("private static void Create(Kingdom kingdom, Transform layer)")
        self.assertLess(root_create.index("HeaderMatches()"), root_create.index("_object.SetActive(true)"))
        clear = method_body("internal static void Clear(string reason)")
        self.assertLess(clear.index("point.Payable.forceBlockPayment = true"), clear.index("DeregisterObject"))
        self.assertIn("found.Pointer == point.Header.Pointer", clear)
        self.assertIn("CancelPendingTransactions()", clear)
        self.assertIn("HasUnsettledNativeTransaction()", clear)
        disabled = method_body("internal static void OnOwnerDisabled(HeavyShieldShopShellOwner owner, int index, long life)")
        self.assertNotIn("Clear(", disabled)
        self.assertIn("IntPtr reason", SHELL)

    def test_world_and_menu_gates(self):
        self.assertIn("HeroShopRetention.Decide(in probe)", SHELL)
        self.assertIn("NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth", SHELL)
        self.assertIn("kingdom.Pointer == _kingdom.Pointer && layer.Pointer == _layer.Pointer", SHELL)
        self.assertIn("Game.State.Menu", SHELL)
        self.assertIn("HeroShopPlacementNative.Find", SHELL)
        self.assertIn("HeroShopGrounding.TryResolve", SHELL)
        self.assertIn("root.IsChildOf(layer)", SHELL)

    def test_failed_started_uses_later_exact_driver_cancellation(self):
        started = method_body("private static void OnTransactionStarted(int index, long life, Player player)")
        self.assertIn("FailStarted", started)
        self.assertNotIn(".CancelTransaction(", started)
        self.assertNotIn(".DropFloatingCurrency(", started)
        failed = method_body("private static void FailStarted(Point point, Player player, long life, string reason)")
        self.assertIn("ObserveFailedStart", failed)
        self.assertIn("point.Payer = player", failed)
        self.assertNotIn(".CancelTransaction(", failed)
        advance = method_body("private static void AdvanceFailedStarts()")
        self.assertIn("Time.frameCount <= failed.StartedFrame", advance)
        self.assertIn("_shopLife != failed.ShopLife", advance)
        self.assertIn("payer.selectedPayable.Pointer.ToInt64() != failed.Payable", advance)
        self.assertIn("payer._payState != Player.PayState.Transaction", advance)
        self.assertIn("payer._payState == Player.PayState.Completed", advance)
        self.assertIn("failed.ObserveDropReturn()", advance)
        self.assertIn("failed.ObserveCleanup", advance)
        self.assertNotIn("ConfirmUnpaidCancellation", advance)


if __name__ == "__main__":
    unittest.main()
