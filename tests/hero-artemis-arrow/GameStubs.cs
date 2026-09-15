// 原生边界替身（游戏/模组侧）：
//   * Arrow / Archer / ArrowAttack 只暴露模块真正引用的成员（多一个都没有）；
//   * HeroArcherRuntime 只提供 Enabled / IsHero 两个缝（编译期证明模块绝无战斗资格/目标判定、
//     不读 HeroArcherRuntime 的其它状态）；
//   * ArcherOptionsScope / KingdomEnhancedPlugin 是模块的 only-world 上下文与日志缝。
// ArrowAttack 存在但没有 ArtemisArrow 组件类型：模块要添加原生组件/行为会直接编不过。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>原生 Arrow 替身：模块只允许读 gameObject/_spriteRenderer/archer/Pointer，写外观仅经 renderer。</summary>
    internal sealed class Arrow : Component
    {
        public SpriteRenderer _spriteRenderer;

        private GameObject _archer;
        /// <summary>true 时读 archer 抛异常（测试归属复核的「未知」分支）。</summary>
        public bool ArcherReadThrows;

        public GameObject archer
        {
            get
            {
                if (ArcherReadThrows) throw new InvalidOperationException("stub: arrow archer read threw");
                return _archer;
            }
            set { _archer = value; }
        }

        // 只读探针（物理/伤害/拖尾/根变换），模块绝不触碰。
        public bool isFireArrow;
        public int damage = 1;
        public bool colliderEnabled = true;
        public float colliderRadius = 0.05f;
        public bool trailEnabled;
        public float trailTime = 0.2f;
        public bool rigidbodyKinematic;
        public float rootX;
        public float rootY;
        public float rootScaleX = 1f;
        public float rootScaleY = 1f;

        /// <summary>测试用：下一次读 Pointer 抛异常。</summary>
        public bool PointerReadThrows;

        public override IntPtr Pointer
        {
            get
            {
                if (PointerReadThrows) throw new InvalidOperationException("stub: arrow Pointer read threw");
                return base.Pointer;
            }
            set { base.Pointer = value; }
        }

        /// <summary>物理/伤害/拖尾/根变换的签名：测试在整轮操作前后比对，必须逐字节一致。</summary>
        public string NonVisualSignature()
        {
            return "fire=" + isFireArrow + " dmg=" + damage + " col=" + colliderEnabled + "/" + colliderRadius
                + " trail=" + trailEnabled + "/" + trailTime + " kin=" + rigidbodyKinematic
                + " pos=" + rootX + "," + rootY + " scale=" + rootScaleX + "," + rootScaleY
                + " comps=" + (gameObject != null ? gameObject.Components.Count : -1);
        }
    }

    internal sealed class Archer : Component
    {
    }

    internal sealed class ArrowAttack : Component
    {
        /// <summary>只为 [HarmonyPatch(typeof(ArrowAttack), "FireArrowInternal")] 的 typeof 而存在。</summary>
        internal void FireArrowInternal(GameObject source) { }
    }

    /// <summary>英雄身份缝：测试用 native 指针集合决定谁当前是英雄。</summary>
    internal static class HeroArcherRuntime
    {
        internal static bool EnabledState;
        internal static readonly HashSet<IntPtr> HeroPointers = new HashSet<IntPtr>();
        internal static int IsHeroCalls;
        internal static bool IsHeroThrows;

        internal static bool Enabled => EnabledState;

        internal static bool IsHero(Archer archer)
        {
            IsHeroCalls++;
            if (IsHeroThrows) throw new InvalidOperationException("stub: IsHero threw");
            if (archer == null) return false;
            try { return HeroPointers.Contains(archer.Pointer); }
            catch (Exception) { return false; }
        }

        internal static void Reset()
        {
            EnabledState = false;
            HeroPointers.Clear();
            IsHeroCalls = 0;
            IsHeroThrows = false;
        }
    }

    internal static class ArcherOptionsScope
    {
        internal static bool ContextAvailable = true;
        internal static IntPtr WorldPtr = new IntPtr(7001);
        internal static IntPtr LayerPtr = new IntPtr(7002);
        internal static int SceneHandle = 7;
        internal static bool Throws;

        internal static bool TryGetContext(out IntPtr world, out IntPtr layer, out int scene)
        {
            if (Throws) throw new InvalidOperationException("stub: TryGetContext threw");
            world = WorldPtr;
            layer = LayerPtr;
            scene = SceneHandle;
            return ContextAvailable;
        }

        internal static void Reset()
        {
            ContextAvailable = true;
            WorldPtr = new IntPtr(7001);
            LayerPtr = new IntPtr(7002);
            SceneHandle = 7;
            Throws = false;
        }
    }

    internal sealed class StubLogSource
    {
        internal readonly List<string> Lines = new List<string>();
        public void LogInfo(string message) => Lines.Add("INFO " + message);
        public void LogWarning(string message) => Lines.Add("WARN " + message);
        public void LogError(string message) => Lines.Add("ERROR " + message);
        internal bool Contains(string fragment) => Lines.Exists(line => line.Contains(fragment));
    }

    internal sealed class KingdomEnhancedPluginStub
    {
        internal readonly StubLogSource LogSource = new StubLogSource();
    }

    internal static class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPluginStub Instance = new KingdomEnhancedPluginStub();
    }
}
