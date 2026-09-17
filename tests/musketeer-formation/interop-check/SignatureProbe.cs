using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// Compile-only probe for the fleet-side native surface this slice touches from
    /// PatchWorld_FleetBoatFormation.cs (which cannot be compiled here because it also needs the
    /// fleet/Greek collaborators). Mirrors the production access pattern — array indexer
    /// reads/writes, the startOffset write, the enum values and the native TryRecruit call — so a
    /// 2.4 interop signature drift fails this build instead of the game.
    /// </summary>
    internal static class MusketeerFormationFleetSignatureProbe
    {
        internal static bool Probe(Formation formation, Archer archer, out int freeSlots)
        {
            freeSlots = 0;
            if (formation == null) return false;

            float baseline = formation.startOffset;
            Il2CppStructArray<Formation.UnitTypes> types = formation.unitTypes;
            Il2CppReferenceArray<Formation.IFormationUnit> units = formation.units;
            if (types == null || units == null || types.Length != units.Length) return false;

            // Array identity, used by the transaction to avoid writing a snapshot into an array
            // another owner replaced.
            IntPtr typesPointer = types.Pointer;
            IntPtr unitsPointer = units.Pointer;
            if (typesPointer == unitsPointer) freeSlots = -1;

            for (int i = 0; i < types.Length; i++)
            {
                if (units[i] == null) freeSlots++;
            }

            Formation.UnitTypes gap = Formation.UnitTypes.Gap;
            Formation.UnitTypes archerType = Formation.UnitTypes.Archer;
            types[0] = gap;
            types[1] = archerType;
            formation.startOffset = baseline;
            formation.unitTypes = types;
            formation.units = units;
            if (formation.GetFormationType != Formation.FormationType.PlayerFormation) return false;

            bool online = NetworkBigBoss.IsOnline;
            if (online) return false;
            if (archer == null) return false;
            if (!archer.gameObject.TryGetComponent<Archer>(out Archer found) || found == null) return false;
            Damageable damageable = archer._damageable;
            if (damageable == null || damageable.isDead) return false;
            archer.OnLeaveFormation();
            return archer.TryRecruit(formation);
        }
    }
}
