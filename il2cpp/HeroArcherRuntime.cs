// 英雄弓箭手·身份/生命周期/射程 slice（契约 operator 2026-09-14 锁定）。
//
// 职责边界（本文件只做这些）：
//   * 身份：仅激活 HeroRecruitment 已购买的 Archer（每侧最多 1 名），F5 开关 ModConfig.HeroArcherEnabled
//     默认 off、全 world 生效；关闭/失活/死亡/role 变/side 变/换 world 一律撤销并恢复原状。
//   * 射程：只写英雄自身的 Archer.shootRange = 基准 ×2；归还带 CAS（第三方改过就不覆盖）。
//     不写 Scanner.range/rangeBehind（原生每条路径自行从 shootRange 同步，见 result.md 待核项），
//     不碰射击间隔/箭数/伤害等其他攻击字段，不碰共享 ArrowAttack SO。
//   * 动画：HeroArcherVisuals 负责像素表现，这里只在 hero 状态变化与每帧 Tick 时驱动它。
//   * 移动：HeroArcherMovement 认领英雄本人 Archer.walkSpeed/runSpeed = 基准 ×1.5（逐字段 CAS 归还；
//     死地随从让位给 DL 的同倍率 Mover 提升）。本文件只在当选/每帧/退役漏斗/关闭处驱动它，不写速度字段。
//
// 锁定 API（其它 slice 只认这 8 个）：
//   bool Enabled { get; }            - 配置开关的即时读数（含所有 gate）
//   bool IsHero(Archer)              - 当前 live 身份是否英雄（战斗 slice 的资格前提；含 pointer/GOID/life 核对）
//   bool IsCombatEligible(Archer)    - IsHero 且当下确实在与「敌人」交战（排除野生动物/友好巨魔、
//                                      跨 world、弩手、北境近战随从、embarked、inert、grabbed、玩家控制）
//   void Observe(Archer)             - 由既有 Archer.Update prefix 桥接调用（每帧，不额外扫描）
//   void OnEnable(Archer)            - 由既有 Archer.OnEnable prefix 桥接（池复用 = 新 life；先归还上一 life 的射程回执）
//   void Tick()                      - 由 ModPanel.Update 直接调用（operator 接线）
//   void OnShot(Archer)              - 由原生 ArrowAttack.FireArrowInternal 每发一次桥接（视觉 release 事件）
//   void Clear()                     - 全量撤销（停用/世界切换/模组关闭，零残留）
// 本文件**不含任何 Harmony 挂钩**：Observe/OnEnable/OnShot 由 combat worker 在既有原生钩子上桥接，
// Tick 由 operator 直接调用 —— 因此这里绝不 patch 自有 ModPanel.Update，也不新增任何 native 入口。
//
// 射程现状（**未完成，不得当成已满足**）：本 slice 只做 Archer.shootRange 的 ×2 CAS 写入/归还。
//   原生的索敌并非只读 shootRange——ShouldShootEnemy 还比较 ActiveArrowAttack.Range，Scanner.range/rangeBehind
//   也不会在每条路径上跟着字段自动更新；且发射力度（FireArrowInternal 已算好的 shootForce）不受本 slice 影响。
//   这些由「每 actor 自有 ArrowAttack 克隆（先解算初速再交给原生弹道）」的独立 slice 负责，本文件不碰、
//   也不得宣称索敌距离/弹道已达成。
//
// 身份 key：native 指针 + GameObject InstanceID + life（life 只在 Archer.OnEnable 递增）。
//   * 相同 Actor（同 pointer/GOID/life）稳定保留，绝不因为「这帧没看到」被顶替。
//   * 池复用（同 GOID 同 pointer 再来一次 OnEnable）视为新 life：旧回执先归还，旧英雄位失效。
//   * 不按 transform.position.x 判 side（优先 Archer.side）；不宣称跨读档永久同人，不写任何存档。
//
// 联机：见 HeroArcherNetwork —— 本 slice 整体 fail-closed（在线时连主机也不启用英雄），
//   因此这里没有「客户端自己挑英雄」的路径，也没有任何 RPC；双方同版本前不产生主客显示分歧。
//
// 纯逻辑（HeroArcherCore / HeroRangeClaim）不依赖 Unity，用 HERO_ARCHER_TEST 编译给无游戏进程的
// 单元测试；Unity 侧胶水（HeroArcherRuntime）及其对 interop/Unity/Harmony 的类型引用在测试编译中被排除。

using System;
using System.Collections.Generic;
#if !HERO_ARCHER_TEST
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

/// <summary>
/// 射程回执：记录「我们写进去的值」与「写之前的原值」，归还是 CAS——
/// 只有现值仍等于我们写入的值时才写回原值；第三方（原生其它路径/其它模组）改过就保留对方值，
/// 只放弃所有权。同一实例重复 Apply 不会叠加（拿当前值当新基准的调用方由 HeroArcherCore 挡）。
/// </summary>
internal struct HeroRangeClaim
{
    internal const float Tolerance = 0.0005f;

    /// <summary>写之前读到的原生值。</summary>
    internal float Original;

    /// <summary>我们写进去的放大值（= Original × 倍率）。</summary>
    internal float Written;

    /// <summary>true = 我们还持有这个写入；false = 已归还或已让渡给第三方。</summary>
    internal bool Owned;

    internal static bool Same(float a, float b) => Math.Abs(a - b) <= Tolerance;

    internal static HeroRangeClaim Create(float baseRange, float multiplier)
    {
        HeroRangeClaim claim = default;
        claim.Original = baseRange;
        claim.Written = baseRange * multiplier;
        claim.Owned = true;
        return claim;
    }

    /// <summary>归还：现值 == 写入值 → 写回原值并返回 true；第三方值 → 不动并返回 false（所有权一律释放）。</summary>
    internal static bool TryRestore(ref float current, ref HeroRangeClaim claim)
    {
        if (!claim.Owned) return false;
        claim.Owned = false;
        if (!Same(current, claim.Written)) return false;
        current = claim.Original;
        return true;
    }

    /// <summary>
    /// 重新断言（每帧巡检用）：现值仍是我们的写入值 → true（无事）；
    /// 现值被原生重置回基准 → 再写一次放大值并返回 true；现值是第三方值 → false（调用方放弃回执）。
    /// </summary>
    internal static bool TryReassert(ref float current, in HeroRangeClaim claim)
    {
        if (!claim.Owned) return false;
        if (Same(current, claim.Written)) return true;
        if (!Same(current, claim.Original)) return false;
        current = claim.Written;
        return true;
    }
}

/// <summary>
/// 英雄身份纯逻辑（无 Unity 依赖、可脱离游戏进程测试）：
/// 观察注册表（goId → side/可选性）+ 每侧至多 1 个英雄位 + 射程回执账本。
///
/// 语义：
///  * <see cref="Observe"/>：upsert 一行；身份（side）变化或「不再可选」时立刻撤销该 goId 的英雄位，
///    绝不把它留在位上（撤销后由 Tick 的确定性命中补人）。容量有界（<see cref="MaxTrackedActors"/>）。
///  * <see cref="TryOpenSlot"/>：只在该侧空位时补人；候选 = side 匹配且可选的行中 goId 最小者
///    （与遍历顺序无关，纯确定性；不按 position.x、不按随机数）。
///  * 射程回执按 goId 记账，池复用/撤销一律先归还（<see cref="TakeClaim"/>）再谈新身份。
/// </summary>
internal sealed class HeroArcherCore
{
    internal const int NoHero = 0;
    internal const int MaxTrackedActors = 512;

    private readonly struct Row
    {
        internal readonly int Side;          // -1 / +1；0 = 未知（不可当选）
        internal readonly bool Selectable;

        internal Row(int side, bool selectable)
        {
            Side = side;
            Selectable = selectable;
        }
    }

    private readonly Dictionary<int, Row> _rows = new Dictionary<int, Row>(MaxTrackedActors);
    private readonly Dictionary<int, HeroRangeClaim> _claims = new Dictionary<int, HeroRangeClaim>(8);
    private int _leftHero = NoHero;
    private int _rightHero = NoHero;

    internal int TrackedCount => _rows.Count;
    internal int ClaimCount => _claims.Count;
    internal int LeftHero => _leftHero;
    internal int RightHero => _rightHero;

    /// <summary>side 符号 → 内部 key（-1/0/+1）。原始值为 NaN/0 时一律 0（不可当选）。</summary>
    internal static int SideKey(float sideSign)
    {
        if (float.IsNaN(sideSign)) return 0;
        if (sideSign > 0f) return 1;
        if (sideSign < 0f) return -1;
        return 0;
    }

    internal static int SideKeyFrom(int side) => side > 0 ? 1 : (side < 0 ? -1 : 0);

    /// <summary>登记/刷新一个 actor。返回 false = 超出容量（调用方应记一次日志，不视为错误）。</summary>
    internal bool Observe(int goId, int side, bool selectable)
    {
        if (goId == 0) return false;
        bool tracked = _rows.TryGetValue(goId, out Row previous);
        if (!tracked && _rows.Count >= MaxTrackedActors) return false;

        Row row = new Row(SideKeyFrom(side), selectable);
        _rows[goId] = row;

        bool changed = tracked && (previous.Side != row.Side || previous.Selectable != row.Selectable);
        if (changed || !row.Selectable || row.Side == 0) RevokeSlotFor(goId);
        return true;
    }

    /// <summary>移除 actor（死亡/失活销毁/换 world/换 life）：同时撤销其英雄位。</summary>
    internal void Forget(int goId)
    {
        if (goId == 0) return;
        _rows.Remove(goId);
        RevokeSlotFor(goId);
    }

    internal int HeroOfSide(int side) => side > 0 ? _rightHero : (side < 0 ? _leftHero : NoHero);

    internal bool IsHero(int goId) => goId != 0 && (goId == _leftHero || goId == _rightHero);

    internal bool SlotsFull => _leftHero != NoHero && _rightHero != NoHero;

    /// <summary>该侧空位时按确定性规则补一名英雄；返回 true 且给出 goId。</summary>
    internal bool TryOpenSlot(int side, out int goId)
    {
        goId = NoHero;
        int key = SideKeyFrom(side);
        if (key == 0) return false;
        if (HeroOfSide(key) != NoHero) return false;

        int best = NoHero;
        foreach (KeyValuePair<int, Row> pair in _rows)
        {
            if (pair.Key == 0) continue;
            if (pair.Value.Side != key || !pair.Value.Selectable) continue;
            if (best == NoHero || pair.Key < best) best = pair.Key;
        }
        if (best == NoHero) return false;

        if (key > 0) _rightHero = best; else _leftHero = best;
        goId = best;
        return true;
    }

    /// <summary>撤销该侧英雄位（不补人）；返回被撤销的 goId（无则 NoHero）。</summary>
    internal int DropSlot(int side)
    {
        int key = SideKeyFrom(side);
        if (key > 0)
        {
            int previous = _rightHero;
            _rightHero = NoHero;
            return previous;
        }
        if (key < 0)
        {
            int previous = _leftHero;
            _leftHero = NoHero;
            return previous;
        }
        return NoHero;
    }

    private void RevokeSlotFor(int goId)
    {
        if (_leftHero == goId) _leftHero = NoHero;
        if (_rightHero == goId) _rightHero = NoHero;
    }

    /// <summary>清空全部身份与回执（模组关闭/世界切换：零残留）。</summary>
    internal void Clear()
    {
        _rows.Clear();
        _claims.Clear();
        _leftHero = NoHero;
        _rightHero = NoHero;
    }

    // ---- 射程回执账本（按 goId） ----

    internal void SetClaim(int goId, in HeroRangeClaim claim)
    {
        if (goId == 0) return;
        _claims[goId] = claim;
    }

    internal bool TryGetClaim(int goId, out HeroRangeClaim claim) => _claims.TryGetValue(goId, out claim);

    /// <summary>取出并移除回执（调用方负责按 CAS 归还原值）。</summary>
    internal bool TakeClaim(int goId, out HeroRangeClaim claim)
    {
        claim = default;
        if (goId == 0) return false;
        if (!_claims.TryGetValue(goId, out claim)) return false;
        _claims.Remove(goId);
        return true;
    }

    internal void DropClaim(int goId)
    {
        if (goId != 0) _claims.Remove(goId);
    }
}

#if !HERO_ARCHER_TEST

/// <summary>
/// Unity 侧胶水：观察既有 Archer、维护 live 身份、选/撤英雄、写回射程、驱动视觉。
/// 无场景扫描：只处理既有入口（Archer.Update / Archer.OnEnable / FireArrowInternal 的桥接调用、
/// 以及 operator 直调的 Tick）喂进来的对象，加上有界的自有注册表巡检（≤ <see cref="HeroArcherCore.MaxTrackedActors"/>）。
/// </summary>
internal static class HeroArcherRuntime
{
    /// <summary>英雄射程倍率（只乘自身 shootRange；必须与 <see cref="HeroArcherRange.RangeFactor"/> 同步）。</summary>
    internal const float RangeMultiplier = 2f;

    /// <summary>失联多久（游戏秒）后从注册表剔除（对象被销毁/场景卸载后的兜底，正常路径靠失活即撤）。</summary>
    private const float ActorStaleSeconds = 30f;

    /// <summary>视觉补挂载重试间隔（游戏秒）。</summary>
    private const float VisualRetrySeconds = 1f;

    private sealed class ActorState
    {
        internal Archer Ref;
        internal IntPtr Pointer;
        internal int GoId;
        internal int Life;
        internal int Side;
        internal long World;
        internal bool Alive;
        internal bool Hero;
        /// <summary>本 life 内出现过第三方改写射程：永久退出选举，绝不反复抢字段（新 life 复位）。</summary>
        internal bool RangeBlocked;
        internal float LastSeen;
        /// <summary>视觉补挂载的最早重试时刻（原生隐藏/临时失败可恢复，避免每帧重试）。</summary>
        internal float VisualRetryAt;
    }

    private struct LifeEntry
    {
        internal IntPtr Pointer;
        internal int Life;
    }

    private static readonly Dictionary<int, ActorState> _actors = new Dictionary<int, ActorState>(HeroArcherCore.MaxTrackedActors);
    private static readonly Dictionary<int, LifeEntry> _lives = new Dictionary<int, LifeEntry>(HeroArcherCore.MaxTrackedActors);
    private static readonly List<int> _scratch = new List<int>(HeroArcherCore.MaxTrackedActors);
    private static readonly HeroArcherCore _core = new HeroArcherCore();

    private static bool _capacityLogged;
    private static bool _authorityLogged;
    private static bool _lifeLogged;
    private static int _visualsRetryCount;

    /// <summary>配置 + 联机 gate 的即时读数；任何探测异常一律 false（fail-closed）。</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                if (ModConfig.HeroArcherEnabled == null || !ModConfig.HeroArcherEnabled.Value) return false;
                return ArcherOptionsScope.IsActive && HeroArcherNetwork.AllowsLocalHero;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 当前 live 身份是否英雄：配置与世界范围可用 + 注册表 live 身份（pointer/GOID/life）+ side/world 仍一致
    /// + **即时资格**（现算，绝不只看注册表标志）。战斗 slice 的资格前提。
    /// </summary>
    internal static bool IsHero(Archer archer)
    {
        try
        {
            if (!Enabled || !ArcherOptionsScope.IsActive || !HeroRecruitment.IsPurchased(archer)) return false;
            ActorState state = Find(archer);
            if (state == null || !state.Hero || !_core.IsHero(state.GoId)) return false;
            if (state.Side != SideKey(archer)) return false;
            if (state.World != CurrentWorldKey()) return false;
            return ImmediateEligible(archer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 英雄且当下确实在与「敌人」交战：身份与即时资格同上，且本次射击目标是 Enemies 层的敌方 Damageable
    /// （野生动物、友好巨魔一律排除）。任何未知状态 → false。
    /// </summary>
    internal static bool IsCombatEligible(Archer archer)
    {
        try
        {
            if (!IsHero(archer)) return false;
            return CombatTarget(archer._shootingTarget);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Archer.Update prefix：登记/刷新当前状态（不挑选、不写字段）。</summary>
    internal static void Observe(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            HeroRecruitment.Observe(archer);
            HeroArcherTowerPolicy.Observe(archer);
            if (!Enabled)
            {
                // 关闭时不做任何登记；残留由 Tick 统一回收（配置关→当帧即撤）。
                return;
            }

            int goId = SafeGoId(archer);
            if (goId == 0) return;

            IntPtr pointer = SafePointer(archer);
            int side = SideKey(archer);
            long world = CurrentWorldKey();
            int life = EnsureLife(goId, pointer);   // 开关在 later 打开时，既有 Archer 也要有 life（首次观测即建账）
            if (life == 0) return;                  // 账本已满：不登记（有界）

            if (!_actors.TryGetValue(goId, out ActorState state))
            {
                if (_actors.Count >= HeroArcherCore.MaxTrackedActors)
                {
                    LogOnce(ref _capacityLogged, "actor registry full (" + HeroArcherCore.MaxTrackedActors + "), ignoring new archers");
                    return;
                }
                state = new ActorState();
                state.Ref = archer;
                state.GoId = goId;
                state.Pointer = pointer;
                state.Life = life;
                state.Side = side;
                state.World = world;
                _actors[goId] = state;
            }
            else if (state.Pointer != pointer)
            {
                // 同 GOID 换了底层对象（原生对象销毁后 ID 复用）：按新 actor 重开，旧回执先归还。
                ResetIdentityTracking(state, "pointer-reuse");
                state.Ref = archer;
                state.Pointer = pointer;
                state.Life = life;
                state.Side = side;
                state.World = world;
            }
            else if (state.Life != life)
            {
                // 池复用/新生命（OnEnable 已抬 life）：旧身份与旧回执一律作废。
                ResetIdentityTracking(state, "life-change");
                state.Ref = archer;
                state.Life = life;
                state.Side = side;
                state.World = world;
            }
            else if (state.Side != side || state.World != world)
            {
                // side 变 / 换 world：撤销（绝不按 position.x 重判后继续挂在位上）。
                ResetIdentityTracking(state, "identity-change");
                state.Ref = archer;
                state.Side = side;
                state.World = world;
            }

            state.Ref = archer;
            state.LastSeen = Now();
            state.Alive = Alive(archer);

            bool eligible = state.Alive && !state.RangeBlocked && HeroRecruitment.IsPurchased(archer) && ImmediateEligible(archer);
            // 资格在两次 Tick 之间失效（换 role/上船/被玩家控制/换 side）：先归还我们的射程与视觉，
            // 再让 core 更新行——绝不留在英雄位上（core 的撤销不带归还责任）。
            if (state.Hero && !eligible) RetireHero(state, "role-change");
            _core.Observe(goId, side, eligible);
        }
        catch (Exception e)
        {
            LogOnce(ref _lifeLogged, "observe failed: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// Archer.OnEnable prefix：池复用 = 新 life。先把上一 life 的射程回执按 CAS 归还，
    /// 再抬 life（旧 key 立即失效），最后重新登记——新生命不会继承上一生命的英雄身份。
    /// </summary>
    internal static void OnEnable(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            HeroRecruitment.OnEnable(archer);
            int goId = SafeGoId(archer);
            if (goId == 0) return;
            IntPtr pointer = SafePointer(archer);

            bool tracked = _actors.TryGetValue(goId, out ActorState state);
            bool known = _lives.ContainsKey(goId);
            // 关闭状态下不为「没见过的 actor」建账；账本上限与 actor 注册表同量级（有界）。
            if (!known && !tracked && !Enabled) return;
            if (!known && _lives.Count >= HeroArcherCore.MaxTrackedActors) return;

            if (tracked) ResetIdentityTracking(state, "pool-reuse");   // 旧 life 的射程回执与视觉先归还

            if (known && _lives.TryGetValue(goId, out LifeEntry entry) && entry.Pointer == pointer)
            {
                entry.Life++;
                _lives[goId] = entry;
            }
            else
            {
                entry = default;
                entry.Pointer = pointer;
                entry.Life = 1;
                _lives[goId] = entry;
            }

            if (tracked)
            {
                state.Ref = archer;
                state.Pointer = pointer;
                state.Life = entry.Life;
                state.LastSeen = Now();
            }
        }
        catch (Exception)
        {
            // OnEnable 是池路径：任何异常都不得让原生流程中断；注册失败只影响本帧观测。
        }
    }

    /// <summary>由 operator 从 ModPanel.Update 直接调用：巡检 + 每侧补位 + 视觉驱动。无场景扫描。</summary>
    internal static void Tick()
    {
        try
        {
            HeroArcherRange.RetryCleanup();
            HeroArcherMovement.RetryCleanup();   // 关闭状态下也要先把移动提速的 pending 归还服务完
            HeroArcherGuardFacing.Tick();
            if (!Enabled)
            {
                if (_actors.Count > 0 || _core.TrackedCount > 0) ClearInternal("disabled");
                return;
            }

            float now = Now();
            long liveWorld = CurrentWorldKey();
            _scratch.Clear();
            foreach (KeyValuePair<int, ActorState> pair in _actors) _scratch.Add(pair.Key);

            int count = _scratch.Count;
            for (int i = 0; i < count; i++)
            {
                int goId = _scratch[i];
                if (!_actors.TryGetValue(goId, out ActorState state)) continue;

                Archer archer = state.Ref;
                if (archer == null || archer.gameObject == null || SafePointer(archer) != state.Pointer)
                {
                    Forget(state, "destroyed");
                    continue;
                }
                if (!archer.gameObject.activeInHierarchy || !archer.enabled)
                {
                    Forget(state, "inactive");
                    continue;
                }
                if (state.Life != CurrentLife(goId, state.Pointer))
                {
                    Forget(state, "life-mismatch");
                    continue;
                }
                if (state.World != liveWorld)
                {
                    Forget(state, "world-change");
                    continue;
                }
                // live side 核对：side 变了按身份变化撤销（不按 position.x 重判）。
                int liveSide = SideKey(archer);
                if (state.Side != liveSide)
                {
                    Forget(state, "side-change");
                    continue;
                }
                if (now - state.LastSeen > ActorStaleSeconds)
                {
                    // 兜底：Update 长时间没来过（对象被禁用但未销毁的极端路径）。撤销并释放槽位，
                    // 对象仍健在的话下一帧 Update 会重新登记、重新参选。
                    Forget(state, "stale");
                    continue;
                }

                bool alive = Alive(archer);
                if (!alive)
                {
                    Forget(state, "dead");
                    continue;
                }

                bool eligible = !state.RangeBlocked && HeroRecruitment.IsPurchased(archer) && ImmediateEligible(archer);
                if (state.Hero)
                {
                    if (!_core.TryGetClaim(goId, out HeroRangeClaim claim))
                    {
                        // 英雄位没有回执（异常中断残留）：按撤销处理，绝不空手持位。
                        RetireHero(state, "claim-missing");
                        continue;
                    }
                    float current = archer.shootRange;
                    if (!HeroRangeClaim.TryReassert(ref current, claim))
                    {
                        // 第三方写了别的值：尊重对方，放弃回执并撤销英雄位；本 life 不再参选（防抖动）。
                        RetireHero(state, "range-stolen");
                        state.RangeBlocked = true;
                        continue;
                    }
                    if (!HeroRangeClaim.Same(current, archer.shootRange)) archer.shootRange = current;
                    if (!HeroArcherRange.Tick(archer)) { RetireHero(state, "projectile-range-lost"); state.RangeBlocked = true; continue; }

                    if (!eligible)
                    {
                        RetireHero(state, "role-change");
                        continue;
                    }

                    // 移动提速：DL 让位 / 幂等维持；Pending 未归还不提升；失败只记一次日志，不影响其它效果。
                    HeroArcherMovement.Reconcile(archer);
                    HeroArcherGuardFacing.Evaluate(archer);
                }
                // 所有 actor（含未当选者）每帧刷新 core 的资格行；撤销永远先归还再让 core 更新。
                _core.Observe(goId, liveSide, eligible);
                // 上一轮归还因瞬时异常被放回的射程回执：这里重试（重试成功前 PromoteHero 的守卫会挡住重复当选）。
                if (!state.Hero && _core.TryGetClaim(goId, out _)) RestoreClaim(state, "retry");
            }

            for (int side = -1; side <= 1; side += 2)
            {
                if (_core.HeroOfSide(side) != HeroArcherCore.NoHero) continue;
                if (!_core.TryOpenSlot(side, out int goId)) continue;
                if (!_actors.TryGetValue(goId, out ActorState promoted)) continue;
                PromoteHero(promoted);
            }

            // 视觉补偿：最多 2 名英雄；原生一时隐藏（SetHideStatus/临时失败）恢复后重试挂载，
            // atlas 已判定不可用则不再重复尝试加载。
            _visualsRetryCount = 0;
            for (int i = 0; i < count && _visualsRetryCount < 2; i++)
            {
                if (!_actors.TryGetValue(_scratch[i], out ActorState state)) continue;
                if (!state.Hero || state.Ref == null || state.Ref.gameObject == null) continue;
                if (now < state.VisualRetryAt) continue;
                if (HeroArcherVisuals.HasVisual(state.Ref)) continue;
                if (HeroArcherVisuals.AtlasUnavailable) break;
                state.VisualRetryAt = now + VisualRetrySeconds;
                _visualsRetryCount++;
                HeroArcherVisuals.Apply(state.Ref);
            }
        }
        catch (Exception e)
        {
            LogOnce(ref _lifeLogged, "tick failed: " + e.GetType().Name);
        }
    }

    /// <summary>FireArrowInternal 的 Archer 侧入口（原生每发一次；视觉 release 事件优先）。</summary>
    internal static void OnShot(Archer archer)
    {
        try
        {
            if (archer == null) return;
            ActorState state = Find(archer);
            if (state == null || !state.Hero) return;
            HeroArcherVisuals.NotifyRelease(archer);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>全量撤销：归还全部射程回执、移除全部视觉、清空身份与 life 账本（零残留）。</summary>
    internal static void Clear()
    {
        try
        {
            ClearInternal("clear");
        }
        catch (Exception e)
        {
            LogOnce(ref _lifeLogged, "clear failed: " + e.GetType().Name);
        }
    }

    // ============================================================
    // 内部实现
    // ============================================================

    private static void ClearInternal(string reason)
    {
        _scratch.Clear();
        foreach (var pair in _actors) _scratch.Add(pair.Key);
        for (int i = 0; i < _scratch.Count; i++)
        {
            if (!_actors.TryGetValue(_scratch[i], out ActorState state)) continue;
            state.Hero = false;
            HeroArcherVisuals.Remove(state.Ref);
            _core.Forget(state.GoId);
            if (!RestoreClaim(state, reason)) continue;
            _actors.Remove(state.GoId);
            _lives.Remove(state.GoId);
        }
        if (_actors.Count == 0) { _lives.Clear(); _core.Clear(); }
        HeroArcherRange.Clear();
        HeroArcherMovement.Clear();
        HeroArcherGuardFacing.Clear();
        HeroArcherLiveDiagnostics.Clear();
        HeroArcherVisuals.Clear();
        _visualsRetryCount = 0;
    }

    private static void PromoteHero(ActorState state)
    {
        Archer archer = state.Ref;
        if (archer == null || archer.gameObject == null) return;
        if (state.Hero) return;                                            // 已在位：绝不重复接管
        if (_core.TryGetClaim(state.GoId, out _)) return;                  // 已有回执：绝不二次乘基准
        if (state.RangeBlocked) { _core.Forget(state.GoId); return; }       // 本 life 已被第三方抢过字段
        if (SafePointer(archer) != state.Pointer) return;                  // 换主：等 Tick 重开
        if (state.Life != CurrentLife(state.GoId, state.Pointer)) return;   // 换 life：旧包装不放行
        if (!HeroRecruitment.IsPurchased(archer) || !ImmediateEligible(archer)) { _core.Forget(state.GoId); return; } // 已购身份与即时资格都须成立

        HeroRangeClaim claim = HeroRangeClaim.Create(archer.shootRange, RangeMultiplier);
        if (!float.IsFinite(claim.Original) || !float.IsFinite(claim.Written) || claim.Original <= 0f)
        {
            // 未知/非法基准：fail-closed，不写字段也不占位。
            _core.Forget(state.GoId);
            return;
        }
        archer.shootRange = claim.Written;
        _core.SetClaim(state.GoId, claim);
        if (!HeroArcherRange.Apply(archer)) { RestoreClaim(state, "projectile-range-unavailable"); _core.Forget(state.GoId); state.RangeBlocked = true; return; }
        state.Hero = true;
        state.VisualRetryAt = 0f;
        HeroArcherVisuals.Apply(archer);
        HeroArcherMovement.Reconcile(archer);
        if (HeroArcherVisuals.HasVisual(archer)) HeroArcherLiveDiagnostics.OnHeroSetup(archer);
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroArcher] selected side=" + state.Side + " actor=" + state.GoId + " range=" + archer.shootRange + " visual=" + HeroArcherVisuals.HasVisual(archer) + " cloth=dynamic offline=true"); } catch { }
    }

    private static void RetireHero(ActorState state, string reason)
    {
        RestoreClaim(state, reason);
        if (state.Hero)
        {
            state.Hero = false;
            HeroArcherVisuals.Remove(state.Ref);
        }
        _core.Forget(state.GoId);
    }

    /// <summary>
    /// 按 CAS 归还射程回执。顺序刻意如此：先验证可达（同一 pointer）→ 再取回执 → 读/写；
    /// **任一 interop 调用抛错都把回执放回账本**（不丢归还责任，Tick 下次重试）；
    /// 对象已销毁/换主 → 无需归还，正常丢弃。归还失败（第三方值）只记录、不覆盖。
    /// </summary>
    private static bool RestoreClaim(ActorState state, string reason)
    {
        if (state == null || state.GoId == 0) return true;
        HeroArcherRange.Restore(state.Ref);
        HeroArcherMovement.Restore(state.Ref);
        HeroArcherGuardFacing.Restore(state.Ref);
        if (HeroArcherGuardFacing.HasOutstanding(state.GoId)) return false;
        if (!_core.TryGetClaim(state.GoId, out HeroRangeClaim existing)) return true;

        Archer archer = state.Ref;
        if (archer == null || archer.gameObject == null)
        {
            _core.DropClaim(state.GoId);   // 对象没了：没有字段可写
            return true;
        }

        IntPtr pointer = SafePointer(archer);
        if (pointer == IntPtr.Zero) return false;      // 指针读不到：保留回执，下次重试
        if (pointer != state.Pointer) { _core.DropClaim(state.GoId); return true; } // 换主：旧对象不可达

        if (!_core.TakeClaim(state.GoId, out HeroRangeClaim claim)) return true;
        HeroRangeClaim retry = claim;   // TryRestore 会释放 Owned；写/读失败时用它重建回执

        float current;
        try
        {
            current = archer.shootRange;
        }
        catch (Exception)
        {
            _core.SetClaim(state.GoId, retry);   // 读失败：放回，下次重试
            return false;
        }

        float beforeRestore = current;
        bool restored = HeroRangeClaim.TryRestore(ref current, ref claim);
        if (!HeroRangeClaim.Same(current, beforeRestore))
        {
            try
            {
                archer.shootRange = current;
            }
            catch (Exception)
            {
                _core.SetClaim(state.GoId, retry);   // 写失败：放回（Owned 仍为 true），下次重试
                return false;
            }
        }
        else if (!restored && (reason == "disabled" || reason == "clear"))
        {
            LogOnce(ref _authorityLogged, "range restore skipped: third-party value kept (" + reason + ")");
        }
        return true;
    }

    /// <summary>作废一个 actor 的身份与回执（life/side/world/底层对象变化或池复用时的统一入口）。</summary>
    private static void ResetIdentityTracking(ActorState state, string reason)
    {
        RestoreClaim(state, reason);
        if (state.Hero)
        {
            state.Hero = false;
            HeroArcherVisuals.Remove(state.Ref);
        }
        state.RangeBlocked = false;
        _core.Forget(state.GoId);
    }

    private static void Forget(ActorState state, string reason)
    {
        // 归还失败（interop 瞬时异常）：保留 actor 与回执，下一帧 Tick 再试（绝不丢归还责任）。
        if (!RestoreClaim(state, reason)) return;
        if (state.Hero)
        {
            state.Hero = false;
            HeroArcherVisuals.Remove(state.Ref);
        }
        _core.Forget(state.GoId);
        _core.DropClaim(state.GoId);   // 到这里（可达性已断或已归还）：不再持有归还责任
        _actors.Remove(state.GoId);
        _lives.Remove(state.GoId);     // 账本随生命周期释放（有界；life 只是 session 凭据）
    }

    private static ActorState Find(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return null;
        int goId = SafeGoId(archer);
        if (goId == 0 || !_actors.TryGetValue(goId, out ActorState state)) return null;
        // 用 native 指针核对（interop 包装实例不保证同一引用），指针为 0 时按不同对象处理。
        IntPtr pointer = SafePointer(archer);
        if (pointer == IntPtr.Zero || pointer != state.Pointer) return null;
        // live 生命核对：池复用后旧 life 的包装引用一律不是英雄（绝不把身份串到新生命）。
        if (state.Life == 0 || state.Life != CurrentLife(goId, pointer)) return null;
        return state;
    }

    /// <summary>该 Archer 当前 live 生命号（未登记/指针未知 → 0）。视觉侧用它核对「还是不是同一个生命」。</summary>
    internal static int CurrentActorLife(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return 0;
            int goId = SafeGoId(archer);
            if (goId == 0) return 0;
            IntPtr pointer = SafePointer(archer);
            if (pointer == IntPtr.Zero) return 0;
            return CurrentLife(goId, pointer);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int CurrentLife(int goId, IntPtr pointer)
    {
        if (_lives.TryGetValue(goId, out LifeEntry entry) && entry.Pointer == pointer) return entry.Life;
        return 0;
    }

    /// <summary>
    /// 首次观测即建账（开关在原生 OnEnable 之后才打开时，既有 Archer 也必须有 life，
    /// 否则 CurrentLife=0 → Find/Apply 全被拒，英雄永远不会出现）。账本有界（≤512），
    /// 满了返回 0（调用方跳过登记）；life 只是本 session 的运行期凭据，不落盘、不跨读档。
    /// </summary>
    private static int EnsureLife(int goId, IntPtr pointer)
    {
        if (goId == 0 || pointer == IntPtr.Zero) return 0;
        if (_lives.TryGetValue(goId, out LifeEntry entry) && entry.Pointer == pointer) return entry.Life;
        if (_lives.Count >= HeroArcherCore.MaxTrackedActors)
        {
            LogOnce(ref _capacityLogged, "life ledger full (" + HeroArcherCore.MaxTrackedActors + "), ignoring new archers");
            return 0;
        }
        entry = default;
        entry.Pointer = pointer;
        entry.Life = 1;
        _lives[goId] = entry;
        return 1;
    }

    /// <summary>
    /// 即时资格（唯一判据，IsHero / IsCombatEligible / PromoteHero / Observe / Tick 全用它现算）：
    /// 活跃且 enabled、非 harmless、side 已知、远程形态（两个 attackMode 同时 Ranged）、存活
    /// （Damageable 存在且 !isDead）、非 inert/grabbed、非 embarked、非玩家控制、非塔位岗哨、
    /// 当前 world/场景、非弩手、非北境近战随从。任何未知状态 → false。
    /// </summary>
    internal static bool IsRecruitable(Archer archer)
    {
        try
        {
            ActorState state = Find(archer);
            return Enabled && !HeroArcherVisuals.AtlasUnavailable
                && (state == null || !state.RangeBlocked) && ImmediateEligible(archer);
        }
        catch { return false; }
    }

    /// <summary>The shop commits its receipt only after actual effect setup succeeds.</summary>
    internal static bool TryActivatePurchased(Archer archer)
    {
        try
        {
            if (!IsRecruitable(archer) || !HeroRecruitment.IsPurchased(archer)) return false;
            Observe(archer);
            Tick();
            if (IsHero(archer) && HeroArcherVisuals.HasVisual(archer)) return true;
        }
        catch { }
        try
        {
            ActorState state = Find(archer);
            if (state != null) RetireHero(state, "purchase-setup-failed");
        }
        catch { }
        return false;
    }

    private static bool ImmediateEligible(Archer archer)
    {
        if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
        if (MusketeerIdentity.IsUnit(archer)) return false;
        if (!archer.enabled || archer.harmless) return false;
        if (SideKey(archer) == 0) return false;
        if (archer._attackMode != Archer.AttackMode.Ranged
            || archer._desiredAttackMode != Archer.AttackMode.Ranged) return false;
        Character character = archer._character;
        if (character == null || character.inert || character.grabbed) return false;
        Damageable damageable = archer._damageable;
        if (damageable == null || damageable.isDead) return false;
        Embarkee embarkee = archer._embarkee;
        if (embarkee != null && embarkee.IsEmbarked) return false;
        if (archer.ShouldPlayerControl()) return false;
        if (archer.inGuardSlot || archer._guardSlot != null) return false;   // 塔位/岗哨：射程由原生塔位路径管
        if (!OptionalQoLScope.IsCurrent(archer)) return false;
        if (PatchRoles_Crossbowman.IsCrossbowman(archer)) return false;
        return !PatchRoles_NorseSquad.IsNorseArcherInstance(archer);
    }

    private static bool Alive(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return false;
        Damageable damageable = archer._damageable;
        return damageable != null && !damageable.isDead;
    }

    /// <summary>本次射击目标：Enemies 层敌方 Damageable；野生动物/友好巨魔排除。</summary>
    private static bool CombatTarget(GameObject target)
    {
        if (target == null || target.transform == null || !target.activeInHierarchy) return false;
        if (!OptionalQoLScope.IsCurrent(target.transform)) return false;
        if (target.layer != EnemiesLayerIndex()) return false;
        if (target.CompareTag(WildlifeTagName)) return false;
        if (target.GetComponentInParent<FriendlyTroll>() != null) return false;
        Damageable damageable = target.GetComponent<Damageable>();
        if (damageable == null || !damageable.enabled || damageable.isDead) return false;
        return damageable.IsDamagedBy(DamageSource.Arrow);
    }

    private const string WildlifeTagName = "Wildlife";
    private const string EnemiesLayerName = "Enemies";
    private static int _enemiesLayer = -2;

    private static int EnemiesLayerIndex()
    {
        if (_enemiesLayer == -2)
        {
            string name = null;
            try { name = Layers.Enemies; } catch (Exception) { }
            _enemiesLayer = LayerMask.NameToLayer(string.IsNullOrEmpty(name) ? EnemiesLayerName : name);
        }
        return _enemiesLayer;
    }

    private static int SideKey(Archer archer)
    {
        try
        {
            // 购买名额的归属不随原版临时重新分配 side 改变。
            int purchasedSide = HeroRecruitment.SeatSide(archer);
            return purchasedSide != 0 ? purchasedSide : HeroArcherCore.SideKey((float)archer.side);
        }
        catch (Exception) { return 0; }
    }

    private static int SafeGoId(Archer archer)
    {
        try { return archer.gameObject.GetInstanceID(); }
        catch (Exception) { return 0; }
    }

    private static IntPtr SafePointer(Archer archer)
    {
        try { return archer.Pointer; }
        catch (Exception) { return IntPtr.Zero; }
    }

    private static long CurrentWorldKey()
    {
        try
        {
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            Transform layer = world != null ? world.gameLayer : null;
            return layer != null ? layer.Pointer.ToInt64() : 0L;
        }
        catch (Exception) { return 0L; }
    }

    private static float Now()
    {
        try { return Time.time; }
        catch (Exception) { return 0f; }
    }

    private static void LogOnce(ref bool flag, string message)
    {
        if (flag) return;
        flag = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroArcher] " + message); }
        catch (Exception) { }
    }

}

#endif
