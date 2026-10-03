// Coin-courier economy suite. Links the real production courier core and bridge
// (CoinCourierPurse/Economy/Rules/Targeting/BankScope, GreekBankScope and the bank
// owner's narrow one-coin entry) against the stub game surface in Stubs.cs; it never
// mirrors the formulas under test.
//
// Run: dotnet run -c Release --project tests/coin-courier-economy
using System;
using KingdomEnhancedMod;
using UnityEngine;

static CoinCourierPurse PurseWith(int coins, int capacity = 4)
{
    var purse = new CoinCourierPurse();
    for (int i = 0; i < coins; i++) purse.TryCreditTakenCoin(capacity);
    return purse;
}

static (CoinCourierResult Result, CoinCourierPurse Purse, Knight Knight) DeliverCase(long life, Action<Knight> mutate)
{
    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(life, 0, 8);
    mutate?.Invoke(knight);
    CoinCourierPurse purse = PurseWith(1);
    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, life);
    return (result, purse, knight);
}

static (CoinCourierResult Result, CoinCourierPurse Purse, Knight Knight) DeliverWorldCase(long life, Action setup)
{
    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(life, 0, 8);
    setup?.Invoke();
    CoinCourierPurse purse = PurseWith(1);
    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, life);
    return (result, purse, knight);
}

// =========================================================================
// Purse core
// =========================================================================

Harness.Test("purse credit respects capacity and never trims an existing balance", () =>
{
    var purse = new CoinCourierPurse();
    Harness.Eq(0, purse.Coins, "new purse is empty");
    Harness.Eq(0, purse.AvailableCoins, "nothing available");
    Harness.False(purse.IsFaulted, "new purse is not faulted");

    Harness.Status(CoinCourierStatus.Applied, purse.TryCreditTakenCoin(4), "first coin applied");
    Harness.Status(CoinCourierStatus.Applied, purse.TryCreditTakenCoin(4), "second coin applied");
    Harness.Status(CoinCourierStatus.Applied, purse.TryCreditTakenCoin(4), "third coin applied");
    Harness.Eq(3, purse.Coins, "three coins held");

    CoinCourierResult lowered = purse.TryCreditTakenCoin(2); // capacity below the current balance
    Harness.Status(CoinCourierStatus.NotApplied, lowered, "credit refused at lowered capacity");
    Harness.Reason(CoinCourierReason.CapacityExceeded, lowered, "lowered-capacity reason");
    Harness.Eq(3, purse.Coins, "existing coins are never trimmed");
    Harness.Eq(3, purse.AvailableCoins, "available balance unchanged");

    CoinCourierResult zero = purse.TryCreditTakenCoin(0);
    Harness.Status(CoinCourierStatus.NotApplied, zero, "zero capacity refused");
    Harness.Reason(CoinCourierReason.InvalidArgument, zero, "zero-capacity reason");
    Harness.False(CoinCourierEconomy.InCall, "economy guard untouched by purse calls");
});

Harness.Test("purse reservation lifecycle: reserve, complete, return", () =>
{
    CoinCourierPurse purse = PurseWith(3);

    CoinCourierResult zeroLife = purse.TryReserveOneForDelivery(0);
    Harness.Status(CoinCourierStatus.NotApplied, zeroLife, "zero lifetime refused");
    Harness.Reason(CoinCourierReason.InvalidArgument, zeroLife, "zero-lifetime reason");

    Harness.Status(CoinCourierStatus.Applied, purse.TryReserveOneForDelivery(71), "reserve applied");
    Harness.Eq(3, purse.Coins, "reserved coin stays in the bag");
    Harness.Eq(2, purse.AvailableCoins, "available excludes the reserved coin");
    Harness.True(purse.HasPendingDelivery, "pending handoff recorded");
    Harness.Eq(71, purse.PendingDeliveryLife, "pending lifetime recorded");

    CoinCourierResult second = purse.TryReserveOneForDelivery(71);
    Harness.Status(CoinCourierStatus.NotApplied, second, "second reservation refused");
    Harness.Reason(CoinCourierReason.PendingExists, second, "pending-exists reason");

    CoinCourierResult wrongLife = purse.TryCompleteDelivery(72);
    Harness.Status(CoinCourierStatus.NotApplied, wrongLife, "wrong lifetime cannot consume");
    Harness.Reason(CoinCourierReason.PendingLifeMismatch, wrongLife, "life-mismatch reason");
    Harness.True(purse.HasPendingDelivery, "pending survives a mismatched consume");

    Harness.Status(CoinCourierStatus.Applied, purse.TryCompleteDelivery(71), "confirmed handoff consumes");
    Harness.Eq(2, purse.Coins, "one coin left the bag");
    Harness.False(purse.HasPendingDelivery, "pending cleared after consume");
    Harness.Eq(2, purse.AvailableCoins, "remaining coin is spendable");

    Harness.Status(CoinCourierStatus.Applied, purse.TryReserveOneForDelivery(72), "reserve again");
    Harness.Status(CoinCourierStatus.Applied, purse.TryReturnDeliveryReservation(72), "confirmed no-op returns");
    Harness.Eq(2, purse.Coins, "returned coin stays in the bag");
    Harness.False(purse.HasPendingDelivery, "pending cleared after return");

    CoinCourierResult noPending = purse.TryReturnDeliveryReservation(72);
    Harness.Status(CoinCourierStatus.NotApplied, noPending, "double return refused");
    Harness.Reason(CoinCourierReason.NoPendingDelivery, noPending, "no-pending reason");
});

Harness.Test("purse refuses a delivery with no spendable coin", () =>
{
    var purse = new CoinCourierPurse();
    CoinCourierResult result = purse.TryReserveOneForDelivery(5);
    Harness.Status(CoinCourierStatus.NotApplied, result, "empty purse cannot reserve");
    Harness.Reason(CoinCourierReason.NoAvailableCoins, result, "no-coins reason");
    Harness.Eq(0, purse.Coins, "still empty");
    Harness.False(purse.HasPendingDelivery, "no pending created");
});

Harness.Test("purse latches the first fault and freezes all economy entries", () =>
{
    CoinCourierPurse purse = PurseWith(1);
    var fault = new CoinCourierFault(CoinCourierFaultKind.BagUnknown,
        CoinCourierReason.BankWriteUnknown, 5, -1, -1, -1, 0);

    Harness.Status(CoinCourierStatus.Applied, purse.MarkUnknown(fault), "first fault latched");
    Harness.True(purse.IsFaulted, "purse is faulted");
    Harness.Eq(5, purse.Fault.BankBefore, "before evidence kept");
    Harness.Eq(-1, purse.Fault.BankAfter, "unknown after kept as unknown");

    CoinCourierResult credit = purse.TryCreditTakenCoin(4);
    Harness.Status(CoinCourierStatus.NotApplied, credit, "credit frozen");
    Harness.Reason(CoinCourierReason.Frozen, credit, "frozen credit reason");

    CoinCourierResult reserve = purse.TryReserveOneForDelivery(9);
    Harness.Status(CoinCourierStatus.NotApplied, reserve, "reserve frozen");
    Harness.Reason(CoinCourierReason.Frozen, reserve, "frozen reserve reason");

    var second = new CoinCourierFault(CoinCourierFaultKind.BagUnknown,
        CoinCourierReason.BankEmpty, 7, 7, -1, -1, 0);
    CoinCourierResult overwrite = purse.MarkUnknown(second);
    Harness.Status(CoinCourierStatus.NotApplied, overwrite, "second fault refused");
    Harness.Reason(CoinCourierReason.Frozen, overwrite, "second fault reason");
    Harness.Eq(5, purse.Fault.BankBefore, "first evidence is never overwritten");
    Harness.Eq(1, purse.Coins, "coins unchanged by faults");
});

Harness.Test("purse snapshot round trip: clean state and orphan pending", () =>
{
    CoinCourierPurse purse = PurseWith(2);
    Harness.Status(CoinCourierStatus.Applied, purse.TryReserveOneForDelivery(9), "reserve for snapshot");
    CoinCourierPurseSnapshot pending = purse.Capture();

    Harness.True(CoinCourierPurse.TryRestore(pending, out CoinCourierPurse restored, out CoinCourierReason reason),
        "pending snapshot restores");
    Harness.Eq((int)CoinCourierReason.None, (int)reason, "restore reason");
    Harness.Eq(2, restored.Coins, "coins restored");
    Harness.True(restored.HasPendingDelivery, "pending restored");
    Harness.Eq(9, restored.PendingDeliveryLife, "pending lifetime restored");
    Harness.Eq(1, restored.AvailableCoins, "reserved coin stays excluded");
    Harness.True(restored.IsFaulted, "a mid-handoff snapshot is surfaced as an explicit unknown");
    Harness.Eq((int)CoinCourierFaultKind.DeliveryUnknown, (int)restored.Fault.Kind, "coerced fault kind");
    Harness.Eq(9, restored.Fault.ExpectedLife, "coerced fault keeps the pending lifetime");

    var clean = new CoinCourierPurse();
    clean.TryCreditTakenCoin(4);
    CoinCourierPurseSnapshot snapshot = clean.Capture();
    Harness.True(CoinCourierPurse.TryRestore(snapshot, out CoinCourierPurse restoredClean, out _),
        "clean snapshot restores");
    Harness.Eq(1, restoredClean.Coins, "clean coins restored");
    Harness.False(restoredClean.HasPendingDelivery, "clean has no pending");
    Harness.False(restoredClean.IsFaulted, "clean is not faulted");
});

Harness.Test("purse restore validates inconsistent snapshots", () =>
{
    bool negative = CoinCourierPurse.TryRestore(
        new CoinCourierPurseSnapshot(-1, false, 0, default), out _, out CoinCourierReason negativeReason);
    Harness.False(negative, "negative coins rejected");
    Harness.Eq((int)CoinCourierReason.InvalidArgument, (int)negativeReason, "negative-coins reason");

    Harness.False(CoinCourierPurse.TryRestore(
        new CoinCourierPurseSnapshot(0, true, 5, default), out _, out _),
        "pending without a coin rejected");
    Harness.False(CoinCourierPurse.TryRestore(
        new CoinCourierPurseSnapshot(2, false, 5, default), out _, out _),
        "lifetime without a pending rejected");
    Harness.False(CoinCourierPurse.TryRestore(
        new CoinCourierPurseSnapshot(1, true, 0, default), out _, out _),
        "pending without a lifetime rejected");
});

Harness.Test("purse re-entrancy guard refuses a nested entry", () =>
{
    CoinCourierPurse purse = PurseWith(1);
    Harness.SetField(purse, "_busy", true);

    CoinCourierResult nested = purse.TryCreditTakenCoin(4);
    Harness.Status(CoinCourierStatus.NotApplied, nested, "nested credit refused");
    Harness.Reason(CoinCourierReason.Reentrant, nested, "reentrant credit reason");
    CoinCourierResult nestedReserve = purse.TryReserveOneForDelivery(7);
    Harness.Status(CoinCourierStatus.NotApplied, nestedReserve, "nested reserve refused");
    Harness.Reason(CoinCourierReason.Reentrant, nestedReserve, "reentrant reserve reason");
    Harness.Eq(1, purse.Coins, "no mutation while the guard is held");

    Harness.SetField(purse, "_busy", false);
    Harness.Status(CoinCourierStatus.Applied, purse.TryCreditTakenCoin(4), "entry works after the guard clears");
});

// =========================================================================
// Bagging: treasury -> purse through the real bank owner entry
// =========================================================================

Harness.Test("bagging Greek debits the treasury, credits the purse and syncs the ledger", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();

    CoinCourierResult first = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, first, "first bag applied");
    Harness.Reason(CoinCourierReason.None, first, "no post-commit fault");
    Harness.Eq(1, purse.Coins, "purse holds one coin");
    Harness.Eq(4, banker._stashedCoins, "treasury debited once");
    Harness.Eq(4, Harness.BankLive(), "shared ledger in sync");
    Castle castle = Managers.Inst.kingdom.castle;
    Harness.Eq(4, castle.StashCalls[castle.StashCalls.Count - 1], "castle stash refreshed");
    Harness.Eq(4, Managers.Inst.stats.StatCalls[Stat.CoinsInBank], "CoinsInBank refreshed");

    CoinCourierResult second = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, second, "repeat bag applied");
    Harness.Eq(2, purse.Coins, "two coins in the purse");
    Harness.Eq(3, banker._stashedCoins, "treasury debited exactly once per call");
    Harness.Eq(3, Harness.BankLive(), "shared ledger tracks each debit");
    Harness.False(CoinCourierEconomy.InCall, "economy guard released");
});

Harness.Test("bagging Greek primes from the shared ledger before debiting", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Fixture.PublicDocument(7);
    Banker banker = Fixture.NewBanker(5);

    var purse = new CoinCourierPurse();
    CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);

    Harness.Status(CoinCourierStatus.Applied, result, "bag applied");
    Harness.Eq(1, purse.Coins, "purse credited");
    Harness.Eq(6, banker._stashedCoins, "prime adopted 7, then one coin was debited");
    Harness.Eq(6, Harness.BankLive(), "shared ledger follows the debit");
});

Harness.Test("courier stash hint leaves an unprimed Greek ledger untouched", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Fixture.PublicDocument(7);
    Banker banker = Fixture.NewBanker(0, apply: false);
    int staged = PlayerPrefs.SetKeys.Count;
    Harness.False(PatchEconomy_Banker.TryReadCourierStash(banker, out _),
        "native zero is unknown before Greek shared-ledger priming");
    Harness.Eq(0, banker._stashedCoins, "the hint did not write the native bank");
    Harness.Eq(7, Harness.BankLive(), "the hint did not alter shared funds");
    Harness.Eq(staged, PlayerPrefs.SetKeys.Count, "the hint staged no ledger write");

    var purse = new CoinCourierPurse();
    Harness.Status(CoinCourierStatus.Applied,
        CoinCourierEconomy.TryBagOneCoin(purse, banker, 4), "the existing transaction primes and bags");
    Harness.True(PatchEconomy_Banker.TryReadCourierStash(banker, out int remaining),
        "the same banker and world now have an authoritative read-only hint");
    Harness.Eq(6, remaining, "the hint sees the post-commit shared balance");
});

Harness.Test("courier known-empty hint reads native and primed Greek banks without writes", () =>
{
    Fixture.NewWorld(1);
    Banker native = Fixture.NewBanker(0);
    UnityEngine.PlayerPrefs.Ints["MyMod_SharedBankStash"] = 91;
    int staged = PlayerPrefs.SetKeys.Count;
    Harness.True(PatchEconomy_Banker.TryReadCourierStash(native, out int nativeCoins),
        "an identified non-Greek banker has a readable native balance");
    Harness.Eq(0, nativeCoins, "empty native bank is known empty");
    Harness.Eq(staged, PlayerPrefs.SetKeys.Count, "native hint staged no shared write");
    Harness.Eq(91, PlayerPrefs.Ints["MyMod_SharedBankStash"], "native hint ignored the legacy key");

    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker greek = Fixture.NewBanker(0);
    var purse = new CoinCourierPurse();
    Harness.Reason(CoinCourierReason.BankEmpty,
        CoinCourierEconomy.TryBagOneCoin(purse, greek, 4), "the original transaction establishes a zero prime");
    staged = PlayerPrefs.SetKeys.Count;
    Harness.True(PatchEconomy_Banker.TryReadCourierStash(greek, out int greekCoins),
        "zero is known only after the same-world Greek prime");
    Harness.Eq(0, greekCoins, "primed Greek bank is known empty");
    Harness.Eq(staged, PlayerPrefs.SetKeys.Count, "Greek hint staged no additional ledger write");
});

Harness.Test("bagging a non-Greek world never touches the shared ledger", () =>
{
    Fixture.NewWorld(1); // explicitly not Greece
    Banker banker = Fixture.NewBanker(5);
    UnityEngine.PlayerPrefs.Ints["MyMod_SharedBankStash"] = 99;

    var purse = new CoinCourierPurse();
    CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);

    Harness.Status(CoinCourierStatus.Applied, result, "bag applied");
    Harness.Eq(1, purse.Coins, "purse credited");
    Harness.Eq(4, banker._stashedCoins, "native treasury debited");
    Harness.False(SharedBankNative.TryLive(out _, out _, out _), "foreign unowned bank does not seed public Live");
    Harness.NoLegacyBankWrite("foreign no legacy write");
    Harness.Eq(4, Managers.Inst.stats.StatCalls[Stat.CoinsInBank], "display refreshed with the native value");
});

Harness.Test("bagging an empty treasury is not applied", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(0);
    var purse = new CoinCourierPurse();

    CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.NotApplied, result, "empty treasury refused");
    Harness.Reason(CoinCourierReason.BankEmpty, result, "empty reason");
    Harness.Eq(0, purse.Coins, "purse unchanged");
    Harness.Eq(0, banker._stashedCoins, "treasury unchanged");
    Harness.False(purse.IsFaulted, "a clean refusal does not fault the purse");
});

Harness.Test("bagging rejects pause, online, disabled mod, non-authority and a stale banker", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();

    void CheckBag(CoinCourierReason expected, int stash, string label)
    {
        CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
        Harness.Status(CoinCourierStatus.NotApplied, result, label + ": not applied");
        Harness.Reason(expected, result, label + ": reason");
        Harness.Eq(0, purse.Coins, label + ": purse unchanged");
        Harness.Eq(stash, banker._stashedCoins, label + ": treasury unchanged");
        Harness.False(purse.IsFaulted, label + ": purse not faulted");
    }

    Time.timeScale = 0f;
    CheckBag(CoinCourierReason.Paused, 5, "paused");
    Time.timeScale = 1f;

    NetworkBigBoss.IsOnline = true;
    CheckBag(CoinCourierReason.Online, 5, "online");
    NetworkBigBoss.IsOnline = false;

    ModConfig.Enabled.Value = false;
    CheckBag(CoinCourierReason.ModDisabled, 5, "mod disabled");
    ModConfig.Enabled.Value = true;

    NetworkBigBoss.HasWorldAuth = false;
    CheckBag(CoinCourierReason.NoAuthority, 5, "no world authority");
    NetworkBigBoss.HasWorldAuth = true;

    Managers.Inst.game.state = Game.State.Menu;
    CheckBag(CoinCourierReason.WorldNotReady, 5, "world not playing");
    Managers.Inst.game.state = Game.State.Playing;

    // 903 登记缺失（读档未完成/旧场景残留）：拒绝且不扣款。
    Fixture.ClearBanker903();
    CheckBag(CoinCourierReason.BankGateClosed, 5, "no 903 registration");
    Fixture.RegisterBanker903(banker);

    NetworkPostbox session = NetworkPostbox.Instance;
    NetworkPostbox.Instance = null;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "no postbox");
    NetworkPostbox.Instance = session;

    CRPCHeader header = banker.parentHeaderRef;
    header.HeaderType = CRPCType.SemiStatic;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "903 entry is not dynamic");
    header.HeaderType = CRPCType.Dynamic;

    header.NetID = 904;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "903 entry carries another id");
    header.NetID = CoinCourierBankScope.CourierBankerNetId;

    header.referencedGO = null;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "903 entry has no object");
    header.referencedGO = banker.gameObject;

    header.referencedGO = Sim.NewActor("NotABanker", Managers.Inst.world.gameLayer);
    CheckBag(CoinCourierReason.BankGateClosed, 5, "903 entry object has no Banker");
    header.referencedGO = banker.gameObject;

    CRPCHeader backReference = banker.parentHeaderRef;
    banker.parentHeaderRef = new CRPCHeader { NetID = 903, HeaderType = CRPCType.Dynamic };
    CheckBag(CoinCourierReason.BankGateClosed, 5, "back-reference header is another instance");
    banker.parentHeaderRef = backReference;

    Banker other = Sim.NewActor("Banker2", Managers.Inst.world.gameLayer).AddComponent<Banker>();
    Fixture.RegisterBanker903(other);
    CheckBag(CoinCourierReason.BankGateClosed, 5, "native reference conflicts with the 903 body");
    Fixture.RegisterBanker903(banker);

    banker.enabled = false;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "banker disabled");
    banker.enabled = true;

    banker.gameObject.SetActive(false);
    CheckBag(CoinCourierReason.BankGateClosed, 5, "banker object inactive");
    banker.gameObject.SetActive(true);

    BiomeHolder.Inst.FailRead = true;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "biome read fault");
    BiomeHolder.Inst.FailRead = false;

    Managers.Inst.world.gameLayer = Sim.NewLayer("OtherLayer").transform;
    CheckBag(CoinCourierReason.BankGateClosed, 5, "banker left the current layer");
});

Harness.Test("bagging accepts a loaded save (903 registered, native field null) and a fresh body", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();

    // 实际 2.4 读档：Castle.CatchupToLevel 见到 903 已登记就跳过 kingdom.banker 赋值。
    Managers.Inst.kingdom.banker = null;
    CoinCourierResult loaded = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, loaded, "903 entry alone is the authoritative identity");
    Harness.Eq(1, purse.Coins, "loaded-save purse credited");
    Harness.Eq(4, banker._stashedCoins, "loaded-save treasury debited once");

    // 新鲜档：原生引用与 903 指向同一本体 → 接受。
    Managers.Inst.kingdom.banker = banker;
    CoinCourierResult fresh = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, fresh, "fresh-save same body accepted");
    Harness.Eq(2, purse.Coins, "fresh-save purse credited");
    Harness.Eq(3, banker._stashedCoins, "fresh-save treasury debited once");
});

Harness.Test("identity loss between bags never double-debits; re-registration reopens", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();

    Harness.Status(CoinCourierStatus.Applied, CoinCourierEconomy.TryBagOneCoin(purse, banker, 4), "first bag applied");

    Fixture.ClearBanker903();
    CoinCourierResult blocked = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.NotApplied, blocked, "identity loss blocks the next bag");
    Harness.Reason(CoinCourierReason.BankGateClosed, blocked, "identity-loss reason");
    Harness.Eq(4, banker._stashedCoins, "treasury not debited again");
    Harness.Eq(1, purse.Coins, "purse untouched");
    Harness.False(purse.IsFaulted, "a definite refusal is not a fault");

    Fixture.RegisterBanker903(banker);
    CoinCourierResult recovered = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, recovered, "re-registration restores bagging");
    Harness.Eq(2, purse.Coins, "purse credited once after recovery");
    Harness.Eq(3, banker._stashedCoins, "treasury debited once after recovery");
});

Harness.Test("bank resolver classifies refusals for the treasury status line", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    Kingdom kingdom = Managers.Inst.kingdom;

    Harness.True(CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out Banker resolved,
        out CoinCourierBankReason reason), "healthy identity resolves");
    Harness.True(resolved != null && resolved.Pointer == banker.Pointer, "resolved body is the 903 body");
    Harness.Eq((int)CoinCourierBankReason.Ready, (int)reason, "healthy reason");

    Fixture.ClearBanker903();
    Harness.False(CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out _, out reason), "missing entry refused");
    Harness.Eq((int)CoinCourierBankReason.EntryMissing, (int)reason, "missing entry reason");
    Fixture.RegisterBanker903(banker);

    ModConfig.Enabled.Value = false;
    Harness.False(CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out _, out reason), "mod off refused");
    Harness.Eq((int)CoinCourierBankReason.ModDisabled, (int)reason, "mod-off reason");
    ModConfig.Enabled.Value = true;

    NetworkBigBoss.HasWorldAuth = false;
    Harness.False(CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out _, out reason), "no authority refused");
    Harness.Eq((int)CoinCourierBankReason.NoWorldAuthority, (int)reason, "no-authority reason");
    NetworkBigBoss.HasWorldAuth = true;

    Managers.Inst.game.state = Game.State.Menu;
    Harness.False(CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out _, out reason), "not playing refused");
    Harness.Eq((int)CoinCourierBankReason.NotPlaying, (int)reason, "not-playing reason");
    Managers.Inst.game.state = Game.State.Playing;

    Banker foreign = Sim.NewActor("Banker2", Managers.Inst.world.gameLayer).AddComponent<Banker>();
    Fixture.RegisterBanker903(foreign);
    Managers.Inst.kingdom.banker = banker;
    Harness.False(CoinCourierBankScope.TryGetCurrentAuthorityBanker(kingdom, out _, out reason), "native conflict refused");
    Harness.Eq((int)CoinCourierBankReason.NativeConflict, (int)reason, "native-conflict reason");
    Fixture.RegisterBanker903(banker);
});

Harness.Test("bank entry reasons are exact when called directly", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);

    Time.timeScale = 0f;
    CourierBankDebit paused = PatchEconomy_Banker.TryWithdrawOneCoinForCourier(banker);
    Harness.Eq((int)CourierBankOutcome.NotApplied, (int)paused.Outcome, "direct paused outcome");
    Harness.Eq((int)CourierBankReason.Paused, (int)paused.Reason, "direct paused reason");
    Time.timeScale = 1f;

    Managers.Inst.game.state = Game.State.Menu;
    CourierBankDebit notPlaying = PatchEconomy_Banker.TryWithdrawOneCoinForCourier(banker);
    Harness.Eq((int)CourierBankOutcome.NotApplied, (int)notPlaying.Outcome, "direct not-playing outcome");
    Harness.Eq((int)CourierBankReason.GateClosed, (int)notPlaying.Reason, "direct not-playing reason");
    Harness.Eq(-1, notPlaying.Before, "unreadable before is unknown");
    Managers.Inst.game.state = Game.State.Playing;

    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker empty = Fixture.NewBanker(0);
    CourierBankDebit emptyResult = PatchEconomy_Banker.TryWithdrawOneCoinForCourier(empty);
    Harness.Eq((int)CourierBankOutcome.NotApplied, (int)emptyResult.Outcome, "direct empty outcome");
    Harness.Eq((int)CourierBankReason.Empty, (int)emptyResult.Reason, "direct empty reason");
    Harness.Eq(0, emptyResult.Before, "empty before is zero");

    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker unreadable = Fixture.NewBanker(5);
    unreadable.ThrowOnStashRead = true;
    CourierBankDebit unreadableResult = PatchEconomy_Banker.TryWithdrawOneCoinForCourier(unreadable);
    Harness.Eq((int)CourierBankOutcome.NotApplied, (int)unreadableResult.Outcome, "direct unreadable outcome");
    Harness.Eq((int)CourierBankReason.Unreadable, (int)unreadableResult.Reason, "direct unreadable reason");
    unreadable.ThrowOnStashRead = false;
});

Harness.Test("bagging a pre-commit write fault is NotApplied and retry is safe", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();
    banker.WriteFaultWhen = value => value == 4; // only the one-coin debit, not the priming write

    CoinCourierResult failed = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.NotApplied, failed, "write fault refused");
    Harness.Reason(CoinCourierReason.WriteFault, failed, "write-fault reason");
    Harness.Eq(0, purse.Coins, "purse not credited");
    Harness.Eq(5, banker._stashedCoins, "treasury not debited");
    Harness.False(purse.IsFaulted, "a proven no-op does not fault the purse");

    banker.WriteFaultWhen = null;
    CoinCourierResult retry = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, retry, "retry applied exactly once");
    Harness.Eq(1, purse.Coins, "purse credited once");
    Harness.Eq(4, banker._stashedCoins, "treasury debited once");
});

Harness.Test("bagging a write that lands before throwing still commits", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();
    banker.WriteAfterFaultWhen = value => value == 4;

    CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, result, "read-back confirms the commit");
    Harness.Reason(CoinCourierReason.WriteFault, result, "write-fault reason after commit");
    Harness.Eq(1, purse.Coins, "purse credited");
    Harness.Eq(4, banker._stashedCoins, "treasury debited exactly once");
    Harness.False(purse.IsFaulted, "confirmed commit is not a fault");
});

Harness.Test("bagging an unverifiable treasury write freezes the purse with evidence", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();
    banker.WriteFaultWhen = value => value == 4;
    banker.OnStashWriteAttempt = value => { if (value == 4) banker.ThrowOnStashRead = true; };

    CoinCourierResult result = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Indeterminate, result, "unknown write is indeterminate");
    Harness.Reason(CoinCourierReason.BankWriteUnknown, result, "unknown reason");
    Harness.True(purse.IsFaulted, "purse frozen");
    Harness.Eq((int)CoinCourierFaultKind.BagUnknown, (int)purse.Fault.Kind, "bag fault kind");
    Harness.Eq(5, purse.Fault.BankBefore, "before evidence kept");
    Harness.Eq(-1, purse.Fault.BankAfter, "unknown after kept unknown");
    Harness.Eq(0, purse.Coins, "purse never credits on a guess");
    Harness.False(CoinCourierEconomy.InCall, "economy guard released");

    CoinCourierResult frozen = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.NotApplied, frozen, "further economy stopped");
    Harness.Reason(CoinCourierReason.Frozen, frozen, "frozen reason");
});

Harness.Test("bagging survives presentation faults and never writes legacy prefs", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    var purse = new CoinCourierPurse();

    Managers.Inst.kingdom.castle.ThrowOnSetStash = true;
    CoinCourierResult display = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, display, "display fault still applied");
    Harness.Reason(CoinCourierReason.PresentationFailed, display, "display-fault reason");
    Harness.Eq(4, banker._stashedCoins, "treasury debited exactly once");
    Harness.Eq(1, purse.Coins, "purse credited exactly once");
    Managers.Inst.kingdom.castle.ThrowOnSetStash = false;

    PlayerPrefs.ThrowOnSet = true;
    CoinCourierResult staging = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.Applied, staging, "staging fault still applied");
    Harness.Reason(CoinCourierReason.None, staging, "legacy prefs fault does not affect native commit");
    Harness.Eq(3, banker._stashedCoins, "second debit not repeated");
    Harness.Eq(2, purse.Coins, "second credit not repeated");
    Harness.Eq(3, Harness.BankLive(), "R3 live follows the committed debit");
    Harness.NoLegacyBankWrite("no PlayerPrefs bank persistence");
    PlayerPrefs.ThrowOnSet = false;
});

Harness.Test("bank scope facade mirrors the 903 resolver in both directions", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);

    Harness.True(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "scope accepts the 903 body");

    Managers.Inst.kingdom.banker = null;
    Harness.True(CoinCourierBankScope.IsCurrentAuthorityBanker(banker),
        "native field null with a live 903 entry is accepted (loaded save)");
    Managers.Inst.kingdom.banker = banker;

    Banker other = Sim.NewActor("Banker2", Managers.Inst.world.gameLayer).AddComponent<Banker>();
    Managers.Inst.kingdom.banker = other;
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "native conflict rejected");
    Managers.Inst.kingdom.banker = banker;

    Fixture.ClearBanker903();
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "missing 903 rejected");
    Fixture.RegisterBanker903(banker);
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(other), "another body never passes");
    Harness.True(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "re-registration restores the gate");

    banker.enabled = false;
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "disabled banker rejected");
    banker.enabled = true;

    ModConfig.Enabled.Value = false;
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "mod disabled rejected");
    ModConfig.Enabled.Value = true;

    NetworkBigBoss.HasWorldAuth = false;
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "no authority rejected");
    NetworkBigBoss.HasWorldAuth = true;

    Managers.Inst.game.state = Game.State.Menu;
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "not playing rejected");
    Managers.Inst.game.state = Game.State.Playing;

    Managers.Inst.world.gameLayer = Sim.NewLayer("LaterLayer").transform;
    Harness.False(CoinCourierBankScope.IsCurrentAuthorityBanker(banker), "stale layer rejected");
});

// =========================================================================
// Delivery: purse -> Knight wallet
// =========================================================================

Harness.Test("delivery credits the Knight without any bank and without persistence writes", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(41, 0, 8);
    CoinCourierPurse purse = PurseWith(2);
    int prefCalls = PlayerPrefs.SetIntCalls;

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 41);
    Harness.Status(CoinCourierStatus.Applied, result, "delivery applied");
    Harness.Reason(CoinCourierReason.None, result, "no fault reason");
    Harness.Eq(1, knight.Wallet.Coins, "wallet credited one");
    Harness.Eq(1, purse.Coins, "purse consumed one");
    Harness.False(purse.HasPendingDelivery, "reservation cleared");
    Harness.Eq(1, purse.AvailableCoins, "one spendable coin left");
    Harness.Eq(prefCalls, PlayerPrefs.SetIntCalls, "delivery never stages persistence");
    Harness.True(Managers.Inst.kingdom.banker == null, "no banker was needed");
    Harness.False(CoinCourierEconomy.InCall, "economy guard released");
});

Harness.Test("delivery ignores an empty treasury", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(0);
    Knight knight = Fixture.NewKnight(42, 0, 8);
    CoinCourierPurse purse = PurseWith(1);

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 42);
    Harness.Status(CoinCourierStatus.Applied, result, "delivery applied from the purse");
    Harness.Eq(1, knight.Wallet.Coins, "wallet credited");
    Harness.Eq(0, purse.Coins, "purse emptied");
    Harness.Eq(0, banker._stashedCoins, "treasury untouched by delivery");
});

Harness.Test("delivery refuses a full wallet and retries after space frees", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(43, 8, 8);
    CoinCourierPurse purse = PurseWith(1);

    CoinCourierResult full = CoinCourierEconomy.TryDeliverOne(purse, knight, 43);
    Harness.Status(CoinCourierStatus.NotApplied, full, "full wallet refused");
    Harness.Reason(CoinCourierReason.WalletFull, full, "wallet-full reason");
    Harness.Eq(1, purse.Coins, "purse unchanged");
    Harness.False(purse.HasPendingDelivery, "reservation returned");
    Harness.Eq(8, knight.Wallet.Coins, "wallet unchanged");

    knight.Wallet.SetCurrency(CurrencyType.Coins, 7);
    CoinCourierResult retry = CoinCourierEconomy.TryDeliverOne(purse, knight, 43);
    Harness.Status(CoinCourierStatus.Applied, retry, "retry applied after space frees");
    Harness.Eq(8, knight.Wallet.Coins, "wallet now full");
    Harness.Eq(0, purse.Coins, "purse consumed the coin");
});

Harness.Test("delivery target validation rejects dead, grabbed, inert, embarking and stale actors", () =>
{
    void ExpectTargetInvalid(string label, Action<Knight> mutate, bool walletReadable = true)
    {
        var (result, purse, knight) = DeliverCase(61, mutate);
        Harness.Status(CoinCourierStatus.NotApplied, result, label + ": status");
        Harness.Reason(CoinCourierReason.TargetInvalid, result, label + ": reason");
        Harness.Eq(1, purse.Coins, label + ": purse unchanged");
        Harness.False(purse.HasPendingDelivery, label + ": reservation returned");
        Harness.False(purse.IsFaulted, label + ": a clean refusal never faults");
        if (walletReadable) Harness.Eq(0, knight.Wallet?.Coins ?? 0, label + ": wallet untouched");
    }

    ExpectTargetInvalid("dead knight", knight => knight._damageable.isDead = true);
    ExpectTargetInvalid("grabbed knight", knight => knight._character.grabbed = true);
    ExpectTargetInvalid("inert knight", knight => knight._character.inert = true);
    ExpectTargetInvalid("embarking knight", knight => knight._fsm.Current = Knight.State.Embarking);
    ExpectTargetInvalid("embarked knight", knight => knight._embarkee.IsEmbarked = true);
    ExpectTargetInvalid("targeting embark", knight => knight._embarkee.IsTargetingEmbarkable = true);
    ExpectTargetInvalid("embarkable target",
        knight => knight._embarkee.EmbarkableTarget = Sim.NewActor("Boat", Managers.Inst.world.gameLayer));
    ExpectTargetInvalid("side-less knight", knight => knight.side = (Side)7);
    ExpectTargetInvalid("missing wallet", knight => knight.Wallet = null, walletReadable: false);
    ExpectTargetInvalid("unreadable wallet", knight => knight.Wallet.ThrowOnRead = true, walletReadable: false);
    ExpectTargetInvalid("stale layer",
        knight => Managers.Inst.world.gameLayer = Sim.NewLayer("OtherLayer").transform);

    // Wrong lifetime cannot be delivered even though everything else is valid.
    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight live = Fixture.NewKnight(61, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    CoinCourierResult wrongLife = CoinCourierEconomy.TryDeliverOne(purse, live, 62);
    Harness.Status(CoinCourierStatus.NotApplied, wrongLife, "wrong lifetime: status");
    Harness.Reason(CoinCourierReason.TargetInvalid, wrongLife, "wrong lifetime: reason");
    Harness.Eq(1, purse.Coins, "wrong lifetime: purse unchanged");
    Harness.Eq(0, live.Wallet.Coins, "wrong lifetime: wallet untouched");
});

Harness.Test("delivery is gated by pause, online, disabled mod, authority and world state", () =>
{
    void ExpectGate(CoinCourierReason expected, string label, Action setup)
    {
        var (result, purse, _) = DeliverWorldCase(71, setup);
        Harness.Status(CoinCourierStatus.NotApplied, result, label + ": status");
        Harness.Reason(expected, result, label + ": reason");
        Harness.Eq(1, purse.Coins, label + ": purse unchanged");
        Harness.False(purse.HasPendingDelivery, label + ": no reservation left behind");
    }

    ExpectGate(CoinCourierReason.Paused, "paused", () => Time.timeScale = 0f);
    ExpectGate(CoinCourierReason.Online, "online", () => NetworkBigBoss.IsOnline = true);
    ExpectGate(CoinCourierReason.ModDisabled, "mod disabled", () => ModConfig.Enabled.Value = false);
    ExpectGate(CoinCourierReason.NoAuthority, "no authority", () => NetworkBigBoss.HasWorldAuth = false);
    ExpectGate(CoinCourierReason.WorldNotReady, "not playing", () => Managers.Inst.game.state = Game.State.Menu);

    Harness.ResetStatics();
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    var purse = PurseWith(1);
    CoinCourierResult nullKnight = CoinCourierEconomy.TryDeliverOne(purse, null, 71);
    Harness.Status(CoinCourierStatus.NotApplied, nullKnight, "null knight refused");
    Harness.Reason(CoinCourierReason.InvalidArgument, nullKnight, "null knight reason");
    CoinCourierResult zeroLife = CoinCourierEconomy.TryDeliverOne(purse, Fixture.NewKnight(71, 0, 8), 0);
    Harness.Status(CoinCourierStatus.NotApplied, zeroLife, "zero lifetime refused");
    Harness.Reason(CoinCourierReason.InvalidArgument, zeroLife, "zero lifetime reason");
});

Harness.Test("world gate read faults close the gate instead of guessing", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Banker banker = Fixture.NewBanker(5);
    Knight knight = Fixture.NewKnight(81, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    Managers.Inst.game.FailStateRead = true;

    CoinCourierResult bag = CoinCourierEconomy.TryBagOneCoin(purse, banker, 4);
    Harness.Status(CoinCourierStatus.NotApplied, bag, "bag gate fault refused");
    Harness.Reason(CoinCourierReason.WorldNotReady, bag, "bag gate fault reason");
    CoinCourierResult deliver = CoinCourierEconomy.TryDeliverOne(purse, knight, 81);
    Harness.Status(CoinCourierStatus.NotApplied, deliver, "delivery gate fault refused");
    Harness.Reason(CoinCourierReason.WorldNotReady, deliver, "delivery gate fault reason");

    Harness.Eq(1, purse.Coins, "purse unchanged by the gate fault");
    Harness.False(purse.HasPendingDelivery, "no reservation left behind");
    Harness.False(purse.IsFaulted, "a closed gate is not an economic fault");
    Harness.Eq(5, banker._stashedCoins, "treasury unchanged by the gate fault");
    Harness.Eq(0, knight.Wallet.Coins, "wallet unchanged by the gate fault");
    Harness.False(CoinCourierEconomy.InCall, "economy guard released after gate faults");
});

Harness.Test("delivery whose wallet write never lands returns the reserved coin", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(44, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    knight.Wallet.BeforeWrite = (_, _) => throw new InvalidOperationException("wallet write fault");

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 44);
    Harness.Status(CoinCourierStatus.NotApplied, result, "no-effect write refused");
    Harness.Reason(CoinCourierReason.WalletRejected, result, "wallet-rejected reason");
    Harness.Eq(0, knight.Wallet.Coins, "wallet unchanged");
    Harness.Eq(1, purse.Coins, "purse unchanged");
    Harness.False(purse.HasPendingDelivery, "reservation returned");
    Harness.Eq(1, purse.AvailableCoins, "coin is spendable again");

    knight.Wallet.BeforeWrite = null;
    CoinCourierResult retry = CoinCourierEconomy.TryDeliverOne(purse, knight, 44);
    Harness.Status(CoinCourierStatus.Applied, retry, "retry applied exactly once");
    Harness.Eq(1, knight.Wallet.Coins, "wallet credited once");
    Harness.Eq(0, purse.Coins, "purse consumed once");
});

Harness.Test("delivery whose wallet write lands before throwing is still applied", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(45, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    knight.Wallet.AfterWrite = (_, _) => throw new InvalidOperationException("wallet write fault after commit");

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 45);
    Harness.Status(CoinCourierStatus.Applied, result, "read-back confirms the credit");
    Harness.Reason(CoinCourierReason.WriteFault, result, "write-fault reason after commit");
    Harness.Eq(1, knight.Wallet.Coins, "wallet credited exactly once");
    Harness.Eq(0, purse.Coins, "purse consumed exactly once");
    Harness.False(purse.IsFaulted, "confirmed credit is not a fault");
});

Harness.Test("delivery read-back failure freezes the purse and keeps the pending coin", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(46, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    knight.Wallet.AfterWrite = (_, _) => knight.Wallet.ThrowOnRead = true;

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 46);
    Harness.Status(CoinCourierStatus.Indeterminate, result, "unverifiable delivery is indeterminate");
    Harness.Reason(CoinCourierReason.WalletWriteUnknown, result, "unknown reason");
    Harness.True(purse.IsFaulted, "purse frozen");
    Harness.Eq((int)CoinCourierFaultKind.DeliveryUnknown, (int)purse.Fault.Kind, "delivery fault kind");
    Harness.Eq(0, purse.Fault.WalletBefore, "wallet before evidence");
    Harness.Eq(-1, purse.Fault.WalletAfter, "unreadable after kept unknown");
    Harness.Eq(46, purse.Fault.ExpectedLife, "expected lifetime recorded");
    Harness.Eq(1, purse.Coins, "coin never leaves the purse on a guess");
    Harness.True(purse.HasPendingDelivery, "pending handoff retained");
    Harness.Eq(0, purse.AvailableCoins, "pending coin is not spendable again");
    Harness.False(CoinCourierEconomy.InCall, "economy guard released");

    knight.Wallet.ThrowOnRead = false;
    Harness.Eq(1, knight.Wallet.Coins, "the wallet did receive the coin; the fault is honest about it");

    CoinCourierResult frozen = CoinCourierEconomy.TryDeliverOne(purse, knight, 46);
    Harness.Status(CoinCourierStatus.NotApplied, frozen, "further delivery stopped");
    Harness.Reason(CoinCourierReason.Frozen, frozen, "frozen delivery reason");
});

Harness.Test("delivery identity loss after the write freezes the purse", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(47, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    knight.Wallet.AfterWrite = (_, _) => knight._damageable.isDead = true;

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 47);
    Harness.Status(CoinCourierStatus.Indeterminate, result, "dead target after write is indeterminate");
    Harness.Reason(CoinCourierReason.WalletWriteUnknown, result, "identity-loss reason");
    Harness.True(purse.IsFaulted, "purse frozen");
    Harness.True(purse.HasPendingDelivery, "pending retained");
    Harness.Eq(0, purse.Fault.WalletBefore, "wallet before evidence");
    Harness.Eq(1, purse.Fault.WalletAfter, "observed wallet value kept as evidence");
});

Harness.Test("delivery wallet replacement after the write freezes the purse", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(48, 0, 8);
    CoinCourierPurse purse = PurseWith(1);
    knight.Wallet.AfterWrite = (_, _) =>
        knight.Wallet = Sim.NewActor("Wallet2", Managers.Inst.world.gameLayer).AddComponent<Wallet>();

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 48);
    Harness.Status(CoinCourierStatus.Indeterminate, result, "replaced wallet is indeterminate");
    Harness.Reason(CoinCourierReason.WalletWriteUnknown, result, "wallet-replacement reason");
    Harness.True(purse.IsFaulted, "purse frozen");
    Harness.True(purse.HasPendingDelivery, "pending retained");
    Harness.Eq(0, knight.Wallet.Coins, "the new wallet was never credited");
});

Harness.Test("delivery re-entrancy is refused without a second credit", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(49, 0, 8);
    CoinCourierPurse purse = PurseWith(2);
    CoinCourierResult nested = default;
    knight.Wallet.BeforeWrite = (_, _) => nested = CoinCourierEconomy.TryDeliverOne(purse, knight, 49);

    CoinCourierResult result = CoinCourierEconomy.TryDeliverOne(purse, knight, 49);
    Harness.Status(CoinCourierStatus.Applied, result, "outer delivery applied");
    Harness.Status(CoinCourierStatus.NotApplied, nested, "nested delivery refused");
    Harness.Reason(CoinCourierReason.Reentrant, nested, "nested reason");
    Harness.Eq(1, knight.Wallet.Coins, "wallet credited exactly once");
    Harness.Eq(1, purse.Coins, "purse consumed exactly once");
});

Harness.Test("delivery repeats once per coin and stops when the purse empties", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(50, 0, 8);
    CoinCourierPurse purse = PurseWith(2);

    Harness.Status(CoinCourierStatus.Applied, CoinCourierEconomy.TryDeliverOne(purse, knight, 50), "first delivery");
    Harness.Status(CoinCourierStatus.Applied, CoinCourierEconomy.TryDeliverOne(purse, knight, 50), "second delivery");
    Harness.Eq(2, knight.Wallet.Coins, "wallet credited twice");
    Harness.Eq(0, purse.Coins, "purse emptied");

    CoinCourierResult drained = CoinCourierEconomy.TryDeliverOne(purse, knight, 50);
    Harness.Status(CoinCourierStatus.NotApplied, drained, "drained purse refused");
    Harness.Reason(CoinCourierReason.NoAvailableCoins, drained, "drained reason");
    Harness.Eq(2, knight.Wallet.Coins, "no extra credit");
});

// =========================================================================
// Targeting: roster selection against the purse
// =========================================================================

Harness.Test("targeting selects on purse coins and shares the candidate validation", () =>
{
    Fixture.NewWorld(BiomeHolder.GreeceBiomeIndex);
    Knight knight = Fixture.NewKnight(51, 0, 8);
    UnitScanCache.Knights = new[] { knight };

    Harness.False(CoinCourierTargeting.TrySelect(0, 10f, -1, 4, null, out _, out _),
        "an empty purse cannot dispatch a visit");

    Harness.True(CoinCourierTargeting.TrySelect(3, 10f, -1, 4, null, out Knight target,
        out CoinCourierRules.VisitPlan plan), "purse coins allow a visit");
    Harness.True(ReferenceEquals(target, knight), "the live knight is selected");
    Harness.Eq(51L, plan.LifeId, "plan lifetime");
    Harness.Eq(3, plan.CoinsToSend, "purse balance bounds the visit");

    Harness.True(CoinCourierTargeting.IsCurrentDeliveryCandidate(knight, 51, out Wallet wallet)
        && wallet != null, "candidate check accepts the live knight");
    Harness.False(CoinCourierTargeting.IsCurrentDeliveryCandidate(knight, 52, out _),
        "candidate check rejects a wrong lifetime");

    knight._damageable.isDead = true;
    Harness.False(CoinCourierTargeting.TrySelect(3, 10f, -1, 4, null, out _, out _),
        "dead knights are never selected");
    Harness.False(CoinCourierTargeting.IsCurrentDeliveryCandidate(knight, 51, out _),
        "candidate check rejects a dead knight");
});

NativeCases.Run();
return Harness.Finish();
