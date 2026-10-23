using System;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Visible shop-plot entrance. Normal candidate stays closed; explicitly enabled private payment uses the native receipt controller.</summary>
internal static class PalaceVisibleEntry
{
    private static bool _attempted;
    private static PalaceVisibleEntryScene _scene;

    // Root calls once with its existing Harmony instance. No attributes: disabled means no hooks.
    internal static void InstallOnce(Harmony harmony)
    {
        if (_attempted || !PalaceRuntimeProfile.Capability("PALACE_NODE1")) return;
        _attempted = true;
        try
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(PalaceVisibleEntryOwner)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(PalaceVisibleEntryOwner),
                    new RegisterTypeOptions { Interfaces = new[] { typeof(IPayableComponentOwner) } });
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(PalaceVisibleEntryScene)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(PalaceVisibleEntryScene));
            var host = new GameObject("KEM_PalaceVisibleEntry_Lifecycle");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _scene = host.AddComponent<PalaceVisibleEntryScene>();
            var postfix = new HarmonyMethod(typeof(PalaceVisibleEntry), nameof(WorldReady));
            harmony.Patch(AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Init)), postfix: postfix);
            harmony.Patch(AccessTools.Method(typeof(Kingdom), nameof(Kingdom.OnLevelLoaded)), postfix: postfix);
            // Current 2.4.0 interop: Game.Update is Private_Void (token 100669713),
            // Game.state is getter-only (100669666). Observe its actual post-update value;
            // OnGameStart/StartTime names alone do not establish the end of Intro/tutorial.
            harmony.Patch(AccessTools.Method(typeof(Game), nameof(Game.Update)),
                postfix: new HarmonyMethod(typeof(PalaceVisibleEntry), nameof(GameplayFrame)));
            harmony.Patch(AccessTools.Method(typeof(Game), nameof(Game.PreExitCampaign)),
                postfix: new HarmonyMethod(typeof(PalaceVisibleEntry), nameof(CampaignEnded)));
            Log("installed; kingdom lifecycle arms one Playing handoff; payment capability="
                + (PalaceRuntimeProfile.Capability("PALACE_ENTRY_PAYMENT_PRIVATE") ? "profile:on" : "profile:off"));
        }
        catch (Exception e) { Log("install failed: " + e); }
    }

    private static void WorldReady(Kingdom __instance)
    {
        try { if (_scene != null && __instance != null) _scene.Arm(__instance); }
        catch (Exception e) { Log("kingdom handoff fault: " + e); }
    }

    private static void GameplayFrame(Game __instance)
    {
        // Once the handoff is consumed, the method returns on managed fields before any Unity read.
        if (!ReferenceEquals(_scene, null)) _scene.ObservePlaying(__instance);
    }

    private static void CampaignEnded()
    {
        try { if (_scene != null) _scene.EndCampaign(); }
        catch (Exception e) { Log("campaign cleanup fault: " + e); }
    }

    internal static bool TryGetCurrentEntry(out PalaceVisibleEntryOwner owner, out PayableComponent payable)
    {
        owner = null; payable = null;
        return _scene != null && _scene.TryGetCurrentEntry(out owner, out payable);
    }

    internal static void OwnerDisabled(PalaceVisibleEntryOwner owner)
    {
        if (_scene != null) _scene.OwnerDisabled(owner);
    }

    /// <summary>付前只读宿主证据（实际推动 PaymentTick 的持续宿主）：GameObject 活跃且组件
    /// <c>isActiveAndEnabled</c> 真实启用（禁用组件不得被当作 ready 放行）。</summary>
    internal static bool HostReady
    {
        get
        {
            try
            {
                PalaceVisibleEntryScene scene = _scene;
                return scene != null && scene.gameObject != null && scene.gameObject.activeInHierarchy
                    && scene.isActiveAndEnabled;
            }
            catch (Exception) { return false; }
        }
    }

    internal static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[PalaceNode1] " + message); }
        catch (Exception) { }
    }
}

/// <summary>Only a lifecycle event opens a bounded readiness window; an established entrance is never respawn-scanned.</summary>
internal sealed class PalaceVisibleEntryScene : MonoBehaviour
{
    private Kingdom _pending, _kingdom;
    private Transform _layer;
    private GameObject _entry;
    private PalaceVisibleEntryOwner _owner;
    private PayableComponent _payable;
    private NetworkPostbox _postbox;
    private CRPCHeader _header;
    private GUIStyle _labelStyle;
    private string _labelText = "天朝入口";
    private float _labelHeight;
    private bool _labelFaulted, _labelDrawLogged;
    private float _deadline, _nextProbe;
    private bool _near;
    private Kingdom _borderKingdom;
    private Il2CppSystem.Action _borderChanged;
    private bool _needsPlot;
    private bool _awaitPlaying;
    private int _lastWaitingState = int.MinValue, _diagnostics;
    private string _waiting = "scene not ready";
    private IntPtr _attemptKingdom, _attemptLayer, _configPreparationGame, _paidPreparationGame;

    public PalaceVisibleEntryScene(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void Arm(Kingdom kingdom)
    {
        if (NetworkBigBoss.IsOnline) { CancelHandoff("online kingdom lifecycle"); Clear("online kingdom lifecycle"); return; }
        BindBorders(kingdom);
        // Init/OnLevelLoaded may happen while Loading or Intro. They establish the owner,
        // not the readiness timeout. A different lifecycle owner replaces the old handoff.
        if (_pending != null && _pending.Pointer == kingdom.Pointer) return;
        Managers managers = Managers.Inst;
        if (_entry != null && _kingdom != null && _kingdom.Pointer == kingdom.Pointer && _layer != null
            && managers != null && managers.world != null && managers.world.gameLayer != null
            && managers.world.gameLayer.Pointer == _layer.Pointer) return;
        if (_entry != null) Clear("kingdom lifecycle changed");
        _pending = kingdom;
        _awaitPlaying = true;
        _lastWaitingState = int.MinValue;
        _diagnostics = 0;
        PalaceVisibleEntry.Log("kingdom armed; readiness timer starts only after actual Playing");
    }

    [HideFromIl2Cpp]
    private void BindBorders(Kingdom kingdom)
    {
        if (_borderKingdom != null && _borderKingdom.Pointer == kingdom.Pointer) return;
        UnbindBorders();
        _borderChanged = (Il2CppSystem.Action)PlotChanged;
        kingdom.add_OnBordersChanged(_borderChanged);
        _borderKingdom = kingdom;
    }

    [HideFromIl2Cpp]
    private void UnbindBorders()
    {
        if (_borderKingdom != null && _borderChanged != null)
        {
            try { _borderKingdom.remove_OnBordersChanged(_borderChanged); }
            catch (Exception e) { PalaceVisibleEntry.Log("own border unsubscribe failed: " + e.Message); }
        }
        _borderKingdom = null; _borderChanged = null;
    }

    [HideFromIl2Cpp]
    private void PlotChanged()
    {
        // Only a real boundary change reopens a no-space handoff; no permanent world scan.
        if (!_needsPlot || _entry != null || _borderKingdom == null) return;
        Managers m = Managers.Inst;
        if (m == null || m.kingdom == null || m.game == null || m.game.state != Game.State.Playing
            || m.kingdom.Pointer != _borderKingdom.Pointer) return;
        Arm(_borderKingdom);
    }

    [HideFromIl2Cpp]
    internal void ObservePlaying(Game game)
    {
        if (!_awaitPlaying) return;
        try
        {
            if (game == null || _pending == null) { CancelHandoff("armed owner unavailable"); return; }
            Game.State state = game.state;
            if (state != Game.State.Playing)
            {
                // No Managers/world/payables/anchor scan while the player is in Intro.
                if (_lastWaitingState != (int)state)
                {
                    _lastWaitingState = (int)state;
                    if (_diagnostics++ < 8) PalaceVisibleEntry.Log("handoff waiting: game-state=" + state);
                }
                return;
            }
            Managers managers = Managers.Inst;
            if (NetworkBigBoss.IsOnline || managers == null || managers.game == null
                || managers.game.Pointer != game.Pointer || managers.kingdom == null
                || managers.kingdom.Pointer != _pending.Pointer)
            { CancelHandoff("Playing owner differs from armed kingdom/game"); return; }
            _awaitPlaying = false; // exactly one transfer; later frames cannot extend the deadline

            _deadline = Time.unscaledTime + 20f;
            _nextProbe = 0f;
            _diagnostics = 0;
            _waiting = "Playing readiness";
            PalaceVisibleEntry.Log("actual Playing handoff; starting bounded readiness window");
        }
        catch (Exception e) { CancelHandoff("Playing handoff fault: " + e.GetType().Name); }
    }

    [HideFromIl2Cpp]
    internal void EndCampaign()
    {
        CancelHandoff("campaign exit");
        Clear("campaign exit");
        UnbindBorders();
        _attemptKingdom = IntPtr.Zero;
        _attemptLayer = IntPtr.Zero;
        _configPreparationGame = _paidPreparationGame = IntPtr.Zero;
    }

    [HideFromIl2Cpp]
    private void CancelHandoff(string reason)
    {
        bool hadPending = _awaitPlaying || _pending != null;
        _awaitPlaying = false;
        _pending = null;
        if (hadPending) PalaceVisibleEntry.Log("handoff ended: " + reason);
    }

    [HideFromIl2Cpp]
    private void WaitFor(string reason)
    {
        if (_waiting == reason) return;
        _waiting = reason;
        if (_diagnostics++ < 12) PalaceVisibleEntry.Log("readiness waiting: " + reason);
    }

    private void Update()
    {
        try
        {
            if (_entry != null)
            {
                Managers m = Managers.Inst;
                if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth || m == null || m.game == null
                    || m.world == null || m.kingdom == null || _kingdom == null || _layer == null
                    || m.kingdom.Pointer != _kingdom.Pointer || m.world.gameLayer == null
                    || m.world.gameLayer.Pointer != _layer.Pointer
                    || (m.game.state != Game.State.Playing && m.game.state != Game.State.Menu))
                { Clear("world ended"); return; }
                if (m.game.state == Game.State.Playing) _owner.PaymentTick();
                bool near = m.game.state == Game.State.Playing
                    && (Near(_kingdom.playerOne) || Near(_kingdom.playerTwo));
                string text = near ? "天朝入口\n" + _owner.PaymentPrompt : "天朝入口";
                if (near != _near || _labelText != text)
                {
                    _near = near;
                    _labelText = text;
                }
                return;
            }
            if (_awaitPlaying || _pending == null) return;
            float now = Time.unscaledTime;
            if (now > _deadline)
            {
                PalaceVisibleEntry.Log("readiness window ended: " + _waiting);
                _pending = null;
                return;
            }
            if (now < _nextProbe) return;
            _nextProbe = now + 0.25f;
            Managers managers = Managers.Inst;
            if (NetworkBigBoss.IsOnline) { CancelHandoff("online world"); return; }
            if (!NetworkBigBoss.HasWorldAuth) { WaitFor("world authority unavailable"); return; }
            if (managers == null) { WaitFor("Managers unavailable"); return; }
            if (managers.game == null) { WaitFor("Game unavailable"); return; }
            if (managers.game.state != Game.State.Playing) { WaitFor("game-state=" + managers.game.state); return; }
            if (managers.kingdom == null) { WaitFor("Kingdom unavailable"); return; }
            if (managers.kingdom.Pointer != _pending.Pointer) { CancelHandoff("ready kingdom replaced"); return; }
            if (!_pending.HasBorderLoaded) { WaitFor("Kingdom.HasBorderLoaded=false"); return; }
            if (managers.world == null) { WaitFor("World unavailable"); return; }
            if (managers.world.gameLayer == null) { WaitFor("World.gameLayer unavailable"); return; }
            if (managers.payables == null) { WaitFor("Payables unavailable"); return; }
            if (IslandSaveData.isSavingGame) { WaitFor("IslandSaveData.isSavingGame=true"); return; }
            // 既有 Playing prepare 事件窗口内做一次保存绑定双读捕获（之后只沿同绑定复核，不重捕）。
            PalaceSaveBindingSource.NotePlayingPrepare();
            if (_paidPreparationGame != managers.game.Pointer)
            {
                PalacePaidPreparationOutcome preparation = PalacePaidResponsibility.PrepareAtPlaying(managers.game, managers.kingdom);
                if (preparation == PalacePaidPreparationOutcome.WaitingForOwner)
                {
                    WaitFor("paid preparation not ready");
                    return;
                }
                _paidPreparationGame = managers.game.Pointer; // One explicit attempt; terminal faults are never retried, never CanPay IO.
                if (managers.game.currentLand == PalaceLand12Runtime.LandIndex && !PalaceIslandTravel.HasActiveJourney
                    && managers.kingdom.playerOne != null
                    && PalacePaidResponsibility.TryGetPreparedHandle(managers.game, managers.kingdom, managers.kingdom.playerOne, out var coldPaid, out _)
                    && coldPaid.Stage == PalacePaidStage.DepartureClaimed)
                {
                    bool entered = PalacePaidResponsibility.ObserveExactColdArrival(coldPaid, managers.game, out string why);
                    PalaceVisibleEntry.Log("cold actual Pop/Playing entry confirmed=" + entered + "; " + why);
                }
            }
            if (PalaceLand12Runtime.Enabled && managers.game.currentLand != PalaceLand12Runtime.LandIndex
                && !PalaceLand12Runtime.CanEnterPrepared(managers.game, out _))
            {
                if (PalaceIslandTravel.HasActiveJourney) { WaitFor("config awaits travel handoff"); return; }
                // The destination witness now depends on the existing prepared file table. Wait
                // inside this bounded Playing window before consuming the one-shot attempt.
                if (!PalaceLand12Runtime.Ready) { WaitFor("config generation/file table not ready"); return; }
                var currentGlobal = GlobalSaveData.loaded;
                var currentCampaign = CampaignSaveData.current;
                if (currentGlobal == null || currentCampaign == null
                    || !PalaceTripContext.TryBinding(currentGlobal, currentCampaign, out _, out _, out _))
                { WaitFor("config campaign identity unavailable"); return; }
                if (_configPreparationGame != managers.game.Pointer)
                {
                    _configPreparationGame = managers.game.Pointer; // One allocation attempt after readiness, never retry failures.
                    bool prepared = PalaceLand12Runtime.PrepareForEntry(managers.game, out string configReason);
                    PalaceVisibleEntry.Log("entry config preparation=" + prepared + "; " + configReason);
                }
            }
            Transform layer = managers.world.gameLayer;
            if (NetworkPostbox.Instance == null) { WaitFor("NetworkPostbox unavailable"); return; }
            if (_attemptKingdom == _pending.Pointer && _attemptLayer == layer.Pointer)
            { _pending = null; return; } // failed or retired in this same world: no automatic rebuild
            if (!TryAnchor(_pending, layer, managers, out Vector3 position, out SpriteRenderer reference, out string reason))
            { _needsPlot = reason == "no independent shop plot within intact borders"; WaitFor("anchor: " + reason); return; }
            _needsPlot = false;
            Kingdom target = _pending;
            _awaitPlaying = false;
            _pending = null;
            _attemptKingdom = target.Pointer;
            _attemptLayer = layer.Pointer;
            Create(target, layer, position, reference);
        }
        catch (Exception e)
        {
            _awaitPlaying = false;
            _pending = null;
            PalaceVisibleEntry.Log("creation/lifecycle failed; no retry in this world: " + e);
            Clear("fault");
        }
    }

    private bool Near(Player player)
        => player != null && _entry != null && Math.Abs(player.transform.position.x - _entry.transform.position.x) <= 3.5f;

    private void OnGUI()
    {
        if (_entry == null || !_entry.activeInHierarchy || _labelFaulted
            || Event.current == null || Event.current.type != EventType.Repaint) return;
        Color savedColor = GUI.color, savedContent = GUI.contentColor;
        Matrix4x4 savedMatrix = GUI.matrix;
        int savedDepth = GUI.depth;
        bool savedEnabled = GUI.enabled, savedChanged = GUI.changed;
        try
        {
            // Existing primary viewport, as used by DeadlandsFollowerCapture. No camera/font/world scan.
            if (!CameraMarshaller.InstExists) return;
            CameraMarshaller marshaller = CameraMarshaller.Inst;
            MainCamera viewport = marshaller != null ? marshaller.GetCamera(0) : null;
            Camera camera = viewport != null ? viewport._camera : null;
            if (camera == null || !camera.isActiveAndEnabled) return;
            Vector3 point = camera.WorldToScreenPoint(_entry.transform.position + new Vector3(0f, _labelHeight, 0f));
            Rect view = camera.pixelRect;
            if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z) || point.z <= 0f
                || point.x < view.xMin || point.x > view.xMax || point.y < view.yMin || point.y > view.yMax) return;
            if (_labelStyle == null)
            {
                // Same native skin/font fallback chain as the working PopulationHud Chinese labels.
                // Do not call CreateDynamicFontFromOSFont: this IL2CPP build strips that method.
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Normal, alignment = TextAnchor.LowerCenter,
                    wordWrap = false, richText = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
                };
                _labelStyle.normal.textColor = Color.white;
            }
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.75f, 2f);
            _labelStyle.fontSize = Mathf.RoundToInt(18f * scale);
            float width = 360f * scale, height = 60f * scale;
            Rect rect = new Rect(point.x - width * 0.5f, Screen.height - point.y - height, width, height);
            GUI.matrix = Matrix4x4.identity;
            GUI.enabled = true;
            GUI.depth = -10;
            GUI.color = Color.white;
            GUI.contentColor = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), _labelText, _labelStyle);
            GUI.contentColor = new Color(1f, 0.94f, 0.72f, 1f);
            GUI.Label(rect, _labelText, _labelStyle);
            if (!_labelDrawLogged)
            {
                _labelDrawLogged = true;
                PalaceVisibleEntry.Log("world label drawn: native GUI.skin font; primary viewport 0; screen=" + point);
            }
        }
        catch (Exception e)
        {
            _labelFaulted = true;
            PalaceVisibleEntry.Log("world label failed; disabled for this entrance (no font fallback): " + e);
        }
        finally
        {
            GUI.color = savedColor; GUI.contentColor = savedContent; GUI.matrix = savedMatrix;
            GUI.depth = savedDepth; GUI.enabled = savedEnabled; GUI.changed = savedChanged;
        }
    }

    [HideFromIl2Cpp]
    private static bool TryAnchor(Kingdom kingdom, Transform layer, Managers managers,
        out Vector3 position, out SpriteRenderer reference, out string reason)
    {
        position = Vector3.zero; reference = null; reason = "castle/campfire anchor";
        Transform anchor = null;
        if (kingdom.castle != null && kingdom.castle.gameObject.activeInHierarchy
            && kingdom.castle.transform.IsChildOf(layer)) anchor = kingdom.castle.transform;
        else if (kingdom.campfire != null && kingdom.campfire.gameObject.activeInHierarchy
            && kingdom.campfire.transform.IsChildOf(layer))
        { anchor = kingdom.campfire.transform; }
        var all = managers.payables.AllPayables;
        // Same bounded native list used by existing shops; only during the ready handoff.
        for (int i = 0; all != null && i < Math.Min(all.Length, 4096); i++)
        {
            Payable payable = all[i];
            if (payable == null || !payable.isActiveAndEnabled || !payable.transform.IsChildOf(layer)) continue;
            if (anchor == null && (payable.gameObject.name == "Castle0_greece(Clone)" || payable.gameObject.name == "Castle0_greece")
                && payable.TryCast<PayableUpgrade>() != null) anchor = payable.transform;
            if (reference != null) continue;
            PayableShop shop = payable.TryCast<PayableShop>();
            if (shop == null) continue;
            ShopTag tag = shop.GetComponent<ShopTag>();
            if (tag == null || (tag.type != PayableShop.ShopType.Bow && tag.type != PayableShop.ShopType.Hammer
                && tag.type != PayableShop.ShopType.Scythe)) continue;
            SpriteRenderer body = shop.GetComponent<SpriteRenderer>();
            if (body != null && body.enabled && body.sprite != null && Math.Abs(body.sprite.pivot.y) <= 0.001f)
                reference = body;
        }
        if (anchor == null) return false;
        if (reference == null) reference = anchor.GetComponent<SpriteRenderer>();
        if (reference == null || reference.sprite == null) { reason = "native visual reference"; return false; }
        Level level = managers.level;
        BoxCollider2D ground = level != null ? level.GroundCollider : null;
        if (ground == null || !ground.enabled || !ground.gameObject.activeInHierarchy
            || ground.gameObject.scene.handle != layer.gameObject.scene.handle)
        { reason = "ground collider"; return false; }
        Bounds bounds = ground.bounds;
        Sprite art = PalaceEntryArt.GetSprite();
        float y = bounds.max.y;
        float halfWidth = art.bounds.size.x * Math.Abs(layer.lossyScale.x) * 0.5f;
        float left = Math.Max(bounds.min.x, kingdom.GetBorderSideIntact(Side.Left));
        float right = Math.Min(bounds.max.x, kingdom.GetBorderSideIntact(Side.Right));
        if (!Finite(y) || !Finite(halfWidth) || halfWidth <= 0f || !Finite(left) || !Finite(right)
            || !Finite(bounds.min.x) || !Finite(bounds.max.x) || bounds.size.x <= 0f)
        { reason = "ground/visual bounds"; return false; }
        if (!HeroShopPlacementNative.Find(managers.payables, left, right, halfWidth, out float x))
        { reason = "no independent shop plot within intact borders"; return false; }
        position = new Vector3(x, y, reference.transform.position.z);
        reason = "ready; independent native-exclusion plot";
        return true;
    }

    private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);

    [HideFromIl2Cpp]
    private void Create(Kingdom kingdom, Transform layer, Vector3 position, SpriteRenderer reference)
    {
        _kingdom = kingdom; _layer = layer;
        _entry = new GameObject("KEM_PalaceVisibleEntry");
        _entry.SetActive(false);
        _entry.transform.SetParent(layer, false);
        _entry.transform.position = position;
        SpriteRenderer renderer = _entry.AddComponent<SpriteRenderer>();
        renderer.sprite = PalaceEntryArt.GetSprite();
        renderer.sortingLayerID = reference.sortingLayerID;
        renderer.sortingOrder = reference.sortingOrder + 1;
        renderer.sharedMaterial = reference.sharedMaterial;
        renderer.color = Color.white;
        _entry.AddComponent<CRPCStamp>();
        _owner = _entry.AddComponent<PalaceVisibleEntryOwner>();
        _owner.Initialize();
        _payable = _entry.AddComponent<PayableComponent>();
        _payable.forceBlockPayment = true;
        _payable.Init(_owner.Cast<IPayableComponentOwner>());
        _payable.Price = 10;
        _payable.Currency = CurrencyType.Coins;
        _payable.priceIncrease = 0;
        _payable.indicatorSpacing = 0.4f;
        _payable.indicatorOffset = Vector2.zero;
        _payable.repositionInSplitScreen = false;
        _payable.playerPayPointOffset = Vector2.zero;
        _payable.playerPayDistance = 1f;
        _payable.payablePlacementExclusionOffset = Vector2.zero;
        _payable.payablePlacementExclusionDistance = renderer.sprite.bounds.size.x * Math.Abs(layer.lossyScale.x) * 0.5f;
        _payable.glowOnSelect = false;
        _payable.needsNewNetId = false;
        var owner = _owner.Cast<IPayableComponentOwner>();
        if (owner.OnPay == null || owner.CanPay(null)) throw new InvalidOperationException("owner interface mismatch");
        LockIndicator.LockReason lockReason = LockIndicator.LockReason.Invalid;
        if (owner.IsLocked(null, out lockReason) || lockReason != LockIndicator.LockReason.NotLocked)
            throw new InvalidOperationException("owner lock ABI mismatch");
        _postbox = NetworkPostbox.Instance;
        _header = _postbox.RegisterObject(_entry, CRPCType.SemiStatic);
        if (_header == null || _header.NetID == 0 || _payable.parentHeaderRef == null
            || _payable.parentHeaderRef.Pointer != _header.Pointer)
            throw new InvalidOperationException("native payable registration failed");

        // The native payable remains in the world; OnGUI projects only this entrance's label.
        _labelHeight = Math.Max(1.8f, renderer.sprite.bounds.size.y * Math.Abs(layer.lossyScale.y) + 0.5f);
        _labelText = "天朝入口";
        _labelStyle = null;
        _labelFaulted = false;
        _labelDrawLogged = false;
        _near = false;
        _entry.SetActive(true);
        _owner.BindPayment(_payable);
        _payable.forceBlockPayment = !_owner.PaymentBound;
        PalaceVisibleEntry.Log("visible at " + position + "; native-exclusion plot; halfWidth="
            + _payable.payablePlacementExclusionDistance + "; native payable registered; price=10; private payment bound=" + _owner.PaymentBound);
    }

    [HideFromIl2Cpp]
    internal bool TryGetCurrentEntry(out PalaceVisibleEntryOwner owner, out PayableComponent payable)
    {
        owner = _owner; payable = _payable;
        return _entry != null && _entry.activeInHierarchy && owner != null && payable != null;
    }

    [HideFromIl2Cpp]
    internal void OwnerDisabled(PalaceVisibleEntryOwner owner)
    {
        if (_owner != null && owner != null && _owner.Pointer == owner.Pointer) Clear("entry disabled");
    }

    [HideFromIl2Cpp]
    private void Clear(string reason)
    {
        GameObject entry = _entry;
        PalaceVisibleEntryOwner owner = _owner;
        if (owner != null) owner.RetirePayment();
        NetworkPostbox postbox = _postbox;
        CRPCHeader header = _header;
        _entry = null; _owner = null; _payable = null; _postbox = null; _header = null;
        _kingdom = null; _layer = null; _labelStyle = null; _needsPlot = false;
        try
        {
            if (entry != null && postbox != null && header != null
                && postbox.MasterSemiCRPCHLookup.TryGetValue(entry, out var found)
                && found != null && found.Pointer == header.Pointer)
                postbox.DeregisterObject(entry, header.NetID, CRPCType.SemiStatic);
        }
        catch (Exception e) { PalaceVisibleEntry.Log("own registration cleanup failed: " + e); }
        finally
        {
            if (entry != null) { entry.SetActive(false); UnityEngine.Object.Destroy(entry); }
            PalaceEntryArt.Release();
        }
        if (entry != null) PalaceVisibleEntry.Log("removed: " + reason);
    }

    private void OnDestroy() { CancelHandoff("lifecycle host destroyed"); Clear("lifecycle host destroyed"); UnbindBorders(); }
}

internal sealed class PalaceVisibleEntryOwner : MonoBehaviour
{
    private Il2CppSystem.Action<Player> _onPay;
    private PalaceEntryPayment _payment;
    public PalaceVisibleEntryOwner(IntPtr pointer) : base(pointer) { }
    [HideFromIl2Cpp] internal void Initialize() { _onPay = (Il2CppSystem.Action<Player>)DispatchPayment; }
    [HideFromIl2Cpp] internal void BindPayment(PayableComponent payable)
    {
        if (PalaceRuntimeProfile.Capability("PALACE_ENTRY_PAYMENT_PRIVATE") && PalaceLand12Runtime.Enabled)
            _payment = new PalaceEntryPayment(this, payable, PalaceIslandTravel.Instance);
    }
    [HideFromIl2Cpp] internal bool PaymentBound => _payment != null && _payment.SubscriptionReady;
    [HideFromIl2Cpp] internal bool PaymentConsumed => _payment != null && _payment.IsConsumed;
    [HideFromIl2Cpp] internal string PaymentPrompt
    {
        get
        {
            var m = Managers.Inst;
            if (m != null && m.game != null && m.kingdom != null && m.kingdom.playerOne != null
                && PalacePaidResponsibility.TryGetPreparedHandle(m.game,m.kingdom,m.kingdom.playerOne,out var paid,out _)
                && (paid.Stage == PalacePaidStage.SourceCaptured || paid.Stage == PalacePaidStage.Resumable))
                return "已付十金币 · 按 ↓ 继续入宫";
            return _payment == null || !_payment.SubscriptionReady ? "暂未开放 · 不收取金币"
                : _payment.IsConsumed ? "正在前往天朝" : "十金币 · 进入天朝";
        }
    }
    [HideFromIl2Cpp] internal void PaymentTick() { _payment?.Tick(); }
    [HideFromIl2Cpp] internal void RetirePayment() { _payment?.Retire(); _payment = null; }
    [HideFromIl2Cpp] private void DispatchPayment(Player player)
    {
        if (_payment != null) _payment.OnPay(player);
        else PalaceVisibleEntry.Log("unexpected OnPay refused; no action");
    }
    public Il2CppSystem.Action<Player> OnPay => _onPay;
    public bool CanPay(Player player) => _payment != null && _payment.CanPay(player);
    // Same verified 32-bit enum output bridge as the baseline shops; CanPay and forceBlockPayment prohibit payment.
    public bool IsLocked(Player player, IntPtr reason)
        => HeroShopOwnerInterop.WriteUnlocked(reason, (int)LockIndicator.LockReason.NotLocked);
    public void OnDisable() => PalaceVisibleEntry.OwnerDisabled(this);
}
