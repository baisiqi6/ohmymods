using System;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Marker added to supported special-tower prefab assets before their CRPC
/// components are enumerated.  It deliberately carries no state and implements
/// neither IRPCable nor Persistent.IBehaviour; the native PayableUpgrade added
/// beside it owns the original payment, networking and save-data lifecycle.
/// </summary>
public sealed class SpecialTowerRebuildMarker : MonoBehaviour
{
    public SpecialTowerRebuildMarker(IntPtr pointer) : base(pointer)
    {
    }
}

/// <summary>
/// First, deliberately narrow special-tower rebuild slice:
///
/// - an idle Ballista specialisation may be rebuilt into the current biome's
///   native level-six ordinary tower;
/// - the player can then use any native passenger upgrade available on that
///   ordinary tower (for example Knight/Berserker) without this patch having to
///   reproduce hermit, construction, Persistent or CRPC behaviour;
/// - Fire, OilFire, Baker/Mead and Knight/Berserker towers remain fail-closed
///   until their inventory, hidden occupants or sibling shop lifecycles have a
///   separately verified teardown path.
/// </summary>
internal static class SpecialTowerRebuild
{
    private const float RebuildCooldown = 1.5f;

    private static bool _markerRegistered;
    private static int _configuredSourceId;
    private static int _configuredBiome = -1;
    private static string _lastFailure;

    public static void EnsurePrefabLayout()
    {
        try
        {
            EnsureMarkerRegistered();

            Managers managers = Managers.Inst;
            Holder holder = managers != null ? managers.holder : null;
            BiomeHolder biomeHolder = BiomeHolder.Inst;
            if (holder == null || holder.towerPrefabs == null || biomeHolder == null
                || biomeHolder.LoadedBiome == null)
            {
                ReportFailure("native Holder/biome data is not ready");
                return;
            }

            Tower tierSix = null;
            for (int i = 0; i < holder.towerPrefabs.Length; i++)
            {
                Tower candidate = holder.towerPrefabs[i];
                if (candidate == null || candidate.level != 6) continue;
                if (tierSix != null && tierSix.Pointer != candidate.Pointer)
                {
                    ReportFailure("multiple native level-six tower prefabs were found");
                    return;
                }
                tierSix = candidate;
            }

            if (tierSix == null || tierSix.gameObject == null)
            {
                ReportFailure("native level-six tower prefab was not found");
                return;
            }

            PayableUpgrade template = tierSix.GetComponent<PayableUpgrade>();
            if (template == null || template.passengerUpgrades == null)
            {
                ReportFailure("native level-six PayableUpgrade profile is unavailable");
                return;
            }

            string ballistaTag = Hermit.GetHermitTag(Hermit.HermitType.Ballista);
            GameObject configuredBallista = null;
            for (int i = 0; i < template.passengerUpgrades.Length; i++)
            {
                RequireTagUpgrade route = template.passengerUpgrades[i];
                if (route == null || route.tag != ballistaTag || route.prefab == null) continue;
                if (configuredBallista != null && configuredBallista.Pointer != route.prefab.Pointer)
                {
                    ReportFailure("multiple native Ballista passenger routes were found");
                    return;
                }
                configuredBallista = route.prefab;
            }

            if (configuredBallista == null)
            {
                ReportFailure("native Ballista passenger route was not found");
                return;
            }

            GameObject effectiveSource = BiomeData.GetAssetSwap<GameObject>(configuredBallista);
            if (effectiveSource == null)
            {
                ReportFailure("current-biome Ballista specialisation could not be resolved");
                return;
            }

            // Norselands maps the Ballista route to OilFireArcherTower rather
            // than Ballista.  It is intentionally excluded from this first
            // slice because it owns hidden GuardSlot/projectile inventory.
            Ballista ballista = effectiveSource.GetComponent<Ballista>();
            if (ballista == null
                || effectiveSource.GetComponent<FireTower>() != null
                || effectiveSource.GetComponent<OilFireArcherTower>() != null
                || effectiveSource.GetComponent<TowerKnight>() != null)
            {
                ReportFailure("current-biome Ballista route is not the safe Ballista source type");
                return;
            }

            int sourceId = effectiveSource.GetInstanceID();
            if (_configuredSourceId == sourceId
                && _configuredBiome == biomeHolder.BiomeIndex
                && effectiveSource.GetComponent<SpecialTowerRebuildMarker>() != null
                && effectiveSource.GetComponent<PayableUpgrade>() != null)
            {
                return;
            }

            PayableUpgrade existing = effectiveSource.GetComponent<PayableUpgrade>();
            SpecialTowerRebuildMarker marker = effectiveSource.GetComponent<SpecialTowerRebuildMarker>();
            if (existing != null && marker == null)
            {
                ReportFailure("source already owns a native PayableUpgrade; refusing to alter its layout");
                return;
            }

            if (marker == null)
            {
                marker = effectiveSource.AddComponent<SpecialTowerRebuildMarker>();
            }
            if (marker == null)
            {
                ReportFailure("failed to attach the rebuild marker");
                return;
            }

            PayableUpgrade rebuild = existing != null
                ? existing
                : effectiveSource.AddComponent<PayableUpgrade>();
            if (rebuild == null)
            {
                ReportFailure("failed to attach native PayableUpgrade before pool registration");
                return;
            }

            ConfigurePayable(rebuild, template, tierSix.gameObject);

            _configuredSourceId = sourceId;
            _configuredBiome = biomeHolder.BiomeIndex;
            _lastFailure = null;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[SpecialTowerRebuild] Ready source=" + effectiveSource.name
                + " target=" + tierSix.gameObject.name
                + " price=" + rebuild.Price
                + " biome=" + biomeHolder.BiomeIndex);
        }
        catch (Exception e)
        {
            ReportFailure(e.GetType().Name + ": " + e.Message);
        }
    }

    private static void EnsureMarkerRegistered()
    {
        if (_markerRegistered) return;
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(SpecialTowerRebuildMarker)))
        {
            ClassInjector.RegisterTypeInIl2Cpp(typeof(SpecialTowerRebuildMarker));
        }
        _markerRegistered = true;
    }

    private static void ConfigurePayable(
        PayableUpgrade rebuild,
        PayableUpgrade template,
        GameObject nativeTierSix)
    {
        // Copy the native level-six payment presentation/profile rather than
        // hard-coding the observed resource price.  Do not copy highlight
        // renderer references: those belong to the template prefab.
        rebuild.Price = template.Price;
        rebuild.Currency = template.Currency;
        rebuild.priceIncrease = 0;
        rebuild.indicatorOffset = template.indicatorOffset;
        rebuild.repositionInSplitScreen = template.repositionInSplitScreen;
        rebuild.coopIndicatorOffset = template.coopIndicatorOffset;
        rebuild.indicatorSpacing = template.indicatorSpacing;
        rebuild.glowOnSelect = template.glowOnSelect;
        rebuild.glowColor = template.glowColor;
        rebuild.soundOnPay = template.soundOnPay;
        rebuild.playerPayPointOffset = template.playerPayPointOffset;
        rebuild.playerPayDistance = template.playerPayDistance;
        rebuild.payablePlacementExclusionOffset = template.payablePlacementExclusionOffset;
        rebuild.payablePlacementExclusionDistance = template.payablePlacementExclusionDistance;
        rebuild.overrideHighlightRenderers = false;
        rebuild._highlightSpriteFXOverride = new Il2CppReferenceArray<SpriteRendererFX>(0);

        rebuild.deactivateAfterUpgrade = false;
        rebuild.nextPrefab = nativeTierSix;
        rebuild.onlyInBuildableRegion = template.onlyInBuildableRegion;
        rebuild.ignoreRegionRestrictIfExpo = template.ignoreRegionRestrictIfExpo;
        rebuild.requiresStoneTech = template.requiresStoneTech;
        rebuild.requiresIronTech = template.requiresIronTech;
        rebuild.cooldown = Mathf.Max(template.cooldown, RebuildCooldown);
        rebuild.passengerUpgrades = new Il2CppReferenceArray<RequireTagUpgrade>(0);
        rebuild.statToIncrement = Stat.Null;
        rebuild.forceBlockPayment = false;
        rebuild.blockPaymentUpgrade = !ModConfig.Enabled.Value;
    }

    public static bool IsRebuildPayable(PayableUpgrade payable)
    {
        return payable != null
            && payable.gameObject != null
            && payable.GetComponent<SpecialTowerRebuildMarker>() != null;
    }

    public static bool CanInteract(PayableUpgrade payable)
    {
        if (!IsRebuildPayable(payable)) return true;

        payable.blockPaymentUpgrade = !ModConfig.Enabled.Value;
        if (!ModConfig.Enabled.Value) return false;

        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.world == null
            || managers.kingdom == null || managers.game.state != Game.State.Playing
            || !managers.kingdom.isSafe || managers.world.gameLayer == null
            || payable.parentHeaderRef == null || payable.nextPrefab == null
            || payable.GetComponent<Persistent>() == null
            || !payable.transform.IsChildOf(managers.world.gameLayer))
        {
            return false;
        }

        Ballista ballista = payable.GetComponent<Ballista>();
        if (ballista == null || ballista.gameObject == null
            || !ballista.gameObject.activeInHierarchy || !ballista.enabled
            || ballista._currentActors == null || ballista._currentActors.Count != 0
            || ballista._target != null
            || ballista._windingUpEmitter != null && ballista._windingUpEmitter.IsPlaying)
        {
            return false;
        }

        if (ballista.tower != null && ballista.tower.UnderConstruction) return false;

        if (ballista._state == Ballista.State.Ready)
        {
            // Held bolts are authority-local until launch; remote clients only
            // receive the Ballista reload/angle state.  Let the client present
            // the payable and rely on the host's immediately repeated CanPay
            // validation before accepting the transaction.
            if (!NetworkBigBoss.HasWorldAuth) return true;

            // A just-restored native Ballista can briefly be Ready before its
            // authority-local held bolt has been reconstructed.  With no
            // target and a safe kingdom there is no inventory to leak, so this
            // is also a valid rebuild state.
            if (ballista._bolt == null) return true;

            if (ballista._bolt.gameObject == null
                || !ballista._bolt.gameObject.activeInHierarchy)
            {
                return false;
            }
            return Pool.GetPoolFromPrefabInstance(ballista._bolt.gameObject) != null;
        }

        return ballista._state == Ballista.State.Reloading
            && ballista._currentWork == 0
            && ballista._bolt == null;
    }

    public static void PrepareNativePay(PayableUpgrade payable)
    {
        if (!IsRebuildPayable(payable)) return;

        try
        {
            Ballista ballista = payable.GetComponent<Ballista>();
            if (ballista == null) return;

            // A held bolt is authority-local before launch.  Return it to the
            // native pool without emitting a separate despawn RPC; Payable.Pay
            // itself is already replayed on both peers by the original RPC.
            Bolt bolt = ballista._bolt;
            ballista._target = null;
            if (bolt != null && bolt.gameObject != null && bolt.gameObject.activeInHierarchy)
            {
                Pool.Despawn(bolt.gameObject, false);
            }
            ballista._bolt = null;

            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[SpecialTowerRebuild] Rebuilding " + payable.gameObject.name
                + " into native level-six tower");
        }
        catch (Exception e)
        {
            // Payment has already completed by this point; never consume the
            // player's coins and then suppress native Pay.  The old root is
            // still replaced by the original transaction even if a defensive
            // bolt cleanup unexpectedly fails.
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[SpecialTowerRebuild] Pre-pay cleanup failed: " + e);
        }
    }

    private static void ReportFailure(string reason)
    {
        if (_lastFailure == reason) return;
        _lastFailure = reason;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[SpecialTowerRebuild] Disabled for this biome: " + reason);
    }
}

/// <summary>
/// Runs before the existing PoolManager Init prefix that eagerly constructs
/// native pools, ensuring cloned Ballista instances have an identical native
/// PayableUpgrade/IRPCable component layout on host and client.
/// </summary>
[HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Init))]
public static class PoolManager_SpecialTowerRebuild_Init_Patch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        SpecialTowerRebuild.EnsurePrefabLayout();
    }
}

[HarmonyPatch]
public static class PayableUpgrade_SpecialTowerRebuild_Gate_Patch
{
    [HarmonyPatch(typeof(PayableUpgrade), nameof(PayableUpgrade.CanSelect))]
    [HarmonyPrefix]
    private static bool CanSelect_Prefix(PayableUpgrade __instance, ref bool __result)
    {
        if (!SpecialTowerRebuild.IsRebuildPayable(__instance)) return true;
        if (SpecialTowerRebuild.CanInteract(__instance)) return true;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(PayableUpgrade), nameof(PayableUpgrade.CanPay))]
    [HarmonyPrefix]
    private static bool CanPay_Prefix(PayableUpgrade __instance, ref bool __result)
    {
        if (!SpecialTowerRebuild.IsRebuildPayable(__instance)) return true;
        if (SpecialTowerRebuild.CanInteract(__instance)) return true;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(PayableUpgrade), nameof(PayableUpgrade.Pay))]
    [HarmonyPrefix]
    private static void Pay_Prefix(PayableUpgrade __instance)
    {
        SpecialTowerRebuild.PrepareNativePay(__instance);
    }
}
