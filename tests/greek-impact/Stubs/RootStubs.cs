// 根模块 stub：ModConfig / ArcherOptionsScope / PatchRoles_KnightStyle / 日志，
// 以及测试世界夹具（world 身份 + collider 注册表 + 全局重置）。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    internal sealed class ConfigEntryStub<T>
    {
        public T Value;
        public ConfigEntryStub(T value) { Value = value; }
    }

    internal static class ModConfig
    {
        /// <summary>与生产一致：默认关闭。</summary>
        internal static ConfigEntryStub<bool> ArcherImpactEnabled = new ConfigEntryStub<bool>(false);
    }

    /// <summary>世界身份门：测试里 world = 当前 layer root，切世界即换 root。</summary>
    internal static class ArcherOptionsScope
    {
        internal static bool Active;
        internal static GameObject LayerRoot;
        internal static IntPtr World, Layer;
        internal static int Scene;

        internal static bool IsActive => Active;

        internal static bool IsCurrent(Component component)
        {
            if (component == null || component.gameObject == null || !component.gameObject.activeInHierarchy) return false;
            GameObject go = component.gameObject;
            while (go.transform.parent != null && go.transform.parent.gameObject != null) go = go.transform.parent.gameObject;
            return go == LayerRoot;
        }

        internal static bool TryGetContext(out IntPtr world, out IntPtr layer, out int scene)
        {
            world = World; layer = Layer; scene = Scene;
            return Active && World != IntPtr.Zero && Layer != IntPtr.Zero;
        }
    }

    internal static class PatchRoles_KnightStyle
    {
        /// <summary>测试可注入的 style 解析（生产实现读取骑士外观/动画风格）。</summary>
        internal static Func<Knight, int> Resolver = _ => 3;

        internal static bool TryGetResolvedStyleIndex(Knight knight, out int styleIndex)
        {
            styleIndex = knight != null ? Resolver(knight) : -1;
            return styleIndex >= 0;
        }
    }

    internal sealed class LogSink
    {
        internal readonly List<string> Lines = new List<string>();
        public void LogInfo(string message) => Lines.Add("I:" + message);
        public void LogWarning(string message) => Lines.Add("W:" + message);
    }

    internal sealed class PluginStub
    {
        internal readonly LogSink LogSource = new LogSink();
    }

    internal static class KingdomEnhancedPlugin
    {
        internal static PluginStub Instance = new PluginStub();
    }

    /// <summary>测试环境：物理注册表 + 层名表 + 世界/配置/时间/账本整体重置。</summary>
    internal static class Env
    {
        internal static readonly List<Collider2D> Colliders = new List<Collider2D>();

        private static readonly Dictionary<string, int> LayerIndices = new Dictionary<string, int>
        {
            { "Default", 0 }, { "Player", 3 }, { "Wildlife", 8 }, { "Enemies", 7 },
            { "Obstacles", 9 }, { "Ground", 10 }, { "Projectiles", 11 }
        };

        internal const int EnemiesLayer = 7;
        internal const int WildlifeLayer = 8;
        internal const int ObstaclesLayer = 9;

        internal static int LayerIndex(string layerName) =>
            layerName != null && LayerIndices.TryGetValue(layerName, out int index) ? index : -1;

        internal static void Reset()
        {
            PatchArcher_GreekImpact.ReleaseAll();
            PatchArcher_GreekImpact.StatBursts = 0;
            PatchArcher_GreekImpact.StatDamage = 0;
            PatchArcher_GreekImpact.StatBufferFull = 0;
            PatchArcher_GreekImpact.StatVolleyCap = 0;
            PatchArcher_GreekImpact.StatLeaseCap = 0;
            PatchArcher_GreekImpact.StatScopeCap = 0;
            PatchArcher_GreekImpact.StatReentry = 0;
            PatchArcher_GreekImpact.StatLeaseRetired = 0;
            PatchArcher_GreekImpact.StatArcherInvalidated = 0;
            PatchArcher_GreekImpact.StatNoTarget = 0;
            PatchArcher_GreekImpact.StatStackOverflow = 0;
            PatchArcher_GreekImpact.StatSourceInvalidated = 0;
            PatchArcher_GreekImpact.StatStaleTicket = 0;
            PatchArcher_GreekImpact.StatSubstituted = 0;
            PatchArcher_GreekImpact.StatSubstituteFailed = 0;
            PatchArcher_GreekImpact.StatSubstituteReentry = 0;
            Colliders.Clear();
            ModConfig.ArcherImpactEnabled.Value = false;
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.HasClientCaughtUp = true;
            Time.time = 100f;
            Time.timeScale = 1f;
            ArcherOptionsScope.Active = false;
            ArcherOptionsScope.LayerRoot = null;
            ArcherOptionsScope.World = IntPtr.Zero;
            ArcherOptionsScope.Layer = IntPtr.Zero;
            ArcherOptionsScope.Scene = 0;
            PatchRoles_KnightStyle.Resolver = _ => 3;
            KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
            Physics2D.QueryCount = 0;
            Physics2D.OverflowIgnored = false;
        }

        /// <summary>建立（或切换）当前世界：layer root 变化 = 换 world。</summary>
        internal static GameObject NewWorld(int scene = 1)
        {
            GameObject root = new GameObject("gameLayer", 0);
            ArcherOptionsScope.Active = true;
            ArcherOptionsScope.LayerRoot = root;
            ArcherOptionsScope.Layer = root.Pointer;
            ArcherOptionsScope.World = new IntPtr(0x5000 + scene);
            ArcherOptionsScope.Scene = scene;
            return root;
        }

        internal static void DisableScope()
        {
            ArcherOptionsScope.Active = false;
        }
    }
}
