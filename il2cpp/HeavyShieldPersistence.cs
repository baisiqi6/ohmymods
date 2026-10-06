using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

// Native callback receiver only. Existing save/GetID/load/generation owners call
// these methods in their own isolated try/catch; this class installs no duplicate
// Harmony patches and never blocks the native save on a HeavyShield failure.
// Staged state has exactly two write protocols: (1) exact save capture stages one
// island in EndNativeIslandSave; (2) first-connect enrollment stages the frozen
// baseline of a provably unpaid campaign through one shared commit gate entered
// from EndNativeIslandLoad or EndNativeGeneration. Both protocols publish only
// through the native PrefsPrepare copy/readback finalizer.
internal static class HeavyShieldPersistence
{
    internal sealed class SaveCapture
    {
        internal SaveCapture Previous;
        internal int Slot, Land, Challenge;
        internal IntPtr Global, Campaign, Game, World, Layer, Island;
        internal IslandSaveData IslandObject;
        internal HeavyShieldIdentity.Session Session;
        internal bool MarkerSeen, Closed, Conflict, SceneExact;
        internal readonly Dictionary<Guid, string> Ids = new();
        internal readonly HashSet<string> UniqueIds = new(StringComparer.Ordinal);
    }

    internal sealed class LoadCapture
    {
        internal LoadCapture Previous;
        internal IntPtr Island;
        internal HeavyShieldIdentity.Session Session;
        internal HeavyShieldSavedIsland Saved;
        internal readonly Dictionary<string, IntPtr> FrozenRows = new(StringComparer.Ordinal);
        internal readonly HashSet<Guid> Restored = new();
        internal readonly List<HeavyShieldCareerHandle> Attached = new();
        internal bool Exact, Fresh, Conflict, Closed;
        internal HeavyShieldIdentityPhase SourcePhase;
        internal EnrollmentCapture Enrollment;
        internal bool EnrollmentRequired;
    }

    // Frozen first-connect baseline for one native pop. Captured in the load
    // prefix (the native pop consumes its objects table), committed in the load
    // finalizer only when that same pop really succeeded and identity stayed exact.
    internal sealed class EnrollmentCapture
    {
        internal IntPtr Global, Prefs, Campaign, Island;
        internal long World, OwnerGeneration;
        internal int Land, Challenge, Slot;
        internal string Guid;
        internal HeavyShieldNativeKeyRead KeyRead;
        internal string KeyRaw;
        internal bool CurrentMissing;
        internal bool IncludesCurrent;
        internal bool KeepBlocked;
        internal bool RequireCurrentSlot;
        internal readonly List<HeavyShieldEnrollment.Row> Rows = new();
    }

    internal sealed class GenerationCapture
    {
        internal IntPtr Campaign, Island;
        internal long World;
        internal int Land, Challenge;
        internal bool WasNew, Closed;
    }

    internal sealed class CreateCapture
    {
        internal CreateCapture Previous;
        internal LoadCapture Load;
        internal IntPtr Row;
        internal bool Owned;
        internal bool Closed;
        internal HeavyShieldLoadRowMask.Scope MaskScope;
    }

    internal sealed class PrepareCapture
    {
        internal PrepareCapture Previous;
        internal IntPtr Global, Prefs;
        internal long OwnerGeneration;
        internal HeavyShieldSaveDocument Document;
        internal string Raw;
        internal bool Written, Closed;
        internal readonly Dictionary<(string Guid, int Challenge, int Land), long> Versions = new();
    }

    private sealed class GlobalBinding
    {
        internal IntPtr Global, Prefs;
        internal long Generation;
        internal bool Closed, KeyPresent, KeyPreflighted;
        internal string OriginalRaw;
        internal HeavyShieldNativeKeyRead KeyRead;
        internal HeavyShieldSaveDocument Loaded;
        internal HeavyShieldSaveDocument Staged;
        internal HeavyShieldSaveDocument Prepared;
        internal string ExpectedRaw;
        internal long NextStage;
        internal readonly Dictionary<(string Guid, int Challenge, int Land), long> StagedVersions = new();
        internal readonly Dictionary<(string Guid, int Challenge, int Land), long> PreparedVersions = new();
        internal readonly HashSet<string> PaidEvidence = new(StringComparer.Ordinal);
        internal readonly Dictionary<IntPtr, HeavyShieldIdentity.Session> Sessions = new();
        internal readonly Dictionary<IntPtr, HeavyShieldSavedCampaign> LoadedByNative = new();
    }

    private static GlobalBinding _global;
    private static long _nextGeneration;
    private static SaveCapture _save;
    private static LoadCapture _load;
    private static CreateCapture _create;
    private static PrepareCapture _prepare;
    private static readonly HeavyShieldLoadRowMask CreateMask = new();
    internal static bool ShieldLoadInProgress => CreateMask.Active;

    internal static void TickBinding()
    {
        try
        {
            var loaded = GlobalSaveData._loaded;
            if (loaded == null) { HeavyShieldIdentity.Install(null); return; }
            if (_global == null || _global.Global != loaded.Pointer) BindGlobal(loaded);
            SyncCampaigns(loaded);
            var current = CampaignSaveData.current;
            if (_global == null || _global.Closed || current == null
                || !_global.Sessions.TryGetValue(current.Pointer, out var session)
                || session.Slot != loaded.currentCampaign
                || session.Challenge != loaded.currentChallenge)
            { HeavyShieldIdentity.Install(null); return; }
            if (session.Land >= 0 && WorldKey() != 0) session.World = WorldKey();
            HeavyShieldIdentity.Install(session);
            if (session.KeyReady && !IsNativeKeyCurrent(session)) Freeze(session);
            if (_load == null) ActivateBoundSoldiers(session);
            HeavyShieldIdentity.PrimeNativeKeyBeforePayment();
        }
        catch { HeavyShieldIdentity.Install(null); }
    }

    internal static void BeforeCampaignMutation(GlobalSaveData exactGlobal)
    {
        try
        {
            if (exactGlobal != null && GlobalSaveData._loaded != null
                && exactGlobal.Pointer == GlobalSaveData._loaded.Pointer) TickBinding();
        }
        catch { }
    }

    internal static void AfterCampaignCreated(GlobalSaveData exactGlobal, CampaignSaveData exactCreated)
    {
        try
        {
            if (exactGlobal == null || exactCreated == null || GlobalSaveData._loaded == null
                || exactGlobal.Pointer != GlobalSaveData._loaded.Pointer) return;
            TickBinding();
            if (_global != null && _global.Sessions.TryGetValue(exactCreated.Pointer, out var session))
            {
                session.Phase = HeavyShieldIdentityPhase.Allocated;
                session.Unknown = true; // generation proof, then first exact island, opens it
            }
        }
        catch { }
    }

    internal static void PrepareNativePrefs(PrefsSaveData exactPrefs)
    {
        try
        {
            var owner = _global;
            if (owner == null || owner.Closed || exactPrefs == null || owner.Prefs != exactPrefs.Pointer
                || GlobalSaveData._loaded == null || GlobalSaveData._loaded.Pointer != owner.Global) return;
            SyncCampaigns(GlobalSaveData._loaded);
            if (!RewriteStaged(owner, out _, out _)) FreezeAll(owner);
        }
        catch { if (_global != null) FreezeAll(_global); }
    }

    internal static PrepareCapture BeginNativePrefsPrepare(PrefsSaveData exactPrefs)
    {
        var scope = new PrepareCapture { Previous = _prepare };
        _prepare = scope;
        try
        {
            var owner = _global;
            if (owner == null || owner.Closed || exactPrefs == null || owner.Prefs != exactPrefs.Pointer
                || GlobalSaveData._loaded == null || GlobalSaveData._loaded.Pointer != owner.Global) return scope;
            SyncCampaigns(GlobalSaveData._loaded);
            scope.Global = owner.Global; scope.Prefs = owner.Prefs; scope.OwnerGeneration = owner.Generation;
            if (!RewriteStaged(owner, out _, out var draft)) { FreezeAll(owner); return scope; }
            scope.Document = draft; scope.Raw = owner.ExpectedRaw; scope.Written = draft != null;
            foreach (var version in owner.StagedVersions)
                if (FindCampaign(draft, version.Key.Guid) != null) scope.Versions[version.Key] = version.Value;
        }
        catch { if (_global != null) FreezeAll(_global); }
        return scope;
    }

    // Native Prepare copies srzEntries after the prefix. Only its normal exact
    // finalizer publishes a restoration source; compatibility writes cannot.
    internal static void EndNativePrefsPrepare(PrepareCapture scope, bool normalReturn)
    {
        if (scope == null || scope.Closed) return;
        bool top = ReferenceEquals(_prepare, scope);
        scope.Closed = true;
        if (top) _prepare = scope.Previous;
        var owner = _global;
        if (scope.Global == IntPtr.Zero) return;
        if (normalReturn && top && !scope.Written && scope.Raw == null && scope.Document == null
            && owner != null && owner.ExpectedRaw == null && owner.Staged.Campaigns.Count == 0) return;
        try
        {
            var loaded = GlobalSaveData._loaded;
            if (!normalReturn || !top || !scope.Written || owner == null || owner.Closed
                || scope.Global != owner.Global || scope.OwnerGeneration != owner.Generation
                || scope.Prefs != owner.Prefs || loaded == null || loaded.Pointer != owner.Global
                || loaded.prefs == null || loaded.prefs.Pointer != owner.Prefs
                || scope.Raw == null || owner.ExpectedRaw != scope.Raw
                || loaded.prefs.contents == null || !loaded.prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key)
                || loaded.prefs.contents[HeavyShieldSaveSchema.Key] != scope.Raw
                || !NativeCopyContainsExactKey(loaded.prefs, scope.Raw))
            { if (owner != null && owner.Generation == scope.OwnerGeneration) FreezeAll(owner); return; }
            SyncCampaigns(loaded);
            foreach (var saved in scope.Document.Campaigns)
            {
                bool survived = false;
                foreach (var session in owner.Sessions.Values)
                    if (session.Guid == saved.Guid && session.Slot == saved.Slot) { survived = true; break; }
                if (!survived) { FreezeAll(owner); return; }
            }
            owner.Prepared = scope.Document;
            owner.PreparedVersions.Clear();
            foreach (var version in scope.Versions) owner.PreparedVersions[version.Key] = version.Value;
        }
        catch { if (owner != null && owner.Generation == scope.OwnerGeneration) FreezeAll(owner); }
    }

    internal static SaveCapture BeginNativeIslandSave(int campaignSlot, int land, int challenge)
    {
        var scope = new SaveCapture { Previous = _save, Slot = campaignSlot, Land = land, Challenge = challenge };
        _save = scope;
        try
        {
            TickBinding();
            var owner = _global;
            var session = HeavyShieldIdentity.Current;
            var loaded = GlobalSaveData._loaded;
            var campaign = CampaignSaveData.current;
            var managers = Managers.Inst;
            var game = managers?.game;
            var world = managers?.world;
            var layer = world?.gameLayer;
            int effectiveLand = land >= 0 ? land : (campaign == null ? -1 : campaign.CurrentLand);
            scope.SceneExact = owner != null && !owner.Closed && session != null
                && loaded != null && loaded.Pointer == owner.Global
                && campaign != null && campaign.Pointer == session.Campaign
                && loaded.currentCampaign == campaignSlot && loaded.currentChallenge == challenge
                && game != null && world != null && layer != null
                && game.currentLand == effectiveLand && effectiveLand == session.Land
                && layer.Pointer.ToInt64() == session.World;
            if (!scope.SceneExact) return scope;
            scope.Global = owner.Global; scope.Campaign = session.Campaign;
            scope.Game = game.Pointer; scope.World = world.Pointer; scope.Layer = layer.Pointer;
            scope.Land = effectiveLand; scope.Session = session;
        }
        catch { scope.Conflict = true; }
        return scope;
    }

    internal static void ObserveNativeId(SaveCapture scope, Persistent exactRoot, string nativeId)
    {
        if (scope == null || scope.Closed || !ReferenceEquals(_save, scope)
            || !scope.SceneExact || exactRoot == null || exactRoot.gameObject == null) return;
        try
        {
            var island = IslandSaveData.CurrentlySavingIsland;
            if (island == null || !IslandSaveData.isSavingGame || island.land != scope.Land
                || string.IsNullOrEmpty(nativeId)
                || nativeId.Length > HeavyShieldSaveSchema.MaxNativeIdLength) return;
            if (scope.Island == IntPtr.Zero) scope.Island = island.Pointer;
            else if (scope.Island != island.Pointer) { scope.Conflict = true; return; }
            if (!HeavyShieldIdentity.IsKnownCareerRoot(exactRoot.gameObject)) return;
            HeavyShieldCareerHandle handle;
            var tool = exactRoot.GetComponent<DroppableTool>();
            if (tool != null && HeavyShieldIdentity.TryGetPaidBow(tool, out handle)) { }
            else
            {
                var archer = exactRoot.GetComponent<Archer>();
                if (archer == null || !HeavyShieldIdentity.TryGetSoldier(archer, out handle))
                { scope.Conflict = true; return; }
            }
            if (scope.Ids.TryGetValue(handle.Receipt, out string previous)
                && previous != nativeId) { scope.Conflict = true; return; }
            if (!scope.UniqueIds.Add(nativeId) && !scope.Ids.ContainsKey(handle.Receipt))
            { scope.Conflict = true; return; }
            scope.Ids[handle.Receipt] = nativeId;
        }
        catch { scope.Conflict = true; }
    }

    internal static void ObserveNativeIslandCaptured(SaveCapture scope, IslandSaveData exactIsland)
    {
        if (scope == null || scope.Closed || !ReferenceEquals(_save, scope)
            || exactIsland == null || !scope.SceneExact) return;
        try
        {
            var saving = IslandSaveData.CurrentlySavingIsland;
            if (saving == null || saving.Pointer != exactIsland.Pointer
                || scope.Island != IntPtr.Zero && scope.Island != exactIsland.Pointer
                || exactIsland.land != scope.Land) { scope.Conflict = true; return; }
            scope.Island = exactIsland.Pointer;
            scope.IslandObject = exactIsland;
            scope.MarkerSeen = true;
        }
        catch { scope.Conflict = true; }
    }

    internal static void EndNativeIslandSave(SaveCapture scope, bool normalReturn)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (ReferenceEquals(_save, scope)) _save = scope.Previous;
        var s = scope.Session;
        if (s == null) return;
        try
        {
            // A known-key snapshot mismatch or ambiguous payment is never
            // normalized by a later save. Only this session's earlier exact
            // capture failure may recover from a later full capture.
            if (s.Unknown && !s.CaptureFaultRecoverable) return;
            bool exactIsland = scope.IslandObject != null
                && scope.IslandObject.Pointer == scope.Island;
            if (!HeavyShieldSaveCaptureGate.CanStage(normalReturn, scope.SceneExact,
                    scope.MarkerSeen, scope.Conflict, SameScene(scope), exactIsland)
                || _global == null || _global.Closed
                || !ReferenceEquals(HeavyShieldIdentity.Current, s))
            { Freeze(s, recoverableCapture: true); return; }
            var island = scope.IslandObject;
            if (island == null || island.Pointer != scope.Island
                || island.objects == null) { Freeze(s, recoverableCapture: true); return; }
            var currentlySaving = IslandSaveData.CurrentlySavingIsland;
            if (currentlySaving != null && currentlySaving.Pointer != scope.Island)
            { Freeze(s, recoverableCapture: true); return; }
            foreach (var claim in s.Claims.Values)
            {
                if (!scope.Ids.TryGetValue(claim.Receipt, out string id))
                { Freeze(s, recoverableCapture: true); return; } // missing paid row: preserve old record and quota
                int count = 0;
                foreach (var row in island.objects)
                    if (row != null && row.uniqueID == id) count++;
                if (count != 1) { Freeze(s, recoverableCapture: true); return; }
            }
            string raw = JsonUtility.ToJson(island, false);
            string hash = HeavyShieldSaveCodec.SnapshotHash(s.Guid, s.Challenge, s.Land, raw);
            if (hash == null || !StageExactIsland(_global, s, scope, hash))
            { Freeze(s, recoverableCapture: true); return; }
            s.Phase = HeavyShieldIdentityPhase.Staged;
            s.StageFault = false;
            if (s.CaptureFaultRecoverable) s.Unknown = false;
            s.CaptureFaultRecoverable = false;
        }
        catch { Freeze(s, recoverableCapture: true); }
    }

    internal static LoadCapture BeginNativeIslandLoad(IslandSaveData exactIsland)
    {
        var scope = new LoadCapture { Previous = _load };
        _load = scope;
        try
        {
            TickBinding();
            var s = HeavyShieldIdentity.Current;
            var owner = _global;
            if (s == null || owner == null || owner.Closed || exactIsland == null
                || exactIsland.land < 0) return scope;
            if (s.Claims.Count != 0 || s.Pending.Count != 0 || s.Completed.Count != 0)
                owner.PaidEvidence.Add(s.Guid);
            if (s.Pending.Count != 0)
            { scope.Session = s; scope.Conflict = true; Freeze(s); return scope; }
            bool hasRecord = FindIsland(FindCampaign(owner.Staged, s.Guid), s.Challenge, exactIsland.land) != null;
            if (s.Land >= 0 && (s.Land != exactIsland.land || hasRecord || s.Claims.Count == 0))
                s = ReplaceIslandSession(owner, s, exactIsland.land);
            s.Land = exactIsland.land;
            s.World = WorldKey();
            scope.Island = exactIsland.Pointer; scope.Session = s;
            string raw = JsonUtility.ToJson(exactIsland, false);
            string hash = HeavyShieldSaveCodec.SnapshotHash(s.Guid, s.Challenge, s.Land, raw);
            var result = ResolveBoundIsland(owner, s, hash, out var campaign, out var saved,
                out var sourcePhase);
            scope.SourcePhase = sourcePhase;
            scope.Saved = saved;
            scope.Fresh = result == HeavyShieldSnapshotResolution.ConfirmedFresh;
            scope.Exact = result == HeavyShieldSnapshotResolution.Exact;
            TryCaptureEnrollment(owner, s, exactIsland, scope, result);
            if (result == HeavyShieldSnapshotResolution.Unknown)
            {
                scope.Conflict = true; s.Unknown = true;
                // Keep the newest proven paid occupancy even when this load
                // cannot bind it. Mismatch must never become an empty shop.
                if (campaign != null)
                {
                    s.MoldReceipt = campaign.MoldReceipt;
                    s.LeftExtraReceipt = campaign.LeftExtraReceipt;
                    s.RightExtraReceipt = campaign.RightExtraReceipt;
                }
                if (s.Claims.Count == 0 && campaign != null)
                {
                    s.Quota.BeginVerifiedRestore(campaign?.MoldReceipt != null,
                        campaign?.LeftExtraReceipt != null, campaign?.RightExtraReceipt != null);
                    if (saved != null)
                        foreach (var claim in saved.Claims) HeavyShieldIdentity.RestoreUnboundClaim(claim);
                    s.Quota.FinishVerifiedRestore(false);
                }
                return scope;
            }
            if (saved != null)
                foreach (var claim in saved.Claims)
                    if (claim.Phase is HeavyShieldSavedClaimPhase.PaidToolUnresolved
                        or HeavyShieldSavedClaimPhase.SoldierUnresolved)
                        scope.Conflict = true;
            if (campaign != null)
            {
                s.MoldReceipt = campaign.MoldReceipt;
                s.LeftExtraReceipt = campaign.LeftExtraReceipt;
                s.RightExtraReceipt = campaign.RightExtraReceipt;
            }
            if (!s.Quota.BeginVerifiedRestore(s.MoldReceipt != null,
                    s.LeftExtraReceipt != null, s.RightExtraReceipt != null))
            { scope.Conflict = true; return scope; }
            if (campaign != null)
            {
                foreach (var receipt in new[] { campaign.MoldReceipt, campaign.LeftExtraReceipt,
                             campaign.RightExtraReceipt })
                    if (receipt != null && (!Guid.TryParseExact(receipt, "D", out var id)
                        || !s.Quota.TryRestoreSpentReceipt(id))) scope.Conflict = true;
            }
            if (saved != null && exactIsland.objects != null)
            {
                var wanted = new HashSet<string>(StringComparer.Ordinal);
                foreach (var claim in saved.Claims)
                    if (!string.IsNullOrEmpty(claim.NativeId)) wanted.Add(claim.NativeId);
                foreach (var row in exactIsland.objects)
                    if (row != null && wanted.Contains(row.uniqueID))
                    {
                        if (!scope.FrozenRows.TryAdd(row.uniqueID, row.Pointer)) scope.Conflict = true;
                    }
            }
        }
        catch { scope.Conflict = true; }
        return scope;
    }

    // Root's existing TryCreateOrFind owner enters this tiny scope only for a
    // frozen paid row. Crossbow/Hero recruitment exclusions can read the flag.
    internal static CreateCapture BeginNativeLoadRow(LoadCapture load,
        IslandSaveData.ObjectData exactRow)
    {
        var scope = new CreateCapture { Previous = _create, Load = load,
            MaskScope = CreateMask.Begin() };
        _create = scope;
        try
        {
            if (load == null || exactRow == null || !ReferenceEquals(load, _load)
                || !load.Exact || load.Conflict || load.Saved == null) return scope;
            scope.Row = exactRow.Pointer;
            string id = exactRow.uniqueID;
            foreach (var claim in load.Saved.Claims)
                if (claim.NativeId == id
                    && load.FrozenRows.TryGetValue(claim.NativeId, out var pointer)
                    && pointer == scope.Row)
                { scope.Owned = CreateMask.MarkOwned(scope.MaskScope); break; }
        }
        catch { scope.Owned = false; if (load != null) load.Conflict = true; }
        return scope;
    }

    internal static void EndNativeLoadRow(CreateCapture scope)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (ReferenceEquals(_create, scope)) _create = scope.Previous;
        CreateMask.End(scope.MaskScope);
    }

    internal static void ObserveNativeLoadRow(LoadCapture scope,
        IslandSaveData.ObjectData exactRow, Persistent exactRoot)
    {
        if (scope == null || scope.Closed || !ReferenceEquals(_load, scope)
            || !scope.Exact || scope.Conflict || exactRow == null || exactRoot == null
            || exactRoot.gameObject == null || scope.Saved == null) return;
        try
        {
            if (!scope.FrozenRows.TryGetValue(exactRow.uniqueID, out var frozen)
                || frozen != exactRow.Pointer) return;
            HeavyShieldSavedClaim claim = null;
            foreach (var row in scope.Saved.Claims)
                if (row.NativeId == exactRow.uniqueID) { claim = row; break; }
            if (claim == null || !Guid.TryParseExact(claim.Receipt, "D", out var receipt)
                || !scope.Restored.Add(receipt)) { scope.Conflict = true; return; }
            GameObject root = exactRoot.gameObject;
            bool paid = claim.Phase is HeavyShieldSavedClaimPhase.PaidTool
                or HeavyShieldSavedClaimPhase.PaidToolUnresolved;
            if (MusketeerIdentity.IsMarked(root)
                || HeroRecruitment.HasPurchasedCareer(root.GetComponent<Character>()))
            { scope.Conflict = true; return; }
            if (paid ? root.GetComponent<DroppableTool>() == null
                : root.GetComponent<Archer>() == null) { scope.Conflict = true; return; }
            if (!HeavyShieldIdentity.RestoreClaim(claim, root)) { scope.Conflict = true; return; }
            // Binding happens during native Loading; carrier activation waits
            // until the outer load completed and all native readiness gates pass.
        }
        catch { scope.Conflict = true; }
    }

    internal static void EndNativeIslandLoad(LoadCapture scope, bool success)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        bool top = ReferenceEquals(_load, scope);
        if (top) _load = scope.Previous;
        var s = scope.Session;
        if (s == null) return;
        bool required = scope.EnrollmentRequired || scope.Enrollment != null;
        bool keepBlocked = scope.Enrollment != null && scope.Enrollment.KeepBlocked;
        bool enrolled = scope.Enrollment != null && success && top && TryCommitEnrollment(scope);
        if (enrolled && !scope.Exact && !scope.Fresh && !keepBlocked)
        {
            // The quarantine raised when this exact island had no record is
            // released by the committed first-connect baseline. The committed
            // rows carry no claims; the campaign stays barred from payment by
            // CampaignRestorePending until a real native PrefsPrepare publishes.
            s.Unknown = false;
            s.StageFault = false;
            s.CaptureFaultRecoverable = false;
            s.Phase = HeavyShieldIdentityPhase.Allocated;
            if (!s.Quota.BeginVerifiedRestore(false, false, false) || !s.Quota.FinishVerifiedRestore(true))
                s.Unknown = true;
            return;
        }
        bool allProven = scope.Saved == null || scope.Restored.Count == scope.Saved.Claims.Count;
        if (scope.Saved != null && success)
            foreach (var saved in scope.Saved.Claims)
                if (Guid.TryParseExact(saved.Receipt, "D", out var id)
                    && !scope.Restored.Contains(id))
                    if (!HeavyShieldIdentity.RestoreUnboundClaim(saved)) scope.Conflict = true;
        bool quotaRestored = s.Quota.FinishVerifiedRestore(allProven && !scope.Conflict);
        if ((required && !enrolled) || keepBlocked
            || !success || scope.Conflict || (!scope.Exact && !scope.Fresh)
            || !allProven || !quotaRestored)
        {
            // An active-but-undischarged first-connect responsibility keeps the
            // first purchase closed until a correct lifecycle completes it; the
            // normal Exact/paid paths never take this branch.
            s.Unknown = true;
            foreach (var handle in scope.Attached)
                try { HeavyShieldRuntime.DetachCarrier(handle); } catch { }
            return;
        }
        s.Unknown = false;
        s.Phase = scope.Exact ? scope.SourcePhase : HeavyShieldIdentityPhase.Allocated;
    }

    internal static GenerationCapture BeginNativeGeneration(CampaignSaveData exactCampaign)
    {
        var scope = new GenerationCapture();
        try
        {
            if (exactCampaign == null || exactCampaign.CurrentIsland == null) return scope;
            var island = exactCampaign.CurrentIsland;
            scope.Campaign = exactCampaign.Pointer;
            scope.Island = island.Pointer;
            scope.Land = island.land;
            scope.WasNew = island.isNew && island.playTimeDays == 0.0;
            scope.World = WorldKey();
            var loaded = GlobalSaveData._loaded;
            scope.Challenge = loaded == null ? -1 : loaded.currentChallenge;
        }
        catch { }
        return scope;
    }

    internal static void EndNativeGeneration(GenerationCapture scope,
        CampaignSaveData exactCampaign, bool normalReturn)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (!normalReturn || !scope.WasNew || exactCampaign == null
            || exactCampaign.Pointer != scope.Campaign) return;
        try
        {
            TickBinding();
            var s = HeavyShieldIdentity.Current;
            var island = exactCampaign.CurrentIsland;
            if (s == null || s.Campaign != scope.Campaign || island == null
                || island.Pointer != scope.Island || island.land != scope.Land
                || WorldKey() != scope.World || s.Challenge != scope.Challenge) return;
            // A previously staged known island never becomes fresh through isNew.
            var record = FindCampaign(_global.Staged, s.Guid);
            if (record != null && record.Islands.Exists(x => x.Land == scope.Land && x.Challenge == scope.Challenge))
            { s.Unknown = true; return; }
            if (s.Land != scope.Land) s = ReplaceIslandSession(_global, s, scope.Land);
            s.Land = scope.Land; s.World = scope.World;
            var entitlement = FindCampaign(_global.Prepared, s.Guid)
                ?? (_global.LoadedByNative.TryGetValue(s.Campaign, out var nativeRecord) ? nativeRecord : null);
            if (entitlement != null)
            {
                s.MoldReceipt = entitlement.MoldReceipt;
                s.LeftExtraReceipt = entitlement.LeftExtraReceipt;
                s.RightExtraReceipt = entitlement.RightExtraReceipt;
            }
            if (!s.Quota.BeginVerifiedRestore(s.MoldReceipt != null,
                    s.LeftExtraReceipt != null, s.RightExtraReceipt != null)
                || !s.Quota.FinishVerifiedRestore(true)) { s.Unknown = true; return; }
            // Verified generation success point: a provably unpaid campaign now
            // registers its other already-visited baselines before the first Gem.
            // The new island itself stays owned by this generation boundary.
            if (!TryEnrollGenerationBaseline(s, island, exactCampaign))
            {
                // Active first-connect responsibility could not be discharged:
                // keep the first purchase closed until a correct lifecycle.
                s.Unknown = true;
                return;
            }
            s.Unknown = false;
            s.Phase = HeavyShieldIdentityPhase.Allocated;
        }
        catch { }
    }

    // Before the first payment, prove this native Prefs dictionary can hold and
    // return our key. Never classify a failed read/write as ConfirmedMissing.
    internal static bool IsNativeKeyCurrent(HeavyShieldIdentity.Session s)
    {
        var owner = _global;
        try
        {
            var loaded = GlobalSaveData._loaded;
            var prefs = loaded?.prefs;
            return s != null && owner != null && !owner.Closed
                && owner.KeyRead != HeavyShieldNativeKeyRead.Failed
                && s.Global == owner.Global && s.OwnerGeneration == owner.Generation
                && loaded != null && loaded.Pointer == owner.Global
                && prefs != null && prefs.Pointer == owner.Prefs && prefs.contents != null
                && owner.ExpectedRaw != null && prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key)
                && prefs.contents[HeavyShieldSaveSchema.Key] == owner.ExpectedRaw;
        }
        catch { return false; }
    }

    internal static bool PreflightKeyWrite(HeavyShieldIdentity.Session s)
    {
        var owner = _global;
        if (owner == null || owner.Closed || s == null || s.Unknown || s.StageFault
            || owner.Global != s.Global || GlobalSaveData._loaded == null
            || GlobalSaveData._loaded.Pointer != owner.Global) return false;
        try
        {
            var prefs = GlobalSaveData._loaded.prefs;
            var contents = prefs?.contents;
            if (prefs == null || prefs.Pointer != owner.Prefs || contents == null) return false;
            bool found = contents.ContainsKey(HeavyShieldSaveSchema.Key);
            string old = found ? contents[HeavyShieldSaveSchema.Key] : null;
            if (found && old == null) return false;
            if (owner.ExpectedRaw != null && (!found || old != owner.ExpectedRaw))
            { Freeze(s); return false; }
            // A key inserted after a confirmed-missing load is not a parsed saved source.
            if (found && owner.ExpectedRaw == null) { Freeze(s); return false; }
            if (owner.KeyRead == HeavyShieldNativeKeyRead.Present && old != owner.OriginalRaw
                && !owner.KeyPreflighted) return false;
            string json;
            if (found && owner.KeyRead == HeavyShieldNativeKeyRead.Present && owner.StagedVersions.Count == 0)
            {
                // Parsing v1 produces a v2 in-memory document; a payment
                // preflight must retain the saved raw bytes until normal Stage/Prepare.
                if (!HeavyShieldSaveCodec.TryValidate(owner.Staged, out _)) return false;
                json = old;
            }
            else if (!HeavyShieldSaveCodec.TrySerialize(owner.Staged, out json, out _)) return false;
            // Existing exact key gets an idempotent write. New key contains only
            // previously staged state, never the current live transaction.
            prefs.SetString(HeavyShieldSaveSchema.Key, json);
            if (!contents.ContainsKey(HeavyShieldSaveSchema.Key)
                || contents[HeavyShieldSaveSchema.Key] != json)
            {
                if (found) TryRestoreOld(prefs, old);
                Freeze(s); return false;
            }
            owner.KeyPreflighted = true;
            owner.ExpectedRaw = json;
            s.KeyReady = true;
            return true;
        }
        catch { Freeze(s); return false; }
    }

    private static void BindGlobal(GlobalSaveData loaded)
    {
        var owner = new GlobalBinding { Global = loaded.Pointer, Generation = ++_nextGeneration };
        _global = owner;
        try
        {
            var prefs = loaded.prefs;
            var contents = prefs?.contents;
            if (prefs == null || contents == null) { owner.Closed = true; return; }
            owner.Prefs = prefs.Pointer;
            bool exists = contents.ContainsKey(HeavyShieldSaveSchema.Key);
            owner.KeyPresent = exists;
            owner.KeyRead = exists ? HeavyShieldNativeKeyRead.Present
                : HeavyShieldNativeKeyRead.ConfirmedMissing;
            if (exists)
            {
                string raw = contents[HeavyShieldSaveSchema.Key];
                if (raw == null || !HeavyShieldSaveCodec.TryParse(raw, out owner.Loaded, out _))
                { owner.Closed = true; owner.KeyRead = HeavyShieldNativeKeyRead.Failed; return; }
                owner.OriginalRaw = raw;
                owner.ExpectedRaw = raw;
                owner.Staged = Clone(owner.Loaded);
            }
            else owner.Staged = new HeavyShieldSaveDocument();
            SyncCampaigns(loaded, initial: true);
        }
        catch { owner.Closed = true; owner.KeyRead = HeavyShieldNativeKeyRead.Failed; }
    }

    private static void SyncCampaigns(GlobalSaveData loaded, bool initial = false)
    {
        var owner = _global;
        if (owner == null || owner.Closed || loaded == null || loaded.Pointer != owner.Global) return;
        var present = new HashSet<IntPtr>();
        var campaigns = loaded.campaigns;
        if (campaigns == null) { owner.Closed = true; return; }
        for (int i = 0; i < campaigns.Count; i++)
        {
            var native = campaigns[i];
            if (native == null || native.Pointer == IntPtr.Zero) continue;
            present.Add(native.Pointer);
            if (owner.Sessions.TryGetValue(native.Pointer, out var existing))
            { existing.Slot = i; continue; }
            HeavyShieldSavedCampaign saved = null;
            if (initial && owner.Loaded != null)
                foreach (var row in owner.Loaded.Campaigns)
                    if (row.Slot == i) { saved = row; break; }
            var session = new HeavyShieldIdentity.Session
            {
                Global = owner.Global, Campaign = native.Pointer,
                OwnerGeneration = owner.Generation, Slot = i,
                Guid = saved?.Guid ?? HeavyShieldSaveCodec.NewGuid(),
                Challenge = loaded.currentChallenge,
                Phase = saved == null ? HeavyShieldIdentityPhase.Allocated
                    : HeavyShieldIdentityPhase.Staged,
                MoldReceipt = saved?.MoldReceipt,
                LeftExtraReceipt = saved?.LeftExtraReceipt,
                RightExtraReceipt = saved?.RightExtraReceipt,
            };
            owner.Sessions[native.Pointer] = session;
            if (saved != null) owner.LoadedByNative[native.Pointer] = saved;
        }
        var removed = new List<IntPtr>();
        foreach (var pointer in owner.Sessions.Keys)
            if (!present.Contains(pointer)) removed.Add(pointer);
        foreach (var pointer in removed)
        { owner.Sessions.Remove(pointer); owner.LoadedByNative.Remove(pointer); }
    }

    private static HeavyShieldIdentity.Session ReplaceIslandSession(GlobalBinding owner,
        HeavyShieldIdentity.Session old, int land)
    {
        var next = new HeavyShieldIdentity.Session
        {
            Global = old.Global, Campaign = old.Campaign,
            OwnerGeneration = old.OwnerGeneration, Slot = old.Slot,
            Guid = old.Guid, Challenge = old.Challenge, Land = land,
            World = WorldKey(), Phase = old.Phase,
            MoldReceipt = old.MoldReceipt, LeftExtraReceipt = old.LeftExtraReceipt,
            RightExtraReceipt = old.RightExtraReceipt,
        };
        owner.Sessions[old.Campaign] = next;
        HeavyShieldIdentity.Install(next);
        return next;
    }

    private static bool StageExactIsland(GlobalBinding owner, HeavyShieldIdentity.Session s,
        SaveCapture capture, string hash)
    {
        // A deleted campaign's staged row must be removed before a newly
        // created native campaign can occupy that same slot. Only exact native
        // object survivors carry their existing GUID and entitlements.
        var surviving = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var live in owner.Sessions.Values) surviving[live.Guid] = live.Slot;
        if (!HeavyShieldSaveCodec.TryReconcileStaged(owner.Staged, surviving,
                out var draft)) return false;
        HeavyShieldSavedCampaign campaign = null;
        foreach (var row in draft.Campaigns)
            if (row.Guid == s.Guid) { campaign = row; break; }
        if (campaign == null)
        {
            campaign = new HeavyShieldSavedCampaign { Guid = s.Guid, Slot = s.Slot };
            draft.Campaigns.Add(campaign);
        }
        campaign.Slot = s.Slot;
        campaign.MoldReceipt = s.MoldReceipt;
        campaign.LeftExtraReceipt = s.LeftExtraReceipt;
        campaign.RightExtraReceipt = s.RightExtraReceipt;
        HeavyShieldSavedIsland island = null;
        foreach (var row in campaign.Islands)
            if (row.Challenge == s.Challenge && row.Land == s.Land) { island = row; break; }
        if (island == null)
        {
            island = new HeavyShieldSavedIsland { Challenge = s.Challenge, Land = s.Land };
            campaign.Islands.Add(island);
        }
        island.SnapshotHash = hash;
        island.Claims.Clear();
        foreach (var claim in s.Claims.Values)
        {
            if (!capture.Ids.TryGetValue(claim.Receipt, out string id)) return false;
            island.Claims.Add(new HeavyShieldSavedClaim
            {
                Receipt = claim.Receipt.ToString("D"), Side = claim.Side,
                Phase = claim.Phase, NativeId = id,
                Durability = claim.Combat.Durability,
                PendingBreak = claim.Combat.PendingBreak,
                RetirementUnknown = claim.Combat.RetirementUnknown,
            });
        }
        if (!HeavyShieldSaveCodec.TryValidate(draft, out _)) return false;
        owner.Staged = draft; // Staging protocol 1 of 2 (exact save capture); enrollment is protocol 2.
        owner.StagedVersions[(s.Guid, s.Challenge, s.Land)] = ++owner.NextStage;
        return true;
    }

    private static bool RewriteStaged(GlobalBinding owner, out string reason, out HeavyShieldSaveDocument prepared)
    {
        reason = null;
        prepared = null;
        var loaded = GlobalSaveData._loaded;
        if (loaded == null || loaded.Pointer != owner.Global || loaded.prefs == null
            || loaded.prefs.Pointer != owner.Prefs) { reason = "owner"; return false; }
        var surviving = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var session in owner.Sessions.Values) surviving[session.Guid] = session.Slot;
        if (!HeavyShieldSaveCodec.TryReconcileStaged(owner.Staged, surviving, out var draft))
        { reason = "reconcile"; return false; }
        if (draft.Campaigns.Count == 0 && !owner.KeyPresent && !owner.KeyPreflighted)
            return true;
        if (!HeavyShieldSaveCodec.TrySerialize(draft, out string json, out reason)) return false;
        var prefs = loaded.prefs;
        var contents = prefs.contents;
        bool found = contents.ContainsKey(HeavyShieldSaveSchema.Key);
        string old = found ? contents[HeavyShieldSaveSchema.Key] : null;
        if (owner.ExpectedRaw != null && (!found || old != owner.ExpectedRaw))
        { reason = "key-changed"; return false; }
        try
        {
            prefs.SetString(HeavyShieldSaveSchema.Key, json);
            if (!contents.ContainsKey(HeavyShieldSaveSchema.Key)
                || contents[HeavyShieldSaveSchema.Key] != json)
            { if (found) TryRestoreOld(prefs, old); reason = "readback"; return false; }
            owner.KeyPreflighted = true;
            owner.ExpectedRaw = json;
            // The draft is an isolated codec clone. The whole native Prepare
            // finalizer, after srzEntries copying, decides whether to publish it.
            prepared = draft;
            return true;
        }
        catch { if (found) TryRestoreOld(prefs, old); reason = "set"; return false; }
    }

    private static void ActivateBoundSoldiers(HeavyShieldIdentity.Session s)
    {
        if (s == null || s.Unknown || s.StageFault || !HeavyShieldRuntime.CarrierPreflightReady) return;
        int count = 0;
        foreach (var claim in s.Claims.Values)
        {
            if (claim.Phase != HeavyShieldSavedClaimPhase.Soldier || ++count > HeavyShieldSaveSchema.MaxClaimsPerIsland) continue;
            if (claim.NativeDeathProven || claim.NativeDemotionProven
                || ((claim.Combat.RetirementUnknown || claim.Combat.PendingBreak) && !claim.InitialActivationPending)
                || HeavyShieldRuntime.IsCarrierActive(claim.Handle)) continue;
            if (Time.time < claim.NextActivationAt) continue;
            try
            {
                var root = claim.RootObject;
                var archer = root?.GetComponent<Archer>();
                if (root == null || !root.activeInHierarchy || archer == null
                    || !HeavyShieldIdentity.ValidateCareer(claim.Handle))
                { Freeze(s); return; }
                var result = HeavyShieldRuntime.TryAttachCarrier(archer, claim.Handle, claim.Combat);
                if (result == HeavyShieldRuntime.AttachResult.Deferred)
                { claim.NextActivationAt = Time.time + 0.2f; continue; }
                claim.InitialActivationPending = false;
                claim.NextActivationAt = 0f;
                if (result != HeavyShieldRuntime.AttachResult.Attached) { Freeze(s); return; }
            }
            catch { Freeze(s); return; }
        }
    }

    private static bool NativeCopyContainsExactKey(PrefsSaveData prefs, string raw)
    {
        var entries = prefs.srzEntries;
        if (entries == null) return false;
        int count = 0;
        foreach (var entry in entries)
            if (entry != null && entry.key == HeavyShieldSaveSchema.Key)
            {
                if (++count != 1 || entry.val != raw) return false;
            }
        return count == 1;
    }

    private static void TryRestoreOld(PrefsSaveData prefs, string old)
    { try { if (old != null) prefs.SetString(HeavyShieldSaveSchema.Key, old); } catch { } }

    private static HeavyShieldSavedCampaign FindCampaign(HeavyShieldSaveDocument document, string guid)
    {
        if (document?.Campaigns == null) return null;
        foreach (var campaign in document.Campaigns) if (campaign.Guid == guid) return campaign;
        return null;
    }

    private static HeavyShieldSavedIsland FindIsland(HeavyShieldSavedCampaign campaign, int challenge, int land)
    {
        if (campaign?.Islands == null) return null;
        foreach (var island in campaign.Islands)
            if (island.Challenge == challenge && island.Land == land) return island;
        return null;
    }

    // A whole immutable Prepared campaign must dominate every stage of its
    // own GUID. Different islands may retain different monotonic revisions;
    // they do not need to share the event number of the most recent capture.
    internal static bool CampaignRestorePending(HeavyShieldIdentity.Session s)
    {
        var owner = _global;
        if (s == null || owner == null || owner.Closed || owner.Global != s.Global
            || owner.Generation != s.OwnerGeneration) return false;
        foreach (var stage in owner.StagedVersions)
            if (stage.Key.Guid == s.Guid && (!owner.PreparedVersions.TryGetValue(stage.Key, out long prepared)
                || prepared < stage.Value)) return true;
        return false;
    }

    // Same GUID + native instance is the lineage. Slots only serialize order.
    // A newer staged island masks startup even when unprepared/mismatched;
    // only a matching successfully prepared version can restore as Staged.
    private static HeavyShieldSnapshotResolution ResolveBoundIsland(GlobalBinding owner,
        HeavyShieldIdentity.Session s, string hash, out HeavyShieldSavedCampaign campaign,
        out HeavyShieldSavedIsland island, out HeavyShieldIdentityPhase sourcePhase)
    {
        campaign = FindCampaign(owner.Prepared, s.Guid);
        island = null;
        sourcePhase = HeavyShieldIdentityPhase.Unavailable;
        bool pending = CampaignRestorePending(s);
        bool stagedCampaign = false;
        foreach (var stage in owner.StagedVersions)
            if (stage.Key.Guid == s.Guid) { stagedCampaign = true; break; }
        if (pending)
        {
            // Quarantine retains the newest paid responsibility, but cannot
            // authorize a role or quote another charge from this draft.
            campaign = FindCampaign(owner.Staged, s.Guid);
            island = FindIsland(campaign, s.Challenge, s.Land);
        }
        else if (campaign != null)
            island = FindIsland(campaign, s.Challenge, s.Land);
        else if (owner.LoadedByNative.TryGetValue(s.Campaign, out var native)
            && native.Guid == s.Guid)
        {
            campaign ??= native;
            island = FindIsland(native, s.Challenge, s.Land);
        }
        try
        {
            var prefs = GlobalSaveData._loaded?.prefs;
            if (owner.KeyRead == HeavyShieldNativeKeyRead.Failed || prefs == null
                || prefs.Pointer != owner.Prefs || prefs.contents == null) return HeavyShieldSnapshotResolution.Unknown;
            bool found = prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key);
            if (owner.ExpectedRaw != null && (!found || prefs.contents[HeavyShieldSaveSchema.Key] != owner.ExpectedRaw))
                return HeavyShieldSnapshotResolution.Unknown;
            if (pending) return HeavyShieldSnapshotResolution.Unknown;
            if (stagedCampaign)
            {
                sourcePhase = HeavyShieldIdentityPhase.Staged;
            }
            else if (island != null) sourcePhase = HeavyShieldIdentityPhase.Loaded;
            else
            {
                bool evidence = FindCampaign(owner.Staged, s.Guid) != null || owner.PaidEvidence.Contains(s.Guid) || s.Claims.Count != 0
                    || s.Pending.Count != 0 || s.Completed.Count != 0;
                return owner.KeyRead == HeavyShieldNativeKeyRead.ConfirmedMissing && !evidence
                    ? HeavyShieldSnapshotResolution.ConfirmedFresh : HeavyShieldSnapshotResolution.Unknown;
            }
            return island != null && island.SnapshotHash == hash
                ? HeavyShieldSnapshotResolution.Exact : HeavyShieldSnapshotResolution.Unknown;
        }
        catch { return HeavyShieldSnapshotResolution.Unknown; }
    }

    // ---------- first-connect baseline enrollment (issue 167) ----------
    // The authoritative no-purchase proof: every campaign copy and the live
    // session must carry no mold/extra receipt and no claim anywhere. TryValidate
    // already forces every claim/extra to depend on MoldReceipt, so this is exact
    // inside the mod-owned durable document; it deliberately does not claim any
    // proof about unrecorded history.

    private static bool CanProveNeverPaid(GlobalBinding owner, HeavyShieldIdentity.Session s)
    {
        if (owner == null || s == null) return false;
        if (s.MoldReceipt != null || s.LeftExtraReceipt != null || s.RightExtraReceipt != null) return false;
        if (PurchaseRecorded(FindCampaign(owner.Loaded, s.Guid))) return false;
        if (PurchaseRecorded(FindCampaign(owner.Prepared, s.Guid))) return false;
        if (PurchaseRecorded(FindCampaign(owner.Staged, s.Guid))) return false;
        return !PurchaseRecorded(LoadedCopy(owner, s));
    }

    private static bool PurchaseRecorded(HeavyShieldSavedCampaign campaign)
    {
        if (campaign == null) return false;
        if (campaign.MoldReceipt != null || campaign.LeftExtraReceipt != null
            || campaign.RightExtraReceipt != null) return true;
        if (campaign.Islands != null)
            foreach (var island in campaign.Islands)
                if (island != null && island.Claims != null && island.Claims.Count != 0) return true;
        return false;
    }

    private static HeavyShieldSavedCampaign LoadedCopy(GlobalBinding owner, HeavyShieldIdentity.Session s)
    {
        if (owner == null || s == null) return null;
        return owner.LoadedByNative.TryGetValue(s.Campaign, out var native) && native.Guid == s.Guid
            ? native : null;
    }

    private static bool HasKnownIslandRow(GlobalBinding owner, HeavyShieldIdentity.Session s, int land)
        => FindIsland(FindCampaign(owner.Loaded, s.Guid), s.Challenge, land) != null
            || FindIsland(FindCampaign(owner.Prepared, s.Guid), s.Challenge, land) != null
            || FindIsland(FindCampaign(owner.Staged, s.Guid), s.Challenge, land) != null
            || FindIsland(LoadedCopy(owner, s), s.Challenge, land) != null;

    // Load prefix: freeze the candidate; write nothing here. Once the unpaid proof
    // holds, the first-connect responsibility is established immediately; every
    // later early return keeps it until an explicit NoEnrollmentNeeded or a
    // successful commit releases it.
    private static void TryCaptureEnrollment(GlobalBinding owner, HeavyShieldIdentity.Session s,
        IslandSaveData island, LoadCapture scope, HeavyShieldSnapshotResolution result)
    {
        try
        {
            if (owner == null || owner.Closed || s == null || island == null || scope == null) return;
            // Already paid or already barred states keep their original rules.
            if (s.StageFault || s.CaptureFaultRecoverable
                || s.Claims.Count != 0 || s.Pending.Count != 0 || s.Completed.Count != 0) return;
            if (owner.PaidEvidence.Contains(s.Guid) || CampaignRestorePending(s)) return;
            if (!CanProveNeverPaid(owner, s)) return;
            scope.EnrollmentRequired = true;
            var game = Managers.Inst?.game;
            var campaign = CampaignSaveData.current;
            bool contextOk = game != null && campaign != null && campaign.Pointer == s.Campaign
                && campaign.CurrentIsland != null && campaign.CurrentIsland.Pointer == island.Pointer
                && game.currentLand == island.land && island.land >= 0 && island.land == s.Land;
            bool currentMissing = contextOk && !HasKnownIslandRow(owner, s, island.land);
            if (contextOk)
            {
                if (result == HeavyShieldSnapshotResolution.Unknown)
                { if (!currentMissing) return; }
                else if (result != HeavyShieldSnapshotResolution.Exact
                    && result != HeavyShieldSnapshotResolution.ConfirmedFresh) return;
            }
            var copies = new[]
            {
                FindCampaign(owner.Loaded, s.Guid), FindCampaign(owner.Prepared, s.Guid),
                FindCampaign(owner.Staged, s.Guid), LoadedCopy(owner, s),
            };
            var outcome = HeavyShieldEnrollment.TryFreezeRows(campaign, island, s.Guid, s.Challenge,
                copies, out var candidate, out _);
            if (outcome == HeavyShieldEnrollment.Outcome.NoEnrollmentNeeded)
            {
                // The bounded read proved there is nothing to register.
                scope.EnrollmentRequired = false;
                return;
            }
            if (outcome != HeavyShieldEnrollment.Outcome.Frozen
                || candidate == null || candidate.Rows.Count == 0) return;
            // Commit-token preconditions: key phase and the frozen context must be
            // exact; any divergence leaves the established responsibility standing.
            var loaded = GlobalSaveData._loaded;
            var prefs = loaded?.prefs;
            if (!contextOk || loaded == null || loaded.Pointer != owner.Global || prefs == null
                || prefs.Pointer != owner.Prefs || prefs.contents == null
                || loaded.currentCampaign != s.Slot || loaded.currentChallenge != s.Challenge) return;
            bool found = prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key);
            string raw = found ? prefs.contents[HeavyShieldSaveSchema.Key] : null;
            if (owner.KeyRead == HeavyShieldNativeKeyRead.Present)
            {
                if (owner.ExpectedRaw == null || !found || raw != owner.ExpectedRaw) return;
            }
            else if (owner.KeyRead == HeavyShieldNativeKeyRead.ConfirmedMissing)
            {
                // A key inserted after a confirmed-missing bind is not this path.
                if (found || owner.ExpectedRaw != null || owner.KeyPreflighted) return;
            }
            else return;
            // The current member cannot be enrolled at this boundary (e.g. a
            // generation target or a never-played slot): the other frozen rows
            // still register, but the session stays barred until a correct
            // lifecycle registers the current island too.
            bool keepBlocked = currentMissing && !candidate.IncludesCurrent;
            if (!TryValidateEnrollmentDraft(owner, s, candidate.Rows)) return;
            var capture = new EnrollmentCapture
            {
                Global = owner.Global, Prefs = owner.Prefs, Campaign = s.Campaign,
                Island = island.Pointer, OwnerGeneration = owner.Generation,
                World = s.World, Land = s.Land, Challenge = s.Challenge, Slot = s.Slot,
                Guid = s.Guid, KeyRead = owner.KeyRead, KeyRaw = raw, CurrentMissing = currentMissing,
                IncludesCurrent = candidate.IncludesCurrent, KeepBlocked = keepBlocked,
                RequireCurrentSlot = true,
            };
            capture.Rows.AddRange(candidate.Rows);
            scope.Enrollment = capture;
        }
        catch { }
    }

    private static bool TryCommitEnrollment(LoadCapture scope)
        => scope != null && CommitEnrollmentToken(_global, scope.Session, scope.Enrollment);

    // Shared commit gate for both first-connect boundaries (native island pop and
    // verified new generation). The frozen rows are applied to a clone; the staged
    // state is assigned once, only if every condition still holds.
    private static bool CommitEnrollmentToken(GlobalBinding owner, HeavyShieldIdentity.Session s,
        EnrollmentCapture token)
    {
        try
        {
            if (token == null || owner == null || s == null || owner.Closed) return false;
            if (owner.Generation != token.OwnerGeneration || owner.Global != token.Global
                || owner.Prefs != token.Prefs) return false;
            var loaded = GlobalSaveData._loaded;
            if (loaded == null || loaded.Pointer != token.Global
                || !ReferenceEquals(HeavyShieldIdentity.Current, s)) return false;
            var prefs = loaded.prefs;
            if (prefs == null || prefs.Pointer != token.Prefs || prefs.contents == null) return false;
            bool found = prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key);
            if (token.KeyRead == HeavyShieldNativeKeyRead.Present)
            { if (!found || prefs.contents[HeavyShieldSaveSchema.Key] != token.KeyRaw) return false; }
            else if (token.KeyRead == HeavyShieldNativeKeyRead.ConfirmedMissing)
            { if (found) return false; }
            else return false;
            if (s.Campaign != token.Campaign || s.Guid != token.Guid || s.Slot != token.Slot
                || s.Challenge != token.Challenge || s.Land != token.Land) return false;
            // Prefix-frozen world identity: a same-owner refresh by TickBinding must
            // not launder a world replacement.
            if (token.World == 0 || s.World != token.World || WorldKey() != token.World) return false;
            if (s.StageFault || s.CaptureFaultRecoverable
                || s.Claims.Count != 0 || s.Pending.Count != 0 || s.Completed.Count != 0) return false;
            if (owner.PaidEvidence.Contains(s.Guid) || CampaignRestorePending(s)) return false;
            if (loaded.currentCampaign != s.Slot || loaded.currentChallenge != s.Challenge) return false;
            if (!CanProveNeverPaid(owner, s)) return false;
            var campaign = CampaignSaveData.current;
            var game = Managers.Inst?.game;
            if (campaign == null || game == null || campaign.Pointer != token.Campaign
                || campaign.CurrentIsland == null || campaign.CurrentIsland.Pointer != token.Island
                || game.currentLand != token.Land) return false;
            if (HeavyShieldEnrollment.TryCurrentSlot(campaign, campaign.CurrentIsland, out int currentSlot))
            { if (currentSlot != token.Land) return false; }
            else if (token.RequireCurrentSlot) return false;
            if (!HeavyShieldEnrollment.TryVerifyRows(campaign, campaign.CurrentIsland, s.Guid,
                    s.Challenge, token.Rows, out _)) return false;
            if (!HeavyShieldSaveCodec.TrySerialize(owner.Staged, out string json, out _)
                || !HeavyShieldSaveCodec.TryParse(json, out var draft, out _)) return false;
            if (!ApplyEnrollmentRows(draft, s, token.Rows, out bool changed) || !changed) return false;
            if (!HeavyShieldSaveCodec.TrySerialize(draft, out _, out _)) return false;
            owner.Staged = draft;
            long version = ++owner.NextStage;
            foreach (var row in token.Rows)
                owner.StagedVersions[(s.Guid, s.Challenge, row.Land)] = version;
            return true;
        }
        catch { return false; }
    }

    // Verified new-generation boundary variant: capture and commit in one call at
    // the already-verified lifecycle point. Returns true only for an explicit paid
    // record (original rules), a bounded read that proves nothing to register, or
    // a successful commit; every context/read/key failure returns false so the
    // caller keeps the first purchase closed until a correct lifecycle completes
    // the responsibility. The outer catch never reports success.
    private static bool TryEnrollGenerationBaseline(HeavyShieldIdentity.Session s,
        IslandSaveData island, CampaignSaveData campaign)
    {
        try
        {
            var owner = _global;
            if (owner == null || owner.Closed || s == null || island == null || campaign == null) return false;
            // An explicit legitimate paid record belongs to the original rules.
            if (!CanProveNeverPaid(owner, s) || owner.PaidEvidence.Contains(s.Guid)) return true;
            var copies = new[]
            {
                FindCampaign(owner.Loaded, s.Guid), FindCampaign(owner.Prepared, s.Guid),
                FindCampaign(owner.Staged, s.Guid), LoadedCopy(owner, s),
            };
            var outcome = HeavyShieldEnrollment.TryFreezeRows(campaign, island, s.Guid, s.Challenge,
                copies, out var candidate, out _);
            if (outcome == HeavyShieldEnrollment.Outcome.NoEnrollmentNeeded) return true;
            if (outcome != HeavyShieldEnrollment.Outcome.Frozen
                || candidate == null || candidate.Rows.Count == 0) return false;
            if (candidate.IncludesCurrent) return false;
            var loaded = GlobalSaveData._loaded;
            var prefs = loaded?.prefs;
            if (loaded == null || loaded.Pointer != owner.Global || prefs == null
                || prefs.Pointer != owner.Prefs || prefs.contents == null
                || loaded.currentCampaign != s.Slot || loaded.currentChallenge != s.Challenge) return false;
            bool found = prefs.contents.ContainsKey(HeavyShieldSaveSchema.Key);
            string raw = found ? prefs.contents[HeavyShieldSaveSchema.Key] : null;
            if (owner.KeyRead == HeavyShieldNativeKeyRead.Present)
            { if (owner.ExpectedRaw == null || !found || raw != owner.ExpectedRaw) return false; }
            else if (owner.KeyRead == HeavyShieldNativeKeyRead.ConfirmedMissing)
            { if (found || owner.ExpectedRaw != null || owner.KeyPreflighted) return false; }
            else return false;
            var token = new EnrollmentCapture
            {
                Global = owner.Global, Prefs = owner.Prefs, Campaign = s.Campaign,
                Island = island.Pointer, OwnerGeneration = owner.Generation,
                World = s.World, Land = s.Land, Challenge = s.Challenge, Slot = s.Slot,
                Guid = s.Guid, KeyRead = owner.KeyRead, KeyRaw = raw, CurrentMissing = true,
                RequireCurrentSlot = false,
            };
            token.Rows.AddRange(candidate.Rows);
            return CommitEnrollmentToken(owner, s, token);
        }
        catch { return false; }
    }

    // Adds only missing rows to a cloned draft; an existing row must match or the
    // whole event aborts. Stored hashes and every other campaign stay untouched.
    private static bool ApplyEnrollmentRows(HeavyShieldSaveDocument draft,
        HeavyShieldIdentity.Session s, List<HeavyShieldEnrollment.Row> rows, out bool changed)
    {
        changed = false;
        if (draft == null || s == null || rows == null || rows.Count == 0) return false;
        var campaign = FindCampaign(draft, s.Guid);
        if (campaign == null)
        {
            campaign = new HeavyShieldSavedCampaign { Guid = s.Guid, Slot = s.Slot };
            draft.Campaigns.Add(campaign);
        }
        foreach (var row in rows)
        {
            HeavyShieldSavedIsland existing = null;
            foreach (var island in campaign.Islands)
                if (island.Challenge == s.Challenge && island.Land == row.Land)
                { existing = island; break; }
            if (existing != null)
            {
                if (existing.SnapshotHash != row.SnapshotHash) return false;
                continue;
            }
            campaign.Islands.Add(new HeavyShieldSavedIsland
            { Challenge = s.Challenge, Land = row.Land, SnapshotHash = row.SnapshotHash });
            changed = true;
        }
        return true;
    }

    private static bool TryValidateEnrollmentDraft(GlobalBinding owner, HeavyShieldIdentity.Session s,
        List<HeavyShieldEnrollment.Row> rows)
    {
        if (owner?.Staged == null) return false;
        if (!HeavyShieldSaveCodec.TrySerialize(owner.Staged, out string json, out _)
            || !HeavyShieldSaveCodec.TryParse(json, out var draft, out _)) return false;
        if (!ApplyEnrollmentRows(draft, s, rows, out _)) return false;
        return HeavyShieldSaveCodec.TrySerialize(draft, out _, out _);
    }

    private static HeavyShieldSaveDocument Clone(HeavyShieldSaveDocument source)
    {
        if (!HeavyShieldSaveCodec.TrySerialize(source, out string json, out _)
            || !HeavyShieldSaveCodec.TryParse(json, out var clone, out _)) return null;
        return clone;
    }

    private static bool SameScene(SaveCapture scope)
    {
        try
        {
            var loaded = GlobalSaveData._loaded;
            var campaign = CampaignSaveData.current;
            var managers = Managers.Inst;
            return loaded != null && loaded.Pointer == scope.Global
                && campaign != null && campaign.Pointer == scope.Campaign
                && loaded.currentCampaign == scope.Slot && loaded.currentChallenge == scope.Challenge
                && managers?.game != null && managers.game.Pointer == scope.Game
                && managers.game.currentLand == scope.Land
                && managers.world != null && managers.world.Pointer == scope.World
                && managers.world.gameLayer != null
                && managers.world.gameLayer.Pointer == scope.Layer;
        }
        catch { return false; }
    }

    private static long WorldKey()
    {
        try { return Managers.Inst?.world?.gameLayer?.Pointer.ToInt64() ?? 0; }
        catch { return 0; }
    }

    private static void Freeze(HeavyShieldIdentity.Session s, bool recoverableCapture = false)
    {
        if (s == null) return;
        s.CaptureFaultRecoverable = recoverableCapture
            && (!s.Unknown || s.CaptureFaultRecoverable);
        s.StageFault = true;
        s.Unknown = true;
    }

    private static void FreezeAll(GlobalBinding owner)
    { foreach (var s in owner.Sessions.Values) Freeze(s); }
}
