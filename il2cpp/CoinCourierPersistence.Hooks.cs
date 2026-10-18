using System;
using HarmonyLib;

namespace KingdomEnhancedMod;

internal static partial class CoinCourierPersistence
{
    // ============================================================ 原生只读小工具

    private static GlobalSaveData SafeLoaded()
    {
        try { return GlobalSaveData._loaded; } catch (Exception) { return null; }
    }

    private static CampaignSaveData SafeCurrentCampaign()
    {
        try { return CampaignSaveData.current; } catch (Exception) { return null; }
    }

    private static bool SafeOnline()
    {
        try { return NetworkBigBoss.IsOnline; } catch (Exception) { return true; }
    }

    // ============================================================ Harmony 钩子（记账型）

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    internal static class IslandSaveScopePatch
    {
        internal sealed class Capture
        {
#if !ANDROID
            internal SaveScope Coin;
#endif
            internal HeavyShieldIntegration.SaveScope Heavy;
            internal SharedBankNative.Scope Bank;
        }
        [HarmonyPrefix]
        private static void Prefix(int __0, int __1, int __2, out Capture __state)
        {
#if !ANDROID
            CancelPendingShopTransactions();
#endif
            __state = new Capture { Heavy = HeavyShieldIntegration.BeginSave(__0, __1, __2) };
#if !ANDROID
            __state.Coin = BeginIslandSave(__0, __1, __2);
#endif
            __state.Bank = SharedBankNative.BeginSave(__0, __1, __2);
        }

#if !ANDROID
        /// <summary>
        /// 真实 2.4 的 Save 是同步序列（Tick 观察不到 isSavingGame 的窗口），因此这里用既有
        /// Shop 取消入口在建立 scope 前取消未完成投币（只走原生退款路径；已结算/Completed/
        /// RecruitmentLocked 的交易不动）。独立隔离：异常只留一次有界诊断，绝不阻塞
        /// BeginIslandSave 与原生 Save。
        /// </summary>
        private static void CancelPendingShopTransactions()
        {
            try
            {
                CoinCourierShop.CancelPendingTransactions();
            }
            catch (Exception e)
            {
                NoteOnce("scope-cancel:" + e.GetType().Name,
                    "cancel pending shop transactions failed before save: " + e.GetType().Name, true);
            }
        }
#endif

        [HarmonyPostfix]
        private static void Postfix(Capture __state)
        {
#if !ANDROID
            EndIslandSave(__state?.Coin, true);
#endif
            SharedBankNative.EndSave(__state?.Bank, true);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Capture __state)
        {
            try
            {
                HeavyShieldIntegration.EndSave(__state?.Heavy, __exception == null);
            }
            finally
            {
#if !ANDROID
                EndIslandSave(__state?.Coin, false);
#endif
                SharedBankNative.EndSave(__state?.Bank, false);
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.UpdateSavedWithRevisions))]
    internal static class IslandCaptureMarkerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(IslandSaveData __instance)
        {
            HeavyShieldIntegration.ObserveMarker(__instance);
#if !ANDROID
            ObserveMarker(__instance);
#endif
            SharedBankNative.Marker(__instance);
        }
    }

    [HarmonyPatch(typeof(PrefsSaveData), nameof(PrefsSaveData.PrepareBeforeSave))]
    internal static class PrefsPreparePatch
    {
        [HarmonyPrefix]
        private static void Prefix(PrefsSaveData __instance, out HeavyShieldPersistence.PrepareCapture __state)
        {
            __state = HeavyShieldIntegration.BeginPrepare(__instance);
#if !ANDROID
            ObservePrepare(__instance);
#endif
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, HeavyShieldPersistence.PrepareCapture __state)
        {
            HeavyShieldIntegration.EndPrepare(__state, __exception == null);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.CreateNewCampaign))]
    internal static class CampaignCreatePatch
    {
        [HarmonyPrefix]
        private static void Prefix(GlobalSaveData __instance)
        {
            HeavyShieldIntegration.BeforeCampaignMutation(__instance);
#if !ANDROID
            ObserveCampaignMutation(__instance);
#endif
            SharedBankNative.BeforeMutation(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(GlobalSaveData __instance, CampaignSaveData __result)
        {
            HeavyShieldIntegration.AfterCampaignCreated(__instance, __result);
#if !ANDROID
            ObserveCampaignCreated(__instance, __result);
#endif
        }
    }

    // 实际 2.4 静态证据：TryDeleteCampaignAsync 内联 RemoveAt+Save+SaveAsync，不调用 _TryDeleteCampaign
    // → 保留本 prefix 做绑前快照；协程侧改挂 d__91.MoveNext（见下）。
    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.TryDeleteCampaignAsync))]
    internal static class CampaignDeleteAsyncPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            HeavyShieldIntegration.BeforeCampaignMutation(SafeLoaded());
#if !ANDROID
            ObserveCampaignMutation(SafeLoaded());
#endif
            SharedBankNative.BeforeMutation(SafeLoaded());
        }
    }

    // 实际 2.4 静态证据：TryDeleteCampaignAsync 内联 RemoveAt+Save+SaveAsync，不调用 _TryDeleteCampaign。
    // 协程 factory 的 4-byte Return<SaveLoadResult> 按值参数在当前 Interop 下被映成 IntPtr 再 value_box，
    // prefix 即使不读参数也会在 native 入口崩（IL_STUB_ReversePInvoke + value_box，两次用户启动 dump 同证）→
    // 改为只挂状态机 MoveNext（bool 无参）的 state==0 记账；绝不触碰 factory 与 Return<T>。
    [HarmonyPatch(typeof(GlobalSaveData.__TryDeleteCampaign_d__91),
        nameof(GlobalSaveData.__TryDeleteCampaign_d__91.MoveNext))]
    internal static class CampaignDeleteRoutinePatch
    {
        [HarmonyPrefix]
        private static void Prefix(GlobalSaveData.__TryDeleteCampaign_d__91 __instance)
        {
            try { if (__instance != null && __instance.__1__state == 0) HeavyShieldIntegration.BeforeCampaignMutation(SafeLoaded()); }
            catch (Exception e) { HeavyShieldIntegration.Fault("delete-routine", e); }
#if !ANDROID
            ObserveDeleteRoutineState(__instance);
#endif
            try
            {
                if (__instance != null && __instance.__1__state == 0)
                    SharedBankNative.BeforeMutation(SafeLoaded());
            }
            catch (Exception) { SharedBankNative.BeforeMutation(SafeLoaded()); }
        }
    }

    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.DeleteChallenge))]
    internal static class ChallengeDeletePatch
    {
        [HarmonyPrefix] private static void Prefix(GlobalSaveData __instance)
            => SharedBankNative.BeforeMutation(__instance);
    }

    [HarmonyPatch(typeof(GlobalSaveData.__TryDeleteChallenge_d__94),
        nameof(GlobalSaveData.__TryDeleteChallenge_d__94.MoveNext))]
    internal static class ChallengeDeleteRoutinePatch
    {
        [HarmonyPrefix] private static void Prefix(GlobalSaveData.__TryDeleteChallenge_d__94 __instance)
        {
            if (__instance != null && __instance.__1__state == 0)
                SharedBankNative.BeforeMutation(SafeLoaded());
        }
    }
}
