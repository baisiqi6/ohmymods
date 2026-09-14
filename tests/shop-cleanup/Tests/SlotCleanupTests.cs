// SlotCleanupTests.cs — the wrong-shop-in-slot path of PatchRoles_Castle / ShopCleanupQueue.
// Every case drives the real production entry points (EnsureBerserkerToolShopInGreece,
// ShopCleanupQueue.TickPendingCleanup, the Castle postfixes, ReRegisterModPools) against the
// boundary doubles; nothing re-implements the queue logic.
using System;
using UnityEngine;

namespace ShopCleanupTests
{
    internal static class SlotCleanupTests
    {
        /// <summary>Longer than the queue's maximum retry backoff, so the next attempt is always due.</summary>
        private const float PastBackoff = 6f;

        internal static void Register()
        {
            Runner.Add("callback registers the wrong shop and touches nothing", CallbackRegistersOnlyNoDestruction);
            Runner.Add("safe-phase tick deregisters, deactivates and defers the destroy", TickDeregistersDeactivatesAndDefersDestroy);
            Runner.Add("late OnDestroy cannot clear the replacement shop", LateOnDestroyCannotClearReplacement);
            Runner.Add("replacement is queued only after the real destruction", ReplacementQueuedOnlyAfterRealDestruction);
            Runner.Add("slot swapped by a third party retires without action", SlotSwapRetiresWithoutAction);
            Runner.Add("object moved to another scene retires without action", ObjectSceneChangeRetiresWithoutAction);
            Runner.Add("world change retires an untouched entry without action", WorldChangeRetiresUntouchedEntry);
            Runner.Add("planner replacement retires an untouched entry without action", PlannerReplacedRetiresUntouchedEntry);
            Runner.Add("authority loss keeps the owned record and acts after restore", AuthorityLossKeepsRecord);
            Runner.Add("disabled mod keeps the owned record and acts after re-enable", DisabledModKeepsRecord);
            Runner.Add("leaving Greece keeps the owned record and acts after return", LeavingGreeceKeepsRecord);
            Runner.Add("unreachable Managers keeps the owned record", UnreachableManagersKeepsRecord);
            Runner.Add("deactivate fault blocks the destroy until inactive is confirmed", DeactivateFaultBlocksDestroy);
            Runner.Add("destroy fault retries with backoff and finally succeeds", DestroyFaultRetriesWithBackoff);
            Runner.Add("RemoveShop write followed by a throw completes without a second deregistration", RemoveWriteThenThrowCompletes);
            Runner.Add("RemoveShop throwing before the write never destroys", RemoveThrowBeforeWriteNeverDestroys);
            Runner.Add("same-frame duplicate requests dedupe to one cleanup", SameFrameDuplicateEnsureSingleCleanup);
            Runner.Add("a correct replacement shop is never touched", CorrectNewShopUntouchedAfterCompletion);
            Runner.Add("cleanup stops after the bounded number of rounds", ChurnGuardStopsUnboundedReplacement);
            Runner.Add("slot cleanup counters do not leak across worlds", SlotCountersDoNotLeakAcrossWorlds);
            Runner.Add("same-world re-register keeps an untouched record", SameWorldReRegisterKeepsUntouchedRecord);
            Runner.Add("native reclaim completes without a double action", NativeReclaimCompletesWithoutDoubleAction);
            Runner.Add("re-entrant registration waits for the next tick", ReentrantRegistrationWaitsForNextTick);
            Runner.Add("item fixed to a Berserker shop after enqueue retires without action", ItemFixedAfterEnqueueRetires);
            Runner.Add("tag change after enqueue retires without action", TagChangeAfterEnqueueRetires);
            Runner.Add("a deregistered leftover is finished even if the slot is taken", DeregisteredLeftoverFinishedAfterSlotTakeover);
            Runner.Add("native despawn before any tick retires an untouched entry", NativeDespawnBeforeTickRetires);
            Runner.Add("deregistered leftover survives a disabled window", DeregisteredLeftoverSurvivesDisabledWindow);
            Runner.Add("deregistered leftover survives an unreadable world", DeregisteredLeftoverSurvivesUnreadableWorld);
            Runner.Add("same-world pool re-registration keeps the owned record", SameWorldReRegisterKeepsRecord);
            Runner.Add("started leftover is held across a world change until destroyed", StartedLeftoverHeldAcrossWorldChange);
            Runner.Add("unreadable shop registry is unknown, never deregistered", UnreadableRegistryKeepsRecord);
            Runner.Add("unreadable slot array is unknown, never an empty slot", UnreadableSlotArrayKeepsRecord);
            Runner.Add("Remove callback changing the item blocks the destroy in the same tick", CallbackChangesItemAfterRemoveWrite);
            Runner.Add("Remove callback changing the tag blocks the destroy in the same tick", CallbackChangesTagAfterRemoveWrite);
            Runner.Add("Remove callback changing the scene blocks the destroy in the same tick", CallbackChangesSceneAfterRemoveWrite);
            Runner.Add("Remove callback swapping the world blocks the destroy in the same tick", CallbackSwapsWorldAfterRemoveWrite);
            Runner.Add("SetActive callback changing the item blocks the destroy in the same tick", SetActiveCallbackChangingItemBlocksDestroy);
            Runner.Add("SetActive callback re-registering the target blocks the destroy", SetActiveCallbackReRegisteringBlocksDestroy);
            Runner.Add("below Castle4 nothing is registered", BelowCastle4LeavesShopAlone);
            Runner.Add("outside Greece nothing is registered", NonGreekWorldLeavesShopAlone);
            Runner.Add("without authority nothing is registered", NonAuthorityEnsureLeavesShopAlone);
        }

        private static void CallbackRegistersOnlyNoDestruction()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();

            bool pending = TestApi.Ensure();

            Check.True(pending, "Ensure reports a pending cleanup");
            Check.Equal(1, TestApi.PendingCount, "exactly one cleanup entry is registered");
            Check.True(TestApi.Pending(Sim.Planner, TestApi.Left), "the left slot is pending");
            Check.Equal(0, Sim.Planner.RemoveShopCalls, "the callback must not deregister");
            Check.Equal(0, Sim.DestroyCalls, "the callback must not destroy (DestroyImmediate would be illegal here)");
            Check.Equal(0, Sim.PendingDestroys.Count, "nothing is queued for destruction by the callback");
            Check.True(wrong != null && wrong.Alive, "the wrong shop is still alive");
            Check.True(wrong.ActiveSelf, "and still active");
            Check.True(Sim.Planner.HasPlacedShop(TestApi.Left, wrong), "its slot registration is unchanged");
            Check.False(Sim.Planner.QueuedTypes.Contains(TestApi.Left), "no replacement is queued while the cleanup is pending");
            Check.True(Sim.Planner.QueuedTypes.Contains(TestApi.Right), "the healthy slot still gets its shop queued");
            Check.True(TestApi.Log.InfosContain("Shop cleanup registered"), "the registration is logged");
        }

        private static void TickDeregistersDeactivatesAndDefersDestroy()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();
            int findsBeforeTick = GameObject.FindCalls;

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the queue deregisters exactly once, in the safe phase");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the slot registration is cleared exactly once");
            Check.Null(TestApi.Slot(TestApi.Left), "the slot is empty");
            Check.False(wrong.ActiveSelf, "the wrong shop is deactivated before the destroy");
            Check.Equal(1, Sim.DestroyCalls, "one deferred destroy is requested");
            Check.True(wrong != null && wrong.Alive, "Object.Destroy is deferred: the object is still there this frame");
            Check.Equal(0, Sim.Planner.QueueCalls, "no refill while the old object is still alive");
            Check.Equal(findsBeforeTick, GameObject.FindCalls, "the safe-phase tick adds no global lookup or scan");
            Check.True(TestApi.Log.InfosContain("Shop cleanup deregistered"), "the deregistration is logged");
        }

        private static void LateOnDestroyCannotClearReplacement()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            PayableShop wrongShop = wrong.GetComponent<PayableShop>();
            TestApi.Ensure();
            TestApi.Tick();
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "precondition: one effective deregistration");

            // 原生在同一帧补位（新店通过自己的 Awake → AddShop 注册进同一个槽位）。
            GameObject replacement = Shops.PlaceBerserker(Sim.Planner, TestApi.Left);
            Sim.Planner.RemoveShopCalls = 0;

            Sim.FlushDestruction(); // 帧末：旧店 OnDestroy → 第二次 RemoveShop

            Check.Equal(1, wrongShop.OnDestroyCalls, "the native PayableShop.OnDestroy ran once");
            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the late OnDestroy call reaches RemoveShop again");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the stale second call clears nothing");
            Check.True(Sim.Planner.HasPlacedShop(TestApi.Left, replacement),
                "the replacement's slot registration survives the old shop's late OnDestroy");
        }

        private static void ReplacementQueuedOnlyAfterRealDestruction()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            TestApi.Tick();
            Check.Equal(1, Sim.DestroyCalls, "the deferred destroy is requested once");
            Check.Equal(0, Sim.Planner.QueueCalls, "no placement while the old shop is still alive");

            TestApi.Tick();
            Check.Equal(1, Sim.DestroyCalls, "a successful destroy request is not repeated every frame");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the entry completes once the object is really gone");
            Check.True(Sim.Planner.QueuedTypes.Contains(TestApi.Left), "the emptied slot is refilled");
            Check.Equal((Side?)Side.Left, Sim.Planner.QueueSides[TestApi.Left], "the left side is requested");
            Check.True(TestApi.Log.InfosContain("Shop cleanup finished"), "the completion is logged");
        }

        private static void SlotSwapRetiresWithoutAction()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            // 第三方（原生 ValidateShops/DespawnShop 之后的重摆）把槽位换成了另一家店。
            GameObject other = Shops.Place(Sim.Planner, TestApi.Left, Shops.LegacyWrongTag, "ShopThirdParty");

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration when the slot no longer holds our object");
            Check.Equal(0, Sim.DestroyCalls, "no destroy for a swapped slot");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the registered object is left untouched");
            Check.True(Sim.Planner.HasPlacedShop(TestApi.Left, other), "the third party's registration is untouched");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("holds a different shop"), "the reason is logged");
        }

        private static void ObjectSceneChangeRetiresWithoutAction()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            // 对象被搬到另一个 scene（同 world 身份不变）：不能再拿旧身份写它。
            wrong.scene = new Scene { handle = Sim.SceneHandle + 100, valid = true };

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration after the object left its scene");
            Check.Equal(0, Sim.DestroyCalls, "no destroy after the object left its scene");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is untouched");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("object left its scene"), "the reason is logged");
        }

        private static void WorldChangeRetiresUntouchedEntry()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.NewScene(false); // 换世界：新 scene handle + 新 gameLayer

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration after the world changed");
            Check.Equal(0, Sim.DestroyCalls, "no destroy after the world changed");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the old-world object is untouched");
            Check.Equal(0, TestApi.PendingCount, "an untouched entry is cancelled");
            Check.True(TestApi.Log.InfosContain("world changed before any action"), "the reason is logged");
        }

        private static void PlannerReplacedRetiresUntouchedEntry()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.NewScene(true); // planner 也被重建

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration against a different planner");
            Check.Equal(0, Sim.DestroyCalls, "no destroy");
            Check.True(wrong != null && wrong.Alive, "the object is untouched");
            Check.Equal(0, TestApi.PendingCount, "the untouched entry retires");
        }

        private static void AuthorityLossKeepsRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            NetworkBigBoss.HasWorldAuth = false;
            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "a non-authority client sends no deregistration");
            Check.Equal(0, Sim.DestroyCalls, "and destroys nothing");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is untouched");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept, not dropped");
            Check.True(TestApi.Log.InfosContain("no world authority"), "the deferral is logged");

            NetworkBigBoss.HasWorldAuth = true;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "after authority returns the cleanup proceeds");
            Check.Equal(1, Sim.DestroyCalls, "and the deferred destroy is requested");
        }

        private static void DisabledModKeepsRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            KingdomEnhancedMod.ModConfig.Enabled.Value = false;
            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "a disabled mod destroys nothing");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is untouched");
            Check.Equal(1, TestApi.PendingCount, "the owned record survives the disabled window");
            Check.True(TestApi.Log.InfosContain("mod disabled"), "the deferral is logged");

            KingdomEnhancedMod.ModConfig.Enabled.Value = true;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "re-enabling resumes the cleanup");
            Check.Equal(1, Sim.DestroyCalls, "with the deferred destroy");
        }

        private static void LeavingGreeceKeepsRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            BiomeHolder.Inst.BiomeIndex = 0;
            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration outside Greece");
            Check.Equal(0, Sim.DestroyCalls, "no destroy outside Greece");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept");
            Check.True(TestApi.Log.InfosContain("left Greece"), "the deferral is logged");

            BiomeHolder.Inst.BiomeIndex = BiomeHolder.GreeceBiomeIndex;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "returning to Greece resumes the cleanup");
        }

        private static void UnreachableManagersKeepsRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DetachManagers(); // 载入/菜单期间 Managers 暂时不可达
            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "nothing is destroyed while the world is unreadable");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is untouched");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept");
            Check.True(TestApi.Log.InfosContain("Managers unavailable"), "the deferral is logged");

            Sim.RestoreManagers();
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "when the world is readable again the cleanup proceeds");
        }

        private static void DeactivateFaultBlocksDestroy()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            GameObject.SetActiveFault = () => new InvalidOperationException("native SetActive fault");
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the deregistration is confirmed");
            Check.True(wrong.ActiveSelf, "the shop is still active");
            Check.Equal(0, Sim.DestroyCalls, "no destroy while the shop is still active");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept");
            Check.True(TestApi.Log.ErrorsContain("deactivate failed"), "the fault is logged");

            GameObject.SetActiveFault = null;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.False(wrong.ActiveSelf, "the shop is inactive now");
            Check.Equal(1, Sim.DestroyCalls, "destroy is requested once inactive is confirmed");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the cleanup completes after the flush");
        }

        private static void DestroyFaultRetriesWithBackoff()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DestroyFault = _ => new InvalidOperationException("native destroy fault");

            for (int attempt = 0; attempt < 4; attempt++)
            {
                TestApi.Tick();
                Check.Equal(1, TestApi.PendingCount, "attempt " + attempt + ": the owned record is kept");
                Check.True(wrong != null && wrong.Alive, "attempt " + attempt + ": the object is still there");
                Check.Equal(0, Sim.Planner.QueueCalls, "attempt " + attempt + ": no refill before the real destruction");
                Sim.Advance(PastBackoff);
            }

            Check.Equal(4, Sim.DestroyCalls, "four failing attempts were retried, none dropped the record");

            Sim.DestroyFault = null;
            TestApi.Tick();
            Check.Equal(5, Sim.DestroyCalls, "the failing destroy is retried until it succeeds");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the cleanup finally completes");
            Check.True(Sim.Planner.QueuedTypes.Contains(TestApi.Left), "and the slot is refilled");
            Check.Equal((Side?)Side.Left, Sim.Planner.QueueSides[TestApi.Left], "with the left side requested");
        }

        private static void RemoveWriteThenThrowCompletes()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.Planner.RemoveShopFaultAfterWrite =
                _ => new InvalidOperationException("native RemoveShop fault after the slot write");

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the deregistration was attempted once");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the side effect happened before the throw");
            Check.Null(TestApi.Slot(TestApi.Left), "the slot is already cleared");
            Check.Equal(1, Sim.DestroyCalls, "the confirmed state lets the cleanup continue");
            Check.False(wrong.ActiveSelf, "the shop is deactivated");
            Check.True(TestApi.Log.ErrorsContain("deregistration threw"), "the fault is logged");

            Sim.Planner.RemoveShopFaultAfterWrite = null;
            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the cleanup completes");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "and never deregisters twice");
            Check.Equal(2, Sim.Planner.RemoveShopCalls,
                "the only later call is the native PayableShop.OnDestroy one; the queue did not retry");
        }

        private static void RemoveThrowBeforeWriteNeverDestroys()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.Planner.RemoveShopFaultBeforeWrite =
                _ => new InvalidOperationException("native RemoveShop fault before any write");

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the deregistration was attempted");
            Check.Equal(0, Sim.Planner.EffectiveDeregistrations, "nothing was deregistered");
            Check.Equal(0, Sim.DestroyCalls, "attempted is not completed: no destroy");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the shop is left untouched");
            Check.True(Sim.Planner.HasPlacedShop(TestApi.Left, wrong), "and still registered");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept");

            Sim.Planner.RemoveShopFaultBeforeWrite = null;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(2, Sim.Planner.RemoveShopCalls, "the deregistration is retried");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "and confirmed this time");
            Check.Equal(1, Sim.DestroyCalls, "destroy follows only after confirmation");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the cleanup completes");
        }

        private static void SameFrameDuplicateEnsureSingleCleanup()
        {
            Shops.WrongShopInLeftSlot();

            // 同一帧 Castle 的两个 postfix 都会进来。
            KingdomEnhancedMod.Castle_Queue_Patch.CatchupToLevel_Postfix(Sim.Castle);
            KingdomEnhancedMod.Castle_Queue_Patch.ReQueueAllBuildings_Postfix(Sim.Castle);
            TestApi.Ensure();

            Check.Equal(1, TestApi.PendingCount, "duplicate requests dedupe to one entry");

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "one deregistration");
            Check.Equal(1, Sim.DestroyCalls, "one destroy request");
        }

        private static void CorrectNewShopUntouchedAfterCompletion()
        {
            Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            TestApi.Tick();
            Sim.FlushDestruction();
            TestApi.Tick();
            Check.Equal(0, TestApi.PendingCount, "precondition: the earlier cleanup completed");

            GameObject correct = Shops.PlaceBerserker(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            int destroysBefore = Sim.DestroyCalls;
            int removesBefore = Sim.Planner.RemoveShopCalls;

            bool pending = TestApi.Ensure();

            Check.False(pending, "a correct shop leaves nothing pending");
            Check.Equal(0, TestApi.PendingCount, "no cleanup is registered for a correct shop");
            Check.Equal(removesBefore, Sim.Planner.RemoveShopCalls, "the correct shop is never deregistered");
            Check.Equal(destroysBefore, Sim.DestroyCalls, "and never destroyed");
            Check.True(correct != null && correct.Alive && correct.ActiveSelf, "the correct shop keeps operating");
            Check.False(Sim.Planner.QueuedTypes.Contains(TestApi.Left), "its slot is left alone");
        }

        private static void ChurnGuardStopsUnboundedReplacement()
        {
            Sim.Boot();

            // 每次清理完成后原生又摆回一家错误商店：说明 prefab 映射本身坏了。
            for (int round = 0; round < KingdomEnhancedMod.ShopCleanupQueue.MaxCleanupsPerSlot; round++)
            {
                Shops.PlaceWrong(Sim.Planner, TestApi.Left);
                TestApi.Ensure();
                Check.Equal(1, TestApi.PendingCount, "round " + round + ": one cleanup is registered");
                TestApi.Tick();
                Sim.FlushDestruction();
                TestApi.Tick();
                Check.Equal(0, TestApi.PendingCount, "round " + round + ": the cleanup completes");
            }

            GameObject extra = Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            int destroysBefore = Sim.DestroyCalls;
            TestApi.Ensure();
            Check.Equal(0, TestApi.PendingCount, "cleanup stops after the bounded number of rounds");
            TestApi.Tick();
            Check.True(extra != null && extra.Alive && extra.ActiveSelf,
                "the extra wrong shop is left in place instead of looping forever");
            Check.Equal(destroysBefore, Sim.DestroyCalls, "no destroy is attempted for it");
            Check.True(TestApi.Log.ErrorsContain("was cleaned"), "the broken prefab mapping is reported once");

            // 槽位真的恢复正常后计数清零，后续仍然能清理。
            Shops.PlaceBerserker(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            Check.False(TestApi.Ensure(), "a correct shop resets the churn guard");

            Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            TestApi.Ensure();
            Check.Equal(1, TestApi.PendingCount, "after the slot recovered, a new wrong shop is cleaned again");
        }

        private static void SlotCountersDoNotLeakAcrossWorlds()
        {
            Sim.Boot();

            for (int round = 0; round < KingdomEnhancedMod.ShopCleanupQueue.MaxCleanupsPerSlot; round++)
            {
                Shops.PlaceWrong(Sim.Planner, TestApi.Left);
                TestApi.Ensure();
                TestApi.Tick();
                Sim.FlushDestruction();
                TestApi.Tick();
            }

            Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            TestApi.Ensure();
            Check.Equal(0, TestApi.PendingCount, "precondition: the cap is reached in this world");

            // 换世界 + 换 planner：上一个世界的清理计数不能继续拦着新世界。
            Sim.NewScene(true);
            GameObject wrong = Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            int destroysBefore = Sim.DestroyCalls;

            TestApi.Ensure();
            Check.Equal(1, TestApi.PendingCount, "the new world is not blocked by the old world's counters");

            TestApi.Tick();
            Check.Equal(1, Sim.Planner.RemoveShopCalls, "and the wrong shop is cleaned normally");
            Check.Equal(destroysBefore + 1, Sim.DestroyCalls, "with the usual deferred destroy");
            Check.True(wrong != null && wrong.Alive, "as a deferred destroy, not immediate");
        }

        private static void SameWorldReRegisterKeepsUntouchedRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Check.Equal(1, TestApi.PendingCount, "precondition: one entry is registered");

            // Holder.Init / PoolFix 会在同一 world 里多次调用 ReRegisterModPools：不能碰 owned 记录。
            KingdomEnhancedMod.PatchRoles_Castle.ReRegisterModPools();

            Check.Equal(1, TestApi.PendingCount, "re-registering pools never drops an untouched owned record");
            Check.True(KingdomEnhancedMod.PatchRoles_NorseSquad.EnsurePoolCalls >= 1, "the pool re-registration path ran");

            Sim.ClearPlacementBookkeeping();
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the untouched entry still cleans up after the re-registration");
            Check.True(wrong != null && wrong.Alive && !wrong.ActiveSelf, "and it is deactivated with a deferred destroy");
        }

        private static void SameWorldReRegisterKeepsRecord()
        {
            Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DestroyFault = _ => new InvalidOperationException("native destroy fault");
            TestApi.Tick();
            Sim.DestroyFault = null;
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "precondition: deregistered and confirmed");
            Check.Equal(1, TestApi.PendingCount, "precondition: the leftover still needs its destroy");
            int destroysBefore = Sim.DestroyCalls;

            // Holder.Init / PoolFix 在同一 world 再次触发 ReRegisterModPools：不能丢掉 owned 记录。
            KingdomEnhancedMod.PatchRoles_Castle.ReRegisterModPools();

            Check.Equal(1, TestApi.PendingCount, "same-world pool re-registration keeps the owned record");
            Check.True(KingdomEnhancedMod.PatchRoles_NorseSquad.EnsurePoolCalls >= 1, "the pool path really ran");

            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(destroysBefore + 1, Sim.DestroyCalls, "the leftover still finishes its destroy afterwards");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "the record is dropped only after the real destruction");
        }

        private static void StartedLeftoverHeldAcrossWorldChange()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DestroyFault = _ => new InvalidOperationException("native destroy fault");
            TestApi.Tick();
            Sim.DestroyFault = null;
            Check.Equal(1, TestApi.PendingCount, "precondition: started leftover waiting for its destroy");

            Sim.NewScene(false);
            Sim.Advance(PastBackoff);
            int destroysBefore = Sim.DestroyCalls;

            TestApi.Tick();

            Check.Equal(destroysBefore, Sim.DestroyCalls, "no writes to the old object after the world changed");
            Check.Equal(1, TestApi.PendingCount, "the started record is kept until the old object is confirmed destroyed");

            // 旧世界的对象随场景卸载消失（真实 liveness），记录这才退役。
            UnityEngine.Object.Destroy(wrong);
            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "confirmed destruction retires the record");
        }

        private static void UnreadableRegistryKeepsRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Il2CppSystem.Collections.Generic.List<GameObject> shops = Sim.Planner._shops;
            Sim.Planner._shops = null;

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "an unreadable registry is unknown, never treated as deregistered");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is untouched");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept");
            Check.True(TestApi.Log.InfosContain("shop registry unreadable"), "the deferral is logged");

            Sim.Planner._shops = shops;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "once readable again the cleanup proceeds");
            Check.Equal(1, Sim.DestroyCalls, "and the destroy follows");
        }

        private static void UnreadableSlotArrayKeepsRecord()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> placed = Sim.Planner._placedShops;
            Sim.Planner._placedShops = null;

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "an unreadable slot array is unknown, never an empty slot");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is untouched");
            Check.Equal(1, TestApi.PendingCount, "the owned record is kept");
            Check.True(TestApi.Log.InfosContain("slot state unreadable"), "the deferral is logged");

            Sim.Planner._placedShops = placed;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "once readable again the cleanup proceeds");
            Check.Equal(1, Sim.DestroyCalls, "and the destroy follows");
        }

        private static void CallbackChangesItemAfterRemoveWrite()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.Planner.RemoveShopFaultAfterWrite = go =>
            {
                go.GetComponent<PayableShop>().itemPrefab = Shops.MakeItem(Shops.BerserkerTag);
                return new InvalidOperationException("native RemoveShop fault after changing the item");
            };

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the deregistration side effect happened");
            Check.Equal(0, Sim.DestroyCalls, "the callback made it a real shop, so nothing is destroyed");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the reclaimed shop is left alive");
            Check.Equal(0, TestApi.PendingCount, "the entry retires instead of destroying a reclaimed shop");
            Check.True(TestApi.Log.InfosContain("object is a real Berserker shop"), "the reason is logged");
        }

        private static void CallbackChangesTagAfterRemoveWrite()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.Planner.RemoveShopFaultAfterWrite = go =>
            {
                go.GetComponent<ShopTag>().type = PayableShop.ShopType.Bow;
                return new InvalidOperationException("native RemoveShop fault after changing the tag");
            };

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "no destroy after the callback changed the tag");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is left untouched");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("shop tag changed"), "the reason is logged");
        }

        private static void CallbackChangesSceneAfterRemoveWrite()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.Planner.RemoveShopFaultAfterWrite = go =>
            {
                go.scene = new Scene { handle = Sim.SceneHandle + 50, valid = true };
                return new InvalidOperationException("native RemoveShop fault after moving the object");
            };

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "no destroy after the callback moved the object to another scene");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is left untouched");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("object left its scene"), "the reason is logged");
        }

        private static void CallbackSwapsWorldAfterRemoveWrite()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.Planner.RemoveShopFaultAfterWrite = _ =>
            {
                Sim.NewScene(false);
                return new InvalidOperationException("native RemoveShop fault after the world changed");
            };

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "no destroy once the world identity changed inside the callback");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the old object is left untouched");
            Check.Equal(1, TestApi.PendingCount, "the started record is kept, with no writes into the new world");
        }

        private static void SetActiveCallbackChangingItemBlocksDestroy()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            GameObject.SetActiveCallback =
                go => go.GetComponent<PayableShop>().itemPrefab = Shops.MakeItem(Shops.BerserkerTag);

            TestApi.Tick();
            GameObject.SetActiveCallback = null;

            Check.False(wrong.ActiveSelf, "the deactivate itself took effect");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the deregistration is confirmed");
            Check.Equal(0, Sim.DestroyCalls, "the SetActive callback made it a real shop, so no destroy this tick");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
        }

        private static void SetActiveCallbackReRegisteringBlocksDestroy()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            GameObject.SetActiveCallback = go => Sim.Planner.AddShop(go);

            TestApi.Tick();
            GameObject.SetActiveCallback = null;

            Check.False(wrong.ActiveSelf, "the deactivate itself took effect");
            Check.Equal(0, Sim.DestroyCalls, "a re-registered target is not destroyed in the same pass");
            Check.Equal(1, TestApi.PendingCount, "the record goes back to the deregistration stage");
            Check.True(Sim.Planner.HasPlacedShop(TestApi.Left, wrong), "the callback's registration is left in place");

            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(2, Sim.Planner.RemoveShopCalls, "the next pass deregisters it again");
            Check.Equal(1, Sim.DestroyCalls, "and only after that confirmation is it destroyed");
        }

        private static void NativeReclaimCompletesWithoutDoubleAction()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            // 原生自己回收这家店（例如 DespawnShop / 科技时代回退）：先解除登记再销毁。
            Sim.Planner.RemoveShop(wrong);
            UnityEngine.Object.Destroy(wrong);
            Sim.FlushDestruction();
            int removeCallsBeforeTick = Sim.Planner.RemoveShopCalls;
            int destroysBeforeTick = Sim.DestroyCalls;

            TestApi.Tick();

            Check.Equal(removeCallsBeforeTick, Sim.Planner.RemoveShopCalls,
                "the queue does not deregister what the native side already removed");
            Check.Equal(destroysBeforeTick, Sim.DestroyCalls, "and does not request another destroy");
            Check.Equal(0, TestApi.PendingCount, "the entry completes because the object is really gone");
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "the slot was cleared once, by the native flow");
            Check.True(Sim.Planner.QueuedTypes.Contains(TestApi.Left), "the emptied slot is refilled after the destruction");
        }

        private static void ReentrantRegistrationWaitsForNextTick()
        {
            Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            TestApi.Tick();
            Sim.FlushDestruction();

            // 旧对象已经销毁；此刻槽位里又出现一家错误商店，补位回调会在 Tick 内重新登记它。
            Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            int destroysBefore = Sim.DestroyCalls;

            TestApi.Tick();

            Check.Equal(1, TestApi.PendingCount, "the re-entrant registration is kept");
            Check.Equal(0, Sim.Planner.RemoveShopCalls, "but it is not processed in the same tick");
            Check.Equal(destroysBefore, Sim.DestroyCalls, "and nothing is destroyed in that tick");

            TestApi.Tick();

            Check.Equal(1, Sim.Planner.RemoveShopCalls, "the next tick processes the new entry");
            Check.Equal(destroysBefore + 1, Sim.DestroyCalls, "with a single deferred destroy");
        }

        private static void ItemFixedAfterEnqueueRetires()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            // 第三方把 itemPrefab 修成了真的狂战士工具：对象不再是"错误物品"。
            wrong.GetComponent<PayableShop>().itemPrefab = Shops.MakeItem(Shops.BerserkerTag);

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration for a shop that is no longer wrong");
            Check.Equal(0, Sim.DestroyCalls, "and no destroy");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the repaired shop keeps operating");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("object is a real Berserker shop"), "the reason is logged");
        }

        private static void TagChangeAfterEnqueueRetires()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            // 重入/第三方改过 tag：注销语义不再可信，必须停止写权。
            wrong.GetComponent<ShopTag>().type = PayableShop.ShopType.Bow;

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration under a changed tag");
            Check.Equal(0, Sim.DestroyCalls, "and no destroy");
            Check.Equal(0, TestApi.PendingCount, "the entry retires");
            Check.True(TestApi.Log.InfosContain("shop tag changed"), "the reason is logged");
        }

        private static void DeregisteredLeftoverFinishedAfterSlotTakeover()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DestroyFault = _ => new InvalidOperationException("native destroy fault");
            TestApi.Tick();

            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "precondition: deregistered and confirmed");
            Check.False(wrong.ActiveSelf, "precondition: deactivated");
            Check.Equal(1, TestApi.PendingCount, "precondition: the destroy is still pending");

            // 第三方把新店摆进同一个槽位：不能因此放弃自己已经停用的旧残留。
            GameObject newcomer = Shops.PlaceBerserker(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();
            Sim.DestroyFault = null;
            Sim.Advance(PastBackoff);
            int destroysBefore = Sim.DestroyCalls;

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "our leftover is not deregistered again");
            Check.True(Sim.Planner.HasPlacedShop(TestApi.Left, newcomer), "the newcomer's slot is untouched");
            Check.Equal(destroysBefore + 1, Sim.DestroyCalls, "our own leftover is still destroyed");
            Check.True(newcomer != null && newcomer.Alive && newcomer.ActiveSelf, "the newcomer keeps operating");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "our leftover finishes and the record is dropped");
        }

        private static void NativeDespawnBeforeTickRetires()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            // 原生先把它从槽位移走（DespawnShop 的语义）：槽位已不精确属于本对象，安全取消。
            Sim.Planner._placedShops[(int)TestApi.Left] = null;

            TestApi.Tick();

            Check.Equal(0, Sim.Planner.RemoveShopCalls, "no deregistration for a shop the native side moved out");
            Check.Equal(0, Sim.DestroyCalls, "and no destroy");
            Check.True(wrong != null && wrong.Alive && wrong.ActiveSelf, "the object is left untouched");
            Check.Equal(0, TestApi.PendingCount, "the untouched entry retires");
            Check.True(TestApi.Log.InfosContain("no longer held by the registered object"), "the reason is logged");
        }

        private static void DeregisteredLeftoverSurvivesDisabledWindow()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DestroyFault = _ => new InvalidOperationException("native destroy fault");
            TestApi.Tick();
            Sim.DestroyFault = null;
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "precondition: deregistered");
            Check.False(wrong.ActiveSelf, "precondition: deactivated");
            int destroysBefore = Sim.DestroyCalls;

            KingdomEnhancedMod.ModConfig.Enabled.Value = false;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(destroysBefore, Sim.DestroyCalls, "nothing happens while the mod is off");
            Check.Equal(1, TestApi.PendingCount, "the owned leftover record is kept");

            KingdomEnhancedMod.ModConfig.Enabled.Value = true;
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(destroysBefore + 1, Sim.DestroyCalls, "re-enabling finishes the owned leftover");
        }

        private static void DeregisteredLeftoverSurvivesUnreadableWorld()
        {
            GameObject wrong = Shops.WrongShopInLeftSlot();
            TestApi.Ensure();
            Sim.ClearPlacementBookkeeping();

            Sim.DestroyFault = _ => new InvalidOperationException("native destroy fault");
            TestApi.Tick();
            Sim.DestroyFault = null;
            Check.Equal(1, Sim.Planner.EffectiveDeregistrations, "precondition: deregistered");
            int destroysBefore = Sim.DestroyCalls;

            Sim.DetachManagers(); // 载入/菜单：世界暂时读不到
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(destroysBefore, Sim.DestroyCalls, "nothing happens while the world is unreadable");
            Check.Equal(1, TestApi.PendingCount, "the owned leftover record is kept");

            Sim.RestoreManagers();
            Sim.Advance(PastBackoff);
            TestApi.Tick();

            Check.Equal(destroysBefore + 1, Sim.DestroyCalls, "the leftover is finished once the world is readable again");

            Sim.FlushDestruction();
            TestApi.Tick();

            Check.Equal(0, TestApi.PendingCount, "and the record is dropped only after the real destruction");
        }

        private static void BelowCastle4LeavesShopAlone()
        {
            Sim.Boot(Castle.Level.Castle3);
            Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            Sim.ClearPlacementBookkeeping();

            Check.False(TestApi.Ensure(), "below Castle4 the ensure does nothing");
            Check.Equal(0, TestApi.PendingCount, "no cleanup entry is created");

            TestApi.Tick();

            Check.Equal(0, Sim.DestroyCalls, "nothing is destroyed");
            Check.Equal(0, Sim.Planner.RemoveShopCalls, "nothing is deregistered");
        }

        private static void NonGreekWorldLeavesShopAlone()
        {
            Sim.Boot();
            GameObject wrong = Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            BiomeHolder.Inst.BiomeIndex = 1;
            Sim.ClearPlacementBookkeeping();

            Check.False(TestApi.Ensure(), "outside Greece the ensure registers nothing");
            Check.Equal(0, TestApi.PendingCount, "no cleanup entry is created");

            TestApi.Tick();

            Check.True(wrong != null && wrong.Alive, "the shop is left alone");
        }

        private static void NonAuthorityEnsureLeavesShopAlone()
        {
            Sim.Boot();
            Shops.PlaceWrong(Sim.Planner, TestApi.Left);
            NetworkBigBoss.HasWorldAuth = false;
            Sim.ClearPlacementBookkeeping();

            Check.False(TestApi.Ensure(), "without authority the ensure registers nothing");
            Check.Equal(0, TestApi.PendingCount, "no cleanup entry is created");
        }
    }
}
