using System.Collections;
using System.Reflection;

// Load a genuinely compiled product DLL; invoke only the native-free request/rules types.
// No game stubs and no Unity/Harmony initialization or original/native execution.
if (args.Length != 1) throw new ArgumentException("Pass the absolute product DLL path.");
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
Type TypeOf(string name) => assembly.GetType("KingdomEnhancedMod." + name, throwOnError: true)!;
var rules = TypeOf("HeavyShieldBannerRules");
var requestType = TypeOf("HeavyShieldBannerRequest");
var candidateType = TypeOf("HeavyShieldBannerCandidate");
var tokenType = TypeOf("HeavyShieldCampaignToken");
var phaseType = TypeOf("HeavyShieldIdentityPhase");
var checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
object? Call(Type type, object? target, string method, params object?[] values) =>
    (type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        ?? throw new MissingMethodException(type.FullName, method)).Invoke(target, values);
object? Rule(string method, params object?[] values) => Call(rules, null, method, values);
object? Req(object target, string method, params object?[] values) => Call(requestType, target, method, values);
object NewRequest() => Activator.CreateInstance(requestType, nonPublic: true)!;
Guid Receipt(int id) => new(id, 0, 0, new byte[8]);
object Candidate(int id, long life = 1, int side = 1, float x = 0, bool eligible = true) =>
    Activator.CreateInstance(candidateType, Receipt(id), life, side, x, eligible)!;
Array Roster(params object[] candidates)
{
    var array = Array.CreateInstance(candidateType, candidates.Length);
    for (var i = 0; i < candidates.Length; i++) array.SetValue(candidates[i], i);
    return array;
}
object Token(string guid = "campaign", int global = 1, int campaign = 2, long owner = 3, int phase = 3) =>
    Activator.CreateInstance(tokenType, guid, (IntPtr)global, (IntPtr)campaign, owner, Enum.ToObject(phaseType, phase))!;
object? Field(object target, string name) => requestType.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
void Begin(object target, long activation = 1, int side = 1) => Req(target, "Begin", activation, 22L, 33L, side, 10f);
void Select(object target, Array roster, Guid excluded = default, long life = 0) => Req(target, "Select", roster, excluded, life);

Check((int)Rule("FrontSlot", 12, 8, 11)! == 8, "living pike outranks further other unit");
Check((int)Rule("FrontSlot", 12, 11, 8)! == 11, "highest live pike");
Check((int)Rule("FrontSlot", 12, -1, 6)! == 6, "occupied fallback");
Check((int)Rule("FrontSlot", 12, -1, -1)! == 11, "empty fallback uses last legitimate slot");
foreach (var count in new[] { 0, -1, 129 }) Check((int)Rule("FrontSlot", count, -1, -1)! == -1, "bounded layout");
Check((int)Rule("FrontSlot", 12, 12, -1)! == -1, "invalid pike index rejected");
Check((int)Rule("FrontSlot", 12, -1, 12)! == -1, "invalid occupied index rejected");
foreach (var speed in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
    Check((float)Rule("Speed", speed)! == 0, "invalid override requests walk speed");
Check((float)Rule("Speed", 2.2f)! == 2.2f, "positive override preserved");
Check((bool)Rule("SameOwner", Token(phase: 1), Token(phase: 2))!, "save phase is not lineage");
foreach (var other in new[] { Token(guid: "other"), Token(global: 9), Token(campaign: 9), Token(owner: 9) })
    Check(!(bool)Rule("SameOwner", Token(), other)!, "every owner identity field matters");
var session = new object();
Check((bool)Rule("SameContext", session, Token(), 33L, 0, session, Token(), 33L, 0)!, "reobserving the captured scope preserves it");
Check((bool)Rule("SameContext", session, Token(phase: 3), 33L, 0, session, Token(phase: 2), 33L, 0)!, "save phase preserves the installed session scope");
Check(!(bool)Rule("SameContext", session, Token(), 33L, 0, new object(), Token(), 33L, 0)!, "same-world same-land reload session is a new scope");
Check(!(bool)Rule("SameContext", session, Token(), 33L, 0, session, Token(), 33L, 1)!, "land alone changes scope");
Check(!(bool)Rule("SameContext", session, Token(), 33L, 0, session, Token(), 34L, 0)!, "world alone changes scope");
Check(!(bool)Rule("SameContext", session, Token(), 33L, 0, session, Token(owner: 4), 33L, 0)!, "owner generation changes scope");
Check(!(bool)Rule("SameContext", null, Token(), 33L, 0, null, Token(), 33L, 0)!, "missing session is never a valid scope");
Check(!(bool)Rule("SameContext", session, Token(), 33L, -1, session, Token(), 33L, -1)!, "unknown land is never a valid scope");
Check(!(bool)Rule("SameContext", session, Token(), 0L, 0, session, Token(), 0L, 0)!, "missing world is never a valid scope");
var advance = new object?[] { 0L, 0L };
Check((bool)Rule("TryNextActivation", advance)! && (long)advance[0]! == 1 && (long)advance[1]! == 1, "first token");
Check((bool)Rule("TryNextActivation", advance)! && (long)advance[0]! == 2 && (long)advance[1]! == 2, "strictly growing tokens");
var overflow = new object?[] { long.MaxValue, 7L };
Check(!(bool)Rule("TryNextActivation", overflow)! && (long)overflow[0]! == long.MaxValue && (long)overflow[1]! == 0, "overflow never wraps");

var first = NewRequest();
Begin(first);
Check((bool)Field(first, "Pending")!, "capture is pending until eligible selection");
Select(first, Roster(Candidate(1, x: 8), Candidate(2, x: 9)), Receipt(2), 1);
Check((Guid)Field(first, "Receipt")! == Receipt(1), "other player's exact escort excluded");
Check(!(bool)Field(first, "Pending")!, "selection consumed");
Check((bool)Req(first, "Observe", Roster(Candidate(1, x: 8, eligible: false)))!, "assigned eligibility does not release its own banner");
Check(!(bool)Req(first, "Observe", Roster(Candidate(1, life: 2)))!, "life replacement ends assignment");
Select(first, Roster(Candidate(2)));
Check((Guid)Field(first, "Receipt")! == Guid.Empty, "lost escort cannot refill");
Begin(first, 2);
Select(first, Roster());
Select(first, Roster(Candidate(1)));
Check((Guid)Field(first, "Receipt")! == Guid.Empty, "empty activation cannot refill");
Begin(first, 3);
Select(first, Roster(Candidate(1)));
Req(first, "End");
Begin(first, 3);
Select(first, Roster(Candidate(2)));
Check((Guid)Field(first, "Receipt")! == Guid.Empty, "end retains activation watermark");
Begin(first, 4);
Select(first, Roster(Candidate(2)));
Check((Guid)Field(first, "Receipt")! == Receipt(2), "fresh activation may pick again");
Check(!(bool)Req(first, "Observe", Roster(Candidate(2), Candidate(2)))!, "ambiguous current receipt ends binding");
Begin(first, 5, -1);
Select(first, Roster(Candidate(1, side: -1), Candidate(2, side: 1)));
Check((Guid)Field(first, "Receipt")! == Receipt(1), "left side request stays left");
Check(!(bool)Req(first, "Observe", Roster(Candidate(1, side: 1)))!, "home side mismatch ends binding");
Console.WriteLine($"PASS {checks} assertions against actual product managed leaf; native execution NOT performed.");
