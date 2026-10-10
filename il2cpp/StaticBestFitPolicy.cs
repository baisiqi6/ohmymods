using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 静态字体 BestFit writer-boundary 规范化策略（Issue206 字体根因修复的纯状态机）。
///
/// 不变量（仅对已知 static/dynamic 字体成立）：
///     effectiveBestFit = wanted &amp;&amp; currentFont.dynamic
/// static 字体不允许 BestFit=true；wanted 是用户/组件表达的意图，Font 能力变化时不得丢失。
///
/// 三个接入点（Unity 读取/写入与日志在 PatchFont_StaticBestFit 适配层）：
///   1. set_resizeTextForBestFit(bool)：外部请求记 wanted；static 下 true 请求改写为 false；
///      外部 false 始终清 wanted（即使 effective 已是 false、原 setter 会 early-return 不写字段）。
///   2. set_font(Font)：prefix 对已知 static 的 incoming 先登记 wanted 再把 effective 置 false
///      （绝不“先置 false 却没记 wanted”）；postfix/finalizer 读回实际 font 能力后结算 wanted。
///   3. OnEnable()：serialized 非法组合（static + true）在原生 cache invalidation 之前规范化；
///      已有 lease 只用已记 wanted，绝不因看到 effective=false 就把 wanted 覆盖成 false。
///
/// 意图权威（R2 修复）：策略不保存 capture 的旧 wanted、也不以旧 capture 做写决策。
///   唯一的“最新请求”注册表就是 live record；结算时只按「当前 record 的 wanted」或
///   「实际 effective 保持不变」决策。因此期间到达的外部 true/false、nested 同 Text 请求
///   天然覆盖旧事务，且旧 ticket 不会把后来请求改回。无 record 且无规范化责任时不写任何值。
///
/// 内部写许可（R2 修复）：内部规范化/恢复写不是“整个调用栈忽略同 life”，而是
///   <see cref="EnterInternalWrite"/> 为即将到来的**一次**直接 setter 调用发放 exact-life 唯一 seq 许可，
///   setter 前缀用 <see cref="TryConsumeInternalWrite"/> 一次性消费。消费后同一 Text 的重入
///   （原 setter 的 dirty/virtual 回调、其它 patch）没有许可，按真实外部请求记录；
///   另一个 Text 更不会借门；nested 再内部写各自持有独立 seq。scope 用 try/finally 按 seq 收尾，
///   复制/重复 Dispose 不会误关仍在用的许可。
///
/// 容量：≤<see cref="Capacity"/> 条，live wanted 绝不 evict；只在既有字体/BestFit/OnEnable
/// 事件里有界清理**确证** destroyed/mismatched 条目（每事件 <see cref="PrunePerEvent"/> 条，
/// 容量满时追加一次 <see cref="PruneBurstOnFull"/> 条突发清理）；读取失败属 Unknown，保留记录。
/// 仍满则保持原请求/状态，由调用方暴露一次 coverage-limit 日志。有限表不宣称覆盖任意规模的全部 Text。
///
/// 本文件不引用 UnityEngine，可被纯测试直接源链接（<c>Compile Include</c>）。
/// </summary>
internal static class StaticBestFitPolicy
{
    internal const int Capacity = 1024;
    internal const int PrunePerEvent = 2;
    internal const int PruneBurstOnFull = 64;

    /// <summary>当前 Font 能力；Unknown = null/读取失败 —— 一律不猜、不写。</summary>
    internal enum Capability { Unknown = 0, Static = 1, Dynamic = 2 }

    /// <summary>记录存活复核：只有 Dead 才允许清理；Unknown（读取异常）必须保留。</summary>
    internal enum Liveness { Unknown = 0, Alive = 1, Dead = 2 }

    /// <summary>结算决策：适配层据此执行内部规范化写（不记录 wanted）。</summary>
    internal enum WriteAction { None = 0, WriteFalse = 1, WriteTrue = 2 }

    /// <summary>Text 生命身份：native pointer + Unity instanceId。managed wrapper 引用不等于 life 身份。</summary>
    internal readonly struct Life : IEquatable<Life>
    {
        internal readonly long Pointer;
        internal readonly int InstanceId;

        internal Life(long pointer, int instanceId)
        {
            Pointer = pointer;
            InstanceId = instanceId;
        }

        internal bool Valid => Pointer != 0L && InstanceId != 0;

        public bool Equals(Life other) => Pointer == other.Pointer && InstanceId == other.InstanceId;

        public override bool Equals(object obj) => obj is Life other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Pointer * 397L) ^ InstanceId);

        public override string ToString() => "0x" + Pointer.ToString("x") + "#" + InstanceId;
    }

    /// <summary>一条 wanted 记录 = 该 life 的最新请求；外部请求直接改写它，事务结算只读它。</summary>
    internal sealed class Record
    {
        internal Life Life;
        internal object Owner;
        internal bool Wanted;
        internal int OpenTransactions;
        internal bool ReleasePending;
    }

    /// <summary>
    /// 一次 set_font 调用的凭据。引用类型：postfix 与 finalizer 共享同一实例，
    /// <see cref="Closed"/> 保证至多结算一次；正常 postfix 不执行（原方法抛出）时 finalizer 接管。
    /// </summary>
    internal sealed class FontTicket
    {
        internal Life Life;
        internal object Owner;
        internal bool HoldsRecord;
        internal bool Normalized;
        internal bool Closed;
    }

    private static readonly Dictionary<Life, Record> Records = new Dictionary<Life, Record>();
    private static readonly List<Record> Order = new List<Record>();
    private static int _cursor;
    private static int _coverageEvents;
    private static bool _coverageNotified;

    private struct Permit
    {
        internal Life Life;
        internal long Seq;
        internal bool Consumed;
    }

    [ThreadStatic] private static List<Permit> _permits;
    [ThreadStatic] private static long _permitSeq;

    // ============================================================
    // 测试/适配层可见的基本面
    // ============================================================

    internal static int RecordCount => Records.Count;

    internal static bool TryGetWanted(Life life, out bool wanted)
    {
        if (Records.TryGetValue(life, out Record rec))
        {
            wanted = rec.Wanted;
            return true;
        }
        wanted = false;
        return false;
    }

    internal static void ResetAll()
    {
        Records.Clear();
        Order.Clear();
        _cursor = 0;
        _coverageEvents = 0;
        _coverageNotified = false;
        _permits?.Clear();
        _permitSeq = 0L;
    }

    /// <summary>一次 coverage-limit 提示：容量满已发生 blocked 次；只在首次返回 true（日志一份）。</summary>
    internal static bool TryConsumeCoverageNotice(out int blocked)
    {
        blocked = _coverageEvents;
        if (_coverageEvents == 0) return false;
        bool first = !_coverageNotified;
        _coverageNotified = true;
        return first;
    }

    // ============================================================
    // 内部写许可（exact instance + 一次性消费；try/finally 按 seq 收尾）
    // ============================================================

    /// <summary>
    /// 为即将到来的一次直接 setter 调用发放 exact-life 许可。
    /// 用 <c>using</c> 包住本次 setter 调用；Dispose 按唯一 seq 收尾（复制/重复 Dispose 无副作用）。
    /// </summary>
    internal static InternalWriteScope EnterInternalWrite(Life life)
    {
        List<Permit> list = _permits;
        if (list == null)
        {
            list = new List<Permit>(4);
            _permits = list;
        }
        long seq = ++_permitSeq;
        list.Add(new Permit { Life = life, Seq = seq });
        return new InternalWriteScope(seq);
    }

    /// <summary>
    /// 一次性消费：存在未消费且匹配 life 的许可时消费最近一个并返回 true ——
    /// 仅表示“本次 setter 调用就是策略自己的写”。此后同 life 的重入无许可，按外部请求记录。
    /// </summary>
    internal static bool TryConsumeInternalWrite(Life life)
    {
        List<Permit> list = _permits;
        if (list == null) return false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Life.Equals(life) && !list[i].Consumed)
            {
                Permit permit = list[i];
                permit.Consumed = true;
                list[i] = permit;
                return true;
            }
        }
        return false;
    }

    private static void ReleasePermit(long seq)
    {
        List<Permit> list = _permits;
        if (list == null) return;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Seq == seq)
            {
                list.RemoveAt(i);
                return;
            }
        }
    }

    internal readonly struct InternalWriteScope : IDisposable
    {
        private readonly long _seq;

        internal InternalWriteScope(long seq)
        {
            _seq = seq;
        }

        public void Dispose() => ReleasePermit(_seq);
    }

    // ============================================================
    // 事件 1/2/3：外部 setter、set_font 事务、OnEnable
    // ============================================================

    /// <summary>
    /// 外部 <c>resizeTextForBestFit = requested</c>（适配层已消费内部许可或确认非内部写）。
    /// 返回 true 表示本次原 setter 参数必须改写为 false（static + wanted 已登记）。
    /// requested=false：无条件清 wanted（no-change early-return 也清），原样放行。
    /// requested=true：无论字体能力已知与否都更新/登记 wanted（Dynamic/Unknown 也不跳过），
    /// 只有 static 才需要改写本次参数。
    /// </summary>
    internal static bool NoteExternalBestFitSet(
        Life life, object owner, bool requested, Capability currentFont,
        Func<object, long, int, Liveness> alive, out bool coverage)
    {
        coverage = false;
        if (!life.Valid) return false;

        if (!requested)
        {
            if (Records.TryGetValue(life, out Record rec))
            {
                rec.Wanted = false;
                if (rec.OpenTransactions == 0) Remove(rec);
                else rec.ReleasePending = true;
            }
            return false;
        }

        Record r = GetOrCreate(life, owner, true, alive, out coverage);
        if (r == null) return false; // 容量满：外部 true 本身不被改写（bit 即意图），只报 coverage
        r.Wanted = true;
        r.ReleasePending = false;
        return currentFont == Capability.Static;
    }

    /// <summary>
    /// set_font prefix：登记/解析 wanted，决定是否先规范化 effective=false。
    /// 返回 null 表示本次调用不做任何接管责任（身份不可信）。
    /// </summary>
    internal static FontTicket BeginFontWrite(
        Life life, object owner, bool effective, Capability incoming,
        Func<object, long, int, Liveness> alive, out bool coverage)
    {
        coverage = false;
        if (!life.Valid) return null;

        var ticket = new FontTicket { Life = life, Owner = owner };

        Record rec = null;
        if (Records.TryGetValue(life, out rec))
        {
            if (owner != null) rec.Owner = owner;
        }
        else if (effective)
        {
            // 意图来自组件 bit：登记 lease，后续 static 能力变化时才能真正保住 true。
            rec = GetOrCreate(life, owner, true, alive, out coverage);
        }

        if (incoming == Capability.Static && effective)
        {
            if (rec == null) rec = GetOrCreate(life, owner, true, alive, out coverage);
            if (rec != null) ticket.Normalized = true;
            else coverage = true; // 不能“先置 false 却没记 wanted”：保持原组合
        }

        ticket.HoldsRecord = rec != null;
        if (rec != null) rec.OpenTransactions++;
        return ticket;
    }

    /// <summary>
    /// set_font postfix/finalizer 共用的结算（≤1 次）：只按「当前 record 的 wanted」与「实际 effective」
    /// 决策，绝不使用旧 capture 的意图；无 record 时除 static 非法组合外不写。
    /// 幂等；重复调用（postfix 已结算）直接 None。
    /// </summary>
    internal static WriteAction FinishFontWrite(
        FontTicket ticket, Capability actual, bool actualEffective,
        Func<object, long, int, Liveness> alive, out bool coverage)
    {
        coverage = false;
        if (ticket == null || ticket.Closed) return WriteAction.None;
        ticket.Closed = true;

        Record rec = null;
        if (ticket.Life.Valid) Records.TryGetValue(ticket.Life, out rec);

        bool releaseAfter = false;
        if (rec != null)
        {
            if (ticket.HoldsRecord && rec.OpenTransactions > 0) rec.OpenTransactions--;
            releaseAfter = rec.ReleasePending && rec.OpenTransactions == 0;
        }

        WriteAction action;
        switch (actual)
        {
            case Capability.Static:
                if (!actualEffective) action = WriteAction.None;
                else if (rec != null) action = WriteAction.WriteFalse;
                else
                {
                    // 实际落成 static 且组合非法：登记 wanted=true 才能写 false，否则保持原状态。
                    Record created = GetOrCreate(ticket.Life, ticket.Owner, true, alive, out coverage);
                    action = created != null ? WriteAction.WriteFalse : WriteAction.None;
                }
                break;

            case Capability.Dynamic:
                if (rec == null) action = WriteAction.None; // 无 record：不拿旧 capture 盲写，保持实际 effective
                else if (rec.Wanted) action = actualEffective ? WriteAction.None : WriteAction.WriteTrue;
                else action = actualEffective ? WriteAction.WriteFalse : WriteAction.None;
                break;

            default:
                action = WriteAction.None; // unknown/null：只保责任，不猜、不写
                break;
        }

        if (releaseAfter) Remove(rec);
        return action;
    }

    /// <summary>
    /// 最末门（纯）：在实际写之前，按**最新** wanted 与**当前** font 能力复核已决动作。
    /// 取消 stale desired（返回 None，保当前 effective）、或按最新 wanted &amp;&amp; dynamic 重新规范。
    /// forced = set_font prefix 的强制规范化（incoming 已知 static）：保持 false —— 此刻组件字段
    /// 可能仍是旧 dynamic 字体，不能据此改判。Unknown 能力一律不猜。
    /// 读到的 life/能力/effective 均为 readonly native 读；调用方在其后到 setter 之间不得再有任何日志/回调。
    /// </summary>
    internal static WriteAction ApplyDecisionGate(
        Life life, WriteAction decided, bool forced, Capability currentFont, bool currentEffective)
    {
        if (decided == WriteAction.None) return WriteAction.None;
        if (forced) return decided;
        if (currentFont == Capability.Unknown) return WriteAction.None;
        if (!TryGetWanted(life, out bool wanted)) return WriteAction.None; // 最新记录已消失：不写
        bool desired = currentFont == Capability.Dynamic && wanted;
        if (desired == currentEffective) return WriteAction.None;
        return desired ? WriteAction.WriteTrue : WriteAction.WriteFalse;
    }

    /// <summary>OnEnable prefix：serialized 非法组合的规范化 / dynamic 下 intent 恢复。写操作由适配层执行。</summary>
    internal static WriteAction NoteEnable(
        Life life, object owner, Capability currentFont, bool effective,
        Func<object, long, int, Liveness> alive, out bool coverage)
    {
        coverage = false;
        if (!life.Valid) return WriteAction.None;

        switch (currentFont)
        {
            case Capability.Static:
                if (!effective) return WriteAction.None;
                if (Records.TryGetValue(life, out Record rec))
                {
                    if (owner != null) rec.Owner = owner;
                    return WriteAction.WriteFalse; // 只用已记 wanted，不覆盖
                }
                Record created = GetOrCreate(life, owner, true, alive, out coverage);
                return created != null ? WriteAction.WriteFalse : WriteAction.None;

            case Capability.Dynamic:
                if (effective) return WriteAction.None;
                return Records.TryGetValue(life, out Record r) && r.Wanted
                    ? WriteAction.WriteTrue
                    : WriteAction.None;

            default:
                return WriteAction.None;
        }
    }

    // ============================================================
    // 有界清理（只在既有事件内调用）
    // ============================================================

    /// <summary>
    /// 检查至多 budget 条记录，仅移除**确证** Dead（validator 返回 Dead）且无未收尾事务的条目；
    /// Alive/Unknown（读取失败）与仍在事务中的条目一律保留。
    /// </summary>
    internal static int Prune(Func<object, long, int, Liveness> alive, int budget)
    {
        if (alive == null || budget <= 0) return 0;
        int removed = 0;
        int examined = 0;
        while (examined < budget && Order.Count > 0)
        {
            if (_cursor >= Order.Count) _cursor = 0;
            Record rec = Order[_cursor];
            examined++;
            Liveness state;
            try
            {
                state = alive(rec.Owner, rec.Life.Pointer, rec.Life.InstanceId);
            }
            catch (Exception)
            {
                state = Liveness.Unknown; // 读取失败 ≠ destroyed：保记录、保 wanted
            }
            if (state == Liveness.Dead && rec.OpenTransactions == 0)
            {
                Order[_cursor] = Order[Order.Count - 1];
                Order.RemoveAt(Order.Count - 1);
                Records.Remove(rec.Life);
                removed++;
                continue; // swap 进来的条目本轮继续检查
            }
            _cursor++;
        }
        return removed;
    }

    // ============================================================
    // 内部
    // ============================================================

    private static Record GetOrCreate(
        Life life, object owner, bool wanted, Func<object, long, int, Liveness> alive, out bool coverage)
    {
        coverage = false;
        if (Records.TryGetValue(life, out Record rec))
        {
            if (owner != null) rec.Owner = owner;
            return rec;
        }
        if (Records.Count >= Capacity)
        {
            Prune(alive, PruneBurstOnFull);
            if (Records.Count >= Capacity)
            {
                _coverageEvents++;
                coverage = true;
                return null;
            }
        }
        rec = new Record { Life = life, Owner = owner, Wanted = wanted };
        Records.Add(life, rec);
        Order.Add(rec);
        return rec;
    }

    private static void Remove(Record rec)
    {
        if (rec == null) return;
        Records.Remove(rec.Life);
        for (int i = 0; i < Order.Count; i++)
        {
            if (ReferenceEquals(Order[i], rec))
            {
                Order[i] = Order[Order.Count - 1];
                Order.RemoveAt(Order.Count - 1);
                if (_cursor > i) _cursor--;
                else if (_cursor >= Order.Count) _cursor = 0;
                return;
            }
        }
    }
}
