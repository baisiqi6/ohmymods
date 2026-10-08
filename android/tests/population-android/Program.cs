// Issue #184 营地补员批 typed host：把真实生产源
// （il2cpp/PatchRoles_BeggarCamp.cs、il2cpp/PatchPerformance_Population.cs、
// il2cpp/PopulationGrounding.cs）与真实 android/MobilePlayerConfig.cs 编进本场景工程，
// 由 Stubs.cs 的 GameAPI doubles 提供实际签名（injection / getComponent / clock / 名册集合）。
// 覆盖契约要求的矩阵：OFF 零 interop 读取、ON 的 3s/网络/暂停门、晚营地与 epoch、容量减增不删旧人、
// 间隔重计时无 burst、多次 reconcile 无重复写、设置变化单次真实写、OFF 一次交还、外写偏离不夺权、
// 局部写失败/Spawn 异常/差量 0 或 2 全部 stop-scene 且无自动重试。oracle 只用名册差量与
// double 的 setter 计数，不复制任何 ownership 策略。
using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using Il2Cpp;

internal static class Program
{
    private static int Main()
    {
        ModConfig.Initialize(static _ => { });
        OffGateChecks();
        EnableAndSpawnChecks();
        LateCampAndEpochChecks();
        CapacityAndIntervalChecks();
        DriftStopChecks();
        PartialWriteChecks();
        SpawnFaultChecks();
        DeltaUncertaintyChecks();
        OffHandsBackOnceChecks();
        SceneChangeChecks();
        CtxGateChecks();
        FaultBlocksAwakeAndCaptureChecks();
        CrossContextHandbackChecks();
        OffCallbackGateChecks();
        IsolationAndRoundChecks();
        WorldLayerGateChecks();
        RestartAfterFaultChecks();
        GroundingApiAdaptationChecks();
        Console.WriteLine("population-android tests: " + Checks.Passed + " passed / " + Checks.Failed + " failed");
        return Checks.Failed == 0 ? 0 : 1;
    }

    private static void OffGateChecks()
    {
        SetPopulation(false);
        SetSteps(4, 120);
        Stubs.NewScene(41, out BeggarCamp camp, out _, out CampaignSaveData campaign);
        Beggar beggar = Stubs.NewBeggar(camp);
        Stubs.ResetTouches();

        BeggarCamp_Awake_Patch.Awake_Prefix(camp);
        BeggarCamp_Awake_Patch.Awake_Postfix(camp);
        BeggarCamp_Awake_Patch.OnDestroy_Prefix(camp);
        Beggar_PopulationLifecycle_Patch.OnEnable_Prefix(beggar);
        Beggar_PopulationLifecycle_Patch.OnDisable_Prefix(beggar);
        PopulationPerformanceApplyPatch.Postfix(campaign);
        PopulationGrounding.Observe(beggar, camp, 1, true);
        Checks.Check(Stubs.TouchCount == 0,
            "OFF with no owned responsibility reads zero interop members across all real entries");
        Checks.Check(camp.MaxSetCalls == 0 && camp.IntervalSetCalls == 0
            && camp.MaxNative == 4 && camp.IntervalNative == 125f,
            "OFF leaves the native camp parameters untouched (no claim, no rewrite, no fallback)");

        // throwing interop：任何 native 访问都会抛异常；OFF 入口必须全部安静返回且零触达。
        Stubs.Throwing = true;
        try
        {
            BeggarCamp_Awake_Patch.Awake_Prefix(camp);
            BeggarCamp_Awake_Patch.Awake_Postfix(camp);
            BeggarCamp_Awake_Patch.OnDestroy_Prefix(camp);
            Beggar_PopulationLifecycle_Patch.OnEnable_Prefix(beggar);
            Beggar_PopulationLifecycle_Patch.OnDisable_Prefix(beggar);
            PopulationPerformanceApplyPatch.Postfix(campaign);
            PopulationGrounding.Observe(beggar, camp, 1, true);
        }
        catch (Exception error)
        {
            Checks.Check(false, "an OFF entry escaped a throwing interop: " + error.GetType().Name);
        }
        Stubs.Throwing = false;
        Checks.Check(Stubs.TouchCount == 0, "OFF survives a throwing interop with zero native reads");
    }

    private static void EnableAndSpawnChecks()
    {
        Fixture scene = Fixture.Create(52);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Checks.Check(Host.Phase == "Waiting", "the real enable action enters the shared BeginScene wait");

        Host.Tick(0.6f);
        Checks.Check(scene.Camp.MaxSetCalls == 1 && scene.Camp.MaxNative == 0,
            "first takeover writes maxBeggars=0 exactly once and records the write credential");
        Checks.Check(scene.Camp.IntervalSetCalls == 1 && scene.Camp.IntervalNative == 115f,
            "first takeover writes the fallback interval (120-5) exactly once");
        Checks.Check(scene.Roster() == 0 && scene.Camp.SpawnCalls == 0,
            "no native replenish before the 3s stable window and the network gate");

        // 把间隔真实改成 1s：deadline 会落在 3s 稳定窗口之内，用来单独证明稳定门。
        ModConfig.BeggarSpawnIntervalSeconds.Value = 1;
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.IntervalSetCalls == 2 && scene.Camp.IntervalNative == 1f,
            "the interval change rewrites the owned interval once (1s fallback) and re-times the deadline");
        Host.Tick(1.5f);
        Checks.Check(scene.Camp.SpawnCalls == 0, "the passed 1s deadline does not bypass the 3s stable gate");
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.SpawnCalls == 1 && scene.Roster() == 1,
            "after the 3s gate the coordinator calls the native SpawnBeggar and the roster grows by one");

        ModConfig.BeggarSpawnIntervalSeconds.Value = 120;
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.IntervalSetCalls == 3 && scene.Camp.IntervalNative == 115f,
            "the interval change back to 120s rewrites the owned interval once");
        Checks.Check(scene.Camp.SpawnCalls == 1 && scene.Roster() == 1,
            "the interval change resets the deadline without a catch-up burst");

        for (int i = 0; i < 6; i++) Host.Tick(0.5f);
        Checks.Check(scene.Camp.MaxSetCalls == 1 && scene.Camp.IntervalSetCalls == 3,
            "six further reconciles rewrite neither owned field (stable field, zero repeat)");
        Checks.Check(scene.Roster() == 1, "no extra spawn while the next deadline is pending");

        // 联机门：在线无已同步 parentHeader 的营地不参与中央补员；客机未跟上时不补员。
        NetworkBigBoss.IsOnline = true;
        NetworkBigBoss.IsClientPresent = true;
        NetworkBigBoss.HasClientCaughtUp = true;
        Host.Tick(122f);
        Checks.Check(scene.Camp.SpawnCalls == 1,
            "an online camp without a synced parent header never receives a central spawn");
        scene.Camp.parentHeaderRef = new Il2Cpp.ParentHeader();
        NetworkBigBoss.HasClientCaughtUp = false;
        Host.Tick(121f);
        Checks.Check(scene.Camp.SpawnCalls == 1, "a not-yet-caught-up client blocks the replenish");
        NetworkBigBoss.HasClientCaughtUp = true;
        Host.Tick(1.0f);
        Checks.Check(scene.Camp.SpawnCalls == 2, "catching up releases the net of the network gate");
        NetworkBigBoss.IsOnline = false;
        NetworkBigBoss.IsClientPresent = false;

        UnityEngine.Time.timeScale = 0f;
        Host.Tick(121f);
        Checks.Check(scene.Camp.SpawnCalls == 2, "a zero time scale (pause/menu) blocks the replenish");
        UnityEngine.Time.timeScale = 1f;
        Host.Tick(1.0f);
        Checks.Check(scene.Camp.SpawnCalls == 3, "unpausing resumes the pending deadline");
        Checks.Check(scene.Roster() == 3, "the roster stays the replenish oracle (roster +1 per spawn)");
        StopScene();
    }

    private static void LateCampAndEpochChecks()
    {
        Fixture scene = Fixture.Create(61);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(3.5f);

        BeggarCamp late = scene.AddCamp();
        BeggarCamp_Awake_Patch.Awake_Prefix(late);
        BeggarCamp_Awake_Patch.Awake_Postfix(late);
        Host.Tick(0.2f);
        Checks.Check(late.MaxSetCalls == 1 && late.IntervalSetCalls == 1 && late.MaxNative == 0,
            "a camp that awakes late is claimed once through the original Awake events");
        Host.Tick(0.6f);
        Checks.Check(late.MaxSetCalls == 1 && late.IntervalSetCalls == 1,
            "the already-owned late camp is never rewritten by the ownership cadence");

        Beggar beggar = scene.AddBeggar(scene.Camp);
        Beggar_PopulationLifecycle_Patch.OnDisable_Prefix(beggar);
        Beggar_PopulationLifecycle_Patch.OnEnable_Prefix(beggar);
        Host.Tick(0.6f);
        Checks.Check(scene.Roster() == 1, "the re-enable epoch refresh never deletes or duplicates an NPC");
        Checks.Check(scene.Camp.MaxSetCalls == 1 && scene.Camp.IntervalSetCalls == 1,
            "the epoch refresh writes no camp parameter");
        StopScene();
    }

    private static void CapacityAndIntervalChecks()
    {
        Fixture scene = Fixture.Create(70);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        scene.AddBeggar(scene.Camp);
        scene.AddBeggar(scene.Camp);
        Host.Tick(0.6f);
        Checks.Check(scene.Roster() == 2, "fixture owns two loaded beggars before the capacity changes");

        ModConfig.BeggarCampCapacity.Value = 1;
        Host.Tick(0.6f);
        Checks.Check(scene.Roster() == 2, "lowering the capacity never despawns loaded beggars");
        ModConfig.BeggarCampCapacity.Value = 20;
        Host.Tick(0.6f);
        Checks.Check(scene.Roster() == 2 && scene.Camp.SpawnCalls == 0,
            "raising the capacity starts no immediate burst");
        Checks.Check(scene.Camp.MaxSetCalls == 1 && scene.Camp.IntervalSetCalls == 1,
            "capacity changes never rewrite the owned camp parameters");

        Host.Tick(119.0f);
        Checks.Check(scene.Camp.SpawnCalls == 0, "the 120s deadline still gates the first replenish");
        Host.Tick(1.5f);
        Checks.Check(scene.Camp.SpawnCalls == 1 && scene.Roster() == 3,
            "the pending deadline produces exactly one native spawn");

        ModConfig.BeggarSpawnIntervalSeconds.Value = 60;
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.IntervalSetCalls == 2 && scene.Camp.IntervalNative == 55f,
            "the interval config change rewrites the owned interval once (120->60 => 55s fallback)");
        Checks.Check(scene.Camp.SpawnCalls == 1, "the rewritten interval re-times the deadline without a burst");
        Host.Tick(58.0f);
        Checks.Check(scene.Camp.SpawnCalls == 1, "the new deadline is honored (no catch-up)");
        Host.Tick(2.5f);
        Checks.Check(scene.Camp.SpawnCalls == 2, "the camp replenishes on the new cadence");
        StopScene();
    }

    private static void DriftStopChecks()
    {
        Fixture scene = Fixture.Create(82);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);

        scene.Camp.MaxNative = 3; // 外国 writer 改走 max 字段
        Host.Tick(1.0f);
        Checks.Check(scene.Camp.MaxSetCalls == 1, "an external max write is never overwritten by the cadence");
        Host.Tick(119.0f);
        Host.Tick(1.0f);
        Checks.Check(scene.Camp.SpawnCalls == 0, "the spawn commit gate stops instead of spawning after a drift");
        Checks.Check(scene.Camp.MaxNative == 3, "the drifted max field is handed back untouched (no seizure)");
        Checks.Check(scene.Camp.IntervalSetCalls == 2 && scene.Camp.IntervalNative == 125f,
            "the still-owned interval field is restored to the captured original exactly once");
        Checks.Check(Host.Phase == "Faulted", "the scene stop is terminal instead of a retry loop");
        int maxWrites = scene.Camp.MaxSetCalls, intervalWrites = scene.Camp.IntervalSetCalls;
        Host.Tick(5.0f);
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites && scene.Camp.IntervalSetCalls == intervalWrites,
            "after the stop no further field write happens (no per-frame restore, no re-claim)");
        StopScene();
    }

    private static void PartialWriteChecks()
    {
        Fixture scene = Fixture.Create(93);
        SetPopulation(true);
        SetSteps(4, 120);
        scene.Camp.FailIntervalWrite = true;
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.MaxSetCalls == 2 && scene.Camp.MaxNative == 4,
            "a failed second field write hands the already-confirmed max field back in the same catch");
        Checks.Check(scene.Camp.IntervalSetCalls == 0 && scene.Camp.IntervalNative == 125f,
            "the failed interval write leaves the original value untouched (no guessed result, no rewrite)");
        Host.Tick(119.0f);
        Host.Tick(1.0f);
        Checks.Check(scene.Camp.SpawnCalls == 0, "an unclaimed camp never reaches the native spawn call");
        Checks.Check(Host.Phase == "Faulted", "the unclaimed-camp commit gate stops the scene");
        int maxWrites = scene.Camp.MaxSetCalls;
        Host.Tick(5.0f);
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites, "the partial failure is not retried on later ticks");
        StopScene();

        // 第一次写（max）就失败：不产生任何凭据、不猜失败结果、interval 不再动。
        Fixture first = Fixture.Create(155);
        SetPopulation(true);
        SetSteps(4, 120);
        first.Camp.FailMaxWrite = true;
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(first.Camp.MaxSetCalls == 0 && first.Camp.MaxNative == 4,
            "a failed first field write owns nothing (the throwing setter never counts as a write)");
        Checks.Check(first.Camp.IntervalSetCalls == 0 && first.Camp.IntervalNative == 125f,
            "a failed first field write never touches the second field");
        Host.Tick(119.0f);
        Host.Tick(1.0f);
        Checks.Check(first.Camp.SpawnCalls == 0 && Host.Phase == "Faulted",
            "the never-claimed camp stops the scene without a blind spawn or retry");
        StopScene();
    }

    private static void SpawnFaultChecks()
    {
        Fixture scene = Fixture.Create(104);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        scene.Camp.FailSpawn = true;
        Host.Tick(119.0f);
        Host.Tick(1.0f);
        Checks.Check(scene.Camp.SpawnCalls == 1 && scene.Roster() == 0,
            "a throwing native SpawnBeggar is not retried and leaves the roster unchanged");
        Checks.Check(scene.Camp.IntervalSetCalls == 2 && scene.Camp.IntervalNative == 125f
            && scene.Camp.MaxSetCalls == 2 && scene.Camp.MaxNative == 4,
            "the spawn fault releases both owned fields once (back to the captured originals)");
        Checks.Check(Host.Phase == "Faulted", "the spawn fault stops the scene instead of a blind redo");
        Host.Tick(5.0f);
        Checks.Check(scene.Camp.SpawnCalls == 1, "no automatic retry of the paid spawn on the next deadline");
        StopScene();
    }

    private static void DeltaUncertaintyChecks()
    {
        Fixture zero = Fixture.Create(115);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        zero.Camp.SpawnDelta = 0;
        Host.Tick(119.0f);
        Host.Tick(1.0f);
        Checks.Check(zero.Camp.SpawnCalls == 1 && zero.Roster() == 0 && Host.Phase == "Faulted",
            "a zero-name delta stops the scene and hands the owned fields back");
        Host.Tick(5.0f);
        Checks.Check(zero.Camp.SpawnCalls == 1, "the uncertain zero delta is not replayed on a later deadline");
        StopScene();

        Fixture two = Fixture.Create(126);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        two.Camp.SpawnDelta = 2;
        Host.Tick(119.0f);
        Host.Tick(1.0f);
        Checks.Check(two.Camp.SpawnCalls == 1 && two.Roster() == 2 && Host.Phase == "Faulted",
            "a two-name delta (not exactly one) stops the scene and hands the owned fields back");
        Checks.Check(two.Camp.MaxNative == 4 && two.Camp.IntervalNative == 125f,
            "the double delta released both fields to the captured originals");
        Host.Tick(5.0f);
        Checks.Check(two.Camp.SpawnCalls == 1, "the uncertain double delta is not replayed");
        StopScene();
    }

    private static void OffHandsBackOnceChecks()
    {
        Fixture scene = Fixture.Create(137);
        SetPopulation(false);
        SetSteps(4, 120);
        Checks.Check(ModConfig.TogglePopulation(), "the real UI toggle turns the batch gate ON and saves");
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.MaxNative == 0 && scene.Camp.IntervalNative == 115f, "the camp is claimed while ON");
        int maxWrites = scene.Camp.MaxSetCalls, intervalWrites = scene.Camp.IntervalSetCalls;

        Checks.Check(!ModConfig.TogglePopulation(), "the real UI toggle turns the batch gate OFF again");
        PopulationPerformanceCoordinator.StopAndRelease();
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites + 1 && scene.Camp.MaxNative == 4,
            "the real OFF action hands the max field back in exactly one write");
        Checks.Check(scene.Camp.IntervalSetCalls == intervalWrites + 1 && scene.Camp.IntervalNative == 125f,
            "the real OFF action hands the interval field back in exactly one write");
        Host.Tick(5.0f);
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites + 1 && scene.Camp.IntervalSetCalls == intervalWrites + 1,
            "no per-frame restore repeats after the hand-back");

        Stubs.ResetTouches();
        Stubs.Throwing = true;
        try { Host.Update(); }
        catch (Exception error) { Checks.Check(false, "the OFF coordinator entry escaped a throwing interop: " + error.GetType().Name); }
        Stubs.Throwing = false;
        Checks.Check(Stubs.TouchCount == 0, "the OFF coordinator entry is a pure managed gate (zero interop touches)");

        Checks.Check(ModConfig.TogglePopulation(), "a second real ON toggle is accepted as a new start");
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites + 2 && scene.Camp.MaxNative == 0,
            "OFF then ON starts a fresh claim instead of resuming a stale credential");
        StopScene();
    }

    private static void SceneChangeChecks()
    {
        Fixture scene = Fixture.Create(148);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        int maxWrites = scene.Camp.MaxSetCalls, intervalWrites = scene.Camp.IntervalSetCalls;

        scene.SwitchSceneHandle(999);
        Host.Tick(0.6f);
        Checks.Check(Host.Phase == "Faulted", "a scene change stops the central work of the old scene");
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites + 1 && scene.Camp.IntervalSetCalls == intervalWrites + 1,
            "the scene change hands both owned fields back exactly once");
        Host.Tick(200.0f);
        Checks.Check(scene.Camp.SpawnCalls == 0 && scene.Roster() == 0,
            "the released old-scene camp never receives a central spawn");
    }

    // 1) ctx 未 ready 时 auth 单独不足以接管；2) 接管前的早捕获不当基线（native 变化后 OFF 回新值）。
    private static void CtxGateChecks()
    {
        Fixture scene = Fixture.Create(211);
        SetPopulation(true);
        SetSteps(4, 120);
        BeggarCamp_Awake_Patch.Awake_Prefix(scene.Camp);
        BeggarCamp_Awake_Patch.Awake_Postfix(scene.Camp);
        Checks.Check(scene.Camp.MaxSetCalls == 0 && scene.Camp.IntervalSetCalls == 0 && scene.Camp.MaxNative == 4,
            "auth alone with no ready current ctx never claims a camp at Awake (zero field writes)");

        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.MaxNative == 0 && scene.Camp.IntervalNative == 115f,
            "the same camp is claimed once the real current ctx is ready");
        StopScene();

        CampaignSaveData saved = CampaignSaveData.Current;
        CampaignSaveData.Current = null;
        BeggarCamp orphan = scene.AddCamp();
        BeggarCamp_Awake_Patch.Awake_Prefix(orphan);
        BeggarCamp_Awake_Patch.Awake_Postfix(orphan);
        Checks.Check(orphan.MaxSetCalls == 0 && orphan.IntervalSetCalls == 0,
            "a null real current context keeps every late camp native (zero writes)");
        CampaignSaveData.Current = saved;
    }

    // 故障后 Awake 也必须零写；接管前重捕当前原生值，OFF 交还的是新基线而非 Awake 早捕获值。
    private static void FaultBlocksAwakeAndCaptureChecks()
    {
        Fixture scene = Fixture.Create(222);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        PopulationPerformanceCoordinator.StopAndRelease();
        Checks.Check(Host.Phase == "Faulted", "the scene stop is terminal for later events");

        BeggarCamp late = scene.AddCamp();
        BeggarCamp_Awake_Patch.Awake_Prefix(late);
        BeggarCamp_Awake_Patch.Awake_Postfix(late);
        Host.Tick(0.6f);
        Checks.Check(late.MaxSetCalls == 0 && late.IntervalSetCalls == 0 && late.MaxNative == 4,
            "after a terminal stop even a late Awake writes nothing");

        Fixture rebind = Fixture.Create(223);
        SetPopulation(true);
        SetSteps(4, 120);
        BeggarCamp_Awake_Patch.Awake_Prefix(rebind.Camp);
        rebind.Camp.MaxNative = 7;
        rebind.Camp.IntervalNative = 99f;
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(rebind.Camp.MaxNative == 0 && rebind.Camp.IntervalNative == 115f,
            "the claim re-captures the field values right before the first real write");
        PopulationPerformanceCoordinator.StopAndRelease();
        Checks.Check(rebind.Camp.MaxNative == 7 && rebind.Camp.IntervalNative == 99f,
            "the hand-back restores the value captured before the write, not the stale Awake value");
    }

    // BeginScene 换 ctx：先交还旧 scene 的仍拥有字段，再清/重建，不把旧 Original 绑给新 ctx。
    private static void CrossContextHandbackChecks()
    {
        Fixture old = Fixture.Create(233);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(old.Camp.MaxNative == 0, "old ctx claims its camp before the rebind");
        int maxWrites = old.Camp.MaxSetCalls, intervalWrites = old.Camp.IntervalSetCalls;

        Fixture next = Fixture.Create(234);
        PopulationPerformanceCoordinator.BeginScene(next.Campaign);
        Checks.Check(old.Camp.MaxSetCalls == maxWrites + 1 && old.Camp.MaxNative == 4
            && old.Camp.IntervalSetCalls == intervalWrites + 1 && old.Camp.IntervalNative == 125f,
            "rebinding to a new ctx hands the old scene's owned fields back exactly once");
        Host.Tick(200f);
        Checks.Check(old.Camp.SpawnCalls == 0, "the released old-context camp never receives a central spawn");
        StopScene();
    }

    // OFF/终态下三个真实回调（Update/OnDisable/OnDestroy）必须在任何 Unity 比较之前纯托管退出。
    private static void OffCallbackGateChecks()
    {
        Fixture scene = Fixture.Create(244);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(!ModConfig.TogglePopulation(), "the real toggle switches the batch gate OFF");
        PopulationPerformanceCoordinator.StopAndRelease();
        Host.Attach();

        Stubs.ResetTouches();
        Stubs.Throwing = true;
        try
        {
            Host.Update();
            Host.Invoke("OnDisable");
            Host.Invoke("OnDestroy");
        }
        catch (Exception error)
        {
            Checks.Check(false, "a callback escaped a throwing interop: " + error.GetType().Name);
        }
        Stubs.Throwing = false;
        Checks.Check(Stubs.TouchCount == 0,
            "Update/OnDisable/OnDestroy read zero native members in the OFF/terminal state");
        Checks.Check(scene.Camp.MaxSetCalls == 2 && scene.Camp.IntervalSetCalls == 2 && scene.Camp.SpawnCalls == 0,
            "the OFF callbacks neither rewrite fields nor spawn (claim + one hand-back only)");
    }

    // 释放/接管的异常隔离：首个 profile getter 抛错不阻断其余释放；interval 更新失败必须终态停止；
    // Spawn 失败显式结束本轮补员枚举。
    private static void IsolationAndRoundChecks()
    {
        Fixture two = Fixture.Create(255);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        BeggarCamp second = two.AddCamp();
        BeggarCamp_Awake_Patch.Awake_Prefix(second);
        BeggarCamp_Awake_Patch.Awake_Postfix(second);
        Host.Tick(0.6f);
        Checks.Check(second.MaxNative == 0 && two.Camp.MaxNative == 0, "both camps are claimed");

        two.Camp.FailGetter = true;
        PopulationPerformanceCoordinator.StopAndRelease();
        Checks.Check(second.MaxNative == 4 && second.IntervalNative == 125f,
            "a throwing first profile never blocks releasing the remaining profile");
        Checks.Check(two.Camp.MaxNative == 0, "the throwing profile keeps its untouchable value (no seizure)");
        two.Camp.FailGetter = false;

        Fixture upd = Fixture.Create(266);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        upd.Camp.FailIntervalWrite = true;
        ModConfig.BeggarSpawnIntervalSeconds.Value = 60;
        Host.Tick(0.6f);
        Checks.Check(Host.Phase == "Faulted", "a throwing interval update stops the scene terminally");
        int writes = upd.Camp.IntervalSetCalls;
        Host.Tick(130f);
        Checks.Check(upd.Camp.IntervalSetCalls == writes && upd.Camp.SpawnCalls == 0,
            "the failed interval update is never retried on the next cadence and spawns nothing");

        Fixture round = Fixture.Create(277);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        BeggarCamp other = round.AddCamp();
        BeggarCamp_Awake_Patch.Awake_Prefix(other);
        BeggarCamp_Awake_Patch.Awake_Postfix(other);
        Host.Tick(0.6f);
        round.Camp.FailSpawn = true;
        Host.Tick(121f);
        Checks.Check(round.Camp.SpawnCalls == 1 && other.SpawnCalls == 0,
            "a failed spawn ends the replenish round instead of continuing to another camp");
        Checks.Check(Host.Phase == "Faulted" && other.MaxNative == 4,
            "the ended round hands the remaining camps back in the same stop");
        StopScene();
    }

    // ANDROID API 适配：观察内容生成处不调用 4 个 stripped 成员/查询（固定 unavailable），
    // 其余已证 native 读取（bodyType/gravity/velocity/ground/collider）与预算/异常隔离保持。
    private static void GroundingApiAdaptationChecks()
    {
        Fixture scene = Fixture.Create(411);
        SetPopulation(true);
        SetSteps(4, 120);
        KingdomEnhancedPlugin.Initialize();
        Stubs.ResetObservation();
        MelonLoader.MelonLogger.WarningCalls = 0;
        MelonLoader.MelonLogger.LastMessage = null;
        MelonLoader.MelonLogger.LastWarning = null;

        Beggar beggar = scene.AddBeggar(scene.Camp);
        beggar.Go.ParentOfTransform = scene.SceneRoot;
        var body = new UnityEngine.Rigidbody2D { InstanceId = Stubs.NextInstanceId() };
        body.Go = beggar.Go;
        beggar.Go.Components.Add(body);
        var ground = new UnityEngine.Collider2D { InstanceId = Stubs.NextInstanceId() };
        ground.Go = new UnityEngine.GameObject { Name = "terrain", Layer = 0 };
        World.GroundStub = ground;
        var child = new UnityEngine.Collider2D { InstanceId = Stubs.NextInstanceId() };
        child.Go = new UnityEngine.GameObject { Name = "body-collider", Layer = 17 };
        beggar.Go.Children.Add(child);

        PopulationGrounding.Begin(Il2Cpp.Managers.StubInstance.WorldStub, scene.SceneRoot, 7);
        PopulationGrounding.Observe(beggar, scene.Camp, 1, true);

        string sample = MelonLoader.MelonLogger.LastMessage;
        Checks.Check(sample != null && sample.Contains("simulated=unavailable") && sample.Contains("constraints=unavailable")
            && sample.Contains("detect=unavailable") && sample.Contains(":ignoreGround=unavailable"),
            "the Android sample reports the four stripped members as unavailable");
        Checks.Check(sample != null && sample.Contains("body=") && sample.Contains("gravity=")
            && sample.Contains("velocity=1.500,-2.500") && sample.Contains("ground=terrain:0")
            && sample.Contains("colliders=[body-collider:17"),
            "the supported body/ground/collider reads stay in the sample");
        Checks.Check(body.StrippedGetterCalls == 0 && UnityEngine.Physics2D.StrippedQueryCalls == 0,
            "no stripped getter or stripped query is ever invoked");
        Checks.Check(body.VelocityGetterCalls == 1, "velocity is read through exactly one getter call");
        Checks.Check(body.SupportedGetterCalls == 2 && MelonLoader.MelonLogger.WarningCalls == 0,
            "bodyType/gravity read once each and the successful sample logs no warning");
        Checks.Check(beggar.Go.Tr.PositionWrites == 0,
            "the observation writes no position (physics getters are read-only)");

        // 预算 + 异常隔离：throwing interop 下同一次观察只记一条一次性 warning（含 message/stack），
        // 第二次观察仍可能读取，但每场景失败日志只记录一次，catch 范围未扩大。
        Stubs.Throwing = true;
        try { PopulationGrounding.Observe(beggar, scene.Camp, 1, true); }
        catch (Exception error) { Checks.Check(false, "the observation escaped its catch under a throwing interop: " + error.GetType().Name); }
        Stubs.Throwing = false;
        Checks.Check(MelonLoader.MelonLogger.WarningCalls == 1
            && (MelonLoader.MelonLogger.LastWarning ?? "").Contains("message=")
            && (MelonLoader.MelonLogger.LastWarning ?? "").Contains("native read"),
            "the one-shot diagnostic keeps its budget and carries the exception message");
        Stubs.Throwing = true;
        try { PopulationGrounding.Observe(beggar, scene.Camp, 1, true); }
        catch (Exception error) { Checks.Check(false, "the second observation escaped its catch: " + error.GetType().Name); }
        Stubs.Throwing = false;
        Checks.Check(MelonLoader.MelonLogger.WarningCalls == 1, "the once-per-scene failure warning remains bounded");
        Stubs.ResetObservation();
        StopScene();
    }

    // 3) 接管门：同 campaign 但 world/sceneRoot 已换 → 零新接管；foreign layer 的晚营地 Awake 零参数读写。
    private static void WorldLayerGateChecks()
    {
        Fixture scene = Fixture.Create(311);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        int maxWrites = scene.Camp.MaxSetCalls;
        Checks.Check(scene.Camp.MaxNative == 0, "the camp is claimed in the bound scene");

        // 同一 campaign，但 Managers.world 整体换掉（world pointer 已不同）。
        var replacement = new Il2Cpp.World { InstanceId = Stubs.NextInstanceId() };
        replacement.Pointer = Stubs.NextPointer();
        replacement.Go = new UnityEngine.GameObject { Name = "world-next" };
        var layer = new UnityEngine.GameObject { Name = "layer-next" };
        replacement.Layer = layer.Tr;
        Il2Cpp.Managers.StubInstance.WorldStub = replacement;
        Host.Tick(0.6f);
        Checks.Check(scene.Camp.MaxSetCalls == maxWrites + 1 && scene.Camp.MaxNative == 4,
            "a swapped world is never taken over again: only the one hand-back write happens");
        Checks.Check(Host.Phase == "Faulted", "the mismatched world stops the scene instead of writing");

        Fixture layerScene = Fixture.Create(312);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        int layerWrites = layerScene.Camp.IntervalSetCalls;
        // foreign layer 的晚营地：scene handle 与 capture 的 layer 不同 → Awake 零参数读写。
        BeggarCamp foreign = layerScene.AddForeignCamp(999);
        Stubs.ResetTouches();
        BeggarCamp_Awake_Patch.Awake_Prefix(foreign);
        BeggarCamp_Awake_Patch.Awake_Postfix(foreign);
        Checks.Check(foreign.MaxSetCalls == 0 && foreign.IntervalSetCalls == 0,
            "a camp from a foreign Unity scene never gets its parameters read or written at Awake");
        Checks.Check(layerScene.Camp.IntervalSetCalls == layerWrites, "the bound camp is unaffected by the foreign camp");
        StopScene();
    }

    // 4) fault 只停当前 scene：真实新 Apply callback 仍可重开（不是永久禁止）。
    private static void RestartAfterFaultChecks()
    {
        Fixture scene = Fixture.Create(321);
        SetPopulation(true);
        SetSteps(4, 120);
        PopulationPerformanceCoordinator.EnableCurrentScene();
        Host.Attach();
        Host.Tick(0.6f);
        PopulationPerformanceCoordinator.StopAndRelease();
        Checks.Check(Host.Phase == "Faulted", "the scene is terminal while the new ctx is not yet applied");

        BeggarCamp late = scene.AddCamp();
        Stubs.ResetTouches();
        Stubs.Throwing = true;
        try
        {
            BeggarCamp_Awake_Patch.Awake_Prefix(late);
            BeggarCamp_Awake_Patch.Awake_Postfix(late);
            PopulationPerformanceCoordinator.ConfigureCamp(late);
        }
        catch (Exception error)
        {
            Checks.Check(false, "a stopped-scene entry escaped a throwing interop: " + error.GetType().Name);
        }
        Stubs.Throwing = false;
        Checks.Check(Stubs.TouchCount == 0 && late.MaxSetCalls == 0 && late.IntervalSetCalls == 0,
            "the terminal scene gate plus ConfigureCamp entry read and write zero native members");

        // 真实新 Apply callback：换 ctx（新 campaign/world）→ 重启并接管。
        Fixture next = Fixture.Create(322);
        PopulationPerformanceApplyPatch.Postfix(next.Campaign);
        Host.Attach();
        Host.Tick(0.6f);
        Checks.Check(next.Camp.MaxNative == 0 && next.Camp.IntervalNative == 115f,
            "a real new Apply callback restarts the central mode after a fault (the stop is not permanent)");
        Checks.Check(Host.Phase != "Faulted", "the restarted scene left the terminal phase");
        StopScene();
    }

    private static void SetPopulation(bool enabled) => ModConfig.PopulationEnabled.Value = enabled;

    private static void SetSteps(int capacity, int interval)
    {
        ModConfig.BeggarCampCapacity.Value = capacity;
        ModConfig.BeggarSpawnIntervalSeconds.Value = interval;
    }

    private static void StopScene()
    {
        PopulationPerformanceCoordinator.StopAndRelease();
        UnityEngine.Time.timeScale = 1f;
    }
}

/// <summary>一个可用的场景夹具：真实 Managers/World/Kingdom/CampaignSaveData + 一个已注册帐篷。</summary>
internal sealed class Fixture
{
    internal CampaignSaveData Campaign;
    internal BeggarCamp Camp;
    internal Transform SceneRoot;
    internal Il2Cpp.Managers Managers;

    internal int Roster() => Managers.KingdomStub.Beggars.Count;

    internal static Fixture Create(int sceneHandle)
    {
        var fixture = new Fixture();
        fixture.Managers = Stubs.NewScene(sceneHandle, out BeggarCamp camp, out Transform root, out CampaignSaveData campaign);
        fixture.Camp = camp;
        fixture.SceneRoot = root;
        fixture.Campaign = campaign;
        return fixture;
    }

    internal BeggarCamp AddForeignCamp(int sceneHandle)
    {
        var camp = new BeggarCamp { InstanceId = Stubs.NextInstanceId() };
        camp.Pointer = Stubs.NextPointer();
        camp.Go = new UnityEngine.GameObject { Name = "camp-foreign" };
        UnityEngine.SceneManagement.Scene scene = default;
        scene.handle = sceneHandle;
        camp.Go.SceneOf = scene;
        Managers.KingdomStub.BeggarCamps.Add(camp);
        return camp;
    }

    internal BeggarCamp AddCamp()
    {
        Transform root = Managers.WorldStub.gameLayer;
        var camp = new BeggarCamp { InstanceId = Stubs.NextInstanceId() };
        camp.Pointer = Stubs.NextPointer();
        camp.Go = new UnityEngine.GameObject { Name = "camp-late" };
        UnityEngine.SceneManagement.Scene scene = default;
        scene.handle = root.gameObject.scene.handle;
        camp.Go.SceneOf = scene;
        Managers.KingdomStub.BeggarCamps.Add(camp);
        return camp;
    }

    internal Beggar AddBeggar(BeggarCamp camp)
    {
        Beggar beggar = Stubs.NewBeggar(camp);
        Managers.KingdomStub.Beggars.Add(beggar);
        return beggar;
    }

    internal void SwitchSceneHandle(int handle)
    {
        // 真实换场景：gameLayer 换成新的原生对象（新 pointer + 新 scene handle）。
        var layerObject = new UnityEngine.GameObject { Name = "gameLayer-next" };
        UnityEngine.SceneManagement.Scene scene = default;
        scene.handle = handle;
        layerObject.SceneOf = scene;
        Managers.WorldStub.Layer = layerObject.Tr;
    }
}

/// <summary>驱动被注入组件的真实 Update（私有实例方法）并读取阶段，全部走真实生产代码路径。</summary>
internal static class Host
{
    private static readonly MethodInfo UpdateHook =
        typeof(PopulationPerformanceCoordinator).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo InstanceField =
        typeof(PopulationPerformanceCoordinator).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo PhaseField =
        typeof(PopulationPerformanceCoordinator).GetField("_phase", BindingFlags.Static | BindingFlags.NonPublic);

    internal static PopulationPerformanceCoordinator Instance;

    internal static string Phase => PhaseField.GetValue(null).ToString();

    internal static void Attach() => Instance = (PopulationPerformanceCoordinator)InstanceField.GetValue(null);

    internal static void Tick(float delta)
    {
        UnityEngine.Time.time += delta;
        UnityEngine.Time.unscaledTime += delta;
        Update();
    }

    internal static void Update()
    {
        if (Instance == null) Attach();
        UpdateHook.Invoke(Instance, null);
    }

    internal static void Invoke(string name)
        => typeof(PopulationPerformanceCoordinator)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Instance, null);
}

internal static class Checks
{
    internal static int Passed;
    internal static int Failed;

    internal static void Check(bool condition, string what)
    {
        if (condition) { Passed++; return; }
        Failed++;
        Console.WriteLine("FAIL " + what);
    }
}
