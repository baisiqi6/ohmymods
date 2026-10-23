// Pure Capital400 runtime policy: one-shot managed package cache, explicit visual inputs,
// reviewed depth grouping and the scaled-clock rule. No Unity type, no IO at read time, no
// Native/visual claim. Warm is the only IO entry and publishes a package only after the S2a
// loader verified the complete 92-name bundle; every failure keeps package=null with a stable
// reason and is never retried by the production one-shot path.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;

namespace KingdomEnhancedMod;

internal static class PalaceCapitalRuntime
{
    internal const int RequiredPlacementCount=452;
    internal const int RequiredAssetCount=86;
    internal const int RequiredImageCount=86;
    internal const int RequiredRearCount=393;
    internal const int RequiredActorCount=10;
    internal const int RequiredFrontCount=49;

    private static readonly object Gate=new object();
    private static bool _warmAttempted;
    private static volatile PalaceCapitalPackage _package;
    private static volatile string _reason="warm-not-run";

    internal static readonly PalaceScrollDescriptor CapitalDescriptor=new PalaceScrollDescriptor(
        PalaceLayoutRecipe.Capital400,12840,252,224,0,0,12840,252);

    internal const double WaterReferenceY=0d;

    private static readonly IReadOnlyDictionary<string,double> ActorScales=BuildActorScales();
    private static readonly IReadOnlyDictionary<string,string> Clips=BuildClips();
    private static readonly IReadOnlyDictionary<string,double> BackgroundRatios=BuildBackgroundRatios();
    private static readonly IReadOnlyDictionary<string,int> ExpectedHints=new ReadOnlyDictionary<string,int>(
        new Dictionary<string,int>(StringComparer.Ordinal)
        {
            {"far-palace-skyline",0},
            {"east-celestial-vista",1},
            {"mid-courtyard-forest",10},
            {"rear-courtyard-architecture",20},
            {"actorBehind",30},
            {"quayFront",35},
            {"shoreEndcap",40},
            {"actor",50},
            {"actorFront",65},
            {"waterFront",80},
        });

    internal static IReadOnlyDictionary<string,double> ActorYScaleById=>ActorScales;
    internal static IReadOnlyDictionary<string,string> ClipByAssetId=>Clips;
    internal static IReadOnlyDictionary<string,double> BackgroundFollowRatioByGroup=>BackgroundRatios;
    internal static IReadOnlyDictionary<string,int> ReviewedDrawOrderHints=>ExpectedHints;

    internal enum PalaceCapitalVisualRoute
    {
        Legacy48=0,
        Known44=1,
        Capital400=2,
        Fault=3,
    }

    /// <summary>
    /// Three-state dispatch for the dedicated whole-island layouts. Non-dedicated (legacy48)
    /// keeps its original path. A dedicated world must yield one exact known view; an unknown
    /// or unavailable view is a fault, never a fallback to Known44/legacy.
    /// </summary>
    internal static PalaceCapitalVisualRoute ClassifyVisualRoute(bool dedicatedWhole,bool viewValid,
        PalaceLayoutRecipe recipe)
    {
        if(!dedicatedWhole)return PalaceCapitalVisualRoute.Legacy48;
        if(!viewValid||recipe==null)return PalaceCapitalVisualRoute.Fault;
        if(ReferenceEquals(recipe,PalaceLayoutRecipe.Capital400))return PalaceCapitalVisualRoute.Capital400;
        if(ReferenceEquals(recipe,PalaceLayoutRecipe.Known44))return PalaceCapitalVisualRoute.Known44;
        return PalaceCapitalVisualRoute.Fault;
    }

    /// <summary>
    /// Actual float worldZ envelope of the produced 452 map. Tick computes
    /// float(worldZ - currentCameraZ) from these frozen endpoints only after cameraZ itself is
    /// proven unchanged; the 452 map is never rebuilt or re-sorted.
    /// </summary>
    internal static bool TryGetWorldZEnvelope(IReadOnlyDictionary<string,double> depthByPlacement,
        out float minWorldZ,out float maxWorldZ,out string reason)
    {
        minWorldZ=0f;maxWorldZ=0f;reason="";
        if(depthByPlacement==null||depthByPlacement.Count!=RequiredPlacementCount)
        {reason="envelope-count";return false;}
        bool any=false;
        foreach(double value in depthByPlacement.Values)
        {
            if(!IsFinite(value)||!float.IsFinite((float)value)){reason="envelope-world";return false;}
            float world=(float)value;
            if(!any){minWorldZ=world;maxWorldZ=world;any=true;continue;}
            if(world<minWorldZ)minWorldZ=world;
            if(world>maxWorldZ)maxWorldZ=world;
        }
        if(!any){reason="envelope-empty";return false;}
        return true;
    }

    internal static void Warm()
    {
        // Settled check happens before any manifest enumeration. WarmOnce is re-entrant under
        // this same Monitor and keeps the one-shot failure latch; after settle this method has
        // zero enumeration and zero IO.
        lock(Gate)
        {
            if(_warmAttempted)return;
            try
            {
                Assembly assembly=Assembly.GetExecutingAssembly();
                if(assembly==null){_warmAttempted=true;_package=null;_reason="warm-assembly-unavailable";return;}
                string[] names=assembly.GetManifestResourceNames();
                if(names==null){_warmAttempted=true;_package=null;_reason="warm-assembly-unavailable";return;}
                WarmOnce(logicalName=>assembly.GetManifestResourceStream(logicalName),names,out _);
            }
            catch(Exception)
            {
                _warmAttempted=true;
                _package=null;
                _reason="warm-assembly-unavailable";
            }
        }
    }

    // Production uses Warm once per process. This seam keeps the same lock/one-shot semantics so
    // a failed first attempt stays unavailable even if the caller later supplies good streams.
    internal static bool WarmOnce(Func<string,Stream> openResource,IReadOnlyCollection<string> availableNames,
        out string reason)
    {
        lock(Gate)
        {
            if(_warmAttempted)
            {
                reason=_reason;
                return _package!=null;
            }
            _warmAttempted=true;
            _package=null;
            try
            {
                if(!PalaceCapitalResources.TryLoad(openResource,availableNames,out PalaceCapitalPackage package,out string detail)
                    ||!MatchesReviewedPackage(package,out detail))
                {
                    _reason="package-unavailable:"+(string.IsNullOrEmpty(detail)?"incomplete":detail);
                    reason=_reason;
                    return false;
                }
                _package=package;
                _reason="ready";
                reason=_reason;
                return true;
            }
            catch(Exception e)
            {
                _reason="warm-fault:"+e.GetType().Name;
                reason=_reason;
                return false;
            }
        }
    }

    /// <summary>Zero-IO readiness accessor for CanPay/prepare consumers.</summary>
    internal static bool TryGetPackage(out PalaceCapitalPackage package,out string reason)
    {
        package=_package;
        reason=_package!=null?"ready":_reason;
        return _package!=null;
    }

    // Kept test-visible but never publishes: verifies the complete bundle for a caller-supplied
    // stream set (the host suite uses it for first/middle/last/null-stream failure positions).
    internal static bool VerifyWarm(Func<string,Stream> openResource,IReadOnlyCollection<string> availableNames,
        out PalaceCapitalPackage package,out string reason)
    {
        package=null;
        reason="";
        if(!PalaceCapitalResources.TryLoad(openResource,availableNames,out package,out reason))return false;
        if(!MatchesReviewedPackage(package,out reason)){package=null;return false;}
        return true;
    }

    private static bool MatchesReviewedPackage(PalaceCapitalPackage package,out string reason)
    {
        reason="";
        if(package==null||package.Source==null){reason="package-null";return false;}
        PalaceProductionSource source=package.Source;
        if(package.ImageCount!=RequiredImageCount||source.Placements.Count!=RequiredPlacementCount
            ||source.Assets.Count!=RequiredAssetCount)
        {reason="package-counts";return false;}
        if(source.RelativeDrawOrderHints==null||source.RelativeDrawOrderHints.Count!=ExpectedHints.Count)
        {reason="package-hints";return false;}
        foreach(var pair in ExpectedHints)
            if(!source.RelativeDrawOrderHints.TryGetValue(pair.Key,out int hint)||hint!=pair.Value)
            {reason="package-hint:"+pair.Key;return false;}

        var actorIds=new HashSet<string>(StringComparer.Ordinal);
        var timedIds=new HashSet<string>(StringComparer.Ordinal);
        var backgroundGroups=new HashSet<string>(StringComparer.Ordinal);
        foreach(PalaceProductionSource.Asset asset in source.Assets)
        {
            if(asset==null||string.IsNullOrEmpty(asset.Id)){reason="package-asset";return false;}
            if(asset.Kind==PalaceProductionSource.AssetKinds.ActorAtlas)actorIds.Add(asset.Id);
            if(asset.Frames.Count>0)timedIds.Add(asset.Id);
        }
        foreach(PalaceProductionSource.Placement placement in source.Placements)
        {
            if(placement==null||string.IsNullOrEmpty(placement.Id)){reason="package-placement";return false;}
            if(!source.TryGetAsset(placement.AssetId,out PalaceProductionSource.Asset asset))
            {reason="package-placement-asset:"+placement.Id;return false;}
            if(asset.Kind==PalaceProductionSource.AssetKinds.BackgroundCompound)
                backgroundGroups.Add(placement.MotionGroup);
        }
        if(!actorIds.SetEquals(ActorScales.Keys)){reason="package-actors";return false;}
        if(!backgroundGroups.SetEquals(BackgroundRatios.Keys)){reason="package-backgrounds";return false;}
        if(timedIds.Count!=Clips.Count){reason="package-timed-count:"+timedIds.Count;return false;}
        foreach(var pair in Clips)
        {
            if(!timedIds.Contains(pair.Key)||!source.TryGetAsset(pair.Key,out PalaceProductionSource.Asset asset))
            {reason="package-clip-asset:"+pair.Key;return false;}
            bool found=false;
            foreach(PalaceClip clip in asset.Clips)
                if(clip!=null&&clip.Id==pair.Value){found=true;break;}
            if(!found){reason="package-clip:"+pair.Key+":"+pair.Value;return false;}
        }
        return true;
    }

    /// <summary>
    /// Builds the reviewed placement->worldZ map. Inputs are the four actual TryPlane endpoint
    /// offsets (rear far/near, front far/near) and the explicit actor interval; unitZ/camera facts
    /// stay caller-measured. Every produced float worldZ and float view depth must be finite,
    /// unique, correctly sided, globally hint-monotonic and inside the current near/far clip.
    /// </summary>
    internal static bool TryBuildDepthMap(PalaceProductionSource source,double unitZ,
        float rearFar,float rearNear,float frontFar,float frontNear,
        double cameraZ,float nearClip,float farClip,
        out IReadOnlyDictionary<string,double> depthByPlacement,out string reason)
    {
        depthByPlacement=null;reason="";
        if(source==null){reason="depth-source-null";return false;}
        if(!IsFinite(unitZ)||!float.IsFinite((float)unitZ)){reason="depth-unit";return false;}
        if(!IsFinite(cameraZ)||!float.IsFinite((float)cameraZ)){reason="depth-camera";return false;}
        if(!float.IsFinite(nearClip)||!float.IsFinite(farClip)||!(nearClip>0f)||!(farClip>nearClip))
        {reason="depth-clip";return false;}
        if(rearFar!=0.21875f||rearNear!=0.03125f||frontFar!=-0.03125f||frontNear!=-0.0703125f)
        {reason="depth-endpoints";return false;}
        const float actorFar=0.003125f,actorNear=0.0003125f;
        if(!(rearFar>rearNear&&rearNear>actorFar&&actorFar>actorNear&&actorNear>0f
            &&frontFar<0f&&frontNear<frontFar))
        {reason="depth-endpoint-order";return false;}

        if(source.RelativeDrawOrderHints==null||source.RelativeDrawOrderHints.Count!=ExpectedHints.Count)
        {reason="depth-hints";return false;}
        foreach(var pair in ExpectedHints)
            if(!source.RelativeDrawOrderHints.TryGetValue(pair.Key,out int hint)||hint!=pair.Value)
            {reason="depth-hint-drift:"+pair.Key;return false;}

        var groups=new List<IndexedPlacement>[3]
        {
            new List<IndexedPlacement>(), // rear hint<=40
            new List<IndexedPlacement>(), // actor hint==50
            new List<IndexedPlacement>(), // front hint>50
        };
        for(int index=0;index<source.Placements.Count;index++)
        {
            PalaceProductionSource.Placement placement=source.Placements[index];
            if(placement==null||string.IsNullOrEmpty(placement.Id)){reason="depth-placement";return false;}
            if(!source.RelativeDrawOrderHints.TryGetValue(placement.DrawRole,out int hint))
            {reason="depth-role:"+placement.DrawRole;return false;}
            int group=hint<=40?0:hint==50?1:hint>50?2:-1;
            if(group<0){reason="depth-hint-range:"+hint;return false;}
            groups[group].Add(new IndexedPlacement(placement,index,hint));
        }
        if(groups[0].Count!=RequiredRearCount||groups[1].Count!=RequiredActorCount
            ||groups[2].Count!=RequiredFrontCount)
        {reason="depth-count:"+groups[0].Count+"/"+groups[1].Count+"/"+groups[2].Count;return false;}

        for(int g=0;g<groups.Length;g++)
            groups[g].Sort((a,b)=>a.Hint!=b.Hint?a.Hint.CompareTo(b.Hint):a.SourceIndex.CompareTo(b.SourceIndex));

        var ordered=new List<IndexedPlacement>(RequiredPlacementCount);
        var offsets=new List<float>(RequiredPlacementCount);
        AddGroup(groups[0],rearFar,rearNear,ordered,offsets,out reason);
        if(reason.Length!=0)return false;
        AddGroup(groups[1],actorFar,actorNear,ordered,offsets,out reason);
        if(reason.Length!=0)return false;
        AddGroup(groups[2],frontFar,frontNear,ordered,offsets,out reason);
        if(reason.Length!=0)return false;
        if(ordered.Count!=RequiredPlacementCount){reason="depth-order-count";return false;}

        var result=new Dictionary<string,double>(RequiredPlacementCount,StringComparer.Ordinal);
        var seenWorld=new HashSet<float>();
        var seenDepth=new HashSet<float>();
        float unitFloat=(float)unitZ;
        float cameraFloat=(float)cameraZ;
        float unitDepth=unitFloat-cameraFloat;
        bool hasPrevious=false;
        float previousWorld=0f,previousCameraDepth=0f;
        int previousHint=-1;
        for(int i=0;i<ordered.Count;i++)
        {
            IndexedPlacement entry=ordered[i];
            if(entry.Hint<previousHint){reason="depth-hint-order:"+entry.Placement.Id;return false;}
            previousHint=entry.Hint;
            float world=(float)((double)unitFloat+(double)offsets[i]);
            if(!float.IsFinite(world)){reason="depth-float:"+entry.Placement.Id;return false;}
            bool rear=entry.Hint<=50;
            if(rear?!((double)world>(double)unitFloat):!((double)world<(double)unitFloat))
            {reason="depth-side:"+entry.Placement.Id;return false;}
            if(hasPrevious&&!(previousWorld>world)){reason="depth-order:"+entry.Placement.Id;return false;}
            if(!seenWorld.Add(world)){reason="depth-tie:"+entry.Placement.Id;return false;}
            float viewDepth=world-cameraFloat;
            if(!float.IsFinite(viewDepth)){reason="depth-view:"+entry.Placement.Id;return false;}
            if(rear?!((double)viewDepth>(double)unitDepth):!((double)viewDepth<(double)unitDepth))
            {reason="depth-view-side:"+entry.Placement.Id;return false;}
            if(!(viewDepth>nearClip)||!(viewDepth<farClip))
            {reason="depth-clip:"+entry.Placement.Id;return false;}
            if(hasPrevious&&!(previousCameraDepth>viewDepth)){reason="depth-view-order:"+entry.Placement.Id;return false;}
            if(!seenDepth.Add(viewDepth)){reason="depth-view-tie:"+entry.Placement.Id;return false;}
            if(!result.TryAdd(entry.Placement.Id,(double)world)){reason="depth-duplicate:"+entry.Placement.Id;return false;}
            previousWorld=world;
            previousCameraDepth=viewDepth;
            hasPrevious=true;
        }
        depthByPlacement=new ReadOnlyDictionary<string,double>(result);
        return true;
    }

    private static void AddGroup(List<IndexedPlacement> group,float far,float near,List<IndexedPlacement> ordered,
        List<float> offsets,out string reason)
    {
        reason="";
        if(group.Count<2){reason="depth-group-size:"+group.Count;return;}
        for(int i=0;i<group.Count;i++)
        {
            float offset=(float)((double)far+((double)near-(double)far)*i/(group.Count-1));
            if(!float.IsFinite(offset)){reason="depth-offset";return;}
            ordered.Add(group[i]);
            offsets.Add(offset);
        }
    }

    internal static bool AdvanceElapsed(ref double elapsed,bool playing,double scaledDelta,out string reason)
    {
        reason="";
        if(!IsFinite(elapsed)||elapsed<0d){reason="elapsed-state";return false;}
        if(!IsFinite(scaledDelta)||scaledDelta<0d){reason="elapsed-delta";return false;}
        if(!playing)return true; // Menu: frozen, no state advance.
        double next=elapsed+scaledDelta;
        if(!IsFinite(next)||next<elapsed){reason="elapsed-overflow";return false;}
        elapsed=next;
        return true;
    }

    internal static bool IsFinite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);

    private static IReadOnlyDictionary<string,double> BuildActorScales()
    {
        const double dancer=21.6d/26d;
        var map=new Dictionary<string,double>(StringComparer.Ordinal)
        {
            {"patrol-guard-lance",0.9d},
            {"patrol-guard-shield",dancer},
            {"court-dancer",dancer},
            {"guardian-orb",1.2d},
            {"guardian-lute",1.2d},
            {"guardian-sword",1.2d},
            {"guardian-pole",1.2d},
            {"resident-cream",1.2d},
            {"resident-pink",1.2d},
            {"resident-brown",1.2d},
        };
        return new ReadOnlyDictionary<string,double>(map);
    }

    private static IReadOnlyDictionary<string,string> BuildClips()
    {
        var map=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            {"patrol-guard-lance","idle"},
            {"patrol-guard-shield","idle"},
            {"court-dancer","dance"},
            {"guardian-orb","idle"},
            {"guardian-lute","idle"},
            {"guardian-sword","idle"},
            {"guardian-pole","idle"},
            {"resident-cream","idle"},
            {"resident-pink","idle"},
            {"resident-brown","idle"},
            {"lotus-flower-open","calm"},
            {"lotus-flower-bud","calm"},
            {"lotus-leaf-flat","calm"},
            {"lotus-leaf-curl","calm"},
        };
        return new ReadOnlyDictionary<string,string>(map);
    }

    private static IReadOnlyDictionary<string,double> BuildBackgroundRatios()
    {
        var map=new Dictionary<string,double>(StringComparer.Ordinal)
        {
            {"independent-far-palace",0d},
            {"independent-middle-forest",0d},
            {"independent-rear-courtyard",0d},
            {"independent-east-vista",0d},
        };
        return new ReadOnlyDictionary<string,double>(map);
    }

    private readonly struct IndexedPlacement
    {
        internal readonly PalaceProductionSource.Placement Placement;
        internal readonly int SourceIndex;
        internal readonly int Hint;

        internal IndexedPlacement(PalaceProductionSource.Placement placement,int sourceIndex,int hint)
        {Placement=placement;SourceIndex=sourceIndex;Hint=hint;}
    }
}
