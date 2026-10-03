using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 原生地图资源图标（issue-98 地图部分；r2 修订）。
    ///
    /// 依据 CONTRACT.md / native-map-abi.json / map-layout.json / r1 REVIEW.md：
    /// - 复用原生 UIMapIcon prefab（clone 后由原生 UIMapIcon.UpdateIcon 决定已获得/未获得态、
    ///   BiomeData 替换与显隐），不改原生全局模板；
    /// - 条目唯一来源 = 当次 UILand.UpdateLand 传入的 reignInfo.landData[land]；不带 currentReign
    ///   回退（数据不可用即失效并清掉该 land 旧自有显示，不混用存在性/状态源）；
    /// - 作用域 = 确切拥有该 UILand 的 Greek MapTimelineMenuGreece 的两套 land（menu.lands /
    ///   menu._mainMap._lands）；不再按全局 biome 泛化；
    /// - 动态槽接管（r1 缺陷 3）：对上述 land 的 Steed/Hermit/Statue 三类 UIDynamicMapIcon，
    ///   原生 _spawnedIcon 在 ON 期间统一压制（记录原 activeSelf，teardown 精确还原），
    ///   全部条目由自有 UIMapIcon clone 按当次数组显示 —— 旧 type/消失条目不会残留；
    ///   静态剧情/建筑/船/狗/矿等图标一律不动；
    /// - 总览布局（r7，用户 2026-10-02 否定 5×2 网格后重做）：**保留原生 10 簇地理关系**——
    ///   冻结原始快照后只施加统一倍率 scale + 整体平移（MapOverviewLayout 纯函数），底部固定预留
    ///   扩展带；不做每岛压缩/网格化/地理排序，也不为挤图标移动或缩岛；
    /// - 排版（r7）：图标按真实 art footprint 的"最近岛"自由区域（MapIconRegionPlanner 纯函数，
    ///   格点 Voronoi + 最大矩形分解）分岛规划；区域两两不相交 ⇒ 不会错归属；放不下则整体不显示
    ///   （日志记精确缺口），可读下限 0.36 档，绝不部分贴图、也不降到 0.2 超小档；
    /// - 实机视口（r10）：布局装入"实际可见 paper 矩形"（Screen/safeArea/rootCanvas/camera.pixelRect 与真实
    ///   RectMask2D 交集，投影回 paper 本地），而不是只在 paper 内 fit；缓存含视口签名且只在 world 稳定帧
    ///   重算（连续两次一致才换布局），GrowTo 不再承担收缩（mask clamp 走可逆 ResizeExact）；
    ///   每视口签名一次有界只读诊断；
    /// - 布局链（r2）：Content Area/Lands Container 命名节点与 paper 一起显式扩到
    ///   min(MapArea-8, cap)，只受真正带 RectMask2D 的祖先约束（单次显式夹取，不做多轮回退），
    ///   快照可逆；未解锁岛簇沿用 UIMainMapLand.IsUnlocked（云层语义保留）；
    /// - clone 的 Image/Text raycastTarget=false，不拦截原生选岛 Button。
    /// </summary>
    internal sealed class MapMountIconView
    {
        internal UILand Land;
        internal bool Overview;
        internal int LandIndex = -1;
        internal GameObject Holder;
        internal int Fingerprint = int.MinValue;
        /// <summary>上次判定"放不下"的请求指纹：同内容不重复压制/重建，保持原生动态槽可见。</summary>
        internal int FailedFingerprint = int.MinValue;
        internal bool HiddenByNativeGate;
        /// <summary>布局键的一部分：几何版本（viewport 提交递增）；与 fingerprint 一起决定 fast-path。</summary>
        internal int LayoutVersion = -1;
        internal readonly List<UIMapIcon> Icons = new List<UIMapIcon>(12);
        internal readonly List<SuppressedIcon> Suppressed = new List<SuppressedIcon>(4);
    }

    internal sealed class SuppressedIcon
    {
        internal GameObject Target;
        internal bool OriginalActive;
    }

    internal static class MapMountIcons
    {
        private const int TypeStatue = 0;   // UIMapIcon/MapIconType（native 枚举）
        private const int TypeSteed = 1;
        private const int TypeHermit = 2;

        private const int DynSteed = 0;     // UIDynamicMapIcon/DynamicIcon（native 主机实测）
        private const int DynHermit = 1;
        private const int DynStatue = 4;

        private const int OverviewButtonBandPx = 18;   // paper 顶部原生按钮带（实测 [Map] 按钮占位）
        private const float StatusLaneWidth = 20f;     // r15：扩展 banner 右侧状态/船标预算车道（真实 box 仍参与碰撞）
        /// <summary>总览图标可读下限档：0.36（不再自动降到 0.2 超小档）。</summary>
        private const int OverviewScaleCount = MapResourceIconPlanner.DefaultScaleCount;

        /// <summary>详情可用区外扩（真实 LandsHolder 294×186 相对 land 180×150 的余量内）。</summary>
        private const float DetailMarginX = 24f;
        private const float DetailMarginY = 18f;

        /// <summary>Map Area 322×216 减 8 的确定值（真实层级实测；r2 不再用 318×212 名义 cap）。</summary>
        private const float PaperMaxW = 314f;
        private const float PaperMaxH = 208f;
        private const float MapAreaPad = 8f;

        private static readonly List<MapMountIconView> Views = new List<MapMountIconView>(24);
        // 复用缓冲：刷新路径不产生每帧分配（UpdateLands 节奏未静态证明）。
        private static readonly int[] SteedBuffer = new int[64];
        private static readonly int[] HermitBuffer = new int[16];
        private static readonly int[] StatueBuffer = new int[16];
        private static readonly List<MapIconRequest> RequestScratch = new List<MapIconRequest>(16);
        private static readonly List<UIMapIcon> SourceScratch = new List<UIMapIcon>(16);

        private static readonly List<OverviewEntry> Overview = new List<OverviewEntry>(10);
        private static readonly List<RectSnapshot> Expanded = new List<RectSnapshot>(4);
        private static bool _overviewApplied;
        private static int _overviewLandCount = -1;
        private static UILand _overviewExtensionLand;
        // r11 实机视口：已提交的视口签名 / 提交用 paper / 几何版本 / 缓存 mask / tick 有界周期 / 诊断去重。
        private static long _overviewAppliedSignature = long.MinValue;
        private static RectTransform _overviewPaper;
        private static int _overviewGeometryVersion;
        private static readonly List<RectTransform> _overviewMasks = new List<RectTransform>(12);
        private static ViewportCycle _viewportCycle;
        private static long _overviewDiagnosedViewport = long.MinValue;
        private static int _worldMasksFitCount;                    // 本帧 world mask fit 数量（诊断）
        private static MapIconBox _worldDomain;                    // 最近一次提交的 world 域（诊断/holder 尺寸）
        private static MapIconBox _worldUpperArea;
        private static MapIconBox _worldBandArea;
        private static bool _worldSuspended;                       // world override 挂起（single1/未知）：数据回调不重建
        private static RectTransform _worldPhysicalPaper;          // exact 物理纸缓存引用（stamp/fit 共用）
        private static readonly List<RectTransform> _bannerExcludedRects = new List<RectTransform>(4);
        private static bool _nativeRefreshActive;                  // 同步重入门
        private static int _nativeRefreshAttemptedVersion = -1;    // 每几何版本至多一次刷新尝试

        // r14/WORLD-OVERVIEW-ONLY：world0 只显示群岛。绑定 exact Greek/menu/map/holder + **稳定 owner 身份**
        // （campaign ptr / scene handle / currentLand int，不用每读取变化的装箱 reign 代理）+ 自有 CanvasGroup；
        // 稳定态 O(1)（只读 state/身份/绑定），只在目标态变化时写自有组 alpha/blocksRaycasts。
        private static MapTimelineMenuGreece _presentationMenu;
        private static UIMainMap _presentationMap;
        private static RectTransform _presentationHolder;
        private static CanvasGroup _presentationGroup;
        private static bool _presentationHidden;                   // 自有组当前是否 alpha0（memo，避免重复写）
        private static IntPtr _presentationCampaign;               // 绑定时 capture 的 campaign 指针身份
        private static int _presentationScene = -1;                // 绑定时 capture 的 scene.handle（valid+loaded）
        private static int _presentationLand = int.MinValue;       // 绑定时 capture 的 campaign.CurrentLand
        private static int _presentationReign = int.MinValue;      // 绑定时 capture 的 campaign.reign（稳定 int）
        // 校验 memo：同一身份 tuple 的结构校验只做一次；拒绝（外部组/未知树/写失败）保持到真实结构换代。
        private static MapTimelineMenuGreece _attemptMenu;
        private static UIMainMap _attemptMap;                      // map 身份：null→ready 可再次尝试
        private static RectTransform _attemptHolder;
        private static IntPtr _attemptCampaign;
        private static int _attemptScene = -1;
        private static int _attemptLand = int.MinValue;
        private static int _attemptReign = int.MinValue;
        private static int _attemptGeneration = -1;                // 详情重建代次（exact Clear 递增）
        private static int _presentationGeneration;                // 代次计数器（只在 exact Clear 递增）
        private static bool _presentationRejected;                 // 该 tuple 已判拒：不接管/不热重建

        private sealed class OverviewEntry
        {
            internal UILand Cluster;
            internal RectTransform Rect;
            internal Vector2 AnchoredPosition;   // 原始（还原用）
            internal Vector3 LocalScale;         // 原始（还原用）
            internal int LandIndex = -1;
            internal bool IsExtension;
            internal MapOverviewClusterInput Input;
            internal MapIconBox TargetArtBox;
            internal MapIconBox BannerArea;       // r15：扩展簇 banner（底部预留带）
            internal readonly List<MapIconBox> RegionRects = new List<MapIconBox>(8);
        }

        private sealed class RectSnapshot
        {
            internal RectTransform Rect;
            internal Vector2 SizeDelta;
            internal Vector2 AnchoredPosition;
            internal Vector3 LocalScale;   // r15：宽岛 banner 子 rect（terrain/outline/Button）还原用
        }

        // ------------------------------------------------------------------ gate

        /// <summary>资源 icons 门（可关闭）：Mod 总门 + CrossWorld 新授予开关 + 当前 Greek。
        /// **不等于**扩展地图必需几何门——current/visited 11 的注册扩展在开关 OFF 时仍须底部布局（见
        /// MapOverviewLifecycle），否则恢复/返航时 UI10 会叠回 Oracle 原位。</summary>
        internal static bool FeatureEnabled()
        {
            try
            {
                return ModConfig.Enabled != null && ModConfig.Enabled.Value
                    && ModConfig.CrossWorldMountsEnabled != null && ModConfig.CrossWorldMountsEnabled.Value
                    && BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex;
            }
            catch (Exception) { return false; }
        }

        /// <summary>扩展岛可用性（含开关 OFF 但 current/visited 11 的恢复场景）：island-worker 的现成门。</summary>
        private static bool IsExtensionAccessAvailable()
        {
            try { return ExtensionIslandRuntime.IsAvailableForCurrentCampaign(); }
            catch (Exception) { return false; }
        }

        /// <summary>缓存几何是否仍需保留（非本菜单/未知 owner 入口用）：缓存实例仍登记且本 campaign 可用。</summary>
        private static bool CachedExtensionGeometryRequired()
        {
            UILand extension = _overviewExtensionLand;
            if (extension == null) return false;
            if (!TryGetRegisteredExtensionIndex(extension, out _)) return false;
            return IsExtensionAccessAvailable();
        }

        // ------------------------------------------------------------------ bridge

        /// <summary>
        /// 登记桥（travel-worker `ExtensionIslandMap.TryGetPhysicalLandIndex`，集成时链接真源）：
        /// 仅对**已登记**的扩展 UILand exact 实例返回 true + physical index（11）；原生实例与未登记
        /// extra 一律 false。本功能只用它做两件事：识别第 11 项、把资源索引换成 physical11
        /// （physical 索引不得反过来索引 UI 数组）。桥缺失/异常一律按未登记处理（fail-closed）。
        /// </summary>
        private static bool TryGetRegisteredExtensionIndex(UILand land, out int physical)
        {
            physical = -1;
            if (land == null) return false;
            try
            {
                if (!ExtensionIslandMap.TryGetPhysicalLandIndex(land, out int index)) return false;
                if (index < 0) return false;
                physical = index;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>数组位置解析（capture 用）：登记扩展 → physical11；原生 → 数组位置；未登记 extra → false（不画）。</summary>
        private static bool TryResolveClusterLandIndex(UILand cluster, int uiPosition, out int landIndex)
        {
            landIndex = -1;
            if (TryGetRegisteredExtensionIndex(cluster, out int physical)) { landIndex = physical; return true; }
            if (uiPosition < 0 || uiPosition >= MapOverviewLayout.NativeUiClusterCount) return false;
            landIndex = uiPosition;
            return true;
        }

        /// <summary>UpdateLand 参数解析：登记扩展 → physical11（无论原生前缀是否已改写）；原生 → 当次 index；未登记 extra → false。</summary>
        private static bool TryResolveIncomingLandIndex(UILand land, int incoming, out int landIndex)
        {
            landIndex = -1;
            if (TryGetRegisteredExtensionIndex(land, out int physical)) { landIndex = physical; return true; }
            if (incoming < 0 || incoming >= MapOverviewLayout.NativeUiClusterCount) return false;
            landIndex = incoming;
            return true;
        }

        /// <summary>当前 map 中已登记的扩展实例（用于布局缓存漂移检测；有界扫描，只在 OnLandUpdated/布局时调用）。</summary>
        private static UILand FindRegisteredExtension(UIMainMap map)
        {
            try
            {
                Il2CppReferenceArray<UILand> lands = map != null ? map._lands : null;
                if (lands == null) return null;
                for (int i = 0; i < lands.Length; i++)
                {
                    if (lands[i] == null) continue;
                    if (TryGetRegisteredExtensionIndex(lands[i], out _)) return lands[i];
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>未登记 extra 的陈旧 view（登记被清理后）：撤显示并还原原生槽，之后完全交还原生。</summary>
        private static void DropUnregisteredView(UILand land)
        {
            MapMountIconView view = FindView(land, true);
            if (view == null) return;
            DropView(view, true);
            Views.Remove(view);
            MapIconLog.Once("unregistered-extra", "unregistered extra UILand dropped (native untouched)");
        }

        // ------------------------------------------------------------------ entry

        /// <summary>UILand.UpdateLand postfix：仅处理确切属于 Greek 菜单两套 land 的实例。</summary>
        internal static void OnLandUpdated(UILand land, int landIndex, CampaignSaveData.ReignInfo reign)
        {
            try
            {
                if (land == null || landIndex < 0) return;

                bool iconsEnabled = FeatureEnabled();

                // r15/R3：任何全局 Teardown/WithdrawIcons/几何请求/overview 重建前，先确认 exact 当前 owner。
                // unknown/迟到 sender 不是清理当前 owner 状态的授权（数据回调窗口可能早于下一次 tick）。
                MapTimelineMenu activeMap = null;
                bool activeFault = false;
                try
                {
                    Menu menuInst = Menu.Inst;
                    if (menuInst != null) activeMap = menuInst.ActiveMap;
                }
                catch (Exception) { activeFault = true; }
                bool OwnerIsCurrent(MapTimelineMenuGreece candidate)
                    => !activeFault && candidate != null && activeMap == candidate;

                if (!TryGetOwnedMenu(land, out MapTimelineMenuGreece menu, out bool overview))
                {
                    // 非本菜单/未知 owner：icons OFF 且缓存几何也不再需要时完整还原（与旧 OFF 清理一致）；
                    // 必需几何（current/visited11 注册扩展）在生效时**不**被这里的 Teardown 撤回。
                    if (OwnerIsCurrent(menu) &&
                        MapOverviewLifecycle.ShouldTeardownOnForeignLand(iconsEnabled, CachedExtensionGeometryRequired()))
                    {
                        Teardown();
                    }
                    return;   // 未知/迟到 sender：不动当前 owner
                }

                // 登记桥优先：登记扩展实例 → physical11；未登记 extra → 完全不属于本功能（不建 view/不压制）。
                if (!TryResolveIncomingLandIndex(land, landIndex, out int resolvedLandIndex))
                {
                    DropUnregisteredView(land);
                    return;
                }
                landIndex = resolvedLandIndex;

                // 生命周期裁决（纯函数决策表）：把"扩展地图必需几何"与"可关闭的 icons"分开。
                UIMainMap ownedMainMap = menu != null ? menu._mainMap : null;
                bool extensionRegistered = FindRegisteredExtension(ownedMainMap) != null;
                bool extensionAvailable = extensionRegistered && IsExtensionAccessAvailable();
                MapOverviewAction action = MapOverviewLifecycle.DecideForOwnedLand(
                    extensionRegistered, iconsEnabled, extensionAvailable);

                if (action == MapOverviewAction.Teardown)
                {
                    if (OwnerIsCurrent(menu)) Teardown();
                    return;
                }
                if (action == MapOverviewAction.LayoutWithoutIcons)
                {
                    if (!OwnerIsCurrent(menu)) return;
                    // icons 关闭：撤自有 holder + 还原原生槽（不得借必要 layout 继续画 disabled 资源）；
                    // 但保留扩展地图必需几何（底部带 + 原 10 统一 shift + 真实 paper/mask），供恢复/返航。
                    WithdrawIcons();
                    if (ownedMainMap != null) EnsureOverviewLayout(ownedMainMap);
                    return;
                }

                if (overview)
                {
                    // r15/R3：world 几何只归 exact 当前 owner + world0 状态；
                    // state1/未知时**同帧**收回 captured overview 或直接拒旧 commit（不等 tick）。
                    if (!OwnerIsCurrent(menu)) return;
                    int callerWorldState = -1;
                    bool callerStateFault = false;
                    try { callerWorldState = (int)menu._openWorldMapState; }
                    catch (Exception) { callerStateFault = true; }
                    if (callerStateFault || callerWorldState != MapOverviewPresentationPolicy.ShowingWorld)
                    {
                        SuspendWorldGeometry(menu);
                        return;
                    }
                    // 几何未提交（viewport 周期 pending/失败）：不动原生槽、不建自有 holder；
                    // 提交发生在 MapTimelineMenu.Update tick，之后由提交触发的**一次 native 刷新**接管。
                    RectTransform overviewPaper = ownedMainMap != null
                        ? ownedMainMap.gameObject.GetComponent<RectTransform>() : null;
                    if (!OverviewLayoutCommitted(ownedMainMap, overviewPaper))
                    {
                        EnsureOverviewLayout(ownedMainMap);
                        return;
                    }
                }

                MapMountIconView view = FindView(land, true);

                // 只用当次 reign：不可用（null / 越界 / entry null）即失效并清掉该 land 旧自有显示，
                // 但 ON+owned 期间**不归还**原生资源槽的接管责任：若此刻还原，原生 _spawnedIcon 会带着
                // 上一历史 reign 的旧 type 回显（R1 缺陷 3 的第三条路径）。数据不可用 ≠ 原生云层门。
                if (!TryReadLand(reign, landIndex, out int steedCount, out int hermitCount, out int statueCount))
                {
                    MapMountIconView stale = EnsureView(view, land, landIndex, overview);
                    SuppressNativeDynamicIcons(land, stale);
                    DestroyHolder(stale);
                    stale.HiddenByNativeGate = false;
                    return;
                }

                if (overview && menu != null && menu._mainMap != null) EnsureOverviewLayout(menu._mainMap);

                BuildRequests(land, steedCount, hermitCount, statueCount,
                    out List<MapIconRequest> requests, out List<UIMapIcon> sources);
                int fingerprint = Fingerprint(landIndex, overview, requests);

                // 合法空集/首次开启也必须建 view：只要 ON 且 owned，接管责任就必须存在
                // （否则 native 自带的旧 _spawnedIcon 原样可见；R1 缺陷 3 的第一、二条路径）。
                view = EnsureView(view, land, landIndex, overview);
                view.LandIndex = landIndex;
                view.Overview = overview;

                // 同内容上次已裁决放不下：保持原生动态槽可见（已还原），不重复压制/重建；
                // 岛完全保持原生可用，详情仍可打开并给出完整真实资源列表。
                if (view.Holder == null && view.FailedFingerprint == fingerprint &&
                    view.LayoutVersion == _overviewGeometryVersion)
                {
                    RestoreSuppressed(view);
                    return;
                }

                // ON 期间统一接管三类动态槽：压制原生 _spawnedIcon（原 activeSelf 记录在案，可还原）。
                SuppressNativeDynamicIcons(land, view);

                // 原生揭露门：未解锁岛簇保持原生云层语义（不建/即毁自有图标根）。
                if (overview && !IsClusterUnlocked(land))
                {
                    if (view.Holder != null || view.Icons.Count > 0) DestroyHolder(view);
                    view.HiddenByNativeGate = true;
                    view.Fingerprint = fingerprint;
                    return;
                }
                view.HiddenByNativeGate = false;

                if (view.Holder != null && view.Fingerprint == fingerprint && view.Icons.Count == requests.Count &&
                    (!overview || view.LayoutVersion == _overviewGeometryVersion))
                {
                    Repaint(view, landIndex, reign);
                    return;
                }

                if (requests.Count == 0)
                {
                    // 合法空集：只清自有图根/fingerprint；原生槽保持压制（还原只发生在 OFF/退出）。
                    DestroyHolder(view);
                    view.Fingerprint = fingerprint;
                    return;
                }

                Rebuild(view, land, landIndex, overview, menu, reign, requests, sources, fingerprint);
            }
            catch (Exception e)
            {
                MapIconLog.Once("refresh-" + e.GetType().Name, "refresh failed: " + e.Message);
            }
        }

        /// <summary>UIMainMapLand.OnEnable：随原生揭露状态刷新自有图标根的显隐（不重跑 Awake、不改原生对象）。</summary>
        internal static void OnClusterEnabled(UIMainMapLand mainMapLand)
        {
            try
            {
                if (!FeatureEnabled() || mainMapLand == null) return;
                UILand land = mainMapLand.gameObject.GetComponent<UILand>();
                if (land == null) return;
                MapMountIconView view = FindView(land, false);
                if (view == null) return;
                bool visible = IsClusterUnlocked(land);
                view.HiddenByNativeGate = !visible;
                if (view.Holder != null) view.Holder.SetActive(visible);
            }
            catch (Exception) { }
        }

        /// <summary>OnDisable postfix（现成入口，带 __instance）：**只按 exact owner 门控**——未知 sender 与
        /// 迟到旧 menu 一律不动任何 owner 的展示/几何；当前 owner 自己的 disable 才撤自有组 + 几何回收。</summary>
        internal static void OnMenuDisabled(MapTimelineMenuGreece menu)
        {
            try
            {
                if (menu == null) return;                       // 未知 sender：不动任何 owner 的状态
                if (_presentationMenu == menu)
                {
                    ReleaseOwnGroup();
                    ClearAttemptMemo();
                }
                if (OwnsCurrentGeometry(menu)) Teardown();
            }
            catch (Exception) { }
        }

        /// <summary>该 menu 是否就是当前已提交几何的 exact owner（map 的 RectTransform == 已提交 paper）。</summary>
        private static bool OwnsCurrentGeometry(MapTimelineMenuGreece menu)
        {
            try
            {
                if (_overviewPaper == null) return false;
                UIMainMap map = menu._mainMap;
                if (map == null || map.gameObject == null) return false;
                return map.gameObject.GetComponent<RectTransform>() == _overviewPaper;
            }
            catch (Exception) { return false; }
        }

        /// <summary>r15：world→single/未知 时撤 world override。只回收 **overview 专属** 资源根（销毁 holder +
        /// 归还其 native 动态槽）、还原原 10 transform/扩展 banner 子 rect/mask/物理纸快照与提交标记；
        /// detail views/holder/缓存与 R14 presentation 组一律不动（single1 只中性）。幂等、有界、异常隔离。</summary>
        private static void SuspendWorldGeometry()
            => SuspendWorldGeometry(null);

        /// <summary>owner 非 null 时只回收**属于该 captured menu** 的 overview view（其他 owner/详情 holder 绝不波及）。</summary>
        private static void SuspendWorldGeometry(MapTimelineMenuGreece owner)
        {
            try
            {
                bool any = false;
                for (int i = Views.Count - 1; i >= 0; i--)
                {
                    MapMountIconView view = Views[i];
                    if (view == null || !view.Overview) continue;   // 窄回收：只 overview
                    if (owner != null &&
                        FindAncestor<MapTimelineMenuGreece>(view.Land != null ? view.Land.transform : null) != owner)
                    {
                        continue;   // 非 captured owner 的 overview：不动
                    }
                    any = true;
                    DropView(view, true);
                    Views.RemoveAt(i);
                }
                if (any) MapIconLog.Once("world-suspend-views", "overview views reclaimed on world exit");
                bool hadGeometry = _overviewApplied || Overview.Count > 0 || Expanded.Count > 0;
                RestoreOverview();
                RestoreExpansion();
                if (hadGeometry || !_worldSuspended)
                {
                    _overviewApplied = false;
                    _overviewLandCount = -1;
                    _overviewGeometryVersion++;
                    _overviewAppliedSignature = long.MinValue;
                    _viewportCycle = null;          // state1→0 重新请求 world 稳定测量（不复用分页 offset）
                    _worldDomain = default;
                    _worldUpperArea = default;
                    _worldBandArea = default;
                    _worldSuspended = true;
                }
            }
            catch (Exception e)
            {
                MapIconLog.Once("world-suspend-" + e.GetType().Name, "world geometry suspend failed: " + e.Message);
            }
        }

        /// <summary>我们的地图 scope 结束（tick 自己判定的 OFF/结束路径）：展示组先撤，再做几何回收。</summary>
        internal static void Teardown()
        {
            try { WithdrawPresentation(); }
            catch (Exception) { }
            TeardownGeometry();
        }

        /// <summary>几何/图标回收（既有语义；不触碰展示组——旧 owner 迟到 disable 也要安全调用）。</summary>
        private static void TeardownGeometry()
        {
            try
            {
                WithdrawIcons();
                RestoreExpansion();
                RestoreOverview();
                _worldSuspended = true;  // r15：数据回调不得借旧 commit 重建全纸面 holder
                _worldPhysicalPaper = null;
                _overviewApplied = false;
                _overviewLandCount = -1;
                _viewportCycle = null;   // 重开/换 owner 后从零请求新的有界周期
            }
            catch (Exception e)
            {
                MapIconLog.Once("teardown-" + e.GetType().Name, "teardown failed: " + e.Message);
            }
        }

        /// <summary>只撤自有资源 icons（holder 销毁 + 原生动态槽还原 + 自有 source/assets 归还），
        /// **不动**扩展地图必需几何（不动 cluster 变换与 paper/mask 扩张）。</summary>
        internal static void WithdrawIcons()
        {
            try
            {
                for (int i = 0; i < Views.Count; i++)
                {
                    MapMountIconView view = Views[i];
                    if (view != null) DropView(view, true);
                }
                Views.Clear();
                MapIconSources.Reset();
                // r6：Views 已销毁之后再归还有自 source/assets（只销毁自有对象，原生模板不动）。
                MapCustomIconAssets.Reset();
            }
            catch (Exception e)
            {
                MapIconLog.Once("withdraw-" + e.GetType().Name, "icon withdraw failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ presentation (r14)

        /// <summary>world0 只显示群岛：OnMenuTick 顶部调用（**先于** world/animating/scrolled/frame 等一切早退）。
        /// 只按当前权威 owner（Menu.Inst.ActiveMap）驱动：迟到/外部 sender 既不 bind 也不撤当前 owner 的组；
        /// 已捕获 owner 失效（换 map/holder/campaign/scene/land 或读 fault）才撤自有 effect（fail-closed）。
        /// 稳定态 O(1)、无层级扫描；只在目标态变化时写自有组。</summary>
        internal static void SyncOverviewPresentation(MapTimelineMenuGreece greek)
        {
            try
            {
                MapTimelineMenu active = null;
                bool activeFault = false;
                try
                {
                    Menu menuInst = Menu.Inst;
                    if (menuInst != null) active = menuInst.ActiveMap;
                }
                catch (Exception) { activeFault = true; }

                // 已绑定 owner 是否仍是当前有效 owner；否则只撤**自己**的 effect（绝不动别人 owner）。
                if (_presentationMenu != null && !CapturedOwnerStillCurrent(active, activeFault))
                {
                    ReleaseOwnGroup();
                    ClearAttemptMemo();
                }

                // 输入 sender 不是当前 owner（迟到旧 menu/外部 tick/未知）：不 bind、不撤当前组。
                if (activeFault || greek == null || active != greek) return;

                int worldState = -1;
                bool readFault = false;
                try { worldState = (int)greek._openWorldMapState; }
                catch (Exception) { readFault = true; }

                bool scopeActive = false;
                if (!readFault)
                {
                    try
                    {
                        // 与 tick 几何门一致：icons ON；或 OFF 但缓存登记扩展仍可用（current/visited11 必要几何）。
                        scopeActive = FeatureEnabled();
                        if (!scopeActive && _overviewExtensionLand != null) scopeActive = IsExtensionAccessAvailable();
                    }
                    catch (Exception) { readFault = true; }
                }

                bool bound = _presentationGroup != null;
                if (!bound && !readFault && scopeActive &&
                    MapOverviewPresentationPolicy.TryDesiredVisible(worldState, out _) &&
                    PresentationAttemptNeeded(greek))
                {
                    bound = TryBindPresentation(greek);
                }

                MapPresentationAction action = MapOverviewPresentationPolicy.Decide(readFault, scopeActive, bound,
                    _presentationRejected, bound, worldState, _presentationHidden,
                    out MapPresentationReason reason);
                if (action == MapPresentationAction.Hide)
                {
                    if (!ApplyPresentationHidden()) RejectPresentationTuple();
                }
                else if (action == MapPresentationAction.Show)
                {
                    if (!ApplyPresentationNeutral()) RejectPresentationTuple();
                }
                else if (action == MapPresentationAction.Withdraw)
                {
                    // 结构拒绝（外部组/未知树/被 memo 拒绝）保持 memo；状态/scope/读 fault 类撤销允许重绑。
                    bool keepMemo = reason == MapPresentationReason.ExternalGroup ||
                        reason == MapPresentationReason.BindingInvalid;
                    ReleaseOwnGroup();
                    if (!keepMemo) ClearAttemptMemo();
                }

                if (reason == MapPresentationReason.ExternalGroup)
                {
                    MapIconLog.Once("presentation-external-group",
                        "lands holder already has a foreign CanvasGroup; own overview hiding stays off");
                }
                else if (reason == MapPresentationReason.BindingInvalid)
                {
                    MapIconLog.Once("presentation-binding-invalid", "overview presentation skipped: binding invalid");
                }
                else if (reason == MapPresentationReason.UnknownState)
                {
                    MapIconLog.Once("presentation-unknown-state",
                        "overview presentation restored: unknown world state=" + worldState);
                }
                else if (reason == MapPresentationReason.ReadFault)
                {
                    MapIconLog.Once("presentation-read-fault", "overview presentation restored: state/scope read fault");
                }
            }
            catch (Exception e)
            {
                MapIconLog.Once("presentation-sync-" + e.GetType().Name, "overview presentation sync failed: " + e.Message);
                try
                {
                    ReleaseOwnGroup();       // 意外异常也绝不留下永久 alpha0；拒绝 tuple 防下一帧热重建
                    RejectPresentationTuple();
                }
                catch (Exception) { }
            }
        }

        /// <summary>ClearLands postfix（现成第三方 hook 调用）：exact owner 的展示组即刻中性化并销毁，
        /// 避免同一 holder 重建详情时旧组继续压制；其它 owner 的清理不影响本绑定。</summary>
        internal static void OnMenuLandsCleared(MapTimelineMenuGreece menu)
        {
            try
            {
                if (menu == null) return;
                // exact owner：捕获的或已尝试的（拒绝后 _presentationMenu 已清）任一匹配都失效 memo；
                // 别的 owner 的 Clear 不动当前 owner 的组/memo。
                if (menu != _presentationMenu && menu != _attemptMenu) return;
                ReleaseOwnGroup();
                _presentationGeneration++;               // 详情重建代次：允许同 holder 重建后重试一次
                ClearAttemptMemo();
            }
            catch (Exception e)
            {
                MapIconLog.Once("presentation-cleared-" + e.GetType().Name, "overview presentation clear failed: " + e.Message);
            }
        }

        /// <summary>已捕获 owner 是否仍是当前有效 owner（O(1) 身份/存活；任何 fault → 视为失效 → 撤自有 effect）。</summary>
        private static bool CapturedOwnerStillCurrent(MapTimelineMenu active, bool activeFault)
        {
            try
            {
                if (activeFault || active == null || active != _presentationMenu) return false;
                if (_presentationMap == null || _presentationHolder == null) return false;
                if (_presentationMenu._mainMap != _presentationMap) return false;
                if (_presentationMenu._landsHolder != _presentationHolder) return false;
                if (CurrentCampaignPointer() != _presentationCampaign) return false;
                if (!TryReadSceneIdentity(_presentationHolder, out int holderScene) ||
                    holderScene != _presentationScene) return false;
                if (!TryReadSceneIdentity(_presentationMenu.transform, out int menuScene) ||
                    menuScene != _presentationScene) return false;
                if (!TryReadSceneIdentity(_presentationMap.transform, out int mapScene) ||
                    mapScene != _presentationScene) return false;
                if (CurrentLandOf() != _presentationLand) return false;
                if (CurrentReignOf() != _presentationReign) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>同一身份 tuple（menu/holder/campaign/scene/land）只允许一次结构校验/扫描；tuple 不变则沿用
        /// 上次结论（含拒绝）。不读 List.Count、不扫描层级。</summary>
        private static bool PresentationAttemptNeeded(MapTimelineMenuGreece greek)
        {
            try
            {
                UIMainMap map = greek._mainMap;
                RectTransform holder = greek._landsHolder;
                IntPtr campaign = CurrentCampaignPointer();
                int scene = TryReadSceneIdentity(holder, out int holderScene) ? holderScene : -1;
                int land = CurrentLandOf();
                int reign = CurrentReignOf();
                if (_attemptMenu == greek && _attemptMap == map && _attemptHolder == holder &&
                    _attemptCampaign == campaign && _attemptScene == scene && _attemptLand == land &&
                    _attemptReign == reign && _attemptGeneration == _presentationGeneration)
                {
                    return false;   // 同身份 tuple：沿用结论（含拒绝），不重扫
                }
                _attemptMenu = greek;
                _attemptMap = map;
                _attemptHolder = holder;
                _attemptCampaign = campaign;
                _attemptScene = scene;
                _attemptLand = land;
                _attemptReign = reign;
                _attemptGeneration = _presentationGeneration;
                _presentationRejected = false;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void ClearAttemptMemo()
        {
            _attemptMenu = null;
            _attemptMap = null;
            _attemptHolder = null;
            _attemptCampaign = IntPtr.Zero;
            _attemptScene = -1;
            _attemptLand = int.MinValue;
            _attemptReign = int.MinValue;
            _attemptGeneration = -1;
            _presentationRejected = false;
        }

        /// <summary>写失败/结构拒绝：保持拒绝 tuple（同身份不再热重建），直到 Clear/换代/换 owner。</summary>
        private static void RejectPresentationTuple()
        {
            ReleaseOwnGroup();
            _presentationRejected = true;
        }

        private static IntPtr CurrentCampaignPointer()
        {
            try
            {
                CampaignSaveData campaign = CampaignSaveData.current;
                return campaign != null ? campaign.Pointer : IntPtr.Zero;
            }
            catch (Exception) { return IntPtr.Zero; }
        }

        private static int CurrentLandOf()
        {
            try
            {
                CampaignSaveData campaign = CampaignSaveData.current;
                return campaign != null ? campaign.CurrentLand : int.MinValue;
            }
            catch (Exception) { return int.MinValue; }
        }

        /// <summary>campaign.reign：稳定 int 身份（不使用每次读取重新装箱的 currentReign 代理）。</summary>
        private static int CurrentReignOf()
        {
            try
            {
                CampaignSaveData campaign = CampaignSaveData.current;
                return campaign != null ? campaign.reign : int.MinValue;
            }
            catch (Exception) { return int.MinValue; }
        }

        /// <summary>scene 身份：必须 valid+loaded 才返回 true；无效/未加载场景不得以 handle/哨兵等值通过。</summary>
        private static bool TryReadSceneIdentity(Transform transform, out int handle)
        {
            handle = -1;
            try
            {
                if (transform == null || transform.gameObject == null) return false;
                Scene scene = transform.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) return false;
                handle = scene.handle;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>祖先判定三态：Under/Outside/Unknown（读 fault 不算 Outside）。深度上限内有界。</summary>
        private enum Ancestry
        {
            Unknown = 0,
            Under = 1,
            Outside = 2,
        }

        private static Ancestry CheckAncestry(Transform node, Transform ancestor, int maxDepth)
        {
            try
            {
                if (node == null || ancestor == null) return Ancestry.Unknown;
                Transform cursor = node;
                for (int depth = 0; depth <= maxDepth; depth++)
                {
                    if (cursor == null) return Ancestry.Outside;   // 自然走完：可证明 Outside
                    if (cursor == ancestor) return Ancestry.Under;
                    cursor = cursor.parent;
                }
                return Ancestry.Unknown;   // 深度截止仍非 null：不可证明 Outside（fail-closed）
            }
            catch (Exception) { return Ancestry.Unknown; }
        }

        /// <summary>一次性绑定：exact ActiveMap 已由 caller 保证；再核 scroller/结构/身份/外部组/后代组；全部通过
        /// 才创建自有 CanvasGroup（中性起步）。任何 unknown/fault 一律拒绝并记 tuple（不接管）。</summary>
        private static bool TryBindPresentation(MapTimelineMenuGreece greek)
        {
            RectTransform holder = null;
            try
            {
                holder = greek._landsHolder;
                if (holder == null || holder.gameObject == null)
                {
                    MapIconLog.Once("presentation-no-holder", "overview presentation skipped: no lands holder");
                    return false;
                }

                UIMainMap map = greek._mainMap;
                if (map == null || map.gameObject == null)
                {
                    MapIconLog.Once("presentation-no-map", "overview presentation skipped: no owned main map");
                    return false;
                }

                // 身份 capture：campaign ptr + 稳定 reign/land int + 同 scene（valid+loaded）；
                // 任一读不到/无效/不同一 → 拒绝，绝不拿哨兵等值通过。
                CampaignSaveData campaign = CampaignSaveData.current;
                if (campaign == null)
                {
                    MapIconLog.Once("presentation-no-campaign", "overview presentation skipped: no current campaign");
                    return false;
                }
                IntPtr campaignPtr = campaign.Pointer;
                int land = campaign.CurrentLand;
                int reign = campaign.reign;
                if (!TryReadSceneIdentity(greek.transform, out int menuScene) ||
                    !TryReadSceneIdentity(map.transform, out int mapScene) ||
                    !TryReadSceneIdentity(holder, out int holderScene))
                {
                    MapIconLog.Once("presentation-scene-identity",
                        "overview presentation skipped: scene identity invalid/unloaded");
                    return false;
                }
                if (menuScene != mapScene || menuScene != holderScene)
                {
                    MapIconLog.Once("presentation-scene-mismatch",
                        "overview presentation skipped: menu/map/holder not in the same scene");
                    return false;
                }
                int scene = holderScene;

                if (!ValidateDetailBranch(greek, map, holder, out string structureReason))
                {
                    MapIconLog.Once("presentation-structure-" + structureReason,
                        "overview presentation skipped: " + structureReason);
                    return false;
                }

                CanvasGroup existing = null;
                try { existing = holder.gameObject.GetComponent<CanvasGroup>(); }
                catch (Exception e)
                {
                    // 读不到不得当 null 继续 Add：拒绝本 tuple。
                    MapIconLog.Once("presentation-holder-get-" + e.GetType().Name,
                        "overview presentation skipped: holder CanvasGroup read failed: " + e.Message);
                    return false;
                }
                if (existing != null)
                {
                    MapIconLog.Once("presentation-external-group-bound",
                        "overview presentation skipped: holder already has a foreign CanvasGroup");
                    return false;
                }

                if (HasIgnoringDescendantGroup(holder, out string descendantReason))
                {
                    MapIconLog.Once("presentation-descendant-" + descendantReason,
                        "overview presentation skipped: descendant canvas group " + descendantReason);
                    return false;
                }

                CanvasGroup group = null;
                try
                {
                    group = holder.gameObject.AddComponent<CanvasGroup>();
                    if (group == null) return false;
                    group.ignoreParentGroups = false;
                    group.interactable = true;
                    group.alpha = MapOverviewPresentationPolicy.NeutralAlpha;
                    group.blocksRaycasts = MapOverviewPresentationPolicy.NeutralBlocksRaycasts;
                }
                catch (Exception e)
                {
                    MapIconLog.Once("presentation-create-" + e.GetType().Name,
                        "overview presentation group create failed: " + e.Message);
                    if (group != null) { try { UnityEngine.Object.Destroy(group); } catch (Exception) { } }
                    return false;
                }

                _presentationMenu = greek;
                _presentationMap = map;
                _presentationHolder = holder;
                _presentationGroup = group;
                _presentationHidden = false;
                _presentationCampaign = campaignPtr;
                _presentationScene = scene;
                _presentationLand = land;
                _presentationReign = reign;
                MapIconLog.Once("presentation-bound", "overview presentation group bound to exact lands holder");
                return true;
            }
            catch (Exception e)
            {
                MapIconLog.Once("presentation-bind-" + e.GetType().Name,
                    "overview presentation bind failed: " + e.Message);
                return false;
            }
        }

        /// <summary>结构校验（一次性有界）：holder 必须在 menu.landScroller 下；map 也在 scroller 下；menu.lands
        /// 的非空详情都在 holder 下；_mainMap 与总览簇都在 holder 外（含超界拒绝与读 fault 拒绝）。</summary>
        private static bool ValidateDetailBranch(MapTimelineMenuGreece greek, UIMainMap map, RectTransform holder,
            out string reason)
        {
            reason = null;
            Transform holderTransform = holder;

            RectTransform scroller;
            try { scroller = greek.landScroller; }
            catch (Exception) { reason = "scroller-read-fault"; return false; }
            if (scroller == null) { reason = "no-scroller"; return false; }
            if (CheckAncestry(holderTransform, scroller, 12) != Ancestry.Under)
            {
                reason = "holder-not-under-scroller";
                return false;
            }
            if (map.gameObject != null && CheckAncestry(map.gameObject.transform, scroller, 12) != Ancestry.Under)
            {
                reason = "map-not-under-scroller";
                return false;
            }

            Il2CppSystem.Collections.Generic.List<UILand> lands;
            int count;
            try
            {
                lands = greek.lands;
                if (lands == null) { reason = "lands-null"; return false; }
                count = lands.Count;
            }
            catch (Exception) { reason = "lands-read-fault"; return false; }
            if (count > 64) { reason = "lands-too-many"; return false; }
            for (int i = 0; i < count; i++)
            {
                UILand land;
                try { land = lands[i]; }
                catch (Exception) { reason = "land-read-fault-" + i; return false; }
                if (land == null) continue;
                Ancestry ancestry = CheckAncestry(land.transform, holderTransform, 12);
                if (ancestry != Ancestry.Under) { reason = "detail-" + ancestry + "-" + i; return false; }
            }

            if (map.gameObject != null)
            {
                Ancestry mapAncestry = CheckAncestry(map.gameObject.transform, holderTransform, 12);
                if (mapAncestry != Ancestry.Outside) { reason = "map-" + mapAncestry + "-under-holder"; return false; }
            }
            Il2CppReferenceArray<UILand> mapLands;
            try { mapLands = map._lands; }
            catch (Exception) { reason = "map-lands-read-fault"; return false; }
            if (mapLands == null) { reason = "map-lands-null"; return false; }
            int mapCount;
            try { mapCount = mapLands.Length; }
            catch (Exception) { reason = "map-lands-length-fault"; return false; }
            if (mapCount > 24) { reason = "map-lands-too-many"; return false; }
            for (int i = 0; i < mapCount; i++)
            {
                UILand cluster;
                try { cluster = mapLands[i]; }
                catch (Exception) { reason = "map-land-read-fault-" + i; return false; }
                if (cluster == null) continue;
                Ancestry ancestry = CheckAncestry(cluster.transform, holderTransform, 12);
                if (ancestry != Ancestry.Outside) { reason = "overview-" + ancestry + "-" + i; return false; }
            }
            return true;
        }

        /// <summary>后代 CanvasGroup（**含 inactive**）：>256 或读异常 ⇒ 整笔拒绝（不能只核前 256 就当全过）；
        /// 存在 ignoreParentGroups=true ⇒ 拒绝（父 alpha 遮不住整支）。</summary>
        private static bool HasIgnoringDescendantGroup(RectTransform holder, out string reason)
        {
            reason = null;
            CanvasGroup[] groups;
            try { groups = holder.gameObject.GetComponentsInChildren<CanvasGroup>(true); }
            catch (Exception e)
            {
                reason = "scan-" + e.GetType().Name;
                return true;
            }
            if (groups == null) { reason = "scan-null"; return true; }
            if (groups.Length > 256) { reason = "scan-overcap-" + groups.Length; return true; }
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null) continue;
                if (group.ignoreParentGroups) { reason = "ignore-parent-groups"; return true; }
            }
            return false;
        }

        /// <summary>隐藏：任何 setter 异常 ⇒ false（caller 转入 reject/释放，绝不留在半隐藏态）。</summary>
        private static bool ApplyPresentationHidden()
        {
            CanvasGroup group = _presentationGroup;
            if (group == null || _presentationHidden) return true;
            try
            {
                group.alpha = MapOverviewPresentationPolicy.HiddenAlpha;
                group.blocksRaycasts = MapOverviewPresentationPolicy.HiddenBlocksRaycasts;
                _presentationHidden = true;
                return true;
            }
            catch (Exception e)
            {
                MapIconLog.Once("presentation-hide-" + e.GetType().Name, "overview presentation hide failed: " + e.Message);
                return false;
            }
        }

        /// <summary>中性（state1/清理前）：逐项独立尝试（一项异常不跳过其余），任一项失败 ⇒ false。
        /// alpha1 乘数不改变原 native outline 内部动画；interactable 保持 true 不改 Selectable 资格。</summary>
        private static bool ApplyPresentationNeutral()
        {
            CanvasGroup group = _presentationGroup;
            if (group == null) return true;
            bool ok = true;
            try { if (group.alpha != MapOverviewPresentationPolicy.NeutralAlpha) group.alpha = MapOverviewPresentationPolicy.NeutralAlpha; }
            catch (Exception e) { ok = false; MapIconLog.Once("presentation-show-a-" + e.GetType().Name, "neutral alpha failed: " + e.Message); }
            try { if (!group.blocksRaycasts) group.blocksRaycasts = MapOverviewPresentationPolicy.NeutralBlocksRaycasts; }
            catch (Exception e) { ok = false; MapIconLog.Once("presentation-show-b-" + e.GetType().Name, "neutral raycast failed: " + e.Message); }
            try { if (!group.interactable) group.interactable = true; }
            catch (Exception e) { ok = false; MapIconLog.Once("presentation-show-c-" + e.GetType().Name, "neutral interactable failed: " + e.Message); }
            try { if (group.ignoreParentGroups) group.ignoreParentGroups = false; }
            catch (Exception e) { ok = false; MapIconLog.Once("presentation-show-d-" + e.GetType().Name, "neutral ignore-parent failed: " + e.Message); }
            if (ok) _presentationHidden = false;
            return ok;
        }

        /// <summary>释放自有组：清绑定引用 + 逐项独立尝试中性化 + **一定** Destroy（一步异常不跳过销毁）。
        /// 只动捕获的那个组件，绝不重新 resolve holder 代清。校验 memo 由 caller 决定是否清。</summary>
        private static void ReleaseOwnGroup()
        {
            CanvasGroup group = _presentationGroup;
            _presentationMenu = null;
            _presentationMap = null;
            _presentationHolder = null;
            _presentationGroup = null;
            _presentationHidden = false;
            _presentationCampaign = IntPtr.Zero;
            _presentationScene = -1;
            _presentationLand = int.MinValue;
            _presentationReign = int.MinValue;
            if (group == null) return;
            try { group.alpha = MapOverviewPresentationPolicy.NeutralAlpha; } catch (Exception) { }
            try
            {
                if (!group.blocksRaycasts) group.blocksRaycasts = MapOverviewPresentationPolicy.NeutralBlocksRaycasts;
            }
            catch (Exception) { }
            try { if (!group.interactable) group.interactable = true; } catch (Exception) { }
            try { UnityEngine.Object.Destroy(group); }
            catch (Exception e)
            {
                MapIconLog.Once("presentation-destroy-" + e.GetType().Name,
                    "overview presentation destroy failed: " + e.Message);
            }
        }

        /// <summary>我们的地图 scope 结束（tick OFF 分支 / 同 owner 失效路径）：释放展示组 + 几何回收。</summary>
        private static void WithdrawPresentation()
        {
            ReleaseOwnGroup();
            ClearAttemptMemo();
        }

        // ------------------------------------------------------------------ ownership

        // ------------------------------------------------------------------ ownership

        /// <summary>land 是否属于 Greek 菜单的两套 land（menu.lands / menu._mainMap._lands）。</summary>
        private static bool TryGetOwnedMenu(UILand land, out MapTimelineMenuGreece menu, out bool overview)
        {
            menu = FindAncestor<MapTimelineMenuGreece>(land.transform);
            overview = false;
            if (menu == null) return false;
            try
            {
                Il2CppSystem.Collections.Generic.List<UILand> lands = menu.lands;
                if (lands != null)
                {
                    for (int i = 0; i < lands.Count; i++)
                    {
                        if (lands[i] == land) return true;
                    }
                }

                UIMainMap map = menu._mainMap;
                if (map == null) return false;
                Il2CppReferenceArray<UILand> mapLands = map._lands;
                if (mapLands == null) return false;
                for (int i = 0; i < mapLands.Length; i++)
                {
                    if (mapLands[i] == land)
                    {
                        overview = true;
                        return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        // ------------------------------------------------------------------ data

        /// <summary>只读当次 reign；任何不可用（null/越界/entry null）都返回 false，绝不回退 currentReign。</summary>
        private static bool TryReadLand(CampaignSaveData.ReignInfo reign, int land,
            out int steedCount, out int hermitCount, out int statueCount)
        {
            steedCount = hermitCount = statueCount = 0;
            try
            {
                if (reign == null) return false;
                Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
                if (landData == null || land < 0 || land >= landData.Count) return false;

                CampaignSaveData.LandMapData entry = landData[land];
                if (entry == null) return false;
                steedCount = CopyInto(entry.steedSpawns, SteedBuffer);
                hermitCount = CopyInto(entry.hermit, HermitBuffer);
                statueCount = CopyInto(entry.statue, StatueBuffer);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int CopyInto(Il2CppStructArray<SteedType> source, int[] buffer)
        {
            if (source == null) return 0;
            int count = Math.Min(source.Length, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = (int)source[i];
            return count;
        }

        private static int CopyInto(Il2CppStructArray<Hermit.HermitType> source, int[] buffer)
        {
            if (source == null) return 0;
            int count = Math.Min(source.Length, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = (int)source[i];
            return count;
        }

        private static int CopyInto(Il2CppStructArray<Statue.Deity> source, int[] buffer)
        {
            if (source == null) return 0;
            int count = Math.Min(source.Length, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = (int)source[i];
            return count;
        }

        /// <summary>动态槽接管：压制三类资源 UIDynamicMapIcon 的原生 _spawnedIcon（可还原）。</summary>
        private static void SuppressNativeDynamicIcons(UILand land, MapMountIconView view)
        {
            Il2CppReferenceArray<UIDynamicMapIcon> hosts = null;
            try { hosts = land._dynamicMapIcons; } catch (Exception) { }
            if (hosts == null) return;
            for (int i = 0; i < hosts.Length; i++)
            {
                UIDynamicMapIcon host = hosts[i];
                if (host == null) continue;
                try
                {
                    int dyn = (int)host._type;
                    if (dyn != DynSteed && dyn != DynHermit && dyn != DynStatue) continue;
                    UIMapIcon spawned = host._spawnedIcon;
                    if (spawned == null) continue;
                    GameObject target = spawned.gameObject;
                    if (target == null) continue;

                    bool known = false;
                    for (int s = 0; s < view.Suppressed.Count; s++)
                    {
                        if (view.Suppressed[s].Target == target) { known = true; break; }
                    }
                    if (!known)
                    {
                        view.Suppressed.Add(new SuppressedIcon { Target = target, OriginalActive = target.activeSelf });
                    }
                    if (target.activeSelf) target.SetActive(false);
                }
                catch (Exception) { }
            }
        }

        private static void RestoreSuppressed(MapMountIconView view)
        {
            for (int i = 0; i < view.Suppressed.Count; i++)
            {
                SuppressedIcon item = view.Suppressed[i];
                if (item.Target == null) continue;
                try
                {
                    if (item.Target.activeSelf != item.OriginalActive) item.Target.SetActive(item.OriginalActive);
                }
                catch (Exception) { }
            }
            view.Suppressed.Clear();
        }

        /// <summary>全部条目（接管模式下三类动态槽不再抵扣）；缺资产按精确 (iconType,type) 记一次日志。</summary>
        private static void BuildRequests(UILand land, int steedCount, int hermitCount, int statueCount,
            out List<MapIconRequest> requests, out List<UIMapIcon> sources)
        {
            requests = RequestScratch;
            sources = SourceScratch;
            requests.Clear();
            sources.Clear();

            AddRequests(land, MapIconKind.Steed, TypeSteed, SteedBuffer, steedCount, requests, sources);
            AddRequests(land, MapIconKind.Hermit, TypeHermit, HermitBuffer, hermitCount, requests, sources);
            AddRequests(land, MapIconKind.Statue, TypeStatue, StatueBuffer, statueCount, requests, sources);
        }

        private static void AddRequests(UILand land, MapIconKind kind, int iconType, int[] values, int count,
            List<MapIconRequest> requests, List<UIMapIcon> sources)
        {
            for (int i = 0; i < count; i++)
            {
                int type = values[i];
                if (type < 0) continue;

                UIMapIcon source = MapIconSources.Resolve(land, iconType, type);
                if (source == null)
                {
                    // 精确缺口（如 3/4/38）：记录一次并保持原生"无此图标"语义，不替换。
                    MapIconLog.Once("missing-" + iconType + "-" + type,
                        "native map icon absent for " + kind + " type=" + type + " (iconType=" + iconType +
                        "); entry stays native-absent");
                    continue;
                }

                if (!TryIconSize(source, out float w, out float h))
                {
                    MapIconLog.Once("size-" + iconType + "-" + type, "icon size unreadable type=" + type);
                    continue;
                }
                requests.Add(new MapIconRequest(kind, type, i, w, h));
                sources.Add(source);
            }
        }

        private static bool TryIconSize(UIMapIcon icon, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            try
            {
                RectTransform rect = icon.gameObject.GetComponent<RectTransform>();
                if (rect == null) return false;
                Vector2 size = rect.sizeDelta;
                Vector3 scale = rect.localScale;
                width = Mathf.Abs(size.x * scale.x);
                height = Mathf.Abs(size.y * scale.y);
                return width > 0.5f && height > 0.5f;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// r15：扩展簇专属规划——两行（优先 0.7，允许 0.6/0.42/0.36 降级）铺在 banner 内；
        /// 只有 exact11 自己的 terrain/outline/Button 背景不算障碍，其余 native 图形（船标/灯塔/状态）仍是障碍。
        /// 放不下 → failed&gt;0，调用方整体 fallback（绝不部分显示/截断）。
        /// </summary>
        private static void PlanExtensionIsland(RectTransform paper, OverviewEntry entry,
            List<MapIconRequest> requests, List<MapIconPlacement> placements, out float usedScale, out int failed)
        {
            usedScale = 0f;
            failed = requests.Count;
            if (paper == null || entry == null || entry.BannerArea.Width <= 1f) return;

            var native = new List<MapIconBox>(240);
            CollectNativeBoxes(paper, paper, native, true, _bannerExcludedRects);
            var blockers = new List<MapIconBox>(32);
            for (int i = 0; i < native.Count; i++)
            {
                if (BoxesIntersect(native[i], entry.BannerArea, 0.01f)) blockers.Add(native[i]);
            }

            MapIconBox reserved = MapWorldLayout.IconAreaOf(entry.BannerArea, true, StatusLaneWidth);
            MapIconBox full = MapWorldLayout.IconAreaOf(entry.BannerArea, false, 0f);
            // R5/独立实测：reserved 先成功会遮蔽 full 的更高可读 scale（300/314 下 reserved .5/.6 vs full .6/.7）。
            // 两个候选都用真实 blockers 规划，取**真实可达 scale 更高**者；相同 scale 保持 reserved 优先。
            // 不隐藏/删除 boat blocker、不重叠缩截：两候选都是完整 16 项合法解，只比较可读倍率。
            var reservedPlacements = new List<MapIconPlacement>(requests.Count);
            bool reservedOk = MapExtensionIslandLayout.TryPlan(reserved, requests, blockers, reservedPlacements,
                out float reservedScale, out _);
            if (reservedOk)
            {
                var fullPlacements = new List<MapIconPlacement>(requests.Count);
                bool fullOk = MapExtensionIslandLayout.TryPlan(full, requests, blockers, fullPlacements,
                    out float fullScale, out _);
                if (fullOk && fullScale > reservedScale + 0.0001f)
                {
                    placements.AddRange(fullPlacements);
                    usedScale = fullScale;
                    failed = 0;
                    return;
                }
                placements.AddRange(reservedPlacements);
                usedScale = reservedScale;
                failed = 0;
                return;
            }
            if (MapExtensionIslandLayout.TryPlan(full, requests, blockers, placements, out usedScale, out failed))
            {
                return;
            }
            placements.Clear();
            MapIconLog.Warn("extension island capacity failed: requests=" + requests.Count +
                " banner=[" + Fmt(entry.BannerArea) + "] blockers=" + blockers.Count +
                "; display suppressed (no partial)");
        }

        // ------------------------------------------------------------------ rebuild

        private static void Rebuild(MapMountIconView view, UILand land, int landIndex, bool overview,
            MapTimelineMenuGreece menu, CampaignSaveData.ReignInfo reign,
            List<MapIconRequest> requests, List<UIMapIcon> sources, int fingerprint)
        {
            DestroyHolder(view);

            RectTransform paper = overview && menu != null && menu._mainMap != null
                ? menu._mainMap.gameObject.GetComponent<RectTransform>()
                : null;
            RectTransform space = overview ? paper : land.gameObject.GetComponent<RectTransform>();
            if (space == null) return;

            List<MapIconPlacement> placements = new List<MapIconPlacement>(requests.Count);
            float scale;
            int failed;

            if (overview)
            {
                PlanOverviewIsland(paper, landIndex, requests, placements, out scale, out failed);
            }
            else
            {
                // 详情：land 矩形 + 适度外边距（真实 LandsHolder 294×186 内），以 holder 实界为布局上界。
                RectTransform nativeHolder = space.parent as RectTransform;
                var surfaceBox = new MapIconBox(-DetailMarginX, -DetailMarginY,
                    space.rect.width + DetailMarginX, space.rect.height + DetailMarginY);
                if (nativeHolder != null && TryLocalBox(space, nativeHolder, out MapIconBox holderBox))
                {
                    surfaceBox = new MapIconBox(
                        Mathf.Max(surfaceBox.X0, holderBox.X0), Mathf.Max(surfaceBox.Y0, holderBox.Y0),
                        Mathf.Min(surfaceBox.X1, holderBox.X1), Mathf.Min(surfaceBox.Y1, holderBox.Y1));
                }
                var surface = new MapIconSurface(surfaceBox.X0, surfaceBox.Y0, surfaceBox.X1, surfaceBox.Y1);
                var blocked = new List<MapIconBox>(240);
                // 详情 view 根 = 该 land：其他岛是同级兄弟，无论隐藏与否都不算本岛障碍（r4）。
                CollectNativeBoxes(space, land.transform, blocked, true);
                for (int i = 0; i < blocked.Count; i++) surface.AddBlocked(blocked[i]);
                MapResourceIconPlanner.TryPlan(requests, surface, placements, out scale, out failed);
            }

            // 只接受全量；部分结果一律丢弃并记录（绝不部分贴图当成功）。
            if (failed > 0 || placements.Count != requests.Count)
            {
                MapIconLog.Warn("icon plan incomplete land=" + landIndex + " overview=" + overview +
                    " placed=" + placements.Count + "/" + requests.Count + " scale=" + scale.ToString("0.00") +
                    "; display suppressed (no partial)");
                view.Fingerprint = int.MinValue;
                view.FailedFingerprint = fingerprint;
                view.LayoutVersion = _overviewGeometryVersion;
                // 超容量 fallback（root 已裁决，不再请求用户）：还原原生动态槽，岛保持原生可用；
                // 完整真实资源列表由详情页提供（详情容量已按 15/16 最坏集覆盖）。
                RestoreSuppressed(view);
                MapIconLog.Info("native dynamic slots restored land=" + landIndex + " overview=" + overview +
                    "; full list stays available in detail");
                return;
            }
            if (placements.Count == 0) return;

            GameObject holder = new GameObject("KEM_MapResourceIcons");
            holder.SetActive(false);
            RectTransform holderRect = holder.AddComponent<RectTransform>();
            holderRect.SetParent(space, false);
            holderRect.anchorMin = new Vector2(0f, 0f);
            holderRect.anchorMax = new Vector2(0f, 0f);
            holderRect.pivot = new Vector2(0f, 0f);
            holderRect.localScale = Vector3.one;
            holderRect.localRotation = Quaternion.identity;
            if (overview)
            {
                // 总览：一个岛的图标可能分布在多个自由区域（均为 paper 坐标），根直接铺满 paper。
                holderRect.anchoredPosition = new Vector2(0f, 0f);
                holderRect.sizeDelta = new Vector2(space.rect.width, space.rect.height);
            }
            else
            {
                holderRect.anchoredPosition = new Vector2(0f, 0f);
                holderRect.sizeDelta = new Vector2(space.rect.width, space.rect.height);
            }
            view.Holder = holder;

            view.Icons.Clear();
            for (int i = 0; i < placements.Count; i++)
            {
                MapIconPlacement placement = placements[i];
                int sourceIndex = placement.RequestIndex;
                UIMapIcon source = sourceIndex >= 0 && sourceIndex < sources.Count ? sources[sourceIndex] : null;
                UIMapIcon clone = SpawnIcon(source, holderRect, placement);
                if (clone != null) view.Icons.Add(clone);
            }
            if (view.Icons.Count != requests.Count)
            {
                // spawn 失败（异常/空源）同样不作为成功：整体丢弃，下一次刷新重试。
                MapIconLog.Warn("icon spawn incomplete land=" + landIndex +
                    " spawned=" + view.Icons.Count + "/" + requests.Count + "; display suppressed");
                DestroyHolder(view);
                view.Fingerprint = int.MinValue;
                view.FailedFingerprint = fingerprint;
                view.LayoutVersion = _overviewGeometryVersion;
                RestoreSuppressed(view);
                return;
            }

            holder.SetActive(!view.HiddenByNativeGate);
            Repaint(view, landIndex, reign);
            view.Fingerprint = fingerprint;
            view.FailedFingerprint = int.MinValue;
            view.LayoutVersion = _overviewGeometryVersion;
            MapIconLog.Info("icons built land=" + landIndex + " overview=" + overview +
                " count=" + view.Icons.Count + " scale=" + scale.ToString("0.00"));
        }

        /// <summary>
        /// 总览排版（r7）：图标放入本岛"最近岛"自由区域（MapIconRegionPlanner 产出的多个矩形，paper 坐标），
        /// 自顶向下贴岛缘；同一岛内跨矩形共享同一档缩放（1.0 → 0.36 逐档降），
        /// 全部放下才算成功；任何一档放不下即 failed>0（调用方不得部分展示）。
        /// **绝不移动/缩放岛来腾地方**——岛的目标位置只由 MapOverviewLayout 的统一变换决定。
        /// </summary>
        private static void PlanOverviewIsland(RectTransform paper, int landIndex,
            List<MapIconRequest> requests, List<MapIconPlacement> placements, out float usedScale, out int failed)
        {
            usedScale = 0f;
            failed = requests.Count;
            if (paper == null || requests.Count == 0) { failed = requests.Count; return; }

            OverviewEntry entry = FindOverviewEntry(landIndex);
            if (entry == null) return;
            if (entry.IsExtension)
            {
                PlanExtensionIsland(paper, entry, requests, placements, out usedScale, out failed);
                return;
            }
            if (entry.RegionRects.Count == 0)
            {
                MapIconLog.Once("island-region-" + landIndex,
                    "overview island land=" + landIndex + " has no free icon region; entries stay native-absent");
                return;
            }

            var native = new List<MapIconBox>(240);
            CollectNativeBoxes(paper, paper, native, true);

            var pending = new List<MapIconRequest>(requests.Count);
            var pendingIndex = new List<int>(requests.Count);
            var packed = new List<MapIconPlacement>(requests.Count);
            var merged = new List<MapIconPlacement>(requests.Count);

            for (int s = 0; s < OverviewScaleCount; s++)
            {
                float scale = MapResourceIconPlanner.Scales[s];
                pending.Clear();
                pendingIndex.Clear();
                merged.Clear();
                for (int i = 0; i < requests.Count; i++) { pending.Add(requests[i]); pendingIndex.Add(i); }

                for (int r = 0; r < entry.RegionRects.Count && pending.Count > 0; r++)
                {
                    MapIconBox rect = entry.RegionRects[r];
                    var surface = new MapIconSurface(rect.X0, rect.Y0, rect.X1, rect.Y1);
                    for (int b = 0; b < native.Count; b++)
                    {
                        MapIconBox box = native[b];
                        if (box.X1 <= rect.X0 || box.X0 >= rect.X1 ||
                            box.Y1 <= rect.Y0 || box.Y0 >= rect.Y1) continue;
                        surface.AddBlocked(box);   // surface 即 paper 坐标，遮挡盒同系
                    }

                    MapResourceIconPlanner.TryPlanAtScale(pending, surface, scale, packed, out _, true);
                    if (packed.Count == 0) continue;
                    for (int p = 0; p < packed.Count; p++)
                    {
                        MapIconPlacement placement = packed[p];
                        merged.Add(new MapIconPlacement(placement.Request,
                            pendingIndex[placement.RequestIndex], placement.X, placement.Y, placement.Scale));
                    }
                    // 从高到低移除已放置项，保证下标仍指向正确的 pending 元素。
                    for (int p = packed.Count - 1; p >= 0; p--)
                    {
                        int index = packed[p].RequestIndex;
                        pending.RemoveAt(index);
                        pendingIndex.RemoveAt(index);
                    }
                }

                if (pending.Count == 0)
                {
                    placements.AddRange(merged);
                    usedScale = scale;
                    failed = 0;
                    return;
                }
            }
        }

        /// <summary>按canonical land索引（原生 ui0..9 / 登记扩展 physical11）取布局条目；不再用数组位置反查。</summary>
        private static OverviewEntry FindOverviewEntry(int landIndex)
        {
            for (int i = 0; i < Overview.Count; i++)
            {
                OverviewEntry entry = Overview[i];
                if (entry == null || entry.Rect == null) continue;
                if (entry.LandIndex == landIndex) return entry;
            }
            return null;
        }

        private static UIMapIcon SpawnIcon(UIMapIcon source, RectTransform holder, in MapIconPlacement placement)
        {
            if (source == null) return null;
            try
            {
                UIMapIcon clone = UnityEngine.Object.Instantiate(source, holder, false);
                if (clone == null) return null;

                RectTransform sourceRect = source.gameObject.GetComponent<RectTransform>();
                RectTransform rect = clone.gameObject.GetComponent<RectTransform>();
                if (rect == null) return null;

                Vector3 baseScale = sourceRect != null ? sourceRect.localScale : Vector3.one;
                float s = placement.Scale;
                rect.SetParent(holder, false);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(placement.X, placement.Y);
                rect.localScale = new Vector3(baseScale.x * s, baseScale.y * s, baseScale.z <= 0f ? 1f : baseScale.z);
                rect.localRotation = Quaternion.identity;

                // 资源图标只作显示：清掉 raycast，绝不能拦截原生岛 Button 的选岛点击。
                try
                {
                    Image image = clone.icon;
                    if (image != null) image.raycastTarget = false;
                }
                catch (Exception) { }
                try
                {
                    Text text = clone.text;
                    if (text != null) text.raycastTarget = false;
                }
                catch (Exception) { }

                clone.gameObject.SetActive(true);
                return clone;
            }
            catch (Exception e)
            {
                MapIconLog.Once("spawn-" + e.GetType().Name, "spawn icon failed: " + e.Message);
                return null;
            }
        }

        private static void Repaint(MapMountIconView view, int landIndex, CampaignSaveData.ReignInfo reign)
        {
            for (int i = 0; i < view.Icons.Count; i++)
            {
                UIMapIcon icon = view.Icons[i];
                if (icon == null) continue;
                try { icon.UpdateIcon(reign, landIndex); }
                catch (Exception e)
                {
                    MapIconLog.Once("repaint-" + e.GetType().Name, "icon repaint failed: " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------------ geometry

        private static bool IsClusterUnlocked(UILand land)
        {
            try
            {
                UIMainMapLand cluster = land.gameObject.GetComponent<UIMainMapLand>();
                if (cluster == null) return true;   // 详情 land（无岛簇门）按原生全显
                return cluster.IsUnlocked;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static T FindAncestor<T>(Transform start) where T : Component
        {
            Transform cursor = start != null ? start.parent : null;
            for (int depth = 0; cursor != null && depth < 12; depth++, cursor = cursor.parent)
            {
                T found = cursor.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }

        private static RectTransform FindAncestorByName(Transform start, string name)
        {
            Transform cursor = start != null ? start.parent : null;
            for (int depth = 0; cursor != null && depth < 8; depth++, cursor = cursor.parent)
            {
                if (cursor.name == name) return cursor.GetComponent<RectTransform>();
            }
            return null;
        }

        private static RectTransform FindArtTransform(UILand land)
        {
            Transform t = land.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child == null) continue;
                if (child.name.StartsWith("Land Button", StringComparison.Ordinal))
                    return child.GetComponent<RectTransform>();
            }
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child == null) continue;
                if (child.name.StartsWith("Land Image", StringComparison.Ordinal))
                    return child.GetComponent<RectTransform>();
            }
            return null;
        }

        /// <summary>target 的轴对齐包围盒（space 的矩形坐标；原点=space.rect 左下角，含缩放/翻转）。</summary>
        private static bool TryLocalBox(RectTransform space, RectTransform target, out MapIconBox box)
        {
            box = default;
            if (space == null || target == null) return false;
            try
            {
                Rect r = target.rect;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    float x = (i & 1) == 0 ? r.xMin : r.xMax;
                    float y = (i & 2) == 0 ? r.yMin : r.yMax;
                    Vector3 local = space.InverseTransformPoint(target.TransformPoint(new Vector3(x, y, 0f)));
                    if (local.x < minX) minX = local.x;
                    if (local.x > maxX) maxX = local.x;
                    if (local.y < minY) minY = local.y;
                    if (local.y > maxY) maxY = local.y;
                }
                Rect sr = space.rect;
                box = new MapIconBox(minX - sr.xMin, minY - sr.yMin, maxX - sr.xMin, maxY - sr.yMin);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 可见性判定（r4）：沿父链收集 activeSelf 直到 view 根（**不含**根本身），逐级必须为 true。
        /// 根及其祖先的 inactive 来自分页/菜单隐藏（root 事实：SetFocusToLand 只 SetActive 当前详情岛，
        /// UpdateLands/UpdateLandIcons 仍逐岛调用 UpdateLand），不能当作"该岛没有原生遮挡"。
        /// 根之下原生明确隐藏的单个装饰/资源仍会被尊重。
        /// </summary>
        private static bool VisibleUnderRoot(Transform root, Transform node)
        {
            if (root == null || node == null) return false;
            BoolChain.Clear();
            Transform cursor = node;
            for (int depth = 0; cursor != null && depth < 24; depth++)
            {
                if (cursor == root) return MapIconVisibility.Visible(BoolChain);
                BoolChain.Add(cursor.gameObject.activeSelf);
                cursor = cursor.parent;
            }
            return MapIconVisibility.Visible(BoolChain);
        }

        private static readonly MapIconPlanBoolChain BoolChain = new MapIconPlanBoolChain(16);

        private static void CollectNativeBoxes(RectTransform space, Transform root, List<MapIconBox> into, bool excludeOwn)
            => CollectNativeBoxes(space, root, into, excludeOwn, null);

        private static void CollectNativeBoxes(RectTransform space, Transform root, List<MapIconBox> into,
            bool excludeOwn, List<RectTransform> excluded)
        {
            if (root == null) return;
            try
            {
                RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
                if (rects == null) return;
                for (int i = 0; i < rects.Length; i++)
                {
                    RectTransform rect = rects[i];
                    if (rect == null) continue;
                    if (excludeOwn && IsUnderOwnHolder(rect.transform)) continue;
                    if (excluded != null && excluded.Contains(rect)) continue;   // r15：扩展自己的 banner 背景不算障碍
                    if (!VisibleUnderRoot(root, rect.transform)) continue;
                    Graphic graphic = rect.gameObject.GetComponent<Graphic>();
                    if (graphic == null || !graphic.enabled) continue;
                    if (TryLocalBox(space, rect, out MapIconBox box)) into.Add(box);
                }
            }
            catch (Exception e)
            {
                MapIconLog.Once("collect-" + e.GetType().Name, "collect native boxes failed: " + e.Message);
            }
        }

        private static bool IsUnderOwnHolder(Transform transform)
        {
            Transform cursor = transform;
            for (int depth = 0; cursor != null && depth < 12; depth++, cursor = cursor.parent)
            {
                if (cursor.name == "KEM_MapResourceIcons" || cursor.name == "KEM_MapIconSources") return true;
            }
            return false;
        }

        private static int Fingerprint(int landIndex, bool overview, List<MapIconRequest> requests)
            => MapIconPlanFingerprint.Compute(landIndex, overview, requests);

        // ------------------------------------------------------------------ overview layout

        /// <summary>
        /// 总览布局入口（r11）：**回调只请求有界视口周期，不提交布局**。真实提交点在
        /// `MapTimelineMenu.Update` tick（两个不同 frame 的一致测量后）——同帧 11 个 UILand 回调不能
        /// 充当稳定证据。已提交且环境匹配时保持（视口变化由 tick 的 stamp 检测）。
        /// </summary>
        private static void EnsureOverviewLayout(UIMainMap map)
        {
            try
            {
                RectTransform paper = map != null ? map.gameObject.GetComponent<RectTransform>() : null;
                if (paper == null) return;
                if (OverviewLayoutCommitted(map, paper)) return;
                if (_worldSuspended) return;   // r15：single1/挂起期间数据回调不请求/不重建 world 布局
                RequestViewportCycle(map, paper);
            }
            catch (Exception e)
            {
                MapIconLog.Once("overview-" + e.GetType().Name, "overview layout request failed: " + e.Message);
            }
        }

        /// <summary>已提交布局且环境匹配：land 数 / 登记扩展实例身份 / 同一 paper / 对象存活。</summary>
        private static bool OverviewLayoutCommitted(UIMainMap map, RectTransform paper)
        {
            if (map == null || paper == null) return false;
            if (!_overviewApplied || _overviewPaper != paper) return false;
            if (!OverviewAlive() || Overview.Count == 0) return false;
            Il2CppReferenceArray<UILand> lands = map._lands;
            int landCount = lands == null ? 0 : lands.Length;
            return _overviewLandCount == landCount && _overviewExtensionLand == FindRegisteredExtension(map);
        }

        private sealed class ViewportCycle
        {
            internal UIMainMap Map;
            internal RectTransform Paper;
            internal int RequestedLandCount = -1;
            internal UILand RequestedExtension;
            internal int Attempts;
            internal int PendingFrame = -1;
            internal long PendingSignature = long.MinValue;
            internal bool Running;
            internal bool FailureLogged;
            internal bool PreparedExpansion;
            internal long Stamp = long.MinValue;
            internal int LastTickFrame = -1;
        }

        private static void RequestViewportCycle(UIMainMap map, RectTransform paper)
        {
            ViewportCycle cycle = _viewportCycle;
            bool created = cycle == null || cycle.Map != map || cycle.Paper != paper;
            if (created)
            {
                cycle = new ViewportCycle { Map = map, Paper = paper };
                _viewportCycle = cycle;
            }
            Il2CppReferenceArray<UILand> lands = map._lands;
            int landCount = lands == null ? 0 : lands.Length;
            UILand extension = FindRegisteredExtension(map);
            bool environmentChanged = cycle.RequestedLandCount != landCount ||
                cycle.RequestedExtension != extension;
            if (!created && !environmentChanged) return;   // 同环境重复请求不重置有界周期
            cycle.RequestedLandCount = landCount;
            cycle.RequestedExtension = extension;
            ResetViewportCycle(cycle, ViewportStamp(paper));
        }

        private static void ResetViewportCycle(ViewportCycle cycle, long stamp)
        {
            cycle.Attempts = 0;
            cycle.PendingFrame = -1;
            cycle.PendingSignature = long.MinValue;
            cycle.Running = true;
            cycle.FailureLogged = false;
            cycle.PreparedExpansion = false;
            cycle.Stamp = stamp;
        }

        /// <summary>tick 稳定门：读不到状态一律 false（unknown 不当 stable）。</summary>
        private static bool TryReadMenuStableState(MapTimelineMenuGreece greek, out int worldState,
            out bool animating, out bool scrolled)
        {
            worldState = -1;
            animating = true;
            scrolled = false;
            try
            {
                worldState = (int)greek._openWorldMapState;
                animating = greek.isAnimating;
                scrolled = greek.HasScrolledTargetLand;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// MapTimelineMenu.Update postfix（唯一新增入口）：真实跨帧 tick。
        /// - 每 Time.frameCount 至多推进一次（同帧多岛/多次调用不能推进稳定计数）；
        /// - exact Greek + 本 owned map；ShowingWorld && !isAnimating && HasScrolledTargetLand 三条件
        ///   （读不到状态不推进）；postfix 看到的是上一帧 canvas layout 的结果，因此要求两个不同 frame
        ///   的一致测量才提交；
        /// - 稳定后只读 O(1) stamp（Screen/safeArea/canvas/paper/缓存 mask），变化才开新有界周期；
        /// - 首次也绝不提前成功。
        /// </summary>
        internal static void OnMenuTick(MapTimelineMenu menu)
        {
            try
            {
                if (menu == null) return;
                MapTimelineMenuGreece greek = menu.TryCast<MapTimelineMenuGreece>();
                if (greek == null) return;
                // r14：展示维护必须在所有 world 稳定/布局门之前（state1 应本帧恢复可见）。
                SyncOverviewPresentation(greek);

                // r15：world 几何维护（准备/版本/缓存/撤销）必须先过 exact 当前 ActiveMap caller 门。
                // 迟到/外部 sender：R14 Sync 已按 captured owner 自行处理；此处绝不撤新 owner 的 world 几何。
                MapTimelineMenu activeMap = null;
                bool activeFault = false;
                try
                {
                    Menu menuInst = Menu.Inst;
                    if (menuInst != null) activeMap = menuInst.ActiveMap;
                }
                catch (Exception) { activeFault = true; }
                if (activeFault || activeMap != greek) return;

                int callerState = -1;
                bool callerStateFault = false;
                try { callerState = (int)greek._openWorldMapState; }
                catch (Exception) { callerStateFault = true; }
                if (callerStateFault || callerState != MapOverviewPresentationPolicy.ShowingWorld)
                {
                    // state1/未知：同帧撤 world override（仅 overview 几何/槽/提交标记；详情与 R14 组不动）。
                    // R4：必须传 exact 当前 greek，只回收属于它的 overview 槽/几何，绝不无差别全量 clear。
                    SuspendWorldGeometry(greek);
                    return;
                }

                UIMainMap map = greek._mainMap;
                if (map == null) return;
                RectTransform paper = map.gameObject.GetComponent<RectTransform>();
                if (paper == null) return;

                int frame = Time.frameCount;
                ViewportCycle cycle = _viewportCycle;
                if (cycle != null)
                {
                    if (cycle.LastTickFrame == frame) return;               // 同帧去重
                    if (cycle.Map != map || cycle.Paper != paper) cycle = null;   // 换 map/paper：丢弃旧周期
                }

                if (!TryReadMenuStableState(greek, out int worldState, out bool animating, out bool scrolled)) return;
                if (worldState != 0 || animating || !scrolled) return;

                // 几何是否需要（与 r9 决策一致）：icons ON；或 OFF 但 exact 注册 + 可用（恢复/返航）。
                bool iconsEnabled = FeatureEnabled();
                bool registered = FindRegisteredExtension(map) != null;
                bool available = registered && IsExtensionAccessAvailable();
                if (!iconsEnabled && !(registered && available))
                {
                    if (_overviewApplied) Teardown();
                    if (cycle != null) { cycle.Running = false; cycle.LastTickFrame = frame; }
                    return;   // 关闭且不需要：不创建周期（避免常驻空转/分配）
                }

                if (cycle == null)
                {
                    RequestViewportCycle(map, paper);
                    cycle = _viewportCycle;
                    if (cycle == null) return;
                }
                cycle.LastTickFrame = frame;

                bool committed = OverviewLayoutCommitted(map, paper);
                long stamp = ViewportStamp(paper);
                if (committed)
                {
                    if (cycle.Running) { /* 变化后的复测周期进行中 */ }
                    else if (stamp != cycle.Stamp)
                    {
                        ResetViewportCycle(cycle, stamp);   // 视口变化 → 新有界周期
                    }
                    else
                    {
                        return;                              // 稳定：O(1) 后无操作
                    }
                }
                else
                {
                    if (!cycle.Running)
                    {
                        bool sameStamp = cycle.Stamp == stamp;
                        if (cycle.Attempts > 0 && sameStamp) return;   // 有界失败后放弃，直到 stamp 变化
                        ResetViewportCycle(cycle, stamp);
                    }
                }
                if (!cycle.Running) return;
                AdvanceViewportCycle(cycle, map, greek, paper);
            }
            catch (Exception e)
            {
                MapIconLog.Once("tick-" + e.GetType().Name, "menu tick failed: " + e.Message);
            }
        }

        /// <summary>推进一次（每帧至多一次）：准备扩张（首测前）→ 测量 → 跨帧一致才提交；失败有界。</summary>
        private static void AdvanceViewportCycle(ViewportCycle cycle, UIMainMap map, MapTimelineMenuGreece menu,
            RectTransform paper)
        {
            if (!cycle.PreparedExpansion)
            {
                if (!OverviewLayoutCommitted(map, paper))
                {
                    // 测量与应用必须同一坐标系：先做可逆扩张准备（无已提交布局时才做），再测量。
                    RestoreExpansion();
                    RestoreOverview();
                    ExpandPaper(paper);
                }
                cycle.PreparedExpansion = true;
            }

            if (cycle.Attempts >= MapViewportPolicy.MaxProbeFrames)
            {
                cycle.Running = false;
                if (!cycle.FailureLogged)
                {
                    cycle.FailureLogged = true;
                    MapIconLog.Info("viewport cycle stopped after " + cycle.Attempts + " attempts; keeping " +
                        (_overviewApplied ? "verified layout" : "native geometry") + " (retry on viewport change)");
                }
                if (!_overviewApplied)
                {
                    RestoreExpansion();
                    RestoreOverview();
                }
                return;
            }

            cycle.Attempts++;
            RectTransform measureReference = FindPhysicalPaperImage(FindAncestorByName(paper.transform, "Map Area"));
            VisibleMeasurement measurement = MeasureVisiblePaper(paper, measureReference);
            if (!measurement.Success)
            {
                if (!cycle.FailureLogged)
                {
                    cycle.FailureLogged = true;
                    MapIconLog.Info("viewport measure pending reason=" + measurement.Reason +
                        " attempt=" + cycle.Attempts + " applied=" + _overviewApplied +
                        "; previous layout kept (no full-paper fallback)");
                }
                return;
            }

            long signature = ViewportSignature(measurement.Rect, paper.rect.width, paper.rect.height);
            MapOverviewCache.Action action = MapOverviewCache.Decide(_overviewApplied, signature,
                _overviewAppliedSignature, cycle.PendingSignature, Time.frameCount, cycle.PendingFrame);
            if (action == MapOverviewCache.Action.Keep)
            {
                cycle.Running = false;
                return;
            }
            if (action == MapOverviewCache.Action.Defer)
            {
                cycle.PendingFrame = Time.frameCount;
                cycle.PendingSignature = signature;
                return;
            }
            bool committed = CommitOverviewLayout(map, menu, paper, measurement.Rect, signature);
            if (committed)
            {
                cycle.Running = false;
            }
            else
            {
                // 失败：保持 Running（有界 attemptLimit 或新 stamp 变化才关），下一次尝试重新准备/测量；
                // 不刷 native/不写成功签名。上限到达时由本函数顶部的 fail-closed 归还准备几何。
                cycle.PreparedExpansion = false;
            }
        }

        private static void InvalidateOverviewViews()
        {
            for (int i = 0; i < Views.Count; i++)
            {
                MapMountIconView view = Views[i];
                if (view == null || !view.Overview) continue;
                view.Fingerprint = int.MinValue;
                view.FailedFingerprint = int.MinValue;
                view.LayoutVersion = -1;
                DestroyHolder(view);
                RestoreSuppressed(view);   // R2-3：提交时必须归还原生槽（不能只隐藏 holder 还继续压制 native）
            }
        }

        /// <summary>
        /// 几何提交后的**一次有界 native overview 刷新**（R3 合同）：按此刻 exact menu.focusedReign 取当前
        /// 浏览 reign（0=currentReign；&gt;0=previousReigns[Count-focus]；&lt;0/越界/null 拒绝，绝不回退
        /// currentReign），复查 owner/campaign/focus 未换代后当次同步 `map.UpdateLandIcons(reign)`，复用既有
        /// OnLandUpdated/Rebuild 全量重建。重入门 + 每几何至多一次（失败不无限重试）；不缓存 ReignInfo 代理；
        /// 异常/无效 focus 记录准确原因并保留原生 fallback。
        /// </summary>
        private static void TryNativeRefreshOverview(UIMainMap map, MapTimelineMenuGreece menu)
        {
            if (_nativeRefreshActive) return;   // 同步重入（11 个 UILand 回调期间）只允许一次
            if (_nativeRefreshAttemptedVersion == _overviewGeometryVersion) return;   // 每几何一次，不无限重试
            _nativeRefreshAttemptedVersion = _overviewGeometryVersion;
            _nativeRefreshActive = true;        // 消费标记在调用前（防 postfix 再触发刷新）
            try
            {
                if (map == null || menu == null || menu._mainMap != map)
                {
                    MapIconLog.Info("native overview refresh skipped: owner/map mismatch");
                    return;
                }
                int focus = menu.focusedReign;
                CampaignSaveData campaign = CampaignSaveData.current;
                if (campaign == null || focus < 0)
                {
                    MapIconLog.Info("native overview refresh skipped: campaign/focus invalid focus=" + focus +
                        " (no currentReign fallback)");
                    return;
                }
                CampaignSaveData.ReignInfo reign;
                if (focus == 0)
                {
                    reign = campaign.currentReign;
                }
                else
                {
                    Il2CppSystem.Collections.Generic.List<CampaignSaveData.ReignInfo> previous = null;
                    try { previous = campaign.previousReigns; } catch (Exception) { }
                    if (previous == null)
                    {
                        MapIconLog.Info("native overview refresh skipped: no previousReigns for focus=" + focus);
                        return;
                    }
                    int previousCount = 0;
                    try { previousCount = previous.Count; } catch (Exception) { }
                    if (!MapOverviewRefresh.TryResolvePreviousIndex(focus, previousCount, out int previousIndex) ||
                        previousIndex == MapOverviewRefresh.CurrentReign)
                    {
                        MapIconLog.Info("native overview refresh skipped: focus=" + focus +
                            " out of previousReigns count=" + previousCount + " (no fallback)");
                        return;
                    }
                    reign = previous[previousIndex];
                }
                if (reign == null)
                {
                    MapIconLog.Info("native overview refresh skipped: null reign focus=" + focus);
                    return;
                }
                // 当次复查：owner/campaign/focus 未换代，然后同步立即调用；ReignInfo 用完即弃。
                if (menu._mainMap != map || !SameIl2CppObject(CampaignSaveData.current, campaign) ||
                    menu.focusedReign != focus)
                {
                    MapIconLog.Info("native overview refresh skipped: context changed before call");
                    return;
                }
                Il2CppReferenceArray<UILand> lands = map._lands;
                Il2CppReferenceArray<UIMainMapLand> mainMapLands = map._mainMapLands;
                if (lands == null || mainMapLands == null || lands.Length < mainMapLands.Length)
                {
                    MapIconLog.Info("native overview refresh skipped: land arrays incomplete");
                    return;
                }
                map.UpdateLandIcons(reign);
                MapIconLog.Info("native overview refresh ok focus=" + focus + " lands=" + mainMapLands.Length);
            }
            catch (Exception e)
            {
                MapIconLog.Info("native overview refresh failed: " + e.GetType().Name + " " + e.Message +
                    " (native fallback kept, no retry for this geometry)");
            }
            finally
            {
                _nativeRefreshActive = false;
            }
        }

        /// <summary>il2cpp 对象同一性（wrapper 可能每次重建；按 pointer 比较）。</summary>
        private static bool SameIl2CppObject(Il2CppObjectBase a, Il2CppObjectBase b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            try { return a.Pointer == b.Pointer; } catch (Exception) { return false; }
        }

        /// <summary>
        /// 几何提交（仅在 tick 的两个跨帧一致测量后调用）：RestoreExpansion/RestoreOverview → ExpandPaper →
        /// 扩张后签名自检 → 从冻结原始值绝对重算（统一倍率 + 整体平移装入已验证有效矩形 + 底部扩展带）→
        /// 使全部 overview view 失效（销毁旧 holder 并**归还原生槽**）→ **一次**有界 native focused-reign 刷新
        /// （`TryNativeRefreshOverview`）重建；detail 不受影响。
        /// </summary>
        private static bool CommitOverviewLayout(UIMainMap map, MapTimelineMenuGreece menu, RectTransform paper,
            in MapIconBox verifiedRect, long verifiedSignature)
        {
            try
            {
                Il2CppReferenceArray<UILand> lands = map._lands;
                int landCount = lands == null ? 0 : lands.Length;
                UILand recognizedExtension = FindRegisteredExtension(map);

                RestoreExpansion();
                RestoreOverview();

                ExpandPaper(paper);
                if (landCount == 0) return false;

                float paperW = paper.rect.width;
                float paperH = paper.rect.height;
                float buttonBand = OverviewButtonBandPx;
                // V1：提交前自检——扩张后的坐标系必须与测量时一致（同签名 + UI 阈值），否则不提交，
                // 由有界周期下一帧按新状态重测（不把失败写成"已应用全纸"）。
                long postSignature = ViewportSignature(verifiedRect, paperW, paperH);
                if (postSignature != verifiedSignature || !MapViewportPolicy.AcceptPaperRect(verifiedRect))
                {
                    MapIconLog.Info("viewport commit skipped: post-expansion signature mismatch");
                    return false;
                }
                MapIconBox visiblePaper = verifiedRect;

                var nativeInputs = new List<MapOverviewClusterInput>(MapOverviewLayout.NativeUiClusterCount);
                var nativeEntries = new List<OverviewEntry>(MapOverviewLayout.NativeUiClusterCount);
                var extensionInputs = new List<MapOverviewClusterInput>(2);
                var extensionEntries = new List<OverviewEntry>(2);

                for (int i = 0; i < landCount; i++)
                {
                    UILand cluster = lands[i];
                    if (cluster == null) continue;
                    // 只有登记桥确证的扩展实例才进底部带；未登记 extra 完全不参与布局/绘制（不猜、不画 fake）。
                    if (!TryResolveClusterLandIndex(cluster, i, out int clusterLandIndex)) continue;
                    bool isExtension = clusterLandIndex >= MapOverviewLayout.NativeUiClusterCount;
                    RectTransform rect = cluster.gameObject.GetComponent<RectTransform>();
                    if (rect == null) continue;
                    RectTransform art = FindArtTransform(cluster);
                    if (art == null) continue;
                    if (!TryLocalBox(paper, art, out MapIconBox artBox)) continue;

                    float absScale = Mathf.Abs(rect.localScale.x);
                    if (absScale <= 0.0001f) absScale = 1f;
                    Vector2 clusterPos = new Vector2(rect.anchoredPosition.x + paperW * 0.5f,
                                                     rect.anchoredPosition.y + paperH * 0.5f);
                    Vector2 artCenter = new Vector2((artBox.X0 + artBox.X1) * 0.5f, (artBox.Y0 + artBox.Y1) * 0.5f);
                    float artW1 = artBox.Width / absScale;
                    float artH1 = artBox.Height / absScale;
                    float offX1 = (artCenter.x - clusterPos.x) / absScale;
                    float offY1 = (artCenter.y - clusterPos.y) / absScale;

                    var entry = new OverviewEntry
                    {
                        Cluster = cluster,
                        Rect = rect,
                        AnchoredPosition = rect.anchoredPosition,
                        LocalScale = rect.localScale,
                        LandIndex = clusterLandIndex,
                        IsExtension = isExtension,
                        Input = new MapOverviewClusterInput(rect.anchoredPosition.x, rect.anchoredPosition.y,
                            rect.localScale.x, rect.localScale.y, rect.localScale.z,
                            artW1, artH1, offX1, offY1),
                    };
                    if (entry.IsExtension) { extensionEntries.Add(entry); extensionInputs.Add(entry.Input); }
                    else { nativeEntries.Add(entry); nativeInputs.Add(entry.Input); }
                    Overview.Add(entry);
                }
                if (Overview.Count == 0) return false;

                // r15：world 域共同分区（raw：不夹回 0..paperW）→ 原 10 绝对统一 fit 进上部区；扩展占底部预留带。
                float reserve = extensionEntries.Count > 0 ? MapWorldLayout.DefaultExtensionReserve : 0f;
                if (!MapWorldLayout.ComposeDomains(visiblePaper, buttonBand, reserve,
                        out MapIconBox upperArea, out MapIconBox bandArea))
                {
                    MapIconLog.Warn("world domains invalid; islands keep native geometry (no commit)");
                    return false;
                }
                _worldDomain = visiblePaper;
                _worldUpperArea = upperArea;
                _worldBandArea = bandArea;

                float uniformScale = 1f;
                var targets = new List<MapOverviewClusterTarget>(nativeInputs.Count);
                bool planned = MapWorldLayout.TryFitNatives(nativeInputs, paperW, paperH, upperArea, targets,
                    out uniformScale);
                if (planned)
                {
                    for (int i = 0; i < nativeEntries.Count && i < targets.Count; i++)
                    {
                        ApplyTarget(nativeEntries[i], targets[i]);
                        nativeEntries[i].TargetArtBox =
                            MapOverviewLayout.ArtBoxOf(nativeEntries[i].Input, targets[i], paperW, paperH);
                    }
                }
                else
                {
                    // 理论上的非法输入回退：保持原生几何，但区域仍锚到真实（未变换）art 盒，避免退化归属。
                    for (int i = 0; i < nativeEntries.Count; i++)
                    {
                        if (MapOverviewLayout.TryScale1Box(nativeEntries[i].Input, paperW, paperH,
                                out MapIconBox nativeBox))
                        {
                            nativeEntries[i].TargetArtBox = nativeBox;
                        }
                    }
                    MapIconLog.Warn("overview native layout plan failed; islands keep native geometry");
                }

                // 扩展簇：宽浅 banner（exact11 自身 terrain/outline/Button 同框；子 rect 快照可逆）；
                // 不动原 10 的目标，不把整簇子图标一起横向拉伸。
                for (int i = 0; i < extensionEntries.Count; i++)
                {
                    if (!ApplyExtensionBanner(extensionEntries[i], bandArea, paper, out MapIconBox bannerArtBox))
                    {
                        MapIconLog.Warn("extension banner apply failed; extension keeps native art");
                        continue;
                    }
                    extensionEntries[i].TargetArtBox = bannerArtBox;
                    extensionEntries[i].BannerArea = bandArea;
                }

                // 每岛自由图标区域：原生用上部区（raw）；扩展簇用 banner 专属 surface（背景不算障碍，Rebuild 时规划）。
                MapIconBox contentRegion = upperArea;
                MapIconBox bandRegion = bandArea;
                var owners = new List<MapIconRegionOwner>(nativeEntries.Count);
                for (int i = 0; i < nativeEntries.Count; i++)
                {
                    owners.Add(new MapIconRegionOwner(nativeEntries[i].LandIndex, nativeEntries[i].TargetArtBox));
                }
                var regionRects = new List<MapIconBox>(64);
                var regionOwners = new List<int>(64);
                MapIconRegionPlanner.Build(owners, contentRegion,
                    MapIconRegionPlanner.DefaultCell, regionRects, regionOwners);
                for (int r = 0; r < regionRects.Count; r++)
                {
                    for (int i = 0; i < nativeEntries.Count; i++)
                    {
                        if (nativeEntries[i].LandIndex != regionOwners[r]) continue;
                        nativeEntries[i].RegionRects.Add(regionRects[r]);
                        break;
                    }
                }
                for (int i = 0; i < extensionEntries.Count; i++)
                {
                    // 扩展簇不再走"扣掉 art 再绕岛"的区域规划：专属 planner 允许图标铺在自有底图上。
                    extensionEntries[i].RegionRects.Clear();
                }

                // r15 post-final-geometry：从**实际最终** paper/masks/投影重新测量并 roundtrip；
                // 旧 verifiedRect 的 hash 不算（合同）。不一致 → 回滚快照、不提交/不刷图标/不写签名。
                RectTransform postReference = ResolvePhysicalPaper(paper);
                VisibleMeasurement postMeasure = MeasureVisiblePaper(paper, postReference);
                if (!postMeasure.Success ||
                    !MapWorldLayout.DomainsEquivalent(verifiedRect, postMeasure.Rect, 1f) ||
                    !ContainsBox(postMeasure.Rect, upperArea, 0.5f) ||
                    (extensionEntries.Count > 0 && !ContainsBox(postMeasure.Rect, bandArea, 0.5f)))
                {
                    MapIconLog.Info("world post-geometry validation failed (" + postMeasure.Reason +
                        " pre=[" + Fmt(verifiedRect) + "] post=[" + Fmt(postMeasure.Rect) +
                        "]); rolling back to native geometry (bounded retry)");
                    RestoreOverview();   // 只回滚目标；mask/纸面准备保留给有界重试（attemptLimit 关周期时统一归还）
                    return false;        // 不刷 native/不写成功签名；周期保留有界重试
                }
                // 成功签名 = 当次**实际新测**的最终 rect（不是旧 verifiedRect 的 hash）。
                long finalSignature = ViewportSignature(postMeasure.Rect, paper.rect.width, paper.rect.height);
                MapIconLog.Info("world layout: domain=[" + Fmt(visiblePaper) + "] upper=[" + Fmt(upperArea) +
                    "] band=[" + Fmt(bandArea) + "] scale=" + uniformScale.ToString("0.###") +
                    " maskFit=" + _worldMasksFitCount + " extensions=" + extensionEntries.Count +
                    (extensionEntries.Count > 0 ? " banner=[" + Fmt(extensionEntries[0].TargetArtBox) + "]" : ""));

                _worldSuspended = false;   // 新的 world0 提交生效
                _overviewApplied = true;
                _overviewLandCount = landCount;
                _overviewExtensionLand = recognizedExtension;
                _overviewAppliedSignature = finalSignature;
                _overviewPaper = paper;
                CollectMaskRects(paper);
                _overviewGeometryVersion++;
                MapIconLog.Info("overview native-geography applied natives=" + nativeEntries.Count +
                    " extensions=" + extensionEntries.Count +
                    " uniformScale=" + uniformScale.ToString("0.000") +
                    " visible=[" + visiblePaper.X0.ToString("0.#") + "," + visiblePaper.Y0.ToString("0.#") + "," +
                    visiblePaper.X1.ToString("0.#") + "," + visiblePaper.Y1.ToString("0.#") + "]" +
                    " band=[" + bandRegion.X0.ToString("0.#") + "," + bandRegion.Y0.ToString("0.#") + "," +
                    bandRegion.X1.ToString("0.#") + "," + bandRegion.Y1.ToString("0.#") + "]" +
                    " paper=" + paperW.ToString("0") + "x" + paperH.ToString("0") +
                    " regionRects=" + regionRects.Count);
                LogViewportDiagnostics(paper, map, visiblePaper, bandRegion, contentRegion, uniformScale,
                    postSignature, extensionEntries);
                // R3/V2：几何提交后立即使全部 overview view 失效并归还原生槽，然后做**一次**有界的 native
                // overview 刷新（当前浏览 reign）→ 原生 UpdateLandIcons → 既有 OnLandUpdated/Rebuild 按新
                // RegionRects 全量重建（tick 提交时没有新的资源回调，不能等外部回调）。
                InvalidateOverviewViews();
                TryNativeRefreshOverview(map, menu);
                return true;   // R4：成功提交必须显式返回 true（此前落入 try 尾而返回 false）

            }
            catch (Exception e)
            {
                MapIconLog.Once("commit-" + e.GetType().Name, "overview commit failed: " + e.Message);
            }
            return false;
        }

        private static bool OverviewAlive()
        {
            for (int i = 0; i < Overview.Count; i++)
            {
                if (Overview[i].Cluster == null || Overview[i].Rect == null) return false;
            }
            return true;
        }

        /// <summary>应用冻结快照 + 纯函数算出的绝对目标（位置/缩放均为绝对值，重复调用幂等）。</summary>
        private static void ApplyTarget(OverviewEntry entry, in MapOverviewClusterTarget target)
        {
            if (entry == null || entry.Rect == null) return;
            entry.Rect.anchoredPosition = new Vector2(target.X, target.Y);
            entry.Rect.localScale = new Vector3(target.ScaleX, target.ScaleY, target.ScaleZ);
        }

        // ------------------------------------------------------------------ r11 viewport measure

        /// <summary>测量结果：显式成功/失败 + 失败原因；失败绝不生成"有效全纸"、绝不推进 applied 签名。</summary>
        private struct VisibleMeasurement
        {
            internal bool Success;
            internal MapIconBox Rect;
            internal string Reason;
        }

        private static long ViewportSignature(in MapIconBox visiblePaper, float paperW, float paperH)
        {
            int screenW = 0, screenH = 0;
            try { screenW = Screen.width; screenH = Screen.height; } catch (Exception) { }
            return MapOverviewCache.Signature(visiblePaper, paperW, paperH, screenW, screenH);
        }

        /// <summary>O(1)/常数规模视口 stamp（稳定态每 tick 只读缓存引用）；变化才启动新的有界周期。</summary>
        private static long ViewportStamp(RectTransform paper)
        {
            unchecked
            {
                long h = 19L;
                try { h = h * 31 + Screen.width; h = h * 31 + Screen.height; } catch (Exception) { }
                try
                {
                    Rect safe = Screen.safeArea;
                    h = h * 31 + (long)Math.Round(safe.x * 4f) + (long)Math.Round(safe.y * 4f) * 7;
                    h = h * 31 + (long)Math.Round(safe.width * 4f) + (long)Math.Round(safe.height * 4f) * 7;
                }
                catch (Exception) { }
                Canvas canvas = FindCanvas(paper);
                if (canvas != null)
                {
                    try
                    {
                        h = h * 31 + (long)Math.Round(canvas.scaleFactor * 1000f);
                        Rect pixel = canvas.pixelRect;
                        h = h * 31 + (long)Math.Round(pixel.width * 4f) + (long)Math.Round(pixel.height * 4f) * 7;
                    }
                    catch (Exception) { }
                }
                if (paper != null)
                {
                    Rect r = paper.rect;
                    h = h * 31 + (long)Math.Round(r.width * 4f) + (long)Math.Round(r.height * 4f) * 7;
                    h = h * 31 + (long)Math.Round(paper.anchoredPosition.x * 4f) +
                        (long)Math.Round(paper.anchoredPosition.y * 4f) * 7;
                }
                for (int i = 0; i < _overviewMasks.Count; i++)
                {
                    RectTransform mask = _overviewMasks[i];
                    if (mask == null) continue;
                    try
                    {
                        h = h * 31 + (long)Math.Round(mask.sizeDelta.x * 4f) +
                            (long)Math.Round(mask.sizeDelta.y * 4f) * 7;
                        // r15/R3：真实投影角点（位置/相机变化必须改变 stamp，即便 sizeDelta 不变）。
                        Camera maskCamera = ResolveCanvasCamera(paper);
                        Rect maskScreen = ScreenRectOf(mask, maskCamera);
                        h = h * 31 + (long)Math.Round(maskScreen.xMin * 4f) + (long)Math.Round(maskScreen.yMin * 4f) * 7;
                        h = h * 31 + (long)Math.Round(maskScreen.xMax * 4f) + (long)Math.Round(maskScreen.yMax * 4f) * 7;
                    }
                    catch (Exception) { }
                }
                try
                {
                    RectTransform physical = _worldPhysicalPaper;
                    if (physical != null)
                    {
                        Rect paperScreen = ScreenRectOf(physical, ResolveCanvasCamera(paper));
                        h = h * 31 + (long)Math.Round(paperScreen.xMin * 4f) + (long)Math.Round(paperScreen.yMin * 4f) * 7;
                        h = h * 31 + (long)Math.Round(paperScreen.xMax * 4f) + (long)Math.Round(paperScreen.yMax * 4f) * 7;
                    }
                }
                catch (Exception) { }
                try
                {
                    Canvas stampCanvas = FindCanvas(paper);
                    Camera stampCamera = stampCanvas != null ? stampCanvas.worldCamera : null;
                    h = h * 31 + (stampCamera != null ? (long)stampCamera.Pointer : 0L);
                }
                catch (Exception) { }
                return h;
            }
        }

        /// <summary>缓存当前 paper 链上的真实 RectMask2D（≤12，常数规模；供测量与 stamp 复用）。</summary>
        private static void CollectMaskRects(RectTransform paper)
        {
            _overviewMasks.Clear();
            try
            {
                Transform cursor = paper != null ? paper.parent : null;
                for (int depth = 0; cursor != null && depth < 12; depth++, cursor = cursor.parent)
                {
                    if (cursor.GetComponent<RectMask2D>() == null) continue;
                    RectTransform rect = cursor.TryCast<RectTransform>();
                    if (rect != null) _overviewMasks.Add(rect);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 实际可见 paper 矩形测量（paper 本地、左下原点）。失败分支显式 Success=false：
        /// 屏幕低于像素阈值 / 无交集 / 交集低于像素阈值 / 逆投影 false / paper 本地矩形低于 UI 阈值 /
        /// round-trip 失败 / 异常。**失败不回退全纸**——由调用方保留上一份已验证布局并安排有界复测。
        /// </summary>
        private static VisibleMeasurement MeasureVisiblePaper(RectTransform paper)
            => MeasureVisiblePaper(paper, null);

        /// <summary>
        /// r15 world 测量：参考矩形 = 真实物理纸（scroll_map_bg），**不与总览根 rect 自交**；
        /// 屏幕域 = Screen∩safeArea∩canvas∩camera，再交物理纸与所有真实 ancestor mask（已做可逆 fit 的两个
        /// 确证 mask 不再裁剪；其他 mask 仍生效）。逆投影回 paper 本地允许负起点/X1&gt;paperW（不 clamp）。
        /// </summary>
        private static VisibleMeasurement MeasureVisiblePaper(RectTransform paper, RectTransform referenceRect)
        {
            var result = new VisibleMeasurement { Success = false, Reason = "invalid" };
            try
            {
                if (paper == null) { result.Reason = "no-paper"; return result; }
                float paperW = paper.rect.width;
                float paperH = paper.rect.height;
                if (paperW <= 1f || paperH <= 1f) { result.Reason = "paper-degenerate"; return result; }

                Camera camera = ResolveCanvasCamera(paper);
                Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
                try
                {
                    Rect safe = Screen.safeArea;
                    if (safe.width > 0f && safe.height > 0f) screen = IntersectRect(screen, safe);
                }
                catch (Exception) { }
                Canvas canvas = FindCanvas(paper);
                if (canvas != null)
                {
                    try
                    {
                        Rect pixel = canvas.pixelRect;
                        if (pixel.width > 0f && pixel.height > 0f) screen = IntersectRect(screen, pixel);
                    }
                    catch (Exception) { }
                }
                if (camera != null)
                {
                    try
                    {
                        Rect pixel = camera.pixelRect;
                        if (pixel.width > 0f && pixel.height > 0f) screen = IntersectRect(screen, pixel);
                    }
                    catch (Exception) { }
                }
                if (!MapViewportPolicy.AcceptScreenIntersection(screen.width, screen.height))
                {
                    result.Reason = "screen-thin";
                    return result;
                }

                CollectMaskRects(paper);
                Rect worldReference = referenceRect != null ? ScreenRectOf(referenceRect, camera)
                    : ScreenRectOf(paper, camera);
                Rect visible = IntersectRect(worldReference, screen);
                for (int i = 0; i < _overviewMasks.Count; i++)
                {
                    if (_overviewMasks[i] == null) continue;
                    visible = IntersectRect(visible, ScreenRectOf(_overviewMasks[i], camera));
                }
                if (visible.width <= 0f || visible.height <= 0f)
                {
                    result.Reason = "intersection-empty";
                    return result;
                }
                if (!MapViewportPolicy.AcceptScreenIntersection(visible.width, visible.height))
                {
                    result.Reason = "intersection-thin";
                    return result;
                }

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(paper,
                        new Vector2(visible.xMin, visible.yMin), camera, out Vector2 localMin))
                {
                    result.Reason = "projection-min";
                    return result;
                }
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(paper,
                        new Vector2(visible.xMax, visible.yMax), camera, out Vector2 localMax))
                {
                    result.Reason = "projection-max";
                    return result;
                }
                Rect local = paper.rect;
                var box = new MapIconBox(localMin.x - local.xMin, localMin.y - local.yMin,
                    localMax.x - local.xMin, localMax.y - local.yMin);
                if (!MapViewportPolicy.AcceptPaperRect(box)) { result.Reason = "paper-thin-ui"; return result; }
                if (!RoundTripInside(box, visible, paper, camera, local)) { result.Reason = "roundtrip"; return result; }
                result.Success = true;
                result.Rect = box;
                result.Reason = "ok";
                return result;
            }
            catch (Exception e)
            {
                result.Reason = "exception-" + e.GetType().Name;
                MapIconLog.Once("viewport-" + e.GetType().Name, "viewport measure failed: " + e.Message);
                return result;
            }
        }

        /// <summary>把有效矩形角点投影回屏幕，必须仍落在测得的 visible 内（±1px），否则测量不可信。</summary>
        private static bool RoundTripInside(in MapIconBox box, in Rect visible, RectTransform paper, Camera camera,
            in Rect local)
        {
            try
            {
                Vector3 world = paper.TransformPoint(new Vector3(box.X0 + local.xMin, box.Y0 + local.yMin, 0f));
                Vector2 cornerMin = RectTransformUtility.WorldToScreenPoint(camera, world);
                world = paper.TransformPoint(new Vector3(box.X1 + local.xMin, box.Y1 + local.yMin, 0f));
                Vector2 cornerMax = RectTransformUtility.WorldToScreenPoint(camera, world);
                const float tol = 1f;
                return cornerMin.x >= visible.xMin - tol && cornerMin.x <= visible.xMax + tol &&
                       cornerMin.y >= visible.yMin - tol && cornerMin.y <= visible.yMax + tol &&
                       cornerMax.x >= visible.xMin - tol && cornerMax.x <= visible.xMax + tol &&
                       cornerMax.y >= visible.yMin - tol && cornerMax.y <= visible.yMax + tol;
            }
            catch (Exception) { return false; }
        }

        /// <summary>RectTransform 四角在屏幕空间的 AABB（相机可为 null = overlay）。</summary>
        private static Rect ScreenRectOf(RectTransform rect, Camera camera)
        {
            Rect r = rect.rect;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? r.xMin : r.xMax;
                float y = (i & 2) == 0 ? r.yMin : r.yMax;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera,
                    rect.TransformPoint(new Vector3(x, y, 0f)));
                if (screen.x < minX) minX = screen.x;
                if (screen.x > maxX) maxX = screen.x;
                if (screen.y < minY) minY = screen.y;
                if (screen.y > maxY) maxY = screen.y;
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static Rect IntersectRect(in Rect a, in Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin);
            float y0 = Mathf.Max(a.yMin, b.yMin);
            float x1 = Mathf.Min(a.xMax, b.xMax);
            float y1 = Mathf.Min(a.yMax, b.yMax);
            if (x1 <= x0 || y1 <= y0) return new Rect(x0, y0, 0f, 0f);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>投影相机：最近 Canvas 的 renderMode 决定（overlay → null）；否则用非 overlay 链上的
        /// worldCamera；再退回 rootCanvas 的 worldCamera。日志分别报 nearest/root。</summary>
        private static Camera ResolveCanvasCamera(RectTransform start)
        {
            try
            {
                Transform cursor = start;
                for (int depth = 0; cursor != null && depth < 8; depth++, cursor = cursor.parent)
                {
                    Canvas canvas = cursor.GetComponent<Canvas>();
                    if (canvas == null) continue;
                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
                    if (canvas.worldCamera != null) return canvas.worldCamera;
                }
                Canvas root = FindRootCanvas(start);
                if (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay && root.worldCamera != null)
                {
                    return root.worldCamera;
                }
            }
            catch (Exception) { }
            return null;
        }

        private static Canvas FindCanvas(RectTransform start)
        {
            try
            {
                Transform cursor = start;
                for (int depth = 0; cursor != null && depth < 8; depth++, cursor = cursor.parent)
                {
                    Canvas canvas = cursor.GetComponent<Canvas>();
                    if (canvas != null) return canvas;
                }
            }
            catch (Exception) { }
            return null;
        }

        private static Canvas FindRootCanvas(RectTransform start)
        {
            try
            {
                Canvas canvas = FindCanvas(start);
                return canvas != null ? canvas.rootCanvas : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 有界只读诊断（每个成功提交的视口签名一次）：Screen/safeArea/nearest+rootCanvas/render camera/
        /// masks/paper/visible/content/band/scale + extra art/outline 读回；不逐帧输出。
        /// </summary>
        private static void LogViewportDiagnostics(RectTransform paper, UIMainMap map, in MapIconBox visiblePaper,
            in MapIconBox bandRegion, in MapIconBox contentRegion, float uniformScale, long signature,
            List<OverviewEntry> extensionEntries)
        {
            if (signature == _overviewDiagnosedViewport) return;
            _overviewDiagnosedViewport = signature;
            try
            {
                string message = "[viewport] screen=" + Screen.width + "x" + Screen.height;
                try
                {
                    Rect safe = Screen.safeArea;
                    message += " safe=(" + safe.xMin.ToString("0.#") + "," + safe.yMin.ToString("0.#") + "," +
                        safe.width.ToString("0.#") + "," + safe.height.ToString("0.#") + ")";
                }
                catch (Exception) { }
                Canvas canvas = FindCanvas(paper);
                Canvas root = FindRootCanvas(paper);
                if (canvas != null)
                {
                    string pixel = "?";
                    try
                    {
                        Rect pr = canvas.pixelRect;
                        pixel = pr.width.ToString("0.#") + "x" + pr.height.ToString("0.#") + "@" +
                            pr.xMin.ToString("0.#") + "," + pr.yMin.ToString("0.#");
                    }
                    catch (Exception) { }
                    message += " canvas=" + canvas.name + " mode=" + canvas.renderMode + " scale=" +
                        canvas.scaleFactor.ToString("0.###") + " pixel=" + pixel;
                }
                if (root != null && root != canvas)
                {
                    message += " rootCanvas=" + root.name + " mode=" + root.renderMode + " scale=" +
                        root.scaleFactor.ToString("0.###");
                }
                Camera camera = ResolveCanvasCamera(paper);
                if (camera != null)
                {
                    string pixel = "?";
                    try
                    {
                        Rect pr = camera.pixelRect;
                        pixel = pr.width.ToString("0.#") + "x" + pr.height.ToString("0.#") + "@" +
                            pr.xMin.ToString("0.#") + "," + pr.yMin.ToString("0.#");
                    }
                    catch (Exception) { }
                    message += " camera=" + camera.name + " pixel=" + pixel;
                }
                message += " paper=" + paper.rect.width.ToString("0.#") + "x" + paper.rect.height.ToString("0.#") +
                    "@" + paper.anchoredPosition.x.ToString("0.#") + "," + paper.anchoredPosition.y.ToString("0.#");
                for (int i = 0; i < _overviewMasks.Count; i++)
                {
                    RectTransform mask = _overviewMasks[i];
                    if (mask == null) continue;
                    MapIconBox maskBox = TryLocalBox(paper, mask, out MapIconBox mb) ? mb : default;
                    message += " mask[" + mask.name + "=(" + maskBox.X0.ToString("0.#") + "," +
                        maskBox.Y0.ToString("0.#") + "," + maskBox.X1.ToString("0.#") + "," +
                        maskBox.Y1.ToString("0.#") + ")]";
                }
                message += " visible=[" + visiblePaper.X0.ToString("0.#") + "," + visiblePaper.Y0.ToString("0.#") +
                    "," + visiblePaper.X1.ToString("0.#") + "," + visiblePaper.Y1.ToString("0.#") + "]";
                message += " content=[" + contentRegion.X0.ToString("0.#") + "," + contentRegion.Y0.ToString("0.#") +
                    "," + contentRegion.X1.ToString("0.#") + "," + contentRegion.Y1.ToString("0.#") + "]";
                message += " band=[" + bandRegion.X0.ToString("0.#") + "," + bandRegion.Y0.ToString("0.#") + "," +
                    bandRegion.X1.ToString("0.#") + "," + bandRegion.Y1.ToString("0.#") + "]";
                message += " scale=" + uniformScale.ToString("0.000");
                for (int i = 0; i < extensionEntries.Count; i++)
                {
                    UILand cluster = extensionEntries[i].Cluster;
                    if (cluster == null) continue;
                    MapIconBox art = TryLocalBox(paper, FindArtTransform(cluster), out MapIconBox artBox)
                        ? artBox : default;
                    message += " extra[" + extensionEntries[i].LandIndex + "] art=(" + art.X0.ToString("0.#") + "," +
                        art.Y0.ToString("0.#") + "," + art.X1.ToString("0.#") + "," + art.Y1.ToString("0.#") + ")";
                    Transform outline = null;
                    try
                    {
                        Transform t = cluster.transform;
                        for (int c = 0; c < t.childCount; c++)
                        {
                            if (t.GetChild(c) != null && t.GetChild(c).name == "Land Outline Highlight")
                            {
                                outline = t.GetChild(c);
                                break;
                            }
                        }
                    }
                    catch (Exception) { }
                    if (outline != null && TryLocalBox(paper, outline as RectTransform, out MapIconBox outlineBox))
                    {
                        message += " outline=(" + outlineBox.X0.ToString("0.#") + "," + outlineBox.Y0.ToString("0.#") +
                            "," + outlineBox.X1.ToString("0.#") + "," + outlineBox.Y1.ToString("0.#") + ")";
                    }
                    message += " regions=" + extensionEntries[i].RegionRects.Count;
                }
                MapIconLog.Info(message);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 显式布局链：命名节点 Content Area / Lands Container / paper 一起扩到 min(MapArea-8, cap)；
        /// 只受真正带 RectMask2D 的祖先约束（单次夹取，R2 不再对任意零宽 LayoutGroup 祖先做多轮回退）；
        /// 每次修改前快照（sizeDelta+anchoredPosition），teardown 全量还原。
        /// </summary>
        private static void ExpandPaper(RectTransform paper)
        {
            RectTransform mapArea = FindAncestorByName(paper.transform, "Map Area");
            RectTransform contentArea = FindAncestorByName(paper.transform, "Content Area");
            RectTransform landsContainer = FindAncestorByName(paper.transform, "Lands Container");

            // r15：不再扩大 mainMap 根/物理纸本身（避免与 HorizontalLayoutGroup 分页竞争、避免把物理纸放大到超屏）；
            // 只把两个确证 RectMask2D（Content Area / Lands Container）**可逆 fit** 到物理纸 safe 内框，
            // 从而让 world 域 = 整张实际可见物理纸（其余真实 ancestor mask 仍参与交集）。

            // r5：**真实原生画卷底纸本身**（Map Area/Background Objects/Image，Tiled Image，
            // sprite=scroll_map_bg 185×146，sizeDelta(-10,-22) ⇒ MapArea 322×216 下实际约 312×194）
            // 必须一起扩大：mask 扩到 314×208 后四边（左 4 / 右 4 / 下 5 / 上 3）会露在底纸之外。
            // 目标 = MapArea 实寸 322×216 并对齐 MapArea 真实中心 ⇒ 底纸最终 = MapArea 矩形。
            // sprite / material / Image 类型 / Tiled 参数 / Scroll Left-Right 卷轴一律不动（只动 Rect）。
            RectTransform physicalPaper = FindPhysicalPaperImage(mapArea);
            // r15：物理纸保持原生（只读参考）；world 域由它也参与的交集定义。

            // r15：world-only mask viewport fit（exact 两个确证 RectMask2D；保存 rect 字段，state1/off/owner 换代原样恢复）。
            FitWorldMasks(paper, physicalPaper);

            // r15：不再移动 mainMap 根 rect（world 域允许超出根 rect；分页/HorizontalLayoutGroup 不动）。

            MapIconLog.Info("world geometry: root=" + paper.rect.width.ToString("0") + "x" +
                paper.rect.height.ToString("0") + " @(" + paper.anchoredPosition.x.ToString("0.##") + "," +
                paper.anchoredPosition.y.ToString("0.##") + ") physical=" +
                (physicalPaper != null ? physicalPaper.rect.width.ToString("0") + "x" +
                    physicalPaper.rect.height.ToString("0") : "none") + " masksFit=" + _worldMasksFitCount);
        }

        /// <summary>
        /// r15：把 exact 两个确证 RectMask2D（Content Area / Lands Container）可逆 fit 到物理纸 safe 内框
        /// （世界屏幕域 ∩ Screen/safeArea/canvas/camera ∩ 物理纸）。保存自身 rect 字段到同一快照列表；
        /// 不禁用/删除 mask、不 reparent、不动分页与 root；其余 ancestor mask 仍参与交集。
        /// 任一步失败 → 该 mask 原样（fail-closed）。
        /// </summary>
        private static void FitWorldMasks(RectTransform paper, RectTransform physicalPaper)
        {
            _worldMasksFitCount = 0;
            if (physicalPaper != null) _worldPhysicalPaper = physicalPaper;
            if (paper == null || physicalPaper == null) return;
            Camera camera = ResolveCanvasCamera(paper);
            Rect screen = ScreenDomain(paper);
            if (!MapViewportPolicy.AcceptScreenIntersection(screen.width, screen.height)) return;
            Rect target = IntersectRect(ScreenRectOf(physicalPaper, camera), screen);
            if (!MapViewportPolicy.AcceptScreenIntersection(target.width, target.height)) return;
            RectTransform contentArea = FindAncestorByName(paper.transform, "Content Area");
            RectTransform landsContainer = FindAncestorByName(paper.transform, "Lands Container");
            if (FitMaskToScreen(target, camera, contentArea)) _worldMasksFitCount++;
            if (FitMaskToScreen(target, camera, landsContainer)) _worldMasksFitCount++;
        }

        /// <summary>Screen ∩ safeArea ∩ canvas.pixelRect ∩ camera.pixelRect（world 域的屏幕上界）。</summary>
        private static Rect ScreenDomain(RectTransform paper)
        {
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            try
            {
                Rect safe = Screen.safeArea;
                if (safe.width > 0f && safe.height > 0f) screen = IntersectRect(screen, safe);
            }
            catch (Exception) { }
            Canvas canvas = FindCanvas(paper);
            if (canvas != null)
            {
                try
                {
                    Rect pixel = canvas.pixelRect;
                    if (pixel.width > 0f && pixel.height > 0f) screen = IntersectRect(screen, pixel);
                }
                catch (Exception) { }
            }
            return screen;
        }

        /// <summary>单 mask fit：把 mask 的 rect 扩到覆盖 targetScreen（父坐标投影），并原样校验；失败恢复原字段。</summary>
        private static bool FitMaskToScreen(in Rect targetScreen, Camera camera, RectTransform mask)
        {
            if (mask == null) return false;
            try
            {
                if (mask.gameObject.GetComponent<RectMask2D>() == null) return false;
                Rect current = ScreenRectOf(mask, camera);
                if (current.width >= targetScreen.width - 0.5f && current.height >= targetScreen.height - 0.5f &&
                    current.xMin <= targetScreen.xMin + 0.5f && current.xMax >= targetScreen.xMax - 0.5f &&
                    current.yMin <= targetScreen.yMin + 0.5f && current.yMax >= targetScreen.yMax - 0.5f)
                {
                    return false;   // 已覆盖：不改（不产生快照）
                }
                RectTransform parent = mask.parent as RectTransform;
                if (parent == null) return false;
                if (!TryParentBoxOfScreen(parent, camera, targetScreen, out MapIconBox need)) return false;
                Snapshot(mask);
                int snapshotIndex = Expanded.Count - 1;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!TryLocalBox(parent, mask, out MapIconBox before)) { RestoreSnapshotAt(snapshotIndex); return false; }
                    float dw = need.Width - before.Width;
                    float dh = need.Height - before.Height;
                    Vector2 size = mask.sizeDelta;
                    if (Math.Abs(dw) > 0.05f || Math.Abs(dh) > 0.05f) mask.sizeDelta = new Vector2(size.x + dw, size.y + dh);
                    if (!TryLocalBox(parent, mask, out MapIconBox after)) { RestoreSnapshotAt(snapshotIndex); return false; }
                    float centerDx = (need.X0 + need.X1) * 0.5f - (after.X0 + after.X1) * 0.5f;
                    float centerDy = (need.Y0 + need.Y1) * 0.5f - (after.Y0 + after.Y1) * 0.5f;
                    if (Math.Abs(centerDx) > 0.05f || Math.Abs(centerDy) > 0.05f)
                    {
                        Vector2 pos = mask.anchoredPosition;
                        mask.anchoredPosition = new Vector2(pos.x + centerDx, pos.y + centerDy);
                    }
                }
                if (!TryLocalBox(parent, mask, out MapIconBox fitted) ||
                    fitted.X0 > need.X0 + 0.5f || fitted.X1 < need.X1 - 0.5f ||
                    fitted.Y0 > need.Y0 + 0.5f || fitted.Y1 < need.Y1 - 0.5f)
                {
                    RestoreSnapshotAt(snapshotIndex);
                    MapIconLog.Once("world-mask-fit-" + mask.name,
                        "world mask fit rejected (verify failed): " + mask.name);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                MapIconLog.Once("world-mask-" + e.GetType().Name, "world mask fit failed: " + e.Message);
                return false;
            }
        }

        private static void RestoreSnapshotAt(int index)
        {
            if (index < 0 || index >= Expanded.Count) return;
            RectSnapshot snapshot = Expanded[index];
            if (snapshot != null && snapshot.Rect != null)
            {
                try
                {
                    snapshot.Rect.sizeDelta = snapshot.SizeDelta;
                    snapshot.Rect.anchoredPosition = snapshot.AnchoredPosition;
                    if (snapshot.LocalScale.x > 0f && snapshot.LocalScale.y > 0f)
                    {
                        snapshot.Rect.localScale = snapshot.LocalScale;
                    }
                }
                catch (Exception) { }
            }
            Expanded.RemoveAt(index);
        }

        /// <summary>targetScreen 四角投影回 parent 的 rect 坐标系（与 TryLocalBox 同口径）。</summary>
        private static bool TryParentBoxOfScreen(RectTransform parent, Camera camera, in Rect targetScreen,
            out MapIconBox box)
        {
            box = default;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? targetScreen.xMin : targetScreen.xMax;
                float sy = (i & 2) == 0 ? targetScreen.yMin : targetScreen.yMax;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(sx, sy), camera,
                        out Vector2 local)) return false;
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }
            Rect rect = parent.rect;
            box = new MapIconBox(minX - rect.xMin, minY - rect.yMin, maxX - rect.xMin, maxY - rect.yMin);
            return box.Width > 0f && box.Height > 0f;
        }

        private static bool BoxesIntersect(in MapIconBox a, in MapIconBox b, float eps)
            => a.X0 < b.X1 - eps && b.X0 < a.X1 - eps && a.Y0 < b.Y1 - eps && b.Y0 < a.Y1 - eps;

        /// <summary>outer 是否覆盖 inner（±eps）——post-final-geometry 校验用。</summary>
        private static bool ContainsBox(in MapIconBox outer, in MapIconBox inner, float eps)
            => outer.X0 <= inner.X0 + eps && outer.X1 >= inner.X1 - eps &&
               outer.Y0 <= inner.Y0 + eps && outer.Y1 >= inner.Y1 - eps;

        private static string Fmt(in MapIconBox box)
            => "(" + box.X0.ToString("0.#") + "," + box.Y0.ToString("0.#") + "," +
               box.X1.ToString("0.#") + "," + box.Y1.ToString("0.#") + ")";

        /// <summary>
        /// r15：扩展簇宽浅 banner——把 exact11 自身 terrain（Land Button/Image）、outline、同框 Button rect
        /// 缩放到覆盖 banner 并保持中心；只改 localScale/anchoredPosition（同一快照列表可逆还原）。
        /// 只操作 exact 实例自身子 rect；sprite/资产/其他子图标不动。
        /// </summary>
        private static bool ApplyExtensionBanner(OverviewEntry entry, in MapIconBox banner, RectTransform paper,
            out MapIconBox artBox)
        {
            artBox = default;
            _bannerExcludedRects.Clear();
            if (entry == null || entry.Cluster == null || paper == null || banner.Width <= 1f || banner.Height <= 1f)
            {
                return false;
            }
            try
            {
                RectTransform art = FindArtTransform(entry.Cluster);
                if (art == null) return false;
                if (!TryLocalBox(paper, art, out MapIconBox current) || current.Width <= 0.5f || current.Height <= 0.5f)
                {
                    return false;
                }
                Snapshot(art);
                _bannerExcludedRects.Add(art);
                RectTransform outline = FindNamedDescendant(entry.Cluster.transform, "Land Outline", 4);
                if (outline == null || CheckAncestry(outline, art, 8) == Ancestry.Under) outline = null;
                else { Snapshot(outline); _bannerExcludedRects.Add(outline); }
                RectTransform buttonRect = FindOverviewButtonRect(entry.Cluster);
                if (buttonRect == null || CheckAncestry(buttonRect, art, 8) == Ancestry.Under) buttonRect = null;
                else { Snapshot(buttonRect); _bannerExcludedRects.Add(buttonRect); }

                // R4/V2：先把 exact 扩展簇**根**整簇刚性平移到 banner 锚点（保持原 scale）——actual native 标记
                // （Boat/Lighthouse 等）是根的兄弟子树，只移子 art 会让它们留在 Oracle 克隆原位；根移动后
                // 这些标记随根系同量移动、尺寸不变，Rebuild 时按真实新位置重新测量为 blockers。
                RectTransform clusterRoot = entry.Cluster != null ? entry.Cluster.transform as RectTransform : null;
                if (clusterRoot != null) TranslateClusterToBox(clusterRoot, paper, banner);
                if (!PlaceVisualToBox(art, paper, banner, current)) return false;
                if (outline != null && TryLocalBox(paper, outline, out MapIconBox outlineBox))
                {
                    PlaceVisualToBox(outline, paper, banner, outlineBox);
                }
                if (buttonRect != null && TryLocalBox(paper, buttonRect, out MapIconBox buttonBox))
                {
                    PlaceVisualToBox(buttonRect, paper, banner, buttonBox);
                }
                return TryLocalBox(paper, art, out artBox) && artBox.Width > 1f && artBox.Height > 1f;
            }
            catch (Exception e)
            {
                MapIconLog.Once("ext-banner-" + e.GetType().Name, "extension banner failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// R4：把 exact 扩展簇**根**整簇刚性平移到 target（banner 锚点）——保持原 scale，只同步根世界位移；
        /// native 标记（Boat/Lighthouse 等）是根的兄弟子树，随根同量移动、尺寸不变。经同一 Snapshot 列表可逆。
        /// 供 ApplyExtensionBanner 与入口探针共同调用（真实方法体，非测试替身）。
        /// </summary>
        internal static bool TranslateClusterToBox(RectTransform clusterRoot, RectTransform paper, in MapIconBox target)
        {
            if (clusterRoot == null || paper == null || target.Width <= 1f || target.Height <= 1f) return false;
            try
            {
                if (!TryLocalBox(paper, clusterRoot, out MapIconBox rootBox) ||
                    rootBox.Width <= 0.5f || rootBox.Height <= 0.5f)
                {
                    return false;
                }
                Snapshot(clusterRoot);
                Rect paperRect = paper.rect;
                Vector3 targetWorld = paper.TransformPoint(new Vector3(
                    (target.X0 + target.X1) * 0.5f + paperRect.xMin,
                    (target.Y0 + target.Y1) * 0.5f + paperRect.yMin, 0f));
                bool moved = false;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!TryLocalBox(paper, clusterRoot, out MapIconBox after)) break;
                    Vector3 rootWorld = paper.TransformPoint(new Vector3(
                        (after.X0 + after.X1) * 0.5f + paperRect.xMin,
                        (after.Y0 + after.Y1) * 0.5f + paperRect.yMin, 0f));
                    Vector3 delta = targetWorld - rootWorld;
                    if (delta.sqrMagnitude < 0.0025f) { moved = true; break; }
                    Transform parent = clusterRoot.parent;
                    if (parent != null) delta = parent.InverseTransformVector(delta);
                    Vector2 pos = clusterRoot.anchoredPosition;
                    clusterRoot.anchoredPosition = new Vector2(pos.x + delta.x, pos.y + delta.y);
                    moved = true;
                }
                return moved;
            }
            catch (Exception e)
            {
                MapIconLog.Once("root-translate-" + e.GetType().Name, "cluster root translate failed: " + e.Message);
                return false;
            }
        }

        /// <summary>把 rect 的 paper 空间盒缩放到覆盖 target（保持中心）；世界增量经父 InverseTransformVector 应用。</summary>
        private static bool PlaceVisualToBox(RectTransform rect, RectTransform paper, in MapIconBox target,
            in MapIconBox current)
        {
            if (rect == null || current.Width <= 0.01f || current.Height <= 0.01f) return false;
            float sx = target.Width / current.Width;
            float sy = target.Height / current.Height;
            if (!(sx > 0f) || !(sy > 0f)) return false;
            Vector3 scale = rect.localScale;
            rect.localScale = new Vector3(scale.x * sx, scale.y * sy, scale.z <= 0f ? 1f : scale.z);
            Rect paperRect = paper.rect;
            Vector3 targetWorld = paper.TransformPoint(new Vector3(
                (target.X0 + target.X1) * 0.5f + paperRect.xMin, (target.Y0 + target.Y1) * 0.5f + paperRect.yMin, 0f));
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!TryLocalBox(paper, rect, out MapIconBox after)) return false;
                Vector3 currentWorld = paper.TransformPoint(new Vector3(
                    (after.X0 + after.X1) * 0.5f + paperRect.xMin, (after.Y0 + after.Y1) * 0.5f + paperRect.yMin, 0f));
                Vector3 delta = targetWorld - currentWorld;
                if (delta.sqrMagnitude < 0.0025f) return true;
                Transform parent = rect.parent;
                if (parent != null) delta = parent.InverseTransformVector(delta);
                Vector2 pos = rect.anchoredPosition;
                rect.anchoredPosition = new Vector2(pos.x + delta.x, pos.y + delta.y);
            }
            return TryLocalBox(paper, rect, out _);
        }

        private static RectTransform FindNamedDescendant(Transform root, string prefix, int maxDepth)
        {
            try
            {
                Transform cursor = root;
                for (int depth = 0; cursor != null && depth <= maxDepth; depth++)
                {
                    for (int i = 0; i < cursor.childCount; i++)
                    {
                        Transform child = cursor.GetChild(i);
                        if (child == null) continue;
                        if (child.name != null && child.name.StartsWith(prefix)) return child.GetComponent<RectTransform>();
                    }
                    cursor = cursor.childCount > 0 ? cursor.GetChild(0) : null;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>UIMainMapLand 的原生 Button rect（typed；缺失返回 null）。</summary>
        private static RectTransform FindOverviewButtonRect(UILand land)
        {
            try
            {
                UIMainMapLand button = land != null ? land.gameObject.GetComponent<UIMainMapLand>() : null;
                if (button == null || button._button == null) return null;
                return button._button.gameObject.GetComponent<RectTransform>();
            }
            catch (Exception) { return null; }
        }

        /// <summary>缓存 exact 物理纸引用（不每帧 FindPhysicalPaper/扫树；对象销毁后才重新解析）。</summary>
        private static RectTransform ResolvePhysicalPaper(RectTransform paper)
        {
            if (_worldPhysicalPaper != null) return _worldPhysicalPaper;
            RectTransform mapArea = FindAncestorByName(paper != null ? paper.transform : null, "Map Area");
            _worldPhysicalPaper = FindPhysicalPaperImage(mapArea);
            return _worldPhysicalPaper;
        }

        /// <summary>
        /// 定位**真实原生画卷底纸**：Map Area/Background Objects/Image（Tiled Image，
        /// sprite=scroll_map_bg）。带 sprite 名守卫，避免误扩别的 Image；找不到就跳过（不改布局）。
        /// </summary>
        private static RectTransform FindPhysicalPaperImage(RectTransform mapArea)
        {
            if (mapArea == null) return null;
            try
            {
                Transform backdrop = null;
                for (int i = 0; i < mapArea.childCount; i++)
                {
                    Transform child = mapArea.GetChild(i);
                    if (child != null && child.name == "Background Objects") { backdrop = child; break; }
                }
                if (backdrop == null) return null;
                for (int i = 0; i < backdrop.childCount; i++)
                {
                    Transform child = backdrop.GetChild(i);
                    if (child == null || child.name != "Image") continue;
                    RectTransform rect = child.GetComponent<RectTransform>();
                    if (rect == null) return null;
                    try
                    {
                        Image graphic = child.GetComponent<Image>();
                        if (graphic != null && graphic.sprite != null)
                        {
                            string name = graphic.sprite.name;
                            if (!string.IsNullOrEmpty(name) &&
                                name.IndexOf("scroll_map_bg", StringComparison.Ordinal) < 0)
                            {
                                // 不是画卷底纸（biome 变体命名也含 scroll_map_bg 前缀）：不碰。
                                return null;
                            }
                        }
                    }
                    catch (Exception) { }
                    return rect;
                }
            }
            catch (Exception e)
            {
                MapIconLog.Once("paper-image-" + e.GetType().Name, "physical paper lookup failed: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// rect 中心对齐 mask/地图区中心：用真实 world 中心差换算到父级本地单位后平移
        /// （快照可逆）。不依赖任何硬编码偏移；父级无 mask 也不影响本换算。
        /// paper 与真实底纸共用同一实现（r3 = paper，r5 = 底纸）。
        /// </summary>
        private static void AlignPaperToViewportCenter(RectTransform paper, RectTransform viewport)
        {
            if (paper == null || viewport == null) return;
            RectTransform parent = paper.parent as RectTransform;
            if (parent == null) return;
            try
            {
                Vector3 paperCenter = paper.TransformPoint(paper.rect.center);
                Vector3 viewCenter = viewport.TransformPoint(viewport.rect.center);
                Vector3 delta = parent.InverseTransformVector(viewCenter - paperCenter);
                if (delta.sqrMagnitude < 0.0001f) return;
                Snapshot(paper);
                paper.anchoredPosition = paper.anchoredPosition + new Vector2(delta.x, delta.y);
                MapIconLog.Info("paper center aligned to viewport: delta=(" + delta.x.ToString("0.##") + "," +
                    delta.y.ToString("0.##") + ")");
            }
            catch (Exception e)
            {
                MapIconLog.Once("align-" + e.GetType().Name, "paper align failed: " + e.Message);
            }
        }

        /// <summary>
        /// 可逆收缩/扩张（r10）：与 GrowTo 同一中心保持与快照语义，但允许缩小（mask clamp 专用）。
        /// GrowTo 继续只承担"扩大"职责，不用它执行收缩。
        /// </summary>
        private static void ResizeExact(RectTransform rect, float width, float height)
        {
            if (rect == null || width <= 0f || height <= 0f) return;
            if (Mathf.Abs(rect.rect.width - width) < 0.01f && Mathf.Abs(rect.rect.height - height) < 0.01f) return;

            RectTransform parent = rect.parent as RectTransform;
            bool hasCenter = false;
            Vector2 centerBefore = Vector2.zero;
            if (parent != null && TryLocalBox(parent, rect, out MapIconBox before))
            {
                centerBefore = new Vector2((before.X0 + before.X1) * 0.5f, (before.Y0 + before.Y1) * 0.5f);
                hasCenter = true;
            }

            Snapshot(rect);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x + (width - rect.rect.width),
                rect.sizeDelta.y + (height - rect.rect.height));

            if (hasCenter && TryLocalBox(parent, rect, out MapIconBox after))
            {
                Vector2 centerAfter = new Vector2((after.X0 + after.X1) * 0.5f, (after.Y0 + after.Y1) * 0.5f);
                rect.anchoredPosition = rect.anchoredPosition + (centerBefore - centerAfter);
            }
        }

        /// <summary>只增不减；扩到目标尺寸并保持中心不动（快照可逆）。</summary>
        private static void GrowTo(RectTransform rect, float width, float height)
        {
            if (rect == null) return;
            if (rect.rect.width >= width - 0.01f && rect.rect.height >= height - 0.01f) return;

            RectTransform parent = rect.parent as RectTransform;
            bool hasCenter = false;
            Vector2 centerBefore = Vector2.zero;
            if (parent != null && TryLocalBox(parent, rect, out MapIconBox before))
            {
                centerBefore = new Vector2((before.X0 + before.X1) * 0.5f, (before.Y0 + before.Y1) * 0.5f);
                hasCenter = true;
            }

            Snapshot(rect);
            float dw = width - rect.rect.width;
            float dh = height - rect.rect.height;
            if (dw > 0f) rect.sizeDelta = new Vector2(rect.sizeDelta.x + dw, rect.sizeDelta.y);
            if (dh > 0f) rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y + dh);

            if (hasCenter && TryLocalBox(parent, rect, out MapIconBox after))
            {
                Vector2 centerAfter = new Vector2((after.X0 + after.X1) * 0.5f, (after.Y0 + after.Y1) * 0.5f);
                rect.anchoredPosition = rect.anchoredPosition + (centerBefore - centerAfter);
            }
        }

        private static void Snapshot(RectTransform rect)
        {
            for (int i = 0; i < Expanded.Count; i++)
            {
                if (Expanded[i].Rect == rect) return;
            }
            Expanded.Add(new RectSnapshot
            {
                Rect = rect,
                SizeDelta = rect.sizeDelta,
                AnchoredPosition = rect.anchoredPosition,
                LocalScale = rect.localScale,
            });
        }

        private static void RestoreExpansion()
        {
            for (int i = 0; i < Expanded.Count; i++)
            {
                RectSnapshot snapshot = Expanded[i];
                if (snapshot.Rect == null) continue;
                try
                {
                    snapshot.Rect.sizeDelta = snapshot.SizeDelta;
                    snapshot.Rect.anchoredPosition = snapshot.AnchoredPosition;
                    if (snapshot.LocalScale.x > 0f && snapshot.LocalScale.y > 0f)
                    {
                        snapshot.Rect.localScale = snapshot.LocalScale;
                    }
                }
                catch (Exception) { }
            }
            Expanded.Clear();
        }

        private static void RestoreOverview()
        {
            for (int i = 0; i < Overview.Count; i++)
            {
                OverviewEntry entry = Overview[i];
                if (entry.Rect == null) continue;
                try
                {
                    entry.Rect.anchoredPosition = entry.AnchoredPosition;
                    entry.Rect.localScale = entry.LocalScale;
                }
                catch (Exception) { }
            }
            Overview.Clear();
            _overviewApplied = false;
            _overviewLandCount = -1;
            _overviewExtensionLand = null;
            _overviewAppliedSignature = long.MinValue;
            _overviewPaper = null;
            _overviewMasks.Clear();
        }

        // ------------------------------------------------------------------ views

        /// <summary>
        /// 取/建 view。ON+owned 期间即便当次数组为空或数据不可用也要有 view——
        /// 它是"该 land 原生资源槽已被本功能接管"的唯一凭据（R1 缺陷 3 修复点）。
        /// </summary>
        private static MapMountIconView EnsureView(MapMountIconView view, UILand land, int landIndex, bool overview)
        {
            if (view != null) return view;
            view = new MapMountIconView { Land = land, Overview = overview, LandIndex = landIndex };
            Views.Add(view);
            return view;
        }

        private static MapMountIconView FindView(UILand land, bool prune)
        {
            for (int i = Views.Count - 1; i >= 0; i--)
            {
                MapMountIconView view = Views[i];
                if (view == null) { Views.RemoveAt(i); continue; }
                if (view.Land == land) return view;
                if (prune && view.Land == null)
                {
                    RestoreSuppressed(view);
                    Views.RemoveAt(i);
                }
            }
            return null;
        }

        private static void DestroyHolder(MapMountIconView view)
        {
            if (view == null) return;
            try
            {
                if (view.Holder != null) UnityEngine.Object.Destroy(view.Holder);
            }
            catch (Exception) { }
            view.Holder = null;
            view.Fingerprint = int.MinValue;
            view.Icons.Clear();
        }

        /// <summary>丢下某 land 的自有显示；restoreNative=true 时同时还原原生动态槽。</summary>
        private static void DropView(MapMountIconView view, bool restoreNative)
        {
            if (view == null) return;
            DestroyHolder(view);
            if (restoreNative) RestoreSuppressed(view);
        }

        private static class MapIconLog
        {
            private static readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);

            internal static void Once(string key, string message)
            {
                try
                {
                    if (!Keys.Add(key)) return;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[MapIcons] " + message);
                }
                catch (Exception) { }
            }

            internal static void Info(string message)
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[MapIcons] " + message); }
                catch (Exception) { }
            }

            internal static void Warn(string message)
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[MapIcons] " + message); }
                catch (Exception) { }
            }
        }
    }

    [HarmonyPatch(typeof(UILand), nameof(UILand.UpdateLand))]
    internal static class MapMountIcons_UpdateLand_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(UILand __instance, CampaignSaveData.ReignInfo __0, int __1)
        {
            MapMountIcons.OnLandUpdated(__instance, __1, __0);
        }
    }

    [HarmonyPatch(typeof(UIMainMapLand), nameof(UIMainMapLand.OnEnable))]
    internal static class MapMountIcons_ClusterEnable_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(UIMainMapLand __instance)
        {
            MapMountIcons.OnClusterEnabled(__instance);
        }
    }

    [HarmonyPatch(typeof(MapTimelineMenuGreece), nameof(MapTimelineMenuGreece.OnDisable))]
    internal static class MapMountIcons_MenuDisable_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(MapTimelineMenuGreece __instance)
        {
            MapMountIcons.OnMenuDisabled(__instance);
        }
    }

    /// <summary>
    /// 唯一新增入口（r11/V3）：MapTimelineMenu.Update（protected void，无参；Greek 继承且无自己的 Update）
    /// postfix，提供真实跨帧 tick；入口内做 exact Greek/owned map 过滤与状态/帧去重。
    /// </summary>
    [HarmonyPatch(typeof(MapTimelineMenu), "Update")]
    internal static class MapMountIcons_MenuTick_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(MapTimelineMenu __instance)
        {
            MapMountIcons.OnMenuTick(__instance);
        }
    }
}
