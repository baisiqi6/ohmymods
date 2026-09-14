using System;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// Identity boundary: a native pointer can be freed and reallocated to a *different* GameObject.
    /// The module must never write cadence fields into an object it did not borrow from, and must keep
    /// the original object's responsibility until the original is verifiably gone.
    /// </summary>
    public class CadenceIdentityTests
    {
        private static Fixture Rate(float multiplier = 2f)
        {
            Fixture fixture = new Fixture();
            ModConfig.ArcherRateEnabled.Value = true;
            ModConfig.ArcherRateMultiplier.Value = multiplier;
            return fixture;
        }

        /// <summary>Values no legitimate cadence outcome can produce, so any stray write is visible.</summary>
        private static void Distinctive(Archer archer)
        {
            archer.shootPrepTime = 0.9f;
            archer._shootIntervalRange = new Vector2(1.5f, 2.5f);
            archer._shootIntervalRangeFormation = new Vector2(3.5f, 4.5f);
        }

        private static void AssertDistinctive(Archer archer)
        {
            Assert.Equal(0.9f, archer.shootPrepTime, 5);
            Assert.Equal(new Vector2(1.5f, 2.5f), archer._shootIntervalRange);
            Assert.Equal(new Vector2(3.5f, 4.5f), archer._shootIntervalRangeFormation);
        }

        [Fact]
        public void MoveNextExit_WithReassignedIteratorOwner_RestoresTheOriginalAndNeverWritesTheNewOne()
        {
            Fixture f = Rate(2f);
            Archer original = f.NewArcher();
            Archer other = f.NewArcher();
            Distinctive(other);

            Archer._Shoot_d__225 iterator = original.NewShootIterator();
            PatchArcher_Options.CadenceBorrow borrow = PatchBridge.ShootMoveNextEnter(iterator);
            Assert.True(borrow.Entered);

            iterator.__4__this = other;                              // iterator owner reassigned mid-borrow

            PatchBridge.ShootMoveNextExit(borrow, iterator, null);

            // the real owner is restored (responsibility kept), the new owner is never touched
            Assert.Equal(0.4f, original.shootPrepTime, 5);
            Assert.Equal(new Vector2(0.35f, 0.65f), original._shootIntervalRange);
            Assert.Equal(new Vector2(0.5f, 0.9f), original._shootIntervalRangeFormation);
            AssertDistinctive(other);
        }

        [Fact]
        public void MoveNextExit_WithDestroyedOriginalOwner_DoesNotWriteTheObjectAtTheReusedAddress()
        {
            Fixture f = Rate(2f);
            Archer original = f.NewArcher();
            Archer reused = f.NewArcher();
            Distinctive(reused);

            Archer._Shoot_d__225 iterator = original.NewShootIterator();
            PatchArcher_Options.CadenceBorrow borrow = PatchBridge.ShootMoveNextEnter(iterator);
            Assert.True(borrow.Entered);
            IntPtr originalPointer = original.Pointer;

            original.DestroyForTests();                              // native object freed ...
            reused.SetPointerForTests(originalPointer);              // ... and the address reused by another GameObject
            iterator.__4__this = reused;

            PatchBridge.ShootMoveNextExit(borrow, iterator, null);

            // neither our applied values nor the original's snapshot may appear on the new object
            AssertDistinctive(reused);
        }

        [Fact]
        public void EnableWithReusedPointer_RetiresTheStaleReceiptAndLeavesTheNewObjectUntouched()
        {
            Fixture f = Rate(2f);
            Archer original = f.NewArcher();

            // outstanding receipt: the prefix applies, the return write fails
            CoroutineRun failed = f.MoveNext(original, duringNative: () => original.ThrowOnPrepWrite = true);
            Assert.True(failed.BorrowEntered);
            Assert.Equal(0.2f, original.shootPrepTime, 5);           // our applied value, receipt in place

            Archer reused = f.NewArcher();
            // the fresh object coincidentally holds exactly our applied values: only identity may stop the write
            reused.shootPrepTime = 0.2f;
            reused._shootIntervalRange = new Vector2(0.175f, 0.325f);
            reused._shootIntervalRangeFormation = new Vector2(0.25f, 0.45f);

            IntPtr pointer = original.Pointer;
            original.DestroyForTests();
            reused.SetPointerForTests(pointer);                      // another GameObject now lives at that address

            PatchBridge.RaiseArcherOnEnable(reused);                 // new life at a reused address

            Assert.Equal(0.2f, reused.shootPrepTime, 5);             // stale receipt retired: nothing was written
            Assert.Equal(new Vector2(0.175f, 0.325f), reused._shootIntervalRange);
            Assert.Equal(new Vector2(0.25f, 0.45f), reused._shootIntervalRangeFormation);

            CoroutineRun after = f.MoveNext(reused);                 // the address is free again: borrowing works
            Assert.True(after.BorrowEntered);
            Assert.Equal(0.1f, after.ReadPrep, 5);                   // 0.2 / 2
        }
    }
}
