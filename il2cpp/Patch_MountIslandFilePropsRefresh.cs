using System;

namespace KingdomEnhancedMod;

/// <summary>一次 UpdateFileProps 刷新的唯一收尾结果（production 用于日志/门槛，tests 断言原因）。</summary>
internal enum MountIslandRefreshResult
{
    None = 0,
    /// <summary>本轮 refresh 确认 ready。</summary>
    Confirmed = 1,
    /// <summary>该调用不是当前活动外层 refresh（嵌套/陈旧/重复收尾）。</summary>
    NotOwner = 2,
    /// <summary>原生异常或收尾侧读取失败。</summary>
    FailedOrAborted = 3,
    /// <summary>原方法未执行（postfix __runOriginal=false 或 finalizer 复核 false）。</summary>
    ChainSkipped = 4,
    /// <summary>本轮 refresh 期间出现嵌套 refresh：外层不确认 ready。</summary>
    NestedObserved = 5,
    /// <summary>冻结的 submitted MAX 与收尾时 current MAX 不一致，或 submitted &lt; required。</summary>
    CapacityMismatch = 6,
}

/// <summary>
/// 纯状态机（零 Unity/Il2Cpp/Harmony 依赖）：一次 <c>IslandSaveData.UpdateFileProps</c> 刷新的
/// 容量/ready 责任。production（<see cref="PatchExtensionIsland_FilePropsRefresh"/> 三个钩子 +
/// ExtensionIslandRuntime 桥）与测试**链接同一类型**，不做镜像模型。
///
/// 单一 hook 合同（root 二审，依据 runtime-followup-review.md:20-24 对 actual 0Harmony 2.10.2
/// HarmonyManipulator 的只读反编译：postfix/finalizer 的 `bool __runOriginal` 由同一 binder 注入）：
/// - prefix：<see cref="Begin"/> 建立本次身份（__state，禁止跨调用复用）并清 ready；
///   桥内单调提升 MAX 后 <see cref="NoteSubmittedMax"/> 冻结**提升后 submitted MAX**；
///   任何 prefix 读取/设置异常 → <see cref="NotePrefixFailed"/>（本轮永不 ready）。
/// - postfix：<see cref="NoteOriginalRun"/> 记录 binder 注入的 `__runOriginal`——
///   false（原方法被 skip）时不得留下"已执行"证据。
/// - finalizer：**唯一收尾** <see cref="Confirm"/>，再次接收 readonly `__runOriginal` 复核；
///   失败/未拥有/被 skip/submitted≠current/嵌套 一律不确认 ready；原 exception 由 patch 原样返回。
/// - 嵌套：内层 native 可执行返回，但期间出现嵌套时**外层不确认 ready**（NestedObserved，日志）；
///   内层收尾永不写 ready（NotOwner）。
/// - `__state` 缺失（prefix 链未到本类）→ <see cref="NoteUnownedAttempt"/> 失效 ready。
/// - ready 查询：<see cref="IsPrepared"/> 要求 currentMax == completed（容量漂移即失效）。
/// </summary>
internal sealed class MountIslandFilePropsRefresh
{
    /// <summary>0..13 共 14 槽；直接引用真实 pure policy（不重复硬编码）。</summary>
    internal const int RequiredCapacity = MountIslandSplitPolicy.RequiredFileCapacity;

    /// <summary>一次 refresh 的身份与执行证据（Harmony __state 承载）。</summary>
    internal sealed class RefreshCall
    {
        internal int Generation;
        internal bool Nested;
        internal bool PrefixFailed;
        internal bool SkippedObserved;
        internal bool OriginalRan;
        internal bool Confirmed;
        /// <summary>prefix 冻结：单调提升后的提交 MAX（0 = 冻结失败 → 永不 ready）。</summary>
        internal int SubmittedMax;
    }

    private static readonly object Gate = new object();
    private static int _nextGeneration;
    private static int _activeGeneration = -1;
    private static bool _inProgress;
    private static bool _prepared;
    private static int _completedCapacity;
    /// <summary>外层 refresh 期间出现过嵌套的代次（该外层不确认 ready）。</summary>
    private static int _nestedObservedGeneration = -1;

    /// <summary>是否有外层 refresh 进行中（EnsureFileCapacity 的重入门使用）。</summary>
    internal static bool InProgress
    {
        get { lock (Gate) { return _inProgress; } }
    }

    /// <summary>prefix 开始：新身份；外层清 ready 并接管责任；嵌套只标记（外层不确认 ready）。</summary>
    internal static void Begin(out RefreshCall call)
    {
        lock (Gate)
        {
            _nextGeneration++;
            call = new RefreshCall { Generation = _nextGeneration, Nested = _inProgress };
            if (_inProgress)
            {
                _nestedObservedGeneration = _activeGeneration;   // 嵌套 attempt → 外层 fail confirmed
                return;
            }
            _inProgress = true;
            _activeGeneration = call.Generation;
            _prepared = false;          // 本轮确认前不得把旧确认当现役
            _completedCapacity = 0;
            _nestedObservedGeneration = -1;
        }
    }

    internal static void NotePrefixFailed(RefreshCall call)
    {
        lock (Gate) { if (call != null) call.PrefixFailed = true; }
    }

    /// <summary>prefix 冻结提交容量（提升后 MAX；0/负值 = 冻结失败候选 → 永不 ready）。</summary>
    internal static void NoteSubmittedMax(RefreshCall call, int submittedMax)
    {
        lock (Gate) { if (call != null) call.SubmittedMax = submittedMax; }
    }

    /// <summary>
    /// postfix：记录 binder 注入的 <c>__runOriginal</c>。true → 记录执行证据；
    /// false（原方法被 skip）→ 只记跳过（不得充当执行证据）。
    /// </summary>
    internal static void NoteOriginalRun(RefreshCall call, bool runOriginal)
    {
        lock (Gate)
        {
            if (call == null) return;
            if (runOriginal) call.OriginalRan = true;
            else call.SkippedObserved = true;
        }
    }

    /// <summary>
    /// finalizer 唯一收尾：<paramref name="failed"/> = 原异常/收尾读取失败；
    /// <paramref name="runOriginalAtFinalizer"/> = finalizer 复核的 readonly <c>__runOriginal</c>；
    /// <paramref name="currentMax"/> = 收尾时刻 MAX。返回确认原因（不确认时 ready 保持 false）。
    /// </summary>
    internal static MountIslandRefreshResult Confirm(
        RefreshCall call, bool failed, int currentMax, bool runOriginalAtFinalizer)
    {
        lock (Gate)
        {
            if (call == null || call.Confirmed) return MountIslandRefreshResult.None;   // 重复 finalizer 无效
            call.Confirmed = true;

            bool owns = !call.Nested && call.Generation == _activeGeneration;
            if (owns)
            {
                _inProgress = false;
                _activeGeneration = -1;
            }
            if (!owns) return MountIslandRefreshResult.NotOwner;   // 嵌套/陈旧收尾：不写 ready 状态

            MountIslandRefreshResult result;
            if (failed) result = MountIslandRefreshResult.FailedOrAborted;
            else if (!runOriginalAtFinalizer || !call.OriginalRan || call.SkippedObserved)
                result = MountIslandRefreshResult.ChainSkipped;
            else if (_nestedObservedGeneration == call.Generation)
                result = MountIslandRefreshResult.NestedObserved;
            else if (call.PrefixFailed
                || call.SubmittedMax < RequiredCapacity
                || currentMax != call.SubmittedMax)
                result = MountIslandRefreshResult.CapacityMismatch;
            else
                result = MountIslandRefreshResult.Confirmed;

            _prepared = result == MountIslandRefreshResult.Confirmed;
            _completedCapacity = _prepared ? call.SubmittedMax : 0;
            _nestedObservedGeneration = -1;
            return result;
        }
    }

    /// <summary>__state 缺失（prefix 未归属到本类身份/链被中断）→ 本次 attempt 未被我们拥有：失效 ready。</summary>
    internal static void NoteUnownedAttempt()
    {
        lock (Gate)
        {
            _prepared = false;
            _completedCapacity = 0;
        }
    }

    /// <summary>ready = 已确认 && completed ≥ required && **当前 MAX == completed**（容量漂移即失效）。</summary>
    internal static bool IsPrepared(int currentMax)
    {
        lock (Gate)
        {
            return _prepared && _completedCapacity >= RequiredCapacity && currentMax == _completedCapacity;
        }
    }
}
