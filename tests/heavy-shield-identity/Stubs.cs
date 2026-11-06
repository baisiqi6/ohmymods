using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject;
        public IntPtr Pointer => gameObject?.Pointer ?? IntPtr.Zero;
        public T GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();
        public bool CompareTag(string tag) => gameObject != null && gameObject.Tag == tag;
    }
    public class GameObject
    {
        private readonly Dictionary<Type, Component> _components = new();
        public IntPtr Pointer;
        public int Id;
        public string name;
        public string Tag;
        public bool activeInHierarchy = true;
        public GameObject(long pointer, int id, string name = "")
        { Pointer = new IntPtr(pointer); Id = id; this.name = name; }
        public int GetInstanceID() => Id;
        public T Add<T>() where T : Component, new()
        { var c = new T { gameObject = this }; _components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component
            => _components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }
    public class Transform : Component { }
    public static class Time { public static float timeScale = 1f; public static float time; }
    public static class JsonUtility
    {
        public static string ToJson(object value, bool pretty)
            => value is KingdomEnhancedMod.IslandSaveData island ? island.Json : JsonSerializer.Serialize(value);
    }
}

namespace KingdomEnhancedMod
{
    using UnityEngine;
    public sealed class BoolOption { public bool Value = true; }
    internal static class ModConfig
    {
        internal static BoolOption Enabled = new();
        internal static BoolOption HeavyShieldEnabled = new();
    }
    internal static class NetworkBigBoss
    {
        internal static bool IsOnline = false;
        internal static bool HasWorldAuth = true;
    }
    internal sealed class Game
    {
        internal enum State { Menu, Loading, Playing }
        internal static bool SavingEnabled = true;
        internal IntPtr Pointer;
        internal int currentLand;
        internal State state = State.Playing;
    }
    internal sealed class World
    {
        internal IntPtr Pointer;
        internal Transform gameLayer;
    }
    internal sealed class Managers
    {
        internal static Managers Inst;
        internal Game game;
        internal World world;
    }
    internal sealed class PrefsSaveData
    {
        internal IntPtr Pointer;
        internal Dictionary<string,string> contents = new();
        internal bool FailSet;
        internal bool FailCopy;
        internal Dictionary<string,string> SerializedContents = new();
        internal sealed class SrzEntry { internal string key; internal string val; }
        internal List<SrzEntry> srzEntries = new();
        internal void CopyToSerializedEntries()
        {
            if (FailCopy) throw new InvalidOperationException("synthetic srzEntries failure");
            SerializedContents = new(contents); srzEntries.Clear();
            foreach (var row in contents) srzEntries.Add(new SrzEntry { key = row.Key, val = row.Value });
        }
        internal void SetString(string key, string value)
        { if (FailSet) throw new InvalidOperationException("synthetic Prefs failure"); contents[key] = value; }
    }
    internal sealed class GlobalSaveData
    {
        internal static GlobalSaveData _loaded;
        internal IntPtr Pointer;
        internal PrefsSaveData prefs;
        internal List<CampaignSaveData> campaigns = new();
        internal int currentCampaign;
        internal int currentChallenge = 0;
    }
    internal sealed class CampaignSaveData
    {
        // IL2CPP may expose distinct managed wrappers for one native pointer;
        // same non-zero Pointer shares native-backed state, distinct pointers do not.
        private static readonly Dictionary<IntPtr, CampaignSaveData> Backings = new();
        private CampaignSaveData _shared;
        private IntPtr _pointer;
        private int _currentLand;
        private IslandSaveData _currentIsland;
        private List<IslandSaveData> _islandsBacking;
        internal static CampaignSaveData current => GlobalSaveData._loaded?.campaigns[GlobalSaveData._loaded.currentCampaign];
        internal CampaignSaveData()
        { _shared = this; _islandsBacking = new(); }
        internal IntPtr Pointer
        {
            get => _shared._pointer;
            set
            {
                if (value != IntPtr.Zero && Backings.TryGetValue(value, out var canonical)
                    && !ReferenceEquals(canonical, this))
                { _shared = canonical; return; }
                _shared._pointer = value;
                if (value != IntPtr.Zero) Backings[value] = _shared;
            }
        }
        internal int CurrentLand
        { get => _shared._currentLand; set => _shared._currentLand = value; }
        internal IslandSaveData CurrentIsland
        { get => _shared._currentIsland; set => _shared._currentIsland = value; }
        // Native campaign island table, addressed by slot; placeholders keep land 0.
        internal List<IslandSaveData> _islands => _shared._islandsBacking;
    }
    internal sealed class IslandSaveData
    {
        internal sealed class ObjectData
        {
            internal IntPtr Pointer;
            internal string uniqueID;
        }
        internal static IslandSaveData CurrentlySavingIsland;
        internal static bool isSavingGame;
        internal IntPtr Pointer;
        internal int land;
        internal bool isNew = false;
        internal double playTimeDays = 0;
        internal string Json;
        internal List<ObjectData> objects = new();
    }
    internal sealed class Persistent : Component { }
    internal class Character : Component { }
    internal sealed class Peasant : Character { }
    internal sealed class Archer : Character
    { internal object _guardSlot; internal object _knight; internal object _currentFormation; }
    internal sealed class DroppableTool : Component { internal bool pickedUp; }
    internal sealed class Pool
    {
        internal GameObject prefab;
        internal static Pool GetPoolFromPrefabInstance(GameObject root)
            => root?.Tag == "Bow" ? new Pool { prefab = new GameObject(999, 999, "ToolBow") } : null;
    }
    internal static class MusketeerIdentity
    {
        internal static bool GunPromotionInProgress = false;
        internal static bool IsGun(DroppableTool tool) => false;
        internal static bool IsMarked(GameObject root) => false;
        internal static bool IsUnit(Archer unit) => false;
    }
    internal static class HeroRecruitment
    { internal static bool HasPurchasedCareer(Character source) => false; }
    internal static class HeavyShieldShopShell
    {
        internal static int ObservedBowCalls;
        internal static bool ThrowObserve;
        internal static bool ExactAtObservation;
        internal static DroppableTool LastObservedBow;
        internal static void ObservePaidBow(DroppableTool bow)
        {
            ObservedBowCalls++; LastObservedBow = bow;
            ExactAtObservation = HeavyShieldIdentity.TryGetPaidBow(bow, out var handle)
                && HeavyShieldIdentity.ValidateCareer(handle);
            if (ThrowObserve) throw new InvalidOperationException("synthetic Bow visual observer failure");
        }
    }
    internal static class HeavyShieldRuntime
    {
        internal enum AttachResult { Deferred, Attached, Failed }
        internal static bool DeferAttach = false;
        internal static int AttachAttempts;
        internal static bool AttachSucceeds = true;
        internal static int AttachCalls;
        internal static int DetachCalls;
        internal static HeavyShieldSavedCombatState LastRestored;
        internal static bool PreflightSucceeds = true;
        private static readonly Dictionary<HeavyShieldCareerHandle, HeavyShieldSavedCombatState> Active = new();
        internal static bool CarrierPreflightReady => PreflightSucceeds && ModConfig.Enabled.Value
            && ModConfig.HeavyShieldEnabled.Value && Time.timeScale > 0
            && Managers.Inst?.game?.state == Game.State.Playing && !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth;
        internal static bool IsCarrierActive(in HeavyShieldCareerHandle handle)
            => CarrierPreflightReady && Active.TryGetValue(handle, out var state) && !state.RetirementUnknown;
        internal static bool AttachCarrier(Archer carrier, in HeavyShieldCareerHandle handle,
            in HeavyShieldSavedCombatState restored)
            => TryAttachCarrier(carrier, handle, restored) == AttachResult.Attached;
        internal static AttachResult TryAttachCarrier(Archer carrier, in HeavyShieldCareerHandle handle,
            in HeavyShieldSavedCombatState restored)
        {
            AttachAttempts++;
            if (!CarrierPreflightReady || DeferAttach) return AttachResult.Deferred;
            AttachCalls++; LastRestored = restored;
            if (!AttachSucceeds) return AttachResult.Failed;
            Active[handle] = restored; return AttachResult.Attached;
        }
        internal static bool DetachCarrier(in HeavyShieldCareerHandle handle)
        { DetachCalls++; Active.Remove(handle); return true; }
    }
}

namespace KingdomEnhancedMod
{
    // This host uses CLR dictionaries and models only the original managed fallback.
    // The actual reader/Native ABI is tested separately in tests/font-performance/native-key.
    internal static class HeavyShieldNativeKeyReader
    {
        internal static bool EqualsCurrent(System.Collections.Generic.Dictionary<string, string> contents, string expected)
        {
            try { return contents[HeavyShieldSaveSchema.Key] == expected; }
            catch { return false; }
        }
    }
}
