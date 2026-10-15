using System;
using System.Collections.Generic;

// Platform stubs for the Issue #223 compensation-runtime leaf suite. The leaf is compiled from
// the real production file; these types replace Unity/game/BepInEx and the sibling core-stage
// types it calls through (Identity/Persistence/NativeSave...). The stubs are deliberately a
// SUBSET: IslandSaveData here has no GetID/objects/ObjectData/prefabPath members and
// UnityEngine.Time has no timeScale, so any leaf attempt to call GetID by hand, fabricate rows,
// pause global time or script input state would fail this suite's compilation.
namespace BepInEx
{
    public static class Paths { public static string ConfigPath; }
}

namespace Coatsink.Common
{
    public enum SaveLoadResult
    {
        Save = 8,
        Success = 64,
        Failure = 128,
        Busy = 256,
        Cancelled = 8192,
        NoChange = Save | Success
    }
}

namespace Il2CppSystem
{
    public class Nullable<T>
    {
        public bool HasValue => false;
    }

    // il2cpp delegate shell: the production leaf converts a managed delegate with an explicit
    // cast (same shape as the V4 native fixture) and must keep both roots until terminal.
    public class Action<T>
    {
        public readonly System.Action<T> Managed;
        public Action(System.Action<T> managed) { Managed = managed; }
        public static implicit operator Action<T>(System.Action<T> managed) => new Action<T>(managed);
        public void Invoke(T value) => Managed(value);
    }
}

namespace UnityEngine
{
    public class Object { static long counter; public IntPtr Pointer = (IntPtr)(++counter); }

    public class GameObject : Object
    {
        static int nextId;
        public int InstanceId = ++nextId;
        public int GetInstanceID() => InstanceId;
        private bool active = true;
        public bool activeInHierarchy { get => active; set => active = value; }
        public string tag = "";
        public Transform transform;
        readonly Dictionary<Type, Component> components = new();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T Add<T>(T component) where T : Component { component.gameObject = this; components[typeof(T)] = component; return component; }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var found) ? (T)found : null;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public class Transform : Component
    {
        public Vector3 position;
        // The suite decides whether a transform belongs to the current world layer.
        public bool InLayer = true;
        public bool IsChildOf(Transform layer) => layer != null && (InLayer || ReferenceEquals(layer, this));
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public override string ToString() => x + "," + y + "," + z;
    }

    public struct Quaternion
    {
        public static Quaternion identity => new Quaternion();
    }

    public static class Time
    {
        public static float unscaledDeltaTime = 1f / 60f;
    }
}

public class Character : UnityEngine.Component
{
    // Scripted identity life used by the Identity stub; 0 means the root life is unknown.
    public long ScriptedLife;
}

public class Archer : UnityEngine.Component { }

public class Persistent : UnityEngine.Component
{
    public string path;
    public bool ShouldPersistResult = true;
    public bool ShouldPersist() => ShouldPersistResult;
}

public class Castle : UnityEngine.Component { }
public class Kingdom { public Castle castle; }
public class World { public UnityEngine.Transform gameLayer; }
public class Holder { public Character archerPrefab; public Character GetCharacterByTag(string tag) => tag == "Archer" ? archerPrefab : null; }
public class Managers { public static Managers Inst; public World world; public Kingdom kingdom; public Holder holder; }

public class Pool
{
    public UnityEngine.GameObject prefab;
    internal static readonly Dictionary<IntPtr, Pool> Registered = new();
    internal static Func<Character, UnityEngine.Vector3, UnityEngine.Quaternion, UnityEngine.Transform, bool, int, Character> SpawnHook;
    internal static readonly List<(Character Prefab, UnityEngine.Vector3 Position, UnityEngine.Quaternion Rotation, UnityEngine.Transform Parent, bool Activate)> SpawnCalls = new();
    internal static readonly List<UnityEngine.GameObject> Despawns = new();
    internal static Func<UnityEngine.GameObject, bool> DespawnHook = _ => true;   // true = native recycled it

    public static Pool GetPoolFromPrefabAsset(UnityEngine.GameObject prefab)
        => prefab != null && Registered.TryGetValue(prefab.Pointer, out var pool) ? pool : null;

    public static T Spawn<T>(T prefab, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation,
        UnityEngine.Transform parent, bool activate) where T : UnityEngine.Component
    {
        SpawnCalls.Add(((Character)(object)prefab, position, rotation, parent, activate));
        int index = SpawnCalls.Count - 1;
        if (SpawnHook == null) return null;
        return (T)(object)SpawnHook((Character)(object)prefab, position, rotation, parent, activate, index);
    }

    public static void Despawn(UnityEngine.GameObject root, bool sync)
    {
        Despawns.Add(root);
        if (DespawnHook(root) && root != null) root.activeInHierarchy = false;
    }
}

// No GetID / objects / ObjectData / PrefabData surface exists here on purpose: the leaf may only
// reach the scene capture through SerializeObjects.
public class IslandSaveData
{
    public static bool isSavingGame;
    public static int SerializeCalls;
    public static Action<Il2CppSystem.Nullable<int>> SerializeHook;
    public static void SerializeObjects(Il2CppSystem.Nullable<int> land)
    {
        SerializeCalls++;
        if (SerializeHook != null) SerializeHook(land);
    }
}

public class PrefsSaveData : UnityEngine.Object { }

public class GlobalSaveData : UnityEngine.Object
{
    public static GlobalSaveData loaded;
    public static string filename = "global-v35";
    // Minimal typed platform fields: the real owner identity is the (Global pointer, prefs pointer,
    // filename) tuple, so the suite can swap one leg at a time.
    public PrefsSaveData prefs = new PrefsSaveData();
    public static int SaveAsyncCalls;
    public static Action<Il2CppSystem.Action<Coatsink.Common.SaveLoadResult>> SaveAsyncHook;
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback)
    {
        SaveAsyncCalls++;
        if (SaveAsyncHook != null) SaveAsyncHook(callback);
    }
}

namespace KingdomEnhancedMod
{
    internal static class MusketeerAccess
    {
        internal static bool EnabledFlag = true, PlayingFlag = true, TrackAllowedFlag = true;
        internal static Func<UnityEngine.GameObject, bool> InWorldHook = root => root != null && root.activeInHierarchy;
        internal static bool Enabled => EnabledFlag;
        internal static bool Playing => EnabledFlag && PlayingFlag;
        internal static bool TrackAllowed => TrackAllowedFlag;
        internal static bool InWorld(UnityEngine.GameObject root)
        {
            try { return TrackAllowedFlag && InWorldHook(root); } catch { return false; }
        }
    }

    // Core-stage stand-in. It replays only the documented contract of the real seams: registration
    // is refused unless OwnsBirth already accepts the ticket, and every call is recorded so the
    // suite can assert the leaf's ordering (ticket before register) and cleanup discipline.
    internal static class MusketeerIdentity
    {
        internal sealed class IslandState
        {
            internal string ContextKey;
            internal string Epoch;
            internal string MatchKind = "none";
            internal long World;
            internal bool Ready;
            internal bool ReadOnly;
            internal bool Unresolved;
            internal bool HasBaseline;
            internal bool CompensationPending;
            internal Guid CompensationRequest;
            internal bool GenerationPending;
        }

        internal static IslandState CurrentState;
        internal static IslandState Current => CurrentState;
        internal static long WorldKeyValue = 777;
        internal static long WorldKey() => WorldKeyValue;

        internal static readonly List<string> Logs = new();
        internal static void Log(string key, Exception error)
        {
            if (key == null) return;
            Logs.Add(error == null ? key : key + ": " + error.GetType().Name);
        }
        internal static bool Logged(string key) => Logs.Exists(x => x == key || x.StartsWith(key + ":", StringComparison.Ordinal));
        internal static int LogCount(string key)
        {
            int count = 0;
            foreach (string line in Logs) if (line == key || line.StartsWith(key + ":", StringComparison.Ordinal)) count++;
            return count;
        }

        internal static long LegacyBirthLife(Character character) => character != null ? character.ScriptedLife : 0;

        internal static readonly List<(IslandState State, Character Character, Guid Id, Guid Request, bool Owned)> RegisterCalls = new();
        internal static Func<int, bool> RegisterVerdict = _ => true;
        internal static bool RegisterLegacyArcher(IslandState state, Character character, Guid id, Guid request, out string reason)
        {
            bool owned = MusketeerLegacyCompensationRuntime.OwnsBirth(character, id, request);
            RegisterCalls.Add((state, character, id, request, owned));
            if (!owned) { reason = "stub-not-owned"; return false; }
            if (!RegisterVerdict(RegisterCalls.Count - 1)) { reason = "stub-verdict"; return false; }
            reason = null;
            return true;
        }

        internal static readonly List<(Character Character, Guid Id, bool Owned)> ForgetCalls = new();
        internal static bool ForgetLegacyBirth(IslandState state, Character character, Guid id, Guid request)
        {
            bool owned = MusketeerLegacyCompensationRuntime.OwnsBirth(character, id, request);
            ForgetCalls.Add((character, id, owned));
            return owned;
        }

        internal static readonly List<IslandState> Released = new();
        internal static void ReleaseAll(IslandState state) => Released.Add(state);
        internal static readonly List<(string Key, IslandState State)> Installed = new();
        internal static void InstallState(string key, IslandState state) { Installed.Add((key, state)); CurrentState = state; }
    }

    internal static class MusketeerPersistence
    {
        internal static int PrepareCalls;
        internal static MusketeerIdentity.IslandState OldArg;
        internal static string JsonArg;
        internal static MusketeerLegacyCompensationPlan PlanArg;
        internal static List<Guid> GrantsArg = new();
        internal static MusketeerIdentity.IslandState LastPrepared;
        internal static MusketeerLegacyCompensationReceipt LastPending;
        internal static string LastEpoch;
        internal static bool PrepareVerdict = true;

        // Replays the Operator seam: a fresh opaque migration epoch, a pending receipt for the exact
        // grants, and the prepared state installed as current. No receipt bytes are persisted here.
        internal static bool PrepareLegacyCompensation(MusketeerIdentity.IslandState old, string rawJson,
            MusketeerLegacyCompensationPlan plan, IReadOnlyList<Guid> grants,
            out MusketeerIdentity.IslandState prepared, out MusketeerLegacyCompensationReceipt pending, out string reason)
        {
            PrepareCalls++;
            OldArg = old; JsonArg = rawJson; PlanArg = plan; GrantsArg = new List<Guid>(grants);
            prepared = null; pending = null; reason = "stub-prepare";
            if (!PrepareVerdict) return false;
            string epoch = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            if (!MusketeerLegacyCompensationPolicy.TryCreatePending(plan, epoch, grants, out var receipt, out reason)) return false;
            var next = new MusketeerIdentity.IslandState
            {
                ContextKey = old.ContextKey,
                Epoch = epoch,
                MatchKind = "legacy-compensation",
                World = old.World,
                Ready = true,
                HasBaseline = true,
                CompensationPending = true,
                CompensationRequest = plan.RequestId
            };
            MusketeerIdentity.InstallState(old.ContextKey, next);
            prepared = next; pending = receipt; LastPrepared = next; LastPending = receipt; LastEpoch = epoch;
            return true;
        }
    }

    internal static class MusketeerNativeSave
    {
        internal static int CancelCalls;
        internal static bool CancelVerdict = true;
        internal static bool AcceptedSet;
        internal static Guid AcceptedRequest;
        internal static string AcceptedContext, AcceptedEpoch;

        internal static bool LegacyCompensationAccepted(Guid request, string context, string epoch)
            => AcceptedSet && request == AcceptedRequest && context == AcceptedContext && epoch == AcceptedEpoch;

        internal static bool CancelLegacyBeforeCapture(Guid request, string context, string epoch, out string reason)
        {
            CancelCalls++;
            reason = CancelVerdict ? null : "stub-cancel";
            return CancelVerdict;
        }
    }

    internal static class PatchRoles_Castle
    {
        internal static int EnsurePoolCalls;
        internal static string LastTag;
        internal static void EnsurePoolForCharacter(string tag) { EnsurePoolCalls++; LastTag = tag; }
    }

    internal static class CoinCourierGround
    {
        internal static int ProbeCalls;
        internal static readonly List<float> Probes = new();
        internal static float ProbeY = 0.875f;
        internal static bool ProbeOk = true;
        internal static bool TryProbe(float x, out float groundY)
        {
            ProbeCalls++; Probes.Add(x);
            groundY = ProbeOk ? ProbeY : 0f;
            return ProbeOk;
        }
    }

    internal static class HeroRecruitment
    {
        internal static bool HasPurchasedCareer(Character character) => false;
    }
}
