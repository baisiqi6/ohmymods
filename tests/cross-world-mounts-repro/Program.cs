using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using L = Il2CppSystem.Collections.Generic.List<LevelBlock>;

/// <summary>
/// issue-98 运行链 R6 复现：复用 R5 Reviewer 的有界 hook/探针，把正确预期变成断言。
///
/// 覆盖本轮三个确认缺陷（finding 1/2/3）的行为契约：
/// R1 查询 prefix 与登记内部回读互调 → 不递归（有界 hook 不触顶）、首次登记/重初始化/部分失败均收敛；
/// R2 登记作用域内原生异常 → 失败不锁死、初始化点强制重试可恢复；
/// R3 定义授予门必须包含 P2 别名 → 19/27/28 缺失或不等价即不授予，恢复后可授予，无关定义不受阻；
/// R4 查询入口在映射已齐、池未就绪/管理器换代时必须补池；全就绪热路径零资源/建池开销；OFF 兼容。
///
/// 期望值全部是"修复后应为"的行为：对红源（red/source）运行会红，对当前四源运行应全绿。
/// 只验证托管接线/状态约束，不冒充 native gameplay 验收。
/// </summary>
internal static class Program
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static int _checks;
    private static int _failed;

    private sealed class Scenario
    {
        internal HashSet<string> MissingBlocks = new HashSet<string>(StringComparer.Ordinal);
        internal HashSet<int> MissingSteeds = new HashSet<int>();
        internal HashSet<string> MissingPoolCollections = new HashSet<string>(StringComparer.Ordinal);
        internal HashSet<int> MissingNativePools = new HashSet<int>();
        internal int Land = 2;
        internal bool Online;
        internal bool Challenge;
    }

    private static void Check(string name, bool ok)
    {
        _checks++;
        if (ok)
        {
            Console.WriteLine("PASS " + name);
        }
        else
        {
            _failed++;
            Console.WriteLine("FAILED " + name);
        }
    }

    private static void Fail(string message)
    {
        _checks++;
        _failed++;
        Console.WriteLine("FAILED " + message);
    }

    private static void Run(string tag, Action scenario)
    {
        try
        {
            scenario();
        }
        catch (Exception e)
        {
            Fail(tag + " crashed: " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace);
        }
    }

    // ------------------------------------------------------------------ world setup

    private static void ClearStatics()
    {
        var runtime = typeof(CrossWorldMountRuntime);
        ((System.Collections.IList)runtime.GetField("Frames", PrivateStatic).GetValue(null))?.Clear();
        ((System.Collections.IDictionary)runtime.GetField("Templates", PrivateStatic).GetValue(null))?.Clear();
        runtime.GetField("_holder", PrivateStatic).SetValue(null, null);
        ((HashSet<string>)runtime.GetField("LoggedKeys", PrivateStatic).GetValue(null))?.Clear();

        var deps = typeof(CrossWorldMountDependencies);
        deps.GetField("_steedStates", PrivateStatic).SetValue(null, null);
        deps.GetField("_poolStates", PrivateStatic).SetValue(null, null);
        deps.GetField("_verifiedAssetsPointer", PrivateStatic).SetValue(null, 0UL);
        deps.GetField("_verified", PrivateStatic).SetValue(null, false);
        deps.GetField("_allSteedsReady", PrivateStatic).SetValue(null, false);
        deps.GetField("_poolManagerPointer", PrivateStatic).SetValue(null, 0UL);
        deps.GetField("_allPoolsReady", PrivateStatic).SetValue(null, false);
        deps.GetField("_poolCollections", PrivateStatic).SetValue(null, null);
        deps.GetField("_inDependencyScope", PrivateStatic)?.SetValue(null, false);   // 红源无此字段：容忍
        deps.GetField("_receiptCampaignPointer", PrivateStatic)?.SetValue(null, 0UL);
        deps.GetField("_receiptReignIndex", PrivateStatic)?.SetValue(null, int.MinValue);
        deps.GetField("_receiptLandDataPointer", PrivateStatic)?.SetValue(null, 0UL);
        ((HashSet<string>)deps.GetField("_receiptGranted", PrivateStatic)?.GetValue(null))?.Clear();
        ((Dictionary<string, int>)deps.GetField("_receiptNegativeTicks", PrivateStatic)?.GetValue(null))?.Clear();
        ((HashSet<string>)deps.GetField("LoggedKeys", PrivateStatic).GetValue(null))?.Clear();

        BiomeData.SteedSwap = null;
        BiomeSpecificAssets.HookEnabled = false;
        BiomeSpecificAssets.HookDepth = 0;
        BiomeSpecificAssets.MaxHookDepth = 0;
        BiomeSpecificAssets.Capped = 0;
        BiomeSpecificAssets.ReadyAtCap = 0;
        BiomeSpecificAssets.HookCalls = 0;
        BiomeSpecificAssets.HookBudget = 4000;
        BiomeSpecificAssets.BudgetExhausted = 0;
        BiomeSpecificAssets.ThrowOnFirstNestedQuery = false;
        BiomeSpecificAssets.PrefabSwap = null;
    }

    private static CampaignSaveData Reset(Scenario scenario = null)
    {
        scenario = scenario ?? new Scenario();
        ClearStatics();
        ModConfig.Enabled.Value = true;
        ModConfig.CrossWorldMountsEnabled.Value = true;
        BiomeHolder.Inst = new BiomeHolder();
        Resources.Items.Clear();
        Resources.Loads = 0;
        Pool.PoolsByPrefab.Clear();
        Pool.CreatePoolForCalls = 0;
        Pool.InitCalls = 0;

        var poolManager = new PoolManager { gameObject = new GameObject("PoolManager") };
        Managers.Inst = new Managers { game = new Managers.Game(), pools = poolManager };
        Managers.Inst.game.currentLand = scenario.Land;
        NetworkBigBoss.IsOnline = scenario.Online;
        GlobalSaveData.loaded = new GlobalSaveData { InChallenge = scenario.Challenge };
        ChallengeHolder.Inst = null;
        SeasonalEventManager.IsEventActiveProvider = null;

        // native pool collections（可缺省/可单条缺省）
        var norselands = new List<Pool>();
        var deadlands = new List<Pool>();
        Dictionary<int, Pool> bySync = new Dictionary<int, Pool>();
        void AddPool(List<Pool> collection, int prefabPathId, short syncId, string name)
        {
            if (scenario.MissingNativePools.Contains(prefabPathId)) return;
            var attack = new GameObject(name);
            var pool = new Pool { gameObject = new GameObject("pool@" + name), prefab = attack, sync = true, syncID = syncId };
            AttackPrefabs[name] = attack;
            collection.Add(pool);
            bySync[syncId] = pool;
        }
        AttackPrefabs.Clear();
        if (!scenario.MissingPoolCollections.Contains("norselands"))
        {
            AddPool(norselands, 28665, 80, "sleipnir.spit");
            AddPool(norselands, 28666, 78, "kelpie.summer");
            AddPool(norselands, 24301, 76, "kelpie.winter");
            Resources.Items["biomepools.norselands"] = new BiomeObjectPools("norselands", norselands.ToArray());
        }
        if (!scenario.MissingPoolCollections.Contains("deadlands"))
        {
            AddPool(deadlands, 24297, 57, "golem.spit");
            AddPool(deadlands, 24298, 55, "beetle.spit");
            Resources.Items["biomepools.deadlands"] = new BiomeObjectPools("deadlands", deadlands.ToArray());
        }
        SyncPoolsById = bySync;

        foreach (CrossWorldMountDefinition definition in CrossWorldMountCatalog.Definitions)
        {
            if (!scenario.MissingBlocks.Contains(definition.Id))
            {
                var blockGo = new GameObject(definition.BlockObjectName);
                var block = new LevelBlock
                {
                    gameObject = blockGo,
                    groupOne = (LevelBlockGroup)definition.GroupOne,
                    groupTwo = (LevelBlockGroup)definition.GroupTwo,
                };
                blockGo.Components.Add(block);
                Resources.Items[definition.BlockResourcePath] = blockGo;
            }
            AddSteed(definition.SteedTypeId, definition.SteedPrefabPath, scenario);
            foreach (CrossWorldMountSteedAlias alias in definition.Aliases)
                AddSteed(alias.SteedTypeId, alias.PrefabPath, scenario);
        }

        var campaign = new CampaignSaveData { currentLand = scenario.Land };
        for (int i = 0; i < 10; i++) campaign.currentReign.landData.Add(new CampaignSaveData.LandMapData());
        CampaignSaveData.current = campaign;
        // 准确 owner：Global.GetCurrentCampaign() 与 CampaignSaveData.current 同一实例（actual 语义）。
        GlobalSaveData.loaded.CurrentCampaignProvider = campaign;
        return campaign;
    }

    internal static readonly Dictionary<string, GameObject> AttackPrefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    internal static Dictionary<int, Pool> SyncPoolsById = new Dictionary<int, Pool>();

    private static void AddSteed(int steedTypeId, string prefabPath, Scenario scenario)
    {
        if (scenario.MissingSteeds.Contains(steedTypeId)) return;
        var go = new GameObject(prefabPath.Substring(prefabPath.LastIndexOf('/') + 1));
        var steed = new Steed { gameObject = go, steedType = (SteedType)steedTypeId };
        go.Components.Add(steed);

        switch (steedTypeId)
        {
            case 22:
                AttachSpit(go, "sleipnir.spit");
                break;
            case 25:
                AttachSpit(go, "kelpie.summer");
                var kelpie = new KelpieSteedAbility
                {
                    gameObject = go,
                    summerAttackPrefab = Attack("kelpie.summer"),
                    winterAttackPrefab = Attack("kelpie.winter"),
                };
                go.Components.Add(kelpie);
                break;
            case 15:
                AttachSpit(go, "golem.spit");
                break;
            case 14:
                AttachSpit(go, "beetle.spit");
                break;
        }
        Resources.Items[prefabPath] = go;
    }

    private static void AttachSpit(GameObject go, string attackName)
    {
        GameObject attack = Attack(attackName);
        if (attack == null) return;
        go.Components.Add(new SpitSteedAbility { gameObject = go, _spitPrefab = attack });
    }

    private static GameObject Attack(string name)
        => AttackPrefabs.TryGetValue(name, out GameObject go) ? go : (AttackPrefabs[name] = new GameObject(name));

    private static L Blocks(params string[] names)
    {
        var list = new L();
        foreach (string name in names)
        {
            var go = new GameObject(name);
            list.Add(new LevelBlock { gameObject = go });
        }
        return list;
    }

    private static void InvokePrivate(Type type, string method, params object[] args)
    {
        MethodInfo info = type.GetMethod(method, PrivateStatic);
        if (info == null) throw new Exception("method not found: " + type.Name + "." + method);
        info.Invoke(null, args);
    }

    private static bool IsReady(CrossWorldMountDefinition definition)
        => CrossWorldMountDependencies.IsDefinitionReady(definition);

    private static CrossWorldMountDefinition Def(int type)
        => CrossWorldMountCatalog.BySteedType(type);

    /// <summary>重入门是否仍被持有（红源无此字段 → false，不构成红/绿判据）。</summary>
    private static bool ScopeHeld()
    {
        FieldInfo field = typeof(CrossWorldMountDependencies).GetField("_inDependencyScope", PrivateStatic);
        if (field == null) return false;
        return field.GetValue(null) is bool held && held;
    }

    private static CrossWorldMountDefinition OwnerOfAlias(int aliasType)
    {
        foreach (CrossWorldMountDefinition d in CrossWorldMountCatalog.Definitions)
            foreach (CrossWorldMountSteedAlias a in d.Aliases)
                if (a.SteedTypeId == aliasType) return d;
        throw new Exception("alias owner not found: " + aliasType);
    }

    private static string AliasPath(CrossWorldMountDefinition owner, int aliasType)
    {
        foreach (CrossWorldMountSteedAlias a in owner.Aliases)
            if (a.SteedTypeId == aliasType) return a.PrefabPath;
        throw new Exception("alias path not found: " + aliasType);
    }

    // ------------------------------------------------------------------ R1

    private static void R1_ReentrantQuery()
    {
        // 首次登记完全走真实 prefix 互调（内部每次原生回读也执行 prefix），不得递归。
        Reset();
        BiomeSpecificAssets assets = BiomeHolder.Inst.curBiomeAssets;
        BiomeSpecificAssets.HookEnabled = true;
        Steed alias = assets.GetSteedByType((SteedType)19);
        Check("R1 first query resolves through the real prefix chain", alias != null);
        Check("R1 one query registers every mapping", CrossWorldMountDependencies.AllSteedsReady
            && CrossWorldMountDependencies.ReadySteedCount == 20);
        Check("R1 bounded hook never hit its cap (no recursion)",
            BiomeSpecificAssets.Capped == 0 && BiomeSpecificAssets.BudgetExhausted == 0);
        Check("R1 hook depth stays minimal", BiomeSpecificAssets.MaxHookDepth <= 3);

        // 同 asset 重初始化（原生清空映射）在真实 prefix 互调下恢复。
        assets.biomeSteeds.Clear();
        assets.objectSteedTypePairs.Clear();
        InvokePrivate(typeof(PatchRide_CrossWorldMount_AssetsInit), "Postfix");
        Check("R1 same-asset reinit restores mappings under the hook",
            assets.GetSteedByType((SteedType)21) != null && CrossWorldMountDependencies.AllSteedsReady);
        Check("R1 reinit stays bounded",
            BiomeSpecificAssets.Capped == 0 && BiomeSpecificAssets.BudgetExhausted == 0);

        // 部分失败（缺 19）：有界收敛，其余条目照常就绪。
        Reset(new Scenario { MissingSteeds = { 19 } });
        BiomeSpecificAssets.HookEnabled = true;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_AssetsInit), "Postfix");
        Check("R1 partial failure stays bounded under the hook",
            BiomeSpecificAssets.Capped == 0 && BiomeSpecificAssets.BudgetExhausted == 0
            && BiomeSpecificAssets.MaxHookDepth <= 3);
        Check("R1 partial failure leaves every other entry ready",
            !CrossWorldMountDependencies.AllSteedsReady && CrossWorldMountDependencies.ReadySteedCount == 19);
    }

    // ------------------------------------------------------------------ R2

    private static void R2_ExceptionRelease()
    {
        // 登记作用域内的原生查询异常：只失败受影响条目，重入门必须归还（下一次强制重试可恢复）。
        Reset();
        BiomeSpecificAssets assets = BiomeHolder.Inst.curBiomeAssets;
        BiomeSpecificAssets.HookEnabled = true;
        BiomeSpecificAssets.ThrowOnFirstNestedQuery = true;
        assets.GetSteedByType((SteedType)6);
        Check("R2 nested native fault fails only the affected entry",
            !CrossWorldMountDependencies.AllSteedsReady && CrossWorldMountDependencies.ReadySteedCount == 19);
        Check("R2 fault path stays bounded and releases the reentrancy latch",
            BiomeSpecificAssets.Capped == 0 && BiomeSpecificAssets.BudgetExhausted == 0 && !ScopeHeld());

        int loadsBefore = Resources.Loads;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_AssetsInit), "Postfix");
        Check("R2 force retry at the init point recovers after the exception",
            CrossWorldMountDependencies.AllSteedsReady && assets.GetSteedByType((SteedType)21) != null);
        Check("R2 retry actually re-reads the resource (no permanent lock)",
            Resources.Loads > loadsBefore && BiomeSpecificAssets.Capped == 0
            && BiomeSpecificAssets.BudgetExhausted == 0);
    }

    // ------------------------------------------------------------------ R3

    private static void R3_AliasGate()
    {
        // 每个 P2 别名单独缺失：主类型就绪也不得授予本定义；无关定义不受阻；恢复后本定义可授予。
        foreach (int aliasType in new[] { 19, 27, 28 })
        {
            CrossWorldMountDefinition owner = OwnerOfAlias(aliasType);
            Reset(new Scenario { MissingSteeds = { aliasType } });
            CrossWorldMountDependencies.EnsureAll();
            Check("R3 alias " + aliasType + " missing blocks its definition", !IsReady(owner));
            Check("R3 alias " + aliasType + " missing keeps unrelated definitions ready",
                IsReady(Def(4)) && IsReady(Def(21)) && !CrossWorldMountDependencies.AllSteedsReady);

            var go = new GameObject("Alias" + aliasType);
            var steed = new Steed { gameObject = go, steedType = (SteedType)aliasType };
            go.Components.Add(steed);
            Resources.Items[AliasPath(owner, aliasType)] = go;
            InvokePrivate(typeof(PatchRide_CrossWorldMount_AssetsInit), "Postfix");
            Check("R3 repaired alias " + aliasType + " re-enables its definition",
                IsReady(owner) && CrossWorldMountDependencies.AllSteedsReady);
        }

        // 别名被原生映射到不等价 prefab：保留原生、本定义 fail-closed，不覆盖。
        Reset(new Scenario { MissingSteeds = { 19 } });
        BiomeSpecificAssets assets = BiomeHolder.Inst.curBiomeAssets;
        var decoyGo = new GameObject("DecoyP2");
        var decoy = new Steed { gameObject = decoyGo, steedType = (SteedType)19 };
        decoyGo.Components.Add(decoy);
        assets.objectSteedTypePairs[(SteedType)19] = decoy;
        assets.biomeSteeds.Add(decoy);
        var realGo = new GameObject("WarhorseP2");
        var real = new Steed { gameObject = realGo, steedType = (SteedType)19 };
        realGo.Components.Add(real);
        Resources.Items["Prefabs/Steeds/Warhorse P2"] = realGo;
        CrossWorldMountDependencies.EnsureAll();
        Check("R3 non-equivalent alias mapping fails closed", !IsReady(Def(7)));
        Check("R3 non-equivalent alias mapping is not overwritten",
            ReferenceEquals(assets.GetSteedByType((SteedType)19), decoy));

        // 授予门端到端：缺 19 时 land3 只有 sleipnir/beetle 进入授予。
        Reset(new Scenario { Land = 3, MissingSteeds = { 19 } });
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("R3 grant gate blocks the alias-owning definition",
            frame.Pending.All(p => p.Definition.SteedTypeId != 7) && frame.Pending.Count == 2
            && frame.Pending.Any(p => p.Definition.SteedTypeId == 22));
        CrossWorldMountRuntime.Abort(frame);
    }

    // ------------------------------------------------------------------ R4

    private static void R4_PoolRefreshOnQuery()
    {
        // 映射已就绪但池未建立：查询入口必须补池（旧早退只看映射，绕过 EnsurePools）。
        Reset();
        BiomeSpecificAssets assets = BiomeHolder.Inst.curBiomeAssets;
        BiomeSpecificAssets.HookEnabled = true;
        CrossWorldMountDependencies.OnAssetsInitialized();
        Check("R4 assets init leaves the pool wiring untouched",
            CrossWorldMountDependencies.AllSteedsReady && Managers.Inst.pools.cachedSyncIdPoolPairs.Count == 0);
        assets.GetSteedByType((SteedType)22);
        Check("R4 first query after assets-init fills the pools",
            Managers.Inst.pools.cachedSyncIdPoolPairs.Count == 5 && IsReady(Def(22)));
        Check("R4 pool recovery stays bounded under the hook",
            BiomeSpecificAssets.Capped == 0 && BiomeSpecificAssets.BudgetExhausted == 0
            && BiomeSpecificAssets.MaxHookDepth <= 3);

        // PoolManager 换代：查询入口同样要重新登记（不依赖显式 EnsureAll/InitPools）。
        Managers.Inst.pools = new PoolManager { gameObject = new GameObject("PoolManager2") };
        Pool.PoolsByPrefab.Clear();
        assets.GetSteedByType((SteedType)22);
        Check("R4 query re-registers pools for a new manager generation",
            Managers.Inst.pools.cachedSyncIdPoolPairs.Count == 5);

        // 全就绪热路径：查询不再产生资源加载/建池。
        int loads = Resources.Loads, creates = Pool.CreatePoolForCalls;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_SteedQuery), "Prefix", assets);
        InvokePrivate(typeof(PatchRide_CrossWorldMount_SteedQuery), "Prefix", assets);
        Console.WriteLine("MEASURE R4 hotpathQueries=2 loadsDelta=" + (Resources.Loads - loads)
            + " poolCreatesDelta=" + (Pool.CreatePoolForCalls - creates));
        Check("R4 fully-ready hot path does no resource/pool work",
            Resources.Loads == loads && Pool.CreatePoolForCalls == creates);

        // OFF（功能关闭、模组总开关打开）：查询入口仍保证恢复所需的映射与池。
        Reset();
        ModConfig.CrossWorldMountsEnabled.Value = false;
        BiomeSpecificAssets offAssets = BiomeHolder.Inst.curBiomeAssets;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_SteedQuery), "Prefix", offAssets);
        Check("R4 OFF keeps query-path registration and pool recovery",
            CrossWorldMountDependencies.AllSteedsReady && IsReady(Def(22))
            && Managers.Inst.pools.cachedSyncIdPoolPairs.Count == 5
            && offAssets.GetSteedByType((SteedType)25) != null);
    }

    // ------------------------------------------------------------------ R5

    /// <summary>真实希腊原生表（objectSteedTypePairs/biomeSteeds 同持有 Horse Regular/type8）：
    /// 登记存在性必须按精确键与列表真实内容判定；公共 getter 缺键会回退 8，不能当作“已有映射”。</summary>
    private static Steed SeedNativeHorse(BiomeSpecificAssets assets)
    {
        var go = new GameObject("Horse");
        var horse = new Steed { gameObject = go, steedType = (SteedType)8 };
        go.Components.Add(horse);
        assets.biomeSteeds.Add(horse);
        assets.objectSteedTypePairs[(SteedType)8] = horse;
        return horse;
    }

    private static bool UniqueSteedTypes(Il2CppSystem.Collections.Generic.List<Steed> list)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < list.Count; i++)
        {
            Steed steed = list[i];
            if (steed == null || !seen.Add((int)steed.steedType)) return false;
        }
        return true;
    }

    private static void R5_NativeFallbackRegistration()
    {
        // 原生 2.4 反汇编（native-stage1 @72c9d0）：GetSteedByType 只按精确键查 objectSteedTypePairs，
        // 缺键/Unity-null 回退 type8 Horse Regular，最后过 BiomeData.GetPrefabSwap。希腊原生表必然
        // 含 Horse Regular：旧登记用公共 getter 判存在性 → 把回退马误判成“已有不同映射”，全部外来
        // 类型注册失败。修法=精确键 + 列表内容判存在，getter 只做最终回读。

        // 1) 全部 20 个 type/alias（14 主 + 3 别名 + 2 变体主 + 1 变体别名）在含 Horse 8 的真实表上就绪：
        //    17 条走精确键登记，3 条变体覆盖只解析资源、不入表。
        Reset();
        BiomeSpecificAssets assets = BiomeHolder.Inst.curBiomeAssets;
        Steed horse = SeedNativeHorse(assets);
        Check("R5 raw getter falls back to native Horse 8 for a missing key",
            ReferenceEquals(assets.GetSteedByType((SteedType)99), horse));
        BiomeSpecificAssets.HookEnabled = true;   // 每次原生查询都走真实 prefix（与 R1 同款）
        CrossWorldMountDependencies.EnsureAll();
        Console.WriteLine("MEASURE R5 registration loadCalls=" + Resources.Loads
            + " poolCreateCalls=" + Pool.CreatePoolForCalls + " poolInitCalls=" + Pool.InitCalls
            + " maxHookDepth=" + BiomeSpecificAssets.MaxHookDepth);
        Check("R5 fallback-aware registration resolves all foreign types (20)",
            CrossWorldMountDependencies.AllSteedsReady && CrossWorldMountDependencies.ReadySteedCount == 20);
        Check("R5 native queries during registration stay bounded under the real prefix",
            BiomeSpecificAssets.Capped == 0 && BiomeSpecificAssets.BudgetExhausted == 0
            && BiomeSpecificAssets.MaxHookDepth <= 3);
        var gullinbursti = (GameObject)Resources.Items["Prefabs/Steeds/Gullinbursti"];
        Check("R5 registered foreign type resolves to its own prefab through the real getter",
            assets.GetSteedByType((SteedType)21) is Steed g && ReferenceEquals(g.gameObject, gullinbursti));
        var warhorseP2 = (GameObject)Resources.Items["Prefabs/Steeds/Warhorse P2"];
        Check("R5 alias registers independently of its owner",
            assets.GetSteedByType((SteedType)19) is Steed w && ReferenceEquals(w.gameObject, warhorseP2));
        Check("R5 biomeSteeds holds one instance per type (17 exact-key foreign + native Horse 8), no duplicates",
            assets.biomeSteeds.Count == 18 && UniqueSteedTypes(assets.biomeSteeds));

        // 2) 重复调用/原生重初始化不追加重复 type（原生重建按 biomeSteeds 逐个 Dictionary.Add，撞键会抛）。
        assets.RebuildSteedTypePairs();
        CrossWorldMountDependencies.OnAssetsInitialized();
        CrossWorldMountDependencies.EnsureAll();
        Check("R5 repeat calls and native rebuild neither duplicate nor drop entries",
            assets.biomeSteeds.Count == 18 && UniqueSteedTypes(assets.biomeSteeds)
            && CrossWorldMountDependencies.AllSteedsReady && CrossWorldMountDependencies.ReadySteedCount == 20);

        // 3) 原生精确键已有不同 prefab：保留、不覆盖，本定义 fail-closed，其余定义不受阻。
        Reset();
        assets = BiomeHolder.Inst.curBiomeAssets;
        SeedNativeHorse(assets);
        var decoyGo = new GameObject("DecoyGullinbursti");
        var decoy = new Steed { gameObject = decoyGo, steedType = (SteedType)21 };
        decoyGo.Components.Add(decoy);
        assets.objectSteedTypePairs[(SteedType)21] = decoy;
        assets.biomeSteeds.Add(decoy);
        CrossWorldMountDependencies.EnsureAll();
        Check("R5 non-equivalent native mapping is kept and its definition fails closed",
            !IsReady(Def(21)) && CrossWorldMountDependencies.ReadySteedCount == 19
            && ReferenceEquals(assets.GetSteedByType((SteedType)21), decoy)
            && assets.biomeSteeds.Count == 18 && UniqueSteedTypes(assets.biomeSteeds));

        // 4) 容器 null：明确未就绪，且不做任何 biomeSteeds 写入（红源会先写列表再失败）。
        Reset();
        assets = BiomeHolder.Inst.curBiomeAssets;
        SeedNativeHorse(assets);
        assets.objectSteedTypePairs = null;
        CrossWorldMountDependencies.EnsureAll();
        // 精确键登记对 null 容器 fail-closed；变体覆盖只读资源、不依赖容器，故 3 条就绪且零列表写入。
        Check("R5 null pair container fails closed without list writes",
            !CrossWorldMountDependencies.AllSteedsReady && CrossWorldMountDependencies.ReadySteedCount == 3
            && assets.biomeSteeds.Count == 1);

        // 5) 真实 swap 改变读回：拒绝授予；撤销 swap 后按精确键采纳恢复。
        Reset();
        assets = BiomeHolder.Inst.curBiomeAssets;
        SeedNativeHorse(assets);
        var swappedGo = new GameObject("SwappedGullinbursti");
        var swapped = new Steed { gameObject = swappedGo, steedType = (SteedType)21 };
        swappedGo.Components.Add(swapped);
        BiomeSpecificAssets.PrefabSwap = s => s != null && (int)s.steedType == 21 ? swapped : s;
        CrossWorldMountDependencies.EnsureAll();
        Check("R5 a swap that changes the getter readback refuses the grant",
            !IsReady(Def(21)) && CrossWorldMountDependencies.ReadySteedCount == 19
            && !CrossWorldMountDependencies.AllSteedsReady
            && ReferenceEquals(assets.GetSteedByType((SteedType)21), swapped));
        BiomeSpecificAssets.PrefabSwap = null;
        CrossWorldMountDependencies.OnAssetsInitialized();
        Check("R5 removing the swap re-adopts the written mapping",
            IsReady(Def(21)) && CrossWorldMountDependencies.AllSteedsReady
            && assets.biomeSteeds.Count == 18 && UniqueSteedTypes(assets.biomeSteeds));
    }

    private static void Main()
    {
        Run("R1", R1_ReentrantQuery);
        Run("R2", R2_ExceptionRelease);
        Run("R3", R3_AliasGate);
        Run("R4", R4_PoolRefreshOnQuery);
        Run("R5", R5_NativeFallbackRegistration);
        Console.WriteLine((_failed == 0 ? "ALL PASS" : "FAILURES " + _failed) + " checks=" + _checks);
        Environment.Exit(_failed == 0 ? 0 : 1);
    }
}
