using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 新生成岛实际宽度扩展的原生适配器（issue-77）：
///   * scope：GenerateInternal 前缀打开、后缀关闭、Finalizer 归还；嵌套用栈；每个 scope 只发布一次。
///   * 候选：只从本次 LevelLayout.blocks 取精确名 Forest_Blocks / Clearing Small_Blocks /
///     Clearing_Blocks / Clearing Large_Blocks，共同核验 groupOne/Two/Three、中心/相机/危险源标记
///     与每个可复制地形对象的确切组件组合、持久化路径、Grass 池戳；同键（名|group|宽）出现
///     第二个不同模板引用即整键歧义拒绝，重复同一引用仅去重。
///   * 接缝：原始返回列表中已验证普通块的右边界，且严格在首末块之间；普通块后邻功能块允许延长。
///   * 发布：先完整规划并构造新 List；验证与日志内容准备完成后，result=inflated 是本函数最后一个
///     成功动作；失败时原列表一个字节都不改。
/// 只在 scope 内工作；倍率 1 或 MOD 关闭时零副作用。不修改 layout/shared 计数/随机源。
/// </summary>
internal static class MapWidthScope
{
    internal sealed class Frame
    {
        internal bool Active;
        internal bool Closed;
        internal float Multiplier;
        internal bool Consumed;
        internal string Failure;
        internal MapWidthPlan Plan;
        internal int BaselineWidth = -1;
        internal int LayoutTotalWidth = -1;
        internal int AddedBlocks;
        internal int SeamCount;
        internal int CandidateCount;
    }

    private sealed class CandidateGroup
    {
        internal string Key;
        internal string Name;
        internal TerrainKind Kind;
        internal int Width;
        internal LevelBlock Representative;
        internal bool Conflict;
    }

    private enum TerrainKind { None, Forest, Clearing }
    private enum TerrainObjectKind { None, Tile, Grass }

    private const string ForestBlockName = "Forest_Blocks";
    private const string ClearingSmallBlockName = "Clearing Small_Blocks";
    private const string ClearingBlockName = "Clearing_Blocks";
    private const string ClearingLargeBlockName = "Clearing Large_Blocks";
    private const float CoverageTolerance = 0.001f;

    // template-boundary.md 实测：level4 四个模板的 GameLayer 直接子对象恰为下列两种组件组合。
    private const string TransformName = "Transform";
    private const string SpriteRendererName = "SpriteRenderer";
    private const string RandomizeSpriteName = "RandomizeSprite";
    private const string TileName = "Tile";
    private const string GrassName = "Grass";
    private const string PersistentName = "Persistent";
    private const string BiomeSpriteSwapperName = "BiomeSpriteSwapper";
    private const string FixedTransformName = "FixedTransform";
    private const string GapsName = "GAPS";
    private const string PoolStamperName = "PoolStamper";

    private const string TileForestPath = "Prefabs/Environment/Tile Forest";
    private const string TileClearingPath = "Prefabs/Environment/Tile Clearing";
    private const string GrassPath = "Prefabs/Vegetation/Grass";

    private static readonly string[] TileComponentNames =
    {
        TransformName, SpriteRendererName, RandomizeSpriteName, TileName,
        PersistentName, BiomeSpriteSwapperName, FixedTransformName,
    };

    private static readonly string[] GrassComponentNames =
    {
        TransformName, SpriteRendererName, GrassName, GapsName,
        PersistentName, FixedTransformName, BiomeSpriteSwapperName, PoolStamperName,
    };

    private static readonly List<Frame> Frames = new List<Frame>();
    private static readonly HashSet<string> ContentLayerNames = BuildContentLayerNames();

    internal static void Open(bool modEnabled, float multiplier, out Frame frame)
    {
        frame = new Frame
        {
            Multiplier = multiplier,
            Active = modEnabled && float.IsFinite(multiplier) && multiplier > 1f,
        };
        Frames.Add(frame);
    }

    /// <summary>GenerateInternal 正常返回：归还 scope，并读回原生 LevelEdges 实际宽度（仅日志）。</summary>
    internal static void Close(Level level, Frame frame)
    {
        if (frame == null || frame.Closed) return;
        frame.Closed = true;
        Remove(frame);
        if (!frame.Active) return;
        if (!frame.Consumed)
        {
            Log("[MapWidth] warning: generation scope closed without a GetBlocks observation", true);
            return;
        }
        if (level == null) return;
        IntRange edges;
        try
        {
            edges = level._levelEdges;
        }
        catch (Exception e)
        {
            // 仅诊断读回失败；生成结果已提交，不受影响。
            Log("[MapWidth] edges read failed: " + e.Message, true);
            return;
        }
        Log(string.Format(CultureInfo.InvariantCulture,
            "[MapWidth] edges min={0} max={1} width={2}", edges.min, edges.max, edges.max - edges.min), false);
    }

    /// <summary>GenerateInternal 原生异常：只归还 scope；异常由 Finalizer 原样返回，绝不在此吞掉。</summary>
    internal static void Abort(Frame frame)
    {
        if (frame == null || frame.Closed) return;
        frame.Closed = true;
        Remove(frame);
        if (frame.Active && frame.Consumed)
        {
            Log("[MapWidth] warning: generation threw after GetBlocks; scope restored, padding may be partial", true);
        }
    }

    /// <summary>
    /// LevelLayout.GetBlocks 后缀入口：只在 GenerateInternal scope 内、且本 scope 首次调用时处理。
    /// scope 之外/倍率 1/关闭时立即返回，原返回列表不做任何读写。
    /// </summary>
    internal static void TryApply(LevelLayout layout, ref Il2CppSystem.Collections.Generic.List<LevelBlock> result)
    {
        Frame frame = Frames.Count > 0 ? Frames[Frames.Count - 1] : null;
        if (frame == null || !frame.Active || frame.Consumed) return;
        frame.Consumed = true;
        try
        {
            Apply(layout, ref result, frame);
        }
        catch (Exception e)
        {
            // 适配失败：明确告警且不触碰原结果（发布点在本函数里是最后一个成功动作）。
            frame.Failure = "exception:" + e.GetType().Name;
            Log("[MapWidth] unsupported reason=exception detail=" + e.Message
                + " baseline=" + frame.BaselineWidth, true);
        }
    }

    private static void Apply(LevelLayout layout, ref Il2CppSystem.Collections.Generic.List<LevelBlock> result, Frame frame)
    {
        Il2CppSystem.Collections.Generic.List<LevelBlock> original = result;
        if (original == null) { Fail(frame, "no-result"); return; }
        if (layout == null) { Fail(frame, "no-layout"); return; }

        int count = original.Count;
        long baseline = 0;
        for (int i = 0; i < count; i++)
        {
            LevelBlock block = original[i];
            if (block == null) { Fail(frame, "null-block"); return; }
            int width = block.GetWidth();
            if (width <= 0) { Fail(frame, "nonpositive-original-width"); return; }
            baseline += width;
        }
        if (baseline <= 0 || baseline > int.MaxValue) { Fail(frame, "baseline-out-of-range"); return; }
        frame.BaselineWidth = (int)baseline;

        var candidates = new List<MapWidthCandidate>();
        var representatives = new List<LevelBlock>();
        CollectCandidates(layout, candidates, representatives);
        frame.CandidateCount = candidates.Count;

        MapWidthPlan plan = MapWidthPlanner.Plan(frame.BaselineWidth, frame.Multiplier, candidates);
        frame.Plan = plan;
        if (!plan.Success) { Fail(frame, plan.Failure); return; }

        if (plan.AddedWidth <= 0)
        {
            // 有效规划但按地块取整后不追加：记录一次，保持原生结果。
            Log(FormatPlannedLine(frame, layout), false);
            return;
        }

        // 合法接缝：左邻是已验证普通块，且插入点严格在首块与末块之间（不得加到末端之外）。
        var seams = new List<int>();
        for (int j = 1; j <= count - 1; j++)
        {
            if (Classify(original[j - 1]) != TerrainKind.None) seams.Add(j);
        }
        frame.SeamCount = seams.Count;
        if (seams.Count == 0) { Fail(frame, "no-seam"); return; }

        int[] slots = MapWidthPlanner.AssignSeams(plan.Paddings.Length, seams.Count);
        var perSeam = new List<int>[seams.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            List<int> bucket = perSeam[slot];
            if (bucket == null)
            {
                bucket = new List<int>();
                perSeam[slot] = bucket;
            }
            bucket.Add(i);
        }

        // 先构造完整新列表（保留原引用与相对顺序）。
        var inflated = new Il2CppSystem.Collections.Generic.List<LevelBlock>();
        int seamCursor = 0;
        for (int j = 0; j < count; j++)
        {
            if (seamCursor < seams.Count && seams[seamCursor] == j)
            {
                List<int> bucket = perSeam[seamCursor];
                if (bucket != null)
                {
                    for (int b = 0; b < bucket.Count; b++)
                    {
                        inflated.Add(representatives[plan.Paddings[bucket[b]]]);
                    }
                }
                seamCursor++;
            }
            inflated.Add(original[j]);
        }

        // 发布前的最后准备：日志内容（含 TotalWidth 只读）先备好；发布之后只有自身隔离的 Log。
        frame.AddedBlocks = plan.Paddings.Length;
        string summary = FormatPlannedLine(frame, layout);
        result = inflated;
        Log(summary, false);
    }

    private static void Fail(Frame frame, string reason)
    {
        frame.Failure = reason;
        Log(string.Format(CultureInfo.InvariantCulture,
            "[MapWidth] unsupported reason={0} baseline={1} slider={2:0.###} target={3:0.##} candidates={4} added=0",
            reason, frame.BaselineWidth, frame.Multiplier,
            frame.Plan != null ? frame.Plan.TargetWidth : 0d, frame.CandidateCount), true);
    }

    private static string FormatPlannedLine(Frame frame, LevelLayout layout)
    {
        int layoutTotal = -1;
        if (layout != null)
        {
            try
            {
                layoutTotal = layout.TotalWidth();
            }
            catch (Exception e)
            {
                // 仅诊断读回失败；不改变规划与发布结果。
                Log("[MapWidth] layoutTotal read failed: " + e.Message, true);
            }
        }
        frame.LayoutTotalWidth = layoutTotal;
        return string.Format(CultureInfo.InvariantCulture,
            "[MapWidth] gen baseline={0} layoutTotal={1} slider={2:0.###} target={3:0.##} plannedWidth={4} addedWidth={5} addedBlocks={6} seams={7} candidates={8}",
            frame.BaselineWidth, layoutTotal, frame.Multiplier,
            frame.Plan != null ? frame.Plan.TargetWidth : 0d,
            frame.Plan != null ? frame.Plan.PlannedWidth : frame.BaselineWidth,
            frame.Plan != null ? frame.Plan.AddedWidth : 0,
            frame.AddedBlocks, frame.SeamCount, frame.CandidateCount);
    }

    /// <summary>
    /// 只从本次 layout.blocks 收集候选；命名/组别/标记/结构全部核验后才进入规划器。
    /// 排序固定为 名字 → group → 实际宽；同键出现第二个不同模板引用即整键歧义拒绝
    /// （对象身份只用于识别重复引用，不参与排序或选拔）。
    /// </summary>
    private static void CollectCandidates(LevelLayout layout, List<MapWidthCandidate> candidates, List<LevelBlock> representatives)
    {
        Il2CppSystem.Collections.Generic.List<LevelBlock> available = layout.blocks;
        if (available == null) return;

        var groups = new List<CandidateGroup>();
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < available.Count; i++)
        {
            LevelBlock block = available[i];
            TerrainKind kind = Classify(block, out int width);
            if (kind == TerrainKind.None) continue;

            string name = block.name;
            string key = name + "|" + GroupCode(kind).ToString(CultureInfo.InvariantCulture)
                + "|" + width.ToString(CultureInfo.InvariantCulture);
            if (byKey.TryGetValue(key, out int at))
            {
                if (!ReferenceEquals(groups[at].Representative, block)) groups[at].Conflict = true;
                continue;
            }
            byKey.Add(key, groups.Count);
            groups.Add(new CandidateGroup
            {
                Key = key,
                Name = name,
                Kind = kind,
                Width = width,
                Representative = block,
            });
        }

        groups.Sort((a, b) =>
        {
            int cmp = string.CompareOrdinal(a.Name, b.Name);
            if (cmp != 0) return cmp;
            cmp = GroupCode(a.Kind).CompareTo(GroupCode(b.Kind));
            if (cmp != 0) return cmp;
            cmp = a.Width.CompareTo(b.Width);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.Key, b.Key);
        });

        for (int i = 0; i < groups.Count; i++)
        {
            CandidateGroup group = groups[i];
            if (group.Conflict)
            {
                Log("[MapWidth] candidate key rejected (ambiguous duplicate template): " + group.Key, true);
                continue;
            }
            candidates.Add(new MapWidthCandidate(group.Key, group.Width, group.Kind == TerrainKind.Clearing));
            representatives.Add(group.Representative);
        }
    }

    private static TerrainKind Classify(LevelBlock block)
    {
        return Classify(block, out _);
    }

    /// <summary>精确名 + 组别 + 标记 + 完整可复制地形内容共同核验；任一不符即不是可复用普通地块。</summary>
    private static TerrainKind Classify(LevelBlock block, out int width)
    {
        width = 0;
        if (block == null) return TerrainKind.None;

        string name = block.name;
        TerrainKind kind;
        LevelBlockGroup expectedGroup;
        if (string.Equals(name, ForestBlockName, StringComparison.Ordinal))
        {
            kind = TerrainKind.Forest;
            expectedGroup = LevelBlockGroup.Forest;
        }
        else if (string.Equals(name, ClearingSmallBlockName, StringComparison.Ordinal)
            || string.Equals(name, ClearingBlockName, StringComparison.Ordinal)
            || string.Equals(name, ClearingLargeBlockName, StringComparison.Ordinal))
        {
            kind = TerrainKind.Clearing;
            expectedGroup = LevelBlockGroup.Clearing;
        }
        else
        {
            return TerrainKind.None;
        }

        if (block.groupOne != expectedGroup
            || block.groupTwo != LevelBlockGroup.None
            || block.groupThree != LevelBlockGroup.None) return TerrainKind.None;
        if (block.absoluteCenter || block.cameraBlockMarker != null || block.isDangerSource) return TerrainKind.None;

        int nativeWidth = block.GetWidth();
        if (nativeWidth <= 0 || nativeWidth > MapWidthPlanner.MaxTotalWidth) return TerrainKind.None;
        if (!VerifyTerrainContent(block, nativeWidth)) return TerrainKind.None;
        width = nativeWidth;
        return kind;
    }

    /// <summary>
    /// 按原生 CloneInto 的可复制集合核验地形内容：
    ///   * 激活内容层容器的每个激活直接子对象都必须是确切 Tile 或 Grass 地形对象（含组件组合、
    ///     Persistent 有效路径、Grass 池戳），不得有子孙（防止功能/NPC/PrefabPlaceholder 子树）；
    ///   * Tile 正宽、noGround=false、按左缘排序连贯覆盖且 floor(span)==原生 GetWidth。
    /// 非内容层容器/非激活对象与原生 CloneInto 一样不参与复制，也不在此核验。
    /// </summary>
    private static bool VerifyTerrainContent(LevelBlock block, int blockWidth)
    {
        Transform root = block.transform;
        if (root == null) return false;

        var spans = new List<TileSpan>();
        int layerRoots = 0;
        int childCount = root.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform layer = root.GetChild(i);
            if (layer == null) return false;
            GameObject layerObject = layer.gameObject;
            if (layerObject == null || !layerObject.activeSelf) continue;
            if (!ContentLayerNames.Contains(layerObject.name)) continue;
            layerRoots++;
            int innerCount = layer.childCount;
            for (int c = 0; c < innerCount; c++)
            {
                Transform child = layer.GetChild(c);
                if (child == null) return false;
                GameObject childObject = child.gameObject;
                if (childObject == null || !childObject.activeSelf) continue;
                TerrainObjectKind objectKind = MatchTerrainObject(childObject);
                if (objectKind == TerrainObjectKind.None) return false;
                if (objectKind == TerrainObjectKind.Tile)
                {
                    Tile tile = childObject.GetComponent<Tile>();
                    if (tile == null || tile.noGround) return false;
                    float left = tile.GetLeft();
                    float right = tile.GetRight();
                    if (!float.IsFinite(left) || !float.IsFinite(right) || right <= left) return false;
                    spans.Add(new TileSpan { Left = left, Right = right });
                }
            }
        }
        if (layerRoots == 0 || spans.Count == 0) return false;

        spans.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right));
        float covered = spans[0].Right;
        for (int i = 1; i < spans.Count; i++)
        {
            if (spans[i].Left > covered + CoverageTolerance) return false; // 缝隙：Tile 未连贯覆盖
            if (spans[i].Right > covered) covered = spans[i].Right;
        }
        float spanStart = spans[0].Left;
        float spanEnd = spans[spans.Count - 1].Right;
        if (!(spanEnd - spanStart > 0f)) return false;
        return (int)Math.Floor((double)(spanEnd - spanStart)) == blockWidth;
    }

    /// <summary>
    /// 单个可复制地形对象的确切准入：无子孙；组件组合必须与已证 Tile 或 Grass 组合完全一致
    /// （按各自原生类名计数）；Persistent 必须启用且路径对应原生 Tile Forest/Tile Clearing/Grass；
    /// Grass 的 PoolStamper 必须指向 Grass 池。不允许 PrefabID 之类未证组件假设。
    /// </summary>
    private static TerrainObjectKind MatchTerrainObject(GameObject subject)
    {
        Transform transform = subject.transform;
        if (transform == null || transform.childCount != 0) return TerrainObjectKind.None;

        var components = subject.GetComponents<Component>();
        if (components == null || components.Length == 0) return TerrainObjectKind.None;
        var names = new string[components.Length];
        Persistent persistent = null;
        PoolStamper stamper = null;
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) return TerrainObjectKind.None;
            string className = IL2CPP.il2cpp_class_get_name_(component.ObjectClass);
            names[i] = className;
            if (string.Equals(className, PersistentName, StringComparison.Ordinal))
            {
                persistent = component.TryCast<Persistent>();
            }
            else if (string.Equals(className, PoolStamperName, StringComparison.Ordinal))
            {
                stamper = component.TryCast<PoolStamper>();
            }
        }

        bool tileShape = SameNameMultiset(names, TileComponentNames);
        bool grassShape = !tileShape && SameNameMultiset(names, GrassComponentNames);
        if (tileShape == grassShape) return TerrainObjectKind.None;
        if (persistent == null || !persistent.persistObject) return TerrainObjectKind.None;

        string path = persistent.path;
        if (tileShape)
        {
            return path == TileForestPath || path == TileClearingPath
                ? TerrainObjectKind.Tile
                : TerrainObjectKind.None;
        }
        if (stamper == null || stamper.targetPool != PoolStamper.Pool.Grass) return TerrainObjectKind.None;
        return path == GrassPath ? TerrainObjectKind.Grass : TerrainObjectKind.None;
    }

    private static bool SameNameMultiset(string[] actual, string[] expected)
    {
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (CountName(actual, expected[i]) != CountName(expected, expected[i])) return false;
        }
        return true;
    }

    private static int CountName(string[] names, string name)
    {
        int count = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private struct TileSpan
    {
        internal float Left;
        internal float Right;
    }

    private static int GroupCode(TerrainKind kind)
    {
        return kind == TerrainKind.Clearing ? (int)LevelBlockGroup.Clearing : (int)LevelBlockGroup.Forest;
    }

    private static HashSet<string> BuildContentLayerNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in Enum.GetNames(typeof(ContentLayers))) names.Add(name);
        return names;
    }

    private static void Remove(Frame frame)
    {
        for (int i = Frames.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Frames[i], frame))
            {
                Frames.RemoveAt(i);
                return;
            }
        }
    }

    private static void Log(string line, bool warning)
    {
        try
        {
            KingdomEnhancedPlugin plugin = KingdomEnhancedPlugin.Instance;
            if (plugin == null || plugin.LogSource == null) return;
            if (warning) plugin.LogSource.LogWarning(line);
            else plugin.LogSource.LogInfo(line);
        }
        catch (Exception)
        {
            // 唯一日志边界：日志失败不影响生成流程与已提交结果。
        }
    }
}
