// Patch_PalaceDestinationSupport.cs — frozen save binding and recipe-bound pre-payment admission.
// New fee callers use the required-recipe overload: RAM/global/target-file/sidecar evidence is prepared
// once, CanPay consumes light cached identity, and freeze revalidates full content before side effects.
// The old overload remains only an explicit historical44 compatibility surface for existing callers/tests.
//
// 依据 #101 生产激活合同 §2/§4/§5 与 exact-gate A–F：
// - PalaceSaveBinding/Source：替换两处 hardRoot（Land12/Travel）与所有 folder 比较。每次进程/owner
//   一份不可变绑定证据：Filer.agent 双读（pointer/实际 DotNetAgent cast/同 pointer）+ 实际 folder
//   （非空、绝对、可规范化、存在的目录，按 OS 规则规范化，绝不用 substring/prefix 代替相等）+
//   BepInEx 实际 Root/ConfigPath + Managers/global/campaign 身份。捕获窗口 = 既有 event-driven
//   Playing prepare 窗口（PalaceVisibleEntryScene.Update 就绪探测调用 NotePlayingPrepare），
//   不在静态初始化或 CanPay 中分配。捕获一次后只沿同绑定复核，绝不重捕漂移的根；
//   Reload token 允许 Game 实例按原合同交接（Game 指针不入绑定）。平台 agent 非 DotNetAgent
//   时清晰拒绝留 unknown，不把 Cloud/其他 Agent 当同协议；不设置 Filer.folder、不用 ConfigPath
//   猜游戏存档目录、不回退旧 P、OHMYMODS_FRESH_OBSERVATION_SAVE_ROOT 不再是业务门。
// - PalaceDestinationSupport/Admission：付前只读目的地分类（Fresh / SupportedSaved /
//   UnsupportedOrUnknown）。Fresh = 确证 owner 且 slot12 在 _islands 范围外或 null 且无冲突/未知
//   的本 campaign 宫廷元数据（文件表 14 槽 readiness 不要求 _islands.Count 已到 14，正常容量 9
//   是合法 fresh 入口）。SupportedSaved = 严格 schema2 sidecar（复用 PalaceCourtArea 同一闭 schema
//   严格解析与 ValidateMetadata；dedicated44u、edges±22、11×4u）与存档端点支持相符 + 两条
//   canonical Beach 端部（prefabPath/localPosition/localScale 实际字段）+ 11×4u Tile 网格；
//   serialized land=0 合法（Native 恢复边界成为 12），禁止把 saved land 必须 12 当付前条件或写回。
//   其它（旧无 Beach、旧 schema、身份不符、损坏/不可读、跨 campaign sidecar 冲突）付前明确拒绝
//   "此存档中的旧天朝地图暂不支持，未收取金币"；不删除 12、不清 sidecar、不重生、不自动迁移。
//   sidecar 磁盘 IO 只在源岛准备窗口（PalaceLand12Runtime.PrepareForEntry 成功路径）执行并缓存；
//   CanPay/TryDescribeDestination 只消费缓存 + 进程内 native 只读。
// - 资源预检：固定 profile 实际消费资源的精确 LogicalName 全集（入口 / R4 三层 / R7 26 层 /
//   R6 48 帧 / R7 actors-only manifest+3 atlas / 地表 2 源 PNG）：名称精确（别名补数不成立）、
//   每文件字节上界、PNG IHDR 尺寸、每个已有字面 SHA256 全量比对、actors manifest 完整契约/帧清单。
//   literals-first：字面值唯一权威在各 loader 自己的表（本文件不新造 hash 表；resource-manifest.json
//   仅含 build 源文件 hash、无资源条目，故不作为资源权威），Root 更新 loader literal 后自动生效。
//   不生成替代素材、零 Unity 对象/解码。IO 只在准备窗口执行一次并缓存；失败付前 refuse。
// 零写：不写存档/配置/sidecar/native 数组；所有失败 fail-closed 且给出 reason。
using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>每次进程/owner 的不可变保存绑定证据（强引用 DotNetAgent；Pointer 仅用于同次实例比较）。
/// filesystem 部分（agent pointer/folder/BepInEx root/config）与语义 owner（guid/slot/biome/reign/start）
/// 捕获后永久冻结；Managers/Global/Campaign 实例属 runtime epoch，只由已验证 Reload token 单次替换。</summary>
internal sealed class PalaceSaveBinding
{
    internal readonly Filer.DotNetAgent Agent;
    internal readonly IntPtr AgentPointer;
    internal readonly string Folder;        // 规范化绝对目录（捕获时存在；普通 getter 不再 stat）
    internal readonly string BepInExRoot;   // 规范化
    internal readonly string ConfigPath;    // 规范化
    internal readonly bool SemanticCaptured; // 捕获窗口是否读到稳定语义 owner
    internal readonly string SemanticGuid;   // 冻结的 durable guid（未捕获 = ""）
    internal readonly int SemanticSlot;      // -1 = 未捕获
    internal readonly int SemanticBiome;     // -1 = 未捕获
    internal readonly int SemanticReign;     // -1 = 未捕获
    internal readonly int SemanticStart;     // -1 = 未捕获（realStartDateTime，int）

    internal PalaceSaveBinding(Filer.DotNetAgent agent, IntPtr agentPointer, string folder,
        string bepinExRoot, string configPath, bool semanticCaptured, string semanticGuid,
        int semanticSlot, int semanticBiome, int semanticReign, int semanticStart)
    {
        Agent = agent;
        AgentPointer = agentPointer;
        Folder = folder;
        BepInExRoot = bepinExRoot;
        ConfigPath = configPath;
        SemanticCaptured = semanticCaptured;
        SemanticGuid = semanticGuid ?? "";
        SemanticSlot = semanticSlot;
        SemanticBiome = semanticBiome;
        SemanticReign = semanticReign;
        SemanticStart = semanticStart;
    }
}

/// <summary>当前 runtime epoch 的 Managers/Global/Campaign/Game 实例（强引用保留供 receipt 复核；
/// 只有 <see cref="PalaceSaveBindingSource.TryAdoptReloadBoundary"/> 才替换，旧 epoch 进 retired 列表）。</summary>
internal sealed class PalaceBindingEpoch
{
    internal readonly Managers Managers;
    internal readonly GlobalSaveData Global;
    internal readonly CampaignSaveData Campaign;
    internal readonly Game Game;
    internal readonly ulong ManagersPointer, GlobalPointer, CampaignPointer, GamePointer;
    internal readonly string TripId;        // 采纳它的 token；"" = 捕获建立（epoch 0）

    internal PalaceBindingEpoch(Managers managers, GlobalSaveData global, CampaignSaveData campaign,
        Game game, string tripId)
    {
        Managers = managers;
        Global = global;
        Campaign = campaign;
        Game = game;
        ManagersPointer = (ulong)managers.Pointer.ToInt64();
        GlobalPointer = (ulong)global.Pointer.ToInt64();
        CampaignPointer = (ulong)campaign.Pointer.ToInt64();
        GamePointer = game != null ? (ulong)game.Pointer.ToInt64() : 0UL;
        TripId = tripId ?? "";
    }

    internal bool MatchesRuntime(ulong managersPointer, ulong globalPointer, ulong campaignPointer)
        => managersPointer == ManagersPointer && globalPointer == GlobalPointer && campaignPointer == CampaignPointer;

    internal bool MatchesAdoptedEpoch(ulong managersPointer, ulong globalPointer, ulong campaignPointer, ulong gamePointer)
        => MatchesRuntime(managersPointer, globalPointer, campaignPointer) && gamePointer == GamePointer;
}

/// <summary>绑定的唯一捕获/复核入口（进程内单绑定；漂移拒绝，不重捕）。</summary>
internal static class PalaceSaveBindingSource
{
    private static readonly object Gate = new object();
    private static PalaceSaveBinding _current;
    private static string _lastCaptureReason = "尚未在 Playing prepare 窗口捕获存档绑定";

    // runtime epoch（捕获建立或 Reload 交接单次替换）；旧 epoch 强引用按上限保留，满拒不清列表换名额。
    private const int MaxRetiredEpochs = 8; // Bound one trip lineage, never all successful visits in a process.
    private static string _retiredLineageTrip = "";
    private static int _retiredInLineage;
    private static PalaceBindingEpoch _epoch;
    private static readonly List<PalaceBindingEpoch> RetiredEpochs = new List<PalaceBindingEpoch>(MaxRetiredEpochs);
    private static string _adoptedLegKey = ""; // 最近一次采纳 token 的 tripId；"" = 尚未采纳

    /// <summary>当前已冻结的绑定（可能为 null；只读）。</summary>
    internal static PalaceSaveBinding Current
    {
        get { lock (Gate) { return _current; } }
    }

    /// <summary>
    /// 既有 Playing prepare 事件窗口（VisibleEntry 就绪探测）调用：未捕获才尝试一次双读捕获
    /// （事件驱动、有界）；已捕获只做复核。不做 CanPay 内分配，不重捕漂移的根。
    /// </summary>
    internal static void NotePlayingPrepare()
    {
        try
        {
            lock (Gate)
            {
                if (_current != null)
                {
                    MatchesCurrentSave(_current, out _);
                    return;
                }
            }
            if (TryCaptureCurrentSave(out PalaceSaveBinding captured, out PalaceBindingEpoch capturedEpoch, out string reason))
            {
                lock (Gate)
                {
                    if (_current == null) { _current = captured; _epoch = capturedEpoch; }
                }
            }
            else
            {
                lock (Gate) { _lastCaptureReason = reason; }
            }
        }
        catch (Exception) { }
    }

    /// <summary>
    /// 双读捕获：第一次读 Filer.agent（非零 pointer、实际 DotNetAgent cast、同 pointer）与实际 folder
    /// （共享纯核心规范化 + 物理存在），记录 BepInEx Root/ConfigPath 与当前 runtime epoch；语义 owner
    /// （saved GUID map + campaign typed 值）在捕获窗口双读稳定才一并冻结。第二次读要求 pointer、类型、
    /// 规范化目录与身份全部未漂移，才冻结绑定并保留 agent/epoch 强引用。
    /// </summary>
    internal static bool TryCaptureCurrentSave(out PalaceSaveBinding binding, out PalaceBindingEpoch epoch, out string reason)
    {
        binding = null;
        epoch = null;
        reason = "";
        try
        {
            var first = Filer.agent as Il2CppObjectBase;
            if (first == null || first.Pointer == IntPtr.Zero) { reason = "filer-agent-unavailable"; return false; }
            var agent1 = first.TryCast<Filer.DotNetAgent>();
            if (agent1 == null || agent1.Pointer != first.Pointer)
            { reason = "filer-agent-unsupported-type（非 DotNetAgent：拒绝并留 unknown）"; return false; }
            if (!TryNormalizeFolder(agent1.folder, out string folder1, out string folderReason))
            { reason = "folder-invalid:" + folderReason; return false; }
            if (!TryNormalizePlainPath(BepInEx.Paths.BepInExRootPath, out string root, out string rootReason))
            { reason = "bepinex-root-invalid:" + rootReason; return false; }
            if (!TryNormalizePlainPath(BepInEx.Paths.ConfigPath, out string config, out string configReason))
            { reason = "config-path-invalid:" + configReason; return false; }
            Managers managers = Managers.Inst;
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = global != null ? global.GetCurrentCampaign() : null;
            CampaignSaveData current = CampaignSaveData.current;
            if (managers == null || global == null || campaign == null || current == null
                || campaign.Pointer != current.Pointer)
            { reason = "global/campaign 身份不可用"; return false; }
            IntPtr managersPtr = managers.Pointer, globalPtr = global.Pointer, campaignPtr = campaign.Pointer;

            var second = Filer.agent as Il2CppObjectBase;
            if (second == null || second.Pointer != first.Pointer)
            { reason = "filer-agent-pointer-drift-between-reads"; return false; }
            var agent2 = second.TryCast<Filer.DotNetAgent>();
            if (agent2 == null || agent2.Pointer != second.Pointer)
            { reason = "filer-agent-type-drift-between-reads"; return false; }
            if (!TryNormalizeFolder(agent2.folder, out string folder2, out string secondFolderReason)
                || !string.Equals(folder1, folder2, StringComparison.Ordinal))
            { reason = "folder-drift-between-reads:" + (folder2 ?? secondFolderReason); return false; }
            Managers freshManagers = Managers.Inst;
            GlobalSaveData freshGlobal = GlobalSaveData.loaded;
            CampaignSaveData freshCampaign = freshGlobal != null ? freshGlobal.GetCurrentCampaign() : null;
            if (freshManagers == null || freshManagers.Pointer != managersPtr
                || freshGlobal == null || freshGlobal.Pointer != globalPtr
                || freshCampaign == null || freshCampaign.Pointer != campaignPtr)
            { reason = "global/campaign/managers 身份在双读间漂移"; return false; }

            bool semanticCaptured = TryCaptureSemanticOwner(global, campaign, out string semanticGuid,
                out int semanticSlot, out int semanticBiome, out int semanticReign, out int semanticStart,
                out string semanticDetail);
            if (!semanticCaptured) { reason = "semantic-not-ready:" + semanticDetail; return false; }
            Managers currentManagers = Managers.Inst;
            epoch = new PalaceBindingEpoch(managers, global, campaign,
                currentManagers != null ? currentManagers.game : null, "");
            if (currentManagers == null || currentManagers.Pointer != managersPtr)
            { reason = "managers-drift-before-freeze"; return false; }
            binding = new PalaceSaveBinding(agent1, first.Pointer, folder1, root, config,
                semanticCaptured, semanticGuid, semanticSlot, semanticBiome, semanticReign, semanticStart);
            reason = semanticCaptured
                ? "captured:" + folder1 + "; " + semanticDetail
                : "captured:" + folder1 + "; semantic:" + semanticDetail;
            return true;
        }
        catch (Exception e) { reason = "capture-fault:" + e.GetType().Name; return false; }
    }


    // Explicit cold palace Pop only; a failed normal CurrentMatches is never recapture authority.
    internal static bool TryCaptureAtColdPalacePop(Game game, IslandSaveData island, out string reason)
    {
        reason = "";
        lock (Gate)
        {
            if (_current != null) return MatchesCurrentSave(_current, out reason);
            Managers m = Managers.Inst;
            CampaignSaveData c = CampaignSaveData.current;
            GlobalSaveData g = GlobalSaveData.loaded;
            if (game == null || island == null || m == null || m.game == null || m.game.Pointer != game.Pointer
                || game.currentLand != 12 || c == null || c.CurrentLand != 12 || c.CurrentIsland == null
                || c.CurrentIsland.Pointer != island.Pointer || g == null || g.GetCurrentCampaign() == null
                || g.GetCurrentCampaign().Pointer != c.Pointer || PalaceLand12Runtime.HasActiveReloadHandoff)
            { reason = "not-explicit-cold-palace-pop"; return false; }
            if (!TryCaptureCurrentSave(out PalaceSaveBinding candidate, out PalaceBindingEpoch epoch, out reason)) return false;
            // Capture's internal double reads must still represent this exact caller and current island.
            Managers again = Managers.Inst;
            if (again == null || again.game == null || again.game.Pointer != game.Pointer
                || epoch.GamePointer != (ulong)game.Pointer.ToInt64() || CampaignSaveData.current == null
                || CampaignSaveData.current.Pointer != c.Pointer || c.CurrentIsland == null
                || c.CurrentIsland.Pointer != island.Pointer || GlobalSaveData.loaded == null
                || GlobalSaveData.loaded.Pointer != g.Pointer)
            { reason = "cold-boundary-drift-before-publish"; return false; }
            _current = candidate; _epoch = epoch;
            return true;
        }
    }

    /// <summary>沿同一绑定复核当前实际 Filer/global/campaign/Managers/BepInEx 路径（漂移=changed，拒绝）。
    /// 普通 getter：只做指针/实例等式与纯规范化字符串比较，零文件 IO（Clock/CanPay 安全）。</summary>
    internal static bool MatchesCurrentSave(PalaceSaveBinding binding, out string reason)
    {
        reason = "";
        if (binding == null) { reason = "binding-null"; return false; }
        PalaceBindingEpoch epoch;
        lock (Gate) { epoch = _epoch; }
        if (epoch == null) { reason = "epoch-missing"; return false; }
        try
        {
            var first = Filer.agent as Il2CppObjectBase;
            if (first == null || first.Pointer == IntPtr.Zero) { reason = "filer-agent-unavailable"; return false; }
            if (first.Pointer != binding.AgentPointer) { reason = "filer-agent-changed"; return false; }
            var agent = first.TryCast<Filer.DotNetAgent>();
            if (agent == null || agent.Pointer != first.Pointer)
            { reason = "filer-agent-unsupported-type"; return false; }
            if (!TryNormalizePlainPath(agent.folder, out string folder, out string folderReason)
                || !string.Equals(folder, binding.Folder, StringComparison.Ordinal))
            { reason = "folder-changed:" + (folder ?? folderReason); return false; }
            Managers managers = Managers.Inst;
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = global != null ? global.GetCurrentCampaign() : null;
            if (managers == null || global == null || campaign == null
                || !epoch.MatchesRuntime((ulong)managers.Pointer.ToInt64(), (ulong)global.Pointer.ToInt64(),
                    (ulong)campaign.Pointer.ToInt64()))
            { reason = "global/campaign/managers-changed"; return false; }
            if (CampaignSaveData.current == null || CampaignSaveData.current.Pointer != campaign.Pointer
                || !binding.SemanticCaptured || global.currentCampaign != binding.SemanticSlot
                || campaign.biomeIndex != binding.SemanticBiome || campaign.reign != binding.SemanticReign
                || campaign.realStartDateTime != binding.SemanticStart)
            { reason = "current-semantic-owner-drift"; return false; }
            if (!CurrentGuidMatches(global, campaign, binding.SemanticGuid, out reason)) return false;
            if (!TryNormalizePlainPath(BepInEx.Paths.BepInExRootPath, out string root, out _)
                || !string.Equals(root, binding.BepInExRoot, StringComparison.Ordinal)
                || !TryNormalizePlainPath(BepInEx.Paths.ConfigPath, out string config, out _)
                || !string.Equals(config, binding.ConfigPath, StringComparison.Ordinal))
            { reason = "bepinex-root/config-path-changed"; return false; }
            return true;
        }
        catch (Exception e) { reason = "match-fault:" + e.GetType().Name; return false; }
    }

    private static IntPtr _guidCacheGlobal, _guidCacheCampaign;
    private static string _guidCacheRaw, _guidCacheGuid, _guidCacheReason;
    private static int _guidCacheSlot;
    private static bool _guidCacheOk;
    private static bool CurrentGuidMatches(GlobalSaveData global, CampaignSaveData campaign, string expected, out string reason)
    {
        reason = "";
        if (global.prefs == null || global.prefs.contents == null
            || !global.prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string raw))
        { reason = "current-guid-map-missing"; return false; }
        int slot = global.currentCampaign;
        lock (Gate)
        {
            if (_guidCacheGlobal != global.Pointer || _guidCacheCampaign != campaign.Pointer
                || _guidCacheSlot != slot || !string.Equals(_guidCacheRaw, raw, StringComparison.Ordinal))
            {
                bool readOk = PalaceLand12Runtime.TryReadSavedCampaignBinding(global, campaign,
                    out string guid, out int readSlot, out string why) && readSlot == slot;
                GlobalSaveData afterGlobal = GlobalSaveData.loaded;
                CampaignSaveData afterCurrent = CampaignSaveData.current;
                CampaignSaveData afterCampaign = afterGlobal != null ? afterGlobal.GetCurrentCampaign() : null;
                if (afterGlobal == null || afterGlobal.Pointer != global.Pointer
                    || afterCurrent == null || afterCurrent.Pointer != campaign.Pointer
                    || afterCampaign == null || afterCampaign.Pointer != campaign.Pointer
                    || afterGlobal.currentCampaign != slot
                    || global.prefs == null || global.prefs.contents == null
                    || !global.prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string again)
                    || !string.Equals(again, raw, StringComparison.Ordinal))
                { reason = "current-guid-map-changed-during-read"; return false; }
                // Publish one complete decode snapshot only after both reads and owner checks.
                _guidCacheGlobal = global.Pointer; _guidCacheCampaign = campaign.Pointer;
                _guidCacheSlot = slot; _guidCacheRaw = raw;
                _guidCacheGuid = guid; _guidCacheReason = why; _guidCacheOk = readOk;
            }
            if (!_guidCacheOk) { reason = "current-guid-map-invalid:" + _guidCacheReason; return false; }
            if (!string.Equals(_guidCacheGuid, expected, StringComparison.Ordinal))
            { reason = "current-guid-drift"; return false; }
            return true;
        }
    }

    /// <summary>未捕获 → 拒绝（reason 指示等待 prepare 窗口）；已捕获 → 沿同绑定复核当前状态。
    /// Clock/CanPay 可安全调用：普通 getter 零文件 IO（指针/实例等式 + 纯规范化字符串比较）。</summary>
    internal static bool CurrentMatches(out string reason)
    {
        PalaceSaveBinding binding;
        string pending;
        lock (Gate)
        {
            binding = _current;
            pending = _lastCaptureReason;
        }
        if (binding == null) { reason = "save-binding-not-captured（等待 Playing prepare 窗口）：" + pending; return false; }
        return MatchesCurrentSave(binding, out reason);
    }

    /// <summary>Travel/TryRead/AdvanceReload/FilerReady/Arrival 的窄 agent 比对：pointer + 规范化 folder 与冻结绑定一致。</summary>
    internal static bool AgentMatchesCurrent(Filer.DotNetAgent agent, out string reason)
    {
        reason = "";
        PalaceSaveBinding binding = Current;
        if (binding == null) { reason = "save-binding-not-captured"; return false; }
        try
        {
            if (agent == null || agent.Pointer == IntPtr.Zero || agent.Pointer != binding.AgentPointer)
            { reason = "agent-pointer-mismatch"; return false; }
            if (!TryNormalizePlainPath(agent.folder, out string folder, out string folderReason)
                || !string.Equals(folder, binding.Folder, StringComparison.Ordinal))
            { reason = "agent-folder-mismatch:" + (folder ?? folderReason); return false; }
            return true;
        }
        catch (Exception e) { reason = "agent-match-fault:" + e.GetType().Name; return false; }
    }

    /// <summary>
    /// folder 规范化 + 物理存在（唯一物理门，只在捕获/prepare/firstpress 明确边界调用）：共享纯核心
    /// <see cref="TryNormalizePlainPath"/> 成功后，才对同一 normalized 串做一次只读 Directory.Exists。
    /// 比较一律 Ordinal 相等；绝不用 substring/prefix 包含代替。
    /// </summary>
    internal static bool TryNormalizeFolder(string raw, out string normalized, out string reason)
    {
        normalized = null;
        if (!TryNormalizePlainPath(raw, out string plain, out reason)) return false;
        if (!Directory.Exists(plain)) { reason = "folder-not-exists:" + plain; return false; }
        normalized = plain;
        return true;
    }

    /// <summary>唯一纯规范化核心（零 IO）：非空、完整绝对路径、GetFullPath、去尾分隔符；不要求存在。
    /// folder 物理门与所有普通 getter 都走这里，捕获串与运行期重规范化串必须同源不可分叉。</summary>
    internal static bool TryNormalizePlainPath(string raw, out string normalized, out string reason)
    {
        normalized = null;
        reason = "";
        if (string.IsNullOrWhiteSpace(raw)) { reason = "path-empty"; return false; }
        if (!Path.IsPathRooted(raw)) { reason = "path-relative:" + raw; return false; }
        try { normalized = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception e) { reason = "path-normalize-fault:" + e.GetType().Name; return false; }
        if (normalized.Length == 0) { reason = "path-root-only"; return false; }
        return true;
    }

    /// <summary>
    /// 语义 owner 单次读取：durable GUID map（<see cref="PalaceLand12Runtime.TryReadSavedCampaignBinding"/>，
    /// 实际 Prefs GUID map，不猜其他 key）+ campaign typed 值（biomeIndex / reign / realStartDateTime，start 为 int）。
    /// </summary>
    private static bool TryReadSemanticOwner(GlobalSaveData global, CampaignSaveData campaign,
        out string guid, out int slot, out int biome, out int reign, out int start, out string reason)
    {
        guid = "";
        slot = -1;
        biome = -1;
        reign = -1;
        start = -1;
        reason = "";
        try
        {
            if (!PalaceLand12Runtime.TryReadSavedCampaignBinding(global, campaign, out guid, out slot, out reason)) return false;
            biome = campaign.biomeIndex;
            reign = campaign.reign;
            start = campaign.realStartDateTime;
            if (biome < 0 || reign < 0)
            {
                reason = "typed-owner:" + biome + "/" + reign;
                guid = ""; slot = -1; biome = -1; reign = -1; start = -1;
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            reason = "semantic-read:" + e.GetType().Name;
            guid = ""; slot = -1; biome = -1; reign = -1; start = -1;
            return false;
        }
    }

    /// <summary>捕获窗口双读语义 owner：两次读取的 guid/slot/biome/reign/start 完全一致才发布（冻结）。</summary>
    private static bool TryCaptureSemanticOwner(GlobalSaveData global, CampaignSaveData campaign,
        out string guid, out int slot, out int biome, out int reign, out int start, out string detail)
    {
        guid = "";
        slot = -1;
        biome = -1;
        reign = -1;
        start = -1;
        detail = "";
        if (!TryReadSemanticOwner(global, campaign, out string guid1, out int slot1, out int biome1,
                out int reign1, out int start1, out string firstDetail))
        { detail = firstDetail; return false; }
        if (!TryReadSemanticOwner(global, campaign, out string guid2, out int slot2, out int biome2,
                out int reign2, out int start2, out string secondDetail))
        { detail = "second-read:" + secondDetail; return false; }
        if (!string.Equals(guid1, guid2, StringComparison.Ordinal) || slot1 != slot2 || biome1 != biome2
            || reign1 != reign2 || start1 != start2)
        { detail = "semantic-drift-between-reads"; return false; }
        guid = guid1;
        slot = slot1;
        biome = biome1;
        reign = reign1;
        start = start1;
        detail = "semantic-owner guid=" + guid + " slot=" + slot + " biome=" + biome
            + " reign=" + reign + " start=" + start;
        return true;
    }

    /// <summary>
    /// 合同 A：冻结语义 owner 的只读出口。仅 <see cref="CurrentMatches"/> 成功才发布捕获窗口冻结并双读稳定的
    /// guid/slot/biome/reign/start（start 为 int）；失败返回安全空值 + reason。绝不重捕、绝不接纳漂移、不读磁盘。
    /// </summary>
    internal static bool TryGetSemanticOwner(out string guid, out int slot,
        out int biome, out int reign, out int start, out string reason)
    {
        guid = "";
        slot = -1;
        biome = -1;
        reign = -1;
        start = -1;
        if (!CurrentMatches(out reason)) return false;
        PalaceSaveBinding binding = Current;
        if (binding == null) { reason = "binding-null"; return false; }
        if (!binding.SemanticCaptured)
        { reason = "semantic-owner-uncaptured（捕获窗口未读到稳定 durable 身份）"; return false; }
        guid = binding.SemanticGuid;
        slot = binding.SemanticSlot;
        biome = binding.SemanticBiome;
        reign = binding.SemanticReign;
        start = binding.SemanticStart;
        reason = "frozen-semantic-owner";
        return true;
    }

    /// <summary>
    /// Root firstpress 窄物理复核（补充 gate 约束 4）：逻辑 CurrentMatches 成功后，对已冻结 normalized folder
    /// 只读一次 Directory.Exists。不捕获、不采纳、不改绑定、不写状态、不做其他 IO。
    /// </summary>
    internal static bool TryVerifyFrozenFolderPhysics(out string reason)
    {
        if (!CurrentMatches(out reason)) return false;
        PalaceSaveBinding binding = Current;
        if (binding == null) { reason = "binding-null"; return false; }
        try
        {
            if (!Directory.Exists(binding.Folder)) { reason = "folder-missing:" + binding.Folder; return false; }
            reason = "folder-present:" + binding.Folder;
            return true;
        }
        catch (Exception e) { reason = "folder-physics-fault:" + e.GetType().Name; return false; }
    }

    /// <summary>冻结 filesystem 绑定复核（零文件 IO）：agent pointer/type/folder 双读 + BepInEx root/config 均
    /// 按规范化字符串 Ordinal 精确相等；目录物理存在不在此判（由明确物理门负责）。</summary>
    private static bool MatchesFrozenFilesystem(PalaceSaveBinding binding, out string reason)
    {
        reason = "";
        try
        {
            var first = Filer.agent as Il2CppObjectBase;
            if (first == null || first.Pointer == IntPtr.Zero) { reason = "filer-agent-unavailable"; return false; }
            if (first.Pointer != binding.AgentPointer) { reason = "filer-agent-changed"; return false; }
            var agent1 = first.TryCast<Filer.DotNetAgent>();
            if (agent1 == null || agent1.Pointer != first.Pointer) { reason = "filer-agent-unsupported-type"; return false; }
            if (!TryNormalizePlainPath(agent1.folder, out string folder1, out string folderReason1)
                || !string.Equals(folder1, binding.Folder, StringComparison.Ordinal))
            { reason = "folder-changed:" + (folder1 ?? folderReason1); return false; }
            var second = Filer.agent as Il2CppObjectBase;
            if (second == null || second.Pointer != first.Pointer) { reason = "filer-agent-drift-between-reads"; return false; }
            var agent2 = second.TryCast<Filer.DotNetAgent>();
            if (agent2 == null || agent2.Pointer != second.Pointer) { reason = "filer-agent-type-drift"; return false; }
            if (!TryNormalizePlainPath(agent2.folder, out string folder2, out string folderReason2)
                || !string.Equals(folder1, folder2, StringComparison.Ordinal))
            { reason = "folder-drift-between-reads:" + (folder2 ?? folderReason2); return false; }
            if (!TryNormalizePlainPath(BepInEx.Paths.BepInExRootPath, out string root, out _)
                || !string.Equals(root, binding.BepInExRoot, StringComparison.Ordinal)
                || !TryNormalizePlainPath(BepInEx.Paths.ConfigPath, out string config, out _)
                || !string.Equals(config, binding.ConfigPath, StringComparison.Ordinal))
            { reason = "bepinex-root/config-path-changed"; return false; }
            return true;
        }
        catch (Exception e) { reason = "filesystem-match-fault:" + e.GetType().Name; return false; }
    }

    /// <summary>
    /// 合同 B：Reload 边界唯一的 runtime epoch 替换点。只消费
    /// <see cref="PalaceLand12Runtime.TryDescribeVerifiedReloadHandoff"/> 的 active token 已验证事实：
    /// 旧 epoch 必须等于当前 binder epoch（不得被先 UpdateBinding 掩盖）、filesystem 绑定双读稳定、
    /// 新 epoch 的 saved GUID map/typed 身份/记录与冻结语义 owner 完全一致，才把 Managers/Global/Campaign
    /// 单次换到新实例（旧 epoch 强引用进 retired 列表；容量 8 满拒，不清列表换名额）。
    /// 同一 token + 同一新 epoch 重复调用是只读幂等成功；同一 token 内第二组实例一律拒绝；
    /// Complete/Abort 后的新 trip 使用新 token 正常继续（不是 session 全局一次 latch）。
    /// 普通 <see cref="CurrentMatches"/> 失败不是采纳许可；本方法零文件 IO。
    /// </summary>
    internal static bool TryAdoptReloadBoundary(Game game, out string reason)
    {
        reason = "";
        PalaceSaveBinding binding;
        PalaceBindingEpoch epoch;
        lock (Gate)
        {
            binding = _current;
            epoch = _epoch;
        }
        if (binding == null) { reason = "save-binding-not-captured"; return false; }
        if (epoch == null) { reason = "epoch-missing"; return false; }
        if (game == null) { reason = "game-null"; return false; }
        try
        {
            // 1) active token 的已验证事实（不造第二 token/ledger；无 token/仍是旧 Game/第二 newGame 都拒）。
            if (!PalaceLand12Runtime.TryDescribeVerifiedReloadHandoff((ulong)game.Pointer.ToInt64(),
                    out PalaceLand12Runtime.PalaceReloadHandoffFacts facts, out string factsReason))
            { reason = "handoff:" + factsReason; return false; }

            string legKey = facts.TripId + ":" + facts.Phase + ":" + facts.TargetLand;
            bool repeated;
            PalaceBindingEpoch sourceEpoch;
            lock (Gate)
            {
                repeated = string.Equals(_adoptedLegKey, legKey, StringComparison.Ordinal);
                sourceEpoch = repeated && RetiredEpochs.Count > 0 ? RetiredEpochs[RetiredEpochs.Count-1] : epoch;
            }
            // Repeated calls still prove the original source epoch and all current facts below.
            if (!sourceEpoch.MatchesRuntime(facts.OldManagers, facts.OldGlobal, facts.OldCampaign))
            { reason = "old-epoch-drift"; return false; }
            if (sourceEpoch.GamePointer != 0UL && facts.OldGame != sourceEpoch.GamePointer)
            { reason = "token-old-game-drift"; return false; }

            // 3) 新 epoch 实例与身份（Loading 阶段不要求 world/HeavyShield live session）。
            Managers managers = Managers.Inst;
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = global != null ? global.GetCurrentCampaign() : null;
            CampaignSaveData current = CampaignSaveData.current;
            if (managers == null || managers.game == null || managers.game.Pointer != game.Pointer
                || global == null || campaign == null || current == null || campaign.Pointer != current.Pointer)
            { reason = "new-epoch-identity"; return false; }
            if (NetworkBigBoss.IsOnline) { reason = "online"; return false; }
            if (global.InChallenge) { reason = "challenge"; return false; }
            ulong managersPointer = (ulong)managers.Pointer.ToInt64();
            ulong globalPointer = (ulong)global.Pointer.ToInt64();
            ulong campaignPointer = (ulong)campaign.Pointer.ToInt64();
            ulong gamePointer = (ulong)game.Pointer.ToInt64();

            if (gamePointer == sourceEpoch.GamePointer) { reason = "no-new-game-instance"; return false; }

            // 5) filesystem 绑定（零 IO 双读）与冻结语义 owner 精确等式。
            if (!MatchesFrozenFilesystem(binding, out string fsReason)) { reason = fsReason; return false; }
            if (!binding.SemanticCaptured) { reason = "semantic-owner-uncaptured"; return false; }
            if (!TryReadSemanticOwner(global, campaign, out string guid, out int slot, out int biome,
                    out int reign, out int start, out string semanticReason))
            { reason = "semantic-read:" + semanticReason; return false; }
            if (!string.Equals(guid, binding.SemanticGuid, StringComparison.Ordinal) || slot != binding.SemanticSlot
                || biome != binding.SemanticBiome || reign != binding.SemanticReign || start != binding.SemanticStart)
            { reason = "semantic-owner-drift"; return false; }
            if (!string.Equals(guid, facts.CampaignGuid, StringComparison.Ordinal) || slot != facts.CampaignSlot
                || biome != facts.Biome || reign != facts.Reign || start != facts.Start)
            { reason = "token-semantic-drift"; return false; }

            // 6) exact trip/phase/记录 source/target 与 campaign 落点（范围沿用既有合同 0..11/12）。
            if (facts.TargetLand < 0 || facts.TargetLand > PalaceLand12Runtime.LandIndex)
            { reason = "target-range:" + facts.TargetLand; return false; }
            if (campaign.CurrentLand != facts.TargetLand)
            { reason = "campaign-not-at-target:" + campaign.CurrentLand + "/" + facts.TargetLand; return false; }
            if (PalaceTripContext.Read(global, out PalaceTripRecord record, out string readDetail)
                != PalaceTripContext.ReadStatus.Ok)
            { reason = "trip-record-unreadable:" + readDetail; return false; }
            if (record.TripId != facts.TripId || record.Phase != facts.Phase
                || record.SourceLand != facts.RecordSourceLand
                || record.CampaignGuid != facts.CampaignGuid || record.CampaignSlot != facts.CampaignSlot)
            { reason = "trip-record-drift"; return false; }
            if (!PalaceTripContext.RecordMatches(campaign, guid, slot, record, out string matchDetail))
            { reason = "record-campaign:" + matchDetail; return false; }

            // 7) 本次调用内双读稳定：filesystem 与实例再读一遍。
            if (!MatchesFrozenFilesystem(binding, out string fsAgain)) { reason = "double-read:" + fsAgain; return false; }
            Managers againManagers = Managers.Inst;
            GlobalSaveData againGlobal = GlobalSaveData.loaded;
            CampaignSaveData againCampaign = againGlobal != null ? againGlobal.GetCurrentCampaign() : null;
            if (againManagers == null || againGlobal == null || againCampaign == null
                || againManagers.game == null || againManagers.game.Pointer != game.Pointer
                || (ulong)againManagers.Pointer.ToInt64() != managersPointer
                || (ulong)againGlobal.Pointer.ToInt64() != globalPointer
                || (ulong)againCampaign.Pointer.ToInt64() != campaignPointer)
            { reason = "instance-drift-between-reads"; return false; }

            // 8) 单次替换（容量满拒；不清 retired 列表换名额）。
            lock (Gate)
            {
                if (!ReferenceEquals(_current, binding) || !ReferenceEquals(_epoch, epoch))
                { reason = "binding-changed-before-adoption"; return false; }
                if (repeated)
                {
                    if (!string.Equals(_adoptedLegKey, legKey, StringComparison.Ordinal)
                        || !epoch.MatchesAdoptedEpoch(managersPointer, globalPointer, campaignPointer, gamePointer))
                    { reason = "second-epoch-within-leg"; return false; }
                    reason = "already-adopted-idempotent:" + legKey;
                    return true;
                }
                int inLineage = string.Equals(_retiredLineageTrip, facts.TripId, StringComparison.Ordinal)
                    ? _retiredInLineage : 0;
                if (inLineage >= MaxRetiredEpochs)
                { reason = "old-epoch-lineage-capacity:" + MaxRetiredEpochs; return false; }
                // The unique verified travel token owns this lineage. Successful new paid trip creation
                // is gated by Clock's committed-old-trip rollover. Keep old native/callback references.
                RetiredEpochs.Add(epoch);
                _retiredLineageTrip = facts.TripId;
                _retiredInLineage = inLineage + 1;
                _epoch = new PalaceBindingEpoch(managers, global, campaign, game, facts.TripId);
                _adoptedLegKey = legKey;
            }
            reason = "adopted:" + facts.TripId + " target=" + facts.TargetLand;
            return true;
        }
        catch (Exception e) { reason = "adopt-fault:" + e.GetType().Name; return false; }
    }
}

/// <summary>付前目的地支持分类（只读；不宣布未来旅程成功）。</summary>
internal enum PalaceDestinationSupport
{
    Fresh,
    SupportedSaved,
    UnsupportedOrUnknown,
}

/// <summary>已保存 slot12 的结构事实（只读取证；plain 字段，供纯逻辑分类）。</summary>
internal sealed class PalaceSavedIsland12Facts
{
    internal bool BeachLeftOk;
    internal bool BeachRightOk;
    internal int BeachObjectCount;
    internal int TileObjectCount;
    internal bool TileGridOk;
}

/// <summary>Admission owns creation/publication; callers can only consume its current cached instance.</summary>
internal sealed class PalaceDestinationDecision
{
    internal PalaceLayoutRecipe Recipe { get; }
    internal PalaceDestinationSupport Support { get; }
    internal readonly PalacePaidScope Owner;
    internal readonly PalaceSaveBinding Binding;
    internal readonly int Epoch,IslandsCount,ObjectsCount,TableCapacity;
    internal readonly IntPtr GamePointer,GlobalPointer,CampaignPointer,IslandsPointer,IslandPointer,ObjectsPointer,TablePointer;
    internal readonly PalaceDestinationFileWitness TargetFile;
    internal readonly PalacePaidSourceProof.NativeDestinationSnapshot GlobalSnapshot;
    internal readonly string RamFingerprint,SidecarFingerprint,GlobalIslandFingerprint;
    internal PalaceDestinationDecision(PalaceLayoutRecipe recipe,PalaceDestinationSupport support,PalacePaidScope owner,
        PalaceSaveBinding binding,int epoch,IntPtr game,IntPtr global,IntPtr campaign,IntPtr islands,int islandsCount,
        IntPtr island,IntPtr objects,int objectsCount,IntPtr table,int capacity,PalaceDestinationFileWitness file,
        PalacePaidSourceProof.NativeDestinationSnapshot snapshot,string ram,string sidecar,string globalIsland)
    { Recipe=recipe;Support=support;Owner=owner;Binding=binding;Epoch=epoch;GamePointer=game;GlobalPointer=global;
      CampaignPointer=campaign;IslandsPointer=islands;IslandsCount=islandsCount;IslandPointer=island;ObjectsPointer=objects;
      ObjectsCount=objectsCount;TablePointer=table;TableCapacity=capacity;TargetFile=file;GlobalSnapshot=snapshot;
      RamFingerprint=ram;SidecarFingerprint=sidecar;GlobalIslandFingerprint=globalIsland; }
    // Cross-time destination identity for prepare -> charge: same owner/epoch and same target witnesses.
    // Whole-global raw bytes are deliberately excluded; a legitimate native autosave may rewrite source
    // progress between arm and charge without touching this destination. Raw equality remains mandatory
    // within one capture's double read (TryCaptureDestination) and against the exact paid source-save
    // proof (TryRefreshAfterPaidSourceSave).
    internal bool SameTarget(PalaceDestinationDecision other)=>TargetDifference(other)==null;
    internal string TargetDifference(PalaceDestinationDecision other)
    {
        if(other==null)return "null";
        if(!ReferenceEquals(Recipe,other.Recipe))return "recipe";
        if(Support!=other.Support)return "support";
        if(!Owner.ContentEquals(other.Owner))return "owner";
        if(!ReferenceEquals(Binding,other.Binding))return "binding";
        if(Epoch!=other.Epoch)return "epoch";
        if(GamePointer!=other.GamePointer)return "game";
        if(GlobalPointer!=other.GlobalPointer)return "global";
        if(CampaignPointer!=other.CampaignPointer)return "campaign";
        if(IslandsPointer!=other.IslandsPointer)return "islands";
        if(IslandsCount!=other.IslandsCount)return "islands-count";
        if(IslandPointer!=other.IslandPointer)return "island";
        if(ObjectsPointer!=other.ObjectsPointer)return "objects";
        if(ObjectsCount!=other.ObjectsCount)return "objects-count";
        if(TablePointer!=other.TablePointer)return "table";
        if(TableCapacity!=other.TableCapacity)return "table-capacity";
        if(TargetFile.FullPath!=other.TargetFile.FullPath)return "target-path";
        if(TargetFile.Kind!=other.TargetFile.Kind)return "target-kind";
        if(RamFingerprint!=other.RamFingerprint)return "ram";
        if(SidecarFingerprint!=other.SidecarFingerprint)return "sidecar";
        if(GlobalSnapshot.SlotKind!=other.GlobalSnapshot.SlotKind)return "global-slot";
        if(GlobalIslandFingerprint!=other.GlobalIslandFingerprint)return "global-island";
        return null;
    }
    internal bool SameDestinationContent(PalaceDestinationDecision other)=>other!=null
        && ReferenceEquals(Recipe,other.Recipe) && Support==other.Support && Owner.ContentEquals(other.Owner)
        && ReferenceEquals(Binding,other.Binding) && Epoch==other.Epoch && GamePointer==other.GamePointer
        && GlobalPointer==other.GlobalPointer && CampaignPointer==other.CampaignPointer
        && TargetFile.FullPath==other.TargetFile.FullPath && TargetFile.Kind==other.TargetFile.Kind
        && RamFingerprint==other.RamFingerprint && SidecarFingerprint==other.SidecarFingerprint
        && GlobalIslandFingerprint==other.GlobalIslandFingerprint
        && (GlobalSnapshot.SlotKind==PalacePaidSourceProof.NativeDestinationSlotKind.Object)
            ==(other.GlobalSnapshot.SlotKind==PalacePaidSourceProof.NativeDestinationSlotKind.Object);
}

/// <summary>目的地分类与固定 profile 资源预检（全部只读；IO 只在准备窗口）。</summary>
internal static class PalaceDestinationAdmission
{
    private const float Tolerance = 0.001f; // 与 PalaceDedicatedOwnedBlock.Tolerance 一致
    internal const string UnsupportedPrefix = "此存档中的旧天朝地图暂不支持，未收取金币";
    private const string BeachPrefabPath = PalaceDedicatedEndpoints.BeachPrefabPath;
    private const float BeachLeftX = PalaceDedicatedEndpoints.OwnedBeachLeftX;    // -14.6875（-22+7.3125）
    private const float BeachRightX = PalaceDedicatedEndpoints.OwnedBeachRightX; // 14.6875（22-7.3125）
    private const int ExpectedTiles = 11;
    private const float TileSpan = 4f;
    private const float TileOrigin = -22f;
    private const int MaxObjectsScan = 8192; // 与 PalaceIslandContentPolicy.MaxObjects 一致的有界扫描

    // 准备窗口缓存的 sidecar 视图（token = durable guid/slot + reign）。
    private static readonly object Gate = new object();
    private static bool _classified;
    private static string _preparedGuid = "";
    private static int _preparedSlot = -1;
    private static int _preparedReign = -1;
    private static string _sidecarState = ""; // none | this-campaign | other-campaign | reign-mismatch | unreadable:<reason>
    private static int _sidecarEdgesMin, _sidecarEdgesMax;
    private static int _sidecarReign = -1;    // sidecar 记录 reign（reign-mismatch 时保留原值）

    // 明确 prepare epoch（current Game/G/C + 同 semantic 身份）：同 epoch 重复 prepare 不重复 IO；
    // 新 epoch（含 return clone reuse 分支）允许且必须重新分类一次。
    private static bool _prepareEpochValid;
    private static IntPtr _prepareGame, _prepareGlobal, _prepareCampaign;
    private static PalaceDestinationDecision _destinationDecision;
    private static bool _destinationAttempted;
    private static string _destinationFault="not-prepared";
    private static int _destinationEpoch,_destinationReign;
    private static IntPtr _destinationGame,_destinationGlobal,_destinationCampaign;
    private static PalaceLayoutRecipe _destinationRequired;
    private static PalaceTripRecord _resolvedTrip;
    private static PalaceLayoutRecipe _resolvedTripRecipe;
    private static IntPtr _resolvedTripGlobal,_resolvedTripCampaign,_resolvedTripIslands,_resolvedTripIsland,_resolvedTripObjects,_resolvedTripGame;
    private static int _resolvedTripObjectCount,_resolvedTripReign;



    // 资源预检缓存（只在源岛准备窗口执行一次）。
    private static bool _resourcePrecheckRun;
    private static bool _resourcePrecheckOk;
    private static string _resourcePrecheckReason = "profile 资源预检尚未在源岛准备窗口执行";

    /// <summary>
    /// 付前只读目的地分类（合同接口）。磁盘 IO 已在准备窗口完成；此处只消费缓存 sidecar 视图 +
    /// 进程内 native 只读（slot12 存在性 / 存档端点 / Beach 与 Tile 结构）。
    /// Fresh/SupportedSaved 返回 true；UnsupportedOrUnknown 返回 false 并给出明确 reason。
    /// </summary>
    internal static bool TryDescribeDestination(CampaignSaveData campaign,
        out PalaceDestinationSupport support, out string reason)
    {
        support = PalaceDestinationSupport.UnsupportedOrUnknown;
        reason = "";
        try
        {
            GlobalSaveData global = GlobalSaveData.loaded;
            if (!PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string bindReason))
            { reason = "campaign 身份未确证：" + bindReason; return false; }
            string sidecarState;
            int sidecarEdgesMin, sidecarEdgesMax, sidecarReign;
            lock (Gate)
            {
                if (!_classified)
                { reason = "目的地分类尚未在源岛准备窗口执行（有界 IO 只在 prepare 窗口）"; return false; }
                if (!string.Equals(guid, _preparedGuid, StringComparison.Ordinal) || slot != _preparedSlot)
                { reason = "campaign 身份与准备窗口分类不一致（需重新准备）"; return false; }
                if (campaign.reign != _preparedReign)
                { reason = "reign 已变化，需在新的源岛准备窗口重新分类"; return false; }
                sidecarState = _sidecarState;
                sidecarEdgesMin = _sidecarEdgesMin;
                sidecarEdgesMax = _sidecarEdgesMax;
                sidecarReign = _sidecarReign;
            }

            var islands = campaign._islands;
            bool slot12Present = islands != null
                && islands.Count > PalaceLand12Runtime.LandIndex
                && islands[PalaceLand12Runtime.LandIndex] != null;

            if (string.Equals(sidecarState, "none", StringComparison.Ordinal))
            {
                if (!slot12Present)
                {
                    // 正常 fresh 入口：容量 9/12 不存在都合法；文件表 14 槽 readiness 与 _islands.Count 分开。
                    support = PalaceDestinationSupport.Fresh;
                    reason = "fresh：本战役无 slot12；首次进入由既有 native 生成/保存路径创建，不在付款门填槽";
                    return true;
                }
                reason = UnsupportedPrefix + "：slot12 已存在但 schema2 sidecar 缺失（不删除、不重生成）";
                return false;
            }
            if (string.Equals(sidecarState, "other-campaign", StringComparison.Ordinal))
            {
                reason = "检测到另一战役共用的天朝 sidecar：本轮拒绝该未知组合，未收取金币（不覆盖、不迁移）";
                return false;
            }
            if (string.Equals(sidecarState, "reign-mismatch", StringComparison.Ordinal))
            {
                reason = "sidecar reign " + sidecarReign + " 与当前/冻结 campaign reign " + _preparedReign
                    + " 不符：same guid/slot 不同 reign 直接拒（未收取金币；不迁移、不删除、不清理）";
                return false;
            }
            if (string.Equals(sidecarState, "this-campaign", StringComparison.Ordinal))
            {
                if (!slot12Present)
                { reason = "sidecar 属于本战役但存档无 slot12（未知组合，拒绝且不迁移）"; return false; }
                IslandSaveData island = islands[PalaceLand12Runtime.LandIndex];
                IntRange edges = island._levelEdges;
                if (edges.min != sidecarEdgesMin || edges.max != sidecarEdgesMax)
                { reason = UnsupportedPrefix + "：存档端点与 schema2 sidecar 不符"; return false; }
                if (!TryReadSavedStructureFacts(island, out PalaceSavedIsland12Facts facts, out string structReason))
                { reason = UnsupportedPrefix + "：存档 slot12 结构不可读（" + structReason + "）"; return false; }
                if (facts.BeachObjectCount != 2 || !facts.BeachLeftOk || !facts.BeachRightOk)
                { reason = UnsupportedPrefix + "：canonical Beach 端部缺失/重复/几何不符"; return false; }
                if (!facts.TileGridOk)
                { reason = UnsupportedPrefix + "：11×4u Tile 网格结构不符"; return false; }
                // serialized land=0 合法（Native 恢复边界成为 12）；禁止把 saved land 必须 12 当付前条件。
                support = PalaceDestinationSupport.SupportedSaved;
                reason = "supported-saved：schema2 sidecar + 存档端点 + 两 canonical Beach 端部 + 11×4u Tile 相符";
                return true;
            }
            reason = UnsupportedPrefix + "：sidecar 不可读（" + sidecarState + "；不删除、不清、不重生成）";
            return false;
        }
        catch (Exception e)
        {
            reason = UnsupportedPrefix + "：分类读取异常 " + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 源岛/回程准备窗口调用：按明确 prepare epoch（current Game/G/C + 同 semantic 身份）最多一次只读
    /// sidecar 分类；同 epoch 重复调用零 IO、不按帧重读。严格复用 PalaceCourtArea.ReadSidecar（同一闭
    /// schema 严格解析 + ValidateMetadata）。比较 meta.Reign 与冻结 semantic owner/current campaign reign：
    /// same guid/slot 不同 reign 直接拒（不把当前 reign 赋给缓存冒充 sidecar 相符）。已确证 missing 记完成
    /// 读分类（state none）：无 slot12 为 Fresh，已有 slot12 拒绝；其余未知 null 结果及其他 campaign 一律拒。
    /// 不迁移、不删除、不清理、不补写任何字段。
    /// </summary>
    internal static void NoteDestinationPrepare(CampaignSaveData campaign)
    {
        try
        {
            GlobalSaveData global = GlobalSaveData.loaded;
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            IntPtr gamePointer = game != null ? game.Pointer : IntPtr.Zero;
            IntPtr globalPointer = global != null ? global.Pointer : IntPtr.Zero;
            IntPtr campaignPointer = campaign != null ? campaign.Pointer : IntPtr.Zero;
            if (gamePointer == IntPtr.Zero || globalPointer == IntPtr.Zero || campaignPointer == IntPtr.Zero
                || !PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string _))
            {
                lock (Gate)
                {
                    _classified = false;
                    _prepareEpochValid = false;
                    _sidecarState = "unreadable:campaign-binding-failed";
                }
                return;
            }
            lock (Gate)
            {
                if (_prepareEpochValid && gamePointer == _prepareGame && globalPointer == _prepareGlobal
                    && campaignPointer == _prepareCampaign
                    && string.Equals(guid, _preparedGuid, StringComparison.Ordinal)
                    && slot == _preparedSlot && campaign.reign == _preparedReign)
                    return; // 同一 prepare epoch：不重复 IO（失败分类同样不按帧重试）
            }

            PalaceCourtArea.CourtAreaMetadataRead meta = PalaceCourtArea.ReadSidecar(out string sidecarReason);
            bool ownerKnown = PalaceSaveBindingSource.TryGetSemanticOwner(out _, out _, out _, out int ownerReign, out _, out _);

            lock (Gate)
            {
                _prepareEpochValid = true;
                _prepareGame = gamePointer;
                _prepareGlobal = globalPointer;
                _prepareCampaign = campaignPointer;
                _preparedGuid = guid;
                _preparedSlot = slot;
                _preparedReign = campaign.reign;
                _sidecarReign = meta != null ? meta.Reign : -1;
                if (meta == null)
                {
                    _classified = string.Equals(sidecarReason, "missing", StringComparison.Ordinal);
                    _sidecarState = _classified ? "none" : "unreadable:" + sidecarReason;
                    _sidecarEdgesMin = 0;
                    _sidecarEdgesMax = 0;
                    return;
                }
                bool sameCampaign = string.Equals(meta.CampaignGuid, guid, StringComparison.Ordinal)
                    && meta.CampaignSlot == slot;
                if (!sameCampaign)
                {
                    _classified = true;
                    _sidecarState = "other-campaign";
                    _sidecarEdgesMin = meta.NativeEdgesMin;
                    _sidecarEdgesMax = meta.NativeEdgesMax;
                    return;
                }
                bool reignMatches = meta.Reign == campaign.reign && (!ownerKnown || meta.Reign == ownerReign);
                if (!reignMatches)
                {
                    _classified = true;
                    _sidecarState = "reign-mismatch";
                    _sidecarEdgesMin = 0;
                    _sidecarEdgesMax = 0;
                    return;
                }
                _classified = true;
                _sidecarState = "this-campaign";
                _sidecarEdgesMin = meta.NativeEdgesMin;
                _sidecarEdgesMax = meta.NativeEdgesMax;
            }
        }
        catch (Exception e)
        {
            lock (Gate)
            {
                _classified = false;
                _prepareEpochValid = false;
                _sidecarState = "unreadable:fault:" + e.GetType().Name;
            }
        }
    }

    /// <summary>
    /// 已保存 slot12 只读结构取证：恰好 2 条 canonical Beach（prefabPath/localPosition/localScale 实际字段，
    /// 左 -14.6875/scale.x=+1、右 +14.6875/scale.x=-1，y/z=0）+ 11 个 4u Tile 网格（x=-22+4i 各恰一次）。
    /// 读取异常/空槽按不可读拒绝；不写任何字段；land 字段不读不断言。
    /// </summary>
    internal static bool TryReadSavedStructureFacts(IslandSaveData island,
        out PalaceSavedIsland12Facts facts, out string reason)
    {
        facts = null;
        reason = "";
        try
        {
            var objects = island.objects;
            if (objects == null) { reason = "objects-null"; return false; }
            int count = objects.Count;
            if (count <= 0) { reason = "objects-empty"; return false; }
            if (count > MaxObjectsScan) { reason = "objects-over-budget:" + count; return false; }
            bool left = false, right = false;
            int beachCount = 0;
            var tileXs = new List<float>(ExpectedTiles + 4);
            for (int i = 0; i < count; i++)
            {
                IslandSaveData.ObjectData od = objects[i];
                if (od == null) { reason = "object-slot-null:" + i; return false; }
                string path = od.prefabPath;
                Vector3 position = od.localPosition;
                Vector3 scale = od.localScale;
                if (string.Equals(path, BeachPrefabPath, StringComparison.Ordinal))
                {
                    beachCount++;
                    if (Approx(position.x, BeachLeftX) && Approx(position.y, 0f) && Approx(position.z, 0f)
                        && Approx(scale.x, 1f)) left = true;
                    else if (Approx(position.x, BeachRightX) && Approx(position.y, 0f) && Approx(position.z, 0f)
                        && Approx(scale.x, -1f)) right = true;
                }
                else if (PalaceIslandContentPolicy.IsGroundTilePath(path))
                {
                    tileXs.Add(position.x);
                }
            }
            bool tileGridOk = tileXs.Count == ExpectedTiles;
            if (tileGridOk)
            {
                bool[] seen = new bool[ExpectedTiles];
                foreach (float x in tileXs)
                {
                    float slots = (x - TileOrigin) / TileSpan;
                    float rounded = Mathf.Round(slots);
                    if (Math.Abs(slots - rounded) > Tolerance) { tileGridOk = false; break; }
                    int index = (int)rounded;
                    if (index < 0 || index >= ExpectedTiles || seen[index]) { tileGridOk = false; break; }
                    seen[index] = true;
                }
                for (int i = 0; tileGridOk && i < ExpectedTiles; i++)
                    if (!seen[i]) { tileGridOk = false; }
            }
            facts = new PalaceSavedIsland12Facts
            {
                BeachLeftOk = left,
                BeachRightOk = right,
                BeachObjectCount = beachCount,
                TileObjectCount = tileXs.Count,
                TileGridOk = tileGridOk,
            };
            return true;
        }
        catch (Exception e) { reason = "structure-fault:" + e.GetType().Name; return false; }
    }

    // New fee path. The legacy overload above remains an explicit old44 compatibility reader;
    // current charge/departure callers must use this recipe-bound decision API.
    internal static bool TryDescribeDestination(CampaignSaveData campaign, PalaceLayoutRecipe required,
        out PalaceDestinationDecision decision, out string reason)
    {
        decision=null; reason="";
        lock(Gate)
        {
            if(!PalaceLayoutRecipe.IsKnown(required) || _destinationDecision==null)
            { reason="destination-not-prepared:"+_destinationFault; return false; }
            if(!ReferenceEquals(required,_destinationDecision.Recipe))
            { reason="RequiresExplicitMigration:destination-layout-differs"; return false; }
            if(!DecisionCurrent(campaign,_destinationDecision,out reason))return false;
            decision=_destinationDecision; return true;
        }
    }

    internal static bool TryFreezeForCharge(Game game, CampaignSaveData campaign,
        PalaceDestinationDecision observed, out PalaceDestinationDecision frozen, out string reason)
    {
        frozen=null; reason="";
        if(observed==null || !TryDescribeDestination(campaign,observed.Recipe,out var current,out reason)
            || !ReferenceEquals(observed,current) || game==null || game.Pointer!=observed.GamePointer)
        { reason="destination-observation-stale:"+reason; return false; }
        if(!TryCaptureDestination(campaign,observed.Recipe,observed.Epoch,out var fresh,out reason))return false;
        if(!observed.SameTarget(fresh))
        { reason="destination-content-drift-before-charge:"+(observed.TargetDifference(fresh)??"unknown"); return false; }
        frozen=observed; return true;
    }

    // This explicit boundary is called only after W verifies durable Resolve of the exact native
    // source-save proof. It never changes the paid recipe and is never reached from CanPay.
    internal static bool TryRefreshAfterPaidSourceSave(Game game, CampaignSaveData campaign,
        PalaceLayoutRecipe required, PalacePaidFileProof proof,
        out PalaceDestinationDecision refreshed,out string reason)
    {
        refreshed=null; reason="";
        PalaceDestinationDecision prior;
        lock(Gate)prior=_destinationDecision;
        if(proof==null || proof.NativeRawResult!=72 || proof.NativeVersion!=16 || game==null || campaign==null
            || prior==null || !ReferenceEquals(required,prior.Recipe) || proof.SourceLand!=game.currentLand
            || !proof.Owner.ContentEquals(prior.Owner) || game.Pointer!=prior.GamePointer
            || GlobalSaveData.loaded==null || GlobalSaveData.loaded.Pointer!=prior.GlobalPointer
            || campaign.Pointer!=prior.CampaignPointer || !PalaceSaveBindingSource.CurrentMatches(out reason))
        { reason="destination-refresh-proof-or-owner:"+reason; return false; }
        if(!TryCaptureDestination(campaign,required,prior.Epoch,out var next,out reason))return false;
        if(next.GlobalSnapshot.RawSaveSha256!=proof.RawSaveSha256 || !prior.SameDestinationContent(next))
        { reason="destination-refresh-target-or-raw-proof-differs"; return false; }
        lock(Gate)
        {
            if(!ReferenceEquals(_destinationDecision,prior)) { reason="destination-refresh-raced"; return false; }
            _destinationDecision=next;
        }
        refreshed=next; return true;
    }

    internal static void NoteDestinationPrepare(CampaignSaveData campaign,PalaceLayoutRecipe required)
    {
        try
        {
            Managers m=Managers.Inst; GlobalSaveData g=GlobalSaveData.loaded;
            if(m==null || m.game==null || g==null || campaign==null || !PalaceLayoutRecipe.IsKnown(required))
            { lock(Gate){_destinationDecision=null;_destinationFault="prepare-owner-or-recipe";} return; }
            lock(Gate)
            {
                if(_destinationAttempted && _destinationGame==m.game.Pointer && _destinationGlobal==g.Pointer
                    && _destinationCampaign==campaign.Pointer && _destinationReign==campaign.reign)
                {
                    if(!ReferenceEquals(_destinationRequired,required))
                    { _destinationDecision=null;_destinationFault="prepare-layout-changed-within-epoch"; }
                    return;
                }
                _destinationAttempted=true;_destinationGame=m.game.Pointer;_destinationGlobal=g.Pointer;
                _destinationCampaign=campaign.Pointer;_destinationReign=campaign.reign;_destinationRequired=required;
                _destinationDecision=null;_destinationEpoch++;_resolvedTrip=null;_resolvedTripRecipe=null;
            }
            bool ok=TryCaptureDestination(campaign,required,_destinationEpoch,out var decision,out string reason);
            lock(Gate){_destinationDecision=ok?decision:null;_destinationFault=reason;}
        }
        catch(Exception e) { lock(Gate){_destinationDecision=null;_destinationFault="prepare-fault:"+e.GetType().Name;} }
    }

    private static bool DecisionCurrent(CampaignSaveData campaign,PalaceDestinationDecision d,out string reason)
    {
        reason="";
        if(campaign==null || IslandSaveData.isSavingGame || !PalaceSaveBindingSource.CurrentMatches(out reason)
            || !ReferenceEquals(PalaceSaveBindingSource.Current,d.Binding))return false;
        Managers m=Managers.Inst; GlobalSaveData g=GlobalSaveData.loaded;
        if(m==null || m.game==null || g==null || m.game.Pointer!=d.GamePointer || g.Pointer!=d.GlobalPointer
            || campaign.Pointer!=d.CampaignPointer || campaign.reign!=d.Owner.Reign
            || campaign.biomeIndex!=d.Owner.Biome || campaign.realStartDateTime!=d.Owner.Start
            || campaign.CurrentLand!=m.game.currentLand || campaign.CurrentLand==12
            || CampaignSaveData.current==null || CampaignSaveData.current.Pointer!=campaign.Pointer
            || g.GetCurrentCampaign()==null || g.GetCurrentCampaign().Pointer!=campaign.Pointer
            || d.Binding.SemanticGuid!=d.Owner.CampaignGuid.ToString("D") || d.Binding.SemanticSlot!=d.Owner.CampaignSlot)
        { reason="destination-prepare-identity-drift"; return false; }
        var islands=campaign._islands;
        if(islands==null || islands.Pointer!=d.IslandsPointer || islands.Count!=d.IslandsCount)
        { reason="destination-islands-container-drift"; return false; }
        IslandSaveData island=islands.Count>12?islands[12]:null;
        if((island==null?IntPtr.Zero:island.Pointer)!=d.IslandPointer
            || (island!=null && (island.objects==null || island.objects.Pointer!=d.ObjectsPointer || island.objects.Count!=d.ObjectsCount)))
        { reason="destination-objects-container-drift"; return false; }
        if(!TryTargetFile(campaign,d.Binding,out var table,out int capacity,out string filename,out reason)
            || table!=d.TablePointer || capacity!=d.TableCapacity || filename!=d.TargetFile.Filename)
        { reason="destination-file-table-drift:"+reason; return false; }
        return true;
    }

    private static bool TryTargetFile(CampaignSaveData campaign,PalaceSaveBinding binding,
        out IntPtr tablePointer,out int capacity,out string filename,out string reason)
    {
        tablePointer=IntPtr.Zero;capacity=0;filename="";reason="";
        if(binding==null || !PalaceSaveBindingSource.CurrentMatches(out reason)
            || !ExtensionIslandRuntime.IsFilePropsPrepared() || IslandSaveData.isSavingGame)
        { reason="destination-file-table-not-ready:"+reason; return false; }
        var table=IslandSaveData.fileProps;
        capacity=IslandSaveData.MAX_BIOME_ISLANDS;
        if(table==null || capacity<ExtensionIslandRuntime.RequiredFileCapacity)
        { reason="destination-file-table-null-or-capacity"; return false; }
        var key=new IslandSaveData.FilePropsKey { campaign=binding.SemanticSlot,land=12,challengeId=0 };
        // FileProps is a native 16-byte value wrapped as a CLR class. The current Interop
        // TryGetValue allocates only an 8-byte out slot; use the runtime-invoke boxed getter.
        if(!table.ContainsKey(key))
        { reason="destination-file-key-missing"; return false; }
        var props=table[key];
        if(props==null)
        { reason="destination-file-key-missing"; return false; }
        string actualFilename=props.filename;
        if(string.IsNullOrEmpty(actualFilename))
        { reason="destination-file-key-missing"; return false; }
        filename=actualFilename; tablePointer=table.Pointer;
        if(Filer.folder!=binding.Folder || binding.Agent.folder!=binding.Folder
            || !ReferenceEquals(binding,PalaceSaveBindingSource.Current))
        { reason="destination-file-binding-drift"; return false; }
        return true;
    }

    private static bool TryCaptureDestination(CampaignSaveData campaign,PalaceLayoutRecipe required,int epoch,
        out PalaceDestinationDecision decision,out string reason)
    {
        decision=null;reason="";
        try
        {
            Managers managers=Managers.Inst;GlobalSaveData global=GlobalSaveData.loaded;
            if(!PalaceLayoutRecipe.IsKnown(required) || managers==null || managers.game==null || global==null || campaign==null
                || IslandSaveData.isSavingGame || managers.game.currentLand!=campaign.CurrentLand || campaign.CurrentLand==12
                || !PalaceSaveBindingSource.TryGetSemanticOwner(out string guid,out int slot,out int biome,out int reign,out int start,out reason)
                || !Guid.TryParseExact(guid,"D",out Guid id) || campaign.biomeIndex!=biome || campaign.reign!=reign || campaign.realStartDateTime!=start)
            { reason="destination-capture-owner:"+reason; return false; }
            var binding=PalaceSaveBindingSource.Current;
            var owner=new PalacePaidScope(PalaceRuntimeProfile.Id,id,slot,biome,reign,start,1);
            var islands=campaign._islands;
            if(islands==null) { reason="destination-ram-islands-unknown"; return false; }
            int count=islands.Count;IntPtr islandsPtr=islands.Pointer;
            IslandSaveData island=count>12?islands[12]:null;
            string ramJson=island==null?null:JsonUtility.ToJson(island,false);
            string ramHash=ramJson==null?"absent":DestinationHash(ramJson);
            if(!TryTargetFile(campaign,binding,out IntPtr table,out int capacity,out string filename,out reason))return false;
            string globalFilename=GlobalSaveData.filename;
            if(!PalaceDestinationWitness.TryReadFile(binding.Folder,globalFilename,PalacePaidSourceProof.MaxNativeCompressedBytes,out var raw,out reason)
                || !PalacePaidSourceProof.TryReadNativeDestinationSnapshot(raw,owner,campaign.CurrentLand,12,out var snapshot,out reason)
                || !PalaceDestinationWitness.TryObserveFile(binding.Folder,filename,out var target,out reason))return false;
            string sidecarPath=PalaceCourtArea.SidecarPathForAdmission();
            if(!PalaceDestinationWitness.TryObserveFile(Path.GetDirectoryName(sidecarPath),Path.GetFileName(sidecarPath),out var sidecarFile,out reason))return false;
            var meta=PalaceCourtArea.ReadSidecar(out string sidecarReason);
            if((meta==null)!=(sidecarFile.Kind==PalaceDestinationFileKind.Missing))
            { reason="destination-sidecar-observation-drift";return false; }
            PalaceDestinationSupport support;
            if(island==null)
            {
                if(snapshot.SlotKind==PalacePaidSourceProof.NativeDestinationSlotKind.Object
                    || target.Kind!=PalaceDestinationFileKind.Missing || meta!=null || sidecarReason!="missing")
                { reason="destination-fresh-four-witnesses-not-absent"; return false; }
                if(!ReferenceEquals(required,PalaceLayoutRecipe.Capital400))
                { reason="fresh-layout-must-be-explicit-capital400"; return false; }
                support=PalaceDestinationSupport.Fresh;
            }
            else
            {
                if(meta==null || meta.CampaignGuid!=guid || meta.CampaignSlot!=slot || meta.Reign!=reign
                    || !PalaceLayoutRecipe.IsKnown(meta.Recipe))
                { reason="destination-saved-sidecar-owner-or-layout:"+sidecarReason; return false; }
                if(!ReferenceEquals(meta.Recipe,required)) { reason="RequiresExplicitMigration:old44-requested400"; return false; }
                if(snapshot.SlotKind!=PalacePaidSourceProof.NativeDestinationSlotKind.Object || snapshot.IslandJson==null
                    || island._levelEdges.min!=required.Left || island._levelEdges.max!=required.Right
                    || !TryValidateSavedJson(ramJson,required,out reason) || !TryValidateSavedJson(snapshot.IslandJson,required,out reason)
                    || DestinationHash(ramJson)!=DestinationHash(snapshot.IslandJson))
                { reason="destination-saved-native-facts:"+reason; return false; }
                support=PalaceDestinationSupport.SupportedSaved;
            }
            if(!PalaceDestinationWitness.TryReadFile(binding.Folder,globalFilename,PalacePaidSourceProof.MaxNativeCompressedBytes,out var again,out reason)
                || !raw.AsSpan().SequenceEqual(again) || GlobalSaveData.filename!=globalFilename
                || !TryTargetFile(campaign,binding,out var tableAgain,out int capAgain,out string fileAgain,out reason)
                || table!=tableAgain || capacity!=capAgain || filename!=fileAgain
                || campaign._islands==null || campaign._islands.Pointer!=islandsPtr || campaign._islands.Count!=count
                || (island!=null && DestinationHash(JsonUtility.ToJson(island,false))!=ramHash)
                || !PalaceSaveBindingSource.CurrentMatches(out reason))
            { reason="destination-capture-double-read-drift:"+reason; return false; }
            decision=new PalaceDestinationDecision(required,support,owner,binding,epoch,managers.game.Pointer,global.Pointer,
                campaign.Pointer,islandsPtr,count,island==null?IntPtr.Zero:island.Pointer,
                island==null?IntPtr.Zero:island.objects.Pointer,island==null?0:island.objects.Count,
                table,capacity,target,snapshot,ramHash,meta==null?"missing":meta.MetadataSchema+":"+meta.CampaignGuid+":"+meta.CampaignSlot+":"+meta.Reign+":"+meta.Recipe.LayoutId,
                snapshot.IslandJson==null?"absent":DestinationHash(snapshot.IslandJson));
            if(!DecisionCurrent(campaign,decision,out reason)) { decision=null; return false; }
            return true;
        }
        catch(Exception e) { reason="destination-capture-fault:"+e.GetType().Name; return false; }
    }

    private static string DestinationHash(string text)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(text,new System.Text.Json.JsonDocumentOptions{MaxDepth=64});
        using var bytes=new MemoryStream();
        using(var writer=new System.Text.Json.Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            foreach(var property in doc.RootElement.EnumerateObject())
            {
                if(property.Name=="land")
                {
                    int land=property.Value.GetInt32();
                    if(land!=0 && land!=12)throw new InvalidDataException("destination-land");
                    // Native restores serialized0 to requested12. This exact alias is not content drift.
                    writer.WriteNumber("land",12);
                }
                else property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }

    private static bool TryValidateSavedJson(string json,PalaceLayoutRecipe recipe,out string reason)
    {
        if(!PalaceLayoutCoverage.TryValidateSavedGeometry(json,recipe,out reason))return false;
        using var doc=System.Text.Json.JsonDocument.Parse(json);
        var root=doc.RootElement;
        if(!root.TryGetProperty("land",out var savedLand) || !savedLand.TryGetInt32(out int nativeLand) || (nativeLand!=0 && nativeLand!=12))
        { reason="saved-land";return false; }
        if(!root.TryGetProperty("_levelEdges",out var edges) || edges.GetProperty("min").GetInt32()!=recipe.Left || edges.GetProperty("max").GetInt32()!=recipe.Right)
        { reason="saved-edges"; return false; }
        int beaches=0,left=0,right=0;
        foreach(var row in root.GetProperty("objects").EnumerateArray())
        {
            if(row.GetProperty("prefabPath").GetString()!=BeachPrefabPath)continue;
            beaches++;var position=row.GetProperty("localPosition");var scale=row.GetProperty("localScale");
            float x=position.GetProperty("x").GetSingle(),sx=scale.GetProperty("x").GetSingle();
            if(position.GetProperty("y").GetSingle()!=0 || position.GetProperty("z").GetSingle()!=0
                || scale.GetProperty("y").GetSingle()!=1 || scale.GetProperty("z").GetSingle()!=1)
            { reason="saved-beach-transform"; return false; }
            if(Approx(x,recipe.BeachLeftX)&&sx==1)left++;
            else if(Approx(x,recipe.BeachRightX)&&sx==-1)right++;
            else { reason="saved-beach-position"; return false; }
        }
        if(beaches!=2 || left!=1 || right!=1) { reason="saved-beach-count"; return false; }
        return true;
    }

    // Cold/config/return boundary only. Existing v3 records require actual saved44 evidence;
    // neither a null durable key nor a profile switch can imply400.
    internal static bool TryResolveTripLayout(CampaignSaveData campaign,PalaceTripRecord trip,
        out PalaceLayoutRecipe recipe,out string reason)
    {
        recipe=null;reason="";
        try
        {
            if(campaign==null || trip==null || !PalaceTripContext.Validate(trip,out reason)
                || !PalaceLand12Runtime.TryReadSavedCampaignBinding(GlobalSaveData.loaded,campaign,out string guid,out int slot,out reason)
                || !PalaceTripContext.RecordMatches(campaign,guid,slot,trip,out reason))return false;
            PalaceLayoutRecipe known=trip.Version==4?trip.DestinationLayout:trip.Version==3?PalaceLayoutRecipe.Known44:null;
            if(!PalaceLayoutRecipe.IsKnown(known)) { reason="trip-layout-unknown"; return false; }
            var islands=campaign._islands;
            IslandSaveData currentIsland=islands!=null && islands.Count>12?islands[12]:null;
            if(_resolvedTrip!=null && PalaceTripContext.TupleEquals(_resolvedTrip,trip) && ReferenceEquals(_resolvedTripRecipe,known)
                && Managers.Inst!=null && Managers.Inst.game!=null && _resolvedTripGame==Managers.Inst.game.Pointer
                && _resolvedTripGlobal==GlobalSaveData.loaded.Pointer && _resolvedTripCampaign==campaign.Pointer
                && _resolvedTripReign==campaign.reign && islands!=null && _resolvedTripIslands==islands.Pointer
                && currentIsland!=null && currentIsland.objects!=null && _resolvedTripIsland==currentIsland.Pointer
                && _resolvedTripObjects==currentIsland.objects.Pointer && _resolvedTripObjectCount==currentIsland.objects.Count)
            { recipe=known;return true; }
            string sidecarPath=PalaceCourtArea.SidecarPathForAdmission();
            if(!PalaceDestinationWitness.TryObserveFile(Path.GetDirectoryName(sidecarPath),Path.GetFileName(sidecarPath),out var sidecarFile,out reason)
                || sidecarFile.Kind!=PalaceDestinationFileKind.Present)return false;
            var meta=PalaceCourtArea.ReadSidecar(out reason);
            if(meta==null || !ReferenceEquals(meta.Recipe,known) || meta.CampaignGuid!=guid || meta.CampaignSlot!=slot
                || meta.Reign!=campaign.reign || islands==null || islands.Count<=12 || islands[12]==null
                || !TryValidateSavedJson(JsonUtility.ToJson(islands[12],false),known,out reason))
            { reason="trip-layout-saved-evidence:"+reason; return false; }
            string frozenTripJson=System.Text.Json.JsonSerializer.Serialize(trip);
            if(!PalaceTripContext.TryParse(frozenTripJson,out var frozenTrip,out reason))return false;
            _resolvedTrip=frozenTrip;_resolvedTripRecipe=known;_resolvedTripGlobal=GlobalSaveData.loaded.Pointer;
            _resolvedTripGame=Managers.Inst!=null && Managers.Inst.game!=null?Managers.Inst.game.Pointer:IntPtr.Zero;
            _resolvedTripCampaign=campaign.Pointer;_resolvedTripReign=campaign.reign;_resolvedTripIslands=islands.Pointer;
            _resolvedTripIsland=islands[12].Pointer;_resolvedTripObjects=islands[12].objects.Pointer;
            _resolvedTripObjectCount=islands[12].objects.Count;
            recipe=known;return true;
        }
        catch(Exception e) { reason="trip-layout-fault:"+e.GetType().Name;return false; }
    }

    internal static void RunResourcePrecheck(PalaceLayoutRecipe recipe)
    {
        if(ReferenceEquals(recipe,PalaceLayoutRecipe.Known44)){RunResourcePrecheck();return;}
        if(ReferenceEquals(recipe,PalaceLayoutRecipe.Capital400))PalaceCapitalRuntime.Warm();
        // Unknown recipes do not warm old R7 as Capital400.
    }
    internal static bool TryGetResourcePrecheck(PalaceLayoutRecipe recipe,out string reason)
    {
        if(ReferenceEquals(recipe,PalaceLayoutRecipe.Known44))return TryGetResourcePrecheck(out reason);
        if(ReferenceEquals(recipe,PalaceLayoutRecipe.Capital400))
            return PalaceCapitalRuntime.TryGetPackage(out _,out reason);
        reason="resource-layout-unknown";
        return false;
    }

    /// <summary>源岛准备窗口调用：精确资源契约预检（LogicalName 全集 / 字节上界 / PNG IHDR 尺寸 /
    /// 已有字面 SHA256 全量比对 / actors manifest 完整契约与帧清单），只执行一次并缓存；失败付前 refuse。</summary>
    internal static void RunResourcePrecheck()
    {
        lock (Gate)
        {
            if (_resourcePrecheckRun) return;
            _resourcePrecheckRun = true;
            _resourcePrecheckOk = TryPrecheckProfileResources(out _resourcePrecheckReason);
        }
    }

    /// <summary>CanPay 只读消费准备窗口的资源预检结果（未执行 → 拒绝；零文件 IO）。</summary>
    internal static bool TryGetResourcePrecheck(out string reason)
    {
        lock (Gate)
        {
            reason = _resourcePrecheckRun
                ? _resourcePrecheckReason
                : "profile 资源预计尚未在源岛准备窗口执行（有界 IO 只在 prepare 窗口）";
            return _resourcePrecheckRun && _resourcePrecheckOk;
        }
    }

    // 契约来源 = 固定 profile 实际消费者自己的 loader 表（单一权威、无第二 hash 表）：
    //   入口 PalaceEntryArt；R4 三层 PalaceScrollScene.Layers；R7 26 层 + R6 48 帧 PalaceDepthScene
    //   （Specs + LocalFrameHashes）；R7 actors-only = PalaceWorldExpansion.ParseActorManifest 全量断言 +
    //   manifest 声明的 3 atlas；地表合成源 2 张 = PalaceTerrainPalette.Build 消费的 KEM_PalaceCourt 源 PNG
    //   （PalaceTerrainPalette.cs 不在本 worker 写入边界，故在此以同域常量声明；tests 逐值与源常量钉死防漂移）。
    // 不消费（不得加成付款依赖）：legacy architecture manifest、PALACE_CONTENT_PRIVATE=false 的 R6 庭院 13 图、
    //   R4 legacy 九图 assets 路径。
    private const int MaxPrecheckResources = 128;
    private const long MaxPrecheckTotalBytes = 320L * 1024 * 1024;
    private const string TerrainSourcePrefix = "KingdomEnhancedMod.KEM_PalaceCourt/";
    private const long TerrainSourceMaxBytes = 256L * 1024;
    private const int TerrainSourceWidth = 64;
    private const int TerrainSourceHeight = 32;

    private static bool TryBuildProfileResourceContracts(out List<PalaceResourceContract> contracts, out string reason)
    {
        contracts = new List<PalaceResourceContract>(96);
        reason = "";
        try
        {
            PalaceEntryArt.DescribeResourceContract(contracts);
            PalaceScrollScene.DescribeResourceContracts(contracts, out string r4Reason);
            if (r4Reason.Length != 0) { reason = "r4:" + r4Reason; contracts = null; return false; }
            PalaceDepthScene.DescribeResourceContracts(contracts, out string depthReason);
            if (depthReason.Length != 0) { reason = "r7:" + depthReason; contracts = null; return false; }
            if (!PalaceWorldExpansion.TryDescribeActorsResourceContract(contracts, out string actorsReason))
            { reason = "actors:" + actorsReason; contracts = null; return false; }
            contracts.Add(new PalaceResourceContract(TerrainSourcePrefix + "ground/brick-surface.png", null,
                TerrainSourceWidth, TerrainSourceHeight, TerrainSourceMaxBytes, "terrain-source"));
            contracts.Add(new PalaceResourceContract(TerrainSourcePrefix + "ground/stone-bank.png", null,
                TerrainSourceWidth, TerrainSourceHeight, TerrainSourceMaxBytes, "terrain-source"));
            if (contracts.Count > MaxPrecheckResources)
            { reason = "contract-count:" + contracts.Count; contracts = null; return false; }
            return true;
        }
        catch (Exception e) { reason = "contract-fault:" + e.GetType().Name; contracts = null; return false; }
    }

    private static bool TryPrecheckProfileResources(out string reason)
    {
        reason = "";
        try
        {
            if (!TryBuildProfileResourceContracts(out List<PalaceResourceContract> contracts, out string contractReason))
            { reason = "resource-contract:" + contractReason; return false; }
            System.Reflection.Assembly assembly = typeof(PalaceSaveBinding).Assembly;
            string[] names = assembly.GetManifestResourceNames();
            var nameSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
                if (names[i] != null) nameSet.Add(names[i]);
            long totalBytes = 0;
            int hashed = 0;
            int gap = 0;
            for (int i = 0; i < contracts.Count; i++)
            {
                PalaceResourceContract contract = contracts[i];
                if (!CheckProfileResource(assembly, nameSet, contract, ref totalBytes, out reason)) return false;
                if (contract.Sha256Hex != null) hashed++;
                else gap++;
            }
            reason = "profile 资源预检通过（精确 LogicalName " + contracts.Count + " 项；字面 SHA256 比对 "
                + hashed + " 项；无 literal 缺口 " + gap + " 项按存在/字节上界/IHDR 尺寸/manifest 契约核验；"
                + "总字节 " + totalBytes + " <= " + MaxPrecheckTotalBytes + "）";
            return true;
        }
        catch (Exception e) { reason = "resource-precheck-fault:" + e.GetType().Name; return false; }
    }

    private static bool CheckProfileResource(System.Reflection.Assembly assembly, HashSet<string> names,
        PalaceResourceContract contract, ref long totalBytes, out string reason)
    {
        reason = "";
        if (contract == null || string.IsNullOrEmpty(contract.LogicalName)) { reason = "contract-invalid"; return false; }
        // 精确名称相等：同 prefix 的别名文件不能替代 required 项（拒绝 prefix 计数语义）。
        if (!names.Contains(contract.LogicalName)) { reason = "resource-missing:" + contract.LogicalName; return false; }
        try
        {
            using System.IO.Stream stream = assembly.GetManifestResourceStream(contract.LogicalName);
            if (!PalaceResourceBytes.TryReadBounded(stream, contract.MaxBytes, out byte[] bytes, out string readReason))
            { reason = "resource-unreadable:" + contract.LogicalName + ":" + readReason; return false; }
            totalBytes += bytes.Length;
            if (totalBytes > MaxPrecheckTotalBytes) { reason = "resource-budget:" + totalBytes; return false; }
            if (!PalaceResourceBytes.TryReadPngSize(bytes, out int width, out int height, out string pngReason))
            { reason = "resource-not-png:" + contract.LogicalName + ":" + pngReason; return false; }
            if (contract.Width != 0 && (width != contract.Width || height != contract.Height))
            { reason = "resource-size:" + contract.LogicalName + ":" + width + "x" + height
                + " != " + contract.Width + "x" + contract.Height; return false; }
            if (!PalaceResourceBytes.MatchesSha256(bytes, contract.Sha256Hex, out string hashReason))
            { reason = "resource-hash:" + contract.LogicalName + ":" + hashReason; return false; }
            return true;
        }
        catch (Exception e) { reason = "resource-fault:" + contract.LogicalName + ":" + e.GetType().Name; return false; }
    }

    private static bool Approx(float a, float b) => Math.Abs(a - b) <= Tolerance;
}
