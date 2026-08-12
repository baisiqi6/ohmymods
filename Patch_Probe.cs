using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    /// <summary>
    /// 一次性探测 patch：dump 所有生物群系的商店/角色 prefab 数据。
    /// 运行一次后可删除此文件 + 从 build.bat / Main.cs 移除注册。
    ///
    /// 使用：编译后进任意世界，看 Player.log 里的 [PROBE] 输出。
    /// Player.log: %USERPROFILE%\AppData\LocalLow\Raw Fury\Kingdom Two Crowns\Player.log
    /// </summary>
    public static class Patch_Probe
    {
        private static bool _probed = false;
        private static readonly System.Collections.Generic.HashSet<int> _sceneShopProbedBiomes =
            new System.Collections.Generic.HashSet<int>();

        public static void Register(HarmonyInstance harmony)
        {
            var shopPlannerType = typeof(ShopPlanner);
            var initMethod = shopPlannerType.GetMethod("InitializeShopTypePrefabPairs",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (initMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Probe).GetMethod("Postfix"));
                harmony.Patch(initMethod, null, postfix);
                Debug.Log("[MyMod] Probe patched ShopPlanner.InitializeShopTypePrefabPairs");
            }

            // 场景商店探测：在场景加载后 dump 当前场景所有 PayableShop（新建/读档都覆盖）
            var castleType = typeof(Castle);
            var catchupMethod = castleType.GetMethod("CatchupToLevel",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (catchupMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Probe).GetMethod("SceneShop_Postfix"));
                harmony.Patch(catchupMethod, null, postfix);
                Debug.Log("[MyMod] Probe patched Castle.CatchupToLevel (scene shop dump)");
            }

            var requeueMethod = castleType.GetMethod("ReQueueAllBuildings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (requeueMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Probe).GetMethod("SceneShop_Postfix"));
                harmony.Patch(requeueMethod, null, postfix);
                Debug.Log("[MyMod] Probe patched Castle.ReQueueAllBuildings (scene shop dump)");
            }
        }

        /// <summary>
        /// dump 当前场景所有 PayableShop——识别北境场景里 BerserkerTool 商店的真实身份。
        /// 按 biome 缓存，每个生物群系只 dump 一次。
        /// </summary>
        public static void SceneShop_Postfix(Castle __instance)
        {
            int biomeIdx = BiomeHolder.Inst != null ? BiomeHolder.Inst.BiomeIndex : -1;
            if (biomeIdx < 0) return;
            if (_sceneShopProbedBiomes.Contains(biomeIdx)) return;
            _sceneShopProbedBiomes.Add(biomeIdx);

            try
            {
                Debug.Log("[PROBE-SCENE] ============================================================");
                Debug.Log("[PROBE-SCENE] === SCENE SHOPS DUMP (biome=" + BiomeHolder.Inst.BiomeIndex + ") ===");
                Debug.Log("[PROBE-SCENE] ============================================================");

                PayableShop[] allShops = UnityEngine.Object.FindObjectsOfType<PayableShop>();
                Debug.Log("[PROBE-SCENE] Total PayableShop in scene: " + allShops.Length);

                foreach (var s in allShops)
                {
                    if (s == null) continue;

                    ShopTag tag = s.GetComponent<ShopTag>();
                    PayableSidedShop sided = s as PayableSidedShop;
                    PayableWorkshop workshop = s.GetComponent<PayableWorkshop>();
                    WorkableBuilding wb = s.GetComponent<WorkableBuilding>();

                    string tagType = tag != null ? tag.type.ToString() + "(" + (int)tag.type + ")" : "NO-ShopTag";
                    string sidedInfo = sided != null ? "SidedShop(shopType=" + sided.shopType + " side=" + sided.side + ")" : "";
                    string workshopInfo = workshop != null ? "Workshop" : "";
                    string wbInfo = wb != null ? "WorkableBuilding" : "";
                    string itemInfo = s.itemPrefab != null ? "sells=" + s.itemPrefab.gameObject.name + " tag=" + s.itemPrefab.tag : "itemPrefab=null";

                    Debug.Log("[PROBE-SCENE] shop \"" + s.gameObject.name + "\""
                        + " tag=" + tagType
                        + " comps=[" + sidedInfo + workshopInfo + wbInfo + "]"
                        + " " + itemInfo
                        + " pos=" + s.transform.position);
                }

                // 也 dump 场景里所有 ShopTag（可能有不带 PayableShop 的商店）
                ShopTag[] allTags = UnityEngine.Object.FindObjectsOfType<ShopTag>();
                Debug.Log("[PROBE-SCENE] Total ShopTag in scene: " + allTags.Length);
                foreach (var t in allTags)
                {
                    if (t == null) continue;
                    bool hasShop = t.GetComponent<PayableShop>() != null;
                    Debug.Log("[PROBE-SCENE] ShopTag \"" + t.gameObject.name + "\""
                        + " type=" + t.type + "(" + (int)t.type + ")"
                        + " hasPayableShop=" + hasShop);
                }

                Debug.Log("[PROBE-SCENE] ============================================================");
                Debug.Log("[PROBE-SCENE] === GLOBAL Worker/Character PREFAB SEARCH ===");
                Debug.Log("[PROBE-SCENE] ============================================================");

                var allWorkers = Resources.LoadAll<Worker>("");
                Debug.Log("[PROBE-SCENE] Total Worker prefabs in Resources: " + allWorkers.Length);
                foreach (var w in allWorkers)
                {
                    if (w == null) continue;
                    string tag = "NULL";
                    try { tag = w.tag; } catch { }
                    // 组件 + 战斗配置探测
                    bool hasShieldUser = w.GetComponent<NpcShieldUser>() != null;
                    string canAttack = "?";
                    try
                    {
                        var f = typeof(Worker).GetField("canAttack", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null) canAttack = f.GetValue(w).ToString();
                    }
                    catch { }
                    Debug.Log("[PROBE-SCENE]   Worker prefab \"" + w.gameObject.name + "\" tag=\"" + tag
                        + "\" NpcShieldUser=" + hasShieldUser + " canAttack=" + canAttack);
                }

                // 也扫所有 Character（可能有 Worker 子类变体）
                var allChars = Resources.LoadAll<Character>("");
                Debug.Log("[PROBE-SCENE] Total Character prefabs in Resources: " + allChars.Length);
                foreach (var c in allChars)
                {
                    if (c == null) continue;
                    string tag = "NULL";
                    try { tag = c.tag; } catch { }
                    string typeName = c.GetType().Name;
                    // 打印 Peasant/Worker 相关 + 缩放
                    if (typeName.Contains("Worker") || tag.Contains("Worker") || tag.Contains("Peasant") || typeName == "Character")
                    {
                        Vector3 scale = c.transform.localScale;
                        Debug.Log("[PROBE-SCENE]   Character prefab \"" + c.gameObject.name + "\" tag=\"" + tag
                            + "\" type=" + typeName + " scale=" + scale.x + "," + scale.y + "," + scale.z);
                    }
                }

                Debug.Log("[PROBE-SCENE] ============================================================");
                Debug.Log("[PROBE-SCENE] === GLOBAL ShopTag PREFAB SEARCH ===");
                Debug.Log("[PROBE-SCENE] ============================================================");

                // 全局搜索所有 ShopTag prefab（Resources 里可能有 ShopBerserker_norselands）
                var allShopTags = Resources.LoadAll<ShopTag>("");
                Debug.Log("[PROBE-SCENE] Total ShopTag prefabs in Resources: " + allShopTags.Length);
                foreach (var st in allShopTags)
                {
                    if (st == null) continue;
                    PayableSidedShop ss = st.GetComponent<PayableSidedShop>();
                    PayableShop ps = st.GetComponent<PayableShop>();
                    string sells = ps != null && ps.itemPrefab != null ? "sells=" + ps.itemPrefab.gameObject.name : "no-item";
                    Debug.Log("[PROBE-SCENE]   ShopTag prefab \"" + st.gameObject.name + "\""
                        + " type=" + st.type + "(" + (int)st.type + ")"
                        + (ss != null ? " SidedShop(shopType=" + ss.shopType + ")" : "")
                        + " " + sells);
                }

                Debug.Log("[PROBE-SCENE] ============================================================");
                Debug.Log("[PROBE-SCENE] === SCENE SHOP DUMP COMPLETE ===");
                Debug.Log("[PROBE-SCENE] ============================================================");
            }
            catch (Exception e)
            {
                Debug.LogError("[PROBE-SCENE] Exception: " + e.Message);
            }
        }

        public static void Postfix(ShopPlanner __instance)
        {
            if (_probed) return;
            _probed = true;

            try
            {
                Debug.Log("[PROBE] ============================================================");
                Debug.Log("[PROBE] === STARTING FULL BIOME PROBE ===");
                Debug.Log("[PROBE] ============================================================");

                var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings",
                    BindingFlags.Public | BindingFlags.Instance);
                if (biomePathStringsField == null)
                {
                    Debug.LogError("[PROBE] biomePathStrings field not found!");
                    return;
                }

                var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
                if (biomePathStrings == null)
                {
                    Debug.LogError("[PROBE] biomePathStrings is null!");
                    return;
                }

                Debug.Log("[PROBE] biomePathStrings.Length = " + biomePathStrings.Length);
                for (int i = 0; i < biomePathStrings.Length; i++)
                {
                    Debug.Log("[PROBE] biomePathStrings[" + i + "] = \"" + biomePathStrings[i] + "\"");
                }

                // ============================================================
                // 第一部分：探测每个生物群系的商店 prefab
                // ============================================================
                Debug.Log("[PROBE] ============================================================");
                Debug.Log("[PROBE] === SHOP PREFABS PER BIOME ===");
                Debug.Log("[PROBE] ============================================================");

                for (int biomeIdx = 0; biomeIdx < biomePathStrings.Length; biomeIdx++)
                {
                    string path = biomePathStrings[biomeIdx];
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.Log("[PROBE] biome=" + biomeIdx + " path is null/empty, skipping");
                        continue;
                    }

                    var biomeData = Resources.Load<BiomeData>(path);
                    if (biomeData == null)
                    {
                        Debug.Log("[PROBE] biome=" + biomeIdx + " BiomeData load FAILED for path=" + path);
                        continue;
                    }

                    var assets = biomeData.biomeSpecificAssets;
                    if (assets == null)
                    {
                        Debug.Log("[PROBE] biome=" + biomeIdx + " biomeSpecificAssets is null");
                        continue;
                    }

                    var uniqueShops = assets.uniqueShopPrefabs;
                    if (uniqueShops == null || uniqueShops.Count == 0)
                    {
                        Debug.Log("[PROBE] biome=" + biomeIdx + " uniqueShopPrefabs is empty");
                        continue;
                    }

                    Debug.Log("[PROBE] --- biome=" + biomeIdx + " (" + path + ") shops=" + uniqueShops.Count + " ---");

                    for (int shopIdx = 0; shopIdx < uniqueShops.Count; shopIdx++)
                    {
                        ShopTag shopTag = uniqueShops[shopIdx];
                        if (shopTag == null)
                        {
                            Debug.Log("[PROBE]   shop[" + shopIdx + "] = NULL");
                            continue;
                        }

                        ProbeShopPrefab(biomeIdx, shopIdx, shopTag);
                    }
                }

                // ============================================================
                // 第二部分：探测每个生物群系的角色 prefab
                // ============================================================
                Debug.Log("[PROBE] ============================================================");
                Debug.Log("[PROBE] === CHARACTER PREFABS PER BIOME ===");
                Debug.Log("[PROBE] ============================================================");

                for (int biomeIdx = 0; biomeIdx < biomePathStrings.Length; biomeIdx++)
                {
                    string path = biomePathStrings[biomeIdx];
                    if (string.IsNullOrEmpty(path)) continue;

                    var biomeData = Resources.Load<BiomeData>(path);
                    if (biomeData == null || biomeData.biomeSpecificAssets == null) continue;

                    var uniqueChars = biomeData.biomeSpecificAssets.uniqueCharacters;
                    if (uniqueChars == null || uniqueChars.Count == 0)
                    {
                        Debug.Log("[PROBE] biome=" + biomeIdx + " uniqueCharacters is empty");
                        continue;
                    }

                    Debug.Log("[PROBE] --- biome=" + biomeIdx + " characters=" + uniqueChars.Count + " ---");

                    for (int charIdx = 0; charIdx < uniqueChars.Count; charIdx++)
                    {
                        Character ch = uniqueChars[charIdx];
                        if (ch == null)
                        {
                            Debug.Log("[PROBE]   char[" + charIdx + "] = NULL");
                            continue;
                        }

                        string tag = "NULL";
                        string typeName = "NULL";
                        try { tag = ch.tag; } catch { }
                        try { typeName = ch.GetType().Name; } catch { }

                        Debug.Log("[PROBE]   char[" + charIdx + "] tag=\"" + tag + "\" type=" + typeName + " name=" + ch.gameObject.name);
                    }
                }

                // ============================================================
                // 第三部分：dump 当前 shopTypePrefabPairs 字典
                // ============================================================
                Debug.Log("[PROBE] ============================================================");
                Debug.Log("[PROBE] === shopTypePrefabPairs AFTER initialization ===");
                Debug.Log("[PROBE] ============================================================");

                var pairsField = typeof(ShopPlanner).GetField("shopTypePrefabPairs",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (pairsField != null)
                {
                    var pairs = pairsField.GetValue(__instance) as Dictionary<PayableShop.ShopType, GameObject>;
                    if (pairs != null)
                    {
                        foreach (var kvp in pairs)
                        {
                            string prefabName = kvp.Value != null ? kvp.Value.name : "NULL";
                            Debug.Log("[PROBE]   " + kvp.Key + " (" + (int)kvp.Key + ") -> " + prefabName);
                        }
                        Debug.Log("[PROBE] Total entries: " + pairs.Count);
                    }
                }
                else
                {
                    Debug.LogError("[PROBE] shopTypePrefabPairs field not found!");
                }

                // ============================================================
                // 第四部分：全局搜索 BerserkerTool prefab
                // ============================================================
                Debug.Log("[PROBE] ============================================================");
                Debug.Log("[PROBE] === GLOBAL DroppableTool SEARCH ===");
                Debug.Log("[PROBE] ============================================================");

                var allTools = Resources.LoadAll<DroppableTool>("");
                Debug.Log("[PROBE] Total DroppableTool prefabs in Resources: " + allTools.Length);
                foreach (var tool in allTools)
                {
                    string toolTag = "NULL";
                    try { toolTag = tool.tag; } catch { }
                    Debug.Log("[PROBE]   DroppableTool: name=\"" + tool.gameObject.name + "\" tag=\"" + toolTag + "\"");
                }

                // ============================================================
                // 第五部分：探测每个生物群系的 uniquePrefabMasterCopies
                // ============================================================
                Debug.Log("[PROBE] ============================================================");
                Debug.Log("[PROBE] === uniquePrefabMasterCopies PER BIOME ===");
                Debug.Log("[PROBE] ============================================================");

                for (int biomeIdx = 0; biomeIdx < biomePathStrings.Length; biomeIdx++)
                {
                    string path = biomePathStrings[biomeIdx];
                    if (string.IsNullOrEmpty(path)) continue;

                    var biomeData = Resources.Load<BiomeData>(path);
                    if (biomeData == null || biomeData.biomeSpecificAssets == null) continue;

                    var masterCopies = biomeData.biomeSpecificAssets.uniquePrefabMasterCopies;
                    if (masterCopies == null || masterCopies.Count == 0)
                    {
                        Debug.Log("[PROBE] biome=" + biomeIdx + " uniquePrefabMasterCopies is empty");
                        continue;
                    }

                    Debug.Log("[PROBE] --- biome=" + biomeIdx + " masterCopies=" + masterCopies.Count + " ---");
                    for (int mcIdx = 0; mcIdx < masterCopies.Count; mcIdx++)
                    {
                        var mc = masterCopies[mcIdx];
                        if (mc == null) { Debug.Log("[PROBE]   masterCopy[" + mcIdx + "] = NULL"); continue; }

                        string mcTag = "NULL";
                        try { mcTag = mc.tag; } catch { }

                        // 检测组件
                        bool isDroppableTool = mc.GetComponent<DroppableTool>() != null;
                        bool isPayableShop = mc.GetComponent<PayableShop>() != null;
                        bool isShopTag = mc.GetComponent<ShopTag>() != null;

                        Debug.Log("[PROBE]   masterCopy[" + mcIdx + "] prefabID=" + mc.prefabID
                            + " name=\"" + mc.gameObject.name + "\" tag=\"" + mcTag + "\""
                            + (isDroppableTool ? " [DroppableTool]" : "")
                            + (isPayableShop ? " [PayableShop]" : "")
                            + (isShopTag ? " [ShopTag]" : ""));
                    }
                }
                Debug.Log("[PROBE] === PROBE COMPLETE — safe to remove Patch_Probe ===");
                Debug.Log("[PROBE] ============================================================");
            }
            catch (Exception e)
            {
                Debug.LogError("[PROBE] Exception: " + e.ToString());
            }
        }

        /// <summary>
        /// 探测单个商店 prefab 的全部信息。
        /// </summary>
        private static void ProbeShopPrefab(int biomeIdx, int shopIdx, ShopTag shopTag)
        {
            string goName = shopTag.gameObject.name;
            PayableShop.ShopType shopType = shopTag.type;
            int shopTypeInt = (int)shopType;

            // 组件类型检测
            string componentInfo = "";
            PayableSidedShop sidedShop = shopTag.GetComponent<PayableSidedShop>();
            PayableWorkshop workshop = shopTag.GetComponent<PayableWorkshop>();
            PayableShop payableShop = shopTag.GetComponent<PayableShop>();

            if (sidedShop != null)
            {
                componentInfo = "PayableSidedShop(shopType=" + sidedShop.shopType + ")";
            }
            else if (workshop != null)
            {
                componentInfo = "PayableWorkshop";
            }
            else if (payableShop != null)
            {
                componentInfo = "PayableShop";
            }
            else
            {
                componentInfo = "NO PayableShop component!";
            }

            Debug.Log("[PROBE]   shop[" + shopIdx + "] biome=" + biomeIdx
                + " ShopType=" + shopType + " (" + shopTypeInt + ")"
                + " name=\"" + goName + "\""
                + " comp=[" + componentInfo + "]");

            // 探测 PayableShop（含子类 PayableSidedShop）卖什么
            // 注意：PayableWorkshop 不继承 PayableShop，跳过 itemPrefab 探测
            PayableShop shop = payableShop;
            if (shop == null && sidedShop != null) shop = sidedShop as PayableShop;

            if (shop != null && shop.itemPrefab != null)
            {
                Droppable item = shop.itemPrefab;
                string itemTag = "NULL";
                string itemType = item.GetType().Name;
                string itemName = item.gameObject.name;
                try { itemTag = item.tag; } catch { }

                bool isDroppableTool = item is DroppableTool;

                Debug.Log("[PROBE]       -> sells: name=\"" + itemName + "\""
                    + " tag=\"" + itemTag + "\""
                    + " type=" + itemType
                    + (isDroppableTool ? " [DroppableTool]" : ""));
            }
            else if (shop != null)
            {
                Debug.Log("[PROBE]       -> itemPrefab is NULL");
            }
        }
    }
}
