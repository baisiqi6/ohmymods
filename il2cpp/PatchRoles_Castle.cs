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
/// - CreateItem 安全产出：SpawnOrInstantiate 代替 Spawn，防止 Pool 不存在导致支付卡死
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - Castle.CatchupToLevel(bool includePrevious) —— 存在；【差异】2.1.0 无参，2.4.0 多 bool 参数（postfix 忽略）
/// - Castle.ReQueueAllBuildings() —— 存在，无变化
/// - Castle.level : Castle.Level（枚举 Castle1..Castle6）—— 存在
/// - PayableShop.CreateItem(bool blink = true) : Droppable —— 存在
/// - PayableShop.itemPrefab : Droppable —— 存在
/// - PayableShop.ShopType（枚举：NinjaLeft/NinjaRight/ShieldShopLeft/ShieldShopRight/Pike 等）—— 存在
/// - ShopPlanner.IsPlacedOrQueued(ShopType) —— 存在
/// - ShopPlanner.QueueNewShopForPlacement(ShopType, Il2CppSystem.Nullable&lt;Side&gt; side = null) —— 【差异】Side 变 Nullable&lt;Side&gt;，本组省略 side 由 ShopType 推导
/// - ShopPlanner.HasPlacedShop(ShopType, GameObject go = null) —— 【差异】多可选 GameObject 参数
/// - ShopPlanner.RemoveShop(GameObject) —— 存在
/// - ShopPlanner._placedShops : Il2CppReferenceArray&lt;GameObject&gt; —— 存在（原 GameObject[]）
/// - Pool.SpawnOrInstantiate&lt;T&gt;(T, Vector3, Quaternion, Transform = null) where T : Component —— 存在
/// - Pool.GetPoolFromPrefabAsset(GameObject) : Pool —— 存在
/// - PoolManager.CreatePoolFor(GameObject) : Pool —— 存在
/// - PoolManager.cachedPools/cachedNamePoolPairs/cachedSyncIdPoolPairs —— 存在（公开属性，免反射）
/// - Pool.sync : bool / Pool.syncID : short / Pool.prefab : GameObject —— 存在
/// - SpriteRendererFX.BlinkOverlay(Color) —— 【缺失】2.4.0 无此方法（改为 BlinkRoutine/FlashRoutine 协程），本地闪烁省略，保留 Droppable.SendBlinkRequest 联网同步
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

[HarmonyPatch(typeof(PayableShop), nameof(PayableShop.CreateItem))]
public static class PayableShop_CreateItem_Patch
{
    [HarmonyPrefix]
    public static bool CreateItem_Prefix(PayableShop __instance, ref Droppable __result, bool blink)
    {
        if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != PatchRoles_Castle.GREECE_BIOME)
            return true;
        if (__instance.itemPrefab == null)
            return true;

        try
        {
            Droppable droppable = Pool.SpawnOrInstantiate<Droppable>(
                __instance.itemPrefab,
                __instance.transform.position,
                Quaternion.identity,
                __instance.transform.parent);

            if (droppable == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] CreateItem: SpawnOrInstantiate returned null for " + __instance.itemPrefab.name);
                return true;
            }

            droppable.dropper = __instance.gameObject;
            Rigidbody2D rb = droppable.GetComponent<Rigidbody2D>();
            if (rb != null) rb.isKinematic = true;

            // 2.4.0 SpriteRendererFX.BlinkOverlay 已移除，本地闪烁省略；保留联网 blink 同步
            if (blink && NetworkBigBoss.IsOnline)
            {
                droppable.SendBlinkRequest(new Color(1f, 1f, 1f, 0.6f));
            }

            __result = droppable;
            return false;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return true;
        }
    }
}

public static class PatchRoles_Castle
{
    public static int GREECE_BIOME => BiomeHolder.GreeceBiomeIndex;

    private static readonly string BERSERKER_SHOP_MARKER = "MyMod_BerserkerShop";
    private static int _nextPoolSyncId = 30000;
    private static Il2CppArrayBase<DroppableTool> _allToolsCache;

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

            var sp = Managers.Inst.shopPlanner;
            if (sp == null) return;

            // 2.4.0 side 参数变 Nullable<Side> 且可选，NinjaLeft/NinjaRight 已编码左右，省略 side
            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.NinjaLeft))
            {
                sp.QueueNewShopForPlacement(PayableShop.ShopType.NinjaLeft);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued NinjaLeft shop for Greece");
            }
            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.NinjaRight))
            {
                sp.QueueNewShopForPlacement(PayableShop.ShopType.NinjaRight);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued NinjaRight shop for Greece");
            }

            EnsurePoolForDroppableTool("ToolNinja");
            EnsurePoolForCharacter("Ninja");
        }
        catch (Exception e)
        {
            // 时序性失败（城堡升级中 shopPlanner 引用重建）：CatchupToLevel 多次触发，
            // shopPlanner 就绪后自然重试——降级为 Warning。
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[Roles] Ninja shop queue skipped (transient): " + e.Message);
        }
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

            var sp = Managers.Inst.shopPlanner;
            if (sp == null) return;

            ReplacePlacedShopIfWrong(sp, PayableShop.ShopType.ShieldShopLeft);
            ReplacePlacedShopIfWrong(sp, PayableShop.ShopType.ShieldShopRight);

            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.ShieldShopLeft))
            {
                sp.QueueNewShopForPlacement(PayableShop.ShopType.ShieldShopLeft);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued Berserker shop (ShieldShopLeft) for Greece");
            }
            if (!sp.IsPlacedOrQueued(PayableShop.ShopType.ShieldShopRight))
            {
                sp.QueueNewShopForPlacement(PayableShop.ShopType.ShieldShopRight);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Roles] Queued Berserker shop (ShieldShopRight) for Greece");
            }

            EnsurePoolForDroppableTool("ToolBerserker");
            EnsurePoolForCharacter("Berserker");
            EnsurePoolForCharacter("BerserkerLeader");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[Roles] Berserker shop queue skipped (transient): " + e.Message);
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

            // Holder 未就绪（Init 早于 Holder.InitializeTagCharacterPairs）时静默跳过——
            // 角色池由 Holder postfix 补注册（ReRegisterModPools 幂等），避免时序噪音错误。
            var holder = Managers.Inst != null ? Managers.Inst.holder : null;
            if (holder == null || holder.tagCharacterPairs == null || !holder.tagCharacterPairs.ContainsKey("Ninja")) return;

            // 工具池
            EnsurePoolForDroppableTool("ToolNinja");
            EnsurePoolForDroppableTool("ToolBerserker");
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
