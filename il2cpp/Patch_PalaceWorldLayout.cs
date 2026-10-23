using System;
using System.Collections.Generic;
using UnityEngine;
namespace KingdomEnhancedMod;

// One current-world authoring plan. Native structures and parallax remain their owners' responsibility.
internal static class PalaceWorldLayout
{
    internal static bool Enabled => PalaceRuntimeProfile.Capability("PALACE_WORLD_FULL_PRIVATE") && PalaceLand12Runtime.Enabled;
    internal static PalaceWorldPlan Create(CourtBinding binding)
    {
        var m = Managers.Inst;
        if (m == null || m.level == null || m.world == null || m.payables == null || binding?.Parent == null)
            throw new InvalidOperationException("world art owner unavailable");
        Transform layer = m.world.gameLayer;
        if (layer == null || binding.Parent.parent == null || binding.Parent.parent.Pointer != layer.Pointer)
            throw new InvalidOperationException("world art game layer changed");
        RequireDedicatedNativeContent(layer);
        float center = binding.Parent.position.x + PalaceCourtAssets.HalfCourt;
        float ground = binding.Parent.position.y;
        var plan = new PalaceWorldPlan { Placements = new List<PalaceWorldPlacement>(), Guards = new List<PalaceWorldGuardRoute>() };
        var occupied = Capture(m, layer);
        // Retained owned gate and R6 buildings also reserve their current full image footprints.
        occupied.Add(new HeroShopPlacement.Interval(center - 3f, center + 3f));
        occupied.Add(new HeroShopPlacement.Interval(center - 14.765625f, center - 10.084375f));
        occupied.Add(new HeroShopPlacement.Interval(center + 9.8875f, center + 12.95f));
        BoxCollider2D collider = m.level.GroundCollider;
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger
            || collider.gameObject.scene.handle != layer.gameObject.scene.handle)
            throw new InvalidOperationException("world art ground unavailable");
        Bounds bounds = collider.bounds;
        float left = bounds.min.x, right = bounds.max.x;
        if (PalaceCourtArea.Enabled)
        {
            if (!PalaceCourtArea.TryGetSpan(m.level, out left, out right, out string spanReason))
                throw new InvalidOperationException("owned native courtyard span unavailable: " + spanReason);
            if (left < bounds.min.x || right > bounds.max.x)
                throw new InvalidOperationException("owned native courtyard span outside ground");
        }
        if (!float.IsFinite(left) || !float.IsFinite(right) || right <= left)
            throw new InvalidOperationException("world art ground bounds invalid");
        SpriteRenderer actorRef = binding.Reference;
        if (actorRef == null || actorRef.sprite == null) throw new InvalidOperationException("borrowed actor reference disappeared");
        float stageX = Place(occupied, left, right, 2.25f, 2.25f, PalaceCourtArea.Enabled ? center - 8f : center - 32f, "stage");
        Surface(collider, stageX, ground);
        plan.Stage = new PalaceWorldStage { Parent = binding.Parent, Reference = actorRef,
            Position = new Vector3(stageX, ground, binding.Parent.position.z),
            DeckOrderOffset = -3, DancerOrderOffset = -2, FrontOrderOffset = -1 };
        AddBuilding("original-tang-tower-exact", 1.5f, 1.5f, PalaceCourtArea.Enabled ? center + 8f : center - 37f);
        AddBuilding("tang-side-pavilion-exact", .75f, .875f, PalaceCourtArea.Enabled ? center + 15f : center - 21.5f);
        AddBuilding("tang-garden-pavilion", 35f/32f, 37f/32f, PalaceCourtArea.Enabled ? center + 19f : center - 19f);
        void AddBuilding(string id, float l, float r, float preferred)
        {
            float x = Place(occupied, left, right, l, r, preferred, id);
            Surface(collider, x, ground);
            plan.Placements.Add(new PalaceWorldPlacement { AssetId = id, Parent = binding.Parent,
                Reference = actorRef, Position = new Vector3(x, ground, binding.Parent.position.z), OrderOffset = -2 });
        }
        // Art authoring positions are native-parent local positions, independent of the current camera correction.
        SpriteRenderer groundStyle = GroundReference(layer);
        LayerBinding far = Bind(m.level, ContentLayers.BackgroundFarLayer, layer, groundStyle);
        LayerBinding mid = Bind(m.level, ContentLayers.BackgroundNearLayer, layer, far.Reference);
        LayerBinding near = Bind(m.level, ContentLayers.ForegroundFarLayer, layer, far.Reference);
        if (PalaceRuntimeProfile.Capability("PALACE_ISLAND_OWNED_PRIVATE"))
        {
            // All panels share a 512px/32ppu=16-unit grid. The previous near panels were
            // 24 units apart, leaving an eight-unit break on the RIGHT of the door.
            // Cover the island support ground as well as the central court; far scenery
            // has one extra panel at each end for native parallax motion.
            int panels = checked((int)Math.Ceiling((bounds.max.x - bounds.min.x) / 16f));
            if (panels < 1 || panels > 16) throw new InvalidOperationException("dedicated island layer span over budget");
            for (int i = -1; i <= panels; i++)
                AddLayer((i & 1) == 0 ? "skyline-a" : "skyline-b", far,
                    bounds.min.x + 8f + i * 16f, ground + .5f);
            for (int i = 0; i < panels; i++)
            {
                float x = bounds.min.x + 8f + i * 16f;
                AddLayer("mid-wall-corridor-trees", mid, x, ground);
                AddLayer("near-rail-lantern-foliage", near, x, ground - .5f);
            }
            // A corridor is a building joined to the palace, not distant scenery:
            // its native parent must move with the gate/player, with no parallax drift.
            var door = PalaceCourtVisuals.ReturnDoor;
            SpriteRenderer doorRenderer = door != null ? door.GetComponent<SpriteRenderer>() : null;
            if (doorRenderer == null || doorRenderer.sprite == null)
                throw new InvalidOperationException("dedicated corridor gate footprint unavailable");
            Bounds doorBounds = doorRenderer.bounds;
            for (float x = doorBounds.min.x - 1.5f; x - 1.5f >= left - .5f; x -= 3f)
                AddJoinedCorridor(x);
            for (float x = doorBounds.max.x + 1.5f; x + 1.5f <= right + .5f; x += 3f)
                AddJoinedCorridor(x);
            void AddJoinedCorridor(float x)
            {
                plan.Placements.Add(new PalaceWorldPlacement { AssetId = "tang-covered-walk", Parent = binding.Parent,
                    Reference = actorRef, Position = new Vector3(x,ground,binding.Parent.position.z), OrderOffset = -4 });
            }
            plan.Placements.Add(new PalaceWorldPlacement { AssetId = "tang-covered-walk-end", Parent = binding.Parent,
                Reference = actorRef, Position = new Vector3(right,ground,binding.Parent.position.z), OrderOffset = -4 });
            PalaceCourtVisuals.Log("dedicated landscape grid: ground=" + bounds.min.x + ".." + bounds.max.x
                + " panels=" + panels + " pitch=16 foregroundYOffset=-0.5 skylineYOffset=0.5; corridor joined to gate game parent");
        }
        else
        {
            AddLayer("skyline-a", far, center - 8f, ground + 2f);
            AddLayer("skyline-b", far, center + 8f, ground + 2f);
            AddLayer("mid-wall-corridor-trees", mid, center - 8f, ground);
            AddLayer("mid-wall-corridor-trees", mid, center + 8f, ground);
            AddLayer("near-rail-lantern-foliage", near, center - 8f, ground);
            AddLayer("near-rail-lantern-foliage", near, center + 16f, ground);
            foreach (float x in new[] { center - 12f, center - 9f, center - 6f, center + 5f, center + 8f, center + 11f })
                AddLayer("tang-covered-walk", mid, x, ground);
            AddLayer("tang-covered-walk-end", mid, center + 13f, ground);
        }
        void AddLayer(string id, LayerBinding b, float x, float y)
        {
            // Convert the ground world anchor to the common Level authoring space, then instantiate it
            // in this direct native Level child. This inherits, rather than counteracts, native parallax.
            Vector3 authored = m.level.transform.InverseTransformPoint(new Vector3(x,y,m.level.transform.position.z));
            plan.Placements.Add(new PalaceWorldPlacement { AssetId = id, Parent = b.Parent,
                Reference = b.Reference, Position = b.Parent.TransformPoint(new Vector3(authored.x, authored.y, -.01f)),
                // These newly authored art strata must stay behind our architecture (-2), stage (-3..-1), and actors (0).
                // Copying the native background's order0 covered them in the actual cold-r2 frame.
                OrderOffset = id == "near-rail-lantern-foliage" ? 1 : id.StartsWith("skyline",StringComparison.Ordinal) ? -6
                    : id.StartsWith("tang-covered-walk",StringComparison.Ordinal) ? -4 : -5 });
        }
        // Presentation routes contain no native entity, collision, save or combat responsibility.
        plan.Guards.Add(new PalaceWorldGuardRoute { ActorId = "patrol-guard-lance", Parent = binding.Parent,
            Reference = actorRef, Left = new Vector3(center - 4.8f, ground, binding.Parent.position.z),
            Right = new Vector3(center - 2.9f, ground, binding.Parent.position.z), WalkLoops = 2, OrderOffset = 0 });
        plan.Guards.Add(new PalaceWorldGuardRoute { ActorId = "patrol-guard-shield", Parent = binding.Parent,
            Reference = actorRef, Left = new Vector3(center + 2.9f, ground, binding.Parent.position.z),
            Right = new Vector3(center + 4.8f, ground, binding.Parent.position.z), WalkLoops = 2, OrderOffset = 0 });
        PalaceCourtVisuals.Log("world plan once: stage=" + stageX.ToString("F3") + " native/owned exclusion spans=" + occupied.Count
            + " placements=" + plan.Placements.Count + "; not a future-upgrade reservation or native NPC AI");
        return plan;
    }

    private static float Place(List<HeroShopPlacement.Interval> spans, float left, float right,
        float leftExtent, float rightExtent, float preferred, string id)
    {
        float half = (leftExtent + rightExtent) * .5f + .25f;
        float bias = (rightExtent - leftExtent) * .5f;
        var expanded = new List<HeroShopPlacement.Interval>(spans);
        bool Fits(float middle)
        {
            double low = middle - half, high = middle + half;
            foreach (var span in spans) if (low <= span.High && high >= span.Low) return false;
            return true;
        }
        float candidate = preferred + bias;
        if (!(candidate > left + half && candidate < right - half && Fits(candidate)))
        {
            if (!HeroShopPlacement.Find(left, right, half, Fits, () => expanded, out candidate))
                throw new InvalidOperationException("no verified current-world gap for " + id);
        }
        float pivot = candidate - bias;
        spans.Add(new HeroShopPlacement.Interval(pivot - leftExtent, pivot + rightExtent));
        PalaceCourtVisuals.Log("world allocated " + id + " pivot=" + pivot.ToString("F3") + " footprint="
            + (pivot-leftExtent).ToString("F3") + ".." + (pivot+rightExtent).ToString("F3"));
        return pivot;
    }

    private static void Surface(BoxCollider2D ground, float x, float y)
    {
        Vector2 surface = ground.ClosestPoint(new Vector2(x, ground.bounds.max.y + 2f));
        if (!float.IsFinite(surface.x) || !float.IsFinite(surface.y) || Math.Abs(surface.x-x)>.01f || Math.Abs(surface.y-y)>.02f)
            throw new InvalidOperationException("art footprint has a different ground surface");
    }

    private static List<HeroShopPlacement.Interval> Capture(Managers m, Transform layer)
    {
        var spans = new List<HeroShopPlacement.Interval>();
        var roots = new HashSet<IntPtr>();
        var all = m.payables.AllPayables; var blockers = m.payables._allBlockers;
        if (all == null || blockers == null || all.Length > 4096 || blockers.Count > 4096)
            throw new InvalidOperationException("art exclusions unavailable or over budget");
        void Add(float lo, float hi)
        {
            if (!float.IsFinite(lo) || !float.IsFinite(hi) || hi < lo) throw new InvalidOperationException("invalid art occupancy");
            if (hi > lo) spans.Add(new HeroShopPlacement.Interval(lo,hi));
        }
        for (int i=0;i<all.Length;i++)
        {
            Payable p=all[i]; if(p==null || !InLayer(p.gameObject,layer) || DynamicActor(p.gameObject))continue;
            float d=p.payablePlacementExclusionDistance;
            if(!float.IsFinite(d) || d<0)throw new InvalidOperationException("invalid payable exclusion");
            if(d>0){float x=p.PayableExclusionPoint();Add(x-d,x+d);}
            Visual(p.gameObject);
        }
        for(int i=0;i<blockers.Count;i++)
        {
            PayableBlocker b=blockers[i];if(b==null || !InLayer(b.gameObject,layer) || DynamicActor(b.gameObject))continue;
            float x=b.GetExclusionPoint(),d=b.exclusionDistance;Add(x-d,x+d);Visual(b.gameObject);
        }
        foreach(string tag in new[]{"Tower","Wall","WallWreck"})
        {
            var tagged=GameObject.FindGameObjectsWithTag(tag);
            if(tagged.Length>4096)throw new InvalidOperationException("tag occupancy over budget");
            foreach(GameObject go in tagged)if(InLayer(go,layer))Visual(go);
        }
        foreach(WorkableBuilding building in layer.GetComponentsInChildren<WorkableBuilding>(true))
            if(building!=null && InLayer(building.gameObject,layer))Visual(building.gameObject);
        foreach(Scaffolding scaffold in layer.GetComponentsInChildren<Scaffolding>(true))
            if(scaffold!=null && InLayer(scaffold.gameObject,layer))
            {
                Visual(scaffold.gameObject);
                if(scaffold.Building!=null && scaffold.Building.transform!=null
                    && scaffold.Building.gameObject.scene.handle==layer.gameObject.scene.handle
                    && scaffold.Building.transform.IsChildOf(layer)) Visual(scaffold.Building.gameObject);
            }
        foreach(WorkableTree tree in layer.GetComponentsInChildren<WorkableTree>(true))
            if(tree!=null && InLayer(tree.gameObject,layer))Visual(tree.gameObject);
        void Visual(GameObject go)
        {
            if(!roots.Add(go.Pointer))return;
            if(roots.Count>2048)throw new InvalidOperationException("structural occupancy over budget");
            Rect native=m.level.GetOverlapRegion(go);
            if(float.IsFinite(native.xMin)&&float.IsFinite(native.xMax)&&native.xMax>native.xMin)Add(native.xMin,native.xMax);
            if(roots.Count<=40)PalaceCourtVisuals.Log("world occupancy root="+go.name+" x="+go.transform.position.x
                +" native="+native.xMin+".."+native.xMax);
            var backgrounds=go.GetComponentsInChildren<BackgroundWall>(true);
            var renderers=go.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length>256)throw new InvalidOperationException("structural renderer budget exceeded");
            foreach(Renderer renderer in renderers)
            {
                if(renderer==null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
                bool background=false;
                foreach(BackgroundWall b in backgrounds)
                    if(b!=null && (renderer.transform.Pointer==b.transform.Pointer || renderer.transform.IsChildOf(b.transform))) {background=true;break;}
                if(background)continue;
                Bounds v=renderer.bounds;Add(v.min.x,v.max.x);
                if(roots.Count<=40)PalaceCourtVisuals.Log("world occupancy visual="+renderer.gameObject.name+" "+v.min.x+".."+v.max.x);
            }
        }
        PalaceCourtVisuals.Log("world current structural snapshot roots="+roots.Count+" spans="+spans.Count+"; read-only creation input");
        return spans;
    }
    private static bool DynamicActor(GameObject go)
    {
        if (go.GetComponentInParent<Player>() != null || go.GetComponentInParent<Steed>() != null) return true;
        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        return body != null && body.bodyType == RigidbodyType2D.Dynamic;
    }
    private static bool InLayer(GameObject go,Transform layer) => go!=null && go.activeInHierarchy && go.scene.handle==layer.gameObject.scene.handle
        && go.transform!=null && go.transform.IsChildOf(layer);
    private static SpriteRenderer GroundReference(Transform layer)
    {
        var tiles = layer.GetComponentsInChildren<Tile>(true);
        if (tiles.Length > 4096) throw new InvalidOperationException("ground reference budget exceeded");
        foreach (Tile tile in tiles)
        {
            if (tile == null || !InLayer(tile.gameObject,layer)) continue;
            Persistent persistent = tile.GetComponent<Persistent>();
            if (persistent == null || !PalaceIslandContentPolicy.IsGroundTilePath(persistent.path)) continue;
            SpriteRenderer renderer = tile.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null && renderer.sharedMaterial != null
                && renderer.sharedMaterial.name.StartsWith("Pow-Diffuse",StringComparison.Ordinal)) return renderer;
        }
        throw new InvalidOperationException("current-world opaque ground style unavailable");
    }
    private readonly struct LayerBinding
    {
        internal readonly Transform Parent; internal readonly SpriteRenderer Reference;
        internal LayerBinding(Transform parent,SpriteRenderer reference){Parent=parent;Reference=reference;}
    }
    private static bool SameParallax(Transform child,Transform parent,Parallax2D expected)
    {
        for(Transform t=child;t!=null;t=t.parent)
        {
            if(t.GetComponent<Parallax2DParentRelative>()!=null)return false;
            Parallax2D p=t.GetComponent<Parallax2D>();
            if(p!=null)return p.Pointer==expected.Pointer && t.Pointer==parent.Pointer;
            if(t.Pointer==parent.Pointer)break;
        }
        return false;
    }
    private static LayerBinding Bind(Level level,ContentLayers id,Transform layer,SpriteRenderer explicitOpaqueReference = null)
    {
        GameObject go=level.GetLayer(id);
        if(go==null || !go.activeInHierarchy || go.scene.handle!=layer.gameObject.scene.handle
            || go.transform.parent==null || go.transform.parent.Pointer!=level.transform.Pointer)
            throw new InvalidOperationException("native art parent unavailable: "+id);
        Vector3 scale=go.transform.lossyScale;Quaternion q=go.transform.rotation;
        if((scale-Vector3.one).sqrMagnitude>.000001f || Math.Abs(q.x)+Math.Abs(q.y)+Math.Abs(q.z)>.0001f || Math.Abs(Math.Abs(q.w)-1f)>.0001f)
            throw new InvalidOperationException("native art parent transform differs: "+id);
        Parallax2D parallax=go.GetComponent<Parallax2D>();
        var registered=level.GetParallaxingLayers();bool found=false;
        if(parallax!=null && registered!=null)foreach(Parallax2D p in registered)if(p!=null && p.Pointer==parallax.Pointer){found=true;break;}
        if(!found)throw new InvalidOperationException("native art parent not registered: "+id);
        SpriteRenderer reference=null;
        var renderers=go.GetComponentsInChildren<SpriteRenderer>(true);
        if(renderers.Length>4096)throw new InvalidOperationException("native background renderer budget exceeded");
        foreach(SpriteRenderer r in renderers)
            if(r!=null && r.enabled && r.gameObject.activeInHierarchy && r.sprite!=null && SameParallax(r.transform,go.transform,parallax)
                && r.sharedMaterial!=null && (r.sharedMaterial.name.StartsWith("Pow-Diffuse",StringComparison.Ordinal)
                    || r.sharedMaterial.name.StartsWith("Pow-Recolor",StringComparison.Ordinal)))
            {reference=r;break;}
        PalaceCourtVisuals.Log("native world art layer="+id+" parent="+go.name+" ratio="+parallax.ratio+" z="+go.transform.position.z
            +" renderers="+renderers.Length+" reference="+(reference!=null?reference.gameObject.name:"none"));
        if(reference==null)
        {
            if (explicitOpaqueReference == null) throw new InvalidOperationException("native art layer has no opaque render reference: "+id);
            reference = explicitOpaqueReference;
            PalaceCourtVisuals.Log("native parent " + id + " has no matching opaque sprite reference; new art deliberately uses current-world diffuse background style "
                + reference.sharedMaterial.name + "; native parent/parallax stays unchanged");
        }
        PalaceCourtVisuals.Log("native world art reference "+id+" sort="+reference.sortingLayerID+"/"+reference.sortingOrder
            +" material="+(reference.sharedMaterial!=null?reference.sharedMaterial.name:"default"));
        return new LayerBinding(go.transform,reference);
    }
    // The existing one-time creation assertion is shared by mutually exclusive art providers.
    internal static void RequireDedicatedNativeContent(Transform layer)
    {
        if (!PalaceIslandContentPolicy.Enabled) return;
        var m = Managers.Inst;
        if (m?.level == null || m.world?.gameLayer == null || layer == null || m.world.gameLayer.Pointer != layer.Pointer)
            throw new InvalidOperationException("dedicated content audit owner unavailable");
        var nativeContent = m.level.GetComponentsInChildren<Persistent>(true);
        if (nativeContent.Length > 8192) throw new InvalidOperationException("dedicated native content audit over budget");
        int forbidden = 0;
        foreach (Persistent p in nativeContent)
            if (p != null && p.gameObject.activeInHierarchy && p.gameObject.scene.handle == layer.gameObject.scene.handle
                && PalaceIslandContentPolicy.IsForbiddenPath(p.path)) forbidden++;
        PalaceCourtVisuals.Log("dedicated first-Playing native content audit: persistent=" + nativeContent.Length + " forbidden=" + forbidden);
        if (forbidden != 0) throw new InvalidOperationException("dedicated island still contains forbidden native content: " + forbidden);
    }

}
