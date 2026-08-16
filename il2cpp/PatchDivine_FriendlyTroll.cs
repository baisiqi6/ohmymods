using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Candidate-stage Squid filtering plus a deterministic 10% TrollWeak counter.
/// Temporary changes to native collections are always undone by a finalizer.
/// </summary>
public static class PatchDivine_FriendlyTroll
{
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private const uint DesignationSchema = 0x46544231u; // "FTB1"
    private const float MovementMultiplier = 1.5f;
    private const float TargetRangeMultiplier = 2f;
    private const float PursuitOuterRange = 10f;
    private const float PursuitTickInterval = 0.25f;
    private const int TrollChaseState = 1;

    private sealed class SquidFilterState
    {
        internal EnemyManager Manager;
        internal readonly List<Squid> Removed = new();
        internal bool Restored;
    }

    private sealed class TargetInjectionState
    {
        internal TargetCacher Cache;
        internal readonly List<Damageable> Injected = new();
        internal bool Restored;
    }

    private sealed class FriendlyEntry
    {
        internal FriendlyTroll Troll;
        internal Damageable Damageable;
        internal Damageable.DamageEvent DamageHandler;
        internal bool DamageHandlerAttempted;
        internal bool DamageHandlerSubscribed;
    }

    private sealed class FriendlyMovementProfile
    {
        internal FriendlyTroll Troll;
        internal float RunSpeed;
        internal float MaxAttackDistance;
        internal bool Enhanced;
    }

    private sealed class TrollState
    {
        internal Troll Troll;
        internal bool Active;
        internal bool HasDesignation;
        internal bool Designated;
        internal uint IdentityHash;
        internal short NetId;
        internal bool DamageLogged;
        internal Coatsink.Common.Haglet Behaviour;
        internal Mover Mover;
        internal bool PursuitSteered;
    }

    private static readonly Dictionary<int, Squid> ActiveSquids = new();
    private static readonly Dictionary<int, FriendlyEntry> ActiveFriendlies = new();
    private static readonly Dictionary<IntPtr, FriendlyEntry> FriendlyByFsm = new();
    private static readonly Dictionary<int, FriendlyMovementProfile> FriendlyMovementProfiles = new();
    private static readonly Dictionary<int, TrollState> TrollStates = new();
    private static readonly Dictionary<int, TrollState> ActiveCounterTrolls = new();
    private static readonly HashSet<uint> LoggedSpecials = new();
    private static readonly HashSet<int> LoggedFriendlyRegistrations = new();
    private static readonly HashSet<uint> LoggedTargetQueries = new();
    private static readonly HashSet<uint> LoggedTargetInjections = new();
    private static readonly HashSet<string> LoggedErrors = new();
    private static bool _loggedSquidFilter;
    private static bool _loggedMissingIdentity;
    private static bool _pursuitCoordinatorRegistered;
    private static FriendlyTrollPursuitCoordinator _pursuitCoordinator;
    private static IntPtr _pursuitWorldPointer;
    private static float _nextPursuitTickAt;

    private static void LogErrorOnce(string key, Exception exception)
    {
        if (!LoggedErrors.Add(key)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError(
            $"[FriendlyTrollBalance] {key}: {exception}");
    }

    private static bool IsUsable(Component component)
    {
        try
        {
            return component != null && component.gameObject != null
                && component.gameObject.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static TrollState GetTrollState(Troll troll)
    {
        int id = troll.GetInstanceID();
        if (TrollStates.TryGetValue(id, out TrollState state))
        {
            state.Troll = troll;
            return state;
        }

        state = new TrollState { Troll = troll };
        TrollStates[id] = state;
        return state;
    }

    private static void EnsurePursuitCoordinator()
    {
        try
        {
            Managers managers = Managers.Inst;
            World world = managers?.world;
            if (world == null || world.gameObject == null) return;

            if (!_pursuitCoordinatorRegistered)
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(
                        typeof(FriendlyTrollPursuitCoordinator)))
                {
                    ClassInjector.RegisterTypeInIl2Cpp(
                        typeof(FriendlyTrollPursuitCoordinator));
                }
                _pursuitCoordinatorRegistered = true;
            }

            FriendlyTrollPursuitCoordinator coordinator =
                world.GetComponent<FriendlyTrollPursuitCoordinator>();
            if (coordinator == null)
            {
                coordinator = world.gameObject
                    .AddComponent<FriendlyTrollPursuitCoordinator>();
            }
            if (coordinator == null) return;

            _pursuitCoordinator = coordinator;
            _pursuitWorldPointer = world.Pointer;
            _nextPursuitTickAt = Time.time;
        }
        catch { }
    }

    internal static void TickPursuit(FriendlyTrollPursuitCoordinator coordinator)
    {
        if (!IsCurrentPursuitCoordinator(coordinator)) return;

        if (!ModConfig.Enabled.Value)
        {
            ClearPursuitSteering(NetworkBigBoss.HasWorldAuth);
            return;
        }
        if (!NetworkBigBoss.HasWorldAuth)
        {
            ClearPursuitSteering(false);
            return;
        }

        Managers managers = Managers.Inst;
        if (managers?.world == null
            || managers.world.Pointer != _pursuitWorldPointer
            || managers.game == null || managers.game.state != Game.State.Playing)
        {
            ClearPursuitSteering(false);
            return;
        }
        if (Time.timeScale <= 0f || Time.time < _nextPursuitTickAt) return;
        _nextPursuitTickAt = Time.time + PursuitTickInterval;

        foreach (TrollState state in ActiveCounterTrolls.Values)
        {
            if (!TryGetPursuitActor(state, out Troll troll, out Mover mover))
            {
                state.PursuitSteered = false;
                continue;
            }

            float trollX = troll.transform.position.x;
            float nearestDistance = PursuitOuterRange + 1f;
            FriendlyEntry nearest = null;
            foreach (FriendlyEntry friendly in ActiveFriendlies.Values)
            {
                if (!IsPursuitTarget(friendly, troll)) continue;
                float distance = Mathf.Abs(friendly.Troll.transform.position.x - trollX);
                if (distance <= PursuitOuterRange && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = friendly;
                }
            }

            float innerRange = Mathf.Max(0f, troll.chargeRange);
            if (nearest != null && nearestDistance > innerRange)
            {
                int direction = nearest.Troll.transform.position.x > trollX ? 1 : -1;
                mover.SetSpeed(troll.runSpeed, direction);
                state.PursuitSteered = true;
                continue;
            }

            // At or inside the native charge range, make no movement write: the existing
            // exact-range TargetCacher injection and Troll charge state own the actor.
            if (nearest != null)
            {
                state.PursuitSteered = false;
                continue;
            }

            if (state.PursuitSteered) RestoreNativeChase(troll, mover);
            state.PursuitSteered = false;
        }
    }

    private static bool IsCurrentPursuitCoordinator(
        FriendlyTrollPursuitCoordinator coordinator)
    {
        try
        {
            return coordinator != null && _pursuitCoordinator != null
                && coordinator.Pointer == _pursuitCoordinator.Pointer
                && coordinator.gameObject != null
                && coordinator.gameObject.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPursuitActor(TrollState state, out Troll troll,
        out Mover mover)
    {
        troll = state?.Troll;
        mover = null;
        if (state == null || !state.Active || !state.HasDesignation
            || !state.Designated || !IsUsable(troll)
            || troll.Type != EnemyType.TrollWeak || !troll.enabled
            || troll.loot != null
            || troll.shouldRetreat || troll.IsDespawning
            || troll._unitController != null || troll._shouldCharge)
            return false;

        Damageable damageable = troll.damageable;
        Petrifiable petrifiable = troll._petrifiable;
        if (damageable == null || damageable.isDead || damageable.invulnerable
            || petrifiable == null || petrifiable.IsPetrified)
            return false;

        try
        {
            if (troll._behaviour == null) return false;
            if (state.Behaviour == null
                || state.Behaviour.Pointer != troll._behaviour.Pointer)
            {
                state.Behaviour = troll._behaviour.Cast<Coatsink.Common.Haglet>();
            }
            if (state.Behaviour == null
                || state.Behaviour.latestGoto != TrollChaseState)
                return false;

            Mover currentMover = troll._mover;
            if (currentMover == null) return false;
            if (state.Mover == null || state.Mover.Pointer != currentMover.Pointer)
                state.Mover = currentMover;
            if (state.Mover.IsPaused() || state.Mover.movingToGoal) return false;

            mover = state.Mover;
            return true;
        }
        catch { return false; }
    }

    private static bool IsPursuitTarget(FriendlyEntry friendly, Troll troll)
    {
        if (friendly == null || !IsUsable(friendly.Troll)) return false;
        Damageable damageable = friendly.Damageable;
        try
        {
            return damageable != null && damageable.gameObject != null
                && damageable.gameObject.activeInHierarchy && !damageable.isDead
                && damageable.IsDamagedBy(troll.damageSource);
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreNativeChase(Troll troll, Mover mover)
    {
        try
        {
            if (troll == null || mover == null || troll._behaviour == null) return;
            Coatsink.Common.Haglet behaviour =
                troll._behaviour.Cast<Coatsink.Common.Haglet>();
            if (behaviour == null || behaviour.latestGoto != TrollChaseState) return;

            float targetX;
            float multiplier = troll.GetWaveSpeedMultiplier(
                troll.transform.position.x, troll.waveType, out targetX,
                troll._waveTargetOffset);
            float speed = troll._currentWalkSpeed > 0f
                ? troll._currentWalkSpeed : troll.walkSpeed;
            int direction = targetX > troll.transform.position.x ? 1 : -1;
            mover.SetSpeed(speed * multiplier, direction);
        }
        catch { }
    }

    private static void ClearPursuitSteering(bool restoreAuthorityMovement)
    {
        foreach (TrollState state in ActiveCounterTrolls.Values)
        {
            if (!state.PursuitSteered) continue;
            if (restoreAuthorityMovement && TryGetPursuitActor(state,
                    out Troll troll, out Mover mover))
            {
                RestoreNativeChase(troll, mover);
            }
            state.PursuitSteered = false;
        }
    }

    internal static void DisablePursuitCoordinator(
        FriendlyTrollPursuitCoordinator coordinator)
    {
        if (coordinator == null || _pursuitCoordinator == null
            || coordinator.Pointer != _pursuitCoordinator.Pointer)
            return;

        // World unload owns mover shutdown. Do not write into the hierarchy while it is
        // recursively disabling; only relinquish the authority-side steering markers.
        ClearPursuitSteering(false);
        _pursuitCoordinator = null;
        _pursuitWorldPointer = IntPtr.Zero;
        _nextPursuitTickAt = 0f;
    }

    private static void RegisterFriendly(FriendlyTroll friendly)
    {
        if (!IsUsable(friendly)) return;

        int id = friendly.GetInstanceID();
        if (!ActiveFriendlies.TryGetValue(id, out FriendlyEntry entry)
            || entry.Troll == null || entry.Troll.Pointer != friendly.Pointer)
        {
            if (entry != null) RemoveFriendlyEntry(id, entry);
            entry = new FriendlyEntry();
            ActiveFriendlies[id] = entry;
        }

        entry.Troll = friendly;
        Damageable currentDamageable = friendly.GetComponent<Damageable>();
        if (!SameNativeComponent(entry.Damageable, currentDamageable))
        {
            UnsubscribeFriendlyDamage(entry);
            entry.Damageable = currentDamageable;
            entry.DamageHandlerAttempted = false;
        }
        EnsureFriendlyDamageSubscription(entry);

        StateMachine fsm = friendly._fsm;
        if (fsm != null) FriendlyByFsm[fsm.Pointer] = entry;
        if (ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth
            && entry.Damageable != null && fsm != null
            && LoggedFriendlyRegistrations.Add(id))
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                $"[FriendlyTrollDiag] stage=friendly-active id={id} "
                + $"fsm=0x{fsm.Pointer.ToInt64():X} hp={entry.Damageable.hitPoints}.");
        }
        if (FriendlyByFsm.Count > 64) PruneFriendlyRegistries();
    }

    private static bool SameNativeComponent(Component left, Component right)
    {
        try
        {
            if (left == null || right == null) return left == null && right == null;
            return left.Pointer == right.Pointer;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureFriendlyDamageSubscription(FriendlyEntry entry)
    {
        if (entry == null || entry.DamageHandlerAttempted
            || !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || !IsUsable(entry.Troll) || entry.Damageable == null)
            return;

        entry.DamageHandlerAttempted = true;
        try
        {
            FriendlyEntry capturedEntry = entry;
            System.Action<int, GameObject, DamageSource> managedHandler =
                (damageMultiplier, damager, source) => ObserveFriendlyDamage(
                    capturedEntry, damageMultiplier, damager, source);
            Damageable.DamageEvent handler = managedHandler;
            if (handler == null) return;

            entry.DamageHandler = handler;
            entry.Damageable.add_OnReceiveDamage(handler);
            entry.DamageHandlerSubscribed = true;
        }
        catch (Exception exception)
        {
            entry.DamageHandlerSubscribed = false;
            entry.DamageHandler = null;
            LogErrorOnce("friendly damage diagnostic subscription failed", exception);
        }
    }

    private static void ObserveFriendlyDamage(FriendlyEntry entry,
        int damageMultiplier, GameObject damager, DamageSource source)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || entry == null || damager == null || !IsUsable(entry.Troll)
            || entry.Damageable == null)
            return;

        try
        {
            int friendlyId = entry.Troll.GetInstanceID();
            if (!ActiveFriendlies.TryGetValue(friendlyId,
                    out FriendlyEntry activeEntry)
                || !ReferenceEquals(activeEntry, entry))
                return;

            Troll troll = damager.GetComponent<Troll>();
            if (troll == null || source != troll.damageSource)
                return;

            int trollId = troll.GetInstanceID();
            if (!ActiveCounterTrolls.TryGetValue(trollId,
                    out TrollState trollState)
                || !trollState.Active || !trollState.HasDesignation
                || !trollState.Designated || trollState.DamageLogged
                || trollState.Troll == null
                || trollState.Troll.GetInstanceID() != trollId
                || trollState.Troll.Pointer != troll.Pointer)
                return;

            trollState.DamageLogged = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                $"[FriendlyTrollDiag] stage=native-damage net={trollState.NetId} "
                + $"hash=0x{trollState.IdentityHash:X8} friendly={friendlyId} "
                + $"multiplier={damageMultiplier} source={source} "
                + $"hpAfterEvent={entry.Damageable.hitPoints}.");
        }
        catch (Exception exception)
        {
            LogErrorOnce("native damage event observation failed", exception);
        }
    }

    private static void UnsubscribeFriendlyDamage(FriendlyEntry entry)
    {
        if (entry == null || !entry.DamageHandlerSubscribed) return;

        try
        {
            if (entry.Damageable != null && entry.DamageHandler != null)
                entry.Damageable.remove_OnReceiveDamage(entry.DamageHandler);
        }
        catch (Exception exception)
        {
            LogErrorOnce("friendly damage diagnostic unsubscribe failed", exception);
        }
        finally
        {
            entry.DamageHandlerSubscribed = false;
            entry.DamageHandler = null;
        }
    }

    private static void RemoveFriendlyEntry(int id, FriendlyEntry expected)
    {
        if (!ActiveFriendlies.TryGetValue(id, out FriendlyEntry entry)
            || (expected != null && !ReferenceEquals(entry, expected)))
            return;

        UnsubscribeFriendlyDamage(entry);
        ActiveFriendlies.Remove(id);

        var staleFsms = new List<IntPtr>();
        foreach (KeyValuePair<IntPtr, FriendlyEntry> pair in FriendlyByFsm)
        {
            if (ReferenceEquals(pair.Value, entry)) staleFsms.Add(pair.Key);
        }
        foreach (IntPtr pointer in staleFsms) FriendlyByFsm.Remove(pointer);
    }

    private static FriendlyMovementProfile CaptureMovementProfile(FriendlyTroll friendly)
    {
        int id = friendly.GetInstanceID();
        if (FriendlyMovementProfiles.TryGetValue(id, out FriendlyMovementProfile profile)
            && profile.Troll != null && profile.Troll.Pointer == friendly.Pointer)
            return profile;

        profile = new FriendlyMovementProfile
        {
            Troll = friendly,
            RunSpeed = friendly._runSpeed,
            MaxAttackDistance = friendly._maxAttackDistance
        };
        FriendlyMovementProfiles[id] = profile;
        return profile;
    }

    private static void ApplyOrRestoreMovementProfile(FriendlyTroll friendly)
    {
        if (friendly == null || friendly.gameObject == null) return;

        try
        {
            FriendlyMovementProfile profile = CaptureMovementProfile(friendly);
            bool shouldEnhance = ModConfig.Enabled.Value;
            if (profile.Enhanced == shouldEnhance) return;

            friendly._runSpeed = shouldEnhance
                ? profile.RunSpeed * MovementMultiplier
                : profile.RunSpeed;
            friendly._maxAttackDistance = shouldEnhance
                ? profile.MaxAttackDistance * TargetRangeMultiplier
                : profile.MaxAttackDistance;
            profile.Enhanced = shouldEnhance;
        }
        catch (Exception exception)
        {
            LogErrorOnce("movement profile update failed", exception);
        }
    }

    private static void PruneFriendlyRegistries()
    {
        var staleIds = new List<int>();
        foreach (KeyValuePair<int, FriendlyEntry> pair in ActiveFriendlies)
        {
            if (!IsUsable(pair.Value.Troll)) staleIds.Add(pair.Key);
        }
        foreach (int id in staleIds)
        {
            if (ActiveFriendlies.TryGetValue(id, out FriendlyEntry entry))
                RemoveFriendlyEntry(id, entry);
        }

        var staleFsms = new List<IntPtr>();
        foreach (KeyValuePair<IntPtr, FriendlyEntry> pair in FriendlyByFsm)
        {
            FriendlyEntry entry = pair.Value;
            try
            {
                if (!IsUsable(entry.Troll) || entry.Troll._fsm == null
                    || entry.Troll._fsm.Pointer != pair.Key)
                    staleFsms.Add(pair.Key);
            }
            catch
            {
                staleFsms.Add(pair.Key);
            }
        }
        foreach (IntPtr pointer in staleFsms) FriendlyByFsm.Remove(pointer);
    }

    private static void DeregisterFriendly(FriendlyTroll friendly)
    {
        try
        {
            if (friendly == null) return;
            int id = friendly.GetInstanceID();
            if (ActiveFriendlies.TryGetValue(id, out FriendlyEntry entry)
                && (entry.Troll == null || entry.Troll.Pointer == friendly.Pointer))
                RemoveFriendlyEntry(id, entry);
            StateMachine fsm = friendly._fsm;
            if (fsm != null) FriendlyByFsm.Remove(fsm.Pointer);
        }
        catch (Exception exception)
        {
            LogErrorOnce("friendly deregistration failed", exception);
        }
    }

    private static uint Mix(uint hash, uint value)
    {
        hash ^= value;
        return unchecked(hash * FnvPrime);
    }

    private static bool TryComputeDesignation(Troll troll, out uint identityHash,
        out short netId)
    {
        identityHash = 0u;
        netId = -1;
        try
        {
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = CampaignSaveData.current;
            NetworkPostbox postbox = NetworkPostbox.Instance;
            IslandSaveData island = campaign?.CurrentIsland;
            if (global == null || campaign == null || island == null || postbox == null)
                return false;

            CRPCHeader header = postbox.GetHeaderFromDynamicObject(troll.gameObject, true);
            if (header == null) return false;
            netId = header.NetID;

            uint hash = FnvOffset;
            hash = Mix(hash, DesignationSchema);
            hash = Mix(hash, unchecked((uint)global.currentCampaign));
            hash = Mix(hash, unchecked((uint)global.currentChallenge));
            hash = Mix(hash, unchecked((uint)campaign.CurrentLand));
            hash = Mix(hash, unchecked((uint)campaign.reign));
            // 2.4 stores an Il2Cpp DateTime here. It improves separation between
            // deleted/restarted island saves, but is not claimed as globally unique.
            long islandStartTicks = island.realStartDateTime.Ticks;
            hash = Mix(hash, unchecked((uint)islandStartTicks));
            hash = Mix(hash, unchecked((uint)(islandStartTicks >> 32)));
            // Dynamic NetID is deliberately treated as the stable sync-slot identity.
            // Pool reuse can therefore repeat a slot's result within the same reign;
            // this is the accepted no-custom-RPC boundary and remains ~10% long-run.
            hash = Mix(hash, unchecked((uint)(ushort)netId));
            identityHash = hash;
            return true;
        }
        catch (Exception exception)
        {
            LogErrorOnce("stable designation identity unavailable", exception);
            return false;
        }
    }

    private static void DesignateFromStableIdentity(Troll troll)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || troll == null || troll.Type != EnemyType.TrollWeak)
            return;

        TrollState state = GetTrollState(troll);
        if (!TryComputeDesignation(troll, out uint identityHash, out short netId))
        {
            if (state.PursuitSteered
                && TryGetPursuitActor(state, out Troll pursuitTroll,
                    out Mover pursuitMover))
            {
                RestoreNativeChase(pursuitTroll, pursuitMover);
            }
            state.PursuitSteered = false;
            state.HasDesignation = false;
            state.Designated = false;
            ActiveCounterTrolls.Remove(troll.GetInstanceID());
            if (!_loggedMissingIdentity)
            {
                _loggedMissingIdentity = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[FriendlyTrollBalance] TrollWeak identity/header unavailable; "
                    + "designation failed closed for this activation.");
            }
            return;
        }

        state.Active = true;
        state.HasDesignation = true;
        if (state.IdentityHash != identityHash) state.DamageLogged = false;
        state.IdentityHash = identityHash;
        state.NetId = netId;
        bool newDesignation = identityHash % 10u == 0u;
        if (!newDesignation && state.PursuitSteered
            && TryGetPursuitActor(state, out Troll previousPursuitTroll,
                out Mover previousPursuitMover))
        {
            RestoreNativeChase(previousPursuitTroll, previousPursuitMover);
        }
        state.Designated = newDesignation;
        if (!newDesignation) state.PursuitSteered = false;
        int id = troll.GetInstanceID();
        if (state.Designated)
        {
            ActiveCounterTrolls[id] = state;
            EnsurePursuitCoordinator();
        }
        else ActiveCounterTrolls.Remove(id);

        if (state.Designated && LoggedSpecials.Add(identityHash))
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                $"[FriendlyTrollBalance] designated TrollWeak counter "
                + $"(net={netId}, hash=0x{identityHash:X8}).");
        }
    }

    private static bool SameDelegate(TargetCacher.SearchConditionDelegate left,
        TargetCacher.SearchConditionDelegate right)
    {
        return left != null && right != null && left.Pointer == right.Pointer;
    }

    private static Troll FindCallingCounterTroll(float position, float range,
        TargetCacher.SearchConditionDelegate condition,
        TargetCacher.SearchConditionDelegate ignore)
    {
        foreach (TrollState state in ActiveCounterTrolls.Values)
        {
            Troll troll = state.Troll;
            if (!state.Active || !state.HasDesignation || !state.Designated
                || !IsUsable(troll) || troll.Type != EnemyType.TrollWeak)
                continue;

            try
            {
                if (Mathf.Abs(troll.transform.position.x - position) > 0.01f
                    || Mathf.Abs(troll.chargeRange - range) > 0.01f
                    || !SameDelegate(condition, troll.TargetPrioritySearchConditionDelegate)
                    || !SameDelegate(ignore, troll.TargetIgnoreSearchConditionDelegate))
                    continue;
                return troll;
            }
            catch (Exception exception)
            {
                LogErrorOnce("counter caller identification failed", exception);
            }
        }

        return null;
    }

    private static void RestoreSquids(SquidFilterState state)
    {
        if (state == null || state.Restored) return;
        state.Restored = true;
        if (state.Manager == null || state.Removed.Count == 0) return;
        try
        {
            EnemyManager current = Managers.Inst?.enemies;
            if (current == null || current.Pointer != state.Manager.Pointer) return;

            var allEnemies = state.Manager.AllEnemies;
            foreach (Squid squid in state.Removed)
            {
                try
                {
                    if (IsUsable(squid) && !allEnemies.Contains(squid))
                        allEnemies.Add(squid);
                }
                catch (Exception exception)
                {
                    LogErrorOnce("one Squid candidate could not be restored", exception);
                }
            }
        }
        catch (Exception exception)
        {
            LogErrorOnce("Squid candidate restoration failed", exception);
        }
    }

    private static void RestoreTargets(TargetInjectionState state)
    {
        if (state == null || state.Restored) return;
        state.Restored = true;
        if (state.Cache == null) return;
        try
        {
            foreach (Damageable damageable in state.Injected)
            {
                try { state.Cache.DeregisterPriorityTarget(damageable); }
                catch (Exception exception)
                {
                    LogErrorOnce("one priority target could not be restored", exception);
                }
            }
        }
        catch (Exception exception)
        {
            LogErrorOnce("priority target restoration failed", exception);
        }
    }

    [HarmonyPatch(typeof(Squid), nameof(Squid.OnEnable))]
    private static class SquidOnEnablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Squid __instance)
        {
            try { ActiveSquids[__instance.GetInstanceID()] = __instance; }
            catch (Exception e) { LogErrorOnce("Squid registration failed", e); }
        }
    }

    [HarmonyPatch(typeof(Squid), nameof(Squid.OnDisable))]
    private static class SquidOnDisablePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Squid __instance)
        {
            try { ActiveSquids.Remove(__instance.GetInstanceID()); }
            catch (Exception e) { LogErrorOnce("Squid deregistration failed", e); }
        }
    }

    [HarmonyPatch(typeof(Squid), "OnDestroy")]
    private static class SquidOnDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Squid __instance)
        {
            try { ActiveSquids.Remove(__instance.GetInstanceID()); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(StateMachine), nameof(StateMachine.StepCoroutine))]
    private static class FriendlyStateMachineStepPatch
    {
        [HarmonyPrefix]
        private static void Prefix(StateMachine __instance,
            out SquidFilterState __state)
        {
            __state = null;
            try
            {
                if (__instance == null
                    || !FriendlyByFsm.TryGetValue(__instance.Pointer,
                        out FriendlyEntry friendlyEntry))
                    return;

                FriendlyTroll friendly = friendlyEntry.Troll;
                if (!IsUsable(friendly) || friendly._fsm == null
                    || friendly._fsm.Pointer != __instance.Pointer)
                {
                    FriendlyByFsm.Remove(__instance.Pointer);
                    return;
                }

                RegisterFriendly(friendly);
                ApplyOrRestoreMovementProfile(friendly);
                if (!ModConfig.Enabled.Value) return;

                Damageable existingTarget = friendly._target;
                if (existingTarget != null
                    && existingTarget.GetComponent<Squid>() != null)
                    friendly._target = null;

                if (ActiveSquids.Count == 0) return;
                EnemyManager manager = Managers.Inst?.enemies;
                if (manager == null || manager.AllEnemies == null) return;

                var allEnemies = manager.AllEnemies;
                var state = new SquidFilterState { Manager = manager };
                __state = state;
                foreach (Squid squid in ActiveSquids.Values)
                {
                    if (IsUsable(squid) && allEnemies.Contains(squid))
                        state.Removed.Add(squid);
                }

                foreach (Squid squid in state.Removed) allEnemies.Remove(squid);
                if (state.Removed.Count == 0) return;

                if (!_loggedSquidFilter)
                {
                    _loggedSquidFilter = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        $"[FriendlyTrollBalance] candidate-stage Squid filter active "
                        + $"({state.Removed.Count} excluded; CrownStealer remains valid).");
                }
            }
            catch (Exception exception)
            {
                RestoreSquids(__state);
                __state = null;
                LogErrorOnce("Squid candidate filtering failed", exception);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            SquidFilterState __state)
        {
            RestoreSquids(__state);
            return __exception;
        }

        [HarmonyPostfix]
        private static void Postfix(SquidFilterState __state)
        {
            RestoreSquids(__state);
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.Init))]
    private static class FriendlyInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendlyTroll __instance)
        {
            RegisterFriendly(__instance);
            ApplyOrRestoreMovementProfile(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.ApplyData))]
    private static class FriendlyApplyDataPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendlyTroll __instance)
        {
            RegisterFriendly(__instance);
            ApplyOrRestoreMovementProfile(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.DeserializeFromData))]
    private static class FriendlyDeserializePatch
    {
        [HarmonyPostfix]
        private static void Postfix(FriendlyTroll __instance)
        {
            RegisterFriendly(__instance);
            ApplyOrRestoreMovementProfile(__instance);
        }
    }

    [HarmonyPatch(typeof(FriendlyTroll), nameof(FriendlyTroll.ResetAndDespawn))]
    private static class FriendlyResetPatch
    {
        [HarmonyPrefix]
        private static void Prefix(FriendlyTroll __instance)
        {
            RestoreMovementProfile(__instance);
            DeregisterFriendly(__instance);
        }
    }

    private static void RestoreMovementProfile(FriendlyTroll friendly)
    {
        if (friendly == null) return;

        try
        {
            int id = friendly.GetInstanceID();
            if (!FriendlyMovementProfiles.TryGetValue(id,
                    out FriendlyMovementProfile profile)
                || profile.Troll == null || profile.Troll.Pointer != friendly.Pointer
                || !profile.Enhanced)
                return;

            friendly._runSpeed = profile.RunSpeed;
            friendly._maxAttackDistance = profile.MaxAttackDistance;
            profile.Enhanced = false;
        }
        catch (Exception exception)
        {
            LogErrorOnce("movement profile restoration failed", exception);
        }
    }

    [HarmonyPatch(typeof(Troll), nameof(Troll.OnEnable))]
    private static class TrollOnEnablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Troll __instance)
        {
            try
            {
                TrollState state = GetTrollState(__instance);
                state.Active = true;
                state.HasDesignation = false;
                state.Designated = false;
                state.IdentityHash = 0u;
                state.NetId = -1;
                state.DamageLogged = false;
                state.Behaviour = null;
                state.Mover = null;
                state.PursuitSteered = false;
                ActiveCounterTrolls.Remove(__instance.GetInstanceID());
            }
            catch (Exception e) { LogErrorOnce("Troll activation reset failed", e); }
        }
    }

    [HarmonyPatch(typeof(Troll), nameof(Troll.OnDisable))]
    private static class TrollOnDisablePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Troll __instance)
        {
            try
            {
                TrollState state = GetTrollState(__instance);
                state.Active = false;
                state.HasDesignation = false;
                state.Designated = false;
                state.IdentityHash = 0u;
                state.NetId = -1;
                state.DamageLogged = false;
                state.Behaviour = null;
                state.Mover = null;
                state.PursuitSteered = false;
                ActiveCounterTrolls.Remove(__instance.GetInstanceID());
            }
            catch (Exception e) { LogErrorOnce("Troll deactivation reset failed", e); }
        }
    }

    [HarmonyPatch(typeof(Troll), "OnDestroy")]
    private static class TrollOnDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Troll __instance)
        {
            try
            {
                int id = __instance.GetInstanceID();
                ActiveCounterTrolls.Remove(id);
                TrollStates.Remove(id);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(EnemyBlueprint), nameof(EnemyBlueprint.Instantiate))]
    private static class EnemyBlueprintInstantiatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnemyBlueprint __instance, Enemy __result)
        {
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
                || __instance == null || __instance.type != EnemyType.TrollWeak
                || __result == null)
                return;
            try
            {
                Troll troll = __result.GetComponent<Troll>();
                if (troll != null && troll.Type == EnemyType.TrollWeak)
                    DesignateFromStableIdentity(troll);
            }
            catch (Exception e) { LogErrorOnce("spawn-time designation failed", e); }
        }
    }

    [HarmonyPatch(typeof(Troll), nameof(Troll.ApplyData))]
    private static class TrollApplyDataPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Troll __instance)
        {
            if (ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth)
                DesignateFromStableIdentity(__instance);
        }
    }

    [HarmonyPatch(typeof(Troll), nameof(Troll.HandleAuthorityChange))]
    private static class TrollAuthorityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Troll __instance, bool newAuthorityState)
        {
            if (ModConfig.Enabled.Value && newAuthorityState)
                DesignateFromStableIdentity(__instance);
        }
    }

    [HarmonyPatch(typeof(TargetCacher), nameof(TargetCacher.GetClosestPriorityTargetWithinRange))]
    private static class PriorityTargetPatch
    {
        [HarmonyPrefix]
        private static void Prefix(TargetCacher __instance, float pos, float range,
            TargetCacher.SearchConditionDelegate conditionDelegate,
            TargetCacher.SearchConditionDelegate ignoreDelegate,
            out TargetInjectionState __state)
        {
            __state = null;
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
                || ActiveCounterTrolls.Count == 0)
                return;

            try
            {
                Troll troll = FindCallingCounterTroll(pos, range,
                    conditionDelegate, ignoreDelegate);
                if (troll == null) return;

                TrollState trollState = GetTrollState(troll);
                if (LoggedTargetQueries.Add(trollState.IdentityHash))
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        $"[FriendlyTrollDiag] stage=counter-query net={trollState.NetId} "
                        + $"hash=0x{trollState.IdentityHash:X8} range={range:F2} "
                        + $"activeFriendlies={ActiveFriendlies.Count}.");
                }

                if (ActiveFriendlies.Count == 0) return;

                var state = new TargetInjectionState { Cache = __instance };
                __state = state;
                var stale = new List<int>();
                foreach (KeyValuePair<int, FriendlyEntry> pair in ActiveFriendlies)
                {
                    FriendlyEntry entry = pair.Value;
                    Damageable damageable = entry.Damageable;
                    if (!IsUsable(entry.Troll) || damageable == null || damageable.isDead)
                    {
                        stale.Add(pair.Key);
                        continue;
                    }

                    if (Mathf.Abs(entry.Troll.transform.position.x - pos) > range
                        || !damageable.IsDamagedBy(troll.damageSource)
                        || __instance._trollPriorityTargets.Contains(damageable))
                        continue;

                    __instance.RegisterPriorityTarget(damageable);
                    state.Injected.Add(damageable);
                }

                foreach (int id in stale)
                {
                    if (ActiveFriendlies.TryGetValue(id, out FriendlyEntry entry))
                        RemoveFriendlyEntry(id, entry);
                }
                if (state.Injected.Count == 0) return;

                if (LoggedTargetInjections.Add(trollState.IdentityHash))
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        $"[FriendlyTrollDiag] stage=friendly-injected net={trollState.NetId} "
                        + $"hash=0x{trollState.IdentityHash:X8} "
                        + $"targets={state.Injected.Count}.");
                }
            }
            catch (Exception exception)
            {
                RestoreTargets(__state);
                __state = null;
                LogErrorOnce("friendly priority injection failed", exception);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            TargetInjectionState __state)
        {
            RestoreTargets(__state);
            return __exception;
        }


        [HarmonyPostfix]
        private static void Postfix(TargetInjectionState __state)
        {
            RestoreTargets(__state);
        }
    }

}

/// <summary>
/// Scaled-time, authority-only steering bridge. All runtime state remains in the
/// static balance registry so the injected IL2CPP component has no managed fields.
/// </summary>
public sealed class FriendlyTrollPursuitCoordinator : MonoBehaviour
{
    public FriendlyTrollPursuitCoordinator(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        PatchDivine_FriendlyTroll.TickPursuit(this);
    }

    private void OnDisable()
    {
        PatchDivine_FriendlyTroll.DisablePursuitCoordinator(this);
    }

    private void OnDestroy()
    {
        PatchDivine_FriendlyTroll.DisablePursuitCoordinator(this);
    }
}
