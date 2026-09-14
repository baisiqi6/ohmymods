using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 错误商店的安全清理队列（Castle 两处 DestroyImmediate 的替代）。
///
/// 为什么不在回调里就地销毁：
/// - <c>Castle.CatchupToLevel</c> / <c>Castle.ReQueueAllBuildings</c> 的 postfix 可能在
///   物理/动画/渲染回调链里执行，DestroyImmediate 在该阶段被 Unity 拒绝
///   （"Destroying components immediately is not permitted during physics trigger/contact,
///   animation event callbacks, rendering callbacks or OnValidate"）；报错后对象仍在，
///   下一次回调会再进同一条路径。这些报错与玩家 17 次 Unity 报错是否同源没有证据，不作结论。
///
/// 实际 2.4 语义（root/独立 reviewer 核验，GameAssembly SHA CD8C2B822B12F5416E73234D6D1052EFB499FE324ACEFF6264E0C87C6F8EDFC1）：
/// - <c>ShopPlanner.RemoveShop</c>（0x762ef0，272 字节唯一）：先 <c>_shops.Remove</c>，
///   再用 Unity <c>==</c> 比较 <c>_placedShops[tag.type]</c> 与传入对象——不等直接返回，
///   只有相等才 <c>SetPlacedShop(type, null)</c>。因此对象销毁时 <c>PayableShop.OnDestroy</c>
///   里那次重复/过期 RemoveShop 清不掉已经补位的新店。<b>本模块不新增任何 RemoveShop /
///   OnDestroy 钩子</b>，只依赖这个原生门。
/// - <c>PayableShop.OnDestroy</c>（0x67a080）确实在 0x67a19b 调 RemoveShop，不能整体跳过
///   （它还要解绑建筑回调）。<c>Payable.OnDisable</c>（0x6676e0）只清 payable 注册与
///   highlight，不碰槽位；<c>ShopTag</c> 没有 OnDisable/OnDestroy。
///
/// 本队列的契约（reviewer 要求）：
/// - 回调只登记具体对象；注销/停用/延迟销毁都在安全阶段（ModPanel.Update →
///   <see cref="TickPendingCleanup"/>）执行，<b>attempted ≠ completed</b>：RemoveShop 发起后必须
///   观察真实登记状态（<c>_shops</c> 与槽位具体对象）才算注销完成，未确认注销绝不销毁。
/// - 每个外部回调后（含 RemoveShop 抛异常后）重核对象身份/scene/tag/slot/仍然错误物品；观察不到
///   与确认改变分开处理：观察失败只退避重试，确认改变才停止旧写权。
/// - owned 责任不因配置关闭、权威暂时丢失、Managers 暂时未知或读取异常而丢弃：保留记录、低频
///   退避重试、日志按次数封顶。只有"尚未动手且前提已失效"才安全取消。
/// - Destroy 成功发起一次后只等 Unity 真正置 null，不逐帧重复 Destroy；失败则退避重试，不设
///   会丢对象的上限次数。
/// - 只有对象被确认销毁后，才回调 Ensure 补位。
/// - 不做全场扫描，也没有逐帧钩子：队列为空时 Tick 直接返回。
/// </summary>
internal static class ShopCleanupQueue
{
    /// <summary>队列容量上限（防御性；正常只有个位数条目）。</summary>
    private const int MaxEntries = 32;

    /// <summary>同一个 planner 会话里，同一个槽位最多允许清理几次（防止 prefab 映射坏了的销毁-重建循环）。</summary>
    internal const int MaxCleanupsPerSlot = 3;

    /// <summary>失败退避：0.5s 起翻倍，5s 封顶。</summary>
    private const float RetryBaseSeconds = 0.5f;
    private const float RetryMaxSeconds = 5f;

    /// <summary>同一条目每 N 次退避才写一行日志，避免刷屏。</summary>
    private const int LogEveryAttempts = 8;

    private const string BERSERKER_TOOL_TAG = "BerserkerTool";

    private enum Kind
    {
        /// <summary>_placedShops 槽位里的错误商店。</summary>
        PlacedShop,

        /// <summary>旧版克隆残留（按名字找到的具体对象）。</summary>
        LegacyMarker,
    }

    /// <summary>清理阶段：区分"已发起"和"已确认"。</summary>
    private enum Stage
    {
        /// <summary>还没对它做过任何事：前提失效可以安全取消。</summary>
        Untouched = 0,

        /// <summary>已发起注销但还没观察到完成，或注销未生效：绝不销毁。</summary>
        Deregistering = 1,

        /// <summary>已确认不再登记：可以停用、延迟销毁。</summary>
        Deregistered = 2,

        /// <summary>Destroy 已成功发起：只等 Unity 真正置 null。</summary>
        DestroyRequested = 3,
    }

    private sealed class Entry
    {
        public Kind Kind;
        public Stage Stage;
        public GameObject Target;
        public ShopPlanner Planner;

        /// <summary>登记时记录的槽位（PlacedShop 语义）。</summary>
        public PayableShop.ShopType Slot;

        /// <summary>登记时对象是否带 ShopTag，以及当时读到的 tag type（不随动态 tag 漂移）。</summary>
        public bool HasTag;
        public PayableShop.ShopType TagType;

        public string Name;
        public int InstanceId;
        public IntPtr Pointer;
        public int SceneHandle;

        /// <summary>登记时的真实世界身份：world/layer 指针 + layer scene + planner 指针。</summary>
        public IntPtr WorldPointer;
        public IntPtr LayerPointer;
        public int LayerScene;
        public IntPtr PlannerPointer;

        public int Attempts;
        public float NextAttemptAt;
    }

    /// <summary>当前世界身份（世界/layer/planner 都是对象指针，不用可复用的实例 id）。</summary>
    private struct WorldIdentity
    {
        public bool Valid;
        public IntPtr World;
        public IntPtr Layer;
        public int LayerScene;
        public IntPtr Planner;
    }

    /// <summary>槽位清理计数的真实 owner：planner 指针 + 槽位（换 planner/world 即失效）。</summary>
    private struct SlotOwner : IEquatable<SlotOwner>
    {
        public IntPtr Planner;
        public int Slot;

        public bool Equals(SlotOwner other) => Planner == other.Planner && Slot == other.Slot;
        public override bool Equals(object obj) => obj is SlotOwner other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Planner.GetHashCode() * 397) ^ Slot;
            }
        }
    }

    private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
    private static readonly Dictionary<SlotOwner, int> SlotCleanups = new Dictionary<SlotOwner, int>();
    private static readonly HashSet<SlotOwner> SlotCapLogged = new HashSet<SlotOwner>();
    private static readonly List<int> Snapshot = new List<int>();
    private static WorldIdentity _lastWorld;
    private static bool _ticking;

    /// <summary>当前待处理条目数（诊断与测试只读）。</summary>
    internal static int PendingCount
    {
        get { return Entries.Count; }
    }

    /// <summary>该 planner 的这个槽位是否还有未完成的清理（Ensure 用它决定不提前补位）。</summary>
    internal static bool IsPending(ShopPlanner planner, PayableShop.ShopType type)
    {
        if (planner == null) return false;
        foreach (var pair in Entries)
        {
            Entry entry = pair.Value;
            if (entry.Kind != Kind.PlacedShop) continue;
            if (entry.Planner != planner) continue;
            if (entry.Slot != type) continue;
            return true;
        }
        return false;
    }

    /// <summary>登记一个槽位里的错误商店（只登记，不做任何销毁）。</summary>
    internal static bool EnqueuePlacedShop(ShopPlanner planner, PayableShop.ShopType type, GameObject shop)
    {
        return Enqueue(planner, shop, Kind.PlacedShop, type, null);
    }

    /// <summary>登记一个旧版克隆残留对象（只登记，不做任何销毁）。</summary>
    internal static bool EnqueueLegacyMarker(ShopPlanner planner, GameObject marker, string markerName)
    {
        return Enqueue(planner, marker, Kind.LegacyMarker, default(PayableShop.ShopType), markerName);
    }

    /// <summary>该槽位当前放着真正的狂战士商店：清掉清理计数，让循环保护自愈。</summary>
    internal static void NotifySlotHealthy(ShopPlanner planner, PayableShop.ShopType type)
    {
        SlotOwner owner = OwnerOf(planner, type);
        SlotCleanups.Remove(owner);
        SlotCapLogged.Remove(owner);
    }

    /// <summary>
    /// 安全阶段处理入口。由 ModPanel.Update 在 <c>BankAssistantCoordinator.TickPendingCleanup()</c>
    /// 之后调用（root 统一接入）。这里既不是物理/动画/渲染回调，也不是 OnValidate，
    /// 因此注销、停用与延迟销毁都合法。
    /// </summary>
    internal static void TickPendingCleanup()
    {
        if (_ticking || Entries.Count == 0) return;

        _ticking = true;
        bool completed = false;
        try
        {
            WorldIdentity world = CaptureWorldIdentity();
            if (world.Valid && !SameWorld(world, _lastWorld))
            {
                // 换 world/planner：槽位计数属于上一个世界，不能跨世界复用（观察不到时不清）。
                SlotCleanups.Clear();
                SlotCapLogged.Clear();
                _lastWorld = world;
            }

            // 快照遍历：处理过程中（补位回调）新登记的条目留到下一帧，避免同帧重入。
            // 注意：门校验不用这份快照，每个阶段都在 EvaluateGates 里现场 CaptureWorld。
            Snapshot.Clear();
            Snapshot.AddRange(Entries.Keys);
            for (int i = 0; i < Snapshot.Count; i++)
            {
                if (!Entries.TryGetValue(Snapshot[i], out Entry entry)) continue;
                if (ProcessGuarded(entry)) completed = true;
            }
            Snapshot.Clear();
        }
        finally
        {
            _ticking = false;
        }

        if (!completed) return;

        // 只有对象被确认销毁之后才重新走 Ensure 补位。
        try
        {
            PatchRoles_Castle.NotifyShopCleanupCompleted();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] Shop cleanup re-ensure failed: " + e);
        }
    }

    /// <summary>门校验结论：通过 / 保留记录退避 / 停止写权退役。</summary>
    private enum Verdict { Pass, Defer, Retire }

    /// <summary>
    /// 每一次外部回调（含抛错）之后都要重新跑一遍：现场 <see cref="CaptureWorldIdentity"/>，
    /// 逐门校验配置/权威/世界归属/对象身份/scene/tag/仍然错误物品。
    /// 确认改变 → Retire；观察不到或暂时性状态 → Defer（保留 owned 记录）。不用 Tick 开头的快照。
    /// </summary>
    private static Verdict EvaluateGates(Entry entry, GameObject target, out string reason)
    {
        reason = null;

        if (!ModConfig.Enabled.Value) { reason = "mod disabled"; return Verdict.Defer; }

        Managers managers = Managers.Inst;
        if (managers == null) { reason = "Managers unavailable"; return Verdict.Defer; }
        if (entry.Planner == null || managers.shopPlanner != entry.Planner)
        { reason = "shop planner replaced"; return Verdict.Retire; }

        if (!NetworkBigBoss.HasWorldAuth) { reason = "no world authority"; return Verdict.Defer; }
        if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != PatchRoles_Castle.GREECE_BIOME)
        { reason = "left Greece"; return Verdict.Defer; }

        WorldIdentity world = CaptureWorldIdentity();
        if (!world.Valid) { reason = "world identity unreadable"; return Verdict.Defer; }
        if (!SameWorldAsEntry(entry, world))
        {
            // 换世界：还没动过手的可以安全取消；已经开始的保留记录，只等旧对象被确认销毁。
            if (entry.Stage == Stage.Untouched)
            { reason = "world changed before any action"; return Verdict.Retire; }
            reason = "world changed; waiting for the old object to be destroyed";
            return Verdict.Defer;
        }

        if (!TryObserveIdentity(entry, target, out bool sameIdentity, out string identityFailure))
        { reason = identityFailure; return Verdict.Defer; }
        if (!sameIdentity) { reason = "object identity changed"; return Verdict.Retire; }

        if (!TryObserveScene(entry, target, out bool sameScene, out string sceneFailure))
        { reason = sceneFailure; return Verdict.Defer; }
        if (!sameScene) { reason = "object left its scene"; return Verdict.Retire; }

        if (!TryObserveTag(entry, target, out bool sameTag, out string tagFailure))
        { reason = tagFailure; return Verdict.Defer; }
        if (!sameTag) { reason = "shop tag changed"; return Verdict.Retire; }

        if (!TryObserveWrongItem(entry, target, out bool stillWrong, out string itemFailure))
        { reason = itemFailure; return Verdict.Defer; }
        if (!stillWrong) { reason = "object is a real Berserker shop"; return Verdict.Retire; }

        return Verdict.Pass;
    }

    /// <summary>把门结论落到队列上；返回 false 表示本轮不再继续。</summary>
    private static bool ApplyVerdict(Entry entry, Verdict verdict, string reason)
    {
        if (verdict == Verdict.Pass) return true;
        if (verdict == Verdict.Retire) Retire(entry, reason);
        else Defer(entry, reason);
        return false;
    }

    private static bool ProcessGuarded(Entry entry)
    {
        try
        {
            return Process(entry);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] Shop cleanup entry failed: " + e);
            Defer(entry, "unexpected exception: " + e.GetType().Name);
            return false;
        }
    }

    /// <summary>返回 true 表示这个条目已经真正完成（对象确认销毁），需要重新补位。</summary>
    private static bool Process(Entry entry)
    {
        GameObject target = entry.Target;

        // 1) 已销毁（原生回收 / 原生 despawn / 我们的延迟销毁生效）→ 完成。
        if (target == null)
        {
            Complete(entry, "target destroyed");
            return true;
        }

        // 2) 已成功发起 Destroy 的条目：只等 Unity 真正置 null，不再重复调用。
        if (entry.Stage == Stage.DestroyRequested) return false;

        // 3) 退避门：失败或暂时不可判定时低频重试，不逐帧打扰。
        if (Now() < entry.NextAttemptAt) return false;

        return AdvanceStage(entry, target);
    }

    /// <summary>
    /// 阶段推进。每一步外部回调（RemoveShop / SetActive）之后立刻重跑全部门与真实登记状态，
    /// 绝不沿用回调之前的观察结果。
    /// </summary>
    private static bool AdvanceStage(Entry entry, GameObject target)
    {
        if (!ApplyVerdict(entry, EvaluateGates(entry, target, out string gateReason), gateReason)) return false;

        // 4) 还没动过手：先确认槽位前提仍然成立（不成立则安全取消）。
        if (entry.Stage == Stage.Untouched)
        {
            if (!TryObserveStartConditions(entry, target, out bool canStart, out string cancelReason, out string startFailure))
            {
                Defer(entry, startFailure);
                return false;
            }
            if (!canStart)
            {
                Retire(entry, cancelReason);
                return false;
            }
        }

        // 5) 注销阶段：attempted ≠ completed，未确认注销绝不销毁。
        if (entry.Stage == Stage.Untouched || entry.Stage == Stage.Deregistering)
        {
            if (!TryObserveRegistration(entry, target, out bool stillRegistered, out string registrationFailure))
            {
                Defer(entry, registrationFailure);
                return false;
            }

            if (stillRegistered)
            {
                entry.Stage = Stage.Deregistering; // 先记"已发起"，再调用
                TryDeregister(entry, target);      // 抛异常也不代表失败：下面复读真实状态

                if (!ApplyVerdict(entry, EvaluateGates(entry, target, out string postDeregisterReason),
                    postDeregisterReason)) return false;

                if (!TryObserveRegistration(entry, target, out stillRegistered, out registrationFailure))
                {
                    Defer(entry, registrationFailure);
                    return false;
                }
            }

            if (stillRegistered)
            {
                Defer(entry, "deregistration not confirmed");
                return false;
            }

            entry.Stage = Stage.Deregistered;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Roles] Shop cleanup deregistered (" + Describe(entry) + ")");
        }

        // 6) 停用：没观察到实际 inactive 就不销毁；SetActive 回调之后同样重跑全部门。
        if (!TryObserveInactive(target, out bool inactive, out string inactiveFailure))
        {
            Defer(entry, inactiveFailure);
            return false;
        }

        if (!inactive)
        {
            if (!TryDeactivate(entry, target))
            {
                Defer(entry, "deactivate not confirmed");
                return false;
            }
            if (!ApplyVerdict(entry, EvaluateGates(entry, target, out string postDeactivateReason),
                postDeactivateReason)) return false;
            if (!TryObserveInactive(target, out inactive, out inactiveFailure))
            {
                Defer(entry, inactiveFailure);
                return false;
            }
            if (!inactive)
            {
                Defer(entry, "deactivate did not take effect");
                return false;
            }
        }

        // 7) 销毁前最后一道：现场全门 + 没有被（重新）登记 + 实际 inactive。
        if (!ApplyVerdict(entry, EvaluateGates(entry, target, out string finalReason), finalReason)) return false;

        if (!TryObserveRegistration(entry, target, out bool registeredAgain, out string registryFailure))
        {
            Defer(entry, registryFailure);
            return false;
        }
        if (registeredAgain)
        {
            // 回调把它重新登记回 planner：这一轮不销毁，退回注销阶段重来。
            entry.Stage = Stage.Deregistering;
            Defer(entry, "target was registered again");
            return false;
        }

        if (!TryObserveInactive(target, out bool stillInactive, out string finalInactiveFailure))
        {
            Defer(entry, finalInactiveFailure);
            return false;
        }
        if (!stillInactive)
        {
            Defer(entry, "target became active again");
            return false;
        }

        // 8) 延迟销毁：成功发起一次即可，之后只等 Unity 置 null。
        if (!TryRequestDestroy(entry, target))
        {
            Defer(entry, "destroy request failed");
            return false;
        }
        entry.Stage = Stage.DestroyRequested;
        return false;
    }

    // ============================================================
    // 观察原语：Ok / 确认改变 / 观察不到（unknown）
    // ============================================================

    private static bool TryObserveIdentity(Entry entry, GameObject target, out bool same, out string failure)
    {
        same = false;
        failure = null;

        int id;
        try { id = target.GetInstanceID(); }
        catch (Exception) { failure = "object identity unreadable"; return false; }
        if (id == 0) { failure = "object identity unreadable"; return false; }

        IntPtr pointer;
        try { pointer = target.Pointer; }
        catch (Exception) { failure = "object pointer unreadable"; return false; }

        same = id == entry.InstanceId && pointer == entry.Pointer;
        return true;
    }

    private static bool TryObserveScene(Entry entry, GameObject target, out bool same, out string failure)
    {
        same = false;
        failure = null;

        int scene;
        try { scene = target.scene.handle; }
        catch (Exception) { failure = "scene identity unreadable"; return false; }

        same = scene == entry.SceneHandle;
        return true;
    }

    /// <summary>登记时的 tag 状态必须仍然一致（无 tag 的对象不能被后来加上 tag）。</summary>
    private static bool TryObserveTag(Entry entry, GameObject target, out bool same, out string failure)
    {
        same = false;
        failure = null;

        ShopTag tag;
        try { tag = target.GetComponent<ShopTag>(); }
        catch (Exception) { failure = "shop tag unreadable"; return false; }

        same = entry.HasTag ? (tag != null && tag.type == entry.TagType) : tag == null;
        return true;
    }

    /// <summary>
    /// 仍然错误物品：明确读到 itemPrefab 且不是 BerserkerTool 才算通过（stillWrong=true）。
    /// 明确不是错误商店 → stillWrong=false；读不到 → failure 非空（保留记录退避，不下结论）。
    /// </summary>
    private static bool TryObserveWrongItem(Entry entry, GameObject target, out bool stillWrong, out string failure)
    {
        stillWrong = false;
        failure = null;

        PayableShop shop;
        try { shop = target.GetComponent<PayableShop>(); }
        catch (Exception) { failure = "shop component unreadable"; return false; }

        Droppable prefab = null;
        if (shop != null)
        {
            try { prefab = shop.itemPrefab; }
            catch (Exception) { failure = "shop item prefab unreadable"; return false; }
        }

        bool berserker = false;
        if (prefab != null)
        {
            try { berserker = prefab.CompareTag(BERSERKER_TOOL_TAG); }
            catch (Exception) { failure = "shop item tag unreadable"; return false; }
        }

        if (berserker) return true; // 已经不是错误商店：调用方停手

        if (entry.Kind == Kind.PlacedShop && (shop == null || prefab == null))
        {
            // 槽位条目必须能明确判定"错误物品"；读不到就留着，不冒险删。
            failure = shop == null ? "shop component missing" : "shop item prefab missing";
            return false;
        }

        stillWrong = true;
        return true;
    }

    /// <summary>
    /// 尚未动手前的前提：槽位必须仍精确属于登记时那个对象（PlacedShop）；
    /// 旧版克隆的 tag 槽位若已被别的商店接管则安全取消。
    /// </summary>
    private static bool TryObserveStartConditions(Entry entry, GameObject target, out bool canStart,
        out string cancelReason, out string failure)
    {
        canStart = false;
        cancelReason = null;
        failure = null;

        if (!entry.HasTag)
        {
            // 登记时就没有 ShopTag → 原生 RemoveShop 只会 _shops.Remove，碰不到任何槽位。
            canStart = true;
            return true;
        }

        if (!TryObserveSlot(entry.Planner, entry.TagType, out GameObject occupant, out failure)) return false;

        if (entry.Kind == Kind.LegacyMarker)
        {
            if (occupant != null && occupant != target)
            {
                cancelReason = "slot " + entry.TagType + " holds a different shop";
                return true;
            }
            canStart = true;
            return true;
        }

        if (occupant != target)
        {
            cancelReason = occupant == null
                ? "slot " + entry.Slot + " is no longer held by the registered object"
                : "slot " + entry.Slot + " holds a different shop";
            return true;
        }

        canStart = true;
        return true;
    }

    /// <summary>
    /// 真实登记状态：对象仍在 planner 的 <c>_shops</c> 里，或它记录的槽位仍指向它，
    /// 都算"注销尚未完成"（attempted ≠ completed）。列表读不到一律 unknown（failure 非空），
    /// 绝不能把"读不到"当作"已注销"。
    /// </summary>
    private static bool TryObserveRegistration(Entry entry, GameObject target, out bool stillRegistered,
        out string failure)
    {
        stillRegistered = false;
        failure = null;

        ShopPlanner planner = entry.Planner;
        if (planner == null) { failure = "planner unavailable"; return false; }

        if (!TryObserveShopsList(planner, target, out bool inShops, out failure)) return false;
        if (inShops) { stillRegistered = true; return true; }
        if (!entry.HasTag) return true;

        if (!TryObserveSlot(planner, entry.TagType, out GameObject occupant, out failure)) return false;
        stillRegistered = occupant == target;
        return true;
    }

    /// <summary>planner._shops 观察；列表为 null 或读异常 → failure（unknown），不是"没登记"。</summary>
    private static bool TryObserveShopsList(ShopPlanner planner, GameObject shop, out bool contains,
        out string failure)
    {
        contains = false;
        failure = null;

        Il2CppSystem.Collections.Generic.List<GameObject> shops;
        try { shops = planner._shops; }
        catch (Exception) { failure = "shop registry unreadable"; return false; }
        if (shops == null) { failure = "shop registry unreadable"; return false; }

        try
        {
            foreach (GameObject registered in shops)
            {
                if (registered == shop) { contains = true; return true; }
            }
        }
        catch (Exception) { failure = "shop registry unreadable"; return false; }

        return true;
    }

    /// <summary>槽位观察；数组为 null / 索引越界 / 读异常 → failure（unknown），不是"槽位为空"。</summary>
    private static bool TryObserveSlot(ShopPlanner planner, PayableShop.ShopType type,
        out GameObject occupant, out string failure)
    {
        occupant = null;
        failure = null;
        if (planner == null) { failure = "slot state unreadable"; return false; }

        Il2CppReferenceArray<GameObject> placed;
        try { placed = planner._placedShops; }
        catch (Exception) { failure = "slot state unreadable"; return false; }
        if (placed == null) { failure = "slot state unreadable"; return false; }

        int index = (int)type;
        if (index < 0 || index >= placed.Length) { failure = "slot state unreadable"; return false; }

        try { occupant = placed[index]; }
        catch (Exception) { failure = "slot state unreadable"; return false; }

        return true;
    }

    private static bool TryObserveInactive(GameObject target, out bool inactive, out string failure)
    {
        inactive = false;
        failure = null;

        try { inactive = !target.activeSelf; }
        catch (Exception) { failure = "activity state unreadable"; return false; }

        return true;
    }

    /// <summary>已由 <see cref="TryObserveRegistration"/> 确认仍需注销时才调用。</summary>
    private static void TryDeregister(Entry entry, GameObject target)
    {
        try
        {
            entry.Planner.RemoveShop(target);
        }
        catch (Exception e)
        {
            // 可能写了一半、也可能完全没写：阶段一律由随后的真实状态观察决定。
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Roles] Shop cleanup deregistration threw: " + e);
        }
    }

    /// <summary>只在观察到仍未 inactive 时调用；返回 false 表示停用没有生效。</summary>
    private static bool TryDeactivate(Entry entry, GameObject target)
    {
        try
        {
            target.SetActive(false);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Roles] Shop cleanup deactivate failed: " + e);
        }

        return TryObserveInactive(target, out bool inactive, out _) && inactive;
    }

    private static bool TryRequestDestroy(Entry entry, GameObject target)
    {
        try
        {
            UnityEngine.Object.Destroy(target);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Roles] Shop cleanup destroy failed: " + e);
            return false;
        }
    }

    // ============================================================
    // 队列维护
    // ============================================================

    private static bool Enqueue(ShopPlanner planner, GameObject shop, Kind kind,
        PayableShop.ShopType slot, string markerName)
    {
        try
        {
            if (planner == null || shop == null) return false;

            int id;
            try { id = shop.GetInstanceID(); }
            catch (Exception) { id = 0; }
            if (id == 0)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Roles] Shop cleanup skipped: unreadable object identity for '" + SafeName(shop) + "'");
                return false;
            }

            if (Entries.TryGetValue(id, out Entry existing))
            {
                if (existing.Target != shop) return false;
                if (kind == Kind.PlacedShop && existing.Kind == Kind.LegacyMarker)
                {
                    // 同一对象先按 Legacy 登记、随后又发现它占着某个槽位：升级为槽位条目，
                    // 让 IsPending/槽位复核/补位按 Placed 语义工作（保留已有阶段与登记结果）。
                    existing.Kind = Kind.PlacedShop;
                    existing.Slot = slot;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[Roles] Shop cleanup upgraded to slot entry (" + Describe(existing) + ")");
                }
                return true;
            }

            if (Entries.Count >= MaxEntries)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Roles] Shop cleanup queue is full (" + MaxEntries + "); skipping '" + SafeName(shop) + "'");
                return false;
            }

            if (kind == Kind.PlacedShop && AtCleanupCap(planner, slot))
            {
                LogCapOnce(planner, slot);
                return false;
            }

            var world = CaptureWorldIdentity();
            var entry = new Entry
            {
                Kind = kind,
                Stage = Stage.Untouched,
                Target = shop,
                Planner = planner,
                Slot = slot,
                Name = kind == Kind.LegacyMarker && !string.IsNullOrEmpty(markerName)
                    ? markerName
                    : SafeName(shop),
                InstanceId = id,
                Pointer = shop.Pointer,
                SceneHandle = SceneHandleOf(shop),
                WorldPointer = world.Valid ? world.World : IntPtr.Zero,
                LayerPointer = world.Valid ? world.Layer : IntPtr.Zero,
                LayerScene = world.Valid ? world.LayerScene : 0,
                PlannerPointer = world.Valid ? world.Planner : IntPtr.Zero,
            };

            // 登记时读一次 tag：之后所有判定都用这份记录，不随动态 tag 漂移。
            ShopTag tag = null;
            try { tag = shop.GetComponent<ShopTag>(); }
            catch (Exception) { tag = null; }
            entry.HasTag = tag != null;
            if (tag != null) entry.TagType = tag.type;

            Entries.Add(id, entry);

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Roles] Shop cleanup registered (" + Describe(entry) + "), scene " + entry.SceneHandle
                + (entry.HasTag ? ", tag " + entry.TagType : ", no tag"));
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return false;
        }
    }

    private static void Complete(Entry entry, string reason)
    {
        Remove(entry);
        if (entry.Kind == Kind.PlacedShop)
        {
            SlotOwner owner = OwnerOf(entry.Planner, entry.Slot);
            SlotCleanups.TryGetValue(owner, out int done);
            SlotCleanups[owner] = done + 1;
        }
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[Roles] Shop cleanup finished (" + Describe(entry) + "): " + reason);
    }

    private static void Retire(Entry entry, string reason)
    {
        Remove(entry);
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[Roles] Shop cleanup retired (" + Describe(entry) + "): " + reason);
    }

    /// <summary>保留 owned 记录、低频退避重试；日志按次数封顶。</summary>
    private static void Defer(Entry entry, string reason)
    {
        entry.Attempts++;
        entry.NextAttemptAt = Now() + RetryDelay(entry.Attempts);

        if (entry.Attempts == 1 || entry.Attempts % LogEveryAttempts == 0)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Roles] Shop cleanup deferred (" + Describe(entry) + "): " + reason
                + " [attempt " + entry.Attempts + "]");
        }
    }

    private static void Remove(Entry entry)
    {
        if (entry == null) return;
        if (Entries.TryGetValue(entry.InstanceId, out Entry current) && ReferenceEquals(current, entry))
            Entries.Remove(entry.InstanceId);
    }

    // ============================================================
    // 世界/槽位/计数辅助
    // ============================================================

    private static WorldIdentity CaptureWorldIdentity()
    {
        var identity = new WorldIdentity();
        try
        {
            Managers managers = Managers.Inst;
            if (managers == null) return identity;

            World world = managers.world;
            Transform layer = world != null ? world.gameLayer : null;
            ShopPlanner planner = managers.shopPlanner;
            if (world == null || layer == null || layer.gameObject == null || planner == null) return identity;

            identity.World = world.Pointer;
            identity.Layer = layer.Pointer;
            identity.LayerScene = layer.gameObject.scene.handle;
            identity.Planner = planner.Pointer;
            identity.Valid = identity.World != IntPtr.Zero && identity.Layer != IntPtr.Zero
                && identity.Planner != IntPtr.Zero;
        }
        catch (Exception)
        {
            return default(WorldIdentity);
        }
        return identity;
    }

    private static bool SameWorld(WorldIdentity a, WorldIdentity b)
    {
        return a.Valid && b.Valid
            && a.World == b.World && a.Layer == b.Layer
            && a.LayerScene == b.LayerScene && a.Planner == b.Planner;
    }

    private static bool SameWorldAsEntry(Entry entry, WorldIdentity world)
    {
        return world.Valid
            && entry.WorldPointer == world.World
            && entry.LayerPointer == world.Layer
            && entry.LayerScene == world.LayerScene
            && entry.PlannerPointer == world.Planner;
    }

    private static SlotOwner OwnerOf(ShopPlanner planner, PayableShop.ShopType type)
    {
        var owner = new SlotOwner { Slot = (int)type };
        try { if (planner != null) owner.Planner = planner.Pointer; }
        catch (Exception) { owner.Planner = IntPtr.Zero; }
        return owner;
    }

    private static bool AtCleanupCap(ShopPlanner planner, PayableShop.ShopType type)
    {
        return SlotCleanups.TryGetValue(OwnerOf(planner, type), out int done) && done >= MaxCleanupsPerSlot;
    }

    private static void LogCapOnce(ShopPlanner planner, PayableShop.ShopType type)
    {
        if (!SlotCapLogged.Add(OwnerOf(planner, type))) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogError(
            "[Roles] Shop slot " + type + " was cleaned " + MaxCleanupsPerSlot
            + " times and still holds a non-Berserker shop; check the ShieldShop prefab mapping.");
    }

    private static float RetryDelay(int attempts)
    {
        int shift = attempts < 1 ? 0 : (attempts > 6 ? 6 : attempts - 1);
        float delay = RetryBaseSeconds * (1 << shift);
        return delay > RetryMaxSeconds ? RetryMaxSeconds : delay;
    }

    private static float Now()
    {
        try { return Time.unscaledTime; }
        catch (Exception) { return 0f; }
    }

    private static int SceneHandleOf(GameObject go)
    {
        try { return go.scene.handle; }
        catch (Exception) { return 0; }
    }

    private static string SafeName(GameObject go)
    {
        try { return go != null ? go.name : "<gone>"; }
        catch (Exception) { return "<unreadable>"; }
    }

    private static string Describe(Entry entry)
    {
        string what = entry.Kind == Kind.PlacedShop ? entry.Slot.ToString() : "legacy marker";
        return what + " '" + entry.Name + "' [" + entry.Stage + "]";
    }
}
