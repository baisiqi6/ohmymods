using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Native Greece ghosts (WarriorGhostLeaderGreece / WarriorGhostGreece) kill
/// themselves once they get farther than _maxPlayerDistance from the summoner
/// (StartDeathCountdown -> DespawnWhenTooFarAway) while their charge AI pushes
/// them away from the campfire every second — a suicide loop that the expanded
/// four-squad Cerberus summon made very visible. Norselands ghosts use the
/// base-class countdown and are intentionally untouched.
///
/// Replace the distance execution with boundary holding + timed expiry:
/// a supervisor coroutine ticks every 0.5s and pins the ghost with
/// Mover.ForceStop + Mover.Pause(0.75f) once |dx| >= _maxPlayerDistance - 1.
/// The pause outlasts the native 1s Charge goal cadence, so a pinned ghost
/// never moves while its FSM keeps slashing/shooting; when the summoner gets
/// back within range the pin stops and native charging resumes. Each ghost is
/// KillUnit()ed 60s after its countdown starts so the Cerberus HasGhosts gate
/// can never be locked forever by undying ghosts.
/// </summary>
public static class PatchDivine_GhostLeashHold
{
    private const float TickSeconds = 0.5f;
    private const float HoldMargin = 1f;
    private const float HoldPauseSeconds = 0.75f;
    private const float LifetimeSeconds = 60f;

    private static bool _loggedFailure;

    internal static IEnumerator Supervise(
        HelsGhost ghost,
        float maxPlayerDistance,
        Func<Mover> moverResolver,
        string kind)
    {
        float expireAt = Time.time + LifetimeSeconds;
        bool loggedHold = false;
        bool loggedError = false;
        while (true)
        {
            yield return new WaitForSeconds(TickSeconds);
            try
            {
                if (ghost == null || ghost.gameObject == null) yield break;

                if (ghost.Summoner == null)
                {
                    if (!loggedError)
                    {
                        loggedError = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                            "[GhostLeashHold] " + kind + " lost its Summoner; killing");
                    }
                    ghost.KillUnit();
                    yield break;
                }

                if (Time.time >= expireAt)
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[GhostLeashHold] " + kind + " expired after "
                        + LifetimeSeconds + "s; killing");
                    ghost.KillUnit();
                    yield break;
                }

                float holdDistance = maxPlayerDistance - HoldMargin;
                float dx = Mathf.Abs(
                    ghost.transform.position.x - ghost.Summoner.transform.position.x);
                if (dx >= holdDistance)
                {
                    Mover mover = moverResolver();
                    if (mover != null && mover.gameObject != null)
                    {
                        mover.ForceStop();
                        mover.Pause(HoldPauseSeconds);
                        if (!loggedHold)
                        {
                            loggedHold = true;
                            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                                "[GhostLeashHold] " + kind + " holding at dx=" + dx
                                + " (limit " + holdDistance + ")");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                        "[GhostLeashHold] " + kind + " supervision failed: " + e);
                }
                if (ghost != null) ghost.KillUnit();
                yield break;
            }
        }
    }

    internal static void LogFailure(string message, Exception exception)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        string text = "[GhostLeashHold] " + message;
        if (exception != null) text += ": " + exception;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError(text);
    }
}

[HarmonyPatch(typeof(WarriorGhostLeaderGreece),
    nameof(WarriorGhostLeaderGreece.StartDeathCountdown))]
public static class WarriorGhostLeaderGreece_StartDeathCountdown_LeashHold_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(WarriorGhostLeaderGreece __instance)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return true;
        try
        {
            __instance.StartCoroutine(PatchDivine_GhostLeashHold.Supervise(
                __instance,
                __instance._maxPlayerDistance,
                () => ResolveMover(__instance),
                "leader").WrapToIl2Cpp());
            return false;
        }
        catch (Exception e)
        {
            // Never leave a ghost without any death countdown: fall back to the
            // native leash execution rather than risking an immortal ghost.
            PatchDivine_GhostLeashHold.LogFailure("leader supervision start failed", e);
            return true;
        }
    }

    private static Mover ResolveMover(WarriorGhostLeaderGreece ghost)
    {
        Mover mover = ghost._mover;
        if (mover == null) mover = ghost.GetComponent<Mover>();
        return mover;
    }
}

[HarmonyPatch(typeof(WarriorGhostGreece),
    nameof(WarriorGhostGreece.StartDeathCountdown))]
public static class WarriorGhostGreece_StartDeathCountdown_LeashHold_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(WarriorGhostGreece __instance)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return true;
        try
        {
            __instance.StartCoroutine(PatchDivine_GhostLeashHold.Supervise(
                __instance,
                __instance._maxPlayerDistance,
                () => ResolveMover(__instance),
                "archer").WrapToIl2Cpp());
            return false;
        }
        catch (Exception e)
        {
            // Never leave a ghost without any death countdown: fall back to the
            // native leash execution rather than risking an immortal ghost.
            PatchDivine_GhostLeashHold.LogFailure("archer supervision start failed", e);
            return true;
        }
    }

    private static Mover ResolveMover(WarriorGhostGreece ghost)
    {
        Mover mover = ghost._mover;
        if (mover == null) mover = ghost.GetComponent<Mover>();
        return mover;
    }
}
