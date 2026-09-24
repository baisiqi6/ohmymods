using System;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 弩手守家高抛修复（方案E，2026-09-21）：弩矢「对墙穿墙 + 低弹道强制」两件套，
/// 让墙后弩手真正打平直弹道。零新增逐帧扫描、零新增 Harmony 作用域外钩子、零新增配置项。
///
/// 背景：弩手在墙后射击时，原生 <c>ArrowAttack.BestShotInternal</c>（ArrowAttack.cs:127-139）
/// 的低弹道解三条件 <c>flag &amp;&amp; !forceHighShot &amp;&amp; !ParabolaCast(Obstacles层)</c>
/// 被自家墙挡（ParabolaCast 从出膛点出发命中墙）→ 原生被迫选高抛解，弩矢画大弧线。
/// 旧对策（出膛点前移 2.5 步避挡）已废弃（<see cref="PatchRoles_Crossbowman"/> 的
/// BoltOriginOffset 回调 0.6）。
///
/// 本文件恰好两件事：
/// 1) **弩矢穿墙**——注入组件（照抄 CrossbowBoltScaleLifecycle 模式）：prefab 构建期
///    AddComponent（EnsureAssets 克隆成功后、**不进** ApplyBoltSprite 换皮成功分支——
///    降级无 sprite 模式同样必须生效）。池 Spawn 激活 → OnEnable →
///    <see cref="HeroArcherWallPierce.Apply(Arrow)"/>（墙快照 + 逐对
///    Physics2D.IgnoreCollision true 的全部语义复用，仅本 SO 池对象命中）；池回收 →
///    OnDisable → <see cref="HeroArcherWallPierce.Restore(Arrow)"/>（比英雄的
///    Arrow.OnEnable 前缀归还更早——回收当帧即归还，不等下一次 OnEnable）。
///    效果域=KEM 弩矢池全部发射者（弩手+死地随从，同一克隆 SO，预期内）。
/// 2) ~~低弹道强制~~——**2026-09-24 用户裁定取消**：原生 BestShotInternal 本就有
///    "低解被障碍挡→自动高抛"的完整选择逻辑；强制低解与穿墙失配叠加会把失配放大成
///    平射拍自家墙（夜间守家实机反馈）。保留穿墙：墙在=原生自然高抛越过；墙破/残余
///    碰撞由穿墙兜底。Harmony prefix 已删，弹道选择完全回归原生。
///    （2.4 interop 方法面已核：actual-interop 摘录 BestShotInternal(Vector2,Vector2,
///    float,bool,bool) PARAMS targetPos,arrowPosition,gravity,forceHighShot,isBoostedShot），
///    门=<c>__instance.Pointer == PatchRoles_Crossbowman.ClonedAttackSoPointer</c>（与克隆
///    SO 同源的指针——ApplySquadCrossbowPackage 判重同款）。命中 → skip 原方法，用
///    <c>Util.ComputeTrajectoryAngle</c>（Util.cs:429，无解时 low==high 天然等价原生
///    回退）复算低解并返回 <c>ClampMagnitude(low*num, num)</c>——即跳过 ParabolaCast
///    墙挡判定；不命中（英雄私有 SO/全部原生 SO）→ 原样执行原生方法。弩箭塔
///    （原生 Bolt/BoltData）完全不碰：不挂组件、SO 门不命中。
///
/// 运行时依赖（回执注明）：HeroArcherWallPierce.Apply/Restore 是 internal 同程序集直调
/// （Mac 侧若改签名在编译期暴露，不会静默断链）；Apply/Restore 内部日志沿用
/// [HeroArcherWallPierce] 前缀（观感误标，已知并接受）。
/// 已知边界：非墙 Obstacles 结构（塔/投石机）不再被高抛越过、弩矢可能被自家结构挡下
/// （方案E固有代价，实机观察）；syncReceipt 命中已 active 对象不触发 OnEnable 的窄洞
/// （英雄同款）；死地随从包同样生效（预期内）。异常一律隔离，绝不外抛进原生调用链；
/// 任何失败 fail-closed 到原生行为（保持原生碰撞/原生弹道解）。
/// </summary>
public sealed class CrossbowmanBoltWallPierce : MonoBehaviour
{
    // ---- 一次性日志去重（每路径每进程一条，防刷屏） ----
    private static bool _loggedAttachFailure;
    private static bool _loggedEnableFailure;
    private static bool _loggedDisableFailure;

    public CrossbowmanBoltWallPierce(IntPtr pointer) : base(pointer) { }

    /// <summary>
    /// 池 Spawn 激活：给这支弩矢挂上「无视全部活动墙碰撞体」。箭组件由 GetComponent
    /// 现取（池对象同一 GO 组件稳定；读不到=Apply 内部 fail-closed，保持原生碰撞）。
    /// </summary>
    private void OnEnable()
    {
        try { HeroArcherWallPierce.Apply(GetComponent<Arrow>()); }
        catch (Exception e) { LifecycleFailure(ref _loggedEnableFailure, "enable", e); }
    }

    /// <summary>
    /// 池回收（SetActive(false) 当帧）：归还墙碰撞（false）。Restore 从不重扫、只处理
    /// 可能已写过的旧对（宁多写一次 false 也不漏）；比英雄的 Arrow.OnEnable 前缀归还
    /// 更早。无快照时是 no-op。
    /// </summary>
    private void OnDisable()
    {
        try { HeroArcherWallPierce.Restore(GetComponent<Arrow>()); }
        catch (Exception e) { LifecycleFailure(ref _loggedDisableFailure, "disable", e); }
    }

    /// <summary>
    /// prefab 构建期挂件（PatchRoles_Crossbowman.EnsureAssets 克隆成功后调用）：
    /// 注册类型（幂等）→ 已挂即返回 → AddComponent。失败只记一次日志并放弃
    /// （弩矢保持原生墙碰撞，其余弩手功能不受影响）。
    /// </summary>
    internal static void EnsureOn(Arrow boltArrow)
    {
        try
        {
            if (boltArrow == null || boltArrow.gameObject == null) return;
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CrossbowmanBoltWallPierce)))
                ClassInjector.RegisterTypeInIl2Cpp<CrossbowmanBoltWallPierce>();
            if (boltArrow.gameObject.GetComponent<CrossbowmanBoltWallPierce>() != null) return;
            boltArrow.gameObject.AddComponent<CrossbowmanBoltWallPierce>();
        }
        catch (Exception e)
        {
            if (_loggedAttachFailure) return;
            _loggedAttachFailure = true;
            KingdomEnhancedPlugin.Instance?.LogSource?.LogError(
                "[CrossbowmanBoltPierce] attach failed; bolts keep native wall collisions: " + e);
        }
    }

    private static void LifecycleFailure(ref bool logged, string step, Exception e)
    {
        if (logged) return;
        logged = true;
        KingdomEnhancedPlugin.Instance?.LogSource?.LogError(
            "[CrossbowmanBoltPierce] lifecycle " + step + " failed; bolt keeps native wall collisions: " + e);
    }
}
