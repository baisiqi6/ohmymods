using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Read-only audit of the shipped 2.4 IL2CPP interop metadata for the paid-musketeer drop
// boundary. It captures the evidence chain behind removing the Character.DropItem Harmony
// detour (the live NREs at DMD<Character::DropItem>):
//
//  1. Character.DropItem's only parameter is Il2CppSystem.Nullable<UnityEngine.Vector2> and the
//     game calls DropItem(null) on the grab/death paths (game-source Assembly-CSharp-2.1.0
//     Character.Grab / HandleOnReceiveDamage; the 2.4 signature is identical).
//  2. In IL2CPP a null Nullable<T> is a genuinely null object pointer (Il2CppInterop issue
//     #182), and the interop wrapper is a reference type (Il2CppObjectBase-derived) whose
//     construction from IntPtr.Zero throws NullReferenceException.
//  3. The generated interop stub for DropItem converts that argument through
//     Il2CppObjectBaseToPtrNotNull, which throws NullReferenceException for a null wrapper
//     (Il2CppInterop v1.5.1, IL2CPP.cs).
//  4. HarmonyX's IL2CPP patcher copies exactly this stub into its generated patch method
//     (Il2CppDetourMethodPatcher.CopyOriginal -> DMD<...>), so a Harmony detour on DropItem
//     throws inside the generated wrapper before the native body runs and swallows every
//     native DropItem(null) call.
//
// This project only reads metadata; it never loads the game or the IL2CPP runtime. It fails if
// the shipped interop no longer shows the null-hostile conversion, which would mean the
// diagnosis must be re-derived before that boundary is ever detoured again.
internal static class Program
{
    private static int failed;

    private static void Check(bool ok, string message)
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + message);
        if (!ok) failed++;
    }

    private static int Main(string[] args)
    {
        string deps = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("KEM_IL2CPP_DEPS")
              ?? @"E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx";
        string interopDir = Path.Combine(deps, "interop");
        string assemblyPath = Path.Combine(interopDir, "Assembly-CSharp.dll");
        if (!File.Exists(assemblyPath))
        {
            Console.WriteLine("cannot verify: " + assemblyPath + " is missing");
            Console.WriteLine("pass the BepInEx directory (containing interop/) as the first argument or set KEM_IL2CPP_DEPS");
            return 2;
        }

        using (var assembly = AssemblyDefinition.ReadAssembly(assemblyPath))
        {
            var module = assembly.MainModule;
            var character = module.GetType("Character");
            Check(character != null, "Character type present in Assembly-CSharp interop");
            var methods = character == null
                ? new List<MethodDefinition>()
                : character.Methods.Where(m => m.Name == "DropItem").ToList();
            Check(methods.Count == 1 && methods[0].Parameters.Count == 1, "exactly one Character.DropItem with one parameter");
            var dropItem = methods.FirstOrDefault(m => m.Parameters.Count == 1);

            string paramType = dropItem == null ? "(missing)" : dropItem.Parameters[0].ParameterType.FullName;
            Console.WriteLine("      direction parameter: " + paramType);
            Check(paramType == "Il2CppSystem.Nullable`1<UnityEngine.Vector2>", "direction parameter is Il2CppSystem.Nullable<UnityEngine.Vector2>");
            Check(dropItem != null && dropItem.ReturnType.FullName == "Droppable", "DropItem returns Droppable");

            var body = dropItem == null ? null : dropItem.Body;
            Check(body != null, "DropItem interop stub has an IL body");
            bool nullHostile = false;
            if (body != null)
            {
                for (int i = 1; i < body.Instructions.Count; i++)
                {
                    if (body.Instructions[i].OpCode != OpCodes.Call) continue;
                    if (body.Instructions[i].Operand is not MethodReference target) continue;
                    if (target.Name != "Il2CppObjectBaseToPtrNotNull") continue;
                    if (LoadedArgIndex(body.Instructions[i - 1]) != 1) continue;
                    nullHostile = true;
                    Console.WriteLine("      direction conversion: " + body.Instructions[i - 1] + " -> " + target.FullName);
                }
            }
            Check(nullHostile, "DropItem stub converts direction with Il2CppObjectBaseToPtrNotNull (throws for a null Nullable)");
        }

        var wrapper = InspectNullableWrapper(interopDir);
        Check(wrapper.found, "Il2CppSystem.Nullable`1 wrapper exists in the interop");
        Check(wrapper.found && !wrapper.isValueType, "Nullable`1 is generated as a reference type (class), not a struct");
        Check(wrapper.found && wrapper.baseType.Contains("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase"), "Nullable`1 derives through its base chain from Il2CppObjectBase");

        Console.WriteLine(failed == 0 ? "PASS drop-interop audit" : "FAIL " + failed + " check(s)");
        return failed == 0 ? 0 : 1;
    }

    private static (bool found, bool isValueType, string baseType) InspectNullableWrapper(string interopDir)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(interopDir);
        resolver.AddSearchDirectory(Path.GetFullPath(Path.Combine(interopDir, "../core")));
        foreach (string candidate in new[] { "Il2Cppmscorlib.dll", "Il2CppSystem.dll", "Assembly-CSharp.dll" })
        {
            string path = Path.Combine(interopDir, candidate);
            if (!File.Exists(path)) continue;
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            var type = assembly.MainModule.GetType("Il2CppSystem.Nullable`1");
            if (type == null) continue;
            Console.WriteLine("      Nullable`1 wrapper found in " + candidate);
            var chain = new List<string>();
            var parent = type.BaseType;
            for (int i=0; parent != null && i<16; i++)
            {
                chain.Add(parent.FullName);
                if (parent.FullName == "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase") break;
                parent = parent.Resolve()?.BaseType;
            }
            Console.WriteLine("      base chain: " + string.Join(" -> ", chain));
            return (true, type.IsValueType, string.Join(" -> ", chain));
        }
        return (false, false, "");
    }

    private static int? LoadedArgIndex(Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldarg_0: return 0;
            case Code.Ldarg_1: return 1;
            case Code.Ldarg_2: return 2;
            case Code.Ldarg_3: return 3;
            case Code.Ldarg_S:
            case Code.Ldarg:
                if (instruction.Operand is ParameterDefinition parameter) return parameter.Index + 1;
                if (instruction.Operand is int index) return index;
                return null;
            default: return null;
        }
    }
}
