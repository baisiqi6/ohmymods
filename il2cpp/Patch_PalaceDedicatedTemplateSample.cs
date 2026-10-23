using System;
using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

// Dedicated template 整岛替换前模板几何/角色只读采样（source-only 诊断：单文件、无 hook、无 MonoBehaviour、无 driver、无周期/扫描/
// 等待/重试、无 static 可变字段、无 UnityObject 分配副作用、无任何 Native 写、不缓存 Native 引用跨调用）。
// 背景：整岛替换需要真实模板几何与必要角色（camp/dock），不用类型名/首末位置猜。本样本只读输出一次可比较事实，
// 不解释 inactive 没有作用、不把 countedZero 扩大 claims 到整岛、不猜 Boat 名字是 Wharf。
// 调用契约（仅 Root）：Root 在 exact current private land12 / Playing / 非 Saving / 同 Level owner 的现 Create
// 边界前置 latch 后调用一次；LevelBlock 枚举由 Root 经 current Level.GetLevelBlocks() 现有 Native 单次已加载
// 枚举完成（只建 list + scene-name 过滤）。helper 不自己调用 getter、不扩扫描、不新 hook、不 Tick、不 latch、
// 不缓存 Native；level 参数只读其 exact ptr 一次用于回执对账。
// 读取清单（当前 interop typed 入口）：每块一次 block.name / block.Pointer / groupOne/Two/Three /
// cached _dimensionsComputed/_left/_right/_width (not lazy GetLeft/GetWidth) / absoluteCenter / cameraBlockMarker(ptr) / isDangerSource，全部冻结 local。
// 不调用 CloneInto/CloneObject/任何构造器/GetPerCampaignSharedBlocks/任何 setter。内容检查仅对
// group 命中 {2=StartingAreaTight,8=PlayerSpawn,5=Clearing,18,19,30,31}（brief 固定数值；任一 group 字段命中
// 即为候选）的块做：递归该块 Transform 一次，节点<=512、深度<=8、tiles<=32/候选、role/tile 内容行总上限 128，
// 任一超出 abort 并在候选 summary 标 unsupported(...)，不悄悄截断当完整。
// 角色识别只用三类依据：typed GetComponent<Campfire/WreckPlaceholder/Wharf/Tile/Persistent>()（仅当前
// GameObject，不 Parent/Children 搜索、无 GetComponentsInChildren）、Persistent.path 显式 token 包含匹配
// （campfire/wreck/wharf/cameramarker/playerspawn，OrdinalIgnoreCase；token 不命中即明确 countedZero）、
// 节点 GO ptr 与 block.cameraBlockMarker ptr 精确相等（CameraMarker）。
// 异常纪律：role/tile 内容局部 catch 输出 unknown（不补默认值、不重试）；结构读取失败 abort 该候选并在 summary
// 标 unsupported；每块局部 catch；最外层 catch 只记一次 unknown。全部输出经 PalaceCourtVisuals.Log。
// 未编译 / 未 Native 运行（Root 统一 copy/wire/build/验收）。
internal static class PalaceDedicatedTemplateSample
{
    private const string Tag = "[PalaceDedicatedTemplateSample] ";
    private const int MaxBlocks = 256;
    private const int MaxNodesPerCandidate = 512;
    private const int MaxDepth = 8;
    private const int MaxTilesPerCandidate = 32;
    private const int MaxContentLines = 128;
    private const int MaxTextLength = 160;

    // 候选组值（brief 固定的 LevelBlockGroup 数值；成员名未读 interop 核验，只按数值匹配）。
    private static readonly int[] CandidateGroupValues = { 2, 8, 5, 18, 19, 30, 31 };

    // Persistent.path 角色 token（包含匹配）；不含 boat——不把带 Boat 名字的对象猜成 Wharf。
    private static readonly string[] PathRoleKinds = { "Campfire", "WreckPlaceholder", "Wharf", "CameraMarker", "PlayerSpawn" };
    private static readonly string[] PathRoleTokens = { "campfire", "wreck", "wharf", "cameramarker", "playerspawn" };

    // 入口：只由 Root 在其 Create 边界 latch 后调用一次。只读；异常最外层只记一次 unknown。
    internal static void Sample(Level level, Il2CppSystem.Collections.Generic.List<LevelBlock> blocks)
    {
        WalkState state = new WalkState();
        try
        {
            if (blocks == null)
            {
                Log("unsupported: blocks list null; nothing sampled");
                return;
            }

            int count = blocks.Count;
            Log("sample level=" + Ptr(level) + " blocks=" + count + " cap=" + MaxBlocks);
            if (count > MaxBlocks)
            {
                Log("unsupported: blocks count " + count + " > " + MaxBlocks + "; nothing sampled");
                return;
            }

            if (count == 0) { Log("unsupported: block scene unloaded or empty; required roles unknown"); return; }
            for (int i = 0; i < count; i++)
            {
                try
                {
                    SampleBlock(i, blocks[i], state);
                }
                catch (Exception e)
                {
                    Log("block " + i + " unknown (" + Fault(e) + "); no defaults filled, no retry");
                }
            }
            // Geometry table first, then core roles before optional Clearing/shore details consume the shared output budget.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < count; i++)
                    try
                    {
                        LevelBlock candidate = blocks[i];
                        if (candidate == null) continue;
                        int a = (int)candidate.groupOne, b = (int)candidate.groupTwo, c = (int)candidate.groupThree;
                        bool core = a == 2 || b == 2 || c == 2 || a == 8 || b == 8 || c == 8;
                        if ((pass == 0) != core) continue;
                        string hit = GroupHit(a, b, c);
                        if (hit.Length == 0) continue;
                        if (state.ContentLines >= MaxContentLines)
                        { Log("candidate " + i + " unsupported(contentLineBudget); tree not walked"); continue; }
                        WalkCandidate(i, candidate, candidate.cameraBlockMarker, hit, state);
                    }
                    catch (Exception e) { Log("candidate " + i + " unknown (" + Fault(e) + ")"); }
        }
        catch (Exception e)
        {
            Log("unknown: sample fault (" + Fault(e) + "); read-only call boundary, no state changed");
        }
    }

    // 每块一行 compact 事实行；候选块才继续走树。每字段只读一次并冻结 local。
    private static void SampleBlock(int ordinal, LevelBlock block, WalkState state)
    {
        if (block == null)
        {
            Log("block " + ordinal + " null entry; skipped");
            return;
        }

        string name = Sanitize(block.name);
        string ptr = Ptr(block);
        int g1 = (int)block.groupOne;
        int g2 = (int)block.groupTwo;
        int g3 = (int)block.groupThree;
        bool computed = block._dimensionsComputed;
        float left = block._left; // read cached fields only: lazy GetLeft/GetWidth can write ComputeDimensions
        int width = block._width;
        float right = block._right;
        bool center = block.absoluteCenter;
        GameObject marker = block.cameraBlockMarker;
        string markerPtr = marker == null ? "none" : Ptr(marker);
        bool danger = block.isDangerSource;

        Log("block " + ordinal + "/name=" + name + "/ptr=" + ptr + "/group=" + g1 + "," + g2 + "," + g3
            + (computed ? "/left=" + Num(left) + "/right=" + Num(right) + "/width=" + width : "/geometry=unsupported(cache-notcomputed)") + "/center=" + center
            + "/cameraMarker=" + markerPtr + "/danger=" + danger);


    }

    // 候选树一次性递归 + summary（complete/unsupported、countedZero 仅本树）。
    private static void WalkCandidate(int ordinal, LevelBlock block, GameObject marker, string groupHit, WalkState state)
    {
        state.BeginCandidate();
        Transform root = block.transform;
        WalkNode(ordinal, root, root, "", 0, marker, state);
        Log("candidate " + ordinal + " groupHit=" + groupHit
            + " nodes=" + state.Nodes + "/" + MaxNodesPerCandidate
            + " roles=" + state.Roles + " tiles=" + state.Tiles + "/" + MaxTilesPerCandidate
            + (state.Aborted ? " unsupported(" + state.AbortReason + ")" : " complete")
            + (state.Roles == 0 && !state.Aborted ? " countedZero(thisCandidateTreeOnly)" : string.Empty));
    }

    // 深度<=8、节点<=512；任一超出或结构读取异常即 abort（summary 标 unsupported，不悄悄截断当完整）。
    private static void WalkNode(int ordinal, Transform root, Transform node, string path, int depth, GameObject marker, WalkState state)
    {
        if (state.Aborted)
        {
            return;
        }

        if (depth > MaxDepth)
        {
            state.Abort("depth>" + MaxDepth);
            return;
        }

        if (state.Nodes >= MaxNodesPerCandidate)
        {
            state.Abort("nodes>=" + MaxNodesPerCandidate);
            return;
        }

        GameObject go;
        bool active;
        Vector3 local;
        Vector3 rootRelative;
        int childCount;
        try
        {
            go = node.gameObject;
            active = go.activeSelf;
            local = node.localPosition;
            rootRelative = root.InverseTransformPoint(node.position);
            childCount = node.childCount;
        }
        catch (Exception e)
        {
            state.Abort("nodeRead:" + e.GetType().Name);
            return;
        }

        state.Nodes++;

        InspectNode(ordinal, go, active, local, rootRelative, path, marker, state);
        if (state.Aborted)
        {
            return;
        }

        for (int c = 0; c < childCount; c++)
        {
            if (state.Aborted)
            {
                return;
            }

            Transform child;
            string childName;
            try
            {
                child = node.GetChild(c);
                childName = Sanitize(child.name);
            }
            catch (Exception e)
            {
                state.Abort("childRead:" + e.GetType().Name);
                return;
            }

            WalkNode(ordinal, root, child, path.Length == 0 ? childName : path + "/" + childName, depth + 1, marker, state);
        }
    }

    // 节点内容（typed 直接组件 + Persistent.path token + cameraBlockMarker ptr 精确相等）；
    // 局部 catch 输出 unknown，不补默认值、不重试、不中断遍历（除非预算 abort）。
    private static void InspectNode(int ordinal, GameObject go, bool active, Vector3 local, Vector3 rootRelative, string path, GameObject marker, WalkState state)
    {
        try
        {
            Campfire campfire = go.GetComponent<Campfire>();
            WreckPlaceholder wreck = go.GetComponent<WreckPlaceholder>();
            Wharf wharf = go.GetComponent<Wharf>();
            Tile tile = go.GetComponent<Tile>();
            Persistent persistent = go.GetComponent<Persistent>();
            string rawPath = persistent != null ? persistent.path : null;
            string persistentPath = rawPath != null ? Sanitize(rawPath) : null;
            if (persistent != null && rawPath == null)
            { ContentLine(state, "persistent " + ordinal + " go=" + Ptr(go) + " path=unknown(null)"); state.Abort("persistentPathUnknown"); }

            StringBuilder kinds = new StringBuilder();
            Il2CppObjectBase roleComponent = null;
            if (campfire != null)
            {
                AppendKind(kinds, "Campfire");
                roleComponent = roleComponent ?? campfire;
            }

            if (wreck != null)
            {
                AppendKind(kinds, "WreckPlaceholder");
                roleComponent = roleComponent ?? wreck;
            }

            if (wharf != null)
            {
                AppendKind(kinds, "Wharf");
                roleComponent = roleComponent ?? wharf;
            }

            if (persistentPath != null)
            {
                for (int k = 0; k < PathRoleTokens.Length; k++)
                {
                    if (persistentPath.IndexOf(PathRoleTokens[k], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AppendKind(kinds, PathRoleKinds[k] + "@path");
                        roleComponent = roleComponent ?? persistent;
                    }
                }
            }

            if (marker != null && go.Pointer == marker.Pointer)
            {
                AppendKind(kinds, "CameraMarker@ptr(cameraBlockMarker)");
            }

            if (kinds.Length > 0)
            {
                if (ContentLine(state, "role " + ordinal + " kind=" + kinds + " go=" + Ptr(go)
                    + " comp=" + (roleComponent == null ? "none" : Ptr(roleComponent))
                    + " camp=" + Ptr(campfire) + " wreck=" + Ptr(wreck) + " wharf=" + Ptr(wharf) + " persistent=" + Ptr(persistent)
                    + " active=" + active
                    + " path=" + Sanitize(path)
                    + " local=" + Vec(local) + " rootRelative=" + Vec(rootRelative)
                    + (persistentPath == null ? string.Empty : " ppath=" + persistentPath)))
                {
                    state.Roles++;
                }
            }

            if (tile != null)
            {
                state.Tiles++;
                if (state.Tiles > MaxTilesPerCandidate)
                {
                    state.Abort("tiles>" + MaxTilesPerCandidate);
                    return;
                }

                float tileLeft = tile.GetLeft();
                float tileRight = tile.GetRight();
                bool noGround = tile.noGround;
                ContentLine(state, "tile " + ordinal + " go=" + Ptr(go) + " comp=" + Ptr(tile)
                    + " active=" + active + " path=" + Sanitize(path)
                    + " left=" + Num(tileLeft) + " right=" + Num(tileRight) + " noGround=" + noGround);
            }
        }
        catch (Exception e)
        {
            ContentLine(state, "role " + ordinal + " kind=unknown go=" + Ptr(go) + " path=" + Sanitize(path)
                + " contentUnknown (" + Fault(e) + "); no defaults filled, no retry");
            state.Abort("contentUnknown:" + e.GetType().Name);
        }
    }

    // role/tile/unknown 内容行共享总预算 128；达到上限不继续走树并在候选 summary 标 unsupported。
    private static bool ContentLine(WalkState state, string message)
    {
        if (state.ContentLines >= MaxContentLines)
        {
            state.Abort("contentLines>=" + MaxContentLines);
            return false;
        }

        state.ContentLines++;
        Log(message);
        return true;
    }

    private static string GroupHit(int g1, int g2, int g3)
    {
        StringBuilder hits = new StringBuilder();
        AppendGroupHit(hits, "g1", g1);
        AppendGroupHit(hits, "g2", g2);
        AppendGroupHit(hits, "g3", g3);
        return hits.ToString();
    }

    private static void AppendGroupHit(StringBuilder hits, string label, int value)
    {
        for (int i = 0; i < CandidateGroupValues.Length; i++)
        {
            if (value == CandidateGroupValues[i])
            {
                if (hits.Length > 0)
                {
                    hits.Append(' ');
                }

                hits.Append(label).Append('=').Append(value);
                return;
            }
        }
    }

    private static void AppendKind(StringBuilder kinds, string kind)
    {
        if (kinds.Length > 0)
        {
            kinds.Append('+');
        }

        kinds.Append(kind);
    }

    // exact 当前 wrapper 的 Native ptr（null=none）；接口/组件统一经 Il2CppObjectBase。
    private static string Ptr(Il2CppObjectBase o)
        => o == null ? "none" : "0x" + o.Pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture);

    private static string Num(float v) => v.ToString("G7", CultureInfo.InvariantCulture);

    private static string Vec(Vector3 v) => Num(v.x) + "," + Num(v.y) + "," + Num(v.z);

    private static string Fault(Exception e) => e.GetType().Name + ": " + Sanitize(e.Message);

    // 转义换行/制表并限制 160 字符；截断带 "..." 标记，不静默截断。
    private static string Sanitize(string s)
    {
        if (s == null)
        {
            return "null";
        }

        string t = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return t.Length <= MaxTextLength ? t : t.Substring(0, MaxTextLength - 3) + "...";
    }

    private static void Log(string message) => PalaceCourtVisuals.Log(Tag + message);

    // 每次调用一个实例、无 static 可变状态；ContentLines 跨候选共享（总预算），其余每候选重置。
    private sealed class WalkState
    {
        internal int ContentLines;
        internal int Nodes;
        internal int Tiles;
        internal int Roles;
        internal bool Aborted;
        internal string AbortReason;

        internal void BeginCandidate()
        {
            Nodes = 0;
            Tiles = 0;
            Roles = 0;
            Aborted = false;
            AbortReason = null;
        }

        internal void Abort(string reason)
        {
            if (!Aborted)
            {
                Aborted = true;
                AbortReason = reason;
            }
        }
    }
}
