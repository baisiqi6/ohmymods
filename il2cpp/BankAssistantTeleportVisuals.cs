using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 税收助手传送表现（离线专属）：真实位置跳变时在出发地与目的地各画一组共享传送线
/// FX（原四槽 0..3 金色横纹、新四槽 4..7 黑色竖纹），并短暂隐藏助手本体（约 0.12 游戏秒）后显形，
/// 再交还原有移动/拾币/巡逻。出发地一组是 Departure（密/长收束）、目的地一组是 Arrival
/// （少/短拉长增密），Arrival 的增长峰值精确对齐 .12 显形窗口——先线后人，而非显形同刻起播。
///
/// 两相状态（等待相 → 残影相）：
/// * Waiting（隐藏等待）：持有 renderer 与两端句柄；deadline 到点由 chokepoint 显形。
/// * Revealed（残影）：只结束隐藏等待——还原可见性并丢弃 renderer 引用；Actor 身份与
///   两个句柄保留至「下一次 Begin 或硬失效」，让 .12~.24 秒残影窗口内
///   ResetAll/Forget/borrow/换 world 仍能取消这两组；已显形后的硬清绝不再写 enabled
///   （renderer 引用已丢），不引入第三期限或新计时器。
///
/// 契约：
/// * 纯表现层：不持有金币/账目/任务归属，不生成角色、不改动画控制器、不 SetActive；
///   隐藏只动调用方传入的 Actor 根 SpriteRenderer.enabled，恢复用捕获的原值，
///   绝不无条件写 true。
/// * 在线（含 host）完全不新播 FX、不延迟：<see cref="NetworkBigBoss.IsOnline"/> 时
///   直接回退原生表现（原网络行为零改动）。
/// * 只使用 Actor 根上已确定的唯一 SpriteRenderer；取不到时 fail-open（不隐藏、
///   不等待），绝不去找不明子节点替代。
/// * 两端 Begin 都成功才隐藏；任一端无效即取消成功的一端，保持原 enabled 与零等待。
///   FX 异常一律 fail-open；Begin 内部自有告警，这里不重复加噪音。
/// * 重复传送先安全结束旧记录（两相皆可）再开始新一次；不会叠加隐藏、不会永久 hidden。
/// * 每槽至多 1 次表现（4 槽对应 4 个助手），Cancel 只针对自己 Begin 的句柄；
///   本类不调用 CoinCourierTeleportFx.Tick/Clear——Tick 由 ModPanel.Update 集中驱动
///   （与哥布林开关无关），Clear 只属于整 world 所有者。
/// * 时钟：只读 Time.time/deadline，与集中 FX 的 Time.deltaTime 同步；暂停时双双冻结，
///   本类不引入第二个实时时钟。
/// * 硬失效（actor null/指针不符/失活/离层/清理中/已借出）由 coordinator 的中央
///   chokepoint 每帧统一校验并取消+归还；移动/巡逻循环只读 <see cref="IsWaiting"/>。
/// </summary>
internal static class BankAssistantTeleportVisuals
{
    /// <summary>显形窗口：到点恢复 renderer.enabled 后立刻交还原更新流程。</summary>
    internal const float RevealDelaySeconds = 0.12f;
    /// <summary>与哥布林侧一致的统一 scale（横纹组几何按 1 倍绘制）。</summary>
    private const float FxScale = 1f;

    private enum Phase
    {
        Idle,
        Waiting,
        Revealed
    }

    private sealed class SlotState
    {
        internal Phase Phase;
        // 两相都保留：残影期内 ResetAll/Forget/borrow 仍要按身份取消。
        internal GameObject Actor;
        internal int ActorInstanceId;
        // 仅 Waiting 相持有；Reveal 后丢弃，硬清绝不再写 enabled。
        internal SpriteRenderer Renderer;
        internal bool OriginalEnabled;
        internal CoinCourierFxHandle FromHandle;
        internal CoinCourierFxHandle ToHandle;
        internal float RevealAt;
    }

    // 槽位与 coordinator 的 8 个 helper 一一对应；只保存窄表现状态。
    private static readonly SlotState[] Slots =
    {
        new SlotState(), new SlotState(), new SlotState(), new SlotState(),
        new SlotState(), new SlotState(), new SlotState(), new SlotState()
    };
    private static readonly HashSet<string> Warned = new();

    /// <summary>移动/巡逻循环的只读等待谓词：仅隐藏等待相需要跳过位置推进。</summary>
    internal static bool IsWaiting(int index)
        => index >= 0 && index < Slots.Length && Slots[index].Phase == Phase.Waiting;

    /// <summary>该槽是否仍有自有状态（等待或残影），供 chokepoint 免做无谓的原生读取。</summary>
    internal static bool NeedsValidation(int index)
        => index >= 0 && index < Slots.Length && Slots[index].Phase != Phase.Idle;

    /// <summary>
    /// 真实位置跳变（TryAssign 接近/TeleportHomeAndDeposit 回家）的表现入口。
    /// 调用方保证两端确有位移；本方法在线时直接返回 false（原生表现原样）。
    /// 返回是否进入隐藏等待窗口。
    /// </summary>
    internal static bool NotifyTeleport(int index, GameObject actor, Vector3 fromPosition, Vector3 toPosition)
    {
        if (index < 0 || index >= Slots.Length) return false;
        // 在线（含 host）：完全不新播 FX、不延迟，保持现网络行为。
        if (NetworkBigBoss.IsOnline) return false;
        if (actor == null) return false;

        SlotState slot = Slots[index];
        // 下一次 Begin：旧记录无论处于等待相还是残影相，先安全结束再开始新一次。
        if (slot.Phase != Phase.Idle) Cancel(slot);

        SpriteRenderer renderer;
        try { renderer = actor.GetComponent<SpriteRenderer>(); }
        catch (Exception e)
        {
            WarnOnce("renderer read failed: " + e.GetType().Name);
            return false;
        }
        if (renderer == null)
        {
            WarnOnce("assistant actor has no root SpriteRenderer; teleport stays native-visible");
            return false;
        }

        CoinCourierFxHandle fromHandle = default;
        CoinCourierFxHandle toHandle = default;
        try
        {
            // 两端都显式传角色自己的排序；3 参重载是 0/0，本仓库有 sortingLayerID
            // 不可见的前科，绝不能用默认排序。样式也按槽位在这里定死：原四槽（0..3）
            // 横纹、新四槽（4..7）竖纹；阈值复用 PatchEconomy_BankAssistants.OriginalSlotCount
            // （槽位划分同一 owner），出发/到达两端必须同样式。
            int sortingLayerId = renderer.sortingLayerID;
            int sortingOrder = renderer.sortingOrder;
            CoinCourierTeleportStyle style = index < PatchEconomy_BankAssistants.OriginalSlotCount
                ? CoinCourierTeleportStyle.Horizontal
                : CoinCourierTeleportStyle.Vertical;
            Color color = new Color(0.95f, 0.82f, 0.42f, 0.85f);
            // 两端方向化：出发地收束（Departure）、目的地入场（Arrival）；Arrival 的增长峰值
            // 精确对齐本类的显形窗口 RevealDelaySeconds，做到"先线后人"。renderer 作为身体来源：
            // Begin 内一次性解析可见 alpha 框并 fit；解析失败返回无效 → 下方按失败路径取消并保持原状。
            fromHandle = CoinCourierTeleportFx.Begin(fromPosition, color, FxScale, sortingLayerId, sortingOrder,
                style, CoinCourierTeleportDirection.Departure, RevealDelaySeconds, renderer);
            toHandle = CoinCourierTeleportFx.Begin(toPosition, color, FxScale, sortingLayerId, sortingOrder,
                style, CoinCourierTeleportDirection.Arrival, RevealDelaySeconds, renderer);
        }
        catch (Exception)
        {
            // 表现层异常绝不传播：取消可能已建的一端，回退原生可见表现。
            // Begin 内部失败已有自己的告警，这里不重复加噪音。
            CancelOwned(fromHandle);
            CancelOwned(toHandle);
            return false;
        }
        // 两端都成功才隐藏：任一端无效即取消成功端，保持原 enabled 与零等待。
        if (!fromHandle.IsValid || !toHandle.IsValid)
        {
            CancelOwned(fromHandle);
            CancelOwned(toHandle);
            return false;
        }

        slot.Phase = Phase.Waiting;
        slot.Actor = actor;
        slot.ActorInstanceId = actor.GetInstanceID();
        slot.Renderer = renderer;
        slot.OriginalEnabled = renderer.enabled;
        slot.FromHandle = fromHandle;
        slot.ToHandle = toHandle;
        slot.RevealAt = Time.time + RevealDelaySeconds;
        renderer.enabled = false;
        return true;
    }

    /// <summary>
    /// 中央 chokepoint：coordinator 在全部生命周期门之后（任何消费者之前）对全部 4 槽
    /// 每帧调用一次（仅在 <see cref="NeedsValidation"/> 为真时）。硬失效（清理中/已借出/
    /// 离层/指针不符/失活）两相通用 → 取消两端自有 FX 并归还（残影相只取消句柄）；
    /// Waiting 相 deadline 到点只显形——两端横纹仍在共享池里自然播完
    /// （角色在特效中显形正是设计目标，绝不半途切断）。
    /// </summary>
    internal static void ValidateSlot(int index, GameObject currentActor, bool reserved,
        bool cleanupPending, bool inCurrentLayer)
    {
        if (index < 0 || index >= Slots.Length) return;
        SlotState slot = Slots[index];
        if (slot.Phase == Phase.Idle) return;
        bool sameActor = slot.Actor != null && currentActor != null
            && slot.Actor.Pointer == currentActor.Pointer;
        bool invalid = cleanupPending || reserved || !inCurrentLayer || !sameActor
            || !slot.Actor.activeInHierarchy;
        if (invalid)
        {
            Cancel(slot);
            return;
        }
        if (slot.Phase == Phase.Waiting && Time.time >= slot.RevealAt) Reveal(slot);
    }

    /// <summary>立即结束该槽记录（等待相：取消句柄+归还；残影相：只取消句柄）。</summary>
    internal static void EndSlot(int index)
    {
        if (index < 0 || index >= Slots.Length) return;
        Cancel(Slots[index]);
    }

    /// <summary>
    /// 池回收/失活入口（BankAssistantVisualLifecycle.OnDisable 按当前 instanceId 调用）：
    /// 两相都按身份匹配；在旧 life 的停止阶段归还，绝不把旧的隐藏状态或恢复动作带进新 life。
    /// </summary>
    internal static void Forget(int instanceId)
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            SlotState slot = Slots[i];
            if (slot.Phase != Phase.Idle && slot.ActorInstanceId == instanceId) Cancel(slot);
        }
    }

    /// <summary>
    /// deadline 显形：只结束隐藏等待——还原可见性并丢弃 renderer 引用；Actor 身份与两个
    /// 句柄保留到下一次 Begin 或硬失效（残影窗口内仍可被 ResetAll/Forget/borrow/换 world
    /// 取消），已显形后的硬清绝不再写 enabled。
    /// </summary>
    private static void Reveal(SlotState slot)
    {
        SpriteRenderer renderer = slot.Renderer;
        bool originalEnabled = slot.OriginalEnabled;
        slot.Renderer = null;
        slot.Phase = Phase.Revealed;
        if (renderer != null) renderer.enabled = originalEnabled;
    }

    /// <summary>硬结束：取消两个自有句柄（仅本代）；等待相归还捕获的原 enabled，残影相不再写。</summary>
    private static void Cancel(SlotState slot)
    {
        slot.Phase = Phase.Idle;
        CoinCourierFxHandle fromHandle = slot.FromHandle;
        CoinCourierFxHandle toHandle = slot.ToHandle;
        slot.FromHandle = default;
        slot.ToHandle = default;
        SpriteRenderer renderer = slot.Renderer;
        bool originalEnabled = slot.OriginalEnabled;
        slot.Renderer = null;
        slot.Actor = null;
        CancelOwned(fromHandle);
        CancelOwned(toHandle);
        // 恢复捕获的原值；renderer 已随对象销毁时跳过（fake-null），绝不无条件写 true。
        if (renderer != null) renderer.enabled = originalEnabled;
    }

    private static void CancelOwned(CoinCourierFxHandle handle)
    {
        if (!handle.IsValid) return;
        try { CoinCourierTeleportFx.Cancel(handle); }
        catch (Exception) { }
    }

    private static void WarnOnce(string reason)
    {
        if (!Warned.Add(reason)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(
                "[BankAssistantTeleportVisuals] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：表现层绝不因日志失败而中断。
        }
    }
}
