// 宫廷本地日历 + 可信返城单次防衰减：引擎翻译层 + Harmony 接线。
//
// 本文件引用 Unity/BepInEx/Harmony，不参与纯 source-link 测试；纯决策全部在 Patch_PalaceClockReceipt.cs。
//
// Hook 目标（已核 2.4.0 ARM64 token/VA/SHA，97 模块 158758 指针表唯一；本文件不声称 runtime ABI 已验）：
// - Director.get_TotalDaysInReign   token 06001227  611660–611768 /264B  4069ffe87ed7387c9662d7fb7d2bd5c11fb3bc1f7a0afcd7cb43e5da88cde3a5
// - Director.set_TotalDaysInReign   token 06001228  611768–611874 /268B  67520a8a5502f5323a2294b8652360e040509132a6b1e5f355cc406c70cb506f
//   （Update 617ea4 读 getter、617eb0 调 setter；setter 尾部 61186c tail-call campaign 日计数 setter 86cc4c，
//   本 slice 不 detour 该 12B 短方法）
// - Director.Persistent_IBehaviour_ApplyData token 06001283 619548–6199a8 /1120B
//   2dd614ae39cab40f97f8efe689fec229ba8190e79ce4f6d47e46d48bbecc0f5c（6197d8 起写回 clock）
// - IslandSaveData.TryPopObjectsToScene token 06003a70 88e024–88f310 /4844B
//   70aed3498b04d62f5a4a92d73218392eee6d39c67cf1bc25da0a666fad7c5b94（88e464 读 lastPlayedTimeDays、
//   88e484 读 campaign.playTimeDays）
// 原生 RetrieveData（06001282，6194e4 调 getter）会把 localday 沿原生 DirectorData 保存；不需要自造保存器。
//
// 职责边界（worker 合同）：
// 1. 新 fee 的昼间门：只提供 Kingdom.isDaytime 的 typed 只读（8B leaf，不 hook），Root 在
//    只在新 ChargeIntent 使用；同 intent Completed、付费 resume / 夜间免费 Return 不加 daygate。
// 2. own12 Director 的 localday：getter 只服务宫廷本地日、setter 吸收宫廷翻日，raw 两数保持出发
//    checkpoint；ApplyData 只从真实宫廷 DirectorData 恢复。foreign Director 原样透传，异常不吞。
// 3. 返程：仅当 Root 在新 Campaign.ApplyToScene 之前已核 exact 新 Game 窗口/campaign._islands[source]
//    指针/来源与 receipt/object 摘要，且在真实 TryPopObjectsToScene prefix `__runOriginal` 仍为 true 时，
//    单次派生 sourceIsd.lastPlayedTimeDays = campaign.playTimeDays 并读回；非 owned/未 arm/其它岛/skip 零写。
//    结果/异常只记在当次调用的 per-call arm 上；不全局免衰减、不回滚 campaign.playTimeDays、
//    不复制 sourceJSON、不每帧推 lastPlayed。

using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace KingdomEnhancedMod;

internal static class PalaceClockLog
{
    private static ManualLogSource _log;
    private const string Marker = "[PALACE-VISIT-CLOCK]";

    internal static void Attach(ManualLogSource log) => _log = log;

    private static ManualLogSource Source => _log ??= Logger.CreateLogSource("KEM.PalaceVisitClock");

    internal static void Info(string message) => Source.LogInfo(Marker + " " + message);
    internal static void Warn(string message) => Source.LogWarning(Marker + " " + message);

    /// <summary>硬失败：保留责任并上报；不做计数器修复，也不声称物理保存已达成。</summary>
    internal static void Fault(string what) => Source.LogError(Marker + " FAULT " + what);
}

/// <summary>ApplyData prefix→finalizer 传递的 per-call 状态（携带 exact epoch policy 实例）。</summary>
internal sealed class PalaceApplyCallState
{
    internal bool Began;
    internal long LeaseEpoch;
    internal PalaceLocalDayPolicy PolicyRef;
    internal PalaceClockPolicyAdapter AdapterRef;
    internal PalaceClockLease LeaseRef;
    internal long Token;
}

/// <summary>
/// 引擎侧 gate：读取 typed 事实 → 驱动纯 adapter → 服务 localday / 吸收翻日 / 单次派生。
/// 每次调用单锁内完成"观察 + 决策 + 动作"，getter/setter 共用同一 snapshot（原子切换）。
/// </summary>
internal static class PalaceVisitClock
{
    private static readonly object Sync = new object();
    private static PalaceClockPolicyAdapter Policy = new PalaceClockPolicyAdapter();
    private static bool _installAttempted;

    /// <summary>真实 patch 登记结果：任一目标 resolve/patch 失败即保持 false（本功能关闭，绝不广域 unpatch/重试）。</summary>
    internal static bool HooksReady { get; private set; }

    internal static string Status { get; private set; } = "未安装";
    internal static string LastFault { get; private set; }

    internal static void AttachLog(ManualLogSource log) => PalaceClockLog.Attach(log);

    /// <summary>Root 在 PatchAll 之后接线一次。只 resolve MethodInfo / 登记 patch；不读任何 Unity 单例。</summary>
    internal static bool InstallOnce(Harmony harmony)
    {
        lock (Sync)
        {
            if (_installAttempted) return HooksReady;
            _installAttempted = true;
            try
            {
                if (harmony == null) { FailInstall("harmony 为 null"); return false; }

                MethodInfo getter = AccessTools.PropertyGetter(typeof(Director), "TotalDaysInReign")
                                    ?? AccessTools.Method(typeof(Director), "get_TotalDaysInReign");
                MethodInfo setter = AccessTools.PropertySetter(typeof(Director), "TotalDaysInReign")
                                    ?? AccessTools.Method(typeof(Director), "set_TotalDaysInReign");
                MethodInfo apply = AccessTools.Method(typeof(Director), "Persistent_IBehaviour_ApplyData",
                    new[] { typeof(Il2CppSystem.Object) });
                MethodInfo pop = AccessTools.Method(typeof(IslandSaveData), "TryPopObjectsToScene", Type.EmptyTypes);
                if (pop == null) pop = AccessTools.Method(typeof(IslandSaveData), "TryPopObjectsToScene");

                string shape = CheckShape(getter, setter, apply, pop);
                if (shape != null) { FailInstall("签名不符合预期: " + shape); return false; }

                var failures = new System.Collections.Generic.List<string>();
                TryPatch(harmony, getter, "getter",
                    new HarmonyMethod(typeof(PalaceVisitClock), nameof(GetterPrefix)), null, null, failures);
                TryPatch(harmony, setter, "setter",
                    new HarmonyMethod(typeof(PalaceVisitClock), nameof(SetterPrefix)), null, null, failures);
                TryPatch(harmony, apply, "apply",
                    new HarmonyMethod(typeof(PalaceVisitClock), nameof(ApplyPrefix)), null,
                    new HarmonyMethod(typeof(PalaceVisitClock), nameof(ApplyFinalizer)), failures);
                TryPatch(harmony, pop, "trypop",
                    new HarmonyMethod(typeof(PalaceVisitClock), nameof(TryPopPrefix)), null,
                    new HarmonyMethod(typeof(PalaceVisitClock), nameof(TryPopFinalizer)), failures);
                if (pop.ReturnType == typeof(bool))
                {
                    // 仅当原生返回 bool 时才注册结果观察；void 时该语义保持"未解"，不伪造。
                    TryPatch(harmony, pop, "trypop-postfix", null,
                        new HarmonyMethod(typeof(PalaceVisitClock), nameof(TryPopPostfixBool)), null, failures);
                }

                if (failures.Count != 0)
                {
                    // partial fail：本功能关闭（HooksReady=false 时所有 prefix 立即放行原生），
                    // 已登记成功的 hook 保留原样，不对其它插件/目标做广域 unpatch，也不重试安装。
                    FailInstall("patch 登记失败: " + string.Join("; ", failures));
                    return false;
                }

                HooksReady = true;
                Status = "已安装：Director get/set/ApplyData + IslandSaveData.TryPop 五段接线待 arm";
                PalaceClockLog.Info(Status + "（MethodInfo 地址与 patch owner 次序仍需 runtime 复核）");
                return true;
            }
            catch (Exception e)
            {
                FailInstall("安装异常: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }
    }

    private static void FailInstall(string why)
    {
        HooksReady = false;
        LastFault = why;
        Status = "安装失败：" + why;
        PalaceClockLog.Fault(why);
    }

    private static string CheckShape(MethodInfo getter, MethodInfo setter, MethodInfo apply, MethodInfo pop)
    {
        if (getter == null) return "get_TotalDaysInReign 未解析";
        if (getter.ReturnType != typeof(int) || getter.GetParameters().Length != 0) return "getter 返回/参数形状";
        if (setter == null) return "set_TotalDaysInReign 未解析";
        if (setter.ReturnType != typeof(void) || setter.GetParameters().Length != 1
            || setter.GetParameters()[0].ParameterType != typeof(int)) return "setter 返回/参数形状";
        if (apply == null) return "Persistent_IBehaviour_ApplyData 未解析";
        if (apply.ReturnType != typeof(void) || apply.GetParameters().Length != 1) return "ApplyData 返回/参数形状";
        if (pop == null) return "TryPopObjectsToScene 未解析";
        if (pop.GetParameters().Length != 0) return "TryPop 参数形状";
        if (pop.ReturnType != typeof(void) && pop.ReturnType != typeof(bool))
            return "TryPop 返回类型（既非 void 也非 bool，未解）";
        return null;
    }

    private static void TryPatch(Harmony harmony, MethodInfo target, string name, HarmonyMethod prefix,
        HarmonyMethod postfix, HarmonyMethod finalizer, System.Collections.Generic.List<string> failures)
    {
        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
        }
        catch (Exception e)
        {
            failures.Add(name + ": " + e.GetType().Name + ": " + e.Message);
        }
    }

    // ---- Root 接线 API ----

    /// <summary>新 fee 昼间门：typed 读取 Kingdom.isDaytime（8B leaf，不 hook）。读不到返回 false（Root 按不可新收费处理）。</summary>
    internal static bool TryReadKingdomDaytime(Kingdom kingdom, out bool isDaytime)
    {
        isDaytime = false;
        try
        {
            if (kingdom == null) return false;
            isDaytime = kingdom.isDaytime;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>当前 Managers.kingdom 的昼间读取；同上失败语义。</summary>
    internal static bool TryReadCurrentKingdomDaytime(out bool isDaytime)
    {
        isDaytime = false;
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            return TryReadKingdomDaytime(kingdom, out isDaytime);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Root 在 Reload 前调用（source checkpoint 已捕获并物理核对，receipt 由已验证 facts 生成）。</summary>
    internal static bool TryArmOutbound(PalaceSourceClockReceipt receipt, string currentTripId, long sourceGamePtr,
        out string detail)
    {
        lock (Sync)
        {
            bool ok = Policy.TryArmOutbound(receipt, currentTripId, sourceGamePtr, out detail);
            Status = ok
                ? "已 arm（出发）：" + receipt.Describe()
                : "arm 拒绝（出发）：" + detail;
            if (ok) PalaceClockLog.Info(Status);
            else PalaceClockLog.Fault(Status);
            return ok;
        }
    }

    /// <summary>冷启（进程启动即宫廷）：Root 读现 trip 绑定的 receipt + 当前 owner scope（含 Start）+ raw 锚后调用。</summary>
    internal static bool TryArmCold(PalaceSourceClockReceipt receipt, string currentTripId,
        in PalaceClockScope currentScope, int rawCampaignTotalDay, int rawReignDay, out string detail)
    {
        lock (Sync)
        {
            bool ok = Policy.TryArmCold(receipt, currentTripId, currentScope, rawCampaignTotalDay, rawReignDay,
                out detail);
            if (ok) SeenPaidTrips.Add(currentTripId);
            Status = ok
                ? "已 arm（冷启）：" + receipt.Describe()
                : "arm 拒绝（冷启）：" + detail;
            if (ok) PalaceClockLog.Info(Status);
            else PalaceClockLog.Fault(Status);
            return ok;
        }
    }

    /// <summary>Root 在新 Campaign.ApplyToScene 之前、核过 exact 新 Game 窗口/campaign._islands[source] 指针/
    /// 来源与 receipt/object 摘要后调用（handoff 边界已核）；同进程已有未决/consumed/fault/retired 的
    /// ReturnArm 时拒绝覆盖。</summary>
    internal static bool TryArmReturn(in PalaceClockReturnFacts facts, out string detail)
    {
        lock (Sync)
        {
            bool ok = Policy.TryArmReturn(facts, out detail);
            Status = ok
                ? "已 arm（返程单次派生）：" + Policy.ReturnArm.Describe()
                : "arm 拒绝（返程）：" + detail;
            if (ok) PalaceClockLog.Info(Status);
            else PalaceClockLog.Fault(Status);
            return ok;
        }
    }

    /// <summary>Root 报告旅程结束/真实 scope 丢失：注销绑定（不清除进程级 trial unknown）。</summary>
    internal static void Retire(string reason)
    {
        lock (Sync)
        {
            Policy.Retire(reason);
            Status = "已注销：" + reason;
            PalaceClockLog.Info(Status);
        }
    }

    internal static bool Armed => Policy.ArmedReceipt != null;

    internal static string StateSummary()
    {
        lock (Sync)
        {
            return "ready=" + HooksReady + " status=" + Status + " " + Policy.StateSummary()
                   + (LastFault != null ? " lastFault=" + LastFault : "");
        }
    }


    // Only the Root-owned bootstrap calls these APIs; no native-success booleans are accepted from callers.
    internal static bool TryConfirmOutboundArmed(PalaceSourceClockReceipt receipt, string tripId, long oldGame, out string reason)
    {
        lock (Sync)
        {
            reason = "outbound-arm-not-exact";
            return HooksReady && !Policy.TrialUnknown && Policy.ArmedKind == PalaceArmedKind.Outbound
                && Policy.ArmedReceipt != null && Policy.ExcludedSourceGamePtr == oldGame && oldGame != 0
                && Policy.ArmedReceipt.SameTripId(tripId)
                && Policy.ArmedReceipt.WriteCanonicalStrict() == receipt.WriteCanonicalStrict();
        }
    }

    internal static bool TryArmReturnOrConfirmExact(in PalaceClockReturnFacts f, out string reason)
    {
        lock (Sync)
        {
            PalaceClockReturnArm a = Policy.ReturnArm;
            if (a == null) return TryArmReturn(f, out reason);
            reason = "existing-return-arm-not-unconsumed-exact";
            return !Policy.TrialUnknown && !a.Consumed && !a.Attempted && a.Attempts == 0 && !a.Fault
                && !a.Retired && !a.NativeResultSeen && string.IsNullOrEmpty(a.Thrown)
                && a.TripId == f.TripId && a.Receipt.WriteCanonicalStrict() == f.Receipt.WriteCanonicalStrict()
                && a.CampaignPtr == f.CampaignPtr && a.OwnerPtr == f.OwnerPtr && a.GlobalPtr == f.GlobalPtr
                && a.GamePtr == f.GamePtr && a.SourceIslandPtr == f.SourceIslandPtr
                && a.RawCampaignTotalDay == f.CurrentRawCampaignTotalDay && a.RawReignDay == f.CurrentRawReignDay
                && a.Receipt.SameSourceObjects(f.CurrentObjectsSha);
        }
    }

    internal static bool TrySeedVerifiedFresh(Game game, IslandSaveData island, out string reason)
    {
        lock (Sync)
        {
            reason = "fresh-facts-pending";
            Managers m = Managers.Inst;
            CampaignSaveData c = CampaignSaveData.current;
            if (!HooksReady || Policy.TrialUnknown || game == null || island == null || c == null
                || m == null || m.game == null || m.game.Pointer != game.Pointer || m.director == null
                || game.currentLand != 12 || c.CurrentLand != 12 || c.CurrentIsland == null
                || c.CurrentIsland.Pointer != island.Pointer || !island.isNew || island.land != 12
                || island.lastPlayedReign > 0 || island.objects == null || island.objects.Count != 0
                || island.savedWithRevisions == null || island.savedWithRevisions.Count != 0) return false;
            PalaceScopeObservation o = Observe(m.director, out _);
            PalaceScopeDecision d = Policy.DecideScope(o);
            if (d.Verdict != PalaceScopeVerdict.Owned || d.Lease == null || d.Lease.StickyUnknown || d.Lease.Suspended)
                return false;
            if (d.Lease.Policy.HasLocal)
            { reason = "fresh-local-already-established"; return d.Lease.Policy.Day1Seeds == 1 && d.Lease.Policy.Hydrations == 0; }
            d.Lease.Policy.AdoptNewIslandDay1();
            reason = "complete-native-fresh-facts-day1";
            return true;
        }
    }

    // Strong references preserve old per-call arm/lease responsibilities after a successful next-trip handoff.
    private static readonly System.Collections.Generic.List<PalaceClockPolicyAdapter> CompletedPolicies = new();
    private static readonly System.Collections.Generic.HashSet<string> SeenPaidTrips = new(System.StringComparer.Ordinal);
    internal static bool TryArmPaidOutbound(PalacePaidHandle paid, Game sourceGame, out string reason)
    {
        lock (Sync)
        {
            reason = "next-visit-evidence";
            var r = paid != null ? paid.SourceClockReceipt : null;
            Managers m = Managers.Inst;
            CampaignSaveData c = CampaignSaveData.current;
            if (!HooksReady || !PalaceClockBootstrap.VerifyOrder(out reason) || Policy.TrialUnknown
                || paid == null || r == null || paid.Stage != PalacePaidStage.DepartureClaimed
                || sourceGame == null || m == null || m.game == null || m.game.Pointer != sourceGame.Pointer
                || sourceGame.state != Game.State.Playing || IslandSaveData.isSavingGame || c == null
                || c.CurrentLand != paid.SourceLand || sourceGame.currentLand != paid.SourceLand
                || !PalaceSaveBindingSource.TryGetSemanticOwner(out string guid, out int slot, out int biome,
                    out int reign, out int start, out reason)
                || !r.Scope.MatchesSemanticOwner(guid, slot, biome, reign, start)
                || r.Scope.SourceLand != paid.SourceLand || !r.SameTripId(paid.TripId.ToString("D"))) return false;
            string next = paid.TripId.ToString("D");
            if (SeenPaidTrips.Contains(next)) { reason = "paid-trip-replay"; return false; }
            PalaceClockPolicyAdapter target = Policy;
            if (Policy.ArmedReceipt != null || Policy.ReturnArm != null || Policy.Lease != null)
            {
                PalaceClockReturnArm old = Policy.ReturnArm;
                if (!IsCompletedReturnForNext(old, next)
                    || old.Receipt.Scope.SourceLand != c.CurrentLand
                    || !old.Receipt.Scope.MatchesSemanticOwner(guid, slot, biome, reign, start)
                    || !PalacePaidResponsibility.TryReadCommittedForNextVisit(paid, old.TripId, out var book, out reason)
                    || book.SourceBytesSha256 == null || book.Current.TripId != paid.TripId
                    || !book.Current.Owner.ContentEquals(paid.Owner)) return false;
                int exact = 0;
                if (book.Current.TripId.ToString("D") == old.TripId && book.Current.Stage == PalacePaidStage.VisitCommitted) exact++;
                foreach (var h in book.History)
                    if (h.TripId.ToString("D") == old.TripId && h.Stage == PalacePaidStage.VisitCommitted
                        && h.Owner.ContentEquals(paid.Owner) && h.SourceLand == old.Receipt.Scope.SourceLand
                        && h.FileProof != null && h.FileProof.RawSaveSha256 == old.Receipt.SourceCheckpointSha) exact++;
                if (exact != 1) { reason = "old-trip-not-unique-committed"; return false; }
                target = new PalaceClockPolicyAdapter(); // Never Retire/re-arm an unknown or same-trip policy.
            }
            if (!target.TryArmOutbound(r, next, sourceGame.Pointer.ToInt64(), out reason)) return false;
            SeenPaidTrips.Add(next);
            if (!ReferenceEquals(target, Policy)) { CompletedPolicies.Add(Policy); Policy = target; }
            return true;
        }
    }

    internal static bool TryConfirmColdHydrated(Game game, PalaceSourceClockReceipt receipt, out string reason)
    {
        lock (Sync)
        {
            reason = "cold-clock-not-hydrated";
            Managers m = Managers.Inst;
            PalaceClockLease lease = Policy.Lease;
            if (!HooksReady || Policy.TrialUnknown || Policy.ArmedKind != PalaceArmedKind.Cold
                || Policy.ArmedReceipt == null || Policy.ArmedReceipt.WriteCanonicalStrict() != receipt.WriteCanonicalStrict()
                || m == null || m.game == null || game == null || m.game.Pointer != game.Pointer
                || game.currentLand != 12 || lease == null || lease.GamePtr != game.Pointer.ToInt64()
                || lease.StickyUnknown || lease.Suspended || !lease.Policy.HasLocal || lease.Policy.CorruptSaved
                || lease.Policy.Hydrations < 1 || lease.Policy.Day1Seeds != 0) return false;
            return lease.BoundTripId == receipt.TripId && lease.BoundScope.Equals(receipt.Scope);
        }
    }

    internal static bool TryConfirmPalaceHealthy(Game game, PalaceSourceClockReceipt receipt, out string reason)
    {
        lock (Sync)
        {
            reason = "palace-clock-not-established";
            Managers m = Managers.Inst;
            PalaceClockLease lease = Policy.Lease;
            return HooksReady && !Policy.TrialUnknown
                && (Policy.ArmedKind == PalaceArmedKind.Outbound || Policy.ArmedKind == PalaceArmedKind.Cold)
                && Policy.ArmedReceipt != null && Policy.ArmedReceipt.WriteCanonicalStrict() == receipt.WriteCanonicalStrict()
                && m != null && m.game != null && game != null && m.game.Pointer == game.Pointer
                && game.currentLand == 12 && lease != null && lease.GamePtr == game.Pointer.ToInt64()
                && !lease.StickyUnknown && !lease.Suspended && lease.Policy.HasLocal && !lease.Policy.CorruptSaved
                && ((lease.Policy.Day1Seeds == 1 && lease.Policy.Hydrations == 0) || lease.Policy.Hydrations >= 1)
                && lease.BoundTripId == receipt.TripId && lease.BoundScope.Equals(receipt.Scope);
        }
    }

    internal static bool TryConfirmReturnApplied(Game game, IslandSaveData island, string tripId,
        PalaceSourceClockReceipt receipt, out string reason)
    {
        lock (Sync)
        {
            reason = "return-native-pop-not-confirmed";
            PalaceClockReturnArm a = Policy.ReturnArm;
            Managers m = Managers.Inst;
            GlobalSaveData g = GlobalSaveData.loaded;
            CampaignSaveData c = CampaignSaveData.current;
            return HooksReady && !Policy.TrialUnknown && a != null && !a.Retired && a.Consumed && a.Attempted
                && a.Attempts == 1 && a.ReadbackOk && a.NativeResultSeen && a.NativeResultValue && !a.NativeResultFalse
                && !a.Fault && string.IsNullOrEmpty(a.Thrown) && a.TripId == tripId
                && a.Receipt.WriteCanonicalStrict() == receipt.WriteCanonicalStrict()
                && m != null && m.game != null && game != null && m.game.Pointer == game.Pointer
                && g != null && c != null && g.GetCurrentCampaign() != null && island != null
                && a.GamePtr == game.Pointer.ToInt64() && a.GlobalPtr == g.Pointer.ToInt64()
                && a.CampaignPtr == c.Pointer.ToInt64() && a.OwnerPtr == g.GetCurrentCampaign().Pointer.ToInt64()
                && a.SourceIslandPtr == island.Pointer.ToInt64() && c.CurrentIsland != null
                && c.CurrentIsland.Pointer == island.Pointer && c.CurrentLand == receipt.Scope.SourceLand
                && game.currentLand == receipt.Scope.SourceLand;
        }
    }

    internal static bool IsCompletedReturnForNext(PalaceClockReturnArm old, string nextTrip)
        => old != null && old.TripId != nextTrip && old.Consumed && old.Attempted && old.Attempts == 1
            && old.ReadbackOk && old.NativeResultSeen && old.NativeResultValue && !old.NativeResultFalse
            && !old.Fault && string.IsNullOrEmpty(old.Thrown);

    // Read-only fee gate; the paid runtime separately proves the actual terminal journal at its IO boundary.
    internal static bool CanBeginPaidVisit(out string reason)
    {
        lock (Sync)
        {
            reason = "clock-next-visit-held";
            if (!HooksReady || Policy.TrialUnknown) return false;
            if (Policy.ArmedReceipt == null && Policy.ReturnArm == null && Policy.Lease == null) return true;
            return IsCompletedReturnForNext(Policy.ReturnArm, "");
        }
    }

    // ---- Harmony prefixes ----

    private static bool GetterPrefix(Director __instance, ref int __result)
    {
        PalaceClockBootstrap.TraceNativeBoundary("first-getter", __instance);
        try
        {
            if (!HooksReady) return true;
            lock (Sync)
            {
                if (!HooksReady) return true;
                PalaceScopeObservation o = Observe(__instance, out _);
                PalaceScopeDecision d = Policy.DecideScope(o);
                LogDecision(d);
                if (d.Verdict != PalaceScopeVerdict.Owned || d.Lease == null) return true;
                bool serving = !d.Lease.StickyUnknown && !d.Lease.Suspended;
                if (d.Lease.Policy.DecideGetter(serving) == PalaceLocalDayGetter.ServeLocal)
                {
                    __result = d.Lease.Policy.LocalDays;
                    return false;   // 服务宫廷本地日，跳过原生 global getter
                }
                return true;
            }
        }
        catch (Exception e)
        {
            NoteObserverException("getter", e, 0);
            return true;   // 原生查询放行；不做任何签名
        }
    }

    private static bool SetterPrefix(Director __instance, int value)
    {
        PalaceClockBootstrap.TraceNativeBoundary("first-setter", __instance);
        PalaceScopeDecision d = default;
        try
        {
            if (!HooksReady) return true;
            lock (Sync)
            {
                if (!HooksReady) return true;
                PalaceScopeObservation o = Observe(__instance, out _);
                d = Policy.DecideScope(o);
                LogDecision(d);
                PalaceSetterMode mode = PalaceClockPolicyAdapter.SetterModeFor(d);
                if (mode == PalaceSetterMode.Pass)
                {
                    d.Lease?.Policy.DecideSetter(PalaceSetterMode.Pass, value);
                    return true;
                }

                bool anchorRead = o.RawAnchorReadable;
                int totalGameDays = o.RawCampaignTotalDay;
                int daysSurvived = o.RawReignDay;

                // 管线在本次锚观察之后从 lease 现场状态重推 mode：本次漂移强制 AbsorbUnknown，
                // 观察前的 AbsorbOwned 不会采用数值；anchorRead=false 由 DecideSetterWithAnchor 自身
                // RaiseSticky 并返回 AbsorbUnknown（不重复 raise、不采用 value）；任何路径都不把 raw 写回。
                PalaceLocalDaySetter action = Policy.DecideSetterWithAnchor(d, value, anchorRead, totalGameDays,
                    daysSurvived);
                return action == PalaceLocalDaySetter.PassNative;
            }
        }
        catch (Exception e)
        {
            NoteObserverException("setter", e, d.Lease != null ? d.Lease.Epoch : 0);
            // pinned director 绝不对原生写默认放行。
            return !FallbackAbsorbForSameLease(__instance);
        }
    }

    private static bool ApplyPrefix(Director __instance, Il2CppSystem.Object data, out PalaceApplyCallState __state)
    {
        PalaceClockBootstrap.TraceNativeBoundary("first-apply", __instance);
        __state = null;
        try
        {
            if (!HooksReady) return true;
            lock (Sync)
            {
                if (!HooksReady) return true;
                PalaceScopeObservation o = Observe(__instance, out _);
                PalaceApplyFacts f = ObserveApplyFacts(data);
                PalaceClockPolicyAdapter adapter = Policy;
                PalaceApplyDecision ad = adapter.DecideApply(o, f);
                LogDecision(ad.Scope);
                if (!ad.Begin) return true;   // foreign / 未绑 own12：原生不动

                PalaceLocalDayPolicy policy = ad.Scope.Lease.Policy;
                long token = policy.BeginApply();
                __state = new PalaceApplyCallState
                {
                    Began = true,
                    LeaseEpoch = ad.Scope.Lease.Epoch,
                    PolicyRef = policy,
                    AdapterRef = adapter,
                    LeaseRef = ad.Scope.Lease,
                    Token = token,
                };
                ApplyOutcomeLog(adapter, ad, f);
                return true;   // 原生 ApplyData 必须照常执行；其异常由 finalizer 原样返回
            }
        }
        catch (Exception e)
        {
            NoteObserverException("apply-prefix", e, __state != null ? __state.LeaseEpoch : 0);
            MarkStaleOrUnknown(__state, "apply-prefix threw " + e.GetType().Name);
            return true;
        }
    }

    private static Exception ApplyFinalizer(Exception __exception, PalaceApplyCallState __state)
    {
        try
        {
            if (__state != null && __state.Began && __state.PolicyRef != null && __state.AdapterRef != null)
            {
                lock (Sync)
                {
                    string outcome = __state.AdapterRef.FinishApply(__state.Began, __state.PolicyRef, __state.LeaseRef,
                        __state.Token, __exception != null);
                    if (__exception != null)
                    {
                        LastFault = "native ApplyData exception; lease marked unknown (" + outcome + ")";
                        PalaceClockLog.Fault("epoch " + __state.LeaseEpoch + " " + LastFault);
                    }
                }
            }
        }
        catch (Exception)
        {
            // observer 自身异常绝不改写原异常。
        }
        return __exception;   // 同一原异常引用（或 null），绝不吞/替换
    }

    /// <summary>
    /// 真实 source IslandSaveData.TryPopObjectsToScene prefix。Priority.Last：在其它 prefix 之后运行，
    /// `__runOriginal` 反映是否已有 prefix 跳过原方法；false 时零写。
    /// per-call `__state` 只绑定当次调用捕获的 arm/ISD/Game/attempt；finalizer/postfix 不复读全局 ReturnArm。
    /// </summary>
    [HarmonyPriority(Priority.Last)]
    private static void TryPopPrefix(IslandSaveData __instance, bool __runOriginal, out PalaceReturnCallState __state)
    {
        __state = null;
        try
        {
            if (!HooksReady) return;
            lock (Sync)
            {
                if (!HooksReady) return;
                PalaceClockReturnArm arm = Policy.ReturnArm;
                if (arm == null) return;

                long instancePtr = 0;
                try { if (__instance != null) instancePtr = __instance.Pointer.ToInt64(); }
                catch (Exception) { instancePtr = 0; }

                long campaignPtr = 0, ownerPtr = 0, globalPtr = 0, gamePtr = 0;
                int land = -1;
                CampaignSaveData campaign = null;
                try
                {
                    GlobalSaveData loaded = GlobalSaveData.loaded;
                    campaign = CampaignSaveData.current;
                    Managers managers = Managers.Inst;
                    Game game = managers != null ? managers.game : null;
                    CampaignSaveData owner = loaded != null ? loaded.GetCurrentCampaign() : null;
                    if (loaded != null && campaign != null && game != null && owner != null)
                    {
                        globalPtr = loaded.Pointer.ToInt64();
                        campaignPtr = campaign.Pointer.ToInt64();
                        ownerPtr = owner.Pointer.ToInt64();
                        gamePtr = game.Pointer.ToInt64();
                        land = campaign.CurrentLand;
                    }
                    else
                    {
                        campaign = null;
                    }
                }
                catch (Exception)
                {
                    campaign = null;
                }

                var observation = new PalaceReturnObservation(__runOriginal, instancePtr, campaignPtr, ownerPtr,
                    globalPtr, gamePtr, land);
                PalaceReturnDecision decision = Policy.DecideReturn(observation, out string reason);
                if (decision != PalaceReturnDecision.Derive)
                {
                    if (decision != PalaceReturnDecision.NotArmed)
                        PalaceClockLog.Warn("返程派生跳过（零写）：" + decision + " | " + reason);
                    return;
                }

                // 单次派生：写前消费，并把本 call 绑定到 exact arm/ISD/Game/attempt；
                // false/throw/漂移只落在这一个 call 的 arm 上，本窗口不重试。
                arm.BeginWrite();
                __state = new PalaceReturnCallState(arm, instancePtr, gamePtr, arm.Attempts);
                double original = __instance.lastPlayedTimeDays;
                double campaignPlayTime = campaign.playTimeDays;
                __instance.lastPlayedTimeDays = campaignPlayTime;
                double readback = __instance.lastPlayedTimeDays;
                arm.RecordResult(original, campaignPlayTime, readback);
                if (arm.ReadbackOk)
                {
                    PalaceClockLog.Info("返程单次派生完成：" + arm.Describe());
                }
                else
                {
                    LastFault = arm.FaultReason;
                    PalaceClockLog.Fault("返程派生读回不一致：" + arm.Describe());
                }
            }
        }
        catch (Exception e)
        {
            try
            {
                lock (Sync)
                {
                    __state?.NoteThrow(e.GetType().Name);
                }
            }
            catch (Exception) { }
            LastFault = "TryPop 派生 threw " + e.GetType().Name + ": " + e.Message;
            PalaceClockLog.Fault(LastFault + "（责任保留，本窗口不再重试）");
        }
    }

    private static void TryPopFinalizer(Exception __exception, PalaceReturnCallState __state)
    {
        try
        {
            if (__exception == null || __state == null || __state.Arm == null) return;
            lock (Sync)
            {
                __state.NoteThrow(__exception.GetType().Name);
                LastFault = "native TryPopObjectsToScene exception: " + __exception.GetType().Name;
                PalaceClockLog.Fault(LastFault + "（原异常照返；责任保留）");
            }
        }
        catch (Exception)
        {
            // observer 异常绝不改写原异常。
        }
    }

    /// <summary>仅当原生 TryPopObjectsToScene 返回 bool 时注册：false = 保当次 call arm 责任并报（per-call __state，不读全局）。</summary>
    private static void TryPopPostfixBool(IslandSaveData __instance, bool __result, PalaceReturnCallState __state)
    {
        try
        {
            if (__state == null || __state.Arm == null) return;
            lock (Sync)
            {
                __state.NoteNativeResult(__result);
                PalaceClockReturnArm arm = __state.Arm;
                if (!arm.NativeResultFalse) return;
                LastFault = "native TryPop 返回 false（已派生仍保责任）：" + arm.Describe();
                PalaceClockLog.Fault(LastFault);
            }
        }
        catch (Exception)
        {
        }
    }

    // ---- 引擎观察 ----

    /// <summary>构建一次调用的普通快照。绝不抛出；失败 = Readable false。</summary>
    private static PalaceScopeObservation Observe(Director instance, out string fail)
    {
        var o = new PalaceScopeObservation();
        fail = null;
        try
        {
            o.InstancePtr = instance != null ? instance.Pointer.ToInt64() : 0;
            GlobalSaveData loaded = GlobalSaveData.loaded;
            CampaignSaveData campaign = CampaignSaveData.current;
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            if (loaded == null || campaign == null || managers == null || game == null)
            { fail = "missing loaded/campaign/managers/game"; o.Fail = fail; return o; }
            CampaignSaveData owner = loaded.GetCurrentCampaign();
            Director nativeDirector = managers.director;
            if (owner == null || nativeDirector == null)
            { fail = "missing owner/nativeDirector"; o.Fail = fail; return o; }

            o.LoadedPtr = loaded.Pointer.ToInt64();
            o.OwnerPtr = owner.Pointer.ToInt64();
            o.CampaignPtr = campaign.Pointer.ToInt64();
            o.GamePtr = game.Pointer.ToInt64();
            o.DirectorPtr = nativeDirector.Pointer.ToInt64();
            o.CampaignIndex = loaded.currentCampaign;
            o.Reign = campaign.reign;
            o.Biome = campaign.biomeIndex;
            o.CampaignLand = campaign.CurrentLand;
            o.GameLand = game.currentLand;

            // Root 冻结语义 owner：只在 PalaceSaveBindingSource.CurrentMatches 成功时返回；
            // 无门读取成功不得当新 owner 捕获——成功且 slot/biome/reign 与 typed 一致才进 ChecksPass。
            string semanticGuid = null;
            int semanticStart = 0;
            bool semanticOk = false;
            string semanticReason = null;
            try
            {
                if (PalaceSaveBindingSource.TryGetSemanticOwner(out string sGuid, out int sSlot, out int sBiome,
                        out int sReign, out int sStart, out semanticReason))
                {
                    semanticGuid = sGuid;
                    semanticStart = sStart;
                    semanticOk = sGuid != null && sSlot == o.CampaignIndex && sBiome == o.Biome
                                 && sReign == o.Reign;
                }
            }
            catch (Exception)
            {
                semanticOk = false;
            }
            o.SemanticGuid = semanticOk ? semanticGuid : null;
            o.Start = semanticOk ? semanticStart : 0;

            UnityEngine.SceneManagement.Scene directorScene = instance != null ? instance.gameObject.scene : default;
            UnityEngine.SceneManagement.Scene gameScene = game.gameObject.scene;
            o.SceneHandle = directorScene.handle;
            o.SceneName = directorScene.name;

            bool ptrOk = o.LoadedPtr != 0 && o.OwnerPtr != 0 && o.CampaignPtr != 0 && o.GamePtr != 0
                         && o.DirectorPtr != 0 && o.OwnerPtr == o.CampaignPtr;
            o.HasIdentity = ptrOk;
            if (!ptrOk) { fail = "identity pointers incomplete or owner != campaign"; o.Fail = fail; return o; }

            bool challenge = loaded.InChallenge;
            bool sceneOk = directorScene.handle != 0 && directorScene.isLoaded && gameScene.handle != 0
                           && directorScene.handle == gameScene.handle;
            o.ChecksPass = !challenge
                && !NetworkBigBoss.IsOnline
                && NetworkBigBoss.HasWorldAuth
                && BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex
                && o.InstancePtr != 0 && o.InstancePtr == o.DirectorPtr
                && instance.gameObject.activeInHierarchy
                && sceneOk
                && semanticOk;    // 冻结语义 owner 门内读取成功且与 typed 一致才算上下文通过
            if (!o.ChecksPass && fail == null)
            {
                fail = semanticOk
                    ? "checks not pass"
                    : "semantic owner unavailable: " + (semanticReason ?? "-");
            }

            bool rawOk = false;
            try
            {
                CampaignSaveData.ReignInfo reign = campaign.currentReign;
                if (reign != null)
                {
                    o.RawReignDay = reign.daysSurvived;
                    o.RawCampaignTotalDay = campaign.totalGameDays;
                    rawOk = true;
                }
            }
            catch (Exception)
            {
                rawOk = false;
            }
            o.RawAnchorReadable = rawOk;

            o.Readable = true;
            o.Fail = fail;
            return o;
        }
        catch (Exception e)
        {
            fail = "observation threw " + e.GetType().Name + ": " + e.Message;
            o.Readable = false;
            o.Fail = fail;
            return o;
        }
    }

    private static PalaceApplyFacts ObserveApplyFacts(Il2CppSystem.Object data)
    {
        var f = new PalaceApplyFacts();
        try
        {
            if (data == null)
            {
                f.DataKind = PalaceApplyDataKind.Missing;
            }
            else
            {
                DirectorData directorData = data.TryCast<DirectorData>();
                if (directorData == null)
                {
                    f.DataKind = PalaceApplyDataKind.WrongType;
                }
                else
                {
                    f.DataKind = PalaceApplyDataKind.Typed;
                    f.SavedDays = directorData.daysInReign;
                    f.LastReignNum = directorData.lastReignNum;
                }
            }
        }
        catch (Exception)
        {
            f.DataKind = PalaceApplyDataKind.ReadFailed;
        }
        try
        {
            CampaignSaveData campaign = CampaignSaveData.current;
            IslandSaveData island = campaign != null ? campaign.CurrentIsland : null;
            if (island != null)
            {
                f.IslandReadable = true;
                f.IslandLand12 = island.land == 12;
                f.IslandLastPlayedReign = island.lastPlayedReign;
                f.SavedRevisionsCount = island.savedWithRevisions != null ? island.savedWithRevisions.Count : -1;
                f.ObjectCount = island.objects != null ? island.objects.Count : -1;
            }
            else
            {
                f.IslandReadable = false;
            }
            f.IsNewCampaign = campaign != null && campaign.isNew;
            f.FirstGenerationToken = false;   // 本 slice 不暴露 native first-generation token（明确未解）
        }
        catch (Exception)
        {
            f.IslandReadable = false;
        }
        return f;
    }

    private static bool FallbackAbsorbForSameLease(Director instance)
    {
        bool ptrKnown = false;
        long ptr = 0;
        if (instance != null)
        {
            try { ptr = instance.Pointer.ToInt64(); ptrKnown = true; }
            catch (Exception) { ptrKnown = false; }
        }
        return Policy.DecideFallbackAbsorb(ptrKnown, ptr);
    }

    private static void MarkStaleOrUnknown(PalaceApplyCallState state, string why)
    {
        if (state == null || state.LeaseRef == null || state.AdapterRef == null) return;
        lock (Sync)
        {
            if (state.AdapterRef.RaiseSticky(state.LeaseRef, why))
            {
                LastFault = "epoch " + state.LeaseRef.Epoch + " observer failure during apply: " + why;
                PalaceClockLog.Fault(LastFault);
            }
        }
    }

    private static void NoteObserverException(string where, Exception e, long epoch)
    {
        LastFault = "observer exception in " + where + " (epoch " + epoch + "): " + e.GetType().Name + ": "
                    + e.Message;
        PalaceClockLog.Fault(LastFault);
    }

    private static void LogDecision(PalaceScopeDecision d)
    {
        if (d.NewEpoch && d.Lease != null)
        {
            PalaceClockLog.Info("新 epoch " + d.Lease.Epoch + " sceneHandle=" + d.Lease.SceneHandle
                                + " director=" + d.Lease.DirectorPtr.ToString("x") + " trip=" + d.Lease.BoundTripId
                                + " reign=" + d.Lease.Reign + " slot=" + d.Lease.CampaignIndex);
        }
        if (d.StickyFault && d.Lease != null)
        {
            LastFault = "epoch " + d.Lease.Epoch + " sticky unknown: " + d.Reason;
            PalaceClockLog.Fault(LastFault + "（计数保留；setter 拒绝原生写）");
        }
    }

    private static void ApplyOutcomeLog(PalaceClockPolicyAdapter adapter, in PalaceApplyDecision ad, in PalaceApplyFacts f)
    {
        string outcome = adapter.ApplyOutcome(ad, f);
        PalaceClockLease lease = ad.Scope.Lease;
        long epoch = lease != null ? lease.Epoch : -1;
        switch (ad.Action)
        {
            case PalaceApplyAction.Hydrated:
                PalaceClockLog.Info("epoch " + epoch + " " + outcome + " from DirectorData (saved lastReignNum="
                                    + f.LastReignNum + " island=" + f.IslandLastPlayedReign
                                    + "); native restore continues; no native data write");
                break;
            case PalaceApplyAction.HeldUnknown:
                PalaceClockLog.Warn("epoch " + epoch + " " + outcome + "（同 epoch 不做恢复签名）");
                break;
            case PalaceApplyAction.NewIslandDay1:
                PalaceClockLog.Info("epoch " + epoch + " new-island baseline day1 (isNew=" + f.IsNewCampaign
                                    + " revisions=" + f.SavedRevisionsCount + " objects=" + f.ObjectCount
                                    + " islandLastPlayedReign=" + f.IslandLastPlayedReign + ")");
                break;
            case PalaceApplyAction.PendingNoData:
                LastFault = "epoch " + epoch + " pending/unknown DirectorData for land-12; no fake day1";
                PalaceClockLog.Fault(LastFault);
                break;
            case PalaceApplyAction.RejectedCorrupt:
                LastFault = "epoch " + epoch + " corrupt DirectorData.daysInReign=" + f.SavedDays + " rejected";
                PalaceClockLog.Fault(LastFault + "（不伪造 1）");
                break;
            case PalaceApplyAction.RejectedMismatch:
                LastFault = "epoch " + epoch + " hydrate evidence mismatch: " + ad.Reason;
                PalaceClockLog.Fault(LastFault);
                break;
        }
    }
}
