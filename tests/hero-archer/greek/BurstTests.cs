using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 命中爆发：取消 native 灼烧但保留直接伤害；半径 0.25 的 collider 相交 AoE；
    /// 目标过滤、多 collider 去重、同 volley 去重、固定缓冲满载时的保守降级。
    /// </summary>
    public sealed class BurstTests : ImpactTestBase
    {
        private (Squad squad, Foe direct, Arrow arrow) Erect(float directX = 0f)
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, directX);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, directX);
            return (squad, direct, arrow);
        }

        [Fact]
        public void DirectHit_KeepsArrowDamage_SuppressesDot()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, direct.Damage);            // hitDamage 原样
            Assert.Equal(0, direct.DotTicks);          // damagePerTick/Ticks 的持续灼烧被取消
            Assert.Equal(0, direct.FireHits);          // 直接伤害来源仍是 _damageSource=Arrow
            Assert.True(arrow.isFireArrow);            // 借值已归还
        }

        [Fact]
        public void Neighbour_CircleHit_DamagedOnceWithFireSource()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, neighbour.Damage);
            Assert.Equal(1, neighbour.FireHits);
            Assert.Equal(1, PatchArcher_GreekImpact.StatBursts);
        }

        [Fact]
        public void DirectTarget_ExcludedFromBurst()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, direct.Damage);            // 只有直接伤害一次
            Assert.Equal(0, direct.FireHits);          // 不重复吃 AoE
        }

        [Fact]
        public void EdgeOverlapCounts_NotCentreDistance()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            // 中心距 0.40 > 0.25，但受击体半径 0.18 → 边缘进入爆发圈（0.40 <= 0.25 + 0.18）
            Foe edge = Harness.SpawnFoe(World, 0.40f, extent: 0.18f);
            Foe outside = Harness.SpawnFoe(World, 0.60f, extent: 0.18f);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, edge.Damage);
            Assert.Equal(0, outside.Damage);
        }

        [Fact]
        public void MultipleCollidersOnSameDamageable_Deduped()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe foe = Harness.SpawnFoe(World, 0.3f, isTrigger: false);
            Harness.AddCollider(foe, null, 0.3f, 0f, 0.01f, isTrigger: true, layer: Env.EnemiesLayer);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, foe.Damage);
            Assert.Single(foe.Damageable.ReceivedSources);
        }

        [Fact]
        public void ChildColliderWithParentDamageable_ResolvedAndDeduped()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe foe = Harness.SpawnFoe(World, 0.28f, extent: 0.01f);
            GameObject child = Harness.ChildOf(foe.Go);
            Harness.AddCollider(foe, child, 0.28f, 0f, 0.05f, isTrigger: true, layer: Env.EnemiesLayer);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, foe.Damage);
            Assert.Single(foe.Damageable.ReceivedSources);
        }

        [Fact]
        public void TriggerHitCollider_Included()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe triggerFoe = Harness.SpawnFoe(World, 0.32f, isTrigger: true);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, triggerFoe.Damage);
            Assert.True(Physics2D.LastFilter.useTriggers);          // 显式 include triggers
            Assert.True(Physics2D.LastFilter.useLayerMask);         // 只 Enemies 层
            Assert.Equal(1 << Env.EnemiesLayer, Physics2D.LastFilter.layerMask.value);
            Assert.Equal(0.25f, Physics2D.LastRadius);              // 半径 0.25
        }

        [Fact]
        public void InvulnerableTarget_NotDamaged()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe invulnerable = Harness.SpawnFoe(World, 0.3f);
            invulnerable.Damageable.invulnerable = true;

            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(0, invulnerable.Damage);
        }

        [Fact]
        public void DisabledOrForeignWorldTargets_NotDamaged()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe disabledDamageable = Harness.SpawnFoe(World, 0.3f);
            disabledDamageable.Damageable.enabled = false;
            GameObject otherRoot = new GameObject("otherWorld", 0);
            Foe foreignWorld = Harness.SpawnFoe(otherRoot, 0.3f);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(0, disabledDamageable.Damage);
            Assert.Equal(0, foreignWorld.Damage);                          // 跨 world 不复核通过
        }

        [Theory]
        [InlineData("immune")]
        [InlineData("friendlyTroll")]
        [InlineData("noEnemyComponent")]
        [InlineData("dead")]
        [InlineData("wildlifeLayer")]
        [InlineData("obstacleLayer")]
        public void FilteredTargets_NotDamaged(string mode)
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe foe = mode switch
            {
                "immune" => Harness.SpawnFoe(World, 0.3f, immune: true),
                "friendlyTroll" => Harness.SpawnFoe(World, 0.3f, friendlyTroll: true),
                "noEnemyComponent" => Harness.SpawnFoe(World, 0.3f, withEnemy: false),
                "dead" => Harness.SpawnFoe(World, 0.3f, dead: true),
                "wildlifeLayer" => Harness.SpawnFoe(World, 0.3f, layer: Env.WildlifeLayer),
                _ => Harness.SpawnFoe(World, 0.3f, layer: Env.ObstaclesLayer)
            };

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(0, foe.Damage);
        }

        [Fact]
        public void SameVolleyScatterArrows_HitNeighbourOnlyOnce()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow first = volley.Fire();
            Arrow extra = volley.Fire();          // scatter 额外箭仍在同一 scope 内
            volley.Close();
            PlaceAt(first, 0f);
            PlaceAt(extra, 0f);

            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(first));
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(extra));

            Harness.SimulateHit(first, direct.Go);
            Harness.SimulateHit(extra, direct.Go);

            Assert.Equal(1, neighbour.Damage);     // 同一原始 shot 对同一敌人最多一次
            Assert.Equal(1, neighbour.FireHits);
            Assert.True(PatchArcher_GreekImpact.StatBursts >= 1);
            Assert.True(PatchArcher_GreekImpact.StatNoTarget >= 1);        // 第二支被 volley 去重挡下
        }

        [Fact]
        public void SeparateVolleys_DamageIndependently()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Foe directA = Harness.SpawnFoe(World, 0f);
            Foe directB = Harness.SpawnFoe(World, 0f);

            Volley first = squad.OpenVolley();
            Arrow arrowA = first.Fire();
            first.Close();
            Volley second = squad.OpenVolley();
            Arrow arrowB = second.Fire();
            second.Close();
            PlaceAt(arrowA, 0f);
            PlaceAt(arrowB, 0f);

            Harness.SimulateHit(arrowA, directA.Go);
            Harness.SimulateHit(arrowB, directB.Go);

            Assert.Equal(2, neighbour.Damage);     // 去重只在同一 volley 内
        }

        [Fact]
        public void FullBuffer_ProcessesCapacityOnly_AndCounts()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            List<Foe> crowd = new List<Foe>();
            int expected = System.Math.Min(PatchArcher_GreekImpact.MaxOverlap, PatchArcher_GreekImpact.MaxVolleyTargets);
            for (int i = 0; i < PatchArcher_GreekImpact.MaxOverlap + 8; i++) crowd.Add(Harness.SpawnFoe(World, 0.05f + i * 0.002f));

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, PatchArcher_GreekImpact.StatBufferFull);       // 满载被计数
            Assert.True(PatchArcher_GreekImpact.StatVolleyCap >= 1);       // 单 volley 去重上限
            int damaged = 0;
            foreach (Foe foe in crowd) if (foe.Damage > 0) damaged++;
            Assert.Equal(expected, damaged);                               // 只处理固定容量
            Assert.Equal(expected, PatchArcher_GreekImpact.StatDamage);
        }

        [Fact]
        public void DistantFoe_Untouched()
        {
            (_, Foe direct, Arrow arrow) = Erect();
            Foe far = Harness.SpawnFoe(World, 1.0f);
            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(0, far.Damage);
            Assert.Equal(0, PatchArcher_GreekImpact.StatBursts);
        }

        [Fact]
        public void PerfectShotDamage_Unchanged()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            arrow._perfect = true;                                          // native PerfectShot 的等价状态
            PlaceAt(arrow, 0f);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(2, direct.Damage);        // hitDamage(1) * perfectDamageMultiplier(2) 不受本模块影响
            Assert.Equal(0, direct.DotTicks);
        }

        [Fact]
        public void NonDamageableTarget_NativeEarlyReturn_NoBurst()
        {
            (_, _, Arrow arrow) = Erect();
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            GameObject plain = new GameObject("plain", Env.EnemiesLayer);
            plain.transform.parent = World.transform;

            HitOutcome outcome = Harness.SimulateHit(arrow, plain);

            Assert.True(outcome.TicketValid);      // 借值过（flag 已归还）
            Assert.False(arrow._hasHit);           // native 早退，未接受命中
            Assert.Equal(0, neighbour.Damage);     // 无 AoE
            Assert.True(arrow.isFireArrow);
        }
    }
}
