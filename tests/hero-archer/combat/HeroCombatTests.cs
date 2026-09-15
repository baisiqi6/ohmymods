using System;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// 英雄弓箭手覆盖层：开关隔离（英雄开关不得惠及非英雄/不得顶替中世纪开关）、
    /// 射速 max 非相乘、固定 3 箭、打猎只发主箭、事件沿既有钩子恰好转交一次、runtime 异常隔离。
    /// </summary>
    public class HeroCombatTests
    {
        /// <summary>构造夹具后登记英雄（Fixture ctor 会重置 runtime 替身，必须在之后登记）。</summary>
        private static Archer Hero(Fixture f, bool enabled = true, bool combatEligible = true)
        {
            Archer archer = f.NewArcher();
            HeroArcherRuntime.Enabled = enabled;
            HeroArcherRuntime.MarkHero(archer, combatEligible);
            return archer;
        }

        // ---------------- 纯决策：射速 / 箭数 / 来源策略 ----------------

        [Theory]
        [InlineData(1.5f, false, false, 1f)]        // 普通开关关 + 非英雄 = 1
        [InlineData(1.5f, false, true, 1.5f)]       // 普通开关关 + 英雄 = 1.5（独立于普通开关）
        [InlineData(2f, true, false, 2f)]           // 普通开关开 + 非英雄 = clamp(原倍率)
        [InlineData(2f, true, true, 2f)]            // 英雄 = max(1.5, 2) = 2，绝不 3 倍
        [InlineData(1.25f, true, true, 1.5f)]       // 普通比英雄慢时英雄仍是 1.5
        [InlineData(1f, true, true, 1.5f)]
        [InlineData(9f, true, true, 2f)]            // 越界配置按 clamp 上限 2 处理
        [InlineData(9f, false, false, 1f)]
        public void EffectiveMultiplier_UsesMaxInsteadOfProduct(float configured, bool rateSwitchOn, bool hero, float expected)
        {
            Assert.Equal(expected, HeroArcherCombat.EffectiveMultiplier(configured, rateSwitchOn, hero), 5);
        }

        [Fact]
        public void EffectiveMultiplier_TreatsInvalidConfiguredValueAsOne()
        {
            Assert.Equal(1f, HeroArcherCombat.EffectiveMultiplier(float.NaN, true, false), 5);
            Assert.Equal(1.5f, HeroArcherCombat.EffectiveMultiplier(float.NaN, true, true), 5);
            Assert.Equal(1f, HeroArcherCombat.EffectiveMultiplier(0f, true, false), 5);
        }

        [Theory]
        [InlineData(true, false, 3, 2)]      // 英雄战斗射击：即使散射开关关闭也固定 3 箭（含主箭）
        [InlineData(true, true, 1, 2)]       // 英雄不吃配置总箭数：不与中世纪散射叠加/重复
        [InlineData(false, false, 3, 0)]     // 非英雄 + 散射关 = 只有主箭
        [InlineData(false, true, 3, 2)]      // 非英雄 + 散射开 = 既有中世纪规则
        [InlineData(false, true, 1, 0)]
        [InlineData(false, true, 99, 2)]     // 越界总箭数按上限 3 处理
        public void WantedExtras_MatchesHeroAndMedievalRules(bool heroCombat, bool scatterOn, int volleyCount, int expected)
        {
            Assert.Equal(expected, HeroArcherCombat.WantedExtras(heroCombat, scatterOn, volleyCount));
        }

        [Fact]
        public void HeroSpec_IsPinnedToThreeArrowsPointTwoFiveRadiusAndOneDamage()
        {
            Assert.Equal(1.5f, HeroArcherCombat.HeroRateMultiplier, 5);
            Assert.Equal(3, HeroArcherCombat.HeroVolleyCount);
            Assert.Equal(0.25f, HeroArcherCombat.HeroBurstRadius, 5);
            Assert.Equal(1, HeroArcherCombat.HeroBurstDamage);
        }

        [Fact]
        public void OriginPolicy_OnlyHeroBindsNormalArrows()
        {
            Assert.True(HeroArcherCombat.AllowsNormalArrow(true));
            Assert.False(HeroArcherCombat.AllowsNormalArrow(false));
            // 直接伤害替代没有 origin-only 开关：由「真 fire 箭 + 该来源 lease 且来源开关开启」判定，
            // 因此英雄 fire 箭同样取消原生灼烧（见 greek-impact 套件的 hero fire 回归）。
        }

        // ---------------- 散射：英雄 3 箭 / 打猎主箭 / 非英雄隔离 ----------------

        [Fact]
        public void HeroCombatShot_SpawnsTwoExtrasOnTopOfTheNativeArrow()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f);

            Arrow main = f.Shot(hero);

            Assert.NotNull(main);
            Assert.Equal(2, f.Extras(main).Count);
            Assert.Equal(3, f.HandedOutArrows().Count);
        }

        [Fact]
        public void HeroHuntingShot_KeepsTheSingleNativeArrow()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f, combatEligible: false);   // runtime：本次目标不是敌人（打猎/无目标）

            Arrow main = f.Shot(hero);

            Assert.Single(f.HandedOutArrows());
            Assert.Empty(f.Extras(main));
        }

        [Fact]
        public void HeroSwitchOff_RevokesHeroEvenWhenTheRuntimeStillClaimsIt()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f, enabled: false);          // runtime.Enabled 关闭

            Arrow main = f.Shot(hero);

            Assert.Single(f.HandedOutArrows());
            Assert.Empty(f.Extras(main));
        }

        [Fact]
        public void HeroSwitchOn_DoesNotGiveExtraArrowsToNonHeroArchers()
        {
            Fixture f = new Fixture();
            HeroArcherRuntime.Enabled = true;               // 只开英雄总门，不开中世纪散射
            Archer plain = f.NewArcher();

            Arrow main = f.Shot(plain);

            Assert.NotNull(main);
            Assert.Single(f.HandedOutArrows());
            Assert.Empty(f.Extras(main));
        }

        [Fact]
        public void HeroSwitchOff_LeavesOnlyTheMedievalRuleInPlaceWithoutDoubleCounting()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f, enabled: false);          // 英雄门关闭
            ModConfig.ArcherScatterEnabled.Value = true;
            ModConfig.ArcherVolleyCount.Value = 3;

            Arrow main = f.Shot(hero);

            // 只剩中世纪规则本身：合计 3 箭（主箭 + 2 额外），绝不叠成英雄 2 支之外的又一份
            Assert.Equal(3, f.HandedOutArrows().Count);
            Assert.Equal(2, f.Extras(main).Count);
        }

        [Fact]
        public void HeroCombatShot_DoesNotAddMedievalExtrasOnTopOfTheHeroThree()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f);
            ModConfig.ArcherScatterEnabled.Value = true;    // 两条路径同时打开
            ModConfig.ArcherVolleyCount.Value = 3;

            Arrow main = f.Shot(hero);

            Assert.Equal(3, f.HandedOutArrows().Count);     // hero 优先，不与中世纪重复
            Assert.Equal(2, f.Extras(main).Count);
        }

        // ---------------- 射速：1.5 / max / 归还 / 非英雄不受影响 ----------------

        [Fact]
        public void HeroRate_IsOnePointFiveWhenTheNormalRateSwitchIsOff()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f);

            CoroutineRun run = f.MoveNext(hero);

            Assert.True(run.BorrowEntered);
            Assert.Equal(0.4f / 1.5f, run.ReadPrep, 5);
            Assert.Equal(new Vector2(0.35f / 1.5f, 0.65f / 1.5f), run.ReadInterval);
            Assert.Equal(new Vector2(0.5f / 1.5f, 0.9f / 1.5f), run.ReadFormation);
            Assert.Equal(0.4f, hero.shootPrepTime, 5);                       // 调用期借用已归还
        }

        [Fact]
        public void HeroRate_WithNormalTwoTimes_UsesTwoNotThree()
        {
            Fixture f = new Fixture();
            ModConfig.ArcherRateEnabled.Value = true;
            ModConfig.ArcherRateMultiplier.Value = 2f;
            Archer hero = Hero(f);

            CoroutineRun run = f.MoveNext(hero);

            Assert.True(run.BorrowEntered);
            Assert.Equal(0.2f, run.ReadPrep, 5);                             // 0.4/2，而不是 0.4/3
            Assert.Equal(new Vector2(0.175f, 0.325f), run.ReadInterval);
        }

        [Fact]
        public void HeroSwitchOn_DoesNotChangeNonHeroCadence()
        {
            Fixture f = new Fixture();
            HeroArcherRuntime.Enabled = true;
            Archer plain = f.NewArcher();

            CoroutineRun run = f.MoveNext(plain);

            Assert.False(run.BorrowEntered);                                 // 无借用
            Assert.Equal(0.4f, run.ReadPrep, 5);
            Assert.Equal(0.4f, plain.shootPrepTime, 5);
        }

        [Fact]
        public void HeroTimer_BoostsCooldownDrainByTheHeroMultiplierOnly()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f);
            Time.deltaTime = 0.05f;
            hero._cooldown = 1f;

            PatchBridge.RunArcherUpdate(hero);

            Assert.Equal(0.925f, hero._cooldown, 5);                         // native -0.05，英雄再 -0.025
        }

        [Fact]
        public void HeroTimer_IsNotDoubleScaledWhenTheNormalSwitchIsAlsoOn()
        {
            Fixture f = new Fixture();
            ModConfig.ArcherRateEnabled.Value = true;
            ModConfig.ArcherRateMultiplier.Value = 2f;
            Archer hero = Hero(f);
            Time.deltaTime = 0.05f;
            hero._cooldown = 1f;

            PatchBridge.RunArcherUpdate(hero);

            Assert.Equal(0.9f, hero._cooldown, 5);                           // 2 倍（max），不是 1.5×2
        }

        // ---------------- 事件转交与异常隔离 ----------------

        [Fact]
        public void HeroEvents_AreForwardedThroughExistingHooksExactlyOnce()
        {
            Fixture f = new Fixture();
            Archer hero = Hero(f);

            PatchBridge.RunArcherUpdate(hero);
            Assert.Equal(1, HeroArcherRuntime.ObserveCalls);

            PatchBridge.RaiseArcherOnEnable(hero);
            Assert.Equal(1, HeroArcherRuntime.OnEnableCalls);

            f.Shot(hero);
            Assert.Equal(1, HeroArcherRuntime.OnShotCalls);
        }

        [Fact]
        public void OnEnableIsForwardedEvenWhileTheSwitchIsOffSoPoolCleanupIsNeverMissed()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            HeroArcherRuntime.Enabled = false;                  // 关闭状态下发生池复用

            PatchBridge.RaiseArcherOnEnable(archer);

            Assert.Equal(1, HeroArcherRuntime.OnEnableCalls);   // 仍必须转交，否则 runtime 漏一次撤销
        }

        [Fact]
        public void HeroEvents_AreNotForwardedWhileTheSwitchIsOff()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            HeroArcherRuntime.Enabled = false;

            PatchBridge.RunArcherUpdate(archer);
            f.Shot(archer);

            Assert.Equal(0, HeroArcherRuntime.ObserveCalls);
            Assert.Equal(0, HeroArcherRuntime.OnShotCalls);
        }

        [Fact]
        public void RuntimeFailure_IsIsolatedFromNativeShotAndUpdatePaths()
        {
            Fixture f = new Fixture();
            Archer hero = f.NewArcher();
            HeroArcherRuntime.Enabled = true;
            HeroArcherRuntime.MarkHero(hero);                                // 仍登记为英雄
            HeroArcherRuntime.ThrowFrom = new InvalidOperationException("runtime exploded");

            Arrow main = f.Shot(hero);                                       // 不得外抛
            PatchBridge.RunArcherUpdate(hero);                               // 不得外抛

            Assert.NotNull(main);
            Assert.Single(f.HandedOutArrows());                              // runtime 不可用 → fail-closed，只发主箭
            Assert.Empty(f.Extras(main));
        }
    }
}
