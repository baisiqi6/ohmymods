using System;
using System.Collections.Generic;
using Coatsink.Common;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 金币哥布林·campaign 存档接线（唯一身份 + 真实钱袋）。契约来自 issue-56 plan（fecb0b62）、
/// persistence-contract-review / persistence-r2-review（GLM APPROVE）与 native-save-research.md
/// 的实际 2.4 证据：
/// <list type="bullet">
/// <item>只使用 <c>GlobalSaveData.prefs</c> 原生 string KV 的一个命名空间键；不写原生顶层 schema、
/// 不用 sidecar/Filer、不碰银行 UnityEngine.PlayerPrefs。</item>
/// <item>live/staged 分离：经济只改 live；仅「当前岛原生捕获成功」才把 live 快照 stage 进键。</item>
/// <item>成功门 = <c>IslandSaveData.Save(campaign,land,challenge)</c> 同步 scope +
/// <c>UpdateSavedWithRevisions</c> 正常 postfix 同实例 marker（原生内部 catch 异常分支不经过该调用）。</item>
/// <item>身份 = campaign 槽位→自有 Guid；加载时按槽位绑定，之后按原生对象身份维护；
/// <c>PrepareBeforeSave</c> 只按幸存对象重排 staged，绝不采样 live。</item>
/// <item>任何无法确定/读取失败/损坏：只关闭本模块（不写、不猜、不截断），有界诊断。</item>
/// </list>
/// Harmony 钩子全部是记账型适配层（两行转调本类 internal 边界入口），绝不阻断/改写原生
/// 创建、删除与保存流程；异常一律隔离。
/// </summary>
internal static class CoinCourierPersistence
{
    // ============================================================ 诊断（有界）

    private const int MaxDiagnosticKeys = 96;
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>(StringComparer.Ordinal);

    private static void NoteOnce(string key, string message, bool error)
    {
        try
        {
            if (LoggedOnce.Count >= MaxDiagnosticKeys)
            {
                if (LoggedOnce.Count == MaxDiagnosticKeys)
                {
                    LogRaw("[CoinCourierPersist] diagnostics saturated; further notes suppressed", true);
                    LoggedOnce.Add("<saturated>");
                }
                return;
            }
            if (!LoggedOnce.Add(key)) return;
            LogRaw("[CoinCourierPersist] " + message, error);
        }
        catch (Exception)
        {
        }
    }

    private static void LogRaw(string message, bool error)
    {
        try
        {
            if (error) KingdomEnhancedPlugin.Instance?.LogSource?.LogError(message);
            else KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo(message);
        }
        catch (Exception)
        {
        }
    }

    // ============================================================ 捕获 scope

    /// <summary>
    /// IslandSaveData.Save 的同步捕获窗口。prefix 建立、postfix/finalizer 关闭；
    /// 只有 <see cref="Live"/>（准确 Game/World/gameLayer + 现场岛 Game.currentLand 与保存目标一致）
    /// 且见到同实例 marker 才 stage。
    /// </summary>
    internal sealed class SaveScope
    {
        internal SaveScope Previous;
        internal int Campaign;
        internal int Land;
        internal int Challenge;
        internal IntPtr GlobalPtr;
        internal IntPtr GamePtr;
        internal IntPtr WorldPtr;
        internal IntPtr LayerPtr;
        internal IntPtr CampaignDataPtr;
        /// <summary>prefix 时 <c>Game.currentLand</c>：真实现场岛（离岛保存期间仍是出发岛）。</summary>
        internal int SceneLand;
        /// <summary>原生 Save 实际解析的目标岛：land &gt;= 0 ? land : campaign.CurrentLand。</summary>
        internal int EffectiveLand;
        internal CampaignBinding Binding;
        internal bool MarkerSeen;
        internal bool Live;
        internal bool Closed;
    }

    private static SaveScope _scope;

    // ============================================================ 绑定

    internal sealed class GlobalBinding
    {
        internal IntPtr GlobalPtr;
        internal IntPtr PrefsPtr;
        internal readonly Dictionary<IntPtr, CampaignBinding> ByCampaign = new Dictionary<IntPtr, CampaignBinding>();
        internal bool Closed;
        /// <summary>关闭原因：只读诊断，仅在仍是当前 native loaded global 时对外可见。</summary>
        internal string CloseReason;
        /// <summary>Prepare 链写失败（纯重排/落键）：成功重写即清除；绝不能解除 campaign 捕获故障。</summary>
        internal string PrepareWriteFault;
        /// <summary>本 global 的键是否已存在（含首次成功写入）；空文档不凭空创建键。</summary>
        internal bool KeyEverPresent;
    }

    /// <summary>
    /// 一个普通 campaign 的 live+staged 状态；作为 <see cref="ICoinCourierCampaignState"/>
    /// 交给运行时。Ready/Owned/Purse 只读，绝不清空。
    /// </summary>
    internal sealed class CampaignBinding : ICoinCourierCampaignState
    {
        internal GlobalBinding Owner;
        internal IntPtr CampaignPtr;
        internal int Slot = -1;
        internal string Guid;
        internal bool Detached;

        internal bool LiveOwned;
        internal CoinCourierPurse LivePurse;

        internal bool StagedOwned;
        internal CoinCourierPurseSnapshot StagedPurse;
        /// <summary>非 null = 该 campaign 的捕获失败已冻结（保留旧 staged）。</summary>
        internal string StageFault;

        internal bool HasStagedState => CoinCourierSaveCodec.HasContent(StagedOwned, StagedPurse);

        internal CoinCourierCampaignRecord ToRecord(int slot)
        {
            return new CoinCourierCampaignRecord
            {
                Slot = slot,
                Guid = Guid,
                Owned = StagedOwned,
                Purse = CoinCourierSaveCodec.ToPurseRecord(StagedPurse),
            };
        }

        CoinCourierAvailabilityInfo ICoinCourierCampaignState.Availability => Evaluate(this);

        bool ICoinCourierCampaignState.Ready => Evaluate(this).Kind == CoinCourierAvailability.Ready;

        bool ICoinCourierCampaignState.Owned => LiveOwned;

        CoinCourierPurse ICoinCourierCampaignState.Purse => LivePurse;

        bool ICoinCourierCampaignState.TryRecordRecruitment() => RecordRecruitment(this);
    }

    private static GlobalBinding _bound;
    private static readonly List<IntPtr> SyncScratch = new List<IntPtr>();
    private static readonly List<IntPtr> SyncRemovals = new List<IntPtr>();

    // ============================================================ 运行时接口

    /// <summary>
    /// 维护入口（Operator 在 AddToScene 之后统一 Tick；单点接线）。幂等：绑定/同步/把运行时
    /// 接到当前 campaign 的 state。整函数异常隔离，失败只影响本模块。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (loaded == null) return;
            if (_bound == null || _bound.GlobalPtr != loaded.Pointer) BindGlobal(loaded);
            SyncOrder();
            BindRuntimeForCurrent();
        }
        catch (Exception e)
        {
            NoteOnce("tick:" + e.GetType().Name, "tick failed: " + e.GetType().Name, true);
        }
    }

    /// <summary>
    /// 复用既有 CampaignSaveData.ApplyToScene patch owner 的确认入口（MusketeerPersistence
    /// postfix 末尾调用，自包 try/catch）。applied 只作确认提示：绑定仍以原生
    /// CampaignSaveData.current 为准，避免把非当前世代绑进运行时。
    /// </summary>
    internal static void EnsureBoundFromApplyToScene(CampaignSaveData applied)
    {
        try
        {
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (loaded == null) return;
            if (_bound == null || _bound.GlobalPtr != loaded.Pointer) BindGlobal(loaded);
            SyncOrder();
            CampaignSaveData current = SafeCurrentCampaign();
            if (applied != null && current != null && applied.Pointer != current.Pointer)
            {
                NoteOnce("apply-mismatch", "ApplyToScene confirmed a non-current campaign; binding follows CampaignSaveData.current", false);
            }
            BindRuntimeForCurrent();
        }
        catch (Exception e)
        {
            NoteOnce("apply:" + e.GetType().Name, "apply-to-scene binding failed: " + e.GetType().Name, true);
        }
    }

    // ============================================================ 原生边界入口
    // Harmony 钩子与离线测试共用这些入口；它们不直接依赖 Harmony。

    internal static SaveScope BeginIslandSave(int campaign, int land, int challenge)
    {
        SaveScope scope = null;
        try
        {
            scope = new SaveScope
            {
                Previous = _scope,
                Campaign = campaign,
                Land = land,
                Challenge = challenge,
            };
            Tick();
            GlobalBinding bound = _bound;
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (bound != null && !bound.Closed && loaded != null && loaded.Pointer == bound.GlobalPtr)
            {
                scope.GlobalPtr = loaded.Pointer;
                CampaignSaveData current = SafeCurrentCampaign();
                scope.CampaignDataPtr = current != null ? current.Pointer : IntPtr.Zero;
                if (current != null) bound.ByCampaign.TryGetValue(current.Pointer, out scope.Binding);
                scope.EffectiveLand = land >= 0 ? land : (current != null ? current.CurrentLand : -1);
                // Live = 准确现场身份（Game/World/gameLayer + Game.currentLand）+ 当前 campaign 映射
                // + 保存目标就是现场岛。离岛真实顺序（campaign.CurrentLand 先改目的地、再保存出发现场）
                // 因此可 stage；旁岛/Decay（目标 != 现场）与任何场景换代一律拒绝。
                scope.Live = scope.Binding != null && !scope.Binding.Detached
                    && loaded.currentCampaign == campaign && loaded.currentChallenge == challenge
                    && current != null
                    && TryCaptureScene(scope)
                    && scope.EffectiveLand == scope.SceneLand;
            }
        }
        catch (Exception e)
        {
            NoteOnce("scope-begin:" + e.GetType().Name, "save scope begin failed: " + e.GetType().Name, true);
            scope = null;
        }
        if (scope != null) _scope = scope;
        return scope;
    }

    /// <summary>postfix(normalReturn=true) 做 stage；finalizer(false)/异常路径只关闭 scope。</summary>
    internal static void EndIslandSave(SaveScope scope, bool normalReturn)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        if (ReferenceEquals(_scope, scope)) _scope = scope.Previous;
        if (!normalReturn) return;
        try
        {
            CompleteCapture(scope);
        }
        catch (Exception e)
        {
            NoteOnce("capture:" + e.GetType().Name, "capture completion failed: " + e.GetType().Name, true);
        }
    }

    /// <summary>IslandSaveData.UpdateSavedWithRevisions 正常 postfix：仅同 scope 的同实例标成功。</summary>
    internal static void ObserveMarker(IslandSaveData island)
    {
        SaveScope scope = _scope;
        if (scope == null || island == null) return;
        try
        {
            IslandSaveData saving = IslandSaveData.CurrentlySavingIsland;
            if (saving != null && saving.Pointer == island.Pointer) scope.MarkerSeen = true;
        }
        catch (Exception e)
        {
            NoteOnce("marker:" + e.GetType().Name, "marker observation failed: " + e.GetType().Name, true);
        }
    }

    /// <summary>PrefsSaveData.PrepareBeforeSave prefix：只按幸存原生对象重排 staged，绝不采样 live。</summary>
    internal static void ObservePrepare(PrefsSaveData prefs)
    {
        try
        {
            GlobalBinding bound = _bound;
            if (bound == null || bound.Closed) return;
            if (prefs == null || prefs.Pointer != bound.PrefsPtr) return;
            SyncOrder();
            if (bound.Closed) return;
            if (!RewriteStaged(bound, "prepare", out string writeReason)) SetPrepareWriteFault(bound, writeReason);
        }
        catch (Exception e)
        {
            NoteOnce("prepare:" + e.GetType().Name, "prepare observation failed: " + e.GetType().Name, true);
        }
    }

    /// <summary>
    /// campaign 结构变动前的提前绑定（CreateNewCampaign prefix / TryDeleteCampaignAsync prefix /
    /// 删除协程状态机 state==0）。只看已安装的 Global；__instance 尚未 loaded 时不套旧 pref 键。
    /// </summary>
    internal static void ObserveCampaignMutation(GlobalSaveData instance)
    {
        try
        {
            if (instance == null) return;
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (loaded == null || loaded.Pointer != instance.Pointer) return;
            if (_bound == null || _bound.GlobalPtr != loaded.Pointer) BindGlobal(loaded);
            SyncOrder();
        }
        catch (Exception e)
        {
            NoteOnce("mutation:" + e.GetType().Name, "campaign mutation prefix failed: " + e.GetType().Name, true);
        }
    }

    /// <summary>
    /// 删除协程状态机 MoveNext 的记账入口：只在首次执行（__1__state == 0，RemoveAt 之前）绑定。
    /// 只读 state；绝不读写 Return&lt;T&gt;/native 协程状态；异常只留一次有界诊断，不阻塞原生 body。
    /// </summary>
    internal static void ObserveDeleteRoutineState(GlobalSaveData.__TryDeleteCampaign_d__91 routine)
    {
        if (routine == null) return;
        try
        {
            if (routine.__1__state != 0) return;
        }
        catch (Exception e)
        {
            NoteOnce("delete-routine:" + e.GetType().Name,
                "delete routine state read failed: " + e.GetType().Name, true);
            return;
        }
        ObserveCampaignMutation(SafeLoaded());
    }

    /// <summary>CreateNewCampaign postfix：新实例（原生总是 new）在下次同步拿到全新 Guid。</summary>
    internal static void ObserveCampaignCreated(GlobalSaveData instance, CampaignSaveData result)
    {
        try
        {
            if (instance == null || result == null) return;
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (loaded == null || loaded.Pointer != instance.Pointer) return;
            if (_bound == null || _bound.GlobalPtr != loaded.Pointer) BindGlobal(loaded);
            SyncOrder();
        }
        catch (Exception e)
        {
            NoteOnce("create:" + e.GetType().Name, "campaign create postfix failed: " + e.GetType().Name, true);
        }
    }

    // ============================================================ 绑定实现

    private static void BindGlobal(GlobalSaveData loaded)
    {
        try
        {
            ICoinCourierCampaignState runtimeState = CoinCourierRuntime.State;
            if (runtimeState is CampaignBinding) CoinCourierRuntime.Unbind(runtimeState);
        }
        catch (Exception)
        {
        }
        var bound = new GlobalBinding { GlobalPtr = loaded.Pointer };
        _bound = bound;

        PrefsSaveData prefs = null;
        try { prefs = loaded.prefs; } catch (Exception) { }
        if (prefs == null)
        {
            Close(bound, "prefs-missing");
            return;
        }
        bound.PrefsPtr = prefs.Pointer;

        Dictionary<int, CoinCourierCampaignRecord> recordsBySlot = null;
        bool keyPresent = false;
        try
        {
            Il2CppSystem.Collections.Generic.Dictionary<string, string> contents = prefs.contents;
            if (contents == null)
            {
                Close(bound, "contents-missing");
                return;
            }
            keyPresent = contents.ContainsKey(CoinCourierSaveSchema.Key);
            if (keyPresent)
            {
                string raw = contents[CoinCourierSaveSchema.Key];
                if (raw == null)
                {
                    Close(bound, "null-value");
                    return;
                }
                if (!CoinCourierSaveCodec.TryParse(raw, out CoinCourierDocument document, out string parseReason))
                {
                    Close(bound, "corrupt-" + parseReason);
                    return;
                }
                bound.KeyEverPresent = true;
                recordsBySlot = new Dictionary<int, CoinCourierCampaignRecord>();
                for (int i = 0; i < document.Campaigns.Count; i++)
                {
                    CoinCourierCampaignRecord record = document.Campaigns[i];
                    recordsBySlot[record.Slot] = record;
                }
            }
        }
        catch (Exception e)
        {
            Close(bound, "read-" + e.GetType().Name);
            return;
        }

        Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns;
        try { campaigns = loaded.campaigns; } catch (Exception) { Close(bound, "campaigns"); return; }
        if (recordsBySlot != null)
        {
            int count;
            try { count = campaigns == null ? 0 : campaigns.Count; } catch (Exception) { Close(bound, "campaigns-read"); return; }
            foreach (KeyValuePair<int, CoinCourierCampaignRecord> pair in recordsBySlot)
            {
                if (pair.Key >= count)
                {
                    Close(bound, "record-slot-range");
                    return;
                }
            }
        }
        RebuildOrder(bound, campaigns, recordsBySlot);
        if (bound.Closed) return;
        NoteOnce("bind", "bound global campaigns=" + bound.ByCampaign.Count
            + " key=" + (keyPresent ? "present" : "missing"), false);
    }

    /// <summary>
    /// 只读的"当前 global 已关闭"诊断：<see cref="GlobalBinding.CloseReason"/> 是唯一 owner，
    /// 这里每次调用都即时核对"仍是当前绑定且与当前 native loaded global 精确同体"；
    /// 不缓存、不发布、无生命周期清理——换档后旧原因自然读不到。
    /// 仅供状态文案在 Runtime.State==null 时取关闭原因，绝不参与交易判定。
    /// </summary>
    internal static bool TryGetCurrentClosedDiagnostic(out CoinCourierAvailabilityInfo info)
    {
        info = default;
        try
        {
            GlobalBinding bound = _bound;
            if (bound == null || !bound.Closed || string.IsNullOrEmpty(bound.CloseReason)) return false;
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (loaded == null || loaded.Pointer != bound.GlobalPtr) return false;
            info = new CoinCourierAvailabilityInfo(CoinCourierAvailability.Closed, bound.CloseReason);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void SyncOrder()
    {
        GlobalBinding bound = _bound;
        if (bound == null || bound.Closed) return;
        GlobalSaveData loaded = GlobalSaveData._loaded;
        if (loaded == null || loaded.Pointer != bound.GlobalPtr) return;
        RebuildOrder(bound, loaded.campaigns, null);
    }

    private static void RebuildOrder(GlobalBinding bound,
        Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns,
        Dictionary<int, CoinCourierCampaignRecord> recordsBySlot)
    {
        int count;
        try { count = campaigns == null ? 0 : campaigns.Count; } catch (Exception) { Close(bound, "campaigns-read"); return; }
        SyncScratch.Clear();
        for (int i = 0; i < count; i++)
        {
            CampaignSaveData campaign;
            try { campaign = campaigns[i]; } catch (Exception) { continue; }
            if (campaign == null || campaign.Pointer == IntPtr.Zero) continue;
            IntPtr pointer = campaign.Pointer;
            SyncScratch.Add(pointer);
            if (bound.ByCampaign.TryGetValue(pointer, out CampaignBinding existing))
            {
                existing.Slot = i;
                continue;
            }
            if (campaign.challengeId != 0) continue; // 挑战条目（原生独立/混合分支）不纳入本首版
            if (!TryCreateBinding(bound, campaign, i, recordsBySlot, out CampaignBinding created)) return;
            bound.ByCampaign[pointer] = created;
        }
        SyncRemovals.Clear();
        foreach (KeyValuePair<IntPtr, CampaignBinding> pair in bound.ByCampaign)
        {
            if (!SyncScratch.Contains(pair.Key)) SyncRemovals.Add(pair.Key);
        }
        for (int i = 0; i < SyncRemovals.Count; i++)
        {
            if (bound.ByCampaign.TryGetValue(SyncRemovals[i], out CampaignBinding dead))
            {
                bound.ByCampaign.Remove(SyncRemovals[i]);
                dead.Detached = true;
            }
        }
    }

    private static bool TryCreateBinding(GlobalBinding bound, CampaignSaveData campaign, int slot,
        Dictionary<int, CoinCourierCampaignRecord> recordsBySlot, out CampaignBinding binding)
    {
        binding = null;
        CoinCourierCampaignRecord record = null;
        if (recordsBySlot != null) recordsBySlot.TryGetValue(slot, out record);
        bool stagedOwned = false;
        CoinCourierPurseSnapshot stagedPurse = CoinCourierSaveCodec.EmptyPurseSnapshot;
        string guid = CoinCourierSaveCodec.CreateGuid();
        if (record != null)
        {
            stagedOwned = record.Owned;
            guid = record.Guid;
            if (!CoinCourierSaveCodec.TryReadPurse(record.Purse, out stagedPurse, out string purseReason))
            {
                Close(bound, "purse-" + purseReason);
                return false;
            }
        }
        if (!CoinCourierPurse.TryRestore(stagedPurse, out CoinCourierPurse purse, out CoinCourierReason restoreReason))
        {
            Close(bound, "purse-" + restoreReason);
            return false;
        }
        binding = new CampaignBinding
        {
            Owner = bound,
            CampaignPtr = campaign.Pointer,
            Slot = slot,
            Guid = guid,
            LiveOwned = stagedOwned,
            StagedOwned = stagedOwned,
            LivePurse = purse,
            StagedPurse = stagedPurse,
        };
        return true;
    }

    private static void Close(GlobalBinding bound, string reason)
    {
        if (bound == null || bound.Closed) return;
        bound.Closed = true;
        bound.CloseReason = reason;
        NoteOnce("closed:" + reason, "courier persistence closed for this global: " + reason, true);
        try
        {
            ICoinCourierCampaignState state = CoinCourierRuntime.State;
            if (state is CampaignBinding binding && ReferenceEquals(binding.Owner, bound)) CoinCourierRuntime.Unbind(state);
        }
        catch (Exception)
        {
        }
    }

    private static void BindRuntimeForCurrent()
    {
        CampaignBinding current = ResolveCurrentBinding();
        if (current == null) return;
        if (!ReferenceEquals(CoinCourierRuntime.State, current)) CoinCourierRuntime.Bind(current);
    }

    private static CampaignBinding ResolveCurrentBinding()
    {
        GlobalBinding bound = _bound;
        if (bound == null || bound.Closed) return null;
        GlobalSaveData loaded = GlobalSaveData._loaded;
        if (loaded == null || loaded.Pointer != bound.GlobalPtr || loaded.currentChallenge != 0) return null;
        CampaignSaveData current = SafeCurrentCampaign();
        if (current == null) return null;
        if (!bound.ByCampaign.TryGetValue(current.Pointer, out CampaignBinding binding)) return null;
        return binding.Detached ? null : binding;
    }

    // ============================================================ 捕获与 staged 落键

    private static void CompleteCapture(SaveScope scope)
    {
        if (!scope.Live || scope.Binding == null) return;
        GlobalBinding bound = _bound;
        if (bound == null || bound.Closed) return;
        if (SafeOnline()) return; // 联机不写本功能 state；不视为故障
        // 本 campaign 的捕获故障不早退：它自己的下一次岛捕获是唯一重试点（完整成功链才清除；失败继续保留旧 staged）。
        CampaignBinding binding = scope.Binding;
        if (!TryVerifyScopeContext(scope, bound, binding, out string reason))
        {
            if (ReferenceEquals(binding.Owner, bound)) FreezeStage(binding, "capture-" + reason);
            else NoteOnce("capture-unattributed:" + reason, "island capture could not be attributed (" + reason + ")", true);
            return;
        }
        if (!scope.MarkerSeen)
        {
            FreezeStage(binding, "capture-no-marker");
            return;
        }

        bool previousOwned = binding.StagedOwned;
        CoinCourierPurseSnapshot previousPurse = binding.StagedPurse;
        binding.StagedOwned = binding.LiveOwned;
        binding.StagedPurse = binding.LivePurse.Capture();
        if (!RewriteStaged(bound, "capture", out string writeReason))
        {
            binding.StagedOwned = previousOwned;
            binding.StagedPurse = previousPurse;
            // 写失败 latch 到本次受影响的 campaign（复用既有 StageFault）；只允许它自己新的
            // 完整成功捕获链解除，其他 campaign 成功/Prepare 重排都不解除。
            FreezeStage(binding, "capture-write:" + writeReason);
            return;
        }
        if (binding.StageFault != null)
        {
            binding.StageFault = null;
            NoteOnce("stage-recovered:" + binding.Guid, "courier capture recovered after a previous stage failure", false);
        }
        NoteOnce("capture:" + binding.Guid + ":" + scope.Land,
            "courier purse staged guid=" + binding.Guid + " land=" + scope.Land
            + " owned=" + binding.StagedOwned + " coins=" + binding.StagedPurse.Coins, false);
    }

    private static bool TryVerifyScopeContext(SaveScope scope, GlobalBinding bound, CampaignBinding binding, out string reason)
    {
        reason = null;
        GlobalSaveData loaded = GlobalSaveData._loaded;
        if (loaded == null || loaded.Pointer != scope.GlobalPtr || loaded.Pointer != bound.GlobalPtr)
        {
            reason = "global";
            return false;
        }
        if (loaded.currentCampaign != scope.Campaign || loaded.currentChallenge != scope.Challenge)
        {
            reason = "campaign";
            return false;
        }
        CampaignSaveData current = SafeCurrentCampaign();
        if (current == null || current.Pointer != scope.CampaignDataPtr)
        {
            reason = "campaigndata";
            return false;
        }
        if (binding.Detached
            || !bound.ByCampaign.TryGetValue(binding.CampaignPtr, out CampaignBinding mapped)
            || !ReferenceEquals(mapped, binding))
        {
            reason = "binding";
            return false;
        }
        return SceneMatches(scope, out reason);
    }

    /// <summary>冻结当前准确 Game/World/gameLayer 与现场岛（Game.currentLand）；任一不可读返回 false。</summary>
    private static bool TryCaptureScene(SaveScope scope)
    {
        try
        {
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            World world = managers != null ? managers.world : null;
            Transform layer = world != null ? world.gameLayer : null;
            if (game == null || world == null || layer == null) return false;
            scope.GamePtr = game.Pointer;
            scope.WorldPtr = world.Pointer;
            scope.LayerPtr = layer.Pointer;
            scope.SceneLand = game.currentLand;
            return scope.GamePtr != IntPtr.Zero && scope.WorldPtr != IntPtr.Zero && scope.LayerPtr != IntPtr.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>完成时必须仍是同一 Game/World/gameLayer 同一现场岛（Game.currentLand 未变）。</summary>
    private static bool SceneMatches(SaveScope scope, out string reason)
    {
        reason = null;
        try
        {
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            World world = managers != null ? managers.world : null;
            Transform layer = world != null ? world.gameLayer : null;
            if (game == null || game.Pointer != scope.GamePtr)
            {
                reason = "game";
                return false;
            }
            if (world == null || world.Pointer != scope.WorldPtr)
            {
                reason = "world";
                return false;
            }
            if (layer == null || layer.Pointer != scope.LayerPtr)
            {
                reason = "layer";
                return false;
            }
            if (game.currentLand != scope.SceneLand || scope.SceneLand != scope.EffectiveLand)
            {
                reason = "land";
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            reason = "scene-read";
            return false;
        }
    }

    private static void FreezeStage(CampaignBinding binding, string reason)
    {
        if (binding == null || binding.StageFault != null) return;
        binding.StageFault = reason;
        NoteOnce("stage-fault:" + binding.Guid + ":" + reason,
            "courier capture failed guid=" + binding.Guid + " (" + reason + "); old staged kept, economy frozen", true);
    }

    /// <summary>
    /// 只按当前原生 campaigns 列表里的幸存绑定重排/生成 staged 文档并写回键。
    /// 绝不采样 live；失败只上报 reason（旧字符串保留）——捕获链由调用方 latch 到该
    /// CampaignBinding.StageFault，Prepare 链由调用方记 PrepareWriteFault。
    /// </summary>
    private static bool RewriteStaged(GlobalBinding bound, string context, out string failReason)
    {
        failReason = null;
        GlobalSaveData loaded = GlobalSaveData._loaded;
        if (loaded == null || loaded.Pointer != bound.GlobalPtr)
        {
            failReason = context + ":global";
            return false;
        }
        var campaigns = loaded.campaigns;
        CoinCourierDocument document = CoinCourierSaveCodec.CreateDocument();
        int count;
        try { count = campaigns == null ? 0 : campaigns.Count; } catch (Exception) { failReason = context + ":campaigns"; return false; }
        for (int i = 0; i < count; i++)
        {
            CampaignSaveData campaign;
            try { campaign = campaigns[i]; } catch (Exception) { continue; }
            if (campaign == null || campaign.Pointer == IntPtr.Zero) continue;
            if (!bound.ByCampaign.TryGetValue(campaign.Pointer, out CampaignBinding binding)) continue;
            if (binding.Detached || !binding.HasStagedState) continue;
            binding.Slot = i;
            document.Campaigns.Add(binding.ToRecord(i));
        }
        if (document.Campaigns.Count == 0 && !bound.KeyEverPresent) return true; // 无可保存内容：绝不凭空创建键
        if (document.Campaigns.Count > CoinCourierSaveSchema.MaxCampaigns)
        {
            failReason = context + ":count";
            return false;
        }
        if (!CoinCourierSaveCodec.TrySerialize(document, out string json, out string serializeReason))
        {
            failReason = context + ":" + serializeReason;
            return false;
        }
        try
        {
            PrefsSaveData prefs = loaded.prefs;
            if (prefs == null || prefs.Pointer != bound.PrefsPtr)
            {
                failReason = context + ":prefs";
                return false;
            }
            prefs.SetString(CoinCourierSaveSchema.Key, json);
            bound.KeyEverPresent = true;
        }
        catch (Exception e)
        {
            failReason = context + ":set-" + e.GetType().Name;
            return false;
        }
        if (bound.PrepareWriteFault != null)
        {
            bound.PrepareWriteFault = null;
            NoteOnce("prepare-write-recovered", "courier persistence prepare rewrite recovered", false);
        }
        return true;
    }

    private static void SetPrepareWriteFault(GlobalBinding bound, string reason)
    {
        if (bound.PrepareWriteFault != null) return;
        bound.PrepareWriteFault = reason;
        NoteOnce("prepare-write-fault:" + reason,
            "courier prepare rewrite failed (" + reason + "); old staged kept, economy frozen", true);
    }

    // ============================================================ Ready / 招募

    /// <summary>
    /// 单一详细就绪评估：<see cref="CampaignBinding"/> 的 Ready（bool）与全部状态文案共用
    /// 这一结果；读取异常不抛（ReadFault）。这是"是否可交易"的唯一判定源，调用方不得
    /// 再造第二套游戏条件。顺序：先核对当前身份（bound/loaded/campaign/slot），身份成立后
    /// 才暴露本绑定的捕获/重写故障——换档瞬间绝不显示旧档的故障或余额。顺手保留原有的
    /// 有界诊断（一次一条）。
    /// </summary>
    private static CoinCourierAvailabilityInfo Evaluate(CampaignBinding binding)
    {
        try
        {
            GlobalBinding bound = _bound;
            if (bound == null || bound.Closed || binding.Detached || !ReferenceEquals(binding.Owner, bound))
                return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NotBound);
            GlobalSaveData loaded = GlobalSaveData._loaded;
            if (loaded == null || loaded.Pointer != bound.GlobalPtr)
                return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NotBound);
            if (loaded.currentChallenge != 0)
                return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Unsupported);
            CampaignSaveData current = SafeCurrentCampaign();
            if (current == null || current.Pointer != binding.CampaignPtr)
                return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NotBound);
            if (binding.Slot < 0 || binding.Slot != loaded.currentCampaign)
                return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NotBound);
            if (bound.PrepareWriteFault != null)
            {
                NoteOnce("ready:prepare-write-fault",
                    "courier economy frozen: prepare rewrite failed (" + bound.PrepareWriteFault + ")", true);
                return new CoinCourierAvailabilityInfo(CoinCourierAvailability.PersistenceFault, bound.PrepareWriteFault);
            }
            if (binding.StageFault != null)
            {
                NoteOnce("ready:stage-fault:" + binding.Guid,
                    "courier economy frozen: capture failed (" + binding.StageFault + ")", true);
                return new CoinCourierAvailabilityInfo(CoinCourierAvailability.PersistenceFault, binding.StageFault);
            }
            CoinCourierAvailabilityInfo gates = EvaluateGates();
            if (gates.Kind != CoinCourierAvailability.Ready)
            {
                NoteOnce("ready:gate:" + gates.Kind, "courier economy not ready: " + gates.Kind
                    + (gates.Detail == null ? "" : " (" + gates.Detail + ")"), false);
            }
            return gates;
        }
        catch (Exception)
        {
            return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.ReadFault);
        }
    }

    /// <summary>
    /// 交易门评估（顺序与原 TryReadGates 一致）；如实区分菜单暂停、非 Playing 加载、
    /// Playing 时标暂停、保存中、联机不可用、无权限与世界未就绪。
    /// </summary>
    private static CoinCourierAvailabilityInfo EvaluateGates()
    {
        try
        {
            if (NetworkBigBoss.IsOnline) return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Online);
            if (!NetworkBigBoss.HasWorldAuth) return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NoAuthority);
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            if (game == null || managers.world == null || managers.world.gameLayer == null)
                return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.WorldUnavailable);
            if (game.state != Game.State.Playing)
                return CoinCourierAvailabilityInfo.Of(game.state == Game.State.Menu
                    ? CoinCourierAvailability.Menu
                    : CoinCourierAvailability.Loading);
            if (Time.timeScale <= 0f) return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Paused);
            if (IslandSaveData.isSavingGame)
                return new CoinCourierAvailabilityInfo(CoinCourierAvailability.Saving, "island-saving");
            if (!Game.SavingEnabled)
                return new CoinCourierAvailabilityInfo(CoinCourierAvailability.Saving, "saving-disabled");
            if (!TryHagletIdle(game._saveGameWithFailurePromptRoutine, "prompt-routine", out string routineReason))
                return new CoinCourierAvailabilityInfo(CoinCourierAvailability.Saving, routineReason);
            if (!TryHagletIdle(game.saveGameWithFailurePrompt, "prompt-save", out string promptReason))
                return new CoinCourierAvailabilityInfo(CoinCourierAvailability.Saving, promptReason);
            return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);
        }
        catch (Exception)
        {
            return CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.ReadFault);
        }
    }

    private static bool TryHagletIdle(Il2CppObjectBase haglet, string name, out string reason)
    {
        reason = null;
        if (haglet == null)
        {
            reason = name + "-missing";
            return false;
        }
        IHagletCallable callable = haglet.TryCast<IHagletCallable>();
        if (callable == null)
        {
            reason = name + "-cast";
            return false;
        }
        Haglet.State state = callable.state;
        if (state == Haglet.State.Started || state == Haglet.State.Paused)
        {
            reason = name + "-" + state;
            return false;
        }
        return true;
    }

    private static bool RecordRecruitment(CampaignBinding binding)
    {
        try
        {
            GlobalBinding bound = _bound;
            if (bound == null || bound.Closed || !ReferenceEquals(binding.Owner, bound)) return false;
            if (Evaluate(binding).Kind != CoinCourierAvailability.Ready) return false;
            if (binding.LiveOwned) return true; // 已拥有：幂等，不产生第二身份
            binding.LiveOwned = true;           // 只改 live；与玩家钱同一岛捕获时再 stage
            return true;
        }
        catch (Exception e)
        {
            NoteOnce("recruit:" + e.GetType().Name, "recruitment record failed: " + e.GetType().Name, true);
            return false;
        }
    }

    // ============================================================ 原生只读小工具

    private static GlobalSaveData SafeLoaded()
    {
        try { return GlobalSaveData._loaded; } catch (Exception) { return null; }
    }

    private static CampaignSaveData SafeCurrentCampaign()
    {
        try { return CampaignSaveData.current; } catch (Exception) { return null; }
    }

    private static bool SafeOnline()
    {
        try { return NetworkBigBoss.IsOnline; } catch (Exception) { return true; }
    }

    // ============================================================ Harmony 钩子（记账型）

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    internal static class IslandSaveScopePatch
    {
        [HarmonyPrefix]
        private static void Prefix(int __0, int __1, int __2, out SaveScope __state)
        {
            CancelPendingShopTransactions();
            __state = BeginIslandSave(__0, __1, __2);
        }

        /// <summary>
        /// 真实 2.4 的 Save 是同步序列（Tick 观察不到 isSavingGame 的窗口），因此这里用既有
        /// Shop 取消入口在建立 scope 前取消未完成投币（只走原生退款路径；已结算/Completed/
        /// RecruitmentLocked 的交易不动）。独立隔离：异常只留一次有界诊断，绝不阻塞
        /// BeginIslandSave 与原生 Save。
        /// </summary>
        private static void CancelPendingShopTransactions()
        {
            try
            {
                CoinCourierShop.CancelPendingTransactions();
            }
            catch (Exception e)
            {
                NoteOnce("scope-cancel:" + e.GetType().Name,
                    "cancel pending shop transactions failed before save: " + e.GetType().Name, true);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(SaveScope __state)
        {
            EndIslandSave(__state, true);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, SaveScope __state)
        {
            EndIslandSave(__state, false);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.UpdateSavedWithRevisions))]
    internal static class IslandCaptureMarkerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(IslandSaveData __instance)
        {
            ObserveMarker(__instance);
        }
    }

    [HarmonyPatch(typeof(PrefsSaveData), nameof(PrefsSaveData.PrepareBeforeSave))]
    internal static class PrefsPreparePatch
    {
        [HarmonyPrefix]
        private static void Prefix(PrefsSaveData __instance)
        {
            ObservePrepare(__instance);
        }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.CreateNewCampaign))]
    internal static class CampaignCreatePatch
    {
        [HarmonyPrefix]
        private static void Prefix(GlobalSaveData __instance)
        {
            ObserveCampaignMutation(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(GlobalSaveData __instance, CampaignSaveData __result)
        {
            ObserveCampaignCreated(__instance, __result);
        }
    }

    // 实际 2.4 静态证据：TryDeleteCampaignAsync 内联 RemoveAt+Save+SaveAsync，不调用 _TryDeleteCampaign
    // → 保留本 prefix 做绑前快照；协程侧改挂 d__91.MoveNext（见下）。
    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.TryDeleteCampaignAsync))]
    internal static class CampaignDeleteAsyncPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ObserveCampaignMutation(SafeLoaded());
        }
    }

    // 实际 2.4 静态证据：TryDeleteCampaignAsync 内联 RemoveAt+Save+SaveAsync，不调用 _TryDeleteCampaign。
    // 协程 factory 的 4-byte Return<SaveLoadResult> 按值参数在当前 Interop 下被映成 IntPtr 再 value_box，
    // prefix 即使不读参数也会在 native 入口崩（IL_STUB_ReversePInvoke + value_box，两次用户启动 dump 同证）→
    // 改为只挂状态机 MoveNext（bool 无参）的 state==0 记账；绝不触碰 factory 与 Return<T>。
    [HarmonyPatch(typeof(GlobalSaveData.__TryDeleteCampaign_d__91),
        nameof(GlobalSaveData.__TryDeleteCampaign_d__91.MoveNext))]
    internal static class CampaignDeleteRoutinePatch
    {
        [HarmonyPrefix]
        private static void Prefix(GlobalSaveData.__TryDeleteCampaign_d__91 __instance)
        {
            ObserveDeleteRoutineState(__instance);
        }
    }
}
