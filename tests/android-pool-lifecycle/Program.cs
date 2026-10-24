using System;
using System.Reflection;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _failures;
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        _checks++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) _failures++;
    }
    private static void Hook(Type type, string method, params object[] args)
        => type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
    private static void Prefix() => Hook(typeof(AndroidProfessionPoolLifecycle), "InitPoolsPrefix");
    private static void Returned(PoolManager pm) => Hook(typeof(AndroidProfessionPoolLifecycle), "InitPoolsPostfix", pm);
    private static void Activated(PoolManager pm) => Hook(typeof(AndroidProfessionPoolActivation), "Postfix");
    private static PoolManager SetUp(bool active = true)
    {
        TestTotals.Reset();
        LogSource.ThrowOnInfo = false;
        ModConfig.Enabled.Value = true;
        Managers.Inst = new Managers();
        var pm = Managers.Inst.pools = new PoolManager((IntPtr)0x1000) { isActiveAndEnabled = active };
        Prefix();
        return pm;
    }
    public static int Main()
    {
        var pm = SetUp();
        Activated(pm);
        Check(TestTotals.Writes == 0, "activation cannot authorize without natural completion");
        Check(!AndroidProfessionPoolLifecycle.CanRegisterCustomPools(pm), "initial writer is denied");

        pm = SetUp(active: false);
        Returned(pm);
        Check(TestTotals.Writes == 0, "inactive completion creates no custom pools");
        Check(!AndroidProfessionPoolLifecycle.CanRegisterCustomPools(pm), "inactive writer remains denied");
        pm.isActiveAndEnabled = true;
        Activated(pm);
        Check(TestTotals.Writes == 3, "activation consumes completion recorded while inactive");

        pm = SetUp();
        Returned(pm);
        Check(TestTotals.Writes == 3, "natural active completion reaches three family registrars");
        Check(AndroidProfessionPoolLifecycle.CanRegisterCustomPools(pm), "current initialized Unity PM is accepted");
        Prefix();
        // Simulates original throwing: Harmony does not call the postfix. The real
        // exception boundary is reviewed separately; the linked prefix must revoke.
        Activated(pm);
        Check(TestTotals.Writes == 3, "outer activation cannot authorize after failed rebuild");
        Check(!AndroidProfessionPoolLifecycle.CanRegisterCustomPools(pm), "failed rebuild retains no old receipt");
        Returned(pm);
        Check(TestTotals.Writes == 6, "later successful rebuild can authorize again");

        pm = SetUp();
        Returned(pm);
        var otherUnityObjectSamePointer = new PoolManager(pm.Pointer);
        Managers.Inst.pools = otherUnityObjectSamePointer;
        Check(!AndroidProfessionPoolLifecycle.CanRegisterCustomPools(otherUnityObjectSamePointer),
            "same pointer cannot transfer receipt to a different Unity instance");
        Check(!AndroidProfessionPoolLifecycle.CanRegisterCustomPools(pm),
            "current global PM must match the initialized Unity instance");
        var anotherWrapper = new PoolManager(pm.Pointer, pm.UnityId);
        Managers.Inst.pools = anotherWrapper;
        Check(AndroidProfessionPoolLifecycle.CanRegisterCustomPools(anotherWrapper),
            "equivalent Unity instance is accepted across managed wrappers");
        pm.Destroyed = true;
        Check(!AndroidProfessionPoolLifecycle.CanRegisterCustomPools(anotherWrapper),
            "destroyed receipt owner is rejected");

        pm = SetUp();
        var futureCurrent = new PoolManager((IntPtr)0x2000);
        Returned(futureCurrent);
        Check(TestTotals.Writes == 0, "noncurrent native builder cannot write custom pools");
        Managers.Inst.pools = futureCurrent;
        Activated(futureCurrent);
        Check(TestTotals.Writes == 3, "native builder fact survives until current ownership handoff");

        pm = SetUp();
        ModConfig.Enabled.Value = false;
        Returned(pm);
        Check(TestTotals.Writes == 0, "disabled mod does not write custom pools");
        ModConfig.Enabled.Value = true;
        Activated(pm);
        Check(TestTotals.Writes == 3, "mod enable consumes existing native completion");

        pm = SetUp();
        LogSource.ThrowOnInfo = true;
        Returned(pm);
        LogSource.ThrowOnInfo = false;
        Check(AndroidProfessionPoolLifecycle.CanRegisterCustomPools(pm),
            "diagnostic failure cannot revoke an original native success");

        Console.WriteLine($"RESULT checks={_checks} failures={_failures}");
        return _failures == 0 ? 0 : 1;
    }
}
