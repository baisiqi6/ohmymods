// Capital400 visual-owner runtime (source-only). One Capital400 CourtScene branch creates this
// scene exactly once: explicit descriptor calibration, the existing Kingdom pipeline check from
// PalaceEnvironmentSortPlanes, the reviewed 452-placement depth map, the S1 plan and the S2b
// drawing adapter. The frozen P1/MainCamera/Camera/Marshaller identity and the borrowed
// reference/material/layer/queue/unitZ are revalidated every owned frame; any drift is a fault
// that stops the owner (no re-pick, Camera.main, scan, retry or legacy44 fallback).
//
// Zero side effects: never writes wallet/player position/paid journal/save/durable state, never
// destroys borrowed objects, never selects clips or sorting values here. A failed Create or Tick
// clears ReturnDoor in the caller and clears owned objects; an unknown cleanup keeps the runtime
// object (and therefore the visual owner + outer root) for the caller to retain.
using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Capital-only outer root ownership. A deactivate/destroy exception keeps the reference for the
/// caller to retain; a later explicit attempt first re-checks Unity liveness so an already
/// destroyed object is never destroyed twice. Borrowed objects are never touched.
/// </summary>
internal static class PalaceCapitalOuterRoot
{
    internal static bool TryClear(ref GameObject root,out string detail)
    {
        detail="";
        GameObject current=root;
        if(current==null){root=null;return true;}
        try
        {
            if(!(bool)current){root=null;detail="already-destroyed";return true;}
        }
        catch(Exception e){detail="alive-check:"+e.GetType().Name;return false;}
        try{current.SetActive(false);}
        catch(Exception e){detail="deactivate:"+e.GetType().Name;return false;}
        try
        {
            if(!(bool)current){root=null;detail="destroyed-during-deactivate";return true;}
            UnityEngine.Object.Destroy(current);
            root=null;
            return true;
        }
        catch(Exception e){detail="destroy:"+e.GetType().Name;return false;}
    }
}

internal sealed class PalaceCapitalRuntimeScene
{
    private const int MaxCameraRefs=4;
    private const int MaxCameraTargets=4;

    private readonly PalaceCapitalScene _visual;
    private readonly CameraIdentity _camera;
    private readonly ReferenceIdentity _reference;
    private double _elapsed;
    private bool _faulted;
    private bool _cleared;

    internal Transform GateAnchor=>_cleared?null:_visual.GateAnchor;
    internal int PlacedSprites=>_visual.PlacedSprites;
    internal int UniqueTextures=>_visual.UniqueTextures;
    internal int Sprites=>_visual.Sprites;
    internal double Elapsed=>_elapsed;

    private PalaceCapitalRuntimeScene(PalaceCapitalScene visual,CameraIdentity camera,ReferenceIdentity reference,
        bool faulted)
    {
        _visual=visual;_camera=camera;_reference=reference;_faulted=faulted;
    }

    /// <summary>Playing advances by the scaled delta; Menu validates ownership but freezes time.</summary>
    internal bool Tick(Game game,double scaledDelta,out string reason)
    {
        reason="";
        if(_cleared){reason="cleared";return false;}
        if(_faulted){reason="faulted";return false;}
        if(game==null){reason="game-null";_faulted=true;return false;}
        if(!TryValidateOwner(game,out reason)){_faulted=true;return false;}
        Game.State state;
        try{state=game.state;}catch(Exception e){reason="state-read:"+e.GetType().Name;_faulted=true;return false;}
        if(state!=Game.State.Playing&&state!=Game.State.Menu)
        {reason="state:"+state;_faulted=true;return false;}
        bool playing=state==Game.State.Playing;
        if(!PalaceCapitalRuntime.AdvanceElapsed(ref _elapsed,playing,scaledDelta,out reason))
        {_faulted=true;return false;}
        if(!playing)return true;
        float cameraX;
        try{cameraX=_camera.Camera.transform.position.x;}
        catch(Exception e){reason="camera-x:"+e.GetType().Name;_faulted=true;return false;}
        if(!float.IsFinite(cameraX)){reason="camera-x";_faulted=true;return false;}
        if(!_visual.Tick(_elapsed,cameraX,out reason)){_faulted=true;return false;}
        return true;
    }

    internal bool TryClear(out string reason)
    {
        reason="";
        if(_cleared)return true;
        if(!_visual.TryClear(out reason))
        {
            _faulted=true;
            return false;
        }
        _cleared=true;
        _faulted=false;
        return true;
    }

    private bool TryValidateOwner(Game game,out string reason)
    {
        reason="";
        try
        {
            Managers managers=Managers.Inst;
            if(managers==null||managers.game==null||managers.kingdom==null)
            {reason="managers";return false;}
            if(managers.Pointer!=_camera.ManagersPointer||managers.game.Pointer!=_camera.GamePointer
                ||managers.kingdom.Pointer!=_camera.KingdomPointer||game.Pointer!=_camera.GamePointer)
            {reason="owner-changed";return false;}
            if(managers.level==null||managers.level.Pointer!=_camera.LevelPointer)
            {reason="level-changed";return false;}
            if(managers.world==null||managers.world.Pointer!=_camera.WorldPointer
                ||managers.world.gameLayer==null||managers.world.gameLayer.Pointer!=_camera.GameLayerPointer)
            {reason="world-layer-changed";return false;}
            CampaignSaveData currentCampaign=CampaignSaveData.current;
            if(currentCampaign==null||currentCampaign.Pointer!=_camera.CampaignPointer)
            {reason="campaign-changed";return false;}
            Player player=managers.kingdom.playerOne;
            if(player==null||player.Pointer!=_camera.PlayerPointer||player.gameObject==null
                ||player.gameObject.Pointer!=_camera.PlayerGoPointer)
            {reason="player-one-changed";return false;}
            CameraMarshaller marshaller=CameraMarshaller.Inst;
            if(marshaller==null||marshaller.Pointer!=_camera.MarshallerPointer)
            {reason="marshaller-changed";return false;}
            var refs=marshaller.cameraRefs;
            if(refs==null||refs.Length<1||refs.Length>MaxCameraRefs)
            {reason="camera-refs";return false;}
            int mainHits=0,targetHits=0;
            for(int i=0;i<refs.Length;i++)
            {
                MainCamera candidate=refs[i];
                if(candidate==null||!(bool)candidate)continue;
                if(candidate.Pointer==_camera.MainCameraPointer)mainHits++;
                var targets=candidate.cameraTargets;
                if(targets==null)continue;
                if(targets.Length>MaxCameraTargets){reason="camera-targets";return false;}
                bool targetsPlayer=false;
                for(int t=0;t<targets.Length;t++)
                {
                    GameObject targetGo=targets[t];
                    if(targetGo!=null&&targetGo.Pointer==_camera.PlayerGoPointer){targetsPlayer=true;break;}
                }
                if(!targetsPlayer)continue;
                targetHits++;
                if(candidate.Pointer!=_camera.MainCameraPointer)
                {reason="player-one-target-repicked";return false;}
            }
            if(mainHits!=1||targetHits!=1){reason="camera-relationship";return false;}
            MainCamera mainCamera=_camera.MainCamera;
            if(mainCamera==null||!(bool)mainCamera||mainCamera.Pointer!=_camera.MainCameraPointer)
            {reason="main-camera";return false;}
            Camera camera=mainCamera._camera;
            if(camera==null||camera.Pointer!=_camera.CameraPointer)
            {reason="camera-association";return false;}
            if(_camera.Camera==null||!(_camera.Camera)||!camera.enabled||camera.gameObject==null
                ||!camera.gameObject.activeInHierarchy
                ||camera.gameObject.scene.handle!=_camera.SceneHandle)
            {reason="camera-live";return false;}
            if(!camera.orthographic){reason="camera-orthographic";return false;}
            if(!float.IsFinite(camera.orthographicSize)||!(camera.orthographicSize>0f)
                ||!float.IsFinite(camera.aspect)||!(camera.aspect>0f))
            {reason="camera-size";return false;}
            Vector3 position=camera.transform.position;
            if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z))
            {reason="camera-position";return false;}
            if(position.z!=_camera.CameraZ){reason="camera-z-drift";return false;}
            Vector3 forward=camera.transform.forward;
            if(!float.IsFinite(forward.x)||!float.IsFinite(forward.y)||!float.IsFinite(forward.z)
                ||forward.x!=_camera.ForwardX||forward.y!=_camera.ForwardY||forward.z!=_camera.ForwardZ)
            {reason="camera-forward-drift";return false;}
            float near=camera.nearClipPlane,far=camera.farClipPlane;
            if(!float.IsFinite(near)||!float.IsFinite(far)||!(near>0f)||!(far>near))
            {reason="camera-clip";return false;}
            if(near!=_camera.NearClip||far!=_camera.FarClip){reason="camera-clip-drift";return false;}
            float minDepth=_camera.MinWorldZ-position.z;
            float maxDepth=_camera.MaxWorldZ-position.z;
            if(!float.IsFinite(minDepth)||!float.IsFinite(maxDepth)
                ||!(minDepth>near)||!(maxDepth<far))
            {reason="camera-depth-clip";return false;}

            if(!TryValidateReference(player,managers.world.gameLayer,out reason))return false;
            return true;
        }
        catch(Exception e)
        {
            reason="owner-fault:"+e.GetType().Name;
            return false;
        }
    }

    private enum ReferenceRoute
    {
        SteedSprite=0,
        SteedRenderer=1,
        RiderRenderer=2,
    }

    private sealed class CameraIdentity
    {
        internal IntPtr ManagersPointer;
        internal IntPtr GamePointer;
        internal IntPtr KingdomPointer;
        internal IntPtr PlayerPointer;
        internal IntPtr PlayerGoPointer;
        internal IntPtr MarshallerPointer;
        internal IntPtr MainCameraPointer;
        internal IntPtr CameraPointer;
        internal IntPtr LevelPointer;
        internal IntPtr WorldPointer;
        internal IntPtr GameLayerPointer;
        internal IntPtr CampaignPointer;
        internal MainCamera MainCamera;
        internal Camera Camera;
        internal int SceneHandle;
        internal float CameraZ;
        internal float NearClip;
        internal float FarClip;
        internal float ForwardX;
        internal float ForwardY;
        internal float ForwardZ;
        internal float MinWorldZ;
        internal float MaxWorldZ;
    }

    private sealed class ReferenceIdentity
    {
        internal SpriteRenderer Reference;
        internal IntPtr Pointer;
        internal IntPtr MaterialPointer;
        internal IntPtr ParentPointer;
        internal IntPtr LayerPointer;
        internal int SceneHandle;
        internal int GoLayer;
        internal int SortingLayerId;
        internal int SortingOrder;
        internal int RenderQueue;
        internal float UnitZ;
        internal ReferenceRoute Route;
    }

    private bool TryValidateReference(Player player,Transform layer,out string reason)
    {
        reason="";
        try
        {
            SpriteRenderer reference=_reference.Reference;
            if(reference==null||!(bool)reference||reference.Pointer!=_reference.Pointer)
            {reason="reference";return false;}
            if(!reference.enabled){reason="reference-disabled";return false;}
            GameObject go=reference.gameObject;
            if(go==null||go.Pointer==IntPtr.Zero||!go.activeInHierarchy
                ||go.scene.handle!=_reference.SceneHandle||go.layer!=_reference.GoLayer)
            {reason="reference-scene-layer";return false;}
            if(reference.sprite==null){reason="reference-sprite";return false;}
            if(reference.sortingLayerID!=_reference.SortingLayerId
                ||reference.sortingOrder!=_reference.SortingOrder)
            {reason="reference-sort";return false;}
            Material material=reference.sharedMaterial;
            if(material==null||!(bool)material||material.Pointer!=_reference.MaterialPointer
                ||material.renderQueue!=_reference.RenderQueue)
            {reason="reference-material";return false;}
            float unitZ=reference.transform.position.z;
            if(!float.IsFinite(unitZ)||unitZ!=_reference.UnitZ){reason="reference-unit-z";return false;}
            Transform parent=reference.transform.parent;
            if(parent==null||parent.Pointer!=_reference.ParentPointer)
            {reason="reference-parent";return false;}
            if(!reference.transform.IsChildOf(layer)){reason="reference-hierarchy";return false;}
            if(!TryRouteRenderer(player,_reference.Route,out SpriteRenderer current,out string routeReason)
                ||current==null||current.Pointer!=_reference.Pointer)
            {reason="reference-route:"+routeReason;return false;}
            return true;
        }
        catch(Exception e)
        {
            reason="reference-fault:"+e.GetType().Name;
            return false;
        }
    }

    private static bool TryCaptureReference(Player player,SpriteRenderer reference,Transform layer,
        out ReferenceIdentity identity,out string reason)
    {
        identity=null;reason="";
        try
        {
            if(player==null||reference==null||!(bool)reference||!reference.enabled)
            {reason="reference-live";return false;}
            GameObject go=reference.gameObject;
            if(go==null||go.Pointer==IntPtr.Zero||!go.activeInHierarchy)
            {reason="reference-active";return false;}
            if(reference.sprite==null){reason="reference-sprite";return false;}
            if(go.scene.handle!=layer.gameObject.scene.handle||!reference.transform.IsChildOf(layer))
            {reason="reference-scene-layer";return false;}
            Transform parent=reference.transform.parent;
            if(parent==null||parent.Pointer==IntPtr.Zero){reason="reference-parent";return false;}
            ReferenceRoute route;
            SpriteRenderer current;
            if(TryRouteRenderer(player,ReferenceRoute.SteedSprite,out current,out _)
                &&current!=null&&current.Pointer==reference.Pointer)route=ReferenceRoute.SteedSprite;
            else if(TryRouteRenderer(player,ReferenceRoute.SteedRenderer,out current,out _)
                &&current!=null&&current.Pointer==reference.Pointer)route=ReferenceRoute.SteedRenderer;
            else if(TryRouteRenderer(player,ReferenceRoute.RiderRenderer,out current,out _)
                &&current!=null&&current.Pointer==reference.Pointer)route=ReferenceRoute.RiderRenderer;
            else {reason="reference-not-frozen-p1";return false;}
            Material material=reference.sharedMaterial;
            if(material==null||!(bool)material){reason="reference-material";return false;}
            float unitZ=reference.transform.position.z;
            if(!float.IsFinite(unitZ)){reason="reference-unit-z";return false;}
            identity=new ReferenceIdentity
            {
                Reference=reference,Pointer=reference.Pointer,MaterialPointer=material.Pointer,
                ParentPointer=parent.Pointer,LayerPointer=layer.Pointer,
                SceneHandle=go.scene.handle,GoLayer=go.layer,
                SortingLayerId=reference.sortingLayerID,SortingOrder=reference.sortingOrder,
                RenderQueue=material.renderQueue,UnitZ=unitZ,Route=route,
            };
            return true;
        }
        catch(Exception e){reason="reference-fault:"+e.GetType().Name;return false;}
    }

    private static bool TryRouteRenderer(Player player,ReferenceRoute route,out SpriteRenderer renderer,
        out string reason)
    {
        renderer=null;reason="";
        try
        {
            if(route==ReferenceRoute.SteedSprite)
            {
                Steed mount=player.steed;
                Player rider=mount!=null?mount.Rider:null;
                if(rider==null||rider.Pointer!=player.Pointer){reason="steed-rider";return false;}
                SpriteRendererFX fx=mount.SpriteFX;
                renderer=fx!=null?fx.Renderer:null;
                if(renderer==null){reason="steed-sprite";return false;}
                return true;
            }
            if(route==ReferenceRoute.SteedRenderer)
            {
                renderer=player._steedRenderer;
                if(renderer==null){reason="steed-renderer";return false;}
                return true;
            }
            renderer=player._riderRenderer;
            if(renderer==null){reason="rider-renderer";return false;}
            return true;
        }
        catch(Exception e){reason="route:"+e.GetType().Name;return false;}
    }

    internal static bool TryCreate(Kingdom kingdom,Transform layer,CourtBinding binding,
        PalaceCourtLayoutView view,PalaceScrollCalibration calibration,SpriteRenderer reference,
        out PalaceCapitalRuntimeScene scene,out string reason)
    {
        scene=null;reason="";
        PalaceCapitalScene visual=null;
        CameraIdentity camera=null;
        ReferenceIdentity referenceIdentity=null;
        float unitZ=0f;
        try
        {
            if(!PalaceCapitalRuntime.IsFinite(calibration!=null?calibration.UnitsPerPixel:double.NaN))
            {reason="calibration";return false;}
            Managers managers=Managers.Inst;
            if(managers==null||managers.game==null||managers.level==null||managers.kingdom==null
                ||managers.world==null||managers.world.gameLayer==null)
            {reason="managers";return false;}
            Game game=managers.game;
            if(kingdom==null||managers.kingdom.Pointer!=kingdom.Pointer){reason="kingdom";return false;}
            if(layer==null||managers.world.gameLayer.Pointer!=layer.Pointer){reason="layer";return false;}
            if(game.currentLand!=PalaceLand12Runtime.LandIndex){reason="land";return false;}
            CampaignSaveData campaign=CampaignSaveData.current;
            if(campaign==null||campaign.CurrentLand!=PalaceLand12Runtime.LandIndex)
            {reason="campaign-land";return false;}
            if(view==null||binding==null||binding.Parent==null||reference==null)
            {reason="owner-input";return false;}
            if(!ReferenceEquals(view.Recipe,PalaceLayoutRecipe.Capital400)){reason="view-recipe";return false;}
            if(view.LevelPointer!=(ulong)managers.level.Pointer.ToInt64()
                ||view.WorldLayerPointer!=(ulong)layer.Pointer.ToInt64()
                ||view.GamePointer!=(ulong)game.Pointer.ToInt64()
                ||view.CampaignPointer!=(ulong)campaign.Pointer.ToInt64())
            {reason="view-owner";return false;}
            if(calibration==null||!ReferenceEquals(calibration.Descriptor!=null?calibration.Descriptor.Recipe:null,
                PalaceLayoutRecipe.Capital400))
            {reason="calibration-recipe";return false;}
            if(binding.Parent.parent==null||binding.Parent.parent.Pointer!=layer.Pointer)
            {reason="binding-parent";return false;}
            if(reference.Pointer==IntPtr.Zero||reference.gameObject==null
                ||reference.gameObject.scene.handle!=layer.gameObject.scene.handle)
            {reason="reference-scene";return false;}

            PalaceCapitalRuntime.Warm();
            if(!PalaceCapitalRuntime.TryGetPackage(out PalaceCapitalPackage package,out string packageReason))
            {reason="package:"+packageReason;return false;}

            if(!TryBindCamera(managers,kingdom,layer.gameObject.scene.handle,out camera,out reason))
                return false;
            if(!TryCaptureReference(managers.kingdom.playerOne,reference,layer,out referenceIdentity,out reason))
                return false;

            Vector3 frozenForward=camera.Camera.transform.forward;
            if(!float.IsFinite(frozenForward.x)||!float.IsFinite(frozenForward.y)||!float.IsFinite(frozenForward.z))
            {reason="camera-forward";return false;}
            camera.ForwardX=frozenForward.x;
            camera.ForwardY=frozenForward.y;
            camera.ForwardZ=frozenForward.z;
            camera.LevelPointer=managers.level.Pointer;
            camera.WorldPointer=managers.world.Pointer;
            camera.GameLayerPointer=managers.world.gameLayer.Pointer;
            camera.CampaignPointer=campaign.Pointer;

            unitZ=reference.transform.position.z;
            if(!float.IsFinite(unitZ)){reason="unit-z";return false;}
            Vector3 cameraPosition=camera.Camera.transform.position;
            float nearClip=camera.Camera.nearClipPlane,farClip=camera.Camera.farClipPlane;
            if(!PalaceEnvironmentSortPlanes.TryCreate(game,reference,camera.Camera,calibration.UnitsPerPixel,
                out PalaceEnvironmentSortPlanes planes,out reason))
                return false;
            if(!planes.TryPlane(-7,0,out float rearFar,out reason)
                ||!planes.TryPlane(-1,0,out float rearNear,out reason)
                ||!planes.TryPlane(1,0,out float frontFar,out reason)
                ||!planes.TryPlane(2,25,out float frontNear,out reason))
                return false;

            if(!PalaceCapitalRuntime.TryBuildDepthMap(package.Source,unitZ,rearFar,rearNear,frontFar,frontNear,
                cameraPosition.z,nearClip,farClip,out var depthByPlacement,out reason))
                return false;
            if(!PalaceCapitalRuntime.TryGetWorldZEnvelope(depthByPlacement,
                out float minWorldZ,out float maxWorldZ,out reason))
                return false;
            camera.CameraZ=cameraPosition.z;
            camera.NearClip=nearClip;
            camera.FarClip=farClip;
            camera.MinWorldZ=minWorldZ;
            camera.MaxWorldZ=maxWorldZ;

            if(!PalaceCapitalScenePlan.TryCreate(package.Source,calibration,PalaceCapitalRuntime.WaterReferenceY,
                PalaceCapitalRuntime.ActorYScaleById,PalaceCapitalRuntime.BackgroundFollowRatioByGroup,
                out PalaceCapitalScenePlan plan,out reason))
                return false;

            if(!PalaceCapitalScene.Create(binding,package,plan,depthByPlacement,
                PalaceCapitalRuntime.ClipByAssetId,out visual,out reason))
            {
                if(visual==null)return false;
                scene=NewRetained(visual,camera,referenceIdentity,true);
                return false;
            }

            if(!visual.Activate(out reason))
            {
                if(!visual.TryClear(out string clearReason))
                {
                    scene=NewRetained(visual,camera,referenceIdentity,true);
                    reason=reason+"; cleanup-retained:"+clearReason;
                    return false;
                }
                visual=null;
                return false;
            }

            scene=NewRetained(visual,camera,referenceIdentity,false);
            if(scene._faulted)
            {
                if(!scene.TryClear(out string clearReason))
                { reason="identity-read; cleanup-retained:"+clearReason; return false; }
                scene=null;
                reason="identity-read";
                return false;
            }
            if(!scene.TryValidateOwner(game,out reason))
            {
                string ownerReason=reason;
                if(!scene.TryClear(out string clearReason))
                { reason="post-create:"+ownerReason+"; cleanup-retained:"+clearReason; return false; }
                scene=null;
                reason="post-create:"+ownerReason;
                return false;
            }
            return true;
        }
        catch(Exception e)
        {
            if(visual!=null)
            {
                if(!visual.TryClear(out string clearReason))
                {
                    if(camera!=null&&referenceIdentity!=null)
                        scene=NewRetained(visual,camera,referenceIdentity,true);
                    else
                        scene=null;
                    reason="create-fault:"+e.GetType().Name+"; cleanup-retained:"+clearReason;
                    return false;
                }
            }
            scene=null;
            reason="create-fault:"+e.GetType().Name;
            return false;
        }
    }

    private static PalaceCapitalRuntimeScene NewRetained(PalaceCapitalScene visual,CameraIdentity camera,
        ReferenceIdentity reference,bool faulted)
    {
        return new PalaceCapitalRuntimeScene(visual,camera,reference,faulted);
    }

    private static bool TryBindCamera(Managers managers,Kingdom kingdom,int sceneHandle,
        out CameraIdentity identity,out string reason)
    {
        identity=null;reason="";
        try
        {
            Player player=kingdom.playerOne;
            if(player==null||!(bool)player||player.gameObject==null)
            {reason="camera-player-one";return false;}
            GameObject target=player.gameObject;
            CameraMarshaller marshaller=CameraMarshaller.Inst;
            if(marshaller==null||marshaller.Pointer==IntPtr.Zero){reason="camera-marshaller";return false;}
            var refs=marshaller.cameraRefs;
            if(refs==null||refs.Length<1||refs.Length>MaxCameraRefs)
            {reason="camera-refs";return false;}
            int hits=0;
            MainCamera match=null;
            for(int i=0;i<refs.Length;i++)
            {
                MainCamera candidate=refs[i];
                if(candidate==null||!(bool)candidate)continue;
                var targets=candidate.cameraTargets;
                if(targets==null)continue;
                if(targets.Length>MaxCameraTargets){reason="camera-targets";return false;}
                bool hit=false;
                for(int t=0;t<targets.Length;t++)
                {
                    GameObject go=targets[t];
                    if(go!=null&&go.Pointer==target.Pointer){hit=true;break;}
                }
                if(!hit)continue;
                hits++;
                match=candidate;
            }
            if(hits!=1||match==null){reason="camera-not-unique:"+hits;return false;}
            Camera camera=match._camera;
            if(camera==null||!(bool)camera||!camera.enabled||camera.gameObject==null
                ||!camera.gameObject.activeInHierarchy)
            {reason="camera-live";return false;}
            if(camera.gameObject.scene.handle!=sceneHandle){reason="camera-scene";return false;}
            if(!camera.orthographic){reason="camera-orthographic";return false;}
            if(!float.IsFinite(camera.orthographicSize)||!(camera.orthographicSize>0f)
                ||!float.IsFinite(camera.aspect)||!(camera.aspect>0f))
            {reason="camera-size";return false;}
            Vector3 position=camera.transform.position;
            if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z))
            {reason="camera-position";return false;}
            identity=new CameraIdentity
            {
                ManagersPointer=managers.Pointer,GamePointer=managers.game.Pointer,
                KingdomPointer=kingdom.Pointer,PlayerPointer=player.Pointer,
                PlayerGoPointer=target.Pointer,MarshallerPointer=marshaller.Pointer,
                MainCameraPointer=match.Pointer,CameraPointer=camera.Pointer,
                MainCamera=match,Camera=camera,SceneHandle=sceneHandle,
            };
            return true;
        }
        catch(Exception e){reason="camera-fault:"+e.GetType().Name;return false;}
    }
}
