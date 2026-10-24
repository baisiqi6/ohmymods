// PalaceInstallationManifest.cs — 付前“真实安装闭包”只读核验（68 个具体 patch 登记项）。
//
// 依据 #101 生产激活合同 §3 与 exact-gate A–F（B 的有界核验）：
// - 原登记表 42 条 + 时钟/付费/共享捕获 24 条 + 返回保存 2 条 = 68 项。数量只作交叉核对；逐项身份才是门：
//   精确 target（resolver 镜像各 InstallOnce 的 AccessTools 调用冻结，不从名字猜重载/不按表补造）
//   + patch MethodInfo（MethodHandle 比对）+ P/X/F 类别 + owner（插件 Harmony id；NativeBackground
//   独立后缀 ".PalaceNativeBackground"）+ 必要 priority（GetBlocks Last / TryPop First /
//   UpdatePayState Before First+1）+ 恰 1 个（重复=拒）。解析不到、重复、查询异常、缺宿主、
//   NativeBackground _hooksReady=false 均拒绝；不以 _installed/attempted/installed 日志代替。
// - 按唯一 target 只读一次 Harmony.GetPatchInfo，同 target 多项共享该次查询。
// - epoch 缓存（B）：普通 Select/CanPay 读缓存 + 廉价宿主复核；完整 68 项反射扫描只在
//   epoch 首次、以及 forceVerify=true（Plugin.PatchAll 后首次 / 首币 press 前 / Completed 提交前）
//   执行。epoch 查询失败 → cached false；forceVerify 重扫可恢复瞬时查询异常。成功登记结果不永久缓存。
// - TowerSpots/WallSpots 的 AllowManagedSpotSchedule 是现有直接接线，不伪列 Harmony 项目；
//   本 manifest 不重复 PatchAll、不再次注册任何类型。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Coatsink.Common;

namespace KingdomEnhancedMod;

internal static class PalaceInstallationManifest
{
    /// <summary>合同核对值：68（原 42 + 时钟/付费/共享捕获 24 + 返回保存 2）。</summary>
    internal const int ExpectedEntries = 68;

    private enum Category : byte { Prefix, Postfix, Finalizer }

    private readonly struct ExpectedPatch
    {
        internal readonly string Group;
        internal readonly Type TargetType;
        internal readonly string TargetMethod;
        internal readonly Type[] TargetArguments; // null = 安装器同名解析（镜像）
        internal readonly Type PatchType;
        internal readonly string PatchName;
        internal readonly Category PatchCategory;
        internal readonly string OwnerSuffix;     // null = 插件主 Harmony owner
        internal readonly int? ExpectedPriority; // null = 不断言（安装器未指定）

        internal ExpectedPatch(string group, Type targetType, string targetMethod, Type[] targetArguments,
            Type patchType, string patchName, Category category, string ownerSuffix, int? priority)
        {
            Group = group;
            TargetType = targetType;
            TargetMethod = targetMethod;
            TargetArguments = targetArguments;
            PatchType = patchType;
            PatchName = patchName;
            PatchCategory = category;
            OwnerSuffix = ownerSuffix;
            ExpectedPriority = priority;
        }

        internal string Label => Group + ":" + TargetType.Name + "." + TargetMethod
            + (PatchCategory == Category.Prefix ? ":P" : PatchCategory == Category.Postfix ? ":X" : ":F")
            + ":" + PatchType.Name + "." + PatchName;
    }

    // ---- 冻结登记表（与各 InstallOnce / PatchAll attribute 安装点一一对应） ----
    private static readonly ExpectedPatch[] Entries = BuildEntries();

    private static void E(List<ExpectedPatch> list, string group, Type targetType, string targetMethod,
        Type[] targetArguments, Type patchType, string patchName, Category category,
        string ownerSuffix = null, int? priority = null)
        => list.Add(new ExpectedPatch(group, targetType, targetMethod, targetArguments,
            patchType, patchName, category, ownerSuffix, priority));

    private static ExpectedPatch[] BuildEntries()
    {
        var list = new List<ExpectedPatch>(ExpectedEntries);
        // VisibleEntry（4）：Kingdom.Init/OnLevelLoaded → X WorldReady；Game.Update → X GameplayFrame；
        // Game.PreExitCampaign → X CampaignEnded。
        E(list, "VisibleEntry", typeof(Kingdom), nameof(Kingdom.Init), null, typeof(PalaceVisibleEntry), "WorldReady", Category.Postfix);
        E(list, "VisibleEntry", typeof(Kingdom), nameof(Kingdom.OnLevelLoaded), null, typeof(PalaceVisibleEntry), "WorldReady", Category.Postfix);
        E(list, "VisibleEntry", typeof(Game), nameof(Game.Update), Type.EmptyTypes, typeof(PalaceVisibleEntry), "GameplayFrame", Category.Postfix);
        E(list, "VisibleEntry", typeof(Game), nameof(Game.PreExitCampaign), null, typeof(PalaceVisibleEntry), "CampaignEnded", Category.Postfix);
        // Land12（1）
        E(list, "Land12", typeof(Level), "GenerateCurrentConfig", new[] { typeof(int) },
            typeof(PalaceLand12Runtime), "GenerateCurrentConfigPrefix", Category.Prefix);
        // CourtArea（5）
        E(list, "CourtArea", typeof(Level), "GenerateInternal", new[] { typeof(LevelConfig), typeof(int) },
            typeof(PalaceCourtArea), "GenerateInternalPrefix", Category.Prefix);
        E(list, "CourtArea", typeof(Level), "GenerateInternal", new[] { typeof(LevelConfig), typeof(int) },
            typeof(PalaceCourtArea), "GenerateInternalPostfix", Category.Postfix);
        E(list, "CourtArea", typeof(Level), "GenerateInternal", new[] { typeof(LevelConfig), typeof(int) },
            typeof(PalaceCourtArea), "GenerateInternalFinalizer", Category.Finalizer);
        E(list, "CourtArea", typeof(LevelLayout), "GetBlocks", null,
            typeof(PalaceCourtArea), "LevelLayoutGetBlocksPostfix", Category.Postfix,
            priority: Priority.Last);
        E(list, "CourtArea", typeof(LevelBlock), "CloneInto", new[] { typeof(Level), typeof(float) },
            typeof(PalaceCourtArea), "CloneIntoPostfix", Category.Postfix);
        // IslandContent（1）
        E(list, "IslandContent", typeof(IslandSaveData), "TryPopObjectsToScene", Type.EmptyTypes,
            typeof(PalaceIslandContentPolicy), "TryPopObjectsToScenePrefix",
            Category.Prefix, priority: Priority.First);
        // Travel（2）
        E(list, "Travel", typeof(GlobalSaveData), nameof(GlobalSaveData.SaveAsync),
            new[] { typeof(Il2CppSystem.Action<SaveLoadResult>) },
            typeof(PalaceIslandTravel), "SavePrefix", Category.Prefix);
        E(list, "Travel", typeof(Game), nameof(Game.Update), Type.EmptyTypes,
            typeof(PalaceIslandTravel), "UpdatePostfix", Category.Postfix);
        // Return（1）
        E(list, "Return", typeof(Player), nameof(Player.UpdatePayState),
            new[] { typeof(bool), typeof(bool), typeof(bool) },
            typeof(PalaceReturnGate), "Before", Category.Prefix,
            priority: Priority.First + 1);
        // Arrival（4）
        E(list, "Arrival", typeof(CampaignSaveData), "ApplyToScene", Type.EmptyTypes,
            typeof(PalaceArrivalPlacement), "ScenePrefix", Category.Prefix);
        E(list, "Arrival", typeof(CampaignSaveData), "ApplyToScene", Type.EmptyTypes,
            typeof(PalaceArrivalPlacement), "SceneFinalizer", Category.Finalizer);
        E(list, "Arrival", typeof(CampaignSaveData), "ApplyStartPosition",
            new[] { typeof(Player), typeof(Player), typeof(Boat), typeof(WreckPlaceholder),
                    typeof(bool), typeof(bool), typeof(bool) },
            typeof(PalaceArrivalPlacement), "StartPrefix", Category.Prefix);
        E(list, "Arrival", typeof(CampaignSaveData), "ApplyStartPosition",
            new[] { typeof(Player), typeof(Player), typeof(Boat), typeof(WreckPlaceholder),
                    typeof(bool), typeof(bool), typeof(bool) },
            typeof(PalaceArrivalPlacement), "StartPostfix", Category.Postfix);
        // CourtVisuals（4）
        E(list, "CourtVisuals", typeof(Kingdom), nameof(Kingdom.Init), null, typeof(PalaceCourtVisuals), "WorldReady", Category.Postfix);
        E(list, "CourtVisuals", typeof(Kingdom), nameof(Kingdom.OnLevelLoaded), null, typeof(PalaceCourtVisuals), "WorldReady", Category.Postfix);
        E(list, "CourtVisuals", typeof(Game), nameof(Game.Update), Type.EmptyTypes, typeof(PalaceCourtVisuals), "GameplayFrame", Category.Postfix);
        E(list, "CourtVisuals", typeof(Game), nameof(Game.PreExitCampaign), null, typeof(PalaceCourtVisuals), "CampaignEnded", Category.Postfix);
        // Terrain（7）
        E(list, "Terrain", typeof(IslandSaveData), "TryCreateOrFind", new[] { typeof(IslandSaveData.ObjectData) },
            typeof(PalaceTerrainRuntime), "RestoreCreated", Category.Postfix);
        E(list, "Terrain", typeof(LevelBlock), "CloneObject", new[] { typeof(float), typeof(GameObject), typeof(Transform) },
            typeof(PalaceTerrainRuntime), "Generated", Category.Postfix);
        E(list, "Terrain", typeof(RandomizeSprite), "ApplyData", new[] { typeof(Il2CppSystem.Object) },
            typeof(PalaceTerrainRuntime), "ApplyBefore", Category.Prefix);
        E(list, "Terrain", typeof(RandomizeSprite), "ApplyData", new[] { typeof(Il2CppSystem.Object) },
            typeof(PalaceTerrainRuntime), "ApplyAfter", Category.Postfix);
        E(list, "Terrain", typeof(BiomeSpriteSwapper), "OnEnable", Type.EmptyTypes,
            typeof(PalaceTerrainRuntime), "BiomeEnable", Category.Prefix);
        E(list, "Terrain", typeof(BiomeSpriteSwapper), "SpecificSwap", new[] { typeof(BiomeSwapData) },
            typeof(PalaceTerrainRuntime), "BiomeSpecific", Category.Prefix);
        E(list, "Terrain", typeof(Game), "PreExitCampaign", null,
            typeof(PalaceTerrainRuntime), "Cleanup", Category.Postfix);
        // NativeBackground（8，独立 owner 后缀；resolver 镜像其 Resolve(name, count, typeNames)）
        const string bg = ".PalaceNativeBackground";
        E(list, "NativeBackground", typeof(Kingdom), "SetupBGComponent", null,
            typeof(PalaceNativeBackground), "SetupPrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "UpdateBGIndeces", null,
            typeof(PalaceNativeBackground), "UpdateBGIndecesPrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "SetCampBGActive", new[] { typeof(TechnologyAge) },
            typeof(PalaceNativeBackground), "SetCampBGActivePrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "RecalculateBGSectionAges", new[] { typeof(Side) },
            typeof(PalaceNativeBackground), "RecalculateBGSectionAgesPrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "SetBGBorderPoint", new[] { typeof(Side), typeof(float) },
            typeof(PalaceNativeBackground), "SetBGBorderPointPrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "Init", null,
            typeof(PalaceNativeBackground), "InitPrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "OnDisable", null,
            typeof(PalaceNativeBackground), "OnDisablePrefix", Category.Prefix, bg);
        E(list, "NativeBackground", typeof(Kingdom), "OnDestroy", null,
            typeof(PalaceNativeBackground), "OnDestroyPrefix", Category.Prefix, bg);
        // 共享 config（1，PatchAll attribute）
        E(list, "SharedConfig", typeof(BiomeHolder), "GetConfigFromIndex", new[] { typeof(int) },
            typeof(PatchExtensionIsland_ConfigGet), "Prefix", Category.Prefix);
        // 共享 table（3，PatchAll attribute：prefix/postfix/finalizer 同 owner）
        E(list, "SharedTable", typeof(IslandSaveData), "UpdateFileProps", Type.EmptyTypes,
            typeof(PatchExtensionIsland_FilePropsRefresh), "Prefix", Category.Prefix);
        E(list, "SharedTable", typeof(IslandSaveData), "UpdateFileProps", Type.EmptyTypes,
            typeof(PatchExtensionIsland_FilePropsRefresh), "Postfix", Category.Postfix);
        E(list, "SharedTable", typeof(IslandSaveData), "UpdateFileProps", Type.EmptyTypes,
            typeof(PatchExtensionIsland_FilePropsRefresh), "Finalizer", Category.Finalizer);
        // 共享完成保险（1，PatchAll attribute）
        E(list, "SharedCompletion", typeof(Game), "DefeatGreed", new[] { typeof(bool), typeof(float) },
            typeof(PatchExtensionIsland_DefeatGreed), "Prefix", Category.Prefix);
        E(list,"Travel",typeof(GlobalSaveData),"SaveAsync",new[]{typeof(Il2CppSystem.Action<SaveLoadResult>)},typeof(PalaceIslandTravel),"SavePostfix",Category.Postfix,priority:Priority.Last);
        E(list,"Travel",typeof(GlobalSaveData),"SaveAsync",new[]{typeof(Il2CppSystem.Action<SaveLoadResult>)},typeof(PalaceIslandTravel),"SaveFinalizer",Category.Finalizer,priority:Priority.Last);
        // Clock: exact existing native targets, including bool result observation.
        E(list,"Clock",typeof(Director),"get_TotalDaysInReign",Type.EmptyTypes,typeof(PalaceVisitClock),"GetterPrefix",Category.Prefix);
        E(list,"Clock",typeof(Director),"set_TotalDaysInReign",new[]{typeof(int)},typeof(PalaceVisitClock),"SetterPrefix",Category.Prefix);
        E(list,"Clock",typeof(Director),"Persistent_IBehaviour_ApplyData",new[]{typeof(Il2CppSystem.Object)},typeof(PalaceVisitClock),"ApplyPrefix",Category.Prefix);
        E(list,"Clock",typeof(Director),"Persistent_IBehaviour_ApplyData",new[]{typeof(Il2CppSystem.Object)},typeof(PalaceVisitClock),"ApplyFinalizer",Category.Finalizer);
        E(list,"Clock",typeof(IslandSaveData),"TryPopObjectsToScene",Type.EmptyTypes,typeof(PalaceVisitClock),"TryPopPrefix",Category.Prefix,priority:Priority.Last);
        E(list,"Clock",typeof(IslandSaveData),"TryPopObjectsToScene",Type.EmptyTypes,typeof(PalaceVisitClock),"TryPopPostfixBool",Category.Postfix);
        E(list,"Clock",typeof(IslandSaveData),"TryPopObjectsToScene",Type.EmptyTypes,typeof(PalaceVisitClock),"TryPopFinalizer",Category.Finalizer);
        E(list,"Bootstrap",typeof(IslandSaveData),"TryPopObjectsToScene",Type.EmptyTypes,typeof(PalaceClockBootstrap),"PreparePopPrefix",Category.Prefix,priority:Priority.First);
        E(list,"Bootstrap",typeof(IslandSaveData),"TryPopObjectsToScene",Type.EmptyTypes,typeof(PalaceClockBootstrap),"PreparePopPostfix",Category.Postfix,priority:Priority.Last);
        E(list,"Bootstrap",typeof(IslandSaveData),"TryPopObjectsToScene",Type.EmptyTypes,typeof(PalaceClockBootstrap),"PreparePopFinalizer",Category.Finalizer,priority:Priority.Last);
        Type paid = typeof(PalacePaidHandle.Engine);
        E(list,"PaidInput",typeof(Player),"UpdatePayState",new[]{typeof(bool),typeof(bool),typeof(bool)},paid,"InputPrefix",Category.Prefix,priority:Priority.Last);
        E(list,"PaidInput",typeof(Player),"UpdatePayState",new[]{typeof(bool),typeof(bool),typeof(bool)},paid,"InputPostfix",Category.Postfix,priority:Priority.Last);
        E(list,"PaidInput",typeof(Player),"UpdatePayState",new[]{typeof(bool),typeof(bool),typeof(bool)},paid,"InputFinalizer",Category.Finalizer,priority:Priority.Last);
        E(list,"PaidWallet",typeof(Wallet),"Persistent_IBehaviour_RetrieveData",Type.EmptyTypes,paid,"WalletPrefix",Category.Prefix,priority:Priority.Last);
        E(list,"PaidWallet",typeof(Wallet),"Persistent_IBehaviour_RetrieveData",Type.EmptyTypes,paid,"WalletPostfix",Category.Postfix,priority:Priority.Last);
        E(list,"PaidWallet",typeof(Wallet),"Persistent_IBehaviour_RetrieveData",Type.EmptyTypes,paid,"WalletFinalizer",Category.Finalizer,priority:Priority.Last);
        E(list,"PaidCancel",typeof(Player),"CancelTransaction",Type.EmptyTypes,paid,"CancelPrefix",Category.Prefix,priority:Priority.Last);
        E(list,"PaidCancel",typeof(Player),"CancelTransaction",Type.EmptyTypes,paid,"CancelPostfix",Category.Postfix,priority:Priority.Last);
        E(list,"PaidCancel",typeof(Player),"CancelTransaction",Type.EmptyTypes,paid,"CancelFinalizer",Category.Finalizer,priority:Priority.Last);
        Type scope = typeof(CoinCourierPersistence.IslandSaveScopePatch), marker = typeof(CoinCourierPersistence.IslandCaptureMarkerPatch), prefs = typeof(CoinCourierPersistence.PrefsPreparePatch);
        E(list,"PaidCapture",typeof(IslandSaveData),"Save",new[]{typeof(int),typeof(int),typeof(int)},scope,"Prefix",Category.Prefix);
        E(list,"PaidCapture",typeof(IslandSaveData),"Save",new[]{typeof(int),typeof(int),typeof(int)},scope,"Finalizer",Category.Finalizer);
        E(list,"PaidCapture",typeof(IslandSaveData),"UpdateSavedWithRevisions",Type.EmptyTypes,marker,"Postfix",Category.Postfix);
        E(list,"PaidPrefs",typeof(PrefsSaveData),"PrepareBeforeSave",Type.EmptyTypes,prefs,"Prefix",Category.Prefix);
        E(list,"PaidPrefs",typeof(PrefsSaveData),"PrepareBeforeSave",Type.EmptyTypes,prefs,"Finalizer",Category.Finalizer);
        return list.ToArray();
    }

    /// <summary>登记项总数（测试/日志核对；不能以 count==68 替代逐项核验）。</summary>
    internal static int EntryCount => Entries.Length;

    // ---- epoch 缓存（B）----
    private static readonly object Gate = new object();
    private static bool _epochHeld;          // epoch token 已捕获
    private static ulong _epochGame, _epochAgent;
    private static bool _epochVerified;      // 本 epoch 已做过一次全核
    private static bool _epochResult;
    private static string _epochReason = "安装闭包尚未在本 epoch 核验（等待首次全核或 forceVerify）";

    /// <summary>
    /// 付前只读安装总门。forceVerify=false：epoch 内读缓存 + 廉价宿主复核（普通 Select/CanPay）；
    /// forceVerify=true：强制全量 68 项重扫（Plugin.PatchAll 后首次 / 首币 press 前 / Completed 提交前 /
    /// epoch 查询从瞬时异常恢复后）。epoch 查询失败 → cached false；本函数不抛出。
    /// </summary>
    internal static bool TryRequire(bool forceVerify, out string reason)
    {
        reason = "";
        bool epochOk = TryProbeEpoch(out ulong game, out ulong agent, out string epochReason);
        if (!epochOk)
        {
            // epoch 查询失败：cached 一律 false；forceVerify 时仍尝试全核（重扫可恢复瞬时异常）。
            if (!forceVerify) { reason = "epoch 不可用：" + epochReason; return false; }
            game = 0;
            agent = 0;
        }
        lock (Gate)
        {
            bool sameEpoch = epochOk && _epochHeld && _epochGame == game && _epochAgent == agent;
            if (!forceVerify && sameEpoch && _epochVerified)
            {
                if (!_epochResult) { reason = _epochReason; return false; }
                if (!LiveHosts(out reason)) return false;
                reason = _epochReason;
                return true;
            }
        }
        bool ok = VerifyAll(out string fullReason);
        lock (Gate)
        {
            _epochHeld = epochOk;
            _epochGame = game;
            _epochAgent = agent;
            _epochVerified = true;
            _epochResult = ok;
            _epochReason = ok ? "68 项安装闭包核验通过（epoch 缓存；资金时刻仍会 forceVerify 重扫）" : fullReason;
        }
        reason = ok ? _epochReason : fullReason;
        return ok;
    }

    /// <summary>当前 epoch 的缓存视图与 reason（诊断只读；不触发扫描）。</summary>
    internal static string CachedState
    {
        get
        {
            lock (Gate)
            {
                return (_epochVerified ? "verified:" : "unverified:") + _epochResult + "; epoch=" + _epochReason;
            }
        }
    }

    private static bool TryProbeEpoch(out ulong game, out ulong agent, out string reason)
    {
        game = 0;
        agent = 0;
        reason = "";
        try
        {
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null) { reason = "managers/game 不可用"; return false; }
            PalaceSaveBinding binding = PalaceSaveBindingSource.Current;
            if (binding == null) { reason = "save-binding-not-captured"; return false; }
            game = (ulong)managers.game.Pointer.ToInt64();
            agent = (ulong)binding.AgentPointer.ToInt64();
            return true;
        }
        catch (Exception e) { reason = "epoch-fault:" + e.GetType().Name; return false; }
    }

    /// <summary>廉价存活复核：两个持续宿主（PaymentTick / 目的地创建+ReturnDoor）与 NativeBackground hooks。</summary>
    private static bool LiveHosts(out string reason)
    {
        reason = "";
        if (!PalaceVisibleEntry.HostReady) { reason = "VisibleEntry host 不存活/未启用"; return false; }
        if (!PalaceCourtVisuals.HostReady) { reason = "CourtVisuals host 不存活/未启用"; return false; }
        if (!PalaceNativeBackground.HooksReady) { reason = "NativeBackground _hooksReady=false（8 前缀未全装）"; return false; }
        return true;
    }

    /// <summary>全量核验：68 项逐项身份 + 双宿主 + NativeBackground。任一失败即拒绝（fail-closed）。</summary>
    private static bool VerifyAll(out string reason)
    {
        reason = "";
        try
        {
            if (Entries.Length != ExpectedEntries)
            { reason = "登记表数量异常：" + Entries.Length + " != " + ExpectedEntries; return false; }
            // 按唯一 target 只读一次 GetPatchInfo；同 target 多项共享查询结果。
            var cache = new Dictionary<MethodInfo, Patches>(16);
            foreach (ExpectedPatch entry in Entries)
            {
                if (!VerifyEntry(entry, cache, out reason)) return false;
            }
            if (!LiveHosts(out reason)) return false;
            reason = "";
            return true;
        }
        catch (Exception e)
        {
            reason = "manifest-verify-fault:" + e.GetType().Name;
            return false;
        }
    }

    private static bool VerifyEntry(in ExpectedPatch entry, Dictionary<MethodInfo, Patches> cache, out string reason)
    {
        reason = "";
        MethodInfo target;
        try { target = AccessTools.Method(entry.TargetType, entry.TargetMethod, entry.TargetArguments); }
        catch (Exception e) { reason = "target-resolve-fault:" + entry.Label + ":" + e.GetType().Name; return false; }
        if (target == null) { reason = "target-unresolvable:" + entry.Label; return false; }
        if (!cache.TryGetValue(target, out Patches info))
        {
            try { info = Harmony.GetPatchInfo(target); }
            catch (Exception e) { reason = "patchinfo-query-fault:" + entry.Label + ":" + e.GetType().Name; return false; }
            cache[target] = info;
        }
        IList<Patch> list = entry.PatchCategory switch
        {
            Category.Prefix => info != null ? info.Prefixes : null,
            Category.Postfix => info != null ? info.Postfixes : null,
            Category.Finalizer => info != null ? info.Finalizers : null,
            _ => null,
        };
        if (list == null) { reason = "no-patchinfo:" + entry.Label; return false; }
        MethodInfo expected;
        try { expected = AccessTools.Method(entry.PatchType, entry.PatchName); }
        catch (Exception e) { reason = "patch-method-resolve-fault:" + entry.Label + ":" + e.GetType().Name; return false; }
        if (expected == null) { reason = "patch-method-unresolvable:" + entry.Label; return false; }
        int matches = 0;
        Patch found = null;
        foreach (Patch patch in list)
        {
            if (patch == null || patch.PatchMethod == null) continue;
            if (patch.PatchMethod.MethodHandle == expected.MethodHandle) { matches++; found = patch; }
        }
        if (matches != 1) { reason = "patch-count:" + entry.Label + ":" + matches + "（期望恰 1）"; return false; }
        string owner = entry.OwnerSuffix == null
            ? MyPluginInfo.PLUGIN_GUID
            : MyPluginInfo.PLUGIN_GUID + entry.OwnerSuffix;
        if (!string.Equals(found.owner, owner, StringComparison.Ordinal))
        { reason = "owner:" + entry.Label + ":" + (found.owner ?? "(null)"); return false; }
        if (entry.ExpectedPriority.HasValue && found.priority != entry.ExpectedPriority.Value)
        { reason = "priority:" + entry.Label + ":" + found.priority + " != " + entry.ExpectedPriority.Value; return false; }
        return true;
    }

    /// <summary>逐项描述（日志/测试；不触发核验）。</summary>
    internal static string DescribeEntries()
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < Entries.Length; i++)
        {
            if (i > 0) text.Append(';');
            text.Append(Entries[i].Label);
        }
        return text.ToString();
    }
}
