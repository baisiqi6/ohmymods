using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 自动补货服务（单文件，无自建driver/hook）。
    /// 目标 = 现有职业人数 + 店内待领取道具 + 已预留尚未出货的订单；
    /// 角色 0..5 读取事件维护的 AutoRestockCounts 缓存（5=农夫，含存活农夫与 Scythe 店库存）；
    /// 角色 6=投石车油桶、7=火塔罐 读取 SiegeAmmoCounts 弹药缓存（全岛未用弹药）；
    /// role 8=火枪手，独立读取本世界已绑定职业/枪具快照并使用显式枪铺银行入口。
    /// 各类计数各自就绪：弹药-only 时职业缓存不可用也不阻断弹药；
    /// 复用税收官调度，无周期全角色扫描，订单最多2并发，每帧step最多处理2单。
    /// 经济commit只走 PatchEconomy_Banker.TrySpendForAutoRestock，随后
    /// 原生 Payable.TransactionComplete()（PerformPay→Pay/CreateItem/RollableOilBarrel…）。
    /// 旧店的 slots/_items/maxItems 门只作用于真正的 PayableShop；弹药目标只走
    /// 原生 Payable 门（CanPay/Currency/forceBlock/选中/玩家占用/网络）并由
    /// SiegeAmmoCounts 计数，付款前不碰弹药字段、不造池。
    /// 仅在当前希腊世界生效：Tick/订单上下文/扣款最终口都有 GreekBankScope 闸门，
    /// 其他世界只释放本地 reservation 并把摘要显示为“仅希腊世界生效”。
    /// </summary>
    internal static class PatchEconomy_AutoRestock
    {
        private const int RoleWorker = 0;           // 工匠（锤子店）
        private const int RoleArcher = 1;           // 弓箭手（弓店）
        private const int RoleNinja = 2;            // 忍者（武士刀店）
        private const int RoleBerserker = 3;        // 狂战士（药水店）
        private const int RolePeasant = 4;          // 无业村民（面包房）
        private const int RoleFarmer = 5;           // 农夫（Scythe 店）
        private const int RoleCatapultBarrel = 6;   // 投石车油桶（PayableWorkshopBarrel）
        private const int RoleFireTowerAmmo = 7;    // 火塔罐（FireTower 上的 PayableComponent）
        private const int RoleMusketeer = 8;
        private const int RoleCount = 9;
        private const int MaxOrders = 2;
        private const float ScanInterval = 2f;
        private const float StartDelay = 0.15f;
        private const float CoinInterval = 0.2f;
        private const float FinalDelay = 0.25f;
        private const float StandoffDistance = 1f;
        private const float EntryDistance = 5f;
        private const float DepartureDistance = 4f;
        private const float ApproachSpeed = 3.2f;
        private const float DepartureSpeed = 1.6f;
        private const float MovementTimeout = 8f;
        private const int MinPrice = 1;
        private const int MaxPrice = 100;
        private const int AutoRestockCostMultiplier = 2;

        private static readonly string[] RoleNames =
            { "工人", "弓箭手", "忍者", "狂战士", "无业村民", "农夫", "投石车油桶", "火塔罐", "火枪手" };

        // Approach through FinalWait owns budget/incoming stock. Departing owns only
        // the assistant and shop slot: the debit already happened and is never retried.
        private enum Phase { Approach, StartDelay, SendingCoins, FinalWait, Departing }

        private sealed class Order
        {
            public int Role;
            public GameObject TargetGO;
            public IntPtr TargetPtr;
            public int TargetInstanceId;
            public Payable Target;          // 店或弹药 Payable：唯一的原生付款/购买入口
            public PayableShop Shop;        // 仅 0..5 角色的店视图；弹药目标为 null
            public int NativePrice;
            public int TotalCost => AutoRestockCost(NativePrice);
            public int AssistantIndex;
            public GameObject Assistant;
            public Phase Phase;
            public float NextActionTime;
            public int CoinsSent;
            public float ArrivalX;
            public float DepartureX;
            public float MovementElapsed;
            public float TargetX;
        }

        private static readonly List<Order> _orders = new List<Order>(MaxOrders);
        private static readonly Dictionary<IntPtr, int> _faultedTargets =
            new Dictionary<IntPtr, int>();
        private static readonly string[] _summary = new string[RoleCount];
        private static readonly string[] _summaryKey = new string[RoleCount];
        private static readonly bool[] _successLogged = new bool[RoleCount];
        private static readonly int[] _motionLogged = new int[RoleCount];
        private static float _nextScanTime;
        private static int _roleCursor;
        private static IntPtr _worldPtr = IntPtr.Zero;
        private static int _sceneHandle;
        private static IntPtr _layerPtr;
        private static long _lastVersion = -1;
        private static long _lastAmmoVersion = -1;
        private static long _lastSettings = -1;
        private static readonly int[] _settingValues = new int[RoleCount];
        private static int _lastBalance = -1;
        // 每角色最近一次扫描得到的真实阻塞原因（来自同一门控评估，不额外查询）。
        private static readonly string[] _blockReason = new string[RoleCount];
        private static readonly string[] _statusKey = new string[RoleCount];
        private static int _summaryLogs;
        private const int SummaryLogBudget = 24;
        private static bool _needsPlan = true;
        private static bool _countsReady;                   // 0..5：AutoRestockCounts 名册/店库存
        private static bool _musketeerReady;
        private static int _musketeerLive, _musketeerGuns;
        private static bool _ammoReady;                     // 6..7：SiegeAmmoCounts 弹药
        private static readonly int[] _roster = new int[RoleCount];
        private static readonly int[] _stock = new int[RoleCount];
        private static readonly int[] _incoming = new int[RoleCount];
        private static readonly int[] _ammo = new int[RoleCount];
        private static readonly int[] _reserved = new int[RoleCount];
        private static readonly float[] _retryAfter = new float[RoleCount];
        private static bool _faultLoggedWorld;

        // ------------------------------------------------------------------ API
        internal static void Tick(Banker banker, Managers managers, bool taxTick)
        {
            try
            {
                if (!AnyRoleEnabled() && _orders.Count == 0 && !_countsReady && !_ammoReady && !_musketeerReady) return;
                // 只有当前世界明确为希腊时才采购。其他世界/加载中一律停采购并释放
                // 本地 reservation（不回家瞬移、不向旧 actor 发位置 RPC、不向任何
                // 银行家扣款），并清掉旧订单摘要文字。
                if (!GreekBankScope.IsActive)
                {
                    Reset(false);
                    AutoRestockCounts.Reset();
                    _countsReady = false;
                    _ammoReady = false;
                    _musketeerReady = false;
                    ClearSummaryState();
                    return;
                }
                if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth)
                {
                    Reset(false);
                    AutoRestockCounts.Reset();
                    _countsReady = false;
                    _ammoReady = false;
                    _musketeerReady = false;
                    return;
                }
                World world = managers?.world;
                Kingdom kingdom = managers?.kingdom;
                if (world == null || world.gameLayer == null || kingdom == null
                    || banker == null || !BankerInCurrentWorld(banker, world))
                { Reset(false); AutoRestockCounts.Reset(); return; }
                DetectWorldChange(world);
                if (!AnyRoleEnabled())
                {
                    Reset(true); AutoRestockCounts.Reset();
                    _countsReady = false;
                    _ammoReady = false;
                    _musketeerReady = false;
                    for (int r = 0; r < RoleCount; r++) { _summary[r] = "已关闭"; _summaryKey[r] = null; }
                    return;
                }
                if (Time.timeScale <= 0f || managers.game == null
                    || managers.game.state != Game.State.Playing) return;
                if (taxTick)
                {
                    // 两类计数各自独立就绪：弹药-only 时职业缓存即使不可用也不阻断弹药。
                    bool shopReady = ShopRolesEnabled() && AutoRestockCounts.Refresh(managers);
                    bool ammoReady = AmmoRolesEnabled() && RefreshedAmmo(managers, false);
                    bool musketeerReady = RoleEnabled(RoleMusketeer) && RefreshMusketeers();
                    // 就绪翻转即重规划，摘要随该族真实状态刷新（不残留旧世界的达标文案）。
                    if (shopReady != _countsReady || ammoReady != _ammoReady || musketeerReady != _musketeerReady) _needsPlan = true;
                    _countsReady = shopReady;
                    _ammoReady = ammoReady;
                    _musketeerReady = musketeerReady;
                    if (!AnyReady())
                    {
                        Reset(true);
                        for (int r = 0; r < RoleCount; r++)
                        { _summary[r] = !RoleEnabled(r) ? "已关闭" : r == RoleMusketeer && !ModConfig.MusketeerEnabled.Value ? "请先开启火铳铺" : r == RoleMusketeer && NetworkBigBoss.IsOnline ? "火枪补货仅单机可用" : r == RoleMusketeer ? "火枪人数或身份尚未确认" : "计数初始化中"; _summaryKey[r] = null; }
                        return;
                    }
                }
                if (!AnyReady()) return;
                StepOrders(banker, managers, kingdom, world, Time.time);
                if (!taxTick) return;
                long settings = _lastSettings;
                bool settingsChanged = false;
                for (int r = 0; r < RoleCount; r++)
                {
                    int value = RoleEnabled(r) ? RoleTarget(r) : -1;
                    if (_settingValues[r] != value) { _settingValues[r] = value; settingsChanged = true; }
                }
                if (settingsChanged) settings = unchecked(settings + 1);
                // 任一缓存版本推进（职业名册/店库存或弹药）都触发重新规划。
                long countsVersion = AutoRestockCounts.Version;
                bool changed = _needsPlan || _lastVersion != countsVersion
                    || _lastAmmoVersion != SiegeAmmoCounts.Version
                    || _lastSettings != settings || _lastBalance != banker._stashedCoins;
                // A blocked shop may finish construction or player payment without a roster event.
                // Retry only cached candidate targets; no actor/scene/registrar enumeration.
                if (changed || Time.time >= _nextScanTime)
                {
                    _nextScanTime = Time.time + ScanInterval;
                    ScanAndAssign(banker, managers);
                    _needsPlan = false;
                    _lastVersion = countsVersion;
                    _lastAmmoVersion = SiegeAmmoCounts.Version;
                    _lastSettings = settings;
                    _lastBalance = banker._stashedCoins;
                }
            }
            catch (Exception e)
            {
                foreach (var order in _orders) _retryAfter[order.Role] = Time.time + ScanInterval;
                Reset(true);
                _countsReady = false;
                _ammoReady = false;
                    _musketeerReady = false;
                if (!_faultLoggedWorld)
                {
                    _faultLoggedWorld = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[AutoRestock] tick fault: " + e.GetType().Name + " " + e.Message);
                }
            }
        }

        internal static void Reset(bool returnAssistants)
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
                ReleaseOrder(_orders[i], returnAssistants);
            _orders.Clear();
            _needsPlan = true;
        }

        internal static string GetSummary(int role)
        {
            if (role < 0 || role >= RoleCount) return string.Empty;
            // scope 判定优先于缓存：非希腊世界/加载中/关闭时给出世界说明，
            // 绝不复述旧世界的订单状态（否则切世界后会看到假进度）。
            switch (GreekBankScope.Current())
            {
                case GreekBankScope.Scope.Unknown:
                    return "世界加载中";
                case GreekBankScope.Scope.Inactive:
                    return ModConfig.Enabled != null && ModConfig.Enabled.Value
                        ? "仅希腊世界生效" : "已关闭";
            }
            return _summary[role] ?? (RoleEnabled(role) ? "初始化中" : "已关闭");
        }

        /// <summary>丢弃旧世界的摘要文字，回到希腊后由 UpdateSummaries 重建。</summary>
        private static void ClearSummaryState()
        {
            for (int r = 0; r < RoleCount; r++)
            {
                _summary[r] = null;
                _summaryKey[r] = null;
                _statusKey[r] = null;
                _blockReason[r] = null;
            }
        }

        // ------------------------------------------------------------- gating
        private static bool AnyRoleEnabled()
        {
            for (int r = 0; r < RoleCount; r++)
                if (RoleEnabled(r)) return true;
            return false;
        }

        /// <summary>0..5 = 职业/工具店（AutoRestockCounts）；6..7 = 弹药（SiegeAmmoCounts）。</summary>
        private static bool ShopRole(int role) => role <= RoleFarmer;

        private static bool AmmoRole(int role) => role == RoleCatapultBarrel || role == RoleFireTowerAmmo;

        private static bool ShopRolesEnabled()
        {
            for (int r = 0; r <= RoleFarmer; r++)
                if (RoleEnabled(r)) return true;
            return false;
        }

        private static bool AmmoRolesEnabled()
            => RoleEnabled(RoleCatapultBarrel) || RoleEnabled(RoleFireTowerAmmo);

        private static bool RoleReady(int role) => role == RoleMusketeer ? _musketeerReady : ShopRole(role) ? _countsReady : _ammoReady;

        private static bool RefreshMusketeers()
        {
            bool ready = MusketeerIdentity.TryGetRestockCounts(out int live, out int guns);
            if (ready != _musketeerReady || (ready && (live != _musketeerLive || guns != _musketeerGuns))) _needsPlan = true;
            _musketeerLive = live; _musketeerGuns = guns;
            _musketeerReady = ready;
            return ready;
        }

        private static bool AnyReady()
            => (ShopRolesEnabled() && _countsReady) || (AmmoRolesEnabled() && _ammoReady)
                || (RoleEnabled(RoleMusketeer) && _musketeerReady);

        /// <summary>刷新弹药快照：失败或客机/未就绪一律视为不可用，绝不兜底成低计数。</summary>
        private static bool RefreshedAmmo(Managers managers, bool force)
        {
            if (!SiegeAmmoCounts.Refresh(managers, true, Time.unscaledTime, force)) return false;
            return SiegeAmmoCounts.IsReady && !SiegeAmmoCounts.ClientUnavailable;
        }

        /// <summary>按角色族刷新计数快照；force 只用于弹药的最终 commit。</summary>
        private static bool RefreshRoleCounts(int role, bool force)
            => role == RoleMusketeer ? RefreshMusketeers() : ShopRole(role)
                ? AutoRestockCounts.Refresh(Managers.Inst)
                : RefreshedAmmo(Managers.Inst, force);

        private static bool RoleEnabled(int role)
        {
            switch (role)
            {
                case RoleWorker: return ModConfig.AutoRestockWorkersEnabled.Value;
                case RoleArcher: return ModConfig.AutoRestockArchersEnabled.Value;
                case RoleNinja: return ModConfig.AutoRestockNinjasEnabled.Value;
                case RoleBerserker: return ModConfig.AutoRestockBerserkersEnabled.Value;
                case RolePeasant: return ModConfig.AutoRestockPeasantsEnabled.Value;
                case RoleFarmer: return ModConfig.AutoRestockFarmersEnabled.Value;
                case RoleCatapultBarrel: return ModConfig.AutoRestockCatapultBarrelsEnabled.Value;
                case RoleFireTowerAmmo: return ModConfig.AutoRestockFireTowerAmmoEnabled.Value;
                case RoleMusketeer: return ModConfig.AutoRestockMusketeersEnabled.Value;
                default: return false;
            }
        }

        private static int RoleTarget(int role)
        {
            switch (role)
            {
                case RoleWorker: return ModConfig.AutoRestockWorkersTarget.Value;
                case RoleArcher: return ModConfig.AutoRestockArchersTarget.Value;
                case RoleNinja: return ModConfig.AutoRestockNinjasTarget.Value;
                case RoleBerserker: return ModConfig.AutoRestockBerserkersTarget.Value;
                case RolePeasant: return ModConfig.AutoRestockPeasantsTarget.Value;
                case RoleFarmer: return ModConfig.AutoRestockFarmersTarget.Value;
                case RoleCatapultBarrel: return ModConfig.AutoRestockCatapultBarrelsTarget.Value;
                case RoleFireTowerAmmo: return ModConfig.AutoRestockFireTowerAmmoTarget.Value;
                case RoleMusketeer: return ModConfig.AutoRestockMusketeersTarget.Value;
                default: return 0;
            }
        }

        private static bool BankerInCurrentWorld(Banker banker, World world)
        {
            Transform layer = world.gameLayer;
            if (!Alive(banker) || layer == null) return false;
            Transform t = banker.transform;
            return t != null && t.IsChildOf(layer)
                && banker.gameObject.scene.handle == layer.gameObject.scene.handle;
        }

        private static bool NetworkReady()
        {
            return !NetworkBigBoss.IsOnline || !NetworkBigBoss.IsClientPresent
                || NetworkBigBoss.HasClientCaughtUp;
        }

        private static void DetectWorldChange(World world)
        {
            int handle = world.gameLayer.gameObject.scene.handle;
            if (_worldPtr == world.Pointer && _layerPtr == world.gameLayer.Pointer && _sceneHandle == handle) return;
            // Release local reservation flags without teleporting old-world actors.
            Reset(false);
            AutoRestockCounts.Reset();
            _countsReady = false;
            _ammoReady = false;
            _musketeerReady = false;
            _layerPtr = world.gameLayer.Pointer;
            _faultedTargets.Clear();
            Array.Clear(_retryAfter, 0, RoleCount);
            Array.Clear(_blockReason, 0, RoleCount);
            Array.Clear(_statusKey, 0, RoleCount);
            Array.Clear(_summaryKey, 0, RoleCount);
            Array.Clear(_summary, 0, RoleCount);
            Array.Clear(_motionLogged, 0, RoleCount);
            _summaryLogs = 0;
            _worldPtr = world.Pointer;
            _sceneHandle = handle;
            _faultLoggedWorld = false;
            _nextScanTime = Time.time + ScanInterval;
        }

        // -------------------------------------------------------------- roster
        private static bool Alive(Component c)
        {
            return c != null && c.gameObject != null;
        }

        // -------------------------------------------------------------- targets
        private sealed class ShopSnapshot
        {
            public GameObject GO;
            public IntPtr Ptr;
            public int InstanceId;
            public Payable Target;          // 店或弹药 Payable：原生付款对象
            public PayableShop Shop;        // 仅真店非空（slots/_items/maxItems 门专用）
            public int Role = -1;
            public int Price;
            public int Items;
            public int CountAt;             // 弹药：该站点本地未用量，用于公平选择
        }

        private static readonly List<Payable> _payableScratch = new List<Payable>(16);

        /// <summary>按角色族收集候选：0..5 读事件缓存店列表，6..7 读弹药缓存目标；均不扫描场景。</summary>
        private static void CollectTargets(Managers managers, List<ShopSnapshot> output)
        {
            output.Clear();
            for (int role = 0; role < RoleCount; role++)
            {
                if (!RoleEnabled(role) || !RoleReady(role)) continue;
                // Already satisfied roles require no target queries.
                if (ExistingCoverage(role) >= RoleTarget(role)) continue;
                if (ShopRole(role)) CollectShopTargets(managers, role, output);
                else if (role == RoleMusketeer)
                {
                    if (MusketeerShop.TryGetAutoRestockTarget(out var target))
                    {
                        var go = target.gameObject;
                        output.Add(new ShopSnapshot { GO = go, Ptr = go.Pointer,
                            InstanceId = go.GetInstanceID(), Target = target, Role = role, Price = MusketeerShop.Price });
                    }
                }
                else CollectAmmoTargets(managers, role, output);
            }
        }

        private static void CollectShopTargets(Managers managers, int role, List<ShopSnapshot> output)
        {
            foreach (PayableShop shop in AutoRestockCounts.Shops)
            {
                if (shop == null || !shop.enabled) continue;
                GameObject go = shop.gameObject;
                if (go == null || !go.activeInHierarchy
                    || !go.transform.IsChildOf(managers.world.gameLayer)) continue;
                Droppable prefab = shop.itemPrefab;
                if (prefab == null) continue;
                if (AutoRestockCounts.ClassifyShop(shop) != role) continue;
                output.Add(new ShopSnapshot
                {
                    GO = go,
                    Ptr = go.Pointer,
                    InstanceId = go.GetInstanceID(),
                    Target = shop,
                    Shop = shop,
                    Role = role,
                    Price = shop.Price,
                    Items = shop.GetItemCount()
                });
            }
        }

        /// <summary>弹药目标来自 ammo worker 的本世界缓存（不建 PayableShop、不造池、不写弹药字段）。</summary>
        private static void CollectAmmoTargets(Managers managers, int role, List<ShopSnapshot> output)
        {
            SiegeAmmoCounts.GetPayables(role, _payableScratch); // 契约：clear/fill，无扫描
            for (int i = 0; i < _payableScratch.Count; i++)
            {
                Payable payable = _payableScratch[i];
                if (payable == null) continue;
                GameObject go = payable.gameObject;
                if (go == null || !go.activeInHierarchy
                    || !go.transform.IsChildOf(managers.world.gameLayer)) continue;
                if (SiegeAmmoCounts.Classify(payable) != role) continue;
                output.Add(new ShopSnapshot
                {
                    GO = go,
                    Ptr = go.Pointer,
                    InstanceId = go.GetInstanceID(),
                    Target = payable,
                    Role = role,
                    Price = payable.Price,
                    CountAt = SiegeAmmoCounts.CountAt(payable)
                });
            }
        }

        /// <summary>实时可用量：0..5 活体+待领+招募中；6..7 全岛未用弹药。</summary>
        private static int ExistingCoverage(int role)
            => role == RoleMusketeer ? _musketeerLive + _musketeerGuns : ShopRole(role)
                ? AutoRestockCounts.LiveCount(role) + AutoRestockCounts.StockCount(role)
                    + AutoRestockCounts.IncomingCount(role)
                : SiegeAmmoCounts.Count(role);

        /// <summary>本 tick 快照可用量（摘要/规划用，不读原生）。</summary>
        private static int SnapshotCoverage(int role)
            => (ShopRole(role) || role == RoleMusketeer) ? _roster[role] + _stock[role] + _incoming[role] : _ammo[role];

        // 同一门控评估，可选输出真实阻塞原因；门控顺序与含义保持不变。
        // 真店（0..5）保留 slots/_items/maxItems 与对象池门；弹药（6..7）只走
        // 原生 Payable 门（CanPay/Currency/forceBlock/选中/玩家占用/网络/持冠）。
        private static bool ShopBuyable(ShopSnapshot s, bool onlineReady)
            { return ShopBuyable(s, onlineReady, out _); }

        private static bool ShopBuyable(ShopSnapshot s, bool onlineReady, out string reason)
        {
            Payable payable = s.Target;
            PayableShop shop = s.Shop;
            reason = "店铺失效";
            if (payable == null || !payable.enabled || s.GO == null || !s.GO.activeInHierarchy) return false;
            if (shop != null && !shop.enabled) return false;
            var world = Managers.Inst?.world;
            if (world?.gameLayer == null || !s.GO.transform.IsChildOf(world.gameLayer)) return false;
            if (s.Role != RoleMusketeer && (shop != null ? AutoRestockCounts.ClassifyShop(shop) != s.Role
                             : SiegeAmmoCounts.Classify(payable) != s.Role)) return false;
            reason = "店铺故障";
            if (_faultedTargets.TryGetValue(s.Ptr, out int faultId) && faultId == s.InstanceId) return false;
            if (shop != null)
            {
                WorkableBuilding wb = shop._workableBuilding;
                reason = "店铺建造中";
                if (wb == null || wb.UnderConstruction) return false;
            }
            reason = "非金币交易";
            if (payable.Currency != CurrencyType.Coins) return false;
            reason = "价格异常";
            if (s.Price < MinPrice || s.Price > MaxPrice) return false;
            reason = "支付被阻止";
            if (payable.forceBlockPayment) return false;
            reason = "玩家支付占用";
            if (payable.selectedByP1 || payable.selectedByP2) return false;
            if (payable.PlayerSelecting != null || payable.interactingPlayer != null) return false;
            if (PlayersEngaging(payable)) return false;
            reason = "网络未就绪";
            if (NetworkBigBoss.IsOnline && (payable.parentHeaderRef == null || payable._payRPCIndex < 0))
                return false;
            if (onlineReady && !NetworkReady()) return false;
            if (s.Role == RoleCatapultBarrel)
            {
                reason = "火药桶对象池未就绪";
                var barrelSite = s.GO.GetComponent<PayableWorkshopBarrel>();
                var barrelPrefab = barrelSite != null ? barrelSite.rollableBarrelPrefab : null;
                if (barrelPrefab == null || BiomeData.Current == null) return false;
                var resolved = BiomeData.Current.GetAssetSwapForThis<GameObject>(barrelPrefab.gameObject);
                if (resolved == null || Pool.GetPoolFromPrefabAsset(resolved) == null) return false;
            }
            if (s.Role == RoleFireTowerAmmo && NetworkBigBoss.IsClientPresent)
            {
                reason = "火焰塔弹药同步未就绪";
                var tower = s.GO.GetComponent<FireTower>();
                if (tower == null || tower._parentHeaderRef == null || tower._fireJarsActiveIndex < 0) return false;
            }
            reason = "无持冠玩家";
            if ((NetworkBigBoss.IsOnline || payable.priceIncrease != 0) && Managers.Inst.kingdom.GetNearestPlayerWithCrown(
                payable.GetApproximateGameLayerPosition()) == null) return false;
            reason = "统计未就绪";
            if (Managers.Inst.stats == null || Managers.Inst.currency == null) return false;
            if (shop != null)
            {
                reason = "对象池未就绪";
                if (Pool.GetPoolFromPrefabAsset(shop.itemPrefab.gameObject) == null) return false;
                reason = "店铺已满";
                int capacity = Mathf.Min(shop.maxItems, shop._limitedNumItems);
                if (shop.GetItemCount() >= capacity) return false;
            }
            if (s.Role == RoleFireTowerAmmo && !FireTowerRestockCapacity.CanPurchase(payable, out reason))
                return false;
            if (s.Role == RoleMusketeer) return MusketeerShop.CanAutoRestock(payable, out reason);
            reason = "原生付款暂不可用";
            return payable.CanPay(null);
        }

        private static bool PlayersEngaging(Payable target)
        {
            Kingdom kingdom = Managers.Inst?.kingdom;
            if (kingdom == null) return false;
            Player p1 = kingdom.playerOne, p2 = kingdom.playerTwo;
            return (p1 != null && (SamePayable(p1.selectedPayable, target)
                        || SamePayable(p1._completingPayable, target)))
                || (p2 != null && (SamePayable(p2.selectedPayable, target)
                        || SamePayable(p2._completingPayable, target)));
        }

        private static bool SamePayable(Payable payable, Payable target)
        {
            return payable != null && payable.Pointer == target.Pointer;
        }

        // --------------------------------------------------------------- orders
        private static void StepOrders(Banker banker, Managers managers, Kingdom kingdom,
            World world, float now)
        {
            int stepped = 0;
            for (int i = _orders.Count - 1; i >= 0 && stepped < MaxOrders; i--)
            {
                Order o = _orders[i];
                // Movement runs each frame, independent of the slower coin cadence.
                if (o.Phase == Phase.Departing)
                {
                    stepped++;
                    StepMovement(o, banker, world, i, true);
                    continue;
                }
                if (now < o.NextActionTime) continue;
                stepped++;
                if (!OrderContextValid(o, banker, kingdom, world))
                {
                    CancelOrder(i, "context");
                    continue;
                }
                switch (o.Phase)
                {
                    case Phase.Approach:
                        if (StepMovement(o, banker, world, i, false))
                        {
                            o.Phase = Phase.StartDelay;
                            o.NextActionTime = now + StartDelay;
                            TraceMotion(o, 2, "arrived");
                        }
                        break;
                    case Phase.StartDelay:
                        o.Phase = Phase.SendingCoins;
                        goto case Phase.SendingCoins;
                    case Phase.SendingCoins:
                        if (!EmitCoin(o, managers, world))
                        {
                            CancelOrder(i, "coin");
                            continue;
                        }
                        if (o.CoinsSent >= o.TotalCost)
                        {
                            o.Phase = Phase.FinalWait;
                            o.NextActionTime = now + FinalDelay;
                        }
                        else o.NextActionTime = now + CoinInterval;
                        break;
                    case Phase.FinalWait:
                        FinalizePurchase(o, banker, i);
                        break;
                }
            }
        }

        private static bool StepMovement(Order o, Banker banker, World world,
            int orderIndex, bool departing)
        {
            bool arrived = false;
            bool valid = false;
            try
            {
                float delta = Time.deltaTime; // Scaled actual frame time; pause does not accrue travel.
                valid = BankerInCurrentWorld(banker, world)
                    && BankAssistantCoordinator.IsCurrentRestockBanker(banker)
                    && float.IsFinite(delta) && delta >= 0f;
                if (valid)
                {
                    o.MovementElapsed += delta;
                    valid = BankAssistantCoordinator.MoveRestockAssistant(o.AssistantIndex,
                        o.Assistant, departing ? o.DepartureX : o.ArrivalX,
                        departing ? DepartureSpeed : ApproachSpeed, delta, out arrived);
                }
            }
            catch { valid = false; }
            if (!valid || o.MovementElapsed >= MovementTimeout)
            {
                // A paid order only releases its lease; no purchase path can run again.
                if (!departing) _retryAfter[o.Role] = Time.time + ScanInterval;
                ReleaseOrder(o, true); // Coordinator validates current world before any teleport.
                _orders.RemoveAt(orderIndex);
                return false;
            }
            if (departing && arrived)
            {
                TraceMotion(o, 4, "walked-out");
                ReleaseOrder(o, true);
                _orders.RemoveAt(orderIndex);
                return false;
            }
            return arrived;
        }

        private static bool OrderContextValid(Order o, Banker banker, Kingdom kingdom,
            World world)
        {
            if (!GreekBankScope.IsActive) return false; // 非希腊世界：任何订单上下文都不再有效
            if (!ModConfig.Enabled.Value || !RoleEnabled(o.Role) || !RoleReady(o.Role)) return false;
            if (Time.timeScale <= 0f || !NetworkBigBoss.HasWorldAuth) return false;
            if (!BankerInCurrentWorld(banker, world)
                || !BankAssistantCoordinator.IsCurrentRestockBanker(banker)) return false;
            if (o.Assistant == null || o.TargetGO == null) return false;
            if (!BankAssistantCoordinator.RestockReservationValid(o.AssistantIndex,
                    o.Assistant)) return false;
            // Once at the counter, never emit/pay after external displacement.
            if (o.Phase != Phase.Approach)
            {
                float distance = Mathf.Abs(o.Assistant.transform.position.x - o.ArrivalX);
                if (!float.IsFinite(distance) || distance > 0.02f) return false;
            }
            if (o.TargetGO.Pointer != o.TargetPtr
                || o.TargetGO.GetInstanceID() != o.TargetInstanceId) return false;
            Payable target = o.Target;
            if (target == null || !target.enabled || !o.TargetGO.activeInHierarchy) return false;
            if (o.Shop != null && !o.Shop.enabled) return false;
            float targetX = o.TargetGO.transform.position.x;
            if (!float.IsFinite(targetX) || Mathf.Abs(targetX - o.TargetX) > 0.02f) return false;
            // 原价变化：取消，下一轮重读价再买。
            if (target.Price != o.NativePrice || !o.TargetGO.transform.IsChildOf(world.gameLayer)) return false;
            if (!RefreshRoleCounts(o.Role, false)) return false;
            if (RoleTarget(o.Role) <= ExistingCoverage(o.Role)) return false;
            return ShopBuyableSnapshot(o) && banker._stashedCoins >= o.TotalCost;
        }

        private static bool EmitCoin(Order o, Managers managers, World world)
        {
            Transform helper = o.Assistant.transform;
            Transform targetT = o.Target.transform;
            if (helper == null || targetT == null) return false;
            DroppableCurrency prefab = managers.currency == null
                ? null
                : managers.currency.GetCurrencyTypePrefab<DroppableCurrency>(CurrencyType.Coins);
            if (prefab == null) return false;
            Vector3 pos = helper.position
                + new Vector3(UnityEngine.Random.Range(-0.1f, 0.1f), 0.2f, 0f);
            DroppableCurrency coin = Pool.Spawn(prefab, pos, Quaternion.identity, world.gameLayer, true);
            if (coin == null) return false;
            try
            {
                // MoveTo自身SetFake(true)禁物理/拾取，结束Despawn（destroyAfter）。
                coin.MoveTo(targetT, new Vector3(0f, 0.4f, 0f), true);
            }
            catch (Exception)
            {
                // 失败立刻回收，避免留下可拾取的真币。
                DiscardVisualCoin(coin);
                return false;
            }
            o.CoinsSent++;
            return true;
        }

        /// <summary>回收不得送达商店的表现币（MoveTo 失败）。不做任何经济动作。</summary>
        private static void DiscardVisualCoin(DroppableCurrency coin)
        {
            try { coin.SetFake(true); } catch { }
            try { Pool.Despawn(coin.gameObject, true); }
            catch { try { coin.gameObject.SetActive(false); } catch { } }
        }

        private static void FinalizePurchase(Order o, Banker banker, int orderIndex)
        {
            // 扣款最终口的入口闸门：非希腊世界/加载中立即取消，不做任何计数或付款准备。
            // TrySpendForAutoRestock 自身也要求“希腊 + 当前权威本体”双闸门。
            if (!GreekBankScope.IsActive)
            {
                CancelOrder(orderIndex, "scope");
                return;
            }
            // Flush event deltas and compare stock across every shop of this role.
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers?.kingdom;
            World world = managers?.world;
            if (kingdom == null || world == null
                || !BankerInCurrentWorld(banker, world))
            {
                CancelOrder(orderIndex, "world");
                return;
            }
            if (!RefreshRoleCounts(o.Role, !ShopRole(o.Role))
                || !OrderContextValid(o, banker, kingdom, world))
            { CancelOrder(orderIndex, "counts"); return; }
            int existing = ExistingCoverage(o.Role);
            // 扣除本订单自身那1个预留，避免自我阻塞。
            int others = 0;
            for (int i = 0; i < _orders.Count; i++)
                if (i != orderIndex && _orders[i].Role == o.Role
                    && _orders[i].Phase != Phase.Departing) others++;
            if (RoleTarget(o.Role) <= existing + others)
            {
                CancelOrder(orderIndex, "met");
                return;
            }
            if (!NetworkReady() || !ShopBuyableSnapshot(o))
            {
                CancelOrder(orderIndex, "gate");
                return;
            }
            int price = o.TotalCost;
            if (o.Role == RoleMusketeer)
            {
                var result = MusketeerShop.PurchaseForAutoRestock(o.Target, banker, () =>
                {
                    o.Phase = Phase.Departing;
                    o.MovementElapsed = 0f;
                    _needsPlan = true;
                }, out string reason);
                if (result == MusketeerShop.AutoPurchaseResult.Rejected)
                { CancelOrder(orderIndex, reason); return; }
                // Even an exception in the paid callback cannot leave this order chargeable.
                o.Phase = Phase.Departing;
                o.MovementElapsed = 0f;
                _needsPlan = true;
                if (result == MusketeerShop.AutoPurchaseResult.PaidUncertain)
                    MarkFault(o, new InvalidOperationException("paid gun shipment unconfirmed: " + reason));
                else RefreshMusketeers();
                return;
            }
            // 唯一经济commit：原子扣款，同步ledger/Castle/Stats。
            if (!PatchEconomy_Banker.TrySpendForAutoRestock(banker, price))
            {
                CancelOrder(orderIndex, "funds");
                return;
            }
            // Commit boundary: discard budget/incoming reservation before calling native
            // code, even if that call or its cleanup fails after spawning stock.
            o.Phase = Phase.Departing;
            o.MovementElapsed = 0f;
            _needsPlan = true;
            try
            {
                // 先扣款后原生购买，同一同步函数内完成，不中间yield。
                o.Target.TransactionComplete();
                if (ShopRole(o.Role)) AutoRestockCounts.HookShopAddItem(o.Shop);
                // 弹药原生付款不出事件：强制刷新快照，下一单立即看到新弹药，不靠低计数兜底。
                else RefreshedAmmo(managers, true);
                if (!_successLogged[o.Role])
                {
                    _successLogged[o.Role] = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[AutoRestock] " + RoleNames[o.Role] + " 补货成功 price=" + price
                        + " nativePrice=" + o.NativePrice + " coins=" + o.CoinsSent);
                }
            }
            catch (Exception e)
            {
                // 已扣款但购买未确认：不退款、不重试，本world该店标fault一次。
                MarkFault(o, e);
            }
            finally
            {
                try
                {
                    var selected = o.Target.PlayerSelecting;
                    // PerformPay may Select a nearby player after increasing price, even offline.
                    // The preflight had no selection; preserve only an actual new player-owned payment.
                    if (selected != null && !SamePayable(selected.selectedPayable, o.Target)
                        && !SamePayable(selected._completingPayable, o.Target))
                        o.Target.Deselect(selected);
                    // Native online TransactionComplete temporarily selects its nearest player.
                    // No player owned this target on entry; release only the synthetic interaction.
                    if (!o.Target.selectedByP1 && !o.Target.selectedByP2
                        && o.Target.PlayerSelecting == null && !PlayersEngaging(o.Target))
                        o.Target.interactingPlayer = null;
                }
                catch (Exception e)
                {
                    // A cleanup fault is also uncertain native completion; retain the
                    // paid phase and never allow a second debit for this order.
                    MarkFault(o, e);
                }
            }
        }

        private static bool ShopBuyableSnapshot(Order o)
        {
            var snap = new ShopSnapshot
            {
                GO = o.TargetGO,
                Ptr = o.TargetPtr,
                InstanceId = o.TargetInstanceId,
                Target = o.Target,
                Shop = o.Shop,
                Role = o.Role,
                Price = o.Target.Price
            };
            return ShopBuyable(snap, true);
        }

        private static void MarkFault(Order o, Exception e)
        {
            // Use the captured identity: native failure may already have destroyed the target.
            if (_faultedTargets.TryGetValue(o.TargetPtr, out int oldId) && oldId == o.TargetInstanceId) return;
            _faultedTargets[o.TargetPtr] = o.TargetInstanceId;
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[AutoRestock] shop fault id=" + o.TargetInstanceId + ": " + e.GetType().Name);
            }
            catch { } // Diagnostics must not interrupt a paid assistant's departure.
        }

        private static void CancelOrder(int index, string reason)
        {
            Order o = _orders[index];
            _retryAfter[o.Role] = Time.time + ScanInterval;
            // 未扣款即取消：助手回家，保留原有收币功能。
            ReleaseOrder(o, true);
            _orders.RemoveAt(index);
        }

        private static void ReleaseOrder(Order o, bool returnHome)
        {
            try
            {
                BankAssistantCoordinator.ReleaseRestockAssistant(
                    o.AssistantIndex, o.Assistant, returnHome);
            }
            catch { }
            _needsPlan = true;
            o.Assistant = null;
            o.Target = null;
            o.Shop = null;
            o.TargetGO = null;
        }

        // ------------------------------------------------------------------ scan
        private static readonly List<ShopSnapshot> _candidateCache = new List<ShopSnapshot>();

        private static void ScanAndAssign(Banker banker, Managers managers)
        {
            for (int r = 0; r < RoleCount; r++)
            {
                if (r == RoleMusketeer)
                {
                    _roster[r] = _musketeerLive; _stock[r] = _musketeerGuns; _incoming[r] = 0;
                }
                else if (ShopRole(r))
                {
                    _roster[r] = AutoRestockCounts.LiveCount(r);
                    _stock[r] = AutoRestockCounts.StockCount(r);
                    _incoming[r] = AutoRestockCounts.IncomingCount(r);
                }
                else
                {
                    _roster[r] = 0;
                    _stock[r] = 0;
                    _incoming[r] = 0;
                    _ammo[r] = SiegeAmmoCounts.Count(r);
                }
                _reserved[r] = 0;
            }
            int[] reserved = _reserved;
            CancelSurplusOrders();
            foreach (Order o in _orders)
                if (o.Phase != Phase.Departing) reserved[o.Role]++;
            bool deficit = false;
            for (int r = 0; r < RoleCount; r++)
                if (RoleEnabled(r) && RoleReady(r) && RoleTarget(r) > SnapshotCoverage(r) + reserved[r])
                    deficit = true;
            _candidateCache.Clear();
            if (deficit) CollectTargets(managers, _candidateCache);
            if (!deficit) { UpdateSummaries(reserved, banker); _nextScanTime = float.PositiveInfinity; return; }
            long stashed = banker._stashedCoins;
            long held = 0;
            foreach (Order o in _orders)
                if (o.Phase != Phase.Departing) held += o.TotalCost;

            int startRole = _roleCursor;
            for (int probe = 0; probe < RoleCount; probe++)
            {
                int role = (startRole + probe) % RoleCount;
                if (!RoleEnabled(role)) continue;
                if (!RoleReady(role)) { _blockReason[role] = null; continue; } // 该族计数未就绪：不规划
                if (reserved[role] > 0) { _blockReason[role] = null; continue; } // 角色不可重复订单
                int target = RoleTarget(role);
                if (target <= SnapshotCoverage(role)) { _blockReason[role] = null; continue; }
                // 角色仍缺货：缓存真实阻塞原因供摘要使用，不为汇总做额外查询。
                if (_orders.Count >= MaxOrders) { _blockReason[role] = "订单已满"; continue; }
                if (Time.time < _retryAfter[role]) { _blockReason[role] = "冷却中"; continue; }
                if (!NetworkReady()) { _blockReason[role] = "网络未就绪"; continue; }
                bool ammo = AmmoRole(role);
                ShopSnapshot best = null;
                string bestReason = null;
                foreach (ShopSnapshot s in _candidateCache)
                {
                    if (s.Role != role) continue;
                    if (ShopHasOrder(s.Ptr)) continue; // 同一目标不可重复订单
                    if (ShopBuyable(s, true, out string why))
                    {
                        // 店按原生价最低；弹药按本地未用量最少，让空的一侧先补、两侧轮换。
                        if (best == null || (ammo ? s.CountAt < best.CountAt : s.Price < best.Price)) best = s;
                    }
                    else if (best == null && (bestReason == null
                        || ReasonRank(why) > ReasonRank(bestReason)))
                        bestReason = why; // 走得最远的那个目标的阻塞原因
                }
                if (best == null)
                {
                    _blockReason[role] = bestReason ?? "无可用目标";
                    continue;
                }
                int totalCost = AutoRestockCost(best.Price);
                if (stashed - held < totalCost) { _blockReason[role] = "金币不足"; continue; } // 预留双倍采购费
                if (TryCreateOrder(banker, best, role, out string fail))
                { held += totalCost; reserved[role]++; _blockReason[role] = null; _roleCursor = (role + 1) % RoleCount; }
                else _blockReason[role] = fail;
            }
            UpdateSummaries(reserved, banker);
        }

        // 门控顺序越靠后代表店铺越接近可购买；比较阻塞原因的深度。
        private static readonly string[] ReasonOrder =
            { "店铺失效", "店铺故障", "店铺建造中", "非金币交易", "价格异常", "支付被阻止",
                "玩家支付占用", "网络未就绪", "无持冠玩家", "统计未就绪", "对象池未就绪",
                "店铺已满", FireTowerRestockCapacity.ReasonNotReady, FireTowerRestockCapacity.ReasonFull, "原生付款暂不可用" };

        private static int ReasonRank(string reason)
        {
            int i = Array.IndexOf(ReasonOrder, reason);
            return i < 0 ? 0 : i;
        }

        private static bool ShopHasOrder(IntPtr targetPtr)
        {
            foreach (Order o in _orders)
                if (o.TargetPtr == targetPtr) return true;
            return false;
        }

        // Only validated native prices (1..100) reach an order. Never modify target.Price:
        // the surcharge belongs to automatic procurement, with one native purchase.
        private static int AutoRestockCost(int nativePrice) => nativePrice * AutoRestockCostMultiplier;

        private static bool TryCreateOrder(Banker banker, ShopSnapshot s, int role,
            out string failReason)
        {
            failReason = "税收官暂不可用";
            if (!BankAssistantCoordinator.TryReserveForRestock(out int index, out GameObject actor))
                return false;
            if (actor == null) return false;
            try
            {
                Transform targetT = s.Target.transform;
                Transform actorT = actor.transform;
                if (actorT == null || targetT == null) throw new InvalidOperationException("missing transform");
                // Teleport nearby, then run to the same-side counter. Actor owns ground Y/Z.
                float targetX = targetT.position.x;
                float side = actorT.position.x < targetX ? -1f : 1f;
                Vector3 pos = actorT.position;
                pos.x = targetX + side * EntryDistance;
                if (!BankAssistantCoordinator.PlaceRestockAssistant(index, actor, pos,
                        targetT.position.x))
                {
                    BankAssistantCoordinator.ReleaseRestockAssistant(index, actor, true);
                    failReason = "税收官到店失败";
                    return false;
                }
                var order = new Order
                {
                    Role = role,
                    TargetGO = s.GO,
                    TargetPtr = s.Ptr,
                    TargetInstanceId = s.InstanceId,
                    Target = s.Target,
                    Shop = s.Shop,
                    NativePrice = s.Price,
                    AssistantIndex = index,
                    Assistant = actor,
                    Phase = Phase.Approach,
                    NextActionTime = Time.time,
                    ArrivalX = targetX + side * StandoffDistance,
                    DepartureX = targetX + side * DepartureDistance,
                    MovementElapsed = 0f,
                    TargetX = targetX
                };
                _orders.Add(order);
                TraceMotion(order, 1, "approach");
                _needsPlan = true;
                return true;
            }
            catch
            {
                try { BankAssistantCoordinator.ReleaseRestockAssistant(index, actor, true); }
                catch { }
                failReason = "税收官暂不可用";
                return false;
            }
        }

        private static void TraceMotion(Order order, int flag, string phase)
        {
            if ((_motionLogged[order.Role] & flag) != 0) return;
            _motionLogged[order.Role] |= flag;
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[RestockMotion] role="
                    + RoleNames[order.Role] + " phase=" + phase + " x="
                    + order.Assistant.transform.position.x.ToString("F2") + " shopX="
                    + order.TargetX.ToString("F2") + " gameTime=" + Time.time.ToString("F2"));
            }
            catch { } // Presentation evidence cannot change the purchase or lease outcome.
        }

        private static void CancelSurplusOrders()
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                Order o = _orders[i];
                if (o.Phase == Phase.Departing) continue; // Paid orders only hold visual/lease slots.
                if (!RoleEnabled(o.Role) || !RoleReady(o.Role))
                {
                    CancelOrder(i, "off");
                    continue;
                }
                int others = 0;
                for (int j = 0; j < _orders.Count; j++)
                    if (j != i && _orders[j].Role == o.Role
                        && _orders[j].Phase != Phase.Departing) others++;
                // 目标降低/手动购买/转职已满足：扣除本单自身预留后判定。
                if (RoleTarget(o.Role) <= SnapshotCoverage(o.Role) + others)
                    CancelOrder(i, "surplus");
            }
        }

        // -------------------------------------------------------------- summary
        private static void UpdateSummaries(int[] reserved, Banker banker)
        {
            for (int r = 0; r < RoleCount; r++)
            {
                string text, status;
                if (!RoleEnabled(r)) { text = "已关闭"; status = text; }
                else if (r == RoleMusketeer && !ModConfig.MusketeerEnabled.Value) { text = "请先开启火铳铺"; status = text; }
                else if (r == RoleMusketeer && NetworkBigBoss.IsOnline) { text = "火枪补货仅单机可用"; status = text; }
                else if (!RoleReady(r)) { text = r == RoleMusketeer ? "火枪人数或身份尚未确认" : "计数初始化中"; status = text; }
                else
                {
                    string head;
                    if (r == RolePeasant)
                        head = "现有 " + _roster[r] + " · 面包 " + _stock[r] + " · 招募中 " + _incoming[r] + " · 采购 " + reserved[r];
                    else if (ShopRole(r) || r == RoleMusketeer)
                        head = "现有 " + _roster[r] + " · 待领 " + _stock[r] + " · 采购 " + reserved[r];
                    else
                        head = "弹药 " + _ammo[r] + " · 采购 " + reserved[r];
                    int target = RoleTarget(r);
                    if (reserved[r] > 0) status = "采购中";
                    else if (SnapshotCoverage(r) >= target)
                        status = r == RolePeasant && _roster[r] < target
                            ? (_incoming[r] > 0 ? "等待入籍" : "面包已备足") : "已达标";
                    else status = _blockReason[r] ?? "等待采购";
                    text = head + " · " + status;
                }
                string key = RoleEnabled(r) + "|" + text;
                if (_summaryKey[r] != key)
                {
                    _summaryKey[r] = key;
                    _summary[r] = text; // 只在低频计数/订单状态变化时构建
                }
                // 仅状态变化时打日志，且每world最多SummaryLogBudget行，避免刷屏。
                if (_statusKey[r] != status)
                {
                    _statusKey[r] = status;
                    if (_summaryLogs < SummaryLogBudget)
                    {
                        _summaryLogs++;
                        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                            "[AutoRestock] role=" + RoleNames[r] + " " + text);
                    }
                }
            }
        }
    }
}
