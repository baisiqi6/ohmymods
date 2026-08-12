using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    public static class Patch_Kingdom
    {
        private static readonly HashSet<int> spawnedKingdomInstances = new HashSet<int>();

        public static void Register(HarmonyInstance harmony)
        {
            var kingdomType = typeof(Kingdom);
            var kingdomMethod = kingdomType.GetMethod("OnLevelLoaded", BindingFlags.Public | BindingFlags.Instance);
            if (kingdomMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Kingdom).GetMethod("Postfix"));
                harmony.Patch(kingdomMethod, null, postfix);
                Debug.Log("[MyMod] Patched Kingdom.OnLevelLoaded");
            }

            var kingdomInitMethod = kingdomType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance);
            if (kingdomInitMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Kingdom).GetMethod("Postfix"));
                harmony.Patch(kingdomInitMethod, null, postfix);
                Debug.Log("[MyMod] Patched Kingdom.Init");
            }
        }

        public static void Postfix(Kingdom __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                // minKingdomExtents 修改已回退（2026-08-12 用户澄清 + 源码核实）：
                // 它只是王国初始安全区边界（场景序列化值），与岛宽度无关。
                // 岛宽度由 Patch_Level（LevelConfig.minLevelWidth × multiplier）控制，
                // 边界由玩家建墙自然扩展（Kingdom.cs:2335 border = outerWall 位置）。
                // 此前改 extents 既有存档污染（旧档放大值无法区分场景值），方向错误。

                int kingdomId = __instance.GetHashCode();
                __instance.StartCoroutine(DelayedBerserkerSpawn(__instance, kingdomId));
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Kingdom patch: " + e.ToString());
            }
        }

        private static System.Collections.IEnumerator DelayedBerserkerSpawn(Kingdom kingdom, int kingdomId)
        {
            yield return null;
            yield return null;
            yield return null;

            // 狂战士 hack 已退役：商店系统原生生成（Patch_Castle + Patch_SidedShop）
            // 忍者 hack 已退役：Patch_Castle 原生入队忍者商店
            // 工人缩放兜底已清理：OnEnable + y 轴守护（Patch_Worker）完全接管，
            // 不再需要每帧 FindObjectsOfType 扫描。此处只保留猫生成。
            if (spawnedKingdomInstances.Contains(kingdomId))
            {
                yield break;
            }

            SpawnCatsInGreece(kingdom, kingdomId);
            spawnedKingdomInstances.Add(kingdomId);
        }

        // === 狂战士 hack 已退役（商店系统原生生成），保留代码备查 ===
        /*
        private static bool SpawnBerserkersInGreece(Kingdom kingdom, int kingdomId)
        {
            if (BiomeHolder.Inst.BiomeIndex != 5) return false;

            var existingBerserkers = SingletonMonoBehaviour<Managers>.Inst.kingdom.Berserkers;
            existingBerserkers.RemoveAll(b => b == null);

            Worker[] workers = UnityEngine.Object.FindObjectsOfType<Worker>();

            int maxBerserkers = 12;
            int maxLeaders = 12;
            int maxTotal = maxBerserkers + maxLeaders;
            int remainingSlots = maxTotal - existingBerserkers.Count;

            if (remainingSlots <= 0) return false;

            int berserkerCount = 0;
            int leaderCount = 0;

            foreach (Worker worker in workers)
            {
                if (worker == null || worker.GetComponent<Character>() == null) continue;
                if (berserkerCount + leaderCount >= remainingSlots) break;

                Character character = worker.GetComponent<Character>();
                if (character.CompareTag("Berserker") || character.CompareTag("BerserkerLeader")) continue;

                if (berserkerCount < maxBerserkers && berserkerCount + leaderCount < remainingSlots)
                {
                    if (ReplaceWithBerserker(worker, false)) berserkerCount++;
                }
                else if (leaderCount < maxLeaders && berserkerCount + leaderCount < remainingSlots)
                {
                    if (ReplaceWithBerserker(worker, true)) leaderCount++;
                }
            }

            Debug.Log("[MyMod] Spawned " + berserkerCount + " Berserkers and " + leaderCount + " BerserkerLeaders in Greece biome");
            return berserkerCount > 0 || leaderCount > 0;
        }

        private static bool ReplaceWithBerserker(Worker worker, bool isLeader)
        {
            try
            {
                Vector3 position = worker.transform.position;
                Character berserkerCharPrefab = null;
                Holder holder = UnityEngine.Object.FindObjectOfType<Holder>();
                if (holder != null)
                {
                    string tag = isLeader ? "BerserkerLeader" : "Berserker";
                    holder.tagCharacterPairs.TryGetValue(tag, out berserkerCharPrefab);
                }

                if (berserkerCharPrefab == null) return false;

                worker.gameObject.SetActive(false);

                GameObject berserkerGO = UnityEngine.Object.Instantiate(berserkerCharPrefab.gameObject, position, Quaternion.identity);
                berserkerGO.SetActive(true);
                Berserker berserker = berserkerGO.GetComponent<Berserker>();

                if (berserker != null)
                {
                    Vector3 newScale = new Vector3(1.3f, 1.3f, 1f);
                    berserkerGO.transform.localScale = newScale;
                    SpriteRenderer[] spriteRenderers = berserkerGO.GetComponentsInChildren<SpriteRenderer>();
                    foreach (var sr in spriteRenderers) sr.transform.localScale = newScale;

                    if (isLeader)
                    {
                        var isLeaderField = typeof(Berserker).GetField("isLeader", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (isLeaderField != null) isLeaderField.SetValue(berserker, true);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MyMod] Error replacing worker with Berserker: " + ex.Message);
                return false;
            }
        }
        */ // === 狂战士 hack 结束 ===


        private static void SpawnCatsInGreece(Kingdom kingdom, int kingdomId)
        {
            if (BiomeHolder.Inst.BiomeIndex != 5) return;

            Cat catPrefab = GetNorseCatPrefab();
            if (catPrefab == null) return;

            Farmhouse[] farmhouses = UnityEngine.Object.FindObjectsOfType<Farmhouse>();
            int catCount = 0;

            foreach (Farmhouse farmhouse in farmhouses)
            {
                if (farmhouse == null) continue;

                int existingCats = 0;
                foreach (Cat existingCat in kingdom.cats)
                {
                    if (existingCat != null && existingCat.domesticated)
                    {
                        var existingFarmHouseField = typeof(Cat).GetField("farmHouse", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (existingFarmHouseField != null)
                        {
                            Farmhouse existingFarm = existingFarmHouseField.GetValue(existingCat) as Farmhouse;
                            if (existingFarm == farmhouse) existingCats++;
                        }
                    }
                }

                int catsToSpawn = 3 - existingCats;
                if (catsToSpawn <= 0) continue;

                for (int c = 0; c < catsToSpawn; c++)
                {
                    Vector3 position = farmhouse.transform.position;
                    position.x += UnityEngine.Random.Range(-4f, 4f);
                    position.y += 0.5f;

                    GameObject newCatGO = UnityEngine.Object.Instantiate(catPrefab.gameObject, position, Quaternion.identity);
                    newCatGO.transform.SetParent(SingletonMonoBehaviour<Managers>.Inst.world.gameLayer);
                    newCatGO.SetActive(true);
                    Cat newCat = newCatGO.GetComponent<Cat>();

                    if (newCat != null)
                    {
                        var domesticatedField = typeof(Cat).GetField("<domesticated>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (domesticatedField != null)
                        {
                            domesticatedField.SetValue(newCat, true);
                        }
                        else
                        {
                            var saveData = new Cat.CatSaveStatusData();
                            saveData.domesticated = true;
                            saveData.color = Color.white;
                            newCat.SetFromSavedState(saveData);
                        }

                        var farmHouseField = typeof(Cat).GetField("farmHouse", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (farmHouseField != null) farmHouseField.SetValue(newCat, farmhouse);

                        catCount++;
                    }
                }
            }

            Debug.Log("[MyMod] Spawned " + catCount + " Cats in Greece biome");
        }

        private static Cat GetNorseCatPrefab()
        {
            try
            {
                int norseBiomeIndex = 3;
                var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings", BindingFlags.Public | BindingFlags.Instance);
                if (biomePathStringsField == null) return null;

                var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
                if (biomePathStrings == null || norseBiomeIndex >= biomePathStrings.Length) return null;

                string norsePath = biomePathStrings[norseBiomeIndex];
                if (string.IsNullOrEmpty(norsePath)) return null;

                var norseBiomeData = Resources.Load<BiomeData>(norsePath);
                if (norseBiomeData == null) return null;

                if (norseBiomeData.biomeSpecificAssets != null && norseBiomeData.biomeSpecificAssets.uniqueCharacters != null)
                {
                    foreach (var character in norseBiomeData.biomeSpecificAssets.uniqueCharacters)
                    {
                        if (character != null && character.CompareTag("Cat"))
                        {
                            Cat cat = character.GetComponent<Cat>();
                            if (cat != null) return cat;
                        }
                    }
                }

                if (norseBiomeData.swapData != null)
                {
                    var prefabSwapField = typeof(BiomeSwapData).GetField("prefabSwapPool", BindingFlags.Public | BindingFlags.Instance);
                    if (prefabSwapField != null)
                    {
                        var prefabSwapPool = prefabSwapField.GetValue(norseBiomeData.swapData) as List<BiomeSwapData.PrefabSwapData>;
                        if (prefabSwapPool != null)
                        {
                            foreach (var swap in prefabSwapPool)
                            {
                                if (swap.swap != null)
                                {
                                    Cat cat = swap.swap.GetComponent<Cat>();
                                    if (cat != null) return cat;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error getting Norse Cat prefab: " + e.Message);
            }
            return null;
        }
    }
}
