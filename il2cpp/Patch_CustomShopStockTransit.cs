using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Patch_CustomShopStockTransit.cs — 自定义商店库存迁移控制器（stock worker 叶子，契约 integration-contract-v1）。
//
// 纯托管有界控制器：不引用 UnityEngine/Il2CppInterop，不做物理/场景/资源发现，不铸造新 GUID、
// 不生成武器、不写 schema/存档/世界。所有原生读写经 ShopStockAccess 的 caller-proven adapter
// 完成（Root 负责）；adapter 必须在每次写前证明 token 同一。控制器对每次写执行
// “写前读 → 资格/CAS 校验 → 写 → 写后读验证”；对写抛异常一律用 reread/CAS 判定实际状态，
// 不假设异常没有副作用。Read 抛异常只视为暂时未知：保留字段所有权与 lease 供重试，
// 绝不因读取异常把 lease 判定为对象已回收（只有 adapter 明确回报 Valid=false 才丢弃托管租约，
// 且丢弃时不触原生）。
//
// 所有权是字段级的：友策/敌策/视觉三字段各自独立；恢复时只写“当前值仍是我们哨兵值”的字段，
// 外来写者接管的字段不覆写、其所有权终止并留有界 fault。Blocked 不是预约锁：既有友/敌认领
// 从不强制取消、从不夺回。位置永不回滚（Release 不移动；中止分支停在当前位）。
//
// host 回归用 CUSTOM_SHOP_STOCK_TRANSIT_CORE_ONLY 编译同一份源码；当前整文件即纯核，
// 将来加入原生装配段必须包进 #if !CUSTOM_SHOP_STOCK_TRANSIT_CORE_ONLY。
//
// 并发约定：与其它模块一致，仅 Unity 主线程调用，无锁。
namespace KingdomEnhancedMod
{
    /// <summary>一件已付款未领取库存的原生身份。全部字段相等且 Owner 为同一引用才视为同一 token；
    /// 任一字段变化（含对象池复用后的 Life/Pointer）都是新身份，旧 lease 不得套用到新对象上。</summary>
    internal readonly struct ShopStockToken : IEquatable<ShopStockToken>
    {
        internal readonly string PaidId;
        internal readonly long World;
        internal readonly long Life;
        internal readonly long Pointer;
        internal readonly int Instance;
        internal readonly int Slot;
        internal readonly object Owner;

        internal ShopStockToken(string paidId, long world, long life, long pointer, int instance, int slot, object owner)
        {
            PaidId = paidId; World = world; Life = life; Pointer = pointer; Instance = instance; Slot = slot; Owner = owner;
        }

        public bool Equals(ShopStockToken other) =>
            string.Equals(PaidId, other.PaidId, StringComparison.Ordinal)
            && World == other.World && Life == other.Life && Pointer == other.Pointer
            && Instance == other.Instance && Slot == other.Slot
            && ReferenceEquals(Owner, other.Owner);

        public override bool Equals(object obj) => obj is ShopStockToken other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (PaidId != null ? StringComparer.Ordinal.GetHashCode(PaidId) : 0);
                hash = hash * 31 + World.GetHashCode();
                hash = hash * 31 + Life.GetHashCode();
                hash = hash * 31 + Pointer.GetHashCode();
                hash = hash * 31 + Instance;
                hash = hash * 31 + Slot;
                hash = hash * 31 + RuntimeHelpers.GetHashCode(Owner);
                return hash;
            }
        }

        public static bool operator ==(ShopStockToken left, ShopStockToken right) => left.Equals(right);
        public static bool operator !=(ShopStockToken left, ShopStockToken right) => !left.Equals(right);
    }

    /// <summary>调用方（Root 的原生 adapter）填入的当前真实原生状态；控制器不假设任何枚举默认值
    /// （例如敌策原值可以是任意值，不默认 Anybody=0）。Valid=false 表示 adapter 已证明该 token
    /// 指向的对象失效/回收。</summary>
    internal struct ShopStockObservation
    {
        internal bool Valid;
        internal bool PickedUp;
        internal bool FriendlyClaimed;
        internal bool EnemyClaimed;
        internal bool StockOwned;
        internal bool VisualParked;
        internal int FriendlyPolicy;
        internal int EnemyPolicy;
        internal float X;
        internal float Y;
        internal float Z;
    }

    /// <summary>caller-proven 原生读写边界。每个写委托都必须在写入前证明 token 同一。
    /// VisualParked 只允许被本 mod 自己的视觉 marker 消费，绝不用于禁用原生 GameObject/组件；
    /// 控制器不做任何物理修改（原生 stock 物理归属不变），Root 只在自身原生 kinematic 条件
    /// 证明后调用 Park/Place。</summary>
    internal sealed class ShopStockAccess
    {
        internal ShopStockToken Token;
        internal Func<ShopStockToken, ShopStockObservation> Read;
        internal Action<ShopStockToken, int> SetFriendlyPolicy;
        internal Action<ShopStockToken, int> SetEnemyPolicy;
        internal Action<ShopStockToken, bool> SetVisualParked;
        internal Action<ShopStockToken, float, float, float> Move;
    }

    /// <summary>无店间隙的库存收存控制器。Protect：仅收存经证实仍在上架、未拾取、无人认领的已付库存，
    /// 双双封锁友/敌领取策略后隐藏视觉，且只捕获一次实际原值；Park：保持封锁与同一身份，凭新鲜
    /// 预检/认领检查后移动到安全位；Place：先在封锁状态下移动到有效货架，再按 CAS 恢复自己的字段
    /// 并结束租约；Release：保持当前位置（绝不传回不安全旧址），只恢复自己仍持有的字段并结束租约；
    /// ReleaseAll：幂等，对“仍有效但没恢复成的自有字段”返回 false/可见 fault，不丢弃所有权、不隐藏失败。
    /// 失效/回收的 token 只丢弃托管租约，不触原生。外来写者接管字段：不覆写、该字段所有权终止、
    /// fault 有界可见。</summary>
    internal sealed class CustomShopStockTransit
    {
        private const float MoveTolerance = 1e-3f;
        private const int FaultCap = 200;

        private enum FieldKind { Friendly, Enemy, Visual }
        private enum FieldOutcome { Resolved, Foreign, Unresolved, Recycled }
        private enum MoveOutcome { Verified, RecycledDropped, AbortedResolved, FaultKept }

        private readonly struct ResolveSummary
        {
            internal readonly bool Ended;        // lease 已结束（全部字段恢复/外来接管/对象回收）
            internal readonly string Foreign;    // 被外来写者接管的字段（未恢复原值，所有权已终止）
            internal readonly string Unresolved; // 仍属我们但未恢复的字段（lease 保留供重试）
            internal ResolveSummary(bool ended, string foreign, string unresolved)
            { Ended = ended; Foreign = foreign; Unresolved = unresolved; }
        }

        private sealed class Lease
        {
            internal ShopStockToken Token;
            internal ShopStockAccess Access;
            internal int OriginalFriendlyPolicy;
            internal int OriginalEnemyPolicy;
            internal bool OriginalVisualParked;
            internal bool OwnFriendly;
            internal bool OwnEnemy;
            internal bool OwnVisual;
            internal string ForeignTaken; // 检测到外来接管过的字段（所有权已终止、未恢复原值），租约结束时如实上报
        }

        private readonly int _maxItems;
        private readonly int _blockedPolicy;
        private readonly Dictionary<ShopStockToken, Lease> _leases = new Dictionary<ShopStockToken, Lease>();
        private string _lastFault;

        internal CustomShopStockTransit(int maxItems = 4, int blockedPolicy = 5)
        {
            if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems), maxItems, "maxItems must be >= 1");
            if (blockedPolicy < 0) throw new ArgumentOutOfRangeException(nameof(blockedPolicy), blockedPolicy, "blockedPolicy must be >= 0");
            _maxItems = maxItems;
            _blockedPolicy = blockedPolicy;
        }

        internal int HeldCount => _leases.Count;
        internal string LastFault => _lastFault;

        // —— 公共操作 ——

        internal bool Protect(ShopStockAccess item)
        {
            if (item == null) return Fail("protect", "null-item", default);
            var shape = TokenShapeFault(item.Token);
            if (shape != null) return Fail("protect", "bad-token:" + shape, item.Token);
            if (item.Read == null) return Fail("protect", "adapter-missing:Read", item.Token);
            if (item.SetFriendlyPolicy == null) return Fail("protect", "adapter-missing:SetFriendlyPolicy", item.Token);
            if (item.SetEnemyPolicy == null) return Fail("protect", "adapter-missing:SetEnemyPolicy", item.Token);
            if (item.SetVisualParked == null) return Fail("protect", "adapter-missing:SetVisualParked", item.Token);

            if (_leases.TryGetValue(item.Token, out var held))
                return ProtectRepeat(held); // 同一 token 重复保护：幂等，不重捕获、不重写

            foreach (var other in _leases.Values)
                if (string.Equals(other.Token.PaidId, item.Token.PaidId, StringComparison.Ordinal))
                    return Fail("protect", "identity-rebind (same paid id is already held by another token)", item.Token);

            if (_leases.Count >= _maxItems)
                return Fail("protect", "capacity " + _leases.Count + "/" + _maxItems, item.Token);

            var lease = new Lease { Token = item.Token, Access = item };
            if (!TryRead(lease, "protect", out var obs))
                return false; // Read 抛异常：不建立 lease，fault 已写（不判失效）
            var reason = IneligibleReason(obs);
            if (reason != null) return Fail("protect", "ineligible:" + reason, lease.Token);

            // 只捕获一次实际原值（敌策可为任意值，不假设默认）
            lease.OriginalFriendlyPolicy = obs.FriendlyPolicy;
            lease.OriginalEnemyPolicy = obs.EnemyPolicy;
            lease.OriginalVisualParked = obs.VisualParked;

            if (!AcquireField(lease, FieldKind.Friendly)) return false;
            if (!AcquireField(lease, FieldKind.Enemy)) return false;
            if (!AcquireField(lease, FieldKind.Visual)) return false;

            _leases[lease.Token] = lease;
            _lastFault = null;
            return true;
        }

        internal bool Park(ShopStockAccess item, float x, float y, float z)
        {
            if (item == null) return Fail("park", "null-item", default);
            if (item.Read == null) return Fail("park", "adapter-missing:Read", item.Token);
            if (item.Move == null) return Fail("park", "adapter-missing:Move", item.Token);
            if (!_leases.TryGetValue(item.Token, out var lease)) return Fail("park", "not-held", item.Token);
            if (!CoordsFinite(x, y, z)) return Fail("park", "coord-invalid", item.Token);
            lease.Access = item; // 同一 token 的最新 caller-proven adapter
            var outcome = MoveUnderHold(lease, x, y, z, "park");
            if (outcome == MoveOutcome.RecycledDropped || outcome == MoveOutcome.Verified)
            { _lastFault = null; return true; } // Verified：保留封锁与 lease
            return false;
        }

        internal bool Place(ShopStockAccess item, float x, float y, float z)
        {
            if (item == null) return Fail("place", "null-item", default);
            if (item.Read == null) return Fail("place", "adapter-missing:Read", item.Token);
            if (item.Move == null) return Fail("place", "adapter-missing:Move", item.Token);
            if (!_leases.TryGetValue(item.Token, out var lease)) return Fail("place", "not-held", item.Token);
            if (!CoordsFinite(x, y, z)) return Fail("place", "coord-invalid", item.Token);
            lease.Access = item;
            var outcome = MoveUnderHold(lease, x, y, z, "place"); // 先在封锁状态下移动
            if (outcome == MoveOutcome.RecycledDropped) { _lastFault = null; return true; }
            if (outcome != MoveOutcome.Verified) return false;   // abort（已恢复并结束）或保留重试
            return ReportResolve("place", FinishLease(lease), lease.Token); // 再按 CAS 恢复自己的字段并结束租约
        }

        internal bool Release(ShopStockToken token)
        {
            if (!_leases.TryGetValue(token, out var lease)) return Fail("release", "unknown-token", token);
            return ReportResolve("release", FinishLease(lease), token); // 不移动：保持当前位置
        }

        internal bool ReleaseAll()
        {
            if (_leases.Count == 0) { _lastFault = null; return true; }
            var keys = new List<ShopStockToken>(_leases.Keys);
            bool allOk = true;
            string firstFault = null;
            foreach (var key in keys)
            {
                if (!_leases.TryGetValue(key, out var lease)) continue;
                if (!ReportResolve("release-all", FinishLease(lease), key))
                {
                    allOk = false;
                    if (firstFault == null) firstFault = _lastFault;
                }
            }
            if (allOk) { _lastFault = null; return true; }
            if (firstFault != null) _lastFault = firstFault;
            return false;
        }

        internal bool IsHeld(ShopStockToken token) => _leases.ContainsKey(token);

        // —— 内部：取得（block both then hide）——

        private bool AcquireField(Lease lease, FieldKind kind)
        {
            if (!TryRead(lease, "protect", out var before))
                return AbortAcquire(lease, "read-failed"); // 读异常：未建立所有权，直接失败
            var reason = IneligibleReason(before);
            if (reason != null) return AbortAcquire(lease, "ineligible:" + reason);
            if (kind == FieldKind.Visual)
            {
                if (before.VisualParked != lease.OriginalVisualParked)
                    return AbortAcquire(lease, "foreign:VisualParked");
            }
            else if (ReadPolicy(before, kind) != OrigPolicy(lease, kind))
                return AbortAcquire(lease, "foreign:" + FieldName(kind));

            // 写入尝试后即按我方持有对待（含 throw/未验证的未知态，由回滚 reread/CAS 判定）
            SetOwned(lease, kind, true);
            if (!TryWrite(lease, kind, "protect",
                    kind == FieldKind.Visual
                        ? (Action)(() => lease.Access.SetVisualParked(lease.Token, true))
                        : () => WritePolicy(lease, kind, _blockedPolicy),
                    out var throwDetail))
                return AbortAcquire(lease, throwDetail ?? ("write-failed:" + FieldName(kind)));
            if (!TryRead(lease, "protect", out var after))
                return AbortAcquire(lease, "read-after-write-failed"); // 已写字段由回滚处理
            if (!after.Valid) return AbortAcquire(lease, "recycled");
            reason = IneligibleReason(after);
            if (reason != null) return AbortAcquire(lease, "ineligible:" + reason);
            bool verified = kind == FieldKind.Visual ? after.VisualParked == true : ReadPolicy(after, kind) == _blockedPolicy;
            if (!verified) return AbortAcquire(lease, "unverified:" + FieldName(kind));
            return true;
        }

        private bool AbortAcquire(Lease lease, string cause)
        {
            // 逆序回滚已写字段：只写“当前值仍是我们哨兵值”的字段；外来接管的不覆写
            ResolveOne(lease, FieldKind.Visual);
            ResolveOne(lease, FieldKind.Enemy);
            ResolveOne(lease, FieldKind.Friendly);
            if (lease.OwnFriendly || lease.OwnEnemy || lease.OwnVisual)
            {
                // 有字段仍被我们封锁且没能恢复：保留 lease（不隐藏失败、不丢弃所有权），供 ReleaseAll 重试
                _leases[lease.Token] = lease;
                Fault("protect", cause + "; lease-kept:" + OwnedNames(lease), lease.Token);
            }
            else Fault("protect", cause, lease.Token);
            return false;
        }

        private bool ProtectRepeat(Lease lease)
        {
            if (!TryRead(lease, "protect", out var obs))
                return false; // 读异常：保留 lease 与所有权，绝不当作失效
            if (!obs.Valid) { DropLease(lease); return Fail("protect", "recycled", lease.Token); }
            var reason = IneligibleReason(obs);
            if (reason != null)
            {
                var s = FinishLease(lease); // 拒绝新持有：恢复仍属自己的字段并结束租约
                if (!s.Ended) return Fail("protect", "ineligible:" + reason + "; unresolved:" + s.Unresolved, lease.Token);
                return Fail("protect", "ineligible:" + reason + (s.Foreign != null ? "; foreign:" + s.Foreign : ""), lease.Token);
            }
            var foreign = SyncOwnership(lease, obs);
            if (!lease.OwnFriendly || !lease.OwnEnemy || !lease.OwnVisual)
                return Fail("protect", (foreign != null ? "foreign:" + foreign + "; " : "") + "partial-hold:" + OwnedNames(lease), lease.Token);
            _lastFault = null;
            return true; // 幂等成功：不重捕获、不重写
        }

        // —— 内部：保持封锁的移动（Park/Place 共用）——

        private MoveOutcome MoveUnderHold(Lease lease, float x, float y, float z, string op)
        {
            if (!TryRead(lease, op, out var pre))
                return MoveOutcome.FaultKept; // 读异常：保留 lease 与所有权
            if (!pre.Valid) { DropLease(lease); return MoveOutcome.RecycledDropped; }
            var foreign = SyncOwnership(lease, pre);
            var reason = IneligibleReason(pre);
            if (reason != null || foreign != null)
                return AbortMove(lease, op, reason != null ? "ineligible:" + reason : "foreign:" + foreign);
            if (!TryWrite(lease, null, op, () => lease.Access.Move(lease.Token, x, y, z), out _))
                return MoveOutcome.FaultKept; // 写异常：位置未知但不回滚，保留 lease 供重试
            if (!TryRead(lease, op, out var post))
                return MoveOutcome.FaultKept; // 读异常：保留所有权（不判失效）
            if (!post.Valid) { DropLease(lease); return MoveOutcome.RecycledDropped; }
            if (MathF.Abs(post.X - x) > MoveTolerance || MathF.Abs(post.Y - y) > MoveTolerance || MathF.Abs(post.Z - z) > MoveTolerance)
            { Fault(op, "move-unverified", lease.Token); return MoveOutcome.FaultKept; }
            foreign = SyncOwnership(lease, post);
            reason = IneligibleReason(post);
            if (reason != null || foreign != null)
                return AbortMove(lease, op, reason != null ? "ineligible:" + reason : "foreign:" + foreign);
            return MoveOutcome.Verified;
        }

        private MoveOutcome AbortMove(Lease lease, string op, string cause)
        {
            var s = FinishLease(lease); // 拒绝继续：恢复仍属自己的字段；位置停在当前位，不回滚
            if (!s.Ended) { Fault(op, cause + "; unresolved:" + s.Unresolved, lease.Token); return MoveOutcome.FaultKept; }
            if (s.Foreign != null && cause.IndexOf("foreign:", StringComparison.Ordinal) < 0) cause += "; foreign:" + s.Foreign;
            Fault(op, cause, lease.Token);
            return MoveOutcome.AbortedResolved;
        }

        // —— 内部：按 CAS 恢复自己的字段并结束租约 ——

        private ResolveSummary FinishLease(Lease lease)
        {
            var v = ResolveOne(lease, FieldKind.Visual);   // 逆于取得顺序：先撤隐藏
            if (v == FieldOutcome.Recycled) { DropLease(lease); return new ResolveSummary(true, null, null); }
            var e = ResolveOne(lease, FieldKind.Enemy);
            if (e == FieldOutcome.Recycled) { DropLease(lease); return new ResolveSummary(true, null, null); }
            var f = ResolveOne(lease, FieldKind.Friendly);
            if (f == FieldOutcome.Recycled) { DropLease(lease); return new ResolveSummary(true, null, null); }
            string foreign = lease.ForeignTaken, unresolved = null;
            void Note(FieldOutcome o, FieldKind k)
            {
                // 外来接管名已由 NoteForeignTaken/MarkForeign 记入 lease.ForeignTaken，这里只收集 unresolved
                if (o == FieldOutcome.Unresolved) unresolved = unresolved == null ? FieldName(k) : unresolved + "," + FieldName(k);
            }
            Note(v, FieldKind.Visual); Note(e, FieldKind.Enemy); Note(f, FieldKind.Friendly);
            if (unresolved != null) return new ResolveSummary(false, foreign, unresolved); // 保留 lease 供重试
            DropLease(lease);
            return new ResolveSummary(true, foreign, null);
        }

        private FieldOutcome ResolveOne(Lease lease, FieldKind kind)
        {
            if (!IsOwned(lease, kind)) return FieldOutcome.Resolved;
            if (!TryRead(lease, "resolve", out var pre))
                return FieldOutcome.Unresolved; // 读异常：所有权保留
            if (!pre.Valid) { SetOwned(lease, kind, false); return FieldOutcome.Recycled; }
            if (kind == FieldKind.Visual)
            {
                if (pre.VisualParked == lease.OriginalVisualParked) { SetOwned(lease, kind, false); return FieldOutcome.Resolved; }
                if (pre.VisualParked != true) { SetOwned(lease, kind, false); NoteForeignTaken(lease, FieldName(kind)); return FieldOutcome.Foreign; } // 外来接管
                if (!TryWrite(lease, kind, "resolve", () => lease.Access.SetVisualParked(lease.Token, lease.OriginalVisualParked), out _))
                    return RecoverAfterThrow(lease, kind);
                if (!TryRead(lease, "resolve", out var post)) return FieldOutcome.Unresolved;
                if (!post.Valid) { SetOwned(lease, kind, false); return FieldOutcome.Recycled; }
                if (post.VisualParked == true && post.VisualParked != lease.OriginalVisualParked)
                    return FieldOutcome.Unresolved; // silent no-op: still our sentinel, still our responsibility
                SetOwned(lease, kind, false);
                return post.VisualParked == lease.OriginalVisualParked ? FieldOutcome.Resolved : MarkForeign(lease, kind);
            }
            int cur = ReadPolicy(pre, kind);
            int orig = OrigPolicy(lease, kind);
            if (cur == orig) { SetOwned(lease, kind, false); return FieldOutcome.Resolved; } // 目标值已在（含上次 throw 前已写入的情形）
            if (cur != _blockedPolicy) { SetOwned(lease, kind, false); NoteForeignTaken(lease, FieldName(kind)); return FieldOutcome.Foreign; } // 外来接管，不覆写
            if (!TryWrite(lease, kind, "resolve", () => WritePolicy(lease, kind, orig), out _))
                return RecoverAfterThrow(lease, kind);
            if (!TryRead(lease, "resolve", out var post2)) return FieldOutcome.Unresolved;
            if (!post2.Valid) { SetOwned(lease, kind, false); return FieldOutcome.Recycled; }
            if (ReadPolicy(post2, kind) == _blockedPolicy && ReadPolicy(post2, kind) != orig)
                return FieldOutcome.Unresolved;
            SetOwned(lease, kind, false);
            return ReadPolicy(post2, kind) == orig ? FieldOutcome.Resolved : MarkForeign(lease, kind);
        }

        /// <summary>写抛异常后的有界恢复：reread 一次。值已是目标 → 恢复完成；仍是哨兵 → 未恢复（保留
        /// 所有权供显式重试）；其它 → 维持 Unresolved，由下一次 ResolveOne 的 CAS 判定。</summary>
        private FieldOutcome RecoverAfterThrow(Lease lease, FieldKind kind)
        {
            if (!TryRead(lease, "recover", out var obs))
                return FieldOutcome.Unresolved; // 读异常：所有权保留，绝不判失效
            if (!obs.Valid) { SetOwned(lease, kind, false); return FieldOutcome.Recycled; }
            bool restored = kind == FieldKind.Visual
                ? obs.VisualParked == lease.OriginalVisualParked
                : ReadPolicy(obs, kind) == OrigPolicy(lease, kind);
            if (restored) { SetOwned(lease, kind, false); return FieldOutcome.Resolved; }
            return FieldOutcome.Unresolved;
        }

        // —— 内部：工具 ——

        /// <summary>发现外来写者：只更新所有权标志（该字段所有权终止），绝不覆写。</summary>
        private string SyncOwnership(Lease lease, in ShopStockObservation o)
        {
            string foreign = null;
            if (lease.OwnFriendly && o.FriendlyPolicy != _blockedPolicy) { lease.OwnFriendly = false; NoteForeignTaken(lease, "FriendlyPolicy"); foreign = AppendName(foreign, "FriendlyPolicy"); }
            if (lease.OwnEnemy && o.EnemyPolicy != _blockedPolicy) { lease.OwnEnemy = false; NoteForeignTaken(lease, "EnemyPolicy"); foreign = AppendName(foreign, "EnemyPolicy"); }
            if (lease.OwnVisual && o.VisualParked != true) { lease.OwnVisual = false; NoteForeignTaken(lease, "VisualParked"); foreign = AppendName(foreign, "VisualParked"); }
            return foreign;
        }

        private static void NoteForeignTaken(Lease lease, string fieldName)
        {
            if (lease.ForeignTaken == null) lease.ForeignTaken = fieldName;
            else if (lease.ForeignTaken.IndexOf(fieldName, StringComparison.Ordinal) < 0)
                lease.ForeignTaken = lease.ForeignTaken + "," + fieldName;
        }

        private static FieldOutcome MarkForeign(Lease lease, FieldKind kind)
        {
            SetOwned(lease, kind, false);
            NoteForeignTaken(lease, FieldName(kind));
            return FieldOutcome.Foreign;
        }

        private static string AppendName(string list, string name) => list == null ? name : list + "," + name;

        private bool ReportResolve(string op, in ResolveSummary s, ShopStockToken token)
        {
            if (!s.Ended) return Fail(op, "unresolved:" + s.Unresolved, token);
            if (s.Foreign != null) return Fail(op, "foreign:" + s.Foreign, token);
            _lastFault = null;
            return true;
        }

        private bool TryRead(Lease lease, string op, out ShopStockObservation obs)
        {
            obs = default;
            try { obs = lease.Access.Read(lease.Token); return true; }
            catch (Exception e)
            {
                Fault(op, "throw:Read<" + e.GetType().Name + "> " + Bounded(e.Message, 60), lease.Token);
                return false;
            }
        }

        private bool TryWrite(Lease lease, FieldKind? kind, string op, Action write, out string throwDetail)
        {
            throwDetail = null;
            try { write(); return true; }
            catch (Exception e)
            {
                throwDetail = "throw:" + (kind == null ? "Move" : FieldName(kind.Value)) + "<" + e.GetType().Name + "> " + Bounded(e.Message, 60);
                Fault(op, throwDetail, lease.Token);
                return false;
            }
        }

        private void DropLease(Lease lease) => _leases.Remove(lease.Token);

        private static string IneligibleReason(in ShopStockObservation o)
        {
            if (!o.Valid) return "invalid";
            if (!o.StockOwned) return "not-stock";
            if (o.PickedUp) return "picked";
            if (o.FriendlyClaimed) return "friendly-claim";
            if (o.EnemyClaimed) return "enemy-claim";
            return null;
        }

        private static string TokenShapeFault(in ShopStockToken t)
        {
            if (t.PaidId == null || t.PaidId.Length == 0) return "empty-paidid";
            if (t.Owner == null) return "null-owner";
            if (t.Pointer == 0) return "zero-pointer";
            if (t.Slot < 0) return "slot<0";
            return null;
        }

        private static string FieldName(FieldKind k) => k == FieldKind.Friendly ? "FriendlyPolicy" : k == FieldKind.Enemy ? "EnemyPolicy" : "VisualParked";
        private static int ReadPolicy(in ShopStockObservation o, FieldKind k) => k == FieldKind.Friendly ? o.FriendlyPolicy : o.EnemyPolicy;
        private static int OrigPolicy(Lease l, FieldKind k) => k == FieldKind.Friendly ? l.OriginalFriendlyPolicy : l.OriginalEnemyPolicy;
        private static void WritePolicy(Lease l, FieldKind k, int v)
        {
            if (k == FieldKind.Friendly) l.Access.SetFriendlyPolicy(l.Token, v);
            else l.Access.SetEnemyPolicy(l.Token, v);
        }
        private static bool IsOwned(Lease l, FieldKind k) => k == FieldKind.Friendly ? l.OwnFriendly : k == FieldKind.Enemy ? l.OwnEnemy : l.OwnVisual;
        private static void SetOwned(Lease l, FieldKind k, bool owned)
        {
            if (k == FieldKind.Friendly) l.OwnFriendly = owned;
            else if (k == FieldKind.Enemy) l.OwnEnemy = owned;
            else l.OwnVisual = owned;
        }
        private static string OwnedNames(Lease l)
        {
            var names = new List<string>(3);
            if (l.OwnFriendly) names.Add("FriendlyPolicy");
            if (l.OwnEnemy) names.Add("EnemyPolicy");
            if (l.OwnVisual) names.Add("VisualParked");
            return string.Join(",", names);
        }
        private static bool CoordsFinite(float x, float y, float z) => float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z);
        private static string Bounded(string s, int cap) => s != null && s.Length > cap ? s.Substring(0, cap) : s;
        private static string PaidTag(in ShopStockToken t)
        {
            var id = t.PaidId ?? string.Empty;
            return id.Length <= 32 ? id : id.Substring(0, 32);
        }
        private void Fault(string op, string detail, ShopStockToken t) =>
            _lastFault = Bounded("[CustomShopStockTransit] " + op + " " + detail + " paid=" + PaidTag(in t), FaultCap);
        private bool Fail(string op, string detail, ShopStockToken t) { Fault(op, detail, t); return false; }
    }
}
