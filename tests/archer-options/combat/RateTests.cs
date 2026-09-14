using System;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    public class RateTests
    {
        private static Fixture Rate(float multiplier = 2f, bool enabled = true)
        {
            Fixture fixture = new Fixture();
            ModConfig.ArcherRateEnabled.Value = enabled;
            ModConfig.ArcherRateMultiplier.Value = multiplier;
            return fixture;
        }

        private static Vector2 Doubled(Vector2 value) => new Vector2(value.x * 2f, value.y * 2f);

        // ---------------- call-time temporary cadence (Archer._Shoot_d__225.MoveNext) ----------------

        [Fact]
        public void Disabled_DoesNotBorrowAndNeverTouchesTheCadenceFields()
        {
            Fixture f = Rate(enabled: false);
            Archer archer = f.NewArcher();

            CoroutineRun run = f.MoveNext(archer);

            Assert.False(run.BorrowEntered);
            Assert.Equal(0.4f, run.ReadPrep, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), run.ReadInterval);
            Assert.Equal(new Vector2(0.5f, 0.9f), run.ReadFormation);
            Assert.Equal(0.4f, archer.shootPrepTime, 5);
        }

        [Fact]
        public void Borrow_ShrinksOnlyForTheCoroutineStepAndIsReturnedAfterwards()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            CoroutineRun run = f.MoveNext(archer);

            Assert.True(run.BorrowEntered);
            Assert.Equal(0.2f, run.ReadPrep, 5);
            Assert.Equal(new Vector2(0.175f, 0.325f), run.ReadInterval);
            Assert.Equal(new Vector2(0.25f, 0.45f), run.ReadFormation);

            // no cross-frame residue: the fields are native again right after the coroutine step
            Assert.Equal(0.4f, archer.shootPrepTime, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), archer._shootIntervalRange);
            Assert.Equal(new Vector2(0.5f, 0.9f), archer._shootIntervalRangeFormation);
        }

        [Fact]
        public void Borrow_MultiplierIsClampedToOneThroughTwo()
        {
            Fixture f = Rate(9f);
            Archer archer = f.NewArcher();

            CoroutineRun fast = f.MoveNext(archer);

            Assert.True(fast.BorrowEntered);
            Assert.Equal(0.2f, fast.ReadPrep, 5);          // 2x max, never 9x

            ModConfig.ArcherRateMultiplier.Value = 0.5f;
            CoroutineRun slow = f.MoveNext(archer);

            Assert.False(slow.BorrowEntered);              // 1x = inert
            Assert.Equal(0.4f, slow.ReadPrep, 5);
        }

        [Fact]
        public void Borrow_FloorsNeverSlowAnAlreadyFasterValue()
        {
            Fixture f = Rate(2f);

            Archer fast = f.NewArcher();
            fast.shootPrepTime = 0.01f;                              // below the 0.02 floor
            fast._shootIntervalRange = new Vector2(0.01f, 0.02f);
            fast._shootIntervalRangeFormation = new Vector2(0.005f, 0.015f);

            CoroutineRun run = f.MoveNext(fast);

            Assert.False(run.BorrowEntered);                         // nothing may be shrunk
            Assert.Equal(0.01f, run.ReadPrep, 6);
            Assert.Equal(new Vector2(0.01f, 0.02f), run.ReadInterval);
            Assert.Equal(new Vector2(0.005f, 0.015f), run.ReadFormation);

            Archer mixed = f.NewArcher();
            mixed._shootIntervalRange = new Vector2(0.03f, 0.5f);

            CoroutineRun mixedRun = f.MoveNext(mixed);

            Assert.True(mixedRun.BorrowEntered);
            Assert.Equal(0.02f, mixedRun.ReadInterval.x, 6);         // floored, still <= 0.03
            Assert.Equal(0.25f, mixedRun.ReadInterval.y, 6);
            Assert.Equal(new Vector2(0.03f, 0.5f), mixed._shootIntervalRange);
        }

        [Fact]
        public void Borrow_NativeFailureStillReturnsTheFieldsAndKeepsTheRealException()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            CoroutineRun run = f.MoveNext(archer, throwFromNative: true);

            Assert.NotNull(run.Thrown);                              // not swallowed
            Assert.Equal(0.4f, archer.shootPrepTime, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), archer._shootIntervalRange);
            Assert.Equal(new Vector2(0.5f, 0.9f), archer._shootIntervalRangeFormation);
        }

        [Fact]
        public void Borrow_WithDeadlandsNesting_ScalesOnTopOfDlTempAndBothFinalizersRestoreOriginals()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            CoroutineRun run = f.MoveNext(archer, deadlands: true);

            // DL halved first (0.4 -> 0.2), then the user multiplier applies: 0.2 / 2 = 0.1
            Assert.Equal(0.1f, run.ReadPrep, 5);
            Assert.Equal(0.0875f, run.ReadInterval.x, 5);            // 0.35 * 0.5 / 2
            Assert.Equal(0.1625f, run.ReadInterval.y, 5);
            Assert.Equal(0.125f, run.ReadFormation.x, 5);            // 0.5 * 0.5 / 2
            Assert.Equal(0.225f, run.ReadFormation.y, 5);

            // our Priority.First finalizer handed back the DL temporary value...
            Assert.Equal(0.2f, run.AfterOurReturn, 5);
            // ...and DL's own finalizer then restored the originals
            Assert.Equal(0.4f, archer.shootPrepTime, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), archer._shootIntervalRange);
            Assert.Equal(new Vector2(0.5f, 0.9f), archer._shootIntervalRangeFormation);
        }

        [Fact]
        public void CrossbowPackageAcrossFrames_IsNeverDerivedFromTheShortenedValue()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            CoroutineRun first = f.MoveNext(archer);
            Assert.True(first.BorrowEntered);

            // external package (crossbowman Apply / ApplySquad) reads the native interval and doubles it once
            archer._shootIntervalRange = Doubled(archer._shootIntervalRange);
            archer._shootIntervalRangeFormation = Doubled(archer._shootIntervalRangeFormation);

            CoroutineRun second = f.MoveNext(archer);

            Assert.True(second.BorrowEntered);
            Assert.Equal(0.35f, second.ReadInterval.x, 5);           // (0.35 * 2) / 2, not (0.175 * 2) / 2
            Assert.Equal(0.65f, second.ReadInterval.y, 5);
            Assert.Equal(0.7f, archer._shootIntervalRange.x, 5);     // returned to the package value
            Assert.Equal(1.3f, archer._shootIntervalRange.y, 5);
        }

        [Fact]
        public void Borrow_ExternalReplacementMidBorrow_IsNeverOverwritten()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            Archer._Shoot_d__225 iterator = archer.NewShootIterator();

            PatchArcher_Options.CadenceBorrow borrow = PatchBridge.ShootMoveNextEnter(iterator);
            Assert.True(borrow.Entered);
            archer._shootIntervalRange = new Vector2(7f, 7f);        // another owner writes during the borrow

            PatchBridge.ShootMoveNextExit(borrow, iterator, null);

            Assert.Equal(new Vector2(7f, 7f), archer._shootIntervalRange);
            Assert.Equal(0.4f, archer.shootPrepTime, 5);             // our own untouched field is returned
        }

        [Fact]
        public void Borrow_ReturnFailure_KeepsARetryableReceiptAndBlocksFurtherShrinking()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            // the prefix applies normally; the failure is injected while the native body runs so that the
            // *return* write fails (prefix-side failures have their own test below)
            CoroutineRun failed = f.MoveNext(archer, duringNative: () => archer.ThrowOnPrepWrite = true);

            Assert.True(failed.BorrowEntered);
            Assert.Equal(0.2f, archer.shootPrepTime, 5);             // residue kept until the receipt is honoured
            Assert.Equal(new Vector2(0.175f, 0.325f), archer._shootIntervalRange);

            CoroutineRun blocked = f.MoveNext(archer);               // pending owner: refuse to shrink again
            Assert.False(blocked.BorrowEntered);
            Assert.Equal(0.2f, blocked.ReadPrep, 5);

            archer.ThrowOnPrepWrite = false;
            Time.unscaledTime += 0.6f;
            f.Tick();

            Assert.Equal(0.4f, archer.shootPrepTime, 5);             // baseline restored exactly once
            Assert.Equal(new Vector2(0.35f, 0.65f), archer._shootIntervalRange);

            CoroutineRun after = f.MoveNext(archer);
            Assert.True(after.BorrowEntered);                        // and borrowing works again
        }

        [Fact]
        public void Borrow_PrefixWriteFailure_IsRecordedAndNeverEscapesTheCoroutine()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            archer.ThrowOnPrepWrite = true;                          // the prefix's own write fails
            CoroutineRun run = f.MoveNext(archer, duringNative: () => archer.ThrowOnPrepWrite = false);

            Assert.Null(run.Thrown);                                 // our failure never breaks the native coroutine
            Assert.True(run.BorrowEntered);
            Assert.Equal(0.4f, run.ReadPrep, 5);                     // the failed field keeps its native value
            Assert.Equal(new Vector2(0.35f, 0.65f), run.ReadInterval);   // later fields were not applied either

            // the finalizer released the partial claim without overwriting the native value
            Assert.Equal(0.4f, archer.shootPrepTime, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), archer._shootIntervalRange);

            // no lingering receipt: the next coroutine step borrows immediately (no Tick needed)
            CoroutineRun after = f.MoveNext(archer);
            Assert.True(after.BorrowEntered);
            Assert.Equal(0.2f, after.ReadPrep, 5);
            Assert.Equal(0.4f, archer.shootPrepTime, 5);             // and it is returned again
        }

        [Fact]
        public void EnableBeforeNewLife_ReturnsTheOutstandingReceipt()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();

            // create a real outstanding receipt: prefix applies, the return write fails
            CoroutineRun failed = f.MoveNext(archer, duringNative: () => archer.ThrowOnPrepWrite = true);
            Assert.True(failed.BorrowEntered);
            Assert.Equal(0.2f, archer.shootPrepTime, 5);             // residue until the receipt is honoured

            archer.ThrowOnPrepWrite = false;
            PatchBridge.RaiseArcherOnEnable(archer);                 // pooled reuse: prefix runs before native OnEnable

            Assert.Equal(0.4f, archer.shootPrepTime, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), archer._shootIntervalRange);
            Assert.True(f.MoveNext(archer).BorrowEntered);
        }

        [Fact]
        public void Receipt_TransientIdentityReadFailure_KeepsRestoreResponsibility()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            f.MoveNext(archer, duringNative: () => archer.ThrowOnPrepWrite = true);
            Assert.Equal(0.2f, archer.shootPrepTime, 5);
            archer.ThrowOnPrepWrite = false;
            archer.gameObject.ThrowOnInstanceIdRead = true;
            Time.unscaledTime += 0.6f;
            f.Tick();
            Assert.Equal(0.2f, archer.shootPrepTime, 5);
            archer.gameObject.ThrowOnInstanceIdRead = false;
            Time.unscaledTime += 0.6f;
            f.Tick();
            Assert.Equal(0.4f, archer.shootPrepTime, 5);
            Assert.True(f.MoveNext(archer).BorrowEntered);
        }

        // ---------------- Update timer observation ----------------

        [Fact]
        public void Timer_AddsTheObservedDrainTimesTheMultiplier()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            Time.deltaTime = 0.05f;
            archer._cooldown = 1f;

            PatchBridge.RunArcherUpdate(archer);

            Assert.Equal(0.9f, archer._cooldown, 5);                 // native -0.05 and module -0.05
        }

        [Fact]
        public void Timer_WithDeadlandsDoubleDecrement_StaysCompatible()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            Time.deltaTime = 0.05f;
            archer._cooldown = 1f;

            PatchBridge.RunArcherUpdate(archer, preNative: () => archer._cooldown -= Time.deltaTime);

            Assert.Equal(0.8f, archer._cooldown, 5);                 // 2 native halves + 2 module halves
        }

        [Fact]
        public void Timer_NeverScalesNativeResets()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            Time.deltaTime = 0.05f;

            archer._cooldown = 5f;
            PatchBridge.RunArcherUpdate(archer, postNative: () => archer._cooldown = 2f);
            Assert.Equal(2f, archer._cooldown, 5);                   // fresh cooldown from the Shoot routine

            archer._cooldown = 5f;
            PatchBridge.RunArcherUpdate(archer, postNative: () => archer._cooldown = 4f);
            Assert.Equal(4f, archer._cooldown, 5);                   // drain far larger than 2dt: treated as a reset

            archer._cooldown = 0f;
            PatchBridge.RunArcherUpdate(archer);
            Assert.Equal(0f, archer._cooldown, 5);                   // ready: nothing drained
        }

        [Fact]
        public void Timer_SkipsNonRangedPlayerControlledAndShootingArchers()
        {
            Fixture f = Rate(2f);
            Time.deltaTime = 0.05f;

            Archer melee = f.NewArcher();
            melee._cooldown = 1f;
            melee._attackMode = Archer.AttackMode.Melee;
            PatchBridge.RunArcherUpdate(melee);
            Assert.Equal(0.95f, melee._cooldown, 5);

            Archer wantedMelee = f.NewArcher();
            wantedMelee._cooldown = 1f;
            wantedMelee._desiredAttackMode = Archer.AttackMode.Melee;
            PatchBridge.RunArcherUpdate(wantedMelee);
            Assert.Equal(0.95f, wantedMelee._cooldown, 5);

            Archer controlled = f.NewArcher();
            controlled._cooldown = 1f;
            controlled._unitController = new FakeUnitController();
            PatchBridge.RunArcherUpdate(controlled);
            Assert.Equal(1f, controlled._cooldown, 5);               // native early return preserved

            Archer shooting = f.NewArcher();
            shooting._cooldown = 1f;
            shooting.shoot = new Coatsink.Common.Haglet { started = true };
            PatchBridge.RunArcherUpdate(shooting);
            Assert.Equal(0.95f, shooting._cooldown, 5);              // coroutine sentinel never accelerated
        }

        [Fact]
        public void Timer_SkipsInertGrabbedDeadPausedUnauthorizedAndOutOfScopeArchers()
        {
            Fixture f = Rate(2f);
            Time.deltaTime = 0.05f;

            Archer inert = f.NewArcher();
            inert._cooldown = 1f;
            inert._character.inert = true;
            PatchBridge.RunArcherUpdate(inert);
            Assert.Equal(0.95f, inert._cooldown, 5);

            Archer grabbed = f.NewArcher();
            grabbed._cooldown = 1f;
            grabbed._character.grabbed = true;
            PatchBridge.RunArcherUpdate(grabbed);
            Assert.Equal(0.95f, grabbed._cooldown, 5);

            Archer dead = f.NewArcher();
            dead._cooldown = 1f;
            dead._damageable.isDead = true;
            PatchBridge.RunArcherUpdate(dead);
            Assert.Equal(0.95f, dead._cooldown, 5);

            Time.timeScale = 0f;
            Time.deltaTime = 0f;
            Archer paused = f.NewArcher();
            paused._cooldown = 1f;
            PatchBridge.RunArcherUpdate(paused);
            Assert.Equal(1f, paused._cooldown, 5);                   // paused: no native tick, no module drain
            Time.timeScale = 1f;
            Time.deltaTime = 0.05f;

            NetworkBigBoss.HasWorldAuth = false;
            Archer client = f.NewArcher();
            client._cooldown = 1f;
            PatchBridge.RunArcherUpdate(client);
            Assert.Equal(0.95f, client._cooldown, 5);
            NetworkBigBoss.HasWorldAuth = true;

            ArcherOptionsScope.Active = false;
            Archer otherWorld = f.NewArcher();
            otherWorld._cooldown = 1f;
            PatchBridge.RunArcherUpdate(otherWorld);
            Assert.Equal(0.95f, otherWorld._cooldown, 5);
            ArcherOptionsScope.Active = true;

            Archer outside = f.NewArcher(currentLayer: false);
            outside._cooldown = 1f;
            PatchBridge.RunArcherUpdate(outside);
            Assert.Equal(0.95f, outside._cooldown, 5);
        }

        [Fact]
        public void Timer_NativeThrow_SkipsTheDrainAndKeepsTheException()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            Time.deltaTime = 0.05f;
            archer._cooldown = 1f;

            UpdateRun run = PatchBridge.RunArcherUpdate(archer, throwFromNative: true);

            Assert.NotNull(run.Thrown);
            Assert.False(run.PostfixRan);
            Assert.True(run.FinalizerRan);                           // finalizers run even when the original throws
            Assert.Same(run.Thrown, run.FinalizerException);         // the real exception is preserved untouched
            Assert.Equal(0.95f, archer._cooldown, 5);                // native dt only, no module drain
        }

        [Fact]
        public void Timer_SuccessPath_AlsoRunsTheFinalizerWithoutASecondDrain()
        {
            Fixture f = Rate(2f);
            Archer archer = f.NewArcher();
            Time.deltaTime = 0.05f;
            archer._cooldown = 1f;

            UpdateRun run = PatchBridge.RunArcherUpdate(archer);

            Assert.True(run.PostfixRan);
            Assert.True(run.FinalizerRan);                           // Harmony runs finalizers on success too
            Assert.Null(run.FinalizerException);
            Assert.Null(run.Thrown);
            Assert.Equal(0.9f, archer._cooldown, 5);                 // drained exactly once, not twice
        }

        [Fact]
        public void Timer_DisabledDoesNothing()
        {
            Fixture f = Rate(enabled: false);
            Archer archer = f.NewArcher();
            Time.deltaTime = 0.05f;
            archer._cooldown = 1f;

            PatchBridge.RunArcherUpdate(archer);

            Assert.Equal(0.95f, archer._cooldown, 5);
        }
    }
}
