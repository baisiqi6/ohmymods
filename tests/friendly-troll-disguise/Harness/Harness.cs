using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace FriendlyTrollDisguiseTests
{
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
                throw new Exception(message + " (expected=" + expected + " actual=" + actual + ")");
        }

        internal static void Same(object expected, object actual, string message)
        {
            if (!ReferenceEquals(expected, actual)) throw new Exception(message);
        }

        internal static void Null(object value, string message)
        {
            if (value != null) throw new Exception(message);
        }
    }

    /// <summary>一条用例：共享静态逐个复位后串行执行。</summary>
    internal static class Case
    {
        internal static int Passed;
        internal static int Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            Fixture.Reset();
            try
            {
                body();
                Passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception exception)
            {
                Failed++;
                Failures.Add(name + ": " + exception.Message);
                Console.WriteLine("  FAIL " + name + " -> " + exception.Message);
            }
        }
    }

    /// <summary>一个友好巨魔单位（含其 Damageable 与可选原生 mask 子对象）。</summary>
    internal sealed class FriendlyUnit
    {
        internal GameObject Object;
        internal FriendlyTroll Troll;
        internal Damageable Damageable;
        internal GameObject MaskObject;
        internal SpriteRenderer Mask;
    }

    internal static class Fixture
    {
        internal static void Reset()
        {
            ModConfig.Enabled = new ConfigEntry<bool> { Value = true };
            NetworkBigBoss.HasWorldAuth = true;
            OptionalQoLScope.SameIsland = true;
            PatchDivine_HermesHeadwear.DisguiseHeadwear = false;
            PatchDivine_HermesHeadwear.Throw = false;
            PatchDivine_HermesHeadwear.Calls = 0;
            PatchDivine_HermesHeadwear.LastTroll = null;
        }

        internal static GameObject NewObject(bool active = true, float x = 0f)
        {
            var go = new GameObject { activeInHierarchy = active, name = "go" };
            go.Transform = go.AddComponent<Transform>();
            go.Transform.position = new Vector3(x, 0f, 0f);
            return go;
        }

        /// <summary>
        /// 友好巨魔单位。<paramref name="maskRenderer"/> 为真时额外建一个 mask 子对象
        /// （原生 Pool.Spawn 出来的 renderer），并按参数设置 enabled / sprite。
        /// </summary>
        internal static FriendlyUnit Friendly(int maskIndex = -1, bool maskRenderer = false,
            bool maskEnabled = true, bool maskSprite = true, bool unitActive = true, float x = 0f)
        {
            var unit = new FriendlyUnit();
            unit.Object = NewObject(unitActive, x);
            unit.Troll = unit.Object.AddComponent<FriendlyTroll>();
            unit.Damageable = unit.Object.AddComponent<Damageable>();

            if (maskRenderer)
            {
                unit.MaskObject = NewObject();
                unit.Mask = unit.MaskObject.AddComponent<SpriteRenderer>();
                unit.Mask.enabled = maskEnabled;
                unit.Mask.sprite = maskSprite ? new Sprite() : null;
                // 原生 Pool.Spawn<SpriteRenderer>(..., parent: transform)：mask 挂在单位下。
                unit.MaskObject.Transform.SetParent(unit.Object.Transform);
                unit.Troll._mask = unit.Mask;
            }

            unit.Troll._maskIndex = maskIndex;
            // 之后的写计数只统计生产代码：替身初始化写完立即清零。
            unit.Troll.MaskIndexWrites = 0;
            unit.Troll.MaskWrites = 0;
            return unit;
        }

        /// <summary>普通（非友好巨魔）候选：独立 GameObject 上的 Damageable。</summary>
        internal static Damageable Target(float x)
        {
            GameObject go = NewObject(true, x);
            return go.AddComponent<Damageable>();
        }

        internal static void AssertNativeFieldsUntouched(FriendlyUnit unit, string message)
        {
            Check.Equal(0, unit.Troll.MaskIndexWrites, message + "：_maskIndex 被写");
            Check.Equal(0, unit.Troll.MaskWrites, message + "：_mask 被写");
        }

        /// <summary>替身初始化（Add）之后清零计数器：断言只统计被测代码的读写。</summary>
        internal static void ResetListCounters(TargetCacher cache)
        {
            cache._trollPriorityTargets.Writes = 0;
            cache._trollPriorityTargets.Reads = 0;
            cache._trollLowPriorityTargets.Writes = 0;
            cache._trollLowPriorityTargets.Reads = 0;
        }
    }
}
