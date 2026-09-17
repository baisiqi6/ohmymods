// Harness.cs — deterministic fixture + production-state reset + assertions.
//
// Every case starts from a clean machine: the Unity-stub registries, the static state of
// the production bank files and the config are reset, then a fresh world fixture is built.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Injection;
using KingdomEnhancedMod;
using UnityEngine;

static class Harness
{
    public const string SharedKey = "MyMod_SharedBankStash";

    private static int _passed, _failed;

    public static void Test(string label, Action body)
    {
        ResetStatics();
        try
        {
            body();
            _passed++;
            Console.WriteLine("PASS " + label);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + label + ": " + e);
        }
    }

    public static int Finish()
    {
        Console.WriteLine(_passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ assertions
    public static void True(bool value, string label)
    {
        if (!value) throw new Exception(label + " (expected true)");
    }

    public static void False(bool value, string label)
    {
        if (value) throw new Exception(label + " (expected false)");
    }

    public static void Eq(float expected, float actual, string label = "value")
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    public static void Eq(int expected, int actual, string label = "value")
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    public static void EqStr(string expected, string actual, string label = "text")
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception(label + ": '" + actual + "', expected '" + expected + "'");
    }

    // ------------------------------------------------------------- reflection
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static object GetStatic(Type type, string name) => type.GetField(name, AnyStatic).GetValue(null);

    public static void SetStatic(Type type, string name, object value) => type.GetField(name, AnyStatic).SetValue(null, value);

    public static void ClearStatic(Type type, string name)
    {
        object collection = GetStatic(type, name);
        if (collection == null) return;
        collection.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(collection, null);
    }

    public static void ClearArrayStatic(Type type, string name)
    {
        if (GetStatic(type, name) is Array array) Array.Clear(array, 0, array.Length);
    }

    public static T GetField<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, AnyInstance).GetValue(instance);

    public static void SetField(object instance, string name, object value)
        => instance.GetType().GetField(name, AnyInstance).SetValue(instance, value);

    public static void SetStaticField<T>(Type type, string name, params T[] values)
    {
        Array target = (Array)GetStatic(type, name);
        for (int i = 0; i < values.Length && i < target.Length; i++) target.SetValue(values[i], i);
    }

    public static void SetEnumStatic(Type type, string name, string enumValue)
    {
        FieldInfo field = type.GetField(name, AnyStatic);
        field.SetValue(null, Enum.Parse(field.FieldType, enumValue));
    }

    public static void SetNestedEnumStatic(Type type, string nestedName, string fieldName, string enumValue)
    {
        Type nested = type.GetNestedType(nestedName, BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo field = type.GetField(fieldName, AnyStatic);
        field.SetValue(null, Enum.Parse(nested, enumValue));
    }

    // ------------------------------------------------------ production reset
    private static readonly Type BankerType = typeof(PatchEconomy_Banker);
    private static readonly Type AssistantsType = typeof(PatchEconomy_BankAssistants);
    private static readonly Type CoordinatorType = typeof(BankAssistantCoordinator);
    private static readonly Type RestockType = typeof(PatchEconomy_AutoRestock);
    private static readonly Type AnimationType = typeof(PositionSync_BankAssistantAnimation_Patch);
    private static readonly Type ScopeType = typeof(GreekBankScope);

    private static readonly FieldInfo AssistantsField =
        CoordinatorType.GetField("Assistants", AnyStatic);
    private static readonly MethodInfo CoordinatorUpdateMethod =
        CoordinatorType.GetMethod("Update", AnyInstance);
    private static readonly MethodInfo CoordinatorOnDestroyMethod =
        CoordinatorType.GetMethod("OnDestroy", AnyInstance);

    public static void ResetStatics()
    {
        UnityEngine.Object.All.Clear();
        UnityEngine.Object.Destroyed.Clear();
        UnityEngine.Object.DestroyListener = null;
        UnityEngine.Resources.Assets.Clear();
        PlayerPrefs.ResetAll();
        Pool.ByPrefab.Clear();
        Pool.ByInstance.Clear();
        Pool.SpawnGoCalls = 0;
        Pool.DespawnCalls = 0;
        Time.time = 0f;
        Time.deltaTime = 0f;
        Time.unscaledTime = 0f;
        Time.timeScale = 1f;
        Time.frameCount = 0;
        Managers.Inst = null;
        BiomeHolder.Inst = new BiomeHolder();
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsOnline = false;
        NetworkBigBoss.IsClientPresent = false;
        NetworkBigBoss.HasClientCaughtUp = true;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        ModConfig.Enabled = new ConfigEntry<bool>(true);
        ModConfig.AutoRestockWorkersEnabled = new ConfigEntry<bool>(false);
        ModConfig.AutoRestockArchersEnabled = new ConfigEntry<bool>(false);
        ModConfig.AutoRestockNinjasEnabled = new ConfigEntry<bool>(false);
        ModConfig.AutoRestockBerserkersEnabled = new ConfigEntry<bool>(false);
        ModConfig.AutoRestockPeasantsEnabled = new ConfigEntry<bool>(false);
        AutoRestockCounts.Clear();
        ClassInjector.Registered.Clear();

        SetStatic(BankerType, "_sharedStash", -1);
        SetStatic(BankerType, "_hasPrimedBanker", false);
        SetStatic(BankerType, "_needsReprime", false);
        SetStatic(BankerType, "_primedWorld", null);
        SetStatic(BankerType, "_nextLateBindFrame", 0);
        ClearStatic(BankerType, "_knownBankers");
        ClearStatic(BankerType, "_profileKeys");
        SetStatic(BankerType, "_primedBanker", null);
        SetStatic(BankerType, "_lastObservedStash", 0);
        SetStatic(BankerType, "_sharedLedgerDirty", false);
        SetStatic(BankerType, "_nextLedgerFlushAt", 0f);
        SetStatic(BankerType, "_bankerCheckFrame", 0);
        ClearStatic(BankerType, "_duplicatesThatSkippedAwake");
        ClearStatic(BankerType, "_workProfiles");

        SetStatic(AssistantsType, "_allBankerPrefabs", null);
        SetStatic(AssistantsType, "_registeredCoordinatorType", false);
        SetStatic(AssistantsType, "_loggedControllerSet", false);
        SetStatic(AssistantsType, "_nextControllerResolveAt", 0f);
        SetStatic(AssistantsType, "_lastControllerFailure", null);
        ClearArrayStatic(AssistantsType, "Prefabs");
        ClearArrayStatic(AssistantsType, "Pools");
        ClearArrayStatic(AssistantsType, "GreekVisualScaleY");

        SetStatic(CoordinatorType, "_cleanupPending", false);
        SetStatic(CoordinatorType, "_cleanupDestroyActors", false);
        SetStatic(CoordinatorType, "_cleanupSyncDespawn", false);
        ClearStatic(CoordinatorType, "SweepCoins");
        SetStatic(CoordinatorType, "_instance", null);
        SetStatic(CoordinatorType, "_mainBanker", null);
        SetStatic(CoordinatorType, "_hadAuthority", false);
        SetStatic(CoordinatorType, "_loggedReady", false);
        SetStatic(CoordinatorType, "_loggedFirstAssignment", false);
        SetStatic(CoordinatorType, "_loggedFirstSubmission", false);
        SetStatic(CoordinatorType, "_nextScanAt", 0f);
        SetStatic(CoordinatorType, "_nextDiagnosticsAt", 0f);
        SetStatic(CoordinatorType, "_nextCollectorIndex", 0);
        SetStatic(CoordinatorType, "_nextRestockAssistant", 0);
        SetStatic(CoordinatorType, "_lastLoggedActiveCount", -1);
        SetStatic(CoordinatorType, "_nextActiveCountLogAt", 0f);
        ClearStatic(CoordinatorType, "Observed");
        ClearStatic(CoordinatorType, "Claims");
        ClearStatic(CoordinatorType, "LiveClaimIds");
        ClearStatic(CoordinatorType, "SeenThisScan");
        ClearStatic(CoordinatorType, "MatureBuffer");
        ClearStatic(CoordinatorType, "RemovalBuffer");
        ClearStatic(CoordinatorType, "FarmOriginCoinIds");
        ClearStatic(CoordinatorType, "SweepPolicies");
        ClearStatic(CoordinatorType, "TriedThisChain");
        ClearStatic(CoordinatorType, "LoggedDiagnosticStates");
        ClearArrayStatic(CoordinatorType, "ActiveCollector");
        foreach (object state in (Array)AssistantsField.GetValue(null))
        {
            SetField(state, "Actor", null);
            SetField(state, "Animator", null);
            SetField(state, "PositionSync", null);
            SetField(state, "Target", null);
            SetField(state, "CarriedCoins", 0);
            SetField(state, "UncreditedCoins", 0);
            SetField(state, "Moving", false);
            SetField(state, "PatrolRight", false);
            SetField(state, "PatrolResumeAt", 0f);
            SetField(state, "RestockReserved", false);
            SetField(state, "WaitDeadline", 0f);
        }

        ClearStatic(RestockType, "_orders");
        ClearStatic(RestockType, "_faultedTargets");
        ClearArrayStatic(RestockType, "_summary");
        ClearArrayStatic(RestockType, "_summaryKey");
        ClearArrayStatic(RestockType, "_statusKey");
        ClearArrayStatic(RestockType, "_blockReason");
        ClearArrayStatic(RestockType, "_successLogged");
        ClearArrayStatic(RestockType, "_motionLogged");
        ClearArrayStatic(RestockType, "_roster");
        ClearArrayStatic(RestockType, "_stock");
        ClearArrayStatic(RestockType, "_incoming");
        ClearArrayStatic(RestockType, "_reserved");
        ClearArrayStatic(RestockType, "_retryAfter");
        SetStatic(RestockType, "_nextScanTime", 0f);
        SetStatic(RestockType, "_roleCursor", 0);
        SetStatic(RestockType, "_worldPtr", IntPtr.Zero);
        SetStatic(RestockType, "_layerPtr", IntPtr.Zero);
        SetStatic(RestockType, "_sceneHandle", 0);
        SetStatic(RestockType, "_lastVersion", -1L);
        SetStatic(RestockType, "_lastSettings", -1L);
        SetStatic(RestockType, "_lastBalance", -1);
        SetStatic(RestockType, "_summaryLogs", 0);
        SetStatic(RestockType, "_needsPlan", true);
        SetStatic(RestockType, "_countsReady", false);
        SetStatic(RestockType, "_faultLoggedWorld", false);

        ClearStatic(AnimationType, "LastPositions");
        ClearStatic(AnimationType, "LastTimes");


        SetStatic(ScopeType, "_loggedFailure", false);
    }

    // ------------------------------------------------------------- driving
    public static void Advance(float seconds)
    {
        Time.time += seconds;
        Time.unscaledTime += seconds;
        Time.deltaTime = seconds;
        Time.frameCount++;
    }

    public static void CoordinatorUpdate(BankAssistantCoordinator coordinator)
        => CoordinatorUpdateMethod.Invoke(coordinator, null);

    public static void CoordinatorDestroy(BankAssistantCoordinator coordinator)
        => CoordinatorOnDestroyMethod.Invoke(coordinator, null);

    public static void BankerUpdate(Banker banker) => PatchEconomy_Banker.Update_Postfix(banker);

    public static object Assistant(int index) => ((Array)AssistantsField.GetValue(null)).GetValue(index);

    public static BankAssistantCoordinator CoordinatorOf(Banker banker)
        => banker.gameObject.GetComponent<BankAssistantCoordinator>();

    /// <summary>Marks an instance as coming from a native pool (production requires it).</summary>
    public static void RegisterPooled(GameObject instance)
        => Pool.ByInstance[instance] = new Pool { gameObject = instance };

    public static void RegisterBankerControllers()
    {
        UnityEngine.Resources.Assets.Add(new RuntimeAnimatorController("banker"));
        UnityEngine.Resources.Assets.Add(new RuntimeAnimatorController("banker_bamboo"));
        UnityEngine.Resources.Assets.Add(new RuntimeAnimatorController("banker_deadlands"));
        UnityEngine.Resources.Assets.Add(new RuntimeAnimatorController("banker_norselands"));
    }

    public static void InjectOrder(int role, int assistantIndex, GameObject actor, int nativePrice,
        float arrivalX, float shopX, string phase = "Approach")
    {
        Type orderType = RestockType.GetNestedType("Order", BindingFlags.NonPublic);
        object order = Activator.CreateInstance(orderType);
        GameObject shopGo = new GameObject("Shop");
        shopGo.transform.Parent = Managers.Inst.world.gameLayer;
        PayableShop shop = shopGo.AddComponent<PayableShop>();
        shop.Price = nativePrice;
        shop.Currency = CurrencyType.Coins;
        SetField(order, "Role", role);
        SetField(order, "AssistantIndex", assistantIndex);
        SetField(order, "Assistant", actor);
        SetField(order, "TargetGO", shopGo);
        SetField(order, "TargetPtr", shopGo.Pointer);
        SetField(order, "TargetInstanceId", shopGo.GetInstanceID());
        SetField(order, "Shop", shop);
        SetField(order, "Target", shop);
        SetField(order, "NativePrice", nativePrice);
        SetField(order, "Phase", Enum.Parse(orderType.GetNestedType("Phase", BindingFlags.NonPublic), phase));
        SetField(order, "NextActionTime", Time.time);
        SetField(order, "ArrivalX", arrivalX);
        SetField(order, "DepartureX", arrivalX);
        SetField(order, "MovementElapsed", 0f);
        SetField(order, "TargetX", shopX);
        ((IList)GetStatic(RestockType, "_orders")).Add(order);
    }

    public static int OrderCount() => ((IList)GetStatic(RestockType, "_orders")).Count;

    /// <summary>Observable of the farm-origin mark set (HashSet&lt;int&gt; is not ICollection).</summary>
    public static int FarmMarkCount()
    {
        object set = GetStatic(CoordinatorType, "FarmOriginCoinIds");
        return (int)set.GetType().GetProperty("Count").GetValue(set);
    }

    public static bool FarmMarkContains(int instanceId)
    {
        object set = GetStatic(CoordinatorType, "FarmOriginCoinIds");
        return (bool)set.GetType().GetMethod("Contains").Invoke(set, new object[] { instanceId });
    }
}

sealed class Fixture
{
    public GameObject LayerGo;
    public Transform Layer;
    public World World;
    public Kingdom Kingdom;
    public Game Game;
    public PoolManager Pools;
    public DroppableRegistrar Registrar;
    public Stats Stats;
    public CurrencyManager Currency;
    public Director Director;
    public GameObject BankerGo;
    public Banker Banker;
    public int SceneHandle;

    public static Fixture BuildGreek(int sceneHandle = 1) => Build(BiomeHolder.GreeceBiomeIndex, sceneHandle);

    public static Fixture BuildForeign(int sceneHandle = 2, int biomeIndex = 1)
        => Build(biomeIndex, sceneHandle);

    /// <summary>Native-looking (non-unit) baseline so restores can be told from hardcoded writes.</summary>
    public const float NativeScanRange = 2.5f;
    public const float NativeGather = 0.31f;
    public const float NativeWalk = 1.31f;
    public const float NativeRun = 2.7f;
    public const float NativeWander = 4.25f;
    public const int NativeMaxCoins = 25;
    public const float NativeScannerRange = 3.1f;
    public const float NativeScannerBehind = 1.7f;
    public const float NativeScannerInterval = 0.5f;

    private static Fixture Build(int biomeIndex, int sceneHandle)
    {
        Fixture fixture = new Fixture { SceneHandle = sceneHandle };
        Sim.SceneHandle = sceneHandle;
        fixture.LayerGo = Sim.NewLayer("GameLayer");
        fixture.Layer = fixture.LayerGo.transform;
        fixture.World = new World { gameLayer = fixture.Layer };
        fixture.Game = new Game { state = Game.State.Playing };
        fixture.Stats = new Stats();
        fixture.Currency = new CurrencyManager { gameObject = fixture.LayerGo };
        fixture.Director = new Director();
        fixture.Pools = new PoolManager { gameObject = fixture.LayerGo };
        fixture.Registrar = new DroppableRegistrar { gameObject = fixture.LayerGo };
        fixture.Kingdom = new Kingdom
        {
            campfirePosition = 0f,
            isSafe = true,
            castle = new Castle { gameObject = fixture.LayerGo }
        };
        fixture.Kingdom.Borders[Side.Left] = -4f;
        fixture.Kingdom.Borders[Side.Right] = 4f;
        fixture.Kingdom._orderedWalls[Side.Left] = new List<Wall> { null, MakeWall(fixture.Layer, -5f) };
        fixture.Kingdom._orderedWalls[Side.Right] = new List<Wall> { null, MakeWall(fixture.Layer, 5f) };
        Managers.Inst = new Managers
        {
            world = fixture.World,
            kingdom = fixture.Kingdom,
            pools = fixture.Pools,
            dropManager = fixture.Registrar,
            game = fixture.Game,
            stats = fixture.Stats,
            currency = fixture.Currency,
            director = fixture.Director
        };
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = biomeIndex };
        return fixture;
    }

    private static Wall MakeWall(Transform parent, float x)
    {
        GameObject wallGo = Sim.NewActor("Wall", parent);
        wallGo.transform.position = new Vector3(x, 0f, 0f);
        return wallGo.AddComponent<Wall>();
    }

    public Banker AddBanker(string name = "Banker", bool kingdomBound = true)
    {
        BankerGo = Sim.NewActor(name, Layer);
        Banker = BankerGo.AddComponent<Banker>();
        Banker._coinScanner = new Scanner();
        Banker._wallet = BankerGo.AddComponent<Wallet>();
        Banker.coinScanRange = NativeScanRange;
        Banker.coinGatherTargetPercentage = NativeGather;
        Banker.walkSpeed = NativeWalk;
        Banker.runSpeed = NativeRun;
        Banker.wanderRange = NativeWander;
        Banker.playerMaxCoins = NativeMaxCoins;
        Banker._coinScanner.range = NativeScannerRange;
        Banker._coinScanner.rangeBehind = NativeScannerBehind;
        Banker._coinScanner._interval = NativeScannerInterval;
        if (kingdomBound) Kingdom.banker = Banker;
        return Banker;
    }

    public DroppableCurrency AddCoin(float x, DropType dropType = DropType.Player, string name = "Coin")
    {
        GameObject coinGo = Sim.NewActor(name, Layer);
        coinGo.transform.position = new Vector3(x, 0f, 0f);
        DroppableCurrency coin = coinGo.AddComponent<DroppableCurrency>();
        coin.droppedBy = dropType;
        coin.CurrencyType = CurrencyType.Coins;
        Harness.RegisterPooled(coinGo);
        Registrar.Droppables.Add(coin);
        return coin;
    }
}
