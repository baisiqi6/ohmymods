// R3 实际入口 probe 的游戏类型桩（namespace KingdomEnhancedMod，与真实源码同命名空间）。
// 只定义真实 MapMountIcons.cs / MapResourceIconPlan.cs 引用的外部类型；native 方法体按 ARM 反汇编
// 语义最小仿真（UIMainMap.UpdateLandIcons 逐 UILand.UpdateLand；UILand.UpdateLand 派发 postfix）。
using System;
using System.Collections.Generic;
using System.Reflection;
using EntryProbe;
using BepInEx.Configuration;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace EntryProbe
{
    /// <summary>probe 观测/驱动钩子（不改真实源码；只被桩调用）。</summary>
    internal static class ProbeHooks
    {
        internal static int NativeRefreshCalls;
        internal static int UpdateLandCalls;
        internal static int ClusterEnableCalls;
        internal static int MenuDisableCalls;
        internal static int RepaintCalls;
        internal static int CanvasGroupAdds;
        internal static int CanvasGroupAlphaWrites;
        internal static int CanvasGroupBlocksWrites;
        internal static int ClearLandsCalls;
        internal static int ConfirmCalls;
        internal static int MenuTickCalls;
        internal static readonly List<string> RefreshReignTags = new List<string>();
        internal static readonly List<string> IconLogs = new List<string>();

        internal static void Reset()
        {
            NativeRefreshCalls = 0;
            UpdateLandCalls = 0;
            ClusterEnableCalls = 0;
            MenuDisableCalls = 0;
            RepaintCalls = 0;
            CanvasGroupAdds = 0;
            CanvasGroupAlphaWrites = 0;
            CanvasGroupBlocksWrites = 0;
            ClearLandsCalls = 0;
            ConfirmCalls = 0;
            MenuTickCalls = 0;
            RefreshReignTags.Clear();
        }

        internal static void RunUpdateLandPostfix(KingdomEnhancedMod.UILand land,
            KingdomEnhancedMod.CampaignSaveData.ReignInfo reign, int index)
        {
            UpdateLandCalls++;
            Invoke("KingdomEnhancedMod.MapMountIcons_UpdateLand_Patch", "Postfix",
                new object[] { land, reign, index });
        }

        internal static void RunMenuTickPostfix(KingdomEnhancedMod.MapTimelineMenu menu)
        {
            MenuTickCalls++;
            Invoke("KingdomEnhancedMod.MapMountIcons_MenuTick_Patch", "Postfix", new object[] { menu });
        }

        /// <summary>接线核验：world pending 门由 MenuTick_Patch.Postfix 内部调用（**无独立 Prefix 注入点**）。</summary>
        internal static void VerifyWorldGateWired()
        {
            Type type = typeof(KingdomEnhancedMod.MapMountIcons).Assembly.GetType(
                "KingdomEnhancedMod.MapMountIcons", throwOnError: true);
            MethodInfo info = type.GetMethod("TickWorldPendingGate",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(KingdomEnhancedMod.MapTimelineMenuGreece) }, null);
            if (info == null) throw new InvalidOperationException("probe: missing MapMountIcons.TickWorldPendingGate");
        }

        internal static void RunClusterEnablePostfix(KingdomEnhancedMod.UIMainMapLand land)
        {
            ClusterEnableCalls++;
            Invoke("KingdomEnhancedMod.MapMountIcons_ClusterEnable_Patch", "Postfix",
                new object[] { land });
        }

        internal static void RunMenuDisablePostfix(KingdomEnhancedMod.MapTimelineMenuGreece menu = null)
        {
            MenuDisableCalls++;
            Invoke("KingdomEnhancedMod.MapMountIcons_MenuDisable_Patch", "Postfix", new object[] { menu });
        }

        /// <summary>直链第三文件现成 ClearLands postfix（验证 OnLandsCleared 顺序 + 新增一行接线）。</summary>
        internal static void RunClearLandsPostfix(KingdomEnhancedMod.MapTimelineMenuGreece menu)
        {
            Invoke("KingdomEnhancedMod.ExtensionIslandMap_ClearLands_Patch", "Postfix", new object[] { menu });
        }

        private static void Invoke(string typeName, string method, object[] args)
        {
            Type type = typeof(KingdomEnhancedMod.MapMountIcons).Assembly.GetType(typeName, throwOnError: true);
            MethodInfo info = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
            if (info == null) throw new InvalidOperationException("probe: missing " + typeName + "." + method);
            info.Invoke(null, args);
        }
    }
}

/// <summary>真实游戏 Menu 类型在全局命名空间（B 源码以未限定名引用 Menu.Inst/ActionMap）。</summary>
public class Menu : UnityEngine.MonoBehaviour
{
    public static Menu Inst;
    public KingdomEnhancedMod.MapTimelineMenu ActiveMap;
}

namespace KingdomEnhancedMod
{
    public enum SteedType { None = 0, Deer = 1, Horse = 2 }

    public static class Hermit
    {
        public enum HermitType { None = 0, Fire = 1 }
    }

    public static class Statue
    {
        public enum Deity { None = 0, Athena = 1 }
    }

    public class CampaignSaveData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        public class LandMapData : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            public Il2CppStructArray<SteedType> steedSpawns;
            public Il2CppStructArray<Hermit.HermitType> hermit;
            public Il2CppStructArray<Statue.Deity> statue;
        }

        public class ReignInfo : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            public string tag = "(no-tag)";
            public Il2CppSystem.Collections.Generic.List<LandMapData> landData =
                new Il2CppSystem.Collections.Generic.List<LandMapData>();
        }

        public static CampaignSaveData current;
        public ReignInfo currentReign = new ReignInfo { tag = "current" };
        public int CurrentLand = 0;
        public int reign = 0;
        public Il2CppSystem.Collections.Generic.List<ReignInfo> previousReigns =
            new Il2CppSystem.Collections.Generic.List<ReignInfo>();

        public static CampaignSaveData MakeCurrent()
        {
            current = new CampaignSaveData();
            return current;
        }
    }

    public class UIMapIcon : Behaviour
    {
        public Image icon;
        public Text text;

        public void UpdateIcon(CampaignSaveData.ReignInfo reign, int landIndex)
        {
            ProbeHooks.RepaintCalls++;
        }

        internal override void CopyFrom(Component source)
        {
            var src = source as UIMapIcon;
            if (src == null) return;
            icon = src.icon;
            text = src.text;
        }
    }

    public class UIDynamicMapIcon : Behaviour
    {
        // native 实测：DynSteed=0 / DynHermit=1 / DynStatue=4（MapMountIcons 常量）
        public enum IconType { Steed = 0, Hermit = 1, Statue = 4, Other = 9 }

        public IconType _type;
        /// <summary>真实 ABI offset 0x24：1-based 坐骑序号（Steed 槽取 steedSpawns[_steedNum-1]）。</summary>
        public int _steedNum;
        public UIMapIcon _spawnedIcon;
    }

    public class UILand : Behaviour
    {
        public Il2CppReferenceArray<UIDynamicMapIcon> _dynamicMapIcons;

        /// <summary>仿真原生 UILand.UpdateLand 的 patch 派发（Harmony postfix）。</summary>
        public void UpdateLand(CampaignSaveData.ReignInfo reignInfo, int landIndex)
        {
            ProbeHooks.RunUpdateLandPostfix(this, reignInfo, landIndex);
        }
    }

    public class UIMainMapLand : Behaviour
    {
        public Button _button;
        public bool IsUnlocked = true;
        public void OnEnable() => ProbeHooks.RunClusterEnablePostfix(this);
    }

    public class UIMainMap : Behaviour
    {
        public Il2CppReferenceArray<UILand> _lands;
        public Il2CppReferenceArray<UIMainMapLand> _mainMapLands;

        /// <summary>仿真 UIMainMap.UpdateLandIcons @958410：i &lt; _lands.Length 且 i &lt; _mainMapLands.Length，
        /// 逐 _lands[i].UpdateLand(reignInfo, i)。</summary>
        public void UpdateLandIcons(CampaignSaveData.ReignInfo reignInfo)
        {
            ProbeHooks.NativeRefreshCalls++;
            ProbeHooks.RefreshReignTags.Add(reignInfo == null ? "<null>" : reignInfo.tag);
            Il2CppReferenceArray<UILand> lands = _lands;
            Il2CppReferenceArray<UIMainMapLand> mainMapLands = _mainMapLands;
            if (lands == null || mainMapLands == null) return;
            int count = Math.Min(lands.Length, mainMapLands.Length);
            for (int i = 0; i < count; i++)
            {
                UILand land = lands[i];
                if (land == null) continue;
                land.UpdateLand(reignInfo, i);
            }
        }
    }

    public class MapTimelineMenu : Behaviour
    {
        public int focusedReign;
        public int targetFocusedReign;
        public bool isAnimating;
        public bool HasScrolledTargetLand;

        /// <summary>仿真被 Harmony Prefix+Postfix 挂接的原生 Update（native spring 推进发生在 Update 内）。</summary>
        public void Update()
        {
            ProbeHooks.VerifyWorldGateWired();
            ProbeHooks.RunMenuTickPostfix(this);
        }
    }

    public class MapTimelineMenuGreece : MapTimelineMenu
    {
        // actual ABI：只有 ShowingWorld=0 / ShowingSingleIsland=1（无第二阶段值）
        public enum OpenWorldMapState { ShowingWorld = 0, ShowingSingleIsland = 1 }

        public Il2CppSystem.Collections.Generic.List<UILand> lands;
        public RectTransform _landsHolder;
        public RectTransform landScroller;
        public UIMainMap _mainMap;
        public OpenWorldMapState _openWorldMapState = OpenWorldMapState.ShowingWorld;

        public void OnDisable() => ProbeHooks.RunMenuDisablePostfix(this);

        // 原生 map 入口桩：ClearLands 按 Harmony 语义派发现成 postfix（第三文件 OnLandsCleared + 新增一行）。
        public void ClearLands() => ProbeHooks.RunClearLandsPostfix(this);
        public void UpdateLands(CampaignSaveData.ReignInfo reignInfo) { }
        public void Confirm(int landIndex) { }
        public void LoadLands(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> prefabs,
            GameObject mainMap) { }
    }

    public class BiomeHolder
    {
        public static BiomeHolder Inst;
        public static int GreeceBiomeIndex = 2;
        public int BiomeIndex = 2;
    }

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool>(true);
        public static ConfigEntry<bool> CrossWorldMountsEnabled = new ConfigEntry<bool>(true);
    }

    public static class ExtensionIslandMap
    {
        /// <summary>probe 桥控：返回 non-null 即视作已登记扩展 UILand（physical index）。</summary>
        public static Func<UILand, int?> PhysicalIndexOverride;

        /// <summary>probe 桥控：canonical 视觉 owner token 与 exact menu（模拟登记代际；换代 = 换新 token）。</summary>
        public static object VisualOwnerToken;
        public static MapTimelineMenuGreece VisualOwnerMenu;

        public static object TryGetVisualOwnerToken() => VisualOwnerToken;

        public static bool IsVisualOwnerMenu(MapTimelineMenuGreece menu)
            => menu != null && ReferenceEquals(menu, VisualOwnerMenu);

        // 第三文件（PatchExtensionIsland_Map）引用的现成入口：probe 计数/空实现（旅行行为不在本 probe 范围）
        // 真实顺序语义：native ClearLands 的 patch 先调本方法（清 registry/owner → token 归 null），
        // 才轮到 MapMountIcons.OnMenuLandsCleared；MapMountIcons 的收尾**不得**依赖 registry 仍活着。
        public static void OnLandsCleared(MapTimelineMenuGreece menu)
        {
            ProbeHooks.ClearLandsCalls++;
            ClearVisualOwner(menu);
        }

        /// <summary>probe 桥控：模拟真实登记的 ClearOwner（token/owner 归 null）。</summary>
        public static void ClearVisualOwner(MapTimelineMenuGreece menu)
        {
            if (menu == null || ReferenceEquals(VisualOwnerMenu, menu))
            {
                VisualOwnerToken = null;
                VisualOwnerMenu = null;
            }
        }
        public static void OnLandsLoaded(MapTimelineMenuGreece menu, Il2CppReferenceArray<GameObject> prefabs,
            GameObject mainMap) { }
        public static void BeforeLandsUpdated(MapTimelineMenuGreece menu) { }
        public static bool TryHandleConfirm(MapTimelineMenuGreece menu, int index)
        {
            ProbeHooks.ConfirmCalls++;
            return false;
        }
        public static bool TryRemapUpdateLand(UILand land, int index, out int physical)
        {
            physical = index;
            return false;
        }

        public static bool TryGetPhysicalLandIndex(UILand land, out int index)
        {
            index = -1;
            if (land == null || PhysicalIndexOverride == null) return false;
            // 真源语义：OnLandsCleared/ClearOwner 后 registry 拒绝（严格 owner）；probe 用 token 建模。
            if (VisualOwnerToken == null) return false;
            int? value = PhysicalIndexOverride(land);
            if (value == null) return false;
            index = value.Value;
            return true;
        }
    }

    public static class ExtensionIslandRuntime
    {
        public static bool Available = true;
        public static bool IsAvailableForCurrentCampaign() => Available;
    }

    public static class MapIconSources
    {
        /// <summary>真实 MapExtensionIslandArt 的日志入口（桩：只收集，不输出）。</summary>
        internal static class Log
        {
            internal static void Info(string message) { ProbeHooks.IconLogs.Add("INFO " + message); }
            internal static void Warn(string message) { ProbeHooks.IconLogs.Add("WARN " + message); }
        }

        /// <summary>probe 控：按 (iconType, type) 返回原生来源图标。</summary>
        public static Func<UILand, int, int, UIMapIcon> Resolver;
        public static int ResetCalls;
        /// <summary>资源目录读取计数（issue-156：原版十岛零接管 ⇒ 原生回调窗口内必须为 0）。</summary>
        public static int ResolveCalls;

        public static UIMapIcon Resolve(UILand land, int iconType, int type)
        {
            ResolveCalls++;
            return Resolver != null ? Resolver(land, iconType, type) : null;
        }

        public static void Reset() => ResetCalls++;
    }

    public static class MapCustomIconAssets
    {
        public static int ResetCalls;
        public static void Reset() => ResetCalls++;
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public ManualLogSource LogSource = new ManualLogSource();
    }
}
