using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Night-volley performance probe.  The defense-depth clamps put every archer
/// inside bow range, so volley sizes can exceed the native peak by several
/// times; each Arrow carries a Rigidbody2D, a trigger collider and a
/// TrailRenderer, and the fixed shoot cooldown synchronises volleys into
/// spikes.  Sample concurrent arrows plus frame times and log only when it
/// matters (many arrows or slow frames), deduplicated by coarse bands.
///
/// Hosted on a World coroutine (per-frame yields): Director.Update proved
/// unhookable in 2.4.0 (AOT inlining), so the original postfix never ran.
/// </summary>
public static class PatchPerformance_NightVolley
{
    private const float SampleInterval = 15f;
    private const int ArrowLogThreshold = 30;
    private const float SlowFrameMs = 25f;

    private static IntPtr _probeWorld;
    private static float _nextSampleAt;
    private static float _frameSum;
    private static float _frameMax;
    private static int _frameCount;
    private static System.Collections.Generic.HashSet<string> _loggedBands
        = new System.Collections.Generic.HashSet<string>();

    internal static IEnumerator ProbeRoutine(World world)
    {
        if (world == null || _probeWorld == world.Pointer) yield break;
        _probeWorld = world.Pointer;
        _loggedBands.Clear();
        _nextSampleAt = 0f;
        while (world != null && world.gameObject != null)
        {
            yield return null;
            if (!ModConfig.Enabled.Value) continue;

            float dt = Time.deltaTime;
            if (dt > 0f && dt < 1f)
            {
                _frameSum += dt;
                _frameCount++;
                if (dt > _frameMax) _frameMax = dt;
            }

            float now = Time.unscaledTime;
            if (_nextSampleAt == 0f) { _nextSampleAt = now + SampleInterval; continue; }
            if (now < _nextSampleAt) continue;
            _nextSampleAt = now + SampleInterval;

            try { EmitSample(); }
            catch (Exception e)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DefensePerf] " + e);
            }
        }
    }

    // The staggered-volley design targets night wall defense only; daytime
    // wildlife hunting never masses volleys, so skip its windows entirely
    // (also saves the FindObjectsOfType scan during the day).
    private static bool IsNightWindow()
    {
        Director director = Managers.Inst != null ? Managers.Inst.director : null;
        if (director == null) return false;
        float t = director.currentTime;
        return t >= 17.5f || t <= 5.5f;
    }

    private static void EmitSample()
    {
        float avgMs = _frameCount > 0 ? _frameSum / _frameCount * 1000f : 0f;
        float maxMs = _frameMax * 1000f;
        _frameSum = 0f;
        _frameMax = 0f;
        _frameCount = 0;
        if (avgMs <= 0f) return;
        if (!IsNightWindow()) return;

        Arrow[] arrows = UnityEngine.Object.FindObjectsOfType<Arrow>();
        int arrowCount = arrows != null ? arrows.Length : 0;
        if (arrowCount < ArrowLogThreshold && maxMs < SlowFrameMs) return;

        float timeOfDay = Managers.Inst != null && Managers.Inst.director != null
            ? Managers.Inst.director.currentTime : -1f;
        string band = (arrowCount / 30) + "|" + (int)(maxMs / 10f);
        if (!_loggedBands.Add(band)) return;

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefensePerf] arrows=" + arrowCount
            + " avgFrame=" + avgMs.ToString("F1") + "ms"
            + " maxFrame=" + maxMs.ToString("F1") + "ms"
            + " t=" + timeOfDay.ToString("F1") + "h");
    }
}

[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_NightVolley_Probe_Host_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try
        {
            __instance.StartCoroutine(
                PatchPerformance_NightVolley.ProbeRoutine(__instance).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefensePerf] probe start failed: " + e);
        }
    }
}
