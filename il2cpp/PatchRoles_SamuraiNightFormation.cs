using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 武士夜间贴墙紧凑列队（用户需求 2026-09-24：「夜里守家就是一排紧凑的武士」）。
///
/// 原生夜间守位（Knight.GetTargetPos，Knight.cs:652-657）=
///   guard.Value − Key × (_distanceFromWall × rank)
/// 即 rank 越大离墙越深、彼此间隔 = _distanceFromWall（实机 1.0/骑士）。本模块只对
/// 武士（style 2）的「夜间守位下发」改用紧凑序列位：
///
///   slot(i) = anchor.Value − Key × (0.7f + 0.5f × i)     （P1-A 绑定符号）
///
///   i    = 该守位侧（GetGuardPosition 返回对的 Key；override/horn 会换侧）上武士按
///          rank 升序、instanceID tie-break 的序号（0 起）。rank 稀疏不影响紧凑。
///   0.7  首格不压墙线；0.5 间距常量（≈随从小队观感与武士体宽折中，可后调）。
///
/// 两条挂接（brief v3）：
/// 1) 重定向（P1-C）：PatchWorld_DefenseSpacing 的 Mover.SetGoal(float,float) 前缀在
///    SamuraiRetreatSpeed.Adjust 之后调用 <see cref="TryTakeRedirect"/>；命中则由该前缀
///    在 _inSetGoalRedirect 守卫内、以 Adjust 后的速度写紧凑位（不新增 SetGoal patch 类）。
/// 2) 翻假（P1-B/P1-E）：Knight.ShouldGoToWall postfix——武士已在自身紧凑位 ±0.25 内时
///    把原生 true 改成 false。否则原生 at-post 检查读的是原生目标，永远判「不在岗」，
///    Stand↔GoToWall 在守位窗口内反复重进（~3.3s 周期、重复动画同步）。蛇近分支
///    （Knight.cs:521-525）是紧急防御，重查为真时绝不翻假；翻假同时复刻 GoToWall 尾部
///    到岗朝外 SetDirection((int)side)（Knight.cs:627-630）。
///
/// 边界（用户/审查裁定）：只重排武士自身，普通骑士保持原生 rank 位（接受推挤/叠影）；
/// 不写 knight.rank / _distanceFromWall（RankKnights 会整体重排且值随存档持久化）；
/// 白天零变化（夜门）；客户端（无 world auth）看原生分散站位=既有可接受偏差。
///
/// 槽位表 instanceID→(owner,slot,side) 由两条路径共享，≤3s TTL（UnitScanCache 约定，
/// 逐项过滤 null/activeInHierarchy）；重建路径有分配、查询路径零逐帧分配。每夜活动日志
/// ≤4 行：翻假事件=FSM 委托链 + postfix detour 的活性证明。
/// </summary>
internal static class PatchRoles_SamuraiNightFormation
{
    internal const int SamuraiStyleIndex = 2;

    private const float FirstSlotOffset = 0.7f;      // 首格不压墙线
    private const float SlotSpacing = 0.5f;          // 槽间距（P1-1）
    private const float AtSlotTolerance = 0.25f;     // 翻假窗口 = 半间距（P1-E①）
    private const float GoalMatchEpsilon = 1e-3f;    // 原生守位目标比对
    private const float SlotCacheTtl = 3f;           // 对齐 UnitScanCache 3s 约定
    private const float SlotRebuildThrottle = 0.25f; // miss 补建节流
    private const int NightLogBudget = 4;            // 每夜 ≤4 行（P2-8 活性证明）

    private struct SlotRecord
    {
        public IntPtr Owner; // 池对象 instanceID 复用防御
        public float X;
        public Side Side;
    }

    private struct Candidate
    {
        public Knight Unit;
        public Side GuardSide;
        public float Anchor;
        public int Rank;
        public int Id;
    }

    private static readonly Dictionary<int, SlotRecord> Slots = new();
    private static readonly List<Candidate> Candidates = new();
    private static readonly Comparison<Candidate> CandidateOrder = static (a, b) =>
    {
        if (a.GuardSide != b.GuardSide) return (int)a.GuardSide < (int)b.GuardSide ? -1 : 1;
        if (a.Rank != b.Rank) return a.Rank < b.Rank ? -1 : 1;
        if (a.Id != b.Id) return a.Id < b.Id ? -1 : 1;
        return 0;
    };

    private static float _slotsBuiltAt = float.NegativeInfinity;
    private static IntPtr _slotsKingdom;
    private static int _nightLogs;

    // 编译期链接锚（故意不被调用）：直接引用 2.4 interop 的 Knight.ShouldGoToWall。interop
    // 对私有原生方法同样生成可调用成员（SamuraiRetreatSpeed 直呼 ShouldPlayerControl /
    // GetFormation 先例）；属性 patch 只按名字解析、构建期不验证目标存在，这一引用让
    // 「2.4 缺该方法」在 0W0E 关口失败，而不是 PatchAll 在启动时炸掉整个插件。
    private static bool LinkageAnchor(Knight knight) => knight.ShouldGoToWall();

    // ---- SetGoal(float,float) 前缀入口（PatchWorld_DefenseSpacing 调用）----

    /// <summary>
    /// 重定向门：命中返回 true 并给出该武士的紧凑槽位 x；调用方在 _inSetGoalRedirect 守卫
    /// 内以 Adjust 后的速度重写目标。只认「原生守位目标」这一种下发（重算原生 GetTargetPos
    /// 式，ε≤1e-3），其余一律放行（白天/非武士/非守位态/租约目标/权限关）。
    /// </summary>
    internal static bool TryTakeRedirect(Knight knight, Mover mover, float goal, out float slotX)
    {
        slotX = 0f;
        try
        {
            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return false;
            if (kingdom.isDaytime)
            {
                _nightLogs = 0; // 白天零变化；顺带重置每夜日志预算
                return false;
            }
            if (knight == null || knight.gameObject == null || mover == null) return false;
            if (!knight.gameObject.activeInHierarchy) return false;
            // 状态门：只有原生 GoToWall 状态的下发才是守位目标（SamuraiRetreatSpeed 先例）。
            if (knight._fsm == null || knight._fsm.Current != Knight.State.GoToWall) return false;
            // 身份门：mover 必须是该骑士当前的 mover（SamuraiRetreatSpeed:14 先例）。
            if (knight._mover == null || knight._mover.Pointer != mover.Pointer) return false;
            if (knight.side != Side.Left && knight.side != Side.Right) return false;
            // 权限门：与 PowerDash :162-163 / SamuraiRetreatSpeed :12,17 完全一致。
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return false;
            if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style) ||
                style != SamuraiStyleIndex) return false;
            // 租约门（P1-D）：PowerDash 的 dash/返队/走位租约拥有目标时绝不改写
            //（租约用值判目标归属，被改写即被误判失败）。
            if (PatchRoles_SamuraiPowerDash.HasActiveMotion(knight)) return false;

            var guard = kingdom.GetGuardPosition(knight.side);
            float native = guard.Value - (float)guard.Key *
                (knight._distanceFromWall * (float)knight.rank);
            if (!float.IsFinite(goal) || !float.IsFinite(native) ||
                Math.Abs(goal - native) > GoalMatchEpsilon) return false;

            if (!TryGetSlot(knight, kingdom, out SlotRecord slot)) return false;
            slotX = slot.X;
            LogRedirect(knight, slot, goal, native);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiNightFormation/redirect] " + e);
            return false;
        }
    }

    // ---- Knight.ShouldGoToWall postfix 主体 ----

    /// <summary>
    /// 武士已在自己的紧凑位 ±0.25 内时把原生 true 翻成 false（防 Stand↔GoToWall 振荡）。
    /// 只在原生 true 上翻假、绝不 false→true；蛇近（紧急防御）、白天、非武士、权限关
    /// 一律保持原生值。
    /// </summary>
    internal static void AfterShouldGoToWall(Knight knight, ref bool result)
    {
        if (!result) return;
        try
        {
            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null) return;
            if (kingdom.isDaytime)
            {
                _nightLogs = 0;
                return;
            }
            if (knight == null || knight.gameObject == null) return;
            if (!knight.gameObject.activeInHierarchy) return;
            if (knight.side != Side.Left && knight.side != Side.Right) return;
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;
            if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style) ||
                style != SamuraiStyleIndex) return;
            Mover mover = knight._mover;
            if (mover == null) return;
            if (!TryGetSlot(knight, kingdom, out SlotRecord slot)) return;
            float x = knight.transform.position.x;
            if (!float.IsFinite(x) || Mathf.Abs(x - slot.X) > AtSlotTolerance) return;
            // P1-E②：蛇近是紧急防御（Knight.cs:521-525 原样重查），不得翻假钉死武士。
            if (SerpentNear(kingdom, x)) return;
            result = false;
            // P1-E③：复刻 GoToWall 尾部到岗朝外（Knight.cs:627-630；该直调点若被内联，
            // 原生尾部不会执行，这里兜底保证整夜面向镇外）。
            mover.SetDirection((int)knight.side);
            LogFlip(knight, slot, x);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiNightFormation/at-post] " + e);
        }
    }

    /// <summary>Knight.ShouldGoToWall 蛇分支原样复刻（Knight.cs:521-525）。</summary>
    private static bool SerpentNear(Kingdom kingdom, float x)
    {
        WorldEatingSerpent serpent = kingdom.Serpent;
        if (serpent == null) return false;
        return Mathf.Abs(serpent.Position - x) < serpent.AttackDistance;
    }

    // ---- 共享槽位缓存 ----

    /// <summary>
    /// 槽位查找：换世界/超过 3s TTL 重建；miss 时按 0.25s 节流补建一次（新转职武士
    /// 最迟下一轮进表）。查不到=按原生行为放行（fail-open 到原生）。
    /// </summary>
    private static bool TryGetSlot(Knight knight, Kingdom kingdom, out SlotRecord slot)
    {
        slot = default;
        if (kingdom == null || knight == null || knight.gameObject == null) return false;
        float now = Time.time;
        if (kingdom.Pointer != _slotsKingdom || now - _slotsBuiltAt > SlotCacheTtl)
            RebuildSlots(kingdom, now);
        if (Lookup(knight, out slot)) return true;
        if (now - _slotsBuiltAt < SlotRebuildThrottle) return false;
        RebuildSlots(kingdom, now);
        return Lookup(knight, out slot);
    }

    private static bool Lookup(Knight knight, out SlotRecord slot)
    {
        slot = default;
        if (!Slots.TryGetValue(knight.gameObject.GetInstanceID(), out SlotRecord record)) return false;
        if (record.Owner != knight.Pointer) return false; // instanceID 复用防御
        slot = record;
        return true;
    }

    /// <summary>
    /// 重建槽位表：UnitScanCache 的 3s 骑士扫描（逐项过滤 null/activeInHierarchy）里筛
    /// 已解析的武士（style 2、存活、side 合法），按「守位侧 → rank → instanceID」升序
    /// 排序后逐侧发槽 slot(i) = anchor − Key × (0.7 + 0.5×i)。
    /// </summary>
    private static void RebuildSlots(Kingdom kingdom, float now)
    {
        if (kingdom.Pointer != _slotsKingdom)
        {
            _slotsKingdom = kingdom.Pointer;
            _nightLogs = 0; // 新世界=新夜，预算重置
        }
        _slotsBuiltAt = now;
        Slots.Clear();
        Candidates.Clear();

        Knight[] knights = UnitScanCache.GetKnights(SlotCacheTtl);
        for (int i = 0; i < knights.Length; i++)
        {
            Knight k = knights[i];
            if (k == null || k.gameObject == null || !k.gameObject.activeInHierarchy) continue;
            if (k.side != Side.Left && k.side != Side.Right) continue;
            if (k._fsm == null || k._mover == null) continue;
            if (k._damageable != null && k._damageable.isDead) continue; // 死亡退出队列（原生同序重排）
            if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) ||
                style != SamuraiStyleIndex) continue;
            Side guardSide;
            float anchor;
            try
            {
                var guard = kingdom.GetGuardPosition(k.side);
                guardSide = guard.Key;
                anchor = guard.Value;
            }
            catch
            {
                continue; // 单个坏对象不能让整表失效
            }
            if (guardSide != Side.Left && guardSide != Side.Right) continue;
            if (!float.IsFinite(anchor)) continue;
            Candidates.Add(new Candidate
            {
                Unit = k,
                GuardSide = guardSide,
                Anchor = anchor,
                Rank = k.rank,
                Id = k.gameObject.GetInstanceID(),
            });
        }

        Candidates.Sort(CandidateOrder);

        bool grouping = false;
        Side groupSide = Side.Left;
        float groupAnchor = 0f;
        int index = 0;
        for (int i = 0; i < Candidates.Count; i++)
        {
            Candidate c = Candidates[i];
            if (!grouping || c.GuardSide != groupSide || c.Anchor != groupAnchor)
            {
                grouping = true;
                groupSide = c.GuardSide;
                groupAnchor = c.Anchor;
                index = 0;
            }
            float slotX = groupAnchor - (float)groupSide * (FirstSlotOffset + SlotSpacing * index);
            Slots[c.Id] = new SlotRecord { Owner = c.Unit.Pointer, X = slotX, Side = groupSide };
            index++;
        }
    }

    // ---- 每夜有界日志（活性证明）----

    private static void LogRedirect(Knight knight, SlotRecord slot, float goal, float native)
    {
        if (_nightLogs >= NightLogBudget) return;
        _nightLogs++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[SamuraiNightFormation/redirect] rank=" + knight.rank +
            " side=" + (int)knight.side +
            " x=" + knight.transform.position.x.ToString("F2") +
            " native=" + native.ToString("F2") + " -> slot=" + slot.X.ToString("F2") +
            " (goal=" + goal.ToString("F2") + ")");
    }

    private static void LogFlip(Knight knight, SlotRecord slot, float x)
    {
        if (_nightLogs >= NightLogBudget) return;
        _nightLogs++;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
            "[SamuraiNightFormation/at-post] rank=" + knight.rank +
            " side=" + (int)slot.Side + " x=" + x.ToString("F2") +
            " slot=" + slot.X.ToString("F2") + " flip=true->false");
    }
}

/// <summary>
/// Knight.ShouldGoToWall postfix：私有原生方法经 FSM Condition 委托间接调用（不惧逐调用点
/// 内联），2.4 无既有 patch。翻假事件由每夜有界日志承担链路活性证明。
/// </summary>
[HarmonyPatch(typeof(Knight), "ShouldGoToWall")]
internal static class Knight_ShouldGoToWall_SamuraiNightFormation_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance, ref bool __result) =>
        PatchRoles_SamuraiNightFormation.AfterShouldGoToWall(__instance, ref __result);
}
