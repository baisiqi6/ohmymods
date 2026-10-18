// PalaceNativeBackground（私有 land12 无墙场景的原生防御背景 None 模式）。source-only：Root 负责接线/构建/实机验收。
//
// 触发证据（receipts/r4-zero-wall-native-background-evidence.json + runtime LogOutput 1566/1571）：
// - 已观察：本次专用 land12 四个墙 tag 计数全 0；bgWallSegments=421 有效（nulls/destroyed/noTransform=0）；
//   NullReferenceException 栈 = Kingdom.UpdateBGIndeces ← SetupBGComponent ← OnLevelLoaded。
// - 实际 throw PC 未捕获：native 选择 callee 在空 list 时返回 null、UpdateBGIndeces 内存在无条件解引用分支，
//   都只是静态读回线索，不能写成"本次已命中该分支"或"已观察到 ordered-wall count=0"。
//
// 修复责任：只为确证的当次私有 land12 BG Setup 定义一个 None epoch——该 epoch 内原生防御背景布局不参与；
//   不补假墙、不猜索引、不写 HasBorderLoaded、不跳过整岛初始化、不改数组/材质/scale/位置/相机/人物。
//   背景根是借用：先停用并读回 false 才发布；生命周期边界按原 activeSelf 归还并读回。
//
// 只接 BG 子系统扩展边界：
// - ready：8 个 hook 全部安装成功才置 _hooksReady=true；任一失败保持 false，所有前缀首行受 ready 约束
//   （UnpatchAll 若失败，残留前缀也因 ready=false 不生效）。一次尝试，不重试。
// - 发布（Kingdom.SetupBGComponent 前缀）：先消费已有 exact epoch——同一 Kingdom 直接 skip（不 SetActive、
//   不复证、不覆盖原责任）；owner 不同或存在未完成归还责任时，先显式完成旧 epoch 交还，失败则不覆盖旧 refs、
//   也不发布新 epoch。新发布门 = Enabled / 离线 / ExtensionIslandRuntime.TryScope / campaign 与 game land 12 /
//   exact Managers.kingdom==__instance / Level 与 world.gameLayer 同 scene / 非 saving / 背景根同 scene 且
//   == 当前 kingdom._bgWallParent / PalaceCourtArea.TryGetSpan(level,out l,out r,out reason) 单次通过
//   （自有生成或 cold 身份 + 活体地面；不新增扫描、不等候、不重试、不写 flag）。全部门过才借用 root。
//   返回 false 只跳过 SetupBGComponent 本体：OnLevelLoaded 其余代码照常继续（其自身在 Setup 返回后写
//   _hasBorderLoaded 再调 CalculateBordersNow），border/军队/事件链不经本类改动。
// - 消费：UpdateBGIndeces / SetCampBGActive / RecalculateBGSectionAges / SetBGBorderPoint 四个前缀，
//   先认 exact Kingdom 实例 == 已发布 owner，命中才 skip；不按当前 land/动态 Enabled 重算，因此换岛时
//   campaign 已先切新岛后，旧 Kingdom 的 BG 子调用与旧 Setup 行为一致。InsertOrderedWall / RemoveOrderedWall /
//   CalculateBorderOnSide / CalculateBordersNow / AcquireWallSegments 一律不 hook、原样保留。
// - 归还：唯一实现 TryHandBack，在生命周期边界（Init/OnDisable/OnDestroy，且 __instance 必须是 exact
//   published owner；旧 Kingdom 的延迟 OnDestroy 不清新 epoch）或 owner 变更 Setup 前单次尝试。仅当 exact
//   借用 root 存活、scene 与 owner._bgWallParent 仍对应且当前 false 时恢复原 activeSelf 并读回复核；原activeSelf
//   已恢复/确证Unity销毁 → 明确归还、无需写；关系漂移或读取异常不动根，保留quarantine。写失败或读回不符 → 保留
//   quarantine 责任（refs 不丢、不重试、无周期纠正器），期间不发布新 epoch。原 Setup 可能 early return，
//   不假设原生一定重新激活。
// 未证明（留待 Root 实机核验，见 completion.md）：Setup 时刻 TryGetSpan 是否已 ready；Init/OnDisable/OnDestroy
//   与下一次消费者调用之间的时序；普通岛在同一 Kingdom 实例上的调用顺序；同方法上既有只读 metrics 前缀与
//   本 false 返回的执行顺序；被内联的消费者调用点。Native cold、免费回程、borrow 归还均未通过验证。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceNativeBackground
{
    private const string EnableEnv = "PALACE_NATIVE_BG_NONE_PRIVATE";
    private const string OwnerSuffix = ".PalaceNativeBackground";

    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    private static bool _installed;    // 单次尝试：resolve/patch 失败也不重试
    private static bool _hooksReady;   // 8 个前缀全部安装成功才 true；false 时所有前缀一律不生效
    /// <summary>付前只读安装证据：8 个前缀全部安装成功（窄暴露，供 PalaceInstallationManifest）。</summary>
    internal static bool HooksReady => _hooksReady;
    private static Harmony _owner;     // 独立 owner：卸载只作用于本次自己的前缀

    /// <summary>
    /// 唯一借用记录（owner 三元组）：Kingdom wrapper + exact pointer、Level/scene、背景根 wrapper + exact
    /// pointer、原 activeSelf。Published=false 且记录存在 = "未发布但需归还"的 quarantine 责任。
    /// </summary>
    private sealed class Borrow
    {
        internal Kingdom Owner;
        internal IntPtr OwnerPtr;
        internal GameObject Root;
        internal IntPtr RootPtr;
        internal bool OriginalActive;
        internal IntPtr LevelPtr;
        internal int Scene;
        internal bool Published;
        internal bool HandBackAttempted; // one boundary attempt; failed responsibility remains quarantined
    }

    private static Borrow _borrow;
    private static int _epoch;

    // 累计消费计数（回执证据，不参与判定）。
    private static int _skippedSetup, _skippedIndeces, _skippedCamp, _skippedAges, _skippedBorder;

    /// <summary>私有环境门：本开关与 PalaceScrollNativeBinding 组合门同时成立才挂 hook/发布新 epoch。</summary>
    internal static bool Enabled
        => PalaceRuntimeProfile.Capability(EnableEnv) && PalaceScrollNativeBinding.Enabled;

    /// <summary>
    /// 单次安装（手动挂载，无属性）：resolve 全部 8 个目标 → 逐个挂前缀 → 全成功才 _hooksReady=true。
    /// owner 用独立 Harmony id（Root id + 后缀），失败清理只可能命中本类自己的前缀。
    /// </summary>
    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed) return;
        // C：安装与动态 Enabled 分离——固定 profile 能力即安装 8 前缀；Enabled 只约束新 epoch 发布。
        if (!PalaceRuntimeProfile.Capability(EnableEnv)) return;
        _installed = true;
        try
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            MethodInfo setup = Resolve("SetupBGComponent", 0);
            MethodInfo indeces = Resolve("UpdateBGIndeces", 0);
            MethodInfo camp = Resolve("SetCampBGActive", 1, "TechnologyAge");
            MethodInfo ages = Resolve("RecalculateBGSectionAges", 1, "Side");
            MethodInfo border = Resolve("SetBGBorderPoint", 2, "Side", "Single");
            MethodInfo init = Resolve("Init", 0);
            MethodInfo disable = Resolve("OnDisable", 0);
            MethodInfo destroy = Resolve("OnDestroy", 0);
            if (setup == null || indeces == null || camp == null || ages == null || border == null
                || init == null || disable == null || destroy == null)
            {
                Log("ERROR targets not uniquely resolvable (SetupBGComponent=" + (setup != null)
                    + " UpdateBGIndeces=" + (indeces != null) + " SetCampBGActive=" + (camp != null)
                    + " RecalculateBGSectionAges=" + (ages != null) + " SetBGBorderPoint=" + (border != null)
                    + " Init=" + (init != null) + " OnDisable=" + (disable != null)
                    + " OnDestroy=" + (destroy != null) + "); no hooks installed, _hooksReady stays false, no retry");
                return;
            }
            MethodInfo[] targets = { setup, indeces, camp, ages, border, init, disable, destroy };
            string[] prefixes =
            {
                nameof(SetupPrefix), nameof(UpdateBGIndecesPrefix), nameof(SetCampBGActivePrefix),
                nameof(RecalculateBGSectionAgesPrefix), nameof(SetBGBorderPointPrefix),
                nameof(InitPrefix), nameof(OnDisablePrefix), nameof(OnDestroyPrefix),
            };
            _owner = new Harmony(harmony.Id + OwnerSuffix);
            for (int i = 0; i < targets.Length; i++)
            {
                try
                {
                    _owner.Patch(targets[i], prefix: new HarmonyMethod(typeof(PalaceNativeBackground), prefixes[i]));
                }
                catch (Exception e)
                {
                    Log("ERROR prefix install failed at Kingdom." + targets[i].Name + " (" + e.GetType().Name
                        + " " + e.Message + "); _hooksReady stays false, removing this class's prefixes, no retry");
                    RemoveOwnPrefixes();
                    return;
                }
            }
            _hooksReady = true;   // 只有 8 个全部就位后，前缀才开始生效
            Log("installed 8 prefixes on Kingdom (SetupBGComponent/UpdateBGIndeces/SetCampBGActive/"
                + "RecalculateBGSectionAges/SetBGBorderPoint/Init/OnDisable/OnDestroy); _hooksReady=true;"
                + " private zero-wall land12 None mode only; owner=" + _owner.Id);
        }
        catch (Exception e)
        {
            Log("ERROR native background install failed (single attempt, no retry): " + e.GetType().Name + " " + e.Message);
            RemoveOwnPrefixes();
        }
    }

    /// <summary>按本类独立 owner id 卸载；即使卸载失败，残留前缀也因 _hooksReady=false 不生效，且不触碰他人 hook。</summary>
    private static void RemoveOwnPrefixes()
    {
        _hooksReady = false;   // 先失效：清理失败时残留前缀也一律不生效
        if (_owner == null) return;
        try { _owner.UnpatchSelf(); }
        catch (Exception e)
        {
            Log("ERROR partial-install cleanup failed: " + e.GetType().Name + " " + e.Message
                + "; residual prefixes stay inert because _hooksReady=false");
        }
    }

    /// <summary>Kingdom 上恰好一个匹配的 void 实例方法才返回它，否则记录候选并返回 null（fail-closed）。</summary>
    private static MethodInfo Resolve(string name, int parameterCount, params string[] parameterTypeNames)
    {
        MethodInfo found = null;
        foreach (MethodInfo method in typeof(Kingdom).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.Name != name || method.IsStatic || method.ContainsGenericParameters) continue;
            if (method.ReturnType != typeof(void)) continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterCount) continue;
            bool typesMatch = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType.Name != parameterTypeNames[i]) { typesMatch = false; break; }
            }
            if (!typesMatch) continue;
            if (found != null)
            {
                Log("ERROR " + name + " has multiple candidates; resolution rejected");
                return null;
            }
            found = method;
        }
        return found;
    }

    // ------------------------------------------------------------------ BG Setup 边界：发布 / 借用

    private static bool SetupPrefix(Kingdom __instance)
    {
        if (!_hooksReady) return true;
        try
        {
            if (_borrow != null && __instance != null && __instance.Pointer == _borrow.OwnerPtr)
            {
                if (_borrow.Published)
                {
                    // 已有 exact epoch：直接消费，不 SetActive、不复证、不覆盖原责任。
                    _skippedSetup++;
                    LogSkipOnce("SetupBGComponent");
                    return false;
                }
                // 未发布但需归还（上次停用未确认）：边界处单次交还；失败保留 quarantine，不发布、不覆盖。
                if (!TryHandBack("Kingdom.SetupBGComponent (unresolved borrow)")) return true;
                // 交还已完成：同一次调用内不再重新借用同一 root（避免反复 SetActive），留到下一个 Setup 边界。
                LogOnce("defer-borrow:" + _epoch, "hand-back completed at this Setup; new borrow deferred to the"
                    + " next BG setup boundary (native SetupBGComponent runs unchanged this time)");
                return true;
            }
            if (_borrow != null)
            {
                // 不同 owner：先显式完成旧 epoch 交还，不能覆盖丢旧 refs；交还失败则不发布新 epoch。
                if (!TryHandBack("Kingdom.SetupBGComponent (owner change)")) return true;
            }
            if (!Enabled) return true; // only new publications depend on current enable flags
            if (!TryConfirmPrivateZeroWallSetup(__instance, out Level level, out GameObject root,
                out string span, out string reason))
            {
                LogOnce("gate:" + reason, "setup is not the confirmed private zero-wall land12 (" + reason
                    + "); native SetupBGComponent runs unchanged");
                return true;
            }
            if (!TryBorrow(__instance, level, root, out string fault))
            {
                Log("ERROR " + fault + "; no publication, native SetupBGComponent runs unchanged");
                return true;
            }
            _skippedSetup++;
            Log("epoch#" + _epoch + " None published at BG setup boundary: kingdom=" + Ptr(__instance.Pointer)
                + " level=" + Ptr(level.Pointer) + " levelScene=" + level.gameObject.scene.handle
                + " bgRoot=" + Ptr(root.Pointer) + " bgRootName=" + SafeName(root)
                + " originalActive=" + _borrow.OriginalActive + " " + span
                + "; borrowed root deactivated & readback false (SetActive(false) only; array/materials/"
                + "_cachedBG*Index untouched); border/army/event chains continue in OnLevelLoaded");
            return false;
        }
        catch (Exception e)
        {
            Log("ERROR setup gate fault; native SetupBGComponent left to run: " + e.GetType().Name + " " + e.Message);
            return true;
        }
    }

    /// <summary>
    /// 当次 Setup 的完整私有门：离线 + 确证 scope + game/campaign land 12 + exact 当前 Kingdom + Level 与
    /// gameLayer 同 scene + 非 saving + 背景根同 scene 且 == 当前 kingdom._bgWallParent + 单次
    /// PalaceCourtArea.TryGetSpan（自有生成/cold 身份 + 活体地面）。reason 为未过门原因，span 为成功证据。
    /// </summary>
    private static bool TryConfirmPrivateZeroWallSetup(
        Kingdom kingdom, out Level level, out GameObject root, out string span, out string reason)
    {
        level = null;
        root = null;
        span = null;
        reason = null;
        if (NetworkBigBoss.IsOnline) { reason = "online"; return false; }
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) { reason = "scope"; return false; }
        Managers managers = Managers.Inst;
        Game game = managers != null ? managers.game : null;
        Kingdom current = managers != null ? managers.kingdom : null;
        if (game == null || current == null) { reason = "managers(game/kingdom)"; return false; }
        if (kingdom == null || kingdom.Pointer != current.Pointer)
        { reason = "not exact current kingdom"; return false; }
        if (game.currentLand != PalaceLand12Runtime.LandIndex
            || campaign.CurrentLand != PalaceLand12Runtime.LandIndex)
        { reason = "land game=" + game.currentLand + " campaign=" + campaign.CurrentLand; return false; }
        if (IslandSaveData.isSavingGame) { reason = "saving"; return false; }
        Level currentLevel = managers.level;
        World world = managers.world;
        Transform layer = world != null ? world.gameLayer : null;
        if (currentLevel == null || layer == null || currentLevel.gameObject == null || layer.gameObject == null)
        { reason = "level/gameLayer unavailable"; return false; }
        int levelScene = currentLevel.gameObject.scene.handle;
        if (levelScene != layer.gameObject.scene.handle)
        { reason = "Level/gameLayer scene differ"; return false; }
        GameObject fromKingdom = kingdom._bgWallParent;
        GameObject fromCurrent = current._bgWallParent;
        if (fromKingdom == null || fromKingdom.Pointer == IntPtr.Zero)
        { reason = "bgWallParent invalid"; return false; }
        if (fromCurrent == null || fromCurrent.Pointer != fromKingdom.Pointer)
        { reason = "bgWallParent is not the exact current kingdom root"; return false; }
        if (fromKingdom.scene.handle != levelScene)
        { reason = "bgWallParent scene differs from Level scene"; return false; }
        if (!PalaceCourtArea.TryGetSpan(currentLevel, out float left, out float right, out string spanReason))
        { reason = "court span unavailable: " + spanReason; return false; }
        level = currentLevel;
        root = fromKingdom;
        span = "courtSpan=[" + F(left) + "," + F(right) + "]";
        return true;
    }

    /// <summary>
    /// 借用：读原 activeSelf → 已停用则直接确认；否则 SetActive(false) 并读回。读回 false → Published；
    /// 读回不为 false 或 SetActive 抛错（即使状态已变）→ Published=false 的归还责任记录（refs 不丢、
    /// 不再重复尝试停用），返回 false。
    /// </summary>
    private static bool TryBorrow(Kingdom kingdom, Level level, GameObject root, out string fault)
    {
        fault = null;
        bool original;
        try { original = root.activeSelf; }
        catch (Exception e)
        {
            fault = "borrowed root activeSelf unreadable (" + e.GetType().Name + "); nothing written, no responsibility";
            return false;
        }
        var record = new Borrow
        {
            Owner = kingdom,
            OwnerPtr = kingdom.Pointer,
            Root = root,
            RootPtr = root.Pointer,
            OriginalActive = original,
            LevelPtr = level.Pointer,
            Scene = level.gameObject.scene.handle,
        };
        _epoch++;
        _borrow = record; // register borrowed responsibility before any SetActive side effect
        if (!original)
        {
            record.Published = true;
            _borrow = record;
            return true;
        }
        try
        {
            root.SetActive(false);
        }
        catch (Exception e)
        {
            record.Published = false;
            _borrow = record;
            fault = "background root SetActive(false) fault (" + e.GetType().Name + " " + e.Message
                + ", readback=" + ActiveText(root) + "); unconfirmed borrow kept as hand-back responsibility";
            return false;
        }
        bool readback;
        string readbackText;
        try { readback = root.activeSelf; readbackText = readback.ToString(); }
        catch (Exception e) { readback = true; readbackText = "unreadable(" + e.GetType().Name + ")"; }
        if (!readback)
        {
            record.Published = true;
            _borrow = record;
            return true;
        }
        record.Published = false;
        _borrow = record;
        fault = "background root readback not false (" + readbackText + "); unconfirmed borrow kept as"
            + " hand-back responsibility";
        return false;
    }

    // ------------------------------------------------------------------ 4 个 BG 消费门

    /// <summary>UpdateBGIndeces：该异常栈的落点方法；None epoch 内跳过（其余原样）。</summary>
    private static bool UpdateBGIndecesPrefix(Kingdom __instance)
    {
        if (!ConsumeNone(__instance, "UpdateBGIndeces")) return true;
        _skippedIndeces++;
        return false;
    }

    /// <summary>SetCampBGActive：None epoch 内不按科技年龄激活原生背景分段（其余原样）。</summary>
    private static bool SetCampBGActivePrefix(Kingdom __instance)
    {
        if (!ConsumeNone(__instance, "SetCampBGActive")) return true;
        _skippedCamp++;
        return false;
    }

    /// <summary>RecalculateBGSectionAges：None epoch 内不重算背景分段年龄（其余原样）。</summary>
    private static bool RecalculateBGSectionAgesPrefix(Kingdom __instance)
    {
        if (!ConsumeNone(__instance, "RecalculateBGSectionAges")) return true;
        _skippedAges++;
        return false;
    }

    /// <summary>SetBGBorderPoint：None epoch 内不写背景 border 点（其余原样）。</summary>
    private static bool SetBGBorderPointPrefix(Kingdom __instance)
    {
        if (!ConsumeNone(__instance, "SetBGBorderPoint")) return true;
        _skippedBorder++;
        return false;
    }

    /// <summary>
    /// 消费门：先认 exact Kingdom 实例 == 已发布 owner，命中才 skip；不按当前 land/动态 Enabled 重算
    /// （换岛时 campaign 先切新岛，旧 Kingdom 的 BG 子调用仍按 owner 消费）。仅检查异常时明确 ERROR 并不跳过。
    /// </summary>
    private static bool ConsumeNone(Kingdom kingdom, string site)
    {
        if (!_hooksReady) return false;
        Borrow b = _borrow;
        if (b == null || !b.Published) return false;
        try
        {
            if (kingdom == null || kingdom.Pointer != b.OwnerPtr) return false;
            LogSkipOnce(site);
            return true;
        }
        catch (Exception e)
        {
            Log("ERROR " + site + " consumption check fault; native call left to run: "
                + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 归还：唯一一次交还

    /// <summary>Kingdom.Init：只对 exact published owner 交还；原方法照常执行（其内部 Setup 会再走门）。</summary>
    private static void InitPrefix(Kingdom __instance)
    {
        if (!_hooksReady) return;
        try { HandBackForExactOwner(__instance, "Kingdom.Init"); }
        catch (Exception e) { Log("ERROR Init hand-back fault: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>Kingdom.OnDisable：只对 exact published owner 交还；保留原方法。</summary>
    private static void OnDisablePrefix(Kingdom __instance)
    {
        if (!_hooksReady) return;
        try { HandBackForExactOwner(__instance, "Kingdom.OnDisable"); }
        catch (Exception e) { Log("ERROR OnDisable hand-back fault: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>Kingdom.OnDestroy：只对 exact published owner 交还；旧 Kingdom 的延迟销毁不清新 epoch。</summary>
    private static void OnDestroyPrefix(Kingdom __instance)
    {
        if (!_hooksReady) return;
        try { HandBackForExactOwner(__instance, "Kingdom.OnDestroy"); }
        catch (Exception e) { Log("ERROR OnDestroy hand-back fault: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>release 必须传入 exact __instance：只匹配该 published owner，其他实例（含旧 Kingdom 延迟事件）忽略。</summary>
    private static void HandBackForExactOwner(Kingdom instance, string cause)
    {
        Borrow b = _borrow;
        if (b == null) return;
        if (instance == null || instance.Pointer != b.OwnerPtr)
        {
            LogOnce("release-skip:" + cause + ":" + Ptr(instance != null ? instance.Pointer : IntPtr.Zero),
                cause + " ignored: caller is not the published owner=" + Ptr(b.OwnerPtr)
                + "; epoch responsibility untouched");
            return;
        }
        TryHandBack(cause);
    }

    /// <summary>
    /// 唯一交还实现（每个borrow仅一次边界尝试）。原activeSelf读回或确证销毁后释放refs；
    /// 关系漂移、读取异常、写失败或读回不符保留quarantine（refs不丢，不重试）。
    /// </summary>
    private static bool TryHandBack(string cause)
    {
        Borrow b = _borrow;
        if (b == null) return true;
        b.Published = false; // lifecycle/handover retires consumption before native Init continues
        if (b.HandBackAttempted) return false; // no repeated restore after an ambiguous failure
        b.HandBackAttempted = true;
        string outcome;
        bool done;
        try
        {
            GameObject root = b.Root;
            if (root == null)
            {
                outcome = "borrowed root Unity-destroyed/null; no write";
                done = true;
            }
            else if (root.Pointer != b.RootPtr)
            {
                outcome = "borrowed root pointer drift; untouched, unresolved ownership retained";
                done = false;
            }
            else if (root.scene.handle != b.Scene)
            {
                outcome = "root scene drift; untouched, unresolved ownership retained";
                done = false;
            }
            else if (b.Owner == null || b.Owner.Pointer != b.OwnerPtr
                || b.Owner._bgWallParent == null || b.Owner._bgWallParent.Pointer != b.RootPtr)
            {
                outcome = "owner/_bgWallParent relationship drift; untouched, unresolved ownership retained";
                done = false;
            }
            else if (root.activeSelf == b.OriginalActive)
            {
                outcome = "original activeSelf already verified; no write";
                done = true;
            }
            else if (!b.OriginalActive)
            {
                outcome = "original false changed externally to true; no owned mutation to undo, unresolved";
                done = false;
            }
            else
            {
                root.SetActive(b.OriginalActive);
                bool readback = root.activeSelf;
                done = readback == b.OriginalActive;
                outcome = done
                    ? "restored original activeSelf=" + b.OriginalActive + " (readback verified)"
                    : "restore readback mismatch (activeSelf=" + readback + ")";
            }
        }
        catch (Exception e)
        {
            bool readable = false;
            bool atOriginal = false;
            bool activeValue = false;
            try
            {
                readable = b.Root != null && b.Root.Pointer == b.RootPtr;
                if (readable) { activeValue = b.Root.activeSelf; atOriginal = activeValue == b.OriginalActive; }
            }
            catch (Exception) { readable = false; }
            if (!readable)
            {
                done = false;
                outcome = "borrowed root state unreadable; responsibility retained (" + e.GetType().Name + ")";
            }
            else if (atOriginal)
            {
                done = true;
                outcome = "fault during hand-back (" + e.GetType().Name + ") but readback shows original"
                    + " activeSelf=" + activeValue;
            }
            else
            {
                done = false;
                outcome = "hand-back fault (" + e.GetType().Name + "); activeSelf=" + activeValue;
            }
        }
        if (!done)
        {
            Log("ERROR epoch#" + _epoch + " hand-back incomplete by " + cause + ": " + outcome
                + "; quarantine retained (exact refs kept, no retry, no periodic corrector);"
                + " new epoch publication stays blocked until this hand-back completes");
            return false;
        }
        Log("epoch#" + _epoch + " None handed back by " + cause + ": " + outcome + "; kingdom=" + Ptr(b.OwnerPtr)
            + " level=" + Ptr(b.LevelPtr) + " scene=" + b.Scene + " bgRoot=" + Ptr(b.RootPtr)
            + " originalActive=" + b.OriginalActive + " " + (b.Published ? "published" : "unpublished")
            + "; consumed[setup=" + _skippedSetup + " indeces=" + _skippedIndeces + " camp=" + _skippedCamp
            + " ages=" + _skippedAges + " border=" + _skippedBorder + "]; refs released, original method preserved");
        _borrow = null;
        return true;
    }

    // ------------------------------------------------------------------ 基础工具

    private static void LogSkipOnce(string site)
    {
        if (LoggedKeys.Add("skip:" + _epoch + ":" + site))
        {
            Log("epoch#" + _epoch + " consumed at " + site + ": native BG sub-step skipped for the published"
                + " owner=" + Ptr(_borrow != null ? _borrow.OwnerPtr : IntPtr.Zero));
        }
    }

    private static string Ptr(IntPtr pointer) => "0x" + pointer.ToInt64().ToString("x");

    private static string F(float value) => value.ToString("F3");

    private static string SafeName(GameObject go)
    {
        try { return go != null ? go.name : "(null)"; }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }

    private static string ActiveText(GameObject go)
    {
        try { return go != null ? go.activeSelf.ToString() : "(null)"; }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }

    private static void Log(string message)
    {
        try { PalaceCourtVisuals.Log("[NativeBgNone] " + message); }
        catch (Exception) { }
    }

    private static void LogOnce(string key, string message)
    {
        try
        {
            if (!LoggedKeys.Add(key)) return;
            PalaceCourtVisuals.Log("[NativeBgNone] " + message);
        }
        catch (Exception) { }
    }
}
