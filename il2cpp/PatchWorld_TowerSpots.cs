using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 箭塔基底点位增密（用户需求"生成的倍数"）。
///
/// 原生事实（game-source/Assembly-CSharp-2.1.0 侦查结论，2.4.0 interop 已验签名）：
/// - 场景里的塔位（可购买基底）是关卡作者手工摆设的资产实例，无任何运行时
///   密度参数——原生只有固定点位，不存在"原生加密"入口。
/// - 塔位与已建塔共用 "Tower" 标签（Tags.cs:169；Castle.cs:624
///   FindGameObjectsWithTag("Tower") 同时吃两者）。脚手架是独立标签
///   "ScaffoldingTower"（Castle.cs:625），不污染扫描。
/// - 特殊升级塔（如 Tower Knight）是无标签（Untagged）对象：root 上是
///   WorkableBuilding + ConstructionBuildingComponent + PayableBlocker，
///   没有普通 Tower 组件、不挂 Tower tag——tag 扫描对它全盲。因此占用
///   集在 tag 扫描外，另做一次 FindObjectsOfType&lt;WorkableBuilding&gt;() 与
///   一次 &lt;Scaffolding&gt;()（活跃施工脚手架的 Building 可能 inactive，
///   仅当 activeScaffolding.Building 明确指向它时计入占用；绝不把
///   prefab/inactive pool 对象当建筑）。
/// - 基底预制体 Holder.towerLocationPrefab（Holder.cs:182）是自包含的：
///   自带 PayableUpgrade（价格/nextPrefab=1级塔/区域限制都在资产里序列化）、
///   Persistent（IslandSaveData.RegisterPersistent 经 OnEnable 自动登记）、
///   CRPCStamp（Persistent.IBehaviour，重载后按存档 NetID 重注册）、
///   Tower 组件 level=0（IsImmune，敌人不可摧毁；已建塔 level>=1）。
/// - 原生运行时实例化先例：Tower.DestroyTower（Tower.cs:27-49）——塔被毁后
///   Instantiate(BiomeData.GetAssetSwap(towerLocationPrefab), pos, rot, parent)
///   + NetworkPostbox.RegisterObject（权威端分配新 NetID）即得到一个完全可用的
///   可购买基底。说明场景摆设与运行时 Instantiate 实例行为一致：差异只在
///   网络注册这一步由外部补上（场景实例由关卡生成时的 CRPCStamp.Setup 完成，
///   运行时实例由 DestroyTower 手工完成），本补丁照抄 DestroyTower 配方。
/// - 买基底→出塔链路（PayableUpgrade.Pay，PayableUpgrade.cs:288-380）完全由
///   预制体自带组件闭环：付钱→TransactionComplete（权威端 ReserveNextNetId
///   SemiStatic）→Pay() 原位 Instantiate(GetAssetSwap(nextPrefab))（1 级塔）→
///   RegisterObject(新塔, nextObjectNetId)→IUpgradeable 迁移→销毁基底。
///   补放实例"零接线即可购买"。
/// - 持久化：IslandSaveData.ObjectData 存 netID/crpcType（IslandSaveData.cs:
///   1504-1508 经 GetHeaderFromObject）；重载时 TryCreateOrFind（:822+）按
///   prefabPath+位置重建 CreateObject 模式对象，CRPCStamp.ApplyData/客户端
///   RegisterObject 按存档 NetID 重注册。因此我们注册过的补放实例跨存档行为
///   与原生 DestroyTower 基底一致：存档保留、读档重建、名字保留 KEM 标记。
///
/// 本补丁语义：
/// - World.OnLevelLoaded postfix 协程宿主（PatchWorld_DefenseSpacing 范式，
///   per-world 指针守卫），延迟 5 秒（等场景物体/PayableManager 就绪）后补放；
///   每次关卡加载重放。修复（旧 KEM 重叠回收 + 补点避障）在下一次重启/读档
///   的这一入口内完成，无新增 native hook/周期扫描/scene mutation 插件。
/// - **对原生参考集幂等**（reviewer P0 修正）：间距估计（中位数）与铺点范围
///   （anchor/outermost/外推终点）只从"未购买的原生基底"参考集取——tag Tower
///   + 本层 + 非船 + 名字无 KEM 标记 + Tower.level==0。补放点（有 KEM 标记）
///   与已建塔（level>=1）绝不进参考集：否则首跑后中位数即缩为 S/m、
///   target 跟着缩为 S/m²，每读档密度×2 直到 3 步地板，outermost 也会随补放
///   点逐档外推。参考集在反复读档间不变（补放点重建后仍带 KEM 标记被排除），
///   大量购买后剩余原生样本 gap 恒为 S 或 2S，中位数稳定。
/// - 占用集（距离+footprint 双守卫的数据源）保持全量：原生基底、补放点、
///   已建普通塔（tag Tower）、特殊升级塔（WorkableBuilding）、施工脚手架
///   （active Scaffolding 及其 Building）。防与任何现有结构贴脸，也保证重放时
///   旧补放点把同位网格点全部拦截（added=0）。footprint 比较一律用
///   sameObject=false（Payable 真实范围 + 视觉 bounds 并集），塔对塔绝不套
///   原生 MinSpacing（模板资产 MinSpacing=8，套用会把增密全禁）。
/// - 新实例命名含 "KEM_TowerSpot" 标记（识别自己的放点：参考集排除 + 日志）。
/// - 现有存档读档即生效的依据：补放完全发生在运行时（场景加载后），不改
///   关卡资产、不改存档结构；读档后照常执行。
/// - 联机（NetworkBigBoss.IsOnline）整体跳过：我们只有权威端注册，没有
///   DestroyTower 的 SendDestroyed 式 RPC 通知对端生成（Tower.cs:39-43 的
///   权威分支把新 NetID 发给客户端，本补丁没有对应通道），客户端会看不到
///   权威端的补放点、自己跑又会产生本地未注册副本 → 分叉。单机与同机分屏
///   （COOP_ENABLED 同进程单权威，IsOnline=false）语义完整；联机待实测前
///   fail-closed（SpecialTowerRebuild 同款纪律）。
/// </summary>
public static class PatchWorld_TowerSpots
{
    private const string MarkerPrefix = "KEM_TowerSpot";
    private const float DelaySeconds = 5f;      // 等场景/PayableManager/池就绪
    private const float MinTargetSpacing = 3f;  // 塔宽约 2-3 单位，4x 时防贴脸下限
    // A live native base supplies the rendered width for the new-base spacing floor.
    // ScatteredObject.MinSpacing is layout clearance, not sprite half-width (the
    // current Greek root sprites are 96px at 32 pixels/unit = 3 world units).
    private const float VisualSpacingPadding = 0.25f;
    private const float OccupiedRatio = 0.6f;   // 距离守卫 = 0.6×目标间距
    private const float OutwardExtension = 1f;  // 越过最外侧原生基底再外扩 1 个原生间距
    private const int MaxPerSide = 40;          // 网格点数硬上限（防御性）

    // per-world 指针守卫：赋值时机在全部就绪检查（holder/prefab/参考集）通过
    // 之后、实际放点之前——瞬时未就绪只跳过本次，不会永久吞掉该世界
    // （reviewer minor 修正）。multiplier<=1 与联机早退发生在守卫之前且不
    // 消费守卫：它们是配置/环境态而非瞬时故障，语义与先前一致。
    private static IntPtr _expandedWorld;
    private static IntPtr _expandedLayer;
    private static bool _loggedOnlineSkip;
    private static bool _loggedNoTemplate;
    private static bool _loggedVisualHealth;
    private static bool _loggedScatterMetadata;
    private static bool _loggedOverlapRetirement;

    /// <summary>
    /// 一次 Expand 内的局部占用快照：同 scene、非船、distinct root。
    /// 数据源=tag Tower 扫描（普通塔/已建塔/KEM 基底）+ 一次
    /// FindObjectsOfType&lt;WorkableBuilding&gt;（无标签特殊升级塔）+ 一次
    /// &lt;Scaffolding&gt;（活跃施工脚手架及其 Building）。新生成的 spot 也即时
    /// 加入，保证同轮新点之间做实际 footprint 校验。
    /// </summary>
    private sealed class OccupancySnapshot
    {
        public readonly List<GameObject> Roots = new List<GameObject>();
        public readonly List<float> AllX = new List<float>();
        // 只被 activeScaffolding.Building 明确指向的（可能 inactive 的）建筑
        // root 到脚手架关联；每次占用复查重新核实活动脚手架仍指向该 root。
        public readonly Dictionary<IntPtr, Scaffolding> ScaffoldingBuildings = new Dictionary<IntPtr, Scaffolding>();
        private readonly HashSet<IntPtr> _seen = new HashSet<IntPtr>();

        public bool Add(GameObject go)
        {
            if (go == null || go.transform == null) return false;
            if (!_seen.Add(go.Pointer)) return false;
            Roots.Add(go);
            AllX.Add(go.transform.position.x);
            return true;
        }

        public bool Remove(GameObject go)
        {
            if (go == null) return false;
            if (!_seen.Remove(go.Pointer)) return false;
            Roots.RemoveAll(r => r == null || r.Pointer == go.Pointer);
            float x = XOf(go);
            AllX.Remove(x); // Remove only this root: a different building may have the same x.
            ScaffoldingBuildings.Remove(go.Pointer);
            return true;
        }
    }

    /// <summary>OnLevelLoaded postfix 入口：调度延迟协程。</summary>
    public static void Schedule(World world)
    {
        try
        {
            if (world == null || world.gameObject == null) return;
            world.StartCoroutine(ExpandRoutine(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[TowerSpots] schedule failed: " + e);
        }
    }

    private static IEnumerator ExpandRoutine(World world)
    {
        yield return new WaitForSeconds(DelaySeconds);
        try
        {
            // The delayed callback may outlive the scene that scheduled it.
            // Require the captured world to still be current and ready before
            // scanning or mutating any scene objects.
            if (!TryGetReadyContext(world, world != null ? world.gameLayer : null,
                out _)) yield break;
            ExpandTowerSpots(world);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[TowerSpots] " + e);
        }
    }

    /// <summary>
    /// Readiness gate for delayed scene work and network registration.  Every
    /// caller passes the world/layer captured by OnLevelLoaded; both pointers
    /// must still match Managers.Inst at the instant of the operation.
    /// </summary>
    private static bool TryGetReadyContext(World capturedWorld, Transform capturedLayer,
        out Transform currentLayer)
    {
        currentLayer = null;
        try
        {
            if (!ModConfig.Enabled.Value || NetworkBigBoss.IsOnline
                || !NetworkBigBoss.HasWorldAuth)
                return false;

            Managers managers = Managers.Inst;
            if (capturedWorld == null || capturedWorld.gameObject == null
                || !capturedWorld.gameObject.activeInHierarchy
                || capturedLayer == null || managers == null || managers.game == null
                || managers.world == null || managers.kingdom == null
                || managers.holder == null || managers.holder.towerLocationPrefab == null
                || managers.game.state != Game.State.Playing
                || managers.world.Pointer != capturedWorld.Pointer)
                return false;

            currentLayer = managers.world.gameLayer;
            if (currentLayer == null || currentLayer.Pointer != capturedLayer.Pointer
                || currentLayer.gameObject == null || !currentLayer.gameObject.activeInHierarchy
                || NetworkPostbox.Instance == null)
                return false;
            return true;
        }
        catch
        {
            currentLayer = null;
            return false;
        }
    }

    /// <summary>补放入口（对原生参考集幂等，每次关卡加载重放）。</summary>
    private static void ExpandTowerSpots(World world)
    {
        // 已就绪检查通过并放点过的 world+gameLayer 不重跑；换世界/换岛/读档
        // （scene 重建，gameLayer 指针变化）会重新执行。
        Transform layer = world != null ? world.gameLayer : null;
        if (!TryGetReadyContext(world, layer, out layer)) return;
        if (_expandedWorld == world.Pointer && _expandedLayer == layer.Pointer) return;

        // ---- 就绪检查（瞬时态：未就绪只跳过本次，不消费 per-world 守卫）----
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        Holder holder = managers != null ? managers.holder : null;
        if (kingdom == null || holder == null || holder.towerLocationPrefab == null)
        {
            if (!_loggedNoTemplate)
            {
                _loggedNoTemplate = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[TowerSpots] holder/towerLocationPrefab not ready; skipping");
            }
            return;
        }
        GameObject prefab = null;
        try { prefab = BiomeData.GetAssetSwap<GameObject>(holder.towerLocationPrefab); }
        catch { prefab = null; }
        if (prefab == null) prefab = holder.towerLocationPrefab;
        if (prefab == null) return;

        // ---- 占用快照：tag Tower + WorkableBuilding + Scaffolding（各扫一次）----
        // 占用集：全量（原生基底/补放点/已建普通塔/特殊升级塔/施工脚手架）。
        // 参考集：tag Tower + 本层 + 非船 + 名字无 KEM 标记 + Tower.level==0
        // （=未购买的原生基底；间距估计/铺点范围/朝向模板数据源，绝不混入
        // 补放点与已建塔；特殊塔不据名称猜身份，只进占用集）。
        var snapshot = new OccupancySnapshot();
        var refGos = new List<GameObject>();           // 参考集（原生基底对象）
        var generatedBases = new List<GameObject>();   // 未购买 KEM 基底（旧重叠收敛）
        BuildOccupancySnapshot(layer, snapshot, refGos, generatedBases);

        LogScatterMetadataOnce(prefab);

        // User-authorized same-site duplicates may already be upgraded and have
        // lost the KEM name. Only an independent completed special tower is a
        // witness; release native occupants before deleting any ordinary root.
        var duplicateRoots = SpecialTowerDuplicateCleanup.RemoveDuplicates(world, layer, snapshot.Roots);
        foreach (var removedRoot in duplicateRoots) snapshot.Remove(removedRoot);
        refGos.RemoveAll(go => go == null || !go.activeInHierarchy);
        generatedBases.RemoveAll(go => go == null || !go.activeInHierarchy);

        // 先清旧重叠，再决定是否补新：即使 multiplier<=1 或原生参照不足，
        // 已随存档恢复的、与真实占用（含无标签特殊塔/已建普通塔/施工）重叠的
        // 旧 KEM 空基底也要回收。modDisabled/online/noauth/invalid world 已由
        // TryGetReadyContext 整体拦截。
        int retired = RetireOverlappingGeneratedBases(
            world, layer, prefab, generatedBases, snapshot);
        if (retired > 0 && !_loggedOverlapRetirement)
        {
            _loggedOverlapRetirement = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[TowerSpots] retired overlapping unbuilt KEM bases=" + retired);
        }
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[TowerSpots] scan native=" + refGos.Count
            + " generatedUnbuilt=" + generatedBases.Count
            + " occupied=" + snapshot.Roots.Count
            + " retired=" + retired);

        float multiplier = ModConfig.TowerSpotMultiplier != null
            ? ModConfig.TowerSpotMultiplier.Value : 2f;
        if (multiplier <= 1f) return; // 1=原生密度，不补点（cleanup 已完成）

        // 联机 fail-closed（见类注释）；单机 HasWorldAuth 恒真。
        if (NetworkBigBoss.IsOnline)
        {
            if (!_loggedOnlineSkip)
            {
                _loggedOnlineSkip = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[TowerSpots] online session detected; tower spot expansion " +
                    "is single-player/splitscreen only, skipping");
            }
            return;
        }

        if (refGos.Count < 2) return; // 原生基底参照不足（<2 无法估计间距），不动

        // 地表吸附：取原生基底 y/z 中位数（原生塔位全部贴地，等价于地面线 y；
        // 取舍：假设地面平直——KTC 建造带基本如此，高地/特殊地形由
        // NotBuildable 点检排除，见 TryPlaceX）。z 同理（游戏层平面）。
        List<float> ys = YsOf(refGos), zs = ZsOf(refGos);
        float groundY = ys[ys.Count / 2];
        float planeZ = zs[zs.Count / 2];

        // ---- 世界 x 合法区间：worldBounds 是 Sided<float> 泛型结构体，interop
        // marshal 出垃圾值（PatchWorld_SerpentLeash 实测 4.7e19，坑同源）——
        // 照抄原生公式复刻（World.cs OnLevelLoaded:165-166）：
        //   bounds = ground.x ± GroundCollider.size.x/2 ∓ 8
        float worldLeft = float.MinValue, worldRight = float.MaxValue;
        BoxCollider2D ground = World.GroundCollider;
        if (ground != null && ground.transform != null)
        {
            worldLeft = ground.transform.position.x - ground.size.x / 2f + 8f;
            worldRight = ground.transform.position.x + ground.size.x / 2f - 8f;
        }

        // ---- 两侧（营火左右）分别估计原生间距并补点 ----
        float campfire = kingdom.campfirePosition;
        var leftRef = new List<GameObject>();
        var rightRef = new List<GameObject>();
        for (int i = 0; i < refGos.Count; i++)
        {
            GameObject go = refGos[i];
            if (go == null || go.transform == null) continue;
            if (go.transform.position.x < campfire) leftRef.Add(go);
            else rightRef.Add(go);
        }
        leftRef.Sort(CompareByX);
        rightRef.Sort(CompareByX);

        float? leftNative = MedianGap(leftRef);
        float? rightNative = MedianGap(rightRef);
        float? fallback = leftNative ?? rightNative;
        if (!fallback.HasValue) return;

        int notBuildableMask = LayerMask.GetMask("NotBuildable");

        int added = 0;
        if (leftRef.Count > 0)
        {
            added += ExpandSide(world, prefab, layer, leftRef, snapshot,
                -1f, worldLeft, worldRight, groundY, planeZ,
                leftNative ?? fallback.Value, multiplier, notBuildableMask);
        }
        if (rightRef.Count > 0)
        {
            added += ExpandSide(world, prefab, layer, rightRef, snapshot,
                1f, worldLeft, worldRight, groundY, planeZ,
                rightNative ?? fallback.Value, multiplier, notBuildableMask);
        }

        // Consume the per-world guard only if the captured context remained
        // valid for the complete mutation pass.  A scene transition or lost
        // authority therefore leaves the load eligible for a later retry.
        if (TryGetReadyContext(world, layer, out _))
        {
            _expandedWorld = world.Pointer;
            _expandedLayer = layer.Pointer;
        }

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[TowerSpots] added " + added + " spots (multiplier=" + multiplier.ToString("F2")
            + ", native spacing=" + fallback.Value.ToString("F1")
            + ", native bases=" + refGos.Count
            + ", occupied total=" + snapshot.Roots.Count + ")");
    }

    /// <summary>
    /// 占用快照采集：tag Tower 扫描（普通塔位/已建塔/KEM 基底，须为本层子物体、
    /// 非船）+ 一次 FindObjectsOfType&lt;WorkableBuilding&gt;（无标签特殊升级塔，
    /// 须 active、与 gameLayer 同 scene、非船）+ 一次 &lt;Scaffolding&gt;（活跃
    /// 脚手架及其 Building 引用；Building 可能 inactive，只在被活跃脚手架明确
    /// 指向时计入并记录指针）。不做名称猜测。
    /// </summary>
    private static void BuildOccupancySnapshot(Transform layer,
        OccupancySnapshot snapshot, List<GameObject> refGos, List<GameObject> generatedBases)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> tagged =
            GameObject.FindGameObjectsWithTag("Tower");
        if (tagged != null)
        {
            for (int i = 0; i < tagged.Length; i++)
            {
                GameObject go = tagged[i];
                if (go == null || go.transform == null
                    || !go.transform.IsChildOf(layer)) continue;
                if (IsOnBoat(go.transform)) continue;
                snapshot.Add(go);
                if (IsNativeBase(go)) refGos.Add(go);
                else if (IsGeneratedBase(go)) generatedBases.Add(go);
            }
        }

        // FindObjectsOfType 只返回 active 对象，天然排除 prefab/inactive pool。
        var workableBuildings = UnityEngine.Object.FindObjectsOfType<WorkableBuilding>();
        if (workableBuildings != null)
        {
            for (int i = 0; i < workableBuildings.Length; i++)
            {
                WorkableBuilding wb = workableBuildings[i];
                if (wb == null) continue;
                GameObject go = wb.gameObject;
                if (go == null || go.transform == null) continue;
                if (!go.activeInHierarchy) continue;
                if (!IsInLayerScene(go, layer)) continue;
                if (IsOnBoat(go.transform)) continue;
                snapshot.Add(go);
            }
        }

        var scaffoldings = UnityEngine.Object.FindObjectsOfType<Scaffolding>();
        if (scaffoldings != null)
        {
            for (int i = 0; i < scaffoldings.Length; i++)
            {
                Scaffolding scaffold = scaffoldings[i];
                if (scaffold == null) continue;
                GameObject go = scaffold.gameObject;
                if (go == null || go.transform == null) continue;
                if (!go.activeInHierarchy) continue;
                if (!IsInLayerScene(go, layer)) continue;
                if (IsOnBoat(go.transform)) continue;
                snapshot.Add(go);

                // Building 可能 inactive（脚手架 Setup 期间），只有这条明确
                // 引用能把它计入占用——其余 inactive 对象一律不算建筑。
                GameObject building = scaffold.Building;
                if (building == null || building.transform == null) continue;
                if (!IsInLayerScene(building, layer)) continue;
                if (IsOnBoat(building.transform)) continue;
                snapshot.Add(building);
                snapshot.ScaffoldingBuildings[building.Pointer] = scaffold;
            }
        }
    }

    /// <summary>对象与 gameLayer 属同一 scene（防御 Resources/DDOL 资产混入）。</summary>
    private static bool IsInLayerScene(GameObject go, Transform layer)
    {
        if (go == null || layer == null || layer.gameObject == null) return false;
        return go.transform != null && go.transform.IsChildOf(layer)
                && go.scene.handle == layer.gameObject.scene.handle;
    }

    /// <summary>
    /// footprint 占用检查：candidate（实例或 prefab+建议 x）与快照中每个占用
    /// root 的 TryGetCombinedOverlapRegion（sameObject=false：Payable 真实范围 +
    /// 视觉 bounds 并集；塔对塔绝不套原生 MinSpacing）比较。逐对象复查
    /// active 与同 scene/层级；inactive root 必须仍有关联活动脚手架。
    /// 不可判定返回 Unknown：禁止新放，但不能据此删除旧对象。
    /// </summary>
    private enum OverlapResult { Clear, Overlap, Unknown }

    private static bool IsLiveOccupant(OccupancySnapshot snapshot, GameObject root, Transform layer)
    {
        if (root == null || !IsInLayerScene(root, layer) || IsOnBoat(root.transform)) return false;
        if (root.activeInHierarchy) return true;
        if (!snapshot.ScaffoldingBuildings.TryGetValue(root.Pointer, out Scaffolding scaffold)
            || scaffold == null || scaffold.gameObject == null || !scaffold.gameObject.activeInHierarchy
            || !IsInLayerScene(scaffold.gameObject, layer) || IsOnBoat(scaffold.transform)) return false;
        return scaffold.Building != null && scaffold.Building.Pointer == root.Pointer;
    }

    private static OverlapResult OverlapsOccupiedRoots(OccupancySnapshot snapshot,
        GameObject candidate, float x, GameObject ignoreRoot, Transform layer,
        World world = null, bool skipRemovablePeers = false)
    {
        try
        {
            if (!TryGetCombinedOverlapRegion(candidate, x, false, out Rect candidateRect)) return OverlapResult.Unknown;
            bool unknown = false;
            for (int i = 0; i < snapshot.Roots.Count; i++)
            {
                GameObject root = snapshot.Roots[i];
                if (root == null || IsSameHierarchy(root, ignoreRoot)) continue;
                try
                {
                    if (!IsLiveOccupant(snapshot, root, layer)) continue;
                    if (skipRemovablePeers && IsGeneratedBase(root) && CanRetireGeneratedBaseInSnapshot(root, world, layer, snapshot)) continue;
                    if (!TryGetCombinedOverlapRegion(root, root.transform.position.x, false, out Rect occupied))
                    { unknown = true; continue; }
                    if (candidateRect.Overlaps(occupied)) return OverlapResult.Overlap;
                }
                catch { unknown = true; }
            }
            return unknown ? OverlapResult.Unknown : OverlapResult.Clear;
        }
        catch { return OverlapResult.Unknown; }
    }

    /// <summary>
    /// 参考集判据：未购买的原生基底——名字无 KEM 标记（补放点排除）且
    /// Tower 组件存在且 level==0（已建塔/特殊塔 level>=1 或无 Tower 组件，
    /// 排除）。调用方已保证 tag/本层/非船。
    /// </summary>
    private static bool IsNativeBase(GameObject go)
    {
        try
        {
            string n = go.name;
            if (n != null && n.StartsWith(MarkerPrefix)) return false;
            Tower tower = go.GetComponent<Tower>();
            return tower != null && tower.level == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGeneratedBase(GameObject go)
    {
        try
        {
            string n = go.name;
            if (n == null || !n.StartsWith(MarkerPrefix)) return false;
            Tower tower = go.GetComponent<Tower>();
            return tower != null && tower.level == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 复刻 Level.PopulateRegionWithScatteredObjects 的 AvoidOverlapWith 横向矩形
    /// 判定。Tower 标签有意跳过：同类塔位距离仍由本补丁的目标倍数/占用快照
    /// 管理，否则原生 MinSpacing 会把所有增密点重新全部挡掉；建筑/墙/农场等
    /// 其余标签完整保留原生避障语义。
    /// </summary>
    private static bool OverlapsNativePlacement(GameObject prefab, Transform layer,
        float x, GameObject ignoreRoot, out string blockedTag, GameObject footprintSource = null)
    {
        blockedTag = null;
        try
        {
            ScatteredObject scatter = prefab != null
                ? prefab.GetComponent<ScatteredObject>() : null;
            if (scatter == null || scatter.AvoidOverlapWith == null) return false;

            // Keep the native Payable rectangle, then conservatively union it
            // with the rendered footprint.  This catches the real visual
            // overlap reported by players while preserving native metadata as
            // the first source of truth.
            if (!TryGetCombinedOverlapRegion(footprintSource ?? prefab, x, false, out Rect candidate))
            { blockedTag = "check-error"; return true; }
            var avoidTags = scatter.AvoidOverlapWith;
            for (int tagIndex = 0; tagIndex < avoidTags.Count; tagIndex++)
            {
                string tag = avoidTags[tagIndex];
                if (string.IsNullOrEmpty(tag) || tag == "Tower") continue;

                var objects = GameObject.FindGameObjectsWithTag(tag);
                for (int i = 0; i < objects.Length; i++)
                {
                    GameObject other = objects[i];
                    if (other == null || other.transform == null
                        || IsSameHierarchy(other, ignoreRoot)
                        || IsOnBoat(other.transform)) continue;

                    // 原生只比较当前 Level 层级；场景句柄再做一次防御，避免
                    // Resources/DontDestroyOnLoad 中同 tag 资产进入判定。
                    Managers managers = Managers.Inst;
                    Level level = managers != null ? managers.level : null;
                    if (level != null && level.transform != null
                        && !other.transform.IsChildOf(level.transform)) continue;
                    if (layer != null && other.scene.handle
                        != layer.gameObject.scene.handle) continue;

                    if (!TryGetCombinedOverlapRegion(other, other.transform.position.x, false, out Rect occupied))
                    { blockedTag = "check-error"; return true; }
                    if (!candidate.Overlaps(occupied)) continue;
                    blockedTag = tag;
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[TowerSpots] overlap check failed closed at x=" + x.ToString("F1")
                + ": " + e.Message);
            blockedTag = "check-error";
            return true;
        }
        return false;
    }

    private static Rect GetOverlapRegion(GameObject go, float x, bool sameObject)
    {
        ScatteredObject scatter = go != null ? go.GetComponent<ScatteredObject>() : null;
        if (scatter != null && (sameObject || !scatter.UsePayableForSpacing))
        {
            float spacing = Mathf.Max(0.5f, scatter.MinSpacing);
            return new Rect(new Vector2(x - spacing, 50f),
                new Vector2(spacing * 2f, 100f));
        }

        Payable payable = go != null ? go.GetComponent<Payable>() : null;
        if (payable != null)
        {
            if (!float.IsFinite(payable.playerPayDistance) || !float.IsFinite(payable.playerPayPointOffset.x))
                throw new InvalidOperationException("Invalid payable footprint");
            float distance = Mathf.Max(0.5f, payable.playerPayDistance);
            return new Rect(new Vector2(
                x + payable.playerPayPointOffset.x - distance, 50f),
                new Vector2(distance * 2f, 100f));
        }

        return new Rect(new Vector2(x - 0.5f, 50f), new Vector2(1f, 100f));
    }

    private static bool TryGetCombinedOverlapRegion(GameObject go, float x, bool sameObject, out Rect result)
    {
        result = default;
        try
        {
            if (!TryGetVisualBounds(go, x, out float visualMin, out float visualMax)) return false;
            Rect native = GetOverlapRegion(go, x, sameObject);
            if (!float.IsFinite(native.xMin) || !float.IsFinite(native.xMax) || native.xMax <= native.xMin) return false;
            float min = Mathf.Min(native.xMin, visualMin), max = Mathf.Max(native.xMax, visualMax);
            result = Rect.MinMaxRect(min, 50f, max, 150f);
            return float.IsFinite(min) && float.IsFinite(max) && max > min;
        }
        catch { return false; }
    }

    private static bool TryGetVisualBounds(GameObject go, float x, out float minX, out float maxX)
    {
        minX = maxX = x;
        try
        {
            if (go == null || go.transform == null || !float.IsFinite(x)) return false;
            float rootX = go.transform.position.x;
            if (!float.IsFinite(rootX)) return false;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                Bounds bounds = renderer.bounds;
                float lo = bounds.min.x, hi = bounds.max.x;
                if (!float.IsFinite(lo) || !float.IsFinite(hi)) return false;
                if (hi <= lo) continue; // Disabled/empty child bounds must not drag a building's span to world zero.
                if (!found) { minX = lo; maxX = hi; found = true; }
                else { minX = Mathf.Min(minX, lo); maxX = Mathf.Max(maxX, hi); }
            }
            if (!found) return false;
            float delta = x - rootX;
            minX += delta; maxX += delta;
            return float.IsFinite(minX) && float.IsFinite(maxX) && maxX > minX;
        }
        catch { minX = maxX = x; return false; }
    }

    private static bool IsSameHierarchy(GameObject candidate, GameObject root)
    {
        if (candidate == null || root == null) return false;
        if (candidate == root) return true;
        try
        {
            return candidate.transform.IsChildOf(root.transform)
                || root.transform.IsChildOf(candidate.transform);
        }
        catch { throw; }
    }

    /// <summary>
    /// 旧 KEM 空基底重叠回收。障碍=占用快照中除"可安全删除的 generated peer"
    /// 外的全部 root（原生基底、已建普通塔、无标签特殊塔、施工脚手架/Building、
    /// 不可删除 KEM）；可安全删除的 generated peer 之间用 keptGenerated 稳定
    /// x 顺序保留先者。candidate 用 spot 实际范围（非 prefab 范围）。每个
    /// 删除动作前用 CanRetireGeneratedBase 全面重验。
    /// </summary>
    private static int RetireOverlappingGeneratedBases(World world, Transform layer,
        GameObject prefab, List<GameObject> generatedBases, OccupancySnapshot snapshot)
    {
        if (!TryGetReadyContext(world, layer, out _)) return 0;
        generatedBases.Sort((a, b) => XOf(a).CompareTo(XOf(b)));
        var keptGenerated = new List<GameObject>();
        int retired = 0;
        foreach (GameObject spot in generatedBases)
        {
            if (!TryGetReadyContext(world, layer, out _)) break;
            if (!CanRetireGeneratedBaseInSnapshot(spot, world, layer, snapshot)) continue;
            float oldX = spot.transform.position.x;
            OverlapResult collision = OverlapsOccupiedRoots(snapshot, spot, oldX, spot, layer, world, true);
            bool nativeCollision = OverlapsNativePlacement(prefab, layer, oldX, spot, out string blockedTag, spot);
            bool confirmed = collision == OverlapResult.Overlap || (nativeCollision && blockedTag != "check-error");
            if (!confirmed && TryGetCombinedOverlapRegion(spot, oldX, false, out Rect candidate))
            {
                foreach (GameObject previous in keptGenerated)
                {
                    if (!IsLiveOccupant(snapshot, previous, layer)) continue;
                    if (!TryGetCombinedOverlapRegion(previous, previous.transform.position.x, false, out Rect occupied)) continue;
                    if (!candidate.Overlaps(occupied)) continue;
                    confirmed = true; blockedTag = MarkerPrefix; break;
                }
            }
            if (!confirmed) { keptGenerated.Add(spot); continue; }
            if (!CanRetireGeneratedBaseInSnapshot(spot, world, layer, snapshot)) continue;
            bool counted = false;
            try
            {
                Persistent persistent = spot.GetComponent<Persistent>();
                CRPCHeader header = NetworkPostbox.Instance.GetHeaderFromObject(spot, true);
                // All ownership and payment checks run directly before the first mutation.
                if (!CanRetireGeneratedBaseInSnapshot(spot, world, layer, snapshot)) continue;
                NetworkPostbox.Instance.DeregisterObject(header);
                persistent.DontPersistInstance(true);
                spot.SetActive(false);
                if (spot.activeInHierarchy) continue;
                snapshot.Remove(spot); retired++; counted = true;
                UnityEngine.Object.Destroy(spot);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[TowerSpots] retired overlap x=" + oldX.ToString("F1")
                    + " cause=" + (blockedTag ?? "occupied-building"));
            }
            catch (Exception e)
            {
                // Keep an unresolved live root as an obstacle. Count a completed deactivation even if a callback threw.
                try
                {
                    if (!counted && (spot == null || !spot.activeInHierarchy))
                    { snapshot.Remove(spot); retired++; }
                }
                catch { }
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogError("[TowerSpots] overlap retirement failed: " + e); } catch { }
                break; // Do not continue a deletion pass after a partial native failure.
            }
        }
        return retired;
    }

    /// <summary>
    /// KEM 空基底的全面可删性重验：marker 名、active、仍在当前世界 scene、
    /// 非船、Tower.level==0、Persistent、SemiStatic 网络头、带 PayableUpgrade、
    /// Payable 未被任何玩家选中/交互（selectedByP1/P2/interactingPlayer/
    /// PlayerSelecting）、无 ConstructionBuildingComponent/WorkableBuilding/
    /// Scaffolding（保守跳过），且 playerOne/playerTwo 的 selectedPayable 与
    /// _completingPayable 均与本 root 无同层级关联。任一条不满足即 fail-closed。
    /// </summary>
    private static bool CanRetireGeneratedBaseInSnapshot(GameObject spot, World world, Transform layer, OccupancySnapshot snapshot)
    {
        try
        {
            return CanRetireGeneratedBase(spot, world, layer)
                && !SpecialTowerDuplicateCleanup.HasAssociatedScaffolding(snapshot.Roots, spot);
        }
        catch { return false; }
    }

    private static bool CanRetireGeneratedBase(GameObject spot, World world, Transform layer)
    {
        try
        {
            if (!TryGetReadyContext(world, layer, out _) || spot == null || spot.transform == null) return false;
            string n = spot.name;
            if (n == null || !n.StartsWith(MarkerPrefix)) return false;
            if (!spot.activeInHierarchy) return false;
            if (IsOnBoat(spot.transform)) return false;
            if (!IsInLayerScene(spot, layer)) return false;
            Managers managers = Managers.Inst;
            if (managers == null || managers.world == null
                || managers.world.Pointer != world.Pointer) return false;

            Tower tower = spot.GetComponent<Tower>();
            Persistent persistent = spot.GetComponent<Persistent>();
            CRPCHeader header = NetworkPostbox.Instance != null
                ? NetworkPostbox.Instance.GetHeaderFromObject(spot, true) : null;
            if (tower == null || tower.level != 0 || persistent == null
                || header == null || header.HeaderType != CRPCType.SemiStatic)
                return false;
            if (spot.GetComponent<PayableUpgrade>() == null) return false;
            if (SpecialTowerDuplicateCleanup.IsProtectedHierarchy(spot, layer)) return false;
            if (!SpecialTowerDuplicateCleanup.IsPaymentClear(spot)) return false;
            // The broad legacy empty-base path must not bypass the new helper's
            // occupant or special-hierarchy protection if that helper retained it.
            if (spot.GetComponentsInChildren<Archer>(true).Length != 0
                || spot.GetComponentsInChildren<GuardSlot>(true).Length != 0
                || spot.GetComponentsInChildren<SpecialTowerRebuildMarker>(true).Length != 0
                || spot.GetComponentsInChildren<TowerKnight>(true).Length != 0
                || spot.GetComponentsInChildren<Ballista>(true).Length != 0
                || spot.GetComponentsInChildren<FireTower>(true).Length != 0
                || spot.GetComponentsInChildren<Baker>(true).Length != 0
                || spot.GetComponentsInChildren<OilFireArcherTower>(true).Length != 0) return false;

            // 施工/可作业组件在场：一律保守跳过（不能删半付费/施工对象）。
            if (spot.GetComponent<ConstructionBuildingComponent>() != null
                || spot.GetComponent<WorkableBuilding>() != null
                || spot.GetComponent<Scaffolding>() != null)
                return false;

            Payable payable = spot.GetComponent<Payable>();
            if (payable != null
                && (payable.selectedByP1 || payable.selectedByP2
                    || payable.interactingPlayer != null
                    || payable.PlayerSelecting != null))
                return false;
            if (IsPlayerEngagedWith(spot)) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// playerOne/playerTwo 的 selectedPayable（公开属性）与
    /// _completingPayable（已核实的实际interop属性，直读失败则保守保留）是否与 spot 同 root/层级关联。
    /// </summary>
    private static bool IsPlayerEngagedWith(GameObject spot)
    {
        try
        {
            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return true;
            Player[] players = new Player[] { kingdom.playerOne, kingdom.playerTwo };
            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                if (player == null) continue;
                Payable selected = player.selectedPayable;
                if (selected != null && IsSameHierarchy(selected.gameObject, spot))
                    return true;
                Payable completing = player._completingPayable;
                if (completing != null && IsSameHierarchy(completing.gameObject, spot))
                    return true;
            }
        }
        catch { return true; } // 不可判定=可能正被付款，fail-closed
        return false;
    }



    private static void LogScatterMetadataOnce(GameObject prefab)
    {
        if (_loggedScatterMetadata) return;
        _loggedScatterMetadata = true;
        try
        {
            ScatteredObject scatter = prefab != null
                ? prefab.GetComponent<ScatteredObject>() : null;
            if (scatter == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[TowerSpots] template has no ScatteredObject; native overlap tags unavailable");
                return;
            }

            string tags = "";
            if (scatter.AvoidOverlapWith != null)
            {
                for (int i = 0; i < scatter.AvoidOverlapWith.Count; i++)
                {
                    if (i > 0) tags += ",";
                    tags += scatter.AvoidOverlapWith[i];
                }
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[TowerSpots] template scatter usePayable=" + scatter.UsePayableForSpacing
                + " minSpacing=" + scatter.MinSpacing.ToString("F1")
                + " avoid=[" + tags + "]");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[TowerSpots] template metadata unavailable: " + e.Message);
        }
    }

    /// <summary>
    /// 单侧补点：把该侧原生基底参考集按"原生间距/倍数"的目标网格在内侧锚点
    /// 与外侧延长线（最外侧原生基底再外扩 1 个原生间距，钳到世界边界）之间
    /// 铺开。anchor/outermost/间距只来自参考集（幂等根基）；每个网格点过三道
    /// 放置守卫（与占用集全量 any 点距离 &gt; 0.6×目标间距；对占用快照的实际
    /// footprint 复核，sameObject=false；NotBuildable 点检为空），通过才实例化。
    /// 新点实时追加进占用快照，保证同轮铺开的相邻新点也满足间距与 footprint
    /// 校验（永不进参考集）。
    /// </summary>
    private static int ExpandSide(
        World world, GameObject prefab, Transform layer, List<GameObject> sideRef,
        OccupancySnapshot snapshot, float dir,
        float worldLeft, float worldRight, float groundY, float planeZ,
        float nativeSpacing, float multiplier, int notBuildableMask)
    {
        float anchor = XOf(sideRef[dir < 0f ? sideRef.Count - 1 : 0]);
        GameObject template = NearestGo(sideRef, anchor);
        if (template == null || !TryGetVisualBounds(template, anchor, out float left, out float right)) return 0;
        float target = nativeSpacing / multiplier;
        float visualHalfWidth = (right - left) * 0.5f;
        if (visualHalfWidth > 0f)
        {
            target = Mathf.Max(target,
                visualHalfWidth * 2f + VisualSpacingPadding);
        }
        if (target < MinTargetSpacing) target = MinTargetSpacing;
        float occupied = target * OccupiedRatio;

        // 锚点=该侧最靠近营火的原生基底；终点=最外侧原生基底向外再延 1 个
        // 原生间距（方向=离开营火），钳到世界边界（留 2 单位余量）。
        // 两者都取自参考集：补放点再靠外也不推进终点（幂等根基）。
        float outermost = XOf(sideRef[dir < 0f ? 0 : sideRef.Count - 1]);         // 最外
        float rawEnd = outermost + dir * nativeSpacing * OutwardExtension;
        float end = Mathf.Clamp(rawEnd,
            worldLeft == float.MinValue ? rawEnd : worldLeft + 2f,
            worldRight == float.MaxValue ? rawEnd : worldRight - 2f);

        // 朝向/缩放模板：最近的原生基底（左右两侧贴图镜像一致）
        int added = 0;
        int steps = 0;
        for (float x = anchor + dir * target; ; x += dir * target)
        {
            if ((dir < 0f && x < end) || (dir > 0f && x > end)) break;
            if (++steps > MaxPerSide) break;

            if (!IsFree(snapshot.AllX, x, occupied)) continue;
            // Native tower locations follow the local ground height.  Using a
            // single island-wide median puts generated bases below/above the
            // terrain on sloped sections: the Payable/CRPC components remain
            // interactive while the sprite is occluded.  Reuse the nearest
            // native base height for each candidate, with the median only as a
            // defensive fallback when a transform is unavailable.
            float spawnY = GroundYForX(sideRef, x, groundY);
            if (!TryPlaceX(x, spawnY, notBuildableMask)) continue;
            if (OverlapsNativePlacement(prefab, layer, x, null, out _, template)) continue;
            // 占用快照（含已建普通塔/无标签特殊塔/施工/同轮新点）的实际
            // footprint 复核；异常 fail-closed=不放。
            if (OverlapsOccupiedRoots(snapshot, template, x, null, layer) != OverlapResult.Clear) continue;

            if (SpawnSpot(world, prefab, layer, template, x, spawnY, planeZ, snapshot))
                added++;
        }
        return added;
    }

    private static float GroundYForX(List<GameObject> sideRef, float x, float fallback)
    {
        GameObject nearest = NearestGo(sideRef, x);
        try
        {
            if (nearest != null && nearest.transform != null)
                return nearest.transform.position.y;
        }
        catch { }
        return fallback;
    }

    /// <summary>
    /// 实例化一个基底：照抄 Tower.DestroyTower 配方（Instantiate 资产换皮
    /// 预制体 → 挂 gameLayer → 权威端 RegisterObject(SemiStatic) 分配新
    /// NetID）。朝向/缩放抄该侧最近原生基底（镜像一致）；命名带 KEM 标记。
    /// 成功后即时加入占用快照（同轮 footprint/距离校验的数据源）。
    /// </summary>
    private static bool SpawnSpot(
        World world, GameObject prefab, Transform layer, GameObject template,
        float x, float y, float z, OccupancySnapshot snapshot)
    {
        // Never create an object that cannot be registered into the current
        // authoritative scene.  This is intentionally checked immediately
        // before Instantiate as well as by the outer expansion gate.
        if (!TryGetReadyContext(world, layer, out _)) return false;
        GameObject spot = UnityEngine.Object.Instantiate(
            prefab, new Vector3(x, y, z), Quaternion.identity, layer);
        if (spot == null) return false;

        try
        {
            // Unity preserves the prefab active flag during Instantiate.  A
            // hidden location prefab would still leave Payable/CRPC objects
            // registered while making the base invisible; native runtime
            // replacement always returns an active location, so normalize the
            // root only (never force child renderers or payable state).
            if (!spot.activeSelf) spot.SetActive(true);

            // 朝向/缩放抄原生基底；模板缺失时保持预制体默认（DestroyTower 对
            // 换皮基底也直接用默认缩放，仅美观差异）。
            if (template != null && template.transform != null)
            {
                spot.transform.rotation = template.transform.rotation;
                spot.transform.localScale = template.transform.localScale;
            }

            // 原生 Level 散布对象路径在网络注册前固定对称朝向与 Y/Z。补放塔基
            // 属于同一散布语义，复刻该顺序可避免 FixedTransform.Start 下一帧才
            // 改 Z/scale，造成首帧注册位置与最终视觉层不一致。
            FixedTransform fixedTransform = spot.GetComponent<FixedTransform>();
            if (fixedTransform != null) fixedTransform.Fix();

            // 标记名（幂等识别：参考集排除 + 读档恢复时 ObjectData.name 保留）
            spot.name = MarkerPrefix + "_" + x.ToString("F1");

            // 网络注册（DestroyTower 权威端同款）：给 Payable/Tower 的 IRPCable
            // 分配 SemiStatic NetID；RegisterObject 内部查重，重复调用安全。
            // 之后玩家购买走原生 PayableUpgrade.Pay（新塔用 nextObjectNetId
            // 注册），存档走 ObjectData(netID)/CRPCStamp——全部原生闭环。
            if (!TryGetReadyContext(world, layer, out _))
                throw new InvalidOperationException("tower spot registration context lost");
            NetworkPostbox.Instance.RegisterObject(spot, CRPCType.SemiStatic);

            if (!_loggedVisualHealth)
            {
                _loggedVisualHealth = true;
                Renderer[] renderers = spot.GetComponentsInChildren<Renderer>(true);
                int enabledRenderers = 0;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null && renderers[i].enabled) enabledRenderers++;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[TowerSpots] visual health: rootActive=" + spot.activeInHierarchy
                    + " renderers=" + renderers.Length
                    + " enabled=" + enabledRenderers
                    + " x=" + x.ToString("F1")
                    + " y=" + y.ToString("F1"));
            }

            // 新点立即进占用快照（仅占用集！进参考集会破坏幂等）：同轮后续
            // 网格点/另一侧扫描都会避开它。
            snapshot.Add(spot);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[TowerSpots] spot setup failed at x=" + x.ToString("F1") + ": " + e);
            try { UnityEngine.Object.Destroy(spot); } catch { }
            return false;
        }
    }

    /// <summary>网格点合法性：与占用集（全量）所有 x 的最小距离 &gt; 阈值。</summary>
    private static bool IsFree(List<float> allX, float x, float occupied)
    {
        for (int i = 0; i < allX.Count; i++)
        {
            if (Mathf.Abs(allX[i] - x) <= occupied) return false;
        }
        return true;
    }

    private static float GetVisualHalfWidth(GameObject go)
    {
        try
        {
            float halfWidth = 0f;
            ScatteredObject scatter = go != null
                ? go.GetComponent<ScatteredObject>() : null;
            if (scatter != null)
            {
                float half = scatter.GetHalfWidth();
                if (half > 0f && !float.IsNaN(half) && !float.IsInfinity(half))
                    halfWidth = half;
            }

            Renderer[] renderers = go != null
                ? go.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null || renderers.Length == 0) return halfWidth;
            bool found = false;
            float min = 0f;
            float max = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                Bounds bounds = renderer.bounds;
                if (!found)
                {
                    min = bounds.min.x;
                    max = bounds.max.x;
                    found = true;
                }
                else
                {
                    min = Mathf.Min(min, bounds.min.x);
                    max = Mathf.Max(max, bounds.max.x);
                }
            }
            if (found)
                halfWidth = Mathf.Max(halfWidth, (max - min) * 0.5f);
            return halfWidth;
        }
        catch { return 0f; }
    }

    /// <summary>
    /// 地形合法性：镜像原生 InvalidRegion 检查（PayableUpgrade.IsLockedForReason
    /// :115 —— Physics2D.OverlapPoint(pos+(0,0.5), "NotBuildable")）。原生
    /// 塔位的 onlyInBuildableRegion 锁会把放在非法区的补放点永远锁死，与其
    /// 放出一个买不了的点，不如放点前就跳过。
    /// </summary>
    private static bool TryPlaceX(float x, float y, int notBuildableMask)
    {
        try
        {
            Collider2D hit = Physics2D.OverlapPoint(
                new Vector2(x, y + 0.5f), notBuildableMask);
            return hit == null;
        }
        catch
        {
            return false; // 检查不可用=不放（fail-closed）
        }
    }

    /// <summary>参考集相邻间距中位数（列表需按 x 升序）。</summary>
    private static float? MedianGap(List<GameObject> sortedRef)
    {
        if (sortedRef == null || sortedRef.Count < 2) return null;
        var gaps = new List<float>();
        for (int i = 1; i < sortedRef.Count; i++)
        {
            float gap = XOf(sortedRef[i]) - XOf(sortedRef[i - 1]);
            if (gap > 0.5f) gaps.Add(gap); // 忽略同点重复/贴脸异常
        }
        if (gaps.Count == 0) return null;
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    /// <summary>参考集中 x 最接近 target 的原生基底（朝向/缩放模板）。</summary>
    private static GameObject NearestGo(List<GameObject> refGos, float targetX)
    {
        GameObject best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < refGos.Count; i++)
        {
            GameObject go = refGos[i];
            if (go == null || go.transform == null) continue;
            float dist = Mathf.Abs(go.transform.position.x - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = go;
            }
        }
        return best;
    }

    private static float XOf(GameObject go)
    {
        return go != null && go.transform != null ? go.transform.position.x : 0f;
    }

    private static List<float> YsOf(List<GameObject> gos)
    {
        var ys = new List<float>();
        for (int i = 0; i < gos.Count; i++)
            if (gos[i] != null && gos[i].transform != null)
                ys.Add(gos[i].transform.position.y);
        ys.Sort();
        return ys;
    }

    private static List<float> ZsOf(List<GameObject> gos)
    {
        var zs = new List<float>();
        for (int i = 0; i < gos.Count; i++)
            if (gos[i] != null && gos[i].transform != null)
                zs.Add(gos[i].transform.position.z);
        zs.Sort();
        return zs;
    }

    private static int CompareByX(GameObject a, GameObject b)
    {
        return XOf(a).CompareTo(XOf(b));
    }

    /// <summary>祖先名含 "Boat" 判定（SpecialTowerRebuildDiagnostics 先例）。</summary>
    private static bool IsOnBoat(Transform t)
    {
        try
        {
            Transform walker = t.parent;
            for (int depth = 0; depth < 4 && walker != null; depth++)
            {
                string n = walker.name;
                if (n != null && n.Contains("Boat")) return true;
                walker = walker.parent;
            }
        }
        catch { throw; }
        return false;
    }
}

/// <summary>
/// World.OnLevelLoaded postfix 宿主：每次关卡加载（新岛/新战役/读档）调度
/// 延迟补放协程。per-world 指针守卫在 ExpandTowerSpots 内部、全部就绪检查
/// 通过之后才消费。
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_TowerSpots_Expand_Host_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        PatchWorld_TowerSpots.Schedule(__instance);
    }
}
