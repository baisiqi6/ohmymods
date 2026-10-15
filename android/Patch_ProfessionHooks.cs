using System;

namespace KingdomEnhancedMod;

internal static class AndroidProfessionHooks
{
    // Fixed G1 subset in the audited PC assembly's TypeDef RID order.
    // Resolve the whole array before the caller installs the first class processor.
    internal static Type[] ResolveOwners()
    {
        var assembly = typeof(AndroidProfessionHooks).Assembly;
        return new Type[]
        {
            assembly.GetType("KingdomEnhancedMod.HeavyShieldNativeDemote", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldReceiveDamage", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldTrollIntent", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldTrollImpact", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldArrowHit", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldArrowDamage", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Musketeer_GunPromotion_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Musketeer_DroppedStock_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Musketeer_PoolSpawnLife_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Musketeer_PoolDespawnLife_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Archer_OnEnable_RateLifetime_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Pool_FastSpawn_CrossbowmanLifecycleScope_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.Archer_OnEnable_CrossbowmanLifecycle_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.CoinCourierPersistence+IslandSaveScopePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.CoinCourierPersistence+IslandCaptureMarkerPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.CoinCourierPersistence+PrefsPreparePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.CoinCourierPersistence+CampaignCreatePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.CoinCourierPersistence+CampaignDeleteAsyncPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.CoinCourierPersistence+CampaignDeleteRoutinePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldCareerExclusion+TargetPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldCareerExclusion+EmbarkPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroBoardingPolicy+TargetPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroBoardingPolicy+EmbarkPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+AsyncGate", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+SyncGate", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+CreateCampaignPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+DeleteCampaignPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+DeleteCampaignRoutinePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+DeleteChallengePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroNativeRights+DeleteChallengeRoutinePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+VirginPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+ReplacementPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+SavePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+SaveMarkerPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+GetIdPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+PopPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+CreatePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeroRecruitment+DespawnPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerHooks+EligibilityPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerHooks+TargetPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerHooks+PickupPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerHooks+CancelPaymentBeforeSavePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerPersistence+SavePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerPersistence+GetIdPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerPersistence+PopPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerPersistence+CreatePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerPersistence+VirginPatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerRuntime+Archer_OnEnable_MusketeerPackage_Patch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.HeavyShieldNativeLoadProof+SourceCapturePatch", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerNativeSave+Musketeer_AsyncWriterGate", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerNativeSave+Musketeer_SyncWriterGate", throwOnError: true),
            assembly.GetType("KingdomEnhancedMod.MusketeerNativeSave+Musketeer_SaveAsyncObserver", throwOnError: true),
        };
    }
}
