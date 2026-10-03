// Greek 平民身高纠正回归（2026-10-03，跟进 PR #102）：+5% 曾误加在共享常量 NorseCivilianScaleY
// 上（北欧模型一起变高），现恢复 0.70×32/18，+5% 只作用于当前 Greek 世界的普通 Peasant。
// 1) 精确 source extraction：共享常量必须恰为 0.70f * 32f / 18f；Promote 与
//    Peasant_norselands.OnEnable 继续共用该常量；普通 Peasant 分支按
//    GreekScaleScope.NativeScale(...).y × 1.05f 应用，并排除带 WarriorPeasant 组件的改名模型；
//    文件里没有第二份身高魔数、没有二次累乘；
// 2) 数值：0.70×32/18 ≈ 1.244444（不再含 +5%）；普通居民的 +5% 是相对原生 Y 的乘法
//    （原生 1.0 → 1.05，1.2 → 1.26）；
// 3) 真实 GreekScaleScope 语义：只写 Y（X 朝向符号与 Z 保留）、重复 apply 不累乘、
//    非 Greek 作用域不写、回到当前 Greek 后恰好应用一次。
// 两个真实 OnEnable 入口、repeated enable、Mover 复位与 OFF/非 Greek restore 的完整行为契约
// 见 tests/native-scale-timing；本套件不模拟 Unity 渲染、不启动游戏；实机观感由用户验收。
using System;
using System.IO;
using System.Text.RegularExpressions;
using KingdomEnhancedMod;
using UnityEngine;
using Scope = KingdomEnhancedMod.GreekScaleScope;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        SourceContract();
        NumericContract();
        ScopeContract();
        Console.WriteLine("greek-civilian-height: ALL PASS — " + _checks + " checks");
        return 0;
    }

    private static void Verify(bool condition, string message)
    {
        if (!condition)
        {
            Console.WriteLine("FAIL " + message);
            Environment.ExitCode = 1;
            throw new InvalidOperationException(message);
        }
        _checks++;
        Console.WriteLine("PASS " + message);
    }

    // ---- 1) source extraction：共享常量与三个入口 ----

    private static void SourceContract()
    {
        string worker = StripComments(ReadSource("PatchRoles_Worker.cs"));
        string promote = StripComments(ReadSource("PatchRoles_Character.cs"));

        Match declaration = Regex.Match(worker,
            @"internal\s+const\s+float\s+NorseCivilianScaleY\s*=\s*(?<expr>[^;]+);");
        Verify(declaration.Success, "PatchRoles_Worker declares the shared NorseCivilianScaleY constant");
        string expression = Regex.Replace(declaration.Groups["expr"].Value, @"\s+", "");
        Verify(expression == "0.70f*32f/18f",
            "the shared constant is restored to exactly 0.70*32/18 (no +5%): " + expression);

        Verify(Regex.IsMatch(worker,
                @"GreekScaleScope\.ApplyY\(__instance\.transform, NorseCivilianScaleY\);"),
            "WarriorPeasant.OnEnable applies the shared constant to Y");
        Verify(Regex.IsMatch(worker,
                @"float targetY = WarriorPeasant_OnEnable_Patch\.NorseCivilianScaleY;"),
            "Peasant_norselands.OnEnable resolves the height from the same shared constant");
        Verify(Regex.IsMatch(worker,
                @"GreekScaleScope\.ApplyY\(__instance\.transform, targetY\);"),
            "Peasant_norselands.OnEnable applies that resolved constant to Y");

        Verify(Regex.Matches(worker, @"NorseCivilianScaleY\s*=").Count == 1,
            "PatchRoles_Worker declares the constant exactly once (uses never redeclare a number)");
        Verify(Regex.Matches(worker, @"\b0\.70f\b").Count == 1,
            "the 0.70 Norse baseline lives only in the shared declaration");
        Verify(!Regex.IsMatch(worker, @"NorseCivilianScaleY\s*\*"),
            "the shared constant is never multiplied again (no double scaling)");

        // 普通 Peasant 的 +5% 分支：组件排除北欧模型，只对原生 Y 做一次相对乘法。
        Verify(Regex.IsMatch(worker,
                @"else if \(__instance\.GetComponent<WarriorPeasant>\(\) == null\)"),
            "the ordinary-Peasant branch excludes any model carrying a WarriorPeasant component");
        Verify(Regex.IsMatch(worker,
                @"float targetY = GreekScaleScope\.NativeScale\(__instance\.transform\)\.y \* 1\.05f;"),
            "the ordinary-Peasant branch scales the native Y by exactly 1.05");
        Verify(Regex.Matches(worker, @"\b1\.05f\b").Count == 1,
            "the +5% factor appears exactly once in the worker file");
        Verify(Regex.IsMatch(worker,
                @"ScaleRegistryHolder\.Register\(__instance\.GetComponent<Mover>\(\), targetY\);"),
            "the ordinary-Peasant branch keeps the shared Mover registration contract");

        Verify(Regex.IsMatch(promote,
                @"GreekScaleScope\.ApplyY\(newChar\.transform, WarriorPeasant_OnEnable_Patch\.NorseCivilianScaleY\);"),
            "the Promote path applies the same shared constant to Y");
        Verify(Regex.Matches(promote, "NorseCivilianScaleY").Count == 1,
            "the Promote path references the constant exactly once (no duplicated number)");
        Verify(!Regex.IsMatch(promote, @"\b0\.70f\b|\b1\.05f\b|\b1\.244"),
            "no stale independent civilian height magic numbers in the Promote path");
    }

    // ---- 2) 数值契约 ----

    private static void NumericContract()
    {
        const float norse = 0.70f * 32f / 18f;
        Verify(Math.Abs(norse - 1.2444444f) < 1e-5f,
            "the Norse stand height is the original 0.70 at 18px / PPU 32: " + norse.ToString("R"));
        Verify(Math.Abs(norse - 0.735f * 32f / 18f) > 0.05f,
            "the +5% is removed from the shared Norse constant (no 0.735 stand height)");
        Verify(Math.Abs(1.0f * 1.05f - 1.05f) < 1e-6f
            && Math.Abs(1.2f * 1.05f - 1.26f) < 1e-6f,
            "the ordinary Greek boost is native-relative: 1.0 -> 1.05, 1.2 -> 1.26");
    }

    // ---- 3) 真实 GreekScaleScope 行为 ----

    private static void ScopeContract()
    {
        const float expected = 0.70f * 32f / 18f;
        ModConfig.Enabled.Value = true;
        BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;

        var civilian = new GameObject("Peasant_norselands");
        civilian.transform.localScale = new Vector3(-1f, 1f, 1.4f);   // x 朝向符号 + 任意 z
        Scope.ApplyY(civilian.transform, expected);
        Vector3 scaled = civilian.transform.localScale;
        Verify(Math.Abs(scaled.y - expected) < 1e-6f,
            "the real scope writes the restored stand height onto the civilian root");
        Verify(scaled.x == -1f && scaled.z == 1.4f,
            "facing sign (x) and z stay untouched: " + scaled);
        Scope.ApplyY(civilian.transform, expected);
        Verify(civilian.transform.localScale.y == scaled.y,
            "re-applying the same Y never multiplies it");

        var deferred = new GameObject("Peasant_norselands");
        BiomeHolder.Inst.BiomeIndex = 0;
        Scope.ApplyY(deferred.transform, expected);
        Verify(deferred.transform.localScale.y == 1f,
            "a foreign-world scope never writes the height");
        BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
        Scope.Tick();
        Verify(Math.Abs(deferred.transform.localScale.y - expected) < 1e-6f,
            "returning to the current Greek world applies the pending height exactly once");
    }

    // ---- helpers ----

    private static string ReadSource(string fileName)
    {
        string root = ResolveSourceRoot();
        string path = Path.Combine(root, "il2cpp", fileName);
        if (!File.Exists(path)) throw new FileNotFoundException("missing production source: " + path);
        return File.ReadAllText(path);
    }

    private static string ResolveSourceRoot()
    {
        string fromEnvironment = Environment.GetEnvironmentVariable("GREEK_CIVILIAN_SOURCE_ROOT");
        if (!string.IsNullOrEmpty(fromEnvironment) && Directory.Exists(fromEnvironment))
            return fromEnvironment;
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "il2cpp", "PatchRoles_Worker.cs")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException(
            "worker-source root not found (run from the source tree or set GREEK_CIVILIAN_SOURCE_ROOT)");
    }

    /// <summary>仅为源契约检查剥离行注释；生产常量/调用点均不在块注释内。</summary>
    private static string StripComments(string source)
    {
        string[] lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) lines[i] = lines[i].Substring(0, comment);
        }
        return string.Join("\n", lines);
    }
}
