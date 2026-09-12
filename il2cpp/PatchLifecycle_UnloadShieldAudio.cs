using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Suppresses only the shield pickup sound while Managers synchronously disables the
/// level hierarchy. NpcShieldUser's native cleanup still owns all shield state, events,
/// regen, formation, bump-force, and RPC work.
/// </summary>
internal static class UnloadShieldAudioScope
{
    internal sealed class ScopeState
    {
        internal bool Began;
        internal bool Ended;
    }

    private static int _depth;
    private static int _suppressed;
    private static int _restoreFailures;

    internal static bool IsActive => _depth > 0;

    internal static ScopeState Begin()
    {
        var state = new ScopeState { Began = true };
        if (_depth++ == 0)
        {
            _suppressed = 0;
            _restoreFailures = 0;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[CrashGuard] unload shield-audio guard active");
        }

        return state;
    }

    internal static void CountSuppressed() => _suppressed++;

    internal static void CountRestoreFailure() => _restoreFailures++;

    internal static void End(ScopeState state)
    {
        if (state == null || !state.Began || state.Ended)
            return;

        state.Ended = true;
        if (_depth > 0)
            _depth--;

        if (_depth == 0)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[CrashGuard] unload shield-audio guard complete: suppressed="
                + _suppressed + " restoreFailures=" + _restoreFailures);
        }
    }

    internal static void ResetStaleScope()
    {
        if (_depth == 0)
            return;

        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[CrashGuard] cleared stale unload shield-audio scope: depth=" + _depth
            + " suppressed=" + _suppressed + " restoreFailures=" + _restoreFailures);
        _depth = 0;
        _suppressed = 0;
        _restoreFailures = 0;
    }
}

[HarmonyPatch(typeof(Managers), nameof(Managers.PrepareUnload))]
internal static class Managers_PrepareUnload_ShieldAudio_Patch
{
    [HarmonyPrefix]
    private static void Prefix(out UnloadShieldAudioScope.ScopeState __state)
    {
        __state = null;
        if (ModConfig.Enabled.Value)
            __state = UnloadShieldAudioScope.Begin();
    }

    [HarmonyPostfix]
    private static void Postfix(UnloadShieldAudioScope.ScopeState __state)
    {
        UnloadShieldAudioScope.End(__state);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(
        Exception __exception,
        UnloadShieldAudioScope.ScopeState __state)
    {
        UnloadShieldAudioScope.End(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Managers), nameof(Managers.OnLevelLoaded))]
internal static class Managers_OnLevelLoaded_ShieldAudio_Patch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        UnloadShieldAudioScope.ResetStaleScope();
    }
}

[HarmonyPatch(typeof(NpcShieldUser), nameof(NpcShieldUser.SetShieldEnabled))]
internal static class NpcShieldUser_SetShieldEnabled_UnloadAudio_Patch
{
    internal sealed class SoundState
    {
        internal bool Applied;
        internal bool Restored;
        internal AudioEmitter Sound;
    }

    [HarmonyPrefix]
    private static void Prefix(NpcShieldUser __instance, bool enabled, out SoundState __state)
    {
        __state = null;
        if (enabled || !UnloadShieldAudioScope.IsActive || __instance == null)
            return;

        try
        {
            // Only Worker shields are introduced/equipped by this mod. Native shield users
            // elsewhere retain their exact unload behavior.
            if (__instance.GetComponent<Worker>() == null || !__instance.HasShield())
                return;

            AudioEmitter sound = __instance.pickupShieldSound;
            if (sound == null)
                return;

            __state = new SoundState
            {
                Applied = true,
                Sound = sound
            };
            __instance.pickupShieldSound = null;
            UnloadShieldAudioScope.CountSuppressed();
        }
        catch
        {
            // Fail open: preserve native teardown if the IL2CPP object is already invalid.
            // Keep an already-created state so the finalizer can still attempt restoration
            // if the field write succeeded before an interop exception surfaced.
        }
    }

    [HarmonyPostfix]
    private static void Postfix(NpcShieldUser __instance, SoundState __state)
    {
        Restore(__instance, __state);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(
        Exception __exception,
        NpcShieldUser __instance,
        SoundState __state)
    {
        Restore(__instance, __state);
        return __exception;
    }

    private static void Restore(NpcShieldUser instance, SoundState state)
    {
        if (state == null || !state.Applied || state.Restored)
            return;

        state.Restored = true;
        try
        {
            if (instance != null)
                instance.pickupShieldSound = state.Sound;
        }
        catch
        {
            UnloadShieldAudioScope.CountRestoreFailure();
        }
    }
}
