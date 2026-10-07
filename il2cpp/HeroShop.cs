using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if !HERO_SHOP_CORE_ONLY
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

internal static class HeroShopOwnerInterop
{
    internal static bool WriteUnlocked(IntPtr reason, int notLocked)
    {
        if (reason == IntPtr.Zero) return true;
        Marshal.WriteInt32(reason, notLocked);
        return false;
    }
}

/// <summary>Bounded placement and one receipt per native transaction; no Unity dependencies.</summary>
internal sealed class HeroShopPayment
{
    private sealed class Ticket
    {
        internal long Payer, Source, Owner;
        internal bool Settled, Cancelled, Settling;
    }
    private readonly List<Ticket> _tickets = new(4);
    private Ticket Find(long payer, long source, long owner)
        => _tickets.Find(x => x.Payer == payer && x.Source == source && x.Owner == owner);

    internal bool Arm(long payer, long source, long owner)
    {
        if (payer == 0 || source == 0 || owner == 0) return false;
        var prior = Find(payer, source, owner);
        if (prior != null)
        {
            if (prior.Settling) return false;
            _tickets.Remove(prior);
        }
        if (_tickets.Count >= 4)
        {
            int old = _tickets.FindIndex(x => x.Settled || x.Cancelled);
            if (old < 0) return false;
            _tickets.RemoveAt(old);
        }
        _tickets.Add(new Ticket { Payer = payer, Source = source, Owner = owner });
        return true;
    }
    internal bool IsArmed(long payer, long source, long owner)
    {
        var ticket = Find(payer, source, owner);
        return ticket != null && !ticket.Settled && !ticket.Cancelled;
    }
    internal bool AlreadySettled(long payer, long source, long owner)
        => Find(payer, source, owner)?.Settled == true;
    internal bool IsSettling(long payer, long source, long owner)
        => Find(payer, source, owner)?.Settling == true;
    internal bool Consume(long payer, long source, long owner, bool completed, int coins)
    {
        var ticket = Find(payer, source, owner);
        if (ticket == null || ticket.Settled || ticket.Cancelled || !completed || coins != 8) return false;
        ticket.Settled = true; ticket.Settling = true;
        return true;
    }
    internal void Finish(long payer, long source, long owner)
    {
        var ticket = Find(payer, source, owner);
        if (ticket != null) ticket.Settling = false;
    }
    internal void Cancel(long payer, long source, long owner)
    {
        var ticket = Find(payer, source, owner);
        if (ticket != null && !ticket.Settling) ticket.Cancelled = true;
    }
    // Existing pure core contract; production always supplies payable and owner identity.
    internal void Arm(long payer) { Clear(); if (payer != 0) _tickets.Add(new Ticket { Payer = payer }); }
    internal bool IsArmed(long payer) => IsArmed(payer, 0, 0);
    internal bool AlreadySettled(long payer) => AlreadySettled(payer, 0, 0);
    internal bool Consume(long payer, bool completed, int coins)
    {
        bool result = Consume(payer, 0, 0, completed, coins);
        if (result) Finish(payer, 0, 0);
        return result;
    }
    internal void Clear() => _tickets.Clear();
}

internal static class HeroShopPlacement
{
    internal readonly struct Interval
    {
        internal readonly float Low, High;
        internal Interval(float low, float high) { Low = low; High = high; }
    }

    // Exclusions only split the search into cells. They never reject a cell: native fits
    // remains authoritative, including for native types whose exclusions it ignores.
    internal static bool Find(float left, float right, float halfWidth, Func<float, bool> fits,
        Func<IReadOnlyList<Interval>> collectIntervals, out float x)
    {
        x = 0;
        if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(halfWidth)
            || halfWidth <= 0 || fits == null || collectIntervals == null) return false;
        double low = (double)left + halfWidth, high = (double)right - halfWidth;
        if (low > high) return false;
        double middle = ((double)left + right) * 0.5;
        bool Inside(float value) => float.IsFinite(value)
            && value - halfWidth >= left && value + halfWidth <= right;
        float center = (float)middle;
        if (Inside(center) && fits(center)) { x = center; return true; }

        var intervals = collectIntervals();
        if (intervals == null) return false;
        var thresholds = new List<double> { low, high };
        foreach (var interval in intervals)
        {
            if (!float.IsFinite(interval.Low) || !float.IsFinite(interval.High)
                || interval.Low > interval.High) return false;
            double a = (double)interval.Low - halfWidth, b = (double)interval.High + halfWidth;
            if (a > low && a < high) thresholds.Add(a);
            if (b > low && b < high) thresholds.Add(b);
        }
        thresholds.Sort();
        var candidates = new HashSet<float>();
        void Add(float value)
        {
            if (value != center && Inside(value)) candidates.Add(value);
        }
        Add((float)low);
        Add((float)high);
        for (int i = 1; i < thresholds.Count; i++)
        {
            double a = thresholds[i - 1], b = thresholds[i];
            if (b <= a) continue;
            void AddInterior(float value)
            {
                if (value > a && value < b) Add(value);
            }
            AddInterior((float)(a + (b - a) * 0.5));
            // No fixed epsilon: even a gap narrower than a sampling step may have a
            // usable float. These also cover a midpoint rounded onto a cell boundary.
            float nearLeft = (float)a, nearRight = (float)b;
            if (nearLeft <= a) nearLeft = MathF.BitIncrement(nearLeft);
            if (nearRight >= b) nearRight = MathF.BitDecrement(nearRight);
            AddInterior(nearLeft);
            AddInterior(nearRight);
        }
        var ordered = new List<float>(candidates);
        ordered.Sort((a, b) =>
        {
            int distance = Math.Abs((double)a - middle).CompareTo(Math.Abs((double)b - middle));
            return distance != 0 ? distance : a.CompareTo(b);
        });
        foreach (float candidate in ordered)
        {
            if (!fits(candidate)) continue;
            x = candidate;
            return true;
        }
        return false;
    }
}

#if !HERO_SHOP_CORE_ONLY
internal static class HeroShopPlacementNative
{
    internal static bool Find(PayableManager payables, float left, float right, float halfWidth, out float x)
        => HeroShopPlacement.Find(left, right, halfWidth,
            p => !payables.OverlapsAnyExclusions(p, halfWidth, false), () => Collect(payables), out x);

    private static IReadOnlyList<HeroShopPlacement.Interval> Collect(PayableManager payables)
    {
        var all = payables.AllPayables;
        var blockers = payables._allBlockers;
        if (all == null || blockers == null)
            throw new InvalidOperationException("Shop placement exclusions are not ready.");
        var intervals = new List<HeroShopPlacement.Interval>();
        void Add(float point, float distance)
        {
            // Match the native float endpoint arithmetic before expanding in double.
            float low = point - distance, high = point + distance;
            if (!float.IsFinite(point) || !float.IsFinite(distance) || distance < 0
                || !float.IsFinite(low) || !float.IsFinite(high))
                throw new InvalidOperationException("Invalid shop placement exclusion.");
            intervals.Add(new HeroShopPlacement.Interval(low, high));
        }
        for (int i = 0; i < all.Length; i++)
        {
            var payable = all[i];
            if (payable == null) continue; // AllPayables contains unused slots.
            float distance = payable.payablePlacementExclusionDistance;
            if (!float.IsFinite(distance))
                throw new InvalidOperationException("Invalid payable placement distance.");
            if (distance > 0) Add(payable.PayableExclusionPoint(), distance);
        }
        for (int i = 0; i < blockers.Count; i++)
        {
            var blocker = blockers[i];
            if (blocker == null) continue;
            Add(blocker.GetExclusionPoint(), blocker.exclusionDistance);
        }
        return intervals;
    }
}
#endif

/// <summary>Ground baseline policy for the MOD shop; no Unity dependencies.</summary>
internal static class HeroShopGrounding
{
    // Actual 2.4 resources.assets/sharedassets0: every ground shop prefab root carries its
    // body SpriteRenderer (pivot.y = 0, PPU 32) at local y 0.875/0.88, so the prefab root y IS
    // the ground line; merchant children sit at their own offsets and are never a baseline.
    // The MOD art is 80 px at 32 PPU with pivot (0.5, 0.025). Its lowest opaque edge sits
    // one pixel below that pivot. Preserve this existing grass-edge overlap, not the old
    // GameLayer-origin placement that buried roughly 28 pixels of the building.
    internal const float BodyGroundOffset = 0f;

    /// <summary>Only a same-world, active native shop root is a vertical reference; a missing,
    /// foreign or unknown one defers creation instead of guessing the GameLayer y.</summary>
    internal static bool TryResolve(bool sameWorld, bool active, float nativeRootY, out float groundY)
    {
        groundY = 0f;
        if (!sameWorld || !active || !float.IsFinite(nativeRootY)) return false;
        float resolved = nativeRootY + BodyGroundOffset;
        if (!float.IsFinite(resolved)) return false;
        groundY = resolved;
        return true;
    }
}

/// <summary>
/// Pure retention policy: a native Menu pause keeps the same-world MOD shop (body, banners and
/// registration untouched) while payment stays blocked; only <c>Game.State.Playing</c> may create
/// a shop or serve a payment. A shop is cleared the moment the feature flag, the single-player
/// world authority, the world itself or the native registration is really gone.
/// No Unity dependencies.
/// </summary>
internal static class HeroShopRetention
{
    internal enum Outcome
    {
        ClearFeatureDisabled,
        ClearNetworkLost,
        ClearWorldChanged,
        ClearHeaderMismatch,
        ClearContextLost,
        ClearNonPlayable,
        Keep,
        Active,
        Create,
        Wait,
    }

    /// <summary>One frame of observations, mapped 1:1 to booleans so the whole table is testable.</summary>
    internal struct Probe
    {
        internal bool FeatureEnabled; // mod enabled AND the hero-archer feature enabled
        internal bool Offline;        // no online session and this peer holds the world authority
        internal bool HasShop;        // MOD shop object or its world binding still resident
        internal bool SameScene;      // current managers expose a kingdom and its world GameLayer
        internal bool Playing;        // managers.game.state == Game.State.Playing
        internal bool Menu;           // only the native pause menu, not Intro/Loss/Quitting
        internal bool SameWorld;      // resident shop belongs to exactly that kingdom + GameLayer
        internal bool HeaderOk;       // native SemiStatic registration still matches
        internal bool SceneAlive;     // resident shop object and its layer are still alive
    }

    internal static Outcome Decide(in Probe p)
    {
        if (!p.FeatureEnabled) return Outcome.ClearFeatureDisabled;
        if (!p.Offline) return Outcome.ClearNetworkLost;
        if (!p.HasShop) return p.SameScene && p.Playing ? Outcome.Create : Outcome.Wait;
        if (!p.SameScene || !p.SceneAlive) return Outcome.ClearContextLost;
        if (!p.SameWorld) return Outcome.ClearWorldChanged;
        if (!p.Playing && !p.Menu) return Outcome.ClearNonPlayable;
        if (!p.HeaderOk) return Outcome.ClearHeaderMismatch;
        return p.Playing ? Outcome.Active : Outcome.Keep;
    }

    /// <summary>Payment is served only in the Active outcome; every other state blocks it.</summary>
    internal static bool CanServe(in Probe p) => Decide(in p) == Outcome.Active;
}

#if !HERO_SHOP_CORE_ONLY
/// <summary>
/// A MOD-owned, offline-only payment point. It never enters ShopPlanner or Persistent.
/// Native PayableComponent owns selection, coin animation, cancellation and spending.
/// The owner interface supplies eligibility and the single purchase callback.
/// </summary>
internal static class HeroShop
{
    internal const int Price = 8;
    private static GameObject _object;
    private static PayableComponent _payable;
    private static HeroShopOwner _owner;
    private static Kingdom _kingdom;
    private static Transform _layer;
    private static NetworkPostbox _postbox;
    private static CRPCHeader _header;
    private static Sprite _sprite;
    private static ShopV2Visuals _v2;
    private static int _leftV2State, _rightV2State;
    private static float _nextV2StateAt;
    private static SpriteRenderer _renderer;
    private static Il2CppSystem.Action<Player> _started;
    private static readonly HeroShopPayment Payment = new();
    private static float _retryAt;
    private static bool _menuSuspended;
    private static string _retryReason = "error";
    private static bool _clearing;
    private static bool _retiring;
    private static bool _registered;
    private static bool _loggedFailure;
    private static bool _cleanupFailureLogged;
    private static float _cleanupRetryAt;
    private static string _status = "等待领地就绪";
    private static string _createStage = "idle";
    private static int _visualSelection = -1, _visualSnapshots;
    private static bool _visualDiagnosticFailed;
    internal static string StatusText => _status;

    internal static void Tick()
    {
        try
        {
            if (_retiring)
            {
                if (Time.unscaledTime >= _cleanupRetryAt) Clear(_retryReason);
                return;
            }
            var probe = Observe(out var kingdom, out var layer);
            switch (HeroShopRetention.Decide(in probe))
            {
                // Real losses still clean immediately: the feature flag, the single-player world
                // authority, the world or the native registration is genuinely gone.
                case HeroShopRetention.Outcome.ClearFeatureDisabled:
                    if (probe.HasShop) Clear("feature-disabled");
                    _status = "已关闭，已购名额保留";
                    return;
                case HeroShopRetention.Outcome.ClearNetworkLost:
                    if (probe.HasShop) Clear("network-lost");
                    _status = "等待可用的单机领地";
                    return;
                case HeroShopRetention.Outcome.ClearWorldChanged:
                    Clear("world-change");
                    return;
                case HeroShopRetention.Outcome.ClearHeaderMismatch:
                    Clear("header-mismatch");
                    return;
                case HeroShopRetention.Outcome.ClearContextLost:
                    Clear("context-lost");
                    return;
                case HeroShopRetention.Outcome.ClearNonPlayable:
                    Clear("left-playing-or-menu");
                    return;
                // Playing: native maintenance only; payment stays governed by CanPurchase.
                case HeroShopRetention.Outcome.Active:
                {
                    ObserveVisualSelection();
                    if (_menuSuspended) { _menuSuspended = false; Log("resume: retained same shop"); }
                    UpdateV2Visuals();
                    _payable.forceBlockPayment = !CanPurchase();
                    _status = HeroRecruitment.StatusText;
                    return;
                }
                // Native Menu pause: the same shop, body, banners and
                // header are preserved; new payment is blocked and an open one is cancelled once.
                case HeroShopRetention.Outcome.Keep:
                {
                    if (_payable != null) _payable.forceBlockPayment = true;
                    if (!_menuSuspended)
                    {
                        SuspendPendingTransaction();
                        _menuSuspended = true; // only after cancellation succeeded
                        Log("pause: retain shop, payment blocked");
                    }
                    _status = "已暂停，英雄商店保留";
                    return;
                }
                case HeroShopRetention.Outcome.Create:
                    if (Time.unscaledTime < _retryAt || IslandSaveData.isSavingGame) return;
                    _retryAt = Time.unscaledTime + 5f;
                    Create(kingdom, layer);
                    return;
                default:
                    _status = "等待可用的单机领地";
                    return;
            }
        }
        catch (Exception e)
        {
            if (!_loggedFailure) { Log("unavailable stage=" + _createStage + ": " + e); _loggedFailure = true; }
            Clear("error");
            _retryAt = Time.unscaledTime + 10f;
        }
    }

    /// <summary>Reads what this frame observed. Also the single source of the payment gate:
    /// a MOD shop may serve a payment only in the Active outcome.</summary>
    private static HeroShopRetention.Probe Observe(out Kingdom kingdom, out Transform layer)
    {
        bool hasShop = _object != null || _kingdom != null;
        bool contextRead = TryContext(out kingdom, out layer);
        bool sameWorld = contextRead && hasShop && _object != null && _kingdom != null && _layer != null
            && kingdom.Pointer == _kingdom.Pointer && layer.Pointer == _layer.Pointer;
        bool playing = contextRead && Managers.Inst.game.state == Game.State.Playing;
        bool menu = contextRead && Managers.Inst.game.state == Game.State.Menu;
        bool sceneAlive = _object != null && _object.activeInHierarchy && _layer != null && _layer.gameObject.activeInHierarchy;
        return new HeroShopRetention.Probe
        {
            FeatureEnabled = ModConfig.Enabled.Value && ModConfig.HeroArcherEnabled.Value,
            Offline = !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth,
            HasShop = hasShop,
            SameScene = contextRead,
            Playing = playing,
            Menu = menu,
            SameWorld = sameWorld,
            HeaderOk = contextRead && sameWorld && HeaderMatches(),
            SceneAlive = sceneAlive,
        };
    }

    /// <summary>Same-scene world context: the current single-player kingdom and its GameLayer.
    /// A native Menu pause keeps both readable, so retention must not depend on Game.State.</summary>
    private static bool TryContext(out Kingdom kingdom, out Transform layer)
    {
        kingdom = null; layer = null;
        if (!ModConfig.Enabled.Value || !ModConfig.HeroArcherEnabled.Value
            || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) return false;
        var managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.world == null || managers.payables == null
            || managers.kingdom == null || !managers.kingdom.HasBorderLoaded || managers.world.gameLayer == null) return false;
        kingdom = managers.kingdom; layer = managers.world.gameLayer;
        return true;
    }

    internal static bool CanPurchase()
    {
        try
        {
            // Payment is served only in the policy's Active outcome: same world, playing, valid
            // header. A Menu pause retains the shop but never re-enables payment.
            if (_clearing || _retiring || _object == null || _payable == null || _owner == null
                || IslandSaveData.isSavingGame || !HeroRecruitment.CanPurchase) return false;
            return HeroShopRetention.CanServe(Observe(out _, out _));
        }
        catch { return false; }
    }

    internal static bool CanPlayerPurchase(Player player)
    {
        try
        {
            // PayableComponent first applies the native base CanPay restrictions (crown,
            // mount, transformed/petrified/gliding and global payment blockers).
            if (!CanPurchase() || player == null || !player.hasLocalAuthority || player.wallet == null
                || !player.gameObject.activeInHierarchy) return false;
            if ((_kingdom.playerOne == null || _kingdom.playerOne.Pointer != player.Pointer)
                && (_kingdom.playerTwo == null || _kingdom.playerTwo.Pointer != player.Pointer)) return false;
            if (player._payState == Player.PayState.Completed)
                return player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer
                    && player._floatingCurrency != null && player._floatingCurrency.Count == Price
                    && Payment.IsArmed(player.Pointer.ToInt64(), _payable.Pointer.ToInt64(), _owner.Pointer.ToInt64());
            // Native UpdatePayState sets Transaction then calls Select before invoking
            // TransactionStarted. Do not require the receipt during that initial Select;
            // it is mandatory at Completed, before TransactionComplete can spend coins.
            return true;
        }
        catch { return false; }
    }

    private static void Create(Kingdom kingdom, Transform layer)
    {
        _createStage = "register-owner";
        if (NetworkPostbox.Instance == null) return;
        if (!_registered)
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(HeroShopOwner)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(HeroShopOwner), new RegisterTypeOptions
                { Interfaces = new[] { typeof(IPayableComponentOwner) } });
            _registered = true;
        }
        _createStage = "load-art";
        if (!LoadArt()) { _status = "英雄商店素材尚未就绪"; return; }
        var payables = Managers.Inst.payables;
        float halfWidth = Math.Max(1f, _sprite.bounds.size.x * 0.5f);
        _createStage = "find-position";
        if (!HeroShopPlacementNative.Find(payables, kingdom.GetBorderSideIntact(Side.Left), kingdom.GetBorderSideIntact(Side.Right),
            halfWidth, out float position))
        { _status = "完整城墙内暂时没有合适的商店空位"; return; }

        _createStage = "ground-reference";
        if (!TryNativeGroundAnchor(layer, out float rootY, out float groundY))
        {
            _status = "等待原生商店地面参照";
            return; // no same-world native shop yet: never fall back to a guessed height
        }
        _kingdom = kingdom; _layer = layer;
        _createStage = "create-renderer";
        _object = new GameObject("KEM_HeroShop");
        _object.SetActive(false);
        _object.transform.SetParent(layer, false);
        _object.transform.position = new Vector3(position, groundY, layer.position.z);
        var renderer = _object.AddComponent<SpriteRenderer>();
        _renderer = renderer;
        renderer.sprite = _sprite;
        // Match a currently active native shop's render layer/material without modifying it.
        var all = payables.AllPayables;
        for (int i = 0; all != null && i < Math.Min(all.Length, 4096); i++)
        {
            var shop = all[i];
            if (shop == null || !shop.isActiveAndEnabled || shop.TryCast<PayableShop>() == null) continue;
            var native = shop.GetComponentInChildren<SpriteRenderer>();
            if (native == null) continue;
            renderer.sortingLayerID = native.sortingLayerID;
            renderer.sortingOrder = native.sortingOrder;
            if (native.sharedMaterial != null) renderer.sharedMaterial = native.sharedMaterial;
            _object.transform.position = new Vector3(_object.transform.position.x,
                _object.transform.position.y, native.transform.position.z);
            break;
        }
        _v2 = ShopV2Visuals.Bind(_object, _renderer, ShopV2Kind.Hero);
        if (_v2 == null) throw new InvalidOperationException("V2 shop layers unavailable");
        _createStage = "create-owner";
        _object.AddComponent<CRPCStamp>();
        _owner = _object.AddComponent<HeroShopOwner>();
        _owner.Initialize();
        _createStage = "create-payable";
        _payable = _object.AddComponent<PayableComponent>();
        _payable.forceBlockPayment = true;
        _payable.Init(_owner.Cast<IPayableComponentOwner>());
        _owner.BindPayable(_payable);
        _payable.Price = Price;
        _payable.Currency = CurrencyType.Coins;
        _payable.priceIncrease = 0;
        _payable.indicatorSpacing = 0.4f;
        _payable.indicatorOffset = Vector2.zero;
        _payable.repositionInSplitScreen = false;
        _payable.playerPayPointOffset = Vector2.zero;
        _payable.playerPayDistance = 1f;
        _payable.payablePlacementExclusionOffset = Vector2.zero;
        _payable.payablePlacementExclusionDistance = halfWidth;
        _payable.glowOnSelect = false;
        _payable.needsNewNetId = false;
        _started = (Il2CppSystem.Action<Player>)OnTransactionStarted;
        _payable.add_OnTransactionStartedCallback(_started);
        // The component's real interface dispatch is checked before making it selectable.
        var ownerInterface = _owner.Cast<IPayableComponentOwner>();
        _createStage = "owner-callback-preflight";
        if (ownerInterface.OnPay == null) throw new InvalidOperationException("owner interface callback missing");
        _createStage = "owner-canpay-preflight";
        if (ownerInterface.CanPay(null))
            throw new InvalidOperationException("owner interface eligibility preflight failed");
        _createStage = "owner-lock-preflight";
        LockIndicator.LockReason lockReason = LockIndicator.LockReason.Invalid;
        if (ownerInterface.IsLocked(null, out lockReason) || lockReason != LockIndicator.LockReason.NotLocked)
            throw new InvalidOperationException("owner interface lock-reason roundtrip failed");
        Log("owner preflight passed: callback=true canPay(null)=false locked=false reason=" + lockReason);
        _createStage = "register-header";
        _postbox = NetworkPostbox.Instance;
        _header = _postbox.RegisterObject(_object, CRPCType.SemiStatic);
        if (!HeaderMatches()) throw new InvalidOperationException("native payment header registration failed");
        _createStage = "activate";
        _object.SetActive(true); // native Awake/OnEnable initialize and register exactly once
        UpdateV2Visuals();
        _payable.forceBlockPayment = !CanPurchase();
        _status = "英雄商店：8 金币";
        _createStage = "ready";
        Log("ground source=native-root rootY=" + rootY.ToString("F4")
            + " bodyOffset=" + HeroShopGrounding.BodyGroundOffset.ToString("F4")
            + " groundY=" + groundY.ToString("F4") + " finalY=" + groundY.ToString("F4"));
        Log("ready x=" + position.ToString("F2") + " native-payment=registered price=8");
    }

    private static bool TryNativeGroundAnchor(Transform layer, out float rootY, out float groundY)
    {
        rootY = 0f; groundY = 0f;
        var all = Managers.Inst.payables.AllPayables;
        for (int i = 0; all != null && i < Math.Min(all.Length, 4096); i++)
        {
            var shop = all[i];
            if (shop == null || shop.TryCast<PayableShop>() == null) continue;
            var tag = shop.GetComponent<ShopTag>();
            // Use only verified standard equipment-shop roots. Change-ruler/item shops
            // also derive PayableShop but are not evidence for a ground-contact anchor.
            if (tag == null || (tag.type != PayableShop.ShopType.Bow
                && tag.type != PayableShop.ShopType.Hammer && tag.type != PayableShop.ShopType.Scythe)) continue;
            var body = shop.GetComponent<SpriteRenderer>();
            if (body == null || body.sprite == null || !body.enabled
                || Math.Abs(body.sprite.pivot.y) > 0.001f) continue;
            var t = shop.transform;
            if (t == null) continue;
            // Native ShopPlanner.CreateShop instantiates every ground shop inside the current
            // level's GameLayer at the prefab root y; only such an active root is a reference
            // (2.4: the body renderer is ON that root). Another world's layer, an inactive or
            // pooled stub, a banner/tool child is not a baseline. IsChildOf is the established
            // current-world test for native shops (AutoRestockCounts, SiegeAmmoCounts).
            float y = t.position.y;
            bool sameWorld = layer != null && t.IsChildOf(layer);
            if (!HeroShopGrounding.TryResolve(sameWorld, shop.isActiveAndEnabled, y, out groundY)) continue;
            rootY = y;
            return true;
        }
        return false;
    }

    private static bool HeaderMatches()
    {
        if (_postbox == null || NetworkPostbox.Instance == null || _postbox.Pointer != NetworkPostbox.Instance.Pointer
            || _header == null || _object == null || _payable == null
            || _payable.parentHeaderRef == null || _payable.parentHeaderRef.Pointer != _header.Pointer
            || _header.referencedGO == null || _header.referencedGO.Pointer != _object.Pointer
            || _header.NetID == 0 || _header.RemoteMethodList == null || _header.RemoteMethodList.Count < 3) return false;
        return _postbox.MasterSemiCRPCHLookup.TryGetValue(_object, out var found)
            && found != null && found.Pointer == _header.Pointer
            && _postbox.SemiStaticObjects.TryGetValue(_header.NetID, out var indexed)
            && indexed != null && indexed.Pointer == _header.Pointer;
    }

    private static void OnTransactionStarted(Player player)
    {
        if (player == null || !CanPurchase() || _payable == null
            || player.selectedPayable == null || player.selectedPayable.Pointer != _payable.Pointer) return;
        Payment.Arm(player.Pointer.ToInt64(), _payable.Pointer.ToInt64(), _owner.Pointer.ToInt64());
    }

    // Instance callback identity survives a static shop Clear/replace. Only this exact native
    // payable may settle its own ticket; a stale callback returns its own floating coins.
    internal static void OnPay(HeroShopOwner owner, Player player)
    {
        if (owner == null || player == null) return;
        long payable = owner.PayablePointer();
        if (payable == 0 || player._completingPayable == null
            || player._completingPayable.Pointer.ToInt64() != payable) return;
        long payer = player.Pointer.ToInt64(), origin = owner.Pointer.ToInt64();
        if (Payment.AlreadySettled(payer, payable, origin)) return;
        bool completed = player._payState == Player.PayState.Completed;
        int coins = player._floatingCurrency == null ? 0 : player._floatingCurrency.Count;
        bool current = _owner != null && _owner.Pointer == owner.Pointer && _payable != null
            && _payable.Pointer.ToInt64() == payable;
        if (!current || !Payment.IsArmed(payer, payable, origin) || !completed || coins != Price)
        {
            if (player.hasLocalAuthority && completed && coins > 0)
            {
                player.CancelTransaction();
                player.DropFloatingCurrency();
                Payment.Cancel(payer, payable, origin);
                Log("stale/incomplete callback cancelled through native floating currency");
            }
            return;
        }
        if (!Payment.Consume(payer, payable, origin, true, coins)) return;
        try
        {
            bool success = false;
            string reason = "商店已不可用";
            try { if (CanPurchase()) success = HeroRecruitment.TryPurchase(out reason); }
            catch (Exception e) { reason = e.GetType().Name; }
            if (!success)
            {
                // Clear may have run reentrantly. If native cancellation already returned the
                // floating coins, a wallet refund here would mint a second eight.
                if (player._floatingCurrency != null && player._floatingCurrency.Count == Price)
                {
                    player.wallet.AddCurrency(CurrencyType.Coins, Price);
                    Log("purchase rejected; refunded 8 coins: " + reason);
                }
                else Log("purchase rejected after native cancellation: " + reason);
            }
            else Log("purchase completed");
            _status = success ? "英雄已应召" : "未招募，已退还 8 金币";
        }
        finally { Payment.Finish(payer, payable, origin); }
    }

    internal static void Clear(string reason)
    {
        if (_clearing) return;
        bool hadState = _object != null || _payable != null || _owner != null || _kingdom != null
            || _header != null || _layer != null;
        bool wasRetrying = _retiring;
        _clearing = true;
        _retiring = true;
        _retryReason = reason;
        bool cleaned = false;
        try
        {
            if (_payable != null)
            {
                _payable.forceBlockPayment = true;
                CancelPendingTransactions();
                if (_started != null) _payable.remove_OnTransactionStartedCallback(_started);
            }
            // Keep exact settled/cancelled tickets so delayed instance callbacks cannot settle twice.
            // Match the original registry, never delete a reused NetID from a new island.
            if (_postbox != null && _header != null && _object != null
                && _postbox.MasterSemiCRPCHLookup.TryGetValue(_object, out var found)
                && found != null && found.Pointer == _header.Pointer)
                _postbox.DeregisterObject(_object, _header.NetID, CRPCType.SemiStatic);
            if (_object != null) { _object.SetActive(false); UnityEngine.Object.Destroy(_object); }
            cleaned = true;
        }
        catch (Exception e)
        {
            _cleanupRetryAt = Time.unscaledTime + 1f;
            if (!_cleanupFailureLogged) { Log("cleanup deferred/failed reason=" + reason + ": " + e.GetType().Name); _cleanupFailureLogged = true; }
        }
        finally
        {
            _clearing = false;
            if (cleaned)
            {
                HeroShopBannerVisuals.Clear();
                _nextV2StateAt = 0; _leftV2State = _rightV2State = 0;
                _object = null; _payable = null; _owner = null; _kingdom = null; _layer = null;
                _postbox = null; _header = null; _started = null;
                _v2?.Clear(); _v2 = null;
                _renderer = null; _retiring = false;
                _cleanupRetryAt = 0f; _cleanupFailureLogged = false; _menuSuspended = false;
                // One bounded line per real cleanup, never per frame with nothing to clean.
                if (hadState && !wasRetrying) Log("clear reason=" + reason);
                _visualSelection = -1; _visualSnapshots = 0; _visualDiagnosticFailed = false;
            }
        }
    }

    /// <summary>Menu pause with an open native transaction: cancel it through the native path
    /// exactly once so the floating coins return, then keep the retained shop payment-blocked.</summary>
    private static void SuspendPendingTransaction()
    {
        if (_payable == null || _kingdom == null) return;
        if (!Pending(_kingdom.playerOne) && !Pending(_kingdom.playerTwo)) return;
        CancelPendingTransactions();
        Log("pause with a pending payment; native cancellation returned the coins");
    }

    private static bool Pending(Player player)
    {
        if (player == null || !player.hasLocalAuthority || _payable == null) return false;
        return (player.selectedPayable != null && player.selectedPayable.Pointer == _payable.Pointer)
            || (player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer);
    }

    // Event-only diagnostic: inspect our shop and the selecting player's own hierarchy.
    // No scene scan, no renderer/material mutation; bounded to four selection snapshots.
    private static void ObserveVisualSelection()
    {
        try
        {
            if (_visualDiagnosticFailed || _kingdom == null || _object == null || _payable == null) return;
            int selection = (Pending(_kingdom.playerOne) ? 1 : 0) | (Pending(_kingdom.playerTwo) ? 2 : 0);
            if (selection == _visualSelection) return;
            _visualSelection = selection;
            if (selection == 0 || _visualSnapshots >= 4) return;
            _visualSnapshots++;
            Log("visual-selection=" + selection + " snapshot=" + _visualSnapshots);
            DescribeRenderers("shop", _object, 14);
            if ((selection & 1) != 0) DescribeRenderers("player1", _kingdom.playerOne.gameObject, 18);
            if ((selection & 2) != 0) DescribeRenderers("player2", _kingdom.playerTwo.gameObject, 18);
        }
        catch (Exception e)
        {
            _visualDiagnosticFailed = true;
            Log("visual diagnostic unavailable: " + e.GetType().Name);
        }
    }

    private static void DescribeRenderers(string source, GameObject root, int limit)
    {
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; renderers != null && i < Math.Min(renderers.Length, limit); i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var material = r.sharedMaterial;
            var bounds = r.bounds;
            Log("visual " + source + " name=" + r.gameObject.name + " active=" + r.gameObject.activeInHierarchy
                + " enabled=" + r.enabled + " alpha=" + r.color.a.ToString("F2")
                + " z=" + r.transform.position.z.ToString("F4") + " order=" + r.sortingOrder
                + " bounds=" + bounds.center + "/" + bounds.size
                + " shader=" + (material != null && material.shader != null ? material.shader.name : "none")
                + " flash=" + (material != null && material.IsKeywordEnabled("FLASH_ON"))
                + " stencil=" + DescribeMaterialFloat(material, "_CustomStencilComp") + "/"
                + DescribeMaterialFloat(material, "_CustomStencilOp") + "/" + DescribeMaterialFloat(material, "_CustomStencilRef"));
        }
    }

    private static string DescribeMaterialFloat(Material material, string property)
        => material != null && material.HasProperty(property) ? material.GetFloat(property).ToString("F0") : "none";

    /// <summary>Call from the existing native Save prefix before wallet serialization.</summary>
    internal static void CancelPendingTransactions()
    {
        if (_payable != null && _kingdom != null)
        {
            Cancel(_kingdom.playerOne);
            Cancel(_kingdom.playerTwo);
        }
    }

    private static void Cancel(Player player)
    {
        if (player == null || !player.hasLocalAuthority) return;
        bool selected = player.selectedPayable != null && player.selectedPayable.Pointer == _payable.Pointer;
        bool completing = player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer;
        if (!selected && !completing) return;
        long payer = player.Pointer.ToInt64(), source = _payable.Pointer.ToInt64();
        long owner = _owner != null ? _owner.Pointer.ToInt64() : 0;
        if (Payment.IsSettling(payer, source, owner)) return;
        player.CancelTransaction();
        player.DropFloatingCurrency(); // same native cancellation refund; clears the list itself
        if (completing) player._completingPayable = null;
        if (selected) player.DeselectPayable();
        Payment.Cancel(payer, source, owner);
    }

    private static void UpdateV2Visuals()
    {
        if (_v2 == null) return;
        _v2.TickMerchant(Time.time);
        // Keep the existing read-only seat sampling cadence; the recruitment owner supplies context.
        if (Time.unscaledTime >= _nextV2StateAt)
        {
            _nextV2StateAt = Time.unscaledTime + .25f;
            _leftV2State = HeroShopBannerVisuals.VisualState(HeroRecruitment.GetShopSeatState(-1), HeroRecruitment.HasFallenSeat(-1));
            _rightV2State = HeroShopBannerVisuals.VisualState(HeroRecruitment.GetShopSeatState(1), HeroRecruitment.HasFallenSeat(1));
        }
        _v2.TickHero(_leftV2State, _rightV2State, Time.time);
    }

    private static bool LoadArt()
    {
        return ShopV2Art.TryGet(ShopV2Kind.Hero, 0, out _sprite);
    }

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroShop] " + message); } catch { }
    }
}

internal sealed class HeroShopOwner : MonoBehaviour
{
    private Il2CppSystem.Action<Player> _onPay;
    private long _payablePointer;
    [HideFromIl2Cpp] internal long PayablePointer() => _payablePointer;
    [HideFromIl2Cpp] internal void BindPayable(PayableComponent payable) => _payablePointer = payable != null ? payable.Pointer.ToInt64() : 0;
    public HeroShopOwner(IntPtr pointer) : base(pointer) { }
    [HideFromIl2Cpp] internal void Initialize() { _onPay = (Il2CppSystem.Action<Player>)HandlePay; }
    [HideFromIl2Cpp] private void HandlePay(Player player) => HeroShop.OnPay(this, player);
    public Il2CppSystem.Action<Player> OnPay => _onPay;
    public bool CanPay(Player player) => HeroShop.CanPlayerPurchase(player);
    // The installed ClassInjector matches interface methods by name/arity. Its ref-enum
    // invoker emits invalid `ldobj LockReason&`, although registration succeeds. Native
    // IsLocked has ABI (this, Player*, LockReason*): receive that output pointer directly,
    // so both the invoker and direct trampoline pass it unchanged. Do not read an out value
    // and never use a pointer-sized store for this 32-bit enum. The native interface wrapper
    // above remains unchanged and verifies the complete roundtrip before any payment.
    public bool IsLocked(Player player, IntPtr reason)
    {
        return HeroShopOwnerInterop.WriteUnlocked(reason, (int)LockIndicator.LockReason.NotLocked);
    }
    public void OnDisable() { HeroShop.Clear("owner-disabled"); }
}
#endif
