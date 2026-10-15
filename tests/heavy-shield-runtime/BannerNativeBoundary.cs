// Controlled native API boundary for the ACTUAL production Banner + Runtime host tests.
// The formation methods supply test values; they do not emulate or validate Unity/IL2CPP.
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T> : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase where T : struct
    {
        private readonly T[] _rows;
        public Il2CppStructArray(T[] rows) { _rows = rows; }
        public int Length => _rows.Length;
        public T this[int index] { get => _rows[index]; set => _rows[index] = value; }
    }
}

public class Player : Behaviour
{
    public Formation _formation;
    public bool hasLocalAuthority = true;
    public bool IsFormationActive => _formation != null && _formation.isActiveAndEnabled;
    public void ActivateFormation() { _formation.enabled = true; }
}

public class Formation : Behaviour
{
    public enum FormationType { Bomb, PassiveShieldWall, ActiveShieldWall, PlayerFormation }
    public enum UnitTypes { Archer, Knight, Squire, Pikemen, Bomb, Gap, Player, Catapult, Ninja, Worker, AnyShieldedUnit, FleetBoat, Total }
    public class IFormationUnit : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public GameObject Root;
        public Formation Owner;
        public virtual GameObject GetGO => Root;
        public virtual Formation GetFormation() => Owner;
    }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnitTypes> unitTypes = new(new[] { UnitTypes.Pikemen });
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<IFormationUnit> units = new(new IFormationUnit[1]);
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> UnitSpacing = new(Enumerable.Repeat(1f, 12).ToArray());
    public FormationType GetFormationType => FormationType.PlayerFormation;
    public Side side = Side.Right;
    public bool lockSide = true;
    public float startOffset, overrideMoveSpeed = 1.75f;
    public float AnchorX, LastRestrictedInput;
    public float? RestrictResult;
    public bool ReadFault;
    public float GetFormationPosition(int index, IFormationUnit unit)
    { if (ReadFault) throw new InvalidOperationException("controlled formation read fault"); return AnchorX; }
    public float RestrictPosition(float position) { LastRestrictedInput = position; return RestrictResult ?? position; }
    public void OnDisable() { }
}

// This wrapper exposes only the metadata members the adapter reads. Native inheritance,
// object identity conversion, life, and actual pike behavior are deliberately not simulated.
public class Pikeman : Formation.IFormationUnit { public Damageable _damageable; }

namespace KingdomEnhancedMod
{
    internal static class PatchMusketeerFormation
    {
        internal static Formation Directed;
        internal static bool HasActiveDirectedTransaction(Formation formation) => ReferenceEquals(Directed, formation);
    }
    internal static class PatchWorld_FleetBoatFormation
    {
        internal static Formation Dirty;
        internal static bool HasDirtyRowTypes(Formation formation) => ReferenceEquals(Dirty, formation);
    }
}

internal sealed class NativeBannerFixture : IDisposable
{
    private readonly Player _previous;
    internal Player Player { get; }
    internal Formation Formation { get; }
    internal static long LastActivation => (long)typeof(HeavyShieldBanner)
        .GetField("_nextActivation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    internal NativeBannerFixture(float targetX, int side = 1)
    {
        _previous = Managers.Inst.kingdom.playerOne;
        var playerGo = new GameObject("boundary banner player");
        var formationGo = new GameObject("boundary player formation");
        playerGo.transform.SetParent(Managers.Inst.world.gameLayer, false);
        formationGo.transform.SetParent(Managers.Inst.world.gameLayer, false);
        playerGo.transform.position = new(targetX, 0);
        Formation = formationGo.AddComponent<Formation>();
        Formation.side = (Side)side; Formation.AnchorX = targetX - side * .4f;
        Player = playerGo.AddComponent<Player>(); Player._formation = Formation;
        Managers.Inst.kingdom.playerOne = Player;
        Capture();
    }
    internal bool Active
    {
        get => Formation.isActiveAndEnabled;
        set { if (value) Capture(); else Deactivate(); }
    }
    internal bool Dirty
    {
        get => ReferenceEquals(PatchWorld_FleetBoatFormation.Dirty, Formation);
        set => PatchWorld_FleetBoatFormation.Dirty = value ? Formation : null;
    }
    internal void Capture(bool runOriginal = true)
    {
        Formation.enabled = true;
        Observer("ActivateObserver", Player, runOriginal);
    }
    internal void Deactivate()
    {
        Formation.enabled = false;
        Observer("DisableObserver", Formation, true);
    }
    private static void Observer(string name, object instance, bool ran) => typeof(HeavyShieldBanner)
        .GetNestedType(name, BindingFlags.NonPublic).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new[] { instance, (object)ran });
    public void Dispose()
    {
        Deactivate();
        if (ReferenceEquals(PatchMusketeerFormation.Directed, Formation)) PatchMusketeerFormation.Directed = null;
        if (Dirty) Dirty = false;
        if (ReferenceEquals(Managers.Inst.kingdom.playerOne, Player)) Managers.Inst.kingdom.playerOne = _previous;
        UnityEngine.Object.Destroy(Player.gameObject); UnityEngine.Object.Destroy(Formation.gameObject);
    }
}
