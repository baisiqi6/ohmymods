using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// Android：职业自定义池在 native PoolManager.InitPools 完成边界重注册。
/// 与旧 PoolManager.Init 外层 post 资源窗口不同：InitPools postfix 在原生池缓存
/// 刚重建返回时即触发，也覆盖直接调用 InitPools 的路径；外层 post 等整个 Init
/// 流程结束，提供较晚的资源就绪窗口。
/// 幂等与 world/Holder/clone/active 条件由两个 helper 自带；不调
/// Castle.ReRegisterModPools（会开 Ninja/Ghost/Berserker 等无关池）。本 hook 不发
/// RPC。仅覆盖北境随从/弩矢两池，不代表全部 mod FX 池，也不替代实际攻击/死亡/
/// 换岛/读档/池重建实测。
/// </summary>
[HarmonyPatch(typeof(PoolManager), nameof(PoolManager.InitPools))]
internal static class AndroidProfessionPoolLifecycle
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ModConfig.Enabled.Value) return;
        PatchRoles_NorseSquad.EnsureNorseArcherPool();
        PatchRoles_Crossbowman.EnsureBoltPoolRegistered();
    }
}
