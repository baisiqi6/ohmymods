using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomEnhancedMod;

/// <summary>
/// Issue206 字体根因修复：静态字体（<c>!Font.dynamic</c>）不接受 BestFit=true。
///
/// 事实依据（当前 ARM64 静态审计，未在 Native 运行验证）：
///   * <c>Text.set_font</c> 只写 FontData.m_Font + FontUpdateTracker/脏标记，不同步 BestFit；
///   * <c>Text.OnEnable</c> 会让 TextGenerator cache 失效并 TrackText（序列化组合先进来）；
///   * <c>Text.set_resizeTextForBestFit</c> 是 instance void(bool by-value)：比较 FontData.m_BestFit 后写 1 byte + 脏调用。
/// 因此规范化落在 writer 边界与首次 activation：
///   1. set_font prefix：incoming 已知 static 时先把 effective 置 false（wanted 先登记），再放行原字体赋值；
///      postfix/finalizer 读回实际 font 能力后按 wanted 结算（动态恢复 true、静态保 false）。
///   2. set_resizeTextForBestFit prefix：外部请求记 wanted；static 下 true 请求改写为 false；
///      外部 false 始终清 wanted（原 setter no-change early-return 也清）。
///   3. OnEnable prefix：serialized 非法组合在原生 cache invalidation 之前规范化；已有 lease 不覆盖 wanted。
///
/// 边界（与设计复审一致）：
///   * 只对「当前 Text 有效、Font 可读且已知 dynamic==false」规范化；null/读取失败一律不猜、不替换字体、
///     不改字号/style/min/max/language settings，不加 Tick、不扫描、不日志过滤、不吞原异常（Finalizer 原样返回）。
///   * 内部规范化/恢复写用 exact-instance **一次性许可**（`EnterInternalWrite` + setter 前缀
///     `TryConsumeInternalWrite`），只忽略这一次直接 setter 调用；消费后同 Text 的重入按外部请求记录，
///     另一个 Text 不会借门；scope try/finally 按 seq 收尾（复制/重复 Dispose 无副作用）。
///     Font setter 失败时按实际 font 恢复，绝不会永久留下 prefix 规范化掉的 false。
///   * 记录存活复核只有确证 dead（fake-null/destroyed、ptr/id 不符）才清理；读取异常为 Unknown，保记录。
///   * 期望记录 ≤1024，live wanted 不 evict；只在既有事件内做有界 prune。
///
/// 启用：修复默认生效；诊断见证 env KEM_FONT_DIAG=1，最多 12 行，只记 static 规范化/恢复事件
/// （id/font ptr/dyn/bestfit/wanted），不输出文本正文，也不是每帧新日志。
/// </summary>
internal static class StaticBestFitPatch
{
    private const int DiagMaxLines = 12;
    private const int FaultMaxLines = 4;

    private static readonly Func<object, long, int, StaticBestFitPolicy.Liveness> Alive = ValidateOwner;

    private static int _diagState; // 0 未检查 / 1 开 / 2 关
    private static int _diagLines;
    private static int _faultLines;

    // ============================================================
    // Unity 侧读取/写入
    // ============================================================

    private static bool TryLife(Text text, out StaticBestFitPolicy.Life life)
    {
        life = default;
        try
        {
            if (text == null) return false;
            IntPtr pointer = text.Pointer;
            if (pointer == IntPtr.Zero) return false;
            int id = text.GetInstanceID();
            if (id == 0) return false;
            life = new StaticBestFitPolicy.Life(pointer.ToInt64(), id);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadEffective(Text text, out bool effective)
    {
        effective = false;
        try
        {
            effective = text.resizeTextForBestFit;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static StaticBestFitPolicy.Capability CapabilityOf(Font font)
    {
        if (font == null) return StaticBestFitPolicy.Capability.Unknown;
        try
        {
            return font.dynamic ? StaticBestFitPolicy.Capability.Dynamic : StaticBestFitPolicy.Capability.Static;
        }
        catch (Exception)
        {
            return StaticBestFitPolicy.Capability.Unknown;
        }
    }

    private static StaticBestFitPolicy.Capability CurrentCapability(Text text)
    {
        try
        {
            return CapabilityOf(text.font);
        }
        catch (Exception)
        {
            return StaticBestFitPolicy.Capability.Unknown;
        }
    }

    /// <summary>
    /// 内部规范化/恢复写。最末门顺序（R3）：life 复核 → 当前 effective → 当前 Font 能力 →
    /// <see cref="StaticBestFitPolicy.ApplyDecisionGate"/>（最新 wanted &amp;&amp; 能力重新裁定 / 取消 stale）→
    /// 一次性许可 + 直接 setter。以上读取均为 readonly native 读；从裁定到 setter 之间**不得**再出现
    /// 任何日志/回调（所有 DiagLine/Log/Fault/ReportCoverage 都在本函数返回之后才发生），因此日志订阅者
    /// 的 reentry 无法让旧动作覆盖新请求。写后读回校验，失败只有界报告。
    /// </summary>
    private static bool WriteEffective(Text text, StaticBestFitPolicy.Life life,
        StaticBestFitPolicy.WriteAction decided, bool forced)
    {
        try
        {
            if (!LifeMatches(text, life)) return false;
            if (!TryReadEffective(text, out bool current)) return false;
            var capability = CurrentCapability(text);
            var action = StaticBestFitPolicy.ApplyDecisionGate(life, decided, forced, capability, current);
            if (action == StaticBestFitPolicy.WriteAction.None) return false;
            bool value = action == StaticBestFitPolicy.WriteAction.WriteTrue;
            using (StaticBestFitPolicy.EnterInternalWrite(life))
            {
                text.resizeTextForBestFit = value;
            }
            if (!TryReadEffective(text, out bool actual) || actual != value)
            {
                Fault("write-verify");
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            Fault("write");
            return false;
        }
    }

    /// <summary>当前 wrapper 是否仍是这条 life（readonly 读；destroyed/mismatch 为 false）。</summary>
    private static bool LifeMatches(Text text, StaticBestFitPolicy.Life life)
    {
        return ValidateOwner(text, life.Pointer, life.InstanceId) == StaticBestFitPolicy.Liveness.Alive;
    }

    /// <summary>
    /// 记录存活复核：只有确认 fake-null/destroyed 或 pointer/instanceId 不符才 Dead；
    /// 读取异常一律 Unknown（保记录），绝不把瞬时读故障当作 destroyed。
    /// </summary>
    private static StaticBestFitPolicy.Liveness ValidateOwner(object owner, long pointer, int instanceId)
    {
        try
        {
            if (!(owner is Text text)) return StaticBestFitPolicy.Liveness.Dead;
            if (text == null || text.Pointer == IntPtr.Zero) return StaticBestFitPolicy.Liveness.Dead;
            if (text.Pointer.ToInt64() != pointer) return StaticBestFitPolicy.Liveness.Dead;
            if (text.GetInstanceID() != instanceId) return StaticBestFitPolicy.Liveness.Dead;
            return StaticBestFitPolicy.Liveness.Alive;
        }
        catch (Exception)
        {
            return StaticBestFitPolicy.Liveness.Unknown;
        }
    }

    private static void PruneTick()
    {
        try
        {
            StaticBestFitPolicy.Prune(Alive, StaticBestFitPolicy.PrunePerEvent);
        }
        catch (Exception)
        {
            Fault("prune");
        }
    }

    // ============================================================
    // 事件接入（三个入口的适配层）
    // ============================================================

    internal static StaticBestFitPolicy.FontTicket BeginFontWrite(Text text, Font incoming)
    {
        if (!TryLife(text, out StaticBestFitPolicy.Life life)) return null;
        PruneTick();
        if (!TryReadEffective(text, out bool effective)) return null; // 读失败不猜
        var incomingCapability = CapabilityOf(incoming);
        var ticket = StaticBestFitPolicy.BeginFontWrite(
            life, text, effective, incomingCapability, Alive, out bool coverage);
        if (ticket == null)
        {
            ReportCoverage(coverage);
            return null;
        }
        if (ticket.Normalized)
        {
            // 强制规范化（incoming 已知 static）先执行；日志一律在其后 —— 仍是“原字体赋值之前”的
            // writer-before 见证，flag 为规范化后的实际值，且日志 reentry 不会让旧动作覆盖新请求。
            WriteEffective(text, life, StaticBestFitPolicy.WriteAction.WriteFalse, forced: true);
            DiagLine("w=writer-before id=" + life.InstanceId
                + " fptr=" + Ptr(incoming)
                + " dyn=" + Dyn(incomingCapability)
                + " bestfit=" + Flag(text)
                + " wanted=" + Wanted(life));
        }
        ReportCoverage(coverage);
        return ticket;
    }

    internal static void FinishFontWrite(Text text, StaticBestFitPolicy.FontTicket ticket)
    {
        if (ticket == null) return;
        if (!TryLife(text, out StaticBestFitPolicy.Life life) || !life.Equals(ticket.Life)
            || !TryReadEffective(text, out bool effective))
        {
            // 身份/状态读失败：只结算事务责任（Unknown 不写），不猜；此路径无写动作，日志无 reentry 窗口。
            StaticBestFitPolicy.FinishFontWrite(
                ticket, StaticBestFitPolicy.Capability.Unknown, false, Alive, out bool ignored);
            ReportCoverage(ignored);
            return;
        }
        var actual = CurrentCapability(text);
        var action = StaticBestFitPolicy.FinishFontWrite(ticket, actual, effective, Alive, out bool coverage);
        bool written = action != StaticBestFitPolicy.WriteAction.None
            && WriteEffective(text, life, action, forced: false);
        ReportCoverage(coverage); // 写之后
        if (action != StaticBestFitPolicy.WriteAction.None)
            DiagWriterAfter(life, text, actual, effective, action, written);
    }

    private static void DiagWriterAfter(
        StaticBestFitPolicy.Life life, Text text, StaticBestFitPolicy.Capability actual, bool effectiveBefore,
        StaticBestFitPolicy.WriteAction action, bool written)
    {
        DiagLine("w=writer-after act=" + (action == StaticBestFitPolicy.WriteAction.WriteTrue ? "true" : "false")
            + " applied=" + (written ? "1" : "0")
            + " id=" + life.InstanceId
            + " fptr=" + Ptr(SafeFont(text))
            + " dyn=" + Dyn(actual)
            + " bfBefore=" + (effectiveBefore ? "1" : "0")
            + " bestfit=" + Flag(text)
            + " wanted=" + Wanted(life));
    }

    internal static void EnableBefore(Text text)
    {
        if (!TryLife(text, out StaticBestFitPolicy.Life life)) return;
        PruneTick();
        if (!TryReadEffective(text, out bool effective)) return;
        var capability = CurrentCapability(text);
        var action = StaticBestFitPolicy.NoteEnable(life, text, capability, effective, Alive, out bool coverage);
        if (action != StaticBestFitPolicy.WriteAction.None)
            WriteEffective(text, life, action, forced: false); // 先写（此调用内无日志）
        ReportCoverage(coverage); // 写之后
        if (action != StaticBestFitPolicy.WriteAction.None)
            DiagLine("w=enable-before act=" + (action == StaticBestFitPolicy.WriteAction.WriteTrue ? "true" : "false")
                + " id=" + life.InstanceId
                + " fptr=" + Ptr(SafeFont(text))
                + " dyn=" + Dyn(capability)
                + " bfDecided=" + (effective ? "1" : "0")
                + " bestfit=" + Flag(text)
                + " wanted=" + Wanted(life));
    }

    internal static void ExternalBestFitSet(Text text, ref bool requested)
    {
        if (!TryLife(text, out StaticBestFitPolicy.Life life)) return;
        // 一次性消费策略自己的内部写许可：只吞“本次直接 setter 调用”。消费后同 Text 的
        // 重入（原 setter dirty/virtual 回调、其它 patch）没有许可，按真实外部请求记录。
        if (StaticBestFitPolicy.TryConsumeInternalWrite(life)) return;
        PruneTick();
        bool rewrite = StaticBestFitPolicy.NoteExternalBestFitSet(
            life, text, requested, CurrentCapability(text), Alive, out bool coverage);
        if (rewrite) requested = false; // static 字体：本次原 setter 参数规范化为 false（wanted 已登记）
        ReportCoverage(coverage);       // 日志放最后：本路径无 bool 写，规范在原始 setter 执行前已确定
    }

    private static Font SafeFont(Text text)
    {
        try
        {
            return text.font;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ============================================================
    // 有界诊断/报告
    // ============================================================

    private static bool DiagEnabled()
    {
        if (_diagState != 0) return _diagState == 1;
        try
        {
            _diagState = Environment.GetEnvironmentVariable("KEM_FONT_DIAG") == "1" ? 1 : 2;
        }
        catch (Exception)
        {
            _diagState = 2;
        }
        return _diagState == 1;
    }

    private static void DiagLine(string line)
    {
        if (_diagLines >= DiagMaxLines || !DiagEnabled()) return;
        _diagLines++;
        Log("[FontBestFit] " + line);
    }

    private static void ReportCoverage(bool coverage)
    {
        if (!coverage) return;
        if (!StaticBestFitPolicy.TryConsumeCoverageNotice(out int blocked)) return;
        Log("[FontBestFit] coverage-limit capacity=" + StaticBestFitPolicy.Capacity
            + " blocked=" + blocked + " kept-original-state");
    }

    private static void Fault(string where)
    {
        if (_faultLines >= FaultMaxLines) return;
        _faultLines++;
        Log("[FontBestFit] fault=" + where);
    }

    private static string Wanted(StaticBestFitPolicy.Life life)
    {
        return StaticBestFitPolicy.TryGetWanted(life, out bool wanted) ? (wanted ? "1" : "0") : "-";
    }

    /// <summary>当前 effective 的只读见证（"?" = 读失败）；只在写动作之后调用。</summary>
    private static string Flag(Text text)
    {
        return TryReadEffective(text, out bool effective) ? (effective ? "1" : "0") : "?";
    }

    private static string Dyn(StaticBestFitPolicy.Capability capability)
    {
        return capability == StaticBestFitPolicy.Capability.Dynamic ? "1"
            : capability == StaticBestFitPolicy.Capability.Static ? "0" : "?";
    }

    private static string Ptr(Font font)
    {
        try
        {
            return font == null ? "0" : font.Pointer.ToInt64().ToString("x");
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static void Log(string line)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(line);
        }
        catch (Exception)
        {
            // 日志失败不得影响游戏。
        }
    }
}

/// <summary>set_font prefix 建 capture、postfix/finalizer 读回实际 font 后结算；原异常原样返回。</summary>
[HarmonyPatch(typeof(Text), "set_font")]
internal static class Text_set_font_StaticBestFit
{
    [HarmonyPrefix]
    private static void Prefix(Text __instance, Font __0, out StaticBestFitPolicy.FontTicket __state)
    {
        __state = null;
        try
        {
            __state = StaticBestFitPatch.BeginFontWrite(__instance, __0);
        }
        catch (Exception)
        {
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Text __instance, StaticBestFitPolicy.FontTicket __state)
    {
        try
        {
            StaticBestFitPatch.FinishFontWrite(__instance, __state);
        }
        catch (Exception)
        {
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Text __instance, Exception __exception, StaticBestFitPolicy.FontTicket __state)
    {
        try
        {
            StaticBestFitPatch.FinishFontWrite(__instance, __state); // 正常路径已结算则幂等跳过
        }
        catch (Exception)
        {
        }
        return __exception;
    }
}

/// <summary>
/// set_resizeTextForBestFit prefix：外部请求记 wanted；static 下 true 改写为 false；
/// 外部 false 无条件清 wanted。内部写门在此放行策略自己的规范化/恢复写。
/// 托管 ref bool 只改写本次原参数，不改变原生 by-value ABI。
/// </summary>
[HarmonyPatch(typeof(Text), "set_resizeTextForBestFit")]
internal static class Text_set_resizeTextForBestFit_StaticBestFit
{
    [HarmonyPrefix]
    private static void Prefix(Text __instance, ref bool __0)
    {
        try
        {
            StaticBestFitPatch.ExternalBestFitSet(__instance, ref __0);
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>OnEnable prefix：serialized 非法组合在原生 cache invalidation/TrackText 之前规范化。</summary>
[HarmonyPatch(typeof(Text), "OnEnable")]
internal static class Text_OnEnable_StaticBestFit
{
    [HarmonyPrefix]
    private static void Prefix(Text __instance)
    {
        try
        {
            StaticBestFitPatch.EnableBefore(__instance);
        }
        catch (Exception)
        {
        }
    }
}
