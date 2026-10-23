// PalaceAdultBodyMetrics（私有只读取样，source-only）：成人 BODY 的第一手 native 元数据单次读数。
//
// 目标：给 Root 之后"在站姿图上标出身体头顶/脚底"这一步提供当帧真实输入 —— actor/renderer/sprite/texture
//   身份与 scene、actual root localScale、实际 renderer.localToWorldMatrix、sprite rect/pivot/PPU、renderer
//   flip、texture readable、动画 state/hash/过渡/ASStand、GreekScaleScope 只读 registered target。
//   CamWater 的 Mac2.4.0 原生 getter 只 RET；不调用、不投影 Water 实例字段，水面保持 Unknown。
//   本文件不测 BODY、不猜 19px 或骑乘整高、不读 GPU/不导出纹理；BODY 像素头顶脚底必须记 Unknown。
//   这是明确的 metadata 样本：`sample complete` **不等于**可信站姿、**不等于**成人 H 冻结（仍需同帧站姿图 +
//   身体头脚标记；晚期之后原生/Render 是否再写也未在源码中证明）。
//
// 门与一次性语义（无新 hook / 无 MonoBehaviour / 无 driver / 无周期探查）：
//   * 正常 PC / Android：Enabled = PALACE_ADULT_BODY_METRICS_PRIVATE 能力（RuntimeProfile 固定 false）且
//     PalaceScrollNativeBinding.Enabled；否则零动作、零日志。RuntimeProfile 表不改，旧诊断路径仍不可到达。
//   * 仅 PALACE_PAID_CAPTURE_DIAGNOSTICS 诊断构建：Enabled = PalaceScrollNativeBinding.Enabled 且既有
//     PalaceSaveBindingSource 已捕获实际 private save binding；精确 agent Pointer/folder 仍在 Arm 一次受理。
//   * Arm（Root 在既有 PalaceCourtVisuals.GameplayFrame 调用）：先闭合"基础 scope"——实际 Playing + 离线 +
//     HasWorldAuth + 非 saving + TryScope 口径 currentCampaign（非挑战 / Greek / GetCurrentCampaign 与
//     CampaignSaveData.current 同实例）+ exact Managers.game/kingdom/level/world.gameLayer 同 scene +
//     game.currentLand 与 campaign.CurrentLand 都=8 + typed `Filer.DotNetAgent.folder` 精确等于既有
//     private save binding（环境变量配置不构成证明）。基础 scope 首次闭合后立刻 latch `_validationAttempt`。
//     正常构建随后 PalaceTripContext.Read / TryBinding / RecordMatches **只受理一次**：任一失败即 `unknown` +
//     Close（不 re-arm、不延时重试、不新增 hook/扫描）；全部通过才冻结 ownerPtrs + scene + agent Pointer。
//     诊断构建不读 trip（fresh source8 无 trip record），其余 identity 全部保留，不伪造 trip 日志。
//     不 spawn、不自动旅行；空 land12 不采。
//   * ObserveAfterRegisteredY（Root 在既有 ScaleRegistryHolder.LateUpdate 的 MaintainRegisteredY 正常返回后调用）：
//     取样前复核 Enabled / 离线 + authority / Greek + 非挑战 / exact managers + scene / `global.GetCurrentCampaign()`
//     的 exact frozen Campaign / 同 agent Pointer + 同 folder；任一漂移 → 一次日志取消（不重 Arm、不重扫、
//     不重读 trip 文件）。否则唯一一次有界 roster 读取（<=256）→ 至多两行 actor + 一行摘要 → 永久关闭。
//     任何读取故障：一次日志 + 关闭，不重试、不换 actor、不在捕获后扫更多。
//
// 只读边界：不写 scale/native/存档/renderer/Animator/输入，不控制演员，不做全场扫描；bounded roster <=256；
//   日志只经 PalaceCourtVisuals.Log("[AdultBody] ...")，最多两 actor + 一次 scope/fault 摘要，未知不记成 0。
using System;
using System.Globalization;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceAdultBodyMetrics
{
    private const string EnableEnv = "PALACE_ADULT_BODY_METRICS_PRIVATE";
    private const int SourceLandIndex = 8;
    private const int MaxRoster = 256;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
    private const int MaxCameraRefs = 4;
    private const int MaxCameraTargets = 4;
#endif

    // 本次 private case 的实际 save-root（同 R5 PalaceCourtScene.SaveFolder；typed agent folder 的 exact 比对值）。

    private static bool _armed;             // 本进程已冻结唯一 epoch；永不重置
    private static bool _closed;            // 已输出结果或失败；永不重置（不重试、不重扫）
    private static bool _validationAttempt; // 基础 scope 闭合后的唯一一次身份受理（latch 后永不重试）
    private static bool _warnedGate;        // 离线/权威门等待说明：只报一次
    private static Epoch _epoch;

    private struct Epoch
    {
        internal IntPtr Game;
        internal IntPtr Kingdom;
        internal IntPtr Level;
        internal IntPtr Layer;
        internal IntPtr Global;
        internal IntPtr Campaign;
        internal IntPtr Agent;
        internal int Scene;
    }

    internal static bool Enabled
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        => DiagnosticEnabled;
#else
        => PalaceRuntimeProfile.Capability(EnableEnv) && PalaceScrollNativeBinding.Enabled;
#endif

#if PALACE_PAID_CAPTURE_DIAGNOSTICS
    /// <summary>
    /// 诊断构建门 = 诊断编译 + R4 scroll + 既有 private save binding 已捕获 + 当前 typed Filer.DotNetAgent
    /// 仍是该 binding 的 agent（Pointer/folder 精确）。Arm 每帧只走廉价前置门，精确 agent 在唯一受理点冻结。
    /// </summary>
    private static bool DiagnosticEnabled
        => PalaceScrollNativeBinding.Enabled && DiagnosticBindingCaptured() && DiagnosticAgentMatches(out _);

    /// <summary>廉价 binding 读取；不在此处读 agent/folder（避免逐帧字符串规范化）。</summary>
    private static bool DiagnosticBindingCaptured()
    {
        try { return PalaceSaveBindingSource.Current != null; }
        catch (Exception) { return false; }
    }

    private static bool DiagnosticAgentMatches(out string detail)
        => CurrentAgent(out _, out detail) != null;
#endif

    /// <summary>
    /// Root 在既有 PalaceCourtVisuals.GameplayFrame(Game) 内调用。基础 scope 未闭合 → 静默保持等待；
    /// 闭合后身份只受理一次（latch 后任何失败都 Close，不重试、不重读）。全部通过才冻结 ownerPtrs + scene + agent。
    /// </summary>
    internal static void Arm(Game game)
    {
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        if (_closed || _armed || _validationAttempt) return;
        // Per-frame cheap gate; the exact typed agent/folder is accepted once below via CurrentAgent.
        if (!PalaceScrollNativeBinding.Enabled || !DiagnosticBindingCaptured()) return;
#else
        if (_closed || _armed || _validationAttempt || !Enabled) return;
#endif
        try
        {
            if (game == null || game.state != Game.State.Playing) return;           // 实际 Playing
            if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth)            // 离线 + 世界权威
            { WaitOnce(ref _warnedGate, "gate: online=" + NetworkBigBoss.IsOnline + " worldAuth=" + NetworkBigBoss.HasWorldAuth); return; }
            if (IslandSaveData.isSavingGame) return;                                // 非 saving
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != game.Pointer) return;
            Kingdom kingdom = managers.kingdom;
            Level level = managers.level;
            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            if (kingdom == null || level == null || layer == null) return;
            if (kingdom.gameObject == null || level.gameObject == null || layer.gameObject == null) return;
            if (!layer.gameObject.scene.IsValid()) return;
            int scene = layer.gameObject.scene.handle;
            if (level.gameObject.scene.handle != scene
                || kingdom.gameObject.scene.handle != scene) return;                // exact managers 同 scene
            GlobalSaveData global = GlobalSaveData.loaded;
            if (global == null || global.InChallenge) return;
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return;
            CampaignSaveData current = CampaignSaveData.current;
            if (current == null) return;
            CampaignSaveData owner = global.GetCurrentCampaign();
            if (owner == null || owner.Pointer != current.Pointer) return;          // TryScope 口径 currentCampaign
            if (game.currentLand != SourceLandIndex || current.CurrentLand != SourceLandIndex) return; // 只在 source8
            // 当前 source8 的基础 scope 已闭合：包括实际 agent 的所有身份读取只受理一次。
            // agent 缺失/不匹配/读取故障都是本窗口 unknown，不等待另一个窗口覆盖此结果。
            _validationAttempt = true;
            Filer.DotNetAgent agent = CurrentAgent(out IntPtr agentPtr, out string agentDetail);
            SampleSource8Camera(managers, game, kingdom, level, layer, global, current, agent, agentPtr, scene);
            if (agent == null)
            { Close("identity unknown: private agent not the exact save-root: " + agentDetail); return; }

#if !PALACE_PAID_CAPTURE_DIAGNOSTICS
            // 正常构建：trip 只受理一次（诊断构建不读 trip，见文件头）。
            PalaceTripContext.ReadStatus status = PalaceTripContext.Read(global, out PalaceTripRecord record, out string readDetail);
            if (status != PalaceTripContext.ReadStatus.Ok || record == null)
            { Close("identity unknown: trip record not readable (status=" + status + " detail=" + readDetail + ")"); return; }
            if (record.SourceLand != SourceLandIndex)
            { Close("identity unknown: trip sourceLand=" + record.SourceLand + " != " + SourceLandIndex); return; }
            if (!PalaceTripContext.TryBinding(global, current, out string guid, out int slot, out string bindReason))
            { Close("identity unknown: trip binding not established (" + bindReason + ")"); return; }
            if (!PalaceTripContext.RecordMatches(current, guid, slot, record, out string matchReason))
            { Close("identity unknown: trip record/campaign mismatch (" + matchReason + ")"); return; }
#endif

            _epoch = new Epoch
            {
                Game = game.Pointer,
                Kingdom = kingdom.Pointer,
                Level = level.Pointer,
                Layer = layer.Pointer,
                Global = global.Pointer,
                Campaign = current.Pointer,
                Agent = agentPtr,
                Scene = scene,
            };
            _armed = true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            Log("armed t=" + Time.unscaledTime.ToString("F2") + ": diagnostic=PALACE_PAID_CAPTURE_DIAGNOSTICS"
                + " scrollR4=1 offline=1 auth=1 saving=0 state=" + game.state
                + " land=game" + game.currentLand + "/campaign" + current.CurrentLand
                + " biome=" + biome.BiomeIndex + " game=" + Ptr(game.Pointer) + " kingdom=" + Ptr(kingdom.Pointer)
                + " level=" + Ptr(level.Pointer) + " gameLayer=" + Ptr(layer.Pointer) + " scene=" + scene
                + " global=" + Ptr(global.Pointer) + " campaign=" + Ptr(current.Pointer)
                + " agent=" + Ptr(agentPtr) + " agentFolder=exact-private-save-binding"
                + " trip=not-required (diagnostic source8 metadata; no trip read and no fabricated record)"
                + "; freeze=ownerPtrs+scene+agent; identity accepted once; no re-arm");
#else
            Log("armed t=" + Time.unscaledTime.ToString("F2") + ": private=1+scrollR4 offline=1 auth=1 saving=0 state="
                + game.state + " land=game" + game.currentLand + "/campaign" + current.CurrentLand
                + " biome=" + biome.BiomeIndex + " game=" + Ptr(game.Pointer) + " kingdom=" + Ptr(kingdom.Pointer)
                + " level=" + Ptr(level.Pointer) + " gameLayer=" + Ptr(layer.Pointer) + " scene=" + scene
                + " global=" + Ptr(global.Pointer) + " campaign=" + Ptr(current.Pointer)
                + " agent=" + Ptr(agentPtr) + " agentFolder=exact-private-root"
                + " record{read=" + readDetail + " sourceLand=" + record.SourceLand + " palaceLand=" + record.PalaceLand
                + " phase=" + record.Phase + " tripId=" + record.TripId + " binding=ok match=ok}"
                + "; freeze=ownerPtrs+scene+agent; identity accepted once; no re-arm");
#endif
        }
        catch (Exception e)
        {
            Close("arm fault (read-only; closed, no retry): " + e.GetType().Name + " " + e.Message);
        }
    }

    // Uses the existing first-source8 validation latch; called before any trip/BODY read or its early returns.
    // Typed source8 baseline only. No new hook/driver/scan, camera control, native pointer arithmetic or saved state.
    private static void SampleSource8Camera(Managers expectedManagers, Game game, Kingdom kingdom,
        Level level, Transform layer, GlobalSaveData global, CampaignSaveData campaign,
        Filer.DotNetAgent acceptedAgent, IntPtr acceptedAgentPointer, int scene)
    {
        try
        {
            if (!_validationAttempt || !Enabled || NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth
                || IslandSaveData.isSavingGame || game == null || game.state != Game.State.Playing
                || game.currentLand != SourceLandIndex || campaign == null || campaign.CurrentLand != SourceLandIndex)
                throw new InvalidOperationException("first-source8-private-window-unavailable");
            Managers currentManagers = Managers.Inst;
            CampaignSaveData currentCampaign = CampaignSaveData.current;
            GlobalSaveData currentGlobal = GlobalSaveData.loaded;
            if (currentManagers == null || expectedManagers == null
                || currentManagers.Pointer != expectedManagers.Pointer
                || currentManagers.game == null || currentManagers.game.Pointer != game.Pointer
                || currentManagers.kingdom == null || currentManagers.kingdom.Pointer != kingdom.Pointer
                || currentManagers.level == null || currentManagers.level.Pointer != level.Pointer
                || currentManagers.world == null || currentManagers.world.gameLayer == null
                || currentManagers.world.gameLayer.Pointer != layer.Pointer
                || currentGlobal == null || global == null || currentGlobal.Pointer != global.Pointer
                || currentGlobal.InChallenge || currentCampaign == null || currentCampaign.Pointer != campaign.Pointer)
                throw new InvalidOperationException("source8-exact-owner-drift");
            CampaignSaveData selectedCampaign = currentGlobal.GetCurrentCampaign();
            if (selectedCampaign == null || selectedCampaign.Pointer != campaign.Pointer)
                throw new InvalidOperationException("source8-selected-campaign-drift");
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex)
                throw new InvalidOperationException("source8-biome-drift");
            var currentAgent = Filer.agent as Il2CppObjectBase;
            // CurrentAgent already accepted its exact private folder once immediately above. Do not reread trip/BODY here.
            if (acceptedAgent == null || acceptedAgentPointer == IntPtr.Zero
                || acceptedAgent.Pointer != acceptedAgentPointer || currentAgent == null
                || currentAgent.Pointer != acceptedAgentPointer)
                throw new InvalidOperationException("source8-private-agent-unproved-or-drift");
            if (kingdom.gameObject == null || level.gameObject == null || layer.gameObject == null
                || kingdom.gameObject.scene.handle != scene || level.gameObject.scene.handle != scene
                || layer.gameObject.scene.handle != scene)
                throw new InvalidOperationException("source8-owner-scene-drift");
            string min = DescribeSource8Boundary(kingdom.cameraMinX);
            string max = DescribeSource8Boundary(kingdom.cameraMaxX);
            Source8CameraLog("readonly single first Playing: game=" + Ptr(game.Pointer)
                + " kingdom=" + Ptr(kingdom.Pointer) + " level=" + Ptr(level.Pointer)
                + " scene=" + scene + " campaign=" + Ptr(campaign.Pointer)
                + " agent=" + Ptr(acceptedAgentPointer) + " exactPrivateFolder=accepted"
                + " land=game" + game.currentLand + "/campaign" + campaign.CurrentLand
                + "; typedCameraMinX={" + min + "}; typedCameraMaxX={" + max + "}"
                + "; getter metadata0x90/0x98 is prior same-interop evidence, not raw memory read or reproved offset"
                + "; no camera writes; BODY/trip flow continues unchanged");
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            SampleSource8CameraAndWater(kingdom, scene);
#endif
        }
        catch (Exception e)
        {
            // Only this diagnostic becomes unknown; never Close/arm/correct the existing BODY workflow.
            Source8CameraLog("unknown: " + e.GetType().Name + " " + e.Message
                + "; one-shot, original BODY/trip flow continues");
        }
    }

#if PALACE_PAID_CAPTURE_DIAGNOSTICS
    // Diagnostic-only typed camera/water metadata: one bounded 4x4 CameraMarshaller match for P1, no Camera.main,
    // no re-pick, no per-frame scan, no write path and no material getter with possible lazy side effects.
    private static void SampleSource8CameraAndWater(Kingdom kingdom, int scene)
    {
        if (!TryResolveUniqueP1Camera(kingdom, scene, out MainCamera mainCamera, out Camera camera, out string identity))
        {
            Source8CameraLog("unknown: typed camera identity rejected: " + identity
                + "; one-shot, no fallback/re-pick; BODY/trip flow continues");
            return;
        }
        Source8CameraLog("camera identity ok: " + identity + "; read-only typed sample");
        try { Source8CameraLog("camera[" + DescribeCamera(camera) + "]"); }
        catch (Exception e) { Source8CameraLog("unknown: camera fields unreadable (" + e.GetType().Name + ")"); }

        // Mac 2.4.0 audit: token 0x060023bc -> ARM64 0x729328 is RET-only,
        // returning self rather than a Water object. Do not invoke it or project Water fields.
        // This is a recorded audit identity, not runtime version detection or authority to use _water.
        Source8CameraLog("water=unknown: CamWater sampling disabled; audited Mac ARM64 2.4.0 "
            + "fat=738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a "
            + "token=0x060023bc VA=0x729328 RET-only; runtime version not inferred; "
            + "no Water instance fields/quad or raw-field substitution");
        try { Source8CameraLog("waterPipeline[" + DescribeWaterPipeline() + "]"); }
        catch (Exception e) { Source8CameraLog("unknown: water pipeline fields unreadable (" + e.GetType().Name + ")"); }
        Source8CameraLog("water limits: visibleWaterY=Unknown (current pipeline water is a post fullscreenBlit material pass;"
            + " geometricQuadTop/waterTopOffset/debugRenderer are NOT the visible surface); no waterY formula;"
            + " no ResizeWater/SetReflectionBuffer/SetPropertiesOnMaterial/GetWaterMaterialInstance; no mesh instantiation;"
            + " one-shot metadata cannot prove pause elapsed or 400u exposure");
    }

    /// <summary>
    /// 当次 playerOne 在 CameraMarshaller.cameraRefs(≤4) × 每个 MainCamera.cameraTargets(≤4) 中唯一命中；
    /// `_camera` 必须 enabled/active 且与 owner scene 一致。只校验不重选，无 Camera.main、无全场扫描。
    /// </summary>
    private static bool TryResolveUniqueP1Camera(Kingdom kingdom, int scene,
        out MainCamera mainCamera, out Camera camera, out string detail)
    {
        mainCamera = null;
        camera = null;
        detail = null;
        Player playerOne = kingdom.playerOne;
        if (playerOne == null || !(bool)playerOne || playerOne.gameObject == null)
        { detail = "kingdom.playerOne unavailable"; return false; }
        GameObject target = playerOne.gameObject;
        CameraMarshaller marshaller = CameraMarshaller.Inst;
        if (marshaller == null) { detail = "CameraMarshaller.Inst unavailable"; return false; }
        var refs = marshaller.cameraRefs;
        if (refs == null || refs.Length < 1 || refs.Length > MaxCameraRefs)
        {
            detail = "cameraRefs " + (refs == null ? "null" : "count=" + refs.Length)
                + " (expected 1.." + MaxCameraRefs + ")";
            return false;
        }
        int hits = 0;
        string skipped = "";
        for (int i = 0; i < refs.Length; i++)
        {
            MainCamera candidate = refs[i];
            if (candidate == null || !(bool)candidate) { skipped += " refs[" + i + "]=null/destroyed;"; continue; }
            var targets = candidate.cameraTargets;
            if (targets == null) { skipped += " refs[" + i + "].targets=null;"; continue; }
            if (targets.Length > MaxCameraTargets)
            {
                detail = "cameraTargets over cap at refs[" + i + "] count=" + targets.Length;
                return false;
            }
            bool hit = false;
            for (int t = 0; t < targets.Length; t++)
            {
                GameObject go = targets[t];
                if (go != null && go.Pointer == target.Pointer) { hit = true; break; }
            }
            if (!hit) { skipped += " refs[" + i + "].targets=" + targets.Length + " no P1;"; continue; }
            hits++;
            mainCamera = candidate;
        }
        if (hits != 1 || mainCamera == null)
        { detail = "P1 camera not unique (hits=" + hits + ";" + skipped + ")"; return false; }
        Camera cam = mainCamera._camera;
        if (cam == null || !(bool)cam) { detail = "MainCamera._camera null/destroyed"; return false; }
        if (!cam.enabled || cam.gameObject == null || !cam.gameObject.activeInHierarchy)
        { detail = "Camera not enabled/active"; return false; }
        if (cam.gameObject.scene.handle != scene)
        { detail = "Camera scene differs from owner scene"; return false; }
        camera = cam;
        detail = "mainCamera=" + Ptr(mainCamera.Pointer) + " camId=" + mainCamera.cameraId
            + " camera=" + Ptr(cam.Pointer) + " cameraScene=" + cam.gameObject.scene.handle
            + " refs=" + refs.Length + " targets<=4 hits=1 (no Camera.main, no scan, no re-pick)";
        return true;
    }

    private static string DescribeCamera(Camera camera)
    {
        Transform transform = camera.transform;
        return "ptr=" + Ptr(camera.Pointer) + " enabled=" + camera.enabled
            + " viewport(pixelRect=" + R(camera.pixelRect) + " pixel=" + camera.pixelWidth + "x" + camera.pixelHeight + ")"
            + " orthographic=" + camera.orthographic + " orthoSize=" + F(camera.orthographicSize)
            + " aspect=" + F(camera.aspect) + " near=" + F(camera.nearClipPlane) + " far=" + F(camera.farClipPlane)
            + " depth=" + F(camera.depth) + " cullingMask=" + camera.cullingMask
            + " position=" + V3(transform.position) + " rotation=" + Q(transform.rotation)
            + " worldToCamera=" + Mat(camera.worldToCameraMatrix)
            + " projection=" + Mat(camera.projectionMatrix);
    }

    private static string DescribeWaterPipeline()
    {
        global::Rendering settings = global::Rendering.Inst;
        if (settings == null)
            return "unknown: Rendering.Inst=null; selectedMaterial=Unknown; no getter call";
        int propertyId;
        try { propertyId = Water.SPReflectionProperties; }
        catch (Exception e)
        {
            return "Rendering.Inst=" + Ptr(settings.Pointer) + " unknown: Water.SPReflectionProperties unreadable ("
                + e.GetType().Name + "); selectedMaterial=Unknown; no getter call";
        }
        return "Rendering.Inst=" + Ptr(settings.Pointer)
            + " selectedMaterial=Unknown (no GetWaterMaterialInstance call; renderId not read)"
            + " waterMaterial=" + DescribeWaterMaterial(settings.waterMaterial, propertyId)
            + " waterMaterialInstance=" + DescribeWaterMaterial(settings.waterMaterialInstance, propertyId)
            + " waterMaterialInstance2=" + DescribeWaterMaterial(settings.waterMaterialInstance2, propertyId)
            + " renderReflections=" + settings.renderReflections
            + " SPReflectionProperties=" + propertyId;
    }

    /// <summary>只读材质字段：shader/queue/passes，以及 HasProperty(id) 为真时才 GetVector(id)；不触发 .material 实例化。</summary>
    private static string DescribeWaterMaterial(Material material, int propertyId)
    {
        if (material == null) return "null";
        string shaderName, queue, passes, reflection;
        try { Shader shader = material.shader; shaderName = shader == null ? "none" : SafeName(shader); }
        catch (Exception e) { shaderName = "unknown(" + e.GetType().Name + ")"; }
        try { queue = material.renderQueue.ToString(CultureInfo.InvariantCulture); }
        catch (Exception e) { queue = "unknown(" + e.GetType().Name + ")"; }
        try { passes = material.passCount.ToString(CultureInfo.InvariantCulture); }
        catch (Exception e) { passes = "unknown(" + e.GetType().Name + ")"; }
        try
        {
            bool has = material.HasProperty(propertyId);
            reflection = has ? "has=true,value=" + V4(material.GetVector(propertyId)) : "has=false";
        }
        catch (Exception e) { reflection = "unknown(" + e.GetType().Name + ")"; }
        return "ptr=" + Ptr(material.Pointer) + " shader=" + shaderName + " queue=" + queue
            + " passes=" + passes + " SPReflectionProperties{" + reflection + "}";
    }

    private static string DescribeRenderTexture(RenderTexture texture)
    {
        if (texture == null || !(bool)texture) return "none";
        string size;
        try { size = texture.width + "x" + texture.height; }
        catch (Exception e) { size = "unknown(" + e.GetType().Name + ")"; }
        return "ptr=" + Ptr(texture.Pointer) + " size=" + size;
    }

    private static string PtrOf(UnityEngine.Object value)
    {
        try { return value == null ? "none" : Ptr(value.Pointer); }
        catch (Exception e) { return "unknown(" + e.GetType().Name + ")"; }
    }

    private static string F(float value)
        => float.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "unknown(nonfinite)";

    private static string R(Rect rect)
        => "(" + F(rect.x) + "," + F(rect.y) + "," + F(rect.width) + "," + F(rect.height) + ")";

    private static string Q(Quaternion q)
        => "(" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + ")";

    private static string V2(Vector2 v) => "(" + F(v.x) + "," + F(v.y) + ")";

    private static string V4(Vector4 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + ")";
#endif

    private static string DescribeSource8Boundary(GameObject value)
    {
        bool clrNull = ReferenceEquals(value, null);
        bool alive = !clrNull && (bool)value;
        if (!alive) return "goClrNull=" + clrNull + ",goUnityAlive=false,x=unknown";
        Transform transform = value.transform;
        bool transformNull = ReferenceEquals(transform, null);
        bool transformAlive = !transformNull && (bool)transform;
        string prefix = "goClrNull=false,goUnityAlive=true,goPtr=" + Ptr(value.Pointer);
        if (!transformAlive) return prefix + ",transformClrNull=" + transformNull
            + ",transformUnityAlive=false,x=unknown";
        return prefix + ",transformClrNull=false,transformUnityAlive=true,transformPtr=" + Ptr(transform.Pointer)
            + ",x=" + transform.position.x.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void Source8CameraLog(string message)
    {
        try { PalaceCourtVisuals.Log("[PalaceCameraSource8] " + message); }
        catch (Exception) { } // Diagnostic logger failure cannot change BODY's existing failure/early-return semantics.
    }

    /// <summary>
    /// Root 在既有 ScaleRegistryHolder.LateUpdate 内、GreekScaleScope.MaintainRegisteredY() 正常返回后调用。
    /// 未 Arm / 已关闭时零动作。冻结身份（含 agent/gate/campaign）任一漂移即取消；不漂移则唯一一次采样后永久关闭。
    /// </summary>
    internal static void ObserveAfterRegisteredY()
    {
        if (_closed || !_armed) return;
        try
        {
            if (!FrozenEpochCurrent(out string drift))
            {
                Close("cancelled before sample (" + drift + "); no re-arm, no re-scan");
                return;
            }
            Managers managers = Managers.Inst;
            string worker = SampleWorker(managers.kingdom, _epoch.Scene);
            string peasant = SamplePeasant(managers.kingdom, _epoch.Scene);
            Log(worker);
            Log(peasant);
            _closed = true;
            Log("sample complete (one-shot, closed) capturePhase=afterScaleRegistryLate scene=" + _epoch.Scene
                + " t=" + Time.unscaledTime.ToString("F2") + ": bodyPixelTop=Unknown bodyPixelBottom=Unknown"
                + " (metadata cannot locate the body; requires the stance image with body top/bottom marked;"
                + " renderer bounds/alpha are NOT body height and were not read);"
                + " stance unconfirmed -> this sample does NOT freeze an adult H and does not prove trustworthy stance;"
                + " registered target is the read-only GreekScaleScope.TryGet request;"
                + " localToWorldMatrix+rect/pivot/PPU+flip are the inputs for that later annotation;"
                + " no writes, no retry, no further sampling in this process");
        }
        catch (Exception e)
        {
            Close("sample fault (read-only; closed, no retry, no further scan): " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>
    /// 冻结身份仍为当前身份（取样前复核，不重读 trip 文件）：Enabled、离线 + authority、Greek + 非挑战、
    /// exact owner 指针/scene/state/非 saving、global.GetCurrentCampaign() 的 exact frozen Campaign、
    /// 同 typed agent Pointer 且 folder 仍为 private save-root、land 仍为 source8。
    /// </summary>
    private static bool FrozenEpochCurrent(out string drift)
    {
        drift = null;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        if (!PalaceScrollNativeBinding.Enabled || !DiagnosticBindingCaptured())
        { drift = "diagnostic gate off (scroll/binding)"; return false; }
#else
        if (!Enabled) { drift = "private gate off"; return false; }
#endif
        if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) { drift = "online/authority lost"; return false; }
        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.kingdom == null
            || managers.level == null || managers.world == null || managers.world.gameLayer == null)
        { drift = "managers unavailable"; return false; }
        Game game = managers.game;
        Kingdom kingdom = managers.kingdom;
        Level level = managers.level;
        Transform layer = managers.world.gameLayer;
        if (game.Pointer != _epoch.Game || kingdom.Pointer != _epoch.Kingdom
            || level.Pointer != _epoch.Level || layer.Pointer != _epoch.Layer)
        { drift = "owner pointer drift"; return false; }
        if (game.state != Game.State.Playing) { drift = "state=" + game.state; return false; }
        if (IslandSaveData.isSavingGame) { drift = "saving"; return false; }
        if (kingdom.gameObject == null || level.gameObject == null || layer.gameObject == null)
        { drift = "owner object unavailable"; return false; }
        if (kingdom.gameObject.scene.handle != _epoch.Scene
            || level.gameObject.scene.handle != _epoch.Scene
            || layer.gameObject.scene.handle != _epoch.Scene)
        { drift = "scene drift"; return false; }
        GlobalSaveData global = GlobalSaveData.loaded;
        CampaignSaveData campaign = CampaignSaveData.current;
        if (global == null || campaign == null || global.Pointer != _epoch.Global || campaign.Pointer != _epoch.Campaign)
        { drift = "campaign drift"; return false; }
        if (global.InChallenge) { drift = "challenge"; return false; }
        BiomeHolder biome = BiomeHolder.Inst;
        if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) { drift = "biome"; return false; }
        CampaignSaveData owner = global.GetCurrentCampaign();
        if (owner == null || owner.Pointer != _epoch.Campaign)
        { drift = "GetCurrentCampaign replaced the frozen campaign"; return false; }
        Filer.DotNetAgent currentAgent = CurrentAgent(out IntPtr agentPtr, out string agentDetail);
        if (currentAgent == null || agentPtr != _epoch.Agent)
        { drift = "agent drift (" + (currentAgent == null ? agentDetail : "agent replaced") + ")"; return false; }
        if (game.currentLand != SourceLandIndex || campaign.CurrentLand != SourceLandIndex)
        { drift = "land drift (game=" + game.currentLand + "/campaign=" + campaign.CurrentLand + ")"; return false; }
        return true;
    }

    /// <summary>
    /// 实际 private agent（typed Filer.DotNetAgent）：调用方指针必须与 frozen 一致，且 folder 精确等于 private
    /// save-root（环境变量配置不构成证明）。缺失/类型不符/读取不确定 → null + detail（记录 unknown，不放宽）；
    /// 不创建、不注册、不写任何东西，也不改变等待状态。
    /// </summary>
    private static Filer.DotNetAgent CurrentAgent(out IntPtr pointer, out string detail)
    {
        pointer = IntPtr.Zero;
        detail = null;
        try
        {
            var raw = Filer.agent as Il2CppObjectBase;
            if (raw == null || raw.Pointer == IntPtr.Zero) { detail = "agent=none"; return null; }
            Filer.DotNetAgent agent = raw.TryCast<Filer.DotNetAgent>();
            if (agent == null || agent.Pointer != raw.Pointer) { detail = "agent-type-mismatch"; return null; }
            string folder = agent.folder;
            if (!PalaceSaveBindingSource.AgentMatchesCurrent(agent, out _))
            { detail = "folder=" + (folder ?? "(null)"); return null; }
            pointer = agent.Pointer;
            return agent;
        }
        catch (Exception e)
        {
            detail = "agent-read-fault:" + e.GetType().Name;
            return null;
        }
    }

    /// <summary>优先 Kingdom.Workers：Count 先判上限，再一次 CopyTo 快照；只取首个合格 typed Worker。</summary>
    private static string SampleWorker(Kingdom kingdom, int scene)
    {
        var roster = kingdom.Workers;
        if (roster == null) return "worker: unknown (Kingdom.Workers=null)";
        int count = roster.Count;
        if (count <= 0) return "worker: unknown (roster empty count=" + count + ")";
        if (count > MaxRoster)
            return "worker: unknown (roster count=" + count + " over cap " + MaxRoster + "; slots not enumerated)";
        var slots = new Il2CppReferenceArray<Worker>(count);
        roster.CopyTo(slots, 0);
        string firstReject = null;
        for (int i = 0; i < slots.Length; i++)
        {
            Worker worker = slots[i];
            if (!QualifiedWorker(worker, scene, out string reject))
            {
                if (firstReject == null) firstReject = reject;
                continue;
            }
            bool norselands = worker.gameObject.name.Contains("norselands");
            return DescribeActor("worker", worker.Pointer, worker.gameObject, worker.transform,
                worker._spriteRenderer, worker._character, worker.GetComponent<Mover>(), worker._animator,
                norselands, false);
        }
        return "worker: unknown (no qualified typed actor of count=" + count
            + "; firstReject=" + (firstReject ?? "none") + "; no actor switching)";
    }

    /// <summary>Peasant 只从当前 Kingdom._characters 找 typed Peasant：Count 先判上限，只取首个合格者。</summary>
    private static string SamplePeasant(Kingdom kingdom, int scene)
    {
        var characters = kingdom._characters;
        if (characters == null) return "peasant: unknown (Kingdom._characters=null)";
        int count = characters.Count;
        if (count <= 0) return "peasant: unknown (roster empty count=" + count + ")";
        if (count > MaxRoster)
            return "peasant: unknown (roster count=" + count + " over cap " + MaxRoster + "; slots not enumerated)";
        string firstReject = null;
        int examined = 0;
        foreach (Character character in characters)
        {
            if (++examined > MaxRoster) break; // 双保险：Count 已判上限
            if (character == null || character.Pointer == IntPtr.Zero || character.gameObject == null) continue;
            // Peasant : MonoBehaviour 与 Character 是同一 actor GO 上的兄弟组件（typed 解编），不是 Character 子类，
            // 所以用 roster Character 的 GO 取 typed Peasant 组件（找不到即 null），不做 TryCast 猜类型。
            Peasant peasant = character.GetComponent<Peasant>();
            if (peasant == null) continue;
            if (!QualifiedPeasant(peasant, character, scene, out string reject))
            {
                if (firstReject == null) firstReject = reject;
                continue;
            }
            bool warriorPeasant = peasant.GetComponent<WarriorPeasant>() != null;
            return DescribeActor("peasant", peasant.Pointer, peasant.gameObject, peasant.transform,
                peasant._spriteRenderer, peasant._character, peasant.GetComponent<Mover>(), peasant._animator,
                false, warriorPeasant);
        }
        return "peasant: unknown (no qualified typed Peasant of count=" + count
            + "; firstReject=" + (firstReject ?? "none") + "; no actor switching)";
    }

    private static bool QualifiedWorker(Worker worker, int scene, out string reject)
    {
        if (worker == null || !(bool)worker || worker.gameObject == null || worker.transform == null)
        { reject = "null/destroyed"; return false; }
        return QualifiedActor(worker.gameObject, worker.transform, worker._spriteRenderer, worker._character,
            worker.GetComponent<Mover>(), worker._animator, scene, out reject);
    }

    private static bool QualifiedPeasant(Peasant peasant, Character rosterCharacter, int scene, out string reject)
    {
        if (peasant == null || !(bool)peasant || peasant.gameObject == null || peasant.transform == null)
        { reject = "null/destroyed"; return false; }
        // roster Character 必须是该 Peasant 的 character（exact Pointer；同一次 roster 枚举，不做第二次扫描）。
        if (rosterCharacter == null || peasant._character == null
            || peasant._character.Pointer != rosterCharacter.Pointer)
        { reject = "rosterCharacterMismatch"; return false; }
        return QualifiedActor(peasant.gameObject, peasant.transform, peasant._spriteRenderer, peasant._character,
            peasant.GetComponent<Mover>(), peasant._animator, scene, out reject);
    }

    /// <summary>
    /// 合格条件（全 typed、只读）：GO 与 Character GO 在本 scene 活跃且 exact 同一 actor GO；Mover 存在且 enabled；
    /// 存活（Character.inert == false，Damageable 存在时 !isDead）；_spriteRenderer 与 _character.spriteFX.Renderer
    /// 指针一致、parent 属于该 actor、scene 相同；Animator（若存在）的现有 transform 属于同 actor 同 scene（只核
    /// 归属，不扫描子树）；未登船（registry 同款 Embarkee.IsEmbarked/IsTargetingEmbarkable）、未抓取
    /// （Character.grabbed）、且是 GreekScaleScope 已登记角色。拒绝不是故障，只记录首个拒绝原因，不换 actor。
    /// </summary>
    private static bool QualifiedActor(GameObject go, Transform root, SpriteRenderer renderer, Character character,
        Mover mover, Animator animator, int scene, out string reject)
    {
        reject = null;
        if (go == null) { reject = "noGameObject"; return false; }
        if (root == null) { reject = "noRoot"; return false; }
        if (!go.activeInHierarchy) { reject = "inactive"; return false; }
        if (go.scene.handle != scene) { reject = "scene"; return false; }
        if (renderer == null || !(bool)renderer) { reject = "noSpriteRenderer"; return false; }
        if (!renderer.enabled || renderer.gameObject == null || !renderer.gameObject.activeInHierarchy)
        { reject = "rendererInactive"; return false; }
        if (renderer.gameObject.scene.handle != scene) { reject = "rendererScene"; return false; }
        if (character == null || !(bool)character || character.gameObject == null)
        { reject = "noCharacter"; return false; }
        if (character.gameObject.scene.handle != scene) { reject = "characterScene"; return false; }
        if (character.gameObject.Pointer != go.Pointer) { reject = "characterGoMismatch"; return false; }
        if (character.inert) { reject = "inert"; return false; }
        Damageable damageable = character._damageable;
        if (damageable != null && damageable.isDead) { reject = "dead"; return false; }
        if (animator != null && (bool)animator)
        {
            if (animator.transform == null || !animator.transform.IsChildOf(root))
            { reject = "animatorNotChildOfActor"; return false; }
            if (animator.gameObject == null || animator.gameObject.scene.handle != scene)
            { reject = "animatorScene"; return false; }
        }
        SpriteRendererFX fx = character.spriteFX;
        SpriteRenderer main = fx != null ? fx.Renderer : null;
        if (main == null || !(bool)main || main.Pointer != renderer.Pointer) { reject = "rendererMismatch"; return false; }
        if (main.transform == null || !main.transform.IsChildOf(root)) { reject = "notChildOfActor"; return false; }
        if (main.gameObject == null || main.gameObject.scene.handle != scene) { reject = "mainRendererScene"; return false; }
        if (mover == null || !(bool)mover || !mover.enabled) { reject = "noActiveMover"; return false; }
        Embarkee embarkee = mover.GetComponent<Embarkee>();
        if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable))
        { reject = "embarked"; return false; }
        if (character.grabbed) { reject = "grabbed"; return false; }
        if (!GreekScaleScope.TryGet(mover, out _)) { reject = "notRegistered"; return false; }
        return true;
    }

    /// <summary>一行只读元数据：身份/scene/root localScale/renderer 矩阵与 flip/sprite rect·pivot·PPU·texture/动画。
    /// registered 来自 GreekScaleScope.TryGet（只读请求值）；stance 不由元数据判定，BODY 像素头脚固定 Unknown。</summary>
    private static string DescribeActor(string kind, IntPtr actorPtr, GameObject go, Transform root,
        SpriteRenderer renderer, Character character, Mover mover, Animator animator,
        bool norselands, bool warriorPeasant)
    {
        bool worker = kind == "worker";
        bool registered = GreekScaleScope.TryGet(mover, out float targetY);
        SpriteRendererFX fx = character.spriteFX;
        return kind + ": actorPtr=" + Ptr(actorPtr) + " go=" + SafeName(go) + " goPtr=" + Ptr(go.Pointer)
            + " scene=" + go.scene.handle + " rootPtr=" + Ptr(root.Pointer)
            + " rootLocalScale=" + V3(root.localScale)
            + " renderer=" + Ptr(renderer.Pointer) + " go=" + SafeName(renderer.gameObject)
            + " enabled=" + renderer.enabled + " flipX=" + renderer.flipX + " flipY=" + renderer.flipY
            + " localToWorld=" + Mat(renderer.localToWorldMatrix)
            + " sprite[" + SpriteText(renderer.sprite) + "]"
            + " character=" + Ptr(character.Pointer) + " characterGo=" + Ptr(character.gameObject.Pointer)
            + " spriteFX=" + (fx != null ? Ptr(fx.Pointer) : "none")
            + " checks[inert=" + character.inert + " grabbed=" + character.grabbed
            + " isStationary=" + character.isStationary
            + " damageable=" + DamageableState(character) + " embarked=False(qualified)]"
            + " registered=" + (registered ? targetY.ToString("R") : "none")
            + (worker ? " norselandsName=" + norselands : " warriorPeasant=" + warriorPeasant)
            + " " + AnimatorText(animator, worker ? Worker.ASStand : (int?)null)
            + " stance=unconfirmed(metadata cannot prove stance; ASStand/state mapping unverified;"
            + " requires stance image with body top/bottom)"
            + " bodyPixelTop=Unknown bodyPixelBottom=Unknown";
    }

    /// <summary>Animator 只读：enabled/speed/当前 state short·full hash·normalizedTime·length·loop/过渡与 next state。
    /// ASStand（仅 Worker 有，signed int，存在性用 nullable 表达，负值同样保留）一并记录，但 hash 相等只作候选
    /// 信号，不当站姿证明。</summary>
    private static string AnimatorText(Animator animator, int? asStand)
    {
        if (animator == null || !(bool)animator) return "animator=none";
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        bool inTransition = animator.IsInTransition(0);
        string transition = "";
        if (inTransition)
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            transition = " next(short=" + next.shortNameHash + " full=" + next.fullPathHash
                + " norm=" + next.normalizedTime.ToString("F3") + ")";
        }
        string stand = asStand.HasValue
            ? " asStand=" + asStand.Value + " asStandHashMatch="
                + (state.shortNameHash == asStand.Value || state.fullPathHash == asStand.Value)
            : "";
        return "animator[ptr=" + Ptr(animator.Pointer) + " enabled=" + animator.enabled
            + " speed=" + animator.speed.ToString("F3")
            + " state(short=" + state.shortNameHash + " full=" + state.fullPathHash
            + " norm=" + state.normalizedTime.ToString("F3") + " len=" + state.length.ToString("F3")
            + " loop=" + state.loop + ")"
            + " inTransition=" + inTransition + transition + stand + "]";
    }

    private static string SpriteText(Sprite sprite)
    {
        if (sprite == null || !(bool)sprite) return "none";
        Rect rect = sprite.rect;
        Vector2 pivot = sprite.pivot;
        Texture2D texture = sprite.texture;
        string textureText = texture == null || !(bool)texture
            ? "none"
            : "ptr=" + Ptr(texture.Pointer) + " name=" + SafeName(texture)
                + " " + texture.width + "x" + texture.height + " readable=" + texture.isReadable;
        return "ptr=" + Ptr(sprite.Pointer) + " name=" + SafeName(sprite)
            + " rect=(" + rect.x.ToString("F1") + "," + rect.y.ToString("F1")
            + "," + rect.width.ToString("F1") + "," + rect.height.ToString("F1") + ")"
            + " pivot=(" + pivot.x.ToString("F2") + "," + pivot.y.ToString("F2") + ")"
            + " ppu=" + sprite.pixelsPerUnit.ToString("R")
            + " texture[" + textureText + "]";
    }

    /// <summary>typed 存活证据：Damageable 缺失显式记 none，不把缺失猜成 alive/dead。</summary>
    private static string DamageableState(Character character)
    {
        Damageable damageable = character._damageable;
        if (damageable == null || !(bool)damageable) return "none";
        return damageable.isDead ? "dead" : "alive";
    }

    /// <summary>终点/漂移/故障：一次日志并永久关闭（不重建、不重试；已关闭后不再输出）。</summary>
    private static void Close(string reason)
    {
        if (_closed) return;
        _closed = true;
        Log("closed: " + reason);
    }

    /// <summary>等待路径诊断：每个给定位（gate/agent）最多一次，不刷 log，也不改变等待或受理状态。</summary>
    private static void WaitOnce(ref bool warned, string reason)
    {
        if (warned) return;
        warned = true;
        Log("wait: " + reason + " (single report; still waiting for the registered source8 Playing)");
    }

    private static void Log(string message) => PalaceCourtVisuals.Log("[AdultBody] " + message);

    private static string Ptr(IntPtr pointer) => pointer == IntPtr.Zero ? "none" : pointer.ToInt64().ToString();

    private static string V3(Vector3 v)
        => "(" + v.x.ToString("F4") + "," + v.y.ToString("F4") + "," + v.z.ToString("F4") + ")";

    private static string Mat(Matrix4x4 m)
        => "(" + m.m00.ToString("F5") + "," + m.m01.ToString("F5") + "," + m.m02.ToString("F5") + "," + m.m03.ToString("F5")
            + "," + m.m10.ToString("F5") + "," + m.m11.ToString("F5") + "," + m.m12.ToString("F5") + "," + m.m13.ToString("F5")
            + "," + m.m20.ToString("F5") + "," + m.m21.ToString("F5") + "," + m.m22.ToString("F5") + "," + m.m23.ToString("F5")
            + "," + m.m30.ToString("F5") + "," + m.m31.ToString("F5") + "," + m.m32.ToString("F5") + "," + m.m33.ToString("F5") + ")";

    private static string SafeName(UnityEngine.Object o)
    {
        try { return o == null ? "none" : o.name; }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }
}
