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

    // ---- trip-target / gap-wait observables ----
    static bool ActiveCollector(int index)
        => ((bool[])GetStatic(typeof(BankAssistantCoordinator), "ActiveCollector"))[index];

    static void ActivateCollector(int index)
        => SetStaticField(typeof(BankAssistantCoordinator), "ActiveCollector", true);

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
            Eq(101, PlayerPrefs.Ints[SharedKey]);
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
            second.AddComponent<PositionSync>();
            RegisterPooled(second);
            CoordinatorUpdate(e.C); // adopt the fresh actor; the next scan is not due yet
            True(GetField<GameObject>(e.Helper, "Actor") == second, "fresh actor adopted after the disable");
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
            third.AddComponent<PositionSync>();
            RegisterPooled(third);
            CoordinatorUpdate(e.C); // adopt the fresh actor; the next scan is not due yet
            True(GetField<GameObject>(e.Helper, "Actor") == third, "fresh actor adopted after the world round trip");
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
            Eq(107, PlayerPrefs.Ints[SharedKey], "native ledger sync is unaffected by a pending gap");
            Eq(107, e.B._stashedCoins);

            BiomeHolder.Inst.BiomeIndex = 1;
            CoordinatorUpdate(e.C);
            Eq(107, e.B._stashedCoins);
            Eq(0, pending.ClaimCalls);
            Eq(0, pending.PolicyRpcCalls);
            Eq(0f, WaitDeadline(e.Helper), "the gap never carries into another world");
        });
        return Finish();
    }
}
