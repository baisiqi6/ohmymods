using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界角色通用：Holder.InitializeTagCharacterPairs 之后，把各 biome 的
/// uniqueCharacters 补登记进 tagCharacterPairs（投币招募/工具转化/读档恢复都走
/// GetCharacterByTag）。希腊世界(biome=5)再把 Worker/Peasant 替换为北境 prefab。
///
/// 2.4.0 签名验证（interop Assembly-CSharp.dll）：
/// - Holder.InitializeTagCharacterPairs() : void —— 存在，无参，与 2.1.0 一致
/// - Holder.tagCharacterPairs : Dictionary&lt;string,Character&gt;（Il2CppSystem）—— 存在
/// - Holder.GetCharacterByTag(string) : Character —— 存在
/// - BiomeHolder.biomePathStrings : Il2CppStringArray（原 string[]，IL2CPP 数组壳）—— 类型变化
/// - BiomeHolder.Inst : BiomeHolder（静态）/ BiomeIndex : int —— 存在
/// - BiomeData.biomeSpecificAssets : BiomeSpecificAssets —— 存在
/// - BiomeSpecificAssets.uniqueCharacters : List&lt;Character&gt;（Il2CppSystem）—— 存在
/// - Resources.LoadAll&lt;Character&gt;("") 返回 Il2CppArrayBase&lt;Character&gt;（原 Character[]）—— 类型变化
/// </summary>
[HarmonyPatch(typeof(Holder))]
public static class Holder_InitializeTagCharacterPairs_Patch
{
    [HarmonyPatch(nameof(Holder.InitializeTagCharacterPairs))]
    [HarmonyPostfix]
    public static void Postfix(Holder __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        try
        {
            var biomePathStrings = BiomeHolder.Inst.biomePathStrings;
            if (biomePathStrings == null) return;

            int addedCount = 0;

            for (int i = 0; i < biomePathStrings.Length; i++)
            {
                string path = biomePathStrings[i];
                if (string.IsNullOrEmpty(path)) continue;

                var biomeData = Resources.Load<BiomeData>(path);
                if (biomeData == null) continue;

                var assets = biomeData.biomeSpecificAssets;
                if (assets == null) continue;

                var uniqueCharacters = assets.uniqueCharacters;
                if (uniqueCharacters == null) continue;

                for (int j = 0; j < uniqueCharacters.Count; j++)
                {
                    var character = uniqueCharacters[j];
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

            // 角色就绪后补注册 mod 角色池（Init_Prefix 的 ReRegisterModPools 可能早于
            // Holder 初始化——此时 tagCharacterPairs 才完整，EnsurePoolForCharacter 才能命中）。
            PatchRoles_Castle.ReRegisterModPools();

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo($"[Roles] Added {addedCount} characters to Holder");

            // 希腊世界：把工匠(Worker)和居民(Peasant)替换为北境版本
            if (BiomeHolder.Inst != null && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex)
            {
                ReplaceCharacterWithNorselands(__instance, "Worker", "Worker_norselands");
                ReplaceCharacterWithNorselands(__instance, "Peasant", "Peasant_norselands");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 把 Holder.tagCharacterPairs[tag] 替换为北境 prefab。2.4.0 用
    /// Resources.LoadAll（返回 Il2CppArrayBase&lt;Character&gt;）扫子目录资源。
    /// </summary>
    private static void ReplaceCharacterWithNorselands(Holder holder, string tag, string prefabName)
    {
        try
        {
            var allChars = Resources.LoadAll<Character>("");
            Character norselandsChar = null;
            for (int i = 0; i < allChars.Length; i++)
            {
                var c = allChars[i];
                if (c != null && c.gameObject.name == prefabName)
                {
                    norselandsChar = c;
                    break;
                }
            }

            if (norselandsChar == null)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError($"[Roles] {prefabName} not found in Resources!");
                return;
            }

            if (norselandsChar.gameObject.tag != tag)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError($"[Roles] {prefabName} tag is '{norselandsChar.gameObject.tag}' not '{tag}'!");
                return;
            }

            holder.tagCharacterPairs[tag] = norselandsChar;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo($"[Roles] Greece: {tag} replaced with {prefabName}");

            // 注册 sync 池：希腊世界没有北境角色的池，转化/招募会崩
            PatchRoles_Castle.EnsurePoolForCharacter(tag);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
