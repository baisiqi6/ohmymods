using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 原生地图图标来源索引（CONTRACT.md：UIDynamicMapIcon._mapIcons 是序列化字段，
    /// 可直接从 CampaignData.mapLandPrefabs 的 prefab 读取，不需要实例化整座地图）。
    ///
    /// - 按战役懒加载一次（精确 ResourceManager 路径见 evidence/campaign-resource-paths.json）；
    /// - 只读 prefab 引用：GetComponentsInChildren&lt;UIMapIcon&gt;(true) + 各 UIDynamicMapIcon
    ///   的 _mapIcons catalog；仅实际显示的图标才会被 clone；
    /// - (iconType, typeSelection) 双键匹配（同型变体多世界并存，不能只按 typeSelection）；
    /// - 找不到就是原生缺口：返回 null，由调用方记录精确缺口，绝不替换成别的变体。
    /// </summary>
    internal static class MapIconSources
    {
        private static readonly string[] CampaignPaths =
        {
            "data/campaigndata_greece",
            "data/campaigndata_norselands",
            "data/campaigndata_deadlands",
            "data/campaigndata_bamboo",
            "data/campaigndata_oakandbirch",
        };

        private static readonly bool[] Searched = new bool[CampaignPaths.Length];
        private static readonly Dictionary<long, UIMapIcon> Index = new Dictionary<long, UIMapIcon>(64);

        private static long Key(int iconType, int selection) => ((long)iconType << 32) ^ (uint)selection;

        /// <summary>
        /// 取图标 prefab 组件（缺失返回 null）。landScoped = 当前岛（其自身 catalog 优先）。
        /// r6：**原生优先**；只有原生确证缺失且属于用户批准的自有范围（steed 1 + type 3/4/38）时，
        /// 才退到自有 source；其余缺源仍是原生缺口（由调用方记录），绝不替换成别的变体。
        /// </summary>
        internal static UIMapIcon Resolve(UILand landScoped, int iconType, int selection)
        {
            UIMapIcon native = ResolveNativeOnly(landScoped, iconType, selection);
            if (native != null) return native;
            if (MapCustomIconCatalog.IsCustomSteed(iconType, selection))
            {
                return MapCustomIconAssets.Resolve(iconType, selection);
            }
            return null;
        }

        /// <summary>只查原生源（无自有 fallback；自有源的模板查找走这里，避免递归）。</summary>
        internal static UIMapIcon ResolveNativeOnly(UILand landScoped, int iconType, int selection)
        {
            try
            {
                UIMapIcon local = FindLocal(landScoped, iconType, selection);
                if (local != null) return local;

                for (int c = 0; c < CampaignPaths.Length; c++)
                {
                    EnsureSearched(c);
                    if (Index.TryGetValue(Key(iconType, selection), out UIMapIcon icon) && icon != null) return icon;
                }
            }
            catch (Exception e)
            {
                Log.Once("resolve-" + e.GetType().Name, "resolve icon failed type=" + iconType + " sel=" + selection + ": " + e.Message);
            }
            return null;
        }

        /// <summary>原生索引即用即取（模板查找）。</summary>
        internal static UIMapIcon ResolveNativeOnly(int iconType, int selection)
            => ResolveNativeOnly(null, iconType, selection);

        internal static void Reset()
        {
            Array.Clear(Searched, 0, Searched.Length);
            Index.Clear();
            Log.OnceKeys.Clear();
        }

        // ------------------------------------------------------------------

        private static UIMapIcon FindLocal(UILand land, int iconType, int selection)
        {
            if (land == null) return null;
            try
            {
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UIMapIcon> icons = land._mapIcons;
                if (icons != null)
                {
                    for (int i = 0; i < icons.Length; i++)
                    {
                        UIMapIcon icon = icons[i];
                        if (Matches(icon, iconType, selection)) return icon;
                    }
                }

                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UIDynamicMapIcon> hosts =
                    land._dynamicMapIcons;
                if (hosts != null)
                {
                    for (int i = 0; i < hosts.Length; i++)
                    {
                        UIDynamicMapIcon host = hosts[i];
                        if (host == null) continue;
                        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UIMapIcon> catalog = host._mapIcons;
                        if (catalog == null) continue;
                        for (int j = 0; j < catalog.Length; j++)
                        {
                            UIMapIcon icon = catalog[j];
                            if (Matches(icon, iconType, selection)) return icon;
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        private static bool Matches(UIMapIcon icon, int iconType, int selection)
        {
            if (icon == null) return false;
            try
            {
                return (int)icon.iconType == iconType && icon.typeSelection == selection;
            }
            catch (Exception) { return false; }
        }

        private static void EnsureSearched(int campaign)
        {
            if (Searched[campaign]) return;
            Searched[campaign] = true;
            try
            {
                CampaignData data = Resources.Load<CampaignData>(CampaignPaths[campaign]);
                if (data == null)
                {
                    Log.Once("campaign-missing-" + CampaignPaths[campaign],
                        "campaign asset not found: " + CampaignPaths[campaign] + " (icons from this world unavailable)");
                    return;
                }

                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> prefabs = data.mapLandPrefabs;
                if (prefabs == null) return;
                for (int i = 0; i < prefabs.Length; i++)
                {
                    GameObject prefab = prefabs[i];
                    if (prefab == null) continue;
                    try
                    {
                        Harvest(prefab);
                    }
                    catch (Exception e)
                    {
                        Log.Once("scan-" + campaign + "-" + i, "scan prefab failed " + prefab.name + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Once("campaign-" + campaign + "-" + e.GetType().Name,
                    "campaign scan failed " + CampaignPaths[campaign] + ": " + e.Message);
            }
        }

        private static void Harvest(GameObject prefab)
        {
            UIMapIcon[] icons = prefab.GetComponentsInChildren<UIMapIcon>(true);
            if (icons != null)
            {
                for (int i = 0; i < icons.Length; i++) Add(icons[i]);
            }

            UIDynamicMapIcon[] hosts = prefab.GetComponentsInChildren<UIDynamicMapIcon>(true);
            if (hosts != null)
            {
                for (int i = 0; i < hosts.Length; i++)
                {
                    UIDynamicMapIcon host = hosts[i];
                    if (host == null) continue;
                    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UIMapIcon> catalog = host._mapIcons;
                    if (catalog == null) continue;
                    for (int j = 0; j < catalog.Length; j++) Add(catalog[j]);
                }
            }
        }

        private static void Add(UIMapIcon icon)
        {
            if (icon == null) return;
            int type, selection;
            try
            {
                type = (int)icon.iconType;
                selection = icon.typeSelection;
            }
            catch (Exception) { return; }

            long key = Key(type, selection);
            if (Index.TryGetValue(key, out UIMapIcon existing))
            {
                // 优先 active 模板；inactive 变体仅作后备（clone 后由调用方显式激活）。
                bool existingActive = SafeActive(existing);
                if (existingActive || !SafeActive(icon)) return;
            }
            Index[key] = icon;
        }

        private static bool SafeActive(UIMapIcon icon)
        {
            try { return icon.gameObject != null && icon.gameObject.activeSelf; }
            catch (Exception) { return false; }
        }

        internal static class Log
        {
            internal static readonly HashSet<string> OnceKeys = new HashSet<string>(StringComparer.Ordinal);

            internal static void Once(string key, string message)
            {
                try
                {
                    if (!OnceKeys.Add(key)) return;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[MapIcons] " + message);
                }
                catch (Exception) { }
            }

            internal static void Info(string message)
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[MapIcons] " + message); }
                catch (Exception) { }
            }

            internal static void Warn(string message)
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[MapIcons] " + message); }
                catch (Exception) { }
            }
        }
    }
}
