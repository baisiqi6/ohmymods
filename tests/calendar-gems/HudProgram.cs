using KingdomEnhancedMod;
using UnityEngine;
int checks=0;
void Check(bool b,string s){checks++;if(!b)throw new Exception(s);}
void Refresh(float t){Time.unscaledTime=t;CalendarHud.Tick();GUI.Labels.Clear();ImGuiCompat.Textures.Clear();CalendarHud.Draw();}
bool Has(string s)=>GUI.Labels.Any(x=>x.Text==s);
var m=Managers.Inst=new Managers();var p=new Player{Pointer=(IntPtr)1,gameObject=new(),transform=new(){Parent=m.world.gameLayer}};
p.wallet=new(){Pointer=(IntPtr)2,_playerRef=p,gems=4};m.kingdom.playerOne=p;
GreekBankScope.IsActive=true;Refresh(1);
Check(Has("4")&&Has("随身钻石"),"real gem cache reaches top HUD");
Check(Has("主城金库")&&Has("80")&&Has("第 15 天")&&Has("外墙耐久上限")&&Has("×3"),"old columns retained");
int reads=Wallet.GemReads,bankReads=BankAssistantCoordinator.Reads;
p.wallet.gems=2;GUI.Labels.Clear();CalendarHud.Draw();Check(Has("4"),"Draw uses cached value");
Check(Wallet.GemReads==reads&&BankAssistantCoordinator.Reads==bankReads,"Draw does not read wallet or treasury");
Refresh(1.2f);Check(Has("4"),"half second throttle");Refresh(1.6f);Check(Has("2"),"payment pickup value updated at next sample");
p.wallet.gems=0;Refresh(2.2f);Check(Has("0"),"real zero is not unavailable");
p.wallet.ThrowOnGems=true;Refresh(2.8f);Check(Has("—")&&Has("第 15 天"),"local wallet fault does not hide calendar");p.wallet.ThrowOnGems=false;
var q=new Player{Pointer=(IntPtr)3,gameObject=new(),transform=new(){Parent=m.world.gameLayer}};q.wallet=new(){Pointer=(IntPtr)4,_playerRef=q,gems=7};m.kingdom.playerTwo=q;p.wallet.gems=5;Refresh(3.4f);
Check(Has("1P 5")&&Has("2P 钻石 7"),"two local wallets display independent values");
p.wallet=null;Refresh(4f);Check(Has("1P —")&&Has("2P 钻石 7"),"missing first wallet keeps both local identities");
p.wallet=new(){Pointer=(IntPtr)2,_playerRef=p,gems=5};Refresh(4.6f);
foreach(bool greek in new[]{true,false})foreach(var screen in new[]{(320,200),(640,360),(1280,720),(1920,1080)}){
GreekBankScope.IsActive=greek;Screen.width=screen.Item1;Screen.height=screen.Item2;GUI.Labels.Clear();CalendarHud.Draw();
Check(GUI.Labels.Count>0,"HUD draws at supported size");
Check(GUI.Labels.All(x=>x.Rect.x*x.Scale>=0&&(x.Rect.x+x.Rect.width)*x.Scale<=Screen.width+.1f),"labels remain in viewport");
var primary=GUI.Labels.Where(x=>x.Rect.y==18).GroupBy(x=>x.Text).Select(g=>g.First()).OrderBy(x=>x.Rect.x).ToArray();
Check(primary.Zip(primary.Skip(1),(a,b)=>a.Rect.x+a.Rect.width<=b.Rect.x).All(x=>x),"primary columns do not overlap");
Check(greek?Has("主城金库"):!Has("主城金库"),"treasury conditional, gems present in both biomes");
}
GUI.color=new(.1f,.2f,.3f);GUI.contentColor=new(.4f,.5f,.6f);GUI.backgroundColor=new(.7f,.8f,.9f);GUI.matrix=new(){scale=3};GUI.enabled=false;GUI.changed=true;GUI.depth=27;
var skin=GUI.skin;GUI.ThrowOnText="1P 5";CalendarHud.Draw();GUI.ThrowOnText=null;
Check(GUI.color.r==.1f&&GUI.contentColor.r==.4f&&GUI.backgroundColor.r==.7f&&GUI.matrix.scale==3&&!GUI.enabled&&GUI.changed&&GUI.depth==27&&GUI.skin==skin,"GUI state restored after gem label fault");
ModConfig.ShowCalendarHud.Value=false;CalendarHud.Tick();Check(PatchUI_CalendarGems.ValueText=="—","disabled HUD clears gem cache");
ModConfig.ShowCalendarHud.Value=true;Managers.Inst=new();Refresh(10);Check(Has("—")&&!Has("1P 5"),"new world has no stale prior wallet");
Check(Wallet.GemWrites==0,"HUD integration never writes wallet");
Console.WriteLine($"ALL PASS ({checks} HUD integration assertions)");
