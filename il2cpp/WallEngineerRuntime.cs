using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Single owner of the wall-engineer feature state for the current world/scene. At most one
/// OwnedWall per side (kingdom.outerWall[Side.Left/Right] -> GameObject -> Wall), each holding the
/// original native B, the last written multiplier, and up to two engineer danger caches. No
/// persistence, no shadow HP, no orphan tables, no generic lifecycle service.
///
/// Pause contract: a Menu pause in the same loaded world freezes the applied snapshot (B / HP / M /
/// Ready / reason) with zero writes; authority loss / online / disabled are checked *before* the
/// pause branch so a paused snapshot can never mask them. Time itself triggers no cleanup.
/// </summary>
internal static class WallEngineerRuntime
{
    private enum NativeReturnPhase { None, Requested, ReadyForNative, PublishUnconfirmed }

    private sealed class EngineerSlot
    {
        internal Worker Worker;                 // wrapper kept only for reads; identity is Pointer
        internal IntPtr WorkerPtr;
        internal IntPtr ScannerPtr, ObserverPtr;
        internal float Epoch = float.NaN;
        internal bool EpochValid;               // Q captured, wall plane crossed, inner side covered
        internal float QMinX, QMinY, QMaxX, QMaxY, PlaneX;
        internal float Range, RangeBehind, Height;
        internal int WallInstanceId;
        internal ContactFilter2D Filter;
        internal readonly List<Collider2D> Hits = new();
    }

    private sealed class OwnedWall
    {
        internal Wall Wall;
        internal IntPtr WallPtr;
        internal int InstanceId;
        internal int Side;                      // 0 = Side.Left, 1 = Side.Right
        internal bool LeftOfKingdom;            // kingdom interior lies at +x of the wall plane
        internal int BaseInitial = -1;          // native B before this module touched the instance
        internal int AppliedMultiplier;         // last value actually written (0 = untouched)
        internal int PokedMultiplier = -1;      // AddWork poke bookkeeping
        internal bool Qualified;                // combat-repair eligibility currently held
        internal bool ReleaseBlocked;           // B not proven returned (or foreign value): no re-capture
        internal bool PendingGraphics;          // UpdateGraphics not yet confirmed for the last boundary change
        internal bool PendingBorder;            // native border recalculation still owed for the last crossing
        internal bool CleanupPending;           // RemoveWork/Reset/queued cleanup not yet confirmed
        internal NativeReturnPhase NativeReturn; // this owner's ordinary native-repair handoff
        internal Kingdom Kingdom;               // the kingdom this wall belonged to at capture
        internal IntPtr KingdomPtr;             // its native identity at capture
        internal readonly EngineerSlot[] Engineers = new EngineerSlot[WallEngineerRules.MaxEngineersPerWall];

        internal void ClearSlots()
        {
            for (int i = 0; i < Engineers.Length; i++) Engineers[i] = null;
        }
    }

    private static readonly OwnedWall[] Owned = new OwnedWall[2];
    private static readonly Il2CppSystem.Collections.Generic.List<Collider2D> QueryBuffer = new();

    private static WallEngineerStatus _status = new(false, false, false, 0, 0, 0, WallEngineerUnavailableReason.Disabled);
    private static long _version;

    // Load gate: a simple in-flight depth plus one proven ApplyToScene tuple.
    private static int _loadDepth;
    private static bool _loadCompleted, _loadedValid;
    private static IntPtr _loadedCampaignPtr, _loadedIslandPtr, _loadedK, _loadedW, _loadedL;
    private static int _loadedScene;

    private static bool _appliedValid;
    private static IntPtr _appliedK, _appliedW, _appliedL;
    private static int _appliedScene;

    private static bool _hasContext;
    private static IntPtr _kingdomPtr, _worldPtr, _layerPtr;
    private static int _scene;
    private static Kingdom _kingdom;
    private static Game _game;
    private static Transform _layerTransform;

    private static Il2CppReferenceArray<Worker> _roster;
    private static int _rosterCount = -1;
    private static bool _rosterDirty = true, _dirty = true;
    private static float _nextMaintain;
    private static float _retryAfter;
    private static bool _faulted;
    private static float _nextFaultLog;

    // ------------------------------------------------------------------ frozen API

    internal static WallEngineerStatus Status => _status;

    internal static void Tick(Managers managers, float now)
    {
        try
        {
            TickCore(managers, now);
        }
        catch (Exception ex)
        {
            SetStatus(EnabledNow(), _status.AuthorityAvailable, false, 0, 0, WallEngineerUnavailableReason.NativeEvidenceUnavailable);
            Fault("tick", ex);
        }
    }

    /// <summary>
    /// Full teardown: still-writable owned walls are returned first (B + jobs); records that were
    /// successfully returned or whose native object is already destroyed are dropped; a failed
    /// owner stays as the side's OwnedWall (blocked) so its B is never lost or re-captured.
    /// </summary>
    internal static void Reset()
    {
        try { ReleaseAllForExit(_kingdom); }
        catch (Exception ex) { Fault("reset", ex); }
        for (int i = 0; i < Owned.Length; i++)
            if (Owned[i] != null) Owned[i].ClearSlots();

        _loadDepth = 0;
        _loadCompleted = _loadedValid = false;
        _loadedCampaignPtr = _loadedIslandPtr = _loadedK = _loadedW = _loadedL = IntPtr.Zero;
        _loadedScene = 0;
        _appliedValid = false;
        _appliedK = _appliedW = _appliedL = IntPtr.Zero;
        _appliedScene = 0;
        _hasContext = false;
        _kingdomPtr = _worldPtr = _layerPtr = IntPtr.Zero;
        _scene = 0;
        _kingdom = null;
        _game = null;
        _layerTransform = null;
        _roster = null;
        _rosterCount = -1;
        _rosterDirty = true;
        _dirty = true;
        _nextMaintain = 0f;
        _retryAfter = 0f;
        _faulted = false;

        bool blocked = Owned[0] != null || Owned[1] != null;
        SetStatus(EnabledNow(), false, false, 0, 0, blocked
            ? WallEngineerUnavailableReason.ApplicationFailed
            : WallEngineerUnavailableReason.Loading);
    }

    // ------------------------------------------------------------- patch notifications

    internal static void NotifyLoadScopeEntered()
    {
        _loadDepth++;
        if (_loadDepth != 1) return;
        _appliedValid = false;
        _loadCompleted = false;
        _loadedValid = false;
        InvalidateReady(WallEngineerUnavailableReason.Loading);   // memory only, no clamp
        if (Writable())
        {
            DropAllQualifications();
        }
        else
        {
            // No native writes without authority: only turn the exceptions off locally and keep the
            // owners (B and every queued responsibility) for a later writable pass.
            for (int i = 0; i < Owned.Length; i++)
            {
                if (Owned[i] == null) continue;
                Owned[i].Qualified = false;
                Owned[i].CleanupPending = true;
            }
        }
        _dirty = true;
    }

    internal static void NotifyLoadScopeExited(bool completed)
    {
        if (_loadDepth > 0) _loadDepth--;
        if (_loadDepth != 0) return;
        if (!completed)
        {
            // A failing pop never leaves a loaded event behind; a later ApplyToScene must prove
            // its own campaign/island/context tuple from scratch.
            _loadCompleted = false;
            _loadedValid = false;
        }
        _dirty = true;
    }

    internal static void NotifyApplyToSceneCompleted(CampaignSaveData campaign)
    {
        try
        {
            if (campaign == null) return;
            // During a pop scope no completion may commit; unknown/partial tuples are rejected.
            if (_loadDepth > 0) return;
            if (CampaignSaveData.current == null || Ptr(CampaignSaveData.current) != Ptr(campaign)) return;
            IntPtr island = Ptr(campaign.CurrentIsland);
            if (island == IntPtr.Zero) return;
            if (!TryCaptureContext(out IntPtr k, out IntPtr w, out IntPtr l, out int scene)) return;
            _loadedCampaignPtr = Ptr(campaign);
            _loadedIslandPtr = island;
            _loadedK = k; _loadedW = w; _loadedL = l; _loadedScene = scene;
            _loadCompleted = true;
            _loadedValid = true;
            _dirty = true;
        }
        catch (Exception ex)
        {
            NotifyApplyToSceneFailed();
            Fault("apply-bind", ex);
        }
    }

    internal static void NotifyApplyToSceneFailed()
    {
        _appliedValid = false;
        _loadCompleted = false;
        _loadedValid = false;
        InvalidateReady(WallEngineerUnavailableReason.Loading);   // memory only
    }

    internal static void NotifyRosterChanged()
    {
        _rosterDirty = true;
        _dirty = true;
    }

    internal static void NotifyWallBookkeepingChanged() => _dirty = true;

    /// <summary>Kingdom.ReplaceWall prefix: only the wall actually being replaced is returned.</summary>
    internal static void NotifyWallReplacing(Wall oldWall)
    {
        if (oldWall == null) return;
        try
        {
            IntPtr ptr = Ptr(oldWall);
            if (ptr == IntPtr.Zero) return;
            for (int i = 0; i < Owned.Length; i++)
            {
                OwnedWall owned = Owned[i];
                if (owned == null || owned.WallPtr != ptr) continue;
                owned.NativeReturn = NativeReturnPhase.None;
                RemoveOwner(_kingdom, owned, i);
            }
        }
        catch (Exception ex) { Fault("replace", ex); }
        _dirty = true;
    }

    /// <summary>Wall.OnDisable prefix: this exact instance is leaving; return B before it dies.</summary>
    internal static void NotifyWallDisabled(Wall wall)
    {
        if (wall == null) return;
        try
        {
            IntPtr ptr = Ptr(wall);
            for (int i = 0; i < Owned.Length; i++)
            {
                OwnedWall owned = Owned[i];
                if (owned == null || owned.WallPtr != ptr) continue;
                owned.NativeReturn = NativeReturnPhase.None;
                RemoveOwner(_kingdom, owned, i);
            }
        }
        catch (Exception ex) { Fault("wall-disable", ex); }
        _dirty = true;
    }

    internal static void NotifyWallDamaged(Wall wall)
    {
        if (wall == null) return;
        _dirty = true;   // re-evaluate qualification on the next Playing maintenance pass
    }

    internal static void NotifyWorkerDisabled(Worker worker)
    {
        if (worker == null) return;
        try
        {
            IntPtr ptr = Ptr(worker);
            for (int i = 0; i < Owned.Length; i++)
                if (Owned[i] != null) ClearSlot(Owned[i], ptr);
        }
        catch (Exception ex) { Fault("worker-disable", ex); }
    }

    internal static void NotifyKingdomDisabled()
    {
        try { ReleaseAllForExit(_kingdom); }
        catch (Exception ex) { Fault("kingdom-disable", ex); }
        _appliedValid = false;
        _loadCompleted = _loadedValid = false;
        _dirty = true;
    }

    /// <summary>
    /// Wall.HandleOnBecameUnSafe prefix: true skips the native work removal for a qualified outer
    /// wall. Full walls are not qualified, so the native safe-removal path is untouched.
    /// </summary>
    internal static bool ShouldSkipUnsafeRemoval(Wall wall)
    {
        try
        {
            if (wall == null || !CanRead()) return false;
            IntPtr ptr = Ptr(wall);
            for (int i = 0; i < Owned.Length; i++)
                if (Owned[i] != null && Owned[i].WallPtr == ptr && Owned[i].Qualified && !Owned[i].ReleaseBlocked)
                    return true;
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Worker.ShouldFlee postfix seam (native result was true). True = suppression allowed. Every
    /// native fact is re-read live here; any doubt keeps the native flee.
    /// </summary>
    internal static bool MaySuppressFlee(Worker worker)
    {
        try
        {
            if (worker == null || !CanRead()) return false;
            if (!IsLiveWorker(worker)) return false;
            if (worker.behaviour == null) return false;
            var haglet = worker.behaviour.Cast<Coatsink.Common.Haglet>();
            if (haglet == null || !haglet.started) return false;
            if (haglet.latestGoto == 8) return false;   // already in the native flee/go-to state

            IntPtr currentPtr = Ptr(worker._currentWork);
            IntPtr queuedPtr = Ptr(worker._queuedWork);
            for (int i = 0; i < Owned.Length; i++)
            {
                OwnedWall owned = Owned[i];
                if (owned == null || owned.ReleaseBlocked) continue;
                if (!owned.Qualified || owned.CleanupPending) continue;   // exception off while not clean
                // Only the wall actually being worked grants the exception; a queued own wall with a
                // different non-null current work must never suppress the native flee.
                bool owns = currentPtr != IntPtr.Zero
                    ? currentPtr == owned.WallPtr
                    : queuedPtr == owned.WallPtr;
                if (!owns) continue;

                if (!LiveWall(owned)) return false;
                if (!IsCurrentOuter(owned)) return false;
                if (!DamagedWall(owned, out int max)) return false;

                float workerX = worker.transform.position.x;
                float wallX = owned.Wall.transform.position.x;
                float scaleX = owned.Wall.transform.localScale.x;   // native Mover Formation uses localScale
                float offset = owned.Wall.GetJobOffset(worker);
                float targetX = wallX + offset * scaleX;
                if (!IsFinite(workerX) || !IsFinite(wallX) || !IsFinite(scaleX) || !IsFinite(offset) || !IsFinite(targetX))
                    return false;
                if (!WallEngineerRules.WorkerInboard(_kingdom.IsWithinWalls(workerX), workerX, wallX, owned.LeftOfKingdom))
                    return false;
                if (!WallEngineerRules.NativeTargetInboard(targetX, wallX, owned.LeftOfKingdom))
                    return false;

                EngineerSlot slot = AcquireSlot(owned, worker);
                if (slot == null) return false;   // both native slots already tracked by other workers
                return DangerInside(worker, owned, slot) == false;
            }
            return false;
        }
        catch
        {
            return false;   // doubt: keep native flee
        }
    }

    internal static void ReportHookFault(string where)
    {
        _faulted = true;
        _dirty = true;
        if (Time.time < _nextFaultLog) return;
        _nextFaultLog = Time.time + WallEngineerRules.FaultLogIntervalSeconds;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[WallEngineer] hook fault: " + where); }
        catch { }
    }

    // ------------------------------------------------------------------ tick core

    private static void TickCore(Managers managers, float now)
    {
        bool enabled = EnabledNow();
        bool authority = SafeAuthority();
        bool online = SafeOnline();

        if (!enabled)
        {
            if (!online && authority) ReleaseAllForNativeReturn(now);
            SetStatus(false, authority, false, 0, 0, WallEngineerUnavailableReason.Disabled);
            return;
        }
        if (online)
        {
            SetStatus(true, authority, false, 0, 0, WallEngineerUnavailableReason.NetworkUnsupported);
            return;
        }

        if (managers == null)
        {
            SetStatus(true, authority, false, 0, 0, WallEngineerUnavailableReason.Loading);
            return;
        }
        Kingdom kingdom = managers.kingdom;
        World world = managers.world;
        Game game = managers.game;
        Transform layer = world != null ? world.gameLayer : null;
        if (kingdom == null || world == null || game == null || layer == null)
        {
            SetStatus(true, authority, false, 0, 0, WallEngineerUnavailableReason.Loading);
            return;
        }

        IntPtr kingdomPtr = Ptr(kingdom), worldPtr = Ptr(world), layerPtr = Ptr(layer);
        int scene = layer.gameObject.scene.handle;
        if (!_hasContext || kingdomPtr != _kingdomPtr || worldPtr != _worldPtr || layerPtr != _layerPtr || scene != _scene)
        {
            // A context switch never writes to the old objects and never drops the completion that
            // was captured for the incoming context; gone owners are cleaned by liveness checks.
            _appliedValid = false;
            for (int i = 0; i < Owned.Length; i++)
                if (Owned[i] != null)
                {
                    Owned[i].NativeReturn = NativeReturnPhase.None;
                    Owned[i].ClearSlots();
                }
            _dirty = true;
        }
        _hasContext = true;
        _kingdomPtr = kingdomPtr; _worldPtr = worldPtr; _layerPtr = layerPtr;
        _scene = scene;
        _kingdom = kingdom; _game = game; _layerTransform = layer;

        // Authority/online/disabled are decided above this point: a paused snapshot can never mask
        // them. Faults must block the same-tick commit.
        if (_faulted)
        {
            _faulted = false;
            SetStatus(true, authority, false, 0, 0, WallEngineerUnavailableReason.NativeEvidenceUnavailable);
            return;
        }

        Game.State state = game.state;
        bool loadedTuple = LoadedTupleValid();
        bool sameApplied = _appliedValid && loadedTuple && _appliedK == kingdomPtr && _appliedW == worldPtr
            && _appliedL == layerPtr && _appliedScene == scene;

        // Failed returns are finished before any load gate: release-only, original kingdom, never a
        // new application. Cadence-limited inside the helper.
        if (state == Game.State.Playing && Writable() && now >= _retryAfter)
            ReleaseRetainedOwners(now);

        if (!authority)
        {
            // Temporary loss: only lose Ready; no writes, B stays recorded.
            SetStatus(true, false, false, 0, 0, WallEngineerUnavailableReason.NoAuthority);
            return;
        }

        if (state == Game.State.Menu)
        {
            if (sameApplied)
            {
                SetStatus(true, true, _status.Ready, _status.AppliedMultiplier, _status.EligibleWallCount, _status.Reason);
                return;
            }
            SetStatus(true, true, false, 0, 0, WallEngineerUnavailableReason.Loading);
            return;
        }

        if (state != Game.State.Playing)
        {
            // Intro or any other non-playable state: wait, keep the completed load event for the
            // eventual Playing, and never clamp before Playing.
            SetStatus(true, true, false, 0, 0, WallEngineerUnavailableReason.Loading);
            return;
        }

        if (_loadDepth != 0 || SafePopping())
        {
            SetStatus(true, true, false, 0, 0, WallEngineerUnavailableReason.Loading);
            return;
        }

        if (!LoadedTupleValid())
        {
            if (!sameApplied)
                SetStatus(true, true, false, 0, 0, WallEngineerUnavailableReason.Loading);
            return;
        }

        if (!_dirty && now < _nextMaintain) return;
        _nextMaintain = now + WallEngineerRules.MaintainIntervalSeconds;
        _dirty = false;

        Maintain(kingdom, now);
    }

    private static bool LoadedTupleValid()
    {
        if (!_loadCompleted || !_loadedValid || _loadedIslandPtr == IntPtr.Zero) return false;
        if (_kingdomPtr != _loadedK || _worldPtr != _loadedW || _layerPtr != _loadedL || _scene != _loadedScene)
            return false;
        try
        {
            CampaignSaveData current = CampaignSaveData.current;
            return current != null && Ptr(current) == _loadedCampaignPtr && Ptr(current.CurrentIsland) == _loadedIslandPtr;
        }
        catch { return false; }
    }

    private static void Maintain(Kingdom kingdom, float now)
    {
        int workers;
        try { workers = CountWorkers(kingdom); }
        catch (Exception ex)
        {
            SetStatus(true, true, false, 0, 0, WallEngineerUnavailableReason.NativeEvidenceUnavailable);
            Fault("roster", ex);
            return;
        }

        int multiplier = WallEngineerRules.MultiplierForWorkers(workers);
        bool any = false, ok = true;
        WallEngineerUnavailableReason failure = WallEngineerUnavailableReason.None;
        int applied = 0;

        Wall left = ReadOuterWall(kingdom, true);
        Wall right = ReadOuterWall(kingdom, false);
        // Same native wall on both sides (same GameObject or another wrapper with the same Pointer):
        // dedupe before any capture, so B is read once and multiplied once.
        if (left != null && right != null && Ptr(left) == Ptr(right)) right = null;

        for (int i = 0; i < Owned.Length; i++)
        {
            Wall target = i == 0 ? left : right;
            OwnedWall owned = Owned[i];

            if (owned != null && !owned.ReleaseBlocked && (target == null || Ptr(target) != owned.WallPtr))
            {
                // The old wall can remain active as an inner wall. A replacement/disable prefix
                // already tears down its owner, so only this ordinary bookkeeping path requests handoff.
                if (!owned.ReleaseBlocked && owned.NativeReturn == NativeReturnPhase.None && LiveWall(owned))
                    owned.NativeReturn = NativeReturnPhase.Requested;
                if (!RemoveOwner(kingdom, owned, i))
                    _retryAfter = now + WallEngineerRules.FaultRetrySeconds;
                owned = Owned[i];
            }
            if (owned != null && owned.ReleaseBlocked)
            {
                if (Writable() && now >= _retryAfter && TryReleaseBlocked(kingdom, owned))
                {
                    Owned[i] = null;
                    owned = null;
                }
                else
                {
                    any = true;
                    ok = false;
                    if (failure == WallEngineerUnavailableReason.None) failure = WallEngineerUnavailableReason.ApplicationFailed;
                    continue;
                }
            }
            if (owned != null && owned.CleanupPending)
            {
                Kingdom cleanupKing = OwnerKingdom(owned);
                if (cleanupKing == null || !DropQualification(cleanupKing, owned))
                {
                    ok = false;
                    if (failure == WallEngineerUnavailableReason.None) failure = WallEngineerUnavailableReason.NativeEvidenceUnavailable;
                    continue;
                }
                owned.CleanupPending = false;
            }
            if (target == null) continue;
            any = true;

            if (!CanApply(target))
            {
                if (owned != null)
                {
                    owned.NativeReturn = NativeReturnPhase.None;
                    if (!RemoveOwner(kingdom, owned, i))
                    {
                        ok = false;
                        if (failure == WallEngineerUnavailableReason.None) failure = WallEngineerUnavailableReason.ApplicationFailed;
                    }
                }
                continue;
            }
            if (owned == null)
            {
                owned = new OwnedWall
                {
                    Wall = target,
                    WallPtr = Ptr(target),
                    InstanceId = target.gameObject.GetInstanceID(),
                    Side = i,
                    LeftOfKingdom = i == 0,
                    Kingdom = kingdom,
                    KingdomPtr = Ptr(kingdom),
                };
                if (!CaptureBase(owned))
                {
                    ok = false;
                    failure = WallEngineerUnavailableReason.NativeEvidenceUnavailable;
                    continue;
                }
                Owned[i] = owned;
            }
            if (!ApplyCap(kingdom, owned, multiplier))
            {
                ok = false;
                if (failure == WallEngineerUnavailableReason.None) failure = WallEngineerUnavailableReason.ApplicationFailed;
                continue;
            }
            Kingdom ownerKing = OwnerKingdom(owned);
            if (ownerKing == null)
            {
                ok = false;
                if (failure == WallEngineerUnavailableReason.None) failure = WallEngineerUnavailableReason.NativeEvidenceUnavailable;
                continue;
            }
            applied++;
            if (!UpdateQualification(ownerKing, owned))
            {
                ok = false;
                if (failure == WallEngineerUnavailableReason.None) failure = WallEngineerUnavailableReason.ApplicationFailed;
            }
        }

        if (!any)
        {
            SetStatus(true, true, false, 0, 0, WallEngineerUnavailableReason.NoOuterWall);
            return;
        }
        if (!ok)
        {
            SetStatus(true, true, false, 0, applied, failure == WallEngineerUnavailableReason.None
                ? WallEngineerUnavailableReason.ApplicationFailed
                : failure);
            return;
        }

        _appliedValid = true;
        _appliedK = _kingdomPtr; _appliedW = _worldPtr; _appliedL = _layerPtr; _appliedScene = _scene;
        SetStatus(true, true, applied > 0, multiplier, applied,
            applied > 0 ? WallEngineerUnavailableReason.None : WallEngineerUnavailableReason.NoOuterWall);
    }

    // ------------------------------------------------------------------ walls / cap

    private static Wall ReadOuterWall(Kingdom kingdom, bool left)
    {
        try
        {
            GameObject go = kingdom.outerWall[left ? Side.Left : Side.Right];
            if (go == null) return null;
            Wall wall = go.GetComponent<Wall>();
            return wall != null && Ptr(wall) != IntPtr.Zero ? wall : null;
        }
        catch { return null; }
    }

    private static bool CanApply(Wall wall)
    {
        if (wall == null) return false;
        try
        {
            GameObject go = wall.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            if (go.scene.handle != _scene) return false;
            if (!InLayerTree(wall.transform)) return false;
            return !UnderConstruction(wall);
        }
        catch { return false; }
    }

    private static bool UnderConstruction(Wall wall)
    {
        try { return wall._workableBuilding == null || wall._workableBuilding.UnderConstruction; }
        catch { return true; }
    }

    private static bool InLayerTree(Transform transform)
    {
        try
        {
            if (transform == null || _layerTransform == null) return false;
            if (Ptr(transform) == Ptr(_layerTransform)) return true;
            return transform.IsChildOf(_layerTransform);
        }
        catch { return false; }
    }

    /// <summary>Base capture: current native value only when this exact instance was never written.</summary>
    private static bool CaptureBase(OwnedWall owned)
    {
        try
        {
            Damageable damageable = owned.Wall._damageable;
            if (damageable == null) return false;
            int current = damageable.initialHitPoints;
            if (current <= 0) return false;
            owned.BaseInitial = current;
            owned.AppliedMultiplier = 0;
            return true;
        }
        catch { return false; }
    }

    private static bool ApplyCap(Kingdom kingdom, OwnedWall owned, int multiplier)
    {
        Wall wall = owned.Wall;
        Kingdom ownerKing = OwnerKingdom(owned);
        if (ownerKing == null) return false;   // exact kingdom unproven: keep owner, no writes
        try
        {
            Damageable damageable = wall._damageable;
            if (damageable == null) return false;

            if (owned.AppliedMultiplier > 0)
            {
                // Written before: the instance must still carry exactly our value.
                if (!WallEngineerRules.TryAppliedInitial(owned.BaseInitial, owned.AppliedMultiplier, out int expected)
                    || damageable.initialHitPoints != expected)
                {
                    owned.ReleaseBlocked = true;   // foreign change: keep the owner, never overwrite
                    return false;
                }
            }
            else if (damageable.initialHitPoints != owned.BaseInitial)
            {
                owned.ReleaseBlocked = true;
                return false;
            }

            if (!WallEngineerRules.TryAppliedInitial(owned.BaseInitial, multiplier, out int target))
            {
                Fault("overflow", null, "base=" + owned.BaseInitial + " m=" + multiplier);
                return false;
            }
            if (owned.Qualified && multiplier != owned.AppliedMultiplier && !DropQualification(ownerKing, owned))
                return false;

            bool intactBefore = SafeIsIntact(wall);
            bool changed = false;
            if (damageable.initialHitPoints != target)
            {
                damageable.initialHitPoints = target;
                changed = true;
            }
            // The written value is the truth from here on: later failures keep it for a safe retry.
            owned.AppliedMultiplier = multiplier;

            int max = wall.GetTotalMaxHitPoints();
            int clamped = WallEngineerRules.ClampedHp(damageable.hitPoints, max);
            if (damageable.hitPoints != clamped)
            {
                damageable.hitPoints = clamped;
                changed = true;
            }
            if (intactBefore != SafeIsIntact(wall)) owned.PendingBorder = true;

            if (changed || owned.PendingGraphics)
            {
                if (!TryUpdateGraphics(wall)) { owned.PendingGraphics = true; return false; }
                owned.PendingGraphics = false;
            }
            if (owned.PendingBorder && !TryCalculateBorders(ownerKing)) return false;
            owned.PendingBorder = false;
            return true;
        }
        catch (Exception ex)
        {
            Fault("apply", ex);
            owned.PendingGraphics = true;   // retried next pass; the written value stays authoritative
            return false;
        }
    }

    private static bool UpdateQualification(Kingdom kingdom, OwnedWall owned)
    {
        try
        {
            bool target = DamagedWall(owned, out _);
            if (target)
            {
                if (owned.Qualified && owned.PokedMultiplier == owned.AppliedMultiplier) return true;
                // Publish only after the native work set actually accepts the wall. If AddWork
                // registered and then threw, the retry must not register again: CheckWork tells us
                // the wall is already a member and only the scheduling request is owed.
                if (kingdom.CheckWork(owned.Wall)) kingdom.ReassignWork();
                else kingdom.AddWork(owned.Wall);
                owned.Qualified = true;
                owned.PokedMultiplier = owned.AppliedMultiplier;
                return true;
            }
            if (!owned.Qualified && !owned.CleanupPending) return true;
            return DropQualification(kingdom, owned);
        }
        catch (Exception ex)
        {
            Fault("qualify", ex);
            return false;
        }
    }

    /// <summary>RemoveWork first, then exactly the current==wall resets, then the queued field.</summary>
    private static bool DropQualification(Kingdom kingdom, OwnedWall owned)
    {
        owned.Qualified = false;   // exception off while any cleanup is pending
        if (kingdom == null)
        {
            owned.CleanupPending = true;
            return false;          // exact kingdom unproven: keep every responsibility, no writes
        }
        IntPtr wallPtr = owned.WallPtr;
        bool ok = true;
        try
        {
            kingdom.RemoveWork(owned.Wall);
        }
        catch (Exception ex)
        {
            Fault("remove-work", ex);
            ok = false;
        }

        Il2CppReferenceArray<Worker> roster = null;
        try { roster = ReadRoster(kingdom); }
        catch (Exception ex)
        {
            Fault("roster-reset", ex);
            ok = false;
        }
        if (roster == null)
        {
            ok = false;
        }
        else
        {
            for (int i = 0; i < roster.Count; i++)
            {
                Worker worker = roster[i];
                if (worker == null) continue;
                try
                {
                    if (Ptr(worker._currentWork) == wallPtr) worker.ResetWorkState(false);
                    if (Ptr(worker._queuedWork) == wallPtr) worker._queuedWork = null;
                }
                catch { ok = false; }   // remaining responsibility kept via CleanupPending
            }
        }
        owned.CleanupPending = !ok;
        return ok;
    }

    /// <summary>
    /// Cleanup roster for one owner's own kingdom, read directly (never the global maintenance
    /// cache, which only tracks the current context and could hold another kingdom's workers).
    /// </summary>
    private static Il2CppReferenceArray<Worker> ReadRoster(Kingdom kingdom)
    {
        var collection = kingdom.Workers;
        if (collection == null) throw new InvalidOperationException("no roster");
        int count = collection.Count;
        if (count < 0) throw new InvalidOperationException("bad roster count");
        var roster = new Il2CppReferenceArray<Worker>(count);
        collection.CopyTo(roster, 0);
        return roster;
    }

    private static bool DamagedWall(OwnedWall owned, out int max)
    {
        max = 0;
        try
        {
            if (owned.Wall == null || owned.ReleaseBlocked) return false;
            if (UnderConstruction(owned.Wall)) return false;
            if (!owned.Wall.isIntact) return false;
            max = owned.Wall.GetTotalMaxHitPoints();
            return owned.Wall._damageable != null && owned.Wall._damageable.hitPoints < max;
        }
        catch { return false; }
    }

    private static void DropAllQualifications()
    {
        for (int i = 0; i < Owned.Length; i++)
        {
            OwnedWall owned = Owned[i];
            if (owned == null) continue;
            if (owned.Qualified || owned.CleanupPending)
                DropQualification(OwnerKingdom(owned), owned);
        }
    }

    // ------------------------------------------------------------------ release

    private static bool Writable()
    {
        try { return !SafeOnline() && SafeAuthority(); }
        catch { return false; }
    }

    /// <summary>Exit path: return everything still writable; keep blocked owners (B never lost).</summary>
    private static void ReleaseAllForExit(Kingdom kingdom)
    {
        for (int i = 0; i < Owned.Length; i++)
        {
            OwnedWall owned = Owned[i];
            if (owned == null) continue;
            owned.NativeReturn = NativeReturnPhase.None;
            if (!RemoveOwner(kingdom, owned, i)) owned.ReleaseBlocked = true;
        }
    }

    private static void ReleaseAllForNativeReturn(float now)
    {
        bool failed = false;
        for (int i = 0; i < Owned.Length; i++)
        {
            OwnedWall owned = Owned[i];
            if (owned == null) continue;
            if (owned.ReleaseBlocked && now < _retryAfter) continue;
            if (!owned.ReleaseBlocked && owned.NativeReturn == NativeReturnPhase.None && LiveWall(owned))
                owned.NativeReturn = NativeReturnPhase.Requested;
            if (!RemoveOwner(_kingdom, owned, i))
            {
                owned.ReleaseBlocked = true;
                failed = true;
            }
        }
        if (failed) _retryAfter = now + WallEngineerRules.FaultRetrySeconds;
    }

    /// <summary>Drops or releases one side's owner. False = kept (blocked) responsibility.</summary>
    private static bool RemoveOwner(Kingdom kingdom, OwnedWall owned, int side)
    {
        if (owned == null) { Owned[side] = null; return true; }
        switch (NativeLifeOf(owned))
        {
            case NativeLife.Destroyed:
                // Native object truly gone: nothing can be returned safely; drop the record.
                Owned[side] = null;
                return true;
            case NativeLife.Unknown:
                owned.ReleaseBlocked = true;   // unreadable: keep the responsibility
                return false;
        }
        if (!Writable()) { owned.ReleaseBlocked = true; return false; }
        if (ReleaseOwned(kingdom, owned))
        {
            Owned[side] = null;
            return true;
        }
        owned.ReleaseBlocked = true;
        return false;
    }

    private static bool TryReleaseBlocked(Kingdom kingdom, OwnedWall owned)
    {
        if (owned == null) return true;
        switch (NativeLifeOf(owned))
        {
            case NativeLife.Destroyed:
                return true;
            case NativeLife.Unknown:
                return false;
        }
        if (!Writable()) return false;
        return ReleaseOwned(kingdom, owned);
    }

    /// <summary>
    /// Release-only retry for owners whose return already failed. Uses each wall's original kingdom
    /// (never the new context), only touches ReleaseBlocked owners, and is cadence-limited so it can
    /// never re-dispatch work every frame.
    /// </summary>
    private static void ReleaseRetainedOwners(float now)
    {
        bool failed = false;
        for (int i = 0; i < Owned.Length; i++)
        {
            OwnedWall owned = Owned[i];
            if (owned == null || !owned.ReleaseBlocked) continue;
            if (!RemoveOwner(OwnerKingdom(owned), owned, i)) failed = true;
        }
        _retryAfter = failed ? now + WallEngineerRules.FaultRetrySeconds : now;
    }

    /// <summary>Returns B and frees our jobs. False = kept owner, B/refresh not proven returned.</summary>
    private static bool ReleaseOwned(Kingdom kingdom, OwnedWall owned)
    {
        if (owned.NativeReturn == NativeReturnPhase.ReadyForNative
            || owned.NativeReturn == NativeReturnPhase.PublishUnconfirmed)
            return PublishNativeReturn(owned);
        if (!ReleaseOwnedBase(kingdom, owned)) return false;
        if (owned.NativeReturn == NativeReturnPhase.Requested)
        {
            owned.NativeReturn = NativeReturnPhase.ReadyForNative;
            return PublishNativeReturn(owned);
        }
        return true;
    }

    private static bool ReleaseOwnedBase(Kingdom kingdom, OwnedWall owned)
    {
        if (owned == null || owned.Wall == null) return true;
        Kingdom ownerKing = OwnerKingdom(owned);
        if (ownerKing == null) return false;   // exact kingdom unproven: keep owner, no writes
        bool registered;
        try { registered = ownerKing.CheckWork(owned.Wall); }
        catch (Exception ex) { Fault("release-membership", ex); return false; }
        // AddWork can register before scheduling throws, without ever committing Qualified.
        if (owned.Qualified || owned.CleanupPending || registered)
        {
            if (!DropQualification(ownerKing, owned)) return false;
            owned.Qualified = false;
            owned.CleanupPending = false;
        }
        if (owned.BaseInitial <= 0) return true;

        if (owned.AppliedMultiplier <= 0)
        {
            // B was written in an earlier attempt; finish the clamp and native refreshes it still owes.
            if (!owned.PendingGraphics && !owned.PendingBorder) return true;
            try
            {
                Damageable damageable = owned.Wall._damageable;
                if (damageable == null) return false;
                int max = owned.Wall.GetTotalMaxHitPoints();
                int clamped = WallEngineerRules.ClampedHp(damageable.hitPoints, max);
                bool changed = damageable.hitPoints != clamped;
                if (changed) damageable.hitPoints = clamped;
                if (changed || owned.PendingGraphics)
                {
                    if (!TryUpdateGraphics(owned.Wall)) return false;
                    owned.PendingGraphics = false;
                }
                if (owned.PendingBorder && !TryCalculateBorders(ownerKing)) return false;
                owned.PendingBorder = false;
                return true;
            }
            catch (Exception ex)
            {
                Fault("release-finish", ex);
                owned.PendingGraphics = true;
                return false;
            }
        }

        try
        {
            Damageable damageable = owned.Wall._damageable;
            if (damageable == null) return false;
            if (!WallEngineerRules.TryAppliedInitial(owned.BaseInitial, owned.AppliedMultiplier, out int written))
                return false;
            if (damageable.initialHitPoints != written) return false;   // foreign change: never overwrite
            bool intactBefore = SafeIsIntact(owned.Wall);
            bool initialChanged = damageable.initialHitPoints != owned.BaseInitial;
            damageable.initialHitPoints = owned.BaseInitial;
            if (initialChanged) owned.PendingGraphics = true;
            owned.AppliedMultiplier = 0;   // the instance now carries B, immediately
            int max = owned.Wall.GetTotalMaxHitPoints();
            int clamped = WallEngineerRules.ClampedHp(damageable.hitPoints, max);
            bool changed = damageable.hitPoints != clamped;
            if (changed) damageable.hitPoints = clamped;
            if (intactBefore != SafeIsIntact(owned.Wall)) owned.PendingBorder = true;

            if (changed || owned.PendingGraphics)
            {
                if (!TryUpdateGraphics(owned.Wall)) { owned.PendingGraphics = true; return false; }
                owned.PendingGraphics = false;
            }
            if (owned.PendingBorder && !TryCalculateBorders(ownerKing)) return false;
            owned.PendingBorder = false;
            return true;
        }
        catch (Exception ex)
        {
            Fault("release", ex);
            owned.PendingGraphics = true;   // the owed clamp/refresh is retried from the B-known state
            return false;
        }
    }

    private static bool PublishNativeReturn(OwnedWall owned)
    {
        try
        {
            Kingdom ownerKing = OwnerKingdom(owned);
            Managers managers = Managers.Inst;
            if (ownerKing == null || managers == null || managers.kingdom == null) return false;
            if (Ptr(managers.kingdom) != owned.KingdomPtr)
            {
                owned.NativeReturn = NativeReturnPhase.None;
                return ReleaseOwnedBase(ownerKing, owned);
            }
            if (!Writable()) return false;
            if (_loadDepth != 0 || SafePopping() || !LoadedTupleValid()) return false;
            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            Game game = managers.game;
            if (world == null || layer == null || game == null) return false;
            if (Ptr(world) != _worldPtr || Ptr(layer) != _layerPtr || layer.gameObject.scene.handle != _scene)
            {
                owned.NativeReturn = NativeReturnPhase.None;
                return ReleaseOwnedBase(ownerKing, owned);
            }
            if (game.state != Game.State.Playing && game.state != Game.State.Menu) return false;
            if (!LiveWall(owned))
            {
                owned.NativeReturn = NativeReturnPhase.None;
                return ReleaseOwnedBase(ownerKing, owned);
            }
            if (!ownerKing.isSafe || !owned.Wall.JobAvailable()) return true;

            bool registered = ownerKing.CheckWork(owned.Wall);
            if (registered)
            {
                if (owned.NativeReturn == NativeReturnPhase.PublishUnconfirmed)
                    ownerKing.ReassignWork();
                return true;
            }
            owned.NativeReturn = NativeReturnPhase.PublishUnconfirmed;
            ownerKing.AddWork(owned.Wall);
            return true;
        }
        catch (Exception ex)
        {
            Fault("native-return", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------ per-worker slots / G3

    private static bool LiveWall(OwnedWall owned)
    {
        try
        {
            Wall wall = owned.Wall;
            if (wall == null || Ptr(wall) != owned.WallPtr) return false;
            if (wall.gameObject.GetInstanceID() != owned.InstanceId) return false;
            GameObject go = wall.gameObject;
            return go.activeInHierarchy && go.scene.handle == _scene && InLayerTree(wall.transform);
        }
        catch { return false; }
    }

    private enum NativeLife { Exists, Destroyed, Unknown }

    /// <summary>
    /// The kingdom this wall was captured under, resolved strictly. Null = the exact original
    /// kingdom cannot be proven right now: callers must keep the owner and perform no writes
    /// (never fall back to the current or a new kingdom).
    /// </summary>
    private static Kingdom OwnerKingdom(OwnedWall owned)
    {
        try
        {
            if (owned.Kingdom == null || owned.KingdomPtr == IntPtr.Zero) return null;
            if (Ptr(owned.Kingdom) != owned.KingdomPtr) return null;
            return owned.Kingdom;
        }
        catch { return null; }
    }

    /// <summary>
    /// Exact native identity for return responsibility: inactive or out-of-tree is NOT destroyed.
    /// Only a Unity-null wrapper / pointer mismatch / different GameObject instance means gone.
    /// </summary>
    private static NativeLife NativeLifeOf(OwnedWall owned)
    {
        try
        {
            Wall wall = owned.Wall;
            if (wall == null) return NativeLife.Destroyed;
            if (Ptr(wall) != owned.WallPtr) return NativeLife.Destroyed;
            GameObject go = wall.gameObject;
            if (go == null) return NativeLife.Destroyed;
            if (go.GetInstanceID() != owned.InstanceId) return NativeLife.Destroyed;
            return NativeLife.Exists;
        }
        catch { return NativeLife.Unknown; }
    }

    private static bool TryUpdateGraphics(Wall wall)
    {
        try { wall.UpdateGraphics(); return true; }
        catch (Exception ex) { Fault("graphics", ex); return false; }
    }

    private static bool IsCurrentOuter(OwnedWall owned)
    {
        try
        {
            Wall outer = ReadOuterWall(_kingdom, owned.Side == 0);
            return outer != null && Ptr(outer) == owned.WallPtr;
        }
        catch { return false; }
    }

    private static void ClearSlot(OwnedWall owned, IntPtr workerPtr)
    {
        for (int i = 0; i < owned.Engineers.Length; i++)
            if (owned.Engineers[i] != null && owned.Engineers[i].WorkerPtr == workerPtr)
                owned.Engineers[i] = null;
    }

    private static EngineerSlot AcquireSlot(OwnedWall owned, Worker worker)
    {
        IntPtr ptr = Ptr(worker);
        for (int i = 0; i < owned.Engineers.Length; i++)
        {
            EngineerSlot existing = owned.Engineers[i];
            if (existing != null && existing.WorkerPtr == ptr) return existing;
        }
        for (int i = 0; i < owned.Engineers.Length; i++)
        {
            EngineerSlot slot = owned.Engineers[i];
            if (slot != null && slot.Worker != null)
            {
                try
                {
                    bool stillAssigned = Ptr(slot.Worker._currentWork) == owned.WallPtr
                        || Ptr(slot.Worker._queuedWork) == owned.WallPtr;
                    if (stillAssigned && IsLiveWorker(slot.Worker)) continue;   // occupied by another worker
                }
                catch { }
                owned.Engineers[i] = null;
            }
            if (owned.Engineers[i] == null)
                return owned.Engineers[i] = new EngineerSlot { Worker = worker, WorkerPtr = ptr };
        }
        return null;
    }

    /// <summary>true = keep the native flee (danger or doubt).</summary>
    private static bool DangerInside(Worker worker, OwnedWall owned, EngineerSlot slot)
    {
        Scanner scanner = worker._enemyScanner;
        if (scanner == null) return true;
        try
        {
            float epoch = scanner._lastRefresh;
            float range = scanner.range, rangeBehind = scanner.rangeBehind, height = scanner._height;
            float wallX = owned.Wall.transform.position.x;
            if (!IsFinite(epoch) || !IsFinite(range) || !IsFinite(rangeBehind) || !IsFinite(height) || !IsFinite(wallX))
                return true;
            ContactFilter2D filter = scanner._contactFilter;
            if (!ValidFilter(ref filter)) return true;

            // Current reach: corners are recomputed for every decision (scratch copied immediately).
            Scanner.ComputeCorners(scanner.observer, range, rangeBehind, height, false);
            float ax = Scanner._pointA.x, ay = Scanner._pointA.y, bx = Scanner._pointB.x, by = Scanner._pointB.y;
            WallEngineerRules.NormalizeRect(ax, ay, bx, by, out float rMinX, out float rMinY, out float rMaxX, out float rMaxY);
            if (!IsFinite(rMinX) || !IsFinite(rMinY) || !IsFinite(rMaxX) || !IsFinite(rMaxY)) return true;

            if (epoch != slot.Epoch)
            {
                slot.Epoch = epoch;
                slot.EpochValid = false;
                int count = Physics2D.OverlapArea(new Vector2(ax, ay), new Vector2(bx, by), filter, QueryBuffer);
                if (count < 0 || count > QueryBuffer.Count) return true;
                // The one physical query of this epoch is reusable only if it crosses this wall and
                // covers its inner side; otherwise this wall can never grant an outside-enemy break.
                if (!WallEngineerRules.Straddles(rMinX, rMaxX, wallX)
                    || !WallEngineerRules.InnerIntersectionNonEmpty(rMinX, rMaxX, wallX, owned.LeftOfKingdom))
                    return true;
                slot.QMinX = rMinX; slot.QMinY = rMinY; slot.QMaxX = rMaxX; slot.QMaxY = rMaxY;
                slot.Hits.Clear();
                for (int i = 0; i < count; i++) slot.Hits.Add(QueryBuffer[i]);
                slot.ScannerPtr = Ptr(scanner);
                slot.ObserverPtr = Ptr(scanner.observer);
                slot.PlaneX = wallX;
                slot.WallInstanceId = owned.InstanceId;
                slot.Range = range; slot.RangeBehind = rangeBehind; slot.Height = height;
                slot.Filter = filter;
                slot.EpochValid = true;
            }
            else if (!slot.EpochValid)
            {
                return true;   // lapsed inside this epoch: wait for the next native refresh
            }
            else if (slot.ScannerPtr != Ptr(scanner) || slot.ObserverPtr != Ptr(scanner.observer)
                     || slot.PlaneX != wallX || slot.WallInstanceId != owned.InstanceId
                     || slot.Range != range || slot.RangeBehind != rangeBehind || slot.Height != height
                     || !FilterEqual(ref slot.Filter, ref filter))
            {
                slot.EpochValid = false;
                return true;
            }

            // Reuse only while the current inner-side reach is fully covered by the queried rect.
            if (!WallEngineerRules.CoveredByQuery(rMinX, rMinY, rMaxX, rMaxY,
                    slot.QMinX, slot.QMinY, slot.QMaxX, slot.QMaxY, wallX, owned.LeftOfKingdom))
            {
                slot.EpochValid = false;
                return true;
            }

            for (int i = 0; i < slot.Hits.Count; i++)
            {
                Collider2D hit = slot.Hits[i];
                if (hit == null) continue;
                try
                {
                    GameObject go = hit.gameObject;
                    if (go == null || !go.activeInHierarchy || go.scene.handle != _scene) continue;
                    Damageable damageable = go.GetComponent<Damageable>();
                    if (damageable != null && damageable.isDead) continue;   // explicitly dead: not a threat
                    Bounds bounds = hit.bounds;
                    if (WallEngineerRules.BoundsTouchInnerHalfSpace(bounds.min.x, bounds.max.x, wallX, owned.LeftOfKingdom))
                        return true;
                }
                catch
                {
                    return true;   // unknown collider state: conservative
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool FilterEqual(ref ContactFilter2D a, ref ContactFilter2D b)
        => a.useTriggers == b.useTriggers
           && a.useLayerMask == b.useLayerMask
           && a.useDepth == b.useDepth
           && a.useOutsideDepth == b.useOutsideDepth
           && a.useNormalAngle == b.useNormalAngle
           && a.useOutsideNormalAngle == b.useOutsideNormalAngle
           && a.layerMask.value == b.layerMask.value
           && a.minDepth == b.minDepth
           && a.maxDepth == b.maxDepth
           && a.minNormalAngle == b.minNormalAngle
           && a.maxNormalAngle == b.maxNormalAngle;

    private static bool ValidFilter(ref ContactFilter2D f)
    {
        if (float.IsNaN(f.minDepth) || float.IsNaN(f.maxDepth)) return false;
        if (f.useDepth && f.minDepth > f.maxDepth) return false;
        if (f.useNormalAngle)
        {
            if (!IsFinite(f.minNormalAngle) || !IsFinite(f.maxNormalAngle)) return false;
            if (f.minNormalAngle > f.maxNormalAngle) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ roster

    private static bool IsLiveWorker(Worker worker)
    {
        if (worker == null) return false;
        try
        {
            GameObject go = worker.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            if (go.scene.handle != _scene || !InLayerTree(worker.transform)) return false;
            Worker current = go.GetComponent<Worker>();
            if (current == null || Ptr(current) != Ptr(worker)) return false;   // not the current native role
            Damageable damageable = go.GetComponent<Damageable>();
            if (damageable != null)
            {
                if (damageable.isDead) return false;
                if (damageable.useHitPoints && damageable.hitPoints <= 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static int CountWorkers(Kingdom kingdom)
    {
        Il2CppReferenceArray<Worker> roster = CopyRoster(kingdom);
        int live = 0;
        for (int i = 0; i < roster.Count; i++)
            if (IsLiveWorker(roster[i])) live++;
        return live;
    }

    private static Il2CppReferenceArray<Worker> CopyRoster(Kingdom kingdom)
    {
        var collection = kingdom.Workers;
        if (collection == null) throw new InvalidOperationException("no roster");
        int count = collection.Count;
        if (count < 0) throw new InvalidOperationException("bad roster count");
        if (_roster == null || _rosterDirty || count != _rosterCount || _roster.Count != count)
        {
            var roster = new Il2CppReferenceArray<Worker>(count);
            collection.CopyTo(roster, 0);
            _roster = roster;
            _rosterCount = count;
            _rosterDirty = false;
        }
        return _roster;
    }

    // ------------------------------------------------------------------ helpers

    private static bool CanRead()
        => FeatureOn() && _appliedValid && LoadedTupleValid() && _loadDepth == 0 && !SafePopping()
           && Writable() && _game != null && _game.state == Game.State.Playing;

    private static bool FeatureOn()
    {
        try { return ModConfig.Enabled != null && ModConfig.Enabled.Value; }
        catch { return false; }
    }

    private static bool EnabledNow() => FeatureOn();

    private static bool SafeOnline()
    {
        try { return NetworkBigBoss.IsOnline; } catch { return true; }
    }

    private static bool SafeAuthority()
    {
        try { return NetworkBigBoss.HasWorldAuth; } catch { return false; }
    }

    private static bool SafePopping()
    {
        try { return IslandSaveData.poppingObjectsToScene; } catch { return true; }
    }

    private static bool SafeIsIntact(Wall wall)
    {
        try { return wall != null && wall.isIntact; } catch { return false; }
    }

    private static bool TryCaptureContext(out IntPtr kingdom, out IntPtr world, out IntPtr layer, out int scene)
    {
        kingdom = world = layer = IntPtr.Zero;
        scene = 0;
        try
        {
            Managers managers = Managers.Inst;
            if (managers == null) return false;
            Kingdom k = managers.kingdom;
            World w = managers.world;
            Transform l = w != null ? w.gameLayer : null;
            if (k == null || w == null || l == null) return false;
            kingdom = Ptr(k); world = Ptr(w); layer = Ptr(l);
            scene = l.gameObject.scene.handle;
            return kingdom != IntPtr.Zero && world != IntPtr.Zero && layer != IntPtr.Zero;
        }
        catch { return false; }
    }

    /// <summary>
    /// Capacity changes can cross the native isIntact threshold, so the native border consumers must
    /// be recalculated once per crossing (never per frame). A failure blocks the ready commit.
    /// </summary>
    private static bool TryCalculateBorders(Kingdom kingdom)
    {
        if (kingdom == null) return false;
        try
        {
            kingdom.CalculateBordersNow();
            return true;
        }
        catch (Exception ex)
        {
            Fault("borders", ex);
            return false;
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static IntPtr Ptr(Il2CppObjectBase obj)
    {
        try { return obj == null ? IntPtr.Zero : obj.Pointer; } catch { return IntPtr.Zero; }
    }

    private static void Fault(string where, Exception ex, string detail = null)
    {
        _faulted = true;
        if (Time.time < _nextFaultLog) return;
        _nextFaultLog = Time.time + WallEngineerRules.FaultLogIntervalSeconds;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[WallEngineer] " + where + (ex == null ? "" : ": " + ex.GetType().Name) + (detail == null ? "" : " " + detail));
        }
        catch { }
    }

    /// <summary>Memory-only invalidation: a load/apply failure must not keep showing Ready.</summary>
    private static void InvalidateReady(WallEngineerUnavailableReason reason)
    {
        if (!_status.Ready && _status.Reason == reason) return;
        SetStatus(_status.Enabled, _status.AuthorityAvailable, false, 0, 0, reason);
    }

    private static void SetStatus(bool enabled, bool authority, bool ready, int multiplier, int walls, WallEngineerUnavailableReason reason)
    {
        if (!ready) multiplier = 0;
        if (_status.Enabled == enabled && _status.AuthorityAvailable == authority && _status.Ready == ready
            && _status.AppliedMultiplier == multiplier && _status.EligibleWallCount == walls
            && _status.Reason == reason) return;
        _status = new WallEngineerStatus(enabled, authority, ready, multiplier, walls, ++_version, reason);
    }
}
