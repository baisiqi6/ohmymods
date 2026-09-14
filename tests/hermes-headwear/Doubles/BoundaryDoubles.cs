using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 边界替身：HarmonyLib 特性、Il2CppInterop 委托桥，以及由 root / 视觉 worker
// 拥有、本 worker 不可修改的生产接缝（ModConfig、插件日志、世界层 scope、视觉层）。
// 生产核心文件与本替身同编进测试程序集，因此这里只需提供“同签名的最小语义”。
// ============================================================================

namespace HarmonyLib
{
    public enum MethodType
    {
        Normal,
        Getter,
        Setter,
        Constructor,
        StaticConstructor,
        Enumerator,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
        public HarmonyPatchAttribute()
        {
        }

        public HarmonyPatchAttribute(Type declaringType)
        {
        }

        public HarmonyPatchAttribute(Type declaringType, string methodName)
        {
        }

        public HarmonyPatchAttribute(Type declaringType, string methodName, Type[] argumentTypes)
        {
        }

        public HarmonyPatchAttribute(Type declaringType, MethodType methodType)
        {
        }

        public HarmonyPatchAttribute(Type declaringType, MethodType methodType, Type[] argumentTypes)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefixAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfixAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizerAttribute : Attribute
    {
    }
}

namespace Il2CppInterop.Runtime
{
    /// <summary>
    /// 生产侧调用 <c>DelegateSupport.ConvertDelegate&lt;NetworkPostbox.DynAction&gt;(Action)</c>；
    /// 替身把它包成 DynAction（原生委托在本替身里就是托管委托的包装）。
    /// </summary>
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(Delegate managedDelegate)
        {
            if (managedDelegate == null) return default;
            NetworkPostbox.DynAction native = new NetworkPostbox.DynAction((Action)managedDelegate);
            return (T)(object)native;
        }

        public static T ConvertDelegate<T>(Action managedDelegate)
        {
            return ConvertDelegate<T>((Delegate)managedDelegate);
        }
    }
}

namespace KingdomEnhancedMod
{
    /// <summary>ModConfig 替身（root 拥有；这里只保留生产核心读取的三个条目）。</summary>
    public sealed class FakeConfigEntry<T>
    {
        public FakeConfigEntry(T value)
        {
            Value = value;
        }

        public T Value { get; set; }
    }

    internal static class ModConfig
    {
        internal static FakeConfigEntry<bool> Enabled = new FakeConfigEntry<bool>(true);
        internal static FakeConfigEntry<bool> HermesHeadwearEnabled = new FakeConfigEntry<bool>(true);
        internal static FakeConfigEntry<int> HermesHeadwearChancePercent = new FakeConfigEntry<int>(30);

        internal static void ResetForTest()
        {
            Enabled = new FakeConfigEntry<bool>(true);
            HermesHeadwearEnabled = new FakeConfigEntry<bool>(true);
            HermesHeadwearChancePercent = new FakeConfigEntry<int>(30);
        }
    }

    internal sealed class FakeLogSource
    {
        internal readonly List<string> Messages = new List<string>();

        internal void LogInfo(object message) => Messages.Add("INFO " + message);

        internal void LogWarning(object message) => Messages.Add("WARN " + message);

        internal void LogError(object message) => Messages.Add("ERROR " + message);
    }

    internal sealed class KingdomEnhancedPlugin
    {
        internal static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();

        internal FakeLogSource LogSource { get; } = new FakeLogSource();
    }

    /// <summary>世界层 scope 替身（生产侧复用 GreekBankScope.IsInCurrentLayer 作为身份闸门）。</summary>
    internal static class GreekBankScope
    {
        internal static Func<Component, bool> IsInCurrentLayerFunc = component => component != null;

        internal static bool IsInCurrentLayer(Component component)
        {
            return IsInCurrentLayerFunc != null && IsInCurrentLayerFunc(component);
        }

        internal static void ResetForTest()
        {
            IsInCurrentLayerFunc = component => component != null;
        }
    }

    /// <summary>
    /// 视觉层替身（visual worker 拥有真实实现）。除了记录调用序列，还**模拟真实视图存在性**：
    /// Apply 成功 → (troll, choice) 记为已显示；Clear/ClearAll → 移除；IsApplied 据此回答。
    /// <see cref="SimulateViewLoss"/> 用来模拟 root/sprite 被销毁（视图消失但核心仍以为成功）。
    /// </summary>
    internal static class HermesHeadwearVisuals
    {
        internal static void OnNativeMaskSpawned(FriendlyTroll troll) { }

        internal readonly struct Call
        {
            internal Call(string kind, FriendlyTroll troll, int choice)
            {
                Kind = kind;
                Troll = troll;
                Choice = choice;
            }

            internal readonly string Kind;
            internal readonly FriendlyTroll Troll;
            internal readonly int Choice;

            public override string ToString() => Kind + "(" + Choice + ")";
        }

        private static readonly Dictionary<FriendlyTroll, int> Displayed = new Dictionary<FriendlyTroll, int>();

        internal static readonly List<Call> Calls = new List<Call>();
        internal static Func<FriendlyTroll, int, bool> ApplyResult = (troll, choice) => true;
        internal static int ApplyCalls;
        internal static int ClearCalls;
        internal static int ClearAllCalls;
        internal static int TickCalls;
        internal static int IsAppliedCalls;
        internal static int ApplyFailuresRemaining;

        internal static bool Apply(FriendlyTroll troll, int choice)
        {
            ApplyCalls++;
            Calls.Add(new Call("Apply", troll, choice));
            if (ApplyFailuresRemaining > 0)
            {
                ApplyFailuresRemaining--;
                return false;
            }
            bool applied = ApplyResult == null || ApplyResult(troll, choice);
            if (applied) Displayed[troll] = choice;
            return applied;
        }

        internal static void Clear(FriendlyTroll troll)
        {
            ClearCalls++;
            Calls.Add(new Call("Clear", troll, -1));
            Displayed.Remove(troll);
        }

        internal static void ClearAll()
        {
            ClearAllCalls++;
            Calls.Add(new Call("ClearAll", null, -1));
            Displayed.Clear();
        }

        internal static void Tick()
        {
            TickCalls++;
        }

        /// <summary>真实视图层提供的存在性探测：核心用它发现“显示被抹掉”并重做。</summary>
        internal static bool IsApplied(FriendlyTroll troll, int choice)
        {
            IsAppliedCalls++;
            return Displayed.TryGetValue(troll, out int shown) && shown == choice;
        }

        /// <summary>测试用：模拟 root/sprite 被销毁（视图消失，核心侧 VisualApplied 仍是 true）。</summary>
        internal static void SimulateViewLoss(FriendlyTroll troll)
        {
            Displayed.Remove(troll);
        }

        internal static void ResetForTest()
        {
            Calls.Clear();
            Displayed.Clear();
            ApplyResult = (troll, choice) => true;
            ApplyCalls = 0;
            ClearCalls = 0;
            ClearAllCalls = 0;
            TickCalls = 0;
            IsAppliedCalls = 0;
            ApplyFailuresRemaining = 0;
        }

        internal static int CountKind(string kind)
        {
            int count = 0;
            for (int i = 0; i < Calls.Count; i++)
            {
                if (Calls[i].Kind == kind) count++;
            }
            return count;
        }

        internal static Call LastKind(string kind)
        {
            for (int i = Calls.Count - 1; i >= 0; i--)
            {
                if (Calls[i].Kind == kind) return Calls[i];
            }
            return default;
        }
    }
}
