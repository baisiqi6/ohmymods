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
  try{Method("DestroyWhiteCache").Invoke(null,null);}catch{}   // 白剪影缓存每用例清空：计数/所有权断言不跨用例漂移
  try{Production.GetField("RetryAt",StaticFlags).SetValue(null,0f);}catch{}   // 清 5s 退避：用例顺序无关
  try{((HashSet<string>)Production.GetField("Logged",StaticFlags).GetValue(null)).Clear();}catch{}   // 一次性日志去重按用例隔离
  Time.time=0;Time.deltaTime=0;Time.timeScale=1;ModConfig.Enabled.Value=true;
  GameObject.ThrowAtSpriteAdd=-1;Shader.FailNames.Clear();
  UnityEngine.Object.Forbidden=0;Renderer.StringSortingWrites=Renderer.MaterialReads=0;Material.PropertyWrites=0;
  Texture2D.ResetCounters();RenderTexture.ResetCounters();Sprite.ResetCounters();Graphics.ResetCounters();
  KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
  try{body();Check(UnityEngine.Object.Forbidden==0,"no scene scans/prefab clones");Check(Renderer.MaterialReads==0,"no implicit material getter");Check(Renderer.StringSortingWrites==0,"no unsafe sorting-layer string calls");Check(Material.PropertyWrites<=8,"writes only to own instanced overlay materials (2026-09-24 visibility fix)");passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.GetBaseException().Message);}
 }
 static (Knight,SpriteRenderer) MakeKnight(Sprite pose=null)
 {
  var go=new GameObject("knight");var k=go.AddComponent<Knight>();k._character=go.AddComponent<Character>();k._damageable=go.AddComponent<Damageable>();
  var sprite=go.AddComponent<SpriteRenderer>();sprite.sprite=pose??new Sprite();sprite.sharedMaterial=new Material();sprite.sortingLayerID=37;sprite.sortingOrder=12;
  var fx=go.AddComponent<SpriteRendererFX>();fx._renderer=sprite;k._character.spriteFX=fx;Knights.Add(k);return(k,sprite);
 }
 // ---- 白剪影夹具 ----------------------------------------------------------------
 static bool Rgba(Color32 p,byte r,byte g,byte b,byte a)=>p.r==r&&p.g==g&&p.b==b&&p.a==a;
 static bool IsWhite(Color32 p,byte a)=>Rgba(p,255,255,255,a);
 // 矩形 quad（pivot 原点局部单位 ×PPU），uv 指向 atlas 的 [uvMin,uvMax] 区域：
 // golden/位移/旋转/键碰撞共用。
 static Sprite RectMeshSprite(Texture2D atlas,Rect rect,Vector2 pivotPixels,float ppu,Vector2 uvMin,Vector2 uvMax,bool packed=false)
 {
  var sprite=new Sprite{name="sprite",texture=atlas,rect=rect,pivot=pivotPixels,pixelsPerUnit=ppu,packed=packed};
  float lx=-pivotPixels.x/ppu,ly=-pivotPixels.y/ppu,ux=(rect.width-pivotPixels.x)/ppu,uy=(rect.height-pivotPixels.y)/ppu;
  sprite.vertices=new[]{new Vector2(lx,ly),new Vector2(ux,ly),new Vector2(ux,uy),new Vector2(lx,uy)};
  sprite.uv=new[]{new Vector2(uvMin.x,uvMin.y),new Vector2(uvMax.x,uvMin.y),new Vector2(uvMax.x,uvMax.y),new Vector2(uvMin.x,uvMax.y)};
  sprite.triangles=new ushort[]{0,1,2,0,2,3};
  sprite.textureRect=new Rect(uvMin.x*atlas.width,uvMin.y*atlas.height,(uvMax.x-uvMin.x)*atlas.width,(uvMax.y-uvMin.y)*atlas.height);
  return sprite;
 }
 // rect 8x4：rect 像素(x,y) → 图集(2+y, 10-x)，即 90° 旋转打包（图集竖条 → 输出横条）。
 static Sprite RotatedSprite(Texture2D atlas)
 {
  var sprite=new Sprite{name="rotated",texture=atlas,rect=new Rect(0,0,8,4),pivot=new Vector2(0,0),pixelsPerUnit=8f,packed=true};
  sprite.vertices=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,.5f),new Vector2(0,.5f)};
  sprite.uv=new[]{new Vector2(2f/16f,10f/16f),new Vector2(2f/16f,2f/16f),new Vector2(6f/16f,2f/16f),new Vector2(6f/16f,10f/16f)};
  sprite.triangles=new ushort[]{0,1,2,0,2,3};
  return sprite;
 }
 static Vector2 VertexPixel(Sprite sprite,int index)=>new(sprite.vertices[index].x*sprite.pixelsPerUnit+sprite.pivot.x,sprite.vertices[index].y*sprite.pixelsPerUnit+sprite.pivot.y);
 // 测试侧独立几何分类：到网格最近边的有符号像素余量（>0 在内侧）。
 static float MeshMargin(Sprite sprite,float sx,float sy)
 {
  float best=float.NegativeInfinity;
  for(int t=0;t+2<sprite.triangles.Length;t+=3)
  {
   var a=VertexPixel(sprite,sprite.triangles[t]);var b=VertexPixel(sprite,sprite.triangles[t+1]);var c=VertexPixel(sprite,sprite.triangles[t+2]);
   float area=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
   if(MathF.Abs(area)<.0001f)continue;
   float w0=((b.x-sx)*(c.y-sy)-(b.y-sy)*(c.x-sx))/area;
   float w1=((c.x-sx)*(a.y-sy)-(c.y-sy)*(a.x-sx))/area;
   float w2=1f-w0-w1;
   float edge=2f*MathF.Abs(area)/(MathF.Abs(b.x-a.x)+MathF.Abs(b.y-a.y)+MathF.Abs(c.x-b.x)+MathF.Abs(c.y-b.y)+MathF.Abs(a.x-c.x)+MathF.Abs(a.y-c.y)+1e-3f);
   float margin=MathF.Min(w0,MathF.Min(w1,w2))*edge;
   if(margin>best)best=margin;
  }
  return best;
 }
 // 真实 2.4 夹具（native-charge-fixture.json，sharedassets0 path_id10631）：vertices/triangles
 // 与 alpha tile 为串行化实数据；uv 标记为 uvInferred——由 serialized atlas.uvTransform 推导，
 // 非 runtime 捕获。
 static readonly string[] NativeAlphaHex={
  "00000000000000ff00000000000000000000ff00000000000000000000000000000000000000000000",
  "00000000000000ff0000000000ffffff00ffff00000000000000000000000000000000000000000000",
  "00000000000000ffffff000000ffffffffffff00000000000000000000000000000000000000000000",
  "00000000000000ffffffffff00ffffffffffff00000000000000000000000000000000000000000000",
  "000000000000ffffffffffffffffffffffffff00000000000000000000000000000000000000000000",
  "000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000",
  "00000000000000ffffffffffffffffffffff0000000000000000000000000000000000000000000000",
  "ffffffffffff0000ffffffffffffffffff000000000000000000000000000000000000000000000000",
  "00000000ffffffffffffffffffffffffff000000000000000000000000000000000000000000000000",
  "0000000000000000d0ffffffffffffffffff0000000000000000000000000000000000000000000000",
  "00000000000000d0d0ffffffffffffffffff0000000000000000000000000000000000000000000000",
  "00000000000000ffffffffffffffffffffffd0d0000000000000000000000000000000000000000000",
  "00000000000000ffffffffffffffffffffffffd0d0d000000000000000000000000000000000000000",
  "0000000000000000ffffffffffffffffffffffffd0d0ffffffffff0000000000000000000000000000",
  "000000000000000000ffffffffffffffffff00000000ffffffffffffffffff00000000000000000000",
  "00000000000000000000ffffffffffffffff00000000000000ffff00000000ffffffffffff00000000",
  "00000000000000000000ffffffffffb0b0b00000000000000000000000000000000000000000000000",
  "0000000000000000000000ffffffffffffffffff000000000000000000000000000000000000000000",
  "0000000000000000000000ffffffffffffffff00000000000000000000000000000000000000000000",
  "000000000000000000000000ffffffffffffff00000000000000000000000000000000000000000000",
  "000000000000000000000000ffffffffffffff00000000000000000000000000000000000000000000",
  "00000000000000000000000000ffffffffffff00000000000000000000000000000000000000000000",
  "0000000000000000000000000000ffff00ffff00000000000000000000000000000000000000000000",
  "000000000000000000000000000000ff00ff0000000000000000000000000000000000000000000000",
  "0000000000000000000000000000000000000000000000000000000000000000000000000000000000",
  "0000000000000000000000000000000000000000000000000000000000000000000000000000000000",
  "0000000000000000000000000000000000000000000000000000000000000000000000000000000000",
  "000000000000000000000000000000000000ffff000000000000000000000000000000000000000000",
  "000000000000000000000000000000000000ffff000000000000000000000000000000000000000000",
  "000000000000000000000000000000ffffffffff00ffff000000ff0000000000000000000000000000",
  "00000000000000000000000000ffffffffffffffffffffff0000ff000000000000ffffffffffffff00",
  "000000000000000000000000ffffffd8d8d8d8ffffffffffff00ff00000000000000000000000000ff"};
 static readonly float[] NativeVertices={
  0.42656251788139343f,0.8125f, 0.7703125476837158f,0.25f, 0.7703125476837158f,0.59375f, 0.33281251788139343f,0f,
  -0.22968748211860657f,0.8125f, -0.44843748211860657f,0.34375f, -0.44843748211860657f,0f};
 static readonly float[] NativeUv={
  0.7304687383584678f,0.095703125f, 0.7358398325741291f,0.0869140625f, 0.7358398325741291f,0.09228515625f,
  0.7290038946084678f,0.0830078125f, 0.7202148321084678f,0.095703125f, 0.7167968633584678f,0.08837890625f,
  0.7167968633584678f,0.0830078125f};
 static readonly ushort[] NativeTriangles={6,5,3,4,3,5,0,3,4,1,3,0,2,1,0};
 static byte[] NativeTile()
 {
  var tile=new byte[41*32];
  for(int y=0;y<32;y++)Array.Copy(Convert.FromHexString(NativeAlphaHex[y]),0,tile,y*41,41);
  return tile;
 }
 static Texture2D NativeFixtureAtlas()
 {
  var atlas=new Texture2D(2048,2048,TextureFormat.RGBA32,false);
  var tile=NativeTile();
  for(int y=0;y<32;y++)for(int x=0;x<41;x++)atlas.Pixels[(170+y)*2048+(1468+x)]=new Color32(7,9,11,tile[y*41+x]);
  return atlas;
 }
 static Sprite NativeChargeSprite(Texture2D atlas)
 {
  var sprite=new Sprite{name="knight_charge_bamboo_0",texture=atlas,rect=new Rect(0,0,41,32),
   pivot=new Vector2(14.349999755620956f,0f),pixelsPerUnit=32f,packed=true};
  sprite.vertices=new Vector2[7];sprite.uv=new Vector2[7];
  for(int i=0;i<7;i++){sprite.vertices[i]=new Vector2(NativeVertices[i*2],NativeVertices[i*2+1]);sprite.uv[i]=new Vector2(NativeUv[i*2],NativeUv[i*2+1]);}
  sprite.triangles=(ushort[])NativeTriangles.Clone();
  sprite.textureRect=new Rect(1468f,170f,38.98708724975586f,25.923879623413086f);
  return sprite;
 }
 static System.Collections.IDictionary Cache(string field)=>(System.Collections.IDictionary)Production.GetField(field,StaticFlags).GetValue(null);
 // 多 owner 用例（场景里同时存在其他武士残影）必须按 owner 取渲染器，不能全局扫。
 static SpriteRenderer OwnerGhost(Knight k)
 {
  var owners=(System.Collections.IDictionary)Production.GetField("Owners",StaticFlags).GetValue(null);
  var state=owners[k.gameObject.GetInstanceID()];var flags=BindingFlags.NonPublic|BindingFlags.Instance;
  var ghosts=(Array)state.GetType().GetField("Ghosts",flags).GetValue(state);
  foreach(var ghost in ghosts)
  {
   var r=(SpriteRenderer)ghost.GetType().GetField("Renderer",flags).GetValue(ghost);
   if(r!=null&&r.enabled&&r.color.a>0)return r;
  }
  return null;
 }
 static SpriteRenderer[] Renderers(SpriteRenderer source,bool visible=false)=>GameObject.All.Where(g=>!g.Destroyed).SelectMany(g=>g.Components).OfType<SpriteRenderer>().Where(r=>!ReferenceEquals(r,source)&&!r.Destroyed&&(!visible||(r.enabled&&r.gameObject.activeInHierarchy&&r.color.a>0))).ToArray();
 static SpriteRenderer[] Ghosts(SpriteRenderer source)=>Renderers(source,true).Where(r=>r.gameObject.name!="BurstWhite").ToArray();
 static SpriteRenderer[] Bodies(SpriteRenderer source)=>Renderers(source,true).Where(r=>r.gameObject.name=="BurstWhite").ToArray();
 static void Tick(float dt)
 {
  Time.deltaTime=dt;Time.time+=dt;Time.frameCount++;
  foreach(var b in GameObject.All.ToArray().Where(g=>g.activeInHierarchy).SelectMany(g=>g.Components.ToArray()).OfType<MonoBehaviour>().Where(b=>b.enabled&&!b.Destroyed))
   b.GetType().GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)?.Invoke(b,null);
 }
 static void Main()
 {
  Test("One dash allocates the eight-slot ghost trail and one body overlay",()=>{var(k,s)=MakeKnight();var token=Begin(k);Check(token!=null,"visual token allocated");Tick(0);Check(Renderers(s).Length<=9,"at most nine visual renderers (8 ghosts + body, 2026-09-25 user ruling)");Check(Ghosts(s).Length>=1,"initial pose ghost visible");Check(Bodies(s).Length==1,"body flash visible");});
  Test("Silhouettes write white into their own instanced material without source mutation",()=>{var(k,s)=MakeKnight();var original=s.sharedMaterial;s.color=new Color(.3f,.4f,.5f,.8f);var block=new MaterialPropertyBlock();int key=Shader.PropertyToID("_Overlay");block.SetColor(key,new Color(.2f,.1f,.4f));s.SetPropertyBlock(block);Begin(k);Tick(0);var rs=Renderers(s);foreach(var r in rs){Check(!ReferenceEquals(original,r.sharedMaterial),"ghost owns its material instance");}
// 2026-09-25 用户裁定（八道白光）：全部 8 槽统一 recipe C（标准白配方，已实机验证可见）。
var g0=rs.FirstOrDefault(r=>r.gameObject.name=="Ghost0");
Check(g0!=null,"ghost slot exists");Check(ReferenceEquals(original,s.sharedMaterial),"source material retained");Check(s.Block[key]==new Color(.2f,.1f,.4f),"source property block retained");Near(.8f,s.color.a,"source alpha retained");});
  Test("Moving stream fills the eight-slot trail with recency opacity and linear fade",()=>{var(k,s)=MakeKnight();Begin(k);for(int i=1;i<=8;i++){k.transform.position=new(i);Tick(.05f);}var ghosts=Ghosts(s).OrderByDescending(r=>r.color.a).ToArray();Check(ghosts.Length==8,"eight bounded living ghosts (2026-09-25 user ruling)");Near(.55f,ghosts[0].color.a,"newest alpha");Near(.4753125f,ghosts[1].color.a,"second alpha after .05s");Near(.0928125f,ghosts[7].color.a,"oldest alpha after .05s");int objects=GameObject.All.Count,materials=Material.Created;for(int i=9;i<=60;i++){k.transform.position=new(i);Tick(.04f);}Check(GameObject.All.Count==objects,"no per-frame game object creation");Check(Material.Created==materials,"no per-frame material creation");Check(Renderers(s).Length<=9,"renderer cap retained");});
  Test("Ghost world pose freezes while owner moves and source pose changes",()=>{var(k,s)=MakeKnight();s.flipX=true;s.flipY=true;k.transform.localScale=new(-1,1.2f,1);s.transform.localPosition=new(.3f,.7f,0);s.transform.localRotation=Quaternion.Euler(0,0,12);Begin(k);Tick(0);var old=Ghosts(s).Single();var oldPosition=old.transform.position;var oldScale=old.transform.lossyScale;k.transform.position=new(5);Tick(.05f);Near(oldPosition.x,old.transform.position.x,"old ghost world x");Near(oldPosition.y,old.transform.position.y,"old ghost world y");Near(oldScale.x,old.transform.lossyScale.x,"signed x scale copied");Check(old.flipX&&old.flipY,"sprite flip copied");Check(old.sortingLayerID==37,"numeric sorting layer copied");Near(-1,k.transform.localScale.x,"owner direction untouched");});
 Test("End stops body immediately and ghosts fade out within two seconds",()=>{var(k,s)=MakeKnight();var token=Begin(k);Tick(0);End(token);Check(Bodies(s).Length==0,"body flash ended synchronously");Check(Ghosts(s).Length>0,"ghost tail retained");Tick(1.9f);Check(Ghosts(s).Length>0,"t+1.9 still bright");Tick(.2f);Check(Renderers(s,true).Length==0,"tail expired by t+2.1");});
 Test("Stationary dash does not emit fake repeated poses",()=>{var(k,s)=MakeKnight();var token=Begin(k);Tick(0);for(int i=0;i<10;i++)Tick(.03f);Check(Ghosts(s).Length==1,"initial stationary pose fades without replacement");Check(Bodies(s).Length==1,"actual burst body remains");Tick(.6f);Check(Ghosts(s).Length==1,"t+0.9 still bright");Check(Bodies(s).Length==1,"body stays on while emitting");Tick(1f);Check(Ghosts(s).Length==1,"t+1.9 still bright");Tick(.2f);Check(Ghosts(s).Length==0,"the pose expires by t+2.1");End(token);});
  Test("Pause freezes ghost age and creates no emissions",()=>{var(k,s)=MakeKnight();Begin(k);Tick(0);var ghost=Ghosts(s).Single();float alpha=ghost.color.a;Time.timeScale=0;for(int i=0;i<100;i++)Tick(0);Near(alpha,ghost.color.a,"scaled lifetime frozen");Check(Ghosts(s).Length==1,"no paused emissions");});
  Test("Clear removes all effects immediately while paused",()=>{var(k,s)=MakeKnight();Begin(k);Tick(0);Time.timeScale=0;Clear(k);Check(Renderers(s,true).Length==0,"pause cannot delay disable cleanup");});
  Test("Late old token End cannot clear replacement dash",()=>{var(k,s)=MakeKnight();var old=Begin(k);End(old);var current=Begin(k);Tick(0);End(old,true);Check(Bodies(s).Length==1,"replacement body still active");End(current,true);Check(Renderers(s,true).Length==0,"current token clears itself");});
  Test("Configuration or style loss cleans active renderers",()=>{var(k,s)=MakeKnight();Begin(k);Tick(0);ModConfig.Enabled.Value=false;Tick(0);Check(Renderers(s,true).Length==0,"config loss cleanup");ModConfig.Enabled.Value=true;Begin(k);Tick(0);k.Style=3;Tick(0);Check(Renderers(s,true).Length==0,"style loss cleanup");});
  Test("Owner disable cleans roots and pooled Begin does not duplicate renderers",()=>{var(k,s)=MakeKnight();var old=Begin(k);Tick(0);k.gameObject.SetActive(false);Clear(k);k.gameObject.SetActive(true);Begin(k);Tick(0);End(old,true);Check(Renderers(s).Length<=9,"pooled owner has bounded visuals (8 ghosts + body)");Check(Bodies(s).Length==1,"new pooled burst survives old token");});
  Test("Partial renderer construction failure cleans its owned root atomically",()=>{var(k,s)=MakeKnight();GameObject.ThrowAtSpriteAdd=GameObject.SpriteRendererAdds+2;var token=Begin(k);Check(token==null,"failed begin returns no token");Check(Renderers(s).Length==0,"created partial ghost renderer removed");Check(!GameObject.All.Any(g=>!g.Destroyed&&g.name=="KEM_SamuraiAfterimages"),"partial scene root removed");});
  Test("Diagnostic chain records enabled state, first update once, stop and final tail",()=>{
   var(k,s)=MakeKnight();Time.time=100;k._trail=k.gameObject.AddComponent<TrailRenderer>();k._trail.enabled=true;k._trail.emitting=true;k._trail.positionCount=4;
   var trace=SamuraiDashDiagnostics.Begin(k,false,Time.time);var token=SamuraiDashVisuals.Begin(k,trace);
   Check(token!=null,"real visual begin accepted");Tick(.01f);Tick(.01f);
   var lines=KingdomEnhancedPlugin.Instance.LogSource.Lines;
   Check(lines.Any(x=>x.Contains("event=visual-ready")&&x.Contains("whiteEnabled=True")),"white enabled evidence");
   Check(lines.Count(x=>x.Contains("event=visual-first-update"))==1,"first update logged once");
   Check(lines.Any(x=>x.Contains("trailPoints=4")),"native trail observation");
   End(token);Tick(2.1f);
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
   Tick(2.1f);Check(Ghosts(s).Length==0,"stationary ghosts already expired");End(token);Tick(0);Tick(0);
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Count(x=>x.Contains("event=tail-cleared"))==1,"idle tail completion logged exactly once");
  });
  Test("Both shader finds failing degrades to a source-palette clone with one log line",()=>{
   // 2026-09-25 审查 P1-2：桩模拟 IL2CPP 剥离——Sprites/Default 与 Unlit/Texture 两级
   // Shader.Find 均失败 → 第三级源材质克隆兜底（shader 引用与源一致），九个 renderer
   // 只落一条一次性 source-clone 降级日志，且不误报 unlit-texture 级。
   Shader.FailNames.Add("Sprites/Default");Shader.FailNames.Add("Unlit/Texture");
   var(k,s)=MakeKnight();Time.time=300;var original=s.sharedMaterial;var token=Begin(k);
   Check(token!=null,"third-level fallback still allocates the visual token");
   var rs=Renderers(s);Check(rs.Length==9,"all nine renderers built (8 ghosts + body)");
   foreach(var r in rs)Check(ReferenceEquals(original.shader,r.sharedMaterial.shader),"every ghost material cloned the source palette");
   var lines=KingdomEnhancedPlugin.Instance.LogSource.Lines;
   Check(lines.Count(x=>x.Contains("[SamuraiDashVisuals]")&&x.Contains("level=source-clone"))==1,"source-clone degradation logged exactly once across nine renderers");
   Check(!lines.Any(x=>x.Contains("level=unlit-texture")),"the unlit level is not reported when it was never reached");
  });
  Test("First-level miss only degrades to the unlit-texture recipe with one log line",()=>{
   // 2026-09-25 复审 P2-1：仅 Sprites/Default 被剥离 → 第二级 Unlit/Texture 接管
   // （材质 shader 名即 Unlit/Texture），恰一条 unlit-texture 降级日志，不触第三级。
   Shader.FailNames.Add("Sprites/Default");
   var(k,s)=MakeKnight();Time.time=300;var token=Begin(k);
   Check(token!=null,"second-level fallback still allocates the visual token");
   var rs=Renderers(s);Check(rs.Length==9,"all nine renderers built (8 ghosts + body)");
   foreach(var r in rs)Check(r.sharedMaterial.shader.name=="Unlit/Texture","every ghost material uses the unlit-texture shader");
   var lines=KingdomEnhancedPlugin.Instance.LogSource.Lines;
   Check(lines.Count(x=>x.Contains("[SamuraiDashVisuals]")&&x.Contains("level=unlit-texture"))==1,"unlit-texture degradation logged exactly once across nine renderers");
   Check(!lines.Any(x=>x.Contains("level=source-clone")),"the source-clone level is not reported when it was never reached");
  });
  Test("Non-packed quad bakes a white silhouette pixel-identical to its atlas rect",()=>{
   var atlas=new Texture2D(8,6,TextureFormat.RGBA32,false);
   for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32((byte)(10+i),(byte)(20+i),(byte)(30+i),(byte)(i%2==0?255:64));
   var pose=RectMeshSprite(atlas,new Rect(0,0,8,6),new Vector2(2,1),8f,new Vector2(0,0),new Vector2(1,1));
   var(k,s)=MakeKnight(pose);var sourceMaterial=s.sharedMaterial;
   Begin(k);Tick(0);
   var white=Ghosts(s).Single().sprite;
   Check(!ReferenceEquals(white,pose)&&!ReferenceEquals(white.texture,atlas),"ghost uses a baked sprite on its own texture");
   Check(white.texture.width==8&&white.texture.height==6,"output keeps the rect size");
   Check(white.pivot==pose.pivot&&white.pixelsPerUnit==pose.pixelsPerUnit,"pixel pivot and PPU preserved (foot point)");
   for(int i=0;i<48;i++)Check(IsWhite(white.texture.Pixels[i],atlas.Pixels[i].a),"golden pixel "+i+": RGB 255 with source alpha");
   Check(Rgba(atlas.Pixels[0],10,20,30,255),"source atlas RGBA untouched");
   Check(ReferenceEquals(s.sprite,pose)&&ReferenceEquals(s.sharedMaterial,sourceMaterial),"source renderer sprite/material untouched");
   Check(Texture2D.GetPixelsCalls==1&&Texture2D.SetPixelsCalls==1&&Texture2D.ApplyCalls==1,"one readback/upload/apply per baked pose");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Any(x=>x.Contains("whiten-baked")&&x.Contains("raster=uv-triangles")),"success diagnostic marks the real baked path");
  });
  Test("Native packed charge sprite bakes through real mesh + inferred atlas UVs without neighbor bleed",()=>{
   var atlas=NativeFixtureAtlas();var pose=NativeChargeSprite(atlas);
   var(k,s)=MakeKnight(pose);
   Begin(k);Tick(0);
   var white=Ghosts(s).Single().sprite;
   Check(!ReferenceEquals(white,pose)&&!ReferenceEquals(white.texture,atlas),"packed sprite is baked, never degraded");
   var px=white.texture.Pixels;var tile=NativeTile();
   Check(white.texture.width==41&&white.texture.height==32,"output is the native rect envelope");
   Near(14.35f,white.pivot.x,"native pivot x kept",.001f);Near(0f,white.pivot.y,"native pivot y kept",.001f);
   Check(white.pixelsPerUnit==32f,"native PPU kept");
   // 网格像素包络 == ceil(native texture_rect 38.987x25.924)：裁边量与 tight 打包一致。
   float minX=float.MaxValue,maxX=float.MinValue,minY=float.MaxValue,maxY=float.MinValue;
   for(int i=0;i<pose.vertices.Length;i++){var v=VertexPixel(pose,i);minX=MathF.Min(minX,v.x);maxX=MathF.Max(maxX,v.x);minY=MathF.Min(minY,v.y);maxY=MathF.Max(maxY,v.y);}
   Check(MathF.Abs((maxX-minX)-MathF.Ceiling(38.98708724975586f))<.001f,"mesh width matches the native tight crop");
   Check(MathF.Abs((maxY-minY)-MathF.Ceiling(25.923879623413086f))<.001f,"mesh height matches the native tight crop");
   int inside=0,outside=0;
   for(int y=0;y<32;y++)for(int x=0;x<41;x++){
    float margin=MeshMargin(pose,x+.5f,y+.5f);
    if(margin>=1f){Check(IsWhite(px[y*41+x],tile[y*41+x]),"native alpha kept at "+x+","+y);inside++;}
    else if(margin<=-1f){Check(IsWhite(px[y*41+x],0),"outside-geometry pixel transparent at "+x+","+y);outside++;}
   }
   Check(inside>100&&outside>100,"fixture exercises coverage and contour ("+inside+"/"+outside+")");
   Check(tile[31*41+40]==255&&px[31*41+40].a==0,"neighbor-alpha corner stays out of the silhouette");
   for(int y=0;y<32;y++)for(int x=0;x<41;x++)Check(Rgba(atlas.Pixels[(170+y)*2048+(1468+x)],7,9,11,tile[y*41+x]),"source atlas pixel "+x+","+y+" untouched");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Any(x=>x.Contains("whiten-baked")&&x.Contains("knight_charge_bamboo_0")&&x.Contains("packed=True")),"success diagnostic reports the native packed sprite");
   Check(Cache("WhiteAtlases").Count==1,"one alpha atlas cached");
  });
  Test("Pose changes mint a fresh baked silhouette while the older ghost keeps its frozen pose",()=>{
   var atlas=new Texture2D(8,8,TextureFormat.RGBA32,false);
   for(int x=0;x<8;x++)atlas.Pixels[1*8+x]=new Color32(4,5,6,255);   // A 区（下半）第 1 行实心
   for(int x=0;x<8;x++)atlas.Pixels[6*8+x]=new Color32(4,5,6,255);   // B 区（上半）第 6 行实心
   var first=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0,0),new Vector2(1f,4f/8f));
   var second=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0f,4f/8f),new Vector2(1f,1f));
   var(k,s)=MakeKnight(first);
   Begin(k);Tick(0);
   var held=Ghosts(s).Single();var heldWhite=held.sprite;
   s.sprite=second;
   for(int i=1;i<=4;i++){k.transform.position=new(i*.5f);Tick(.05f);}
   var alive=Ghosts(s).OrderByDescending(r=>r.color.a).ToArray();
   Check(alive.Length==5,"one ghost per sampled pose");
   Check(ReferenceEquals(alive[alive.Length-1].sprite,heldWhite),"old ghost still shows its frozen baked pose");
   Check(!ReferenceEquals(alive[0].sprite,heldWhite),"new pose baked a different silhouette");
   Check(!heldWhite.Destroyed&&!heldWhite.texture.Destroyed,"frozen silhouette stays alive while its ghost lives");
   Check(IsWhite(heldWhite.texture.Pixels[1*8+3],255),"old silhouette keeps its own alpha pattern");
   Check(IsWhite(alive[0].sprite.texture.Pixels[2*8+3],255),"new silhouette samples its own atlas region");
  });
  Test("White bakes are shared across owners and never rebuilt per frame",()=>{
   var atlas=new Texture2D(8,4,TextureFormat.RGBA32,false);for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32(30,40,50,255);
   var pose=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0,0),new Vector2(1,1));
   var(k1,s1)=MakeKnight(pose);var(k2,s2)=MakeKnight(pose);
   Begin(k1);Begin(k2);Tick(0);
   var g1=OwnerGhost(k1).sprite;var g2=OwnerGhost(k2).sprite;
   Check(ReferenceEquals(g1,g2),"the same pose reuses one baked sprite across owners");
   Check(Sprite.Created==1&&Texture2D.GetPixelsCalls==1,"one bake and one atlas readback for both owners");
   Check(Cache("WhiteSprites").Count==1,"cache holds the single shared entry");
   int reads=Texture2D.GetPixelsCalls,mints=Sprite.Created;
   for(int i=1;i<=6;i++){k1.transform.position=new(i);Tick(.05f);}
   Check(Texture2D.GetPixelsCalls==reads&&Sprite.Created==mints,"no per-frame readback or re-mint");
  });
  Test("Baked stream keeps the eight-slot ladder, ordering offsets and one readback",()=>{
   var atlas=new Texture2D(8,4,TextureFormat.RGBA32,false);for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32(30,40,50,255);
   var pose=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0,0),new Vector2(1,1));
   var(k,s)=MakeKnight(pose);
   Begin(k);for(int i=1;i<=8;i++){k.transform.position=new(i);Tick(.05f);}
   var ghosts=Ghosts(s).OrderByDescending(r=>r.color.a).ToArray();
   Check(ghosts.Length==8,"eight bounded white ghosts");
   Near(.55f,ghosts[0].color.a,"newest alpha");
   Near(.0928125f,ghosts[7].color.a,"oldest alpha after .35s");
   foreach(var g in ghosts){Check(g.sortingOrder==14,"ghost order +2 over the source");Check(g.sprite!=null&&!g.sprite.Destroyed&&g.sprite.texture!=null,"baked silhouette alive");Check(g.sharedMaterial.shader.name=="Sprites/Default","standard multiplicative sprite shader kept");}
   Check(Bodies(s).Single().sortingOrder==15,"body order +3");
   Check(Texture2D.GetPixelsCalls==1&&Sprite.Created==1,"one bake for the whole stream");
  });
  Test("Unreadable atlases whiten through the GPU readback path and return RT + staging",()=>{
   var atlas=new Texture2D(6,4,TextureFormat.RGBA32,false);
   for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32(9,9,9,(byte)(i*10));
   atlas.SimulateUnreadable=true;
   var pose=RectMeshSprite(atlas,new Rect(0,0,6,4),new Vector2(1,1),6f,new Vector2(0,0),new Vector2(1,1));
   var sentinel=new RenderTexture(8,8);RenderTexture.active=sentinel;
   var(k,s)=MakeKnight(pose);
   Begin(k);Tick(0);
   var white=Ghosts(s).Single().sprite;
   Check(!ReferenceEquals(white.texture,atlas),"GPU readback produced the silhouette");
   for(int i=0;i<atlas.Pixels.Length;i++)Check(IsWhite(white.texture.Pixels[i],(byte)(i*10)),"alpha kept through the GPU path at "+i);
   Check(RenderTexture.TemporaryGets==1&&RenderTexture.TemporaryReleases==1&&RenderTexture.DestroyedCount==1,"temporary RT taken, returned and destroyed");
   Check(ReferenceEquals(RenderTexture.active,sentinel),"previous active render target restored");
   Check(Graphics.Blits==1,"exactly one blit");
   Check(Texture2D.Created==3&&Texture2D.DestroyedCount==1,"atlas + staging + pose texture created, staging destroyed");
  });
  Test("Failed GPU readback degrades truthfully, returns the RT and destroys the staging texture",()=>{
   var atlas=new Texture2D(6,4,TextureFormat.RGBA32,false);
   for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32(9,9,9,200);
   atlas.SimulateUnreadable=true;
   var pose=RectMeshSprite(atlas,new Rect(0,0,6,4),new Vector2(1,1),6f,new Vector2(0,0),new Vector2(1,1));
   Texture2D.ReadPixelsThrows=true;
   var sentinel=new RenderTexture(8,8);RenderTexture.active=sentinel;
   var(k,s)=MakeKnight(pose);
   Begin(k);Tick(0);
   Check(ReferenceEquals(Ghosts(s).Single().sprite,pose),"fallback keeps the source sprite (never a fake white)");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Any(x=>x.Contains("whiten-degraded")&&x.Contains("atlas-alpha-unavailable")&&x.Contains("NOT white")),"degradation is logged as NOT white");
   Check(RenderTexture.TemporaryGets==1&&RenderTexture.TemporaryReleases==1,"temporary RT returned on the failure path");
   Check(ReferenceEquals(RenderTexture.active,sentinel),"active restored on the failure path");
   Check(Texture2D.Created==2&&Texture2D.DestroyedCount==1,"staging texture destroyed, no orphan");
   Check(Sprite.Created==0,"no white sprite minted on failure");
   Check(Cache("WhiteSprites").Count==1,"negative result cached (no per-frame retry)");
   Check(Rgba(atlas.Pixels[5],9,9,9,200),"source atlas untouched");
   int created=Texture2D.Created,blits=Graphics.Blits;
   Begin(k);Tick(.05f);Tick(.05f);
   Check(Texture2D.Created==created&&Graphics.Blits==blits,"failed pose is not retried every frame");
  });
  Test("Atlas displacement: a known block lands at pivot-relative pixel offsets",()=>{
   var atlas=new Texture2D(64,64,TextureFormat.RGBA32,false);
   for(int y=40;y<45;y++)for(int x=30;x<35;x++)atlas.Pixels[y*64+x]=new Color32(200,10,10,255);
   var pose=RectMeshSprite(atlas,new Rect(0,0,10,10),new Vector2(3,4),10f,new Vector2(30f/64f,40f/64f),new Vector2(40f/64f,50f/64f));
   var(k,s)=MakeKnight(pose);Begin(k);Tick(0);
   var px=Ghosts(s).Single().sprite.texture.Pixels;
   Check(IsWhite(px[0*10+0],255)&&IsWhite(px[4*10+4],255)&&IsWhite(px[3*10+2],255),"block lands at rect pixels 0..4 on both axes (no y flip)");
   Check(IsWhite(px[5*10+5],0)&&IsWhite(px[9*10+9],0),"beyond the block stays transparent");
   Check(IsWhite(px[0*10+5],0)&&IsWhite(px[5*10+0],0),"row/column beyond the block transparent");
  });
  Test("Rotated packing: UV-driven bake still yields an upright silhouette",()=>{
   var atlas=new Texture2D(16,16,TextureFormat.RGBA32,false);
   for(int y=2;y<10;y++)atlas.Pixels[y*16+5]=new Color32(200,10,10,255);   // 图集竖条 = 旋转 90° 存放的横条
   var pose=RotatedSprite(atlas);
   var(k,s)=MakeKnight(pose);Begin(k);Tick(0);
   var px=Ghosts(s).Single().sprite.texture.Pixels;
   for(int x=0;x<8;x++)Check(IsWhite(px[3*8+x],255),"rotated bar row 3 opaque at x="+x);
   for(int y=0;y<4;y++)if(y!=3)for(int x=0;x<8;x++)Check(IsWhite(px[y*8+x],0),"other rows transparent at row "+y);
  });
  Test("Key collision: same rect/pivot/PPU but different atlas regions bake distinct silhouettes",()=>{
   var atlas=new Texture2D(8,8,TextureFormat.RGBA32,false);
   for(int x=0;x<8;x++)atlas.Pixels[0*8+x]=new Color32(1,2,3,255);
   for(int x=0;x<8;x++)atlas.Pixels[6*8+x]=new Color32(1,2,3,255);
   var poseA=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0,0),new Vector2(1f,4f/8f),packed:true);
   var poseB=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0f,4f/8f),new Vector2(1f,1f),packed:true);
   var(k,s)=MakeKnight(poseA);Begin(k);Tick(0);
   var a=OwnerGhost(k).sprite;
   s.sprite=poseB;k.transform.position=new(9f);Tick(.05f);
   var alive=Ghosts(s).OrderByDescending(r=>r.color.a).ToArray();
   var b=alive[0].sprite;
   Check(!ReferenceEquals(a,b),"two poses with identical rect/pivot/PPU never share one cache entry");
   Check(Cache("WhiteSprites").Count==2,"two distinct cache entries");
   Check(IsWhite(a.texture.Pixels[0*8+3],255)&&IsWhite(a.texture.Pixels[3*8+3],0),"first pose keeps its own atlas region");
   Check(IsWhite(b.texture.Pixels[2*8+3],255)&&IsWhite(b.texture.Pixels[0*8+3],0),"second pose keeps its own atlas region");
  });
  Test("White cache bounds itself, never destroys in-flight silhouettes and survives a fresh bake",()=>{
   var atlas=new Texture2D(8,4,TextureFormat.RGBA32,false);for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32(30,40,50,255);
   var pose=RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0,0),new Vector2(1,1));
   var(k,s)=MakeKnight(pose);Begin(k);Tick(0);
   var flying=Ghosts(s).Single();var flyingWhite=flying.sprite;
   var whitened=Method("Whitened");
   for(int i=0;i<100;i++){
    var t=new Texture2D(2,2,TextureFormat.RGBA32,false);
    var p=RectMeshSprite(t,new Rect(0,0,2,2),new Vector2(0,0),2f,new Vector2(0,0),new Vector2(1,1));
    Check(whitened.Invoke(null,new object[]{p})!=null,"distinct pose bakes");
   }
   Check(Cache("WhiteSprites").Count<=64,"cache converges to its soft cap");
   Check(Sprite.DestroyedCount>0&&Texture2D.DestroyedCount>0,"evicted poses are reclaimed with their textures");
   Check(!flyingWhite.Destroyed&&!flyingWhite.texture.Destroyed&&flying.enabled,"in-flight silhouette and its texture stay alive");
   Check(IsWhite(flyingWhite.texture.Pixels[4],255),"in-flight silhouette still white");
   // 反向时钟（早于已有条目）：新条目成为 LRU，验证本轮淘汰保护——刚返回的 sprite/贴图绝不被自己的 trim 销毁。
   var fresh=Method("Whitened");
   for(int i=0;i<40;i++){
    Time.time=-1-i;
    var t=new Texture2D(2,2,TextureFormat.RGBA32,false);
    var p=RectMeshSprite(t,new Rect(0,0,2,2),new Vector2(0,0),2f,new Vector2(0,0),new Vector2(1,1));
    var baked=(Sprite)fresh.Invoke(null,new object[]{p});
    Check(baked!=null&&!baked.Destroyed&&baked.texture!=null&&!baked.texture.Destroyed,"fresh bake survives its own trim");
   }
   Check(!flyingWhite.Destroyed,"in-flight silhouette survives the whole saturation phase");
   Clear(k);
   Check(whitened.Invoke(null,new object[]{RectMeshSprite(atlas,new Rect(0,0,8,4),new Vector2(0,0),8f,new Vector2(0,0),new Vector2(1,1))})!=null,"bake after release succeeds");
   Check(Cache("WhiteSprites").Count<=64,"cache stays bounded after the release");
  });
  Test("Native charge pixels match the independent polygon and atlas-offset reference",()=>{
   var atlas=NativeFixtureAtlas();var pose=NativeChargeSprite(atlas);var(k,s)=MakeKnight(pose);Begin(k);
   var actual=OwnerGhost(k).sprite.texture.Pixels;
   // Independently derived from the native convex polygon and exact atlas offset, without UV interpolation.
   var expected=Convert.FromBase64String("AAAAAAAAAP8AAAAAAAAAAAAA/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wAAAAAA////AP//AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD///8AAAD///////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP//////AP///////wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP///////////////wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP//////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD///////8AAP///////////wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADQ////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA0ND///////////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD//////////////9DQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP///////////////9DQ0AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP///////////////9DQ//////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP///////////wAAAAD///////////8AAAAAAAAAAAAAAAAAAAAAAAAAAP//////////AAAAAAAAAP//AAAAAP///////wAAAAAAAAAAAAAAAAAA//////+wsLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD//////////wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP////////8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP///////wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP//AP//AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP8A/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
   Check(actual.Length==expected.Length,"native envelope size");
   for(int i=0;i<expected.Length;i++)Check(IsWhite(actual[i],expected[i]),"independent native pixel "+i);
  });
  Test("Many owners pin more than 64 distinct poses and release them without destroying live ghosts",()=>{
   var atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);
   for(int i=0;i<4;i++)atlas.Pixels[i]=new Color32(1,2,3,255);
   var pinned=new List<(Knight Knight,Sprite White)>();
   for(int i=0;i<130;i++){
    var pose=RectMeshSprite(atlas,new Rect(0,0,2,2),new Vector2(0,0),2f,new Vector2(0,0),new Vector2(1,1));
    var(k,s)=MakeKnight(pose);Begin(k);var white=OwnerGhost(k).sprite;pinned.Add((k,white));
    Check(!white.Destroyed&&!white.texture.Destroyed,"new owner receives a live baked pose");
   }
   Check(Cache("WhiteSprites").Count==130,"in-flight references can exceed the soft cap across owners");
   foreach(var pair in pinned)Check(!pair.White.Destroyed&&!pair.White.texture.Destroyed,"all pinned poses remain alive");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.Count(x=>x.Contains("whiten-cache-overflow"))==1,"overflow is logged once");
   foreach(var pair in pinned)Clear(pair.Knight);
   var next=RectMeshSprite(atlas,new Rect(0,0,2,2),new Vector2(0,0),2f,new Vector2(0,0),new Vector2(1,1));
   var baked=(Sprite)Method("Whitened").Invoke(null,new object[]{next});
   Check(baked!=null&&!baked.Destroyed,"fresh result survives convergence");
   Check(Cache("WhiteSprites").Count<=64,"released entries converge to the cache target");
  });
  Test("ClearAll releases baked pose sprites and their textures",()=>{
   var atlas=new Texture2D(8,8,TextureFormat.RGBA32,false);for(int i=0;i<atlas.Pixels.Length;i++)atlas.Pixels[i]=new Color32(5,6,7,200);
   var pose=RectMeshSprite(atlas,new Rect(0,0,8,8),new Vector2(2,2),8f,new Vector2(0,0),new Vector2(1,1));
   var(k,s)=MakeKnight(pose);Begin(k);Tick(0);
   var white=Ghosts(s).Single().sprite;var whiteTexture=white.texture;
   Method("ClearAll").Invoke(null,null);
   Check(Renderers(s,true).Length==0,"renderers cleared");
   Check(white.Destroyed&&whiteTexture.Destroyed,"baked sprite and its texture destroyed together");
   Check(Cache("WhiteSprites").Count==0&&Cache("WhiteAtlases").Count==0,"caches emptied");
  });
  Console.WriteLine($"RESULT {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
