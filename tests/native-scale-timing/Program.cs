// 平民身高纠正的真实入口行为回归（2026-10-03）：直接编译生产 GreekScaleScope.cs +
// PatchRoles_Worker.cs，驱动两个真实 OnEnable postfix 与 Mover.Update postfix。
// 六个场景中三个在纠正前必然失败：普通 Greek 居民未 +5%、北欧常量被误加 +5%、
// 非单位原生 Y 未做相对 +5%；其余三个是北欧改名模型排除、非 Greek/OFF 不加高且
// 退出时还原的护栏。边界替身见 Stubs.cs；不模拟 Unity 渲染、不启动游戏。
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using KingdomEnhancedMod;
using Scope = KingdomEnhancedMod.GreekScaleScope;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    private static int _checks;
    private static int _failures;

    private static int Main()
    {
        Case("ordinary Greek Peasant gets only native Y +5%, stable across repeated enable and Mover reset",
            OrdinaryGreekPeasant);
        Case("Norse Peasant keeps the 0.70 stand height through both entry hooks",
            NorsePeasantHooks);
        Case("WarriorPeasant component excludes a renamed Norse model from the ordinary +5%",
            RenamedWarriorExcluded);
        Case("ordinary Peasant +5% is relative to a non-unit native baseline and restores",
            NonUnitBaseline);
        Case("ordinary Peasant stays native outside Greek and restores when the scope exits",
            ForeignWorld);
        Case("ordinary Peasant stays native with the mod disabled and restores when disabled",
            ModDisabled);

        Console.WriteLine(_failures == 0
            ? "native-scale-timing: ALL PASS — " + _checks + " checks"
            : "native-scale-timing: " + _failures + " case(s) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void OrdinaryGreekPeasant()
    {
        Mover mover = Actor(1.0f);
        Peasant peasant = PeasantOn(mover, "Peasant(Clone)");

        Enter(peasant);
        Equal(1.05f, mover.transform.localScale.y, "Greek resident native 1.0 -> 1.05");
        Equal(-1f, mover.transform.localScale.x, "facing sign preserved");
        Equal(1.4f, mover.transform.localScale.z, "z preserved");

        Enter(peasant);
        Equal(1.05f, mover.transform.localScale.y, "repeated enable does not multiply again");

        NativeReset(mover);
        Mover_Update_Patch.Mover_Update_Postfix(mover);
        Equal(1.05f, mover.transform.localScale.y, "Mover update repairs a native Y reset");
        Equal(1f, mover.transform.localScale.x, "repaired X untouched");
        Equal(1f, mover.transform.localScale.z, "repaired Z untouched");
    }

    private static void NorsePeasantHooks()
    {
        const float norse = 0.70f * 32f / 18f;
        Mover mover = Actor(1.0f);
        Peasant peasant = PeasantOn(mover, "Peasant_norselands(Clone)");
        WarriorPeasant warrior = mover.gameObject.AddComponent<WarriorPeasant>();

        Enter(peasant);
        Equal(norse, mover.transform.localScale.y, "Peasant_norselands keeps the 0.70 stand height");
        WarriorPeasant_OnEnable_Patch.WarriorPeasant_OnEnable_Postfix(warrior);
        Equal(norse, mover.transform.localScale.y, "WarriorPeasant hook shares the same constant");
        True(norse < 0.735f * 32f / 18f - 0.05f, "no leftover +5% on the Norse constant");
    }

    private static void RenamedWarriorExcluded()
    {
        Mover mover = Actor(0.8f);
        Peasant peasant = PeasantOn(mover, "renamed-norse-model");
        mover.gameObject.AddComponent<WarriorPeasant>();

        Enter(peasant);
        Equal(0.8f, mover.transform.localScale.y, "component exclusion keeps the model native");
        True(!Scope.TryGet(mover, out _), "no scale ownership registered for the excluded model");
    }

    private static void NonUnitBaseline()
    {
        Mover mover = Actor(1.2f);
        Peasant peasant = PeasantOn(mover, "Peasant(Clone)");

        Enter(peasant);
        Equal(1.26f, mover.transform.localScale.y, "native 1.2 -> 1.26 (relative +5%)");

        Scope.Restore(mover.transform);
        Equal(1.2f, mover.transform.localScale.y, "explicit restore returns the native baseline");
    }

    private static void ForeignWorld()
    {
        Mover mover = Actor(1.0f);
        Peasant peasant = PeasantOn(mover, "Peasant(Clone)");

        BiomeHolder.Inst.BiomeIndex = BiomeHolder.NorselandsBiomeIndex;
        Enter(peasant);
        Equal(1.0f, mover.transform.localScale.y, "outside Greek stays native");

        BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
        Mover_Update_Patch.Mover_Update_Postfix(mover);
        Equal(1.05f, mover.transform.localScale.y, "entering Greek applies the pending +5% once");

        BiomeHolder.Inst.BiomeIndex = BiomeHolder.NorselandsBiomeIndex;
        Mover_Update_Patch.Mover_Update_Postfix(mover);
        Equal(1.0f, mover.transform.localScale.y, "leaving Greek restores the native Y");
    }

    private static void ModDisabled()
    {
        Mover mover = Actor(1.0f);
        Peasant peasant = PeasantOn(mover, "Peasant(Clone)");

        Enter(peasant);
        Equal(1.05f, mover.transform.localScale.y, "Greek baseline applies +5%");

        ModConfig.Enabled.Value = false;
        Mover_Update_Patch.Mover_Update_Postfix(mover);
        Equal(1.0f, mover.transform.localScale.y, "OFF restores the native Y");
    }

    private static void Enter(Peasant peasant) =>
        Peasant_OnEnable_Patch.Peasant_OnEnable_Postfix(peasant);

    private static Mover Actor(float nativeY)
    {
        var go = new GameObject();
        go.transform.localScale = new Vector3(-1f, nativeY, 1.4f);
        return go.AddComponent<Mover>();
    }

    private static Peasant PeasantOn(Mover mover, string name)
    {
        mover.gameObject.name = name;
        return mover.gameObject.AddComponent<Peasant>();
    }

    private static void NativeReset(Mover mover) =>
        mover.transform.localScale = new Vector3(1f, 1f, 1f);

    private static void Case(string name, Action action)
    {
        Reset();
        try
        {
            action();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            _failures++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }

    private static void Reset()
    {
        var requests = (IDictionary)typeof(Scope).GetField("Requests", Static).GetValue(null);
        requests.Clear();
        ModConfig.Enabled.Value = true;
        BiomeHolder.Inst = new BiomeHolder();
    }

    private static void Equal(float expected, float actual, string label)
    {
        _checks++;
        if (MathF.Abs(expected - actual) > 1e-6f)
            throw new InvalidOperationException(label + ": expected " + expected + ", actual " + actual);
        Console.WriteLine("  ok " + label);
    }

    private static void True(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine("  ok " + label);
    }
}
