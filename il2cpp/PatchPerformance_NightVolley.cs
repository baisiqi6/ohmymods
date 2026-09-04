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
///
/// 天亮顿挫诊断扩展：用户报告"每天天刚亮时卡很久"，怀疑是原生每日自动存档
/// 在天亮时刻触发的一次性长顿挫。为此在夜间探针之外新增独立的天亮采样窗口
/// （累计区间 5.5<t<7.0），用单独一套累计器量化该时段的 avg/max 帧耗时，
/// 两套累计器互不污染；夜间探针（>=17.5 或 <=5.5）的口径与日志完全不变。
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
    // 天亮窗口独立累计器（与上方夜间累计器互不污染）：
    // 诊断每日天亮顿挫（疑原生自动存档）的量化探针专用，
    // 仅在 5.5<t<7.0 时段的帧会计入这里。
    private static float _dawnFrameSum;
    private static float _dawnFrameMax;
    private static int _dawnFrameCount;
    // Low-frequency state probe for reports that workers stay in night posture
    // and enemy waves arrive late.  This is diagnostics only: it never writes
    // Director/Kingdom time or changes the native scheduler.
    private static float _nextClockSampleAt;
    private static bool _lastClockNight;
    private static bool _lastClockDaytime;
    private static int _lastClockDay = -1;
    private static bool _clockStateInitialized;
    private static System.Collections.Generic.HashSet<string> _loggedBands
        = new System.Collections.Generic.HashSet<string>();

    internal static IEnumerator ProbeRoutine(World world)
    {
        if (world == null || _probeWorld == world.Pointer) yield break;
        _probeWorld = world.Pointer;
        _loggedBands.Clear();
        _nextSampleAt = 0f;
        _dawnFrameSum = 0f;
        _dawnFrameMax = 0f;
        _dawnFrameCount = 0;
        _nextClockSampleAt = 0f;
        _clockStateInitialized = false;
        _lastClockDay = -1;
        while (world != null && world.gameObject != null)
        {
            yield return null;
            if (!ModConfig.Enabled.Value) continue;

            EmitClockSample();

            float dt = Time.deltaTime;
            if (dt > 0f && dt < 1f)
            {
                _frameSum += dt;
                _frameCount++;
                if (dt > _frameMax) _frameMax = dt;

                // 天亮窗口累计：夜间窗口先判，5.0~5.5 重叠期归夜间探针，
                // 只有 5.5<t<7.0 的帧计入 dawn 累计器，两套累计器互不污染。
                if (!IsNightWindow() && IsDawnWindow())
                {
                    _dawnFrameSum += dt;
                    _dawnFrameCount++;
                    if (dt > _dawnFrameMax) _dawnFrameMax = dt;
                }
            }

            float now = Time.unscaledTime;
            if (_nextSampleAt == 0f) { _nextSampleAt = now + SampleInterval; continue; }
            if (now < _nextSampleAt) continue;
            _nextSampleAt = now + SampleInterval;

            try { EmitSample(); EmitDawnSample(); }
            catch (Exception e)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DefensePerf] " + e);
            }
        }
    }

    /// <summary>
    /// Emits a bounded, state-change/30-second snapshot of the native clock.
    /// The three values are intentionally logged together because peasants use
    /// Kingdom.isDaytime while serpent/enemy scheduling uses Director.IsNight.
    /// A mismatch or a frozen currentTime is actionable evidence; changing any
    /// of them here would corrupt save, farming, and wave scheduling.
    /// </summary>
    private static void EmitClockSample()
    {
        Managers managers = Managers.Inst;
        Director director = managers != null ? managers.director : null;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        if (director == null || kingdom == null) return;

        float now = Time.unscaledTime;
        bool isNight = director.IsNight;
        bool isDaytime = kingdom.isDaytime;
        int islandDays = director.CurrentIslandDays;
        bool changed = !_clockStateInitialized
            || isNight != _lastClockNight
            || isDaytime != _lastClockDaytime
            || islandDays != _lastClockDay;
        if (!changed && now < _nextClockSampleAt) return;

        _clockStateInitialized = true;
        _lastClockNight = isNight;
        _lastClockDaytime = isDaytime;
        _lastClockDay = islandDays;
        _nextClockSampleAt = now + 30f;

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[ClockDiag] t=" + director.currentTime.ToString("F2")
            + " isNight=" + isNight
            + " kingdomDaytime=" + isDaytime
            + " islandDays=" + islandDays
            + " timeScale=" + Time.timeScale.ToString("F2"));
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

    // 天亮窗口判定（5.0~7.0）：诊断每日天亮顿挫（疑原生自动存档）的量化探针。
    // 与夜间窗口在 5.0~5.5 重叠；累计时夜间判定优先，重叠期归夜间探针，
    // 因此 dawn 累计器实际只在 5.5<t<7.0 生效。
    private static bool IsDawnWindow()
    {
        Director director = Managers.Inst != null ? Managers.Inst.director : null;
        if (director == null) return false;
        float t = director.currentTime;
        return t >= 5.0f && t <= 7.0f;
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

    // 天亮采样输出：与夜间探针共用 15s 采样节拍，但累计与输出完全独立。
    // 天亮没有箭雨，不需要 arrows 计数；也不做阈值过滤与频段去重——
    // 窗口很短（约 1.5 游戏小时），每 15s 一行直接输出，用来量化
    // 每日天亮顿挫（疑原生自动存档）的 avg/max 帧幅度。
    private static void EmitDawnSample()
    {
        float timeOfDay = Managers.Inst != null && Managers.Inst.director != null
            ? Managers.Inst.director.currentTime : -1f;

        // 窗口结束（t>=7.0）：清零 dawn 累计器（丢弃跨窗残留），等明天再累计。
        if (timeOfDay >= 7.0f)
        {
            _dawnFrameSum = 0f;
            _dawnFrameMax = 0f;
            _dawnFrameCount = 0;
            return;
        }

        if (_dawnFrameCount == 0) return;
        float avgMs = _dawnFrameSum / _dawnFrameCount * 1000f;
        float maxMs = _dawnFrameMax * 1000f;
        _dawnFrameSum = 0f;
        _dawnFrameMax = 0f;
        _dawnFrameCount = 0;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[DefensePerf] dawn: avgFrame=" + avgMs.ToString("F1") + "ms"
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
