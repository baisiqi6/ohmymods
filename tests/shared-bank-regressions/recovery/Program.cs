using System;
using System.Reflection;
using KingdomEnhancedMod;
using Coatsink.Common;

static class Proof
{
    const string Key = "MyMod_SharedBankNative_v1";
    static bool Gate(Il2CppSystem.Action<SaveLoadResult> callback = null)
    {
        Type nested = typeof(SharedBankNative).GetNestedType("AsyncGate", Harness.AnyStatic);
        return (bool)nested.GetMethod("Prefix", Harness.AnyStatic).Invoke(null,
            new object[] { GlobalSaveData._loaded, callback, true });
    }
    static void Save(Banker banker, bool invalidId = false)
    {
        var island = new IslandSaveData { land = Managers.Inst.game.currentLand };
        island.objects.Add(new IslandSaveData.ObjectData {
            uniqueID = "bank-root", netID = 903,
            componentData2 = new() { new IslandSaveData.ComponentData {
                name="Banker", type="BankerData", data="{\"stashedCoins\":" + banker._stashedCoins + "}" } }
        });
        IslandSaveData.CurrentlySavingIsland = island;
        var scope = SharedBankNative.BeginSave(0, island.land, 0);
        var root = banker.gameObject.GetComponent<Persistent>();
        SharedBankNative.ObserveId(root, invalidId ? "" : "bank-root");
        SharedBankNative.Marker(island);
        SharedBankNative.EndSave(scope, true);
    }
    static int Main()
    {
        Harness.Test("OFF existing public90 + fresh main bank0 must remain public90", () => {
            Fixture.NewWorld(); Fixture.PublicDocument(90);
            ModConfig.Enabled.Value = false;
            Banker banker = Fixture.NewBanker(0, apply:false);
            PatchEconomy_Banker.Awake_Postfix(banker);
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            PatchEconomy_Banker.Update_Prefix(banker);
            PatchEconomy_Banker.Update_Postfix(banker);
            Save(banker);
            Harness.True(Gate(), "save gate accepted");
            string doc=GlobalSaveData._loaded.prefs.contents[Key];
            Console.WriteLine("OBSERVED OFF doc="+doc+" actor="+banker._stashedCoins+" Live="+Harness.BankLive());
            Harness.True(doc.Contains("\"coins\":90"), "existing public balance preserved");
        });
        Harness.Test("foreign island existing public90 + fresh main bank0 must share90", () => {
            Fixture.NewWorld(1); Fixture.PublicDocument(90);
            Banker banker = Fixture.NewBanker(0, apply:false);
            PatchEconomy_Banker.Awake_Postfix(banker);
            PatchEconomy_Banker.FinaliseEmerge_Prefix(banker);
            PatchEconomy_Banker.Update_Prefix(banker);
            PatchEconomy_Banker.Update_Postfix(banker);
            Save(banker);
            Harness.True(Gate(), "save gate accepted");
            string doc=GlobalSaveData._loaded.prefs.contents[Key];
            Console.WriteLine("OBSERVED foreign doc="+doc+" actor="+banker._stashedCoins+" Live="+Harness.BankLive());
            Harness.True(doc.Contains("\"coins\":90"), "existing public balance preserved");
        });
        Harness.Test("one invalid root ID must be repairable by same-source complete capture", () => {
            Fixture.NewWorld(); Banker banker=Fixture.NewBanker(90);
            Save(banker,true);
            Harness.False(Gate(), "bad checkpoint rejected");
            Save(banker);
            Console.WriteLine("OBSERVED repaired ID reason="+Harness.GetStatic<string>(typeof(SharedBankNative),"_reason"));
            Harness.True(Gate(), "valid same-source capture recovers");
        });
        Harness.Test("temporary prefs write failure + throwing callback must allow later retry", () => {
            Fixture.NewWorld(); Banker banker=Fixture.NewBanker(90); Save(banker);
            var prefs=GlobalSaveData._loaded.prefs.contents; prefs.ThrowOnSet=true;
            int calls=0;
            Harness.False(Gate(new Il2CppSystem.Action<SaveLoadResult>(_=>{calls++; throw new InvalidOperationException("callback");})),"fault blocked");
            Harness.Eq(1,calls,"callback once");
            prefs.ThrowOnSet=false;
            Console.WriteLine("OBSERVED healed prefs reason="+Harness.GetStatic<string>(typeof(SharedBankNative),"_reason"));
            Harness.True(Gate(), "same staged document retry recovers");
        });
        Harness.Test("temporary prime write failure must recover with next exact Apply and capture", () => {
            Fixture.NewWorld(); Fixture.PublicDocument(90);
            Banker banker=Fixture.NewBanker(100,apply:false);
            banker.WriteFaultWhen=_=>true;
            Harness.False(PatchEconomy_Banker.AfterNativeApply(banker,100),"first prime failed");
            banker.WriteFaultWhen=null;
            Console.WriteLine("OBSERVED healed prime reason="+Harness.GetStatic<string>(typeof(SharedBankNative),"_reason"));
            Harness.True(PatchEconomy_Banker.AfterNativeApply(banker,100),"same exact actor prime retries");
            Save(banker); Harness.True(Gate(),"valid same source capture restores save");
        });
        return Harness.Finish();
    }
}
