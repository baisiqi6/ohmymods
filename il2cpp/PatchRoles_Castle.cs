using System;
using UnityEngine;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod;

/// <summary>
/// 希腊世界(biome=5)自洽商店生成：
/// - 忍者商店：走 ShopPlanner 标准队列（ShopType 枚举，NinjaLeft/NinjaRight）
/// - 狂战士工具商店：ShieldShop 槽位(12/13)已被 Patch_ShopPlanner 换成 Berserker prefab，
///   本组入队 ShieldShopLeft/Right + 清理旧版克隆残留
/// - ToolNinja/ToolBerserker 与角色池在初始化期显式注册，商店产出保留原生 CreateItem 路径
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - Castle.CatchupToLevel(bool includePrevious) —— 存在；【差异】2.1.0 无参，2.4.0 多 bool 参数（postfix 忽略）
/// - Castle.ReQueueAllBuildings() —— 存在，无变化
/// - Castle.level : Castle.Level（枚举 Castle1..Castle6）—— 存在
/// - PayableShop.ShopType（枚举：NinjaLeft/NinjaRight/ShieldShopLeft/ShieldShopRight/Pike 等）—— 存在
/// - ShopPlanner.IsPlacedOrQueued(ShopType) —— 存在
/// - ShopPlanner.QueueNewShopForPlacement(ShopType, Il2CppSystem.Nullable&lt;Side&gt; side = null) —— 【差异】Side 变 Nullable&lt;Side&gt;，左右商店显式传 Side
/// - ShopPlanner.HasPlacedShop(ShopType, GameObject go = null) —— 【差异】多可选 GameObject 参数
/// - ShopPlanner.RemoveShop(GameObject) —— 存在
/// - ShopPlanner._placedShops : Il2CppReferenceArray&lt;GameObject&gt; —— 存在（原 GameObject[]）
/// - Pool.GetPoolFromPrefabAsset(GameObject) : Pool —— 存在
/// - PoolManager.CreatePoolFor(GameObject) : Pool —— 存在
/// - PoolManager.cachedPools/cachedNamePoolPairs/cachedSyncIdPoolPairs —— 存在（公开属性，免反射）
/// - Pool.sync : bool / Pool.syncID : short / Pool.prefab : GameObject —— 存在
/// </summary>

[HarmonyPatch(typeof(Castle))]
public static class Castle_Queue_Patch
{
    [HarmonyPatch(nameof(Castle.CatchupToLevel))]
    [HarmonyPostfix]
    public static void CatchupToLevel_Postfix(Castle __instance)
    {
        PatchRoles_Castle.EnsureNinjaShopsInGreece(__instance);
        PatchRoles_Castle.EnsureBerserkerToolShopInGreece(__instance);
    }

    [HarmonyPatch(nameof(Castle.ReQueueAllBuildings))]
    [HarmonyPostfix]
    public static void ReQueueAllBuildings_Postfix(Castle __instance)
    {
        PatchRoles_Castle.EnsureNinjaShopsInGreece(__instance);
        PatchRoles_Castle.EnsureBerserkerToolShopInGreece(__instance);
    }
}

/// <summary>
/// Castle.CatchupToLevel 可能早于 ShopPlanner.Start；此时 IsPlacedOrQueued 所需的
/// ShopPlanner 状态还不稳定。等 Start（含 InitializeShopTypePrefabPairs）完整返回后，
/// 再对已经存在的城堡执行一次幂等补建。
/// </summary>
[HarmonyPatch(typeof(ShopPlanner), nameof(ShopPlanner.Start))]
public static class ShopPlanner_Start_GreeceShopRetry_Patch
{
    [HarmonyPostfix]
    public static void Start_Postfix(ShopPlanner __instance)
    {
        PatchRoles_Castle.RetryGreeceShopsAfterPlannerStart(__instance);
    }
}

public static class PatchRoles_Castle
{
    public static int GREECE_BIOME => BiomeHolder.GreeceBiomeIndex;

    private static readonly string BERSERKER_SHOP_MARKER = "MyMod_BerserkerShop";
    private static int _nextPoolSyncId = 30000;
    private const int BANK_ASSISTANT_SYNC_ID_MIN = 30120;
    private const int BANK_ASSISTANT_SYNC_ID_MAX = 30123;
    private static Il2CppArrayBase<DroppableTool> _allToolsCache;
    private static ShopPlanner _initializedShopPlanner;

    // ============================================================
    // 忍者商店：走 ShopPlanner 标准队列
    // ============================================================
    public static void EnsureNinjaShopsInGreece(Castle castle)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != GREECE_BIOME) return;
            if (castle == null || castle.level < Castle.Level.Castle5) return;
            if (!NetworkBigBoss.HasWorldAuth) return;

            // Castle 的升级/读档回放可能发生在 ShopPlanner.Start 之前；Start postfix
            // 会从 Kingdom 取现有城堡补建，禁止此处提前调用 IsPlacedOrQueued。
            var managers = Managers.Inst;
            var sp = managers != null ? managers.shopPlanner : null;
            if (sp == null || _initializedShopPlanner != sp) return;

            if (sp.shopTypePrefabPairs == null
                || !sp.shopTypePrefabPairs.ContainsKey(PayableShop.ShopType.NinjaLeft)
                || !sp.shopTypePrefabPairs.ContainsKey(PayableShop.ShopType.NinjaRight))
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Roles] Ninja shop prefab mapping is missing after ShopPlanner.Start");
                return;
            }
            if (sp.raisingShops == null || sp._placedShops == null || sp._queuedShopPlacements == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Roles] Ninja shop queue state is unavailable after ShopPlanner.Start");
                return;
            }

            RepairQueuedSidedShopValues(sp);

            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.NinjaLeft))
            {
                sp.QueueNewShopForPlacement(
                    PayableShop.ShopType.NinjaLeft,
                    new Il2CppSystem.Nullable<Side>(Side.Left));
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued NinjaLeft shop for Greece");
            }
            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.NinjaRight))
            {
                sp.QueueNewShopForPlacement(
                    PayableShop.ShopType.NinjaRight,
                    new Il2CppSystem.Nullable<Side>(Side.Right));
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued NinjaRight shop for Greece");
            }

            EnsurePoolForDroppableTool("ToolNinja");
            EnsurePoolForCharacter("Ninja");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Roles] Ninja shop queue failed after readiness checks: " + e);
        }
    }

    /// <summary>
    /// 旧版本曾用空 side 入队，存档会继续保留这批条目。仅修正尚在队列中的
    /// Ninja/ShieldShop 左右条目；不改变队列结构，已放置商店和原版
    /// CanShopFit/科技门槛均不改动。
    /// </summary>
    private static void RepairQueuedSidedShopValues(ShopPlanner sp)
    {
        int normalized = 0;
        foreach (var queued in sp._queuedShopPlacements)
        {
            if (queued == null) continue;

            Side expected;
            switch (queued.shopType)
            {
            case PayableShop.ShopType.NinjaLeft:
            case PayableShop.ShopType.ShieldShopLeft:
                expected = Side.Left;
                break;
            case PayableShop.ShopType.NinjaRight:
            case PayableShop.ShopType.ShieldShopRight:
                expected = Side.Right;
                break;
            default:
                continue;
            }

            // 不读取旧 shopSide：旧存档的 native nullable 为空时，仅调用 getter
            // 就会在 interop Nullable(IntPtr) / CreateGCHandle 中抛 NRE。
            queued.shopSide = new Il2CppSystem.Nullable<Side>(expected);
            normalized++;
        }

        if (normalized > 0)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogDebug(
                "[Roles] Normalized " + normalized + " queued sided shop value(s)");
        }
    }

    public static void RetryGreeceShopsAfterPlannerStart(ShopPlanner shopPlanner)
    {
        _initializedShopPlanner = shopPlanner;
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
        if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != GREECE_BIOME) return;

        Castle castle = null;
        var managers = Managers.Inst;
        if (managers != null && managers.kingdom != null)
            castle = managers.kingdom.castle;
        EnsureNinjaShopsInGreece(castle);
        EnsureBerserkerToolShopInGreece(castle);
    }

    // ============================================================
    // 狂战士工具商店：ShieldShop 槽位原生刷新
    // ============================================================
    public static void EnsureBerserkerToolShopInGreece(Castle castle)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != GREECE_BIOME) return;
            if (castle == null || castle.level < Castle.Level.Castle4) return;
            if (!NetworkBigBoss.HasWorldAuth) return;

            var managers = Managers.Inst;
            var sp = managers != null ? managers.shopPlanner : null;
            if (sp == null || _initializedShopPlanner != sp) return;
            if (sp.raisingShops == null || sp._placedShops == null || sp._queuedShopPlacements == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Roles] Berserker shop queue state is unavailable after ShopPlanner.Start");
                return;
            }

            RepairQueuedSidedShopValues(sp);

            // 清理旧版克隆残留（MyMod_BerserkerShop，卖 ToolBow 的 bug 版）
            GameObject stale = GameObject.Find(BERSERKER_SHOP_MARKER);
            if (stale != null)
            {
                PayableShop staleShop = stale.GetComponent<PayableShop>();
                bool isRealBerserker = staleShop != null && staleShop.itemPrefab != null
                    && staleShop.itemPrefab.CompareTag("BerserkerTool");
                if (!isRealBerserker)
                {
                    var staleSp = Managers.Inst.shopPlanner;
                    if (staleSp != null)
                    {
                        ShopTag staleTag = stale.GetComponent<ShopTag>();
                        if (staleTag != null && staleSp.HasPlacedShop(staleTag.type, stale))
                        {
                            staleSp.RemoveShop(stale);
                        }
                    }
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Found stale Berserker shop, destroying");
                    GameObject.DestroyImmediate(stale);
                }
            }

            ReplacePlacedShopIfWrong(sp, PayableShop.ShopType.ShieldShopLeft);
            ReplacePlacedShopIfWrong(sp, PayableShop.ShopType.ShieldShopRight);

            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.ShieldShopLeft))
            {
                sp.QueueNewShopForPlacement(
                    PayableShop.ShopType.ShieldShopLeft,
                    new Il2CppSystem.Nullable<Side>(Side.Left));
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued Berserker shop (ShieldShopLeft) for Greece");
            }
            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.ShieldShopRight))
            {
                sp.QueueNewShopForPlacement(
                    PayableShop.ShopType.ShieldShopRight,
                    new Il2CppSystem.Nullable<Side>(Side.Right));
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued Berserker shop (ShieldShopRight) for Greece");
            }

            EnsurePoolForDroppableTool("ToolBerserker");
            EnsurePoolForCharacter("Berserker");
            EnsurePoolForCharacter("BerserkerLeader");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Roles] Berserker shop queue failed after readiness checks: " + e);
        }
    }

    /// <summary>
    /// 槽位已放置商店但卖的不是 BerserkerTool（旧版盾牌商店残留）→ 销毁并清空槽位。
    /// </summary>
    private static void ReplacePlacedShopIfWrong(ShopPlanner sp, PayableShop.ShopType type)
    {
        try
        {
            if (sp == null || !sp.HasPlacedShop(type)) return;

            GameObject placed = null;
            var placedArr = sp._placedShops;
            if (placedArr != null && (int)type < placedArr.Length)
                placed = placedArr[(int)type];
            if (placed == null) return;

            PayableShop ps = placed.GetComponent<PayableShop>();
            bool isBerserker = ps != null && ps.itemPrefab != null
                && ps.itemPrefab.CompareTag("BerserkerTool");
            if (isBerserker) return;

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Replacing wrong shop in slot " + type + " (" + placed.name + ")");
            sp.RemoveShop(placed);
            GameObject.DestroyImmediate(placed);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    // ============================================================
    // 对象池保障
    // ============================================================
    /// <summary>
    /// 重新注册 mod 的 sync 池（PoolFix 的 force InitPools 清掉了它们）。幂等：
    /// EnsurePoolFor* 内部有去重检查。供 PatchPoolFix.PoolManager_OnLevelLoaded_Patch 调用。
    /// </summary>
    public static void ReRegisterModPools()
    {
        try
        {
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;

            // Cerberus' two Norse-visual Greece-logic pools do not depend on
            // Holder tags and must be registered on host and clients even when
            // PoolManager.Init runs before Holder.InitializeTagCharacterPairs.
            PatchDivine_GhostSquads.EnsurePools();

            // Holder 未就绪（Init 早于 Holder.InitializeTagCharacterPairs）时静默跳过——
            // 角色池由 Holder postfix 补注册（ReRegisterModPools 幂等），避免时序噪音错误。
            var holder = Managers.Inst != null ? Managers.Inst.holder : null;
            if (holder == null || holder.tagCharacterPairs == null || !holder.tagCharacterPairs.ContainsKey("Ninja")) return;

            // 工具池
            EnsurePoolForDroppableTool("ToolNinja");
            EnsurePoolForDroppableTool("ToolBerserker");
            // PoolManager.Init_Prefix 会强制 InitPools() 并清空全部运行时池缓存；
            // Holder 若已就绪，不会再次触发 Holder postfix，因此这里同步恢复忍者
            // 的飞镖/烟雾依赖池，再恢复 Ninja 角色池。
            PatchRoles_Ninja.EnsureRuntimePoolsInGreece(holder);
            // 角色池（从 Holder 拿 prefab）
            EnsurePoolForCharacter("Ninja");
            EnsurePoolForCharacter("Berserker");
            EnsurePoolForCharacter("BerserkerLeader");
            EnsurePoolForCharacter("Worker");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    public static void EnsurePoolForDroppableTool(string toolName)
    {
        try
        {
            var pm = Managers.Inst.pools;
            if (pm == null) return;

            DroppableTool prefab = LoadDroppableTool(toolName);
            if (prefab == null) return;

            if (Pool.GetPoolFromPrefabAsset(prefab.gameObject) != null) return;

            DestroyOrphanPools(pm, prefab.gameObject);
            RegisterSyncedPool(pm, prefab.gameObject, toolName);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 为角色 prefab 创建 sync 池（转化时 ReplaceBy → Pool.Spawn 需要池）。希腊世界不加载幕府/北境角色池。
    /// </summary>
    public static void EnsurePoolForCharacter(string tag)
    {
        try
        {
            var pm = Managers.Inst.pools;
            var holder = Managers.Inst.holder;
            if (pm == null || holder == null) return;

            Character prefab = holder.GetCharacterByTag(tag);
            if (prefab == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] EnsurePoolForCharacter: tag '" + tag + "' not in Holder");
                return;
            }

            if (Pool.GetPoolFromPrefabAsset(prefab.gameObject) != null) return;

            DestroyOrphanPools(pm, prefab.gameObject);
            RegisterSyncedPool(pm, prefab.gameObject, "char:" + tag);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    private static void DestroyOrphanPools(PoolManager pm, GameObject prefab)
    {
        try
        {
            Pool[] physical = pm.GetComponentsInChildren<Pool>();
            foreach (var p in physical)
            {
                if (p != null && p.prefab == prefab && Pool.GetPoolFromPrefabAsset(prefab) == null)
                {
                    UnityEngine.Object.Destroy(p.gameObject);
                }
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 创建并注册 sync 池。2.4.0 的 cachedPools/cachedNamePoolPairs/cachedSyncIdPoolPairs
    /// 均为公开属性，直接读写（原反射 SetValue）。
    /// </summary>
    private static void RegisterSyncedPool(PoolManager pm, GameObject prefab, string label)
    {
        try
        {
            Pool pool = pm.CreatePoolFor(prefab);
            if (pool == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] RegisterSyncedPool: CreatePoolFor returned null for " + label);
                return;
            }

            pool.sync = true;
            // 30120..30123 are deterministic bank-assistant pools. This allocator
            // survives repeated PoolManager.Init rebuilds, so it must explicitly
            // skip that reserved interval instead of eventually colliding with it.
            if (_nextPoolSyncId >= BANK_ASSISTANT_SYNC_ID_MIN
                && _nextPoolSyncId <= BANK_ASSISTANT_SYNC_ID_MAX)
                _nextPoolSyncId = BANK_ASSISTANT_SYNC_ID_MAX + 1;
            if (_nextPoolSyncId >= PatchDivine_GhostSquads.SyncIdMin
                && _nextPoolSyncId <= PatchDivine_GhostSquads.SyncIdMax)
                _nextPoolSyncId = PatchDivine_GhostSquads.SyncIdMax + 1;
            pool.syncID = (short)_nextPoolSyncId++;

            if (pm.cachedPools != null && !pm.cachedPools.Contains(pool)) pm.cachedPools.Add(pool);
            if (pm.cachedNamePoolPairs != null && !pm.cachedNamePoolPairs.ContainsKey(pool.prefab.name))
                pm.cachedNamePoolPairs.Add(pool.prefab.name, pool);
            if (pm.cachedSyncIdPoolPairs != null && !pm.cachedSyncIdPoolPairs.ContainsKey(pool.syncID))
                pm.cachedSyncIdPoolPairs.Add(pool.syncID, pool);

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Registered synced pool for " + label + " (syncID=" + pool.syncID + ")");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 用 LoadAll 查找 DroppableTool（返回 Il2CppArrayBase&lt;DroppableTool&gt;）。
    /// </summary>
    private static DroppableTool LoadDroppableTool(string name)
    {
        if (_allToolsCache == null)
            _allToolsCache = Resources.LoadAll<DroppableTool>("");
        for (int i = 0; i < _allToolsCache.Length; i++)
        {
            var t = _allToolsCache[i];
            if (t != null && t.gameObject.name == name) return t;
        }
        KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] LoadDroppableTool: " + name + " not found in " + _allToolsCache.Length + " tools");
        return null;
    }
}
