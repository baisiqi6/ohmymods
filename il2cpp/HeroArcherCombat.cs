using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄弓箭手战斗覆盖层（combat slice，默认关闭）。
///
/// 职责：把用户锁定的四条英雄规则接到**既有**路径上，并作为 <c>HeroArcherRuntime</c> 契约的唯一调用点，
/// 本文件自己不做身份判定、不做手感/动画、不写任何原生字段、不生成或删除箭、不新增 Harmony 钩子。
///
/// 英雄规则（与用户确认一致）：
/// 1) 射速：普通开关关闭且非英雄 = 1、英雄 = 1.5；普通开关打开 = clamp(原倍率)、英雄 = max(1.5, 原倍率)。
///    **取 max 而非相乘**，所以普通 2 倍不会让英雄变慢，也不会出现 3 倍。
/// 2) 散射：英雄在「本次确实射击敌人」时固定 3 箭（含原生主箭）；打猎、无敌人目标、非英雄一律不额外发箭。
///    hero 分支优先，绝不与中世纪散射叠加出第二份额外箭。
/// 3) 火焰命中：半径 0.25、每个邻近敌人额外 1 点，直接目标不重复、同次齐射去重 —— 全部由
///    <see cref="PatchArcher_GreekImpact"/> 的 hero 来源执行（本文件只提供来源门与常量别名）。
/// 4) 权限：只有主机/离线发额外箭与伤害；客户端不发（沿既有 HasWorldAuth / HasClientCaughtUp 门）。
///
/// 与 runtime 的边界（契约由 operator 锁定，本 slice 不实现、不 stub 生产实现）：
/// <c>HeroArcherRuntime.Enabled / IsHero(Archer) / IsCombatEligible(Archer) / Observe(Archer) /
/// OnEnable(Archer) / Tick() / OnShot(Archer) / Clear()</c>。
/// 本文件只做三件事：查开关与身份（Enabled / IsHero / IsCombatEligible）、按既有钩子转交事件
/// （Observe ← PatchArcher_Options 的 Archer.Update prefix、OnEnable ← Archer.OnEnable prefix、
/// OnShot ← FireArrowInternal postfix），以及把异常挡在原生调用链之外。
/// <c>Tick()</c> 与 <c>Clear()</c> 由 operator 在既有 ModPanel.Update 直接接 runtime，不从这里转发（避免双调用）。
///
/// 失败语义：任何 runtime 异常都视为「不是英雄」（fail-closed），绝不外抛进原生协程/射击链；
/// runtime 未接入（类型缺失）时本文件不参与编译，接入后所有入口零分配、每帧有界。
/// </summary>
internal static class HeroArcherCombat
{
    /// <summary>英雄固定射速倍率（普通开关打开时与本值取 max，不相乘）。</summary>
    internal const float HeroRateMultiplier = 1.5f;
    /// <summary>英雄每发总箭数（含原生主箭）。</summary>
    internal const int HeroVolleyCount = 3;
    /// <summary>英雄箭命中爆发半径：与既有希腊火矢同一条 AoE 路径、同一个常量（不各自维护两个值）。</summary>
    internal const float HeroBurstRadius = PatchArcher_GreekImpact.Radius;
    /// <summary>英雄箭每个邻近敌人的伤害：同上，与希腊火矢共用同一常量。</summary>
    internal const int HeroBurstDamage = PatchArcher_GreekImpact.BurstDamage;

    private const float RateMin = 1f;
    private const float RateMax = 2f;

    private static readonly HashSet<string> Logged = new HashSet<string>();

    private static void Once(string key, string message)
    {
        try
        {
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeroArcher] " + message);
        }
        catch (Exception) { }
    }

    // ============================================================
    // 一、纯决策（无 Unity/无 runtime 依赖；测试直接覆盖）
    // ============================================================

    /// <summary>
    /// 每个 actor 的射速倍率。configured 是普通开关的原倍率（会被 clamp 到 1..2，非法值按 1 处理）；
    /// hero=true 时取 max(1.5, 普通值)，绝不与普通倍率相乘。
    /// </summary>
    internal static float EffectiveMultiplier(float configured, bool rateSwitchOn, bool hero)
    {
        float normal = rateSwitchOn ? ClampConfigured(configured) : RateMin;
        if (!hero) return normal;
        return Mathf.Max(HeroRateMultiplier, normal);
    }

    /// <summary>
    /// 本次射击的额外箭数。hero 且确认真实敌人目标 → 固定 2 支（合计 3 箭，含主箭）；
    /// 否则退回既有中世纪规则：散射开关打开时按配置总箭数减主箭，关闭时为 0。
    /// </summary>
    internal static int WantedExtras(bool heroCombat, bool scatterOn, int volleyCount)
    {
        if (heroCombat) return HeroVolleyCount - 1;
        if (!scatterOn) return 0;
        int total = volleyCount;
        if (total < 1) total = 1;
        if (total > HeroVolleyCount) total = HeroVolleyCount;
        return total - 1;
    }

    /// <summary>
    /// 该来源是否允许「非 fire 箭」绑定 lease：只有英雄覆盖层允许（英雄普通箭同样触发 0.25 爆发），
    /// 希腊路径仍然只认真 fire 箭。
    /// </summary>
    internal static bool AllowsNormalArrow(bool heroOrigin)
    {
        return heroOrigin;
    }

    // 注：「直接伤害替代」不再有 origin-only 开关函数：能否替代由 PatchArcher_GreekImpact 用
    // 「真 fire 箭 + 该来源 lease 且来源开关开启」判定（英雄 fire 箭同样取消原生灼烧）。

    private static float ClampConfigured(float value)
    {
        if (!float.IsFinite(value)) return RateMin;
        if (value < RateMin) return RateMin;
        if (value > RateMax) return RateMax;
        return value;
    }

    // ============================================================
    // 二、开关与身份（runtime 契约；任何异常 fail-closed）
    // ============================================================

    /// <summary>runtime 报告的开关（契约 Enabled；未接入或异常一律 false）。</summary>
    internal static bool RuntimeEnabled
    {
        get
        {
            try { return HeroArcherRuntime.Enabled; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>英雄覆盖层是否可能生效：runtime 开关 + root 世界范围；不看具体 actor（便宜门）。</summary>
    internal static bool HeroPossible
    {
        get
        {
            try { return RuntimeEnabled && ArcherOptionsScope.IsActive; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>该 actor 当前是否是英雄（runtime 负责存活/当前选中/角色状态等判定）。</summary>
    internal static bool IsHeroActor(Archer archer)
    {
        try
        {
            if (!HeroPossible) return false;
            if (archer == null || archer.gameObject == null) return false;
            return HeroArcherRuntime.IsHero(archer);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 该 actor 的**本次射击**是否属于英雄战斗（再核 runtime 的真实敌人目标判定：
    /// 排除打猎野生动物、友军巨魔、非当前世界、弩手/北境近战、embarked/inert/grabbed 等）。
    /// 只有它为真时才加箭与爆发；打猎保持主箭。
    /// </summary>
    internal static bool IsHeroCombatEligible(Archer archer)
    {
        try
        {
            if (!IsHeroActor(archer)) return false;
            return HeroArcherRuntime.IsCombatEligible(archer);
        }
        catch (Exception) { return false; }
    }

    // ============================================================
    // 三、事件转交（既有钩子；异常隔离、零分配）
    // ============================================================

    /// <summary>
    /// Archer.Update prefix：把 actor 交给 runtime 观察（它据此维护每侧 1 名的选择与撤销）。
    /// 关闭时不调用 runtime，也不做任何扫描。
    /// </summary>
    internal static void Observe(Archer archer)
    {
        try
        {
            if (!HeroPossible) return;
            if (archer == null || archer.gameObject == null) return;
            HeroArcherRuntime.Observe(archer);
        }
        catch (Exception e) { Once("observe", "observe failed; hero layer stays off for this frame: " + e); }
    }

    /// <summary>
    /// Archer.OnEnable prefix（池复用/新生命）：**无条件**转交 runtime，即使开关当前是关闭的 ——
    /// 关闭期间发生的池复用也必须让 runtime 撤销旧生命身份，否则"关掉→再打开"之间会漏掉一次清理。
    /// 不做任何门槛判定、不写任何字段。
    /// </summary>
    internal static void OnEnable(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            HeroArcherRuntime.OnEnable(archer);
        }
        catch (Exception e) { Once("enable", "on-enable failed; hero state stays revoked for this actor: " + e); }
    }

    /// <summary>
    /// FireArrowInternal postfix：原生命令每发恰好通知一次（英雄的视觉 release 事件）。
    /// 这里只解析射手组件，不做资格判定（runtime 自己决定该 actor 是否英雄）。
    /// </summary>
    internal static void NotifyShot(GameObject source)
    {
        try
        {
            if (!HeroPossible) return;
            if (source == null || source.activeInHierarchy == false) return;
            if (!source.TryGetComponent<Archer>(out Archer archer)) return;
            if (archer == null || archer.gameObject == null) return;
            HeroArcherRuntime.OnShot(archer);
        }
        catch (Exception e) { Once("shot", "on-shot failed; hero visuals keep their previous frame: " + e); }
    }

    // ============================================================
    // 测试钩子（仅 internal；只清本地日志键，绝不触碰 runtime 状态）
    // ============================================================

    internal static void ResetLogForTests() => Logged.Clear();
}
