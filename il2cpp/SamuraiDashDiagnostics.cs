using System;
using System.Globalization;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 被动冲刺诊断（samurai-diag）：统计每次真实冲刺尝试并输出有界的 [SamuraiDiag] 行。
/// 无 Harmony 属性、无 Unity tick、不扫场景、静态字段不做原生读取；调用时机由现有
/// 冲刺/视觉代码在它自己的分支里决定。原生读取与日志的任何异常都被吞掉，
/// 保证诊断永远不会破坏冲刺本身。
/// </summary>
internal static class SamuraiDashDiagnostics
{
    private const int Capacity = 8;           // 空闲后最多连续记录 8 次冲刺；1 token = 1 条 start 行
    private const float RefillPerSecond = 1f; // 每秒补 1 个 token
    private const int MaxLines = 12;          // 每次冲刺的行数上限，含 start 行

    /// <summary>单次冲刺的诊断状态：只放 primitive/string，绝不持有 Unity 对象。</summary>
    internal sealed class Trace
    {
        internal int Id;                 // 已准入序号，每条 start 行唯一
        internal int KnightId;           // 骑士 GameObject InstanceID，读取失败时为 0
        internal bool Returning;         // true=return 冲刺，false=attack 冲刺
        internal float StartedAt;        // Motion 捕获的 now，这里不重读时钟
        internal bool FirstUpdateLogged = false; // 归现有视觉 Tick 所有：本 helper 只声明，不写不读
        internal bool TailLogged = false;        // 同上：最后一条尾迹消失只记一行
        internal int Lines;              // 已用行数（含 start），上限 MaxLines
        internal int Dropped;            // 被行数上限拒绝的 Write 次数，供现场排查
    }

    private static int _nextId, _totalDashes, _suppressedSinceLast;
    private static float _tokens = Capacity, _refilledAt = float.NegativeInfinity;

    /// <summary>只由现有 Motion.Begin 调用，计一次真实冲刺尝试；null 表示这次被限流不记录。</summary>
    internal static Trace Begin(Knight owner, bool returning, float now)
    {
        try
        {
            _totalDashes++;
            if (!Admit(now)) { _suppressedSinceLast++; return null; } // 先限流，再碰 Unity
            int knightId = 0;
            float x = float.NaN;
            try
            {
                if (owner != null && owner.gameObject != null)
                {
                    knightId = owner.gameObject.GetInstanceID();
                    x = owner.transform.position.x; // 单次读取，只为日志上下文
                }
            }
            catch { } // 原生读取失败只降级上下文，不影响冲刺
            int suppressed = _suppressedSinceLast;
            _suppressedSinceLast = 0;
            var trace = new Trace { Id = ++_nextId, KnightId = knightId, Returning = returning,
                StartedAt = now, Lines = 1 };
            Line(trace, "start", "mode=" + (returning ? "return" : "attack") + " time=" + Num(now, "F3") +
                " x=" + Num(x, "F2") + " suppressedSinceLast=" + suppressed + " totalDashCount=" + _totalDashes);
            return trace;
        }
        catch { return null; }
    }

    /// <summary>追加一行有界日志；trace 为 null、行数用尽或 sink 抛错都只是 no-op。</summary>
    internal static void Write(Trace trace, string eventName, string details)
    {
        if (trace == null) return;
        if (trace.Lines >= MaxLines) { trace.Dropped++; return; }
        trace.Lines++; // 先记账再落盘：抛错的 sink 既不能重试也不能撑破上限
        Line(trace, eventName, details);
    }

    private static bool Admit(float now)
    {
        if (float.IsNaN(now) || float.IsInfinity(now)) return false; // 无效时间戳不动 bucket
        if (now < _refilledAt) { _tokens = Capacity; _refilledAt = now; } // 时钟倒退：重置 bucket
        else
        {
            float elapsed = now - _refilledAt;
            if (elapsed > 0f) { _tokens = Math.Min(Capacity, _tokens + elapsed * RefillPerSecond); _refilledAt = now; }
        }
        if (_tokens < 1f) return false;
        _tokens -= 1f;
        return true;
    }

    private static string Num(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static void Line(Trace trace, string eventName, string details)
    {
        try
        {
            string line = "[SamuraiDiag] dash=" + trace.Id + " knight=" + trace.KnightId +
                " event=" + (eventName ?? "?") + (string.IsNullOrEmpty(details) ? "" : " " + details);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(line);
        }
        catch { }
    }
}
