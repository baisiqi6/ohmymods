using System;
using System.Collections.Generic;
using Coatsink.Common;
using KingdomEnhancedMod;
using UnityEngine;

// Direct-linked regression harness for il2cpp/WallEngineerRules.cs + WallEngineerRuntime.cs +
// PatchWorld_WallEngineers.cs (project Link items). Stubs.cs mirrors the actual 2.4 member shapes;
// no algorithm is re-implemented here.
internal static class Program
{
    private static int _passed, _failed;
    private static float _now = 100f;
    private static readonly List<Wall> FixtureWalls = new();

    private static void Check(bool ok, string name)
    {
        if (ok) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL " + name);
    }

    private static void Eq<T>(T actual, T expected, string name)
        => Check(EqualityComparer<T>.Default.Equals(actual, expected), name + " (got " + actual + ", want " + expected + ")");

    private sealed class WorldFixture
    {
        internal Managers Managers;
        internal Kingdom Kingdom;
        internal World World;
        internal Game Game;
        internal CampaignSaveData Campaign;
        internal Wall Left;
        internal Wall Right;
    }

    private static void ResetAll()
    {
        // Each independent fixture replaces the native scene; previous objects are destroyed.
        foreach (Wall previous in FixtureWalls) previous.gameObject = null;
        FixtureWalls.Clear();
        WallEngineerRuntime.Reset();
        ModConfig.Enabled.Value = true;
        NetworkBigBoss.IsOnline = false;
        NetworkBigBoss.HasWorldAuth = true;
        IslandSaveData.poppingObjectsToScene = false;
        CampaignSaveData.current = null;
        Managers.Inst = null;
        Physics2D.NextResults = null;
        Physics2D.Calls = 0;
        Physics2D.ForceCount = null;
        Scanner.ComputeCornersCalls = 0;
        Scanner.ComputeCornersThrow = false;
        Scanner.UseMovingCorners = false;
        Time.time = _now;
    }

    private static WorldFixture Build(int workers, bool left = true, bool right = true,
        int leftInitial = 100, int leftHp = 70, int rightInitial = 100, int rightHp = 90)
    {
        var fixture = new WorldFixture
        {
            Managers = new Managers(),
            Kingdom = new Kingdom(),
            World = new World(),
            Game = new Game(),
            Campaign = new CampaignSaveData { Pointer = (IntPtr)555 },
        };
        fixture.Managers.kingdom = fixture.Kingdom;
        fixture.Managers.world = fixture.World;
        fixture.Managers.game = fixture.Game;
        Managers.Inst = fixture.Managers;
        fixture.World.gameLayer.gameObject.scene.handle = 7;
        fixture.Game.state = Game.State.Menu;

        if (left) fixture.Left = AttachWall(fixture, MakeWall(-5f, leftInitial, leftHp), Side.Left);
        if (right) fixture.Right = AttachWall(fixture, MakeWall(5f, rightInitial, rightHp), Side.Right);
        for (int i = 0; i < workers; i++) fixture.Kingdom.Workers.Items.Add(Attach(fixture, MakeWorker()));
        return fixture;
    }

    private static Wall AttachWall(WorldFixture fixture, Wall wall, Side side)
    {
        wall.gameObject.Components.Add(wall);
        wall.transform.parent = fixture.World.gameLayer;
        fixture.Kingdom.outerWall[side] = wall.gameObject;
        return wall;
    }

    private static Worker Attach(WorldFixture fixture, Worker worker)
    {
        worker.transform.parent = fixture.World.gameLayer;
        return worker;
    }

    private static Worker MakeWorker(bool dead = false, bool useHitPoints = true, int hp = 1, int layer = 17)
    {
        var worker = new Worker
        {
            transform = new Transform { position = new Vector3(0f, 0f, 0f) },
            behaviour = new Haglet { started = true },
        };
        worker.gameObject.scene = new Scene { handle = 7 };
        worker.gameObject.layer = layer;
        worker.gameObject.Components.Add(worker);
        var damageable = new Damageable { isDead = dead, useHitPoints = useHitPoints, hitPoints = hp };
        worker.gameObject.Components.Add(damageable);
        return worker;
    }

    private static Collider2D MakeCollider(float minX, float maxX, bool dead = false)
    {
        var collider = new Collider2D { bounds = new Bounds(new Vector3(minX, -1f, 0f), new Vector3(maxX, 1f, 0f)) };
        collider.gameObject.scene = new Scene { handle = 7 };
        collider.gameObject.layer = 9;
        if (dead) collider.gameObject.Components.Add(new Damageable { isDead = true });
        return collider;
    }

    private static Wall MakeWall(float x, int initial, int hp, int layer = 3)
    {
        var wall = new Wall
        {
            _damageable = new Damageable { initialHitPoints = initial, hitPoints = hp },
            transform = new Transform
            {
                position = new Vector3(x, 0, 0),
                localScale = new Vector3(x < 0 ? -1f : 1f, 1f, 1f),
            },
            gameObject = new GameObject { scene = new Scene { handle = 7 }, layer = layer },
        };
        FixtureWalls.Add(wall);
        return wall;
    }

    // Real order: Pop scope enter -> exit -> ApplyToScene completion -> first Tick.
    private static void CompleteLoad(WorldFixture fixture, bool scopeSucceeded = true)
    {
        CampaignSaveData.current = fixture.Campaign;
        fixture.Campaign.CurrentIsland = new IslandSaveData { Pointer = (IntPtr)777 };
        WallEngineerRuntime.NotifyLoadScopeEntered();
        WallEngineerRuntime.NotifyLoadScopeExited(scopeSucceeded);
        if (scopeSucceeded) WallEngineerRuntime.NotifyApplyToSceneCompleted(fixture.Campaign);
    }

    private static void Tick(WorldFixture fixture)
    {
        _now += 1f;
        Time.time = _now;
        WallEngineerRuntime.Tick(fixture.Managers, _now);
    }

    private static WallEngineerStatus Apply(WorldFixture fixture)
    {
        CompleteLoad(fixture);
        fixture.Game.state = Game.State.Playing;
        Tick(fixture);
        return WallEngineerRuntime.Status;
    }

    private static Worker RightEngineer(WorldFixture fixture, float x, float epoch)
    {
        Worker worker = Attach(fixture, MakeWorker());
        worker.transform.position.x = x;
        worker._currentWork = fixture.Right;
        worker._enemyScanner.observer = worker.transform;
        worker._enemyScanner._lastRefresh = epoch;
        return worker;
    }

    // Parent-operator regression (intent preserved verbatim): movement must reuse the epoch query
    // while the current inner-side reach stays covered, and lapse otherwise.
    private static void MovementCoverageRegression()
    {
        foreach (bool left in new[] { false, true })
        {
            ResetAll();
            WorldFixture f = Build(10);
            Apply(f);
            Worker w = Attach(f, MakeWorker());
            Wall wall = left ? f.Left : f.Right;
            w._queuedWork = wall;
            w.transform.position.x = left ? -3.5f : 3.5f;
            w.transform.localScale = new Vector3(left ? -1f : 1f, 1f, 1f);
            w._enemyScanner.observer = w.transform;
            w._enemyScanner.range = 4f;
            w._enemyScanner.rangeBehind = 2f;
            w._enemyScanner._height = 2f;
            w._enemyScanner._lastRefresh = 50f;
            Scanner.UseMovingCorners = true;
            Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
            Check(WallEngineerRuntime.MaySuppressFlee(w), "moving coverage initial " + left);
            int queries = Physics2D.Calls;
            w.transform.position.x = left ? -4f : 4f;
            Check(WallEngineerRuntime.MaySuppressFlee(w), "towards wall covered rectangle stays qualified " + left);
            Eq(Physics2D.Calls, queries, "movement reuses native epoch query " + left);
            w.transform.position.x = left ? -0.5f : 0.5f;
            Check(!WallEngineerRuntime.MaySuppressFlee(w), "reverse outside cached rectangle flees " + left);
            Eq(Physics2D.Calls, queries, "reverse does not force physical query " + left);
        }
        ResetAll();
        WorldFixture distant = Build(10);
        Apply(distant);
        Worker far = Attach(distant, MakeWorker());
        far._queuedWork = distant.Right;
        far._enemyScanner.observer = far.transform;
        far._enemyScanner.range = far._enemyScanner.rangeBehind = 1f;
        far._enemyScanner._height = 2f;
        far._enemyScanner._lastRefresh = 10f;
        Scanner.UseMovingCorners = true;
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        Check(!WallEngineerRuntime.MaySuppressFlee(far), "query not crossing wall must preserve native flee");
        far.transform.position.x = 0.25f;
        Check(!WallEngineerRuntime.MaySuppressFlee(far), "non-crossing epoch cannot resurrect exception");
        Scanner.UseMovingCorners = false;
    }

    private static void FinalLifecycleFailureRegression()
    {
        ResetAll();
        WorldFixture inactive = Build(10, left: false, right: true, rightHp: 150);
        Apply(inactive);
        inactive.Right.gameObject.activeInHierarchy = false; // Unity OnDisable observes inactive, not destroyed.
        WallEngineerRuntime.NotifyWallDisabled(inactive.Right);
        Eq(inactive.Right._damageable.initialHitPoints, 100, "inactive OnDisable returns original B");
        Eq(inactive.Right._damageable.hitPoints, 100, "inactive OnDisable clamps only on actual return");

        ResetAll();
        WorldFixture addFault = Build(10, left: false, right: true);
        addFault.Kingdom.AddWorkThrow = true;
        WallEngineerStatus failed = Apply(addFault);
        Check(!failed.Ready, "AddWork failure cannot be overwritten by ready commit");
        int attempts = addFault.Kingdom.AddWorkCalls;
        Tick(addFault);
        addFault.Kingdom.AddWorkThrow = false;
        Tick(addFault);
        Check(WallEngineerRuntime.Status.Ready, "AddWork retry eventually ready");
        Check(addFault.Kingdom.AddWorkCalls > attempts, "AddWork retry actually calls native publication");
    }
    private static void LifecycleResponsibilityRegression()
    {
        foreach (bool resetExit in new[] { false, true })
        {
            ResetAll();
            WorldFixture halfExit = Build(10, left: false);
            halfExit.Kingdom.PostRegisterThrow = true;
            Apply(halfExit);
            Check(halfExit.Kingdom.CheckWork(halfExit.Right), "half publication exists before immediate exit");
            Worker halfWorker = Attach(halfExit, MakeWorker());
            halfExit.Kingdom.Workers.Items.Add(halfWorker);
            halfWorker._currentWork = halfExit.Right;
            if (resetExit) WallEngineerRuntime.Reset();
            else { ModConfig.Enabled.Value = false; Tick(halfExit); }
            Check(!halfExit.Kingdom.CheckWork(halfExit.Right), "immediate " + (resetExit ? "Reset" : "off") + " removes half publication");
            Check(halfWorker.ResetWorkCalls >= 1 && halfWorker._currentWork == null, "immediate exit returns half-published work");
            Eq(halfExit.Right._damageable.initialHitPoints, 100, "immediate exit returns half-publication B");
        }

        ResetAll();
        WorldFixture sameHpReturn = Build(20, left: false, rightHp: 100);
        Apply(sameHpReturn);
        int priorGraphics = sameHpReturn.Right.UpdateGraphicsCalls;
        ModConfig.Enabled.Value = false;
        Tick(sameHpReturn);
        Eq(sameHpReturn.Right._damageable.hitPoints, 100, "capacity return preserves same absolute HP");
        Check(sameHpReturn.Right.UpdateGraphicsCalls > priorGraphics, "capacity-only return refreshes wall graphics");

        ResetAll();
        WorldFixture failedApply = Build(10, left: false);
        Apply(failedApply);
        Check(WallEngineerRuntime.ShouldSkipUnsafeRemoval(failedApply.Right), "qualified unsafe exception before failed apply");
        WallEngineerRuntime.NotifyApplyToSceneFailed();
        Check(!WallEngineerRuntime.ShouldSkipUnsafeRemoval(failedApply.Right), "failed apply immediately disables unsafe exception");
        failedApply.Game.state = Game.State.Menu;
        Tick(failedApply);
        Check(!WallEngineerRuntime.Status.Ready, "same context failed apply menu cannot retain ready");

        ResetAll();
        WorldFixture resetLoss = Build(10, left: false);
        Apply(resetLoss);
        Worker resetWorker = Attach(resetLoss, MakeWorker());
        resetLoss.Kingdom.Workers.Items.Add(resetWorker);
        resetWorker._currentWork = resetLoss.Right;
        NetworkBigBoss.HasWorldAuth = false;
        WallEngineerRuntime.Reset();
        Eq(resetLoss.Right._damageable.initialHitPoints, 200, "lost authority reset does not write initial");
        Eq(resetWorker.ResetWorkCalls, 0, "lost authority reset does not reset job");
        NetworkBigBoss.HasWorldAuth = true;
        ModConfig.Enabled.Value = false;
        Tick(resetLoss); // no new load completion: returning old owner is not a new application.
        Eq(resetLoss.Right._damageable.initialHitPoints, 100, "disabled retry returns B after Reset cleared load proof");
        Check(resetWorker.ResetWorkCalls >= 1 && resetWorker._currentWork == null, "disabled retry after Reset returns original kingdom job");

        // Feature stays on; after Reset (no new load) the retained owner returns but is never
        // re-applied.
        ResetAll();
        WorldFixture onRetry = Build(10, left: false);
        Apply(onRetry);
        Worker onWorker = Attach(onRetry, MakeWorker());
        onRetry.Kingdom.Workers.Items.Add(onWorker);
        onWorker._currentWork = onRetry.Right;
        NetworkBigBoss.HasWorldAuth = false;
        WallEngineerRuntime.Reset();
        NetworkBigBoss.HasWorldAuth = true;
        ModConfig.Enabled.Value = true;
        Tick(onRetry);
        Eq(onRetry.Right._damageable.initialHitPoints, 100, "feature-on retry returns B without a new load");
        Check(onWorker.ResetWorkCalls >= 1 && onWorker._currentWork == null, "feature-on retry returns the job");
        Check(!WallEngineerRuntime.Status.Ready, "feature-on retry never re-applies");

        // A context switch must use the original kingdom and its roster for the retained owner,
        // never the new kingdom.
        ResetAll();
        WorldFixture oldOwner = Build(10, left: false);
        Apply(oldOwner);
        Worker oldWorker = Attach(oldOwner, MakeWorker());
        oldOwner.Kingdom.Workers.Items.Add(oldWorker);
        oldWorker._currentWork = oldOwner.Right;
        NetworkBigBoss.HasWorldAuth = false;
        WallEngineerRuntime.Reset();
        NetworkBigBoss.HasWorldAuth = true;
        var nextKingdom = new Kingdom();
        var nextWorld = new World();
        var nextGame = new Game { state = Game.State.Playing };
        var nextManagers = new Managers { kingdom = nextKingdom, world = nextWorld, game = nextGame };
        nextWorld.gameLayer.gameObject.scene.handle = 99;
        Managers.Inst = nextManagers;
        ModConfig.Enabled.Value = false;
        int newRemoves = nextKingdom.RemoveWorkCalls, newAdds = nextKingdom.AddWorkCalls;
        _now += 1f;
        Time.time = _now;
        WallEngineerRuntime.Tick(nextManagers, _now);
        Eq(oldOwner.Right._damageable.initialHitPoints, 100, "context switch retry returns the old B");
        Check(oldWorker.ResetWorkCalls >= 1 && oldWorker._currentWork == null, "old kingdom roster used for the old responsibility");
        Eq(nextKingdom.RemoveWorkCalls, newRemoves, "new kingdom never used for the old owner");
        Eq(nextKingdom.AddWorkCalls, newAdds, "new kingdom never receives a re-dispatch");

        // Unknown original identity: a changed kingdom pointer keeps the owner (no writes, no
        // fallback to the new kingdom); restoring the pointer finishes B + job return.
        ResetAll();
        WorldFixture unknown = Build(10, left: false);
        Apply(unknown);
        Worker unknownWorker = Attach(unknown, MakeWorker());
        unknown.Kingdom.Workers.Items.Add(unknownWorker);
        unknownWorker._currentWork = unknown.Right;
        NetworkBigBoss.HasWorldAuth = false;
        WallEngineerRuntime.Reset();
        NetworkBigBoss.HasWorldAuth = true;
        var strayKingdom = new Kingdom();
        var strayWorld = new World();
        var strayGame = new Game { state = Game.State.Playing };
        var strayManagers = new Managers { kingdom = strayKingdom, world = strayWorld, game = strayGame };
        strayWorld.gameLayer.gameObject.scene.handle = 99;
        Managers.Inst = strayManagers;
        ModConfig.Enabled.Value = false;
        IntPtr originalPointer = unknown.Kingdom.Pointer;
        unknown.Kingdom.Pointer = (IntPtr)0x7FFF;
        int strayRemoves = strayKingdom.RemoveWorkCalls, strayAdds = strayKingdom.AddWorkCalls;
        _now += 1f;
        Time.time = _now;
        WallEngineerRuntime.Tick(strayManagers, _now);
        Eq(unknown.Right._damageable.initialHitPoints, 200, "unknown identity keeps B");
        Eq(unknownWorker.ResetWorkCalls, 0, "unknown identity writes no job reset");
        Check(unknownWorker._currentWork == unknown.Right, "unknown identity keeps current work");
        Eq(strayKingdom.RemoveWorkCalls, strayRemoves, "unknown identity never falls back to the new kingdom");
        Eq(strayKingdom.AddWorkCalls, strayAdds, "unknown identity never re-dispatches");

        unknown.Kingdom.Pointer = originalPointer;
        _now += 1f;
        Time.time = _now;
        WallEngineerRuntime.Tick(strayManagers, _now);
        _now += 1f; // disabled release retries at the existing fault cadence
        Time.time = _now;
        WallEngineerRuntime.Tick(strayManagers, _now);
        Eq(unknown.Right._damageable.initialHitPoints, 100, "restored identity returns B");
        Check(unknownWorker.ResetWorkCalls >= 1 && unknownWorker._currentWork == null, "restored identity returns the job");
        Eq(strayKingdom.RemoveWorkCalls, strayRemoves, "restored return still uses the original kingdom");

        // Authority loss while a load begins: no native cleanup writes, exception off, B kept,
        // responsibility retried once a new writable pass exists.
        ResetAll();
        WorldFixture loss = Build(10, left: false);
        Apply(loss);
        Worker engineer = Attach(loss, MakeWorker());
        loss.Kingdom.Workers.Items.Add(engineer);
        engineer._currentWork = loss.Right;
        int removes = loss.Kingdom.RemoveWorkCalls;
        int hpBefore = loss.Right._damageable.hitPoints;
        NetworkBigBoss.HasWorldAuth = false;
        WallEngineerRuntime.NotifyLoadScopeEntered();
        Eq(loss.Kingdom.RemoveWorkCalls, removes, "authority loss load enter writes no RemoveWork");
        Eq(engineer.ResetWorkCalls, 0, "authority loss load enter writes no reset");
        Check(engineer._currentWork == loss.Right, "authority loss keeps current work untouched");
        Eq(loss.Right._damageable.initialHitPoints, 200, "authority loss keeps initial");
        Eq(loss.Right._damageable.hitPoints, hpBefore, "authority loss keeps hp");
        Check(!WallEngineerRuntime.Status.Ready, "authority loss load enter not ready");

        NetworkBigBoss.HasWorldAuth = true;
        WallEngineerRuntime.NotifyLoadScopeExited(false);
        CompleteLoad(loss);
        loss.Game.state = Game.State.Playing;
        Tick(loss);
        Check(loss.Kingdom.RemoveWorkCalls > removes, "retained cleanup runs when writable again");
        Check(engineer.ResetWorkCalls >= 1 && engineer._currentWork == null, "retained reset frees the work");

        // Truly destroyed native object: drop the record, never touch values.
        ResetAll();
        WorldFixture gone = Build(10, left: false);
        Apply(gone);
        int goneRemoves = gone.Kingdom.RemoveWorkCalls;
        gone.Right.gameObject = null;   // Unity null: destroyed, not merely inactive
        WallEngineerRuntime.NotifyWallDisabled(gone.Right);
        Eq(gone.Kingdom.RemoveWorkCalls, goneRemoves, "destroyed instance writes no RemoveWork");
        Eq(gone.Right._damageable.initialHitPoints, 200, "destroyed instance stays untouched");

        // AddWork registered then threw: the retry must not register again, only complete dispatch.
        ResetAll();
        WorldFixture half = Build(10, left: false);
        half.Kingdom.PostRegisterThrow = true;
        WallEngineerStatus halfStatus = Apply(half);
        Check(!halfStatus.Ready, "post-register throw cannot be overwritten by ready commit");
        Check(half.Kingdom.CheckWork(half.Right), "job is registered despite the throw");
        int halfAdds = half.Kingdom.AddWorkCalls;
        half.Kingdom.PostRegisterThrow = false;
        Tick(half);   // consumes the fault
        Tick(half);
        Check(WallEngineerRuntime.Status.Ready, "post-register retry becomes ready");
        Eq(half.Kingdom.AddWorkCalls, halfAdds, "retry never re-registers the same job");
        Check(half.Kingdom.ReassignCalls >= 1, "retry completes the scheduling request");
    }

    private static int Main()
    {
        MovementCoverageRegression();
        FinalLifecycleFailureRegression();
        LifecycleResponsibilityRegression();
        RulesMatrix();
        LoadGateBinding();
        SingleSideAndDedupe();
        PhysicalLayersAndTrees();
        ApplyAndPauseFreeze();
        RosterSameCountReplacement();
        DisableAndReplaceReturn();
        NativeRepairReturnRegression();
        SingleSideReplaceAndReset();
        ForeignChangeAndPartialFailure();
        RosterReadAbort();
        FleeExceptionMatrix();
        LocalScaleAndSlots();
        FilterAndLivenessGates();
        BorderRecalculation();

        Console.WriteLine((_failed == 0 ? "ALL PASS " : "FAILURES ") + _passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void RulesMatrix()
    {
        Eq(WallEngineerRules.MultiplierForWorkers(0), 1, "M(0)=1");
        Eq(WallEngineerRules.MultiplierForWorkers(9), 1, "M(9)=1");
        Eq(WallEngineerRules.MultiplierForWorkers(10), 2, "M(10)=2");
        Eq(WallEngineerRules.MultiplierForWorkers(19), 2, "M(19)=2");
        Eq(WallEngineerRules.MultiplierForWorkers(20), 3, "M(20)=3");
        Eq(WallEngineerRules.MultiplierForWorkers(21), 3, "M(21)=3");
        Eq(WallEngineerRules.MultiplierForWorkers(100), 3, "M(100)=3");

        Check(WallEngineerRules.TryAppliedInitial(100, 3, out int applied) && applied == 300, "B*3 checked ok");
        Check(!WallEngineerRules.TryAppliedInitial(int.MaxValue, 3, out _), "overflow fails closed");
        Check(!WallEngineerRules.TryAppliedInitial(100, 0, out _), "bad multiplier fails closed");
        Eq(WallEngineerRules.ClampedHp(70, 300), 70, "raise does not heal");
        Eq(WallEngineerRules.ClampedHp(150, 100), 100, "downgrade clamps");
        Check(WallEngineerRules.IsWartimeTarget(false, true, true), "damaged intact wall is a war target");
        Check(!WallEngineerRules.IsWartimeTarget(false, true, false), "full wall not a war target");

        Check(!WallEngineerRules.BoundsTouchInnerHalfSpace(0f, 5f, 10f, true), "left: outside");
        Check(WallEngineerRules.BoundsTouchInnerHalfSpace(9f, 11f, 10f, true), "left: straddle");
        Check(WallEngineerRules.BoundsTouchInnerHalfSpace(12f, 14f, 10f, true), "left: inside");
        Check(!WallEngineerRules.BoundsTouchInnerHalfSpace(12f, 14f, 10f, false), "right: outside");
        Check(WallEngineerRules.BoundsTouchInnerHalfSpace(9f, 11f, 10f, false), "right: straddle");
        Check(WallEngineerRules.WorkerInboard(true, 12f, 10f, true), "left worker inboard");
        Check(!WallEngineerRules.WorkerInboard(true, 12f, 10f, false), "past wall not inboard");
        Check(WallEngineerRules.NativeTargetInboard(10.5f, 10f, true), "left target inboard");
        Check(!WallEngineerRules.NativeTargetInboard(9.5f, 10f, true), "left target outboard");

        WallEngineerRules.NormalizeRect(5f, 1f, -1f, 3f, out float minX, out float minY, out float maxX, out float maxY);
        Check(minX == -1f && maxX == 5f && minY == 1f && maxY == 3f, "normalize rect");
        Check(WallEngineerRules.Straddles(0f, 10f, 5f), "straddles");
        Check(!WallEngineerRules.Straddles(6f, 10f, 5f), "not straddling");
        Check(WallEngineerRules.CoveredByQuery(2f, 1f, 4f, 2f, 0f, 0f, 5f, 3f, 5f, false), "covered right");
        Check(!WallEngineerRules.CoveredByQuery(-1f, 1f, 4f, 2f, 0f, 0f, 5f, 3f, 5f, false), "not covered right");
        Check(!WallEngineerRules.CoveredByQuery(6f, 1f, 8f, 2f, 0f, 0f, 5f, 3f, 5f, false), "empty inner not covered");
    }

    private static void LoadGateBinding()
    {
        // Real order; a Menu wait keeps the completed event and never clamps; Playing then applies.
        ResetAll();
        WorldFixture fixture = Build(10);
        CompleteLoad(fixture);
        fixture.Game.state = Game.State.Menu;
        Tick(fixture);
        Check(!WallEngineerRuntime.Status.Ready, "menu wait not ready");
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.Loading, "menu wait reason");
        Eq(fixture.Left._damageable.initialHitPoints, 100, "menu wait does not clamp");
        fixture.Game.state = Game.State.Playing;
        Tick(fixture);
        Check(WallEngineerRuntime.Status.Ready, "playing after wait applies");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "playing after wait cap");

        // An Apply during a pop scope cannot commit; a failing pop leaves nothing behind.
        ResetAll();
        fixture = Build(10);
        fixture.Game.state = Game.State.Playing;
        CampaignSaveData.current = fixture.Campaign;
        fixture.Campaign.CurrentIsland = new IslandSaveData { Pointer = (IntPtr)777 };
        WallEngineerRuntime.NotifyLoadScopeEntered();
        WallEngineerRuntime.NotifyApplyToSceneCompleted(fixture.Campaign);
        WallEngineerRuntime.NotifyLoadScopeExited(false);
        Tick(fixture);
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.Loading, "apply in scope stays loading");
        Eq(fixture.Left._damageable.initialHitPoints, 100, "apply in scope never clamps");

        // No context proof at completion: the event is not recorded and no later tick can bind it.
        ResetAll();
        fixture = Build(10);
        fixture.Game.state = Game.State.Playing;
        CampaignSaveData.current = fixture.Campaign;
        fixture.Campaign.CurrentIsland = new IslandSaveData { Pointer = (IntPtr)777 };
        Managers.Inst = null;
        WallEngineerRuntime.NotifyLoadScopeEntered();
        WallEngineerRuntime.NotifyLoadScopeExited(true);
        WallEngineerRuntime.NotifyApplyToSceneCompleted(fixture.Campaign);
        Tick(fixture);
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.Loading, "unproven apply not loaded");
        Managers.Inst = fixture.Managers;
        Tick(fixture);
        Check(!WallEngineerRuntime.Status.Ready, "no blind binding on later ticks");
        WallEngineerRuntime.NotifyApplyToSceneCompleted(fixture.Campaign);   // a new legal apply event
        Tick(fixture);
        Check(WallEngineerRuntime.Status.Ready, "a proven apply event enables");

        // Foreign campaign/island tuple is rejected.
        ResetAll();
        fixture = Build(10);
        fixture.Game.state = Game.State.Playing;
        CompleteLoad(fixture);
        CampaignSaveData.current = new CampaignSaveData { Pointer = (IntPtr)999, CurrentIsland = new IslandSaveData { Pointer = (IntPtr)1 } };
        Tick(fixture);
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.Loading, "foreign tuple not applied");
    }

    private static void SingleSideAndDedupe()
    {
        ResetAll();
        WorldFixture fixture = Build(10, right: false);
        WallEngineerStatus status = Apply(fixture);
        Check(status.Ready, "single side ready");
        Eq(status.EligibleWallCount, 1, "single side count");
        Eq(status.AppliedMultiplier, 2, "single side multiplier");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "single side cap applied");

        ResetAll();
        fixture = Build(10);
        fixture.Kingdom.outerWall[Side.Right] = fixture.Left.gameObject;   // same GameObject both sides
        status = Apply(fixture);
        Check(status.Ready, "same GO dedupe ready");
        Eq(status.EligibleWallCount, 1, "same GO owned once");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "same GO multiplied once");

        // Another wrapper with the same native Pointer on a different GameObject.
        ResetAll();
        fixture = Build(10);
        var alias = new Wall
        {
            Pointer = fixture.Left.Pointer,
            _damageable = fixture.Left._damageable,
            _workableBuilding = fixture.Left._workableBuilding,
            gameObject = new GameObject { scene = new Scene { handle = 7 }, layer = 3 },
            transform = new Transform { position = new Vector3(-5f, 0, 0), localScale = new Vector3(-1f, 1f, 1f) },
        };
        alias.gameObject.Components.Add(alias);
        alias.transform.parent = fixture.World.gameLayer;
        fixture.Kingdom.outerWall[Side.Right] = alias.gameObject;
        status = Apply(fixture);
        Check(status.Ready, "same pointer dedupe ready");
        Eq(status.EligibleWallCount, 1, "same pointer owned once");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "same pointer multiplied once");
    }

    private static void PhysicalLayersAndTrees()
    {
        // Root layer 0, worker 17, wall 3: different physical layers, same transform tree.
        ResetAll();
        WorldFixture fixture = Build(10);
        fixture.World.gameLayer.gameObject.layer = 0;
        WallEngineerStatus status = Apply(fixture);
        Check(status.Ready, "different physics layers in the same tree apply");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "same-tree wall capped");

        // One detached side is skipped; the remaining side is still ready and the detached wall is
        // never touched.
        ResetAll();
        fixture = Build(10);
        fixture.Left.transform.parent = null;
        status = Apply(fixture);
        Check(status.Ready, "detached side skipped, other side ready");
        Eq(status.EligibleWallCount, 1, "detached side not counted");
        Eq(fixture.Left._damageable.initialHitPoints, 100, "detached wall untouched");
        Eq(fixture.Left._damageable.hitPoints, 70, "detached wall hp untouched");
        Eq(fixture.Right._damageable.initialHitPoints, 200, "remaining wall applied");

        // A single wall outside the tree leaves nothing eligible.
        ResetAll();
        fixture = Build(10, right: false);
        fixture.Left.transform.parent = null;
        status = Apply(fixture);
        Check(!status.Ready, "only out-of-tree wall not ready");
        Eq(status.Reason, WallEngineerUnavailableReason.NoOuterWall, "only out-of-tree reason");
        Eq(fixture.Left._damageable.initialHitPoints, 100, "only out-of-tree untouched");
    }

    private static void ApplyAndPauseFreeze()
    {
        ResetAll();
        WorldFixture fixture = Build(21);   // M = 3
        WallEngineerStatus status = Apply(fixture);
        Check(status.Ready, "M3 apply ready");
        Eq(status.AppliedMultiplier, 3, "M3 for 21 workers");
        Eq(fixture.Left._damageable.initialHitPoints, 300, "M3 cap");
        fixture.Left._damageable.hitPoints = 50;
        Tick(fixture);
        Check(fixture.Kingdom.AddWorkCalls >= 1, "damaged wall qualified");
        Check(WallEngineerRuntime.ShouldSkipUnsafeRemoval(fixture.Left), "qualified wall skips unsafe removal");
        fixture.Right._damageable.hitPoints = fixture.Right.GetTotalMaxHitPoints();
        Tick(fixture);
        Check(!WallEngineerRuntime.ShouldSkipUnsafeRemoval(fixture.Right), "full wall keeps native removal");

        fixture.Game.state = Game.State.Menu;
        int adds = fixture.Kingdom.AddWorkCalls, removes = fixture.Kingdom.RemoveWorkCalls;
        int graphics = fixture.Left.UpdateGraphicsCalls;
        Tick(fixture);
        Tick(fixture);
        Check(WallEngineerRuntime.Status.Ready, "pause keeps Ready");
        Eq(WallEngineerRuntime.Status.AppliedMultiplier, 3, "pause keeps M3");
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.None, "pause keeps reason");
        Eq(fixture.Left._damageable.initialHitPoints, 300, "pause keeps initial");
        Eq(fixture.Left._damageable.hitPoints, 50, "pause keeps hp");
        Eq(fixture.Kingdom.AddWorkCalls, adds, "pause does not re-dispatch");
        Eq(fixture.Kingdom.RemoveWorkCalls, removes, "pause does not cancel");
        Eq(fixture.Left.UpdateGraphicsCalls, graphics, "pause does not touch graphics");

        // Authority loss while paused must not keep Ready true; no cleanup happens on freeze.
        NetworkBigBoss.HasWorldAuth = false;
        Tick(fixture);
        Check(!WallEngineerRuntime.Status.Ready, "paused authority loss not ready");
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.NoAuthority, "paused authority reason");
        Eq(fixture.Left._damageable.initialHitPoints, 300, "authority loss keeps B");
        NetworkBigBoss.HasWorldAuth = true;
        Tick(fixture);
        Eq(fixture.Left._damageable.initialHitPoints, 300, "authority restore keeps B");
        fixture.Game.state = Game.State.Playing;
        Tick(fixture);
        Check(WallEngineerRuntime.Status.Ready, "authority restored becomes ready on resume");
        Eq(fixture.Left._damageable.initialHitPoints, 300, "resume keeps M3 cap");
    }

    private static void RosterSameCountReplacement()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Eq(Apply(fixture).AppliedMultiplier, 2, "M2 before replacement");

        fixture.Kingdom.Workers.Items.Clear();
        for (int i = 0; i < 9; i++) fixture.Kingdom.Workers.Items.Add(Attach(fixture, MakeWorker()));
        fixture.Kingdom.Workers.Items.Add(Attach(fixture, MakeWorker(dead: true)));
        WallEngineerRuntime.NotifyRosterChanged();
        Tick(fixture);
        Eq(WallEngineerRuntime.Status.AppliedMultiplier, 1, "same-count replacement recounted");
        Eq(fixture.Left._damageable.initialHitPoints, 100, "downgrade clamped to B");
    }

    private static void NativeRepairReturnRegression()
    {
        ResetAll();
        WorldFixture off = Build(10, left: false, rightHp: 80);
        off.Kingdom.isSafe = true;
        Apply(off);
        ModConfig.Enabled.Value = false;
        Tick(off);
        Eq(off.Right._damageable.initialHitPoints, 100, "safe feature-off returns B");
        Check(off.Kingdom.CheckWork(off.Right), "safe damaged feature-off retains native repair work");

        ResetAll();
        WorldFixture inner = Build(10, left: false, rightHp: 80);
        inner.Kingdom.isSafe = true;
        Apply(inner);
        inner.Kingdom.outerWall[Side.Right] = null;
        Tick(inner);
        Eq(inner.Right._damageable.initialHitPoints, 100, "old outer wall returns B");
        Check(inner.Kingdom.CheckWork(inner.Right), "safe damaged old outer wall retains native repair work");

        foreach (bool safe in new[] { false, true })
        {
            ResetAll();
            WorldFixture noJob = Build(10, left: false, rightHp: safe ? 100 : 80);
            noJob.Kingdom.isSafe = safe;
            Apply(noJob);
            ModConfig.Enabled.Value = false;
            Tick(noJob);
            Check(!noJob.Kingdom.CheckWork(noJob.Right), "ineligible native return does not publish: safe=" + safe);
        }

        ResetAll();
        WorldFixture nativeFirst = Build(10, left: false, rightHp: 80);
        nativeFirst.Kingdom.isSafe = true;
        Apply(nativeFirst);
        nativeFirst.Kingdom.NativeRegisterOnRemove = true;
        int firstAdds = nativeFirst.Kingdom.AddWorkCalls;
        int firstReassigns = nativeFirst.Kingdom.ReassignCalls;
        ModConfig.Enabled.Value = false;
        Tick(nativeFirst);
        Check(nativeFirst.Kingdom.CheckWork(nativeFirst.Right), "native event registered ordinary repair first");
        Eq(nativeFirst.Kingdom.AddWorkCalls, firstAdds, "native event first needs no AddWork");
        Eq(nativeFirst.Kingdom.ReassignCalls, firstReassigns, "native event first needs no ReassignWork");

        ResetAll();
        WorldFixture half = Build(10, left: false, rightHp: 80);
        half.Kingdom.isSafe = true;
        Apply(half);
        half.Kingdom.PostRegisterThrow = true;
        ModConfig.Enabled.Value = false;
        Tick(half);
        Check(half.Kingdom.CheckWork(half.Right), "half native return registered member");
        int halfAdds = half.Kingdom.AddWorkCalls;
        int halfRemoves = half.Kingdom.RemoveWorkCalls;
        int halfReassigns = half.Kingdom.ReassignCalls;
        half.Kingdom.PostRegisterThrow = false;
        Tick(half);
        Eq(half.Kingdom.AddWorkCalls, halfAdds, "half return obeys fault cadence before retry");
        Eq(half.Kingdom.ReassignCalls, halfReassigns, "half return does not retry each frame");
        Tick(half);
        Eq(half.Kingdom.AddWorkCalls, halfAdds, "half return never registers twice");
        Eq(half.Kingdom.RemoveWorkCalls, halfRemoves, "half return retry never removes native member");
        Eq(half.Kingdom.ReassignCalls, halfReassigns + 1, "half return retry confirms native scheduling");

        ResetAll();
        WorldFixture innerHalf = Build(10, left: false, rightHp: 80);
        innerHalf.Kingdom.isSafe = true;
        Apply(innerHalf);
        innerHalf.Kingdom.PostRegisterThrow = true;
        int innerReassigns = innerHalf.Kingdom.ReassignCalls;
        innerHalf.Kingdom.outerWall[Side.Right] = null;
        Tick(innerHalf);
        Check(innerHalf.Kingdom.CheckWork(innerHalf.Right), "inner return half publication exists");
        Eq(innerHalf.Kingdom.ReassignCalls, innerReassigns, "inner return does not retry within same maintenance");
        int innerRemoves = innerHalf.Kingdom.RemoveWorkCalls;
        int innerAdds = innerHalf.Kingdom.AddWorkCalls;
        innerHalf.Kingdom.PostRegisterThrow = false;
        float innerFaultAt = _now;
        _now = innerFaultAt + 0.1f;
        Time.time = _now;
        WallEngineerRuntime.Tick(innerHalf.Managers, _now); // consume the fault
        Eq(innerHalf.Kingdom.ReassignCalls, innerReassigns, "inner return waits for fault cadence");
        _now = innerFaultAt + 0.5f; // maintenance is due, but the two-second retry is not
        Time.time = _now;
        WallEngineerRuntime.Tick(innerHalf.Managers, _now);
        Eq(innerHalf.Kingdom.ReassignCalls, innerReassigns, "inner return maintenance cannot bypass fault cadence");
        Eq(innerHalf.Kingdom.RemoveWorkCalls, innerRemoves, "inner return maintenance keeps half member");
        Eq(innerHalf.Kingdom.AddWorkCalls, innerAdds, "inner return maintenance does not register again");
        _now = innerFaultAt + WallEngineerRules.FaultRetrySeconds;
        Time.time = _now;
        WallEngineerRuntime.Tick(innerHalf.Managers, _now);
        Eq(innerHalf.Kingdom.ReassignCalls, innerReassigns + 1, "inner return retry confirms scheduling");
        Eq(innerHalf.Kingdom.RemoveWorkCalls, innerRemoves, "inner return retry keeps the member");
        Eq(innerHalf.Kingdom.AddWorkCalls, innerAdds, "inner return retry does not register twice");

        ResetAll();
        WorldFixture halfExit = Build(10, left: false, rightHp: 80);
        halfExit.Kingdom.isSafe = true;
        Apply(halfExit);
        halfExit.Kingdom.PostRegisterThrow = true;
        ModConfig.Enabled.Value = false;
        Tick(halfExit);
        int exitAdds = halfExit.Kingdom.AddWorkCalls;
        WallEngineerRuntime.NotifyKingdomDisabled();
        Check(!halfExit.Kingdom.CheckWork(halfExit.Right), "world exit cancels half native handoff");
        Eq(halfExit.Kingdom.AddWorkCalls, exitAdds, "world exit never republishes ordinary repair");

        ResetAll();
        WorldFixture inactive = Build(10, left: false, rightHp: 80);
        inactive.Kingdom.isSafe = true;
        Apply(inactive);
        int inactiveAdds = inactive.Kingdom.AddWorkCalls;
        inactive.Right.gameObject.activeInHierarchy = false;
        WallEngineerRuntime.NotifyWallDisabled(inactive.Right);
        Eq(inactive.Kingdom.AddWorkCalls, inactiveAdds, "inactive OnDisable never publishes ordinary repair");

        ResetAll();
        WorldFixture replaced = Build(10, left: false, rightHp: 80);
        replaced.Kingdom.isSafe = true;
        Apply(replaced);
        int replaceAdds = replaced.Kingdom.AddWorkCalls;
        WallEngineerRuntime.NotifyWallReplacing(replaced.Right);
        Eq(replaced.Kingdom.AddWorkCalls, replaceAdds, "replace prefix never publishes ordinary repair");

        ResetAll();
        WorldFixture lost = Build(10, left: false, rightHp: 80);
        lost.Kingdom.isSafe = true;
        Apply(lost);
        int lostAdds = lost.Kingdom.AddWorkCalls;
        NetworkBigBoss.HasWorldAuth = false;
        ModConfig.Enabled.Value = false;
        Tick(lost);
        Eq(lost.Kingdom.AddWorkCalls, lostAdds, "authority loss never publishes ordinary repair");

        ResetAll();
        WorldFixture failedLoad = Build(10, left: false, rightHp: 80);
        failedLoad.Kingdom.isSafe = true;
        Apply(failedLoad);
        int failedLoadAdds = failedLoad.Kingdom.AddWorkCalls;
        WallEngineerRuntime.NotifyApplyToSceneFailed();
        ModConfig.Enabled.Value = false;
        Tick(failedLoad);
        Eq(failedLoad.Kingdom.AddWorkCalls, failedLoadAdds, "failed load never publishes ordinary repair");
    }

    private static void DisableAndReplaceReturn()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Apply(fixture);
        ModConfig.Enabled.Value = false;
        Tick(fixture);
        Eq(fixture.Left._damageable.initialHitPoints, 100, "disable returns B");
        Eq(fixture.Right._damageable.initialHitPoints, 100, "disable returns both sides");
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.Disabled, "disable reason");
        Check(fixture.Kingdom.RemoveWorkCalls >= 1, "disable freed jobs");

        ResetAll();
        fixture = Build(10);
        Apply(fixture);
        WallEngineerRuntime.NotifyWallReplacing(fixture.Left);
        Eq(fixture.Left._damageable.initialHitPoints, 100, "replace returns the replaced wall B");
        Tick(fixture);
        Eq(fixture.Left._damageable.initialHitPoints, 200, "replace re-applies from the new set");
    }

    private static void SingleSideReplaceAndReset()
    {
        ResetAll();
        WorldFixture fixture = Build(21);   // M3, caps 300
        Apply(fixture);
        fixture.Right._damageable.hitPoints = 250;
        Tick(fixture);
        int rightAdds = fixture.Kingdom.AddWorkCalls;

        WallEngineerRuntime.NotifyWallReplacing(fixture.Left);
        Eq(fixture.Left._damageable.initialHitPoints, 100, "replaced left returns B");
        Eq(fixture.Right._damageable.initialHitPoints, 300, "right cap untouched by left replace");
        Eq(fixture.Right._damageable.hitPoints, 250, "right hp untouched by left replace");
        Tick(fixture);
        Eq(fixture.Right._damageable.initialHitPoints, 300, "right cap still untouched after tick");
        Eq(fixture.Right._damageable.hitPoints, 250, "right hp still untouched after tick");
        Eq(fixture.Left._damageable.initialHitPoints, 300, "left re-applied at M3");
        Check(fixture.Kingdom.AddWorkCalls >= rightAdds, "right work pokes not regressed");

        Worker engineer = Attach(fixture, MakeWorker());
        fixture.Kingdom.Workers.Items.Add(engineer);
        engineer._currentWork = fixture.Right;
        int removes = fixture.Kingdom.RemoveWorkCalls;
        WallEngineerRuntime.Reset();
        Eq(fixture.Right._damageable.initialHitPoints, 100, "reset returns right B");
        Eq(fixture.Right._damageable.hitPoints, 100, "reset clamps right hp to B");
        Check(fixture.Kingdom.RemoveWorkCalls > removes, "reset freed jobs");
        Check(engineer.ResetWorkCalls >= 1 && engineer._currentWork == null, "reset frees current work");
        Check(!WallEngineerRuntime.Status.Ready, "reset not ready");
    }

    private static void ForeignChangeAndPartialFailure()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Apply(fixture);
        fixture.Left._damageable.initialHitPoints = 999;
        Tick(fixture);
        Eq(fixture.Left._damageable.initialHitPoints, 999, "foreign initial never overwritten");
        Check(!WallEngineerRuntime.Status.Ready, "foreign change reported");
        Tick(fixture);
        Eq(fixture.Left._damageable.initialHitPoints, 999, "foreign change stays untouched");
        Eq(fixture.Right._damageable.initialHitPoints, 200, "other side keeps its cap");

        ResetAll();
        fixture = Build(10);
        fixture.Right._damageable = null;
        WallEngineerStatus status = Apply(fixture);
        Check(!status.Ready, "partial failure not ready");
        Eq(status.AppliedMultiplier, 0, "partial failure publishes no multiplier");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "successful side applied");
    }

    private static void RosterReadAbort()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Apply(fixture);
        for (int i = 0; i < 11; i++) fixture.Kingdom.Workers.Items.Add(Attach(fixture, MakeWorker()));
        fixture.Kingdom.Workers = null;
        WallEngineerRuntime.NotifyRosterChanged();
        Tick(fixture);
        Check(!WallEngineerRuntime.Status.Ready, "roster read failure not ready");
        Eq(WallEngineerRuntime.Status.Reason, WallEngineerUnavailableReason.NativeEvidenceUnavailable, "roster failure reason");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "roster failure never clamps");
    }

    private static void FleeExceptionMatrix()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Apply(fixture);
        Worker worker = RightEngineer(fixture, 4.5f, 20f);
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        Check(WallEngineerRuntime.MaySuppressFlee(worker), "clear inner space grants");
        Eq(Physics2D.Calls, 1, "one physical query for the epoch");
        Check(WallEngineerRuntime.MaySuppressFlee(worker), "same epoch reuses cache");
        Eq(Physics2D.Calls, 1, "no physical re-query");

        worker._enemyScanner._lastRefresh = 21f;
        Check(WallEngineerRuntime.MaySuppressFlee(worker), "outside enemy grants");

        Physics2D.NextResults.Add(MakeCollider(4f, 6f));
        worker._enemyScanner._lastRefresh = 22f;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "straddle keeps native flee");

        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        Physics2D.NextResults.Add(MakeCollider(0f, 3f));
        worker._enemyScanner._lastRefresh = 23f;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "fully inside keeps native flee");

        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        worker._enemyScanner._lastRefresh = 24f;
        ((Haglet)worker.behaviour).latestGoto = 8;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "goto 8 keeps native flee");
        ((Haglet)worker.behaviour).latestGoto = 0;

        // A non-null current work that is not the owned wall must never suppress, even queued owner.
        worker._currentWork = new Workable();
        worker._queuedWork = fixture.Right;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "unrelated current work keeps native flee");
        worker._currentWork = null;
        worker._queuedWork = null;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "no owned work keeps native flee");

        worker._currentWork = fixture.Right;
        worker.transform.position.x = 6f;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "past the wall keeps native flee");
        worker.transform.position.x = 4.5f;
        worker._enemyScanner._lastRefresh = 25f;
        Scanner.ComputeCornersThrow = true;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "query failure keeps native flee");
        Scanner.ComputeCornersThrow = false;

        // Wall destroyed / no longer the outer wall: immediate refusal.
        fixture.Kingdom.outerWall[Side.Right] = null;
        worker._enemyScanner._lastRefresh = 26f;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "destroyed outer wall keeps native flee");
        fixture.Kingdom.outerWall[Side.Right] = fixture.Right.gameObject;

        // Cached straddling enemy that is explicitly dead is excluded; the exception returns.
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        Collider2D dead = MakeCollider(4f, 6f, dead: true);
        Physics2D.NextResults.Add(dead);
        worker._enemyScanner._lastRefresh = 27f;
        Check(WallEngineerRuntime.MaySuppressFlee(worker), "dead cached collider excluded");
    }

    private static void LocalScaleAndSlots()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Apply(fixture);

        fixture.Right.transform.localScale = new Vector3(2f, 1f, 1f);
        Worker scaled = RightEngineer(fixture, 4.5f, 40f);
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        Check(WallEngineerRuntime.MaySuppressFlee(scaled), "localScale 2 keeps the target inboard");
        fixture.Right.transform.localScale = new Vector3(-1f, 1f, 1f);
        Check(!WallEngineerRuntime.MaySuppressFlee(scaled), "flipped scale keeps native flee");
        fixture.Right.transform.localScale = new Vector3(1f, 1f, 1f);

        // Two engineers per wall, both sides: four independent grants, four queries.
        ResetAll();
        fixture = Build(10);
        Apply(fixture);
        Worker l1 = Attach(fixture, MakeWorker()); l1._currentWork = fixture.Left;
        Worker l2 = Attach(fixture, MakeWorker()); l2._currentWork = fixture.Left;
        Worker r1 = RightEngineer(fixture, 4.5f, 41f);
        Worker r2 = RightEngineer(fixture, 4.5f, 42f);
        foreach (Worker w in new[] { l1, l2 })
        {
            w.transform.position.x = -4.5f;
            w._enemyScanner.observer = w.transform;
            w._enemyScanner._lastRefresh = 41f + (w == l1 ? 0f : 1f);
        }
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        int before = Physics2D.Calls;
        Check(WallEngineerRuntime.MaySuppressFlee(l1), "left engineer 1 grants");
        Check(WallEngineerRuntime.MaySuppressFlee(l2), "left engineer 2 grants");
        Check(WallEngineerRuntime.MaySuppressFlee(r1), "right engineer 1 grants");
        Check(WallEngineerRuntime.MaySuppressFlee(r2), "right engineer 2 grants");
        Eq(Physics2D.Calls, before + 4, "four independent per-worker caches");
        int cached = Physics2D.Calls;
        Check(WallEngineerRuntime.MaySuppressFlee(l1) && WallEngineerRuntime.MaySuppressFlee(r2), "caches reused");
        Eq(Physics2D.Calls, cached, "no extra physical queries within the epoch");

        // One worker assigned to one wall must not occupy the other wall's slot: switching work to
        // the other wall creates a fresh cache (one more query).
        ResetAll();
        fixture = Build(10);
        Apply(fixture);
        Worker dual = Attach(fixture, MakeWorker());
        dual.transform.position.x = -4.5f;
        dual._enemyScanner.observer = dual.transform;
        dual._enemyScanner._lastRefresh = 43f;
        dual._currentWork = fixture.Left;
        dual._queuedWork = fixture.Right;
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();
        int dualBefore = Physics2D.Calls;
        Check(WallEngineerRuntime.MaySuppressFlee(dual), "owned current left takes priority");
        Eq(Physics2D.Calls, dualBefore + 1, "left slot used once");
        dual.transform.position.x = 4.5f;
        dual._currentWork = fixture.Right;
        dual._queuedWork = null;
        Check(WallEngineerRuntime.MaySuppressFlee(dual), "right work grants separately");
        Eq(Physics2D.Calls, dualBefore + 2, "right slot was not occupied before");
    }

    private static void FilterAndLivenessGates()
    {
        ResetAll();
        WorldFixture fixture = Build(10);
        Apply(fixture);
        Worker worker = RightEngineer(fixture, 4.5f, 60f);
        Physics2D.NextResults = new Il2CppSystem.Collections.Generic.List<Collider2D>();

        // Filter field changes within the epoch lapse; a new epoch queries again.
        Check(WallEngineerRuntime.MaySuppressFlee(worker), "filter baseline grants");
        int calls = Physics2D.Calls;
        worker._enemyScanner._contactFilter.useLayerMask = true;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "filter change lapses the epoch");
        Eq(Physics2D.Calls, calls, "filter change does not re-query in the epoch");
        worker._enemyScanner._lastRefresh = 61f;
        Check(WallEngineerRuntime.MaySuppressFlee(worker), "new epoch after filter change queries");

        // NaN depth is invalid geometry: keep native flee.
        worker._enemyScanner._contactFilter.useDepth = true;
        worker._enemyScanner._contactFilter.minDepth = float.NaN;
        worker._enemyScanner._lastRefresh = 62f;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "NaN depth keeps native flee");
        worker._enemyScanner._contactFilter.minDepth = 0f;
        worker._enemyScanner._contactFilter.useDepth = false;

        // Inconsistent query count: keep native flee.
        worker._enemyScanner._lastRefresh = 63f;
        Physics2D.ForceCount = 5;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "count mismatch keeps native flee");
        Physics2D.ForceCount = null;

        // Non-finite scanner parameters: keep native flee.
        worker._enemyScanner._lastRefresh = 64f;
        worker._enemyScanner.range = float.PositiveInfinity;
        Check(!WallEngineerRuntime.MaySuppressFlee(worker), "infinite range keeps native flee");
        worker._enemyScanner.range = 10f;
    }

    private static void BorderRecalculation()
    {
        // Crossing the native intact threshold on the first apply recalculates borders once.
        ResetAll();
        WorldFixture fixture = Build(10, leftHp: 50, rightHp: 90);
        fixture.Left.MinimumIntactRatio = 0.3f;   // intact at 50/100, broken at 50/200
        WallEngineerStatus status = Apply(fixture);
        Check(status.Ready, "border crossing still ready when recalculation succeeds");
        Eq(fixture.Kingdom.BorderCalls, 1, "border recalculated once for the crossing");
        Eq(fixture.Right._damageable.initialHitPoints, 200, "right unaffected by left crossing");

        // A throwing recalculation blocks the ready commit and is retried afterwards.
        ResetAll();
        fixture = Build(10, leftHp: 50, rightHp: 90);
        fixture.Left.MinimumIntactRatio = 0.3f;
        fixture.Kingdom.BorderThrow = true;
        status = Apply(fixture);
        Check(!status.Ready, "border failure blocks ready");
        Check(fixture.Kingdom.BorderCalls >= 1, "border failure attempted");
        Tick(fixture);   // consumes the fault
        fixture.Kingdom.BorderThrow = false;
        Tick(fixture);
        Check(WallEngineerRuntime.Status.Ready, "border retry succeeds");
        Check(fixture.Kingdom.BorderCalls >= 2, "border retry actually called native recalc again");
        Eq(fixture.Left._damageable.initialHitPoints, 200, "retry keeps the applied cap");
    }
}
