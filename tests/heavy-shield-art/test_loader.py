"""Compile the full production art file against recording Unity stubs and run resource contracts.

Ten real final PNGs are embedded. The PNG decoder exposes exact pixels through
Unity's bottom-up GetPixels32 contract. Only the chosen evidence directory is written.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

STUBS = r"""
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Linq;
namespace Il2CppInterop.Runtime.Attributes { public sealed class HideFromIl2CppAttribute : Attribute { } }
namespace UnityEngine
{
    public class Object { public static readonly List<Object> Destroyed = new(); public static void Destroy(Object o) { Destroyed.Add(o); } }
    public class MonoBehaviour : Object { public GameObject gameObject; public MonoBehaviour(IntPtr p) { } }
    public class GameObject : Object
    {
        public IntPtr Pointer; public Transform transform = new(); public GameObject(string name) { }
        public int GetInstanceID() => 1; public T AddComponent<T>() where T : new() => new T();
    }
    public class Transform { public Vector3 localPosition, position; public void SetParent(Transform p, bool world) { } }
    public struct Vector2
    {
        public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; }
        public static Vector2 zero => new(0,0);
        public static bool operator ==(Vector2 a,Vector2 b) => a.x==b.x && a.y==b.y;
        public static bool operator !=(Vector2 a,Vector2 b) => !(a==b);
        public override bool Equals(object o) => o is Vector2 v && this==v;
        public override int GetHashCode() => HashCode.Combine(x,y);
    }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h) { this.x=x;this.y=y;width=w;height=h; } }
    public struct Color32 { public byte r,g,b,a; }
    public enum FilterMode { Point, Bilinear }
    public enum TextureWrapMode { Clamp, Repeat }
    public enum TextureFormat { RGBA32 }
    public enum SpriteMeshType { FullRect }
    public class Texture2D : Object
    {
        public int width,height,anisoLevel; public bool Mipmaps; public FilterMode filterMode = FilterMode.Bilinear;
        public TextureWrapMode wrapMode=TextureWrapMode.Repeat; public Color32[] Pixels;
        public Texture2D(int w,int h,TextureFormat f,bool mip) { width=w;height=h;Mipmaps=mip; }
        public Color32[] GetPixels32() => ImageConversion.Mode == "pixelsNull" ? null : Pixels;
    }
    public class Sprite : Object
    {
        public static int Creates, FailAt; public Texture2D texture; public Rect rect; public Vector2 pivot; public float pixelsPerUnit;
        public static Sprite Create(Texture2D t,Rect r,Vector2 p,float ppu,uint e,SpriteMeshType mesh)
        {
            Creates++; if (FailAt == Creates) return null;
            return new Sprite { texture=t,rect=r,pivot=p,pixelsPerUnit=ppu };
        }
    }
    public class SpriteRenderer : Object { public bool enabled,flipX; public int sortingLayerID,sortingOrder; public object sharedMaterial; public Sprite sprite; }
    public class Rigidbody2D { public bool isKinematic;public Vector2 velocity;public float angularVelocity; }
    public static class ImageConversion
    {
        public static int Loads; public static string Mode;
        public static bool LoadImage(Texture2D texture,byte[] bytes,bool unreadable)
        {
            Loads++; if (Mode=="loadFalse") return false;
            using var data=new MemoryStream(); int width=0,height=0;
            for (int offset=8; offset<bytes.Length;)
            {
                int length=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset,4)); string name=System.Text.Encoding.ASCII.GetString(bytes,offset+4,4);
                if (name=="IHDR")
                {
                    width=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset+8,4)); height=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset+12,4));
                    if (bytes[offset+16]!=8 || bytes[offset+17]!=6 || bytes[offset+20]!=0) throw new Exception("test decoder requires noninterlaced RGBA8 PNG");
                }
                if (name=="IDAT") data.Write(bytes,offset+8,length);
                offset+=length+12;
            }
            data.Position=0;using var uncompressed=new MemoryStream();using (var zlib=new ZLibStream(data,CompressionMode.Decompress,true)) zlib.CopyTo(uncompressed);
            byte[] scan=uncompressed.ToArray(),previous=new byte[width*4],row=new byte[width*4];int cursor=0;
            Color32[] pixels=new Color32[width*height];
            for (int y=0;y<height;y++)
            {
                byte filter=scan[cursor++];
                for (int i=0;i<row.Length;i++)
                {
                    int left=i>=4?row[i-4]:0,up=previous[i],upperLeft=i>=4?previous[i-4]:0;
                    int predict=filter switch {0=>0,1=>left,2=>up,3=>(left+up)/2,4=>Paeth(left,up,upperLeft),_=>throw new Exception("unknown PNG filter")};
                    row[i]=unchecked((byte)(scan[cursor++]+predict));
                }
                for (int x=0;x<width;x++) pixels[(height-1-y)*width+x]=new Color32{r=row[x*4],g=row[x*4+1],b=row[x*4+2],a=row[x*4+3]};
                (previous,row)=(row,previous);
            }
            if (cursor!=scan.Length) throw new Exception("PNG decompressed byte count");
            texture.width=Mode=="size"?width+1:width;texture.height=height;
            if (Mode=="alpha") { int index=Array.FindIndex(pixels,p=>p.a==255);pixels[index].a=127; }
            if (Mode=="transparent") for(int i=0;i<pixels.Length;i++) pixels[i].a=0;
            if (Mode=="opaque") for(int i=0;i<pixels.Length;i++) pixels[i].a=255;
            if (Mode=="padding") pixels[13*64].a=255;
            texture.Pixels=pixels;return true;
        }
        private static int Paeth(int a,int b,int c)
        { int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb && pa<=pc?a:pb<=pc?b:c; }
    }
}
namespace KingdomEnhancedMod
{
    internal sealed class TestLog { internal readonly List<string> Warnings=new();internal void LogWarning(string s) => Warnings.Add(s); }
    internal sealed class KingdomEnhancedPlugin { internal static KingdomEnhancedPlugin Instance=new();internal TestLog LogSource=new(); }
    internal struct HeavyShieldCareerHandle
    {
        internal int GoId;internal IntPtr Root;internal HeavyShieldQuota.Side Side;
        public static bool operator ==(HeavyShieldCareerHandle a,HeavyShieldCareerHandle b) => a.GoId==b.GoId;
        public static bool operator !=(HeavyShieldCareerHandle a,HeavyShieldCareerHandle b) => !(a==b);
        public override bool Equals(object o) => o is HeavyShieldCareerHandle h && this==h;
        public override int GetHashCode()=>GoId;
    }
    internal static class HeavyShieldQuota { internal enum Side { Left,Right } }
    internal static class HeavyShieldIdentity
    {
        internal static bool TryGetPaidBow(DroppableTool tool,out HeavyShieldCareerHandle h) { h=default;return false; }
        internal static bool ValidateCareer(in HeavyShieldCareerHandle h)=>false;
    }
    internal class DroppableTool
    {
        internal UnityEngine.GameObject gameObject;internal UnityEngine.Transform transform=new();internal bool pickedUp;
        internal object friendlyClaimer,enemyClaimer;internal UnityEngine.Rigidbody2D _rigidbody;
        internal T GetComponent<T>() where T : new()=>new T();
    }
}
"""

PROGRAM=r"""
using System;
using KingdomEnhancedMod;
using UnityEngine;
int checks=0;
void Check(bool yes,string label) { checks++;if(!yes)throw new Exception(label); }
void Near(float a,float b,string label)=>Check(Math.Abs(a-b)<.000002f,label);
string mode=args.Length==0?"success":args[0];
if(mode=="success")
{
    Check(!HeavyShieldArt.TryGetSprite((HeavyShieldAtlasId)99,0,out var unknown)&&unknown==null,"unknown id closed");
    Check(!HeavyShieldArt.TryGetSoldierSprite(-1,out _)&&!HeavyShieldArt.TryGetSoldierSprite(509,out _),"invalid soldier frame closed");
    Check(ImageConversion.Loads==0,"bad indices don't load");
    foreach(var id in Enum.GetValues<HeavyShieldAtlasId>())
    {
        Texture2D texture=null;
        for(int frame=0;frame<HeavyShieldArtLayout.FrameCount(id);frame++)
        {
            Check(HeavyShieldArt.TryGetSprite(id,frame,out var sprite)&&sprite!=null,id+" frame loads");
            Check(HeavyShieldArt.TryGetSprite(id,frame,out var cached)&&ReferenceEquals(cached,sprite),id+" cached sprite");
            texture??=sprite.texture;Check(ReferenceEquals(sprite.texture,texture),id+" one shared texture");
            Check(!texture.Mipmaps&&texture.filterMode==FilterMode.Point&&texture.wrapMode==TextureWrapMode.Clamp&&texture.anisoLevel==0,id+" crisp sampler");
            Near(sprite.pixelsPerUnit,HeavyShieldArtLayout.PixelsPerUnitFor(id),id+" PPU");
            Near(sprite.pivot.x,HeavyShieldArtLayout.PivotX(id),id+" pivot x");Near(sprite.pivot.y,HeavyShieldArtLayout.PivotY(id),id+" pivot y");
            HeavyShieldArtLayout.FrameToCell(id,frame,out int column,out int row);
            Near(sprite.rect.x,column*HeavyShieldArtLayout.CellWidth(id),id+" rect x");
            Near(sprite.rect.y,(HeavyShieldArtLayout.Rows(id)-1-row)*HeavyShieldArtLayout.CellHeight(id),id+" rect y");
            Near(sprite.rect.width,HeavyShieldArtLayout.CellWidth(id),id+" rect width");Near(sprite.rect.height,HeavyShieldArtLayout.CellHeight(id),id+" rect height");
        }
    }
    Check(ImageConversion.Loads==8,"all8 atlas resources decoded once");
    Check(HeavyShieldArt.TryGetPaidShieldSprite(out var detail)&&HeavyShieldArt.TryGetPaidShieldSprite(out var detailCached)&&ReferenceEquals(detail,detailCached),"paid detail cache");
    Check(HeavyShieldArt.TryGetPaidShieldSprite(HeavyShieldArtDensity.Coarse,out var coarse)&&HeavyShieldArt.TryGetPaidShieldSprite(HeavyShieldArtDensity.Coarse,out var coarseCached)&&ReferenceEquals(coarse,coarseCached),"paid coarse cache");
    foreach(var pair in new[]{(detail,19f,30f,48f),(coarse,13f,20f,32f)})
    {
        var s=pair.Item1;Near(s.rect.x,0,"independent paid rect x");Near(s.rect.y,0,"independent paid rect y");Near(s.rect.width,pair.Item2,"paid width");Near(s.rect.height,pair.Item3,"paid height");Near(s.pixelsPerUnit,pair.Item4,"paid shop PPU");Near(s.pivot.x,.5f,"paid center");Near(s.pivot.y,0,"paid bottom");
        Check(!s.texture.Mipmaps&&s.texture.filterMode==FilterMode.Point&&s.texture.wrapMode==TextureWrapMode.Clamp,"paid crisp sampler");
    }
    Check(!ReferenceEquals(detail.texture,coarse.texture),"density independent paid texture");
    Check(HeavyShieldArt.TryGetSoldierSprite(0,out var normal)&&HeavyShieldArt.TryGetSoldierSprite(0,HeavyShieldArtDensity.Detail,out var densityDetail)&&ReferenceEquals(normal,densityDetail),"default detail entry");
    Check(HeavyShieldArt.TryGetSoldierSprite(0,HeavyShieldArtDensity.Coarse,out var densityCoarse)&&densityCoarse.texture.width==768,"coarse entry");
    Check(!HeavyShieldArt.TryGetPaidShieldSprite((HeavyShieldArtDensity)99,out _),"invalid paid density");
    Check(ImageConversion.Loads==10&&Sprite.Creates==1102,"all10 resources loaded and1102 sprites created once");
    Check(KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count==0&&UnityEngine.Object.Destroyed.Count==0,"ready resources no warnings/destruction");
}
else
{
    bool paid=mode.StartsWith("paid-");string fault=paid?mode.Substring(5):mode;
    ImageConversion.Mode=fault;
    if(fault=="sprite") { ImageConversion.Mode=null;Sprite.FailAt=paid?1:3; }
    for(int i=0;i<12;i++)
    {
        bool loaded=paid?HeavyShieldArt.TryGetPaidShieldSprite(out var shield):HeavyShieldArt.TryGetSoldierSprite(0,out shield);
        Check(!loaded&&shield==null,"failed output null "+i);
    }
    Check(ImageConversion.Loads==1,"failure isn't decoded again");
    Check(KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count==1,"failure warning once");
    Check(UnityEngine.Object.Destroyed.Count==(fault=="sprite"&&!paid?3:1),"partial sprite/texture cleanup");
}
Console.WriteLine($"PASS loader {mode}: {checks} checks, loads={ImageConversion.Loads}, creates={Sprite.Creates}, warnings={KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count}");
"""

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--evidence",type=Path,default=Path(__file__).resolve().parent/"evidence"/"loader")
    ap.add_argument("--dotnet",type=Path,default=Path("C:/Users/ADMIN/dotnet8/dotnet.exe"))
    args=ap.parse_args();here=Path(__file__).resolve().parent
    repo=next(p for p in here.parents if (p/"il2cpp/PatchRoles_HeavyShieldArt.cs").is_file())
    args.evidence.mkdir(parents=True,exist_ok=True)
    (args.evidence/"Stubs.cs").write_text(STUBS,encoding="utf-8")
    (args.evidence/"Program.cs").write_text(PROGRAM,encoding="utf-8")
    resources=sorted((repo/"il2cpp/Assets").glob("HeavyShield*.png"))
    assert len(resources)==10,resources
    xml='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>latest</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable><NoWarn>CS0649</NoWarn></PropertyGroup><ItemGroup>'
    for source in (repo/"il2cpp/PatchRoles_HeavyShieldArt.cs",args.evidence/"Program.cs",args.evidence/"Stubs.cs"):
        xml+=f'<Compile Include="{escape(str(source))}" />'
    for source in resources:xml+=f'<EmbeddedResource Include="{escape(str(source))}" LogicalName="KingdomEnhancedMod.{source.name}" />'
    xml+='</ItemGroup></Project>'
    project=args.evidence/"Loader.csproj";project.write_text(xml,encoding="utf-8")
    commands=[]
    def run(command):
        env=os.environ.copy();env["DOTNET_CLI_UI_LANGUAGE"]="en-US"
        proc=subprocess.run([str(s) for s in command],cwd=repo,env=env,capture_output=True,text=True,encoding="utf-8",errors="replace")
        entry={"command":[str(s) for s in command],"exitCode":proc.returncode,"stdout":proc.stdout,"stderr":proc.stderr};commands.append(entry)
        print(proc.stdout.strip());print(proc.stderr.strip()) if proc.stderr else None
        if proc.returncode:raise RuntimeError(f"command failed: {command}")
    report={"notGameTested":True,"productionSHA256":hashlib.sha256((repo/"il2cpp/PatchRoles_HeavyShieldArt.cs").read_bytes()).hexdigest(),"commands":commands}
    try:
        run([args.dotnet,"build",project,"-c","Release","-p:BepInExPluginsPath="])
        dll=args.evidence/"bin/Release/net8.0/Loader.dll"
        for mode in ("success","loadFalse","size","pixelsNull","alpha","transparent","opaque","padding","sprite","paid-loadFalse","paid-size","paid-pixelsNull","paid-alpha","paid-transparent","paid-opaque","paid-sprite"):
            run([args.dotnet,dll,mode])
        report["result"]="pass"
    except Exception as e:
        report["result"]="fail";report["error"]=str(e);raise
    finally:(args.evidence/"loader-evidence.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")

if __name__=="__main__":main()
