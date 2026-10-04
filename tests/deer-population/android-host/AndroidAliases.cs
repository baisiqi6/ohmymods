// Typed ANDROID host alias map: mirrors the production android/GlobalAliases.cs entries the
// shared deer source and the shared harness consume. The shared source itself carries
// file-local aliases for BiomeHolder/Deer/Game/Hind/PopulationController/Steed (exactly like
// the production Android build, where adding them here would be CS1537); Managers/World/
// NetworkBigBoss resolve through these global aliases as they do in production.
global using Il2Cpp;
global using Managers = Il2Cpp.Managers;
global using NetworkBigBoss = Il2Cpp.NetworkBigBoss;
global using World = Il2Cpp.World;
