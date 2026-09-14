// Greek bank-scope regression: drives the UNMODIFIED production bank sources
// (GreekBankScope.cs, GreekScaleScope.cs, PatchEconomy_Banker.cs,
// PatchEconomy_AutoRestock.cs) against the fake game surface in Stubs.cs.
using System;
using KingdomEnhancedMod;
using UnityEngine;
using Scope = KingdomEnhancedMod.GreekBankScope;

static class Program
{
    private static int Main()
    {
        // ---------------------------------------------------------------- scope
        Harness.Test("greek scope active, disabled scope inactive", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Harness.True(Scope.IsActive, "greek active");
            Harness.True(Scope.IsModEnabled, "mod enabled");
            ModConfig.Enabled.Value = false;
            Harness.False(Scope.IsActive, "disabled is not active");
            Harness.False(Scope.IsModEnabled, "disabled mod");
            ModConfig.Enabled = null;
            Harness.False(Scope.IsActive, "null config is not active");
            Harness.Eq((int)Scope.Current(), (int)Scope.Scope.Unknown, "null config unknown");
            Harness.False(Scope.IsCurrentBanker(banker), "null config has no current banker");
        });

        Harness.Test("foreign biome inactive, unreadable biome unknown", () =>
        {
            Fixture f = Fixture.BuildForeign();
            Harness.False(Scope.IsActive, "foreign inactive");
            BiomeHolder.Inst = null;
            Harness.Eq((int)Scope.Current(), (int)Scope.Scope.Unknown, "no holder unknown");
            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = -1 };
            Harness.Eq((int)Scope.Current(), (int)Scope.Scope.Unknown, "negative index unknown");
            BiomeHolder.Inst = new BiomeHolder { FailRead = true };
            Harness.Eq((int)Scope.Current(), (int)Scope.Scope.Unknown, "read fault unknown");
            Harness.False(Scope.IsActive, "fault never activates");
        });

        Harness.Test("local identity vs authority identity", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Harness.True(Scope.IsCurrentBanker(banker), "local identity");
            Harness.True(Scope.IsAuthorityBanker(banker), "authority identity");
            NetworkBigBoss.HasWorldAuth = false;
            Harness.True(Scope.IsActive, "visual scope ignores auth");
            Harness.True(Scope.IsCurrentBanker(banker), "client keeps local identity");
            Harness.False(Scope.IsAuthorityBanker(banker), "no auth => no economic write");
            NetworkBigBoss.HasWorldAuth = true;

            // 另一个仍在当前层的本体持有 kingdom.banker => 不是当前本体
            Banker other = f.AddBanker("BankerOther", kingdomBound: false);
            f.Kingdom.banker = other;
            Harness.False(Scope.IsCurrentBanker(banker), "another live banker owns the role");

            // 旧层残留引用不阻止当前层本体工作（换岛重叠帧）
            Fixture old = Fixture.BuildGreek(sceneHandle: 90);
            other.transform.Parent = old.Layer;
            Harness.False(Scope.IsCurrentBanker(banker), "stale reference is not current banker proof");
        });

        Harness.Test("stale layer banker is not the current banker", () =>
        {
            Fixture old = Fixture.BuildGreek(sceneHandle: 1);
            Banker stale = old.AddBanker();
            Fixture current = Fixture.BuildGreek(sceneHandle: 2);
            Harness.False(Scope.IsCurrentBanker(stale), "old layer rejected");
            Harness.False(Scope.IsAuthorityBanker(stale), "old layer has no authority");
            Banker fresh = current.AddBanker();
            Harness.True(Scope.IsCurrentBanker(fresh), "new world banker accepted");
        });

        // ------------------------------------------------------- behavior hooks
        Harness.Test("hide and emerge override only for the current greek banker", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            bool result = true;
            Harness.False(PatchEconomy_Banker.ShouldHide_Prefix(banker, ref result), "greek override");
            Harness.False(result, "never hides while working");
            result = true;
            Harness.False(PatchEconomy_Banker.ShouldEmerge_Prefix(banker, ref result), "greek emerge");
            Harness.True(result, "safe kingdom emerges");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker native = foreign.AddBanker();
            result = true;
            Harness.True(PatchEconomy_Banker.ShouldHide_Prefix(native, ref result), "foreign native runs");
            Harness.True(result, "foreign result untouched");
            Harness.True(PatchEconomy_Banker.ShouldEmerge_Prefix(native, ref result), "foreign native runs 2");
        });

        Harness.Test("claim coins fail closed only in greek with unresolvable domain", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            f.BreakDomain();
            banker._targetCoin = f.AddCoin(3f);
            Harness.False(PatchEconomy_Banker.ClaimCoins_Prefix(banker), "fail closed");
            Harness.True(banker._targetCoin == null, "target coin cleared");

            f = Fixture.BuildGreek(sceneHandle: 3);
            banker = f.AddBanker();
            Harness.True(PatchEconomy_Banker.ClaimCoins_Prefix(banker), "domain resolved => native");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 4);
            Harness.True(PatchEconomy_Banker.ClaimCoins_Prefix(foreign.AddBanker()), "foreign passthrough");
        });

        Harness.Test("outside wall claim patch passes through in foreign", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Droppable currency = f.AddCoin(9f);
            bool result = true;
            Harness.False(
                Droppable_MainBankerOutsideWallClaim_Patch.Prefix(currency, banker.gameObject, ref result),
                "greek outside-wall claim denied");
            Harness.False(result, "claim result false");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker native = foreign.AddBanker();
            Droppable foreignCoin = foreign.AddCoin(9f);
            result = true;
            Harness.True(
                Droppable_MainBankerOutsideWallClaim_Patch.Prefix(foreignCoin, native.gameObject, ref result),
                "foreign native claim allowed");
            Harness.True(result, "foreign result untouched");
        });

        // -------------------------------------------------------------- ledger
        Harness.Test("greek primes once and native daily interest staged once", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            Harness.Eq(100, banker._stashedCoins, "primed stash");
            Harness.Eq(100, PlayerPrefs.Ints[Harness.SharedKey], "seeded key");
            Harness.Eq(1, PlayerPrefs.SetIntCalls, "seed once");
            Harness.Eq(1, PlayerPrefs.SaveCalls, "seed flush once");

            banker.InterestPerDay = 5;
            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            banker.HandleOnDayStart(); // native interest, exactly once
            PatchEconomy_Banker.HandleOnDayStart_Postfix(banker);
            Harness.Eq(105, banker._stashedCoins, "interest once");
            Harness.Eq(105, PlayerPrefs.Ints[Harness.SharedKey], "staged after interest");
            Harness.Eq(2, PlayerPrefs.SetIntCalls, "one staged write");
            Harness.Eq(2, PlayerPrefs.SaveCalls, "forced flush");

            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            banker.HandleOnDayStart();
            PatchEconomy_Banker.HandleOnDayStart_Postfix(banker);
            Harness.Eq(110, banker._stashedCoins, "second day interest");
            Harness.Eq(3, PlayerPrefs.SetIntCalls, "no double accounting");
        });

        Harness.Test("foreign banker never reads or writes the shared prefs", () =>
        {
            Fixture f = Fixture.BuildForeign();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 77;
            PlayerPrefs.Ints[Harness.SharedKey] = 5709;
            PlayerPrefs.ResetAll();
            PlayerPrefs.Ints[Harness.SharedKey] = 5709;

            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            banker.HandleOnDayStart();
            PatchEconomy_Banker.HandleOnDayStart_Postfix(banker);
            PatchEconomy_Banker.Update_Postfix(banker);

            Harness.Eq(77, banker._stashedCoins, "foreign native stash untouched");
            Harness.Eq(5709, PlayerPrefs.Ints[Harness.SharedKey], "greek shared value untouched");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no shared write");
            Harness.Eq(0, PlayerPrefs.GetIntCalls, "no shared read");
            Harness.Eq(0, PlayerPrefs.SaveCalls, "no flush outside greek");
        });

        Harness.Test("foreign banker never overwrites greek shared balance", () =>
        {
            Fixture greek = Fixture.BuildGreek(sceneHandle: 1);
            Banker greekBanker = greek.AddBanker();
            greekBanker._stashedCoins = 400;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(greekBanker);
            Harness.Eq(400, PlayerPrefs.Ints[Harness.SharedKey], "greek seeded");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            foreignBanker._stashedCoins = 12;
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(foreignBanker, 5), "deposit refused");
            PatchEconomy_Banker.Update_Postfix(foreignBanker);
            Harness.Eq(12, foreignBanker._stashedCoins, "foreign stash untouched");
            Harness.Eq(400, PlayerPrefs.Ints[Harness.SharedKey], "greek ledger intact");
        });

        Harness.Test("scope exit suspends prime proof and reentry reprimes", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            Harness.Eq(100, PlayerPrefs.Ints[Harness.SharedKey], "seeded");

            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 1 };
            PatchEconomy_Banker.Update_Postfix(banker); // 已知离开希腊：吊销 prime 凭证
            banker._stashedCoins = 88;                  // 其他世界的原生变动

            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = BiomeHolder.GreeceBiomeIndex };
            Harness.Eq(1, PatchEconomy_Banker.DepositFromAssistant(banker, 1), "reprimes then deposits");
            Harness.Eq(101, banker._stashedCoins, "foreign drift discarded by reprime");
            Harness.Eq(101, PlayerPrefs.Ints[Harness.SharedKey], "shared ledger follows greek");
        });

        Harness.Test("world context change forces a reprime", () =>
        {
            Fixture greek = Fixture.BuildGreek(sceneHandle: 1);
            Banker banker = greek.AddBanker();
            banker._stashedCoins = 100;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);

            // 同一个 banker 被搬进新 world（Persistent 恢复路径）：旧 prime 凭证作废
            Fixture next = Fixture.BuildGreek(sceneHandle: 2);
            banker.transform.Parent = next.Layer;
            banker.gameObject.scene = next.Layer.gameObject.scene;
            next.Kingdom.banker = banker;
            banker._stashedCoins = 55;
            PatchEconomy_Banker.Update_Postfix(banker);
            Harness.Eq(100, PlayerPrefs.Ints[Harness.SharedKey], "foreign drift never saved");
            Harness.Eq(1, PlayerPrefs.SetIntCalls, "only the seed write");

            Harness.Eq(1, PatchEconomy_Banker.DepositFromAssistant(banker, 1), "reprimed in new world");
            Harness.Eq(101, banker._stashedCoins, "shared value adopted again");
        });

        Harness.Test("instance id reuse cannot inherit the prime proof", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker first = f.AddBanker();
            first._stashedCoins = 100;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(first);
            int reusedId = first.gameObject.Id;

            UnityEngine.Object.Destroy(first.gameObject);
            Banker second = f.AddBanker("BankerReused", kingdomBound: true);
            second.gameObject.Id = reusedId;
            second._stashedCoins = 7;
            PatchEconomy_Banker.Update_Postfix(second);
            Harness.Eq(100, PlayerPrefs.Ints[Harness.SharedKey], "reused id did not write");
            Harness.Eq(1, PatchEconomy_Banker.DepositFromAssistant(second, 1), "reused id reprimes");
            Harness.Eq(101, second._stashedCoins, "shared value adopted");
        });

        Harness.Test("unknown scope never primes", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BiomeHolder.Inst = null;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            Harness.Eq(0, PlayerPrefs.GetIntCalls, "no shared read while unknown");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no shared write while unknown");
            BiomeHolder.Inst = new BiomeHolder { FailRead = true };
            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "faulting scope never primes");
        });

        Harness.Test("deposit commits in greek and is refused elsewhere", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Harness.Eq(7, PatchEconomy_Banker.DepositFromAssistant(banker, 7), "greek deposit");
            Harness.Eq(7, banker._stashedCoins, "stash credited");
            Harness.Eq(7, PlayerPrefs.Ints[Harness.SharedKey], "ledger staged");
            Harness.Eq(1, f.Kingdom.castle.StashCalls.Count, "castle refreshed");
            Harness.Eq(7, f.Kingdom.castle.StashCalls[0], "castle value");
            Harness.Eq(7, f.Stats.StatCalls[Stat.CoinsInBank], "stats value");
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(banker, 0), "zero refused");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            PlayerPrefs.ResetAll();
            PlayerPrefs.Ints[Harness.SharedKey] = 7;
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(foreignBanker, 3), "foreign refused");
            Harness.Eq(0, foreignBanker._stashedCoins, "no foreign credit");
            Harness.Eq(0, foreign.Kingdom.castle.StashCalls.Count, "no foreign castle write");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no foreign staging");

            Fixture greek = Fixture.BuildGreek(sceneHandle: 3);
            Banker clientBanker = greek.AddBanker();
            NetworkBigBoss.HasWorldAuth = false;
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(clientBanker, 3), "client refused");
            Harness.Eq(0, clientBanker._stashedCoins, "no client credit");
        });

        Harness.Test("restock debit only in current greek authority", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = banker;
            Harness.True(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 20), "greek debit");
            Harness.Eq(80, banker._stashedCoins, "debited once");
            Harness.Eq(80, f.Kingdom.castle.StashCalls[0], "castle follows");
            Harness.False(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 200), "insufficient funds");
            Harness.Eq(80, banker._stashedCoins, "no overdraw");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            foreignBanker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = foreignBanker;
            int staged = PlayerPrefs.SetIntCalls;
            Harness.False(PatchEconomy_Banker.TrySpendForAutoRestock(foreignBanker, 20), "foreign refused");
            Harness.Eq(100, foreignBanker._stashedCoins, "foreign stash untouched");
            Harness.Eq(staged, PlayerPrefs.SetIntCalls, "no foreign staging");

            Fixture greek2 = Fixture.BuildGreek(sceneHandle: 3);
            Banker clientBanker = greek2.AddBanker();
            clientBanker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = clientBanker;
            NetworkBigBoss.HasWorldAuth = false;
            Harness.False(PatchEconomy_Banker.TrySpendForAutoRestock(clientBanker, 20), "client refused");
            Harness.Eq(100, clientBanker._stashedCoins, "client stash untouched");
        });

        // -------------------------------------------------------- work profile
        Harness.Test("greek applies work profile and disable restores captured values", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(banker);
            f.AssertEnhancedProfile(banker, "enhanced");
            Harness.Eq(5f, banker.coinScanRange, "domain range");
            Harness.Eq(5f, banker._coinScanner.range, "scanner forward");
            Harness.Eq(5f, banker._coinScanner.rangeBehind, "scanner behind");

            ModConfig.Enabled.Value = false;
            Harness.BankerUpdate(banker);
            f.AssertNativeProfile(banker, "restored");
        });

        Harness.Test("foreign world restores and never touches a foreign banker", () =>
        {
            Fixture greek = Fixture.BuildGreek(sceneHandle: 1);
            Banker greekBanker = greek.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(greekBanker);
            greek.AssertEnhancedProfile(greekBanker, "enhanced");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            Harness.BankerUpdate(foreignBanker);
            Harness.Eq(Fixture.NativeWalk, foreignBanker.walkSpeed, "foreign banker untouched");
            Harness.Eq(Fixture.NativeScanRange, foreignBanker.coinScanRange, "foreign scan untouched");

            Harness.BankerUpdate(greekBanker);
            greek.AssertNativeProfile(greekBanker, "restored after world switch");
        });

        Harness.Test("unknown scope defers and keeps the restore receipt", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(banker);
            BiomeHolder.Inst = null;
            Harness.BankerUpdate(banker);
            f.AssertEnhancedProfile(banker, "unknown defers");

            BiomeHolder.Inst = new BiomeHolder { FailRead = true };
            Harness.BankerUpdate(banker);
            f.AssertEnhancedProfile(banker, "fault defers");

            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 1 };
            Harness.BankerUpdate(banker);
            f.AssertNativeProfile(banker, "restored once world is known");
        });

        Harness.Test("external writes survive restoration", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(banker);
            banker.runSpeed = 9.9f;   // 外部改写
            banker.walkSpeed = 2.2f;  // 外部改写
            ModConfig.Enabled.Value = false;
            Harness.BankerUpdate(banker);
            Harness.Eq(2.2f, banker.walkSpeed, "external walk preserved");
            Harness.Eq(9.9f, banker.runSpeed, "external run preserved");
            Harness.Eq(Fixture.NativeGather, banker.coinGatherTargetPercentage, "other fields restored");
            Harness.Eq(Fixture.NativeScannerRange, banker._coinScanner.range, "scanner restored");
        });

        Harness.Test("instance id reuse cannot inherit a work profile", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker first = f.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(first);
            int reusedId = first.gameObject.Id;

            UnityEngine.Object.Destroy(first.gameObject);
            Banker second = f.AddBanker("BankerReused", kingdomBound: true);
            second.gameObject.Id = reusedId;
            second.walkSpeed = 1.11f;
            second._coinScanner.range = 6.6f;
            ModConfig.Enabled.Value = false;
            Harness.BankerUpdate(second);
            Harness.Eq(1.11f, second.walkSpeed, "reused id untouched");
            Harness.Eq(6.6f, second._coinScanner.range, "reused scanner untouched");
        });

        Harness.Test("failed restore setter keeps the receipt for retry", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(banker);
            ModConfig.Enabled.Value = false;
            banker.FailWalkSpeedWrite = true; // 下一次写入抛错
            Harness.BankerUpdate(banker);
            Harness.Eq(1.95f, banker.walkSpeed, "faulted write did not land");
            Harness.Eq(Fixture.NativeGather, banker.coinGatherTargetPercentage, "earlier fields restored");

            Harness.BankerUpdate(banker); // receipt retained => 下帧继续归还
            Harness.Eq(Fixture.NativeWalk, banker.walkSpeed, "receipt retried successfully");
            Harness.Eq(Fixture.NativeRun, banker.runSpeed, "later fields restored");
            Harness.Eq(Fixture.NativeScannerInterval, banker._coinScanner._interval, "scanner restored");
        });

        Harness.Test("disable then re-enable reapplies the profile", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            PatchEconomy_Banker.Awake_Postfix(banker);
            ModConfig.Enabled.Value = false;
            Harness.BankerUpdate(banker);
            f.AssertNativeProfile(banker, "restored");
            ModConfig.Enabled.Value = true;
            PatchEconomy_Banker.Awake_Postfix(banker);
            f.AssertEnhancedProfile(banker, "reapplied");
        });

        // ------------------------------------------------- dedupe and OnDestroy
        Harness.Test("greek dedupe destroys duplicate and its OnDestroy is skipped", () =>
        {
            Harness.WireDestroyToOnDestroy();
            Fixture f = Fixture.BuildGreek();
            Banker original = f.AddBanker();
            Banker duplicate = f.AddBanker("Banker_Extra", kingdomBound: false);

            Harness.False(PatchEconomy_Banker.Awake_Prefix(duplicate), "duplicate Awake skipped");
            Harness.False(duplicate.gameObject.Alive, "duplicate destroyed");
            Harness.True(original.gameObject.Alive, "original kept");
            // Destroy 触发的 OnDestroy 必须仍跳过原生注销（owned cleanup）
            Harness.Eq(0, duplicate.OnDestroyCalls, "native OnDestroy skipped for duplicate");

            // 即使之后进入其他世界，同一个 duplicate 的 OnDestroy 仍必须跳过
            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            PatchEconomy_Banker.OnDestroy_Prefix(duplicate);
            Harness.Eq(0, duplicate.OnDestroyCalls, "still skipped");
        });

        Harness.Test("real banker OnDestroy still runs", () =>
        {
            Harness.WireDestroyToOnDestroy();
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            UnityEngine.Object.Destroy(banker.gameObject);
            Harness.Eq(1, banker.OnDestroyCalls, "native destroy ran");
            Harness.False(banker.gameObject.Alive, "destroyed");
        });

        Harness.Test("foreign world leaves native duplicates alive", () =>
        {
            Fixture f = Fixture.BuildForeign();
            Banker first = f.AddBanker();
            Banker second = f.AddBanker("Banker2", kingdomBound: false);
            Harness.True(PatchEconomy_Banker.Awake_Prefix(second), "foreign native Awake");
            Harness.True(first.gameObject.Alive && second.gameObject.Alive, "both natives alive");
        });

        Harness.Test("stale layer banker cannot kill the new world banker", () =>
        {
            Harness.WireDestroyToOnDestroy();
            Fixture old = Fixture.BuildGreek(sceneHandle: 1);
            Banker stale = old.AddBanker();
            Fixture next = Fixture.BuildGreek(sceneHandle: 2);
            Banker fresh = next.AddBanker();

            Harness.True(PatchEconomy_Banker.Awake_Prefix(fresh), "new world banker survives");
            Harness.True(fresh.gameObject.Alive, "fresh alive");
            Harness.True(stale.gameObject.Alive, "old world banker untouched");
        });

        Harness.Test("on destroy saves the owned tail delta in the same world", () =>
        {
            Harness.WireDestroyToOnDestroy();
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            banker._stashedCoins = 104; // 原生在两次观测之间改动
            UnityEngine.Object.Destroy(banker.gameObject);
            Harness.Eq(104, PlayerPrefs.Ints[Harness.SharedKey], "tail delta saved");
            Harness.Eq(1, banker.OnDestroyCalls, "native destroy ran");
            Harness.True(PlayerPrefs.SaveCalls >= 2, "staged ledger flushed");
        });

        // ----------------------------------------------------- auto restock
        Harness.Test("foreign tick releases orders without debit or teleport", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = banker;
            Harness.EnableRoles(0);
            GameObject actor = Sim.NewActor("Assistant", f.Layer);
            Harness.InjectOrder(0, 0, actor, 10, 3f, 3f);
            Harness.PrepareWorldPointers();

            PatchEconomy_AutoRestock.Tick(banker, Managers.Inst, false);
            Harness.Eq(1, Harness.OrderCount(), "greek keeps the order");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            BankAssistantCoordinator.MainBanker = banker;
            BankAssistantCoordinator.LeaseValid = false; // 旧世界的 lease 已失效
            PatchEconomy_AutoRestock.Tick(banker, Managers.Inst, false);
            Harness.Eq(0, Harness.OrderCount(), "foreign releases the order");
            Harness.Eq(1, BankAssistantCoordinator.Releases.Count, "released once");
            Harness.False(BankAssistantCoordinator.Releases[0].ReturnHome, "no home teleport");
            Harness.Eq(0, BankAssistantCoordinator.Teleports, "no teleport");
            Harness.Eq(100, banker._stashedCoins, "no debit outside greek");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no ledger write outside greek");
        });

        Harness.Test("summary text follows the world scope", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Harness.EqStr("已关闭", PatchEconomy_AutoRestock.GetSummary(0), "greek role disabled");

            Harness.EnableRoles(0);
            Harness.EqStr("初始化中", PatchEconomy_AutoRestock.GetSummary(0), "greek enabled");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Harness.EqStr("仅希腊世界生效", PatchEconomy_AutoRestock.GetSummary(0), "foreign notice");

            BiomeHolder.Inst = null;
            Harness.EqStr("世界加载中", PatchEconomy_AutoRestock.GetSummary(0), "unknown notice");

            ModConfig.Enabled.Value = false;
            Harness.EqStr("已关闭", PatchEconomy_AutoRestock.GetSummary(0), "disabled notice");
        });

        Harness.Test("finalize purchase is gated and debits only in greek", () =>
        {
            Harness.WireDestroyToOnDestroy();
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = banker;
            Harness.EnableRoles(0);
            GameObject actor = Sim.NewActor("Assistant", f.Layer);
            Harness.InjectOrder(0, 0, actor, 10, 3f, 3f);
            Harness.PrepareWorldPointers();

            object order = Harness.Order(0);
            PayableShop shop = Harness.GetField<PayableShop>(order, "Shop");
            Harness.FinalizePurchase(order, banker, 0);
            Harness.Eq(1, shop.TransactionCompleteCalls, "native purchase once");
            Harness.Eq(80, banker._stashedCoins, "double price debited once");
            Harness.EqStr("Departing", Harness.OrderPhase(order), "paid order departs");

            // 其他世界：同一个订单必须被取消且绝不扣款
            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            foreignBanker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = foreignBanker;
            GameObject actor2 = Sim.NewActor("Assistant2", foreign.Layer);
            PatchEconomy_AutoRestock.Reset(false);
            Harness.InjectOrder(0, 1, actor2, 10, 3f, 3f);
            object order2 = Harness.Order(0);
            PayableShop shop2 = Harness.GetField<PayableShop>(order2, "Shop");
            Harness.FinalizePurchase(order2, foreignBanker, 0);
            Harness.Eq(0, shop2.TransactionCompleteCalls, "no native purchase outside greek");
            Harness.Eq(100, foreignBanker._stashedCoins, "no debit outside greek");
            Harness.Eq(0, Harness.OrderCount(), "cancelled order removed");
        });

        Harness.Test("coordinator bind retries once the world is ready", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            PatchEconomy_BankAssistants.Bound = false;
            Time.frameCount = 200;
            Harness.BankerUpdate(banker);
            Harness.Eq(1, PatchEconomy_BankAssistants.EnsureCalls, "late bind retried");
            Harness.True(PatchEconomy_BankAssistants.Bound, "coordinator bound");

            // 身份未就绪（本体还没进 gameLayer）时不绑定，等 Update 低频重试
            PatchEconomy_BankAssistants.Bound = false;
            PatchEconomy_BankAssistants.EnsureCalls = 0;
            f.Kingdom.banker = null;
            Banker unparented = Sim.NewActor("BankerUnparented").AddComponent<Banker>();
            PatchEconomy_Banker.Awake_Postfix(unparented);
            Harness.Eq(0, PatchEconomy_BankAssistants.EnsureCalls, "identity gate blocks early bind");
        });

        return Harness.Finish();
    }
}
