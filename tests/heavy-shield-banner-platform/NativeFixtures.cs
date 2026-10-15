// Host-side native boundary fixtures for the source-linked ANDROID Banner suite.
// Every game/native member is a canary: it counts the access and then throws, so a
// compiled path that actually reads the native boundary fails loudly instead of
// returning a plausible value. Positive controls in Program.cs trigger each canary
// once, proving the counting/throwing behaviour itself works.
using System;

namespace HarmonyLib
{
    // The Android product references 0Harmony; on ANDROID the Harmony attributes are
    // compiled out of the Banner adapter, so only the namespace has to exist here.
}

namespace UnityEngine
{
    public struct Vector3 { public float x; }

    public sealed class Transform
    {
        internal static int Reads;
        internal static void Touch(string what) { Reads++; throw new InvalidOperationException("native read: " + what); }
        public IntPtr Pointer { get { Touch("Transform.Pointer"); return IntPtr.Zero; } }
        public Vector3 position { get { Touch("Transform.position"); return default; } }
        public GameObject gameObject { get { Touch("Transform.gameObject"); return null; } }
        public bool IsChildOf(Transform parent) { Touch("Transform.IsChildOf"); return false; }
    }

    public sealed class GameObject
    {
        internal static int Reads;
        internal static void Touch(string what) { Reads++; throw new InvalidOperationException("native read: " + what); }
        public IntPtr Pointer { get { Touch("GameObject.Pointer"); return IntPtr.Zero; } }
        public bool activeInHierarchy { get { Touch("GameObject.activeInHierarchy"); return false; } }
        public Transform transform { get { Touch("GameObject.transform"); return null; } }
        public Scene scene { get { Touch("GameObject.scene"); return default; } }
        public T GetComponent<T>() where T : class { Touch("GameObject.GetComponent"); return null; }
    }

    public struct Scene { public int handle; }
}

public sealed class Damageable
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: Damageable"); }
    public bool isDead { get { Touch(); return false; } }
    public UnityEngine.GameObject gameObject { get { Touch(); return null; } }
}

public sealed class FixtureArray<T>
{
    internal static void Touch() { FixtureArrayReads.Total++; throw new InvalidOperationException("native read: FixtureArray"); }
    public int Length { get { Touch(); return 0; } }
    public T this[int index] { get { Touch(); return default; } }
}

// Static fields on generic types are per-instantiation; the canary counter is shared
// here so every FixtureArray<T> instantiation the production code actually uses reports
// into the same total.
internal static class FixtureArrayReads { internal static int Total; }

public class FormationUnit
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: FormationUnit"); }
    public UnityEngine.GameObject GetGO { get { Touch(); return null; } }
    public Formation GetFormation() { Touch(); return null; }
    public T TryCast<T>() where T : class { Touch(); return null; }
}

public sealed class Pikeman
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: Pikeman"); }
    public Damageable _damageable { get { Touch(); return null; } }
    public UnityEngine.GameObject GetGO { get { Touch(); return null; } }
    public Formation GetFormation() { Touch(); return null; }
}

public sealed class Formation
{
    public enum Side { Left = -1, Right = 1 }
    public enum FormationType { None, PlayerFormation }
    public enum UnitTypes { None, Pikemen, Total }

    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: Formation"); }
    public IntPtr Pointer { get { Touch(); return IntPtr.Zero; } }
    public int GetInstanceID() { Touch(); return 0; }
    public FixtureArray<UnitTypes> unitTypes { get { Touch(); return null; } }
    public FixtureArray<FormationUnit> units { get { Touch(); return null; } }
    public FixtureArray<float> UnitSpacing { get { Touch(); return null; } }
    public float startOffset { get { Touch(); return 0f; } }
    public float overrideMoveSpeed { get { Touch(); return 0f; } }
    public bool lockSide { get { Touch(); return false; } }
    public Side side { get { Touch(); return Side.Left; } }
    public bool isActiveAndEnabled { get { Touch(); return false; } }
    public FormationType GetFormationType { get { Touch(); return FormationType.None; } }
    public UnityEngine.GameObject gameObject { get { Touch(); return null; } }
    public float GetFormationPosition(int index, Player unit) { Touch(); return 0f; }
    public float RestrictPosition(float x) { Touch(); return 0f; }
    public void OnDisable() { Touch(); }
}

public sealed class Player
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: Player"); }
    public IntPtr Pointer { get { Touch(); return IntPtr.Zero; } }
    public int GetInstanceID() { Touch(); return 0; }
    public bool hasLocalAuthority { get { Touch(); return false; } }
    public bool isActiveAndEnabled { get { Touch(); return false; } }
    public bool IsFormationActive { get { Touch(); return false; } }
    public UnityEngine.Transform transform { get { Touch(); return null; } }
    public UnityEngine.GameObject gameObject { get { Touch(); return null; } }
    public Formation _formation { get { Touch(); return null; } }
    // Referenced only from the PC-only observers (typeof/nameof), kept for signature parity.
    public static void ActivateFormation() { }
}

public sealed class World
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: World"); }
    public UnityEngine.Transform gameLayer { get { Touch(); return null; } }
}

public sealed class Kingdom
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: Kingdom"); }
    public Player playerOne { get { Touch(); return null; } }
    public Player playerTwo { get { Touch(); return null; } }
}

public sealed class Managers
{
    internal static int Reads;
    internal static void Touch() { Reads++; throw new InvalidOperationException("native read: Managers"); }
    public static Managers Inst { get { Touch(); return null; } }
    public World world { get { Touch(); return null; } }
    public Kingdom kingdom { get { Touch(); return null; } }
}
