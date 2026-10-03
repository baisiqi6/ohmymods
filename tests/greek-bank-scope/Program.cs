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
            // 旧断言观察 PP 共享键的 seed/stage；生产改用原生 campaign balance 后，
            // 首次 seed 只来自原生 Apply 收据（R3 Live），落盘只经 Global 保存门。
            Harness.True(Fixture.SeedNative(banker, 100), "native apply seeds the shared account");
            Harness.Eq(100, banker._stashedCoins, "primed stash");
            Harness.Eq(100, Harness.BankLive(), "R3 Live seeded once from the native stash");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP ledger write");
            Harness.Eq(0, PlayerPrefs.SaveCalls, "no legacy PP flush");

            banker.InterestPerDay = 5;
            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            banker.HandleOnDayStart(); // native interest, exactly once
            PatchEconomy_Banker.HandleOnDayStart_Postfix(banker);
            Harness.Eq(105, banker._stashedCoins, "interest once");
            Harness.Eq(105, Harness.BankLive(), "R3 Live follows the interest once");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "durable bank writes only happen at the native save gate");

            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            banker.HandleOnDayStart();
            PatchEconomy_Banker.HandleOnDayStart_Postfix(banker);
            Harness.Eq(110, banker._stashedCoins, "second day interest");
            Harness.Eq(110, Harness.BankLive(), "no double accounting");
        });

        Harness.Test("foreign account banker never reads or writes the greek shared ledger", () =>
        {
            // 旧断言观察全局 PP 共享键（跨账号串账）；生产改用原生 campaign balance 后，
            // 隔离面变成“账户 + 903 身份”：另一个 campaign account 不导入、不改写 greek 账本。
            Fixture greek = Fixture.BuildGreek(sceneHandle: 1);
            Banker greekBanker = greek.AddBanker();
            Harness.True(Fixture.SeedNative(greekBanker, 5709), "greek account seeded");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker banker = foreign.AddBanker();
            Fixture.SwitchAccount(1); // 另一个 campaign account：旧“全局共享”期望的替代隔离面
            Fixture.CompleteCatalogReconcile(); // 真实目录已含该账户，拒绝不能只是“冷未知”
            banker._stashedCoins = 77;
            int reads = GlobalSaveData._loaded.prefs.contents.Reads;

            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            banker.HandleOnDayStart();
            PatchEconomy_Banker.HandleOnDayStart_Postfix(banker);
            PatchEconomy_Banker.Update_Postfix(banker);

            Harness.Eq(77, banker._stashedCoins, "foreign native stash untouched");
            Harness.Eq(reads, GlobalSaveData._loaded.prefs.contents.Reads, "no shared document read for the foreign account");
            Harness.False(Harness.BankLiveAvailable(), "foreign account has no shared Live");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP write");
            Harness.Eq(0, PlayerPrefs.GetIntCalls, "no legacy PP read");
            Harness.Eq(0, PlayerPrefs.SaveCalls, "no legacy PP flush");

            Fixture.SwitchAccount(0);
            Harness.Eq(5709, Harness.BankLive(), "greek account ledger untouched");
            Harness.Eq(5709, greekBanker._stashedCoins, "greek banker untouched");
        });

        Harness.Test("foreign banker never overwrites greek shared balance", () =>
        {
            Fixture greek = Fixture.BuildGreek(sceneHandle: 1);
            Banker greekBanker = greek.AddBanker();
            Harness.True(Fixture.SeedNative(greekBanker, 400), "greek account seeded");
            Harness.Eq(400, Harness.BankLive(), "greek seeded");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            Fixture.SwitchAccount(1);
            Fixture.CompleteCatalogReconcile();
            foreignBanker._stashedCoins = 12;
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(foreignBanker, 5), "deposit refused");
            PatchEconomy_Banker.Update_Postfix(foreignBanker);
            Harness.Eq(12, foreignBanker._stashedCoins, "foreign stash untouched");

            Fixture.SwitchAccount(0);
            Harness.Eq(400, Harness.BankLive(), "greek ledger intact");
        });

        Harness.Test("scope exit gates economy, same-world reentry keeps the account prime", () =>
        {
            // 新契约（bank-native R2 有意变更）：prime 凭证绑定 903 身份 + world，
            // 不再由 biome 翻转吊销（旧 SuspendPrimeProof 已删除）；希腊 scope 只闸经济入口。
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Harness.True(Fixture.SeedNative(banker, 100), "native apply seeds the shared account");
            Harness.Eq(100, Harness.BankLive(), "seeded");

            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 1 };
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(banker, 1), "deposit refused outside greek");
            PatchEconomy_Banker.Update_Postfix(banker); // 离开希腊：只落盘已观察值，不吊销 prime
            Harness.Eq(100, Harness.BankLive(), "no fabricated ledger value on scope exit");
            banker._stashedCoins = 88;                  // 同账户本体的原生变动

            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = BiomeHolder.GreeceBiomeIndex };
            Harness.Eq(1, PatchEconomy_Banker.DepositFromAssistant(banker, 1), "reentry deposits");
            Harness.Eq(89, banker._stashedCoins, "same-world actor keeps its native balance");
            Harness.Eq(89, Harness.BankLive(), "shared ledger follows the account's actor");
        });

        Harness.Test("world context change forces a reprime", () =>
        {
            Fixture greek = Fixture.BuildGreek(sceneHandle: 1);
            Banker banker = greek.AddBanker();
            Harness.True(Fixture.SeedNative(banker, 100), "greek account seeded");

            // 同一个 banker 被搬进新 world（Persistent 恢复路径）：按真实原生顺序驱动
            // BeforeNativeApply（retire 在 Apply 覆盖前读旧值）→ ApplyData 新岛值(55) → Applied。
            Fixture next = Fixture.BuildGreek(sceneHandle: 2);
            banker.transform.Parent = next.Layer;
            banker.gameObject.scene = next.Layer.gameObject.scene;
            next.Kingdom.banker = banker;
            PatchEconomy_Banker.BeforeNativeApply(banker);
            banker.Persistent_IBehaviour_ApplyData(new BankerData { stashedCoins = 55 });
            PatchEconomy_Banker.AfterNativeApply(banker, 55);
            PatchEconomy_Banker.Update_Postfix(banker);
            Harness.Eq(100, Harness.BankLive(), "foreign drift never saved");
            Harness.Eq(100, banker._stashedCoins, "old receipt value retained until reprime");

            Harness.Eq(1, PatchEconomy_Banker.DepositFromAssistant(banker, 1), "reprimed in new world");
            Harness.Eq(101, banker._stashedCoins, "shared value adopted again");
            Harness.Eq(101, Harness.BankLive(), "ledger follows the new world actor");
        });

        Harness.Test("instance id reuse cannot inherit the prime proof", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker first = f.AddBanker();
            Harness.True(Fixture.SeedNative(first, 100), "greek account seeded");
            int reusedId = first.gameObject.Id;

            UnityEngine.Object.Destroy(first.gameObject);
            Banker second = f.AddBanker("BankerReused", kingdomBound: true);
            second.gameObject.Id = reusedId;
            second._stashedCoins = 7;
            PatchEconomy_Banker.Update_Postfix(second);
            Harness.Eq(100, Harness.BankLive(), "reused id did not write");
            Harness.Eq(1, PatchEconomy_Banker.DepositFromAssistant(second, 1), "reused id reprimes");
            Harness.Eq(101, second._stashedCoins, "shared value adopted");
            Harness.Eq(101, Harness.BankLive(), "ledger follows the adopted value");
        });

        Harness.Test("unknown scope never primes", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BiomeHolder.Inst = null;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            Harness.False(Harness.BankLiveAvailable(), "no shared Live while unknown");
            Harness.Eq(0, PlayerPrefs.GetIntCalls, "no legacy PP read");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP write");
            BiomeHolder.Inst = new BiomeHolder { FailRead = true };
            PatchEconomy_Banker.HandleOnDayStart_Prefix(banker);
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "faulting scope never primes");
            Harness.False(Harness.BankLiveAvailable(), "faulting scope never seeds");
        });

        Harness.Test("deposit commits in greek and is refused elsewhere", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            // 新生产不允许无收据造零；首 seed 走真实原生 Apply 收据（typed BankerData=0）。
            Harness.True(Fixture.SeedNative(banker, 0), "native apply seeds an empty account");
            Harness.Eq(7, PatchEconomy_Banker.DepositFromAssistant(banker, 7), "greek deposit");
            Harness.Eq(7, banker._stashedCoins, "stash credited");
            Harness.Eq(7, Harness.BankLive(), "ledger follows the deposit");
            Harness.Eq(1, f.Kingdom.castle.StashCalls.Count, "castle refreshed");
            Harness.Eq(7, f.Kingdom.castle.StashCalls[0], "castle value");
            Harness.Eq(7, f.Stats.StatCalls[Stat.CoinsInBank], "stats value");
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(banker, 0), "zero refused");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            Harness.Eq(0, PatchEconomy_Banker.DepositFromAssistant(foreignBanker, 3), "foreign refused");
            Harness.Eq(0, foreignBanker._stashedCoins, "no foreign credit");
            Harness.Eq(0, foreign.Kingdom.castle.StashCalls.Count, "no foreign castle write");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP staging");

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
            Harness.True(Fixture.SeedNative(banker, 100), "greek account seeded");
            BankAssistantCoordinator.MainBanker = banker;
            Harness.True(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 20), "greek debit");
            Harness.Eq(80, banker._stashedCoins, "debited once");
            Harness.Eq(80, f.Kingdom.castle.StashCalls[0], "castle follows");
            Harness.False(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 200), "insufficient funds");
            Harness.Eq(80, banker._stashedCoins, "no overdraw");
            Harness.Eq(80, Harness.BankLive(), "ledger follows the debit");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 2);
            Banker foreignBanker = foreign.AddBanker();
            foreignBanker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = foreignBanker;
            Harness.False(PatchEconomy_Banker.TrySpendForAutoRestock(foreignBanker, 20), "foreign refused");
            Harness.Eq(100, foreignBanker._stashedCoins, "foreign stash untouched");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP staging");

            Fixture greek2 = Fixture.BuildGreek(sceneHandle: 3);
            Banker clientBanker = greek2.AddBanker();
            clientBanker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = clientBanker;
            NetworkBigBoss.HasWorldAuth = false;
            Harness.False(PatchEconomy_Banker.TrySpendForAutoRestock(clientBanker, 20), "client refused");
            Harness.Eq(100, clientBanker._stashedCoins, "client stash untouched");
        });

        Harness.Test("restock debit works at night and normal banking still works (all-day)", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Harness.True(Fixture.SeedNative(banker, 100), "greek account seeded");
            BankAssistantCoordinator.MainBanker = banker;

            f.Kingdom.isDaytime = false;
            Harness.True(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 20), "night debit accepted");
            Harness.Eq(80, banker._stashedCoins, "night balance debited once");
            Harness.Eq(80, Harness.BankLive(), "night debit stages the ledger");
            Harness.Eq(80, f.Kingdom.castle.StashCalls[f.Kingdom.castle.StashCalls.Count - 1],
                "night debit refreshes castle");
            // 常规银行操作不受影响：夜间存入照常入账。
            Harness.Eq(5, PatchEconomy_Banker.DepositFromAssistant(banker, 5), "night deposit still accepted");
            Harness.Eq(85, banker._stashedCoins, "night deposit credited");
            Harness.Eq(85, Harness.BankLive(), "night deposit observed");

            f.Kingdom.isDaytime = true;
            Harness.True(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 20), "day debit works");
            Harness.Eq(65, banker._stashedCoins, "day debit commits once");
            Harness.Eq(65, f.Kingdom.castle.StashCalls[f.Kingdom.castle.StashCalls.Count - 1],
                "day debit refreshes castle");
            Harness.Eq(65, Harness.BankLive(), "day debit observed");
        });

        Harness.Test("night flip during prime no longer refuses the debit (all-day)", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = banker;
            // 旧 PP 种值迁移：文档先写不绑定，prime 读取时导入（OnGet 模拟读取中的原生回调）。
            Fixture.WriteDocumentRaw(100);
            GlobalSaveData._loaded.prefs.contents.OnGet = key =>
            {
                if (key == Harness.BankDocumentKey) f.Kingdom.isDaytime = false;
            };
            Harness.True(PatchEconomy_Banker.TrySpendForAutoRestock(banker, 20),
                "night flip inside prime cannot refuse the debit");
            Harness.Eq(1, GlobalSaveData._loaded.prefs.contents.Reads, "prime read still happens before the debit");
            Harness.Eq(80, banker._stashedCoins, "debit commits after the prime-time flip");
            Harness.Eq(80, f.Kingdom.castle.StashCalls[f.Kingdom.castle.StashCalls.Count - 1],
                "castle refreshed after the prime-time flip");
            Harness.Eq(80, Harness.BankLive(), "ledger observed once after the prime-time flip");
            Harness.Eq(0, PlayerPrefs.SetIntCalls, "no legacy PP ledger write");
        });

        Harness.Test("night tick keeps the pending order without debit (all-day)", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._stashedCoins = 100;
            BankAssistantCoordinator.MainBanker = banker;
            Harness.EnableRoles(0);
            GameObject actor = Sim.NewActor("Assistant", f.Layer);
            Harness.InjectOrder(0, 0, actor, 10, 3f, 3f);
            Harness.PrepareWorldPointers();

            f.Kingdom.isDaytime = false;
            PatchEconomy_AutoRestock.Tick(banker, Managers.Inst, false);
            Harness.Eq(1, Harness.OrderCount(), "night keeps the pending order");
            Harness.Eq(100, banker._stashedCoins, "night tick debits nothing");
            Harness.Eq(0, BankAssistantCoordinator.Releases.Count, "nightfall releases nobody");
            Harness.EqStr("初始化中", PatchEconomy_AutoRestock.GetSummary(0), "no night-pause summary");

            // 白天同一注入订单同样保持。
            GameObject dayActor = Sim.NewActor("AssistantDay", f.Layer);
            Harness.InjectOrder(0, 1, dayActor, 10, 3f, 3f);
            f.Kingdom.isDaytime = true;
            PatchEconomy_AutoRestock.Tick(banker, Managers.Inst, false);
            Harness.Eq(2, Harness.OrderCount(), "daylight keeps injected orders too");
            PatchEconomy_AutoRestock.Reset(false);
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
            Harness.True(Fixture.SeedNative(banker, 100), "greek account seeded");
            banker._stashedCoins = 104; // 原生在两次观测之间改动
            UnityEngine.Object.Destroy(banker.gameObject);
            Harness.Eq(104, Harness.BankLive(), "tail delta saved");
            Harness.Eq(1, banker.OnDestroyCalls, "native destroy ran");
            Harness.Eq(0, PlayerPrefs.SaveCalls, "no legacy PP flush");
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
            Harness.False(Harness.BankLiveAvailable(), "no shared Live fabricated outside greek");
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
            Harness.True(Fixture.SeedNative(banker, 100), "greek account seeded");
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
