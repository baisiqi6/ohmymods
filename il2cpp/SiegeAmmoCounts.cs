using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 攻城弹药真值缓存（role6 投石车油桶 / role7 希腊火塔火罐），只读游戏世界字段。
/// 口径 = 现有未消耗、仍可用的真实弹药，不区分付款来源，也不把永久 buff 当未来无限库存：
///
/// role6 = 站点投石车 queuedOilBarrels + 仍真实挂在 spoon 上的那一发 OilBarrel + 本站点
/// <c>_activeBarrels</c> 中有效且归属本站点的运输桶（含 _delivered=true 仍在原生 DelayDespawn
/// 一秒窗口内、尚未 QueueOilBarrel 的桶）。原生链条：RollableOilBarrel.DelayDespawn 先
/// QueueOilBarrel() 再 OnBarrelComplete()/Despawn（同帧同步）→ 运输与 queued 不会同时可见；
/// Catapult.CreateProjectile 装载时 queuedOilBarrels-- 且弹体挂到 spoon 之下，真正发射由
/// ReleaseProjectileFromAnimation 把弹体移出 spoon 并把 _projectile 置空。
///   - “已装填”判据不依赖 currentLaunchableIsOil（Catapult.Fire 在发射动画开始就清它，
///     而弹体真正离开 spoon 更晚），也不按 _fireAttacks 排除：以 _projectile.activeInHierarchy
///     + GetComponent&lt;OilBarrel&gt;() + parent 精确等于 spoon + 当前场景/层为证。
///   - 投石车被毁/待重建时原生保留 _activeBarrels（OnCatapultBuilt 再重指目标），因此站点
///     仍计归属自己的活跃运输桶，不强制 targetCatapult 现存或等于旧已毁对象；队列/装填在
///     无存活投石车时为 0。
///   - 池中 active=false、跨场景/世界层、非本站点归属的桶一律不计。
///
/// role7 = FireTower._fireJarsActiveNum。CreateProjectile 只隐藏一个个假罐并在 spoon 上生成预览，
/// 绝不 +=，只有 ReleaseProjectile 才 --，所以绝不把 _projectile / _fakeFireJars 再加一遍。
/// owner 必须精确是这座塔（_owner.Pointer == tower.Pointer），只有 non-null 或
/// _payableComponent 反指都不算；坏 owner / 非激活塔不进入可买 targets。
/// 目标容量满（CanPay 为假）仍发布真实库存；可购买资格由服务自己的原生 CanPay 门判定。
///
/// 成员集合只来自 PayableManager.AllPayables（bootstrap / AddPayable+RemovePayable dirty 钩子 /
/// 世界·层·场景上下文切换），与 0.5s 采样窗口合并，绝不每帧重扫；<c>force=true</c> 仅供最终
/// commit 立即重读，且照样被异常退避拦截。Classify/采样/GetPayables 均复核目标 active、
/// owner、指针/实例 ID 与 context；无周期 FindObjects/Resources/全场景扫描，不做任何原生写、
/// RPC、池与游戏状态恢复。启动/registry/读字段异常一律 fail-closed（IsReady=false，1s 退避，
/// 连续 3 次 latch，新注册事件或新上下文解锁）；不发布伪造 0。客户端无世界权威时不发布计数。
///
/// 时钟：所有 <c>now</c> 参数必须是无缩放时钟（Time.unscaledTime）。HUD 与服务的调用方
/// 统一传 Time.unscaledTime；混用 Time.time 会在暂停/加载时破坏采样与退避 deadline。
/// </summary>
internal static class SiegeAmmoCounts
{
    internal const int RoleCatapultBarrel = 6;
    internal const int RoleFireTowerAmmo = 7;
    private const int RoleCount = 8;

    // 稳态弹药字段读取 + dirty 成员重读的共同节流窗口
    private const float ReadInterval = 0.5f;
    // 异常退避：force 也不得冲破，避免每帧重扫全 Payables
    private const float ErrorBackoff = 1f;
    private const int MaxFailures = 3;

    private enum Verdict { Admit, Defer, Drop }

    /// <summary>一个已发布的真实弹药站点。</summary>
    private sealed class Target
    {
        internal Payable Payable;             // 强引用 root：池化复用期间保持原生身份
        internal GameObject Go;
        internal IntPtr GoPtr;
        internal int GoId;
        internal int Role;
        internal int IslandCount;
        internal PayableWorkshopBarrel Site;  // role6：运输/队列站点
        internal FireTower Tower;             // role7：火塔
        internal IntPtr ActorPtr;             // role7：火塔指针（防池化复用）
        internal int ActorId;
        internal int Count;                   // 最近一次采样的本地未用弹药
    }

    // === 状态（Unity 主线程；无锁） ===
    private static readonly List<Target> _targets = new List<Target>();
    private static readonly Dictionary<IntPtr, Target> _byGoPtr = new Dictionary<IntPtr, Target>();
    private static readonly Dictionary<IntPtr, Payable> _pending = new Dictionary<IntPtr, Payable>();
    private static readonly HashSet<IntPtr> _registered = new HashSet<IntPtr>();
    private static readonly HashSet<IntPtr> _seenBarrels = new HashSet<IntPtr>();
    private static readonly HashSet<IntPtr> _seenCatapults = new HashSet<IntPtr>();
    private static readonly List<IntPtr> _scratch = new List<IntPtr>();
    private static readonly int[] _counts = new int[RoleCount];
    private static readonly int[] _published = new int[RoleCount];

    private static Managers _managers;
    private static Transform _gameLayer;
    private static IntPtr _kingdom, _world, _layer;
    private static int _scene;
    private static bool _hasContext, _registryDirty = true, _latched, _faultLogged;
    private static bool _snapshotLogged;
    private static int _failures;
    private static float _nextRead, _retryAfter;
    private static int _memberRevision, _publishedRevision = -1;

    internal static bool IsReady { get; private set; }
    internal static bool ClientUnavailable { get; private set; }
    internal static long Version { get; private set; }

    // === 公共 API（严格 contract） ===

    /// <summary>role6/role7 的全岛真实未用弹药；未就绪或其它 role 返回 0。</summary>
    internal static int Count(int role)
        => IsReady && (role == RoleCatapultBarrel || role == RoleFireTowerAmmo) ? _counts[role] : 0;

    /// <summary>
    /// clear/fill 当前世界已缓存的有效目标（复核 active/owner/指针·实例 ID/context），
    /// 绝不读取 AllPayables、FindObjects 或 Resources。
    /// </summary>
    internal static void GetPayables(int role, List<Payable> output)
    {
        if (output == null) return;
        output.Clear();
        if (!IsReady) return;
        if (role != RoleCatapultBarrel && role != RoleFireTowerAmmo) return;
        try
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Role != role) continue;
                if (!ValidateTarget(target)) continue;      // 坏 owner / 非激活 / 换场景不列给服务
                output.Add(target.Payable);
            }
        }
        catch (Exception)
        {
            RequestRecovery();
            output.Clear();
        }
    }

    /// <summary>
    /// 6 = PayableWorkshopBarrel；7 = 同 GO FireTower 持有（_owner.Pointer 精确相等）且仍激活的
    /// PayableComponent；否则 -1。坏 owner / 非激活塔 / 非当前 context 都不算可买目标。
    /// </summary>
    internal static int Classify(Payable payable)
    {
        try
        {
            if (payable == null) return -1;
            GameObject go = payable.gameObject;
            if (go == null) return -1;
            if (_hasContext)
            {
                // 已建立 context 时复核场景/世界层；未建立时不否决（由服务自身的 world 门兜底）。
                if (go.scene.handle != _scene) return -1;
                Transform t = go.transform;
                if (t == null || !t.IsChildOf(_gameLayer)) return -1;
            }
            if (payable.TryCast<PayableWorkshopBarrel>() != null) return RoleCatapultBarrel;
            var component = payable.TryCast<PayableComponent>();
        if (component == null) return -1;
            FireTower tower = FindTower(component);
            if (tower == null || !tower.gameObject.activeInHierarchy) return -1;
            return OwnsTower(component, tower) ? RoleFireTowerAmmo : -1;
        }
        catch (Exception)
        {
            RequestRecovery();
            return -1;
        }
    }

    /// <summary>目标本地未用弹药（缓存值；未知/未就绪返回 0，不做任何原生读）。</summary>
    internal static int CountAt(Payable payable)
    {
        if (payable == null || !IsReady) return 0;
        try
        {
            GameObject go = payable.gameObject;
            if (go == null) return 0;
            if (!_byGoPtr.TryGetValue(go.Pointer, out Target target) || target == null) return 0;
            return ReferenceEquals(target.Payable, payable) ? target.Count : 0;
        }
        catch (Exception)
        {
            RequestRecovery();
            return 0;
        }
    }

    /// <summary>
    /// 主调度（HUD 与服务共用；enabled = HUD 开关 OR 弹药自动开关；now 必须是无缩放时钟）：
    /// 核上下文 → 有界成员重建 → 节流采样缓存目标。异常 fail-closed 并退避。
    /// </summary>
    internal static bool Refresh(Managers managers, bool enabled, float now, bool force = false)
    {
        if (!enabled)
        {
            if (_hasContext || IsReady || _targets.Count != 0) Clear();
            return false;
        }
        // 异常退避优先于 force：绝不因为最终 commit 每帧重扫 AllPayables
        if (now < _retryAfter) return IsReady;
        try
        {
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            World world = managers != null ? managers.world : null;
            Game game = managers != null ? managers.game : null;
            Transform layer = world != null ? world.gameLayer : null;
            if (kingdom == null || world == null || game == null || layer == null)
            { ClearUnavailableContext(); return false; }

            IntPtr kingdomPtr = kingdom.Pointer, worldPtr = world.Pointer, layerPtr = layer.Pointer;
            int scene = layer.gameObject.scene.handle;
            bool sameContext = _hasContext && kingdomPtr == _kingdom && worldPtr == _world
                && layerPtr == _layer && scene == _scene;
            Game.State state = game.state;
            bool pausedInKnownWorld = state == Game.State.Menu && sameContext;
            if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying && !pausedInKnownWorld)
            { ClearUnavailableContext(); return false; }

            if (!sameContext)
            {
                Clear();
                _hasContext = true;
                _kingdom = kingdomPtr; _world = worldPtr; _layer = layerPtr; _scene = scene;
            }
            _managers = managers;
            _gameLayer = layer;

            // 客户端没有权威弹药值：空/不完整读数不是可信的 0
            if (!NetworkBigBoss.HasWorldAuth)
            {
                if (!ClientUnavailable)
                {
                    ClearTargets();
                    ClientUnavailable = true;
                    IsReady = false;
                    Version++;
                }
                return false;
            }
            if (ClientUnavailable)
            {
                ClientUnavailable = false;
                _registryDirty = true;
                _latched = false;
                _failures = 0;
                _nextRead = _retryAfter = 0;
                Version++;
            }
            if (_latched) return false;

            // dirty 成员重读与采样共用 0.5s 窗口；force 立即重读（最终 commit）
            bool rebuild = _registryDirty && (force || now >= _nextRead);
            if (rebuild) Rebuild();
            if (rebuild || force || now >= _nextRead)
            {
                _nextRead = now + ReadInterval;
                FlushPending();
                bool stable = Sample();          // 有目标身份失效 = 本轮快照不可发布
                if (stable)
                {
                    Publish();
                    IsReady = _pending.Count == 0;
                    if (IsReady && !_snapshotLogged)
                    {
                        _snapshotLogged = true;
                        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SiegeAmmoCounts] ready=true sites=" + _targets.Count
                            + " barrels=" + _counts[RoleCatapultBarrel] + " fireJars=" + _counts[RoleFireTowerAmmo]); } catch { }
                    }
                }
                else
                {
                    IsReady = false;
                    _registryDirty = true;       // 下轮有界重建，绝不静默发布低数
                }
                _failures = 0;
                _retryAfter = 0f;
            }
            return IsReady;
        }
        catch (Exception ex)
        {
            // 有界恢复：1s 退避 + 连续 3 次失败 latch；不保留半可信快照
            IsReady = false;
            _registryDirty = true;
            _nextRead = now + ErrorBackoff;
            _retryAfter = _nextRead;
            if (++_failures >= MaxFailures) _latched = true;
            if (!_faultLogged)
            {
                _faultLogged = true;
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SiegeAmmoCounts] ammo counts unavailable: " + ex.GetType().Name); }
                catch { }
            }
            return false;
        }
    }

    /// <summary>AddPayable/RemovePayable Postfix：只置脏（原生回调内不碰任何原生对象）。</summary>
    internal static void HookRegistryChanged()
    {
        _registryDirty = true;
        if (_latched)
        {
            _latched = false;
            _failures = 0;
        }
    }

    // === 成员集合：只在 bootstrap / dirty / 换世界时读一次 AllPayables ===

    private static void Rebuild()
    {
        _registryDirty = false;
        _registered.Clear();
        _targets.Clear();
        _byGoPtr.Clear();
        Array.Clear(_counts, 0, RoleCount);
        _memberRevision++;

        PayableManager registry = _managers != null ? _managers.payables : null;
        if (registry == null) throw new InvalidOperationException("Payable registry not ready");
        Il2CppReferenceArray<Payable> payables = registry.AllPayables;
        if (payables == null) throw new InvalidOperationException("Payable registry not ready");

        for (int i = 0; i < payables.Length; i++)
        {
            Payable payable = payables[i];
            if (payable == null) continue;
            GameObject go = payable.gameObject;
            if (go == null) continue;
            _registered.Add(go.Pointer);
            int role = KindOf(payable);
            if (role < 0) continue;                                  // 非目标类型：不计、不 pending
            // Defer（原生 Awake 早于 parent 挂接 / owner 尚未 Init）保留候选，绝不丢到换岛
            if (TryAdmit(payable, role) == Verdict.Defer) _pending[go.Pointer] = payable;
        }

        // pending 候选只在仍注册于原生 registry 时保留（不因重建丢失，也不留回收垃圾）
        if (_pending.Count != 0)
        {
            _scratch.Clear();
            foreach (KeyValuePair<IntPtr, Payable> pair in _pending)
                if (!_registered.Contains(pair.Key)) _scratch.Add(pair.Key);
            for (int i = 0; i < _scratch.Count; i++) _pending.Remove(_scratch[i]);
        }
    }

    /// <summary>只判种类（可为未完成状态保留候选）；严格 owner/active 判定见 Classify/TryAdmit。</summary>
    private static int KindOf(Payable payable)
    {
        if (payable.TryCast<PayableWorkshopBarrel>() != null) return RoleCatapultBarrel;
        var component = payable.TryCast<PayableComponent>();
        if (component != null && FindTower(component) != null) return RoleFireTowerAmmo;
        return -1;
    }

    /// <summary>admit：身份/层级/归属齐全，可采样；defer：原生 Awake-before-parent 未完成；drop：非本类目标或已死。</summary>
    private static Verdict TryAdmit(Payable payable, int role)
    {
        GameObject go = payable.gameObject;
        if (go == null) return Verdict.Drop;
        IntPtr goPtr = go.Pointer;
        if (goPtr == IntPtr.Zero) return Verdict.Defer;
        if (go.scene.handle != _scene) return Verdict.Drop;                  // 不属于当前世界
        Transform t = go.transform;
        if (t == null) return Verdict.Drop;
        if (!t.IsChildOf(_gameLayer)) return Verdict.Defer;                  // 原生 Awake 早于 parent 挂接
        if (_byGoPtr.ContainsKey(goPtr)) return Verdict.Drop;                // 已入册（ptr dedup）

        if (role == RoleCatapultBarrel)
        {
            var site = payable.TryCast<PayableWorkshopBarrel>();
            if (site == null) return Verdict.Drop;
            if (site.catapultCounterpart == null) return Verdict.Defer;      // 暂缺绑定不能发布低库存
            Add(new Target
            {
                Payable = payable, Go = go, GoPtr = goPtr, GoId = go.GetInstanceID(),
                Role = role, Site = site
            });
            return Verdict.Admit;
        }

        var component = payable.TryCast<PayableComponent>();
        if (component == null) return Verdict.Drop;
        FireTower tower = FindTower(component);
        if (tower == null) return Verdict.Drop;                              // 非火塔名下的 PayableComponent
        PayableComponent canonical = tower._payableComponent;
        if (canonical != null && (canonical.Pointer != component.Pointer
            || canonical.GetInstanceID() != component.GetInstanceID()))
            return Verdict.Drop;                                             // 不是火塔持有的那份
        if (component._owner == null) return Verdict.Defer;                  // Awake/Init 尚未完成
        if (!OwnsTower(component, tower)) return Verdict.Drop;               // owner 必须精确是这座塔
        if (!tower.gameObject.activeInHierarchy) return Verdict.Drop;                   // 非激活塔不是可买目标
        Add(new Target
        {
            Payable = payable, Go = go, GoPtr = goPtr, GoId = go.GetInstanceID(),
            Role = role, Tower = tower, ActorPtr = tower.Pointer, ActorId = tower.GetInstanceID()
        });
        return Verdict.Admit;
    }

    private static void Add(Target target)
    {
        _targets.Add(target);
        _byGoPtr[target.GoPtr] = target;
        _memberRevision++;
    }

    private static void FlushPending()
    {
        if (_pending.Count == 0) return;
        _scratch.Clear();
        foreach (KeyValuePair<IntPtr, Payable> pair in _pending)
        {
            Payable payable = pair.Value;
            if (payable == null) { _scratch.Add(pair.Key); continue; }
            int role = KindOf(payable);
            if (role < 0) { _scratch.Add(pair.Key); continue; }
            if (TryAdmit(payable, role) == Verdict.Defer) continue;   // 保留候选，下轮继续等归属
            _scratch.Add(pair.Key);
        }
        for (int i = 0; i < _scratch.Count; i++) _pending.Remove(_scratch[i]);
    }

    // === 采样：只读缓存目标的少量弹药字段 ===

    /// <summary>返回 false = 存在身份失效目标（本轮不得发布）；目标会先移出缓存再请求有界重建。</summary>
    private static bool Sample()
    {
        _seenBarrels.Clear();
        _seenCatapults.Clear();
        int barrels = 0, towers = 0;
        bool stable = true;
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            Target target = _targets[i];
            int count;
            if (!TrySample(target, out count))
            {
                RemoveTarget(target);
                stable = false;
                continue;
            }
            target.Count = count;
            if (target.Role == RoleCatapultBarrel) barrels += target.IslandCount; else towers += count;
        }
        _counts[RoleCatapultBarrel] = barrels;
        _counts[RoleFireTowerAmmo] = towers;
        return stable;
    }

    private static bool TrySample(Target target, out int count)
    {
        count = 0;
        GameObject go = target.Go;
        if (target.Payable == null || go == null) return false;
        if (!go.activeInHierarchy) return false;                          // 本体必须仍激活
        if (go.Pointer != target.GoPtr || go.GetInstanceID() != target.GoId) return false;  // 池化复用
        if (go.scene.handle != _scene) return false;
        Transform t = go.transform;
        if (t == null || !t.IsChildOf(_gameLayer)) return false;

        if (target.Role == RoleCatapultBarrel)
        {
            var site = target.Payable.TryCast<PayableWorkshopBarrel>();
            if (site == null) return false;
            if (site.catapultCounterpart == null) return false;           // 对口链路断了
            count = SiteAmmo(site, out int uniqueCount);
            target.IslandCount = uniqueCount;
            return true;
        }

        FireTower tower = target.Tower;
        if (tower == null || !tower.gameObject.activeInHierarchy) return false;
        if (tower.Pointer != target.ActorPtr || tower.GetInstanceID() != target.ActorId) return false;
        var component = target.Payable.TryCast<PayableComponent>();
        if (component == null || !OwnsTower(component, tower)) return false;
        count = Math.Max(0, tower._fireJarsActiveNum);   // 唯一真值：_projectile 只是 spoon 预览
        return true;
    }

    /// <summary>
    /// 站点本地未用弹药 = 存活投石车的队列 + spoon 上那一发 + 归属本站点的活跃运输桶。
    /// 投石车无效（未建/被毁待重建）时不放弃站点：运输桶仍按归属计数，队列/装填为 0。
    /// </summary>
    private static int SiteAmmo(PayableWorkshopBarrel site, out int uniqueCount)
    {
        uniqueCount = 0;
        if (site == null) return 0;
        PayableWorkshop counterpart = site.catapultCounterpart;
        Catapult catapult = counterpart != null ? counterpart.catapult : null;
        int count = 0;
        if (LiveCatapult(catapult))
        {
            count += Math.Max(0, catapult.queuedOilBarrels);
            if (CountsLoadedBarrel(catapult)) count++;
        }
        int transport = CountTransport(site);
        uniqueCount = transport + (catapult != null && _seenCatapults.Add(catapult.Pointer) ? count : 0);
        return count + transport;
    }

    private static bool LiveCatapult(Catapult catapult)
    {
        if (catapult == null) return false;
        GameObject go = catapult.gameObject;
        if (go == null || !go.activeInHierarchy) return false;
        if (go.scene.handle != _scene) return false;
        Transform t = catapult.transform;
        return t != null && t.IsChildOf(_gameLayer);
    }

    /// <summary>
    /// spoon 上仍真实装载、尚未发射的油弹：弹体自身 active + 带 OilBarrel 组件 +
    /// parent 精确等于 spoon + 当前场景/层。不读 currentLaunchableIsOil，也不按 _fireAttacks 排除。
    /// </summary>
    private static bool CountsLoadedBarrel(Catapult catapult)
    {
        GameObject projectile = catapult._projectile;
        if (projectile == null || !projectile.activeInHierarchy) return false;
        if (projectile.GetComponent<OilBarrel>() == null) return false;   // 巨石不是油弹
        Transform spoon = catapult.spoon;
        if (spoon == null) return false;
        Transform p = projectile.transform;
        if (p == null) return false;
        Transform parent = p.parent;
        if (parent == null || parent.Pointer != spoon.Pointer || parent.GetInstanceID() != spoon.GetInstanceID())
            return false;                                                // 已离开 spoon（发射）不算
        if (projectile.scene.handle != _scene) return false;
        return p.IsChildOf(_gameLayer);
    }

    /// <summary>
    /// 运输中的桶：归属必然属于本站点（_parentWorkshop 精确相等），对象 active、同场景、
    /// 在世界层或原生生成容器内；含 _delivered=true 尚未 QueueOilBarrel 的一秒窗口。
    /// 归属/有效性证明通过后才做 ptr 去重，避免错误站点的陈旧引用抢键漏数。
    /// </summary>
    private static int CountTransport(PayableWorkshopBarrel site)
    {
        var barrels = site._activeBarrels;
        if (barrels == null) return 0;
        Transform spawnParent = SpawnContainer(site);
        int siteId = site.GetInstanceID();
        int count = 0;
        foreach (RollableOilBarrel barrel in barrels)
        {
            if (barrel == null) continue;
            GameObject go = barrel.gameObject;
            if (go == null || !go.activeInHierarchy) continue;                  // 池中 active=false
            if (go.scene.handle != _scene) continue;
            Transform t = barrel.transform;
            if (t == null) continue;
            // 同世界层；或正好挂在原生生成容器（Pool.Spawn 的 parent = payable.transform.parent.parent）
            if (!t.IsChildOf(_gameLayer) && !SameRef(t.parent, spawnParent)) continue;

            PayableWorkshopBarrel owner = barrel._parentWorkshop;
            if (owner == null || owner.Pointer != site.Pointer || owner.GetInstanceID() != siteId) continue;

            IntPtr ptr = barrel.Pointer;
            if (ptr == IntPtr.Zero || !_seenBarrels.Add(ptr)) continue;         // 跨站点 ptr 去重
            count++;
        }
        return count;
    }

    /// <summary>原生 Pool.Spawn 的 parent：payable.transform.parent.parent。</summary>
    private static Transform SpawnContainer(PayableWorkshopBarrel site)
    {
        Transform t = site.transform;
        Transform parent = t != null ? t.parent : null;
        return parent != null ? parent.parent : null;
    }

    /// <summary>轻量身份复核（不做 AllPayables 扫描）：与采样同一套 active/owner/指针/context 判据。</summary>
    private static bool ValidateTarget(Target target)
    {
        GameObject go = target.Go;
        if (target.Payable == null || go == null || !go.activeInHierarchy) return false;
        if (go.Pointer != target.GoPtr || go.GetInstanceID() != target.GoId) return false;
        if (go.scene.handle != _scene) return false;
        Transform t = go.transform;
        if (t == null || !t.IsChildOf(_gameLayer)) return false;
        if (target.Role == RoleCatapultBarrel)
            return target.Payable.TryCast<PayableWorkshopBarrel>() is PayableWorkshopBarrel site && site.catapultCounterpart != null;
        FireTower tower = target.Tower;
        if (tower == null || !tower.gameObject.activeInHierarchy) return false;
        if (tower.Pointer != target.ActorPtr || tower.GetInstanceID() != target.ActorId) return false;
        return target.Payable.TryCast<PayableComponent>() is PayableComponent component && OwnsTower(component, tower);
    }

    private static FireTower FindTower(PayableComponent component)
    {
        GameObject go = component.gameObject;
        return go != null ? go.GetComponent<FireTower>() : null;
    }

    /// <summary>owner 精确身份：只认 _owner.Pointer == tower.Pointer（both Il2CppObjectBase）。</summary>
    private static bool OwnsTower(PayableComponent component, FireTower tower)
    {
        IPayableComponentOwner owner = component._owner;
        return owner != null && owner.Pointer == tower.Pointer;
    }

    private static bool SameRef(Transform a, Transform b)
        => a != null && b != null && a.Pointer == b.Pointer;

    private static void Publish()
    {
        bool changed = _publishedRevision != _memberRevision;
        for (int r = 0; r < RoleCount; r++)
        {
            if (_published[r] == _counts[r]) continue;
            _published[r] = _counts[r];
            changed = true;
        }
        _publishedRevision = _memberRevision;
        if (changed) Version++;
    }

    private static void RemoveTarget(Target target)
    {
        if (!_byGoPtr.TryGetValue(target.GoPtr, out Target current) || !ReferenceEquals(current, target)) return;
        _byGoPtr.Remove(target.GoPtr);
        _targets.Remove(target);
        target.Payable = null;
        target.Go = null;
        target.Site = null;
        target.Tower = null;
        target.Count = 0;
        _memberRevision++;
    }

    // === 生命周期 ===

    private static void ClearTargets()
    {
        _targets.Clear();
        _byGoPtr.Clear();
        _pending.Clear();
        _registered.Clear();
        Array.Clear(_counts, 0, RoleCount);
        _memberRevision++;
    }

    private static void Clear()
    {
        bool hadSnapshot = IsReady || _hasContext || _targets.Count != 0
            || _published[RoleCatapultBarrel] != 0 || _published[RoleFireTowerAmmo] != 0;
        ClearTargets();
        _managers = null;
        _gameLayer = null;
        _kingdom = _world = _layer = IntPtr.Zero;
        _scene = 0;
        _hasContext = false;
        _snapshotLogged = false;
        _registryDirty = true;
        _latched = false;
        _failures = 0;
        _nextRead = _retryAfter = 0;
        ClientUnavailable = false;
        IsReady = false;
        if (hadSnapshot)
        {
            Array.Clear(_published, 0, RoleCount);
            _publishedRevision = _memberRevision;
            Version++;
        }
    }

    private static void ClearUnavailableContext()
    {
        if (_hasContext || IsReady || _targets.Count != 0) Clear();
    }

    private static void RequestRecovery()
    {
        IsReady = false;
        _registryDirty = true;
    }
}

/// <summary>
/// 唯一原生钩子：PayableManager.AddPayable / RemovePayable 的 Postfix 只置 registry dirty
/// （成员集合下一轮刷新才重读；回调内不读字段、不写状态、不发 RPC）。
/// 两个独立精准目标 wrapper；operator 已审计这两个 actual native long target 的实际地址匹配。
/// </summary>
internal static class SiegeAmmoCountsHooks
{
    [HarmonyPatch(typeof(PayableManager), nameof(PayableManager.AddPayable))]
    internal static class PayableManager_AddPayable
    {
        [HarmonyPostfix]
        internal static void Postfix() => SiegeAmmoCounts.HookRegistryChanged();
    }

    [HarmonyPatch(typeof(PayableManager), nameof(PayableManager.RemovePayable))]
    internal static class PayableManager_RemovePayable
    {
        [HarmonyPostfix]
        internal static void Postfix() => SiegeAmmoCounts.HookRegistryChanged();
    }
}
