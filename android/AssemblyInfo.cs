// Suppresses MelonLoader's automatic per-type Harmony scan for this assembly.
//
// The deployed LemonLoader runtime (MelonLoader 0.7.3, net6 loader directory) reads this
// assembly-level attribute in MelonAssembly.LoadMelons and MelonBase.HarmonyInit short-circuits
// before CreateClassProcessor(type, false).Patch(); the attribute and that consumption path were
// verified against the deployed MelonLoader.dll build (Cecil metadata + IL, see
// .local/tasks/android-port-continuation-20261003/recon/report.md section 2).
//
// The linked shared source il2cpp/PatchPlayer_HoldPurchase.cs keeps its [HarmonyPatch] /
// [HarmonyPrefix] / [HarmonyPostfix] / [HarmonyFinalizer] annotations, but nothing auto-applies
// them: the Operator registers each handler exactly once with an explicit method name/signature
// (see result.md "Operator integration points"), so every target has a single registration path
// whose device-side count is checked with Harmony.GetPatchInfo.
using MelonLoader;

[assembly: HarmonyDontPatchAll]
