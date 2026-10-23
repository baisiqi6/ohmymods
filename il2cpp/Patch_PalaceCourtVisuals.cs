using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 原生 land12 场景的自有庭院 sprite（仅画面：不付款、不回岛、无状态动画/新交互）。
/// 激活 = PALACE_COURT_PRIVATE=1 且 PalaceLand12Runtime.Enabled（private roots 精确）；否则零 hook、零副作用。
/// 素材 = Assembly 嵌入资源 "KingdomEnhancedMod.KEM_PalaceCourt/" + relative（九张 PNG 由 Root 原字节嵌入）；
/// 不依赖旧 test 根或外部文件路径，不生成/不重编码任何图片。
/// 生命周期沿 PalaceVisibleEntry：Kingdom.Init/OnLevelLoaded arm 单一加载 epoch；Game.Update 实际 Playing 后
/// 开 20s 有界 readiness（每 0.25s）；scoped 离线 land12 + 锚点/地面范围 + 当前玩家参照全部满足才创建
/// 唯一自有 root；失败/缺参考/越界 fail-closed，一次日志，不自动重试分配；同一 epoch 只 attemptCreate 一次。
/// 清理：Game.PreExitCampaign / Kingdom 新加载 arm / root 父级或 gameLayer 漂移 → 只销毁自有 root/sprite/texture，
/// 借用材质/精灵/游戏对象不销毁；不隐藏/改写/销毁任何原生 renderer，不新增周期森林治理。
/// </summary>
internal static class PalaceCourtVisuals
{
    private static bool _attempted;
    private static PalaceCourtScene _scene;
    private static PalaceCourtAssets _assets;
    internal static Transform ReturnDoor { get; private set; }
    internal static void SetReturnDoor(Transform door) => ReturnDoor = door;

    internal static void InstallOnce(Harmony harmony)
    {
        if (_attempted) return;
        if (!PalaceRuntimeProfile.Capability("PALACE_COURT_PRIVATE") || !PalaceLand12Runtime.Enabled) return;
        _attempted = true;
        try
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            PalaceNativeMetrics.InstallOnce(harmony);
        PalaceNativeBackground.InstallOnce(harmony);
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(PalaceCourtScene)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(PalaceCourtScene));
            var host = new GameObject("KEM_PalaceCourt_Lifecycle");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _scene = host.AddComponent<PalaceCourtScene>();
            var postfix = new HarmonyMethod(typeof(PalaceCourtVisuals), nameof(WorldReady));
            harmony.Patch(AccessTools.Method(typeof(Kingdom), nameof(Kingdom.Init)), postfix: postfix);
            harmony.Patch(AccessTools.Method(typeof(Kingdom), nameof(Kingdom.OnLevelLoaded)), postfix: postfix);
            // Game.Update/PreExitCampaign 挂点与 PalaceVisibleEntry 相同（本 interop 已验证可解析）。
            harmony.Patch(AccessTools.Method(typeof(Game), nameof(Game.Update)),
                postfix: new HarmonyMethod(typeof(PalaceCourtVisuals), nameof(GameplayFrame)));
            harmony.Patch(AccessTools.Method(typeof(Game), nameof(Game.PreExitCampaign)),
                postfix: new HarmonyMethod(typeof(PalaceCourtVisuals), nameof(CampaignEnded)));
            Log("installed; private land12 court visuals only; disabled means no hooks");
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
        PalaceAdultBodyMetrics.Arm(__instance);
        PalaceNativeMetrics.Observe(__instance);
        if (!ReferenceEquals(_scene, null)) _scene.ObservePlaying(__instance);
    }

    private static void CampaignEnded()
    {
        try { if (_scene != null) _scene.EndCampaign(); }
        catch (Exception e) { Log("campaign cleanup fault: " + e); }
    }

    internal static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[PalaceCourt] " + message); }
        catch (Exception) { }
    }

    internal static void SetAssets(PalaceCourtAssets assets) => _assets = assets;

    /// <summary>付前只读宿主证据（目的地创建/ReturnDoor 发布的持续宿主）：GameObject 活跃且组件
    /// <c>isActiveAndEnabled</c> 真实启用（禁用组件不得被当作 ready 放行）。</summary>
    internal static bool HostReady
    {
        get
        {
            try
            {
                PalaceCourtScene scene = _scene;
                return scene != null && scene.gameObject != null && scene.gameObject.activeInHierarchy
                    && scene.isActiveAndEnabled;
            }
            catch (Exception) { return false; }
        }
    }

    internal static PalaceCourtAssets TakeAssets()
    {
        PalaceCourtAssets assets = _assets;
        _assets = null;
        return assets;
    }
}

/// <summary>加载 epoch 状态机：arm → Playing → 有界 readiness → 唯一 root；清理不重建。</summary>
internal sealed class PalaceCourtScene : MonoBehaviour
{
    // 与 PalaceLand12Runtime/PalaceIslandTravel 相同的 private root（RootsMatch 已由 PalaceLand12Runtime.Enabled 把关）。
    private const float WindowSeconds = 20f;
    private const float ProbeSeconds = 0.25f;

    private Kingdom _pending, _kingdom;
    private Transform _layer;
    private GameObject _root;
    private PalaceCourtContent _content;
    private PalaceWorldExpansion _worldExpansion;
    private bool _actorsR7; // private actors-only owner, no legacy architecture/stage
    private PalaceScrollScene _scroll;
    private PalaceDepthScene _depth; // sole R5 owner; mutually exclusive with R4/legacy
    private bool _cleanupRetained;
    private bool _created;
    private float _deadline, _nextProbe;
    private bool _awaitPlaying;
    private int _waitLogs;
    private string _waiting = "scene not ready";
    private IntPtr _attemptKingdom, _attemptLayer;

    public PalaceCourtScene(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void Arm(Kingdom kingdom)
    {
        if (_cleanupRetained) { CancelHandoff("R4 cleanup owner retained; no new creation"); return; }
        if (NetworkBigBoss.IsOnline) { CancelHandoff("online kingdom lifecycle"); Cleanup("online kingdom lifecycle"); return; }
        if (kingdom == null) return;
        if (_pending != null && _pending.Pointer == kingdom.Pointer) return;
        Managers managers = Managers.Inst;
        if (_created && _kingdom != null && _kingdom.Pointer == kingdom.Pointer && _layer != null
            && managers != null && managers.world != null && managers.world.gameLayer != null
            && managers.world.gameLayer.Pointer == _layer.Pointer) return;
        if (_created) Cleanup("kingdom lifecycle changed");
        if (_cleanupRetained) { CancelHandoff("R4 cleanup owner retained during handoff; no re-arm"); return; }
        _pending = kingdom;
        _awaitPlaying = true;
        _waitLogs = 0;
        PalaceCourtVisuals.Log("kingdom armed; readiness window starts only at actual Playing land12");
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
                if (_waiting != "game-state") { _waiting = "game-state"; PalaceCourtVisuals.Log("handoff waiting: game-state=" + state); }
                return;
            }
            Managers managers = Managers.Inst;
            if (NetworkBigBoss.IsOnline || managers == null || managers.game == null
                || managers.game.Pointer != game.Pointer || managers.kingdom == null
                || managers.kingdom.Pointer != _pending.Pointer)
            { CancelHandoff("Playing owner differs from armed kingdom/game"); return; }
            _awaitPlaying = false; // 恰好一次交接；后续帧不再延长 deadline
            _deadline = Time.unscaledTime + WindowSeconds;
            _nextProbe = 0f;
            _waitLogs = 0;
            _waiting = "Playing readiness";
            PalaceCourtVisuals.Log("actual Playing handoff; starting bounded " + WindowSeconds + "s readiness window");
        }
        catch (Exception e) { CancelHandoff("Playing handoff fault: " + e.GetType().Name); }
    }

    [HideFromIl2Cpp]
    internal void EndCampaign()
    {
        CancelHandoff("campaign exit");
        Cleanup("campaign exit");
        _attemptKingdom = IntPtr.Zero;
        _attemptLayer = IntPtr.Zero;
    }

    [HideFromIl2Cpp]
    private void CancelHandoff(string reason)
    {
        bool hadPending = _awaitPlaying || _pending != null;
        _awaitPlaying = false;
        _pending = null;
        if (hadPending) PalaceCourtVisuals.Log("handoff ended: " + reason);
    }

    [HideFromIl2Cpp]
    private void WaitFor(string reason)
    {
        if (_waiting == reason) return;
        _waiting = reason;
        if (_waitLogs++ < 12) PalaceCourtVisuals.Log("readiness waiting: " + reason);
    }

    private void Update()
    {
        try
        {
            if (_created) { KeepAlive(); return; }
            if (_awaitPlaying || _pending == null) return;
            float now = Time.unscaledTime;
            if (now > _deadline)
            {
                PalaceCourtVisuals.Log("readiness window ended without creation (fail-closed, no retry): " + _waiting);
                MarkAttempt(_pending);
                _pending = null;
                return;
            }
            if (now < _nextProbe) return;
            _nextProbe = now + ProbeSeconds;
            Probe();
        }
        catch (Exception e)
        {
            _awaitPlaying = false;
            _pending = null;
            PalaceCourtVisuals.Log("probe fault; no rebuild in this world: " + e);
            Cleanup("probe fault");
        }
    }

    [HideFromIl2Cpp]
    private void KeepAlive()
    {
        if (_root == null) { Cleanup("court root destroyed externally"); return; }
        Managers m = Managers.Inst;
        if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth || m == null || m.game == null
            || m.world == null || m.kingdom == null || _kingdom == null || _layer == null
            || m.kingdom.Pointer != _kingdom.Pointer || m.world.gameLayer == null
            || m.world.gameLayer.Pointer != _layer.Pointer
            || (m.game.state != Game.State.Playing && m.game.state != Game.State.Menu))
        { Cleanup("world ended"); return; }
        Transform parent = _root.transform.parent;
        if (parent == null || parent.Pointer != _layer.Pointer) { Cleanup("court root parent/layer changed"); return; }
        if (m.game.state == Game.State.Playing)
        {
            if (_depth != null && !_depth.Tick(m.game, out string depthReason))
            { Cleanup("R5 depth tick stopped: " + depthReason); return; }
            _content?.Tick(Time.deltaTime, true);
            _worldExpansion?.Tick(Time.deltaTime, true);
        }
    }

    [HideFromIl2Cpp]
    private void Probe()
    {
        Managers managers = Managers.Inst;
        if (NetworkBigBoss.IsOnline) { CancelHandoff("online world"); return; }
        if (!NetworkBigBoss.HasWorldAuth) { WaitFor("world authority unavailable"); return; }
        if (managers == null) { WaitFor("Managers unavailable"); return; }
        if (managers.game == null) { WaitFor("Game unavailable"); return; }
        if (managers.game.state != Game.State.Playing) { WaitFor("game-state=" + managers.game.state); return; }
        if (managers.kingdom == null) { WaitFor("Kingdom unavailable"); return; }
        if (managers.kingdom.Pointer != _pending.Pointer) { CancelHandoff("ready kingdom replaced"); return; }
        Kingdom kingdom = _pending;
        // Native BG None now completes OnLevelLoaded normally (cold and 12→8→12 verified).
        // Remove the old R4 readiness bypass: every environment must await native completion; never write this flag.
        if (!kingdom.HasBorderLoaded)
        { WaitFor("Kingdom.HasBorderLoaded=false"); return; }
        if (managers.world == null) { WaitFor("World unavailable"); return; }
        if (managers.world.gameLayer == null) { WaitFor("World.gameLayer unavailable"); return; }
        if (managers.level == null) { WaitFor("Level unavailable"); return; }
        if (managers.payables == null) { WaitFor("Payables unavailable"); return; }
        if (IslandSaveData.isSavingGame) { WaitFor("IslandSaveData.isSavingGame=true"); return; }
        Transform layer = managers.world.gameLayer;
        if (_attemptKingdom == kingdom.Pointer && _attemptLayer == layer.Pointer)
        { _pending = null; return; } // 本 epoch 已尝试：绝不自动重建
        if (!TryScope(managers.game, out _, out _)) { WaitFor("private land12 scope not ready"); return; }
        Vector3 scale = layer.lossyScale;
        Quaternion rotation = layer.rotation;
        if (!Finite(scale.x) || !Finite(scale.y) || !Finite(scale.z)
            || Math.Abs(scale.x - 1f) > 0.0001f || Math.Abs(scale.y - 1f) > 0.0001f || Math.Abs(scale.z - 1f) > 0.0001f
            || !Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w)
            || Math.Abs(rotation.x) > 0.0001f || Math.Abs(rotation.y) > 0.0001f || Math.Abs(rotation.z) > 0.0001f
            || Math.Abs(Math.Abs(rotation.w) - 1f) > 0.0001f)
        {
            PalaceCourtVisuals.Log("court layer transform not identity; world footprint cannot be proven; creation stopped");
            MarkAttempt(kingdom); _pending = null; return;
        }
        if (!TryAnchor(kingdom, layer, out float centerX, out float groundY, out string anchorSource,
            out string detail, out bool fatal))
        {
            if (fatal) { PalaceCourtVisuals.Log("anchor refused (fail-closed, one log, no retry): " + detail); MarkAttempt(kingdom); _pending = null; return; }
            WaitFor("anchor: " + detail); return;
        }
        if (!TryReference(kingdom, layer, out SpriteRenderer reference, out string referenceSource))
        { WaitFor("no visible player/steed sprite renderer"); return; }
        PalaceCourtVisuals.Log("native readiness ok: scoped offline land12 + agent folder; anchor=" + anchorSource
            + "; " + detail + "; ref=" + referenceSource + " go=" + GoName(reference));
        _awaitPlaying = false;
        _pending = null;
        _attemptKingdom = kingdom.Pointer;
        _attemptLayer = layer.Pointer;
        Create(kingdom, layer, new Vector3(centerX - PalaceCourtAssets.HalfCourt, groundY,
            reference.transform.position.z), reference, anchorSource);
    }

    /// <summary>scoped 离线授权：非 challengeGreek、currentCampaign 精确、campaign/game 双 12、agent 私有目录双读一致。</summary>
    [HideFromIl2Cpp]
    private bool TryScope(Game game, out GlobalSaveData global, out CampaignSaveData campaign)
    {
        global = null; campaign = null;
        try
        {
            if (game == null || Filer.agent == null) return false;
            GlobalSaveData loaded = GlobalSaveData.loaded;
            CampaignSaveData current = CampaignSaveData.current;
            if (loaded == null || loaded.InChallenge || current == null) return false;
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            campaign = loaded.GetCurrentCampaign();
            if (campaign == null || campaign.CurrentLand != PalaceLand12Runtime.LandIndex
                || game.currentLand != PalaceLand12Runtime.LandIndex) return false;
            var first = Filer.agent as Il2CppObjectBase;
            var agent = first != null && first.Pointer != IntPtr.Zero ? first.TryCast<Filer.DotNetAgent>() : null;
            if (agent == null || agent.Pointer != first.Pointer || !PalaceSaveBindingSource.AgentMatchesCurrent(agent, out _)) return false;
            var second = Filer.agent as Il2CppObjectBase;
            GlobalSaveData again = GlobalSaveData.loaded;
            CampaignSaveData againCampaign = again != null ? again.GetCurrentCampaign() : null;
            if (second == null || second.Pointer != first.Pointer || again == null || again.Pointer != loaded.Pointer
                || againCampaign == null || campaign.Pointer != current.Pointer || againCampaign.Pointer != campaign.Pointer)
                return false;
            global = loaded;
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>锚点与地表：castle/campfire/uniqueFoundation 只决定 centerX（campY/castleY 仅日志对比——旧实现误用 campfire.transform.y，
    /// 实测为 0）；groundY 一律取自当前 Level.GroundCollider：exact scene/enabled/active/nontrigger/axisaligned，向
    /// (centerX, bounds.max.y+2) 调 exact collider 的 ClosestPoint，仅接受有限且贴近 flat 顶面的 surfacePoint；
    /// 不满足即等待/fail-closed，不做任何猜测平移（不用玩家 y，不 fallback）。</summary>
    [HideFromIl2Cpp]
    private static bool TryAnchor(Kingdom kingdom, Transform layer, out float centerX, out float groundY,
        out string source, out string detail, out bool fatal)
    {
        centerX = 0f; groundY = 0f; source = null; detail = null; fatal = false;
        float castleY = float.NaN, campY = float.NaN; // 旧实现实际用过的 anchor y；现在只读记录，用于日志对比
        try
        {
            Castle castle = kingdom.castle;
            if (castle != null && castle.gameObject.activeInHierarchy && castle.transform.IsChildOf(layer))
            {
                Vector3 p = castle.transform.position;
                if (Finite(p.x)) { castleY = p.y; centerX = p.x; source = "kingdom-castle"; }
            }
        }
        catch (Exception e) { detail = "castle read threw " + e.GetType().Name; }
        try
        {
            Campfire campfire = kingdom.campfire;
            if (campfire != null && campfire.transform != null) campY = campfire.transform.position.y;
            if (source == null && campfire != null && campfire.gameObject.activeInHierarchy
                && campfire.transform.IsChildOf(layer))
            {
                Vector3 p = campfire.transform.position;
                if (Finite(p.x)) { centerX = p.x; source = "kingdom-campfire(x only; campY diagnostic)"; }
            }
        }
        catch (Exception e) { detail = detail ?? ("campfire read threw " + e.GetType().Name); }
        if (source == null)
        {
            string foundationDetail = null;
            try
            {
                var all = Managers.Inst != null && Managers.Inst.payables != null ? Managers.Inst.payables.AllPayables : null;
                int matches = 0;
                float foundX = 0f;
                for (int i = 0; all != null && i < Math.Min(all.Length, 4096); i++)
                {
                    Payable payable = all[i];
                    if (payable == null || !payable.isActiveAndEnabled) continue;
                    string name;
                    try { name = payable.gameObject.name; } catch (Exception) { continue; }
                    if (name != "Castle0_greece(Clone)" && name != "Castle0_greece") continue;
                    if (payable.TryCast<PayableUpgrade>() == null) continue;
                    Transform root = payable.transform;
                    if (root == null || !root.IsChildOf(layer)) continue;
                    Vector3 p = root.position;
                    if (!Finite(p.x)) { foundationDetail = "foundation non-finite"; continue; }
                    matches++; foundX = p.x;
                    if (matches > 1) break;
                }
                if (matches == 1) { centerX = foundX; source = "castle0-greece-foundation(x only)"; }
                else foundationDetail = matches > 1 ? "foundation duplicate" : "no stable anchor yet";
            }
            catch (Exception e) { foundationDetail = "foundation read threw " + e.GetType().Name; }
            if (source == null) { detail = foundationDetail ?? "no stable anchor yet"; return false; }
        }
        try
        {
            Level level = Managers.Inst != null ? Managers.Inst.level : null;
            if (PalaceCourtArea.Enabled)
            {
                if (PalaceCourtArea.TryGetSpan(level, out float courtLeft, out float courtRight, out string areaReason))
                {
                    centerX = PalaceCourtArea.RenderAxis(courtLeft, courtRight);
                    source = PalaceCourtArea.DedicatedWholeEnabled ? "schema2-dedicated-island-gate-axis" : "native-owned-clean-court-span";
                }
                else PalaceCourtVisuals.Log("new court span unavailable; base gateway retained only: " + areaReason);
            }
            BoxCollider2D ground = level != null ? level.GroundCollider : null;
            if (ground == null || !ground.enabled || ground.gameObject == null
                || !ground.gameObject.activeInHierarchy
                || ground.gameObject.scene.handle != layer.gameObject.scene.handle)
            { detail = "ground collider unavailable"; return false; }
            if (ground.isTrigger)
            { fatal = true; detail = "ground collider is trigger; geometry contract broken"; return false; }
            Transform groundTransform = ground.transform;
            Quaternion rotation = groundTransform != null ? groundTransform.rotation : Quaternion.identity;
            const float aligned = 1e-4f;
            if (groundTransform == null
                || Math.Abs(rotation.x) > aligned || Math.Abs(rotation.y) > aligned
                || Math.Abs(rotation.z) > aligned || Math.Abs(Math.Abs(rotation.w) - 1f) > aligned)
            {
                fatal = true;
                detail = "ground collider not axis-aligned rotation=(" + rotation.x.ToString("F4") + ","
                    + rotation.y.ToString("F4") + "," + rotation.z.ToString("F4") + "," + rotation.w.ToString("F4") + ")";
                return false;
            }
            Bounds bounds = ground.bounds;
            if (!Finite(bounds.min.x) || !Finite(bounds.max.x) || !Finite(bounds.size.x) || bounds.size.x <= 0f
                || !Finite(bounds.min.y) || !Finite(bounds.max.y) || !Finite(bounds.size.y) || bounds.size.y <= 0f)
            { detail = "ground bounds non-finite"; return false; }
            float half = PalaceCourtAssets.HalfCourt;
            if (centerX - half < bounds.min.x || centerX + half > bounds.max.x)
            {
                fatal = true;
                detail = "court out of bounds centerX=" + centerX.ToString("F3") + " half=" + half.ToString("F3")
                    + " ground=[minX=" + bounds.min.x.ToString("F2") + " maxX=" + bounds.max.x.ToString("F2") + "]";
                return false;
            }
            Vector2 surface = new Vector2(centerX, bounds.max.y);
            string surfaceText;
            try
            {
                surface = ground.ClosestPoint(new Vector2(centerX, bounds.max.y + 2f));
                float dx = Math.Abs(surface.x - centerX);
                float dy = Math.Abs(surface.y - bounds.max.y);
                if (!Finite(surface.x) || !Finite(surface.y) || dx > 0.5f || dy > 0.5f)
                {
                    detail = "surface probe rejected dx=" + dx.ToString("F3") + " dy=" + dy.ToString("F3")
                        + " point=(" + surface.x.ToString("F3") + "," + surface.y.ToString("F3") + ")";
                    return false; // 等待：不猜 y、不做猜测平移
                }
                groundY = surface.y;
                surfaceText = "actualSurfaceY=" + surface.y.ToString("F3") + " via ClosestPoint(dx=" + dx.ToString("F3")
                    + " dy=" + dy.ToString("F3") + ")";
            }
            catch (Exception e)
            {
                detail = "ClosestPoint fault(" + e.GetType().Name + "); no y assumed";
                return false; // 等待：不 fallback
            }
            detail = "ground=" + ground.name + " pos=" + Vec(groundTransform.position)
                + " scale=" + Vec(groundTransform.lossyScale)
                + " rot=(" + rotation.x.ToString("F3") + "," + rotation.y.ToString("F3") + ","
                + rotation.z.ToString("F3") + "," + rotation.w.ToString("F3") + ")"
                + " bounds=[minX=" + bounds.min.x.ToString("F2") + " maxX=" + bounds.max.x.ToString("F2")
                + " minY=" + bounds.min.y.ToString("F2") + " maxY=" + bounds.max.y.ToString("F2") + "]"
                + " campY=" + F3(campY) + " castleY=" + F3(castleY)
                + "; " + surfaceText + "; groundY=" + groundY.ToString("F3");
            return true;
        }
        catch (Exception e) { detail = "ground bounds read threw " + e.GetType().Name; return false; }
    }

    [HideFromIl2Cpp]
    private static bool TryReference(Kingdom kingdom, Transform layer, out SpriteRenderer reference, out string source)
    {
        reference = null; source = null;
        if (TryPlayerRenderer(kingdom.playerOne, layer, out reference)) { source = "player-one"; return true; }
        if (TryPlayerRenderer(kingdom.playerTwo, layer, out reference)) { source = "player-two"; return true; }
        return false;
    }

    /// <summary>当前本地玩家的可见 sprite 参照（只读）：优先骑乘 steed 的 SpriteFX.Renderer（须核该 steed 的 Rider 就是本玩家），
    /// 其次 Player._steedRenderer、Player._riderRenderer；逐项核 visible/enabled/sprite/scene/parent。
    /// 三者都不可确证时等待——绝不退取任意 child（r1 实测曾借到 Molossian Hound）。</summary>
    [HideFromIl2Cpp]
    private static bool TryPlayerRenderer(Player player, Transform layer, out SpriteRenderer reference)
    {
        reference = null;
        try
        {
            if (player == null || !player.gameObject.activeInHierarchy) return false;
            bool local;
            try { local = player.hasLocalAuthority; } catch (Exception) { local = false; }
            if (!local) return false;
            Transform t = player.transform;
            if (t == null || !t.IsChildOf(layer)) return false;
            // 1) 当前骑乘 steed 的真 sprite（Rider 指针必须就是本玩家）。
            SpriteRenderer steedSprite = null;
            try
            {
                Steed mount = player.steed;
                Player rider = mount != null ? mount.Rider : null;
                if (rider != null && rider.Pointer == player.Pointer)
                {
                    SpriteRendererFX fx = mount.SpriteFX;
                    steedSprite = fx != null ? fx.Renderer : null;
                }
            }
            catch (Exception) { steedSprite = null; }
            if (Usable(steedSprite, layer)) { reference = steedSprite; return true; }
            // 2) Player 自有 steed 渲染器。
            SpriteRenderer steedRenderer = null;
            try { steedRenderer = player._steedRenderer; } catch (Exception) { steedRenderer = null; }
            if (Usable(steedRenderer, layer)) { reference = steedRenderer; return true; }
            // 3) Player 自有 rider 渲染器。
            SpriteRenderer riderRenderer = null;
            try { riderRenderer = player._riderRenderer; } catch (Exception) { riderRenderer = null; }
            if (Usable(riderRenderer, layer)) { reference = riderRenderer; return true; }
            return false; // 未知参照：等待，不借任意 child
        }
        catch (Exception) { reference = null; return false; }
    }

    /// <summary>参照核验：visible/enabled/sprite + GO active + scene 与当前 gameLayer 相同 + 父级链在 gameLayer 内。</summary>
    private static bool Usable(SpriteRenderer renderer, Transform layer)
    {
        try
        {
            if (renderer == null || !renderer.enabled || renderer.sprite == null) return false;
            GameObject go = renderer.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            Transform t = renderer.transform;
            if (t == null || !t.IsChildOf(layer)) return false;
            return go.scene.handle == layer.gameObject.scene.handle;
        }
        catch (Exception) { return false; }
    }

    /// <summary>一次只读 inventory（≤96 节点）：找 forest/curtain/background/startingArea 名称或近锚点的原生幕来源，只记录。</summary>
    [HideFromIl2Cpp]
    private static void LogInventory(Transform layer, float anchorX, float anchorY)
    {
        try
        {
            var pending = new Queue<Transform>();
            pending.Enqueue(layer);
            int visited = 0, renderers = 0, logged = 0;
            while (pending.Count > 0 && visited < 96)
            {
                Transform node = pending.Dequeue();
                visited++;
                int children = 0;
                try { children = node.childCount; } catch (Exception) { }
                for (int i = 0; i < children && pending.Count < 96; i++) pending.Enqueue(node.GetChild(i));
                SpriteRenderer renderer = null;
                try { renderer = node.GetComponent<SpriteRenderer>(); } catch (Exception) { }
                if (renderer == null || renderer.sprite == null) continue;
                renderers++;
                bool near = Math.Abs(node.position.x - anchorX) <= 12f && Math.Abs(node.position.y - anchorY) <= 12f;
                bool named = FocusName(node.name) || FocusName(renderer.sprite.name);
                if ((near || named) && logged < 24)
                {
                    logged++;
                    PalaceCourtVisuals.Log("inventory go=" + node.name + " sprite=" + renderer.sprite.name
                        + " layerID=" + renderer.sortingLayerID + " order=" + renderer.sortingOrder
                        + " z=" + node.position.z.ToString("F3") + " near=" + near + " named=" + named);
                }
            }
            PalaceCourtVisuals.Log("inventory done (read-only, not a repair scan): visited=" + visited
                + " spriteRenderers=" + renderers + " logged=" + logged
                + "; forest occlusion boundary decision left to a later source-generating step");
        }
        catch (Exception e) { PalaceCourtVisuals.Log("inventory fault (read-only; ignored): " + e.GetType().Name); }
    }

    private static bool FocusName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = name.ToLowerInvariant();
        return lower.Contains("forest") || lower.Contains("curtain") || lower.Contains("background")
            || lower.Contains("starting") || lower.Contains("area");
    }

    [HideFromIl2Cpp]
    private void Create(Kingdom kingdom, Transform layer, Vector3 position, SpriteRenderer reference, string anchorSource)
    {
        _kingdom = kingdom; _layer = layer;
        bool r4 = PalaceRuntimeProfile.Capability("PALACE_SCROLL_R4_PRIVATE");
        PalaceCourtAssets assets = r4 ? null : new PalaceCourtAssets();
        PalaceCourtVisuals.SetAssets(assets);
        GameObject root = null;
        PalaceCourtContent content = null;
        PalaceWorldExpansion worldExpansion = null;
        PalaceScrollScene scroll = null;
        PalaceDepthScene depth = null;
        bool r5 = PalaceRuntimeProfile.Capability("PALACE_DEPTH_R5_PRIVATE");
        bool actorsR7 = PalaceRuntimeProfile.Capability("PALACE_ACTORS_R7_PRIVATE");
        _actorsR7 = actorsR7;
        try
        {
            int goLayer = reference.gameObject.layer;
            int layerId = reference.sortingLayerID;
            int baseOrder = reference.sortingOrder;
            Material material = reference.sharedMaterial;
            LogInventory(layer, position.x + PalaceCourtAssets.HalfCourt, position.y);
            PalaceCourtVisuals.Log("borrowed ref=" + reference.gameObject.name + " goLayer=" + goLayer
                + " sortingLayerID=" + layerId + " baseOrder=" + baseOrder
                + " material=" + (material != null ? material.name : "(default)")
                + " z=" + reference.transform.position.z.ToString("F3"));
            root = new GameObject("KEM_PalaceCourt");
            root.SetActive(false);
            root.layer = goLayer;
            root.transform.SetParent(layer, false);
            root.transform.position = position;
            var binding = new CourtBinding
            {
                Assets = assets,
                Parent = root.transform,
                GoLayer = goLayer,
                LayerId = layerId,
                BaseOrder = baseOrder,
                Material = material,
                Reference = reference
            };
            if (actorsR7 && (!r5 || !r4 || !PalaceRuntimeProfile.Capability("PALACE_SORT_DEPTH_R6_PRIVATE")))
                throw new InvalidOperationException("R7 actors require verified complete environment and Native unit-depth scope");
            if (r5 && !r4) throw new InvalidOperationException("R5 requires the complete R4 native binding scope; no legacy fallback");
            int placed = 0;
            if (r4)
            {
                if (!PalaceScrollNativeBinding.Enabled)
                    throw new InvalidOperationException("R4 requested but private terrain/content scope unavailable; no legacy fallback");
                PalaceWorldLayout.RequireDedicatedNativeContent(layer);
                binding.ScrollCalibration = PalaceScrollNativeBinding.Calibrate(binding,
                    position.x + PalaceCourtAssets.HalfCourt, position.y);
                if (r5)
                {
                    if (!PalaceDepthScene.Create(binding, binding.ScrollCalibration, out depth, out string depthReason))
                        throw new InvalidOperationException("R5 depth rejected: " + depthReason);
                    _depth = depth;
                    PalaceCourtVisuals.SetReturnDoor(depth.GateAnchor);
                    placed = 24;
                    PalaceCourtVisuals.Log("R7 environment created; no R4/legacy architecture overlap; screen acceptance pending");
                    if (actorsR7)
                    {
                        placed = 26;
                        PalaceActorsPlan actorPlan = CreateR7ActorsPlan(binding);
                        if (!PalaceWorldExpansion.CreateActors(actorPlan, out worldExpansion, out string actorReason))
                        {
                            if (worldExpansion != null)
                            {
                                _worldExpansion = worldExpansion; _root = root; _created = false; _cleanupRetained = true;
                                PalaceCourtVisuals.SetReturnDoor(null);
                                PalaceCourtVisuals.Log("R7 actor allocation failed with retained owner/outer root; no activation/retry: " + actorReason);
                                return;
                            }
                            PalaceCourtVisuals.Log("R7 actor batch rejected once; known rollback, environment retained without actors: " + actorReason);
                        }
                        else
                        {
                            _worldExpansion = worldExpansion;
                            placed += worldExpansion.PlacedSprites;
                        }
                    }
                }
                else
                {
                    if (!PalaceScrollScene.Create(binding, binding.ScrollCalibration,
                        checked(baseOrder - 3), checked(baseOrder - 2), checked(baseOrder - 2),
                        out scroll, out string scrollReason))
                        throw new InvalidOperationException("R4 scroll rejected: " + scrollReason);
                    _scroll = scroll;
                    PalaceCourtVisuals.SetReturnDoor(scroll.GateAnchor);
                    placed = 3;
                    PalaceCourtVisuals.Log("R4 environment only; three coherent plates, zero parallax; no legacy court/R6/world actors or architecture");
                }
            }
            else
            {
                // Native TileBig palette now owns ground; remove the old overlaid paving compensation.
                for (int i = 0; !PalaceTerrainRuntime.Enabled && i < PalaceCourtAssets.TileCount; i++)
                {
                    placed += binding.Place("ground/stone-bank.png", i * 64f, 204f, CourtStratum.WalkableSurface);
                    placed += binding.Place("ground/brick-surface.png", i * 64f, 204f, CourtStratum.WalkableSurface);
                }
                foreach (CourtPiece piece in PalaceCourtAssets.Layout)
                {
                    if (PalaceWorldLayout.Enabled && (piece.File == "far-scenery.png" || piece.File == "colonnade.png")) continue;
                    placed += binding.Place(piece.File, piece.X, piece.Y, piece.Stratum);
                }
                PalaceCourtVisuals.SetReturnDoor(root.transform.Find("palace-court-idle"));
                bool r6 = PalaceRuntimeProfile.Capability("PALACE_CONTENT_PRIVATE") && PalaceLand12Runtime.Enabled;
                if (r6)
                {
                    if (!PalaceCourtContent.Create(binding, out content, out string contentReason))
                        throw new InvalidOperationException("R6 court batch rejected: " + contentReason);
                    placed += content.PlacedSprites;
                    _content = content;
                    PalaceCourtVisuals.Log("R6 environment placed=" + content.PlacedSprites + "; 2 buildings, 4 pond pieces; static character placeholders removed");
                }
                else foreach (CourtLotusBlade blade in PalaceCourtAssets.LotusLayout)
                    placed += binding.PlaceBlade(blade);
                if (PalaceWorldLayout.Enabled)
                {
                    try
                    {
                        var worldPlan = PalaceWorldLayout.Create(binding);
                        if (!PalaceWorldExpansion.Create(worldPlan, out worldExpansion, out string worldReason))
                            throw new InvalidOperationException("complete world art rejected: " + worldReason);
                        _worldExpansion = worldExpansion;
                        placed += worldExpansion.PlacedSprites;
                    }
                    catch (Exception worldError)
                    {
                        worldExpansion?.Clear();
                        worldExpansion = null;
                        _worldExpansion = null;
                        PalaceCourtVisuals.Log("full world art failed once; existing court/free return retained: " + worldError);
                    }
                }
            }
            _root = root;
            _created = true;
            root.SetActive(true);
            if (depth != null && !depth.Activate(out string depthActivationReason))
                throw new InvalidOperationException("R5 activation failed: " + depthActivationReason);
            if (scroll != null && !scroll.Activate(out string scrollActivationReason))
                throw new InvalidOperationException("R4 activation failed: " + scrollActivationReason);
            if (worldExpansion != null && !worldExpansion.Activate(out string activationReason))
            {
                if (actorsR7 && !worldExpansion.TryClear(out string actorClearReason))
                {
                    _worldExpansion = worldExpansion; _created = false; _cleanupRetained = true;
                    PalaceCourtVisuals.SetReturnDoor(null);
                    PalaceCourtVisuals.Log("R7 actors activation cleanup unknown; owner/outer root retained: " + actorClearReason);
                    return;
                }
                _worldExpansion = null;
                PalaceCourtVisuals.Log("world actors activation failed; known rollback, environment gate retained: " + activationReason);
            }
            content?.ObserveFiguresOnce();
            PalaceCourtVisuals.Log("court root created: anchor=" + anchorSource + " center=" + Vec(position)
                + " sprites=" + placed + " textures=" + (r5 ? 24 : r4 ? 3 : assets.Textures.Count)
                + "; call returned - screen acceptance pending, no interaction");
            PalaceCourtVisuals.Log("land12 read-only snapshot: kingdom=" + kingdom.Pointer.ToInt64()
                + " " + ReadOnlySnapshot(kingdom) + "; no writes");
        }
        catch (Exception e)
        {
            PalaceCourtVisuals.SetReturnDoor(null);
            if (actorsR7 && worldExpansion != null && !worldExpansion.TryClear(out string actorClearReason))
            {
                _worldExpansion = worldExpansion; _root = root; _created = false; _cleanupRetained = true;
                PalaceCourtVisuals.Log("R7 actors create cleanup unknown; owner/outer root retained: " + actorClearReason + "; " + e.Message);
                return;
            }
            if (actorsR7) { worldExpansion = null; _worldExpansion = null; }
            if (depth != null && !depth.Clear(out string depthClearReason))
            {
                _depth = depth; _root = root; _created = false; _cleanupRetained = true;
                PalaceCourtVisuals.Log("R5 create failed; cleanup owner/outer root retained: " + depthClearReason + "; " + e.Message);
                return;
            }
            _depth = null;
            if (scroll != null && !scroll.Clear(out string scrollClearReason))
            {
                _scroll = scroll; _root = root; _created = false; _cleanupRetained = true;
                PalaceCourtVisuals.Log("R4 create failed; cleanup owner/outer root retained, no retry: " + scrollClearReason + "; " + e.Message);
                return;
            }
            _scroll = null;
            worldExpansion?.Clear();
            _worldExpansion = null;
            content?.Clear();
            _content = null;
            if (root != null) { try { root.SetActive(false); UnityEngine.Object.Destroy(root); } catch (Exception) { } }
            assets?.DestroyAll();
            PalaceCourtVisuals.SetAssets(null);
            _created = false;
            PalaceCourtVisuals.Log("court create failed; own assets rolled back; no retry this lifecycle: " + e);
        }
    }

    // R7 shared art coordinates, actor-specific visible-height factors from fixed idle0 semantics.
    // One CourtBinding calibration; no Native actor scan, no old stage/building placement.
    [HideFromIl2Cpp]
    private static PalaceActorsPlan CreateR7ActorsPlan(CourtBinding binding)
    {
        PalaceScrollCalibration c = binding.ScrollCalibration;
        if (c == null || binding.Reference == null) throw new InvalidOperationException("R7 actor binding missing");
        float z = binding.Reference.transform.position.z; // same unit plane tested between all R7 back/front depths
        float street = (float)c.WorldY(187);
        float stage = (float)c.WorldY(178);
        var plan = new PalaceActorsPlan { Guards = new List<PalaceActorGuardPlan>() };
        plan.Guards.Add(new PalaceActorGuardPlan
        {
            ActorId = "patrol-guard-lance", Parent = binding.Parent, Reference = binding.Reference,
            Left = new Vector3((float)c.WorldX(585), street, z), Right = new Vector3((float)c.WorldX(635), street, z),
            WalkLoops = 3, YScale = 0.9f
        });
        plan.Guards.Add(new PalaceActorGuardPlan
        {
            ActorId = "patrol-guard-shield", Parent = binding.Parent, Reference = binding.Reference,
            Left = new Vector3((float)c.WorldX(1100), street, z), Right = new Vector3((float)c.WorldX(1160), street, z),
            WalkLoops = 4, YScale = (0.675f * 32f / 26f)
        });
        plan.Dancer = new PalaceActorDancerPlan
        {
            Parent = binding.Parent, Reference = binding.Reference,
            WorldPosition = new Vector3((float)c.WorldX(999), stage, z), YScale = 0.72f
        };
        PalaceCourtVisuals.Log("R7 actors plan: native-visible-worker reference~.675u; y=lance.9/shield21.6/26/dancer.72,"
            + " x=1/flipX-facing, shared street187/stage178; stage=(" + plan.Dancer.WorldPosition + ")"
            + "; staged actor clocks only, no NPC/combat/interaction/save identity; Native foot/front/height pending");
        return plan;
    }

    [HideFromIl2Cpp]
    private void Cleanup(string reason)
    {
        PalaceCourtVisuals.SetReturnDoor(null);
        if (_cleanupRetained) return;
        bool had = _created;
        if (_actorsR7 && _worldExpansion != null)
        {
            if (!_worldExpansion.TryClear(out string actorClearReason))
            {
                _created = false; _pending = null; _awaitPlaying = false; _cleanupRetained = true;
                PalaceCourtVisuals.Log("R7 actor cleanup owner/outer root retained: " + reason + "; " + actorClearReason);
                return;
            }
            _worldExpansion = null;
        }
        if (_depth != null && !_depth.Clear(out string depthClearReason))
        {
            _created = false; _pending = null; _awaitPlaying = false; _cleanupRetained = true;
            PalaceCourtVisuals.Log("R5 cleanup owner/outer root retained: " + reason + "; " + depthClearReason);
            return;
        }
        _depth = null;
        if (_scroll != null && !_scroll.Clear(out string scrollClearReason))
        {
            _created = false; _pending = null; _awaitPlaying = false; _cleanupRetained = true;
            PalaceCourtVisuals.Log("R4 cleanup owner/outer root retained: " + reason + "; " + scrollClearReason);
            return;
        }
        _scroll = null;
        GameObject root = _root;
        _root = null;
        _created = false;
        _kingdom = null;
        _layer = null;
        _worldExpansion?.Clear();
        _worldExpansion = null;
        _content?.Clear();
        _content = null;
        PalaceCourtAssets assets = PalaceCourtVisuals.TakeAssets();
        try
        {
            if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
        }
        catch (Exception e) { PalaceCourtVisuals.Log("own root destroy failed: " + e.Message); }
        if (assets != null) assets.DestroyAll();
        if (had) PalaceCourtVisuals.Log("court removed: " + reason);
    }

    /// <summary>同 epoch 防重建标记：本 Kingdom + gameLayer 组合只 attemptCreate 一次，清理后不自动重建。</summary>
    [HideFromIl2Cpp]
    private void MarkAttempt(Kingdom kingdom)
    {
        if (kingdom == null) return;
        _attemptKingdom = kingdom.Pointer;
        _attemptLayer = IntPtr.Zero;
        try
        {
            Managers m = Managers.Inst;
            if (m != null && m.world != null && m.world.gameLayer != null) _attemptLayer = m.world.gameLayer.Pointer;
        }
        catch (Exception) { }
    }

    private void OnDestroy()
    {
        CancelHandoff("lifecycle host destroyed");
        Cleanup("lifecycle host destroyed");
    }

    private static string Vec(Vector3 v)
        => "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";

    /// <summary>只读位置/钱包快照（land12 本次加载）；绝不写 Currency/payState。</summary>
    private static string ReadOnlySnapshot(Kingdom kingdom)
    {
        try
        {
            Player player = kingdom != null ? kingdom.playerOne : null;
            if (player == null) return "player=n/a";
            string coins = player.wallet != null ? player.wallet.Coins.ToString() : "n/a";
            return "wallet.Coins=" + coins + " player=" + Vec(player.transform.position);
        }
        catch (Exception) { return "read-failed"; }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static string F3(float value) => float.IsNaN(value) ? "n/a" : value.ToString("F3");

    private static string GoName(SpriteRenderer renderer)
    {
        try { return renderer != null && renderer.gameObject != null ? renderer.gameObject.name : "n/a"; }
        catch (Exception) { return "read-failed"; }
    }
}

/// <summary>
/// 自有庭院资产（纹理/精灵先登记进列表，再由调用方统一回滚；只销毁自有对象）。
/// 单张失败（资源缺失/字节数异常/解码失败/尺寸不符/Sprite.Create 失败）抛明确异常；无占位 fallback。
/// </summary>
internal sealed class PalaceCourtAssets
{
    internal const float PixelsPerUnit = 45.7142857f;    // 32px 参考口径（32 / 0.7 世界单位）
    internal const float FootLinePx = 214f;              // 布局统一脚线 y=214（y 向下）
    internal const int CourtWidthPx = 1536;              // 3 × 512px 庭院
    private const float WaterEdgePx = FootLinePx + 22f;  // 石岸下缘水线（荷塘锚）
    private const int TileSizePx = 64;
    private const int MaxResourceBytes = 256 * 1024;
    private const string ResourcePrefix = "KingdomEnhancedMod.KEM_PalaceCourt/";

    internal static float HalfCourt => CourtWidthPx * 0.5f / PixelsPerUnit; // = 16.8 world
    internal static int TileCount => CourtWidthPx / TileSizePx;

    /// <summary>景物布局（像素，y 向下；root 原点 = 庭院左端 + 脚线 y214）；orderOffset 见 CourtStratum。</summary>
    internal static readonly CourtPiece[] Layout =
    {
        new CourtPiece("far-scenery.png", 512f, 12f, CourtStratum.FarScenery),
        new CourtPiece("colonnade.png", 512f, 95f, CourtStratum.Architecture),
        new CourtPiece("colonnade.png", 832f, 95f, CourtStratum.Architecture),
        new CourtPiece("colonnade.png", 256f, 95f, CourtStratum.Architecture),
        new CourtPiece("colonnade.png", 1088f, 95f, CourtStratum.Architecture),
        // Return 门仅画面：静态 idle 帧（不做 opening/open 动画、不付款、不回岛）。
        new CourtPiece("return/idle.png", 640f, 0f, CourtStratum.Architecture),
    };

    /// <summary>近岸荷塘小簇（像素，y 向下）：7 簇 × (3 叶 + 0..1 花)，叶面中心/茎底锚在水缘下 3..13px。</summary>
    internal static readonly CourtLotusBlade[] LotusLayout =
    {
        new CourtLotusBlade("lotus/lotus-leaf.png", 106f, WaterEdgePx + 8f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 128f, WaterEdgePx + 4f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 150f, WaterEdgePx + 12f),
        new CourtLotusBlade("lotus/lotus-flower.png", 138f, WaterEdgePx + 5f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 322f, WaterEdgePx + 4f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 344f, WaterEdgePx + 10f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 366f, WaterEdgePx + 6f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 538f, WaterEdgePx + 12f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 560f, WaterEdgePx + 6f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 582f, WaterEdgePx + 10f),
        new CourtLotusBlade("lotus/lotus-flower.png", 552f, WaterEdgePx + 3f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 754f, WaterEdgePx + 6f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 776f, WaterEdgePx + 12f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 798f, WaterEdgePx + 4f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 970f, WaterEdgePx + 10f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 992f, WaterEdgePx + 5f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1014f, WaterEdgePx + 13f),
        new CourtLotusBlade("lotus/lotus-flower.png", 1004f, WaterEdgePx + 6f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1186f, WaterEdgePx + 5f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1208f, WaterEdgePx + 11f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1230f, WaterEdgePx + 7f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1402f, WaterEdgePx + 11f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1424f, WaterEdgePx + 7f),
        new CourtLotusBlade("lotus/lotus-leaf.png", 1446f, WaterEdgePx + 13f),
        new CourtLotusBlade("lotus/lotus-flower.png", 1412f, WaterEdgePx + 4f),
    };

    internal readonly List<Texture2D> Textures = new List<Texture2D>();
    internal readonly List<Sprite> Sprites = new List<Sprite>();
    private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    internal Sprite Load(string relative)
    {
        if (_cache.TryGetValue(relative, out Sprite cached)) return cached;
        byte[] bytes = Read(relative);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Textures.Add(texture); // 先登记：其后任何失败由调用方 DestroyAll 统一回滚
        if (!ImageConversion.LoadImage(texture, bytes, false))
            throw new InvalidOperationException("decode failed: " + relative);
        ExpectedSize(relative, out int width, out int height);
        if (texture.width != width || texture.height != height)
            throw new InvalidOperationException(relative + " size " + texture.width + "x" + texture.height
                + " != expected " + width + "x" + height);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 0;
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0f), PixelsPerUnit, 0u, SpriteMeshType.FullRect);
        if (sprite == null) throw new InvalidOperationException("Sprite.Create failed: " + relative);
        Sprites.Add(sprite);
        _cache[relative] = sprite;
        return sprite;
    }

    internal void DestroyAll()
    {
        foreach (Sprite sprite in Sprites) DestroyQuietly(sprite);
        foreach (Texture2D texture in Textures) DestroyQuietly(texture);
        Sprites.Clear();
        Textures.Clear();
        _cache.Clear();
    }

    private static byte[] Read(string relative)
    {
        string logicalName = ResourcePrefix + relative;
        Assembly assembly = typeof(PalaceCourtVisuals).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream(logicalName))
        {
            if (stream == null)
                throw new FileNotFoundException("embedded resource missing: " + logicalName + " (" + ManifestHint() + ")");
            long length = stream.Length;
            if (length <= 0 || length > MaxResourceBytes)
                throw new InvalidDataException("resource size rejected: " + logicalName + " " + length + " bytes");
            byte[] bytes = new byte[(int)length];
            int read = 0;
            while (read < bytes.Length)
            {
                int step = stream.Read(bytes, read, bytes.Length - read);
                if (step <= 0) break;
                read += step;
            }
            if (read != bytes.Length)
                throw new InvalidDataException("resource truncated: " + logicalName + " " + read + "/" + bytes.Length);
            return bytes;
        }
    }

    private static void ExpectedSize(string relative, out int width, out int height)
    {
        switch (relative)
        {
            case "far-scenery.png": width = 512; height = 192; return;
            case "colonnade.png": width = 192; height = 128; return;
            case "ground/stone-bank.png": width = 64; height = 32; return;
            case "ground/brick-surface.png": width = 64; height = 32; return;
            case "lotus/lotus-leaf.png": width = 16; height = 8; return;
            case "lotus/lotus-flower.png": width = 16; height = 16; return;
            case "return/idle.png": width = 256; height = 224; return;
            default: throw new InvalidOperationException("no expected size for " + relative);
        }
    }

    private static string ManifestHint()
    {
        try
        {
            string[] names = typeof(PalaceCourtVisuals).Assembly.GetManifestResourceNames();
            var hits = new List<string>();
            foreach (string name in names)
                if (name != null && name.Contains("PalaceCourt")) hits.Add(name);
            return hits.Count == 0 ? "no KEM_PalaceCourt resources embedded" : "embedded: " + string.Join(", ", hits);
        }
        catch (Exception) { return "manifest names unavailable"; }
    }

    private static void DestroyQuietly(UnityEngine.Object obj)
    {
        try { if (obj != null) UnityEngine.Object.Destroy(obj); } catch (Exception) { }
    }
}

/// <summary>一次创建的借用绑定（sorting/material/layer/z 全部取自当前参照；借来的对象只读不销毁）。</summary>
internal sealed class CourtBinding
{
    internal PalaceCourtAssets Assets;
    internal PalaceScrollCalibration ScrollCalibration; // written once by Create; all R4 layers consume this instance
    internal Transform Parent;
    internal int GoLayer, LayerId, BaseOrder;
    internal Material Material;
    internal SpriteRenderer Reference;

    internal int Place(string file, float x, float y, CourtStratum stratum)
        => PlaceSprite(Assets.Load(file), Path.GetFileNameWithoutExtension(file), x, y, stratum);

    internal int PlaceBlade(CourtLotusBlade blade)
    {
        Sprite sprite = Assets.Load(blade.File);
        float w = sprite.rect.width, h = sprite.rect.height;
        bool flower = blade.File.EndsWith("lotus-flower.png", StringComparison.Ordinal);
        float left = blade.CenterX - w * 0.5f;
        float top = flower ? blade.AnchorY - h : blade.AnchorY - h * 0.5f;
        return PlaceSprite(sprite, Path.GetFileNameWithoutExtension(blade.File), left, top, CourtStratum.NearWaterDecoration);
    }

    private int PlaceSprite(Sprite sprite, string label, float x, float y, CourtStratum stratum)
    {
        float w = sprite.rect.width, h = sprite.rect.height;
        var child = new GameObject("palace-court-" + label);
        child.layer = GoLayer;
        child.transform.SetParent(Parent, false);
        // 布局像素（左上角，y 向下）→ root 局部；z 恒为参照平面（不做跨平面移动）。
        child.transform.localPosition = new Vector3((x + w * 0.5f) / PalaceCourtAssets.PixelsPerUnit,
            (PalaceCourtAssets.FootLinePx - (y + h)) / PalaceCourtAssets.PixelsPerUnit, 0f);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerID = LayerId;
        renderer.sortingOrder = BaseOrder + (int)stratum;
        renderer.color = Color.white;
        if (Material != null) renderer.sharedMaterial = Material;
        return 1;
    }
}

/// <summary>相对当前角色参照 sortingOrder 的偏移：远景 -3 / 建筑 -2 / 可走表面 -1 / 近岸荷塘 +1；z 不偏移。</summary>
internal enum CourtStratum
{
    FarScenery = -3,
    Architecture = -2,
    WalkableSurface = -1,
    Figure = 0,
    NearWaterDecoration = 1,
}

internal readonly struct CourtPiece
{
    internal readonly string File;
    internal readonly float X, Y;
    internal readonly CourtStratum Stratum;

    internal CourtPiece(string file, float x, float y, CourtStratum stratum)
    {
        File = file; X = x; Y = y; Stratum = stratum;
    }
}

internal readonly struct CourtLotusBlade
{
    internal readonly string File;
    internal readonly float CenterX, AnchorY;

    internal CourtLotusBlade(string file, float centerX, float anchorY)
    {
        File = file; CenterX = centerX; AnchorY = anchorY;
    }
}
