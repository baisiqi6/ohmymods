using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄弓手「Artemis 金箭」外观：只换 runtime SpriteRenderer 的 sprite + 实例色，别的一律不碰。
///
/// 需求（operator 2026-09-15 锁定）：被选中的英雄弓手（每侧至多 1 名）射出的**每一支箭**——
/// 原生主箭 + 本模组的额外箭，含打猎的那支主箭——显示 Artemis 神器箭外观；其他弓手/其他来源的箭不变。
/// **纯外观**：不添加 ArtemisArrow 组件、不模拟 homing/20 次命中、不改伤害/池/prefab/材质/collider/
/// root transform/trail/共享 SO；不发 RPC、不做网络同步（沿用英雄既有的离线门）。
///   * 形状 = 原生 artemis_bow_arrow（resources.assets sprite 9867：30x5 px、pivot(.5,.5)），
///     从 embedded PNG `KingdomEnhancedMod.ArtemisArrow.png` 解码，Point/Clamp/无 MipMap，只加载一次；
///   * 显示尺寸 = 原生尺寸 × <see cref="HeroArrowDisplayScale"/>（0.65，用户 2026-09-17 锁定）：只把自建
///     Sprite 的 PPU 从 32 提到 32/0.65（≈49.23，世界尺寸 0.609375），不动 transform/collider/弹道/伤害；
///   * 颜色 = 原生 ArtemisArrow SpriteRenderer(79915) 的 _Highlight 金 (.99215686,.90196079,.44705883)，
///     透明度保持该箭原本的 alpha（实例色，不写 sharedMaterial）。
///
/// 为什么必须用「发射作用域」而不是 arrow.archer：<c>Arrow.OnEnable</c> 在原生 FireArrowInternal 的
/// <c>Pool.Spawn</c> 内执行，**早于** <c>arrow.archer = source</c>，池复用还会把上一任 owner 留在字段里。
/// 因此外观资格只来自「本次 FireArrowInternal 调用是不是英雄射的箭」：
///   * <see cref="BeginShot"/>（FireArrowInternal Prefix，Priority.First）：英雄（HeroArcherRuntime.IsHero，
///     含开关/当前 world/离线门）→ 压入自有 main-thread 作用域栈（≤<see cref="MaxStack"/> 层）；
///     非英雄/关闸/溢出压**掩蔽项**（屏蔽外层资格；溢出项不占栈位但 Depth 仍对称自增，出栈即恢复）。
///   * <see cref="ResetArrow"/>（Arrow.OnEnable Prefix，Priority.First）：先归还——池复用必须在原生
///     OnEnable 之前恢复原样，且不受功能开关限制（关闭期间发生的复用同样要清干净）；墙碰撞按箭身份
///     兜底归还（<see cref="HeroArcherWallPierce.Restore"/>），权威归还仍走回执缝合点（见该文件账本说明）。
///   * <see cref="OnSpawn"/>（Arrow.OnEnable Postfix，Priority.Last）：作用域内才上色（同一 shot 的主箭与
///     额外箭天然同域）；OnEnable 时刻**绝不**读 arrow.archer。英雄分支外观写入成功后由
///     <see cref="HeroArcherWallPierce.Apply"/> 给该箭挂上「无视墙碰撞」——同样以既有作用域为资格门。
///   * <see cref="EndShot"/>（Finalizer，在所有 Postfix 之后）：出栈，并对本次 shot 回执复核
///     <c>arrow.archer</c> 确实是该射手（归属复核只在原生写完 owner 之后做；读不到/为 null 一律不撤）。
///   * <see cref="Tick"/>（operator 接 ModPanel.Update）：只扫自有 ≤<see cref="Capacity"/> 条回执——
///     功能关/世界失效或变更/箭已回收 → 归还；待归还 → 退避重试；未复核 → 复核；已确认 → 账本里
///     未确认的 true 写入/未决探测做有界退避重试（不扫描世界）。绝不每帧重染。
///
/// 回执账本（≤<see cref="Capacity"/> 条，满时不染该箭；绝不驱逐已有回执、绝不销毁任何箭）：
/// 身份 = GO InstanceID + GO 指针 + Arrow 指针 + Renderer 指针（IL2CPP 下 C# wrapper 每帧可能是不同对象，
/// **绝不**用 ReferenceEquals 认人）。记录 base sprite/color、written sprite/color、world/layer/scene、
/// 来源身份、shot 世代与复核状态。归还**逐属性 CAS**：只有当前值仍等于本模块写入值才写回 base；
/// 第三方改过的属性绝不覆盖，只放弃所有权。读/写异常 = 保留回执 + <see cref="RetrySeconds"/> 秒退避
/// （绝不静默丢未完成的归还责任，也绝不因一次失败就丢账）；确证换对象（指针/InstanceID 不符）或
/// renderer/箭已销毁才退休。外观在箭退役前可以一直保持（射手死亡/退役都不撤），但功能关、世界失效或
/// 变更、箭被回收时必须归还。所有入口异常隔离，绝不外抛进原生调用链。
///
/// 穿墙账本与回执同生共死：英雄分支给箭挂上的墙碰撞责任（<see cref="HeroArcherWallPierce.PierceLedger"/>）
/// 也记在这条回执里，**只有外观归还完成 且 碰撞账本清空，回执才退休**——owner 否决、功能关闭、世界换代、
/// 池复用、Clear、巡检任一归还路径都不会因为外观已还原/renderer 已销毁/某次写失败而丢掉仍活动的物理 pair。
/// 同一支箭（GO 身份或 Arrow 指针身份）同一时刻只允许一条回执：旧责任未结清时，新 renderer/新生命被
/// **封锁**（保持原生外观 + 原生碰撞，fail-closed），结清之后下一次 OnSpawn 才允许接管；因此旧账绝不会
/// 跨生命被复用或被换成新的归还目标。
/// </summary>
internal static class HeroArcherArrowVisuals
{
    // ---------- 容量与常量（全部硬上限，压力下退化为「不染」而不是驱逐/销毁） ----------

    /// <summary>自有回执硬上限：满时不染该箭（不删箭、不挪箭、不驱逐已有回执）。</summary>
    internal const int Capacity = 128;
    /// <summary>发射作用域最大深度：第 17 层起掩蔽；Depth 仍对称自增，finalizer 出栈后外层立即恢复可见。</summary>
    internal const int MaxStack = 16;

    /// <summary>embedded 资源名（operator 在 csproj 里以 LogicalName 注入）。</summary>
    private const string ResourceName = "KingdomEnhancedMod.ArtemisArrow.png";
    /// <summary>原生 artemis_bow_arrow 的像素尺寸（30x5）。</summary>
    private const int NativePixelWidth = 30;
    private const int NativePixelHeight = 5;
    /// <summary>原生 PPU（PNG 必须与原生同尺寸，不做缩放折算）。</summary>
    private const float NativePixelsPerUnit = 32f;
    private const float PivotX = 0.5f;
    private const float PivotY = 0.5f;

    /// <summary>原生 ArtemisArrow SpriteRenderer(79915) 的 _Highlight：视觉金色（保持原 alpha）。</summary>
    private const float GoldR = 0.99215686f;
    private const float GoldG = 0.90196079f;
    private const float GoldB = 0.44705883f;
    /// <summary>颜色 CAS 容差（只用于「仍是本模块写入值 / 已是 base」判定，远小于任何有意改色）。</summary>
    private const float ColorEpsilon = 1e-3f;
    /// <summary>读/写失败后的重试间隔（unscaled 秒），避免每帧无退避重试。</summary>
    private const float RetrySeconds = 0.5f;

    /// <summary>
    /// 英雄金箭的显示缩放（相对原生完整尺寸的比例；1 = 原生 30px/PPU32 尺寸）。用户 2026-09-17 锁定 0.65。
    /// 只作用显示路径：自建 Sprite 的 PPU = <see cref="NativePixelsPerUnit"/> / 本值（32/0.65 ≈ 49.23，
    /// 世界尺寸 0.609375）；不动 transform/collider/弹道/伤害，池复用换回原生 sprite 时天然还原（无乘叠）。
    /// </summary>
    internal const float HeroArrowDisplayScale = 0.65f;

    // ---------- 状态 ----------

    /// <summary>一条上色回执：native 身份 + base/written 外观 + 世界上下文 + 复核/归还状态。</summary>
    private sealed class Receipt
    {
        internal IntPtr GoPtr;
        internal int GoId;
        internal IntPtr ArrowPtr;
        internal IntPtr RendererPtr;
        internal Arrow Arrow;                   // 有界强引用（≤Capacity）
        internal SpriteRenderer Renderer;
        internal HeroArcherWallPierce.PierceLedger Pierce;   // 本生命接管的墙碰撞账本（与外观同生共死）
        internal IntPtr WorldPtr;
        internal IntPtr LayerPtr;
        internal int SceneHandle;
        internal Sprite BaseSprite;             // 可能是 null（原生允许无 sprite）
        internal Color BaseColor;
        internal Sprite WrittenSprite;          // 共享金箭 sprite（全场仅一份）
        internal IntPtr WrittenSpritePtr;
        internal Color WrittenColor;
        internal IntPtr SourcePtr;              // 本次射手的 GO 身份（不可变，仅用于一次归属复核）
        internal int SourceGoId;
        internal long Epoch;                    // 所属 shot 世代
        internal bool Confirmed;                // arrow.archer == 来源 已复核
        internal bool PendingRestore;           // true = 必须先归还，此后绝不重染
        internal float NextRetry;
    }

    /// <summary>作用域栈项：Eligible=false 是掩蔽项（非英雄/关闸），只占位不授权。</summary>
    private readonly struct Scope
    {
        internal readonly long Serial;
        internal readonly bool Eligible;
        internal readonly IntPtr SourcePtr;
        internal readonly int SourceGoId;
        internal readonly IntPtr WorldPtr;
        internal readonly IntPtr LayerPtr;
        internal readonly int SceneHandle;

        internal Scope(long serial, bool eligible, IntPtr sourcePtr, int sourceGoId,
            IntPtr worldPtr, IntPtr layerPtr, int sceneHandle)
        {
            Serial = serial;
            Eligible = eligible;
            SourcePtr = sourcePtr;
            SourceGoId = sourceGoId;
            WorldPtr = worldPtr;
            LayerPtr = layerPtr;
            SceneHandle = sceneHandle;
        }
    }

    /// <summary>
    /// 一次 FireArrowInternal 调用的作用域凭据（Finalizer 原样回传）。
    /// <see cref="Serial"/> 是本次调用的**唯一世代**（掩蔽项也有，非零），<see cref="Index"/> 是压入时的
    /// 真实栈下标（-1 = 溢出层，不记录数据），<see cref="PrevSerial"/> 是压入前栈顶的世代——
    /// 出栈时精确还原栈顶，溢出层不需要无界栈。凭据只在「它仍是当前栈顶」时生效。
    /// </summary>
    internal readonly struct ShotToken
    {
        internal readonly bool Valid;
        internal readonly int Index;
        internal readonly long Serial;
        internal readonly long PrevSerial;

        internal ShotToken(bool valid, int index, long serial, long prevSerial)
        {
            Valid = valid;
            Index = index;
            Serial = serial;
            PrevSerial = prevSerial;
        }
    }

    private enum SpriteState
    {
        Unknown,
        Ready,
        Unavailable,
    }

    private static readonly Receipt[] Slots = new Receipt[Capacity];
    private static readonly Scope[] ShotStack = new Scope[MaxStack];
    private static readonly HashSet<string> Logged = new HashSet<string>();
    private static int _depth;
    private static long _serialSeq;
    private static long _topSerial;

    private static SpriteState _spriteState = SpriteState.Unknown;
    private static Texture2D _texture;
    private static Sprite _sprite;
    private static IntPtr _spritePtr;

    // ---------- 只读观测（测试/诊断） ----------

    /// <summary>当前在案回执数（含待归还），测试与日志用。</summary>
    internal static int TrackedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++) if (Slots[i] != null) count++;
            return count;
        }
    }

    /// <summary>当前作用域深度（&gt;0 = 正在原生发射调用内），测试与诊断用。</summary>
    internal static int ScopeDepth => _depth;

    // ============================================================
    // 一、发射作用域（FireArrowInternal Prefix/Finalizer）
    // ============================================================

    /// <summary>
    /// 开始一次 shot 作用域。资格 = 开关/世界/离线门 + 该射手确实是英雄（HeroArcherRuntime.IsHero）。
    /// 不合格、掩蔽或溢出一律压掩蔽项（绝不继承外层英雄资格）。探测异常一律按「不合格」处理，
    /// 绝不让异常进入原生调用链。
    /// </summary>
    internal static ShotToken BeginShot(GameObject source)
    {
        bool hero = false;
        IntPtr sourcePtr = IntPtr.Zero;
        int sourceGoId = 0;
        IntPtr world = IntPtr.Zero;
        IntPtr layer = IntPtr.Zero;
        int scene = 0;
        try
        {
            hero = IsHeroShot(source, out sourcePtr, out sourceGoId, out world, out layer, out scene);
        }
        catch (Exception e)
        {
            Fail("begin", "shot scope probe failed; arrows keep native appearance this shot: " + e);
            hero = false;
        }

        long serial = ++_serialSeq;                                  // 每次调用（含掩蔽项）唯一
        long prevSerial = _topSerial;
        int index = _depth;                                          // 真实栈下标（含溢出层）
        if (index < MaxStack)
        {
            ShotStack[index] = hero
                ? new Scope(serial, true, sourcePtr, sourceGoId, world, layer, scene)
                : new Scope(serial, false, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, 0);
        }
        else
        {
            Fail("overflow", "shot scope deeper than " + MaxStack + "; nested shots masked until the stack unwinds");
        }
        _depth++;
        _topSerial = serial;
        return new ShotToken(true, index, serial, prevSerial);
    }

    /// <summary>
    /// 结束一次 shot 作用域：**先验栈顶再出栈**（凭据必须是当前栈顶 —— 陈旧/乱序/重复的 finalizer 完全
    /// 不动栈，绝不弹掉后来的作用域）。通过后才出栈（外层立即恢复可见），并对本次 shot 的回执做归属复核。
    /// 异常一律隔离。
    /// </summary>
    internal static void EndShot(ShotToken token)
    {
        Scope scope = default;
        bool eligible = false;
        try
        {
            if (!token.Valid) return;                                 // 未压栈
            if (token.Serial != _topSerial) return;                    // 不是当前栈顶：陈旧/乱序/重复
            if (token.Index != _depth - 1) return;                     // 真实深度复核（防御）
            _topSerial = token.PrevSerial;
            _depth--;
            if (token.Index < MaxStack)                                // 记录项才需要清数据
            {
                Scope top = ShotStack[token.Index];
                ShotStack[token.Index] = default;
                if (top.Serial == token.Serial)                        // 记录项确属本次调用才可用
                {
                    scope = top;
                    eligible = top.Eligible;
                }
            }
        }
        catch (Exception e)
        {
            Fail("end", "shot scope finalizer failed: " + e);
            return;
        }

        if (!eligible) return;
        try
        {
            for (int i = 0; i < Capacity; i++)
            {
                Receipt receipt = Slots[i];
                if (receipt == null || receipt.Confirmed || receipt.Epoch != scope.Serial) continue;
                TryConfirm(i, scope.Serial);
            }
        }
        catch (Exception e) { Fail("confirm", "shot ownership recheck failed: " + e); }
    }

    /// <summary>
    /// 本 shot 的射手资格：开关 + 世界范围 + 离线门（HeroArcherRuntime.Enabled 内已含三者）→
    /// 当前 world 上下文可读 → source 有可信身份 → source 上有 Archer 且该 Archer 当前是英雄。
    /// 任何一步不确定都返回 false（fail-closed：保持原生外观）。
    /// </summary>
    private static bool IsHeroShot(GameObject source, out IntPtr sourcePtr, out int sourceGoId,
        out IntPtr worldPtr, out IntPtr layerPtr, out int sceneHandle)
    {
        sourcePtr = IntPtr.Zero;
        sourceGoId = 0;
        worldPtr = IntPtr.Zero;
        layerPtr = IntPtr.Zero;
        sceneHandle = 0;
        if (source == null) return false;
        if (!HeroArcherRuntime.Enabled) return false;                 // 配置 + world 范围 + 离线门
        if (!ArcherOptionsScope.TryGetContext(out IntPtr world, out IntPtr layer, out int scene)) return false;
        if (world == IntPtr.Zero || layer == IntPtr.Zero) return false;
        if (!TryGoIdentity(source, out sourcePtr, out sourceGoId)) return false;
        if (!source.TryGetComponent<Archer>(out Archer archer)) return false;
        if (archer == null || archer.gameObject == null) return false;
        if (!HeroArcherRuntime.IsHero(archer)) return false;
        worldPtr = world;
        layerPtr = layer;
        sceneHandle = scene;
        return true;
    }

    /// <summary>当前可见作用域（栈顶且授权、未溢出）。</summary>
    private static bool CurrentScope(out Scope scope)
    {
        scope = default;
        if (_depth <= 0) return false;
        if (_depth > MaxStack) return false;                          // 溢出：一律掩蔽
        Scope top = ShotStack[_depth - 1];
        if (!top.Eligible) return false;                              // 掩蔽项
        scope = top;
        return true;
    }

    // ============================================================
    // 二、上色 / 归还（Arrow.OnEnable 前缀与后缀）
    // ============================================================

    /// <summary>
    /// Arrow.OnEnable 前缀：池复用/新生命，先把本模块的外观责任归还（**不受功能开关限制**），
    /// 失败也保留回执由 Tick 重试。身份读失败时退回按 arrow 指针匹配，绝不因此丢账。
    /// 此时绝不读 arrow.archer（原生还没写它，字段里可能是上一任 owner）。
    /// </summary>
    internal static void ResetArrow(Arrow arrow)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return;
            // 穿墙归还先于一切（也先于回执循环）：池复用/新生命必须在原生 OnEnable 之前恢复墙碰撞，
            // 且不受功能开关限制（关闭期间发生的复用同样要清干净）。这里只是按箭身份的兜底；
            // 权威归还走下面的回执缝合点（外观 + 账本一起结算），单点失败不阻塞回执归还。
            try { HeroArcherWallPierce.Restore(arrow); }
            catch (Exception e) { Fail("reset-pierce", "wall pierce restore failed: " + e); }
            IntPtr arrowPtr;
            try { arrowPtr = arrow.Pointer; }
            catch (Exception) { return; }

            bool haveGo = TryGoIdentity(arrow.gameObject, out IntPtr goPtr, out int goId);
            for (int i = 0; i < Capacity; i++)
            {
                Receipt receipt = Slots[i];
                if (receipt == null) continue;
                bool match = haveGo
                    ? receipt.GoId == goId && receipt.GoPtr == goPtr
                    : arrowPtr != IntPtr.Zero && receipt.ArrowPtr == arrowPtr;
                if (!match) continue;
                receipt.PendingRestore = true;                        // 新生命之前必须先归还；此后只恢复不重染
                try { TryRestore(i); }
                catch (Exception e) { Keep(i); Fail("reset-restore", "arrow reset restore failed: " + e); }
            }
        }
        catch (Exception e) { Fail("reset", "arrow reset failed: " + e); }
    }

    /// <summary>
    /// Arrow.OnEnable 后缀（Priority.Last）：只有在英雄 shot 作用域内才上色，其余一律保持原生外观。
    /// 资格来自作用域，不读 arrow.archer；同一身份已有回执时幂等（绝不重复写、绝不二次接管）；
    /// 同一支箭的旧责任未结清（外观待归还 / 穿墙账本未清）时本次一律封锁：不建第二条回执、不上色、
    /// 不挂穿墙（fail-closed），结算完成后的下一次 OnSpawn 才允许接管。
    /// </summary>
    internal static void OnSpawn(Arrow arrow)
    {
        try
        {
            if (arrow == null || arrow.gameObject == null) return;
            if (!CurrentScope(out Scope scope)) return;               // 非英雄 shot：原生外观
            IntPtr arrowPtr;
            try { arrowPtr = arrow.Pointer; }
            catch (Exception) { return; }
            if (arrowPtr == IntPtr.Zero) return;
            if (!TryGoIdentity(arrow.gameObject, out IntPtr goPtr, out int goId)) return;

            SpriteRenderer renderer = ResolveRenderer(arrow);
            if (renderer == null || renderer.gameObject == null) return;
            IntPtr rendererPtr;
            try { rendererPtr = renderer.Pointer; }
            catch (Exception) { return; }
            if (rendererPtr == IntPtr.Zero) return;
            // 只认「arrow 自身 GO 上的 renderer」：回执的身份/归还复核都按同一个 GO 记账，
            // 子物体 renderer 会让归属复核失去依据 —— 宁可不上色，也绝不写无法归位的对象。
            if (!SameGoIdentity(renderer.gameObject, goPtr, goId))
            {
                Fail("child-renderer", "arrow sprite renderer is not on the arrow GameObject; arrow keeps native appearance");
                return;
            }

            int slot = FindExact(goPtr, goId, arrowPtr, rendererPtr);
            if (slot >= 0)
            {
                Receipt existing = Slots[slot];
                if (existing.PendingRestore) { TryRestore(slot); return; }   // 归还中：不重染（不叠第二份责任）
                if (existing.Epoch != scope.Serial)
                {
                    // 同一支箭在后续 shot 里再次 OnEnable（正常路径已被前缀归还）：跟随新世代重新复核。
                    existing.Epoch = scope.Serial;
                    existing.SourcePtr = scope.SourcePtr;
                    existing.SourceGoId = scope.SourceGoId;
                    existing.Confirmed = false;
                }
                return;                                              // 已上色：幂等，不重复写
            }
            int other = FindOtherReceiptSlot(goPtr, goId, arrowPtr);
            if (other >= 0)
            {
                Receipt previous = Slots[other];
                if (IsUnsettled(previous))
                {
                    // 旧责任未结清（外观待归还 / 穿墙账本未清）：这里再试一次结清，然后**封锁**本次接管——
                    // 新 renderer/新生命既不能接管，也不能替旧账换归还目标；本生命保持原生外观 + 原生碰撞
                    // （fail-closed）。结算完成后，下一次 OnSpawn 才会建新回执。
                    previous.PendingRestore = true;
                    try { TryRestore(other); }
                    catch (Exception e) { Keep(other); Fail("spawn-settle", "settling the earlier receipt failed: " + e); }
                    Fail("blocked", "an earlier receipt for this arrow is still unsettled; this life keeps native appearance and collisions");
                    return;
                }
                // 已结清但 renderer/arrow 身份已变：同一支箭只允许一条回执，绝不认领第二条。
                Fail("conflict", "same arrow already owned by another receipt; arrow keeps native appearance");
                return;
            }
            if (!EnsureSprite()) return;                             // 资源缺失/非法/未就绪：整块 fail-closed
            int free = Allocate();
            if (free < 0)
            {
                Fail("cap", "arrow receipt table full (" + Capacity + "); arrow keeps native appearance (no arrow moved)");
                return;
            }

            Sprite baseSprite;
            Color baseColor;
            try { baseSprite = renderer.sprite; baseColor = renderer.color; }
            catch (Exception e) { Fail("read", "arrow renderer read failed; arrow keeps native appearance: " + e); return; }

            Receipt receipt = new Receipt
            {
                GoPtr = goPtr,
                GoId = goId,
                ArrowPtr = arrowPtr,
                RendererPtr = rendererPtr,
                Arrow = arrow,
                Renderer = renderer,
                WorldPtr = scope.WorldPtr,
                LayerPtr = scope.LayerPtr,
                SceneHandle = scope.SceneHandle,
                BaseSprite = baseSprite,
                BaseColor = baseColor,
                WrittenSprite = _sprite,
                WrittenSpritePtr = _spritePtr,
                WrittenColor = new Color(GoldR, GoldG, GoldB, baseColor.a),
                SourcePtr = scope.SourcePtr,
                SourceGoId = scope.SourceGoId,
                Epoch = scope.Serial,
                Confirmed = false,
                PendingRestore = true,                                // 写色确认前一律待归还
                NextRetry = 0f,
            };
            Slots[free] = receipt;                                    // 先登记责任，再写外观
            if (!WriteAppearance(free)) return;                        // 半写失败：回执保留为待归还，Tick 重试
            receipt.PendingRestore = false;
            // 外观写入成功才给这支英雄箭挂穿墙（资格门就是本次 shot 作用域）；返回的账本记进回执，
            // 归还与退休都由回执缝合点负责。失败只记日志，绝不影响外观、绝不外抛进原生链路。
            try
            {
                HeroArcherWallPierce.PierceLedger pierce =
                    HeroArcherWallPierce.Apply(arrow, receipt.GoPtr, receipt.GoId, receipt.ArrowPtr);
                if (pierce != null) receipt.Pierce = pierce;
            }
            catch (Exception e) { Fail("spawn-pierce", "wall pierce apply failed: " + e); }
        }
        catch (Exception e) { Fail("spawn", "arrow appearance apply failed: " + e); }
    }

    /// <summary>写上色（sprite → color）。任何一步失败都保留回执为待归还，绝不半途丢掉责任。</summary>
    private static bool WriteAppearance(int slot)
    {
        Receipt receipt = Slots[slot];
        try { receipt.Renderer.sprite = receipt.WrittenSprite; }
        catch (Exception e) { Keep(slot); Fail("write-sprite", "arrow sprite write failed (kept for retry): " + e); return false; }
        try { receipt.Renderer.color = receipt.WrittenColor; }
        catch (Exception e) { Keep(slot); Fail("write-color", "arrow color write failed (kept for retry): " + e); return false; }
        return true;
    }

    // ============================================================
    // 三、巡检（operator 接 ModPanel.Update；只扫自有 ≤128 条）
    // ============================================================

    /// <summary>
    /// 有界巡检：功能关 / 世界失效或变更 / 箭已回收 → 归还；待归还 → 退避重试；未复核 → 复核。
    /// 每条回执独立 try/catch（一条坏账不饿死其余），绝不重染、绝不全场扫描、绝不销毁任何对象。
    /// </summary>
    internal static void Tick()
    {
        float now = Now();
        bool enabled;
        try { enabled = HeroArcherRuntime.Enabled; }
        catch (Exception) { enabled = false; }
        bool hasWorld = false;
        IntPtr world = IntPtr.Zero;
        IntPtr layer = IntPtr.Zero;
        int scene = 0;
        try
        {
            hasWorld = ArcherOptionsScope.TryGetContext(out world, out layer, out scene)
                && world != IntPtr.Zero && layer != IntPtr.Zero;
        }
        catch (Exception) { hasWorld = false; }

        for (int i = 0; i < Capacity; i++)
        {
            Receipt receipt = Slots[i];
            if (receipt == null || receipt.NextRetry > now) continue;
            try
            {
                if (receipt.Renderer == null || receipt.Renderer.gameObject == null)
                {
                    // 箭/renderer 已销毁：外观侧无处可写（视为了结），但碰撞账本可能还活着——
                    // 照样过归还缝合点，绝不因为视觉侧消失就丢掉仍活动的物理 pair。
                    receipt.PendingRestore = true;
                    TryRestore(i);
                    continue;
                }
                bool despawned = false;
                try { despawned = !receipt.Renderer.gameObject.activeSelf; }
                catch (Exception) { Backoff(i); continue; }           // 读不到激活状态 = 未知：保留外观 + 退避重试
                if (despawned || !enabled || !hasWorld
                    || receipt.WorldPtr != world || receipt.LayerPtr != layer || receipt.SceneHandle != scene)
                {
                    receipt.PendingRestore = true;                    // 池回收/功能关/世界失效或变更：必须归还
                }
                if (receipt.PendingRestore) { TryRestore(i); continue; }
                if (!receipt.Confirmed) { TryConfirm(i, receipt.Epoch); continue; }
                // 已确认的在飞箭：账本里未确认的 true 写入 / 未决探测做有界退避重试（不扫描世界）。
                if (HeroArcherWallPierce.HasPendingRetry(receipt.Pierce)) HeroArcherWallPierce.RetryPending(receipt.Pierce);
            }
            catch (Exception e)
            {
                Keep(i);
                Fail("tick", "tick on a receipt failed (kept for retry): " + e);
            }
        }
    }

    /// <summary>
    /// 全量归还（功能关闭/世界切换/模组卸载，operator 调用）：逐条置待归还并尝试归还；
    /// 失败的回执**保留**（退避重试），绝不静默丢弃仍在手里的归还责任。共享 sprite/纹理保留复用。
    /// </summary>
    internal static void Clear()
    {
        for (int i = 0; i < Capacity; i++)
        {
            Receipt receipt = Slots[i];
            if (receipt == null) continue;
            receipt.PendingRestore = true;
            try { TryRestore(i); }
            catch (Exception e) { Keep(i); Fail("clear", "clear restore failed (kept for retry): " + e); }
        }
    }

    // ============================================================
    // 四、归属复核与归还会话
    // ============================================================

    /// <summary>
    /// 归属复核：这支箭的 arrow.archer 是否确实是本次射手（指针 + InstanceID）。
    /// 只在原生写完 owner 之后调用（EndShot / Tick），OnEnable 时刻绝不调用。
    /// **只有真读到匹配的 owner 才置 Confirmed**；读异常 / wrapper 已换对象 / owner 仍为 null 一律
    /// 保留未复核状态 + 退避重试（绝不因为一次读失败就永久放行，也绝不误撤）。
    /// 确证是别人（指针/InstanceID 都不符）才归还：绝不把外观留在别的射手的箭上。
    /// </summary>
    private static void TryConfirm(int slot, long epoch)
    {
        Receipt receipt = Slots[slot];
        if (receipt.Epoch != epoch) return;                               // 世代已变：由新世代处理
        if (receipt.SourcePtr == IntPtr.Zero) { Backoff(slot); return; }   // 无从核对来源：稍后再试
        Arrow arrow = receipt.Arrow;
        if (arrow == null) { Backoff(slot); return; }
        IntPtr arrowPtr;
        try { arrowPtr = arrow.Pointer; }
        catch (Exception) { Backoff(slot); return; }
        if (arrowPtr != receipt.ArrowPtr) { Backoff(slot); return; }        // wrapper 已指向别的对象
        GameObject owner;
        try { owner = arrow.archer; }
        catch (Exception) { Backoff(slot); return; }                        // owner 读异常 = 未知：稍后再核
        if (owner == null) { Backoff(slot); return; }                       // 原生还没写 owner：稍后再看
        IntPtr ownerPtr;
        int ownerGoId;
        if (!TryGoIdentity(owner, out ownerPtr, out ownerGoId)) { Backoff(slot); return; }
        if (ownerPtr == receipt.SourcePtr && ownerGoId == receipt.SourceGoId)
        {
            receipt.Confirmed = true;                                       // 只有实测相符才确认
            return;
        }
        receipt.PendingRestore = true;                                      // 确证不是本次射手的箭：不是我们的外观
        TryRestore(slot);
    }

    /// <summary>未决回执的退避：不动所有权，只推迟下次核对（读失败/未写入一律走这里）。</summary>
    private static void Backoff(int slot)
    {
        Receipt receipt = Slots[slot];
        if (receipt == null) return;
        receipt.NextRetry = Now() + RetrySeconds;
    }

    /// <summary>
    /// 归还缝合点（功能关闭/世界切换/owner 否决/池复用/巡检/Clear/卸载都汇到这里）：外观与穿墙账本
    /// **一起结算**——外观归还完成 **且** 碰撞账本清空，回执才退休；否则保留回执 + 退避重试。
    /// 绝不因为外观已还原、renderer 已销毁或某次写失败就丢掉仍活动的物理 pair 责任。
    /// </summary>
    private static void TryRestore(int slot)
    {
        Receipt receipt = Slots[slot];
        if (receipt == null) return;
        bool visualsSettled = TryRestoreVisuals(receipt);
        bool pierceSettled = HeroArcherWallPierce.RestoreLedger(receipt.Pierce);
        if (visualsSettled && pierceSettled) { Slots[slot] = null; return; }
        Keep(slot);
    }

    /// <summary>
    /// 逐属性 CAS 归还：只写回执里记录的 renderer（不碰可能被替换的新 renderer，也不要求 Arrow 组件还活着）。
    /// 身份复核：仍同 renderer/GO 指针 + InstanceID 才写；identity 读异常 = 未知 → 保留回执 + 退避；
    /// 确证换对象 / renderer 已销毁 = 外观侧了结（不写新对象）。逐属性独立判定，第三方改过的那一项绝不覆盖：
    ///   * sprite：当前 sprite 的 native 指针 == 我们写入的那份才算我们的；指针读异常 = 未知 → 保留重试。
    ///   * color：本模块**只拥有 RGB**（永不染指 alpha）——当前 RGB == 金色才写回 base RGB，
    ///     并保留**当前** alpha（游戏自己的淡入淡出/闪烁照旧）；RGB 已被第三方改掉则整条颜色都不碰。
    /// 返回 true = 外观侧责任已了结（已写回 / 确证换对象 / renderer·GO 明确消失 / 两项都已不是我们的）。
    /// </summary>
    private static bool TryRestoreVisuals(Receipt receipt)
    {
        if (receipt.Renderer == null || receipt.Renderer.gameObject == null) return true;   // 无处可写：外观侧了结

        OwnerCheck owner = CheckOwner(receipt);
        if (owner == OwnerCheck.Different) return true;                       // 确证换对象：绝不写新对象
        if (owner == OwnerCheck.Unknown) return false;                        // 读异常：未知，保留回执

        Sprite currentSprite;
        Color currentColor;
        try
        {
            currentSprite = receipt.Renderer.sprite;
            currentColor = receipt.Renderer.color;
        }
        catch (Exception) { return false; }

        Ownership spriteState = SpriteOwnership(currentSprite, receipt.WrittenSpritePtr);
        if (spriteState == Ownership.Unknown) return false;                   // 指针读不到 = 未知，绝不当作别人的
        bool rgbOurs = SameRgb(currentColor, receipt.WrittenColor);
        if (spriteState == Ownership.Foreign && !rgbOurs) return true;        // 两项都已不是我们的

        if (spriteState == Ownership.Ours)
        {
            try { receipt.Renderer.sprite = receipt.BaseSprite; }
            catch (Exception) { return false; }                              // 写异常：保留 sprite 责任
        }
        if (rgbOurs)
        {
            Color restored = new Color(receipt.BaseColor.r, receipt.BaseColor.g, receipt.BaseColor.b, currentColor.a);
            try { receipt.Renderer.color = restored; }
            catch (Exception) { return false; }                              // 写异常：保留 color 责任
        }
        return true;
    }

    /// <summary>回执保留：标记待归还并退避（读/写失败一律走这里，绝不丢账、绝不绕过退避）。</summary>
    private static void Keep(int slot)
    {
        Receipt receipt = Slots[slot];
        if (receipt == null) return;
        receipt.PendingRestore = true;
        receipt.NextRetry = Now() + RetrySeconds;
    }

    // ============================================================
    // 五、共享 sprite（embedded PNG，只加载一次；失败整块 fail-closed）
    // ============================================================

    /// <summary>惰性加载 embedded 金箭 PNG；任何失败都记一次日志并永久保持原生外观（绝不半可用）。</summary>
    private static bool EnsureSprite()
    {
        if (_spriteState == SpriteState.Ready) return true;
        if (_spriteState == SpriteState.Unavailable) return false;
        _spriteState = SpriteState.Unavailable;                          // 先置失败：任何异常都保持原生外观

        Texture2D texture = null;
        try
        {
            byte[] bytes = ReadEmbeddedPng();
            if (bytes == null) return false;

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                Unavailable("ImageConversion.LoadImage returned false");
                DestroyQuietly(texture);
                return false;
            }

            int width = texture.width;
            int height = texture.height;
            if (width != NativePixelWidth || height != NativePixelHeight)
            {
                Unavailable("artemis arrow PNG size " + width + "x" + height + " != native "
                    + NativePixelWidth + "x" + NativePixelHeight);
                DestroyQuietly(texture);
                return false;
            }

            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != width * height)
            {
                Unavailable("artemis arrow PNG pixel read failed (" + (pixels == null ? 0 : pixels.Length) + ")");
                DestroyQuietly(texture);
                return false;
            }
            int opaque = 0;
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].a != 0) opaque++;
            if (opaque == 0 || opaque == pixels.Length)
            {
                Unavailable("artemis arrow PNG is not a shaped sprite (" + opaque + "/" + pixels.Length + " opaque)");
                DestroyQuietly(texture);
                return false;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            // 显示缩放（纯显示层）：只抬 PPU —— 世界尺寸 = rect / (32 / 0.65) = 原生的 65%；
            // rect/纹理/碰撞体/transform/弹道全不动，池复用换回原生 sprite 天然还原（无乘叠）。
            float displayPixelsPerUnit = NativePixelsPerUnit / HeroArrowDisplayScale;
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                new Vector2(PivotX, PivotY), displayPixelsPerUnit, 0u, SpriteMeshType.FullRect);
            if (sprite == null)
            {
                Unavailable("Sprite.Create returned null for the artemis arrow PNG");
                DestroyQuietly(texture);
                return false;
            }
            IntPtr spritePtr;
            try { spritePtr = sprite.Pointer; }
            catch (Exception) { spritePtr = IntPtr.Zero; }
            if (spritePtr == IntPtr.Zero)
            {
                Unavailable("artemis arrow sprite has no native pointer");
                DestroyQuietly(sprite);
                DestroyQuietly(texture);
                return false;
            }

            _texture = texture;
            _sprite = sprite;
            _spritePtr = spritePtr;
            _spriteState = SpriteState.Ready;
            Log("artemis arrow sprite ready: " + width + "x" + height
                + " ppu=" + displayPixelsPerUnit + " scale=" + HeroArrowDisplayScale
                + " opaque=" + opaque + "/" + pixels.Length);
            return true;
        }
        catch (Exception e)
        {
            Unavailable("artemis arrow PNG load failed: " + e.GetType().Name);
            if (texture != null) DestroyQuietly(texture);
            return false;
        }
    }

    /// <summary>读 embedded PNG（有界：4 MiB 上限、读满校验；缺失/截断/超限 → null）。</summary>
    private static byte[] ReadEmbeddedPng()
    {
        try
        {
            Assembly assembly = typeof(HeroArcherArrowVisuals).Assembly;
            using (System.IO.Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    Unavailable("embedded resource missing: " + ResourceName);
                    return null;
                }
                long length = stream.Length;
                if (length <= 0 || length > 4L * 1024L * 1024L)
                {
                    Unavailable("embedded resource size rejected: " + length + " bytes");
                    return null;
                }
                byte[] bytes = new byte[(int)length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int step = stream.Read(bytes, read, bytes.Length - read);
                    if (step <= 0) break;
                    read += step;
                }
                if (read != bytes.Length)
                {
                    Unavailable("embedded resource truncated: " + read + "/" + bytes.Length);
                    return null;
                }
                return bytes;
            }
        }
        catch (Exception e)
        {
            Unavailable("embedded resource read failed: " + e.GetType().Name);
            return null;
        }
    }

    // ============================================================
    // 六、工具
    // ============================================================

    private enum OwnerCheck
    {
        /// <summary>renderer 仍属于回执记录的原 native 对象。</summary>
        Match,
        /// <summary>确证：renderer 已销毁，或其 renderer 指针 / GO 指针 / InstanceID 与回执不符。</summary>
        Different,
        /// <summary>身份读取抛异常：未知（既不算换对象也不算销毁）。</summary>
        Unknown,
    }

    /// <summary>单项属性（sprite）的所有权三态：未知绝不当作已换人。</summary>
    private enum Ownership
    {
        Ours,
        Foreign,
        Unknown,
    }

    /// <summary>每次写外观前复核回执原身份（renderer 指针 → 其 GO 指针 + GO InstanceID）。</summary>
    private static OwnerCheck CheckOwner(Receipt receipt)
    {
        if (receipt.Renderer == null || receipt.Renderer.gameObject == null) return OwnerCheck.Different;
        try
        {
            if (receipt.Renderer.Pointer != receipt.RendererPtr) return OwnerCheck.Different;
            if (receipt.Renderer.gameObject.Pointer != receipt.GoPtr) return OwnerCheck.Different;
            if (receipt.Renderer.gameObject.GetInstanceID() != receipt.GoId) return OwnerCheck.Different;
            return OwnerCheck.Match;
        }
        catch (Exception) { return OwnerCheck.Unknown; }
    }

    /// <summary>四元身份精确匹配（GO 指针 + InstanceID + arrow 指针 + renderer 指针）。</summary>
    private static int FindExact(IntPtr goPtr, int goId, IntPtr arrowPtr, IntPtr rendererPtr)
    {
        for (int i = 0; i < Capacity; i++)
        {
            Receipt receipt = Slots[i];
            if (receipt != null && receipt.GoPtr == goPtr && receipt.GoId == goId
                && receipt.ArrowPtr == arrowPtr && receipt.RendererPtr == rendererPtr) return i;
        }
        return -1;
    }

    /// <summary>
    /// 同一支箭（GO 身份或 Arrow 指针身份）名下已有的**另一条**回执下标（调用方已排除完全同身份的那条）；
    /// 无 = -1。一条回执 = 一条生命：旧责任未结清前绝不允许第二条接管（renderer 换掉也不能绕过）。
    /// </summary>
    private static int FindOtherReceiptSlot(IntPtr goPtr, int goId, IntPtr arrowPtr)
    {
        for (int i = 0; i < Capacity; i++)
        {
            Receipt receipt = Slots[i];
            if (receipt == null) continue;
            if (receipt.GoPtr == goPtr && receipt.GoId == goId) return i;
            if (arrowPtr != IntPtr.Zero && receipt.ArrowPtr == arrowPtr) return i;
        }
        return -1;
    }

    /// <summary>回执是否还有未结清责任：外观待归还，或穿墙账本里仍有未归还/未确认/待重探的 pair。</summary>
    private static bool IsUnsettled(Receipt receipt)
    {
        return receipt.PendingRestore || HeroArcherWallPierce.HasUnsettled(receipt.Pierce);
    }

    /// <summary>取空闲槽位；满时返回 -1（绝不驱逐已有回执、绝不动任何箭）。</summary>
    private static int Allocate()
    {
        for (int i = 0; i < Capacity; i++) if (Slots[i] == null) return i;
        return -1;
    }

    private static SpriteRenderer ResolveRenderer(Arrow arrow)
    {
        try
        {
            SpriteRenderer renderer = arrow._spriteRenderer;
            if (renderer != null && renderer.gameObject != null) return renderer;
        }
        catch (Exception) { }
        try
        {
            GameObject go = arrow.gameObject;
            return go != null ? go.GetComponent<SpriteRenderer>() : null;
        }
        catch (Exception) { return null; }
    }

    private static bool TryGoIdentity(GameObject gameObject, out IntPtr goPtr, out int goId)
    {
        goPtr = IntPtr.Zero;
        goId = 0;
        if (gameObject == null) return false;
        try
        {
            goPtr = gameObject.Pointer;
            goId = gameObject.GetInstanceID();
            return goPtr != IntPtr.Zero && goId != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>该 GO 是否就是回执记账用的那个 native GO（指针 + InstanceID；读异常 = 否）。</summary>
    private static bool SameGoIdentity(GameObject gameObject, IntPtr goPtr, int goId)
    {
        try
        {
            return gameObject != null && gameObject.Pointer == goPtr && gameObject.GetInstanceID() == goId;
        }
        catch (Exception) { return false; }
    }

    /// <summary>当前 sprite 是否仍是本模块写入的那一份（native 指针比较，不用 wrapper ReferenceEquals）。</summary>
    private static Ownership SpriteOwnership(Sprite current, IntPtr writtenPtr)
    {
        if (writtenPtr == IntPtr.Zero) return Ownership.Foreign;      // 本模块没写过 sprite（不会发生）
        if (current == null) return Ownership.Foreign;                // 第三方清空/换走了 sprite
        try { return current.Pointer == writtenPtr ? Ownership.Ours : Ownership.Foreign; }
        catch (Exception) { return Ownership.Unknown; }               // 指针读异常 = 未知，绝不当作已换人
    }

    /// <summary>只比 RGB：本模块永不拥有 alpha（游戏淡入淡出/闪烁照旧由原生掌控）。</summary>
    private static bool SameRgb(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) <= ColorEpsilon
            && Mathf.Abs(a.g - b.g) <= ColorEpsilon
            && Mathf.Abs(a.b - b.b) <= ColorEpsilon;
    }

    private static float Now()
    {
        try { return Time.unscaledTime; }
        catch (Exception) { return 0f; }
    }

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        try { if (target != null) UnityEngine.Object.Destroy(target); }
        catch (Exception) { }
    }

    private static void Unavailable(string message)
    {
        Fail("unavailable", "gold arrow appearance disabled (native arrow appearance kept): " + message);
    }

    private static void Fail(string key, string message)
    {
        try
        {
            if (Logged.Add("[HeroArrowArt]" + key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogError("[HeroArrowArt] " + message);
        }
        catch (Exception) { }
    }

    

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroArrowArt] " + message); }
        catch (Exception) { }
    }

    // ---------- 测试钩子（仅 internal；不做任何游戏写入、不销毁任何对象） ----------

    /// <summary>清空全部回执与作用域，供测试用例之间隔离；共享 sprite 保留（只读缓存）。</summary>
    internal static void ResetForTests()
    {
        for (int i = 0; i < Capacity; i++) Slots[i] = null;
        for (int i = 0; i < MaxStack; i++) ShotStack[i] = default;
        _depth = 0;
        _serialSeq = 0;
        _topSerial = 0;
        Logged.Clear();
    }
}

// ============================================================
// Harmony 钩子（只用已核 native 目标：ArrowAttack.FireArrowInternal / Arrow.OnEnable，
// 不新增任何 native 地址、不钩短 getter、不使用 iterator）。异常一律隔离在钩子内。
// ============================================================

/// <summary>
/// 发射作用域：Prefix 记录本次 shot 是否英雄射的箭（嵌套安全），Finalizer 在所有 Postfix
/// （含既有散射额外箭）之后出栈并复核归属。不改写原方法执行、不吞原异常（原异常原样返回）。
/// </summary>
[HarmonyPatch(typeof(ArrowAttack), "FireArrowInternal")]
internal static class ArrowAttack_FireArrowInternal_HeroArrowArt_Patch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static void Prefix(GameObject source, out HeroArcherArrowVisuals.ShotToken __state)
    {
        __state = default;
        try { __state = HeroArcherArrowVisuals.BeginShot(source); }
        catch { }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(HeroArcherArrowVisuals.ShotToken __state, Exception __exception)
    {
        try { HeroArcherArrowVisuals.EndShot(__state); }
        catch { }
        return __exception;
    }
}

/// <summary>
/// 箭生命窗口：Prefix（Priority.First）先把池复用/新生命前的原生外观归还（不读 arrow.archer）；
/// Postfix（Priority.Last）在英雄 shot 作用域内上色，保证颜色不被其它 OnEnable 后缀覆盖。
/// </summary>
[HarmonyPatch(typeof(Arrow), "OnEnable")]
internal static class Arrow_OnEnable_HeroArrowArt_Patch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static void Prefix(Arrow __instance)
    {
        try { HeroArcherArrowVisuals.ResetArrow(__instance); }
        catch { }
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void Postfix(Arrow __instance)
    {
        try { HeroArcherArrowVisuals.OnSpawn(__instance); }
        catch { }
    }
}
