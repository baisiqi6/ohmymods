using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 墙基（可购买城墙地基，tag "WallFoundation"/Wall0）密度滑块（Issue #125）。
///
/// 原生事实（存档取证 evidence-save-parse-20261004 + 资产扫描，审查 #1 已核）：
/// - 墙基是运行时动态生成的持久化对象：存档条目 name=Wall0(Clone)、
///   netID 已分配、crpcType=2（SemiStatic）、prefabPath=
///   "Prefabs/Buildings and Interactive/Wall0"。起始岛仅 4 个静态摆设，外档
///   全部动态补出；Holder.wallLocationPrefab 与 towerLocationPrefab 对称。
/// - 未建墙基没有 Wall 组件：形态是 PayableUpgrade.nextPrefab 直接指向带 Wall
///   的预制体（MainBankerFixedDomain 已证）。购买后原生 Pay 原位实例化墙体
///   （Wall1 木/Wall2 石独立持久化）并销毁基底——与塔基先例一致。
/// - 真实档距极度非均匀（6.7~142，随岛建造带变化），不存在均匀档位梯。
///
/// 本补丁语义（审查 #1 修正后定稿）：
/// - **per-gap 细分**：只对每侧相邻原生墙基间隙内部补点，目标间距=
///   max(gap/m, 视觉宽×2+padding, 3.0 地板)；放不下的间隙少放或不放并日志
///   钳制。绝不越过该侧最内原生档（银行家固定域 MainBankerFixedDomain 以
///   最内墙基划域，审查 P0-3），也绝不超出最外原生档（扩张带不预放）。
/// - **双触发**：World.OnLevelLoaded postfix 延迟 5s（加载时已存在的原生档）
///   + Kingdom.OnBordersChanged 订阅（per-Kingdom 重绑，CoinCourier 纪律）延迟
///   2s 重查。漏一次触发只延迟到下一触发/读档（每档基都是普通持久化对象，
///   无正确性风险）；≥5s 节流 + 单次 in-flight 门防事件风暴。
/// - **注册**：无条件 RegisterObject(SemiStatic)（动态原生档实证同语义）。
/// - **幂等**：KEM_WallSpot 名字标记；参考集（原生间隙端点）永不混入 KEM 点；
///   占用集全量（WallFoundation/Wall/WallWreck/ScaffoldingWall/Tower 系）。
///   反复读档密度不爬升；KEM 名字+netID 跨存档保留（KEM_TowerSpot 同管线
///   本机存档 19 条再证）。
/// - **回收**：滑块降档/关闭后，不再位于当前网格（或与真实占用重叠）的未购
///   KEM 墙基在下一轮触发时回收（网格成员资格回收，审查 P2-1）；已购墙保留。
/// - **联机 fail-closed**：NetworkBigBoss.IsOnline 整体跳过（塔基同纪律）。
/// </summary>
public static class PatchWorld_WallSpots
{
    private const string MarkerPrefix = "KEM_WallSpot";
    private const float LoadDelaySeconds = 5f;        // 等场景/PayableManager/holder 就绪
    private const float BorderRecheckDelaySeconds = 2f; // 等新档基随边界扩张稳定出现
    private const float MinPassIntervalSeconds = 5f;  // 事件风暴节流（level-load 豁免）
    private const float MinTargetSpacing = 3f;        // 塔基同款交互/视觉地板
    private const float VisualSpacingPadding = 0.25f;
    private const float OccupiedRatio = 0.6f;
    private const float GapEndMargin = 0.5f;          // 网格点距间隙端点的最小余量
    private const int MaxPerSide = 60;                // 防御性硬上限

    private static bool _running;                     // 单轮 in-flight 门（主线程同步，双保险）
    private static float _lastPassTime = float.MinValue;
    private static float _lastRecheckSchedule = float.MinValue;
    private static bool _loggedNoTemplate;
    private static bool _loggedOnlineSkip;

    // 订阅生命周期（per-Kingdom 重绑；换岛/读档换 Kingdom 实例）
    private static Kingdom _subscribedKingdom;
    private static Il2CppSystem.Action _borderHandler;

    /// <summary>World.OnLevelLoaded postfix 入口：延迟协程 + 订阅重绑。</summary>
    public static void Schedule(World world)
    {
        try
        {
            if (world == null || world.gameObject == null) return;
            world.StartCoroutine(LoadPassRoutine(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[WallSpots] schedule failed: " + e);
        }
    }

    private static IEnumerator LoadPassRoutine(World world)
    {
        yield return new WaitForSeconds(LoadDelaySeconds);
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            if (kingdom != null && kingdom.gameObject != null)
                BindKingdom(kingdom);
            EnsureWallSpots(world, true);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[WallSpots] " + e);
        }
    }

    /// <summary>OnBordersChanged：延迟重查（合并 + 节流由 Ensure 节流统一）。</summary>
    private static void OnBordersChangedFired()
    {
        try
        {
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            if (world == null || world.gameObject == null
                || !world.gameObject.activeInHierarchy) return;
            float now = Time.unscaledTime;
            if (now - _lastRecheckSchedule < BorderRecheckDelaySeconds) return;
            _lastRecheckSchedule = now;
            world.StartCoroutine(BorderRecheckRoutine(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[WallSpots] border event handler failed: " + e.Message);
        }
    }

    private static IEnumerator BorderRecheckRoutine(World world)
    {
        yield return new WaitForSeconds(BorderRecheckDelaySeconds);
        try
        {
            EnsureWallSpots(world, false);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[WallSpots] " + e);
        }
    }

    /// <summary>
    /// 就绪门（塔基 TryGetReadyContext 同纪律）：捕获的 world/layer 必须仍是
    /// 当前活动实例；mod 关闭/联机/无权威整体拦截。
    /// </summary>
    private static bool TryGetReadyContext(World capturedWorld, out Transform layer)
    {
        layer = null;
        try
        {
            if (!ModConfig.Enabled.Value || NetworkBigBoss.IsOnline
                || !NetworkBigBoss.HasWorldAuth)
                return false;
            Managers managers = Managers.Inst;
            if (capturedWorld == null || capturedWorld.gameObject == null
                || !capturedWorld.gameObject.activeInHierarchy
                || managers == null || managers.game == null || managers.world == null
                || managers.kingdom == null || managers.holder == null
                || managers.game.state != Game.State.Playing
                || managers.world.Pointer != capturedWorld.Pointer)
                return false;
            layer = managers.world.gameLayer;
            if (layer == null || layer.gameObject == null
                || !layer.gameObject.activeInHierarchy
                || NetworkPostbox.Instance == null)
                return false;
            return true;
        }
        catch
        {
            layer = null;
            return false;
        }
    }

    /// <summary>幂等主入口：节流（level-load 豁免）+ 单轮 in-flight 门。</summary>
    private static void EnsureWallSpots(World world, bool force)
    {
        if (_running) return; // 主线程同步轮次进行中：下一触发自然重查
        if (!force && Time.unscaledTime - _lastPassTime < MinPassIntervalSeconds) return;
        if (!TryGetReadyContext(world, out Transform layer)) return;
        _running = true;
        try
        {
            RunPass(world, layer);
        }
        finally
        {
            _running = false;
            _lastPassTime = Time.unscaledTime;
        }
    }

    private static void RunPass(World world, Transform layer)
    {
        Managers managers = Managers.Inst;
        Holder holder = managers != null ? managers.holder : null;
        if (holder == null || holder.wallLocationPrefab == null)
        {
            if (!_loggedNoTemplate)
            {
                _loggedNoTemplate = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[WallSpots] holder/wallLocationPrefab not ready; skipping");
            }
            return;
        }
        GameObject prefab = null;
        try { prefab = BiomeData.GetAssetSwap<GameObject>(holder.wallLocationPrefab); }
        catch { prefab = null; }
        if (prefab == null) prefab = holder.wallLocationPrefab;
        if (prefab == null) return;

        if (NetworkBigBoss.IsOnline)
        {
            if (!_loggedOnlineSkip)
            {
                _loggedOnlineSkip = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[WallSpots] online session detected; wall spot expansion " +
                    "is single-player/splitscreen only, skipping");
            }
            return;
        }

        // ---- 快照：参考集（原生档）/ KEM 点 / 占用集（全量墙系+塔系）----
        var natives = new List<GameObject>();
        var kemBases = new List<GameObject>();
        var roots = new List<GameObject>();
        var allX = new List<float>();
        var seen = new HashSet<IntPtr>();
        CollectFoundations(layer, natives, kemBases, roots, allX, seen);
        AddTaggedToOccupancy("Wall", layer, roots, allX, seen);
        AddTaggedToOccupancy("WallWreck", layer, roots, allX, seen);
        AddTaggedToOccupancy("ScaffoldingWall", layer, roots, allX, seen, scaffolding: true);
        AddTaggedToOccupancy("Tower", layer, roots, allX, seen);
        AddTaggedToOccupancy("ScaffoldingTower", layer, roots, allX, seen);

        float multiplier = ModConfig.WallSpotMultiplier != null
            ? ModConfig.WallSpotMultiplier.Value : 1f;

        Kingdom kingdom = managers.kingdom;
        float campfire = kingdom.campfirePosition;
        if (!float.IsFinite(campfire)) return; // 未点火/特殊世界：不动（审查 P2-2）

        // ---- 回收：网格成员资格 + 真实占用重叠（未购 KEM 墙基）----
        var expected = new List<float>();
        if (multiplier > 1f) ComputeExpectedGrid(natives, campfire, multiplier, expected);
        int retired = RetireStaleKemBases(world, layer, kemBases, roots, allX, expected);
        if (multiplier <= 1f)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[WallSpots] multiplier<=1 retired=" + retired
                + " (native=" + natives.Count + ")");
            return;
        }

        // ---- per-gap 细分补放 ----
        int added = 0, clampedGaps = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            var side = new List<GameObject>();
            for (int i = 0; i < natives.Count; i++)
            {
                GameObject go = natives[i];
                if (go == null || go.transform == null) continue;
                bool left = go.transform.position.x < campfire;
                if ((pass == 0) == left) side.Add(go);
            }
            side.Sort(CompareByX);
            if (side.Count < 2) continue;

            for (int i = 1; i < side.Count; i++)
            {
                GameObject inner = side[i - 1], outer = side[i];
                if (inner == null || outer == null) continue;
                float xa = inner.transform.position.x, xb = outer.transform.position.x;
                float gap = xb - xa;
                if (gap <= GapEndMargin) continue;

                GameObject template = NearestGo(side, (xa + xb) * 0.5f);
                float target = gap / multiplier;
                if (template != null && TryGetVisualBounds(template, xa, out float lo, out float hi)
                    && hi > lo)
                {
                    target = Mathf.Max(target, (hi - lo) + VisualSpacingPadding);
                }
                if (target < MinTargetSpacing) target = MinTargetSpacing;
                if (target * 2f > gap) { clampedGaps++; continue; } // 放不下：整间隙跳过

                float occupied = target * OccupiedRatio;
                float y = inner.transform.position.y;
                float z = inner.transform.position.z;
                int sideAdded = 0;
                for (float x = xa + target; x < xb - GapEndMargin; x += target)
                {
                    if (sideAdded + added >= MaxPerSide) break;
                    if (!IsFree(allX, x, occupied)) continue;
                    if (!TryPlaceX(x, y)) continue;
                    if (OverlapsNativePlacement(prefab, layer, x, null, out _)) continue;
                    if (OverlapsOccupiedRoots(roots, template, x, null, layer)
                        == OverlapResult.Overlap) continue;
                    if (SpawnSpot(world, prefab, layer, template, x, y, z))
                    {
                        added++;
                        sideAdded++;
                        allX.Add(x); // 新点即时进入距离占用（footprint 复核下轮全量）
                    }
                }
            }
        }

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[WallSpots] pass done: native=" + natives.Count
            + " kemUnbuilt=" + (kemBases.Count - retired)
            + " added=" + added
            + " clampedGaps=" + clampedGaps
            + " multiplier=" + multiplier.ToString("F2")
            + " retired=" + retired);
    }

    /// <summary>
    /// WallFoundation tag 扫描：本层、非船；名字无 KEM 标记=原生档（参考集），
    /// 有标记=KEM 未购点。两类同时进占用集。
    /// </summary>
    private static void CollectFoundations(Transform layer,
        List<GameObject> natives, List<GameObject> kemBases,
        List<GameObject> roots, List<float> allX, HashSet<IntPtr> seen)
    {
        var tagged = GameObject.FindGameObjectsWithTag("WallFoundation");
        if (tagged == null) return;
        for (int i = 0; i < tagged.Length; i++)
        {
            GameObject go = tagged[i];
            if (go == null || go.transform == null || !go.transform.IsChildOf(layer)) continue;
            if (IsOnBoat(go.transform)) continue;
            if (!seen.Add(go.Pointer)) continue;
            roots.Add(go);
            allX.Add(go.transform.position.x);
            string n = go.name;
            if (n != null && n.StartsWith(MarkerPrefix, StringComparison.Ordinal)) kemBases.Add(go);
            else natives.Add(go);
        }
    }

    private static void AddTaggedToOccupancy(string tag, Transform layer,
        List<GameObject> roots, List<float> allX, HashSet<IntPtr> seen, bool scaffolding = false)
    {
        var tagged = GameObject.FindGameObjectsWithTag(tag);
        if (tagged == null) return;
        for (int i = 0; i < tagged.Length; i++)
        {
            GameObject go = tagged[i];
            if (go == null || go.transform == null || !go.transform.IsChildOf(layer)) continue;
            if (IsOnBoat(go.transform)) continue;
            if (!seen.Add(go.Pointer)) continue;
            roots.Add(go);
            allX.Add(go.transform.position.x);

            // 墙施工脚手架明确指向的（可能 inactive 的）建筑才计入占用。
            if (!scaffolding) continue;
            try
            {
                Scaffolding scaffold = go.GetComponent<Scaffolding>();
                GameObject building = scaffold != null ? scaffold.Building : null;
                if (building == null || building.transform == null) continue;
                if (!building.transform.IsChildOf(layer) || IsOnBoat(building.transform)) continue;
                if (!seen.Add(building.Pointer)) continue;
                roots.Add(building);
                allX.Add(building.transform.position.x);
            }
            catch { }
        }
    }

    /// <summary>
    /// 计算当前倍数下的期望网格点（回收成员资格判据）。与补放同一公式，
    /// 端点只取原生档 → 与补放结果一致；容差由调用方按目标间距比例取。
    /// </summary>
    private static void ComputeExpectedGrid(List<GameObject> natives, float campfire,
        float multiplier, List<float> expected)
    {
        var sides = new List<List<GameObject>> { new List<GameObject>(), new List<GameObject>() };
        for (int i = 0; i < natives.Count; i++)
        {
            GameObject go = natives[i];
            if (go == null || go.transform == null) continue;
            sides[go.transform.position.x < campfire ? 0 : 1].Add(go);
        }
        for (int s = 0; s < 2; s++)
        {
            var side = sides[s];
            side.Sort(CompareByX);
            for (int i = 1; i < side.Count; i++)
            {
                GameObject inner = side[i - 1], outer = side[i];
                if (inner == null || outer == null) continue;
                float xa = inner.transform.position.x, xb = outer.transform.position.x;
                float gap = xb - xa;
                if (gap <= GapEndMargin) continue;
                GameObject template = NearestGo(side, (xa + xb) * 0.5f);
                float target = gap / multiplier;
                if (template != null && TryGetVisualBounds(template, xa, out float lo, out float hi)
                    && hi > lo)
                {
                    target = Mathf.Max(target, (hi - lo) + VisualSpacingPadding);
                }
                if (target < MinTargetSpacing) target = MinTargetSpacing;
                if (target * 2f > gap) continue;
                for (float x = xa + target; x < xb - GapEndMargin; x += target)
                    expected.Add(x);
            }
        }
    }

    /// <summary>
    /// 回收不再位于期望网格（tolerance=MinTargetSpacing×OccupiedRatio）或与
    /// 真实占用重叠的未购 KEM 墙基。删除动作前全面重验（塔基同纪律：付款中/
    /// 施工中/玩家选中一律 fail-closed 保留）。
    /// </summary>
    private static int RetireStaleKemBases(World world, Transform layer,
        List<GameObject> kemBases, List<GameObject> roots, List<float> allX,
        List<float> expected)
    {
        int retired = 0;
        float tolerance = MinTargetSpacing * OccupiedRatio;
        foreach (GameObject spot in kemBases)
        {
            if (!TryGetReadyContext(world, out _)) break;
            if (!CanRetire(spot, world, layer)) continue;
            float x = spot.transform.position.x;

            bool onGrid = false;
            for (int i = 0; i < expected.Count; i++)
            {
                if (Mathf.Abs(expected[i] - x) <= tolerance) { onGrid = true; break; }
            }
            bool overlapsOccupancy = OverlapsOccupiedRoots(
                roots, spot, x, spot, layer) == OverlapResult.Overlap;
            if (onGrid && !overlapsOccupancy) continue;

            if (!CanRetire(spot, world, layer)) continue;
            try
            {
                Persistent persistent = spot.GetComponent<Persistent>();
                CRPCHeader header = NetworkPostbox.Instance.GetHeaderFromObject(spot, true);
                NetworkPostbox.Instance.DeregisterObject(header);
                persistent.DontPersistInstance(true);
                spot.SetActive(false);
                if (spot.activeInHierarchy) continue;
                allX.Remove(x);
                retired++;
                UnityEngine.Object.Destroy(spot);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[WallSpots] retired x=" + x.ToString("F1")
                    + " cause=" + (overlapsOccupancy ? "overlap" : "off-grid"));
            }
            catch (Exception e)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[WallSpots] retirement failed: " + e);
                break; // 部分原生失败后停止本轮删除（塔基同纪律）
            }
        }
        return retired;
    }

    /// <summary>
    /// KEM 未购墙基可删性全验：标记名、活跃、本层同场景、非船、tag
    /// WallFoundation、无 Wall 组件（未购）、Persistent、SemiStatic 头、
    /// PayableUpgrade 在场、无施工组件、Payable 未被选中/交互、玩家未付款中。
    /// </summary>
    private static bool CanRetire(GameObject spot, World world, Transform layer)
    {
        try
        {
            if (!TryGetReadyContext(world, out _)) return false;
            if (spot == null || spot.transform == null || !spot.activeInHierarchy) return false;
            string n = spot.name;
            if (n == null || !n.StartsWith(MarkerPrefix, StringComparison.Ordinal)) return false;
            if (!spot.transform.IsChildOf(layer)) return false;
            if (IsOnBoat(spot.transform)) return false;
            if (spot.tag != "WallFoundation") return false;
            if (spot.GetComponent<Wall>() != null) return false; // 已购/变身：不属回收
            if (spot.GetComponent<Persistent>() == null) return false;
            if (spot.GetComponent<PayableUpgrade>() == null) return false;
            CRPCHeader header = NetworkPostbox.Instance != null
                ? NetworkPostbox.Instance.GetHeaderFromObject(spot, true) : null;
            if (header == null || header.HeaderType != CRPCType.SemiStatic) return false;
            if (spot.GetComponent<ConstructionBuildingComponent>() != null
                || spot.GetComponent<WorkableBuilding>() != null
                || spot.GetComponent<Scaffolding>() != null) return false;
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

    /// <summary>玩家 selectedPayable/_completingPayable 与 spot 同层级关联判定。</summary>
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
                if (selected != null && IsSameHierarchy(selected.gameObject, spot)) return true;
                Payable completing = player._completingPayable;
                if (completing != null && IsSameHierarchy(completing.gameObject, spot)) return true;
            }
        }
        catch { return true; } // 不可判定=可能正被付款，fail-closed
        return false;
    }

    /// <summary>
    /// 实例化一个墙基：塔基 SpawnSpot 同配方（Instantiate 资产换皮预制体 →
    /// 挂 gameLayer → RegisterObject(SemiStatic)，存档取证实证动态原生档同语义）。
    /// 朝向/缩放抄最近原生档；命名带 KEM 标记（存档去重/参考集排除）。
    /// </summary>
    private static bool SpawnSpot(World world, GameObject prefab, Transform layer,
        GameObject template, float x, float y, float z)
    {
        if (!TryGetReadyContext(world, out _)) return false;
        GameObject spot = UnityEngine.Object.Instantiate(
            prefab, new Vector3(x, y, z), Quaternion.identity, layer);
        if (spot == null) return false;
        try
        {
            if (!spot.activeSelf) spot.SetActive(true);
            if (template != null && template.transform != null)
            {
                spot.transform.rotation = template.transform.rotation;
                spot.transform.localScale = template.transform.localScale;
            }
            FixedTransform fixedTransform = spot.GetComponent<FixedTransform>();
            if (fixedTransform != null) fixedTransform.Fix();
            spot.name = MarkerPrefix + "_" + x.ToString("F1");
            if (!TryGetReadyContext(world, out _))
                throw new InvalidOperationException("wall spot registration context lost");
            NetworkPostbox.Instance.RegisterObject(spot, CRPCType.SemiStatic);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[WallSpots] spot setup failed at x=" + x.ToString("F1") + ": " + e);
            try { UnityEngine.Object.Destroy(spot); } catch { }
            return false;
        }
    }

    private enum OverlapResult { Clear, Overlap, Unknown }

    /// <summary>占用快照实际 footprint 复核（塔基同款；不可判定=不放）。</summary>
    private static OverlapResult OverlapsOccupiedRoots(List<GameObject> roots,
        GameObject candidate, float x, GameObject ignoreRoot, Transform layer)
    {
        try
        {
            if (!TryGetCombinedOverlapRegion(candidate, x, out Rect candidateRect))
                return OverlapResult.Unknown;
            bool unknown = false;
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null || IsSameHierarchy(root, ignoreRoot)) continue;
                try
                {
                    if (root.transform == null || !root.transform.IsChildOf(layer)) continue;
                    if (!root.gameObject.activeInHierarchy) continue;
                    if (!TryGetCombinedOverlapRegion(root, root.transform.position.x, out Rect occupied))
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
    /// 复刻原生 ScatteredObject.AvoidOverlapWith 横向矩形判定（塔基同款）。
    /// WallFoundation/Wall 标签有意跳过：同间隙端点就是原生墙基，真实邻近
    /// 已由占用距离+footprint 复核管理，套原生同类排斥会把细分全禁。
    /// </summary>
    private static bool OverlapsNativePlacement(GameObject prefab, Transform layer,
        float x, GameObject ignoreRoot, out string blockedTag)
    {
        blockedTag = null;
        try
        {
            ScatteredObject scatter = prefab != null
                ? prefab.GetComponent<ScatteredObject>() : null;
            if (scatter == null || scatter.AvoidOverlapWith == null) return false;
            if (!TryGetCombinedOverlapRegion(prefab, x, out Rect candidate))
            { blockedTag = "check-error"; return true; }
            var avoidTags = scatter.AvoidOverlapWith;
            for (int tagIndex = 0; tagIndex < avoidTags.Count; tagIndex++)
            {
                string tag = avoidTags[tagIndex];
                if (string.IsNullOrEmpty(tag) || tag == "WallFoundation" || tag == "Wall") continue;

                var objects = GameObject.FindGameObjectsWithTag(tag);
                for (int i = 0; i < objects.Length; i++)
                {
                    GameObject other = objects[i];
                    if (other == null || other.transform == null
                        || IsSameHierarchy(other, ignoreRoot)
                        || IsOnBoat(other.transform)) continue;
                    Managers managers = Managers.Inst;
                    Level level = managers != null ? managers.level : null;
                    if (level != null && level.transform != null
                        && !other.transform.IsChildOf(level.transform)) continue;
                    if (layer != null && other.scene.handle
                        != layer.gameObject.scene.handle) continue;
                    if (!TryGetCombinedOverlapRegion(other, other.transform.position.x, out Rect occupied))
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
                "[WallSpots] overlap check failed closed at x=" + x.ToString("F1")
                + ": " + e.Message);
            blockedTag = "check-error";
            return true;
        }
        return false;
    }

    private static Rect GetOverlapRegion(GameObject go, float x)
    {
        ScatteredObject scatter = go != null ? go.GetComponent<ScatteredObject>() : null;
        if (scatter != null && !scatter.UsePayableForSpacing)
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

    private static bool TryGetCombinedOverlapRegion(GameObject go, float x, out Rect result)
    {
        result = default;
        try
        {
            if (!TryGetVisualBounds(go, x, out float visualMin, out float visualMax)) return false;
            Rect native = GetOverlapRegion(go, x);
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
                if (hi <= lo) continue;
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

    /// <summary>地形合法性：镜像原生 Physics2D NotBuildable 点检（塔基同款）。</summary>
    private static bool TryPlaceX(float x, float y)
    {
        try
        {
            int notBuildableMask = LayerMask.GetMask("NotBuildable");
            Collider2D hit = Physics2D.OverlapPoint(
                new Vector2(x, y + 0.5f), notBuildableMask);
            return hit == null;
        }
        catch
        {
            return false; // 检查不可用=不放（fail-closed）
        }
    }

    /// <summary>订阅绑定（CoinCourier 纪律）：重绑前对旧实例 remove。</summary>
    private static void BindKingdom(Kingdom kingdom)
    {
        try
        {
            if (kingdom == null || kingdom.gameObject == null) return;
            if (_subscribedKingdom != null
                && _subscribedKingdom.Pointer == kingdom.Pointer) return;
            if (_subscribedKingdom != null && _borderHandler != null)
            {
                try { _subscribedKingdom.remove_OnBordersChanged(_borderHandler); }
                catch { /* 旧实例可能已随场景销毁；下一条订阅即恢复 */ }
            }
            _borderHandler = (Il2CppSystem.Action)OnBordersChangedFired;
            kingdom.add_OnBordersChanged(_borderHandler);
            _subscribedKingdom = kingdom;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[WallSpots] OnBordersChanged subscribe failed: " + e.Message);
        }
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
        catch { return false; }
    }

    private static bool IsFree(List<float> allX, float x, float occupied)
    {
        for (int i = 0; i < allX.Count; i++)
        {
            if (Mathf.Abs(allX[i] - x) <= occupied) return false;
        }
        return true;
    }

    private static GameObject NearestGo(List<GameObject> gos, float targetX)
    {
        GameObject best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < gos.Count; i++)
        {
            GameObject go = gos[i];
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

    private static int CompareByX(GameObject a, GameObject b)
    {
        return XOf(a).CompareTo(XOf(b));
    }

    private static float XOf(GameObject go)
    {
        return go != null && go.transform != null ? go.transform.position.x : 0f;
    }

    /// <summary>祖先名含 "Boat" 判定（塔基同款）。</summary>
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
        catch { }
        return false;
    }
}

/// <summary>World.OnLevelLoaded postfix：调度延迟补放 + 订阅重绑。</summary>
[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_WallSpots_Expand_Host_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        PatchWorld_WallSpots.Schedule(__instance);
    }
}
