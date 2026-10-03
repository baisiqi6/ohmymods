using System;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using static Harness;

static class Program
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static object Invoke(string name, params object[] args) => typeof(BankAssistantCoordinator)
        .GetMethod(name, PrivateStatic).Invoke(null, args);
    static void InvokeInstance(object instance, string name)
        => instance.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(instance, null);

    sealed class Env
    {
        internal readonly Fixture F = Fixture.BuildGreek();
        internal readonly Banker B;
        internal readonly BankAssistantCoordinator C;
        internal readonly GameObject Actor;
        internal readonly object Helper;
        internal Env()
        {
            B = F.AddBanker();
            // 旧 PP 时代的隐式首次 seed 已废弃（无收据不造账）：按真实原生 Apply 收据建立账户 Live。
            True(Fixture.SeedNative(B, 100), "bank account seeded");
            C = B.gameObject.AddComponent<BankAssistantCoordinator>();
            BankAssistantCoordinator.AttachTo(B);
            SetStatic(typeof(BankAssistantCoordinator), "_hadAuthority", true);
            Actor = Sim.NewActor("KEM_BankAssistant_0_test", F.Layer);
            Helper = Assistant(0);
            SetField(Helper, "Actor", Actor);
            SetField(Helper, "Animator", Actor.AddComponent<Animator>());
            SetField(Helper, "PositionSync", Actor.AddComponent<PositionSync>());
            RegisterPooled(Actor);
        }
        internal DroppableCurrency Claim(bool sweep = false)
        {
            var coin = F.AddCoin(sweep ? 9f : 8f);
            True((bool)Invoke(sweep ? "TryClaimSweepCoin" : "TryAssign", Helper, coin), "native claim acquired");
            Eq((int)PickUpPolicy.OnlyClaimer, (int)coin.pickUpPolicy, "exclusive policy applied");
            return coin;
        }
        internal void ForeignExit()
        {
            BiomeHolder.Inst.BiomeIndex = 1;
            CoordinatorUpdate(C);
        }
    }

    static void Restored(DroppableCurrency coin)
    {
        True(coin.friendlyClaimer == null, "friendly claim released");
        Eq((int)PickUpPolicy.Everyone, (int)coin.pickUpPolicy, "native policy restored");
    }

    /// <summary>
    /// 真实池路径 fixture：走生产 EnsurePools/EnsureEightActors 与原生式 SpawnGO 克隆
    /// （HideAndDontSave 标志、DontSave 过滤、_activeCache 登记），覆盖 issue-81 的
    /// 丢槽回认、目标回池与失权迁移；除槽位引用外不手写任何生产状态。
    /// atlasReady=false（默认，stub 图集未就绪）→ 只登记原四槽；true → 全八槽。
    /// </summary>
    sealed class PoolEnv
    {
        internal readonly Fixture F;
        internal readonly Banker B;
        internal readonly BankAssistantCoordinator C;
        internal readonly GameObject[] Original;
        internal readonly Pool[] Pools;
        internal readonly int Slots;
        internal PoolEnv(bool atlasReady = false, float sourceBankerY = 1f)
        {
            if (atlasReady) BankAssistantAtlasVisuals.Available = true;
            F = Fixture.BuildGreek();
            B = F.AddBanker();
            // 源 banker 自身高度（原生/用户可任意）：助手槽取值必须与它无关。
            B.transform.localScale = new Vector3(1f, sourceBankerY, 1f);
            // 旧 PP 时代的隐式首次 seed 已废弃（无收据不造账）：按真实原生 Apply 收据建立账户 Live。
            True(Fixture.SeedNative(B, 100), "bank account seeded");
            C = B.gameObject.AddComponent<BankAssistantCoordinator>();
            BankAssistantCoordinator.AttachTo(B);
            RegisterBankerControllers();
            PatchEconomy_BankAssistants.EnsurePools(B, F.Pools);
            Invoke("EnsureEightActors", F.Layer);
            Slots = atlasReady ? 8 : 4;
            Original = Enumerable.Range(0, Slots)
                .Select(i => GetField<GameObject>(Assistant(i), "Actor")).ToArray();
            Pools = Original.Select(Pool.GetPoolByInstance).ToArray();
            True(BankAssistantCoordinator.Instance == C, "coordinator bound to the main banker");
            True(Original.All(a => a != null && a.activeInHierarchy), Slots + " initial live actors");
            Eq(Slots, Pool.SpawnGoCalls, "initial spawns");
            Eq(Slots, Pools.Sum(p => p._activeCache.Count), "initial pool membership");
            Eq(0, UnityEngine.Object.FindObjectsOfType<PositionSync>().Length, "DontSave excludes component query");
        }
        internal DroppableCurrency DepartTarget()
        {
            var coin = F.AddCoin(12f);
            SetField(Assistant(0), "Target", coin);
            // 原生回池：币离开当前 gameLayer 并被停用（挂到层外父节点）。
            coin.transform.Parent = new GameObject("CurrencyPoolOutsideLayer").transform;
            coin.gameObject.SetActive(false);
            return coin;
        }
        internal int SameActors() => Enumerable.Range(0, Slots)
            .Count(i => GetField<GameObject>(Assistant(i), "Actor") == Original[i]);
        internal int PoolActive() => Pools.Sum(p => p._activeCache.Count);
    }

    // ---- trip-target / gap-wait observables ----
    static bool ActiveCollector(int index)
        => ((bool[])GetStatic(typeof(BankAssistantCoordinator), "ActiveCollector"))[index];

    static void ActivateCollector(int index)
        => SetStaticField(typeof(BankAssistantCoordinator), "ActiveCollector", true);

    // ---- issue-89 observables（轮次 owner / 激活槽 / claim 记账） ----
    static int ActiveCount()
    {
        bool[] flags = (bool[])GetStatic(typeof(BankAssistantCoordinator), "ActiveCollector");
        int count = 0;
        for (int i = 0; i < flags.Length; i++) if (flags[i]) count++;
        return count;
    }

    static BankAssistantCoinOriginKind RoundKindOf(int index)
        => GetField<BankAssistantCoinOriginKind>(Assistant(index), "RoundKind");

    static Player RoundOwnerOf(int index)
        => GetField<Player>(Assistant(index), "PlayerRoundOwner");

    static int ClaimCount()
    {
        object claims = GetStatic(typeof(BankAssistantCoordinator), "Claims");
        return (int)claims.GetType().GetProperty("Count").GetValue(claims);
    }

    static bool ObservedContains(int instanceId)
    {
        object observed = GetStatic(typeof(BankAssistantCoordinator), "Observed");
        return (bool)observed.GetType().GetMethod("ContainsKey").Invoke(observed, new object[] { instanceId });
    }

    static float WaitDeadline(object helper) => GetField<float>(helper, "WaitDeadline");

    static void GapIsFresh(object helper, string label)
        => True(Math.Abs(WaitDeadline(helper) - (Time.time + 4.2f)) < 0.01f, label);

    /// <summary>One update after a non-multiple-of-interval advance so each call scans exactly once.</summary>
    static void ScanTick(BankAssistantCoordinator coordinator, float seconds = 0.61f)
    {
        Advance(seconds);
        CoordinatorUpdate(coordinator);
    }

    static int Main()
    {
        Test("idle assistants survive unknown receipt and are removed in foreign world", () =>
        {
            var e = new Env(); BiomeHolder.Inst = null; CoordinatorUpdate(e.C);
            True(GetField<GameObject>(e.Helper, "Actor") == e.Actor, "idle actor retained");
            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 1 };
            BankAssistantCoordinator.TickPendingCleanup(); Eq(1, Pool.DespawnCalls);
        });
        Test("idle authority-loss keeps cleanup responsibility until foreign authority returns", () =>
        {
            var e = new Env(); NetworkBigBoss.HasWorldAuth = false; CoordinatorUpdate(e.C);
            BiomeHolder.Inst.BiomeIndex = 1; BankAssistantCoordinator.TickPendingCleanup();
            True(e.Actor.Alive, "client cannot despawn"); Eq(0, Pool.DespawnCalls);
            True(GetField<GameObject>(e.Helper, "Actor") == e.Actor, "idle owner retained");
            NetworkBigBoss.HasWorldAuth = true; BankAssistantCoordinator.TickPendingCleanup();
            Eq(1, Pool.DespawnCalls);
        });
        Test("destroyed old actors do not block client late binding", () =>
        {
            var e = new Env(); NetworkBigBoss.HasWorldAuth = false; CoordinatorUpdate(e.C);
            UnityEngine.Object.Destroy(e.Actor);
            BankAssistantCoordinator.TickPendingCleanup();
            False((bool)GetStatic(typeof(BankAssistantCoordinator), "_cleanupPending"), "dead native records cleared locally");
            Eq(0, Pool.DespawnCalls);
        });
        Test("foreign exit restores target before despawning assistant; no second credit", () =>
        {
            var e = new Env(); var coin = e.Claim();
            SetField(e.Helper, "CarriedCoins", 4);
            e.ForeignExit(); Restored(coin);
            Eq(100, e.B._stashedCoins); Eq(0, PlayerPrefs.SetIntCalls);
            Eq(1, Pool.DespawnCalls); Eq(-1, BankAssistantCoordinator.GetStashedCoinsForPanel());
        });
        Test("approach teleport keeps the actor's ground Y/Z when the coin is airborne", () =>
        {
            // 玩家实测根因回归：扔出的币还在空中弧线（Y=飞行高度）时被分配，
            // 接近瞬移必须保留助手自身的地面 Y/Z——旧实现整抄币坐标导致
            // 助手悬空出生且后续 X-only 移动永不回地（"空中平移"）。
            var e = new Env();
            e.Actor.transform.position = new Vector3(30f, 0.5f, 0.7f);   // 助手地面位（独特 Y/Z）
            var coin = e.F.AddCoin(8f);
            coin.transform.position = new Vector3(8f, 5.5f, 0f);          // 空中币：|dx|>6 → 瞬移触发
            True((bool)Invoke("TryAssign", e.Helper, coin), "native claim acquired");
            Eq(0.5f, e.Actor.transform.position.y, "actor keeps its ground Y (not the coin's flight Y)");
            Eq(0.7f, e.Actor.transform.position.z, "actor keeps its Z");
            True(Mathf.Abs(e.Actor.transform.position.x - 8f) <= 2.01f,
                "actor lands within the approach distance of the coin X");
        });
        Test("foreign exit restores sweep and target claims with their owner retained", () =>
        {
            var e = new Env(); var target = e.Claim(); var sweep = e.Claim(true);
            e.ForeignExit(); Restored(target); Restored(sweep);
            Eq(1, sweep.ClearClaimCalls); Eq(1, Pool.DespawnCalls);
        });
        Test("authority loss sends no claim RPC; regained authority restores both claims", () =>
        {
            var e = new Env(); var target = e.Claim(); var sweep = e.Claim(true);
            int rpc = target.PolicyRpcCalls + sweep.PolicyRpcCalls;
            NetworkBigBoss.HasWorldAuth = false;
            CoordinatorUpdate(e.C);
            Eq(rpc, target.PolicyRpcCalls + sweep.PolicyRpcCalls, "client has no policy writes");
            True(GetField<GameObject>(e.Helper, "Actor") == e.Actor, "owner receipt retained");
            NetworkBigBoss.HasWorldAuth = true;
            BankAssistantCoordinator.TickPendingCleanup();
            Restored(target); Restored(sweep); True(e.Actor.Alive, "auth migration does not destroy synced actor");
        });
        Test("unknown biome defers owned cleanup until world becomes known", () =>
        {
            var e = new Env(); var coin = e.Claim(); int rpc = coin.PolicyRpcCalls;
            BiomeHolder.Inst = null; CoordinatorUpdate(e.C);
            Eq(rpc, coin.PolicyRpcCalls); True(e.Actor.Alive, "unknown preserves actor");
            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 1 };
            BankAssistantCoordinator.TickPendingCleanup(); Restored(coin);
        });
        Test("failed final policy RPC retains receipt and retries before actor cleanup", () =>
        {
            var e = new Env(); var coin = e.Claim(); coin.FailPolicyRpc = 1;
            e.ForeignExit(); True(e.Actor.Alive, "actor retained until RPC succeeds");
            True(GetField<DroppableCurrency>(e.Helper, "Target") == coin, "target receipt retained");
            BankAssistantCoordinator.TickPendingCleanup(); Restored(coin); Eq(1, Pool.DespawnCalls);
        });
        Test("failed current-layer read is deferred rather than treated as old scene", () =>
        {
            var e = new Env(); var coin = e.Claim(); coin.transform.FailChildReads = 1;
            e.ForeignExit(); True(e.Actor.Alive, "transient read retained owner");
            True(GetField<DroppableCurrency>(e.Helper, "Target") == coin, "read fault retained receipt");
            BankAssistantCoordinator.TickPendingCleanup(); Restored(coin);
        });
        Test("old-scene target never receives cleanup RPC", () =>
        {
            var e = new Env(); var coin = e.Claim(); int rpc = coin.PolicyRpcCalls;
            Sim.SceneHandle = 9; var old = Sim.NewLayer("old");
            coin.transform.Parent = old.transform; coin.gameObject.scene = old.scene;
            e.ForeignExit(); Eq(rpc, coin.PolicyRpcCalls); Eq(0, coin.ClearClaimCalls);
        });
        Test("current coin owned by old-layer actor is restored without actor RPC", () =>
        {
            var e = new Env(); var coin = e.Claim();
            var sync = e.Actor.GetComponent<PositionSync>(); int sends = sync.SendCalls;
            Sim.SceneHandle = 9; var old = Sim.NewLayer("old");
            e.Actor.transform.Parent = old.transform; e.Actor.scene = old.scene;
            e.ForeignExit(); Restored(coin); Eq(sends, sync.SendCalls); Eq(0, Pool.DespawnCalls);
        });
        Test("external ownership and policy survive cleanup", () =>
        {
            var e = new Env(); var coin = e.Claim(); int rpc = coin.PolicyRpcCalls;
            var other = Sim.NewActor("nativeClaimer", e.F.Layer);
            coin.friendlyClaimer = other; coin.pickUpPolicy = PickUpPolicy.Nobody;
            e.ForeignExit(); True(coin.friendlyClaimer == other, "external claimer kept");
            Eq((int)PickUpPolicy.Nobody, (int)coin.pickUpPolicy); Eq(rpc, coin.PolicyRpcCalls);
        });
        Test("foreign and old-layer coins cannot acquire new assistant claims", () =>
        {
            var e = new Env(); var coin = e.F.AddCoin(8f);
            BiomeHolder.Inst.BiomeIndex = 1;
            False((bool)Invoke("TryAssign", e.Helper, coin), "foreign target rejected");
            False((bool)Invoke("TryClaimSweepCoin", e.Helper, coin), "foreign sweep rejected");
            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            coin.transform.Parent = Sim.NewLayer("other").transform;
            False((bool)Invoke("TryAssign", e.Helper, coin), "old layer rejected");
            Eq(0, coin.ClaimCalls); Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("direct public binding cannot replace canonical banker", () =>
        {
            var e = new Env(); var other = e.F.AddBanker("other", false);
            PatchEconomy_BankAssistants.EnsureForMainBanker(other);
            BankAssistantCoordinator.AttachTo(other);
            True(BankAssistantCoordinator.MainBanker == e.B, "canonical reference retained");
            True(other.GetComponent<BankAssistantCoordinator>() == null, "no foreign coordinator injected");
        });
        Test("foreign destroy retires the account actor without any legacy PP write", () =>
        {
            var e = new Env(); PatchEconomy_Banker.FinaliseEmerge_Prefix(e.B);
            BiomeHolder.Inst.BiomeIndex = 1; e.B._stashedCoins = 17;
            PatchEconomy_Banker.OnDestroy_Prefix(e.B);
            // 新契约：退休读取同一 actor 的末次准确值并更新 R3 Live（旧 PP 全局键已不存在）。
            Eq(17, BankLive(), "retire keeps the actor's last accurate read");
            Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP ledger write");
        });
        Test("disabled banker's same-world reentry keeps the account prime and observes the delta", () =>
        {
            // bank-native R2 有意变更：biome 翻转不再吊销 prime（旧 SuspendPrimeProof 已删除），
            // 同 world 的 actor delta 会被观察进 R3 Live；希腊 scope 只闸经济入口。
            var e = new Env(); PatchEconomy_Banker.FinaliseEmerge_Prefix(e.B);
            BiomeHolder.Inst.BiomeIndex = 1; e.B.enabled = false;
            PatchEconomy_Banker.TickOwnedProfiles(); e.B._stashedCoins = 17;
            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            PatchEconomy_Banker.Update_Prefix(e.B); Eq(17, e.B._stashedCoins, "same-world delta kept");
            e.B._stashedCoins += 5; PatchEconomy_Banker.Update_Postfix(e.B);
            Eq(22, BankLive(), "ledger follows the observed delta");
        });
        Test("panel tick binds disabled client after Awake before SetParent", () =>
        {
            var f = Fixture.BuildGreek(); var b = f.AddBanker();
            b.transform.Parent = null; b.enabled = false; NetworkBigBoss.HasWorldAuth = false;
            PatchEconomy_Banker.Awake_Postfix(b);
            True(b.GetComponent<BankAssistantCoordinator>() == null, "not ready in Awake");
            b.transform.Parent = f.Layer;
            PatchEconomy_Banker.TickOwnedProfiles();
            True(b.GetComponent<BankAssistantCoordinator>() != null, "late-ready bound without native Update");
            Eq(1.95f, b.walkSpeed); Eq(0, PlayerPrefs.SetIntCalls);
            BiomeHolder.Inst.BiomeIndex = 1; PatchEconomy_Banker.TickOwnedProfiles();
            Eq(Fixture.NativeWalk, b.walkSpeed, "disabled owner's original value restored");
        });
        Test("an empty snapshot keeps a carried collector waiting one bounded gap, then homes it", () =>
        {
            var e = new Env();
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);
            float startX = e.Actor.transform.position.x;

            ScanTick(e.C, 1.0f);
            True(ActiveCollector(0), "a carried collector stays on the trip while waiting");
            GapIsFresh(e.Helper, "the first break establishes exactly one bounded gap");
            float deadline = WaitDeadline(e.Helper);
            Eq(5, GetField<int>(e.Helper, "CarriedCoins"), "carried coins are kept for the trip");
            Eq(startX, e.Actor.transform.position.x, "the waiting actor stands still");
            Eq(0, Pool.DespawnCalls);
            Eq(100, e.B._stashedCoins);
            Eq(0, PlayerPrefs.SetIntCalls);

            ScanTick(e.C);
            Eq(deadline, WaitDeadline(e.Helper), "later scans do not extend the gap");
            ScanTick(e.C); ScanTick(e.C); ScanTick(e.C);
            Eq(deadline, WaitDeadline(e.Helper), "the gap deadline stays fixed");
            True(ActiveCollector(0), "still waiting inside the gap");
            Eq(0, PlayerPrefs.SetIntCalls);

            ScanTick(e.C);
            True(ActiveCollector(0), "the gap is still open before expiry");
            ScanTick(e.C);
            True(ActiveCollector(0), "the gap is untouched until its deadline");
            ScanTick(e.C);
            False(ActiveCollector(0), "an expired gap homes the collector");
            Eq(0, GetField<int>(e.Helper, "CarriedCoins"), "carried coins are closed out at home");
            Eq(0f, WaitDeadline(e.Helper), "the deadline is cleared when the trip ends");
            True(Math.Abs(e.Actor.transform.position.x + 1.65f) < 0.01f, "the actor is back at its home slot");
            Eq(100, e.B._stashedCoins);
            Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("a coin maturing inside the gap resumes the same trip", () =>
        {
            var e = new Env();
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);

            ScanTick(e.C, 1.0f);
            GapIsFresh(e.Helper, "bounded gap established on an empty snapshot");
            float deadline = WaitDeadline(e.Helper);
            var coin = e.F.AddCoin(8f);

            for (int i = 0; i < 5; i++) ScanTick(e.C);
            Eq(deadline, WaitDeadline(e.Helper), "a maturing coin does not extend the gap");
            True(GetField<DroppableCurrency>(e.Helper, "Target") == null, "an immature coin is not claimed");
            Eq(0, coin.ClaimCalls);
            Eq(0, coin.PolicyRpcCalls);

            Advance(0.30f);
            Advance(0.31f);
            CoordinatorUpdate(e.C);
            Eq(0f, WaitDeadline(e.Helper), "a new target clears the pending gap");
            True(GetField<DroppableCurrency>(e.Helper, "Target") == coin, "the matured coin is assigned");
            True(ActiveCollector(0), "the collector keeps its slot");
            Eq(1, coin.ClaimCalls);
            Eq(1, coin.PolicyRpcCalls);
            Eq(5, GetField<int>(e.Helper, "CarriedCoins"), "nothing is credited before the coin is reached");
            Eq(100, e.B._stashedCoins);
            Eq(0, PlayerPrefs.SetIntCalls);
            float actorX = e.Actor.transform.position.x;
            True(actorX > 6.5f && actorX < 7.1f, "the actor runs at the coin instead of homing");
        });
        Test("the twentieth coin closes the trip without eating a twenty-first", () =>
        {
            var e = new Env();
            ActivateCollector(0);
            SetField(e.Helper, "CarriedCoins", 19);
            var target = e.F.AddCoin(5.0f);
            var neighbour = e.F.AddCoin(5.2f);

            ScanTick(e.C, 1.0f);
            for (int i = 0; i < 4; i++) ScanTick(e.C);
            True(GetField<DroppableCurrency>(e.Helper, "Target") == null, "no target before maturity");
            True(ActiveCollector(0), "the carried collector waits instead of homing");

            ScanTick(e.C);
            True(GetField<DroppableCurrency>(e.Helper, "Target") == target, "the nearest coin is assigned");
            ScanTick(e.C, 0.31f);
            ScanTick(e.C, 0.31f);
            ScanTick(e.C, 0.31f);

            Eq(1, Pool.DespawnCalls, "only one coin is consumed");
            Eq(101, e.B._stashedCoins, "exactly one credit");
            Eq(101, BankLive(), "the credit is observed into the shared account once");
            Eq(0, GetField<int>(e.Helper, "CarriedCoins"), "the trip closes out at home");
            Eq(0f, WaitDeadline(e.Helper));
            False(ActiveCollector(0), "the collector is released at the trip target");
            True(Math.Abs(e.Actor.transform.position.x + 1.65f) < 0.01f, "the actor teleported home");
            False(neighbour.gameObject.Alive, "nearby sweep coin is the twentieth consumed coin");
            True(target.gameObject.Alive, "original target survives as the twenty-first candidate");
            Eq(1, target.ClearClaimCalls, "home releases the original target exactly once");
            Restored(target);
        });
        Test("an empty-handed collector is still released immediately", () =>
        {
            var e = new Env();
            ActivateCollector(0);
            var immature = e.F.AddCoin(8f);

            ScanTick(e.C, 1.0f);
            False(ActiveCollector(0), "no carried coins means no wait");
            Eq(0f, WaitDeadline(e.Helper));
            Eq(0, immature.ClaimCalls);
            Eq(0, Pool.DespawnCalls);
            Eq(-0.8f, e.Actor.transform.position.x, "released empty-handed actor resumes ordinary patrol rather than teleporting home");
        });
        Test("a restock lease ends a pending gap instead of inheriting it", () =>
        {
            var e = new Env();
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);
            ScanTick(e.C, 1.0f);
            True(WaitDeadline(e.Helper) > 0f, "waiting before the lease");

            True(BankAssistantCoordinator.TryReserveForRestock(out int index, out GameObject actor), "assistant leased");
            Eq(0, index);
            True(actor == e.Actor, "the waiting actor is the lease");
            Eq(0f, WaitDeadline(e.Helper), "the lease clears the pending gap");
            True(GetField<bool>(e.Helper, "RestockReserved"), "the lease is recorded");
            Eq(0, GetField<int>(e.Helper, "CarriedCoins"), "carried coins are closed out before the lease");
            False(ActiveCollector(0), "leased helper leaves collection duty");

            BankAssistantCoordinator.ReleaseRestockAssistant(index, actor, false);
            False(GetField<bool>(e.Helper, "RestockReserved"), "the lease is released");
            Eq(0f, WaitDeadline(e.Helper));

            SetField(e.Helper, "CarriedCoins", 4);
            ActivateCollector(0);
            ScanTick(e.C);
            GapIsFresh(e.Helper, "the next trip times its own bounded gap");
        });
        Test("authority loss, disable and world exit drop a pending gap", () =>
        {
            var e = new Env();
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);
            ScanTick(e.C, 1.0f);
            True(WaitDeadline(e.Helper) > 0f, "waiting");

            NetworkBigBoss.HasWorldAuth = false;
            CoordinatorUpdate(e.C);
            Eq(0f, WaitDeadline(e.Helper), "authority loss drops the gap before the deferred reset");

            NetworkBigBoss.HasWorldAuth = true;
            ModConfig.Enabled.Value = false;
            CoordinatorUpdate(e.C);
            Eq(0f, WaitDeadline(e.Helper), "disable drops the gap");
            Eq(1, Pool.DespawnCalls, "disable despawns the scoped actor");
            ModConfig.Enabled.Value = true;

            var second = Sim.NewActor("KEM_BankAssistant_0_second", e.F.Layer);
            SetField(e.Helper, "Actor", second);
            SetField(e.Helper, "Animator", second.AddComponent<Animator>());
            SetField(e.Helper, "PositionSync", second.AddComponent<PositionSync>());
            RegisterPooled(second);
            CoordinatorUpdate(e.C); // 本用例只验证断流 deadline；回认只能走本槽自有池（issue-81 用例覆盖）
            True(GetField<GameObject>(e.Helper, "Actor") == second, "fresh actor takes over after the disable");
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);
            ScanTick(e.C);
            True(WaitDeadline(e.Helper) > 0f, "waiting again after re-enable");

            BiomeHolder.Inst.BiomeIndex = 1;
            CoordinatorUpdate(e.C);
            Eq(0f, WaitDeadline(e.Helper), "world exit drops the gap");
            Eq(2, Pool.DespawnCalls, "world exit despawns the scoped actor");

            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            var third = Sim.NewActor("KEM_BankAssistant_0_third", e.F.Layer);
            SetField(e.Helper, "Actor", third);
            SetField(e.Helper, "Animator", third.AddComponent<Animator>());
            SetField(e.Helper, "PositionSync", third.AddComponent<PositionSync>());
            RegisterPooled(third);
            CoordinatorUpdate(e.C); // 同上：本用例只驱动 gap 生命周期
            True(GetField<GameObject>(e.Helper, "Actor") == third, "fresh actor takes over after the world round trip");
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);
            ScanTick(e.C);
            True(ActiveCollector(0), "the new trip is live");
            GapIsFresh(e.Helper, "the new trip times its own bounded gap");
            Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("a pending gap leaves native bank sync and other worlds untouched", () =>
        {
            var e = new Env();
            SetField(e.Helper, "CarriedCoins", 5);
            ActivateCollector(0);
            var pending = e.F.AddCoin(8f);

            ScanTick(e.C, 1.0f);
            True(ActiveCollector(0), "waiting with an immature coin in range");
            Eq(0, pending.ClaimCalls);
            Eq(0, pending.PolicyRpcCalls);
            Eq(100, e.B._stashedCoins);
            Eq(0, PlayerPrefs.SetIntCalls);

            PatchEconomy_Banker.FinaliseEmerge_Prefix(e.B);
            e.B._stashedCoins += 7;
            PatchEconomy_Banker.Update_Postfix(e.B);
            Eq(107, BankLive(), "native ledger sync is unaffected by a pending gap");
            Eq(107, e.B._stashedCoins);

            BiomeHolder.Inst.BiomeIndex = 1;
            CoordinatorUpdate(e.C);
            Eq(107, e.B._stashedCoins);
            Eq(0, pending.ClaimCalls);
            Eq(0, pending.PolicyRpcCalls);
            Eq(0f, WaitDeadline(e.Helper), "the gap never carries into another world");
        });
        Test("offline approach teleport plays both ends, hides, and resumes only after the reveal", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "clean FX pool");

            True((bool)Invoke("TryAssign", e.Helper, coin), "native claim acquired");
            False(renderer.enabled, "assistant hidden during the teleport window");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "departure and destination each play a stripe group");
            True(BankAssistantTeleportVisuals.IsWaiting(0), "movement waits for the reveal");

            float hiddenX = e.Actor.transform.position.x;
            Advance(0.05f);
            CoordinatorUpdate(e.C);
            Eq(hiddenX, e.Actor.transform.position.x, "position frozen while hidden");
            False(renderer.enabled, "still hidden before the deadline");
            True(BankAssistantTeleportVisuals.IsWaiting(0), "still waiting before the deadline");

            Advance(0.10f);
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "renderer restored at the deadline");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "wait released");
            True(e.Actor.transform.position.x > hiddenX, "the collector resumes running towards the coin");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes keep fading after the reveal");
            Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("online host teleports without any FX or hidden frame and keeps collecting", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0f, 0f);
            NetworkBigBoss.IsOnline = true;
            var coin = e.F.AddCoin(8f);
            int fxBefore = CoinCourierTeleportFx.ActiveCount;

            True((bool)Invoke("TryAssign", e.Helper, coin), "native claim acquired online");
            Eq(fxBefore, CoinCourierTeleportFx.ActiveCount, "zero FX online");
            True(renderer.enabled, "never hidden online");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "no delay online");
            True(Math.Abs(e.Actor.transform.position.x - 6f) <= 0.01f, "native approach jump still applied");
            ActivateCollector(0);

            for (int i = 0; i < 12 && coin.gameObject.Alive; i++)
            {
                Advance(0.31f);
                CoordinatorUpdate(e.C);
            }
            False(coin.gameObject.Alive, "coin still collected online");
            Eq(101, e.B._stashedCoins, "exactly one credit");
            True(renderer.enabled, "never hidden while collecting");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "no FX from the online collection either");
        });
        Test("a repeated teleport ends the previous presentation before starting the next", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "first jump");
            False(renderer.enabled, "hidden after the first jump");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "one stripe pair");

            Invoke("TeleportHomeAndDeposit", e.Helper);
            False(renderer.enabled, "still hidden during the new window");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "previous pair cancelled; exactly one pair remains");
            True(BankAssistantTeleportVisuals.IsWaiting(0), "waiting on the latest jump only");

            Advance(0.13f);
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "revealed after the second window");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "not permanently hidden");
        });
        Test("leasing an assistant under a pending teleport window gets a visible actor in the same call", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            SetField(e.Helper, "CarriedCoins", 5);
            Invoke("TeleportHomeAndDeposit", e.Helper);   // pass-0 回家瞬移开启一次显形等待
            False(renderer.enabled, "home jump hides during its window");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "home jump stripes alive");

            True(BankAssistantCoordinator.TryReserveForRestock(out int index, out GameObject actor), "assistant leased");
            Eq(0, index);
            True(actor == e.Actor, "the hidden actor is the lease");
            True(renderer.enabled, "the lease caller gets a visible actor in the same call");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "no hidden lease window");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "the owned stripe pair was cancelled");

            True(BankAssistantCoordinator.PlaceRestockAssistant(index, actor, new Vector3(2f, 0f, 0f), 1f), "lease placement accepted");
            True(renderer.enabled, "still visible during the lease");
            BankAssistantCoordinator.ReleaseRestockAssistant(index, actor, false);
            False(GetField<bool>(e.Helper, "RestockReserved"), "lease released");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "no residual stripes");
        });
        Test("a claim invalidated during the reveal window is released by the original path after it", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "claim acquired with an approach teleport");
            True(BankAssistantTeleportVisuals.IsWaiting(0), "hidden window active");

            int rpc = coin.PolicyRpcCalls;
            coin.friendlyClaimer = null;                 // 原生侧认领失效
            Advance(0.05f);
            CoordinatorUpdate(e.C);
            Eq(rpc, coin.PolicyRpcCalls, "no release while hidden");
            True(GetField<DroppableCurrency>(e.Helper, "Target") == coin, "receipt retained in the window");

            Advance(0.10f);
            CoordinatorUpdate(e.C);
            True(GetField<DroppableCurrency>(e.Helper, "Target") == null, "target released after the reveal");
            Restored(coin);
        });
        Test("authority loss ends the window and restores the captured enabled value", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "jump");
            False(renderer.enabled, "hidden");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes alive");

            NetworkBigBoss.HasWorldAuth = false;
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "native visibility restored on authority loss");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "no lingering wait");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "own handles cancelled");
            Eq(0, Pool.DespawnCalls, "client-side loss cannot destroy the synced actor");
        });
        Test("leaving the Greek world cancels the window and restores visibility before the pool return", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "jump");
            False(renderer.enabled, "hidden");

            BiomeHolder.Inst.BiomeIndex = 1;
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "restored in the old life before any despawn");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "wait dropped");
            Eq(1, Pool.DespawnCalls, "scoped actor despawned");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "own handles cancelled");
        });
        Test("a lease after the reveal cancels the residual stripes without touching the renderer", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            SetField(e.Helper, "CarriedCoins", 5);
            Invoke("TeleportHomeAndDeposit", e.Helper);
            Eq(2, CoinCourierTeleportFx.ActiveCount, "home jump stripes playing");
            Advance(0.13f);
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "revealed by the chokepoint");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "wait over");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes still in their residual window");

            renderer.enabled = false;   // reveal 之后原生/他人改写
            True(BankAssistantCoordinator.TryReserveForRestock(out int index, out GameObject actor), "assistant leased");
            Eq(0, index);
            False(renderer.enabled, "the lease cancels residuals but must not rewrite the renderer");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "residual pair cancelled by the lease");
        });
        Test("a world exit after the reveal cancels the residual stripes", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "jump");
            Advance(0.13f);
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "revealed");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes in the residual window");

            BiomeHolder.Inst.BiomeIndex = 1;
            CoordinatorUpdate(e.C);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "residual pair cancelled on world exit");
            Eq(1, Pool.DespawnCalls, "scoped actor despawned");
        });
        Test("an approach jump with no real displacement plays no teleport FX", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            // approach 点恰好等于当前位置（CarriedCoins==0 分支允许接近瞬移，但零位移不播）。
            e.Actor.transform.position = new Vector3(6f, 0.5f, 0.7f);
            var coin = e.F.AddCoin(8f);
            coin.transform.position = new Vector3(8f, 5.5f, 0f);   // 空中币：approach=(6,0.5,0.7)

            True((bool)Invoke("TryAssign", e.Helper, coin), "native claim acquired");
            True(renderer.enabled, "no hide without a real jump");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "no wait");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "zero Begin");
            Eq(6f, e.Actor.transform.position.x, "position unchanged");
            Eq(0.7f, e.Actor.transform.position.z, "ground Z preserved");
        });
        Test("assistant teleport FX do not depend on the courier switch", () =>
        {
            var e = new Env();
            e.Actor.AddComponent<SpriteRenderer>();
            ModConfig.CoinCourierEnabled.Value = false;
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "jump");

            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes created with the courier switch off");
            CoinCourierTeleportFx.TickForFrame(0.1f, 700001);
            Eq(2, CoinCourierTeleportFx.ActiveCount, "the central tick advances them while the switch is off");
            CoinCourierTeleportFx.TickForFrame(1f, 700002);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "they complete on the shared clock");
        });
        Test("pool recycling restores only the recycled actor's presentation", () =>
        {
            var e = new Env();
            var rendererA = e.Actor.AddComponent<SpriteRenderer>();
            var actorB = Sim.NewActor("KEM_BankAssistant_1_test", e.F.Layer);
            var rendererB = actorB.AddComponent<SpriteRenderer>();
            SetField(Assistant(1), "Actor", actorB);
            object helperB = Assistant(1);
            RegisterPooled(actorB);

            True(BankAssistantTeleportVisuals.NotifyTeleport(0, e.Actor,
                new Vector3(1f, 0f, 0f), new Vector3(9f, 0f, 0f)), "A hidden");
            True(BankAssistantTeleportVisuals.NotifyTeleport(1, actorB,
                new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f)), "B hidden");
            False(rendererA.enabled, "A hidden");
            False(rendererB.enabled, "B hidden");
            Eq(4, CoinCourierTeleportFx.ActiveCount, "both pairs alive");

            var lifecycle = e.Actor.AddComponent<BankAssistantVisualLifecycle>();
            InvokeInstance(lifecycle, "OnDisable");
            True(rendererA.enabled, "recycled actor's renderer restored");
            False(rendererB.enabled, "other actor's presentation untouched");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "only A's own handles cancelled");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "A slot cleared");
            True(BankAssistantTeleportVisuals.IsWaiting(1), "B slot retained");

            BankAssistantTeleportVisuals.EndSlot(1);
            True(rendererB.enabled, "B restored when its own window ends");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "B handles cancelled");
            True(GetField<GameObject>(helperB, "Actor") == actorB, "B owner retained");
        });
        Test("pause freezes the reveal window without dropping it", () =>
        {
            var e = new Env();
            var renderer = e.Actor.AddComponent<SpriteRenderer>();
            e.Actor.transform.position = new Vector3(20f, 0.5f, 0f);
            var coin = e.F.AddCoin(8f);
            True((bool)Invoke("TryAssign", e.Helper, coin), "jump");
            False(renderer.enabled, "hidden");

            Time.timeScale = 0f;
            CoordinatorUpdate(e.C);
            False(renderer.enabled, "still hidden while the game is paused");
            True(BankAssistantTeleportVisuals.IsWaiting(0), "the window is not dropped by pause");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes frozen, not cancelled");

            Time.timeScale = 1f;
            Advance(0.13f);
            CoordinatorUpdate(e.C);
            True(renderer.enabled, "reveals after the game resumes");
            False(BankAssistantTeleportVisuals.IsWaiting(0), "released after resume");
        });

        // ---- issue-81: 助手本体归属（目标回池、丢槽回认、歧义拒绝） ----
        Test("a target coin returning to the pool keeps the other actors, lease and receipts", () =>
        {
            var e = new PoolEnv();
            SetField(Assistant(1), "RestockReserved", true);
            SetField(Assistant(2), "CarriedCoins", 7);
            SetField(Assistant(2), "UncreditedCoins", 3);
            e.DepartTarget();

            Invoke("DropStaleWorldState", e.F.Layer);
            Eq(4, e.SameActors(), "all four actor references retained");
            True(GetField<bool>(Assistant(1), "RestockReserved"), "restock lease retained");
            Eq(7, GetField<int>(Assistant(2), "CarriedCoins"), "carried coins retained");
            Eq(3, GetField<int>(Assistant(2), "UncreditedCoins"), "uncredited responsibility retained");
            True(GetField<DroppableCurrency>(Assistant(0), "Target") == null,
                "only the departed target is released");
            Eq(0, Pool.DespawnCalls, "no actor despawn");
            Eq(100, e.B._stashedCoins, "no second credit");
            Eq(0, PlayerPrefs.SetIntCalls, "no ledger write");

            Invoke("EnsureEightActors", e.F.Layer);
            Eq(4, e.PoolActive(), "pool active members stay four");
            Eq(4, Pool.SpawnGoCalls, "no replacement spawn");
        });
        Test("a pooled target return cannot multiply the assistants on the next ensure", () =>
        {
            var e = new PoolEnv();
            e.DepartTarget();
            Invoke("DropStaleWorldState", e.F.Layer);
            Invoke("EnsureEightActors", e.F.Layer);
            Eq(4, e.SameActors(), "the four slot actors survive the target departure");
            Eq(4, e.PoolActive(), "owned pool actor count");
            Eq(4, Pool.SpawnGoCalls, "no second four");
            Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("a nondestroy reset re-adopts the hidden pool actors instead of spawning", () =>
        {
            var e = new PoolEnv();
            Invoke("ResetAll", true, false, false);
            Invoke("EnsureEightActors", e.F.Layer);
            Eq(4, e.SameActors(), "the same hidden actors are re-adopted from the owned pools");
            Eq(4, Pool.SpawnGoCalls, "no replacement spawn while the owned pool has the actors");
            Eq(4, e.PoolActive(), "pool active members stay four");
            Invoke("EnsureEightActors", e.F.Layer);
            Eq(4, e.SameActors(), "a redundant ensure neither re-adopts nor replaces");
            Eq(4, Pool.SpawnGoCalls, "the redundant ensure spawns nothing");
            Eq(4, e.PoolActive(), "at most one slot actor per owned pool");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("ownership=pool-adopted")), "adoption is diagnosed");
        });
        Test("client authority loss and regain re-adopts the four pooled actors without spawning", () =>
        {
            var e = new PoolEnv();
            SetStatic(typeof(BankAssistantCoordinator), "_hadAuthority", true);
            NetworkBigBoss.HasWorldAuth = false;
            CoordinatorUpdate(e.C);
            True(e.Original.All(a => a.Alive), "client cannot despawn the synced actors");

            NetworkBigBoss.HasWorldAuth = true;
            CoordinatorUpdate(e.C);
            Eq(4, e.SameActors(), "the same four hidden pool actors are re-adopted");
            Eq(4, Pool.SpawnGoCalls, "no replacement spawn after the migration");
            Eq(4, e.PoolActive(), "pool active members stay four");
            Eq(100, e.B._stashedCoins, "no duplicate credit");
            Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("two legal actors in the owned pool refuse adoption and spawning with a diagnostic", () =>
        {
            var e = new PoolEnv();
            var surplus = Sim.NewActor("KEM_BankAssistant_0_surplus", e.F.Layer);
            surplus.hideFlags = HideFlags.HideAndDontSave;
            surplus.AddComponent<PositionSync>().hideFlags = HideFlags.HideAndDontSave;
            Pool.ByInstance[surplus] = e.Pools[0];
            e.Pools[0]._activeCache.Add(surplus);
            SetField(Assistant(0), "Actor", null);   // 丢槽：同池两只合法同名对象

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null, "ambiguous slot stays empty");
            Eq(4, Pool.SpawnGoCalls, "no fifth actor while two candidates exist");
            Eq(5, e.PoolActive(), "existing pool members untouched, no brute cleanup");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("slot=0") && m.Contains("ownership=pool-ambiguous")), "ambiguity diagnosed");
        });
        Test("an unverifiable owned pool refuses spawning instead of guessing", () =>
        {
            var e = new PoolEnv();
            e.Pools[0].prefab = Sim.NewActor("KEM_BankAssistant_0_hijacked", e.F.Layer);
            SetField(Assistant(0), "Actor", null);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null, "unverifiable slot stays empty");
            Eq(4, Pool.SpawnGoCalls, "no spawn on an unverifiable pool");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("slot=0") && m.Contains("ownership=pool-unverified")), "refusal diagnosed");
        });
        Test("a stale old-layer pool actor is not adopted and no RPC touches it", () =>
        {
            var e = new PoolEnv();
            Invoke("ResetAll", true, false, false);
            Sim.SceneHandle = 9;
            var oldLayer = Sim.NewLayer("old");
            e.Original[0].transform.Parent = oldLayer.transform;
            e.Original[0].scene = oldLayer.scene;
            int sends = e.Original[0].GetComponent<PositionSync>().SendCalls;

            Invoke("EnsureEightActors", e.F.Layer);
            True(e.Original[0].Alive, "stale actor is left alone");
            Eq(sends, e.Original[0].GetComponent<PositionSync>().SendCalls,
                "no position RPC to the old actor");
            True(GetField<GameObject>(Assistant(0), "Actor") != e.Original[0], "stale actor is not adopted");
            True(GetField<GameObject>(Assistant(0), "Actor") != null, "a fresh current-layer actor takes the slot");
            Eq(5, Pool.SpawnGoCalls, "exactly one replacement for the stale slot");
            Eq(0, PlayerPrefs.SetIntCalls);
        });
        Test("a destroyed pool member is skipped instead of blocking adoption", () =>
        {
            var e = new PoolEnv();
            Invoke("ResetAll", true, false, false);
            var dead = Sim.NewActor("KEM_BankAssistant_0_dead", e.F.Layer);
            dead.AddComponent<PositionSync>();
            Pool.ByInstance[dead] = e.Pools[0];
            e.Pools[0]._activeCache.Add(dead);
            UnityEngine.Object.Destroy(dead);

            Invoke("EnsureEightActors", e.F.Layer);
            Eq(4, e.SameActors(), "the four live actors are still adopted past the destroyed entry");
            Eq(4, Pool.SpawnGoCalls, "the destroyed member causes no extra spawn");
        });
        Test("a null owned-pool collection blocks instead of being treated as empty", () =>
        {
            var e = new PoolEnv();
            e.Pools[0]._activeCache = null;
            SetField(Assistant(0), "Actor", null);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null, "unreadable collection blocks the slot");
            Eq(4, Pool.SpawnGoCalls, "no spawn when the pool collection is unreadable");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("slot=0") && m.Contains("ownership=pool-unreadable")), "read fault diagnosed");
        });
        Test("current-world members with broken identity block adoption and spawning", () =>
        {
            var e = new PoolEnv();
            var stray = Sim.NewActor("KEM_BankAssistant_0_stray", e.F.Layer); // 无 PositionSync
            Pool.ByInstance[stray] = e.Pools[0];
            e.Pools[0]._activeCache.Add(stray);
            SetField(Assistant(0), "Actor", null);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null,
                "a same-world member without PositionSync blocks the slot");
            Eq(4, Pool.SpawnGoCalls, "no spawn while an unverifiable member exists");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("slot=0") && m.Contains("ownership=pool-uncertain")), "uncertainty diagnosed");

            e.Pools[0]._activeCache.Remove(stray);
            var misnamed = Sim.NewActor("KEM_BankAssistant_9_wrong", e.F.Layer);
            misnamed.AddComponent<PositionSync>();
            Pool.ByInstance[misnamed] = e.Pools[0];
            e.Pools[0]._activeCache.Add(misnamed);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null, "a wrong-slot-name member blocks the slot");
            Eq(4, Pool.SpawnGoCalls, "no spawn for a misnamed member either");
            Eq(5, e.PoolActive(), "existing pool members untouched, no brute cleanup");
        });
        Test("a native read fault in the owned pool blocks instead of spawning", () =>
        {
            var e = new PoolEnv();
            e.Original[0].transform.FailChildReads = 1; // 候选成员读取抛异常
            SetField(Assistant(0), "Actor", null);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null, "read fault blocks the slot");
            Eq(4, Pool.SpawnGoCalls, "no spawn after a native read fault");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("slot=0") && m.Contains("ownership=pool-unreadable")), "read fault diagnosed");
        });
        Test("a missing registered pool blocks even when the native prefab map still owns one", () =>
        {
            var e = new PoolEnv();
            GameObject prefab = PatchEconomy_BankAssistants.GetPrefab(0);
            True(Pool.GetPoolFromPrefabAsset(prefab) != null, "native prefab map still owns a pool");
            ClearArrayStatic(typeof(PatchEconomy_BankAssistants), "Pools");
            SetField(Assistant(0), "Actor", null);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<GameObject>(Assistant(0), "Actor") == null,
                "a missing registered pool blocks the slot");
            Eq(4, Pool.SpawnGoCalls, "no spawn through a different map");
            True(KingdomEnhancedPlugin.Instance.LogSource.Infos.Any(m =>
                m.Contains("slot=0") && m.Contains("ownership=pool-missing")), "missing registration diagnosed");
        });
        Test("a failed target release keeps the receipt and stops the ensure", () =>
        {
            var e = new PoolEnv();
            var coin = e.F.AddCoin(8f);
            coin.FailPolicyRpc = 1;
            SetField(Assistant(0), "Target", coin);
            SetField(Assistant(0), "Actor", null);
            SetField(Assistant(0), "UncreditedCoins", 5);
            SetField(Assistant(0), "CarriedCoins", 2);

            Invoke("EnsureEightActors", e.F.Layer);
            True(GetField<DroppableCurrency>(Assistant(0), "Target") == coin, "target receipt retained");
            Eq(5, GetField<int>(Assistant(0), "UncreditedCoins"), "uncredited responsibility retained");
            Eq(2, GetField<int>(Assistant(0), "CarriedCoins"), "carried coins retained");
            Eq(4, Pool.SpawnGoCalls, "no replacement spawn after the failed release");
            Eq(0, Pool.DespawnCalls, "no actor despawn");
        });
        // ---- issue-89：八槽名册与同君主单轮收币 ----

        Test("one player's 1/8/64-coin streams keep a single collector slot", () =>
        {
            foreach (int size in new[] { 1, 8, 64 })
            {
                ResetStatics();
                var e = new PoolEnv();
                var monarch = e.F.AddPlayer(-2f);
                for (int i = 0; i < size; i++)
                    BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f + i * 0.1f), monarch);

                ScanTick(e.C, 0.61f);
                ScanTick(e.C, 3.1f);
                ScanTick(e.C, 2.5f);   // 离线接近显形窗口结束后完成首枚拾取

                Eq(1, ActiveCount(), size + " coins: exactly one collector");
                True(Pool.DespawnCalls >= 1, size + " coins: the owner collects");
                Eq(100 + Pool.DespawnCalls, e.B._stashedCoins,
                    size + " coins: every collected coin is credited exactly once");
                True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer,
                    size + " coins: the round is bound to the player");
                True(RoundOwnerOf(0) == monarch, size + " coins: the owner is the exact Player object");
                for (int i = 1; i < 4; i++)
                    True(GetField<DroppableCurrency>(Assistant(i), "Target") == null,
                        size + " coins: slot " + i + " stays out of the round");

                ScanTick(e.C);
                Eq(1, ActiveCount(), size + " coins: later scans never add a second helper");
                True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer,
                    size + " coins: the owner keeps its round");
            }
        });
        Test("two monarchs run one round each in parallel", () =>
        {
            var e = new PoolEnv();
            var first = e.F.AddPlayer(-2f, "P1");
            var second = e.F.AddPlayer(2f, "P2");
            for (int i = 0; i < 3; i++)
            {
                BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f + i * 0.2f), first);
                BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(8f + i * 0.2f), second);
            }

            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);

            Eq(2, ActiveCount(), "each known player gets its own round");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer
                && RoundKindOf(1) == BankAssistantCoinOriginKind.KnownPlayer, "both rounds are known");
            True(RoundOwnerOf(0) != RoundOwnerOf(1), "the two rounds belong to different Player objects");
            True((RoundOwnerOf(0) == first && RoundOwnerOf(1) == second)
                || (RoundOwnerOf(0) == second && RoundOwnerOf(1) == first),
                "each monarch owns exactly one round");
        });
        Test("known and unknown rounds stay mutually exclusive in both directions", () =>
        {
            var e = new PoolEnv();
            var monarch = e.F.AddPlayer(-2f);
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f), monarch);
            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);
            Eq(1, ActiveCount(), "the known round opened");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer, "known owner bound");

            var unknownCoin = e.F.AddCoin(9f);
            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);
            Eq(1, ActiveCount(), "an unknown coin never opens a second round beside a known one");
            Eq(0, unknownCoin.ClaimCalls, "the unknown coin stays untouched while known runs");

            ResetStatics();
            var u = new PoolEnv();
            u.F.AddCoin(6f);
            ScanTick(u.C, 0.61f);
            ScanTick(u.C, 3.1f);
            Eq(1, ActiveCount(), "the unknown round opened");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.UnknownPlayer, "unknown owner bound");

            var monarch2 = u.F.AddPlayer(-2f);
            BankAssistantCoinOrigin.MarkKnownPlayer(u.F.AddCoin(9f), monarch2);
            ScanTick(u.C, 0.61f);
            ScanTick(u.C, 3.1f);
            Eq(1, ActiveCount(), "a known coin never opens a second round beside an unknown one");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.UnknownPlayer, "the unknown owner keeps its round");
        });
        Test("a waiting player round is never leased by either restock pass", () =>
        {
            var e = new PoolEnv();
            var monarch = e.F.AddPlayer(-2f);
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f), monarch);
            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);
            ScanTick(e.C, 2.5f);   // 完成首枚拾取后进入 4.2 秒等待
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer, "player round still owned");
            True(WaitDeadline(Assistant(0)) > 0f, "the owner waits for the next coin of its trip");
            True(GetField<DroppableCurrency>(Assistant(0), "Target") == null, "the wait has no target");

            // 其余槽均为 unknown 轮（同样属于玩家轮次）：两遍都不得借走任何玩家轮次。
            for (int i = 1; i < 4; i++)
                SetField(Assistant(i), "RoundKind", BankAssistantCoinOriginKind.UnknownPlayer);
            False(BankAssistantCoordinator.TryReserveForRestock(out _, out _),
                "no player round may be borrowed, including the 4.2s wait slot");
            False(GetField<bool>(Assistant(0), "RestockReserved"), "the owner keeps its trip");
            True(WaitDeadline(Assistant(0)) > 0f, "the wait deadline survives the refused borrow");
        });
        Test("a known round never sweeps or chains onto another player's coin", () =>
        {
            var e = new PoolEnv();
            var p1 = e.F.AddPlayer(-2f, "P1");
            // 另一君主的本体不在当前 world layer：其币不可归属任何轮次（保守降级 unknown），
            // 从而能干净地验证“本槽 Known 轮绝不顺吸另一实际来源的币”。
            var p2Go = Sim.NewActor("P2", Sim.NewLayer("FarWorld").transform);
            var p2 = p2Go.AddComponent<Player>();
            p2.parentHeaderRef = new CRPCHeader { referencedGO = p2Go };
            var target = e.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(target, p1);
            var foreign = e.F.AddCoin(6.25f);   // 顺吸半径内的另一来源币
            BankAssistantCoinOrigin.MarkKnownPlayer(foreign, p2);

            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);
            ScanTick(e.C, 2.5f);   // 完成首枚拾取（顺吸窗口在移动路径上）

            Eq(0, foreign.ClaimCalls, "the foreign coin keeps its native claim state");
            Eq(0, foreign.PolicyRpcCalls, "no policy write crosses owners");
            Eq(1, Pool.DespawnCalls, "only the owner's coin is consumed");
            Eq(101, e.B._stashedCoins, "exactly one credit");
            True(RoundOwnerOf(0) == p1, "the round stays with its player");
            Eq(1, ActiveCount(), "no second round opens for the foreign coin");
        });
        Test("a finished trip releases the owner only after the next coin matures", () =>
        {
            var e = new PoolEnv();
            var monarch = e.F.AddPlayer(-2f);
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f), monarch);
            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);
            ScanTick(e.C, 2.5f);   // 完成首枚拾取并建立 4.2 秒等待
            Eq(1, ActiveCount(), "the first trip runs");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer, "known round bound");
            Eq(101, e.B._stashedCoins, "the first coin is collected");

            // 等待到期（4.2 秒自断流）且没有同君主的可续币：本趟回家并释放 owner。
            ScanTick(e.C, 4.5f);
            Eq(0, ActiveCount(), "the expired gap ends the trip");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.None, "the ended trip releases the owner");
            Eq(0f, WaitDeadline(Assistant(0)), "the deadlined gap is closed");

            // 下一枚同君主币之后才由另一个空槽接替——同君主永远只有一趟。
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(9f), monarch);
            ScanTick(e.C, 0.61f);
            Eq(0, ActiveCount(), "an immature coin opens nothing");
            ScanTick(e.C, 3.1f);
            Eq(1, ActiveCount(), "the next trip starts after the previous one ended");
            True(RoundKindOf(1) == BankAssistantCoinOriginKind.KnownPlayer, "the handoff slot takes the round");
            True(RoundOwnerOf(1) == monarch, "it is the same monarch's next round");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.None, "the finished slot never resumes the trip");
        });
        Test("farm backlog keeps parallel helpers beside a player round", () =>
        {
            var e = new PoolEnv();
            var monarch = e.F.AddPlayer(-2f);
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f), monarch);
            for (int i = 0; i < 9; i++)
                BankAssistantCoinOrigin.MarkDropFarm(e.F.AddCoin(9f + i * 0.1f));

            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);

            int farm = 0;
            int known = 0;
            for (int i = 0; i < 4; i++)
            {
                if (RoundKindOf(i) == BankAssistantCoinOriginKind.Farm) farm++;
                if (RoundKindOf(i) == BankAssistantCoinOriginKind.KnownPlayer) known++;
            }
            Eq(2, farm, "9 farm coins keep the 1 + backlog/8 parallel farm helpers");
            Eq(1, known, "the player round stays a single owner");
            Eq(3, ActiveCount(), "farm and player work never serialize onto one helper");
        });
        Test("policy RPC peek consumes nothing and only a proven header marks a known origin", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var coin = e.F.AddCoin(3f);

            // 完整有 header 包：n=21、首字节 1、原生消费 21。
            ByteBuffer.Available = 21;
            ByteBuffer.Index = 2;
            ByteBuffer.Buffer[2] = 1;
            coin.dropper = monarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var headerState);
            Eq(0, ByteBuffer.Reads, "prefix never consumes the buffer");
            Eq(2, ByteBuffer.Index, "prefix never moves the cursor");
            True(headerState.Valid && headerState.HasHeader, "complete header packet is a candidate");
            ByteBuffer.Index += 21;
            ByteBuffer.Available -= 21;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(coin, headerState);
            True(BankAssistantCoinOrigin.KindOf(coin, out Player resolved) == BankAssistantCoinOriginKind.KnownPlayer,
                "the verified packet marks the known player");
            True(resolved == monarch, "the resolved player is the dropper object");
            Eq(0, coin.Enables + coin.Disables, "the hook never touches the native lifecycle");

            // 无 header：即使原生 dropper 字段仍是旧值也不得当作来源；已有 known 降为 unknown。
            var second = e.F.AddCoin(4f);
            BankAssistantCoinOrigin.MarkKnownPlayer(second, monarch);
            ByteBuffer.Available = 15;
            ByteBuffer.Index = 0;
            ByteBuffer.Buffer[0] = 0;
            second.dropper = monarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var noHeaderState);
            True(noHeaderState.Valid && !noHeaderState.HasHeader, "no-header packet is recognized");
            ByteBuffer.Index += 15;
            ByteBuffer.Available -= 15;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(second, noHeaderState);
            True(BankAssistantCoinOrigin.KindOf(second, out _) == BankAssistantCoinOriginKind.UnknownPlayer,
                "a no-header packet only downgrades to the unknown channel");

            // 有 header 但消费位置不符（buffer 被切换）：同样只降级。
            var third = e.F.AddCoin(5f);
            BankAssistantCoinOrigin.MarkKnownPlayer(third, monarch);
            ByteBuffer.Available = 21;
            ByteBuffer.Index = 3;
            ByteBuffer.Buffer[3] = 1;
            third.dropper = monarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var mismatchState);
            ByteBuffer.Index += 20;
            ByteBuffer.Available -= 20;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(third, mismatchState);
            True(BankAssistantCoinOrigin.KindOf(third, out _) == BankAssistantCoinOriginKind.UnknownPlayer,
                "a cursor mismatch never yields a known origin");

            // 短包（有 header 但不足 21）：prefix 不给来源许可。
            ByteBuffer.Available = 20;
            ByteBuffer.Index = 0;
            ByteBuffer.Buffer[0] = 1;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var shortState);
            False(shortState.Valid, "an incomplete header packet carries no provenance");
        });
        Test("a coin lifecycle reset drops origin, claim and stale maturity", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var coin = e.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(coin, monarch);
            ScanTick(e.C, 0.61f);   // 旧观察（未成熟）建立
            int coinId = coin.gameObject.GetInstanceID();
            True(ObservedContains(coinId), "the pre-reset life was observed");
            True((bool)Invoke("TryAssign", e.Helper, coin), "claimed under the known round");
            Eq(1, coin.ClaimCalls, "native claim acquired");
            ActivateCollector(0);

            BankAssistantCoordinator.OnCoinLifecycleReset(coin);
            True(BankAssistantCoinOrigin.KindOf(coin, out _) == BankAssistantCoinOriginKind.None,
                "the life boundary clears the origin record");
            False(ObservedContains(coinId), "the stale maturity observation is dropped with the life");
            True(GetField<DroppableCurrency>(e.Helper, "Target") == null, "the claim receipt is released");
            Restored(coin);
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer,
                "a single-coin release keeps the round owner for the trip");

            // 新 life 从零成熟：3 秒窗口内不会被重新认领，空手收工后才释放 owner。
            ScanTick(e.C, 3.1f);
            True(GetField<DroppableCurrency>(e.Helper, "Target") == null,
                "the reused coin matures from scratch instead of inheriting the old clock");
            Eq(1, coin.ClaimCalls, "no re-claim inside the fresh maturity window");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.None, "an empty-handed trip releases the owner");
        });
        Test("eight-slot wiring: templates, pool ids and the fx cap follow the roster", () =>
        {
            var e = new PoolEnv(atlasReady: true);
            Eq(8, Pool.SpawnGoCalls, "all eight templates spawn");
            Eq(8, e.PoolActive(), "eight owned pool members");
            var ids = new System.Collections.Generic.HashSet<short>();
            for (int i = 0; i < 8; i++)
                True(ids.Add(PatchEconomy_BankAssistants.GetPoolSyncId(i)), "pool id " + i + " is unique");
            Eq(8, ids.Count, "30120..30127 are all reserved");
            True(PatchEconomy_BankAssistants.GetPrefab(4) != null, "the fifth template is registered");
            True(PatchEconomy_BankAssistants.GetPrefab(7) != null, "the eighth template is registered");
            float[] scales = (float[])GetStatic(typeof(PatchEconomy_BankAssistants), "GreekVisualScaleY");
            Eq(1.0f, scales[4], "new slots keep the frozen 1.0 visual scale");
            Eq(1.0f, scales[7], "new slots keep the frozen 1.0 visual scale");
            Eq(17, CoinCourierTeleportFx.MaxConcurrent, "8 assistants x 2 ends + 1 goblin");
        });
        Test("fixed assistant heights ignore the source banker scale and survive a full rebuild", () =>
        {
            float[] sources = { 1f, 1.075f, 1.7f };
            var perSource = new float[sources.Length][];
            for (int s = 0; s < sources.Length; s++)
            {
                ResetStatics(); // 每个源高度独立 world：spawn 计数与静态槽目标都从零开始
                var e = new PoolEnv(atlasReady: true, sourceBankerY: sources[s]);
                Eq(sources[s], e.B.transform.localScale.y, "the live banker keeps its own height");
                Eq(1, e.B.transform.Writes, "pool setup does not write the live banker's transform");
                Eq(sources[s], PatchEconomy_BankAssistants.GetPrefab(0).transform.localScale.y,
                    "the template keeps the neutral native scale instead of baking our target");
                float[] scales = (float[])GetStatic(typeof(PatchEconomy_BankAssistants), "GreekVisualScaleY");
                Eq(0.70f * 32f / 24f, scales[0], "slot 0 targets 0.70 stand height from its 24px art");
                Eq(1.0f, scales[1], "slot 1 keeps the frozen 1.0");
                Eq(0.70f * 32f / 19f, scales[2], "slot 2 targets 0.70 stand height from its 19px art");
                Eq(1.2f, scales[3], "slot 3 keeps the frozen 1.2");
                for (int i = 4; i < 8; i++) Eq(1.0f, scales[i], "new slot " + i + " keeps the frozen 1.0");
                perSource[s] = (float[])scales.Clone();
            }
            True(perSource[0].SequenceEqual(perSource[1]) && perSource[1].SequenceEqual(perSource[2]),
                "three source heights produce identical slot targets");

            // 完整清空静态缓存后再建一次：八槽重新从零取值，不累缩、不漂移。
            ResetStatics();
            var rebuilt = new PoolEnv(atlasReady: true, sourceBankerY: 2.4f);
            Eq(2.4f, rebuilt.B.transform.localScale.y, "the rebuilt banker keeps its own height");
            float[] after = (float[])GetStatic(typeof(PatchEconomy_BankAssistants), "GreekVisualScaleY");
            True(perSource[0].SequenceEqual(after), "a full rebuild reproduces the same fixed targets without drift");
            Eq(8, Pool.SpawnGoCalls, "the rebuild spawns exactly its own eight actors");
        });
        Test("the atlas bridge follows pool ownership on both peers", () =>
        {
            var e = new PoolEnv(atlasReady: true);
            var hostActor = e.Original[4];
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(hostActor);
            Eq(1, BankAssistantAtlasVisuals.TickCalls, "the host slot is driven");
            Eq(4, BankAssistantAtlasVisuals.LastSlot, "slot index is passed through");
            True(BankAssistantAtlasVisuals.LastAllowLeisure, "an idle host slot allows leisure");

            SetStaticField(typeof(BankAssistantCoordinator), "ActiveCollector",
                false, false, false, false, true, false, false, false);
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(hostActor);
            False(BankAssistantAtlasVisuals.LastAllowLeisure, "a collecting slot never shows leisure");
            SetStaticField(typeof(BankAssistantCoordinator), "ActiveCollector",
                false, false, false, false, false, false, false, false);

            // 客户端：网络池 spawn 没有 authority 槽回填，视觉仍须换新皮；allowLeisure 恒 false。
            ResetStatics();
            var c = new PoolEnv(atlasReady: true);
            NetworkBigBoss.HasWorldAuth = false;
            for (int i = 0; i < 8; i++) SetField(Assistant(i), "Actor", null);
            var clientActor = c.Original[4];
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(clientActor);
            Eq(1, BankAssistantAtlasVisuals.TickCalls, "a client pool member drives the atlas");
            Eq(4, BankAssistantAtlasVisuals.LastSlot, "client slot index passed through");
            False(BankAssistantAtlasVisuals.LastAllowLeisure, "clients never claim leisure");

            int ticks = BankAssistantAtlasVisuals.TickCalls;
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(
                Sim.NewActor("KEM_BankAssistant_4_fake", c.F.Layer));
            Eq(ticks, BankAssistantAtlasVisuals.TickCalls, "a fake slot name is not driven");
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(PatchEconomy_BankAssistants.GetPrefab(4));
            Eq(ticks, BankAssistantAtlasVisuals.TickCalls, "the template itself is not driven");
            var stale = Sim.NewActor("KEM_BankAssistant_4_stale", c.F.Layer);
            c.Pools[4]._activeCache.Add(stale);
            stale.transform.Parent = Sim.NewLayer("FarWorld").transform;
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(stale);
            Eq(ticks, BankAssistantAtlasVisuals.TickCalls,
                "an actor outside the current layer is not driven");
            var wrongPool = Sim.NewActor("KEM_BankAssistant_4_wrongpool", c.F.Layer);
            c.Pools[5]._activeCache.Add(wrongPool);
            PatchEconomy_BankAssistants.TickAssistantAtlasVisuals(wrongPool);
            Eq(ticks, BankAssistantAtlasVisuals.TickCalls,
                "a member of another slot's pool is not driven");
        });
        Test("a real Drop7 restarts maturity while a repeated policy packet does not", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var dropped = e.F.AddCoin(6f);
            Droppable_CoinOrigin_Mark_Patch.Postfix(dropped, monarch.gameObject);
            ScanTick(e.C, 0.61f);
            int id = dropped.gameObject.GetInstanceID();
            True(ObservedContains(id), "the drop life is observed");

            Advance(2.0f);
            Droppable_CoinOrigin_Mark_Patch.Postfix(dropped, monarch.gameObject);   // 同 coin 同 Player 再投掷
            False(ObservedContains(id), "a real Drop7 resets the maturity observation");
            ScanTick(e.C, 1.2f);
            True(GetField<DroppableCurrency>(e.Helper, "Target") == null,
                "the re-dropped coin is not mature from the old clock");
            Eq(100, e.B._stashedCoins, "nothing is collected from the stale clock");
            ScanTick(e.C, 3.1f);
            Eq(101, e.B._stashedCoins, "the re-dropped coin matures from its own clock");

            ResetStatics();
            var p = new Env();
            var pMonarch = p.F.AddPlayer(-2f);
            var repeated = p.F.AddCoin(6f);
            Droppable_CoinOrigin_Mark_Patch.Postfix(repeated, pMonarch.gameObject);
            ScanTick(p.C, 0.61f);
            int repeatedId = repeated.gameObject.GetInstanceID();
            True(ObservedContains(repeatedId), "the policy life is observed");
            Advance(2.0f);
            ByteBuffer.Available = 21;
            ByteBuffer.Index = 0;
            ByteBuffer.Buffer[0] = 1;
            repeated.dropper = pMonarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var policyState);
            ByteBuffer.Index += 21;
            ByteBuffer.Available -= 21;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(repeated, policyState);
            True(ObservedContains(repeatedId), "a repeated policy packet keeps the maturity clock");
            ScanTick(p.C, 1.2f);
            Eq(101, p.B._stashedCoins, "the policy life still matures on its original clock");
        });
        Test("a valid-header policy packet outside Greece never creates a source entry", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var coin = e.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(coin, monarch);
            Eq(1, OriginSnapshot().Count, "the Greek-scope record exists");

            // 非希腊当前世界（layer/scene 与希腊相同，只有 biome 不同）：完整 header 包
            // 仍不得登记——清掉已有自有记录且不建新 Entry。
            BiomeHolder.Inst.BiomeIndex = 1;
            ByteBuffer.Available = 21;
            ByteBuffer.Index = 0;
            ByteBuffer.Buffer[0] = 1;
            coin.dropper = monarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var foreignState);
            True(foreignState.Valid && foreignState.HasHeader, "the packet itself is complete");
            ByteBuffer.Index += 21;
            ByteBuffer.Available -= 21;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(coin, foreignState);
            Eq(0, OriginSnapshot().Count,
                "outside Greece the packet registers nothing and drops its own old record");
            True(BankAssistantCoinOrigin.KindOf(coin, out _) == BankAssistantCoinOriginKind.None,
                "no known owner leaks from the foreign packet");

            var fresh = e.F.AddCoin(9f);
            ByteBuffer.Available = 21;
            ByteBuffer.Index = 0;
            ByteBuffer.Buffer[0] = 1;
            fresh.dropper = monarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var freshState);
            ByteBuffer.Index += 21;
            ByteBuffer.Available -= 21;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(fresh, freshState);
            Eq(0, OriginSnapshot().Count, "a fresh foreign coin never gets an entry");

            // 回希腊：同一完整包仍按原路径登记具体君主。
            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            ByteBuffer.Available = 21;
            ByteBuffer.Index = 0;
            ByteBuffer.Buffer[0] = 1;
            coin.dropper = monarch.gameObject;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Prefix(out var greekState);
            ByteBuffer.Index += 21;
            ByteBuffer.Available -= 21;
            Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Postfix(coin, greekState);
            True(BankAssistantCoinOrigin.KindOf(coin, out Player resolved) == BankAssistantCoinOriginKind.KnownPlayer,
                "the Greek world still registers the verified header");
            True(resolved == monarch, "the Greek owner is the exact player");
        });
        Test("an exception inside ReceivePolicyRPC still downgrades the own source", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var coin = e.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(coin, monarch);
            var failure = new InvalidOperationException("native rpc failure");
            Exception returned = Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Finalizer(coin, failure);
            True(ReferenceEquals(failure, returned), "the original exception is preserved");
            True(BankAssistantCoinOrigin.KindOf(coin, out _) == BankAssistantCoinOriginKind.UnknownPlayer,
                "the own source is downgraded on the exception path");
            True(Droppable_ReceivePolicyRPC_CoinOrigin_Patch.Finalizer(coin, null) == null,
                "a successful call is untouched");
        });
        Test("ApplyData clears the previous source and maturity without touching receipts", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var coin = e.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(coin, monarch);
            True((bool)Invoke("TryAssign", e.Helper, coin), "claimed before the load restore");
            ActivateCollector(0);
            Advance(0.6f);
            ScanTick(e.C, 0.01f);   // 观察成立但位移极小：本轮不拾取
            int id = coin.gameObject.GetInstanceID();
            True(ObservedContains(id), "observed before ApplyData");

            coin.Persistent_IBehaviour_ApplyData(null);   // 原生入口（stub 形状）
            Eq(1, coin.ApplyDataCalls, "the native ApplyData entry ran");
            Droppable_ApplyData_CoinOrigin_Patch.Postfix(coin);
            True(BankAssistantCoinOrigin.KindOf(coin, out _) == BankAssistantCoinOriginKind.None,
                "the restored coin keeps no previous owner");
            False(ObservedContains(id), "the restored coin keeps no previous maturity");
            Eq(1, ClaimCount(), "the economic receipt is untouched");
        });
        Test("an unclaimable oldest task never blocks the newer round", () =>
        {
            var e = new PoolEnv();
            var monarch = e.F.AddPlayer(-2f);
            var stale = e.F.AddCoin(6f);                     // 未知来源、被原生侧占用
            stale.friendlyClaimer = Sim.NewActor("nativeClaimer", e.F.Layer);
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(9f), monarch);

            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);

            Eq(1, ActiveCount(), "the newer known round still starts");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer, "the known owner is bound");
            Eq(0, stale.ClaimCalls, "the externally claimed coin is never claimed");
            True(ObservedContains(stale.gameObject.GetInstanceID()),
                "the blocked coin keeps its maturity clock");

            ResetStatics();
            var r = new PoolEnv();
            var monarch2 = r.F.AddPlayer(-2f);
            var knownStale = r.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(knownStale, monarch2);
            knownStale.friendlyClaimer = Sim.NewActor("nativeClaimer", r.F.Layer);
            r.F.AddCoin(9f);                                  // 未知来源
            ScanTick(r.C, 0.61f);
            ScanTick(r.C, 3.1f);
            Eq(1, ActiveCount(), "the unknown round still starts");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.UnknownPlayer, "the unknown owner is bound");
            Eq(0, knownStale.ClaimCalls, "the externally claimed known coin is never claimed");
        });
        Test("the unknown round is dispatched after the known round finishes", () =>
        {
            var e = new PoolEnv();
            var monarch = e.F.AddPlayer(-2f);
            BankAssistantCoinOrigin.MarkKnownPlayer(e.F.AddCoin(6f), monarch);
            ScanTick(e.C, 0.61f);
            ScanTick(e.C, 3.1f);
            ScanTick(e.C, 2.5f);                              // 首枚拾取 → 4.2 秒等待
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer, "the known trip runs");

            e.F.AddCoin(9f);                                  // 更年轻的未知币
            ScanTick(e.C, 0.61f);
            Eq(1, ActiveCount(), "the unknown waits while a known round runs");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.KnownPlayer, "the known owner keeps the slot");

            ScanTick(e.C, 4.5f);                              // 等待到期 → known 收工 → 同帧派 unknown
            Eq(1, ActiveCount(), "the unknown round takes over");
            True(RoundKindOf(0) == BankAssistantCoinOriginKind.None, "the finished known slot is released");
            True(RoundKindOf(1) == BankAssistantCoinOriginKind.UnknownPlayer, "another slot owns the unknown round");
        });
        Test("foreign worlds never grow the origin table", () =>
        {
            var e = new Env();
            BiomeHolder.Inst.BiomeIndex = 1;                  // 非希腊
            for (int i = 0; i < 16; i++)
            {
                var coin = e.F.AddCoin(6f + i * 0.1f);
                Droppable_OnEnable_CoinOrigin_Patch.Postfix(coin);
                Droppable_OnDisable_CoinOrigin_Patch.Prefix(coin);
            }
            Eq(0, OriginSnapshot().Count, "no tombstone entries are created outside Greece");

            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            var greekCoin = e.F.AddCoin(9f);
            Droppable_OnEnable_CoinOrigin_Patch.Postfix(greekCoin);
            Eq(1, OriginSnapshot().Count, "the current Greek world still tracks generations");
        });
        Test("a failed release keeps the receipt and the cleanup responsibility", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);
            var coin = e.F.AddCoin(6f);
            BankAssistantCoinOrigin.MarkKnownPlayer(coin, monarch);
            True((bool)Invoke("TryAssign", e.Helper, coin), "claimed");
            coin.FailPolicyRpc = 1;

            BankAssistantCoordinator.OnCoinLifecycleReset(coin);
            Eq(1, ClaimCount(), "the claim receipt survives the failed release");
            True((bool)GetStatic(typeof(BankAssistantCoordinator), "_cleanupPending"),
                "cleanup responsibility retained");
            True(BankAssistantCoinOrigin.KindOf(coin, out _) == BankAssistantCoinOriginKind.None,
                "the source is still cleared so the next life never inherits it");
        });
        Test("drop-argument provenance: only the exact player or farmland marks a source", () =>
        {
            var e = new Env();
            var monarch = e.F.AddPlayer(-2f);

            var walletCoin = e.F.AddCoin(2f);
            Droppable_CoinOrigin_Mark_Patch.Postfix(walletCoin, monarch.gameObject);
            True(BankAssistantCoinOrigin.KindOf(walletCoin, out Player resolved) == BankAssistantCoinOriginKind.KnownPlayer,
                "the exact dropper object becomes the known owner");
            True(resolved == monarch, "identity is the dropper object, not a nearby player");

            var farmGo = Sim.NewActor("Farmland", e.F.Layer);
            farmGo.AddComponent<Farmland>();
            var farmCoin = e.F.AddCoin(3f, DropType.Wildlife);
            Droppable_CoinOrigin_Mark_Patch.Postfix(farmCoin, farmGo);
            True(BankAssistantCoinOrigin.KindOf(farmCoin, out _) == BankAssistantCoinOriginKind.Farm,
                "the farmland dropper keeps the exact-farm lane");

            var unitCoin = e.F.AddCoin(4f);
            Droppable_CoinOrigin_Mark_Patch.Postfix(unitCoin, Sim.NewActor("unit", e.F.Layer));
            True(BankAssistantCoinOrigin.KindOf(unitCoin, out _) == BankAssistantCoinOriginKind.None,
                "a foreign dropper clears the record instead of guessing");

            // 两参 Drop 与 OnEnable 换代入口都清来源（池复用不得继承旧身份）。
            True(BankAssistantCoinOrigin.KindOf(walletCoin, out _) == BankAssistantCoinOriginKind.KnownPlayer,
                "record present before the lifecycle entry");
            Droppable_OnEnable_CoinOrigin_Patch.Postfix(walletCoin);
            True(BankAssistantCoinOrigin.KindOf(walletCoin, out _) == BankAssistantCoinOriginKind.None,
                "OnEnable clears the previous life's source");
            Droppable_CoinOrigin_Mark_Patch.Postfix(walletCoin, monarch.gameObject);
            Droppable_CoinOrigin_Clear_Patch.Postfix(walletCoin);
            True(BankAssistantCoinOrigin.KindOf(walletCoin, out _) == BankAssistantCoinOriginKind.None,
                "the two-argument Drop clears the source");
        });
        Test("non-finite coins are rejected by scan and commit; domain edges belong to assistants", () =>
        {
            var e = new Env();
            var nan = e.F.AddCoin(8f);
            nan.transform.position = new Vector3(float.NaN, 0f, 0f);
            False((bool)Invoke("IsTrackableCoin", nan, -5f, 5f, false), "NaN rejected by scan");
            False((bool)Invoke("CanCommitPickup", e.Helper, nan), "NaN rejected by commit");
            ScanTick(e.C, 1.0f);
            False(ObservedContains(nan.gameObject.GetInstanceID()), "no observation for a NaN coin");
            Eq(0, nan.ClaimCalls, "NaN coin never claimed");

            var edge = e.F.AddCoin(5f);   // 等于固定域边界：归助手
            True((bool)Invoke("IsTrackableCoin", edge, -5f, 5f, false),
                "domain edge is assistant-owned");
            var inside = e.F.AddCoin(3f); // 域内：归银行家，助手不碰
            False((bool)Invoke("IsTrackableCoin", inside, -5f, 5f, false),
                "inside domain stays banker-owned");
        });
        return Finish();
    }
}
