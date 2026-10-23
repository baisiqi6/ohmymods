// 单文件到达/回程 adapter：十币进入（PalaceEntryPayment 的 TryStart）与免费回程（PalaceReturnGate）
// 复用同一条已实测 native 短链（源 TriggerSave → 72 → carry Boats → selection → global-only SaveAsync
// → Reload → 新场景 Playing）。单次 visit 制；不付款、不退款、不自动出发、失败不自动回岛。
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using Coatsink.Common;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 进入/回程 wrapper（<see cref="CanStart"/>/<see cref="TryStart"/> 保留十币接口实现；
/// <see cref="CanReturn"/>/<see cref="TryReturn"/> 为免费回程，不经 Payment、不扣/退金币）：
///
/// 激活（每个方向独立 driver，且都要求两个私有 root 精确匹配 + <see cref="PalaceLand12Runtime.Enabled"/>）：
/// - 进入：<c>PALACE_ENTRY_PAYMENT_PRIVATE=1</c>；- 回程：<c>PALACE_RETURN_PRIVATE=1</c>。
/// 未激活时 InstallOnce 不挂 hook、所有入口零副作用。
///
/// 共用 native 时序（当前 2.4 实测）：
/// 1) 唯一一次源 TriggerSave；SaveAsync prefix 只在 source-save 阶段包一次原 callback
///    （每次实际调用 exact result 转发；异常记录并 rethrow），回调精确 72、回调执行完、非保存中、
///    源身份一致 → 唯一一次 <c>PopulateCarryForward(SailAwayGroup.Boats)</c>（也用于回程，非 mount writer）。
/// 2) 选择目标前写单键行程记录（KEM.Palace.Trip.v1；进入 PalaceSelected/回程 ReturnSelected）并 readback
///    校验，然后 <c>campaign.CurrentLand = v.TargetLand</c>（进入 12 / 回程 record.SourceLand）并读回。
/// 3) 一次性 <c>global.SaveAsync</c>（不用 TriggerSave 代 GlobalOnly）；单回调 72 后下一帧唯一 Reload。
/// 4) Reload 后只认当前 Managers.game 新对象，须离开源 Playing；新场景 Playing 且 campaign/game 都是
///    <c>v.TargetLand</c> 才算到达观测。钱包/坐骑只读日志。
///
/// visit 生命周期：claim 在 TriggerSave 前；每次成功 Arrived 才允许下一次 claim（进入/回程各自与
/// 十币/Payment 无关）；Failed 或 unknown 一律 latch 拒绝后续 claim，绝不自动重置/重做；旧 Visit 强引用
/// 保留到本私有 run 结束。TryXxx 返回 true 只表示受理，不表示到达。
/// 不写 PreviousLand、不写钱包、不 Ride/SetSteedStatus；不声称付款/到达/玩家验收。
/// </summary>
internal sealed class PalaceIslandTravel : IPalaceIslandTravel
{
    private const string EnterEnv = "PALACE_ENTRY_PAYMENT_PRIVATE";
    private const string ReturnEnv = "PALACE_RETURN_PRIVATE";
    private const int Price = 10;
    private const int PalaceLand12 = PalaceLand12Runtime.LandIndex;
    private const float SourceSaveSeconds = 30f;
    private const float GlobalSaveSeconds = 30f;
    private const float ReloadSeconds = 120f;
    private const int SaveSuccess = (int)(SaveLoadResult.Save | SaveLoadResult.Success);

    private enum Phase { SourceSave, SourceDurable, TargetSelected, ReloadIssued, Arrived, Failed }

    /// <summary>唯一一次旅程的全部身份快照；SaveAsync wrapper/回调与 Update postfix 共享。</summary>
    private sealed class Visit
    {
        internal Phase Stage = Phase.SourceSave;
        internal bool Returning;
        internal PalacePaidHandle Paid;
        internal PalaceLayoutRecipe DestinationLayout;
        internal int RecordVersion;
        internal PalaceTripRecord SourceRecord, WrittenRecord;
        internal PalaceSourceClockReceipt ClockReceipt;
        internal bool DepartureClaimed;
        internal bool UsedDurableResume;
        internal PalaceEntryAnchorData SourceEntryAnchor;
        internal string TripId = "";
        internal string RecordPhase = "";
        internal string RecordEvidence = "";
        internal IntPtr Game, Kingdom, Global, Campaign, Agent, ArrivalGame;
        internal int SourceLand, TargetLand;
        internal string CampaignGuid;
        internal int CampaignSlot;
        internal float Deadline;
        internal string Fault = "";
        internal bool SaveReturned, CarryAttempted, GlobalFinished, SawLoading, RecordWritten;
        internal int SaveCalls, SourceCallbacks, SourceCallbackFinished, SourceRaw;
        internal int GlobalCallbacks, GlobalRaw;
        internal Il2CppSystem.Action<SaveLoadResult> Original, Wrapper, TargetCallback;
        internal System.Action<SaveLoadResult> Forward, TargetForward;
    }

    private static readonly object Gate = new object();
    private static PalaceIslandTravel _instance;
    private static bool _installed, _insideUpdate, _failedLatch;
    private static Visit _visit; // active strong ref
    // Keep completed callback roots for this process; epoch admission is per trip, not a lifetime fee cap.
    private static readonly List<Visit> _completed = new List<Visit>();
    private static IntPtr _resumeGame, _resumePlayer;
    private static ReturnSaveObservation _returnSave;
    private static Visit _commitAfterArrival;
    private static string _returnCheckpointFault = "";
    private static bool _returnTriggerInProgress;
    private static readonly List<ReturnSaveObservation> _returnSaveRoots = new List<ReturnSaveObservation>();
    private static string _status = "未启用：等待 DedicatedR7V1 固定 profile 的存档绑定（正常 PC 与受保护隔离共用）";

    private PalaceIslandTravel() { }

    /// <summary>Root 接线用单例：作为 IPalaceIslandTravel 交给现有十金币控制器。</summary>
    internal static PalaceIslandTravel Instance
    {
        get { if (_instance == null) _instance = new PalaceIslandTravel(); return _instance; }
    }

    /// <summary>Root label 用状态文案：描述当前阶段或失败原因，不预先承诺成功。</summary>
    internal static string Status { get { lock (Gate) { return _status; } } }

    /// <summary>
    /// Root 在 Main 里接线一次。进入/回程任一 driver 开启且 land12 私有模式成立时挂
    /// <c>GlobalSaveData.SaveAsync</c> prefix 与 <c>Game.Update</c> postfix；否则零 hook。
    /// </summary>
    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed) return;
        bool enter = PalaceRuntimeProfile.Capability(EnterEnv);
        bool ret = PalaceRuntimeProfile.Capability(ReturnEnv);
        if (!enter && !ret) return;
        if (!PalaceLand12Runtime.Enabled)
        {
            SetStatus("未启用：land12 profile 未确认（不挂 hook）");
            Log(_status);
            return;
        }
        if (harmony == null) throw new InvalidOperationException("PalaceIslandTravel requires Harmony");
        MethodInfo update = AccessTools.Method(typeof(Game), nameof(Game.Update), Type.EmptyTypes);
        MethodInfo save = AccessTools.Method(typeof(GlobalSaveData), nameof(GlobalSaveData.SaveAsync),
            new[] { typeof(Il2CppSystem.Action<SaveLoadResult>) });
        if (update == null || save == null || save.ReturnType != typeof(void))
            throw new InvalidOperationException("PalaceIslandTravel hook signature unavailable");
        _installed = true;
        harmony.Patch(save, prefix: new HarmonyMethod(typeof(PalaceIslandTravel), nameof(SavePrefix)),
            postfix: new HarmonyMethod(typeof(PalaceIslandTravel), nameof(SavePostfix)) { priority=Priority.Last },
            finalizer: new HarmonyMethod(typeof(PalaceIslandTravel), nameof(SaveFinalizer)) { priority=Priority.Last });
        harmony.Patch(update, postfix: new HarmonyMethod(typeof(PalaceIslandTravel), nameof(UpdatePostfix)));
        SetStatus("待命：进入/回程共用一个 native 短链（单次旅程、不自动出发）");
        Log("armed palace travel adapter; drivers enter=" + enter + " return=" + ret + "; one native short chain");
    }

    // ---- 十币进入 wrapper（IPalaceIslandTravel） ----

    /// <summary>
    /// 只读检查（无写入）：Ready + 离线授权希腊源岛 + 非 land12 + exact Managers + campaign 双实例一致 +
    /// payer 属当前 Kingdom + payable 活于源场景且 Price=10 Coins + 非保存中 + 记录写入所需 campaign 身份。
    /// 刻意不检查钱包余额 ≥10（native 浮币已减）。
    /// </summary>
    public bool CanStart(Game game, Kingdom kingdom, Player payer, PayableComponent payable, out string reason)
    {
        if (!TryDescribeForCharge(game, kingdom, payer, payable, out _, out reason)) return false;
        return !ReturnAnchorEnabled || PalaceReturnAnchor.TryCapture(game, kingdom, payer, payable, out _, out reason);
    }

    public bool TryDescribeForCharge(Game game, Kingdom kingdom, Player payer, PayableComponent payable,
        out PalaceDestinationDecision decision, out string reason)
        => Evaluate(game,kingdom,payer,payable,PalaceLayoutRecipe.Capital400,out decision,out reason);

    /// <summary>控制器在 native settlement 后调用：条件复核 + 同步 claim 一次 visit（先标记）再唯一源 TriggerSave。</summary>
    public bool TryStart(Game game, Kingdom kingdom, Player payer, PayableComponent payable, out string reason)
    { reason = "durable paid handle required"; return false; }

    public bool CanStartPaid(Game game, Kingdom kingdom, Player payer, PayableComponent payable, PalacePaidHandle paid, out string reason)
    {
        reason = "paid source not settled/resumable";
        if (paid!=null && !PalaceLayoutRecipe.IsKnown(paid.DestinationLayout))
        { reason="legacy-destination-layout-unknown"; return false; }
        if (paid == null || paid.HasFault || paid.SourceLand != game.currentLand
            || !(paid.Stage == PalacePaidStage.Resumable || paid.Stage == PalacePaidStage.SourceCaptured
                 || (paid.Stage == PalacePaidStage.PaidObserved && paid.SettlementObserved))) return false;
        return Evaluate(game, kingdom, payer, payable, paid.DestinationLayout, out _, out reason, false);
    }

    public bool TryStartPaid(Game game, Kingdom kingdom, Player payer, PayableComponent payable, PalacePaidHandle paid, out string reason)
    {
        if (!CanStartPaid(game, kingdom, payer, payable, paid, out reason)) return false;
        return Claim(game, kingdom, payer, false, PalaceLand12, paid.TripId.ToString("D"),
            PalaceTripContext.PhasePalaceSelected, "", paid.Anchor, out reason, paid);
    }

    // Existing input gateway consumes a genuine source-resume gesture. No Pay/OnPay/price-zero path.
    internal static bool TryConsumePaidResume(Player payer, bool held, bool down)
    {
        try
        {
            var m = Managers.Inst;
            if (_resumePlayer != IntPtr.Zero)
            {
                if (m != null && m.game != null && payer != null && m.game.Pointer == _resumeGame && payer.Pointer == _resumePlayer)
                { if (held || down) return true; _resumeGame = _resumePlayer = IntPtr.Zero; }
                else _resumeGame = _resumePlayer = IntPtr.Zero;
            }
            if ((!held && !down) || m == null || m.game == null || m.kingdom == null || payer == null
                || m.game.currentLand < 0 || m.game.currentLand > 11 || m.game.state != Game.State.Playing
                || payer._payState != Player.PayState.None || payer._floatingCurrency == null || payer._floatingCurrency.Count != 0
                || !PalaceVisibleEntry.TryGetCurrentEntry(out var owner, out var payable)
                || owner == null || !owner.gameObject.activeInHierarchy
                || payer.gameObject.scene.handle != owner.gameObject.scene.handle
                || Math.Abs(payer.transform.position.x-owner.transform.position.x)>1.7f
                || Math.Abs(payer.transform.position.y-owner.transform.position.y)>1.5f
                || !PalacePaidResponsibility.TryGetPreparedHandle(m.game,m.kingdom,payer,out var paid,out _)
                || (paid.Stage != PalacePaidStage.SourceCaptured && paid.Stage != PalacePaidStage.Resumable)) return false;
            _resumeGame=m.game.Pointer; _resumePlayer=payer.Pointer;
            if (down)
            {
                bool accepted=Instance.TryStartPaid(m.game,m.kingdom,payer,payable,paid,out var why);
                Log("paid continuation gesture accepted="+accepted+"; "+why+"; no native payment invoked");
            }
            return true;
        }
        catch(Exception e) { Log("paid continuation observation failed: "+e.GetType().Name); return _resumePlayer != IntPtr.Zero && payer != null && payer.Pointer==_resumePlayer; }
    }

    // ---- 免费回程 wrapper（PalaceReturnGate 调用；不经 Payment、不扣/退金币） ----

    internal static bool CanReturn(Game game, Kingdom kingdom, Player payer, out string reason)
        => EvaluateReturn(game, kingdom, payer, out reason, out _);

    internal static bool TryReturn(Game game, Kingdom kingdom, Player payer, out string reason)
    {
        reason = "";
        try
        {
            if (!EvaluateReturn(game, kingdom, payer, out reason, out PalaceTripRecord record)) return false;
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = CampaignSaveData.current;
            if (!PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string bindReason))
            { reason = "campaign 身份复核失败：" + bindReason; return false; }
            if (!PalaceTripContext.RecordMatches(campaign, guid, slot, record, out string matchReason))
            { reason = "记录复核失败：" + matchReason; return false; }
            if (PalaceTripContext.Read(global, out PalaceTripRecord fresh, out string detail) != PalaceTripContext.ReadStatus.Ok
                || !SameTrip(fresh, record))
            { reason = "记录 claim 前漂移：" + detail; return false; }
            if (!PalacePaidResponsibility.TryGetPreparedHandle(game,kingdom,payer,out var paid,out reason)
                || paid.Stage != PalacePaidStage.Entered || paid.TripId.ToString("D") != record.TripId) return false;
            if (!Claim(game, kingdom, payer, true, record.SourceLand, record.TripId,
                    PalaceTripContext.PhaseReturnSelected, record.MigrationEvidence ?? "", record.SourceEntryAnchor, out reason, paid)) return false;
            reason = "已受理：回程源岛存档已请求；受理≠到达";
            return true;
        }
        catch (Exception e)
        {
            Visit v = _visit;
            if (v != null && v.Stage == Phase.SourceSave) Fail(v, "TryReturn 异常：" + e.GetType().Name);
            reason = "TryReturn 异常：" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>GUI 只读窥视：有效记录时给出回程目标岛（动态值，不加 1）；不 claim、不副作用。</summary>
    internal static bool TryPeekReturnTarget(Game game, Kingdom kingdom, Player payer, out int sourceLand, out string reason)
    {
        sourceLand = -1;
        if (!EvaluateReturn(game, kingdom, payer, out reason, out PalaceTripRecord record)) return false;
        sourceLand = record.SourceLand;
        return true;
    }

    internal static bool HasActiveJourney { get { lock (Gate) { return _visit != null; } } }
    internal static string CurrentTripId { get { lock (Gate) { return _visit?.TripId ?? ""; } } }
    // Native arrival extension reads the existing driver's exact reload owner; no new claim or writer.
    internal static bool IsEnteringArrivalGame(Game game, string tripId)
    {
        lock (Gate)
        {
            Visit v = _visit;
            Managers current = Managers.Inst;
            if (current == null || current.game == null || game == null || current.game.Pointer != game.Pointer) return false;
            return v != null && !v.Returning && v.Stage == Phase.ReloadIssued && v.Fault.Length == 0
                && v.TargetLand == PalaceLand12 && v.RecordWritten && v.RecordPhase == PalaceTripContext.PhasePalaceSelected
                && !string.IsNullOrEmpty(tripId) && v.TripId == tripId && game != null
                && game.Pointer != v.Game && v.ArrivalGame != IntPtr.Zero && game.Pointer == v.ArrivalGame
                && game.currentLand == PalaceLand12;
        }
    }
    internal static bool TryGetActiveArrivalLayout(Game game,string tripId,out PalaceLayoutRecipe recipe)
    {
        recipe=null;
        lock(Gate)
        {
            Visit v=_visit; Managers m=Managers.Inst;
            if(m==null||m.game==null||game==null||m.game.Pointer!=game.Pointer||v==null
                ||v.Stage!=Phase.ReloadIssued||v.Fault.Length!=0||!v.RecordWritten||v.RecordVersion!=4
                ||tripId!=v.TripId||game.Pointer==v.Game||v.ArrivalGame==IntPtr.Zero||game.Pointer!=v.ArrivalGame
                ||game.currentLand!=v.TargetLand||!VisitDestinationMatches(v)
                ||!VerifyWrittenDestination(v,GlobalSaveData.loaded,out _)) return false;
            recipe=v.DestinationLayout; return true;
        }
    }
    // 0 pending, 1 exact arrived, -1 failed/unknown; never equate new Playing with arrival.
    internal static int PrivateJourneyOutcome(Game game, string tripId, int target, out string reason)
    {
        reason = "";
        lock (Gate)
        {
            if (string.IsNullOrEmpty(tripId)) { reason = "missing trip claim"; return -1; }
            if (_visit != null && _visit.TripId == tripId) { reason = "driver in progress"; return 0; }
            for (int i = _completed.Count - 1; i >= 0; i--)
            {
                var v = _completed[i];
                if (v.TripId != tripId) continue;
                if (v.Stage == Phase.Failed) { reason = "driver failed: " + v.Fault; return -1; }
                if (v.Stage != Phase.Arrived || game == null || game.Pointer != v.ArrivalGame
                    || v.TargetLand != target || game.currentLand != target)
                { reason = "arrival identity/target mismatch"; return -1; }
                reason = "exact driver arrival"; return 1;
            }
            reason = "claim outcome unavailable"; return -1;
        }
    }

    // Private diagnostic only. Shares the exact travel chain; no Pay/OnPay or wallet mutation.
    internal static bool CanEnterForPrivateVerification(Game game, Kingdom kingdom, Player payer, out string reason)
    {
        reason = "";
        if (!PalaceRuntimeProfile.Capability("PALACE_ROUNDTRIP_QA") || !_installed
            || !PalaceLand12Runtime.Enabled || !RootsMatch() || !FilerReady())
        { reason = "private roundtrip QA inactive"; return false; }
        if (!ClaimFree(false, out reason) || !TryScope(game, kingdom, payer, out var global, out var campaign, out reason)) return false;
        if (campaign.CurrentLand == PalaceLand12 || campaign.CurrentLand != game.currentLand || !payer.hasLocalAuthority)
        { reason = "QA source is not the current ordinary island"; return false; }
        if (!PalaceTripContext.TryBinding(global, campaign, out var guid, out var slot, out reason)
            || !OutboundRecordAllowed(global, campaign, guid, slot, out reason)) return false;
        if (!PalaceLand12Runtime.CanEnterPrepared(game, PalaceLayoutRecipe.Capital400, out reason)) return false;
        if (!ReturnAnchorEnabled) return true;
        // This bounded QA only publishes a V2 anchor when the real current entry exists.
        if (!PalaceVisibleEntry.TryGetCurrentEntry(out _, out PayableComponent entry))
        { reason = "QA requires actual source entry, no guessed anchor"; return false; }
        return PalaceReturnAnchor.TryCapture(game, kingdom, payer, entry, out _, out reason);
    }

    internal static bool TryEnterForPrivateVerification(Game game, Kingdom kingdom, Player payer, out string reason)
    {
        if (!CanEnterForPrivateVerification(game, kingdom, payer, out reason)) return false;
        PalaceEntryAnchorData anchor = null;
        if (ReturnAnchorEnabled)
        {
            if (!PalaceVisibleEntry.TryGetCurrentEntry(out _, out PayableComponent entry))
            { reason = "QA source entry disappeared before claim"; return false; }
            if (!PalaceReturnAnchor.TryCapture(game, kingdom, payer, entry, out anchor, out reason)) return false;
        }
        return Claim(game, kingdom, payer, false, PalaceLand12, Guid.NewGuid().ToString("D"),
            PalaceTripContext.PhasePalaceSelected, "", anchor, out reason);
    }

    // ---- 共享 claim/记录/快照 ----

    private static bool Claim(Game game, Kingdom kingdom, Player payer, bool returning, int targetLand,
        string tripId, string recordPhase, string recordEvidence, out string reason)
        => Claim(game, kingdom, payer, returning, targetLand, tripId, recordPhase, recordEvidence, null, out reason);

    /// <summary>
    /// 选择目标前写单键记录：重新完整复核 binding + phase/sourceLand（进入=当前源，回程=valid record.
    /// sourceLand 且目标岛 native 记录可读），write+readback 必须过，否则整条链在 selection 前失败。
    /// </summary>
    private static bool WriteTripRecord(Visit v, GlobalSaveData global, CampaignSaveData campaign, out string detail)
    {
        detail = "";
        try
        {
            int recordSource = v.Returning ? v.TargetLand : v.SourceLand;
            if (v.Returning)
            {
                if (v.SourceLand != PalaceLand12) { detail = "return-source-not-palace:" + v.SourceLand; return false; }
                if (v.TargetLand < 0 || v.TargetLand > 11) { detail = "return-target-range:" + v.TargetLand; return false; }
                if (campaign.CurrentLand != PalaceLand12) { detail = "campaign-not-palace:" + campaign.CurrentLand; return false; }
                if (!PalaceTripContext.TryTargetIsland(campaign, v.TargetLand, out string islandDetail))
                { detail = "return-target-" + islandDetail; return false; }
            }
            else
            {
                if (v.SourceLand == PalaceLand12) { detail = "enter-source-is-palace"; return false; }
                if (campaign.CurrentLand != recordSource) { detail = "source-land:" + campaign.CurrentLand; return false; }
            }
            if (!PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string bindDetail))
            { detail = "binding:" + bindDetail; return false; }
            if (guid != v.CampaignGuid || slot != v.CampaignSlot) { detail = "claimed-campaign-identity-drift"; return false; }
            if (!VisitDestinationMatches(v)) { detail="visit-destination-drift"; return false; }
            if (v.Returning)
            {
                if (PalaceTripContext.Read(global, out var old, out detail) != PalaceTripContext.ReadStatus.Ok
                    || old.TripId != v.TripId || old.SourceLand != v.TargetLand || old.Phase != PalaceTripContext.PhasePalaceSelected
                    || !PalaceTripContext.RecordMatches(campaign, guid, slot, old, out detail)
                    || !SameAnchor(old.SourceEntryAnchor, v.SourceEntryAnchor)
                    || v.SourceRecord==null || !PalaceTripContext.TupleEquals(old,v.SourceRecord)
                    || !v.Paid.MatchesDestination(old))
                { detail = "return-record-drift:" + detail; return false; }
            }
            else if (!OutboundRecordAllowed(global, campaign, guid, slot, out detail)) return false;
            var record = new PalaceTripRecord
            {
                Version = v.RecordVersion,
                DestinationLayoutId = v.DestinationLayout?.LayoutId,
                DestinationLayoutVersion = v.DestinationLayout?.LayoutVersion,
                SourceEntryAnchor = CloneAnchor(v.SourceEntryAnchor),
                SourceClockReceipt = v.ClockReceipt != null ? v.ClockReceipt.WriteCanonicalStrict() : null,
                TripId = v.TripId,
                CampaignGuid = guid,
                CampaignSlot = slot,
                Biome = campaign.biomeIndex,
                Reign = campaign.reign,
                Start = campaign.realStartDateTime,
                SourceLand = recordSource,
                PalaceLand = PalaceLand12,
                Phase = v.RecordPhase,
                MigrationEvidence = v.Returning ? (v.RecordEvidence ?? "") : ""
            };
            if (!RecordDestinationMatches(v,record)) { detail="record-destination-drift"; return false; }
            if (!PalaceTripContext.Write(global, record, out string writeDetail))
            { detail = "write:" + writeDetail; return false; }
            v.WrittenRecord = record;
            v.RecordWritten = true;
            Log("trip record phase=" + record.Phase + " sourceLand=" + record.SourceLand + " trip=" + record.TripId
                + " slot=" + slot + "; " + writeDetail + "; readback verified before selection");
            return true;
        }
        catch (Exception e)
        {
            detail = "write-fault:" + e.GetType().Name;
            return false;
        }
    }

    // ---- 评估 ----

    private static bool Evaluate(Game game, Kingdom kingdom, Player payer, PayableComponent payable, PalaceLayoutRecipe required, out PalaceDestinationDecision decision, out string reason, bool newFee = true)
    {
        decision=null;
        reason = "";
        try
        {
            if (!PalaceLayoutRecipe.IsKnown(required)) { reason="destination-layout-unknown"; return false; }
            if (!_installed) { reason = "adapter 未安装 hook（Root 未完成 InstallOnce）"; return false; }
            if (!ActivationReady(EnterEnv, out reason)) return false;
            if (newFee && (ModConfig.Enabled == null || !ModConfig.Enabled.Value))
            { reason = "Mod 总开关关闭，禁止新收费（未收取金币）"; return false; }
            if (!PalacePlayable.TryRequirePaidEntryInstallation(out reason)) return false;
            if (!PalaceLand12Runtime.Ready) { reason = "land12 生成门/文件表（共享 RequiredFileCapacity）未就绪"; return false; }
            if (!ClaimFree(false, out reason)) return false;
            if (payable == null) { reason = "payable 为空"; return false; }
            if (!TryScope(game, kingdom, payer, out GlobalSaveData global, out CampaignSaveData campaign, out reason)) return false;
            if (campaign.CurrentLand == PalaceLand12 || game.currentLand == PalaceLand12)
            { reason = "已在 land12"; return false; }
            if (campaign.CurrentLand != game.currentLand)
            { reason = "campaign 与 Game 当前岛不一致"; return false; }
            if (campaign.CurrentLand < 0 || campaign.CurrentLand > 11)
            { reason = "当前岛暂不支持天朝往返，未收取金币"; return false; }
            if (!PalaceDestinationAdmission.TryDescribeDestination(campaign, required, out decision, out reason)) return false;
            if (!PalaceDestinationAdmission.TryGetResourcePrecheck(required, out string resourceReason))
            { reason = "profile 资源预检未通过：" + resourceReason; return false; }
            if (payable.gameObject == null || !payable.gameObject.activeInHierarchy || !payable.isActiveAndEnabled)
            { reason = "入口 payable 不可用"; return false; }
            if (payable.gameObject.scene.handle != payer.gameObject.scene.handle)
            { reason = "payable 不在源场景"; return false; }
            if (payable.Price != Price || payable.Currency != CurrencyType.Coins)
            { reason = "价格/货币不匹配"; return false; }
            if (!FilerReady()) { reason = "当前存档绑定未确证（Filer agent/目录不可用或漂移）"; return false; }
            if (!PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string bindReason))
            { reason = "记录所需 campaign 身份不可用：" + bindReason; return false; }
            if (!OutboundRecordAllowed(global, campaign, guid, slot, out reason)) return false;
            if (!PalaceLand12Runtime.CanEnterPrepared(game, required, out reason)) return false;
            reason = "可出发：源 land=" + campaign.CurrentLand + " -> " + PalaceLand12;
            return true;
        }
        catch (Exception e)
        {
            reason = "检查异常：" + e.GetType().Name;
            return false;
        }
    }

    private static bool EvaluateReturn(Game game, Kingdom kingdom, Player payer, out string reason, out PalaceTripRecord record)
    {
        record = null;
        reason = "";
        try
        {
            if (!_installed) { reason = "adapter 未安装 hook（Root 未完成 InstallOnce）"; return false; }
            if (!ActivationReady(ReturnEnv, out reason)) return false;
            if (!PalaceLand12Runtime.Ready) { reason = "land12 生成门/文件表（共享 RequiredFileCapacity）未就绪"; return false; }
            if (!ClaimFree(true, out reason)) return false;
            if (!TryScope(game, kingdom, payer, out GlobalSaveData global, out CampaignSaveData campaign, out reason)) return false;
            if (!payer.hasLocalAuthority) { reason = "非本地玩家"; return false; }
            if (campaign.CurrentLand != PalaceLand12 || game.currentLand != PalaceLand12)
            { reason = "当前不在 land12（campaign=" + campaign.CurrentLand + " game=" + game.currentLand + "）"; return false; }
            if (!PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string bindReason))
            { reason = "campaign 身份不可用：" + bindReason; return false; }
            PalaceTripContext.ReadStatus status = PalaceTripContext.Read(global, out PalaceTripRecord loaded, out string detail);
            if (status != PalaceTripContext.ReadStatus.Ok)
            { reason = "行程记录不可用（" + status + "：" + detail + "）"; return false; }
            if (loaded.Phase != PalaceTripContext.PhasePalaceSelected)
            { reason = "记录相位不允许回程：" + loaded.Phase; return false; }
            if (!PalaceTripContext.RecordMatches(campaign, guid, slot, loaded, out string matchReason))
            { reason = "记录与当前档不一致：" + matchReason; return false; }
            if (!PalaceTripContext.TryTargetIsland(campaign, loaded.SourceLand, out string islandDetail))
            { reason = "目标岛记录不可读：" + islandDetail; return false; }
            if ((loaded.Version!=3 && loaded.Version!=4) || !PalacePaidResponsibility.TryGetPreparedHandle(game,kingdom,payer,out var paid,out reason)
                || paid.Stage != PalacePaidStage.Entered || paid.TripId.ToString("D") != loaded.TripId
                || !paid.MatchesDestination(loaded)) return false;
            record = loaded;
            reason = "可回程：land12 -> " + loaded.SourceLand;
            return true;
        }
        catch (Exception e)
        {
            reason = "回程检查异常：" + e.GetType().Name;
            return false;
        }
    }

    private static bool ClaimFree(bool returning, out string reason)
    {
        reason = "";
        lock (Gate)
        {
            if (_visit != null) { reason = "已有旅程在途"; return false; }
            if (_failedLatch || _returnCheckpointFault.Length != 0)
            { reason = "前一次旅程/回程保存失败或未知，本次运行不再受理"; return false; }
        }
        return true;
    }

    /// <summary>两个方向共用的源 scope：离线授权、exact Managers Game/Kingdom、Playing、非保存中、
    /// campaign 双实例一致、希腊源、payer 为当前 Kingdom 实际玩家且本地可用。</summary>
    private static bool TryScope(Game game, Kingdom kingdom, Player payer,
        out GlobalSaveData global, out CampaignSaveData campaign, out string reason)
    {
        global = null;
        campaign = null;
        reason = "";
        try
        {
            if (game == null || kingdom == null || payer == null) { reason = "入参为空"; return false; }
            if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) { reason = "非离线授权世界"; return false; }
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.kingdom == null) { reason = "Managers 未就绪"; return false; }
            if (m.game.Pointer != game.Pointer || m.kingdom.Pointer != kingdom.Pointer)
            { reason = "Game/Kingdom 非当前实例"; return false; }
            if (game.state != Game.State.Playing) { reason = "Game 不在 Playing"; return false; }
            if (IslandSaveData.isSavingGame) { reason = "岛屿保存进行中"; return false; }
            global = GlobalSaveData.loaded;
            campaign = global != null ? global.GetCurrentCampaign() : null;
            CampaignSaveData current = CampaignSaveData.current;
            if (global == null || global.InChallenge || campaign == null || current == null
                || campaign.Pointer == IntPtr.Zero || campaign.Pointer != current.Pointer)
            { reason = "当前 campaign 未确证"; return false; }
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex)
            { reason = "非希腊源（未验证范围）"; return false; }
            if (!kingdom.HasBorderLoaded || kingdom.playerOne == null || kingdom.playerOne.wallet == null)
            { reason = "源 Kingdom 未就绪"; return false; }
            if (!IsKingdomPlayer(kingdom, payer)) { reason = "付款人不属于当前 Kingdom"; return false; }
            if (payer.gameObject == null || !payer.gameObject.activeInHierarchy) { reason = "玩家不可用"; return false; }
            if (payer.wallet == null) { reason = "玩家钱包不可用"; return false; }
            return true;
        }
        catch (Exception e)
        {
            reason = "scope 异常：" + e.GetType().Name;
            return false;
        }
    }

    private static bool ActivationReady(string env, out string reason)
    {
        reason = "";
        if (!PalaceRuntimeProfile.Capability(env)) { reason = "未启用：" + env + "（DedicatedR7V1 固定 profile）"; return false; }
        if (!PalaceLand12Runtime.Enabled) { reason = "未启用：land12 profile 未确认"; return false; }
        if (!PalaceSaveBindingSource.CurrentMatches(out string bindReason))
        { reason = "存档绑定不可用或漂移：" + bindReason; return false; }
        return true;
    }

    // ---- SaveAsync 观察 + Update 推进 ----

    /// <summary>只在自己 source-save 阶段包一次原 callback；其它阶段/foreign 保存原样透传。</summary>
    private static void SavePrefix(GlobalSaveData __instance, ref Il2CppSystem.Action<SaveLoadResult> __0,
        out ReturnSaveObservation __state)
    {
        __state = null;
        try
        {
            Visit v = _visit;
            if (v == null || v.Stage != Phase.SourceSave)
            { __state = ObserveReturnedSavePrefix(__instance, ref __0); return; }
            IntPtr pointer = ReferenceEquals(__instance, null) ? IntPtr.Zero : __instance.Pointer;
            lock (Gate)
            {
                if (v.Stage != Phase.SourceSave) return;
                v.SaveCalls++;
                if (v.SaveCalls != 1 || pointer != v.Global || __0 == null)
                {
                    v.Fault = "source-save interleaved/owner mismatch/callback missing";
                    Log("source save observer rejected: calls=" + v.SaveCalls + "; ownerMatch=" + (pointer == v.Global));
                    return;
                }
                v.Original = __0;
                Il2CppSystem.Action<SaveLoadResult> original = v.Original;
                v.Forward = result => ForwardSourceCallback(v, original, result);
                v.Wrapper = (Il2CppSystem.Action<SaveLoadResult>)v.Forward;
                __0 = v.Wrapper;
            }
        }
        catch (Exception e)
        {
            Visit v = _visit;
            if (v != null) lock (Gate) { if (v.Fault.Length == 0) v.Fault = "save-observer-fault:" + e.GetType().Name; }
            Log("source save observer fault: " + e.GetType().Name);
        }
    }

    /// <summary>每次实际调用都转发 exact result 给原 callback；原 callback 异常记录后照常 rethrow。</summary>
    private static void ForwardSourceCallback(Visit v, Il2CppSystem.Action<SaveLoadResult> original, SaveLoadResult result)
    {
        int sequence;
        lock (Gate)
        {
            sequence = ++v.SourceCallbacks;
            v.SourceRaw = (int)result;
            if (sequence != 1) v.Fault = "duplicate-save-callback";
        }
        try { original.Invoke(result); }
        catch (Exception e)
        {
            lock (Gate) { if (v.Fault.Length == 0) v.Fault = "original-callback-threw:" + e.GetType().Name; }
            Log("original source callback threw; forwarded then rethrown: " + e.GetType().Name);
            throw;
        }
        finally
        {
            lock (Gate) { v.SourceCallbackFinished++; }
            Log("source save callback raw=" + (int)result + "; sequence=" + sequence + "; forwarded; not disk proof");
        }
    }

    /// <summary>手动推进：只认当前 Managers.game；源阶段拒绝非源 Game，Reload 后才允许新 Game 交接。</summary>
    private static void UpdatePostfix(Game __instance)
    {
        lock (Gate) { if (_insideUpdate) return; _insideUpdate = true; }
        try
        {
            AdvanceReturnedSave(__instance);
            Visit v = _visit;
            if (v == null) return;
            if (v.Stage == Phase.Arrived || v.Stage == Phase.Failed) return;
            if (v.Fault.Length != 0) { Fail(v, v.Fault); return; }
            if (Time.realtimeSinceStartup > v.Deadline) { Fail(v, "超时：" + PhaseName(v.Stage) + " 内未完成"); return; }
            switch (v.Stage)
            {
                case Phase.SourceSave: AdvanceSourceSave(v, __instance); break;
                case Phase.SourceDurable: AdvanceSourceDurable(v, __instance); break;
                case Phase.TargetSelected: AdvanceGlobalSave(v, __instance); break;
                case Phase.ReloadIssued: AdvanceReload(v, __instance); break;
            }
        }
        catch (Exception e) { Fail(_visit, "adapter exception; no retry: " + e); }
        finally { lock (Gate) { _insideUpdate = false; } }
    }

    /// <summary>源阶段：拒绝非源 Game；save 终态精确 72 → carry → 记录写入 → selection → 一次性 global SaveAsync。</summary>
    private static void AdvanceSourceSave(Visit v, Game game)
    {
        if (game == null || game.Pointer != v.Game)
        {
            Fail(v, "出现非源 Game 更新（尚未发起 Reload，拒绝）");
            return;
        }
        if (!SameSource(v, game, out Kingdom kingdom, out GlobalSaveData global, out CampaignSaveData campaign))
        {
            Fail(v, "源身份/land 漂移");
            return;
        }
        bool returned;
        int calls, callbacks, finished;
        lock (Gate)
        {
            returned = v.SaveReturned;
            calls = v.SaveCalls;
            callbacks = v.SourceCallbacks;
            finished = v.SourceCallbackFinished;
            if (v.Fault.Length != 0) { Fail(v, v.Fault); return; }
        }
        if (!returned || callbacks == 0 || finished != callbacks) return;
        int raw;
        lock (Gate) raw = v.SourceRaw;
        if (calls != 1 || callbacks != 1 || raw != SaveSuccess)
        {
            Fail(v, "源存档终态被拒：calls=" + calls + " callbacks=" + callbacks + " raw=" + raw);
            return;
        }
        if (!v.Returning)
        {
            string proofReason = "missing handle";
            if (v.Paid == null || !PalacePaidResponsibility.TryResolvePhysicalSource(v.Paid, raw, out var receipt, out proofReason))
            { Fail(v, "physical paid source refused: " + (v.Paid == null ? "no handle" : proofReason)); return; }
            v.ClockReceipt = receipt;
        }
        AdvanceSourceDurable(v, game);
    }

    private static void AdvanceSourceDurable(Visit v, Game game)
    {
        if (!SameSource(v, game, out Kingdom kingdom, out GlobalSaveData global, out CampaignSaveData campaign))
        { Fail(v, "durable source identity drift"); return; }
        if (IslandSaveData.isSavingGame || v.CarryAttempted) return;
        if (!VisitDestinationMatches(v) || (v.Returning && !VerifyReturnSourceRecord(v,global,out _)))
        { Fail(v,"destination drift before source effects"); return; }
        if (!v.Returning && !v.DepartureClaimed)
        {
            string claimReason = "missing handle";
            if (v.Paid == null || !PalacePaidResponsibility.TryClaimDeparture(v.Paid, out claimReason))
            { Fail(v, "durable departure claim refused: " + (v.Paid == null ? "no handle" : claimReason)); return; }
            v.DepartureClaimed = true;
            if (!PalaceClockBootstrap.TryPrepareOutbound(v.Paid, game, out var clockReason))
            { Fail(v, "outbound clock arm refused: " + clockReason); return; }
            v.ClockReceipt = v.Paid.SourceClockReceipt;
        }
        if (!SameSource(v, game, out kingdom, out global, out campaign) || IslandSaveData.isSavingGame)
        {
            Fail(v, "carry 前源身份/保存状态漂移");
            return;
        }
        lock (Gate)
        {
            if (v.Fault.Length != 0 || !SourceSaveProven(v)) { Fail(v, "carry 前源保存证明漂移"); return; }
            v.CarryAttempted = true;
        }
        int before1 = Coins(kingdom.playerOne) ?? -1;
        int? before2 = Coins(kingdom.playerTwo);
        Log("carry before " + CoinsText(kingdom) + "; " + CarryText(campaign) + "; Boats input once; eligibility not claimed");
        campaign.PopulateCarryForward(SailAwayGroup.Boats); // 唯一一次；不伪造船/单位/钱包；进入与回程同用
        int after1 = Coins(kingdom.playerOne) ?? -1;
        int? after2 = Coins(kingdom.playerTwo);
        Log("carry after " + CoinsText(kingdom) + "; " + CarryText(campaign)
            + "; walletChanged=" + (before1 != after1 || before2 != after2));

        if (!WriteTripRecord(v, global, campaign, out string recordDetail))
        {
            Fail(v, "行程记录失败：" + recordDetail);
            return;
        }
        if (!SameSource(v, game, out _, out _, out _) || IslandSaveData.isSavingGame
            || !VerifyWrittenDestination(v,global,out _))
        {
            Fail(v, "selection 前源身份漂移");
            return;
        }
        lock (Gate)
        {
            bool sourceProven = SourceSaveProven(v);
            if ((v.Stage != Phase.SourceSave && v.Stage != Phase.SourceDurable) || v.Fault.Length != 0 || !sourceProven || !v.RecordWritten)
            { Fail(v, "selection claim callback/stage drift"); return; }
            v.Stage = Phase.TargetSelected;
            v.Deadline = Time.realtimeSinceStartup + GlobalSaveSeconds;
        }
        campaign.CurrentLand = v.TargetLand;
        if (!SameSelectedSource(v, game, out _, out GlobalSaveData g, out _)
            || !VerifyWrittenDestination(v,g,out _))
        {
            Fail(v, "selection 读回被拒（不把 setter 当到达）");
            return;
        }
        Log("selected " + (v.Returning ? "return" : "enter") + " target=" + v.TargetLand
            + "; source=" + v.SourceLand + "; game.currentLand still=" + game.currentLand
            + "; 源 Kingdom 仍为捕获实例");

        v.TargetForward = result =>
        {
            lock (Gate)
            {
                v.GlobalCallbacks++;
                v.GlobalRaw = (int)result;
                v.GlobalFinished = true;
                if (v.GlobalCallbacks != 1) v.Fault = "duplicate-global-only-callback";
            }
            Log("global-only callback raw=" + (int)result + "; callback 内不做 Unity 操作");
        };
        v.TargetCallback = (Il2CppSystem.Action<SaveLoadResult>)v.TargetForward;
        try { g.SaveAsync(v.TargetCallback); } // GlobalOnly：只用 SaveAsync，不用 TriggerSave
        catch (Exception e)
        {
            Fail(v, "global-only SaveAsync 异常：" + e.GetType().Name);
            return;
        }
        Log("global-only SaveAsync returned; waiting single Save|Success(72)");
    }

    /// <summary>选择后：源身份不变 + 单回调 72 + 非保存中 → 下一帧先标记再唯一一次 Reload。</summary>
    private static void AdvanceGlobalSave(Visit v, Game game)
    {
        if (!SameSelectedSource(v, game, out _, out var global, out _)
            || !VerifyWrittenDestination(v,global,out _))
        {
            Fail(v, "选择后的源身份/布局漂移");
            return;
        }
        bool finished;
        int count, raw;
        lock (Gate)
        {
            if (v.Fault.Length != 0) { Fail(v, v.Fault); return; }
            finished = v.GlobalFinished;
            count = v.GlobalCallbacks;
            raw = v.GlobalRaw;
        }
        if (!finished) return;
        if (count != 1 || raw != SaveSuccess)
        {
            Fail(v, "global-only 存档终态被拒：callbacks=" + count + " raw=" + raw);
            return;
        }
        if (IslandSaveData.isSavingGame) return;
        lock (Gate)
        {
            if (v.Stage != Phase.TargetSelected || v.Fault.Length != 0 || !v.GlobalFinished || v.GlobalCallbacks != 1
                || v.GlobalRaw != SaveSuccess || !SourceSaveProven(v) || !v.RecordWritten)
            { Fail(v, "Reload claim callback/stage drift"); return; }
            v.Stage = Phase.ReloadIssued;
            v.Deadline = Time.realtimeSinceStartup + ReloadSeconds;
        }
        if (!PalaceLand12Runtime.BeginReloadHandoff(game, v.CampaignGuid, v.CampaignSlot, v.TripId, v.TargetLand, out string configHandoff))
        { Fail(v, "config Reload handoff refused: " + configHandoff); return; }
        Log("requesting native Reload once; " + (v.Returning ? "return" : "enter")
            + " target=" + v.TargetLand + "; source=" + v.SourceLand);
        try { game.Reload(); }
        catch (Exception e)
        {
            Fail(v, "Reload 异常，结果未知：" + e.GetType().Name);
            return;
        }
        v.SawLoading = game.state != Game.State.Playing;
        Log("native Reload returned state=" + game.state + "; 尚未claim到达");
    }

    /// <summary>
    /// Reload 后：只认当前 Managers.game 新对象；必须观察到离开源 Playing，且新场景 Playing +
    /// campaign/global 同实例 + campaign/game 都是 v.TargetLand 才算到达观测。
    /// </summary>
    private static void AdvanceReload(Visit v, Game game)
    {
        // 晚边界防御：同 API 单次采纳 active token 的新 runtime epoch（无 token/任何漂移一律拒绝，返回 false
        // 后仍由下方原 CurrentMatches 拒绝）。这里不是 Clock 的保护边界：return 的 TryPop/clock 首 writer 之前
        // 的早期插点由 Root 按独立时序审查接线（ApplyToScene 前桥仅为待定提议，未被本调用覆盖）；
        // 旧次数/callback/strongrefs 责任不变，不新增重试。
        PalaceSaveBindingSource.TryAdoptReloadBoundary(game, out _);
        if (!PalaceSaveBindingSource.CurrentMatches(out _) || game == null || NetworkBigBoss.IsOnline)
        {
            Fail(v, "Reload 后 owner 范围变化");
            return;
        }
        Managers m = Managers.Inst;
        if (m == null || m.game == null || m.game.Pointer != game.Pointer) return;
        if (game.Pointer == v.Game)
        {
            if (game.state != Game.State.Playing) v.SawLoading = true;
            return; // 源 Quitting/旧场景 update 不算目标
        }
        if (v.ArrivalGame == IntPtr.Zero)
        {
            v.ArrivalGame = game.Pointer;
            Log("post-Reload Game handoff source=" + v.Game.ToInt64() + " -> current=" + v.ArrivalGame.ToInt64());
        }
        else if (game.Pointer != v.ArrivalGame)
        {
            Fail(v, "Reload 后出现第二个 Game 实例");
            return;
        }
        if (game.state != Game.State.Playing) { v.SawLoading = true; return; }
        if (!v.SawLoading) return; // 只看 selection/新 Game 出现，绝不认到达
        GlobalSaveData global = GlobalSaveData.loaded;
        CampaignSaveData campaign = global != null ? global.GetCurrentCampaign() : null;
        CampaignSaveData current = CampaignSaveData.current;
        var first = Filer.agent as Il2CppObjectBase;
        var agent = first != null ? first.TryCast<Filer.DotNetAgent>() : null;
        if (global == null || global.InChallenge || campaign == null || current == null
            || campaign.Pointer != current.Pointer || campaign.CurrentLand != v.TargetLand
            || game.currentLand != v.TargetLand || agent == null
            || !PalaceSaveBindingSource.AgentMatchesCurrent(agent, out _)
            || !NetworkBigBoss.HasWorldAuth) return;
        if (m.kingdom == null || m.kingdom.playerOne == null || m.kingdom.playerOne.wallet == null) return;
        if (!PalaceTripContext.TryBinding(global, campaign, out string arrivalGuid, out int arrivalSlot, out _)) return;
        if (PalaceTripContext.Read(global, out var arrivedRecord, out _) != PalaceTripContext.ReadStatus.Ok
            || arrivalGuid != v.CampaignGuid || arrivalSlot != v.CampaignSlot || arrivedRecord.TripId != v.TripId
            || arrivedRecord.Phase != v.RecordPhase || !PalaceTripContext.RecordMatches(campaign, arrivalGuid, arrivalSlot, arrivedRecord, out _)
            || !RecordDestinationMatches(v,arrivedRecord) || v.WrittenRecord==null
            || !PalaceTripContext.TupleEquals(v.WrittenRecord,arrivedRecord))
        { Fail(v, "arrival durable trip/campaign identity mismatch"); return; }
        if (!PalaceClockBootstrap.WasPreparedFor(game, v.TripId, out string clockArrival))
        { Fail(v, "early clock boundary not verified: " + clockArrival); return; }
        if (!PalaceLand12Runtime.CompleteReloadHandoff(game, v.TripId, out string configArrival))
        { Fail(v, "config arrival handoff refused: " + configArrival); return; }
        if (v.Returning)
        {
            bool entryPrepared = PalaceLand12Runtime.PrepareForEntry(game, out string preparedReason);
            Log("return arrival entry config preparation=" + entryPrepared + "; " + preparedReason);
        }
        lock (Gate)
        {
            if (v.Stage != Phase.ReloadIssued) return;
            v.Stage = Phase.Arrived;
            _completed.Add(v);
            _status = v.Returning
                ? "已回到原岛 land " + v.TargetLand + "：新场景 Playing 观测（仅到达观测）"
                : "已到达 land12：新 Game Playing + campaign/game=12（仅到达观测）";
            if (ReferenceEquals(_visit, v)) _visit = null;
        }
        string paidArrival = "missing handle";
        if (v.Paid == null || !PalacePaidResponsibility.ObserveExactArrival(v.Paid, game, v.TripId, v.TargetLand, out paidArrival))
        {
            lock (Gate)
            {
                v.Fault = "paid arrival not committed: " + (v.Paid == null ? "missing handle" : paidArrival);
                _failedLatch = true; _status = v.Fault;
            }
            Log(v.Fault); return;
        }
        if (v.Returning) _commitAfterArrival = v;
        Snapshot(v, m.kingdom, campaign, "arrival");
        Log("arrival observed " + (v.Returning ? "return" : "enter") + " target=" + v.TargetLand
            + "; " + CoinsText(m.kingdom) + "; " + CarryText(campaign) + "; trip=" + v.TripId
            + "; source=" + v.SourceLand + "; 已离开旧 Playing；不claim mount/存档/玩家验收");
    }

    // ---- 身份/读取 ----

    private static bool SameSource(Visit v, Game game, out Kingdom kingdom, out GlobalSaveData global, out CampaignSaveData campaign)
    {
        kingdom = null;
        global = null;
        campaign = null;
        if (!TryRead(game, out kingdom, out global, out campaign, out IntPtr agent)) return false;
        return game.Pointer == v.Game && kingdom.Pointer == v.Kingdom && global.Pointer == v.Global
            && campaign.Pointer == v.Campaign && agent == v.Agent
            && campaign.CurrentLand == v.SourceLand && game.currentLand == v.SourceLand;
    }

    private static bool SameSelectedSource(Visit v, Game game, out Kingdom kingdom, out GlobalSaveData global, out CampaignSaveData campaign)
    {
        kingdom = null;
        global = null;
        campaign = null;
        if (!TryRead(game, out kingdom, out global, out campaign, out IntPtr agent)) return false;
        return game.Pointer == v.Game && kingdom.Pointer == v.Kingdom && global.Pointer == v.Global
            && campaign.Pointer == v.Campaign && agent == v.Agent && campaign.CurrentLand == v.TargetLand && game.currentLand == v.SourceLand;
    }

    /// <summary>源 scope 读取（不比较 campaign/Game 的 land 相等，交由调用方判定）：双读一致 + Filer 私有目录。</summary>
    private static bool TryRead(Game game, out Kingdom kingdom, out GlobalSaveData global, out CampaignSaveData campaign, out IntPtr agentPointer)
    {
        kingdom = null;
        global = null;
        campaign = null;
        agentPointer = IntPtr.Zero;
        try
        {
            if (!PalaceSaveBindingSource.CurrentMatches(out _) || game == null || game.state != Game.State.Playing
                || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) return false;
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.game.Pointer != game.Pointer || m.kingdom == null) return false;
            kingdom = m.kingdom;
            if (!kingdom.HasBorderLoaded || kingdom.playerOne == null || kingdom.playerOne.wallet == null) return false;
            global = GlobalSaveData.loaded;
            campaign = global != null ? global.GetCurrentCampaign() : null;
            CampaignSaveData current = CampaignSaveData.current;
            if (global == null || global.InChallenge || BiomeHolder.Inst == null
                || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex
                || campaign == null || current == null || campaign.Pointer == IntPtr.Zero
                || campaign.Pointer != current.Pointer) return false;
            var first = Filer.agent as Il2CppObjectBase;
            var agent = first != null && first.Pointer != IntPtr.Zero ? first.TryCast<Filer.DotNetAgent>() : null;
            if (agent == null || agent.Pointer != first.Pointer || !PalaceSaveBindingSource.AgentMatchesCurrent(agent, out _)) return false;
            var second = Filer.agent as Il2CppObjectBase;
            Managers fresh = Managers.Inst;
            var freshGlobal = GlobalSaveData.loaded;
            var freshCampaign = freshGlobal != null ? freshGlobal.GetCurrentCampaign() : null;
            if (second == null || second.Pointer != first.Pointer || fresh == null || fresh.game == null
                || fresh.game.Pointer != game.Pointer || fresh.kingdom == null || fresh.kingdom.Pointer != kingdom.Pointer
                || freshGlobal == null || freshGlobal.Pointer != global.Pointer || freshCampaign == null
                || freshCampaign.Pointer != campaign.Pointer) return false;
            agentPointer = first.Pointer;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool FilerReady()
    {
        try
        {
            var first = Filer.agent as Il2CppObjectBase;
            var agent = first != null && first.Pointer != IntPtr.Zero ? first.TryCast<Filer.DotNetAgent>() : null;
            if (agent == null || agent.Pointer != first.Pointer || !PalaceSaveBindingSource.AgentMatchesCurrent(agent, out _)) return false;
            var second = Filer.agent as Il2CppObjectBase;
            return second != null && second.Pointer == first.Pointer;
        }
        catch (Exception) { return false; }
    }

    private static bool IsKingdomPlayer(Kingdom kingdom, Player payer)
    {
        Player p1 = kingdom.playerOne;
        Player p2 = kingdom.playerTwo;
        return (p1 != null && p1.Pointer == payer.Pointer) || (p2 != null && p2.Pointer == payer.Pointer);
    }

    private static bool OutboundRecordAllowed(GlobalSaveData global, CampaignSaveData campaign, string guid, int slot, out string reason)
    {
        var state = PalaceTripContext.Read(global, out var old, out reason);
        if (state == PalaceTripContext.ReadStatus.Missing) return true;
        if (state != PalaceTripContext.ReadStatus.Ok) { reason = "已有行程记录未知/损坏，拒绝新收费："+reason; return false; }
        if (!PalaceTripContext.RecordMatches(campaign, guid, slot, old, out reason)
            || old.Phase != PalaceTripContext.PhaseReturnSelected || old.SourceLand != campaign.CurrentLand)
        { reason = "上一行程未在此原岛完成或身份不符，拒绝新收费："+reason; return false; }
        return true;
    }

    private static bool SameTrip(PalaceTripRecord a, PalaceTripRecord b)
        => PalaceTripContext.TupleEquals(a, b);

    private static bool RootsMatch()
    {
        try
        {
            // 旧 hardRoot（BepInEx 根 + env save 根）已由进程内唯一 PalaceSaveBinding 替代：
            // 同一绑定沿 visit/claim/callback/selection/reload 复核，不重捕漂移的根。
            return PalaceSaveBindingSource.CurrentMatches(out _);
        }
        catch (Exception) { return false; }
    }

    // ---- 只读快照（日志；不写坐骑/钱包） ----

    private static void Snapshot(Visit v, Kingdom kingdom, CampaignSaveData campaign, string tag)
    {
        try
        {
            Player p1 = kingdom != null ? kingdom.playerOne : null;
            string live = "liveSteed=none";
            if (p1 != null)
            {
                Steed steed = p1.steed;
                if (steed == null) live = "liveSteed=null";
                else
                {
                    int liveType = (int)steed.steedType;
                    Player rider = steed.Rider;
                    bool riderMatch = rider != null && rider.Pointer == p1.Pointer;
                    live = "liveType=" + liveType + "; riderMatch=" + riderMatch
                        + "; riderPtr=" + (rider != null ? rider.Pointer.ToInt64() : 0L)
                        + "; playerPos=" + p1.transform.position + "; steedPos=" + steed.transform.position
                        + "; steedLocal=" + steed.transform.localPosition
                        + "; steedChildOfPlayer=" + steed.transform.IsChildOf(p1.transform);
                }
            }
            Log("snapshot " + tag + " trip=" + (v != null ? v.TripId : "") + "; " + CoinsText(kingdom)
                + "; " + GemsText(p1) + "; " + live + "; saved=" + MountStatus(p1, campaign)
                + "; 只读日志，不写钱包/坐骑/PreviousLand");
        }
        catch (Exception e)
        {
            Log("snapshot " + tag + " fault (log-only): " + e.GetType().Name);
        }
    }

    /// <summary>campaign.GetPlayerSteedType / GetSteedStatus 只读（反射，log-only）；不索引 struct 数组。</summary>
    private static string MountStatus(Player player, CampaignSaveData campaign)
    {
        try
        {
            if(player==null || campaign==null)return "unavailable";
            SteedType savedType=campaign.GetPlayerSteedType(player.playerId);
            SteedStatus status=campaign.GetSteedStatus(savedType);
            return "savedType="+(int)savedType+"; status.land="+status.land+"; status.player="+status.player
                +"; status.position="+status.position+"; status.hasBeenRidden="+status.hasBeenRidden;
        }
        catch(Exception e){return "read-fault:"+e.GetType().Name;}
    }
    private static string GemsText(Player player)
        => player!=null && player.wallet!=null ? "Gems="+player.wallet.Gems : "Gems=unavailable";

    // ---- 工具 ----

    private static void Fail(Visit v, string reason)
    {
        if (v == null) return;
        lock (Gate)
        {
            if (v.Stage == Phase.Failed || v.Stage == Phase.Arrived) return;
            v.Stage = Phase.Failed;
            _failedLatch = true;
            if (!_completed.Contains(v)) _completed.Add(v);
            _status = "失败：" + reason + "（无重试、无退款、不自动回岛）";
            if (ReferenceEquals(_visit, v)) _visit = null;
        }
        PalaceLand12Runtime.AbortReloadHandoff(v.TripId);
        Log("failed: " + reason + "; no retry, no refund, no restore; trip=" + v.TripId
            + "; source=" + v.SourceLand + " -> " + v.TargetLand);
    }

    private static void SetStatus(string text) { lock (Gate) { _status = text; } }

    private static string PhaseName(Phase stage) => stage switch
    {
        Phase.SourceSave => "源岛存档 30s 窗口",
        Phase.TargetSelected => "全局存档 30s 窗口",
        Phase.ReloadIssued => "Reload 120s 窗口",
        _ => "窗口"
    };

    private static int? Coins(Player player) => player != null && player.wallet != null ? player.wallet.Coins : (int?)null;
    private static string N(int? value) => value.HasValue ? value.Value.ToString() : "?";
    private static string CoinsText(Kingdom kingdom)
        => "P1=" + N(Coins(kingdom != null ? kingdom.playerOne : null))
        + "; P2=" + N(Coins(kingdom != null ? kingdom.playerTwo : null));

    private static string Map(CurrencyMap<int> map)
        => map == null ? "null" : "Coins=" + map[CurrencyType.Coins] + ",Gems=" + map[CurrencyType.Gems];

    private static string CarryText(CampaignSaveData campaign)
    {
        var c = campaign != null ? campaign.carryForward : null;
        return c == null ? "carry=null" : "present=" + c.present + "; usesCurrencySystem=" + c.usesCurrencySystem
            + "; coins1=" + c.coins1 + "; coins2=" + c.coins2 + "; gems1=" + c.gems1 + "; gems2=" + c.gems2
            + "; currencyP1={" + Map(c.currencyP1) + "}; currencyP2={" + Map(c.currencyP2) + "}; group=" + c.sailAwayGroup;
    }

    private static void Log(string message) => PalaceVisibleEntry.Log("[PalaceTravel] " + message);

    // New helpers kept after existing methods to preserve generated callback identities.
    internal static bool ReturnAnchorEnabled => PalaceLand12Runtime.Enabled
        && PalaceRuntimeProfile.Capability("PALACE_RETURN_ANCHOR_PRIVATE");

    // Same exact native reload window as entering; V1/cold/nonactive returns never acquire placement.
    internal static bool TryGetReturningArrivalAnchor(Game game, string tripId, out PalaceEntryAnchorData anchor)
    {
        anchor = null;
        if (!ReturnAnchorEnabled) return false;
        lock (Gate)
        {
            Visit v = _visit;
            Managers current = Managers.Inst;
            if (current == null || current.game == null || game == null || current.game.Pointer != game.Pointer
                || v == null || !v.Returning || v.Stage != Phase.ReloadIssued || v.Fault.Length != 0
                || !v.RecordWritten || v.RecordPhase != PalaceTripContext.PhaseReturnSelected
                || string.IsNullOrEmpty(tripId) || tripId != v.TripId || game.Pointer == v.Game
                || v.ArrivalGame == IntPtr.Zero || game.Pointer != v.ArrivalGame || game.currentLand != v.TargetLand
                || !PalaceReturnAnchor.ValidateData(v.SourceEntryAnchor, out _)) return false;
            anchor = CloneAnchor(v.SourceEntryAnchor);
            return true;
        }
    }

    internal static bool SameAnchor(PalaceEntryAnchorData a, PalaceEntryAnchorData b)
        => a == null ? b == null : b != null && a.EntryX == b.EntryX && a.EntryY == b.EntryY
            && a.EntryZ == b.EntryZ && a.SourcePlayerX == b.SourcePlayerX && a.StandX == b.StandX && a.Side == b.Side;

    private static PalaceEntryAnchorData CloneAnchor(PalaceEntryAnchorData a)
        => a == null ? null : new PalaceEntryAnchorData { EntryX=a.EntryX, EntryY=a.EntryY, EntryZ=a.EntryZ,
            SourcePlayerX=a.SourcePlayerX, StandX=a.StandX, Side=a.Side };

    private static bool Claim(Game game, Kingdom kingdom, Player payer, bool returning, int targetLand,
        string tripId, string recordPhase, string recordEvidence, PalaceEntryAnchorData anchor, out string reason, PalacePaidHandle paid = null)
    {
        reason = "";
        if (anchor != null && !PalaceReturnAnchor.ValidateData(anchor, out reason)) return false;
        lock (Gate)
        {
            if (_visit != null) { reason = "已有旅程在途"; return false; }
            if (_failedLatch || _returnCheckpointFault.Length != 0)
            { reason = "前一次旅程/回程保存失败或未知，本次运行不再受理"; return false; }
        }
        if (!ExtensionIslandRuntime.EnsureFileCapacity())
        { reason = "land12 文件表未达共享 RequiredFileCapacity（失败不出发）"; return false; }
        if (!PalaceLand12Runtime.Ready)
        { reason = "land12 Ready 未满足（生成门/文件表）"; return false; }
        if (!TryRead(game, out Kingdom k, out GlobalSaveData g, out CampaignSaveData c, out IntPtr agent)
            || k.Pointer != kingdom.Pointer)
        { reason = "源身份/Manager 复核失败"; return false; }
        if (returning)
        {
            if (c.CurrentLand != PalaceLand12 || targetLand < 0 || targetLand > 11)
            { reason = "回程源/目标无效：" + c.CurrentLand + " -> " + targetLand; return false; }
        }
        else
        {
            if (c.CurrentLand == PalaceLand12 || c.CurrentLand != game.currentLand)
            { reason = "进入源岛无效：" + c.CurrentLand + " (game " + game.currentLand + ")"; return false; }
        }
        if (paid==null || paid.HasFault || paid.TripId.ToString("D")!=tripId)
        { reason="durable-paid-handle-required"; return false; }
        PalaceTripRecord sourceRecord=null;
        if (returning)
        {
            if (PalaceTripContext.Read(g,out sourceRecord,out reason)!=PalaceTripContext.ReadStatus.Ok
                || sourceRecord.Phase!=PalaceTripContext.PhasePalaceSelected || sourceRecord.TripId!=tripId
                || sourceRecord.SourceLand!=targetLand || !paid.MatchesDestination(sourceRecord)
                || !SameAnchor(sourceRecord.SourceEntryAnchor,anchor)) return false;
        }
        else if (!PalaceLayoutRecipe.IsKnown(paid.DestinationLayout)
            || !PalaceLand12Runtime.CanEnterPrepared(game, paid.DestinationLayout, out reason)) return false;
        if (!PalaceTripContext.TryBinding(g, c, out string claimGuid, out int claimSlot, out reason)) return false;
        Visit v;
        lock (Gate)
        {
            if (_visit != null) { reason = "已有旅程在途"; return false; }
            if (_failedLatch) { reason = "前一次旅程失败/未知"; return false; }
            v = new Visit
            {
                Stage = Phase.SourceSave,
                Returning = returning,
                Paid = paid,
                DestinationLayout = paid.DestinationLayout,
                RecordVersion = returning ? sourceRecord.Version : 4,
                SourceRecord = sourceRecord,
                ClockReceipt = paid != null ? paid.SourceClockReceipt : null,
                SourceEntryAnchor = CloneAnchor(anchor),
                CampaignGuid = claimGuid,
                CampaignSlot = claimSlot,
                TripId = tripId,
                RecordPhase = recordPhase,
                RecordEvidence = recordEvidence ?? "",
                Game = game.Pointer,
                Kingdom = k.Pointer,
                Global = g.Pointer,
                Campaign = c.Pointer,
                Agent = agent,
                SourceLand = c.CurrentLand,
                TargetLand = targetLand,
                Deadline = Time.realtimeSinceStartup + SourceSaveSeconds
            };
            _visit = v;
            _status = returning
                ? "已受理：回程源岛存档已请求（第 1/4 阶段，30s 窗口；受理≠到达）"
                : "已受理：进入源岛存档已请求（第 1/4 阶段，30s 窗口；受理≠到达）";
        }
        Log("claim " + (returning ? "return" : "enter") + " trip=" + tripId + " land=" + v.SourceLand
            + " -> " + targetLand + "; " + CoinsText(k) + "; game=" + v.Game.ToInt64()
            + "; kingdom=" + v.Kingdom.ToInt64() + "; global=" + v.Global.ToInt64()
            + "; campaign=" + v.Campaign.ToInt64() + "; payer=" + payer.Pointer.ToInt64()
            + "; sole source TriggerSave");
        Snapshot(v, k, c, "source-claim");
        try
        {
            if (!returning && paid != null && paid.Stage == PalacePaidStage.Resumable)
            {
                if (paid.SourceClockReceipt == null) { Fail(v,"resumable receipt missing"); return false; }
                v.UsedDurableResume = true;
                v.Stage = Phase.SourceDurable; // Actual durable file proof, no synthetic callback/SaveCalls.
                return true;
            }
            if (!returning && paid != null && paid.Stage == PalacePaidStage.SourceCaptured
                && !PalacePaidResponsibility.TryValidateSourceCapturedBeforeVerification(paid,out var preflightReason))
            { Fail(v,"source verification preflight refused:"+preflightReason); return false; }
            game.TriggerSave(); // Fresh source or SourceCaptured verification: exactly one real save, no new fee.
            v.SaveReturned = true;
        }
        catch (Exception e)
        {
            Fail(v, "TriggerSave 异常，结果未知：" + e.GetType().Name);
            reason = "TriggerSave 异常，结果未知；已阻断（无重试/退款）";
            return false;
        }
        Log("source TriggerSave returned; waiting exact Save|Success(72) callback");
        return true;
    }


    private static bool VisitDestinationMatches(Visit v)
    {
        if(v==null||v.Paid==null||v.Paid.HasFault||v.Paid.TripId.ToString("D")!=v.TripId) return false;
        if(v.RecordVersion==4)
            return PalaceLayoutRecipe.IsKnown(v.DestinationLayout)&&ReferenceEquals(v.DestinationLayout,v.Paid.DestinationLayout);
        return v.Returning&&v.RecordVersion==3&&v.DestinationLayout==null&&v.Paid.DestinationLayout==null
            &&v.SourceRecord!=null&&v.Paid.MatchesDestination(v.SourceRecord);
    }
    private static bool RecordDestinationMatches(Visit v,PalaceTripRecord record)
        => VisitDestinationMatches(v)&&record!=null&&record.Version==v.RecordVersion
            &&v.Paid.MatchesDestination(record)
            &&(record.Version==3 || ReferenceEquals(record.DestinationLayout,v.DestinationLayout));
    private static bool VerifyReturnSourceRecord(Visit v,GlobalSaveData global,out string reason)
    {
        reason="return-source-record-drift";
        return v.SourceRecord!=null&&PalaceTripContext.Read(global,out var current,out reason)==PalaceTripContext.ReadStatus.Ok
            &&PalaceTripContext.TupleEquals(current,v.SourceRecord)&&v.Paid.MatchesDestination(current);
    }
    private static bool VerifyWrittenDestination(Visit v,GlobalSaveData global,out string reason)
    {
        reason="written-destination-drift";
        return v.WrittenRecord!=null&&PalaceTripContext.Read(global,out var current,out reason)==PalaceTripContext.ReadStatus.Ok
            &&RecordDestinationMatches(v,current)&&PalaceTripContext.TupleEquals(current,v.WrittenRecord);
    }

    private static bool SourceSaveProven(Visit v)
        => v.UsedDurableResume
            ? !v.Returning && v.DepartureClaimed && v.Paid != null && !v.Paid.HasFault
                && v.Paid.Stage == PalacePaidStage.DepartureClaimed && v.ClockReceipt != null
            : v.SaveReturned && v.SaveCalls == 1 && v.SourceCallbacks == 1
                && v.SourceCallbackFinished == 1 && v.SourceRaw == SaveSuccess;

    // Projection of the existing completed driver only: no caller-supplied success, token or state mutation.
    internal static bool TryDescribeCompletedPaidArrival(PalacePaidHandle paid, Game game, string tripId, int targetLand,
        out IntPtr oldGame, out IntPtr oldGlobal, out IntPtr oldCampaign,
        out GlobalSaveData newGlobal, out CampaignSaveData newCampaign, out string reason)
    {
        oldGame=oldGlobal=oldCampaign=IntPtr.Zero; newGlobal=null; newCampaign=null; reason="completed arrival unavailable";
        lock (Gate)
        {
            var m=Managers.Inst; var g=GlobalSaveData.loaded; var c=CampaignSaveData.current;
            if (paid==null || game==null || m==null || m.game==null || m.game.Pointer!=game.Pointer
                || game.state!=Game.State.Playing || game.currentLand!=targetLand || g==null || c==null
                || g.GetCurrentCampaign()==null || g.GetCurrentCampaign().Pointer!=c.Pointer
                || c.CurrentLand!=targetLand || !PalaceSaveBindingSource.CurrentMatches(out reason)) return false;
            for (int i=_completed.Count-1;i>=0;i--)
            {
                Visit v=_completed[i];
                if (v.TripId!=tripId) continue;
                if (!ReferenceEquals(v.Paid,paid) || v.Stage!=Phase.Arrived || v.Fault.Length!=0
                    || v.ArrivalGame!=game.Pointer || v.TargetLand!=targetLand || game.Pointer==v.Game) return false;
                oldGame=v.Game; oldGlobal=v.Global; oldCampaign=v.Campaign;
                newGlobal=g; newCampaign=c; reason="exact completed driver roots"; return true;
            }
            return false;
        }
    }

    private sealed class ReturnSaveObservation
    {
        internal PalacePaidHandle Paid;
        internal Game Game;
        internal GlobalSaveData Global;
        internal int Calls, Finished, Raw;
        internal bool NativeCallFinished, NativeRan, PostfixSeen, ConfirmationClaimed;
        internal string Fault = "";
        internal Il2CppSystem.Action<SaveLoadResult> Original, Wrapper;
        internal System.Action<SaveLoadResult> Forward;
    }

    private static ReturnSaveObservation ObserveReturnedSavePrefix(GlobalSaveData global, ref Il2CppSystem.Action<SaveLoadResult> callback)
    {
        var m = Managers.Inst;
        if (m == null || m.game == null || m.kingdom == null || m.kingdom.playerOne == null
            || global == null || callback == null || !PalacePaidResponsibility.TryGetPreparedHandle(m.game,m.kingdom,m.kingdom.playerOne,out var paid,out _)
            || paid.Stage != PalacePaidStage.Returned) return null;
        var loaded=GlobalSaveData.loaded; var campaign=CampaignSaveData.current;
        if (loaded == null || global.Pointer != loaded.Pointer || campaign == null
            || loaded.GetCurrentCampaign() == null || loaded.GetCurrentCampaign().Pointer != campaign.Pointer
            || campaign.CurrentLand != m.game.currentLand || m.game.currentLand != paid.SourceLand)
        { lock(Gate) { if (_returnCheckpointFault.Length==0) _returnCheckpointFault="foreign return SaveAsync owner"; } return null; }
        var o = new ReturnSaveObservation { Paid=paid, Game=m.game, Global=global, Original=callback };
        o.Forward = result =>
        {
            lock(Gate)
            {
                o.Calls++;
                if (o.Calls != 1) SetReturnObservationFault(o,"duplicate return save callback");
            }
            try { o.Original.Invoke(result); }
            catch(Exception e) { lock(Gate) SetReturnObservationFault(o,"return original callback threw:"+e.GetType().Name); throw; }
            finally { lock(Gate) { o.Raw=(int)result; o.Finished++; } }
        };
        o.Wrapper=(Il2CppSystem.Action<SaveLoadResult>)o.Forward;
        lock(Gate)
        {
            if (_returnCheckpointFault.Length!=0) return null;
            if (_returnSave != null)
            { SetReturnObservationFault(_returnSave,"interleaved return save"); return null; }
            _returnSave=o; _returnSaveRoots.Add(o); callback=o.Wrapper;
        }
        return o;
    }

    private static void SavePostfix(bool __runOriginal, ReturnSaveObservation __state)
    {
        if (__state == null) return;
        lock(Gate)
        {
            __state.PostfixSeen=true; __state.NativeRan=__runOriginal;
            if (!__runOriginal) SetReturnObservationFault(__state,"return SaveAsync native body skipped");
        }
    }

    // Observe this exact SaveAsync call; never swallow or replace its original exception.
    private static void SaveFinalizer(Exception __exception, bool __runOriginal, ReturnSaveObservation __state)
    {
        if (__state == null) return;
        lock(Gate)
        {
            __state.NativeCallFinished=true;
            if (__exception != null || !__runOriginal || !__state.PostfixSeen || !__state.NativeRan)
                SetReturnObservationFault(__state,"return SaveAsync did not finish native normally:"+(__exception?.GetType().Name ?? "skipped"));
        }
    }

    // Caller holds Gate. Late faults retain their original observation and never rewrite a newer one.
    private static void SetReturnObservationFault(ReturnSaveObservation observation, string reason)
    {
        if(observation.Fault.Length==0) observation.Fault=reason;
        if(observation.ConfirmationClaimed)
        { _failedLatch=true; _status="late return checkpoint fault: "+observation.Fault; }
    }

    private static void AdvanceReturnedSave(Game game)
    {
        if (game == null) return;
        Visit pending;
        lock(Gate) { if(_returnCheckpointFault.Length!=0 || _returnTriggerInProgress) return; pending=_commitAfterArrival; }
        if (pending != null)
        {
            if (game.Pointer != pending.ArrivalGame || game.state != Game.State.Playing || IslandSaveData.isSavingGame) return;
            var pendingGlobal=GlobalSaveData.loaded;
            bool trigger=false;
            lock(Gate)
            {
                if(!ReferenceEquals(pending,_commitAfterArrival)||_returnCheckpointFault.Length!=0) return;
                _commitAfterArrival=null; // One real return checkpoint; a throw/result-unknown is not retried.
                if (_returnSave != null)
                {
                    var natural=_returnSave;
                    if(!ReferenceEquals(natural.Paid,pending.Paid) || natural.Game.Pointer!=game.Pointer
                        || pendingGlobal==null || natural.Global.Pointer!=pendingGlobal.Pointer
                        || natural.Fault.Length!=0 || natural.ConfirmationClaimed || pending.Paid==null || pending.Paid.HasFault)
                    { _returnCheckpointFault="existing natural return save identity/fault mismatch"; _failedLatch=true; _status=_returnCheckpointFault; return; }
                    // Consume/wait for this exact natural save; never start a second writer or reset its evidence.
                }
                else { _returnTriggerInProgress=true; trigger=true; }
            }
            if(trigger)
            {
            try { game.TriggerSave(); }
            catch(Exception e)
            {
                lock(Gate)
                {
                    if(_returnCheckpointFault.Length==0) _returnCheckpointFault="return checkpoint TriggerSave threw:"+e.GetType().Name;
                    _failedLatch=true;
                    if (_returnSave != null) SetReturnObservationFault(_returnSave,_returnCheckpointFault);
                    _status=_returnCheckpointFault;
                }
                Log(_status); return;
            }
            finally { lock(Gate) _returnTriggerInProgress=false; }
            lock(Gate)
            {
                if (_returnSave == null)
                { if(_returnCheckpointFault.Length==0) _returnCheckpointFault="return checkpoint had no exact native SaveAsync observation"; _failedLatch=true; _status=_returnCheckpointFault; Log(_status); return; }
            }
            }
        }
        if(IslandSaveData.isSavingGame) return;
        var loaded=GlobalSaveData.loaded;
        ReturnSaveObservation o; int raw;
        lock(Gate)
        {
            if(_returnCheckpointFault.Length!=0) return;
            o=_returnSave;
            if(o==null || o.Calls==0 || o.Finished!=o.Calls) return;
            if(game.Pointer != o.Game.Pointer || loaded == null || loaded.Pointer != o.Global.Pointer
                || !o.NativeCallFinished || !o.PostfixSeen || !o.NativeRan || o.Fault.Length != 0 || o.Calls != 1 || o.Raw != SaveSuccess)
            { _returnCheckpointFault="return checkpoint outcome unknown: "+o.Fault+" raw="+o.Raw; _failedLatch=true; _status=_returnCheckpointFault; Log(_status); return; }
            raw=o.Raw; o.ConfirmationClaimed=true; _returnSave=null; // Claim one terminal check under Gate; no native or physical IO under this lock.
        }
        bool committed; string why;
        try { committed=PalacePaidResponsibility.TryConfirmReturnedSave(o.Paid,raw,out why); }
        catch(Exception e) { committed=false; why="physical commit threw:"+e.GetType().Name; }
        if (!committed) { lock(Gate) { if(_returnCheckpointFault.Length==0) _returnCheckpointFault="returned physical commit refused:"+why; _failedLatch=true; _status=_returnCheckpointFault; } }
        Log("returned native checkpoint committed="+committed+"; "+why);
    }

}
