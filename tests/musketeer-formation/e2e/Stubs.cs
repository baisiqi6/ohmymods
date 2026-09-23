// End-to-end stub wiring for the real PatchWorld_FleetBoatFormation.cs and
// Patch_MusketeerFormation.cs. Every native entry point below mirrors the 2.1 game-source
// semantics (game-source/Assembly-CSharp-2.1.0/Formation.cs, Archer.cs, Player.cs) and calls the
// production Harmony hook bodies through Harness.ProductionHooks, so the activation, TopUp,
// TryDirected and cleanup paths execute as production code. Mod-owned collaborators are stubbed
// with the member shapes the full plugin build uses. Nothing here runs in the game.
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    internal static class ArrayIdentity
    {
        private static int _next = 1000;
        internal static IntPtr Next() => (IntPtr)(_next++);
    }

    public class Il2CppStructArray<T>
    {
        private readonly T[] _items;

        // Test knobs for the partial-write / failed-restore paths (1-based set-attempt counter).
        internal static int ThrowBeforeApplyOnAttempt;
        internal static int ApplyThenThrowOnAttempt;
        internal static int FailRestoreFromAttempt;
        private static int _setAttempts;

        internal static void ResetSetAttempts() => _setAttempts = 0;

        public Il2CppStructArray(int size) { _items = new T[size]; }
        public int Length => _items.Length;
        public IntPtr Pointer { get; } = ArrayIdentity.Next();

        public T this[int index]
        {
            get => _items[index];
            set
            {
                int attempt = ++_setAttempts;
                if (attempt == ThrowBeforeApplyOnAttempt
                    || (FailRestoreFromAttempt > 0 && attempt >= FailRestoreFromAttempt))
                    throw new InvalidOperationException("scripted set failure (before apply)");
                _items[index] = value;
                if (attempt == ApplyThenThrowOnAttempt)
                    throw new InvalidOperationException("scripted set failure (after apply)");
            }
        }
    }

    public class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(int size) { _items = new T[size]; }
        public int Length => _items.Length;
        public IntPtr Pointer { get; } = ArrayIdentity.Next();
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static bool IsTypeRegisteredInIl2Cpp(Type type) => true;
        public static void RegisterTypeInIl2Cpp(Type type) { }
    }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _next = 1;
        public IntPtr Pointer = (IntPtr)_next++;
        public int GetInstanceID() => (int)Pointer;
    }

    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public GameObject() { transform = new Transform(); transform.gameObject = this; }
        public Transform transform;

        private readonly Dictionary<Type, Component> _components = new();

        public T Add<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            _components[typeof(T)] = component;
            return component;
        }

        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T), new object[] { IntPtr.Zero });
            component.gameObject = this;
            _components[typeof(T)] = component;
            return component;
        }

        public T GetComponent<T>() where T : Component
            => _components.TryGetValue(typeof(T), out Component component) ? (T)component : null;

        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;

        public T GetComponent<T>() where T : Component
            => gameObject != null ? gameObject.GetComponent<T>() : null;

        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour
    {
        public MonoBehaviour() { }
        public MonoBehaviour(IntPtr pointer) { Pointer = pointer; }
    }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 position;

        public bool IsChildOf(Transform other)
        {
            for (Transform current = this; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, other)) return true;
            }
            return false;
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
    }

    public static class Time
    {
        public static float unscaledTime;
        public static float timeScale = 1f;
    }
}

public enum Side
{
    Left = -1,
    Right = 1
}

public class Character
{
    public bool inert;
    public bool grabbed;
}

public class Damageable
{
    public bool isDead;
    public bool IsInBlockingFormation;
}

public class Embarkable : UnityEngine.Component { }

public class Embarkee : UnityEngine.Component
{
    public bool IsEmbarked;
    public Embarkable EmbarkableTarget;
}

public class Knight : UnityEngine.Behaviour { }

public class GuardSlot : UnityEngine.Component { }

public class FSM
{
    public int Current;
}

public class Formation : UnityEngine.Behaviour
{
    public enum FormationType
    {
        Bomb,
        PassiveShieldWall,
        ActiveShieldWall,
        PlayerFormation
    }

    public enum UnitTypes
    {
        Archer,
        Knight,
        Squire,
        Pikemen,
        Bomb,
        Gap,
        Player,
        Catapult,
        Ninja,
        Worker,
        AnyShieldedUnit,
        FleetBoat,
        Total
    }

    public interface IFormationUnit
    {
        GameObject GetGO { get; }
        Formation.UnitTypes[] GetFormationUnitTypes();
        bool CanJoinFormation(Formation.FormationType formationType, Side formationSide);
        bool TryRecruit(Formation formation);
        void FormationDestroy();
        void OnLeaveFormation();
        void OnFormationInspire();
        void OnFormationRush();
        void OnFormationRushEnd();
        void OnFormationUpdate();
    }

    public FormationType formationType = FormationType.PlayerFormation;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnitTypes> unitTypes;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> UnitSpacing;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<IFormationUnit> units;
    public float startOffset;
    public Side side = Side.Left;

    /// <summary>Test knob: consume one scripted throw at the start of the native unregister body.</summary>
    public int ThrowOnUnregister;

    public FormationType GetFormationType => formationType;

    public bool RegisterUnit(IFormationUnit unit)
    {
        if (unit == null || unitTypes == null || units == null) return false;
        for (int i = unitTypes.Length - 1; i >= 0; i--)
        {
            if (units[i] != null) continue;
            if (!Contains(unit.GetFormationUnitTypes(), unitTypes[i])) continue;
            units[i] = unit;
            return true;
        }
        return false;
    }

    public void UnregisterUnit(IFormationUnit unit)
    {
        if (unit == null) return;
        object state = Harness.ProductionHooks.UnregisterUnitPrefix(this, unit);
        try
        {
            NativeUnregisterUnit(unit);
        }
        finally
        {
            Harness.ProductionHooks.UnregisterUnitPostfix(state);
        }
    }

    private void NativeUnregisterUnit(IFormationUnit unit)
    {
        if (units == null) return;
        for (int i = 0; i < units.Length; i++)
        {
            if (!ReferenceEquals(units[i], unit)) continue;
            if (ThrowOnUnregister > 0)
            {
                ThrowOnUnregister--;
                throw new InvalidOperationException("scripted UnregisterUnit failure");
            }
            unit.OnFormationRushEnd();
            unit.OnLeaveFormation();
            units[i] = null;
            if (formationType == FormationType.PlayerFormation) UpdatePhalanx();
            return;
        }
    }

    public void OnDisable()
    {
        if (units != null && unitTypes != null)
        {
            for (int i = 0; i < unitTypes.Length; i++)
            {
                if (units[i] != null) UnregisterUnit(units[i]);
            }
        }
        Harness.ProductionHooks.FormationOnDisablePostfix(this);
    }

    public void UpdatePhalanx()
    {
        if (formationType != FormationType.PlayerFormation || units == null) return;
        int num = units.Length - 1;
        while (num >= 1 && (units[num - 1] == null || ContainsPikemen(units[num - 1])))
        {
            if (units[num] == null && units[num - 1] != null)
            {
                units[num] = units[num - 1];
                units[num - 1] = null;
                units[num].OnFormationUpdate();
            }
            num--;
        }
    }

    public bool HasUnits()
    {
        if (units == null) return false;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null) return true;
        }
        return false;
    }

    public bool IsInFormation(IFormationUnit unit)
    {
        if (units == null) return false;
        for (int i = 0; i < units.Length; i++)
        {
            if (ReferenceEquals(units[i], unit)) return true;
        }
        return false;
    }

    /// <summary>Native Formation.GetXPosForIndex (game-source Formation.cs:319-332).</summary>
    public float GetXPosForIndex(int index)
    {
        float num = startOffset;
        for (int i = 0; i < index; i++)
        {
            if (units[i] != null || unitTypes[i] == UnitTypes.Gap) num += UnitSpacing[(int)unitTypes[i]];
        }
        return side != Side.Left ? num : -num;
    }

    public float GetXPosForUnit(IFormationUnit unit)
    {
        if (units == null) return 0f;
        for (int i = 0; i < units.Length; i++)
        {
            if (ReferenceEquals(units[i], unit)) return GetXPosForIndex(i);
        }
        return 0f;
    }

    private static bool Contains(UnitTypes[] list, UnitTypes type)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == type) return true;
        }
        return false;
    }

    private static bool ContainsPikemen(IFormationUnit unit)
        => Contains(unit.GetFormationUnitTypes(), UnitTypes.Pikemen);
}

public class Archer : UnityEngine.Behaviour, Formation.IFormationUnit
{
    public Character _character = new();
    public Damageable _damageable = new();
    public Embarkee _embarkee = new();
    public Knight _knight;
    public GuardSlot _guardSlot;
    public bool inGuardSlot;
    public bool harmless;
    public bool playerControlled;

    private Formation _formation;

    // Script knobs (test-only failure injection).
    public bool ThrowOnConvertToSoldier;
    public bool ReturnFalseAfterRegister;
    public bool ReturnFalseAfterBind;
    public int ThrowOnLeave;
    public Action<Formation> ReplaceArraysOnRecruit;
    public int ConvertToSoldierCalls;
    public int OnLeaveCalls;

    public Formation GetFormation() => _formation;
    public bool ShouldPlayerControl() => playerControlled;
    public GameObject GetGO => gameObject;

    public Formation.UnitTypes[] GetFormationUnitTypes() => new[]
    {
        Formation.UnitTypes.Archer,
        Formation.UnitTypes.AnyShieldedUnit
    };

    public bool CanJoinFormation(Formation.FormationType formationType, Side formationSide)
        => !_character.inert && _formation == null && _guardSlot == null
           && _embarkee.EmbarkableTarget == null && !_embarkee.IsEmbarked
           && (formationType != Formation.FormationType.PlayerFormation || _knight == null);

    /// <summary>Native Archer.TryRecruit (game-source Archer.cs:1741-1769) with the production guard.</summary>
    public bool TryRecruit(Formation formation)
    {
        if (!Harness.ProductionHooks.ArcherTryRecruitAllowed(this, formation)) return false;
        if (playerControlled) return false;
        if (!CanJoinFormation(formation.GetFormationType, formation.side)) return false;
        if (!formation.RegisterUnit(this)) return false;
        if (ReplaceArraysOnRecruit != null) ReplaceArraysOnRecruit(formation);
        if (ReturnFalseAfterRegister) return false;
        if (formation.GetFormationType == Formation.FormationType.PlayerFormation) ConvertToSoldier();
        _formation = formation;
        if (ReturnFalseAfterBind) return false;
        return true;
    }

    public void ConvertToSoldier()
    {
        ConvertToSoldierCalls++;
        if (ThrowOnConvertToSoldier) throw new InvalidOperationException("scripted ConvertToSoldier failure");
    }

    public void OnLeaveFormation()
    {
        if (ThrowOnLeave > 0)
        {
            ThrowOnLeave--;
            throw new InvalidOperationException("scripted OnLeave failure");
        }
        _formation = null;
        OnLeaveCalls++;
    }

    public void FormationDestroy() { }
    public void OnFormationInspire() { }
    public void OnFormationRush() { }
    public void OnFormationRushEnd() { }
    public void OnFormationUpdate() { }

    /// <summary>Test helper: bind to a formation without going through RegisterUnit.</summary>
    public void BindForTests(Formation formation) => _formation = formation;
}

public class Pikeman : UnityEngine.Behaviour, Formation.IFormationUnit
{
    public GameObject GetGO => gameObject;
    public Formation.UnitTypes[] GetFormationUnitTypes() => new[] { Formation.UnitTypes.Pikemen };
    public bool CanJoinFormation(Formation.FormationType formationType, Side formationSide) => true;
    public bool TryRecruit(Formation formation) => formation.RegisterUnit(this);
    public void FormationDestroy() { }
    public void OnLeaveFormation() { }
    public void OnFormationInspire() { }
    public void OnFormationRush() { }
    public void OnFormationRushEnd() { }
    public void OnFormationUpdate() { }
}

public class Player : UnityEngine.Behaviour
{
    public Formation _formation;

    /// <summary>Test knob: consume one scripted throw in the native activate body.</summary>
    public static int ThrowInActivateBody;

    public void ActivateFormation() => Harness.ProductionHooks.ActivateFormation(this);

    internal void ActivateBody()
    {
        _formation.enabled = true;
        if (ThrowInActivateBody > 0)
        {
            ThrowInActivateBody--;
            throw new InvalidOperationException("scripted native ActivateFormation failure");
        }
        _formation.side = Util.SideApproximately(transform.position.x);
    }

    public void DeactivateFormation()
    {
        _formation.enabled = false;
        _formation.OnDisable();
    }
}

public static class Util
{
    public static Side SideApproximately(float x) => x < 0f ? Side.Left : Side.Right;
}

public class World : UnityEngine.Object
{
    public UnityEngine.Transform gameLayer;
}

public class Game : UnityEngine.Object
{
    public enum State { Menu, Playing }
    public State state = State.Playing;
}

public class Kingdom
{
    public readonly List<FleetBoat> FleetBoats = new();
}

public class Managers
{
    public static Managers Inst;
    public Kingdom kingdom;
    public World world;
    public Game game;
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline;
}

public class FleetBoat : UnityEngine.Behaviour, Formation.IFormationUnit
{
    public static class State
    {
        public const int Idle = 0, InFormation = 1, WaitingForSquad = 2, WaitingForSailAway = 3,
            Attacking = 4, ReturningToBase = 5;

        public static bool CanJoinFormation(int state) => state == Idle || state == ReturningToBase;
    }

    public Side Side = Side.Right;
    public int _boatNumber = 1;
    public Formation _currentFormation;
    public FSM _fsm = new();
    public bool IsAccessible = true;
    public bool nativeCanJoin = true;

    public bool HasFormation => _currentFormation != null;
    public GameObject GetGO => gameObject;

    public Formation.UnitTypes[] GetFormationUnitTypes() => new[] { Formation.UnitTypes.FleetBoat };

    public bool CanJoinFormation(Formation.FormationType formationType, Side formationSide)
        => nativeCanJoin && formationType == Formation.FormationType.PlayerFormation;

    public bool TryRecruit(Formation formation)
    {
        if (!CanJoinFormation(formation.GetFormationType, formation.side)) return false;
        if (!State.CanJoinFormation(_fsm.Current)) return false;
        if (!IsAccessible) return false;
        if (!formation.RegisterUnit(this)) return false;
        _currentFormation = formation;
        _fsm.Current = State.InFormation;
        return true;
    }

    public void FormationDestroy() { }
    public void OnLeaveFormation()
    {
        _currentFormation = null;
        _fsm.Current = State.ReturningToBase;   // native FleetBoat.OnLeaveFormation
    }
    public void OnFormationInspire() { }
    public void OnFormationRush() { }
    public void OnFormationRushEnd() { }
    public void OnFormationUpdate() { }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class Flag
        {
            internal bool Value;
            internal Flag(bool value) { Value = value; }
        }

        internal static Flag Enabled = new Flag(true);
        internal static Flag MusketeerEnabled = new Flag(true);
    }

    internal static class MusketeerIdentity
    {
        internal static readonly List<Archer> Units = new();
        internal static bool MarkedEnabled = true;

        internal static bool IsUnit(Archer archer)
            => MarkedEnabled && archer != null && Units.Contains(archer);

        internal static void CopyUnits(List<Archer> destination)
        {
            destination.Clear();
            destination.AddRange(Units);
        }
    }

    internal static class MusketeerAccess
    {
        internal static bool EnabledFlag = true;
        internal static bool InWorldResult = true;

        internal static bool Enabled =>
            EnabledFlag && ModConfig.Enabled.Value && ModConfig.MusketeerEnabled.Value
            && NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.IsOnline;

        internal static bool Playing => Enabled && Time.timeScale > 0f
            && Managers.Inst != null && Managers.Inst.game != null
            && Managers.Inst.game.state == Game.State.Playing;

        internal static bool InWorld(UnityEngine.Component component)
            => component != null && InWorldResult;

        internal static bool InWorld(UnityEngine.GameObject root)
            => root != null && InWorldResult;
    }

    internal static class HeroArcherRuntime
    {
        internal static readonly HashSet<Archer> Heroes = new();

        internal static bool IsHero(Archer archer) => archer != null && Heroes.Contains(archer);
    }

    /// <summary>
    /// Real: il2cpp/CrossbowmanLifecycle.cs identity reader (reusable marker + live global switch,
    /// fail-closed). The stub mirrors only the shape the guard consumes: an instance marker (set
    /// here) behind the global mod switch. Default off so existing pipeline scenarios keep their
    /// native outcomes; flip IdentityEnabled and register the archer to exercise the exclusion.
    /// </summary>
    internal static class CrossbowmanLifecycle
    {
        internal static readonly HashSet<Archer> Crossbowmen = new();
        internal static bool IdentityEnabled;

        internal static bool IsCrossbowman(Archer archer)
            => IdentityEnabled && ModConfig.Enabled.Value && archer != null && Crossbowmen.Contains(archer);
    }

    /// <summary>
    /// Real: deer slice il2cpp/MusketeerRuntime.cs. Only the same-life lease contract this slice
    /// consumes is stubbed: 0 = no applied package / unknown / stripped, a new life gets a new
    /// monotonic lease. The knobs simulate a pooled re-arm on the same GameObject.
    /// </summary>
    internal static class MusketeerRuntime
    {
        private static long _counter;
        private static readonly Dictionary<Archer, long> Leases = new();

        internal static long BindingLease(Archer archer)
            => archer != null && Leases.TryGetValue(archer, out long lease) ? lease : 0L;

        internal static bool MatchesBindingLease(Archer archer, long lease)
            => lease != 0L && BindingLease(archer) == lease;

        /// <summary>Test knob: same GameObject, new life (lease changes, state otherwise kept).</summary>
        internal static void ArmNewLife(Archer archer)
        {
            if (archer != null) Leases[archer] = ++_counter;
        }

        internal static void Disarm(Archer archer)
        {
            if (archer != null) Leases[archer] = 0L;
        }

        internal static void ResetLeases()
        {
            Leases.Clear();
            _counter = 0L;
        }
    }

    /// <summary>Fleet pairing/selection is exercised by tests/fleet-greek-squads; here it is a pass-through.</summary>
    internal static class FleetGreekSquads
    {
        internal static void Release(Formation formation) { }
        internal static void Select(Formation formation, UnityEngine.Transform root, Side side,
            List<FleetBoat> candidates) { }
        internal static void Complete(Formation formation) { }
        internal static void Maintain() { }

        internal static bool NativeCandidateCanJoin(FleetBoat boat, Side side)
            => boat != null && boat.CanJoinFormation(Formation.FormationType.PlayerFormation, side);
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new();
        internal Log LogSource = new();
    }

    internal class Log
    {
        internal readonly List<string> Infos = new();
        internal readonly List<string> Warnings = new();

        internal void LogInfo(string message) => Infos.Add(message);
        internal void LogWarning(string message) => Warnings.Add(message);
    }
}
