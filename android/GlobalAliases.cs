// Maps the bare game type names used by the shared desktop sources
// (il2cpp/PatchPlayer_HoldPurchase.cs, il2cpp/PatchWorld_EnemyManager.cs; Windows interop
// keeps game types in the global namespace) onto the Android interop names.
//
// Android interop (Il2CppInterop 1.5.1, namespace-prefix mode) puts global-namespace
// Assembly-CSharp types under "Il2Cpp."; UnityEngine.* namespaces are preserved.
// File-local aliases with a name that duplicates one of these global aliases would be
// CS1537 ("using alias previously appeared"), so those local alias lines are removed from
// the adapted Android copies; every other local alias stays.
global using Baker = Il2Cpp.Baker;
global using CurrencyType = Il2Cpp.CurrencyType;
global using EnemyManager = Il2Cpp.EnemyManager;
global using FireTower = Il2Cpp.FireTower;
global using Managers = Il2Cpp.Managers;
global using NetworkBigBoss = Il2Cpp.NetworkBigBoss;
global using Payable = Il2Cpp.Payable;
global using PayableComponent = Il2Cpp.PayableComponent;
global using PayableWorkshopBarrel = Il2Cpp.PayableWorkshopBarrel;
global using Player = Il2Cpp.Player;
global using Wave = Il2Cpp.Wave;
global using World = Il2Cpp.World;
