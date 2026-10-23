using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>One installation path for the palace entrance and its existing travel lifecycle.</summary>
internal static class PalacePlayable
{
    private static bool _attempted, _installationFault;

    internal static void InstallOnce(Harmony harmony)
    {
        if (_attempted) return;
        _attempted = true;
        InstallModule("PalaceVisibleEntry", () => PalaceVisibleEntry.InstallOnce(harmony));
        InstallModule("PalaceLand12Runtime", () => PalaceLand12Runtime.InstallOnce(harmony));
        InstallModule("PalaceCourtArea", () => PalaceCourtArea.InstallOnce(harmony));
        InstallModule("PalaceIslandContentPolicy", () => PalaceIslandContentPolicy.InstallOnce(harmony));
        InstallModule("PalaceIslandTravel", () => PalaceIslandTravel.InstallOnce(harmony));
        InstallModule("PalaceReturnGate", () => PalaceReturnGate.InstallOnce(harmony));
        InstallModule("PalaceArrivalPlacement", () => PalaceArrivalPlacement.InstallOnce(harmony));
        InstallModule("PalaceCourtVisuals", () => PalaceCourtVisuals.InstallOnce(harmony));
        InstallModule("PalaceTerrainRuntime", () => PalaceTerrainRuntime.InstallOnce(harmony));
        InstallModule("PalaceVisitClock", () => { if (!PalaceVisitClock.InstallOnce(harmony)) throw new InvalidOperationException("clock hooks incomplete"); });
        InstallModule("PalaceClockBootstrap", () => { if (!PalaceClockBootstrap.InstallOnce(harmony)) throw new InvalidOperationException("early clock hooks incomplete"); });
        InstallModule("PalacePaidResponsibility", () => { if (!PalacePaidResponsibility.InstallOnce(harmony)) throw new InvalidOperationException("paid hooks incomplete"); });
    }

    private static void InstallModule(string name, Action install)
    {
        try { install(); }
        catch (Exception error)
        {
            _installationFault = true;
            try { PalaceVisibleEntry.Log("ERROR install " + name + ": " + error.GetType().Name
                + "; palace entry remains unavailable for this process"); }
            catch (Exception) { }
        }
    }

    internal static bool TryRequirePaidEntryInstallation(bool forceVerify, out string reason)
    {
        if (_installationFault) { reason = "宫廷组件安装未完成，暂不收取金币"; return false; }
        return PalaceInstallationManifest.TryRequire(forceVerify, out reason);
    }

    internal static bool TryRequirePaidEntryInstallation(out string reason)
        => TryRequirePaidEntryInstallation(false, out reason);
}
