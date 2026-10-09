using KingdomEnhancedMod;
using UnityEngine;
int checks=0;
void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
void Refresh(float t){Time.unscaledTime=t;CalendarHud.Tick();GUI.Labels.Clear();ImGuiCompat.Textures.Clear();CalendarHud.Draw();}
bool Has(string s)=>GUI.Labels.Any(x=>x.Text==s);
var m=Managers.Inst=new Managers();
var campaign=new CampaignSaveData{Stored=96};GlobalSaveData.Raw=new(){campaigns=new(){campaign}};
GreekBankScope.IsActive=true;Refresh(1);
Check(Has("96")&&Has("储存钻石"),"real campaign storage cache reaches top HUD");
Check(Has("主城金库")&&Has("80")&&Has("第 15 天")&&Has("外墙耐久上限")&&Has("×3"),"old columns retained");
int reads=CampaignSaveData.Reads,bankReads=BankAssistantCoordinator.Reads;
campaign.Stored=92;GUI.Labels.Clear();CalendarHud.Draw();Check(Has("96"),"Draw uses cached value");
Check(CampaignSaveData.Reads==reads&&BankAssistantCoordinator.Reads==bankReads,"Draw does not read storage or treasury");
Refresh(1.2f);Check(Has("96"),"half second throttle");Refresh(1.6f);Check(Has("92"),"withdrawal updates next sample");
campaign.Stored=0;Refresh(2.2f);Check(Has("0"),"real zero is not unavailable");
campaign.ThrowOnStored=true;Refresh(2.8f);Check(Has("—")&&Has("第 15 天"),"storage read fault does not hide calendar");campaign.ThrowOnStored=false;
campaign.Stored=5;m.kingdom.playerOne=new(){wallet=new()};m.kingdom.playerTwo=new(){wallet=new()};Refresh(3.4f);
Check(Has("5")&&Has("储存钻石")&&!GUI.Labels.Any(x=>x.Text.StartsWith("1P ")||x.Text.StartsWith("2P ")),"shared campaign shown once, two wallets never summed");
GlobalSaveData.Raw._currentCampaign=1;Refresh(4f);Check(Has("—")&&!Has("5"),"invalid active selector cannot show slot zero");
GlobalSaveData.Raw._currentCampaign=0;Refresh(4.6f);
foreach(bool greek in new[]{true,false})foreach(var screen in new[]{(320,200),(640,360),(1280,720),(1920,1080)}){
GreekBankScope.IsActive=greek;Screen.width=screen.Item1;Screen.height=screen.Item2;GUI.Labels.Clear();CalendarHud.Draw();
Check(GUI.Labels.Count>0,"HUD draws at supported size");
Check(GUI.Labels.All(x=>x.Rect.x*x.Scale>=0&&(x.Rect.x+x.Rect.width)*x.Scale<=Screen.width+.1f),"labels remain in viewport");
var primary=GUI.Labels.Where(x=>x.Rect.y==18).GroupBy(x=>x.Text).Select(g=>g.First()).OrderBy(x=>x.Rect.x).ToArray();
Check(primary.Zip(primary.Skip(1),(a,b)=>a.Rect.x+a.Rect.width<=b.Rect.x).All(x=>x),"primary columns do not overlap");
Check(greek?Has("主城金库"):!Has("主城金库"),"treasury conditional, gems present in both biomes");
}
GUI.color=new(.1f,.2f,.3f);GUI.contentColor=new(.4f,.5f,.6f);GUI.backgroundColor=new(.7f,.8f,.9f);GUI.matrix=new(){scale=3};GUI.enabled=false;GUI.changed=true;GUI.depth=27;
var skin=GUI.skin;GUI.ThrowOnText="5";CalendarHud.Draw();GUI.ThrowOnText=null;
Check(GUI.color.r==.1f&&GUI.contentColor.r==.4f&&GUI.backgroundColor.r==.7f&&GUI.matrix.scale==3&&!GUI.enabled&&GUI.changed&&GUI.depth==27&&GUI.skin==skin,"GUI state restored after gem label fault");
ModConfig.ShowCalendarHud.Value=false;CalendarHud.Tick();Check(PatchUI_CalendarGems.ValueText=="—","disabled HUD clears gem cache");
ModConfig.ShowCalendarHud.Value=true;Managers.Inst=new();GlobalSaveData.Raw=null;Refresh(10);Check(Has("—")&&!Has("5"),"new world has no stale prior campaign");
GlobalSaveData.Raw=new(){campaigns=new(){new(){Stored=11}}};Refresh(11);Check(Has("11"),"loaded current campaign shows its own storage");
Managers.Inst.game.state=Game.State.Loading;Refresh(12);Check(PatchUI_CalendarGems.ValueText=="—"&&GUI.Labels.Count==0,"loading clears and hides stale storage");
Managers.Inst.game.state=Game.State.Playing;Refresh(13);Check(Has("11"),"playing restores current balance");
Managers.Inst.game.state=Game.State.Menu;GlobalSaveData.Raw.campaigns[0].Stored=12;Refresh(14);Check(Has("12"),"pause in same known world can refresh storage");
Managers.Inst=new();Managers.Inst.world.Pointer=(IntPtr)110;Managers.Inst.world.gameLayer.Pointer=(IntPtr)130;Managers.Inst.director.Pointer=(IntPtr)120;Managers.Inst.game.state=Game.State.Menu;Refresh(15);Check(GUI.Labels.Count==0&&PatchUI_CalendarGems.ValueText=="—","new menu does not borrow previous world cache");
Managers.Inst.game.state=Game.State.NetworkClientPlaying;NetworkBigBoss.Online=true;Refresh(16);Check(Has("—")&&Has("第 15 天"),"network campaign storage remains explicitly unknown");
Check(CampaignSaveData.Writes==0&&GlobalSaveData.LoadedReads==0&&GlobalSaveData.SelectorReads==0,"HUD never creates a save or writes stored gems");
Check(Wallet.Reads==0&&Wallet.Writes==0,"HUD integration never reads or writes wallet");
Console.WriteLine($"ALL PASS ({checks} HUD integration assertions)");
