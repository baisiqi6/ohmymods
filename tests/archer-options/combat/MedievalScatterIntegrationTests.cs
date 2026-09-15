using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests;

public class MedievalScatterIntegrationTests
{
    static Fixture Enabled()
    {
        var f = new Fixture();
        ModConfig.ArcherScatterEnabled.Value = true;
        ModConfig.ArcherVolleyCount.Value = 3;
        return f;
    }

    [Theory]
    [InlineData(0, 2)] [InlineData(1, 0)] [InlineData(2, 0)] [InlineData(3, 0)] [InlineData(4, 0)]
    public void ShootingPipeline_UsesOnlyMedievalLeader(int style, int extras)
    {
        var f = Enabled(); var archer = f.NewArcher(); archer._knight.StyleForTests = style;
        var main = f.Shot(archer);
        Assert.Equal(extras, f.Extras(main).Count);
        Assert.Equal(Color.white, main._spriteRenderer.color);
        Assert.Single(main.GetComponent<NetworkSoftSimulator>().LastPayload);
    }

    [Theory]
    [InlineData("wildlife")] [InlineData("friendly")] [InlineData("unassigned")]
    [InlineData("crossbow")] [InlineData("norse")]
    public void ExcludedSourcesAndTargets_StillShootOneOriginalArrow(string reason)
    {
        var f = Enabled(); var archer = f.NewArcher();
        if (reason == "wildlife") { archer._shootingTarget.layer = 9; archer._shootingTarget.tag = "Wildlife"; }
        if (reason == "friendly") archer._shootingTarget.AddComponent<FriendlyTroll>();
        if (reason == "unassigned") archer._knight = null;
        if (reason == "crossbow") archer.IsCrossbowForTests = true;
        if (reason == "norse") archer.IsNorseForTests = true;
        var main = f.Shot(archer);
        Assert.Empty(f.Extras(main)); Assert.Equal(Color.white, main._spriteRenderer.color);
        Assert.Single(main.GetComponent<Rigidbody2D>().Impulses);
    }

    [Fact]
    public void TeamChangeAffectsNextShot_ExistingExtrasKeepTheirColour()
    {
        var f = Enabled(); var archer = f.NewArcher(); var first = f.Shot(archer);
        var oldExtras = f.Extras(first); Assert.Equal(2, oldExtras.Count);
        archer._knight = f.NewArcher()._knight; archer._knight.StyleForTests = 3;
        f.AdvanceFrame(.2f); var next = f.Shot(archer);
        Assert.Empty(f.Extras(next));
        Assert.All(oldExtras, x => Assert.NotEqual(Color.white, x._spriteRenderer.color));
    }

    [Fact]
    public void PooledGoldExtra_BecomesAnOrdinaryArrowWithOriginalColour_WhenDisabled()
    {
        var f = Enabled(); var archer = f.NewArcher(); var original = f.Shot(archer);
        var extra = f.Extras(original)[0]; Assert.NotEqual(Color.white, extra._spriteRenderer.color);
        Pool.DespawnForTests(extra);
        ModConfig.ArcherScatterEnabled.Value = false;
        var nextMain = f.Shot(archer);
        Assert.Same(extra, nextMain); Assert.Equal(Color.white, nextMain._spriteRenderer.color);
        Assert.Empty(f.Extras(nextMain)); Assert.Single(nextMain.GetComponent<NetworkSoftSimulator>().LastPayload);
    }

    [Fact]
    public void GoldPayloadRoundTrip_PreservesPerfectAndColoursClientWithLocalScatterOff()
    {
        var f = Enabled(); var main = f.Shot(f.NewArcher(), perfect: true); var extra = f.Extras(main)[0];
        byte[] payload = extra.GetComponent<NetworkSoftSimulator>().LastPayload;
        Assert.Equal(6, payload.Length);
        var clientArrow = f.MakeArrowInstance();
        ModConfig.ArcherScatterEnabled.Value = false; NetworkBigBoss.HasWorldAuth = false;
        ByteBuffer.Incoming = payload; ByteBuffer.Position = 0;
        Assert.False(ScatterArrowTint.OnReceiveInitialise(clientArrow));
        Assert.True(clientArrow.Perfect); Assert.Equal(6, ByteBuffer.Position);
        Assert.Equal(extra._spriteRenderer.color, clientArrow._spriteRenderer.color);
    }
}
