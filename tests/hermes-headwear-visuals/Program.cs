// Direct-link regression for il2cpp/HermesHeadwearVisuals.cs (production file compiled as-is).
// Two harness-level conventions:
//   1. order matters for the shared, process-global sprite cache: the first case documents cold start,
//      every later case runs with the cache built (a miss still refreshes at most once per global 30s cooldown);
//   2. scaled Time.time only ever moves forward, so the production cooldown behaves like it does in game.
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private const string ChildName = "KEM_HermesHeadwear";
    private const string HeadName = "Head";
    private const int BodyLayer = 6, BodySortingLayer = 21, BodySortingOrder = 4;
    private const int MaskLayer = 9, MaskSortingLayer = 7, MaskSortingOrder = 3;
    private const int PrefabLayer = 11, PrefabSortingLayer = 12, PrefabSortingOrder = 6;
    /// <summary>Own child's fixed local position relative to the native Head bone (contract value).</summary>
    private static readonly Vector3 ChildAnchor = new(0f, 0f, -1e-6f);
    /// <summary>Native Head local position measured in the shipped Troll_friendly asset.</summary>
    private static readonly Vector3 HeadLocalPosition = new(-.03125f, .625f, -.00390625f);
    /// <summary>Deliberately distinctive native-mask anchor: copying it into the own child must fail a test.</summary>
    private static readonly Vector3 MaskAnchor = new(.123f, .456f, -.0005f);
    private static readonly Material MaskMaterial = new("MaskMaterial");
    private static readonly Material BodyMaterial = new("BodyMaterial");
    private static readonly Material PrefabMaterial = new("PrefabMaterial");
    private static readonly Color MaskColor = new(.25f, .5f, .75f, 1f);
    private static readonly Color BodyColor = new(.5f, .5f, .5f, 1f);
    private static readonly Color PrefabColor = new(.75f, .25f, .25f, 1f);

    /// <summary>Contract whitelist, pinned literally in code order: 5 world groups x index 0..5, then 7 party hats, then 7 party masks.</summary>
    private static readonly string[] Expected =
    {
        "troll_masks_0", "troll_masks_1", "troll_masks_2", "troll_masks_3", "troll_masks_4", "troll_masks_5",
        "troll_masks_bamboo_0", "troll_masks_bamboo_1", "troll_masks_bamboo_2", "troll_masks_bamboo_3", "troll_masks_bamboo_4", "troll_masks_bamboo_5",
        "troll_masks_deadlands_0", "troll_masks_deadlands_1", "troll_masks_deadlands_2", "troll_masks_deadlands_3", "troll_masks_deadlands_4", "troll_masks_deadlands_5",
        "troll_masks_norselands_0", "troll_masks_norselands_1", "troll_masks_norselands_2", "troll_masks_norselands_3", "troll_masks_norselands_4", "troll_masks_norselands_5",
        "troll_masks_greece_0", "troll_masks_greece_1", "troll_masks_greece_2", "troll_masks_greece_3", "troll_masks_greece_4", "troll_masks_greece_5",
        "troll_hats_party_0", "troll_hats_party_1", "troll_hats_party_2", "troll_hats_party_3", "troll_hats_party_4", "troll_hats_party_5", "troll_hats_party_6",
        "troll_masks_party_0", "troll_masks_party_1", "troll_masks_party_2", "troll_masks_party_3", "troll_masks_party_4", "troll_masks_party_5", "troll_masks_party_6"
    };

    /// <summary>Never electable: world index 6 plus wrong-group / out-of-range names that look plausible.</summary>
    private static readonly string[] Banned =
    {
        "troll_masks_6", "troll_masks_bamboo_6", "troll_masks_deadlands_6", "troll_masks_norselands_6", "troll_masks_greece_6",
        "troll_masks_medieval_0", "troll_masks_bamboo_7", "troll_masks_party_7", "PlayerHorse"
    };

    /// <summary>Handled paths (destroy/restore retries) log on purpose; only unexpected escapes are fatal here.</summary>
    private static readonly string[] ForbiddenLogMarkers =
    {
        "Apply 失败", "Tick 失败", "Clear 失败", "ClearAll 失败", "同步头饰视觉失败", "同步单项失败",
        "处理原生 SpawnMask 通知失败", "释放 owned 状态失败", "销毁自有头饰对象异常",
        "清理队列失败", "清理排队图失败", "清理单项失败"
    };

    private static int passed, failed;
    private static float clock;

    private static void Main()
    {
        // Cache-sensitive cold start first: everything after it assumes a populated whitelist cache.
        Test("cold start: no scan before demand, failed scan retried at most every 30s, cache reused afterwards", CatalogBootstrap);
        Test("codes 0..43 map to the exact 30 world + 14 party whitelist, index 6 and lookalikes never elected", WhitelistMapping);
        Test("choice -1 preserves the native look and needs no resources", PreserveChoiceReleasesOwnedVisual);
        Test("own child hangs on this troll's Head, inherits facing, and never rewrites the animated parent chain", HeadAnchorAndFacingInheritance);
        Test("missing Head fails closed; a rebuilt Head is rebound on the next apply", HeadMissingFailsClosedAndRebindOnChange);
        Test("layout comes from the body (+1 order) while appearance comes from the native mask prefab", BodyLayoutAndNativeAppearance);
        Test("appearance falls back to the live mask when the prefab template is absent", AppearanceFallsBackWhenPrefabMissing);
        Test("bare head uses the body renderer only and never writes a hidden native mask", BareHeadUsesBodyOnly);
        Test("repeat apply creates once and follows a rebuilt mask instance", RepeatApplyAndRebuiltMask);
        Test("tick re-hides native re-enables and adopts a fresh instance baseline", TickRefreshesNativeEnabledOwnership);
        Test("disabled client still renders; inactive/destroyed GO and a reused instance id drop the owned visual (core owns despawn logic)", DisabledClientAndOwnedCleanupOnFlags);
        Test("native SpawnMask notify refreshes the real enabled baseline (false included) on the same instance", NativeMaskSpawnedRefreshesBaseline);
        Test("native SpawnMask notify adopts a replacement renderer and drops the old ownership", NativeMaskSpawnedAdoptsReplacementRenderer);
        Test("native SpawnMask notify without an owned visual is a no-op", NativeMaskSpawnedWithoutOwnedStateIsNoOp);
        Test("a failed restore keeps the ownership and retries instead of dropping it", ClearRetriesOwnedRestoreAfterWriteFailure);
        Test("a foreign-parented mask renderer is never written (no cross-unit restore)", ForeignParentedMaskIsNeverWritten);
        Test("identity getter failures (enabled/parent) keep ownership instead of assuming a swap", GetterFailuresKeepOwnershipForRetry);
        Test("a failed Destroy keeps the tracked root hidden and retries without per-frame churn", FailedDestroyKeepsTrackedRootAndHidesIt);
        Test("a failed SetParent leaves no orphan and the native mask untouched", SetParentFailureLeavesNoOrphan);
        Test("ClearAll isolates per-item cleanup failures", ClearAllIsolatesPerItemFailures);
        Test("native restore retries beyond three attempts until it succeeds", RestoreRetriesBeyondThreeAttemptsUntilSuccess);
        Test("IsApplied reports the displayed choice and drives the resource-loss recovery flow", IsAppliedTracksDisplayedState);
        Test("ClearAll restores every owned visual and stays silent when idle", ClearAllOnWorldChange);
        Test("failed child creation leaves no orphan and recovers on retry", FailedCreationLeavesNoOrphan);
        Test("combined creation and Destroy failures keep an unparented child tracked for cleanup", FailedCreationAndDestroyKeepResponsibility);
        Test("missing or destroyed sprite refreshes at most once per global cooldown and keeps the choice", MissingResourceRefreshIsBoundedAndShared);
        Test("null/inactive inputs fail closed without touching resources or native objects", InvalidAndInactiveInputs);

        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- cases

    private static void CatalogBootstrap()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        int writes = troll._mask.EnabledWrites;
        Eq(0, Resources.AssetScanCalls, "no sprite scan before the first real need");

        Check(!HermesHeadwearVisuals.Apply(troll, 0), "apply fails while the resources are absent");
        Eq(0, ChildCount(troll), "no child on failure");
        Eq(writes, troll._mask.EnabledWrites, "failed apply never hides the native mask");
        Eq(true, troll._mask.enabled, "native look intact");
        Eq(1, Resources.AssetScanCalls, "exactly one scan attempt");

        Check(!HermesHeadwearVisuals.Apply(troll, 0), "retry inside the cooldown still fails");
        Eq(1, Resources.AssetScanCalls, "no rescan inside the cooldown");
        HermesHeadwearVisuals.Tick();
        Check(!HermesHeadwearVisuals.Apply(troll, 0), "tick does not change resource readiness");
        Eq(1, Resources.AssetScanCalls, "still no rescan");

        Time.time += 30f;
        PopulateCatalog();
        Check(HermesHeadwearVisuals.Apply(troll, 0), "apply succeeds once the sprites exist");
        Eq(2, Resources.AssetScanCalls, "second scan only after the cooldown");
        Eq(1, ChildCount(troll), "one owned child");
        Eq(false, troll._mask.enabled, "native mask hidden once the headwear is really shown");
        Check(HermesHeadwearVisuals.Apply(troll, 1), "another choice reuses the cached catalog");
        Eq(2, Resources.AssetScanCalls, "hits never rescan");
        Eq(1, ChildCount(troll), "still exactly one owned child");
    }

    private static void WhitelistMapping()
    {
        Eq(44, Expected.Length, "contract pins 44 choices");
        FriendlyTroll troll = NewTroll(mask: true);
        List<string> used = new();
        Sprite greeceChoice = null;
        for (int choice = 0; choice < Expected.Length; choice++)
        {
            Check(HermesHeadwearVisuals.Apply(troll, choice), "apply choice " + choice);
            SpriteRenderer child = ChildOf(troll);
            Check(child is not null, "child exists for choice " + choice);
            Eq(Expected[choice], child.sprite.name, "whitelist name for choice " + choice);
            Eq(1, ChildCount(troll), "switching choices reuses the single owned child at " + choice);
            if (choice == 26) greeceChoice = child.sprite; // 26 = troll_masks_greece_2 (the duplicated pair)
            used.Add(child.sprite.name);
        }

        Eq(44, used.Distinct().Count(), "44 distinct resources");
        foreach (string banned in Banned) Check(!used.Contains(banned), "never elects " + banned);
        Check(ReferenceEquals(SpritesNamed("troll_masks_greece_2")[0], greeceChoice), "duplicate greece objects keep the first instance");

        int writes = troll._mask.EnabledWrites;
        Check(!HermesHeadwearVisuals.Apply(troll, 44), "code 44 is rejected and takes over nothing");
        Eq(0, ChildCount(troll), "an invalid code drops the owned visual instead of keeping it");
        Eq(true, troll._mask.enabled, "the native look is handed back");
        Eq(writes + 1, troll._mask.EnabledWrites, "only the release write happens");

        FriendlyTroll fresh = NewTroll(mask: true);
        int freshWrites = fresh._mask.EnabledWrites;
        Check(!HermesHeadwearVisuals.Apply(fresh, 44), "code 44 rejected");
        Check(!HermesHeadwearVisuals.Apply(fresh, -2), "a negative code other than -1 is rejected, not preserved");
        Eq(freshWrites, fresh._mask.EnabledWrites, "invalid codes never write the native mask");
        Eq(0, ChildCount(fresh), "invalid codes create nothing");

        FriendlyTroll owned = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(owned, 5), "apply a valid choice first");
        int ownedWrites = owned._mask.EnabledWrites;
        Check(!HermesHeadwearVisuals.Apply(owned, -2), "a negative code other than -1 drops the owned visual and fails");
        Eq(0, ChildCount(owned), "owned visual dropped");
        Eq(true, owned._mask.enabled, "native look handed back");
        Eq(ownedWrites + 1, owned._mask.EnabledWrites, "only the release write happens");
        Check(HermesHeadwearVisuals.Apply(owned, -1), "choice -1 stays the preserve-success path");
        Eq(0, ChildCount(owned), "choice -1 creates nothing");
    }

    private static void PreserveChoiceReleasesOwnedVisual()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 42), "apply party mask 5");
        SpriteRenderer mask = troll._mask;
        int writes = mask.EnabledWrites;
        Eq(false, mask.enabled, "mask suppressed while owned");
        Eq(true, HermesHeadwearVisuals.Apply(troll, -1), "choice -1 preserves the native look");
        Eq(0, ChildCount(troll), "owned child destroyed");
        Eq(true, mask.enabled, "native mask handed back");
        Eq(writes + 1, mask.EnabledWrites, "exactly one restore write");

        int loads = Resources.AssetScanCalls;
        FriendlyTroll bare = NewTroll(mask: false);
        Eq(true, HermesHeadwearVisuals.Apply(bare, -1), "choice -1 without any owned state");
        Eq(0, ChildCount(bare), "nothing created");
        Eq(loads, Resources.AssetScanCalls, "preserve needs no sprite");
    }

    private static void HeadAnchorAndFacingInheritance()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Transform head = HeadOf(troll);
        SpriteRenderer mask = troll._mask;
        int headWrites = ParentWrites(head);
        int maskPositionWrites = mask.transform.LocalPositionWrites;
        Check(HermesHeadwearVisuals.Apply(troll, 18), "apply");
        SpriteRenderer child = ChildOf(troll);
        AssertHeadAnchor(troll, child);
        Eq(MaskAnchor.x, mask.transform.localPosition.x, "the child never copies the native mask anchor x");
        Eq(MaskAnchor.y, mask.transform.localPosition.y, "the child never copies the native mask anchor y");
        Eq(MaskAnchor.z, mask.transform.localPosition.z, "the child never copies the native mask anchor z");
        Eq(maskPositionWrites, mask.transform.LocalPositionWrites, "the native mask is never repositioned");
        Eq(headWrites, ParentWrites(head), "the Head chain is never written by us");

        // Native Head is animated (clip bindings include crc32("Head")): the child must stay local-fixed and inherit.
        head.localPosition = new Vector3(.5f, 1.5f, -.25f);
        head.localScale = new Vector3(1.25f, 1.25f, 1f);
        head.localRotation = new Quaternion();
        troll.transform.localScale = new Vector3(-1f, 1f, 1f);
        int headWritesAfterAnimation = ParentWrites(head);
        int trollScaleWrites = troll.transform.LocalScaleWrites;
        int finds = troll.transform.FindCalls;
        HermesHeadwearVisuals.Tick();
        Eq(ChildAnchor, child.transform.localPosition, "own local position stays fixed while the Head animates");
        Eq(Vector3.one, child.transform.localScale, "own scale stays one while the parent chain flips/scales");
        Eq(Quaternion.identity, child.transform.localRotation, "own rotation stays identity");
        Check(ReferenceEquals(head, child.transform.parent), "own child still hangs on the same Head");
        Eq(headWritesAfterAnimation, ParentWrites(head), "tick never rewrites the Head hierarchy");
        Eq(trollScaleWrites, troll.transform.LocalScaleWrites, "tick never rewrites the unit transform (no double flip)");
        Eq(finds, troll.transform.FindCalls, "tick never calls Transform.Find");

        FriendlyTroll other = NewTroll(mask: true);
        Transform otherHead = HeadOf(other);
        Check(HermesHeadwearVisuals.Apply(other, 19), "apply on a second troll");
        Check(!ReferenceEquals(otherHead, head), "the two trolls have distinct Head nodes");
        Check(ReferenceEquals(otherHead, ChildOf(other).transform.parent), "each troll's child hangs on its own Head");
        Check(ReferenceEquals(head, ChildOf(troll).transform.parent), "the first troll keeps its own Head");
    }

    private static void HeadMissingFailsClosedAndRebindOnChange()
    {
        FriendlyTroll noHead = NewTroll(mask: true, head: false);
        int writes = noHead._mask.EnabledWrites;
        int finds = noHead.transform.FindCalls;
        Check(!HermesHeadwearVisuals.Apply(noHead, 4), "a troll without Head fails closed");
        Eq(0, ChildCount(noHead), "nothing is created without an anchor");
        Eq(writes, noHead._mask.EnabledWrites, "the native mask is untouched when there is no anchor");
        Eq(true, noHead._mask.enabled, "native look intact");
        Eq(finds + 1, noHead.transform.FindCalls, "the lookup is a single direct-child Find");
        HermesHeadwearVisuals.Tick();
        Eq(writes, noHead._mask.EnabledWrites, "tick after a failed apply stays silent");

        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 4), "apply with a Head");
        Transform firstHead = HeadOf(troll);
        SpriteRenderer mask = troll._mask;
        Check(ReferenceEquals(firstHead, ChildOf(troll).transform.parent), "child hangs on the first Head");
        UnityEngine.Object.Destroy(firstHead.gameObject);
        int findsBefore = troll.transform.FindCalls;
        HermesHeadwearVisuals.Tick();
        Eq(0, ChildCount(troll), "a dead Head retires the owned visual");
        Eq(true, mask.enabled, "native mask handed back when the anchor dies");
        Eq(findsBefore, troll.transform.FindCalls, "tick still never calls Transform.Find");

        Transform rebound = AttachHead(troll);
        Check(HermesHeadwearVisuals.Apply(troll, 4), "apply after the Head was rebuilt");
        Check(ReferenceEquals(rebound, ChildOf(troll).transform.parent), "child rebinds onto the new Head");
        Eq(1, ChildCount(troll), "exactly one child after rebind");
    }

    private static void BodyLayoutAndNativeAppearance()
    {
        FriendlyTroll troll = NewTroll(mask: true, prefab: true);
        SpriteRenderer mask = troll._mask;
        SpriteRenderer prefab = troll._maskPrefab;
        int writes = mask.EnabledWrites;
        int prefabWrites = prefab.EnabledWrites;
        int maskPositionWrites = mask.transform.LocalPositionWrites;
        Check(HermesHeadwearVisuals.Apply(troll, 6), "apply");
        SpriteRenderer child = ChildOf(troll);
        Eq("troll_masks_bamboo_0", child.sprite.name, "code 6 is bamboo index 0, never a regular index 6");
        AssertHeadAnchor(troll, child);
        AssertBodyLayout(child);
        Check(ReferenceEquals(PrefabMaterial, child.sharedMaterial), "appearance comes from the native mask prefab");
        Eq(PrefabColor, child.color, "color comes from the native mask prefab");
        Eq(true, child.enabled, "own child enabled even though the template is not");
        Eq(MaskAnchor.x, mask.transform.localPosition.x, "the native mask keeps its own anchor");
        Eq(maskPositionWrites, mask.transform.LocalPositionWrites, "the native mask is never repositioned");
        Eq(prefabWrites, prefab.EnabledWrites, "the asset template is never written");
        Eq(false, mask.enabled, "native mask hidden");
        Eq(writes + 1, mask.EnabledWrites, "exactly one hide write");

        HermesHeadwearVisuals.Clear(troll);
        Eq(0, ChildCount(troll), "child destroyed on clear");
        Eq(true, mask.enabled, "baseline restored");
        Eq(writes + 2, mask.EnabledWrites, "exactly one restore write");
        Eq(1, UnityEngine.Object.GameObjectDestroyCalls, "only the owned child object was destroyed");
        Check(!mask.gameObject.Destroyed, "the native mask object is never destroyed");
        Check(HeadOf(troll) is not null && !HeadOf(troll).gameObject.Destroyed, "the native Head is never destroyed");
    }

    private static void AppearanceFallsBackWhenPrefabMissing()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        SpriteRenderer mask = troll._mask;
        Check(HermesHeadwearVisuals.Apply(troll, 7), "apply with a live mask but no prefab template");
        SpriteRenderer child = ChildOf(troll);
        Eq("troll_masks_bamboo_1", child.sprite.name, "expected resource");
        AssertHeadAnchor(troll, child);
        AssertBodyLayout(child);
        Check(ReferenceEquals(MaskMaterial, child.sharedMaterial), "appearance falls back to the live mask");
        Eq(MaskColor, child.color, "color falls back to the live mask");
        Eq(false, mask.enabled, "native mask hidden");
    }

    private static void BareHeadUsesBodyOnly()
    {
        FriendlyTroll bare = NewTroll(mask: false);
        Check(HermesHeadwearVisuals.Apply(bare, 37), "apply on a bare head (native maskIndex=-1)");
        SpriteRenderer child = ChildOf(bare);
        Eq("troll_masks_party_0", child.sprite.name, "raw whitelist sprite");
        AssertHeadAnchor(bare, child);
        AssertBodyLayout(child);
        Check(ReferenceEquals(BodyMaterial, child.sharedMaterial), "appearance from the body renderer");
        Eq(BodyColor, child.color, "color from the body renderer");
        Eq(true, child.enabled, "owned child enabled");
        Eq(0, UnityEngine.Object.GameObjectDestroyCalls, "no native object was destroyed");

        FriendlyTroll hidden = NewTroll(mask: true, maskEnabled: false);
        int writes = hidden._mask.EnabledWrites;
        Check(HermesHeadwearVisuals.Apply(hidden, 3), "apply while the native mask is already hidden");
        Eq(1, ChildCount(hidden), "headwear shown");
        Eq(writes, hidden._mask.EnabledWrites, "an already hidden native mask is never written");
        HermesHeadwearVisuals.Clear(hidden);
        Eq(0, ChildCount(hidden), "child destroyed on clear");
        Eq(writes, hidden._mask.EnabledWrites, "clear does not write a mask we never hid");
        Eq(false, hidden._mask.enabled, "external (native) value preserved");
    }

    private static void RepeatApplyAndRebuiltMask()
    {
        FriendlyTroll troll = NewTroll(mask: true, prefab: true);
        Check(HermesHeadwearVisuals.Apply(troll, 10), "first apply");
        SpriteRenderer first = ChildOf(troll);
        Eq(1, ChildCount(troll), "one child");

        troll._mask.enabled = true;
        Check(HermesHeadwearVisuals.Apply(troll, 10), "repeat apply after a native re-enable (SpawnMask)");
        Eq(1, ChildCount(troll), "repeat apply never creates a second child");
        Check(ReferenceEquals(first, ChildOf(troll)), "the same owned child is reused");
        Eq(false, troll._mask.enabled, "native re-enable suppressed again");
        AssertBodyLayout(ChildOf(troll));

        SpriteRenderer old = troll._mask;
        int oldWrites = old.EnabledWrites;
        SpriteRenderer rebuilt = AttachMask(troll, enabled: true);
        int writes = rebuilt.EnabledWrites;
        Check(HermesHeadwearVisuals.Apply(troll, 10), "apply after the mask instance was rebuilt");
        Eq(1, ChildCount(troll), "still one child");
        AssertBodyLayout(ChildOf(troll));
        Eq(writes + 1, rebuilt.EnabledWrites, "rebuilt mask hidden once");
        HermesHeadwearVisuals.Clear(troll);
        Eq(true, rebuilt.enabled, "rebuilt mask restored from its own baseline");
        Eq(oldWrites, old.EnabledWrites, "the replaced instance is not written by clear");
    }

    private static void TickRefreshesNativeEnabledOwnership()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 20), "apply");
        SpriteRenderer mask = troll._mask;
        mask.enabled = true;
        int writes = mask.EnabledWrites;
        HermesHeadwearVisuals.Tick();
        Eq(false, mask.enabled, "tick re-hides a native re-enable on the same instance");
        Eq(writes + 1, mask.EnabledWrites, "tick re-hides with exactly one write");
        HermesHeadwearVisuals.Clear(troll);
        Eq(true, mask.enabled, "clear restores the refreshed baseline, never our own hidden false");

        FriendlyTroll second = NewTroll(mask: true, prefab: true);
        Check(HermesHeadwearVisuals.Apply(second, 21), "second apply");
        SpriteRenderer replacement = AttachMask(second, enabled: false);
        int replacementWrites = replacement.EnabledWrites;
        HermesHeadwearVisuals.Tick();
        Eq(1, ChildCount(second), "tick keeps a live owned child");
        Eq(replacementWrites, replacement.EnabledWrites, "an already hidden replacement instance is adopted without a write");
        AssertBodyLayout(ChildOf(second));
        HermesHeadwearVisuals.Clear(second);
        Eq(false, replacement.enabled, "clear keeps the replacement baseline instead of resurrecting our old value");
        Eq(replacementWrites, replacement.EnabledWrites, "no write on a mask we never hid");
    }

    /// <summary>
    /// 覆盖 visual 真正 own 的清理触发面：component disabled（客户端常态）、GO inactive、GO 被销毁、同 InstanceID 换代。
    /// 逻辑死亡/despawn（core 的 ResetAndDespawn / Persistent.OnDisable → Clear）与真实池复用顺序由 root 集成验证，本文件不代其断言。
    /// </summary>
    private static void DisabledClientAndOwnedCleanupOnFlags()
    {
        FriendlyTroll client = NewTroll(mask: true);
        client.enabled = false; // native relays/dummies keep Friendly.enabled = false
        Check(HermesHeadwearVisuals.Apply(client, 31), "a disabled client component still gets the visual");
        Eq(1, ChildCount(client), "child created for the disabled client");

        client.gameObject.activeInHierarchy = false;
        HermesHeadwearVisuals.Tick();
        Eq(0, ChildCount(client), "inactive troll drops the owned visual");
        Eq(true, client._mask.enabled, "native mask handed back on inactivity");

        FriendlyTroll destroyed = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(destroyed, 12), "apply before the GO is destroyed");
        SpriteRenderer deadMask = destroyed._mask;
        int deadWrites = deadMask.EnabledWrites;
        UnityEngine.Object.Destroy(destroyed.gameObject);
        HermesHeadwearVisuals.Tick();
        Eq(deadWrites, deadMask.EnabledWrites, "a destroyed GO is never written again");

        FriendlyTroll first = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(first, 5), "apply on the first generation");
        Eq(1, ChildCount(first), "one child on the first generation");
        SpriteRenderer firstMask = first._mask;
        int firstWrites = firstMask.EnabledWrites;

        FriendlyTroll reused = NewTroll(mask: true); // 同 InstanceID 换代（真实池复用顺序由 root 集成验证）
        reused.gameObject.InstanceId = first.gameObject.InstanceId;
        Check(HermesHeadwearVisuals.Apply(reused, 13), "apply on the reused instance id");
        Eq(1, ChildCount(reused), "exactly one child for the new generation");
        Eq(Expected[13], ChildOf(reused).sprite.name, "the new generation gets its own choice");
        Eq(0, ChildCount(first), "the stale generation child is gone");
        Eq(true, firstMask.enabled, "the stale generation native mask is handed back");
        Eq(firstWrites + 1, firstMask.EnabledWrites, "the stale generation release is written exactly once");
    }

    private static void NativeMaskSpawnedRefreshesBaseline()
    {
        // 原生 repeat Init/ApplyData：maskIndex=-1 → SpawnMask 把同一个 renderer 再写 false（值上不可见，只能靠 core 的通知）
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 8), "apply");
        SpriteRenderer mask = troll._mask;
        Eq(false, mask.enabled, "own visual hides the native mask");
        int writes = mask.EnabledWrites;
        HermesHeadwearVisuals.OnNativeMaskSpawned(troll);
        Eq(writes, mask.EnabledWrites, "adopting the already hidden instance needs no write");
        Check(HermesHeadwearVisuals.Apply(troll, 8), "re-apply after the native respawn");
        Eq(1, ChildCount(troll), "the same owned child is kept");
        HermesHeadwearVisuals.Clear(troll);
        Eq(0, ChildCount(troll), "child destroyed");
        Eq(false, mask.enabled, "clear keeps native's refreshed false instead of resurrecting the mask");
        Eq(writes, mask.EnabledWrites, "clear writes nothing when native already wants it hidden");

        // 原生后来 maskIndex>=0 又写 true：通知刷新 baseline=true 并重新隐藏，释放时恢复 true
        Check(HermesHeadwearVisuals.Apply(troll, 8), "apply again");
        mask.enabled = true;
        HermesHeadwearVisuals.OnNativeMaskSpawned(troll);
        Eq(false, mask.enabled, "the notify re-hides a native re-enable");
        Check(HermesHeadwearVisuals.Apply(troll, 8), "apply after the notify");
        HermesHeadwearVisuals.Clear(troll);
        Eq(true, mask.enabled, "clear restores the refreshed true baseline");
    }

    private static void NativeMaskSpawnedAdoptsReplacementRenderer()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 15), "apply");
        SpriteRenderer old = troll._mask;
        int oldWrites = old.EnabledWrites;
        SpriteRenderer replacement = AttachMask(troll, enabled: false); // 原生这轮换了实例且已把它写 false
        int writes = replacement.EnabledWrites;
        HermesHeadwearVisuals.OnNativeMaskSpawned(troll);
        Eq(writes, replacement.EnabledWrites, "a fresh already-hidden instance is adopted without a write");
        Check(HermesHeadwearVisuals.Apply(troll, 15), "apply after the instance swap");
        Eq(1, ChildCount(troll), "still one child");
        HermesHeadwearVisuals.Clear(troll);
        Eq(false, replacement.enabled, "clear keeps the fresh baseline (native wants it hidden)");
        Eq(writes, replacement.EnabledWrites, "no write on the fresh hidden instance");
        Eq(oldWrites, old.EnabledWrites, "the replaced instance is never written again");
    }

    private static void NativeMaskSpawnedWithoutOwnedStateIsNoOp()
    {
        FriendlyTroll troll = NewTroll(mask: true); // 例如功能关闭 / 从未 Apply 过
        int writes = troll._mask.EnabledWrites;
        int finds = troll.transform.FindCalls;
        HermesHeadwearVisuals.OnNativeMaskSpawned(troll);
        HermesHeadwearVisuals.OnNativeMaskSpawned(null);
        Eq(true, troll._mask.enabled, "without an owned visual the native mask is left alone");
        Eq(writes, troll._mask.EnabledWrites, "no write without an owned visual");
        Eq(0, ChildCount(troll), "a notify never creates a visual");
        Eq(finds, troll.transform.FindCalls, "a notify never looks for a Head");
        Eq(0, UnityEngine.Object.GameObjectDestroyCalls, "a notify destroys nothing");
    }

    private static void ClearRetriesOwnedRestoreAfterWriteFailure()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 23), "apply");
        SpriteRenderer mask = troll._mask;
        int writes = mask.EnabledWrites;
        SpriteRenderer.EnabledWriteFailure = renderer => ReferenceEquals(renderer, mask);
        try { HermesHeadwearVisuals.Clear(troll); }
        finally { SpriteRenderer.EnabledWriteFailure = null; }

        Eq(0, ChildCount(troll), "the owned child is destroyed even when the restore write fails");
        Eq(false, mask.enabled, "the failed restore leaves the mask hidden for now");
        Eq(writes, mask.EnabledWrites, "the failed write never landed");
        Check(LogContains("恢复原生面罩 enabled 失败"), "the failed restore is logged");

        Time.time += 5f;
        HermesHeadwearVisuals.Tick();
        Eq(true, mask.enabled, "the retained ownership retries and restores the baseline");
        Eq(writes + 1, mask.EnabledWrites, "the retry writes exactly once");
    }

    private static void ForeignParentedMaskIsNeverWritten()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 16), "apply");
        SpriteRenderer mask = troll._mask;
        int writes = mask.EnabledWrites;
        GameObject other = new("OtherUnit"); // 该 renderer 已被原生/池转挂到别处：绝不跨单位写回
        mask.transform.SetParent(other.transform, false);
        HermesHeadwearVisuals.Clear(troll);
        Eq(0, ChildCount(troll), "the owned child is still destroyed");
        Eq(writes, mask.EnabledWrites, "a mask that no longer hangs on this troll is never written");
        Eq(false, mask.enabled, "the foreign instance keeps its own value");
        HermesHeadwearVisuals.Tick();
        Eq(writes, mask.EnabledWrites, "tick does not retry a foreign instance either");
    }

    private static void GetterFailuresKeepOwnershipForRetry()
    {
        // enabled getter 抛：读取失败视为未知 → 保留归属、绝不当作“外部已替换”
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 24), "apply");
        SpriteRenderer mask = troll._mask;
        int writes = mask.EnabledWrites;
        SpriteRenderer.EnabledReadFailure = renderer => ReferenceEquals(renderer, mask);
        HermesHeadwearVisuals.Clear(troll);
        Eq(0, ChildCount(troll), "the owned child is destroyed even when the identity read fails");
        Eq(writes, mask.EnabledWrites, "no write happened while the enabled read fails");
        SpriteRenderer.EnabledReadFailure = null;
        Eq(false, mask.enabled, "the native mask stays hidden until the ownership question settles");
        Time.time += 5f;
        HermesHeadwearVisuals.Tick();
        Eq(true, mask.enabled, "the retained ownership retries and restores the baseline");
        Eq(writes + 1, mask.EnabledWrites, "the retry writes exactly once");

        // transform.parent getter 抛：同样保留归属，不当外部接管
        FriendlyTroll second = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(second, 24), "apply on the second troll");
        SpriteRenderer secondMask = second._mask;
        int secondWrites = secondMask.EnabledWrites;
        Transform.ParentReadFailure = transform => ReferenceEquals(transform, secondMask.transform);
        HermesHeadwearVisuals.Clear(second);
        Eq(0, ChildCount(second), "the owned child is still destroyed");
        Eq(secondWrites, secondMask.EnabledWrites, "no write while the parent read fails");
        Transform.ParentReadFailure = null;
        Time.time += 5f;
        HermesHeadwearVisuals.Tick();
        Eq(true, secondMask.enabled, "a parent-read failure also keeps ownership for a later retry");
    }

    private static void FailedDestroyKeepsTrackedRootAndHidesIt()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 26), "apply");
        SpriteRenderer mask = troll._mask;
        UnityEngine.Object.DestroyFailure = target => target is GameObject go && go.name == ChildName;
        HermesHeadwearVisuals.Clear(troll);

        SpriteRenderer child = ChildOf(troll);
        Check(child is not null, "a failed destroy keeps the tracked reference instead of orphaning the object");
        Eq(false, child.gameObject.activeSelf, "a failed destroy still hides the owned object");
        Eq(true, mask.enabled, "the native restore still completes");
        int destroys = UnityEngine.Object.GameObjectDestroyCalls;
        Time.time += 2f;
        HermesHeadwearVisuals.Tick();
        HermesHeadwearVisuals.Tick();
        Check(ChildOf(troll) is not null, "the tracked root survives while the backoff is pending");
        Eq(destroys, UnityEngine.Object.GameObjectDestroyCalls, "no per-frame destroy retry spam");

        UnityEngine.Object.DestroyFailure = null;
        Time.time += 5f;
        HermesHeadwearVisuals.Tick();
        Eq(0, ChildCount(troll), "the retry finishes the destroy");
        int writes = mask.EnabledWrites;
        HermesHeadwearVisuals.ClearAll();
        Eq(writes, mask.EnabledWrites, "the released state is gone and writes nothing further");
    }

    private static void SetParentFailureLeavesNoOrphan()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        int writes = troll._mask.EnabledWrites;
        Transform.SetParentFailure = (transform, parent) => parent is not null; // 只拦生产代码的挂载，不拦销毁时的解除父级
        bool applied;
        try { applied = HermesHeadwearVisuals.Apply(troll, 27); }
        finally { Transform.SetParentFailure = null; }

        Check(!applied, "a failed SetParent fails closed");
        Eq(0, ChildCount(troll), "the partially built child is destroyed, never orphaned");
        Eq(writes, troll._mask.EnabledWrites, "the native mask is never hidden for a visual that failed to attach");
        Eq(true, troll._mask.enabled, "native look intact");
        Eq(1, UnityEngine.Object.GameObjectDestroyCalls, "the partial child object was destroyed exactly once");

        Check(HermesHeadwearVisuals.Apply(troll, 27), "retry succeeds once parenting works");
        Eq(1, ChildCount(troll), "exactly one child after recovery");
        AssertHeadAnchor(troll, ChildOf(troll));
        Eq(writes + 1, troll._mask.EnabledWrites, "the recovered visual hides the native mask once");
    }

    private static void ClearAllIsolatesPerItemFailures()
    {
        FriendlyTroll broken = NewTroll(mask: true);
        FriendlyTroll healthy = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(broken, 28), "apply broken");
        Check(HermesHeadwearVisuals.Apply(healthy, 29), "apply healthy");
        SpriteRenderer brokenMask = broken._mask, healthyMask = healthy._mask;
        int brokenWrites = brokenMask.EnabledWrites, healthyWrites = healthyMask.EnabledWrites;

        SpriteRenderer.EnabledWriteFailure = renderer => ReferenceEquals(renderer, brokenMask);
        try { HermesHeadwearVisuals.ClearAll(); }
        finally { SpriteRenderer.EnabledWriteFailure = null; }

        Eq(0, ChildCount(broken), "the broken item still loses its child in the same pass");
        Eq(0, ChildCount(healthy), "the healthy item is cleaned in the same pass");
        Eq(true, healthyMask.enabled, "the healthy item's native mask is restored");
        Eq(healthyWrites + 1, healthyMask.EnabledWrites, "the healthy item restored exactly once");
        Eq(false, brokenMask.enabled, "the broken item keeps ownership for a later retry");
        Eq(brokenWrites, brokenMask.EnabledWrites, "the failed restore never landed");

        Time.time += 5f;
        HermesHeadwearVisuals.ClearAll();
        Eq(true, brokenMask.enabled, "the kept ownership retries and restores the broken item");
        Eq(brokenWrites + 1, brokenMask.EnabledWrites, "the retry writes exactly once");
    }

    private static void RestoreRetriesBeyondThreeAttemptsUntilSuccess()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(troll, 22), "apply");
        SpriteRenderer mask = troll._mask;
        int writes = mask.EnabledWrites;
        int attempts = 0;
        SpriteRenderer.EnabledWriteFailure = renderer =>
        {
            if (!ReferenceEquals(renderer, mask)) return false;
            attempts++;
            return attempts <= 4; // 前 4 次失败：不允许按次数永久放弃
        };

        try
        {
            for (int round = 0; round < 4; round++)
            {
                HermesHeadwearVisuals.Clear(troll);
                Time.time += 30f;
            }
        }
        finally { SpriteRenderer.EnabledWriteFailure = null; }

        Eq(4, attempts, "four restore attempts were made without giving up");
        Eq(false, mask.enabled, "the mask stays hidden while the restore keeps failing");
        Eq(writes, mask.EnabledWrites, "no failed write landed");

        HermesHeadwearVisuals.Tick();
        Eq(true, mask.enabled, "the fifth attempt succeeds because the responsibility was kept");
        Eq(writes + 1, mask.EnabledWrites, "exactly one successful write");
    }

    private static void IsAppliedTracksDisplayedState()
    {
        Check(!HermesHeadwearVisuals.IsApplied(null, 5), "no troll means not applied");
        FriendlyTroll troll = NewTroll(mask: true);
        Check(!HermesHeadwearVisuals.IsApplied(troll, 5), "nothing tracked yet");
        Check(HermesHeadwearVisuals.Apply(troll, 5), "apply");
        SpriteRenderer child = ChildOf(troll);
        SpriteRenderer mask = troll._mask;
        int writes = mask.EnabledWrites;
        int children = ChildCount(troll);
        Check(HermesHeadwearVisuals.IsApplied(troll, 5), "the displayed choice reports applied");
        Check(!HermesHeadwearVisuals.IsApplied(troll, 6), "a different choice is not applied");
        Eq(children, ChildCount(troll), "IsApplied is read-only over the tracked visual");
        Eq(writes, mask.EnabledWrites, "IsApplied never writes the native mask");

        // 显示成功后资源被销毁：core Sweep 需要看到 false 才能按同一 receipt 重新排队 Apply
        Sprite resource = child.sprite;
        resource.Destroyed = true;
        Check(!HermesHeadwearVisuals.IsApplied(troll, 5), "a destroyed sprite resource reports not applied");
        HermesHeadwearVisuals.Tick();
        Eq(0, ChildCount(troll), "tick clears the child whose resource died");
        Eq(true, mask.enabled, "the native mask is handed back meanwhile");
        Check(!HermesHeadwearVisuals.IsApplied(troll, 5), "a cleared state is not applied");
        int restoredWrites = mask.EnabledWrites;

        // 资源补齐后原 choice 直接恢复
        Resources.All[Resources.All.IndexOf(resource)] = new Sprite(resource.name);
        Check(HermesHeadwearVisuals.Apply(troll, 5), "the same choice re-applies once the resource is back");
        Eq("troll_masks_5", ChildOf(troll).sprite.name, "the original choice is kept");
        Eq(restoredWrites + 1, mask.EnabledWrites, "the restored visual hides the native mask once");
        Check(HermesHeadwearVisuals.IsApplied(troll, 5), "the restored visual reports applied again");

        // 自有 root 被外部销毁：同样必须立刻报 false，并由下一次 Apply 恢复
        UnityEngine.Object.Destroy(ChildOf(troll).gameObject);
        Check(!HermesHeadwearVisuals.IsApplied(troll, 5), "a destroyed owned root reports not applied");
        HermesHeadwearVisuals.Tick();
        Eq(true, mask.enabled, "the native mask is handed back after the root died");
        Check(HermesHeadwearVisuals.Apply(troll, 5), "apply recovers after the owned root was destroyed");
        Eq(1, ChildCount(troll), "exactly one child after the root-loss recovery");
        Check(HermesHeadwearVisuals.IsApplied(troll, 5), "the recovered visual reports applied");
    }

    private static void ClearAllOnWorldChange()
    {
        FriendlyTroll a = NewTroll(mask: true);
        FriendlyTroll b = NewTroll(mask: true);
        Check(HermesHeadwearVisuals.Apply(a, 7), "apply a");
        Check(HermesHeadwearVisuals.Apply(b, 40), "apply b");
        Eq(false, a._mask.enabled, "a suppressed");
        Eq(false, b._mask.enabled, "b suppressed");
        int writesA = a._mask.EnabledWrites, writesB = b._mask.EnabledWrites;

        HermesHeadwearVisuals.ClearAll();
        Eq(0, ChildCount(a), "a child destroyed");
        Eq(0, ChildCount(b), "b child destroyed");
        Eq(true, a._mask.enabled, "a restored");
        Eq(true, b._mask.enabled, "b restored");
        Eq(writesA + 1, a._mask.EnabledWrites, "a restored exactly once");
        Eq(writesB + 1, b._mask.EnabledWrites, "b restored exactly once");
        Check(HeadOf(a) is not null && HeadOf(b) is not null, "the native Heads survive ClearAll");

        HermesHeadwearVisuals.ClearAll();
        HermesHeadwearVisuals.Tick();
        Eq(writesA + 1, a._mask.EnabledWrites, "idle ClearAll/Tick write nothing for a");
        Eq(writesB + 1, b._mask.EnabledWrites, "idle ClearAll/Tick write nothing for b");
    }

    private static void FailedCreationLeavesNoOrphan()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        int writes = troll._mask.EnabledWrites;
        GameObject.AddComponentFailure = type => type == typeof(SpriteRenderer);
        bool applied;
        try { applied = HermesHeadwearVisuals.Apply(troll, 9); }
        finally { GameObject.AddComponentFailure = null; }

        Check(!applied, "a failed child creation fails closed");
        Eq(0, ChildCount(troll), "no orphan child after a failed creation");
        Eq(writes, troll._mask.EnabledWrites, "the native mask is never hidden for a visual that does not exist");
        Eq(true, troll._mask.enabled, "native look intact");

        Check(HermesHeadwearVisuals.Apply(troll, 9), "retry succeeds");
        Eq(1, ChildCount(troll), "exactly one child after recovery");
        Eq("troll_masks_bamboo_3", ChildOf(troll).sprite.name, "expected resource after recovery");
        Eq(writes + 1, troll._mask.EnabledWrites, "the recovered visual hides the native mask once");
        HermesHeadwearVisuals.Tick();
        Eq(1, ChildCount(troll), "tick keeps the recovered child");
    }

    private static void FailedCreationAndDestroyKeepResponsibility()
    {
        FriendlyTroll troll = NewTroll(mask: true);
        int writes = troll._mask.EnabledWrites;
        GameObject failedRoot = null;
        GameObject.AddComponentFailure = type => type == typeof(SpriteRenderer);
        UnityEngine.Object.DestroyFailure = target =>
        {
            if (target is GameObject go && go.name == ChildName) { failedRoot = go; return true; }
            return false;
        };
        try { Check(!HermesHeadwearVisuals.Apply(troll, 12), "dual failure reports unavailable"); }
        finally { GameObject.AddComponentFailure = null; UnityEngine.Object.DestroyFailure = null; }
        Check(failedRoot is not null, "failed unparented root remains identifiable");
        Check(!failedRoot.Destroyed && !failedRoot.activeInHierarchy, "failed root is hidden until retry");
        Eq(writes, troll._mask.EnabledWrites, "native mask was never taken over");
        Time.time += 35f;
        HermesHeadwearVisuals.Tick();
        Check(failedRoot.Destroyed, "kept cleanup responsibility eventually destroys the root");
        Check(HermesHeadwearVisuals.Apply(troll, 12), "same choice can be rendered after recovery");
    }

    private static void MissingResourceRefreshIsBoundedAndShared()
    {
        Sprite missing = SpritesNamed("troll_masks_party_3")[0];
        missing.Destroyed = true; // Unity: the referenced asset was unloaded
        FriendlyTroll troll = NewTroll(mask: true);
        int writes = troll._mask.EnabledWrites;
        int loads = Resources.AssetScanCalls;

        Check(!HermesHeadwearVisuals.Apply(troll, 40), "an unavailable sprite fails closed");
        Eq(loads + 1, Resources.AssetScanCalls, "a miss triggers exactly one bounded refresh scan");
        Eq(0, ChildCount(troll), "no child while the resource is unavailable");
        Eq(true, troll._mask.enabled, "native look untouched");
        Eq(writes, troll._mask.EnabledWrites, "no native mask write without a visual");

        Check(!HermesHeadwearVisuals.Apply(troll, 40), "retry inside the cooldown fails without rescanning");
        Eq(loads + 1, Resources.AssetScanCalls, "no rescan inside the cooldown");
        FriendlyTroll other = NewTroll(mask: true);
        Check(!HermesHeadwearVisuals.Apply(other, 40), "a second object missing the same resource fails closed");
        Eq(loads + 1, Resources.AssetScanCalls, "objects missing the same resource share one LoadAll");
        Eq(1, KingdomEnhancedPlugin.Instance.LogSource.Warnings.Count(w => w.Contains("白名单贴图缺失")), "logged once for the same missing name");

        Time.time += 30f;
        Sprite replacement = new("troll_masks_party_3");
        Resources.All[Resources.All.IndexOf(missing)] = replacement;
        Check(HermesHeadwearVisuals.Apply(troll, 40), "the unchanged choice succeeds after the bounded refresh");
        Eq(loads + 2, Resources.AssetScanCalls, "exactly one more refresh scan");
        Check(ReferenceEquals(replacement, ChildOf(troll).sprite), "the refreshed sprite instance is used");
        Eq("troll_masks_party_3", ChildOf(troll).sprite.name, "the originally chosen resource is kept");
        Eq(writes + 1, troll._mask.EnabledWrites, "hide happens only for a real visual");
    }

    private static void InvalidAndInactiveInputs()
    {
        int loads = Resources.AssetScanCalls;
        Check(!HermesHeadwearVisuals.Apply(null, 5), "null troll fails closed");
        FriendlyTroll troll = NewTroll(mask: true);
        troll.gameObject.activeInHierarchy = false;
        int writes = troll._mask.EnabledWrites;
        Check(!HermesHeadwearVisuals.Apply(troll, 5), "inactive troll fails closed");
        Eq(0, ChildCount(troll), "nothing created for an inactive troll");
        Eq(writes, troll._mask.EnabledWrites, "no native write for an inactive troll");
        Eq(loads, Resources.AssetScanCalls, "invalid/inactive input never scans resources");

        HermesHeadwearVisuals.Clear(null);
        HermesHeadwearVisuals.Tick();
        HermesHeadwearVisuals.ClearAll();
        Eq(loads, Resources.AssetScanCalls, "idle api never loads sprites");
        Eq(writes, troll._mask.EnabledWrites, "idle api never writes the native mask");
    }

    // ---------------------------------------------------------------- harness

    private static void Test(string name, Action action)
    {
        Reset();
        try
        {
            action();
            foreach (string warning in KingdomEnhancedPlugin.Instance.LogSource.Warnings)
                Check(!ForbiddenLogMarkers.Any(warning.Contains), "unexpected production exception: " + warning);
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message);
        }
    }

    private static void Reset()
    {
        GameObject.AddComponentFailure = null;
        SpriteRenderer.EnabledWriteFailure = null;
        SpriteRenderer.EnabledReadFailure = null;
        Transform.ParentReadFailure = null;
        Transform.SetParentFailure = null;
        UnityEngine.Object.DestroyFailure = null;
        HermesHeadwearVisuals.ClearAll();
        UnityEngine.Object.GameObjectDestroyCalls = 0;
        KingdomEnhancedPlugin.Instance = new();
        clock += 60f; // monotonic scaled time: every production cooldown has long expired between cases
        Time.time = clock;
    }

    private static void Eq<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static bool LogContains(string fragment) =>
        KingdomEnhancedPlugin.Instance.LogSource.Warnings.Any(warning => warning.Contains(fragment));

    private static int ParentWrites(Transform transform) =>
        transform.ParentWrites + transform.LocalPositionWrites + transform.LocalRotationWrites + transform.LocalScaleWrites;

    private static void AssertHeadAnchor(FriendlyTroll troll, SpriteRenderer child)
    {
        Check(ReferenceEquals(HeadOf(troll), child.transform.parent), "own child hangs on this troll's Head");
        Eq(ChildAnchor, child.transform.localPosition, "own local position is fixed");
        Eq(Vector3.one, child.transform.localScale, "own local scale is one");
        Eq(Quaternion.identity, child.transform.localRotation, "own local rotation is identity");
    }

    private static void AssertBodyLayout(SpriteRenderer child)
    {
        Eq(BodyLayer, child.gameObject.layer, "GO layer from the body renderer");
        Eq(BodySortingLayer, child.sortingLayerID, "sorting layer from the body renderer");
        Eq(BodySortingOrder + 1, child.sortingOrder, "sorting order = body order + 1 so the headwear is never occluded");
        Eq(true, child.flipX, "flipX follows the body in the same parent tree (no second flip)");
        Eq(true, child.flipY, "flipY follows the body");
    }

    private static FriendlyTroll NewTroll(bool mask, bool maskEnabled = true, bool head = true, bool prefab = false)
    {
        GameObject go = new("FriendlyTroll");
        FriendlyTroll troll = go.AddComponent<FriendlyTroll>();
        SpriteRenderer body = go.AddComponent<SpriteRenderer>(); // body renderer on the root, as on the native prefab
        body.gameObject.layer = BodyLayer;
        body.sortingLayerID = BodySortingLayer;
        body.sortingOrder = BodySortingOrder;
        body.flipX = true;
        body.flipY = true;
        body.sharedMaterial = BodyMaterial;
        body.color = BodyColor;
        if (head) AttachHead(troll);
        if (prefab) troll._maskPrefab = NewMaskPrefab();
        if (mask) AttachMask(troll, maskEnabled);
        return troll;
    }

    private static Transform AttachHead(FriendlyTroll troll)
    {
        GameObject go = new(HeadName);
        go.transform.SetParent(troll.transform, false);
        go.transform.localPosition = HeadLocalPosition;
        return go.transform;
    }

    private static SpriteRenderer NewMaskPrefab()
    {
        GameObject go = new("TrollMask");
        go.layer = PrefabLayer;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.enabled = false;
        renderer.sortingLayerID = PrefabSortingLayer;
        renderer.sortingOrder = PrefabSortingOrder;
        renderer.flipX = false;
        renderer.flipY = false;
        renderer.sharedMaterial = PrefabMaterial;
        renderer.color = PrefabColor;
        return renderer;
    }

    private static SpriteRenderer AttachMask(FriendlyTroll troll, bool enabled = true)
    {
        GameObject go = new("TrollMaskInstance");
        go.layer = MaskLayer;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.enabled = enabled;
        renderer.sortingLayerID = MaskSortingLayer;
        renderer.sortingOrder = MaskSortingOrder;
        renderer.flipX = true;
        renderer.sharedMaterial = MaskMaterial;
        renderer.color = MaskColor;
        go.transform.SetParent(troll.transform, false);
        go.transform.localPosition = MaskAnchor;
        troll._mask = renderer;
        return renderer;
    }

    private static Transform HeadOf(FriendlyTroll troll)
    {
        foreach (Transform child in troll.transform.Children)
            if (child.gameObject.name == HeadName) return child;
        return null;
    }

    private static SpriteRenderer ChildOf(FriendlyTroll troll)
    {
        Transform found = FindOwnChild(troll.transform);
        return found?.gameObject.GetComponent<SpriteRenderer>();
    }

    private static int ChildCount(FriendlyTroll troll) => OwnChildCount(troll.transform);

    /// <summary>Own child hangs on the Head, so lookups walk the subtree: a child left anywhere else is still found (and fails the anchor checks).</summary>
    private static Transform FindOwnChild(Transform parent)
    {
        foreach (Transform child in parent.Children)
        {
            if (child.gameObject.name == ChildName) return child;
            Transform nested = FindOwnChild(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static int OwnChildCount(Transform parent)
    {
        int count = 0;
        foreach (Transform child in parent.Children)
        {
            if (child.gameObject.name == ChildName) count++;
            count += OwnChildCount(child);
        }
        return count;
    }

    private static List<Sprite> SpritesNamed(string name)
    {
        List<Sprite> matches = new();
        foreach (Sprite sprite in Resources.All) if (sprite.name == name) matches.Add(sprite);
        return matches;
    }

    /// <summary>Everything the shipped resources contain that the visual layer may look at, plus lookalikes that must be ignored.</summary>
    private static void PopulateCatalog()
    {
        Resources.All.Clear();
        string[] prefixes = { "troll_masks_", "troll_masks_bamboo_", "troll_masks_deadlands_", "troll_masks_norselands_", "troll_masks_greece_" };
        foreach (string prefix in prefixes)
            for (int index = 0; index <= 6; index++) Resources.All.Add(new Sprite(prefix + index));
        for (int index = 0; index <= 6; index++) Resources.All.Add(new Sprite("troll_hats_party_" + index));
        for (int index = 0; index <= 6; index++) Resources.All.Add(new Sprite("troll_masks_party_" + index));
        foreach (string banned in Banned) Resources.All.Add(new Sprite(banned));
        Resources.All.Add(new Sprite("troll_masks_greece_2")); // second object for the same greece sprite
    }
}
