using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Exit-phase guard for Menu.SetMenuInput(false): once Rewired is torn down, the input-map write
/// inside Menu.ToggleMenuInputLayout can only log "Rewired is not initialized" and throw into
/// Menu.OnDisable. Only that dead write is skipped; Menu.OnDisable keeps stopping its update
/// effect, releasing the fullscreen material and unsubscribing its handler. Ready states, the
/// enabled=true OnEnable path and an unreadable readiness flag all stay vanilla.
/// </summary>
internal static class MenuInputGuard
{
    private static int _skips;
    private static int _readFailures;
    private static bool _skipReported;
    private static bool _readFailureReported;

    internal static int Skips => _skips;

    internal static int ReadFailures => _readFailures;

    /// <summary>A proven not-ready close is the only state that skips; anything else runs vanilla.</summary>
    internal static bool ShouldRun(bool enabled, bool isReady) => enabled || isReady;

    /// <summary>Skip the dead input write: one bounded line, no per-frame scanning.</summary>
    internal static bool SkipCloseAndReport()
    {
        _skips++;
        if (!_skipReported)
        {
            _skipReported = true;
            TryLog(
                "[MenuInputGuard] skipped Menu.SetMenuInput(false): Rewired not ready at shutdown; OnDisable cleanup continues",
                warning: false);
        }

        return false;
    }

    /// <summary>An unreadable readiness flag is not proof of not-ready: fail open to vanilla, once.</summary>
    internal static bool FailOpenAfterReadFailure(Exception error)
    {
        _readFailures++;
        if (!_readFailureReported)
        {
            _readFailureReported = true;
            TryLog(
                "[MenuInputGuard] Rewired readiness read failed; running vanilla Menu.SetMenuInput(false). "
                + error.GetType().Name + ": " + error.Message,
                warning: true);
        }

        return true;
    }

    private static void TryLog(string text, bool warning)
    {
        try
        {
            var source = KingdomEnhancedPlugin.Instance?.LogSource;
            if (source == null)
                return;

            if (warning)
                source.LogWarning(text);
            else
                source.LogInfo(text);
        }
        catch
        {
            // A logging failure must never change the exit-phase decision.
        }
    }
}

/// <summary>
/// Prefix only; Menu.OnDisable itself is never hooked, so its animation/material/event cleanup
/// always runs. Managers.PrepareUnload scope is irrelevant: the real OnDisable runs after it ends.
/// </summary>
[HarmonyPatch(typeof(Menu), "SetMenuInput")]
internal static class Menu_SetMenuInput_MenuInputGuard_Patch
{
    private static bool Prefix(bool enabled)
    {
        if (enabled)
            return true; // OnEnable path: vanilla, and no Rewired probe during teardown

        bool isReady;
        try
        {
            isReady = Rewired.ReInput.isReady;
        }
        catch (Exception error)
        {
            return MenuInputGuard.FailOpenAfterReadFailure(error);
        }

        if (MenuInputGuard.ShouldRun(enabled, isReady))
            return true;

        return MenuInputGuard.SkipCloseAndReport();
    }
}
