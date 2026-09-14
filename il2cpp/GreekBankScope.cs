using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 银行增强的唯一世界 scope：Banker hooks、共享金库、银行助手、税收小队与自动补货
/// 都从这里取“当前是否希腊”。判定只看当前世界（current BiomeHolder），绝不依据
/// 角色/对象的来源世界，也不因为对方是“移植进希腊的其他世界单位”而改变。
///
/// 三态语义：
/// - Active：ModEnabled 且 BiomeHolder 明确等于 GreeceBiomeIndex；
/// - Inactive：明确不是希腊（其他世界）或总开关关闭；
/// - Unknown：配置未初始化 / BiomeHolder 未就绪 / 读取异常 / 负数编号。
///   Unknown 与 Inactive 都不新增任何增强，但调用方必须区别对待：Unknown 暂缓并
///   保留已改参数的 restore receipt，Inactive 才归还本补丁写过的值。
///
/// 经济写入（账本、入账、扣款）在 scope 之外还要过 <see cref="IsAuthorityBanker"/>：
/// world auth + 当前 gameLayer/scene + 本体身份。真实读档时 kingdom.banker 可能为
/// null（fixedID903 银行家不会被 Castle 重新赋引用），此时当前层的 banker 仍须工作，
/// 因此只在 native 非 null 且指向他人时拒绝。纯本地希腊视觉只依赖 IsActive，
/// 不因客户端无主机权限而关闭。
/// </summary>
internal static class GreekBankScope
{
    internal enum Scope { Unknown, Inactive, Active }

    private static bool _loggedFailure;

    internal static Scope Current()
    {
        try
        {
            var enabled = ModConfig.Enabled;
            if (enabled == null) return Scope.Unknown;
            // 关闭优先于世界判定：关掉总开关必须能立刻归还本补丁写过的参数，
            // 不能因为此刻正在加载而把 receipt 留到下一个世界。
            if (!enabled.Value) return Scope.Inactive;
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null) return Scope.Unknown;
            int index = holder.BiomeIndex;
            if (index < 0) return Scope.Unknown;
            return index == BiomeHolder.GreeceBiomeIndex ? Scope.Active : Scope.Inactive;
        }
        catch (Exception e)
        {
            ReportFailure(e);
            return Scope.Unknown;
        }
    }

    /// <summary>当前世界是否明确为希腊且模组开启（纯本地增强与本地视觉的闸门）。</summary>
    internal static bool IsActive => Current() == Scope.Active;

    /// <summary>总开关状态（不含世界判定），供 owned 收尾（落盘/归还）使用。</summary>
    internal static bool IsModEnabled
    {
        get
        {
            try
            {
                var enabled = ModConfig.Enabled;
                return enabled != null && enabled.Value;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// 纯本地身份闸门：希腊 scope + 传入 banker 是当前 gameLayer/scene 里的活本体。
    /// 不要求主机权限——本地行为/视觉在客机上照常生效。
    /// </summary>
    internal static bool IsCurrentBanker(Banker banker)
    {
        try
        {
            if (banker == null || !IsActive) return false;
            GameObject owner = banker.gameObject;
            if (owner == null || !owner.activeInHierarchy) return false;
            if (!IsInCurrentLayer(banker)) return false;
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            if (kingdom == null) return false;
            Banker native = kingdom.banker;
            return native == null || native.Pointer == banker.Pointer;
        }
        catch (Exception e)
        {
            ReportFailure(e);
            return false;
        }
    }

    /// <summary>
    /// 经济写入闸门：<see cref="IsCurrentBanker"/> 之外还要 world auth。
    /// <paramref name="banker"/> 为 null、在异层/旧场景、非活动对象，或 kingdom.banker
    /// 指向别的实例时一律拒绝。
    /// </summary>
    internal static bool IsAuthorityBanker(Banker banker)
    {
        try
        {
            return NetworkBigBoss.HasWorldAuth && IsCurrentBanker(banker);
        }
        catch (Exception e)
        {
            ReportFailure(e);
            return false;
        }
    }

    /// <summary>
    /// 对象是否仍属于当前 world 的 gameLayer 与同一 scene。用于在换岛/卸载/切世界后
    /// 区分“还活着的本世界对象”（可做原生回滚）与“旧场景残留”（只丢本地引用，
    /// 绝不发 RPC、绝不触碰原生对象）。读取异常一律按不是当前层处理。
    /// </summary>
    internal static bool IsInCurrentLayer(Component component)
    {
        if (component == null) return false;
        try { return InLayer(component.gameObject, component.transform); }
        catch (Exception e) { ReportFailure(e); return false; }
    }

    internal static bool IsInCurrentLayer(GameObject gameObject)
    {
        if (gameObject == null) return false;
        try { return InLayer(gameObject, gameObject.transform); }
        catch (Exception e) { ReportFailure(e); return false; }
    }

    private static bool InLayer(GameObject owner, Transform self)
    {
        if (owner == null || self == null) return false;
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (layer == null || layer.gameObject == null || !layer.gameObject.activeInHierarchy) return false;
        if (!self.IsChildOf(layer)) return false;
        return owner.scene.handle == layer.gameObject.scene.handle;
    }

    private static void ReportFailure(Exception error)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[BankScope] World read fault; treating scope as unknown this frame: "
            + error.GetType().Name);
    }
}
