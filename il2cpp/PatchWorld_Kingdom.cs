using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
#if IL2CPP
using BepInEx.Unity.IL2CPP.Utils.Collections;
#endif

namespace KingdomEnhancedMod;

/// <summary>
/// 希腊世界自动生成猫（每农场补足 3 只家养猫）。
///
/// 与 Mono 版 Patch_Kingdom 的差异（忠实迁移"当前生效代码"）：
///   - 狂战士/忍者自动生成已退役：商店系统原生生成（Mono 侧由 Patch_Castle + Patch_SidedShop 接管，
///     本迁移中属其它 worker 域，不在此实现）。Mono 源码里 SpawnBerserkersInGreece 已注释退役。
///   - minKingdomExtents 修改已回退（与岛宽度无关，岛宽度由 PatchWorld_Level 控制）。
///   - 仅保留：OnLevelLoaded / Init 后延迟 3 帧，希腊（BiomeIndex==5）按农场补足猫。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - Kingdom.OnLevelLoaded()       存在 ✓ public override void
///   - Kingdom.Init()                存在 ✓ public override void
///   - Kingdom.cats                  存在 ✓ public List&lt;Cat&gt; 属性（Mono 同）
///   - BiomeHolder.Inst              存在 ✓ public static 属性
///   - BiomeHolder.BiomeIndex        存在 ✓ public int 属性
///   - BiomeHolder.biomePathStrings  存在 ✓ public Il2CppStringArray（漂移：Mono 为 string[]，仍可索引）
///   - Cat.domesticated              存在 ✓ public bool 属性（私有 setter，只读）
///   - Cat.farmHouse                 存在 ✓ public Farmhouse 字段（漂移：Mono 为私有字段需反射）
///   - Cat.SetFromSavedState(Cat.CatSaveStatusData) 存在 ✓ public（CatSaveStatusData 为嵌套 struct）
///   - BiomeData / BiomeSpecificAssets.uniqueCharacters(List&lt;Character&gt;) /
///     BiomeSwapData.prefabSwapPool(List&lt;PrefabSwapData&gt;) 均存在 ✓ public
///   结论：结构基本一致，仅 biomePathStrings 类型、farmHouse 可见性变化；全部可用 public API 直连，
///   无需 Mono 的反射。GetNorseCatPrefab 依赖 Resources.Load&lt;BiomeData&gt; + 深层资产遍历，
///   无法在本环境运行时验证（见 notes-world.md 待 Operator 决策清单）。
/// </summary>
[HarmonyPatch(typeof(Kingdom))]
public static class PatchWorld_Kingdom
{
    private static readonly HashSet<int> SpawnedKingdomInstances = new HashSet<int>();

    [HarmonyPatch(nameof(Kingdom.OnLevelLoaded))]
    [HarmonyPostfix]
    public static void OnLevelLoaded_Postfix(Kingdom __instance) => SpawnCats(__instance);

    [HarmonyPatch(nameof(Kingdom.Init))]
    [HarmonyPostfix]
    public static void Init_Postfix(Kingdom __instance) => SpawnCats(__instance);

    private static void SpawnCats(Kingdom __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        try
        {
            int kingdomId = __instance.GetHashCode();
#if IL2CPP
            __instance.StartCoroutine(DelayedCatSpawn(__instance, kingdomId).WrapToIl2Cpp());
#else
            __instance.StartCoroutine(DelayedCatSpawn(__instance, kingdomId));
#endif
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    private static IEnumerator DelayedCatSpawn(Kingdom kingdom, int kingdomId)
    {
        yield return null;
        yield return null;
        yield return null;

        if (SpawnedKingdomInstances.Contains(kingdomId))
        {
            yield break;
        }

        SpawnCatsInGreece(kingdom, kingdomId);
        SpawnedKingdomInstances.Add(kingdomId);
    }

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
                if (existingCat != null && existingCat.domesticated && existingCat.farmHouse == farmhouse)
                {
                    existingCats++;
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
                newCatGO.transform.SetParent(Managers.Inst.world.gameLayer);
                newCatGO.SetActive(true);
                Cat newCat = newCatGO.GetComponent<Cat>();

                if (newCat != null)
                {
                    var saveData = new Cat.CatSaveStatusData();
                    saveData.domesticated = true;
                    saveData.color = Color.white;
                    newCat.SetFromSavedState(saveData);
                    newCat.farmHouse = farmhouse;
                    catCount++;
                }
            }
        }

        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo($"[KingdomEnhancedMod] Spawned {catCount} Cats in Greece biome");
    }

    private static Cat GetNorseCatPrefab()
    {
        try
        {
            const int norseBiomeIndex = 3;
            var biomePathStrings = BiomeHolder.Inst.biomePathStrings;
            if (biomePathStrings == null || norseBiomeIndex >= biomePathStrings.Length) return null;

            string norsePath = biomePathStrings[norseBiomeIndex];
            if (string.IsNullOrEmpty(norsePath)) return null;

            var norseBiomeData = UnityEngine.Resources.Load<BiomeData>(norsePath);
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

            if (norseBiomeData.swapData != null && norseBiomeData.swapData.prefabSwapPool != null)
            {
                foreach (var swap in norseBiomeData.swapData.prefabSwapPool)
                {
                    if (swap.swap != null)
                    {
                        Cat cat = swap.swap.GetComponent<Cat>();
                        if (cat != null) return cat;
                    }
                }
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }

        return null;
    }
}
