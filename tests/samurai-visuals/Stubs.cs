using System.Reflection;
namespace Il2CppInterop.Runtime.Injection
{ public static class ClassInjector {public static void RegisterTypeInIl2Cpp<T>() { } public static void RegisterTypeInIl2Cpp(Type t){}public static bool IsTypeRegisteredInIl2Cpp(Type t)=>false; } }
namespace UnityEngine
{
 public class Object
 {
  static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public bool Destroyed;public string name;
  public HideFlags hideFlags;
  public static int Forbidden;
  public static implicit operator bool(Object a)=>a!=null;
  public static bool operator ==(Object a,Object b)=>ReferenceEquals(a,b)||((a is null||a.Destroyed)&&(b is null||b.Destroyed));
  public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object a)=>ReferenceEquals(this,a);public override int GetHashCode()=>Pointer.GetHashCode();
  public int GetInstanceID()=>(int)Pointer;
  public static void Destroy(Object o){if(o is null||o.Destroyed)return;o.Destroyed=true;o.OnDestroyed();if(o is GameObject g){g.SetActive(false);foreach(var child in g.transform.Children.ToArray())Destroy(child.gameObject);foreach(var c in g.Components)c.Destroyed=true;}}
  protected virtual void OnDestroyed(){}
  public static void DestroyImmediate(Object o)=>Destroy(o);
  public static void DontDestroyOnLoad(Object o){}
  public static T Instantiate<T>(T o) where T:Object {Forbidden++;throw new Exception("No sprite prefab cloning allowed");}
  public static T[] FindObjectsOfType<T>() {Forbidden++;throw new Exception("No scene scans allowed");}
  public T Cast<T>() where T:class=>this as T;
 }
 public class Component:Object
 {
  public GameObject gameObject;public Transform transform=>gameObject.transform;
  public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();
  public T GetComponentInChildren<T>() where T:class=>gameObject.GetComponentInChildren<T>();
  public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T:class=>gameObject.GetComponentsInChildren<T>(includeInactive);
 }
 public class MonoBehaviour:Component
 {
  public MonoBehaviour(){}public MonoBehaviour(IntPtr p){Pointer=p;}public bool enabled=true;
  public bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;
 }
 public class GameObject:Object
 {
  public static readonly List<GameObject> All=new();public readonly List<Component> Components=new();
  public static int SpriteRendererAdds,ThrowAtSpriteAdd=-1;
  public Transform transform;private bool active=true;public bool activeSelf=>active;public int layer;
  public bool activeInHierarchy=>!Destroyed&&active&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
  public GameObject(string name="object"){this.name=name;transform=new Transform{gameObject=this};Components.Add(transform);All.Add(this);}
  public void SetActive(bool value){active=value;}
  public T AddComponent<T>() where T:Component
  { var type=typeof(T);if(type==typeof(SpriteRenderer)&&++SpriteRendererAdds==ThrowAtSpriteAdd)throw new InvalidOperationException("Injected renderer creation failure");var c=(T)(type.GetConstructor(Type.EmptyTypes)!=null?Activator.CreateInstance(type):Activator.CreateInstance(type,new object[]{IntPtr.Zero}));if(c.Pointer==IntPtr.Zero)c.Pointer=(IntPtr)(100000+All.Count*100+Components.Count);c.gameObject=this;Components.Add(c);return c; }
  public T GetComponent<T>() where T:class=>Components.OfType<T>().FirstOrDefault();
  public T GetComponentInChildren<T>() where T:class=>GetComponentsInChildren<T>().FirstOrDefault();
  public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T:class
  {var own=includeInactive||activeInHierarchy?Components.OfType<T>():Enumerable.Empty<T>();return own.Concat(transform.Children.SelectMany(c=>c.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();}
 }
 public class Transform:Component
 {
  private Transform ownerParent;public readonly List<Transform> Children=new();public Vector3 localPosition;public Vector3 localScale=Vector3.one;public Quaternion localRotation=Quaternion.identity;
  public Transform parent{get=>ownerParent;set=>SetParent(value,true);}
  public Vector3 position {get=>ownerParent==null?localPosition:ownerParent.position+ownerParent.rotation*Vector3.Scale(ownerParent.lossyScale,localPosition);set=>localPosition=ownerParent==null?value:Vector3.Divide(Quaternion.Inverse(ownerParent.rotation)*(value-ownerParent.position),ownerParent.lossyScale);}
  public Quaternion rotation{get=>ownerParent==null?localRotation:ownerParent.rotation*localRotation;set=>localRotation=ownerParent==null?value:Quaternion.Inverse(ownerParent.rotation)*value;}
  public Vector3 lossyScale=>ownerParent==null?localScale:Vector3.Scale(ownerParent.lossyScale,localScale);
  public void SetParent(Transform p,bool worldPositionStays=true){var pos=position;var rot=rotation;var scale=lossyScale;ownerParent?.Children.Remove(this);ownerParent=p;p?.Children.Add(this);if(worldPositionStays){position=pos;rotation=rot;localScale=p==null?scale:Vector3.Divide(scale,p.lossyScale);}}
  public void SetPositionAndRotation(Vector3 pos,Quaternion rot){position=pos;rotation=rot;}
 }
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y=0,float z=0){this.x=x;this.y=y;this.z=z;}
  public static Vector3 one=>new(1,1,1);public static Vector3 zero=>new();public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>MathF.Sqrt(sqrMagnitude);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator /(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
  public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
  public static Vector3 Divide(Vector3 a,Vector3 b)=>new(a.x/b.x,a.y/b.y,a.z/b.z);
  public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
 }
 public struct Quaternion
 {
  internal System.Numerics.Quaternion q;public static Quaternion identity=>new(){q=System.Numerics.Quaternion.Identity};
  public static Quaternion Euler(float x,float y,float z)=>new(){q=System.Numerics.Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180)};
  public static Quaternion Inverse(Quaternion a)=>new(){q=System.Numerics.Quaternion.Inverse(a.q)};
  public static Quaternion operator *(Quaternion a,Quaternion b)=>new(){q=a.q*b.q};
  public static Vector3 operator *(Quaternion a,Vector3 b){var v=System.Numerics.Vector3.Transform(new(b.x,b.y,b.z),a.q);return new(v.X,v.Y,v.Z);}
 }
 public struct Color
 {
  public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
  public static Color white=>new(1,1,1,1);public static Color clear=>new(0,0,0,0);
  public static bool operator ==(Color a,Color b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;public static bool operator !=(Color a,Color b)=>!(a==b);
  public override bool Equals(object other)=>other is Color c&&this==c;public override int GetHashCode()=>HashCode.Combine(r,g,b,a);
 }
 public static class Mathf
 {public static float Abs(float a)=>MathF.Abs(a);public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static float Clamp01(float a)=>Math.Clamp(a,0,1);public static float Clamp(float a,float min,float max)=>Math.Clamp(a,min,max);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;}
 public static class Time{public static float time,deltaTime=.02f,timeScale=1;public static int frameCount;}
 public class Sprite:Object
 {
  public static int Created,DestroyedCount,SpriteCreateCalls;
  public Texture2D texture;public Rect rect;public Vector2 pivot;public float pixelsPerUnit;public uint extrude;
  public bool packed;public SpritePackingRotation packingRotation=SpritePackingRotation.None;
  public Rect textureRect;   // 桩：未打包时与 rect 相同（真实语义）；打包/错位用例可直接覆盖
  public Vector2 textureRectOffset=>new(textureRect.x-rect.x, textureRect.y-rect.y);
  // 烘焙真实输入：网格在 pivot 原点局部单位，uv 指图集归一化坐标（packed sprite 的真实采样面）。
  public Vector2[] vertices;public ushort[] triangles;public Vector2[] uv;
  public static Sprite Create(Texture2D texture,Rect rect,Vector2 pivot,float pixelsPerUnit,uint extrude,SpriteMeshType meshType)
  {
   SpriteCreateCalls++;
   if(texture==null)return null;
   if(rect.width<=0f||rect.height<=0f||pixelsPerUnit<=0f)throw new ArgumentException("stub: invalid Sprite.Create arguments");
   Created++;
   // 复刻 Sprite.pivot 的真实语义：归一化入参 → 相对 rect 左下角的像素（见 Unity 文档）。
   return new Sprite{name="sprite",texture=texture,rect=rect,pivot=new Vector2(pivot.x*rect.width,pivot.y*rect.height),
     pixelsPerUnit=pixelsPerUnit,extrude=extrude,textureRect=rect};
  }
  public static void ResetCounters(){Created=DestroyedCount=SpriteCreateCalls=0;}
  protected override void OnDestroyed()=>DestroyedCount++;
 }
 public class Shader:Object{public static int PropertyToID(string name)=>name.GetHashCode();public static int Finds;public static readonly HashSet<string> FailNames=new();
  // P1-2 测试钩：按名字模拟 IL2CPP 剥离后 Shader.Find 返回 null（默认全部成功，既有用例不受影响）。
  public static Shader Find(string n){Finds++;return FailNames.Contains(n)?null:new Shader{name=n};}public string name;}
 public class Material:Object
 {
  public static int Created,PropertyWrites;public readonly Dictionary<int,Color> Colors=new();public Shader shader=new();
  public Material(){Created++;}public Material(Material m){Created++;shader=m.shader;foreach(var p in m.Colors)Colors[p.Key]=p.Value;}
  public void SetColor(int id,Color c){PropertyWrites++;Colors[id]=c;}public bool HasProperty(int id)=>true;public Color GetColor(int id)=>Colors.TryGetValue(id,out var c)?c:Color.clear;
  public static int FloatWrites,KeywordEnables;public readonly Dictionary<int,float> Floats=new();public readonly System.Collections.Generic.HashSet<string> EnabledKeywords=new();
  public void SetFloat(int id,float v){FloatWrites++;Floats[id]=v;}public void EnableKeyword(string k){KeywordEnables++;EnabledKeywords.Add(k);}
  public Material(Shader sh){Created++;shader=sh;}
 }
 public class MaterialPropertyBlock
 {
  public static int Created;public readonly Dictionary<int,Color> Colors=new();public MaterialPropertyBlock(){Created++;}
  public void SetColor(int id,Color c)=>Colors[id]=c;public Color GetColor(int id)=>Colors.TryGetValue(id,out var c)?c:Color.clear;
  public void Clear()=>Colors.Clear();public bool isEmpty=>Colors.Count==0;
 }
 public class Renderer:Component
 {
  public bool enabled=true;public int sortingLayerID,sortingOrder;public Material sharedMaterial;public readonly Dictionary<int,Color> Block=new();
  public static int StringSortingWrites,MaterialReads;
  public string sortingLayerName {set{StringSortingWrites++;throw new MissingMethodException("GetPinnableReference");}}
  public Material material{get{MaterialReads++;throw new Exception("Implicit per-renderer material access forbidden");}set=>sharedMaterial=value;}
  public void SetPropertyBlock(MaterialPropertyBlock p){Block.Clear();if(p!=null)foreach(var c in p.Colors)Block[c.Key]=c.Value;}
  public void GetPropertyBlock(MaterialPropertyBlock p){p.Clear();foreach(var c in Block)p.Colors[c.Key]=c.Value;}
 }
 public class TrailRenderer:Renderer {public bool emitting;public int positionCount;}
 public class SpriteRenderer:Renderer {public Sprite sprite;public bool flipX,flipY;public Color color=Color.white;}
 // ---- 白剪影烘焙所需的纹理/GPU 桩（2026-09-25 根因 D 测试面）----
 public enum HideFlags { None=0, HideAndDontSave=61 }
 public enum TextureFormat { RGBA32=4, ARGB32=5 }
 public enum FilterMode { Point=0, Bilinear=1, Trilinear=2 }
 public enum TextureWrapMode { Repeat=0, Clamp=1 }
 public enum RenderTextureFormat { ARGB32=0, Default=7 }
 public enum RenderTextureReadWrite { Default=0, Linear=1, sRGB=2 }
 public enum SpriteMeshType { FullRect=0, Tight=1 }
 public enum SpritePackingRotation { None=0, FlipHorizontal=1, FlipVertical=2, Rotate180=3, Any=4 }
 public struct Vector2
 {
  public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
  public static Vector2 zero=>new(0,0);
  public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);
  public override bool Equals(object o)=>o is Vector2 v&&this==v;public override int GetHashCode()=>HashCode.Combine(x,y);
  public override string ToString()=>$"({x}, {y})";
 }
 public struct Rect
 {
  public float x,y,width,height;
  public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
  public Vector2 position=>new(x,y);public Vector2 size=>new(width,height);
  public static bool operator ==(Rect a,Rect b)=>a.x==b.x&&a.y==b.y&&a.width==b.width&&a.height==b.height;public static bool operator !=(Rect a,Rect b)=>!(a==b);
  public override bool Equals(object o)=>o is Rect r&&this==r;public override int GetHashCode()=>HashCode.Combine(x,y,width,height);
  public override string ToString()=>$"(x:{x}, y:{y}, w:{width}, h:{height})";
 }
 public struct Color32
 {
  public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}
  public override string ToString()=>$"RGBA({r},{g},{b},{a})";
 }
 public class Texture:Object
 {
  public int width,height;public FilterMode filterMode;public TextureWrapMode wrapMode;
  public virtual bool isReadable=>false;
 }
 public class Texture2D:Texture
 {
  public static int Created,DestroyedCount,GetPixelsCalls,SetPixelsCalls,ApplyCalls;
  public static bool ReadPixelsThrows;
  public TextureFormat format;public bool mipChain;public Color32[] Pixels;public bool SimulateUnreadable;
  private readonly bool readable;
  public override bool isReadable=>readable&&!SimulateUnreadable;
  public Texture2D(int width,int height):this(width,height,TextureFormat.RGBA32,false){}
  public Texture2D(int width,int height,TextureFormat format,bool mipChain)
  {this.width=width;this.height=height;this.format=format;this.mipChain=mipChain;Pixels=new Color32[Math.Max(0,width*height)];readable=true;Created++;}
  public Color32[] GetPixels32()
  {
   GetPixelsCalls++;
   if(!isReadable)throw new InvalidOperationException("stub: texture is not CPU readable");
   var copy=new Color32[Pixels.Length];Array.Copy(Pixels,copy,Pixels.Length);return copy;   // 真实 GetPixels32 返回新副本
  }
  public void SetPixels32(Color32[] colors)
  {
   SetPixelsCalls++;
   if(colors==null||colors.Length!=Pixels.Length)throw new ArgumentException("stub: SetPixels32 length mismatch");
   Array.Copy(colors,Pixels,colors.Length);
  }
  public void Apply(){ApplyCalls++;}public void Apply(bool updateMipmaps){ApplyCalls++;}public void Apply(bool updateMipmaps,bool makeNoLongerReadable){ApplyCalls++;}
  public void ReadPixels(Rect source,int destX,int destY,bool recalculateMipMaps)
  {
   if(ReadPixelsThrows)throw new InvalidOperationException("stub: ReadPixels failed");
   var rt=RenderTexture.active??throw new InvalidOperationException("stub: no active render target");
   int w=(int)source.width,h=(int)source.height;
   for(int row=0;row<h;row++)for(int col=0;col<w;col++)
   {
    int sx=(int)source.x+col,sy=(int)source.y+row,dx=destX+col,dy=destY+row;
    if(sx<0||sy<0||sx>=rt.width||sy>=rt.height||dx<0||dy<0||dx>=width||dy>=height)continue;
    Pixels[dy*width+dx]=rt.Pixels[sy*rt.width+sx];   // 左下原点，复刻真实 ReadPixels 下标语义
   }
  }
  public static void ResetCounters(){Created=DestroyedCount=GetPixelsCalls=SetPixelsCalls=ApplyCalls=0;ReadPixelsThrows=false;}
  protected override void OnDestroyed()=>DestroyedCount++;
 }
 public class RenderTexture:Texture
 {
  public static int TemporaryGets,TemporaryReleases,DestroyedCount;public static RenderTexture active;
  public static bool GetTemporaryThrows;
  public bool Released;public Color32[] Pixels;
  public RenderTexture(int width,int height){this.width=width;this.height=height;Pixels=new Color32[Math.Max(0,width*height)];}
  public static RenderTexture GetTemporary(int width,int height,int depthBuffer,RenderTextureFormat format,RenderTextureReadWrite readWrite)
  {TemporaryGets++;if(GetTemporaryThrows)throw new InvalidOperationException("stub: GetTemporary failed");return new RenderTexture(width,height);}
  public static void ReleaseTemporary(RenderTexture temp){if(temp==null)return;TemporaryReleases++;temp.Released=true;Object.Destroy(temp);}
  public static void ResetCounters(){TemporaryGets=TemporaryReleases=DestroyedCount=0;active=null;GetTemporaryThrows=false;}
  protected override void OnDestroyed()=>DestroyedCount++;
 }
 public static class Graphics
 {
  public static int Blits;public static bool BlitThrows;
  public static void Blit(Texture source,RenderTexture dest)
  {
   Blits++;
   if(BlitThrows)throw new InvalidOperationException("stub: Blit failed");
   if(source is not Texture2D src||dest==null)return;
   int w=Math.Min(src.width,dest.width),h=Math.Min(src.height,dest.height);
   for(int y=0;y<h;y++)Array.Copy(src.Pixels,y*src.width,dest.Pixels,y*dest.width,w);   // GPU 侧拷贝：绕过 CPU 可读门
  }
  public static void ResetCounters(){Blits=0;BlitThrows=false;}
 }
}
public class SpriteRendererFX:UnityEngine.Component {public UnityEngine.SpriteRenderer _renderer;}
public class Character:UnityEngine.Component {public SpriteRendererFX spriteFX;}
public class Damageable:UnityEngine.Component {public bool isDead;}
public class Knight:UnityEngine.MonoBehaviour {public UnityEngine.TrailRenderer _trail;public int Style=2;public Character _character;public Damageable _damageable;}
namespace KingdomEnhancedMod
{
 public static class ModConfig{public class Option{public bool Value=true;}public static Option Enabled=new();}
 public static class PatchRoles_KnightStyle{public static bool TryGetResolvedStyleIndex(Knight k,out int style){style=k.Style;return true;}}
 public class KingdomEnhancedPlugin {public static KingdomEnhancedPlugin Instance=new();public Logger LogSource=new();public class Logger{public readonly List<string> Lines=new();public void LogWarning(string x)=>Lines.Add(x);public void LogError(string x)=>Lines.Add(x);public void LogInfo(string x)=>Lines.Add(x);}}
}
