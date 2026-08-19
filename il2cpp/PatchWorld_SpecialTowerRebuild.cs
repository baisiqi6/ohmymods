using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
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
/// - a FireTower may be rebuilt the same way while its fuel is full and no
///   worker is on the crank: fuel full hides the native fuel PayableComponent
///   (FireTower.IsLocked always reports NotLocked, so its CanSelect collapses
///   to CanPay = _fireJarsActiveNum &lt; _maxFireJars), and with no worker the
///   state machine never leaves Reloading, so no fire jar is consumed and no
///   projectile is held while the old root is destroyed;
/// - the player can then use any native passenger upgrade available on that
///   ordinary tower (for example Knight/Berserker) without this patch having to
///   reproduce hermit, construction, Persistent or CRPC behaviour;
/// - OilFire, Baker/Mead and Knight/Berserker towers remain fail-closed
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
        FireFuelNotFull,
        FireWorkerPresent,
        FireProjectileInFlight,
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
    private static readonly HashSet<int> _configuredSources = new();
    private static int _configuredBiome = -1;
    private static string _lastFailure;
    private static string _lastUnsafeSummary;
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

            // A single-source resolution through GetAssetSwap is not reliable at
            // PoolManager.Init time: the swap may return the base "Tower Ballista"
            // asset (observed) while real builds and save restores instantiate the
            // biome-specific variant (e.g. "Tower Ballista_greece").  Every safe
            // candidate gets the layout, so lazily cloned instances inherit it no
            // matter which asset the game actually spawns.  FireTower assets are
            // collected by type scan (LoadAll), which needs no route resolution.
            List<GameObject> ballistaCandidates = CollectBallistaCandidates(configuredBallista);
            List<GameObject> fireCandidates = CollectFireTowerCandidates();
            if (ballistaCandidates.Count == 0 && fireCandidates.Count == 0)
            {
                ReportFailure("no special-tower source candidates could be resolved");
                return;
            }

            int biomeIndex = biomeHolder.BiomeIndex;
            if (_configuredBiome != biomeIndex)
            {
                _configuredSources.Clear();
                _configuredBiome = biomeIndex;
            }

            // The two families are component-exclusive (an asset owns either a
            // Ballista or a FireTower, never both), so a single flat loop with a
            // per-entry family flag keeps one idempotent _configuredSources set.
            var candidates = new List<GameObject>();
            var fireFlags = new List<bool>();
            for (int i = 0; i < ballistaCandidates.Count; i++)
            {
                candidates.Add(ballistaCandidates[i]);
                fireFlags.Add(false);
            }
            for (int i = 0; i < fireCandidates.Count; i++)
            {
                candidates.Add(fireCandidates[i]);
                fireFlags.Add(true);
            }

            var unsafeCandidates = new List<string>();
            var configuredBallistaNames = new List<string>();
            var configuredFireNames = new List<string>();
            PayableUpgrade firstPayable = null;
            int configuredCount = 0;
            bool configuredThisPass = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject source = candidates[i];
                if (source == null) continue;

                bool isFire = fireFlags[i];
                if (isFire ? !IsSafeFireTowerSource(source) : !IsSafeBallistaSource(source))
                {
                    unsafeCandidates.Add(source.name);
                    continue;
                }

                int sourceId = source.GetInstanceID();
                SpecialTowerRebuildMarker marker = source.GetComponent<SpecialTowerRebuildMarker>();
                PayableUpgrade existing = source.GetComponent<PayableUpgrade>();
                if (marker != null && existing != null && _configuredSources.Contains(sourceId))
                {
                    if (isFire) configuredFireNames.Add(source.name);
                    else configuredBallistaNames.Add(source.name);
                    configuredCount++;
                    if (firstPayable == null) firstPayable = existing;
                    continue;
                }

                if (existing != null && marker == null)
                {
                    // Only this candidate is skipped; other candidates continue.
                    // FireTower prefabs natively carry a PayableComponent (fuel),
                    // not a PayableUpgrade, so this check never rejects them.
                    ReportFailure("source " + source.name
                        + " already owns a native PayableUpgrade; refusing to alter its layout");
                    continue;
                }

                if (marker == null)
                {
                    marker = source.AddComponent<SpecialTowerRebuildMarker>();
                }
                if (marker == null)
                {
                    ReportFailure("failed to attach the rebuild marker to " + source.name);
                    continue;
                }

                PayableUpgrade rebuild = existing != null
                    ? existing
                    : source.AddComponent<PayableUpgrade>();
                if (rebuild == null)
                {
                    ReportFailure("failed to attach native PayableUpgrade to " + source.name);
                    continue;
                }

                ConfigurePayable(rebuild, template, tierSix.gameObject);

                _configuredSources.Add(sourceId);
                if (isFire) configuredFireNames.Add(source.name);
                else configuredBallistaNames.Add(source.name);
                configuredCount++;
                configuredThisPass = true;
                if (firstPayable == null) firstPayable = rebuild;
            }

            if (unsafeCandidates.Count > 0)
            {
                string unsafeSummary = string.Join(", ", unsafeCandidates.ToArray());
                if (_lastUnsafeSummary != unsafeSummary)
                {
                    _lastUnsafeSummary = unsafeSummary;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[SpecialTowerRebuild] Skipped unsafe special-tower candidates: ["
                        + unsafeSummary + "]");
                }
            }

            if (configuredCount == 0)
            {
                ReportFailure("no safe special-tower source candidate could be configured");
                return;
            }

            // A later Init for the same biome may find every candidate already
            // configured; there is nothing new to report then.
            if (!configuredThisPass) return;

            _lastFailure = null;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[SpecialTowerRebuild] Ready ballista=["
                + string.Join(", ", configuredBallistaNames.ToArray())
                + "] fire=["
                + string.Join(", ", configuredFireNames.ToArray())
                + "] target=" + tierSix.gameObject.name
                + " price=" + firstPayable.Price
                + " biome=" + biomeIndex);
        }
        catch (Exception e)
        {
            ReportFailure(e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Collects every plausible Ballista source prefab: the native route prefab
    /// after the current biome's asset swap (candidate A) plus every Ballista
    /// asset whose name contains "Tower Ballista" (candidate B, covering biome
    /// variants such as "Tower Ballista_greece").  Duplicates are dropped by
    /// native pointer.
    /// </summary>
    private static List<GameObject> CollectBallistaCandidates(GameObject routePrefab)
    {
        var candidates = new List<GameObject>();

        // Candidate A: the swap may not be effective yet at PoolManager.Init
        // time (observed: it returned the base "Tower Ballista" asset), so this
        // is just one candidate, never the single source of truth.
        GameObject swapped = null;
        try
        {
            swapped = BiomeData.GetAssetSwap<GameObject>(routePrefab);
        }
        catch
        {
            swapped = null;
        }
        AddCandidate(candidates, swapped);

        // Candidate B: Resources.LoadAll scans subdirectories, which
        // Resources.Load cannot do (repo precedent: LoadAll<WarriorGhostLeader>).
        var scanned = Resources.LoadAll<Ballista>("");
        if (scanned != null)
        {
            for (int i = 0; i < scanned.Length; i++)
            {
                Ballista scannedBallista = scanned[i];
                if (scannedBallista == null || scannedBallista.gameObject == null) continue;
                GameObject scannedObject = scannedBallista.gameObject;
                if (scannedObject.name == null
                    || !scannedObject.name.Contains("Tower Ballista")) continue;
                AddCandidate(candidates, scannedObject);
            }
        }

        return candidates;
    }

    private static void AddCandidate(List<GameObject> candidates, GameObject candidate)
    {
        if (candidate == null) return;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null && candidates[i].Pointer == candidate.Pointer) return;
        }
        candidates.Add(candidate);
    }

    /// <summary>
    /// Collects every FireTower prefab asset by type scan.  LoadAll walks
    /// subdirectories, which Resources.Load cannot do (same precedent as the
    /// Ballista scan), and OilFireArcherTower extends Workable directly, never
    /// FireTower, so the type filter excludes it natively.  The name filter
    /// drops unrelated assets ("Tower_upgrade_Fire", "Tower_upgrade_Fire_greece"
    /// etc. all contain "Tower").
    /// </summary>
    private static List<GameObject> CollectFireTowerCandidates()
    {
        var candidates = new List<GameObject>();
        var scanned = Resources.LoadAll<FireTower>("");
        if (scanned != null)
        {
            for (int i = 0; i < scanned.Length; i++)
            {
                FireTower scannedFire = scanned[i];
                if (scannedFire == null || scannedFire.gameObject == null) continue;
                GameObject scannedObject = scannedFire.gameObject;
                if (scannedObject.name == null
                    || !scannedObject.name.Contains("Tower")) continue;
                AddCandidate(candidates, scannedObject);
            }
        }
        return candidates;
    }

    /// <summary>
    /// True when the asset is a plain FireTower specialisation.  OilFireArcherTower
    /// is a sibling Workable class, not a FireTower subclass, so the LoadAll type
    /// scan already excludes it; the component check stays as defence in depth.
    /// Fire tower assets carry no Ballista/bolt inventory, so no Ballista-related
    /// checks apply here.
    /// </summary>
    private static bool IsSafeFireTowerSource(GameObject source)
    {
        if (source == null) return false;
        if (source.GetComponent<FireTower>() == null) return false;
        return source.GetComponent<OilFireArcherTower>() == null;
    }

    /// <summary>
    /// True when the asset is a plain Ballista tower: Norselands maps the
    /// Ballista route to OilFireArcherTower rather than Ballista, and Fire,
    /// OilFire and Knight towers own hidden GuardSlot/projectile inventory that
    /// this first slice has no teardown path for.
    /// </summary>
    private static bool IsSafeBallistaSource(GameObject source)
    {
        if (source == null) return false;
        Ballista ballista = source.GetComponent<Ballista>();
        if (ballista == null) return false;
        return source.GetComponent<FireTower>() == null
            && source.GetComponent<OilFireArcherTower>() == null
            && source.GetComponent<TowerKnight>() == null;
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
        // Rebuild replaces an existing structure in place; it is not a new
        // placement, so the buildable-region restriction must not apply (the
        // ballista's own keep-out collider would otherwise lock the prompt
        // forever with InvalidRegion and no visible feedback).
        rebuild.onlyInBuildableRegion = false;
        rebuild.ignoreRegionRestrictIfExpo = false;
        // Stone/iron tech gates make no sense for reverting a finished tower.
        rebuild.requiresStoneTech = false;
        rebuild.requiresIronTech = false;
        // A flat short cooldown: inheriting the tower-upgrade template's long
        // cooldown would keep the prompt locked (InvalidTime) for minutes after
        // every spawn/restore.
        rebuild.cooldown = RebuildCooldown;
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
        if (ballista != null)
        {
            if (ballista.gameObject == null
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

        // FireTower chain: the rebuild prompt must never coexist with the native
        // fuel PayableComponent prompt.  Fuel full hides the native interaction
        // (FireTower.IsLocked always reports NotLocked, so its CanSelect
        // collapses to CanPay = _fireJarsActiveNum < _maxFireJars); with no
        // worker on the crank the state machine is stuck in Reloading — the only
        // 0->1 transition lives in OnJobFinish — so no fire jar is consumed and
        // _projectile stays null while the old root is being destroyed.
        FireTower fireTower = payable.GetComponent<FireTower>();
        if (fireTower == null || fireTower.gameObject == null)
            return Block(payable, BlockReason.BallistaNotActive);
        if (fireTower._fireJarsActiveNum < fireTower._maxFireJars)
            return Block(payable, BlockReason.FireFuelNotFull);
        if (fireTower._currentActors != null && fireTower._currentActors.Count > 0)
            return Block(payable, BlockReason.FireWorkerPresent);
        if (fireTower._projectile != null)
            return Block(payable, BlockReason.FireProjectileInFlight);
        return Allow(payable);
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
            FireTower fireTower = ballista == null
                ? payable.GetComponent<FireTower>() : null;
            if (ballista == null && fireTower == null)
                return Block(payable, BlockReason.BallistaNotActive);

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

            // Only Ballista holds an authority-local bolt to clean up.  The fire
            // path performs no teardown: CanInteract already proved fuel is full,
            // no worker is on the crank and no projectile is held, so nothing can
            // leak when the old root is destroyed.
            Bolt bolt = ballista != null ? ballista._bolt : null;
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
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[SpecialTowerRebuild] prepared instance=" + payable.gameObject.name
                + " parent=" + (payable.transform.parent != null
                    ? payable.transform.parent.name : "<none>"));

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
/// Field diagnostics for the rebuild interaction: after each level load, scan
/// every Ballista and FireTower tower in the scene twice (+10s / +40s) and
/// report whether the marker/payable landed on the live instances and which
/// native lock reason (if any) is suppressing the prompt.  Fire towers also
/// report their fuel jars and worker count.  Identical lines are logged once.
/// </summary>
internal static class SpecialTowerRebuildDiagnostics
{
    private const int MaxScansPerSession = 6;
    private static int _scansDone;
    private static bool _loggedFailure;
    private static readonly HashSet<string> LoggedStates = new();

    public static void Schedule(World world)
    {
        if (!ModConfig.Enabled.Value || world == null) return;
        try
        {
            world.StartCoroutine(ScanRoutine(world).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[SpecialTowerRebuildDiag] schedule failed: " + e);
        }
    }

    private static IEnumerator ScanRoutine(World world)
    {
        yield return new WaitForSeconds(10f);
        ScanOnce(world, "t+10s");
        yield return new WaitForSeconds(30f);
        ScanOnce(world, "t+40s");
    }

    private static void ScanOnce(World world, string tag)
    {
        if (_scansDone >= MaxScansPerSession) return;
        _scansDone++;
        try
        {
            Transform layer = world != null && world.gameObject != null ? world.gameLayer : null;
            if (layer == null) return;

            Ballista[] towers = layer.GetComponentsInChildren<Ballista>(false);
            FireTower[] fireTowers = layer.GetComponentsInChildren<FireTower>(false);
            int registered = CountRegisteredRebuildPayables();
            Log($"[{tag}] sceneBallistas={towers.Length} sceneFireTowers={fireTowers.Length} registeredRebuildPayables={registered}");
            if (towers.Length == 0 && fireTowers.Length == 0) return;

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            for (int i = 0; i < towers.Length; i++)
            {
                Ballista tower = towers[i];
                if (tower == null) continue;
                GameObject go = tower.gameObject;
                if (go == null) continue;

                bool marker = go.GetComponent<SpecialTowerRebuildMarker>() != null;
                PayableUpgrade payable = go.GetComponent<PayableUpgrade>();
                string state = ProbePayableState(go, payable, kingdom);

                // Walk the full ancestor chain: earlier probes only checked
                // the Ballista component's own object and the scene root, and
                // missed the intermediate "Ballista" parent where the marker
                // and payable are now suspected to live.
                StringBuilder chain = new StringBuilder(go.name);
                Transform ancestor = go.transform.parent;
                for (int depth = 0; depth < 4 && ancestor != null; depth++)
                {
                    GameObject ancestorGo = ancestor.gameObject;
                    chain.Append(" <- ").Append(ancestorGo.name)
                        .Append("[m=").Append(
                            ancestorGo.GetComponent<SpecialTowerRebuildMarker>() != null ? 1 : 0)
                        .Append(",p=").Append(
                            ancestorGo.GetComponent<PayableUpgrade>() != null ? 1 : 0)
                        .Append(",P=").Append(
                            ancestorGo.GetComponent<Persistent>() != null ? 1 : 0)
                        .Append(",B=").Append(
                            ancestorGo.GetComponent<Ballista>() != null ? 1 : 0)
                        .Append(']');
                    ancestor = ancestor.parent;
                }
                Log($"[{tag}] chain={chain} selfMarker={marker} {state}");
            }

            for (int i = 0; i < fireTowers.Length; i++)
            {
                FireTower tower = fireTowers[i];
                if (tower == null) continue;
                GameObject go = tower.gameObject;
                if (go == null) continue;

                bool marker = go.GetComponent<SpecialTowerRebuildMarker>() != null;
                PayableUpgrade payable = go.GetComponent<PayableUpgrade>();
                string state = ProbePayableState(go, payable, kingdom);
                int actorCount = 0;
                string jars;
                try
                {
                    if (tower._currentActors != null) actorCount = tower._currentActors.Count;
                    jars = tower._fireJarsActiveNum + "/" + tower._maxFireJars;
                }
                catch
                {
                    jars = "probeFailed";
                }
                Log($"[{tag}] {go.name} active={go.activeInHierarchy} jars={jars}"
                    + " actors=" + actorCount + " marker=" + marker + " " + state);
            }
        }
        catch (Exception e)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[SpecialTowerRebuildDiag] scan failed: " + e);
        }
    }

    private static string ProbePayableState(GameObject go, PayableUpgrade payable,
        Kingdom kingdom)
    {
        if (payable == null) return "payable=missing";
        string reason;
        try
        {
            Player player = kingdom != null
                ? kingdom.GetNearestPlayer(go.transform.position.x)
                : null;
            LockIndicator.LockReason lockReason;
            bool locked = payable.IsLocked(player, out lockReason);
            reason = "locked=" + locked + " reason=" + lockReason
                + " blockPay=" + payable.blockPaymentUpgrade
                + " regionRestrict=" + payable.onlyInBuildableRegion;
        }
        catch (Exception e)
        {
            reason = "probeFailed=" + e.GetType().Name;
        }
        return "payable=present " + reason;
    }

    private static int CountRegisteredRebuildPayables()
    {
        try
        {
            PayableManager manager = Managers.Inst != null ? Managers.Inst.payables : null;
            if (manager == null || manager.AllPayables == null) return -1;
            Il2CppArrayBase<Payable> all = manager.AllPayables;
            int count = 0;
            int limit = Math.Min(all.Length, 2000);
            for (int i = 0; i < limit; i++)
            {
                Payable payable = all[i];
                if (payable == null || payable.gameObject == null) continue;
                if (payable.gameObject.GetComponent<SpecialTowerRebuildMarker>() != null)
                    count++;
            }
            return count;
        }
        catch
        {
            return -1;
        }
    }

    private static void Log(string line)
    {
        if (!LoggedStates.Add(line)) return;
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SpecialTowerRebuildDiag] " + line);
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

[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]
public static class World_SpecialTowerRebuild_Diagnostics_Patch
{
    [HarmonyPostfix]
    private static void Postfix(World __instance)
    {
        SpecialTowerRebuildDiagnostics.Schedule(__instance);
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
