using System.Collections;

namespace UnityEngine
{
 public class Object
 {
  static long next;
  IntPtr ptr=(IntPtr)Interlocked.Increment(ref next);
  public bool ThrowPointer;
  public IntPtr Pointer {get{if(ThrowPointer)throw new InvalidOperationException("injected native wrapper read");return ptr;}set=>ptr=value;}
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject?.transform;}
 public struct Scene {public int handle;}
 public class GameObject:Object
 {
  static int nextId;
  public int Id=Interlocked.Increment(ref nextId), ComponentReads;
  public bool activeInHierarchy=true;
  public string name="Peasant";
  public Scene scene=new(){handle=10};
  public Transform transform;
  readonly List<Component> components=new();
  public GameObject(){transform=new Transform{gameObject=this};}
  public int GetInstanceID()=>Id;
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:Component {ComponentReads++;return components.OfType<T>().FirstOrDefault();}
 }
 public class Transform:Component
 {
  public Transform parent;
  public bool IsChildOf(Transform target){for(Transform t=this;t!=null;t=t.parent)if(t==target)return true;return false;}
 }
 public static class Time {public static float unscaledTime;}
 public enum EventType {Repaint,Layout,MouseDown}
 public class Event {public static Event current=new(){type=EventType.Repaint};public EventType type;}
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color white=>new(1,1,1,1);}
 public struct Rect {public float x,y,width,height;public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero=>new(0,0,0);}
 public struct Quaternion {public static Quaternion identity=>new();}
 public struct Matrix4x4 {public float sx,sy;public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new(){sx=s.x,sy=s.y};}
 public enum FontStyle {Normal,Bold}
 public enum TextAnchor {MiddleLeft}
 public enum TextClipping {Clip}
 public class RectOffset {public RectOffset(int l,int r,int t,int b){}}
 public class GUIStyleState {public Color textColor;}
 public class GUIStyle
 {
  public int fontSize;public FontStyle fontStyle;public TextAnchor alignment;public RectOffset padding,margin;
  public bool wordWrap,richText;public TextClipping clipping;public GUIStyleState normal=new();public object FontChain;
  public GUIStyle(){}public GUIStyle(GUIStyle other){FontChain=other.FontChain;}
 }
 public class GUISkin {public GUIStyle label=new(){FontChain=new object()};}
 public static class Screen {public static int width=1280,height=720;}
 public static class Mathf {public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);}
 public static class GUI
 {
  public static GUISkin skin=new();public static Color color,contentColor,backgroundColor;
  public static Matrix4x4 matrix=new(){sx=1,sy=1};public static bool enabled,changed;public static int depth;
  public static bool ThrowLabel;public static readonly List<(Rect Rect,string Text,GUIStyle Style,Matrix4x4 Matrix)> Labels=new();
  public static void Label(Rect r,string text,GUIStyle style){if(ThrowLabel)throw new InvalidOperationException("injected GUI fault");Labels.Add((r,text,style,matrix));}
 }
}

public class CountingRoster:IEnumerable<Character>
{
 public readonly List<Character> Items=new();public int Enumerations;public bool ThrowEnumeration;public int ThrowAfter=-1;
 public IEnumerator<Character> GetEnumerator(){Enumerations++;if(ThrowEnumeration)throw new InvalidOperationException("injected roster read");return Iterate().GetEnumerator();}
 IEnumerable<Character> Iterate(){for(int i=0;i<Items.Count;i++){if(i==ThrowAfter)throw new InvalidOperationException("injected partial roster read");yield return Items[i];}}
 IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}
public class Character:UnityEngine.Component {public Damageable _damageable;}
public class Damageable:UnityEngine.Component {public bool isDead;}
public class Worker:UnityEngine.Component{}
public class Archer:UnityEngine.Component{}
public class Farmer:UnityEngine.Component{}
public class Pikeman:UnityEngine.Component{}
public class Ninja:UnityEngine.Component {public bool _isFisher;}
public class Berserker:UnityEngine.Component{}
public class Peasant:UnityEngine.Component{}
public class Beggar:UnityEngine.Component{}
public class Knight:UnityEngine.Component {public int Style=-1;public bool Resolved,ThrowStyle;}
public class Kingdom:UnityEngine.Object {public CountingRoster _characters=new();}
public class World:UnityEngine.Object {public UnityEngine.Transform gameLayer=new UnityEngine.GameObject().transform;}
public class Game {public State state=State.Playing;public enum State {Playing,NetworkClientPlaying,Menu,Loading,MainMenu}}
public static class NetworkBigBoss {public static bool HasWorldAuth=true;}
public class Managers
{
 static Managers instance;public static bool ThrowInst;public static int InstReads;
 public static Managers Inst {get{InstReads++;if(ThrowInst)throw new InvalidOperationException("injected manager singleton read");return instance;}set=>instance=value;}
 public Kingdom kingdom=new();public World world=new();public Game game=new();
}
namespace KingdomEnhancedMod
{
 public static class ModConfig
 {
  public class Flag {public bool Value=true;}
  public static Flag Enabled=new(),ShowPopulationHud=new(),AutoRestockWorkersEnabled=new(){Value=false};
 }
 public static class PatchRoles_KnightStyle
 {
  public static bool TryGetResolvedStyleIndex(Knight knight,out int style){if(knight.ThrowStyle)throw new InvalidOperationException("injected style read");style=knight.Style;return knight.Resolved;}
 }
 public class KingdomEnhancedPlugin
 {
  public static KingdomEnhancedPlugin Instance=new();public Log LogSource=new();
  public class Log {public List<string> Info=new(),Warnings=new();public void LogInfo(string text)=>Info.Add(text);public void LogWarning(string text)=>Warnings.Add(text);}
 }
}
