using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    /// <summary>
    /// 希腊世界(biome=5)自洽商店生成：
    /// - 忍者商店：走 ShopPlanner 标准队列系统（有 ShopType 枚举）
    /// - 狂战士工具商店：动态克隆已有商店 + 替换 itemPrefab（无 ShopType 枚举，无商店 prefab）
    /// - CreateItem 安全产出：用 SpawnOrInstantiate 代替 Spawn，防止 Pool 不存在导致支付系统卡死
    /// </summary>
    public static class Patch_Castle
    {
        private const int GREECE_BIOME = 5;

        // ============================================================
        // 注册
        // ============================================================

        public static void Register(HarmonyInstance harmony)
        {
            var castleType = typeof(Castle);

            var catchupMethod = castleType.GetMethod("CatchupToLevel",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (catchupMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Castle).GetMethod("CatchupToLevel_Postfix"));
                harmony.Patch(catchupMethod, null, postfix);
                Debug.Log("[MyMod] Patched Castle.CatchupToLevel");
            }

            var requeueMethod = castleType.GetMethod("ReQueueAllBuildings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (requeueMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Castle).GetMethod("ReQueueAllBuildings_Postfix"));
                harmony.Patch(requeueMethod, null, postfix);
                Debug.Log("[MyMod] Patched Castle.ReQueueAllBuildings");
            }

            var createItemMethod = typeof(PayableShop).GetMethod("CreateItem",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (createItemMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Castle).GetMethod("CreateItem_Prefix"));
                harmony.Patch(createItemMethod, prefix, null);
                Debug.Log("[MyMod] Patched PayableShop.CreateItem (cross-biome safe spawn)");
            }
        }

        // ============================================================
        // Postfix 入口
        // ============================================================

        public static void CatchupToLevel_Postfix(Castle __instance)
        {
            EnsureNinjaShopsInGreece(__instance);
            EnsureBerserkerToolShopInGreece(__instance);
        }

        public static void ReQueueAllBuildings_Postfix(Castle __instance)
        {
            EnsureNinjaShopsInGreece(__instance);
            EnsureBerserkerToolShopInGreece(__instance);
        }

        // ============================================================
        // 忍者商店：走 ShopPlanner 标准队列（有 ShopType 枚举）
        // ============================================================

        private static void EnsureNinjaShopsInGreece(Castle castle)
        {
            if (!Main.Enabled) return;
            try
            {
                if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != GREECE_BIOME) return;
                if (castle == null || castle.level < Castle.Level.Castle5) return;

                var sp = SingletonMonoBehaviour<Managers>.Inst.shopPlanner;
                if (sp == null) return;

                if (!sp.IsPlacedOrQueued(PayableShop.ShopType.NinjaLeft))
                {
                    sp.QueueNewShopForPlacement(PayableShop.ShopType.NinjaLeft, Side.Left);
                    Debug.Log("[MyMod] Queued NinjaLeft shop for Greece");
                }
                if (!sp.IsPlacedOrQueued(PayableShop.ShopType.NinjaRight))
                {
                    sp.QueueNewShopForPlacement(PayableShop.ShopType.NinjaRight, Side.Right);
                    Debug.Log("[MyMod] Queued NinjaRight shop for Greece");
                }

                EnsurePoolForDroppableTool("ToolNinja");

                // Ninja 角色转化也要走 Pool（ReplaceBy → Pool.Spawn），希腊世界没这个池
                EnsurePoolForCharacter("Ninja");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] EnsureNinjaShopsInGreece error: " + e.Message);
            }
        }

        // ============================================================
        // 狂战士工具商店：动态克隆（无 ShopType 枚举，无商店 prefab）
        // ============================================================

        private static readonly string BERSERKER_SHOP_MARKER = "MyMod_BerserkerShop";

        /// <summary>
        /// 狂战士商店原生刷新（希腊世界）：
        /// - Patch_ShopPlanner 已把 ShieldShopLeft/Right 槽位替换为 ShopBerserker_norselands prefab
        ///   （Pike 9/10 保留给长矛商店）
        /// - 希腊城堡 optionalShopType=Pike，原版 Castle4 入队 Pike；本方法入队 ShieldShop 12/13
        /// - Patch_SidedShop 在 Awake 时把 Berserker 实例身份改写为 12/13，全链路原生自洽
        /// - 本方法做双路径保险（CatchupToLevel + ReQueueAllBuildings）+ 清理旧版克隆残留
        /// </summary>
        private static void EnsureBerserkerToolShopInGreece(Castle castle)
        {
            if (!Main.Enabled) return;
            try
            {
                if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != GREECE_BIOME) return;
                if (castle == null || castle.level < Castle.Level.Castle4) return;

                // 清理旧版克隆残留商店（MyMod_BerserkerShop，卖 ToolBow 的 bug 版）
                GameObject stale = GameObject.Find(BERSERKER_SHOP_MARKER);
                if (stale != null)
                {
                    PayableShop staleShop = stale.GetComponent<PayableShop>();
                    bool isRealBerserker = staleShop != null && staleShop.itemPrefab != null
                        && staleShop.itemPrefab.CompareTag("BerserkerTool");
                    if (!isRealBerserker)
                    {
                        // 销毁前校验槽位归属：避免 DestroyImmediate → RemoveShop 误清真实商店槽位
                        var staleSp = SingletonMonoBehaviour<Managers>.Inst.shopPlanner;
                        if (staleSp != null)
                        {
                            ShopTag staleTag = stale.GetComponent<ShopTag>();
                            if (staleTag != null && staleSp.HasPlacedShop(staleTag.type, stale))
                            {
                                staleSp.RemoveShop(stale);
                            }
                        }
                        Debug.Log("[MyMod] Found stale Berserker shop, destroying");
                        GameObject.DestroyImmediate(stale);
                    }
                }

                // 原生队列：ShieldShop 槽位(12/13)已是狂战士商店 prefab，入队即原生摆放
                // （希腊原版不会入队 ShieldShop——那是北境 biome=3 的；Pike 9/10 保留给长矛）
                var sp = SingletonMonoBehaviour<Managers>.Inst.shopPlanner;
                if (sp == null) return;

                // 槽位内容校验：旧存档可能已在 12/13 摆过盾牌商店（上一版 bug），
                // 发现不是卖 BerserkerTool 的商店就销毁并重新入队
                ReplacePlacedShopIfWrong(sp, PayableShop.ShopType.ShieldShopLeft, Side.Left);
                ReplacePlacedShopIfWrong(sp, PayableShop.ShopType.ShieldShopRight, Side.Right);

                if (!sp.IsPlacedOrQueued(PayableShop.ShopType.ShieldShopLeft))
                {
                    sp.QueueNewShopForPlacement(PayableShop.ShopType.ShieldShopLeft, Side.Left);
                    Debug.Log("[MyMod] Queued Berserker shop (ShieldShopLeft) for Greece");
                }
                if (!sp.IsPlacedOrQueued(PayableShop.ShopType.ShieldShopRight))
                {
                    sp.QueueNewShopForPlacement(PayableShop.ShopType.ShieldShopRight, Side.Right);
                    Debug.Log("[MyMod] Queued Berserker shop (ShieldShopRight) for Greece");
                }

                // 池保障：工具 + 角色转化
                EnsurePoolForDroppableTool("ToolBerserker");
                EnsurePoolForCharacter("Berserker");
                EnsurePoolForCharacter("BerserkerLeader");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] EnsureBerserkerToolShopInGreece error: " + e.Message);
            }
        }
        /// <summary>
        /// 检查指定槽位已放置的商店：如果卖的不是 BerserkerTool（旧版盾牌商店残留），
        /// 销毁并清空槽位，让后续 QueueNewShopForPlacement 重新摆放狂战士商店。
        /// </summary>
        private static void ReplacePlacedShopIfWrong(ShopPlanner sp, PayableShop.ShopType type, Side side)
        {
            try
            {
                if (sp == null || !sp.HasPlacedShop(type)) return;

                GameObject placed = null;
                var placedField = typeof(ShopPlanner).GetField("_placedShops",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (placedField != null)
                {
                    var placedArr = placedField.GetValue(sp) as GameObject[];
                    if (placedArr != null && (int)type < placedArr.Length)
                        placed = placedArr[(int)type];
                }
                if (placed == null) return;

                PayableShop ps = placed.GetComponent<PayableShop>();
                bool isBerserker = ps != null && ps.itemPrefab != null
                    && ps.itemPrefab.CompareTag("BerserkerTool");
                if (isBerserker) return; // 已经是狂战士商店

                Debug.Log("[MyMod] Replacing wrong shop in slot " + type + " (" + placed.name + ")");
                sp.RemoveShop(placed);
                GameObject.DestroyImmediate(placed);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] ReplacePlacedShopIfWrong error: " + e.Message);
            }
        }

        // ============================================================
        // CreateItem 安全产出：防止 Pool 不存在导致支付系统卡死
        // ============================================================

        public static bool CreateItem_Prefix(PayableShop __instance, ref Droppable __result, bool blink)
        {
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != GREECE_BIOME)
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
                    Debug.LogError("[MyMod] CreateItem: SpawnOrInstantiate returned null for " + __instance.itemPrefab.name);
                    return true;
                }

                Debug.Log("[MyMod][CREATE] shop=" + __instance.gameObject.name + " item=" + __instance.itemPrefab.name
                    + " tag=" + droppable.tag + " active=" + droppable.gameObject.activeSelf
                    + " layer=" + LayerMask.LayerToName(droppable.gameObject.layer)
                    + " pos=" + droppable.transform.position);
                droppable.dropper = __instance.gameObject;
                Rigidbody2D rb = droppable.GetComponent<Rigidbody2D>();
                if (rb != null) rb.isKinematic = true;

                if (blink)
                {
                    Color color = new Color(1f, 1f, 1f, 0.6f);
                    SpriteRendererFX fx = droppable.GetComponent<SpriteRendererFX>();
                    if (fx != null)
                    {
                        fx.BlinkOverlay(color);
                        if (NetworkBigBoss.IsOnline) droppable.SendBlinkRequest(color);
                    }
                }

                __result = droppable;
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] CreateItem_Prefix error: " + e.Message);
                return true;
            }
        }

        // ============================================================
        // 对象池保障
        // ============================================================

        public static void EnsurePoolForDroppableTool(string toolName)
        {
            try
            {
                var pm = SingletonMonoBehaviour<Managers>.Inst.pools;
                if (pm == null) return;

                DroppableTool prefab = LoadDroppableTool(toolName);

                // 同 EnsurePoolForCharacter：用 poolsByPrefab 静态字典判断可用性
                if (Pool.GetPoolFromPrefabAsset(prefab.gameObject) != null) return;

                DestroyOrphanPools(pm, prefab.gameObject);

                RegisterSyncedPool(pm, prefab.gameObject, toolName);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] EnsurePoolForDroppableTool error: " + e.Message);
            }
        }

        // ============================================================

        /// <summary>
        /// 为角色 prefab 创建 Pool（转化时 ReplaceBy → Pool.Spawn<Character> 需要池）。
        /// 希腊世界不加载幕府/北境的角色池。
        /// </summary>
        public static void EnsurePoolForCharacter(string tag)
        {
            try
            {
                var pm = SingletonMonoBehaviour<Managers>.Inst.pools;
                var holder = SingletonMonoBehaviour<Managers>.Inst.holder;
                if (pm == null || holder == null) return;

                Character prefab = holder.GetCharacterByTag(tag);
                if (prefab == null) { Debug.LogError("[MyMod] EnsurePoolForCharacter: tag '" + tag + "' not in Holder"); return; }

                // 关键：用 poolsByPrefab 静态字典判断（GetPoolFromPrefabAsset），
                // 不用 GetComponentsInChildren<Pool>——后者只看物理 GameObject，
                // InitPools 清缓存后物理池还在但不可用，会导致跳过注册。
                if (Pool.GetPoolFromPrefabAsset(prefab.gameObject) != null) return;

                // 清理孤儿物理池（InitPools 清缓存后残留的旧 Pool GameObject）
                DestroyOrphanPools(pm, prefab.gameObject);

                RegisterSyncedPool(pm, prefab.gameObject, "char:" + tag);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] EnsurePoolForCharacter error: " + e.Message);
            }
        }

        /// <summary>
        /// 销毁指定 prefab 的孤儿物理池（Pool GameObject 存在但 poolsByPrefab 静态字典
        /// 里没有——InitPools 清缓存后的残留）。避免 CreatePoolFor 累积重复池。
        /// </summary>
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
                Debug.LogError("[MyMod] DestroyOrphanPools error: " + e.Message);
            }
        }

        private static int _nextPoolSyncId = 30000;

        /// <summary>
        /// 创建并注册一个 sync 池。必须 sync=true 才会：
        /// 1. FastSpawn 时 RegisterPoolInstance → 设置 parentHeaderRef（否则 Ninja.SendSide 等 NRE）
        /// 2. 联机时 SendPoolSpawn 同步给客户端
        /// 3. 加入 cachedPools 才能被 DoUpdate 延迟销毁 / ResetPools 重置
        /// </summary>
        private static void RegisterSyncedPool(PoolManager pm, GameObject prefab, string label)
        {
            try
            {
                Pool pool = pm.CreatePoolFor(prefab);
                if (pool == null) { Debug.LogError("[MyMod] RegisterSyncedPool: CreatePoolFor returned null for " + label); return; }

                // 设置 sync 和唯一 syncID（2.1.0 中 Pool.syncID 为 short，需显式转换）
                pool.sync = true;
                pool.syncID = (short)_nextPoolSyncId++;

                // 注册到 PoolManager 缓存（反射访问 private 字段）
                var cachedPoolsField = typeof(PoolManager).GetField("cachedPools", BindingFlags.NonPublic | BindingFlags.Instance);
                var cachedNameField = typeof(PoolManager).GetField("cachedNamePoolPairs", BindingFlags.NonPublic | BindingFlags.Instance);
                var cachedSyncIdField = typeof(PoolManager).GetField("cachedSyncIdPoolPairs", BindingFlags.NonPublic | BindingFlags.Instance);

                if (cachedPoolsField != null)
                {
                    var cachedPools = cachedPoolsField.GetValue(pm) as List<Pool>;
                    if (cachedPools != null && !cachedPools.Contains(pool)) cachedPools.Add(pool);
                }
                if (cachedNameField != null)
                {
                    var cachedNames = cachedNameField.GetValue(pm) as Dictionary<string, Pool>;
                    if (cachedNames != null && !cachedNames.ContainsKey(pool.prefab.name))
                        cachedNames.Add(pool.prefab.name, pool);
                }
                if (cachedSyncIdField != null)
                {
                    var cachedSyncIds = cachedSyncIdField.GetValue(pm) as Dictionary<int, Pool>;
                    if (cachedSyncIds != null && !cachedSyncIds.ContainsKey(pool.syncID))
                        cachedSyncIds.Add(pool.syncID, pool);
                }

                Debug.Log("[MyMod] Registered synced pool for " + label + " (syncID=" + pool.syncID + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] RegisterSyncedPool error: " + e.Message);
            }
        }

        private static DroppableTool[] _allToolsCache;

        /// <summary>
        /// 用 LoadAll 查找 DroppableTool（Resources.Load 按名字查不到子目录下的资源）。
        /// </summary>
        private static DroppableTool LoadDroppableTool(string name)
        {
            if (_allToolsCache == null)
                _allToolsCache = Resources.LoadAll<DroppableTool>("");
            foreach (var t in _allToolsCache)
            {
                if (t != null && t.gameObject.name == name) return t;
            }
            Debug.LogError("[MyMod] LoadDroppableTool: " + name + " not found in " + (_allToolsCache != null ? _allToolsCache.Length : 0) + " tools");
            return null;
        }
    }
}
