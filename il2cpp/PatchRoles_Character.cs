using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 希腊乞丐变北欧平民：Character.Promote(string, IUnitController) 在希腊世界把
/// Beggar 提升为 Peasant 时，替换为北境 WarriorPeasant prefab（带 1.2 缩放 + 配色同步）。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - Character.Promote(string newTag, IUnitController unitController = null) : Character —— 存在
///   （Promote 有两个重载：Promote(DroppableTool,...) 与 Promote(string,...)，patch 用参数类型消歧）
/// - Character.skinColor/outfitColor : Color —— 【差异】2.1.0 私有 _skinColor/_outfitColor，2.4.0 公开属性，直接赋值免反射
/// - Character.PickOutfitColor(string, Il2CppSystem.Nullable&lt;Color&gt; overrideColor = null) —— 存在（省略第二参）
/// - Character.UpgradeTransitionFX()/SendSyncColours() : void —— 存在
/// - Character.spawnSound : AudioEmitter —— 存在；AudioEmitter.Play(Vector3, bool, bool, bool) —— 存在
/// - IUnitController.ReplaceControlledUnit(GameObject) : void —— 存在
/// - Persistent.DontPersistInstance(bool includeChildren = false, List&lt;GameObject&gt; excludeObjects = null) —— 存在
/// - Pool.Despawn(GameObject clone, bool syncDespawn = true) —— 存在
/// - BiomeSwapData.prefabSwapPool : List&lt;PrefabSwapData&gt;（Il2CppSystem）—— 存在
/// - BiomeSwapData.PrefabSwapData.original/swap : GameObject —— 存在
/// </summary>
[HarmonyPatch(typeof(Character))]
public static class Character_Promote_Patch
{
    private const int GREECE_BIOME_INDEX = 5;
    private const int NORSE_BIOME_INDEX = 3;
    private static Character cachedNorseWarriorPeasant = null;

    [HarmonyPatch(nameof(Character.Promote), new[] { typeof(string), typeof(IUnitController) })]
    [HarmonyPrefix]
    public static bool Promote_Prefix(Character __instance, string newTag, IUnitController unitController)
    {
        if (!ModConfig.Enabled.Value) return true;
        if (newTag != "Peasant") return true;

        try
        {
            if (BiomeHolder.Inst.BiomeIndex != GREECE_BIOME_INDEX) return true;
            if (!__instance.CompareTag("Beggar")) return true;

            Character norseWarriorPeasant = GetNorseWarriorPeasant();
            if (norseWarriorPeasant == null) return true;

            Vector3 position = __instance.transform.position;
            Color skinColor = __instance.skinColor;
            Color outfitColor = __instance.outfitColor;

            GameObject newGO = UnityEngine.Object.Instantiate(norseWarriorPeasant.gameObject, position, Quaternion.identity);
            if (newGO == null) return true;
            Character newChar = newGO.GetComponent<Character>();
            if (newChar == null) { UnityEngine.Object.Destroy(newGO); return true; }

            newChar.transform.parent = __instance.transform.parent;
            newChar.transform.position = position;
            // 北境居民缩放：y=1.2（x 是 Mover 朝向符号不能动）
            newChar.transform.localScale = new Vector3(1f, 1.2f, 1f);

            newChar.skinColor = skinColor;
            newChar.outfitColor = outfitColor;

            newChar.PickOutfitColor("Peasant");
            newChar.UpgradeTransitionFX();
            newChar.spawnSound.Play(position, false, false, false);

            if (NetworkBigBoss.HasWorldAuth && NetworkBigBoss.IsOnline && NetworkBigBoss.IsClientPresent)
                newChar.SendSyncColours();

            if (unitController != null)
                unitController.ReplaceControlledUnit(newChar.gameObject);

            Persistent persistent = __instance.GetComponent<Persistent>();
            if (persistent != null)
                persistent.DontPersistInstance(false);

            Pool.Despawn(__instance.gameObject, true);
            return false;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return true;
        }
    }

    private static Character GetNorseWarriorPeasant()
    {
        if (cachedNorseWarriorPeasant != null) return cachedNorseWarriorPeasant;

        try
        {
            var biomePathStrings = BiomeHolder.Inst.biomePathStrings;
            if (biomePathStrings == null || NORSE_BIOME_INDEX >= biomePathStrings.Length) return null;

            string norsePath = biomePathStrings[NORSE_BIOME_INDEX];
            if (string.IsNullOrEmpty(norsePath)) return null;

            var norseBiomeData = Resources.Load<BiomeData>(norsePath);
            if (norseBiomeData == null || norseBiomeData.swapData == null) return null;

            Character basePeasant = Managers.Inst.holder.GetCharacterByTag("Peasant");
            if (basePeasant == null) return null;

            var prefabSwapPool = norseBiomeData.swapData.prefabSwapPool;
            if (prefabSwapPool == null) return null;

            GameObject basePeasantGO = basePeasant.gameObject;
            for (int i = 0; i < prefabSwapPool.Count; i++)
            {
                var swap = prefabSwapPool[i];
                if (swap.original == null || swap.swap == null) continue;
                if (swap.original == basePeasantGO)
                {
                    cachedNorseWarriorPeasant = swap.swap.GetComponent<Character>();
                    return cachedNorseWarriorPeasant;
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
