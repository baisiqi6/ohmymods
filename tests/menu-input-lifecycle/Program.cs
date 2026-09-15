using System;
using System.Linq;
using KingdomEnhancedMod;
using Rewired;

internal static class Program
{
    private static int passed, failed;

    private static void Check(bool condition, string why)
    {
        if (!condition)
            throw new Exception(why);
    }

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message);
        }
    }

    private static void Ready(bool ready, bool throwOnRead = false)
    {
        ReInput.Ready = ready;
        ReInput.ThrowOnRead = throwOnRead;
    }

    private static int LogCount() => KingdomEnhancedPlugin.Instance.LogSource.Lines.Count;

    private static int SkipLines() =>
        KingdomEnhancedPlugin.Instance.LogSource.Lines.Count(l => l.Contains("[MenuInputGuard] skipped"));

    private static int FailLines() =>
        KingdomEnhancedPlugin.Instance.LogSource.Lines.Count(l => l.Contains("readiness read failed"));

    private static void Main()
    {
        Test("enabled=true stays vanilla and never probes Rewired", () =>
        {
            var menu = new Menu();
            int writes = Menu.InputWrites, reads = ReInput.Reads, logs = LogCount();
            Ready(true);
            Check(GuardHarness.RunSetMenuInput(menu, enabled: true), "enable path must keep the original body");
            Check(Menu.InputWrites == writes + 1, "vanilla input write still happens when Rewired is ready");
            Check(ReInput.Reads == reads, "enable path must not read Rewired.isReady");
            Check(LogCount() == logs, "enable path must not log a guard diagnostic");

            Ready(false, throwOnRead: true);
            Check(GuardHarness.Decide(true), "enabled=true stays vanilla even when readiness is unreadable");
            Check(ReInput.Reads == reads, "enabled=true must not probe the unreadable readiness flag");
            Check(LogCount() == logs, "no diagnostic may appear on the enable path");
        });

        Test("ready close still runs the vanilla input write", () =>
        {
            var menu = new Menu();
            int writes = Menu.InputWrites, failed = Menu.FailedWrites, skips = MenuInputGuard.Skips, logs = SkipLines();
            Ready(true);
            Check(GuardHarness.RunSetMenuInput(menu, enabled: false), "ready close must keep the original body");
            Check(Menu.InputWrites == writes + 1 && Menu.FailedWrites == failed, "the map write reaches Rewired");
            Check(MenuInputGuard.Skips == skips && SkipLines() == logs, "a ready close is never skipped");
        });

        Test("not-ready close skips only the dead input write", () =>
        {
            var menu = new Menu();
            Ready(false);
            int writes = Menu.InputWrites, failed = Menu.FailedWrites, skips = MenuInputGuard.Skips, logs = SkipLines();
            Check(!GuardHarness.RunSetMenuInput(menu, enabled: false), "not-ready close must skip the original body");
            Check(Menu.InputWrites == writes, "no input write may reach Rewired");
            Check(Menu.FailedWrites == failed, "the throwing write must not be attempted at all");
            Check(MenuInputGuard.Skips == skips + 1, "the skip is counted");
            Check(SkipLines() == logs + 1, "exactly one bounded skip line for the first skip");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Any(l => l.Contains("[MenuInputGuard] skipped Menu.SetMenuInput(false)")),
                "the line names the guarded call");
        });

        Test("repeated not-ready closes keep the log bounded", () =>
        {
            var menu = new Menu();
            Ready(false);
            int writes = Menu.InputWrites, skips = MenuInputGuard.Skips, logs = SkipLines();
            for (int i = 0; i < 8; i++)
                Check(!GuardHarness.RunSetMenuInput(menu, enabled: false), "every not-ready close is skipped");
            Check(MenuInputGuard.Skips == skips + 8, "every skip is counted");
            Check(SkipLines() == logs, "consecutive closes add no further log line");
            Check(Menu.InputWrites == writes && Menu.FailedWrites == 0, "no Rewired write was attempted");
        });

        Test("negative control: the unguarded not-ready write is the observed failure", () =>
        {
            Ready(false);
            int writes = Menu.InputWrites, failed = Menu.FailedWrites;
            bool threw = false;
            try
            {
                new Menu().SetMenuInput(false);
            }
            catch (NullReferenceException)
            {
                threw = true;
            }

            Check(threw, "the unguarded stub must reproduce the Player.log 4741 NullReferenceException");
            Check(Menu.FailedWrites == failed + 1 && Menu.InputWrites == writes, "the failing path reached Rewired once");
        });

        Test("readiness read failure fails open to vanilla with one diagnostic", () =>
        {
            var menu = new Menu();
            Ready(true, throwOnRead: true);
            int writes = Menu.InputWrites, skips = MenuInputGuard.Skips, failures = MenuInputGuard.ReadFailures, logs = FailLines();
            Check(GuardHarness.RunSetMenuInput(menu, enabled: false), "an unreadable readiness flag must keep the original body");
            Check(Menu.InputWrites == writes + 1, "the vanilla write still happens");
            Check(MenuInputGuard.Skips == skips, "a read failure is never treated as not-ready");
            Check(MenuInputGuard.ReadFailures == failures + 1, "the read failure is counted");
            Check(FailLines() == logs + 1, "exactly one bounded diagnostic");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Any(l => l.Contains("[MenuInputGuard] Rewired readiness read failed; running vanilla Menu.SetMenuInput(false)")),
                "the diagnostic states the fail-open decision");

            for (int i = 0; i < 3; i++)
                Check(GuardHarness.RunSetMenuInput(menu, enabled: false), "later read failures also fail open");
            Check(FailLines() == logs + 1, "consecutive read failures stay bounded");
            Check(MenuInputGuard.Skips == skips, "no silent skip in the failure path");
        });

        Test("OnDisable model: only the input write is skipped, remaining cleanup runs", () =>
        {
            Ready(false);
            var menu = new Menu();
            int writes = Menu.InputWrites, failed = Menu.FailedWrites, skips = MenuInputGuard.Skips;
            menu.OnDisableModel();
            Check(menu.UpdateEffectStops == 1, "the update-effect stop must still run");
            Check(menu.MaterialReleases == 1, "the fullscreen material release must still run");
            Check(menu.HandlerUnsubscribes == 1, "the handler unsubscribe must still run");
            Check(Menu.InputWrites == writes && Menu.FailedWrites == failed, "the dead input write was not attempted");
            Check(MenuInputGuard.Skips == skips + 1, "the input write was skipped by the guard");

            var patches = typeof(Menu_SetMenuInput_MenuInputGuard_Patch)
                .GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), inherit: false)
                .Cast<HarmonyLib.HarmonyPatch>()
                .ToArray();
            Check(patches.Length == 1, "the guard declares exactly one patch");
            Check(patches[0].Target == typeof(Menu) && patches[0].Name == "SetMenuInput",
                "the only hook is Menu.SetMenuInput");
            Check(patches.All(p => p.Name != "OnDisable"), "Menu.OnDisable is never hooked");
        });

        Test("ShouldRun truth table agrees with the prefix decision", () =>
        {
            Check(MenuInputGuard.ShouldRun(true, true) && MenuInputGuard.ShouldRun(true, false),
                "enabled=true always runs the vanilla body");
            Check(MenuInputGuard.ShouldRun(false, true), "a ready close runs the vanilla body");
            Check(!MenuInputGuard.ShouldRun(false, false), "a proven not-ready close skips");

            foreach (bool ready in new[] { true, false })
            {
                Ready(ready);
                Check(GuardHarness.Decide(false) == MenuInputGuard.ShouldRun(false, ready),
                    "prefix follows the helper for enabled=false (ready=" + ready + ")");
                Check(GuardHarness.Decide(true) == MenuInputGuard.ShouldRun(true, ready),
                    "prefix follows the helper for enabled=true (ready=" + ready + ")");
            }
        });

        Console.WriteLine("RESULT passed=" + passed + " failed=" + failed);
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
