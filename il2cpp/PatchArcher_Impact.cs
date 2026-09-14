using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弓箭命中火焰特效（纯装饰，默认关闭；ArcherImpactEnabled）。
///
/// 原生所有权：Arrow.HitObject 只做前缀观察 + 后缀爆发——前缀记录命中前
/// <c>_hasHit</c> 状态、当前 Archer 归属、命中点与渲染层/排序；后缀仅当原生
/// 实际接受该次命中（<c>_hasHit</c> 由 false 变 true）时生成特效。不替换原生
/// 方法、不改伤害/弹跳/物理/消失时序，不碰原生 <c>_impactSpawner</c> 粒子、
/// 不写原生 Arrow 的 scale/颜色/renderer 生命周期。
///
/// 世界身份：池绑定当前可信 world/layer/scene（<see cref="ArcherOptionsScope.TryGetContext"/>）。
/// Tick 与命中路径每次都先核对，身份变化、无法解析（断层/主菜单）或 scope 关闭
/// 都立即释放旧池的全部自有对象，绝不让旧世界资源跨世界存活。
///
/// 预算：本地固定 16 槽池（<=16 同时存活），<=24 个/秒（滑动窗口），
/// <=4 次尝试/帧（失败也占额度），寿命 0.5s（<=0.6s）。任何失败（材质解析、
/// 池构建、复用时写入）自清理并退避 5s，绝不在同帧反复重建。每槽三层
/// LineRenderer 静态几何（每层 13 个索引点，仅在新槽建立时构造），逐帧只写
/// transform 缩放/线宽/顶点色，无逐帧数组、无逐次 Mesh/材质、无 Resources.Load、
/// 无 IL2CPP MonoBehaviour 子类（根面板 Update 驱动 <see cref="Tick"/>）。
/// </summary>
internal static class PatchArcher_Impact
{
    internal const int MaxEffects = 16;
    internal const int MaxSpawnsPerSecond = 24;
    internal const int MaxSpawnsPerFrame = 4;
    internal const float EffectLifetime = .5f;
    internal const int LayerCount = 3;
    internal const int RingPoints = 13;
    internal const int RingSegments = RingPoints - 1;

    private const float GrowEnd = .55f;
    private const float ScaleFloor = .42f;
    private const float RetryBackoffSeconds = 5f;
    private const float TwoPi = 6.2831855f;
    private const string FallbackShader = "Sprites/Default";
    private const string RootName = "KEM_ArcherImpact";
    private static readonly string[] LayerNames = { "KEM_ImpactCore", "KEM_ImpactMid", "KEM_ImpactOuter" };

    // 层 0 = 明亮厚核心（接近圆形），1 = 橙中层，2 = 稀薄锯齿外圈（强烈星形扭曲）。
    internal static readonly float[] LayerRadius = { .26f, .52f, .84f };
    internal static readonly float[] LayerWidth = { .17f, .10f, .048f };
    internal static readonly float[] LayerJag = { .07f, .2f, .42f };
    internal static readonly float[] LayerScale = { .78f, 1f, 1.22f };
    internal static readonly float[] LayerDelay = { 0f, .05f, .1f };
    internal static readonly float[] LayerAlpha = { .85f, .7f, .55f };
    internal static readonly Color[] LayerColor =
    {
        new Color(1f, .94f, .62f),
        new Color(1f, .52f, .14f),
        new Color(.92f, .17f, .04f),
    };
    private static readonly int[] LayerOrderDelta = { 2, 1, 0 };
    private static readonly float[] LayerPhase = { 0f, 2.1f, 4.35f };

    private sealed class Slot
    {
        internal GameObject Root;
        internal float Born = -1f;
    }

    private static readonly Slot[] Slots = new Slot[MaxEffects];
    private static readonly GameObject[] LayerObjects = new GameObject[MaxEffects * LayerCount];
    private static readonly LineRenderer[] LayerRenderers = new LineRenderer[MaxEffects * LayerCount];
    private static readonly float[] SpawnTimes = new float[MaxSpawnsPerSecond];
    private static readonly HashSet<string> LoggedOnce = new();

    private static Material SharedMaterial;
    private static bool MaterialOwned;
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

    internal static HitCandidate Capture(Arrow arrow)
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
            candidate.Valid = true;
        }
        catch (Exception e)
        {
            candidate.Valid = false;
            LogOnce("capture:" + e.GetType().Name, "impact capture failed: " + e.Message);
        }
        return candidate;
    }

    internal static void OnNativeHit(Arrow arrow, HitCandidate candidate)
    {
        try
        {
            // 唯一接受证据：前缀未命中 + 原生返回后 _hasHit 已成为 true。
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
            if (!Spawn(candidate, now)) return;             // Spawn 内部负责失败退避；仅池满时静默丢弃
            RecordSpawn(now);
        }
        catch (Exception e)
        {
            LogOnce("hit:" + e.GetType().Name, "impact burst failed: " + e.Message);
        }
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
            float t = (now - slot.Born) / EffectLifetime;
            if (t >= 1f)
            {
                FinishSlot(i);  // 正常淡出：保留几何供复用，不销毁
                continue;
            }
            try { Pose(i, t); }
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

    private static bool Spawn(HitCandidate candidate, float now)
    {
        int index = AcquireSlot();
        if (index < 0) return false; // 池满：静默丢弃，不触发退避
        try
        {
            if (Slots[index].Root == null && !BuildSlot(index, candidate.Source)) return false;

            GameObject root = Slots[index].Root;
            Slots[index].Born = now;
            root.layer = candidate.GoLayer;
            root.transform.position = candidate.Position;
            root.transform.rotation = Quaternion.Euler(0f, 0f, Next01() * 360f);
            for (int layer = 0; layer < LayerCount; layer++)
            {
                int flat = index * LayerCount + layer;
                GameObject go = LayerObjects[flat];
                LineRenderer lr = LayerRenderers[flat];
                if (go == null || lr == null)
                {
                    ReleaseSlot(index);
                    RetryAt = now + RetryBackoffSeconds;
                    LogOnce("layer-missing", "impact layer vanished during spawn; burst aborted");
                    return false;
                }
                go.layer = candidate.GoLayer;
                lr.sortingLayerID = candidate.SortingLayerId;
                lr.sortingOrder = AddOrder(candidate.SortingOrder, LayerOrderDelta[layer]);
                lr.enabled = true;
            }
            root.SetActive(true);
            Pose(index, 0f);
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

    private static bool BuildSlot(int index, Arrow source)
    {
        // 无条件退避：材质一旦解析成功，构建/复用失败绝不能变成同帧无限重建。
        if (Time.time < RetryAt) return false;
        Material material = ResolveMaterial(source);
        if (material == null) return false;

        GameObject root = null;
        try
        {
            root = new GameObject(RootName);
            root.SetActive(false);
            for (int layer = 0; layer < LayerCount; layer++)
            {
                int flat = index * LayerCount + layer;
                GameObject go = new GameObject(LayerNames[layer]);
                LayerObjects[flat] = go; // 先登记：SetParent/AddComponent 失败也能清到这个 child
                go.transform.SetParent(root.transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                LayerRenderers[flat] = lr;
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.numCapVertices = 0;
                lr.numCornerVertices = 0;
                lr.sharedMaterial = material; // 只赋共享引用，绝不触发材质克隆
                // 索引写入（SetPositions/Span 在本运行时不可用）；几何只在此构造一次。
                lr.positionCount = RingPoints;
                float phase = Next01() * TwoPi;
                float firstRadius = 0f;
                for (int point = 0; point < RingPoints; point++)
                {
                    // 闭合点复刻首点：loop 自行闭合，不再于 2π 处多抽随机形成径向接缝。
                    float radius = point == RingSegments ? firstRadius : RingRadius(layer, point, phase);
                    if (point == 0) firstRadius = radius;
                    float angle = TwoPi * point / RingSegments;
                    lr.SetPosition(point, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
                }
                lr.enabled = false;
            }
            Slots[index].Root = root;
            AnyBuilt = true;
            return true;
        }
        catch (Exception e)
        {
            // 逐个清理本次新建的 child（含尚未挂上 root 的孤儿），再清 root，不留悬挂对象。
            for (int layer = 0; layer < LayerCount; layer++)
            {
                int flat = index * LayerCount + layer;
                GameObject child = LayerObjects[flat];
                LayerObjects[flat] = null;
                LayerRenderers[flat] = null;
                DestroyOwn(child);
            }
            Slots[index].Root = null;
            DestroyOwn(root);
            RetryAt = Time.time + RetryBackoffSeconds;
            LogOnce("build:" + e.GetType().Name, "impact pool build failed: " + e.Message);
            return false;
        }
    }

    /// <summary>共享材质解析（每次启用最多自建 1 个，从不克隆）：优先复用命中箭矢
    /// TrailRenderer 的共享线材质，缺失时懒建唯一一个 Sprites/Default 材质；
    /// 两者皆缺则整体跳过并退避重试。借用的材质永不销毁。</summary>
    private static Material ResolveMaterial(Arrow source)
    {
        if (SharedMaterial != null) return SharedMaterial;
        if (Time.time < RetryAt) return null;
        try
        {
            TrailRenderer trail = source != null ? source._trail : null;
            Material borrowed = trail != null ? trail.sharedMaterial : null;
            if (borrowed != null)
            {
                SharedMaterial = borrowed; // 只赋共享引用：不新建、不克隆、不销毁
                MaterialOwned = false;
                return SharedMaterial;
            }
            Shader shader = Shader.Find(FallbackShader);
            if (shader != null)
            {
                SharedMaterial = new Material(shader);
                MaterialOwned = true;
                return SharedMaterial;
            }
        }
        catch (Exception e)
        {
            LogOnce("material:" + e.GetType().Name, "impact material lookup failed: " + e.Message);
        }
        RetryAt = Time.time + RetryBackoffSeconds;
        LogOnce("material-unavailable", "no shared arrow material and Sprites/Default shader unavailable; impact burst disabled");
        return null;
    }

    /// <summary>
    /// 单点半径：奇偶交替的星形尖角幅度随 <see cref="LayerJag"/> 放大（亮核心近乎圆形、
    /// 外圈强烈锯齿），瓣状与随机扰动只向外加扰，因此各层扭曲幅度按层深单调分离，
    /// 几何特征与随机相位无关、可确定性断言。
    /// </summary>
    private static float RingRadius(int layer, int point, float phase)
    {
        float jag = LayerJag[layer];
        float angle = TwoPi * point / RingSegments;
        float spike = (point & 1) == 0 ? jag * .45f : -jag * .45f;
        float lobe = Mathf.Abs(Mathf.Sin(angle * 3f + LayerPhase[layer])) * (jag * .16f);
        float bump = Next01() * (jag * .12f);
        return LayerRadius[layer] * (1f + spike + lobe + bump);
    }

    /// <summary>逐帧只写缩放/线宽/顶点色；几何静止，无分配。</summary>
    private static void Pose(int index, float t)
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            int flat = index * LayerCount + layer;
            float p = (t - LayerDelay[layer]) / (1f - LayerDelay[layer]);
            p = p < 0f ? 0f : p > 1f ? 1f : p;
            float grow = p >= GrowEnd ? 1f : Ease(p / GrowEnd);

            GameObject go = LayerObjects[flat];
            if (go != null)
            {
                float scale = LayerScale[layer] * (ScaleFloor + (1f - ScaleFloor) * grow);
                go.transform.localScale = new Vector3(scale, scale, 1f);
            }
            LineRenderer lr = LayerRenderers[flat];
            if (lr == null) continue;
            lr.widthMultiplier = LayerWidth[layer] * (.55f + .45f * grow);
            Color color = LayerColor[layer];
            color.a = LayerAlpha[layer] * (1f - p);
            lr.startColor = color;
            color.a *= .7f;
            lr.endColor = color;
        }
    }

    private static float Ease(float x) => x * (2f - x);

    private static void FinishSlot(int index)
    {
        Slot slot = Slots[index];
        if (slot.Root != null)
        {
            try { slot.Root.SetActive(false); }
            catch { }
        }
        slot.Born = -1f;
    }

    /// <summary>退回未建状态并销毁自建对象（场景卸载/异常路径/关闭路径共用）。
    /// 仅用于已成功建成的槽：child 均挂在 root 下，随 root 一并销毁。</summary>
    private static void ReleaseSlot(int index)
    {
        Slot slot = Slots[index];
        slot.Born = -1f;
        GameObject root = slot.Root;
        slot.Root = null;
        for (int layer = 0; layer < LayerCount; layer++)
        {
            int flat = index * LayerCount + layer;
            LayerObjects[flat] = null;
            LayerRenderers[flat] = null;
        }
        DestroyOwn(root);
    }

    private static void ReleaseAll()
    {
        if (!AnyBuilt && SharedMaterial == null && RetryAt == 0f && !ContextSet) return;
        for (int i = 0; i < Slots.Length; i++)
            if (Slots[i].Root != null || Slots[i].Born >= 0f) ReleaseSlot(i);
        Material material = SharedMaterial;
        bool owned = MaterialOwned;
        SharedMaterial = null;
        MaterialOwned = false;
        AnyBuilt = false;
        RetryAt = 0f;
        ContextSet = false;
        SpawnHead = 0;
        SpawnCount = 0;
        FrameStamp = int.MinValue;
        FrameAttempts = 0;
        if (owned) DestroyOwn(material); // 借用的共享材质绝不销毁
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

    /// <summary>私有确定性 RNG：绝不消耗 UnityEngine.Random，避免扰动原生弹跳等随机序列。</summary>
    private static float Next01()
    {
        Rng = Rng * 1664525u + 1013904223u;
        return (Rng >> 8) * (1f / 16777216f);
    }
}

/// <summary>Arrow.HitObject 观测宿主：前缀捕获、后缀在原生接受命中后生成特效。
/// 前缀不改写原方法执行（无 __runOriginal），原生方法与其 Finalizer 语义不变。</summary>
[HarmonyPatch(typeof(Arrow), "HitObject")]
public static class Arrow_HitObject_ArcherImpact_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Arrow __instance, out PatchArcher_Impact.HitCandidate __state)
    {
        __state = PatchArcher_Impact.Capture(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(Arrow __instance, PatchArcher_Impact.HitCandidate __state)
    {
        PatchArcher_Impact.OnNativeHit(__instance, __state);
    }
}
