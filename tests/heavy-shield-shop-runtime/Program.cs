using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string name) { _checks++; if (!value) throw new Exception("FAIL " + name); }
    private static void Next() { Time.frameCount++; Time.unscaledTime += .6f; Time.time += .6f; }
    private static void Setup(bool mold = false, bool greek = false)
    {
        // Each case is an independent process/world in effect, without running Unity/IL2CPP initialization.
        foreach (var name in new[] { "_object", "_points", "_kingdom", "_layer", "_postbox", "_renderers", "_bowPrefab", "_bowPool", "_pools", "_paidBow" })
            typeof(HeavyShieldShopShell).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
        foreach (var name in new[] { "_clearing", "_retiring", "_menuSuspended", "_failureLogged", "_economyFault" })
            typeof(HeavyShieldShopShell).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        foreach (var name in new[] { "_retryAt", "_cleanupRetryAt", "_nextBowCheck" })
            typeof(HeavyShieldShopShell).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0f);
        HeavyShieldIdentity.Ready = true; HeavyShieldIdentity.Unknown = false; HeavyShieldIdentity.Mold = mold;
        HeavyShieldIdentity.LeftExtra = false; HeavyShieldIdentity.RightExtra = false;
        HeavyShieldIdentity.Pending.Clear(); HeavyShieldIdentity.Paid.Clear();
        HeavyShieldIdentity.Reserves = HeavyShieldIdentity.GemCommits = HeavyShieldIdentity.BowBinds = HeavyShieldIdentity.Cancels = HeavyShieldIdentity.UnknownCalls = 0;
        HeavyShieldIdentity.ReserveCalls = HeavyShieldIdentity.SeenReceiptCount = 0;
        HeavyShieldIdentity.ReserveRefused = HeavyShieldIdentity.ReserveThrows = HeavyShieldIdentity.ReserveMalformed = HeavyShieldIdentity.ReserveThrowsAfterReservation = false;
        HeavyShieldIdentity.GemResult = HeavyShieldIdentity.BowResult = HeavyShieldCommitResult.Applied;
        HeavyShieldRuntime.CarrierPreflightReady = true;
        Pool.SpawnCalls = 0; Pool.ReturnNull = Pool.ThrowAfterSpawn = false; Pool.ReturnExisting = Pool.LastIssued = null;
        NetworkBigBoss.IsOnline = false; NetworkBigBoss.HasWorldAuth = true; IslandSaveData.isSavingGame = false;
        Time.unscaledTime = 100; Time.time = 100; Time.frameCount = 10; NetworkPostbox.Instance = new();
        BiomeHolder.Inst = greek ? new() { BiomeIndex = BiomeHolder.GreeceBiomeIndex } : null;
        Managers.Inst = new(); var layer = new GameObject("GameLayer"); Managers.Inst.world.gameLayer = layer.transform;
        var kingdom = new GameObject("Kingdom").AddComponent<Kingdom>(); Managers.Inst.kingdom = kingdom;
        kingdom.playerOne = new GameObject("PlayerOne").AddComponent<Player>(); kingdom.playerTwo = new GameObject("PlayerTwo").AddComponent<Player>();
        var pool = new GameObject("ToolBow (Pool)").AddComponent<Pool>(); var prefab = new GameObject("ToolBow") { tag = "Bow" };
        prefab.AddComponent<DroppableTool>(); prefab.AddComponent<Persistent>(); prefab.AddComponent<SpriteRenderer>(); pool.prefab = prefab;
        Managers.Inst.pools = new GameObject("Pools").AddComponent<PoolManager>(); Managers.Inst.pools.BowPool = pool;
        var native = new GameObject("NativeBowShop"); native.transform.SetParent(layer.transform, false); native.transform.position = new(8, .875f, 0);
        var shop = native.AddComponent<PayableShop>(); native.AddComponent<ShopTag>().type = PayableShop.ShopType.Bow;
        native.AddComponent<SpriteRenderer>().sprite = new Sprite { pivot = Vector2.zero }; Managers.Inst.payables.Items.Add(shop);
        HeavyShieldShopShell.Tick(true);
    }
    private static PayableComponent Point(HeavyShieldPurchaseKind kind)
        => Managers.Inst.payables.Items.Single(p => p.gameObject.name == "shield_pay_" + kind);
    private static Player P1 => Managers.Inst.kingdom.playerOne;
    private static Player P2 => Managers.Inst.kingdom.playerTwo;
    private static void Fill(Player payer, CurrencyType currency, int price)
    { payer._floatingCurrency.Clear(); for (int i = 0; i < price; i++) payer._floatingCurrency.Add(new(currency)); }

    private static void FixedPricesAndReadonly()
    {
        Setup(); Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5, "five independently registered headers");
        foreach (var kind in Enum.GetValues<HeavyShieldPurchaseKind>())
        {
            var point = Point(kind); int price = HeavyShieldShopPayment.PriceFor(kind);
            Check(point.Price == price, "fixed price " + kind);
            Check(point.Currency == (HeavyShieldShopPayment.IsGem(kind) ? CurrencyType.Gems : CurrencyType.Coins), "fixed currency " + kind);
            Check(point.priceIncrease == 0, "zero native priceIncrease " + kind);
            Check(point.Owner.OnPay != null, "native owner callback " + kind);
        }
        var mold = Point(HeavyShieldPurchaseKind.Mold);
        Check(mold.Owner.CanPay(P1), "mold initial eligible");
        for (int i = 0; i < 5; i++) mold.Owner.CanPay(P1);
        Check(HeavyShieldIdentity.Reserves == 0, "CanPay never reserves");
        HeavyShieldRuntime.CarrierPreflightReady = false;
        Check(!mold.Owner.CanPay(P1), "carrier preflight prevents first charge");
        HeavyShieldRuntime.CarrierPreflightReady = true; HeavyShieldIdentity.Ready = false;
        Check(!mold.Owner.CanPay(P1), "identity readiness prevents first charge");
    }
    private static void GemPhases()
    {
        Setup();
        foreach (var kind in new[] { HeavyShieldPurchaseKind.Mold, HeavyShieldPurchaseKind.ExtraLeft, HeavyShieldPurchaseKind.ExtraRight })
        {
            var point = Point(kind); Check(point.Owner.CanPay(P1), "gem phase eligible " + kind);
            int writes = point.PriceWrites + point.CurrencyWrites;
            point.Start(P1); Check(HeavyShieldIdentity.Pending.Count == 1, "Started freezes one lease " + kind);
            Check(point.Owner.CanPay(P1), "own armed reservation remains CanPay " + kind);
            Check(!point.Owner.CanPay(P2), "different payer excluded " + kind);
            Fill(P1, CurrencyType.Gems, point.Price); point.CompleteNative(P1, false);
            Check(HeavyShieldIdentity.GemCommits == (int)kind + 1, "one gem commit " + kind);
            Check(point.PriceWrites + point.CurrencyWrites == writes, "OnPay preserves native price/currency " + kind);
            point.Owner.OnPay.Invoke(P1); Check(HeavyShieldIdentity.GemCommits == (int)kind + 1, "repeated OnPay ignored " + kind);
            Check(Point(HeavyShieldPurchaseKind.ShieldLeft).forceBlockPayment, "phase cannot switch during floating cleanup " + kind);
            Next(); HeavyShieldShopShell.Tick(true);
            Check(Point(HeavyShieldPurchaseKind.ShieldLeft).forceBlockPayment, "unsettled native floating still blocks " + kind);
            P1._floatingCurrency.Clear(); P1._completingPayable = null; P1._payState = Player.PayState.None;
            HeavyShieldShopShell.Tick(true); // same frame as earlier tick is now after original Completed frame.
            Check(!Point(HeavyShieldPurchaseKind.ShieldLeft).forceBlockPayment, "later driver phase after native cleanup " + kind);
        }
        Check(HeavyShieldIdentity.Mold && HeavyShieldIdentity.LeftExtra && HeavyShieldIdentity.RightExtra, "separate campaign gem purchases");
        Check(P1.WalletAdds == 0, "no fabricated gem refunds");
    }
    private static void Cancellation()
    {
        Setup(); var point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 2);
        P1.DelayDrop = true; HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave();
        Check(P1.DropCalls > 0 && HeavyShieldIdentity.Cancels == 0, "Drop return with nonempty list is not refund proof");
        Check(HeavyShieldIdentity.Pending.Count == 1, "partial gem keeps lease until actual Clear");
        P1._floatingCurrency.Clear(); P1._payState = Player.PayState.None; P1._completingPayable = null;
        Next(); HeavyShieldShopShell.Tick(true);
        Check(HeavyShieldIdentity.Cancels == 1 && HeavyShieldIdentity.Pending.Count == 0, "confirmed native cleanup releases once");
        HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave(); Next(); HeavyShieldShopShell.Tick(true);
        Check(HeavyShieldIdentity.Cancels == 1 && P1.WalletAdds == 0, "no repeated cancellation or wallet credit");
        Setup(true); point = Point(HeavyShieldPurchaseKind.ShieldRight); point.Start(P1); Fill(P1, CurrencyType.Coins, 3);
        HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave();
        Check(HeavyShieldIdentity.Cancels == 1 && Pool.SpawnCalls == 0, "partial coin cancellation never issues");
        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 1);
        P1.CancelTransaction(); P1.DropFloatingCurrency(); P1._payState = Player.PayState.None; // native release-button flow
        Next(); HeavyShieldShopShell.Tick(true);
        Check(HeavyShieldIdentity.Cancels == 1, "native cancellation observed without explicit save cancellation");
        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 1); P1.ThrowAfterDrop = true;
        HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave();
        Check(HeavyShieldIdentity.Unknown && HeavyShieldIdentity.Cancels == 0, "drop throw locks uncertainty even if list now empty");
    }
    private static void InvalidReceipts()
    {
        Setup(); var point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1);
        Fill(P1, CurrencyType.Gems, 4); P1._floatingCurrency[2].CurrencyType = CurrencyType.Coins; point.CompleteNative(P1);
        Check(HeavyShieldIdentity.GemCommits == 0 && HeavyShieldIdentity.Unknown, "mixed native currencies never commit");
        Check(P1.WalletAdds == 0 && HeavyShieldIdentity.Pending.Count == 1, "mixed floats retain unknown slot without refunds");
        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 3); point.CompleteNative(P1);
        Check(HeavyShieldIdentity.GemCommits == 0 && HeavyShieldIdentity.Unknown, "wrong completed amount locks unknown");
        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 4);
        P1._completingPayable = Point(HeavyShieldPurchaseKind.ExtraLeft); P1._payState = Player.PayState.Completed; point.Owner.OnPay.Invoke(P1);
        Check(HeavyShieldIdentity.GemCommits == 0 && !HeavyShieldIdentity.Unknown, "foreign completing payable ignored");
        P2._completingPayable = point; P2._payState = Player.PayState.Completed; Fill(P2, CurrencyType.Gems, 4); point.Owner.OnPay.Invoke(P2);
        Check(HeavyShieldIdentity.GemCommits == 0, "different Completed payer ignored");
    }
    private static void MoreTests()
    {
        Setup(true); var point = Point(HeavyShieldPurchaseKind.ShieldRight);
        Check(point.Owner.CanPay(P1), "coin recruitment ready"); point.Start(P1);
        Check(!Point(HeavyShieldPurchaseKind.ShieldLeft).Owner.CanPay(P2), "both side points compete for single rack slot");
        point.Start(P2); Check(HeavyShieldIdentity.Reserves == 1, "second Started cannot reserve same slot");
        Fill(P1, CurrencyType.Coins, 6); int writes = point.PriceWrites + point.CurrencyWrites; point.CompleteNative(P1);
        Check(Pool.SpawnCalls == 1 && HeavyShieldIdentity.BowBinds == 1, "one native ToolBow issued and identity-bound");
        Check(HeavyShieldIdentity.Paid[Pool.LastIssued].Side == HeavyShieldQuota.Side.Right, "selected right side stays fixed");
        Check(point.PriceWrites + point.CurrencyWrites == writes, "coin OnPay preserves price/currency");
        point.Owner.OnPay.Invoke(P1); Check(Pool.SpawnCalls == 1, "repeated native completion never reissues");
        Next(); HeavyShieldShopShell.Tick(true);
        Check(!Point(HeavyShieldPurchaseKind.ShieldLeft).Owner.CanPay(P1), "paid tool occupies rack and seat");
        var bow = Pool.LastIssued; var position = bow.transform.position;
        bow.friendlyClaimer = new GameObject("claimer"); bow.transform.position = new(7, 1, 0);
        HeavyShieldShopShell.ObservePaidBow(bow); Next(); HeavyShieldShopShell.Tick(true);
        Check(bow.transform.position.x == 7 && HeavyShieldIdentity.Paid.Count == 1, "claimed paid tool not stolen/reanchored");
        var ordinary = new GameObject("OrdinaryBow") { tag = "Bow" }.AddComponent<DroppableTool>();
        ordinary.transform.SetParent(Managers.Inst.world.gameLayer, false); ordinary.transform.position = new(9, 2, 0);
        HeavyShieldShopShell.ObservePaidBow(ordinary);
        Check(ordinary.transform.position.x == 9 && !ordinary.Fake && !ordinary.pickedUp, "ordinary Bow untouched by rack observer");

        Setup(true); point = Point(HeavyShieldPurchaseKind.ShieldLeft); point.Start(P1); Fill(P1, CurrencyType.Coins, 6);
        Pool.ReturnNull = true; point.CompleteNative(P1); point.Owner.OnPay.Invoke(P1); Next(); HeavyShieldShopShell.Tick(true);
        Check(HeavyShieldIdentity.Unknown && Pool.SpawnCalls == 1 && HeavyShieldIdentity.BowBinds == 0, "unknown null issuance locks without retry");
        HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave();
        Check(HeavyShieldIdentity.Cancels == 0 && P1.WalletAdds == 0, "unknown issuance gets no native cancellation/refund replay");

        Setup(true); point = Point(HeavyShieldPurchaseKind.ShieldLeft); point.Start(P1); Fill(P1, CurrencyType.Coins, 6);
        HeavyShieldIdentity.BowResult = HeavyShieldCommitResult.Unknown; point.CompleteNative(P1);
        Check(HeavyShieldIdentity.Unknown && Pool.SpawnCalls == 1 && HeavyShieldIdentity.BowBinds == 1, "unknown binding keeps issued receipt");
        Check(Pool.LastIssued.Fake && Pool.LastIssued.pickedUp, "exact new unbound issue not left as free ordinary Bow");
        point.Owner.OnPay.Invoke(P1); Check(Pool.SpawnCalls == 1 && HeavyShieldIdentity.UnknownCalls == 1, "unknown hold once");

        Setup(true); point = Point(HeavyShieldPurchaseKind.ShieldLeft);
        var cachedGo = new GameObject("ToolBow cached active") { tag = "Bow" }; cachedGo.transform.SetParent(Managers.Inst.world.gameLayer, false);
        var cached = cachedGo.AddComponent<DroppableTool>(); cachedGo.AddComponent<Persistent>();
        Managers.Inst.pools.BowPool._activeCache.Add(cachedGo); Pool.ReturnExisting = cached;
        point.Start(P1); Fill(P1, CurrencyType.Coins, 6); point.CompleteNative(P1);
        Check(HeavyShieldIdentity.Unknown && HeavyShieldIdentity.BowBinds == 0, "cached active pool return is not a new paid issue");
        Check(!cached.Fake && !cached.pickedUp && cached.dropper == null, "cached active ordinary Bow fields not borrowed");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 4);
        HeavyShieldIdentity.GemResult = HeavyShieldCommitResult.Rejected; point.CompleteNative(P1);
        Check(HeavyShieldIdentity.LastReason == "commit-rejected-refund-unproven", "definite commit rejection distinguished");
        point.Owner.OnPay.Invoke(P1); HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave();
        Check(P1.WalletAdds == 0 && HeavyShieldIdentity.Cancels == 0 && HeavyShieldIdentity.GemCommits == 1, "unverified refund never invented or repeated");
        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 4);
        HeavyShieldIdentity.GemResult = HeavyShieldCommitResult.Unknown; point.CompleteNative(P1);
        Check(HeavyShieldIdentity.LastReason == "commit-outcome-unknown", "unknown commit diagnostic distinguished");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); point.Start(P1); Fill(P1, CurrencyType.Gems, 4);
        P1._completingPayable = point; P1._payState = Player.PayState.Completed;
        HeavyShieldShopShell.Tick(false);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5 && !point.forceBlockPayment, "retirement leaves Completed receipt available for native callback");
        point.CompleteNative(P1); Next(); HeavyShieldShopShell.Tick(false);
        Check(HeavyShieldIdentity.GemCommits == 1 && NetworkPostbox.Instance.SemiStaticObjects.Count == 0, "retirement after native cleanup only");
        Setup(true); point = Point(HeavyShieldPurchaseKind.ExtraLeft); point.gameObject.SetActive(false);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5 && Point(HeavyShieldPurchaseKind.ShieldRight).Owner.CanPay(P1), "one hidden interaction does not destroy shop");
        Setup(true); Managers.Inst.game.state = Game.State.Menu; HeavyShieldShopShell.Tick(true);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5 && !Point(HeavyShieldPurchaseKind.ShieldLeft).Owner.CanPay(P1), "pause retains five point shop without new fees");

        // The pure production receipt also proves same-frame deferral and callback reentrancy.
        Setup(true);
        var core = new HeavyShieldShopPayment(HeavyShieldPurchaseKind.ShieldLeft);
        Check(core.TryArm(11, 12, 13, true, HeavyShieldIdentity.TryReservePurchase, (l,r)=>{}), "production core arms immutable lease");
        var native = new HeavyShieldShopPayment.NativeReceipt(11, 12, 12, true, 6, CurrencyType.Coins, 6, i => CurrencyType.Coins);
        int commits = 0;
        var outcome = core.Complete(in native, 13, 20, true, lease =>
        {
            commits++; Check(core.Complete(in native, 13, 20, true, _ => throw new Exception("replay"), (l,r)=>{}) == HeavyShieldShopPayment.Outcome.Ignored, "reentrant callback sees latch");
            return HeavyShieldCommitResult.Applied;
        }, (l,r)=>{});
        Check(outcome == HeavyShieldShopPayment.Outcome.Applied && commits == 1, "one reentrant commit");
        Check(!core.ObserveSettled(20, true, true, true), "no same-frame phase switch");
        Check(core.ObserveSettled(21, true, true, true), "following native-cleaned frame can retire receipt");
        Check(!core.TryArm(1, 2, 3, false, HeavyShieldIdentity.TryReservePurchase, (l,r)=>{}), "false preflight no reservation");
    }
    public static int Main()
    {
        try { FixedPricesAndReadonly(); GemPhases(); Cancellation(); InvalidReceipts(); MoreTests(); VisualBorrowing(); BilateralPresentation(); FailedStartedTests(); Console.WriteLine("PASS " + _checks + " production payment/native adapter assertions"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void VisualBorrowing()
    {
        Setup(true); var point = Point(HeavyShieldPurchaseKind.ShieldLeft); point.Start(P1); Fill(P1, CurrencyType.Coins, 6); point.CompleteNative(P1);
        var bow = Pool.LastIssued; var body = bow.GetComponent<SpriteRenderer>(); var nativeSprite = body.sprite;
        var marker = bow.GetComponent<HeavyShieldPaidBowVisual>();
        Check(marker != null && !body.enabled && bow._rigidbody.isKinematic, "paid identity borrows native visibility/physics");
        Check(body.sprite == nativeSprite, "native Bow sprite asset unchanged");
        var placed = bow.transform.position; bow.transform.position = new(6, 2, 0); Next(); HeavyShieldShopShell.Tick(true);
        Check(bow.transform.position.x == 6, "same paid life anchored only once");
        bow.gameObject.SetActive(false);
        Check(body.enabled && !bow._rigidbody.isKinematic, "native pool disable returns borrowed fields");
        Check(HeavyShieldIdentity.Paid.Count == 1, "visual disable never releases paid career");
        bow.gameObject.SetActive(true); HeavyShieldIdentity.Paid.Clear();
        Check(body.enabled && body.sprite == nativeSprite, "ordinary pooled reuse retains native appearance");
        Check(HeavyShieldArt.TryGetPaidShieldSprite(out var shield) && shield.rect.width == 19 && shield.rect.height == 30, "final independent paid shield dimensions");
        Check(shield.texture.width == 19 && shield.texture.height == 30, "paid shield owns independent PNG texture");
        Check(shield.pivot.x == .5f && shield.pivot.y == 0 && shield.pixelsPerUnit == 48f, "paid shield bottom-center pivot and shop PPU");
    }
    private static void BilateralPresentation()
    {
        foreach (bool greek in new[] { false, true })
        foreach (var kind in new[] { HeavyShieldPurchaseKind.ShieldLeft, HeavyShieldPurchaseKind.ShieldRight })
        {
            Setup(true, greek);
            Check(Point(HeavyShieldPurchaseKind.Mold).payablePlacementExclusionDistance == 1.5f, "three-world-unit shop placement width");
            var renderers = (SpriteRenderer[])typeof(HeavyShieldShopShell).GetField("_renderers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(renderers.Length == 4 && renderers.All(r => r.sprite.rect.width == 144 && r.sprite.rect.height == 90 && r.sprite.pixelsPerUnit == 48f), "detail bilateral layers use shop geometry in both worlds");
            var firstMerchant = renderers[2].sprite;
            Time.time += .125f; Time.unscaledTime += .125f; Time.frameCount++;
            HeavyShieldShopShell.Tick(true);
            Check(!ReferenceEquals(firstMerchant, renderers[2].sprite), "smith advances at manifest eight FPS");
            var point = Point(kind); point.Start(P1); Fill(P1, CurrencyType.Coins, 6); point.CompleteNative(P1);
            var bow = Pool.LastIssued;
            var shieldChildren = bow.transform.children.Where(t => t.gameObject.name == "KEM_PaidShield").ToArray();
            Check(shieldChildren.Length == 1 && HeavyShieldIdentity.Paid.Count == 1, "one physical paid shield across both pickup racks");
            bool right = kind == HeavyShieldPurchaseKind.ShieldRight;
            Check(Math.Abs(bow.transform.position.x - (right ? .93f : -.93f)) < .00001f && Math.Abs(bow.transform.position.y - .995f) < .00001f, "existing left/right pickup anchors retained");
            var owned = shieldChildren[0].gameObject.GetComponent<SpriteRenderer>();
            Check(owned.flipX == right && owned.sprite.texture.width == 19, "paid shield direction follows purchased career side");
            Check(!Point(right ? HeavyShieldPurchaseKind.ShieldLeft : HeavyShieldPurchaseKind.ShieldRight).Owner.CanPay(P1), "opposite rack cannot create a second pending paid shield");
        }
    }
    private static HeavyShieldShopPayment PaymentFor(HeavyShieldPurchaseKind kind)
    {
        var points = (Array)typeof(HeavyShieldShopShell).GetField("_points", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var p in points)
        {
            var payment = (HeavyShieldShopPayment)p.GetType().GetField("Payment", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p);
            if (payment.Kind == kind) return payment;
        }
        throw new Exception("payment not found");
    }
    private static void FailAndAddAfterCallback(HeavyShieldPurchaseKind kind, bool throws = false)
    {
        Setup(kind != HeavyShieldPurchaseKind.Mold);
        var point = Point(kind); Check(point.Owner.CanPay(P1), "failure path initially advertised " + kind);
        HeavyShieldIdentity.ReserveRefused = !throws; HeavyShieldIdentity.ReserveThrows = throws;
        point.Start(P1);
        var failed = PaymentFor(kind).FailedStart;
        Check(failed != null && failed.Pending && !PaymentFor(kind).Armed, "failed Started remains owned observation " + kind);
        Check(failed.Payer == P1.Pointer.ToInt64() && failed.Payable == point.Pointer.ToInt64()
            && failed.Kind == kind && failed.Price == point.Price && failed.Currency == point.Currency
            && failed.StartedFrame == Time.frameCount && failed.World == Managers.Inst.world.gameLayer.Pointer.ToInt64(), "frozen failure tuple " + kind);
        Check(P1.CancelCalls == 0 && P1.DropCalls == 0 && P1.CancelInsideStarted == 0 && P1.DropInsideStarted == 0, "Started does not reenter cancellation " + kind);
        Check(HeavyShieldIdentity.Pending.Count == 0 && !point.Owner.CanPay(P1), "no invented A lease or renewed CanPay " + kind);
        // Native Player can append the first currency only AFTER callback returns.
        P1._floatingCurrency.Add(new(point.Currency));
        HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 0 && P1._floatingCurrency.Count == 1, "same Started frame cannot certify empty/drop " + kind);
        Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 1 && P1.DropCalls == 1 && P1._floatingCurrency.Count == 0, "later driver exact cancellation drops post-callback currency " + kind);
        Check(failed.Pending && failed.DropReturned && !failed.Finished, "native Cancelling must settle before local failure cleanup " + kind);
        Check(HeavyShieldShopShell.StatusText.Contains("等待原生取消收尾"), "pending cancellation status " + kind);
        Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 1 && P1.DropCalls == 1, "repeated tick never repeats native request " + kind);
        P1._payState = Player.PayState.None; Next(); HeavyShieldShopShell.Tick(true);
        Check(failed.Finished && !failed.Pending, "normal Drop + native settled finishes only local observation " + kind);
        Check(HeavyShieldIdentity.Cancels == 0 && P1.WalletAdds == 0 && HeavyShieldIdentity.GemCommits == 0 && Pool.SpawnCalls == 0, "failed startup no A release/refund/reissue " + kind);
        Check(!point.Owner.CanPay(P1) && !Point(HeavyShieldPurchaseKind.ShieldLeft).Owner.CanPay(P2)
            && HeavyShieldShopShell.StatusText.Contains("已暂停收费"), "after cleanup module remains explicitly paused " + kind);
        Next(); HeavyShieldShopShell.Tick(true); Check(P1.CancelCalls == 1 && HeavyShieldIdentity.ReserveCalls == 1, "no automatic reservation retry " + kind);
    }
    private static void FailedStartedTests()
    {
        foreach (var kind in Enum.GetValues<HeavyShieldPurchaseKind>()) FailAndAddAfterCallback(kind);
        FailAndAddAfterCallback(HeavyShieldPurchaseKind.Mold, true);

        Setup(); var point = Point(HeavyShieldPurchaseKind.Mold);
        HeavyShieldIdentity.SeenReceiptCount = 4095; Check(point.Owner.CanPay(P1), "capacity race before 4096 cap");
        HeavyShieldIdentity.SeenReceiptCount = 4096;
        for (int i = 0; i < 3; i++) Check(!point.Owner.CanPay(P1), "readonly 4096 preflight rejects new receipt");
        Check(HeavyShieldIdentity.SeenReceiptCount == 4096 && HeavyShieldIdentity.ReserveCalls == 0, "capacity query neither evicts nor reserves");
        point.Start(P1); P1._floatingCurrency.Add(new(CurrencyType.Gems)); Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 1 && P1.DropCalls == 1 && HeavyShieldIdentity.ReserveCalls == 0, "already-started capacity race gets next tick cancellation");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true;
        point.Start(P1); P1._floatingCurrency.Add(new(CurrencyType.Gems));
        P1.selectedPayable = Point(HeavyShieldPurchaseKind.ExtraLeft); Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 0 && P1.DropCalls == 0 && P1._floatingCurrency.Count == 1, "foreign selected point never cancelled/dropped");
        Check(PaymentFor(HeavyShieldPurchaseKind.Mold).FailedStart.Unknown && HeavyShieldShopShell.StatusText.Contains("待确认"), "foreign transaction is Unknown");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        P1._floatingCurrency.Add(new(CurrencyType.Gems));
        var points = (Array)typeof(HeavyShieldShopShell).GetField("_points", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        points.GetValue(0).GetType().GetField("Payer", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(points.GetValue(0), P2);
        P2.selectedPayable = point; P2._payState = Player.PayState.Transaction; P2._floatingCurrency.Add(new(CurrencyType.Gems));
        Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 0 && P2.CancelCalls == 0 && P2._floatingCurrency.Count == 1, "replacement payer cannot inherit failed cancellation ticket");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        P1._floatingCurrency.Add(new(CurrencyType.Gems));
        var life = typeof(HeavyShieldShopShell).GetField("_shopLife", BindingFlags.NonPublic | BindingFlags.Static);
        life.SetValue(null, (long)life.GetValue(null) + 1); Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 0 && PaymentFor(HeavyShieldPurchaseKind.Mold).FailedStart.Unknown, "new shop life cannot borrow old failed ticket");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        P1._floatingCurrency.Add(new(CurrencyType.Gems)); Managers.Inst.world.gameLayer = new GameObject("OtherWorld").transform;
        Next(); HeavyShieldShopShell.Tick(true);
        Check(P1.CancelCalls == 0 && P1.DropCalls == 0 && NetworkPostbox.Instance.SemiStaticObjects.Count == 5, "world change holds old failure without foreign cancel/destroy");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        Fill(P1, CurrencyType.Gems, 4); P1._completingPayable = point; P1._payState = Player.PayState.Completed;
        Next(); HeavyShieldShopShell.Tick(true); point.Owner.OnPay.Invoke(P1);
        Check(PaymentFor(HeavyShieldPurchaseKind.Mold).FailedStart.Unknown && P1.CancelCalls == 0 && P1.DropCalls == 0
            && P1.WalletAdds == 0 && HeavyShieldIdentity.GemCommits == 0, "failed but Completed never cancelled/refunded/committed");
        point.CompleteNative(P1); Next(); HeavyShieldShopShell.Tick(true);
        Check(!point.Owner.CanPay(P1) && HeavyShieldIdentity.ReserveCalls == 1 && HeavyShieldIdentity.UnknownCalls == 0, "unarmed Completed consumption never creates a fake A receipt");

        Setup(true); point = Point(HeavyShieldPurchaseKind.ShieldRight); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        Fill(P1, CurrencyType.Coins, 1); P1.DelayDrop = true; Next(); HeavyShieldShopShell.Tick(true);
        var failure = PaymentFor(HeavyShieldPurchaseKind.ShieldRight).FailedStart;
        Next(); HeavyShieldShopShell.Tick(true);
        Check(failure.Pending && failure.DropReturned && P1.CancelCalls == 1 && P1.DropCalls == 1, "Drop return without native list Clear keeps failure pending");
        P1._floatingCurrency.Clear(); Next(); HeavyShieldShopShell.Tick(true);
        Check(failure.Pending, "empty list with Cancelling state cannot finish failure");
        P1._payState = Player.PayState.None; Next(); HeavyShieldShopShell.Tick(true);
        Check(failure.Finished && P1.CancelCalls == 1 && !point.Owner.CanPay(P1), "late native Clear and settled closes observation without fee recovery");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        Fill(P1, CurrencyType.Gems, 1); P1.ThrowAfterDrop = true; Next(); HeavyShieldShopShell.Tick(true);
        failure = PaymentFor(HeavyShieldPurchaseKind.Mold).FailedStart; P1._payState = Player.PayState.None;
        Next(); HeavyShieldShopShell.Tick(true);
        Check(failure.Unknown && failure.Pending && !failure.DropReturned && P1.CancelCalls == 1 && P1.DropCalls == 1,
            "Drop throw remains unknown even with empty list; no repeat");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveMalformed = true; point.Start(P1);
        Fill(P1, CurrencyType.Gems, 1); Next(); HeavyShieldShopShell.Tick(true); P1._payState = Player.PayState.None;
        Next(); HeavyShieldShopShell.Tick(true);
        Check(PaymentFor(HeavyShieldPurchaseKind.Mold).FailedStart.Finished && HeavyShieldIdentity.Pending.Count == 1
            && HeavyShieldIdentity.Unknown && HeavyShieldIdentity.Cancels == 0, "contract-mismatch cancellation never releases A partial/unknown reservation");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveThrowsAfterReservation = true; point.Start(P1);
        Fill(P1, CurrencyType.Gems, 1); Next(); HeavyShieldShopShell.Tick(true); P1._payState = Player.PayState.None;
        Next(); HeavyShieldShopShell.Tick(true);
        Check(HeavyShieldIdentity.Pending.Count == 1 && HeavyShieldIdentity.Unknown && HeavyShieldIdentity.Cancels == 0,
            "A partial reservation is preserved by its own failclosed state");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        Fill(P1, CurrencyType.Gems, 1); Managers.Inst.game.state = Game.State.Menu; Next(); HeavyShieldShopShell.Tick(true);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5 && P1.CancelCalls == 1 && HeavyShieldShopShell.StatusText.Contains("等待原生取消收尾"), "menu retains failed point until native cleanup settles");
        P1._payState = Player.PayState.None; Next(); HeavyShieldShopShell.Tick(true);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5 && !point.Owner.CanPay(P1), "menu settled shop stays closed to fees");

        Setup(); point = Point(HeavyShieldPurchaseKind.Mold); HeavyShieldIdentity.ReserveRefused = true; point.Start(P1);
        Fill(P1, CurrencyType.Gems, 1); HeavyShieldShopShell.CancelPendingTransactionsBeforeNativeSave();
        Check(P1.CancelCalls == 0, "save helper cannot shortcut failed-start delayed driver cancellation");
        Next(); HeavyShieldShopShell.Tick(false);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 5 && P1.CancelCalls == 1, "feature off retains failed point while native Cancelling");
        P1._payState = Player.PayState.None; Next(); HeavyShieldShopShell.Tick(false);
        Check(NetworkPostbox.Instance.SemiStaticObjects.Count == 0 && HeavyShieldIdentity.Cancels == 0, "feature off retires only after exact failed-start cleanup");
        Next(); HeavyShieldShopShell.Tick(true);
        Check(!Point(HeavyShieldPurchaseKind.Mold).Owner.CanPay(P1), "shop recreate does not clear module failure latch");
    }
}
