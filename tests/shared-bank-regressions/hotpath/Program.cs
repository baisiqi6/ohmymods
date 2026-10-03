using System;
using KingdomEnhancedMod;
class Perf {
 static void Main() {
  Harness.ResetStatics();Fixture.NewWorld();
  for(int i=1;i<64;i++)GlobalSaveData._loaded.campaigns.Add(new CampaignSaveData());
  var bank=Fixture.NewBanker(90);
  UnityEngine.Object.CampaignPointerReads=0;
  PatchEconomy_Banker.Update_Prefix(bank);PatchEconomy_Banker.Update_Postfix(bank);
  Console.WriteLine("64 held campaigns; one unchanged banker frame; campaign Pointer reads="+UnityEngine.Object.CampaignPointerReads);
 }
}
