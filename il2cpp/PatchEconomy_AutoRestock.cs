using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 自动补货服务（单文件，无自建driver/hook）。
    /// 目标 = 现有职业人数 + 店内待领取道具 + 已预留尚未出货的订单；
    /// 读取事件维护的 AutoRestockCounts 缓存，复用税收官调度；
    /// 无周期全角色扫描，订单最多2并发，每帧step最多处理2单。
    /// 经济commit只走 PatchEconomy_Banker.TrySpendForAutoRestock，随后
    /// 原生 PayableShop.TransactionComplete()（PerformPay→Pay/CreateItem…）。
    /// </summary>
    internal static class PatchEconomy_AutoRestock
    {
        private const int RoleCount = 5; // Workers, Archers, Ninjas, Berserkers, Peasants
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
            { "工人", "弓箭手", "忍者", "狂战士", "无业村民" };

        // Approach through FinalWait owns budget/incoming stock. Departing owns only
        // the assistant and shop slot: the debit already happened and is never retried.
        private enum Phase { Approach, StartDelay, SendingCoins, FinalWait, Departing }

        private sealed class Order
        {
            public int Role;
            public GameObject ShopGO;
            public IntPtr ShopPtr;
            public int ShopInstanceId;
            public PayableShop Shop;
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
            public float ShopX;
        }

        private static readonly List<Order> _orders = new List<Order>(MaxOrders);
        private static readonly Dictionary<IntPtr, int> _faultedShops =
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
        private static long _lastSettings = -1;
        private static int _lastBalance = -1;
        // 每角色最近一次扫描得到的真实阻塞原因（来自同一门控评估，不额外查询）。
        private static readonly string[] _blockReason = new string[RoleCount];
        private static readonly string[] _statusKey = new string[RoleCount];
        private static int _summaryLogs;
        private const int SummaryLogBudget = 24;
        private static bool _needsPlan = true;
        private static bool _countsReady;
        private static readonly int[] _roster = new int[RoleCount];
        private static readonly int[] _stock = new int[RoleCount];
        private static readonly int[] _incoming = new int[RoleCount];
        private static readonly int[] _reserved = new int[RoleCount];
        private static readonly float[] _retryAfter = new float[RoleCount];
        private static bool _faultLoggedWorld;

        // ------------------------------------------------------------------ API
        internal static void Tick(Banker banker, Managers managers, bool taxTick)
        {
            try
            {
                if (!AnyRoleEnabled() && _orders.Count == 0 && !_countsReady) return;
                if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth)
                { Reset(false); AutoRestockCounts.Reset(); return; }
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
                    for (int r = 0; r < RoleCount; r++) { _summary[r] = "已关闭"; _summaryKey[r] = null; }
                    return;
                }
                if (Time.timeScale <= 0f || managers.game == null
                    || managers.game.state != Game.State.Playing) return;
                if (taxTick)
                {
                    _countsReady = AutoRestockCounts.Refresh(managers);
                    if (!_countsReady)
                    {
                        Reset(true);
                        for (int r = 0; r < RoleCount; r++)
                        { _summary[r] = RoleEnabled(r) ? "计数初始化中" : "已关闭"; _summaryKey[r] = null; }
                        return;
                    }
                }
                if (!_countsReady) return;
                StepOrders(banker, managers, kingdom, world, Time.time);
                if (!taxTick) return;
                long settings = 0;
                for (int r = 0; r < RoleCount; r++)
                    settings = settings * 512 + (RoleEnabled(r) ? RoleTarget(r) : 0);
                bool changed = _needsPlan || _lastVersion != AutoRestockCounts.Version
                    || _lastSettings != settings || _lastBalance != banker._stashedCoins;
                // A blocked shop may finish construction or player payment without a roster event.
                // Retry only cached candidate shops; no actor/scene/registrar enumeration.
                if (changed || Time.time >= _nextScanTime)
                {
                    _nextScanTime = Time.time + ScanInterval;
                    ScanAndAssign(banker, managers, kingdom, world);
                    _needsPlan = false;
                    _lastVersion = AutoRestockCounts.Version;
                    _lastSettings = settings;
                    _lastBalance = banker._stashedCoins;
                }
            }
            catch (Exception e)
            {
                foreach (var order in _orders) _retryAfter[order.Role] = Time.time + ScanInterval;
                Reset(true);
                _countsReady = false;
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
            return _summary[role] ?? (RoleEnabled(role) ? "初始化中" : "已关闭");
        }

        // ------------------------------------------------------------- gating
        private static bool AnyRoleEnabled()
        {
            for (int r = 0; r < RoleCount; r++)
                if (RoleEnabled(r)) return true;
            return false;
        }

        private static bool RoleEnabled(int role)
        {
            switch (role)
            {
                case 0: return ModConfig.AutoRestockWorkersEnabled.Value;
                case 1: return ModConfig.AutoRestockArchersEnabled.Value;
                case 2: return ModConfig.AutoRestockNinjasEnabled.Value;
                case 3: return ModConfig.AutoRestockBerserkersEnabled.Value;
                case 4: return ModConfig.AutoRestockPeasantsEnabled.Value;
                default: return false;
            }
        }

        private static int RoleTarget(int role)
        {
            switch (role)
            {
                case 0: return ModConfig.AutoRestockWorkersTarget.Value;
                case 1: return ModConfig.AutoRestockArchersTarget.Value;
                case 2: return ModConfig.AutoRestockNinjasTarget.Value;
                case 3: return ModConfig.AutoRestockBerserkersTarget.Value;
                case 4: return ModConfig.AutoRestockPeasantsTarget.Value;
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
            _layerPtr = world.gameLayer.Pointer;
            _faultedShops.Clear();
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

        // ---------------------------------------------------------------- shops
        private sealed class ShopSnapshot
        {
            public GameObject GO;
            public IntPtr Ptr;
            public int InstanceId;
            public PayableShop Shop;
            public int Role = -1;
            public int Price;
            public int Items;
        }

        private static void CollectShops(Managers managers, List<ShopSnapshot> output)
        {
            output.Clear();
            foreach (PayableShop shop in AutoRestockCounts.Shops)
            {
                if (shop == null || !shop.enabled) continue;
                GameObject go = shop.gameObject;
                if (go == null || !go.activeInHierarchy
                    || !go.transform.IsChildOf(managers.world.gameLayer)) continue;
                Droppable prefab = shop.itemPrefab;
                if (prefab == null) continue;
                int role = AutoRestockCounts.ClassifyShop(shop);
                if (role < 0 || !RoleEnabled(role)) continue;
                // Already satisfied roles require no shop queries.
                if (ExistingCoverage(role)
                    >= RoleTarget(role)) continue;
                var snap = new ShopSnapshot
                {
                    GO = go,
                    Ptr = go.Pointer,
                    InstanceId = go.GetInstanceID(),
                    Shop = shop,
                    Role = role,
                    Price = shop.Price,
                    Items = shop.GetItemCount()
                };
                output.Add(snap);
            }
        }

        private static int ExistingCoverage(int role) => AutoRestockCounts.LiveCount(role)
            + AutoRestockCounts.StockCount(role) + AutoRestockCounts.IncomingCount(role);

        // 同一门控评估，可选输出真实阻塞原因；门控顺序与含义保持不变。
        private static bool ShopBuyable(ShopSnapshot s, bool onlineReady)
            { return ShopBuyable(s, onlineReady, out _); }

        private static bool ShopBuyable(ShopSnapshot s, bool onlineReady, out string reason)
        {
            PayableShop shop = s.Shop;
            reason = "店铺失效";
            if (shop == null || s.GO == null || !shop.enabled || !s.GO.activeInHierarchy) return false;
            var world = Managers.Inst?.world;
            if (world?.gameLayer == null || !s.GO.transform.IsChildOf(world.gameLayer)
                || AutoRestockCounts.ClassifyShop(shop) != s.Role) return false;
            reason = "店铺故障";
            if (_faultedShops.TryGetValue(s.Ptr, out int faultId) && faultId == s.InstanceId) return false;
            WorkableBuilding wb = shop._workableBuilding;
            reason = "店铺建造中";
            if (wb == null || wb.UnderConstruction) return false;
            reason = "非金币交易";
            if (shop.Currency != CurrencyType.Coins) return false;
            reason = "价格异常";
            if (s.Price < MinPrice || s.Price > MaxPrice) return false;
            reason = "支付被阻止";
            if (shop.forceBlockPayment) return false;
            reason = "玩家支付占用";
            if (shop.selectedByP1 || shop.selectedByP2) return false;
            if (shop.PlayerSelecting != null || shop.interactingPlayer != null) return false;
            if (PlayersEngaging(shop)) return false;
            reason = "网络未就绪";
            if (NetworkBigBoss.IsOnline && (shop.parentHeaderRef == null || shop._payRPCIndex < 0))
                return false;
            if (onlineReady && !NetworkReady()) return false;
            reason = "无持冠玩家";
            if ((NetworkBigBoss.IsOnline || shop.priceIncrease != 0) && Managers.Inst.kingdom.GetNearestPlayerWithCrown(
                shop.GetApproximateGameLayerPosition()) == null) return false;
            reason = "统计未就绪";
            if (Managers.Inst.stats == null || Managers.Inst.currency == null) return false;
            reason = "对象池未就绪";
            if (Pool.GetPoolFromPrefabAsset(shop.itemPrefab.gameObject) == null) return false;
            reason = "店铺已满";
            int capacity = Mathf.Min(shop.maxItems, shop._limitedNumItems);
            if (shop.GetItemCount() >= capacity) return false;
            reason = "原生付款暂不可用";
            return shop.CanPay(null);
        }

        private static bool PlayersEngaging(PayableShop shop)
        {
            Kingdom kingdom = Managers.Inst?.kingdom;
            if (kingdom == null) return false;
            Player p1 = kingdom.playerOne, p2 = kingdom.playerTwo;
            return (p1 != null && (SamePayable(p1.selectedPayable, shop)
                        || SamePayable(p1._completingPayable, shop)))
                || (p2 != null && (SamePayable(p2.selectedPayable, shop)
                        || SamePayable(p2._completingPayable, shop)));
        }

        private static bool SamePayable(Payable payable, PayableShop shop)
        {
            return payable != null && payable.Pointer == shop.Pointer;
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
            if (!ModConfig.Enabled.Value || !RoleEnabled(o.Role)) return false;
            if (Time.timeScale <= 0f || !NetworkBigBoss.HasWorldAuth) return false;
            if (!BankerInCurrentWorld(banker, world)
                || !BankAssistantCoordinator.IsCurrentRestockBanker(banker)) return false;
            if (o.Assistant == null || o.ShopGO == null) return false;
            if (!BankAssistantCoordinator.RestockReservationValid(o.AssistantIndex,
                    o.Assistant)) return false;
            // Once at the counter, never emit/pay after external displacement.
            if (o.Phase != Phase.Approach)
            {
                float distance = Mathf.Abs(o.Assistant.transform.position.x - o.ArrivalX);
                if (!float.IsFinite(distance) || distance > 0.02f) return false;
            }
            if (o.ShopGO.Pointer != o.ShopPtr
                || o.ShopGO.GetInstanceID() != o.ShopInstanceId) return false;
            PayableShop shop = o.Shop;
            if (shop == null || !shop.enabled || !o.ShopGO.activeInHierarchy) return false;
            float shopX = o.ShopGO.transform.position.x;
            if (!float.IsFinite(shopX) || Mathf.Abs(shopX - o.ShopX) > 0.02f) return false;
            // 店价变化：取消，下一轮重读价再买。
            if (shop.Price != o.NativePrice || !o.ShopGO.transform.IsChildOf(world.gameLayer)) return false;
            if (!AutoRestockCounts.Refresh(Managers.Inst)) return false;
            if (RoleTarget(o.Role) <= ExistingCoverage(o.Role)) return false;
            return ShopBuyableSnapshot(o) && banker._stashedCoins >= o.TotalCost;
        }

        private static bool EmitCoin(Order o, Managers managers, World world)
        {
            Transform helper = o.Assistant.transform;
            Transform shopT = o.Shop.transform;
            if (helper == null || shopT == null) return false;
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
                coin.MoveTo(shopT, new Vector3(0f, 0.4f, 0f), true);
            }
            catch (Exception)
            {
                // 失败立刻回收，避免留下可拾取的真币。
                try { coin.SetFake(true); } catch { }
                try { Pool.Despawn(coin.gameObject, true); }
                catch { try { coin.gameObject.SetActive(false); } catch { } }
                return false;
            }
            o.CoinsSent++;
            return true;
        }

        private static void FinalizePurchase(Order o, Banker banker, int orderIndex)
        {
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
            if (!AutoRestockCounts.Refresh(managers) || !OrderContextValid(o, banker, kingdom, world))
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
                o.Shop.TransactionComplete();
                AutoRestockCounts.HookShopAddItem(o.Shop);
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
                    var selected = o.Shop != null ? o.Shop.PlayerSelecting : null;
                    // PerformPay may Select a nearby player after increasing price, even offline.
                    // The preflight had no selection; preserve only an actual new player-owned payment.
                    if (selected != null && !SamePayable(selected.selectedPayable, o.Shop)
                        && !SamePayable(selected._completingPayable, o.Shop))
                        o.Shop.Deselect(selected);
                    // Native online TransactionComplete temporarily selects its nearest player.
                    // No player owned this shop on entry; release only the synthetic interaction.
                    if (o.Shop != null && !o.Shop.selectedByP1 && !o.Shop.selectedByP2
                        && o.Shop.PlayerSelecting == null && !PlayersEngaging(o.Shop))
                        o.Shop.interactingPlayer = null;
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
                GO = o.ShopGO,
                Ptr = o.ShopPtr,
                InstanceId = o.ShopInstanceId,
                Shop = o.Shop,
                Role = o.Role,
                Price = o.Shop.Price
            };
            return ShopBuyable(snap, true);
        }

        private static void MarkFault(Order o, Exception e)
        {
            // Use the captured identity: native failure may already have destroyed the shop.
            if (_faultedShops.TryGetValue(o.ShopPtr, out int oldId) && oldId == o.ShopInstanceId) return;
            _faultedShops[o.ShopPtr] = o.ShopInstanceId;
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[AutoRestock] shop fault id=" + o.ShopInstanceId + ": " + e.GetType().Name);
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
            o.Shop = null;
            o.ShopGO = null;
        }

        // ------------------------------------------------------------------ scan
        private static readonly List<ShopSnapshot> _shopCache = new List<ShopSnapshot>();

        private static void ScanAndAssign(Banker banker, Managers managers,
            Kingdom kingdom, World world)
        {
            int[] roster = _roster, shopItems = _stock, reserved = _reserved;
            for (int r = 0; r < RoleCount; r++)
            {
                roster[r] = AutoRestockCounts.LiveCount(r);
                shopItems[r] = AutoRestockCounts.StockCount(r);
                _incoming[r] = AutoRestockCounts.IncomingCount(r);
                reserved[r] = 0;
            }
            CancelSurplusOrders(kingdom, world, roster, shopItems);
            foreach (Order o in _orders)
                if (o.Phase != Phase.Departing) reserved[o.Role]++;
            bool deficit = false;
            for (int r = 0; r < RoleCount; r++)
                if (RoleEnabled(r) && RoleTarget(r) > roster[r] + shopItems[r] + _incoming[r] + reserved[r])
                    deficit = true;
            _shopCache.Clear();
            if (deficit) CollectShops(managers, _shopCache);
            if (!deficit) { UpdateSummaries(roster, shopItems, reserved, banker); _nextScanTime = float.PositiveInfinity; return; }
            long stashed = banker._stashedCoins;
            long held = 0;
            foreach (Order o in _orders)
                if (o.Phase != Phase.Departing) held += o.TotalCost;

            int startRole = _roleCursor;
            for (int probe = 0; probe < RoleCount; probe++)
            {
                int role = (startRole + probe) % RoleCount;
                if (!RoleEnabled(role)) continue;
                if (reserved[role] > 0) { _blockReason[role] = null; continue; } // 角色不可重复订单
                int target = RoleTarget(role);
                if (target <= roster[role] + shopItems[role] + _incoming[role]) { _blockReason[role] = null; continue; }
                // 角色仍缺货：缓存真实阻塞原因供摘要使用，不为汇总做额外查询。
                if (_orders.Count >= MaxOrders) { _blockReason[role] = "订单已满"; continue; }
                if (Time.time < _retryAfter[role]) { _blockReason[role] = "冷却中"; continue; }
                if (!NetworkReady()) { _blockReason[role] = "网络未就绪"; continue; }
                ShopSnapshot best = null;
                string bestReason = null;
                foreach (ShopSnapshot s in _shopCache)
                {
                    if (s.Role != role) continue;
                    if (ShopHasOrder(s.Ptr)) continue; // 店不可重复订单
                    if (ShopBuyable(s, true, out string why))
                    {
                        if (best == null || s.Price < best.Price) best = s;
                    }
                    else if (best == null && (bestReason == null
                        || ReasonRank(why) > ReasonRank(bestReason)))
                        bestReason = why; // 走得最远的那家店的阻塞原因
                }
                if (best == null)
                {
                    _blockReason[role] = bestReason ?? "无可用店铺";
                    continue;
                }
                int totalCost = AutoRestockCost(best.Price);
                if (stashed - held < totalCost) { _blockReason[role] = "金币不足"; continue; } // 预留双倍采购费
                if (TryCreateOrder(banker, best, role, out string fail))
                { held += totalCost; reserved[role]++; _blockReason[role] = null; _roleCursor = (role + 1) % RoleCount; }
                else _blockReason[role] = fail;
            }
            UpdateSummaries(roster, shopItems, reserved, banker);
        }

        // 门控顺序越靠后代表店铺越接近可购买；比较阻塞原因的深度。
        private static readonly string[] ReasonOrder =
            { "店铺失效", "店铺故障", "店铺建造中", "非金币交易", "价格异常", "支付被阻止",
                "玩家支付占用", "网络未就绪", "无持冠玩家", "统计未就绪", "对象池未就绪",
                "店铺已满", "原生付款暂不可用" };

        private static int ReasonRank(string reason)
        {
            int i = Array.IndexOf(ReasonOrder, reason);
            return i < 0 ? 0 : i;
        }

        private static bool ShopHasOrder(IntPtr shopPtr)
        {
            foreach (Order o in _orders)
                if (o.ShopPtr == shopPtr) return true;
            return false;
        }

        // Only validated native prices (1..100) reach an order. Never modify shop.Price:
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
                Transform shopT = s.Shop.transform;
                Transform actorT = actor.transform;
                if (actorT == null || shopT == null) throw new InvalidOperationException("missing transform");
                // Teleport nearby, then run to the same-side counter. Actor owns ground Y/Z.
                float shopX = shopT.position.x;
                float side = actorT.position.x < shopX ? -1f : 1f;
                Vector3 pos = actorT.position;
                pos.x = shopX + side * EntryDistance;
                if (!BankAssistantCoordinator.PlaceRestockAssistant(index, actor, pos,
                        shopT.position.x))
                {
                    BankAssistantCoordinator.ReleaseRestockAssistant(index, actor, true);
                    failReason = "税收官到店失败";
                    return false;
                }
                var order = new Order
                {
                    Role = role,
                    ShopGO = s.GO,
                    ShopPtr = s.Ptr,
                    ShopInstanceId = s.InstanceId,
                    Shop = s.Shop,
                    NativePrice = s.Price,
                    AssistantIndex = index,
                    Assistant = actor,
                    Phase = Phase.Approach,
                    NextActionTime = Time.time,
                    ArrivalX = shopX + side * StandoffDistance,
                    DepartureX = shopX + side * DepartureDistance,
                    MovementElapsed = 0f,
                    ShopX = shopX
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
                    + order.ShopX.ToString("F2") + " gameTime=" + Time.time.ToString("F2"));
            }
            catch { } // Presentation evidence cannot change the purchase or lease outcome.
        }

        private static void CancelSurplusOrders(Kingdom kingdom, World world,
            int[] roster, int[] shopItems)
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                Order o = _orders[i];
                if (o.Phase == Phase.Departing) continue; // Paid orders only hold visual/lease slots.
                if (!RoleEnabled(o.Role))
                {
                    CancelOrder(i, "off");
                    continue;
                }
                int others = 0;
                for (int j = 0; j < _orders.Count; j++)
                    if (j != i && _orders[j].Role == o.Role
                        && _orders[j].Phase != Phase.Departing) others++;
                // 目标降低/手动购买/转职已满足：扣除本单自身预留后判定。
                if (RoleTarget(o.Role) <= roster[o.Role] + shopItems[o.Role] + _incoming[o.Role] + others)
                    CancelOrder(i, "surplus");
            }
        }

        // -------------------------------------------------------------- summary
        private static void UpdateSummaries(int[] roster, int[] shopItems, int[] reserved,
            Banker banker)
        {
            for (int r = 0; r < RoleCount; r++)
            {
                string text, status;
                if (!RoleEnabled(r)) { text = "已关闭"; status = text; }
                else
                {
                    string head = r == 4
                        ? "现有 " + roster[r] + " · 面包 " + shopItems[r] + " · 招募中 " + _incoming[r] + " · 采购 " + reserved[r]
                        : "现有 " + roster[r] + " · 待领 " + shopItems[r] + " · 采购 " + reserved[r];
                    int target = RoleTarget(r);
                    if (reserved[r] > 0) status = "采购中";
                    else if (roster[r] + shopItems[r] + _incoming[r] >= target)
                        status = r == 4 && roster[r] < target ? (_incoming[r] > 0 ? "等待入籍" : "面包已备足") : "已达标";
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
