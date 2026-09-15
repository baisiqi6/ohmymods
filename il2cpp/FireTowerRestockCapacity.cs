using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// AutoRestock role7（希腊火塔火罐）容量门控：购买前直接核对该塔此刻真实剩余的安全
/// 弹药槽，提供明确的满仓等待原因和单槽对象安全检查。原生 CanPay 与支付路径仍保留。
///
/// 口径（纯只读；无缓存、无扫描、无原生写、不新增 hook）：
/// - 目标必须落在同 GO 的 <see cref="FireTower"/> 上，且
///   <c>component._owner.Pointer</c> 精确等于该塔指针（<c>Init(owner)</c> 写入的唯一身份；
///   non-null owner、<c>_payableComponent</c> 反指都不算）；塔与组件必须当前 active/enabled。
/// - 有效容量 = <c>min(_maxFireJars, _fakeFireJars.Length)</c>，两者都必须 &gt; 0。
///   2.1/2.4 原生 <c>OnPayHandler</c> 直接执行
///   计数 ++ 后对原索引槽位调用 SetActive(true)，数组长度和槽对象均需验证；
///   数组尚未生成（null）时无法确认槽位，一律未就绪。
/// - 当前 <c>_fireJarsActiveNum</c> 必须 &gt;= 0 且 &lt; cap 才可买；== cap 是合法“已满”，
///   &lt; 0 或 &gt; cap 属非法数据，按未就绪 fail-closed。
/// 每次调用都重读字段：换数组、容量变化、塔复用立即生效，绝不缓存可能过期的容量。
/// 只有 role7 的购买门调用本类；其余角色不引用。
/// </summary>
internal static class FireTowerRestockCapacity
{
    /// <summary>合法已满：所有安全弹药槽都在用，等消耗后再补。</summary>
    internal const string ReasonFull = "弹药已满，等待消耗";

    /// <summary>fail-closed：owner/初始化/字段数据不可信，或塔当前不可用。</summary>
    internal const string ReasonNotReady = "弹药容量未就绪";

    /// <summary>
    /// 该目标此刻是否仍可安全购买一发（原生扣款与 OnPay 保持不变，本类只做门控）。
    /// 可买时 <paramref name="reason"/> = null；已满 = <see cref="ReasonFull"/>；
    /// 其余（owner 不符 / 未初始化 / 非法数据 / 禁用销毁 / 读取异常）= <see cref="ReasonNotReady"/>。
    /// </summary>
    internal static bool CanPurchase(Payable target, out string reason)
    {
        reason = ReasonNotReady;
        try
        {
            if (target == null) return false;
            GameObject go = target.gameObject;
            if (go == null || !go.activeInHierarchy) return false;

            PayableComponent component = target.TryCast<PayableComponent>();
            if (component == null || !component.enabled) return false;

            FireTower tower = go.GetComponent<FireTower>();
            if (tower == null || !tower.enabled || !tower.gameObject.activeInHierarchy) return false;

            IPayableComponentOwner owner = component._owner;
            if (owner == null || owner.Pointer != tower.Pointer) return false;

            int max = tower._maxFireJars;
            var jars = tower._fakeFireJars;
            if (max <= 0 || jars == null) return false;
            int length = jars.Length;
            if (length <= 0) return false;
            int capacity = length < max ? length : max;

            int current = tower._fireJarsActiveNum;
            if (current < 0 || current > capacity) return false;   // 非法数据，不是“已满”
            if (current == capacity) { reason = ReasonFull; return false; }

            if (jars[current] == null) return false; // allocated array may still contain unbuilt/destroyed slots
            reason = null;
            return true;
        }
        catch (Exception)
        {
            // 销毁/池化复用/字段读取异常：无法核实容量，fail-closed。
            reason = ReasonNotReady;
            return false;
        }
    }
}
