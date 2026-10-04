// Minimal typed boundary doubles for compiling the actual android/PatchDivine_StaffCooldown.cs,
// android/MobilePlayerMenu.cs, android/MobilePlayerConfig.cs and the real HoldBridges logger
// bridge into this host scenario project. Shapes mirror only what those files consume:
//   - Il2Cpp.ItemOfPower / Il2Cpp.HermesStaff: one shared native cooldown slot per object, so a
//     TryCast proxy models Il2CppInterop's "same native object, new managed wrapper" identity
//     (the patch writes through the cast result; the test observes the source object). TryCast
//     matches on the simulated native class type, never managed `is`.
//   - The nested HermesStaff._StartAbilityRoutine_d__17 state machine with the audited
//     __1__state / __4__this properties; __4__this can be made to throw so a test can prove the
//     state gate is read before the owner.
//   - `_itemCooldown` counts getters/setters and can fail before/after the store, so the
//     prefix/cleanup failure and exception-identity boundaries can be exercised without
//     claiming anything about real proxy lifetimes.
//   - UnityEngine GUI/Rect/GUIStyle doubles record label/button calls (rect + text) and return
//     a programmed click, so the Player menu rows can be driven without a Unity runtime.
//   - OhMyMods.AndroidProbe.ProbeTicker carries the real FloatLayout, mirroring the production
//     ticker's static Layout the menu reads for the Back row.
// These doubles never reimplement the Prefix/End or Cycle algorithm and are not native evidence.
using System;
using System.Collections.Generic;

namespace Il2Cpp
{
    internal sealed class ItemCooldownSlot
    {
        internal float Value = 1f;
        internal int GetterCalls;
        internal int SetterCalls;
        internal bool ThrowOnGetter;
        internal Exception GetterException;
        internal readonly HashSet<int> FailBeforeWrite = new HashSet<int>();
        internal readonly HashSet<int> FailAfterWrite = new HashSet<int>();
    }

    internal class ItemOfPower
    {
        // Simulated native object storage: every cast proxy of the same native object shares it.
        internal ItemCooldownSlot Slot = new ItemCooldownSlot();

        // Simulated native class identity (TryCast reads the native class in Il2CppInterop).
        internal Type NativeType = typeof(ItemOfPower);
        internal int TryCastCalls;
        internal bool ThrowOnTryCast;

        internal void Seed(float value) => Slot.Value = value;
        internal float Raw => Slot.Value;
        internal int GetterCalls => Slot.GetterCalls;
        internal int SetterCalls => Slot.SetterCalls;
        internal bool ThrowOnGetter { get => Slot.ThrowOnGetter; set => Slot.ThrowOnGetter = value; }
        internal Exception GetterException { get => Slot.GetterException; set => Slot.GetterException = value; }
        internal HashSet<int> FailBeforeWrite => Slot.FailBeforeWrite;
        internal HashSet<int> FailAfterWrite => Slot.FailAfterWrite;

        internal float _itemCooldown
        {
            get
            {
                Slot.GetterCalls++;
                if (Slot.ThrowOnGetter) throw Slot.GetterException ?? new InvalidOperationException("stub item cooldown getter failure");
                return Slot.Value;
            }
            set
            {
                Slot.SetterCalls++;
                if (Slot.FailBeforeWrite.Contains(Slot.SetterCalls)) throw new InvalidOperationException("stub item cooldown setter failure before write");
                Slot.Value = value;
                if (Slot.FailAfterWrite.Contains(Slot.SetterCalls)) throw new InvalidOperationException("stub item cooldown setter failure after write");
            }
        }

        internal T TryCast<T>() where T : ItemOfPower
        {
            TryCastCalls++;
            if (ThrowOnTryCast) throw new InvalidOperationException("stub TryCast failure");
            if (NativeType != typeof(T)) return null;
            var proxy = (T)Activator.CreateInstance(typeof(T));
            proxy.Slot = Slot; // the cast wrapper shares the same native object
            proxy.NativeType = NativeType;
            return proxy;
        }
    }

    internal class HermesStaff : ItemOfPower
    {
        // Shape of the compiler-generated state machine the MoveNext prefix patches. The state
        // and owner getters count accesses and can throw, so a test can prove the config
        // short-circuit never touches them.
        internal sealed class _StartAbilityRoutine_d__17
        {
            internal bool ThrowOnStateGet;
            internal bool ThrowOnOwnerGet;
            internal int StateReads;
            internal int OwnerReads;
            private int state;
            private HermesStaff owner;

            internal int __1__state
            {
                get
                {
                    StateReads++;
                    if (ThrowOnStateGet) throw new InvalidOperationException("stub state machine state read failure");
                    return state;
                }
                set => state = value;
            }

            internal HermesStaff __4__this
            {
                get
                {
                    OwnerReads++;
                    if (ThrowOnOwnerGet) throw new InvalidOperationException("stub state machine owner read failure");
                    return owner;
                }
                set => owner = value;
            }
        }
    }
}

namespace UnityEngine
{
    internal struct Rect
    {
        internal float X, Y, Width, Height;
        internal Rect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    }

    internal class GUIStyle
    {
    }

    internal static class GUI
    {
        internal sealed class Record
        {
            internal Rect Rect;
            internal string Text;
            internal bool IsButton;
        }

        internal static readonly List<Record> Records = new List<Record>();

        // When set, a Button whose text equals this returns true (a scripted click).
        internal static string ClickText;

        internal static void Reset()
        {
            Records.Clear();
            ClickText = null;
        }

        internal static void Label(Rect rect, string text, GUIStyle style)
            => Records.Add(new Record { Rect = rect, Text = text });

        internal static bool Button(Rect rect, string text, GUIStyle style)
        {
            Records.Add(new Record { Rect = rect, Text = text, IsButton = true });
            return ClickText != null && text == ClickText;
        }
    }
}

namespace OhMyMods.AndroidProbe
{
    // The production ticker's static layout the menu reads for Back; the real FloatLayout is
    // linked into this project so the flag settles on the actual geometry object.
    internal static class ProbeTicker
    {
        internal static readonly FloatLayout Layout = new FloatLayout();
    }
}
