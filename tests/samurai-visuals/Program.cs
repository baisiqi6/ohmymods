using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
internal static class Program
{
 static int passed,failed;static readonly List<Knight> Knights=new();
 static readonly Type Production=typeof(SamuraiDashVisuals);static readonly BindingFlags StaticFlags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
 static MethodInfo Method(string n)=>Production.GetMethods(StaticFlags).Single(m=>m.Name==n);
 static object Begin(Knight k)=>Method("Begin").Invoke(null,new object[]{k,null});
 static void End(object token,bool immediate=false)=>Method("End").Invoke(null,new object[]{token,immediate});
 static void Clear(Knight k)=>Method("Clear").Invoke(null,new object[]{k});
 static void Check(bool b,string why){if(!b)throw new Exception(why);}static void Near(float expected,float actual,string why,float tolerance=.015f){if(MathF.Abs(expected-actual)>tolerance)throw new Exception($"{why}: expected {expected}, got {actual}");}
 static void Test(string n,Action body)
 {
  foreach(var k in Knights.ToArray())try{Clear(k);}catch{}Knights.Clear();
  foreach(var go in GameObject.All.ToArray())UnityEngine.Object.Destroy(go);GameObject.All.Clear();
  Time.time=0;Time.deltaTime=0;Time.timeScale=1;ModConfig.Enabled.Value=true;
  GameObject.ThrowAtSpriteAdd=-1;
  UnityEngine.Object.Forbidden=0;Renderer.StringSortingWrites=Renderer.MaterialReads=0;Material.PropertyWrites=0;
  KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
  try{body();Check(UnityEngine.Object.Forbidden==0,"no scene scans/prefab clones");Check(Renderer.MaterialReads==0,"no implicit material getter");Check(Renderer.StringSortingWrites==0,"no unsafe sorting-layer string calls");Check(Material.PropertyWrites<=8,"writes only to own instanced overlay materials (2026-09-24 visibility fix)");passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.GetBaseException().Message);}
 }
 static (Knight,SpriteRenderer) MakeKnight()
 {
  var go=new GameObject("knight");var k=go.AddComponent<Knight>();k._character=go.AddComponent<Character>();k._damageable=go.AddComponent<Damageable>();
  var sprite=go.AddComponent<SpriteRenderer>();sprite.sprite=new Sprite();sprite.sharedMaterial=new Material();sprite.sortingLayerID=37;sprite.sortingOrder=12;
  var fx=go.AddComponent<SpriteRendererFX>();fx._renderer=sprite;k._character.spriteFX=fx;Knights.Add(k);return(k,sprite);
 }
 static SpriteRenderer[] Renderers(SpriteRenderer source,bool visible=false)=>GameObject.All.Where(g=>!g.Destroyed).SelectMany(g=>g.Components).OfType<SpriteRenderer>().Where(r=>!ReferenceEquals(r,source)&&!r.Destroyed&&(!visible||(r.enabled&&r.gameObject.activeInHierarchy&&r.color.a>0))).ToArray();
 static SpriteRenderer[] Ghosts(SpriteRenderer source)=>Renderers(source,true).Where(r=>r.color.a<=.451f).ToArray();
 static SpriteRenderer[] Bodies(SpriteRenderer source)=>Renderers(source,true).Where(r=>r.color.a>.451f).ToArray();
 static void Tick(float dt)
 {
  Time.deltaTime=dt;Time.time+=dt;Time.frameCount++;
  foreach(var b in GameObject.All.ToArray().Where(g=>g.activeInHierarchy).SelectMany(g=>g.Components.ToArray()).OfType<MonoBehaviour>().Where(b=>b.enabled&&!b.Destroyed))
   b.GetType().GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)?.Invoke(b,null);
 }
 static void Main()
 {
  Test("One dash allocates at most three ghosts and one body overlay",()=>{var(k,s)=MakeKnight();var token=Begin(k);Check(token!=null,"visual token allocated");Tick(0);Check(Renderers(s).Length<=4,"at most four visual renderers");Check(Ghosts(s).Length>=1,"initial pose ghost visible");Check(Bodies(s).Length==1,"body flash visible");});
  Test("Silhouettes write white into their own instanced material without source mutation",()=>{var(k,s)=MakeKnight();var original=s.sharedMaterial;s.color=new Color(.3f,.4f,.5f,.8f);var block=new MaterialPropertyBlock();int key=Shader.PropertyToID("_Overlay");block.SetColor(key,new Color(.2f,.1f,.4f));s.SetPropertyBlock(block);Begin(k);Tick(0);foreach(var r in Renderers(s,true)){var m=r.sharedMaterial;Check(m!=null&&m.Colors.TryGetValue(key,out var c)&&c==Color.white,"own instanced material carries the white overlay (2026-09-24 visibility fix: property blocks are inert on PowerSprite2)");Check(!ReferenceEquals(original,m),"ghost owns its material instance");}Check(ReferenceEquals(original,s.sharedMaterial),"source material retained");Check(s.Block[key]==new Color(.2f,.1f,.4f),"source property block retained");Near(.8f,s.color.a,"source alpha retained");});
  Test("Moving stream reuses three ghosts with recency opacity and linear fade",()=>{var(k,s)=MakeKnight();Begin(k);for(int i=1;i<=3;i++){k.transform.position=new(i);Tick(.05f);}var ghosts=Ghosts(s).OrderByDescending(r=>r.color.a).ToArray();Check(ghosts.Length==3,"three bounded living ghosts");Near(.45f,ghosts[0].color.a,"newest alpha");Near(.2375f,ghosts[1].color.a,"middle alpha after .05s");Near(.09f,ghosts[2].color.a,"oldest alpha after .10s");int objects=GameObject.All.Count,materials=Material.Created;for(int i=4;i<=60;i++){k.transform.position=new(i);Tick(.04f);}Check(GameObject.All.Count==objects,"no per-frame game object creation");Check(Material.Created==materials,"no per-frame material creation");Check(Renderers(s).Length<=4,"renderer cap retained");});
  Test("Ghost world pose freezes while owner moves and source pose changes",()=>{var(k,s)=MakeKnight();s.flipX=true;s.flipY=true;k.transform.localScale=new(-1,1.2f,1);s.transform.localPosition=new(.3f,.7f,0);s.transform.localRotation=Quaternion.Euler(0,0,12);Begin(k);Tick(0);var old=Ghosts(s).Single();var oldPosition=old.transform.position;var oldScale=old.transform.lossyScale;k.transform.position=new(5);Tick(.05f);Near(oldPosition.x,old.transform.position.x,"old ghost world x");Near(oldPosition.y,old.transform.position.y,"old ghost world y");Near(oldScale.x,old.transform.lossyScale.x,"signed x scale copied");Check(old.flipX&&old.flipY,"sprite flip copied");Check(old.sortingLayerID==37,"numeric sorting layer copied");Near(-1,k.transform.localScale.x,"owner direction untouched");});
 Test("End stops body immediately and ghosts fade out within one second",()=>{var(k,s)=MakeKnight();var token=Begin(k);Tick(0);End(token);Check(Bodies(s).Length==0,"body flash ended synchronously");Check(Ghosts(s).Length>0,"ghost tail retained");Tick(.9f);Check(Ghosts(s).Length>0,"t+0.9 still bright");Tick(.16f);Check(Renderers(s,true).Length==0,"tail expired by t+1.06");});
 Test("Stationary dash does not emit fake repeated poses",()=>{var(k,s)=MakeKnight();var token=Begin(k);Tick(0);for(int i=0;i<10;i++)Tick(.03f);Check(Ghosts(s).Length==1,"initial stationary pose fades without replacement");Check(Bodies(s).Length==1,"actual burst body remains");Tick(.6f);Check(Ghosts(s).Length==1,"t+0.9 still bright");Check(Bodies(s).Length==1,"body stays on while emitting");Tick(.16f);Check(Ghosts(s).Length==0,"the pose expires by t+1.06");End(token);});
  Test("Pause freezes ghost age and creates no emissions",()=>{var(k,s)=MakeKnight();Begin(k);Tick(0);var ghost=Ghosts(s).Single();float alpha=ghost.color.a;Time.timeScale=0;for(int i=0;i<100;i++)Tick(0);Near(alpha,ghost.color.a,"scaled lifetime frozen");Check(Ghosts(s).Length==1,"no paused emissions");});
  Test("Clear removes all effects immediately while paused",()=>{var(k,s)=MakeKnight();Begin(k);Tick(0);Time.timeScale=0;Clear(k);Check(Renderers(s,true).Length==0,"pause cannot delay disable cleanup");});
  Test("Late old token End cannot clear replacement dash",()=>{var(k,s)=MakeKnight();var old=Begin(k);End(old);var current=Begin(k);Tick(0);End(old,true);Check(Bodies(s).Length==1,"replacement body still active");End(current,true);Check(Renderers(s,true).Length==0,"current token clears itself");});
  Test("Configuration or style loss cleans active renderers",()=>{var(k,s)=MakeKnight();Begin(k);Tick(0);ModConfig.Enabled.Value=false;Tick(0);Check(Renderers(s,true).Length==0,"config loss cleanup");ModConfig.Enabled.Value=true;Begin(k);Tick(0);k.Style=3;Tick(0);Check(Renderers(s,true).Length==0,"style loss cleanup");});
  Test("Owner disable cleans roots and pooled Begin does not duplicate renderers",()=>{var(k,s)=MakeKnight();var old=Begin(k);Tick(0);k.gameObject.SetActive(false);Clear(k);k.gameObject.SetActive(true);Begin(k);Tick(0);End(old,true);Check(Renderers(s).Length<=4,"pooled owner has bounded visuals");Check(Bodies(s).Length==1,"new pooled burst survives old token");});
  Test("Partial renderer construction failure cleans its owned root atomically",()=>{var(k,s)=MakeKnight();GameObject.ThrowAtSpriteAdd=GameObject.SpriteRendererAdds+2;var token=Begin(k);Check(token==null,"failed begin returns no token");Check(Renderers(s).Length==0,"created partial ghost renderer removed");Check(!GameObject.All.Any(g=>!g.Destroyed&&g.name=="KEM_SamuraiAfterimages"),"partial scene root removed");});
  Test("Diagnostic chain records enabled state, first update once, stop and final tail",()=>{
   var(k,s)=MakeKnight();Time.time=100;k._trail=k.gameObject.AddComponent<TrailRenderer>();k._trail.enabled=true;k._trail.emitting=true;k._trail.positionCount=4;
   var trace=SamuraiDashDiagnostics.Begin(k,false,Time.time);var token=SamuraiDashVisuals.Begin(k,trace);
   Check(token!=null,"real visual begin accepted");Tick(.01f);Tick(.01f);
   var lines=KingdomEnhancedPlugin.Instance.LogSource.Lines;
   Check(lines.Any(x=>x.Contains("event=visual-ready")&&x.Contains("whiteEnabled=True")),"white enabled evidence");
   Check(lines.Count(x=>x.Contains("event=visual-first-update"))==1,"first update logged once");
   Check(lines.Any(x=>x.Contains("trailPoints=4")),"native trail observation");
   End(token);Tick(1.06f);
   Check(lines.Any(x=>x.Contains("event=visual-stop")&&x.Contains("whiteEnabled=False")),"white stop evidence");
   Check(lines.Any(x=>x.Contains("event=tail-cleared")&&x.Contains("enabledGhosts=0")),"tail gone evidence");
  });
  Test("Diagnostic skipped source distinguishes missing sprite",()=>{
   var(k,s)=MakeKnight();s.sprite=null;Time.time=100;
   var trace=SamuraiDashDiagnostics.Begin(k,false,Time.time);
   Check(SamuraiDashVisuals.Begin(k,trace)==null,"missing sprite skipped");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Any(x=>x.Contains("reason=missing-sprite")),"precise skip reason");
  });
  Test("Stationary dash whose ghosts expired before End still logs tail-cleared once",()=>{
   var(k,s)=MakeKnight();Time.time=200;
   var trace=SamuraiDashDiagnostics.Begin(k,false,Time.time);var token=SamuraiDashVisuals.Begin(k,trace);
   Tick(1.1f);Check(Ghosts(s).Length==0,"stationary ghosts already expired");End(token);Tick(0);Tick(0);
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Count(x=>x.Contains("event=tail-cleared"))==1,"idle tail completion logged exactly once");
  });
  Console.WriteLine($"RESULT {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
