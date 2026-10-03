// Harness.cs — deterministic fixture + production-state reset + assertions.
//
// Every case starts from a clean machine: the Unity-stub registries, the static state of
// the production bank/courier files and the config are reset, then a fresh world fixture
// is built by the test itself.
using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

static class Harness
{
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
            Console.WriteLine("FAIL " + label + ": " + e.Message);
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

    public static void Eq(int expected, int actual, string label)
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    public static void Eq(long expected, long actual, string label)
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    public static CoinCourierResult Status(CoinCourierStatus expected, CoinCourierResult actual, string label)
    {
        if (actual.Status != expected)
            throw new Exception(label + ": status " + actual.Status + ", expected " + expected);
        return actual;
    }

    public static CoinCourierResult Reason(CoinCourierReason expected, CoinCourierResult actual, string label)
    {
        if (actual.Reason != expected)
            throw new Exception(label + ": reason " + actual.Reason + ", expected " + expected);
        return actual;
    }

    // ------------------------------------------------------------- reflection
    public static object GetStatic(Type type, string name)
    {
        FieldInfo field = type.GetField(name, AnyStatic) ?? throw new Exception("missing static " + type.Name + "." + name);
        return field.GetValue(null);
    }

    public static T GetStatic<T>(Type type, string name) => (T)GetStatic(type, name);

    public static void SetStatic(Type type, string name, object value)
    {
        FieldInfo field = type.GetField(name, AnyStatic) ?? throw new Exception("missing static " + type.Name + "." + name);
        if (value != null || !field.FieldType.IsValueType)
        {
            field.SetValue(null, value);
            return;
        }
        // Default-initialize a struct static such as the priming receipts.
        try
        {
            field.SetValue(null, Activator.CreateInstance(field.FieldType));
        }
        catch
        {
            field.SetValue(null, System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(field.FieldType));
        }
    }

    public static void ClearStatic(Type type, string name)
    {
        object collection = GetStatic(type, name);
        if (collection == null) return;
        collection.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(collection, null);
    }

    public static void SetField(object instance, string name, object value)
    {
        FieldInfo field = instance.GetType().GetField(name, AnyInstance)
            ?? throw new Exception("missing field " + instance.GetType().Name + "." + name);
        field.SetValue(instance, value);
    }

    public static T GetField<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, AnyInstance)
            ?? throw new Exception("missing field " + instance.GetType().Name + "." + name);
        return (T)field.GetValue(instance);
    }

    // ------------------------------------------------------ production reset
    private static readonly Type BankerPatch = typeof(PatchEconomy_Banker);
    private static readonly Type GreekScope = typeof(GreekBankScope);
    private static readonly Type Economy = typeof(CoinCourierEconomy);
    private static readonly Type BankScope = typeof(CoinCourierBankScope);
    private static readonly Type Targeting = typeof(CoinCourierTargeting);

    public static void ResetStatics()
    {
        UnityEngine.Object.All.Clear();
        UnityEngine.Object.Destroyed.Clear();
        UnityEngine.Object.DestroyListener = null;
        PlayerPrefs.ResetAll();
        Time.time = 0f;
        Time.deltaTime = 0f;
        Time.unscaledTime = 0f;
        Time.timeScale = 1f;
        Time.frameCount = 0;
        Managers.Inst = null;
        NetworkPostbox.Instance = null;
        BiomeHolder.Inst = new BiomeHolder();
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsOnline = false;
        NetworkBigBoss.IsClientPresent = false;
        NetworkBigBoss.HasClientCaughtUp = true;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        ModConfig.ResetConfig();
        Sim.SceneHandle = 1;
        Sim.SceneValid = true;
        KnightIdentityRuntime.Reset();
        UnitScanCache.Reset();
        BankAssistantCoordinator.ResetCoord();
        PatchEconomy_BankAssistants.Reset();

        SetStatic(BankerPatch, "_hasPrimedBanker", false);
        SetStatic(BankerPatch, "_needsReprime", false);
        SetStatic(BankerPatch, "_lastObservedStash", 0);
        SetStatic(BankerPatch, "_primedCaptured", false);
        SetStatic(BankerPatch, "_primedOwner", null);
        SetStatic(BankerPatch, "_primedAccount", (nint)0);
        SetStatic(BankerPatch, "_primedLand", 0);
        SetStatic(BankerPatch, "_primedActor", null);
        SetStatic(BankerPatch, "_primedRoot", null);
        SetStatic(BankerPatch, "_bankerCheckFrame", 0);
        SetStatic(BankerPatch, "_nextLateBindFrame", 0);
        SetStatic(BankerPatch, "_primedBanker", null);
        SetStatic(BankerPatch, "_primedWorld", null);
        ClearStatic(BankerPatch, "_duplicatesThatSkippedAwake");
        ClearStatic(BankerPatch, "_workProfiles");
        ClearStatic(BankerPatch, "_profileKeys");
        ClearStatic(BankerPatch, "_knownBankers");
        GlobalSaveData._loaded = null;
        foreach (string field in new[] { "_global", "_prefs", "_campaignRefs", "_token", "_state",
            "_reason", "_save", "_pop", "_ready" })
            SetStatic(typeof(SharedBankNative), field, null);
        SetStatic(typeof(SharedBankNative), "_bankSerial", 0);

        SetStatic(GreekScope, "_loggedFailure", false);
        SetStatic(Economy, "_inCall", 0);
        SetStatic(Economy, "_loggedFault", false);
        SetStatic(BankScope, "_loggedFault", false);
        SetStatic(Targeting, "_loggedFault", false);
        ClearStatic(Targeting, "Snapshots");
        ClearStatic(Targeting, "Owners");
    }

    public static int BankLive()
    {
        if (!SharedBankNative.TryLive(out _, out _, out int coins))
            throw new Exception("bank Live unavailable");
        return coins;
    }

    public static void NoLegacyBankWrite(string label)
    {
        Eq(0, PlayerPrefs.SetIntCalls, label + " SetInt");
        Eq(0, PlayerPrefs.SaveCalls, label + " Save");
    }
}

/// <summary>Minimal world/knight builders on top of the stub game surface.</summary>
static class Fixture
{
    public static void PublicDocument(int coins)
    {
        var global = GlobalSaveData._loaded;
        global.prefs.contents["MyMod_SharedBankNative_v1"] =
            PrivateBankR3.BankDocument.Write(
                new[] { (PrivateBankR3.BankCategory.Normal, 0, coins) });
        SharedBankNative.BeforeMutation(global);
    }

    public static Managers NewWorld(int biome = BiomeHolder.GreeceBiomeIndex)
    {
        GameObject layerGo = Sim.NewLayer("GameLayer");
        World world = new World { gameLayer = layerGo.transform };
        Managers managers = new Managers
        {
            world = world,
            game = new Game(),
            stats = new Stats(),
            director = new Director(),
            kingdom = new Kingdom(),
        };
        Managers.Inst = managers;
        BiomeHolder.Inst.BiomeIndex = biome;
        GlobalSaveData._loaded = new GlobalSaveData();
        GlobalSaveData._loaded.campaigns.Add(new CampaignSaveData());
        return managers;
    }

    public static Banker NewBanker(int stash, bool apply = true)
    {
        GameObject go = Sim.NewActor("Banker", Managers.Inst.world.gameLayer);
        Banker banker = go.AddComponent<Banker>();
        go.AddComponent<Persistent>();
        banker._stashedCoins = stash;
        Managers.Inst.kingdom.banker = banker;
        Managers.Inst.kingdom.castle =
            Sim.NewActor("Castle", Managers.Inst.world.gameLayer).AddComponent<Castle>();
        RegisterBanker903(banker);
        if (apply && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex)
            PatchEconomy_Banker.AfterNativeApply(banker, stash);
        return banker;
    }

    /// <summary>注册 fixedID 903 动态登记（模拟原生 Banker.Awake / Castle 建行的登记链）。</summary>
    public static CRPCHeader RegisterBanker903(Banker banker)
    {
        NetworkPostbox postbox = NetworkPostbox.Instance;
        if (postbox == null) NetworkPostbox.Instance = postbox = new NetworkPostbox();
        var header = new CRPCHeader
        {
            NetID = CoinCourierBankScope.CourierBankerNetId,
            netID = CoinCourierBankScope.CourierBankerNetId,
            HeaderType = CRPCType.Dynamic,
            referencedGO = banker.gameObject,
        };
        banker.parentHeaderRef = header;
        postbox.DynamicObjects[header.NetID] = header;
        return header;
    }

    /// <summary>清掉 903 登记（模拟登记缺失/销毁，登记对象引用仍留在原处）。</summary>
    public static void ClearBanker903()
    {
        NetworkPostbox.Instance?.DynamicObjects.Clear();
    }

    public static Knight NewKnight(long life, int coins, int capacity, Side side = Side.Left)
    {
        GameObject go = Sim.NewActor("Knight", Managers.Inst.world.gameLayer);
        Knight knight = go.AddComponent<Knight>();
        knight.side = side;
        knight.Wallet = go.AddComponent<Wallet>();
        knight.Wallet.TotalCapacity = capacity;
        knight.Wallet.SetCurrency(CurrencyType.Coins, coins);
        knight._character = go.AddComponent<Character>();
        knight._damageable = go.AddComponent<Damageable>();
        knight._fsm = new StateMachine();
        knight._embarkee = go.AddComponent<Embarkee>();
        KnightIdentityRuntime.SetLifetime(knight, life);
        return knight;
    }
}
