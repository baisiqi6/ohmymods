// R4/R5 签名回归屏障（真实候选驱动）：读取**指定编译候选 DLL**，用 Cecil 从
// `KingdomEnhancedMod.CoinCourierPersistence` 的嵌套类型上读取真实 HarmonyPatch 元数据
// （declaringType/method/overload argTypes），再到实际 2.4 `Assembly-CSharp.dll` 解析唯一
// native wrapper，并对真实目标集检查：
//   1. 每个 patch 目标唯一存在（重载歧义=FAIL）；
//   2. 无 any 按值 `Il2CppSystem.ValueType` 派生参数（当前 Interop value_box 崩因）；
//   3. 删除 factory 绝不能是目标；`d__91.MoveNext` 必须存在且 bool 无参；
//   4. 仅支持本模块现用的两种 HarmonyPatch ctor（(Type,string) / (Type,string,Type[])），
//      未知形状显式 FAIL，不猜。
// 解析失败（非 primitive/基础类型的 Resolve 失败）一律 FAIL，禁止 catch 吞掉假装安全。
// 只读 Cecil；不加载/执行游戏程序集或候选 DLL。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

internal static class SignatureCheck
{
    private static int _checks;
    private static int _failures;
    private static DefaultAssemblyResolver _resolver;
    private static ModuleDefinition _game;

    private static int Main(string[] args)
    {
        string interopDir = null;
        string coreDir = null;
        string candidatePath = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--candidate=", StringComparison.Ordinal)) candidatePath = arg.Substring("--candidate=".Length);
            else if (arg.StartsWith("--interop=", StringComparison.Ordinal)) interopDir = arg.Substring("--interop=".Length);
            else if (arg.StartsWith("--core=", StringComparison.Ordinal)) coreDir = arg.Substring("--core=".Length);
            else if (!arg.StartsWith("--", StringComparison.Ordinal)) interopDir = arg;
        }
        const string defaultRoot = @"E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx";
        interopDir ??= Path.Combine(defaultRoot, "interop");
        coreDir ??= Path.Combine(defaultRoot, "core");

        Check(candidatePath != null && File.Exists(candidatePath), "--candidate=<absolute DLL> is required and exists");
        if (candidatePath == null || !File.Exists(candidatePath)) return Finish();

        _resolver = new DefaultAssemblyResolver();
        _resolver.AddSearchDirectory(interopDir);
        _resolver.AddSearchDirectory(coreDir);
        _resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(candidatePath)));

        _game = AssemblyDefinition.ReadAssembly(Path.Combine(interopDir, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = _resolver }).MainModule;
        ModuleDefinition candidate = AssemblyDefinition.ReadAssembly(candidatePath,
            new ReaderParameters { AssemblyResolver = _resolver }).MainModule;

        TypeDefinition persistence = candidate.GetType("KingdomEnhancedMod.CoinCourierPersistence");
        Check(persistence != null, "candidate exposes KingdomEnhancedMod.CoinCourierPersistence");
        if (persistence == null) return Finish();

        var targets = new List<Target>();
        foreach (TypeDefinition nested in persistence.NestedTypes)
        {
            CustomAttribute[] attrs = nested.CustomAttributes
                .Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
            if (attrs.Length == 0) continue; // 非 patch 嵌套类型（SaveScope/绑定实现类）
            Check(attrs.Length == 1, nested.Name + " carries exactly one HarmonyPatch attribute (found " + attrs.Length + ")");
            foreach (CustomAttribute attr in attrs)
            {
                Target target = ReadTarget(nested.Name, attr);
                if (target != null) targets.Add(target);
            }
        }
        Check(targets.Count > 0, "candidate declares at least one Harmony target");

        foreach (Target target in targets) ResolveTarget(target);

        // factory 绝不能是目标 + MoveNext 必须在目标集
        Check(!targets.Any(t => t.DeclaringType == "GlobalSaveData" && t.Method == "_TryDeleteCampaign"),
            "no Harmony target points at the delete coroutine factory");
        Check(targets.Any(t => t.DeclaringType == "GlobalSaveData/__TryDeleteCampaign_d__91" && t.Method == "MoveNext"),
            "the delete state machine MoveNext is a target");

        // 负对照依据：factory 自身参数确证触发 value_box 崩因规则（若被误挂必 FAIL）
        TypeDefinition global = _game.GetType("GlobalSaveData");
        MethodDefinition[] factories = global == null
            ? Array.Empty<MethodDefinition>()
            : global.Methods.Where(m => m.Name == "_TryDeleteCampaign").ToArray();
        Check(factories.Length == 1, "delete coroutine factory exists and is unique (found " + factories.Length + ")");
        if (factories.Length == 1)
        {
            bool risky = false;
            foreach (ParameterDefinition p in factories[0].Parameters)
            {
                ValueTypeVerdict v = DerivesFromIl2CppValueType(p.ParameterType);
                if (v.Error != null) { Check(false, "factory param resolution failed: " + v.Error); continue; }
                if (!(p.ParameterType is ByReferenceType) && v.IsValueTypeDerived) risky = true;
            }
            Check(risky, "factory's by-value parameter reproduces the value_box crash shape (excluded by design)");
        }

        return Finish();
    }

    private sealed class Target
    {
        public string PatchClass;
        public string DeclaringType;
        public string Method;
        public string[] ArgTypes; // null = 未指定重载
        public MethodDefinition Resolved;
    }

    private static Target ReadTarget(string patchClass, CustomAttribute attr)
    {
        int argc = attr.ConstructorArguments.Count;
        if (argc != 2 && argc != 3)
        {
            Check(false, patchClass + ": unsupported HarmonyPatch ctor shape (" + argc + " args); expected (Type,string) or (Type,string,Type[])");
            return null;
        }
        if (!(attr.ConstructorArguments[0].Value is TypeReference typeRef))
        {
            Check(false, patchClass + ": HarmonyPatch first argument is not a Type");
            return null;
        }
        if (!(attr.ConstructorArguments[1].Value is string method))
        {
            Check(false, patchClass + ": HarmonyPatch second argument is not a method name string");
            return null;
        }
        string[] argTypes = null;
        if (argc == 3)
        {
            if (!(attr.ConstructorArguments[2].Value is CustomAttributeArgument[] array))
            {
                Check(false, patchClass + ": HarmonyPatch third argument is not a Type[]");
                return null;
            }
            argTypes = array.Select(a => ((TypeReference)a.Value).FullName).ToArray();
        }
        return new Target
        {
            PatchClass = patchClass,
            DeclaringType = typeRef.FullName,
            Method = method,
            ArgTypes = argTypes,
        };
    }

    private static void ResolveTarget(Target target)
    {
        string label = target.PatchClass + " -> " + target.DeclaringType + "." + target.Method;
        TypeDefinition type = _game.GetType(target.DeclaringType);
        Check(type != null, label + ": target type exists in actual Assembly-CSharp");
        if (type == null) return;

        MethodDefinition[] byName = type.Methods.Where(m => m.Name == target.Method).ToArray();
        MethodDefinition[] matches = target.ArgTypes == null
            ? byName
            : byName.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(target.ArgTypes)).ToArray();
        Check(matches.Length == 1, label + ": resolves to exactly one native wrapper (found " + matches.Length
            + ", overloads=" + byName.Length + ")");
        if (matches.Length != 1) return;
        target.Resolved = matches[0];

        foreach (ParameterDefinition p in target.Resolved.Parameters)
        {
            ValueTypeVerdict v = DerivesFromIl2CppValueType(p.ParameterType);
            if (v.Error != null)
            {
                Check(false, label + " param '" + p.Name + "': " + v.Error);
                continue;
            }
            Check(!(p.ParameterType is ByReferenceType) && !v.IsValueTypeDerived,
                label + " param '" + p.Name + "' (" + p.ParameterType.FullName + ") is not a by-value Il2CppSystem.ValueType-derived type");
        }
        if (target.DeclaringType == "GlobalSaveData/__TryDeleteCampaign_d__91" && target.Method == "MoveNext")
        {
            Check(target.Resolved.ReturnType.FullName == "System.Boolean", label + " returns System.Boolean");
            Check(target.Resolved.Parameters.Count == 0, label + " is parameterless");
            PropertyDefinition state = type.Properties.FirstOrDefault(p => p.Name == "__1__state");
            Check(state != null && state.PropertyType.FullName == "System.Int32",
                label + ": __1__state is int (the only field the prefix reads)");
        }
    }

    private struct ValueTypeVerdict
    {
        public bool IsValueTypeDerived;
        public string Error;
    }

    /// <summary>
    /// 沿 base 链解析判断是否派生自 Il2CppSystem.ValueType；解析失败/中断返回 Error（调用方必须 FAIL），
    /// 绝不把解析失败当安全 false。
    /// </summary>
    private static ValueTypeVerdict DerivesFromIl2CppValueType(TypeReference type)
    {
        var verdict = new ValueTypeVerdict();
        try
        {
            TypeDefinition definition = type.Resolve();
            while (definition != null)
            {
                if (definition.FullName == "Il2CppSystem.ValueType") { verdict.IsValueTypeDerived = true; return verdict; }
                TypeReference baseType = definition.BaseType;
                if (baseType == null) return verdict;
                if (baseType.FullName == "Il2CppSystem.ValueType") { verdict.IsValueTypeDerived = true; return verdict; }
                definition = baseType.Resolve();
                if (definition == null)
                {
                    verdict.Error = "base type resolution returned null for " + baseType.FullName;
                    return verdict;
                }
            }
            verdict.Error = "resolution chain ended without a TypeDefinition for " + type.FullName;
            return verdict;
        }
        catch (Exception e)
        {
            verdict.Error = "type resolution failed for " + type.FullName + ": " + e.GetType().Name;
            return verdict;
        }
    }

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (condition)
        {
            Console.WriteLine("PASS " + label);
            return;
        }
        _failures++;
        Console.WriteLine("FAIL " + label);
    }

    private static int Finish()
    {
        Console.WriteLine("SIGNATURE CHECK: " + _checks + " checks, " + _failures + " failures");
        return _failures == 0 ? 0 : 1;
    }
}
