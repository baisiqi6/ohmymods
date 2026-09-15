
public class RollableOilBarrel : UnityEngine.Component { }
public class PayableWorkshopBarrel : Payable { public RollableOilBarrel rollableBarrelPrefab; }
public class IPayableComponentOwner : UnityEngine.Behaviour { }
public class PayableComponent : Payable { public IPayableComponentOwner _owner; }
public class FireTower : IPayableComponentOwner
{
 public object _parentHeaderRef; public int _fireJarsActiveIndex;
 public int _maxFireJars; public int _fireJarsActiveNum;
 public UnityEngine.GameObject[] _fakeFireJars;
}
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
