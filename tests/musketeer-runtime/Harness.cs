using System;
using System.Collections.Generic;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// PC / Android 2.4 的 Renderer 边界断言 helper（MusketeerVisuals 与 MusketeerGunVisuals 两套测试共用）。
    /// Android 2.4 没有 forceRenderingOff / Get-SetPropertyBlock ICall，隐藏原语是共享 Renderer.enabled；
    /// PC 保持独占 forceRenderingOff。测试按模式选择真实语义，而不是按 PC 语义伪造 Android 结果。
    /// </summary>
    internal static class RendererContract
    {
        /// <summary>原生 renderer 是否被隐藏：PC 读 forceRenderingOff；Android 读共享 enabled。</summary>
        internal static bool NativeHidden(UnityEngine.Renderer native)
        {
#if ANDROID
            return !native.enabled;
#else
            return native.forceRenderingOff;
#endif
        }

        /// <summary>模拟第三方/引擎隐藏原生 renderer（PC 独占字段 / Android 共享字段）。</summary>
        internal static void HideNative(UnityEngine.Renderer native)
        {
#if ANDROID
            native.enabled = false;
#else
            native.forceRenderingOff = true;
#endif
        }

        /// <summary>
        /// 让下一次原生渲染可见性写入失败（验证 Hide/Show 写后读回确认与凭据保留）：PC 让
        /// forceRenderingOff setter 抛异常；Android 让 enabled 写入被引擎忽略，触发读回确认失败。
        /// </summary>
        internal static void FailNextNativeWrite(UnityEngine.Renderer native)
        {
#if ANDROID
            native.SuppressEnabledWrites = 1;
#else
            native.ThrowOnForceRenderingOffSet = true;
#endif
        }

#if ANDROID
        /// <summary>
        /// 全套件（含 GunVisualTests）跑完后统一断言：Android producer 路径从不读写 Android 2.4
        /// 缺失的成员。ANDROID stub 让这些成员计数并抛异常，所以计数为 0 同时证明零触达。
        /// </summary>
        internal static void AssertAndroidIcallContract()
        {
            Check.Equal(0, UnityEngine.Renderer.ForceOffReads, "forceRenderingOff reads");
            Check.Equal(0, UnityEngine.Renderer.ForceOffWrites, "forceRenderingOff writes");
            Check.Equal(0, UnityEngine.Renderer.PropertyBlockReads, "GetPropertyBlock reads");
            Check.Equal(0, UnityEngine.Renderer.PropertyBlockWrites, "SetPropertyBlock writes");
            Check.Equal(0, UnityEngine.SpriteRenderer.FlipYReads, "flipY reads");
            Check.Equal(0, UnityEngine.SpriteRenderer.FlipYWrites, "flipY writes");
            Check.Equal(1, CountInfo("per-instance MaterialPropertyBlock not copied"),
                "property-block skip info logged exactly once");
        }
#endif

        /// <summary>统计一次性 info 日志（LogStub 行前缀 "I:"）出现的次数。</summary>
        internal static int CountInfo(string fragment)
        {
            int count = 0;
            var lines = KingdomEnhancedMod.KingdomEnhancedPlugin.Instance.LogSource.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line != null && line.StartsWith("I:", StringComparison.Ordinal)
                    && line.IndexOf(fragment, StringComparison.Ordinal) >= 0) count++;
            }
            return count;
        }
    }

    internal static class Check
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void False(bool condition, string message)
        {
            if (condition) throw new Exception(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
        }

        internal static void Near(double expected, double actual, double tolerance, string message)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
        }
    }

    internal static class Case
    {
        internal static int Passed;
        internal static int Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            try
            {
                body();
                Passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception e)
            {
                Failed++;
                Failures.Add(name + ": " + e.Message);
                Console.WriteLine("FAIL " + name + ": " + e.Message);
            }
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            AnimationTests.Run();
            CombatTests.Run();
            DeerHuntTests.Run();
            RuntimeTests.Run();
            WorldTests.Run();
            VisualTests.Run();
            GunVisualTests.Run();
#if ANDROID
            // 必须最后跑：统计全套件的缺失 ICall 触达（含 GunVisualTests）。
            Case.Run("android never touches the removed renderer / property-block ICalls",
                RendererContract.AssertAndroidIcallContract);
#endif

            Console.WriteLine();
            Console.WriteLine("passed=" + Case.Passed + " failed=" + Case.Failed);
            for (int i = 0; i < Case.Failures.Count; i++) Console.WriteLine("  FAIL " + Case.Failures[i]);
            return Case.Failed == 0 ? 0 : 1;
        }
    }
}
