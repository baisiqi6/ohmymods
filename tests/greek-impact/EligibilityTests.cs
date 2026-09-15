using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 资格判定：配置 / style3 / 双方 FireAttacks 窗口 / 所属关系 / prefab / world 权威。
    /// 资格在发射时记录，中途过期不取消已发射的 shot。
    /// </summary>
    public sealed class EligibilityTests : ImpactTestBase
    {
        [Fact]
        public void ConfigOff_KeepsVanillaBehaviour()
        {
            ModConfig.ArcherImpactEnabled.Value = false;
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.False(outcome.TicketValid);                 // 不借值 → 不改写 flag
            Assert.True(arrow.FireFlagAtTryDamage);            // native 灼烧路径保持原样
            Assert.Equal(3, direct.DotTicks);                  // 原版 DOT 未被取消
            Assert.Equal(0, neighbour.Damage);                 // 无 AoE
            Assert.Equal(0, Physics2D.QueryCount);             // 关闭路径不做查询
        }

        [Fact]
        public void ConfigOn_EligibleFireArrow()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.TicketValid);
            Assert.True(arrow.FireFlagAtTryDamage);            // 原生字段从未被改写
            Assert.True(outcome.EndResult);                    // EndHit 返回 accepted
            Assert.Equal(0, direct.DotTicks);                  // 持续灼烧被取消
            Assert.Equal(1, direct.Damage);                    // 直接伤害保留
            Assert.Equal(1, neighbour.Damage);                  // 邻近敌人 1 点
            Assert.Equal(1, neighbour.FireHits);                // 伤害来源为 Fire
        }

        [Theory]
        [InlineData("style2")]
        [InlineData("knightWindow")]
        [InlineData("archerWindow")]
        [InlineData("notFollower")]
        [InlineData("deadArcher")]
        [InlineData("inertArcher")]
        public void GatingConditions_MakeShotIneligible(string mode)
        {
            Squad squad = mode switch
            {
                "style2" => Harness.BuildSquad(World, style3: false),
                "knightWindow" => Harness.BuildSquad(World, knightWindow: false),
                "archerWindow" => Harness.BuildSquad(World, archerWindow: false),
                "notFollower" => Harness.BuildSquad(World, withKnight: false),
                "deadArcher" => Harness.BuildSquad(World, living: false),
                _ => Harness.BuildSquad(World)
            };
            if (mode == "inertArcher") squad.ArcherCharacter.inert = true;

            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, Physics2D.QueryCount);
        }

        [Fact]
        public void NonFirePrefab_NotEligible()
        {
            Squad squad = Harness.BuildSquad(World);
            squad.Attack._arrowPrefab = Harness.BuildArrowPrefab(isFire: false);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, neighbour.FireHits);
        }

        [Fact]
        public void ForeignArrowAttack_NotEligible()
        {
            Squad squad = Harness.BuildSquad(World);
            ArrowAttack other = new ArrowAttack { _arrowPrefab = Harness.BuildArrowPrefab(isFire: true) };

            Volley volley = squad.OpenVolley(other);
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));   // 非该弓手 _fireArrowAttack
        }

        [Fact]
        public void AuthorityAndScopeGates()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));

            // 客户端（无世界权威）：不改写资格记录，但命中/AoE 路径整体失效
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            NetworkBigBoss.HasWorldAuth = false;
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);
            Assert.False(outcome.TicketValid);
            Assert.Equal(0, neighbour.Damage);

            NetworkBigBoss.HasWorldAuth = true;
            Env.DisableScope();                                              // 无可用世界
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
        }

        [Fact]
        public void ZeroTimeScale_NotEligible()
        {
            Squad squad = Harness.BuildSquad(World);
            Time.timeScale = 0f;                                             // 暂停
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
        }

        [Fact]
        public void BuffExpiresMidFlight_ShotStillApplies()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            squad.SetWindow(knightOpen: false, archerOpen: false);           // 空中过期
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.TicketValid);
            Assert.Equal(0, direct.DotTicks);                                // 仍取消灼烧
            Assert.Equal(1, neighbour.Damage);                               // 仍爆发
            Assert.Equal(1, neighbour.FireHits);
        }

        [Fact]
        public void WindowExpired_NewShotIneligible()
        {
            Squad squad = Harness.BuildSquad(World);
            squad.SetWindow(knightOpen: false, archerOpen: false);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, neighbour.FireHits);
        }

        [Fact]
        public void NonFireArrowShotWhileWindowOpen_NotEligible()
        {
            Squad squad = Harness.BuildSquad(World);
            Arrow plain = Harness.BuildArrowPrefab(isFire: false);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire(plain);                                 // 同 scope 内的非火 prefab
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, neighbour.FireHits);
        }

        [Fact]
        public void ArrowOutsideAnyVolley_NotEligible()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            // 不在 FireArrowInternal scope 内被 spawn 的火箭（例如别的系统直接 Pool.Spawn）
            Arrow stray = Harness.SpawnStrayArrow(squad.Source, Harness.BuildArrowPrefab(isFire: true), World);

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(stray));
            PlaceAt(stray, 0f);
            Harness.SimulateHit(stray, direct.Go);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, neighbour.FireHits);
            Assert.Equal(0, Physics2D.QueryCount);
        }
    }
}
