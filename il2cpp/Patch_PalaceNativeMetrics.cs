// PalaceNativeMetrics（私有只读观测，source-only）。不是修复：不绕门、不校正、不改任何游戏状态。
// (a) Setup 读数：Kingdom.SetupBGComponent 的前两次调用——state / instance 与 currentKingdom 是否同一 /
//     game+campaign land / Level 与 gameLayer 指针、scene；仅当 currentKingdom exact 一致时再读
//     _bgWallParent 与 bgWallSegments（不一致即 scope 未证，不猜）。
// (b) Playing 读数：实际 playerOne 相机（CameraMarshaller.cameraRefs 与 MainCamera.cameraTargets 唯一匹配；
//     无 Camera.main、无 fallback 枚举）与原生背景层（Level.parallaxLayers 单一入口，<=64）随相机 x 的
//     两次以上位置对照；首次 Playing 与同一相机 x 相对上一条变化 >=2 world units 时采样，最多 16 条。
// 禁用（PALACE_NATIVE_METRICS_PRIVATE!=1 或 PalaceScrollNativeBinding.Enabled!=true）时零 hook、零效果。
// 只读边界：不写 native/存档/玩家/相机/renderer/transform/ratio/flag，不隐藏/停用原生 root，不做全场扫描、
//   不新增墙 tag 扫描、不做副作用重试；日志只经 PalaceCourtVisuals.Log("[NativeMetrics] ...")。
// 结构读数上限：snapshot<=16、parallaxLayers<=64、cameraRefs<=4、cameraTargets<=4、bgWallSegments<=512；
//   超过上限即一次错误退出，不截断后冒称完整；未知/失败显式记录（missing/unreadable/over-cap），不吞成 0/默认。
// Root 接线：本文件只提供 InstallOnce(Harmony) 与 Observe(Game)；不新增 Game.Update hook、不新增 MonoBehaviour。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceNativeMetrics
{
    private const string EnableEnv = "PALACE_NATIVE_METRICS_PRIVATE";
    private const int MaxSnapshots = 16;
    private const int MaxLayers = 64;
    private const int MaxCameraRefs = 4;
    private const int MaxCameraTargets = 4;
    private const int MaxWallSegments = 512;
    private const float MinCameraXDelta = 2f;

    // 安装与 Setup 读数（前两次合格调用；_setupSeen 记录全部进入次数，用于顺序证据）
    private static bool _installed;
    private static int _setupSeen;
    private static int _setupCalls;

    // 单一 owner 观测状态：绑定后漂移或失败即停止，不重建、不重新挑相机
    private static bool _stopped;
    private static bool _bound;
    private static bool _warned;
    private static bool _layersCaptured;
    private static bool _layersEntryNull;
    private static int _snapshots;
    private static int _layerCount;
    private static float _prevCameraX;
    private static Parallax2D[] _layers;
    private static MainCamera _mainCamera;
    private static Camera _camera;
    private static IntPtr _ownerGame;
    private static IntPtr _ownerKingdom;
    private static IntPtr _ownerLevel;
    private static IntPtr _ownerLayer;
    private static IntPtr _ownerMainCamera;
    private static IntPtr _ownerCamera;
    private static int _ownerLayerScene;

    internal static bool Enabled
        => PalaceRuntimeProfile.Capability(EnableEnv) && PalaceScrollNativeBinding.Enabled;

    /// <summary>
    /// 仅当 PALACE_NATIVE_METRICS_PRIVATE=1 且 PalaceScrollNativeBinding.Enabled 时安装唯一 hook：
    /// Kingdom.SetupBGComponent 的 void Prefix（原方法照常执行，前缀不跳过、不写 native 字段）。
    /// 目标先 resolve（实例、void、唯一）；resolve 或 patch 失败只记一次并退出，不重试、不"捕获后继续注册"。
    /// </summary>
    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed) return;
        if (!Enabled) return;
        _installed = true; // 单次尝试：失败也不在后续 InstallOnce 调用中重试
        try
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            MethodInfo target = ResolveSetupTarget();
            if (target == null)
            {
                Log("ERROR Kingdom.SetupBGComponent not uniquely resolvable as instance void; hook NOT installed, no retry");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PalaceNativeMetrics), nameof(SetupPrefix)));
            Log("installed: void prefix on " + target.DeclaringType.Name + "." + target.Name
                + " -> " + target.ReturnType.Name + "; read-only, original always runs, no native writes");
        }
        catch (Exception e)
        {
            Log("ERROR metrics install failed (single attempt, no retry): " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>Kingdom 上恰好一个名为 SetupBGComponent 的 void 实例方法才返回它，否则返回 null（fail-closed）。</summary>
    private static MethodInfo ResolveSetupTarget()
    {
        var candidates = new List<MethodInfo>();
        foreach (MethodInfo method in typeof(Kingdom).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.Name == "SetupBGComponent") candidates.Add(method);
        }
        MethodInfo target = null;
        foreach (MethodInfo method in candidates)
        {
            if (method.IsStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters) continue;
            if (target != null) return null; // 多个 void 实例重载：无法唯一确定
            target = method;
        }
        return target;
    }

    /// <summary>
    /// Setup 前缀（void：绝不跳过原方法）。只在完整私有门、离线、game/campaign land 同为 12 时读数，
    /// 记录前两次调用；currentKingdom 不一致时只输出 scope 未证，不读墙字段、不猜。
    /// </summary>
    private static void SetupPrefix(Kingdom __instance)
    {
        try
        {
            if (!Enabled) return;
            _setupSeen++;
            if (NetworkBigBoss.IsOnline) return;
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            Kingdom current = managers != null ? managers.kingdom : null;
            if (game == null || game.currentLand != PalaceLand12Runtime.LandIndex) return;
            CampaignSaveData campaign = CampaignSaveData.current;
            if (campaign == null || campaign.CurrentLand != PalaceLand12Runtime.LandIndex) return;
            if (_setupCalls > 2) return;
            if (_setupCalls == 2)
            {
                _setupCalls = 3;
                Log("setup: two land12 readings recorded (allCalls=" + _setupSeen + "); further Setup readings suppressed");
                return;
            }
            _setupCalls++;

            Level level = managers.level;
            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            bool same = __instance != null && current != null && __instance.Pointer == current.Pointer;
            string line = "setup#" + _setupCalls + " allCalls=" + _setupSeen + " t=" + Time.unscaledTime.ToString("F2")
                + " instance=" + Ptr(__instance) + " currentKingdom=" + Ptr(current) + " same=" + same
                + " state=" + game.state + " gameLand=" + game.currentLand + " campaignLand=" + campaign.CurrentLand
                + " level=" + Ptr(level) + " levelScene=" + SceneHandle(level != null ? level.gameObject : null)
                + " gameLayer=" + Ptr(layer) + " layerScene=" + SceneHandle(layer != null ? layer.gameObject : null);
            if (!same)
            {
                Log(line + " wallScope=unproven(currentKingdom differs; _bgWallParent/bgWallSegments not read, no guess)");
                return;
            }
            Log(line + " wallParent=" + WallParent(__instance) + " wallSegments=" + WallSegments(__instance));
        }
        catch (Exception e)
        {
            Log("setup read fault (read-only; ignored): " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>_bgWallParent（只读）：pointer/name/active 或显式 unreadable。</summary>
    private static string WallParent(Kingdom kingdom)
    {
        try
        {
            var parent = kingdom._bgWallParent;
            if (parent == null) return "none";
            return "ptr=" + parent.Pointer.ToInt64() + " name=" + SafeName(parent) + " active=" + ActiveText(parent);
        }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }

    /// <summary>bgWallSegments（只读、有界）：count + null/destroyed/noTransform；超上限只报 count 并不枚举。</summary>
    private static string WallSegments(Kingdom kingdom)
    {
        try
        {
            var segments = kingdom.bgWallSegments;
            if (segments == null) return "null";
            int count = segments.Length;
            if (count > MaxWallSegments)
                return "count=" + count + " overCap(" + MaxWallSegments + "); slots not enumerated";
            int nulls = 0, destroyed = 0, noTransform = 0;
            for (int i = 0; i < count; i++)
            {
                var item = segments[i];
                if (item is null || item.Pointer == IntPtr.Zero) nulls++;
                else if (!(bool)item) destroyed++;
                else if (item.transform == null) noTransform++;
            }
            return "count=" + count + " nulls=" + nulls + " destroyed=" + destroyed + " noTransform=" + noTransform;
        }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }

    /// <summary>
    /// 每帧条件读数（由既有 PalaceCourtVisuals.GameplayFrame 调用；本类不新增 Update hook）。
    /// 门：私有 / 离线 / exact Managers.game==game / currentKingdom 非空 / campaign+game land 12 / 实际 Playing /
    /// 非 saving / Level 与 world.gameLayer 可用且同 scene。非 12 帧直接退出且无日志。
    /// owner（game/kingdom/level/gameLayer/camera）签名绑定；漂移即停止，不重挑相机、不重建。
    /// </summary>
    internal static void Observe(Game game)
    {
        if (_stopped || !Enabled) return;
        try
        {
            if (game == null) return;
            if (NetworkBigBoss.IsOnline) return;
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != game.Pointer) return;
            Kingdom kingdom = managers.kingdom;
            if (kingdom == null) return;
            if (game.currentLand != PalaceLand12Runtime.LandIndex) return;
            CampaignSaveData campaign = CampaignSaveData.current;
            if (campaign == null || campaign.CurrentLand != PalaceLand12Runtime.LandIndex) return;
            if (game.state != Game.State.Playing) return;
            if (IslandSaveData.isSavingGame) return;
            Level level = managers.level;
            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            if (level == null || layer == null)
            { WarnOnce("Level or World.gameLayer unavailable"); return; }
            if (level.gameObject == null || layer.gameObject == null
                || level.gameObject.scene.handle != layer.gameObject.scene.handle)
            { WarnOnce("Level and World.gameLayer scene differ"); return; }

            if (!_bound)
            {
                BindOwner(game, kingdom, level, layer);
                return;
            }

            if (managers.game.Pointer != _ownerGame) { Stop("owner drift: Game"); return; }
            if (kingdom.Pointer != _ownerKingdom) { Stop("owner drift: Kingdom"); return; }
            if (level.Pointer != _ownerLevel) { Stop("owner drift: Level"); return; }
            if (layer.Pointer != _ownerLayer) { Stop("owner drift: World.gameLayer"); return; }
            if (!CameraBound(out string cameraReason)) { Stop("owner drift: camera (" + cameraReason + ")"); return; }

            float x = _camera.transform.position.x;
            if (!float.IsFinite(x)) { Stop("non-finite camera x; no displacement sample"); return; }
            float delta = x - _prevCameraX;
            if (Math.Abs(delta) < MinCameraXDelta) return;
            float from = _prevCameraX;
            _prevCameraX = x;
            EmitSnapshot(game, kingdom, level, layer,
                "camera-x delta=" + delta.ToString("F2") + " (" + from.ToString("F2") + "->" + x.ToString("F2") + ")", false);
        }
        catch (Exception e)
        {
            Stop("observe fault (read-only): " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>
    /// 首次 Playing 绑定单一 owner：用 cameraRefs(<=4) + 每相机 cameraTargets(<=4) 唯一匹配 playerOne GO；
    /// _camera 须 enabled/active 且与 gameLayer 同 scene。无法唯一识别时一次日志并停止该 owner 观察（无 fallback）。
    /// </summary>
    private static void BindOwner(Game game, Kingdom kingdom, Level level, Transform layer)
    {
        Player playerOne = kingdom.playerOne;
        if (playerOne == null || !(bool)playerOne || playerOne.gameObject == null)
        { WarnOnce("kingdom.playerOne unavailable"); return; }
        GameObject target = playerOne.gameObject;

        CameraMarshaller marshaller = CameraMarshaller.Inst;
        if (marshaller == null) { CameraFail("CameraMarshaller.Inst unavailable"); return; }
        var refs = marshaller.cameraRefs;
        if (refs == null || refs.Length == 0 || refs.Length > MaxCameraRefs)
        {
            CameraFail("cameraRefs " + (refs == null ? "null" : "count=" + refs.Length)
                + " (expected 1.." + MaxCameraRefs + ")");
            return;
        }

        int hits = 0;
        MainCamera match = null;
        string skipped = "";
        for (int i = 0; i < refs.Length; i++)
        {
            MainCamera candidate = refs[i];
            if (candidate == null || !(bool)candidate) { skipped += " refs[" + i + "]=null/destroyed;"; continue; }
            var targets = candidate.cameraTargets;
            if (targets == null) { skipped += " refs[" + i + "].targets=null;"; continue; }
            if (targets.Length > MaxCameraTargets)
            {
                CameraFail("cameraTargets over cap at cameraRefs[" + i + "] count=" + targets.Length);
                return;
            }
            bool hit = false;
            for (int t = 0; t < targets.Length; t++)
            {
                GameObject go = targets[t];
                if (go != null && go.Pointer == target.Pointer) { hit = true; break; }
            }
            if (!hit) { skipped += " refs[" + i + "].targets=" + targets.Length + " no playerOne;"; continue; }
            hits++;
            match = candidate;
        }
        if (hits != 1 || match == null)
        {
            CameraFail("playerOne camera not unique (hits=" + hits + ";" + skipped + ")");
            return;
        }

        Camera cam = match._camera;
        if (cam == null || !(bool)cam) { CameraFail("MainCamera._camera null/destroyed"); return; }
        if (!cam.enabled || cam.gameObject == null || !cam.gameObject.activeInHierarchy)
        { CameraFail("_camera not enabled/active"); return; }
        if (cam.gameObject.scene.handle != layer.gameObject.scene.handle)
        { CameraFail("_camera scene differs from gameLayer scene"); return; }

        _bound = true;
        _mainCamera = match;
        _camera = cam;
        _ownerGame = game.Pointer;
        _ownerKingdom = kingdom.Pointer;
        _ownerLevel = level.Pointer;
        _ownerLayer = layer.Pointer;
        _ownerMainCamera = match.Pointer;
        _ownerCamera = cam.Pointer;
        _ownerLayerScene = layer.gameObject.scene.handle;
        _prevCameraX = cam.transform.position.x;
        if (!float.IsFinite(_prevCameraX)) { Stop("non-finite initial camera x"); return; }
        Log("owner bound: game=" + Ptr(game) + " kingdom=" + Ptr(kingdom) + " level=" + Ptr(level)
            + " gameLayer=" + Ptr(layer) + " mainCamera=" + Ptr(match) + " camId=" + match.cameraId
            + " _camera=" + Ptr(cam) + " (unique playerOne target match; read-only)");
        EmitSnapshot(game, kingdom, level, layer, "first-playing", true);
    }

    /// <summary>绑定相机仍为当前 owner：指针/启用/active/scene 不变，且 cameraRefs 中仍唯一出现（每帧条件读数）。</summary>
    private static bool CameraBound(out string reason)
    {
        reason = null;
        if (_mainCamera == null || !(bool)_mainCamera) { reason = "MainCamera destroyed"; return false; }
        if (_mainCamera.Pointer != _ownerMainCamera) { reason = "MainCamera replaced"; return false; }
        if (_camera == null || !(bool)_camera) { reason = "Camera destroyed"; return false; }
        if (_camera.Pointer != _ownerCamera) { reason = "Camera replaced"; return false; }
        Camera currentCamera = _mainCamera._camera;
        if (currentCamera == null || currentCamera.Pointer != _ownerCamera) { reason = "MainCamera camera association changed"; return false; }
        if (!_camera.enabled || _camera.gameObject == null || !_camera.gameObject.activeInHierarchy)
        { reason = "Camera disabled/inactive"; return false; }
        if (_camera.gameObject.scene.handle != _ownerLayerScene) { reason = "Camera scene changed"; return false; }
        CameraMarshaller marshaller = CameraMarshaller.Inst;
        if (marshaller == null) { reason = "CameraMarshaller.Inst unavailable"; return false; }
        var refs = marshaller.cameraRefs;
        if (refs == null || refs.Length == 0 || refs.Length > MaxCameraRefs)
        { reason = "cameraRefs changed (" + (refs == null ? "null" : "count=" + refs.Length) + ")"; return false; }
        Managers managers = Managers.Inst;
        Player player = managers != null && managers.kingdom != null ? managers.kingdom.playerOne : null;
        if (player == null || player.gameObject == null) { reason = "current playerOne unavailable"; return false; }
        IntPtr targetPointer = player.gameObject.Pointer;
        int referenceHits = 0, targetHits = 0;
        for (int i = 0; i < refs.Length; i++)
        {
            MainCamera candidate = refs[i];
            if (candidate == null || !(bool)candidate) continue;
            if (candidate.Pointer == _ownerMainCamera) referenceHits++;
            var targets = candidate.cameraTargets;
            if (targets == null) continue;
            if (targets.Length > MaxCameraTargets) { reason = "current target array over cap"; return false; }
            bool targetsPlayer = false;
            for (int j = 0; j < targets.Length; j++)
                if (targets[j] != null && targets[j].Pointer == targetPointer) { targetsPlayer = true; break; }
            if (!targetsPlayer) continue;
            targetHits++;
            if (candidate.Pointer != _ownerMainCamera) { reason = "playerOne target changed to another MainCamera"; return false; }
        }
        if (referenceHits != 1 || targetHits != 1) { reason = "bound MainCamera/P1 relationship no longer unique"; return false; }
        return true;
    }

    /// <summary>
    /// 一条 snapshot：首次为全量（含 parallaxLayers 逐项与 GetLayer 候选），后续只补层 position 对照。
    /// 首次先做有界 layer 采集：超上限即一次错误退出（不截断、不伪称完整），该次不计入 snapshot。
    /// </summary>
    private static void EmitSnapshot(Game game, Kingdom kingdom, Level level, Transform layer, string trigger, bool first)
    {
        if (first && !_layersCaptured && !CaptureLayers(level)) return;
        _snapshots++;
        int n = _snapshots;
        MainCamera mc = _mainCamera;
        Camera cam = _camera;
        Vector3 camPos = cam.transform.position;
        Quaternion camRot = cam.transform.rotation;
        Vector3 mcPos = mc.transform.position;
        Quaternion mcRot = mc.transform.rotation;
        string bounds;
        try
        {
            Bounds b = mc.GetWorldBounds();
            bounds = "center=" + V3(b.center) + " size=" + V3(b.size);
        }
        catch (Exception e) { bounds = "unreadable(" + e.GetType().Name + ")"; }
        string playerText;
        try
        {
            Player playerOne = kingdom.playerOne;
            playerText = playerOne == null
                ? "none"
                : "ptr=" + Ptr(playerOne) + " go=" + SafeName(playerOne.gameObject)
                    + " pos=" + (playerOne.transform != null ? V3(playerOne.transform.position) : "n/a");
        }
        catch (Exception e) { playerText = "unreadable(" + e.GetType().Name + ")"; }

        Log("snap#" + n + " trigger=" + trigger + " t=" + Time.unscaledTime.ToString("F2")
            + " camMain=" + Ptr(mc) + " mainGo=" + SafeName(mc.gameObject)
            + " mainScene=" + SceneHandle(mc.gameObject) + " mainPos=" + V3(mcPos) + " mainRot=" + Q(mcRot)
            + " camId=" + mc.cameraId
            + " camObj=" + Ptr(cam) + " camGo=" + SafeName(cam.gameObject)
            + " camScene=" + SceneHandle(cam.gameObject) + " camPos=" + V3(camPos) + " camRot=" + Q(camRot)
            + " ortho=" + cam.orthographic + " aspect=" + cam.aspect.ToString("F4")
            + " orthoSize=" + cam.orthographicSize.ToString("F3")
            + " pixelRect=" + R(cam.pixelRect)
            + " worldBounds[" + bounds + "]"
            + " playerOne[" + playerText + "]"
            + " state=" + game.state + " game=" + Ptr(game) + " kingdom=" + Ptr(kingdom)
            + " nativeHasBorderLoaded=" + kingdom.HasBorderLoaded
            + " level=" + Ptr(level) + " levelScene=" + SceneHandle(level.gameObject)
            + " gameLayer=" + Ptr(layer) + " layerScene=" + SceneHandle(layer.gameObject));

        if (first)
        {
            Log("snap#" + n + " levelsrc=Level.parallaxLayers "
                + (_layersEntryNull
                    ? "entry=null (no layer reads, no fabricated defaults)"
                    : "count=" + _layerCount + " (single entry; cap " + MaxLayers + ")")
                + "; GetParallaxingLayers not consulted");
            for (int i = 0; i < _layerCount; i++)
                Log("snap#" + n + " layer[" + i + "] " + LayerDetail(_layers[i]));
            foreach (ContentLayers key in new[]
            {
                ContentLayers.BackgroundFarLayer, ContentLayers.BackgroundMidLayer, ContentLayers.BackgroundNearLayer
            })
                Log("snap#" + n + " getLayer[" + key + "]=" + LayerSlot(level, key));
        }
        else
        {
            for (int i = 0; i < _layerCount; i++)
                Log("snap#" + n + " layerPos[" + i + "] " + LayerPos(_layers[i]));
        }
        if (_snapshots >= MaxSnapshots)
            Stop("snapshot budget " + MaxSnapshots + " reached; observation complete");
    }

    /// <summary>首次采集 Level.parallaxLayers（只读快照，<=64）：超上限一次错误退出；null 入口显式记录。</summary>
    private static bool CaptureLayers(Level level)
    {
        var list = level.parallaxLayers;
        if (list == null)
        {
            _layersCaptured = true;
            _layersEntryNull = true;
            _layerCount = 0;
            return true;
        }
        int count = list.Count;
        if (count > MaxLayers)
        {
            Stop("Level.parallaxLayers count=" + count + " over cap " + MaxLayers + "; snapshot aborted, no truncation");
            return false;
        }
        _layers = new Parallax2D[count];
        for (int i = 0; i < count; i++) _layers[i] = list[i];
        _layerCount = count;
        _layersCaptured = true;
        return true;
    }

    /// <summary>首次逐项（只读）：pointer/GO name/scene/parent/local+world position/ratio/legacyRatio/cameraId/showOnCamera/hasSavedata。</summary>
    private static string LayerDetail(Parallax2D layer)
    {
        if (layer == null) return "ptr=none state=null";
        if (!(bool)layer) return "ptr=" + Ptr(layer) + " state=destroyed";
        try
        {
            GameObject go = layer.gameObject;
            Transform t = layer.transform;
            Transform parent = t != null ? t.parent : null;
            return "ptr=" + Ptr(layer) + " go=" + SafeName(go) + " scene=" + SceneHandle(go)
                + " parent=" + Ptr(parent)
                + " local=" + (t != null ? V3(t.localPosition) : "n/a")
                + " world=" + (t != null ? V3(t.position) : "n/a")
                + " ratio=" + V2(layer.ratio) + " legacyRatio=" + V2(layer.legacyRatio)
                + " camId=" + layer.cameraId + " show=" + layer.showOnCamera + " hasSavedata=" + layer.hasSavedata;
        }
        catch (Exception e) { return "ptr=" + Ptr(layer) + " state=unreadable(" + e.GetType().Name + ")"; }
    }

    /// <summary>后续 snapshot 的同组 layer：只读实际 position（world+local）用于两位比较；丢失显式记录。</summary>
    private static string LayerPos(Parallax2D layer)
    {
        if (layer == null) return "ptr=none state=null";
        if (!(bool)layer) return "ptr=" + Ptr(layer) + " state=destroyed";
        try
        {
            Transform t = layer.transform;
            if (t == null) return "ptr=" + Ptr(layer) + " state=noTransform";
            return "ptr=" + Ptr(layer) + " world=" + V3(t.position) + " local=" + V3(t.localPosition);
        }
        catch (Exception e) { return "ptr=" + Ptr(layer) + " state=unreadable(" + e.GetType().Name + ")"; }
    }

    /// <summary>Level.GetLayer 候选（只读；不创建/不 Register/不 Validate/不 ApplyParallaxCorrection）：单项 missing 即标 missing。</summary>
    private static string LayerSlot(Level level, ContentLayers key)
    {
        try
        {
            GameObject go = level.GetLayer(key);
            if (go == null) return "missing(null return; no create/register/correct)";
            Transform t = go.transform;
            return "ptr=" + Ptr(go) + " go=" + SafeName(go) + " scene=" + SceneHandle(go)
                + " parent=" + Ptr(t != null ? t.parent : null)
                + " world=" + (t != null ? V3(t.position) : "n/a");
        }
        catch (Exception e) { return "missing(" + e.GetType().Name + "; no fallback)"; }
    }

    // ------------------------------------------------------------------ 日志与格式化

    private static void Log(string message) => PalaceCourtVisuals.Log("[NativeMetrics] " + message);

    /// <summary>scope 未就绪（可能随加载顺序自行满足）：只报一次，不刷 log，也不提前停止 owner 观察。</summary>
    private static void WarnOnce(string reason)
    {
        if (_warned) return;
        _warned = true;
        Log("scope wait: " + reason + " (single report; no repeat)");
    }

    /// <summary>相机无法唯一识别：一次日志并停止该 owner 观察（无 Camera.main fallback、不重挑相机）。</summary>
    private static void CameraFail(string reason) => Stop("camera identification failed: " + reason
        + "; observation stopped for this owner (no fallback, no re-pick)");

    /// <summary>终点/漂移/故障：一次日志并永久停止本进程内的观察（不重建）。</summary>
    private static void Stop(string reason)
    {
        if (_stopped) return;
        _stopped = true;
        Log("stopped: " + reason);
    }

    private static string Ptr(Il2CppObjectBase o)
        => o == null || o.Pointer == IntPtr.Zero ? "none" : o.Pointer.ToInt64().ToString();

    private static string V3(Vector3 v)
        => "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";

    private static string V2(Vector2 v) => "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + ")";

    private static string Q(Quaternion q)
        => "(" + q.x.ToString("F3") + "," + q.y.ToString("F3") + "," + q.z.ToString("F3") + "," + q.w.ToString("F3") + ")";

    private static string R(Rect r)
        => "(x=" + r.x.ToString("F1") + ",y=" + r.y.ToString("F1") + ",w=" + r.width.ToString("F1") + ",h=" + r.height.ToString("F1") + ")";

    private static string SceneHandle(GameObject go)
    {
        try { return go == null ? "n/a" : go.scene.handle.ToString(); }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }

    private static string SafeName(UnityEngine.Object o)
    {
        try { return o == null ? "none" : o.name; }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }

    private static string ActiveText(object o)
    {
        try
        {
            if (o is Component component) return component.gameObject != null && component.gameObject.activeInHierarchy ? "True" : "False";
            if (o is GameObject go) return go.activeInHierarchy ? "True" : "False";
            return "n/a";
        }
        catch (Exception e) { return "unreadable(" + e.GetType().Name + ")"; }
    }
}
