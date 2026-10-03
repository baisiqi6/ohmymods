using System;
using System.Reflection;
using KingdomEnhancedMod;

// r3 additions around the save entry freeze: the same failures driven through the real
// SavePatch prefix/postfix/finalizer, plus the no-rights control and the unrelated-save control.
internal static partial class Program
{
    private static void R3Probe()
    {
        ResetArchive();
        var host = Host.Create(1, VirginJsonA, isNew: true, days: 0);
        BootVirginIsland(host);
        var payable = BootShop(2000f, 4000);
        var first = host.CreateArcher("First", Side.Right);
        HeroArcherRuntime.Observe(first.Archer);
        Check(NativePayment(host, payable, true).Gate, "r3-first-paid");
        SavePaidHero(host, first, SavedJsonA, "first-id");
        Check(NativeGlobalSaveAllowed(), "r3-first-checkpoint");
        host.Island.isNew = false; host.Island.playTimeDays = 1;

        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var before = typeof(HeroRecruitment.SavePatch).GetMethod("Before", flags);
        var after = typeof(HeroRecruitment.SavePatch).GetMethod("After", flags);
        var final = typeof(HeroRecruitment.SavePatch).GetMethod("Finally", flags);

        if (_scenario == "r3-before-id-throw")
        {
            // The native Save throws before its first successful GetID: no island capture has
            // frozen owner identity, so the responsibility must come from the save entry point.
            object[] args = { 1, 1, 0, null };
            before.Invoke(null, args);
            host.Island.Json = "{\"land\":1,\"partial\":true}";
            host.Island.objects.Clear();
            IslandSaveData.CurrentlySavingIsland = host.Island; IslandSaveData.isSavingGame = true;
            final.Invoke(null, new object[] { new InvalidOperationException("native save fault"), args[3] });
            IslandSaveData.isSavingGame = false; IslandSaveData.CurrentlySavingIsland = null;
            Check(!NativeGlobalSaveAllowed(), "pre-id-throw-leaves-async-responsibility");
            Check(!NativeCoroutineSaveAllowed(), "pre-id-throw-leaves-sync-responsibility");
            SavePaidHero(host, first, SavedJsonA, "first-id");
            Check(NativeGlobalSaveAllowed(), "same-source-complete-retry-recovers");
            return;
        }
        if (_scenario == "r3-unrelated-no-rights")
        {
            // A failed save of a source with no paid rights must not lock the global save.
            object[] args = { 0, 1, 0, null };
            before.Invoke(null, args);
            after.Invoke(null, new[] { args[3] });
            final.Invoke(null, new object[] { null, args[3] });
            Check(NativeGlobalSaveAllowed(), "no-rights-failed-save-does-not-lock");
            return;
        }
        if (_scenario == "r3-keep-responsibility")
        {
            object[] args = { 1, 1, 0, null };
            before.Invoke(null, args);
            after.Invoke(null, new[] { args[3] });
            final.Invoke(null, new object[] { null, args[3] });
            Check(!NativeGlobalSaveAllowed(), "failed-source-keeps-responsibility");
            // A failing save of an unrelated source neither clears nor duplicates the block.
            object[] other = { 0, 1, 0, null };
            before.Invoke(null, other);
            after.Invoke(null, new[] { other[3] });
            final.Invoke(null, new object[] { null, other[3] });
            Check(!NativeGlobalSaveAllowed(), "unrelated-save-does-not-clear-failed-source");
            host.Island.Json = SavedJsonA; host.Island.objects.Clear();
            SavePaidHero(host, first, SavedJsonA, "first-id");
            Check(NativeGlobalSaveAllowed(), "complete-source-capture-recovers");
            return;
        }
        throw new Exception("unknown r3 case");
    }
}
