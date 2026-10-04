// Maps the bare game type names used by the shared desktop sources
// (il2cpp/PatchPlayer_HoldPurchase.cs, il2cpp/PatchWorld_EnemyManager.cs,
// il2cpp/PatchWorld_FarmCats.cs, il2cpp/FarmCatMovement.cs, il2cpp/GreekScaleScope.cs;
// Windows interop keeps game types in the global namespace) onto the Android interop names.
//
// Android interop (Il2CppInterop 1.5.1, namespace-prefix mode) puts global-namespace
// Assembly-CSharp types under "Il2Cpp."; UnityEngine.* namespaces are preserved.
// File-local aliases with a name that duplicates one of these global aliases would be
// CS1537 ("using alias previously appeared"), so those local alias lines are removed from
// the adapted Android copies; every other local alias stays.
//
// BiomeHolder / Mover / Character / Kingdom / Game are consumed by the FarmCats block but
// cannot be declared here as global aliases: the adapted copies OptionalQoLScope.cs,
// PatchWorld_Mover.cs and PopulationCounts.cs still declare file-local aliases with those
// names (CS1537, verified). The global namespace import resolves them instead; explicit
// aliases still win over it where both exist.
global using Il2Cpp;
global using Baker = Il2Cpp.Baker;
global using BiomeData = Il2Cpp.BiomeData;
global using Cat = Il2Cpp.Cat;
global using CurrencyType = Il2Cpp.CurrencyType;
global using Droppable = Il2Cpp.Droppable;
global using Embarkee = Il2Cpp.Embarkee;
global using EnemyManager = Il2Cpp.EnemyManager;
global using Farmhouse = Il2Cpp.Farmhouse;
global using Farmland = Il2Cpp.Farmland;
global using FireTower = Il2Cpp.FireTower;
global using GAPS = Il2Cpp.GAPS;
global using Holder = Il2Cpp.Holder;
global using IslandSaveData = Il2Cpp.IslandSaveData;
global using Managers = Il2Cpp.Managers;
global using NetworkBigBoss = Il2Cpp.NetworkBigBoss;
global using Payable = Il2Cpp.Payable;
global using PayableComponent = Il2Cpp.PayableComponent;
global using PayableWorkshopBarrel = Il2Cpp.PayableWorkshopBarrel;
global using Player = Il2Cpp.Player;
global using Pool = Il2Cpp.Pool;
global using Side = Il2Cpp.Side;
global using StateMachine = Il2Cpp.StateMachine;
global using Wave = Il2Cpp.Wave;
global using World = Il2Cpp.World;
