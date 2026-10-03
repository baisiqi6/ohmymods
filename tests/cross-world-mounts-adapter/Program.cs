using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;
using L = Il2CppSystem.Collections.Generic.List<LevelBlock>;

/// <summary>
/// issue-98 运行链 R5 适配器：直接链接当前四源 + 桩，覆盖四个审查契约的修复场景。
/// 期望值全部是"修复后应为"的行为；对 r4 语义运行时会红（见 red.log），对当前源码运行应全绿。
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
        deps.GetField("_receiptCampaignPointer", PrivateStatic).SetValue(null, 0UL);
        deps.GetField("_receiptReignIndex", PrivateStatic).SetValue(null, int.MinValue);
        deps.GetField("_receiptLandDataPointer", PrivateStatic).SetValue(null, 0UL);
        ((HashSet<string>)deps.GetField("_receiptGranted", PrivateStatic).GetValue(null))?.Clear();
        ((Dictionary<string, int>)deps.GetField("_receiptNegativeTicks", PrivateStatic).GetValue(null))?.Clear();
        ((HashSet<string>)deps.GetField("LoggedKeys", PrivateStatic).GetValue(null))?.Clear();
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
        BiomeData.SteedSwap = null;

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
            // 记录以便场景代码按 prefab 名取回
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

        // 定义地块 + 坐骑（含别名）
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

        // 原生希腊表必然含 Horse Regular（type 8）：objectSteedTypePairs 与 biomeSteeds 同持有，
        // 且原生 getter 缺键时回退它——登记存在性必须按精确键判定，不能把回退当“已有映射”。
        BiomeSpecificAssets nativeAssets = BiomeHolder.Inst.curBiomeAssets;
        var horseGo = new GameObject("Horse");
        var horse = new Steed { gameObject = horseGo, steedType = (SteedType)8 };
        horseGo.Components.Add(horse);
        nativeAssets.biomeSteeds.Add(horse);
        nativeAssets.objectSteedTypePairs[(SteedType)8] = horse;

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
            case 22: // Sleipnir：SpitSteedAbility._spitPrefab
                AttachSpit(go, "sleipnir.spit");
                break;
            case 25: // Kelpie：Spit + Kelpie 夏季/冬季攻击预制体
                AttachSpit(go, "kelpie.summer");
                var kelpie = new KelpieSteedAbility
                {
                    gameObject = go,
                    summerAttackPrefab = Attack( "kelpie.summer"),
                    winterAttackPrefab = Attack("kelpie.winter"),
                };
                go.Components.Add(kelpie);
                break;
            case 15: // Golem
                AttachSpit(go, "golem.spit");
                break;
            case 14: // Beetle
                AttachSpit(go, "beetle.spit");
                break;
        }
        Resources.Items[prefabPath] = go;   // Resources.Load<GameObject> 语义（与生产路径一致）
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

    private static void Observe(CrossWorldMountRuntime.Frame frame, Level level)
    {
        foreach (CrossWorldMountRuntime.PendingGrant pending in frame.Pending)
            CrossWorldMountRuntime.NoteBlockPlaced(pending.Template, level);
    }

    private static int Marks(CampaignSaveData campaign, int land)
        => campaign.currentReign.landData[land].steedSpawns.Length;

    private static void InvokePrivate(Type type, string method, params object[] args)
    {
        MethodInfo info = type.GetMethod(method, PrivateStatic);
        if (info == null) throw new Exception("method not found: " + type.Name + "." + method);
        info.Invoke(null, args);
    }

    private static bool PublishedContains(L list, CrossWorldMountRuntime.Frame frame)
    {
        // 待提交项与发布列表一一对应：每个 PendingGrant 的模板实例都必须真实出现在发布列表中。
        foreach (CrossWorldMountRuntime.PendingGrant pending in frame.Pending)
        {
            bool found = false;
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], pending.Template)) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    private static bool IsReady(CrossWorldMountDefinition definition)
        => CrossWorldMountDependencies.IsDefinitionReady(definition);

    private static CrossWorldMountDefinition Def(int type)
        => CrossWorldMountCatalog.BySteedType(type);

    /// <summary>该 type 必须解回自己的 prefab（防止“非空即通过”被原生 Horse 回退蒙混）。</summary>
    private static bool ResolvesTo(BiomeSpecificAssets assets, int typeId, string prefabPath)
    {
        Steed steed = assets.GetSteedByType((SteedType)typeId);
        return steed != null && (int)steed.steedType == typeId
            && Resources.Items.TryGetValue(prefabPath, out UnityEngine.Object prefab)
            && ReferenceEquals(steed.gameObject, prefab);
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

    // ------------------------------------------------------------------ scenarios

    private static void Contract1_Publish()
    {
        // fresh land2（gullinbursti/hrimfaxe/mansion 同岛）：一次发布全部累计块。
        var campaign = Reset();
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        L before = list;
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C1 fresh grant queues 3 definitions", frame.Pending.Count == 3);
        Check("C1 caller receives the published list (new instance)", !ReferenceEquals(before, list));
        Check("C1 published list contains original + 3 injected blocks", list.Count == 5);
        Check("C1 pending templates all present in published list", PublishedContains(list, frame));
        Check("C1 nothing written before placement", Marks(campaign, 2) == 0);

        var level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        Check("C1 markers written only after placement (3)", Marks(campaign, 2) == 3);

        // 部分失败：mansion 模板缺失 → 只发布其余两条，不产生 mansion 的待提交项。
        campaign = Reset(new Scenario { MissingBlocks = { "woodlands.mansion" } });
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C1 partial failure drops only the broken definition", frame.Pending.Count == 2);
        Check("C1 partial failure still publishes the working ones", list.Count == 4);
        Check("C1 partial failure keeps pending↔published consistent", PublishedContains(list, frame));
        Check("C1 partial failure has no pending for mansion",
            frame.Pending.All(p => p.Definition.Id != "woodlands.mansion"));
        level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        Check("C1 partial failure writes only proven markers (2)", Marks(campaign, 2) == 2);

        // OFF + 已授予：重建必须真正补块，且不新增授予。
        campaign = Reset();
        campaign.currentReign.landData[2].steedSpawns = new[] { (SteedType)21 };
        ModConfig.CrossWorldMountsEnabled.Value = false;
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C1 OFF rebuild publishes the marked definition", list.Count == 3);
        Check("C1 OFF rebuild creates no grants", frame.Pending.Count == 0);
        Check("C1 OFF rebuild does not add markers", Marks(campaign, 2) == 1);
        CrossWorldMountRuntime.Abort(frame);

        // OFF + 岛内存档回执（标记被覆盖的兜底）：补块并自愈标记。
        campaign = Reset(new Scenario { Land = 6 });
        campaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/KelpiePool_norselands" });
        ModConfig.CrossWorldMountsEnabled.Value = false;
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C1 OFF receipt rebuild publishes block", list.Count == 3);
        Check("C1 OFF receipt restores native marker", Marks(campaign, 6) == 1);
        CrossWorldMountRuntime.Abort(frame);

        // 组合回归：先被地图倍率 postfix 扩张过的列表，本地块是纯追加且不破坏既有内容。
        campaign = Reset();
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "Pad_A", "Pad_B", "End");
        int padded = list.Count;
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C1 map-width combination keeps padding and appends locals", list.Count == padded + 3);
        int padA = -1, padB = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].name == "Pad_A") padA = i;
            if (list[i] != null && list[i].name == "Pad_B") padB = i;
        }
        Check("C1 map-width combination preserves padding order",
            padA >= 0 && padB > padA && list[list.Count - 1].name == "End");
        CrossWorldMountRuntime.Abort(frame);

        // 联机/挑战：整体跳过，零发布。
        campaign = Reset(new Scenario { Online = true });
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        before = list;
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C1 online generation publishes nothing", ReferenceEquals(before, list) && frame.Pending.Count == 0);
        CrossWorldMountRuntime.Abort(frame);

        // Harmony 顺序契约静态核对：GetBlocks 后缀固定 Priority.Last。
        MethodInfo postfix = typeof(PatchRide_CrossWorldMount_GetBlocks).GetMethod("Postfix", PrivateStatic);
        HarmonyLib.HarmonyPriority priority = postfix?.GetCustomAttribute<HarmonyLib.HarmonyPriority>();
        Check("C1 GetBlocks postfix pinned to Priority.Last",
            priority != null && priority.Value == HarmonyLib.Priority.Last);
    }

    private static void Contract2_Registration()
    {
        // 全部定义 + 别名（20 条 = 14 主 + 3 别名 + 2 变体主 + 1 变体别名）在正确路径下就绪。
        var campaign = Reset();
        CrossWorldMountDependencies.EnsureAll();
        Check("C2 all steed mappings ready (20)", CrossWorldMountDependencies.AllSteedsReady
            && CrossWorldMountDependencies.ReadySteedCount == 20);
        Check("C2 variant definitions are ready without touching the shared table",
            IsReady(Def(13)) && IsReady(Def(6))
            && !BiomeHolder.Inst.curBiomeAssets.biomeSteeds.Any(s =>
                s != null && ((int)s.steedType == 6 || (int)s.steedType == 13 || (int)s.steedType == 17)));
        var assets = BiomeHolder.Inst.curBiomeAssets;
        Check("C2 corrected type2/4/7 resolve to their own prefabs",
            ResolvesTo(assets, 2, "Prefabs/Steeds/Lizard") && ResolvesTo(assets, 4, "Prefabs/Steeds/Spookyhorse")
            && ResolvesTo(assets, 7, "Prefabs/Steeds/Warhorse P1"));
        Check("C2 P2 alias types resolve to their own prefabs (19/27/28)",
            ResolvesTo(assets, 19, "Prefabs/Steeds/Warhorse P2") && ResolvesTo(assets, 27, "Prefabs/Steeds/Kelpie P2")
            && ResolvesTo(assets, 28, "Prefabs/Steeds/Reindeer_norselands P2"));
        Steed warhorse = assets.GetSteedByType((SteedType)7);
        Check("C2 registered prefab carries the right steedType",
            warhorse != null && (int)warhorse.steedType == 7);
        // 真实原生表语义：14 主定义 + 3 别名占 17 个外来实例，变体覆盖（6/13/17）不入表，
        // 加原生 Horse 8 共 18 项；同 type 不得重复（原生重建 Add 撞键会抛）。
        Check("C2 biomeSteeds holds one instance per type (17 exact-key foreign + native Horse 8)",
            assets.biomeSteeds.Count == 18 && UniqueSteedTypes(assets.biomeSteeds));

        // 部分失败不是全局成功缓存：补上资源后在初始化点重试成功，且真实重新加载。
        Reset(new Scenario { MissingSteeds = { 2 } });
        CrossWorldMountDependencies.EnsureAll();
        BiomeSpecificAssets missingAssets = BiomeHolder.Inst.curBiomeAssets;
        Check("C2 missing resource is not reported ready",
            !CrossWorldMountDependencies.AllSteedsReady && !IsReady(Def(2)));
        // 原生 getter 对缺键会回退 Horse Regular（type8）：不能再用“==null”代理“未登记”，
        // 改为核对回退结果与原生马一致（期望的外来 prefab 未被登记）。
        Check("C2 missing type stays unregistered; raw getter yields the native Horse fallback",
            missingAssets.GetSteedByType((SteedType)2) is Steed fallback2 && (int)fallback2.steedType == 8
            && ReferenceEquals(fallback2, missingAssets.GetSteedByType((SteedType)8)));
        int loadsBefore = Resources.Loads;
        var repairedGo = new GameObject("Lizard");
        var repaired = new Steed { gameObject = repairedGo, steedType = (SteedType)2 };
        repairedGo.Components.Add(repaired);
        Resources.Items["Prefabs/Steeds/Lizard"] = repairedGo;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_AssetsInit), "Postfix");
        Check("C2 retry at init point picks up the repaired resource",
            BiomeHolder.Inst.curBiomeAssets.GetSteedByType((SteedType)2) != null
            && CrossWorldMountDependencies.AllSteedsReady);
        Check("C2 retry actually re-read the resource (no permanent blacklist)", Resources.Loads > loadsBefore);

        // 原生映射不同 prefab：保留原生、该定义不就绪（fail-closed，不覆盖）。
        Reset(new Scenario { MissingSteeds = { 2 } });
        var foreign = new GameObject("Decoy");
        var decoy = new Steed { gameObject = foreign, steedType = (SteedType)2 };
        foreign.Components.Add(decoy);
        var freshAssets = BiomeHolder.Inst.curBiomeAssets;
        freshAssets.objectSteedTypePairs[(SteedType)2] = decoy;
        freshAssets.biomeSteeds.Add(decoy);
        var lizardGo = new GameObject("Lizard");
        var lizard = new Steed { gameObject = lizardGo, steedType = (SteedType)2 };
        lizardGo.Components.Add(lizard);
        Resources.Items["Prefabs/Steeds/Lizard"] = lizardGo;
        CrossWorldMountDependencies.EnsureAll();
        Check("C2 native mapping mismatch fails closed", !IsReady(Def(2)));
        Check("C2 native mapping mismatch is not overwritten",
            ReferenceEquals(freshAssets.GetSteedByType((SteedType)2), decoy));

        // 首次查询钩子：未显式 Ensure 也保证映射存在。
        Reset();
        BiomeSpecificAssets queryAssets = BiomeHolder.Inst.curBiomeAssets;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_SteedQuery), "Prefix", queryAssets);
        Check("C2 query-before-init hook registers mappings",
            ResolvesTo(queryAssets, 4, "Prefabs/Steeds/Spookyhorse")
            && CrossWorldMountDependencies.AllSteedsReady);

        // 同 asset 重初始化：条目被清掉后，InitializeAssets 钩子强制重验恢复。
        Reset();
        CrossWorldMountDependencies.EnsureAll();
        BiomeSpecificAssets reinitAssets = BiomeHolder.Inst.curBiomeAssets;
        reinitAssets.biomeSteeds.Clear();
        reinitAssets.objectSteedTypePairs.Clear();
        Check("C2 reinit simulation clears the mapping", reinitAssets.GetSteedByType((SteedType)6) == null);
        InvokePrivate(typeof(PatchRide_CrossWorldMount_AssetsInit), "Postfix");
        Check("C2 same-asset reinit restores mappings", reinitAssets.GetSteedByType((SteedType)21) != null
            && CrossWorldMountDependencies.AllSteedsReady && reinitAssets.biomeSteeds.Count > 0);

        // 换世界回来（新 assets 实例）：按实例换代重新登记。
        Reset();
        CrossWorldMountDependencies.EnsureAll();
        var second = new BiomeSpecificAssets();
        BiomeHolder.Inst.curBiomeAssets = second;
        CrossWorldMountDependencies.EnsureAll();
        Check("C2 world return registers into the new asset instance",
            second.GetSteedByType((SteedType)25) != null);
    }

    private static void Contract2_GrantGate()
    {
        // 依赖缺失（type2）+ 无活动 stub：land6 的 eggsteed 被依赖门挡、rainbowpony 被新授予
        // 活动门挡；kelpie/wolf 照常授予。
        var campaign = Reset(new Scenario { Land = 6, MissingSteeds = { 2 } });
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C2 grant gate blocks the broken and the seasonal definitions",
            frame.Pending.All(p => p.Definition.SteedTypeId != 2 && p.Definition.SteedTypeId != 38)
            && frame.Pending.Count == 2
            && frame.Pending.Any(p => p.Definition.SteedTypeId == 25)
            && frame.Pending.Any(p => p.Definition.SteedTypeId == 13));
        Check("C2 grant gate still publishes the ready definitions", list.Count == 4);
        Check("C2 grant gate publishes only proven pendings", PublishedContains(list, frame));
        CrossWorldMountRuntime.Abort(frame);
    }

    private static void Contract3_Pools()
    {
        // 五个原生同步池按依赖登记（syncID 来自原生集合，不写死猜测）。
        Reset();
        CrossWorldMountDependencies.EnsureAll();
        var assets = BiomeHolder.Inst.curBiomeAssets;
        PoolManager pm = Managers.Inst.pools;
        foreach (KeyValuePair<int, Pool> pair in SyncPoolsById)
        {
            if (!pm.cachedSyncIdPoolPairs.ContainsKey(pair.Key))
            {
                Fail("C3 syncID " + pair.Key + " missing from cachedSyncIdPoolPairs");
                continue;
            }
            Pool registered = pm.cachedSyncIdPoolPairs[pair.Key];
            GameObject attackGo = pair.Value.prefab;
            Check("C3 syncID " + pair.Key + " registered to the right prefab",
                ReferenceEquals(registered.prefab, attackGo));
            Check("C3 syncID " + pair.Key + " readback via static pool table",
                ReferenceEquals(Pool.GetPoolFromPrefabAsset(attackGo), registered));
            Check("C3 syncID " + pair.Key + " fields match native definition",
                registered.sync && registered.syncID == pair.Key);
        }
        Check("C3 pool count matches evidence (5)", pm.cachedSyncIdPoolPairs.Count == 5);
        Check("C3 definitions with pools are ready", IsReady(Def(22)) && IsReady(Def(25)) && IsReady(Def(15)) && IsReady(Def(14)));
        Check("C3 definitions without pools are ready without pool work", IsReady(Def(21)) && IsReady(Def(2)));

        // syncID 冲突：拒绝登记，定义不授予。
        Reset(new Scenario { Land = 3 });
        PoolManager conflictPm = Managers.Inst.pools;
        var otherGo = new GameObject("OtherPrefab");
        var otherPool = new Pool { gameObject = new GameObject("other"), prefab = otherGo, sync = true, syncID = 80 };
        conflictPm.cachedSyncIdPoolPairs[80] = otherPool;
        CrossWorldMountDependencies.EnsureAll();
        Check("C3 syncID conflict fails closed for the definition", !IsReady(Def(22)));
        Check("C3 syncID conflict keeps the existing registration",
            ReferenceEquals(conflictPm.cachedSyncIdPoolPairs[80], otherPool));
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C3 conflicted definition receives no grant",
            frame.Pending.All(p => p.Definition.SteedTypeId != 22) && frame.Pending.Count == 2);
        CrossWorldMountRuntime.Abort(frame);

        // PoolManager.InitPools 重建：清空缓存后钩子幂等重注册。
        Reset();
        CrossWorldMountDependencies.EnsureAll();
        PoolManager rebuildPm = Managers.Inst.pools;
        Pool beforePool = rebuildPm.cachedSyncIdPoolPairs.ContainsKey(80) ? rebuildPm.cachedSyncIdPoolPairs[80] : null;
        Pool.PoolsByPrefab.Clear();
        rebuildPm.cachedPools.Clear();
        rebuildPm.cachedNamePoolPairs.Clear();
        rebuildPm.cachedSyncIdPoolPairs.Clear();
        InvokePrivate(typeof(PatchRide_CrossWorldMount_PoolInit), "Postfix");
        Check("C3 pool rebuild re-registers every syncID", rebuildPm.cachedSyncIdPoolPairs.Count == 5);
        Check("C3 pool rebuild created a fresh pool instance",
            rebuildPm.cachedSyncIdPoolPairs.ContainsKey(80)
            && !ReferenceEquals(rebuildPm.cachedSyncIdPoolPairs[80], beforePool));
        Pool rebuilt = rebuildPm.cachedSyncIdPoolPairs.ContainsKey(80) ? rebuildPm.cachedSyncIdPoolPairs[80] : null;
        Check("C3 pool rebuild readback intact",
            rebuilt != null && rebuilt.prefab != null
            && ReferenceEquals(Pool.GetPoolFromPrefabAsset(rebuilt.prefab), rebuilt));

        // CreatePoolFor 变体（只建池不挂 static 表）：Init 自愈一次后回读仍成立。
        Reset();
        PoolManager.CreatePoolForSkipsInit = true;
        CrossWorldMountDependencies.EnsureAll();
        Check("C3 self-heal registers the static pool table when CreatePoolFor skipped it",
            Managers.Inst.pools.cachedSyncIdPoolPairs.Count == 5
            && Pool.GetPoolFromPrefabAsset(Managers.Inst.pools.cachedSyncIdPoolPairs[80].prefab) != null);
        PoolManager.CreatePoolForSkipsInit = false;

        // PoolManager 换代：按实例指针重新登记。
        Reset();
        CrossWorldMountDependencies.EnsureAll();
        Managers.Inst.pools = new PoolManager { gameObject = new GameObject("PoolManager2") };
        Pool.PoolsByPrefab.Clear();
        CrossWorldMountDependencies.EnsureAll();
        Check("C3 new pool manager gets the pools", Managers.Inst.pools.cachedSyncIdPoolPairs.Count == 5);

        // 池集合缺失：缺池定义不得授予（land3 sleipnir 被挡，beetle/grave 正常）。
        var campaign = Reset(new Scenario { Land = 3, MissingPoolCollections = { "norselands" } });
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C3 missing pool collection blocks its definitions",
            frame.Pending.All(p => p.Definition.SteedTypeId != 22));
        Check("C3 missing pool collection leaves others granting", frame.Pending.Count == 2 && list.Count == 4);
        CrossWorldMountRuntime.Abort(frame);

        // OFF 已拥有外来坐骑：池/映射保持可查询（不受功能开关影响）。
        Reset();
        CrossWorldMountDependencies.EnsureAll();
        ModConfig.CrossWorldMountsEnabled.Value = false;
        CrossWorldMountDependencies.EnsureAll();
        Check("C3 OFF keeps foreign mappings queryable",
            BiomeHolder.Inst.curBiomeAssets.GetSteedByType((SteedType)27) != null
            && IsReady(Def(25)));
        ModConfig.Enabled.Value = false;
        InvokePrivate(typeof(PatchRide_CrossWorldMount_SteedQuery), "Prefix", BiomeHolder.Inst.curBiomeAssets);
        Check("C3 master-off hooks do nothing harmful", CrossWorldMountDependencies.AllSteedsReady);
    }

    // ------------------------------------------------------------------ variant overrides (Wolf type13/17, Kirin type6)

    private static Steed SteedOf(string name, int typeId)
    {
        var go = new GameObject(name);
        var steed = new Steed { gameObject = go, steedType = (SteedType)typeId };
        go.Components.Add(steed);
        return steed;
    }

    /// <summary>Resources.Load&lt;GameObject&gt;(path).GetComponent&lt;Steed&gt;() 语义（Resources 存的是 GO）。</summary>
    private static Steed Prefab(string path)
        => Resources.Items.TryGetValue(path, out UnityEngine.Object value) && value is GameObject go
            ? go.GetComponent<Steed>() : null;

    private static Steed Resolve(BiomeSpecificAssets assets, int typeId, Steed native)
        => CrossWorldMountDependencies.ResolveQueryResult(assets, typeId, native);

    private static Steed RouteSpawn(SteedSpawn spawn, Steed requested)
        => CrossWorldMountDependencies.RouteSpawnSteed(spawn, requested);

    private static void Contract4_VariantOverride()
    {
        // A. 原生冲突键 fixture：希腊表已有 type6=Unicorn、type13/17=Greek 狼。
        //    变体覆盖既不得写表，也不得被"精确键已有不同实例"卡成未就绪/永不授予。
        var campaign = Reset(new Scenario { Land = 4 });
        var assets = BiomeHolder.Inst.curBiomeAssets;
        Steed nativeUnicorn = SteedOf("Unicorn", 6);
        assets.objectSteedTypePairs[(SteedType)6] = nativeUnicorn;
        assets.biomeSteeds.Add(nativeUnicorn);
        Steed nativeWolf13 = SteedOf("Wolf P1", 13);
        assets.objectSteedTypePairs[(SteedType)13] = nativeWolf13;
        assets.biomeSteeds.Add(nativeWolf13);
        Steed nativeWolf17 = SteedOf("Wolf P2", 17);
        assets.objectSteedTypePairs[(SteedType)17] = nativeWolf17;
        assets.biomeSteeds.Add(nativeWolf17);

        CrossWorldMountDependencies.EnsureAll();
        Check("C4 conflicting native keys do not block variant readiness",
            CrossWorldMountDependencies.AllSteedsReady && IsReady(Def(6)) && IsReady(Def(13)));
        Check("C4 shared table keeps the native variants untouched",
            ReferenceEquals(assets.objectSteedTypePairs[(SteedType)6], nativeUnicorn)
            && ReferenceEquals(assets.objectSteedTypePairs[(SteedType)13], nativeWolf13)
            && ReferenceEquals(assets.objectSteedTypePairs[(SteedType)17], nativeWolf17));
        Check("C4 no variant instance appended to the shared list (only the 3 native decoys carry 6/13/17)",
            assets.biomeSteeds.Count(s => s != null
                && ((int)s.steedType == 6 || (int)s.steedType == 13 || (int)s.steedType == 17)) == 3
            && UniqueSteedTypes(assets.biomeSteeds));

        Steed kirin = Prefab("Prefabs/Steeds/Kirin");
        Steed wolf = Prefab("Prefabs/Steeds/Wolf_norselands");
        Steed wolfP2 = Prefab("Prefabs/Steeds/Wolf Norselands P2");
        Check("C4 fixture resolves the exact variant prefabs", kirin != null && wolf != null && wolfP2 != null);

        // 未授予：查询与生成都必须原样返回原生结果。
        Check("C4 un-granted query returns the native result untouched",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), nativeUnicorn)
            && ReferenceEquals(Resolve(assets, 13, nativeWolf13), nativeWolf13)
            && ReferenceEquals(Resolve(assets, 17, nativeWolf17), nativeWolf17));

        var spawnGo = new GameObject("BlossomTree(Clone)");
        var sakuraSpawn = new SteedSpawn { gameObject = spawnGo };
        spawnGo.Components.Add(sakuraSpawn);
        Check("C4 un-granted spawn route leaves the facility request untouched",
            ReferenceEquals(RouteSpawn(sakuraSpawn, nativeUnicorn), nativeUnicorn));

        // B. 端到端托管授予：land4 新岛生成注入 Kirin 块、放置证实后写 marker；land6 同验 Wolf。
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C4 land4 generation grants the Kirin acquisition block",
            frame.Pending.Any(p => p.Definition.Id == "bamboo.kirin"));
        Check("C4 kirin pending corresponds to a published block", PublishedContains(list, frame));
        var level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        Check("C4 land4 markers written incl. kirin type6",
            Marks(campaign, 4) == 3 && campaign.currentReign.landData[4].steedSpawns.Any(v => (int)v == 6));

        Managers.Inst.game.currentLand = 6;
        campaign.currentLand = 6;
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C4 land6 generation grants the wolf acquisition block",
            frame.Pending.Any(p => p.Definition.Id == "norselands.wolf"));
        level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        // land6 = eggsteed(2) + kelpie(25) + wolf(13)（rainbowpony 被新授予活动门挡下，无活动 stub）。
        Check("C4 land6 markers written incl. wolf type13",
            Marks(campaign, 6) == 3
            && campaign.currentReign.landData[6].steedSpawns.Any(v => (int)v == 13)
            && !campaign.currentReign.landData[6].steedSpawns.Any(v => (int)v == 38));

        // C. 授予后：查询结果按确切变体 prefab。
        Managers.Inst.game.currentLand = 4;
        campaign.currentLand = 4;
        Check("C4 granted query resolves to the exact Kirin prefab",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), kirin));
        Check("C4 granted query resolves to the exact Norse Wolf prefab",
            ReferenceEquals(Resolve(assets, 13, nativeWolf13), wolf));
        Check("C4 wolf P2 alias follows the same grant",
            ReferenceEquals(Resolve(assets, 17, nativeWolf17), wolfP2));

        // 换岛查询一致：授予是战役级 marker，当前 land 变化不影响解析。
        Managers.Inst.game.currentLand = 9;
        Check("C4 cross-island query keeps the granted variant",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), kirin)
            && ReferenceEquals(Resolve(assets, 13, nativeWolf13), wolf));
        Managers.Inst.game.currentLand = 4;

        // Wolf 设施原生引用就是带 BuffUnits 的 Norse prefab：生成路由不重写、读取与首购同源。
        var wolfRockGo = new GameObject("WolfRock_norselands(Clone)");
        var wolfRock = new SteedSpawn { gameObject = wolfRockGo };
        wolfRockGo.Components.Add(wolfRock);
        Check("C4 wolf facility keeps its native Norse prefab (no rewrite needed)",
            ReferenceEquals(RouteSpawn(wolfRock, wolf), wolf)
            && ReferenceEquals(Resolve(assets, 13, nativeWolf13), wolf));

        // 经真实 Harmony Postfix（反射），不是镜像 policy。
        object[] postfixArgs = { assets, (SteedType)6, assets.GetSteedByType((SteedType)6) };
        typeof(PatchRide_CrossWorldMount_SteedQuery).GetMethod("Postfix", PrivateStatic)?.Invoke(null, postfixArgs);
        Check("C4 real query postfix swaps the result under grant",
            postfixArgs[2] is Steed postfixed && ReferenceEquals(postfixed, kirin));

        // 覆盖值仍走原生 GetPrefabSwap 语义（桩计数证明路径经过；Greek/MtOlympus 现表恒等）。
        var swapSeen = new List<Steed>();
        BiomeData.SteedSwap = s => { swapSeen.Add(s); return s; };
        Check("C4 override result goes through GetPrefabSwap",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), kirin)
            && swapSeen.Count == 1 && ReferenceEquals(swapSeen[0], kirin));
        BiomeData.SteedSwap = null;

        // 生成路由：同一 Sakura 设施（首次购买 / 恢复设施重购买）请求参数替换为 Kirin。
        Check("C4 granted spawn route replaces the Sakura facility request",
            ReferenceEquals(RouteSpawn(sakuraSpawn, nativeUnicorn), kirin));
        Check("C4 spawn route is idempotent for the target prefab",
            ReferenceEquals(RouteSpawn(sakuraSpawn, kirin), kirin));
        var darkGo = new GameObject("BlossomTreeDark(Clone)");
        var darkSpawn = new SteedSpawn { gameObject = darkGo };
        darkGo.Components.Add(darkSpawn);
        Check("C4 spawn route ignores non-matching facilities",
            ReferenceEquals(RouteSpawn(darkSpawn, nativeUnicorn), nativeUnicorn));
        Steed horse = assets.GetSteedByType((SteedType)8);
        Check("C4 spawn route ignores other types", ReferenceEquals(RouteSpawn(sakuraSpawn, horse), horse));

        object[] prefixArgs = { sakuraSpawn, nativeUnicorn };
        typeof(PatchRide_CrossWorldMount_SpawnSteed).GetMethod("Prefix", PrivateStatic)?.Invoke(null, prefixArgs);
        Check("C4 real spawn prefix replaces the request under grant",
            prefixArgs[1] is Steed routed && ReferenceEquals(routed, kirin));

        // Harmony 接线声明：两个 patch 类必须指向真实的原生目标方法（防声明错目标）。
        var spawnPatch = typeof(PatchRide_CrossWorldMount_SpawnSteed).GetCustomAttribute<HarmonyLib.HarmonyPatch>();
        Check("C4 spawn patch declares SteedSpawn.SpawnSteed",
            spawnPatch != null && spawnPatch.Target == typeof(SteedSpawn) && spawnPatch.Method == "SpawnSteed");
        var queryPatch = typeof(PatchRide_CrossWorldMount_SteedQuery).GetCustomAttribute<HarmonyLib.HarmonyPatch>();
        Check("C4 query patch declares BiomeSpecificAssets.GetSteedByType",
            queryPatch != null && queryPatch.Target == typeof(BiomeSpecificAssets)
            && queryPatch.Method == "GetSteedByType");

        // D. scope 反例：功能开关/其他 world/挑战/联机/错误 assets/未授予战役一律原样。
        ModConfig.CrossWorldMountsEnabled.Value = false;
        Check("C4 feature OFF still restores the granted variant",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), kirin));
        ModConfig.Enabled.Value = false;
        Check("C4 master OFF returns the native result",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), nativeUnicorn));
        ModConfig.Enabled.Value = true;
        ModConfig.CrossWorldMountsEnabled.Value = true;

        BiomeHolder.Inst.BiomeIndex = 0;
        Check("C4 other world returns native", ReferenceEquals(Resolve(assets, 6, nativeUnicorn), nativeUnicorn));
        BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
        GlobalSaveData.loaded.InChallenge = true;
        Check("C4 challenge returns native", ReferenceEquals(Resolve(assets, 6, nativeUnicorn), nativeUnicorn));
        GlobalSaveData.loaded.InChallenge = false;
        NetworkBigBoss.IsOnline = true;
        Check("C4 online returns native", ReferenceEquals(Resolve(assets, 6, nativeUnicorn), nativeUnicorn));
        NetworkBigBoss.IsOnline = false;
        Check("C4 stale assets instance returns native",
            ReferenceEquals(Resolve(new BiomeSpecificAssets(), 6, nativeUnicorn), nativeUnicorn));

        // 切换战役：旧授予立即失效；换回恢复；receipt 兜底独立成立。
        // 切换战役按真实读档语义：Global 当前选择与 current 同步指向新战役。
        var next = new CampaignSaveData { currentLand = 4 };
        for (int i = 0; i < 10; i++) next.currentReign.landData.Add(new CampaignSaveData.LandMapData());
        CampaignSaveData.current = next;
        GlobalSaveData.loaded.CurrentCampaignProvider = next;
        Check("C4 different campaign loses the grant immediately",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), nativeUnicorn));
        CampaignSaveData.current = campaign;
        GlobalSaveData.loaded.CurrentCampaignProvider = campaign;
        Check("C4 returning to the granting campaign restores it",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), kirin));

        var receiptCampaign = new CampaignSaveData { currentLand = 4 };
        for (int i = 0; i < 10; i++) receiptCampaign.currentReign.landData.Add(new CampaignSaveData.LandMapData());
        receiptCampaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/BlossomTree" });
        CampaignSaveData.current = receiptCampaign;
        GlobalSaveData.loaded.CurrentCampaignProvider = receiptCampaign;
        Check("C4 island receipt alone grants the variant",
            ReferenceEquals(Resolve(assets, 6, nativeUnicorn), kirin));
        CampaignSaveData.current = campaign;
        GlobalSaveData.loaded.CurrentCampaignProvider = campaign;
    }

    // ------------------------------------------------------------------ seasonal eligibility (Anniversary id10)

    /// <summary>诚实桩：ChallengeHolder.Inst（静态入口）+ ChallengeDataForID（实例方法）+
    /// SeasonalEventManager.IsEventActive（静态方法），对应 actual ABI。</summary>
    private static void EventStub(bool active, int dataId = 10, bool seasonal = true,
        bool missingData = false, bool throwOnData = false, bool throwOnActive = false)
    {
        var holder = new ChallengeHolder();
        holder.DataProvider = _ =>
        {
            if (throwOnData) throw new InvalidOperationException("stub event data fault");
            if (missingData) return null;
            return new ChallengeData { id = dataId, isSeasonalEvent = seasonal };
        };
        ChallengeHolder.Inst = holder;
        SeasonalEventManager.IsEventActiveProvider = _ =>
        {
            if (throwOnActive) throw new InvalidOperationException("stub event active fault");
            return active;
        };
    }

    private sealed class Land6Run
    {
        internal CampaignSaveData Campaign;
        internal CrossWorldMountRuntime.Frame Frame;
        internal L List;
        internal Level Level;
        /// <summary>Close 前的 pending 快照（Close 会清空 frame.Pending）。</summary>
        internal int PendingCount;
        internal bool BirthdayPending;
    }

    /// <summary>land6 真实生成管线：Reset 后执行 setup（活动桩等）→ TryApply → Observe → Close。</summary>
    private static Land6Run RunLand6(Action setup, bool close = true)
    {
        var campaign = Reset(new Scenario { Land = 6 });
        setup?.Invoke();
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        var level = new Level();
        Observe(frame, level);
        var run = new Land6Run
        {
            Campaign = campaign,
            Frame = frame,
            List = list,
            Level = level,
            PendingCount = frame.Pending.Count,
            BirthdayPending = frame.Pending.Any(p => p.Definition.Id == "anniversary.rainbowpony"),
        };
        if (close) CrossWorldMountRuntime.Close(frame, level);
        return run;
    }

    private static bool HasPendingDefinition(Land6Run run, string definitionId)
        => run.Frame.Pending.Any(p => p.Definition.Id == definitionId);

    private static bool HasMarker(Land6Run run, int land, int steedTypeId)
        => run.Campaign.currentReign.landData[land].steedSpawns.Any(v => (int)v == steedTypeId);

    private static bool ContainsBlockNamed(L list, string namePrefix)
    {
        for (int i = 0; i < list.Count; i++)
        {
            LevelBlock block = list[i];
            if (block != null && block.name != null && block.name.StartsWith(namePrefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void Contract5_SeasonalEligibility()
    {
        const string Birthday = "anniversary.rainbowpony";

        // 1) 缺 holder（活动无法评估）：Birthday 无块/pending/marker；同岛其他定义照常授予。
        Land6Run run = RunLand6(null);
        Check("C5 missing holder blocks the birthday grant (no block/pending/marker)",
            !run.BirthdayPending
            && !ContainsBlockNamed(run.List, "Steed Birthday_Blocks")
            && !HasMarker(run, 6, 38));
        Check("C5 missing holder keeps the other land6 definitions granting",
            run.PendingCount == 3 && HasMarker(run, 6, 25) && HasMarker(run, 6, 13));

        // 2) 活动激活：Birthday 经真实放置观察提交 marker。
        run = RunLand6(() => EventStub(true));
        Check("C5 active event grants the birthday block through the real pipeline",
            run.BirthdayPending && run.PendingCount == 4
            && ContainsBlockNamed(run.List, "Steed Birthday_Blocks")
            && HasMarker(run, 6, 38) && Marks(run.Campaign, 6) == 4);

        // 3) 活动非激活：只挡 Birthday；其他定义照常。
        run = RunLand6(() => EventStub(false));
        Check("C5 inactive event blocks the birthday grant only",
            !run.BirthdayPending && run.PendingCount == 3
            && !HasMarker(run, 6, 38) && HasMarker(run, 6, 25) && HasMarker(run, 6, 13));

        // 4) data 缺失 / id 不符 / 非季节活动：一律不新授予。
        run = RunLand6(() => EventStub(true, missingData: true));
        Check("C5 missing challenge data blocks the birthday grant",
            !run.BirthdayPending && !HasMarker(run, 6, 38));
        run = RunLand6(() => EventStub(true, dataId: 11));
        Check("C5 wrong challenge id blocks the birthday grant",
            !run.BirthdayPending && !HasMarker(run, 6, 38));
        run = RunLand6(() => EventStub(true, seasonal: false));
        Check("C5 non-seasonal challenge data blocks the birthday grant",
            !run.BirthdayPending && !HasMarker(run, 6, 38));

        // 5) 原生调用抛异常：fail-closed、不外泄、其他定义照常。
        run = RunLand6(() => EventStub(true, throwOnData: true));
        Check("C5 challenge data exception fails closed without breaking others",
            !run.BirthdayPending && !HasMarker(run, 6, 38)
            && HasMarker(run, 6, 25) && HasMarker(run, 6, 13));
        run = RunLand6(() => EventStub(true, throwOnActive: true));
        Check("C5 event-active exception fails closed without breaking others",
            !run.BirthdayPending && !HasMarker(run, 6, 38)
            && HasMarker(run, 6, 25) && HasMarker(run, 6, 13));

        // 6) 活动过期 + 功能 OFF + 已有 marker：仍重建（InjectOnly 不经过活动门）。
        var campaign = Reset(new Scenario { Land = 6 });
        campaign.currentReign.landData[6].steedSpawns = new[] { (SteedType)38 };
        ModConfig.CrossWorldMountsEnabled.Value = false;
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C5 expired activity + OFF still rebuilds a granted birthday island",
            ContainsBlockNamed(list, "Steed Birthday_Blocks") && list.Count == 3
            && frame.Pending.Count == 0 && Marks(campaign, 6) == 1);
        CrossWorldMountRuntime.Abort(frame);

        // 7) 活动过期 + 功能 OFF + 设施回执：重建并自愈 marker。
        campaign = Reset(new Scenario { Land = 6 });
        campaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/Steed_BirthdayParty_Abandoned" });
        ModConfig.CrossWorldMountsEnabled.Value = false;
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C5 expired activity + OFF + receipt rebuilds and heals the marker",
            ContainsBlockNamed(list, "Steed Birthday_Blocks") && list.Count == 3
            && Marks(campaign, 6) == 1);
        CrossWorldMountRuntime.Abort(frame);

        // 8) 已访问/已游玩岛：活动晚到不补发（既有 visited/played 门）；未 Close 前无 marker 写入。
        run = RunLand6(() => { EventStub(true); CampaignSaveData.current.visitedIslands.Add(6); }, close: false);
        Check("C5 visited island receives no late-event birthday grant",
            !HasPendingDefinition(run, Birthday) && run.Frame.Pending.Count == 0
            && Marks(run.Campaign, 6) == 0);
        CrossWorldMountRuntime.Abort(run.Frame);
        run = RunLand6(() => { EventStub(true); CampaignSaveData.current.currentReign.landData[6].lastPlayedTimeDays = 3d; }, close: false);
        Check("C5 already-played island receives no late-event birthday grant",
            !HasPendingDefinition(run, Birthday) && Marks(run.Campaign, 6) == 0);
        CrossWorldMountRuntime.Abort(run.Frame);

        // 9) 活动门不进入依赖注册/查询：无 holder 时 Birthday 依赖与全体映射仍就绪。
        Reset(new Scenario { Land = 6 });
        CrossWorldMountDependencies.EnsureAll();
        Check("C5 seasonal gate stays out of dependency registration and queries",
            IsReady(Def(38)) && CrossWorldMountDependencies.AllSteedsReady);

        // 10) 非季节定义不受活动状态影响：land5（santa/gamigin）在活动非激活时照常授予。
        campaign = Reset(new Scenario { Land = 5 });
        EventStub(false);
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C5 non-seasonal definitions grant normally while the event is inactive",
            frame.Pending.Any(p => p.Definition.Id == "santahouse.reindeer")
            && frame.Pending.Any(p => p.Definition.Id == "woodlands.gamigin"));
        CrossWorldMountRuntime.Abort(frame);
    }

    // ------------------------------------------------------------------ review fixes: boxed-reign owner identity + strict scope

    private static void Contract6_OwnerIdentityAndScope()
    {
        // A. ABI 前提：get_currentReign 每次新包装、共享 landData；currentCampaign<0 → null。
        var campaign = Reset(new Scenario { Land = 4 });
        var assets = BiomeHolder.Inst.curBiomeAssets;
        var read1 = campaign.currentReign;
        var read2 = campaign.currentReign;
        Check("C6 boxed reign ABI: fresh wrapper each read, shared landData",
            !ReferenceEquals(read1, read2) && read1.Pointer != read2.Pointer
            && ReferenceEquals(read1.landData, read2.landData));
        Check("C6 global owner getter ABI: currentCampaign<0 yields null",
            RunGetCurrentCampaignWith(-1, campaign) == null);

        // B. 正常提交：稳定身份下放置证实后必须写入 marker（旧 boxed 指针比较会拒绝）。
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C6 pending kirin published", frame.Pending.Any(p => p.Definition.SteedTypeId == 6));
        var level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        Check("C6 normal close commits marker despite boxed reign getter",
            campaign.currentReign.landData[4].steedSpawns.Any(v => (int)v == 6));

        // C. 切王朝：捕获后 reign 变化 → 拒写。
        campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        campaign.reign = 5;
        level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        Check("C6 reign switch after capture refuses marker", Marks(campaign, 4) == 0);

        // D. 换 landData 容器（同 campaign/reign）→ 拒写（持原容器引用核对未被写入）。
        campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        var originalLandData = campaign.currentReign.landData;
        campaign.currentReign = new CampaignSaveData.ReignInfo();
        var replacedLevel = new Level();
        Observe(frame, replacedLevel);
        CrossWorldMountRuntime.Close(frame, replacedLevel);
        Check("C6 landData container swap after capture refuses marker",
            originalLandData[4].steedSpawns.Length == 0);

        // E. 切 campaign → 拒写。
        campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        var other = new CampaignSaveData { currentLand = 4 };
        for (int i = 0; i < 10; i++) other.currentReign.landData.Add(new CampaignSaveData.LandMapData());
        CampaignSaveData.current = other;
        GlobalSaveData.loaded.CurrentCampaignProvider = other;
        var otherLevel = new Level();
        Observe(frame, otherLevel);
        CrossWorldMountRuntime.Close(frame, otherLevel);
        Check("C6 campaign switch after capture refuses marker", Marks(campaign, 4) == 0);
        CampaignSaveData.current = campaign;
        GlobalSaveData.loaded.CurrentCampaignProvider = campaign;

        // F. 无准确 Global owner（菜单/无选中回退）：拒写。
        campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        GlobalSaveData.loaded.currentCampaign = -1;
        var menuLevel = new Level();
        Observe(frame, menuLevel);
        CrossWorldMountRuntime.Close(frame, menuLevel);
        Check("C6 unproven Global owner refuses marker", Marks(campaign, 4) == 0);
        GlobalSaveData.loaded.currentCampaign = 0;

        // G. owner 不一致（provider 指向别的对象）→ 拒写。
        campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        GlobalSaveData.loaded.CurrentCampaignProvider = new CampaignSaveData();
        var mismatchLevel = new Level();
        Observe(frame, mismatchLevel);
        CrossWorldMountRuntime.Close(frame, mismatchLevel);
        Check("C6 mismatched Global owner refuses marker", Marks(campaign, 4) == 0);

        // H. 查询/生成 scope 反例（review 探针 case + 换代/菜单扩展）。
        campaign = Reset(new Scenario { Land = 4 });
        assets = BiomeHolder.Inst.curBiomeAssets;
        var unicorn = SteedOf("NativeUnicorn", 6);
        var spawn = new SteedSpawn { gameObject = new GameObject("BlossomTree(Clone)") };
        CrossWorldMountDependencies.EnsureAll();
        campaign.currentReign.landData[4].steedSpawns = new[] { (SteedType)6 };
        Check("C6 setup marker grants Kirin", !ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));

        GlobalSaveData.loaded = null;
        Check("C6 unknown Global leaves native query", ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
        Check("C6 unknown Global leaves native spawn", ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));

        GlobalSaveData.loaded = new GlobalSaveData();
        Check("C6 unproven owner leaves native query", ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
        Check("C6 unproven owner leaves native spawn", ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));
        GlobalSaveData.loaded = new GlobalSaveData { CurrentCampaignProvider = campaign };
        Check("C6 restored owner grants again", !ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));

        GlobalSaveData.loaded.currentCampaign = -1;   // 菜单/无选中：原生 current 会回退 slot0
        Check("C6 menu fallback (currentCampaign=-1) leaves native query",
            ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
        Check("C6 menu fallback (currentCampaign=-1) leaves native spawn",
            ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));
        GlobalSaveData.loaded.currentCampaign = 0;

        BiomeHolder.Inst.curBiomeAssets = null;
        Check("C6 missing current assets leaves native spawn", ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));
        Check("C6 missing current assets leaves native query", ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
        BiomeHolder.Inst.curBiomeAssets = assets;

        BiomeHolder.Inst.curBiomeAssets = new BiomeSpecificAssets();   // 换代未重验
        Check("C6 unverified asset generation leaves native spawn",
            ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));
        BiomeHolder.Inst.curBiomeAssets = assets;

        // I. receipt 兜底身份：换王朝（reign 整数）或换 landData 容器立即失效。
        campaign = Reset(new Scenario { Land = 4 });
        assets = BiomeHolder.Inst.curBiomeAssets;
        CrossWorldMountDependencies.EnsureAll();
        campaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/BlossomTree" });
        Check("C6 receipt grants before identity change", !ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
        campaign.island.objects.Clear();
        campaign.reign = 7;
        Check("C6 new reign drops the cached receipt grant",
            ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));

        campaign = Reset(new Scenario { Land = 4 });
        assets = BiomeHolder.Inst.curBiomeAssets;
        CrossWorldMountDependencies.EnsureAll();
        campaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/BlossomTree" });
        Check("C6 receipt grants before container change", !ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
        campaign.currentReign = new CampaignSaveData.ReignInfo();
        for (int i = 0; i < 10; i++) campaign.currentReign.landData.Add(new CampaignSaveData.LandMapData());
        campaign.island.objects.Clear();
        Check("C6 new landData container drops the cached receipt grant",
            ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
    }

    /// <summary>测试 helper：按给定 currentCampaign 读取桩的 GetCurrentCampaign()。</summary>
    private static CampaignSaveData RunGetCurrentCampaignWith(int currentCampaign, CampaignSaveData provider)
    {
        var global = new GlobalSaveData { currentCampaign = currentCampaign, CurrentCampaignProvider = provider };
        return global.GetCurrentCampaign();
    }

    // ------------------------------------------------------------------ review R2: pay animation route (Sakura backup body)

    private static SteedSpawn SakuraSpawn(string name = "BlossomTree(Clone)")
    {
        var go = new GameObject(name);
        var spawn = new SteedSpawn { gameObject = go };
        go.Components.Add(spawn);
        return spawn;
    }

    private static void Contract7_PayRoute()
    {
        // A. 匹配设施 + 已授予 + 准确 scope：原数组/实例保持，窗口内 copy 携带 Kirin，
        //    模拟原生 Pay 取 array[0] 造出 Kirin 备用体；Postfix/Finalizer 归还。
        var campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        campaign.currentReign.landData[4].steedSpawns = new[] { (SteedType)6 };
        Steed kirin = Prefab("Prefabs/Steeds/Kirin");
        Steed unicorn = SteedOf("ReloadedUnicorn", 6);   // 设施保存重载后的 Unicorn 初始数组实例
        var spawn = SakuraSpawn();
        var original = new Il2CppReferenceArray<Steed>(new[] { unicorn });
        spawn.steeds = original;
        spawn.steedPool = new Il2CppSystem.Collections.Generic.List<Steed> { unicorn };

        var scope = CrossWorldMountDependencies.BeginPayRoute(spawn);
        Check("C7 granted Sakura facility gets a routed private copy",
            scope != null && scope.Installed && !ReferenceEquals(spawn.steeds, original));
        var installed = spawn.steeds;
        Check("C7 pay window copy carries Kirin",
            installed != null && installed.Length == 1 && ReferenceEquals(installed[0], kirin));
        Check("C7 original array instance/content untouched",
            original.Length == 1 && ReferenceEquals(original[0], unicorn) && (int)original[0].steedType == 6);
        Check("C7 steedPool untouched",
            spawn.steedPool.Count == 1 && ReferenceEquals(spawn.steedPool[0], unicorn));

        spawn.Pay();   // 模拟原生 Pay 动画分支：Instantiate(steeds[0]) 造备用体
        Check("C7 backup body built in the pay window is Kirin", ReferenceEquals(spawn.LastBackupPrefab, kirin));

        CrossWorldMountDependencies.RestorePayRoute(scope);
        Check("C7 restore returns the original array reference",
            ReferenceEquals(spawn.steeds, original) && ReferenceEquals(spawn.steeds[0], unicorn));
        CrossWorldMountDependencies.RestorePayRoute(scope);
        Check("C7 repeated restore is idempotent (still original, content intact)",
            ReferenceEquals(spawn.steeds, original) && ReferenceEquals(original[0], unicorn));

        // B. 真实 patch 反射：Prefix 安装；Finalizer 归还且原异常继续抛出；Postfix 路径同样归还。
        spawn = SakuraSpawn();
        original = new Il2CppReferenceArray<Steed>(new[] { unicorn });
        spawn.steeds = original;
        MethodInfo prefix = typeof(PatchRide_CrossWorldMount_Pay).GetMethod("Prefix", PrivateStatic);
        MethodInfo postfix = typeof(PatchRide_CrossWorldMount_Pay).GetMethod("Postfix", PrivateStatic);
        MethodInfo finalizer = typeof(PatchRide_CrossWorldMount_Pay).GetMethod("Finalizer", PrivateStatic);
        object[] prefixArgs = { spawn, null };
        prefix.Invoke(null, prefixArgs);
        Check("C7 real pay prefix installs the routed copy",
            spawn.steeds != null && !ReferenceEquals(spawn.steeds, original)
            && ReferenceEquals(spawn.steeds[0], kirin));
        var fault = new InvalidOperationException("stub pay fault");
        var returned = (Exception)finalizer.Invoke(null, new object[] { fault, prefixArgs[1] });
        Check("C7 finalizer restores and rethrows the original exception",
            ReferenceEquals(returned, fault) && ReferenceEquals(spawn.steeds, original));

        prefixArgs = new object[] { spawn, null };
        prefix.Invoke(null, prefixArgs);
        postfix.Invoke(null, new[] { prefixArgs[1] });
        Check("C7 postfix restores the original array", ReferenceEquals(spawn.steeds, original));

        // C. 无需路由 / 任一 scope 门未过：一律不装、零半状态。
        campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        spawn = SakuraSpawn();
        original = new Il2CppReferenceArray<Steed>(new[] { unicorn });
        spawn.steeds = original;
        Check("C7 un-granted facility is not touched",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, original));

        campaign.currentReign.landData[4].steedSpawns = new[] { (SteedType)6 };
        spawn = SakuraSpawn("BlossomTreeDark(Clone)");
        original = new Il2CppReferenceArray<Steed>(new[] { unicorn });
        spawn.steeds = original;
        Check("C7 non-matching facility is not touched",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, original));

        BiomeHolder.Inst.BiomeIndex = 0;
        Check("C7 other world is not touched",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, original));
        BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
        NetworkBigBoss.IsOnline = true;
        Check("C7 online is not touched",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, original));
        NetworkBigBoss.IsOnline = false;
        GlobalSaveData.loaded.InChallenge = true;
        Check("C7 challenge is not touched",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, original));
        GlobalSaveData.loaded.InChallenge = false;
        GlobalSaveData.loaded.CurrentCampaignProvider = null;   // unknown owner
        Check("C7 unknown owner is not touched",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, original));

        // 已是正确变体（Kirin）：changed=false，不产生 copy（Reset 换代后需重取资源实例）。
        GlobalSaveData.loaded.CurrentCampaignProvider = campaign;
        kirin = Prefab("Prefabs/Steeds/Kirin");
        spawn = SakuraSpawn();
        var already = new Il2CppReferenceArray<Steed>(new[] { kirin });
        spawn.steeds = already;
        Check("C7 already-routed array produces no copy",
            CrossWorldMountDependencies.BeginPayRoute(spawn) == null && ReferenceEquals(spawn.steeds, already));
    }

    // ------------------------------------------------------------------ root addendum: early owner gate before any injection

    private static void Contract8_EarlyOwnerGate()
    {
        // 无选中 Global（GetCurrentCampaign=null）但 Campaign.current 仍可读（原生回退 slot0）：
        // Apply 在发布前即拒绝——零新增块、零 pending、原 result 保持、零 marker。
        var campaign = Reset(new Scenario { Land = 4 });
        CrossWorldMountDependencies.EnsureAll();
        GlobalSaveData.loaded.currentCampaign = -1;
        CrossWorldMountRuntime.Frame frame = CrossWorldMountRuntime.Open();
        L list = Blocks("Clearing_Blocks", "End");
        L before = list;
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C8 unproven owner publishes nothing and keeps the caller list",
            ReferenceEquals(before, list) && list.Count == 2 && frame.Pending.Count == 0);
        CrossWorldMountRuntime.Close(frame, new Level());
        Check("C8 unproven owner writes no marker", Marks(campaign, 4) == 0);

        // 既有有效上下文仍全过：恢复准确 owner 后正常注入、放置、提交 marker。
        GlobalSaveData.loaded.currentCampaign = 0;
        frame = CrossWorldMountRuntime.Open();
        list = Blocks("Clearing_Blocks", "End");
        CrossWorldMountRuntime.TryApply(ref list);
        Check("C8 restored owner publishes blocks again", frame.Pending.Count == 3 && list.Count == 5);
        var level = new Level();
        Observe(frame, level);
        CrossWorldMountRuntime.Close(frame, level);
        Check("C8 restored owner commits markers", Marks(campaign, 4) == 3);
    }

    // ------------------------------------------------------------------ astra R3 residual: receipt negative cache is land-bound

    private static void Contract9_ReceiptLandBoundary()
    {
        var campaign = Reset(new Scenario { Land = 4 });
        var assets = BiomeHolder.Inst.curBiomeAssets;
        CrossWorldMountDependencies.EnsureAll();
        Steed kirin = Prefab("Prefabs/Steeds/Kirin");
        Steed unicorn = SteedOf("NativeUnicorn2", 6);
        var spawn = SakuraSpawn();

        // A. 岛 A（land 4）：无 marker、无 receipt → 查询写入阴性缓存（不调用任何清理 API）。
        Check("C9 island A stays native (negative cached)",
            ReferenceEquals(Resolve(assets, 6, unicorn), unicorn)
            && ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));

        // B. 立即切岛 B（land 5，无 marker、仅设施回执）→ query 与 Spawn 立即 Kirin：
        //    不被 A 的 2 秒阴性挡住（不等待、不调 InvalidateGrantCache）。
        Managers.Inst.game.currentLand = 5;
        campaign.currentLand = 5;
        campaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/BlossomTree" });
        Check("C9 island B receipt grants immediately for query and spawn",
            ReferenceEquals(Resolve(assets, 6, unicorn), kirin)
            && ReferenceEquals(RouteSpawn(spawn, unicorn), kirin));

        // C. 正结果不跨 reign 沿用：新王朝无 marker 无 receipt → 立即 native。
        campaign.currentReign = new CampaignSaveData.ReignInfo();
        for (int i = 0; i < 10; i++) campaign.currentReign.landData.Add(new CampaignSaveData.LandMapData());
        campaign.island.objects.Clear();
        Check("C9 positive grant does not carry into a new reign",
            ReferenceEquals(Resolve(assets, 6, unicorn), unicorn)
            && ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));

        // D. CurrentLand 未知：receipt 兜底 fail-closed（即使有回执也不授权、也不写入阴性）；
        //    land 恢复已知后同一回执立即生效。
        campaign = Reset(new Scenario { Land = 4 });
        assets = BiomeHolder.Inst.curBiomeAssets;
        CrossWorldMountDependencies.EnsureAll();
        campaign.island.objects.Add(new IslandSaveData.ObjectData
        { prefabPath = "Prefabs/Environment/MountAreas/BlossomTree" });
        Managers.Inst.game.currentLand = -1;
        campaign.currentLand = -1;
        Check("C9 unknown current land fails closed for the receipt fallback",
            ReferenceEquals(Resolve(assets, 6, unicorn), unicorn)
            && ReferenceEquals(RouteSpawn(spawn, unicorn), unicorn));
        Managers.Inst.game.currentLand = 4;
        campaign.currentLand = 4;
        Check("C9 known land again grants from the island receipt",
            !ReferenceEquals(Resolve(assets, 6, unicorn), unicorn));
    }

    private static void Main()
    {
        try
        {
            Contract1_Publish();
            Contract2_Registration();
            Contract2_GrantGate();
            Contract3_Pools();
            Contract4_VariantOverride();
            Contract5_SeasonalEligibility();
            Contract6_OwnerIdentityAndScope();
            Contract7_PayRoute();
            Contract8_EarlyOwnerGate();
            Contract9_ReceiptLandBoundary();
        }
        catch (Exception e)
        {
            Fail("adapter crashed: " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace);
        }
        Console.WriteLine((_failed == 0 ? "ALL PASS" : "FAILURES " + _failed) + " checks=" + _checks);
        Environment.Exit(_failed == 0 ? 0 : 1);
    }
}
