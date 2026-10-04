using System;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 主银行家固定活动域（Issue 100）。当前 Greek 权威本体银行家的活动、认领与钱包
/// 拾取统一限定为“一次成功完整加载后快照到的、campfire 左右各最近的固定墙基”
/// 之间；墙未建、被毁或升级都不改变这个域，新增外墙也绝不扩大它。
///
/// 数据来源（2.4 原生调查已确认，见 issue-100 native review）：
///   - 未建墙基 Wall0 没有 Wall 组件，只有 PayableUpgrade.nextPrefab 直接指向带
///     Wall 的预制；建成墙与残骸都是当前 gameLayer 里的建筑根；
///   - Kingdom._orderedWalls / GetWall / GetBorderSide 只反映“当前已建墙”或
///     地形边界，不能代表固定墙基，因此本域一次都不读它们；
///   - 同名 Wall0 装饰（脚手架/塔内）只带 BiomeSpriteSwapper，没有 Wall 组件，
///     按组件判定天然排除。
/// 选择器：遍历当前 gameLayer 子树（includeInactive=true），
/// 候选 = 建筑根 Wall 组件，或 PayableUpgrade.nextPrefab 直接含 Wall；
/// 取 campfire 左右最近的候选，两侧齐全才发布。不按名字、不硬编码 ±9
/// （Greek 原生布局同时存在 ±9 与 ±8.75，外层 ±17）。
///
/// 完整性门：只有 Managers.OnLevelLoaded(bool) 正常返回后的独立 Postfix 才标记
/// “本次加载成功”；fromSave=false 是新岛的合法成功路径，同样是成功通知；
/// 真正的失败是通知根本没被调用或原生抛异常（postfix 不执行）。
/// 下一安全维护点（Banker.Update）做一次结构快照并发布缓存；捕获失败有界低频
/// 重试，任何读取/结构异常都不得部分发布。未收到成功通知、半加载、上下文换代
/// 而尚未重新发布时一律 fail-closed（域未知），绝不凭 Playing 状态补域。
/// 成功发布后直到下一次成功加载通知不再重选：墙的升级/倒塌/新增外墙都不改变
/// 已发布坐标（损坏存档或第三方预先删除内层墙不在已证支持范围）。
/// 客户端 catchup/权威门不受本通知影响——本类只维护只读结构快照。
/// </summary>
internal static class MainBankerFixedDomain
{
    /// <summary>捕获失败后的有界低频重试间隔（帧）：不逐帧全场扫描。</summary>
    internal const int CaptureRetryFrames = 30;

    /// <summary>上下文身份：world + kingdom + gameLayer + scene 四者一致才算同一域。</summary>
    private readonly struct ContextKey : IEquatable<ContextKey>
    {
        private readonly IntPtr _world;
        private readonly IntPtr _kingdom;
        private readonly IntPtr _layer;
        private readonly int _sceneHandle;

        internal ContextKey(IntPtr world, IntPtr kingdom, IntPtr layer, int sceneHandle)
        {
            _world = world;
            _kingdom = kingdom;
            _layer = layer;
            _sceneHandle = sceneHandle;
        }

        public bool Equals(ContextKey other)
            => _world == other._world && _kingdom == other._kingdom
                && _layer == other._layer && _sceneHandle == other._sceneHandle;

        public override bool Equals(object obj) => obj is ContextKey other && Equals(other);

        public override int GetHashCode()
            => (_sceneHandle * 397) ^ _world.GetHashCode()
                ^ (_kingdom.GetHashCode() * 31) ^ (_layer.GetHashCode() * 131);
    }

    private static int _loadGeneration;
    private static bool _capturePending;
    private static bool _hasNotifiedKey;
    private static ContextKey _notifiedKey;
    private static int _notifiedGeneration;
    private static bool _published;
    private static ContextKey _publishedKey;
    private static int _publishedGeneration;
    private static float _left;
    private static float _right;
    private static int _lastAttemptFrame = int.MinValue;
    private static int _lastLoggedGeneration = -1;

    /// <summary>已发布域所属的加载代数；未发布为 0。供调用方按域换代刷新。</summary>
    internal static int ReadyGeneration => _published ? _publishedGeneration : 0;

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>域内判定：严格 left &lt; x &lt; right（等于边界归助手）；非有限一律拒绝。</summary>
    internal static bool IsInside(float x, float left, float right)
        => IsFinite(x) && IsFinite(left) && IsFinite(right) && x > left && x < right;

    /// <summary>
    /// Managers.OnLevelLoaded(bool) 正常返回后的成功标记。与 ModEnabled 无关：
    /// 关模组期间也必须记账（marker 跨 off 保留），重新开启后才能在下一个维护点捕获。
    /// 只认 native 身份与 Managers.Inst 相同的实例。
    /// 通知当即绑定“本次成功加载的上下文 key”；key 当场读不到时本次不武装捕获
    /// （绝不延期用任意未来 current 代替），必须等下一次成功通知。
    /// 换代时旧 published proof 立即撤销。
    /// </summary>
    internal static void NoteSuccessfulLevelLoad(Managers instance)
    {
        bool hasKey;
        ContextKey key;
        try
        {
            if (instance == null) return;
            Managers current = Managers.Inst;
            if (current == null || instance.Pointer != current.Pointer) return;
            hasKey = TryReadContext(current, out key);
        }
        catch
        {
            hasKey = false;
            key = default;
        }

        _loadGeneration++;
        _published = false; // 换代后旧域立即失效
        _hasNotifiedKey = hasKey;
        _notifiedKey = key;
        _notifiedGeneration = _loadGeneration;
        _capturePending = hasKey; // key 读不到：本次不武装，等下一次成功通知
        _lastAttemptFrame = int.MinValue;
    }

    /// <summary>安全维护点的有界低频重试：仅在 pending 时尝试，返回本次是否发布成功。</summary>
    internal static bool Maintain(Managers managers)
    {
        if (!_capturePending) return false;
        int frame = Time.frameCount;
        if (_lastAttemptFrame != int.MinValue
            && frame - _lastAttemptFrame < CaptureRetryFrames) return false;
        _lastAttemptFrame = frame;
        return TryCapture(managers);
    }

    /// <summary>
    /// 一次性结构快照并发布。捕获只读、不做任何网络/权威变更。
    /// 入场先快照“本次要代表的这条通知”（key + generation）：扫描期间若同 context
    /// 再次成功加载（静态字段会一起换代），本次捕获必须作废并保留新通知的 pending，
    /// 绝不把旧快照发布到新代数上；比较对象自始至终是入场 local，而不是两个会同时
    /// 推进的静态字段互相比。当前上下文可读且不等于入场 key 时，该通知永久作废
    /// （必须等 fresh 成功通知，回到旧 wrapper 也不复活）。读取异常不部分发布。
    /// </summary>
    internal static bool TryCapture(Managers managers)
    {
        if (!_capturePending || !_hasNotifiedKey || managers == null) return false;
        if (_notifiedGeneration != _loadGeneration)
        {
            CancelPending(); // 过期 pending：不允许代表旧通知发布
            return false;
        }

        int entryGeneration = _loadGeneration;
        ContextKey entryKey = _notifiedKey;
        try
        {
            if (!TryReadContext(managers, out ContextKey pre)) return false; // 读不到：暂缓保留 pending
            if (!pre.Equals(entryKey))
            {
                // 可读但不等于通知绑定的 context：永久作废（含 pending），等 fresh 通知。
                CancelPendingIfUnchanged(entryGeneration, entryKey);
                return false;
            }
            Kingdom kingdom = managers.kingdom;
            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            if (kingdom == null || layer == null || layer.gameObject == null) return false;
            float campfire = kingdom.campfirePosition;
            if (!IsFinite(campfire)) return false;
            if (!TrySelectDomain(layer, campfire, out float left, out float right)) return false;

            // 发布前复核（对入场 local）：期间未被新通知取代，且上下文仍是同一份。
            if (_loadGeneration != entryGeneration || _notifiedGeneration != entryGeneration
                || !_capturePending || !_hasNotifiedKey)
                return false; // 新通知已覆盖：保留它的 pending，交给下一次捕获
            if (!TryReadContext(managers, out ContextKey post)) return false;
            if (!post.Equals(entryKey))
            {
                CancelPendingIfUnchanged(entryGeneration, entryKey);
                return false;
            }

            _left = left;
            _right = right;
            _publishedKey = entryKey;
            _publishedGeneration = entryGeneration;
            _published = true;
            _capturePending = false;
            if (_lastLoggedGeneration != _publishedGeneration)
            {
                _lastLoggedGeneration = _publishedGeneration;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[BankerDomain] fixed foundations published left=" + left
                    + " right=" + right + " generation=" + _publishedGeneration);
            }
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[BankerDomain] capture failed: " + e.GetType().Name);
            return false;
        }
    }

    /// <summary>撤销当前通知 proof/pending：必须等一次 fresh 成功通知才能再捕获。</summary>
    private static void CancelPending()
    {
        _capturePending = false;
        _hasNotifiedKey = false;
        _notifiedKey = default;
    }

    /// <summary>
    /// 仅当静态通知仍是入场时那条（代数/key 都未变）才撤销；被新通知取代时保持新 pending。
    /// </summary>
    private static void CancelPendingIfUnchanged(int generation, ContextKey key)
    {
        if (_loadGeneration != generation || _notifiedGeneration != generation
            || !_hasNotifiedKey || !_notifiedKey.Equals(key)) return;
        CancelPending();
    }

    /// <summary>
    /// 只读缓存查询。代数换代立即失效；当前上下文可读但不等于发布上下文时
    /// 永久撤销旧 proof（旧 wrapper/旧 scene 之后不能复活）；
    /// 当前上下文暂时读不到时 fail-closed 但保留 proof（加载中不确定，不猜）。
    /// </summary>
    internal static bool TryGetDomain(out float left, out float right)
    {
        left = 0f;
        right = 0f;
        try
        {
            if (!_published) return false;
            if (_publishedGeneration != _loadGeneration)
            {
                RevokePublished();
                return false;
            }
            if (!TryReadContext(Managers.Inst, out ContextKey key)) return false;
            if (!key.Equals(_publishedKey))
            {
                RevokePublished();
                return false;
            }
            if (!IsFinite(_left) || !IsFinite(_right) || !(_left < _right)) return false;
            left = _left;
            right = _right;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RevokePublished()
    {
        _published = false;
        _left = 0f;
        _right = 0f;
    }

    private static bool TryReadContext(Managers managers, out ContextKey key)
    {
        key = default;
        if (managers == null) return false;
        World world = managers.world;
        Kingdom kingdom = managers.kingdom;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || kingdom == null || layer == null || layer.gameObject == null)
            return false;
        key = new ContextKey(world.Pointer, kingdom.Pointer, layer.Pointer,
            layer.gameObject.scene.handle);
        return true;
    }

    /// <summary>
    /// 结构快照选择器：当前 gameLayer 子树（includeInactive）中，建筑根 Wall 组件或
    /// PayableUpgrade.nextPrefab 直接含 Wall 的候选；两侧各取离 campfire 最近的一个。
    /// 任一候选坐标非有限、任一侧缺失、left &lt; campfire &lt; right 不成立都拒绝
    /// 整次快照（不跳过坏点去选更外的墙，避免扩域）。同坐标多根（升级旧/新）自然归并。
    /// </summary>
    internal static bool TrySelectDomain(Transform layer, float campfire,
        out float left, out float right)
    {
        left = 0f;
        right = 0f;
        if (layer == null || !IsFinite(campfire)) return false;

        float bestLeft = float.NegativeInfinity;
        float bestRight = float.PositiveInfinity;
        bool hasLeft = false;
        bool hasRight = false;

        Wall[] walls = layer.GetComponentsInChildren<Wall>(true);
        if (walls != null)
        {
            for (int i = 0; i < walls.Length; i++)
            {
                Wall wall = walls[i];
                if (wall == null || wall.gameObject == null || wall.transform == null) continue;
                if (!ConsiderCandidate(wall.transform.position.x, campfire,
                        ref bestLeft, ref bestRight, ref hasLeft, ref hasRight))
                    return false;
            }
        }

        PayableUpgrade[] upgrades = layer.GetComponentsInChildren<PayableUpgrade>(true);
        if (upgrades != null)
        {
            for (int i = 0; i < upgrades.Length; i++)
            {
                PayableUpgrade upgrade = upgrades[i];
                if (upgrade == null || upgrade.gameObject == null || upgrade.transform == null)
                    continue;
                GameObject next = upgrade.nextPrefab;
                if (next == null) continue;
                if (next.GetComponent<Wall>() == null) continue; // 直接含 Wall（已证 Wall0 形状）
                // 防御层（Issue #125）：排除 KEM 补放墙基候选。几何上墙基滑块已
                // 禁止越过最内原生档，最内候选恒为原生档；此处再挡一层，防未来
                // 回归（KEM 名字在未购阶段可靠，已购墙全在原生最内档之外）。
                string candidateName = upgrade.gameObject != null ? upgrade.gameObject.name : null;
                if (candidateName != null
                    && candidateName.StartsWith(
                        PatchWorld_WallSpots.MarkerPrefix, System.StringComparison.Ordinal))
                    continue;
                if (!ConsiderCandidate(upgrade.transform.position.x, campfire,
                        ref bestLeft, ref bestRight, ref hasLeft, ref hasRight))
                    return false;
            }
        }

        if (!hasLeft || !hasRight) return false;
        if (!IsFinite(bestLeft) || !IsFinite(bestRight)) return false;
        if (!(bestLeft < campfire && campfire < bestRight)) return false;
        left = bestLeft;
        right = bestRight;
        return true;
    }

    private static bool ConsiderCandidate(float x, float campfire,
        ref float bestLeft, ref float bestRight, ref bool hasLeft, ref bool hasRight)
    {
        if (!IsFinite(x)) return false; // 损坏坐标：整次拒绝，绝不退到更外层的墙
        if (x < campfire)
        {
            if (!hasLeft || x > bestLeft) { bestLeft = x; hasLeft = true; }
        }
        else if (x > campfire)
        {
            if (!hasRight || x < bestRight) { bestRight = x; hasRight = true; }
        }
        // x == campfire：既非左也非右，忽略（strict left < campfire < right 由两侧共同保证）
        return true;
    }
}

/// <summary>
/// 成功完整加载门：Managers.OnLevelLoaded(bool) 正常返回后的独立 Postfix。
/// 参数 fromSave 只描述来源（新岛也是 false），不是成功/失败信号——
/// 本 postfix 能否执行才是；原生抛异常时 Harmony 不会调用 postfix，因此该次不会标记。
/// 与既有 Managers_OnLevelLoaded_ShieldAudio_Patch（Prefix）互不影响。
/// </summary>
[HarmonyPatch(typeof(Managers), nameof(Managers.OnLevelLoaded))]
internal static class Managers_OnLevelLoaded_MainBankerFixedDomain_Patch
{
    [HarmonyPostfix]
    internal static void Postfix(Managers __instance, bool fromSave)
    {
        MainBankerFixedDomain.NoteSuccessfulLevelLoad(__instance);
    }
}
