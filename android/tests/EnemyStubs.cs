// Host doubles for compiling the unmodified production source
// il2cpp/PatchWorld_EnemyManager.cs into this test project (see AdapterTests.csproj). Only the
// surface that source actually consumes is declared, and each shape mirrors the real one:
//   - HarmonyLib.HarmonyPatch / HarmonyPrefix attribute forms used by the source;
//   - UnityEngine.Mathf.RoundToInt with Unity's ties-to-even midpoint rule;
//   - bare EnemyManager / Wave mapped exactly like GlobalAliases.cs maps them for the Android
//     build (Il2Cpp.*); they only ever appear in typeof(...) here and are never constructed;
//   - KingdomEnhancedMod.KingdomEnhancedPlugin / Logger mirroring the HoldBridges.cs shapes,
//     including LogError(object). The production csproj compiles the same source against the
//     real bridge, so a signature drift fails that build; this double stays shape-identical.
// These are host doubles, not native evidence and not the real game types.
global using EnemyManager = Il2Cpp.EnemyManager;
global using Wave = Il2Cpp.Wave;

using System;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type declaringType) { }
        public HarmonyPatch(string methodName) { }
        public HarmonyPatch(string methodName, Type[] argumentTypes) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }
}

namespace UnityEngine
{
    internal static class Mathf
    {
        internal static int RoundToInt(float f) => (int)MathF.Round(f);
    }
}

namespace Il2Cpp
{
    internal sealed class EnemyManager { }
    internal sealed class Wave { }
}

namespace KingdomEnhancedMod
{
    // Shape double of HoldBridges.cs. The suite never calls Initialize, so Instance stays null
    // (the linked source only calls Instance?.LogSource.LogError(e) from its catch path, which
    // this suite does not force); the method exists to mirror the real bridge.
    internal sealed class KingdomEnhancedPlugin
    {
        private static KingdomEnhancedPlugin instance;
        private readonly Logger logSource = new Logger();

        internal static KingdomEnhancedPlugin Instance => instance;

        internal Logger LogSource => logSource;

        internal static void Initialize()
        {
            if (instance != null) return;
            instance = new KingdomEnhancedPlugin();
        }
    }

    internal sealed class Logger
    {
        internal void LogWarning(string message) => MelonLoader.MelonLogger.Warning(message);
        internal void LogInfo(string message) => MelonLoader.MelonLogger.Msg(message);
        internal void LogError(object message) => MelonLoader.MelonLogger.Error(message.ToString());
    }
}
