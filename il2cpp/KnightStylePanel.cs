// 骑士风格分配面板（knight-style-panel-20260922）：ModPanel 新分区「骑士」。
//
// 与身份系统的分工（硬边界，勿越权）：
//  * 功能 A（首见自动均匀分配）在 KnightIdentityRuntime.AssignFirstSeenUniform，由既有 5s IntegrityPass
//    在 PrimeExisting 之前调用；本文件只负责面板展示与重派（用户动作），绝不参与自动分配判定。
//  * 功能 B（本文件）：五行 = 风格名 + 预览小图（SetPreview 可替换；缺资产=纯色占位）+ [−]/[+] 步进器；
//    剩余池 = 本岛可分配骑士数 − 五行之和（只读）。+ 仅池>0 可点、− 仅该行>0 可点——总量守恒由按钮
//    使能物理保证，无需锁。「均分剩余」把池逐枚摊到当前最少的行；「全部重洗」开关默认关。
//    应用（用户定稿）：剩余池先全部归入希腊风格 → 按五行目标对当前岛骑士一次性重派（保留式=已确认
//    且配额容得下的随机保留、超出配额与零记录者随机填剩余配额；重洗开=全部参与随机）→ 复用既有表现
//    路径 → unresolved 上下文挂设计 C pending、已解析上下文挂用户修订 pending → 一次性日志。
//    应用按钮在门不满足时禁用并提示原因。
//  * 设计 C（用户锚定再基线化）与用户修订（已解析岛同 epoch 修订链）的写入都在运行时的 SaveBridge 内
//    （save 形态 JSON、旧记录全保留、证据缺口/失败 pending 保持不写）；本文件只在应用成功后按 binding
//    状态挂对应 pending 并如实提示玩家。修订号由写入路径从重读的盘上取「该 context 最大修订号 + 1」
//    （两次 apply 未保存只 bump 一次）。
//
// 权限与口径：
//  * 仅单机/主机 authority 可用；NetworkBigBoss.IsOnline 时整分区禁用（灰显 + 提示），不做 nonce 确认链。
//  * 本面板的骑士集合 = 当前 world 内实测归属的 tagKnight（可读写身份的对象）；「待识别」= 其中尚无
//    身份记录者。HUD 第六行统计「无已解析风格」的骑士，常见状态下两者一致；本岛存在侍从等非 tagKnight
//    的 Knight 组件对象时，面板在剩余池行脚注 HUD 骑士计数供对照（侍从不可分配，绝不进入重派）。
//  * 持久化语义与现行一切身份写入一致：运行时收据+表现即时生效；持久化走既有 Save 桥/LoadSeed flush
//    ——应用后到下次原生保存前退出=丢弃；不存在也不要求 per-knight 即时写档 API。GUID 策略：已有收据
//    保留 GUID 只改 style（档案历史留同 GUID 异 style 记录）；零记录才铸新 GUID。
//  * 骑士数在面板打开期间变化：不做实时跟随，下次打开（重进本分区）刷新；应用时按现场复核，人数不符
//    一律拒绝并提示。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 面板会话状态与纯算式（无 Unity 依赖，测试可直接驱动）：
/// 五行目标/剩余池守恒、步进器使能、均分剩余、应用前剩余归希腊、保留式/全重洗重派。
/// </summary>
internal sealed class KnightStylePanelCore
{
    internal const int StyleCount = 5;
    internal const int GreeceStyle = 3; // 顺序与 KnightStyle/PopulationHud 一致：中世纪/死地/幕府/希腊/北境

    private readonly int[] _targets = new int[StyleCount];
    private readonly int[] _confirmed = new int[StyleCount];
    private int _knights;

    internal int Knights { get { return _knights; } }

    internal int Target(int style) { return (uint)style < StyleCount ? _targets[style] : 0; }

    internal int Confirmed(int style) { return (uint)style < StyleCount ? _confirmed[style] : 0; }

    internal int Pool
    {
        get
        {
            int pool = _knights - Sum(_targets);
            return pool > 0 ? pool : 0;
        }
    }

    /// <summary>重置为一次刷新结果：knights = 本岛可分配骑士数；confirmed = 各风格已确认数（和应 ≤ knights）。</summary>
    internal void Reset(int knights, int[] confirmedByStyle)
    {
        _knights = knights < 0 ? 0 : knights;
        for (int i = 0; i < StyleCount; i++)
        {
            int value = confirmedByStyle != null && (uint)i < confirmedByStyle.Length && confirmedByStyle[i] > 0
                ? confirmedByStyle[i] : 0;
            _confirmed[i] = value;
            _targets[i] = value;
        }
        int confirmedTotal = Sum(_targets);
        if (confirmedTotal > _knights)
        {
            // 数据自相矛盾（已确认 > 总数）：只修正总数用于守恒显示；应用会因目标和不等于现场人数被门拒绝。
            _knights = confirmedTotal;
        }
    }

    internal bool CanAdd(int style) { return (uint)style < StyleCount && Pool > 0; }

    internal bool CanRemove(int style) { return (uint)style < StyleCount && _targets[style] > 0; }

    /// <summary>从池里取一枚到该行（+ 按钮；使能由 CanAdd 物理保证守恒）。</summary>
    internal bool Add(int style)
    {
        if (!CanAdd(style)) return false;
        _targets[style]++;
        return true;
    }

    /// <summary>把该行一枚退回池（− 按钮；使能由 CanRemove 保证）。</summary>
    internal bool Remove(int style)
    {
        if (!CanRemove(style)) return false;
        _targets[style]--;
        return true;
    }

    /// <summary>把剩余池逐枚摊到当前最少的行（同数取下标小者）——结果五行数量差 ≤1。</summary>
    internal void SpreadEvenly()
    {
        int guard = _knights; // 有界：每次循环池必然 -1
        while (Pool > 0 && guard-- > 0)
        {
            int best = 0;
            for (int i = 1; i < StyleCount; i++) if (_targets[i] < _targets[best]) best = i;
            _targets[best]++;
        }
    }

    /// <summary>应用目标：剩余池全部归入希腊风格（用户定稿）；folded = 本次归入数。</summary>
    internal int[] ApplyTargets(out int folded)
    {
        int[] final = new int[StyleCount];
        Array.Copy(_targets, final, StyleCount);
        folded = Pool;
        final[GreeceStyle] += folded;
        return final;
    }

    /// <summary>应用成功后把面板状态收敛为已应用目标（池=0；下次打开面板由刷新覆盖为实时值）。</summary>
    internal void SetTargets(int[] targets)
    {
        for (int i = 0; i < StyleCount; i++)
        {
            _targets[i] = targets != null && (uint)i < targets.Length && targets[i] > 0 ? targets[i] : 0;
        }
    }

    internal int[] CopyTargets()
    {
        int[] copy = new int[StyleCount];
        Array.Copy(_targets, copy, StyleCount);
        return copy;
    }

    /// <summary>
    /// 重派算式：currentStyles[i] = 第 i 名骑士当前风格（-1 = 零记录）；targets = 五风格目标（和必须等于人数）。
    /// 保留式：每个风格先按 min(现有, 配额) 随机保留，其余（超额者与零记录者）随机填剩余配额；
    /// allReroll = 全部参与随机（忽略现有）。返回每名骑士的目标风格（绝不 -1）。非法输入抛 ArgumentException。
    /// 熵源由调用方提供（私有 Guid 熵；绝不用 Unity random）：同熵源下结果确定，便于测试。
    /// </summary>
    internal static int[] Reassign(int[] currentStyles, int[] targets, bool allReroll, Func<uint> entropy)
    {
        if (currentStyles == null) throw new ArgumentNullException(nameof(currentStyles));
        if (targets == null || targets.Length != StyleCount)
            throw new ArgumentException("targets must have " + StyleCount + " entries", nameof(targets));
        if (entropy == null) throw new ArgumentNullException(nameof(entropy));

        int count = currentStyles.Length;
        int sum = 0;
        for (int i = 0; i < StyleCount; i++)
        {
            if (targets[i] < 0) throw new ArgumentException("targets must be non-negative", nameof(targets));
            sum += targets[i];
        }
        if (sum != count) throw new ArgumentException("targets must sum to the knight count", nameof(targets));

        int[] result = new int[count];
        for (int i = 0; i < count; i++) result[i] = -1;
        int[] left = new int[StyleCount];
        Array.Copy(targets, left, StyleCount);

        if (!allReroll)
        {
            List<int> holders = new List<int>();
            for (int style = 0; style < StyleCount; style++)
            {
                holders.Clear();
                for (int i = 0; i < count; i++)
                {
                    if (currentStyles[i] == style) holders.Add(i);
                }
                if (holders.Count == 0 || left[style] == 0) continue;
                int keep = holders.Count < left[style] ? holders.Count : left[style];
                Shuffle(holders, entropy);
                for (int h = 0; h < keep; h++)
                {
                    result[holders[h]] = style;
                    left[style]--;
                }
            }
        }

        List<int> free = new List<int>();
        for (int i = 0; i < count; i++) if (result[i] < 0) free.Add(i);
        List<int> slots = new List<int>(free.Count);
        for (int style = 0; style < StyleCount; style++)
        {
            for (int n = 0; n < left[style]; n++) slots.Add(style);
        }
        if (slots.Count != free.Count) throw new InvalidOperationException("panel reassignment lost slots");
        Shuffle(slots, entropy);
        for (int i = 0; i < free.Count; i++) result[free[i]] = slots[i];
        return result;
    }

    private static void Shuffle<T>(List<T> list, Func<uint> entropy)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(entropy() % (uint)(i + 1));
            T swap = list[i];
            list[i] = list[j];
            list[j] = swap;
        }
    }

    private static int Sum(int[] values)
    {
        int total = 0;
        for (int i = 0; i < values.Length; i++) total += values[i];
        return total;
    }
}

/// <summary>ModPanel「骑士」分区：五风格步进器 + 剩余池 + 重派应用（单机/主机；联机灰显）。</summary>
internal static class KnightStylePanel
{
    private const int StyleCount = KnightStylePanelCore.StyleCount;
    private const float RowHeight = 32f;
    private const float SectionHeight = 358f;
    private const float PreviewSize = 26f;

    private static readonly string[] StyleNames = { "中世纪", "死地", "幕府", "希腊", "北境" };

    // 缺预览资产时的纯色占位（SetPreview 可随时替换为真实风格帧）
    private static readonly Color[] PlaceholderColors =
    {
        new Color(0.85f, 0.72f, 0.42f), new Color(0.55f, 0.35f, 0.72f), new Color(0.80f, 0.28f, 0.28f),
        new Color(0.36f, 0.63f, 0.85f), new Color(0.62f, 0.85f, 0.92f),
    };

    private static readonly KnightStylePanelCore Core = new KnightStylePanelCore();
    private static readonly Texture2D[] Previews = new Texture2D[StyleCount];
    private static Texture2D[] _placeholders;
    private static bool _sectionVisible, _needsRefresh = true, _refreshed, _allReroll;
    private static bool _hasBinding, _contextUnresolved = true;
    private static string _blocked, _status, _contextKey;
    private static int _scanKnights, _unverified, _hudKnights = -1;

    // ---- 测试/诊断只读面（会话状态由本类拥有；写操作一律走 Refresh/Apply/SetSectionVisible）----

    internal static KnightStylePanelCore Session { get { return Core; } }
    internal static string Blocked { get { return _blocked; } }
    internal static string Status { get { return _status; } }
    internal static bool AllReroll { get { return _allReroll; } }
    internal static bool Refreshed { get { return _refreshed; } }
    internal static int ScanKnights { get { return _scanKnights; } }
    internal static int Unverified { get { return _unverified; } }
    internal static int HudKnights { get { return _hudKnights; } }
    internal static bool HasContextBinding { get { return _hasBinding; } }
    internal static bool ContextUnresolved { get { return _contextUnresolved; } }
    internal static string ContextKey { get { return _contextKey; } }

    /// <summary>
    /// 面板预览图接口（可替换）：外部（资产提取流程/测试）可为某风格挂一张约 24×24 的帧。
    /// 未挂时使用纯色占位 + 风格名——面板不依赖任何新增资产即可工作。
    /// </summary>
    internal static void SetPreview(int style, Texture2D texture)
    {
        if ((uint)style >= StyleCount) return;
        Previews[style] = texture;
    }

    // ---- 嵌入资源预览（operator 从游戏资产提取的五风格帧，LogicalName=KingdomEnhancedMod.KnightStylePreview{N}.png）----
    private static bool _embeddedPreviewsTried;

    private static void TryLoadEmbeddedPreviews()
    {
        if (_embeddedPreviewsTried) return;
        _embeddedPreviewsTried = true;
        try
        {
            var assembly = typeof(KnightStylePanel).Assembly;
            for (int style = 0; style < StyleCount; style++)
            {
                // 已被 SetPreview 挂过的（测试注入）不覆盖
                if (Previews[style] != null) continue;
                using var stream = assembly.GetManifestResourceStream(
                    "KingdomEnhancedMod.KnightStylePreview" + style + ".png");
                if (stream == null || stream.Length <= 0 || stream.Length > 65536) continue;
                using var bytes = new System.IO.MemoryStream();
                stream.CopyTo(bytes);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false))
                {
                    UnityEngine.Object.Destroy(texture);
                    continue;
                }
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                Previews[style] = texture;
            }
        }
        catch (Exception e)
        {
            KnightPanelLog.WarnOnce("preview-embed-" + e.GetType().Name,
                "embedded knight preview unavailable: " + e.GetType().Name);
        }
    }

    /// <summary>ModPanel 分区可见性通知：打开（false→true）时安排一次刷新（会话内不做实时跟随）。</summary>
    internal static void SetSectionVisible(bool visible)
    {
        if (visible && !_sectionVisible) _needsRefresh = true;
        _sectionVisible = visible;
    }

    /// <summary>仅测试：清空会话状态（不写任何文件、不碰 Unity 对象；预览与占位纹理保持）。</summary>
    internal static void ResetForTests()
    {
        Core.Reset(0, null);
        _sectionVisible = false;
        _needsRefresh = true;
        _refreshed = false;
        _allReroll = false;
        _hasBinding = false;
        _contextUnresolved = true;
        _blocked = null;
        _status = null;
        _contextKey = null;
        _scanKnights = 0;
        _unverified = 0;
        _hudKnights = -1;
        for (int i = 0; i < StyleCount; i++) Previews[i] = null;
    }

    // ------------------------------------------------------------------ 绘制

    internal static void DrawSection(ref float y, float width, GUIStyle card, GUIStyle label, GUIStyle muted,
        GUIStyle valueBox, GUIStyle button, GUIStyle activeButton)
    {
        bool online = Online();
        bool host = HostAuthority();
        if (!online && host && _needsRefresh) Refresh();
        // 联机/客机：整分区禁用（灰显 + 提示）；其余门只挡应用/均分/重洗（步进器仍可调，页脚给原因）。
        bool sectionDisabled = !host || online;
        bool ready = !sectionDisabled && _refreshed && _blocked == null;

        bool savedEnabled = GUI.enabled;
        Color savedColor = GUI.color;
        try
        {
            if (sectionDisabled)
            {
                GUI.enabled = false;
                GUI.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            }
            GUI.Box(new Rect(0f, y, width, SectionHeight), GUIContent.none, card);

            GUI.Label(new Rect(14f, y + 8f, width - 250f, 32f), "骑士风格分配", label);
            GUI.Box(new Rect(width - 224f, y + 10f, 210f, 30f),
                _refreshed ? "已确认 " + ConfirmedTotal() + " / 骑士 " + _scanKnights : "未就绪", valueBox);
            GUI.Label(new Rect(16f, y + 40f, width - 32f, 26f),
                "五行 + 剩余池 = 本岛可分配骑士（tag=Knight）；+ 仅池内可取、− 仅本行有数可退。点「应用」时剩余全部归入希腊。",
                muted);

            float xMinus = width - 188f, xCount = width - 152f, xPlus = width - 82f;
            float ry = y + 72f;
            for (int style = 0; style < StyleCount; style++)
            {
                DrawPreview(style, new Rect(18f, ry + 3f, PreviewSize, PreviewSize));
                GUI.Label(new Rect(52f, ry + 2f, 150f, 26f), StyleNames[style], label);

                bool previous = GUI.enabled;
                GUI.enabled = previous && !sectionDisabled && Core.CanRemove(style);
                if (GUI.Button(new Rect(xMinus, ry + 2f, 30f, 26f), "−", button)) Core.Remove(style);
                GUI.enabled = previous;
                GUI.Box(new Rect(xCount, ry + 2f, 64f, 26f), Core.Target(style).ToString(CultureInfo.InvariantCulture), valueBox);
                GUI.enabled = previous && !sectionDisabled && Core.CanAdd(style);
                if (GUI.Button(new Rect(xPlus, ry + 2f, 30f, 26f), "+", button)) Core.Add(style);
                GUI.enabled = previous;

                ry += RowHeight;
            }

            string pool = "剩余名额  " + Core.Pool.ToString(CultureInfo.InvariantCulture);
            // 2026-09-24：HUD 骑士行已不含侍从（侍从独立行）；此脚注只解释刷新时差导致的
            // 短暂差 1（面板会话刷新 vs HUD 1 秒节奏、TryVerifyPanelKnight 未就绪的真骑士）。
            if (_hudKnights >= 0 && _hudKnights != _scanKnights)
                pool += "     HUD 骑士计数 " + _hudKnights.ToString(CultureInfo.InvariantCulture) + "（刷新时差）";
            GUI.Label(new Rect(18f, ry + 2f, width - 36f, 26f), pool, muted);
            ry += 32f;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && ready && Core.Pool > 0;
            if (GUI.Button(new Rect(18f, ry, 130f, 30f), "均分剩余", button)) Core.SpreadEvenly();
            GUI.enabled = previousEnabled && ready;
            if (GUI.Button(new Rect(156f, ry, 170f, 30f), _allReroll ? "全部重洗：开" : "全部重洗：关",
                    _allReroll ? activeButton : button)) _allReroll = !_allReroll;
            if (GUI.Button(new Rect(width - 132f, ry, 114f, 30f), "应用", activeButton)) Apply();
            GUI.enabled = previousEnabled;
            ry += 38f;

            string footer;
            if (!host) footer = "客机不可用：身份与表现由主机同步。";
            else if (online) footer = "联机模式暂不开放（单机/本地主机专用）。";
            else if (_blocked != null) footer = _blocked;
            else if (_status != null) footer = _status;
            else footer = "应用后立即生效并复用既有风格表现；写入发生在下一次原生保存（保存前退出=本会话内有效）。";
            GUI.Label(new Rect(16f, ry, width - 32f, 46f), footer, muted);

            y += SectionHeight + 12f;
        }
        finally
        {
            GUI.enabled = savedEnabled;
            GUI.color = savedColor;
        }
    }

    private static void DrawPreview(int style, Rect rect)
    {
        TryLoadEmbeddedPreviews();
        Texture2D texture = Previews[style];
        if (texture == null) texture = Placeholder(style);
        ImGuiCompat.DrawTexture(rect, texture);
    }

    private static Texture2D Placeholder(int style)
    {
        if (_placeholders == null) _placeholders = new Texture2D[StyleCount];
        Texture2D texture = _placeholders[style];
        if (texture != null) return texture;
        try
        {
            texture = new Texture2D(1, 1);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, PlaceholderColors[style]);
            texture.Apply();
            _placeholders[style] = texture;
            return texture;
        }
        catch (Exception e)
        {
            KnightPanelLog.WarnOnce("placeholder-" + e.GetType().Name, "placeholder preview unavailable: " + e.GetType().Name);
            return null;
        }
    }

    // ------------------------------------------------------------------ 刷新与门槛

    /// <summary>
    /// 面板打开/切到本分区时刷新一次（会话内不做实时跟随）：扫描当前 world 的 tagKnight，逐名验证
    /// 可读写身份后统计「已确认各风格数」，剩余池 = 总数 − 已确认。无骑士/有未就绪骑士/读失败时
    /// 禁用应用并给出原因（绝不半改）。
    /// </summary>
    internal static void Refresh()
    {
        _needsRefresh = false;
        _refreshed = false;
        _blocked = null;
        _status = null;
        _scanKnights = 0;
        _unverified = 0;
        _hudKnights = -1;
        _contextKey = null;
        _hasBinding = false;
        _contextUnresolved = true;
        try
        {
            if (!HostAuthority()) { _blocked = "客机不可用：等待主机同步的身份与表现。"; return; }
            if (Online()) { _blocked = "联机模式暂不开放（单机/本地主机专用）。"; return; }
            if (!PatchRoles_KnightStyle.HasStylePool()) { _blocked = "风格资源未就绪，稍后重开面板。"; return; }

            Knight[] knights = UnitScanCache.GetKnights(0f);
            if (knights == null) { _blocked = "骑士名单未就绪，稍后重开面板。"; return; }

            int[] confirmed = new int[StyleCount];
            int total = 0, unverified = 0;
            for (int i = 0; i < knights.Length; i++)
            {
                Knight knight = knights[i];
                if (!IsPanelKnight(knight)) continue;
                if (!KnightIdentityRuntime.TryVerifyPanelKnight(knight)) { unverified++; continue; }
                total++;
                if (KnightIdentityRuntime.TryGetReceipt(knight, out KnightIdentityReceipt receipt)
                    && receipt.IsValid && (uint)receipt.Style < StyleCount) confirmed[receipt.Style]++;
            }

            _scanKnights = total;
            _unverified = unverified;
            _hudKnights = SafeHudKnights();
            Core.Reset(total, confirmed);
            ReadContext(out _contextKey, out _hasBinding, out _contextUnresolved);

            if (total == 0) _blocked = "当前岛没有可分配的骑士。";
            else if (unverified > 0) _blocked = "有 " + unverified.ToString(CultureInfo.InvariantCulture) + " 名骑士尚未就绪，稍后重开面板。";
            // Mac 静态复核收口（PR #28）：无身份绑定上下文时 apply 后无法挂 pending（rebaseline/revision
            // 都要求 applyContextKey != null && applyHasBinding），UI 却暗示可持久——明确禁用+原因提示。
            else if (!_hasBinding) _blocked = "身份上下文未就绪（无绑定），稍后重开面板。";
            else _refreshed = true;
        }
        catch (Exception e)
        {
            _blocked = "读取失败：" + e.GetType().Name;
            KnightPanelLog.WarnOnce("refresh-" + e.GetType().Name, "panel refresh failed: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// 应用（用户定稿语义）：剩余池先全部归入希腊风格 → 按五行目标重派当前岛骑士（保留式/全重洗）
    /// → 复用既有表现路径 → unresolved 上下文挂设计 C pending → 一次性日志。
    /// 门不满足（名单变化/未就绪/人数不符/容量已满）一律不应用并提示，绝不半改。
    /// </summary>
    internal static void Apply()
    {
        try
        {
            _status = null;
            if (!_refreshed || _blocked != null) return;

            Knight[] knights = UnitScanCache.GetKnights(0f);
            List<Knight> live = new List<Knight>();
            int unverified = 0;
            if (knights != null)
            {
                for (int i = 0; i < knights.Length; i++)
                {
                    Knight knight = knights[i];
                    if (!IsPanelKnight(knight)) continue;
                    if (!KnightIdentityRuntime.TryVerifyPanelKnight(knight)) { unverified++; continue; }
                    live.Add(knight);
                }
            }
            if (unverified > 0 || live.Count != _scanKnights)
            {
                _status = "名单已变化（" + live.Count.ToString(CultureInfo.InvariantCulture) + "/"
                    + _scanKnights.ToString(CultureInfo.InvariantCulture) + "），请关闭重开面板刷新后再应用。";
                return;
            }
            if (live.Count == 0) { _status = "当前岛没有可分配的骑士。"; return; }

            int[] current = new int[live.Count];
            for (int i = 0; i < live.Count; i++)
            {
                current[i] = KnightIdentityRuntime.TryGetReceipt(live[i], out KnightIdentityReceipt receipt)
                    && receipt.IsValid && (uint)receipt.Style < StyleCount ? receipt.Style : -1;
            }

            int[] targets = Core.ApplyTargets(out int folded);
            int[] styles;
            try
            {
                styles = KnightStylePanelCore.Reassign(current, targets, _allReroll, KnightIdentityRuntime.NextPanelEntropy);
            }
            catch (Exception e)
            {
                _status = "分配计算失败，未应用。";
                KnightPanelLog.WarnOnce("assign-" + e.GetType().Name, "panel reassignment rejected: " + e.GetType().Name);
                return;
            }

            int[] before = new int[StyleCount];
            for (int i = 0; i < StyleCount; i++) before[i] = Core.Confirmed(i);

            if (!KnightIdentityRuntime.TryPanelAssignAll(live.ToArray(), styles, out int assigned) || assigned != live.Count)
            {
                _status = "分配未应用（骑士状态已变化或容量已满），请稍后重试。";
                return;
            }
            for (int i = 0; i < live.Count; i++) PatchRoles_KnightStyle.ApplyPanelRestyle(live[i]);

            bool armed = false;
            bool armedRevision = false;
            ReadContext(out string applyContextKey, out bool applyHasBinding, out bool applyUnresolved);
            if (applyContextKey != null && applyHasBinding)
            {
                if (applyUnresolved)
                {
                    // 设计 C：unresolved 上下文（legacy-pending/失配/冲突）的写入必须等下一次原生 Save
                    // 的捕获作用域（apply 时点的装载形态 JSON 是死 hash）。binding 现场重读，不用刷新时的旧值。
                    KnightIdentityRuntime.ArmPanelRebaseline(applyContextKey);
                    armed = KnightIdentityRuntime.PanelRebaselineArmed;
                }
                else
                {
                    // 已解析岛：用户重派只改 MOD 收据、原生 JSON 不变——同 hash 无修订号会被既有
                    // RejectedConflict 拒写。挂修订 pending，由下一次原生 Save 写成「修订号 +1」的新快照
                    //（旧记录保留；两次 apply 未保存只 bump 一次，见 AppendSnapshot）。
                    KnightIdentityRuntime.ArmPanelRevision(applyContextKey);
                    armedRevision = KnightIdentityRuntime.PanelRevisionArmed;
                }
            }

            KnightPanelLog.Info("apply: knights=" + assigned.ToString(CultureInfo.InvariantCulture)
                + " reroll=" + (_allReroll ? "on" : "off")
                + " before=" + Counts(before)
                + " after=" + Counts(targets)
                + " pool->greece=" + folded.ToString(CultureInfo.InvariantCulture)
                + " rebaseline=" + (armed ? "armed" : "none")
                + " revision=" + (armedRevision ? "armed" : "none"));

            Core.SetTargets(targets);
            _status = armed || armedRevision
                ? "已应用 " + assigned.ToString(CultureInfo.InvariantCulture)
                    + " 名骑士：将在下一次原生保存写入档案；保存前退出则重载回原状。"
                : "已应用 " + assigned.ToString(CultureInfo.InvariantCulture) + " 名骑士：运行时立即生效，保存后持久。";
            _needsRefresh = false; // 保持已应用状态可见，直到重开面板再刷新为实时值
        }
        catch (Exception e)
        {
            _status = "应用失败：" + e.GetType().Name;
            KnightPanelLog.WarnOnce("apply-" + e.GetType().Name, "panel apply failed: " + e.GetType().Name);
        }
    }

    // ------------------------------------------------------------------ 内部

    private static bool IsPanelKnight(Knight knight)
    {
        try
        {
            if (knight == null) return false;
            GameObject go = knight.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            return go.CompareTag("Knight");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>当前岛稳定上下文与（若本会话已装载过）binding 状态；读不到上下文时 contextKey=null。</summary>
    private static void ReadContext(out string contextKey, out bool hasBinding, out bool unresolved)
    {
        contextKey = null;
        hasBinding = false;
        unresolved = true;
        try
        {
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = CampaignSaveData.current;
            IslandSaveData island = campaign != null ? campaign.CurrentIsland : null;
            if (global == null || island == null) return;
            if (!KnightIdentitySidecar.TryBuildContextKey(global.currentCampaign, global.currentChallenge, island.land, out contextKey))
            {
                contextKey = null;
                return;
            }
            if (KnightIdentityContexts.TryGetBinding(contextKey, out _, out bool boundUnresolved, out _))
            {
                hasBinding = true;
                unresolved = boundUnresolved;
            }
        }
        catch (Exception e)
        {
            contextKey = null;
            hasBinding = false;
            unresolved = true;
            KnightPanelLog.WarnOnce("context-" + e.GetType().Name, "panel context read failed: " + e.GetType().Name);
        }
    }

    private static int ConfirmedTotal()
    {
        int total = 0;
        for (int i = 0; i < StyleCount; i++) total += Core.Confirmed(i);
        return total;
    }

    private static string Counts(int[] values)
    {
        if (values == null) return "-";
        System.Text.StringBuilder builder = new System.Text.StringBuilder(16);
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append('/');
            builder.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private static int SafeHudKnights()
    {
        try { return PopulationCounts.Knights; }
        catch { return -1; }
    }

    private static bool Online()
    {
        try { return NetworkBigBoss.IsOnline; }
        catch { return true; } // 读不到联机状态：按联机处理（fail-closed，不开放面板）
    }

    private static bool HostAuthority()
    {
        try { return NetworkBigBoss.HasWorldAuth; }
        catch { return false; }
    }
}
