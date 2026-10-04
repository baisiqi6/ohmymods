// Minimal typed boundary doubles for compiling the actual android/PatchRide_SteedCooldown.cs
// into this host scenario project. Shapes mirror only what the patch consumes:
//   - Il2Cpp.SteedAbility + the four registered consumer types. TryCast models Il2CppInterop's
//     native-class identity check (NOT managed `is`): a base-typed proxy whose simulated native
//     identity is SummonGhostSteedAbility returns a ghost proxy, so a managed `is` check inside
//     the patch could never pass the ghost scenario.
//   - `_cooldown` is the native float field with counted, injectable get/set failures; `Seed`
//     and `Raw` write/read it without touching the counters (external/native observers).
//   - `Pointer` is the interop proxy pointer with an injectable read failure, so the prefix's
//     CLR failure boundary (owner check / owner write) can be exercised without claiming
//     anything about real proxy lifetimes.
//   - KingdomEnhancedMod.ModConfig mirrors the two MobilePlayerConfig members the patch reads
//     (non-persisted session Setting<bool> + MelonPreferences_Entry<float> multiplier).
// These doubles record consumption shapes and setter counts; they never reimplement the
// Prefix/End algorithm and they are not native evidence.
using System;

namespace Il2Cpp
{
    internal class SteedAbility
    {
        // Simulated native class identity (TryCast reads the native class in Il2CppInterop).
        internal Type NativeType = typeof(SteedAbility);
        private IntPtr pointer = new IntPtr(1);
        internal bool ThrowOnPointerGet;

        internal IntPtr Pointer
        {
            get
            {
                if (ThrowOnPointerGet) throw new InvalidOperationException("stub pointer get failure");
                return pointer;
            }
            set => pointer = value;
        }

        private float cooldown = 1f;
        internal int GetterCalls;
        internal int SetterCalls;
        internal bool ThrowOnGetter;
        internal readonly System.Collections.Generic.HashSet<int> FailBeforeWrite = new System.Collections.Generic.HashSet<int>();
        internal readonly System.Collections.Generic.HashSet<int> FailAfterWrite = new System.Collections.Generic.HashSet<int>();

        internal void Seed(float value) => cooldown = value;
        internal float Raw => cooldown;

        internal float _cooldown
        {
            get
            {
                GetterCalls++;
                if (ThrowOnGetter) throw new InvalidOperationException("stub getter failure");
                return cooldown;
            }
            set
            {
                SetterCalls++;
                if (FailBeforeWrite.Contains(SetterCalls)) throw new InvalidOperationException("stub setter failure before write");
                cooldown = value;
                if (FailAfterWrite.Contains(SetterCalls)) throw new InvalidOperationException("stub setter failure after write");
            }
        }

        internal T TryCast<T>() where T : SteedAbility
        {
            if (NativeType != typeof(T)) return null;
            var proxy = (T)Activator.CreateInstance(typeof(T));
            proxy.Pointer = Pointer;
            proxy.NativeType = NativeType;
            return proxy;
        }
    }

    internal class BuffUnitsSteedAbility : SteedAbility { }
    internal class GlideMovementSteedAbility : SteedAbility { }
    internal class SpeedBoostSteedAbility : SteedAbility { }
    internal class SummonGhostSteedAbility : SteedAbility { }
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
        internal static readonly Setting<bool> Enabled = new Setting<bool>(true);
        internal static MelonLoader.MelonPreferences_Entry<float> SteedCooldownMultiplier =
            new MelonLoader.MelonPreferences_Entry<float>(1f);
    }
}
