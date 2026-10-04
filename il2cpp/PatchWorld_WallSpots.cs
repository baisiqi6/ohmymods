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
/// 原生事实（存档取证 evidence-save-parse-20261004 + 资产扫描，两轮审查已核）：
/// - 墙基是运行时动态生成的持久化对象：存档条目 name=Wall0(Clone)、
///   netID 已分配、crpcType=2（SemiStatic）、prefabPath=
///   "Prefabs/Buildings and Interactive/Wall0"。起始岛仅 4 个静态摆设，外档
///   全部动态补出；Holder.wallLocationPrefab 与 towerLocationPrefab 对称。
/// - 未建墙基没有 Wall 组件：形态是 PayableUpgrade.nextPrefab 直接指向带 Wall
///   的预制体（MainBankerFixedDomain 已证）。购买后原生 Pay 原位实例化墙体
///   （Wall1 木/Wall2 石独立持久化）并销毁基底——墙体只出现在原基底位置。
/// - 真实档距极度非均匀（6.7~142，随岛建造带变化），不存在均匀档位梯。
///
/// 本补丁语义（两轮审查修正后定稿）：
/// - **per-gap 细分**：只对每侧墙线（原生档 ∪ 已购墙/残骸虚拟端点）相邻端点
///   间隙内部补点，目标间距=max(gap/m, 视觉宽+padding, 3.0 地板)；放不下
///   的间隙整段跳过并计数钳制。绝不越过该侧最内原生档（银行家固定域
///   MainBankerFixedDomain 以最内墙基划域，审查 P0-3），也绝不超出最外原生档
///   （扩张带不预放）。购买间隙端点墙基不合并间隙、不回收相邻 KEM 点
///   （复审#2 P1-1 虚拟端点）。
/// - **双触发**：World.OnLevelLoaded postfix 延迟 5s（加载时已存在的原生档）
///   + Kingdom.OnBordersChanged 订阅（per-Kingdom 重绑，CoinCourier 纪律）延迟
///   2s 重查；每次成功 pass 兼作订阅恢复点。漏一次触发只延迟到下一触发/读档
///   （每档基都是普通持久化对象，无正确性风险）；≥5s 节流 + 单次 in-flight 门
///   防事件风暴。
/// - **注册**：无条件 RegisterObject(SemiStatic)（动态原生档实证同语义）。
/// - **幂等**：KEM_WallSpot 名字标记；参考集（间隙端点）永不混入 KEM 点；
///   占用集全量（WallFoundation/Wall/WallWreck/ScaffoldingWall/Tower 系）。
///   反复读档密度不爬升；KEM 名字+netID 跨存档保留（KEM_TowerSpot 同管线
///   本机存档 19 条再证）。补放与回收共用同一候选枚举（网格恒一致）。
/// - **回收**：滑块降档/关闭后，不再位于当前网格（或与真实占用重叠）的未购
///   KEM 墙基在下一轮触发时回收（网格成员资格回收）；已购墙保留。
/// - **联机 fail-closed**：NetworkBigBoss.IsOnline 整体跳过（塔基同纪律）。
/// </summary>
public static class PatchWorld_WallSpots
{
    internal const string MarkerPrefix = "KEM_WallSpot";
    private const float LoadDelaySeconds = 5f;        // 等场景/PayableManager/holder 就绪
    private const float BorderRecheckDelaySeconds = 2f; // 等新档基随边界扩张稳定出现
    private const float MinPassIntervalSeconds = 5f;  // 事件风暴节流（level-load 豁免）
    private const float MinTargetSpacing = 3f;        // 塔基同款交互/视觉地板
    private const float VisualSpacingPadding = 0.25f;
    private const float OccupiedRatio = 0.6f;
    private const float GapEndMargin = 0.5f;          // 网格点距间隙端点的最小余量
    private const int MaxTotalPerPass = 60;           // 单轮补放总数硬上限（防御性）
    private const float OffsetWindowCap = 8f;         // 偏移搜索窗口上限（复审#4 P2-3）

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

        // ---- 快照：参考集（原生档）/ KEM 点 / 占用集（全量墙线+塔线）----
        var natives = new List<GameObject>();
        var kemBases = new List<GameObject>();
        var roots = new List<GameObject>();
        var wallLine = new List<GameObject>(); // 已购墙/残骸：间隙虚拟端点（复审#2 P1-1）
        var allX = new List<float>();
        var seen = new HashSet<IntPtr>();
        CollectFoundations(layer, natives, kemBases, roots, allX, seen);
        AddTaggedToOccupancy("Wall", layer, roots, allX, seen, wallLine: wallLine);
        AddTaggedToOccupancy("WallWreck", layer, roots, allX, seen, wallLine: wallLine);
        AddTaggedToOccupancy("ScaffoldingWall", layer, roots, allX, seen, scaffolding: true);
        AddTaggedToOccupancy("Tower", layer, roots, allX, seen, distanceOccupancy: false);
        AddTaggedToOccupancy("ScaffoldingTower", layer, roots, allX, seen, distanceOccupancy: false);

        // 每次成功 pass 同时是订阅恢复点（复审#2 P2-4：+5s 时 kingdom 尚
        // null 导致订阅丢失的边路在此自愈）。
        BindKingdom(managers.kingdom);

        float multiplier = ModConfig.WallSpotMultiplier != null
            ? ModConfig.WallSpotMultiplier.Value : 1f;

        Kingdom kingdom = managers.kingdom;
        float campfire = kingdom.campfirePosition;
        if (!float.IsFinite(campfire)) return; // 未点火/特殊世界：不动（审查 P2-2）

        // ---- 回收 + 补放共用同一候选源（网格恒一致；守卫只过滤）----
        int clampedGaps = 0;
        List<GapCandidate> candidates = multiplier > 1f
            ? EnumerateGapCandidates(natives, wallLine, campfire, multiplier, out clampedGaps)
            : null;
        var expected = candidates ?? new List<GapCandidate>();
        int retired = RetireStaleKemBases(world, layer, kemBases, roots, allX, expected);
        if (multiplier <= 1f)
        {
            if (retired > 0)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[WallSpots] multiplier<=1 retired=" + retired
                    + " (native=" + natives.Count + ")");
            }
            return;
        }

        // ---- per-gap 细分补放 ----
        int notBuildableMask = LayerMask.GetMask("NotBuildable"); // 每 pass 缓存一次（复审#2 P2-2）
        int added = 0, directPlaced = 0, offsetPlaced = 0, searchFailed = 0;
        foreach (GapCandidate candidate in candidates)
        {
            if (added >= MaxTotalPerPass) break;
            // 有界偏移搜索（实机#2：大间隙在 2x 下候选稀疏，单点恰落塔基脚印
            // 会让整段空置）。先试精确网格点，被任意守卫拦住则在窗口内按 1
            // 单位步进找最近可用位；窗口= min(0.5×target, 8)（复审#4 P2-3 上限，
            // 数学上 0.5t<端点 IsFree 半径 0.6t，任何偏移点不可能越过间隙端点）。
            float placedX = float.NaN;
            float halfWindow = Mathf.Min(candidate.Target * 0.5f, OffsetWindowCap);
            for (float offset = 0f; offset <= halfWindow && float.IsNaN(placedX); offset += 1f)
            {
                float[] signs = offset == 0f ? new float[] { 1f } : new float[] { 1f, -1f };
                for (int si = 0; si < signs.Length; si++)
                {
                    float x = candidate.X + signs[si] * offset;
                    if (!IsFree(allX, x, candidate.Target * OccupiedRatio)) continue;
                    if (!TryPlaceX(x, candidate.Y, notBuildableMask)) continue;
                    // footprint 源兜底：无原生模板时用 prefab（有 renderer/payable，
                    // 避免整线已购边路恒 Unknown；复审#4 P1-2）。
                    GameObject footprintSource = candidate.Template != null
                        ? candidate.Template : prefab;
                    if (OverlapsNativePlacement(prefab, layer, x, null, out _,
                        footprintSource)) continue;
                    // 复核 fail-closed：Unknown 不放（与塔基判据/注释对齐，P1-2）。
                    if (OverlapsOccupiedRoots(roots, footprintSource, x, null, layer)
                        != OverlapResult.Clear) continue;
                    placedX = x;
                    break;
                }
            }
            if (float.IsNaN(placedX))
            {
                searchFailed++;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[WallSpots] candidate blocked gap=[" + candidate.A.x.ToString("F1")
                    + "," + candidate.B.x.ToString("F1") + "] grid=" + candidate.X.ToString("F1")
                    + " window=" + halfWindow.ToString("F1"));
                continue;
            }
            // 偏移显著时按最终落位重取较近端点的 y/z（复审#4 P2-2）。
            bool placedNearInner = Mathf.Abs(placedX - candidate.A.x)
                <= Mathf.Abs(candidate.B.x - placedX);
            float placedY = placedNearInner ? candidate.A.y : candidate.B.y;
            float placedZ = placedNearInner ? candidate.A.z : candidate.B.z;
            GameObject spawned = SpawnSpot(world, prefab, layer, candidate.Template,
                placedX, placedY, placedZ, candidate.Target);
            if (spawned == null) continue;
            added++;
            if (Mathf.Abs(placedX - candidate.X) < 0.01f) directPlaced++; else offsetPlaced++;
            // 新点即时进入全量占用（含 footprint 数据源，复审#2 P2-3：同轮
            // 后续点对它做实际 footprint 复核）。
            roots.Add(spawned);
            allX.Add(spawned.transform.position.x);
        }

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[WallSpots] pass done: native=" + natives.Count
            + " wallLine=" + wallLine.Count
            + " kemUnbuilt=" + (kemBases.Count - retired)
            + " added=" + added + " (direct=" + directPlaced + " offset=" + offsetPlaced + ")"
            + " searchFailed=" + searchFailed
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
        List<GameObject> roots, List<float> allX, HashSet<IntPtr> seen,
        bool scaffolding = false, List<GameObject> wallLine = null,
        bool distanceOccupancy = true)
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
            // 距离占用只收墙线族（基底/墙/残骸/墙脚手架）；塔族进 roots 供
            // footprint/原生避障复核即可——否则塔基以 ~8 单位铺满防线时，
            // 墙基候选点在任意大间隙内都会被距离守卫全灭（实机 added=0 实证）。
            if (distanceOccupancy) allX.Add(go.transform.position.x);
            if (wallLine != null) wallLine.Add(go);

            // 墙施工脚手架明确指向的（可能 inactive 的）建筑才计入占用。
            // 注：Scaffolding 组件按打标根上 GetComponent 取（若实证组件在
            // 子物体上会漏挂 Building 占用；后果有界——施工完成后下一轮
            // pass 会因 overlap 回收，复审#2 P2-8 备查）。
            if (!scaffolding) continue;
            try
            {
                Scaffolding scaffold = go.GetComponent<Scaffolding>();
                GameObject building = scaffold != null ? scaffold.Building : null;
                if (building == null || building.transform == null) continue;
                if (!building.transform.IsChildOf(layer) || IsOnBoat(building.transform)) continue;
                if (!seen.Add(building.Pointer)) continue;
                roots.Add(building);
                if (distanceOccupancy) allX.Add(building.transform.position.x);
            }
            catch { }
        }
    }

    private sealed class GapCandidate
    {
        public float X;
        public float Y;
        public float Z;
        public float Target;
        public GameObject Template;
        public Vector3 A; // 间隙内端点（y/z 随偏移落位重取较近端，复审#4 P2-2）
        public Vector3 B; // 间隙外端点
    }

    /// <summary>
    /// 间隙候选单源枚举（复审#2 P1-1/P1-2）。间隙端点 = 原生档 ∪ 已购墙/残骸
    /// 虚拟端点——购买间隙端点的原生墙基后，该位置由 tag "Wall" 的墙体占据，
    /// 间隙不合并、相邻 KEM 点保持 on-grid 不被误回收（墙体只会出现在原基
    /// 底位置，虚拟端点语义严格成立）。视觉宽模板只取原生基底（墙体宽度
    /// 不是基底宽度）；候选点 y/z 取较近端点值（塔基 GroundYForX 两端点
    /// 特例，修斜坡段悬空/入土）。补放与回收共用本函数 → 网格恒一致。
    /// </summary>
    private static List<GapCandidate> EnumerateGapCandidates(List<GameObject> natives,
        List<GameObject> wallLine, float campfire, float multiplier, out int clampedGaps)
    {
        clampedGaps = 0;
        var result = new List<GapCandidate>();
        for (int pass = 0; pass < 2; pass++)
        {
            var side = new List<GameObject>(natives.Count + wallLine.Count);
            for (int i = 0; i < natives.Count; i++)
                AddSide(side, natives[i], campfire, pass == 0);
            for (int i = 0; i < wallLine.Count; i++)
                AddSide(side, wallLine[i], campfire, pass == 0);
            side.Sort(CompareByX);
            if (side.Count < 2) continue;

            for (int i = 1; i < side.Count; i++)
            {
                GameObject inner = side[i - 1], outer = side[i];
                if (inner == null || outer == null) continue;
                Vector3 a = inner.transform.position, b = outer.transform.position;
                float gap = b.x - a.x;
                if (gap <= GapEndMargin) continue;

                GameObject template = NearestGo(natives, (a.x + b.x) * 0.5f);
                float target = gap / multiplier;
                if (template != null && TryGetVisualBounds(template, a.x, out float lo, out float hi)
                    && hi > lo)
                {
                    target = Mathf.Max(target, (hi - lo) + VisualSpacingPadding);
                }
                if (target < MinTargetSpacing) target = MinTargetSpacing;
                if (target * 2f > gap) { clampedGaps++; continue; } // 放不下：整间隙跳过

                for (float x = a.x + target; x < b.x - GapEndMargin; x += target)
                {
                    bool nearInner = Mathf.Abs(x - a.x) <= Mathf.Abs(b.x - x);
                    result.Add(new GapCandidate
                    {
                        X = x,
                        Y = nearInner ? a.y : b.y,
                        Z = nearInner ? a.z : b.z,
                        Target = target,
                        Template = template,
                        A = a,
                        B = b,
                    });
                }
            }
        }
        return result;
    }

    private static void AddSide(List<GameObject> side, GameObject go, float campfire, bool left)
    {
        if (go == null || go.transform == null) return;
        if ((go.transform.position.x < campfire) == left) side.Add(go);
    }

    /// <summary>
    /// 回收不再位于期望网格（tolerance=MinTargetSpacing×OccupiedRatio）或与
    /// 真实占用重叠的未购 KEM 墙基。删除动作前全面重验（塔基同纪律：付款中/
    /// 施工中/玩家选中一律 fail-closed 保留）。
    /// </summary>
    private static int RetireStaleKemBases(World world, Transform layer,
        List<GameObject> kemBases, List<GameObject> roots, List<float> allX,
        List<GapCandidate> expected)
    {
        int retired = 0;
        foreach (GameObject spot in kemBases)
        {
            if (!TryGetReadyContext(world, out _)) break;
            if (!CanRetire(spot, world, layer)) continue;
            float x = spot.transform.position.x;

            // on-grid 判据（复审#4 P1-1）：位置在候选网格点的 0.6×target 邻域
            // 内，且名字编码的落位 target 与该候选当前 target 一致（档位切换后
            // 旧点不再豁免，降档真正降密度）；旧名解析失败退回仅位置判据。
            float? encodedTarget = ParseEncodedTarget(spot.name);
            bool onGrid = false;
            for (int i = 0; i < expected.Count; i++)
            {
                if (Mathf.Abs(expected[i].X - x)
                    > expected[i].Target * OccupiedRatio) continue;
                if (encodedTarget.HasValue
                    && Mathf.Abs(expected[i].Target - encodedTarget.Value) > 0.05f) continue;
                onGrid = true; break;
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
    /// 成功返回实例（调用方将其计入全量占用），失败返回 null。
    /// </summary>
    private static GameObject SpawnSpot(World world, GameObject prefab, Transform layer,
        GameObject template, float x, float y, float z, float target)
    {
        if (!TryGetReadyContext(world, out _)) return null;
        GameObject spot = UnityEngine.Object.Instantiate(
            prefab, new Vector3(x, y, z), Quaternion.identity, layer);
        if (spot == null) return null;
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
            // 名字编码落位时的目标间距（复审#4 P1-1）：回收时据此区分"本档
            // 合法偏移点"与"旧档位网格点"——档位切换后旧 t 与当前枚举不匹配
            // 即 off-grid 回收，降档真正降密度；旧名（无 _t 后缀）解析失败退
            // 回按当前容差判断（一次性遗留豁免，有界）。
            spot.name = MarkerPrefix + "_" + x.ToString("F1")
                + "_t" + target.ToString("F1");
            if (!TryGetReadyContext(world, out _))
                throw new InvalidOperationException("wall spot registration context lost");
            NetworkPostbox.Instance.RegisterObject(spot, CRPCType.SemiStatic);
            return spot;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[WallSpots] spot setup failed at x=" + x.ToString("F1") + ": " + e);
            try { UnityEngine.Object.Destroy(spot); } catch { }
            return null;
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
        float x, GameObject ignoreRoot, out string blockedTag, GameObject footprintSource = null)
    {
        blockedTag = null;
        try
        {
            ScatteredObject scatter = prefab != null
                ? prefab.GetComponent<ScatteredObject>() : null;
            if (scatter == null || scatter.AvoidOverlapWith == null) return false;
            // 候选范围取实际实例的模板（抄了其缩放），塔基同款（复审#2 P2-3）。
            if (!TryGetCombinedOverlapRegion(footprintSource ?? prefab, x, out Rect candidate))
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
            float halfCap = StructuralHalfCapFor(go);
            if (halfCap > 0f && go.transform != null)
            {
                float cx = go.transform.position.x;
                min = Mathf.Max(min, cx - halfCap);
                max = Mathf.Min(max, cx + halfCap);
            }
            result = Rect.MinMaxRect(min, 50f, max, 150f);
            return float.IsFinite(min) && float.IsFinite(max) && max > min;
        }
        catch { return false; }
    }

    /// <summary>
    /// 实机#3 实证（occupancy-root-rects 日志）：Wall/WallWreck 根的子渲染器
    /// 并集会把背景墙段（从城墙延伸回主城的装饰长带，如 [0..159.4]）吞进
    /// 占用矩形，一次罩死整条内带（塔基误回收 13 个 + 墙基候选全灭）。
    /// 墙的结构占地仅 ~3 单位：这两类根的矩形钳制到 root.x ± WallStructuralHalf。
    /// </summary>
    private static float StructuralHalfCapFor(GameObject go)
    {
        try
        {
            if (go == null) return 0f;
            string t = go.tag;
            return t == "Wall" || t == "WallWreck" ? 2.5f : 0f;
        }
        catch { return 0f; }
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

    /// <summary>地形合法性：镜像原生 Physics2D NotBuildable 点检（塔基同款；mask 每 pass 缓存）。</summary>
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

    /// <summary>解析 KEM 名字后缀 _t&lt;target&gt;（无后缀/坏格式返回 null）。</summary>
    private static float? ParseEncodedTarget(string name)
    {
        try
        {
            if (name == null) return null;
            int idx = name.LastIndexOf("_t", StringComparison.Ordinal);
            if (idx < 0) return null;
            if (float.TryParse(name.Substring(idx + 2),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float t)
                && t >= MinTargetSpacing)
                return t;
        }
        catch { }
        return null;
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
