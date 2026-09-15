using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public readonly Type Target;
        public readonly string Name;

        public HarmonyPatch(Type t, string n)
        {
            Target = t;
            Name = n;
        }
    }
}

namespace Rewired
{
    /// <summary>
    /// Stub of the shipped Rewired_Core interop surface: `public static bool Rewired.ReInput.isReady`
    /// (token 100668624 in both the shipped 2.4 interop and the dev deps interop). The getter is the
    /// only member the guard reads, and it can be made unreadable to model teardown-time failures.
    /// </summary>
    public static class ReInput
    {
        internal static bool Ready = true;
        internal static bool ThrowOnRead;
        internal static int Reads;

        public static bool isReady
        {
            get
            {
                Reads++;
                if (ThrowOnRead)
                    throw new InvalidOperationException("readiness unavailable during teardown (stub)");
                return Ready;
            }
        }
    }
}

/// <summary>
/// Stub of the shipped Menu. Both observed facts are modelled explicitly:
///   Menu.SetMenuInput(bool) body is exactly Menu.ToggleMenuInputLayout(bool)  (game-source Menu.cs:640)
///   that write throws once Rewired is gone -> "Rewired is not initialized" + NRE (Player.log 4741)
/// InputWrites counts writes that reached Rewired; FailedWrites counts the throws of the unguarded path.
/// </summary>
public class Menu
{
    internal static int InputWrites;
    internal static int FailedWrites;

    internal object updateEffect = new object(); // non-null in the shipped Menu
    internal int UpdateEffectStops;
    internal int MaterialReleases;
    internal int HandlerUnsubscribes;

    public void SetMenuInput(bool enabled) => ToggleMenuInputLayout(enabled);

    public static void ToggleMenuInputLayout(bool toggleOn)
    {
        if (!Rewired.ReInput.Ready)
        {
            FailedWrites++;
            throw new NullReferenceException("Rewired not initialized (stub of the Player.log 4741 exit path)");
        }

        InputWrites++;
    }

    /// <summary>
    /// Call-order model of the shipped Menu.OnDisable (game-source Menu.cs:652): the input write first,
    /// then update-effect stop, material release and handler unsubscribe. Harmony contract: a Prefix
    /// returning false skips the original body and the caller continues with the next statement - so a
    /// skipped input write must not stop the cleanup that follows.
    /// </summary>
    public void OnDisableModel()
    {
        GuardHarness.RunSetMenuInput(this, enabled: false);

        if (updateEffect != null)
            UpdateEffectStops++;

        MaterialReleases++;
        HandlerUnsubscribes++;
    }
}

/// <summary>
/// Test-side Harmony stand-in: invokes the real production Prefix by reflection and applies the
/// "false => original body skipped" contract. No game code, no Harmony runtime.
/// </summary>
public static class GuardHarness
{
    private static readonly MethodInfo PrefixMethod =
        typeof(KingdomEnhancedMod.Menu_SetMenuInput_MenuInputGuard_Patch)
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);

    internal static int SkippedBodies;

    internal static bool Decide(bool enabled)
    {
        if (PrefixMethod == null)
            throw new InvalidOperationException("production Prefix not found");
        if (PrefixMethod.ReturnType != typeof(bool) || PrefixMethod.GetParameters().Length != 1 ||
            PrefixMethod.GetParameters()[0].ParameterType != typeof(bool))
            throw new InvalidOperationException("production Prefix has an unexpected signature");

        return (bool)PrefixMethod.Invoke(null, new object[] { enabled });
    }

    internal static bool RunSetMenuInput(Menu menu, bool enabled)
    {
        if (!Decide(enabled))
        {
            SkippedBodies++;
            return false;
        }

        menu.SetMenuInput(enabled);
        return true;
    }
}

namespace KingdomEnhancedMod
{
    /// <summary>Stub of the plugin log surface used by the guard (LogInfo/LogWarning on BepInEx ManualLogSource).</summary>
    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();

        public Logger LogSource = new Logger();

        public class Logger
        {
            public readonly List<string> Lines = new List<string>();

            public void LogInfo(string text)
            {
                Lines.Add(text);
            }

            public void LogWarning(string text)
            {
                Lines.Add(text);
            }
        }
    }
}
