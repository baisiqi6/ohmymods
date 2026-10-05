#if ANDROID
using Il2Cpp;
#endif
// issue-94 夜袭出发补偿回归：链接真实生产两文件（NightDepartureTiming.cs /
// PatchWorld_NightDeparture.cs），stub 外部边界。oracle 值全部来自已审设计
// （decision-proposal-review.md 的算术表），不是生产实现的镜像。
// 绿：dotnet run -c Release --project tests/night-departure/NightDepartureTests.csproj
// 红对照：追加 -- --red。红模式把替身的 SuppressTravelPostfixCall 置 true（真实
// postfix 完全不被调用），同一批“预期补偿”断言应当失败并以非零退出——证明绿断言有牙。
using System;
using System.Collections.Generic;
using System.Globalization;
using KingdomEnhancedMod;

int passed = 0, failed = 0;
void Check(string name, bool ok)
{
    if (ok) { passed++; Console.WriteLine("PASS " + name); }
    else { failed++; Console.WriteLine("FAIL " + name); }
}

// —— 场景搭建：每例独立世界图 ——
static (Director dir, EnemyManager em, Wave wave) Graph(
    float now = 0f, float travel = 3f, WaveType type = WaveType.Regular)
{
    NightDepartureScope.TestReset();
    NightDepartureLog.TestReset();
    KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
    ModConfig.Enabled.Value = true;
#if ANDROID
    ModConfig.NightDepartureEnabled.Value = true; // independent scenarios exercise opt-in ON; default tested below
#endif
    NetworkBigBoss.HasWorldAuth = true;

    var managers = new Managers();
    managers.director.currentTime = now;
    managers.enemies.ConfiguredTravel = travel;
    Managers.Inst = managers;
    return (managers.director, managers.enemies, new Wave { type = type });
}

// 直调真实 Harmony 入口（prefix → postfix → finalizer），day 默认取 stub 的
// CurrentDayForSpawning=12。
static float RunTravel(float arrival, float travel, float now,
    out Director dir, out Wave wave, Action between = null, int day = 12)
{
    var (d, em, w) = Graph(now, travel);
    dir = d; wave = w;
    NightDepartureScope.State st = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, arrival, ref st);
    between?.Invoke();
    float r = travel;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, day, ref r);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
    return r;
}

int NightLogCount() => KingdomEnhancedPlugin.Instance?.LogSource.Info.FindAll(s => s.Contains("[NightDeparture]")).Count ?? 0;

#if ANDROID
// Real MobilePlayerConfig, standard loader entry host double; no scope/planner mirror.
bool seedNight = Array.IndexOf(args, "--seed-night") >= 0;
bool old16 = Array.IndexOf(args, "--old16cfg") >= 0;
if (old16)
{
    foreach (var entry in new Dictionary<string,object> {
        {"SpeedMultiplier",1},{"InfiniteSteedStamina",false},{"HoldPurchaseEnabled",false},{"CalendarEnabled",true},
        {"EnemyCountMultiplier",1f},{"EnemyTimelineSpeed",1f},{"SteedCooldownMultiplier",1f},{"StaffCooldownMultiplier",1f},
        {"InfiniteMoney",false},{"FarmCatsEnabled",false},{"FastBuild",false},{"BoatCapacityEnabled",false},
        {"MapSizeMultiplier",1f},{"FastForestRecedeEnabled",false},{"DeerPopulationEnabled",false},{"DenseThicketsEnabled",false} })
        MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android",entry.Key,entry.Value);
}
static object Old16Values() => (ModConfig.SpeedMultiplier.Value, ModConfig.InfiniteSteedStamina.Value,
    ModConfig.HoldPurchaseEnabled.Value, ModConfig.CalendarEnabled.Value, ModConfig.EnemyCountMultiplier.Value,
    ModConfig.EnemyTimelineSpeed.Value, ModConfig.SteedCooldownMultiplier.Value, ModConfig.StaffCooldownMultiplier.Value,
    ModConfig.InfiniteMoney.Value, ModConfig.FarmCatsEnabled.Value, ModConfig.FastBuild.Value,
    ModConfig.BoatCapacityEnabled.Value, ModConfig.MapSizeMultiplier.Value, ModConfig.FastForestRecedeEnabled.Value,
    ModConfig.DeerPopulationEnabled.Value, ModConfig.DenseThicketsEnabled.Value);
if (seedNight) MelonLoader.MelonPreferencesStub.Seed("OhMyMods.Android", "NightDepartureEnabled", true);
ModConfig.Initialize(_ => { });
Check("Android creates 17 real entries", MelonLoader.MelonPreferencesStub.CreatedEntries.Count == 17);
Check("Android night declared default false", MelonLoader.MelonPreferencesStub.CreatedEntries.Contains("OhMyMods.Android/NightDepartureEnabled default=False"));
Check("Android night load absorbs seed or defaults OFF", ModConfig.NightDepartureEnabled.Value == seedNight);
Check("Android load saves zero", MelonLoader.MelonPreferencesStub.SaveCalls == 0);
object old16Values=Old16Values();
Check("Android old16 cfg absorbs existing calendar with no save", ModConfig.CalendarEnabled.Value == old16 && MelonLoader.MelonPreferencesStub.SaveCalls==0);
ModConfig.SetNightDeparture(seedNight);
Check("Android same value saves zero", MelonLoader.MelonPreferencesStub.SaveCalls == 0);
ModConfig.SetNightDeparture(!seedNight);
Check("Android changed value saves once", MelonLoader.MelonPreferencesStub.SaveCalls == 1 && ModConfig.NightDepartureEnabled.Value == !seedNight);
ModConfig.SetNightDeparture(!seedNight);
Check("Android repeated same value does not save", MelonLoader.MelonPreferencesStub.SaveCalls == 1);
ModConfig.ToggleNightDeparture();
Check("Android UI toggle saves exactly once", MelonLoader.MelonPreferencesStub.SaveCalls == 2 && ModConfig.NightDepartureEnabled.Value == seedNight);
Check("Android night changes leave all old16 values intact",Old16Values().Equals(old16Values));
{
    var (d, em, w) = Graph(0f, 3f);
    ModConfig.NightDepartureEnabled.Value = false; d.ThrowOnPointer = true;
    NightDepartureScope.State outer = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d,w,Side.Left,1f,ref outer);
    Check("Android OFF still pushes disabled mask before native reads", NightDepartureScope.TestDepth == 1 && d.TimesOfDayQueries.Count == 0 && KingdomEnhancedPlugin.Instance.LogSource.Warning.Count == 0);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null,ref outer);
}
{
    var (d, em, w) = Graph(0f, 3f);
    NightDepartureScope.State outer = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d,w,Side.Left,1f,ref outer);
    ModConfig.NightDepartureEnabled.Value = false;
    NightDepartureScope.State inner = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d,w,Side.Left,1f,ref inner);
    float r=3f;PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em,w,1.5f,12,ref r);
    Check("Android OFF nested call masks ON parent", r==3f && NightDepartureScope.TestDepth==2);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null,ref inner);
    ModConfig.NightDepartureEnabled.Value = true;
    r=3f;PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em,w,1.5f,12,ref r);
    Check("Android parent remains available after disabled child",r==5f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null,ref outer);
}
{
    var (d,em,w)=Graph(0f,3f);NightDepartureScope.State st=default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d,w,Side.Left,1f,ref st);
    ModConfig.NightDepartureEnabled.Value=false;
    float r=3f;PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em,w,1.5f,12,ref r);
    Check("Android gate changed at commit keeps native value",r==3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null,ref st);
}
{
    var (d,em,w)=Graph(0f,3f);
    d.ScheduleWaveToday(w,Side.Left,1f);d.ScheduleWaveToday(w,Side.Left,1f);
    var lines=KingdomEnhancedPlugin.Instance.LogSource.Info.FindAll(s=>s.StartsWith("ANDROID_NIGHT_DEPARTURE_SCOPE"));
    Check("Android natural scope observation is bounded once",lines.Count==1);
    Check("Android scope observation includes real captured E D current decision",lines.Count==1&&lines[0].Contains("E=18.00 D=6.00 current=0.00")&&lines[0].Contains("decision=compensated"));
}
#endif

// ============ 一、纯策略（真实 NightDepartureTiming.TryPlan） ============
{
    Console.WriteLine("-- pure policy (A=25: arrival=1, E=18, D=6, N=0 unless stated)");
    NightDepartureTiming.Plan? Plan(float arrival, float travel, float e = 18f, float d = 6f, float n = 0f)
        => NightDepartureTiming.TryPlan(arrival, travel, e, d, n);
    var p = Plan(1f, 2f); Check("T2 -> +1 (23->22)", p != null && p.Value.ExtraHours == 1f && p.Value.OriginalSpawn == 23f && p.Value.AdjustedSpawn == 22f && p.Value.TravelWithMargin == 3f);
    p = Plan(1f, 3f); Check("T3 -> +2 cap (22->20)", p != null && p.Value.ExtraHours == 2f && p.Value.AdjustedSpawn == 20f);
    p = Plan(1f, 1.5f); Check("T1.5 -> +0.5 (23.5->23)", p != null && p.Value.ExtraHours == 0.5f && p.Value.AdjustedSpawn == 23f);
    Check("near T0.5 unchanged", Plan(1f, 0.5f) == null);
    Check("near T1 unchanged", Plan(1f, 1f) == null);
    p = Plan(1f, 6f); Check("T6 evening clamp (19->18)", p != null && p.Value.ExtraHours == 1f && p.Value.AdjustedSpawn == 18f);
    Check("T8 native earlier (S0=17<E) unchanged", Plan(1f, 8f) == null);
    p = Plan(1f, 3f, n: 21f); Check("late call N21 (22->21)", p != null && p.Value.ExtraHours == 1f && p.Value.AdjustedSpawn == 21f);
    Check("late call N22 expired unchanged", Plan(1f, 3f, n: 22f) == null);
    Check("late call N23 unchanged", Plan(1f, 3f, n: 23f) == null);
    p = Plan(2f, 1.25f); Check("cross-midnight S0=24.75 (A=26)", p != null && p.Value.AdjustedSpawn == 24.5f);
    p = Plan(2f, 1.25f, n: 23.5f); Check("cross-midnight late call", p != null && p.Value.AdjustedSpawn == 24.5f);
    // arrival=12 走 +24（A=36）；12 以上保持当天。
    Check("arrival 12 -> A36: S0=32 (T4) outside window", Plan(12f, 4f) == null);
    Check("arrival 12 -> A36: S0=31 (T5) still rejected", Plan(12f, 5f) == null);
    Check("arrival 12 -> A36: S0=30 (T6) strict dawn bound", Plan(12f, 6f) == null);
    p = Plan(12f, 7f); Check("arrival 12 -> A36: S0=29 -> 27", p != null && p.Value.ExtraHours == 2f && p.Value.AdjustedSpawn == 27f);
    Check("arrival 12.5 stays same-day (S0<E)", Plan(12.5f, 5f) == null);
    Check("H bound: S0=30 (A=32,T2) unchanged", Plan(8f, 2f) == null);
    Check("H bound: S0=30.5 unchanged", Plan(8f, 1.5f) == null);
    p = Plan(8f, 3f); Check("H bound: S0=29 -> 27", p != null && p.Value.AdjustedSpawn == 27f);
    // 次日 profile 不同于当日：D 取次日。
    Check("next-day dawn 5: S0=29.25 >= H29 unchanged", Plan(7.5f, 2.25f, d: 5f) == null);
    p = Plan(7.5f, 3f, d: 5f); Check("next-day dawn 5: S0=28.5 -> 26.5", p != null && p.Value.AdjustedSpawn == 26.5f);
    // 非法输入一律原生。
    Check("NaN arrival", Plan(float.NaN, 3f) == null);
    Check("+Inf travel", Plan(1f, float.PositiveInfinity) == null);
    Check("-Inf travel", Plan(1f, float.NegativeInfinity) == null);
    Check("negative travel", Plan(1f, -0.1f) == null);
    Check("zero travel (near)", Plan(1f, 0f) == null);
    Check("NaN travel", Plan(1f, float.NaN) == null);
    Check("arrival 24.5 out of range", Plan(24.5f, 3f) == null);
    Check("arrival -0.1 out of range", Plan(-0.1f, 3f) == null);
    Check("N -1 out of range", Plan(1f, 3f, n: -1f) == null);
    Check("N 25 out of range", Plan(1f, 3f, n: 25f) == null);
    Check("E NaN", Plan(1f, 3f, e: float.NaN) == null);
    Check("D NaN", Plan(1f, 3f, d: float.NaN) == null);
    Check("D negative", Plan(1f, 3f, d: -0.1f) == null);
    Check("E == D invalid", Plan(1f, 3f, e: 18f, d: 18f) == null);
    Check("D > E invalid", Plan(1f, 3f, e: 6f, d: 7f) == null);
    Check("E >= 24 invalid", Plan(1f, 3f, e: 24f, d: 6f) == null);
    Check("S0 == N unchanged", Plan(2f, 4f, n: 22f) == null); // A=26,T4 -> S0=22 == N
}

// ============ 二、作用域接线（真实 hook 入口） ============
{
    Console.WriteLine("-- scope wiring");
    Director d; Wave w;
    float r = RunTravel(1f, 3f, 0f, out d, out w);
    Check("far T3 compensated to 5", r == 5f);
    Check("compensation logged once", NightLogCount() == 1);
    r = RunTravel(1f, 0.5f, 0f, out d, out w);
    Check("near unchanged + no log", r == 0.5f && NightLogCount() == 0);
    r = RunTravel(1f, 8f, 0f, out d, out w);
    Check("native earlier unchanged + no log", r == 8f && NightLogCount() == 0);

    // 门外调用：无活动 scope / wave 失配 / manager 失配 / 错误 day。
    var (d0, em0, w0) = Graph(0f, 3f);
    float raw = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em0, w0, 1.5f, 12, ref raw);
    Check("no scope: ordinary query unchanged", raw == 3f);
    NightDepartureScope.State st = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d0, w0, Side.Left, 1f, ref st);
    float raw2 = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em0, new Wave(), 1.5f, 12, ref raw2);
    Check("different wave: unchanged", raw2 == 3f);
    float raw3 = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(new EnemyManager(), w0, 1.5f, 12, ref raw3);
    Check("different enemies manager: unchanged", raw3 == 3f);
    float wrongDay = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em0, w0, 1.5f, 13, ref wrongDay);
    Check("wrong targetDay: unchanged", wrongDay == 3f);
    float raw4 = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em0, w0, 1.5f, 12, ref raw4);
    Check("wrong-day query did not consume scope: correct day compensated", raw4 == 5f);
    float raw5 = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em0, w0, 1.5f, 12, ref raw5);
    Check("single consumption: second call unchanged", raw5 == 3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
    Check("depth back to 0", NightDepartureScope.TestDepth == 0);

    // 开关 / 权限。
    r = RunTravel(1f, 3f, 0f, out d, out w, () => ModConfig.Enabled.Value = false);
    Check("mod disabled at commit: unchanged", r == 3f);
    r = RunTravel(1f, 3f, 0f, out d, out w, () => NetworkBigBoss.HasWorldAuth = false);
    Check("authority lost at commit: unchanged", r == 3f);
    var (dOff, emOff, wOff) = Graph(0f, 3f);
    ModConfig.Enabled.Value = false;
    NightDepartureScope.State stOff = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dOff, wOff, Side.Left, 1f, ref stOff);
    float rOff = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emOff, wOff, 1.5f, 12, ref rOff);
    Check("mod disabled at prefix: unchanged", rOff == 3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stOff);
    var (dCl, emCl, wCl) = Graph(0f, 3f);
    NetworkBigBoss.HasWorldAuth = false;
    NightDepartureScope.State stCl = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dCl, wCl, Side.Left, 1f, ref stCl);
    float rCl = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emCl, wCl, 1.5f, 12, ref rCl);
    Check("client (no world auth): unchanged", rCl == 3f);

    // 全部非 Regular 波型（prefix 拒绝）与提交前波型翻转（commit 复核拒绝）。
    foreach (WaveType t in new[] { WaveType.None, WaveType.BossWave, WaveType.Recovery,
             WaveType.Retaliation, WaveType.PortalDefence, WaveType.EventWave, WaveType.PortalProximity })
    {
        var (dt, emt, wt) = Graph(0f, 3f, t);
        NightDepartureScope.State stt = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dt, wt, Side.Left, 1f, ref stt);
        float rt = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emt, wt, 1.5f, 12, ref rt);
        Check("non-Regular " + t + ": unchanged, frame still pushed+cleaned",
            rt == 3f && NightDepartureScope.TestDepth == 1 && NightLogCount() == 0);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stt);
    }
    r = RunTravel(1f, 3f, 0f, out d, out w, () => w.type = WaveType.BossWave);
    Check("wave type flipped at commit: unchanged", r == 3f);
    r = RunTravel(1f, 3f, 0f, out d, out w, () => Managers.Inst.enemies = new EnemyManager());
    Check("Managers.enemies swapped at commit: unchanged", r == 3f);

    // side 枚举与身份缺件。
    var (ds, ems, ws) = Graph(0f, 3f);
    NightDepartureScope.State sts = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(ds, ws, (Side)0, 1f, ref sts);
    float rs = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(ems, ws, 1.5f, 12, ref rs);
    Check("unknown side 0: unchanged", rs == 3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref sts);
    sts = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(ds, ws, (Side)7, 1f, ref sts);
    rs = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(ems, ws, 1.5f, 12, ref rs);
    Check("unknown side 7: unchanged", rs == 3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref sts);
    float rL = RunTravel(1f, 3f, 0f, out d, out w);
    Check("side Left works", rL == 5f);
    {
        var (dr, emr, wr) = Graph(0f, 3f);
        NightDepartureScope.State str = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dr, wr, Side.Right, 1f, ref str);
        float rr = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emr, wr, 1.5f, 12, ref rr);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref str);
        Check("side Right works + logged", rr == 5f && NightLogCount() == 1
            && KingdomEnhancedPlugin.Instance.LogSource.Info.Find(s => s.Contains("[NightDeparture]")).Contains("side=Right"));
    }
    var (dz, emz, wz) = Graph(0f, 3f);
    wz.Pointer = IntPtr.Zero;
    NightDepartureScope.State stz = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dz, wz, Side.Left, 1f, ref stz);
    float rz = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emz, wz, 1.5f, 12, ref rz);
    Check("zero wave pointer: unchanged", rz == 3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stz);

    // Managers/world/gameLayer/scene/director 缺件与失配。
    foreach (var broken in new[] { "managers", "director-swap", "enemies-null", "world-null",
                                    "layer-null", "layer-object-null", "director-go-null",
                                    "enemies-go-null", "season-max" })
    {
        var (db, emb, wb) = Graph(0f, 3f);
        switch (broken)
        {
            case "managers": Managers.Inst = null; break;
            case "director-swap": Managers.Inst.director = new Director(); break;
            case "enemies-null": Managers.Inst.enemies = null; break;
            case "world-null": Managers.Inst.world = null; break;
            case "layer-null": Managers.Inst.world.gameLayer = null; break;
            case "layer-object-null": Managers.Inst.world.gameLayer.gameObject = null; break;
            case "director-go-null": db.gameObject = null; break;
            case "enemies-go-null": emb.gameObject = null; break;
            case "season-max": db.CurrentSeasonDay = int.MaxValue; break;
        }
        NightDepartureScope.State stb = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(db, wb, Side.Left, 1f, ref stb);
        float rb = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emb, wb, 1.5f, 12, ref rb);
        bool ok = rb == 3f && NightLogCount() == 0 && NightDepartureScope.TestDepth == 1;
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stb);
        Check("broken context [" + broken + "]: unchanged", ok);
    }
    var (dn, emn, wn) = Graph(0f, 3f);
    dn.ProfileResolver = _ => null;
    NightDepartureScope.State stn = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dn, wn, Side.Left, 1f, ref stn);
    float rn = 3f;
    PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(emn, wn, 1.5f, 12, ref rn);
    Check("null profile: unchanged", rn == 3f);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stn);
}

// ============ 三、提交前身份/上下文漂移 ============
{
    Console.WriteLine("-- commit-time drift");
    string[] drifts = { "managers", "director", "world", "gameLayer", "scene", "director-scene",
                        "enemies-scene", "schedulerDay", "seasonDay", "spawnDay" };
    foreach (var drift in drifts)
    {
        var (d, em, w) = Graph(0f, 3f);
        NightDepartureScope.State st = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref st);
        switch (drift)
        {
            case "managers": Managers.Inst = new Managers(); break;
            case "director": Managers.Inst.director = new Director(); break;
            case "world": Managers.Inst.world = new World(); break;
            case "gameLayer": Managers.Inst.world.gameLayer = new UnityEngine.Transform(); break;
            case "scene": Managers.Inst.world.gameLayer.gameObject.scene.handle = 77; break;
            case "director-scene": d.gameObject.scene.handle = 88; break;
            case "enemies-scene": em.gameObject.scene.handle = 89; break;
            case "schedulerDay": d.CurrentSchedulerDay += 1; break;
            case "seasonDay": d.CurrentSeasonDay += 1; break;
            case "spawnDay": d.CurrentDayForSpawning += 1; break;
        }
        float r = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref r);
        bool ok = r == 3f;
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        Check("drift [" + drift + "]: unchanged", ok);
    }
    // 提交前现读 currentTime（不用 prefix 快照）。
    float rc = RunTravel(1f, 3f, 0f, out _, out _, () => { });
    Check("no-drift control still compensates", rc == 5f);
    Director dN = null;
    float r21 = RunTravel(1f, 3f, 0f, out dN, out _, () => dN.currentTime = 21.5f);
    Check("fresh currentTime 21.5 -> spawn clamped to 21.5 (r=3.5)", r21 == 3.5f);
    float r22 = RunTravel(1f, 3f, 0f, out dN, out _, () => dN.currentTime = 22.5f);
    Check("fresh currentTime past S0: unchanged", r22 == 3f);
    float rBad = RunTravel(1f, 3f, 0f, out dN, out _, () => dN.currentTime = float.NaN);
    Check("fresh currentTime NaN: unchanged", rBad == 3f);
    float rOut = RunTravel(1f, 3f, 0f, out dN, out _, () => dN.currentTime = 25f);
    Check("fresh currentTime out of range: unchanged", rOut == 3f);
    // E/D 按 seasonDay 与 seasonDay+1 查询。
    var (dq, emq, wq) = Graph(0f, 3f);
    NightDepartureScope.State stq = default;
    PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(dq, wq, Side.Left, 1f, ref stq);
    PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stq);
    Check("profiles read for seasonDay and seasonDay+1 only",
        dq.TimesOfDayQueries.Count == 2 && dq.TimesOfDayQueries[0] == 40 && dq.TimesOfDayQueries[1] == 41);
}

// ============ 四、嵌套 / 消费 / 深度 / token 所有权 ============
{
    Console.WriteLine("-- nesting, consumption, depth, token ownership");
    // 不合格内层 mask：内层期间父授权不可见，弹栈后恢复。
    {
        var (d, em, w1) = Graph(0f, 3f);
        var w2 = new Wave { type = WaveType.BossWave };
        NightDepartureScope.State stA = default, stB = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w1, Side.Left, 1f, ref stA);
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w2, Side.Left, 1f, ref stB);
        float masked = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w1, 1.5f, 12, ref masked);
        bool maskedOk = masked == 3f && NightDepartureScope.TestDepth == 2;
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stB);
        float restored = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w1, 1.5f, 12, ref restored);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stA);
        Check("ineligible inner masks parent; restored after pop",
            maskedOk && restored == 5f && NightDepartureScope.TestDepth == 0);
    }
    // 内层 Regular 自带 arrival：各自补偿。
    {
        var (d, em, w1) = Graph(0f, 2f);
        var w2 = new Wave();
        NightDepartureScope.State stA = default, stB = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w1, Side.Left, 1f, ref stA);   // A=25,T2 -> +1 => 3
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w2, Side.Left, 3f, ref stB);   // A=27,T2 -> +1 => 3 (spawn 24)
        float rInner = 2f, rOuter = 2f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w2, 1.5f, 12, ref rInner);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stB);
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w1, 1.5f, 12, ref rOuter);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stA);
        Check("inner Regular has own arrival; parent restored",
            rInner == 3f && rOuter == 3f && NightLogCount() == 2 && NightDepartureScope.TestDepth == 0);
    }
    // 深度上限：第 16 层仍有效，第 17 层起整棵子树屏蔽；溢出退栈后恢复。
    {
        var (d, em, w) = Graph(0f, 3f);
        var states = new List<NightDepartureScope.State>();
        for (int i = 0; i < 16; i++)
        {
            NightDepartureScope.State s = default;
            PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref s);
            states.Add(s);
        }
        Check("16 frames pushed", NightDepartureScope.TestDepth == 16 && NightDepartureScope.TestOverflow == 0);
        NightDepartureScope.State st17 = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref st17);
        bool overflowPushed = NightDepartureScope.TestDepth == 16 && NightDepartureScope.TestOverflow == 1;
        float masked = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref masked);
        bool maskedOk = masked == 3f; // 溢出子树屏蔽，不暴露第 16 层授权
        NightDepartureScope.State staleOverflow = st17;
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st17);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref staleOverflow); // 复制旧 state 重清
        bool staleRejected = NightDepartureScope.TestOverflow == 0;
        float after = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref after);
        bool restored = after == 5f; // 溢出退栈后第 16 层恢复
        for (int i = states.Count - 1; i >= 0; i--) { var s = states[i]; PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref s); }
        Check("overflow masks subtree, restores after unwind, stale copy rejected",
            overflowPushed && maskedOk && staleRejected && restored
            && NightDepartureScope.TestDepth == 0 && NightDepartureScope.TestOverflow == 0);
    }
    // 恰好 16 层：最内层有效。
    {
        var (d, em, w) = Graph(0f, 3f);
        var states = new List<NightDepartureScope.State>();
        for (int i = 0; i < 16; i++)
        {
            NightDepartureScope.State s = default;
            PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref s);
            states.Add(s);
        }
        float r = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref r);
        for (int i = states.Count - 1; i >= 0; i--) { var s = states[i]; PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref s); }
        Check("exactly 16 frames: innermost still effective", r == 5f && NightDepartureScope.TestDepth == 0);
    }
    // 过期 frame token：复制旧 state 重清不能弹别的层。
    {
        var (d, em, w) = Graph(0f, 3f);
        NightDepartureScope.State stA = default, stB = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref stA);
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref stB);
        NightDepartureScope.State staleB = stB;
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stB);   // 正常弹 B
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref staleB); // 复制旧 state 重清
        bool survived = NightDepartureScope.TestDepth == 1;
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref stA);
        Check("stale frame token cannot pop another layer",
            survived && NightDepartureScope.TestDepth == 0);
    }
    // 重复 finalizer（同一 state）：只弹一次。
    {
        var (d, em, w) = Graph(0f, 3f);
        NightDepartureScope.State st = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref st);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        Check("finalizer idempotent", NightDepartureScope.TestDepth == 0);
    }
}

// ============ 五、异常安全 ============
{
    Console.WriteLine("-- exception safety");
    // prefix 读失败：frame disabled、原生继续、finalizer 正常弹栈。
    {
        var (d, em, w) = Graph(0f, 3f);
        d.ThrowOnDayRead = true;
        NightDepartureScope.State st = default;
        bool threw = false;
        try { PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref st); }
        catch { threw = true; }
        float r = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref r);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        int warns = KingdomEnhancedPlugin.Instance.LogSource.Warning.FindAll(s => s.Contains("scope read failed")).Count;
        Check("prefix read failure: no throw, unchanged, one warn, popped",
            !threw && r == 3f && NightDepartureScope.TestDepth == 0 && warns == 1);
    }
    // 原生体内异常：finalizer 原样交回同一异常且弹栈；后续调用不受污染。
    {
        var (d, em, w) = Graph(0f, 3f);
        d.ThrowInNativeBody = true;
        Exception caught = null;
        try { d.ScheduleWaveToday(w, Side.Left, 1f); } catch (Exception e) { caught = e; }
        bool clean = NightDepartureScope.TestDepth == 0;
        var (d2, em2, w2) = Graph(0f, 3f);
        float r = 3f;
        NightDepartureScope.State st = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d2, w2, Side.Left, 1f, ref st);
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em2, w2, 1.5f, 12, ref r);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        Check("native exception propagated unchanged; no state leak",
            caught is InvalidOperationException && caught.Message == "injected native body failure"
            && clean && r == 5f);
    }
    // postfix 自身异常：保持原生结果且不外抛；后续有效调用仍可补偿。
    {
        var (d, em, w) = Graph(0f, 3f);
        NightDepartureScope.State st = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref st);
        w.ThrowOnPointer = true;
        float r1 = 3f;
        bool threw = false;
        try { PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref r1); }
        catch { threw = true; }
        int warns = KingdomEnhancedPlugin.Instance.LogSource.Warning.FindAll(s => s.Contains("travel postfix failed")).Count;
        w.ThrowOnPointer = false;
        float r2 = 3f;
        PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref r2);
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        Check("postfix internal failure isolated", !threw && r1 == 3f && warns == 1 && r2 == 5f);
    }
    // 日志宿主缺失：补偿结果不受影响。
    {
        var (d, em, w) = Graph(0f, 3f);
        KingdomEnhancedPlugin.Instance = null;
        NightDepartureScope.State st = default;
        PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(d, w, Side.Left, 1f, ref st);
        float r = 3f;
        bool threw = false;
        try { PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(em, w, 1.5f, 12, ref r); }
        catch { threw = true; }
        PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(null, ref st);
        Check("log host missing does not affect committed result", !threw && r == 5f);
    }
}

// ============ 六、端到端（stub native 体 + 真实钩子编排；红模式=替身不调 postfix） ============
void EndToEnd()
{
    Console.WriteLine("-- end-to-end (compensated expectations; fail under --red)");
    {
        var (d, _, w) = Graph(0f, 0.5f);
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("near portal: native spawn kept (24.5), no log",
            d.LastSpawnTime == 24.5f && d.LastTravelTime == 0.5f && NightLogCount() == 0 && d.NativeCalls == 1);
    }
    {
        var (d, _, w) = Graph(0f, 3f);
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("far portal: spawn 22->20, takes=5 with margin",
            d.LastSpawnTime == 20f && d.LastTravelTime == 5f && NightLogCount() == 1);
    }
    {
        var (d, _, w) = Graph(0f, 6f);
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("evening clamp: spawn 19->18", d.LastSpawnTime == 18f && d.LastTravelTime == 7f);
    }
    {
        var (d, _, w) = Graph(0f, 8f);
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("native earlier: spawn 17 kept both modes",
            d.LastSpawnTime == 17f && d.LastTravelTime == 8f && NightLogCount() == 0);
    }
    {
        var (d, _, w) = Graph(21f, 3f);
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("late call: spawn 22->21", d.LastSpawnTime == 21f);
    }
    {
        var (d, _, w) = Graph(0f, 7f);
        d.ScheduleWaveToday(w, Side.Right, 12f);
        Check("arrival 12 -> A36: S0=29 -> 27", d.LastSpawnTime == 27f);
    }
    {
        var (d, em, w) = Graph(0f, 3f, WaveType.Recovery);
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("recovery early-out: no travel call, no compensation",
            d.LastTravelTime == 0f && em.Calls == 0 && NightLogCount() == 0 && NightDepartureScope.TestDepth == 0);
    }
    {
        var (d, em, w) = Graph(0f, 3f);
        d.NoSpawnerPresent = true;
        d.ScheduleWaveToday(w, Side.Left, 1f);
        Check("no spawner early-out: untouched",
            d.LastTravelTime == 0f && em.Calls == 0 && NightLogCount() == 0);
    }
    {
        var (d, _, w) = Graph(0f, 2.25f);
        d.ProfileResolver = day => day <= 40
            ? new TimesOfDay { eveningStart = 18f, dawnStart = 6f }
            : new TimesOfDay { eveningStart = 18f, dawnStart = 5f };
        d.ScheduleWaveToday(w, Side.Left, 7.5f); // A=31.5, S0=29.25 >= H=29 -> 原样
        Check("next-day dawn used (H=29): S0=29.25 kept",
            d.LastSpawnTime == 29.25f && NightLogCount() == 0 && w.type == WaveType.Regular);
    }
    {
        var (d, _, w) = Graph(0f, 3f);
        d.ProfileResolver = day => day <= 40
            ? new TimesOfDay { eveningStart = 18f, dawnStart = 6f }
            : new TimesOfDay { eveningStart = 18f, dawnStart = 5f };
        d.ScheduleWaveToday(w, Side.Left, 7.5f); // A=31.5, S0=28.5 -> 26.5
        Check("next-day dawn used: S0=28.5 -> 26.5", d.LastSpawnTime == 26.5f);
    }
}

// ============ 七、日志字段 ============
{
    Console.WriteLine("-- log fields");
    var (d, _, w) = Graph(0f, 3f);
    d.ScheduleWaveToday(w, Side.Right, 1f);
    var line = KingdomEnhancedPlugin.Instance.LogSource.Info.Find(s => s.Contains("[NightDeparture]"));
    Check("summary contains day/side/nativeTravel/extra/spawn",
        line != null && line.Contains("day=10") && line.Contains("side=Right")
        && line.Contains("nativeTravel=3.00") && line.Contains("extra=2.00")
        && line.Contains("spawn=22.00->20.00"));
    Check("summary explains takes margin", line != null && line.Contains("takes now includes the margin"));
}

// —— 主流程：绿 = 全部通过退出 0；红 = 替身不调真实 postfix，预期非零退出 ——
bool redMode = args.Length > 0 && Array.IndexOf(args, "--red") >= 0;
if (redMode) EnemyManager.SuppressTravelPostfixCall = true;
EndToEnd();

if (redMode)
    Console.WriteLine(failed == 0
        ? "RED-FAILED: assertions passed despite suppression (tests have no teeth)"
        : "RED run: " + failed + " failures as expected while the real postfix was not called (non-zero exit is the teeth proof)");
Console.WriteLine((redMode ? "red" : "green") + " summary: passed=" + passed + " failed=" + failed);
Console.Out.Flush();
Environment.Exit(failed == 0 ? 0 : 1);
