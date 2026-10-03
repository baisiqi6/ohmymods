// Contract doubles for old-role regression suites. These suites exercise their
// own production files; HeavyShield's identity/payment/runtime suites own the new career.
using System.Collections.Generic;
namespace KingdomEnhancedMod;
internal static class HeavyShieldIdentity
{
    private static readonly HashSet<object> Roots = new();
    internal static bool ShieldPromotionInProgress { get; set; }
    internal static bool IsKnownCareerRoot(object root) => root != null && Roots.Contains(root);
    internal static bool CanNativePickup(object source, object tool) => true;
    internal static void Bind(object root) => Roots.Add(root);
    internal static void Reset() { Roots.Clear(); ShieldPromotionInProgress = false; }
}
internal static class HeavyShieldPersistence
{
    internal class GenerationCapture { }
    internal class CreateCapture { }
    internal class PrepareCapture { }
    internal static bool ShieldLoadInProgress { get; set; }
}
internal static class HeavyShieldPromotionBridge
{
    internal class PromotionState { }
    internal static void Before(object source, object tool, out PromotionState state) { state = null; }
    internal static void After(object result, PromotionState state) { }
    internal static void Finally(PromotionState state) { }
}
internal static class HeavyShieldRuntime
{
    internal static bool CarrierMutationInProgress => false;
}
internal static class HeavyShieldIntegration
{
    internal class SaveScope { }
    internal class LoadScope { }
    internal static void Fault(string point, System.Exception error) { }
    internal static SaveScope BeginSave(int campaign, int land, int challenge) => new();
    internal static void EndSave(SaveScope scope, bool normal) { }
    internal static void ObserveId(object root, string id) { }
    internal static void ObserveMarker(object island) { }
    internal static LoadScope BeginLoad(object island) => new();
    internal static void EndLoad(LoadScope scope, bool success) { }
    internal static void ObserveLoadRow(object row, object root) { }
    internal static HeavyShieldPersistence.CreateCapture BeginLoadRow(object row) => new();
    internal static void EndLoadRow(HeavyShieldPersistence.CreateCapture scope) { }
    internal static HeavyShieldPersistence.PrepareCapture BeginPrepare(object prefs) => new();
    internal static void EndPrepare(HeavyShieldPersistence.PrepareCapture scope, bool normal) { }
    internal static void BeforeCampaignMutation(object global) { }
    internal static void AfterCampaignCreated(object global, object campaign) { }
    internal static HeavyShieldPersistence.GenerationCapture BeginGeneration(object campaign) => new();
    internal static void EndGeneration(HeavyShieldPersistence.GenerationCapture scope, object campaign, bool success) { }
    internal static void BeginPoolSpawn() { }
    internal static bool InPoolSpawn => false;
    internal static void EndPoolSpawn() { }
    internal static void ObserveArcherEnable(object archer) { }
    internal static void BeforePoolDespawn(object root, float delay) { }
    internal static void AfterPoolDespawn(object root, float delay) { }
}
