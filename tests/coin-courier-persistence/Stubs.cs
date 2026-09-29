// Stubs.cs — 金币哥布林持久化套件的“原生边界/游戏表面”模型。
//
// 这里只模拟生产文件（CoinCourierPersistence / CoinCourierSaveData / CoinCourierPurse /
// CoinCourierCampaignState）实际使用的原生可观察行为：
// - Harmony 在离线套件里不运行，所以每个“原生存根”方法在模仿真实方法体的同时，调用
//   CoinCourierPersistence 的同一批 internal 边界入口（与 [HarmonyPrefix/Postfix] 一一对应）。
//   调用顺序按实际 2.4 反汇编：IslandSaveData.Save 先置 isSavingGame/CurrentlySavingIsland、
//   正常路径才调 UpdateSavedWithRevisions（内部 catch 路径不调）；两个 Global 保存路径都先
//   PrepareBeforeSave 再“序列化”。
// - Il2Cpp 对象/集合按 BepInEx 6 interop 的形状建模：Il2CppObjectBase.Pointer、TryCast<T>()、
//   Il2CppSystem 集合的 Count/索引器/ContainsKey。
// - 绝不在这里复制被测算法（scope 匹配、staged 重写、损坏判定都在生产文件里）。
using System;
using System.Collections;
using System.Collections.Generic;
using CoinCourierPersistenceType = KingdomEnhancedMod.CoinCourierPersistence;
using KingdomEnhancedMod;

namespace Il2CppInterop.Runtime.InteropTypes
{
    /// <summary>BepInEx 6 interop 对象基类的最小形状（Pointer + TryCast）。</summary>
    public class Il2CppObjectBase
    {
        private static long _next;
        public readonly IntPtr Pointer;

        public Il2CppObjectBase()
        {
            Pointer = (IntPtr)(0x20000000L + _next++ * 0x40L);
        }

        public T TryCast<T>() where T : Il2CppObjectBase => this as T;
    }
}

namespace Il2CppSystem
{
    public delegate void Action<in T>(T obj);
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : IEnumerable<T>
    {
        private readonly System.Collections.Generic.List<T> _items = new System.Collections.Generic.List<T>();

        public int Count => _items.Count;
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public void Add(T item) => _items.Add(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
        public void Clear() => _items.Clear();
        public bool Contains(T item) => _items.Contains(item);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    public class Dictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly System.Collections.Generic.Dictionary<TKey, TValue> _items = new System.Collections.Generic.Dictionary<TKey, TValue>();

        public int Count => _items.Count;
        public TValue this[TKey key] { get => _items[key]; set => _items[key] = value; }
        public bool ContainsKey(TKey key) => _items.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value);
        public void Add(TKey key, TValue value) => _items.Add(key, value);
        public bool Remove(TKey key) => _items.Remove(key);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        // Stored so the regression can pin the exact native targets (and prove the factory is absent).
        public Type TargetType;
        public string TargetMethod;
        public Type[] ArgumentTypes;

        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { TargetType = type; }
        public HarmonyPatch(Type type, string method) { TargetType = type; TargetMethod = method; }
        public HarmonyPatch(Type type, string method, Type[] argumentTypes)
        {
            TargetType = type; TargetMethod = method; ArgumentTypes = argumentTypes;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

namespace Coatsink.Common
{
    public enum SaveLoadResult
    {
        Success = 64,
        Failure = 128,
        Cancelled = 32,
        Delete = 16,
    }

    public static class Routine
    {
        public struct Return<T> { }
    }

    /// <summary>interop 把原生接口生成成派生自 Il2CppObjectBase 的类（同实际 2.4）。</summary>
    public class IHagletCallable : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public virtual Haglet.State state { get; set; }
    }

    public class Haglet : IHagletCallable
    {
        public enum State
        {
            Stopped = 0,
            Started = 1,
            Paused = 2,
            Completed = 3,
        }
    }

    /// <summary>故意不是 IHagletCallable：覆盖 TryCast 失败 → 明确不就绪。</summary>
    public class ForeignCallable : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }
}

namespace UnityEngine
{
    public static class Time
    {
        public static float timeScale = 1f;
    }
}

// ---------------------------------------------------------------------------
// 游戏表面（global namespace，与 Assembly-CSharp interop 一致）。
// ---------------------------------------------------------------------------
public class Game : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public enum State
    {
        Loading = 0,
        Intro = 1,
        Playing = 2,
        Menu = 4,
        SailingAway = 32,
    }

    public static bool SavingEnabled = true;
    public State state = State.Playing;
    /// <summary>现场岛（真实 2.4 Game.get_currentLand 读 Game+0x78；离岛保存期间仍是出发岛）。</summary>
    public int currentLand = 1;
    public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase _saveGameWithFailurePromptRoutine = new Coatsink.Common.Haglet();
    public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase saveGameWithFailurePrompt = new Coatsink.Common.Haglet();
}

public class World : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public Transform gameLayer;
}

public class Transform : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase { }

public class Managers : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public static Managers Inst;
    public Game game;
    public World world;
}

public class CampaignSaveData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public static CampaignSaveData current;
    public int challengeId;
    public int CurrentLand;
    private readonly Dictionary<int, IslandSaveData> _islands = new Dictionary<int, IslandSaveData>();

    public IslandSaveData GetIsland(int land)
    {
        if (!_islands.TryGetValue(land, out IslandSaveData island))
        {
            island = new IslandSaveData { land = land };
            _islands[land] = island;
        }
        return island;
    }

    public void ApplyToScene()
    {
        current = this;
        // 与 MusketeerPersistence.VirginPatch 末尾的调用同点（离线不跑 Harmony）。
        CoinCourierPersistenceType.EnsureBoundFromApplyToScene(this);
    }
}

public class IslandSaveData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public static bool isSavingGame;
    public static IslandSaveData CurrentlySavingIsland;
    public int land;

    /// <summary>
    /// 模型：正常路径对象枚举成功后调用 UpdateSavedWithRevisions；原生内部 catch 的
    /// 失败路径不经过它（方法仍然正常返回——所以生产侧不能把“正常返回”当捕获成功）。
    /// </summary>
    public static void Save(int campaign, int land, int challengeId)
    {
        KingdomEnhancedMod.CoinCourierPersistence.SaveScope scope =
            CoinCourierPersistenceType.BeginIslandSave(campaign, land, challengeId);
        isSavingGame = true;
        try
        {
            GlobalSaveData global = GlobalSaveData._loaded;
            CampaignSaveData campaignData = null;
            if (global != null && campaign >= 0 && campaign < global.campaigns.Count)
                campaignData = global.campaigns[campaign];
            int effectiveLand = land >= 0 ? land : (campaignData != null ? campaignData.CurrentLand : -1);
            IslandSaveData island = campaignData != null ? campaignData.GetIsland(effectiveLand) : null;
            CurrentlySavingIsland = island;
            // 场景换代/现场 land 变化等注入点：后于 CurrentlySavingIsland、先于正常 marker。
            Harness.DuringSave?.Invoke();
            if (island != null && !Harness.NextSaveEnumerationFails)
                island.UpdateSavedWithRevisions();
        }
        finally
        {
            CurrentlySavingIsland = null;
            isSavingGame = false;
            CoinCourierPersistenceType.EndIslandSave(scope, true);
        }
    }

    public void UpdateSavedWithRevisions()
    {
        CoinCourierPersistenceType.ObserveMarker(this);
    }
}

public class GlobalSaveData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public static GlobalSaveData _loaded;
    public static GlobalSaveData loaded => _loaded;

    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns = new Il2CppSystem.Collections.Generic.List<CampaignSaveData>();
    public Il2CppSystem.Collections.Generic.List<CampaignSaveData> challenges = new Il2CppSystem.Collections.Generic.List<CampaignSaveData>();
    public int currentCampaign;
    public int currentChallenge;
    public PrefsSaveData prefs = new PrefsSaveData();

    public CampaignSaveData CreateNewCampaign(int campaignIndex, int challengeId)
    {
        CoinCourierPersistenceType.ObserveCampaignMutation(this);
        CampaignSaveData result = NativeCreate(campaignIndex, challengeId);
        CoinCourierPersistenceType.ObserveCampaignCreated(this, result);
        return result;
    }

    private CampaignSaveData NativeCreate(int campaignIndex, int challengeId)
    {
        // 实际 2.4：challenge 分支单列；普通分支“扩容或同槽替换新对象，返回 campaigns[index]”。
        if (campaignIndex < campaigns.Count)
        {
            var replaced = new CampaignSaveData();
            campaigns[campaignIndex] = replaced;
            return replaced;
        }
        while (campaigns.Count <= campaignIndex) campaigns.Add(new CampaignSaveData());
        return campaigns[campaignIndex];
    }

    public void TryDeleteCampaignAsync(int campaignIndex, Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback)
    {
        CoinCourierPersistenceType.ObserveCampaignMutation(this);
        NativeDelete(campaignIndex);
        IslandSaveData.Save(currentCampaign, CampaignSaveData.current != null ? CampaignSaveData.current.CurrentLand : -1, currentChallenge);
        SaveAsync(callback);
    }

    /// <summary>
    /// 实际 2.4 协程状态机（GlobalSaveData/__TryDeleteCampaign_d__91）：state0 首次执行时
    /// RemoveAt → SaveAsync → Wait；这里只建模可观察链，Harmony detour 用生产同一入口记账。
    /// </summary>
    public class __TryDeleteCampaign_d__91 : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        private int _state;
        /// <summary>Test hook: state 读取故障（覆盖 prefix 的异常隔离路径）。</summary>
        public bool ThrowOnStateRead;
        public int __1__state
        {
            get => ThrowOnStateRead ? throw new InvalidOperationException("state read fault") : _state;
            set => _state = value;
        }
        public int campaignIndex;
        public GlobalSaveData Owner;

        public bool MoveNext()
        {
            if (__1__state != 0) return false; // state1 仅收尾，不再触碰 list/save
            CoinCourierPersistenceType.ObserveDeleteRoutineState(this);
            Owner.NativeDelete(campaignIndex);
            Owner.SaveAsync(null);
            __1__state = 1;
            return true;
        }
    }

    public IEnumerator<Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>> _TryDeleteCampaign(
        int campaignIndex, Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult> @return)
    {
        // 原生 factory 只构造状态机（不读 Return<T>）；记账发生在 state==0 的 MoveNext。
        var machine = new __TryDeleteCampaign_d__91 { Owner = this, campaignIndex = campaignIndex };
        if (machine.MoveNext())
            yield return default(Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult>);
    }

    private void NativeDelete(int campaignIndex)
    {
        if (campaignIndex < 0 || campaignIndex >= campaigns.Count) return;
        campaigns.RemoveAt(campaignIndex);
        if (campaignIndex < currentCampaign) currentCampaign--;
    }

    /// <summary>异步保存路径：先 PrepareBeforeSave（原生 0x73758b）再序列化。</summary>
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback)
    {
        prefs.PrepareBeforeSave();
        Harness.LastPayload = prefs.SnapshotPayload();
        callback?.Invoke(Coatsink.Common.SaveLoadResult.Success);
    }

    /// <summary>协程保存路径也先 PrepareBeforeSave（原生 0x748dcf）。</summary>
    public void SaveRoutine()
    {
        prefs.PrepareBeforeSave();
        Harness.LastPayload = prefs.SnapshotPayload();
    }
}

public class PrefsSaveData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
{
    public Il2CppSystem.Collections.Generic.Dictionary<string, string> contents = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
    public Il2CppSystem.Collections.Generic.List<SrzEntry> srzEntries = new Il2CppSystem.Collections.Generic.List<SrzEntry>();
    public bool ThrowOnSet;
    public int SetCalls;

    public struct SrzEntry
    {
        public string key;
        public string val;
    }

    public void SetString(string key, string val)
    {
        SetCalls++;
        if (ThrowOnSet) throw new InvalidOperationException("prefs set fault");
        contents[key] = val;
    }

    public string GetString(string key, string defaultVal)
        => contents.TryGetValue(key, out string value) ? value : defaultVal;

    public void PrepareBeforeSave()
    {
        CoinCourierPersistenceType.ObservePrepare(this);
        srzEntries.Clear();
        foreach (KeyValuePair<string, string> pair in contents)
            srzEntries.Add(new SrzEntry { key = pair.Key, val = pair.Value });
    }

    public string SnapshotPayload()
    {
        foreach (SrzEntry entry in srzEntries)
            if (entry.key == KingdomEnhancedMod.CoinCourierSaveSchema.Key) return entry.val;
        return null;
    }
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline = false;
}

// ---------------------------------------------------------------------------
// Mod 侧表面（global namespace，同 interop 边界）。
// ---------------------------------------------------------------------------
public class ManualLogSource
{
    public readonly List<string> Infos = new List<string>();
    public readonly List<string> Warnings = new List<string>();
    public readonly List<string> Errors = new List<string>();

    public void LogInfo(string message) => Infos.Add(message);
    public void LogWarning(string message) => Warnings.Add(message);
    public void LogError(object message) => Errors.Add(Convert.ToString(message));
}

public class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
    public ManualLogSource LogSource = new ManualLogSource();
}

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 仅离线夹具：Save prefix 的取消入口。生产实现（含节点/退款守卫）在 CoinCourierShop.cs，
    /// 依赖整个运行时，不进本套件；这里只记录调用与相对 scope 的顺序。
    /// </summary>
    internal static class CoinCourierShop
    {
        internal static int CancelCalls;
        internal static bool ThrowOnCancel;
        internal static bool ScopeWasNullAtCancel;

        internal static void CancelPendingTransactions()
        {
            CancelCalls++;
            ScopeWasNullAtCancel = Harness.ScopeIsNull;
            if (ThrowOnCancel) throw new InvalidOperationException("shop cancel fault");
        }

        internal static void Reset()
        {
            CancelCalls = 0;
            ThrowOnCancel = false;
            ScopeWasNullAtCancel = false;
        }
    }

    /// <summary>
    /// 只有 Bind/Unbind/State 三个真实 API 的替身：CoinCourierRuntime.cs 依赖 Unity/Il2Cpp
    /// 全家桶，不进离线套件；其绑定语义（引用切换、旧世代解锁）不是本套件被测对象。
    /// </summary>
    internal static class CoinCourierRuntime
    {
        internal static ICoinCourierCampaignState State;
        internal static int BindCalls;
        internal static int UnbindCalls;

        internal static void Bind(ICoinCourierCampaignState state)
        {
            BindCalls++;
            State = state;
        }

        internal static void Unbind(ICoinCourierCampaignState state)
        {
            UnbindCalls++;
            if (ReferenceEquals(State, state)) State = null;
        }

        internal static void Reset()
        {
            State = null;
            BindCalls = 0;
            UnbindCalls = 0;
        }
    }
}
