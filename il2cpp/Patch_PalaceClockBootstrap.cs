#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Root-owned early adapter. It neither starts travel nor signs arrival/payment/save success.
// Required Root hunks are delivered separately; the native ordering remains an isolated-test gate.
internal static class PalaceClockBootstrap
{
    private static readonly object Gate = new();
    private static bool _attempted, _installed, _coldAttempted;
    private static string? _owner;
    private static MethodInfo? _pop, _prepare, _consume;
    private static PalacePaidHandle? _paid;
    private static ColdPopCall? _coldPop;
    private static readonly HashSet<string> FirstNative = new(StringComparer.Ordinal);
    private sealed class ColdPopCall
    {
        internal readonly Game Game;
        internal readonly GlobalSaveData Global;
        internal readonly CampaignSaveData Campaign;
        internal readonly IslandSaveData Island;
        internal readonly PalacePaidScope Owner;
        internal readonly string TripId;
        internal readonly PalaceSourceClockReceipt Receipt;
        internal readonly Game.State InitialState;
        internal readonly long Sequence;
        internal bool PostfixSeen, ActualRan, NativeResult, Closed, Fault;
        internal ColdPopCall(Game game, GlobalSaveData global, CampaignSaveData campaign, IslandSaveData island,
            PalacePaidScope owner, string tripId, PalaceSourceClockReceipt receipt, long sequence)
        { Game=game; Global=global; Campaign=campaign; Island=island; Owner=owner; TripId=tripId;
          Receipt=receipt; InitialState=game.state; Sequence=sequence; }
    }
    internal sealed class PalaceColdLoadEvidence
    {
        internal readonly Game Game;
        internal readonly GlobalSaveData Global;
        internal readonly CampaignSaveData Campaign;
        internal readonly IslandSaveData Island;
        internal readonly PalacePaidScope Owner;
        internal readonly string TripId;
        internal readonly int SourceLand;
        internal readonly PalaceSourceClockReceipt SourceClockReceipt;
        internal readonly Game.State InitialState;
        internal readonly long PopSequence;
        private PalaceColdLoadEvidence(ColdPopCall call)
        { Game=call.Game; Global=call.Global; Campaign=call.Campaign; Island=call.Island; Owner=call.Owner;
          TripId=call.TripId; SourceLand=call.Receipt.Scope.SourceLand; SourceClockReceipt=call.Receipt;
          InitialState=call.InitialState; PopSequence=call.Sequence; }
        // Private construction remains inside this enclosing type's evidence factory.
        internal static PalaceColdLoadEvidence? FromVerifiedCall(object call)
        {
            if (call is not ColdPopCall state || !ReferenceEquals(state, _coldPop) || !state.Closed
                || !state.PostfixSeen || !state.ActualRan || !state.NativeResult || state.Fault
                || state.InitialState == Game.State.Playing || state.Game.state != Game.State.Playing) return null;
            return new PalaceColdLoadEvidence(state);
        }
    }
    private static long _sequence;
    internal static string Status { get; private set; } = "not-installed";

    internal static bool InstallOnce(Harmony harmony)
    {
        lock (Gate)
        {
            if (_attempted) return _installed;
            _attempted = true;
            try
            {
                if (harmony == null || !PalaceLand12Runtime.Enabled || !PalaceVisitClock.HooksReady)
                    return Fail("clock/profile-not-ready");
                _pop = AccessTools.Method(typeof(IslandSaveData), "TryPopObjectsToScene", Type.EmptyTypes);
                _prepare = AccessTools.Method(typeof(PalaceClockBootstrap), nameof(PreparePopPrefix));
                _consume = AccessTools.Method(typeof(PalaceVisitClock), "TryPopPrefix");
                if (_pop == null || _pop.GetParameters().Length != 0 || _pop.ReturnType != typeof(bool)
                    || _prepare == null || _consume == null) return Fail("exact-pop-shape");
                _owner = harmony.Id;
                harmony.Patch(_pop, prefix: new HarmonyMethod(_prepare) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(PalaceClockBootstrap), nameof(PreparePopPostfix)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(typeof(PalaceClockBootstrap), nameof(PreparePopFinalizer)) { priority = Priority.Last });
                if (!VerifyOrder(out string why)) return Fail(why);
                _installed = true;
                Status = "installed-first-before-clock-last";
                Trace("install", Status);
                return true;
            }
            catch (Exception e) { return Fail("install:" + e.GetType().Name); }
        }
    }

    // Public Harmony sorter is essential: priority numbers alone do not establish actual order.
    internal static bool VerifyOrder(out string reason)
    {
        reason = "";
        try
        {
            if (_pop == null || _prepare == null || _consume == null || string.IsNullOrEmpty(_owner))
            { reason = "order-targets-missing"; return false; }
            var info = Harmony.GetPatchInfo(_pop);
            if (info == null) { reason = "order-no-patchinfo"; return false; }
            MethodInfo post = AccessTools.Method(typeof(PalaceClockBootstrap), nameof(PreparePopPostfix));
            MethodInfo fin = AccessTools.Method(typeof(PalaceClockBootstrap), nameof(PreparePopFinalizer));
            if (post == null || fin == null
                || info.Postfixes.Count(p => p.owner == _owner && p.PatchMethod == post && p.priority == Priority.Last) != 1
                || info.Finalizers.Count(p => p.owner == _owner && p.PatchMethod == fin && p.priority == Priority.Last) != 1
                || PatchProcessor.GetSortedPatchMethods(_pop, info.Finalizers.ToArray()).LastOrDefault() != fin)
            { reason = "order-exact-observation-or-finalizer-not-last"; return false; }
            Patch[] all = info.Prefixes.ToArray();
            if (all.Count(p => p.owner == _owner && p.PatchMethod == _prepare && p.priority == Priority.First) != 1
                || all.Count(p => p.owner == _owner && p.PatchMethod == _consume && p.priority == Priority.Last) != 1)
            { reason = "order-exact-owner-method-priority"; return false; }
            List<MethodInfo> sorted = PatchProcessor.GetSortedPatchMethods(_pop, all);
            if (!IsOrderedExactlyOnce(sorted, _prepare, _consume) || sorted.LastOrDefault() != _consume)
            { reason = "order-sorted-prepare-not-before-consume"; return false; }
            reason = "actual-harmony-sorted-first-before-last";
            return true;
        }
        catch (Exception e) { reason = "order:" + e.GetType().Name; return false; }
    }

    internal static bool IsOrderedExactlyOnce(IReadOnlyList<MethodInfo> methods, MethodInfo prepare, MethodInfo consume)
    {
        int p = -1, c = -1;
        for (int i = 0; i < methods.Count; i++)
        {
            if (methods[i] == prepare) { if (p >= 0) return false; p = i; }
            if (methods[i] == consume) { if (c >= 0) return false; c = i; }
        }
        return p >= 0 && c > p;
    }

    // After actual checkpoint + durable DepartureClaimed, before Carry/Trip/selection, and before unique Reload.
    internal static bool TryPrepareOutbound(PalacePaidHandle paid, Game sourceGame, out string reason)
    {
        reason = "";
        try
        {
            lock (Gate)
            {
                if (!_installed || !VerifyOrder(out reason)) return false;
                if (paid == null || sourceGame == null || paid.SourceClockReceipt == null
                    || paid.Stage != PalacePaidStage.DepartureClaimed || !PalaceLayoutRecipe.IsKnown(paid.DestinationLayout!)
                    || !TryCurrent(sourceGame, null, out var global, out var campaign, out reason)
                    || sourceGame.currentLand != paid.SourceLand || campaign.CurrentLand != paid.SourceLand
                    || sourceGame.state != Game.State.Playing || IslandSaveData.isSavingGame
                    || !OwnerMatches(paid.Owner, out reason)
                    || !paid.SourceClockReceipt.SameTripId(paid.TripId.ToString("D")))
                { if (reason.Length == 0) reason = "outbound-evidence"; return false; }
                if (!PalaceVisitClock.TryArmPaidOutbound(paid, sourceGame, out reason)) return false;
                _paid = paid; // Same handle through enter/return. This is not a second journal/token.
                Trace("outbound-arm", paid.TripId.ToString("D"));
                return true;
            }
        }
        catch (Exception e) { reason = "outbound:" + e.GetType().Name; return false; }
    }

    // VOID prefix always preserves the original and subsequent prefixes, including when preparation fails.
    private static void PreparePopPrefix(IslandSaveData __instance, bool __runOriginal, out ColdPopCall? __state)
    {
        __state = null;
        if (!__runOriginal) return;
        try
        {
            lock (Gate)
            {
                string why = "not-installed";
                if (!_installed || !VerifyOrder(out why)) { Status = "held:" + why; return; }
                Managers managers = Managers.Inst;
                Game? game = managers != null ? managers.game : null;
                if (game == null || !TryCurrent(game, __instance, out var global, out var campaign, out why)) return;
                Trace("prepare-pop", "game=" + game.Pointer.ToInt64() + " land=" + campaign.CurrentLand);
                if (PalaceLand12Runtime.TryDescribeVerifiedReloadHandoff((ulong)game.Pointer.ToInt64(), out var facts, out why))
                {
                    if (!PalaceSaveBindingSource.TryAdoptReloadBoundary(game, out why)) { Hold(why); return; }
                    if (!ReadTrip(global, campaign, out var trip, out var receipt, out var owner, out why)
                        || trip.TripId != facts.TripId || trip.Phase != facts.Phase
                        || trip.SourceLand != facts.RecordSourceLand || facts.TargetLand != campaign.CurrentLand)
                    { Hold("active-trip:" + why); return; }
                    PalacePaidHandle? paid = _paid;
                    if (!HandleMatches(paid, owner, trip, receipt, out why)) { Hold(why); return; }
                    if (facts.TargetLand == 12 && trip.Phase == PalaceTripContext.PhasePalaceSelected)
                    {
                        if (!PalaceVisitClock.TryConfirmOutboundArmed(receipt, trip.TripId, (long)facts.OldGame, out why)) Hold(why);
                        else { Status = "active-outbound-prepared"; Trace("adopt-outbound", trip.TripId); }
                        return; // Saved palace hydrates only from real Director.ApplyData inside native Pop.
                    }
                    if (facts.TargetLand != trip.SourceLand || trip.Phase != PalaceTripContext.PhaseReturnSelected
                        || paid!.Stage != PalacePaidStage.Entered || !TrySourceDigest(campaign, trip.SourceLand, owner, receipt, out why)
                        || campaign.currentReign == null) { Hold("return:" + why); return; }
                    if (!TryCurrent(game, __instance, out var stableGlobal, out var stableCampaign, out why)
                        || stableGlobal.Pointer != global.Pointer || stableCampaign.Pointer != campaign.Pointer
                        || !OwnerMatches(owner, out why)) { Hold("return-after-digest:" + why); return; }
                    var ret = new PalaceClockReturnFacts(trip.TripId, receipt, campaign.Pointer.ToInt64(),
                        global.GetCurrentCampaign().Pointer.ToInt64(), global.Pointer.ToInt64(), game.Pointer.ToInt64(),
                        __instance.Pointer.ToInt64(), campaign.totalGameDays, campaign.currentReign.daysSurvived,
                        receipt.SourceObjectsSha);
                    if (!PalaceVisitClock.TryArmReturnOrConfirmExact(ret, out why)) Hold(why);
                    else { Status = "return-prepared"; Trace("arm-return", trip.TripId); }
                    return;
                }
                // No token: only the explicit cold palace Pop may perform one initial IO preparation.
                // A failed active-token verification must never become an alternative cold capture.
                if (PalaceLand12Runtime.HasActiveReloadHandoff || campaign.CurrentLand != 12 || game.currentLand != 12
                    || _coldAttempted || PalaceVisitClock.Armed || game.state == Game.State.Playing) return;
                _coldAttempted = true; // Attempt consumed before IO; unknown is not retried from later getters/Playing.
                if (!PalaceSaveBindingSource.TryCaptureAtColdPalacePop(game, __instance, out why)
                    || !ReadTrip(global, campaign, out var cold, out var coldReceipt, out var coldOwner, out why)
                    || cold.Phase != PalaceTripContext.PhasePalaceSelected
                    || !PalacePaidResponsibility.TryReadExistingForBootstrap(coldOwner, cold.TripId, cold.SourceLand, out var existing, out why)
                    || !HandleMatches(existing, coldOwner, cold, coldReceipt, out why)
                    || (existing.Stage != PalacePaidStage.DepartureClaimed && existing.Stage != PalacePaidStage.Entered)
                    || !TrySourceDigest(campaign, cold.SourceLand, coldOwner, coldReceipt, out why)
                    || !TryCurrent(game, __instance, out var coldStableGlobal, out var coldStableCampaign, out why)
                    || coldStableGlobal.Pointer != global.Pointer || coldStableCampaign.Pointer != campaign.Pointer
                    || !OwnerMatches(coldOwner, out why)
                    || campaign.currentReign == null || !VerifyOrder(out why))
                { Hold("cold:" + why); return; }
                var scope = new PalaceClockScope(cold.CampaignGuid, cold.CampaignSlot, cold.Biome, cold.Reign, cold.Start, cold.SourceLand);
                if (!PalaceVisitClock.TryArmCold(coldReceipt, cold.TripId, scope, campaign.totalGameDays,
                        campaign.currentReign.daysSurvived, out why)) { Hold("cold-arm:" + why); return; }
                _paid = existing;
                __state = new ColdPopCall(game, global, campaign, __instance, coldOwner, cold.TripId, coldReceipt, _sequence);
                _coldPop = __state;
                Status = "cold-prepared-before-pop";
                Trace("arm-cold", cold.TripId);
            }
        }
        catch (Exception e) { Hold("prepare-pop:" + e.GetType().Name); }
    }

    private static void PreparePopPostfix(bool __result, bool __runOriginal, ColdPopCall? __state)
    {
        if (__state == null) return;
        try
        {
            lock (Gate)
            {
                if (__state.PostfixSeen) { __state.Fault = true; return; }
                __state.PostfixSeen = true; __state.ActualRan = __runOriginal; __state.NativeResult = __result;
                if (!__runOriginal || !__result) __state.Fault = true;
            }
        }
        catch (Exception) { __state.Fault = true; }
    }

    private static void PreparePopFinalizer(Exception? __exception, ColdPopCall? __state)
    {
        if (__state == null) return;
        try
        {
            lock (Gate)
            {
                if (__state.Closed || __exception != null || !__state.PostfixSeen
                    || !__state.ActualRan || !__state.NativeResult) __state.Fault = true;
                __state.Closed = true;
            }
        }
        catch (Exception) { __state.Fault = true; }
        // VOID finalizer: never replace/suppress the native exception.
    }

    internal static bool TryDescribeColdArrival(Game game, string tripId,
        out PalaceColdLoadEvidence evidence, out string reason)
    {
        evidence = null!; reason = "cold-pop-not-complete";
        try
        {
            lock (Gate)
            {
                ColdPopCall? state = _coldPop;
                if (!_installed || !VerifyOrder(out reason) || state == null || game == null
                    || game.Pointer != state.Game.Pointer || tripId != state.TripId
                    || !state.Closed || !state.PostfixSeen || !state.ActualRan || !state.NativeResult || state.Fault
                    || state.InitialState == Game.State.Playing || game.state != Game.State.Playing
                    || !TryCurrent(game, state.Island, out var global, out var campaign, out reason)
                    || global.Pointer != state.Global.Pointer || campaign.Pointer != state.Campaign.Pointer
                    || campaign.CurrentLand != 12 || game.currentLand != 12
                    || !ReadTrip(global, campaign, out var trip, out var receipt, out var owner, out reason)
                    || trip.TripId != tripId || trip.Phase != PalaceTripContext.PhasePalaceSelected
                    || !owner.ContentEquals(state.Owner) || receipt.WriteCanonicalStrict() != state.Receipt.WriteCanonicalStrict()
                    || !HandleMatches(_paid, owner, trip, receipt, out reason)
                    || !PalaceVisitClock.TryConfirmColdHydrated(game, receipt, out reason)) return false;
                PalaceColdLoadEvidence? result = PalaceColdLoadEvidence.FromVerifiedCall(state);
                if (result == null) return false;
                evidence = result; reason = "actual-cold-pop-to-same-game-playing";
                return true;
            }
        }
        catch (Exception e) { reason = "cold-arrival:" + e.GetType().Name; return false; }
    }

    [System.Diagnostics.Conditional("PALACE_NATIVE_SEQUENCE_TESTS")]
    internal static void TraceNativeBoundary(string boundary, Director director)
    {
        try
        {
            Managers m = Managers.Inst;
            Game? game = m != null ? m.game : null;
            if (m == null || game == null || director == null || m.director == null || m.director.Pointer != director.Pointer) return;
            lock (Gate)
            {
                string key = game.Pointer.ToInt64() + ":" + director.Pointer.ToInt64() + ":" + boundary;
                if (FirstNative.Count >= 96 || !FirstNative.Add(key)) return;
                CampaignSaveData c = CampaignSaveData.current;
                Trace(boundary, "game=" + game.Pointer.ToInt64() + " director=" + director.Pointer.ToInt64()
                    + " rawTotal=" + (c != null ? c.totalGameDays.ToString() : "unknown")
                    + " rawReign=" + (c != null && c.currentReign != null ? c.currentReign.daysSurvived.ToString() : "unknown"));
            }
        }
        catch (Exception) { }
    }

    // Root calls inside the existing GenerateCurrentConfig prefix and verified config-adoption boundary.
    // Saved islands are never seeded here; no fake DirectorData/ApplyData is constructed.
    internal static bool TryPrepareConfig(Game game, out string reason)
    {
        reason = "";
        try
        {
            lock (Gate)
            {
                if (!_installed || !VerifyOrder(out reason)
                    || !TryCurrent(game, null, out var global, out var campaign, out reason)
                    || campaign.CurrentLand != 12 || game.currentLand != 12
                    || !PalaceLand12Runtime.TryDescribeVerifiedReloadHandoff((ulong)game.Pointer.ToInt64(), out var facts, out reason)
                    || facts.TargetLand != 12 || facts.Phase != PalaceTripContext.PhasePalaceSelected
                    || !PalaceSaveBindingSource.TryAdoptReloadBoundary(game, out reason)
                    || !ReadTrip(global, campaign, out var trip, out var receipt, out var owner, out reason)
                    || trip.TripId != facts.TripId || trip.SourceLand != facts.RecordSourceLand
                    || !HandleMatches(_paid, owner, trip, receipt, out reason)
                    || !PalaceVisitClock.TryConfirmOutboundArmed(receipt, trip.TripId, (long)facts.OldGame, out reason)) return false;
                Trace("config-adopt", trip.TripId);
                IslandSaveData island = campaign.CurrentIsland;
                if (island == null || !island.isNew) { reason = "saved-island-awaits-native-apply"; return true; }
                // Failure remains pending/unknown: never repair it at Playing after an earlier native writer.
                bool seeded = PalaceVisitClock.TrySeedVerifiedFresh(game, island, out reason);
                Status = seeded ? "fresh-day1-prepared" : "fresh-pending:" + reason;
                Trace("fresh", Status);
                return seeded;
            }
        }
        catch (Exception e) { reason = "config:" + e.GetType().Name; return false; }
    }

    internal static bool WasPreparedFor(Game game, string tripId, out string reason)
    {
        reason = "palace-not-prepared";
        try
        {
            lock (Gate)
            {
                if (!_installed || !VerifyOrder(out reason) || game == null || game.state != Game.State.Playing
                    || !TryCurrent(game, null, out var global, out var campaign, out reason)
                    || !ReadTrip(global, campaign, out var trip, out var receipt, out var owner, out reason)
                    || trip.TripId != tripId || !HandleMatches(_paid, owner, trip, receipt, out reason)) return false;
                if (trip.Phase == PalaceTripContext.PhasePalaceSelected && campaign.CurrentLand == 12 && game.currentLand == 12)
                    return PalaceVisitClock.TryConfirmPalaceHealthy(game, receipt, out reason);
                if (trip.Phase == PalaceTripContext.PhaseReturnSelected && campaign.CurrentLand == trip.SourceLand
                    && game.currentLand == trip.SourceLand && campaign.CurrentIsland != null)
                    return PalaceVisitClock.TryConfirmReturnApplied(game, campaign.CurrentIsland, tripId, receipt, out reason);
                reason = "prepared-arrival-phase-land";
                return false;
            }
        }
        catch (Exception e) { reason = "prepared-arrival:" + e.GetType().Name; return false; }
    }

    private static bool TryCurrent(Game game, IslandSaveData? pop, out GlobalSaveData global,
        out CampaignSaveData campaign, out string reason)
    {
        global = null!; campaign = null!; reason = "";
        Managers m = Managers.Inst;
        global = GlobalSaveData.loaded; campaign = CampaignSaveData.current;
        if (m == null || game == null || m.game == null || m.game.Pointer != game.Pointer
            || global == null || campaign == null || global.GetCurrentCampaign() == null
            || global.GetCurrentCampaign().Pointer != campaign.Pointer || game.currentLand != campaign.CurrentLand
            || NetworkBigBoss.IsOnline || global.InChallenge || IslandSaveData.isSavingGame)
        { reason = "current-identity"; return false; }
        if (pop != null && (campaign.CurrentIsland == null || campaign.CurrentIsland.Pointer != pop.Pointer
            || campaign._islands == null || campaign.CurrentLand < 0 || campaign.CurrentLand >= campaign._islands.Count
            || campaign._islands[campaign.CurrentLand] == null || campaign._islands[campaign.CurrentLand].Pointer != pop.Pointer))
        { reason = "pop-not-exact-current-island"; return false; }
        return true;
    }

    private static bool ReadTrip(GlobalSaveData global, CampaignSaveData campaign, out PalaceTripRecord trip,
        out PalaceSourceClockReceipt receipt, out PalacePaidScope owner, out string reason)
    {
        trip = null!; receipt = null!; owner = null!;
        if (!PalaceSaveBindingSource.TryGetSemanticOwner(out string guid, out int slot, out int biome, out int reign, out int start, out reason)
            || !Guid.TryParseExact(guid, "D", out Guid id) || id == Guid.Empty) return false;
        owner = new PalacePaidScope(PalaceRuntimeProfile.Id, id, slot, biome, reign, start, 1);
        if (PalaceTripContext.Read(global, out trip, out reason) != PalaceTripContext.ReadStatus.Ok || (trip.Version != 3 && trip.Version != 4)
            || !PalaceTripContext.RecordMatches(campaign, guid, slot, trip, out reason)
            || trip.CampaignGuid != guid || !PalaceSourceClockReceipt.TryParseStrict(trip.SourceClockReceipt, out receipt, out reason)
            || !receipt.SameScope(new PalaceClockScope(guid, slot, biome, reign, start, trip.SourceLand))
            || !receipt.SameTripId(trip.TripId)
            || (trip.Version==3 && (!PalaceDestinationAdmission.TryResolveTripLayout(campaign,trip,out var layout,out reason)
                || !ReferenceEquals(layout,PalaceLayoutRecipe.Known44)))) return false;
        return true;
    }

    private static bool OwnerMatches(PalacePaidScope owner, out string reason)
    {
        if (!PalaceSaveBindingSource.TryGetSemanticOwner(out string guid, out int slot, out int biome, out int reign, out int start, out reason)) return false;
        return owner.Profile == PalaceRuntimeProfile.Id && owner.CampaignGuid.ToString("D") == guid
            && owner.CampaignSlot == slot && owner.Biome == biome && owner.Reign == reign && owner.Start == start && owner.PlayerSlot == 1;
    }

    private static bool HandleMatches(PalacePaidHandle? paid, PalacePaidScope owner, PalaceTripRecord trip,
        PalaceSourceClockReceipt receipt, out string reason)
    {
        reason = "paid-handle-drift";
        return paid != null && paid.MatchesDestination(trip) && paid.Owner.ContentEquals(owner) && paid.TripId.ToString("D") == trip.TripId
            && paid.SourceLand == trip.SourceLand && PalaceIslandTravel.SameAnchor(paid.Anchor, trip.SourceEntryAnchor)
            && paid.SourceClockReceipt != null
            && string.Equals(paid.SourceClockReceipt.WriteCanonicalStrict(), receipt.WriteCanonicalStrict(), StringComparison.Ordinal);
    }

    private static bool TrySourceDigest(CampaignSaveData campaign, int source, PalacePaidScope owner,
        PalaceSourceClockReceipt receipt, out string reason)
    {
        reason = "source-digest-unavailable";
        GlobalSaveData global = GlobalSaveData.loaded;
        if (campaign._islands == null || source < 0 || source >= campaign._islands.Count || campaign._islands[source] == null
            || global == null || global.prefs == null || global.prefs.contents == null) return false;
        var prefs = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] keys = { "KEM.CoinCourier.Campaigns.v1", "KEM.HeavyShield.Campaigns.v1", "KingdomEnhancedMod_MusketeerRights_v1",
            "KingdomEnhancedMod_HeroRights_v1", "MyMod_SharedBankNative_v1" };
        foreach (string key in keys) if (global.prefs.contents.TryGetValue(key, out string value)) prefs.Add(key, value);
        // Complete current source island; only compare object/Director digests with the original receipt.
        // Current palace save gzip and rights maps are allowed to differ from the departure snapshot.
        string json = JsonUtility.ToJson(campaign._islands[source], false);
        if (!PalacePaidSourceProof.TryReadCapturedIsland(json, owner, source, prefs, out var values, out reason)) return false;
        return receipt.SameSourceObjects(values.ObjectsSummarySha256)
            && string.Equals(receipt.SourceDirectorSha, values.DirectorSummarySha256, StringComparison.Ordinal);
    }

    private static bool Fail(string reason) { _installed = false; Status = "held:" + reason; Trace("install-fault", reason); return false; }
    private static void Hold(string reason) { Status = "held:" + reason; Trace("hold", reason); }
    internal static void Trace(string boundary, string details)
    {
        try
        {
            long n = System.Threading.Interlocked.Increment(ref _sequence);
            if (n <= 128) PalaceClockLog.Info("bootstrap-seq=" + n + " boundary=" + boundary + " " + details);
        }
        catch (Exception) { } // Observer/logging failures never alter native flow or exception semantics.
    }
}
