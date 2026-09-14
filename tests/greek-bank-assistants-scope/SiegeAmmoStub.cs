
public class RollableOilBarrel : UnityEngine.Component { }
public class PayableWorkshopBarrel : Payable { public RollableOilBarrel rollableBarrelPrefab; }
public class FireTower : UnityEngine.Component { public object _parentHeaderRef; public int _fireJarsActiveIndex; }
namespace KingdomEnhancedMod
{
 internal static class SiegeAmmoCounts
 {
  internal static bool IsReady => false;
  internal static bool ClientUnavailable => false;
  internal static long Version => 0;
  internal static bool Refresh(Managers managers, bool enabled, float now, bool force=false) => false;
  internal static int Count(int role) => 0;
  internal static int CountAt(Payable payable) => 0;
  internal static int Classify(Payable payable) => -1;
  internal static void GetPayables(int role, System.Collections.Generic.List<Payable> output) { output.Clear(); }
 }
}
