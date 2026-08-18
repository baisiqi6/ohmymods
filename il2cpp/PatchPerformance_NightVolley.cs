using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Night-volley performance probe.  The defense-depth clamps put every archer
/// inside bow range, so volley sizes can exceed the native peak by several
/// times; each Arrow carries a Rigidbody2D, a trigger collider and a
/// TrailRenderer, and the fixed shoot cooldown synchronises volleys into
/// spikes.  Sample concurrent arrows plus frame times on a slow cadence and
/// log only when it matters (many arrows or slow frames), deduplicated by
/// coarse bands so long nights do not flood the log.
/// </summary>
public static class PatchPerformance_NightVolley
{
    private const float SampleInterval = 15f;
    private const int ArrowLogThreshold = 30;
    private const float SlowFrameMs = 25f;

    private static float _nextSampleAt;
    private static float _frameSum;
    private static float _frameMax;
    private static int _frameCount;
    private static readonly HashSet<string> LoggedBands = new();

    [HarmonyPatch(typeof(Director), "Update")]
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            float now = Time.unscaledTime;
            if (_nextSampleAt == 0f)
            {
                _nextSampleAt = now + SampleInterval;
                return;
            }

            float dt = Time.deltaTime;
            if (dt > 0f && dt < 1f)
            {
                _frameSum += dt;
                _frameCount++;
                if (dt > _frameMax) _frameMax = dt;
            }

            if (now < _nextSampleAt) return;
            _nextSampleAt = now + SampleInterval;

            float avgMs = _frameCount > 0 ? _frameSum / _frameCount * 1000f : 0f;
            float maxMs = _frameMax * 1000f;
            _frameSum = 0f;
            _frameMax = 0f;
            _frameCount = 0;
            if (avgMs <= 0f) return;

            // FindObjectsOfType is not free; the 15s cadence keeps it negligible.
            Arrow[] arrows = UnityEngine.Object.FindObjectsOfType<Arrow>();
            int arrowCount = arrows != null ? arrows.Length : 0;
            if (arrowCount < ArrowLogThreshold && maxMs < SlowFrameMs) return;

            float timeOfDay = Managers.Inst != null && Managers.Inst.director != null
                ? Managers.Inst.director.currentTime : -1f;
            string band = (arrowCount / 30) + "|" + (int)(maxMs / 10f);
            if (!LoggedBands.Add(band)) return;

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[DefensePerf] arrows=" + arrowCount
                + " avgFrame=" + avgMs.ToString("F1") + "ms"
                + " maxFrame=" + maxMs.ToString("F1") + "ms"
                + " t=" + timeOfDay.ToString("F1") + "h");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DefensePerf] " + e);
        }
    }
}
