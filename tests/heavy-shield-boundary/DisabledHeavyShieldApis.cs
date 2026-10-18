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
    internal class GenerationCapture
    {
        internal int Token;
        internal bool Closed;
    }
    internal class CreateCapture
    {
        internal int Token;
        internal bool Closed;
    }
    internal class PrepareCapture { }
    internal static bool ShieldLoadInProgress { get; set; }
}

// Neutral wiring spy for suites that link the musketeer persistence source but deliberately do
// not link HeavyShield's real algorithm. Every method still behaves exactly like the old no-op
// double; the counters only prove that the shared begin/end callbacks were wired once, balanced
// and closed by the exception finalizer. It is never evidence that HeavyShield itself works.
internal static class HeavyShieldWiringSpy
{
    internal static int GenerationBegins, GenerationEnds, GenerationOpen, GenerationDoubleEnds;
    internal static int LoadBegins, LoadEnds, LoadOpen, LoadDoubleEnds;
    internal static int LoadRowBegins, LoadRowEnds, LoadRowOpen, LoadRowDoubleEnds;
    internal static int SaveBegins, SaveEnds, SaveOpen, SaveDoubleEnds;
    internal static bool? LastGenerationSuccess, LastLoadSuccess, LastLoadRowSuccess, LastSaveSuccess;
    private static int _nextToken;

    internal static void Reset()
    {
        GenerationBegins = GenerationEnds = GenerationOpen = GenerationDoubleEnds = 0;
        LoadBegins = LoadEnds = LoadOpen = LoadDoubleEnds = 0;
        LoadRowBegins = LoadRowEnds = LoadRowOpen = LoadRowDoubleEnds = 0;
        SaveBegins = SaveEnds = SaveOpen = SaveDoubleEnds = 0;
        LastGenerationSuccess = LastLoadSuccess = LastLoadRowSuccess = LastSaveSuccess = null;
        _nextToken = 0;
    }

    internal static int NextToken() => ++_nextToken;

    internal static void BeganGeneration() { GenerationBegins++; GenerationOpen++; }
    internal static void EndedGeneration(int token, bool closed, bool success)
    {
        if (closed) { GenerationDoubleEnds++; return; }
        GenerationEnds++; GenerationOpen--; LastGenerationSuccess = success;
    }

    internal static void BeganLoad() { LoadBegins++; LoadOpen++; }
    internal static void EndedLoad(int token, bool closed, bool success)
    {
        if (closed) { LoadDoubleEnds++; return; }
        LoadEnds++; LoadOpen--; LastLoadSuccess = success;
    }

    internal static void BeganLoadRow() { LoadRowBegins++; LoadRowOpen++; }
    internal static void EndedLoadRow(int token, bool closed)
    {
        if (closed) { LoadRowDoubleEnds++; return; }
        LoadRowEnds++; LoadRowOpen--;
    }

    internal static void BeganSave() { SaveBegins++; SaveOpen++; }
    internal static void EndedSave(int token, bool closed, bool success)
    {
        if (closed) { SaveDoubleEnds++; return; }
        SaveEnds++; SaveOpen--; LastSaveSuccess = success;
    }
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
    internal class SaveScope { internal int Token; internal bool Closed; }
    internal class LoadScope { internal int Token; internal bool Closed; }
    internal static void Fault(string point, System.Exception error) { }
    internal static SaveScope BeginSave(int campaign, int land, int challenge)
    {
        HeavyShieldWiringSpy.BeganSave();
        return new SaveScope { Token = HeavyShieldWiringSpy.NextToken() };
    }
    internal static void EndSave(SaveScope scope, bool normal)
    {
        if (scope == null) { HeavyShieldWiringSpy.EndedSave(0, false, normal); return; }
        HeavyShieldWiringSpy.EndedSave(scope.Token, scope.Closed, normal);
        scope.Closed = true;
    }
    internal static void ObserveId(object root, string id) { }
    internal static void ObserveMarker(object island) { }
    internal static LoadScope BeginLoad(object island)
    {
        HeavyShieldWiringSpy.BeganLoad();
        return new LoadScope { Token = HeavyShieldWiringSpy.NextToken() };
    }
    internal static void EndLoad(LoadScope scope, bool success)
    {
        if (scope == null) { HeavyShieldWiringSpy.EndedLoad(0, false, success); return; }
        HeavyShieldWiringSpy.EndedLoad(scope.Token, scope.Closed, success);
        scope.Closed = true;
    }
    internal static void ObserveLoadRow(object row, object root) { }
    internal static HeavyShieldPersistence.CreateCapture BeginLoadRow(object row)
    {
        HeavyShieldWiringSpy.BeganLoadRow();
        return new HeavyShieldPersistence.CreateCapture { Token = HeavyShieldWiringSpy.NextToken() };
    }
    internal static void EndLoadRow(HeavyShieldPersistence.CreateCapture scope)
    {
        if (scope == null) { HeavyShieldWiringSpy.EndedLoadRow(0, false); return; }
        HeavyShieldWiringSpy.EndedLoadRow(scope.Token, scope.Closed);
        scope.Closed = true;
    }
    internal static HeavyShieldPersistence.PrepareCapture BeginPrepare(object prefs) => new();
    internal static void EndPrepare(HeavyShieldPersistence.PrepareCapture scope, bool normal) { }
    internal static void BeforeCampaignMutation(object global) { }
    internal static void AfterCampaignCreated(object global, object campaign) { }
    internal static HeavyShieldPersistence.GenerationCapture BeginGeneration(object campaign)
    {
        HeavyShieldWiringSpy.BeganGeneration();
        return new HeavyShieldPersistence.GenerationCapture { Token = HeavyShieldWiringSpy.NextToken() };
    }
    internal static void EndGeneration(HeavyShieldPersistence.GenerationCapture scope, object campaign, bool success)
    {
        if (scope == null) { HeavyShieldWiringSpy.EndedGeneration(0, false, success); return; }
        HeavyShieldWiringSpy.EndedGeneration(scope.Token, scope.Closed, success);
        scope.Closed = true;
    }
    internal static void BeginPoolSpawn() { }
    internal static bool InPoolSpawn => false;
    internal static void EndPoolSpawn() { }
    internal static void ObserveArcherEnable(object archer) { }
    internal static void BeforePoolDespawn(object root, float delay) { }
    internal static void AfterPoolDespawn(object root, float delay) { }
}
