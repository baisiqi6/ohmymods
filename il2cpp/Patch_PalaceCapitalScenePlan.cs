// Pure scene geometry and explicit frame resolver for the frozen Capital400 r1 source.
// Design facts only: no IO, textures, Unity types, Harmony, camera reads, sorting authority,
// Native measurement, resource readiness or promotion gate. Every calibration/plane/scale/ratio
// is an explicit caller input; a missing value is rejected, never defaulted.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KingdomEnhancedMod;

internal readonly struct PalaceCapitalScenePose
{
    internal double WorldX { get; }
    internal double WorldY { get; }
    internal double ScaleX { get; }
    internal double ScaleY { get; }
    internal double SpritePivotNormalizedX { get; }
    internal double SpritePivotNormalizedY { get; }

    internal PalaceCapitalScenePose(double worldX,double worldY,double scaleX,double scaleY,
        double spritePivotNormalizedX,double spritePivotNormalizedY)
    {
        WorldX=worldX;WorldY=worldY;ScaleX=scaleX;ScaleY=scaleY;
        SpritePivotNormalizedX=spritePivotNormalizedX;SpritePivotNormalizedY=spritePivotNormalizedY;
    }
}

// One clip resolved against the asset's explicit frame indices/durations. Durations may repeat
// for different physical poses; frames are never merged, coiled or indexed modulo an atlas row.
internal sealed class PalaceCapitalSceneClip
{
    internal string Id { get; }
    internal IReadOnlyList<PalaceFrame> Frames { get; }
    internal IReadOnlyList<double> BoundariesSeconds { get; } // cumulative; strictly increasing; last == PeriodSeconds
    internal double PeriodSeconds { get; }
    internal bool Loops { get; }

    internal PalaceCapitalSceneClip(string id,IReadOnlyList<PalaceFrame> frames,IReadOnlyList<double> boundariesSeconds,
        double periodSeconds,bool loops)
    {
        Id=id;Frames=frames;BoundariesSeconds=boundariesSeconds;PeriodSeconds=periodSeconds;Loops=loops;
    }
}

internal sealed class PalaceCapitalSceneNode
{
    private readonly double _anchorWorldX;

    internal PalaceProductionSource.Asset Asset { get; }
    internal PalaceProductionSource.Placement Placement { get; }
    internal double SupportWorldX { get; }
    internal double ReferencePlaneWorldY { get; }
    internal double LayoutOffsetYUnits { get; }
    internal double SupportWorldY => ReferencePlaneWorldY+LayoutOffsetYUnits;
    internal double ScaleX => 1d; // facing/orientation stays a future renderer flipX decision
    internal double ScaleY { get; }
    internal double CameraFollowRatio { get; }
    internal bool IsBackgroundCompound { get; }
    internal string DrawRoleHint => Placement.DrawRole; // relative semantic hint, never Native Z
    internal double StaticWorldX { get; }
    internal double StaticWorldY { get; }
    internal IReadOnlyDictionary<string,PalaceCapitalSceneClip> Clips { get; }

    internal PalaceCapitalSceneNode(PalaceProductionSource.Asset asset,PalaceProductionSource.Placement placement,
        double supportWorldX,double referencePlaneWorldY,double layoutOffsetYUnits,double scaleY,
        double cameraFollowRatio,bool isBackgroundCompound,double staticWorldX,double staticWorldY,
        double anchorWorldX,IReadOnlyDictionary<string,PalaceCapitalSceneClip> clips)
    {
        Asset=asset;Placement=placement;SupportWorldX=supportWorldX;ReferencePlaneWorldY=referencePlaneWorldY;
        LayoutOffsetYUnits=layoutOffsetYUnits;ScaleY=scaleY;CameraFollowRatio=cameraFollowRatio;
        IsBackgroundCompound=isBackgroundCompound;StaticWorldX=staticWorldX;StaticWorldY=staticWorldY;
        _anchorWorldX=anchorWorldX;Clips=clips;
    }

    // Pure design-follow translation: base + (cameraX - calibration anchor) * explicit ratio.
    // The base never depends on a camera sampled at Create time. No camera is read here.
    internal double GetWorldX(double cameraX)
    {
        if(Clips.Count>0)
            throw new InvalidOperationException("Timed nodes have no layer parallax; use TryFrameAt for their pose.");
        if(!PalaceCapitalScenePlan.IsFinite(cameraX))
            throw new ArgumentOutOfRangeException(nameof(cameraX),cameraX,"cameraX must be finite.");
        double worldX=StaticWorldX+(cameraX-_anchorWorldX)*CameraFollowRatio;
        if(!PalaceCapitalScenePlan.IsFloatRepresentable(worldX))
            throw new ArgumentOutOfRangeException(nameof(cameraX),cameraX,"world X result must be a finite float.");
        return worldX;
    }
}

internal sealed class PalaceCapitalScenePlan
{
    internal const double RequiredUnitsPerPixel=1d/32d;
    internal const int RequiredMasterWidthPx=12840;
    internal const int RequiredMasterHeightPx=252;
    internal const int RequiredAxisPixelX=6420;
    internal const int RequiredStreetReferenceY=224;

    private static readonly IReadOnlyDictionary<string,PalaceCapitalSceneClip> NoClips
        =new ReadOnlyDictionary<string,PalaceCapitalSceneClip>(new Dictionary<string,PalaceCapitalSceneClip>(StringComparer.Ordinal));

    private readonly Dictionary<string,PalaceCapitalSceneNode> _nodesById;

    internal IReadOnlyList<PalaceCapitalSceneNode> Nodes { get; }
    internal double AnchorWorldX { get; }
    internal double GroundReferenceY { get; }
    internal double? WaterReferenceY { get; }
    internal double UnitsPerPixel { get; }
    internal int DesignPpu { get; }
    internal int GateAxisPixelX { get; }
    internal int DesignStreetReferenceY { get; }

    private PalaceCapitalScenePlan(IReadOnlyList<PalaceCapitalSceneNode> nodes,double anchorWorldX,
        double groundReferenceY,double? waterReferenceY,double unitsPerPixel,int designPpu,int gateAxisPixelX,int streetReferenceY)
    {
        Nodes=nodes;AnchorWorldX=anchorWorldX;GroundReferenceY=groundReferenceY;WaterReferenceY=waterReferenceY;
        UnitsPerPixel=unitsPerPixel;DesignPpu=designPpu;GateAxisPixelX=gateAxisPixelX;DesignStreetReferenceY=streetReferenceY;
        _nodesById=new Dictionary<string,PalaceCapitalSceneNode>(StringComparer.Ordinal);
        foreach(var node in nodes)_nodesById.Add(node.Placement.Id,node);
    }

    internal bool TryGetNode(string id,out PalaceCapitalSceneNode node)
    {
        if(id!=null&&_nodesById.TryGetValue(id,out node))return true;
        node=null;return false;
    }

    // Builds the immutable design plan for the reviewed Capital400 recipe pair.
    // ground reference = calibration.GroundWorldY; water reference must be explicit when the
    // source contains native-water placements. No value is inferred from the other plane.
    internal static bool TryCreate(PalaceProductionSource source,PalaceScrollCalibration calibration,
        double? explicitWaterReferenceY,IReadOnlyDictionary<string,double> actorYScaleById,
        IReadOnlyDictionary<string,double> backgroundFollowRatioByGroup,
        out PalaceCapitalScenePlan plan,out string reason)
    {
        plan=null;reason="";
        if(source==null){reason="source-null";return false;}
        if(calibration==null){reason="calibration-null";return false;}
        if(actorYScaleById==null){reason="actor-scale-map-null";return false;}
        if(backgroundFollowRatioByGroup==null){reason="background-ratio-map-null";return false;}
        if(!TryValidateCalibration(calibration,out reason))return false;
        if(source.DesignPpu!=PalaceProductionSource.RequiredPpu
            ||source.GateAxisPixelX!=RequiredAxisPixelX
            ||source.DesignStreetReferenceY!=RequiredStreetReferenceY
            ||source.PhysicalPixelDomainMinX!=PalaceProductionSource.RequiredPixelMinX
            ||source.PhysicalPixelDomainMaxX!=PalaceProductionSource.RequiredPixelMaxX
            ||source.WorldDomainMinX!=PalaceProductionSource.RequiredWalkMinX
            ||source.WorldDomainMaxX!=PalaceProductionSource.RequiredWalkMaxX)
        {reason="source-constants";return false;}
        if(!IsFloatRepresentable(calibration.GroundWorldY)){reason="ground-reference";return false;}

        bool hasWater=false;
        foreach(var placement in source.Placements)
        {
            if(placement.ReferencePlane=="native-water")hasWater=true;
            else if(placement.ReferencePlane!="native-ground"){reason="reference-plane";return false;}
        }
        double? waterReferenceY=null;
        if(explicitWaterReferenceY.HasValue)
        {
            double value=explicitWaterReferenceY.Value;
            if(!IsFloatRepresentable(value)){reason="water-reference";return false;}
            waterReferenceY=value;
        }
        if(hasWater&&!waterReferenceY.HasValue){reason="water-reference-missing";return false;}

        var actorIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var asset in source.Assets)
            if(asset.Kind==PalaceProductionSource.AssetKinds.ActorAtlas)actorIds.Add(asset.Id);
        foreach(var asset in source.Assets)
        {
            if(asset.Kind!=PalaceProductionSource.AssetKinds.ActorAtlas)continue;
            if(!actorYScaleById.TryGetValue(asset.Id,out double scale)){reason="actor-scale-missing:"+asset.Id;return false;}
            if(!IsFloatRepresentable(scale)||!(scale>0d)||!((float)scale>0f))
            {reason="actor-scale:"+asset.Id;return false;}
        }
        foreach(string id in actorYScaleById.Keys)
            if(!actorIds.Contains(id)){reason="actor-scale-unknown:"+id;return false;}

        var backgroundGroups=new HashSet<string>(StringComparer.Ordinal);
        foreach(var placement in source.Placements)
        {
            if(!source.TryGetAsset(placement.AssetId,out var asset)){reason="asset-missing:"+placement.AssetId;return false;}
            if(asset.Kind==PalaceProductionSource.AssetKinds.BackgroundCompound)backgroundGroups.Add(placement.MotionGroup);
        }
        foreach(string group in backgroundGroups)
            if(!backgroundFollowRatioByGroup.ContainsKey(group)){reason="background-follow-missing:"+group;return false;}
        foreach(var pair in backgroundFollowRatioByGroup)
        {
            if(!backgroundGroups.Contains(pair.Key)){reason="background-follow-unknown:"+pair.Key;return false;}
            if(!IsFinite(pair.Value)||pair.Value<0d||pair.Value>1d||!IsFloatRepresentable(pair.Value))
            {reason="background-follow:"+pair.Key;return false;}
        }

        var nodes=new PalaceCapitalSceneNode[source.Placements.Count];
        var clipsByAsset=new Dictionary<string,IReadOnlyDictionary<string,PalaceCapitalSceneClip>>(StringComparer.Ordinal);
        int index=0;
        foreach(var placement in source.Placements)
        {
            if(!source.TryGetAsset(placement.AssetId,out var asset)){reason="asset-missing:"+placement.AssetId;return false;}
            bool background=asset.Kind==PalaceProductionSource.AssetKinds.BackgroundCompound;
            double referenceY;
            if(placement.ReferencePlane=="native-water")
            {
                if(!waterReferenceY.HasValue){reason="water-reference-missing";return false;}
                referenceY=waterReferenceY.Value;
            }
            else if(placement.ReferencePlane=="native-ground")referenceY=calibration.GroundWorldY;
            else {reason="reference-plane";return false;}

            if(!IsFinite(placement.SupportWorldX)||!IsFinite(placement.SupportLocalPixel.X)
                ||!IsFinite(placement.SupportLocalPixel.Y)||!IsFinite(placement.SupportOffsetYUnits))
            {reason="node-input:"+placement.Id;return false;}

            bool timed=asset.Frames.Count>0;
            if(timed)
            {
                if(asset.Kind!=PalaceProductionSource.AssetKinds.ActorAtlas
                    &&asset.Kind!=PalaceProductionSource.AssetKinds.LocalEnvironmentMotionAtlas)
                {reason="asset-frames:"+asset.Id;return false;}
                if(asset.Clips.Count==0){reason="asset-clips:"+asset.Id;return false;}
                if(!asset.LogicalCellWidth.HasValue||!asset.LogicalCellHeight.HasValue
                    ||asset.LogicalCellWidth.Value<=0||asset.LogicalCellHeight.Value<=0)
                {reason="logical-cell:"+asset.Id;return false;}
            }
            else if(asset.Clips.Count!=0
                ||asset.Kind==PalaceProductionSource.AssetKinds.ActorAtlas
                ||asset.Kind==PalaceProductionSource.AssetKinds.LocalEnvironmentMotionAtlas)
            {reason="asset-frames:"+asset.Id;return false;}

            IReadOnlyDictionary<string,PalaceCapitalSceneClip> clips;
            if(!timed)clips=NoClips;
            else if(!clipsByAsset.TryGetValue(asset.Id,out clips))
            {
                if(!TryBuildClips(asset,out clips,out reason))return false;
                clipsByAsset.Add(asset.Id,clips);
            }

            double staticWorldX=placement.SupportWorldX-placement.SupportLocalPixel.X/32d;
            double staticWorldY=referenceY+placement.SupportOffsetYUnits
                +(placement.SupportLocalPixel.Y-asset.Height)/32d;
            if(!IsFloatRepresentable(staticWorldX)||!IsFloatRepresentable(staticWorldY))
            {reason="node-geometry:"+placement.Id;return false;}

            double scaleY=1d;
            if(asset.Kind==PalaceProductionSource.AssetKinds.ActorAtlas)scaleY=actorYScaleById[asset.Id];
            double followRatio=background?backgroundFollowRatioByGroup[placement.MotionGroup]:0d;

            nodes[index++]=new PalaceCapitalSceneNode(asset,placement,placement.SupportWorldX,referenceY,
                placement.SupportOffsetYUnits,scaleY,followRatio,background,staticWorldX,staticWorldY,
                calibration.AnchorWorldX,clips);
        }

        plan=new PalaceCapitalScenePlan(Array.AsReadOnly(nodes),calibration.AnchorWorldX,calibration.GroundWorldY,
            waterReferenceY,calibration.UnitsPerPixel,source.DesignPpu,source.GateAxisPixelX,source.DesignStreetReferenceY);
        return true;
    }

    // Static nodes have no clip clock: null clip id, zero phase and a pivot-(0,0) pose.
    // Timed nodes resolve one explicit clip against explicit elapsed/initial-offset seconds.
    // The only period is clip.TotalDurationMs/1000; callers never pass a period or a seed.
    internal static bool TryFrameAt(PalaceCapitalSceneNode node,string explicitClipId,double elapsedSeconds,
        double explicitPhaseOffsetSeconds,out PalaceFrame frame,out PalaceCapitalScenePose pose,out string reason)
    {
        frame=null;pose=default;reason="";
        if(node==null){reason="node-null";return false;}
        if(!IsFinite(elapsedSeconds)||elapsedSeconds<0d){reason="elapsed";return false;}
        if(!IsFinite(explicitPhaseOffsetSeconds)){reason="phase-offset";return false;}

        if(node.Clips.Count==0)
        {
            if(explicitClipId!=null){reason="clip-on-static";return false;}
            if(explicitPhaseOffsetSeconds!=0d){reason="phase-offset-static";return false;}
            pose=new PalaceCapitalScenePose(node.StaticWorldX,node.StaticWorldY,1d,1d,0d,0d);
            if(!IsRepresentable(pose)){pose=default;reason="pose";return false;}
            return true;
        }

        if(string.IsNullOrEmpty(explicitClipId)){reason="clip-required";return false;}
        if(!node.Clips.TryGetValue(explicitClipId,out var clip)){reason="clip-unknown:"+explicitClipId;return false;}
        double period=clip.PeriodSeconds;
        double effective;
        if(clip.Loops)
        {
            if(explicitPhaseOffsetSeconds<0d||explicitPhaseOffsetSeconds>=period)
            {reason="phase-offset-loop";return false;}
            effective=((elapsedSeconds%period)+explicitPhaseOffsetSeconds)%period;
        }
        else
        {
            if(explicitPhaseOffsetSeconds!=0d){reason="phase-offset-oneshot";return false;}
            effective=elapsedSeconds>=period?period:elapsedSeconds;
        }

        int selected;
        if(effective==period)selected=clip.Frames.Count-1;
        else
        {
            selected=0;
            var boundaries=clip.BoundariesSeconds;
            while(selected<boundaries.Count&&!(effective<boundaries[selected]))selected++;
            if(selected>=clip.Frames.Count){reason="clock";return false;}
        }
        frame=clip.Frames[selected];

        PalaceActorRegistry.Point pivot=frame.PivotBottomLeftPixels;
        double cellWidth=node.Asset.LogicalCellWidth.Value;
        double cellHeight=node.Asset.LogicalCellHeight.Value;
        double worldX=node.SupportWorldX-(node.Placement.SupportLocalPixel.X-pivot.X)/32d;
        double worldY=node.ReferencePlaneWorldY+node.LayoutOffsetYUnits
            -node.ScaleY*(cellHeight-node.Placement.SupportLocalPixel.Y-pivot.Y)/32d;
        pose=new PalaceCapitalScenePose(worldX,worldY,1d,node.ScaleY,pivot.X/cellWidth,pivot.Y/cellHeight);
        if(!IsRepresentable(pose)){frame=null;pose=default;reason="pose";return false;}
        return true;
    }

    private static bool TryValidateCalibration(PalaceScrollCalibration calibration,out string reason)
    {
        reason="";
        PalaceScrollDescriptor descriptor=calibration.Descriptor;
        if(descriptor==null){reason="descriptor-missing";return false;}
        if(!ReferenceEquals(descriptor.Recipe,PalaceLayoutRecipe.Capital400)){reason="descriptor-recipe";return false;}
        if(descriptor.MasterWidthPx!=RequiredMasterWidthPx||descriptor.MasterHeightPx!=RequiredMasterHeightPx)
        {reason="descriptor-master";return false;}
        if(descriptor.AxisPixelX!=RequiredAxisPixelX){reason="descriptor-axis";return false;}
        if(descriptor.FootlinePixelY!=RequiredStreetReferenceY){reason="descriptor-footline";return false;}
        if(calibration.UnitsPerPixel!=RequiredUnitsPerPixel){reason="units-per-pixel";return false;}
        if(calibration.AnchorWorldX!=PalaceLayoutRecipe.Capital400.GateWorldX){reason="anchor-world-x";return false;}
        return true;
    }

    private static bool TryBuildClips(PalaceProductionSource.Asset asset,
        out IReadOnlyDictionary<string,PalaceCapitalSceneClip> clips,out string reason)
    {
        clips=null;reason="";
        var table=new Dictionary<string,PalaceCapitalSceneClip>(StringComparer.Ordinal);
        foreach(var clip in asset.Clips)
        {
            if(clip==null||string.IsNullOrEmpty(clip.Id)){reason="clip-id";return false;}
            if(clip.Indices==null||clip.DurationsMs==null||clip.Indices.Count==0
                ||clip.Indices.Count!=clip.DurationsMs.Count){reason="clip-shape";return false;}
            var frames=new PalaceFrame[clip.Indices.Count];
            var boundaries=new double[clip.Indices.Count];
            long cumulativeMs=0;
            for(int k=0;k<frames.Length;k++)
            {
                int frameIndex=clip.Indices[k];
                int durationMs=clip.DurationsMs[k];
                if(frameIndex<0||frameIndex>=asset.Frames.Count||durationMs<=0){reason="clip-frame";return false;}
                frames[k]=asset.Frames[frameIndex];
                cumulativeMs=checked(cumulativeMs+durationMs);
                boundaries[k]=cumulativeMs/1000d;
                if(!IsFloatRepresentable(boundaries[k])){reason="clip-boundary";return false;}
            }
            if(cumulativeMs!=clip.TotalDurationMs||cumulativeMs<=0){reason="clip-duration";return false;}
            double period=clip.TotalDurationMs/1000d;
            if(!IsFloatRepresentable(period)||!(period>0d)){reason="clip-period";return false;}
            if(boundaries[boundaries.Length-1]!=period){reason="clip-period";return false;}
            if(!table.TryAdd(clip.Id,new PalaceCapitalSceneClip(clip.Id,Array.AsReadOnly(frames),
                Array.AsReadOnly(boundaries),period,clip.Loops)))
            {reason="clip-duplicate";return false;}
        }
        clips=new ReadOnlyDictionary<string,PalaceCapitalSceneClip>(table);
        return true;
    }

    private static bool IsRepresentable(PalaceCapitalScenePose pose)
        =>IsFloatRepresentable(pose.WorldX)&&IsFloatRepresentable(pose.WorldY)
        &&IsFloatRepresentable(pose.ScaleX)&&IsFloatRepresentable(pose.ScaleY)
        &&IsFloatRepresentable(pose.SpritePivotNormalizedX)&&IsFloatRepresentable(pose.SpritePivotNormalizedY);

    internal static bool IsFinite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    internal static bool IsFloatRepresentable(double value)=>IsFinite(value)&&float.IsFinite((float)value);
}
