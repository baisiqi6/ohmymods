using System;
using System.IO;
using KingdomEnhancedMod;

static class Check
{
    static int _checks;
    static void True(bool actual, string label)
    {
        if (!actual) throw new Exception(label);
        _checks++;
    }

    static void Main()
    {
        var ledger = new HeavyShieldIssuedToolLedger();
        var key = new HeavyShieldIssuedToolLedger.Key(11, 22, 33);
        var issue = Guid.NewGuid();
        ledger.SetWorld(33);
        True(!ledger.IsIssued(key), "ordinary Shield is not issued");
        True(!ledger.Register(key with { World = 44 }, issue, out _), "foreign world refused");
        True(!ledger.Register(key, Guid.Empty, out _), "missing receipt refused");
        True(ledger.Register(key, issue, out var first), "issued once");
        True(!ledger.Register(key, Guid.NewGuid(), out _), "same pointer cannot register twice");
        True(ledger.IsIssued(key), "live issued tool recognized");
        True(!ledger.IsIssued(key with { InstanceId = 23 }), "pointer alone cannot authorize");
        True(ledger.TryBegin(key, out var captured) && captured == first, "first attempt consumes exact lease");
        True(!ledger.TryBegin(key, out _), "reentrant or failed attempt cannot replay");
        True(ledger.IsSameLife(first), "same life remains until pool recycle");
        ledger.Recycle(11, 23);
        True(ledger.IsSameLife(first), "wrong instance cannot end lease");
        ledger.Recycle(11, 22);
        True(!ledger.IsSameLife(first), "confirmed recycle ends lease");
        True(!ledger.Register(key, issue, out _), "spent issue cannot be reissued");
        True(ledger.Register(key, Guid.NewGuid(), out var second) && second.Life > first.Life,
            "new issue gets new life");
        ledger.Spawn(11, false);
        True(ledger.IsIssued(key), "cached active FastSpawn cannot invent a new life");
        ledger.Spawn(11, true);
        True(!ledger.IsIssued(key), "proven fresh pool life revokes even same pointer and instance id");
        ledger.SetWorld(44);
        True(!ledger.IsSameLife(second), "world replacement revokes old lease");
        True(!ledger.Register(key with { World = 44 }, issue, out _),
            "same issue cannot replay in another world");
        True(ledger.RegisterRestored(key with { World = 44 }, issue, out var restored)
            && restored.Issue == issue, "verified native load may rebind one paid receipt in a new world");
        True(!ledger.RegisterRestored(key with { World = 44, Pointer = 99 }, issue, out _),
            "restored receipt cannot bind a second tool in the same world");
        var newKey = key with { World = 44, Pointer = 12 };
        True(ledger.Register(newKey, Guid.NewGuid(), out _), "new world can issue a distinct tool");
        ledger.Revoke(key with { World = 33 });
        True(ledger.IsIssued(newKey), "stale world cannot revoke new issue");
        ledger.Revoke(newKey);
        True(!ledger.IsIssued(newKey), "issuer can revoke exact live tool");

        // The existing native owner invokes callbacks; this bridge adds no second hook.
        var ancestor = new DirectoryInfo(AppContext.BaseDirectory);
        while (ancestor != null && !File.Exists(Path.Combine(ancestor.FullName,
            "il2cpp", "HeavyShieldPromotionBridge.cs"))) ancestor = ancestor.Parent;
        if (ancestor == null) throw new DirectoryNotFoundException("production bridge source ancestor");
        var bridgePath = Path.Combine(ancestor.FullName, "il2cpp", "HeavyShieldPromotionBridge.cs");
        var source = File.ReadAllText(bridgePath);
        True(source.Contains("internal static void Before(Character source")
            && source.Contains("internal static void Finally(PromotionState state)"),
            "per-call callbacks cover native Promote through finalizer");
        True(source.Contains("MusketeerIdentity.GunPromotionInProgress"),
            "paid gun promotion is excluded");
        True(source.Contains("MusketeerIdentity.IsUnit"),
            "paid musketeer Archer is excluded");
        True(!source.Contains("HarmonyPatch") && !source.Contains("Character.Professions[")
            && !source.Contains("tagCharacterPairs[")
            && !source.Contains("ref Character __result") && !source.Contains(".Promote(\"Archer\""),
            "no duplicate hooks or replacement promotion");
        Console.WriteLine("PASS " + _checks + " ledger assertions");
    }
}
