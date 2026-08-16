using System;
using System.Collections.Generic;
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
    private const float BlockLogInterval = 30f;
    private const float RuntimePruneInterval = 5f;

    private enum BlockReason
    {
        None,
        OnlineTransactionUnsupported,
        WorldAuthorityMissing,
        WorldNotPlaying,
        KingdomUnsafe,
        OutsideCurrentScene,
        NativePaymentLayoutNotReady,
        BallistaNotActive,
        ActiveTarget,
        WindingUp,
        TowerUnderConstruction,
        HeldBoltInactive,
        HeldBoltPoolMissing,
        BallistaStateNotStable,
        PlayerTransactionMismatch,
        CleanupFailed,
        PreparedTokenMissing
    }

    private sealed class BlockDiagnostic
    {
        internal IntPtr Pointer;
        internal IntPtr SceneRootPointer;
        internal BlockReason PendingReason;
        internal BlockReason LastLoggedReason;
        internal float NextLogAt;
    }

    private sealed class PreparedToken
    {
        internal IntPtr Pointer;
        internal IntPtr PlayerPointer;
        internal int PlayerInstanceId;
        internal IntPtr WorldPointer;
        internal IntPtr SceneRootPointer;
        internal int Frame;
    }

    private static bool _markerRegistered;
    private static int _configuredSourceId;
    private static int _configuredBiome = -1;
    private static string _lastFailure;
    private static readonly Dictionary<int, BlockDiagnostic> BlockDiagnostics = new();
    private static readonly Dictionary<int, PreparedToken> PreparedTokens = new();
    private static float _nextRuntimePruneAt;

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
        if (NetworkBigBoss.IsOnline)
            return Block(payable, BlockReason.OnlineTransactionUnsupported);
        if (!NetworkBigBoss.HasWorldAuth)
            return Block(payable, BlockReason.WorldAuthorityMissing);

        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.world == null
            || managers.kingdom == null || managers.game.state != Game.State.Playing)
            return Block(payable, BlockReason.WorldNotPlaying);
        if (!managers.kingdom.isSafe) return Block(payable, BlockReason.KingdomUnsafe);
        if (managers.world.gameLayer == null
            || payable.transform == null
            || !payable.transform.IsChildOf(managers.world.gameLayer))
            return Block(payable, BlockReason.OutsideCurrentScene);
        if (payable.parentHeaderRef == null || payable.nextPrefab == null
            || payable.GetComponent<Persistent>() == null)
            return Block(payable, BlockReason.NativePaymentLayoutNotReady);

        Ballista ballista = payable.GetComponent<Ballista>();
        if (ballista == null || ballista.gameObject == null
            || !ballista.gameObject.activeInHierarchy || !ballista.enabled
            || ballista._currentActors == null)
            return Block(payable, BlockReason.BallistaNotActive);

        // Workers are deliberately allowed. Destroying the old root invalidates their
        // Workable reference; the native worker routine observes Unity-null on its next
        // step and runs ResetWorkState. Never clear actors or call private worker hooks.
        if (ballista._target != null) return Block(payable, BlockReason.ActiveTarget);
        if (ballista._windingUpEmitter != null && ballista._windingUpEmitter.IsPlaying)
            return Block(payable, BlockReason.WindingUp);

        if (ballista.tower != null && ballista.tower.UnderConstruction)
            return Block(payable, BlockReason.TowerUnderConstruction);

        if (ballista._state == Ballista.State.Ready)
        {
            // A just-restored native Ballista can briefly be Ready before its
            // authority-local held bolt has been reconstructed.  With no
            // target and a safe kingdom there is no inventory to leak, so this
            // is also a valid rebuild state.
            if (ballista._bolt == null) return Allow(payable);

            if (ballista._bolt.gameObject == null
                || !ballista._bolt.gameObject.activeInHierarchy)
                return Block(payable, BlockReason.HeldBoltInactive);
            if (Pool.GetPoolFromPrefabInstance(ballista._bolt.gameObject) == null)
                return Block(payable, BlockReason.HeldBoltPoolMissing);
            return Allow(payable);
        }

        if (ballista._state == Ballista.State.Reloading
            && ballista._currentWork == 0 && ballista._bolt == null)
            return Allow(payable);
        return Block(payable, BlockReason.BallistaStateNotStable);
    }

    public static bool TryPrepare(PayableUpgrade payable, Player player)
    {
        if (!IsRebuildPayable(payable)) return false;
        if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth
            || player == null || player._payState != Player.PayState.Completed
            || player._completingPayable == null
            || player._completingPayable.Pointer != payable.Pointer)
            return Block(payable, BlockReason.PlayerTransactionMismatch);
        if (!CanInteract(payable)) return false;

        try
        {
            Ballista ballista = payable.GetComponent<Ballista>();
            if (ballista == null) return Block(payable, BlockReason.BallistaNotActive);

            int id = payable.gameObject.GetInstanceID();
            if (!TryGetRuntimeIdentity(out IntPtr worldPointer,
                    out IntPtr scenePointer))
                return Block(payable, BlockReason.OutsideCurrentScene);
            PruneRuntimeState(worldPointer, scenePointer);
            if (PreparedTokens.TryGetValue(id, out PreparedToken existing)
                && existing.Pointer == payable.Pointer
                && existing.PlayerPointer == player.Pointer
                && existing.PlayerInstanceId == player.GetInstanceID()
                && existing.WorldPointer == worldPointer
                && existing.SceneRootPointer == scenePointer
                && existing.Frame == Time.frameCount)
            {
                return true;
            }
            PreparedTokens.Remove(id);

            Bolt bolt = ballista._bolt;
            GameObject boltObject = bolt?.gameObject;
            var token = new PreparedToken
            {
                Pointer = payable.Pointer,
                PlayerPointer = player.Pointer,
                PlayerInstanceId = player.GetInstanceID(),
                WorldPointer = worldPointer,
                SceneRootPointer = scenePointer,
                Frame = Time.frameCount
            };
            PreparedTokens[id] = token;

            // A held bolt is authority-local before launch. Temporarily detach the
            // reference, then use the public boolean pool operation. A failed call
            // restores only a still-active bolt with a valid origin pool; otherwise
            // native Reloading state lets workers rebuild it after the refund.
            if (bolt != null)
            {
                if (boltObject == null || !boltObject.activeInHierarchy
                    || Pool.GetPoolFromPrefabInstance(boltObject) == null)
                {
                    PreparedTokens.Remove(id);
                    return Block(payable, BlockReason.HeldBoltPoolMissing);
                }

                ballista._bolt = null;
                bool despawned;
                try { despawned = Pool.TryDespawn(boltObject, 0f); }
                catch
                {
                    RestoreBoltOrNormalizeReload(ballista, bolt, boltObject);
                    PreparedTokens.Remove(id);
                    throw;
                }
                if (!despawned)
                {
                    RestoreBoltOrNormalizeReload(ballista, bolt, boltObject);
                    PreparedTokens.Remove(id);
                    return Block(payable, BlockReason.CleanupFailed);
                }
            }
            return true;
        }
        catch (Exception e)
        {
            RemovePrepared(payable);
            // Continuing would destroy the old root while potentially leaving its
            // authority-local held projectile orphaned. CanPay preflights the pool;
            // this is a last-moment fail-closed guard for wrapper/pool races.
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[SpecialTowerRebuild] Pre-pay cleanup failed: " + e);
            return Block(payable, BlockReason.CleanupFailed);
        }
    }

    private static void RestoreBoltOrNormalizeReload(Ballista ballista, Bolt bolt,
        GameObject boltObject)
    {
        bool canRestore = false;
        try
        {
            canRestore = bolt != null && boltObject != null
                && boltObject.activeInHierarchy
                && Pool.GetPoolFromPrefabInstance(boltObject) != null;
        }
        catch { }

        try
        {
            if (canRestore)
            {
                ballista._bolt = bolt;
                return;
            }

            ballista._bolt = null;
            ballista._state = Ballista.State.Reloading;
            ballista._currentWork = 0;
        }
        catch
        {
            // The transaction still fails closed. Never reattach an object whose
            // active/pool identity could not be proven after TryDespawn failed.
            try { ballista._bolt = null; } catch { }
        }
    }

    private static void RemovePrepared(PayableUpgrade payable)
    {
        try
        {
            if (payable == null || payable.gameObject == null) return;
            int id = payable.gameObject.GetInstanceID();
            if (PreparedTokens.TryGetValue(id, out PreparedToken token)
                && token.Pointer == payable.Pointer)
                PreparedTokens.Remove(id);
        }
        catch { }
    }

    public static bool ConsumePrepared(PayableUpgrade payable)
    {
        if (!IsRebuildPayable(payable)) return true;
        try
        {
            int id = payable.gameObject.GetInstanceID();
            if (!TryGetRuntimeIdentity(out IntPtr worldPointer,
                    out IntPtr scenePointer))
                return Block(payable, BlockReason.PreparedTokenMissing);
            PruneRuntimeState(worldPointer, scenePointer);
            Player player = payable.interactingPlayer;
            if (!PreparedTokens.TryGetValue(id, out PreparedToken token)
                || token.Pointer != payable.Pointer
                || player == null || token.PlayerPointer != player.Pointer
                || token.PlayerInstanceId != player.GetInstanceID()
                || token.WorldPointer != worldPointer
                || token.SceneRootPointer != scenePointer
                || token.Frame != Time.frameCount)
                return Block(payable, BlockReason.PreparedTokenMissing);

            PreparedTokens.Remove(id);
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[SpecialTowerRebuild] Prepared token validation failed: " + e);
            return false;
        }
    }

    private static bool Allow(PayableUpgrade payable)
    {
        try
        {
            int id = payable.gameObject.GetInstanceID();
            if (BlockDiagnostics.TryGetValue(id, out BlockDiagnostic diagnostic)
                && diagnostic.Pointer == payable.Pointer)
            {
                diagnostic.PendingReason = BlockReason.None;
                diagnostic.LastLoggedReason = BlockReason.None;
            }
        }
        catch { }
        return true;
    }

    private static bool Block(PayableUpgrade payable, BlockReason reason)
    {
        try
        {
            int id = payable.gameObject.GetInstanceID();
            IntPtr pointer = payable.Pointer;
            TryGetRuntimeIdentity(out IntPtr worldPointer, out IntPtr scenePointer);
            PruneRuntimeState(worldPointer, scenePointer);
            if (!BlockDiagnostics.TryGetValue(id, out BlockDiagnostic diagnostic)
                || diagnostic.Pointer != pointer
                || diagnostic.SceneRootPointer != scenePointer)
            {
                diagnostic = new BlockDiagnostic
                {
                    Pointer = pointer,
                    SceneRootPointer = scenePointer
                };
                BlockDiagnostics[id] = diagnostic;
            }

            diagnostic.PendingReason = reason;
            if (diagnostic.LastLoggedReason == reason) return false;
            float now = Time.unscaledTime;
            if (now < diagnostic.NextLogAt) return false;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[SpecialTowerRebuild] Blocked instance=" + id + " reason=" + reason);
            diagnostic.LastLoggedReason = reason;
            diagnostic.NextLogAt = now + BlockLogInterval;
        }
        catch
        {
            // Diagnostics must never change the payable gate.
        }
        return false;
    }

    private static bool TryGetRuntimeIdentity(out IntPtr worldPointer,
        out IntPtr scenePointer)
    {
        worldPointer = IntPtr.Zero;
        scenePointer = IntPtr.Zero;
        try
        {
            World world = Managers.Inst?.world;
            Transform sceneRoot = world?.gameLayer;
            if (world == null || sceneRoot == null) return false;
            worldPointer = world.Pointer;
            scenePointer = sceneRoot.Pointer;
            return worldPointer != IntPtr.Zero && scenePointer != IntPtr.Zero;
        }
        catch { return false; }
    }

    private static void PruneRuntimeState(IntPtr worldPointer, IntPtr scenePointer)
    {
        float now = Time.unscaledTime;
        if (now < _nextRuntimePruneAt) return;
        _nextRuntimePruneAt = now + RuntimePruneInterval;

        var staleDiagnostics = new List<int>();
        foreach (KeyValuePair<int, BlockDiagnostic> pair in BlockDiagnostics)
        {
            if (pair.Value == null || pair.Value.SceneRootPointer != scenePointer)
                staleDiagnostics.Add(pair.Key);
        }
        for (int i = 0; i < staleDiagnostics.Count; i++)
            BlockDiagnostics.Remove(staleDiagnostics[i]);

        var staleTokens = new List<int>();
        foreach (KeyValuePair<int, PreparedToken> pair in PreparedTokens)
        {
            if (pair.Value == null || pair.Value.WorldPointer != worldPointer
                || pair.Value.SceneRootPointer != scenePointer)
                staleTokens.Add(pair.Key);
        }
        for (int i = 0; i < staleTokens.Count; i++)
            PreparedTokens.Remove(staleTokens[i]);
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
    private static bool CanPay_Prefix(PayableUpgrade __instance, Player player,
        ref bool __result)
    {
        if (!SpecialTowerRebuild.IsRebuildPayable(__instance)) return true;
        if (SpecialTowerRebuild.CanInteract(__instance)) return true;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(PayableUpgrade), nameof(PayableUpgrade.CanPay))]
    [HarmonyPostfix]
    private static void CanPay_Postfix(PayableUpgrade __instance, Player player,
        ref bool __result)
    {
        if (!__result || !SpecialTowerRebuild.IsRebuildPayable(__instance)
            || player == null || player._payState != Player.PayState.Completed
            || player._completingPayable == null
            || player._completingPayable.Pointer != __instance.Pointer)
            return;

        // This is the final native CanPay in Player's Completed branch. Returning
        // false makes the untouched transaction follow CancelTransaction and then
        // DropFloatingCurrency instead of reaching TransactionComplete.
        __result = SpecialTowerRebuild.TryPrepare(__instance, player);
    }

    [HarmonyPatch(typeof(PayableUpgrade), nameof(PayableUpgrade.Pay))]
    [HarmonyPrefix]
    private static bool Pay_Prefix(PayableUpgrade __instance)
    {
        return SpecialTowerRebuild.ConsumePrepared(__instance);
    }
}
