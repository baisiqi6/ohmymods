using System;
using System.Collections.Generic;
namespace UnityEngine {
public partial class Object {public static void Destroy(Object o){}}
public partial class Transform {public IntPtr Pointer=(IntPtr)30;}
public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero=>new();}
public struct Quaternion {public static Quaternion identity=>new();}
public struct Matrix4x4 {public float scale;public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>new(){scale=s.x};}
public struct Color {public float r,g,b,a;public Color(float x,float y,float z,float w=1){r=x;g=y;b=z;a=w;}public static Color white=>new(1,1,1);}
public struct Rect {public float x,y,width,height;public Rect(float a,float b,float c,float d){x=a;y=b;width=c;height=d;}}
public enum EventType {Repaint,Layout} public class Event {public static Event current=new(){type=EventType.Repaint};public EventType type;}
public static class Time {public static float unscaledTime;}
public static class Screen {public static int width=1280,height=720;}
public static class Mathf {
public const float PI=MathF.PI; public static float Abs(float x)=>MathF.Abs(x);public static float Sin(float x)=>MathF.Sin(x);public static float Cos(float x)=>MathF.Cos(x);public static float Sqrt(float x)=>MathF.Sqrt(x);
public static float Round(float x)=>MathF.Round(x);public static int FloorToInt(float x)=>(int)MathF.Floor(x);
public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);
public static float Min(float a,float b)=>MathF.Min(a,b);public static int Min(int a,int b)=>Math.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
}
public enum FontStyle {Normal} public enum TextAnchor {MiddleLeft} public enum TextClipping {Clip}
public class RectOffset {public RectOffset(int a,int b,int c,int d){}}
public class Font {public string name="native";} public class GUIStyleState {public Color textColor;}
public class GUIStyle {public int fontSize=20;public FontStyle fontStyle;public TextAnchor alignment;public RectOffset padding,margin;public bool wordWrap,richText;public TextClipping clipping;public GUIStyleState normal=new();public object FontChain=new();public GUIStyle(){}public GUIStyle(GUIStyle s){FontChain=s.FontChain;}}
public class GUISkin {public GUIStyle label=new();public Font font=new();}
public static class GUI {
public static GUISkin skin=new(); public static Color color,contentColor,backgroundColor;public static Matrix4x4 matrix;public static bool enabled,changed;public static int depth;
public record Drawn(Rect Rect,string Text,int FontSize,object FontChain,float Scale);
public static List<Drawn> Labels=new();public static string ThrowOnText;
public static void Label(Rect r,string text,GUIStyle style){if(text==ThrowOnText)throw new Exception("GUI label fault");Labels.Add(new(r,text,style.fontSize,style.FontChain,matrix.scale));}
}
public enum TextureFormat {RGBA32} public enum HideFlags {HideAndDontSave} public enum TextureWrapMode {Clamp} public enum FilterMode {Point}
public class Texture2D:Object {public HideFlags hideFlags;public TextureWrapMode wrapMode;public FilterMode filterMode;public int width,height;public Color[] Pixels;public Texture2D(int w,int h,TextureFormat f,bool m){width=w;height=h;}public void SetPixels(Color[] p){Pixels=p;}public void Apply(bool a,bool b){}}
}
public enum Season {Spring,Summer,Autumn,Winter}
public class Game {public enum State {Playing,NetworkClientPlaying,Menu,Loading} public State state=State.Playing;}
public class World {public IntPtr Pointer=(IntPtr)10;public UnityEngine.Transform gameLayer=new();}
public class Director {public IntPtr Pointer=(IntPtr)20;}
public class Managers {public static Managers Inst;public World world=new();public Director director=new();public Game game=new();public Kingdom kingdom=new();}
namespace BepInEx.Logging {public partial class ManualLogSource {public void LogInfo(string s){}}}
namespace KingdomEnhancedMod {
public class Setting {public bool Value=true;}
public static class ModConfig {public static Setting Enabled=new(),ShowCalendarHud=new();}
public static class GreekBankScope {public static bool IsActive;}
public static class BankAssistantCoordinator {public static int Reads;public static int GetStashedCoinsForPanel(){Reads++;return 80;}}
public enum WallEngineerUnavailableReason {Disabled,Loading,NoOuterWall,NetworkUnsupported}
public struct WallEngineerStatus {public bool Ready;public int AppliedMultiplier;public WallEngineerUnavailableReason Reason;}
public static class WallEngineerRuntime {public static WallEngineerStatus Status=new(){Ready=true,AppliedMultiplier=3};}
public struct CalendarSnapshot {public int TotalDay,Hour,SeasonDay,NextSeasonDay;public Season CurrentSeason,NextSeason;public bool HasNextSeason;public float Progress;}
public static class CalendarReader {public static bool TryRead(Director d,out CalendarSnapshot s){s=new(){TotalDay=15,Hour=10,SeasonDay=2,CurrentSeason=Season.Spring,NextSeason=Season.Summer,HasNextSeason=true,NextSeasonDay=30,Progress=.4f};return true;}}
public static class ImGuiCompat {public static List<(UnityEngine.Rect Rect,UnityEngine.Texture2D Texture)> Textures=new();public static void DrawTexture(UnityEngine.Rect r,UnityEngine.Texture2D t){Textures.Add((r,t));}}
}
