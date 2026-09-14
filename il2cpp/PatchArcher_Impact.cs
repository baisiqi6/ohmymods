using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弓箭命中火焰特效（纯装饰，默认关闭；ArcherImpactEnabled）。
///
/// 视觉来源：原作者真实 DLL（<c>Arrow.HitObject</c> 的内联 delegate + 嵌套
/// <c>PixelFireAnimator</c>）的几何/动画按源码迁移；排序继承箭矢 renderer、保持
/// 原地 z 为**明确兼容适配**（不恢复作者的全局 30000 排序与随机 z），不再使用
/// LineRenderer 空心圈：
/// 每层 5x5 像素格 → 36 顶点 / 150 索引，格点 x=k*.22-.55、y=j*.22-.55 加
/// ±.022 抖动，UV=(k/5,j/5)，每格两三角 (v00,v10,v01)+(v01,v10,v11)；
/// 逐帧 PerlinNoise(i*.1,Time.time*5) 与 PerlinNoise(i*.1+100,Time.time*5)
/// 取 ±1 后乘 (1-t)*.1*.22 偏移，再 Round(coord/.22)*.22 像素对齐；
/// EaseOutQuad(min(t,.8)) 把 scale 从 .01 长到 maxSize=.2*sizeRandom*layerSize；
/// 寿命 40% 后 EaseOutCubic 透明、RGB×Lerp(1,.5,f)、scale×Lerp(1,.8,f)。
/// 每层时长 = 命中类别 delay2（敌人 .5 / 其余 1）× 作者 animSpeed（核心 1.5-2、
/// 中 1、外 .6-.8），核心层最长，画到 alpha 0 即回池（不留作者多存 1.5-2x 的透明对象）。
///
/// 材质/纹理（自有、绝不借用箭矢 trail 共享材质）：每次池构建解析一次
/// Sprites/Default → Unlit/Texture（作者用 <c>??</c> 链），自建 1x1 白色 Point
/// 纹理一张，所有层材质共享它，逐层 material.color 控色（作者无有效 Emission）。
/// 借用/改写的来源全部移除；纹理与材质随 ReleaseAll 释放。
///
/// 原生所有权：Arrow.HitObject 只做前缀观察 + 后缀爆发——前缀记录命中前
/// <c>_hasHit</c> 状态、当前 Archer 归属、命中点与渲染层/排序；后缀仅当原生
/// 实际接受该次命中（<c>_hasHit</c> 由 false 变 true）时生成特效，并按 target
/// 复刻作者的命中类别（Wall/Crusher/Structure/Enemy/Ground）时长与色偏。
/// 不替换原生方法、不改伤害/弹跳/物理/消失时序、不碰原生 <c>_impactSpawner</c>
/// 粒子、不写原生 Arrow 的 scale/颜色/renderer 生命周期，不引入作者战斗副作用
/// （isFireArrow / trail 改色 / 火伤 / FireSplash 实例）。
///
/// 世界身份：池绑定当前可信 world/layer/scene（<see cref="ArcherOptionsScope.TryGetContext"/>）。
/// Tick 与命中路径每次都先核对，身份变化、无法解析（断层/主菜单）或 scope 关闭
/// 都立即释放旧池的全部自有对象（root/网格/材质/白纹理），绝不让旧世界资源跨世界存活。
///
/// 预算：本地固定 16 槽池（&lt;=16 同时存活），&lt;=24 个/秒（滑动窗口），
/// &lt;=4 次尝试/帧（失败也占额度），单次寿命 &lt;= 2s（delay2 1 × 核心 animSpeed 2）。
/// 任何失败（shader/纹理解析、池构建、复用时写入）自清理并退避 5s，绝不在同帧
/// 反复重建。网格/UV/索引/材质/纹理在槽建立时构造一次并复用；逐帧只做
/// Il2CppStructArray&lt;Vector3&gt; 索引填充 + mesh.vertices 原生 invoke
/// setter，无逐帧托管数组、无 Mesh/材质/纹理分配、无 Resources.Load、
/// 无 IL2CPP MonoBehaviour 子类（根面板 Update 驱动 <see cref="Tick"/>）。
/// </summary>
internal static class PatchArcher_Impact
{
    internal const int MaxEffects = 16;
    internal const int MaxSpawnsPerSecond = 24;
    internal const int MaxSpawnsPerFrame = 4;

    internal const int LayerCount = 3;
    internal const int GridCells = 5;
    internal const int GridPoints = GridCells + 1;
    internal const int VertexCount = GridPoints * GridPoints;   // 36
    internal const int IndexCount = GridCells * GridCells * 6;  // 150
    internal const float PixelSize = .22f;
    internal const float HalfExtent = GridCells * PixelSize * .5f;         // .55
    internal const float StartSize = .01f;
    internal const float MaxSizeBase = .2f;
    internal const float DurationEnemy = .5f;
    internal const float DurationDefault = 1f;
    /// <summary>作者核心层 animSpeed 上限 2 × 最长 delay2 1：池槽寿命上界。</summary>
    internal const float MaxEffectLifetime = DurationDefault * 2f;

    private const float GrowEnd = .8f;          // EaseOutQuad 生长到 80% 寿命后停
    private const float FadeStart = .4f;        // 40% 寿命后开始淡出
    private const float FadeDuration = .6f;
    private const float Jitter = .1f;           // 初始顶点抖动 ±(0.1 * pixelSize)
    private const float Drift = .1f;            // 逐帧偏移 (1-t) * 0.1 * pixelSize
    private const float PerlinStep = .1f;
    private const float PerlinRate = 5f;
    private const float PerlinOffsetY = 100f;
    private const float RetryBackoffSeconds = 5f;
    private const string PrimaryShader = "Sprites/Default";
    private const string FallbackShader = "Unlit/Texture";
    private const string RootName = "KEM_ArcherImpact";
    private const string TextureName = "KEM_ImpactPixel";
    private static readonly string[] LayerNames = { "KEM_ImpactCore", "KEM_ImpactMid", "KEM_ImpactOuter" };

    // 作者逐层参数（PixelFireAnimator 调用点）：强度、尺寸、时长倍率、RGB 乘子。
    internal static readonly float[] LayerIntensityMin = { 4f, 2.5f, 1.8f };
    internal static readonly float[] LayerIntensityMax = { 6f, 3.5f, 2.5f };
    internal static readonly float[] LayerSizeMin = { .6f, .9f, 1.3f };
    internal static readonly float[] LayerSizeMax = { .8f, 1.1f, 1.6f };
    internal static readonly float[] LayerSpeedMin = { 1.5f, 1f, .6f };
    internal static readonly float[] LayerSpeedMax = { 2f, 1f, .8f };
    internal static readonly float[] LayerTintR = { 1.3f, 1.1f, .9f };
    internal static readonly float[] LayerTintG = { 1.2f, .9f, .7f };
    internal static readonly float[] LayerTintB = { .7f, .4f, .3f };
    private static readonly int[] LayerOrderDelta = { 2, 1, 0 };

    private sealed class Slot
    {
        internal GameObject Root;
        internal float Born = -1f;
        internal float Lifetime;
        internal readonly GameObject[] Layers = new GameObject[LayerCount];
        internal readonly Transform[] Transforms = new Transform[LayerCount];
        internal readonly Mesh[] Meshes = new Mesh[LayerCount];
        internal readonly MeshFilter[] Filters = new MeshFilter[LayerCount];
        internal readonly MeshRenderer[] Renderers = new MeshRenderer[LayerCount];
        internal readonly Material[] Materials = new Material[LayerCount];
        internal readonly Il2CppStructArray<Vector3>[] Vertices = new Il2CppStructArray<Vector3>[LayerCount];
        internal readonly float[][] BaseXY = new float[LayerCount][];
        internal readonly float[] LayerLifetime = new float[LayerCount];
        internal readonly bool[] LayerDone = new bool[LayerCount];
        internal readonly float[] LayerMaxSize = new float[LayerCount];
        internal readonly Color[] LayerColor = new Color[LayerCount];
    }

    private static readonly Slot[] Slots = new Slot[MaxEffects];
    private static readonly float[] SpawnTimes = new float[MaxSpawnsPerSecond];
    private static readonly HashSet<string> LoggedOnce = new();

    private static Shader ImpactShader;
    private static string ShaderName = "unknown";
    private static Texture2D WhiteTexture;
    private static bool PixelFireLogged;
    private static float RetryAt;
    private static uint Rng = 0x9E3779B9u;
    private static int SpawnHead, SpawnCount, FrameStamp = int.MinValue, FrameAttempts;
    private static bool AnyBuilt;

    // 池身份：world/layer/scene 指针快照（与候选命中携带的身份比对）。
    private static IntPtr ContextWorld, ContextLayer;
    private static int ContextScene;
    private static bool ContextSet;

    static PatchArcher_Impact()
    {
        for (int i = 0; i < Slots.Length; i++) Slots[i] = new Slot();
    }

    /// <summary>前缀捕获（无分配、绝不抛异常；重复命中与越界世界在此就地短路）。</summary>
    internal struct HitCandidate
    {
        internal bool Valid;
        internal HitKind Kind;
        internal Vector3 Position;
        internal int GoLayer;
        internal int SortingLayerId;
        internal int SortingOrder;
        internal Archer Owner;
        internal Arrow Source;
        internal IntPtr World, Layer;
        internal int Scene;
        internal IntPtr ArrowPtr;
        internal int ArrowGoId;
    }

    /// <summary>作者按命中对象分流的火焰时长/色偏类别（wall/crusher/structure/enemy/ground）。</summary>
    internal enum HitKind { Wall, Crusher, Structure, Enemy, Ground }

    internal static void LogOnce(string key, string message)
    {
        if (!LoggedOnce.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[ArcherImpact] " + message); }
        catch { }
    }

    private static bool IsEnabled => ModConfig.ArcherImpactEnabled != null && ModConfig.ArcherImpactEnabled.Value;

    // ============================================================
    // 世界身份
    // ============================================================

    /// <summary>核对当前可信 world/layer/scene 并绑定到池；身份变化立即释放旧池。</summary>
    private static bool TryBindCurrentContext()
    {
        if (!ArcherOptionsScope.TryGetContext(out IntPtr world, out IntPtr layer, out int scene)) return false;
        if (ContextSet && (world != ContextWorld || layer != ContextLayer || scene != ContextScene))
            ReleaseAll(); // scope 仍为 true 也照样释放：绝不让旧世界资源存活
        ContextWorld = world;
        ContextLayer = layer;
        ContextScene = scene;
        ContextSet = true;
        return true;
    }

    // ============================================================
    // 命中确认（Harmony Prefix/Postfix 调用的两个入口）
    // ============================================================

    internal static HitCandidate Capture(Arrow arrow, GameObject target, bool physicalHit)
    {
        HitCandidate candidate = default;
        try
        {
            // 先做便宜且与命中对象无关的关卡，再读取 arrow 属性。
            if (!IsEnabled || !ArcherOptionsScope.IsActive) return candidate;
            if (!ArcherOptionsScope.TryGetContext(out IntPtr world, out IntPtr layer, out int scene)) return candidate;
            if (arrow == null) return candidate;
            if (!ArcherOptionsScope.IsCurrent(arrow)) return candidate; // 旧世界/旧层残留箭矢
            if (arrow._hasHit) return candidate;                        // 原生随即早退，不可能产生接受命中

            GameObject ownerGo = arrow.archer;
            Archer owner = ownerGo != null ? ownerGo.GetComponent<Archer>() : null;
            if (owner == null || !owner.isActiveAndEnabled || !ArcherOptionsScope.IsCurrent(owner)) return candidate;

            SpriteRenderer sprite = arrow._spriteRenderer;
            if (sprite == null)
            {
                // 原生 Awake 用 Require.Component 保证存在；缺失即无可信层/排序，宁可不画。
                LogOnce("renderer-missing", "arrow sprite renderer unavailable; impact burst skipped for this hit");
                return candidate;
            }

            candidate.World = world;
            candidate.Layer = layer;
            candidate.Scene = scene;
            candidate.ArrowPtr = arrow.Pointer;
            candidate.ArrowGoId = arrow.gameObject.GetInstanceID();
            candidate.Position = arrow.transform.position;
            candidate.GoLayer = arrow.gameObject.layer;
            candidate.SortingLayerId = sprite.sortingLayerID;
            candidate.SortingOrder = sprite.sortingOrder;
            candidate.Owner = owner;
            candidate.Source = arrow;
            candidate.Kind = Classify(target, physicalHit); // 命中当刻缓存类别：原生击杀/回池后再读 target 已不可信
            candidate.Valid = true;
        }
        catch (Exception e)
        {
            candidate.Valid = false;
            LogOnce("capture:" + e.GetType().Name, "impact capture failed: " + e.Message);
        }
        return candidate;
    }

    /// <summary>后缀入口：唯一接受证据是前缀未命中 + 原生返回后 <c>_hasHit</c> 为 true；
    /// 命中类别用前缀缓存的 <see cref="HitCandidate.Kind"/>，不再在后缀读 target。</summary>
    internal static void OnNativeHit(Arrow arrow, HitCandidate candidate)
    {
        try
        {
            if (!candidate.Valid || arrow == null || !arrow._hasHit) return;
            if (!IsEnabled || !ArcherOptionsScope.IsActive)
            {
                ReleaseAll(); // 关模组/无世界：命中路径同样立即释放旧池
                return;
            }
            if (!TryBindCurrentContext())
            {
                ReleaseAll(); // 无可信 world（断层/主菜单）：不保留旧池
                return;
            }
            // 命中与当前世界一致，且确实是同一个 arrow 实例（防失效 wrapper/池复用串位）。
            if (candidate.World != ContextWorld || candidate.Layer != ContextLayer || candidate.Scene != ContextScene) return;
            if (arrow.gameObject == null || arrow.Pointer != candidate.ArrowPtr
                || arrow.gameObject.GetInstanceID() != candidate.ArrowGoId) return;

            Archer owner = candidate.Owner;
            if (owner == null || owner.gameObject == null || !owner.isActiveAndEnabled
                || !ArcherOptionsScope.IsCurrent(owner)) return;

            float now = Time.time;
            int frame = Time.frameCount;
            if (frame != FrameStamp)
            {
                FrameStamp = frame;
                FrameAttempts = 0;
            }
            if (FrameAttempts >= MaxSpawnsPerFrame) return; // 压力下丢弃，绝不影响原生命中
            FrameAttempts++;                                // 失败尝试同样占本帧额度
            if (!AllowWindow(now)) return;
            if (!Spawn(candidate, candidate.Kind, now)) return; // Spawn 内部负责失败退避；仅池满时静默丢弃
            RecordSpawn(now);
        }
        catch (Exception e)
        {
            LogOnce("hit:" + e.GetType().Name, "impact burst failed: " + e.Message);
        }
    }

    /// <summary>
    /// 复刻作者 <c>HitObject</c> 的分支顺序（physicalHit+Wall → Crusher → Damageable
    /// 的 Structure/Flesh → 其余按 Ground），**在前缀、命中当刻**求值：原生击杀后目标会
    /// 死亡/回池/组件被摘除，后缀再读 target 会退化成 Ground。
    /// 只读探针：仅 <c>GetComponentInParent</c> 与 <c>IsStunned</c>/<c>surface</c> 读取，
    /// 不调用任何伤害或可伤判定（不复制原生战斗逻辑）。
    /// </summary>
    private static HitKind Classify(GameObject target, bool physicalHit)
    {
        if (target == null) return HitKind.Ground;
        try
        {
            if (physicalHit && target.GetComponentInParent<Wall>() != null) return HitKind.Wall;
            Crusher crusher = target.GetComponentInParent<Crusher>();
            if (crusher != null && !crusher.IsStunned) return HitKind.Crusher;
            Damageable damageable = target.GetComponentInParent<Damageable>();
            if (damageable != null)
                return damageable.surface == DamageSurface.Structure ? HitKind.Structure : HitKind.Enemy;
        }
        catch (Exception e)
        {
            LogOnce("classify:" + e.GetType().Name, "impact hit classification failed: " + e.Message);
        }
        return HitKind.Ground;
    }

    // ============================================================
    // 逐帧驱动（根面板 Update 调用；关闭/断世界时只做一次释放）
    // ============================================================

    internal static void Tick()
    {
        try
        {
            if (!IsEnabled || !ArcherOptionsScope.IsActive)
            {
                ReleaseAll();
                return;
            }
            if (!TryBindCurrentContext())
            {
                ReleaseAll();
                return;
            }
        }
        catch (Exception e)
        {
            ReleaseAll();
            LogOnce("tick:" + e.GetType().Name, "impact pool context check failed: " + e.Message);
            return;
        }

        float now = Time.time;
        for (int i = 0; i < Slots.Length; i++)
        {
            Slot slot = Slots[i];
            if (slot.Born < 0f) continue;
            if (slot.Root == null)
            {
                ReleaseSlot(i); // 宿主场景卸载带走了自建根对象：槽退回未建状态，下次重建
                continue;
            }
            float elapsed = now - slot.Born;
            if (elapsed >= slot.Lifetime)
            {
                FinishSlot(i);  // 作者动画走完（alpha 已到 0）：隐藏并保留资源供复用
                continue;
            }
            try { Pose(slot, elapsed); }
            catch (Exception e)
            {
                ReleaseSlot(i);
                RetryAt = now + RetryBackoffSeconds; // 复用失败也退避，避免同帧反复重建
                LogOnce("pose:" + e.GetType().Name, "impact burst animation failed: " + e.Message);
            }
        }
    }

    // ============================================================
    // 生成 / 池
    // ============================================================

    private static bool Spawn(HitCandidate candidate, HitKind kind, float now)
    {
        int index = AcquireSlot();
        if (index < 0) return false; // 池满：静默丢弃，不触发退避
        Slot slot = Slots[index];
        try
        {
            if (slot.Root == null && !BuildSlot(index)) return false;
            if (slot.Root == null) return false; // 构建失败已自清理

            Color baseColor = NewBaseColor(kind);
            float delay2 = kind == HitKind.Enemy ? DurationEnemy : DurationDefault;

            slot.Born = now;
            slot.Lifetime = delay2 * LayerSpeedMax[0]; // 按核心层时长上界回池；各层按实际时长提前隐藏
            for (int layer = 0; layer < LayerCount; layer++)
            {
                float animSpeed = NextRange(LayerSpeedMin[layer], LayerSpeedMax[layer]);
                float layerLifetime = delay2 * animSpeed;
                slot.LayerLifetime[layer] = layerLifetime;
                if (layerLifetime > slot.Lifetime) slot.Lifetime = layerLifetime;

                float sizeRandomness = NextRange(.7f, 1.3f);
                float layerSize = NextRange(LayerSizeMin[layer], LayerSizeMax[layer]);
                slot.LayerMaxSize[layer] = MaxSizeBase * sizeRandomness * layerSize;

                Color layerColor = baseColor;
                layerColor.r *= LayerTintR[layer];
                layerColor.g *= LayerTintG[layer];
                layerColor.b *= LayerTintB[layer];
                float intensity = NextRange(LayerIntensityMin[layer], LayerIntensityMax[layer]);
                layerColor.r *= intensity;
                layerColor.g *= intensity;
                layerColor.b *= intensity;
                layerColor.a = NextRange(.7f, .9f);
                slot.LayerColor[layer] = layerColor;

                // 作者：每命中新建网格（格点 + ±.022 抖动），此处按槽重掷同一公式。
                float[] baseXY = slot.BaseXY[layer];
                for (int j = 0; j <= GridCells; j++)
                {
                    for (int k = 0; k <= GridCells; k++)
                    {
                        int vertex = j * GridPoints + k;
                        baseXY[vertex * 2] = k * PixelSize - HalfExtent + NextRange(-PixelSize * Jitter, PixelSize * Jitter);
                        baseXY[vertex * 2 + 1] = j * PixelSize - HalfExtent + NextRange(-PixelSize * Jitter, PixelSize * Jitter);
                    }
                }
                WriteVertices(slot, layer, 0f);
                slot.Meshes[layer].RecalculateBounds();
                slot.Materials[layer].color = layerColor;                          // 作者在构建点即写 material.color
                slot.Transforms[layer].localScale = new Vector3(StartSize * sizeRandomness * layerSize,
                                                               StartSize * sizeRandomness * layerSize, 1f);

                MeshRenderer renderer = slot.Renderers[layer];
                slot.LayerDone[layer] = false; // 复用槽重新点亮所有层（Pose 会在各层超时后显式关闭）
                slot.Layers[layer].layer = candidate.GoLayer;
                renderer.sortingLayerID = candidate.SortingLayerId;
                renderer.sortingOrder = AddOrder(candidate.SortingOrder, LayerOrderDelta[layer]);
                renderer.enabled = true;
            }

            GameObject root = slot.Root;
            root.layer = candidate.GoLayer;
            root.transform.position = candidate.Position; // 作者不随机旋转：像素格保持横平竖直
            root.SetActive(true);
            LogPixelFireReady(slot);
            return true;
        }
        catch (Exception e)
        {
            ReleaseSlot(index); // 半成品自清理，绝不留半个特效
            RetryAt = now + RetryBackoffSeconds;
            LogOnce("spawn:" + e.GetType().Name, "impact burst spawn failed: " + e.Message);
            return false;
        }
    }

    /// <summary>首次真正生成成功后记录一次实际状态（shader/网格顶点与索引/白纹理），
    /// 供 root 从游戏日志确认网格确实上传成功，而不只是 shader 存在。</summary>
    private static void LogPixelFireReady(Slot slot)
    {
        if (PixelFireLogged) return;
        PixelFireLogged = true;
        try
        {
            Mesh mesh = slot.Meshes[0];
            Texture2D texture = WhiteTexture;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[ArcherImpact] pixel fire ready: shader=" + ShaderName
                + " vertexCount=" + mesh.vertexCount + " indices=" + mesh.triangles.Length
                + " texture=" + (texture != null ? texture.width + "x" + texture.height : "none")
                + " filter=" + (texture != null ? texture.filterMode.ToString() : "none"));
        }
        catch { }
    }

    private static int AcquireSlot()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            Slot slot = Slots[i];
            if (slot.Born >= 0f)
            {
                if (slot.Root != null) continue; // 仍在淡出
                ReleaseSlot(i);                  // 根对象被外部销毁：清掉残留引用
            }
            return i;
        }
        return -1;
    }

    private static bool BuildSlot(int index)
    {
        if (Time.time < RetryAt) return false;
        Shader shader = ResolveShader();
        if (shader == null) return false;
        Texture2D texture = ResolveWhiteTexture();
        if (texture == null) return false;

        Slot slot = Slots[index];
        GameObject root = null;
        try
        {
            root = new GameObject(RootName);
            root.SetActive(false);
            for (int layer = 0; layer < LayerCount; layer++)
            {
                GameObject go = new GameObject(LayerNames[layer]);
                slot.Layers[layer] = go; // 先登记：SetParent/AddComponent 失败也能清到这个 child
                go.transform.SetParent(root.transform, false);
                slot.Transforms[layer] = go.transform;
                MeshFilter filter = go.AddComponent<MeshFilter>();
                slot.Filters[layer] = filter;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                slot.Renderers[layer] = renderer;

                Mesh mesh = new Mesh();
                slot.Meshes[layer] = mesh;
                slot.Vertices[layer] = new Il2CppStructArray<Vector3>(VertexCount); // 逐帧复用的原生数组（强 root）
                slot.BaseXY[layer] = new float[VertexCount * 2];
                // 空网格 vertexCount 为 0 时 uv/indices 会被拒绝：先喂入 36 个顶点（零值，命中时再写实际格点）。
                mesh.vertices = slot.Vertices[layer];
                FillUv(mesh);
                FillTriangles(mesh);

                Material material = new Material(shader);
                slot.Materials[layer] = material; // 立刻登记：后续 name/mainTexture/SetInt 抛异常也能被 cleanup
                material.name = LayerNames[layer];
                material.mainTexture = texture;      // 共享的唯一自有白纹理，不新建/不借用其他纹理
                material.SetInt("_ZWrite", 0);       // 沿用作者写法；真实效果以运行期解析到的 shader 为准
                material.SetInt("_ZTest", 8);
                material.SetInt("_SrcBlend", 5);
                material.SetInt("_DstBlend", 1);
                material.renderQueue = 3000;
                renderer.sharedMaterial = material;  // 自有材质：绝不触碰箭矢 trail 的共享材质
                renderer.enabled = false;
                filter.sharedMesh = mesh;            // sharedMesh：避免 mesh 实例克隆
            }
            slot.Root = root;
            AnyBuilt = true;
            return true;
        }
        catch (Exception e)
        {
            // 逐个清理本次新建的 child（含尚未挂上 root 的孤儿）与自有材质/网格，再清 root。
            for (int layer = 0; layer < LayerCount; layer++) ReleaseLayer(slot, layer);
            slot.Root = null;
            DestroyOwn(root);
            RetryAt = Time.time + RetryBackoffSeconds;
            LogOnce("build:" + e.GetType().Name, "impact pool build failed: " + e.Message);
            return false;
        }
    }

    /// <summary>UV=(k/5,j/5)（作者原式），只在建槽时写一次。</summary>
    private static void FillUv(Mesh mesh)
    {
        Il2CppStructArray<Vector2> uv = new Il2CppStructArray<Vector2>(VertexCount);
        for (int j = 0; j <= GridCells; j++)
            for (int k = 0; k <= GridCells; k++)
                uv[j * GridPoints + k] = new Vector2((float)k / GridCells, (float)j / GridCells);
        mesh.uv = uv;
    }

    /// <summary>作者索引顺序：(v00,v10,v01)+(v01,v10,v11)，5x5 格 = 150 索引，建槽时写一次。</summary>
    private static void FillTriangles(Mesh mesh)
    {
        Il2CppStructArray<int> indices = new Il2CppStructArray<int>(IndexCount);
        int next = 0;
        for (int l = 0; l < GridCells; l++)
        {
            for (int m = 0; m < GridCells; m++)
            {
                int v00 = l * GridPoints + m;
                int v01 = v00 + 1;
                int v10 = (l + 1) * GridPoints + m;
                int v11 = v10 + 1;
                indices[next++] = v00;
                indices[next++] = v10;
                indices[next++] = v01;
                indices[next++] = v01;
                indices[next++] = v10;
                indices[next++] = v11;
            }
        }
        mesh.triangles = indices;
    }

    /// <summary>共享 shader 解析（作者 Sprites/Default ?? Unlit/Texture）；解析结果记名一次，
    /// 供 root 按实际可用 shader 验证。</summary>
    private static Shader ResolveShader()
    {
        if (ImpactShader != null) return ImpactShader;
        if (Time.time < RetryAt) return null;
        try
        {
            Shader shader = Shader.Find(PrimaryShader);
            string name = PrimaryShader;
            if (shader == null)
            {
                shader = Shader.Find(FallbackShader);
                name = FallbackShader;
            }
            if (shader != null)
            {
                ImpactShader = shader;
                ShaderName = name;
                LogOnce("shader:" + name, "impact fire using shader '" + name + "'");
                return ImpactShader;
            }
        }
        catch (Exception e)
        {
            LogOnce("shader:" + e.GetType().Name, "impact shader lookup failed: " + e.Message);
        }
        RetryAt = Time.time + RetryBackoffSeconds;
        LogOnce("shader-unavailable", "neither " + PrimaryShader + " nor " + FallbackShader
            + " resolved; impact fire disabled for this world");
        return null;
    }

    /// <summary>唯一自有 1x1 白纹理（作者运行时 new Texture2D(1,1) + SetPixel + Apply + Point）。
    /// 一次构建、所有层材质共享；ReleaseAll 释放，绝不加载外部贴图文件。</summary>
    private static Texture2D ResolveWhiteTexture()
    {
        if (WhiteTexture != null) return WhiteTexture;
        if (Time.time < RetryAt) return null;
        try
        {
            Texture2D texture = new Texture2D(1, 1);
            WhiteTexture = texture; // 先强 root：SetPixel/Apply 抛异常时也不会漏掉这张白图
            texture.name = TextureName;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            texture.filterMode = FilterMode.Point; // 作者 mainTexture.filterMode = 0
            return WhiteTexture;
        }
        catch (Exception e)
        {
            Texture2D failed = WhiteTexture; // 失败即丢弃局部对象，绝不留半初始化的纹理给下次构建
            WhiteTexture = null;
            DestroyOwn(failed);
            RetryAt = Time.time + RetryBackoffSeconds;
            LogOnce("texture:" + e.GetType().Name, "impact white texture creation failed: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 作者逐帧顶点式（PixelFireAnimator.Update）：Perlin 噪声 ±1 × (1-t)*.1*.22 偏移，
    /// 加到原格点上再 Round(coord/.22)*.22 对齐像素。写入缓存的 Il2CppStructArray，
    /// 再由 mesh.vertices 原生 invoke setter 上传；无托管数组、无 Mesh/材质分配。
    /// </summary>
    private static void WriteVertices(Slot slot, int layer, float drift)
    {
        float[] baseXY = slot.BaseXY[layer];
        Il2CppStructArray<Vector3> vertices = slot.Vertices[layer];
        float time = Time.time * PerlinRate;
        for (int i = 0; i < VertexCount; i++)
        {
            float x = baseXY[i * 2];
            float y = baseXY[i * 2 + 1];
            if (drift != 0f)
            {
                x += (Mathf.PerlinNoise(i * PerlinStep, time) * 2f - 1f) * drift;
                y += (Mathf.PerlinNoise(i * PerlinStep + PerlinOffsetY, time) * 2f - 1f) * drift;
            }
            vertices[i] = new Vector3(Mathf.Round(x / PixelSize) * PixelSize, Mathf.Round(y / PixelSize) * PixelSize, 0f);
        }
        slot.Meshes[layer].vertices = vertices;
    }

    /// <summary>逐帧只写网格顶点（原生数组）+ 缩放/材质色；无分配。</summary>
    private static void Pose(Slot slot, float elapsed)
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            float t = elapsed / slot.LayerLifetime[layer];
            if (t >= 1f)
            {
                // 低帧率跨过该层 duration 时，上一帧的 alpha 仍可能残留：显式关闭该层。
                if (!slot.LayerDone[layer])
                {
                    slot.LayerDone[layer] = true;
                    slot.Renderers[layer].enabled = false;
                }
                continue; // 作者 animator：超过自身 duration 即停写
            }

            WriteVertices(slot, layer, (1f - t) * Drift * PixelSize);
            slot.Meshes[layer].RecalculateBounds();

            float grow = t < GrowEnd ? t : GrowEnd;
            grow = EaseOutQuad(grow);
            float size = StartSize + (slot.LayerMaxSize[layer] - StartSize) * grow;

            float fade = t > FadeStart ? (t - FadeStart) / FadeDuration : 0f;
            if (fade > 1f) fade = 1f;
            float alpha = 1f - EaseOutCubic(fade);
            float brightness = 1f - .5f * fade;
            float reduction = 1f - .2f * fade;

            // 作者：localScale = (size, size, 1) 后整向量 ×= reduction。
            float scale = size * reduction;
            slot.Transforms[layer].localScale = new Vector3(scale, scale, reduction);

            Color color = slot.LayerColor[layer];
            color.r *= brightness;
            color.g *= brightness;
            color.b *= brightness;
            color.a *= alpha;
            slot.Materials[layer].color = color;
        }
    }

    private static float EaseOutQuad(float t) => t * (2f - t);

    private static float EaseOutCubic(float t)
    {
        float inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }

    private static void FinishSlot(int index)
    {
        Slot slot = Slots[index];
        for (int layer = 0; layer < LayerCount; layer++)
        {
            // 池回收同样清掉层的发光状态：绝不留“已隐藏但仍 enabled”的残影给下一次复用。
            slot.LayerDone[layer] = true;
            MeshRenderer renderer = slot.Renderers[layer];
            if (renderer != null) renderer.enabled = false;
        }
        if (slot.Root != null)
        {
            try { slot.Root.SetActive(false); }
            catch { }
        }
        slot.Born = -1f;
    }

    /// <summary>释放单层自有对象（GameObject/材质/网格/原生顶点数组），引用一律清空。</summary>
    private static void ReleaseLayer(Slot slot, int layer)
    {
        GameObject child = slot.Layers[layer];
        Material material = slot.Materials[layer];
        Mesh mesh = slot.Meshes[layer];
        slot.Layers[layer] = null;
        slot.Transforms[layer] = null;
        slot.Filters[layer] = null;
        slot.Renderers[layer] = null;
        slot.Materials[layer] = null;
        slot.Meshes[layer] = null;
        slot.Vertices[layer] = null;
        slot.BaseXY[layer] = null;
        DestroyOwn(child);
        DestroyOwn(material); // 自有材质：只有本模组销毁（本模组从不借用共享材质）
        DestroyOwn(mesh);     // 自有网格：new Mesh() 不随 GameObject 释放
    }

    /// <summary>退回未建状态并销毁自建对象（场景卸载/异常路径/关闭路径共用）。
    /// child 随 root 一并销毁，自有材质、网格与原生几何缓存逐个释放。</summary>
    private static void ReleaseSlot(int index)
    {
        Slot slot = Slots[index];
        slot.Born = -1f;
        GameObject root = slot.Root;
        slot.Root = null;
        for (int layer = 0; layer < LayerCount; layer++) ReleaseLayer(slot, layer);
        DestroyOwn(root);
    }

    private static void ReleaseAll()
    {
        if (!AnyBuilt && WhiteTexture == null && ImpactShader == null && RetryAt == 0f && !ContextSet) return;
        for (int i = 0; i < Slots.Length; i++)
            if (Slots[i].Root != null || Slots[i].Born >= 0f) ReleaseSlot(i);
        Texture2D texture = WhiteTexture;
        WhiteTexture = null;
        ImpactShader = null;
        AnyBuilt = false;
        RetryAt = 0f;
        ContextSet = false;
        SpawnHead = 0;
        SpawnCount = 0;
        FrameStamp = int.MinValue;
        FrameAttempts = 0;
        DestroyOwn(texture); // 唯一自有纹理：随池一起释放
    }

    private static void DestroyOwn(UnityEngine.Object obj)
    {
        if (obj == null) return;
        try { UnityEngine.Object.Destroy(obj); } catch { }
    }

    // ============================================================
    // 预算：1 秒滑动窗口 + 逐帧尝试上限
    // ============================================================

    private static bool AllowWindow(float now)
    {
        while (SpawnCount > 0)
        {
            if (now - SpawnTimes[SpawnHead] < 1f) break;
            SpawnHead = SpawnHead + 1 >= MaxSpawnsPerSecond ? 0 : SpawnHead + 1;
            SpawnCount--;
        }
        return SpawnCount < MaxSpawnsPerSecond;
    }

    private static void RecordSpawn(float now)
    {
        int tail = SpawnHead + SpawnCount;
        if (tail >= MaxSpawnsPerSecond) tail -= MaxSpawnsPerSecond;
        SpawnTimes[tail] = now;
        SpawnCount++;
    }

    private static int AddOrder(int order, int delta) => order > int.MaxValue - delta ? int.MaxValue : order + delta;

    /// <summary>命中底色：普通火焰黄橙 HSV 0–0.12（作者弓箭雕像门槛不移植，由本模组开关开启），
    /// 再按命中类别乘作者色偏系数。</summary>
    private static Color NewBaseColor(HitKind kind)
    {
        Color color = Color.HSVToRGB(NextRange(0f, .12f), NextRange(.8f, 1f), NextRange(.9f, 1.2f));
        color.a = NextRange(.8f, 1f);
        float variant = kind switch
        {
            HitKind.Enemy => NextRange(.9f, 1.1f),
            HitKind.Ground => NextRange(1f, 1.4f),
            _ => NextRange(.8f, 1.2f), // wall / crusher / structure 共用作者 arg
        };
        color.r *= variant;
        color.g *= variant;
        color.b *= variant;
        color.a *= variant;
        return color;
    }

    /// <summary>私有确定性 RNG：绝不消耗 UnityEngine.Random，避免扰动原生弹跳等随机序列。</summary>
    private static float Next01()
    {
        Rng = Rng * 1664525u + 1013904223u;
        return (Rng >> 8) * (1f / 16777216f);
    }

    private static float NextRange(float min, float max) => min + (max - min) * Next01();
}

/// <summary>Arrow.HitObject 观测宿主：前缀捕获（含命中当刻的类别）、后缀在原生接受命中后
/// 生成特效。前缀不改写原方法执行（无 __runOriginal），原生方法与其 Finalizer 语义不变。</summary>
[HarmonyPatch(typeof(Arrow), "HitObject")]
public static class Arrow_HitObject_ArcherImpact_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Arrow __instance, GameObject target, bool physicalHit, out PatchArcher_Impact.HitCandidate __state)
    {
        __state = PatchArcher_Impact.Capture(__instance, target, physicalHit);
    }

    [HarmonyPostfix]
    private static void Postfix(Arrow __instance, PatchArcher_Impact.HitCandidate __state)
    {
        PatchArcher_Impact.OnNativeHit(__instance, __state);
    }
}
