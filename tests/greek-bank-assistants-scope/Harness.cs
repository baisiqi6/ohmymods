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
using PrivateBankR3;
using UnityEngine;

static class Harness
{
    /// <summary>旧 PP 共享余额键：生产已改原生 campaign balance / R3，保留常量只为断言“不再访问”。</summary>
    public const string LegacySharedKey = "MyMod_SharedBankStash";
    /// <summary>新观察面：R3 文档键（GlobalSaveData.prefs.contents 里的 JSON 快照）。</summary>
    public const string BankDocumentKey = "MyMod_SharedBankNative_v1";

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

    /// <summary>
    /// 新观察面：当前 campaign account 的 R3 Live。旧断言里的 PlayerPrefs.Ints[SharedKey]
    /// （全局 PP 共享余额）迁移到这里——生产改用原生 campaign balance，且不再跨账号串账。
    /// </summary>
    public static int BankLive()
    {
        if (!SharedBankNative.TryLive(out _, out _, out int coins))
            throw new Exception("bank Live unavailable");
        return coins;
    }

    public static bool BankLiveAvailable()
        => SharedBankNative.TryLive(out _, out _, out _);

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

        SetStatic(BankerType, "_hasPrimedBanker", false);
        SetStatic(BankerType, "_needsReprime", false);
        SetStatic(BankerType, "_primedWorld", null);
        SetStatic(BankerType, "_nextLateBindFrame", 0);
        ClearStatic(BankerType, "_knownBankers");
        ClearStatic(BankerType, "_profileKeys");
        SetStatic(BankerType, "_primedBanker", null);
        SetStatic(BankerType, "_lastObservedStash", 0);
        SetStatic(BankerType, "_primedOwner", null);
        SetStatic(BankerType, "_primedAccount", (nint)0);
        SetStatic(BankerType, "_primedLand", 0);
        SetStatic(BankerType, "_primedActor", null);
        SetStatic(BankerType, "_primedRoot", null);
        SetStatic(BankerType, "_primedCaptured", false);
        SetStatic(BankerType, "_bankerCheckFrame", 0);
        ClearStatic(BankerType, "_duplicatesThatSkippedAwake");
        ClearStatic(BankerType, "_workProfiles");

        // 真实 SharedBankNative 的 owner/R3 状态逐用例隔离（旧 PP 共享键已无生产读写点）。
        GlobalSaveData._loaded = null;
        NetworkPostbox.Instance = null;
        foreach (string field in new[] { "_global", "_prefs", "_campaignRefs", "_token", "_state",
            "_reason", "_save", "_pop", "_ready" })
            SetStatic(typeof(SharedBankNative), field, null);
        SetStatic(typeof(SharedBankNative), "_bankSerial", 0);

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
        BankAssistantCoinOrigin.ClearAll();
        ByteBuffer.ResetForTest();
        BankAssistantAtlasVisuals.Reset();
        ClearStatic(CoordinatorType, "SweepPolicies");
        ClearStatic(CoordinatorType, "TriedThisChain");
        ClearStatic(CoordinatorType, "LoggedDiagnosticStates");
        ClearArrayStatic(CoordinatorType, "ActiveCollector");
        ClearArrayStatic(CoordinatorType, "PoolOwnershipDiagnostic");
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
            SetField(state, "RoundKind", BankAssistantCoinOriginKind.None);
            SetField(state, "PlayerRoundOwner", null);
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

        // 传送表现（真实 production 类）与共享 FX 池的确定性隔离：结束所有槽（取消
        // 自有句柄并归还捕获的 enabled），再把池里残留的活动 effect 推进到寿命终点
        // （不调用 Clear——那是整 world 所有者入口，业务侧不得使用）。
        for (int i = 0; i < 8; i++) BankAssistantTeleportVisuals.EndSlot(i);
        int fxGuard = 0;
        while (CoinCourierTeleportFx.ActiveCount > 0 && fxGuard++ < 16)
            CoinCourierTeleportFx.TickForFrame(1f, 900000 + fxGuard);


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

    /// <summary>Observable of the exact-farm origin registry (issue-89 helper).</summary>
    public static int FarmMarkCount()
    {
        int count = 0;
        foreach (var entry in OriginSnapshot())
            if (entry.Value == BankAssistantCoinOriginKind.Farm) count++;
        return count;
    }

    public static bool FarmMarkContains(int instanceId)
        => OriginSnapshot().TryGetValue(instanceId, out var kind)
            && kind == BankAssistantCoinOriginKind.Farm;

    /// <summary>origin registry 的只读快照（测试经私有 Entries 字段反射观测）。</summary>
    public static Dictionary<int, BankAssistantCoinOriginKind> OriginSnapshot()
    {
        var result = new Dictionary<int, BankAssistantCoinOriginKind>();
        object entries = GetStatic(typeof(BankAssistantCoinOrigin), "Entries");
        foreach (DictionaryEntry pair in (IDictionary)entries)
        {
            object entry = pair.Value;
            object kind = entry.GetType()
                .GetField("Kind", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .GetValue(entry);
            result[(int)pair.Key] = (BankAssistantCoinOriginKind)kind;
        }
        return result;
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
        // Global 是进程级对象（跨换世界保持）；只有测试重置（_loaded=null）后才新建。
        // campaigns[0] 即默认 bank account（旧 PP 全局共享键的替代观察面按账户隔离）。
        if (GlobalSaveData._loaded == null)
        {
            GlobalSaveData._loaded = new GlobalSaveData();
            GlobalSaveData._loaded.campaigns.Add(new CampaignSaveData());
        }
        if (NetworkPostbox.Instance == null) NetworkPostbox.Instance = new NetworkPostbox();
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

    /// <summary>注册 fixedID 903 动态登记（模拟原生 Banker.Awake / Castle 建行的登记链）。</summary>
    public static CRPCHeader RegisterBanker903(Banker banker)
    {
        NetworkPostbox postbox = NetworkPostbox.Instance;
        if (postbox == null) NetworkPostbox.Instance = postbox = new NetworkPostbox();
        var header = new CRPCHeader
        {
            NetID = 903,
            netID = 903,
            HeaderType = CRPCType.Dynamic,
            referencedGO = banker.gameObject,
        };
        banker.parentHeaderRef = header;
        postbox.DynamicObjects[header.NetID] = header;
        return header;
    }

    /// <summary>
    /// 模拟原生 BankerData Apply 收据：真实 2.4 里新银行/读档的余额先经
    /// Persistent_IBehaviour_ApplyData 回到本体，共享账本以该 typed 金额做首次 seed。
    /// 旧 PP seed（首个经济入口直接写键）已废弃，故测试统一用本入口建立 Live。
    /// </summary>
    public static bool SeedNative(Banker banker, int coins)
        => PatchEconomy_Banker.AfterNativeApply(banker, coins);

    /// <summary>
    /// 旧 PP 种值迁移：把既存共享余额写进 R3 文档（account normal slot 0）但不绑定，
    /// 让下一次真实 prime 在读取文档时导入——替代旧 `PlayerPrefs.Ints[SharedKey] = X`。
    /// </summary>
    public static void WriteDocumentRaw(int coins)
        => GlobalSaveData._loaded.prefs.contents[Harness.BankDocumentKey] =
            BankDocument.Write(new[] { (BankCategory.Normal, 0, coins) });

    /// <summary>切换当前 campaign/challenge 账户（旧“两个世界”场景的按账户隔离观察）。</summary>
    public static void SwitchAccount(int campaignIndex, int challengeIndex = 0)
    {
        GlobalSaveData global = GlobalSaveData._loaded;
        while (global.campaigns.Count <= campaignIndex) global.campaigns.Add(new CampaignSaveData());
        while (global.challenges.Count < challengeIndex) global.challenges.Add(new CampaignSaveData());
        global.currentCampaign = campaignIndex;
        global.currentChallenge = challengeIndex;
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
        // 原生 903 身份链：Persistent root + Dynamic header 自指回本体（SharedBankNative.Physical 证据）。
        BankerGo.AddComponent<Persistent>();
        RegisterBanker903(Banker);
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

    /// <summary>issue-89：当前层的原生式君主（header 自指回本体，作为 KnownPlayer 证据）。</summary>
    public Player AddPlayer(float x, string name = "Monarch")
    {
        GameObject playerGo = Sim.NewActor(name, Layer);
        playerGo.transform.position = new Vector3(x, 0f, 0f);
        Player player = playerGo.AddComponent<Player>();
        player.parentHeaderRef = new CRPCHeader { referencedGO = playerGo };
        return player;
    }
}
