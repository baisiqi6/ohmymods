using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
#if !MUSKETEER_SHOP_CORE_ONLY
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

internal sealed class MusketeerShopPayment
{
    private long _payer;
    private bool _armed;
    private bool _settled;
    internal void Arm(long payer) { _payer = payer; _armed = payer != 0; _settled = false; }
    internal bool IsArmed(long payer) => _armed && payer != 0 && payer == _payer;
    internal bool AlreadySettled(long payer) => _settled && payer != 0 && payer == _payer;
    internal bool Consume(long payer, bool completed, int coins)
    {
        if (!_armed || payer == 0 || payer != _payer || !completed || coins != 4) return false;
        _armed = false;
        _settled = true;
        return true;
    }
    internal void Clear() { _armed = false; _settled = false; _payer = 0; }
}

#if !MUSKETEER_SHOP_CORE_ONLY
internal static partial class MusketeerShop
{
    internal const int Price = 4;
    private static GameObject _object;
    private static PayableComponent _payable;
    private static MusketeerShopOwner _owner;
    private static Kingdom _kingdom;
    private static Transform _layer;
    private static NetworkPostbox _postbox;
    private static CRPCHeader _header;
    private static Texture2D _texture;
    private static Sprite _sprite;
    private static Sprite[] _frames;
    private static SpriteRenderer _renderer;
    private static int _frame = -1;
    private static Il2CppSystem.Action<Player> _started;
    private static readonly MusketeerShopPayment Payment = new();
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
    // Legacy selection-time hierarchy dumps (14 shop + 18 player renderer lines per selection
    // change) served their banner/material investigation; they are obsolete now. Default off:
    // the dumps never run (one cheap field read per frame), while the direct error logs
    // elsewhere stay. Flip only for a new, bounded investigation.
    private static readonly bool VisualSelectionDiagnostics = false;

    // --- cached native Bow tool prefab: discovered only in Tick, read-only in the payment path ---
    private static readonly MusketeerBowCachePolicy BowCache = new();
    private static readonly Func<string, long> BowPoolResolver = ResolveBowPoolPointer;
    private static DroppableTool _bowPrefab;
    private static float _bowLogAt;
    private static int _bowCaptures;
    private const float BowLogIntervalSeconds = 60f;
    private const int PayableScanLimit = 4096;
    // Verified native asset + native pool key: resources.assets holds exactly one Bow tool
    // (ToolBow, id 19747) and its native pool prefab ("ToolBow (Pool)", id 36589), so the
    // fallback below resolves the already-registered pool by its real name - never an asset
    // enumeration, never a Resources path, never a guessed or custom path.
    private const string NativeBowPrefabName = "ToolBow";
    private const int PurchaseTimingLogBudget = 12;
    private const double SlowPurchaseMs = 8.0;
    private static int _purchaseTimingLogs;
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
                    _status = "已关闭，已购职业与枪具记录保留";
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
                    if (VisualSelectionDiagnostics) ObserveVisualSelection();
                    MaintainBowCache();
                    MaintainRackLayout();
                    if (_menuSuspended) { _menuSuspended = false; Log("resume: retained same shop"); }
                    float ambientPhase = Time.time % 4f;
                    int frame = ambientPhase < 2f ? 0 : ambientPhase < 2.5f ? 1 : ambientPhase < 3.5f ? 2 : 3;
                    if (_renderer != null && frame != _frame) { _renderer.sprite = _frames[frame]; _frame = frame; }
                    _payable.forceBlockPayment = !CanPurchase();
                    _status = BowReady() ? MusketeerIdentity.StatusText : "火铳铺：等待原生弓具（未收费）";
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
                    _status = "已暂停，火铳铺保留";
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
            FeatureEnabled = ModConfig.Enabled.Value && ModConfig.MusketeerEnabled.Value,
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
        if (!ModConfig.Enabled.Value || !ModConfig.MusketeerEnabled.Value
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
            // header. A Menu pause retains the shop but never re-enables payment. The cached
            // native Bow prefab must still be exact before any coin may be accepted: an
            // unavailable asset blocks payment up front instead of taking and refunding 4 coins.
            if (_clearing || _retiring || _object == null || _payable == null || _owner == null
                || IslandSaveData.isSavingGame || !_rackLayoutReady || !MusketeerIdentity.CanPurchase || RackCount() >= 3) return false;
            if (!HeroShopRetention.CanServe(Observe(out _, out _))) return false;
            return BowReady();
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
                    && Payment.IsArmed(player.Pointer.ToInt64());
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
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(MusketeerShopOwner)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(MusketeerShopOwner), new RegisterTypeOptions
                { Interfaces = new[] { typeof(IPayableComponentOwner) } });
            _registered = true;
        }
        _createStage = "load-art";
        if (!LoadArt()) { _status = "火铳铺素材尚未就绪"; return; }
        var payables = Managers.Inst.payables;
        float halfWidth = MusketeerShopRules.HalfWidth;
        _createStage = "find-position";
        if (!HeroShopPlacement.Find(kingdom.GetBorderSideIntact(Side.Left), kingdom.GetBorderSideIntact(Side.Right),
            halfWidth, p => !payables.OverlapsAnyExclusions(p, halfWidth, false), out float layoutCenter))
        { _status = "领地中段暂时没有商店空位"; return; }

        float position = layoutCenter - MusketeerShopRules.LayoutCenterX;
        _createStage = "ground-reference";
        if (!TryNativeGroundAnchor(layer, out float rootY, out float groundY))
        {
            _status = "等待原生商店地面参照";
            return; // no same-world native shop yet: never fall back to a guessed height
        }
        _kingdom = kingdom; _layer = layer;
        _createStage = "create-renderer";
        _object = new GameObject("KEM_MusketeerShop");
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
        _createStage = "create-owner";
        _object.AddComponent<CRPCStamp>();
        _owner = _object.AddComponent<MusketeerShopOwner>();
        _owner.Initialize();
        _createStage = "create-payable";
        _payable = _object.AddComponent<PayableComponent>();
        _payable.forceBlockPayment = true;
        _payable.Init(_owner.Cast<IPayableComponentOwner>());
        _payable.Price = Price;
        _payable.Currency = CurrencyType.Coins;
        _payable.priceIncrease = 0;
        _payable.indicatorSpacing = 0.4f;
        _payable.indicatorOffset = Vector2.zero;
        _payable.repositionInSplitScreen = false;
        _payable.playerPayPointOffset = Vector2.zero;
        _payable.playerPayDistance = 1f;
        _payable.payablePlacementExclusionOffset = new Vector2(MusketeerShopRules.LayoutCenterX, 0f);
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
        _payable.forceBlockPayment = !CanPurchase();
        _status = "火铳铺：4 金币";
        _createStage = "ready";
        Log("ground source=native-root rootY=" + rootY.ToString("F4")
            + " bodyOffset=" + HeroShopGrounding.BodyGroundOffset.ToString("F4")
            + " groundY=" + groundY.ToString("F4") + " finalY=" + groundY.ToString("F4"));
        Log("ready x=" + position.ToString("F2") + " native-payment=registered price=4");
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
        Payment.Arm(player.Pointer.ToInt64());
    }

    internal static void OnPay(Player player)
    {
        if (player == null || _payable == null || player._completingPayable == null
            || player._completingPayable.Pointer != _payable.Pointer) return;
        long payer = player.Pointer.ToInt64();
        if (!Payment.Consume(payer, player._payState == Player.PayState.Completed,
            player._floatingCurrency == null ? 0 : player._floatingCurrency.Count))
        {
            if (Payment.AlreadySettled(payer)) return; // repeated callback for the same native payment
            // The native final CanPay gate above prevents incomplete/unrecorded payments.
            // If another caller bypasses it, return only the actual floating objects, never
            // mint an assumed four coins. DropFloatingCurrency clears the source list, so
            // the native caller's later consume loop has nothing to charge a second time.
            if (player.hasLocalAuthority && player._payState == Player.PayState.Completed)
            {
                player.CancelTransaction();
                player.DropFloatingCurrency();
                Payment.Clear();
                Log("unrecorded/incomplete callback cancelled; native floating coins returned");
            }
            return;
        }
        bool success = false;
        string reason = "商店已不可用";
        try { if (CanPurchase()) success = TryCreateGun(out reason); }
        catch (Exception e) { reason = e.GetType().Name; }
        if (!success)
        {
            // Coins already left the wallet on entering the native floating slots. The caller
            // consumes those slots after this callback, so restore only the wallet balance once.
            player.wallet.AddCurrency(CurrencyType.Coins, Price);
            Log("purchase rejected; refunded 4 coins: " + reason);
        }
        else Log("purchase completed");
        _status = success ? "火枪已上架，等待居民领取" : "未出货，已退还 4 金币";
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
            Payment.Clear();
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
                _object = null; _payable = null; _owner = null; _kingdom = null; _layer = null;
                _postbox = null; _header = null; _started = null;
                _renderer = null; _frame = -1; _retiring = false;
                RackLayout.Reset(); _rackLayoutReady = false; _nextRackLayoutAt = 0f; RackItems.Clear();
                Array.Clear(RackItemCache, 0, RackItemCache.Length); SlotNumbers.Clear();
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
        Payment.Clear();
    }

    private static void Cancel(Player player)
    {
        if (player == null || !player.hasLocalAuthority) return;
        bool selected = player.selectedPayable != null && player.selectedPayable.Pointer == _payable.Pointer;
        bool completing = player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer;
        if (!selected && !completing) return;
        player.CancelTransaction();
        player.DropFloatingCurrency(); // same native cancellation refund; clears the list itself
        if (completing) player._completingPayable = null;
        if (selected) player.DeselectPayable();
    }


    private static readonly System.Collections.Generic.List<int> SlotNumbers = new();
    private static readonly System.Collections.Generic.List<MusketeerRackLayout.Item> RackItems = new(3);
    private static readonly MusketeerRackLayout.Item[] RackItemCache = new MusketeerRackLayout.Item[3];
    private static readonly MusketeerRackLayout RackLayout = new();
    private static bool _rackLayoutReady;
    private static float _nextRackLayoutAt;

    /// <summary>Tick-only maintenance of the native Bow prefab cache. Discovery runs here - never
    /// on the payment path - at most once per bounded retry window, and any capture that stopped
    /// matching its world/pool identities is dropped first so no stale prefab can be spawned.</summary>
    private static void MaintainBowCache()
    {
        try
        {
            if (ReadBowContext(out var context) && BowUsable(in context)) return;
            if (BowCache.HasBinding || _bowPrefab != null) InvalidateBowCache("stale");
            if (!BowCache.TryBeginAttempt(Time.unscaledTime)) return;
            if (!TryDiscoverBow(out string reason))
            {
                if (Time.unscaledTime >= _bowLogAt)
                {
                    _bowLogAt = Time.unscaledTime + BowLogIntervalSeconds;
                    Log("native bow prefab unavailable (" + reason + "); payment stays blocked");
                }
                return;
            }
            if (Time.unscaledTime >= _bowLogAt)
            {
                _bowLogAt = Time.unscaledTime + BowLogIntervalSeconds;
                Log("native bow prefab cached name=" + BowCache.PrefabName + " captures=" + _bowCaptures);
            }
        }
        catch (Exception e)
        {
            InvalidateBowCache("error");
            if (Time.unscaledTime >= _bowLogAt)
            {
                _bowLogAt = Time.unscaledTime + BowLogIntervalSeconds;
                Log("bow cache maintenance failed: " + e.GetType().Name);
            }
        }
    }

    /// <summary>Payment-side read: the cached prefab exists and its full world/pool binding is
    /// still exact. Cheap and side-effect free; never discovers or enumerates assets.</summary>
    private static bool BowReady()
    {
        try { return ReadBowContext(out var context) && BowUsable(in context); }
        catch { return false; }
    }

    private static bool BowUsable(in MusketeerBowCachePolicy.Context context)
    {
        if (!TryPrefabIdentity(_bowPrefab, out long prefabPointer, out int prefabGoId)) return false;
        if (!BowCache.MatchesPrefab(prefabPointer, prefabGoId)) return false;
        return BowCache.TryUse(in context, BowPoolResolver);
    }

    /// <summary>Single source of the cached prefab identity: always the prefab GameObject, never
    /// the component wrapper (DroppableTool.Pointer and gameObject.Pointer are different native
    /// pointers, so mixing the two kinds silently rejects every capture). Capture and validation
    /// both consume this helper.</summary>
    private static bool TryPrefabIdentity(DroppableTool prefab, out long pointer, out int goId)
    {
        pointer = 0;
        goId = 0;
        if (prefab == null || prefab.gameObject == null) return false;
        pointer = prefab.gameObject.Pointer.ToInt64();
        goId = prefab.gameObject.GetInstanceID();
        return pointer != 0;
    }

    private static void InvalidateBowCache(string reason)
    {
        if (!BowCache.HasBinding && _bowPrefab == null) return;
        BowCache.Invalidate();
        _bowPrefab = null;
        if (Time.unscaledTime >= _bowLogAt)
        {
            _bowLogAt = Time.unscaledTime + BowLogIntervalSeconds;
            Log("bow cache invalidated reason=" + reason);
        }
    }

    /// <summary>Current world identities the cache is bound to. An unreadable context is
    /// fail-closed: the capture is not usable and payment stays blocked.</summary>
    private static bool ReadBowContext(out MusketeerBowCachePolicy.Context context)
    {
        context = default;
        try
        {
            var managers = Managers.Inst;
            var pools = managers != null ? managers.pools : null;
            var world = managers != null ? managers.world : null;
            var biome = BiomeHolder.Inst;
            if (pools == null || world == null || world.gameLayer == null || biome == null || biome.BiomeIndex < 0) return false;
            context = new MusketeerBowCachePolicy.Context
            {
                WorldReady = true,
                PoolManager = pools.Pointer.ToInt64(),
                World = world.gameLayer.Pointer.ToInt64(),
                Biome = biome.Pointer.ToInt64(),
                BiomeIndex = biome.BiomeIndex,
            };
            return true;
        }
        catch { return false; }
    }

    /// <summary>Live resolver for the captured name: the pool must still be the exact captured
    /// instance AND still base on the exact captured prefab asset. Any mismatch - rebuilt pools,
    /// name reuse, missing pool - returns 0 and the payment gate treats the capture as stale.</summary>
    private static long ResolveBowPoolPointer(string name)
    {
        try
        {
            var prefab = _bowPrefab;
            if (prefab == null || prefab.gameObject == null || string.IsNullOrEmpty(name)) return 0;
            var pools = Managers.Inst != null ? Managers.Inst.pools : null;
            if (pools == null) return 0;
            var pool = pools.GetPoolByPrefabName(name);
            if (pool == null || pool.prefab == null) return 0;
            return pool.prefab.Pointer == prefab.gameObject.Pointer ? pool.Pointer.ToInt64() : 0;
        }
        catch { return 0; }
    }

    /// <summary>Bounded discovery of the already-loaded native Bow prefab. Preferred source: the
    /// exact DroppableTool a live native equipment shop holds as PayableShop.itemPrefab - the same
    /// reference native CreateItem() spawns. Fallback (no Bow shop placed yet): the already-
    /// registered native ToolBow pool's own prefab, resolved by its verified native pool key. No
    /// Resources enumeration, no guessed asset path, and a spawned live instance is rejected by
    /// ValidateBowCandidate. Runs only from MaintainBowCache.</summary>
    private static bool TryDiscoverBow(out string reason)
    {
        reason = "没有可用的原生弓商店";
        var managers = Managers.Inst;
        if (managers == null) { reason = "世界上下文尚未就绪"; return false; }
        var pools = managers.pools;
        if (pools == null) { reason = "对象池尚未就绪"; return false; }
        var layer = managers.world != null ? managers.world.gameLayer : null;
        var all = managers.payables != null ? managers.payables.AllPayables : null;
        for (int i = 0; layer != null && all != null && i < Math.Min(all.Length, PayableScanLimit); i++)
        {
            var shop = all[i];
            if (shop == null || !shop.isActiveAndEnabled || shop.transform == null || !shop.transform.IsChildOf(layer)) continue;
            var native = shop.TryCast<PayableShop>();
            if (native == null) continue;
            var droppable = native.itemPrefab;
            if (droppable == null || droppable.gameObject == null) continue;
            var tool = droppable.TryCast<DroppableTool>();
            if (tool == null) continue;
            if (!ValidateBowCandidate(tool, pools, out var pool, out reason)) continue;
            if (TryCaptureBow(tool, pool, out reason)) return true;
        }
        return TryDiscoverBowFallback(out reason);
    }

    /// <summary>Fallback when no native Bow shop is placed yet: the already-registered native
    /// ToolBow pool's own prefab. The name is the verified native pool key (resources.assets holds
    /// exactly one Bow tool "ToolBow", id 19747, and its native pool "ToolBow (Pool)", id 36589),
    /// the object is the pool's asset - never a spawned instance - and the same
    /// ValidateBowCandidate rules apply unchanged.</summary>
    private static bool TryDiscoverBowFallback(out string reason)
    {
        reason = "原生弓具池尚未注册";
        var managers = Managers.Inst;
        if (managers == null) { reason = "世界上下文尚未就绪"; return false; }
        var pools = managers.pools;
        if (pools == null) { reason = "对象池尚未就绪"; return false; }
        var pool = pools.GetPoolByPrefabName(NativeBowPrefabName);
        if (pool == null || pool.prefab == null) return false;
        var tool = pool.prefab.GetComponent<DroppableTool>();
        if (tool == null) { reason = "ToolBow 池 prefab 缺少 DroppableTool 组件"; return false; }
        if (!ValidateBowCandidate(tool, pools, out var resolved, out reason)) return false;
        return TryCaptureBow(tool, resolved, out reason);
    }

    /// <summary>Commits one validated candidate. The capture takes the identity from the shared
    /// TryPrefabIdentity (GameObject pointer + Unity instance id) and immediately runs the
    /// payment-path validator as a live self-check, so a component/GameObject pointer mix-up can
    /// never bind silently; a failed self-check drops the capture and keeps the bounded retry
    /// cadence instead of rescanning every frame.</summary>
    private static bool TryCaptureBow(DroppableTool tool, Pool pool, out string reason)
    {
        reason = "弓具身份尚未就绪";
        if (!TryPrefabIdentity(tool, out long prefabPointer, out int prefabGoId)) return false;
        if (!ReadBowContext(out var context)) { reason = "世界上下文尚未就绪"; return false; }
        BowCache.Capture(prefabPointer, prefabGoId, tool.gameObject.name, pool.Pointer.ToInt64(), in context);
        if (!BowCache.HasBinding) { reason = "弓具身份未通过捕获校验"; return false; }
        _bowPrefab = tool;
        if (!BowUsable(in context))
        {
            _bowPrefab = null;
            BowCache.Invalidate();
            BowCache.DeferRetry(Time.unscaledTime, MusketeerBowCachePolicy.RetrySeconds);
            reason = "弓具身份自检未通过";
            return false;
        }
        _bowCaptures++;
        reason = "";
        return true;
    }

    /// <summary>A candidate is only the real native asset when its Persistent contract is intact,
    /// it is not a live world instance, and the live PoolManager already bases a pool on that
    /// exact prefab. Everything else keeps the cache empty (payment blocked) instead of spawning
    /// through a wrong or stale pool.</summary>
    private static bool ValidateBowCandidate(DroppableTool tool, PoolManager pools, out Pool pool, out string reason)
    {
        pool = null;
        reason = "原生弓具尚未就绪";
        if (tool == null || tool.gameObject == null || tool.gameObject.tag != "Bow")
        { reason = "候选弓具标签不是 Bow"; return false; }
        var persistent = tool.GetComponent<Persistent>();
        if (persistent == null || !persistent.persistObject || string.IsNullOrEmpty(persistent.path))
        { reason = "原生弓具持久字段缺失"; return false; }
        // A spawned live instance is never a prefab: it lives under the current world GameLayer.
        if (MusketeerAccess.InWorld(tool.gameObject)) { reason = "候选弓具是场上实例"; return false; }
        pool = pools.GetPoolByPrefabName(tool.gameObject.name);
        if (pool == null || pool.prefab == null) { reason = "原生弓具尚无对象池"; return false; }
        if (pool.prefab.Pointer != tool.gameObject.Pointer) { reason = "同名弓具与对象池不匹配"; return false; }
        return true;
    }
    private static int RackCount()
    {
        if (!ReadRackItems()) return MusketeerShopRules.RackCapacity;
        foreach (var item in RackItems)
            if (!item.Unclaimed || !RackLayout.IsPlaced(item)) return MusketeerShopRules.RackCapacity;
        return RackItems.Count;
    }

    private static bool RackContextReady()
    {
        if (_clearing || _retiring || _object == null || _layer == null || IslandSaveData.isSavingGame
            || Time.timeScale <= 0f || !HeroShopRetention.CanServe(Observe(out _, out _))) return false;
        var state = MusketeerIdentity.Current;
        if (state == null || !state.Ready || state.ReadOnly || state.Unresolved || !state.HasBaseline
            || state.Epoch == null || state.StockRestores.Count != 0) return false;
        // Match the manual shop's existing state gates, not automatic population coverage.
        // An unrelated unbound historical unit must neither trigger native roster reads here
        // nor newly lock manual purchases. TryContext uses Identity's existing per-frame cache.
        return MusketeerIdentity.TryContext(out string context, out long world)
            && context == state.ContextKey && world == state.World;
    }

    // Read only the already-bound identity registry; no scene/resource discovery and no writes
    // until every slot has been checked for duplicate/invalid metadata.
    private static bool ReadRackItems()
    {
        RackItems.Clear(); SlotNumbers.Clear();
        if (!RackContextReady()) return false;
        var state = MusketeerIdentity.Current;
        if (state == null) return false;
        int occupied = 0;
        foreach (var career in state.Careers)
        {
            if (career == null) return false;
            if (career.Kind != MusketeerCareer.KindGun || career.StockSlot == MusketeerCareer.NoStockSlot) continue;
            // Check managed slot metadata before native proof: even malformed duplicate
            // records can never expand this path beyond three native gun inspections.
            if (!MusketeerShopRules.ValidSlot(career.StockSlot)
                || (occupied & (1 << career.StockSlot)) != 0) return false;
            occupied |= 1 << career.StockSlot;
            if (!MusketeerIdentity.StockClaimProven(career)) return false;
            var gun = career.Tool;
            if (gun == null || gun.gameObject == null) return false;
            var stamp = new MusketeerRackLayout.Stamp(career, state, career.Life, gun.Pointer.ToInt64(),
                gun.gameObject.GetInstanceID(), _object.GetInstanceID(), _layer.Pointer.ToInt64());
            int slot = career.StockSlot;
            var item = RackItemCache[slot];
            if (item == null)
            {
                item = new MusketeerRackLayout.Item { Slot = slot };
                var captured = item;
                item.MoveAndVerify = (x, y, z) => AnchorRackGun((MusketeerIdentity.Career)captured.Identity.Career,
                    captured.Identity, captured.Slot, x, y, z);
                RackItemCache[slot] = item;
            }
            item.Identity = stamp;
            item.Unclaimed = gun.friendlyClaimer == null && gun.enemyClaimer == null && !gun.pickedUp;
            RackItems.Add(item);
            SlotNumbers.Add(slot);
        }
        return MusketeerShopRules.TryMask(SlotNumbers, out _);
    }

    private static bool AnchorRackGun(MusketeerIdentity.Career career, MusketeerRackLayout.Stamp stamp,
        int slot, float x, float y, float z)
    {
        if (!RackContextReady() || !ReferenceEquals(MusketeerIdentity.Current, stamp.State)
            || career.Life != stamp.Life || career.StockSlot != slot
            || !MusketeerIdentity.StockClaimProven(career)) return false;
        var gun = career.Tool;
        if (gun == null || gun.Pointer.ToInt64() != stamp.Pointer || gun.gameObject == null
            || gun.gameObject.GetInstanceID() != stamp.Instance || _object.GetInstanceID() != stamp.Shop
            || _layer.Pointer.ToInt64() != stamp.Layer || gun.pickedUp
            || gun.friendlyClaimer != null || gun.enemyClaimer != null) return false;
        var body = gun.GetComponent<Rigidbody2D>();
        if (body == null || !body.isKinematic) return false; // native load must finish its own stock-physics restore
        Vector3 target = _object.transform.position + new Vector3(x, y, z);
        if (!SamePosition(gun.transform.position, target)) gun.transform.position = target;
        return SamePosition(gun.transform.position, target);
    }

    private static bool SamePosition(Vector3 actual, Vector3 target)
        => float.IsFinite(actual.x) && float.IsFinite(actual.y) && float.IsFinite(actual.z)
            && Mathf.Abs(actual.x - target.x) < 0.001f && Mathf.Abs(actual.y - target.y) < 0.001f
            && Mathf.Abs(actual.z - target.z) < 0.001f;

    private static void MaintainRackLayout()
    {
        if (Time.unscaledTime < _nextRackLayoutAt) return;
        _nextRackLayoutAt = Time.unscaledTime + 0.5f;
        try { _rackLayoutReady = ReadRackItems() && RackLayout.Reconcile(true, RackItems); }
        catch { _rackLayoutReady = false; } // do not destroy a paid gun or clear its identity on a layout read failure
    }

    internal static bool TryGetRackSorting(DroppableTool gun, out int layer, out int order)
    {
        layer = order = 0;
        if (_renderer == null || !IsRackGun(gun)) return false;
        layer = _renderer.sortingLayerID; order = Math.Min(32767, _renderer.sortingOrder + 1);
        return true;
    }
    internal static bool IsRackGun(DroppableTool gun)
    {
        try { return gun != null && !gun.pickedUp && MusketeerAccess.InWorld(gun)
                && MusketeerIdentity.StockSlot(gun) >= 0; }
        catch { return false; }
    }
    private static bool TryCreateGun(out string reason)
    {
        reason = "枪具尚未就绪";
        var watch = Stopwatch.StartNew();
        double rackMs = 0, spawnMs = 0, identityMs = 0;
        bool created = false;
        try
        {
            if (!MusketeerIdentity.CanPurchase || _object == null || _layer == null) return false;
            // The native prefab must already be cached and still exact. This path never discovers
            // or loads assets, so a stale capture blocks here and the existing refund path returns
            // the coins; the next Tick revalidates or rediscovers outside the payment.
            if (!ReadBowContext(out var context) || !BowUsable(in context)) { reason = "原生弓具尚未就绪"; return false; }
            var prefab = _bowPrefab;
            if (RackCount() >= 3) { reason = "枪架已满"; return false; }
            // Spawn from the real native Bow pool, retaining origin and the valid persistent path.
            // The shop itself is transient; the paid gun is parented directly beneath GameLayer.
            if (!ReadRackItems()) { reason = "枪架身份尚未确认"; return false; }
            int slot = MusketeerShopRules.FirstFreeSlot(SlotNumbers);
            if (slot < 0) { reason = "枪架已满"; return false; }
            rackMs = watch.Elapsed.TotalMilliseconds;
            var position = _object.transform.position + new Vector3(MusketeerShopRules.SlotX(slot), MusketeerShopRules.SlotY(slot), MusketeerShopRules.SlotZ);
            DroppableTool gun = null;
            bool marked = false;
            try
            {
                gun = Pool.Spawn<DroppableTool>(prefab, position, Quaternion.identity, _layer, true);
                if (gun == null || gun.gameObject == null) return false;
                gun.transform.SetParent(_layer, true);
                gun.transform.position = position;
                gun.dropper = null; gun.parentShopRef = null;
                gun.selfDestruct = false;
                gun.pickUpPolicy = PickUpPolicy.Anybody;
                gun.pickedUp = false; gun.SetFake(false);
                var persistent = gun.GetComponent<Persistent>();
                if (persistent == null || !persistent.ShouldPersist() || string.IsNullOrEmpty(persistent.path))
                { reason = "原生枪具持久化尚未就绪"; return false; }
                if (gun._rigidbody != null) { gun._rigidbody.velocity = Vector2.zero; gun._rigidbody.angularVelocity = 0f; gun._rigidbody.isKinematic = true; }
                spawnMs = watch.Elapsed.TotalMilliseconds - rackMs;
                marked = MusketeerIdentity.TryRegisterPaidGun(gun, slot);
                identityMs = watch.Elapsed.TotalMilliseconds - rackMs - spawnMs;
                if (!marked) { reason = "职业记录暂不可写"; return false; }
                reason = "";
                created = true;
                return true;
            }
            finally
            {
                if (!marked && gun != null && gun.gameObject != null)
                {
                    MusketeerIdentity.ForgetUnpaidGun(gun);
                    gun.SetFake(true); gun.pickedUp = true;
                    gun.gameObject.SetActive(false); // even a failed pool return cannot leave a refunded usable weapon
                    Pool.Despawn(gun.gameObject, true);
                }
            }
        }
        finally
        {
            watch.Stop();
            LogPurchaseTimings(watch, rackMs, spawnMs, identityMs, created, reason);
        }
    }

    /// <summary>Bounded purchase-stage samples (Stopwatch around the real stages only; never per
    /// frame). The first <see cref="PurchaseTimingLogBudget"/> purchases are always sampled, later
    /// ones only when slow, so a hitch report still has numbers without log chatter.</summary>
    private static void LogPurchaseTimings(Stopwatch watch, double rackMs, double spawnMs, double identityMs, bool created, string reason)
    {
        try
        {
            double totalMs = watch.Elapsed.TotalMilliseconds;
            if (totalMs < SlowPurchaseMs && _purchaseTimingLogs >= PurchaseTimingLogBudget) return;
            _purchaseTimingLogs++;
            Log("purchase stage-ms total=" + totalMs.ToString("F1")
                + " rack=" + rackMs.ToString("F1")
                + " spawn=" + spawnMs.ToString("F1")
                + " identity=" + identityMs.ToString("F1")
                + (created ? " result=created" : " result=fail:" + reason));
        }
        catch { }
    }

    private static bool LoadArt()
    {
        if (_sprite != null) return true;
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KingdomEnhancedMod.MusketeerShop.png");
        if (stream == null || stream.Length > 2 * 1024 * 1024) return false;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false)) { UnityEngine.Object.Destroy(texture); return false; }
        if (texture.width != MusketeerShopRules.AtlasWidth || texture.height != MusketeerShopRules.FrameHeight) { UnityEngine.Object.Destroy(texture); return false; }
        texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; texture.anisoLevel = 0;
        _texture = texture;
        _frames = new Sprite[4];
        for (int i = 0; i < 4; i++)
            _frames[i] = Sprite.Create(texture, new Rect(i * MusketeerShopRules.FrameWidth, 0, MusketeerShopRules.FrameWidth, MusketeerShopRules.FrameHeight),
                new Vector2(MusketeerShopRules.PivotX, MusketeerShopRules.PivotY),
                32f, 0u, SpriteMeshType.FullRect);
        _sprite = _frames[0];
        return _sprite != null;
    }

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[MusketeerShop] " + message); } catch { }
    }
}

internal sealed class MusketeerShopOwner : MonoBehaviour
{
    private Il2CppSystem.Action<Player> _onPay;
    public MusketeerShopOwner(IntPtr pointer) : base(pointer) { }
    [HideFromIl2Cpp] internal void Initialize() { _onPay = (Il2CppSystem.Action<Player>)MusketeerShop.OnPay; }
    public Il2CppSystem.Action<Player> OnPay => _onPay;
    public bool CanPay(Player player) => MusketeerShop.CanPlayerPurchase(player);
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
    public void OnDisable() { MusketeerShop.Clear("owner-disabled"); }
}
#endif
