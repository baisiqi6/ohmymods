using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    /// <summary>运行时身份状态机用例：生命周期、复用、均衡、迁移、client 拒分配、容量与清理。</summary>
    internal static class IdentityTests
    {
        internal static void Run()
        {
            NewLifeClearsReceipt();
            RecycledInstanceIdDoesNotInheritIdentity();
            DuplicatePromotionKeepsGuidAndStyle();
            ClientRefusesAllocationAndRespectsLife();
            BalancedRecruitsSpreadAcrossBatch();
            PrimeExistingPinsLegacyBeforeBalance();
            AssetMissingDefersWithoutRebranding();
            FreezeExistingLiveStyle();
            MigrationHashUsedOnceThenFrozen();
            SquireNeverEntersRegistry();
            PollCleansOldWorldAndDeadAndDefers();
            CapacityRefusesWithoutDroppingReceipts();
            PrimeCallbackRecursionRefused();
            WorldMembershipIsVerified();
        }

        private static void WorldMembershipIsVerified()
        {
            Case.Run("resolve_requires_verified_current_world_membership", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit detached = NativeSim.NewDetachedKnight(31001);
                    KnightIdentityRuntime.OnEnable(detached.Knight);
                    Check.False(KnightIdentityRuntime.TryResolve(detached.Knight, -1, 3u, Styles.All, out _),
                        "unit outside world.gameLayer cannot resolve");
                    Check.False(KnightIdentityRuntime.TryResolve(detached.Knight, 2, 3u, Styles.All, out _),
                        "still refused while detached");

                    NativeSim.PlaceUnit(detached, true); // 层级补齐
                    Check.True(KnightIdentityRuntime.TryResolve(detached.Knight, 2, 3u, Styles.All, out int style), "resolves after entering the world");
                    Check.Equal(2, style, "frozen live style");

                    KnightUnit stale = NativeSim.NewKnight(31002);
                    KnightIdentityRuntime.OnEnable(stale.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(stale.Knight, 1, 3u, Styles.All, out _), "resolves in world A");
                    NativeSim.ResetWorld(0x8888); // 换 world（新 scene）
                    Check.False(KnightIdentityRuntime.TryResolve(stale.Knight, 1, 3u, Styles.All, out _), "entry from the old world cannot resolve");
                    KnightIdentityRuntime.Poll();
                    Check.Equal(0L, KnightIdentityRuntime.GetLifetime(stale.Knight), "old-world entry cleaned by Poll");
                }
            });
        }

        private static void NewLifeClearsReceipt()
        {
            Case.Run("lifecycle_new_life_clears_receipt_and_grows_lifetime", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(1001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    long firstLife = KnightIdentityRuntime.GetLifetime(unit.Knight);
                    Check.True(firstLife > 0, "first OnEnable assigns a life");
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 3u, Styles.All, out int style), "host resolves");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt first), "receipt exists");
                    Check.Equal(style, first.Style, "receipt style matches resolve");

                    KnightIdentityRuntime.OnEnable(unit.Knight); // 同一对象再次启用 = 新生命
                    long secondLife = KnightIdentityRuntime.GetLifetime(unit.Knight);
                    Check.True(secondLife > firstLife, "new OnEnable gets a fresh global life");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "new life clears the old receipt");
                }
            });
        }

        private static void RecycledInstanceIdDoesNotInheritIdentity()
        {
            Case.Run("recycled_instance_id_and_pointer_do_not_inherit_identity", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit first = NativeSim.NewKnight(2001);
                    KnightIdentityRuntime.OnEnable(first.Knight);
                    long firstLife = KnightIdentityRuntime.GetLifetime(first.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(first.Knight, 2, 3u, Styles.All, out _), "first object resolves");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(first.Knight, out KnightIdentityReceipt original), "first receipt");

                    // Unity 复用 instanceID 给新对象：GOID 相同、Pointer 不同。
                    KnightUnit recycled = NativeSim.NewKnight(2001);
                    recycled.Go.Pointer = new IntPtr(2001 * 64 + 4096);
                    KnightIdentityRuntime.OnEnable(recycled.Knight);

                    long recycledLife = KnightIdentityRuntime.GetLifetime(recycled.Knight);
                    Check.True(recycledLife > 0, "recycled object has its own life");
                    Check.NotEqual(firstLife, recycledLife, "life counter is never reused after instanceID recycling");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(recycled.Knight, out _), "recycled object inherits nothing");
                    Check.Equal(1, KnightIdentityRuntime.TrackedCount, "recycled GOID drops the stale entry");
                    Check.True(KnightIdentityRuntime.TryResolve(recycled.Knight, -1, 9u, Styles.All, out _), "recycled object resolves fresh");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(recycled.Knight, out KnightIdentityReceipt fresh), "fresh receipt");
                    Check.NotEqual(original.Id, fresh.Id, "recycled object gets a different Guid");
                }
            });
        }

        private static void DuplicatePromotionKeepsGuidAndStyle()
        {
            Case.Run("duplicate_promotion_keeps_guid_and_style", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(3001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    KnightIdentityRuntime.MarkPromoted(unit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 3u, Styles.All, out int style), "recruit resolves");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt first), "receipt");

                    KnightIdentityRuntime.MarkPromoted(unit.Knight); // 重复 promotion
                    KnightIdentityRuntime.MarkPromoted(unit.Knight);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt again), "receipt survives repeat promotion");
                    Check.Same(first.Id, again.Id, "repeat promotion does not rebuild the Guid");
                    Check.Equal(first.Style, again.Style, "repeat promotion does not change style");
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, 4, 77u, Styles.All, out int resolvedAgain), "resolve again");
                    Check.Equal(style, resolvedAgain, "resolve returns the frozen style");
                }
            });
        }

        private static void ClientRefusesAllocationAndRespectsLife()
        {
            Case.Run("client_never_allocates_and_accepts_only_matching_life", () =>
            {
                using (Fixture f = new Fixture())
                {
                    NetworkBigBoss.HasWorldAuth = false; // client
                    KnightUnit unit = NativeSim.NewKnight(4001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    long life = KnightIdentityRuntime.GetLifetime(unit.Knight);
                    Check.True(life > 0, "client tracks life");

                    Check.False(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 5u, Styles.All, out int style), "client cannot create a Guid");
                    Check.Equal(-1, style, "no style while waiting");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "client has no receipt yet");

                    KnightIdentityReceipt receipt = new KnightIdentityReceipt(Guid.NewGuid(), 3);
                    Check.False(KnightIdentityRuntime.ApplyHostReceipt(unit.Knight, life + 1, receipt), "wrong life is rejected");
                    Check.True(KnightIdentityRuntime.ApplyHostReceipt(unit.Knight, life, receipt), "matching life accepted");
                    Check.True(KnightIdentityRuntime.ApplyHostReceipt(unit.Knight, life, receipt), "repeat is idempotent");
                    Check.False(KnightIdentityRuntime.ApplyHostReceipt(unit.Knight, life, new KnightIdentityReceipt(Guid.NewGuid(), 0)),
                        "different Guid for the same life must not overwrite");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt stored) && stored.Equals(receipt), "accepted receipt intact");

                    Check.False(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 5u, Styles.Of(), out int fixedStyle), "style missing from the asset pool => wait");
                    Check.Equal(3, fixedStyle, "frozen style is reported but never re-rolled");
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 5u, Styles.All, out int appliedStyle), "resolves once the asset is available");
                    Check.Equal(3, appliedStyle, "receipt style fixed");

                    IslandSaveData island = new IslandSaveData { land = 1 };
                    NativeSim.RunSave(island, 0, 1, 0, new List<KnightUnit> { unit });
                    Check.False(f.SidecarExists, "client never writes the sidecar");

                    NetworkBigBoss.HasWorldAuth = true; // 主机不接受 client 判定
                    KnightUnit hostUnit = NativeSim.NewKnight(4901);
                    KnightIdentityRuntime.OnEnable(hostUnit.Knight);
                    Check.False(KnightIdentityRuntime.ApplyHostReceipt(hostUnit.Knight, KnightIdentityRuntime.GetLifetime(hostUnit.Knight),
                        new KnightIdentityReceipt(Guid.NewGuid(), 1)), "host refuses host-receipt application");
                }
            });
        }

        private static void BalancedRecruitsSpreadAcrossBatch()
        {
            Case.Run("balanced_recruits_spread_styles_across_batch", () =>
            {
                using (Fixture f = new Fixture())
                {
                    int[] counts = new int[KnightIdentityReceipt.StyleCount];
                    for (int i = 0; i < 10; i++)
                    {
                        KnightUnit recruit = NativeSim.NewKnight(5000 + i);
                        KnightIdentityRuntime.OnEnable(recruit.Knight);
                        KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                        Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, -1, 3u, Styles.All, out int style), "recruit " + i + " resolves");
                        Check.True(KnightIdentityReceipt.IsValidStyle(style), "style in range");
                        counts[style]++;
                    }
                    int min = int.MaxValue;
                    int max = 0;
                    for (int i = 0; i < counts.Length; i++)
                    {
                        if (counts[i] < min) min = counts[i];
                        if (counts[i] > max) max = counts[i];
                    }
                    Check.True(max - min <= 1, "batch stays balanced [counts=" + string.Join(",", counts) + "]");
                }
            });
        }

        private static void PrimeExistingPinsLegacyBeforeBalance()
        {
            Case.Run("prime_existing_pins_legacy_before_balance", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit a = NativeSim.NewKnight(6001);
                    KnightUnit b = NativeSim.NewKnight(6002);
                    KnightUnit c = NativeSim.NewKnight(6003);
                    KnightIdentityRuntime.OnEnable(a.Knight);
                    KnightIdentityRuntime.OnEnable(b.Knight);
                    KnightIdentityRuntime.OnEnable(c.Knight);

                    KnightIdentityRuntime.PrimeExisting(new[] { a.Knight, b.Knight, c.Knight }, knight => 1);
                    CheckPinned(a, 1);
                    CheckPinned(b, 1);
                    CheckPinned(c, 1);

                    KnightUnit recruit = NativeSim.NewKnight(6010);
                    KnightIdentityRuntime.OnEnable(recruit.Knight);
                    KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, -1, 3u, Styles.All, out int style), "recruit resolves");
                    Check.NotEqual(1, style, "balance avoids the style already taken by migrated veterans");

                    // MarkedNew 不参与迁移
                    KnightUnit marked = NativeSim.NewKnight(6020);
                    KnightIdentityRuntime.OnEnable(marked.Knight);
                    KnightIdentityRuntime.MarkPromoted(marked.Knight);
                    KnightIdentityRuntime.PrimeExisting(new[] { marked.Knight }, knight => 2);
                    Check.False(KnightIdentityRuntime.TryGetReceipt(marked.Knight, out _), "marked recruit is skipped by PrimeExisting");
                }
            });
        }

        private static void AssetMissingDefersWithoutRebranding()
        {
            Case.Run("asset_missing_defers_without_rebranding_or_reroll", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(7001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    KnightIdentityRuntime.MarkPromoted(unit.Knight);
                    Check.False(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 3u, Styles.Of(), out _), "empty asset pool defers");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out _), "no identity while assets are missing");

                    KnightIdentityRuntime.PrimeExisting(new[] { unit.Knight }, knight => -1);
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 3u, Styles.All, out int first), "resolves once assets return");
                    KnightIdentityRuntime.PrimeExisting(new[] { unit.Knight }, knight => 4);
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt");
                    Check.Equal(first, receipt.Style, "pinned style unchanged by later priming");
                }
            });
        }

        private static void FreezeExistingLiveStyle()
        {
            Case.Run("existing_live_style_is_frozen_and_not_remapped", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(8001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, 3, 9u, Styles.All, out int style), "freeze live style");
                    Check.Equal(3, style, "live style kept");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "identity created for the frozen style");
                    Check.Equal(3, receipt.Style, "receipt style frozen");

                    Check.False(KnightIdentityRuntime.TryResolve(unit.Knight, 3, 1u, Styles.Of(0, 1), out int again), "frozen style missing from the pool => wait");
                    Check.Equal(3, again, "no remap while a receipt exists");

                    KnightUnit pending = NativeSim.NewKnight(8002);
                    KnightIdentityRuntime.OnEnable(pending.Knight);
                    Check.False(KnightIdentityRuntime.TryResolve(pending.Knight, 3, 1u, Styles.Of(0, 1), out _), "missing asset for the live style defers");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(pending.Knight, out _), "no identity created while waiting");
                }
            });
        }

        private static void MigrationHashUsedOnceThenFrozen()
        {
            Case.Run("legacy_migration_hash_applied_once_then_frozen", () =>
            {
                using (Fixture f = new Fixture())
                {
                    IReadOnlyList<int> pool = Styles.Of(0, 1, 2);
                    KnightUnit unit = NativeSim.NewKnight(9001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 7u, pool, out int style), "legacy migration");
                    Check.Equal(1, style, "7 % 3 => pool[1]");
                    Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, 8u, pool, out int second), "second call");
                    Check.Equal(1, second, "migration is one-shot: the receipt fixes the style");
                }
            });
        }

        private static void SquireNeverEntersRegistry()
        {
            Case.Run("squire_gets_a_life_but_never_an_identity", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit squire = NativeSim.NewKnight(9501, "Squire", "Squire(Clone)");
                    KnightIdentityRuntime.OnEnable(squire.Knight);
                    Check.True(KnightIdentityRuntime.GetLifetime(squire.Knight) > 0, "squire gets a life (promotion needs the prior-life evidence)");
                    KnightIdentityRuntime.MarkPromoted(squire.Knight);
                    Check.False(KnightIdentityRuntime.TryResolve(squire.Knight, -1, 1u, Styles.All, out _), "squire cannot resolve");
                    Check.False(KnightIdentityRuntime.BindLoadedReceipt(squire.Knight, new KnightIdentityReceipt(Guid.NewGuid(), 1)), "squire cannot bind a receipt");
                    Check.Equal(0, KnightIdentityRuntime.ReceiptCount, "squire never holds an identity");

                    KnightUnit recruit = NativeSim.NewKnight(9502);
                    KnightIdentityRuntime.OnEnable(recruit.Knight);
                    KnightIdentityRuntime.MarkPromoted(recruit.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(recruit.Knight, -1, 1u, Styles.All, out int style), "real knight resolves normally");
                    Check.True(KnightIdentityReceipt.IsValidStyle(style), "valid style");
                }
            });
        }

        private static void PollCleansOldWorldAndDeadAndDefers()
        {
            Case.Run("poll_cleans_old_world_and_dead_and_defers_without_world", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit alive = NativeSim.NewKnight(10001);
                    KnightUnit dead = NativeSim.NewKnight(10002);
                    KnightUnit oldWorld = NativeSim.NewKnight(10003);
                    KnightIdentityRuntime.OnEnable(alive.Knight);
                    KnightIdentityRuntime.OnEnable(dead.Knight);
                    Check.True(KnightIdentityRuntime.TryResolve(alive.Knight, 1, 1u, Styles.All, out _), "alive resolves");
                    Check.True(KnightIdentityRuntime.TryResolve(dead.Knight, 2, 1u, Styles.All, out _), "dead resolves first");
                    dead.DestroyForTests();
                    oldWorld.Go.DestroyForTests();

                    KnightIdentityRuntime.Poll(); // 无骑士销毁时：dead 被回收，alive 保留
                    Check.True(KnightIdentityRuntime.TryGetReceipt(alive.Knight, out _), "alive entry kept");
                    Check.Equal(0L, KnightIdentityRuntime.GetLifetime(dead.Knight), "destroyed entry cleaned");

                    Managers.Inst = null; // world 不可解析：延后，不清状态
                    KnightIdentityRuntime.Poll();
                    Check.True(KnightIdentityRuntime.TryGetReceipt(alive.Knight, out _), "unresolvable world defers cleanup");

                    NativeSim.ResetWorld(0x9999); // 换 world
                    KnightIdentityRuntime.Poll();
                    Check.Equal(0L, KnightIdentityRuntime.GetLifetime(alive.Knight), "entry from the previous world is dropped");
                }
            });
        }

        private static void CapacityRefusesWithoutDroppingReceipts()
        {
            Case.Run("capacity_refuses_new_identity_without_dropping_live_receipts", () =>
            {
                using (Fixture f = new Fixture())
                {
                    int capacity = KnightIdentityRuntime.MaxTrackedKnights;
                    for (int i = 0; i < capacity; i++)
                    {
                        KnightUnit unit = NativeSim.NewKnight(20000 + i);
                        KnightIdentityRuntime.OnEnable(unit.Knight);
                        Check.True(KnightIdentityRuntime.TryResolve(unit.Knight, -1, (uint)i, Styles.All, out _), "fill " + i);
                    }
                    Check.Equal(capacity, KnightIdentityRuntime.TrackedCount, "registry full");
                    Check.Equal(capacity, KnightIdentityRuntime.ReceiptCount, "all receipts live");

                    KnightUnit extra = NativeSim.NewKnight(999999);
                    KnightIdentityRuntime.OnEnable(extra.Knight);
                    Check.False(KnightIdentityRuntime.TryResolve(extra.Knight, -1, 1u, Styles.All, out _), "no identity at capacity");
                    Check.False(KnightIdentityRuntime.TryGetReceipt(extra.Knight, out _), "nothing allocated at capacity");
                    Check.Equal(capacity, KnightIdentityRuntime.ReceiptCount, "live receipts untouched");
                }
            });
        }

        private static void PrimeCallbackRecursionRefused()
        {
            Case.Run("recursive_try_resolve_from_legacy_callback_is_refused", () =>
            {
                using (Fixture f = new Fixture())
                {
                    KnightUnit unit = NativeSim.NewKnight(30001);
                    KnightIdentityRuntime.OnEnable(unit.Knight);
                    bool inner = true;
                    KnightIdentityRuntime.PrimeExisting(new[] { unit.Knight }, knight =>
                    {
                        inner = KnightIdentityRuntime.TryResolve(knight, -1, 4u, Styles.All, out _);
                        return 2;
                    });
                    Check.False(inner, "recursive resolve refused");
                    Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "legacy style still pinned");
                    Check.Equal(2, receipt.Style, "pinned legacy style");
                }
            });
        }

        private static void CheckPinned(KnightUnit unit, int expectedStyle)
        {
            Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight, out KnightIdentityReceipt receipt), "receipt for " + unit.Go.name);
            Check.Equal(expectedStyle, receipt.Style, "pinned style");
        }
    }
}
