using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 账本/池复用/世代/容量回归：
    /// GO 与组件身份区分、ReleaseAll 后旧 ticket、伤害回调中的池复用、
    /// 借值归还（含 setter 部分成功后抛错）、非 accepted 命中、world/配置失效、容量保守降级。
    /// </summary>
    public sealed class LedgerTests : ImpactTestBase
    {
        [Fact]
        public void ComponentPointerDiffersFromGoPointer_ArcherEnableInvalidates()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));

            // 组件 Pointer（Archer）与 GO Pointer 明确不同：旧实现混用两者永不匹配
            Assert.NotEqual(squad.Archer.Pointer, squad.Archer.gameObject.Pointer);

            PatchArcher_GreekImpact.OnArcherEnable(squad.Archer);     // 既有 Archer.OnEnable Prefix 接入点

            Assert.True(PatchArcher_GreekImpact.StatArcherInvalidated >= 1);
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            Harness.SimulateHit(arrow, squad.Source);
            Assert.Equal(0, neighbour.Damage);
        }

        [Fact]
        public void ArcherEnable_AlsoInvalidatesOpenVolley()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley volley = squad.OpenVolley();
            Arrow first = volley.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(first));

            PatchArcher_GreekImpact.OnArcherEnable(squad.Archer);     // scope 仍开着，但 source 已复用

            Arrow later = volley.Fire();                              // scope 内尚未 spawn 的箭
            Assert.True(PatchArcher_GreekImpact.StatSourceInvalidated >= 1);
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(later));
            volley.Close();
        }

        [Fact]
        public void ReleaseAllThenReusedSlot_StaleFinalizerDoesNotTouchNewScope()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley first = squad.OpenVolley();
            Arrow firstArrow = first.Fire();
            PlaceAt(firstArrow, 0f);
            PatchArcher_GreekImpact.ShotTicket stale = first.Scope;

            PatchArcher_GreekImpact.ReleaseAll();                    // 旧 scope/lease/ticket 全部失效
            Volley second = squad.OpenVolley();                      // 复用同一个 volley 槽
            Arrow secondArrow = second.Fire();
            PlaceAt(secondArrow, 0f);

            PatchArcher_GreekImpact.EndShot(stale);                  // 迟到的旧 Finalizer

            Assert.True(PatchArcher_GreekImpact.StatStaleTicket >= 1);
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(secondArrow));   // 新 scope 未被误关
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Harness.SimulateHit(secondArrow, direct.Go);
            Assert.Equal(1, neighbour.Damage);
            second.Close();
        }

        [Fact]
        public void PoolReuseInsideDamageCallback_DoesNotDamageOrRetireNewLife()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            bool recycled = false;
            direct.Damageable.OnReceive = _ =>
            {
                if (recycled) return;
                recycled = true;
                // 直接伤害回调里这只箭被池复用：OnEnable 退役旧世代 → 新 shot 绑定新 lease
                PatchArcher_GreekImpact.OnArrowEnable(arrow);
                arrow.OnEnableBody();
                Volley again = squad.OpenVolley();
                PatchArcher_GreekImpact.OnArrowSpawned(arrow);
                arrow.archer = squad.Source;
                again.Close();
            };

            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.True(recycled);
            Assert.True(arrow.isFireArrow);
            Assert.False(outcome.EndResult);                                  // 旧 ticket 已过期（箭被复用）
            Assert.True(PatchArcher_GreekImpact.StatStaleTicket >= 1);
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));      // 新世代 lease 未被旧事务撤掉
        }

        [Fact]
        public void NonAcceptedHit_EndsTransactionAndKeepsLease()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            GameObject plain = new GameObject("plain", Env.EnemiesLayer);
            plain.transform.parent = World.transform;
            HitOutcome outcome = Harness.SimulateHit(arrow, plain);

            Assert.True(outcome.TicketValid);
            Assert.False(arrow._hasHit);                           // native 未接受
            Assert.True(arrow.isFireArrow);
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));   // 箭仍在飞 → lease 保留
            Assert.Equal(0, neighbour.Damage);

            Foe direct = Harness.SpawnFoe(World, 0f);
            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(1, neighbour.Damage);                     // 后续命中仍生效
        }

        [Fact]
        public void AuthorityFalse_NoBorrowNoBurst()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);
            arrow.authorityActive = false;                          // native TryDamage 的同一前提

            HitOutcome outcome = Harness.SimulateHit(arrow, direct.Go);

            Assert.False(outcome.TicketValid);
            Assert.True(arrow.isFireArrow);
            Assert.Equal(0, direct.DotTicks);                       // native 自己也不会灼烧
            Assert.Equal(0, direct.Damage);
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, Physics2D.QueryCount);
        }

        [Fact]
        public void WorldChangeBeforeTick_WithdrawsLedgerAndBlocksDamage()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));

            GameObject world2 = Env.NewWorld(2);                    // 换 world（Tick 尚未跑）
            Foe foe = Harness.SpawnFoe(world2, 0f);

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));   // 入口自带撤账
            Harness.SimulateHit(arrow, foe.Go);

            Assert.Equal(1, foe.Damage);                            // 只有原版直接伤害
            Assert.Equal(0, foe.FireHits);
            Assert.Equal(3, foe.DotTicks);                          // 未借值 → 原版灼烧保留
        }

        [Fact]
        public void ConfigDisable_ReleasesStateAndRestoresVanilla()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            ModConfig.ArcherImpactEnabled.Value = false;
            PatchArcher_GreekImpact.Tick();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            Harness.SimulateHit(arrow, direct.Go);
            Assert.Equal(3, direct.DotTicks);                       // 回到原版战斗
            Assert.Equal(0, neighbour.Damage);
            Assert.Equal(0, Physics2D.QueryCount);
        }

        [Fact]
        public void TickWhileDisabled_TouchesNothing()
        {
            ModConfig.ArcherImpactEnabled.Value = false;
            PatchArcher_GreekImpact.Tick();
            PatchArcher_GreekImpact.Tick();

            Assert.Equal(0, Physics2D.QueryCount);
            Assert.Equal(0, PatchArcher_GreekImpact.StatBursts);
        }

        [Fact]
        public void ConcurrentShotsBeyondOldEightLimit_AllEligible()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley[] volleys = new Volley[16];
            Arrow[] arrows = new Arrow[16];
            for (int i = 0; i < volleys.Length; i++)
            {
                volleys[i] = squad.OpenVolley();
                arrows[i] = volleys[i].Fire();
            }
            foreach (Arrow arrow in arrows) Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));
            Assert.Equal(0, PatchArcher_GreekImpact.StatScopeCap);
            foreach (Volley volley in volleys) volley.Close();
        }

        [Fact]
        public void DeepConcurrency_MasksInsteadOfInheriting()
        {
            Squad squad = Harness.BuildSquad(World);
            Volley outer = squad.OpenVolley();
            Arrow outerArrow = outer.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(outerArrow));

            Volley[] fillers = new Volley[PatchArcher_GreekImpact.MaxVolleys];
            for (int i = 0; i < fillers.Length; i++) fillers[i] = squad.OpenVolley();
            Volley overflow = squad.OpenVolley();                   // 深度/容量受限 → 掩蔽
            Arrow masked = overflow.Fire();

            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(masked));
            Assert.True(PatchArcher_GreekImpact.StatStackOverflow + PatchArcher_GreekImpact.StatScopeCap >= 1);

            overflow.Close();                                       // LIFO：先关最内层
            for (int i = fillers.Length - 1; i >= 0; i--) fillers[i].Close();
            Arrow after = outer.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(after));   // 外层的后续箭恢复
            outer.Close();
        }

        [Fact]
        public void NestedIneligibleScope_MasksOuterThenRestores()
        {
            Squad squad = Harness.BuildSquad(World);
            ArrowAttack foreign = new ArrowAttack { _arrowPrefab = Harness.BuildArrowPrefab(isFire: false) };

            Volley outer = squad.OpenVolley();
            Arrow before = outer.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(before));

            Volley inner = squad.OpenVolley(foreign);               // 不合资格的内层
            Arrow duringInner = inner.Fire();
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(duringInner));   // 绝不继承外层资格
            inner.Close();

            Arrow after = outer.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(after));          // 结束恢复外层
            outer.Close();
        }

        [Fact]
        public void DeepNestingOverflow_MasksAndRecoversSymmetrically()
        {
            Squad squad = Harness.BuildSquad(World);
            int depth = PatchArcher_GreekImpact.MaxStack + 2;
            PatchArcher_GreekImpact.ShotTicket[] tickets = new PatchArcher_GreekImpact.ShotTicket[depth];
            for (int i = 0; i < depth; i++) tickets[i] = PatchArcher_GreekImpact.BeginShot(squad.Attack, squad.Source);

            Volley deepVolley = squad.OpenVolley();
            Arrow deep = deepVolley.Fire();
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(deep));
            Assert.True(PatchArcher_GreekImpact.StatStackOverflow >= 1);
            Volley extra = squad.OpenVolley();
            extra.Close();
            deepVolley.Close();

            for (int i = depth - 1; i >= 0; i--) PatchArcher_GreekImpact.EndShot(tickets[i]);

            Volley after = squad.OpenVolley();
            Arrow arrow = after.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(arrow));    // 栈对称回收
            after.Close();
        }

        [Fact]
        public void DamageCallbackDroppingAuthority_StopsRemainingTargets()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe first = Harness.SpawnFoe(World, 0.25f);
            Foe second = Harness.SpawnFoe(World, 0.26f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            first.Damageable.OnReceive = _ => NetworkBigBoss.HasWorldAuth = false;

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, first.FireHits);          // 已在回调前打出的这一下保留
            Assert.Equal(0, second.Damage);           // 权限变化后不再继续
        }

        [Fact]
        public void TargetMovedOutOfCircleDuringBurst_Skipped()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe mover = Harness.SpawnFoe(World, 0.25f);
            Foe second = Harness.SpawnFoe(World, 0.26f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            mover.Damageable.OnReceive = _ => second.Colliders[0].Center = new Vector2(5f, 0f);

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, mover.Damage);
            Assert.Equal(0, second.Damage);           // 每次实际伤害前复核 ClosestPoint 距离
        }

        [Fact]
        public void Abort_EndsTransactionWithoutAoE()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            HitOutcome outcome = Harness.SimulateHitThrowing(arrow, direct.Go);

            Assert.True(outcome.Threw);
            Assert.True(outcome.TicketValid);
            Assert.True(arrow.isFireArrow);                                   // 原生字段从未被改写
            Assert.Equal(0, neighbour.Damage);                                // 事务被清 → 无 AoE
            Assert.Equal(0, Physics2D.QueryCount);
        }

        [Fact]
        public void LeaseCapacity_ConservativelySkips_ThenFullyRecycled()
        {
            Squad squad = Harness.BuildSquad(World);
            for (int v = 0; v < PatchArcher_GreekImpact.MaxVolleys - 1; v++)
            {
                Volley volley = squad.OpenVolley();
                for (int a = 0; a < 5; a++) volley.Fire();              // 每发最多 5 箭
                volley.Close();
            }
            Assert.Equal(0, PatchArcher_GreekImpact.StatLeaseCap);

            Volley last = squad.OpenVolley();
            Arrow overflowArrow = null;
            for (int a = 0; a < 6; a++) overflowArrow = last.Fire();
            last.Close();

            Assert.True(PatchArcher_GreekImpact.StatLeaseCap >= 1);      // 超限保守失败（退回原版 DOT）
            Assert.False(PatchArcher_GreekImpact.IsEligibleArrow(overflowArrow));

            PatchArcher_GreekImpact.ReleaseAll();                        // 归还全部槽（不泄漏）

            Volley after = squad.OpenVolley();
            Arrow fresh = after.Fire();
            Assert.True(PatchArcher_GreekImpact.IsEligibleArrow(fresh));
            after.Close();
        }

    }
}
