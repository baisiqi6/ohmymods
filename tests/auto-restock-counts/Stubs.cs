// Behavioral test stubs for game/native APIs referenced by AutoRestockCounts.cs.
// These model the IL2CPP surface the production file compiles against:
// Pointer identities, native call counts, event subscribe/unsubscribe and
// implicit Action->event conversions. No production logic is mirrored here.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Scene { public int handle; }

    public class Transform
    {
        public IntPtr Pointer;
        public GameObject gameObject;
        public Transform parent;
        public bool IsChildOf(Transform t)
        {
            Transform cur = this;
            while (cur != null)
            {
                if (ReferenceEquals(cur, t)) return true;
                cur = cur.parent;
            }
            return false;
        }
    }

    public class Component
    {
        private static int _nextId;
        public int InstanceId = ++_nextId;
        public int GetInstanceID() => InstanceId;
        public GameObject gameObject;
        public Transform transform;
        public IntPtr Pointer;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
    }

    public static class Time { public static int frameCount; public static float time; }

    public class GameObject
    {
        public IntPtr Pointer;
        public bool activeInHierarchy = true;
        public bool throwOnGetComponent;
        public Scene scene;
        public Transform transform;
        public readonly List<Component> components = new List<Component>();
        public T GetComponent<T>() where T : Component
        {
            if (throwOnGetComponent) throw new InvalidOperationException("GetComponent failed");
            foreach (Component c in components) if (c is T t) return t;
            return null;
        }
        public void Add(Component c) { c.gameObject = this; c.transform = transform; components.Add(c); }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
        public HarmonyPatchAttribute() { }
        public HarmonyPatchAttribute(Type type) { }
        public HarmonyPatchAttribute(Type type, string name) { }
    }
    public sealed class HarmonyPostfixAttribute : Attribute { }
    public sealed class HarmonyPrefixAttribute : Attribute { }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public T Value;
        public static ConfigEntry<T> On(T v) => new ConfigEntry<T> { Value = v };
    }
}

namespace Il2CppSystem
{
    public sealed class Action<T>
    {
        public System.Action<T> Handler;
        public static implicit operator Action<T>(System.Action<T> a) => new Action<T> { Handler = a };
        public static Action<T> operator +(Action<T> a, Action<T> b) => new() { Handler = a?.Handler + b?.Handler };
        public static Action<T> operator -(Action<T> a, Action<T> b) => new() { Handler = a?.Handler - b?.Handler };
    }
}

namespace KingdomEnhancedMod
{
    public static class ModConfig
    {
        public static BepInEx.Configuration.ConfigEntry<bool> Enabled;
        public static BepInEx.Configuration.ConfigEntry<bool> AutoRestockWorkersEnabled;
        public static BepInEx.Configuration.ConfigEntry<bool> AutoRestockArchersEnabled;
        public static BepInEx.Configuration.ConfigEntry<bool> AutoRestockNinjasEnabled;
        public static BepInEx.Configuration.ConfigEntry<bool> AutoRestockBerserkersEnabled;
        public static BepInEx.Configuration.ConfigEntry<bool> AutoRestockPeasantsEnabled;
    }
}

// === game types (global namespace, as in Assembly-CSharp) ===

public static class Native
{
    public static int GetItemCountCalls;
    public static void Reset() => GetItemCountCalls = 0;
}

public class Damageable : UnityEngine.Component
{
    public sealed class DeathEvent
    {
        public System.Action<UnityEngine.GameObject> Handler;
        public static implicit operator DeathEvent(System.Action<UnityEngine.GameObject> a) => new() { Handler = a };
        public static DeathEvent operator +(DeathEvent a, DeathEvent b) => new() { Handler = a?.Handler + b?.Handler };
        public static DeathEvent operator -(DeathEvent a, DeathEvent b) => new() { Handler = a?.Handler - b?.Handler };
    }
    public bool isDead, throwOnSubscribe;
    private DeathEvent _handler;
    public DeathEvent OnDeath
    {
        get => _handler;
        set { if (throwOnSubscribe && (value?.Handler?.GetInvocationList().Length ?? 0) > DeathSubscriberCount)
            throw new InvalidOperationException("OnDeath subscribe failed"); _handler = value; }
    }
    public int DeathSubscriberCount => _handler?.Handler?.GetInvocationList().Length ?? 0;
    public void FireDeath() { _handler?.Handler?.Invoke(gameObject); }
}

public class Character : UnityEngine.Component { public Damageable _damageable; }
public class Berserker : UnityEngine.Component { }
public class Ninja : UnityEngine.Component { public bool _isFisher; }
public class Archer : UnityEngine.Component { }
public class Worker : UnityEngine.Component { }
public class Peasant : UnityEngine.Component { }
public class Beggar : UnityEngine.Component
{
    public Character _character;
    public bool _isEating;
    public bool enabled = true;
    public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
}
public class Baker : UnityEngine.Component { public bool TryEatBread(Beggar beggar) => false; }

public class CharacterRoster : IEnumerable<Character>
{
    private readonly HashSet<Character> _set = new HashSet<Character>();
    public int EnumerationCount;
    public bool throwOnEnumerate;
    public void Add(Character c) => _set.Add(c);
    public bool Remove(Character c) => _set.Remove(c);
    public IEnumerator<Character> GetEnumerator()
    {
        EnumerationCount++;
        if (throwOnEnumerate) throw new InvalidOperationException("roster enumeration failed");
        return _set.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class Kingdom : UnityEngine.Component
{
    public CharacterRoster _characters = new CharacterRoster();
    public void AddCharacter(Character c) { _characters.Add(c); }
    public void RemoveCharacter(Character c) { _characters.Remove(c); }
}

public class World : UnityEngine.Component { public Transform gameLayer; }

public class ShopPlanner : UnityEngine.Component
{
    public List<UnityEngine.GameObject> shops = new List<UnityEngine.GameObject>();
    // Legacy test-fixture adapter: production reads only native registered shops.
    // Separate backing keeps new tests able to leave a bakery outside placed shops.
    private UnityEngine.GameObject[] _placed = Array.Empty<UnityEngine.GameObject>();
    public UnityEngine.GameObject[] _placedShops
    {
        get => _placed;
        set { _placed = value; shops = value == null ? null : new List<UnityEngine.GameObject>(value); }
    }
    public void AddShop(UnityEngine.GameObject go) { shops.Add(go); }
    public void RemoveShop(UnityEngine.GameObject go) { shops.Remove(go); }
    private void SetPlacedShop() { }
}

public class Droppable : UnityEngine.Component
{
    public string tag;
    public bool throwOnSubscribe;
    private Il2CppSystem.Action<Droppable> _picked, _disabled;
    public Il2CppSystem.Action<Droppable> OnPickedUp
    {
        get => _picked;
        set { if (throwOnSubscribe && (value?.Handler?.GetInvocationList().Length ?? 0) > PickedUpSubscriberCount)
            throw new InvalidOperationException("OnPickedUp subscribe failed"); _picked=value; }
    }
    public Il2CppSystem.Action<Droppable> OnDisabled
    {
        get => _disabled;
        set { if (throwOnSubscribe && (value?.Handler?.GetInvocationList().Length ?? 0) > DisabledSubscriberCount)
            throw new InvalidOperationException("OnDisabled subscribe failed"); _disabled=value; }
    }
    public int PickedUpSubscriberCount => _picked?.Handler?.GetInvocationList().Length ?? 0;
    public int DisabledSubscriberCount => _disabled?.Handler?.GetInvocationList().Length ?? 0;
    public void FirePickedUp() { _picked?.Handler?.Invoke(this); }
    public void FireDisabled() { _disabled?.Handler?.Invoke(this); }
    public bool CompareTag(string t) => tag == t;
}

public class PayableShop : UnityEngine.Component
{
    public Droppable itemPrefab;
    public bool throwOnGetItemCount;
    public int itemCountOverride = -1;
    public int GetItemCountCalls;
    public int ItemsReadCount;
    private Droppable[] _itemsBacking;
    public Droppable[] _items { get { ItemsReadCount++; return _itemsBacking; } }
    public void SetItems(Droppable[] items) => _itemsBacking = items;
    public void AddItem() { }
    public int GetItemCount()
    {
        Native.GetItemCountCalls++;
        GetItemCountCalls++;
        if (throwOnGetItemCount) throw new InvalidOperationException("native GetItemCount failed");
        return itemCountOverride >= 0 ? itemCountOverride : (_itemsBacking != null ? _itemsBacking.Length : 0);
    }
}

public class Managers
{
    public Kingdom kingdom;
    public World world;
    public ShopPlanner shopPlanner;
}

public static class Fabric
{
    public static UnityEngine.GameObject NewGo(IntPtr ptr, int sceneHandle, UnityEngine.Transform parent)
    {
        var t = new UnityEngine.Transform { Pointer = ptr };
        var go = new UnityEngine.GameObject { Pointer = ptr, scene = new UnityEngine.Scene { handle = sceneHandle }, transform = t };
        t.gameObject = go;
        t.parent = parent;
        return go;
    }
}
