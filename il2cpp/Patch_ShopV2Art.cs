// ShopV2：三个商店（英雄 20 格 / 盾 26 格 / 火枪 10 格）的静态层资源加载 + 普通 C# 视觉 binding。
//
// 层图（per kind，均取同一张不可变图集）：
//   cell0  rear       —— 由 Root 在 Create 阶段设置到原商店 SpriteRenderer（本文件不改原生 renderer）。
//   cell1  front      —— 前景遮罩层（静态）。
//   cell2..9 merchant —— 商人 8 帧（FrameAt(gameTime)），z 最靠前。
//   cell10+ states    —— 英雄：左 10..14 / 右 15..19（state 与 cell 一一对应）；
//                        盾：niche*4+state，niche0 lowerLeft / 1 upperLeft / 2 lowerRight / 3 upperRight；
//                        火枪：无 state 层（库存由真实 tool/Gun 渲染）。
//
// 纯策略（enum / layout / FrameAt / HeroCell / NicheState）不带 Unity 类型，可用
// #define SHOP_V2_CORE_ONLY 单独编译（tests/Tests.csproj）；所有 Unity/runtime 类包在
// #if !SHOP_V2_CORE_ONLY 内。纯策略放在独立类 ShopV2Layout / ShopV2Frames / ShopV2HeroPolicy /
// ShopV2NichePolicy（契约允许，使用单独 ShopV2Layout 即此文档说明）。
//
// 约束落实（见 ../contract.json 与 ../review/minimal-integration-design-review.md）：
//   * 资源惰性加载一次、按 kind 独立缓存；失败 log 一次并置 Unavailable，不重试、不回退旧资源。
//   * 图集只创建有效格（英雄 20 / 盾 26 / 火枪 10），末尾空槽必须全透明；rear/front/merchant 非空。
//   * binding 不移动 root、不改原生 renderer、不做 per-frame sprite/scale 重写，只在本层 state 变化时
//     改自己的 SpriteRenderer.sprite；无 scene 扫描、无新 Harmony、无状态写回。
//   * 英雄 state 0..4 由既有 HeroShopBannerVisuals.VisualState 转换后传入；非法值绝不画成 available。
//   * 盾龛 ready==false 或 unknown==true → 全部 Unknown 暗盖（绝不空）；mold 未开 → 全部 Locked 盖。
//   * 纯投影不查询、不修改钱包/身份/库存；quota counts already include Paying/PaidTool claims.
//
// Root 接入要点（不在本文件）：
//   * EmbeddedAssets.props 增加三个 LogicalName：KingdomEnhancedMod.ShopV2Hero.png /
//     ShopV2Shield.png / ShopV2Musket.png（Assets/ShopV2*.png）。
//   * Create：先确认本 kind 资源就绪（TryGet cell0 成功后）再取 _sprite.bounds（halfWidth 依赖完整
//     canvas 4 world 宽），再 Bind(root, rear, kind)；Bind 失败按现有 bounded 创建错误处理，不建半包。
//   * Hero：HeroShop._renderer = rear，原排序/材质字段保留；State0 兼容 Greek_shell _renderers[1]。
//   * Shield：CreatePoint 的 referenceRenderer 用 Front（front material/order+1），不再伪造四元素数组。
//   * Musket：原 _renderer 继续作为 nativeGun 的 TryGetRackSorting 参照（本 binding 不碰）。
//   * Tick 只在 Active 路径调用；Keep/Menu 保持原样；Clear 只在既有事务取消/注销成功后调用
//     （Shield 的 HasUnsettledNativeTransaction 提前 return 时必须保留 binding）。

#if !SHOP_V2_CORE_ONLY
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

/// <summary>V2 商店种类；每种对应一张独立嵌入 PNG 与独立缓存。</summary>
internal enum ShopV2Kind : byte
{
    Hero = 0,
    Shield = 1,
    Musket = 2
}

/// <summary>盾龛可见状态，索引即图集 state（cell10 + niche*4 + state）。</summary>
internal enum ShopV2NicheState : byte
{
    Available = 0, // 可购买空龛（透明）
    Locked = 1,    // 模具未开/扩展位未开：宝石盖
    Occupied = 2,  // 已占用：铜封
    Unknown = 3    // 未知/不可广告：暗盖；绝不显示为可购买
}

/// <summary>图集几何与格位映射（纯策略，无 Unity 依赖）。</summary>
internal static class ShopV2Layout
{
    internal const int Columns = 4;
    internal const int PixelsPerUnit = 32;
    internal const int RearCell = 0;
    internal const int FrontCell = 1;
    internal const int MerchantFirstCell = 2;
    internal const int MerchantFrames = 8;
    internal const int FirstSeatCell = 10;

    internal const int HeroCellWidth = 128, HeroCellHeight = 80, HeroRootPixelX = 64, HeroRootPixelY = 78, HeroRows = 5, HeroCellCount = 20;
    internal const int ShieldCellWidth = 128, ShieldCellHeight = 72, ShieldRootPixelX = 64, ShieldRootPixelY = 70, ShieldRows = 7, ShieldCellCount = 26;
    internal const int MusketCellWidth = 176, MusketCellHeight = 80, MusketRootPixelX = 64, MusketRootPixelY = 78, MusketRows = 3, MusketCellCount = 10;

    internal static bool IsKnown(ShopV2Kind kind) => kind == ShopV2Kind.Hero || kind == ShopV2Kind.Shield || kind == ShopV2Kind.Musket;

    internal static int CellWidth(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return HeroCellWidth;
            case ShopV2Kind.Shield: return ShieldCellWidth;
            case ShopV2Kind.Musket: return MusketCellWidth;
            default: return 0;
        }
    }

    internal static int CellHeight(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return HeroCellHeight;
            case ShopV2Kind.Shield: return ShieldCellHeight;
            case ShopV2Kind.Musket: return MusketCellHeight;
            default: return 0;
        }
    }

    internal static int Rows(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return HeroRows;
            case ShopV2Kind.Shield: return ShieldRows;
            case ShopV2Kind.Musket: return MusketRows;
            default: return 0;
        }
    }

    internal static int CellCount(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return HeroCellCount;
            case ShopV2Kind.Shield: return ShieldCellCount;
            case ShopV2Kind.Musket: return MusketCellCount;
            default: return 0;
        }
    }

    internal static int RootPixelX(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return HeroRootPixelX;
            case ShopV2Kind.Shield: return ShieldRootPixelX;
            case ShopV2Kind.Musket: return MusketRootPixelX;
            default: return 0;
        }
    }

    internal static int RootPixelY(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return HeroRootPixelY;
            case ShopV2Kind.Shield: return ShieldRootPixelY;
            case ShopV2Kind.Musket: return MusketRootPixelY;
            default: return 0;
        }
    }

    internal static int AtlasWidth(ShopV2Kind kind) => CellWidth(kind) * Columns;
    internal static int AtlasHeight(ShopV2Kind kind) => CellHeight(kind) * Rows(kind);
    internal static int GridSlots(ShopV2Kind kind) => Columns * Rows(kind);

    /// <summary>所有层的 pivot = 画布地面锚点（同一图集内每格一致），单位为像素。</summary>
    internal static float PivotX(ShopV2Kind kind) => RootPixelX(kind) / (float)CellWidth(kind);
    // Image root uses TOP origin; Unity normalized pivot uses BOTTOM origin.
    internal static float PivotY(ShopV2Kind kind) => (CellHeight(kind) - RootPixelY(kind)) / (float)CellHeight(kind);

    internal static bool IsValidCell(ShopV2Kind kind, int cell) => IsKnown(kind) && cell >= 0 && cell < CellCount(kind);

    /// <summary>图集末尾未使用槽位（火枪 10/11、盾 26/27）；必须全透明，不创建 Sprite。</summary>
    internal static bool IsPaddingSlot(ShopV2Kind kind, int slot) => slot >= CellCount(kind) && slot < GridSlots(kind);

    internal static int RowOf(int cell) => cell / Columns;
    internal static int ColumnOf(int cell) => cell % Columns;

    /// <summary>state 层数量：英雄左/右 2，盾 4 龛，火枪 0。</summary>
    internal static int StateLayerCount(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return ShopV2HeroPolicy.SideCount;
            case ShopV2Kind.Shield: return ShopV2NichePolicy.Niches;
            default: return 0;
        }
    }

    internal static string ResourceName(ShopV2Kind kind)
    {
        switch (kind)
        {
            case ShopV2Kind.Hero: return "KingdomEnhancedMod.ShopV2Hero.png";
            case ShopV2Kind.Shield: return "KingdomEnhancedMod.ShopV2Shield.png";
            case ShopV2Kind.Musket: return "KingdomEnhancedMod.ShopV2Musket.png";
            default: return string.Empty;
        }
    }
}

/// <summary>8 帧时长表（1.6s 休息 + 7 帧动作，总周期 3.2s）。caller 传 Time.time；本类不读任何时钟。</summary>
internal static class ShopV2Frames
{
    internal const int FrameCount = 8;
    internal const float TotalSeconds = 3.2f;

    internal static readonly float[] Durations = { 1.6f, 0.18f, 0.18f, 0.14f, 0.12f, 0.18f, 0.18f, 0.62f };

    private static readonly float[] Boundaries = BuildBoundaries();

    private static float[] BuildBoundaries()
    {
        var bounds = new float[FrameCount];
        float acc = 0f;
        for (int i = 0; i < FrameCount - 1; i++)
        {
            acc += Durations[i];
            bounds[i] = acc;
        }
        bounds[FrameCount - 1] = TotalSeconds;
        return bounds;
    }

    /// <summary>周期内帧索引 0..7；帧 0 为长休息。负值/超长值按周期回绕（长休息与暂停值稳定同帧）。</summary>
    internal static int FrameAt(float gameTime)
    {
        if (float.IsNaN(gameTime) || float.IsInfinity(gameTime)) return 0;
        float t = gameTime % TotalSeconds;
        if (t < 0f) t += TotalSeconds;
        for (int i = 0; i < FrameCount - 1; i++)
            if (t < Boundaries[i]) return i;
        return FrameCount - 1;
    }
}

/// <summary>英雄座位格位映射。state 由既有 HeroShopBannerVisuals.VisualState 转换后传入。</summary>
internal static class ShopV2HeroPolicy
{
    internal const int SideCount = 2;
    internal const int StateCount = 5;

    internal const int Unavailable = 0; // 不在售
    internal const int Available = 1;   // 可购买（透明叠层）
    internal const int Occupied = 2;    // 已占用
    internal const int Reserved = 3;    // 已预留/归属未知：非空、不可购买
    internal const int Fallen = 4;      // 阵亡撕裂

    internal const int LeftFirstCell = ShopV2Layout.FirstSeatCell;        // 10..14
    internal const int RightFirstCell = ShopV2Layout.FirstSeatCell + StateCount; // 15..19

    /// <summary>非法 state（&lt;0 / &gt;4）归为 Reserved：绝不画成 available，也不假装空位。</summary>
    internal static int Normalize(int visualState) => visualState >= 0 && visualState < StateCount ? visualState : Reserved;

    /// <summary>两侧独立：左 10+state、右 15+state。</summary>
    internal static int CellFor(bool right, int visualState) => (right ? RightFirstCell : LeftFirstCell) + Normalize(visualState);

    /// <summary>可用（透明叠层，无需绘制）。</summary>
    internal static bool IsAvailable(int visualState) => Normalize(visualState) == Available;
}

/// <summary>盾龛投影：纯策略，只看 ready/unknown/mold/extra/占用计数，不查询库存或钱包。</summary>
internal static class ShopV2NichePolicy
{
    internal const int Niches = 4;
    internal const int BaseSeatsPerSide = 1;
    internal const int MaxSeatsPerSide = 2;

    /// <summary>niche 编号 → 侧：0 lowerLeft / 1 upperLeft / 2 lowerRight / 3 upperRight。</summary>
    internal static bool IsRight(int niche) => niche >= 2;

    /// <summary>每侧可见容量：未买扩展 1（基位），已买 2（基位 + 扩位）。</summary>
    internal static int Limit(bool extra) => extra ? MaxSeatsPerSide : BaseSeatsPerSide;

    internal static int CellFor(int niche, ShopV2NicheState state) =>
        ShopV2Layout.FirstSeatCell + niche * Niches + (int)state;

    /// <summary>
    /// 4 龛状态（索引 = niche）。优先级：ready==false 或 unknown → 全部 Unknown（暗盖，绝不空）；
    /// mold 未开 → 全部 Locked；随后每侧独立投影：基位(下) count>=1→Occupied else Available，
    /// 扩位(上) 未开→Locked、已开 count>=2→Occupied else Available；count 非法（&lt;0 / &gt;2 / &gt;limit）
    /// → 该侧两龛 Unknown（不 clamp 成“正常空龛”）。
    /// </summary>
    internal static ShopV2NicheState[] Project(bool ready, bool unknown, bool mold, bool leftExtra, bool rightExtra, int leftOccupied, int rightOccupied)
    {
        var states = new ShopV2NicheState[Niches];
        if (!ready || unknown)
        {
            Fill(states, ShopV2NicheState.Unknown);
            return states;
        }
        if (!mold)
        {
            Fill(states, ShopV2NicheState.Locked);
            return states;
        }
        ProjectSide(states, 0, leftExtra, leftOccupied);
        ProjectSide(states, 2, rightExtra, rightOccupied);
        return states;
    }

    private static void ProjectSide(ShopV2NicheState[] states, int firstNiche, bool extra, int occupied)
    {
        if (occupied < 0 || occupied > MaxSeatsPerSide || occupied > Limit(extra))
        {
            states[firstNiche] = ShopV2NicheState.Unknown;
            states[firstNiche + 1] = ShopV2NicheState.Unknown;
            return;
        }
        states[firstNiche] = occupied >= BaseSeatsPerSide ? ShopV2NicheState.Occupied : ShopV2NicheState.Available;
        states[firstNiche + 1] = !extra ? ShopV2NicheState.Locked
            : occupied >= MaxSeatsPerSide ? ShopV2NicheState.Occupied : ShopV2NicheState.Available;
    }

    private static void Fill(ShopV2NicheState[] states, ShopV2NicheState value)
    {
        for (int i = 0; i < states.Length; i++) states[i] = value;
    }
}

#if !SHOP_V2_CORE_ONLY
/// <summary>三张 V2 图集的不可变惰性缓存；与 binding 生命周期无关，跨岛复用。</summary>
internal static class ShopV2Art
{
    private const long MaxResourceBytes = 2L * 1024L * 1024L;

    private enum LoadState : byte { Unloaded = 0, Ready = 1, Unavailable = 2 }

    private sealed class KindCache
    {
        internal LoadState State;
        internal Texture2D Texture;
        internal Sprite[] Sprites;
        internal bool Logged;
    }

    private static readonly KindCache[] Caches = { new KindCache(), new KindCache(), new KindCache() };

    /// <summary>取某 kind 的 cell sprite。padding/非法格返回 false；Unavailable 后不再尝试加载。</summary>
    internal static bool TryGet(ShopV2Kind kind, int cell, out Sprite sprite)
    {
        sprite = null;
        if (!ShopV2Layout.IsKnown(kind)) return false;
        if (!ShopV2Layout.IsValidCell(kind, cell)) return false;
        var cache = Caches[(int)kind];
        if (cache.State == LoadState.Unavailable) return false;
        if (cache.State == LoadState.Unloaded && !Load(cache, kind)) return false;
        sprite = cache.Sprites[cell];
        return sprite != null;
    }

    private static bool Load(KindCache cache, ShopV2Kind kind)
    {
        Texture2D texture = null;
        var sprites = new Sprite[ShopV2Layout.CellCount(kind)];
        try
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ShopV2Layout.ResourceName(kind)))
            {
                if (stream == null || stream.Length <= 0 || stream.Length > MaxResourceBytes) throw new InvalidDataException("resource missing/oversized");
                var bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int step = stream.Read(bytes, read, bytes.Length - read);
                    if (step <= 0) throw new InvalidDataException("resource truncated");
                    read += step;
                }
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidDataException("decode failed");
            }
            if (texture.width != ShopV2Layout.AtlasWidth(kind) || texture.height != ShopV2Layout.AtlasHeight(kind))
                throw new InvalidDataException("atlas dimensions");
            ValidateCells(kind, texture);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            var pivot = new Vector2(ShopV2Layout.PivotX(kind), ShopV2Layout.PivotY(kind));
            int cellWidth = ShopV2Layout.CellWidth(kind), cellHeight = ShopV2Layout.CellHeight(kind);
            int rows = ShopV2Layout.Rows(kind);
            for (int cell = 0; cell < sprites.Length; cell++)
            {
                var rect = new Rect(ShopV2Layout.ColumnOf(cell) * cellWidth, (rows - 1 - ShopV2Layout.RowOf(cell)) * cellHeight, cellWidth, cellHeight);
                sprites[cell] = Sprite.Create(texture, rect, pivot, ShopV2Layout.PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                if (sprites[cell] == null) throw new InvalidDataException("sprite create failed");
            }
            cache.Texture = texture;
            cache.Sprites = sprites;
            cache.State = LoadState.Ready;
            return true;
        }
        catch (Exception e)
        {
            for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) UnityEngine.Object.Destroy(sprites[i]);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            cache.Texture = null;
            cache.Sprites = null;
            cache.State = LoadState.Unavailable;
            WarnOnce(cache, kind, e.GetType().Name + ": " + ShopV2Layout.ResourceName(kind));
            return false;
        }
    }

    /// <summary>
    /// 有界一次性像素校验：rear/front/merchant（cell0..9）必须非空；有效 seat/state 格允许透明
    /// （英雄 available、盾 available）；图集末尾空槽必须全透明。像素数组只用于校验，随即丢弃。
    /// </summary>
    private static void ValidateCells(ShopV2Kind kind, Texture2D texture)
    {
        int width = texture.width;
        int cellWidth = ShopV2Layout.CellWidth(kind), cellHeight = ShopV2Layout.CellHeight(kind), rows = ShopV2Layout.Rows(kind);
        Color32[] pixels = texture.GetPixels32();
        if (pixels == null || pixels.Length != width * texture.height) throw new InvalidDataException("pixel read failed");
        foreach (var pixel in pixels)
        {
            if (pixel.a != 0 && pixel.a != 255) throw new InvalidDataException("non-binary alpha");
            if (pixel.a == 0 && (pixel.r != 0 || pixel.g != 0 || pixel.b != 0))
                throw new InvalidDataException("transparent RGB must be zero");
        }
        try
        {
            int lastLayerCell = ShopV2Layout.MerchantFirstCell + ShopV2Layout.MerchantFrames - 1; // 9
            for (int slot = 0; slot < ShopV2Layout.GridSlots(kind); slot++)
            {
                int x0 = ShopV2Layout.ColumnOf(slot) * cellWidth, y0 = (rows - 1 - ShopV2Layout.RowOf(slot)) * cellHeight;
                bool opaque = CellHasOpaque(pixels, width, x0, y0, cellWidth, cellHeight);
                if (slot <= lastLayerCell)
                {
                    if (!opaque) throw new InvalidDataException("layer cell empty: " + slot);
                }
                else if (slot >= ShopV2Layout.CellCount(kind) && opaque)
                {
                    throw new InvalidDataException("padding cell not transparent: " + slot);
                }
            }
        }
        finally { pixels = null; }
    }

    private static bool CellHasOpaque(Color32[] pixels, int width, int x0, int y0, int cellWidth, int cellHeight)
    {
        for (int y = y0; y < y0 + cellHeight; y++)
        {
            int line = y * width;
            for (int x = x0; x < x0 + cellWidth; x++)
                if (pixels[line + x].a != 0) return true;
        }
        return false;
    }

    private static void WarnOnce(KindCache cache, ShopV2Kind kind, string reason)
    {
        if (cache.Logged) return;
        cache.Logged = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[ShopV2Art] " + kind + " unavailable: " + reason); } catch { }
    }
}

/// <summary>
/// 普通 C# 视觉 binding（非 MonoBehaviour、无注入）。生命周期：已有 Shop.Create 之后 Bind，
/// Active Tick 驱动，只有在既有事务取消/注销成功后的 Clear 才销毁自己的子层。
/// </summary>
internal sealed class ShopV2Visuals
{
    // 与 V2 静态合成一致的层深：front 在前景，states 次之，merchant 最靠相机（同 sortingOrder 内用 localZ）。
    private const float FrontZ = -0.001f;
    private const float StateZ = -0.002f;
    private const float MerchantZ = -0.003f;

    private static readonly bool[] AssetLogged = new bool[3];
    private static readonly bool[] UsageLogged = new bool[3];

    private readonly GameObject _root;
    private readonly ShopV2Kind _kind;
    private SpriteRenderer _front;
    private SpriteRenderer _merchant;
    private SpriteRenderer[] _states;
    private GameObject _frontObject;
    private GameObject _merchantObject;
    private GameObject[] _stateObjects;
    private int[] _stateCells;
    private int _merchantCell = -1;
    private bool _cleared;

    private ShopV2Visuals(GameObject root, ShopV2Kind kind)
    {
        _root = root;
        _kind = kind;
    }

    internal SpriteRenderer Front => _front;
    internal SpriteRenderer Merchant => _merchant;

    /// <summary>首个 state 层渲染器（盾/英雄各层同在 _renderers 语义下的第 0 个；火枪为 null）。</summary>
    internal SpriteRenderer State0 => _states != null && _states.Length > 0 ? _states[0] : null;

    /// <summary>资源先验证成功后一次性建子层；任何失败销毁已建子层并返回 null（全-or-none）。</summary>
    internal static ShopV2Visuals Bind(GameObject root, SpriteRenderer rear, ShopV2Kind kind)
    {
        try
        {
            if (root == null || rear == null || !ShopV2Layout.IsKnown(kind)) return null;
            if (!AssetsReady(kind))
            {
                WarnOnce(AssetLogged, kind, "[ShopV2Visuals] assets unavailable; binding skipped");
                return null;
            }
            var visuals = new ShopV2Visuals(root, kind);
            try
            {
                visuals.Build(rear);
                return visuals;
            }
            catch (Exception e)
            {
                visuals.Clear();
                WarnOnce(AssetLogged, kind, "[ShopV2Visuals] bind failed: " + e.GetType().Name);
                return null;
            }
        }
        catch { return null; }
    }

    private static bool AssetsReady(ShopV2Kind kind)
    {
        if (!ShopV2Art.TryGet(kind, ShopV2Layout.RearCell, out _)) return false;
        if (!ShopV2Art.TryGet(kind, ShopV2Layout.FrontCell, out _)) return false;
        for (int frame = 0; frame < ShopV2Layout.MerchantFrames; frame++)
            if (!ShopV2Art.TryGet(kind, ShopV2Layout.MerchantFirstCell + frame, out _)) return false;
        for (int cell = ShopV2Layout.FirstSeatCell; cell < ShopV2Layout.CellCount(kind); cell++)
            if (!ShopV2Art.TryGet(kind, cell, out _)) return false;
        return true;
    }

    private void Build(SpriteRenderer rear)
    {
        int stateCount = ShopV2Layout.StateLayerCount(_kind);
        _states = new SpriteRenderer[stateCount];
        _stateObjects = new GameObject[stateCount];
        _stateCells = new int[stateCount];
        for (int i = 0; i < stateCount; i++) _stateCells[i] = -1;

        ShopV2Art.TryGet(_kind, ShopV2Layout.FrontCell, out var frontSprite);
        _front = CreateLayer(rear, "KEM_ShopV2_Front", FrontZ, out _frontObject);
        _front.sprite = frontSprite;
        _front.enabled = true;

        ShopV2Art.TryGet(_kind, ShopV2Layout.MerchantFirstCell, out var merchantSprite);
        _merchant = CreateLayer(rear, "KEM_ShopV2_Merchant", MerchantZ, out _merchantObject);
        _merchant.sprite = merchantSprite;
        _merchant.enabled = merchantSprite != null;
        _merchantCell = merchantSprite != null ? ShopV2Layout.MerchantFirstCell : -1;

        for (int i = 0; i < stateCount; i++)
        {
            _states[i] = CreateLayer(rear, "KEM_ShopV2_State" + i, StateZ, out _stateObjects[i]);
            _states[i].enabled = false;
        }
    }

    /// <summary>子层：同 native sortingLayerID/order/sharedMaterial，localPosition 零 + 层深；不触发任何源字段变更。</summary>
    private SpriteRenderer CreateLayer(SpriteRenderer rear, string name, float z, out GameObject created)
    {
        created = new GameObject(name);
        try
        {
            created.transform.SetParent(_root.transform, false);
            created.transform.localPosition = new Vector3(0f, 0f, z);
            var renderer = created.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID = rear.sortingLayerID;
            renderer.sortingOrder = rear.sortingOrder;
            if (rear.sharedMaterial != null) renderer.sharedMaterial = rear.sharedMaterial;
            renderer.enabled = false;
            return renderer;
        }
        catch
        {
            UnityEngine.Object.Destroy(created);
            created = null;
            throw;
        }
    }

    /// <summary>商人帧：只有帧索引变化才改 sprite；暂停由 caller 决定（不读时钟、不扫场景）。</summary>
    internal void TickMerchant(float gameTime)
    {
        if (Unavailable()) return;
        int cell = ShopV2Layout.MerchantFirstCell + ShopV2Frames.FrameAt(gameTime);
        if (cell == _merchantCell) return;
        if (ShopV2Art.TryGet(_kind, cell, out var sprite) && sprite != null)
        {
            _merchant.sprite = sprite;
            _merchantCell = cell;
            _merchant.enabled = true;
        }
    }

    /// <summary>
    /// 英雄两侧席位：caller 传入已转换的 VisualState（0 不可用 / 1 可用透明 / 2 占用 / 3 预留 / 4 阵亡）。
    /// 非法值按 Reserved 处理（绝不画成 available）。gameTime 仅为签名一致性保留：V2 英雄层是纯 state 驱动。
    /// </summary>
    internal void TickHero(int leftVisualState, int rightVisualState, float gameTime)
    {
        if (Unavailable()) return;
        if (_states.Length != ShopV2HeroPolicy.SideCount) { WarnUsage(); return; }
        SetState(0, ShopV2HeroPolicy.CellFor(false, leftVisualState), !ShopV2HeroPolicy.IsAvailable(leftVisualState));
        SetState(1, ShopV2HeroPolicy.CellFor(true, rightVisualState), !ShopV2HeroPolicy.IsAvailable(rightVisualState));
    }

    /// <summary>
    /// 盾四龛：caller直接传quota已计算的占用计数（其中已含Paying/PaidTool）；不能再加ShopOccupied。
    /// 纯投影，不查询身份/库存/钱包。gameTime 仅为签名一致性保留。
    /// </summary>
    internal void TickShield(bool ready, bool unknown, bool mold, bool leftExtra, bool rightExtra, int leftOccupied, int rightOccupied, float gameTime)
    {
        if (Unavailable()) return;
        if (_states.Length != ShopV2NichePolicy.Niches) { WarnUsage(); return; }
        var states = ShopV2NichePolicy.Project(ready, unknown, mold, leftExtra, rightExtra, leftOccupied, rightOccupied);
        for (int niche = 0; niche < states.Length; niche++)
        {
            var state = states[niche];
            SetState(niche, ShopV2NichePolicy.CellFor(niche, state), state != ShopV2NicheState.Available);
        }
    }

    private void SetState(int index, int cell, bool visible)
    {
        var renderer = _states[index];
        if (renderer == null) return;
        if (visible)
        {
            if (_stateCells[index] != cell)
            {
                if (!ShopV2Art.TryGet(_kind, cell, out var sprite) || sprite == null) return; // 保持上一稳定画面
                renderer.sprite = sprite;
                _stateCells[index] = cell;
            }
            if (!renderer.enabled) renderer.enabled = true;
        }
        else if (renderer.enabled)
        {
            renderer.enabled = false; // available = 透明叠层，无需绘制
        }
    }

    /// <summary>只销毁自己创建的子层；图集/纹理属于 ShopV2Art 的不可变缓存，永不在此销毁。</summary>
    internal void Clear()
    {
        if (_cleared) return;
        _cleared = true;
        DestroyQuietly(_frontObject);
        DestroyQuietly(_merchantObject);
        if (_stateObjects != null)
            for (int i = 0; i < _stateObjects.Length; i++) DestroyQuietly(_stateObjects[i]);
        _front = null;
        _merchant = null;
        _states = null;
        _frontObject = null;
        _merchantObject = null;
        _stateObjects = null;
        _stateCells = null;
        _merchantCell = -1;
    }

    private bool Unavailable() => _cleared || _root == null || _front == null;

    private static void DestroyQuietly(GameObject target)
    {
        try { if (target != null) UnityEngine.Object.Destroy(target); } catch { }
    }

    private void WarnUsage()
    {
        WarnOnce(UsageLogged, _kind, "[ShopV2Visuals] state tick on " + _kind + " without matching state layers");
    }

    private static void WarnOnce(bool[] log, ShopV2Kind kind, string message)
    {
        int index = (int)kind;
        if (index < 0 || index >= log.Length || log[index]) return;
        log[index] = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning(message); } catch { }
    }
}
#endif
