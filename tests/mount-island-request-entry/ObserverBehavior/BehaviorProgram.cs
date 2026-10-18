using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

// Host behaviour driver for the exact Observer source selected by ObserverSource.
// The stubs only provide game-field inputs and a fake native reader; the Observer decision logic
// and the Harmony Prefix/Postfix/Finalizer methods are the real production/frozen source.
internal static class Program
{
    private static readonly List<string> Failures = new List<string>();
    private static readonly BindingFlags HookFlags = BindingFlags.NonPublic | BindingFlags.Static;

    private static void Check(bool condition, string label)
    {
        if (!condition) Failures.Add(label);
    }

    private static IslandSaveData.__TryLoad_d__54 SetupState()
    {
        StubState.TypedGetterCalls = 0;
        StubState.NativeFieldGetCalls = 0;
        StubState.DriftOnRead = false;
        StubState.DriftDone = false;
        StubState.DriftEnvelope = default;

        var global = new GlobalSaveData { Pointer = new IntPtr(0xC0DE), InChallenge = false };
        GlobalSaveData.loaded = global;
        NetworkBigBoss.IsOnline = false;
        IslandSaveData.isSavingGame = false;

        var holder = new BiomeHolder { Pointer = new IntPtr(0xF00D), BiomeIndex = BiomeHolder.GreeceBiomeIndex };
        BiomeHolder.Inst = holder;

        var slot = new IslandSaveData
        {
            Pointer = new IntPtr(0x1000),
            land = 11,
            playTimeDays = 0d,
            lastPlayedTimeDays = 0d,
            objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>(),
            savedWithRevisions = new Il2CppSystem.Collections.Generic.List<int>(),
        };

        var islands = new Il2CppSystem.Collections.Generic.List<IslandSaveData>
        {
            Pointer = new IntPtr(0xCCC0),
        };
        for (int i = 0; i < 14; i++) islands.Add(null);
        islands[11] = slot;

        var landData = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>
        {
            Pointer = new IntPtr(0xBBB0),
        };
        for (int i = 0; i < 14; i++) landData.Add(null);
        landData[11] = new CampaignSaveData.LandMapData { lastPlayedTimeDays = 0d };

        CampaignSaveData.current = new CampaignSaveData
        {
            Pointer = new IntPtr(0xAAA0),
            CurrentLand = 11,
            reign = 2,
            _islands = islands,
            visitedIslands = new Il2CppSystem.Collections.Generic.List<int>(),
            currentReign = new CampaignSaveData.ReignInfo { landData = landData },
        };

        return new IslandSaveData.__TryLoad_d__54
        {
            Pointer = new IntPtr(0x9000),
            __1__state = 0,
            campaignIndex = 0,
            challengeId = 0,
            land = 11,
        };
    }

    private static MountIslandOwnerIdentity Owner()
        => new MountIslandOwnerIdentity
        {
            GlobalPointer = 0xC0DEUL,
            SelectedCampaignIndex = 0,
            CampaignPointer = 0xAAA0UL,
            ReignIndex = 2,
            LandDataPointer = 0xBBB0UL,
            IslandsPointer = 0xCCC0UL,
            HolderInstancePointer = 0xF00DUL,
            HolderBiomeIndex = 1,
            CurrentLand = 11,
        };

    private static MountIslandRequestEnvelope Envelope()
        => new MountIslandRequestEnvelope
        {
            ScopeOk = true,
            RoutinePointer = 0x9000UL,
            RequestCampaignIndex = 0,
            RequestChallengeId = 0,
            RequestLand = 11,
            Owner = Owner(),
            PreSlotPresent = true,
            PreSlotPointer = 0x1000UL,
            PreHistoryReadsOk = true,
            PreHistoryAnyPositive = false,
        };

    private static MoveNextObservation Prefix(IslandSaveData.__TryLoad_d__54 instance, bool runOriginal)
    {
        MethodInfo method = typeof(Patch_MountIslandFreshObserver).GetMethod("Prefix", HookFlags);
        object[] args = { instance, runOriginal, null };
        method.Invoke(null, args);
        return (MoveNextObservation)args[2];
    }

    private static void Postfix(bool result, bool runOriginal, MoveNextObservation state)
    {
        MethodInfo method = typeof(Patch_MountIslandFreshObserver).GetMethod("Postfix", HookFlags);
        method.Invoke(null, new object[] { result, runOriginal, state });
    }

    private static void Finalizer(IslandSaveData.__TryLoad_d__54 instance, MoveNextObservation state)
    {
        MethodInfo method = typeof(Patch_MountIslandFreshObserver).GetMethod("Finalizer", HookFlags);
        method.Invoke(null, new object[] { null, instance, state });
    }

    private static void SimulateYield(IslandSaveData.__TryLoad_d__54 instance,
        MoveNextObservation state, int postState)
    {
        instance.__1__state = postState;
        Postfix(result: true, runOriginal: true, state);
        Finalizer(instance, state);
    }

    private static void SimulateTerminal(IslandSaveData.__TryLoad_d__54 instance,
        MoveNextObservation state)
    {
        instance.__1__state = -1;
        Postfix(result: false, runOriginal: true, state);
        Finalizer(instance, state);
    }

    private static MoveNextObservation PrefixAt(IslandSaveData.__TryLoad_d__54 instance, int state)
    {
        instance.__1__state = state;
        return Prefix(instance, runOriginal: true);
    }

    private static void Print(string mode)
    {
        Console.WriteLine("mode=" + mode
            + " state=" + MountIslandFreshObservation.Gate.State
            + " generation=" + MountIslandFreshObservation.Gate.Generation
            + " pending=" + MountIslandFreshObservation.Gate.HasPending
            + " typedGetterCalls=" + StubState.TypedGetterCalls
            + " nativeFieldGetCalls=" + StubState.NativeFieldGetCalls);
    }

    private static int Main(string[] args)
    {
        string mode = args.Length == 0 ? "first-seen" : args[0];
        try
        {
            switch (mode)
            {
                case "stale": Stale(); break;
                case "drift": Drift(); break;
                case "first-seen": FirstSeen(); break;
                case "pc0-pc1": Pc0Pc1(); break;
                case "restart": Restart(); break;
                case "owner-drift": OwnerDrift(); break;
                default:
                    Console.WriteLine("unknown mode " + mode);
                    return 2;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("mode=" + mode + " fault=" + Unwrap(e));
            return 3;
        }

        Print(mode);
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        Console.WriteLine("contract=" + (Failures.Count == 0 ? "PASS" : "FAIL"));
        return Failures.Count == 0 ? 0 : 1;
    }

    private static string Unwrap(Exception e)
        => e is TargetInvocationException tie && tie.InnerException != null
            ? tie.InnerException.GetType().Name + ":" + tie.InnerException.Message
            : e.GetType().Name + ":" + e.Message;

    private static void Stale()
    {
        IslandSaveData.__TryLoad_d__54 instance = SetupState();
        Check(MountIslandFreshObservation.Gate.TrySealEnvelope(Envelope()), "stale: first seal");
        Check(MountIslandFreshObservation.Gate.TrySealEnvelope(Envelope()), "stale: second seal");
        var observation = new MoveNextObservation
        {
            PreState = 1,
            RequestCampaignIndex = 0,
            RequestChallengeId = 0,
            RequestLand = 11,
            RunOriginal = true,
            PostfixRan = true,
            Result = false,
            Finalized = true,
            SnapshotReadOk = true,
            EnvelopeSealed = true,
            PrefixGeneration = 1,
        };
        instance.__1__state = -1;
        MountIslandFreshObservation.OnFinalized(instance, observation);

        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.Started,
            "stale: new current remains Started");
        Check(!MountIslandFreshObservation.Gate.HasPending, "stale: no pending candidate");
        Check(MountIslandFreshObservation.Gate.Generation == 2, "stale: new generation preserved");
        Check(StubState.TypedGetterCalls == 0, "stale: typed getter not read");
        Check(StubState.NativeFieldGetCalls == 0, "stale: native flags not read");
    }

    private static void Drift()
    {
        IslandSaveData.__TryLoad_d__54 instance = SetupState();
        MountIslandRequestEnvelope envelope = Envelope();
        Check(MountIslandFreshObservation.Gate.TrySealEnvelope(envelope), "drift: initial seal");
        var observation = new MoveNextObservation
        {
            PreState = 1,
            RequestCampaignIndex = 0,
            RequestChallengeId = 0,
            RequestLand = 11,
            RunOriginal = true,
            PostfixRan = true,
            Result = false,
            Finalized = true,
            SnapshotReadOk = true,
            EnvelopeSealed = true,
            PrefixGeneration = 1,
        };
        StubState.DriftOnRead = true;
        StubState.DriftEnvelope = envelope;
        instance.__1__state = -1;
        MountIslandFreshObservation.OnFinalized(instance, observation);

        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.Started,
            "drift: drifted current remains Started");
        Check(!MountIslandFreshObservation.Gate.HasPending, "drift: no pending candidate");
        Check(MountIslandFreshObservation.Gate.Generation == 2, "drift: new generation preserved");
        Check(StubState.TypedGetterCalls == 0, "drift: typed getter not read");
        Check(StubState.NativeFieldGetCalls == 1, "drift: native flags read once then discarded");
    }

    private static void FirstSeen()
    {
        IslandSaveData.__TryLoad_d__54 instance = SetupState();
        MoveNextObservation pc1 = PrefixAt(instance, 1);
        Check(pc1 != null && pc1.EnvelopeSealed, "first-seen: PC1 prefix seals a new request");
        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.Started,
            "first-seen: gate Started after PC1");
        Check(MountIslandFreshObservation.Gate.Generation == 1,
            "first-seen: PC1 issues generation 1");

        SimulateYield(instance, pc1, postState: 2);
        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.Started,
            "first-seen: 1->2 yield keeps Started");
        Check(!MountIslandFreshObservation.Gate.HasPending, "first-seen: 1->2 yield publishes nothing");
        Check(StubState.NativeFieldGetCalls == 0, "first-seen: 1->2 yield does not read native flags");
        Check(StubState.TypedGetterCalls == 0, "first-seen: 1->2 yield does not read typed flags");

        MoveNextObservation pc2 = PrefixAt(instance, 2);
        Check(pc2 != null && pc2.EnvelopeSealed, "first-seen: PC2 prefix resumes the same request");
        SimulateTerminal(instance, pc2);

        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.CompletedOnce,
            "first-seen: terminal publishes one candidate");
        Check(MountIslandFreshObservation.Gate.HasPending, "first-seen: exactly one pending candidate");
        Check(StubState.TypedGetterCalls == 0, "first-seen: typed getter never used");
        Check(StubState.NativeFieldGetCalls == 1, "first-seen: native flags read once");
    }

    private static void Pc0Pc1()
    {
        IslandSaveData.__TryLoad_d__54 instance = SetupState();
        MoveNextObservation pc0 = PrefixAt(instance, 0);
        Check(pc0 != null && pc0.EnvelopeSealed, "pc0-pc1: PC0 prefix seals");
        Check(MountIslandFreshObservation.Gate.Generation == 1, "pc0-pc1: PC0 issues generation 1");
        SimulateYield(instance, pc0, postState: 1);

        MoveNextObservation pc1 = PrefixAt(instance, 1);
        Check(pc1 != null && pc1.EnvelopeSealed, "pc0-pc1: PC1 prefix continues");
        Check(MountIslandFreshObservation.Gate.Generation == 1, "pc0-pc1: PC1 does not seal again");
        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.Started,
            "pc0-pc1: gate remains Started");
        Check(!MountIslandFreshObservation.Gate.HasPending, "pc0-pc1: no publish before terminal");
        Check(StubState.NativeFieldGetCalls == 0, "pc0-pc1: no native flags read");
        Check(StubState.TypedGetterCalls == 0, "pc0-pc1: no typed flags read");
    }

    private static void Restart()
    {
        IslandSaveData.__TryLoad_d__54 instance = SetupState();
        MoveNextObservation pc0 = PrefixAt(instance, 0);
        Check(pc0 != null && pc0.EnvelopeSealed, "restart: PC0 seals");
        SimulateYield(instance, pc0, postState: 1);

        MoveNextObservation firstPc1 = PrefixAt(instance, 1);
        Check(firstPc1 != null && firstPc1.EnvelopeSealed, "restart: first PC1 continues");
        Check(MountIslandFreshObservation.Gate.Generation == 1, "restart: first PC1 keeps generation 1");

        MoveNextObservation secondPc1 = PrefixAt(instance, 1);
        Check(secondPc1 != null && secondPc1.EnvelopeSealed, "restart: second PC1 seals");
        Check(MountIslandFreshObservation.Gate.Generation == 2,
            "restart: same pointer/args PC1 issues generation 2");
        Check(MountIslandFreshObservation.Gate.State == MountIslandEnvelopeState.Started,
            "restart: second request Started");
        Check(StubState.NativeFieldGetCalls == 0, "restart: no native flags read");
        Check(StubState.TypedGetterCalls == 0, "restart: no typed flags read");
    }

    private static void OwnerDrift()
    {
        IslandSaveData.__TryLoad_d__54 instance = SetupState();
        MoveNextObservation pc0 = PrefixAt(instance, 0);
        Check(pc0 != null && pc0.EnvelopeSealed, "owner-drift: PC0 seals");
        SimulateYield(instance, pc0, postState: 1);

        MoveNextObservation pc1 = PrefixAt(instance, 1);
        Check(pc1 != null && pc1.EnvelopeSealed, "owner-drift: PC1 continues");
        Check(MountIslandFreshObservation.Gate.Generation == 1, "owner-drift: generation 1");

        CampaignSaveData.current.CurrentLand = 10;   // owner drift after prefix, before terminal
        SimulateTerminal(instance, pc1);

        Check(!MountIslandFreshObservation.Gate.HasPending, "owner-drift: no publish");
        Check(StubState.NativeFieldGetCalls == 0, "owner-drift: native flags not read");
        Check(StubState.TypedGetterCalls == 0, "owner-drift: typed flags not read");
    }
}
