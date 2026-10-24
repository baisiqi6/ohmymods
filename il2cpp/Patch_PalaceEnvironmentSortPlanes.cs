using System;
using Coatsink.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomEnhancedMod;

// R7 自有环境深度平面创建契约（source-only：单文件、无 hook、无 MonoBehaviour、无 driver、无周期扫描、无任何写入）。
//
// 目的：R2 实测同 sortingLayer(0)/rank(0)/queue(3000) 的可比对象里，自有层用负 sortingOrder 抢在原生天空
// （Moon primary z=62、glow z=61.5、order0）之前绘制，导致天空后来绘制并覆盖后景。本契约只把同一套 band/recipe key
// 换成一次验证的固定 z 深度：create 时只读校验通过后，Root 逐层写 child.localPosition.z 一次并令 sortingOrder=0，
// 由已验证的相机 +Z 深度决定 sky < rear < unit < front。
//
// 用法（Root 唯一接线，见 PalaceDepthScene.Create/AddLayer）：在分配自有 root 之前，用当次已验证的
// game/camera/借用 unit 调用 TryCreate 一次；成功后才允许逐层 TryPlane(band, recipeIndex) 写 z 与 order0。
// helper 自身无状态：不持任何 Native 引用、无静态可变字段、无缓存、不重试、不扫描、不按名字/scene/cameraId 选择天空。
//
// 数值契约（证据：receipts/r6-sky-sort-cold-r2-result.json、R4/R5 calibration receipts 与 r7-runtime-wiring-boundary-review-r1）：
// s = 唯一 calibration 的 UnitsPerPixel = 0.03125（32 PPU）；band ∈ {-7..-1, 1, 2}（R7 runtime 语义 order，
// -7 仅新 mid-behind-far-roofs）；recipeIndex ∈ 0..25（冻结 runtimeRecipe 26 项，R7 起 26 层）；
// rank = band*100 + recipeIndex；localZ = -(double)rank*s/100 → float。
// 一个 band = 1 个艺术像素 = s 世界单位；同一 band 内相邻 recipe = s/100。rear（band<0）worldZ > unitZ，
// front（band>0）worldZ < unitZ；最远 rear 名义 worldZ = unitZ + 7*s（band -7 index 0），最近 front = band 2 index 25；
// 全合法组合 9 band × 26 index = 234 个 rank 的 float localZ 严格单调互异（范围 +.21875..-.0703125），
// 两端都必须留在正向 camera clip 范围内。顺序固定，不随相机移动调整，不改 xy/PPU/order，不 clamp 索引。
//
// 排序路由：创建期经 PalaceKingdomSortContract.TryRead 只读核验当次实际 Kingdom pipeline 自定义透明排序路由
// （该 custom Kingdom contract 已 Native 通过），不使用 Builtin/global transparencySortMode 假设；
// 本文件不写任何 Native/全局状态。Native 全 shader pass 未验。
internal sealed class PalaceEnvironmentSortPlanes
{
    private const string Tag = "[PalaceEnvSort] ";

    // CelestialBody.CelestialBodyType：Moon 声明在 Sun 之前 → Moon==0（receipt r6-night-sky-sort-readonly-r1）。
    private const int MoonTypeValue = 0;


    // 实测 unit/sky/owned sharedMaterial 的 renderQueue（Pow-Recolor/Pow-Emissive-Additive/MoonMat 均为 3000）。
    private const int SpriteQueue = 3000;

    private const int MaxCelestialBodies = 8;
    private const int MaxGlowRenderers = 8;

    // self + ≤7 级祖先（与已编译核实的 typed SortingGroup 读法一致）；超过即 unknown。
    private const int MaxGroupLevels = 8;

    private const int RuntimeLayerCount = 26;
    private const int RankPerBand = 100;
    private const int RearMostBand = -7;
    private const int FrontMostBand = 2;

    // 纯字段：只保留冻结的创建期数值，不持 Native 引用、可随 owner 一起丢弃。
    private readonly float _unitWorldZ;
    private readonly double _unitsPerPixel;

    private PalaceEnvironmentSortPlanes(float unitWorldZ, double unitsPerPixel)
    {
        _unitWorldZ = unitWorldZ;
        _unitsPerPixel = unitsPerPixel;
    }

    /// <summary>
    /// 只读创建契约：任一项未通过即拒绝（reason 带回，调用方不得启用 depth sort、不得 clamp 或重试）。
    /// 只读事实、无写入、无 Native alloc、无静态缓存；失败路径不改变 unit/sky/camera/global 任何状态。
    /// </summary>
    internal static bool TryCreate(Game game, SpriteRenderer unit, Camera camera, double unitsPerPixel,
        out PalaceEnvironmentSortPlanes result, out string reason)
    {
        result = null;
        reason = null;
        try
        {
            if (game == null || game.Pointer == IntPtr.Zero)
                throw new InvalidOperationException("game missing/destroyed");
            if (unit == null || !(bool)unit || unit.gameObject == null)
                throw new InvalidOperationException("reference unit renderer missing/destroyed");
            if (camera == null || !(bool)camera || camera.gameObject == null)
                throw new InvalidOperationException("camera missing/destroyed");

            // 身份：直接读 static _Inst（不用行为未解码的 Inst getter），并要求它就是当次 game。
            Managers managers = SingletonMonoBehaviour<Managers>._Inst;
            if (managers == null || managers.game == null)
                throw new InvalidOperationException("Managers._Inst/Managers.game unavailable");
            if (managers.game.Pointer != game.Pointer)
                throw new InvalidOperationException("Managers.game is not the caller game");

            CampaignSaveData campaign = CampaignSaveData.current;
            if (campaign == null || campaign.CurrentLand != PalaceLand12Runtime.LandIndex
                || game.currentLand != PalaceLand12Runtime.LandIndex)
                throw new InvalidOperationException("land12 identity differs (campaign="
                    + (campaign == null ? "none" : campaign.CurrentLand.ToString()) + " game=" + game.currentLand
                    + " expected=" + PalaceLand12Runtime.LandIndex + ")");
            if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth)
                throw new InvalidOperationException("offline worldAuth required (online=" + NetworkBigBoss.IsOnline
                    + " worldAuth=" + NetworkBigBoss.HasWorldAuth + ")");

            int scene = unit.gameObject.scene.handle;
            if (!unit.enabled || !unit.gameObject.activeInHierarchy)
                throw new InvalidOperationException("reference unit renderer is not live (enabled/active)");
            if (!camera.enabled || !camera.gameObject.activeInHierarchy)
                throw new InvalidOperationException("camera is not live (enabled/active)");
            if (camera.gameObject.scene.handle != scene)
                throw new InvalidOperationException("camera scene differs from the reference unit scene (camera="
                    + camera.gameObject.scene.handle + " unit=" + scene + ")");

            // reference unit 渲染契约（只读：sharedMaterial 不触发 .material 实例化）。
            int unitOrder = unit.sortingOrder;
            if (unitOrder != 0)
                throw new InvalidOperationException("reference unit sortingOrder=" + unitOrder + " != 0");
            int unitLayerId = unit.sortingLayerID;
            Material unitMaterial = unit.sharedMaterial;
            if (unitMaterial == null)
                throw new InvalidOperationException("reference unit sharedMaterial unavailable");
            int unitQueue = unitMaterial.renderQueue;
            if (unitQueue != SpriteQueue)
                throw new InvalidOperationException("reference unit renderQueue=" + unitQueue + " != " + SpriteQueue);
            if (HasActiveSortingGroup(unit.transform, out string unitGroupDetail))
                throw new InvalidOperationException("reference unit is under an enabled active SortingGroup ("
                    + unitGroupDetail + "); owned order0/depth contract would be overridden");

            // 相机契约（只读，不改任何设置）：ortho + 有效 sort 模式 + forward +Z + 有限正 clip。
            if (!camera.orthographic)
                throw new InvalidOperationException("camera is not orthographic");
            if (!PalaceKingdomSortContract.TryRead(camera, unit, out int firstPassMask, out string routeReason))
                throw new InvalidOperationException("current KingdomPipeline route rejected: " + routeReason);
            Vector3 forward = camera.transform.forward;
            if (!Finite(forward.x) || !Finite(forward.y) || !Finite(forward.z)
                || forward.x != 0f || forward.y != 0f || forward.z != 1f)
                throw new InvalidOperationException("camera forward is not exactly +Z: " + V3(forward));
            float nearClip = camera.nearClipPlane;
            float farClip = camera.farClipPlane;
            if (!Finite(nearClip) || !Finite(farClip) || !(nearClip > 0f) || !(farClip > nearClip))
                throw new InvalidOperationException("camera clip planes invalid (near=" + nearClip.ToString("R")
                    + " far=" + farClip.ToString("R") + ")");
            Vector3 camPosition = camera.transform.position;
            if (!Finite(camPosition.x) || !Finite(camPosition.y) || !Finite(camPosition.z))
                throw new InvalidOperationException("camera position not finite");

            // 平面数值来源：单一 calibration 的 s 与借用 unit 的实际世界 z。
            if (!IsFinite(unitsPerPixel) || !(unitsPerPixel > 0d))
                throw new InvalidOperationException("unitsPerPixel is not finite positive");
            float unitsPerPixelFloat = (float)unitsPerPixel;
            if (!Finite(unitsPerPixelFloat) || !(unitsPerPixelFloat > 0f))
                throw new InvalidOperationException("unitsPerPixel is not representable as a positive finite Unity float");
            Vector3 unitPosition = unit.transform.position;
            if (!Finite(unitPosition.x) || !Finite(unitPosition.y) || !Finite(unitPosition.z))
                throw new InvalidOperationException("reference unit position not finite");
            float unitZ = unitPosition.z;
            RequirePlaneSortPoint(unit, "unit", unitZ);

            // 全合法组合（9 个合法 band × 26 runtime index = 234 个 rank）的 float 平面：finite、严格单调（⇒ 任意两个 rank
            // 都不在 float 里塌成同一个 z，含相邻 rank）、rear/front 侧正确、两端都在正向 clip 内。不 clamp、不量化。
            bool havePrevious = false;
            float previousLocal = 0f;
            int planeCount = 0;
            double rearWorld = double.NegativeInfinity;
            double frontWorld = double.PositiveInfinity;
            float rearMostPlaneWorld = 0f;
            for (int band = RearMostBand; band <= FrontMostBand; band++)
            {
                if (band == 0) continue; // 合法 band 只有 -6..-1 与 1,2
                for (int index = 0; index < RuntimeLayerCount; index++)
                {
                    int rank = band * RankPerBand + index;
                    float localFloat = PlaneZ(rank, unitsPerPixel);
                    if (!Finite(localFloat))
                        throw new InvalidOperationException("rank " + rank + " localZ is not finite");
                    if (havePrevious && !(localFloat < previousLocal))
                        throw new InvalidOperationException("rank " + rank + " collapses with the previous rank in float (localZ "
                            + localFloat.ToString("R") + " >= " + previousLocal.ToString("R")
                            + "); no clamp/quantize is applied");
                    previousLocal = localFloat;
                    havePrevious = true;
                    planeCount++;
                    float worldFloat = (float)((double)unitZ + localFloat);
                    if (!Finite(worldFloat))
                        throw new InvalidOperationException("rank " + rank + " worldZ is not finite");
                    if (rank < 0 ? !(worldFloat > unitZ) : !(worldFloat < unitZ))
                        throw new InvalidOperationException("rank " + rank + " does not stay on the expected side of the reference unit in float (worldZ "
                            + worldFloat.ToString("R") + " unitZ " + unitZ.ToString("R") + ")");
                    double world = worldFloat;
                    if (world > rearWorld)
                    {
                        rearWorld = world;
                        rearMostPlaneWorld = worldFloat;
                    }
                    if (world < frontWorld) frontWorld = world;
                }
            }
            // 最远 rear 名义值 = band -7 index 0 → unitZ + 7*s；最近的 front = band 2 index 25。单调性保证检查两端即可。
            // Exact +Z makes view-depth equal worldZ-cameraZ; Root also checks actual 26 float view-depth keys.
            double nominalRearWorld = (double)unitZ + 7d * unitsPerPixel;
            if (!IsFinite(nominalRearWorld))
                throw new InvalidOperationException("nominal farthest rear worldZ is not finite");
            double frontDepth = frontWorld - (double)camPosition.z;
            double rearDepth = rearWorld - (double)camPosition.z;
            if (!IsFinite(frontDepth) || !IsFinite(rearDepth)
                || !(frontDepth > (double)nearClip) || !(rearDepth < (double)farClip))
                throw new InvalidOperationException("owned planes leave the positive camera clip range (frontZ="
                    + frontWorld.ToString("R") + " rearZ=" + rearWorld.ToString("R") + " camZ=" + camPosition.z.ToString("R")
                    + " near=" + nearClip.ToString("R") + " far=" + farClip.ToString("R") + ")");

            // 已注册天空：只按注册表逐项判定与本 director 的 Moon(_director 指针一致)；不按名字/scene/first/cameraId 选。
            Director director = managers.director;
            if (director == null || director.Pointer == IntPtr.Zero)
                throw new InvalidOperationException("Managers.director unavailable");
            var bodies = director.lunarCelestialBodies;
            if (bodies == null)
                throw new InvalidOperationException("director.lunarCelestialBodies unavailable; no name search/scene scan/sun fallback");
            int bodyCount = bodies.Count;
            if (bodyCount < 1 || bodyCount > MaxCelestialBodies)
                throw new InvalidOperationException("lunarCelestialBodies count=" + bodyCount + " outside 1.." + MaxCelestialBodies);
            int eligibleMoons = 0;
            string skyDetail = "";
            for (int i = 0; i < bodyCount; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || body.Pointer == IntPtr.Zero)
                    throw new InvalidOperationException("lunarCelestialBodies[" + i
                        + "] is null/destroyed (all registered entries must be analyzable)");
                Director owner = body._director;
                if ((int)body.celestialBodyType != MoonTypeValue) continue;
                if (owner == null || owner.Pointer != director.Pointer)
                    throw new InvalidOperationException("registered moon[" + i + "] director ownership is missing/drifted; no partial contract");
                eligibleMoons++;
                RequireSkyRenderer(body._renderer, "moon[" + i + "] primary", unitLayerId, unitOrder, unitQueue,
                    nominalRearWorld, rearMostPlaneWorld, camera.cullingMask, firstPassMask, out string primaryDetail);
                var glow = body._glowRenderers;
                if (glow == null)
                    throw new InvalidOperationException("moon[" + i + "] _glowRenderers list unavailable");
                if (glow.Count > MaxGlowRenderers)
                    throw new InvalidOperationException("moon[" + i + "] glow count=" + glow.Count + " over cap " + MaxGlowRenderers);
                string glowDetail = "";
                for (int g = 0; g < glow.Count; g++)
                {
                    RequireSkyRenderer(glow[g], "moon[" + i + "] glow[" + g + "]", unitLayerId, unitOrder, unitQueue,
                        nominalRearWorld, rearMostPlaneWorld, camera.cullingMask, firstPassMask, out string glowEntry);
                    glowDetail += (glowDetail.Length == 0 ? "" : ",") + glowEntry;
                }
                // cachedCameraId 只是诊断数据，不用于选择/修复参照；天空 scene 不强制等于 unit scene（可 DDOL）。
                skyDetail += " moon[" + i + "] body=" + body.Pointer.ToInt64() + " cachedCam=" + body.cachedCameraId
                    + " primary{" + primaryDetail + "} glows=" + glow.Count + "[" + glowDetail + "]";
            }
            if (eligibleMoons == 0)
                throw new InvalidOperationException("no eligible Moon (type==" + MoonTypeValue
                    + " && _director==director); sun fallback is not permitted");

            var planes = new PalaceEnvironmentSortPlanes(unitZ, unitsPerPixel);
            result = planes;
            Log("create ok (read-only): managers=" + managers.Pointer.ToInt64() + " game=" + game.Pointer.ToInt64()
                + " campaign=" + campaign.Pointer.ToInt64() + " land=" + PalaceLand12Runtime.LandIndex
                + " online=" + NetworkBigBoss.IsOnline + " worldAuth=" + NetworkBigBoss.HasWorldAuth
                + "; unit=" + unit.Pointer.ToInt64() + " scene=" + scene + " z=" + unitZ.ToString("R")
                + " order=" + unitOrder + " layerID=" + unitLayerId + " queue=" + unitQueue
                + " groups=none(<=" + MaxGroupLevels + " levels checked)"
                + "; cam=" + camera.Pointer.ToInt64() + " scene=" + camera.gameObject.scene.handle + " ortho=True"
                + " actualKingdomRouteOrthographic=True firstPassMask=0x" + firstPassMask.ToString("x8")
                + " fwd=" + V3(forward) + " pos=" + V3(camPosition)
                + " near=" + nearClip.ToString("R") + " far=" + farClip.ToString("R")
                + "; s=" + unitsPerPixel.ToString("R") + " unitZ=" + unitZ.ToString("R")
                + " bandStep=" + unitsPerPixel.ToString("R") + " recipeStep=" + (unitsPerPixel / 100d).ToString("R")
                + " planes=" + planeCount + " ranks distinct-strict"
                + " rearWorldZ=" + rearWorld.ToString("R") + " frontWorldZ=" + frontWorld.ToString("R")
                + " nominalRearWorldZ=" + nominalRearWorld.ToString("R")
                + " clipDepthFront=" + frontDepth.ToString("R") + " clipDepthRear=" + rearDepth.ToString("R")
                + "; moons=" + eligibleMoons + "/" + bodyCount + " eligible" + skyDetail
                + "; no writes to unit/sky/camera/global, no SortingGroup added; Native full-pass acceptance pending");
            return true;
        }
        catch (Exception error)
        {
            string detail = error is InvalidOperationException
                ? error.Message
                : error.GetType().Name + ": " + error.Message;
            reason = (error is InvalidOperationException ? "owned draw contract rejected: " : "unknown fault (single attempt, no retry): ")
                + detail;
            Log(reason + "; nothing written (unit/sky/camera/global untouched)");
            return false;
        }
    }

    /// <summary>
    /// 纯平面映射：band ∈ {-7..-1,1,2}、recipeIndex ∈ 0..25 → localZ（float）。只返回数值，不写任何状态；
    /// 非法输入或 float 不可表现/塌陷/越侧返回 false + reason，不 clamp、不量化、不重试。
    /// </summary>
    internal bool TryPlane(int band, int recipeIndex, out float localZ, out string reason)
    {
        localZ = 0f;
        reason = null;
        if (!IsReviewedBand(band))
        {
            reason = "band " + band + " is outside the reviewed set {-7..-1,1,2}";
            return false;
        }
        if (recipeIndex < 0 || recipeIndex >= RuntimeLayerCount)
        {
            reason = "recipeIndex " + recipeIndex + " is outside 0.." + (RuntimeLayerCount - 1);
            return false;
        }
        int rank = band * RankPerBand + recipeIndex;
        float candidate = PlaneZ(rank, _unitsPerPixel);
        if (!Finite(candidate))
        {
            reason = "rank " + rank + " localZ is not finite";
            return false;
        }
        float worldFloat = (float)((double)_unitWorldZ + candidate);
        if (!Finite(worldFloat))
        {
            reason = "rank " + rank + " worldZ is not finite";
            return false;
        }
        if (rank < 0 ? !(worldFloat > _unitWorldZ) : !(worldFloat < _unitWorldZ))
        {
            reason = "rank " + rank + " does not stay on the expected side of the reference unit in float (worldZ "
                + worldFloat.ToString("R") + " unitZ " + _unitWorldZ.ToString("R") + ")";
            return false;
        }
        localZ = candidate;
        return true;
    }

    // rank = band*100 + recipeIndex；localZ = -(double)rank*s/100 → float（band 差 = 1 艺术像素 = s，recipe 差 = s/100）。
    private static float PlaneZ(int rank, double unitsPerPixel)
        => (float)(-(double)rank * unitsPerPixel / 100d);

    private static bool IsReviewedBand(int band)
        => (band >= RearMostBand && band <= -1) || (band >= 1 && band <= FrontMostBand);

    /// <summary>
    /// 只读渲染契约：renderer 自身/≤7 级祖先上不允许 enabled 且 activeInHierarchy 的 SortingGroup
    /// （组会覆盖 order/depth 语义）。层级超过 8 级记 unknown（抛给唯一 catch），不做全场扫描。
    /// </summary>
    private static bool HasActiveSortingGroup(Transform node, out string detail)
    {
        detail = null;
        int depth = 0;
        for (; node != null && depth < MaxGroupLevels; node = node.parent, depth++)
        {
            SortingGroup group = node.GetComponent<SortingGroup>();
            if (group == null || group.gameObject == null) continue;
            if (group.enabled && group.gameObject.activeInHierarchy)
            {
                detail = "level=" + depth + " ptr=" + group.Pointer.ToInt64() + " enabled=" + group.enabled
                    + " active=" + group.gameObject.activeInHierarchy;
                return true;
            }
        }
        if (node != null)
            throw new InvalidOperationException("SortingGroup ancestor chain exceeds " + MaxGroupLevels
                + " levels (unknown, no full-scene scan)");
        return false;
    }

    /// <summary>
    /// 已注册 eligible 天空 renderer 的只读契约：live、与 reference unit 同 sortingLayerID/order/queue、
    /// 无 enabled active SortingGroup、worldZ finite 且在最远 rear 平面之外。天空 scene 不强制等于 unit scene。
    /// </summary>
    private static void RequireSkyRenderer(SpriteRenderer renderer, string label, int unitLayerId, int unitOrder,
        int unitQueue, double nominalRearWorld, double rearMostPlaneWorld, int cameraMask, int firstPassMask, out string detail)
    {
        detail = null;
        if (renderer == null || !(bool)renderer || renderer.gameObject == null)
            throw new InvalidOperationException(label + " is missing/destroyed");
        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
            throw new InvalidOperationException(label + " is not live (enabled=" + renderer.enabled
                + " active=" + renderer.gameObject.activeInHierarchy + ")");
        if (renderer.sortingLayerID != unitLayerId)
            throw new InvalidOperationException(label + " sortingLayerID=" + renderer.sortingLayerID
                + " != reference unit " + unitLayerId);
        if (renderer.sortingOrder != unitOrder)
            throw new InvalidOperationException(label + " sortingOrder=" + renderer.sortingOrder
                + " != reference unit " + unitOrder + " (0)");
        Material material = renderer.sharedMaterial; // 只读共享材质，不触发 .material 实例化
        if (material == null)
            throw new InvalidOperationException(label + " sharedMaterial unavailable");
        if (material.renderQueue != unitQueue)
            throw new InvalidOperationException(label + " renderQueue=" + material.renderQueue
                + " != reference unit " + unitQueue);
        if (HasActiveSortingGroup(renderer.transform, out string groupDetail))
            throw new InvalidOperationException(label + " is under an enabled active SortingGroup (" + groupDetail + ")");
        int goLayer = renderer.gameObject.layer;
        if (goLayer < 0 || goLayer > 31)
            throw new InvalidOperationException(label + " GO layer outside0..31");
        int bit = 1 << goLayer;
        if ((cameraMask & bit) != 0 && (firstPassMask & bit) == 0)
            throw new InvalidOperationException(label + " is visible to P1 but not in the same first pass as owned/unit");
        float z = renderer.transform.position.z;
        if (!Finite(z))
            throw new InvalidOperationException(label + " worldZ is not finite");
        RequirePlaneSortPoint(renderer, label, z);
        if (!((double)z > nominalRearWorld) || !((double)z > rearMostPlaneWorld))
            throw new InvalidOperationException(label + " worldZ=" + z.ToString("R")
                + " is not beyond the farthest owned rear plane (nominal " + nominalRearWorld.ToString("R")
                + " / actual " + rearMostPlaneWorld.ToString("R") + ")");
        detail = "z=" + z.ToString("F3") + " layerID=" + renderer.sortingLayerID + " order=" + renderer.sortingOrder
            + " queue=" + material.renderQueue + " ptr=" + renderer.Pointer.ToInt64();
    }

    private static void RequirePlaneSortPoint(SpriteRenderer renderer, string label, float planeZ)
    {
        SpriteSortPoint point = renderer.spriteSortPoint;
        float sortZ;
        if (point == SpriteSortPoint.Pivot) sortZ = renderer.transform.position.z;
        else if (point == SpriteSortPoint.Center) sortZ = renderer.bounds.center.z;
        else throw new InvalidOperationException(label + " unknown SpriteSortPoint=" + (int)point);
        if (!Finite(sortZ) || sortZ != planeZ)
            throw new InvalidOperationException(label + " effective sort-point z=" + sortZ.ToString("R")
                + " differs from planeZ=" + planeZ.ToString("R") + " point=" + point);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string V3(Vector3 value)
        => "(" + value.x.ToString("F3") + "," + value.y.ToString("F3") + "," + value.z.ToString("F3") + ")";

    private static void Log(string message) => PalaceCourtVisuals.Log(Tag + message);
}
