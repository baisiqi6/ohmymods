using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Five fixed-price native payment points. Each Started callback owns one immutable receipt.</summary>
internal static class HeavyShieldShopShell
{
    private sealed class Point
    {
        internal readonly HeavyShieldShopPayment Payment;
        internal GameObject Object;
        internal PayableComponent Payable;
        internal HeavyShieldShopShellOwner Owner;
        internal CRPCHeader Header;
        internal Il2CppSystem.Action<Player> Started;
        internal Player Payer;
        internal bool Disabled;
        internal Point(HeavyShieldPurchaseKind kind) { Payment = new(kind); }
    }
    private static readonly HeavyShieldPurchaseKind[] Kinds = { HeavyShieldPurchaseKind.Mold,
        HeavyShieldPurchaseKind.ExtraLeft, HeavyShieldPurchaseKind.ExtraRight,
        HeavyShieldPurchaseKind.ShieldLeft, HeavyShieldPurchaseKind.ShieldRight };
    private static GameObject _object;
    private static Point[] _points;
    private static Kingdom _kingdom;
    private static Transform _layer;
    private static NetworkPostbox _postbox;
    private static SpriteRenderer[] _renderers;
    private static HeavyShieldAtlasId _shopAtlas = HeavyShieldAtlasId.ShopLayers;
    private static long _shopLife;
    private static int _merchantFrame = -1, _fixturesFrame = -1;
    private static bool _registered, _clearing, _retiring, _menuSuspended, _failureLogged, _economyFault;
    private static float _retryAt, _cleanupRetryAt, _nextBowCheck;
    private static string _retryReason = "error", _stage = "idle", _status = "等待领地就绪";
    private static DroppableTool _bowPrefab, _paidBow;
    private static Pool _bowPool;
    private static PoolManager _pools;
    private static int _bowPrefabId;
    internal static string StatusText
    {
        get
        {
            if (_points != null) foreach (var point in _points)
                if (point?.Payment.FailedStart is { Pending: true } failed)
                    return failed.Unknown ? "盾具启动结果待确认，已暂停收费" : "盾具启动失败，等待原生取消收尾";
            return _economyFault ? "盾具付款异常，已暂停收费；需诊断后重新加载" : _status;
        }
    }

    internal static void Tick(bool enabled)
    {
        try
        {
            AdvanceFailedStarts();
            ObserveNativeSettlement();
            if (_retiring) { if (Time.unscaledTime >= _cleanupRetryAt) Clear(_retryReason); return; }
            var probe = Observe(enabled, out var kingdom, out var layer);
            switch (HeroShopRetention.Decide(in probe))
            {
                case HeroShopRetention.Outcome.ClearFeatureDisabled:
                    if (probe.HasShop) Clear("feature-disabled"); _status = "宝石盾卫已关闭"; return;
                case HeroShopRetention.Outcome.ClearNetworkLost:
                    if (probe.HasShop) Clear("network-lost"); _status = "等待可用的单机领地"; return;
                case HeroShopRetention.Outcome.ClearWorldChanged: Clear("world-change"); return;
                case HeroShopRetention.Outcome.ClearHeaderMismatch: Clear("header-mismatch"); return;
                case HeroShopRetention.Outcome.ClearContextLost: Clear("context-lost"); return;
                case HeroShopRetention.Outcome.ClearNonPlayable: Clear("left-playing-or-menu"); return;
                case HeroShopRetention.Outcome.Active:
                    _menuSuspended = false;
                    if (IslandSaveData.isSavingGame) CancelPendingTransactionsBeforeNativeSave();
                    MaintainBowPrefab(); UpdatePaymentGates(); UpdateVisuals(); ReconcilePaidBow();
                    _status = HeavyShieldIdentity.StatusText;
                    if (!HeavyShieldRuntime.CarrierPreflightReady) _status = "等待盾卫原生载体就绪";
                    else if (!BowUsable()) _status = "等待原生 ToolBow 池就绪";
                    return;
                case HeroShopRetention.Outcome.Keep:
                    if (!_menuSuspended) { CancelPendingTransactionsBeforeNativeSave(); _menuSuspended = true; }
                    UpdatePaymentGates(); _status = "已暂停，盾具店保留"; return;
                case HeroShopRetention.Outcome.Create:
                    if (Time.unscaledTime < _retryAt || IslandSaveData.isSavingGame) return;
                    _retryAt = Time.unscaledTime + 5f; Create(kingdom, layer); return;
                default: _status = "等待可用的单机领地"; return;
            }
        }
        catch (Exception e)
        {
            if (!_failureLogged) { Log("unavailable stage=" + _stage + ": " + e); _failureLogged = true; }
            Clear("error"); _retryAt = Time.unscaledTime + 10f;
        }
    }
    private static HeroShopRetention.Probe Observe(bool enabled, out Kingdom kingdom, out Transform layer)
    {
        bool hasShop = _object != null || _kingdom != null || _points != null;
        bool context = TryContext(out kingdom, out layer);
        bool sameWorld = context && hasShop && _object != null && _kingdom != null && _layer != null
            && kingdom.Pointer == _kingdom.Pointer && layer.Pointer == _layer.Pointer && _shopAtlas == CurrentShopAtlas();
        var state = context ? Managers.Inst.game.state : default;
        return new HeroShopRetention.Probe
        {
            FeatureEnabled = enabled, Offline = !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth,
            HasShop = hasShop, SameScene = context, Playing = context && state == Game.State.Playing,
            Menu = context && state == Game.State.Menu, SameWorld = sameWorld,
            HeaderOk = context && sameWorld && HeaderMatches(),
            SceneAlive = _object != null && _object.activeInHierarchy && _layer != null && _layer.gameObject.activeInHierarchy
        };
    }
    private static bool TryContext(out Kingdom kingdom, out Transform layer)
    {
        kingdom = null; layer = null;
        if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) return false;
        var managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.world == null || managers.payables == null
            || managers.kingdom == null || !managers.kingdom.HasBorderLoaded || managers.world.gameLayer == null) return false;
        kingdom = managers.kingdom; layer = managers.world.gameLayer; return true;
    }
    private static void RegisterTypes()
    {
        if (_registered) return;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(HeavyShieldShopShellOwner)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(HeavyShieldShopShellOwner), new RegisterTypeOptions
            { Interfaces = new[] { typeof(IPayableComponentOwner) } });
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(HeavyShieldPaidBowVisual)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(HeavyShieldPaidBowVisual));
        _registered = true;
    }
    private static void Create(Kingdom kingdom, Transform layer)
    {
        _stage = "register-owner";
        if (NetworkPostbox.Instance == null) return;
        RegisterTypes();
        _stage = "resolve-shop-layers";
        var atlas = CurrentShopAtlas();
        if (!HeavyShieldArtLayout.Validate(atlas, out _)
            || !TrySequenceFrame(atlas, "rear", 0, out int rearFrame)
            || !TrySequenceFrame(atlas, "fixtures", 0, out int fixturesFrame)
            || !TrySequenceFrame(atlas, "merchant", 0, out int merchantFrame)
            || !TrySequenceFrame(atlas, "front", 0, out int frontFrame)
            || !TryLayerSprite(atlas, rearFrame, out var rear) || !TryLayerSprite(atlas, fixturesFrame, out var fixtures)
            || !TryLayerSprite(atlas, merchantFrame, out var merchant) || !TryLayerSprite(atlas, frontFrame, out var front))
        { _status = "盾具店素材尚未就绪"; return; }
        if (!HeavyShieldArt.TryGetPaidShieldSprite(out _)) { _status = "盾具素材尚未就绪"; return; }
        var payables = Managers.Inst.payables;
        float halfWidth = Math.Max(1.5f, HeavyShieldArtLayout.ShopCellWidth / HeavyShieldArtLayout.ShopPixelsPerUnit * .5f);
        _stage = "find-position";
        // Native adapter supplies real payable and blocker intervals to the latest exclusion-provider contract.
        if (!HeroShopPlacementNative.Find(payables, kingdom.GetBorderSideIntact(Side.Left),
            kingdom.GetBorderSideIntact(Side.Right), halfWidth, out float position))
        { _status = "领地中段暂时没有盾具店空位"; return; }
        _stage = "ground-reference";
        if (!TryNativeGroundAnchor(layer, out float groundY, out var nativeRenderer))
        { _status = "等待原生商店地面参照"; return; }
        _kingdom = kingdom; _layer = layer; _shopAtlas = atlas; _postbox = NetworkPostbox.Instance;
        _shopLife = checked(_shopLife + 1);
        _object = new GameObject("KEM_HeavyShieldShop"); _object.SetActive(false);
        _object.transform.SetParent(layer, false);
        _object.transform.position = new Vector3(position, groundY, nativeRenderer.transform.position.z);
        _renderers = new SpriteRenderer[4];
        Sprite[] sprites = { rear, fixtures, merchant, front };
        string[] names = { "rear", "fixtures", "merchant", "front" };
        for (int i = 0; i < sprites.Length; i++)
        {
            var child = new GameObject("shield_shop_" + names[i]); child.transform.SetParent(_object.transform, false);
            var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = sprites[i];
            renderer.sortingOrder = nativeRenderer.sortingOrder + i - 2; renderer.sortingLayerID = nativeRenderer.sortingLayerID;
            if (nativeRenderer.sharedMaterial != null) renderer.sharedMaterial = nativeRenderer.sharedMaterial;
            _renderers[i] = renderer;
        }
        _merchantFrame = 0; _fixturesFrame = 0;
        _points = new Point[Kinds.Length];
        for (int i = 0; i < _points.Length; i++) CreatePoint(i, halfWidth);
        if (!HeaderMatches()) throw new InvalidOperationException("native registration failed");
        _stage = "activate"; _object.SetActive(true);
        MaintainBowPrefab(); UpdatePaymentGates(); UpdateVisuals();
        _status = HeavyShieldIdentity.StatusText; _stage = "ready";
        Log("native shop ready points=5 x=" + position.ToString("F2") + " y=" + groundY.ToString("F4"));
    }
    private static float PointX(HeavyShieldPurchaseKind kind) => kind switch
    {
        HeavyShieldPurchaseKind.ExtraLeft => -1.3f, HeavyShieldPurchaseKind.ExtraRight => 1.3f,
        HeavyShieldPurchaseKind.ShieldLeft => -.65f, HeavyShieldPurchaseKind.ShieldRight => .65f, _ => 0f
    };
    private static void CreatePoint(int index, float halfWidth)
    {
        var point = _points[index] = new Point(Kinds[index]);
        point.Object = new GameObject("shield_pay_" + point.Payment.Kind);
        point.Object.transform.SetParent(_object.transform, false);
        point.Object.transform.localPosition = new Vector3(PointX(point.Payment.Kind), 0, 0);
        point.Object.AddComponent<CRPCStamp>(); point.Owner = point.Object.AddComponent<HeavyShieldShopShellOwner>();
        point.Owner.Initialize(index, _shopLife); point.Payable = point.Object.AddComponent<PayableComponent>();
        point.Payable.forceBlockPayment = true; point.Payable.Init(point.Owner.Cast<IPayableComponentOwner>());
        point.Payable.Price = point.Payment.Price; point.Payable.Currency = point.Payment.Currency; point.Payable.priceIncrease = 0;
        bool expansion = point.Payment.Kind == HeavyShieldPurchaseKind.ExtraLeft || point.Payment.Kind == HeavyShieldPurchaseKind.ExtraRight;
        point.Payable.indicatorOffset = new Vector2(0, expansion ? 1.3f : .65f);
        point.Payable.repositionInSplitScreen = false; point.Payable.playerPayPointOffset = Vector2.zero;
        point.Payable.playerPayDistance = .3f; point.Payable.payablePlacementExclusionOffset = Vector2.zero;
        point.Payable.payablePlacementExclusionDistance = index == 0 ? halfWidth : 0f;
        point.Payable.glowOnSelect = false; point.Payable.needsNewNetId = false;
        int capturedIndex = index; long capturedLife = _shopLife;
        point.Started = (Il2CppSystem.Action<Player>)(player => OnTransactionStarted(capturedIndex, capturedLife, player));
        point.Payable.add_OnTransactionStartedCallback(point.Started);
        var ownerInterface = point.Owner.Cast<IPayableComponentOwner>();
        if (ownerInterface.OnPay == null || ownerInterface.CanPay(null)) throw new InvalidOperationException("owner interface preflight failed");
        LockIndicator.LockReason reason = LockIndicator.LockReason.Invalid;
        if (ownerInterface.IsLocked(null, out reason) || reason != LockIndicator.LockReason.NotLocked)
            throw new InvalidOperationException("owner lock-reason preflight failed");
        point.Header = _postbox.RegisterObject(point.Object, CRPCType.SemiStatic);
        if (!PointHeaderMatches(point)) throw new InvalidOperationException("point registration failed");
        // Approved shield pixels: one inner purchase icon, two outer expansion icons, mirrored per side.
        if (index > 0 && HeavyShieldArt.TryGetPaidShieldSprite(out var shield))
            for (int icon = 0; icon < (expansion ? 2 : 1); icon++)
            {
                var child = new GameObject(expansion ? "extra_seat_shield" : "purchase_shield");
                child.transform.SetParent(point.Object.transform, false);
                child.transform.localPosition = new Vector3(expansion ? (icon == 0 ? -.09f : .09f) : 0, expansion ? .95f : .4f, -.001f);
                child.transform.localScale = expansion ? new Vector3(.22f, .22f, 1) : new Vector3(.35f, .35f, 1);
                var renderer = child.AddComponent<SpriteRenderer>(); renderer.sprite = shield;
                renderer.flipX = point.Payment.Side == HeavyShieldQuota.Side.Right;
                renderer.sortingLayerID = _renderers[3].sortingLayerID; renderer.sortingOrder = _renderers[3].sortingOrder + 1;
                renderer.sharedMaterial = _renderers[3].sharedMaterial;
            }
    }
    private static bool TryPoint(int index, long life, out Point point)
    {
        point = _points != null && index >= 0 && index < _points.Length ? _points[index] : null;
        return point != null && life == _shopLife && point.Object != null && point.Payable != null;
    }
    private static bool AnyBusy()
    {
        if (_points != null) foreach (var point in _points) if (point != null && point.Payment.Busy) return true;
        return false;
    }
    private static bool CanStart(Point point, Player player)
    {
        if (player == null || !player.hasLocalAuthority || point.Disabled || _economyFault || _retiring || _menuSuspended
            || _object == null || !_object.activeInHierarchy || IslandSaveData.isSavingGame
            || Managers.Inst?.game == null || Managers.Inst.game.state != Game.State.Playing
            || !HeavyShieldRuntime.CarrierPreflightReady || !HeaderMatches() || !BowUsable() || AnyBusy()) return false;
        if (!TryContext(out var kingdom, out var layer) || _kingdom == null || _layer == null
            || kingdom.Pointer != _kingdom.Pointer || layer.Pointer != _layer.Pointer) return false;
        return HeavyShieldIdentity.CanReservePurchase(point.Payment.Kind);
    }
    internal static bool CanPay(int index, long life, Player player)
    {
        try
        {
            if (player == null || !TryPoint(index, life, out var point)) return false;
            var payment = point.Payment; var lease = payment.Lease;
            if (payment.Busy) return payment.MayContinue(player.Pointer.ToInt64(),
                point.Payable.Pointer.ToInt64(), life, point.Payable.Price, point.Payable.Currency,
                HeavyShieldIdentity.ValidatePurchase(in lease));
            return CanStart(point, player);
        }
        catch { return false; }
    }
    private static void OnTransactionStarted(int index, long life, Player player)
    {
        Point point = null;
        bool ownStarted = false;
        try
        {
            if (!TryPoint(index, life, out point) || player == null || point.Payment.Busy
                || point.Payment.FailedStart != null || player.selectedPayable == null
                || player.selectedPayable.Pointer != point.Payable.Pointer || player._payState != Player.PayState.Transaction) return;
            ownStarted = true;
            point.Payer = player;
            if (!CanStart(point, player) || player._floatingCurrency == null || player._floatingCurrency.Count != 0
                || point.Payable.Price != point.Payment.Price || point.Payable.Currency != point.Payment.Currency)
            {
                FailStarted(point, player, life, "Started-preflight-refused"); return;
            }
            if (!point.Payment.TryArm(player.Pointer.ToInt64(), point.Payable.Pointer.ToInt64(), life, true,
                HeavyShieldIdentity.TryReservePurchase, HoldUnknown))
            {
                FailStarted(point, player, life, point.Payment.Unknown ? "reservation-contract-mismatch" : "reservation-refused"); return;
            }
            UpdatePaymentGates();
        }
        catch (Exception e)
        {
            if (ownStarted && point != null) FailStarted(point, player, life, "reservation-threw");
            else _economyFault = true;
            Log("Started outcome unknown: " + e.GetType().Name);
        }
    }
    private static void FailStarted(Point point, Player player, long life, string reason)
    {
        _economyFault = true;
        point.Payer = player;
        point.Payment.ObserveFailedStart(player.Pointer.ToInt64(), point.Payable.Pointer.ToInt64(), life,
            _layer != null ? _layer.Pointer.ToInt64() : 0, _kingdom != null ? _kingdom.Pointer.ToInt64() : 0,
            point.Payment.Price, point.Payment.Currency, Time.frameCount, reason);
        // Close new fees immediately, but never synchronously reenter Player.CancelTransaction from Started.
        UpdatePaymentGates();
        Log("Started rejected; native cancellation deferred kind=" + point.Payment.Kind + " reason=" + reason);
    }
    private static void AdvanceFailedStarts()
    {
        if (_points == null) return;
        foreach (var point in _points)
        {
            var failed = point?.Payment.FailedStart;
            if (failed == null || !failed.Pending || failed.Unknown || Time.frameCount <= failed.StartedFrame) continue;
            try
            {
                var payer = point.Payer;
                var managers = Managers.Inst;
                if (payer == null || payer.Pointer.ToInt64() != failed.Payer || !payer.hasLocalAuthority
                    || _shopLife != failed.ShopLife || point.Payable == null || point.Payable.Pointer.ToInt64() != failed.Payable
                    || _layer == null || _layer.Pointer.ToInt64() != failed.World || _kingdom == null || _kingdom.Pointer.ToInt64() != failed.Kingdom
                    || managers?.world?.gameLayer == null || managers.world.gameLayer.Pointer.ToInt64() != failed.World
                    || managers.kingdom == null || managers.kingdom.Pointer.ToInt64() != failed.Kingdom
                    || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth)
                { failed.HoldUnknown("failed-start-owner-context-changed"); continue; }
                if (payer._payState == Player.PayState.Completed)
                { failed.HoldUnknown("failed-start-already-completed"); continue; }
                if (!failed.CancelRequested)
                {
                    if (payer._payState != Player.PayState.Transaction || payer.selectedPayable == null
                        || payer.selectedPayable.Pointer.ToInt64() != failed.Payable || payer._completingPayable != null)
                    { failed.HoldUnknown("failed-start-transaction-changed"); continue; }
                    if (!failed.TryRequestCancel(Time.frameCount)) continue;
                    payer.CancelTransaction();
                    payer.DropFloatingCurrency(); // must return normally and actually Clear before the observation can finish
                    failed.ObserveDropReturn();
                    if (payer._floatingCurrency != null && payer._floatingCurrency.Count == 0
                        && payer.selectedPayable != null && payer.selectedPayable.Pointer.ToInt64() == failed.Payable)
                        payer.DeselectPayable();
                }
                bool empty = payer._floatingCurrency != null && payer._floatingCurrency.Count == 0;
                bool settled = payer._payState != Player.PayState.Transaction && payer._payState != Player.PayState.Completed
                    && payer._payState != Player.PayState.Cancelling;
                if (failed.ObserveCleanup(Time.frameCount, empty, payer._completingPayable == null, settled)) point.Payer = null;
            }
            catch { failed.HoldUnknown("failed-start-cancellation-outcome-unknown"); }
        }
    }
    internal static void OnPay(int index, long life, Player player)
    {
        if (!TryPoint(index, life, out var point) || player == null) return;
        var payment = point.Payment;
        try
        {
            if (payment.FailedStart is { Pending: true } failed)
            {
                if (player.Pointer.ToInt64() == failed.Payer && player._completingPayable != null
                    && player._completingPayable.Pointer.ToInt64() == failed.Payable && player._payState == Player.PayState.Completed)
                    failed.HoldUnknown("failed-start-already-completed");
                return;
            }
            var native = new HeavyShieldShopPayment.NativeReceipt(player.Pointer.ToInt64(), point.Payable.Pointer.ToInt64(),
                player._completingPayable != null ? player._completingPayable.Pointer.ToInt64() : 0,
                player._payState == Player.PayState.Completed, point.Payable.Price, point.Payable.Currency,
                player._floatingCurrency != null ? player._floatingCurrency.Count : -1, i => player._floatingCurrency[i].CurrencyType);
            var lease = payment.Lease;
            var outcome = payment.Complete(in native, life, Time.frameCount, HeavyShieldIdentity.ValidatePurchase(in lease), Commit, HoldUnknown);
            if (outcome != HeavyShieldShopPayment.Outcome.Ignored)
                Log("native Completed kind=" + payment.Kind + " result=" + outcome + " receipt=" + lease.Receipt);
            // No Price/Currency/forceBlockPayment changes or destruction on the original callback stack.
        }
        catch (Exception e)
        {
            if (payment.Busy) payment.LatchUnknown("native-receipt-read-unknown", HoldUnknown); else _economyFault = true;
            Log("OnPay outcome unknown: " + e.GetType().Name);
        }
    }
    private static HeavyShieldCommitResult Commit(HeavyShieldPurchaseLease lease)
    {
        if (HeavyShieldShopPayment.IsGem(lease.Kind)) return HeavyShieldIdentity.CompleteGemPayment(in lease);
        if (!BowUsable() || _object == null || _layer == null) return HeavyShieldCommitResult.Rejected;
        var before = _bowPool._activeCache;
        if (before == null || before.Count > 4096) return HeavyShieldCommitResult.Rejected;
        var activeBefore = new HashSet<(IntPtr, int)>();
        for (int i = 0; i < before.Count; i++)
            if (before[i] != null) activeBefore.Add((before[i].Pointer, before[i].GetInstanceID()));
        DroppableTool tool = null; bool bound = false, issued = false;
        try
        {
            // Once native Spawn is invoked, null/throw is unknown. This receipt never invokes it twice.
            tool = Pool.Spawn<DroppableTool>(_bowPrefab, RackPosition(lease.Side), Quaternion.identity, _layer, true);
            if (tool == null || tool.gameObject == null || tool.gameObject.tag != "Bow" || tool.transform == null
                || !tool.gameObject.activeInHierarchy || !tool.transform.IsChildOf(_layer)
                || activeBefore.Contains((tool.gameObject.Pointer, tool.gameObject.GetInstanceID()))) return HeavyShieldCommitResult.Unknown;
            var origin = Pool.GetPoolByInstance(tool.gameObject);
            if (origin == null || origin.Pointer != _bowPool.Pointer) return HeavyShieldCommitResult.Unknown;
            issued = true;
            tool.dropper = null; tool.parentShopRef = null; tool.selfDestruct = false;
            tool.pickUpPolicy = PickUpPolicy.Anybody; tool.pickedUp = false; tool.SetFake(false);
            var persistent = tool.GetComponent<Persistent>();
            if (persistent == null || !persistent.ShouldPersist() || string.IsNullOrEmpty(persistent.path)) return HeavyShieldCommitResult.Unknown;
            if (!HeavyShieldIdentity.ValidatePurchase(in lease)) return HeavyShieldCommitResult.Unknown;
            var result = HeavyShieldIdentity.BindIssuedPaidBow(in lease, tool);
            bound = (result == HeavyShieldCommitResult.Applied || result == HeavyShieldCommitResult.AlreadyApplied)
                && HeavyShieldIdentity.TryGetPaidBow(tool, out var paid) && paid.Receipt == lease.Receipt
                && paid.Side == lease.Side && paid.Land == lease.Land && paid.World == lease.World
                && HeavyShieldIdentity.ValidateCareer(in paid);
            if (bound) try { ObservePaidBow(tool); } catch { Log("paid Bow visual deferred"); }
            return bound ? result : HeavyShieldCommitResult.Unknown;
        }
        catch { return HeavyShieldCommitResult.Unknown; }
        finally
        {
            // Only this call's exact issued object: no free Bow from failed binding, no guessed reclaim/reissue.
            if (issued && !bound && tool != null) try { tool.SetFake(true); tool.pickedUp = true; } catch { }
        }
    }
    private static void HoldUnknown(HeavyShieldPurchaseLease lease, string reason)
    {
        _economyFault = true; HeavyShieldIdentity.HoldUnknown(in lease, reason);
    }
    private static void UpdatePaymentGates()
    {
        if (_points == null) return;
        for (int i = 0; i < _points.Length; i++)
        {
            var point = _points[i]; if (point?.Payable == null) continue;
            // Started stays usable for native completion even while the shop waits to retire.
            point.Payable.forceBlockPayment = point.Payment.Busy ? !CanPay(i, _shopLife, point.Payer)
                : (_economyFault || _retiring || _menuSuspended || IslandSaveData.isSavingGame
                    || !HeavyShieldRuntime.CarrierPreflightReady || !BowUsable()
                    || !HeavyShieldIdentity.CanReservePurchase(point.Payment.Kind) || AnyBusy() || point.Disabled);
        }
    }
    internal static void CancelPendingTransactionsBeforeNativeSave() => CancelPendingTransactions();
    internal static void CancelPendingTransactions()
    {
        if (_points == null) return;
        foreach (var point in _points)
        {
            if (point == null) continue;
            Cancel(point, point.Payer);
            if (_kingdom != null) { Cancel(point, _kingdom.playerOne); Cancel(point, _kingdom.playerTwo); }
        }
    }
    private static void Cancel(Point point, Player player)
    {
        if (point.Payment.FailedStart != null) return; // failed Started is cancelled only by the later driver tick
        if (player == null || !player.hasLocalAuthority || point.Payable == null) return;
        try
        {
            bool selected = player.selectedPayable != null && player.selectedPayable.Pointer == point.Payable.Pointer;
            bool completing = player._completingPayable != null && player._completingPayable.Pointer == point.Payable.Pointer;
            if (!selected && !completing) return;
            if (player._payState == Player.PayState.Completed || point.Payment.Latched || point.Payment.Unknown) return;
            bool owned = point.Payment.RequestCancellation(player.Pointer.ToInt64(), point.Payable.Pointer.ToInt64(), _shopLife, false);
            if (point.Payment.Busy && !owned) return;
            player.CancelTransaction(); player.DropFloatingCurrency(); // native loop + Clear; no wallet additions
            bool empty = player._floatingCurrency != null && player._floatingCurrency.Count == 0;
            if (owned) point.Payment.ObserveUnpaidCancellation(true, empty, ConfirmUnpaidCancellation, HoldUnknown);
            if (empty && selected) player.DeselectPayable();
        }
        catch
        {
            if (point.Payment.Busy) point.Payment.LatchUnknown("native-cancellation-outcome-unknown", HoldUnknown); else _economyFault = true;
        }
    }
    private static bool ConfirmUnpaidCancellation(HeavyShieldPurchaseLease lease)
        => HeavyShieldIdentity.ConfirmUnpaidCancellation(in lease);
    private static void ObserveNativeSettlement()
    {
        if (_points == null) return;
        foreach (var point in _points)
        {
            if (point == null || point.Payment.FailedStart != null || !point.Payment.Busy || point.Payment.Unknown || point.Payer == null) continue;
            try
            {
                var payer = point.Payer;
                bool empty = payer._floatingCurrency != null && payer._floatingCurrency.Count == 0;
                bool cleared = payer._completingPayable == null;
                bool settled = payer._payState != Player.PayState.Completed && payer._payState != Player.PayState.Cancelling
                    && payer._payState != Player.PayState.Transaction;
                // Native release-button cancellation also goes through DropFloatingCurrency before None.
                if (point.Payment.Armed && !point.Payment.CancellationRequested && empty && cleared && settled)
                {
                    var lease = point.Payment.Lease;
                    if (point.Disabled || point.Payable == null || point.Payable.forceBlockPayment
                        || point.Payable.Price != point.Payment.Price
                        || point.Payable.Currency != point.Payment.Currency || !PointHeaderMatches(point)
                        || !HeavyShieldIdentity.ValidatePurchase(in lease))
                    {
                        point.Payment.LatchUnknown("unobserved-native-settlement-unknown", HoldUnknown);
                        continue;
                    }
                    point.Payment.RequestCancellation(payer.Pointer.ToInt64(), point.Payable.Pointer.ToInt64(), _shopLife, false);
                }
                if (point.Payment.CancellationRequested && !point.Payment.CancellationConfirmed && empty && settled)
                    point.Payment.ObserveUnpaidCancellation(true, true, ConfirmUnpaidCancellation, HoldUnknown);
                if (point.Payment.ObserveSettled(Time.frameCount, empty, cleared, settled)) point.Payer = null;
            }
            catch { point.Payment.LatchUnknown("native-settlement-observation-unknown", HoldUnknown); }
        }
    }
    private static bool HasUnsettledNativeTransaction()
    {
        if (_points == null) return false;
        foreach (var point in _points)
        {
            if (point == null || !point.Payment.Busy) continue;
            if (point.Payment.FailedStart != null)
            {
                if (point.Payment.FailedStart.Pending) return true;
                continue; // finished failure has exact native cleanup proof; never release an A receipt here
            }
            if (point.Payer == null) return true;
            try
            {
                if (point.Payer._floatingCurrency == null || point.Payer._floatingCurrency.Count != 0
                    || (point.Payer._completingPayable != null && point.Payable != null
                        && point.Payer._completingPayable.Pointer == point.Payable.Pointer)
                    || point.Payer._payState == Player.PayState.Completed) return true;
            }
            catch { return true; }
        }
        return false;
    }
    internal static void OnOwnerDisabled(HeavyShieldShopShellOwner owner, int index, long life)
    {
        if (_clearing || !TryPoint(index, life, out var point) || owner == null || point.Owner == null
            || owner.Pointer != point.Owner.Pointer) return;
        // Hiding one interaction point never destroys the whole shop.
        point.Disabled = true; Cancel(point, point.Payer);
    }
    internal static void Clear(string reason)
    {
        if (_clearing) return;
        _clearing = true; _retiring = true; _retryReason = reason;
        try
        {
            CancelPendingTransactions();
            if (HasUnsettledNativeTransaction())
            {
                UpdatePaymentGates(); _cleanupRetryAt = Time.unscaledTime + .1f; _status = "等待原生付款清理完成"; return;
            }
            if (_points != null) foreach (var point in _points)
            {
                if (point == null) continue;
                if (point.Payment.Armed) point.Payment.LatchUnknown("shop-retired-without-cancellation-proof", HoldUnknown);
                if (point.Payable != null)
                {
                    point.Payable.forceBlockPayment = true;
                    if (point.Started != null) point.Payable.remove_OnTransactionStartedCallback(point.Started);
                }
                if (_postbox != null && point.Header != null && point.Object != null
                    && _postbox.MasterSemiCRPCHLookup.TryGetValue(point.Object, out var found)
                    && found != null && found.Pointer == point.Header.Pointer)
                    _postbox.DeregisterObject(point.Object, point.Header.NetID, CRPCType.SemiStatic);
            }
            if (_object != null) { _object.SetActive(false); UnityEngine.Object.Destroy(_object); }
            _object = null; _points = null; _kingdom = null; _layer = null; _postbox = null; _renderers = null;
            _bowPrefab = null; _bowPool = null; _pools = null; _bowPrefabId = 0; _nextBowCheck = 0;
            _merchantFrame = -1; _fixturesFrame = -1; _shopAtlas = HeavyShieldAtlasId.ShopLayers;
            _retiring = false; _menuSuspended = false; _cleanupRetryAt = 0;
            _status = "盾具店已清理";
        }
        catch (Exception e) { _cleanupRetryAt = Time.unscaledTime + 1f; Log("cleanup deferred reason=" + reason + ": " + e.GetType().Name); }
        finally { _clearing = false; }
    }
    private static HeavyShieldAtlasId CurrentShopAtlas()
    {
        var biome = BiomeHolder.Inst;
        return biome != null && biome.BiomeIndex == BiomeHolder.GreeceBiomeIndex ? HeavyShieldAtlasId.GreekShopLayers : HeavyShieldAtlasId.ShopLayers;
    }
    private static bool TrySequenceFrame(HeavyShieldAtlasId atlas, string sequence, int offset, out int frame)
        => HeavyShieldArtLayout.TryGetSequenceFrame(atlas, sequence, offset, out frame);
    private static bool TryLayerSprite(HeavyShieldAtlasId atlas, int frame, out Sprite sprite)
        => atlas == HeavyShieldAtlasId.GreekShopLayers ? HeavyShieldArt.TryGetGreekShopLayerSprite(frame, out sprite) : HeavyShieldArt.TryGetShopLayerSprite(frame, out sprite);
    private static bool TryNativeGroundAnchor(Transform layer, out float groundY, out SpriteRenderer reference)
    {
        groundY = 0; reference = null;
        var all = Managers.Inst.payables.AllPayables;
        for (int i = 0; all != null && i < Math.Min(all.Length, 4096); i++)
        {
            var shop = all[i]; if (shop == null || shop.TryCast<PayableShop>() == null) continue;
            var tag = shop.GetComponent<ShopTag>();
            if (tag == null || (tag.type != PayableShop.ShopType.Bow && tag.type != PayableShop.ShopType.Hammer && tag.type != PayableShop.ShopType.Scythe)) continue;
            var body = shop.GetComponent<SpriteRenderer>();
            if (body == null || body.sprite == null || !body.enabled || Math.Abs(body.sprite.pivot.y) > .001f) continue;
            var root = shop.transform;
            if (root != null && HeroShopGrounding.TryResolve(layer != null && root.IsChildOf(layer), shop.isActiveAndEnabled, root.position.y, out groundY))
            { reference = body; return true; }
        }
        return false;
    }
    private static bool HeaderMatches()
    {
        if (_points == null || _points.Length != Kinds.Length) return false;
        foreach (var point in _points) if (!PointHeaderMatches(point)) return false;
        return true;
    }
    private static bool PointHeaderMatches(Point point)
    {
        if (_postbox == null || NetworkPostbox.Instance == null || _postbox.Pointer != NetworkPostbox.Instance.Pointer
            || point == null || point.Header == null || point.Object == null || point.Payable == null
            || point.Payable.parentHeaderRef == null || point.Payable.parentHeaderRef.Pointer != point.Header.Pointer
            || point.Header.referencedGO == null || point.Header.referencedGO.Pointer != point.Object.Pointer
            || point.Header.NetID == 0 || point.Header.RemoteMethodList == null || point.Header.RemoteMethodList.Count < 3) return false;
        return _postbox.MasterSemiCRPCHLookup.TryGetValue(point.Object, out var found) && found != null && found.Pointer == point.Header.Pointer
            && _postbox.SemiStaticObjects.TryGetValue(point.Header.NetID, out var indexed) && indexed != null && indexed.Pointer == point.Header.Pointer;
    }
    private static void UpdateVisuals()
    {
        if (_renderers == null || _renderers.Length != 4) return;
        if (HeavyShieldArtLayout.TryGetSequence(_shopAtlas, "merchant", out var sequence) && sequence.Count > 0)
        {
            float fps = float.IsFinite(sequence.Fps) && sequence.Fps > 0f ? sequence.Fps : 8f;
            int offset = (int)(Time.time * fps) % sequence.Count;
            if (offset != _merchantFrame && TrySequenceFrame(_shopAtlas, "merchant", offset, out int frame) && TryLayerSprite(_shopAtlas, frame, out var sprite))
            { _renderers[2].sprite = sprite; _merchantFrame = offset; }
        }
        var view = HeavyShieldIdentity.GetQuotaView();
        int fixture = !view.MoldUnlocked ? 0 : view.Unknown || view.ShopOccupied
            || (view.LeftOccupied >= view.LeftLimit && view.RightOccupied >= view.RightLimit) ? 5 : 4;
        if (!view.MoldUnlocked && _points != null && _points[0]?.Payer != null && !_points[0].Payment.Latched)
            fixture = Math.Clamp(_points[0].Payer._floatingCurrency?.Count ?? 0, 0, 3);
        if (fixture != _fixturesFrame && TrySequenceFrame(_shopAtlas, "fixtures", fixture, out int fixtures) && TryLayerSprite(_shopAtlas, fixtures, out var image))
        { _renderers[1].sprite = image; _fixturesFrame = fixture; }
    }
    private static void MaintainBowPrefab()
    {
        if (Time.unscaledTime < _nextBowCheck) return;
        _nextBowCheck = Time.unscaledTime + .5f;
        if (BowUsable()) return;
        _bowPrefab = null; _bowPool = null; _pools = null; _bowPrefabId = 0;
        var pools = Managers.Inst?.pools; var pool = pools?.GetPoolByPrefabName("ToolBow");
        var prefab = pool?.prefab; var tool = prefab?.GetComponent<DroppableTool>();
        if (tool == null || prefab.name != "ToolBow" || prefab.tag != "Bow" || _layer == null || prefab.transform.IsChildOf(_layer)) return;
        var persistent = tool.GetComponent<Persistent>();
        if (persistent == null || !persistent.persistObject || string.IsNullOrEmpty(persistent.path)) return;
        _bowPrefab = tool; _bowPool = pool; _pools = pools; _bowPrefabId = prefab.GetInstanceID();
        if (!BowUsable()) { _bowPrefab = null; _bowPool = null; _pools = null; _bowPrefabId = 0; }
    }
    private static bool BowUsable()
    {
        if (_bowPrefab == null || _bowPool == null || _bowPool._activeCache == null || _bowPool._activeCache.Count > 4096
            || _pools == null || _layer == null || Managers.Inst?.pools == null
            || Managers.Inst.pools.Pointer != _pools.Pointer || _bowPrefab.gameObject == null || _bowPrefab.gameObject.GetInstanceID() != _bowPrefabId
            || _bowPrefab.gameObject.name != "ToolBow" || _bowPrefab.gameObject.tag != "Bow" || _bowPrefab.transform.IsChildOf(_layer)) return false;
        var pool = _pools.GetPoolByPrefabName("ToolBow"); var persistent = _bowPrefab.GetComponent<Persistent>();
        return pool != null && pool.Pointer == _bowPool.Pointer && pool.prefab != null && pool.prefab.Pointer == _bowPrefab.gameObject.Pointer
            && persistent != null && persistent.persistObject && !string.IsNullOrEmpty(persistent.path);
    }
    private static Vector3 RackPosition(HeavyShieldQuota.Side side)
        => _object.transform.position + new Vector3(side == HeavyShieldQuota.Side.Left ? -.93f : .93f, .12f, -.001f);
    // A calls only after exact successful PaidBow bind, including native load restoration. No world tool search.
    internal static void ObservePaidBow(DroppableTool bow)
    {
        if (bow == null || !HeavyShieldIdentity.TryGetPaidBow(bow, out var handle) || !HeavyShieldIdentity.ValidateCareer(in handle)) return;
        _paidBow = bow; ReconcilePaidBow();
    }
    private static void ReconcilePaidBow()
    {
        if (_paidBow == null || _object == null || _layer == null || IslandSaveData.isSavingGame
            || Managers.Inst?.game == null || Managers.Inst.game.state != Game.State.Playing) return;
        if (!HeavyShieldIdentity.TryGetPaidBow(_paidBow, out var handle) || !HeavyShieldIdentity.ValidateCareer(in handle)) { _paidBow = null; return; }
        if (_paidBow.gameObject == null || _paidBow.transform == null || !_paidBow.transform.IsChildOf(_layer)
            || _paidBow.pickedUp || _paidBow.friendlyClaimer != null || _paidBow.enemyClaimer != null) return;
        RegisterTypes();
        var marker = _paidBow.GetComponent<HeavyShieldPaidBowVisual>();
        if (marker == null) marker = _paidBow.gameObject.AddComponent<HeavyShieldPaidBowVisual>();
        marker.Place(_paidBow, in handle, RackPosition(handle.Side));
    }
    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeavyShieldShop] " + message); } catch { }
    }
}

internal sealed class HeavyShieldShopShellOwner : MonoBehaviour
{
    private Il2CppSystem.Action<Player> _onPay;
    private int _index;
    private long _life;
    public HeavyShieldShopShellOwner(IntPtr pointer) : base(pointer) { }
    [HideFromIl2Cpp] internal void Initialize(int index, long life)
    {
        _index = index; _life = life;
        _onPay = (Il2CppSystem.Action<Player>)(player => HeavyShieldShopShell.OnPay(_index, _life, player));
    }
    public Il2CppSystem.Action<Player> OnPay => _onPay;
    public bool CanPay(Player player) => HeavyShieldShopShell.CanPay(_index, _life, player);
    // ABI output-pointer bridge: out LockReason makes invalid ClassInjector ldobj on actual 2.4.
    public bool IsLocked(Player player, IntPtr reason) => HeroShopOwnerInterop.WriteUnlocked(reason, (int)LockIndicator.LockReason.NotLocked);
    public void OnDisable() => HeavyShieldShopShell.OnOwnerDisabled(this, _index, _life);
}
