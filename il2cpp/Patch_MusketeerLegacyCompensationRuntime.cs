using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
#if ANDROID
using Il2CppCoatsink.Common;
#else
using Coatsink.Common;
#endif
using UnityEngine;
#if !ANDROID
using ModDataPaths = BepInEx.Paths;
#endif

namespace KingdomEnhancedMod;

// Issue #223 runtime leaf: the single automatic attempt that turns the authorized legacy plan into
// real native musketeer births, collects the production scene checkpoint and then waits for the
// native disk harvest. The leaf owns orchestration only: every state transition goes through the
// frozen Operator seams (Persistence.PrepareLegacyCompensation, Identity.RegisterLegacyArcher /
// ForgetLegacyBirth / LegacyBirthLife, NativeSave.CancelLegacyBeforeCapture /
// LegacyCompensationAccepted). It never invokes GetID, never constructs ObjectData/PrefabData,
// never writes native files, Prefs or global Prefs, never pauses time and never adds Harmony hooks.
//
// Scene discipline (GLM-reviewed contract, Issue223 runtime gate):
//  * At most one automatic attempt per scene generation / loaded owner / context. The plan file is
//    read at most once per process; a first read failure latches a fault instead of silently
//    re-reading config on later loads. Recovery for a failed attempt is a player reload; nothing
//    is retried in the same scene.
//  * Nothing is created before the native writer barrier is taken (Prepare). Preflight (world,
//    layer, castle anchor, holder prefab, pool, Persistent path, per-x ground probe) is read-only
//    and runs first; the Prepare call and the birth batch then share one main-thread tick.
//  * Every birth is ticketed (exact wrapper pointer, instance id, spawn world, root life) before
//    Identity.RegisterLegacyArcher may ask OwnsBirth. A ticket never validates a foreign scene
//    actor.
//  * The scene capture (IslandSaveData.SerializeObjects with a boxed empty Nullable) runs on a
//    later main-thread tick, after all eight registrations are visible. Completion of the
//    checkpoint is produced by the production save callback (Persistence -> Stage -> CapturePrepared);
//    the leaf verifies the staged receipt and only then issues one SaveAsync with a rooted callback.
//    A "save callback fired" is not success: only NativeSave.LegacyCompensationAccepted (memory
//    flag published by the disk harvest) clears the pending state.
//  * A partial birth batch before any capture is cleaned only when every object this attempt
//    created is still exactly owned (pointer/instance id/world/life). That clean path unregisters,
//    pool-despawns, verifies removal, cancels the barrier and restores the pre-attempt runtime; it
//    keeps a visible fault and never re-attempts in the same scene (a later scene generation may).
//    Recovery differs by branch and must not be conflated: a clean cancellation clears the scene's
//    attempt and a reload may try again, while ANY materialized attempt with an unresolved outcome
//    (unknown spawn object set, failed capture, unconfirmed save, replaced loaded owner) holds its
//    births/barrier and is only cleared by quitting and restarting the process.
//  * The loaded Global owner observed at OnLoaded is part of the attempt's identity: a replacement
//    owner never spawns, captures, saves or clears the old attempt's pending state.
internal static class MusketeerLegacyCompensationRuntime
{
    // Current explicit authorization for this compensation: exactly one plan, eight units. The
    // generic codec cap (MusketeerLegacyCompensationPolicy.MaxUnits) is a codec bound, not an
    // authorization to raise this; the private plan's SourceReservations are re-verified against
    // the live legacy reservations by Prepare.
    private const int AuthorizedUnits = 8;
    private const string PlanDirectory = "KingdomEnhancedMod";
    private const string PlanFileName = "manual-musketeer-compensation.v1.json";

    // Preflight waits for playing world/holder/pool only while the game actually plays; the wait is
    // bounded and a timeout is a visible fault (nothing was created, so a reload may try again).
    private const float MaxPreflightSeconds = 60f;
    private const float CampSpacing = 1f;

    private enum Phase { Waiting, AwaitingCapture, AwaitingHarvest, Accepted, Cancelled, Fault }

    // One exact birth this attempt created. The ticket is recorded before registration and is the
    // only thing OwnsBirth accepts.
    private sealed class Birth
    {
        internal Guid Id;
        internal Guid Request;
        internal IntPtr Pointer;
        internal int GoId;
        internal long World;
        internal long Life;
        internal bool Registered;
        internal Character Character;
    }

    private sealed class Attempt
    {
        internal MusketeerLegacyCompensationPlan Plan;
        internal string ContextKey;
        internal string Epoch;
        internal Guid Request;
        internal MusketeerIdentity.IslandState Old;
        internal MusketeerIdentity.IslandState SceneState;   // the loaded state this attempt belongs to
        internal MusketeerIdentity.IslandState State;        // prepared state once the barrier is taken
        internal string RawJson;
        internal MusketeerLegacyCompensationReceipt Receipt;
        internal readonly List<Birth> Births = new();
        internal Phase Phase;
        internal bool Materialized;          // barrier taken / objects created: never replaced, never transfered
        internal bool CaptureStarted;        // our SerializeObjects call was entered
        internal bool SaveIssued;            // exactly one SaveAsync per attempt
        internal bool CallbackObserved;
        internal System.Action<SaveLoadResult> CallbackManaged;   // managed root until the callback is terminal
        internal Il2CppSystem.Action<SaveLoadResult> CallbackNative; // converted IL2CPP root until terminal
        internal long World;
        internal IntPtr Owner;               // loaded Global owner observed at OnLoaded (identity fence)
        internal IntPtr OwnerPrefs;          // its prefs pointer: the native owner identity is the tuple
        internal string OwnerFile;           // its filename (Ordinal): Global + prefs + filename
        internal float PreflightSeconds;
        internal int Registered;
        internal string Fault;
    }

    private static Attempt _attempt;
    private static int _saveCallbackTerminal;      // published from the native callback thread
    private static int _saveCallbackResult = int.MinValue;
    private static bool _planRead;                 // process latch: the config file is read at most once
    private static MusketeerLegacyCompensationPlan _plan;
    private static string _planFault;

    // ------------------------------------------------------------------ load / eligibility

    // Called by LoadCapture.End after a successful native load. Only the authorized initial state
    // (unknown-paid, ready, unresolved, no epoch, writable, non-virgin) can start a wait; ordinary
    // islands never read the plan file.
    internal static void OnLoaded(MusketeerIdentity.IslandState state, string rawJson)
    {
        try
        {
            Attempt attempt = _attempt;
            if (attempt != null)
            {
                if (attempt.Materialized)
                {
                    // A load replaced the world this attempt created objects in. The tickets, the
                    // barrier and any staged receipt stay fail-closed; nothing transfers to the new
                    // scope and no new attempt is started while this one is materialized.
                    if (attempt.Phase == Phase.AwaitingCapture) Fail(attempt, "legacy-scene-replaced-before-capture");
                    else if (attempt.Phase == Phase.AwaitingHarvest) Fail(attempt, "legacy-scene-replaced-before-native-acceptance");
                    return;
                }
                if (ReferenceEquals(attempt.SceneState, state)) return;
                _attempt = null;   // a waiting attempt for a discarded scene created nothing
            }
            if (!Eligible(state) || string.IsNullOrEmpty(rawJson)) return;
            if (!TryEnsurePlan()) return;
            if (_plan == null || !string.Equals(_plan.Context, state.ContextKey, StringComparison.Ordinal)) return;
            // The loaded Global owner this attempt belongs to is frozen now as the full native
            // identity tuple (Global pointer + prefs pointer + filename); an incomplete or null one
            // is never guessed later.
            GlobalSaveData loaded = GlobalSaveData.loaded;
            var prefs = loaded != null ? loaded.prefs : null;
            string file = GlobalSaveData.filename;
            if (loaded == null || prefs == null || prefs.Pointer == IntPtr.Zero || string.IsNullOrEmpty(file))
            {
                Log("legacy-compensation-owner-missing");
                return;
            }
            _attempt = new Attempt
            {
                Plan = _plan,
                ContextKey = state.ContextKey,
                Old = state,
                SceneState = state,
                State = state,
                RawJson = rawJson,
                Phase = Phase.Waiting,
                World = MusketeerIdentity.WorldKey(),
                Owner = loaded.Pointer,
                OwnerPrefs = prefs.Pointer,
                OwnerFile = file
            };
            Log("legacy-compensation-waiting");
        }
        catch (Exception e)
        {
            LogOnce("legacy-on-loaded", e);
            Fail(_attempt, "legacy-on-loaded-exception");
        }
    }

    private static bool Eligible(MusketeerIdentity.IslandState state)
    {
        if (state == null || !state.Ready || state.ReadOnly || !state.Unresolved) return false;
        if (state.MatchKind != "unknown-paid" || state.Epoch != null || state.GenerationPending) return false;
        return MusketeerAccess.Enabled;
    }

    // The private plan is configuration, read once per process. A failure (missing file, invalid
    // JSON, wrong units) latches a fault and is never silently retried by another OnLoaded;
    // restarting the process may read it again.
    private static bool TryEnsurePlan()
    {
        if (_planRead) return _plan != null;
        _planRead = true;
        try
        {
            string path = Path.Combine(ModDataPaths.ConfigPath ?? string.Empty, PlanDirectory, PlanFileName);
            byte[] bytes = ReadBounded(path, MusketeerLegacyCompensationPolicy.MaxPlanJsonBytes);
            if (bytes == null)
            {
                _planFault = "plan-file-unavailable";
                Log("legacy-compensation-plan-unavailable");
                return false;
            }
            if (!MusketeerLegacyCompensationPolicy.TryParsePlan(bytes, out var plan, out string reason))
            {
                _planFault = "plan-invalid";
                Log("legacy-compensation-plan-invalid:" + reason);
                return false;
            }
            if (plan.Units != AuthorizedUnits)
            {
                _planFault = "plan-units-not-authorized";
                Log("legacy-compensation-plan-units");
                return false;
            }
            _plan = plan;
            Log("legacy-compensation-plan-loaded");
            return true;
        }
        catch (Exception e)
        {
            _planFault = "plan-read-exception";
            LogOnce("legacy-compensation-plan-read", e);
            return false;
        }
    }

    // Safe bounded read: the file must exist, be non-empty and fit the codec's documented limit.
    // A missing/unreadable file is a clean null (the plan is optional private configuration), not
    // an exception path.
    private static byte[] ReadBounded(string path, int limit)
    {
        if (string.IsNullOrEmpty(path) || limit <= 0) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length <= 0 || length > limit) return null;
            var buffer = new byte[(int)length];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }
            return total == buffer.Length ? buffer : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // ------------------------------------------------------------------ per-frame tick

    // Called once per frame from Identity.Tick before its defense/sweep. Without an active attempt
    // this returns immediately: no disk read, no scene scan, no allocation.
    internal static void Tick(MusketeerIdentity.IslandState state)
    {
        try
        {
            Attempt attempt = _attempt;
            if (attempt == null) return;
            ObserveTerminal(attempt);
            ObserveAccepted(attempt);
            if (attempt.Phase == Phase.Accepted || attempt.Phase == Phase.Cancelled || attempt.Phase == Phase.Fault) return;
            if (attempt.Phase == Phase.Waiting) TryBegin(attempt, state);
            else if (attempt.Phase == Phase.AwaitingCapture) TryCapture(attempt, state);
        }
        catch (Exception e)
        {
            LogOnce("legacy-tick", e);
            Fail(_attempt, "legacy-tick-exception");
        }
    }

    // The native SaveAsync continuation may run off the main thread: it publishes the terminal fact
    // only. It never touches Unity, the attempt's orchestration state or any file.
    private static void OnNativeSaveTerminal(SaveLoadResult result)
    {
        try
        {
            Interlocked.Exchange(ref _saveCallbackResult, (int)result);
            Interlocked.Exchange(ref _saveCallbackTerminal, 1);
        }
        catch { }
    }

    private static void ObserveTerminal(Attempt attempt)
    {
        if (!attempt.SaveIssued || attempt.CallbackObserved) return;
        if (Volatile.Read(ref _saveCallbackTerminal) == 0) return;
        attempt.CallbackObserved = true;
        // Terminal callback: both roots are no longer needed. This says nothing about disk
        // acceptance; success is the harvest-owned flag below.
        attempt.CallbackManaged = null;
        attempt.CallbackNative = null;
        Log("legacy-compensation-save-callback:" + Volatile.Read(ref _saveCallbackResult));
    }

    // The only success signal: the owner-scoped in-memory flag published by the one-time disk
    // harvest. It is independent of which writer produced the terminal save (the leaf's SaveAsync or
    // an external player/autosave); a callback status alone is not proof. The fences are the exact
    // attempt (materialized, complete receipt, current state, same world, original loaded owner).
    private static void ObserveAccepted(Attempt attempt)
    {
        if (!attempt.Materialized || attempt.Receipt == null || !attempt.Receipt.Complete) return;
        if (attempt.Phase == Phase.Accepted || attempt.Phase == Phase.Cancelled) return;
        if (!ReferenceEquals(attempt.State, MusketeerIdentity.Current)) return;
        if (MusketeerIdentity.WorldKey() != attempt.World) return;
        if (!SameOwner(attempt)) return;
        if (!MusketeerNativeSave.LegacyCompensationAccepted(attempt.Request, attempt.ContextKey, attempt.Epoch)) return;
        if (attempt.State != null) attempt.State.CompensationPending = false;
        attempt.Phase = Phase.Accepted;
        Log("legacy-compensation-accepted");
    }

    // ------------------------------------------------------------------ prepare + birth batch

    private static void TryBegin(Attempt attempt, MusketeerIdentity.IslandState state)
    {
        if (!ReferenceEquals(state, attempt.State) || !ReferenceEquals(MusketeerIdentity.Current, attempt.State)) return;
        // The attempt belongs to the loaded Global owner identity observed at OnLoaded (Global +
        // prefs + filename); a replaced owner never prepares or creates anything for it.
        if (!SameOwner(attempt))
        {
            Fail(attempt, "legacy-owner-replaced");
            return;
        }
        if (!MusketeerAccess.Playing) return;
        if (IslandSaveData.isSavingGame) return;   // the writer barrier below covers any concurrent writer
        float delta = Time.unscaledDeltaTime;
        if (delta > 0f) attempt.PreflightSeconds += delta;
        if (attempt.PreflightSeconds > MaxPreflightSeconds)
        {
            Fail(attempt, "legacy-preflight-timeout");
            return;
        }
        if (MusketeerIdentity.WorldKey() == 0) return;

        Managers managers = Managers.Inst;
        if (managers == null || managers.world == null || managers.kingdom == null || managers.holder == null) return;
        Transform layer = managers.world.gameLayer;
        if (layer == null || layer.gameObject == null || !layer.gameObject.activeInHierarchy) return;

        // Inside-camp anchor: the castle transform x; every birth is spaced around it. The y of each
        // birth comes from a local downward probe onto the real ground layer at that exact x (the
        // established placement probe); a probe failure defers instead of guessing a height.
        Castle castle = managers.kingdom.castle;
        Transform anchor = castle != null ? castle.transform : null;
        if (anchor == null) return;
        if (!anchor.IsChildOf(layer)) return;
        float anchorX = anchor.position.x;
        if (!float.IsFinite(anchorX)) return;

        Character prefab = managers.holder.GetCharacterByTag("Archer");
        if (prefab == null || prefab.gameObject == null || prefab.GetComponent<Archer>() == null) return;
        GameObject prefabRoot = prefab.gameObject;
        Persistent prefabPersistent = prefab.GetComponent<Persistent>();
        if (prefabPersistent == null
            || !string.Equals(prefabPersistent.path, MusketeerLegacyGrantCapture.MusketeerPrefabPath, StringComparison.Ordinal)) return;

        PatchRoles_Castle.EnsurePoolForCharacter("Archer");
        Pool pool = Pool.GetPoolFromPrefabAsset(prefabRoot);
        if (pool == null || pool.prefab == null || pool.prefab.Pointer != prefabRoot.Pointer) return;

        int units = attempt.Plan.Units;
        if (units != AuthorizedUnits) return;
        var xs = new float[units];
        var ys = new float[units];
        for (int i = 0; i < units; i++)
        {
            float x = anchorX + (i - (units - 1) / 2f) * CampSpacing;
            if (!CoinCourierGround.TryProbe(x, out float groundY) || !float.IsFinite(groundY)) return;
            xs[i] = x;
            ys[i] = groundY;
        }

        var grants = new Guid[units];
        for (int i = 0; i < units; i++) grants[i] = Guid.NewGuid();

        // Operator gate: rechecks the live legacy source (hash, reservations, context), takes the
        // native writer barrier and stages the empty-baseline bootstrap; no Pending bytes exist.
        if (!MusketeerPersistence.PrepareLegacyCompensation(attempt.Old, attempt.RawJson, attempt.Plan, grants,
            out var prepared, out var pending, out string reason))
        {
            Fail(attempt, "legacy-prepare:" + reason);
            return;
        }
        attempt.State = prepared;
        attempt.Epoch = prepared.Epoch;
        attempt.Request = attempt.Plan.RequestId;
        attempt.Receipt = pending;
        attempt.Materialized = true;
        attempt.World = MusketeerIdentity.WorldKey();

        bool complete = true;
        try
        {
            for (int i = 0; i < units; i++)
            {
                Character character = Pool.Spawn<Character>(prefab, new Vector3(xs[i], ys[i], 0f),
                    Quaternion.identity, layer, true);
                if (character == null || character.gameObject == null)
                {
                    // A native Spawn that returned null or a wrapper without a GameObject leaves the
                    // created-object set unknown (an anonymous actor may exist): cleanup, cancellation
                    // and any further native write are forbidden and the whole attempt stays visible.
                    Fail(attempt, "legacy-spawn-unknown-object");
                    return;
                }
                GameObject root = character.gameObject;
                if (root.Pointer == prefabRoot.Pointer)
                {
                    // The shared prefab asset: never operate on, move, despawn or clean it, and never
                    // treat this outcome as a clean scene.
                    Fail(attempt, "legacy-spawn-unknown-object");
                    return;
                }

                // Ticket first: Identity.RegisterLegacyArcher will ask OwnsBirth for exactly this.
                var birth = new Birth
                {
                    Id = grants[i],
                    Request = attempt.Request,
                    Pointer = root.Pointer,
                    GoId = root.GetInstanceID(),
                    World = MusketeerIdentity.WorldKey(),
                    Life = MusketeerIdentity.LegacyBirthLife(character),
                    Character = character
                };
                attempt.Births.Add(birth);
                if (birth.Life == 0)
                {
                    // No root life means ownership can never be proven again: leave everything
                    // visible instead of attempting any cleanup.
                    Fail(attempt, "legacy-birth-life");
                    return;
                }
                if (!ValidBirth(character, prefabRoot)) { complete = false; break; }
                if (!MusketeerIdentity.RegisterLegacyArcher(prepared, character, grants[i], attempt.Request, out _))
                { complete = false; break; }
                birth.Registered = true;
                attempt.Registered++;
            }
        }
        catch (Exception e)
        {
            // A native Spawn/create throw leaves the created-object set unknown ("null/throw is
            // unknown"): "all of ours removed" cannot be proven, so cleanup and cancellation are
            // forbidden and the whole attempt stays visible.
            LogOnce("legacy-spawn-unknown", e);
            Fail(attempt, "legacy-spawn-unknown");
            return;
        }

        if (!complete)
        {
            CleanupOrFault(attempt);
            return;
        }
        attempt.Phase = Phase.AwaitingCapture;
        Log("legacy-compensation-births:" + attempt.Registered + "/" + units);
    }

    // Every rejection here is a real "we spawned something that is not a live same-world native
    // Archer with the recorded Persistent path" outcome; the caller treats it as a partial batch.
    private static bool ValidBirth(Character character, GameObject prefabRoot)
    {
        try
        {
            GameObject root = character.gameObject;
            if (root == null || root.Pointer == prefabRoot.Pointer) return false;
            if (!root.activeInHierarchy || !MusketeerAccess.InWorld(root)) return false;
            if (character.GetComponent<Archer>() == null) return false;
            Persistent persistent = character.GetComponent<Persistent>();
            if (persistent == null
                || !string.Equals(persistent.path, MusketeerLegacyGrantCapture.MusketeerPrefabPath, StringComparison.Ordinal)) return false;
            if (!persistent.ShouldPersist() || HeroRecruitment.HasPurchasedCareer(character)) return false;
            return true;
        }
        catch { return false; }
    }

    // Pre-capture cleanup, all-or-nothing: only when every object this attempt created is still
    // exactly owned is each one unregistered (ForgetLegacyBirth), pool-despawned and verified
    // removed; only then may the barrier be cancelled and the pre-attempt runtime restored.
    private static void CleanupOrFault(Attempt attempt)
    {
        foreach (Birth birth in attempt.Births)
            if (!OwnsBirth(birth.Character, birth.Id, birth.Request))
            {
                Fail(attempt, "legacy-cleanup-ownership");
                return;
            }
        foreach (Birth birth in attempt.Births)
        {
            Character character = birth.Character;
            GameObject root = character != null ? character.gameObject : null;
            if (!MusketeerIdentity.ForgetLegacyBirth(attempt.State, character, birth.Id, birth.Request))
            {
                Fail(attempt, "legacy-cleanup-unregister");
                return;
            }
            try
            {
                if (root != null) Pool.Despawn(root, true);
            }
            catch (Exception e)
            {
                LogOnce("legacy-cleanup-despawn", e);
                Fail(attempt, "legacy-cleanup-despawn");
                return;
            }
            if (root != null && root.activeInHierarchy)
            {
                Fail(attempt, "legacy-cleanup-not-removed");
                return;
            }
        }
        if (!MusketeerNativeSave.CancelLegacyBeforeCapture(attempt.Request, attempt.ContextKey, attempt.Epoch, out string reason))
        {
            Fail(attempt, "legacy-cleanup-cancel:" + reason);
            return;
        }
        // Only a successful cancellation may restore: release the prepared state's bindings and
        // put the untouched pre-attempt state back (no old id/epoch/Baseline was ever altered).
        if (attempt.State != null) MusketeerIdentity.ReleaseAll(attempt.State);
        MusketeerIdentity.InstallState(attempt.ContextKey, attempt.Old);
        attempt.Births.Clear();
        attempt.Receipt = null;
        attempt.Materialized = false;
        attempt.Phase = Phase.Cancelled;
        attempt.Fault = "legacy-cancelled-reload";
        Log("legacy-compensation-cancelled");
    }

    // ------------------------------------------------------------------ scene capture + one save

    private static void TryCapture(Attempt attempt, MusketeerIdentity.IslandState state)
    {
        if (!ReferenceEquals(state, attempt.State) || !ReferenceEquals(MusketeerIdentity.Current, attempt.State)) return;
        if (!SameOwner(attempt))
        {
            Fail(attempt, "legacy-owner-replaced");
            return;
        }
        if (!MusketeerAccess.TrackAllowed) return;
        if (attempt.Births.Count != attempt.Plan.Units || attempt.Registered != attempt.Plan.Units)
        {
            Fail(attempt, "legacy-births-incomplete");
            return;
        }
        foreach (Birth birth in attempt.Births)
            if (!OwnsBirth(birth.Character, birth.Id, birth.Request))
            {
                Fail(attempt, "legacy-births-lost-before-capture");
                return;
            }
        if (MusketeerIdentity.WorldKey() != attempt.World)
        {
            Fail(attempt, "legacy-world-replaced-before-capture");
            return;
        }
        if (!attempt.CaptureStarted && attempt.Receipt != null && attempt.Receipt.Complete)
        {
            // A production save (player/autosave) already collected the complete checkpoint and owns
            // the write pipeline; never issue a second native save for the same attempt.
            attempt.Phase = Phase.AwaitingHarvest;
            Log("legacy-compensation-capture-preempted");
            return;
        }
        if (IslandSaveData.isSavingGame) return;

        GlobalSaveData loaded = GlobalSaveData.loaded;
        if (loaded == null)
        {
            Fail(attempt, "legacy-save-loaded-missing");
            return;
        }
        attempt.CaptureStarted = true;
        var empty = new Il2CppSystem.Nullable<int>();
        try
        {
            if (empty.HasValue)
            {
                Fail(attempt, "legacy-nullable-not-empty");
                return;
            }
            // Actual 2.4 scene capture: the native code reads the live campaign/land/challenge and
            // runs the unchanged production Save/GetID boundaries. Land is never overridden and no
            // id is ever requested by hand.
            IslandSaveData.SerializeObjects(empty);
        }
        catch (Exception e)
        {
            LogOnce("legacy-capture", e);
            Fail(attempt, "legacy-capture-exception");
            return;
        }
        finally { GC.KeepAlive(empty); }

        // The loaded owner identity (Global + prefs + filename) must be unchanged across the
        // capture; a swapped leg never receives this attempt's write.
        if (!SameOwner(attempt))
        {
            Fail(attempt, "legacy-save-loaded-replaced");
            return;
        }
        // Completion proof is owned by the production callback (Stage -> CapturePrepared), never
        // constructed here. Missing/incomplete captures are a visible fault with the hard barrier.
        if (!TryGetPendingReceipt(attempt.State, out var staged) || !staged.Complete)
        {
            Fail(attempt, "legacy-capture-incomplete");
            return;
        }

        System.Action<SaveLoadResult> managed = OnNativeSaveTerminal;
        attempt.CallbackManaged = managed;
        Il2CppSystem.Action<SaveLoadResult> native = (Il2CppSystem.Action<SaveLoadResult>)managed;
        attempt.CallbackNative = native;
        attempt.SaveIssued = true;
        attempt.Phase = Phase.AwaitingHarvest;
        try
        {
            loaded.SaveAsync(native);
        }
        catch (Exception e)
        {
            LogOnce("legacy-save-async", e);
            Fail(attempt, "legacy-save-async-exception");
            return;
        }
        Log("legacy-compensation-save-issued");
    }

    // ------------------------------------------------------------------ frozen seams

    // Ownership ticket check: wrapper, instance id, spawn world and root life must still be exact.
    // A life refresh (pool recycle), a foreign world or any other actor is never a birth.
    internal static bool OwnsBirth(Character character, Guid id, Guid request)
    {
        try
        {
            Attempt attempt = _attempt;
            if (attempt == null || !attempt.Materialized || character == null || character.gameObject == null) return false;
            if (id == Guid.Empty || request == Guid.Empty || request != attempt.Request) return false;
            Birth birth = null;
            for (int i = 0; i < attempt.Births.Count; i++)
                if (attempt.Births[i].Id == id) { birth = attempt.Births[i]; break; }
            if (birth == null || birth.Request != request) return false;
            GameObject root = character.gameObject;
            if (root.Pointer != birth.Pointer) return false;
            if (root.GetInstanceID() != birth.GoId) return false;
            if (MusketeerIdentity.WorldKey() != birth.World) return false;
            if (birth.Life == 0 || MusketeerIdentity.LegacyBirthLife(character) != birth.Life) return false;
            return true;
        }
        catch { return false; }
    }

    // In-memory pending source filter only: exactly the authorized source epoch of the live attempt
    // is excluded until the complete receipt is staged into the native Prefs archive, after which
    // the persisted ledger (Archive.HistoricalOnly) owns the exclusion.
    internal static bool ExcludesSourceEpoch(string context, string epoch)
    {
        try
        {
            Attempt attempt = _attempt;
            if (attempt == null || !attempt.Materialized || attempt.Plan == null || attempt.Receipt == null) return false;
            if (attempt.Receipt.Complete) return false;
            if (!string.Equals(attempt.ContextKey, context, StringComparison.Ordinal)) return false;
            return string.Equals(attempt.Plan.SourceEpoch, epoch, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // The same attempt/state/request/context/epoch always returns the frozen receipt (pending or
    // staged complete). No budget is recomputed and nothing is allocated when the attempt is done.
    internal static bool TryGetPendingReceipt(MusketeerIdentity.IslandState state, out MusketeerLegacyCompensationReceipt pending)
    {
        pending = null;
        try
        {
            Attempt attempt = _attempt;
            if (attempt == null || attempt.Receipt == null || state == null) return false;
            if (!ReferenceEquals(state, attempt.State) || !state.CompensationPending) return false;
            if (state.CompensationRequest != attempt.Request) return false;
            if (!string.Equals(state.ContextKey, attempt.ContextKey, StringComparison.Ordinal)) return false;
            if (!string.Equals(state.Epoch, attempt.Epoch, StringComparison.Ordinal)) return false;
            pending = attempt.Receipt;
            return true;
        }
        catch { pending = null; return false; }
    }

    // Called by the production save path only after the actual capture + Stage succeeded. This
    // replaces the pending receipt and never claims that any disk write was verified.
    internal static void CapturePrepared(MusketeerLegacyCompensationReceipt complete)
    {
        try
        {
            Attempt attempt = _attempt;
            if (attempt == null || complete == null || !complete.Complete || attempt.Plan == null) return;
            if (complete.Plan == null || complete.Plan.RequestId != attempt.Request) return;
            if (!string.Equals(complete.Plan.Context, attempt.ContextKey, StringComparison.Ordinal)) return;
            if (!string.Equals(complete.MigrationEpoch, attempt.Epoch, StringComparison.Ordinal)) return;
            attempt.Receipt = complete;
            Log("legacy-compensation-staged");
        }
        catch (Exception e) { LogOnce("legacy-capture-prepared", e); }
    }

    // Bounded progress/error text for the identity status line while the native owner reports a
    // pending compensation. Not a per-frame log.
    internal static string StatusText
    {
        get
        {
            try
            {
                Attempt attempt = _attempt;
                if (attempt == null)
                    return _planFault != null ? "旧档火枪补偿：配置未就绪（" + _planFault + "）" : "旧档火枪补偿：等待处理";
                switch (attempt.Phase)
                {
                    case Phase.Waiting: return "旧档火枪补偿：等待世界/原生角色池就绪";
                    case Phase.AwaitingCapture: return "旧档火枪补偿：8 名已登记，等待真实场景采集";
                    case Phase.AwaitingHarvest:
                        return attempt.CallbackObserved || Volatile.Read(ref _saveCallbackTerminal) != 0
                            ? "旧档火枪补偿：原生保存未确认，保持禁止购买（退出游戏后重新启动）"
                            : "旧档火枪补偿：已请求原生保存，等待落盘确认";
                    case Phase.Accepted: return "旧档火枪补偿：已完成一次发放";
                    case Phase.Cancelled: return "旧档火枪补偿：本次未发放，重载岛屿后可重新尝试";
                    default:
                        if (attempt.Fault == null) return "旧档火枪补偿：等待处理";
                        return attempt.Materialized
                            ? "旧档火枪补偿：" + attempt.Fault + "（保持禁止购买，需退出游戏后重新启动）"
                            : "旧档火枪补偿：" + attempt.Fault + "（重载岛屿后可重新尝试）";
                }
            }
            catch { return "旧档火枪补偿状态暂不可用"; }
        }
    }

    // The native owner identity is the (Global pointer, prefs pointer, filename) tuple
    // (NativeSave.CurrentOwnerIs); every fence re-reads it and never adopts a swapped leg.
    private static bool SameOwner(Attempt attempt)
    {
        GlobalSaveData loaded = GlobalSaveData.loaded;
        if (loaded == null || loaded.Pointer != attempt.Owner) return false;
        var prefs = loaded.prefs;
        if (prefs == null || prefs.Pointer != attempt.OwnerPrefs) return false;
        return string.Equals(GlobalSaveData.filename, attempt.OwnerFile, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static void Fail(Attempt attempt, string reason)
    {
        if (attempt == null) return;
        if (attempt.Phase != Phase.Accepted && attempt.Phase != Phase.Cancelled)
        {
            attempt.Phase = Phase.Fault;
            attempt.Fault ??= reason;
        }
        Log("legacy-compensation-fault:" + reason);
    }

    private static void Log(string key) => MusketeerIdentity.Log(key, null);

    private static void LogOnce(string key, Exception error) => MusketeerIdentity.Log(key, error);
}
