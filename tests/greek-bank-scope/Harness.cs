// Harness.cs — deterministic fixture + production-state reset + assertions.
//
// Every case starts from a clean machine: the Unity-stub registries, the static state of the
// production bank files and the config are reset, then a fresh world fixture is built.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using KingdomEnhancedMod;
using UnityEngine;

static class Harness
{
    public const string SharedKey = "MyMod_SharedBankStash";
    public const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    public const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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

    public static MethodInfo Method(Type type, string name)
        => type.GetMethod(name, AnyStatic) ?? throw new Exception("missing method " + type.Name + "." + name);

    // ------------------------------------------------------ production reset
    private static readonly Type BankerType = typeof(PatchEconomy_Banker);
    private static readonly Type RestockType = typeof(PatchEconomy_AutoRestock);
    private static readonly Type ScopeType = typeof(GreekBankScope);

    public static void ResetStatics()
    {
        UnityEngine.Object.All.Clear();
        UnityEngine.Object.Destroyed.Clear();
        UnityEngine.Object.DestroyListener = null;
        PlayerPrefs.ResetAll();
        Pool.ByPrefab.Clear();
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
        ModConfig.ResetConfig();
        AutoRestockCounts.Clear();
        BankAssistantCoordinator.ResetCoord();
        PatchEconomy_BankAssistants.EnsureCalls = 0;
        PatchEconomy_BankAssistants.Bound = false;

        SetStatic(BankerType, "_sharedStash", -1);
        SetStatic(BankerType, "_hasPrimedBanker", false);
        SetStatic(BankerType, "_needsReprime", false);
        SetStatic(BankerType, "_nextLateBindFrame", 0);
        ClearStatic(BankerType, "_knownBankers");
        ClearStatic(BankerType, "_profileKeys");
        SetStatic(BankerType, "_primedBanker", null);
        SetStatic(BankerType, "_primedWorld", null);
        SetStatic(BankerType, "_lastObservedStash", 0);
        SetStatic(BankerType, "_sharedLedgerDirty", false);
        SetStatic(BankerType, "_nextLedgerFlushAt", 0f);
        SetStatic(BankerType, "_bankerCheckFrame", 0);
        ClearStatic(BankerType, "_duplicatesThatSkippedAwake");
        ClearStatic(BankerType, "_workProfiles");

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

    public static void BankerUpdate(Banker banker) => PatchEconomy_Banker.Update_Postfix(banker);

    /// <summary>
    /// Installs the Destroy→OnDestroy bridge: Unity calls OnDestroy, our Harmony prefix may
    /// veto the native call (duplicate NetID-903 skip), so the native counter is observable.
    /// </summary>
    public static void WireDestroyToOnDestroy()
    {
        UnityEngine.Object.DestroyListener = gameObject =>
        {
            Banker banker = gameObject.GetComponent<Banker>();
            if (banker == null) return;
            if (PatchEconomy_Banker.OnDestroy_Prefix(banker)) banker.OnDestroy();
        };
    }

    /// <summary>Marks the current world/layer/scene as the one AutoRestock already tracks.</summary>
    public static void PrepareWorldPointers()
    {
        SetStatic(RestockType, "_countsReady", true); // injected order models a planner with a published shop snapshot
        World world = Managers.Inst.world;
        SetStatic(RestockType, "_worldPtr", world.Pointer);
        SetStatic(RestockType, "_layerPtr", world.gameLayer.Pointer);
        SetStatic(RestockType, "_sceneHandle", world.gameLayer.gameObject.scene.handle);
    }

    public static void InjectOrder(int role, int assistantIndex, GameObject actor, int nativePrice,
        float arrivalX, float shopX, string phase = "Approach")
    {
        Type orderType = RestockType.GetNestedType("Order", BindingFlags.NonPublic);
        object order = Activator.CreateInstance(orderType);
        GameObject shopGo = Sim.NewActor("Shop", Managers.Inst.world.gameLayer);
        PayableShop shop = shopGo.AddComponent<PayableShop>();
        shop.Price = nativePrice;
        shop.Currency = CurrencyType.Coins;
        shop.itemPrefab = Sim.NewActor(RoleTag(role)).AddComponent<Droppable>();
        shop.itemPrefab.name = RoleTag(role);
        Pool.ByPrefab[shop.itemPrefab.gameObject] = new Pool();
        SetField(order, "Role", role);
        SetField(order, "AssistantIndex", assistantIndex);
        SetField(order, "Assistant", actor);
        SetField(order, "TargetGO", shopGo);
        SetField(order, "TargetPtr", shopGo.Pointer);
        SetField(order, "TargetInstanceId", shopGo.GetInstanceID());
        SetField(order, "Shop", shop);
        SetField(order, "Target", shop);
        SetField(order, "NativePrice", nativePrice);
        actor.transform.position = new Vector3(arrivalX, 0f, 0f);
        shopGo.transform.position = new Vector3(shopX, 0f, 0f);
        SetField(order, "Phase", Enum.Parse(RestockType.GetNestedType("Phase", BindingFlags.NonPublic), phase));
        SetField(order, "NextActionTime", Time.time);
        SetField(order, "ArrivalX", arrivalX);
        SetField(order, "DepartureX", arrivalX);
        SetField(order, "MovementElapsed", 0f);
        SetField(order, "TargetX", shopX);
        ((IList)GetStatic(RestockType, "_orders")).Add(order);
    }

    public static string RoleTag(int role)
    {
        switch (role)
        {
            case 0: return "Hammer";
            case 1: return "Bow";
            case 2: return "Katana";
            case 3: return "Axe";
            default: return "Bread";
        }
    }

    public static int OrderCount() => ((IList)GetStatic(RestockType, "_orders")).Count;

    public static object Order(int index) => ((IList)GetStatic(RestockType, "_orders"))[index];

    public static string OrderPhase(object order) => Convert.ToString(GetField<object>(order, "Phase"));

    public static string OrderReason(int index)
    {
        string[] reasons = (string[])GetStatic(RestockType, "_blockReason");
        return reasons[index];
    }

    /// <summary>Runs the private per-order finalize path (the debit gate).</summary>
    public static void FinalizePurchase(object order, Banker banker, int orderIndex)
        => Method(RestockType, "FinalizePurchase").Invoke(null, new[] { order, banker, (object)orderIndex });

    public static void EnableRoles(params int[] roles)
    {
        foreach (int role in roles)
        {
            switch (role)
            {
                case 0: ModConfig.AutoRestockWorkersEnabled.Value = true; break;
                case 1: ModConfig.AutoRestockArchersEnabled.Value = true; break;
                case 2: ModConfig.AutoRestockNinjasEnabled.Value = true; break;
                case 3: ModConfig.AutoRestockBerserkersEnabled.Value = true; break;
                case 4: ModConfig.AutoRestockPeasantsEnabled.Value = true; break;
            }
        }
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
    public Stats Stats;
    public CurrencyManager Currency;
    public Director Director;
    public GameObject BankerGo;
    public Banker Banker;
    public int SceneHandle;

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

    public static Fixture BuildGreek(int sceneHandle = 1) => Build(BiomeHolder.GreeceBiomeIndex, sceneHandle);

    public static Fixture BuildForeign(int sceneHandle = 2, int biomeIndex = 1) => Build(biomeIndex, sceneHandle);

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

    /// <summary>Removes the wall pair so the main-banker domain cannot be resolved.</summary>
    public void BreakDomain()
    {
        Kingdom._orderedWalls[Side.Left] = null;
        Kingdom._orderedWalls[Side.Right] = null;
        Kingdom.HasBorderLoaded = false;
        Kingdom.Borders[Side.Left] = 0f;
        Kingdom.Borders[Side.Right] = 0f;
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

    /// <summary>Coin placed inside the current layer at x (for claim-gate cases).</summary>
    public DroppableCurrency AddCoin(float x)
    {
        GameObject coinGo = Sim.NewActor("Coin", Layer);
        coinGo.transform.position = new Vector3(x, 0f, 0f);
        DroppableCurrency coin = coinGo.AddComponent<DroppableCurrency>();
        coin.droppedBy = DropType.Player;
        coin.CurrencyType = CurrencyType.Coins;
        return coin;
    }

    public void AssertNativeProfile(Banker banker, string label)
    {
        Harness.Eq(NativeGather, banker.coinGatherTargetPercentage, label + " gather");
        Harness.Eq(NativeWalk, banker.walkSpeed, label + " walk");
        Harness.Eq(NativeRun, banker.runSpeed, label + " run");
        Harness.Eq(NativeWander, banker.wanderRange, label + " wander");
        Harness.Eq(NativeMaxCoins, banker.playerMaxCoins, label + " maxCoins");
        Harness.Eq(NativeScanRange, banker.coinScanRange, label + " coinScanRange");
        Harness.Eq(NativeScannerRange, banker._coinScanner.range, label + " scanner.range");
        Harness.Eq(NativeScannerBehind, banker._coinScanner.rangeBehind, label + " scanner.rangeBehind");
        Harness.Eq(NativeScannerInterval, banker._coinScanner._interval, label + " scanner.interval");
    }

    public void AssertEnhancedProfile(Banker banker, string label)
    {
        Harness.Eq(0.5f, banker.coinGatherTargetPercentage, label + " gather");
        Harness.Eq(1.95f, banker.walkSpeed, label + " walk");
        Harness.Eq(3.6f, banker.runSpeed, label + " run");
        Harness.Eq(8.75f, banker.wanderRange, label + " wander");
        Harness.Eq(100, banker.playerMaxCoins, label + " maxCoins");
        Harness.Eq(1f, banker._coinScanner._interval, label + " scanner.interval");
    }
}
