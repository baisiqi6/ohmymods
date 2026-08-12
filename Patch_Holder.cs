using System;
using System.Reflection;
using UnityEngine;
using Harmony;

namespace MyMod
{
    public static class Patch_Holder
    {
        public static void Register(HarmonyInstance harmony)
        {
            var holderType = typeof(Holder);
            var initMethod = holderType.GetMethod("InitializeTagCharacterPairs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (initMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Holder).GetMethod("Postfix"));
                harmony.Patch(initMethod, null, postfix);
                Debug.Log("[MyMod] Patched Holder.InitializeTagCharacterPairs");
            }
        }

        public static void Postfix(Holder __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings", BindingFlags.Public | BindingFlags.Instance);
                if (biomePathStringsField == null) return;

                var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
                if (biomePathStrings == null) return;

                int addedCount = 0;

                for (int i = 0; i < biomePathStrings.Length; i++)
                {
                    var path = biomePathStrings[i];
                    if (string.IsNullOrEmpty(path)) continue;

                    var biomeData = Resources.Load<BiomeData>(path);
                    if (biomeData == null) continue;

                    var assets = biomeData.biomeSpecificAssets;
                    if (assets == null) continue;

                    var uniqueCharacters = assets.uniqueCharacters;
                    if (uniqueCharacters == null) continue;

                    foreach (var character in uniqueCharacters)
                    {
                        if (character == null) continue;

                        string tag = character.gameObject.tag;
                        if (string.IsNullOrEmpty(tag)) continue;

                        if (!__instance.tagCharacterPairs.ContainsKey(tag))
                        {
                            __instance.tagCharacterPairs.Add(tag, character);
                            addedCount++;
                        }
                    }
                }

                Debug.Log("[MyMod] Added " + addedCount + " characters to Holder");

                // 希腊世界：把工匠(Worker)和居民(Peasant)替换为北境版本
                // 狂战士系统是北境的，配套工匠用北境的外观/行为更自洽
                if (BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == 5)
                {
                    ReplaceCharacterWithNorselands(__instance, "Worker", "Worker_norselands");
                    ReplaceCharacterWithNorselands(__instance, "Peasant", "Peasant_norselands");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Holder patch: " + e.ToString());
            }
        }

        /// <summary>
        /// 把 Holder.tagCharacterPairs[tag] 替换为北境 prefab。
        /// 所有生成路径（投币招募、工具转化、读档恢复）都走 GetCharacterByTag(tag)。
        /// 注意：Resources.Load 按名字找不到子目录资源，必须用 LoadAll 扫。
        /// </summary>
        private static void ReplaceCharacterWithNorselands(Holder holder, string tag, string prefabName)
        {
            try
            {
                var allChars = Resources.LoadAll<Character>("");
                Character norselandsChar = null;
                foreach (var c in allChars)
                {
                    if (c != null && c.gameObject.name == prefabName)
                    {
                        norselandsChar = c;
                        break;
                    }
                }

                if (norselandsChar == null)
                {
                    Debug.LogError("[MyMod] " + prefabName + " not found in Resources!");
                    return;
                }

                // 确保 tag 正确
                if (norselandsChar.gameObject.tag != tag)
                {
                    Debug.LogError("[MyMod] " + prefabName + " tag is '" + norselandsChar.gameObject.tag + "' not '" + tag + "'!");
                    return;
                }

                holder.tagCharacterPairs[tag] = norselandsChar;
                Debug.Log("[MyMod] Greece: " + tag + " replaced with " + prefabName);

                // 关键：注册 sync 池——希腊世界没有北境角色的池，
                // Pool.Spawn 会失败（招募/转化/降级全崩）或产生非 sync 池（联机 desync）
                Patch_Castle.EnsurePoolForCharacter(tag);
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] ReplaceCharacterWithNorselands(" + tag + ") error: " + e.Message);
            }
        }
}
}
