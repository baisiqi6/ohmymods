using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 职业/库存事件计数缓存（自动补货只读数据源）。事件驱动增量名册：
/// Kingdom.AddCharacter/RemoveCharacter + Damageable.OnDeath 维护活体数；
/// PayableShop.AddItem + ShopPlanner 生命周期维护库存；Baker.TryEatBread 维护晋升中数。无周期遍历 /
/// FindObjects / Resources 扫描；seed 仅在首次启用 / 王国·世界·层上下文变化 /
/// enabledMask 变化 / 一次性异常恢复时对 kingdom.characters 做一次枚举。
/// 索引 0=Worker,1=Archer,2=Ninja,3=Berserker,4=Peasant（Ninja 含 _isFisher；Pikeman 无
/// Ninja 组件不计；Berserker leader 同计）。Unity 主线程模型：无锁无 Volatile。
/// 订阅/读取失败一律 fail-closed：一次恢复 reseed，仍失败则 latch 不可用，
/// 直到新上下文/配置或新相关事件，绝不静默发布低计数。
/// </summary>
internal static class AutoRestockCounts
{
    private const int RoleWorker = 0;
    private const int RoleArcher = 1;
    private const int RoleNinja = 2;
    private const int RoleBerserker = 3;
    private const int RolePeasant = 4;
    private const int RoleCount = 5;

    // pending 角色最多重试的 Refresh 轮数（Pool.Spawn 后 parent/gameLayer 尚未就绪）
    private const int MaxPendingAttempts = 6;

    private sealed class ActorEntry
    {
        public IntPtr Ptr;              // Character native 指针，池化复用后旧 entry 失效
        public Character Ref;           // 强引用 root（连同 delegate 一起防 GC）
        public Damageable Damageable;
        public Damageable.DeathEvent DeathHandler; // root 精确 delegate 身份供 += / -=
        public int Role;
        public bool Counted;
        public bool Valid;              // false = 已移除/作废，残留回调不得生效
    }

    private sealed class ToolSub
    {
        public IntPtr ItemPtr;
        public Droppable Item;          // 强引用 root delegate
        public Il2CppSystem.Action<Droppable> OnPickedUp;
        public Il2CppSystem.Action<Droppable> OnDisabled;
    }

    // A successful native bread pickup is capacity only while that exact Beggar
    // remains in its native promotion wait. No elapsed-time or purchase credits.
    private sealed class IncomingEntry
    {
        public Character Character;
        public IntPtr CharacterPtr;
        public int CharacterId;
        public Beggar Beggar;
        public IntPtr BeggarPtr;
        public int BeggarId;
        public int ConsumedFrame;
    }

    private sealed class ShopEntry
    {
        public IntPtr GoPtr;            // 商店 GameObject 指针，dedup 键
        public PayableShop Shop;
        public int Role;
        public bool Valid;              // false = 已作废，回调按引用相等拒绝
        public int Stock;
        public readonly List<ToolSub> Subs = new List<ToolSub>();
    }

    // === 状态（Unity 主线程访问；回调只置脏标志 / 请求恢复） ===
    private static bool _active;                    // 任一职业开关 on 且上下文有效
    private static int _enabledMask;                // bit0..4 = Worker..Peasant；seed 前赋值
    private static IntPtr _kingdomPtr;              // kingdom.Pointer（全路径统一用 .Pointer，非 gameObject.Pointer）
    private static IntPtr _worldPtr;
    private static IntPtr _gameLayerPtr;
    private static int _sceneHandle;
    private static Transform _gameLayer;            // 上下文缓存，避免每帧取
    private static Managers _managers;
    private static IntPtr _shopPlannerPtr;
    private static bool _seeded;                    // 当前上下文+mask 已 seed
    private static int _seededMask;
    private static bool _recoveryPending;           // 本次事故允许一次恢复 reseed
    private static int _recoveryAttempts;           // 连续恢复尝试计数，超限 latch
    private static bool _latched;                   // 恢复仍失败：不可用，直到新上下文/配置/相关事件
    private static bool _cacheUsable;

    private static readonly Dictionary<IntPtr, ActorEntry> _tracked = new Dictionary<IntPtr, ActorEntry>();
    private static readonly Dictionary<IntPtr, Character> _pending = new Dictionary<IntPtr, Character>(); // 强引用
    private static readonly Dictionary<IntPtr, int> _pendingAttempts = new Dictionary<IntPtr, int>();
    private static readonly Dictionary<IntPtr, ShopEntry> _shops = new Dictionary<IntPtr, ShopEntry>();
    private static readonly Dictionary<IntPtr, IncomingEntry> _incoming = new Dictionary<IntPtr, IncomingEntry>();
    private static readonly HashSet<IntPtr> _dirtyShops = new HashSet<IntPtr>(); // 稳态 Refresh 零遍历
    private static bool _shopRegistryDirty = true;  // 归 RebuildShopRegistry / 上下文变化所有，ClearShops 不动它
    private static readonly int[] _live = new int[RoleCount];
    private static readonly int[] _stock = new int[RoleCount];
    private static readonly List<PayableShop> _shopsCache = new List<PayableShop>();

    // scratch：不在 foreach 中直接改集合
    private static readonly List<IntPtr> _scratchA = new List<IntPtr>();
    private static readonly List<IntPtr> _scratchB = new List<IntPtr>();

    private static long _version;

    // === 公共 API ===

    public static long Version => _version;

    /// <summary>当前已注册且去重的工具商店缓存，仅供采购服务借读；getter 不做任何扫描。</summary>
    public static List<PayableShop> Shops => _shopsCache;

    public static int LiveCount(int role)
        => (uint)role < RoleCount && _cacheUsable && (_enabledMask & (1 << role)) != 0 ? _live[role] : 0;

    public static int StockCount(int role)
        => (uint)role < RoleCount && _cacheUsable && (_enabledMask & (1 << role)) != 0 ? _stock[role] : 0;

    public static int IncomingCount(int role)
        => role == RolePeasant && _cacheUsable && (_enabledMask & (1 << role)) != 0 ? _incoming.Count : 0;

    /// <summary>
    /// 主调度（税收官 taxTick）调用：核 context → cheap 读配置 → 必要时 seed/恢复 →
    /// flush pending / dirty 商店及少量晋升中角色。稳态不枚举完整名册或读取商店库存。
    /// </summary>
    public static bool Refresh(Managers managers)
    {
        try
        {
            Kingdom kingdom; World world; Transform gameLayer;
            if (!ValidContext(managers, out kingdom, out world, out gameLayer))
            {
                if (_active || _tracked.Count > 0 || _shops.Count > 0) ClearAll(); // 清一次，不留订阅
                _cacheUsable = false;
                return false;
            }

            int mask = ReadEnabledMask();
            if (mask == 0 || !IsEnabled())
            {
                if (_active || _tracked.Count > 0 || _shops.Count > 0) ClearAll();
                _cacheUsable = false;
                return false;
            }
            _active = true;
            _managers = managers;
            IntPtr plannerPtr = managers.shopPlanner != null ? managers.shopPlanner.Pointer : IntPtr.Zero;
            if (_shopPlannerPtr != plannerPtr)
            {
                _shopPlannerPtr = plannerPtr;
                _shopRegistryDirty = true;
            }

            // 上下文身份：统一 kingdom.Pointer；世界/层/场景任一变化即重 seed
            IntPtr kingdomPtr = kingdom.Pointer;
            IntPtr worldPtr = world.Pointer;
            IntPtr layerPtr = gameLayer.Pointer;
            int sceneHandle = gameLayer.gameObject.scene.handle;
            bool contextChanged = kingdomPtr != _kingdomPtr || worldPtr != _worldPtr
                || layerPtr != _gameLayerPtr || sceneHandle != _sceneHandle;
            if (contextChanged)
            {
                ClearIncoming(); // native identities belong to one world/layer only
                _kingdomPtr = kingdomPtr;
                _worldPtr = worldPtr;
                _gameLayerPtr = layerPtr;
                _sceneHandle = sceneHandle;
                _gameLayer = gameLayer;
                _seeded = false;
                _shopRegistryDirty = true;
                _latched = false; // 新上下文解除 latch
                _recoveryAttempts = 0;
            }
            if (mask != _seededMask)
            {
                _seededMask = mask; // remember the requested mask even when its first seed throws
                _seeded = false; // 配置变化（非周期扫描）可重 seed 一次
                _shopRegistryDirty = true;
                _latched = false;
                _recoveryAttempts = 0;
            }
            if (_latched)
            {
                _cacheUsable = false;
                return false; // 恢复失败：不无限 reseed，直到上述任一事件解锁
            }

            if (!_seeded || _recoveryPending)
            {
                // 有界恢复：每次事故至多一次 reseed，连续失败（3 次）latch 到新事件解锁
                _recoveryAttempts++;
                if (_recoveryAttempts >= 3)
                {
                    _latched = true;
                    _cacheUsable = false;
                    return false;
                }
                _recoveryPending = false;
                try
                {
                    Reseed(managers, kingdom, mask);
                    _seeded = true;
                    _seededMask = mask;
                }
                catch (Exception)
                {
                    _cacheUsable = false;
                    _recoveryPending = true; // 下轮再试，超限即 latch
                    return false;
                }
            }

            FlushPending();
            ProcessIncoming();
            ProcessDirtyShops(managers);
            if (_recoveryPending || _pending.Count > 0)
            {
                _cacheUsable = false; // seed/flush 中途请求恢复：本轮不发布
                return false;
            }
            _cacheUsable = true;
            _recoveryAttempts = 0;
            return true;
        }
        catch (Exception)
        {
            // fail-closed：不静默保留可信计数；下轮一次恢复 reseed，超限 latch
            _cacheUsable = false;
            _seeded = false;
            _recoveryPending = true;
            return false;
        }
    }

    /// <summary>清空全部 tracked 成员、事件订阅、计数与上下文身份（卸载/重置用）。</summary>
    public static void Reset()
    {
        ClearAll();
        _kingdomPtr = _worldPtr = _gameLayerPtr = IntPtr.Zero;
        _sceneHandle = 0;
        _gameLayer = null;
        _managers = null;
        _shopPlannerPtr = IntPtr.Zero;
        _seededMask = 0;
        _version++;
    }

    // The existing plugin-wide Harmony.PatchAll installs these patch classes once.

    // === 配置读取（cheap：5 个 bool + 总开关，无 SettingChanged 订阅） ===

    private static bool IsEnabled() => ModConfig.Enabled == null || ModConfig.Enabled.Value;

    private static int ReadEnabledMask()
    {
        int mask = 0;
        if (On(ModConfig.AutoRestockWorkersEnabled)) mask |= 1 << RoleWorker;
        if (On(ModConfig.AutoRestockArchersEnabled)) mask |= 1 << RoleArcher;
        if (On(ModConfig.AutoRestockNinjasEnabled)) mask |= 1 << RoleNinja;
        if (On(ModConfig.AutoRestockBerserkersEnabled)) mask |= 1 << RoleBerserker;
        if (On(ModConfig.AutoRestockPeasantsEnabled)) mask |= 1 << RolePeasant;
        return mask;
    }

    private static bool On(BepInEx.Configuration.ConfigEntry<bool> e) => e != null && e.Value;

    // === 上下文 ===

    private static bool ValidContext(Managers managers, out Kingdom kingdom, out World world, out Transform gameLayer)
    {
        kingdom = null; world = null; gameLayer = null;
        if (managers == null || managers.kingdom == null || managers.world == null) return false;
        kingdom = managers.kingdom;
        world = managers.world;
        gameLayer = world.gameLayer;
        return gameLayer != null;
    }

    // === 名册：seed / pending / 事件 ===

    private static void Reseed(Managers managers, Kingdom kingdom, int mask)
    {
        _enabledMask = mask; // seed 前赋值（bug 修复：此前从未赋值）
        ClearTracked();
        ClearShops();
        Array.Clear(_live, 0, RoleCount);
        Array.Clear(_stock, 0, RoleCount);
        if ((mask & (1 << RolePeasant)) == 0) ClearIncoming();

        // 唯一一次全量枚举：原生 kingdom.characters（HashSet，非场景 Find）
        if (kingdom._characters == null) throw new InvalidOperationException("Kingdom roster not ready");
        foreach (Character c in kingdom._characters)
        {
            if (c == null) continue;
            TryAdmit(c);
            // This is the existing, bounded roster seed, never a periodic scan.
            // Preserve a just-consumed entry's frame grace across recovery and
            // reconstruct native pending promotions after a settings toggle.
            if ((mask & (1 << RolePeasant)) != 0)
            {
                Beggar beggar = c.GetComponent<Beggar>();
                if (beggar != null && beggar.isActiveAndEnabled && beggar._isEating && IsLiveCharacter(c))
                    AdmitIncoming(c, beggar);
            }
        }

        RebuildShopRegistry(managers); // mask/epoch/world 变化时商店一并重建（修复旧 ShopEntry epoch 失配）
        _version++;
    }

    private static void TryAdmit(Character c)
    {
        int role = Classify(c); // 分类异常会请求恢复，不静默吞
        if (role < 0 || (_enabledMask & (1 << role)) == 0) return; // off 职业不占名册
        if (!IsLiveCharacter(c))
        {
            // A known role can be registered before Pool.Spawn attaches the final parent.
            if (c != null && c.gameObject.activeInHierarchy && c._damageable != null && !c._damageable.isDead)
            { _pending[c.Pointer] = c; _pendingAttempts[c.Pointer] = 0; }
            return;
        }

        IntPtr ptr = c.Pointer;
        if (_tracked.ContainsKey(ptr)) return;
        var entry = new ActorEntry { Ptr = ptr, Ref = c, Role = role, Counted = true, Valid = true };
        _tracked[ptr] = entry;
        _live[role]++;
        _version++; // 新入册即版本变化

        Damageable d = c._damageable != null ? c._damageable : c.GetComponent<Damageable>();
        if (d != null)
        {
            entry.Damageable = d;
            // IL2CPP delegate：显式类型 + 闭包持 entry（强 root）；保存精确身份供 -=
            entry.DeathHandler = (Action<GameObject>)(_ => OnCharacterDeath(entry));
            d.OnDeath += entry.DeathHandler;
        }
    }

    /// <summary>角色分类：0..4；Beggar 不计活农民。Pikeman 无 Ninja 组件不计。</summary>
    private static int Classify(Character c)
    {
        try
        {
            var go = c.gameObject;
            if (go.GetComponent<Berserker>() != null) return RoleBerserker;
            if (go.GetComponent<Ninja>() != null) return RoleNinja;       // 含 _isFisher 白天样式
            if (go.GetComponent<Archer>() != null) return RoleArcher;     // 含随从/塔上
            if (go.GetComponent<Worker>() != null) return RoleWorker;
            if (go.GetComponent<Peasant>() != null && go.GetComponent<Beggar>() == null) return RolePeasant;
            return -1;
        }
        catch (Exception)
        {
            RequestRecovery(); // 分类失败必须暴露，不能吞掉伪装未知职业
            return -1;
        }
    }

    /// <summary>存活判定：damageable 必须非空 + active + 未死 + 属于当前 gameLayer/场景。</summary>
    private static bool IsLiveCharacter(Character c)
    {
        try
        {
            if (c == null || !c.gameObject.activeInHierarchy) return false;
            if (c.gameObject.scene.handle != _sceneHandle) return false;
            if (_gameLayer != null && !c.transform.IsChildOf(_gameLayer)) return false;
            Damageable d = c._damageable != null ? c._damageable : c.GetComponent<Damageable>();
            if (d == null) throw new InvalidOperationException("Profession damageable not ready");
            return !d.isDead;
        }
        catch (Exception)
        {
            RequestRecovery();
            return false;
        }
    }

    private static void FlushPending()
    {
        if (_pending.Count == 0) return;
        _scratchA.Clear(); // admit
        _scratchB.Clear(); // drop
        List<IntPtr> retry = null;
        foreach (KeyValuePair<IntPtr, Character> kv in _pending)
        {
            Character c = kv.Value;
            if (c == null) { _scratchB.Add(kv.Key); continue; }

            int role = Classify(c);
            if (role < 0) { _scratchB.Add(kv.Key); continue; }              // 非目标职业立即丢
            if ((_enabledMask & (1 << role)) == 0) { _scratchB.Add(kv.Key); continue; } // off 职业立即丢

            if (IsLiveCharacter(c))
            {
                _scratchA.Add(kv.Key);
            }
            else if (_pendingAttempts[kv.Key] + 1 >= MaxPendingAttempts)
            {
                _scratchB.Add(kv.Key);
                // Do not publish an undercount when a current roster member stays unresolved.
                RequestRecovery();
            }
            else
            {
                (retry ??= new List<IntPtr>()).Add(kv.Key);
            }
        }

        foreach (IntPtr ptr in _scratchA)
        {
            Character c = _pending[ptr];
            DropPending(ptr);
            TryAdmit(c); // 成功入册时内部 _version++
        }
        foreach (IntPtr ptr in _scratchB) DropPending(ptr);
        if (retry != null)
            foreach (IntPtr ptr in retry) _pendingAttempts[ptr] = _pendingAttempts[ptr] + 1;
    }

    private static void DropPending(IntPtr ptr)
    {
        _pending.Remove(ptr);
        _pendingAttempts.Remove(ptr);
    }

    // === Bread already consumed, native promotion still pending ===

    internal static void HookBreadConsumed(Baker baker, Beggar beggar, bool consumed)
    {
        if (!consumed || !_active || baker == null || beggar == null) return;
        try
        {
            if (!IsEnabled() || !On(ModConfig.AutoRestockPeasantsEnabled)
                || (_enabledMask & (1 << RolePeasant)) == 0) return;
            Kingdom kingdom; World world; Transform layer;
            if (!ValidContext(_managers, out kingdom, out world, out layer)
                || kingdom.Pointer != _kingdomPtr || world.Pointer != _worldPtr
                || layer.Pointer != _gameLayerPtr || layer.gameObject.scene.handle != _sceneHandle) return;
            GameObject go = baker.gameObject;
            if (go == null || !go.activeInHierarchy || go.scene.handle != _sceneHandle
                || !go.transform.IsChildOf(_gameLayer)) return;
            PayableShop shop = go.GetComponent<PayableShop>();
            if (shop == null || ClassifyShop(shop) != RolePeasant) return;
            Character character = beggar._character;
            if (character == null) throw new InvalidOperationException("Consumed bread without a character");
            if (!beggar.isActiveAndEnabled || !IsLiveCharacter(character)) return;
            AdmitIncoming(character, beggar);
            WakeRecovery();
        }
        catch (Exception) { RequestRecovery(); }
    }

    private static void AdmitIncoming(Character character, Beggar beggar)
    {
        Beggar current = character.GetComponent<Beggar>();
        Character owner = beggar._character;
        if (current == null || current.Pointer != beggar.Pointer
            || current.GetInstanceID() != beggar.GetInstanceID() || owner == null
            || owner.Pointer != character.Pointer || owner.GetInstanceID() != character.GetInstanceID())
            throw new InvalidOperationException("Bread promotion actor identity unresolved");
        IntPtr ptr = character.Pointer;
        if (_incoming.TryGetValue(ptr, out IncomingEntry existing)
            && existing.CharacterId == character.GetInstanceID()
            && existing.BeggarPtr == beggar.Pointer && existing.BeggarId == beggar.GetInstanceID()) return;
        _incoming[ptr] = new IncomingEntry
        {
            Character = character, CharacterPtr = ptr, CharacterId = character.GetInstanceID(),
            Beggar = beggar, BeggarPtr = beggar.Pointer, BeggarId = beggar.GetInstanceID(),
            ConsumedFrame = Time.frameCount
        };
        _version++;
    }

    private static void ProcessIncoming()
    {
        if (_incoming.Count == 0) return;
        _scratchA.Clear();
        foreach (KeyValuePair<IntPtr, IncomingEntry> pair in _incoming)
        {
            IncomingEntry entry = pair.Value;
            Character character = entry.Character;
            Beggar beggar = entry.Beggar;
            if (character == null || beggar == null
                || character.Pointer != entry.CharacterPtr || character.GetInstanceID() != entry.CharacterId
                || beggar.Pointer != entry.BeggarPtr || beggar.GetInstanceID() != entry.BeggarId
                || !beggar.isActiveAndEnabled || !IsLiveCharacter(character))
            { _scratchA.Add(pair.Key); continue; }
            Beggar current = character.GetComponent<Beggar>();
            Character owner = beggar._character;
            if (current == null || current.Pointer != entry.BeggarPtr || current.GetInstanceID() != entry.BeggarId
                || owner == null || owner.Pointer != entry.CharacterPtr || owner.GetInstanceID() != entry.CharacterId)
            { _scratchA.Add(pair.Key); continue; }
            // TryEatBread's postfix runs before its caller sets _isEating. Keep
            // only that same frame as grace; subsequent state comes from native.
            if (!beggar._isEating && Time.frameCount != entry.ConsumedFrame) _scratchA.Add(pair.Key);
        }
        foreach (IntPtr ptr in _scratchA) RemoveIncoming(ptr);
    }

    private static void RemoveIncoming(IntPtr ptr)
    {
        if (_incoming.Remove(ptr)) _version++;
    }

    private static void ClearIncoming()
    {
        if (_incoming.Count == 0) return;
        _incoming.Clear();
        _version++;
    }

    // --- hook 回调（快速路径：未启用直接返回；异常请求恢复而非被 Refresh 覆盖） ---

    internal static void HookAddCharacter(Kingdom kingdom, Character character)
    {
        if (!_active || kingdom == null || character == null) return;
        try
        {
            if (kingdom.Pointer != _kingdomPtr) return; // 仅当前 Kingdom
            IntPtr ptr = character.Pointer;
            if (_tracked.ContainsKey(ptr) || _pending.ContainsKey(ptr)) return;
            // Pool.Spawn 可先于最终 parent 挂 gameLayer：推迟到 Refresh 验证
            _pending[ptr] = character; // 强引用防 GC
            _pendingAttempts[ptr] = 0;
            WakeRecovery(); // new roster event
        }
        catch (Exception) { RequestRecovery(); }
    }

    internal static void HookRemoveCharacter(Character character)
    {
        if (!_active || character == null) return;
        try
        {
            IntPtr ptr = character.Pointer;
            DropPending(ptr);
            if (_incoming.TryGetValue(ptr, out IncomingEntry incoming)
                && incoming.CharacterId == character.GetInstanceID()) RemoveIncoming(ptr);
            if (_tracked.TryGetValue(ptr, out ActorEntry entry))
            {
                RetireEntry(entry); // 幂等：Valid/Counted 置位后重复事件、同指针复用均不再生效
                _version++;
            }
        }
        catch (Exception) { RequestRecovery(); }
    }

    private static void OnCharacterDeath(ActorEntry entry)
    {
        if (!_active || entry == null || !entry.Valid || !entry.Counted) return;
        // 引用相等校验字典当前 entry：同指针池化复用出的新 entry 不受旧回调影响
        if (!_tracked.TryGetValue(entry.Ptr, out ActorEntry cur) || !ReferenceEquals(cur, entry)) return;
        entry.Counted = false; // 只在真正死亡减一次
        _live[entry.Role]--;
        _version++;
    }

    /// <summary>作废 entry（先断回调生效性再解绑订阅），重复 Remove/同指针复用安全。</summary>
    private static void RetireEntry(ActorEntry entry)
    {
        entry.Valid = false;
        if (entry.Counted) _live[entry.Role]--;
        entry.Counted = false;
        if (entry.Damageable != null && entry.DeathHandler != null)
        {
            try { entry.Damageable.OnDeath -= entry.DeathHandler; } catch { }
        }
        if (_tracked.TryGetValue(entry.Ptr, out ActorEntry cur) && ReferenceEquals(cur, entry))
            _tracked.Remove(entry.Ptr);
    }

    private static void ClearTracked()
    {
        foreach (ActorEntry entry in _tracked.Values)
        {
            entry.Valid = false; entry.Counted = false;
            if (entry.Damageable != null && entry.DeathHandler != null)
                try { entry.Damageable.OnDeath -= entry.DeathHandler; } catch { }
        }
        _tracked.Clear();
        _pending.Clear();
        _pendingAttempts.Clear();
        Array.Clear(_live, 0, RoleCount);
    }

    private static void ClearAll()
    {
        ClearTracked();
        ClearIncoming();
        ClearShops();
        _active = false;
        _cacheUsable = false;
        _seeded = false;
        _recoveryPending = false;
        _recoveryAttempts = 0;
        _latched = false;
        _version++;
    }

    // === 商店与库存 ===

    internal static void HookShopAddItem(PayableShop shop)
    {
        if (!_active || shop == null) return;
        try
        {
            GameObject go;
            try { go = shop.gameObject; } catch { return; }
            if (go == null) return;
            IntPtr goPtr = go.Pointer;
            if (_shops.TryGetValue(goPtr, out ShopEntry entry) && entry.Valid && entry.Shop != null && entry.Shop.Pointer == shop.Pointer)
            {
                _dirtyShops.Add(goPtr); // 下个 Refresh 只重算该店
                WakeRecovery();
                return;
            }
            // 未 tracked：先分类，属于本 mod 关心的角色才重建注册表，否则置之（避免永久 dirty）
            int role = ClassifyShop(shop);
            if (role >= 0 && (_enabledMask & (1 << role)) != 0) _shopRegistryDirty = true;
        }
        catch (Exception) { RequestRecovery(); }
    }

    internal static void HookSetPlacedShop(ShopPlanner planner)
    {
        if (!_active) return;
        try
        {
            if (planner == null || planner.Pointer != _shopPlannerPtr) return;
            _shopRegistryDirty = true; // next refresh reads only registered shops
            WakeRecovery();
        }
        catch (Exception) { RequestRecovery(); }
    }

    private static void ProcessDirtyShops(Managers managers)
    {
        if (_shopRegistryDirty)
        {
            _shopRegistryDirty = false;
            RebuildShopRegistry(managers);
            if (_recoveryPending) return; // 重建已失败并请求恢复：本轮不发布
        }
        if (_dirtyShops.Count == 0) return; // 稳态零遍历
        _scratchA.Clear();
        _scratchA.AddRange(_dirtyShops);
        _dirtyShops.Clear();
        bool stockChanged = false;
        foreach (IntPtr goPtr in _scratchA)
        {
            if (!_shops.TryGetValue(goPtr, out ShopEntry entry) || !entry.Valid) continue;
            int old = entry.Stock;
            if (!UpdateShopStock(entry)) return; // 失败传播：fail-closed
            _stock[entry.Role] += entry.Stock - old;
            if (entry.Stock != old) stockChanged = true;
        }
        if (stockChanged) _version++;
    }

    /// <summary>读 ShopPlanner.shops 原生注册列表（包含面包塔，按 GO 指针 dedup），不做全场查找。</summary>
    private static void RebuildShopRegistry(Managers managers)
    {
        _shopRegistryDirty = false;
        var oldKeys = new HashSet<IntPtr>(_shops.Keys);
        bool membershipChanged = false;
        try
        {
            foreach (ShopEntry e in _shops.Values) RetireShopEntry(e); // 先作废旧 entry（含其工具回调）
            _shops.Clear();
            _dirtyShops.Clear();
            _shopsCache.Clear();
            Array.Clear(_stock, 0, RoleCount);

            var planner = managers != null ? managers.shopPlanner : null;
            var placed = planner != null ? planner.shops : null;
            if (placed == null) throw new InvalidOperationException("Shop registry not ready");

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject go = placed[i];
                if (go == null || !go.activeInHierarchy) continue;
                if (go.scene.handle != _sceneHandle) continue;
                if (_gameLayer != null && !go.transform.IsChildOf(_gameLayer)) continue; // 仅当前层
                IntPtr goPtr = go.Pointer;
                if (_shops.ContainsKey(goPtr)) continue; // dedup
                PayableShop shop = go.GetComponent<PayableShop>();
                if (shop == null) continue;
                int role = ClassifyShop(shop);
                if (role < 0 || (_enabledMask & (1 << role)) == 0) continue; // 只统计开启职业的工具店

                var entry = new ShopEntry { GoPtr = goPtr, Shop = shop, Role = role, Valid = true };
                _shops[goPtr] = entry;
                if (!oldKeys.Contains(goPtr)) membershipChanged = true;
                if (!UpdateShopStock(entry)) return; // 订阅/读数失败传播，统一恢复路径
                _stock[role] += entry.Stock;
                _version++; // 入册即版本变化（即使 0 stock）
            }
            foreach (IntPtr old in oldKeys)
                if (!_shops.ContainsKey(old)) membershipChanged = true;
        }
        catch (Exception)
        {
            RequestRecovery();
            return;
        }
        finally
        {
            _shopsCache.Clear();
            foreach (ShopEntry e in _shops.Values)
                if (e.Valid) _shopsCache.Add(e.Shop);
        }
        if (membershipChanged) _version++; // 集合成员变化即版本变化（即使 stock 全 0）
    }

    /// <summary>四工具沿用 prefab tag；同 GO 的 Baker + 非空 itemPrefab 识别面包店。</summary>
    internal static int ClassifyShop(PayableShop shop)
    {
        try
        {
            Droppable prefab = shop.itemPrefab;
            if (prefab == null) return -1;
            if (prefab.CompareTag("BerserkerTool")) return RoleBerserker;
            if (prefab.CompareTag("Katana")) return RoleNinja;
            if (prefab.CompareTag("Bow")) return RoleArcher;
            if (prefab.CompareTag("Hammer")) return RoleWorker;
            if (shop.gameObject.GetComponent<Baker>() != null) return RolePeasant;
            return -1;
        }
        catch (Exception)
        {
            RequestRecovery();
            return -1;
        }
    }

    /// <summary>
    /// 重算单个 shop：原生 GetItemCount() 返回值即库存真值；遍历 _items 仅为建立事件订阅
    ///（先入 Subs 再订阅，部分失败精确回滚该 sub 的 delegate 并请求恢复）。
    /// </summary>
    private static bool UpdateShopStock(ShopEntry entry)
    {
        UnsubscribeShopTools(entry);
        entry.Stock = 0;
        try
        {
            PayableShop shop = entry.Shop;
            if (shop == null) return true;
            entry.Stock = shop.GetItemCount(); // 原生已按 pickedUp/inactive/距离门清理 _items

            var items = shop._items;
            if (items == null) return true;
            for (int i = 0; i < items.Length; i++)
            {
                Droppable item = items[i];
                if (item == null) continue;
                var sub = new ToolSub { ItemPtr = item.Pointer, Item = item };
                entry.Subs.Add(sub); // 先登记成员身份，回调据此验证
                ShopEntry captured = entry;
                ToolSub capturedSub = sub;
                sub.OnPickedUp = (Action<Droppable>)(_ => OnToolEvent(captured, capturedSub));
                sub.OnDisabled = (Action<Droppable>)(_ => OnToolEvent(captured, capturedSub));
                try
                {
                    item.OnPickedUp += sub.OnPickedUp;
                    item.OnDisabled += sub.OnDisabled;
                }
                catch (Exception)
                {
                    // 部分失败：精确清理已加 delegate，失败传播
                    entry.Subs.Remove(sub);
                    try { item.OnPickedUp -= sub.OnPickedUp; } catch { }
                    try { item.OnDisabled -= sub.OnDisabled; } catch { }
                    RequestRecovery();
                    return false;
                }
            }
            return true;
        }
        catch (Exception)
        {
            RequestRecovery();
            return false;
        }
    }

    private static void OnToolEvent(ShopEntry entry, ToolSub sub)
    {
        if (!_active || entry == null || !entry.Valid || sub == null) return;
        // 精确校验：字典当前 entry 引用相等 + sub 仍在该 entry.Subs（引用成员）。
        // 指针 alone 不防池化复用，成员身份才是准绳。
        if (!_shops.TryGetValue(entry.GoPtr, out ShopEntry cur) || !ReferenceEquals(cur, entry)) return;
        if (!entry.Subs.Contains(sub)) return; // List<ToolSub>.Contains = 引用相等（sealed class）
        _dirtyShops.Add(entry.GoPtr);          // 回调只置脏，重算在 Refresh
        WakeRecovery();
    }

    private static void UnsubscribeShopTools(ShopEntry entry)
    {
        foreach (ToolSub sub in entry.Subs)
        {
            try
            {
                if (sub.Item != null)
                {
                    if (sub.OnPickedUp != null) sub.Item.OnPickedUp -= sub.OnPickedUp;
                    if (sub.OnDisabled != null) sub.Item.OnDisabled -= sub.OnDisabled;
                }
            }
            catch { }
        }
        entry.Subs.Clear();
    }

    private static void RetireShopEntry(ShopEntry entry)
    {
        entry.Valid = false; // 先作废（含池化复用后的旧回调），再解绑
        UnsubscribeShopTools(entry);
    }

    private static void ClearShops()
    {
        foreach (ShopEntry entry in _shops.Values) RetireShopEntry(entry);
        _shops.Clear();
        _dirtyShops.Clear();
        _shopsCache.Clear();
        Array.Clear(_stock, 0, RoleCount);
        // 注意：_shopRegistryDirty 不在此复位/置位——归上下文变化与 RebuildShopRegistry 所有
    }

    // === 失败策略：一次恢复 / latch（fail-closed，绝不静默发布低计数） ===

    private static void WakeRecovery()
    {
        if (_latched) { _latched = false; _recoveryAttempts = 0; _recoveryPending = true; }
    }

    private static void RequestRecovery()
    {
        _cacheUsable = false;
        _recoveryPending = true; // latch 判定统一在 Refresh 的有界恢复计数
    }
}

/// <summary>
/// 7 个 Harmony hook（IL2CPP interop，独立嵌套 patch 类，统一由插件 PatchAll 注册）。
/// 方法存在性对照 game-source/Assembly-CSharp-2.1.0：Kingdom.AddCharacter/RemoveCharacter
/// (L1699/L1705)、PayableShop.AddItem(L196)、ShopPlanner.SetPlacedShop(L291，private)。
/// Baker.TryEatBread(Beggar)、ShopPlanner.AddShop/RemoveShop(GameObject) 已核对 2.4 interop。
/// 实际 native 体/地址匹配由 Operator 编译验签最终审计。
/// </summary>
internal static class AutoRestockCountsHooks
{
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.AddCharacter))]
    internal static class Kingdom_AddCharacter
    {
        [HarmonyPostfix]
        internal static void Postfix(Kingdom __instance, Character __0)
            => AutoRestockCounts.HookAddCharacter(__instance, __0);
    }

    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.RemoveCharacter))]
    internal static class Kingdom_RemoveCharacter
    {
        [HarmonyPrefix]
        internal static void Prefix(Character __0)
            => AutoRestockCounts.HookRemoveCharacter(__0);
    }

    [HarmonyPatch(typeof(PayableShop), nameof(PayableShop.AddItem))]
    internal static class PayableShop_AddItem
    {
        [HarmonyPostfix]
        internal static void Postfix(PayableShop __instance)
            => AutoRestockCounts.HookShopAddItem(__instance);
    }

    [HarmonyPatch(typeof(ShopPlanner), "SetPlacedShop")]
    internal static class ShopPlanner_SetPlacedShop
    {
        [HarmonyPostfix]
        internal static void Postfix(ShopPlanner __instance)
            => AutoRestockCounts.HookSetPlacedShop(__instance);
    }

    [HarmonyPatch(typeof(ShopPlanner), nameof(ShopPlanner.AddShop))]
    internal static class ShopPlanner_AddShop
    {
        [HarmonyPostfix]
        internal static void Postfix(ShopPlanner __instance)
            => AutoRestockCounts.HookSetPlacedShop(__instance);
    }

    [HarmonyPatch(typeof(ShopPlanner), nameof(ShopPlanner.RemoveShop))]
    internal static class ShopPlanner_RemoveShop
    {
        [HarmonyPostfix]
        internal static void Postfix(ShopPlanner __instance)
            => AutoRestockCounts.HookSetPlacedShop(__instance);
    }

    [HarmonyPatch(typeof(Baker), nameof(Baker.TryEatBread))]
    internal static class Baker_TryEatBread
    {
        [HarmonyPostfix]
        internal static void Postfix(Baker __instance, Beggar __0, bool __result)
            => AutoRestockCounts.HookBreadConsumed(__instance, __0, __result);
    }
}
