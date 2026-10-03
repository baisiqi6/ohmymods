using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Musketeer half of the banner back row (Player.ActivateFormation / FormationType.PlayerFormation).
///
/// The composite formation layout (native order + up to four Squire-typed musketeer row slots
/// inserted before the bow line + the expanded fleet-boat slots) is owned by
/// <see cref="PatchWorld_FleetBoatFormation"/>: that file is the only writer of the live
/// unitTypes/units/UnitSpacing/startOffset arrays, the only owner of the per-formation
/// profile/coordinator, and the only restorer of the native baseline. This file owns what is
/// musketeer-specific:
///
///  * the candidate policy for the four row slots (identity + native free-archer gates, nearest
///    first, at most four, missing slots simply stay empty), and
///  * the guard that keeps marked musketeers out of the native bow slots while a row is reserved,
///    so ordinary archers keep their native four slots and musketeers never displace them (the
///    reverse is inherent: an empty row slot is Squire-typed, which no native IFormationUnit type
///    table matches); the guard also refuses every other Archer for that formation while a
///    directed transaction is armed or while a temporary-type restore is still owed, because the
///    reserved seat is open during those windows.
///
/// <see cref="MusketeerFormationLayout"/> is the pure planning half (array in, array out) so the
/// composite order, the row placement and the directed-recruit transaction are unit-testable
/// without the game assembly. Neither half creates, teleports or re-stats a unit: joining always
/// goes through the native Archer.TryRecruit, and every other lifecycle (movement, enemy
/// targeting, death, banner furl, pool reuse) stays native.
/// </summary>
internal static class PatchMusketeerFormation
{
    /// <summary>Rear-row capacity. Fewer owned musketeers just leave slots empty.</summary>
    internal const int MaxMusketeers = MusketeerFormationLayout.RowSeats;

    private static readonly List<Archer> Roster = new(MaxMusketeers * 8);
    private static readonly List<Archer> Eligible = new(MaxMusketeers);
    private static readonly RearRowNearestFirst Ranking = new();
    private static readonly HashSet<string> Logged = new();

    private static IntPtr _directedArcher;
    private static IntPtr _directedFormation;
    private static long _directedLife;
    private static int _directedFamily = -1;
    private static IntPtr _directedWorld;
    private static IntPtr _directedWorldRoot;

    /// <summary>Musketeer row family id shared with the owner's transaction (0 = musketeer).</summary>
    internal const int DirectedMusketeerFamily = 0;
    /// <summary>Crossbow row family id shared with the owner's transaction (1 = crossbow).</summary>
    internal const int DirectedCrossbowFamily = 1;

    private static bool TryCurrentWorldPointers(out IntPtr world, out IntPtr root)
    {
        world = IntPtr.Zero;
        root = IntPtr.Zero;
        try
        {
            Managers managers = Managers.Inst;
            if (managers == null || managers.world == null || managers.world.gameLayer == null) return false;
            world = managers.world.Pointer;
            root = managers.world.gameLayer.Pointer;
            return world != IntPtr.Zero && root != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Narrow aliveness check for an already-joined directed member: active object, not dead.
    /// The pre-join free gate (GetFormation()==null etc.) must not be reused after the join.
    /// </summary>
    internal static bool IsDirectedActorLive(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
            Damageable damageable = archer._damageable;
            return damageable != null && !damageable.isDead;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Row feature gate used by the directed bypass (same predicate the row maintenance uses).</summary>
    private static bool DirectedFeaturePlaying(int family)
    {
        try
        {
            return family == DirectedCrossbowFamily
                ? PatchCrossbowFormation.Playing
                : MusketeerAccess.Playing;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True only while the offline musketeer feature is usable in this world (config on, world
    /// authority, not online, known biome). The fleet owner asks for the rear row with this and
    /// re-checks it on every maintenance pass; a row is never created for a co-op/disabled peer.
    /// </summary>
    internal static bool RowRequested
    {
        get
        {
            try { return MusketeerAccess.Enabled; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Marks the single career unit that is allowed through the native recruit guard right now.
    /// The scope carries the row family, the captured positive life and the current world/layer
    /// pointers, not only the two object pointers: a synchronous native callback may recycle the
    /// same GameObject into a new pool life (new token), switch career, lose authority or move
    /// the unit into another world, and such an object must never inherit the armed exception.
    /// </summary>
    internal static void BeginDirected(Archer archer, Formation formation, int family, long life)
    {
        _directedArcher = archer != null ? archer.Pointer : IntPtr.Zero;
        _directedFormation = formation != null ? formation.Pointer : IntPtr.Zero;
        _directedFamily = family;
        _directedLife = life;
        if (!TryCurrentWorldPointers(out _directedWorld, out _directedWorldRoot))
        {
            _directedWorld = IntPtr.Zero;
            _directedWorldRoot = IntPtr.Zero;
        }
    }

    internal static void EndDirected()
    {
        _directedArcher = IntPtr.Zero;
        _directedFormation = IntPtr.Zero;
        _directedLife = 0L;
        _directedFamily = -1;
        _directedWorld = IntPtr.Zero;
        _directedWorldRoot = IntPtr.Zero;
    }

    /// <summary>
    /// Strict directed check used as the guard's bypass. All of the following must hold: exact
    /// archer/formation pointer pair, a captured positive life still current for the family's
    /// career reader, the row identity live and mutually exclusive with the other family, the
    /// unit still in the current world layer, offline world authority, the same world/layer as at
    /// arm time, and the row's own feature predicate still playing. A same-pointer new life, a
    /// career switch, an authority/world change or a disabled feature never passes on pointer
    /// equality alone.
    /// </summary>
    internal static bool IsDirected(Archer archer, Formation formation)
    {
        if (_directedArcher == IntPtr.Zero || _directedFormation == IntPtr.Zero
            || _directedLife <= 0L || _directedFamily < 0) return false;
        if (archer == null || formation == null) return false;
        if (archer.Pointer != _directedArcher || formation.Pointer != _directedFormation) return false;
        try
        {
            if (!NetworkBigBoss.HasWorldAuth || NetworkBigBoss.IsOnline) return false;
            if (!DirectedFeaturePlaying(_directedFamily)) return false;
            if (!IsDirectedActorLive(archer)) return false;
            if (!MusketeerAccess.InWorld(archer)) return false;
            if (!TryCurrentWorldPointers(out IntPtr world, out IntPtr root)
                || world != _directedWorld || root != _directedWorldRoot) return false;
            if (_directedFamily == DirectedCrossbowFamily)
            {
                return CrossbowmanLifecycle.MatchesFormationLife(archer, _directedLife)
                    && CrossbowmanLifecycle.IsCrossbowman(archer)
                    && !MusketeerIdentity.IsUnit(archer);
            }
            return MusketeerRuntime.MatchesBindingLease(archer, _directedLife)
                && MusketeerIdentity.IsUnit(archer)
                && !CrossbowmanLifecycle.IsCrossbowman(archer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True while a directed recruit is armed for this formation. During that window the temporary
    /// types leave the reserved seat open, so every other Archer recruit for this formation must be
    /// refused; the exact directed pair is still allowed through <see cref="IsDirected"/>.
    /// </summary>
    internal static bool HasActiveDirectedTransaction(Formation formation)
    {
        if (_directedArcher == IntPtr.Zero || _directedFormation == IntPtr.Zero) return false;
        return formation != null && formation.Pointer == _directedFormation;
    }

    /// <summary>
    /// Native-free-archer gate for one rear slot. Everything native reserves for another job or
    /// lifecycle is left untouched: knight followers, tower/guard posts, embarked or boarding
    /// units, grabbed/inert/hidden units, play-controlled units, dead units, units already in a
    /// formation and heroes. Any unknown state is a rejection (fail closed). Shared verbatim by
    /// the musketeer and the crossbow rows so both rows can never disagree about who is free.
    /// </summary>
    internal static bool IsFreeForRearRow(Archer archer)
    {
        try
        {
            if (archer == null) return false;
            if (!MusketeerAccess.InWorld(archer)) return false;          // current world layer, active
            GameObject gameObject = archer.gameObject;
            if (gameObject == null || !gameObject.activeInHierarchy || !archer.enabled) return false;
            if (archer.harmless) return false;                            // hiding spot / quest hidden
            if (archer.GetFormation() != null) return false;              // already in this or another formation
            if (archer._knight != null) return false;                     // knight follower: native slot owner
            if (archer._guardSlot != null || archer.inGuardSlot) return false;   // tower / guard post
            Embarkee embarkee = archer._embarkee;
            if (embarkee == null || embarkee.IsEmbarked || embarkee.EmbarkableTarget != null) return false;
            if (archer.ShouldPlayerControl()) return false;               // player-controlled unit
            Character character = archer._character;
            if (character == null || character.inert || character.grabbed) return false;
            Damageable damageable = archer._damageable;
            if (damageable == null || damageable.isDead) return false;
            if (HeroArcherRuntime.IsHero(archer)) return false;           // hero keeps its own slot
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Musketeer row gate: live paid career on top of the shared native-free gate. A crossbowman
    /// stays refused here, so the crossbow row's own directed seat remains the only way a marked
    /// crossbowman can join a banner.
    /// </summary>
    internal static bool IsEligible(Archer archer)
    {
        try
        {
            if (archer == null) return false;
            if (!MusketeerAccess.Enabled) return false;
            if (!MusketeerIdentity.IsUnit(archer)) return false;
            if (CrossbowmanLifecycle.IsCrossbowman(archer)) return false;
            return IsFreeForRearRow(archer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Nearest-first snapshot of the musketeers that may take one of the free rear slots right
    /// now. Reads only the identity career registry (bounded, never a scene scan) and returns at
    /// most <paramref name="freeSlots"/> entries. Invalid candidates are skipped, not waited on.
    /// </summary>
    internal static int Collect(Formation formation, int freeSlots, List<Archer> output)
    {
        if (output == null) return 0;
        output.Clear();
        try
        {
            if (formation == null || formation.transform == null || freeSlots <= 0) return 0;
            if (!MusketeerAccess.Enabled) return 0;
            float formationX = formation.transform.position.x;
            if (!float.IsFinite(formationX)) return 0;
            int limit = Math.Min(freeSlots, MaxMusketeers);
            Roster.Clear();
            MusketeerIdentity.CopyUnits(Roster);
            Eligible.Clear();
            for (int i = 0; i < Roster.Count; i++)
            {
                Archer archer = Roster[i];
                if (IsEligible(archer)) Eligible.Add(archer);
            }
            if (Eligible.Count == 0) return 0;
            Ranking.X = formationX;
            Eligible.Sort(Ranking);
            for (int i = 0; i < Eligible.Count && i < limit; i++) output.Add(Eligible[i]);
            return output.Count;
        }
        catch (Exception e)
        {
            Log("collect", e);
            output.Clear();
            return 0;
        }
        finally
        {
            Roster.Clear();
            Eligible.Clear();
        }
    }

    /// <summary>
    /// True when a native Archer.TryRecruit must be refused. Precedence:
    ///  * the one directed call currently in flight is always allowed;
    ///  * a formation that still owes a temporary-type restore (dirty row seat left open) refuses
    ///    every Archer, even with the musketeer feature switched off, until the restore lands or
    ///    the array is confirmed to belong to another owner;
    ///  * while a directed transaction is armed for the formation, every other Archer is refused;
    ///  * crossbowmen are kept out of every native seat no matter what the musketeer feature or
    ///    row says: their only way into a managed banner is the owner's directed crossbow seat
    ///    (2026-10-03 user ruling, superseding the wall-duty-only #17 guard). The directed pair
    ///    above is checked first, so the owner's exact crossbow transaction still passes;
    ///    <see cref="CrossbowmanLifecycle.IsCrossbowman"/> is the live, fail-closed identity read;
    ///  * otherwise only marked musketeers are kept out of the native bow slots of a managed
    ///    formation; ordinary archers, other formations and a disabled feature stay native.
    /// </summary>
    internal static bool ShouldBlockNativeRecruit(Archer archer, Formation formation)
    {
        try
        {
            if (archer == null || formation == null) return false;
            if (IsDirected(archer, formation)) return false;
            if (formation.GetFormationType != Formation.FormationType.PlayerFormation) return false;
            // 2026-10-03 user ruling: crossbowmen still never take a native bow/musketeer seat,
            // but the owner's directed crossbow row is allowed through the IsDirected branch
            // above. The identity is independent of the musketeer feature switch and row, so it
            // is checked before both (IsCrossbowman reads the global switch live and is
            // fail-closed itself).
            if (CrossbowmanLifecycle.IsCrossbowman(archer)) return true;
            if (PatchWorld_FleetBoatFormation.HasDirtyRowTypes(formation)) return true;
            if (HasActiveDirectedTransaction(formation)) return true;
            if (!MusketeerAccess.Enabled) return false;
            if (!PatchWorld_FleetBoatFormation.HasMusketeerRow(formation)) return false;
            return MusketeerIdentity.IsUnit(archer);
        }
        catch
        {
            return false;
        }
    }

    private static void Log(string key, Exception e)
    {
        if (!Logged.Add(key)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[MusketeerFormation] " + key + ": " + e.GetType().Name);
        }
        catch { }
    }

    /// <summary>
    /// The only native entry point the rear row uses. While the directed call is in flight the
    /// guard above lets exactly this archer/formation pair through; all other recruits of marked
    /// musketeers on that formation stay native-refused, and every other crossbowman recruit is
    /// refused too (its only seat is the owner's directed crossbow row).
    /// </summary>
    [HarmonyPatch(typeof(Archer), nameof(Archer.TryRecruit))]
    internal static class ArcherTryRecruitGuard
    {
        [HarmonyPrefix]
        private static bool Prefix(Archer __instance, Formation formation)
        {
            try
            {
                return !ShouldBlockNativeRecruit(__instance, formation);
            }
            catch
            {
                return true;   // never let this policy break a native recruit path
            }
        }
    }
}

/// <summary>
/// Deterministic nearest-first ranking shared by the musketeer and crossbow candidate passes:
/// ascending distance to the formation anchor x, ties broken by GameObject instance id. No
/// allocation and no scene access beyond each candidate's own transform.
/// </summary>
internal sealed class RearRowNearestFirst : IComparer<Archer>
{
    internal float X;

    public int Compare(Archer left, Archer right)
    {
        int byDistance = Distance(left).CompareTo(Distance(right));
        if (byDistance != 0) return byDistance;
        return Id(left).CompareTo(Id(right));
    }

    private float Distance(Archer archer)
    {
        try
        {
            Transform transform = archer != null ? archer.transform : null;
            return transform != null ? Mathf.Abs(transform.position.x - X) : float.MaxValue;
        }
        catch
        {
            return float.MaxValue;
        }
    }

    private static int Id(Archer archer)
    {
        try
        {
            return archer != null && archer.gameObject != null
                ? archer.gameObject.GetInstanceID()
                : int.MaxValue;
        }
        catch
        {
            return int.MaxValue;
        }
    }
}

/// <summary>
/// Pure plan for the fleet owner's composite layout and for one directed recruit transaction.
/// No Unity or IL2CPP calls: <see cref="PatchWorld_FleetBoatFormation"/> turns the plan into live
/// arrays and persists it in its profile. Kept pure so the array order, the rear placement and
/// the temporary transaction types are unit-testable.
/// </summary>
internal static class MusketeerFormationLayout
{
    /// <summary>Seats per rear row. Musketeer and crossbow rows share the shape.</summary>
    internal const int RowSeats = 4;

    /// <summary>
    /// Legacy entry (musketeer row only): delegates to the two-row planner with no crossbow row.
    /// Kept so the existing pure-layout tests and any external single-row caller keep compiling.
    /// </summary>
    internal static bool TryCompose(Formation.UnitTypes[] baselineTypes, int boatCount,
        bool musketeerRow, out Formation.UnitTypes[] types, out int[] boatSlots,
        out int[] musketeerSlots, out int rowLength)
    {
        return TryCompose(baselineTypes, boatCount, musketeerRow, false,
            out types, out boatSlots, out musketeerSlots, out _, out rowLength);
    }

    /// <summary>
    /// Builds the composite array. The requested rear rows are inserted immediately before the
    /// first native Archer slot (so after the two authoring gaps) and typed Squire: no native
    /// IFormationUnit type table and no recruit path matches Squire, and — unlike Gap — an empty
    /// Squire slot does not count in Formation.GetXPosForIndex (game-source Formation.cs:319-332),
    /// so an unfilled row compacts toward the fleet block under the native "empty non-Gap slot"
    /// rule instead of pushing the bow line away. The caller compensates startOffset by exactly
    /// rowLength row steps: a full row leaves every archer-down slot at its old coordinate while
    /// the fleet block moves back by the row length, and the nearest row member is then exactly one
    /// row step from the bow line.
    /// Order is array-index low → high; the native side mirrors it, so physically the crossbow row
    /// ends up behind the musketeer row and both behind the bow line (user order: foot → gap →
    /// archers → musketeers → crossbowmen). The crossbow row is written first because it sits
    /// farther back; when the musketeer row is absent the crossbow row degrades to sit directly
    /// behind the bow line.
    /// Returns false only for a structurally unusable baseline; a requested row that the baseline
    /// cannot carry (no archer slot) yields rowLength 0 with the remaining plan intact.
    /// </summary>
    internal static bool TryCompose(Formation.UnitTypes[] baselineTypes, int boatCount,
        bool musketeerRow, bool crossbowRow, out Formation.UnitTypes[] types, out int[] boatSlots,
        out int[] musketeerSlots, out int[] crossbowSlots, out int rowLength)
    {
        types = null;
        boatSlots = Array.Empty<int>();
        musketeerSlots = Array.Empty<int>();
        crossbowSlots = Array.Empty<int>();
        rowLength = 0;
        if (baselineTypes == null || baselineTypes.Length == 0 || boatCount < 0) return false;

        int fleetSlot = -1;
        int fleetSlots = 0;
        int archerSlots = 0;
        for (int i = 0; i < baselineTypes.Length; i++)
        {
            if (baselineTypes[i] == Formation.UnitTypes.FleetBoat)
            {
                fleetSlot = i;
                fleetSlots++;
            }
            else if (baselineTypes[i] == Formation.UnitTypes.Archer)
            {
                archerSlots++;
            }
        }
        if (fleetSlots != 1) return false;

        int musketeerRowLength = musketeerRow && archerSlots > 0 ? RowSeats : 0;
        int crossbowRowLength = crossbowRow && archerSlots > 0 ? RowSeats : 0;
        rowLength = musketeerRowLength + crossbowRowLength;
        int added = Math.Max(0, boatCount - 1);
        int length = baselineTypes.Length + added + rowLength;
        types = new Formation.UnitTypes[length];
        boatSlots = boatCount > 0 ? new int[boatCount] : Array.Empty<int>();
        musketeerSlots = musketeerRowLength > 0 ? new int[musketeerRowLength] : Array.Empty<int>();
        crossbowSlots = crossbowRowLength > 0 ? new int[crossbowRowLength] : Array.Empty<int>();

        int write = 0;
        bool rowsWritten = false;
        for (int read = 0; read < baselineTypes.Length; read++)
        {
            Formation.UnitTypes type = baselineTypes[read];
            if (rowLength > 0 && !rowsWritten && type == Formation.UnitTypes.Archer)
            {
                rowsWritten = true;
                for (int row = 0; row < crossbowRowLength; row++)
                {
                    crossbowSlots[row] = write;
                    types[write++] = Formation.UnitTypes.Squire;
                }
                for (int row = 0; row < musketeerRowLength; row++)
                {
                    musketeerSlots[row] = write;
                    types[write++] = Formation.UnitTypes.Squire;
                }
            }
            if (read != fleetSlot)
            {
                types[write++] = type;
                continue;
            }
            if (boatCount == 0)
            {
                types[write++] = Formation.UnitTypes.Gap;
                continue;
            }
            for (int boat = 0; boat < boatCount; boat++)
            {
                boatSlots[boat] = write;
                types[write++] = Formation.UnitTypes.FleetBoat;
            }
        }
        return write == length;
    }

    /// <summary>
    /// Temporary unitTypes for one directed recruit. The reserved target slot advertises Archer
    /// (so native RegisterUnit accepts the marked musketeer) while every other empty slot that
    /// would match Archer.FormationUnitType ({ Archer, AnyShieldedUnit } — game-source
    /// Archer.cs:2748) is closed to Gap for the duration of the single synchronous TryRecruit.
    /// Occupied slots keep their type, the input array is never modified, and the caller restores
    /// its own snapshot in a finally whether the recruit succeeded or threw.
    /// </summary>
    internal static bool TryPlanDirected(Formation.UnitTypes[] types, bool[] occupied, int targetSlot,
        out Formation.UnitTypes[] temporary)
    {
        temporary = null;
        if (types == null || occupied == null || types.Length == 0 || types.Length != occupied.Length)
            return false;
        if (targetSlot < 0 || targetSlot >= types.Length || occupied[targetSlot]) return false;

        temporary = new Formation.UnitTypes[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            Formation.UnitTypes type = types[i];
            if (i != targetSlot && !occupied[i]
                && (type == Formation.UnitTypes.Archer || type == Formation.UnitTypes.AnyShieldedUnit))
            {
                type = Formation.UnitTypes.Gap;
            }
            temporary[i] = type;
        }
        temporary[targetSlot] = Formation.UnitTypes.Archer;
        return true;
    }
}
