using System;
#if !COURIER_RUNTIME_CORE_ONLY
using System.Globalization;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

/// <summary>
/// 单次招募付款的收据（纯逻辑）：付款开始（Arm）时冻结当次价格；只有"完成态 +
/// 实际浮币数 == 冻结价 + 同一 payer"才允许消费一次。价格配置在交易开始后再改
/// 不影响本次交易；同一 payer 的重复 Completed 回调只会被 AlreadySettled 忽略。
/// </summary>
internal sealed class CoinCourierShopPayment
{
    private long _payer;
    private int _price;
    private bool _armed;
    private bool _settled;

    /// <summary>本次交易的冻结价（未武装为 0）；绝不回读当前配置。</summary>
    internal int Price => _price;

    internal bool IsArmed(long payer) => _armed && payer != 0 && payer == _payer;

    internal bool AlreadySettled(long payer) => _settled && payer != 0 && payer == _payer;

    internal void Arm(long payer, int price)
    {
        _payer = payer;
        _price = price;
        _armed = payer != 0 && price > 0;
        _settled = false;
    }

    internal bool Consume(long payer, bool completed, int coins)
    {
        if (!_armed || payer == 0 || payer != _payer || !completed || coins != _price) return false;
        _armed = false;
        _settled = true;
        return true;
    }

    internal void Clear()
    {
        _armed = false;
        _settled = false;
        _payer = 0;
        _price = 0;
    }
}

/// <summary>一次 Completed 回调的裁决结果。</summary>
internal enum CoinCourierSettleResult
{
    /// <summary>不是本次交易的完成态（重复回调/未武装/金额不符）：什么都不做。</summary>
    Ignored = 0,
    /// <summary>记录明确报告"未写入"：按原生路径退款一次，角色不生成。</summary>
    Refunded = 1,
    /// <summary>记录成功：已拥有；视觉失败也绝不重复收费或退款。</summary>
    Owned = 2,
    /// <summary>记录抛异常/结果不可判定：锁住本付款上下文，有界诊断，不退款不重购。</summary>
    LatchedUnknown = 3,
}

/// <summary>
/// 招募结算流（纯逻辑，供测试直接驱动）：消费一次原生 Completed 付款凭据，然后
/// 至多调用一次 campaign 记录。true → Owned；false → Refunded；抛异常 → LatchedUnknown。
/// </summary>
internal static class CoinCourierRecruitmentFlow
{
    internal const string UnknownLockReason = "recruit-record-unknown";

    internal static CoinCourierSettleResult Settle(CoinCourierShopPayment payment, long payer,
        bool completed, int coins, Func<bool> tryRecord, Action<string> log)
    {
        if (payment == null) return CoinCourierSettleResult.Ignored;
        if (payment.AlreadySettled(payer)) return CoinCourierSettleResult.Ignored;
        if (!payment.Consume(payer, completed, coins)) return CoinCourierSettleResult.Ignored;
        bool recorded;
        try
        {
            recorded = tryRecord != null && tryRecord();
        }
        catch (Exception e)
        {
            log?.Invoke("record threw: " + e.GetType().Name);
            return CoinCourierSettleResult.LatchedUnknown;
        }
        if (recorded) return CoinCourierSettleResult.Owned;
        log?.Invoke("record reported no change");
        return CoinCourierSettleResult.Refunded;
    }
}

#if !COURIER_RUNTIME_CORE_ONLY
/// <summary>
/// 金币哥布林的招募点：城堡/营火左侧、真实 <see cref="PayableComponent"/> +
/// 独立注入的 <see cref="IPayableComponentOwner"/>，原生持有选择、投币动画、
/// 取消与扣款；本类只提供资格判定与唯一的购买回调。它不是 F5 免费按钮，
/// 也不占银行助手/火铳铺的池 ID 或净标识；存档 owner 未绑定/未就绪时不开放。
/// </summary>
internal static class CoinCourierShop
{
    private const float RetireDelaySeconds = 0.75f;

    private static GameObject _object;
    private static PayableComponent _payable;
    private static CoinCourierShopOwner _owner;
    private static Kingdom _kingdom;
    private static Transform _layer;
    private static NetworkPostbox _postbox;
    private static CRPCHeader _header;
    private static CoinCourierView _marker;
    private static Il2CppSystem.Action<Player> _started;
    private static readonly CoinCourierShopPayment Payment = new();
    private static readonly CoinCourierCleanupGuard Cleanup = new();
    private static float _retryAt;
    private static float _retireAt;
    private static bool _clearing;
    private static bool _retiring;
    private static bool _registered;
    private static bool _loggedFailure;
    private static bool _paymentResolved;
    private static string _createStage = "idle";
    private static float _anchorX;
    private static float _anchorY;
    private static string _status = "尚未创建";   // 唯一创建历史：只由真实 Create 结果更新
    private static IntPtr _attemptKingdom;   // 创建历史的来源：最近一次尝试的 kingdom.Pointer
    private static IntPtr _attemptLayer;     // 与 _attemptKingdom 成对的 world GameLayer.Pointer
    private static ICoinCourierCampaignState _attemptState;   // 来源身份：CampaignState 引用，不缓存 Ready

    /// <summary>
    /// 面板投影（只读、不回写）：正文直接用 Runtime 的唯一详细投影（暂停/保存/联机/身份/unknown
    /// 即时呈现，不在此重算第二套门）；没有店时再附创建历史——只有三个来源（CampaignState 引用 +
    /// kingdom + layer）全部非空且精确匹配、且当前身份已确认时才给出真实结果；任何来源缺失
    /// （从未尝试、已清理、来源读取失败）只给中性"尚未创建"，绝不把无来源的旧失败 raw 外溢到
    /// 其它 campaign/world。异常时只回答"当前不可读"，绝不回退旧历史。
    /// </summary>
    internal static string StatusText
    {
        get
        {
            try
            {
                string current = CoinCourierRuntime.StatusText;
                if (_object != null) return current;
                return CanShowCreationHistory()
                    ? current + " · 上次创建：" + _status
                    : current + " · 上次创建：尚未创建";
            }
            catch (Exception)
            {
                return "状态不可用";
            }
        }
    }

    /// <summary>
    /// 创建历史必须来源完整且精确匹配：相同 CampaignState 引用（不缓存 Ready）、kingdom 与 layer
    /// 两个 pointer 都非零且与当前世界一致、当前身份已确认（复用 Runtime 唯一 Availability 投影）。
    /// 任一来源缺失即不展示真实结果，由调用方回落中性态。
    /// </summary>
    private static bool CanShowCreationHistory()
    {
        ICoinCourierCampaignState state = CoinCourierRuntime.State;
        if (state == null || _attemptState == null || !ReferenceEquals(_attemptState, state)) return false;
        if (_attemptKingdom == IntPtr.Zero || _attemptLayer == IntPtr.Zero) return false;
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        Transform layer = managers != null && managers.world != null ? managers.world.gameLayer : null;
        if (kingdom == null || layer == null) return false;
        if (kingdom.Pointer != _attemptKingdom || layer.Pointer != _attemptLayer) return false;
        return CoinCourierRuntime.IdentityConfirmed;
    }

    internal static void Tick()
    {
        try
        {
            // 清理未完成：保持责任、挡住新建第二家/新付款，按既有维护节奏重试；成功才释放。
            if (Cleanup.Pending)
            {
                HideMarker();
                if (!Cleanup.Due(Time.unscaledTime)) return;
                Clear(Cleanup.Reason);
                return;
            }
            if (_retiring)
            {
                HideMarker();
                if (Time.unscaledTime >= _retireAt)
                {
                    if (Pending(_kingdom != null ? _kingdom.playerOne : null)
                        || Pending(_kingdom != null ? _kingdom.playerTwo : null))
                        _retireAt = Time.unscaledTime + 0.5f;   // 等原生收尾，绝不在事务中途销毁
                    else
                        Clear("recruited");
                }
                return;
            }
            var probe = Observe(out var kingdom, out var layer);
            switch (HeroShopRetention.Decide(in probe))
            {
                case HeroShopRetention.Outcome.ClearFeatureDisabled:
                    if (probe.HasShop) Clear("feature-disabled");
                    return;
                case HeroShopRetention.Outcome.ClearNetworkLost:
                    if (probe.HasShop) Clear("network-lost");
                    return;
                case HeroShopRetention.Outcome.ClearWorldChanged:
                    Clear("world-change");
                    return;
                case HeroShopRetention.Outcome.ClearHeaderMismatch:
                    Clear("header-mismatch");
                    return;
                case HeroShopRetention.Outcome.ClearContextLost:
                    Clear("context-lost");
                    return;
                case HeroShopRetention.Outcome.ClearNonPlayable:
                    Clear("left-playing-or-menu");
                    return;
                case HeroShopRetention.Outcome.Active:
                    TickActive();
                    return;
                case HeroShopRetention.Outcome.Keep:
                    // 普通 Menu 暂停：商店上下文保留、新付款禁止；不主动取消进行中的原生事务。
                    // 文案由 StatusText 的 Runtime 投影即时回答，不写创建历史。
                    if (_payable != null) _payable.forceBlockPayment = true;
                    return;
                case HeroShopRetention.Outcome.Create:
                    // 时标 0（原生菜单/面板兜底暂停）、保存中或重试未到时只等：不创建，也不覆盖
                    // 上次实际结果（投影按读取时的上下文呈现）。
                    if (Time.timeScale <= 0f || IslandSaveData.isSavingGame
                        || Time.unscaledTime < _retryAt) return;
                    _retryAt = Time.unscaledTime + 5f;
                    Create(kingdom, layer);
                    return;
                default:
                    // Wait（无店且当前不可创建）：不写创建历史、不创建；当前状态由投影即时回答。
                    return;
            }
        }
        catch (Exception e)
        {
            if (!_loggedFailure)
            {
                Log("unavailable stage=" + _createStage + ": " + e);
                _loggedFailure = true;
            }
            Clear("error");
            // 这次失败属于当前上下文：清场后记录并重新附上来源，面板不得隐藏已观察失败。
            _status = "招募点创建失败（" + _createStage + "）";
            _retryAt = Time.unscaledTime + 10f;
            try { if (TryContext(out Kingdom kingdom, out Transform layer)) MarkAttempt(kingdom, layer); }
            catch (Exception) { }
        }
    }

    private static void TickActive()
    {
        if (CoinCourierRuntime.State == null)
        {
            // 绑定被解除（owner 释放/新战役未接）：立即清场景，绝不动 campaign 的 Owned/钱袋。
            Clear("unbound");
            return;
        }
        bool saving = IslandSaveData.isSavingGame;
        if (saving && (Pending(_kingdom != null ? _kingdom.playerOne : null)
            || Pending(_kingdom != null ? _kingdom.playerTwo : null)))
        {
            // 保存前取消未完成投币，走原生退款路径（浮币原样返还）。
            CancelPendingTransactions();
            Log("save with a pending payment; native cancellation returned the coins");
        }
        // 价格只在没有进行中的原生事务时刷新：交易开始即冻结，改动不影响已开始的交易。
        if (!Pending(_kingdom != null ? _kingdom.playerOne : null)
            && !Pending(_kingdom != null ? _kingdom.playerTwo : null))
            _payable.Price = CoinCourierRuntime.RecruitPrice;
        _payable.forceBlockPayment = !CanPurchase();
        if (!saving && CoinCourierRuntime.RecruitmentOwned)
        {
            // 招募完成：先禁付，等原生事务收尾后再延迟销毁招募点（角色由运行时接管）。
            _payable.forceBlockPayment = true;
            BeginRetire();
        }
        RenderMarker();
    }

    private static void HideMarker()
    {
        if (_marker == null) return;
        CoinCourierVisuals.Render(_marker, new Vector3(_anchorX, _anchorY, 0f), true,
            CoinCourierPose.Idle, 0f, false);
    }

    private static void RenderMarker()
    {
        if (_marker == null || _object == null || !_object.activeInHierarchy) return;
        float phase = Time.time;
        CoinCourierPose pose = phase % 4f < 2f ? CoinCourierPose.Idle : CoinCourierPose.Leisure;
        bool facingRight = _kingdom == null || _anchorX <= _kingdom.campfirePosition;
        float z = _layer != null ? _layer.position.z : 0f;
        CoinCourierVisuals.Render(_marker, new Vector3(_anchorX, _anchorY, z), facingRight, pose, phase, true);
    }

    internal static bool CanPurchase()
    {
        try
        {
            if (_clearing || _object == null || _payable == null || _owner == null) return false;
            if (IslandSaveData.isSavingGame) return false;
            if (!CoinCourierRuntime.CanOpenRecruitment()) return false;
            return HeroShopRetention.CanServe(Observe(out _, out _));
        }
        catch
        {
            return false;
        }
    }

    internal static bool CanPlayerPurchase(Player player)
    {
        try
        {
            // PayableComponent 先施加原生基础 CanPay 限制（王冠/坐骑/石化/滑翔与全局付款拦截）。
            if (!CanPurchase() || player == null || !player.hasLocalAuthority || player.wallet == null
                || player.gameObject == null || !player.gameObject.activeInHierarchy) return false;
            if (_kingdom == null) return false;
            if ((_kingdom.playerOne == null || _kingdom.playerOne.Pointer != player.Pointer)
                && (_kingdom.playerTwo == null || _kingdom.playerTwo.Pointer != player.Pointer)) return false;
            if (player._payState == Player.PayState.Completed)
                return player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer
                    && player._floatingCurrency != null && player._floatingCurrency.Count == Payment.Price
                    && Payment.IsArmed(player.Pointer.ToInt64());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Create(Kingdom kingdom, Transform layer)
    {
        // 当前门（身份/暂停/保存/unknown）由 StatusText 的 Runtime 投影如实回答：这里不记创建结果。
        if (!CoinCourierRuntime.CanOpenRecruitment()) return;
        _createStage = "home";
        if (!CoinCourierGround.TryResolveHome(kingdom, out float x, out float y))
        {
            Wait("home-ground", "等待营火左侧地面", kingdom, layer);
            return;
        }
        _createStage = "register-owner";
        if (NetworkPostbox.Instance == null)
        {
            Wait("postbox-missing", "等待网络注册就绪", kingdom, layer);
            return;
        }
        if (!_registered)
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CoinCourierShopOwner)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(CoinCourierShopOwner),
                    new RegisterTypeOptions { Interfaces = new[] { typeof(IPayableComponentOwner) } });
            _registered = true;
        }
        _createStage = "create-object";
        _object = new GameObject("KEM_CoinCourierShop");
        _object.SetActive(false);
        _object.transform.SetParent(layer, false);
        _object.transform.position = new Vector3(x, y, layer.position.z);
        _object.AddComponent<CRPCStamp>();
        _owner = _object.AddComponent<CoinCourierShopOwner>();
        _owner.Initialize();
        _createStage = "create-payable";
        _payable = _object.AddComponent<PayableComponent>();
        _payable.forceBlockPayment = true;
        _payable.Init(_owner.Cast<IPayableComponentOwner>());
        _payable.Price = CoinCourierRuntime.RecruitPrice;
        _payable.Currency = CurrencyType.Coins;
        _payable.priceIncrease = 0;
        _payable.indicatorSpacing = 0.4f;
        _payable.indicatorOffset = Vector2.zero;
        _payable.repositionInSplitScreen = false;
        _payable.playerPayPointOffset = Vector2.zero;
        _payable.playerPayDistance = 1f;
        _payable.payablePlacementExclusionOffset = Vector2.zero;
        _payable.payablePlacementExclusionDistance = 0f;   // NPC 不为其它建筑/生成布局提供预留距离
        _payable.glowOnSelect = false;
        _payable.needsNewNetId = false;
        _started = (Il2CppSystem.Action<Player>)OnTransactionStarted;
        _payable.add_OnTransactionStartedCallback(_started);
        _createStage = "owner-callback-preflight";
        var ownerInterface = _owner.Cast<IPayableComponentOwner>();
        if (ownerInterface.OnPay == null) throw new InvalidOperationException("owner callback missing");
        _createStage = "owner-canpay-preflight";
        if (ownerInterface.CanPay(null)) throw new InvalidOperationException("owner eligibility preflight failed");
        _createStage = "owner-lock-preflight";
        LockIndicator.LockReason lockReason = LockIndicator.LockReason.Invalid;
        if (ownerInterface.IsLocked(null, out lockReason) || lockReason != LockIndicator.LockReason.NotLocked)
            throw new InvalidOperationException("owner lock-reason roundtrip failed");
        _createStage = "register-header";
        _postbox = NetworkPostbox.Instance;
        _header = _postbox.RegisterObject(_object, CRPCType.SemiStatic);
        if (!HeaderMatches()) throw new InvalidOperationException("native payment header registration failed");
        _createStage = "activate";
        _object.SetActive(true);
        _kingdom = kingdom;
        _layer = layer;
        _anchorX = x;
        _anchorY = y;
        _marker = CoinCourierVisuals.Create(_object.transform, CoinCourierRuntime.ResolveVisualReference());
        _createStage = "ready";
        MarkAttempt(kingdom, layer);
        _status = "已创建";
        Log("ready x=" + x.ToString("F2") + " y=" + y.ToString("F2")
            + " price=" + CoinCourierRuntime.RecruitPrice);
    }

    /// <summary>创建历史的唯一来源标记：CampaignState 引用（身份，不缓存 Ready）+ kingdom/layer。</summary>
    private static void MarkAttempt(Kingdom kingdom, Transform layer)
    {
        _attemptKingdom = kingdom != null ? kingdom.Pointer : IntPtr.Zero;
        _attemptLayer = layer != null ? layer.Pointer : IntPtr.Zero;
        _attemptState = CoinCourierRuntime.State;
    }

    /// <summary>
    /// 记录一次真实创建结果（历史的唯一写入点）：同来源 + 同结果时不重复日志；结果或来源变化时
    /// 落一条 stage/reason + 固定锚现场值的有界日志（无独立日志状态字段）。
    /// </summary>
    private static void Wait(string code, string text, Kingdom kingdom, Transform layer)
    {
        bool repeat = _status == text
            && _attemptKingdom == (kingdom != null ? kingdom.Pointer : IntPtr.Zero)
            && _attemptLayer == (layer != null ? layer.Pointer : IntPtr.Zero)
            && ReferenceEquals(_attemptState, CoinCourierRuntime.State);
        MarkAttempt(kingdom, layer);
        _status = text;
        if (repeat) return;
        Log("create wait stage=" + _createStage + " reason=" + code + " home=" + HomeText(kingdom));
    }

    /// <summary>固定锚现场值（只读，日志用）：营火 x 与 campfire+HomeOffsetX。</summary>
    private static string HomeText(Kingdom kingdom)
    {
        if (kingdom == null || !float.IsFinite(kingdom.campfirePosition)) return "?";
        float campfire = kingdom.campfirePosition;
        float home = campfire + CoinCourierTiming.HomeOffsetX;
        return "campfire=" + campfire.ToString("F2", CultureInfo.InvariantCulture)
            + " home=" + home.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static bool HeaderMatches()
    {
        if (_postbox == null || NetworkPostbox.Instance == null || _postbox.Pointer != NetworkPostbox.Instance.Pointer
            || _header == null || _object == null || _payable == null
            || _payable.parentHeaderRef == null || _payable.parentHeaderRef.Pointer != _header.Pointer
            || _header.referencedGO == null || _header.referencedGO.Pointer != _object.Pointer
            || _header.NetID == 0 || _header.RemoteMethodList == null || _header.RemoteMethodList.Count < 3) return false;
        return _postbox.MasterSemiCRPCHLookup.TryGetValue(_object, out var found)
            && found != null && found.Pointer == _header.Pointer
            && _postbox.SemiStaticObjects.TryGetValue(_header.NetID, out var indexed)
            && indexed != null && indexed.Pointer == _header.Pointer;
    }

    private static void OnTransactionStarted(Player player)
    {
        if (player == null || !CanPurchase() || _payable == null) return;
        if (player.selectedPayable == null || player.selectedPayable.Pointer != _payable.Pointer) return;
        _paymentResolved = false;   // 新一次交易：取消退款许可重新打开
        Payment.Arm(player.Pointer.ToInt64(), _payable.Price);
    }

    internal static void OnPay(Player player)
    {
        if (player == null || _payable == null || player._completingPayable == null
            || player._completingPayable.Pointer != _payable.Pointer) return;
        long payer = player.Pointer.ToInt64();
        int coins = player._floatingCurrency == null ? 0 : player._floatingCurrency.Count;
        int price = Payment.Price;
        CoinCourierSettleResult result = CoinCourierRecruitmentFlow.Settle(Payment, payer,
            player._payState == Player.PayState.Completed, coins, TryRecordRecruitment, Log);
        switch (result)
        {
            case CoinCourierSettleResult.Ignored:
                return;   // 重复 Completed/未武装/金额不符：什么都不做，不重复记录
            case CoinCourierSettleResult.Owned:
                _paymentResolved = true;
                _payable.forceBlockPayment = true;
                BeginRetire();
                Log("recruitment completed price=" + price);
                return;
            case CoinCourierSettleResult.Refunded:
                // 记录明确未写入：只按原生路径退一次钱（调用方随后消费浮币，净额归零），不生成角色。
                _paymentResolved = true;
                if (player.wallet != null) player.wallet.AddCurrency(CurrencyType.Coins, price);
                Payment.Clear();
                Log("recruitment record refused; refunded " + price);
                return;
            case CoinCourierSettleResult.LatchedUnknown:
                _paymentResolved = true;
                CoinCourierRuntime.LatchRecruitment(CoinCourierRecruitmentFlow.UnknownLockReason);
                _payable.forceBlockPayment = true;
                Log("record outcome unknown; payment context locked");
                return;
        }
    }

    private static bool TryRecordRecruitment()
    {
        ICoinCourierCampaignState state = CoinCourierRuntime.State;
        if (!CoinCourierCampaignAccess.TryRecordRecruitment(state, out bool recorded))
            throw new InvalidOperationException("campaign record unavailable");
        return recorded;
    }

    private static void BeginRetire()
    {
        _retiring = true;
        _retireAt = Time.unscaledTime + RetireDelaySeconds;
    }

    internal static void Clear(string reason)
    {
        if (_clearing) return;
        if (!Cleanup.Pending) Cleanup.Begin(reason, Time.unscaledTime);
        _clearing = true;
        bool hadState = _object != null || _payable != null || _owner != null || _kingdom != null;
        try
        {
            if (_payable != null)
            {
                _payable.forceBlockPayment = true;
                // 只取消"进行中且未结算"的交易；已付/未知/已完成的绝不在清理里再次退款。
                CancelPendingTransactions();
                if (_started != null) _payable.remove_OnTransactionStartedCallback(_started);
            }
            if (_postbox != null && _header != null && _object != null
                && _postbox.MasterSemiCRPCHLookup.TryGetValue(_object, out var found)
                && found != null && found.Pointer == _header.Pointer)
                _postbox.DeregisterObject(_object, _header.NetID, CRPCType.SemiStatic);
            if (_marker != null) CoinCourierVisuals.Destroy(_marker);
            if (_object != null)
            {
                _object.SetActive(false);
                UnityEngine.Object.Destroy(_object);
            }
            // 全部步骤成功：释放清理责任与全部引用（此后才允许新建第二家）。
            Cleanup.Complete();
            Payment.Clear();
            _marker = null;
            _object = null;
            _payable = null;
            _owner = null;
            _kingdom = null;
            _layer = null;
            _postbox = null;
            _header = null;
            _started = null;
            _retiring = false;
            _retireAt = 0f;
            _paymentResolved = false;
            // 清理完成即清空创建历史来源与结果：新上下文不展示旧原因，零来源时历史为"尚未创建"。
            _attemptKingdom = IntPtr.Zero;
            _attemptLayer = IntPtr.Zero;
            _attemptState = null;
            _status = "尚未创建";
            if (hadState) Log("clear reason=" + reason);
        }
        catch (Exception e)
        {
            // 保留未完成的清理步骤引用与退役状态：不新建、不重复收费；按维护节奏稍后重试。
            Cleanup.Defer(Time.unscaledTime, 1f);
            _status = "清理未完成，稍后重试";
            Log("cleanup deferred reason=" + reason + ": " + e.GetType().Name);
        }
        finally
        {
            _clearing = false;
        }
    }

    /// <summary>owner 的 OnDisable：只允许当前同一实例清理，旧 owner 绝不碰后来新建的店。</summary>
    internal static void OnOwnerDisabled(CoinCourierShopOwner owner)
    {
        if (!CoinCourierOwnerPolicy.MayClearCurrent(_owner, owner)) return;
        Clear("owner-disabled");
    }

    /// <summary>保存/世界替换前取消未完成投币：只走原生取消路径（浮币原样返还）。</summary>
    internal static void CancelPendingTransactions()
    {
        if (_payable != null && _kingdom != null)
        {
            Cancel(_kingdom.playerOne);
            Cancel(_kingdom.playerTwo);
        }
        Payment.Clear();
    }

    private static void Cancel(Player player)
    {
        if (player == null || !player.hasLocalAuthority || _payable == null) return;
        bool selected = player.selectedPayable != null && player.selectedPayable.Pointer == _payable.Pointer;
        bool completing = player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer;
        bool pending = selected || completing;
        bool settled;
        bool completedState;
        try
        {
            settled = _paymentResolved || Payment.AlreadySettled(player.Pointer.ToInt64());
            completedState = player._payState == Player.PayState.Completed;
        }
        catch (Exception)
        {
            return;   // 读不到交易状态：绝不在清理路径猜测退款
        }
        if (!CoinCourierCancelPolicy.MayCancel(pending, settled, completedState,
                CoinCourierRuntime.RecruitmentLocked)) return;
        player.CancelTransaction();
        player.DropFloatingCurrency();   // 原生取消退款；列表由它自己清空
        if (completing) player._completingPayable = null;
        if (selected) player.DeselectPayable();
    }

    private static bool Pending(Player player)
    {
        if (player == null || !player.hasLocalAuthority || _payable == null) return false;
        return (player.selectedPayable != null && player.selectedPayable.Pointer == _payable.Pointer)
            || (player._completingPayable != null && player._completingPayable.Pointer == _payable.Pointer);
    }

    private static HeroShopRetention.Probe Observe(out Kingdom kingdom, out Transform layer)
    {
        bool hasShop = _object != null || _kingdom != null;
        bool contextRead = TryContext(out kingdom, out layer);
        bool sameWorld = contextRead && hasShop && _object != null && _kingdom != null && _layer != null
            && kingdom.Pointer == _kingdom.Pointer && layer.Pointer == _layer.Pointer;
        bool playing = contextRead && Managers.Inst.game.state == Game.State.Playing;
        bool menu = contextRead && Managers.Inst.game.state == Game.State.Menu;
        bool sceneAlive = _object != null && _object.activeInHierarchy
            && _layer != null && _layer.gameObject.activeInHierarchy;
        return new HeroShopRetention.Probe
        {
            FeatureEnabled = CoinCourierRuntime.FeatureEnabled,
            Offline = !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth,
            HasShop = hasShop,
            SameScene = contextRead,
            Playing = playing,
            Menu = menu,
            SameWorld = sameWorld,
            HeaderOk = contextRead && sameWorld && HeaderMatches(),
            SceneAlive = sceneAlive,
        };
    }

    private static bool TryContext(out Kingdom kingdom, out Transform layer)
    {
        kingdom = null;
        layer = null;
        if (!CoinCourierRuntime.FeatureEnabled || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) return false;
        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.world == null || managers.payables == null
            || managers.kingdom == null || !managers.kingdom.HasBorderLoaded || managers.world.gameLayer == null) return false;
        kingdom = managers.kingdom;
        layer = managers.world.gameLayer;
        return true;
    }

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[CoinCourierShop] " + message); } catch { }
    }
}

/// <summary>
/// 独立注入的付款 owner（与英雄驿站/火铳铺同款但独立命名与实例）：
/// 不占助手池 ID、不复用硬编码价格凭据；锁定原因输出走 IntPtr 桥，
/// 规避本机 ClassInjector 对 out enum 的非法 Invoker。
/// </summary>
internal sealed class CoinCourierShopOwner : MonoBehaviour
{
    private Il2CppSystem.Action<Player> _onPay;

    public CoinCourierShopOwner(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void Initialize() { _onPay = (Il2CppSystem.Action<Player>)CoinCourierShop.OnPay; }

    public Il2CppSystem.Action<Player> OnPay => _onPay;

    public bool CanPay(Player player) => CoinCourierShop.CanPlayerPurchase(player);

    public bool IsLocked(Player player, IntPtr reason)
        => HeroShopOwnerInterop.WriteUnlocked(reason, (int)LockIndicator.LockReason.NotLocked);

    public void OnDisable() { CoinCourierShop.OnOwnerDisabled(this); }
}
#endif
