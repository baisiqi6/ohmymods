using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Native-free decisions used by the adapter. The formation remains the sole layout owner.
internal static class HeavyShieldBannerRules
{
    internal static bool SameOwner(in HeavyShieldCampaignToken a, in HeavyShieldCampaignToken b) =>
        a.Guid == b.Guid && a.Global == b.Global && a.Campaign == b.Campaign
        && a.OwnerGeneration == b.OwnerGeneration; // Native save legitimately changes Phase.

    internal static bool SameContext(object sessionA, in HeavyShieldCampaignToken campaignA, long worldA, int landA,
        object sessionB, in HeavyShieldCampaignToken campaignB, long worldB, int landB) =>
        sessionA != null && ReferenceEquals(sessionA, sessionB) && worldA > 0 && worldA == worldB
        && landA >= 0 && landA == landB && SameOwner(campaignA, campaignB);

    internal static bool TryNextActivation(ref long counter, out long activation)
    {
        activation = 0;
        if (counter == long.MaxValue) return false;
        activation = ++counter;
        return true;
    }

    internal static int FrontSlot(int count, int livingPikeman, int occupied)
    {
        if (count <= 0 || count > 128 || livingPikeman < -1 || livingPikeman >= count
            || occupied < -1 || occupied >= count) return -1;
        return livingPikeman >= 0 ? livingPikeman : occupied >= 0 ? occupied : count - 1;
    }

    internal static float Speed(float value) => float.IsFinite(value) && value > 0f ? value : 0f;
}

// One captured activation, with the G0 binding as the only assignment authority.
// No native references here: these actual production transitions are host-testable.
internal sealed class HeavyShieldBannerRequest
{
    private readonly HeavyShieldBannerBinding _binding = new();
    private readonly List<HeavyShieldBannerCandidate> _selection = new(4);
    internal long Activation { get; private set; }
    internal long Formation { get; private set; }
    internal long World { get; private set; }
    internal int Side { get; private set; }
    internal float BannerX { get; private set; }
    internal bool Pending { get; private set; }
    internal Guid Receipt { get; private set; }
    internal long Life { get; private set; }

    internal void Begin(long activation, long formation, long world, int side, float bannerX)
    {
        End();
        Activation = activation; Formation = formation; World = world; Side = side; BannerX = bannerX;
        Pending = activation > 0 && formation > 0 && world > 0 && (side == -1 || side == 1)
            && float.IsFinite(bannerX);
    }

    internal void Select(IReadOnlyList<HeavyShieldBannerCandidate> candidates, Guid excluded, long excludedLife)
    {
        if (!Pending) return;
        Pending = false;
        _selection.Clear();
        if (candidates != null && candidates.Count <= 4)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                _selection.Add(candidate.Receipt == excluded && candidate.Life == excludedLife
                    ? candidate with { Eligible = false } : candidate);
            }
        }
        HeavyShieldBannerSelection.TrySelect(_selection, Side, BannerX, out var chosen);
        if (_binding.TryBind(Activation, Formation, World, Side, chosen))
        { Receipt = chosen.Receipt; Life = chosen.Life; }
        _selection.Clear();
    }

    internal bool Observe(IReadOnlyList<HeavyShieldBannerCandidate> candidates)
    {
        if (Receipt == Guid.Empty) return false;
        int matches = 0;
        bool current = false;
        if (candidates != null && candidates.Count <= 4)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var row = candidates[i];
                if (row.Receipt != Receipt) continue;
                ++matches;
                current = row.Life == Life && row.Side == Side && float.IsFinite(row.X);
            }
        }
        // Eligible is an initial-selection fact. InBanner makes an assigned row
        // ineligible for another banner without invalidating its existing life.
        if (_binding.IsCurrent(Activation, Formation, World, Side, current && matches == 1, Receipt, Life))
            return true;
        End();
        return false;
    }

    internal void End()
    {
        _binding.End(); // Never reset its activation watermark on ordinary loss.
        Pending = false; Receipt = Guid.Empty; Life = 0;
        Activation = Formation = World = 0; Side = 0; BannerX = 0;
        _selection.Clear();
    }
}

internal static class HeavyShieldBanner
{
    private sealed class Capture
    {
        internal readonly HeavyShieldBannerRequest Request = new();
        internal Player Player;
        internal Formation Formation;
        internal IntPtr PlayerPointer, FormationPointer;
        internal int PlayerId, FormationId;
        internal void End()
        {
            Request.End(); Player = null; Formation = null;
            PlayerPointer = FormationPointer = IntPtr.Zero; PlayerId = FormationId = 0;
        }
    }

    private static readonly Capture[] Captures = { new(), new() };
    private static HeavyShieldCampaignToken _campaign;
    private static HeavyShieldIdentity.Session _session;
    private static long _world, _nextActivation;
    private static int _land = -1, _diagnostics;
    private static bool _contextUnavailableLogged;

    internal static void Tick(in HeavyShieldCampaignToken campaign, long world,
        IReadOnlyList<HeavyShieldBannerCandidate> candidates, bool selecting)
    {
        if (!ObserveContext(campaign, world, HeavyShieldIdentity.Current)) return;
        // Validate both existing assignments before selecting either local player's new request.
        for (int i = 0; i < Captures.Length; i++)
        {
            var capture = Captures[i];
            if (capture.PlayerPointer == IntPtr.Zero) continue;
            try
            {
                if (!CurrentNative(capture, i)) { capture.End(); continue; }
                if (!capture.Request.Pending && !capture.Request.Observe(candidates)) capture.End();
            }
            catch (Exception ex) { Fail(capture, "owner read " + ex.GetType().Name); }
        }
        if (!selecting || !Available(campaign.Phase)) return;
        int first = Captures[0].Request.Activation <= Captures[1].Request.Activation ? 0 : 1;
        for (int n = 0; n < Captures.Length; n++)
        {
            int i = (first + n) % Captures.Length;
            var request = Captures[i].Request;
            if (!request.Pending) continue;
            var other = Captures[1 - i].Request;
            request.Select(candidates, other.Receipt, other.Life);
            if (request.Receipt == Guid.Empty) Captures[i].End(); // Consumed; no future automatic refill.
        }
    }

    internal static bool IsAssigned(Guid receipt, long life)
    {
        if (receipt == Guid.Empty || life <= 0) return false;
        for (int i = 0; i < Captures.Length; i++)
            if (Captures[i].Request.Receipt == receipt && Captures[i].Request.Life == life) return true;
        return false;
    }

    internal static bool TryGetOrder(Guid receipt, long life, out HeavyShieldBannerOrder order)
    {
        order = default;
        if (receipt == Guid.Empty || life <= 0) return false;
        for (int i = 0; i < Captures.Length; i++)
        {
            var capture = Captures[i];
            if (capture.Request.Receipt != receipt || capture.Request.Life != life) continue;
            try
            {
                if (!CurrentNative(capture, i)) { capture.End(); return false; }
                Formation formation = capture.Formation;
                // These gates precede every array/spacing read. Do not sample a temporary recruit layout.
                if (PatchMusketeerFormation.HasActiveDirectedTransaction(formation)
                    || PatchWorld_FleetBoatFormation.HasDirtyRowTypes(formation))
                {
                    order = new(0f, capture.Request.Side, 0f, true);
                    return true; // Root holds actual current x; X=0 is only an unused placeholder.
                }
                if (!TryReadFront(formation, out int front))
                { Fail(capture, "invalid formation layout"); return false; }
                // Current ARM64 GetFormationPosition returns world x and does not read the unit parameter.
                float frontX = formation.GetFormationPosition(front, null);
                float candidateX = frontX + capture.Request.Side * .4f;
                if (!float.IsFinite(frontX) || !float.IsFinite(candidateX))
                { Fail(capture, "nonfinite formation anchor"); return false; }
                float targetX = formation.RestrictPosition(candidateX);
                if (!float.IsFinite(targetX)) { Fail(capture, "nonfinite restricted anchor"); return false; }
                order = new(targetX, capture.Request.Side, HeavyShieldBannerRules.Speed(formation.overrideMoveSpeed), false);
                return true;
            }
            catch (Exception ex) { Fail(capture, "anchor read " + ex.GetType().Name); return false; }
        }
        return false;
    }

    internal static void Reset()
    {
        foreach (var capture in Captures) capture.End();
        _campaign = default; _session = null; _world = 0; _land = -1;
        // This process-lifetime token must never be reused, even after world/owner teardown.
    }

    private static bool ObserveContext(in HeavyShieldCampaignToken campaign, long world, HeavyShieldIdentity.Session session)
    {
        if (session == null || session.Land < 0 || world <= 0 || session.World != world
            || !ReferenceEquals(session, HeavyShieldIdentity.Current) || !HeavyShieldIdentity.ValidateCampaign(campaign))
        { ContextUnavailable(); return false; }
        // ReplaceIslandSession installs a fresh authoritative object even when world,
        // land and OwnerGeneration stay equal after a successful same-island load.
        if (!HeavyShieldBannerRules.SameContext(_session, _campaign, _world, _land,
                session, campaign, world, session.Land)) Reset();
        _campaign = campaign; _session = session; _world = world; _land = session.Land;
        return true;
    }

    private static bool Available(HeavyShieldIdentityPhase phase) => phase is HeavyShieldIdentityPhase.Allocated
        or HeavyShieldIdentityPhase.Staged or HeavyShieldIdentityPhase.Loaded;

    private static void Activated(Player player)
    {
        try
        {
            if (player == null) return;
            // A new return for the same player ends its prior request even when the new capture is invalid.
            foreach (var old in Captures) if (old.PlayerPointer == player.Pointer) old.End();
            if (!HeavyShieldRuntime.Enabled) return;
            var session = HeavyShieldIdentity.Current;
            if (session == null || session.Land < 0 || !HeavyShieldIdentity.TryGetCampaign(out var campaign)
                || !Available(campaign.Phase)) { ContextUnavailable(); return; }
            long world = Managers.Inst.world.gameLayer.Pointer.ToInt64();
            // Missing identity/Land never consumes an activation token. The failed
            // observation is bounded and visible; Tick cannot reconstruct that event.
            if (!ObserveContext(campaign, world, session)) return;
            int index = LocalIndex(player);
            if (index < 0) return;
            Formation formation = player._formation;
            var capture = Captures[index];
            capture.End();
            if (formation == null || !HeavyShieldBannerRules.TryNextActivation(ref _nextActivation, out long activation)) return;
            capture.Player = player; capture.Formation = formation;
            capture.PlayerPointer = player.Pointer; capture.FormationPointer = formation.Pointer;
            capture.PlayerId = player.GetInstanceID(); capture.FormationId = formation.GetInstanceID();
            capture.Request.Begin(activation, formation.Pointer.ToInt64(), world, (int)formation.side, player.transform.position.x);
            if (!capture.Request.Pending || !CurrentNative(capture, index)) capture.End();
            // The same actual formation cannot own two local players' escort bindings.
            else if (Captures[1 - index].FormationPointer == formation.Pointer) Captures[1 - index].End();
        }
        catch (Exception ex)
        {
            // Any partially captured request is ended once; never reconstruct it in Tick.
            foreach (var capture in Captures)
                if (ReferenceEquals(capture.Player, player)) Fail(capture, "activation read " + ex.GetType().Name);
        }
    }

    private static int LocalIndex(Player player)
    {
        if (player == null || !player.hasLocalAuthority) return -1;
        var kingdom = Managers.Inst?.kingdom;
        if (kingdom == null) return -1;
        if (kingdom.playerOne != null && kingdom.playerOne.Pointer == player.Pointer) return 0;
        if (kingdom.playerTwo != null && kingdom.playerTwo.Pointer == player.Pointer) return 1;
        return -1;
    }

    private static bool CurrentNative(Capture capture, int index)
    {
        Player player = capture.Player;
        Formation formation = capture.Formation;
        var session = HeavyShieldIdentity.Current;
        if (session == null || session.Land < 0) { ContextUnavailable(); return false; }
        var layer = Managers.Inst?.world?.gameLayer;
        return HeavyShieldRuntime.Enabled && layer != null && layer.Pointer.ToInt64() == _world
            && ReferenceEquals(session, _session) && session.Land == _land && session.World == _world
            && capture.Request.World == _world && HeavyShieldIdentity.ValidateCampaign(_campaign)
            && player != null && player.Pointer == capture.PlayerPointer && player.GetInstanceID() == capture.PlayerId
            && LocalIndex(player) == index && player.isActiveAndEnabled && InWorld(player.gameObject, layer)
            && float.IsFinite(player.transform.position.x) && player.IsFormationActive
            && formation != null && formation.Pointer == capture.FormationPointer && formation.GetInstanceID() == capture.FormationId
            && player._formation != null && player._formation.Pointer == capture.FormationPointer
            && formation.GetFormationType == Formation.FormationType.PlayerFormation && formation.isActiveAndEnabled
            && InWorld(formation.gameObject, layer) && formation.lockSide
            && ((int)formation.side == -1 || (int)formation.side == 1) && (int)formation.side == capture.Request.Side;
    }

    private static bool TryReadFront(Formation formation, out int front)
    {
        front = -1;
        var types = formation.unitTypes;
        var units = formation.units;
        var spacing = formation.UnitSpacing;
        if (types == null || units == null || spacing == null || types.Length <= 0 || types.Length > 128
            || types.Length != units.Length || !float.IsFinite(formation.startOffset)) return false;
        int livingPikeman = -1, occupied = -1;
        var layer = Managers.Inst.world.gameLayer;
        for (int i = 0; i < types.Length; i++)
        {
            int type = (int)types[i];
            if (type < 0 || type >= (int)Formation.UnitTypes.Total || type >= spacing.Length
                || !float.IsFinite(spacing[type]) || spacing[type] < 0f) return false;
            var unit = units[i];
            if (unit == null) continue;
            GameObject go = unit.GetGO;
            if (!InWorld(go, layer)) continue;
            Formation owner = unit.GetFormation();
            if (owner == null || owner.Pointer != formation.Pointer) continue;
            var damage = go.GetComponent<Damageable>();
            if (damage != null && damage.isDead) continue;
            occupied = i;
            if (types[i] != Formation.UnitTypes.Pikemen) continue;
            var pike = unit.TryCast<Pikeman>();
            if (pike != null && pike._damageable != null && !pike._damageable.isDead
                && pike._damageable.gameObject != null && pike._damageable.gameObject.Pointer == go.Pointer
                && pike.GetGO != null && pike.GetGO.Pointer == go.Pointer
                && pike.GetFormation() != null && pike.GetFormation().Pointer == formation.Pointer)
                livingPikeman = i;
        }
        front = HeavyShieldBannerRules.FrontSlot(types.Length, livingPikeman, occupied);
        return front >= 0;
    }

    private static bool InWorld(GameObject go, Transform layer) => go != null && go.activeInHierarchy
        && layer != null && layer.gameObject != null && layer.gameObject.activeInHierarchy
        && go.scene.handle == layer.gameObject.scene.handle && go.transform.IsChildOf(layer);

    private static void Fail(Capture capture, string reason)
    {
        capture.End();
        Log("binding ended: " + reason);
    }

    private static void ContextUnavailable()
    {
        if (_contextUnavailableLogged) return;
        _contextUnavailableLogged = true;
        // This one context diagnostic remains visible even if the separate bounded
        // native-read diagnostics were already exhausted by earlier activations.
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
            "[HeavyShieldBanner] identity session/land unavailable; no new activation captured or token consumed"); } catch { }
    }

    private static void Log(string message)
    {
        if (_diagnostics >= 8) return;
        ++_diagnostics;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldBanner] " + message); } catch { }
    }

    // Static 2.4 ARM64 map: ActivateFormation=0x7dd638, OnDisable=0x664d20, each one mapped CSharp slot.
    // Postfix observes the final native side after ActivateFormation's enabled=true/side/recruit sequence.
    [HarmonyPatch(typeof(Player), nameof(Player.ActivateFormation))]
    private static class ActivateObserver
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, bool __runOriginal)
        { if (__runOriginal) Activated(__instance); }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.OnDisable))]
    private static class DisableObserver
    {
        [HarmonyPostfix]
        private static void Postfix(Formation __instance, bool __runOriginal)
        {
            if (!__runOriginal || ReferenceEquals(__instance, null)) return;
            foreach (var capture in Captures)
                if (capture.FormationPointer == __instance.Pointer) capture.End();
        }
    }
}
