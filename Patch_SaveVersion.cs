using System;
using UnityEngine;
using Harmony;

namespace MyMod
{
    /// <summary>
    /// 存档版本容忍（2026-08-13 用户反馈 Mono 2.1.0 读不了 Steam 2.4.0 升级后的 v16 存档）。
    ///
    /// 背景：所有版本共用 %USERPROFILE%\AppData\LocalLow\noio\KingdomTwoCrowns\Release\global-v35
    /// （GZip JSON）。2.4.0 启动即把存档升级到 serializedSaveDataVersion=16，2.1.0 的
    /// GlobalSaveData._TryLoad 版本检查 `> 13` 直接拒绝（yield break）→ 旧版进不去。
    ///
    /// 修复：patch JsonUtility.FromJsonOverwrite 的 prefix（ref string json）——反序列化前
    /// 把版本字段 16 降为 13。这是版本检查（`_loaded.serializedSaveDataVersion > 13`）的
    /// **唯一数据入口**，改这里即绕过检查；其他 JSON 字段由 FromJsonOverwrite 天然容错
    /// （Unity 忽略未知字段）。保存走 ToJson 不受影响——Mono 版保存后档变 13，
    /// Steam 版下次启动官方升级回 16（官方支持方向，无损拉锯）。
    ///
    /// 副作用评估：FromJsonOverwrite 全游戏通用，但只有存档 JSON 含该版本字段，
    /// 其余调用不受影响；仅当 mod 启用（Main.Enabled）时生效。
    /// </summary>
    public static class Patch_SaveVersion
    {
        public static void Register(HarmonyInstance harmony)
        {
            var method = typeof(JsonUtility).GetMethod("FromJsonOverwrite",
                new[] { typeof(string), typeof(object) });
            if (method != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_SaveVersion).GetMethod("FromJsonOverwrite_Prefix"));
                harmony.Patch(method, prefix, null);
                Debug.Log("[MyMod] Patched JsonUtility.FromJsonOverwrite (save version 16->13 tolerance)");
            }
            else
            {
                Debug.LogError("[MyMod] JsonUtility.FromJsonOverwrite not found!");
            }
        }

        public static void FromJsonOverwrite_Prefix(ref string json)
        {
            if (!Main.Enabled) return;
            try
            {
                if (json == null || !json.Contains("\"serializedSaveDataVersion\":16"))
                {
                    return;
                }
                json = json.Replace("\"serializedSaveDataVersion\":16,", "\"serializedSaveDataVersion\":13,");
                Debug.Log("[MyMod] Save version tolerance: 16 -> 13 applied");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] SaveVersion error: " + e.Message);
            }
        }
    }
}
