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
///   每次关卡加载重放。
/// - **对原生参考集幂等**（reviewer P0 修正）：间距估计（中位数）与铺点范围
///   （anchor/outermost/外推终点）只从"未购买的原生基底"参考集取——tag Tower
///   + 本层 + 非船 + 名字无 KEM 标记 + Tower.level==0。补放点（有 KEM 标记）
///   与已建塔（level>=1）绝不进参考集：否则首跑后中位数即缩为 S/m、
///   target 跟着缩为 S/m²，每读档密度×2 直到 3 步地板，outermost 也会随补放
///   点逐档外推。参考集在反复读档间不变（补放点重建后仍带 KEM 标记被排除），
///   大量购买后剩余原生样本 gap 恒为 S 或 2S，中位数稳定。
/// - 距离守卫的占用集保持全量（原生基底+补放点+已建塔）：防与任何现有结构
///   贴脸，也保证重放时旧补放点把同位网格点全部拦截（added=0）。
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
            ExpandTowerSpots(world);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[TowerSpots] " + e);
        }
    }

    /// <summary>补放入口（对原生参考集幂等，每次关卡加载重放）。</summary>
    private static void ExpandTowerSpots(World world)
    {
        // 已就绪检查通过并放点过的 world+gameLayer 不重跑；换世界/换岛/读档
        // （scene 重建，gameLayer 指针变化）会重新执行。
        Transform layer = world.gameLayer;
        if (layer == null) return;
        if (_expandedWorld == world.Pointer && _expandedLayer == layer.Pointer) return;

        float multiplier = ModConfig.TowerSpotMultiplier != null
            ? ModConfig.TowerSpotMultiplier.Value : 2f;
        if (multiplier <= 1f) return; // 1=原生密度，不补点

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

        // ---- 扫描 "Tower" 标签：占用集（全量）与参考集（原生基底） ----
        // 占用集：tag Tower + 本层 + 非船（距离守卫数据源，含补放点/已建塔）。
        // 参考集：再加 名字无 KEM 标记 + Tower.level==0（=未购买的原生基底；
        // 间距估计/铺点范围/朝向模板数据源，绝不混入补放点与已建塔）。
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> tagged =
            GameObject.FindGameObjectsWithTag("Tower");
        if (tagged == null || tagged.Length == 0) return;

        var allX = new List<float>();          // 占用集（全量 x）
        var refGos = new List<GameObject>();   // 参考集（原生基底对象）
        for (int i = 0; i < tagged.Length; i++)
        {
            GameObject go = tagged[i];
            if (go == null || go.transform == null || !go.transform.IsChildOf(layer)) continue;
            if (IsOnBoat(go.transform)) continue;
            allX.Add(go.transform.position.x);
            if (IsNativeBase(go)) refGos.Add(go);
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

        // 全部就绪检查通过：此刻才消费 per-world 守卫（之后的失败属于放点期
        // 异常，由各处 try/catch 兜底，不吞世界）。
        _expandedWorld = world.Pointer;
        _expandedLayer = layer.Pointer;

        int added = 0;
        if (leftRef.Count > 0)
        {
            added += ExpandSide(prefab, layer, leftRef, allX,
                -1f, worldLeft, worldRight, groundY, planeZ,
                leftNative ?? fallback.Value, multiplier, notBuildableMask);
        }
        if (rightRef.Count > 0)
        {
            added += ExpandSide(prefab, layer, rightRef, allX,
                1f, worldLeft, worldRight, groundY, planeZ,
                rightNative ?? fallback.Value, multiplier, notBuildableMask);
        }

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[TowerSpots] added " + added + " spots (multiplier=" + multiplier.ToString("F2")
            + ", native spacing=" + fallback.Value.ToString("F1")
            + ", native bases=" + refGos.Count
            + ", occupied total=" + allX.Count + ")");
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

    /// <summary>
    /// 单侧补点：把该侧原生基底参考集按"原生间距/倍数"的目标网格在内侧锚点
    /// 与外侧延长线（最外侧原生基底再外扩 1 个原生间距，钳到世界边界）之间
    /// 铺开。anchor/outermost/间距只来自参考集（幂等根基）；每个网格点过两道
    /// 放置守卫（与占用集全量 any 点距离 &gt; 0.6×目标间距；NotBuildable 点检
    /// 为空），通过才实例化。新点实时追加进占用集，保证同轮铺开的相邻新点也
    /// 满足间距（永不进参考集）。
    /// </summary>
    private static int ExpandSide(
        GameObject prefab, Transform layer, List<GameObject> sideRef,
        List<float> allX, float dir,
        float worldLeft, float worldRight, float groundY, float planeZ,
        float nativeSpacing, float multiplier, int notBuildableMask)
    {
        float target = nativeSpacing / multiplier;
        if (target < MinTargetSpacing) target = MinTargetSpacing;
        float occupied = target * OccupiedRatio;

        // 锚点=该侧最靠近营火的原生基底；终点=最外侧原生基底向外再延 1 个
        // 原生间距（方向=离开营火），钳到世界边界（留 2 单位余量）。
        // 两者都取自参考集：补放点再靠外也不推进终点（幂等根基）。
        float anchor = XOf(sideRef[dir < 0f ? sideRef.Count - 1 : 0]);            // 最内
        float outermost = XOf(sideRef[dir < 0f ? 0 : sideRef.Count - 1]);         // 最外
        float rawEnd = outermost + dir * nativeSpacing * OutwardExtension;
        float end = Mathf.Clamp(rawEnd,
            worldLeft == float.MinValue ? rawEnd : worldLeft + 2f,
            worldRight == float.MaxValue ? rawEnd : worldRight - 2f);

        // 朝向/缩放模板：最近的原生基底（左右两侧贴图镜像一致）
        GameObject template = NearestGo(sideRef, anchor);

        int added = 0;
        int steps = 0;
        for (float x = anchor + dir * target; ; x += dir * target)
        {
            if ((dir < 0f && x < end) || (dir > 0f && x > end)) break;
            if (++steps > MaxPerSide) break;

            if (!IsFree(allX, x, occupied)) continue;
            if (!TryPlaceX(x, groundY, notBuildableMask)) continue;

            if (SpawnSpot(prefab, layer, template, x, groundY, planeZ, allX))
                added++;
        }
        return added;
    }

    /// <summary>
    /// 实例化一个基底：照抄 Tower.DestroyTower 配方（Instantiate 资产换皮
    /// 预制体 → 挂 gameLayer → 权威端 RegisterObject(SemiStatic) 分配新
    /// NetID）。朝向/缩放抄该侧最近原生基底（镜像一致）；命名带 KEM 标记。
    /// </summary>
    private static bool SpawnSpot(
        GameObject prefab, Transform layer, GameObject template,
        float x, float y, float z, List<float> allX)
    {
        GameObject spot = UnityEngine.Object.Instantiate(
            prefab, new Vector3(x, y, z), Quaternion.identity, layer);
        if (spot == null) return false;

        try
        {
            // 朝向/缩放抄原生基底；模板缺失时保持预制体默认（DestroyTower 对
            // 换皮基底也直接用默认缩放，仅美观差异）。
            if (template != null && template.transform != null)
            {
                spot.transform.rotation = template.transform.rotation;
                spot.transform.localScale = template.transform.localScale;
            }

            // 标记名（幂等识别：参考集排除 + 读档恢复时 ObjectData.name 保留）
            spot.name = MarkerPrefix + "_" + x.ToString("F1");

            // 网络注册（DestroyTower 权威端同款）：给 Payable/Tower 的 IRPCable
            // 分配 SemiStatic NetID；RegisterObject 内部查重，重复调用安全。
            // 之后玩家购买走原生 PayableUpgrade.Pay（新塔用 nextObjectNetId
            // 注册），存档走 ObjectData(netID)/CRPCStamp——全部原生闭环。
            if (NetworkBigBoss.HasWorldAuth && NetworkPostbox.Instance != null)
            {
                NetworkPostbox.Instance.RegisterObject(spot, CRPCType.SemiStatic);
            }

            // 新点立即进占用集（仅占用集！进参考集会破坏幂等）：同轮后续网格
            // 点/另一侧扫描都会避开它。
            allX.Add(x);
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
        catch { }
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
