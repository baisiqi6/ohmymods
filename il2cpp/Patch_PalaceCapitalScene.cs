// Source-only Unity drawing adapter for the frozen Capital400 R12 package (S2b).
//
// Boundary: consumes an already verified PalaceCapitalPackage, an S1 PalaceCapitalScenePlan, an
// explicit placement->worldZ map and an explicit timed-asset->clip map. It creates one inactive
// owned root, 452 SpriteRenderers, one Point/Clamp Texture2D per reviewed asset and the physical
// sprites the S1 frame tables can reference. It never chooses sorting values, clips, phase, scale,
// camera, water, ground, fee state or behaviour; a missing/extra/non-finite input is refused.
//
// Borrowed: CourtBinding.Parent, the reference SpriteRenderer's GameObject layer/sortingLayer and
// the reference's sharedMaterial. The Root contract keeps that reference/material alive for the
// whole scene lifetime. This class never destroys borrowed objects, never reads .material/mesh,
// never reads a Camera or Water and adds no Harmony patch, MonoBehaviour, driver or scan.
//
// Ownership: the root, gate anchor and every placement GameObject are owned from creation.
// Gate/placement GameObjects enter a pending list as soon as new GameObject returns and transfer
// to root ownership only after SetParent returns; an attachment that throws (even after mutating
// the parent) stays pending and is destroyed directly. Every Texture2D/Sprite enters its unique
// list immediately after construction, before any name/loader setter can throw. TryClear
// deactivates the root and pending objects first, then destroys pending GameObjects, sprites,
// textures and only then the root. A native destroy exception stops the clear, leaves the
// remaining owned objects and root reference with the scene (owner must retain it), and can be
// retried without destroying successful items again. Create returns scene=null only after a
// known-complete rollback.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

internal sealed class PalaceCapitalScene
{
    private const int RequiredPlacementCount=452;
    private const int RequiredTimedPlacementCount=56;
    private const int RequiredStaticPlacementCount=396;
    private const int RequiredAssetCount=86;
    private const int RequiredStaticAssetCount=72;
    private const int RequiredTimedAssetCount=14;
    private const int RequiredActorTimedAssetCount=10;
    private const int RequiredLocalTimedAssetCount=4;
    private const float RequiredPixelsPerUnit=32f;
    private const string GateAssetId="central-palace-gate";

    private readonly struct FramePivot
    {
        internal readonly double X;
        internal readonly double Y;
        internal FramePivot(double x,double y){X=x;Y=y;}
    }

    // Physical sprite identity: explicit source rect plus the S1-normalized pivot. Distinct
    // durations on the same rect share one physical Sprite; the temporal table stays in S1.
    private readonly struct FrameKey : IEquatable<FrameKey>
    {
        private readonly int _x,_y,_width,_height;
        private readonly double _pivotX,_pivotY;

        internal FrameKey(int x,int y,int width,int height,double pivotX,double pivotY)
        {
            _x=x;_y=y;_width=width;_height=height;_pivotX=pivotX;_pivotY=pivotY;
        }

        public bool Equals(FrameKey other)
            =>_x==other._x&&_y==other._y&&_width==other._width&&_height==other._height
            &&_pivotX==other._pivotX&&_pivotY==other._pivotY;

        public override bool Equals(object obj)=>obj is FrameKey other&&Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash=_x;
                hash=hash*31+_y;
                hash=hash*31+_width;
                hash=hash*31+_height;
                hash=hash*31+_pivotX.GetHashCode();
                hash=hash*31+_pivotY.GetHashCode();
                return hash;
            }
        }
    }

    private Transform _parent;
    private SpriteRenderer _reference;
    private Material _material;
    private IntPtr _referencePointer;
    private IntPtr _materialPointer;
    private IntPtr _rootPointer;
    private int _sceneHandle;

    private GameObject _root;
    private Transform _gateAnchor;
    private PalaceCapitalSceneNode[] _nodes;
    private SpriteRenderer[] _renderers;
    private Transform[] _transforms;
    private float[] _worldZ;
    private string[] _clipByNode;
    private Sprite[][] _frameSprites;
    private bool[] _hasParallax;
    private Sprite[] _appliedSprites;
    private float[] _appliedX;
    private float[] _appliedY;
    private PalaceCapitalScenePose[] _nextPoses;
    private Sprite[] _nextSprites;

    private readonly List<Texture2D> _textures=new List<Texture2D>(RequiredAssetCount);
    private readonly List<Sprite> _sprites=new List<Sprite>(RequiredAssetCount*5);
    private readonly List<GameObject> _pendingObjects=new List<GameObject>(2);
    private int _placedSprites;
    private int _uniqueTextures;
    private int _spriteCount;
    private bool _active;
    private bool _faulted;
    private bool _cleared;
    private double _lastElapsed;
    private double _lastCameraX;

    /// <summary>Visual gate anchor only; not a Native-tested trigger extent or fee source anchor.</summary>
    internal Transform GateAnchor=>_cleared?null:_gateAnchor;
    /// <summary>Owned root for owner diagnostics; inactive until Activate succeeds.</summary>
    internal GameObject RootObject=>_root;
    internal int PlacedSprites=>_placedSprites;
    internal int UniqueTextures=>_uniqueTextures;
    internal int Sprites=>_spriteCount;

    /// <summary>
    /// Validates every pure precondition and the complete S1 geometry before allocating anything,
    /// then builds an inactive scene. Failure before allocation returns null. Failure during
    /// allocation rolls back; if rollback is incomplete the scene is returned retained with the
    /// owned root/objects and Root must call TryClear before dropping it.
    /// </summary>
    internal static bool Create(CourtBinding binding,PalaceCapitalPackage package,PalaceCapitalScenePlan plan,
        IReadOnlyDictionary<string,double> depthByPlacement,IReadOnlyDictionary<string,string> clipByAsset,
        out PalaceCapitalScene scene,out string reason)
    {
        scene=null;reason="";
        var self=new PalaceCapitalScene();
        try
        {
            if(!self.TryPrepare(binding,package,plan,depthByPlacement,clipByAsset,out reason))
            {
                self=null;
                return false;
            }
            try
            {
                self.Build(package);
                scene=self;
                return true;
            }
            catch(Exception e)
            {
                string failure="create-fault:"+e.GetType().Name+":"+e.Message;
                if(self.TryClearCore(out string clearReason))
                {
                    reason=failure+"; rolled back";
                    return false;
                }
                self._faulted=true;
                scene=self;
                reason=failure+"; rollback-incomplete:"+clearReason+"; owner retained, call TryClear";
                return false;
            }
        }
        catch(Exception e)
        {
            string failure="create-check-fault:"+e.GetType().Name+":"+e.Message;
            if(self._root==null&&self._textures.Count==0&&self._sprites.Count==0
                &&self._pendingObjects.Count==0)
            {
                reason=failure;
                return false;
            }
            if(self.TryClearCore(out string clearReason))
            {
                reason=failure+"; rolled back";
                return false;
            }
            self._faulted=true;
            scene=self;
            reason=failure+"; rollback-incomplete:"+clearReason+"; owner retained, call TryClear";
            return false;
        }
    }

    /// <summary>First activation of the owned root; validates the borrowed reference/material again.</summary>
    internal bool Activate(out string reason)
    {
        reason="";
        if(_cleared){reason="cleared";return false;}
        if(_faulted){reason="faulted";return false;}
        if(_active)return true;
        if(!TryValidateBorrowed(out _,out reason)){_faulted=true;return false;}
        try
        {
            if(_root==null||!(bool)_root||_root.Pointer!=_rootPointer){reason="root-dead";_faulted=true;return false;}
            _root.SetActive(true);
        }
        catch(Exception e)
        {
            reason="activate-fault:"+e.GetType().Name;
            _faulted=true;
            return false;
        }
        _active=true;
        return true;
    }

    /// <summary>
    /// Advances the explicit local clock and camera. First finite non-negative elapsed is accepted;
    /// later elapsed must not decrease. All next poses/frames are budgeted in preallocated arrays
    /// before any mutation. Invalid input returns false without advancing the applied elapsed.
    /// A borrowed-object or native-write fault latches the scene, returns false and requires
    /// Root TryClear; Tick never retries or rebuilds.
    /// </summary>
    internal bool Tick(double elapsedSeconds,double cameraX,out string reason)
    {
        reason="";
        if(_cleared){reason="cleared";return false;}
        if(_faulted){reason="faulted";return false;}
        if(!_active){reason="inactive";return false;}
        if(!PalaceCapitalScenePlan.IsFinite(elapsedSeconds)||elapsedSeconds<0d)
        {reason="elapsed";return false;}
        if(elapsedSeconds<_lastElapsed){reason="elapsed-backwards";return false;}
        if(!PalaceCapitalScenePlan.IsFinite(cameraX)){reason="camera";return false;}
        if(!TryValidateBorrowed(out _,out reason)){_faulted=true;return false;}
        if(elapsedSeconds==_lastElapsed&&cameraX==_lastCameraX)return true;

        bool elapsedAdvanced=elapsedSeconds!=_lastElapsed;
        try
        {
            for(int i=0;i<_nodes.Length;i++)
            {
                _nextSprites[i]=null;
                if(_clipByNode[i]!=null)
                {
                    if(!elapsedAdvanced)continue;
                    if(!PalaceCapitalScenePlan.TryFrameAt(_nodes[i],_clipByNode[i],elapsedSeconds,0d,
                        out PalaceFrame frame,out PalaceCapitalScenePose pose,out string frameReason))
                    {reason="frame:"+frameReason;return false;}
                    Sprite[] frames=_frameSprites[i];
                    if(frame==null||frames==null||frame.Index<0||frame.Index>=frames.Length)
                    {reason="frame-index:"+_nodes[i].Placement.Id;return false;}
                    Sprite next=frames[frame.Index];
                    if(next==null){reason="frame-sprite:"+_nodes[i].Placement.Id;return false;}
                    _nextSprites[i]=next;
                    _nextPoses[i]=pose;
                }
                else if(_nodes[i].IsBackgroundCompound)
                {
                    double worldX;
                    try{worldX=_nodes[i].GetWorldX(cameraX);}
                    catch(Exception e){reason="camera:"+e.GetType().Name;return false;}
                    _nextPoses[i]=new PalaceCapitalScenePose(worldX,_nodes[i].StaticWorldY,
                        1d,_nodes[i].ScaleY,0d,0d);
                }
            }
        }
        catch(Exception e)
        {
            reason="tick-check-fault:"+e.GetType().Name;
            return false;
        }

        try
        {
            for(int i=0;i<_nodes.Length;i++)
            {
                if(_clipByNode[i]!=null)
                {
                    Sprite next=_nextSprites[i];
                    if(next!=null&&!ReferenceEquals(next,_appliedSprites[i]))
                    {
                        _renderers[i].sprite=next;
                        _appliedSprites[i]=next;
                    }
                    if(next==null)continue;
                    float x=(float)_nextPoses[i].WorldX;
                    float y=(float)_nextPoses[i].WorldY;
                    if(x!=_appliedX[i]||y!=_appliedY[i])
                    {
                        _transforms[i].position=new Vector3(x,y,_worldZ[i]);
                        _appliedX[i]=x;
                        _appliedY[i]=y;
                    }
                }
                else if(_hasParallax[i])
                {
                    float x=(float)_nextPoses[i].WorldX;
                    if(x!=_appliedX[i])
                    {
                        _transforms[i].position=new Vector3(x,(float)_nodes[i].StaticWorldY,_worldZ[i]);
                        _appliedX[i]=x;
                    }
                }
            }
        }
        catch(Exception e)
        {
            reason="write-fault:"+e.GetType().Name;
            _faulted=true;
            return false;
        }

        _lastElapsed=elapsedSeconds;
        _lastCameraX=cameraX;
        return true;
    }

    /// <summary>
    /// Confirmable clear of owned objects only. The root is deactivated first. Sprites and
    /// textures are destroyed exactly once each (successful items are removed immediately), then
    /// the root. Any exception retains the remaining owned objects and the root reference and
    /// returns false; a retry resumes the remaining list. Borrowed reference/material/parent are
    /// never touched.
    /// </summary>
    internal bool TryClear(out string reason)
    {
        reason="";
        if(_cleared)return true;
        if(TryClearCore(out reason))return true;
        _faulted=true;
        return false;
    }

    private bool TryClearCore(out string reason)
    {
        reason="";
        if(_cleared)return true;
        GameObject root=_root;
        if(root!=null&&(bool)root)
        {
            try{root.SetActive(false);}
            catch(Exception e){reason="deactivate:"+e.GetType().Name;return false;}
        }
        for(int i=_pendingObjects.Count-1;i>=0;i--)
        {
            GameObject pending=_pendingObjects[i];
            if(pending==null||!(bool)pending){_pendingObjects.RemoveAt(i);continue;}
            try{pending.SetActive(false);}
            catch(Exception e){reason="pending-deactivate:"+e.GetType().Name;return false;}
        }
        for(int i=_pendingObjects.Count-1;i>=0;i--)
        {
            GameObject pending=_pendingObjects[i];
            if(pending==null||!(bool)pending){_pendingObjects.RemoveAt(i);continue;}
            try
            {
                UnityEngine.Object.Destroy(pending);
                _pendingObjects.RemoveAt(i);
            }
            catch(Exception e){reason="pending-destroy:"+e.GetType().Name;return false;}
        }
        for(int i=_sprites.Count-1;i>=0;i--)
        {
            Sprite sprite=_sprites[i];
            if(sprite==null||!(bool)sprite){_sprites.RemoveAt(i);continue;}
            try
            {
                UnityEngine.Object.Destroy(sprite);
                _sprites.RemoveAt(i);
            }
            catch(Exception e){reason="sprite-destroy:"+e.GetType().Name;return false;}
        }
        for(int i=_textures.Count-1;i>=0;i--)
        {
            Texture2D texture=_textures[i];
            if(texture==null||!(bool)texture){_textures.RemoveAt(i);continue;}
            try
            {
                UnityEngine.Object.Destroy(texture);
                _textures.RemoveAt(i);
            }
            catch(Exception e){reason="texture-destroy:"+e.GetType().Name;return false;}
        }
        if(root!=null)
        {
            if(!(bool)root)_root=null;
            else
            {
                try
                {
                    UnityEngine.Object.Destroy(root);
                    _root=null;
                }
                catch(Exception e){reason="root-destroy:"+e.GetType().Name;return false;}
            }
        }
        _gateAnchor=null;
        _active=false;
        _faulted=false;
        _cleared=true;
        _pendingObjects.Clear();
        _placedSprites=0;
        _uniqueTextures=0;
        _spriteCount=0;
        _nodes=null;_renderers=null;_transforms=null;_worldZ=null;_clipByNode=null;
        _frameSprites=null;_hasParallax=null;_appliedSprites=null;
        _appliedX=null;_appliedY=null;_nextPoses=null;_nextSprites=null;
        _pivotsByAsset=null;_parent=null;_reference=null;_material=null;
        return true;
    }

    private bool TryValidateBorrowed(out Material material,out string reason)
    {
        material=null;reason="";
        SpriteRenderer reference=_reference;
        if(reference==null||!(bool)reference||reference.Pointer!=_referencePointer||reference.Pointer==IntPtr.Zero)
        {reason="reference-dead-or-replaced";return false;}
        try{material=reference.sharedMaterial;}
        catch(Exception e){reason="material-read:"+e.GetType().Name;return false;}
        if(material==null||!(bool)material||material.Pointer==IntPtr.Zero)
        {reason="material-dead";return false;}
        if(material.Pointer!=_materialPointer){reason="material-replaced";return false;}
        try
        {
            if(reference.gameObject==null||reference.gameObject.scene.handle!=_sceneHandle)
            {reason="reference-scene";return false;}
        }
        catch(Exception e){reason="reference-read:"+e.GetType().Name;return false;}
        if(_root==null||!(bool)_root||_root.Pointer!=_rootPointer||_root.Pointer==IntPtr.Zero)
        {reason="root-dead-or-replaced";return false;}
        return true;
    }

    private bool TryPrepare(CourtBinding binding,PalaceCapitalPackage package,PalaceCapitalScenePlan plan,
        IReadOnlyDictionary<string,double> depthByPlacement,IReadOnlyDictionary<string,string> clipByAsset,
        out string reason)
    {
        reason="";
        if(binding==null){reason="binding-null";return false;}
        Transform parent=binding.Parent;
        SpriteRenderer reference=binding.Reference;
        if(parent==null||!(bool)parent){reason="parent-null";return false;}
        if(reference==null||!(bool)reference||reference.Pointer==IntPtr.Zero){reason="reference-null";return false;}
        if(reference.gameObject==null){reason="reference-gameobject-null";return false;}
        if(parent.gameObject==null){reason="parent-gameobject-null";return false;}
        int sceneHandle=parent.gameObject.scene.handle;
        if(reference.gameObject.scene.handle!=sceneHandle){reason="binding-scene";return false;}
        Material material;
        try{material=reference.sharedMaterial;}
        catch(Exception e){reason="material-read:"+e.GetType().Name;return false;}
        if(material==null||!(bool)material||material.Pointer==IntPtr.Zero){reason="material-null";return false;}
        if(binding.Material!=null&&binding.Material.Pointer!=material.Pointer)
        {reason="binding-material";return false;}
        if(package==null||package.Source==null){reason="package-null";return false;}
        if(plan==null){reason="plan-null";return false;}
        PalaceProductionSource source=package.Source;
        if(plan.Nodes.Count!=RequiredPlacementCount||source.Placements.Count!=RequiredPlacementCount||
            source.Assets.Count!=RequiredAssetCount||package.ImageCount!=RequiredAssetCount)
        {reason="count:plan="+plan.Nodes.Count+"/placements="+source.Placements.Count
            +"/assets="+source.Assets.Count+"/images="+package.ImageCount;return false;}

        var indexById=new Dictionary<string,int>(RequiredPlacementCount,StringComparer.Ordinal);
        int timedPlacements=0;
        for(int i=0;i<plan.Nodes.Count;i++)
        {
            PalaceCapitalSceneNode node=plan.Nodes[i];
            PalaceProductionSource.Placement placement=source.Placements[i];
            if(node==null){reason="node-null:"+i;return false;}
            if(node.Placement==null||!ReferenceEquals(node.Placement,placement)
                ||node.Placement.Id!=placement.Id)
            {reason="plan-source:"+i;return false;}
            if(!source.TryGetAsset(placement.AssetId,out PalaceProductionSource.Asset asset)
                ||!ReferenceEquals(node.Asset,asset)||node.Asset.Id!=placement.AssetId)
            {reason="plan-asset:"+i;return false;}
            if(!indexById.TryAdd(placement.Id,i)){reason="placement-duplicate:"+placement.Id;return false;}
            if(node.Clips.Count>0)timedPlacements++;
        }
        if(timedPlacements!=RequiredTimedPlacementCount){reason="timed-placements:"+timedPlacements;return false;}
        if(RequiredPlacementCount-timedPlacements!=RequiredStaticPlacementCount)
        {reason="static-placements";return false;}

        if(depthByPlacement==null){reason="depth-null";return false;}
        if(depthByPlacement.Count!=RequiredPlacementCount)
        {reason="depth-count:"+depthByPlacement.Count;return false;}
        var worldZ=new float[RequiredPlacementCount];
        var seenDepth=new HashSet<string>(StringComparer.Ordinal);
        var seenFloat=new HashSet<float>();
        for(int i=0;i<source.Placements.Count;i++)
        {
            string id=source.Placements[i].Id;
            if(!depthByPlacement.TryGetValue(id,out double value)){reason="depth-missing:"+id;return false;}
            if(!PalaceCapitalScenePlan.IsFinite(value)||!float.IsFinite((float)value))
            {reason="depth-float:"+id;return false;}
            float narrow=(float)value;
            if(!seenFloat.Add(narrow)){reason="depth-tie:"+id;return false;}
            worldZ[i]=narrow;
            seenDepth.Add(id);
        }
        if(seenDepth.Count!=depthByPlacement.Count){reason="depth-extra";return false;}
        foreach(string key in depthByPlacement.Keys)
            if(key==null||!seenDepth.Contains(key)){reason="depth-extra:"+(key??"null");return false;}
        if(!TryValidateDepthOrder(source,indexById,worldZ,out reason))return false;

        if(clipByAsset==null){reason="clip-map-null";return false;}
        var timedAssets=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<plan.Nodes.Count;i++)
        {
            PalaceCapitalSceneNode node=plan.Nodes[i];
            if(node.Clips.Count>0)timedAssets.Add(node.Asset.Id);
        }
        if(timedAssets.Count!=RequiredTimedAssetCount||clipByAsset.Count!=RequiredTimedAssetCount)
        {reason="clip-count:"+clipByAsset.Count+"/"+timedAssets.Count;return false;}
        int staticAssets=0,actorTimedAssets=0,localTimedAssets=0;
        foreach(PalaceProductionSource.Asset asset in source.Assets)
        {
            if(asset.Frames.Count==0){staticAssets++;continue;}
            if(asset.Kind==PalaceProductionSource.AssetKinds.ActorAtlas)actorTimedAssets++;
            else if(asset.Kind==PalaceProductionSource.AssetKinds.LocalEnvironmentMotionAtlas)localTimedAssets++;
            else {reason="timed-kind:"+asset.Id;return false;}
        }
        if(staticAssets!=RequiredStaticAssetCount||actorTimedAssets!=RequiredActorTimedAssetCount
            ||localTimedAssets!=RequiredLocalTimedAssetCount)
        {reason="asset-kind-count:"+staticAssets+"/"+actorTimedAssets+"/"+localTimedAssets;return false;}
        foreach(string assetId in timedAssets)
        {
            if(!clipByAsset.TryGetValue(assetId,out string clipId)||string.IsNullOrEmpty(clipId))
            {reason="clip-missing:"+assetId;return false;}
            if(!source.TryGetAsset(assetId,out PalaceProductionSource.Asset asset))
            {reason="clip-asset:"+assetId;return false;}
            bool found=false;
            foreach(PalaceClip clip in asset.Clips)
                if(clip!=null&&clip.Id==clipId){found=true;break;}
            if(!found){reason="clip-unknown:"+assetId+":"+clipId;return false;}
        }
        foreach(string key in clipByAsset.Keys)
            if(key==null||!timedAssets.Contains(key)){reason="clip-extra:"+(key??"null");return false;}

        _nodes=new PalaceCapitalSceneNode[RequiredPlacementCount];
        _renderers=new SpriteRenderer[RequiredPlacementCount];
        _transforms=new Transform[RequiredPlacementCount];
        _worldZ=worldZ;
        _clipByNode=new string[RequiredPlacementCount];
        _frameSprites=new Sprite[RequiredPlacementCount][];
        _hasParallax=new bool[RequiredPlacementCount];
        _appliedSprites=new Sprite[RequiredPlacementCount];
        _appliedX=new float[RequiredPlacementCount];
        _appliedY=new float[RequiredPlacementCount];
        _nextPoses=new PalaceCapitalScenePose[RequiredPlacementCount];
        _nextSprites=new Sprite[RequiredPlacementCount];
        for(int i=0;i<plan.Nodes.Count;i++)_nodes[i]=plan.Nodes[i];

        var pivotsByAsset=new Dictionary<string,FramePivot[]>(RequiredTimedAssetCount,StringComparer.Ordinal);
        var timedNodeByAsset=new Dictionary<string,PalaceCapitalSceneNode>(RequiredTimedAssetCount,StringComparer.Ordinal);
        for(int i=0;i<plan.Nodes.Count;i++)
        {
            PalaceCapitalSceneNode node=plan.Nodes[i];
            if(node.Clips.Count>0)
            {
                _clipByNode[i]=clipByAsset[node.Asset.Id];
                if(!timedNodeByAsset.ContainsKey(node.Asset.Id))timedNodeByAsset.Add(node.Asset.Id,node);
            }
            else _hasParallax[i]=node.IsBackgroundCompound&&node.CameraFollowRatio!=0d;
        }
        foreach(var pair in timedNodeByAsset)
        {
            PalaceCapitalSceneNode node=pair.Value;
            PalaceProductionSource.Asset asset=node.Asset;
            if(asset.Frames.Count==0||asset.Clips.Count==0
                ||!asset.LogicalCellWidth.HasValue||!asset.LogicalCellHeight.HasValue
                ||asset.LogicalCellWidth.Value<=0||asset.LogicalCellHeight.Value<=0)
            {reason="timed-asset:"+asset.Id;return false;}
            var pivots=new FramePivot[asset.Frames.Count];
            var filled=new bool[asset.Frames.Count];
            foreach(PalaceClip assetClip in asset.Clips)
            {
                if(assetClip==null){reason="clip-null:"+asset.Id;return false;}
                if(!node.Clips.TryGetValue(assetClip.Id,out PalaceCapitalSceneClip resolved))
                {reason="clip-table:"+asset.Id+":"+assetClip.Id;return false;}
                if(resolved.Frames.Count!=assetClip.Indices.Count)
                {reason="clip-shape:"+asset.Id+":"+assetClip.Id;return false;}
                for(int k=0;k<resolved.Frames.Count;k++)
                {
                    double elapsed=k==0?0d:resolved.BoundariesSeconds[k-1];
                    if(!PalaceCapitalScenePlan.TryFrameAt(node,assetClip.Id,elapsed,0d,
                        out PalaceFrame frame,out PalaceCapitalScenePose pose,out string frameReason))
                    {reason="frame:"+asset.Id+":"+assetClip.Id+":"+frameReason;return false;}
                    if(frame==null||frame.Index<0||frame.Index>=asset.Frames.Count
                        ||!ReferenceEquals(asset.Frames[frame.Index],frame))
                    {reason="frame-index:"+asset.Id;return false;}
                    if(!PalaceCapitalScenePlan.IsFinite(pose.SpritePivotNormalizedX)
                        ||!PalaceCapitalScenePlan.IsFinite(pose.SpritePivotNormalizedY)
                        ||pose.SpritePivotNormalizedX<0d||pose.SpritePivotNormalizedX>1d
                        ||pose.SpritePivotNormalizedY<0d||pose.SpritePivotNormalizedY>1d)
                    {reason="pivot:"+asset.Id+":"+frame.Index;return false;}
                    if(filled[frame.Index])
                    {
                        if(pivots[frame.Index].X!=pose.SpritePivotNormalizedX
                            ||pivots[frame.Index].Y!=pose.SpritePivotNormalizedY)
                        {reason="pivot-disagreement:"+asset.Id+":"+frame.Index;return false;}
                    }
                    else
                    {
                        pivots[frame.Index]=new FramePivot(pose.SpritePivotNormalizedX,
                            pose.SpritePivotNormalizedY);
                        filled[frame.Index]=true;
                    }
                }
            }
            for(int frameIndex=0;frameIndex<filled.Length;frameIndex++)
                if(!filled[frameIndex]){reason="frame-uncovered:"+asset.Id+":"+frameIndex;return false;}
            pivotsByAsset.Add(asset.Id,pivots);
        }

        for(int i=0;i<plan.Nodes.Count;i++)
        {
            PalaceCapitalSceneNode node=plan.Nodes[i];
            bool timed=_clipByNode[i]!=null;
            if(!PalaceCapitalScenePlan.TryFrameAt(node,timed?_clipByNode[i]:null,0d,0d,
                out PalaceFrame frame,out PalaceCapitalScenePose pose,out string frameReason))
            {reason="initial-frame:"+node.Placement.Id+":"+frameReason;return false;}
            if(timed!=(frame!=null)){reason="initial-frame-kind:"+node.Placement.Id;return false;}
            if(!IsFinitePose(pose)){reason="initial-pose:"+node.Placement.Id;return false;}
            if(timed)
            {
                PalaceProductionSource.Asset asset=node.Asset;
                if(frame.Index<0||frame.Index>=asset.Frames.Count)
                {reason="initial-index:"+node.Placement.Id;return false;}
                PalaceActorRegistry.Rect rect=frame.SourceRectTopLeft;
                if(rect.X<0||rect.Y<0||rect.Width<=0||rect.Height<=0
                    ||(long)rect.X+rect.Width>asset.Width||(long)rect.Y+rect.Height>asset.Height)
                {reason="source-rect:"+node.Placement.Id;return false;}
                if(!pivotsByAsset.ContainsKey(asset.Id))
                {reason="pivot-table:"+asset.Id;return false;}
            }
        }

        int gateIndex=-1;
        for(int i=0;i<plan.Nodes.Count;i++)
        {
            if(plan.Nodes[i].Asset.Id!=GateAssetId)continue;
            if(gateIndex>=0){reason="gate-not-unique";return false;}
            gateIndex=i;
        }
        if(gateIndex<0){reason="gate-missing";return false;}
        PalaceCapitalSceneNode gate=plan.Nodes[gateIndex];
        PalaceProductionSource.InteractionAnchor anchor=gate.Asset.InteractionAnchor;
        if(anchor==null||anchor.Id!=PalaceProductionSource.RequiredInteractionId)
        {reason="gate-anchor";return false;}
        if(!PalaceCapitalScenePlan.IsFinite(anchor.Point.X)||!PalaceCapitalScenePlan.IsFinite(anchor.Point.Y))
        {reason="gate-anchor-point";return false;}
        double gateX=gate.SupportWorldX
            -(gate.Placement.SupportLocalPixel.X-anchor.Point.X)*plan.UnitsPerPixel;
        double gateY=gate.ReferencePlaneWorldY+gate.LayoutOffsetYUnits
            +gate.ScaleY*(gate.Placement.SupportLocalPixel.Y-anchor.Point.Y)*plan.UnitsPerPixel;
        if(!PalaceCapitalScenePlan.IsFloatRepresentable(gateX)
            ||!PalaceCapitalScenePlan.IsFloatRepresentable(gateY))
        {reason="gate-geometry";return false;}
        _gateAnchorX=(float)gateX;
        _gateAnchorY=(float)gateY;
        _gateAnchorZ=_worldZ[gateIndex];

        _parent=parent;
        _reference=reference;
        _material=material;
        _referencePointer=reference.Pointer;
        _materialPointer=material.Pointer;
        _sceneHandle=sceneHandle;
        _lastElapsed=0d;
        _lastCameraX=plan.AnchorWorldX;
        _pivotsByAsset=pivotsByAsset;
        return true;
    }

    private float _gateAnchorX;
    private float _gateAnchorY;
    private float _gateAnchorZ;
    private Dictionary<string,FramePivot[]> _pivotsByAsset;

    private static bool TryValidateDepthOrder(PalaceProductionSource source,
        IReadOnlyDictionary<string,int> indexById,float[] worldZ,out string reason)
    {
        reason="";
        var roles=new List<KeyValuePair<string,int>>(source.RelativeDrawOrderHints);
        roles.Sort((a,b)=>a.Value.CompareTo(b.Value));
        for(int i=1;i<roles.Count;i++)
            if(roles[i-1].Value==roles[i].Value){reason="depth-role-tie:"+roles[i].Key;return false;}
        int covered=0;
        int previous=-1;
        foreach(var role in roles)
        {
            foreach(PalaceProductionSource.Placement placement in source.Placements)
            {
                if(placement.DrawRole!=role.Key)continue;
                if(!indexById.TryGetValue(placement.Id,out int index))
                {reason="depth-index:"+placement.Id;return false;}
                if(previous>=0&&!(worldZ[previous]>worldZ[index]))
                {reason="depth-order:"+placement.Id;return false;}
                previous=index;
                covered++;
            }
        }
        if(covered!=source.Placements.Count){reason="depth-covered:"+covered;return false;}
        return true;
    }

    private void Build(PalaceCapitalPackage package)
    {
        _root=new GameObject("palace-capital-scene");
        _root.SetActive(false);
        _root.layer=_reference.gameObject.layer;
        _root.transform.SetParent(_parent,false);
        _root.transform.localPosition=Vector3.zero;
        _root.transform.localScale=Vector3.one;
        _rootPointer=_root.Pointer;
        if(_rootPointer==IntPtr.Zero)throw new InvalidOperationException("root pointer is zero");

        var gate=new GameObject("palace-capital-gate-anchor");
        _pendingObjects.Add(gate);
        gate.layer=_reference.gameObject.layer;
        gate.transform.SetParent(_root.transform,false);
        _pendingObjects.Remove(gate);
        gate.transform.position=new Vector3(_gateAnchorX,_gateAnchorY,_gateAnchorZ);
        _gateAnchor=gate.transform;

        var staticSprites=new Dictionary<string,Sprite>(StringComparer.Ordinal);
        var frameSprites=new Dictionary<string,Sprite[]>(StringComparer.Ordinal);
        foreach(PalaceProductionSource.Asset asset in package.Source.Assets)
        {
            byte[] bytes=package.CopyPngBytes(asset.Id);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            _textures.Add(texture);
            _uniqueTextures=_textures.Count;
            texture.name="palace-capital-"+asset.Id;
            if(!ImageConversion.LoadImage(texture,bytes,false))
                throw new InvalidOperationException("LoadImage failed: "+asset.Id);
            if(texture.width!=asset.Width||texture.height!=asset.Height)
                throw new InvalidOperationException("texture size: "+asset.Id+":"+texture.width+"x"+texture.height
                    +"/"+asset.Width+"x"+asset.Height);
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.anisoLevel=0;
            if(asset.Frames.Count==0)
            {
                Sprite sprite=CreateSprite(texture,asset,0,0,asset.Width,asset.Height,0d,0d);
                staticSprites.Add(asset.Id,sprite);
                continue;
            }
            if(!_pivotsByAsset.TryGetValue(asset.Id,out FramePivot[] pivots))
                throw new InvalidOperationException("missing pivot table: "+asset.Id);
            var byKey=new Dictionary<FrameKey,Sprite>(asset.Frames.Count);
            var sprites=new Sprite[asset.Frames.Count];
            for(int frameIndex=0;frameIndex<asset.Frames.Count;frameIndex++)
            {
                PalaceFrame frame=asset.Frames[frameIndex];
                PalaceActorRegistry.Rect rect=frame.SourceRectTopLeft;
                FramePivot pivot=pivots[frameIndex];
                var key=new FrameKey(rect.X,rect.Y,rect.Width,rect.Height,pivot.X,pivot.Y);
                if(!byKey.TryGetValue(key,out Sprite sprite))
                {
                    sprite=CreateSprite(texture,asset,rect.X,rect.Y,rect.Width,rect.Height,pivot.X,pivot.Y);
                    byKey.Add(key,sprite);
                }
                sprites[frameIndex]=sprite;
            }
            frameSprites.Add(asset.Id,sprites);
        }
        if(_textures.Count!=RequiredAssetCount)
            throw new InvalidOperationException("texture count: "+_textures.Count);

        for(int i=0;i<_nodes.Length;i++)
        {
            PalaceCapitalSceneNode node=_nodes[i];
            PalaceCapitalScenePose pose;
            Sprite sprite;
            if(_clipByNode[i]!=null)
            {
                if(!PalaceCapitalScenePlan.TryFrameAt(node,_clipByNode[i],0d,0d,
                    out PalaceFrame frame,out pose,out string frameReason))
                    throw new InvalidOperationException("initial frame: "+node.Placement.Id+":"+frameReason);
                if(!frameSprites.TryGetValue(node.Asset.Id,out Sprite[] frames)
                    ||frame.Index<0||frame.Index>=frames.Length)
                    throw new InvalidOperationException("frame cache: "+node.Placement.Id);
                sprite=frames[frame.Index];
                _frameSprites[i]=frames;
            }
            else
            {
                if(!PalaceCapitalScenePlan.TryFrameAt(node,null,0d,0d,out _,out pose,out string frameReason))
                    throw new InvalidOperationException("static pose: "+node.Placement.Id+":"+frameReason);
                if(!staticSprites.TryGetValue(node.Asset.Id,out sprite))
                    throw new InvalidOperationException("static sprite: "+node.Placement.Id);
            }
            if(sprite==null)throw new InvalidOperationException("sprite missing: "+node.Placement.Id);

            var go=new GameObject("palace-capital-"+node.Placement.Id);
            _pendingObjects.Add(go);
            go.layer=_reference.gameObject.layer;
            go.transform.SetParent(_root.transform,false);
            _pendingObjects.Remove(go);
            go.transform.position=new Vector3((float)pose.WorldX,(float)pose.WorldY,_worldZ[i]);
            go.transform.localScale=new Vector3(1f,(float)pose.ScaleY,1f);
            var renderer=go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID=_reference.sortingLayerID;
            renderer.sortingOrder=0;
            renderer.sharedMaterial=_material;
            renderer.sprite=sprite;
            _renderers[i]=renderer;
            _transforms[i]=go.transform;
            _appliedSprites[i]=sprite;
            _appliedX[i]=(float)pose.WorldX;
            _appliedY[i]=(float)pose.WorldY;
            _placedSprites++;
        }
        if(_placedSprites!=RequiredPlacementCount)
            throw new InvalidOperationException("renderer count: "+_placedSprites);
    }

    private Sprite CreateSprite(Texture2D texture,PalaceProductionSource.Asset asset,
        int sourceX,int sourceY,int width,int height,double pivotX,double pivotY)
    {
        if(texture==null||sourceX<0||sourceY<0||width<=0||height<=0
            ||(long)sourceX+width>texture.width||(long)sourceY+height>texture.height)
            throw new InvalidOperationException("sprite rect: "+asset.Id);
        float bottomLeftY=texture.height-sourceY-height;
        var rect=new Rect(sourceX,bottomLeftY,width,height);
        var pivot=new Vector2((float)pivotX,(float)pivotY);
        Sprite sprite=Sprite.Create(texture,rect,pivot,RequiredPixelsPerUnit,0u,SpriteMeshType.FullRect);
        if(sprite==null)throw new InvalidOperationException("Sprite.Create failed: "+asset.Id);
        _sprites.Add(sprite);
        _spriteCount=_sprites.Count;
        sprite.name="palace-capital-"+asset.Id+"-"+sourceX+"-"+sourceY;
        return sprite;
    }

    private static bool IsFinitePose(PalaceCapitalScenePose pose)
        =>PalaceCapitalScenePlan.IsFloatRepresentable(pose.WorldX)
        &&PalaceCapitalScenePlan.IsFloatRepresentable(pose.WorldY)
        &&PalaceCapitalScenePlan.IsFloatRepresentable(pose.ScaleX)
        &&PalaceCapitalScenePlan.IsFloatRepresentable(pose.ScaleY)
        &&PalaceCapitalScenePlan.IsFloatRepresentable(pose.SpritePivotNormalizedX)
        &&PalaceCapitalScenePlan.IsFloatRepresentable(pose.SpritePivotNormalizedY);
}
