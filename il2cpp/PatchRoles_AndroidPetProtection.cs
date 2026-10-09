#if ANDROID
namespace KingdomEnhancedMod;

/// <summary>
/// Android 薄聚合 handler：Operator 在 Probe 里对 Droppable.OnEnable / OnDisable 两个长生命周期
/// 目标各注册一个 HarmonyInstance.Patch（OnEnable postfix1；OnDisable prefix1），按 PC 加载源
/// 既有顺序 Hermit→PetGuard 调用两个已有共享 handler。
/// 无 HarmonyPatch 扫描特性、无 catch/finalizer、无自有逻辑；不为同一 target 建重叠 detour，
/// 短 getter 不挂。Droppable 由 android/GlobalAliases.cs 的 global alias 解析到实际 interop 类型。
/// </summary>
internal static class AndroidPetProtectionHooks
{
    internal static void OnEnablePostfix(Droppable __instance)
    {
        Droppable_OnEnable_HermitPickupPolicy_Patch.Postfix(__instance);
        Droppable_OnEnable_PetGuard_Patch.Postfix(__instance);
    }

    internal static void OnDisablePrefix(Droppable __instance)
    {
        Droppable_OnDisable_HermitPickupPolicy_Patch.Prefix(__instance);
        Droppable_OnDisable_PetGuard_Patch.Prefix(__instance);
    }
}
#endif
