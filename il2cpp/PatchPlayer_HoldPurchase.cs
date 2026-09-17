using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 长按续买（默认关闭的独立开关）。
///
/// Root 契约（由 operator 接线，本文件只引用不定义）：
///   ModConfig.HoldPurchaseEnabled 为 ConfigEntry&lt;bool&gt;；
///   OptionalQoLScope.IsActive 为当前世界闸门（ModEnabled 加所有明确 world，不硬编码任何世界）；
///   OptionalQoLScope.IsCurrent(Component) 判定对象仍在当前 world 层与 scene；
///   ModPanel.IsShown 为面板可见标记（打开时立即取消本次 hold）；
///   ModPanel.Update 里调用 PatchPlayer_HoldPurchase.Tick()（剪枝与关闭清理，非必需路径）。
///
/// 行为：
/// 1. 开关关闭、scope 非 Active、面板打开时完整原版：不写任何字段、不合成输入、不留状态；
/// 2. 开启时，同一名本地玩家对同一家白名单商店连续按住购买键满 0.6 秒后，
///    Holding 阈值与第一枚金币仍按原版；同一次按住内的后续金币改用缩短后的
///    timeBetweenCoins（原值 1/4，不低于 0.03 秒，且绝不放慢）；
/// 3. 一次原生交易完整结束且确认为成功后，只要按键仍未松开、仍是同一家店、店仍可付款且
///    付得起，就在随后的 None 阶段把本次 UpdatePayState 的 payKeyDown 合成为 true，
///    让原生 None 分支自己再次进入 Holding；金币与扣款全部走原生路径；
/// 4. 成功证明分两条路（2.4 反汇编核对 native-disassembly.txt）：
///    主机/离线由 TransactionComplete 直接调用 PerformPay；
///    客机由 TransactionComplete 写 forceBlockPayment 后等 RPC，回包时 RecvPay 才调用
///    PerformPay（拒绝只清 forceBlockPayment 并退款，绝不 PerformPay）。
///    两者都用 Payable.PerformPay 的 Postfix 记成功回执；客机 Completed 转 None 只是本地
///    投币完成，此时进入等待态，不合成、不加速，直到回执或拒绝；
/// 5. 松开、面板打开、暂停或菜单停帧、换店、选到别店（等待期选中变 null 属正常，保留
///    sameShop 身份）、走出可支付距离、等待超时、钱不足、货架满、关闭开关、
///    换 world/layer/scene 都会结束这次 hold，必须重新按下才会对新店续买。
///
/// 明确不做：不直接调用 TransactionComplete 或 PerformPay、不改钱包余额、不自己生成金币、
/// 不改价格、不改 keyDownThreshold、不改 timeBeforeTransaction 与 timeToReachIndicator
/// （金币必须真实飞到槽位，落槽等待由 DroppableCurrency.MoveTimeUntilNearTarget 决定）、
/// 不碰 ByteBuffer 与 forceBlockPayment（只读它判断客机是否仍在等待回包）。
/// 只处理本地输入：hasLocalAuthority 为 true 且 TunnelInput 为 false 的 player，
/// 远端玩家（客机托管输入）一概不接管。
///
/// 白名单按 PayableShop 的 GameObject tag 判定（tag 名与 PayableShop.GetShopTag 一致，
/// PayableShop.Pay 也按这些 tag 记统计）：ShopBow、ShopHammer、ShopScythe、
/// ShopNinjaLeft、ShopNinjaRight、ShopPikeLeft、ShopPikeRight、LeftShieldShop、
/// RightShieldShop；面包店按同一 GameObject 上的 Baker 组件识别
/// （沿用 AutoRestockCounts.ClassifyShop 的既有识别法）。ChangeRuler、ChangeItem、
/// Workshop、Forge、祭坛、换坐骑等仍排除。额外只开放 PayableWorkshopBarrel，及同GO
/// FireTower精确拥有的PayableComponent；这两类每次加速/续买/回执均重验，禁止资格缓存串用。
///
/// 2.1.0 源与 interop API 核对（actual-api.json / Player.cs / Payable.cs / PayableShop.cs）：
///   Player.UpdatePayState(bool,bool,bool)：存在（token 100675566）。
///   Payable.PerformPay()：存在（token 100674502，反汇编 0x667d30，448 字节）。
///   Player._payState / _payTimer / timeBetweenCoins / selectedPayable / coins /
///   hasLocalAuthority / TunnelInput / actionState：全部存在，interop 可读写。
///   PayState 取值：None=0, Holding=1, Transaction=2, Completed=3, Cancelling=7（私有枚举，按数值比较）。
///   Payable.CanPay(Player) / CanSelect(Player) / PlayerPayPoint() / Price / Currency /
///   interactingPlayer / playerPayDistance / forceBlockPayment(+0x100)：公开成员，直接访问。
///   本文件不改 Player.AddCurrencyToSelectedPayable，也不碰 DroppableCurrency 字段。
/// </summary>
[HarmonyPatch(typeof(Player))]
public static class PatchPlayer_HoldPurchase
{
    /// <summary>连续按住多久之后才允许加速与续买合成。</summary>
    internal const float HoldSeconds = 0.6f;
    /// <summary>加速后的金币间隔相对原值的比例。</summary>
    internal const float CoinIntervalFraction = 0.25f;
    /// <summary>加速下限，同时保证绝不把原值改慢（原值更小则原样保留）。</summary>
    internal const float MinCoinInterval = 0.03f;
    /// <summary>本地帧间隔超过该秒数视为暂停/菜单/读档停帧，结束本次 hold。</summary>
    internal const float StallSeconds = 0.5f;
    /// <summary>与原生 None 分支相同的 0.5 格贴身距离。</summary>
    internal const float PayRangeLimit = 0.5f;
    /// <summary>客机等待 RPC 回包的上限，超时结束本次 hold（不无限等待）。</summary>
    internal const float AwaitReceiptSeconds = 5f;
    /// <summary>字段归还退避：基数与上限，无限重试但不打爆帧。</summary>
    internal const float RestoreBackoffBase = 0.25f;
    internal const float RestoreBackoffMax = 5f;
    private const int NoteBudget = 8;

    private const int StateNone = 0;
    private const int StateHolding = 1;
    private const int StateTransaction = 2;
    private const int StateCompleted = 3;
    private const int StateCancelling = 7;

    /// <summary>Player.ActionState 数值（Walk=0, Run=1, Stand=2, Eat=3, Spit=4, Rear=5, ManualAttack=6, Transformed=7, Immobile=8, Glide=9）。</summary>
    private const int ActionStateRun = 1;
    private const int ActionStateTransformed = 7;

    /// <summary>白名单商店的 GameObject tag，名字取自 PayableShop.GetShopTag。</summary>
    private static readonly string[] GoodsShopTags =
    {
        "ShopBow", "ShopHammer", "ShopScythe",
        "ShopNinjaLeft", "ShopNinjaRight",
        "ShopPikeLeft", "ShopPikeRight",
        "LeftShieldShop", "RightShieldShop"
    };

    /// <summary>
    /// 一次连续按住的状态。键为 player.Pointer，身份同时记录 player/world/layer/scene/商店，
    /// 不共享计时器、不跨世界复用。
    /// </summary>
    internal sealed class Session
    {
        internal Player Player;
        internal IntPtr PlayerPtr;
        /// <summary>player GameObject 实例身份（0 表示读取失败，此时只按 Pointer 判）。</summary>
        internal int PlayerInstanceId;
        internal Context Scope;
        internal Payable Shop;
        internal IntPtr ShopPtr;
        internal int ShopInstanceId;
        internal bool ShopIsGoods;
        internal bool RecheckAmmoGoods;
        internal IntPtr AmmoOwnerPtr;
        internal float HeldElapsed;
        internal float LastUnscaledTime;
        internal bool ContinuationArmed;
        /// <summary>本帧正处于原生 Completed 分支（主机 PerformPay 只可能在这一窗口内命中）。</summary>
        internal bool Submitting;
        /// <summary>客机已提交、等待 RPC 回包：保留 sameShop 身份，不合成、不加速。</summary>
        internal bool AwaitingReceipt;
        internal float AwaitDeadline;
        /// <summary>PerformPay 成功回执（主机同步、客机回包）。</summary>
        internal bool SuccessReceipt;
        internal bool HasPendingRestore;
        internal bool IsQueued;
        internal float PendingOriginal;
        internal float PendingApplied;
        internal int RestoreAttempts;
        internal float NextRestoreAttempt;
    }

    /// <summary>world 指针加 gameLayer 指针加 scene handle 的联合身份。</summary>
    internal readonly struct Context : IEquatable<Context>
    {
        private readonly IntPtr _worldPtr;
        private readonly IntPtr _layerPtr;
        private readonly int _sceneHandle;

        private Context(IntPtr worldPtr, IntPtr layerPtr, int sceneHandle)
        {
            _worldPtr = worldPtr;
            _layerPtr = layerPtr;
            _sceneHandle = sceneHandle;
        }

        internal static bool TryCapture(Component component, out Context context)
        {
            context = default;
            try
            {
                if (component == null) return false;
                GameObject go = component.gameObject;
                if (go == null) return false;
                Managers managers = Managers.Inst;
                World world = managers != null ? managers.world : null;
                Transform layer = world != null ? world.gameLayer : null;
                if (layer == null) return false;
                Transform self = component.transform;
                if (self == null || !self.IsChildOf(layer)) return false;
                context = new Context(world.Pointer, layer.Pointer, go.scene.handle);
                return true;
            }
            catch
            {
                context = default;
                return false;
            }
        }

        public bool Equals(Context other)
            => _worldPtr == other._worldPtr && _layerPtr == other._layerPtr
                && _sceneHandle == other._sceneHandle;

        public override bool Equals(object obj) => obj is Context other && Equals(other);

        public override int GetHashCode()
            => _sceneHandle * 397 ^ _worldPtr.GetHashCode() ^ _layerPtr.GetHashCode();
    }

    /// <summary>单次原生调用内的局部凭据，按值传给 Postfix/Finalizer，不产生堆分配。</summary>
    internal struct FrameReceipt
    {
        internal Session Session;
        internal int StateBefore;
        internal bool Injected;
        internal bool Overrode;
        internal float OriginalInterval;
        internal float AppliedInterval;
    }

    private static readonly Dictionary<IntPtr, Session> _sessions = new Dictionary<IntPtr, Session>(4);
    private static readonly List<IntPtr> _scratchKeys = new List<IntPtr>(4);
    private static readonly List<Session> _scratchSessions = new List<Session>(4);
    private static readonly List<Session> _restoreQueue = new List<Session>(4);
    private static int _notes;
    private static bool _injectionFaultLogged;
    private static bool _hostReceiptMissLogged;
    private static bool _denialLogged;
    private static bool _restoreRetryLogged;

    /// <summary>当前挂着的 hold 会话数（测试与诊断用）。</summary>
    internal static int ActiveSessionCount => _sessions.Count;

    /// <summary>待归还的字段 receipt 数（测试与诊断用）。</summary>
    internal static int PendingRestoreCount => _restoreQueue.Count;

    // ------------------------------------------------------------------ hooks

    [HarmonyPatch(typeof(Player), "UpdatePayState")]
    [HarmonyPrefix]
    private static void UpdatePayState_Prefix(Player __instance, bool __0, ref bool __1,
        out FrameReceipt __state)
        => BeforePayUpdate(__instance, __0, ref __1, out __state);

    [HarmonyPatch(typeof(Player), "UpdatePayState")]
    [HarmonyPostfix]
    private static void UpdatePayState_Postfix(Player __instance, bool __0, FrameReceipt __state)
        => AfterPayUpdate(__instance, __0, __state);

    [HarmonyPatch(typeof(Player), "UpdatePayState")]
    [HarmonyFinalizer]
    private static void UpdatePayState_Finalizer(Exception __exception, Player __instance,
        FrameReceipt __state)
        => FinishPayUpdate(__instance, __exception, __state);

    [HarmonyPatch(typeof(Payable), "PerformPay")]
    [HarmonyPostfix]
    private static void PerformPay_Postfix(Payable __instance)
        => OnPerformPay(__instance);

    // ------------------------------------------------------------- heartbeat

    /// <summary>
    /// 由 root 在 ModPanel.Update 调用：面板打开、开关关闭、离开世界、停帧时丢弃全部会话并
    /// 归还字段，重试失败的字段归还 receipt，剪掉已销毁、已不在当前层或等待超时的 player。
    /// 核心行为不依赖 Tick：钩子自身每帧也做同样的判定。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            // receipt 重试与开关/世界状态无关：只要写过字段就必须能归还，直到写回成功、
            // 原值已恢复或对象确已销毁。
            for (int i = _restoreQueue.Count - 1; i >= 0; i--)
            {
                Session queued = _restoreQueue[i];
                if (!RetryPendingRestore(queued)) continue;
                queued.IsQueued = false;
                _restoreQueue.RemoveAt(i);
            }

            if (ModPanel.IsShown || !FeatureOn() || !OptionalQoLScope.IsActive
                || Time.timeScale <= 0f)
            {
                ResetAll();
                return;
            }

            _scratchKeys.Clear();
            foreach (KeyValuePair<IntPtr, Session> pair in _sessions)
            {
                Session session = pair.Value;
                if (!SessionUsable(pair.Key, session)
                    || !AmmoGoodsStillValid(session)
                    || (session.AwaitingReceipt && Time.unscaledTime > session.AwaitDeadline))
                    _scratchKeys.Add(pair.Key);
            }
            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                if (_sessions.TryGetValue(_scratchKeys[i], out Session stale)) Drop(stale);
            }
        }
        catch (Exception e)
        {
            Note("tick", e);
        }
    }

    /// <summary>丢弃全部本地会话（面板打开、开关关闭、离开世界、停帧、退出清理）。</summary>
    internal static void ResetAll()
    {
        if (_sessions.Count == 0) return;
        _scratchKeys.Clear();
        foreach (KeyValuePair<IntPtr, Session> pair in _sessions) _scratchKeys.Add(pair.Key);
        for (int i = 0; i < _scratchKeys.Count; i++)
        {
            if (_sessions.TryGetValue(_scratchKeys[i], out Session session)) Drop(session);
        }
    }

    // ------------------------------------------------------------------ logic

    internal static void BeforePayUpdate(Player player, bool payKey, ref bool payKeyDown,
        out FrameReceipt receipt)
    {
        receipt = default;
        try
        {
            if (player == null) return;

            if (ModPanel.IsShown || !FeatureOn() || !OptionalQoLScope.IsActive)
            {
                DropFor(player);
                return;
            }
            if (!player.hasLocalAuthority || player.TunnelInput)
            {
                DropFor(player);
                return;
            }
            if (Time.timeScale <= 0f)
            {
                DropFor(player);
                return;
            }

            Context context;
            if (!Context.TryCapture(player, out context) || !OptionalQoLScope.IsCurrent(player))
            {
                DropFor(player);
                return;
            }

            int state = ReadState(player);
            receipt.StateBefore = state;

            Session session = Find(player);
            if (session != null && !session.Scope.Equals(context))
            {
                Drop(session);
                session = null;
            }

            float now = Time.unscaledTime;
            if (session != null && session.LastUnscaledTime > 0f
                && now - session.LastUnscaledTime > StallSeconds)
            {
                // 暂停、菜单、读档等停帧期间钩子不再被调用：恢复调用即结束本次 hold。
                Drop(session);
                session = null;
            }

            if (!payKey)
            {
                if (session != null) Drop(session);
                return;
            }

            if (session == null)
            {
                if (!payKeyDown) return; // 只有真实按下才开始一次 hold，合成按下不会自我续期
                session = Create(player, context, now);
            }

            float delta = Time.deltaTime;
            if (delta > 0f && float.IsFinite(delta)) session.HeldElapsed += delta;
            session.LastUnscaledTime = now;
            receipt.Session = session;

            if (!AmmoGoodsStillValid(session))
            {
                Drop(session); receipt.Session = null; return;
            }

            // 客机等待回包：保留 sameShop 身份，不合成、不加速；成功、拒绝与超时在这里定论。
            if (session.AwaitingReceipt)
            {
                if (session.SuccessReceipt)
                {
                    session.SuccessReceipt = false;
                    session.AwaitingReceipt = false;
                    session.ContinuationArmed = true;
                }
                else if (!ShopAlive(session) || !ShopInReach(session, player)
                    || now > session.AwaitDeadline)
                {
                    Drop(session);
                    receipt.Session = null;
                    return;
                }
                else if (TryReadForceBlock(session.Shop, out bool blocked) && !blocked)
                {
                    // 回包已解除 forceBlockPayment 却没有成功回执：原生走的是拒绝加退款分支，
                    // 不重试、不续买，结束本次 hold 要求重新按下。
                    if (!_denialLogged)
                    {
                        _denialLogged = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                            "[HoldPurchase] client payment was denied (forceBlockPayment cleared without PerformPay), hold dropped");
                    }
                    Drop(session);
                    receipt.Session = null;
                    return;
                }
                return;
            }

            if (!KeepBoundShop(session, player))
            {
                Drop(session);
                receipt.Session = null;
                return;
            }
            if (!AmmoGoodsStillValid(session))
            {
                Drop(session); receipt.Session = null; return;
            }

            if (state == StateCompleted && session.Shop != null
                && SameShop(session, player.selectedPayable))
            {
                // 主机 PerformPay 只会在本帧的原生 Completed 分支内同步发生，
                // 该窗口之外到达的 PerformPay 属于银行补货或别的交易。
                session.Submitting = true;
            }

            if (state == StateNone && !payKeyDown && session.ContinuationArmed
                && session.HeldElapsed >= HoldSeconds)
            {
                if (session.Shop != null && session.ShopIsGoods
                    && CanEnterHolding(player, session.Shop))
                {
                    payKeyDown = true;
                    session.ContinuationArmed = false;
                    receipt.Injected = true;
                }
                else
                {
                    // 钱不足、货架满、被别的玩家占用、店失效、走远：结束本次 hold，要求重新按下。
                    Drop(session);
                    receipt.Session = null;
                    return;
                }
            }

            if (session.ShopIsGoods && session.HeldElapsed >= HoldSeconds
                && (state == StateHolding || state == StateTransaction))
            {
                if (HasPendingRestoreFor(player))
                {
                    // 同一 player 还有未归还的 owned 字段凭据（本会话的，或松手丢弃后进入重试
                    // 队列的旧会话的）：先尝试归还，失败则旧 receipt 原样保留；本轮绝不写入——
                    // 绝不把残留的临时值重新当基线再缩一次（0.1 不能变成 0.03 叠加）。
                    RetryPendingFor(player);
                }
                else
                {
                    OverrideCoinInterval(session, player, ref receipt);
                }
            }
        }
        catch (Exception e)
        {
            if (!receipt.Overrode) receipt.Session = null;
            DropFor(player);
            Note("prefix", e);
        }
    }

    internal static void AfterPayUpdate(Player player, bool payKey, FrameReceipt receipt)
    {
        try
        {
            Session session = receipt.Session;
            if (session != null) session.Submitting = false;
            if (receipt.Overrode) ReturnInterval(player, receipt);

            if (session == null || player == null || !TryGetActive(session, player)) return;

            int after = ReadState(player);

            if (receipt.Injected)
            {
                if (after != StateHolding)
                {
                    // 合成的按下没能进入 Holding：原生可能落到了掉地币分支，立即结束本次 hold
                    // 并留一次告警；绝不在同一 hold 里重试。
                    if (!_injectionFaultLogged)
                    {
                        _injectionFaultLogged = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                            "[HoldPurchase] synthetic payKeyDown did not enter Holding (state="
                            + after + "), hold session dropped");
                    }
                    Drop(session);
                    return;
                }
            }

            if (session.Shop == null && after != StateNone)
                Bind(session, player.selectedPayable);

            if (!AmmoGoodsStillValid(session)) { Drop(session); return; }

            if (!ShopSelectionAcceptable(session, player))
            {
                Drop(session);
                return;
            }

            if (after == StateCancelling || receipt.StateBefore == StateCancelling)
                session.ContinuationArmed = false;

            if (receipt.StateBefore == StateCompleted && after == StateNone && payKey
                && session.Shop != null && session.ShopIsGoods)
            {
                // 原生成功交易完整结束（Completed 转 None 即 TransactionComplete 已执行）。
                if (session.SuccessReceipt)
                {
                    session.SuccessReceipt = false;
                    session.AwaitingReceipt = false;
                    session.ContinuationArmed = true;
                }
                else if (NetworkBigBoss.HasWorldAuth)
                {
                    // 主机/离线：Completed 转 None 只可能来自 TransactionComplete，其内部直接
                    // 调用 PerformPay，必然成功；回执缺失只说明副本没命中，记一次并继续。
                    if (!_hostReceiptMissLogged)
                    {
                        _hostReceiptMissLogged = true;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                            "[HoldPurchase] host PerformPay receipt missing, continuing on the native success path");
                    }
                    session.ContinuationArmed = true;
                }
                else if (TryReadForceBlock(session.Shop, out bool blocked) && blocked)
                {
                    // 客机：本地投币完成，但购买成功与否要等 RPC 回包。
                    session.AwaitingReceipt = true;
                    session.AwaitDeadline = Time.unscaledTime + AwaitReceiptSeconds;
                    session.ContinuationArmed = false;
                }
                else
                {
                    session.AwaitingReceipt = false;
                    session.ContinuationArmed = false;
                }
            }
        }
        catch (Exception e)
        {
            if (receipt.Overrode) ReturnInterval(player, receipt);
            DropFor(player);
            Note("postfix", e);
        }
    }

    /// <summary>
    /// Finalizer 会被 driver 每帧调用（正常成功也调用，exception 为 null）：
    /// 无条件归还本帧 owned 字段并关闭 Submitting 窗口；只有原生真的抛异常时才丢弃会话，
    /// 正常成功路径不得清掉 Postfix 刚建立的状态（续买窗口、等待标志、已 arm 的会话）。
    /// </summary>
    internal static void FinishPayUpdate(Player player, Exception exception, FrameReceipt receipt)
    {
        try
        {
            Session session = receipt.Session;
            if (session != null) session.Submitting = false;
            if (receipt.Overrode) ReturnInterval(player, receipt);
            if (exception == null) return;
            if (session != null && player != null && TryGetActive(session, player)) Drop(session);
        }
        catch (Exception e)
        {
            Note("finalizer", e);
        }
    }

    /// <summary>
    /// Payable.PerformPay Postfix：唯一的成功回执来源（反汇编 0x667d30）。
    /// 主机与离线由 TransactionComplete 内同步调用；客机由 RecvPay 在正常回包时调用，
    /// 拒绝分支只清 forceBlockPayment 加退款，绝不进入这里。
    /// 只认当前跟踪的同一家店、且付款人就是本会话 player 的交易；银行补货与别的玩家的
    /// 同店交易（interactingPlayer 不是本会话 player，或不在本会话的 Completed 窗口内）不算。
    /// </summary>
    internal static void OnPerformPay(Payable payable)
    {
        try
        {
            if (payable == null) return;
            IntPtr shopPointer = payable.Pointer;
            if (shopPointer == IntPtr.Zero) return;

            _scratchSessions.Clear();
            foreach (KeyValuePair<IntPtr, Session> pair in _sessions) _scratchSessions.Add(pair.Value);
            for (int i = 0; i < _scratchSessions.Count; i++)
            {
                Session session = _scratchSessions[i];
                if (session.Shop == null || session.ShopPtr != shopPointer) continue;
                if (!AmmoGoodsStillValid(session)) { Drop(session); continue; }

                Player payer = payable.interactingPlayer;
                if (payer == null || payer.Pointer != session.PlayerPtr) continue;

                if (session.AwaitingReceipt)
                {
                    // 客机回包路径：只记回执，等待标志保留给下一帧 Prefix 消费（消费时才 arm）。
                    // 等待期原生会把 selectedPayable 清成 null，过早清标志会让同一帧之后的
                    // KeepBoundShop 判定成「丢店」而直接丢会话。
                    session.SuccessReceipt = true;
                    continue;
                }
                if (!session.Submitting) continue;
                // 主机/离线路径：只在原生 Completed 分支内、且本 player 正选着这家店时认账。
                if (ReadState(session.Player) != StateCompleted) continue;
                if (!SameShop(session, session.Player.selectedPayable)) continue;
                session.SuccessReceipt = true;
            }
            _scratchSessions.Clear();
        }
        catch (Exception e)
        {
            Note("performPay", e);
        }
    }

    // ------------------------------------------------------------- primitives

    private static bool FeatureOn()
    {
        if (ModConfig.HoldPurchaseEnabled == null) return false;
        return ModConfig.HoldPurchaseEnabled.Value;
    }

    private static int ReadState(Player player)
    {
        try
        {
            return (int)player._payState;
        }
        catch
        {
            return -1;
        }
    }

    private static Session Find(Player player)
    {
        try
        {
            if (_sessions.TryGetValue(player.Pointer, out Session session)
                && session.Player != null && session.Player.Pointer == player.Pointer)
                return session;
        }
        catch
        {
        }
        return null;
    }

    private static bool TryGetActive(Session session, Player player)
    {
        try
        {
            return _sessions.TryGetValue(session.PlayerPtr, out Session current)
                && ReferenceEquals(current, session)
                && session.Player != null && session.Player.Pointer == player.Pointer;
        }
        catch
        {
            return false;
        }
    }

    private static Session Create(Player player, Context context, float now)
    {
        var session = new Session
        {
            Player = player,
            PlayerPtr = player.Pointer,
            PlayerInstanceId = PlayerInstanceIdOf(player),
            Scope = context,
            LastUnscaledTime = now
        };
        _sessions[session.PlayerPtr] = session;
        return session;
    }

    /// <summary>player 是否还活着、还是本地输入、还在同一 world 层与 scene。</summary>
    private static bool SessionUsable(IntPtr key, Session session)
    {
        try
        {
            Player player = session.Player;
            if (player == null || player.gameObject == null) return false;
            if (player.Pointer != key) return false;
            if (!player.hasLocalAuthority || player.TunnelInput) return false;
            if (!OptionalQoLScope.IsCurrent(player)) return false;
            Context context;
            return Context.TryCapture(player, out context) && context.Equals(session.Scope);
        }
        catch
        {
            return false;
        }
    }

    private static void DropFor(Player player)
    {
        if (player == null) return;
        try
        {
            if (_sessions.TryGetValue(player.Pointer, out Session session)) Drop(session);
        }
        catch
        {
        }
    }

    private static void Drop(Session session)
    {
        if (session == null) return;
        if (_sessions.TryGetValue(session.PlayerPtr, out Session current)
            && ReferenceEquals(current, session))
            _sessions.Remove(session.PlayerPtr);
        if (session.HasPendingRestore)
        {
            session.NextRestoreAttempt = 0f;
            if (!RetryPendingRestore(session)) EnsureQueued(session);
        }
    }

    /// <summary>把待归还的 receipt 排进重试队列（按会话去重）。</summary>
    private static void EnsureQueued(Session session)
    {
        if (session == null || session.IsQueued) return;
        session.IsQueued = true;
        _restoreQueue.Add(session);
    }

    private static int PlayerInstanceIdOf(Player player)
    {
        try
        {
            GameObject go = player != null ? player.gameObject : null;
            return go != null ? go.GetInstanceID() : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 凭据是否属于这个 player：player Pointer 加 GameObject 实例身份（读过实例号才比实例号，
    /// 避免 Pointer 复用后把别人的残留凭据算到新对象头上）。
    /// </summary>
    private static bool PendingOwnerMatches(Session session, Player player)
    {
        if (session == null || player == null) return false;
        try
        {
            if (session.PlayerPtr != player.Pointer) return false;
            GameObject go = player.gameObject;
            if (go == null) return false;
            if (session.PlayerInstanceId != 0 && go.GetInstanceID() != session.PlayerInstanceId)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 该 player 是否还有未归还的 owned 字段凭据：可能是活跃会话上的，也可能是松手丢弃后
    /// 进入重试队列的旧会话上的。命中时调用方只允许「尝试归还」，绝不允许用残留值重新构造
    /// 基线写入（否则会把 0.1 当原值再缩到 0.03 叠加），也绝不丢旧 receipt。
    /// </summary>
    private static bool HasPendingRestoreFor(Player player)
    {
        if (player == null) return false;
        for (int i = 0; i < _restoreQueue.Count; i++)
        {
            if (_restoreQueue[i].HasPendingRestore && PendingOwnerMatches(_restoreQueue[i], player))
                return true;
        }
        foreach (KeyValuePair<IntPtr, Session> pair in _sessions)
        {
            if (pair.Value.HasPendingRestore && PendingOwnerMatches(pair.Value, player)) return true;
        }
        return false;
    }

    /// <summary>
    /// 尝试归还该 player 挂在别处的凭据。归还失败（含退避中）时凭据原样留在队列，
    /// 由后续帧或 Tick 继续重试；只有真正归还掉/外部已改写/对象已销毁才会清掉。
    /// </summary>
    private static void RetryPendingFor(Player player)
    {
        if (player == null) return;
        for (int i = _restoreQueue.Count - 1; i >= 0; i--)
        {
            Session queued = _restoreQueue[i];
            if (!PendingOwnerMatches(queued, player)) continue;
            if (!RetryPendingRestore(queued)) continue; // 失败或退避中：保留
            queued.IsQueued = false;
            _restoreQueue.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------- shop scope

    /// <summary>
    /// 绑定当前选中的店；返回 false 表示选中店已变/丢失且不允许再保留，本次 hold 必须结束。
    /// 客机等待回包、或成功回执已到但原生还没选回同店时，selectedPayable 会短暂为 null，
    /// 这时保留捕获的 sameShop 身份；一旦选中了另一家店，或者已经走出可支付距离、
    /// 目标店消失，就结束本次 hold。
    /// </summary>
    private static bool KeepBoundShop(Session session, Player player)
    {
        Payable selected = player.selectedPayable;
        if (session.Shop == null)
        {
            if (selected == null) return true; // 真实按下当帧原生还没选出店，下一帧再绑
            Bind(session, selected);
            return true;
        }
        if (SameShop(session, selected)) return true;
        if (selected == null)
        {
            if (!session.AwaitingReceipt && !session.ContinuationArmed) return false;
            return ShopAlive(session) && ShopInReach(session, player);
        }
        return false; // 选到别家店
    }

    /// <summary>选中店：等待期允许为 null，一旦选中就必须是本会话同一家店。</summary>
    private static bool ShopSelectionAcceptable(Session session, Player player)
    {
        Payable selected = player.selectedPayable;
        if (selected == null) return true;
        return SameShop(session, selected);
    }

    private static bool ShopAlive(Session session)
    {
        try
        {
            Payable shop = session.Shop;
            if (shop == null) return false;
            GameObject go = shop.gameObject;
            return go != null && go.activeInHierarchy
                && shop.Pointer == session.ShopPtr
                && go.GetInstanceID() == session.ShopInstanceId;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShopInReach(Session session, Player player)
    {
        try
        {
            Payable shop = session.Shop;
            if (shop == null) return false;
            float distance = Mathf.Abs(shop.PlayerPayPoint() - player.transform.position.x);
            // 与原生选中块一致：0.5 格与 playerPayDistance 都要满足。
            return distance <= PayRangeLimit && distance <= shop.playerPayDistance;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>只读 forceBlockPayment：客机在等待回包期间为 true，回包（成功或拒绝）后变 false。</summary>
    private static bool TryReadForceBlock(Payable shop, out bool blocked)
    {
        blocked = true;
        try
        {
            if (shop == null) return false;
            blocked = shop.forceBlockPayment;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Bind(Session session, Payable payable)
    {
        try
        {
            if (payable == null) return;
            GameObject go = payable.gameObject;
            if (go == null) return;
            IntPtr pointer = payable.Pointer;
            if (pointer == IntPtr.Zero) return;
            session.Shop = payable;
            session.ShopPtr = pointer;
            session.ShopInstanceId = go.GetInstanceID();
            session.ShopIsGoods = IsSafeGoods(payable);
            var component = payable.TryCast<PayableComponent>();
            session.RecheckAmmoGoods = component != null || payable.TryCast<PayableWorkshopBarrel>() != null;
            session.AmmoOwnerPtr = component?._owner != null ? component._owner.Pointer : IntPtr.Zero;
        }
        catch (Exception e)
        {
            // Classification may have succeeded before a later native owner/type read failed.
            // Never leave cached goods=true without the corresponding live-owner proof.
            session.ShopIsGoods = false;
            Note("bind", e);
        }
    }

    private static bool SameShop(Session session, Payable selected)
    {
        try
        {
            if (selected == null || session.Shop == null) return false;
            if (selected.Pointer != session.ShopPtr) return false;
            GameObject go = selected.gameObject;
            if (go == null) return false;
            return go.GetInstanceID() == session.ShopInstanceId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>白名单：原工具/面包店、火药桶、精确火塔owner的弹药付款点；其余不加速不续买。</summary>
    private static bool IsSafeGoods(Payable payable)
    {
        try
        {
            if (payable == null || !payable.enabled) return false;
            GameObject go = payable.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            if (payable.TryCast<PayableWorkshopBarrel>() != null) return true;
            var component = payable.TryCast<PayableComponent>();
            if (component != null)
            {
                var tower = go.GetComponent<FireTower>();
                // FireTower AI may be disabled on a client while its payment owner remains
                // valid. Native CanPay/CanSelect retain capacity/readiness/RPC decisions.
                return tower != null && tower.gameObject != null && tower.gameObject.activeInHierarchy
                    && component._owner != null && component._owner.Pointer == tower.Pointer;
            }
            for (int i = 0; i < GoodsShopTags.Length; i++)
            {
                if (go.CompareTag(GoodsShopTags[i])) return true;
            }
            // 面包店：与原作一样，PayableShop 与 Baker 挂在同一个 GameObject 上。
            return go.GetComponent<Baker>() != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool AmmoGoodsStillValid(Session session)
    {
        if (session == null || !session.RecheckAmmoGoods) return true;
        try
        {
            if (!session.ShopIsGoods || !ShopAlive(session) || !OptionalQoLScope.IsCurrent(session.Shop)
                || !Context.TryCapture(session.Shop, out var context) || !context.Equals(session.Scope)
                || !IsSafeGoods(session.Shop)) return false;
            var component = session.Shop.TryCast<PayableComponent>();
            // Even a replacement owner that is another valid FireTower cannot inherit this
            // hold or an in-flight receipt from the previous native payment owner.
            return component == null ? session.AmmoOwnerPtr == IntPtr.Zero
                : component._owner != null && component._owner.Pointer == session.AmmoOwnerPtr;
        }
        catch { return false; }
    }

    /// <summary>
    /// 复刻原生 None 加 payKeyDown 分支进入 Holding 的全部前置条件，逐条一致：
    /// ActionState 不是 Run 也不是 Transformed（原生选中块与 CanPay 都会拒绝这两态，
    /// 否则原生会先取消选中再走 else 掉地币分支）、同一家被选中的店、CanSelect、
    /// 0.5 格与 playerPayDistance 贴身、CanPay、占用者可抢占、金币价且付得起一个整价
    /// （比原生的 HasCurrency 更严，绝不写钱包）。任一条不成立就不合成。
    /// </summary>
    private static bool CanEnterHolding(Player player, Payable shop)
    {
        try
        {
            if (shop == null) return false;
            int actionState = (int)player.actionState;
            if (actionState == ActionStateRun || actionState == ActionStateTransformed) return false;
            Payable selected = player.selectedPayable;
            if (selected == null || selected.Pointer != shop.Pointer) return false;
            if (!shop.CanSelect(player)) return false;
            float distance = Mathf.Abs(shop.PlayerPayPoint() - player.transform.position.x);
            if (distance > PayRangeLimit || distance > shop.playerPayDistance) return false;
            if (!shop.CanPay(player)) return false;
            Player other = shop.interactingPlayer;
            if (other != null && other.Pointer != player.Pointer && other.gameObject != null
                && other.gameObject.activeInHierarchy) return false;
            if (shop.Currency != CurrencyType.Coins) return false;
            if (shop.Price < 1) return false;
            if (player.coins < shop.Price) return false;
            return true;
        }
        catch (Exception e)
        {
            Note("canEnter", e);
            return false;
        }
    }

    // ----------------------------------------------------------- coin cadence

    private static void OverrideCoinInterval(Session session, Player player, ref FrameReceipt receipt)
    {
        float original;
        try
        {
            original = player.timeBetweenCoins;
        }
        catch (Exception e)
        {
            Note("interval read", e);
            return;
        }
        if (!float.IsFinite(original) || original <= MinCoinInterval) return;
        float target = original * CoinIntervalFraction;
        if (target < MinCoinInterval) target = MinCoinInterval;
        if (target >= original) return; // 原值已更快：绝不放慢
        try
        {
            player.timeBetweenCoins = target;
        }
        catch (Exception e)
        {
            Note("interval write", e);
            return;
        }
        receipt.Overrode = true;
        receipt.OriginalInterval = original;
        receipt.AppliedInterval = target;
    }

    /// <summary>
    /// 归还字段。返回 true 表示这次 receipt 可以清掉：写回成功、字段已经等于原值，
    /// 或者字段被外部改写成既非原值也非本补丁写入值的第三值（外部更新优先，不覆盖）。
    /// 返回 false 表示写入或读取失败，必须保留 receipt 继续重试。
    /// </summary>
    private static bool TryReturnInterval(Player player, float original, float applied)
    {
        if (player == null || player.gameObject == null) return true; // 对象已销毁：字段随之消失
        float current;
        try
        {
            current = player.timeBetweenCoins;
        }
        catch (Exception e)
        {
            Note("interval read", e);
            return false;
        }
        if (current == original) return true;   // 已是原值
        if (current != applied) return true;    // 外部改写过：交还所有权，不覆盖
        try
        {
            player.timeBetweenCoins = original;
        }
        catch (Exception e)
        {
            Note("interval restore", e);
            return false;
        }
        try
        {
            return player.timeBetweenCoins == original;
        }
        catch
        {
            return false;
        }
    }

    private static void ReturnInterval(Player player, FrameReceipt receipt)
    {
        if (TryReturnInterval(player, receipt.OriginalInterval, receipt.AppliedInterval)) return;
        Session session = receipt.Session;
        if (session == null) return;
        if (session.Player == null) session.Player = player;
        if (!session.HasPendingRestore)
        {
            // 第一张凭据记录真正的原值；后续失败只更新 applied，绝不把临时值当成新原值。
            session.PendingOriginal = receipt.OriginalInterval;
            session.HasPendingRestore = true;
        }
        session.PendingApplied = receipt.AppliedInterval;
        EnsureQueued(session);
        if (!_restoreRetryLogged)
        {
            _restoreRetryLogged = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[HoldPurchase] timeBetweenCoins restore failed, receipt kept and retried with backoff");
        }
    }

    private static bool RetryPendingRestore(Session session)
    {
        if (session == null || !session.HasPendingRestore) return true;
        Player player = session.Player;
        if (player == null || player.gameObject == null)
        {
            ClearPending(session); // 对象确已销毁：字段随之消失，receipt 无处归还
            return true;
        }
        if (Time.unscaledTime < session.NextRestoreAttempt) return false; // 退避中
        if (TryReturnInterval(player, session.PendingOriginal, session.PendingApplied))
        {
            ClearPending(session);
            return true;
        }
        session.RestoreAttempts++;
        int shift = session.RestoreAttempts > 6 ? 6 : session.RestoreAttempts;
        float delay = RestoreBackoffBase * (1 << shift);
        if (delay > RestoreBackoffMax) delay = RestoreBackoffMax;
        session.NextRestoreAttempt = Time.unscaledTime + delay;
        return false;
    }

    private static void ClearPending(Session session)
    {
        session.HasPendingRestore = false;
        session.IsQueued = false;
    }

    // ------------------------------------------------------------------ notes

    private static void Note(string what, Exception e)
    {
        if (_notes >= NoteBudget) return;
        _notes++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[HoldPurchase] " + what + " fault: " + e.GetType().Name + " " + e.Message);
    }
}
