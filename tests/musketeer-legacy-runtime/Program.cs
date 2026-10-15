using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using KingdomEnhancedMod;

// Scenario regression suite for the Issue #223 compensation-runtime leaf. The real production leaf
// (il2cpp/Patch_MusketeerLegacyCompensationRuntime.cs) and the real pure kernel are compiled here
// against platform/core-stage stubs (Stubs.cs). This suite verifies the leaf's orchestration
// decisions: eligibility, the process-latched plan read, the prepare+birth batch ordering, ticket
// ownership, the capture/one-save sequence, the acceptance fence, the bounded pre-capture cleanup
// and every fail-closed branch. It is NOT native evidence: Pool.Spawn<Character> closure, the real
// SerializeObjects scene capture, the real save harvest and coldload acceptance stay for the root's
// isolated native gate.
internal static class Program
{
    // Synthetic fixtures only: no player context, legacy archive hash or source epoch appears in
    // this public test source.
    private const string Context = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SourceEpoch = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OtherContext = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string SourceSha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string ArcherPath = "Prefabs/Characters/Archer";
    private const string PlanRequest = "00112233445566778899aabbccddee01";
    private const string LoadJson = "{\"loaded\":\"native-island\"}";

    private static int _checks;
    private static int _failed;

    private static void Check(bool condition, string reason)
    {
        _checks++;
        if (condition) return;
        _failed++;
        Console.WriteLine("FAIL: " + reason);
    }

    private static int Main()
    {
        FastReturnWithoutAttempt();
        PlanGates();
        HappyPath();
        PartialBatchCleanAndRetryPolicy();
        CleanupRefusedWhenOwnershipLost();
        SpawnThrowStaysFailClosedWithOwnershipFences();
        CaptureIncompleteIsVisibleAndNotReissued();
        PreflightTimeoutIsBounded();
        FenceQueries();
        ExternalHarvestCompletesWithoutLeafSave();
        SpawnNullKeepsBarrierAndBirths();
        OwnerReplacementFences();
        OwnerTupleFences();

        Console.WriteLine("checks=" + _checks + " passed=" + (_checks - _failed) + " failed=" + _failed);
        Console.WriteLine(_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ scenario 1: fast return

    private static void FastReturnWithoutAttempt()
    {
        ResetAll();
        MusketeerIdentity.IslandState state = EligibleState();
        MusketeerIdentity.CurrentState = state;
        MusketeerLegacyCompensationRuntime.Tick(state);
        Check(MusketeerPersistence.PrepareCalls == 0, "tick without attempt prepares nothing");
        Check(Pool.SpawnCalls.Count == 0, "tick without attempt spawns nothing");
        Check(IslandSaveData.SerializeCalls == 0 && CoinCourierGround.ProbeCalls == 0, "tick without attempt does no capture/probe");
        Check(MusketeerIdentity.Logs.Count == 0, "tick without attempt logs nothing");
    }

    // ------------------------------------------------------------------ scenario 2: plan gates

    private static void PlanGates()
    {
        // (a) ineligible islands never read the plan file.
        ResetAll();
        WritePlanValid();
        MusketeerIdentity.IslandState exact = EligibleState();
        exact.MatchKind = "exact"; exact.Unresolved = false; exact.Epoch = SourceEpoch;
        MusketeerLegacyCompensationRuntime.OnLoaded(exact, LoadJson);
        Check(!MusketeerIdentity.Logged("legacy-compensation-plan-loaded"), "ineligible state does not read the plan");
        Check(MusketeerPersistence.PrepareCalls == 0, "ineligible state starts no attempt");

        // (b) a missing plan file latches: a later eligible load does not silently re-read.
        ResetAll();
        SetConfigDir();
        File.Delete(PlanPath);
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(MusketeerIdentity.Logged("legacy-compensation-plan-unavailable"), "missing plan is visible");
        Check(!MusketeerIdentity.Logged("legacy-compensation-waiting"), "missing plan starts no wait");
        WritePlanValid();
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(!MusketeerIdentity.Logged("legacy-compensation-waiting"), "plan read failure is latched for the process");
        Check(MusketeerIdentity.LogCount("legacy-compensation-plan-unavailable") == 1, "plan failure logged once");

        // (c) parsed but unauthorized unit count is refused (cap 32 is a codec bound, not a grant).
        ResetAll();
        SetConfigDir();
        WritePlan(reservations: 18, units: 9);
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(MusketeerIdentity.Logged("legacy-compensation-plan-units"), "units != 8 is refused");
        Check(!MusketeerIdentity.Logged("legacy-compensation-waiting"), "unauthorized units start no wait");

        // (d) a plan larger than the bounded read is refused.
        ResetAll();
        SetConfigDir();
        WritePlan(padding: " " + new string(' ', 17 * 1024));
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(!MusketeerIdentity.Logged("legacy-compensation-waiting"), "oversized plan starts no wait");

        // (e) a valid plan for another context starts no wait for this island.
        ResetAll();
        SetConfigDir();
        WritePlan(context: OtherContext);
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(MusketeerIdentity.Logged("legacy-compensation-plan-loaded"), "valid plan parses");
        Check(!MusketeerIdentity.Logged("legacy-compensation-waiting"), "foreign context plan starts no wait");

        // (f) a valid plan matching the context waits exactly once per scene generation: the same
        // loaded state never queues twice, a new load replaces the (non-materialized) wait.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        MusketeerIdentity.IslandState first = EligibleState();
        MusketeerLegacyCompensationRuntime.OnLoaded(first, LoadJson);
        Check(MusketeerIdentity.LogCount("legacy-compensation-waiting") == 1, "eligible load starts one wait");
        MusketeerLegacyCompensationRuntime.OnLoaded(first, LoadJson);
        Check(MusketeerIdentity.LogCount("legacy-compensation-waiting") == 1, "same loaded state never queues a second wait");
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(MusketeerIdentity.LogCount("legacy-compensation-waiting") == 2, "a new scene generation replaces the non-materialized wait");
    }

    // ------------------------------------------------------------------ scenario 3: happy path

    private static void HappyPath()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        Check(MusketeerIdentity.LogCount("legacy-compensation-plan-loaded") == 1, "plan read exactly once");
        Check(MusketeerIdentity.Logged("legacy-compensation-waiting"), "waiting started");

        // Tick 1: preflight + Prepare + birth batch in one main-thread tick.
        MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerPersistence.PrepareCalls == 1, "prepare called once");
        Check(ReferenceEquals(MusketeerPersistence.OldArg, scene) && MusketeerPersistence.JsonArg == LoadJson, "prepare receives the exact scene and load json");
        Check(MusketeerPersistence.PlanArg != null && MusketeerPersistence.PlanArg.Context == Context, "prepare receives the parsed plan");
        Check(MusketeerPersistence.GrantsArg.Count == 8, "eight grants allocated");
        Check(new HashSet<Guid>(MusketeerPersistence.GrantsArg).Count == 8 && !MusketeerPersistence.GrantsArg.Contains(Guid.Empty), "grants unique and non-empty");
        Check(PatchRoles_Castle.EnsurePoolCalls == 1 && PatchRoles_Castle.LastTag == "Archer", "archer pool ensured before spawn");
        Check(CoinCourierGround.ProbeCalls == 8, "eight ground probes");
        Check(CoinCourierGround.Probes.Count == 8 && CoinCourierGround.Probes[0] == 70.5f && CoinCourierGround.Probes[7] == 77.5f, "probes spaced around the castle x");
        Check(Pool.SpawnCalls.Count == 8, "eight spawns attempted");
        bool spawnShape = true;
        for (int i = 0; i < Pool.SpawnCalls.Count; i++)
        {
            var call = Pool.SpawnCalls[i];
            if (!ReferenceEquals(call.Prefab, _prefab) || !call.Activate) spawnShape = false;
            if (!ReferenceEquals(call.Parent, Managers.Inst.world.gameLayer)) spawnShape = false;
            if (call.Position.x != 70.5f + i || call.Position.y != CoinCourierGround.ProbeY) spawnShape = false;
        }
        Check(spawnShape, "spawn shape: holder prefab, probed pose, world layer, activate");
        Check(MusketeerIdentity.RegisterCalls.Count == 8, "eight registrations attempted");
        bool ownedAtRegister = true;
        foreach (var call in MusketeerIdentity.RegisterCalls) ownedAtRegister &= call.Owned;
        Check(ownedAtRegister, "ticket exists before RegisterLegacyArcher (OwnsBirth true at every register)");
        Check(MusketeerIdentity.Logged("legacy-compensation-births:8/8"), "registered birth count logged");
        var prepared = MusketeerPersistence.LastPrepared;
        Check(prepared != null && prepared.CompensationPending && prepared.Epoch == MusketeerPersistence.LastEpoch, "prepared state pending with migration epoch");
        Check(ReferenceEquals(MusketeerIdentity.CurrentState, prepared), "prepared state installed");
        Check(MusketeerLegacyCompensationRuntime.ExcludesSourceEpoch(Context, SourceEpoch), "pending attempt excludes the authorized source epoch");
        Check(!MusketeerLegacyCompensationRuntime.ExcludesSourceEpoch(Context, OtherContext), "other epochs are not excluded");
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "no capture before every birth is registered");

        // Tick 2: the real scene-capture call shape, completed by the production callback path.
        bool emptyNullable = false;
        IslandSaveData.SerializeHook = nullable =>
        {
            emptyNullable = !nullable.HasValue;
            CompleteViaProductionCallback();
        };
        GlobalSaveData.SaveAsyncHook = callback => _savedCallback = callback;
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(IslandSaveData.SerializeCalls == 1, "scene capture issued once");
        Check(emptyNullable, "SerializeObjects receives the boxed empty Nullable (never a hand-written land)");
        Check(GlobalSaveData.SaveAsyncCalls == 1 && _savedCallback != null, "one rooted SaveAsync issued after the staged complete");
        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out var staged) && staged.Complete, "staged receipt is complete");
        Check(!MusketeerLegacyCompensationRuntime.ExcludesSourceEpoch(Context, SourceEpoch), "staged complete hands the exclusion to the persisted ledger");

        // Terminal callback from a background thread: memory-only publication, no success claim.
        string waitingText = MusketeerLegacyCompensationRuntime.StatusText;
        Check(waitingText.Contains("等待落盘确认"), "status shows pending native confirmation: " + waitingText);
        var thread = new Thread(() => _savedCallback.Invoke(Coatsink.Common.SaveLoadResult.NoChange));
        thread.Start(); thread.Join();
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(MusketeerIdentity.LogCount("legacy-compensation-save-callback:72") == 1, "terminal callback observed once");
        _savedCallback.Invoke(Coatsink.Common.SaveLoadResult.NoChange);
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(MusketeerIdentity.LogCount("legacy-compensation-save-callback:72") == 1, "duplicate callback never re-observes");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("未确认"), "callback terminal without harvest stays visibly unconfirmed");
        Check(GlobalSaveData.SaveAsyncCalls == 1, "no second native save after a terminal callback");
        Check(prepared.CompensationPending, "callback alone never clears the pending state");

        // Acceptance fence: wrong epoch is not acceptance, exact owner receipt is.
        MusketeerNativeSave.AcceptedSet = true;
        MusketeerNativeSave.AcceptedRequest = MusketeerPersistence.PlanArg.RequestId;
        MusketeerNativeSave.AcceptedContext = Context;
        MusketeerNativeSave.AcceptedEpoch = "deadbeef";
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(prepared.CompensationPending && !MusketeerIdentity.Logged("legacy-compensation-accepted"), "wrong epoch is not acceptance");
        MusketeerNativeSave.AcceptedEpoch = MusketeerPersistence.LastEpoch;
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(!prepared.CompensationPending, "accepted harvest clears the pending state");
        Check(MusketeerIdentity.Logged("legacy-compensation-accepted"), "acceptance logged");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("已完成"), "status shows completion");

        // Done attempts are inert: no capture/save/probe/prepare re-issue on later ticks or loads.
        for (int i = 0; i < 5; i++) MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(IslandSaveData.SerializeCalls == 1 && GlobalSaveData.SaveAsyncCalls == 1 && MusketeerPersistence.PrepareCalls == 1, "accepted attempt re-issues nothing");
        Check(CoinCourierGround.ProbeCalls == 8 && Pool.SpawnCalls.Count == 8, "accepted attempt spawns/probes nothing further");
        MusketeerLegacyCompensationRuntime.OnLoaded(EligibleState(), LoadJson);
        Check(MusketeerPersistence.PrepareCalls == 1, "a materialized attempt never transfers to a new scene");
    }

    // ------------------------------------------------------------------ scenario 4: partial batch

    private static void PartialBatchCleanAndRetryPolicy()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerIdentity.RegisterVerdict = index => index != 4;   // fifth birth fails to register
        MusketeerLegacyCompensationRuntime.Tick(scene);

        Check(MusketeerPersistence.PrepareCalls == 1, "prepare ran before the partial batch");
        Check(Pool.SpawnCalls.Count == 5 && MusketeerIdentity.RegisterCalls.Count == 5, "batch stopped at the failing birth");
        Check(Pool.Despawns.Count == 5, "every created birth was pool-despawned");
        Check(MusketeerIdentity.ForgetCalls.Count == 5, "every created birth was unregistered");
        bool allOwned = true;
        foreach (var forget in MusketeerIdentity.ForgetCalls) allOwned &= forget.Owned;
        Check(allOwned, "cleanup only touched exactly owned births");
        Check(MusketeerNativeSave.CancelCalls == 1, "pre-capture cancellation requested after verified removal");
        Check(MusketeerIdentity.Released.Contains(MusketeerPersistence.LastPrepared), "prepared state released");
        Check(ReferenceEquals(MusketeerIdentity.CurrentState, scene), "pre-attempt runtime restored");
        Check(MusketeerIdentity.Logged("legacy-compensation-cancelled"), "cancellation logged");
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "no capture/save after a clean cancel");

        // Same scene generation: no automatic retry (visible fault + old unknown-paid remain).
        MusketeerLegacyCompensationRuntime.Tick(scene);
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerPersistence.PrepareCalls == 1 && Pool.SpawnCalls.Count == 5, "no re-attempt in the same scene");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("本次未发放"), "cancelled attempt shows a visible fault state");

        // New scene generation: a fresh attempt is allowed (nothing was left materialized).
        MusketeerIdentity.RegisterVerdict = _ => true;
        MusketeerIdentity.IslandState next = EligibleState();
        MusketeerIdentity.CurrentState = next;
        MusketeerLegacyCompensationRuntime.OnLoaded(next, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(next);
        Check(MusketeerPersistence.PrepareCalls == 2, "a new scene generation may attempt again");

        // Cancellation refuses to restore when the native CAS refuses.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene2 = EligibleState();
        MusketeerIdentity.CurrentState = scene2;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene2, LoadJson);
        MusketeerIdentity.RegisterVerdict = index => index != 2;
        MusketeerNativeSave.CancelVerdict = false;
        MusketeerLegacyCompensationRuntime.Tick(scene2);
        Check(MusketeerNativeSave.CancelCalls == 1, "cancel attempted");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-cleanup-cancel:stub-cancel"), "failed cancel is a visible fault");
        Check(!MusketeerIdentity.Released.Contains(MusketeerPersistence.LastPrepared), "runtime is not restored without a successful cancel");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-cleanup-cancel:stub-cancel") && !MusketeerIdentity.Logged("legacy-compensation-cancelled"), "no cancellation success claim");
    }

    // ------------------------------------------------------------------ scenario 5: ownership lost

    private static void CleanupRefusedWhenOwnershipLost()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        // The world pointer changes while the batch is still running: the earlier tickets belong to
        // the old world and cleanup can no longer prove ownership of everything this attempt made.
        Pool.SpawnHook = (prefab, position, rotation, parent, activate, index) =>
        {
            var character = SpawnBirth(index);
            if (index == 3) MusketeerIdentity.WorldKeyValue = 888;
            return character;
        };
        MusketeerIdentity.RegisterVerdict = index => index != 4;
        MusketeerLegacyCompensationRuntime.Tick(scene);

        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-cleanup-ownership"), "unprovable ownership is a visible fault");
        Check(Pool.Despawns.Count == 0, "no blind despawn of unprovable roots");
        Check(MusketeerNativeSave.CancelCalls == 0, "no cancellation without a proven clean scene");
        Check(IslandSaveData.SerializeCalls == 0, "no capture in a broken batch");
        MusketeerIdentity.IslandState other = EligibleState();
        MusketeerIdentity.CurrentState = other;
        MusketeerLegacyCompensationRuntime.OnLoaded(other, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(other);
        Check(MusketeerPersistence.PrepareCalls == 1, "materialized fault never starts a second attempt");
    }

    // ------------------------------------------------------------------ scenario 6: spawn throw

    private static void SpawnThrowStaysFailClosedWithOwnershipFences()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        var created = new List<Character>();
        Pool.SpawnHook = (prefab, position, rotation, parent, activate, index) =>
        {
            if (index == 3) throw new InvalidOperationException("stub-native-spawn-throw");
            var character = SpawnBirth(index);
            created.Add(character);
            return character;
        };
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);

        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-spawn-unknown"), "spawn throw is a visible unknown-object fault");
        Check(Pool.Despawns.Count == 0 && MusketeerNativeSave.CancelCalls == 0, "a throw forbids cleanup and cancellation");
        Check(Pool.SpawnCalls.Count == 4, "the batch stopped at the throwing spawn");

        // Ownership fences on the two retained tickets.
        Guid id0 = MusketeerPersistence.GrantsArg[0];
        Guid id1 = MusketeerPersistence.GrantsArg[1];
        Guid request = MusketeerPersistence.PlanArg.RequestId;
        Check(MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], id0, request), "exact ticket is owned");
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(created[1], id0, request), "another wrapper is not the birth");
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], id1, request), "another id is not the birth");
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], id0, Guid.NewGuid()), "another request is not the birth");
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], Guid.Empty, request), "empty id is not the birth");
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(new Character(), id0, request), "foreign actor is never a new birth");
        MusketeerIdentity.WorldKeyValue = 999;
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], id0, request), "world replacement invalidates the ticket");
        MusketeerIdentity.WorldKeyValue = 777;
        Check(MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], id0, request), "restored world keeps the exact ticket");
        created[0].ScriptedLife = 4242;
        Check(!MusketeerLegacyCompensationRuntime.OwnsBirth(created[0], id0, request), "life reuse invalidates the ticket");
        Check(!MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(scene, out _), "no receipt is published for a foreign state");
    }

    // ------------------------------------------------------------------ scenario 7: capture gaps

    private static void CaptureIncompleteIsVisibleAndNotReissued()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);
        var prepared = MusketeerPersistence.LastPrepared;
        IslandSaveData.SerializeHook = nullable => { };   // capture runs, the checkpoint never completes
        MusketeerLegacyCompensationRuntime.Tick(prepared);

        Check(IslandSaveData.SerializeCalls == 1, "capture attempted");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-capture-incomplete"), "incomplete capture is a visible fault");
        Check(GlobalSaveData.SaveAsyncCalls == 0, "no native save without a staged complete receipt");
        Check(MusketeerNativeSave.CancelCalls == 0, "no automatic cancellation after the capture started");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("legacy-capture-incomplete"), "fault is visible in the status text");
        for (int i = 0; i < 5; i++) MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(IslandSaveData.SerializeCalls == 1 && GlobalSaveData.SaveAsyncCalls == 0, "failed capture is never reissued");
        Check(MusketeerPersistence.PrepareCalls == 1, "failed capture never prepares again");

        // A production save that completed the checkpoint first is not duplicated by the leaf.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene2 = EligibleState();
        MusketeerIdentity.CurrentState = scene2;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene2, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene2);
        CompleteViaProductionCallback();   // player/autosave path staged the completion, not the leaf
        var prepared2 = MusketeerPersistence.LastPrepared;
        MusketeerLegacyCompensationRuntime.Tick(prepared2);
        Check(IslandSaveData.SerializeCalls == 0, "preempted capture is not issued on top of a production save");
        Check(GlobalSaveData.SaveAsyncCalls == 0 && MusketeerIdentity.Logged("legacy-compensation-capture-preempted"), "no second writer for a checkpoint another save owns");
    }

    // ------------------------------------------------------------------ scenario 8: wait bound

    private static void PreflightTimeoutIsBounded()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        Managers.Inst.holder = null;   // native holder never becomes ready
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        UnityEngine.Time.unscaledDeltaTime = 1f;
        for (int i = 0; i < 61; i++) MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-preflight-timeout"), "bounded wait faults visibly");
        Check(MusketeerPersistence.PrepareCalls == 0 && Pool.SpawnCalls.Count == 0, "timeout creates nothing");
        Check(CoinCourierGround.ProbeCalls == 0, "timeout probes nothing");
    }

    // ------------------------------------------------------------------ scenario 9: fence queries

    private static void FenceQueries()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);
        var prepared = MusketeerPersistence.LastPrepared;
        var pending = MusketeerPersistence.LastPending;

        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out var frozen) && ReferenceEquals(frozen, pending), "frozen pending receipt returned for the exact attempt");
        Check(!MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(EligibleState(), out _), "foreign state has no receipt");
        Check(!MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(null, out _), "null state has no receipt");
        prepared.CompensationRequest = Guid.NewGuid();
        Check(!MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out _), "request fence rejects a mutated request");
        prepared.CompensationRequest = MusketeerPersistence.PlanArg.RequestId;
        prepared.Epoch = "aa";
        Check(!MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out _), "epoch fence rejects a mutated epoch");
        prepared.Epoch = MusketeerPersistence.LastEpoch;

        // CapturePrepared only adopts the exact attempt's completion (request/context/epoch fence).
        var foreignPlan = new MusketeerLegacyCompensationPlan(1, Guid.NewGuid(), Context, SourceEpoch, SourceSha, 17, 8);
        var foreignIds = new string[pending.GrantIds.Count];
        for (int i = 0; i < foreignIds.Length; i++) foreignIds[i] = "foreign-" + i;
        var foreign = new MusketeerLegacyCompensationReceipt(foreignPlan, pending.MigrationEpoch,
            new List<Guid>(pending.GrantIds), foreignIds, true);
        MusketeerLegacyCompensationRuntime.CapturePrepared(foreign);
        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out var still) && ReferenceEquals(still, pending), "foreign request completion is ignored");
        var foreignEpoch = new MusketeerLegacyCompensationReceipt(pending.Plan, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            new List<Guid>(pending.GrantIds), foreignIds, true);
        MusketeerLegacyCompensationRuntime.CapturePrepared(foreignEpoch);
        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out still) && ReferenceEquals(still, pending), "foreign epoch completion is ignored");
        MusketeerLegacyCompensationRuntime.CapturePrepared(null);
        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out still) && ReferenceEquals(still, pending), "null completion is ignored");
        var mine = Complete(pending, "row-mine");
        MusketeerLegacyCompensationRuntime.CapturePrepared(mine);
        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out var replaced) && ReferenceEquals(replaced, mine), "exact completion replaces the pending receipt");

        // A pending receipt is never a persisted grant: the codec refuses to write one.
        string pendingJson = Serialize(pending, out bool pendingThrew);
        Check(pendingThrew, "pending receipt cannot be serialized");
        string completeJson = Serialize(mine, out bool completeThrew);
        Check(!completeThrew && completeJson.Contains("\"complete\":true"), "complete receipt serializes");
    }

    // ------------------------------------------------------ scenario 10: external autosave path

    // A production save (player/autosave) can stage the complete checkpoint and harvest it while the
    // leaf never issued SaveAsync. The authoritative owner flag must still clear the same prepared
    // state; a staged completion or a callback status alone never clears.
    private static void ExternalHarvestCompletesWithoutLeafSave()
    {
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);
        var prepared = MusketeerPersistence.LastPrepared;
        CompleteViaProductionCallback();   // external save staged the completion, not the leaf
        Check(MusketeerLegacyCompensationRuntime.TryGetPendingReceipt(prepared, out var staged) && staged.Complete, "external completion is visible to the leaf");
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "leaf issued no capture/save");

        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(prepared.CompensationPending, "staged completion alone never clears the pending state");
        Check(MusketeerIdentity.Logged("legacy-compensation-capture-preempted"), "external checkpoint is not duplicated by the leaf");
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "no leaf save for the external checkpoint");

        MusketeerNativeSave.AcceptedSet = true;
        MusketeerNativeSave.AcceptedRequest = MusketeerPersistence.PlanArg.RequestId;
        MusketeerNativeSave.AcceptedContext = Context;
        MusketeerNativeSave.AcceptedEpoch = MusketeerPersistence.LastEpoch;
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(!prepared.CompensationPending, "authoritative harvest clears the prepared pending state without a leaf save");
        Check(MusketeerIdentity.Logged("legacy-compensation-accepted"), "external acceptance logged");
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "no leaf save is issued to piggyback the external harvest");
        Check(Pool.SpawnCalls.Count == 8, "no second birth batch");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("已完成"), "status reports completion");
    }

    // ------------------------------------------------ scenario 11: unknown spawn outcomes

    private static void SpawnNullKeepsBarrierAndBirths()
    {
        // (a) a null spawn return leaves the created-object set unknown: visible fault only.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        Pool.SpawnHook = (prefab, position, rotation, parent, activate, index) => index == 3 ? null : SpawnBirth(index);
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-spawn-unknown-object"), "null spawn is a visible fault");
        Check(Pool.SpawnCalls.Count == 4, "batch stopped at the null spawn");
        Check(Pool.Despawns.Count == 0 && MusketeerNativeSave.CancelCalls == 0, "unknown set forbids cleanup and cancellation");
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "no capture/save after an unknown spawn");
        MusketeerLegacyCompensationRuntime.Tick(scene);
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerPersistence.PrepareCalls == 1 && Pool.SpawnCalls.Count == 4, "unknown spawn never retries in the same scene");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("退出游戏后重新启动"), "materialized fault demands a full restart");

        // (b) a wrapper without a GameObject is equally unknown.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        Pool.SpawnHook = (prefab, position, rotation, parent, activate, index) => index == 2 ? new Character() : SpawnBirth(index);
        MusketeerIdentity.IslandState scene2 = EligibleState();
        MusketeerIdentity.CurrentState = scene2;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene2, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene2);
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-spawn-unknown-object"), "missing GameObject is a visible fault");
        Check(Pool.Despawns.Count == 0 && MusketeerNativeSave.CancelCalls == 0, "missing GameObject forbids cleanup and cancellation");

        // (c) the shared prefab asset itself must never be operated on or cleaned.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        Pool.SpawnHook = (prefab, position, rotation, parent, activate, index) => index == 1 ? _prefab : SpawnBirth(index);
        MusketeerIdentity.IslandState scene3 = EligibleState();
        MusketeerIdentity.CurrentState = scene3;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene3, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene3);
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-spawn-unknown-object"), "asset pointer is a visible fault");
        Check(Pool.Despawns.Count == 0 && MusketeerNativeSave.CancelCalls == 0, "the prefab asset is never despawned or cancelled");
        Check(_prefab.gameObject.activeInHierarchy, "the prefab asset stays untouched");
    }

    // ------------------------------------------------ scenario 12: loaded-owner fences

    private static void OwnerReplacementFences()
    {
        // (i) the origin owner is captured at OnLoaded; a replacement before Prepare creates nothing.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        GlobalSaveData.loaded = new GlobalSaveData();
        MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerPersistence.PrepareCalls == 0 && Pool.SpawnCalls.Count == 0, "owner replacement before prepare creates nothing");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-owner-replaced"), "owner replacement before prepare is a visible fault");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("重载岛屿后可重新尝试"), "non-materialized fault keeps the reload wording");

        // (ii) a replacement after births never captures/saves for the old attempt in the new owner,
        // and the new owner's harvest flag never clears it.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene2 = EligibleState();
        MusketeerIdentity.CurrentState = scene2;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene2, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene2);
        var prepared = MusketeerPersistence.LastPrepared;
        Check(prepared != null && prepared.CompensationPending, "attempt prepared under the origin owner");
        GlobalSaveData.loaded = new GlobalSaveData();
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "new owner never captures or saves the old attempt");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-owner-replaced"), "owner replacement after births is a visible fault");
        MusketeerNativeSave.AcceptedSet = true;
        MusketeerNativeSave.AcceptedRequest = MusketeerPersistence.PlanArg.RequestId;
        MusketeerNativeSave.AcceptedContext = Context;
        MusketeerNativeSave.AcceptedEpoch = MusketeerPersistence.LastEpoch;
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(prepared.CompensationPending, "the new owner's flag never clears the old attempt");
        Check(!MusketeerIdentity.Logged("legacy-compensation-accepted"), "no acceptance is claimed in a replaced owner");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("退出游戏后重新启动"), "materialized owner fault demands a full restart");
    }

    // ------------------------------------------------ scenario 13: owner tuple fences

    // The real NativeSave owner identity is the (Global pointer, prefs pointer, filename) tuple.
    // Swapping one leg while Global/world/CurrentState stay identical must behave exactly like a
    // full owner replacement: nothing is prepared/captured/saved for the old attempt, the new
    // owner's flag never clears it, and the fault demands a full restart.
    private static void OwnerTupleFences()
    {
        // (i) prefs pointer swapped while waiting.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene = EligibleState();
        MusketeerIdentity.CurrentState = scene;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene, LoadJson);
        GlobalSaveData.loaded.prefs = new PrefsSaveData();
        MusketeerLegacyCompensationRuntime.Tick(scene);
        Check(MusketeerPersistence.PrepareCalls == 0 && Pool.SpawnCalls.Count == 0, "prefs swap before prepare creates nothing");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-owner-replaced"), "prefs swap is a visible fault");

        // (ii) filename swapped while waiting.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene2 = EligibleState();
        MusketeerIdentity.CurrentState = scene2;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene2, LoadJson);
        GlobalSaveData.filename = "global-v36";
        MusketeerLegacyCompensationRuntime.Tick(scene2);
        Check(MusketeerPersistence.PrepareCalls == 0 && Pool.SpawnCalls.Count == 0, "filename swap before prepare creates nothing");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-owner-replaced"), "filename swap is a visible fault");

        // (iii) prefs pointer swapped after eight births: no capture/save, new flag cannot clear.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene3 = EligibleState();
        MusketeerIdentity.CurrentState = scene3;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene3, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene3);
        var prepared = MusketeerPersistence.LastPrepared;
        Check(prepared != null && prepared.CompensationPending, "attempt prepared under the origin tuple");
        GlobalSaveData.loaded.prefs = new PrefsSaveData();
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "prefs swap after births never captures or saves");
        Check(MusketeerIdentity.Logged("legacy-compensation-fault:legacy-owner-replaced"), "prefs swap after births is a visible fault");
        MusketeerNativeSave.AcceptedSet = true;
        MusketeerNativeSave.AcceptedRequest = MusketeerPersistence.PlanArg.RequestId;
        MusketeerNativeSave.AcceptedContext = Context;
        MusketeerNativeSave.AcceptedEpoch = MusketeerPersistence.LastEpoch;
        MusketeerLegacyCompensationRuntime.Tick(prepared);
        Check(prepared.CompensationPending, "the new owner tuple never clears the old pending state");
        Check(!MusketeerIdentity.Logged("legacy-compensation-accepted"), "no acceptance is claimed for a swapped owner tuple");

        // (iv) filename swapped after eight births.
        ResetAll();
        SetConfigDir();
        WritePlanValid();
        SetupWorld();
        DefaultSpawn();
        MusketeerIdentity.IslandState scene4 = EligibleState();
        MusketeerIdentity.CurrentState = scene4;
        MusketeerLegacyCompensationRuntime.OnLoaded(scene4, LoadJson);
        MusketeerLegacyCompensationRuntime.Tick(scene4);
        var prepared2 = MusketeerPersistence.LastPrepared;
        GlobalSaveData.filename = "global-v36";
        MusketeerLegacyCompensationRuntime.Tick(prepared2);
        Check(IslandSaveData.SerializeCalls == 0 && GlobalSaveData.SaveAsyncCalls == 0, "filename swap after births never captures or saves");
        Check(MusketeerLegacyCompensationRuntime.StatusText.Contains("退出游戏后重新启动"), "owner tuple fault demands a full restart");
    }

    // ------------------------------------------------------------------ fixtures

    private static List<Character> _births = new();
    private static Character _prefab;
    private static Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> _savedCallback;

    private static MusketeerIdentity.IslandState EligibleState()
        => new()
        {
            ContextKey = Context,
            MatchKind = "unknown-paid",
            Unresolved = true,
            Ready = true,
            ReadOnly = false,
            GenerationPending = false,
            World = 777
        };

    private static void SetupWorld()
    {
        var layerGo = new UnityEngine.GameObject { activeInHierarchy = true };
        var castleGo = new UnityEngine.GameObject { activeInHierarchy = true };
        var castle = new Castle();
        castleGo.Add(castle);
        castleGo.transform.position = new UnityEngine.Vector3(74f, 0f, 0f);
        var kingdom = new Kingdom { castle = castle };
        _prefab = new Character();
        var prefabGo = new UnityEngine.GameObject();
        prefabGo.Add(_prefab);
        prefabGo.Add(new Archer());
        prefabGo.Add(new Persistent { path = ArcherPath });
        var holder = new Holder { archerPrefab = _prefab };
        Managers.Inst = new Managers
        {
            world = new World { gameLayer = layerGo.transform },
            kingdom = kingdom,
            holder = holder
        };
        Pool.Registered[prefabGo.Pointer] = new Pool { prefab = prefabGo };
    }

    private static Character SpawnBirth(int index)
    {
        var go = new UnityEngine.GameObject { activeInHierarchy = true };
        var character = new Character { ScriptedLife = 1000 + index };
        go.Add(character);
        go.Add(new Archer());
        go.Add(new Persistent { path = ArcherPath });
        _births.Add(character);
        return character;
    }

    private static void DefaultSpawn()
    {
        _births = new List<Character>();
        Pool.SpawnHook = (prefab, position, rotation, parent, activate, index) => SpawnBirth(index);
    }

    // Production-callback replay: a completed capture for the exact budget, as
    // Persistence.SaveCapture.Apply would report it after Stage. The leaf never builds this itself.
    private static void CompleteViaProductionCallback()
    {
        var pending = MusketeerPersistence.LastPending;
        MusketeerLegacyCompensationRuntime.CapturePrepared(Complete(pending, "row"));
    }

    private static MusketeerLegacyCompensationReceipt Complete(MusketeerLegacyCompensationReceipt pending, string prefix)
    {
        var captures = new List<MusketeerLegacyGrantCapture>(pending.GrantIds.Count);
        for (int i = 0; i < pending.GrantIds.Count; i++)
            captures.Add(new MusketeerLegacyGrantCapture(pending.GrantIds[i], prefix + "-" + i, 1, ArcherPath, 1));
        MusketeerLegacyCompensationPolicy.TryComplete(pending, captures, out var complete, out _);
        return complete;
    }

    private static string Serialize(MusketeerLegacyCompensationReceipt receipt, out bool threw)
    {
        threw = false;
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
                MusketeerLegacyCompensationPolicy.WriteReceipt(writer, receipt);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (InvalidOperationException) { threw = true; return null; }
    }

    // The leaf reads the plan from BepInEx.Paths.ConfigPath; each scenario gets a private tree.
    private static string _configDir;
    private static string PlanPath => Path.Combine(_configDir, "KingdomEnhancedMod", "manual-musketeer-compensation.v1.json");

    private static void SetConfigDir()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "kem-legacy-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_configDir, "KingdomEnhancedMod"));
        BepInEx.Paths.ConfigPath = _configDir;
    }

    private static void WritePlanValid() => WritePlan();

    private static void WritePlan(string context = Context, int reservations = 17, int units = 8, string requestId = PlanRequest, string padding = "")
        => File.WriteAllText(PlanPath,
            "{\"version\":1,\"requestId\":\"" + requestId + "\",\"context\":\"" + context
            + "\",\"sourceEpoch\":\"" + SourceEpoch + "\",\"sourceArchiveSha\":\"" + SourceSha
            + "\",\"sourceReservations\":" + reservations + ",\"units\":" + units + "}" + padding);

    // ------------------------------------------------------------------ harness reset

    private static void ResetAll()
    {
        _births = new List<Character>();
        _prefab = null;
        _savedCallback = null;
        SetConfigDir();

        Managers.Inst = null;
        Pool.Registered.Clear();
        Pool.SpawnCalls.Clear();
        Pool.Despawns.Clear();
        Pool.SpawnHook = null;
        Pool.DespawnHook = _ => true;
        IslandSaveData.isSavingGame = false;
        IslandSaveData.SerializeCalls = 0;
        IslandSaveData.SerializeHook = null;
        GlobalSaveData.loaded = new GlobalSaveData();
        GlobalSaveData.filename = "global-v35";
        GlobalSaveData.SaveAsyncCalls = 0;
        GlobalSaveData.SaveAsyncHook = null;
        UnityEngine.Time.unscaledDeltaTime = 1f / 60f;

        MusketeerAccess.EnabledFlag = true;
        MusketeerAccess.PlayingFlag = true;
        MusketeerAccess.TrackAllowedFlag = true;
        MusketeerAccess.InWorldHook = root => root != null && root.activeInHierarchy;

        MusketeerIdentity.CurrentState = null;
        MusketeerIdentity.WorldKeyValue = 777;
        MusketeerIdentity.Logs.Clear();
        MusketeerIdentity.RegisterCalls.Clear();
        MusketeerIdentity.RegisterVerdict = _ => true;
        MusketeerIdentity.ForgetCalls.Clear();
        MusketeerIdentity.Released.Clear();
        MusketeerIdentity.Installed.Clear();

        MusketeerPersistence.PrepareCalls = 0;
        MusketeerPersistence.OldArg = null;
        MusketeerPersistence.JsonArg = null;
        MusketeerPersistence.PlanArg = null;
        MusketeerPersistence.GrantsArg = new List<Guid>();
        MusketeerPersistence.LastPrepared = null;
        MusketeerPersistence.LastPending = null;
        MusketeerPersistence.LastEpoch = null;
        MusketeerPersistence.PrepareVerdict = true;

        MusketeerNativeSave.CancelCalls = 0;
        MusketeerNativeSave.CancelVerdict = true;
        MusketeerNativeSave.AcceptedSet = false;
        MusketeerNativeSave.AcceptedRequest = Guid.Empty;
        MusketeerNativeSave.AcceptedContext = MusketeerNativeSave.AcceptedEpoch = null;

        PatchRoles_Castle.EnsurePoolCalls = 0;
        PatchRoles_Castle.LastTag = null;
        CoinCourierGround.ProbeCalls = 0;
        CoinCourierGround.Probes.Clear();
        CoinCourierGround.ProbeY = 0.875f;
        CoinCourierGround.ProbeOk = true;

        SetLeafField("_attempt", null);
        SetLeafField("_planRead", false);
        SetLeafField("_plan", null);
        SetLeafField("_planFault", null);
        SetLeafField("_saveCallbackTerminal", 0);
        SetLeafField("_saveCallbackResult", int.MinValue);
    }

    private static void SetLeafField(string name, object value)
    {
        FieldInfo field = typeof(MusketeerLegacyCompensationRuntime).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        Check(field != null, "leaf field exists: " + name);
        field?.SetValue(null, value);
    }
}
