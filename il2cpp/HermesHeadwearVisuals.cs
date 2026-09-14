using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Hermes 头饰·视觉侧（contract: core/visual 分工里的 visual worker）。
/// 拥有：每只被选中的 FriendlyTroll 一个自有子 SpriteRenderer（纯 SpriteRenderer：无原生 mask/pool/persistent/physics/net 组件），
///       以及原生 _mask renderer 的 enabled 归属（只在"自有头饰确实显示出来"之后才隐藏它；原生 _mask 只作视图开关，
///       既不是自有 child 的父节点也不是锚点，永不重定位/销毁它）。
/// 不拥有：choice 随机/receipt/host 判定/存档/RPC/重试策略（核心 PatchDivine_HermesHeadwear 负责），
///       不写 _maskIndex/_toughTroll/health/stats/root localScale，不改 biome swap、不调用 BiomeData.GetAssetSwap。
///
/// 44 白名单（contract 固定）：0..29 = 五世界组 × index 0..5，组序 medieval(&quot;troll_masks_&quot;) / bamboo / deadlands /
/// norselands / greece；30..36 = troll_hats_party_0..6；37..43 = troll_masks_party_0..6。任何世界组的 index 6 都不出现。
/// 贴图按准确 name 从 Resources.FindObjectsOfTypeAll&lt;Sprite&gt;("") 缓存：命中零扫描；缺名或缓存条目已销毁时按全局 30 秒冷却做一次有界刷新
/// （多个缺同一资源的对象共享同一次 FindObjectsOfTypeAll）；没有可用 sprite 时绝不接管原生、也绝不改选 choice。
///
/// 挂点（root 离线资产校验结论，非游戏实测）：resources Troll_friendly 的原生 `Head` 直接子 Transform 才是头饰挂点
/// （localPosition (-.03125,.625,-.00390625)、scale 1、rot identity，且 troll_walk 等 AnimationClip 直接绑定该节点）。
/// 自有 child 一律 parent = 本 troll 的 `Head`、localPosition = (0,0,-1e-6)、localScale = one、localRotation = identity：
/// 朝向、位移、动画全部由该父链自然继承，子节点不再二次翻转，也不强写父链的 local 值。
/// 找不到 `Head` → fail closed（不建视觉、原生不动）；Head 身份变化只在 Apply 重新 Find 时检测，检测到即销毁旧 child 并 rebind
/// （原生面罩归属与 Head 无关，继续沿用）；Tick 只同步自有样式，绝不逐帧 Find、绝不场景扫描。
/// 排序：复制本体 renderer 的层/排序层/flip，order = 本体 order + 1（原生 maskPrefab 自带 order 0，直套会被本体遮挡）；
/// 材质/颜色取原生 maskPrefab（TrollMask）→ _mask → 本体。44 项 raw sprite 保留原 pivot/PPU，周年帽/面具在 Head 原 pivot
/// 直套位置正确（root 离线合成图结论），不需要额外 scale；三个 nudge 常量默认零，仅作为后续可调点保留。
///
/// API: Apply / Clear / ClearAll / Tick，外加 core 回调 `OnNativeMaskSpawned(troll)`（core 确认原生 SpawnMask 成功执行后、
/// 其 Apply 之前调用）：它是"原生刚写过 enabled"的唯一可信时刻，把同一（或换代后的）native _mask 的真实 enabled 刷成新
/// baseline（false 也算真实值），从而覆盖"原生把 renderer 设回 false（如 maskIndex=-1）"的场景；无 tracked 状态时 no-op。
/// </summary>
internal static class HermesHeadwearVisuals
{
    private const int ChoiceCount = 44;
    private const int PartyHatFirstChoice = 30;  // 0..29 世界组，30..36 party 帽
    private const int PartyMaskFirstChoice = 37;
    private const int ChoiceGroupSize = 6;
    private const string ChildObjectName = "KEM_HermesHeadwear";
    private const string HeadChildName = "Head";
    private const string PartyHatPrefix = "troll_hats_party_";
    private const string PartyMaskPrefix = "troll_masks_party_";
    private const float CatalogRetrySeconds = 30f;
    private const float CleanupRetrySeconds = 5f;
    private const float CleanupRetryMaxSeconds = 30f;

    /// <summary>自有 child 相对 Head 的固定局部位置（原生 maskPrefab 用的同一 z 微偏移，保证盖在本体之前）。</summary>
    private static readonly Vector3 ChildLocalPosition = new(0f, 0f, -1e-6f);

    /// <summary>五世界组资源前缀，组序 = contract：medieval, bamboo, deadlands, norselands, greece。</summary>
    private static readonly string[] WorldPrefixes =
    {
        "troll_masks_", "troll_masks_bamboo_", "troll_masks_deadlands_", "troll_masks_norselands_", "troll_masks_greece_"
    };

    // 可调摆位常量（相对 Head 的局部空间，只影响自有 child）：root 的 44 项离线合图显示零偏移即正确，
    // 故默认全零；若后续 anniversary 摆位需要微调，只改这里，不要动 sprite 数据、不要加夸张 scale。
    private static readonly Vector3 WorldMaskNudge = Vector3.zero;
    private static readonly Vector3 PartyHatNudge = Vector3.zero;
    private static readonly Vector3 PartyMaskNudge = Vector3.zero;

    /// <summary>我们对某个原生 _mask renderer 实例写入过的 enabled 归属：baseline 只在"新实例第一次被我们接管"时读，
    /// 外部把同一实例改回 enabled 时刷新 baseline（绝不用我们自己写过的 false 当原值），Clear 只在自己仍是最后写入者时恢复。
    /// 恢复写入/读取失败一律保留归属并退避重试，绝不按次数永久放弃。</summary>
    private sealed class MaskOwnership
    {
        internal SpriteRenderer Renderer;
        internal bool Baseline;
        internal bool Hidden;
        internal int RestoreFailures;
        internal float NextRestoreAt;
    }

    private sealed class VisualState
    {
        internal FriendlyTroll Troll;
        internal Transform Head;
        internal int Choice;
        internal GameObject Root;
        internal SpriteRenderer Renderer;
        internal MaskOwnership Mask;
        internal int DestroyFailures;
        internal float NextDestroyAt;

        /// <summary>销毁已尝试且失败（退避重试中）：此时只推进清理，不再同步。</summary>
        internal bool CleanupPending => NextDestroyAt > 0f;
    }

    private static readonly Dictionary<int, VisualState> Tracked = new();
    private static readonly List<VisualState> PendingCleanup = new();
    private static readonly List<int> Retire = new();
    private static readonly HashSet<string> Logged = new();
    private static Dictionary<string, Sprite> catalog;
    private static HashSet<string> whitelist;
    private static float nextCatalogAttempt;

    /// <summary>
    /// choice 0..43 → 白名单名；`-1` = 保留原生（成功语义）；其余负数与 >=44 非法（不接管）。
    /// 只在"自有 child 已挂到本 troll 的 Head 上"之后才隐藏原生面罩：缺 Head、缺资源、构建失败都返回 false 且不碰原生对象。
    /// </summary>
    internal static bool Apply(FriendlyTroll troll, int choice)
    {
        try
        {
            if (choice == -1)
            {
                // -1 = 保留原生（成功语义）
                Clear(troll);
                return true;
            }

            if (choice < 0)
            {
                // 其余负数非法：不接管，并清掉本 troll 的 owned 视觉
                Clear(troll);
                return false;
            }

            if (troll == null || troll.gameObject == null || !troll.gameObject.activeInHierarchy) return false;

            string name = NameForChoice(choice);
            if (name == null)
            {
                // 非法 code：不接管，并清掉本 troll 的 owned 视觉
                Clear(troll);
                return false;
            }

            int id = troll.gameObject.GetInstanceID();
            VisualState state = null;
            if (Tracked.TryGetValue(id, out VisualState tracked))
            {
                if (Same(tracked.Troll, troll)) state = tracked;
                else
                {
                    // 池复用：同一 InstanceID 换了新的一代。先尽力释放旧 owned 视觉再让出 key；
                    // 未彻底归还的部分转入 cleanup 队列继续有界重试（绝不丢引用、绝不跨单位写回）
                    Release(tracked);
                    if (!FullyReleased(tracked)) PendingCleanup.Add(tracked);
                    Tracked.Remove(id);
                }
            }

            if (!ResolveHead(troll, out Transform head))
            {
                // 缺 Head 合法（不是所有 troll 都有），但没有挂点就绝不建视觉；已有 owned 视觉当场交回原生
                // （未彻底归还的条目留在 tracked，等下一次有界重试）
                if (state != null)
                {
                    Release(state);
                    if (FullyReleased(state)) Tracked.Remove(id);
                }

                return false;
            }

            if (state != null && state.Head != null && !Same(state.Head, head))
            {
                // Head 身份变化（重建/换骨骼）：销毁旧 child 并 rebind；原生面罩归属与 baseline 与 Head 无关，继续沿用
                DestroyRoot(state);
                state.Head = head;
            }

            if (state != null && state.Head != null && state.Choice == choice &&
                state.Renderer != null && state.Renderer.sprite != null)
            {
                // 重复 Apply（含原生 SpawnMask 重建之后）：只重新同步，不重复创建
                Sync(state);
                return true;
            }

            Sprite sprite = ResolveSprite(name);
            if (sprite == null) return false;

            if (state == null) state = new VisualState { Troll = troll };
            state.Choice = choice;
            state.Head = head;
            try
            {
                Tracked[id] = state;
                if (!EnsureChild(state))
                {
                    if (FullyReleased(state)) Tracked.Remove(id);
                    return false;
                }
                ApplyReference(state, sprite);
                SuppressNativeMask(state);
            }
            catch (Exception e)
            {
                Release(state);
                if (FullyReleased(state)) Tracked.Remove(id);
                Log("build", "构建头饰视觉失败: " + e.GetType().Name + ": " + e.Message);
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            Log("apply", "Apply 失败: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// core 在**确认原生 SpawnMask 已成功执行**后、其 Apply 之前调用（只对此 troll 的 owned visual 生效）：
    /// 把当前接管的 native _mask renderer 的真实 enabled 重新读成 baseline（true/false 都算真实值——因为刚被原生写过），
    /// 因此原生把同一个 renderer 设成 false（例如 maskIndex=-1）也能被正确记住；renderer 换代时解除旧归属、只关联新实例。
    /// 无 tracked 状态 → no-op：不建视觉、不写 receipt、不改 _maskIndex/health/tough。
    /// </summary>
    internal static void OnNativeMaskSpawned(FriendlyTroll troll)
    {
        try
        {
            if (troll == null || troll.gameObject == null) return;
            int id = troll.gameObject.GetInstanceID();
            if (!Tracked.TryGetValue(id, out VisualState state) || !Same(state.Troll, troll)) return;

            SpriteRenderer mask = troll._mask;
            if (mask == null)
            {
                // 原生这轮没有面罩实例：没有可接管的对象
                state.Mask = null;
                return;
            }

            MaskOwnership owned = state.Mask;
            if (owned == null || !Same(owned.Renderer, mask))
            {
                // renderer 换代（池/重建）：旧实例的归属当场解除（它是原生/池对象，原生会自己写它的 enabled），只关联新实例
                owned = new MaskOwnership { Renderer = mask, Baseline = mask.enabled };
                state.Mask = owned;
            }
            else
            {
                // 同一实例刚被原生写过：false 也是真实 baseline；我们不再声称是最后写入者，除非下面重新隐藏
                owned.Baseline = mask.enabled;
                owned.Hidden = false;
                owned.RestoreFailures = 0;
                owned.NextRestoreAt = 0f;
            }

            if (!mask.enabled) return;
            owned.Hidden = true;
            mask.enabled = false;
        }
        catch (Exception e)
        {
            Log("native-mask-spawned", "处理原生 SpawnMask 通知失败: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>只清本 troll 的 owned 视觉：销毁自有 child、按归属恢复原生 _mask.enabled；不删原生 mask、不做场景扫描。
    /// 恢复写入失败时保留归属（下一次 Clear/Tick/ClearAll 有界重试），不会丢掉恢复责任。</summary>
    internal static void Clear(FriendlyTroll troll)
    {
        try
        {
            if (troll == null || troll.gameObject == null) return;
            int id = troll.gameObject.GetInstanceID();
            if (!Tracked.TryGetValue(id, out VisualState state) || !Same(state.Troll, troll)) return;
            Release(state);
            if (FullyReleased(state)) Tracked.Remove(id);
        }
        catch (Exception e)
        {
            Log("clear", "Clear 失败: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>关闭/离场/world 切换用：清全部 owned 视觉（只 own 的状态，不碰原生 mask 对象、不碰 Head）。
    /// 任何单项异常都不得中断其他项；未彻底归还（native 恢复或 child 销毁未完成）的条目留在 tracked，等下一次有界重试。</summary>
    internal static void ClearAll()
    {
        try
        {
            Retire.Clear();
            foreach (KeyValuePair<int, VisualState> pair in Tracked)
            {
                try
                {
                    Release(pair.Value);
                    if (FullyReleased(pair.Value)) Retire.Add(pair.Key);
                }
                catch (Exception e)
                {
                    Log("clear-all-item", "清理单项失败: " + e.GetType().Name + ": " + e.Message);
                }
            }

            foreach (int id in Retire) Tracked.Remove(id);
            Retire.Clear();
        }
        catch (Exception e)
        {
            Retire.Clear();
            Log("clear-all", "ClearAll 失败: " + e.GetType().Name + ": " + e.Message);
        }

        CleanupQueued();
    }

    /// <summary>
    /// 只在自有状态上工作（无场景扫描、无 Transform.Find）：巨魔死亡/离场/回收/inactive、Head 失效、自有 child 被外部移除、
    /// 资源失效或清理未完成 → 推进 owned 清理并交回原生面罩；存活者只重新同步自有样式。
    /// 任一项异常不中断其他项；Head 身份变化由下一次 Apply 重新 Find 时检测并 rebind。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            if (Tracked.Count > 0)
            {
                Retire.Clear();
                foreach (KeyValuePair<int, VisualState> pair in Tracked)
                {
                    VisualState state = pair.Value;
                    try
                    {
                        if (!Live(state) || state.Head == null || state.Root == null || state.Renderer == null ||
                            state.Renderer.sprite == null || state.CleanupPending)
                        {
                            Retire.Add(pair.Key);
                            continue;
                        }

                        Sync(state);
                    }
                    catch (Exception e)
                    {
                        Log("tick-item", "同步单项失败: " + e.GetType().Name + ": " + e.Message);
                    }
                }

                foreach (int id in Retire)
                {
                    if (!Tracked.TryGetValue(id, out VisualState state)) continue;
                    try
                    {
                        Release(state);
                        if (FullyReleased(state)) Tracked.Remove(id);
                    }
                    catch (Exception e)
                    {
                        Log("tick-retire", "清理单项失败: " + e.GetType().Name + ": " + e.Message);
                    }
                }

                Retire.Clear();
            }
        }
        catch (Exception e)
        {
            Retire.Clear();
            Log("tick", "Tick 失败: " + e.GetType().Name + ": " + e.Message);
        }

        CleanupQueued();
    }

    /// <summary>
    /// 只读查询：该 troll 此刻是否**确实显示着**指定 choice 的自有头饰（供 core Sweep 识别“显示后 sprite/root 被毁”并重新排队 Apply）。
    /// 要求 tracked 状态属于同一个 troll、choice 相同、Head/自有 Root/Renderer 存活且启用、sprite 仍在、且当前未处于清理中。
    /// 任何 Unity 属性读取失败都返回 false，但绝不修改状态、也绝不因此丢弃 owned cleanup 责任。
    /// </summary>
    internal static bool IsApplied(FriendlyTroll troll, int choice)
    {
        try
        {
            if (troll == null || troll.gameObject == null) return false;
            int id = troll.gameObject.GetInstanceID();
            if (!Tracked.TryGetValue(id, out VisualState state) || !Same(state.Troll, troll)) return false;
            if (state.Choice != choice || state.Head == null || state.Root == null || state.Renderer == null) return false;
            if (state.CleanupPending) return false;
            return state.Root.activeInHierarchy && state.Renderer.enabled && state.Renderer.sprite != null &&
                Same(state.Renderer.transform.parent, state.Head);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>继续清理"已让出 tracked key 但尚未彻底归还"的自有图（同 InstanceID 换代等）：逐项独立重试，单项异常不中断其他项。</summary>
    private static void CleanupQueued()
    {
        if (PendingCleanup.Count == 0) return;
        try
        {
            for (int index = PendingCleanup.Count - 1; index >= 0; index--)
            {
                VisualState state = PendingCleanup[index];
                try
                {
                    Release(state);
                    if (FullyReleased(state)) PendingCleanup.RemoveAt(index);
                }
                catch (Exception e)
                {
                    Log("cleanup-item", "清理排队图失败: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Log("cleanup", "清理队列失败: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static bool Live(VisualState state)
    {
        FriendlyTroll troll = state.Troll;
        return troll != null && troll.gameObject != null && troll.gameObject.activeInHierarchy;
    }

    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;

    private static void Log(string key, string message)
    {
        if (!Logged.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[HermesHeadwear] " + message); }
        catch { }
    }

    private static string NameForChoice(int choice)
    {
        if (choice < 0 || choice >= ChoiceCount) return null;
        if (choice >= PartyMaskFirstChoice) return PartyMaskPrefix + (choice - PartyMaskFirstChoice);
        if (choice >= PartyHatFirstChoice) return PartyHatPrefix + (choice - PartyHatFirstChoice);
        return WorldPrefixes[choice / ChoiceGroupSize] + (choice % ChoiceGroupSize);
    }

    private static Vector3 NudgeForChoice(int choice)
    {
        if (choice >= PartyMaskFirstChoice) return PartyMaskNudge;
        if (choice >= PartyHatFirstChoice) return PartyHatNudge;
        return WorldMaskNudge;
    }

    private static bool IsWhitelisted(string name)
    {
        if (whitelist == null)
        {
            HashSet<string> set = new HashSet<string>(ChoiceCount, StringComparer.Ordinal);
            for (int choice = 0; choice < ChoiceCount; choice++) set.Add(NameForChoice(choice));
            whitelist = set;
        }

        return whitelist.Contains(name);
    }

    /// <summary>头饰挂点 = 本 troll 的直接子节点 "Head"（原生动画骨骼）。只在 Apply 里 Find，Tick 不查。</summary>
    private static bool ResolveHead(FriendlyTroll troll, out Transform head)
    {
        head = null;
        try
        {
            head = troll.transform.Find(HeadChildName);
        }
        catch (Exception e)
        {
            Log("head-lookup", "查找 Head 失败: " + e.GetType().Name + ": " + e.Message);
            return false;
        }

        if (head == null)
        {
            Log("head-missing", "本 troll 没有原生 Head 子节点，保持原生貌（不接管）");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 白名单贴图：命中且存活即返回（零扫描）。缺名或缓存条目已销毁（Unity 资源卸载）时，按**全局**冷却做一次有界刷新，
    /// 多个缺同一资源的对象共享同一次 FindObjectsOfTypeAll（绝不每对象各扫一次）；仍拿不到就 fail-closed 返回 null（不接管原生、不换 choice）。
    /// </summary>
    private static Sprite ResolveSprite(string name)
    {
        if (catalog != null && catalog.TryGetValue(name, out Sprite cached) && cached != null) return cached;

        if (Time.time >= nextCatalogAttempt) BuildCatalog();
        if (catalog != null && catalog.TryGetValue(name, out Sprite refreshed) && refreshed != null) return refreshed;

        Log("missing-" + name, "白名单贴图缺失: " + name);
        return null;
    }

    /// <summary>扫 Resources.FindObjectsOfTypeAll&lt;Sprite&gt;("") 重建白名单映射（首次需要才扫；同名重复对象取第一个）。
    /// 每次尝试都先推进全局冷却，避免任何单个对象反复触发扫描；刷新失败时保留旧映射（已命中的名字继续可用）。</summary>
    private static bool BuildCatalog()
    {
        nextCatalogAttempt = Time.time + CatalogRetrySeconds;
        try
        {
            Dictionary<string, Sprite> found = new Dictionary<string, Sprite>(ChoiceCount, StringComparer.Ordinal);
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null) continue;
                string spriteName = sprite.name;
                if (spriteName == null || found.ContainsKey(spriteName) || !IsWhitelisted(spriteName)) continue;
                found[spriteName] = sprite;
            }

            if (found.Count == 0)
            {
                Log("catalog-empty", "Resources.FindObjectsOfTypeAll<Sprite>(\"\") 没有返回任何白名单贴图");
                return false;
            }

            catalog = found;
            return true;
        }
        catch (Exception e)
        {
            Log("catalog", "读取白名单贴图失败: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    /// <summary>自有 child 的第一时间登记：GameObject 一创建就写进 state.Root，任何后续失败都销毁它，绝不 orphan；
    /// 若旧图的销毁失败过（清理未完成），把旧图交给 cleanup 队列继续重试，本状态换新图（绝不复用被标记销毁的图、绝不丢引用）。</summary>
    private static bool EnsureChild(VisualState state)
    {
        if (state.Root != null && state.Renderer != null && !state.CleanupPending) return true;
        if (state.Root != null)
        {
            PendingCleanup.Add(new VisualState
            {
                Root = state.Root,
                Renderer = state.Renderer,
                DestroyFailures = state.DestroyFailures,
                NextDestroyAt = state.NextDestroyAt
            });
            state.Root = null;
            state.Renderer = null;
            state.DestroyFailures = 0;
            state.NextDestroyAt = 0f;
        }

        GameObject go = null;
        try
        {
            go = new GameObject(ChildObjectName);
            state.Root = go;
            state.Renderer = go.AddComponent<SpriteRenderer>();
            return true;
        }
        catch (Exception e)
        {
            Release(state);
            Log("child", "创建自有头饰对象失败: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 销毁自有 child：先保存引用、优先 SetActive(false)（销毁失败也不可见），Destroy 抛异常时**保留 Root/Renderer 引用**并退避重试，
    /// 只有 Destroy 调用成功或对象确证 Unity-null 才清引用——绝不产生无法追踪的 orphan。只销毁自有对象，
    /// 绝不碰 Head / native mask / 粒子或其它原生物体。
    /// </summary>
    private static bool DestroyRoot(VisualState state)
    {
        GameObject root = state.Root;
        if (root == null)
        {
            state.Root = null;
            state.Renderer = null;
            return true;
        }

        if (Time.time < state.NextDestroyAt) return false;
        try
        {
            try { root.SetActive(false); }
            catch (Exception hide) { Log("hide", "隐藏自有头饰对象失败: " + hide.GetType().Name + ": " + hide.Message); }
            UnityEngine.Object.Destroy(root);
        }
        catch (Exception e)
        {
            state.DestroyFailures++;
            state.NextDestroyAt = Time.time + BackoffSeconds(state.DestroyFailures);
            Log("destroy", "销毁自有头饰对象失败(" + state.DestroyFailures + "): " + e.GetType().Name + ": " + e.Message);
            return false;
        }

        state.Root = null;
        state.Renderer = null;
        state.DestroyFailures = 0;
        state.NextDestroyAt = 0f;
        return true;
    }

    /// <summary>
    /// 自有 child 的全部表现：parent = 本 troll 的 Head（缓存），localPosition = (0,0,-1e-6) + 可调 nudge，
    /// localScale = one、localRotation = identity（朝向/位移/动画由父链继承，子节点不二次翻转、不强写父链 local）。
    /// 排序/翻转复制本体 renderer（order = 本体 order + 1），材质/颜色取原生 maskPrefab → _mask → 本体；
    /// sprite 用白名单原图（raw，不走 biome swap、不改 sprite 数据）。
    /// </summary>
    private static void ApplyReference(VisualState state, Sprite sprite)
    {
        FriendlyTroll troll = state.Troll;
        SpriteRenderer renderer = state.Renderer;
        SpriteRenderer body = troll.GetComponent<SpriteRenderer>();
        SpriteRenderer maskPrefab = troll._maskPrefab;
        SpriteRenderer appearance = maskPrefab;
        if (appearance == null) appearance = troll._mask;
        if (appearance == null) appearance = body;
        SpriteRenderer layout = body;
        if (layout == null) layout = maskPrefab;

        Transform head = state.Head;
        if (head == null) return;   // 没有挂点就绝不挂到场景根/别处
        if (!Same(renderer.transform.parent, head)) renderer.transform.SetParent(head, false);
        renderer.transform.localPosition = ChildLocalPosition + NudgeForChoice(state.Choice);
        renderer.transform.localRotation = Quaternion.identity;
        renderer.transform.localScale = Vector3.one;
        renderer.sprite = sprite;

        if (layout != null)
        {
            renderer.gameObject.layer = layout.gameObject.layer;
            renderer.sortingLayerID = layout.sortingLayerID;
            renderer.sortingOrder = NextOrder(layout.sortingOrder);
            renderer.flipX = layout.flipX;
            renderer.flipY = layout.flipY;
        }

        if (appearance != null)
        {
            renderer.sharedMaterial = appearance.sharedMaterial;
            renderer.color = appearance.color;
        }

        renderer.enabled = true;
    }

    /// <summary>自有 renderer 始终盖在本体之前一格（原生 maskPrefab 是 order 0，直套会被本体遮挡）。</summary>
    private static int NextOrder(int order) => order == int.MaxValue ? order : order + 1;

    private static void Sync(VisualState state)
    {
        try
        {
            if (state.Head == null || state.Root == null || state.Renderer == null || state.Renderer.sprite == null) return;
            ApplyReference(state, state.Renderer.sprite);
            SuppressNativeMask(state);
        }
        catch (Exception e)
        {
            Log("sync", "同步头饰视觉失败: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// 接管原生 _mask.enabled（仅视图开关；不动它的父子/位置）：新实例第一次接管时以它当时的 enabled 为 baseline
    /// （绝不沿用我们写过的 false）；同一实例被原生重新 enable（SpawnMask/ApplyData/反序列化）时刷新 baseline 再隐藏；
    /// 实例被重建则丢弃旧归属。
    /// </summary>
    private static void SuppressNativeMask(VisualState state)
    {
        SpriteRenderer mask = state.Troll._mask;
        if (mask == null)
        {
            // 原生面罩缺失合法（maskIndex=-1 / 尚未生成）：没有可接管的对象
            state.Mask = null;
            return;
        }

        MaskOwnership owned = state.Mask;
        if (owned == null || !Same(owned.Renderer, mask))
        {
            owned = new MaskOwnership { Renderer = mask, Baseline = mask.enabled };
            state.Mask = owned;
        }

        if (!mask.enabled) return;
        if (owned.Hidden) owned.Baseline = true;
        owned.Hidden = true;
        mask.enabled = false;
    }

    /// <summary>释放一个 owned 状态：先按归属恢复原生 enabled（失败则保留归属、退避重试），并在 finally 里始终尝试销毁自有 child。
    /// 绝不销毁 Head 或原生 mask。</summary>
    private static void Release(VisualState state)
    {
        try
        {
            ReleaseMask(state);
        }
        catch (Exception e)
        {
            Log("release", "释放 owned 状态失败: " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            state.Head = null;
            try { DestroyRoot(state); }
            catch (Exception e) { Log("destroy", "销毁自有头饰对象异常: " + e.GetType().Name + ": " + e.Message); }
        }
    }

    /// <summary>
    /// 只用我们自己写下的那次 enabled 归属去恢复。整段身份读取+写入都在 try 内：**读取失败视为未知**，保留归属等下一次重试，
    /// 绝不把读取失败当成"外部已替换"。只有三种确定情形才解除归属且一个字节都不写：没有我们写下的隐藏、当前值已被外部改写、
    /// 该 renderer 已不属于本 troll（换单位/池复用/被外部重挂父节点）。恢复责任保留到恢复成功或确证被外部接管，不按次数永久放弃。
    /// </summary>
    private static bool ReleaseMask(VisualState state)
    {
        MaskOwnership owned = state.Mask;
        if (owned == null) return true;
        if (Time.time < owned.NextRestoreAt) return false;

        try
        {
            if (!owned.Hidden || owned.Renderer == null || owned.Renderer.enabled)
            {
                state.Mask = null;
                return true;
            }

            FriendlyTroll troll = state.Troll;
            if (troll == null || !Same(owned.Renderer.transform.parent, troll.transform))
            {
                state.Mask = null;
                return true;
            }

            owned.Renderer.enabled = owned.Baseline;
            state.Mask = null;
            return true;
        }
        catch (Exception e)
        {
            owned.RestoreFailures++;
            owned.NextRestoreAt = Time.time + BackoffSeconds(owned.RestoreFailures);
            Log("restore", "恢复原生面罩 enabled 失败(" + owned.RestoreFailures + "): " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    /// <summary>该 owned 状态是否已彻底归还：原生 enabled 已恢复 **且** 自有 child 已确认销毁。两者都达成才允许从 tracked 移除。</summary>
    private static bool FullyReleased(VisualState state) => state.Mask == null && state.Root == null;

    /// <summary>有界退避（5s 起步、上限 30s），避免逐帧重试与逐帧日志。</summary>
    private static float BackoffSeconds(int failures) =>
        Math.Min(CleanupRetryMaxSeconds, CleanupRetrySeconds * (failures < 1 ? 1 : failures));
}
