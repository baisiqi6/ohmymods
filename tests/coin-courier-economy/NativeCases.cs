using System;
using System.Reflection;
using Coatsink.Common;
using KingdomEnhancedMod;
using PrivateBankR3;

static class NativeCases
{
    private const string Key = "MyMod_SharedBankNative_v1";

    private static bool Gate(GlobalSaveData global, out int callbacks, out SaveLoadResult result)
    {
        int count = 0;
        SaveLoadResult observed = default;
        var callback = new Il2CppSystem.Action<SaveLoadResult>(value =>
        {
            count++;
            observed = value;
        });
        bool proceed = InvokeGate(global, callback);
        callbacks = count;
        result = observed;
        return proceed;
    }

    private static bool InvokeGate(GlobalSaveData global, Il2CppSystem.Action<SaveLoadResult> callback)
    {
        Type nested = typeof(SharedBankNative).GetNestedType("AsyncGate", Harness.AnyStatic);
        MethodInfo method = nested.GetMethod("Prefix", Harness.AnyStatic);
        return (bool)method.Invoke(null, new object[] { global, callback, true }); // 第3槽 = HarmonyX 注入的 ref __runOriginal（首个前缀为 true）
    }

    private static bool SyncGate(GlobalSaveData._Save_d__89 routine, out bool result)
    {
        Type nested = typeof(SharedBankNative).GetNestedType("SyncGate", Harness.AnyStatic);
        object[] args = { routine, true };
        bool proceed = (bool)nested.GetMethod("Prefix", Harness.AnyStatic).Invoke(null, args);
        result = (bool)args[1];
        return proceed;
    }

    private static string Document()
    {
        var contents = GlobalSaveData._loaded.prefs.contents;
        return contents.TryGetValue(Key, out string text) ? text : null;
    }

    private static IslandSaveData Island(int land, int? bankCoins = null)
    {
        var island = new IslandSaveData { land = land };
        if (bankCoins.HasValue)
            island.objects.Add(new IslandSaveData.ObjectData
            {
                uniqueID = "bank-root",
                netID = 903,
                componentData2 = new()
                {
                    new IslandSaveData.ComponentData
                    {
                        name = "Banker", type = "BankerData",
                        data = "{\"stashedCoins\":" + bankCoins.Value + "}"
                    }
                }
            });
        IslandSaveData.CurrentlySavingIsland = island;
        IslandSaveData.isSavingGame = true;
        return island;
    }

    private static void SaveBank(Banker banker, int value, bool marker = true)
    {
        IslandSaveData island = Island(Managers.Inst.game.currentLand, value);
        var scope = SharedBankNative.BeginSave(0, island.land, 0);
        SharedBankNative.ObserveId(banker.gameObject.GetComponent<Persistent>(), "bank-root");
        if (marker) SharedBankNative.Marker(island);
        SharedBankNative.EndSave(scope, true);
        IslandSaveData.isSavingGame = false;
    }

    private static void SaveNoBank(int land)
    {
        IslandSaveData island = Island(land);
        var scope = SharedBankNative.BeginSave(0, land, 0);
        SharedBankNative.Marker(island);
        SharedBankNative.EndSave(scope, true);
        IslandSaveData.isSavingGame = false;
    }

    public static void Run()
    {
        Harness.Test("Native row100 stages100 after Live90 and retries failed prefs readback", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(100);
            SaveBank(banker, 100);
            banker._stashedCoins = 90;
            PatchEconomy_Banker.Update_Postfix(banker);
            Harness.Eq(90, Harness.BankLive(), "later live observed");
            var prefs = GlobalSaveData._loaded.prefs.contents;
            prefs.ThrowOnSet = true;
            Harness.False(Gate(GlobalSaveData._loaded, out int failures, out SaveLoadResult failure),
                "write fault rejects native save");
            Harness.Eq(1, failures, "callback once");
            Harness.Eq(0x88, (int)failure, "failure receipt");
            prefs.ThrowOnSet = false;
            Harness.True(Gate(GlobalSaveData._loaded, out int successCalls, out _), "same stage retries");
            Harness.Eq(0, successCalls, "native path owns successful callback");
            Harness.True(Document().Contains("\"coins\":100"), "document retains captured100");
            Harness.NoLegacyBankWrite("Native save did not use PlayerPrefs");
        });

        Harness.Test("Native Apply100 with public Live90 Stage80 restores actor90", () =>
        {
            Fixture.NewWorld();
            Fixture.PublicDocument(80);
            Banker banker = Fixture.NewBanker(100, apply: false);
            Harness.True(SharedBankNative.Claim(banker, out object owner, out nint account, out _),
                "accurate account and 903");
            Harness.True(SharedBankNative.Observe(banker, owner, account, 90), "live becomes90");
            IslandSaveData island = Island(0, 100);
            var pop = SharedBankNative.BeginPop(island);
            SharedBankNative.Created(island.objects[0], banker.gameObject.GetComponent<Persistent>());
            PatchEconomy_Banker.BeforeNativeApply(banker);
            banker.Persistent_IBehaviour_ApplyData(new BankerData { stashedCoins = 100 });
            SharedBankNative.Applied(banker, new BankerData { stashedCoins = 100 });
            SharedBankNative.EndPop(pop, true);
            Harness.Eq(90, banker._stashedCoins, "one-time public prime");
            Harness.Eq(90, Harness.BankLive(), "Live unchanged by native100");
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _), "stage80 is saveable");
            Harness.True(Document().Contains("\"coins\":80"), "Stage stays80");
            banker._stashedCoins = 89;
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            Harness.Eq(89, banker._stashedCoins, "repeat prime does not overwrite delta");
            PatchEconomy_Banker.Update_Postfix(banker);
            Harness.Eq(89, Harness.BankLive(), "delta observed");
        });

        Harness.Test("new actor default zero cannot seed without typed Apply", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(0, apply: false);
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            Harness.False(SharedBankNative.TryLive(out _, out _, out _),
                "default native zero has no R3 Live");
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _), "no key remains native NoKey");
            Harness.True(Document() == null, "no invented zero document");
        });

        Harness.Test("complete native BankerData capture can first seed only eligible account", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(42, apply: false);
            SaveBank(banker, 42);
            Harness.Eq(42, Harness.BankLive(), "trusted complete row first seed");
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _), "new captured account ready");
            Harness.True(Document().Contains("\"coins\":42"), "captured42 persisted");

            Harness.ResetStatics();
            Fixture.NewWorld(1);
            banker = Fixture.NewBanker(42, apply: false);
            SaveBank(banker, 42);
            Harness.False(SharedBankNative.TryLive(out _, out _, out _),
                "foreign account has no public seed");
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _), "foreign nonparticipant NoKey");
        });

        Harness.Test("existing public account primes OFF restore while foreign new account does not seed", () =>
        {
            Fixture.NewWorld();
            Fixture.PublicDocument(90);
            ModConfig.Enabled.Value = false;
            Banker banker = Fixture.NewBanker(100, apply: false);
            IslandSaveData island = Island(0, 100);
            var pop = SharedBankNative.BeginPop(island);
            SharedBankNative.Created(island.objects[0], banker.gameObject.GetComponent<Persistent>());
            PatchEconomy_Banker.BeforeNativeApply(banker);
            banker.Persistent_IBehaviour_ApplyData(new BankerData { stashedCoins = 100 });
            SharedBankNative.Applied(banker, new BankerData { stashedCoins = 100 });
            SharedBankNative.EndPop(pop, true);
            Harness.Eq(90, banker._stashedCoins, "OFF existing public balance restored");
            Harness.Eq(90, Harness.BankLive(), "OFF kept R3 Live");

            Harness.ResetStatics();
            Fixture.NewWorld(1);
            Banker foreign = Fixture.NewBanker(100, apply: false);
            island = Island(0, 100);
            pop = SharedBankNative.BeginPop(island);
            SharedBankNative.Created(island.objects[0], foreign.gameObject.GetComponent<Persistent>());
            foreign.Persistent_IBehaviour_ApplyData(new BankerData { stashedCoins = 100 });
            SharedBankNative.Applied(foreign, new BankerData { stashedCoins = 100 });
            SharedBankNative.EndPop(pop, true);
            Harness.False(SharedBankNative.TryLive(out _, out _, out _),
                "foreign first restore does not create public balance");
        });

        Harness.Test("Pop true with one missing Banker Apply keeps source fault", () =>
        {
            Fixture.NewWorld();
            Fixture.PublicDocument(80);
            Banker banker = Fixture.NewBanker(100, apply: false);
            IslandSaveData island = Island(0, 100);
            var pop = SharedBankNative.BeginPop(island);
            SharedBankNative.Created(island.objects[0], banker.gameObject.GetComponent<Persistent>());
            SharedBankNative.EndPop(pop, true);
            Harness.False(Gate(GlobalSaveData._loaded, out int calls, out _), "missing Apply rejects");
            Harness.Eq(1, calls, "one failure callback");
        });

        Harness.Test("Pop false faults even after one Apply and repeated GetID is legal", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(90);
            SaveBank(banker, 90);
            IslandSaveData island = Island(0, 90);
            var pop = SharedBankNative.BeginPop(island);
            SharedBankNative.Created(island.objects[0], banker.gameObject.GetComponent<Persistent>());
            PatchEconomy_Banker.BeforeNativeApply(banker);
            banker.Persistent_IBehaviour_ApplyData(new BankerData { stashedCoins = 90 });
            SharedBankNative.Applied(banker, new BankerData { stashedCoins = 90 });
            SharedBankNative.EndPop(pop, false);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "failed whole Pop cannot certify partial Banker Apply");

            Harness.ResetStatics();
            Fixture.NewWorld();
            banker = Fixture.NewBanker(90);
            island = Island(0, 90);
            var scope = SharedBankNative.BeginSave(0, 0, 0);
            Persistent root = banker.gameObject.GetComponent<Persistent>();
            SharedBankNative.ObserveId(root, "bank-root");
            SharedBankNative.ObserveId(root, "bank-root"); // parent link may call GetID twice
            SharedBankNative.Marker(island);
            SharedBankNative.EndSave(scope, true);
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _),
                "same-root repeated ID is not duplicate row");

            Harness.ResetStatics();
            Fixture.NewWorld();
            banker = Fixture.NewBanker(90);
            island = Island(0, 90);
            island.objects.Add(new IslandSaveData.ObjectData
            {
                uniqueID = "bank-root", netID = 903,
                componentData2 = island.objects[0].componentData2
            });
            scope = SharedBankNative.BeginSave(0, 0, 0);
            SharedBankNative.ObserveId(banker.gameObject.GetComponent<Persistent>(), "bank-root");
            SharedBankNative.Marker(island);
            SharedBankNative.EndSave(scope, true);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "two final object rows with same ID reject");
        });

        Harness.Test("NoPhysical stages frozen Live but preserves older source fault", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(100);
            SaveBank(banker, 100);
            banker._stashedCoins = 90;
            PatchEconomy_Banker.Update_Postfix(banker);
            SaveBank(banker, 90, marker: false); // old source unresolved
            PatchEconomy_Banker.OnDestroy_Prefix(banker);
            Fixture.ClearBanker903();
            Managers.Inst.kingdom.banker = null;
            Managers.Inst.game.currentLand = 1;
            SharedBankNative.SceneApplied();
            IslandSaveData island = Island(1);
            var scope = SharedBankNative.BeginSave(0, 1, 0);
            SharedBankNative.Marker(island);
            SharedBankNative.EndSave(scope, true);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "NoPhysical cannot clear old source fault");
        });

        Harness.Test("NoPhysical reliable retirement stages90 then later Live80 keeps document90", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(100);
            SaveBank(banker, 100);
            banker._stashedCoins = 90;
            PatchEconomy_Banker.Update_Postfix(banker);
            PatchEconomy_Banker.OnDestroy_Prefix(banker);
            Fixture.ClearBanker903();
            Managers.Inst.kingdom.banker = null;
            Managers.Inst.game.currentLand = 1;
            SharedBankNative.SceneApplied();
            SaveNoBank(1);
            Harness.True(SharedBankNative.TryLive(out object owner, out nint account, out _),
                "public account available");
            Harness.True(SharedBankNative.Observe(null, owner, account, 80),
                "later live change is distinct from captured90");
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _), "NoPhysical document ready");
            Harness.True(Document().Contains("\"coins\":90"), "document uses frozen90");
        });

        Harness.Test("retire unreadable after closed capture stays closed; after delta faults", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(100);
            SaveBank(banker, 100);
            banker.ThrowOnStashRead = true;
            PatchEconomy_Banker.OnDestroy_Prefix(banker);
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _),
                "closed source does not invent retire fault");

            Harness.ResetStatics();
            Fixture.NewWorld();
            banker = Fixture.NewBanker(100);
            SaveBank(banker, 100);
            banker._stashedCoins = 90;
            PatchEconomy_Banker.Update_Postfix(banker);
            banker.ThrowOnStashRead = true;
            PatchEconomy_Banker.OnDestroy_Prefix(banker);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "unclosed late delta remains unresolved");
        });

        Harness.Test("ExpectedPhysical missing row never falls back to NoPhysical", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(90);
            IslandSaveData island = Island(0); // no BankerData row
            var scope = SharedBankNative.BeginSave(0, 0, 0);
            SharedBankNative.ObserveId(banker.gameObject.GetComponent<Persistent>(), "bank-root");
            SharedBankNative.Marker(island);
            SharedBankNative.EndSave(scope, true);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "missing physical row rejects save");
        });

        Harness.Test("unregistered kingdom bank claim is a source fault, not NoKey", () =>
        {
            Fixture.NewWorld();
            Banker banker = Fixture.NewBanker(90);
            Fixture.ClearBanker903(); // kingdom still claims a physical bank
            IslandSaveData island = Island(0);
            var scope = SharedBankNative.BeginSave(0, 0, 0);
            SharedBankNative.Marker(island);
            SharedBankNative.EndSave(scope, true);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "registry fault remains after Global reconcile");
            Harness.Eq(90, banker._stashedCoins, "native bank preserved");
            Fixture.RegisterBanker903(banker);
            SaveBank(banker, 90);
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _),
                "same source complete capture repairs attributable registry fault");
        });

        Harness.Test("fresh no-bank owner stays NoKey and foreign receiver fails", () =>
        {
            Fixture.NewWorld();
            Harness.True(Gate(GlobalSaveData._loaded, out _, out _), "fresh NoKey passes");
            Harness.True(Document() == null, "NoKey does not invent document");
            var foreign = new GlobalSaveData();
            foreign.campaigns.Add(new CampaignSaveData());
            Harness.False(Gate(foreign, out int calls, out SaveLoadResult failure),
                "noncurrent rejected");
            Harness.Eq(1, calls, "foreign callback once");
            Harness.Eq(0x88, (int)failure, "foreign failure code");
        });

        Harness.Test("corrupt source text survives both failure gates", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData global = GlobalSaveData._loaded;
            global.prefs.contents[Key] = "{broken";
            Harness.False(Gate(global, out int calls, out _), "corrupt async rejected");
            Harness.Eq(1, calls, "async callback once");
            var routine = new GlobalSaveData._Save_d__89 { __1__state = 0, __4__this = global };
            Harness.False(SyncGate(routine, out bool result), "sync state0 intercepted");
            Harness.False(result, "sync returns false");
            Harness.Eq(-1, routine.__1__state, "sync terminal state");
            Harness.Eq(0x88, (int)routine.@return.value, "boxed result copied back");
            Harness.True(Document() == "{broken", "corrupt text preserved");
        });

        Harness.Test("async callback throwing still blocks native and sync state1 does not notify twice", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData global = GlobalSaveData._loaded;
            global.prefs.contents[Key] = "{broken";
            int calls = 0;
            var callback = new Il2CppSystem.Action<SaveLoadResult>(_ =>
            {
                calls++;
                throw new InvalidOperationException("consumer fault");
            });
            Harness.False(InvokeGate(global, callback), "callback exception cannot reopen native save");
            Harness.Eq(1, calls, "callback called once");
            var routine = new GlobalSaveData._Save_d__89 { __1__state = 1, __4__this = global };
            Harness.True(SyncGate(routine, out _), "later sync state left to native");
            Harness.Eq(1, routine.__1__state, "later state unchanged");
        });

        Harness.Test("NoPhysical bank appearance or account switch during scope rejects capture", () =>
        {
            Fixture.NewWorld();
            Banker old = Fixture.NewBanker(90);
            SaveBank(old, 90);
            PatchEconomy_Banker.OnDestroy_Prefix(old);
            Fixture.ClearBanker903();
            Managers.Inst.kingdom.banker = null;
            Managers.Inst.game.currentLand = 1;
            SharedBankNative.SceneApplied();
            IslandSaveData island = Island(1);
            var scope = SharedBankNative.BeginSave(0, 1, 0);
            Banker appeared = Fixture.NewBanker(0, apply: false);
            SharedBankNative.BankLifecycleChanged();
            SharedBankNative.Marker(island);
            SharedBankNative.EndSave(scope, true);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "bank appearing in no-bank scope faults source");
            Harness.Eq(0, appeared._stashedCoins, "new actor not adopted as evidence");

            Harness.ResetStatics();
            Fixture.NewWorld();
            Banker bank = Fixture.NewBanker(90);
            GlobalSaveData._loaded.campaigns.Add(new CampaignSaveData());
            IslandSaveData second = Island(0, 90);
            var accountScope = SharedBankNative.BeginSave(0, 0, 0);
            GlobalSaveData._loaded.currentCampaign = 1;
            SharedBankNative.ObserveId(bank.gameObject.GetComponent<Persistent>(), "bank-root");
            SharedBankNative.Marker(second);
            SharedBankNative.EndSave(accountScope, true);
            Harness.False(Gate(GlobalSaveData._loaded, out _, out _),
                "account switch faults frozen old source");
        });

        Harness.Test("old Global late save cannot taint newly installed owner", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData old = GlobalSaveData._loaded;
            Banker bank = Fixture.NewBanker(90);
            SaveBank(bank, 90);
            GlobalSaveData next = new GlobalSaveData();
            next.campaigns.Add(new CampaignSaveData());
            GlobalSaveData._loaded = next;
            Harness.False(Gate(old, out int calls, out _), "old receiver rejected");
            Harness.Eq(1, calls, "old receiver callback once");
            Harness.True(Gate(next, out _, out _), "new empty owner NoKey");
            Harness.True(next.prefs.contents.Count == 0, "old document not copied to new owner");
        });

        Harness.Test("cold challenge deletion keeps survivor after slot reorder", () =>
        {
            Fixture.NewWorld();
            GlobalSaveData global = GlobalSaveData._loaded;
            global.challenges.Add(new CampaignSaveData());
            global.challenges.Add(new CampaignSaveData());
            global.currentChallenge = 2;
            global.prefs.contents[Key] = BankDocument.Write(new[]
            {
                (BankCategory.Challenge, 0, 50), (BankCategory.Challenge, 1, 70)
            });
            SharedBankNative.BeforeMutation(global); // existing challenge relay calls before RemoveAt
            global.challenges.RemoveAt(0);
            global.currentChallenge = 1;
            Harness.True(Gate(global, out _, out _), "mutation after reconcile saves");
            Harness.Eq(70, Harness.BankLive(), "survivor kept original account");
            Harness.True(Document().Contains("\"ordinal\":0") && Document().Contains("\"coins\":70"),
                "survivor moved to slot0 with 70");
            Harness.False(Document().Contains("\"coins\":50"), "deleted account omitted");
        });
    }
}
