using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests;

public sealed class OperatorRegressionTests : ImpactTestBase
{
    private (Squad, Arrow, Foe) Armed()
    {
        var squad = Harness.BuildSquad(World);
        var volley = squad.OpenVolley();
        var arrow = volley.Fire();
        volley.Close();
        PlaceAt(arrow, 0f);
        return (squad, arrow, Harness.SpawnFoe(World, 0f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSameLifeTicketCannotEndOrAbortNewHit(bool end)
    {
        var (_, arrow, direct) = Armed();
        var old = PatchArcher_GreekImpact.BeginHit(arrow, direct.Go);
        Assert.False(PatchArcher_GreekImpact.EndHit(arrow, old)); // native rejected: same life still flying
        var current = PatchArcher_GreekImpact.BeginHit(arrow, direct.Go);
        Assert.True(current.Valid);
        if (end) Assert.False(PatchArcher_GreekImpact.EndHit(arrow, old));
        else PatchArcher_GreekImpact.AbortHit(arrow, old);
        Harness.DispatchTryDamage(arrow, direct.Damageable);
        arrow._hasHit = true;
        Assert.True(PatchArcher_GreekImpact.EndHit(arrow, current));
        Assert.Equal(1, direct.Damage);
        Assert.Equal(0, direct.DotTicks);
    }

    [Fact]
    public void RecycledLeaseThenThrowCannotMarkNewHitFailed()
    {
        var (squad, arrow, direct) = Armed();
        var nextTarget = Harness.SpawnFoe(World, 0.1f);
        var neighbour = Harness.SpawnFoe(World, 0.3f);
        var old = PatchArcher_GreekImpact.BeginHit(arrow, direct.Go);
        PatchArcher_GreekImpact.HitTicket current = default;
        direct.Damageable.OnReceive = _ =>
        {
            PatchArcher_GreekImpact.OnArrowEnable(arrow);
            arrow.OnEnableBody();
            var volley = squad.OpenVolley();
            PatchArcher_GreekImpact.OnArrowSpawned(arrow);
            arrow.archer = squad.Source;
            volley.Close();
            current = PatchArcher_GreekImpact.BeginHit(arrow, nextTarget.Go);
            throw new InvalidOperationException("after reuse");
        };
        Assert.Throws<InvalidOperationException>(() => Harness.DispatchTryDamage(arrow, direct.Damageable));
        PatchArcher_GreekImpact.AbortHit(arrow, old);
        Assert.True(current.Valid);
        Harness.DispatchTryDamage(arrow, nextTarget.Damageable);
        arrow._hasHit = true;
        direct.Damageable.OnReceive = null;
        Assert.True(PatchArcher_GreekImpact.EndHit(arrow, current));
        Assert.Equal(0, nextTarget.DotTicks);
        Assert.Equal(1, neighbour.Damage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateChangeBetweenHitAndDamageDoesNotSubstitute(bool changeWorld)
    {
        var (squad, arrow, direct) = Armed();
        var ticket = PatchArcher_GreekImpact.BeginHit(arrow, direct.Go);
        if (changeWorld) Env.NewWorld(2); else squad.Source.activeSelf = false;
        Harness.DispatchTryDamage(arrow, direct.Damageable);
        Assert.Equal(3, direct.DotTicks); // outside qualified scope: native behavior
        Assert.Equal(0, PatchArcher_GreekImpact.StatSubstituted);
        PatchArcher_GreekImpact.AbortHit(arrow, ticket);
    }

    [Fact]
    public void FailedSpawnIdentityReadDoesNotConsumeAnyLease()
    {
        var (squad, _, _) = Armed();
        int initial = Counter("ActiveLeases");
        var volley = squad.OpenVolley();
        var go = new GameObject("badArrow", 11);
        go.transform.parent = World.transform;
        var arrow = go.AddComponent<Arrow>();
        arrow.isFireArrow = true;
        go.ThrowIdRead = true;
        PatchArcher_GreekImpact.OnArrowSpawned(arrow);
        Assert.Equal(initial, Counter("ActiveLeases"));
        go.ThrowIdRead = false;
        PatchArcher_GreekImpact.OnArrowSpawned(arrow);
        arrow.archer = squad.Source;
        Assert.Equal(initial + 1, Counter("ActiveLeases"));
        PatchArcher_GreekImpact.OnArrowSpawned(arrow); // duplicate notification cannot consume a second lease
        Assert.Equal(initial + 1, Counter("ActiveLeases"));
        volley.Close();
        Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
    }

    [Fact]
    public void SequentialClosedShotsStillInFlightRespectVolleyCapacity()
    {
        var squad = Harness.BuildSquad(World);
        int limit = (int)typeof(PatchArcher_GreekImpact).GetField("MaxVolleys", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
        for (int i = 0; i < limit; i++)
        {
            var volley = squad.OpenVolley(); var arrow = volley.Fire(); volley.Close();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
        }
        var overflow = squad.OpenVolley(); var ordinary = overflow.Fire(); overflow.Close();
        Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(ordinary));
        Assert.True(PatchArcher_GreekImpact.StatScopeCap > 0);
        PatchArcher_GreekImpact.ReleaseAll();
        var fresh = squad.OpenVolley(); var admitted = fresh.Fire(); fresh.Close();
        Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(admitted));
    }

    private static int Counter(string name) => (int)typeof(PatchArcher_GreekImpact)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
}
