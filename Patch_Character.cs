using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    public static class Patch_Character
    {
        private const int GREECE_BIOME_INDEX = 5;
        private const int NORSE_BIOME_INDEX = 3;
        private static Character cachedNorseWarriorPeasant = null;

        public static void Register(HarmonyInstance harmony)
        {
            var characterType = typeof(Character);
            var promoteMethod = characterType.GetMethod("Promote", new Type[] { typeof(string), typeof(IUnitController) });
            if (promoteMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Character).GetMethod("Promote_Prefix"));
                harmony.Patch(promoteMethod, prefix, null);
                Debug.Log("[MyMod] Patched Character.Promote");
            }
        }

        public static bool Promote_Prefix(Character __instance, string newTag, IUnitController unitController)
        {
            if (!Main.Enabled) return true;
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
                // 北境居民缩放：y=1.2（x 是 Mover 朝向符号不能动，只体现在 y），
                // 后续 WarriorPeasant_OnEnable_Postfix 会登记 y 轴守护
                newChar.transform.localScale = new Vector3(1f, 1.2f, 1f);

                var skinColorField = typeof(Character).GetField("_skinColor", BindingFlags.NonPublic | BindingFlags.Instance);
                var outfitColorField = typeof(Character).GetField("_outfitColor", BindingFlags.NonPublic | BindingFlags.Instance);
                if (skinColorField != null) skinColorField.SetValue(newChar, skinColor);
                if (outfitColorField != null) outfitColorField.SetValue(newChar, outfitColor);

                newChar.PickOutfitColor("Peasant", null);
                newChar.UpgradeTransitionFX();
                newChar.spawnSound.Play(position, false, false, false);

                if (NetworkBigBoss.HasWorldAuth && NetworkBigBoss.IsOnline && NetworkBigBoss.IsClientPresent)
                    newChar.SendSyncColours();

                if (unitController != null)
                    unitController.ReplaceControlledUnit(newChar.gameObject);

                Persistent persistent = __instance.GetComponent<Persistent>();
                if (persistent != null)
                    persistent.DontPersistInstance(false, null);

                Pool.Despawn(__instance.gameObject, true);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Promote_Prefix: " + e.Message);
                return true;
            }
        }

        private static Character GetNorseWarriorPeasant()
        {
            if (cachedNorseWarriorPeasant != null) return cachedNorseWarriorPeasant;

            try
            {
                var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings", BindingFlags.Public | BindingFlags.Instance);
                if (biomePathStringsField == null) return null;

                var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
                if (biomePathStrings == null || NORSE_BIOME_INDEX >= biomePathStrings.Length) return null;

                string norsePath = biomePathStrings[NORSE_BIOME_INDEX];
                if (string.IsNullOrEmpty(norsePath)) return null;

                var norseBiomeData = Resources.Load<BiomeData>(norsePath);
                if (norseBiomeData == null || norseBiomeData.swapData == null) return null;

                Character basePeasant = SingletonMonoBehaviour<Managers>.Inst.holder.GetCharacterByTag("Peasant");
                if (basePeasant == null) return null;

                var prefabSwapField = typeof(BiomeSwapData).GetField("prefabSwapPool", BindingFlags.Public | BindingFlags.Instance);
                if (prefabSwapField == null) return null;

                var prefabSwapPool = prefabSwapField.GetValue(norseBiomeData.swapData) as List<BiomeSwapData.PrefabSwapData>;
                if (prefabSwapPool == null) return null;

                GameObject basePeasantGO = basePeasant.gameObject;
                foreach (var swap in prefabSwapPool)
                {
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
                Debug.LogError("[MyMod] Error getting Norse WarriorPeasant: " + e.Message);
            }

            return null;
        }
    }
}