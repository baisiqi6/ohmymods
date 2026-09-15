using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 英雄来源（hero origin）：英雄普通箭同样绑定这套 volley/lease，复用同一份 FX 门与 0.25/1 点爆发；
    /// 两个来源开关互不共享 —— Greek 关闭时非英雄希腊箭拿不到特效，Hero 关闭时飞行中的英雄箭不再爆炸。
    /// </summary>
    public sealed class HeroImpactTests : ImpactTestBase
    {
        /// <summary>
        /// 英雄小队：style2、无骑士、FireAttacks 窗口关闭（证明 hero 来源不依赖希腊资格），
        /// 默认用**普通箭** prefab。
        /// </summary>
        private Squad HeroSquad(bool combatEligible = true, bool firePrefab = false)
        {
            Squad squad = Harness.BuildSquad(World, style3: false, knightWindow: false, archerWindow: false, withKnight: false);
            if (!firePrefab) squad.Attack._arrowPrefab = Harness.BuildArrowPrefab(isFire: false);
            HeroArcherRuntime.Enabled = true;
            HeroArcherRuntime.MarkHero(squad.Archer, combatEligible);
            return squad;
        }

        [Fact]
        public void HeroNormalArrow_BindsLeaseBurstsFireDamageAndKeepsNativeDirectDamage()
        {
            Squad squad = HeroSquad();
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));       // FX 门与 AoE 用同一判定
            Assert.False(arrow.isFireArrow);                                  // 英雄普通箭
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.TicketValid);
            Assert.True(outcome.EndResult);
            Assert.Equal(1, direct.Damage);                                   // 直接伤害仍是原生
            Assert.Equal(0, PatchArcher_GreekImpact.StatSubstituted);         // 不走 Greek 灼烧替代
            Assert.Equal(1, neighbour.Damage);                                // 半径 0.25 / 1 点
            Assert.Equal(1, neighbour.FireHits);
        }

        [Fact]
        public void HeroFireArrowUnderItsOwnSwitch_CancelsNativeDot()
        {
            ModConfig.ArcherImpactEnabled.Value = false;                      // 希腊总门关闭，只有英雄开着
            Squad squad = HeroSquad(firePrefab: true);                        // 英雄在原生 FireAttacks buff 下打出真 fire 箭
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(arrow.isFireArrow);
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.TicketValid);
            Assert.Equal(1, PatchArcher_GreekImpact.StatSubstituted);         // 真 fire 箭：取消持续灼烧
            Assert.Equal(0, direct.DotTicks);                                 // 没有 delayed damage 留下
            Assert.Equal(1, direct.Damage);                                   // 直接伤害保持
            Assert.Equal(1, neighbour.Damage);
            Assert.Equal(1, neighbour.FireHits);
        }

        [Fact]
        public void HeroFireArrowWithHeroSwitchOff_FallsBackToNativeDot()
        {
            ModConfig.ArcherImpactEnabled.Value = false;
            Squad squad = HeroSquad(firePrefab: true);
            HeroArcherRuntime.Enabled = false;                                // 英雄也关掉
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.False(outcome.TicketValid);
            Assert.Equal(0, PatchArcher_GreekImpact.StatSubstituted);
            Assert.Equal(3, direct.DotTicks);                                 // 原版灼烧
            Assert.Equal(0, neighbour.Damage);
        }

        [Fact]
        public void NormalHeroArrow_KeepsNativeDirectDamageWithoutSubstitution()
        {
            Squad squad = HeroSquad();                                        // 普通箭
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(arrow.isFireArrow);
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(outcome.TicketValid);
            Assert.Equal(0, PatchArcher_GreekImpact.StatSubstituted);         // 普通箭没有 DOT 路径：保持原生
            Assert.Equal(1, direct.Damage);
            Assert.Equal(DamageSource.Arrow, direct.Damageable.ReceivedSources[0]);
            Assert.Equal(1, neighbour.Damage);
        }

        [Fact]
        public void DualEnabledHeroVolley_TakesHeroOriginAndDoesNotFallBackToGreekWhenHeroCloses()
        {
            ModConfig.ArcherImpactEnabled.Value = true;                       // 希腊 + 英雄双开
            Squad squad = Harness.BuildSquad(World);                          // style3 + 双方 FireAttacks 窗口都打开
            HeroArcherRuntime.Enabled = true;
            HeroArcherRuntime.MarkHero(squad.Archer);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(PatchArcher_GreekImpact.TryGetEligibleOrigin(arrow, out bool heroOrigin));
            Assert.True(heroOrigin);                                          // 英雄优先：该 volley 记 Hero

            HeroArcherRuntime.Enabled = false;                                // 空中关掉英雄
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));     // 不会退回希腊来源
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.False(outcome.TicketValid);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, PatchArcher_GreekImpact.StatSubstituted);
            Assert.Equal(3, direct.DotTicks);                                 // 原版灼烧
        }

        [Fact]
        public void GreekOnlyArcher_StillReportsTheGreekOrigin()
        {
            ModConfig.ArcherImpactEnabled.Value = true;
            HeroArcherRuntime.Enabled = true;                                 // 英雄门开着也不影响非英雄
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(PatchArcher_GreekImpact.TryGetEligibleOrigin(arrow, out bool heroOrigin));
            Assert.False(heroOrigin);
            PlaceAt(arrow, 0f);
            Assert.True(Harness.SimulateHit(arrow, direct.Go).TicketValid);
        }

        [Fact]
        public void HeroShotWithoutEnemyTarget_StaysVanilla()
        {
            Squad squad = HeroSquad(combatEligible: false);                   // 打猎/无敌人目标
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.False(outcome.TicketValid);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, Physics2D.QueryCount);
        }

        [Fact]
        public void GreekSwitchOff_HeroSwitchOnDoesNotActivateNonHeroGreekArrows()
        {
            ModConfig.ArcherImpactEnabled.Value = false;                      // 希腊总门关闭
            HeroArcherRuntime.Enabled = true;                                 // 英雄总门打开（不得泄漏给希腊箭）
            Squad squad = Harness.BuildSquad(World);                          // style3 + 窗口打开，但不是英雄
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(3, direct.DotTicks);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, Physics2D.QueryCount);
        }

        [Fact]
        public void HeroSwitchOn_KeepsGreekPathUnchangedForNonHeroArrows()
        {
            ModConfig.ArcherImpactEnabled.Value = true;
            HeroArcherRuntime.Enabled = true;
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(0, direct.DotTicks);                                 // 希腊替代照旧
            Assert.Equal(1, PatchArcher_GreekImpact.StatSubstituted);
            Assert.Equal(1, neighbour.Damage);
        }

        [Fact]
        public void HeroSwitchDisabledInflight_StopsBurstAndFx()
        {
            Squad squad = HeroSquad();
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));

            HeroArcherRuntime.Enabled = false;                                // 空中关闭英雄
            PlaceAt(arrow, 0f);
            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            Assert.False(outcome.TicketValid);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, neighbour.FireHits);
        }

        [Fact]
        public void HeroVolley_ExtraArrowsDoNotDamageTheSameNeighbourTwice()
        {
            Squad squad = HeroSquad();
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow main = volley.Fire();
            Arrow extra1 = volley.Fire();                                     // 同一次齐射里的散射额外箭
            Arrow extra2 = volley.Fire();
            volley.Close();

            PlaceAt(main, 0f);
            Harness.SimulateHit(main, direct.Go);
            PlaceAt(extra1, 0.02f);
            Harness.SimulateHit(extra1, direct.Go);
            PlaceAt(extra2, -0.02f);
            Harness.SimulateHit(extra2, direct.Go);

            Assert.Equal(1, neighbour.Damage);                                // 同 volley 去重
            Assert.Equal(1, PatchArcher_GreekImpact.StatBursts);
        }

        [Fact]
        public void HeroOrigin_DoesNotRequireGreekWindowsOrKnightFollower()
        {
            Squad squad = HeroSquad();                                        // style2 / 无骑士 / 窗口关闭
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);

            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();

            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            PlaceAt(arrow, 0f);
            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, neighbour.Damage);
        }
    }
}
