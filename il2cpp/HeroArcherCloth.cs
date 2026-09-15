// 英雄弓箭手·红双飘带表现（自有对象 + 一次共享材质；由 HeroArcherVisuals 的 LateUpdate 驱动）。
//
// 契约（operator 2026-09-14）：
//   internal static HeroArcherCloth {
//       internal sealed class Handle;
//       static Handle Create(Transform parent, SpriteRenderer reference);
//       static void Tick(Handle, float dt, float localVelocity, bool visible);
//       static void Destroy(Handle);
//       static void SetWind(Handle, float signedNormalized);   // 原生世界风：视觉方向/强度，-1..1
//   }
//
// 资源/呈现：
// - 肩部 root：父母节点 = 原生/自有 renderer 的父节点，localPosition = reference.localPosition
//   + (-5/32, 14/32, 0)（肩高 14px）；sprite 默认朝右，朝向由父 root scale.x 负责
//   （本模块只额外镜像 flipX）；地面 = 肩部 local y = -14/32，链长 24/22px 会折叠在身后，
//   绝不穿地；两条链锚点错开（后 1px、上 3px）避免加宽后合成单片；
// - 两条自有链（长 24px / 22px），每条一个自有 GameObject + MeshFilter + MeshRenderer(Mesh)：
//   8 节点 / 7 段 × 2 个平涂面，主体 3–4px / 尾部 2–3px，固定红色折面，无发光；
// - 材质只建一次（静态共享；shader = reference 共享材质的 shader ?? Sprites/Default ?? Unlit/Texture，
//   mainTexture = 自有 1x1 白纹理，绝不采样图集/不产生柔边光晕）；只用 sharedMaterial，
//   从不读写原生 renderer 的 material/propertyBlock；逐帧 alpha 走自有 MaterialPropertyBlock；
// - 排序在身体后：sortingLayerID = reference.sortingLayerID、sortingOrder = reference.sortingOrder - 1；
// - 顶点每帧只写「量化到 1/32 格」的呈现坐标（模拟状态不量化）；复用 Il2CppStructArray，
//   零逐帧托管分配；无坐标扫描（两条链各 8 点）、无协程、无 Unity 物理组件、无碰撞；
// - reference 只读（enabled/color.a/sorting/flip/layer），绝不读也绝不写 forceRenderingOff；
//   可见性由 caller 显式传入 visible（reference 被本模组隐藏时不会被误伤）。
//
// 调用方（HeroArcherVisuals）建议接线：
//   var cloth = HeroArcherCloth.Create(own.transform.parent, own);            // own = 自有 SpriteRenderer
//   float worldVx = (own.transform.position.x - state.LastWorldX) / dt;
//   float localVx = HeroArcherClothMath.LocalVelocityXFromWorld(worldVx, own.transform.parent.lossyScale.x);
//   HeroArcherCloth.Tick(cloth, dt, localVx, own.enabled);
//   HeroArcherCloth.SetWind(cloth, smoothedWind);   // World.WindSpeedCapped 采样后（operator 4Hz）平滑成 -1..1
//   // SetWind 只描述「视觉方向/强度」，不是物理 m/s；本类不引用任何游戏 World。
//   // Tick 必须**每帧恰好一次**（由 LateUpdate 驱动，全局 visual Sync 一帧至多一次），
//   // 不得从 OnShot 等事件里额外调用：固定步的 catch-up 账目按「每帧一次」设计。
//   // 移除/换场/池复用：HeroArcherCloth.Destroy(cloth);

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class HeroArcherCloth
{
    internal const int ChainCount = 2;

    /// <summary>肩部相对 sprite pivot 的偏移（像素；pivot 在脚，肩高 14px、中轴后 5px）。</summary>
    private const float ShoulderOffsetPixelsX = -5f;
    private const float ShoulderOffsetPixelsY = HeroArcherClothMath.ShoulderHeightPixels;

    private const float PrimaryLengthPixels = 24f;
    private const float SecondaryLengthPixels = 22f;

    /// <summary>两条链的锚点错开（后 1px、上 3px），加宽后仍保留两个尾部轮廓。</summary>
    private const float PrimaryAnchorPixelsX = 0f;
    private const float PrimaryAnchorPixelsY = 0f;
    private const float SecondaryAnchorPixelsX = -1f;
    private const float SecondaryAnchorPixelsY = 3f;

    /// <summary>两条链的风相位/拖曳比例不同 → 不会齐刷刷同一条线。</summary>
    private const float PrimaryPhase = 0f;
    private const float SecondaryPhase = 1.1f;
    private const float PrimaryDragScale = 1f;
    private const float SecondaryDragScale = 0.9f;
    private const float PrimaryWindScale = 1f;
    private const float SecondaryWindScale = 1.15f;

    /// <summary>cloth 画在身体后（同 sortingLayer 内）：主链 -1、副链 -2，顺序确定不打架。</summary>
    private const int PrimarySortingOffset = 1;
    private const int SecondarySortingOffset = 2;

    private const string RootName = "KEM_HeroArcherCloth";
    private const string ChainName = "KEM_HeroArcherClothChain";
    private const string MeshName = "KEM_HeroArcherClothMesh";

    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int RendererColorProperty = Shader.PropertyToID("_RendererColor");
    private static readonly MaterialPropertyBlock Tint = new MaterialPropertyBlock();

    private static Material _material;
    private static Texture2D _whiteTexture;
    private static bool _materialFailed;
    private static bool _warnedMaterial;
    private static bool _warnedBuild;
    private static bool _warnedTick;

    /// <summary>一名英雄的红双飘带（自有 root + 2 条链渲染器 + 2 条链状态）。</summary>
    internal sealed class Handle
    {
        internal GameObject Root;
        internal Transform RootTransform;
        internal SpriteRenderer Reference;
        internal HeroArcherClothChain[] Chains = new HeroArcherClothChain[ChainCount];
        internal Mesh[] Meshes = new Mesh[ChainCount];
        internal MeshFilter[] Filters = new MeshFilter[ChainCount];
        internal MeshRenderer[] Renderers = new MeshRenderer[ChainCount];
        internal Il2CppStructArray<Vector3>[] Vertices = new Il2CppStructArray<Vector3>[ChainCount];
        internal float WindClock;
        internal float LastOrientation;
        internal bool AppliedSecondarySortingOrder;
        internal bool AppliedVisible;
        internal float AppliedAlpha = float.NaN;
        internal int AppliedSortingLayer = int.MinValue;
        internal int AppliedSortingOrder = int.MinValue;
        internal int AppliedGoLayer = int.MinValue;
        internal bool AppliedFlipX;
    }

    /// <summary>
    /// 为一名英雄建立自有红双飘带。任一步失败 → 清掉已建的自有对象并返回 null（fail-closed，绝不露出半成品）。
    /// 幂等性由 caller 保证（每名英雄一次）；池复用/换场请先 Destroy 再 Create。
    /// </summary>
    internal static Handle Create(Transform parent, SpriteRenderer reference)
    {
        try
        {
            if (parent == null || reference == null) return null;

            Vector3 referenceLocal;
            int sortingLayer;
            int sortingOrder;
            int goLayer;
            bool flipX;
            Color referenceColor;
            try
            {
                Transform referenceTransform = reference.transform;
                if (referenceTransform == null) return null;
                referenceLocal = referenceTransform.localPosition;
                sortingLayer = reference.sortingLayerID;
                sortingOrder = reference.sortingOrder;
                goLayer = reference.gameObject.layer;
                flipX = reference.flipX;
                referenceColor = reference.color;
            }
            catch (Exception)
            {
                return null;
            }

            Material material = ResolveMaterial(reference);
            if (material == null)
            {
                WarnOnce(ref _warnedMaterial, "cloth unavailable: no usable sprite shader");
                return null;
            }

            Handle handle = new Handle();
            handle.Reference = reference;
            // 地面在肩部 local -14/32；各链减去自身锚点偏移得到链 local 地面。
            handle.Chains[0] = new HeroArcherClothChain(
                PrimaryLengthPixels / HeroArcherClothMath.PixelsPerUnit, PrimaryPhase, PrimaryDragScale, PrimaryWindScale,
                GroundYForAnchor(PrimaryAnchorPixelsY));
            handle.Chains[1] = new HeroArcherClothChain(
                SecondaryLengthPixels / HeroArcherClothMath.PixelsPerUnit, SecondaryPhase, SecondaryDragScale, SecondaryWindScale,
                GroundYForAnchor(SecondaryAnchorPixelsY));

            try
            {
                GameObject root = new GameObject(RootName);
                handle.Root = root;
                Transform rootTransform = root.transform;
                handle.RootTransform = rootTransform;
                rootTransform.SetParent(parent, false);
                rootTransform.localPosition = new Vector3(
                    referenceLocal.x + (flipX ? -ShoulderOffsetPixelsX : ShoulderOffsetPixelsX) / HeroArcherClothMath.PixelsPerUnit,
                    referenceLocal.y + ShoulderOffsetPixelsY / HeroArcherClothMath.PixelsPerUnit,
                    referenceLocal.z);
                rootTransform.localRotation = Quaternion.identity;
                rootTransform.localScale = flipX ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                root.layer = goLayer;

                for (int c = 0; c < ChainCount; c++)
                {
                    float anchorPixelsX = c == 0 ? PrimaryAnchorPixelsX : SecondaryAnchorPixelsX;
                    float anchorPixelsY = c == 0 ? PrimaryAnchorPixelsY : SecondaryAnchorPixelsY;
                    int sortingOffset = c == 0 ? PrimarySortingOffset : SecondarySortingOffset;

                    GameObject chainObject = new GameObject(ChainName);
                    Transform chainTransform = chainObject.transform;
                    chainTransform.SetParent(rootTransform, false);
                    chainTransform.localPosition = new Vector3(
                        anchorPixelsX / HeroArcherClothMath.PixelsPerUnit,
                        anchorPixelsY / HeroArcherClothMath.PixelsPerUnit,
                        0f);
                    chainTransform.localRotation = Quaternion.identity;
                    chainTransform.localScale = Vector3.one;
                    chainObject.layer = goLayer;

                    MeshFilter filter = chainObject.AddComponent<MeshFilter>();
                    MeshRenderer renderer = chainObject.AddComponent<MeshRenderer>();
                    Mesh mesh = new Mesh();
                    handle.Meshes[c] = mesh; // Own immediately, including partial initialization failures.
                    mesh.name = MeshName;
                    mesh.MarkDynamic();

                    Il2CppStructArray<Vector3> vertices = new Il2CppStructArray<Vector3>(HeroArcherScarfGeometry.VertexCount);
                    handle.Vertices[c] = vertices;
                    mesh.vertices = vertices; // 空网格会拒绝 colors/triangles：先喂顶点
                    mesh.colors = BuildColors(c);
                    mesh.triangles = BuildTriangles();
                    mesh.bounds = new Bounds(new Vector3(0f, -0.4f, 0f), new Vector3(3f, 3f, 1f)); // 固定包围盒：无逐帧 RecalculateBounds

                    filter.sharedMesh = mesh;
                    renderer.sharedMaterial = material;
                    renderer.sortingLayerID = sortingLayer;
                    renderer.sortingOrder = sortingOrder - sortingOffset;
                    renderer.enabled = false;

                    handle.Meshes[c] = mesh;
                    handle.Filters[c] = filter;
                    handle.Renderers[c] = renderer;
                    WriteMesh(handle, c);
                }
            }
            catch (Exception e)
            {
                WarnOnce(ref _warnedBuild, "cloth build failed: " + e.GetType().Name);
                Destroy(handle);
                return null;
            }

            handle.AppliedSortingLayer = sortingLayer;
            handle.AppliedSortingOrder = sortingOrder - PrimarySortingOffset;
            handle.AppliedGoLayer = goLayer;
            handle.AppliedFlipX = flipX;
            ApplyAlpha(handle, referenceColor.a);
            return handle;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 每帧同步（由 HeroArcherVisuals 的 LateUpdate 调用）：
    /// 推进两条链 → 重量化后的顶点 → 照搬 reference 的 enabled/alpha/sorting/layer/flip。
    /// </summary>
    /// <param name="dt">自上次 Tick 的秒数（非法值只在纯逻辑里被忽略；风钟也只在合法时推进）。</param>
    /// <param name="localVelocity">角色实际速度在父 local 的 X 分量（见 HeroArcherClothMath.LocalVelocityXFromWorld）。</param>
    /// <param name="visible">caller 明确给出的可见性（例如自有 SpriteRenderer.enabled）。</param>
    internal static void Tick(Handle handle, float dt, float localVelocity, bool visible)
    {
        if (handle == null) return;
        try
        {
            SpriteRenderer reference = handle.Reference;
            if (handle.Root == null || reference == null)
            {
                Destroy(handle);
                return;
            }

            if (!visible)
            {
                SetVisible(handle, false);
                return; // 隐藏期间冻结：不推进、不写原生字段
            }

            Color color;
            int sortingLayer;
            int sortingOrder;
            int secondarySortingOrder;
            int goLayer;
            bool flipX;
            try
            {
                color = reference.color;
                sortingLayer = reference.sortingLayerID;
                sortingOrder = reference.sortingOrder - PrimarySortingOffset;
                secondarySortingOrder = reference.sortingOrder - SecondarySortingOffset;
                goLayer = reference.gameObject.layer;
                flipX = reference.flipX;
            }
            catch (Exception)
            {
                Destroy(handle);
                return;
            }

            if (HeroArcherClothMath.IsFinite(dt) && dt > 0f)
            {
                float step = dt > HeroArcherClothChain.MaxTickDelta ? HeroArcherClothChain.MaxTickDelta : dt;
                handle.WindClock += step;
                if (!HeroArcherClothMath.IsFinite(handle.WindClock)) handle.WindClock = 0f;
            }

            HeroArcherClothChain primary = handle.Chains[0];
            HeroArcherClothChain secondary = handle.Chains[1];
            if (primary == null || secondary == null)
            {
                Destroy(handle);
                return;
            }
            float simulationVelocity = flipX ? -localVelocity : localVelocity;
            primary.Step(dt, simulationVelocity, handle.WindClock);
            secondary.Step(dt, simulationVelocity, handle.WindClock);

            WriteMesh(handle, 0);
            WriteMesh(handle, 1);
            ApplyAlpha(handle, color.a);
            ApplySorting(handle, sortingLayer, sortingOrder, secondarySortingOrder);
            ApplyLayer(handle, goLayer);
            ApplyFlip(handle, flipX);
            SetVisible(handle, true);
        }
        catch (Exception e)
        {
            WarnOnce(ref _warnedTick, "cloth tick failed: " + e.GetType().Name);
            Destroy(handle);
        }
    }

    /// <summary>
    /// 原生世界风输入（视觉方向/强度，不是物理速度）：转发给两条链，signedNormalized ∈ [-1,1]
    /// （正/负 = 吹向父 local +x/−x）。NaN/±Inf 忽略；链内部再平滑，不产生跳变或爆炸。
    /// </summary>
    internal static void Reorient(Handle handle, float parentScaleX)
    {
        if (handle == null || !HeroArcherClothMath.IsFinite(parentScaleX) || Math.Abs(parentScaleX)<1e-5f || handle.Reference==null) return;
        bool flip=handle.Reference.flipX;
        float orientation=parentScaleX*(flip?-1f:1f);
        float old=handle.LastOrientation;
        if (old!=0f && old*orientation<0f)
        {
            float ratio=old/orientation;
            for (int i=0;i<ChainCount;i++)
            {
                float anchorX=(ShoulderOffsetPixelsX+(i==0?PrimaryAnchorPixelsX:SecondaryAnchorPixelsX))/32f;
                handle.Chains[i]?.Rebase(ratio,(ratio-1f)*anchorX);
            }
        }
        handle.LastOrientation=orientation;
        ApplyFlip(handle,flip);
    }

    internal static void SetWind(Handle handle, float signedNormalized)
    {
        if (handle == null) return;
        try
        {
            HeroArcherClothChain primary = handle.Chains[0];
            HeroArcherClothChain secondary = handle.Chains[1];
            if (primary != null) primary.SetWind(signedNormalized);
            if (secondary != null) secondary.SetWind(signedNormalized);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>销毁自有对象（root + 两条自有 mesh；共享材质/白纹理保留给其余英雄）。</summary>
    internal static void Destroy(Handle handle)
    {
        if (handle == null) return;
        try
        {
            GameObject root = handle.Root;
            handle.Root = null;
            handle.RootTransform = null;
            handle.Reference = null;
            for (int c = 0; c < ChainCount; c++)
            {
                Mesh mesh = handle.Meshes[c];
                handle.Meshes[c] = null;
                handle.Filters[c] = null;
                handle.Renderers[c] = null;
                handle.Vertices[c] = null;
                handle.Chains[c] = null;
                DestroyOwn(mesh); // new Mesh() 不随 GameObject 释放
            }
            DestroyOwn(root);
        }
        catch (Exception)
        {
        }
    }

    // ============================================================
    // 顶点写入（每帧：只写量化后的呈现坐标）
    // ============================================================

    /// <summary>Seven segments, two flat-color faces each; all buffers are created once.</summary>
    private static void WriteMesh(Handle handle, int index)
    {
        HeroArcherClothChain chain = handle.Chains[index];
        Il2CppStructArray<Vector3> vertices = handle.Vertices[index];
        if (chain == null || vertices == null) return;
        for (int segment = 0; segment < HeroArcherClothChain.NodeCount - 1; segment++)
        {
            if (!HeroArcherScarfGeometry.TrySegment(chain.PointsX, chain.PointsY, segment, index, handle.WindClock, out var band,
                attachmentBridge: index == 1))
                continue; // A transient collapsed segment retains its last valid face.
            int start = segment * HeroArcherScarfGeometry.VerticesPerSegment;
            WritePoint(vertices, start, band.StartOuter);
            WritePoint(vertices, start + 1, band.StartFold);
            WritePoint(vertices, start + 2, band.EndOuter);
            WritePoint(vertices, start + 3, band.EndFold);
            WritePoint(vertices, start + 4, band.StartFold);
            WritePoint(vertices, start + 5, band.StartInner);
            WritePoint(vertices, start + 6, band.EndFold);
            WritePoint(vertices, start + 7, band.EndInner);
        }
        handle.Meshes[index].vertices = vertices;
    }

    private static void WritePoint(Il2CppStructArray<Vector3> vertices, int index, HeroArcherScarfGeometry.Point point)
        => vertices[index] = new Vector3(point.X, point.Y, 0f);

    private static Il2CppStructArray<Color> BuildColors(int index)
    {
        var colors = new Il2CppStructArray<Color>(HeroArcherScarfGeometry.VertexCount);
        for (int segment = 0; segment < HeroArcherClothChain.NodeCount - 1; segment++)
        {
            for (int face = 0; face < HeroArcherScarfGeometry.FacesPerSegment; face++)
            {
                int rgb = HeroArcherScarfGeometry.FaceRgb(index, segment, face);
                Color color = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
                int start = segment * HeroArcherScarfGeometry.VerticesPerSegment + face * 4;
                for (int corner = 0; corner < 4; corner++) colors[start + corner] = color;
            }
        }
        return colors;
    }

    private static Il2CppStructArray<int> BuildTriangles()
    {
        int faces = (HeroArcherClothChain.NodeCount - 1) * HeroArcherScarfGeometry.FacesPerSegment;
        var triangles = new Il2CppStructArray<int>(faces * 6);
        for (int face = 0; face < faces; face++)
        {
            int a = face * 4, index = face * 6;
            triangles[index] = a;
            triangles[index + 1] = a + 1;
            triangles[index + 2] = a + 2;
            triangles[index + 3] = a + 2;
            triangles[index + 4] = a + 1;
            triangles[index + 5] = a + 3;
        }
        return triangles;
    }

    // ============================================================
    // reference 同步（只读原生，只在变化时写自有对象）
    // ============================================================

    private static void ApplyAlpha(Handle handle, float alpha)
    {
        if (!HeroArcherClothMath.IsFinite(alpha)) return;
        if (handle.AppliedAlpha == alpha) return;
        handle.AppliedAlpha = alpha;
        Tint.SetColor(ColorProperty, new Color(1f, 1f, 1f, alpha));
        Tint.SetColor(RendererColorProperty, new Color(1f, 1f, 1f, alpha));
        for (int c = 0; c < ChainCount; c++)
        {
            MeshRenderer renderer = handle.Renderers[c];
            if (renderer != null) renderer.SetPropertyBlock(Tint);
        }
    }

    private static void ApplySorting(Handle handle, int sortingLayer, int primarySortingOrder, int secondarySortingOrder)
    {
        if (handle.AppliedSortingLayer == sortingLayer && handle.AppliedSortingOrder == primarySortingOrder
            && handle.AppliedSecondarySortingOrder == (secondarySortingOrder == primarySortingOrder - 1)) return;
        handle.AppliedSortingLayer = sortingLayer;
        handle.AppliedSortingOrder = primarySortingOrder;
        handle.AppliedSecondarySortingOrder = secondarySortingOrder == primarySortingOrder - 1;
        for (int c = 0; c < ChainCount; c++)
        {
            MeshRenderer renderer = handle.Renderers[c];
            if (renderer == null) continue;
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = c == 0 ? primarySortingOrder : secondarySortingOrder;
        }
    }

    private static void ApplyLayer(Handle handle, int goLayer)
    {
        if (handle.AppliedGoLayer == goLayer) return;
        handle.AppliedGoLayer = goLayer;
        GameObject root = handle.Root;
        if (root != null) root.layer = goLayer;
        for (int c = 0; c < ChainCount; c++)
        {
            MeshRenderer renderer = handle.Renderers[c];
            if (renderer != null && renderer.gameObject != null) renderer.gameObject.layer = goLayer;
        }
    }

    private static void ApplyFlip(Handle handle, bool flipX)
    {
        if (handle.AppliedFlipX == flipX) return;
        handle.AppliedFlipX = flipX;
        Transform rootTransform = handle.RootTransform;
        if (rootTransform != null)
        {
            rootTransform.localScale = flipX ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            Vector3 origin = handle.Reference.transform.localPosition;
            rootTransform.localPosition = new Vector3(origin.x + (flipX ? -ShoulderOffsetPixelsX : ShoulderOffsetPixelsX) / 32f, origin.y + ShoulderOffsetPixelsY / 32f, origin.z);
        }
    }

    private static void SetVisible(Handle handle, bool visible)
    {
        if (handle.AppliedVisible == visible) return;
        handle.AppliedVisible = visible;
        for (int c = 0; c < ChainCount; c++)
        {
            MeshRenderer renderer = handle.Renderers[c];
            if (renderer != null) renderer.enabled = visible;
        }
    }

    // ============================================================
    // 共享资源（一次构建，所有英雄共用；绝不借用/修改原生材质与纹理）
    // ============================================================

    private static Material ResolveMaterial(SpriteRenderer reference)
    {
        Material material = _material;
        if (material != null) return material;
        if (_materialFailed) return null;
        try
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                _materialFailed = true;
                return null;
            }

            Texture2D white = ResolveWhiteTexture();
            if (white == null)
            {
                _materialFailed = true;
                return null;
            }

            material = new Material(shader);
            // Publish only after every required property is initialized.
            material.name = "KEM_HeroArcherClothMaterial";
            material.mainTexture = white; // 严禁采样英雄图集：1x1 白纹理 + 顶点色
            material.color = Color.white;
            material.renderQueue = 3000; // 与 sprite 同队列，靠 sortingOrder 落在身体后
            _material = material;
            return material;
        }
        catch (Exception)
        {
            _materialFailed = true;
            DestroyOwn(material);
            return null;
        }
    }

    private static Texture2D ResolveWhiteTexture()
    {
        Texture2D texture = _whiteTexture;
        if (texture != null) return texture;
        try
        {
            texture = new Texture2D(1, 1);
            // Publish only after initialization.
            texture.name = "KEM_HeroArcherClothWhite";
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            _whiteTexture = texture;
            return texture;
        }
        catch (Exception)
        {
            DestroyOwn(texture);
            return null;
        }
    }

    /// <summary>链 local 地面 Y：肩部 local -14/32 减去该链锚点相对肩部的 y 偏移。</summary>
    private static float GroundYForAnchor(float anchorPixelsY)
    {
        return (-HeroArcherClothMath.ShoulderHeightPixels - anchorPixelsY) / HeroArcherClothMath.PixelsPerUnit;
    }

    private static void DestroyOwn(UnityEngine.Object target)
    {
        if (target == null) return;
        try
        {
            UnityEngine.Object.Destroy(target);
        }
        catch (Exception)
        {
        }
    }

    private static void WarnOnce(ref bool warned, string message)
    {
        if (warned) return;
        warned = true;
        try
        {
            Debug.LogWarning("[KEM] " + message);
        }
        catch (Exception)
        {
        }
    }
}
