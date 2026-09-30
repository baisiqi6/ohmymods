// 夜袭出发补偿回归测试的边界替身（Unity/Harmony/游戏类型）。
// 形状以任务签名证据为准：actual-interop.json（WaveType 值、两入口签名）、
// actual-dayphase-api.json（GetTimesOfDayForDay/dawnStart/eveningStart public、Side ±1）、
// native-disassembly.txt（ScheduleWaveToday 的 arrival>12?arrival:arrival+24、
// Recovery 早退、GetWaveTravelTime 每次排期恰好调用一次、day 实参未被原生使用）。
// Director/EnemyManager 替身按 Harmony 顺序手动编排真实生产钩子（prefix → native 体 →
// finalizer / postfix）；这里的 A 归一是对 native 反汇编的独立复写，不复用生产实现。
// 红对照开关只存在于替身：SuppressTravelPostfixCall=true 时替身不调用真实 postfix，
// 生产代码没有任何测试旁路。
using System.Collections.Generic;

namespace HarmonyLib
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
    public class HarmonyPatch : System.Attribute
    {
        public HarmonyPatch(System.Type type) { }
        public HarmonyPatch(System.Type type, string method) { }
    }
    public class HarmonyPrefix : System.Attribute { }
    public class HarmonyPostfix : System.Attribute { }
    public class HarmonyFinalizer : System.Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        static int next;
        IntPtr _pointer = (IntPtr)System.Threading.Interlocked.Increment(ref next);
        bool _throwOnPointer;
        public bool ThrowOnPointer { get { return _throwOnPointer; } set { _throwOnPointer = value; } }
        public IntPtr Pointer
        {
            get { if (_throwOnPointer) throw new System.InvalidOperationException("injected pointer read"); return _pointer; }
            set { _pointer = value; }
        }
    }
    public struct Scene { public int handle; }
    public class GameObject : Object { public Scene scene = new Scene { handle = 10 }; }
    public class Component : Object { public GameObject gameObject = new GameObject(); }
    public class Transform : Component { }
}

public enum Side { Left = -1, Right = 1 }

public enum WaveType
{
    None = 0, Regular = 1, BossWave = 2, Recovery = 3, Retaliation = 4,
    PortalDefence = 5, EventWave = 6, PortalProximity = 7,
}

public class Wave : UnityEngine.Object { public WaveType type = WaveType.Regular; }

public class TimesOfDay { public float eveningStart = 18f; public float dawnStart = 6f; }

public class World : UnityEngine.Object { public UnityEngine.Transform gameLayer = new UnityEngine.Transform(); }

public static class NetworkBigBoss { public static bool HasWorldAuth = true; }

public class EnemyManager : UnityEngine.Component
{
    // 红对照开关（仅测试替身）：true 时替身不调用真实 postfix——原生路径原样返回。
    public static bool SuppressTravelPostfixCall;
    public float ConfiguredTravel = 3f; // 本场景的“原生”行程估计（测试直接设定）
    public int Calls;
    public float GetWaveTravelTime(Wave wave, float portalX, int day)
    {
        Calls++;
        float result = ConfiguredTravel;
        if (!SuppressTravelPostfixCall)
            KingdomEnhancedMod.PatchWorld_NightDepartureTravel.GetWaveTravelTime_Postfix(
                this, wave, portalX, day, ref result);
        return result;
    }
}

public class Director : UnityEngine.Component
{
    public float currentTime;
    private int _schedulerDay = 10, _seasonDay = 40, _spawnDay = 12;
    public bool ThrowOnDayRead;
    public int CurrentSchedulerDay
    {
        get { if (ThrowOnDayRead) throw new System.InvalidOperationException("injected day read"); return _schedulerDay; }
        set { _schedulerDay = value; }
    }
    public int CurrentSeasonDay
    {
        get { if (ThrowOnDayRead) throw new System.InvalidOperationException("injected day read"); return _seasonDay; }
        set { _seasonDay = value; }
    }
    public int CurrentDayForSpawning
    {
        get { if (ThrowOnDayRead) throw new System.InvalidOperationException("injected day read"); return _spawnDay; }
        set { _spawnDay = value; }
    }
    public readonly List<int> TimesOfDayQueries = new();
    public System.Func<int, TimesOfDay> ProfileResolver = _ => new TimesOfDay { eveningStart = 18f, dawnStart = 6f };
    public TimesOfDay GetTimesOfDayForDay(int day) { TimesOfDayQueries.Add(day); return ProfileResolver(day); }

    // native 体的替身（RVA 0x4e57a0 语义镜像）：
    public bool NoSpawnerPresent;      // 原生“无 Spawner”早退
    public bool ThrowInNativeBody;     // 注入原生体内异常（测异常传播/finalizer）
    public float LastTravelTime;       // 原生拿到的 takes（= 补偿后的 T'）
    public float LastSpawnTime;        // 原生写入的出发时刻（A - takes）
    public int NativeCalls;

    public void ScheduleWaveToday(Wave wave, Side side, float waveArrivalTime)
    {
        NativeCalls++;
        KingdomEnhancedMod.NightDepartureScope.State state = default;
        KingdomEnhancedMod.PatchWorld_NightDeparture.ScheduleWaveToday_Prefix(
            this, wave, side, waveArrivalTime, ref state);
        System.Exception native = null;
        try
        {
            if (wave.type != WaveType.Recovery && !NoSpawnerPresent)
            {
                float travel = Managers.Inst.enemies.GetWaveTravelTime(
                    wave, 1.5f, CurrentDayForSpawning);
                // 独立镜像 native 的归一与出发计算（comiss 12f / addss 24f / sub）。
                float a = waveArrivalTime > 12f ? waveArrivalTime : waveArrivalTime + 24f;
                LastTravelTime = travel;
                LastSpawnTime = a - travel;
            }
            if (ThrowInNativeBody) throw new System.InvalidOperationException("injected native body failure");
        }
        catch (System.Exception e) { native = e; }
        System.Exception returned = KingdomEnhancedMod.PatchWorld_NightDeparture.ScheduleWaveToday_Finalizer(
            native, ref state);
        if (returned != null) throw returned;
    }
}

public class Managers : UnityEngine.Object
{
    public static Managers Inst { get; set; } = new Managers();
    public Director director = new Director();
    public World world = new World();
    public EnemyManager enemies = new EnemyManager();
}

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public class Flag { public bool Value = true; }
        public static Flag Enabled = new Flag();
    }
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public Log LogSource = new Log();
        public class Log
        {
            public readonly List<string> Info = new(), Warning = new();
            public void LogInfo(string text) => Info.Add(text);
            public void LogWarning(string text) => Warning.Add(text);
            public void Clear() { Info.Clear(); Warning.Clear(); }
        }
    }
}
