using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 窄伤害替代：只替代「当前命中事务 + 同一直接 Damageable」的那一次 TryDamage；
    /// 分支与原生逐字等价（除持续灼烧）、免疫返回值、perfect 倍率、其它目标走原生、
    /// 替代失败不回落原生 DOT、重入不双算、任何路径都不改写原生字段。
    /// </summary>
    public sealed class DamageSubstitutionTests : ImpactTestBase
    {
        private (Squad squad, Foe direct, Arrow arrow) Armed()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);
            return (squad, direct, arrow);
        }

        [Fact]
        public void SubstitutedBranch_MatchesNativeDirectDamage_WithoutDot()
        {
            (_, Foe direct, Arrow arrow) = Armed();
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.EndResult);
            Assert.Equal(1, PatchArcher_GreekImpact.StatSubstituted);
            Assert.Equal(1, direct.Damage);                    // 与原生 hitDamage 相同
            Assert.Equal(DamageSource.Arrow, direct.Damageable.ReceivedSources[0]);
            Assert.Equal(0, direct.DotTicks);                  // 唯一差异：无持续灼烧
        }

        [Fact]
        public void PerfectShot_UsesNativeMultiplier()
        {
            (_, Foe direct, Arrow arrow) = Armed();
            arrow._perfect = true;

            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.EndResult);
            Assert.Equal(2, direct.Damage);                    // hitDamage(1) * perfectDamageMultiplier(2)
            Assert.Equal(0, direct.DotTicks);
        }

        [Fact]
        public void SubstituteOnImmuneTarget_ReturnsTrueWithoutDamage()
        {
            (Squad squad, Foe direct, Arrow arrow) = Armed();
            direct.Damageable.Immune = true;
            PatchArcher_GreekImpact.HitTicket ticket = PatchArcher_GreekImpact.BeginHit(arrow, direct.Go);
            Assert.True(ticket.Valid);

            // 只有当前事务的直接目标会被替代（这里正是它）
            bool result = Harness.DispatchTryDamage(arrow, direct.Damageable);

            Assert.True(result);                               // 与原生 `return true` 一致
            Assert.Equal(0, direct.Damage);
            Assert.Equal(0, direct.DotTicks);
            PatchArcher_GreekImpact.AbortHit(arrow, ticket);
        }

        [Fact]
        public void ForeignTarget_RunsNativePath()
        {
            (_, Foe direct, Arrow arrow) = Armed();
            Foe bystander = Harness.SpawnFoe(World, 3f);

            PatchArcher_GreekImpact.HitTicket ticket = PatchArcher_GreekImpact.BeginHit(arrow, direct.Go);
            Assert.True(ticket.Valid);

            bool result = Harness.DispatchTryDamage(arrow, bystander.Damageable);   // 非本事务目标

            Assert.True(result);
            Assert.True(bystander.DotTicks > 0);               // 原生路径照跑（含 DOT）
            Assert.Equal(1, bystander.Damage);
            PatchArcher_GreekImpact.AbortHit(arrow, ticket);
        }

        [Fact]
        public void NoTransaction_TryDamageAlwaysRunsNative()
        {
            (_, _, Arrow arrow) = Armed();
            Foe foe = Harness.SpawnFoe(World, 0f);

            bool result = Harness.DispatchTryDamage(arrow, foe.Damageable);

            Assert.True(result);
            Assert.True(foe.DotTicks > 0);                     // 无事务 → 原生 DOT 照旧
            Assert.Equal(0, PatchArcher_GreekImpact.StatSubstituted);
        }

        [Fact]
        public void DirectDamageThrows_NoNativeFallbackAndNoAoE()
        {
            (_, Foe direct, Arrow arrow) = Armed();
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            direct.Damageable.OnReceive = _ => throw new InvalidOperationException("boom");

            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.Threw);                        // 异常沿 HitObject Finalizer 上抛
            Assert.True(arrow.FireFlagAtTryDamage);            // 原生字段仍未改写
            Assert.Equal(1, direct.Damage);                    // 只应用了一次直接伤害
            Assert.Equal(0, direct.DotTicks);                  // 绝不回落原生 DOT
            Assert.Equal(0, neighbour.Damage);                 // 失败事务不 AoE
            Assert.True(PatchArcher_GreekImpact.StatSubstituteFailed >= 1);
        }

        [Fact]
        public void ReentrantTryDamage_SameTarget_NoDoubleCountNoNativeDot()
        {
            (_, Foe direct, Arrow arrow) = Armed();
            bool reentered = false;
            direct.Damageable.OnReceive = _ =>
            {
                if (reentered) return;
                reentered = true;
                Harness.DispatchTryDamage(arrow, direct.Damageable);   // 同一箭/同一目标重入
            };

            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.EndResult);
            Assert.True(reentered);
            Assert.Equal(1, direct.Damage);                    // 不双算
            Assert.Equal(0, direct.DotTicks);                  // 不回落原生 DOT
            Assert.True(PatchArcher_GreekImpact.StatSubstituteReentry >= 1);
        }

        [Fact]
        public void AllPaths_LeaveNativeFieldsUntouched()
        {
            (_, Foe direct, Arrow arrow) = Armed();
            int ticks = arrow.damageTicks, perTick = arrow.damagePerTick;
            float delayTime = arrow.damageDelayTime, delayOffset = arrow.damageDelayOffset;
            int hitDamage = arrow.hitDamage, perfectMultiplier = arrow.perfectDamageMultiplier;
            DamageSource source = arrow._damageSource;
            bool authority = arrow.authorityActive;
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.True(arrow.isFireArrow);
            Assert.Equal(ticks, arrow.damageTicks);
            Assert.Equal(perTick, arrow.damagePerTick);
            Assert.Equal(delayTime, arrow.damageDelayTime);
            Assert.Equal(delayOffset, arrow.damageDelayOffset);
            Assert.Equal(hitDamage, arrow.hitDamage);
            Assert.Equal(perfectMultiplier, arrow.perfectDamageMultiplier);
            Assert.Equal(source, arrow._damageSource);
            Assert.Equal(authority, arrow.authorityActive);
            Assert.True(arrow.FireFlagAtTryDamage);            // TryDamage 当刻仍是原生 true
            Assert.Equal(1, neighbour.Damage);
        }

        [Fact]
        public void DestroyedObjects_BehaveAsNull()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(arrow != null);
            arrow.DestroySelf();
            Assert.True(arrow == null);                        // Unity 销毁语义：== null
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PatchArcher_GreekImpact.HitTicket ticket = PatchArcher_GreekImpact.BeginHit(arrow, null);
            Assert.False(ticket.Valid);
            for (int i = 0; i < 60; i++) PatchArcher_GreekImpact.Tick();   // 轮转清理到达该槽
            Assert.True(PatchArcher_GreekImpact.StatLeaseRetired >= 1);
        }
    }
}
