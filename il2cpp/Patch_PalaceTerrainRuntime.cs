// Own land12 ground palette handoff at native generation/restoration boundaries.
// No polling/scanning, no saved index/physics/Tile changes, no shared prefab changes.
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace KingdomEnhancedMod;
internal static class PalaceTerrainRuntime
{
    private const string PathKey = "Prefabs/Environment/Tile Big";
    private static bool IsGroundPath(string path) => path == PathKey
        || (PalaceRuntimeProfile.Capability("PALACE_ISLAND_OWNED_PRIVATE")
            && (path == "Prefabs/Environment/Tile Forest" || path == "Prefabs/Environment/Tile Clearing"
                || path == "Prefabs/Environment/Tile Forest Building"));
    private static readonly Dictionary<IntPtr, TileOwner> Tiles = new();
    private static readonly Dictionary<int, PalaceTerrainPalette> Palettes = new();
    private static readonly List<TileOwner> RetiredOwners = new();
    private static CampaignSaveData _campaign;
    private static Transform _layer;
    private static bool _fault, _installed;
    internal static bool Enabled => PalaceLand12Runtime.Enabled && PalaceRuntimeProfile.Capability("PALACE_TERRAIN_PRIVATE");
    internal static void InstallOnce(Harmony h)
    {
        if(!Enabled || _installed)return;
        _installed=true;
        h.Patch(AccessTools.Method(typeof(IslandSaveData),"TryCreateOrFind",new[]{typeof(IslandSaveData.ObjectData)}),
            postfix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(RestoreCreated)));
        h.Patch(AccessTools.Method(typeof(LevelBlock),"CloneObject",new[]{typeof(float),typeof(GameObject),typeof(Transform)}),
            postfix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(Generated)));
        h.Patch(AccessTools.Method(typeof(RandomizeSprite),"ApplyData",new[]{typeof(Il2CppSystem.Object)}),
            prefix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(ApplyBefore)),postfix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(ApplyAfter)));
        h.Patch(AccessTools.Method(typeof(BiomeSpriteSwapper),"OnEnable",Type.EmptyTypes),
            prefix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(BiomeEnable)));
        h.Patch(AccessTools.Method(typeof(BiomeSpriteSwapper),"SpecificSwap",new[]{typeof(BiomeSwapData)}),
            prefix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(BiomeSpecific)));
        h.Patch(AccessTools.Method(typeof(Game),"PreExitCampaign"),postfix:new HarmonyMethod(typeof(PalaceTerrainRuntime),nameof(Cleanup)));
        Log("installed; exact ground persistent paths only, native original ApplyData keeps saved indices");
    }
    private static bool Scope(out CampaignSaveData c, out Transform layer)
    {
        c=null;layer=null;
        if(!Enabled || NetworkBigBoss.IsOnline || !ExtensionIslandRuntime.TryScope(out c) || c.CurrentLand!=12)return false;
        var m=Managers.Inst;
        if(m==null || m.game==null || m.game.currentLand!=12 || m.world==null || m.world.gameLayer==null)return false;
        layer=m.world.gameLayer;return true;
    }
    private static bool Instance(GameObject obj, Transform layer)
        => obj!=null && layer!=null && obj.scene==layer.gameObject.scene && obj.transform.IsChildOf(layer);
    private static void RestoreCreated(IslandSaveData.ObjectData __0, Persistent __result)
    {
        try
        {
            if(__0==null || !IsGroundPath(__0.prefabPath) || __result==null)return;
            Bind(__result, "restored:"+__0.uniqueID);
        }
        catch(Exception e){Fail(e);}
    }
    private static void Generated(Transform __result)
    {
        try
        {
            if(__result==null)return;
            var p=__result.GetComponent<Persistent>();
            if(p!=null && IsGroundPath(p.path))Bind(p,"generated:"+p.Pointer);
        }
        catch(Exception e){Fail(e);}
    }
    private static void Bind(Persistent persistent,string identity)
    {
        if(!Scope(out var campaign,out var layer) || !Instance(persistent.gameObject,layer))return;
        if(_layer!=null && (_layer.Pointer!=layer.Pointer || _campaign==null || _campaign.Pointer!=campaign.Pointer))Cleanup();
        if(_fault)return;
        var obj=persistent.gameObject;
        if(!IsGroundPath(persistent.path))return;
        if(Tiles.ContainsKey(obj.Pointer))return;
        var random=obj.GetComponent<RandomizeSprite>();var renderer=obj.GetComponent<SpriteRenderer>();var tile=obj.GetComponent<Tile>();
        if(random==null || renderer==null || tile==null || random.sprites==null || renderer.sprite==null)
            throw new InvalidOperationException("ground required components/palette unavailable: "+identity+" path="+persistent.path);
        int count=random.sprites.Count;
        int pick=random.randomPick;
        if(pick<0 || pick>=count)throw new InvalidOperationException("native randomPick out of original palette: "+pick+" / "+count);
        PalaceTerrainPalette.ValidateNativeSprite(renderer.sprite); // every entity, including cache hits/new persistent paths
        if(!Palettes.TryGetValue(count,out var palette))
        {palette=PalaceTerrainPalette.CreateLike(renderer.sprite,count);Palettes.Add(count,palette);}
        var owner=new TileOwner {Identity=identity,Obj=obj,Persistent=persistent,Random=random,Renderer=renderer,
            OriginalList=random.sprites,OriginalSprite=renderer.sprite,OriginalIgnore=random.ignoreBiome,Palette=palette};
        _campaign=campaign;_layer=layer;Tiles.Add(obj.Pointer,owner);
        // Explicit boundary transfer before original saved-index ApplyData or generation Randomize.
        random.sprites=palette.Sprites;random.ignoreBiome=true;renderer.sprite=palette.Sprites[pick];
        Log("handoff "+identity+" path="+persistent.path+" go="+obj.name+" ptr="+obj.Pointer+" count="+count+" pick="+pick+" x="+obj.transform.position.x+" ownSprite="+palette.Sprite.Pointer+" bounds="+palette.Sprite.bounds);
    }
    private static bool TryOwner(GameObject obj,out TileOwner owner)
    {
        owner=null;
        if(!Scope(out var campaign,out var layer) || _campaign==null || _layer==null || campaign.Pointer!=_campaign.Pointer || layer.Pointer!=_layer.Pointer
            || !Instance(obj,layer) || !Tiles.TryGetValue(obj.Pointer,out owner))return false;
        return owner.Obj!=null && owner.Persistent!=null && owner.Random!=null && owner.Renderer!=null
            && obj.GetComponent<Persistent>()?.Pointer==owner.Persistent.Pointer
            && obj.GetComponent<RandomizeSprite>()?.Pointer==owner.Random.Pointer
            && owner.Random.sprites!=null && owner.Random.sprites.Pointer==owner.Palette.Sprites.Pointer;
    }
    // Creation-time read of the existing palette-owner table; no scene enumeration or state writes.
    // This is the environment's pixel grid, not a measurement of an adult character's BODY.
    internal static bool TryReadScrollPixelGrid(Transform layer,float axisWorldX,out double unitsPerPixel,out string evidence)
    {
        unitsPerPixel=0d;evidence=null;
        try
        {
            if(!Scope(out var campaign,out var currentLayer) || layer==null || currentLayer.Pointer!=layer.Pointer
                || _campaign==null || _campaign.Pointer!=campaign.Pointer || _layer==null || _layer.Pointer!=layer.Pointer || _fault)
            {evidence="current terrain owner epoch unavailable";return false;}
            if(!float.IsFinite(axisWorldX) || Tiles.Count==0 || Tiles.Count>4096)
            {evidence="terrain grid anchor/owner count invalid";return false;}
            int matched=0;double selected=0d;string source=null;
            foreach(var owner in Tiles.Values)
            {
                if(owner.Obj==null || owner.Renderer==null || !owner.Obj.activeInHierarchy || !owner.Renderer.enabled) continue;
                Bounds bounds=owner.Renderer.bounds;
                if(!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x) || bounds.max.x<=bounds.min.x)
                    throw new InvalidOperationException("terrain owner has invalid visual bounds");
                if(axisWorldX<bounds.min.x-.001f || axisWorldX>bounds.max.x+.001f) continue;
                if(!TryOwner(owner.Obj,out var active) || !ReferenceEquals(active,owner)
                    || owner.Renderer.sprite==null || owner.Renderer.sprite.Pointer!=owner.Palette.Sprite.Pointer)
                    throw new InvalidOperationException("axis terrain palette identity changed");
                Vector3 scale=owner.Renderer.transform.lossyScale;
                float angle=Quaternion.Angle(owner.Renderer.transform.rotation,Quaternion.identity);
                float ppu=owner.Renderer.sprite.pixelsPerUnit;
                if(!float.IsFinite(scale.x) || !float.IsFinite(scale.y) || scale.y<=0f
                    || Math.Abs(Math.Abs(scale.x)-scale.y)>.0001f || !float.IsFinite(angle) || angle>.0001f
                    || !float.IsFinite(ppu) || ppu<=0f)
                    throw new InvalidOperationException("axis terrain grid is not finite, upright and isotropic");
                double metric=(double)scale.y/ppu;
                if(!double.IsFinite(metric) || metric<=0d)
                    throw new InvalidOperationException("axis terrain pixel metric invalid");
                if(matched!=0 && Math.Abs(metric-selected)>Math.Max(metric,selected)*.000001d)
                    throw new InvalidOperationException("axis terrain owners disagree about pixel metric");
                if(matched==0){selected=metric;source=owner.Identity+" sprite="+owner.Renderer.sprite.Pointer+" ppu="+ppu+" scaleY="+scale.y;}
                matched++;
            }
            if(matched==0){evidence="no current registered ground sprite covers axis";return false;}
            unitsPerPixel=selected;evidence=source+" agreeingOwners="+matched;return true;
        }
        catch(Exception e){evidence="terrain pixel read rejected: "+e.Message;return false;}
    }
    private static void ApplyBefore(RandomizeSprite __instance, Il2CppSystem.Object __0, out int __state)
    {
        __state=-1;
        try
        {
            if(__instance==null || !TryOwner(__instance.gameObject,out var owner))return;
            var data=__0!=null?__0.TryCast<RandSpriteData>():null;
            if(data==null || data.selectedSpriteId<0 || data.selectedSpriteId>=owner.OriginalList.Count)
                throw new InvalidOperationException("saved tile index does not fit original palette: "+owner.Identity);
            __state=data.selectedSpriteId;
            Log("ApplyData.before "+owner.Identity+" savedIndex="+__state+" flip="+data.flip+" count="+__instance.sprites.Count+" ignoreBiome="+__instance.ignoreBiome);
        }
        catch(Exception e){Fail(e);}
    }
    private static void ApplyAfter(RandomizeSprite __instance, Il2CppSystem.Object __0,int __state)
    {
        if(__state<0)return;
        try
        {
            if(__instance==null || !TryOwner(__instance.gameObject,out var owner))throw new InvalidOperationException("palette owner lost during ApplyData");
            var data=__0.TryCast<RandSpriteData>();
            bool valid=data!=null && data.selectedSpriteId==__state && __instance.randomPick==__state && __instance.sprites.Count==owner.OriginalList.Count
                && owner.Renderer.sprite!=null && owner.Renderer.sprite.Pointer==owner.Palette.Sprites[__state].Pointer;
            Log("ApplyData.return "+owner.Identity+" savedIndex="+__state+" randomPick="+__instance.randomPick+" count="+__instance.sprites.Count+" nativeOutputOwn="+valid);
            if(!valid)throw new InvalidOperationException("original ApplyData did not emit owned palette; no corrective overwrite");
        }
        catch(Exception e){Fail(e);}
    }
    private static bool BiomeEnable(BiomeSpriteSwapper __instance)=>BiomeAllowed(__instance,"OnEnable");
    private static bool BiomeSpecific(BiomeSpriteSwapper __instance)=>BiomeAllowed(__instance,"SpecificSwap");
    private static bool BiomeAllowed(BiomeSpriteSwapper swapper,string method)
    {
        try
        {
            if(swapper==null || !TryOwner(swapper.gameObject,out var owner))return true;
            Log("owned palette authority excludes Greek biome "+method+" on "+owner.Identity);
            return false; // This instance's palette is owned by land12, not Greek biome rewriting.
        }
        catch(Exception e){Fail(e);return true;}
    }
    private static void Fail(Exception e)
    { _fault=true;Log("ERROR palette handoff stopped in this world: "+e.GetType().Name+" "+e.Message); }
    private static void Cleanup()
    {
        // Explicit owner list only, no scan or timer. Retain assets if a live reference cannot be detached.
        var owners=new List<TileOwner>(RetiredOwners);owners.AddRange(Tiles.Values);RetiredOwners.Clear();
        var allPalettes=new HashSet<PalaceTerrainPalette>(Palettes.Values);
        var retained=new HashSet<PalaceTerrainPalette>();
        foreach(var owner in owners)
        {
            allPalettes.Add(owner.Palette);
            try
            {
                if(owner.Obj!=null && owner.Random!=null && owner.Random.sprites!=null && owner.Random.sprites.Pointer==owner.Palette.Sprites.Pointer)
                {owner.Random.sprites=owner.OriginalList;owner.Random.ignoreBiome=owner.OriginalIgnore;}
                if(owner.Renderer!=null && owner.Renderer.sprite!=null && owner.Renderer.sprite.Pointer==owner.Palette.Sprite.Pointer)
                    owner.Renderer.sprite=owner.OriginalSprite;
                if((owner.Obj!=null && owner.Random!=null && owner.Random.sprites!=null && owner.Random.sprites.Pointer==owner.Palette.Sprites.Pointer)
                    || (owner.Renderer!=null && owner.Renderer.sprite!=null && owner.Renderer.sprite.Pointer==owner.Palette.Sprite.Pointer))
                    throw new InvalidOperationException("owned palette remains referenced after detach");
            }
            catch(Exception e)
            {retained.Add(owner.Palette);RetiredOwners.Add(owner);Log("cleanup retains live/unknown references "+owner.Identity+": "+e.GetType().Name);}
        }
        Tiles.Clear();Palettes.Clear();
        foreach(var palette in allPalettes)if(!retained.Contains(palette))palette.DestroyAll();
        _layer=null;_campaign=null;_fault=false;
    }
    private static void Log(string text)=>PalaceCourtVisuals.Log("[TerrainPalette] "+text);
    private sealed class TileOwner
    {
        internal string Identity;
        internal GameObject Obj;internal Persistent Persistent;internal RandomizeSprite Random;internal SpriteRenderer Renderer;
        internal Il2CppSystem.Collections.Generic.List<Sprite> OriginalList;internal Sprite OriginalSprite;internal bool OriginalIgnore;
        internal PalaceTerrainPalette Palette;
    }
}
