using KingdomEnhancedMod;

int checks = 0;
void Check(bool ok, string message)
{
    checks++;
    if (!ok) throw new Exception(message);
}

var quota = new HeavyShieldQuota();
var left = Guid.NewGuid();
var right = Guid.NewGuid();
var third = Guid.NewGuid();
Check(!quota.CanPay, "unverified save must not charge");
Check(!quota.TryReservePayment(left, HeavyShieldQuota.Side.Left, out _), "unverified reserve");
Check(quota.BeginVerifiedRestore(false, false, false), "new save restore begins");
Check(!quota.CanPay && quota.FinishVerifiedRestore(true), "new save known, mold still locked");
Check(!quota.CanPay && !quota.CanBuyUpgrade(HeavyShieldQuota.Side.Left), "mold gates coins and extras");
var moldReceipt = Guid.NewGuid();
Check(quota.CanBuyMold && quota.TryBeginMoldPayment(moldReceipt), "four-gem mold transaction reserved");
Check(!quota.CanBuyMold && !quota.CanPay, "mold payment blocks concurrent sale");
Check(!quota.ConfirmMoldUnlocked(Guid.NewGuid()), "wrong mold receipt rejected");
Check(quota.ConfirmMoldUnlocked(moldReceipt) && quota.CanPay,
    "durably unlocked mold permits manual purchase");
Check(!quota.TryBeginUpgradePayment(moldReceipt, HeavyShieldQuota.Side.Left),
    "mold payment cannot also buy a side seat");
Check(!quota.TryReservePayment(moldReceipt, HeavyShieldQuota.Side.Left, out _),
    "mold payment cannot also buy a shield");
Check(quota.SeatLimit(HeavyShieldQuota.Side.Left) == 1 && quota.SeatLimit(HeavyShieldQuota.Side.Right) == 1,
    "one base seat on each side");
Check(quota.TryReservePayment(left, HeavyShieldQuota.Side.Left, out var side)
      && side == HeavyShieldQuota.Side.Left, "first left reservation");
Check(!quota.CanPay && quota.OccupiedLeft == 1 && quota.ShopOccupied, "paying owns rack and side");
Check(!quota.TryReservePayment(right, HeavyShieldQuota.Side.Right, out _), "single rack forbids parallel coin pay");
Check(!quota.ConfirmSoldier(left) && !quota.ConfirmUnpaidCancellation(right), "unpaid/wrong receipt refused");
Check(quota.ConfirmPaidTool(left) && !quota.ConfirmUnpaidCancellation(left), "paid shield cannot cancel as unpaid");
Check(!quota.CanPay && quota.ConfirmSoldier(left), "exact pickup frees rack only at transfer");
Check(!quota.ConfirmSoldier(left), "repeat pickup cannot duplicate seat");
Check(!quota.CanReservePayment(HeavyShieldQuota.Side.Left)
      && !quota.TryReservePayment(right, HeavyShieldQuota.Side.Left, out _),
      "full selected left never transfers a payment to right");
Check(quota.CanReservePayment(HeavyShieldQuota.Side.Right)
      && quota.TryReservePayment(right, HeavyShieldQuota.Side.Right, out side)
      && side == HeavyShieldQuota.Side.Right, "explicit right selection reserves right");
Check(quota.ConfirmPaidTool(right) && quota.ConfirmSoldier(right), "second side recruited");
Check(quota.OccupiedLeft == 1 && quota.OccupiedRight == 1 && !quota.CanPay, "base total two hard cap");
Check(!quota.TryReservePayment(third, HeavyShieldQuota.Side.Left, out _), "third purchase rejected before gems");

var upgradeLeft = Guid.NewGuid();
var upgradeLeftRetry = Guid.NewGuid();
var upgradeRight = Guid.NewGuid();
Check(quota.CanBuyUpgrade(HeavyShieldQuota.Side.Left), "left extra seat can be bought");
Check(quota.TryBeginUpgradePayment(upgradeLeft, HeavyShieldQuota.Side.Left), "left gem transaction reserved");
Check(!quota.CanPay && !quota.CanBuyUpgrade(HeavyShieldQuota.Side.Right), "one shop transaction at a time");
Check(!quota.ConfirmUpgradePurchased(upgradeRight), "wrong gem receipt cannot grant capacity");
Check(quota.ConfirmUpgradeCancelledAfterRefund(upgradeLeft), "native refund clears pending upgrade");
Check(!quota.ExtraSeatPurchased(HeavyShieldQuota.Side.Left), "cancel never grants a seat");
Check(!quota.TryBeginUpgradePayment(upgradeLeft, HeavyShieldQuota.Side.Left),
    "cancelled receipt cannot be recycled into another payment");
Check(quota.TryBeginUpgradePayment(upgradeLeftRetry, HeavyShieldQuota.Side.Left), "fresh left gem receipt");
Check(quota.ConfirmUpgradePurchased(upgradeLeftRetry), "durable two-gem left upgrade");
Check(quota.SeatLimit(HeavyShieldQuota.Side.Left) == 2 && quota.SeatLimit(HeavyShieldQuota.Side.Right) == 1,
    "one purchase grants only left");
Check(!quota.CanBuyUpgrade(HeavyShieldQuota.Side.Left), "same side cannot be charged twice");
Check(!quota.TryBeginUpgradePayment(upgradeLeftRetry, HeavyShieldQuota.Side.Right),
    "completed left receipt cannot unlock right");
Check(!quota.TryReservePayment(upgradeLeftRetry, HeavyShieldQuota.Side.Right, out _),
    "gem receipt cannot be reused as a coin shield purchase");
Check(quota.CanPay && !quota.CanReservePayment(HeavyShieldQuota.Side.Right)
      && !quota.TryReservePayment(third, HeavyShieldQuota.Side.Right, out _),
      "full right cannot borrow purchased left seat");
Check(quota.TryReservePayment(third, HeavyShieldQuota.Side.Left, out side)
      && side == HeavyShieldQuota.Side.Left, "explicit left selection uses purchased left seat");
Check(quota.ConfirmPaidTool(third) && quota.ConfirmSoldier(third), "third soldier transferred");
Check(!quota.CanPay && quota.OccupiedTotal == 3, "left2/right1 cap before right upgrade");
Check(quota.TryBeginUpgradePayment(upgradeRight, HeavyShieldQuota.Side.Right)
      && quota.ConfirmUpgradePurchased(upgradeRight), "separate two-gem right upgrade");
Check(quota.SeatLimit(HeavyShieldQuota.Side.Right) == 2 && quota.CanPay, "right seat now open");
var fourth = Guid.NewGuid();
Check(quota.TryReservePayment(fourth, HeavyShieldQuota.Side.Right, out side)
      && side == HeavyShieldQuota.Side.Right, "fourth soldier reserved right");
Check(quota.ConfirmPaidTool(fourth) && quota.ConfirmSoldier(fourth), "fourth soldier bound");
Check(quota.OccupiedLeft == 2 && quota.OccupiedRight == 2 && !quota.CanPay,
    "absolute two-per-side/four-total cap");
Check(!quota.CanBuyUpgrade(HeavyShieldQuota.Side.Left) && !quota.CanBuyUpgrade(HeavyShieldQuota.Side.Right),
    "no fifth or sixth seat sale");
Check(quota.ConfirmSoldierExit(left) && !quota.ConfirmSoldierExit(left), "confirmed exit only once");
Check(quota.CanPay && quota.OccupiedLeft == 1, "exit restores only manual purchase eligibility");

var lost = new HeavyShieldQuota();
Check(lost.BeginVerifiedRestore(true, false, false) && lost.FinishVerifiedRestore(true), "known unlocked save");
var paid = Guid.NewGuid();
Check(lost.TryReservePayment(paid, HeavyShieldQuota.Side.Right, out _), "reserve one paid shield");
Check(lost.ConfirmPaidTool(paid) && lost.HoldUnresolvedPaidTool(paid), "lost paid item quarantined");
Check(!lost.CanPay && !lost.ConfirmSoldier(paid), "quarantine blocks duplicate purchase");
Check(lost.ConfirmRefundedTool(paid) && lost.CanPay, "verified refund releases item and seat");

var savedTool = new HeavyShieldQuota();
var toolId = Guid.NewGuid();
Check(savedTool.BeginVerifiedRestore(true, false, false), "restore with paid tool begins locked");
Check(savedTool.TryRestorePaidTool(toolId, HeavyShieldQuota.Side.Left), "exact paid tool re-bound");
Check(savedTool.FinishVerifiedRestore(true) && !savedTool.CanPay && savedTool.ShopOccupied,
    "save/load paid-unpicked tool still blocks sale");
Check(savedTool.ConfirmSoldier(toolId) && savedTool.CanPay,
    "pickup after load frees only rack; other side remains available");
var missingTool = new HeavyShieldQuota();
Check(missingTool.BeginVerifiedRestore(true, false, false), "unresolved save begins");
Check(!missingTool.FinishVerifiedRestore(false) && !missingTool.CanPay,
    "unproven paid tool cannot become an empty rack");
var duplicate = new HeavyShieldQuota();
Check(duplicate.BeginVerifiedRestore(true, false, false), "duplicate save begins");
Check(duplicate.TryRestoreSoldier(toolId, HeavyShieldQuota.Side.Left), "one proven soldier");
Check(!duplicate.TryRestorePaidTool(toolId, HeavyShieldQuota.Side.Right), "same GUID cannot own two lives");
Check(!duplicate.FinishVerifiedRestore(true) && !duplicate.CanPay, "bad restore stays blocked");
var overCap = new HeavyShieldQuota();
Check(overCap.BeginVerifiedRestore(true, false, false), "over-cap save begins");
Check(overCap.TryRestoreSoldier(Guid.NewGuid(), HeavyShieldQuota.Side.Left), "first restored left");
Check(!overCap.TryRestoreSoldier(Guid.NewGuid(), HeavyShieldQuota.Side.Left), "extra left without upgrade rejected");
Check(!overCap.FinishVerifiedRestore(true) && !overCap.CanPay, "over-cap save quarantined");
var impossible = new HeavyShieldQuota();
Check(!impossible.BeginVerifiedRestore(false, true, false) && impossible.Unknown,
    "extra seat without mold is invalid");

var restoredReceipt = new HeavyShieldQuota();
Check(restoredReceipt.BeginVerifiedRestore(true, true, false), "upgrade save begins");
Check(restoredReceipt.TryRestoreSpentReceipt(moldReceipt), "durable mold receipt reloaded");
Check(restoredReceipt.TryRestoreSpentReceipt(upgradeLeftRetry), "durable upgrade receipt reloaded");
Check(restoredReceipt.FinishVerifiedRestore(true), "upgrade save verified");
Check(!restoredReceipt.TryBeginUpgradePayment(upgradeLeftRetry, HeavyShieldQuota.Side.Right),
    "reloaded left receipt cannot buy right");
Check(!restoredReceipt.TryReservePayment(upgradeLeftRetry, HeavyShieldQuota.Side.Right, out _),
    "reloaded upgrade receipt cannot buy a shield");
Check(!restoredReceipt.TryBeginUpgradePayment(moldReceipt, HeavyShieldQuota.Side.Right),
    "reloaded mold receipt cannot buy side upgrade");

var cancelledMold = new HeavyShieldQuota();
Check(cancelledMold.BeginVerifiedRestore(false, false, false)
      && cancelledMold.FinishVerifiedRestore(true), "cancelled mold setup");
var cancelledMoldId = Guid.NewGuid();
Check(cancelledMold.TryBeginMoldPayment(cancelledMoldId)
      && cancelledMold.ConfirmMoldCancelledAfterRefund(cancelledMoldId), "native refund releases mold transaction");
Check(!cancelledMold.TryBeginMoldPayment(cancelledMoldId), "cancelled mold receipt cannot be reused");
var unknownMold = new HeavyShieldQuota();
Check(unknownMold.BeginVerifiedRestore(false, false, false)
      && unknownMold.FinishVerifiedRestore(true), "unknown mold setup");
var unknownMoldId = Guid.NewGuid();
Check(unknownMold.TryBeginMoldPayment(unknownMoldId)
      && unknownMold.HoldUnknownMold(unknownMoldId), "ambiguous mold result quarantined");
Check(!unknownMold.CanBuyMold && !unknownMold.CanPay, "ambiguous mold never recharges");

var uncertainUpgrade = new HeavyShieldQuota();
Check(uncertainUpgrade.BeginVerifiedRestore(true, false, false)
      && uncertainUpgrade.FinishVerifiedRestore(true), "upgrade ambiguity setup");
var uncertainId = Guid.NewGuid();
Check(uncertainUpgrade.TryBeginUpgradePayment(uncertainId, HeavyShieldQuota.Side.Left)
      && uncertainUpgrade.HoldUnknownUpgrade(uncertainId), "uncertain two-gem result freezes shop");
Check(!uncertainUpgrade.CanPay && !uncertainUpgrade.CanBuyUpgrade(HeavyShieldQuota.Side.Left),
    "ambiguous charge never silently retries");

foreach (bool unlocked in new[] { false, true })
{
    var capacity = new HeavyShieldQuota();
    Check(capacity.BeginVerifiedRestore(unlocked, false, false), "receipt-cap restore begins");
    for (int i = 0; i < HeavyShieldQuota.MaxSeenReceipts; i++)
        if (!capacity.TryRestoreSpentReceipt(Guid.NewGuid())) throw new Exception("receipt-cap fixture");
    Check(capacity.FinishVerifiedRestore(true), "receipt-cap exact restore succeeds");
    Check(!capacity.CanBuyMold && !capacity.CanBuyUpgrade(HeavyShieldQuota.Side.Left)
        && !capacity.CanBuyUpgrade(HeavyShieldQuota.Side.Right) && !capacity.CanPay
        && !capacity.CanReservePayment(HeavyShieldQuota.Side.Left)
        && !capacity.CanReservePayment(HeavyShieldQuota.Side.Right), "full receipt capacity advertises no new currency charge");
    Check(!capacity.TryBeginMoldPayment(Guid.NewGuid())
        && !capacity.TryBeginUpgradePayment(Guid.NewGuid(), HeavyShieldQuota.Side.Left)
        && !capacity.TryReservePayment(Guid.NewGuid(), HeavyShieldQuota.Side.Right, out _),
        "full receipt capacity refuses all reservations consistently");
}

Console.WriteLine($"PASS heavy-shield capacity and paid-tool restore: {checks} checks");
