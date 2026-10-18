using System;
using KingdomEnhancedMod;

internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = args.Length == 0 ? "normal" : args[0];
        switch (mode)
        {
            case "normal":
                break;
            case "return-byref":
                Il2CppInterop.Runtime.IL2CPP.ReturnByRef = true;
                break;
            case "value-byref":
                Il2CppInterop.Runtime.IL2CPP.ValueByRef = true;
                break;
            case "enum-base-byref":
                Il2CppInterop.Runtime.IL2CPP.EnumBaseByRef = true;
                break;
            case "enum-base-pointer":
                Il2CppInterop.Runtime.IL2CPP.EnumBaseKind = 0x0F;
                break;
            case "enum-base-byref-kind":
                Il2CppInterop.Runtime.IL2CPP.EnumBaseKind = 0x10;
                break;
            default:
                Console.WriteLine("unknown mode " + mode);
                return 2;
        }

        bool ok = MountIslandNativeResult.TryRead(new IslandSaveData.__TryLoad_d__54(),
            out int flags, out string reason);
        int reads = Il2CppInterop.Runtime.IL2CPP.FieldReadCalls;
        int byrefCalls = Il2CppInterop.Runtime.IL2CPP.ByRefApiCalls;
        bool reject = mode != "normal";
        bool pass = reject
            ? !ok && reads == 0 && byrefCalls >= 2
            : ok && reads == 1 && flags == 0x410 && byrefCalls >= 3;

        Console.WriteLine("mode=" + mode
            + " returnByRef=" + Il2CppInterop.Runtime.IL2CPP.ReturnByRef
            + " valueByRef=" + Il2CppInterop.Runtime.IL2CPP.ValueByRef
            + " enumBaseByRef=" + Il2CppInterop.Runtime.IL2CPP.EnumBaseByRef
            + " enumBaseKind=0x" + Il2CppInterop.Runtime.IL2CPP.EnumBaseKind.ToString("X"));
        Console.WriteLine("actual: ok=" + ok + " flags=0x" + flags.ToString("X")
            + " fieldReadCalls=" + reads + " byrefApiCalls=" + byrefCalls
            + " reason=" + reason);
        Console.WriteLine("contract=" + (pass ? "PASS" : "FAIL") + " nativeABI=not-exercised");
        return pass ? 0 : 10;
    }
}
