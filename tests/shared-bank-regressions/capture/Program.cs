using System;
using KingdomEnhancedMod;
using Coatsink.Common;
class Adjacent {
 const string Key="MyMod_SharedBankNative_v1";
 static bool Gate() { var t=typeof(SharedBankNative).GetNestedType("AsyncGate",Harness.AnyStatic);return (bool)t.GetMethod("Prefix",Harness.AnyStatic).Invoke(null,new object[]{GlobalSaveData._loaded,null,true}); }
 static IslandSaveData Island() { var island=new IslandSaveData{land=0}; return island; }
 static void Row(IslandSaveData island,Banker bank) {
  island.objects.Add(new IslandSaveData.ObjectData{uniqueID="bank-root",netID=903,componentData2=new(){new IslandSaveData.ComponentData{name="Banker",type="BankerData",data="{\"stashedCoins\":"+bank._stashedCoins+"}"}}});
 }
 static void Save(Banker bank) {
  var scope=SharedBankNative.BeginSave(0,0,0);
  var island=Island();Row(island,bank);
  IslandSaveData.CurrentlySavingIsland=island;
  SharedBankNative.ObserveId(bank.gameObject.GetComponent<Persistent>(),"bank-root");SharedBankNative.Marker(island);SharedBankNative.EndSave(scope,true);
 }
 static int Main() {
  Harness.Test("OFF fresh main bank saves public90 before first Update",()=>{
   Fixture.NewWorld();Fixture.PublicDocument(90);ModConfig.Enabled.Value=false;var bank=Fixture.NewBanker(0,apply:false);Save(bank);
   Harness.True(Gate(),"first complete capture accepted");Harness.Eq(90,bank._stashedCoins,"actor primed before capture");
  });
  Harness.Test("OFF replacement first save after reliable oldclaim retirement accepted",()=>{
   Fixture.NewWorld();var old=Fixture.NewBanker(90);ModConfig.Enabled.Value=false;var bank=Fixture.NewBanker(0,apply:false);Save(bank);
   Console.WriteLine("OBSERVED replacement serial="+Harness.GetStatic<int>(typeof(SharedBankNative),"_bankSerial")+" actor="+bank._stashedCoins);
   Harness.True(Gate(),"complete first save after prefix-owned retirement accepted");
  });
  Harness.Test("temporary current account read heals without owner replacement",()=>{
   Fixture.NewWorld();var bank=Fixture.NewBanker(90);Save(bank);var g=GlobalSaveData._loaded;
   g.ThrowOnCurrentRead=true;Harness.False(SharedBankNative.TryLive(out _,out _,out _),"temporary read fails");g.ThrowOnCurrentRead=false;
   Console.WriteLine("OBSERVED healed account reason="+Harness.GetStatic<string>(typeof(SharedBankNative),"_reason"));
   Harness.True(Gate(),"normal Global retry resumes after read recovers");Harness.True(SharedBankNative.TryLive(out _,out _,out int live),"current account recovers");Harness.Eq(90,live,"balance preserved");
  });
  Harness.Test("foreign lifecycle change after prefix prep still rejects capture",()=>{
   Fixture.NewWorld();Fixture.NewBanker(90);ModConfig.Enabled.Value=false;var bank=Fixture.NewBanker(0,apply:false);
   var scope=SharedBankNative.BeginSave(0,0,0);
   var island=Island();Row(island,bank);
   IslandSaveData.CurrentlySavingIsland=island;
   SharedBankNative.ObserveId(bank.gameObject.GetComponent<Persistent>(),"bank-root");SharedBankNative.Marker(island);
   SharedBankNative.BankLifecycleChanged();
   SharedBankNative.EndSave(scope,true);
   Harness.False(Gate(),"capture after foreign lifecycle change rejected");
  });
  Harness.Test("account read recovery keeps real capture fault rejection",()=>{
   Fixture.NewWorld();var bank=Fixture.NewBanker(90);
   var scope=SharedBankNative.BeginSave(0,0,0);
   var island=Island();Row(island,bank);
   IslandSaveData.CurrentlySavingIsland=island;
   SharedBankNative.ObserveId(bank.gameObject.GetComponent<Persistent>(),"");
   SharedBankNative.Marker(island);SharedBankNative.EndSave(scope,true);
   var g=GlobalSaveData._loaded;g.ThrowOnCurrentRead=true;Harness.False(SharedBankNative.TryLive(out _,out _,out _),"temporary read fails");g.ThrowOnCurrentRead=false;
   Console.WriteLine("OBSERVED protected fault reason="+Harness.GetStatic<string>(typeof(SharedBankNative),"_reason"));
   Harness.False(Gate(),"source fault still rejects after account read heals");
  });
  return Harness.Finish();
 }
}
