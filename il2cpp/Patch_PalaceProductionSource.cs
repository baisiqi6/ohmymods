// Pure bounded reader for the reviewed R12 production registry/layout pair.
// Parses design facts only: no IO, textures, pixels, loaders, native sizing, sorting authority,
// ready/promotion authority or paid-resource gate. Unknown support/footprint/interaction facts
// stay immutable JsonElement clones; they are never rewritten to false/0.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Point=KingdomEnhancedMod.PalaceActorRegistry.Point;
using Rect=KingdomEnhancedMod.PalaceActorRegistry.Rect;

namespace KingdomEnhancedMod;

internal sealed class PalaceProductionSource
{
    internal const int MaxJsonBytes=1024*1024;
    internal const int MaxDepth=32;
    internal const int MaxNodes=100000;
    internal const int MaxAssets=128;
    internal const int MaxPlacements=1024;
    internal const int MaxAssetFrames=512;
    internal const int MaxTimedFrames=4096;
    internal const int MaxPngSize=2048;
    internal const int RequiredPpu=32;
    internal const int RequiredWalkMinX=-200;
    internal const int RequiredWalkMaxX=200;
    internal const int RequiredPixelMinX=20;
    internal const int RequiredPixelMaxX=12820;
    internal const int RequiredGateAxisPixelX=6420;
    internal const int RequiredDesignStreetReferenceY=224;
    internal const int RequiredDistrictCount=11;
    internal const string RequiredRuntimePaidEntryGateOwner="main-runtime";
    internal const string RequiredOutsideWalkScope="decorative-background-outside-walk-domain";
    internal const string RequiredInteractionId="return-to-source";
    internal const string RequiredInteractionPurpose="return-source-island";
    internal const string RequiredInteractionCoordinateSystem="asset-local-top-left-pixels";
    internal const string RegistrySchema="palace400-r12-production/1";
    internal const string LayoutSchema="palace400-r12-layout/1";
    internal const string RequiredPlacementFormula="worldY=measuredNativeReferencePlaneY+supportOffsetYUnits+(supportLocalY-localY)/32";

    private static readonly IReadOnlyList<PalaceFrame> NoFrames=Array.AsReadOnly(Array.Empty<PalaceFrame>());
    private static readonly IReadOnlyList<PalaceClip> NoClips=Array.AsReadOnly(Array.Empty<PalaceClip>());

    private readonly Dictionary<string,Asset> _assetsById;
    private readonly Dictionary<string,Placement> _placementsById;

    internal IReadOnlyList<Asset> Assets { get; }
    internal IReadOnlyList<Placement> Placements { get; }
    internal IReadOnlyList<District> Districts { get; }
    internal IReadOnlyDictionary<string,int> RelativeDrawOrderHints { get; }
    internal string RegistrySha256 { get; }
    internal string LayoutSha256 { get; }
    internal string PaletteSha256 { get; }
    internal IReadOnlyList<string> NativeUnknowns { get; }
    internal IReadOnlyList<string> OfflineReviewPending { get; }
    internal IReadOnlyDictionary<string,double> BackgroundMotionRates { get; }
    internal int DesignPpu { get; }
    internal int WorldDomainMinX { get; }
    internal int WorldDomainMaxX { get; }
    internal int PhysicalPixelDomainMinX { get; }
    internal int PhysicalPixelDomainMaxX { get; }
    internal int GateAxisPixelX { get; }
    internal int DesignStreetReferenceY { get; }
    internal string PlacementFormula { get; }
    internal bool DeclaredReadyForIsolatedIntegration { get; }
    internal string RuntimePaidEntryGateOwner { get; }
    internal bool NativeAccepted=>false;
    internal bool PaidResourceGate=>false;

    private PalaceProductionSource(IReadOnlyList<Asset> assets,IReadOnlyList<Placement> placements,
        IReadOnlyList<District> districts,IReadOnlyDictionary<string,int> hints,string registryHash,string layoutHash,string paletteHash,
        IReadOnlyList<string> nativeUnknowns,IReadOnlyList<string> offlineReviewPending,int ppu,
        int worldMin,int worldMax,int pixelMin,int pixelMax,int gateX,int streetY,string formula,
        bool declaredReady,string paidEntryGateOwner)
    {
        Assets=assets;Placements=placements;Districts=districts;RelativeDrawOrderHints=hints;
        RegistrySha256=registryHash;LayoutSha256=layoutHash;PaletteSha256=paletteHash;
        NativeUnknowns=nativeUnknowns;OfflineReviewPending=offlineReviewPending;
        BackgroundMotionRates=null;DesignPpu=ppu;
        DeclaredReadyForIsolatedIntegration=declaredReady;RuntimePaidEntryGateOwner=paidEntryGateOwner;
        WorldDomainMinX=worldMin;WorldDomainMaxX=worldMax;
        PhysicalPixelDomainMinX=pixelMin;PhysicalPixelDomainMaxX=pixelMax;
        GateAxisPixelX=gateX;DesignStreetReferenceY=streetY;PlacementFormula=formula;
        _assetsById=new Dictionary<string,Asset>(StringComparer.Ordinal);
        foreach(var asset in assets)_assetsById.Add(asset.Id,asset);
        _placementsById=new Dictionary<string,Placement>(StringComparer.Ordinal);
        foreach(var placement in placements)_placementsById.Add(placement.Id,placement);
    }

    internal bool TryGetAsset(string id,out Asset asset)
    {
        if(id!=null&&_assetsById.TryGetValue(id,out asset))return true;
        asset=null;return false;
    }

    internal bool TryGetPlacement(string id,out Placement placement)
    {
        if(id!=null&&_placementsById.TryGetValue(id,out placement))return true;
        placement=null;return false;
    }

    internal static bool TryParse(string registryJson,string layoutJson,out PalaceProductionSource source,out string reason)
    {
        source=null;reason="";
        if(string.IsNullOrEmpty(registryJson)||string.IsNullOrEmpty(layoutJson)){reason="size";return false;}
        if(Encoding.UTF8.GetByteCount(registryJson)>MaxJsonBytes||Encoding.UTF8.GetByteCount(layoutJson)>MaxJsonBytes){reason="size";return false;}
        try
        {
            using var registryDoc=JsonDocument.Parse(registryJson,new JsonDocumentOptions{MaxDepth=MaxDepth});
            using var layoutDoc=JsonDocument.Parse(layoutJson,new JsonDocumentOptions{MaxDepth=MaxDepth});
            string bounded=CheckBounded(registryDoc.RootElement);if(bounded!=null){reason=bounded;return false;}
            bounded=CheckBounded(layoutDoc.RootElement);if(bounded!=null){reason=bounded;return false;}

            var layout=ParseLayoutRoot(layoutDoc.RootElement);
            var registry=ParseRegistry(registryDoc.RootElement,layout.Ppu);
            var assetsById=new Dictionary<string,Asset>(StringComparer.Ordinal);
            foreach(var asset in registry.Assets)assetsById.Add(asset.Id,asset);
            var placements=ParsePlacements(layout.PlacementsNode,assetsById,layout.Districts,layout.Hints,layout.GateAxisPixelX,layout.DesignStreetReferenceY,layout.Ppu);

            string registryHash=Sha256(registryJson),layoutHash=Sha256(layoutJson);
            source=new PalaceProductionSource(
                Array.AsReadOnly(registry.Assets.ToArray()),Array.AsReadOnly(placements.ToArray()),
                Array.AsReadOnly(layout.Districts.ToArray()),new ReadOnlyDictionary<string,int>(layout.Hints),
                registryHash,layoutHash,registry.PaletteSha256,
                Array.AsReadOnly(layout.NativeUnknowns),Array.AsReadOnly(registry.OfflineReviewPending),
                layout.Ppu,layout.WorldMin,layout.WorldMax,layout.PixelMin,layout.PixelMax,
                layout.GateAxisPixelX,layout.DesignStreetReferenceY,layout.Formula,
                registry.DeclaredReady,registry.PaidEntryGateOwner);
            return true;
        }
        catch(JsonException){reason="json";return false;}
        catch(FormatException e){reason=e.Message;return false;}
        catch(InvalidOperationException){reason="type";return false;}
        catch(KeyNotFoundException){reason="missing-field";return false;}
        catch(OverflowException){reason="number-overflow";return false;}
    }

    // ---------- layout ----------

    private sealed class LayoutFacts
    {
        internal int Ppu,WorldMin,WorldMax,PixelMin,PixelMax,GateAxisPixelX,DesignStreetReferenceY;
        internal string Formula;
        internal List<District> Districts;
        internal Dictionary<string,int> Hints;
        internal string[] NativeUnknowns;
        internal JsonElement PlacementsNode;
    }

    private static LayoutFacts ParseLayoutRoot(JsonElement root)
    {
        Keys(root,"schema,worldDomain,pixelsPerUnitDesign,designXAxis,designStreetReferenceY,districts,placementFormula,drawOrderHints,nativeUnknowns,placements,backgroundMotionRates,nativeAccepted");
        Require(Text(root,"schema",64)==LayoutSchema,"layout-schema");
        Require(!Bool(root,"nativeAccepted"),"native-accepted");
        Require(root.GetProperty("backgroundMotionRates").ValueKind==JsonValueKind.Null,"unsupported-background-motion-rates");

        var facts=new LayoutFacts();
        int[] world=ReadInts(root.GetProperty("worldDomain"),2,2);
        Require(world[0]==RequiredWalkMinX&&world[1]==RequiredWalkMaxX,"world-domain");
        facts.WorldMin=world[0];facts.WorldMax=world[1];
        facts.Ppu=Int(root,"pixelsPerUnitDesign");Require(facts.Ppu==RequiredPpu,"design-ppu");
        facts.DesignStreetReferenceY=Int(root,"designStreetReferenceY");Require(facts.DesignStreetReferenceY==RequiredDesignStreetReferenceY,"street-reference");

        var axis=root.GetProperty("designXAxis");Keys(axis,"physicalPixelDomain,gateAxisPixelX");
        int[] pixels=ReadInts(axis.GetProperty("physicalPixelDomain"),2,2);
        Require(pixels[0]==RequiredPixelMinX&&pixels[1]==RequiredPixelMaxX,"physical-pixel-domain");
        facts.PixelMin=pixels[0];facts.PixelMax=pixels[1];
        facts.GateAxisPixelX=Int(axis,"gateAxisPixelX");
        Require(facts.GateAxisPixelX==RequiredGateAxisPixelX,"gate-axis-pixel");
        facts.Districts=ParseDistricts(root.GetProperty("districts"));

        facts.Formula=Text(root,"placementFormula",256);
        Require(facts.Formula==RequiredPlacementFormula,"placement-formula");

        var hints=root.GetProperty("drawOrderHints");Require(hints.ValueKind==JsonValueKind.Object,"draw-order-hints");
        facts.Hints=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var hint in hints.EnumerateObject())
        {
            string name=hint.Name;
            Require(name.Length>0&&name.Length<=64&&!name.Any(char.IsControl),"draw-order-role");
            Require(hint.Value.ValueKind==JsonValueKind.Number,"draw-order-value");
            Require(hint.Value.TryGetInt32(out int value),"draw-order-value");
            Require(facts.Hints.TryAdd(name,value),"duplicate-draw-order-role");
        }
        Require(facts.Hints.Count>0&&facts.Hints.Count<=64,"draw-order-count");

        facts.NativeUnknowns=ReadTextArray(root.GetProperty("nativeUnknowns"),32,"native-unknowns");
        facts.PlacementsNode=root.GetProperty("placements");
        Require(facts.PlacementsNode.ValueKind==JsonValueKind.Array,"placements");
        Require(facts.PlacementsNode.GetArrayLength()>0&&facts.PlacementsNode.GetArrayLength()<=MaxPlacements,"placement-count");
        return facts;
    }

    private static List<District> ParseDistricts(JsonElement node)
    {
        Require(node.ValueKind==JsonValueKind.Array&&node.GetArrayLength()==RequiredDistrictCount,"district-count");
        var districts=new List<District>(RequiredDistrictCount);
        int expectedStart=RequiredPixelMinX;
        int index=0;
        foreach(var row in node.EnumerateArray())
        {
            Keys(row,"id,designPixelDomain");
            int id=Int(row,"id");Require(id==index+1,"district-id");
            int[] domain=ReadInts(row.GetProperty("designPixelDomain"),2,2);
            Require(domain[0]==expectedStart,"district-gap");
            Require(domain[1]>domain[0],"district-domain");
            expectedStart=domain[1];
            districts.Add(new District(id,domain[0],domain[1]));
            index++;
        }
        Require(expectedStart==RequiredPixelMaxX,"district-end");
        return districts;
    }

    private static int DistrictAt(List<District> districts,double designPixelX)
    {
        for(int i=0;i<districts.Count;i++)
        {
            var district=districts[i];
            if(designPixelX>=district.DesignPixelMinX&&designPixelX<district.DesignPixelMaxX)return district.Id;
            if(i==districts.Count-1&&designPixelX==district.DesignPixelMaxX)return district.Id;
        }
        return 0;
    }

    private static List<Placement> ParsePlacements(JsonElement rows,Dictionary<string,Asset> assets,List<District> districts,
        IReadOnlyDictionary<string,int> hints,int gateAxisPixelX,int streetReferenceY,int ppu)
    {
        var counts=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var row in rows.EnumerateArray())
        {
            string assetId=Text(row,"assetId",128);
            counts.TryGetValue(assetId,out int n);counts[assetId]=n+1;
        }
        var placements=new List<Placement>(rows.GetArrayLength());
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in rows.EnumerateArray())
        {
            var placement=ParsePlacement(row,assets,districts,hints,counts,gateAxisPixelX,streetReferenceY,ppu);
            Require(ids.Add(placement.Id),"duplicate-placement");
            placements.Add(placement);
        }
        return placements;
    }

    private static Placement ParsePlacement(JsonElement row,Dictionary<string,Asset> assets,List<District> districts,
        IReadOnlyDictionary<string,int> hints,Dictionary<string,int> counts,int gateAxisPixelX,int streetReferenceY,int ppu)
    {
        KeysAllowed(row,PlacementRequired,PlacementOptional);
        string id=Text(row,"id",128);
        string assetId=Text(row,"assetId",128);
        Require(assets.TryGetValue(assetId,out var asset),"unknown-asset-reference:"+assetId);
        string plane=Text(row,"referencePlane",64);
        Require(plane=="native-ground"||plane=="native-water","reference-plane");
        double worldX=Finite(row,"supportWorldX");
        double offset=Finite(row,"supportOffsetYUnits");
        Point local=ReadPoint(row.GetProperty("supportLocalPixel"));
        Point design=ReadPoint(row.GetProperty("designSupportPixel"));
        int district=Int(row,"district");
        string drawRole=Text(row,"drawRole",64);
        Require(hints.ContainsKey(drawRole),"unknown-draw-role:"+drawRole);
        string motionGroup=Text(row,"motionGroup",64);

        double expectedX=(design.X-gateAxisPixelX)/(double)ppu;
        Require(Math.Abs(expectedX-worldX)<=1e-9,"design-support-x");
        double expectedY=streetReferenceY-offset*ppu;
        Require(Math.Abs(expectedY-design.Y)<=1e-9,"design-support-y");

        bool allowOutside=false;
        string scope=null;
        if(row.TryGetProperty("allowOutsideWalkDomain",out _))
        {
            allowOutside=Bool(row,"allowOutsideWalkDomain");Require(allowOutside,"outside-flag-false");
            scope=Text(row,"placementScope",64);Require(scope==RequiredOutsideWalkScope,"outside-scope");
        }
        else Require(!row.TryGetProperty("placementScope",out _),"scope-without-flag");

        bool designOutside=design.X<RequiredPixelMinX||design.X>RequiredPixelMaxX;
        bool worldOutside=worldX<RequiredWalkMinX||worldX>RequiredWalkMaxX;
        if(allowOutside)
        {
            Require(designOutside&&worldOutside,"outside-flag-inside-domain");
            Require(asset.Kind==AssetKinds.BackgroundCompound,"outside-asset-kind");
            int edge=design.X<RequiredPixelMinX?1:RequiredDistrictCount;
            Require(district==edge,"outside-district");
        }
        else
        {
            Require(!designOutside&&!worldOutside,"unflagged-outside-domain");
            Require(district==DistrictAt(districts,design.X),"district-membership");
        }

        if(asset.Support.TryGetProperty("designOffsetYUnits",out _))
        {
            double assetOffset=Finite(asset.Support,"designOffsetYUnits");
            Require(Math.Abs(assetOffset-offset)<=1e-9,"double-design-offset");
        }

        string behavior=null;
        if(row.TryGetProperty("behavior",out _))behavior=Text(row,"behavior",64);
        double? phaseSeed=null;
        if(row.TryGetProperty("localClockPhaseSeed",out _))phaseSeed=Finite(row,"localClockPhaseSeed");
        ParentSurface parent=null;
        if(row.TryGetProperty("parentSurface",out var parentNode))
        {
            Keys(parentNode,"assetId,localDeckPixel");
            string parentId=Text(parentNode,"assetId",128);
            Require(parentId!=assetId,"parent-self");
            Require(assets.TryGetValue(parentId,out var parentAsset),"unknown-parent-reference:"+parentId);
            Require(counts.TryGetValue(parentId,out int parentCount)&&parentCount==1,"ambiguous-parent-surface:"+parentId);
            Point deck=ReadPoint(parentNode.GetProperty("localDeckPixel"));
            Require(deck.X>=0&&deck.X<parentAsset.Width&&deck.Y>=0&&deck.Y<parentAsset.Height,"parent-deck-bounds");
            parent=new ParentSurface(parentId,deck);
        }
        return new Placement(id,assetId,plane,worldX,offset,local,design,district,allowOutside,scope,drawRole,motionGroup,behavior,phaseSeed,parent);
    }

    // ---------- registry ----------

    private sealed class RegistryFacts
    {
        internal List<Asset> Assets;
        internal string[] OfflineReviewPending;
        internal string PaletteSha256;
        internal bool DeclaredReady;
        internal string PaidEntryGateOwner;
    }

    private static RegistryFacts ParseRegistry(JsonElement root,int ppu)
    {
        Keys(root,"schema,assets,entityCount,backgroundAssetCount,groundModuleCount,actorCount,actorTemporalFrames,localMotionAtlases,localMotionFrames,offlineReviewPending,nativeAccepted,readyForIsolatedIntegration,runtimePaidEntryGateOwner,paletteSHA256");
        Require(Text(root,"schema",64)==RegistrySchema,"registry-schema");
        Require(!Bool(root,"nativeAccepted"),"native-accepted");
        var facts=new RegistryFacts{PaletteSha256=Hash(root,"paletteSHA256")};
        facts.DeclaredReady=Bool(root,"readyForIsolatedIntegration");
        facts.PaidEntryGateOwner=Text(root,"runtimePaidEntryGateOwner",64);
        Require(facts.PaidEntryGateOwner==RequiredRuntimePaidEntryGateOwner,"paid-entry-gate-owner");

        var rows=root.GetProperty("assets");
        Require(rows.ValueKind==JsonValueKind.Array,"assets");
        int count=rows.GetArrayLength();
        Require(count>0&&count<=MaxAssets,"asset-count");
        var assets=new List<Asset>(count);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var masters=new HashSet<string>(StringComparer.Ordinal);
        int entity=0,background=0,ground=0,actor=0,local=0,actorFrames=0,localFrames=0,totalFrames=0;
        foreach(var row in rows.EnumerateArray())
        {
            var asset=ParseAsset(row,ppu);
            Require(ids.Add(asset.Id),"duplicate-asset-id");
            Require(paths.Add(asset.RelativePath),"duplicate-asset-path");
            Require(masters.Add(asset.MasterId),"duplicate-master-id");
            switch(asset.Kind)
            {
                case AssetKinds.EnvironmentEntity:entity++;break;
                case AssetKinds.BackgroundCompound:background++;break;
                case AssetKinds.GroundModule:ground++;break;
                case AssetKinds.ActorAtlas:actor++;actorFrames=checked(actorFrames+asset.Frames.Count);break;
                case AssetKinds.LocalEnvironmentMotionAtlas:local++;localFrames=checked(localFrames+asset.Frames.Count);break;
            }
            totalFrames=checked(totalFrames+asset.Frames.Count);
            Require(totalFrames<=MaxTimedFrames,"frame-budget");
            assets.Add(asset);
        }
        Require(Int(root,"entityCount")==entity,"entity-count");
        Require(Int(root,"backgroundAssetCount")==background,"background-count");
        Require(Int(root,"groundModuleCount")==ground,"ground-count");
        Require(Int(root,"actorCount")==actor,"actor-count");
        Require(Int(root,"actorTemporalFrames")==actorFrames,"actor-frame-count");
        Require(Int(root,"localMotionAtlases")==local,"local-atlas-count");
        Require(Int(root,"localMotionFrames")==localFrames,"local-frame-count");
        facts.Assets=assets;
        facts.OfflineReviewPending=ReadTextArray(root.GetProperty("offlineReviewPending"),64,"offline-review-pending");
        if(facts.DeclaredReady)Require(facts.OfflineReviewPending.Length==0,"ready-with-pending-review");
        return facts;
    }

    private static Asset ParseAsset(JsonElement row,int ppu)
    {
        string kind=Text(row,"kind",64);
        string keys;
        switch(kind)
        {
            case AssetKinds.EnvironmentEntity:
                keys=row.TryGetProperty("provenance",out _)?AssetEnvProvenanceKeys:AssetEnvKeys;break;
            case AssetKinds.BackgroundCompound:keys=AssetBackgroundKeys;break;
            case AssetKinds.GroundModule:keys=AssetGroundKeys;break;
            case AssetKinds.ActorAtlas:keys=AssetActorKeys;break;
            case AssetKinds.LocalEnvironmentMotionAtlas:keys=AssetLocalKeys;break;
            default:throw new FormatException("asset-kind");
        }
        Keys(row,keys);

        string id=Text(row,"id",128);
        string path=Text(row,"path",256);Require(SafePath(path),"unsafe-path");
        string sha=Hash(row,"SHA256");
        int[] size=ReadInts(row.GetProperty("size"),2,2);
        int width=size[0],height=size[1];
        Require(width>0&&width<=MaxPngSize&&height>0&&height<=MaxPngSize,"asset-size");
        Point localOrigin=ReadPoint(row.GetProperty("localOrigin"));
        string masterId=Text(row,"masterId",256);
        Rect masterRect=ReadRect(row.GetProperty("masterRect"));
        Require(masterRect.Width==width&&masterRect.Height==height&&Inside(masterRect,new Rect(0,0,width,height)),"master-rect");
        int assetPpu=Int(row,"pixelsPerUnitDesign");Require(assetPpu==ppu&&assetPpu==RequiredPpu,"asset-design-ppu");
        Require(Text(row,"filterMode",32)=="Point","filter-mode");
        Require(!Bool(row,"nativeAccepted"),"native-accepted");

        JsonElement support=row.GetProperty("support");
        ValidateSupport(support,width,height);
        JsonElement footprint=row.GetProperty("footprint");
        ValidateFootprint(footprint);
        JsonElement interaction=row.GetProperty("interaction");
        InteractionAnchor interactionAnchor=ParseInteraction(interaction,id,width,height);
        JsonElement motion=row.GetProperty("motionGroup");
        ValidateStringOrObject(motion,"motion-group");
        JsonElement amodal=row.GetProperty("amodalStatus");
        ValidateStringOrObject(amodal,"amodal-status");

        JsonElement waterFit=default;
        if(kind==AssetKinds.GroundModule)
        {
            waterFit=row.GetProperty("waterFit");Keys(waterFit,"status,verticalStretchAllowed");
            Text(waterFit,"status",128);Require(!Bool(waterFit,"verticalStretchAllowed"),"water-stretch");
        }
        JsonElement provenance=default;
        if(row.TryGetProperty("provenance",out var source)){ValidateProvenance(source);provenance=source;}

        bool? contained=null;
        if(kind==AssetKinds.EnvironmentEntity)
        {
            bool value=Bool(row,"containedObjectsSeparateReady");Require(!value,"contained-objects-ready");contained=value;
        }

        PalaceActorRegistry.Actor actor=null;
        IReadOnlyList<PalaceFrame> frames=NoFrames;
        IReadOnlyList<PalaceClip> clips=NoClips;
        int? declaredFrameCount=null,declaredDistinct=null,cellWidth=null,cellHeight=null;
        Point? pivot=null;
        int? originalPpu=null;
        bool? paletteUnchanged=null,wired=null,calibrationPending=null;
        if(kind==AssetKinds.ActorAtlas)
        {
            actor=new PalaceActorRegistry.Actor(row,ppu,PalaceActorRegistry.ActorEnvelope.Production);
            Require(actor.Id==id&&actor.Path==path&&actor.Sha256==sha&&actor.Width==width&&actor.Height==height
                &&actor.MasterId==masterId&&actor.MasterRect.X==masterRect.X&&actor.MasterRect.Y==masterRect.Y,"actor-identity");
            frames=FrameView(actor.Frames);clips=ClipView(actor.Clips);
            declaredFrameCount=Int(row,"frameCount");Require(declaredFrameCount.Value==frames.Count,"declared-frame-count");
            declaredDistinct=Int(row,"distinctAtlasCells");Require(declaredDistinct.Value==actor.DistinctAtlasCells,"declared-distinct-cells");
            cellWidth=actor.CellWidth;cellHeight=actor.CellHeight;pivot=actor.PivotBottomLeftPixels;
            originalPpu=actor.OriginalDeclaredLogicalPPU;
            paletteUnchanged=Bool(row,"authorPaletteUnchanged");Require(paletteUnchanged.Value,"author-palette");
            wired=Bool(row,"consumerWired");Require(!wired.Value,"consumer-wired");
            calibrationPending=Bool(row,"nativeCalibrationPending");Require(calibrationPending.Value,"native-calibration-pending");
        }
        else if(kind==AssetKinds.LocalEnvironmentMotionAtlas)
        {
            int[] cell=ReadInts(row.GetProperty("logicalCell"),2,2);
            cellWidth=cell[0];cellHeight=cell[1];
            Require(cellWidth.Value>0&&cellHeight.Value>0&&cellWidth.Value<=width&&cellHeight.Value<=height,"logical-cell");
            pivot=ReadPoint(row.GetProperty("pivotBottomLeft"));
            var localFrames=ParseLocalFrames(row,width,height,cellWidth.Value,cellHeight.Value,masterId,masterRect);
            var localClips=ParseLocalClips(row,localFrames);
            frames=LocalFrameView(localFrames);clips=LocalClipView(localClips);
            declaredFrameCount=Int(row,"frameCount");Require(declaredFrameCount.Value==localFrames.Length,"declared-frame-count");
        }
        else
        {
            var framesNode=row.GetProperty("frames");
            Require(framesNode.ValueKind==JsonValueKind.Array&&framesNode.GetArrayLength()==0,"static-frames");
        }

        return new Asset(id,kind,path,sha,width,height,assetPpu,localOrigin,masterId,masterRect,
            Text(row,"drawRole",64),motion,amodal,support,footprint,interaction,interactionAnchor,waterFit,provenance,contained,
            originalPpu,declaredFrameCount,declaredDistinct,cellWidth,cellHeight,pivot,paletteUnchanged,wired,calibrationPending,
            actor,frames,clips);
    }

    private static LocalMotionFrame[] ParseLocalFrames(JsonElement row,int atlasWidth,int atlasHeight,
        int cellWidth,int cellHeight,string masterId,Rect masterRect)
    {
        var node=row.GetProperty("frames");
        Require(node.ValueKind==JsonValueKind.Array&&node.GetArrayLength()>0&&node.GetArrayLength()<=MaxAssetFrames,"frame-count");
        var frames=new LocalMotionFrame[node.GetArrayLength()];
        int index=0;
        foreach(var frame in node.EnumerateArray())
        {
            Keys(frame,"index,state,phase,durationMs,sourceRect,masterId,masterRect,localOrigin,pivotBottomLeft,contactAnchorCellTopLeft,logicalRGBA_SHA256");
            Require(Int(frame,"index")==index,"frame-order");
            string state=Text(frame,"state",64);
            int phase=Int(frame,"phase");Require(phase>=0&&phase<=MaxAssetFrames,"phase");
            int duration=Int(frame,"durationMs");Require(duration>0,"duration");
            Rect sourceRect=ReadRect(frame.GetProperty("sourceRect"));
            Require(Inside(sourceRect,new Rect(0,0,atlasWidth,atlasHeight)),"source-rect-bounds");
            Require(sourceRect.Width==cellWidth&&sourceRect.Height==cellHeight,"logical-cell");
            Rect unityRect=new Rect(sourceRect.X,atlasHeight-sourceRect.Y-sourceRect.Height,sourceRect.Width,sourceRect.Height);
            string frameMaster=Text(frame,"masterId",256);Require(frameMaster==masterId,"frame-master-id");
            Rect frameMasterRect=ReadRect(frame.GetProperty("masterRect"));
            Require(Inside(frameMasterRect,masterRect)&&frameMasterRect.Width==sourceRect.Width&&frameMasterRect.Height==sourceRect.Height,"master-rect-bounds");
            Point localOrigin=ReadPoint(frame.GetProperty("localOrigin"));
            Point pivot=ReadPoint(frame.GetProperty("pivotBottomLeft"));
            Point contact=ReadPoint(frame.GetProperty("contactAnchorCellTopLeft"));
            frames[index]=new LocalMotionFrame(index,state,phase,duration,sourceRect,unityRect,
                frameMaster,frameMasterRect,localOrigin,pivot,contact,Hash(frame,"logicalRGBA_SHA256"));
            index++;
        }
        return frames;
    }

    private static LocalMotionClip[] ParseLocalClips(JsonElement row,LocalMotionFrame[] frames)
    {
        var node=row.GetProperty("clips");
        Require(node.ValueKind==JsonValueKind.Array&&node.GetArrayLength()>0&&node.GetArrayLength()<=32,"clips");
        var clips=new List<LocalMotionClip>(node.GetArrayLength());
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var covered=new HashSet<int>();
        foreach(var clip in node.EnumerateArray())
        {
            Keys(clip,"id,indices,durationMs,loop,maxPixelDisplacement,distinctPhysicalPoses");
            string id=Text(clip,"id",64);Require(ids.Add(id),"duplicate-clip");
            int[] indices=ReadInts(clip.GetProperty("indices"),1,MaxAssetFrames);
            int previous=-1;long total=0;
            foreach(int ix in indices)
            {
                Require(ix>previous&&ix>=0&&ix<frames.Length&&covered.Add(ix),"clip-order-index");
                Require(frames[ix].State==id,"clip-frame-mismatch");
                previous=ix;total+=frames[ix].DurationMs;
            }
            Require(clip.GetProperty("durationMs").TryGetInt32(out int declared)&&declared==total,"clip-duration");
            bool loop=Bool(clip,"loop");
            int displacement=Int(clip,"maxPixelDisplacement");Require(displacement>=0,"max-displacement");
            int poses=Int(clip,"distinctPhysicalPoses");Require(poses>0&&poses<=indices.Length,"distinct-poses");
            int[] durations=indices.Select(i=>frames[i].DurationMs).ToArray();
            clips.Add(new LocalMotionClip(id,Array.AsReadOnly(indices),Array.AsReadOnly(durations),total,loop,displacement,poses));
        }
        Require(covered.Count==frames.Length,"clip-coverage");
        return clips.ToArray();
    }

    // ---------- validation helpers ----------

    private static InteractionAnchor ParseInteraction(JsonElement interaction,string assetId,int width,int height)
    {
        Require(interaction.ValueKind==JsonValueKind.Array,"interaction");
        if(assetId!="central-palace-gate")
        {
            Require(interaction.GetArrayLength()==0,"unsupported-interaction:"+assetId);
            return null;
        }
        Require(interaction.GetArrayLength()==1,"interaction-count");
        var row=interaction[0];
        Keys(row,"id,purpose,coordinateSystem,point,meaning,nativeConfirmed,chargeCoins,runtimeTriggerExtent,runtimeResponsibility");
        Require(Text(row,"id",64)==RequiredInteractionId,"interaction-id");
        Require(Text(row,"purpose",64)==RequiredInteractionPurpose,"interaction-purpose");
        Require(Text(row,"coordinateSystem",64)==RequiredInteractionCoordinateSystem,"interaction-coordinate-system");
        Point point=ReadPoint(row.GetProperty("point"));
        Require(point.X>=0&&point.X<width&&point.Y>=0&&point.Y<height,"interaction-point-bounds");
        string meaning=Text(row,"meaning",256);
        Require(!Bool(row,"nativeConfirmed"),"interaction-native-claim");
        Require(Int(row,"chargeCoins")==0,"interaction-charge");
        JsonElement extent=row.GetProperty("runtimeTriggerExtent");
        Require(extent.ValueKind==JsonValueKind.Null,"interaction-trigger-extent");
        string responsibility=Text(row,"runtimeResponsibility",512);
        return new InteractionAnchor(RequiredInteractionId,RequiredInteractionPurpose,RequiredInteractionCoordinateSystem,
            point,meaning,false,0,extent.Clone(),responsibility);
    }

    private static void ValidateSupport(JsonElement support,int width,int height)
    {
        Require(support.ValueKind==JsonValueKind.Object&&support.EnumerateObject().Any(),"support");
        string kind=Text(support,"kind",128);
        if(kind=="authored-construction-top-edge")
        {
            Keys(support,"kind,point,nativeConfirmed,designOffsetYUnits,meaning");
            Point top=ReadPoint(support.GetProperty("point"));
            Require(top.Y==0&&top.X>=0&&top.X<width,"construction-top-edge");
            Require(!Bool(support,"nativeConfirmed"),"native-support-claim");
            Finite(support,"designOffsetYUnits");
            Text(support,"meaning",256);
            return;
        }
        if(kind=="airborne-compound")
        {
            Keys(support,"kind,point,nativeConfirmed,meaning");
            Point airPoint=ReadPoint(support.GetProperty("point"));
            Require(airPoint.X>=0&&airPoint.X<=width&&airPoint.Y>=0&&airPoint.Y<=height,"airborne-support-bounds");
            Require(!Bool(support,"nativeConfirmed"),"native-support-claim");
            Text(support,"meaning",256);
            return;
        }
        if(support.TryGetProperty("nativeConfirmed",out _))Require(!Bool(support,"nativeConfirmed"),"native-support-claim");
        if(support.TryGetProperty("point",out var point)&&point.ValueKind!=JsonValueKind.Null)ReadPoint(point);
        if(support.TryGetProperty("cellPixelTopLeft",out var cell))ReadPoint(cell);
        if(support.TryGetProperty("sourcePixelBoundaryTopLeft",out var boundary))ReadPoint(boundary);
        if(support.TryGetProperty("sourceAuthorDeclaredAnchor",out _))Bool(support,"sourceAuthorDeclaredAnchor");
    }

    private static void ValidateFootprint(JsonElement footprint)
    {
        Require(footprint.ValueKind==JsonValueKind.Object&&footprint.EnumerateObject().Any(),"footprint");
        Text(footprint,"kind",128);
        if(footprint.TryGetProperty("value",out var value))
            Require(value.ValueKind==JsonValueKind.Null,"unsupported-footprint-value");
        if(footprint.TryGetProperty("isNativeCollider",out _))
            Require(!Bool(footprint,"isNativeCollider"),"native-collider-claim");
        if(footprint.TryGetProperty("span",out var span))
        {
            int[] range=ReadInts(span,2,2);Require(range[0]>=0&&range[0]<=range[1],"footprint-span");
        }
        if(footprint.TryGetProperty("points",out var points))
        {
            Require(points.ValueKind==JsonValueKind.Array&&points.GetArrayLength()<=32,"footprint-points");
            foreach(var p in points.EnumerateArray())ReadPoint(p);
        }
    }

    private static void ValidateProvenance(JsonElement provenance)
    {
        Require(provenance.ValueKind==JsonValueKind.Object,"provenance");
        var names=new HashSet<string>(provenance.EnumerateObject().Select(p=>p.Name),StringComparer.Ordinal);
        if(names.SetEquals(new[]{"R11SourceRect","R11SHA256"}))
        {
            Rect rect=ReadRect(provenance.GetProperty("R11SourceRect"));
            Require(rect.X>=0&&rect.Y>=0,"provenance-rect");
            Hash(provenance,"R11SHA256");
            return;
        }
        if(names.SetEquals(new[]{"amodalSourceSHA256","uniformScaleXY"}))
        {
            Hash(provenance,"amodalSourceSHA256");
            JsonElement scale=provenance.GetProperty("uniformScaleXY");
            Require(scale.ValueKind==JsonValueKind.Array&&scale.GetArrayLength()==2,"provenance-scale");
            foreach(var e in scale.EnumerateArray())Require(e.TryGetDouble(out double n)&&double.IsFinite(n)&&n>0,"provenance-scale");
            return;
        }
        throw new FormatException("provenance-shape");
    }

    private static void ValidateStringOrObject(JsonElement value,string reason)
    {
        if(value.ValueKind==JsonValueKind.String){TextValue(value,512);return;}
        Require(value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Any(),reason);
    }

    private static void KeysAllowed(JsonElement row,string[] required,string[] optional)
    {
        Require(row.ValueKind==JsonValueKind.Object,"object");
        var expected=new HashSet<string>(required,StringComparer.Ordinal);
        expected.UnionWith(optional);
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in row.EnumerateObject())
        {
            Require(expected.Contains(property.Name),"unknown-field:"+property.Name);
            Require(seen.Add(property.Name),"duplicate-field:"+property.Name);
        }
        foreach(string name in required)Require(seen.Contains(name),"missing-field:"+name);
    }

    private static void Require(bool ok,string reason){if(!ok)throw new FormatException(reason);}

    private static void Keys(JsonElement row,string csv)
    {
        Require(row.ValueKind==JsonValueKind.Object,"object");
        var expected=new HashSet<string>(csv.Split(','),StringComparer.Ordinal);
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in row.EnumerateObject())
        {
            Require(expected.Contains(property.Name),"unknown-field:"+property.Name);
            Require(seen.Add(property.Name),"duplicate-field:"+property.Name);
        }
        Require(seen.Count==expected.Count,"field-count");
    }

    private static int Int(JsonElement row,string key){Require(row.GetProperty(key).TryGetInt32(out int n),"integer:"+key);return n;}
    private static double Finite(JsonElement row,string key)
    {
        Require(row.GetProperty(key).TryGetDouble(out double n)&&double.IsFinite(n),"finite:"+key);return n;
    }
    private static bool Bool(JsonElement row,string key)
    {
        var e=row.GetProperty(key);Require(e.ValueKind==JsonValueKind.True||e.ValueKind==JsonValueKind.False,"boolean:"+key);return e.GetBoolean();
    }
    private static string Text(JsonElement row,string key,int max=64)=>TextValue(row.GetProperty(key),max);
    private static string TextValue(JsonElement e,int max)
    {
        Require(e.ValueKind==JsonValueKind.String,"string");
        string s=e.GetString();
        Require(!string.IsNullOrEmpty(s)&&s.Length<=max&&!s.Any(char.IsControl),"string-size");
        return s;
    }
    private static string Hash(JsonElement row,string key)
    {
        string s=Text(row,key,64);
        Require(s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"sha256");
        return s;
    }
    private static int[] ReadInts(JsonElement row,int min,int max)
    {
        Require(row.ValueKind==JsonValueKind.Array&&row.GetArrayLength()>=min&&row.GetArrayLength()<=max,"int-array");
        var values=new List<int>(row.GetArrayLength());
        foreach(var e in row.EnumerateArray()){Require(e.TryGetInt32(out int n),"integer-array");values.Add(n);}
        return values.ToArray();
    }
    private static Point ReadPoint(JsonElement row)
    {
        Require(row.ValueKind==JsonValueKind.Array&&row.GetArrayLength()==2,"point");
        Require(row[0].TryGetDouble(out double x)&&double.IsFinite(x),"finite-point-x");
        Require(row[1].TryGetDouble(out double y)&&double.IsFinite(y),"finite-point-y");
        return new Point(x,y);
    }
    private static Rect ReadRect(JsonElement row)
    {
        int[] r=ReadInts(row,4,4);
        Require(r[0]>=0&&r[1]>=0&&r[2]>0&&r[3]>0,"rect");
        return new Rect(r[0],r[1],r[2],r[3]);
    }
    private static bool Inside(Rect r,Rect bounds)
        =>r.X>=bounds.X&&r.Y>=bounds.Y&&(long)r.X+r.Width<=(long)bounds.X+bounds.Width&&(long)r.Y+r.Height<=(long)bounds.Y+bounds.Height;
    private static bool SafePath(string path)
        =>path.Length<=256&&!path.StartsWith("/",StringComparison.Ordinal)&&path.EndsWith(".png",StringComparison.Ordinal)
        &&path.Split('/').All(s=>s.Length>0&&s!="."&&s!=".."&&s.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'||c=='_'||c=='.'));

    private static string[] ReadTextArray(JsonElement node,int max,string reason)
    {
        Require(node.ValueKind==JsonValueKind.Array&&node.GetArrayLength()<=max,reason);
        var values=new List<string>(node.GetArrayLength());
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var e in node.EnumerateArray())
        {
            string value=TextValue(e,128);
            Require(seen.Add(value),"duplicate-text:"+reason);
            values.Add(value);
        }
        return values.ToArray();
    }

    private static string CheckBounded(JsonElement root)
    {
        int nodes=0;
        return Walk(root,0,ref nodes);
    }

    private static string Walk(JsonElement element,int depth,ref int nodes)
    {
        if(++nodes>MaxNodes)return "node-budget";
        if(depth>MaxDepth)return "depth-budget";
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in element.EnumerateObject())
            {
                if(!names.Add(property.Name))return "duplicate-key";
                string reason=Walk(property.Value,depth+1,ref nodes);
                if(reason!=null)return reason;
            }
        }
        else if(element.ValueKind==JsonValueKind.Array)
        {
            foreach(var child in element.EnumerateArray())
            {
                string reason=Walk(child,depth+1,ref nodes);
                if(reason!=null)return reason;
            }
        }
        return null;
    }

    private static string Sha256(string text)
        =>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static IReadOnlyList<PalaceFrame> FrameView(IReadOnlyList<PalaceActorRegistry.Frame> frames)
    {
        var view=new PalaceFrame[frames.Count];
        for(int i=0;i<frames.Count;i++)view[i]=frames[i];
        return Array.AsReadOnly(view);
    }

    private static IReadOnlyList<PalaceClip> ClipView(IReadOnlyList<PalaceActorRegistry.Clip> clips)
    {
        var view=new PalaceClip[clips.Count];
        for(int i=0;i<clips.Count;i++)view[i]=clips[i];
        return Array.AsReadOnly(view);
    }

    private static IReadOnlyList<PalaceFrame> LocalFrameView(LocalMotionFrame[] frames)
    {
        var view=new PalaceFrame[frames.Length];
        for(int i=0;i<frames.Length;i++)view[i]=frames[i];
        return Array.AsReadOnly(view);
    }

    private static IReadOnlyList<PalaceClip> LocalClipView(LocalMotionClip[] clips)
    {
        var view=new PalaceClip[clips.Length];
        for(int i=0;i<clips.Length;i++)view[i]=clips[i];
        return Array.AsReadOnly(view);
    }

    private static readonly string[] PlacementRequired=
    {
        "id","assetId","referencePlane","supportWorldX","supportOffsetYUnits","supportLocalPixel","designSupportPixel","district","drawRole","motionGroup"
    };
    private static readonly string[] PlacementOptional=
    {
        "behavior","localClockPhaseSeed","parentSurface","allowOutsideWalkDomain","placementScope"
    };

    private const string AssetEnvKeys="SHA256,amodalStatus,containedObjectsSeparateReady,drawRole,filterMode,footprint,frames,id,interaction,kind,localOrigin,masterId,masterRect,motionGroup,nativeAccepted,path,pixelsPerUnitDesign,size,support";
    private const string AssetEnvProvenanceKeys=AssetEnvKeys+",provenance";
    private const string AssetBackgroundKeys="SHA256,amodalStatus,drawRole,filterMode,footprint,frames,id,interaction,kind,localOrigin,masterId,masterRect,motionGroup,nativeAccepted,path,pixelsPerUnitDesign,size,support";
    private const string AssetGroundKeys=AssetBackgroundKeys+",waterFit";
    private const string AssetActorKeys="SHA256,amodalStatus,authorPaletteUnchanged,clips,consumerWired,distinctAtlasCells,drawRole,filterMode,footprint,frameCount,frames,id,interaction,kind,localOrigin,logicalCell,masterId,masterRect,motionGroup,nativeAccepted,nativeCalibrationPending,originalDeclaredLogicalPPU,path,pivotBottomLeft,pixelsPerUnitDesign,size,support";
    private const string AssetLocalKeys="SHA256,amodalStatus,clips,drawRole,filterMode,footprint,frameCount,frames,id,interaction,kind,localOrigin,logicalCell,masterId,masterRect,motionGroup,nativeAccepted,path,pivotBottomLeft,pixelsPerUnitDesign,size,support";

    internal static class AssetKinds
    {
        internal const string EnvironmentEntity="environment-entity";
        internal const string BackgroundCompound="background-compound";
        internal const string GroundModule="ground-module";
        internal const string ActorAtlas="actor-atlas";
        internal const string LocalEnvironmentMotionAtlas="local-environment-motion-atlas";
    }

    internal sealed class Asset
    {
        internal string Id { get; } internal string Kind { get; } internal string RelativePath { get; }
        internal string DeclaredPngSha256 { get; }
        internal int Width { get; } internal int Height { get; } internal int DesignPpu { get; }
        internal Point LocalOrigin { get; }
        internal string MasterId { get; } internal Rect MasterRect { get; }
        internal string AssetDrawRole { get; }
        internal JsonElement MotionGroup { get; } internal JsonElement AmodalStatus { get; }
        internal JsonElement Support { get; } internal JsonElement Footprint { get; } internal JsonElement Interaction { get; }
        internal InteractionAnchor InteractionAnchor { get; }
        internal JsonElement WaterFit { get; } internal JsonElement Provenance { get; }
        internal bool? ContainedObjectsSeparateReady { get; }
        internal int? OriginalDeclaredLogicalPpu { get; }
        internal int? DeclaredFrameCount { get; } internal int? DeclaredDistinctAtlasCells { get; }
        internal int? LogicalCellWidth { get; } internal int? LogicalCellHeight { get; }
        internal Point? PivotBottomLeftPixels { get; }
        internal bool? AuthorPaletteUnchanged { get; } internal bool? ConsumerWired { get; } internal bool? NativeCalibrationPending { get; }
        internal PalaceActorRegistry.Actor Actor { get; }
        internal IReadOnlyList<PalaceFrame> Frames { get; }
        internal IReadOnlyList<PalaceClip> Clips { get; }

        internal Asset(string id,string kind,string relativePath,string declaredPngSha256,int width,int height,int designPpu,
            Point localOrigin,string masterId,Rect masterRect,string assetDrawRole,JsonElement motionGroup,JsonElement amodalStatus,
            JsonElement support,JsonElement footprint,JsonElement interaction,InteractionAnchor interactionAnchor,JsonElement waterFit,JsonElement provenance,
            bool? containedObjectsSeparateReady,int? originalDeclaredLogicalPpu,int? declaredFrameCount,int? declaredDistinctAtlasCells,
            int? logicalCellWidth,int? logicalCellHeight,Point? pivotBottomLeftPixels,bool? authorPaletteUnchanged,bool? consumerWired,
            bool? nativeCalibrationPending,PalaceActorRegistry.Actor actor,IReadOnlyList<PalaceFrame> frames,IReadOnlyList<PalaceClip> clips)
        {
            Id=id;Kind=kind;RelativePath=relativePath;DeclaredPngSha256=declaredPngSha256;Width=width;Height=height;DesignPpu=designPpu;
            LocalOrigin=localOrigin;MasterId=masterId;MasterRect=masterRect;AssetDrawRole=assetDrawRole;
            MotionGroup=motionGroup.Clone();AmodalStatus=amodalStatus.Clone();Support=support.Clone();Footprint=footprint.Clone();
            Interaction=interaction.Clone();InteractionAnchor=interactionAnchor;WaterFit=waterFit.ValueKind==JsonValueKind.Undefined?default:waterFit.Clone();
            Provenance=provenance.ValueKind==JsonValueKind.Undefined?default:provenance.Clone();
            ContainedObjectsSeparateReady=containedObjectsSeparateReady;OriginalDeclaredLogicalPpu=originalDeclaredLogicalPpu;
            DeclaredFrameCount=declaredFrameCount;DeclaredDistinctAtlasCells=declaredDistinctAtlasCells;
            LogicalCellWidth=logicalCellWidth;LogicalCellHeight=logicalCellHeight;PivotBottomLeftPixels=pivotBottomLeftPixels;
            AuthorPaletteUnchanged=authorPaletteUnchanged;ConsumerWired=consumerWired;NativeCalibrationPending=nativeCalibrationPending;
            Actor=actor;Frames=frames;Clips=clips;
        }
    }

    internal sealed class Placement
    {
        internal string Id { get; } internal string AssetId { get; } internal string ReferencePlane { get; }
        internal double SupportWorldX { get; } internal double SupportOffsetYUnits { get; }
        internal Point SupportLocalPixel { get; }
        internal Point DesignSupportPixel { get; }
        internal int District { get; }
        internal bool AllowOutsideWalkDomain { get; }
        internal string PlacementScope { get; }
        internal string DrawRole { get; } internal string MotionGroup { get; }
        internal string Behavior { get; }
        internal double? LocalClockPhaseSeed { get; }
        internal ParentSurface ParentSurface { get; }
        internal Placement(string id,string assetId,string referencePlane,double supportWorldX,double supportOffsetYUnits,
            Point supportLocalPixel,Point designSupportPixel,int district,bool allowOutsideWalkDomain,string placementScope,
            string drawRole,string motionGroup,string behavior,double? localClockPhaseSeed,ParentSurface parentSurface)
        {
            Id=id;AssetId=assetId;ReferencePlane=referencePlane;SupportWorldX=supportWorldX;SupportOffsetYUnits=supportOffsetYUnits;
            SupportLocalPixel=supportLocalPixel;DesignSupportPixel=designSupportPixel;District=district;
            AllowOutsideWalkDomain=allowOutsideWalkDomain;PlacementScope=placementScope;
            DrawRole=drawRole;MotionGroup=motionGroup;Behavior=behavior;LocalClockPhaseSeed=localClockPhaseSeed;ParentSurface=parentSurface;
        }
    }

    internal sealed class InteractionAnchor
    {
        internal string Id { get; } internal string Purpose { get; } internal string CoordinateSystem { get; }
        internal Point Point { get; } internal string Meaning { get; }
        internal bool NativeConfirmed { get; } internal int ChargeCoins { get; }
        internal JsonElement RuntimeTriggerExtent { get; } internal string RuntimeResponsibility { get; }
        internal InteractionAnchor(string id,string purpose,string coordinateSystem,Point point,string meaning,bool nativeConfirmed,
            int chargeCoins,JsonElement runtimeTriggerExtent,string runtimeResponsibility)
        {
            Id=id;Purpose=purpose;CoordinateSystem=coordinateSystem;Point=point;Meaning=meaning;
            NativeConfirmed=nativeConfirmed;ChargeCoins=chargeCoins;RuntimeTriggerExtent=runtimeTriggerExtent.Clone();
            RuntimeResponsibility=runtimeResponsibility;
        }
    }

    internal sealed class District
    {
        internal int Id { get; } internal int DesignPixelMinX { get; } internal int DesignPixelMaxX { get; }
        internal District(int id,int designPixelMinX,int designPixelMaxX)
        {
            Id=id;DesignPixelMinX=designPixelMinX;DesignPixelMaxX=designPixelMaxX;
        }
    }

    internal sealed class ParentSurface
    {
        internal string AssetId { get; } internal Point LocalDeckPixel { get; }
        internal ParentSurface(string assetId,Point localDeckPixel){AssetId=assetId;LocalDeckPixel=localDeckPixel;}
    }

    internal sealed class LocalMotionFrame : PalaceFrame
    {
        internal int Index { get; } internal string State { get; } internal int PhaseIndex { get; } internal int DurationMs { get; }
        internal Rect SourceRectTopLeft { get; } internal Rect UnitySourceRectBottomLeft { get; }
        internal string MasterId { get; } internal Rect MasterRect { get; }
        internal Point LocalOriginPixels { get; } internal Point PivotBottomLeftPixels { get; }
        internal Point ContactAnchorCellTopLeftPixels { get; } internal string LogicalRgbaSha256 { get; }
        internal LocalMotionFrame(int index,string state,int phase,int durationMs,Rect sourceRectTopLeft,Rect unitySourceRectBottomLeft,
            string masterId,Rect masterRect,Point localOriginPixels,Point pivotBottomLeftPixels,Point contactAnchorCellTopLeftPixels,string logicalRgbaSha256)
        {
            Index=index;State=state;PhaseIndex=phase;DurationMs=durationMs;SourceRectTopLeft=sourceRectTopLeft;
            UnitySourceRectBottomLeft=unitySourceRectBottomLeft;MasterId=masterId;MasterRect=masterRect;
            LocalOriginPixels=localOriginPixels;PivotBottomLeftPixels=pivotBottomLeftPixels;
            ContactAnchorCellTopLeftPixels=contactAnchorCellTopLeftPixels;LogicalRgbaSha256=logicalRgbaSha256;
        }
        int PalaceFrame.Index=>Index;
        string PalaceFrame.State=>State;
        int PalaceFrame.DurationMs=>DurationMs;
        Rect PalaceFrame.SourceRectTopLeft=>SourceRectTopLeft;
        Rect PalaceFrame.UnitySourceRectBottomLeft=>UnitySourceRectBottomLeft;
        string PalaceFrame.MasterId=>MasterId;
        Rect PalaceFrame.MasterRect=>MasterRect;
        Point PalaceFrame.PivotBottomLeftPixels=>PivotBottomLeftPixels;
        string PalaceFrame.LogicalRgbaSha256=>LogicalRgbaSha256;
        int? PalaceFrame.Phase=>PhaseIndex;
        Point? PalaceFrame.LocalOriginPixels=>LocalOriginPixels;
        Point? PalaceFrame.ContactAnchorCellTopLeftPixels=>ContactAnchorCellTopLeftPixels;
    }

    internal sealed class LocalMotionClip : PalaceClip
    {
        internal string Id { get; } internal IReadOnlyList<int> Indices { get; } internal IReadOnlyList<int> DurationsMs { get; }
        internal long TotalDurationMs { get; } internal bool Loops { get; }
        internal int MaxPixelDisplacement { get; } internal int DistinctPhysicalPoses { get; }
        internal LocalMotionClip(string id,IReadOnlyList<int> indices,IReadOnlyList<int> durationsMs,long totalDurationMs,
            bool loops,int maxPixelDisplacement,int distinctPhysicalPoses)
        {
            Id=id;Indices=indices;DurationsMs=durationsMs;TotalDurationMs=totalDurationMs;Loops=loops;
            MaxPixelDisplacement=maxPixelDisplacement;DistinctPhysicalPoses=distinctPhysicalPoses;
        }
        string PalaceClip.Id=>Id;
        IReadOnlyList<int> PalaceClip.Indices=>Indices;
        IReadOnlyList<int> PalaceClip.DurationsMs=>DurationsMs;
        long PalaceClip.TotalDurationMs=>TotalDurationMs;
        bool PalaceClip.Loops=>Loops;
    }
}
