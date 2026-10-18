using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using System.Security.Cryptography;
using UnityEngine;

namespace KingdomEnhancedMod;

// R7 有界景深环境替换模块（source-only：无 hook、无 MonoBehaviour、无周期扫描）。
// 生产层 = 冻结 manifest.runtimeRecipe 26 项：新增 mid-behind-far-roofs(Mid 视差/band−7) 与
// near-fixed-residual(Street 视差/band−3，index5 定子序)；far-palace、mid-courtyard、mid-canopy 换 R7 冻结字节，
// 其余 21 图与 48 帧字节不变（全部 hash 取冻结 inputs-manifest）。road 诊断项/preview/RGB/角色仍不嵌入。
// Root 接线（唯一 owner）：CourtBinding 创建窗口 PalaceDepthScene.Create(binding, binding.ScrollCalibration, out scene, out reason)
// 一次 → Activate(out reason) → 既有 Game.Update 帧回调 Tick(game, out reason) → outer Cleanup 先 Clear(out reason)。
// 相机只在 Create 窗口经 cameraRefs(≤4)×cameraTargets(≤4) 唯一匹配当次 playerOne；无 Camera.main、无扫描、无 fallback、不重选。
// c0 为唯一Calibrate提供的世界主轴，不随保存位置或Create相机改变；far=round(.3*(camX-c0)/s)、mid=round(.2*(camX-c0)/s)（round 平局远离 0；R7 起无 ±32/±16 clamp）。
// 渲染窗口 = 2*orthographicSize*aspect（非 GetWorldBounds 的 20u 诊断值）；R7 实测 source width=640.0000047683716px。
// Native viewport may cross either fixed-art edge. One own moving sprite (mid-behind-far-roofs) keeps its full
// finite source quad (0..1448 full-source own parallax); grounded far-palace/mid-courtyard/mid-canopy are Street
// world-fixed; no fixed world-canvas mesh crop, source texture UVs/pivot/z/order and Native camera unchanged.
// Scene identity/projection/finite arithmetic remain fail-closed.
// Actual whole-route/edge appearance, original asset bytes and end-shore closure remain pending independent evidence.
internal sealed class PalaceDepthScene
{
    private const string ResourcePrefix = "KingdomEnhancedMod.KEM_PalaceDepthR7/";
    private const string LocalMotionPrefix = "KingdomEnhancedMod.KEM_PalaceLocalMotionR6/";
    private const int MaxResourceBytes = 4 * 1024 * 1024;
    private const int MaxCameraRefs = 4;
    private const int MaxCameraTargets = 4;
    private const int MaxOffsetLogs = 16;

    // 移除 fixed world-canvas mesh crop 后保留的一次性只读 source 契约值（原 PalaceParallaxOwnCrop 注册校验）：
    // Sprite.Create full rect 1448x252、像素 pivot(AnchorPixelX 645, CanvasHeight-FootlinePixelY 65)、PPU32。
    private const float ExpectedFullSourcePivotX = 645f;
    private const float ExpectedFullSourcePivotY = 65f;
    private const float ExpectedFullSourcePpu = 32f;

    // 像素量化视差：far 0.3 / mid 0.2 倍相机位移（canvas px）；round 平局远离 0，无 clamp。
    private const double FarRatio = .3d;
    private const double MidRatio = .2d;

    // 旧mode band/order；R6depth修正仅把同一key映射成childZ，Native实际order统一0。
    // R7：MidBehindFarRoofs(-7) 排 far 之前；NearResidual 与 mid-canopy 同为 -3，仅靠 runtimeIndex5 定子序。
    // Motion 与 drawRole 独立：mid-behind-far-roofs 是 Mid 视差（不改为 Far）；near-residual 是 Street 视差（不改为 Mid）。
    private const int MidBehindFarRoofsOrder = -7; // 新 mid-behind-far-roofs：最远 rear 绘制
    private const int FarOrder = -6;
    private const int MidCourtyardOrder = -5;
    private const int GardenBackwallOrder = -4;
    private const int MidCanopyOrder = -3;
    private const int NearResidualOrder = -3;      // 新 near-fixed-residual：Street 运动，band 同 -3、index 决定子序
    private const int BackStructureOrder = -2; // street-static + stage/pav 后层 + plant-stem-backing
    private const int StaticLocalOrder = -1;   // 六个 dyn-* 本轮静态原位（worker 决定：不进入未审 actor-front 带，见 completion）
    private const int FrontOrder = 1;          // 前柱/前栏 + tree-visible-front-twigs
    private const int ShoreOrder = 2;          // shore-fixed

    private enum Motion : byte { Street = 0, Far = 1, Mid = 2 }

    private readonly struct LayerSpec
    {
        internal readonly Motion Motion;
        internal readonly int Order;
        internal readonly string Id;
        internal readonly string File;
        internal readonly string Hash;

        internal LayerSpec(Motion motion, int order, string id, string file, string hash)
        {
            Motion = motion; Order = order; Id = id; File = file; Hash = hash;
        }
    }

    // 固定 26 张 runtime PNG（全部 1448×252 / 共享原点 0/0）。创建顺序 = 冻结 manifest.runtimeRecipe 26 项顺序，
    // 同语义band内使用runtime index确定唯一子序，全部后层<0、前层>0，不依赖Unity tie-break。
    // road-surface-reference（zeroRecipe 诊断项）、preview 天空水、RGB 整卷、角色一律不嵌入、不绘制。
    private static readonly LayerSpec[] Specs =
    {
        new LayerSpec(Motion.Mid, MidBehindFarRoofsOrder, "mid-behind-far-roofs", "layers/mid-behind-far-roofs.png",
            "d055d88ffc0d30d557928f1ccc51d75c72982bc56b33ff1137ac6ef63d900a44"),
        new LayerSpec(Motion.Street, FarOrder, "far-palace", "layers/far-palace.png",
            "ab0cc416aa4685e1a05a1045fa2d73364e26ad610c038172bd7b010b2f09ae1c"),
        new LayerSpec(Motion.Street, MidCourtyardOrder, "mid-courtyard", "layers/mid-courtyard.png",
            "23df82e98ec048f82ceb6e8445166aeca5ec06e2d8fbf4895ef387119aff03ce"),
        new LayerSpec(Motion.Street, GardenBackwallOrder, "garden-backwall", "layers/garden-backwall.png",
            "ffee439d71f6991f37d4f0170c2409f18239e0b8ff3ed311f1800508dc7f2158"),
        new LayerSpec(Motion.Street, MidCanopyOrder, "mid-canopy-in-front-of-wall", "layers/mid-canopy-in-front-of-wall.png",
            "56208794a2c53d2a4358c63f225d166cd981dee96cd8d7178f89697ec0838d8a"),
        new LayerSpec(Motion.Street, NearResidualOrder, "near-fixed-residual", "layers/near-fixed-residual.png",
            "8649127147c244473441bac229045ea6fc7b4e32e87f3f12f1b7738707efd6bd"),
        new LayerSpec(Motion.Street, BackStructureOrder, "street-static", "layers/street-static.png",
            "5a2dac717ab22bcd244126e6fc9e5f0915d55bd0e6cb19f8a49281612863f77b"),
        new LayerSpec(Motion.Street, BackStructureOrder, "stage-roof-rear-frame", "parts/stage-roof-rear-frame.png",
            "f24c680418cad827d0233685840ac13e0af047c6a04fae0eda973e82a01eefdc"),
        new LayerSpec(Motion.Street, BackStructureOrder, "stage-backdrop-screen", "parts/stage-backdrop-screen.png",
            "1661fc1da647ebfc2ed1d504cb45de546f4f55eeb7921d3b96e4de11cdee86c1"),
        new LayerSpec(Motion.Street, BackStructureOrder, "stage-side-panels", "parts/stage-side-panels.png",
            "f1d5895ad1ed261e96329dc939ac7c673ade93ead078d504329bb45ed1e2acdc"),
        new LayerSpec(Motion.Street, BackStructureOrder, "stage-deck-steps", "parts/stage-deck-steps.png",
            "5a0b6cd8991fb768a1050c28ddfb3f3e146471de2eeaffe446ac74993664ddcf"),
        new LayerSpec(Motion.Street, BackStructureOrder, "pav-roof-beam", "parts/pav-roof-beam.png",
            "dc6369bb444b2a8ffdd9eb2ea2226de52fb2117888042ef59200e427f92fadc0"),
        new LayerSpec(Motion.Street, BackStructureOrder, "pav-terrace-steps", "parts/pav-terrace-steps.png",
            "8cb4d41a58fb43a8cc6c3f9aaad2688017aa387f7fa3b35ce8d3be316c7719d1"),
        new LayerSpec(Motion.Street, FrontOrder, "stage-front-columns", "parts/stage-front-columns.png",
            "99a2f5d465df1b2796883890b0f931514ebade5c87f14c936f5617840a6f297f"),
        new LayerSpec(Motion.Street, FrontOrder, "stage-front-railing", "parts/stage-front-railing.png",
            "a33cf750d05c67f278469ee3706458cb087f430c409d0497f9884d8d866f32f9"),
        new LayerSpec(Motion.Street, FrontOrder, "pav-front-columns", "parts/pav-front-columns.png",
            "a7dfd44f005d879cc33ca6527739fb697eabf1f38a2186f656a91dd47e99979a"),
        new LayerSpec(Motion.Street, FrontOrder, "pav-front-rail", "parts/pav-front-rail.png",
            "c59577eacbc4f355ae996e866f7a35c568059d29cd11f5db2b6159fcc8682f64"),
        new LayerSpec(Motion.Street, BackStructureOrder, "plant-stem-backing", "layers/plant-stem-backing.png",
            "1873cbe78a541b8d447e8d5adb3a03d1d5877e196da3723d1432964a6ca33bb0"),
        new LayerSpec(Motion.Street, StaticLocalOrder, "dyn-awning-w1", "parts/dyn-awning-w1.png",
            "1092b5efa7ec6f0941367c0a7ed1b59d696550d66ec043595e1362b69af4a7df"),
        new LayerSpec(Motion.Street, StaticLocalOrder, "dyn-awning-w2", "parts/dyn-awning-w2.png",
            "d08d5f25ee366bfeca33ae84aa309683c83f82dc63090bc2ba77c36225df019b"),
        new LayerSpec(Motion.Street, StaticLocalOrder, "dyn-awning-w3", "parts/dyn-awning-w3.png",
            "3baf7ebab36f1433f494e0f37923276b010b5ad1ffa9ad51554390693fa871fe"),
        new LayerSpec(Motion.Street, StaticLocalOrder, "dyn-willow-west", "parts/dyn-willow-west.png",
            "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77"),
        new LayerSpec(Motion.Street, StaticLocalOrder, "dyn-willow-east", "parts/dyn-willow-east.png",
            "db51196d17c2d8c325f5dc3cbed130188dfaf40e492af0e49f1383cb0db69306"),
        new LayerSpec(Motion.Street, StaticLocalOrder, "dyn-bamboo-leaves", "parts/dyn-bamboo-leaves.png",
            "99b49c34bdd8973e5f9508ad372aa2e4db10e4dd1ace55bdb0c611a6da45c945"),
        new LayerSpec(Motion.Street, FrontOrder, "tree-visible-front-twigs", "layers/tree-visible-front-twigs.png",
            "a8bee79b23b7ee32907cd4ee75d006f4346ec7b4892d20c0d82952b5141decb6"),
        new LayerSpec(Motion.Street, ShoreOrder, "shore-fixed", "layers/shore-fixed.png",
            "dfe0a59823676950da661305dd0181f372077b4c9676a6ad26df63f261d47e9e")
    };

    private static PalaceDepthScene _retainedAfterFailedClear;
    private static bool _dedicatedTemplateSampleAttempted;
    private bool _dedicatedTemplateSampleEnabled;
    private readonly List<GameObject> _children = new List<GameObject>(26 + 2); // 26 层 + far/mid 两个组
    private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>(26);
    private readonly HashSet<IntPtr> _movingSpritePointers = new HashSet<IntPtr>();
    private readonly List<Sprite> _sprites = new List<Sprite>(68);       // 20 静态 + 6×8 动态帧（关闭 local motion 时为 26）
    private readonly List<Texture2D> _textures = new List<Texture2D>(68);
    private Game _skyGame;
    private SpriteRenderer _skyFar, _skyStreet, _skyUnit;
    private PalaceLocalMotionClock _localMotion;
    private PalaceEnvironmentSortPlanes _sortPlanes;
    private GameObject _root;
    private Transform _farGroup;
    private Transform _midGroup;
    private Transform _parent;
    private IntPtr _parentPointer;
    private int _sceneHandle;
    private Vector3 _anchor;
    private bool _ready, _active, _cleared, _clearFailed;

    // 创建窗口一次冻结的所有权与相机绑定：不重选、不重建、c0 不在 Tick/saturation 重设。
    private double _unitsPerPixel, _anchorWorldX, _c0;
    private IntPtr _gamePointer, _kingdomPointer, _levelPointer, _layerPointer, _mainCameraPointer, _cameraPointer;
    private MainCamera _mainCamera;
    private Camera _camera;
    private int _cameraSceneHandle;
    private int _lastFarPx = int.MinValue, _lastMidPx = int.MinValue;
    private int _offsetLogs;
    private bool _loggedFailure;
    private bool _arrivalReadEnabled, _arrivalReadFirst, _arrivalReadMoved;
    private float _arrivalReadStartX;

    internal Transform GateAnchor => _ready && !_cleared && _root != null ? _root.transform : null;

    internal static bool Create(CourtBinding binding, PalaceScrollCalibration calibration,
        out PalaceDepthScene result, out string reason)
    {
        result = null;
        reason = null;
        if (_retainedAfterFailedClear != null)
        { reason = "previous R5 owner retained after incomplete clear; no new allocation"; return false; }
        var self = new PalaceDepthScene();
        try
        {
            if (binding == null || binding.Parent == null || binding.Reference == null || calibration == null)
                throw new InvalidOperationException("R5 binding/calibration unavailable");
            if (!ReferenceEquals(calibration, binding.ScrollCalibration))
                throw new InvalidOperationException("R5 requires the single CourtBinding calibration instance");
            Transform parent = binding.Parent;
            SpriteRenderer reference = binding.Reference;
            if (parent.gameObject.scene.handle != reference.gameObject.scene.handle)
                throw new InvalidOperationException("R5 binding spans different scenes");
            RequireTransformBasis(parent);
            calibration.RequireUnityCanvas();
            // 候选显式 order 只以「当次 reference order 0」为事实前提；其余一律拒绝，不缩放不改锚。
            if (reference.sortingOrder != 0)
                throw new InvalidOperationException("R5 reference sortingOrder=" + reference.sortingOrder
                    + " differs from the reviewed 0 baseline; candidate explicit orders rejected");
            if (reference.sortingLayerID != binding.LayerId)
                throw new InvalidOperationException("R5 binding LayerId differs from the current reference sorting layer");

            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.kingdom == null
                || managers.level == null || managers.world == null || managers.world.gameLayer == null)
                throw new InvalidOperationException("R5 owner data unavailable: Managers game/kingdom/level/world.gameLayer");
            Game game = managers.game;
            Level level = managers.level;
            Transform layer = managers.world.gameLayer;
            if (level.gameObject.scene.handle != parent.gameObject.scene.handle
                || layer.gameObject.scene.handle != parent.gameObject.scene.handle)
                throw new InvalidOperationException("R5 level/gameLayer scene differs from the owned parent");

            Camera cam = IdentifyPlayerOneCamera(managers, parent.gameObject.scene.handle, out MainCamera mainCamera);
            double cameraX = cam.transform.position.x;
            double c0 = calibration.AnchorWorldX; // stable shared world anchor across cold/reentry; no new save state
            if (!IsFinite(cameraX) || !IsFinite(c0))
                throw new InvalidOperationException("R5 camera x is not finite");
            float x = UnityFloat(calibration.WorldX(PalaceScrollCalibration.AnchorPixelX));
            float y = UnityFloat(calibration.WorldY(PalaceScrollCalibration.FootlinePixelY));
            float z = reference.transform.position.z;
            if (!Finite(z)) throw new InvalidOperationException("R5 reference plane is not finite");

            if (PalaceRuntimeProfile.Capability("PALACE_SORT_DEPTH_R6_PRIVATE"))
            {
                if (!PalaceEnvironmentSortPlanes.TryCreate(game, reference, cam, calibration.UnitsPerPixel,
                    out self._sortPlanes, out string sortReason))
                    throw new InvalidOperationException("R6 owned draw contract rejected: " + sortReason);
                // Verify the actual float view-depth keys before Unity allocation; large camera coordinates can collapse gaps.
                float cameraZ = cam.transform.position.z;
                float unitViewDepth = z - cameraZ;
                var viewDepths = new HashSet<float>();
                for (int i = 0; i < Specs.Length; i++)
                {
                    if (!self._sortPlanes.TryPlane(Specs[i].Order, i, out float localZ, out string planeReason))
                        throw new InvalidOperationException("R6 plane planning rejected: " + planeReason);
                    float worldZ = z + localZ;
                    float viewDepth = worldZ - cameraZ;
                    if (!Finite(viewDepth) || viewDepth == unitViewDepth || !viewDepths.Add(viewDepth)
                        || (Specs[i].Order < 0 ? viewDepth <= unitViewDepth : viewDepth >= unitViewDepth))
                        throw new InvalidOperationException("R6 projected float depth collapsed or reversed for " + Specs[i].Id);
                }
                Log("R7 planned " + viewDepths.Count + " distinct owned depths at unitZ=" + z.ToString("R")
                    + " cameraZ=" + cameraZ.ToString("R") + "; band keys are only geometry, native order0");
            }

            self._skyGame = game;
            self._skyUnit = reference;
            self._parent = parent;
            self._parentPointer = parent.Pointer;
            self._sceneHandle = parent.gameObject.scene.handle;
            self._unitsPerPixel = calibration.UnitsPerPixel;
            self._arrivalReadEnabled = PalaceRuntimeProfile.Capability("PALACE_ARRIVAL_CONTROL_OBSERVE_PRIVATE");
            self._dedicatedTemplateSampleEnabled = PalaceRuntimeProfile.Capability("PALACE_DEDICATED_TEMPLATE_READ_PRIVATE");
            self._anchorWorldX = calibration.AnchorWorldX;
            self._gamePointer = game.Pointer;
            self._kingdomPointer = managers.kingdom.Pointer;
            self._levelPointer = level.Pointer;
            self._layerPointer = layer.Pointer;
            self._mainCamera = mainCamera;
            self._mainCameraPointer = mainCamera.Pointer;
            self._camera = cam;
            self._cameraPointer = cam.Pointer;
            self._cameraSceneHandle = cam.gameObject.scene.handle;
            self._c0 = c0;
            SampleNativeCameraBoundaries(managers, cam);
            if (!self.TryWindow(cameraX, cam, out double leftPx, out double rightPx, out string windowReason))
                throw new InvalidOperationException("R5 create window rejected: " + windowReason);

            self._root = new GameObject("KEM_PalaceDepthR7"); // 产生处立即持有；后续失败统一 Clear
            self._root.SetActive(false);
            self._root.layer = binding.GoLayer;
            self._root.transform.SetParent(parent, false);
            // 父层是已平移的 court root，不是单位矩阵；共享 pivot 已按世界锚编写，root 直接放世界锚。
            self._anchor = new Vector3(x, y, z);
            self._root.transform.position = self._anchor;
            self._farGroup = self.NewGroup(binding, "far");
            self._midGroup = self.NewGroup(binding, "mid");
            if (PalaceRuntimeProfile.Capability("PALACE_LOCAL_MOTION_R6_PRIVATE"))
                self._localMotion = new PalaceLocalMotionClock(self._root);
            for (int i = 0; i < Specs.Length; i++) self.AddLayer(binding, calibration, Specs[i], i);
            if (self._movingSpritePointers.Count != 1) throw new InvalidOperationException("R7 expected one independently owned moving sprite");
            double initialDelta = (cameraX - c0) / self._unitsPerPixel;
            if (!IsFinite(initialDelta)) throw new InvalidOperationException("R5 initial camera delta is not finite");
            self.ApplyOffsets(Quantize(FarRatio * initialDelta), Quantize(MidRatio * initialDelta));
            self._ready = true;
            result = self;
            Log("created " + Specs.Length + " layers: far group(empty), mid group(" + MidBehindFarRoofsOrder
                + "), street-fixed " + (Specs.Length - 1)
                + " (grounded far-palace " + FarOrder + ", mid-courtyard " + MidCourtyardOrder + ", mid-canopy " + MidCanopyOrder
                + ", garden " + GardenBackwallOrder + ", near-residual " + NearResidualOrder
                + ", back " + BackStructureOrder + ", static-local " + StaticLocalOrder
                + ", front " + FrontOrder + ", shore " + ShoreOrder + ")"
                + "; mainCamera=" + mainCamera.Pointer.ToInt64() + " camId=" + mainCamera.cameraId
                + " cam=" + cam.Pointer.ToInt64() + " scene=" + self._sceneHandle + " sceneOfCamera=" + self._cameraSceneHandle
                + " ortho=" + cam.orthographic + " size=" + cam.orthographicSize.ToString("F3") + " aspect=" + cam.aspect.ToString("F4")
                + " c0=calibrated-world-axis(" + c0.ToString("F3") + ") createCameraX=" + cameraX.ToString("F3") + " windowPx=[" + leftPx.ToString("F1") + "," + rightPx.ToString("F1")
                + ") s=" + self._unitsPerPixel.ToString("R") + " axisWorldX=" + self._anchorWorldX.ToString("R")
                + "; single calibration; unique playerOne target; road-surface-reference/preview-blue/RGB-master/actors not embedded"
                + "; one own moving sprite (mid-behind-far-roofs) full-source own parallax; grounded far/mid layers street world-fixed; no mesh crop; viewport remains Native"
                + "; original asset bytes and end-shore closure acceptance pending; full route acceptance pending");
            return true;
        }
        catch (Exception error)
        {
            reason = "R5 depth create rejected: " + error.Message;
            if (!self.Clear(out string clearReason))
            {
                _retainedAfterFailedClear = self;
                result = self; // 保留 owner 与静态分配守卫：不清仓不新建
                reason += "; " + clearReason;
            }
            return false;
        }
    }

    private Transform NewGroup(CourtBinding binding, string label)
    {
        var group = new GameObject("palace-depth-group-" + label);
        _children.Add(group); // 注册后才做后续改动
        group.layer = binding.GoLayer;
        group.transform.SetParent(_root.transform, false); // 局部 0：组内共享 pivot 对齐 root 世界锚
        return group.transform;
    }

    private void AddLayer(CourtBinding binding, PalaceScrollCalibration calibration, LayerSpec spec, int layerIndex)
    {
        Sprite[] frames = null;
        Sprite sprite;
        if (_localMotion != null && LocalFrameHashes.TryGetValue(spec.Id, out string[] hashes))
        {
            if (spec.Motion != Motion.Street || hashes.Length != 8 || hashes[0] != spec.Hash)
                throw new InvalidDataException("R7 fixed local identity/phase00 differs from spec: " + spec.Id);
            frames = new Sprite[8];
            for (int phase = 0; phase < frames.Length; phase++)
            {
                string file = LocalFrameFile(spec.Id, phase);
                frames[phase] = LoadOwnedSprite(calibration, ReadLocal(file, hashes[phase]), file, true);
            }
            sprite = frames[0];
        }
        else sprite = LoadOwnedSprite(calibration, Read(spec.File, spec.Hash), spec.File);
        Transform parent = spec.Motion == Motion.Far ? _farGroup : spec.Motion == Motion.Mid ? _midGroup : _root.transform;
        var child = new GameObject("palace-depth-" + spec.Id);
        _children.Add(child); // 注册即使后续失败
        child.transform.SetParent(parent, false);
        child.layer = binding.GoLayer;
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        _renderers.Add(renderer);
        renderer.sprite = sprite;
        if (spec.Id == "far-palace") _skyFar = renderer;
        if (spec.Id == "street-static") _skyStreet = renderer;
        renderer.sortingLayerID = binding.LayerId;
        if (_sortPlanes != null)
        {
            if (!_sortPlanes.TryPlane(spec.Order, layerIndex, out float localZ, out string sortReason))
                throw new InvalidOperationException("R6 owned depth plane rejected for " + spec.Id + ": " + sortReason);
            child.transform.localPosition = new Vector3(0f, 0f, localZ);
            renderer.spriteSortPoint = SpriteSortPoint.Pivot; // Explicit owned sort point; inactive bounds need not be updated.
            renderer.sortingOrder = 0; // Equal native order lets verified orthographic depth decide sky/rear/unit/front.
            Vector3 actualLocal = child.transform.localPosition;
            float expectedWorldZ = _anchor.z + localZ;
            float actualWorldZ = child.transform.position.z;
            float actualViewDepth = actualWorldZ - _camera.transform.position.z;
            float unitViewDepth = _anchor.z - _camera.transform.position.z;
            if (actualLocal.x != 0f || actualLocal.y != 0f || actualLocal.z != localZ
                || renderer.sortingOrder != 0 || renderer.spriteSortPoint != SpriteSortPoint.Pivot
                || !Finite(actualWorldZ) || actualWorldZ != expectedWorldZ || !Finite(actualViewDepth)
                || !(actualViewDepth > _camera.nearClipPlane && actualViewDepth < _camera.farClipPlane)
                || (spec.Order < 0 ? actualViewDepth <= unitViewDepth : actualViewDepth >= unitViewDepth))
                throw new InvalidOperationException("R6 owned effective sort-point/depth/readback differs for " + spec.Id);
        }
        else renderer.sortingOrder = checked(spec.Order * 100 + layerIndex);
        renderer.color = Color.white;
        if (binding.Material != null) renderer.sharedMaterial = binding.Material;
        if (spec.Motion != Motion.Street)
        {
            // LoadOwnedSprite created this sprite/texture for this owner; no prototypes or frame sharing.
            if (frames != null || !_movingSpritePointers.Add(sprite.Pointer))
                throw new InvalidOperationException("R7 moving sprite shared or animated: " + spec.Id);
            // 一次性只读校验（原 TryRegister 关联检查 + MatchFullSource 值契约）：仅确认 sprite/renderer 确关联
            // 与 1448x252 full rect、pivot(645,65)、PPU32、未打包；不写 mesh/UV，无逐帧读回。
            if (!(bool)sprite || sprite.Pointer == IntPtr.Zero
                || !(bool)renderer || renderer.Pointer == IntPtr.Zero
                || renderer.sprite == null || renderer.sprite.Pointer != sprite.Pointer
                || sprite.packed || sprite.texture == null || !(bool)sprite.texture
                || sprite.rect.x != 0f || sprite.rect.y != 0f
                || sprite.rect.width != PalaceScrollCalibration.CanvasWidth || sprite.rect.height != PalaceScrollCalibration.CanvasHeight
                || sprite.texture.width != PalaceScrollCalibration.CanvasWidth || sprite.texture.height != PalaceScrollCalibration.CanvasHeight
                || !Finite(sprite.pivot.x) || !Finite(sprite.pivot.y)
                || sprite.pivot.x != ExpectedFullSourcePivotX || sprite.pivot.y != ExpectedFullSourcePivotY
                || !Finite(sprite.pixelsPerUnit) || sprite.pixelsPerUnit != ExpectedFullSourcePpu)
                throw new InvalidOperationException("R7 own moving sprite full-source geometry contract: " + spec.Id);
        }
        if (frames != null && !_localMotion.Bind(spec.Id, renderer, frames, out string motionReason))
            throw new InvalidOperationException("R6 local binding rejected: " + motionReason);
    }

    internal bool Activate(out string reason)
    {
        reason = null;
        if (_cleared || _clearFailed)
        { reason = "R5 owner is not ready for activation"; return false; }
        try
        {
            // 场景可能在 Create 与 Activate 之间销毁 root（texture 仍归本 owner）；走同一条回滚路径。
            if (!_ready || _root == null)
                throw new InvalidOperationException("R5 root disappeared before activation");
            if (_parent == null || _parent.Pointer != _parentPointer
                || _parent.gameObject.scene.handle != _sceneHandle || !_parent.gameObject.activeInHierarchy
                || _root.gameObject.scene.handle != _sceneHandle || _root.transform.parent == null
                || _root.transform.parent.Pointer != _parentPointer)
                throw new InvalidOperationException("R5 parent/scene epoch changed before activation");
            Vector3 now = _root.transform.position;
            if (!Finite(now.x) || !Finite(now.y) || !Finite(now.z) || (now - _anchor).sqrMagnitude > .00000001f)
                throw new InvalidOperationException("R5 world anchor changed before activation");
            RequireTransformBasis(_root.transform);
            if (_active)
            {
                if (!_root.activeInHierarchy) throw new InvalidOperationException("activated R5 root unexpectedly became inactive");
                return true;
            }
            _root.SetActive(true);
            if (!_root.activeInHierarchy) throw new InvalidOperationException("R5 root did not become active");
            if (_localMotion != null && !_localMotion.Begin(Time.time, out string motionReason))
                throw new InvalidOperationException("R6 local begin rejected: " + motionReason);
            _active = true;
            if (PalaceRuntimeProfile.Capability("PALACE_SKY_SORT_OBSERVE_PRIVATE"))
                PalaceSkySortSample.Sample(_skyGame, _root, _skyFar, _skyStreet, _skyUnit, _mainCamera, _camera);
            Log("activated (first activation): parent=" + _parentPointer.ToInt64() + " scene=" + _sceneHandle
                + "; sort=semanticBand*100+runtimeRecipeIndex; only owned far/mid group transforms move");
            return true;
        }
        catch (Exception error)
        {
            reason = "R5 activation rejected: " + error.Message;
            if (!Clear(out string clearReason)) reason += "; " + clearReason;
            return false;
        }
    }

    // 每帧由 Root 既有 callback 调用。越界 / owner 漂移 / 相机关系改变 / 任何未预期故障 → 返回 false（原因带回），
    // 由 Root 统一 Clear；此处不清仓、不重试、不重选相机、不移动玩家或相机。
    internal bool Tick(Game game, out string reason)
    {
        reason = null;
        try
        {
            if (_cleared || _clearFailed || !_ready)
                throw new InvalidOperationException("R5 owner is not ready for ticking");
            if (!_active)
                throw new InvalidOperationException("R5 tick before activation is an owner error");
            if (game == null || game.Pointer != _gamePointer)
                throw new InvalidOperationException("R5 game owner changed");
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != _gamePointer)
                throw new InvalidOperationException("R5 Managers.game owner changed");
            Kingdom kingdom = managers.kingdom;
            if (kingdom == null || kingdom.Pointer != _kingdomPointer)
                throw new InvalidOperationException("R5 kingdom owner changed");
            Level level = managers.level;
            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            if (level == null || layer == null || level.Pointer != _levelPointer || layer.Pointer != _layerPointer)
                throw new InvalidOperationException("R5 level/gameLayer owner changed");
            if (level.gameObject.scene.handle != _sceneHandle || layer.gameObject.scene.handle != _sceneHandle)
                throw new InvalidOperationException("R5 level/gameLayer scene changed");
            if (_root == null || _root.transform.parent == null || _root.transform.parent.Pointer != _parentPointer)
                throw new InvalidOperationException("R5 root parent changed");
            if (!CameraBound(out string cameraWhy))
                throw new InvalidOperationException("R5 camera owner/relation changed: " + cameraWhy);
            Camera cam = _camera;
            if (!cam.orthographic)
                throw new InvalidOperationException("R5 camera switched away from orthographic");
            if (_dedicatedTemplateSampleEnabled && !_dedicatedTemplateSampleAttempted
                && game.state == Game.State.Playing && !IslandSaveData.isSavingGame
                && !NetworkBigBoss.IsOnline && game.currentLand == PalaceLand12Runtime.LandIndex
                && ExtensionIslandRuntime.TryScope(out CampaignSaveData templateCampaign)
                && templateCampaign.CurrentLand == PalaceLand12Runtime.LandIndex)
            {
                _dedicatedTemplateSampleAttempted = true; // latch before exact borrowed-level getter
                // Existing Native getter only enumerates loaded LevelBlock objects and filters current block-scene name.
                // No load/generation/Layout constructor/shared-ledger getter is invoked here.
                try { PalaceDedicatedTemplateSample.Sample(level, level.GetLevelBlocks()); }
                catch (Exception e) { Log("dedicated template sample unsupported: existing getter " + e.GetType().Name
                    + "; sample latched, environment continues unchanged"); }
            }
            if (_arrivalReadEnabled && !_arrivalReadMoved && game.state == Game.State.Playing
                && !IslandSaveData.isSavingGame && game.currentLand == PalaceLand12Runtime.LandIndex
                && CampaignSaveData.current != null && CampaignSaveData.current.CurrentLand == PalaceLand12Runtime.LandIndex)
            {
                Player observedP1 = kingdom.playerOne;
                float observedX = observedP1.transform.position.x;
                if (!_arrivalReadFirst)
                {
                    _arrivalReadFirst = true; _arrivalReadStartX = observedX;
                    PalaceArrivalControlSample.Sample(game, observedP1, _skyUnit, "firstPlaying");
                }
                else if (Finite(observedX) && Math.Abs(observedX - _arrivalReadStartX) >= 2f)
                {
                    _arrivalReadMoved = true;
                    PalaceArrivalControlSample.Sample(game, observedP1, _skyUnit, "movedTwoUnits");
                }
            }
            float cameraX = cam.transform.position.x;
            if (!Finite(cameraX))
                throw new InvalidOperationException("R5 camera x is not finite");
            if (!TryWindow(cameraX, cam, out double leftPx, out double rightPx, out string windowWhy))
                throw new InvalidOperationException("R5 render window rejected: " + windowWhy);
            double deltaSource = ((double)cameraX - _c0) / _unitsPerPixel;
            if (!IsFinite(deltaSource))
                throw new InvalidOperationException("R5 camera delta is not finite");
            int farPx = Quantize(FarRatio * deltaSource);
            int midPx = Quantize(MidRatio * deltaSource);
            ApplyOffsets(farPx, midPx);
            if (_localMotion != null && !_localMotion.Tick(Time.time, out string motionReason))
                throw new InvalidOperationException("R6 local tick rejected: " + motionReason);
            return true;
        }
        catch (Exception error)
        {
            reason = "R5 depth tick rejected: " + error.Message;
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Log("first tick rejection (Root clears; no retry, no re-pick): " + reason);
            }
            return false;
        }
    }

    // 成功仅代表这些检查过的 Destroy 调用已返回；Unity 销毁是延迟的。
    // 未知失败 → retain/quarantine：保留 owner/列表并禁止再分配，不做盲重试。
    internal bool Clear(out string reason)
    {
        reason = null;
        if (_cleared) return true;
        if (_clearFailed) { reason = "R5 cleanup responsibility retained; no blind retry"; return false; }
        _ready = false; _active = false;
        _localMotion?.Close();
        _localMotion = null;
        _sortPlanes = null; // Pure frozen numbers only; no borrowed Native lifetime.
        _skyGame = null; _skyFar = null; _skyStreet = null; _skyUnit = null;
        try
        {
            if (_root != null)
            {
                _root.SetActive(false);
                if (_root.activeSelf) throw new InvalidOperationException("R5 root remained active");
            }
            foreach (SpriteRenderer renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.sprite = null;
                if (renderer.sprite != null) throw new InvalidOperationException("R5 renderer retained its sprite");
            }
            foreach (GameObject child in _children) if (child != null) UnityEngine.Object.Destroy(child);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (Sprite sprite in _sprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
            _root = null; _parent = null; _farGroup = null; _midGroup = null;
            _mainCamera = null; _camera = null;
            _children.Clear(); _renderers.Clear(); _sprites.Clear(); _textures.Clear();
            _cleared = true;
            return true;
        }
        catch (Exception error)
        {
            _clearFailed = true;
            _retainedAfterFailedClear = this;
            reason = "R5 cleanup call failed; owner and assets retained: " + error.Message;
            return false;
        }
    }

    /// <summary>
    /// Create 窗口单次识别：cameraRefs(≤4) × cameraTargets(≤4) 中唯一命中当次 kingdom.playerOne.gameObject 的 MainCamera。
    /// 拒绝 Camera.main、拒绝枚举扫描、拒绝 fallback 与下一帧重选；scene/ortho/finite 全部在此时校验。
    /// </summary>
    private static Camera IdentifyPlayerOneCamera(Managers managers, int sceneHandle, out MainCamera mainCamera)
    {
        mainCamera = null;
        Player playerOne = managers.kingdom != null ? managers.kingdom.playerOne : null;
        if (playerOne == null || !(bool)playerOne || playerOne.gameObject == null)
            throw new InvalidOperationException("R5 camera identification: kingdom.playerOne unavailable");
        GameObject target = playerOne.gameObject;
        CameraMarshaller marshaller = CameraMarshaller.Inst;
        if (marshaller == null)
            throw new InvalidOperationException("R5 camera identification: CameraMarshaller.Inst unavailable");
        var refs = marshaller.cameraRefs;
        if (refs == null || refs.Length < 1 || refs.Length > MaxCameraRefs)
            throw new InvalidOperationException("R5 camera identification: cameraRefs "
                + (refs == null ? "null" : "count=" + refs.Length) + " (expected 1.." + MaxCameraRefs + ")");
        int hits = 0;
        MainCamera match = null;
        for (int i = 0; i < refs.Length; i++)
        {
            MainCamera candidate = refs[i];
            if (candidate == null || !(bool)candidate) continue;
            var targets = candidate.cameraTargets;
            if (targets == null) continue;
            if (targets.Length > MaxCameraTargets)
                throw new InvalidOperationException("R5 camera identification: cameraTargets over cap at cameraRefs[" + i
                    + "] count=" + targets.Length);
            bool hit = false;
            for (int t = 0; t < targets.Length; t++)
            {
                GameObject go = targets[t];
                if (go != null && go.Pointer == target.Pointer) { hit = true; break; }
            }
            if (!hit) continue;
            hits++;
            match = candidate;
        }
        if (hits != 1 || match == null)
            throw new InvalidOperationException("R5 camera identification: playerOne camera not unique (hits=" + hits
                + "); no Camera.main, no scan, no fallback, no re-pick");
        Camera cam = match._camera;
        if (cam == null || !(bool)cam)
            throw new InvalidOperationException("R5 camera identification: MainCamera._camera null/destroyed");
        if (!cam.enabled || cam.gameObject == null || !cam.gameObject.activeInHierarchy)
            throw new InvalidOperationException("R5 camera identification: _camera not enabled/active");
        if (cam.gameObject.scene.handle != sceneHandle)
            throw new InvalidOperationException("R5 camera identification: _camera scene differs from the owned parent scene");
        if (!cam.orthographic)
            throw new InvalidOperationException("R5 camera identification: _camera is not orthographic");
        if (!Finite(cam.orthographicSize) || cam.orthographicSize <= 0f || !Finite(cam.aspect) || cam.aspect <= 0f)
            throw new InvalidOperationException("R5 camera identification: orthographicSize/aspect not finite positive");
        Vector3 position = cam.transform.position;
        if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
            throw new InvalidOperationException("R5 camera identification: camera position not finite");
        mainCamera = match;
        return cam;
    }

    /// <summary>绑定相机仍为当前 owner：指针/启用/active/scene 不变，且 playerOne 目标关系仍唯一（每帧只读，≤4+4）。</summary>
    private bool CameraBound(out string reason)
    {
        reason = null;
        if (_mainCamera == null || !(bool)_mainCamera) { reason = "MainCamera destroyed"; return false; }
        if (_mainCamera.Pointer != _mainCameraPointer) { reason = "MainCamera replaced"; return false; }
        Camera currentCamera = _mainCamera._camera;
        if (currentCamera == null || currentCamera.Pointer != _cameraPointer)
        { reason = "MainCamera camera association changed"; return false; }
        if (_camera == null || !(bool)_camera) { reason = "Camera destroyed"; return false; }
        if (!_camera.enabled || _camera.gameObject == null || !_camera.gameObject.activeInHierarchy)
        { reason = "Camera disabled/inactive"; return false; }
        if (_camera.gameObject.scene.handle != _cameraSceneHandle) { reason = "Camera scene changed"; return false; }
        Managers managers = Managers.Inst;
        Player player = managers != null && managers.kingdom != null ? managers.kingdom.playerOne : null;
        if (player == null || player.gameObject == null) { reason = "current playerOne unavailable"; return false; }
        IntPtr targetPointer = player.gameObject.Pointer;
        CameraMarshaller marshaller = CameraMarshaller.Inst;
        if (marshaller == null) { reason = "CameraMarshaller.Inst unavailable"; return false; }
        var refs = marshaller.cameraRefs;
        if (refs == null || refs.Length < 1 || refs.Length > MaxCameraRefs)
        { reason = "cameraRefs changed (" + (refs == null ? "null" : "count=" + refs.Length) + ")"; return false; }
        int referenceHits = 0, targetHits = 0;
        for (int i = 0; i < refs.Length; i++)
        {
            MainCamera candidate = refs[i];
            if (candidate == null || !(bool)candidate) continue;
            if (candidate.Pointer == _mainCameraPointer) referenceHits++;
            var targets = candidate.cameraTargets;
            if (targets == null) continue;
            if (targets.Length > MaxCameraTargets) { reason = "current target array over cap"; return false; }
            bool targetsPlayer = false;
            for (int t = 0; t < targets.Length; t++)
                if (targets[t] != null && targets[t].Pointer == targetPointer) { targetsPlayer = true; break; }
            if (!targetsPlayer) continue;
            targetHits++;
            if (candidate.Pointer != _mainCameraPointer)
            { reason = "playerOne target changed to another MainCamera"; return false; }
        }
        if (referenceHits != 1 || targetHits != 1)
        { reason = "bound MainCamera/playerOne relationship no longer unique"; return false; }
        return true;
    }

    /// <summary>
    // Actual viewport is observed, not used as artwork ownership bounds. Native space beyond the
    // finite self-art rectangle remains visible. No camera/zoom/position modification.
    // Sole Create-window sample, after exact playerOne camera identification and before the finite art-window gate.
    // Metadata queries and named typed getters only: no Native pointer arithmetic, mutation, hook, driver or Tick sample.
    private static void SampleNativeCameraBoundaries(Managers expectedManagers, Camera camera)
    {
        if (!PalaceCourtArea.DedicatedWholeEnabled) return;
        try
        {
            Managers current = Managers.Inst;
            if (current == null || expectedManagers == null || current.Pointer != expectedManagers.Pointer
                || current.kingdom == null || camera == null || !(bool)camera)
            { Log("camera-boundary Create sample unknown: exact current owner/camera unavailable"); return; }
            Kingdom kingdom = current.kingdom;
            var text = new StringBuilder("camera-boundary Create sample readonly: managers=");
            text.Append(current.Pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture))
                .Append(" kingdom=").Append(kingdom.Pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture))
                .Append(" cameraX=").Append(camera.transform.position.x.ToString("R", CultureInfo.InvariantCulture));
            text.Append("; min=").Append(DescribeBoundaryTransform(kingdom.cameraMinX));
            text.Append("; max=").Append(DescribeBoundaryTransform(kingdom.cameraMaxX));
            AppendFieldMetadata(text, typeof(Managers), "kingdom", out _, out _);
            AppendFieldMetadata(text, typeof(Kingdom), "cameraMinX", out _, out _);
            AppendFieldMetadata(text, typeof(Kingdom), "cameraMaxX", out _, out _);
            AppendFieldMetadata(text, typeof(CameraMarshaller), "_screenManRef", out _, out _);
            CameraMarshaller marshaller = CameraMarshaller.Inst;
            ScreenManager screen = null;
            PropertyInfo screenGetter = typeof(CameraMarshaller).GetProperty("_screenManRef",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (marshaller != null && screenGetter != null && screenGetter.PropertyType == typeof(ScreenManager)
                && screenGetter.GetGetMethod(true) != null)
                screen = screenGetter.GetValue(marshaller) as ScreenManager;
            text.Append("; screen=").Append(screen == null ? "null-or-unknown"
                : screen.Pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture));
            AppendScreenGetter(text, screen, "_width");
            AppendScreenGetter(text, screen, "scaledWidth");
            AppendScreenGetter(text, screen, "WindowWidth");
            AppendScreenGetter(text, screen, "renderWidth");
            text.Append("; geometry44 does not prove camera limits; one Create read, no writes");
            Log(text.ToString());
        }
        catch (Exception e)
        {
            // A failed diagnostic remains unknown. It never catches/replaces the existing Create failure.
            Log("camera-boundary Create sample unknown: " + e.GetType().Name + "; original Create continues");
        }
    }

    private static string DescribeBoundaryTransform(GameObject value)
    {
        bool clrNull = ReferenceEquals(value, null);
        bool unityAlive = !clrNull && (bool)value;
        if (!unityAlive) return "goClrNull=" + clrNull + ",goUnityAlive=false,x=unknown";
        string goPointer = value.Pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture);
        Transform transform = value.transform;
        bool transformNull = ReferenceEquals(transform, null);
        bool transformAlive = !transformNull && (bool)transform;
        if (!transformAlive) return "goClrNull=false,goUnityAlive=true,goPtr=" + goPointer
            + ",transformClrNull=" + transformNull + ",transformUnityAlive=false,x=unknown";
        return "goClrNull=false,goUnityAlive=true,goPtr=" + goPointer
            + ",transformClrNull=false,transformUnityAlive=true,transformPtr="
            + transform.Pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture)
            + ",x=" + transform.position.x.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void AppendFieldMetadata(StringBuilder text, Type wrapperType, string exactName,
        out long offset, out string nativeKind)
    {
        offset = -1;
        nativeKind = "unknown";
        text.Append("; meta ").Append(wrapperType.Name).Append('.').Append(exactName).Append('=');
        try
        {
            FieldInfo field = wrapperType.GetField("NativeFieldInfoPtr_" + exactName,
                BindingFlags.NonPublic | BindingFlags.Static);
            object raw = field != null ? field.GetValue(null) : null;
            IntPtr fieldInfo = raw is IntPtr pointer ? pointer : IntPtr.Zero;
            if (fieldInfo == IntPtr.Zero) { text.Append("unknown-fieldInfo"); return; }
            offset = (long)IL2CPP.il2cpp_field_get_offset(fieldInfo);
            // Resolve exact public query APIs without presuming their wrapper signatures or enum assembly.
            MethodInfo getFieldType = typeof(IL2CPP).GetMethod("il2cpp_field_get_type",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IntPtr) }, null);
            MethodInfo getKind = typeof(IL2CPP).GetMethod("il2cpp_type_get_type",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IntPtr) }, null);
            if (getFieldType != null && getKind != null)
            {
                object rawType = getFieldType.Invoke(null, new object[] { fieldInfo });
                if (rawType is IntPtr nativeType && nativeType != IntPtr.Zero)
                {
                    object kind = getKind.Invoke(null, new object[] { nativeType });
                    if (kind != null && kind.GetType().IsEnum) nativeKind = kind.ToString();
                }
            }
            text.Append("offset=0x").Append(offset.ToString("x", CultureInfo.InvariantCulture))
                .Append(",nativeType=").Append(nativeKind);
        }
        catch (Exception e)
        { text.Append("unknown:").Append(e.GetType().Name); }
    }

    private static void AppendScreenGetter(StringBuilder text, ScreenManager screen, string exactName)
    {
        AppendFieldMetadata(text, typeof(ScreenManager), exactName, out long offset, out string nativeKind);
        PropertyInfo property = typeof(ScreenManager).GetProperty(exactName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        MethodInfo getter = property != null ? property.GetGetMethod(true) : null;
        text.Append("; getter ").Append(exactName).Append('=');
        if (getter == null || (!getter.IsStatic && (screen == null || !(bool)screen)))
        { text.Append("unknown-missing-getter-or-screen"); return; }
        text.Append("clrType=").Append(property.PropertyType.FullName).Append(',');
        // In particular, a 0x60 field is only read as float when Native R4 and the named float getter agree.
        bool matchingFloat = property.PropertyType == typeof(float) && nativeKind == "IL2CPP_TYPE_R4";
        bool matchingInt = property.PropertyType == typeof(int) && nativeKind == "IL2CPP_TYPE_I4";
        if (offset < 0 || (!matchingFloat && !matchingInt))
        { text.Append("value=unknown-type-unproved"); return; }
        try
        {
            object value = property.GetValue(getter.IsStatic ? null : screen);
            if (matchingFloat && value is float f)
                text.Append("value=").Append(f.ToString("R", CultureInfo.InvariantCulture));
            else if (matchingInt && value is int i)
                text.Append("value=").Append(i.ToString(CultureInfo.InvariantCulture));
            else text.Append("value=unknown-return-type");
        }
        catch (Exception e) { text.Append("value=unknown:").Append(e.GetType().Name); }
    }

    private bool TryWindow(double cameraX, Camera cam, out double leftPx, out double rightPx, out string reason)
    {
        leftPx = 0d; rightPx = 0d; reason = null;
        double size = cam.orthographicSize, aspect = cam.aspect;
        if (!IsFinite(size) || size <= 0d || !IsFinite(aspect) || aspect <= 0d)
        { reason = "orthographicSize/aspect not finite positive"; return false; }
        double widthPx = 2d * size * aspect / _unitsPerPixel;
        double centerPx = PalaceScrollCalibration.AnchorPixelX + (cameraX - _anchorWorldX) / _unitsPerPixel;
        if (!IsFinite(widthPx) || !IsFinite(centerPx))
        { reason = "window width/center not finite"; return false; }
        leftPx = centerPx - widthPx * .5d;
        rightPx = centerPx + widthPx * .5d;
        // Viewport can legitimately reveal native space beyond this finite artwork. Its bounds do not
        // invalidate our scene: all own sprites keep their full finite source quad (no mesh clip).
        if (!(widthPx > 0d) || !IsFinite(leftPx) || !IsFinite(rightPx) || !(rightPx > leftPx))
        {
            reason = "render viewport is not a finite positive interval";
            return false;
        }
        return true;
    }

    // round 平局远离 0（+.5 → 上、-.5 → 下）；R7 起无 ±32/±16 clamp。非有限或超出 int 范围 → 抛出拒绝，走调用方原有 Clear 路径。
    private static int Quantize(double raw)
    {
        double rounded = Math.Round(raw, MidpointRounding.AwayFromZero);
        if (!IsFinite(rounded) || rounded > int.MaxValue || rounded < int.MinValue)
            throw new InvalidOperationException("R7 quantized offset not finite/int-range: " + raw.ToString("R"));
        return (int)rounded;
    }

    // 只写自有两个组的 transform；无 offset 变化不重复写。日志上限 MaxOffsetLogs 条，之后静默继续应用。
    private void ApplyOffsets(int farPx, int midPx)
    {
        bool farChanged = farPx != _lastFarPx;
        bool midChanged = midPx != _lastMidPx;
        if (!farChanged && !midChanged) return;
        if (farChanged)
        {
            _farGroup.localPosition = new Vector3(UnityFloat(farPx * _unitsPerPixel), 0f, 0f);
            _lastFarPx = farPx;
        }
        if (midChanged)
        {
            _midGroup.localPosition = new Vector3(UnityFloat(midPx * _unitsPerPixel), 0f, 0f);
            _lastMidPx = midPx;
        }
        if (_offsetLogs < MaxOffsetLogs)
        {
            _offsetLogs++;
            Log("offset far=" + farPx + "px mid=" + midPx + "px (quantized; log " + _offsetLogs + "/" + MaxOffsetLogs
                + "; full-source own parallax, Native viewport, no mesh crop; asset/end-shore acceptance pending)");
        }
    }

    private static byte[] Read(string file, string expectedHash)
    {
        using Stream stream = typeof(PalaceDepthScene).Assembly.GetManifestResourceStream(ResourcePrefix + file);
        if (stream == null || stream.Length <= 0 || stream.Length > MaxResourceBytes)
            throw new InvalidDataException("R5 embedded resource missing or out of bounds: " + file);
        byte[] bytes = new byte[checked((int)stream.Length)];
        int read = 0;
        while (read < bytes.Length)
        {
            int step = stream.Read(bytes, read, bytes.Length - read);
            if (step <= 0) throw new EndOfStreamException("R5 embedded resource truncated: " + file);
            read += step;
        }
        using SHA256 sha = SHA256.Create();
        if (!string.Equals(Convert.ToHexString(sha.ComputeHash(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("R5 embedded resource hash differs: " + file);
        return bytes;
    }

    private static float UnityFloat(double value)
    {
        float result = (float)value;
        if (!Finite(result)) throw new ArgumentOutOfRangeException(nameof(value), "R5 value cannot be represented by Unity float");
        return result;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static void Log(string message) => PalaceCourtVisuals.Log("[PalaceDepthR7] " + message);

    private static void RequireTransformBasis(Transform transform)
    {
        Vector3 scale = transform.lossyScale;
        float angle = Quaternion.Angle(transform.rotation, Quaternion.identity);
        if (!Finite(scale.x) || !Finite(scale.y) || !Finite(scale.z) || !Finite(angle)
            || Math.Abs(scale.x - 1f) > .0001f || Math.Abs(scale.y - 1f) > .0001f || Math.Abs(scale.z - 1f) > .0001f
            || angle > .0001f)
            throw new InvalidOperationException("R5 transform must have unit world scale and no rotation");
    }

    // Only these six ID-bound renderers consume R6 motion. Resource ownership stays in this DepthScene.
    private static readonly Dictionary<string, string[]> LocalFrameHashes = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        { "dyn-awning-w1", new[] { "1092b5efa7ec6f0941367c0a7ed1b59d696550d66ec043595e1362b69af4a7df", "930d2a60fa65cbf23f35b782c7b99792c42c04265719b829d48929d96264a619", "6e6c8b60ac359dfe60607b643e1153f40070ab97d847ba75340f525e791b28c7", "ee7207daa9517589fd2269d1dc7949c81c7e32d9fbbd587a1f82891afa9ba2c8", "03c38578f7671c6b99825b223062b3c3d4df9ebdff33c2a415d4f451e82c8376", "a0da92bf74d03e50ed5005a88f7f6cdbc651c7809c044128007ab7bfe6c3d763", "19f78bea1f2e3c1fd7dc6fb72ad76badf69acd9ed50af093e8f7c5571e99d1d7", "782e255c44448dd9fec3660994e49743d75dc9dd7c00dd87e560f73cfbfd9539" } },
        { "dyn-awning-w2", new[] { "d08d5f25ee366bfeca33ae84aa309683c83f82dc63090bc2ba77c36225df019b", "0b32cc918fd26bb72b03eb3ec8d30012e6b52bede0fa0cdf5454fffa16f29a95", "602b1543e996e93c553f32d6b1389178de82c39a1e5abf3130387ac60b2c4a3e", "ea9286399556e298465ae61c4da449c12b3d6b56bd0c7691c6bd3ef669e14cc3", "62ea245c93ac5c2483bae62c9e9576359121a5a29e7f1209f465656e8e3d451c", "a87048e8a947bf3825e91e25ba6ffeff044672da30f115cf34e056eaa5f3d06e", "693771c2213ed5f9693446e3dd65ef9e3ba38dc6ad3e0bd82174a7bd4597b4cb", "f93ba2649f6c8cae58d3c985043c31cf2d5b029bcc51035fc852cb479391420a" } },
        { "dyn-awning-w3", new[] { "3baf7ebab36f1433f494e0f37923276b010b5ad1ffa9ad51554390693fa871fe", "cafb034d9004652c28ff758f9f0e987b853cbde1166563c61a1ab28367079182", "824f6420cf7ec7159eed1783d80f0eb7898f722035211bfeb6818664e763a793", "052ef46ef74634677ff4ded98d1b5d1535fcb4c5794aebf069e9fa40b0f1aac4", "9c29226f1150e43121fc45cfa93bf0c375462a568cacdb26a3a9fbf1a76c6e90", "96245d7d6df5bc897ea953ff9f9340457d512294a1002b525144a59009e371d6", "d213be63a26a70951e6ac701e2ff160bf8330c38009e2f67caec56b45e026971", "ace609cda2a72ff5d13befab8fb4e335a14f748ed233f7b37e27395156247270" } },
        { "dyn-willow-west", new[] { "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77", "e0fce7d91176fa84784a3a6c537658f42fe002a1d0cad66b31073241394b3b77" } },
        { "dyn-willow-east", new[] { "db51196d17c2d8c325f5dc3cbed130188dfaf40e492af0e49f1383cb0db69306", "d23f5871b87f1d011cd6e1e8c6217de0943de3f851de8ff50485502cfdd98b2a", "70b23b4a5dbfbd8eda8484b38efc9bec18422d510356ddea1449a1a450b41ef2", "418776b41b477d4ec9d603efbc7d022d3fcb4815b07b2f7970fb30a4a4474014", "f09931be5a9b2564c3d26aa97d3295db4b7c2ac65d1be069a66d8728fd35c426", "470236e11f617868831f4ba5f448225bc11c61689ed0ca58c59813024758c36f", "18084c7a8f64d96794878c3ff3082dca60f7c46a54e8cf685b15756596b41366", "87056cd5ec06cd88b84ed599ed002c2127437d2556f833f9c65f8cb4576e17c5" } },
        { "dyn-bamboo-leaves", new[] { "99b49c34bdd8973e5f9508ad372aa2e4db10e4dd1ace55bdb0c611a6da45c945", "f197fe859adbca71bb21f050dc71f95558f8b3f70849afcd90168e6cfa6bd518", "97b7981241bebb2971b5f49d562298c8325c76fadd042b7c6c3a389c6f424b74", "0cec20c71748517ff136d616d9f5298b7c6ddf27da237654bf417305a918e5e1", "594093a75d27caae682edc175f375e2636ca3c2e3bf7bb1cc0db80c641c02b5e", "7cd89e7f12bd750601f5e4d71249746c4a32cd6666dadc2675b029be61087c62", "5e779f467673316eca954a738811f6d28154ff3ce36846497f33ca8c0543b5c3", "36f74d8d1666a20ad0dae09fff58d76fbc1fcc0b0e0dc28780dbe4131e569296" } },
    };

    /// <summary>R6 本地动画帧 relative 名（AddLayer/ReadLocal/precheck 共用同一命名，避免第二处推导）。</summary>
    private static string LocalFrameFile(string id, int phase)
        => "frames/" + id + "/phase-" + phase.ToString("D2") + ".png";

    /// <summary>R7 26 层与 R6 启用 48 帧（6 id × 8 相位）的付前资源契约（只读；与 AddLayer/ReadLocal
    /// 消费同一 Specs/LocalFrameHashes 表，尺寸 = 共享画布 1448x252；不复制 hash 字面值）。</summary>
    internal static void DescribeResourceContracts(List<PalaceResourceContract> into, out string reason)
    {
        reason = "";
        if (into == null) { reason = "into-null"; return; }
        for (int i = 0; i < Specs.Length; i++)
            into.Add(new PalaceResourceContract(ResourcePrefix + Specs[i].File, Specs[i].Hash,
                PalaceScrollCalibration.CanvasWidth, PalaceScrollCalibration.CanvasHeight, MaxResourceBytes,
                "r7-layer:" + Specs[i].Id));
        foreach (KeyValuePair<string, string[]> pair in LocalFrameHashes)
        {
            if (pair.Value == null || pair.Value.Length != 8)
            { reason = "local-frame-count:" + pair.Key + ":" + (pair.Value != null ? pair.Value.Length : -1); return; }
            for (int phase = 0; phase < pair.Value.Length; phase++)
                into.Add(new PalaceResourceContract(LocalMotionPrefix + LocalFrameFile(pair.Key, phase),
                    pair.Value[phase], PalaceScrollCalibration.CanvasWidth, PalaceScrollCalibration.CanvasHeight,
                    MaxResourceBytes, "r6-local:" + pair.Key + ":" + phase));
        }
    }

    private Sprite LoadOwnedSprite(PalaceScrollCalibration calibration, byte[] bytes, string label, bool retainUnused = false)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _textures.Add(texture);
        if (retainUnused)
        {
            // Preloaded non-current frames have no native renderer reference yet. CLR lists are cleanup ownership, not Unity retention.
            texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if ((texture.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
                throw new InvalidOperationException("R6 texture retention readback failed: " + label);
        }
        if (!ImageConversion.LoadImage(texture, bytes, false))
            throw new InvalidDataException("Palace PNG decode failed: " + label);
        if (texture.width != PalaceScrollCalibration.CanvasWidth || texture.height != PalaceScrollCalibration.CanvasHeight)
            throw new InvalidDataException("Palace PNG dimensions differ from 1448x252: " + label);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 0;
        PalaceScrollPivot pivot = calibration.SharedPivot(0, 0, texture.width, texture.height);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(UnityFloat(pivot.X), UnityFloat(pivot.Y)), UnityFloat(calibration.PixelsPerUnit), 0u, SpriteMeshType.FullRect);
        if (sprite == null) throw new InvalidOperationException("Palace Sprite.Create returned null: " + label);
        _sprites.Add(sprite);
        if (retainUnused)
        {
            sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if ((sprite.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
                throw new InvalidOperationException("R6 sprite retention readback failed: " + label);
        }
        return sprite;
    }

    private static byte[] ReadLocal(string file, string expectedHash)
    {
        using Stream stream = typeof(PalaceDepthScene).Assembly.GetManifestResourceStream(LocalMotionPrefix + file);
        if (stream == null || stream.Length <= 0 || stream.Length > MaxResourceBytes)
            throw new InvalidDataException("R6 local resource missing/size rejected: " + file);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (actual != expectedHash) throw new InvalidDataException("R6 local resource hash drift: " + file);
        return bytes;
    }
}
