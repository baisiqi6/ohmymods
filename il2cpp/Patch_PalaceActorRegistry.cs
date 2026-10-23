// Pure reader for the reviewed R12 actor batch. It parses design facts only: no IO, textures,
// native actor sizing, footprint inference, animation allocation or ready/promotion authority.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod;

internal sealed class PalaceActorRegistry
{
    internal const int MaxJsonChars=1024*1024;
    private static readonly HashSet<string> KnownIds=new(StringComparer.Ordinal)
    { "patrol-guard-lance","patrol-guard-shield","court-dancer","guardian-orb","guardian-lute",
      "guardian-sword","guardian-pole","resident-cream","resident-pink","resident-brown" };
    internal IReadOnlyList<Actor> Actors { get; }
    internal int TimedFrameCount { get; }
    internal string RegistrySha256 { get; }
    internal string SourceManifestSha256 { get; }
    internal int DesignOnlyPPU { get; }
    internal bool NativeAccepted => false;

    private PalaceActorRegistry(Actor[] actors,string rawHash,string sourceHash,int designPpu)
    {
        Actors=Array.AsReadOnly(actors); TimedFrameCount=actors.Sum(a=>a.Frames.Count);
        RegistrySha256=rawHash; SourceManifestSha256=sourceHash; DesignOnlyPPU=designPpu;
    }

    internal static bool TryParse(string json,out PalaceActorRegistry registry,out string reason)
    {
        registry=null;reason="";
        if(string.IsNullOrEmpty(json)||json.Length>MaxJsonChars){reason="size";return false;}
        try
        {
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});
            var root=doc.RootElement; int nodes=0; Require(NoDuplicates(root,ref nodes),"duplicate-or-budget");
            Keys(root,"schema,runtimePathsAreRelative,coordinateSystem,designOnlyPPU,pixelDataStatus,consumerWired,nativeAcceptance,actors,sourceManifestSHA256");
            Require(Text(root,"schema")=="palace-r12-actor-batch/1"&&Bool(root,"runtimePathsAreRelative"),"schema-coordinate");
            Require(!Bool(root,"consumerWired")&&!Bool(root,"nativeAcceptance"),"unverified-native-status");
            Text(root,"pixelDataStatus",256);
            var coords=root.GetProperty("coordinateSystem");Keys(coords,"sourceRect,localOrigin,pivotBottomLeft");
            Require(Text(coords,"sourceRect",256)=="x,y,width,height, atlas top-left logical pixels"
                &&Text(coords,"localOrigin",256)=="actor cell top-left"
                &&Text(coords,"pivotBottomLeft",256)=="fixed cell bottom-left pixels; never re-center bbox","coordinate-system");
            int ppu=Int(root,"designOnlyPPU");Require(ppu==32,"design-ppu");
            string sourceHash=Hash(root,"sourceManifestSHA256");
            var rows=root.GetProperty("actors");Require(rows.ValueKind==JsonValueKind.Array&&rows.GetArrayLength()==KnownIds.Count,"actor-count");
            var actors=new List<Actor>();var ids=new HashSet<string>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var masters=new HashSet<string>(StringComparer.Ordinal);
            int total=0;
            foreach(var row in rows.EnumerateArray())
            {
                var actor=new Actor(row,ppu);Require(ids.Add(actor.Id)&&paths.Add(actor.Path)&&masters.Add(actor.MasterId),"duplicate-actor-or-master");
                total=checked(total+actor.Frames.Count);Require(total<=4096,"frame-budget");actors.Add(actor);
            }
            string rawHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            registry=new PalaceActorRegistry(actors.ToArray(),rawHash,sourceHash,ppu);return true;
        }
        catch(JsonException){reason="json";return false;}
        catch(FormatException e){reason=e.Message;return false;}
        catch(InvalidOperationException){reason="type";return false;}
        catch(KeyNotFoundException){reason="missing-field";return false;}
        catch(OverflowException){reason="number-overflow";return false;}
    }

    internal readonly struct Rect
    {
        internal int X { get; } internal int Y { get; } internal int Width { get; } internal int Height { get; }
        internal Rect(int x,int y,int width,int height){X=x;Y=y;Width=width;Height=height;}
    }
    internal readonly struct Point
    {
        internal double X { get; } internal double Y { get; }
        internal Point(double x,double y){X=x;Y=y;}
    }
    internal sealed class Frame
    {
        internal int Index { get; } internal string State { get; } internal int DurationMs { get; }
        internal Rect SourceRectTopLeft { get; } internal Rect UnitySourceRectBottomLeft { get; }
        internal string MasterId { get; } internal Rect MasterRect { get; }
        internal Point PivotBottomLeftPixels { get; } internal string LogicalRgbaSha256 { get; }
        internal Frame(JsonElement row,int index,Actor actor)
        {
            Keys(row,"index,state,durationMs,sourceRect,masterId,masterRect,pivotBottomLeft,logicalRGBA_SHA256");
            Index=Int(row,"index");Require(Index==index,"frame-order");State=Text(row,"state");DurationMs=Int(row,"durationMs");Require(DurationMs>0,"duration");
            SourceRectTopLeft=ReadRect(row.GetProperty("sourceRect"));
            Require(Inside(SourceRectTopLeft,new Rect(0,0,actor.Width,actor.Height)),"source-rect-bounds");
            Require(SourceRectTopLeft.Width==actor.CellWidth&&SourceRectTopLeft.Height==actor.CellHeight,"logical-cell");
            UnitySourceRectBottomLeft=new Rect(SourceRectTopLeft.X,actor.Height-SourceRectTopLeft.Y-SourceRectTopLeft.Height,SourceRectTopLeft.Width,SourceRectTopLeft.Height);
            MasterId=Text(row,"masterId",256);Require(MasterId==actor.MasterId,"frame-master-id");
            MasterRect=ReadRect(row.GetProperty("masterRect"));Require(Inside(MasterRect,actor.MasterRect)
                &&MasterRect.Width==SourceRectTopLeft.Width&&MasterRect.Height==SourceRectTopLeft.Height,"master-rect-bounds");
            PivotBottomLeftPixels=ReadPoint(row.GetProperty("pivotBottomLeft"));
            LogicalRgbaSha256=Hash(row,"logicalRGBA_SHA256");
        }
    }
    internal sealed class Clip
    {
        internal string State { get; } internal IReadOnlyList<int> Indices { get; } internal IReadOnlyList<int> DurationsMs { get; }
        internal long TotalDurationMs { get; } internal bool Loops { get; } internal bool OneShot { get; }
        internal string DefaultFacing { get; } internal string EndFacing { get; } internal string EntryPoseRelation { get; }
        internal IReadOnlyList<string> CompatiblePhases { get; }
        internal Clip(string state,JsonElement row,Actor actor,HashSet<int> covered)
        {
            Keys(row,"indices,durationsMs,totalDurationMs,loops,oneShot,defaultFacing,endFacing,entryPoseRelation,compatiblePhases");
            State=state;var indices=ReadInts(row.GetProperty("indices"),1,512);var durations=ReadInts(row.GetProperty("durationsMs"),1,512);
            Require(indices.Length==durations.Length,"clip-length");long sum=0;int previous=-1;
            for(int i=0;i<indices.Length;i++)
            {
                int ix=indices[i];Require(ix>previous&&ix>=0&&ix<actor.Frames.Count&&covered.Add(ix),"clip-order-index");
                Require(actor.Frames[ix].State==state&&durations[i]==actor.Frames[ix].DurationMs,"clip-frame-mismatch");previous=ix;sum+=durations[i];
            }
            Require(row.GetProperty("totalDurationMs").TryGetInt64(out long declared)&&declared==sum,"clip-duration");
            TotalDurationMs=sum;Indices=Array.AsReadOnly(indices);DurationsMs=Array.AsReadOnly(durations);
            Loops=Bool(row,"loops");OneShot=Bool(row,"oneShot");Require(!(Loops&&OneShot),"clip-loop-mode");
            DefaultFacing=NullableText(row,"defaultFacing");EndFacing=NullableText(row,"endFacing");EntryPoseRelation=NullableText(row,"entryPoseRelation");
            var phases=row.GetProperty("compatiblePhases");
            if(phases.ValueKind==JsonValueKind.Null)CompatiblePhases=null;
            else {Require(phases.ValueKind==JsonValueKind.Array&&phases.GetArrayLength()<=32,"phase-list");CompatiblePhases=Array.AsReadOnly(phases.EnumerateArray().Select(p=>TextValue(p,128)).ToArray());}
        }
    }
    internal sealed class Actor
    {
        internal string Id { get; } internal string Path { get; } internal string Sha256 { get; }
        internal int Width { get; } internal int Height { get; } internal int CellWidth { get; } internal int CellHeight { get; }
        internal string MasterId { get; } internal Rect MasterRect { get; } internal Point LocalOrigin { get; }
        internal int DesignPixelsPerUnit { get; } internal int? OriginalDeclaredLogicalPPU { get; }
        internal Point PivotBottomLeftPixels { get; } internal Point SupportCandidateTopLeftPixels { get; }
        internal bool NativeSupportConfirmed => false; internal bool NativeFootprintKnown => false;
        internal bool NativeCalibrationPending => true;
        internal string DrawRole { get; } internal string MotionGroup { get; } internal string AmodalStatus { get; }
        internal IReadOnlyList<Frame> Frames { get; } internal IReadOnlyList<Clip> Clips { get; }
        internal int DistinctAtlasCells { get; }
        internal Actor(JsonElement row,int ppu)
        {
            Keys(row,"id,path,SHA256,size,kind,masterId,masterRect,logicalCell,localOrigin,pixelsPerUnitDesign,originalDeclaredLogicalPPU,pivotBottomLeft,support,interaction,footprint,drawRole,motionGroup,amodalStatus,frames,clips,frameCount,distinctAtlasCells,nativeCalibrationPending,consumerWired,authorPaletteUnchanged");
            Id=Text(row,"id");Require(KnownIds.Contains(Id),"unknown-role");Path=Text(row,"path",256);Require(SafePath(Path),"unsafe-path");Sha256=Hash(row,"SHA256");
            var size=ReadInts(row.GetProperty("size"),2,2);Width=size[0];Height=size[1];Require(Width>0&&Width<=2048&&Height>0&&Height<=2048,"atlas-size");
            Require(Text(row,"kind")=="actor-atlas","kind");MasterId=Text(row,"masterId",256);MasterRect=ReadRect(row.GetProperty("masterRect"));
            Require(MasterRect.Width==Width&&MasterRect.Height==Height&&Inside(MasterRect,new Rect(0,0,Width,Height)),"actor-master-size");
            var cell=ReadInts(row.GetProperty("logicalCell"),2,2);CellWidth=cell[0];CellHeight=cell[1];Require(CellWidth>0&&CellHeight>0&&CellWidth<=Width&&CellHeight<=Height,"cell-size");
            LocalOrigin=ReadPoint(row.GetProperty("localOrigin"));PivotBottomLeftPixels=ReadPoint(row.GetProperty("pivotBottomLeft"));
            DesignPixelsPerUnit=Int(row,"pixelsPerUnitDesign");Require(DesignPixelsPerUnit==ppu,"actor-design-ppu");
            var original=row.GetProperty("originalDeclaredLogicalPPU");if(original.ValueKind==JsonValueKind.Null)OriginalDeclaredLogicalPPU=null;
            else {Require(original.TryGetInt32(out int n)&&n>0,"original-design-ppu");OriginalDeclaredLogicalPPU=n;}
            var support=row.GetProperty("support");Keys(support,"kind,cellPixelTopLeft,nativeConfirmed");
            Require(Text(support,"kind")=="author-cell-pivot-candidate"&&!Bool(support,"nativeConfirmed"),"native-support-unknown");
            SupportCandidateTopLeftPixels=ReadPoint(support.GetProperty("cellPixelTopLeft"));
            var footprint=row.GetProperty("footprint");Keys(footprint,"kind,value");Require(Text(footprint,"kind")=="native-actor-owned-not-inferred-from-alpha"&&footprint.GetProperty("value").ValueKind==JsonValueKind.Null,"native-footprint-unknown");
            var interaction=row.GetProperty("interaction");Require(interaction.ValueKind==JsonValueKind.Array&&interaction.GetArrayLength()==0,"unexpected-interaction");
            DrawRole=Text(row,"drawRole");MotionGroup=Text(row,"motionGroup");AmodalStatus=Text(row,"amodalStatus");
            Require(DrawRole=="actor"&&MotionGroup=="actor-runtime-clock"&&AmodalStatus=="existing-reviewed-complete-frames","actor-semantics");
            Require(Bool(row,"nativeCalibrationPending")&&!Bool(row,"consumerWired")&&Bool(row,"authorPaletteUnchanged"),"actor-status");
            var frames=row.GetProperty("frames");Require(frames.ValueKind==JsonValueKind.Array&&frames.GetArrayLength()>0&&frames.GetArrayLength()<=512,"frame-count");
            var parsed=new List<Frame>();foreach(var frame in frames.EnumerateArray())parsed.Add(new Frame(frame,parsed.Count,this));Frames=Array.AsReadOnly(parsed.ToArray());
            Require(Int(row,"frameCount")==Frames.Count,"declared-frame-count");
            DistinctAtlasCells=Frames.Select(f=>(f.SourceRectTopLeft.X,f.SourceRectTopLeft.Y,f.SourceRectTopLeft.Width,f.SourceRectTopLeft.Height)).Distinct().Count();
            Require(Int(row,"distinctAtlasCells")==DistinctAtlasCells,"distinct-cells");
            var clips=row.GetProperty("clips");Require(clips.ValueKind==JsonValueKind.Object,"clips");var covered=new HashSet<int>();var parsedClips=new List<Clip>();
            foreach(var clip in clips.EnumerateObject()){Require(clip.Name.Length>0&&clip.Name.Length<=64&&parsedClips.Count<32,"clip-budget");parsedClips.Add(new Clip(clip.Name,clip.Value,this,covered));}
            Require(covered.Count==Frames.Count,"clip-coverage");Clips=Array.AsReadOnly(parsedClips.ToArray());
        }
    }

    private static void Require(bool ok,string reason){if(!ok)throw new FormatException(reason);}
    private static void Keys(JsonElement row,string csv)
    {
        Require(row.ValueKind==JsonValueKind.Object,"object");var expected=new HashSet<string>(csv.Split(','),StringComparer.Ordinal);int n=0;
        foreach(var p in row.EnumerateObject()){n++;Require(expected.Contains(p.Name),"unknown-field:"+p.Name);}
        Require(n==expected.Count,"field-count");
    }
    private static int Int(JsonElement row,string key){Require(row.GetProperty(key).TryGetInt32(out int n),"integer:"+key);return n;}
    private static bool Bool(JsonElement row,string key){var e=row.GetProperty(key);Require(e.ValueKind==JsonValueKind.True||e.ValueKind==JsonValueKind.False,"boolean:"+key);return e.GetBoolean();}
    private static string Text(JsonElement row,string key,int max=64)=>TextValue(row.GetProperty(key),max);
    private static string TextValue(JsonElement e,int max){Require(e.ValueKind==JsonValueKind.String,"string");string s=e.GetString();Require(!string.IsNullOrEmpty(s)&&s.Length<=max&&!s.Any(char.IsControl),"string-size");return s;}
    private static string NullableText(JsonElement row,string key)=>row.GetProperty(key).ValueKind==JsonValueKind.Null?null:Text(row,key,512);
    private static string Hash(JsonElement row,string key){string s=Text(row,key,64);Require(s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"sha256");return s;}
    private static int[] ReadInts(JsonElement row,int min,int max)
    {Require(row.ValueKind==JsonValueKind.Array&&row.GetArrayLength()>=min&&row.GetArrayLength()<=max,"int-array");var v=new List<int>();foreach(var e in row.EnumerateArray()){Require(e.TryGetInt32(out int n),"integer-array");v.Add(n);}return v.ToArray();}
    private static Point ReadPoint(JsonElement row)
    {Require(row.ValueKind==JsonValueKind.Array&&row.GetArrayLength()==2,"point");double x=0,y=0;Require(row[0].TryGetDouble(out x)&&double.IsFinite(x)&&row[1].TryGetDouble(out y)&&double.IsFinite(y),"finite-point");return new Point(x,y);}
    private static Rect ReadRect(JsonElement row){int[] r=ReadInts(row,4,4);Require(r[0]>=0&&r[1]>=0&&r[2]>0&&r[3]>0,"rect");return new Rect(r[0],r[1],r[2],r[3]);}
    private static bool Inside(Rect r,Rect b)=>r.X>=b.X&&r.Y>=b.Y&&(long)r.X+r.Width<=(long)b.X+b.Width&&(long)r.Y+r.Height<=(long)b.Y+b.Height;
    private static bool SafePath(string path)=>path.Length<=256&&!path.StartsWith("/",StringComparison.Ordinal)&&path.EndsWith(".png",StringComparison.Ordinal)
        &&path.Split('/').All(s=>s.Length>0&&s!="."&&s!=".."&&s.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'||c=='_'||c=='.'));
    private static bool NoDuplicates(JsonElement e,ref int nodes)
    {
        if(++nodes>100000)return false;
        if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject())if(!names.Add(p.Name)||!NoDuplicates(p.Value,ref nodes))return false;}
        else if(e.ValueKind==JsonValueKind.Array){foreach(var c in e.EnumerateArray())if(!NoDuplicates(c,ref nodes))return false;}
        return true;
    }
}
