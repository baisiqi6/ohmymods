using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 传送表现句柄：一次 Begin 一代（slot + 模块单调 generation）。旧句柄永不匹配后续代
/// （Clear/世界重建后 generation 也不重置），不会误伤后来者的 effect。
/// </summary>
internal readonly struct CoinCourierFxHandle
{
    internal readonly int Slot;
    internal readonly long Generation;

    internal CoinCourierFxHandle(int slot, long generation)
    {
        Slot = slot;
        Generation = generation;
    }

    internal bool IsValid => Slot >= 0;
}

/// <summary>传送线样式：同在脚锚上立起整组，只有线轴方向不同（几何表在 <see cref="CoinCourierTeleportFx"/>）。</summary>
internal enum CoinCourierTeleportStyle
{
    /// <summary>离散横线（y 偏移 + 中心 x 偏移 + 半长，四点同 y）。</summary>
    Horizontal,
    /// <summary>离散竖线（x 偏移 + 中心 y + 半长，四点同 x）。</summary>
    Vertical
}

/// <summary>
/// 金币哥布林与税收助手共用的传送表现：一次 Begin 生成一个 effect 组，
/// 组内 10 根长短、宽窄、明暗与色阶各异的离散线（6 条轻斜骨架 + 4 条填充细辅线；各自 4 点
/// LineRenderer，四点共线等分 0、1/3、2/3、1，宽度由一次性创建的 widthCurve 收尖，
/// 端部 ≤ 中线宽的约 10%）。每条带固定轻微斜度（约 4.6°~7.8°，同号 rise/lean 与收缩同乘，
/// 端点关于表中点对称、中点不漂移，无随机抖动）；宽度/亮度按主长线、中等线、短细线分档，
/// 辅线更细更暗（alpha .4~.65）。组内线数 <see cref="DashesPerEffect"/> 由表长派生。
/// 样式由调用方显式选择：Horizontal 横线、Vertical 竖线（哥布林与税收助手新四槽用竖纹，
/// 旧调用形状默认横纹）。每条线依次淡入 → 沿自身线轴收缩 → 淡出，最晚一条在 .24s 内结束，
/// 重叠错落而不延迟角色动作。不是拖尾、不使用 TrailRenderer。
///
/// 契约：
/// * 句柄：Begin 返回 <see cref="CoinCourierFxHandle"/>；业务只取消/持有自己的句柄
///   （<see cref="Cancel"/>），旧句柄不会取消复用后的别人的 effect。
/// * <see cref="Clear"/> 只在世界/模块整体结束时调用（由 Operator 整合世界清理由）；
///   业务停用请 Cancel 自己的句柄，不得在业务关闭时清共享池。
/// * 时钟：调用方驱动。<see cref="Tick(float)"/> 按 Time.frameCount 去重，
///   同帧多次调用只推进一次（runtime 与后续 ModPanel 集中 Tick 并存时不双倍）；
///   纯核心为 <see cref="TickForFrame"/>。暂停传 0/非法值不改变任何状态。
/// * 只画线：不创建角色/Damageable/Wallet/网络组件，不生成金币、不碰账本、不持有身份。
///   排序默认 0/0；需要贴合角色层时用 5 参重载显式给。
/// * 单表：几何、宽度、颜色、时差全部来自 <see cref="StripeSpecs"/> 一项只读表；
///   线数 <see cref="DashesPerEffect"/> 直接由表长派生，不存在可失配的平行数组。
/// * 热路径：Tick/Configure 只写既有 4 个顶点、start/endColor 与 widthMultiplier；
///   AnimationCurve 只在类型初始化时为每条线建一次，池复用与逐帧更新零数组/零 curve 分配。
/// </summary>
internal static class CoinCourierTeleportFx
{
    // 8 助手双端 + 哥布林单端 = 17 组（issue-89）。满时复用最旧活动组，代际取消语义不变。
    internal const int MaxConcurrent = 17;     // effect 组上限
    internal const float LifetimeSeconds = 0.24f;

    private const float FadeInPortion = 0.22f;   // alpha 从 0 升到满的时程占比
    private const float FadeOutPortion = 0.55f;  // alpha 从满降到 0 的时程占比
    private const float ShrinkPortion = 0.5f;    // 半长从 1.0 收缩到 0.5
    private const float Third = 1f / 3f;         // 四点等分比例（与 curve key time 一致）
    private const float BaseLineWidth = 0.026f;  // 单条线世界宽度上限；实际取 curve 值 × 表内比例
    private const float TipShare = 0.09f;        // 起端收尖：中线峰值的最多约 10%
    private const float TipShareEnd = 0.07f;     // 末端收尖
    private const float BaseColorMix = 0.10f;    // 传入 Base RGB 只做少量色调混合，主体保持白亮/灰白
    private const float EndRgbFloor = 0.88f;     // 端点亮度随 alpha 端点比例轻微压暗的下限
    private const float EndRgbSpan = 0.12f;

    private readonly struct StripeSpec
    {
        internal readonly float HorizontalOffsetY;      // 横线：相对脚锚的 y（行中心）
        internal readonly float HorizontalCenterX;      // 横线：中心 x 相对脚锚的轻微错位
        internal readonly float HorizontalHalfLength;   // 横线：半长
        internal readonly float HorizontalRiseY;        // 横线：两端 ±y 的半高差（斜度 = rise/half）
        internal readonly float VerticalOffsetX;        // 竖线：相对脚锚的 x（线中心）
        internal readonly float VerticalCenterY;        // 竖线：中心 y
        internal readonly float VerticalHalfLength;     // 竖线：半长
        internal readonly float VerticalLeanX;          // 竖线：两端 ±x 的半宽差（斜度 = lean/half）
        internal readonly float WidthAtFirstThird;      // widthCurve t=1/3 值（BaseLineWidth 的比例）
        internal readonly float WidthAtSecondThird;     // widthCurve t=2/3 值（中部左右不对称）
        internal readonly byte Palette;                 // 0 白亮 / 1 灰白 / 2 淡金
        internal readonly float Brightness;             // RGB 明暗微调
        internal readonly float Opacity;                // 该线整体 alpha（乘在 Base alpha 上）
        internal readonly float StartAlpha;             // 起点端 alpha/亮度比例
        internal readonly float EndAlpha;               // 终点端 alpha/亮度比例
        internal readonly float Delay;                  // 该线自身窗口的起始偏移（秒）

        internal StripeSpec(float horizontalOffsetY, float horizontalCenterX, float horizontalHalfLength,
            float horizontalRiseY, float verticalOffsetX, float verticalCenterY, float verticalHalfLength,
            float verticalLeanX, float widthAtFirstThird, float widthAtSecondThird, byte palette,
            float brightness, float opacity, float startAlpha, float endAlpha, float delay)
        {
            HorizontalOffsetY = horizontalOffsetY;
            HorizontalCenterX = horizontalCenterX;
            HorizontalHalfLength = horizontalHalfLength;
            HorizontalRiseY = horizontalRiseY;
            VerticalOffsetX = verticalOffsetX;
            VerticalCenterY = verticalCenterY;
            VerticalHalfLength = verticalHalfLength;
            VerticalLeanX = verticalLeanX;
            WidthAtFirstThird = widthAtFirstThird;
            WidthAtSecondThird = widthAtSecondThird;
            Palette = palette;
            Brightness = brightness;
            Opacity = opacity;
            StartAlpha = startAlpha;
            EndAlpha = endAlpha;
            Delay = delay;
        }
    }

    // 单一事实来源：6 条线各自的长短/位置/宽度/色阶/端点明暗/时差。
    // 尺度按脚锚展开（不是效果中心）：横线 y 覆盖 ~.09~.66 的角色身体，
    // 竖线并集覆盖 ~.02~.84（低线到脚踝、高线过头顶），长度与宽度一一不同。
    // Delay 按时间先后错开（排序后相邻间隔 .011~.012s），最晚延迟 + 自身窗口 == 总寿命，
    // 全部在 .24s 内结束且全部在 .12s 显形窗口前可见。
    private static readonly StripeSpec[] StripeSpecs = BuildStripeSpecs();

    /// <summary>每组线数：唯一由表长派生，不重复魔法数字。</summary>
    internal static readonly int DashesPerEffect = StripeSpecs.Length;

    // 色阶：白亮主、灰白辅、淡金点缀（每条线再乘自己的 Brightness，并与传入 Base RGB 少量混合）。
    private static readonly Color[] Palette =
    {
        new Color(1.00f, 1.00f, 1.00f),
        new Color(0.80f, 0.84f, 0.89f),
        new Color(1.00f, 0.93f, 0.70f)
    };

    // 每条线的宽度曲线：4 个 key 的 time 与四点顶点一一对应（0、1/3、2/3、1），
    // 端部收尖、中部不定对称，切线取相邻 secant 斜率（线性过渡，不超调）。
    // 类型初始化时一次性建立；Configure/Tick 只读引用，池复用不重建。
    private static readonly AnimationCurve[] WidthCurves = BuildWidthCurves();

    private sealed class Dash
    {
        internal GameObject Root;
        internal LineRenderer Line;
    }

    private sealed class Slot
    {
        internal GameObject Root;
        internal Dash[] Dashes;
        internal CoinCourierTeleportStyle Style;   // 每次 Begin（含池复用）先写入，再首次 Configure
        internal Vector3 Origin;
        internal float Age;
        internal bool Active;
        internal Color Base;
        internal float Scale;
        internal long Generation;
    }

    private static readonly List<Slot> Slots = new();
    private static readonly HashSet<string> Warned = new();
    private static GameObject _root;
    private static Material _material;
    private static bool _materialResolved;
    private static int _lastTickFrame = int.MinValue;
    private static long _nextGeneration;   // 模块单调：Clear/Shutdown 均不重置

    internal static int ActiveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].Active) count++;
            }
            return count;
        }
    }
    /// <summary>在角色脚锚（actor 根 / S.Position，非身体中心）向上立起一组线；3/5 参重载保持旧调用形状（Horizontal）。</summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale)
        => Begin(worldPosition, color, scale, 0, 0);

    /// <summary>5 参重载：排序由调用方显式指定（3 参重载用 0/0），样式保持 Horizontal。</summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder)
        => Begin(worldPosition, color, scale, sortingLayerID, sortingOrder, CoinCourierTeleportStyle.Horizontal);

    /// <summary>6 参重载：样式显式给；非法样式在占用池槽前拒收（不猜样式、不残留未知几何）。</summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder, CoinCourierTeleportStyle style)
    {
        if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;
        if (!IsFinite(color)) color = Color.white;
        if (style != CoinCourierTeleportStyle.Horizontal && style != CoinCourierTeleportStyle.Vertical)
        {
            WarnOnce("unknown teleport stripe style; refusing to draw");
            return new CoinCourierFxHandle(-1, 0);
        }
        try
        {
            Slot slot = Acquire();
            if (slot == null || slot.Root == null) return new CoinCourierFxHandle(-1, 0);
            if (_nextGeneration == long.MaxValue)
            {
                WarnOnce("effect generation exhausted; refusing new effects");
                return new CoinCourierFxHandle(-1, 0);
            }
            slot.Generation = ++_nextGeneration;
            slot.Style = style;   // 池复用的槽在这里被覆盖，首次 Configure 之前已完成样式写入
            slot.Origin = worldPosition;
            slot.Base = color;
            slot.Scale = scale;
            slot.Age = 0f;
            slot.Active = true;
            slot.Root.SetActive(true);
            for (int i = 0; i < slot.Dashes.Length; i++)
            {
                if (slot.Dashes[i] != null && slot.Dashes[i].Root != null) slot.Dashes[i].Root.SetActive(true);
            }
            Configure(slot, 0f);   // 透明起步：淡入由 Tick 时程负责
            for (int i = 0; i < slot.Dashes.Length; i++)
            {
                slot.Dashes[i].Line.sortingLayerID = sortingLayerID;
                slot.Dashes[i].Line.sortingOrder = sortingOrder;
            }
            return new CoinCourierFxHandle(Slots.IndexOf(slot), slot.Generation);
        }
        catch (Exception e)
        {
            WarnOnce("begin failed: " + e.GetType().Name);
            return new CoinCourierFxHandle(-1, 0);
        }
    }

    /// <summary>只取消该句柄这一代；句柄过期/无效/已结束时不碰任何当前 effect。</summary>
    internal static void Cancel(CoinCourierFxHandle handle)
    {
        if (!handle.IsValid || handle.Slot >= Slots.Count) return;
        Slot slot = Slots[handle.Slot];
        if (!slot.Active || slot.Generation != handle.Generation) return;
        Deactivate(slot);
    }

    /// <summary>调用方驱动的薄包装：按 Time.frameCount 去重，同帧多次调用只推进一次。</summary>
    internal static void Tick(float gameDelta) => TickForFrame(gameDelta, Time.frameCount);

    /// <summary>纯核心：显式帧号去重；非正/非有限增量（暂停、卡帧值）忽略且不消费该帧。</summary>
    internal static void TickForFrame(float gameDelta, int frameId)
    {
        if (!float.IsFinite(gameDelta) || gameDelta <= 0f) return;
        if (frameId == _lastTickFrame) return;
        _lastTickFrame = frameId;
        for (int i = 0; i < Slots.Count; i++)
        {
            Slot slot = Slots[i];
            if (!slot.Active) continue;
            slot.Age += gameDelta;
            if (!(slot.Age / LifetimeSeconds < 1f))
            {
                Deactivate(slot);
                continue;
            }
            Configure(slot, slot.Age);
        }
    }

    /// <summary>世界/模块整体结束才调用：销毁全部 effect 组、根对象与共享材质。之后可继续 Begin。</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            Slot slot = Slots[i];
            GameObject root = slot.Root;
            slot.Root = null;
            slot.Dashes = null;
            slot.Active = false;
            if (root != null) DestroyQuietly(root);
        }
        Slots.Clear();
        if (_root != null)
        {
            DestroyQuietly(_root);
            _root = null;
        }
        if (_material != null)
        {
            DestroyQuietly(_material);
            _material = null;
        }
        _materialResolved = false;
    }

    /// <summary>
    /// 逐帧热路径：只写既有顶点/颜色/宽度。每条线用自己延迟后的局部时钟（Delay + 局部窗口 ==
    /// 总寿命，因此最晚一条也在 .24s 内结束），四点共线等分 0、1/3、2/3、1，与 widthCurve 的
    /// key time 对齐；端部颜色/alpha 与另一端不同，整体色相来自只读色阶并按条目亮度区分。
    /// </summary>
    private static void Configure(Slot slot, float age)
    {
        for (int i = 0; i < slot.Dashes.Length; i++)
        {
            Dash dash = slot.Dashes[i];
            if (dash == null || dash.Line == null) continue;
            StripeSpec spec = StripeSpecs[i];

            float local = (age - spec.Delay) / (LifetimeSeconds - spec.Delay);
            float shrink;
            float factor;
            if (local <= 0f)
            {
                shrink = 1f;
                factor = 0f;
            }
            else
            {
                if (local > 1f) local = 1f;
                shrink = 1f - ShrinkPortion * local;
                float fadeIn = local / FadeInPortion;
                float fadeOut = (1f - local) / FadeOutPortion;
                factor = fadeIn < fadeOut ? fadeIn : fadeOut;
                if (factor > 1f) factor = 1f;
            }

            Vector3 first;
            Vector3 second;
            switch (slot.Style)
            {
                case CoinCourierTeleportStyle.Vertical:
                {
                    float half = spec.VerticalHalfLength * slot.Scale * shrink;
                    float lean = spec.VerticalLeanX * slot.Scale * shrink;   // 与收缩同乘：斜率恒定、中点不动
                    float x = slot.Origin.x + spec.VerticalOffsetX * slot.Scale;
                    float centerY = slot.Origin.y + spec.VerticalCenterY * slot.Scale;
                    first = new Vector3(x - lean, centerY - half, slot.Origin.z);
                    second = new Vector3(x + lean, centerY + half, slot.Origin.z);
                    break;
                }
                default:   // Horizontal；非法样式已在 Begin 拒收，不可能到达这里
                {
                    float half = spec.HorizontalHalfLength * slot.Scale * shrink;
                    float rise = spec.HorizontalRiseY * slot.Scale * shrink;  // 与收缩同乘：斜率恒定、中点不动
                    float y = slot.Origin.y + spec.HorizontalOffsetY * slot.Scale;
                    float centerX = slot.Origin.x + spec.HorizontalCenterX * slot.Scale;
                    first = new Vector3(centerX - half, y - rise, slot.Origin.z);
                    second = new Vector3(centerX + half, y + rise, slot.Origin.z);
                    break;
                }
            }

            Vector3 step = (second - first) * Third;
            Vector3 mid1 = first + step;
            dash.Line.SetPosition(0, first);
            dash.Line.SetPosition(1, mid1);
            dash.Line.SetPosition(2, mid1 + step);
            dash.Line.SetPosition(3, second);

            Color role = Palette[spec.Palette];
            float r = (role.r + (slot.Base.r - role.r) * BaseColorMix) * spec.Brightness;
            float g = (role.g + (slot.Base.g - role.g) * BaseColorMix) * spec.Brightness;
            float b = (role.b + (slot.Base.b - role.b) * BaseColorMix) * spec.Brightness;
            float baseAlpha = slot.Base.a * spec.Opacity * factor;
            float startAlpha = Math.Clamp(baseAlpha * spec.StartAlpha, 0f, 1f);
            float endAlpha = Math.Clamp(baseAlpha * spec.EndAlpha, 0f, 1f);
            float startRgb = EndRgbFloor + EndRgbSpan * spec.StartAlpha;
            float endRgb = EndRgbFloor + EndRgbSpan * spec.EndAlpha;
            dash.Line.startColor = new Color(r * startRgb, g * startRgb, b * startRgb, startAlpha);
            dash.Line.endColor = new Color(r * endRgb, g * endRgb, b * endRgb, endAlpha);
            dash.Line.widthMultiplier = slot.Scale;
            dash.Line.enabled = true;
        }
    }

    private static StripeSpec[] BuildStripeSpecs()
    {
        // 顺序 = 顶点索引 0..9：0~5 为原轻斜骨架（长/中/短三档），6~9 为填 H 行间与 V 列间
        // 空隙的细辅线（更短更细、alpha .4~.65，不外扩原有身体覆盖边界；H 填充行 y≈.60/.47/.25/.14，
        // V 填充列 x≈-.20/-.08/.055/.255，中心与半长都收在既有包络内）。
        // H（行中心 x、行中心 y、半长、rise）、V（线中心 x、线中心 y、半长、lean），
        // 斜度 = rise/half（H）或 lean/half（V）∈ .08~.137（约 4.6°~7.8°），正负两向混排、固定无随机。
        // 宽度对（t=1/3, t=2/3）乘 BaseLineWidth 后：主线 .02496/.02288 最粗、中等 .01846/.01690、
        // 细线 .01352/.01248 与四辅线 .01287/.01243/.01258/.01235；全部落在 .012~.026 且互不相同。
        // 延迟全部落在 0~.058s：骨架保持原值，辅线插入 .005/.017/.029/.041（相邻间隔约 .005~.011）。
        return new[]
        {
            new StripeSpec(0.66f, 0.02f, 0.31f, 0.025f, -0.25f, 0.38f, 0.32f, 0.033f, 0.90f, 0.96f, 0, 1.00f, 0.98f, 0.62f, 1.00f, 0.023f),
            new StripeSpec(0.52f, -0.08f, 0.18f, -0.021f, -0.14f, 0.31f, 0.13f, -0.016f, 0.68f, 0.71f, 1, 0.92f, 0.62f, 1.00f, 0.58f, 0.000f),
            new StripeSpec(0.43f, 0.07f, 0.12f, 0.015f, -0.015f, 0.44f, 0.40f, 0.044f, 0.50f, 0.52f, 0, 1.00f, 0.92f, 0.70f, 1.00f, 0.035f),
            new StripeSpec(0.31f, 0.025f, 0.28f, 0.025f, 0.11f, 0.53f, 0.21f, -0.025f, 0.84f, 0.88f, 2, 0.97f, 0.95f, 1.00f, 0.62f, 0.058f),
            new StripeSpec(0.19f, -0.04f, 0.16f, -0.017f, 0.205f, 0.395f, 0.33f, 0.026f, 0.62f, 0.65f, 1, 0.90f, 0.58f, 0.66f, 1.00f, 0.011f),
            new StripeSpec(0.09f, 0.06f, 0.08f, 0.01f, 0.29f, 0.22f, 0.095f, -0.013f, 0.46f, 0.48f, 0, 0.88f, 0.66f, 1.00f, 0.55f, 0.047f),
            new StripeSpec(0.60f, -0.05f, 0.115f, -0.011f, -0.20f, 0.40f, 0.16f, 0.014f, 0.482f, 0.495f, 1, 0.90f, 0.58f, 0.60f, 1.00f, 0.005f),
            new StripeSpec(0.47f, 0.04f, 0.10f, 0.008f, -0.08f, 0.47f, 0.12f, -0.010f, 0.466f, 0.478f, 0, 0.88f, 0.52f, 1.00f, 0.62f, 0.017f),
            new StripeSpec(0.25f, -0.055f, 0.09f, -0.008f, 0.055f, 0.30f, 0.15f, 0.012f, 0.472f, 0.484f, 2, 0.95f, 0.64f, 0.68f, 1.00f, 0.029f),
            new StripeSpec(0.14f, 0.045f, 0.06f, 0.005f, 0.255f, 0.26f, 0.10f, -0.009f, 0.464f, 0.475f, 1, 0.86f, 0.46f, 1.00f, 0.56f, 0.041f)
        };
    }

    private static AnimationCurve[] BuildWidthCurves()
    {
        var curves = new AnimationCurve[StripeSpecs.Length];
        for (int i = 0; i < curves.Length; i++)
        {
            StripeSpec spec = StripeSpecs[i];
            float peak = (spec.WidthAtFirstThird > spec.WidthAtSecondThird
                ? spec.WidthAtFirstThird : spec.WidthAtSecondThird) * BaseLineWidth;
            float v0 = peak * TipShare;
            float v1 = spec.WidthAtFirstThird * BaseLineWidth;
            float v2 = spec.WidthAtSecondThird * BaseLineWidth;
            float v3 = peak * TipShareEnd;
            float m0 = (v1 - v0) / Third;          // 相邻 key 的 secant 斜率：线性过渡、无超调
            float m1 = (v2 - v1) / Third;
            float m2 = (v3 - v2) / Third;
            var keys = new Keyframe[4];
            keys[0] = new Keyframe(0f, v0, m0, m0);
            keys[1] = new Keyframe(Third, v1, m0, m1);
            keys[2] = new Keyframe(2f * Third, v2, m1, m2);
            keys[3] = new Keyframe(1f, v3, m2, m2);
            curves[i] = new AnimationCurve(keys);
        }
        return curves;
    }

    private static Slot Acquire()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            if (!Slots[i].Active) return Slots[i];
        }
        if (Slots.Count < MaxConcurrent)
        {
            Slot created = CreateSlot();
            if (created != null) Slots.Add(created);
            return created;
        }
        Slot oldest = null;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (oldest == null || Slots[i].Age > oldest.Age) oldest = Slots[i];
        }
        return oldest;   // 池满：复用最旧的 effect 组（上限按组计）
    }

    private static Slot CreateSlot()
    {
        GameObject group = null;
        try
        {
            if (!EnsureRoot()) return null;
            group = new GameObject("KEM_CoinCourierTeleportFxEffect");
            group.transform.SetParent(_root.transform, false);
            group.transform.localPosition = Vector3.zero;
            Dash[] dashes = new Dash[DashesPerEffect];
            for (int i = 0; i < dashes.Length; i++)
            {
                GameObject dashObject = new GameObject("KEM_CoinCourierTeleportDash" + i);
                dashObject.transform.SetParent(group.transform, false);
                LineRenderer line = dashObject.AddComponent<LineRenderer>();
                if (line == null)
                {
                    DestroyQuietly(dashObject);
                    throw new InvalidOperationException("line renderer missing on a dash");
                }
                line.useWorldSpace = true;
                line.loop = false;
                line.positionCount = 4;   // 四点共线等分，与 widthCurve 的 4 个 key 对齐
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.widthCurve = WidthCurves[i];   // 一次建立、池复用不重建
                Material material = ResolveMaterial();
                if (material != null) line.sharedMaterial = material;
                line.enabled = false;
                dashObject.SetActive(false);
                dashes[i] = new Dash { Root = dashObject, Line = line };
            }
            group.SetActive(false);
            return new Slot { Root = group, Dashes = dashes };
        }
        catch (Exception e)
        {
            if (group != null) DestroyQuietly(group);
            WarnOnce("effect group create failed: " + e.GetType().Name);
            return null;
        }
    }

    private static void Deactivate(Slot slot)
    {
        slot.Active = false;
        slot.Age = 0f;
        try
        {
            if (slot.Dashes != null)
            {
                for (int i = 0; i < slot.Dashes.Length; i++)
                {
                    Dash dash = slot.Dashes[i];
                    if (dash == null) continue;
                    if (dash.Line != null) dash.Line.enabled = false;
                    if (dash.Root != null) dash.Root.SetActive(false);
                }
            }
            if (slot.Root != null) slot.Root.SetActive(false);
        }
        catch (Exception)
        {
            // 停用失败不抛出：下一帧 Tick 会再次尝试。
        }
    }

    private static bool EnsureRoot()
    {
        if (_root != null) return true;
        try
        {
            _root = new GameObject("KEM_CoinCourierTeleportFx");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            return true;
        }
        catch (Exception e)
        {
            _root = null;
            WarnOnce("root create failed: " + e.GetType().Name);
            return false;
        }
    }

    private static Material ResolveMaterial()
    {
        if (_materialResolved) return _material;
        _materialResolved = true;
        try
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _material = new Material(shader);
        }
        catch (Exception)
        {
            _material = null;
        }
        if (_material == null) WarnOnce("Sprites/Default shader unavailable; dashes keep the renderer default material");
        return _material;
    }

    private static bool IsFinite(Color value)
        => float.IsFinite(value.r) && float.IsFinite(value.g)
        && float.IsFinite(value.b) && float.IsFinite(value.a);

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        try { if (target != null) UnityEngine.Object.Destroy(target); }
        catch (Exception) { }
    }

    private static void WarnOnce(string reason)
    {
        if (!Warned.Add(reason)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourierTeleportFx] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：表现层绝不因日志失败而中断。
        }
    }
}
