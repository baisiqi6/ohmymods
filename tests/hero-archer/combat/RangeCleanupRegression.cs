using KingdomEnhancedMod;
using UnityEngine;
using Xunit;
namespace KingdomArcherOptions.Combat.Tests;
public class RangeCleanupRegression
{
    private static Archer Arm(Fixture f)
    {
        var a=f.NewArcher(); a._arrowAttack=new ArrowAttack{_shotMagnitude=12,_boostedShotMagnitude=12,_arrowGravity=-30};
        a.ActiveArrowAttack=a._arrowAttack; a._fireArrowAttack=null; a.shootRange=10; a._enemyScanner=new Scanner{range=8,rangeBehind=8};return a;
    }
    [Fact] public void ConfirmedDestroyedOwnerReleasesCloneCapacity()
    {
        var f=new Fixture();var a=Arm(f);Assert.True(HeroArcherRange.Apply(a)); a.Owner=null;
        HeroArcherRange.Clear(); Assert.Equal(0,HeroArcherRange.PendingCleanupCount);
        Assert.True(HeroArcherRange.Apply(Arm(f)));Assert.True(HeroArcherRange.Apply(Arm(f)));
    }
    [Fact] public void FirstPendingOwnerCannotStarveSecondRetry()
    {
        var f=new Fixture();var a=Arm(f);var b=Arm(f);
        Assert.True(HeroArcherRange.Apply(a));Assert.True(HeroArcherRange.Apply(b));
        a._enemyScanner.ThrowOnRangeWrite=true;b._enemyScanner.ThrowOnRangeWrite=true;
        HeroArcherRange.Restore(a);HeroArcherRange.Restore(b);Assert.Equal(2,HeroArcherRange.PendingCleanupCount);
        b._enemyScanner.ThrowOnRangeWrite=false;HeroArcherRange.RetryCleanup();Assert.Equal(1,HeroArcherRange.PendingCleanupCount);
        a._enemyScanner.ThrowOnRangeWrite=false;HeroArcherRange.RetryCleanup();Assert.Equal(0,HeroArcherRange.PendingCleanupCount);
    }
    [Fact] public void LaterNativeFireAssetAndForeignActiveAreDetected()
    {
        var f=new Fixture();var a=Arm(f);Assert.True(HeroArcherRange.Apply(a));
        a._fireArrowAttack=new ArrowAttack{_shotMagnitude=7};Assert.False(HeroArcherRange.Tick(a));HeroArcherRange.Restore(a);
        var b=Arm(f);Assert.True(HeroArcherRange.Apply(b));b.ActiveArrowAttack=new ArrowAttack{_shotMagnitude=9};
        Assert.False(HeroArcherRange.Tick(b));var foreign=b.ActiveArrowAttack;HeroArcherRange.Restore(b);Assert.Same(foreign,b.ActiveArrowAttack);
    }
}
