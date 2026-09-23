using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 帧耗时看门狗（2026-09-24 玩家报告两次明显卡顿、日志无错误，需帧级证据定位）。
/// 每帧读 unscaledDeltaTime：超过 100ms 且过了启动暖机（600 帧）记一行，带当时上下文——
/// GC 代际收集数（卡顿=分配压力的铁证）、面板/暂停/武士冲刺状态。有界：会话 ≤16 行、
/// 行间 ≥2s；&gt;1s 的帧大概率是切屏/失焦，照记但标注。纯只读诊断，不改任何行为。
/// 对抗审查豁免依据（边界规则）：logging-only、只读、有界，先例=守家图腾候选诊断（PR #36）。
/// </summary>
internal static class PatchDiag_FrameWatch
{
    private const float ThresholdSeconds = 0.1f;
    private const int WarmupFrames = 600, MaxLines = 16;
    private const float MinGapSeconds = 2f;

    private static int _lines;
    private static float _lastLineAt = -10f;
    private static int _lastGen0, _lastGen1;

    internal static void Tick()
    {
        try
        {
            if (_lines >= MaxLines || Time.frameCount < WarmupFrames) return;
            float dt = Time.unscaledDeltaTime;
            if (dt < ThresholdSeconds) return;
            float now = Time.unscaledTime;
            if (now - _lastLineAt < MinGapSeconds) return;
            _lastLineAt = now;

            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1);
            int gc0 = Math.Max(0, gen0 - _lastGen0), gc1 = Math.Max(0, gen1 - _lastGen1);
            _lastGen0 = gen0; _lastGen1 = gen1;

            _lines++;
            string hint = dt > 1f ? " (likely focus loss / scene work)" : "";
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[FrameWatch] dt=" + (dt * 1000f).ToString("0") + "ms frame=" + Time.frameCount
                + " timeScale=" + Time.timeScale.ToString("0.##")
                + " panel=" + ModPanel.IsShown
                + " panelPaused=" + PatchUI_PanelFocus.Engaged
                + " samuraiCut=" + PatchRoles_SamuraiPowerDash.ActiveCutLeases
                + " gc0=" + gc0 + " gc1=" + gc1
                + hint);
        }
        catch
        {
            // 诊断失败不得影响任何帧。
        }
    }
}
