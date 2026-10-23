using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// R6 建筑与荷景：静态人物占位入口已撤回。角色须具备已验证动作/最小行为及原生身高校准才准入。
/// 非原生 NPC/可交互建筑：无 collider/pool/save/付款/AI/动画；无新 Harmony/MonoBehaviour/Install hook、无 GUI、无循环扫描；
/// 不替代现有 return gate，人物避开中宫门中心 x768 区域。
/// 纯 managed 类（不注册 IL2CPP、不建 MonoBehaviour）：生命周期由 Root 显式驱动——CourtBinding 就绪后调用 Create，
/// 庭院 PreExit/Cleanup 调用 Clear。载入嵌资源、放置 child、回收自有对象都在本类内完成，Root 负责激活与写成功日志。
/// 素材 = Assembly 嵌入资源 "KingdomEnhancedMod.KEM_PalaceR6/" + relative（13 张 PNG 由 Root 原字节嵌入；本类不读磁盘、不改字节）。
/// 数据表 = 已审 manifest（player-content-r6-art-reviewed）：atlas cols×rows×320、idle.indices[0]（guardian=0 / resident=6，
/// 不是 walk）、建筑 1712×768 / 1120×600、荷景 24×24 / 24×24 / 24×16 / 18×14；只引用 clip 实际使用的 cell（不含 atlas 空白格），
/// 不读动态 frames、不伪造 Native 状态。坐标：root 局部原点 = 庭院左端 + 脚线 y214（y 向下）；本批只写自己的 child 局部位置，
/// 不改 actor/native root scale。
/// 所有权与清理：新 GO/Sprite 创建即登记进自有清单（container 先登记再 layer/SetParent，构建期 inactive，13 项完整才 activate）；
/// Clear 先停用自有 children/container，再逐个解除自有 renderer 的 sprite 并回读确认 null，借用者解除明确后才销毁自有资产；
/// CourtBinding 的 Parent/Material/sortingLayer 借用只读，绝不销毁。native 清理任一步未知（异常/回读不符）→ 不继续清单、
/// 不清空自有清单，本实例连同 container/清单作单个静态 retained quarantine + 可观察日志，阻止后续 Create 新分配；不重试。
/// 可选舞台（PALACE_STAGE_PRIVATE=1 且 PalaceLand12Runtime.Enabled 才建）：deck + 舞女 dance 循环 + front 三个自有 renderer，
/// 纯装饰；素材/rect/时长/锚点全部沿 PalaceStageData 常量（PPU32，不改旧 R6 口径），sort 为本组候选（deck -3/dancer -2/front -1，
/// 未 approve native 遮挡；横位 1200 未 runtime 验，Root 后续收口）。Tick 由 Root 在 KeepAlive 后驱动（时钟条件归 Root），
/// 只在帧变化时写自有 renderer.sprite；renderer 外部销毁/异常 → 日志一次并停本动画，不重建/重试。未开 flag：零新增分配、旧 13 项不变。
/// </summary>
internal sealed class PalaceCourtContent
{
    private const string ResourcePrefix = "KingdomEnhancedMod.KEM_PalaceR6/";
    private const float LogicalPpu = 45.7142857f;   // 庭院 1× 口径（320/7）：1536px = 33.6 world
    private const float CellPpu = LogicalPpu * 8f;  // 人物/建筑 8× 口径：320px cell = 40 logical px
    private const float FootLinePx = 214f;          // root 局部 y=0 = 脚线（y 向下 214）
    private const int CellPx = 320;
    private const float PivotX = 164f / CellPx;     // 人物脚点 pivot（manifest fixedPivotPixelsBottomLeft [164,32]）
    private const float PivotY = 32f / CellPx;
    private const int MaxResourceBytes = 4 * 1024 * 1024;

    /// <summary>人物：atlas 的 idle 首帧；脚点 X（logical px）。</summary>
    private readonly struct CharacterSpec
    {
        internal readonly string File;
        internal readonly int Columns, Rows, FrameIndex;
        internal readonly float FootX;

        internal CharacterSpec(string file, int columns, int rows, int frameIndex, float footX)
        {
            File = file; Columns = columns; Rows = rows; FrameIndex = frameIndex; FootX = footX;
        }
    }

    /// <summary>静态建筑：整图 bottom-center，底边贴脚线；center X（logical px）。</summary>
    private readonly struct BuildingSpec
    {
        internal readonly string File;
        internal readonly int Width, Height;
        internal readonly float CenterX;

        internal BuildingSpec(string file, int width, int height, float centerX)
        {
            File = file; Width = width; Height = height; CenterX = centerX;
        }
    }

    /// <summary>荷景单体：花 = 茎底 pivot(0.5,0)，叶 = 中心 pivot(0.5,0.5)（显式按 ID，不依旧文件后缀）。</summary>
    private readonly struct LotusSpec
    {
        internal readonly string File;
        internal readonly int Width, Height;
        internal readonly float CenterX, AnchorYDown;
        internal readonly bool StemBaseAnchor;

        internal LotusSpec(string file, int width, int height, float centerX, float anchorYDown, bool stemBaseAnchor)
        {
            File = file; Width = width; Height = height; CenterX = centerX; AnchorYDown = anchorYDown;
            StemBaseAnchor = stemBaseAnchor;
        }
    }

    // guardian 四人取前四项（270/430/600/950，跨过 x768 中宫门），resident 后三项（1120/1250/1400）。
    private static readonly CharacterSpec[] Characters =
    {
        new CharacterSpec("guardian-orb/atlas.png", 4, 8, 0, 270f),
        new CharacterSpec("guardian-lute/atlas.png", 4, 7, 0, 430f),
        new CharacterSpec("guardian-sword/atlas.png", 4, 6, 0, 600f),
        new CharacterSpec("guardian-pole/atlas.png", 4, 7, 0, 950f),
        new CharacterSpec("resident-cream/atlas.png", 6, 6, 6, 1120f),
        new CharacterSpec("resident-pink/atlas.png", 6, 6, 6, 1250f),
        new CharacterSpec("resident-brown/atlas.png", 6, 6, 6, 1400f),
    };

    private static readonly BuildingSpec[] Buildings =
    {
        new BuildingSpec("reception-house/idle.png", 1712, 768, 200f),
        new BuildingSpec("hermit-hut/idle.png", 1120, 600, 1290f),
    };

    private static readonly LotusSpec[] Lotus =
    {
        new LotusSpec("lotus/lotus-flower-open.png", 24, 24, 580f, 224f, true),
        new LotusSpec("lotus/lotus-flower-bud.png", 24, 24, 640f, 226f, true),
        new LotusSpec("lotus/lotus-leaf-flat.png", 24, 16, 905f, 229f, false),
        new LotusSpec("lotus/lotus-leaf-curl.png", 18, 14, 970f, 226f, false),
    };

    private readonly List<GameObject> _children = new List<GameObject>(14);
    private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>(13);
    private readonly List<Sprite> _sprites = new List<Sprite>(13);
    private readonly List<Texture2D> _textures = new List<Texture2D>(13);
    private GameObject _container;
    private bool _cleared;
    private bool _quarantined;

    /// <summary>native 清理未知时的单个静态保留槽：实例连同 container/清单留存，阻止后续 Create 新分配。</summary>
    private static PalaceCourtContent _quarantine;

    /// <summary>本节点已放置的 sprite 数（供 Root 写成功日志 count/size）。</summary>
    internal int PlacedSprites { get; private set; }

    // 可选舞台：仅本实例字段（无静态 owner、无第二套 cleanup 清单；对象统一登记进既有 _children/_renderers/_sprites/_textures）。
    private const float StageCenterLogicalX = 1472f; // world center15.4；left13.15>hut right12.95，实际地面/遮挡仍待验
    private PalaceStageDanceClock _stageClock;
    private Sprite[] _stageFrameSprites;   // FrameID → dance sprite（只填 4..18；idle/bow 不创建）
    private SpriteRenderer _stageRenderer; // 舞女 renderer（已登记在 _renderers）
    private int _stageFrameId = -1;        // 已应用的 dance frame
    private bool _stageBuilt;
    private bool _stageStopped;

    /// <summary>舞台候选开关（Build 时求值一次）：未开时不做任何舞台加载/创建，旧 13 项路径不变。</summary>
    private static bool StageRequested
        => PalaceRuntimeProfile.Capability("PALACE_STAGE_PRIVATE") && PalaceLand12Runtime.Enabled && !PalaceWorldLayout.Enabled;

    /// <summary>
    /// 一批全成：任一资源缺失 / 尺寸或 rect 不符 / owner 不可用 → 清掉本批自有部分并返回 false，不重试；
    /// 失败原因写入 reason（Root 决定如何记录）。绝不触碰借用对象。静态 quarantine 存在时拒绝任何新分配。
    /// </summary>
    internal static bool Create(CourtBinding binding, out PalaceCourtContent content, out string reason)
    {
        content = null;
        reason = null;
        if (_quarantine != null) { reason = "retained quarantine active; refusing new allocation"; return false; }
        if (!TryOwner(binding, out reason)) return false;
        var self = new PalaceCourtContent();
        try
        {
            self.Build(binding);
            content = self;
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

    /// <summary>owner = 借用的 CourtBinding.Parent：必须存活（Unity destroyed 对象 == null；native 读取异常同样视为不可用）。</summary>
    private static bool TryOwner(CourtBinding binding, out string reason)
    {
        reason = null;
        if (binding == null) { reason = "binding is null"; return false; }
        try
        {
            Transform parent = binding.Parent;
            if (parent == null) { reason = "owner parent null or destroyed"; return false; }
            if (parent.gameObject == null) { reason = "owner gameObject null or destroyed"; return false; }
        }
        catch (Exception e)
        {
            reason = "owner check threw " + e.GetType().Name;
            return false;
        }
        return true;
    }

    /// <summary>
    /// 确认式清理，只动自有对象：
    /// 1. 先 Deactivate 全部自有 children/container（逐个登记，SetParent 曾失败的游离 child 也覆盖）；
    /// 2. 逐个解除自有 renderer 的 sprite 并回读确认 null——借用者解除明确前不销毁任何资产；
    /// 3. 任一异常或回读不符 → 立即停手：不继续清单、不清空自有清单，静态单个 quarantine + 日志；
    /// 4. 全部确证后才逐个 Destroy children/container/sprites/textures 并清空自有清单。借用 Parent/material 全程不动。幂等。
    /// </summary>
    internal void Clear()
    {
        if (_cleared || _quarantined) return;
        _stageStopped = true;        // 先撤销动画推进责任：Clear 之后 Tick 不再写 sprite
        _stageClock = null;
        _stageFrameSprites = null;
        _stageRenderer = null;
        foreach (GameObject child in _children)
        {
            try { if (child != null) child.SetActive(false); }
            catch (Exception) { Quarantine("child deactivate fault; own lists retained"); return; }
        }
        try { if (_container != null) _container.SetActive(false); }
        catch (Exception) { Quarantine("container deactivate fault; own lists retained"); return; }
        foreach (SpriteRenderer renderer in _renderers)
        {
            if (!TryDetach(renderer)) { Quarantine("renderer sprite detach unconfirmed; assets retained"); return; }
        }
        foreach (GameObject child in _children)
        {
            if (!DestroyChecked(child)) { Quarantine("child destroy fault; own lists retained"); return; }
        }
        if (!DestroyChecked(_container)) { Quarantine("container destroy fault; own lists retained"); return; }
        foreach (Sprite sprite in _sprites)
        {
            if (!DestroyChecked(sprite)) { Quarantine("sprite destroy fault; own lists retained"); return; }
        }
        foreach (Texture2D texture in _textures)
        {
            if (!DestroyChecked(texture)) { Quarantine("texture destroy fault; own lists retained"); return; }
        }
        _container = null;
        _children.Clear();
        _renderers.Clear();
        _sprites.Clear();
        _textures.Clear();
        PlacedSprites = 0;
        _cleared = true;
    }

    private bool _figuresObserved;
    internal void ObserveFiguresOnce()
    {
        if (_figuresObserved || _cleared || _quarantined) return;
        _figuresObserved = true;
        PalaceFigureVisuals.ObserveOwnedFigures(_renderers);
    }

    private void Build(CourtBinding binding)
    {
        var container = new GameObject("palace-r6-content");
        _container = container;      // 立即强引用登记：layer/SetParent 失败也能被 Clear 定位
        container.SetActive(false);  // 构建期 inactive，13 项完整后才 activate
        container.layer = binding.GoLayer;
        container.transform.SetParent(binding.Parent, false);
        Transform parent = container.transform;
        // Human 2026-10-07: no static-character placeholders, including intermediate candidates.
        // The old idle-frame-only character path is deliberately not invoked. Keep assets for future
        // proper animated actors; do not manufacture an idle billboard to occupy a planned position.
        foreach (BuildingSpec spec in Buildings) PlaceBuilding(binding, parent, spec);
        foreach (LotusSpec spec in Lotus) PlaceLotus(binding, parent, spec);
        if (StageRequested) BuildStage(binding, parent); // 13 项 R6 之后、activate 之前；未开 flag 零新增分配
        container.SetActive(true);

    }

    private void PlaceCharacter(CourtBinding binding, Transform parent, CharacterSpec spec)
    {
        if (spec.FrameIndex < 0 || spec.FrameIndex >= spec.Columns * spec.Rows)
            throw new InvalidDataException("idle frame index outside atlas: " + spec.File + " #" + spec.FrameIndex);
        Texture2D atlas = LoadTexture(spec.File, spec.Columns * CellPx, spec.Rows * CellPx);
        int column = spec.FrameIndex % spec.Columns;
        int row = spec.FrameIndex / spec.Columns;
        // manifest rectPixelsTopLeft（y 向下）→ Unity rect（y 向上）：bottom = height - yTop - cellH。
        var rect = new Rect(column * CellPx, atlas.height - (row + 1) * CellPx, CellPx, CellPx);
        Sprite sprite = OwnSprite(atlas, rect, new Vector2(PivotX, PivotY), CellPpu, spec.File);
        AddChild(binding, parent, spec.File, sprite,
            new Vector3(spec.FootX / LogicalPpu, 0f, 0f), CourtStratum.Figure);
    }

    private void PlaceBuilding(CourtBinding binding, Transform parent, BuildingSpec spec)
    {
        Texture2D texture = LoadTexture(spec.File, spec.Width, spec.Height);
        Sprite sprite = OwnSprite(texture, new Rect(0f, 0f, spec.Width, spec.Height),
            new Vector2(0.5f, 0f), CellPpu, spec.File);
        AddChild(binding, parent, spec.File, sprite,
            new Vector3(spec.CenterX / LogicalPpu, 0f, 0f), CourtStratum.Architecture);
    }

    private void PlaceLotus(CourtBinding binding, Transform parent, LotusSpec spec)
    {
        Texture2D texture = LoadTexture(spec.File, spec.Width, spec.Height);
        Vector2 pivot = spec.StemBaseAnchor ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
        Sprite sprite = OwnSprite(texture, new Rect(0f, 0f, spec.Width, spec.Height), pivot, LogicalPpu, spec.File);
        float y = (FootLinePx - spec.AnchorYDown) / LogicalPpu; // 脚线 214 下方 10..15px 的水缘锚
        AddChild(binding, parent, spec.File, sprite,
            new Vector3(spec.CenterX / LogicalPpu, y, 0f), CourtStratum.NearWaterDecoration);
    }

    /// <summary>
    /// 可选装饰舞台（PALACE_STAGE_PRIVATE=1 且 land12 runtime 启用）：deck + 舞女 dance 循环 + front，三个自有 renderer。
    /// 纯装饰：无 NativeNPC/AI/互动/physics/pool/RPC/save/Harmony；素材与 rect/时长/锚点全部沿 PalaceStageData，不自行推格位。
    /// sort 为本组候选（deck FarScenery=-3 / dancer Architecture=-2 / front WalkableSurface=-1），未 approve native 遮挡；
    /// 横位 1472/LogicalPpu=32.2（world15.4）、脚面=root 局部 y0（原 Court groundY）未 runtime 验。
    /// </summary>
    private void BuildStage(CourtBinding binding, Transform parent)
    {
        Texture2D deckTexture = LoadTexture(PalaceStageData.DeckRelative,
            PalaceStageData.DeckWidth, PalaceStageData.DeckHeight);
        Texture2D frontTexture = LoadTexture(PalaceStageData.FrontRelative,
            PalaceStageData.FrontWidth, PalaceStageData.FrontHeight);
        Texture2D dancerTexture = LoadTexture(PalaceStageData.DancerAtlasRelative,
            PalaceStageData.DancerAtlasWidth, PalaceStageData.DancerAtlasHeight);

        float centerX = StageCenterLogicalX / LogicalPpu; // 右侧候选空位：1472/45.7142857 = 32.2，未 runtime 验
        Sprite deckSprite = OwnSprite(deckTexture,
            new Rect(0f, 0f, PalaceStageData.DeckWidth, PalaceStageData.DeckHeight),
            new Vector2(PalaceStageData.DeckPivotX, PalaceStageData.DeckPivotY), PalaceStageData.Ppu, PalaceStageData.DeckRelative);
        AddChild(binding, parent, PalaceStageData.DeckRelative, deckSprite,
            new Vector3(centerX, 0f, 0f), CourtStratum.FarScenery);

        Sprite[] danceSprites = CreateDanceSprites(dancerTexture);
        _stageFrameSprites = danceSprites;
        _stageFrameId = PalaceStageData.DanceFrameIds[0]; // 首轮直接 dance：从 frame 4 开始
        _stageRenderer = AddChild(binding, parent, PalaceStageData.DancerAtlasRelative, danceSprites[_stageFrameId],
            new Vector3(centerX, PalaceStageData.StageSurfaceYOffset, 0f), CourtStratum.Architecture);

        Sprite frontSprite = OwnSprite(frontTexture,
            new Rect(0f, 0f, PalaceStageData.FrontWidth, PalaceStageData.FrontHeight),
            new Vector2(PalaceStageData.FrontPivotX, PalaceStageData.FrontPivotY), PalaceStageData.Ppu, PalaceStageData.FrontRelative);
        AddChild(binding, parent, PalaceStageData.FrontRelative, frontSprite,
            new Vector3(centerX, PalaceStageData.FrontPivotYOffset, 0f), CourtStratum.WalkableSurface);

        _stageClock = new PalaceStageDanceClock();
        _stageBuilt = true;
    }

    /// <summary>dance 4..18 逐项显式查 PalaceStageData.Frames；相同 rect 复用同一 sprite；idle/bow 不创建。</summary>
    private Sprite[] CreateDanceSprites(Texture2D atlas)
    {
        var table = new Sprite[PalaceStageData.Frames.Length];
        var byRect = new Dictionary<long, Sprite>(PalaceStageData.DanceFrameIds.Length);
        foreach (int frameId in PalaceStageData.DanceFrameIds)
        {
            if (frameId < 0 || frameId >= PalaceStageData.Frames.Length)
                throw new InvalidDataException("dance frame id outside table: " + frameId);
            PixelRect px = PalaceStageData.Frames[frameId];
            long key = ((long)px.X << 32) | (uint)px.YTop; // cell 恒 48×48（数据表），(X,YTop) 即唯一 rect
            if (!byRect.TryGetValue(key, out Sprite sprite))
            {
                // 原图 top-left rect → Unity rect（y 向上）：bottom = atlasH − yTop − h。
                var rect = new Rect(px.X, PalaceStageData.DancerAtlasHeight - px.YTop - px.H, px.W, px.H);
                sprite = OwnSprite(atlas, rect,
                    new Vector2(PalaceStageData.DancerPivotX, PalaceStageData.DancerPivotY),
                    PalaceStageData.Ppu, PalaceStageData.DancerAtlasRelative + " dance#" + frameId);
                byRect[key] = sprite;
            }
            table[frameId] = sprite;
        }
        return table;
    }

    /// <summary>
    /// Root 在场景 KeepAlive 通过后调用（时钟条件由 Root 负责；本方法不查 Managers/actor state、不扫场景）：
    /// 仅 playing 且有限 dt>0、舞台已建、未 stop/cleared/quarantined 时推进实例 Clock；仅 frame 改变时写自有 renderer.sprite；
    /// 无每帧新资产、不逐帧补跑。renderer 外部销毁（假 null）或写入异常 → 日志一次并停本动画，不重建/重试。
    /// </summary>
    internal void Tick(float deltaTime, bool playing)
    {
        if (!_stageBuilt || _stageStopped || _cleared || _quarantined) return;
        if (!playing || !(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;
        int frameId = _stageClock.Advance(deltaTime, true);
        if (frameId == _stageFrameId) return;
        Sprite sprite = frameId >= 0 && frameId < _stageFrameSprites.Length ? _stageFrameSprites[frameId] : null;
        SpriteRenderer renderer = _stageRenderer;
        try
        {
            if (renderer == null || sprite == null) { StopStageAnimation("dancer renderer/sprite unavailable"); return; }
            renderer.sprite = sprite;
            _stageFrameId = frameId;
        }
        catch (Exception e)
        {
            StopStageAnimation("dancer renderer fault: " + e.GetType().Name);
        }
    }

    /// <summary>本动画停止（日志一次）：不再写 sprite、不重建、不重试；对象仍归现有清理清单。</summary>
    private void StopStageAnimation(string reason)
    {
        if (_stageStopped) return;
        _stageStopped = true;
        PalaceCourtVisuals.Log("KEM_PalaceR6 stage animation stopped (no rebuild/retry): " + reason);
    }

    private SpriteRenderer AddChild(CourtBinding binding, Transform parent, string label, Sprite sprite,
        Vector3 localPosition, CourtStratum stratum)
    {
        string name = "palace-r6-" + LabelOf(label);
        var child = new GameObject(name);
        _children.Add(child);        // 立即登记：SetParent/AddComponent 失败也不丢责任
        child.layer = binding.GoLayer;
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        var renderer = child.AddComponent<SpriteRenderer>();
        _renderers.Add(renderer);    // 记录本批 renderer：Clear 负责逐个解除 sprite
        renderer.sprite = sprite;
        renderer.sortingLayerID = binding.LayerId;
        renderer.sortingOrder = binding.BaseOrder + (stratum == CourtStratum.Figure
            ? PalaceFigureVisuals.FigureOrderOffset : (int)stratum);
        renderer.color = Color.white;
        if (binding.Material != null) renderer.sharedMaterial = binding.Material; // 借来材质只读引用
        PlacedSprites++;
        return renderer; // 旧调用可忽略；舞台只取舞女 renderer（仅本实例引用，清理走 _renderers）
    }

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
            throw new InvalidDataException("frame rect outside atlas: " + label + " " + rect);
        Sprite sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0u, SpriteMeshType.FullRect);
        if (sprite == null) throw new InvalidOperationException("Sprite.Create failed: " + label);
        _sprites.Add(sprite); // 先登记再命名：命名异常也不丢责任
        sprite.name = "kem-r6:" + label;
        return sprite;
    }

    private static byte[] ReadResource(string relative)
    {
        string logicalName = ResourcePrefix + relative;
        Assembly assembly = typeof(PalaceCourtContent).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream(logicalName))
        {
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
    }

    private static string LabelOf(string relative)
    {
        string trimmed = relative.EndsWith(".png", StringComparison.Ordinal)
            ? relative.Substring(0, relative.Length - 4)
            : relative;
        return trimmed.Replace('/', '-');
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

    /// <summary>native 清理未知：本实例连同 container/children/清单静态保留（单个槽），日志可观察，阻止后续 Create 新分配。</summary>
    private void Quarantine(string fault)
    {
        _quarantined = true;
        if (_quarantine == null) _quarantine = this;
        PalaceCourtVisuals.Log("KEM_PalaceR6 retained quarantine (no retry, no silent release, new Create blocked): " + fault);
    }
}
