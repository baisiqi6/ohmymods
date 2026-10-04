// Minimal typed boundary doubles for compiling the actual android/PatchWorld_BoatCapacity.cs
// (and the shared il2cpp/BoatCapacityProfile.cs) into this host scenario project. Shapes mirror
// only what the patch consumes:
//   - Il2Cpp.Boat with the four int max properties recorded from the Android interop
//     (get_maxWorkers/set_maxWorkers, ...), each backed by an independent field cell with
//     counted, injectable get/set failures: `Seed`/`Raw` write/read without touching the
//     counters (external/native observers), `FailBeforeWrite`/`FailAfterWrite` keyed by that
//     field's setter-call index model the before/after-write throw boundary, `ThrowOnGetter`
//     models a throwing interop read.
//   - KingdomEnhancedMod.ModConfig mirrors the two MobilePlayerConfig members the patch reads
//     (non-persisted session Setting<bool> + the persisted MelonPreferences_Entry<bool>).
// The doubles record consumption shapes only; they do not model Embarkable/RegisterUnitSlots,
// slot materialization or any Unity lifecycle, and they are not native evidence.
using System;
using System.Collections.Generic;

namespace Il2Cpp
{
    internal class Boat
    {
        internal sealed class Field
        {
            private int value;
            internal int GetterCalls;
            internal int SetterCalls;
            internal bool ThrowOnGetter;
            internal readonly HashSet<int> FailBeforeWrite = new HashSet<int>();
            internal readonly HashSet<int> FailAfterWrite = new HashSet<int>();

            internal void Seed(int external) => value = external;
            internal int Raw => value;

            internal int Read()
            {
                GetterCalls++;
                if (ThrowOnGetter) throw new InvalidOperationException("stub getter failure");
                return value;
            }

            internal void Write(int applied)
            {
                SetterCalls++;
                if (FailBeforeWrite.Contains(SetterCalls)) throw new InvalidOperationException("stub setter failure before write");
                value = applied;
                if (FailAfterWrite.Contains(SetterCalls)) throw new InvalidOperationException("stub setter failure after write");
            }
        }

        internal readonly Field Workers = new Field();
        internal readonly Field Knights = new Field();
        internal readonly Field Pikemen = new Field();
        internal readonly Field Farmers = new Field();

        internal int maxWorkers { get => Workers.Read(); set => Workers.Write(value); }
        internal int maxKnights { get => Knights.Read(); set => Knights.Write(value); }
        internal int maxPikemen { get => Pikemen.Read(); set => Pikemen.Write(value); }
        internal int maxFarmers { get => Farmers.Read(); set => Farmers.Write(value); }
    }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
        internal static readonly Setting<bool> Enabled = new Setting<bool>(true);
        internal static MelonLoader.MelonPreferences_Entry<bool> BoatCapacityEnabled =
            new MelonLoader.MelonPreferences_Entry<bool>(false);
    }
}
