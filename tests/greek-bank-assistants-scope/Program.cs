using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using static Harness;

static class Program
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static object Invoke(string name, params object[] args) => typeof(BankAssistantCoordinator)
        .GetMethod(name, PrivateStatic).Invoke(null, args);

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
            B._stashedCoins = 100;
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
        Test("foreign destroy without an intervening update cannot contaminate ledger", () =>
        {
            var e = new Env(); PatchEconomy_Banker.FinaliseEmerge_Prefix(e.B);
            BiomeHolder.Inst.BiomeIndex = 1; e.B._stashedCoins = 17;
            PatchEconomy_Banker.OnDestroy_Prefix(e.B);
            Eq(100, PlayerPrefs.Ints[SharedKey]); Eq(1, PlayerPrefs.SetIntCalls);
        });
        Test("disabled banker's reentry primes before native work", () =>
        {
            var e = new Env(); PatchEconomy_Banker.FinaliseEmerge_Prefix(e.B);
            BiomeHolder.Inst.BiomeIndex = 1; e.B.enabled = false;
            PatchEconomy_Banker.TickOwnedProfiles(); e.B._stashedCoins = 17;
            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            PatchEconomy_Banker.Update_Prefix(e.B); Eq(100, e.B._stashedCoins);
            e.B._stashedCoins += 5; PatchEconomy_Banker.Update_Postfix(e.B);
            Eq(105, PlayerPrefs.Ints[SharedKey]);
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
        return Finish();
    }
}
