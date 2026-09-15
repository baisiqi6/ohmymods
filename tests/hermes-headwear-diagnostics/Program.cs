using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

/// <summary>
/// 直接源回归：把生产 il2cpp/HermesHeadwearDiagnostics.cs 与本目录的 Unity/BepInEx 桩一起编译，
/// 只断言可观察契约（额度、快照上限、字段内容、只读、归因、异常吞掉），不镜像整行日志字符串。
/// </summary>
internal static class Program
{
    internal static readonly List<string> Failures = new List<string>();
    private static int _passed;
    private static int _failed;

    internal static List<string> Log => KingdomEnhancedPlugin.Instance.LogSource.Lines;

    private static int Main()
    {
        Console.WriteLine("hermes headwear diagnostics tests (console / serial)");
        Console.WriteLine();
        Tests.Run();
        Console.WriteLine();
        Console.WriteLine("total: passed=" + _passed + " failed=" + _failed);
        for (int i = 0; i < Failures.Count; i++) Console.WriteLine("FAILED: " + Failures[i]);
        return _failed == 0 && _passed > 0 ? 0 : 1;
    }

    internal static void Test(string name, Action body)
    {
        Reset();
        try
        {
            body();
            _passed++;
            Console.WriteLine("ok   " + name);
        }
        catch (Exception e)
        {
            _failed++;
            Failures.Add(name + " :: " + e.Message);
            Console.WriteLine("FAIL " + name + " :: " + e.Message);
        }
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Eq<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + " (expected=" + expected + " actual=" + actual + ")");
    }

    internal static string Line(int index) => Log[index];

    internal static string Last() => Log[Log.Count - 1];

    internal static bool Contains(string line, string fragment) => line.IndexOf(fragment, StringComparison.Ordinal) >= 0;

    /// <summary>取 " key[...]" 这一块（用于断言各 renderer/head 块各自的字段）。</summary>
    internal static string Block(string line, string key)
    {
        int start = line.IndexOf(" " + key + "[", StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        int end = line.IndexOf("] ", start, StringComparison.Ordinal);
        return end < 0 ? line.Substring(start) : line.Substring(start, end - start + 1);
    }

    internal static void Reset()
    {
        KingdomEnhancedPlugin.Instance = new PluginStub();
        ((IList)Get("Samples")).Clear();
        ((System.Text.StringBuilder)Get("Line")).Clear();
        Set("_headwear", 0);
        Set("_miss", 0);
        Set("_visualOpen", 0);
        Set("_unattributedRemoved", 0);
        Counter.Reset();
    }

    private static readonly Type Diagnostics = typeof(HermesHeadwearDiagnostics);

    private static object Get(string name) =>
        Diagnostics.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    private static void Set(string name, object value) =>
        Diagnostics.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
}

/// <summary>原生读取计数器：额度和快照用尽后的重复调用必须一个都不增加。</summary>
internal static class Counter
{
    internal static void Reset()
    {
        UnityEngine.Object.PointerReads = 0;
        UnityEngine.Component.ContextReads = 0;
        UnityEngine.GameObject.InstanceIdReads = 0;
        UnityEngine.Transform.FieldReads = 0;
        UnityEngine.SpriteRenderer.FieldReads = 0;
        UnityEngine.SpriteRenderer.Writes = 0;
        UnityEngine.SpriteRenderer.MaterialAccesses = 0;
    }

    internal static int NativeReads =>
        UnityEngine.Object.PointerReads + UnityEngine.Component.ContextReads + UnityEngine.GameObject.InstanceIdReads;

    internal static int RendererReads => UnityEngine.SpriteRenderer.FieldReads;

    internal static int Writes => UnityEngine.SpriteRenderer.Writes;

    internal static int MaterialAccesses => UnityEngine.SpriteRenderer.MaterialAccesses;
}

/// <summary>一个转化出的友好巨魔：本体 GameObject + body/原生 mask/template/自有头饰 renderer + Head 挂点。</summary>
internal sealed class Unit
{
    internal GameObject Go;
    internal GameObject HeadGo;
    internal FriendlyTroll Troll;
    internal Transform Head;
    internal SpriteRenderer Body;
    internal SpriteRenderer Mask;
    internal SpriteRenderer Template;
    internal SpriteRenderer Own;

    internal static Unit Make()
    {
        var unit = new Unit();
        unit.Go = new GameObject { layer = 3 };
        unit.HeadGo = new GameObject { layer = 3 };
        unit.Troll = new FriendlyTroll();
        unit.Head = new Transform();
        unit.Body = new SpriteRenderer();
        unit.Mask = new SpriteRenderer();
        unit.Template = new SpriteRenderer();
        unit.Own = new SpriteRenderer();
        unit.Go.Add(unit.Troll);
        unit.Go.Add(unit.Body);
        unit.Go.Add(unit.Mask);
        unit.Go.Add(unit.Template);
        unit.HeadGo.Add(unit.Head);
        unit.Go.Add(unit.Own);

        var sprite = new Sprite { name = "troll_masks_0" };
        var material = new Material { name = "Pow-Diffuse-Snow", shader = new Shader { name = "Sprites/Default" } };
        unit.Body.sprite = new Sprite { name = "Troll_friendly" };
        unit.Body.sharedMaterial = material;
        unit.Mask.sprite = sprite;
        unit.Mask.sharedMaterial = material;
        unit.Template.sprite = sprite;
        unit.Template.sharedMaterial = material;
        unit.Own.sprite = sprite;
        unit.Own.sharedMaterial = material;
        unit.Own.sortingOrder = 1;
        unit.Troll._mask = unit.Mask;
        unit.Troll._maskPrefab = unit.Template;
        unit.Troll._maskIndex = 2;

        Counter.Reset(); // 布置 fixture 的写入/读取不算诊断行为
        return unit;
    }
}
