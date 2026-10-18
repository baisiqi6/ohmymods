using System;
using Coatsink.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomEnhancedMod;

// R6 夜空排序契约样本（source-only 只读诊断；单文件、无 hook、无 MonoBehaviour、无 driver、无周期扫描）。
// 唯一入口 <see cref="PalaceSkySortSample.Sample"/>：Root 在已验证私有 R5 完整 scope（自有 root + 24 层 +
// Activate 成功）之后按 root 调用一次；helper 自身无状态、不扫描/不等待/不重选/不重试、不 Nativealloc、不写任何状态。
// 一次窗口 = PALACE_SKY_SORT_OBSERVE_PRIVATE==1 且 PalaceLand12Runtime.Enabled；门关闭零日志零效果。
// 身份（只读，任一缺失/漂移 → 记 unknown 并 return，无 fallback）：Coatsink.Common.SingletonMonoBehaviour<Managers>._Inst
// 直接读静态字段（不用行为未解码的 Native Inst getter）→ m.game 指针==game、离线且 HasWorldAuth、
// campaign 与 game 均为 land12 且互相一致、kingdom/level/world.gameLayer 与 root 同 scene、typed playerOne 存活、
// 当次 playerOne 在 CameraMarshaller.cameraRefs(≤4)×cameraTargets(≤4) 中唯一命中传入的 MainCamera 且 camera 绑定一致。
// 天空：m.director.lunarCelestialBodies（≤8，超限记 unknown 关闭）逐项读 type/_director/cachedCameraId；
// eligible = CelestialBodyType.Moon(0) 且 _director.Pointer==m.director.Pointer；多 eligible 记 ambiguous 并输出各项实际值，
// 绝不取第一项、不用于选择或修复；sun 不做 fallback。天空对象 scene 允许 DontDestroyOnLoad：记录而不拒绝。
// 渲染契约：moon 的 primary _renderer 与仅其注册的 _glowRenderers（≤8，超限该项记 unknown）逐 renderer 读实际
// pointer/enabled/activeInHierarchy/goLayer/scene/worldpos/bounds/sortingLayerID/layer rank/
// sortingOrder/sharedMaterial(pointer/name/shader/renderQueue)；不读 moonMaterial/bloodMoonMaterial 代替实际材质；
// owned far/street/unit 同字段（unit 为借用参照，只读不销毁）。camera 只校验不重选，记录 cameraId/pointers/scene/
// cullingMask/depth/transparencySortMode/transparencySortAxis，cachedCameraId 只作为数据对照。
// sortingLayer 名称不读：仓库既有证据（docs/project-harness/tasks/combat-visuals-20260911/render-api.md）记录
// sortingLayerName / 层名转换是缺实现的 span shim；rank 用 int→int 的 SortingLayer.GetLayerValueFromID。
// SortingGroup：读取当前已编译核实的 typed self/parent链（≤8）；超限记 unknown。日志 ≤32 行、prefix [PalaceSkySortSample]、只含只读数值与已知字符串；
// 异常只在本次调用边界捕获一次并记 unknown，其余数据异常为只读失败，不影响原画面/游戏/钱/存档。
internal static class PalaceSkySortSample
{
    private const string Tag = "[PalaceSkySortSample] ";
    private const int MaxLines = 32;
    private const int MaxListEntries = 8;
    private const int MaxGlowRenderers = 8;
    private const int MaxCameraRefs = 4;
    private const int MaxCameraTargets = 4;

    // CelestialBody.CelestialBodyType：Moon 声明在 Sun 之前 → Moon==0、Sun==1（receipt r6-night-sky-sort-readonly-r1）。
    // 以 int 比较，避免依赖嵌套 enum 在当前 interop wrapper 的暴露形态；日志同时输出原始 type 值。
    private const int MoonTypeValue = 0;

    internal static void Sample(Game game, GameObject root, SpriteRenderer far, SpriteRenderer street,
        SpriteRenderer unit, MainCamera mainCamera, Camera camera)
    {
        if (!PalaceRuntimeProfile.Capability("PALACE_SKY_SORT_OBSERVE_PRIVATE") || !PalaceLand12Runtime.Enabled)
            return;
        int lines = 0;
        bool truncated = false;
        try
        {
            SampleCore(game, root, far, street, unit, mainCamera, camera, ref lines, ref truncated);
        }
        catch (Exception e)
        {
            Log(ref lines, ref truncated, "unknown: sample fault (" + e.GetType().Name + "): " + e.Message
                + "; read-only call boundary, no state changed");
        }
    }

    private static void SampleCore(Game game, GameObject root, SpriteRenderer far, SpriteRenderer street,
        SpriteRenderer unit, MainCamera mainCamera, Camera camera, ref int lines, ref bool truncated)
    {
        Log(ref lines, ref truncated,
            "source-only read-only sample (diagnostic, not player acceptance); gate=PALACE_SKY_SORT_OBSERVE_PRIVATE=1+land12");

        Managers managers = SingletonMonoBehaviour<Managers>._Inst;
        if (managers == null)
        { Log(ref lines, ref truncated, "unknown: Managers._Inst unavailable; sample closed"); return; }
        if (game == null || managers.game == null || managers.game.Pointer != game.Pointer)
        {
            Log(ref lines, ref truncated, "unknown: Managers.game is not the caller game (game="
                + (game == null ? "null" : game.Pointer.ToInt64().ToString()) + "); sample closed");
            return;
        }
        if (managers.kingdom == null || managers.level == null || managers.world == null || managers.world.gameLayer == null)
        { Log(ref lines, ref truncated, "unknown: kingdom/level/world/gameLayer unavailable; sample closed"); return; }
        if (root == null || !(bool)root || !root.activeInHierarchy)
        { Log(ref lines, ref truncated, "unknown: owned root unavailable; sample closed"); return; }

        Kingdom kingdom = managers.kingdom;
        Level level = managers.level;
        Transform gameLayer = managers.world.gameLayer;
        int rootScene = root.scene.handle;
        if (kingdom.gameObject == null || level.gameObject == null || gameLayer.gameObject == null
            || kingdom.gameObject.scene.handle != rootScene || level.gameObject.scene.handle != rootScene
            || gameLayer.gameObject.scene.handle != rootScene)
        {
            Log(ref lines, ref truncated, "unknown: owner scene differs from owned root (rootScene=" + rootScene
                + " kingdomScene=" + SceneOf(kingdom.gameObject) + " levelScene=" + SceneOf(level.gameObject)
                + " gameLayerScene=" + SceneOf(gameLayer.gameObject) + "); sample closed");
            return;
        }
        if (!root.transform.IsChildOf(gameLayer) || far == null || !(bool)far || street == null || !(bool)street
            || unit == null || !(bool)unit || !far.transform.IsChildOf(root.transform)
            || !street.transform.IsChildOf(root.transform) || unit.gameObject.scene.handle != rootScene)
        { Log(ref lines, ref truncated, "unknown: owned renderer/root hierarchy or borrowed unit scene differs; sample closed"); return; }
        if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth)
        {
            Log(ref lines, ref truncated, "unknown: offline worldAuth required (online=" + NetworkBigBoss.IsOnline
                + " worldAuth=" + NetworkBigBoss.HasWorldAuth + "); sample closed");
            return;
        }

        CampaignSaveData campaign = CampaignSaveData.current;
        int campaignLand = campaign != null ? campaign.CurrentLand : int.MinValue;
        int gameLand = game.currentLand;
        if (campaign == null || campaignLand != PalaceLand12Runtime.LandIndex
            || gameLand != PalaceLand12Runtime.LandIndex || campaignLand != gameLand)
        {
            Log(ref lines, ref truncated, "unknown: land12 identity differs (campaignLand=" + campaignLand
                + " gameLand=" + gameLand + " expected=" + PalaceLand12Runtime.LandIndex
                + " campaign=" + (campaign == null ? "null" : campaign.Pointer.ToInt64().ToString()) + "); sample closed");
            return;
        }
        Player playerOne = kingdom.playerOne;
        if (playerOne == null || !(bool)playerOne || playerOne.gameObject == null)
        { Log(ref lines, ref truncated, "unknown: typed playerOne unavailable; sample closed"); return; }

        // Verify the actual typed donor identity; a riding renderer need not be under the Player transform.
        Steed mount = playerOne.steed;
        Player rider = mount != null ? mount.Rider : null;
        SpriteRendererFX fx = mount != null ? mount.SpriteFX : null;
        SpriteRenderer mountRenderer = fx != null ? fx.Renderer : null;
        bool donorMatches = rider != null && rider.Pointer == playerOne.Pointer && mountRenderer != null
            && mountRenderer.Pointer == unit.Pointer;
        if (!donorMatches)
        {
            SpriteRenderer ownSteed = playerOne._steedRenderer;
            SpriteRenderer ownRider = playerOne._riderRenderer;
            donorMatches = (ownSteed != null && ownSteed.Pointer == unit.Pointer)
                || (ownRider != null && ownRider.Pointer == unit.Pointer);
        }
        if (!donorMatches)
        { Log(ref lines, ref truncated, "unknown: borrowed unit is not the typed current P1 renderer or ridden mount; sample closed"); return; }
        if (!TryValidateCamera(playerOne, mainCamera, camera, rootScene, out string cameraDetail))
        { Log(ref lines, ref truncated, "unknown: camera identity rejected: " + cameraDetail + "; sample closed"); return; }

        Log(ref lines, ref truncated, "identity ok: game=" + game.Pointer.ToInt64() + " managers._Inst=" + managers.Pointer.ToInt64()
            + " kingdom=" + kingdom.Pointer.ToInt64() + " level=" + level.Pointer.ToInt64()
            + " gameLayer=" + gameLayer.Pointer.ToInt64() + " root=" + root.Pointer.ToInt64() + " rootScene=" + rootScene
            + " campaign=" + campaign.Pointer.ToInt64() + " campaignLand=" + campaignLand + " gameLand=" + gameLand
            + " online=" + NetworkBigBoss.IsOnline + " worldAuth=" + NetworkBigBoss.HasWorldAuth
            + " p1=" + playerOne.Pointer.ToInt64() + " far=" + Ptr(far) + " street=" + Ptr(street) + " unit=" + Ptr(unit));
        Log(ref lines, ref truncated, "camera ok: " + cameraDetail);

        Director director = managers.director;
        if (director == null)
        { Log(ref lines, ref truncated, "unknown: Managers.director unavailable; sample closed"); return; }
        var bodies = director.lunarCelestialBodies;
        if (bodies == null)
        { Log(ref lines, ref truncated, "unknown: director.lunarCelestialBodies unavailable; sample closed"); return; }
        int count = bodies.Count;
        if (count < 1)
        { Log(ref lines, ref truncated, "unknown: lunarCelestialBodies empty; wait-for-night is not part of this sample"); return; }
        if (count > MaxListEntries)
        {
            Log(ref lines, ref truncated, "unknown: lunarCelestialBodies count=" + count + " over diagnostic cap "
                + MaxListEntries + "; sample closed without partial selection");
            return;
        }
        Log(ref lines, ref truncated, "sky: director=" + director.Pointer.ToInt64() + " lunarCount=" + count
            + " eligibleRule=type==" + MoonTypeValue + "(Moon) && _director==director; no sun fallback");

        int eligibleCount = 0;
        for (int i = 0; i < count; i++)
        {
            CelestialBody body = bodies[i];
            if (body == null)
            {
                if (count > 1) Log(ref lines, ref truncated, "entry[" + i + "] ptr=null");
                continue;
            }
            int typeValue = (int)body.celestialBodyType;
            Director bodyOwner = body._director;
            bool ownerMatch = bodyOwner != null && bodyOwner.Pointer == director.Pointer;
            bool eligible = typeValue == MoonTypeValue && ownerMatch;
            if (count > 1 || !eligible)
                Log(ref lines, ref truncated, "entry[" + i + "] ptr=" + body.Pointer.ToInt64() + " type=" + typeValue
                    + " ownerPtr=" + (bodyOwner == null ? "none" : bodyOwner.Pointer.ToInt64().ToString())
                    + " ownerMatch=" + ownerMatch + " cachedCameraId=" + body.cachedCameraId);
            if (!eligible) continue;
            eligibleCount++;
        }

        if (eligibleCount == 0)
        {
            Log(ref lines, ref truncated, "unknown: no eligible moon (need type==" + MoonTypeValue
                + " and _director==director); entries listed above are data only");
            return;
        }
        if (eligibleCount > 1)
        {
            Log(ref lines, ref truncated, "ambiguous: eligibleMoons=" + eligibleCount
                + "; first item not selected; entry values above are data only, not a repair/selection input");
        }
        // Budget priority: owned comparisons and every registered primary precede optional glow details.
        Log(ref lines, ref truncated, "owned[far-palace] " + DescribeRenderer(far));
        Log(ref lines, ref truncated, "owned[street-static] " + DescribeRenderer(street));
        Log(ref lines, ref truncated, "owned[unit-borrowed] go=" + GoName(unit) + " " + DescribeRenderer(unit));
        for (int moonIndex = 0; moonIndex < count; moonIndex++)
        {
            CelestialBody moon = bodies[moonIndex];
            if (moon == null || (int)moon.celestialBodyType != MoonTypeValue || moon._director == null
                || moon._director.Pointer != director.Pointer) continue;
            var glow = moon._glowRenderers;
            int cachedCameraId = moon.cachedCameraId;
            Log(ref lines, ref truncated, "moonPrimary[" + moonIndex + "] bodyPtr=" + moon.Pointer.ToInt64()
                + " ownerPtr=" + moon._director.Pointer.ToInt64() + " cachedCameraId=" + cachedCameraId
                + " mainCameraId=" + mainCamera.cameraId + " cachedIdEqualsMain=" + (cachedCameraId == mainCamera.cameraId)
                + " glowCount=" + (glow == null ? "none" : glow.Count.ToString()) + " " + DescribeRenderer(moon._renderer));
        }
        for (int moonIndex = 0; moonIndex < count; moonIndex++)
        {
            CelestialBody moon = bodies[moonIndex];
            if (moon == null || (int)moon.celestialBodyType != MoonTypeValue || moon._director == null
                || moon._director.Pointer != director.Pointer) continue;
            var glow = moon._glowRenderers;
            if (glow == null)
                Log(ref lines, ref truncated, "unknown: moon[" + moonIndex + "] _glowRenderers list unavailable");
            else if (glow.Count > MaxGlowRenderers)
                Log(ref lines, ref truncated, "unknown: moon[" + moonIndex + "] glow count=" + glow.Count
                    + " over cap " + MaxGlowRenderers + "; glow renderers not sampled");
            else
                for (int g = 0; g < glow.Count; g++)
                {
                    Log(ref lines, ref truncated, "moonGlow[" + moonIndex + "," + g + "] " + DescribeRenderer(glow[g]));
                    if (truncated) break;
                }
            if (truncated) break;
        }

        Log(ref lines, ref truncated, "read-only sample complete: pointers/enabled/active/layer/scene/pos/bounds/sort values/"
            + "material(shared) recorded; sortingLayer names not read (documented span-shim pitfall), rank=SortingLayer.GetLayerValueFromID(id);"
            + " screen acceptance and any repair remain outside this file");
    }

    /// <summary>单次相机校验（只校验不重选）：传入 camera 必须是传入 MainCamera._camera，且 playerOne 唯一命中同一 MainCamera。</summary>
    private static bool TryValidateCamera(Player playerOne, MainCamera mainCamera, Camera camera,
        int sceneHandle, out string detail)
    {
        detail = null;
        if (mainCamera == null || !(bool)mainCamera) { detail = "MainCamera null/destroyed"; return false; }
        if (camera == null || !(bool)camera) { detail = "Camera null/destroyed"; return false; }
        Camera bound = mainCamera._camera;
        if (bound == null || !(bool)bound || bound.Pointer != camera.Pointer)
        { detail = "passed Camera is not the passed MainCamera._camera"; return false; }
        if (!camera.enabled || camera.gameObject == null || !camera.gameObject.activeInHierarchy)
        { detail = "Camera not enabled/active"; return false; }
        if (camera.gameObject.scene.handle != sceneHandle)
        { detail = "Camera scene differs from owned root scene"; return false; }
        GameObject target = playerOne.gameObject;
        CameraMarshaller marshaller = SingletonMonoBehaviour<CameraMarshaller>._Inst;
        if (marshaller == null) { detail = "CameraMarshaller._Inst unavailable"; return false; }
        var refs = marshaller.cameraRefs;
        if (refs == null || refs.Length < 1 || refs.Length > MaxCameraRefs)
        {
            detail = "cameraRefs " + (refs == null ? "null" : "count=" + refs.Length)
                + " (expected 1.." + MaxCameraRefs + ")";
            return false;
        }
        int hits = 0;
        for (int i = 0; i < refs.Length; i++)
        {
            MainCamera candidate = refs[i];
            if (candidate == null || !(bool)candidate) continue;
            var targets = candidate.cameraTargets;
            if (targets == null) continue;
            if (targets.Length > MaxCameraTargets) { detail = "cameraTargets over cap at refs[" + i + "]"; return false; }
            bool hit = false;
            for (int t = 0; t < targets.Length; t++)
            {
                GameObject go = targets[t];
                if (go != null && go.Pointer == target.Pointer) { hit = true; break; }
            }
            if (!hit) continue;
            hits++;
            if (candidate.Pointer != mainCamera.Pointer)
            { detail = "playerOne target resolves to another MainCamera"; return false; }
        }
        if (hits != 1) { detail = "playerOne camera hits=" + hits + " (expected exactly 1)"; return false; }
        detail = "mainCamera=" + mainCamera.Pointer.ToInt64() + " camId=" + mainCamera.cameraId
            + " cam=" + camera.Pointer.ToInt64() + " camScene=" + camera.gameObject.scene.handle
            + " cullingMask=" + camera.cullingMask + " depth=" + camera.depth.ToString("F3")
            + " sortMode=" + camera.transparencySortMode + " sortAxis=" + V3(camera.transparencySortAxis)
            + " hits=1 (no Camera.main, no scan, no re-pick)";
        return true;
    }

    /// <summary>单个 renderer 的实际渲染契约（只读；material 只读 sharedMaterial，绝不触发 .material 实例化）。</summary>
    private static string DescribeRenderer(SpriteRenderer r)
    {
        if (r == null) return "null";
        GameObject go = r.gameObject;
        Vector3 position = r.transform.position;
        Bounds bounds = r.bounds;
        int layerId = r.sortingLayerID;
        return "ptr=" + r.Pointer.ToInt64() + " en=" + r.enabled
            + " act=" + (go != null && go.activeInHierarchy)
            + " goLayer=" + (go != null ? go.layer.ToString() : "none")
            + " scene=" + SceneOf(go)
            + " wp=" + V3(position) + " bndC=" + V3(bounds.center) + " bndE=" + V3(bounds.extents)
            + " layerID=" + layerId + " layerRank=" + SortingLayer.GetLayerValueFromID(layerId)
            + " order=" + r.sortingOrder + " mat=" + DescribeMaterial(r.sharedMaterial) + " groups=" + DescribeGroups(r.transform);
    }

    private static string DescribeMaterial(Material mat)
    {
        if (mat == null) return "(null)";
        Shader shader = mat.shader;
        return "ptr=" + mat.Pointer.ToInt64() + " name=" + mat.name
            + " shader=" + (shader != null ? shader.name : "(null)") + " queue=" + mat.renderQueue;
    }

    // Exact renderer ancestor chain only, bounded8; current compiler verifies the typed SortingGroup wrapper.
    private static string DescribeGroups(Transform node)
    {
        string result = "";
        int depth = 0;
        for (; node != null && depth < 8; node = node.parent, depth++)
        {
            SortingGroup group = node.GetComponent<SortingGroup>();
            if (group == null) continue;
            result += "[ancestor=" + depth + " ptr=" + group.Pointer.ToInt64() + " enabled=" + group.enabled
                + " active=" + group.gameObject.activeInHierarchy + " layerID=" + group.sortingLayerID
                + " rank=" + SortingLayer.GetLayerValueFromID(group.sortingLayerID) + " order=" + group.sortingOrder + "]";
        }
        if (node != null) return result + "unknown:ancestor-cap8";
        return result.Length == 0 ? "none-in-observed-chain" : result;
    }

    private static string GoName(SpriteRenderer r)
        => r == null || r.gameObject == null ? "none" : r.gameObject.name;

    private static string Ptr(SpriteRenderer r) => r == null ? "none" : r.Pointer.ToInt64().ToString();

    private static string SceneOf(GameObject go) => go == null ? "none" : go.scene.handle.ToString();

    private static string V3(Vector3 v)
        => "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")";

    /// <summary>有界日志：最多 MaxLines 行（含一条截断说明），超出即记 unknown 并停止输出。</summary>
    private static void Log(ref int lines, ref bool truncated, string message)
    {
        if (lines < MaxLines - 1) { lines++; PalaceCourtVisuals.Log(Tag + message); return; }
        if (truncated) return;
        truncated = true;
        lines++;
        PalaceCourtVisuals.Log(Tag + "unknown: line budget " + MaxLines + " reached; remaining sample lines dropped");
    }
}
