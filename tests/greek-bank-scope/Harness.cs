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
using PrivateBankR3;
using UnityEngine;

static class Harness
{
    /// <summary>旧 PP 共享余额键：生产已改原生 campaign balance / R3，保留常量只为断言“不再访问”。</summary>
    public const string LegacySharedKey = "MyMod_SharedBankStash";
    /// <summary>新观察面：R3 文档键（GlobalSaveData.prefs.contents 里的 JSON 快照）。</summary>
    public const string BankDocumentKey = "MyMod_SharedBankNative_v1";
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

    /// <summary>当前 loaded 的共享账本文档是否可导入（未绑定前的观察）。</summary>
    public static bool BankLiveAvailable()
        => SharedBankNative.TryLive(out _, out _, out _);

    // ------------------------------------------------------ production reset
    private static readonly Type BankerType = typeof(PatchEconomy_Banker);
    private static readonly Type RestockType = typeof(PatchEconomy_AutoRestock);
    private static readonly Type ScopeType = typeof(GreekBankScope);

    public static void ResetStatics()
    {
        UnityEngine.Object.All.Clear();
        UnityEngine.Object.Destroyed.Clear();
        UnityEngine.Object.DestroyListener = null;
        Sim.ThrowOnChildTraversal = false;
        Sim.TraversalCallback = null;
        Sim.FaultPosition = null;
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

        SetStatic(BankerType, "_hasPrimedBanker", false);
        SetStatic(BankerType, "_needsReprime", false);
        SetStatic(BankerType, "_nextLateBindFrame", 0);
        ClearStatic(BankerType, "_knownBankers");
        ClearStatic(BankerType, "_profileKeys");
        SetStatic(BankerType, "_primedBanker", null);
        SetStatic(BankerType, "_primedWorld", null);
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
        ResetFixedDomain();
    }

    /// <summary>Issue 100：固定域是进程级静态缓存，夹具必须逐测试清零。</summary>
    private static void ResetFixedDomain()
    {
        Type domainType = typeof(MainBankerFixedDomain);
        object emptyKey = Activator.CreateInstance(
            domainType.GetNestedType("ContextKey", BindingFlags.NonPublic));
        SetStatic(domainType, "_loadGeneration", 0);
        SetStatic(domainType, "_capturePending", false);
        SetStatic(domainType, "_hasNotifiedKey", false);
        SetStatic(domainType, "_notifiedKey", emptyKey);
        SetStatic(domainType, "_notifiedGeneration", 0);
        SetStatic(domainType, "_published", false);
        SetStatic(domainType, "_publishedKey", emptyKey);
        SetStatic(domainType, "_publishedGeneration", 0);
        SetStatic(domainType, "_left", 0f);
        SetStatic(domainType, "_right", 0f);
        SetStatic(domainType, "_lastAttemptFrame", int.MinValue);
        SetStatic(domainType, "_lastLoggedGeneration", -1);
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

    /// <summary>Issue 100：驱动生产固定域 prefix（prepare→profile→movement 的真实入口）。</summary>
    public static void BankerFixedDomainUpdate(Banker banker)
        => Banker_FixedDomain_Patch.Prefix(banker);

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

    public static Fixture BuildGreek(int sceneHandle = 1, bool notify = true)
        => Build(BiomeHolder.GreeceBiomeIndex, sceneHandle, notify);

    public static Fixture BuildForeign(int sceneHandle = 2, int biomeIndex = 1, bool notify = true)
        => Build(biomeIndex, sceneHandle, notify);

    private static Fixture Build(int biomeIndex, int sceneHandle, bool notify = true)
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
            game = fixture.Game,
            stats = fixture.Stats,
            currency = fixture.Currency,
            director = fixture.Director
        };
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = biomeIndex };
        // Issue 100：固定域只随“成功加载通知”发布。夹具走生产入口复现完整链路：
        // 原生通知 postfix（绑定当次 context）→ 维护点结构快照发布（±5 实体墙根）。
        if (notify)
        {
            Managers_OnLevelLoaded_MainBankerFixedDomain_Patch.Postfix(Managers.Inst, false);
            MainBankerFixedDomain.TryCapture(Managers.Inst);
        }
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

    /// <summary>写入文档并立即让 owner 绑定导入（等价于“已存在的共享余额”）。</summary>
    public static void SeedDocument(int coins)
    {
        WriteDocumentRaw(coins);
        SharedBankNative.BeforeMutation(GlobalSaveData._loaded);
    }

    /// <summary>切换当前 campaign/challenge 账户（旧“两个世界”场景的按账户隔离观察）。</summary>
    public static void SwitchAccount(int campaignIndex, int challengeIndex = 0)
    {
        GlobalSaveData global = GlobalSaveData._loaded;
        while (global.campaigns.Count <= campaignIndex) global.campaigns.Add(new CampaignSaveData());
        while (global.challenges.Count < challengeIndex) global.challenges.Add(new CampaignSaveData());
        global.currentCampaign = campaignIndex;
        global.currentChallenge = challengeIndex;
    }

    /// <summary>
    /// 驱动真实 Global 保存门的目录同步（AsyncGate.Prefix → Ensure(true) → ReconcileCatalog），
    /// 让新增 campaign/challenge 账户进入已验证 catalog。只做目录/文档准备，不放行也不再造 Live。
    /// </summary>
    public static void CompleteCatalogReconcile()
    {
        Type nested = typeof(SharedBankNative).GetNestedType("AsyncGate", Harness.AnyStatic)
            ?? throw new Exception("missing SharedBankNative.AsyncGate");
        MethodInfo prefix = nested.GetMethod("Prefix", Harness.AnyStatic)
            ?? throw new Exception("missing AsyncGate.Prefix");
        object[] args = { GlobalSaveData._loaded, null, true }; // 第3槽 = HarmonyX 注入的 ref __runOriginal（首个前缀为 true）
        if (!(bool)prefix.Invoke(null, args))
            throw new Exception("bank save gate rejected the reconcile probe");
    }

    private static Wall MakeWall(Transform parent, float x)
    {
        GameObject wallGo = Sim.NewActor("Wall", parent);
        wallGo.transform.position = new Vector3(x, 0f, 0f);
        return wallGo.AddComponent<Wall>();
    }

    /// <summary>
    /// Issue 100：域只在“成功加载通知 + 结构快照发布”后可用。这里模拟收到一次新的
    /// 成功加载通知但尚未捕获：旧域立即失效、重试前域未知（fail-closed）。
    /// 不再通过删墙/删边界制造不可解析域——固定域不依赖 orderedWalls/border。
    /// </summary>
    public void BreakDomain()
    {
        Managers_OnLevelLoaded_MainBankerFixedDomain_Patch.Postfix(Managers.Inst, false);
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

    /// <summary>域内 wander：min(8.75, ±5 域最小距离 5 - epsilon 0.25) = 4.75。</summary>
    public const float ExpectedDomainWander = 4.75f;

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
        Harness.Eq(1f, banker.coinGatherTargetPercentage, label + " gather");
        Harness.Eq(1.95f, banker.walkSpeed, label + " walk");
        Harness.Eq(3.6f, banker.runSpeed, label + " run");
        Harness.Eq(ExpectedDomainWander, banker.wanderRange, label + " wander");
        Harness.Eq(100, banker.playerMaxCoins, label + " maxCoins");
        Harness.Eq(1f, banker._coinScanner._interval, label + " scanner.interval");
    }
}
