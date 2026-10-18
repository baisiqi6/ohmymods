// ohmymods — palace visible nodes / 全量世界艺术节点：world-expansion-r1 已审固定艺术的场景自有物件接入。
// 纯 managed 类（不注册 IL2CPP、无 Harmony/MonoBehaviour/Manager/全局场景扫描/GUI/循环扫描/新 hook）：
// Root 在 native footprint 核查后构建 PalaceWorldPlan（含借用 native 父 Transform 与参照 SpriteRenderer），
// 本类只负责 Create/Activate/Tick/Clear 四个入口；激活时机、时钟条件（playing）与清理时机归 Root。
// 素材 = Assembly 嵌入资源 "KingdomEnhancedMod.KEM_PalaceWorldFull/" + relative（本类不读磁盘、不改字节）：
//   architecture/manifest.json + architecture/<file>（13 件建筑/环境层，exact 与 clean 均可嵌入、均支持加载；
//   默认放置哪些由 plan 决定——只放 plan 列出的 exact，不自动重复放置 clean）、actors/manifest.json + actors/<atlas>。
// 数据全部来自已审 manifest（System.Text.Json 严格解析）：PPU32（logicalPpu32×pixelMultiplier=1）、
// 建筑 pivot 上左原点 → Unity normalized (px/w, 1−py/h)；演员帧只用显式 rectPixelsTopLeft（rect bottom = atlasH−topY−h），
// 绝不用 atlas 空白格、不做格位整除推算；同 rect 复用同一 sprite；guard pivot (32.5,4)/64、dancer (24.5,4)/48 读字段。
// 舞台关系断言沿 r1 已验收：deck 144×88 pivot[72,87] baseline65、front 144×14 pivot[72,14]，偏离即拒绝；
// 舞女脚线 = deck pivot + PalaceStageData.StageSurfaceYOffset(22/32)、front pivot + FrontPivotYOffset(21/32)。
// 守卫 = 纯场景表演巡逻（不是 NativeNPC/combatAI）：左端点起步右行，walk 恰 N×1040ms 连续插值路线（整循环结束，
// stop 帧 13 与 walk 触地相的 entryPoseRelation 成立）→ stop1000 → greet1900 → idle1500 → turn1420 后反向循环。
// flipX：左向 walk/stop/greet/idle 为 true、右向 false；右→左 turn 为 false（尾帧 29 实际画向左，manifest frame facing
// 是已知误标不读，以 clip endFacing=left 断言）；左→右 turn 为 true。绝不写借用 actor 的 xScale/位置；
// 守卫自身 GO 的位置更新即表演巡逻权威。舞女 idle1940 → dance4640×2 → bow2220 循环，flipX 恒 false，26 帧全支持。
// R7 actors-only 入口 CreateActors（纯源码 API，未接线）：只解析 actors manifest + 3 atlas（仍断言 86 显式帧）、
// 只建守卫/舞女自有根，不解析/不加载 architecture、不建旧建筑/舞台/静态层，也不碰 sky/Native/global sorter。
// plan 由 Root 以当次 CourtBinding 单标定填（本类不测量/不扫描/不找人物/不推算 PPU）：
// PalaceActorsPlan{Guards≤2（lance/shield 各一、ID 不重复），Dancer?}，每项 Parent/Reference/worldZ 与自有 YScale∈(0,2]，
// 守卫给 Left/Right（left.x<right.x、两端同 y/z）、舞女给脚点 WorldPosition（无 deck/front、无 +22/32 偏移）。
// 借用父须 unit lossyScale 且无旋转、与 Reference 同场景；Reference.sortingOrder 必须为 0（R7 unit plane 的排序所有权），
// 演员 order=0 + spriteSortPoint=Pivot、localScale=(1,YScale,1)（x 恒 1，朝向仍 flipX；缩放绕 pivot，脚点坐标不被改写）。
// CreateActors 先完成全部校验再分配、整批 inactive，Root 接线后 Activate；失败统一走既有 Clear/quarantine，无重试。
// R2：Root 清理走 TryClear(out reason)（只调 Clear 一次，false=清理未确证）；CreateActors 失败且清理不能确证时
// result 交回该实例（Root 保住 owner，不丢弃仍在借 native 父的根）；own renderer 产生处读回 scale/parent/位置(含 z)/
// order/pivot/layer/首帧 sprite 不符即抛，交 CreateActors 统一 Clear（只读借用对象、不搜索 Unity 对象）。
// 所有权与清理：新 GO/renderer/texture/sprite 创建即登记（先登记后做可能失败的 layer/SetParent/解码/尺寸校验），
// 构建期整批 inactive，Activate 才整体激活；借用父/材质/参照只读，绝不禁用/销毁/改写。每个自有根以绝对世界坐标落位
// （SetParent(parent,false) 后写 world position；假定 Root 已核 native 父 unit scale）。renderer 的
// sortingLayer/sortingOrder(=参照+显式偏移)/material/layer 复制自提供的参照，Color.white。
// Clear 先停实例时钟，再停用自有 children、逐个解除自有 renderer 的 sprite 并回读确认，最后才销毁自有资产；
// 任一步未知（异常/回读不符）→ 单个静态 retained quarantine + 可观察日志，阻止后续 Create 新分配，不重试。
// Tick：实例时钟 bounded modulo（大有限 delta 先取模再累加，之后正常 delta 仍工作）；NaN/±Inf/负数忽略；
// playing=false 冻结；仅帧变化才写 sprite；renderer 不可用/写入异常 → 该 actor 停一次并记日志，不重建；
// 仅相位变化时记日志。无每帧分配/场景读取/新资源/重试。边界：不声明 native NPC/持久化/网络/实机验收。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingdomEnhancedMod;

/// <summary>计划项：一件建筑/环境层放置。同一 AssetId 可重复出现（如连廊多模块/多层复用），共享同一 sprite。</summary>
internal sealed class PalaceWorldPlacement
{
    internal string AssetId;           // architecture manifest 的 13 个 id 之一
    internal Transform Parent;         // 借用 native 父（Root 已核 unit scale）；只借用不改写
    internal SpriteRenderer Reference; // 借用参照：layer/sortingLayer/sortingOrder/material 的复制源
    internal Vector3 Position;         // 绝对世界坐标（sprite pivot 落点）
    internal int OrderOffset;          // 叠加在 Reference.sortingOrder 上的显式偏移
}

/// <summary>巡逻守卫路线（纯表演，非 NativeNPC/combatAI）。端点为绝对世界坐标。</summary>
internal sealed class PalaceWorldGuardRoute
{
    internal string ActorId;           // patrol-guard-lance / patrol-guard-shield
    internal Transform Parent;
    internal SpriteRenderer Reference;
    internal Vector3 Left;             // 左端点（起步点）
    internal Vector3 Right;            // 右端点（须满足 Left.x < Right.x）
    internal int WalkLoops;            // 单程 walk 循环数，>= 1（单程时长 = WalkLoops × 1040ms）
    internal int OrderOffset;
}

/// <summary>舞台：deck + 舞女 + front 三件自有 renderer。Position 为 deck 底边 pivot 的绝对世界坐标（落地）。</summary>
internal sealed class PalaceWorldStage
{
    internal Transform Parent;
    internal SpriteRenderer Reference;
    internal Vector3 Position;         // deck 底边 pivot（144×88 canvas、pivot[72,87]）
    internal int DeckOrderOffset;      // 三者排序由 plan 显式给出（Root 保证 deck < dancer < front 的关系）
    internal int DancerOrderOffset;
    internal int FrontOrderOffset;
}

/// <summary>一次 Create 的完整计划：全部坐标为绝对世界坐标；Stage 可为 null（不建舞台）。</summary>
internal sealed class PalaceWorldPlan
{
    internal List<PalaceWorldPlacement> Placements;
    internal List<PalaceWorldGuardRoute> Guards;
    internal PalaceWorldStage Stage;
}

/// <summary>R7 actors-only 计划项：单个巡逻守卫（绝对世界端点；自有 YScale 只缩放 y 轴，脚点仍取 pivot 落点）。</summary>
internal sealed class PalaceActorGuardPlan
{
    internal string ActorId;           // 仅 patrol-guard-lance / patrol-guard-shield
    internal Transform Parent;         // 借用 native 父（须 unit lossyScale、无旋转、与 Reference 同场景）
    internal SpriteRenderer Reference; // 借用参照（sortingOrder 必须为 0：R7 unit plane 的排序所有权）
    internal Vector3 Left;             // 左端点（起步点）；left.x < right.x 且与 Right 同 y/z
    internal Vector3 Right;            // 右端点
    internal int WalkLoops;            // 单程 walk 循环数，>= 1（单程 = WalkLoops × 1040ms）
    internal float YScale;             // 自有根 localScale.y；finite 且 0 < y <= 2（x 恒 1）
}

/// <summary>R7 actors-only 计划项：单个院中舞女（脚点由 Root 给出；无 deck/front、无 +22/32 偏移）。</summary>
internal sealed class PalaceActorDancerPlan
{
    internal Transform Parent;
    internal SpriteRenderer Reference; // sortingOrder 必须为 0
    internal Vector3 WorldPosition;    // 脚点（sprite pivot）绝对世界坐标；世界 z 由 Root 取 R7 back/front 之间
    internal float YScale;
}

/// <summary>R7 actors-only 计划：最多 2 守卫（lance/shield 各一、ID 不重复）+ 1 舞女，至少一项。
/// 全部坐标/父/参照由 Root 以当次 CourtBinding 单标定填；本类不测量/不扫描/不找人物/不推算 PPU。</summary>
internal sealed class PalaceActorsPlan
{
    internal List<PalaceActorGuardPlan> Guards;
    internal PalaceActorDancerPlan Dancer; // 可为 null（不建舞女）
}

internal sealed class PalaceWorldExpansion
{
    private const string ResourcePrefix = "KingdomEnhancedMod.KEM_PalaceWorldFull/";
    private const int MaxResourceBytes = 4 * 1024 * 1024;
    private const float Ppu = 32f; // 全批 1x logical import：Unity 纹理 PPU = 32（不用旧 R6 口径/40cell）

    // 已知 13 件建筑/环境资产（world-expansion-r1 已审集合；缺失/多出/重复即拒绝）。
    private static readonly string[] ExpectedArchitectIds =
    {
        "original-tang-tower-exact", "original-tang-tower-clean",
        "tang-side-pavilion-exact", "tang-side-pavilion-clean",
        "tang-garden-pavilion", "tang-covered-walk", "tang-covered-walk-end",
        "court-performance-stage-deck", "court-performance-stage-front",
        "skyline-a", "skyline-b", "mid-wall-corridor-trees", "near-rail-lantern-foliage",
    };

    private static readonly string[] ExpectedActorIds = { "patrol-guard-lance", "patrol-guard-shield", "court-dancer" };

    // R7 actors-only 借用边界：父 unit scale / 无旋转容差与 R7 depth（RequireTransformBasis）一致；自有 YScale 上限。
    private const float BorrowedParentScaleEpsilon = 1e-4f;
    private const float BorrowedParentAngleDeg = 1e-4f;
    private const float MaxActorYScale = 2f;

    // 已知节目时长断言（ms）：与 manifest clip durations 逐项和核对的期望值，不符即拒绝。
    private const int GuardIdleMs = 1500, GuardWalkLoopMs = 1040, GuardStopMs = 1000, GuardGreetMs = 1900, GuardTurnMs = 1420;
    private const int DancerIdleMs = 1940, DancerDanceMs = 4640, DancerBowMs = 2220;

    private static readonly string[] DancerSegmentNames = { "idle", "dance-1", "dance-2", "bow" };
    private static readonly string[] GuardClipOrder = { "idle", "walk", "stop", "greet", "turn" };

    private static readonly JsonDocumentOptions ManifestParseOptions = new JsonDocumentOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>manifest 建筑资产条目（解析后仍保留像素 pivot 供舞台关系断言）。</summary>
    private sealed class ArchEntry
    {
        internal string Id, File;
        internal int Width, Height;
        internal float PixelPivotX, PixelPivotY; // manifest 原始上左像素 pivot
        internal float PivotX, PivotY;           // Unity normalized（bottom-left 原点）
        internal int DeckBaselineRow = -1;       // 仅 deck 使用（舞女脚线行）
        internal Texture2D Texture;
        internal Sprite Sprite;                  // 重复 placement 共享
    }

    /// <summary>clip：显式 FrameIds + 逐帧 durationsMs（累计表预生成，Tick 无分配）。</summary>
    private sealed class ActorClip
    {
        internal string Name;
        internal int[] FrameIds;
        internal int[] DurationsMs;
        internal int[] CumMs;
        internal int TotalMs;
        internal string EndFacing;
    }

    private sealed class ActorEntry
    {
        internal string Id, Atlas;
        internal int Columns, Rows, CellW, CellH, AtlasW, AtlasH;
        internal float PivotX, PivotY; // normalized bottom-left（frame 内）
        internal PixelRect[] FrameRects;
        internal int[] FrameDurations;
        internal Dictionary<string, ActorClip> Clips;
        internal Texture2D Texture;
        internal Sprite[] Sprites; // FrameID → Sprite（同 rect 去重复用；构建一次共享）
        internal int FrameCount => FrameRects.Length;
    }

    /// <summary>守卫相位：clip 引用 + 时长 + flip + 位置段（walk 为 From→To 连续插值，其余相位 From==To==端点）。</summary>
    private readonly struct GuardPhase
    {
        internal readonly int ClipIndex;
        internal readonly double DurationMs;
        internal readonly bool FlipX, Moving;
        internal readonly Vector3 From, To;
        internal readonly string Name;

        internal GuardPhase(int clipIndex, double durationMs, bool flipX, bool moving,
            Vector3 from, Vector3 to, string name)
        {
            ClipIndex = clipIndex; DurationMs = durationMs; FlipX = flipX; Moving = moving;
            From = from; To = to; Name = name;
        }
    }

    private sealed class GuardRuntime
    {
        internal string Label;
        internal SpriteRenderer Renderer;
        internal Sprite[] Sprites;
        internal ActorClip[] Clips;       // 按 GuardClipOrder
        internal GuardPhase[] Phases;
        internal double[] CumMs;
        internal double TotalCycleMs, CycleMs;
        internal int LastPhaseIndex = -1;
        internal int LastFrameId = -1;
        internal bool Stopped;
    }

    private sealed class DancerRuntime
    {
        internal SpriteRenderer Renderer;
        internal Sprite[] Sprites;
        internal ActorClip[] Segments;    // idle, dance, dance, bow
        internal double[] CumMs;
        internal double TotalCycleMs, CycleMs;
        internal int LastSegmentIndex = -1;
        internal int LastFrameId = -1;
        internal bool Stopped;
    }

    private readonly List<GameObject> _children = new List<GameObject>(24);
    private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>(24);
    private readonly List<Sprite> _sprites = new List<Sprite>(64);
    private readonly List<Texture2D> _textures = new List<Texture2D>(16);
    private readonly List<GuardRuntime> _guards = new List<GuardRuntime>(4);
    private Dictionary<string, ArchEntry> _architecture;
    private Dictionary<string, ActorEntry> _actors;
    private DancerRuntime _dancer;
    private bool _cleared;
    private bool _quarantined;
    private bool _activated;

    /// <summary>native 清理未知时的单个静态保留槽：实例连同清单留存，阻止后续 Create 新分配。</summary>
    private static PalaceWorldExpansion _quarantine;

    /// <summary>本实例已放置的 renderer/sprite 数（供 Root 写成功日志）。</summary>
    internal int PlacedSprites { get; private set; }

    /// <summary>manifest 显式演员帧总数（守卫 30+30 + 舞女 26 = 86；解析期断言）。</summary>
    internal int ActorFrameCount { get; private set; }

    // ------------------------------------------------------------------ 生命周期入口

    /// <summary>
    /// 整批构建（inactive）：任一资源缺失 / manifest 偏离已验收关系 / plan 引用不可用 → 清掉本批自有部分并返回 false，
    /// 不重试、无缺省兜底；失败原因写入 reason。绝不触碰借用对象。静态 quarantine 存在时拒绝任何新分配。
    /// </summary>
    internal static bool Create(PalaceWorldPlan plan, out PalaceWorldExpansion result, out string reason)
    {
        result = null;
        reason = null;
        if (_quarantine != null) { reason = "retained quarantine active; refusing new allocation"; return false; }
        if (plan == null) { reason = "plan is null"; return false; }
        if (plan.Placements == null) { reason = "plan.Placements is null"; return false; }
        if (plan.Guards == null) { reason = "plan.Guards is null"; return false; }
        PalaceWorldExpansion self = new PalaceWorldExpansion();
        try
        {
            self.ParseManifests();
            self.ValidatePlan(plan);
            self.LoadTextures();
            self.Build(plan);
            result = self;
            return true;
        }
        catch (Exception e)
        {
            self.Clear();
            reason = e.GetType().Name + ": " + e.Message
                + (self._quarantined ? "; rollback incomplete: retained quarantine" : "; own parts rolled back");
            return false;
        }
    }

    /// <summary>
    /// R7 actors-only 入口（纯源码 API，未接线）：只解析 actors manifest + 3 atlas，不解析/不加载 architecture，
    /// 不建旧建筑/舞台/静态层，也不产生 Native/身份/存档副作用。顺序固定：manifest 断言 → plan 全量校验（尚未
    /// 分配任何 Unity 对象）→ 演员纹理加载 → 整批 inactive 构建；任一失败 → 统一走既有 Clear 后返回 false，不重试。
    /// Root 在接线（自有参照/时钟条件）完成后调用 Activate。
    /// </summary>
    internal static bool CreateActors(PalaceActorsPlan plan, out PalaceWorldExpansion result, out string reason)
    {
        result = null;
        reason = null;
        if (_quarantine != null) { reason = "retained quarantine active; refusing new allocation"; return false; }
        if (plan == null) { reason = "actors plan is null"; return false; }
        if (plan.Guards == null) { reason = "actors plan.Guards is null"; return false; }
        PalaceWorldExpansion self = new PalaceWorldExpansion();
        try
        {
            self._actors = ParseActorManifest();
            self.ActorFrameCount = CountActorFrames(self._actors);
            self.ValidateActorsPlan(plan);
            self.LoadActorTextures();
            self.BuildActors(plan);
            result = self;
            return true;
        }
        catch (Exception e)
        {
            self.Clear();
            // 清理不能确证（retained quarantine）时把实例交回 Root：Root 保住该 owner/outer root，
            // 不会因 result 为 null 就丢弃仍在借用 native 父的自有根；旧 Create 语义不变（仍 result=null）。
            if (self._quarantined) result = self;
            reason = e.GetType().Name + ": " + e.Message
                + (self._quarantined ? "; rollback incomplete: retained quarantine" : "; own parts rolled back");
            return false;
        }
    }

    /// <summary>整批激活（Create 成功后由 Root 调用一次）：全部自有 children SetActive(true)；失败 → quarantine。</summary>
    internal bool Activate(out string reason)
    {
        reason = null;
        if (_cleared || _quarantined) { reason = "owner cleared or quarantined"; return false; }
        if (_activated) return true;
        try
        {
            foreach (GameObject child in _children)
            {
                if (child == null) throw new InvalidOperationException("owned root disappeared before activation");
                child.SetActive(true);
            }
            _activated = true;
            Log("activated own roots " + _children.Count + " (placedSprites=" + PlacedSprites + ")");
            return true;
        }
        catch (Exception e)
        {
            reason = "activation failed: " + e.GetType().Name;
            Clear(); // One cleanup attempt; Clear tries every owned root before retaining an unknown cleanup.
            if (_quarantined) reason += "; cleanup unknown, retained quarantine";
            Log(reason);
            return false;
        }
    }

    /// <summary>
    /// Root 每帧驱动（时钟条件归 Root）：!playing 或 dt 为 NaN/±Inf/≤0 时冻结不推进。
    /// 实例时钟 bounded modulo；仅帧变化写 sprite、仅相位变化写 flip/位置/日志；renderer 故障停该 actor 一次。
    /// </summary>
    internal void Tick(float deltaTime, bool playing)
    {
        if (!_activated || _cleared || _quarantined) return;
        if (!playing || !(deltaTime > 0f) || float.IsInfinity(deltaTime)) return; // NaN 比较 false → 同样忽略
        for (int i = 0; i < _guards.Count; i++) TickGuard(_guards[i], deltaTime);
        if (_dancer != null) TickDancer(_dancer, deltaTime);
    }

    /// <summary>
    /// 确认式清理，只动自有对象：1. 先停全部实例时钟（Clear 后误触 Tick 也不写 sprite）；
    /// 2. 停用自有 children；3. 逐个解除自有 renderer 的 sprite 并回读确认 null——借用者解除明确前不销毁资产；
    /// 4. 全部确证后才销毁 children/sprites/textures 并清空清单。借用 Parent/material/参照全程不动。幂等；
    /// 任一步未知 → 立即停手并静态单个 quarantine + 日志（不清空清单、不重试）。
    /// </summary>
    internal void Clear()
    {
        if (_cleared || _quarantined) return;
        foreach (GuardRuntime guard in _guards) guard.Stopped = true; // 先停时钟，再动资产
        if (_dancer != null) _dancer.Stopped = true;
        bool deactivateUnknown = false;
        foreach (GameObject child in _children)
        {
            try { if (child != null) child.SetActive(false); }
            catch (Exception) { deactivateUnknown = true; }
        }
        if (deactivateUnknown) { Quarantine("child deactivate fault; all roots attempted once, own lists retained"); return; }
        foreach (SpriteRenderer renderer in _renderers)
        {
            if (!TryDetach(renderer)) { Quarantine("renderer sprite detach unconfirmed; assets retained"); return; }
        }
        foreach (GameObject child in _children)
        {
            if (!DestroyChecked(child)) { Quarantine("child destroy fault; own lists retained"); return; }
        }
        foreach (Sprite sprite in _sprites)
        {
            if (!DestroyChecked(sprite)) { Quarantine("sprite destroy fault; own lists retained"); return; }
        }
        foreach (Texture2D texture in _textures)
        {
            if (!DestroyChecked(texture)) { Quarantine("texture destroy fault; own lists retained"); return; }
        }
        _children.Clear();
        _renderers.Clear();
        _sprites.Clear();
        _textures.Clear();
        _guards.Clear();
        _dancer = null;
        if (_architecture != null)
        {
            foreach (ArchEntry entry in _architecture.Values) { entry.Texture = null; entry.Sprite = null; }
        }
        if (_actors != null)
        {
            foreach (ActorEntry entry in _actors.Values) { entry.Texture = null; entry.Sprites = null; }
        }
        _architecture = null;
        _actors = null;
        PlacedSprites = 0;
        ActorFrameCount = 0;
        _cleared = true;
    }

    /// <summary>
    /// Root 清理入口（旧/新路径共用）：只调用既有 Clear 一次（Clear 幂等），返回 _cleared &amp;&amp; !_quarantined；
    /// 已 quarantine 直接 false 不重试。false 表示 native 清理未确证，实例仍保留清单与 quarantine，新 Create 被阻断。
    /// </summary>
    internal bool TryClear(out string reason)
    {
        if (_quarantined) { reason = "retained quarantine; clear not retried"; return false; }
        Clear();
        if (_cleared && !_quarantined) { reason = null; return true; }
        reason = _quarantined ? "cleanup unknown; retained quarantine" : "cleanup incomplete";
        return false;
    }

    // ------------------------------------------------------------------ manifest 解析（严格，无缺省兜底）

    private void ParseManifests()
    {
        _architecture = ParseArchitectureManifest();
        _actors = ParseActorManifest();
        ActorFrameCount = CountActorFrames(_actors);
    }

    /// <summary>manifest 显式演员帧总数断言（守卫 30+30 + 舞女 26 = 86）；两个入口共用，不符即拒绝。</summary>
    private static int CountActorFrames(Dictionary<string, ActorEntry> actors)
    {
        int frames = 0;
        foreach (KeyValuePair<string, ActorEntry> pair in actors) frames += pair.Value.FrameCount;
        if (frames != 86)
            throw new InvalidDataException("explicit actor frames " + frames + " != 86 (30+30+26)");
        return frames;
    }

    private static Dictionary<string, ArchEntry> ParseArchitectureManifest()
    {
        using JsonDocument document = ParseManifestJson("architecture/manifest.json");
        JsonElement assets = RequireArray(document.RootElement, "assets", "architecture");
        var map = new Dictionary<string, ArchEntry>(StringComparer.Ordinal);
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string id = RequireString(asset, "id", "architecture asset");
            string context = "architecture " + id;
            string file = ValidateRelative(RequireString(asset, "file", context));
            (int width, int height) = ReadIntPair(RequireArray(asset, "logicalCanvas", context), context + " logicalCanvas");
            if (width <= 0 || height <= 0)
                throw new InvalidDataException(context + ": canvas " + width + "x" + height);
            int multiplier = RequireInt(asset, "pixelMultiplier", context);
            if (multiplier != 1)
                throw new InvalidDataException(context + ": pixelMultiplier " + multiplier + " != 1 (仅支持 1x logical 导入)");
            float logicalPpu = RequireFiniteFloat(asset, "logicalPixelsPerUnitCandidate", context);
            if (logicalPpu != Ppu)
                throw new InvalidDataException(context + ": logicalPpu " + logicalPpu + " != " + Ppu);
            (float pivotX, float pivotY) = ReadFloatPair(RequireArray(asset, "pivot", context), context + " pivot");
            if (!(pivotX >= 0f) || !(pivotX <= width) || !(pivotY >= 0f) || !(pivotY <= height))
                throw new InvalidDataException(context + ": pivot [" + pivotX + "," + pivotY + "] outside canvas");
            var entry = new ArchEntry
            {
                Id = id,
                File = file,
                Width = width,
                Height = height,
                PixelPivotX = pivotX,
                PixelPivotY = pivotY,
                // 上左原点 pivot → Unity normalized（bottom-left 原点）：x/w，1 − y/h。
                PivotX = pivotX / width,
                PivotY = 1f - pivotY / height,
            };
            if (id == "court-performance-stage-deck")
                entry.DeckBaselineRow = RequireInt(asset, "deckBaselineRow", context);
            if (!map.TryAdd(id, entry))
                throw new InvalidDataException("architecture duplicate id " + id);
        }
        if (map.Count != ExpectedArchitectIds.Length)
            throw new InvalidDataException("architecture asset count " + map.Count + " != " + ExpectedArchitectIds.Length);
        foreach (string expected in ExpectedArchitectIds)
        {
            if (!map.ContainsKey(expected))
                throw new InvalidDataException("architecture missing id " + expected);
        }
        // 舞台 r1 已验收关系断言（不允许静默换锚）：deck 144×88、pivot[72,87]、deckBaselineRow 65
        // → 舞女脚线 = deck pivot + (87−65)/32；front 144×14、pivot[72,14]（normalized (0.5,0)）。
        ArchEntry deck = map["court-performance-stage-deck"];
        if (deck.Width != 144 || deck.Height != 88
            || deck.PixelPivotX != 72f || deck.PixelPivotY != 87f || deck.DeckBaselineRow != 65)
            throw new InvalidDataException("stage deck manifest no longer matches r1 relation (144x88 pivot[72,87] baseline 65)");
        float dancerOffset = (deck.PixelPivotY - deck.DeckBaselineRow) / Ppu;
        if (dancerOffset != PalaceStageData.StageSurfaceYOffset)
            throw new InvalidDataException("deck dancer-foot offset " + dancerOffset + " != PalaceStageData.StageSurfaceYOffset");
        ArchEntry front = map["court-performance-stage-front"];
        if (front.Width != 144 || front.Height != 14 || front.PixelPivotX != 72f || front.PixelPivotY != 14f)
            throw new InvalidDataException("stage front manifest no longer matches r1 relation (144x14 pivot[72,14])");
        return map;
    }

    /// <summary>R7 actors-only 付前资源契约（只读纯解析）：复用 loader 的 ParseActorManifest/CountActorFrames
    /// （id 集合、86 显式帧、clip/rect/duration 契约全部断言），导出 actors/manifest.json 声明的每个 atlas
    /// 与尺寸；不解析/不加载 architecture（本 profile 不消费）。</summary>
    internal static bool TryDescribeActorsResourceContract(List<PalaceResourceContract> into, out string reason)
    {
        reason = "";
        if (into == null) { reason = "into-null"; return false; }
        try
        {
            Dictionary<string, ActorEntry> actors = ParseActorManifest();
            CountActorFrames(actors);
            foreach (KeyValuePair<string, ActorEntry> pair in actors)
                into.Add(new PalaceResourceContract(ResourcePrefix + "actors/" + pair.Value.Atlas, null,
                    pair.Value.AtlasW, pair.Value.AtlasH, MaxResourceBytes, "r7-actor:" + pair.Key));
            return true;
        }
        catch (Exception e) { reason = "actors-manifest:" + e.Message; return false; }
    }

    private static Dictionary<string, ActorEntry> ParseActorManifest()
    {
        using JsonDocument document = ParseManifestJson("actors/manifest.json");
        JsonElement characters = RequireArray(document.RootElement, "characters", "actors");
        var map = new Dictionary<string, ActorEntry>(StringComparer.Ordinal);
        foreach (JsonElement element in characters.EnumerateArray())
        {
            ActorEntry entry = ParseActor(element);
            if (!map.TryAdd(entry.Id, entry))
                throw new InvalidDataException("actor duplicate id " + entry.Id);
        }
        if (map.Count != ExpectedActorIds.Length)
            throw new InvalidDataException("actor count " + map.Count + " != " + ExpectedActorIds.Length);
        foreach (string expected in ExpectedActorIds)
        {
            if (!map.ContainsKey(expected))
                throw new InvalidDataException("actor missing id " + expected);
        }
        return map;
    }

    private static ActorEntry ParseActor(JsonElement element)
    {
        string id = RequireString(element, "id", "actor");
        string context = "actor " + id;
        string atlas = ValidateRelative(RequireString(element, "atlas", context));
        (int logicalW, int logicalH) = ReadIntPair(RequireArray(element, "logicalCell", context), context + " logicalCell");
        (int cellW, int cellH) = ReadIntPair(RequireArray(element, "cell", context), context + " cell");
        if (cellW <= 0 || cellH <= 0 || logicalW != cellW || logicalH != cellH)
            throw new InvalidDataException(context + ": logicalCell/cell mismatch " + logicalW + "x" + logicalH + " vs " + cellW + "x" + cellH);
        int multiplier = RequireInt(element, "pixelMultiplier", context);
        if (multiplier != 1)
            throw new InvalidDataException(context + ": pixelMultiplier " + multiplier + " != 1 (仅支持 1x logical 导入)");
        float ppu = RequireFiniteFloat(element, "logicalPixelsPerUnit32", context);
        if (ppu != Ppu)
            throw new InvalidDataException(context + ": ppu " + ppu + " != " + Ppu);
        int columns = RequireInt(element, "columns", context);
        int rows = RequireInt(element, "rows", context);
        if (columns <= 0 || rows <= 0)
            throw new InvalidDataException(context + ": columns/rows " + columns + "x" + rows);
        (float pivotX, float pivotY) = ReadFloatPair(RequireArray(element, "fixedPivotPixelsBottomLeft", context), context + " pivot");
        if (!(pivotX >= 0f) || !(pivotX <= cellW) || !(pivotY >= 0f) || !(pivotY <= cellH))
            throw new InvalidDataException(context + ": pivot [" + pivotX + "," + pivotY + "] outside cell");

        var entry = new ActorEntry
        {
            Id = id,
            Atlas = atlas,
            Columns = columns,
            Rows = rows,
            CellW = cellW,
            CellH = cellH,
            AtlasW = columns * cellW,
            AtlasH = rows * cellH,
            PivotX = pivotX / cellW, // frame 内 bottom-left 像素 pivot → normalized
            PivotY = pivotY / cellH,
            Clips = new Dictionary<string, ActorClip>(8, StringComparer.Ordinal),
        };

        // frames：显式 rect 表（index 全覆盖、rect 限 atlas 内、整格尺寸）；durationMs 留作 clip 一致性核对。
        JsonElement frames = RequireArray(element, "frames", context);
        entry.FrameRects = new PixelRect[frames.GetArrayLength()];
        entry.FrameDurations = new int[entry.FrameRects.Length];
        bool[] seen = new bool[entry.FrameRects.Length];
        foreach (JsonElement frame in frames.EnumerateArray())
        {
            string frameContext = context + " frame";
            int index = RequireInt(frame, "index", frameContext);
            if (index < 0 || index >= entry.FrameRects.Length || seen[index])
                throw new InvalidDataException(frameContext + " index " + index + " invalid or duplicate");
            seen[index] = true;
            (int rx, int ry, int rw, int rh) = ReadIntQuad(RequireArray(frame, "rectPixelsTopLeft", frameContext), frameContext + " rect");
            if (rw != cellW || rh != cellH)
                throw new InvalidDataException(frameContext + " rect " + rw + "x" + rh + " != cell " + cellW + "x" + cellH);
            if (rx < 0 || ry < 0 || rx + rw > entry.AtlasW || ry + rh > entry.AtlasH)
                throw new InvalidDataException(frameContext + " rect [" + rx + "," + ry + "] outside atlas "
                    + entry.AtlasW + "x" + entry.AtlasH);
            entry.FrameRects[index] = new PixelRect(rx, ry, rw, rh);
            entry.FrameDurations[index] = RequireInt(frame, "durationMs", frameContext);
        }
        for (int i = 0; i < seen.Length; i++)
        {
            if (!seen[i]) throw new InvalidDataException(context + " frame " + i + " missing from manifest");
        }

        JsonElement clips = RequireObject(element, "clips", context);
        foreach (JsonProperty property in clips.EnumerateObject())
        {
            ActorClip clip = ParseClip(property.Name, property.Value, context + " clip " + property.Name, entry.FrameRects.Length);
            entry.Clips[property.Name] = clip;
        }
        // frame.durationMs 与 clip durationsMs 逐槽一致（两处都显式，必须互相印证）。
        foreach (KeyValuePair<string, ActorClip> pair in entry.Clips)
        {
            ActorClip clip = pair.Value;
            for (int i = 0; i < clip.FrameIds.Length; i++)
            {
                int frameId = clip.FrameIds[i];
                if (clip.DurationsMs[i] != entry.FrameDurations[frameId])
                    throw new InvalidDataException(context + " clip " + pair.Key + " slot " + i + " duration "
                        + clip.DurationsMs[i] + " != frame " + frameId + " durationMs " + entry.FrameDurations[frameId]);
            }
        }

        if (id == "court-dancer") AssertDancerProgram(entry, context);
        else AssertGuardProgram(entry, context);
        return entry;
    }

    private static ActorClip ParseClip(string name, JsonElement element, string context, int frameCount)
    {
        int[] indices = ReadIntArray(RequireArray(element, "indices", context), context + " indices");
        int[] durations = ReadIntArray(RequireArray(element, "durationsMs", context), context + " durationsMs");
        if (indices.Length == 0 || indices.Length != durations.Length)
            throw new InvalidDataException(context + ": indices/durations length " + indices.Length + "/" + durations.Length);
        var clip = new ActorClip { Name = name, FrameIds = indices, DurationsMs = durations, CumMs = new int[indices.Length] };
        int total = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0 || indices[i] >= frameCount)
                throw new InvalidDataException(context + ": frame id " + indices[i] + " outside explicit table");
            if (durations[i] <= 0)
                throw new InvalidDataException(context + ": duration " + durations[i] + " <= 0");
            total += durations[i];
            clip.CumMs[i] = total;
        }
        clip.TotalMs = total;
        if (element.TryGetProperty("endFacing", out JsonElement facing) && facing.ValueKind == JsonValueKind.String)
            clip.EndFacing = facing.GetString();
        return clip;
    }

    /// <summary>守卫节目断言：5 clip 的帧数/总时长/连续起点，共 30 帧；turn 尾帧朝向以 clip endFacing=left 为准
    /// （frame29 的 facing=right 是 manifest 已知误标，运行时不读 frame facing）。</summary>
    private static void AssertGuardProgram(ActorEntry entry, string context)
    {
        RequireClip(entry, "idle", 5, GuardIdleMs, 0, context);
        RequireClip(entry, "walk", 8, GuardWalkLoopMs, 5, context);
        RequireClip(entry, "stop", 5, GuardStopMs, 13, context);
        RequireClip(entry, "greet", 7, GuardGreetMs, 18, context);
        ActorClip turn = RequireClip(entry, "turn", 5, GuardTurnMs, 25, context);
        if (entry.FrameCount != 30)
            throw new InvalidDataException(context + ": frame count " + entry.FrameCount + " != 30");
        if (turn.EndFacing != "left")
            throw new InvalidDataException(context + ": turn endFacing '" + turn.EndFacing + "' != left (r1 已验收朝向关系被改动)");
    }

    /// <summary>舞女节目断言：idle 4 帧/1940ms、dance 15 帧/4640ms、bow 7 帧/2220ms，共 26 帧。</summary>
    private static void AssertDancerProgram(ActorEntry entry, string context)
    {
        RequireClip(entry, "idle", 4, DancerIdleMs, 0, context);
        RequireClip(entry, "dance", 15, DancerDanceMs, 4, context);
        RequireClip(entry, "bow", 7, DancerBowMs, 19, context);
        if (entry.FrameCount != 26)
            throw new InvalidDataException(context + ": frame count " + entry.FrameCount + " != 26");
    }

    private static ActorClip RequireClip(ActorEntry entry, string name, int count, int totalMs, int firstIndex, string context)
    {
        if (!entry.Clips.TryGetValue(name, out ActorClip clip))
            throw new InvalidDataException(context + ": missing clip " + name);
        if (clip.FrameIds.Length != count)
            throw new InvalidDataException(context + ": clip " + name + " count " + clip.FrameIds.Length + " != " + count);
        if (clip.TotalMs != totalMs)
            throw new InvalidDataException(context + ": clip " + name + " total " + clip.TotalMs + "ms != " + totalMs + "ms");
        for (int i = 0; i < clip.FrameIds.Length; i++)
        {
            if (clip.FrameIds[i] != firstIndex + i)
                throw new InvalidDataException(context + ": clip " + name + " indices not contiguous from " + firstIndex);
        }
        return clip;
    }

    // ------------------------------------------------------------------ plan 校验

    private void ValidatePlan(PalaceWorldPlan plan)
    {
        foreach (PalaceWorldPlacement placement in plan.Placements)
        {
            if (placement == null) throw new InvalidDataException("placement entry is null");
            string context = "placement " + placement.AssetId;
            if (string.IsNullOrEmpty(placement.AssetId) || !_architecture.ContainsKey(placement.AssetId))
                throw new InvalidDataException(context + ": unknown architecture id");
            CheckBorrowed(placement.Parent, placement.Reference, context);
            CheckFinite(placement.Position, context + " position");
        }
        foreach (PalaceWorldGuardRoute route in plan.Guards)
        {
            if (route == null) throw new InvalidDataException("guard route entry is null");
            string context = "guard " + route.ActorId;
            if (string.IsNullOrEmpty(route.ActorId) || !_actors.TryGetValue(route.ActorId, out ActorEntry actor))
                throw new InvalidDataException(context + ": unknown actor id");
            if (!actor.Clips.ContainsKey("greet"))
                throw new InvalidDataException(context + ": actor lacks patrol program (idle/walk/stop/greet/turn)");
            CheckBorrowed(route.Parent, route.Reference, context);
            CheckFinite(route.Left, context + " left");
            CheckFinite(route.Right, context + " right");
            if (route.WalkLoops < 1)
                throw new InvalidDataException(context + ": walkLoops " + route.WalkLoops + " < 1");
            if ((long)route.WalkLoops * GuardWalkLoopMs > 100L * 24 * 60 * 60 * 1000)
                throw new InvalidDataException(context + ": walkLoops " + route.WalkLoops + " unreasonably large");
            if (!(route.Left.x < route.Right.x))
                throw new InvalidDataException(context + ": route must satisfy left.x < right.x");
        }
        if (plan.Stage != null)
        {
            PalaceWorldStage stage = plan.Stage;
            CheckBorrowed(stage.Parent, stage.Reference, "stage");
            CheckFinite(stage.Position, "stage position");
        }
    }

    /// <summary>
    /// R7 actors-only plan 全量校验（先于任何 Unity 分配）：上限 2 守卫/1 舞女、守卫 ID 唯一且限 lance/shield、
    /// 借用父/参照存在 + 同场景 + unit scale/无旋转、Reference.sortingOrder==0、端点 finite 且 left.x&lt;right.x 同 y/z、
    /// 自有 YScale∈(0,2]。任一偏离 → 具体失败（无静默兜底），交 CreateActors 统一 Clear。
    /// </summary>
    private void ValidateActorsPlan(PalaceActorsPlan plan)
    {
        int guardCount = plan.Guards.Count;
        if (guardCount > 2)
            throw new InvalidDataException("actors plan guard count " + guardCount + " > 2");
        if (guardCount == 0 && plan.Dancer == null)
            throw new InvalidDataException("actors plan has neither guard nor dancer");
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < guardCount; i++)
        {
            PalaceActorGuardPlan guard = plan.Guards[i];
            if (guard == null) throw new InvalidDataException("guard plan entry " + i + " is null");
            string context = "actor guard " + i;
            if (!IsGuardActorId(guard.ActorId))
                throw new InvalidDataException(context + ": actorId '" + guard.ActorId
                    + "' not in {patrol-guard-lance, patrol-guard-shield}");
            if (!_actors.TryGetValue(guard.ActorId, out ActorEntry actor))
                throw new InvalidDataException(context + ": unknown actor id " + guard.ActorId);
            if (!actor.Clips.ContainsKey("greet"))
                throw new InvalidDataException(context + ": actor " + guard.ActorId
                    + " lacks patrol program (idle/walk/stop/greet/turn)");
            if (!seenIds.Add(guard.ActorId))
                throw new InvalidDataException(context + ": duplicate actor id " + guard.ActorId);
            CheckBorrowedInSameScene(guard.Parent, guard.Reference, context);
            CheckBorrowedUnitParent(guard.Parent, context);
            CheckReferenceOrderZero(guard.Reference, context);
            CheckFinite(guard.Left, context + " left");
            CheckFinite(guard.Right, context + " right");
            if (!(guard.Left.x < guard.Right.x))
                throw new InvalidDataException(context + ": route must satisfy left.x < right.x");
            if (guard.Left.y != guard.Right.y || guard.Left.z != guard.Right.z)
                throw new InvalidDataException(context + ": route endpoints must share y/z: left " + guard.Left
                    + " right " + guard.Right);
            if (guard.WalkLoops < 1)
                throw new InvalidDataException(context + ": walkLoops " + guard.WalkLoops + " < 1");
            if ((long)guard.WalkLoops * GuardWalkLoopMs > 100L * 24 * 60 * 60 * 1000)
                throw new InvalidDataException(context + ": walkLoops " + guard.WalkLoops + " unreasonably large");
            CheckOwnedYScale(guard.YScale, context);
        }
        if (plan.Dancer != null)
        {
            string context = "actor dancer";
            CheckBorrowedInSameScene(plan.Dancer.Parent, plan.Dancer.Reference, context);
            CheckBorrowedUnitParent(plan.Dancer.Parent, context);
            CheckReferenceOrderZero(plan.Dancer.Reference, context);
            CheckFinite(plan.Dancer.WorldPosition, context + " worldPosition");
            CheckOwnedYScale(plan.Dancer.YScale, context);
        }
    }

    private static bool IsGuardActorId(string actorId)
        => actorId is "patrol-guard-lance" or "patrol-guard-shield";

    /// <summary>借用对象可用且父/参照同一有效场景（跨场景会在不可见层级落位，且生命周期归属不清）。</summary>
    private static void CheckBorrowedInSameScene(Transform parent, SpriteRenderer reference, string context)
    {
        CheckBorrowed(parent, reference, context);
        Scene parentScene, referenceScene;
        try
        {
            parentScene = parent.gameObject.scene;
            referenceScene = reference.gameObject.scene;
        }
        catch (Exception e) { throw new InvalidDataException(context + ": scene read fault (" + e.GetType().Name + ")"); }
        if (!parentScene.IsValid() || !referenceScene.IsValid() || parentScene.handle != referenceScene.handle)
            throw new InvalidDataException(context + ": parent/reference not in the same valid scene ("
                + parentScene.handle + "/" + referenceScene.handle + ")");
    }

    /// <summary>借用父须 unit lossyScale 且无旋转：自有根直接落绝对世界坐标并只改 y 缩放，父的缩放/旋转会破坏该语义。</summary>
    private static void CheckBorrowedUnitParent(Transform parent, string context)
    {
        Vector3 scale;
        float angle;
        try
        {
            scale = parent.lossyScale;
            angle = Quaternion.Angle(parent.rotation, Quaternion.identity);
        }
        catch (Exception e) { throw new InvalidDataException(context + ": parent transform read fault (" + e.GetType().Name + ")"); }
        if (!float.IsFinite(scale.x) || !float.IsFinite(scale.y) || !float.IsFinite(scale.z)
            || Math.Abs(scale.x - 1f) > BorrowedParentScaleEpsilon
            || Math.Abs(scale.y - 1f) > BorrowedParentScaleEpsilon
            || Math.Abs(scale.z - 1f) > BorrowedParentScaleEpsilon)
            throw new InvalidDataException(context + ": parent lossyScale " + scale + " not unit");
        if (!float.IsFinite(angle) || angle > BorrowedParentAngleDeg)
            throw new InvalidDataException(context + ": parent rotation not identity (" + angle + " deg)");
    }

    /// <summary>R7 unit plane 的排序所有权在 Root 提供的参照（order 0）；演员不叠加任何 order 偏移。</summary>
    private static void CheckReferenceOrderZero(SpriteRenderer reference, string context)
    {
        int order;
        try { order = reference.sortingOrder; }
        catch (Exception e) { throw new InvalidDataException(context + ": reference sortingOrder read fault (" + e.GetType().Name + ")"); }
        if (order != 0)
            throw new InvalidDataException(context + ": reference sortingOrder " + order + " != 0 (R7 unit plane owns order 0)");
    }

    /// <summary>自有 YScale 边界：只缩放 y 轴，finite 且 (0, 2]；具体身高/PPU 换算由 Root 标定，本类不硬编码像素高。</summary>
    private static void CheckOwnedYScale(float yScale, string context)
    {
        if (!float.IsFinite(yScale) || !(yScale > 0f) || yScale > MaxActorYScale)
            throw new InvalidDataException(context + ": yScale " + yScale + " must be finite in (0, " + MaxActorYScale + "]");
    }

    /// <summary>借用对象可用性：Unity destroyed 假 null 与 native 读取异常同样视为不可用。</summary>
    private static void CheckBorrowed(Transform parent, SpriteRenderer reference, string context)
    {
        bool alive = false;
        try { alive = parent != null && parent.gameObject != null; }
        catch (Exception) { alive = false; }
        if (!alive) throw new InvalidDataException(context + ": parent null/destroyed/native read fault");
        alive = false;
        try { alive = reference != null && reference.gameObject != null; }
        catch (Exception) { alive = false; }
        if (!alive) throw new InvalidDataException(context + ": reference renderer null/destroyed/native read fault");
    }

    private static void CheckFinite(Vector3 value, string context)
    {
        if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
            throw new InvalidDataException(context + " not finite: " + value);
    }

    // ------------------------------------------------------------------ 构建期（整批 inactive）

    /// <summary>13 建筑 + 3 演员图集一次性加载（严格尺寸校验；Point/Clamp/aniso0）。</summary>
    private void LoadTextures()
    {
        foreach (ArchEntry entry in _architecture.Values)
            entry.Texture = LoadTexture("architecture/" + entry.File, entry.Width, entry.Height);
        LoadActorTextures();
    }

    /// <summary>3 演员图集加载（R7 actors-only 入口复用；不触碰 architecture）。</summary>
    private void LoadActorTextures()
    {
        foreach (ActorEntry entry in _actors.Values)
            entry.Texture = LoadTexture("actors/" + entry.Atlas, entry.AtlasW, entry.AtlasH);
    }

    private void Build(PalaceWorldPlan plan)
    {
        foreach (PalaceWorldPlacement placement in plan.Placements)
        {
            ArchEntry entry = _architecture[placement.AssetId];
            AddRoot(placement.Parent, entry.File, GetArchSprite(entry), placement.Position,
                placement.Reference, placement.OrderOffset);
        }
        foreach (PalaceWorldGuardRoute route in plan.Guards) PlaceGuard(route);
        if (plan.Stage != null) PlaceStage(plan.Stage);
        Log("world expansion created: architecture=" + _architecture.Count + " actorAtlases=" + _actors.Count
            + " actorFrames=" + ActorFrameCount + " textures=" + _textures.Count + " sprites=" + _sprites.Count
            + " placements=" + plan.Placements.Count + " guards=" + _guards.Count
            + " stage=" + (plan.Stage != null ? "built" : "none") + " placedSprites=" + PlacedSprites);
    }

    private Sprite GetArchSprite(ArchEntry entry)
    {
        if (entry.Sprite == null)
        {
            entry.Sprite = OwnSprite(entry.Texture, new Rect(0f, 0f, entry.Width, entry.Height),
                new Vector2(entry.PivotX, entry.PivotY), Ppu, entry.File);
        }
        return entry.Sprite;
    }

    /// <summary>守卫：10 相位循环（右向半环 + 左向半环镜像），时长全部取自已断言的 manifest clip 总时长。</summary>
    private void PlaceGuard(PalaceWorldGuardRoute route)
    {
        ActorEntry actor = _actors[route.ActorId];
        Sprite[] sprites = EnsureActorSprites(actor);
        var clips = new ActorClip[GuardClipOrder.Length];
        for (int i = 0; i < GuardClipOrder.Length; i++) clips[i] = actor.Clips[GuardClipOrder[i]];
        double walkMs = route.WalkLoops * (double)clips[1].TotalMs; // N × 1040ms，整循环结束保证 stop 帧 13 关系成立
        var phases = new GuardPhase[10];
        // 右向半环：walk(R)→stop→greet→idle→turn(→左)；turn flipX=false（尾帧 29 实际画向左）。
        phases[0] = new GuardPhase(1, walkMs, false, true, route.Left, route.Right, "walk-right");
        phases[1] = new GuardPhase(2, clips[2].TotalMs, false, false, route.Right, route.Right, "stop-right");
        phases[2] = new GuardPhase(3, clips[3].TotalMs, false, false, route.Right, route.Right, "greet-right");
        phases[3] = new GuardPhase(0, clips[0].TotalMs, false, false, route.Right, route.Right, "idle-right");
        phases[4] = new GuardPhase(4, clips[4].TotalMs, false, false, route.Right, route.Right, "turn-to-left");
        // 左向半环：walk/stop/greet/idle flipX=true；turn(→右) flipX=true（镜像后尾帧呈现向右）。
        phases[5] = new GuardPhase(1, walkMs, true, true, route.Right, route.Left, "walk-left");
        phases[6] = new GuardPhase(2, clips[2].TotalMs, true, false, route.Left, route.Left, "stop-left");
        phases[7] = new GuardPhase(3, clips[3].TotalMs, true, false, route.Left, route.Left, "greet-left");
        phases[8] = new GuardPhase(0, clips[0].TotalMs, true, false, route.Left, route.Left, "idle-left");
        phases[9] = new GuardPhase(4, clips[4].TotalMs, true, false, route.Left, route.Left, "turn-to-right");
        var cumMs = new double[phases.Length];
        double total = 0.0;
        for (int i = 0; i < phases.Length; i++)
        {
            total += phases[i].DurationMs;
            cumMs[i] = total;
        }
        int initialFrame = clips[1].FrameIds[0]; // 左端点起步右行：walk 首帧
        SpriteRenderer renderer = AddRoot(route.Parent, route.ActorId, sprites[initialFrame], route.Left,
            route.Reference, route.OrderOffset);
        _guards.Add(new GuardRuntime
        {
            Label = route.ActorId + "#" + _guards.Count,
            Renderer = renderer,
            Sprites = sprites,
            Clips = clips,
            Phases = phases,
            CumMs = cumMs,
            TotalCycleMs = total,
            LastFrameId = initialFrame,
        });
    }

    /// <summary>舞台：deck（=Stage.Position）+ 舞女（脚线 +22/32）+ front（pivot +21/32）；舞女 idle→dance×2→bow 循环。</summary>
    private void PlaceStage(PalaceWorldStage stage)
    {
        ArchEntry deck = _architecture["court-performance-stage-deck"];
        ArchEntry front = _architecture["court-performance-stage-front"];
        ActorEntry dancer = _actors["court-dancer"];
        Sprite[] sprites = EnsureActorSprites(dancer);
        int initialFrame = dancer.Clips["idle"].FrameIds[0];

        AddRoot(stage.Parent, deck.File, GetArchSprite(deck), stage.Position, stage.Reference, stage.DeckOrderOffset);
        Vector3 dancerPos = stage.Position + new Vector3(0f, PalaceStageData.StageSurfaceYOffset, 0f);
        SpriteRenderer dancerRenderer = AddRoot(stage.Parent, dancer.Atlas, sprites[initialFrame], dancerPos,
            stage.Reference, stage.DancerOrderOffset); // flipX 恒 false（AddRoot 默认）
        Vector3 frontPos = stage.Position + new Vector3(0f, PalaceStageData.FrontPivotYOffset, 0f);
        AddRoot(stage.Parent, front.File, GetArchSprite(front), frontPos, stage.Reference, stage.FrontOrderOffset);

        _dancer = NewDancerRuntime(dancer, dancerRenderer, sprites);
    }

    /// <summary>舞女片段表（构建一次）：idle1940 → dance4640×2 → bow2220；旧舞台与 R7 actors-only 入口共用同一节目。</summary>
    private static DancerRuntime NewDancerRuntime(ActorEntry dancer, SpriteRenderer renderer, Sprite[] sprites)
    {
        var segments = new[] { dancer.Clips["idle"], dancer.Clips["dance"], dancer.Clips["dance"], dancer.Clips["bow"] };
        var cumMs = new double[segments.Length];
        double total = 0.0;
        for (int i = 0; i < segments.Length; i++)
        {
            total += segments[i].TotalMs;
            cumMs[i] = total;
        }
        return new DancerRuntime
        {
            Renderer = renderer,
            Sprites = sprites,
            Segments = segments,
            CumMs = cumMs,
            TotalCycleMs = total,
            LastFrameId = segments[0].FrameIds[0],
        };
    }

    /// <summary>
    /// R7 actors-only 构建：只建守卫/舞女自有根（全部 inactive，等 Root 接线后 Activate），不建建筑/舞台/静态层。
    /// </summary>
    private void BuildActors(PalaceActorsPlan plan)
    {
        foreach (PalaceActorGuardPlan guard in plan.Guards) PlaceActorGuard(guard);
        if (plan.Dancer != null) PlaceActorDancer(plan.Dancer);
        Log("actors created: actorAtlases=" + _actors.Count + " actorFrames=" + ActorFrameCount
            + " textures=" + _textures.Count + " sprites=" + _sprites.Count
            + " guards=" + _guards.Count + " dancer=" + (plan.Dancer != null ? "built" : "none")
            + " placedSprites=" + PlacedSprites);
    }

    /// <summary>
    /// R7 守卫：复用 PlaceGuard 的真实 10 相位巡逻循环与实例时钟，生成后只对本项新 runtime（构建期最后一项）写
    /// spriteSortPoint=Pivot、sortingOrder=0、owned localScale=(1, YScale, 1)；order 偏移恒 0（Reference 已校验为 0）。
    /// 不搜索 Unity 对象、不改旧 Create 路径。
    /// </summary>
    private void PlaceActorGuard(PalaceActorGuardPlan plan)
    {
        int before = _guards.Count;
        PlaceGuard(new PalaceWorldGuardRoute
        {
            ActorId = plan.ActorId,
            Parent = plan.Parent,
            Reference = plan.Reference,
            Left = plan.Left,
            Right = plan.Right,
            WalkLoops = plan.WalkLoops,
            OrderOffset = 0,
        });
        if (_guards.Count != before + 1)
            throw new InvalidOperationException("guard placement did not append exactly one runtime ("
                + before + " -> " + _guards.Count + ")");
        GuardRuntime guard = _guards[before];
        string expectedLabel = plan.ActorId + "#" + before;
        if (guard.Label != expectedLabel)
            throw new InvalidOperationException("guard runtime label mismatch: " + guard.Label + " != " + expectedLabel);
        SpriteRenderer renderer = guard.Renderer;
        if (renderer == null) throw new InvalidOperationException("guard renderer unavailable for " + expectedLabel);
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.sortingOrder = 0;
        renderer.transform.localScale = new Vector3(1f, plan.YScale, 1f);
        ActorEntry actor = _actors[plan.ActorId];
        Sprite initialFrame = EnsureActorSprites(actor)[actor.Clips["walk"].FrameIds[0]]; // PlaceGuard 起始帧（走首帧）
        VerifyOwnedActorRenderer(renderer, plan.Parent, plan.Left, plan.Reference, plan.YScale, initialFrame, expectedLabel);
        Log("actor guard " + expectedLabel + " placed (z=" + plan.Left.z + " yScale=" + plan.YScale + " order=0 pivotSort)");
    }

    /// <summary>
    /// R7 舞女：单 renderer 直接落在提供的脚点世界坐标（无 deck/front、无 +22/32 或 +21/32 偏移），节目仍为
    /// idle1940 → dance4640×2 → bow2220；sortingOrder=0、spriteSortPoint=Pivot、localScale=(1, YScale, 1)。
    /// </summary>
    private void PlaceActorDancer(PalaceActorDancerPlan plan)
    {
        ActorEntry dancer = _actors["court-dancer"];
        Sprite[] sprites = EnsureActorSprites(dancer);
        int initialFrame = dancer.Clips["idle"].FrameIds[0];
        SpriteRenderer renderer = AddRoot(plan.Parent, dancer.Atlas, sprites[initialFrame], plan.WorldPosition,
            plan.Reference, 0);
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.sortingOrder = 0;
        renderer.transform.localScale = new Vector3(1f, plan.YScale, 1f);
        VerifyOwnedActorRenderer(renderer, plan.Parent, plan.WorldPosition, plan.Reference, plan.YScale,
            sprites[initialFrame], "actor dancer");
        _dancer = NewDancerRuntime(dancer, renderer, sprites);
        Log("actor dancer placed (z=" + plan.WorldPosition.z + " yScale=" + plan.YScale + " order=0 pivotSort)");
    }

    /// <summary>
    /// R7 actors-only：own renderer 产生处读回（守卫/舞女共用；只读，不写借用对象、不搜索 Unity 对象），
    /// 不符 → 抛异常交 CreateActors 统一 Clear。位置用计划同 float 目标值比较（借用父 basis 已在
    /// ValidateActorsPlan 核为 unit lossyScale/无旋转，Unity 赋值与读回走同一 float 值域）。
    /// </summary>
    private static void VerifyOwnedActorRenderer(SpriteRenderer renderer, Transform parent, Vector3 worldPosition,
        SpriteRenderer reference, float yScale, Sprite initialFrame, string context)
    {
        if (renderer == null) throw new InvalidOperationException(context + ": owned renderer unavailable");
        Transform transform = renderer.transform;
        if (transform.parent != parent)
            throw new InvalidOperationException(context + ": parent identity mismatch");
        Vector3 scale = transform.localScale;
        if (scale != new Vector3(1f, yScale, 1f))
            throw new InvalidOperationException(context + ": localScale " + scale + " != (1," + yScale + ",1)");
        Vector3 position = transform.position;
        if (position != worldPosition)
            throw new InvalidOperationException(context + ": worldPosition " + position + " != " + worldPosition);
        if (renderer.sortingOrder != 0)
            throw new InvalidOperationException(context + ": sortingOrder " + renderer.sortingOrder + " != 0");
        if (renderer.spriteSortPoint != SpriteSortPoint.Pivot)
            throw new InvalidOperationException(context + ": spriteSortPoint != Pivot");
        int layerId = renderer.sortingLayerID;
        if (layerId != reference.sortingLayerID)
            throw new InvalidOperationException(context + ": sortingLayerID " + layerId
                + " != reference " + reference.sortingLayerID);
        if (renderer.sprite != initialFrame)
            throw new InvalidOperationException(context + ": sprite != own initial frame");
    }

    /// <summary>FrameID → Sprite 表（构建一次，多守卫/舞台共享）：同 rect 复用同一 sprite，绝不用 atlas 空白格。</summary>
    private Sprite[] EnsureActorSprites(ActorEntry actor)
    {
        if (actor.Sprites != null) return actor.Sprites;
        var table = new Sprite[actor.FrameCount];
        var byRect = new Dictionary<long, Sprite>(actor.FrameCount);
        for (int frameId = 0; frameId < actor.FrameCount; frameId++)
        {
            PixelRect px = actor.FrameRects[frameId];
            long key = ((long)px.X << 32) | (uint)px.YTop;
            if (!byRect.TryGetValue(key, out Sprite sprite))
            {
                // 原图 top-left rect → Unity rect（y 向上）：bottom = atlasH − yTop − h。
                var rect = new Rect(px.X, actor.AtlasH - px.YTop - px.H, px.W, px.H);
                sprite = OwnSprite(actor.Texture, rect, new Vector2(actor.PivotX, actor.PivotY), Ppu,
                    actor.Atlas + " #" + frameId);
                byRect[key] = sprite;
            }
            table[frameId] = sprite;
        }
        actor.Sprites = table;
        return table;
    }

    /// <summary>
    /// 自有根 GO + renderer：创建即登记（先登记后做可能失败的借用读取/parent/位置），构建期 inactive（Activate 才激活）。
    /// 落位 = SetParent(借用父, false) 后写绝对世界 position（假定 Root 已核父 unit scale；Parentnative 引用非 actor 身份）。
    /// renderer 的 layer/sortingLayer/sortingOrder(=参照+偏移)/material 复制自参照，Color.white。
    /// </summary>
    private SpriteRenderer AddRoot(Transform parent, string label, Sprite sprite, Vector3 worldPosition,
        SpriteRenderer reference, int orderOffset)
    {
        var child = new GameObject("palace-world-" + LabelOf(label));
        _children.Add(child);   // 立即登记：其后任何失败由 Clear 统一回滚
        child.SetActive(false); // 整批完成前保持 inactive
        int goLayer, sortingLayerId, baseOrder;
        Material material;
        try
        {
            goLayer = reference.gameObject.layer;
            sortingLayerId = reference.sortingLayerID;
            baseOrder = reference.sortingOrder;
            material = reference.sharedMaterial;
        }
        catch (Exception e)
        {
            throw new InvalidDataException("reference renderer read fault (" + label + "): " + e.GetType().Name);
        }
        child.layer = goLayer;
        child.transform.SetParent(parent, false);
        child.transform.position = worldPosition;
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        _renderers.Add(renderer);
        renderer.sprite = sprite;
        renderer.flipX = false;
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = baseOrder + orderOffset;
        renderer.color = Color.white;
        if (material != null) renderer.sharedMaterial = material; // 借来材质只读引用
        PlacedSprites++;
        return renderer;
    }

    // ------------------------------------------------------------------ Tick（无每帧分配/场景读取）

    private void TickGuard(GuardRuntime guard, float deltaTime)
    {
        if (guard.Stopped) return;
        double incrementMs = deltaTime * 1000.0; // deltaTime 已验有限正数；取模先行 → 大有限 delta 也不失界
        guard.CycleMs = (guard.CycleMs + incrementMs % guard.TotalCycleMs) % guard.TotalCycleMs;
        int phaseIndex = Locate(guard.CumMs, guard.CycleMs, out double phaseT);
        GuardPhase phase = guard.Phases[phaseIndex];
        int frameId = GuardFrameAt(guard.Clips[phase.ClipIndex], phaseT, phase.Moving);
        try
        {
            SpriteRenderer renderer = guard.Renderer;
            if (renderer == null) { StopGuard(guard, "renderer unavailable"); return; }
            if (phaseIndex != guard.LastPhaseIndex)
            {
                guard.LastPhaseIndex = phaseIndex;
                renderer.flipX = phase.FlipX;
                if (!phase.Moving) renderer.transform.position = phase.From;
                Log(guard.Label + " -> " + phase.Name + " flip=" + phase.FlipX + " frame=" + frameId);
            }
            if (phase.Moving)
            {
                renderer.transform.position = Vector3.Lerp(phase.From, phase.To, (float)(phaseT / phase.DurationMs));
            }
            if (frameId != guard.LastFrameId)
            {
                Sprite sprite = frameId >= 0 && frameId < guard.Sprites.Length ? guard.Sprites[frameId] : null;
                if (sprite == null) { StopGuard(guard, "frame sprite unavailable #" + frameId); return; }
                renderer.sprite = sprite;
                guard.LastFrameId = frameId;
            }
        }
        catch (Exception e) { StopGuard(guard, "renderer fault " + e.GetType().Name); }
    }

    private void TickDancer(DancerRuntime dancer, float deltaTime)
    {
        if (dancer.Stopped) return;
        double incrementMs = deltaTime * 1000.0;
        dancer.CycleMs = (dancer.CycleMs + incrementMs % dancer.TotalCycleMs) % dancer.TotalCycleMs;
        int segmentIndex = Locate(dancer.CumMs, dancer.CycleMs, out double segmentT);
        if (segmentIndex != dancer.LastSegmentIndex)
        {
            dancer.LastSegmentIndex = segmentIndex;
            Log("stage dancer -> " + DancerSegmentNames[segmentIndex]);
        }
        int frameId = FrameAt(dancer.Segments[segmentIndex], segmentT);
        if (frameId == dancer.LastFrameId) return;
        try
        {
            SpriteRenderer renderer = dancer.Renderer;
            Sprite sprite = frameId >= 0 && frameId < dancer.Sprites.Length ? dancer.Sprites[frameId] : null;
            if (renderer == null || sprite == null) { StopDancer(dancer, "renderer/sprite unavailable #" + frameId); return; }
            renderer.sprite = sprite;
            dancer.LastFrameId = frameId;
        }
        catch (Exception e) { StopDancer(dancer, "renderer fault " + e.GetType().Name); }
    }

    /// <summary>相位定位：t 落入的相位下标 + 相位内时间（边界 t==cum 属下一相位；线性扫描，无分配）。</summary>
    private static int Locate(double[] cumMs, double t, out double phaseT)
    {
        for (int i = 0; i < cumMs.Length; i++)
        {
            if (t < cumMs[i])
            {
                phaseT = t - (i == 0 ? 0.0 : cumMs[i - 1]);
                return i;
            }
        }
        phaseT = 0.0;
        return cumMs.Length - 1;
    }

    /// <summary>clip 帧定位：相位内时间 → FrameID（时间相位边界选帧，不逐帧补跑）。</summary>
    private static int GuardFrameAt(ActorClip clip, double phaseT, bool moving)
        => FrameAt(clip, moving ? phaseT % clip.TotalMs : phaseT);

    private static int FrameAt(ActorClip clip, double phaseT)
    {
        for (int i = 0; i < clip.CumMs.Length; i++)
        {
            if (phaseT < clip.CumMs[i]) return clip.FrameIds[i];
        }
        return clip.FrameIds[clip.FrameIds.Length - 1];
    }

    private void StopGuard(GuardRuntime guard, string reason)
    {
        if (guard.Stopped) return;
        guard.Stopped = true;
        Log("guard " + guard.Label + " stopped (no rebuild/retry): " + reason);
    }

    private void StopDancer(DancerRuntime dancer, string reason)
    {
        if (dancer.Stopped) return;
        dancer.Stopped = true;
        Log("stage dancer stopped (no rebuild/retry): " + reason);
    }

    // ------------------------------------------------------------------ 资源与自有资产工具

    private Texture2D LoadTexture(string relative, int width, int height)
    {
        byte[] bytes = ReadResource(relative);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        _textures.Add(texture); // 先登记：其后任何失败由 Clear 统一回滚
        if (!ImageConversion.LoadImage(texture, bytes, false))
            throw new InvalidDataException("decode failed: " + relative);
        if (texture.width != width || texture.height != height)
            throw new InvalidDataException(relative + " size " + texture.width + "x" + texture.height
                + " != expected " + width + "x" + height);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 0;
        return texture;
    }

    private Sprite OwnSprite(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, string label)
    {
        if (rect.width <= 0f || rect.height <= 0f || rect.xMin < 0f || rect.yMin < 0f
            || rect.xMax > texture.width || rect.yMax > texture.height)
            throw new InvalidDataException("sprite rect outside texture: " + label + " " + rect);
        Sprite sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0u, SpriteMeshType.FullRect);
        if (sprite == null) throw new InvalidOperationException("Sprite.Create failed: " + label);
        _sprites.Add(sprite); // 先登记再命名：命名异常也不丢责任
        sprite.name = "kem-worldfull:" + label;
        return sprite;
    }

    private static byte[] ReadResource(string relative)
    {
        string logicalName = ResourcePrefix + relative;
        System.Reflection.Assembly assembly = typeof(PalaceWorldExpansion).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName);
        if (stream == null) throw new FileNotFoundException("embedded resource missing: " + logicalName);
        long length = stream.Length;
        if (length <= 0 || length > MaxResourceBytes)
            throw new InvalidDataException("resource size rejected: " + logicalName + " " + length + " bytes");
        byte[] bytes = new byte[(int)length];
        int read = 0;
        while (read < bytes.Length)
        {
            int step = stream.Read(bytes, read, bytes.Length - read);
            if (step <= 0) break;
            read += step;
        }
        if (read != bytes.Length)
            throw new InvalidDataException("resource truncated: " + logicalName + " " + read + "/" + bytes.Length);
        return bytes;
    }

    private static JsonDocument ParseManifestJson(string relative)
    {
        byte[] bytes = ReadResource(relative);
        if (relative == "actors/manifest.json"
            && !PalaceResourceBytes.MatchesSha256(bytes, PalaceResourceHashGaps.Resolve(ResourcePrefix + relative), out string hashReason))
            throw new InvalidDataException("actor manifest hash: " + hashReason);
        try { return JsonDocument.Parse(bytes, ManifestParseOptions); }
        catch (JsonException e)
        {
            throw new InvalidDataException("manifest json invalid: " + relative + ": " + e.Message);
        }
    }

    /// <summary>解除自有 renderer 的 sprite 并回读确认；renderer 已销毁（无借用者）视为已解除，异常/回读不符视为未知。</summary>
    private static bool TryDetach(SpriteRenderer renderer)
    {
        try
        {
            if (renderer == null) return true;
            renderer.sprite = null;
            return renderer.sprite == null;
        }
        catch (Exception) { return false; }
    }

    /// <summary>只销毁自有对象；异常视为 native 状态未知（不重试、不二次销毁）。</summary>
    private static bool DestroyChecked(UnityEngine.Object owned)
    {
        try
        {
            if (owned != null) UnityEngine.Object.Destroy(owned);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>native 清理未知：本实例连同清单静态保留（单个槽），日志可观察，阻止后续 Create 新分配。</summary>
    private void Quarantine(string fault)
    {
        _quarantined = true;
        if (_quarantine == null) _quarantine = this;
        Log("KEM_PalaceWorldFull retained quarantine (no retry, no silent release, new Create blocked): " + fault);
    }

    private static string LabelOf(string relative)
    {
        string trimmed = relative.EndsWith(".png", StringComparison.Ordinal)
            ? relative.Substring(0, relative.Length - 4)
            : relative;
        return trimmed.Replace('/', '-');
    }

    private static void Log(string message)
    {
        try { PalaceVisibleEntry.Log("[PalaceWorld] " + message); }
        catch (Exception) { }
    }

    // ------------------------------------------------------------------ JSON 严格读取辅助

    private static string ValidateRelative(string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.StartsWith("/", StringComparison.Ordinal)
            || relative.Contains("..") || relative.Contains("\\") || relative.Contains(":"))
            throw new InvalidDataException("illegal relative resource path: " + relative);
        return relative;
    }

    private static JsonElement RequireArray(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(context + ": missing array " + name);
        return value;
    }

    private static JsonElement RequireObject(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(context + ": missing object " + name);
        return value;
    }

    private static string RequireString(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(context + ": missing string " + name);
        string text = value.GetString();
        if (string.IsNullOrEmpty(text)) throw new InvalidDataException(context + ": empty string " + name);
        return text;
    }

    private static int RequireInt(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number))
            throw new InvalidDataException(context + ": missing int " + name);
        return number;
    }

    private static float RequireFiniteFloat(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetSingle(out float number) || !float.IsFinite(number))
            throw new InvalidDataException(context + ": missing finite number " + name);
        return number;
    }

    private static (int, int) ReadIntPair(JsonElement array, string context)
    {
        if (array.GetArrayLength() != 2) throw new InvalidDataException(context + ": expected [2]");
        int a = IntAt(array[0], context), b = IntAt(array[1], context);
        return (a, b);
    }

    private static (int, int, int, int) ReadIntQuad(JsonElement array, string context)
    {
        if (array.GetArrayLength() != 4) throw new InvalidDataException(context + ": expected [4]");
        return (IntAt(array[0], context), IntAt(array[1], context), IntAt(array[2], context), IntAt(array[3], context));
    }

    private static (float, float) ReadFloatPair(JsonElement array, string context)
    {
        if (array.GetArrayLength() != 2) throw new InvalidDataException(context + ": expected [2]");
        if (!array[0].TryGetSingle(out float a) || !float.IsFinite(a)
            || !array[1].TryGetSingle(out float b) || !float.IsFinite(b))
            throw new InvalidDataException(context + ": non-finite entry");
        return (a, b);
    }

    private static int[] ReadIntArray(JsonElement array, string context)
    {
        int[] values = new int[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (!item.TryGetInt32(out values[i]))
                throw new InvalidDataException(context + ": non-int entry at " + i);
            i++;
        }
        return values;
    }

    private static int IntAt(JsonElement item, string context)
    {
        if (!item.TryGetInt32(out int value)) throw new InvalidDataException(context + ": non-int entry");
        return value;
    }
}
