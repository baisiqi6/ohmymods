using System;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 目标生命 token：GO 身份（Pointer + InstanceID）+ 该 GO 上的 Damageable 组件身份 + 全局单调 life 序号。
/// 同一对象回池（GO 停用/启用）会拿到新 life，token 因此不同；同一 life 内 token 恒定。
/// 纯值类型、不持有任何 Unity 引用：跨 world / 禁用 / 销毁 / 池复用都不会留下旧引用。
/// </summary>
internal struct CombatTargetToken
{
    internal IntPtr GoPointer;
    internal int GoId;
    internal IntPtr Damageable;
    internal long Life;

    /// <summary>
    /// 完整身份比较。life 是判别式：不同 life 永不相等（新生命可在新 burst 中吃一次 AoE）。
    /// Life == 0 的降级 token 永不匹配任何 token（调用方不得把它登记进账本）。
    /// </summary>
    internal static bool Matches(in CombatTargetToken a, in CombatTargetToken b) =>
        a.Life != 0 && a.Life == b.Life && a.GoPointer == b.GoPointer && a.GoId == b.GoId && a.Damageable == b.Damageable;
}

/// <summary>
/// 目标生命 marker：按需挂在**被 AoE 实际检查过的** Damageable 所在 GO 上（池复用同一组件、同一实例字段）。
///
/// 注入面只暴露 `void OnEnable/OnDisable/OnDestroy` 三个 Unity 消息：无 Update / 协程 / 网络 RPC / 持久化 /
/// 全场扫描；非 Unity 辅助方法（<see cref="EnsureLife"/>、活性读取）标 <c>[HideFromIl2Cpp]</c>，
/// 不让注入器为 `long` / `bool?` 生成 invoker。
/// 不改任何 hideFlags（marker 保持默认 flags；`DontSave` 位会触发额外的 OnDisable/OnEnable 语义），
/// 不碰目标 GO / 旧组件 flags / HP / 存档 —— 纯 runtime marker 不实现 Persistent/IBehaviour/IRPCable
/// 等存档接口，不进入自定义 Save 流程。
///
/// life 规则（池回收 vs 组件开关 vs 观察缺口）：
/// - 首次激活取一次全局单调序号：AddComponent 的同步 OnEnable 与读取初始化（<see cref="EnsureLife"/>）
///   谁先到谁取号，另一方幂等让位 —— 绝不因「首次 OnEnable + 读取初始化」双增 life。
/// - 只 disable 本组件（GO 仍激活）**不**结束 life，但会进入**观察缺口**：组件禁用期间 GO 的
///   停用/启用不会派发消息，此后的 life 无法证明仍对应同一次池生命。
///   缺口期间/重新启用后一律 fail-closed（<see cref="Observing"/> = false ⇒ 拒绝解析），
///   **不凭重新 enable 发新 token、也不沿用旧 token**；只有之后再确证一次完整 GO 停用
///   （OnDisable 且 GO 已不活跃）才恢复，并在随后的启用上取新序号。
/// - GO（或父链）停用、组件销毁才是真实生命结束；活性读取抛异常视为「未知」，
///   既不算确证停用、也不允许继续使用当前 life。
/// </summary>
public sealed class CombatTargetLifeMarker : MonoBehaviour
{
    public CombatTargetLifeMarker(IntPtr pointer) : base(pointer)
    {
    }

    /// <summary>本 life 的全局序号；0 = 尚未取号（刚添加未激活 / 已停用未再启用）。</summary>
    internal long Life;

    /// <summary>
    /// 观察链是否完整。false = 曾在本组件被单独禁用（或活性读取失败）后尚未确证过一次完整 GO 停用：
    /// 此时 <see cref="Life"/> 不可信，<see cref="CombatTargetLife.TryResolve"/> 必须拒绝。
    /// </summary>
    internal bool Observing = true;

    /// <summary>幂等取号：只在当前没有 life 时取新号；重复调用（Unity 消息与读取初始化竞争）不换号。</summary>
    [HideFromIl2Cpp]
    internal long EnsureLife()
    {
        if (Life == 0) Life = CombatTargetLife.TakeLife();
        return Life;
    }

    private void OnEnable()
    {
        // 观察缺口未修复（曾单独禁用/读取失败且尚未确证 GO 停用/启用）：
        // 既不沿用旧 life、也不伪造新 life（fail-closed，等一次确证的完整停用）。
        if (!Observing) return;
        EnsureLife();
    }

    private void OnDisable()
    {
        bool? active = GameObjectActive();
        if (active == false)
        {
            // 确证 GO / 父链停用（池回收）：生命结束，观察链恢复；下次启用取新序号。
            Life = 0;
            Observing = true;
        }
        else
        {
            // 单独 disable 本组件，或活性读取失败：都不允许再用/新取 life（组件仍保留 Life 便于测试观测）。
            Observing = false;
        }
    }

    private void OnDestroy()
    {
        Life = 0;
        Observing = false;
    }

    /// <summary>true/false = 活性；null = 读取失败（未知，绝不能当作「已确证停用」）。</summary>
    [HideFromIl2Cpp]
    private bool? GameObjectActive()
    {
        try
        {
            GameObject go = gameObject;
            if (go == null) return null;                 // 已销毁/不可读：同样是未知
            return go.activeInHierarchy;
        }
        catch { return null; }
    }
}

/// <summary>
/// 目标生命标识：给 AoE 去重账本提供「同一对象的新 life」判别证据。
///
/// 设计边界：
/// - 只做目标 GO 上的组件查询 + 幂等取号；不做全场扫描、不打 native getter detour、
///   不用时间/位置/HP 猜 life、不写原生字段。
/// - 注册（ClassInjector）必须先于一切类型接触点：<see cref="Initialize"/> 供 root 在 Plugin.Init 调用，
///   每进程只尝试一次注册（成功后零成本早退；失败即本进程 fail-closed，命中点只计数不刷注册错误日志）。
/// - 解析失败（注册 / AddComponent / 取号 / marker 仍禁用 / marker 处于观察缺口）返回 false：
///   调用方必须**显式放弃该目标**，绝不退回 Damageable 指针去重（那正是被修的「同对象回池误去重」），
///   也绝不用假 token 冒充。
/// </summary>
internal static class CombatTargetLife
{
    /// <summary>internal 供测试与实机核对；不逐次日志。</summary>
    internal static int StatResolved, StatMarkerCreated, StatDegraded;

    private const int ReportEvery = 256;
    private static long NextLife;
    private static bool MarkerRegistered;
    private static bool RegistrationAttempted;     // 每进程只尝试一次注册（失败 fail-closed，不逐目标重试）
    private static int DegradeReports;

    /// <summary>
    /// root 接入入口（Plugin.Init 调用一次即可）：显式注册 marker 类型并返回是否可用。
    /// 本进程只尝试一次注册（见 <see cref="EnsureRegistered"/>）；失败后本功能 fail-closed 到进程结束。
    /// </summary>
    internal static bool Initialize() => EnsureRegistered();

    /// <summary>
    /// 注册先于一切 marker 类型接触点（AddComponent/GetComponent 未注册时会抛异常）。
    /// 每进程只尝试一次：成功后零成本早退；失败则本进程 fail-closed（不再逐目标重复尝试/刷日志，
    /// 命中点仍用既有 Degrade 限频计数）。启动时 IL2CPP 已就绪，无需每个接触点重试，下一次启动再试。
    /// </summary>
    internal static bool EnsureRegistered()
    {
        if (MarkerRegistered) return true;
        if (RegistrationAttempted) return false;
        RegistrationAttempted = true;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(CombatTargetLifeMarker)))
            {
                ClassInjector.RegisterTypeInIl2Cpp(typeof(CombatTargetLifeMarker));
            }
            MarkerRegistered = true;
            return true;
        }
        catch (Exception e)
        {
            Report("register:" + e.GetType().Name);       // 一次性日志（本进程不再重试）
            return false;
        }
    }

    /// <summary>全局单调 life 序号（永不复用；仅 marker 与读取初始化取号用）。</summary>
    internal static long TakeLife() => ++NextLife;

    /// <summary>
    /// 解析目标当前 life token。失败返回 false 且绝不抛异常：调用方按「显式放弃本目标」处理
    /// （原生直伤路径与其它目标不受影响）。
    /// </summary>
    internal static bool TryResolve(Damageable target, out CombatTargetToken token)
    {
        token = default;
        try
        {
            if (target == null) return false;
            GameObject go = target.gameObject;
            if (go == null) return false;
            if (!EnsureRegistered()) { Degrade("unregistered"); return false; }
            CombatTargetLifeMarker marker = go.GetComponent<CombatTargetLifeMarker>();
            if (marker == null)
            {
                marker = go.AddComponent<CombatTargetLifeMarker>();
                if (marker == null) { Degrade("marker-null"); return false; }
                StatMarkerCreated++;
                // 不写 hideFlags：DontSave 位会让 Unity 把物件移出 Scene/physics 并触发 OnDisable/OnEnable，
                // 而我们的生命周期语义依赖真实 GO 停用；纯 runtime marker 不实现任何存档接口，无需该位。
            }
            if (!marker.enabled) { Degrade("marker-disabled"); return false; }   // 组件仍禁用：观察不可信
            if (!marker.Observing) { Degrade("marker-unobserved"); return false; } // 观察缺口：fail-closed
            long life = marker.EnsureLife();      // OnEnable 已取号则无操作（幂等）
            if (life == 0) { Degrade("no-life"); return false; }
            int goId = SafeId(go);
            if (goId == 0) { Degrade("go-id"); return false; }
            token = new CombatTargetToken
            {
                GoPointer = go.Pointer,
                GoId = goId,
                Damageable = target.Pointer,
                Life = life,
            };
            StatResolved++;
            return true;
        }
        catch (Exception e)
        {
            Degrade("resolve:" + e.GetType().Name);
            token = default;
            return false;
        }
    }

    private static int SafeId(GameObject go)
    {
        try { return go.GetInstanceID(); }
        catch { return 0; }
    }

    private static void Degrade(string key)
    {
        StatDegraded++;
        DegradeReports++;
        if (DegradeReports != 1 && (DegradeReports % ReportEvery) != 0) return;
        Report(key);
    }

    /// <summary>限量报告：日志自身异常绝不影响调用方。</summary>
    private static void Report(string key)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                "[CombatTargetLife] " + key + " count=" + DegradeReports);
        }
        catch { }
    }
}
