using KingdomEnhancedMod;
using UnityEngine;
using Counts=KingdomEnhancedMod.PopulationCounts;

static class Program
{
 static int passed,failed;
 static void Eq<T>(T expected,T actual,string label){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"{label}: expected {expected}, got {actual}");}
 static void Test(string name,Action action)
 {
  Counts.Reset();Managers.ThrowInst=false;Managers.Inst=new();Time.unscaledTime=0;
  NetworkBigBoss.HasWorldAuth=true;
  ModConfig.Enabled.Value=ModConfig.ShowPopulationHud.Value=true;ModConfig.AutoRestockWorkersEnabled.Value=false;
  GUI.Labels.Clear();GUI.ThrowLabel=false;Event.current=new(){type=EventType.Repaint};Screen.width=1280;Screen.height=720;
  try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}
 }
 static Character Actor<T>(Managers m) where T:Component,new()
 {
  var go=new GameObject();go.scene=m.world.gameLayer.gameObject.scene;go.transform.parent=m.world.gameLayer;
  var c=go.AddComponent<Character>();c._damageable=go.AddComponent<Damageable>();go.AddComponent<T>();m.kingdom._characters.Items.Add(c);return c;
 }
 static Character Unknown(Managers m)
 {
  var go=new GameObject();go.scene=m.world.gameLayer.gameObject.scene;go.transform.parent=m.world.gameLayer;
  var c=go.AddComponent<Character>();c._damageable=go.AddComponent<Damageable>();m.kingdom._characters.Items.Add(c);return c;
 }
 static bool Read(float time,bool enabled=true){Time.unscaledTime=time;return Counts.Refresh(Managers.Inst,enabled,time);}
 static void Settle(){Eq(true,Read(0),"first sample");Eq(true,Read(1),"delayed seed one");Eq(true,Read(2),"delayed seed two");}
 static int Total(){int n=Counts.Knights;for(int i=0;i<8;i++)n+=Counts.Role(i);return n;}
 static void Main()
 {
  Test("All eight component professions, unknown NPC exclusion and stale Peasant names",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Actor<Archer>(m);Actor<Farmer>(m);Actor<Pikeman>(m);
   Actor<Ninja>(m).gameObject.GetComponent<Ninja>()._isFisher=true;Actor<Berserker>(m);Actor<Peasant>(m);Actor<Beggar>(m);Unknown(m);
   Eq(true,Read(0),"ready");for(int i=0;i<8;i++)Eq(1,Counts.Role(i),"role "+i);Eq(8,Total(),"no unknown default villager");
  });
  Test("Component precedence is exclusive, including Beggar before Peasant and Knight before Berserker",()=>
  {
   var m=Managers.Inst;
   Actor<Knight>(m).gameObject.AddComponent<Berserker>();Actor<Berserker>(m).gameObject.AddComponent<Ninja>();
   Actor<Ninja>(m).gameObject.AddComponent<Pikeman>();Actor<Pikeman>(m).gameObject.AddComponent<Farmer>();
   Actor<Farmer>(m).gameObject.AddComponent<Archer>();Actor<Archer>(m).gameObject.AddComponent<Worker>();
   Actor<Worker>(m).gameObject.AddComponent<Beggar>();Actor<Beggar>(m).gameObject.AddComponent<Peasant>();Actor<Peasant>(m);
   Read(0);for(int i=0;i<8;i++)Eq(1,Counts.Role(i),"exclusive role "+i);Eq(1,Counts.Knights,"knight priority");Eq(9,Total(),"one role per GO");
  });
  Test("Native Character and GameObject identities both deduplicate roster aliases",()=>
  {
   var m=Managers.Inst;var c=Actor<Worker>(m);m.kingdom._characters.Items.Add(c);
   var sameGo=c.gameObject.AddComponent<Character>();sameGo._damageable=c._damageable;m.kingdom._characters.Items.Add(sameGo);
   var samePointer=Actor<Worker>(m);samePointer.Pointer=c.Pointer;
   Read(0);Eq(1,Counts.Role(0),"deduplicated worker");
  });
  Test("Dead inactive outside-layer and outside-scene entries are excluded",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Actor<Worker>(m)._damageable.isDead=true;
   Actor<Worker>(m).gameObject.activeInHierarchy=false;Actor<Worker>(m).transform.parent=new GameObject().transform;
   Actor<Worker>(m).gameObject.scene=new(){handle=99};Read(0);Eq(1,Counts.Role(0),"only current live actor");
  });
  Test("Cached active death and parent changes update each second without roster enumeration",()=>
  {
   var m=Managers.Inst;var c=Actor<Worker>(m);Settle();int scans=m.kingdom._characters.Enumerations;
   c._damageable.isDead=true;Read(2.1f);Eq(1,Counts.Role(0),"sample interval preserves known count");Read(3);Eq(0,Counts.Role(0),"death observed");
   c._damageable.isDead=false;c.gameObject.activeInHierarchy=false;Read(4);Eq(0,Counts.Role(0),"inactive excluded");
   c.gameObject.activeInHierarchy=true;c.transform.parent=null;Read(5);Eq(0,Counts.Role(0),"detached excluded");
   c.transform.parent=m.world.gameLayer;Read(6);Eq(1,Counts.Role(0),"active current-layer actor returns");Eq(scans,m.kingdom._characters.Enumerations,"no steady roster traversal");
  });
  Test("Damageable component fallback is captured once and not queried in stable samples",()=>
  {
   var m=Managers.Inst;var c=Actor<Worker>(m);c._damageable=null;Settle();int reads=c.gameObject.ComponentReads;
   for(int i=3;i<30;i++)Read(i);Eq(1,Counts.Role(0),"component fallback alive");Eq(reads,c.gameObject.ComponentReads,"no stable component searches");
  });
  Test("All five knight worlds plus unresolved remain separate and late style resolves from cache",()=>
  {
   var m=Managers.Inst;for(int i=0;i<5;i++){var k=Actor<Knight>(m).gameObject.GetComponent<Knight>();k.Style=i;k.Resolved=true;}
   var late=Actor<Knight>(m).gameObject.GetComponent<Knight>();Settle();Eq(6,Counts.Knights,"knight total");Eq(1,Counts.UnknownKnights,"unresolved visible");
   for(int i=0;i<5;i++)Eq(1,Counts.Style(i),"world "+i);int scans=m.kingdom._characters.Enumerations;
   late.Style=3;late.Resolved=true;Read(3);Eq(2,Counts.Style(3),"late Greek style");Eq(0,Counts.UnknownKnights,"unknown cleared");Eq(scans,m.kingdom._characters.Enumerations,"style refresh uses cached Knight");
  });
  Test("Resolved out-of-range knight style is unknown rather than Medieval",()=>
  {
   var k=Actor<Knight>(Managers.Inst).gameObject.GetComponent<Knight>();k.Style=8;k.Resolved=true;Read(0);
   Eq(1,Counts.UnknownKnights,"unknown index");Eq(0,Counts.Style(0),"no Medieval fallback");
  });
  Test("Steady frames perform at most one cached sample per second and never rebuild",()=>
  {
   var m=Managers.Inst;Actor<Archer>(m);Settle();int scans=m.kingdom._characters.Enumerations;long version=Counts.Version;
   for(int i=1;i<50;i++)Read(2+i*.01f);Eq(version,Counts.Version,"no subsecond sample");
   for(int i=3;i<80;i++)Read(i);Eq(scans,m.kingdom._characters.Enumerations,"bounded startup scans only");Eq(3,scans,"initial plus two delayed seeds");
  });
  Test("Dirty roster updates add remove and pooled reclassification without restock enabled",()=>
  {
   var m=Managers.Inst;var worker=Actor<Worker>(m);Settle();m.kingdom._characters.Items.Remove(worker);Actor<Farmer>(m);
   Counts.NotifyRosterChanged();Counts.NotifyRosterChanged();Read(3);Eq(0,Counts.Role(0),"removed worker");Eq(1,Counts.Role(2),"added farmer");Eq(false,ModConfig.AutoRestockWorkersEnabled.Value,"restock stays disabled");
   m.kingdom._characters.Items.Add(worker);worker.gameObject.AddComponent<Ninja>();Counts.NotifyRosterChanged();Read(4);
   Eq(1,Counts.Role(4),"same pooled identity reclassified");Eq(0,Counts.Role(0),"no stale worker count");
  });
  Test("Late components after AddCharacter are picked up by bounded delayed seeds",()=>
  {
   var m=Managers.Inst;var c=Unknown(m);Read(0);Eq(0,Total(),"unknown initially omitted");
   c.gameObject.AddComponent<Worker>();Read(1);Eq(1,Counts.Role(0),"late worker recognized");
   Read(2);int scans=m.kingdom._characters.Enumerations;for(int i=3;i<20;i++)Read(i);Eq(scans,m.kingdom._characters.Enumerations,"delayed retries stop");
  });
  Test("Dirty roster preserves last complete snapshot until next sample without HUD flicker",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Settle();int scans=m.kingdom._characters.Enumerations;
   Actor<Worker>(m);Counts.NotifyRosterChanged();Eq(true,Read(2.5f),"known snapshot stays ready");Eq(1,Counts.Role(0),"previous complete count retained");
   Eq(scans,m.kingdom._characters.Enumerations,"dirty waits for next second");Read(3);Eq(2,Counts.Role(0),"next sample publishes fresh count");
  });
  Test("Late parent attachment is recognized from the cached Entry",()=>
  {
   var m=Managers.Inst;var c=Actor<Archer>(m);c.transform.parent=null;Settle();Eq(0,Counts.Role(1),"not attached yet");
   int scans=m.kingdom._characters.Enumerations;c.transform.parent=m.world.gameLayer;Read(3);Eq(1,Counts.Role(1),"late parent admitted");Eq(scans,m.kingdom._characters.Enumerations,"no reseed needed");
  });
  Test("Missing live damageable hides data and retries to recover",()=>
  {
   var m=Managers.Inst;var c=Unknown(m);c.gameObject.AddComponent<Worker>();c._damageable=null;
   // The normal helper added a damageable fallback; create a distinct incomplete actor instead.
   m.kingdom._characters.Items.Clear();var go=new GameObject();go.transform.parent=m.world.gameLayer;
   var incomplete=go.AddComponent<Character>();go.AddComponent<Worker>();m.kingdom._characters.Items.Add(incomplete);
   Eq(false,Read(0),"missing liveness hides overlay");Eq(false,Counts.Ready,"not a trustworthy zero");
   incomplete._damageable=go.AddComponent<Damageable>();Eq(false,Read(.5f),"one second backoff");Eq(true,Read(1),"retry recovered");Eq(1,Counts.Role(0),"recovered worker");
  });
  Test("Roster fault retries are bounded and a new event unlocks recovery",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);m.kingdom._characters.ThrowEnumeration=true;
   Eq(false,Read(0),"first fault");for(int i=1;i<10;i++)Read(i*.05f);Eq(1,m.kingdom._characters.Enumerations,"fault backoff");
   Read(1);Read(2);for(int i=3;i<20;i++)Read(i);Eq(3,m.kingdom._characters.Enumerations,"three attempts then latch");Eq(false,Counts.Ready,"hidden after persistent fault");
   m.kingdom._characters.ThrowEnumeration=false;Counts.NotifyRosterChanged();Eq(true,Read(20),"event recovers");Eq(1,Counts.Role(0),"fresh trustworthy count");
  });
  Test("Partial roster exceptions do not publish partial totals",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Actor<Archer>(m);m.kingdom._characters.ThrowAfter=1;
   Eq(false,Read(0),"partial enumeration rejected");Eq(false,Counts.Ready,"no partial publication");
   m.kingdom._characters.ThrowAfter=-1;Read(1);Eq(2,Total(),"complete retry publishes both");
  });
  Test("Cached native identity changes cannot keep an old actor counted",()=>
  {
   var m=Managers.Inst;var c=Actor<Worker>(m);Settle();c.Pointer=(IntPtr)987654;Read(3);Eq(0,Counts.Role(0),"changed wrapper identity rejected");
   Counts.NotifyRosterChanged();Read(4);Eq(1,Counts.Role(0),"explicit roster event admits new identity");
  });
  Test("New scene handle and new layer immediately replace old island population",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Settle();m.world.gameLayer.gameObject.scene=new(){handle=88};
   Eq(true,Read(2.1f),"scene switch bypasses old sample timer");Eq(0,Counts.Role(0),"old scene actors excluded");
   m.world.gameLayer=new GameObject().transform;Actor<Farmer>(m);Read(2.2f);Eq(0,Counts.Role(0),"old layer excluded");Eq(1,Counts.Role(2),"new layer actor");
  });
  Test("New kingdom or world identity reseeds before one second expires",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Read(0);m.kingdom=new();Actor<Archer>(m);Read(.1f);Eq(0,Counts.Role(0),"new kingdom drops old roster");Eq(1,Counts.Role(1),"new kingdom roster");
   m.world=new();Actor<Farmer>(m);Read(.2f);Eq(0,Counts.Role(1),"old world layer gone");Eq(1,Counts.Role(2),"new world population");
  });
  Test("Known paused world retains counts while loading and fresh menu clear them",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Read(0);m.game.state=Game.State.Menu;Eq(true,Read(.1f),"pause retains known world");Eq(1,Counts.Role(0),"paused count");
   m.game.state=Game.State.Loading;Eq(false,Read(.2f),"loading clears");Eq(false,Counts.Ready,"loading hidden");m.game.state=Game.State.Menu;Eq(false,Read(.3f),"fresh menu cannot adopt stale world");
   m.game.state=Game.State.Playing;Eq(true,Read(.4f),"playing resumes");Eq(1,Counts.Role(0),"host count");
  });
  Test("Client empty native roster is explicitly unavailable instead of a ready zero snapshot",()=>
  {
   var m=Managers.Inst;m.game.state=Game.State.NetworkClientPlaying;NetworkBigBoss.HasWorldAuth=false;
   Eq(false,Read(0),"client never ready");Eq(true,Counts.ClientUnavailable,"client limitation exposed");Eq(0,m.kingdom._characters.Enumerations,"client roster never used");
   for(int i=1;i<10;i++)Read(i);Eq(0,m.kingdom._characters.Enumerations,"no client retries or false zero publication");
   m.game.state=Game.State.Loading;Read(10);Eq(false,Counts.ClientUnavailable,"loading clears notice state");
  });
  Test("Host to client clears cached numbers and regained authority immediately reseeds",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Read(0);Eq(1,Counts.Role(0),"host worker");
   NetworkBigBoss.HasWorldAuth=false;m.game.state=Game.State.NetworkClientPlaying;Read(.1f);
   Eq(false,Counts.Ready,"old host snapshot cleared immediately");Eq(true,Counts.ClientUnavailable,"notice replaces host numbers");Eq(0,Counts.Role(0),"old worker number inaccessible");Eq(0,Counts.Knights,"no stale knight totals");
   m.kingdom._characters.Items.Clear();Actor<Farmer>(m);NetworkBigBoss.HasWorldAuth=true;m.game.state=Game.State.Playing;
   Eq(true,Read(.2f),"host regain bypasses old sample timer");Eq(false,Counts.ClientUnavailable,"notice removed");Eq(1,Counts.Role(2),"fresh host roster seeded");Eq(0,Counts.Role(0),"no old worker");
  });
  Test("Disable releases roster and re-enable rebuilds without consulting restock",()=>
  {
   var m=Managers.Inst;Actor<Worker>(m);Read(0);Eq(false,Read(.1f,false),"disabled");Eq(false,Counts.Ready,"hidden");m.kingdom._characters.Items.Clear();Actor<Ninja>(m);
   Eq(true,Read(.2f),"re-enabled");Eq(0,Counts.Role(0),"old count gone");Eq(1,Counts.Role(4),"new count shown");
  });
  Test("HUD Draw uses cached text, inherits font chain, and only paints on Repaint",()=>
  {
   Actor<Worker>(Managers.Inst);PopulationHud.Tick();int scans=Managers.Inst.kingdom._characters.Enumerations;
   Event.current.type=EventType.Layout;PopulationHud.Draw();Eq(0,GUI.Labels.Count,"layout does not draw");Event.current.type=EventType.MouseDown;PopulationHud.Draw();Eq(0,GUI.Labels.Count,"input does not draw");
   Event.current.type=EventType.Repaint;PopulationHud.Draw();Eq(30,GUI.Labels.Count,"15 labels with shadows, unknown omitted");
   Eq("本岛人数",GUI.Labels[1].Text,"scope title");Eq(54f,GUI.Labels[1].Rect.y,"title above roster");
   Eq("工匠  1",GUI.Labels[3].Text,"cached worker label");Eq(16f,GUI.Labels[3].Rect.x,"left position");Eq(76f,GUI.Labels[3].Rect.y,"top position");
   Eq(GUI.skin.label.FontChain,GUI.Labels[1].Style.FontChain,"native CJK font chain inherited");Eq(15,GUI.Labels[1].Style.fontSize,"font size");Eq(scans,Managers.Inst.kingdom._characters.Enumerations,"Draw never enumerates");
  });
  Test("HUD unknown knight fills only spare cell and independent display switch hides it",()=>
  {
   Actor<Knight>(Managers.Inst);PopulationHud.Tick();PopulationHud.Draw();Eq(32,GUI.Labels.Count,"unknown extra label");Eq("待识别  1",GUI.Labels[^1].Text,"unknown text");
   GUI.Labels.Clear();ModConfig.ShowPopulationHud.Value=false;PopulationHud.Tick();PopulationHud.Draw();Eq(0,GUI.Labels.Count,"disabled HUD hidden");Eq(false,Counts.Ready,"disabled counts clear");
  });
  Test("Client HUD paints only title and unavailable notice and clears notice on disable",()=>
  {
   Managers.Inst.game.state=Game.State.NetworkClientPlaying;NetworkBigBoss.HasWorldAuth=false;
   PopulationHud.Tick();PopulationHud.Draw();Eq(4,GUI.Labels.Count,"two text labels plus shadows only");Eq("本岛人数",GUI.Labels[1].Text,"client scope title");
   Eq("联机客机人数暂不可用",GUI.Labels[3].Text,"client limitation text");Eq(330f,GUI.Labels[3].Rect.width,"notice has full width");
   GUI.Labels.Clear();ModConfig.ShowPopulationHud.Value=false;PopulationHud.Tick();PopulationHud.Draw();Eq(0,GUI.Labels.Count,"disabled notice hidden");Eq(false,Counts.ClientUnavailable,"disabled state cleared");
  });
  Test("Disabling HUD or whole mod resets cache without touching Managers singleton",()=>
  {
   foreach(bool wholeMod in new[]{false,true})
   {
    Managers.ThrowInst=false;ModConfig.Enabled.Value=ModConfig.ShowPopulationHud.Value=true;Actor<Worker>(Managers.Inst);PopulationHud.Tick();Eq(true,Counts.Ready,"enabled HUD ready");
    if(wholeMod)ModConfig.Enabled.Value=false;else ModConfig.ShowPopulationHud.Value=false;
    Managers.ThrowInst=true;int reads=Managers.InstReads;PopulationHud.Tick();Eq(reads,Managers.InstReads,"disabled does not read singleton");Eq(false,Counts.Ready,"disabled clears known roster");
   }
   Managers.ThrowInst=false;
  });
  Test("HUD global GUI state restores on successful and failing draw",()=>
  {
   Actor<Worker>(Managers.Inst);PopulationHud.Tick();
   var skin=GUI.skin;var color=new Color(.1f,.2f,.3f,.4f);var content=new Color(.3f,.4f,.5f,.6f);var background=new Color(.6f,.7f,.8f,.9f);var matrix=new Matrix4x4{sx=7,sy=8};
   foreach(bool fail in new[]{false,true})
   {
    GUI.color=color;GUI.contentColor=content;GUI.backgroundColor=background;GUI.matrix=matrix;GUI.enabled=false;GUI.changed=true;GUI.depth=123;GUI.ThrowLabel=fail;
    PopulationHud.Draw();Eq(skin,GUI.skin,"skin restored");Eq(color,GUI.color,"color restored");Eq(content,GUI.contentColor,"content restored");Eq(background,GUI.backgroundColor,"background restored");Eq(matrix,GUI.matrix,"matrix restored");Eq(false,GUI.enabled,"enabled restored");Eq(true,GUI.changed,"changed restored");Eq(123,GUI.depth,"depth restored");
   }
  });
  Test("HUD scales within narrow screen bounds",()=>
  {
   Time.unscaledTime=100;Actor<Worker>(Managers.Inst);PopulationHud.Tick();Screen.width=160;Screen.height=100;PopulationHud.Draw();Eq(true,GUI.Labels.Count>0,"narrow layout drawn");
   foreach(var label in GUI.Labels){Eq(true,label.Rect.x*label.Matrix.sx>=0,"left inside screen");Eq(true,(label.Rect.x+label.Rect.width)*label.Matrix.sx<=Screen.width,"right inside screen");}
  });
  Test("HUD singleton faults back off and disable still resets immediately during retry delay",()=>
  {
   Actor<Worker>(Managers.Inst);PopulationHud.Tick();Eq(true,Counts.Ready,"initial cache");
   Managers.ThrowInst=true;PopulationHud.Tick();int reads=Managers.InstReads;
   Time.unscaledTime=.5f;PopulationHud.Tick();Eq(reads,Managers.InstReads,"no subsecond singleton retries");
   ModConfig.ShowPopulationHud.Value=false;PopulationHud.Tick();Eq(false,Counts.Ready,"disable overrides backoff and clears cache");Eq(reads,Managers.InstReads,"no singleton access on disable");
   Managers.ThrowInst=false;
  });
  Console.WriteLine($"RESULT: {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
