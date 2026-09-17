using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomGreekImpact.Tests
{
    /// <summary>
    /// 目标生命（life token）回归：
    /// - 同一对象回池（GO 停用/启用）后新 life 可在后续 burst 再吃一次 AoE（修复「旧指针账本误去重」）；
    /// - 同一 life 同一 volley 仍然只吃一次（既有规则不变）；
    /// - 单次 burst 的物理查询快照：本次 burst 中才回池重生的新 life 不会被同一批旧 collider 再打；
    /// - 伤害回调异常只丢该目标、不重试、不阻塞其余独立目标；
    /// - life 证据不可用（marker 创建失败）时显式放弃该目标，绝不退回指针去重；
    /// - marker 只加在被 AoE 实际检查过的目标上，且不改任何 flags（保持默认，不碰目标 GO/旧组件）。
    /// 实机运行结果以 Operator 的构建/测试为准（本文件只提供可执行回归）。
    /// </summary>
    public sealed class TargetLifeTests : ImpactTestBase
    {
        [Fact]
        public void RebornFoe_NewLife_TakesOneMoreAoeInSameVolley()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe directA = Harness.SpawnFoe(World, 0f);
            Foe neighbour = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow first = volley.Fire();
            Arrow second = volley.Fire();
            volley.Close();
            PlaceAt(first, 0f);
            PlaceAt(second, 0f);

            Harness.SimulateHit(first, directA.Go);
            Assert.Equal(1, neighbour.FireHits);            // life#1 吃一次

            neighbour.Go.SetActive(false);
            neighbour.Go.SetActive(true);                   // 真实池回收：同 GO / 同指针，新 life

            Foe directB = Harness.SpawnFoe(World, 0f);
            Harness.SimulateHit(second, directB.Go);

            Assert.Equal(2, neighbour.FireHits);            // 新 life 可再吃一次（旧指针账本不再误去重）
            Assert.Equal(1, directB.Damage);                // 第二支箭的直接伤害仍是原生路径
        }

        [Fact]
        public void RecycledDuringBurst_StaleCollidersDoNotHitNewLife_LaterBurstDoes()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe first = Harness.SpawnFoe(World, 0.25f);
            Foe recycled = Harness.SpawnFoe(World, 0.3f);
            Harness.AddCollider(recycled, null, 0.3f, 0f, 0.01f, isTrigger: true, layer: Env.EnemiesLayer);

            Volley volley = squad.OpenVolley();
            Arrow arrowA = volley.Fire();
            Arrow arrowB = volley.Fire();
            volley.Close();
            PlaceAt(arrowA, 0f);
            PlaceAt(arrowB, 0f);

            bool recycledOnce = false;
            first.Damageable.OnReceive = _ =>
            {
                if (recycledOnce) return;
                recycledOnce = true;
                recycled.Go.SetActive(false);
                recycled.Go.SetActive(true);                // 在本次 burst 的回调里回池 → 新 life
            };

            Foe directB = Harness.SpawnFoe(World, 0f);
            Harness.SimulateHit(arrowA, direct.Go);

            Assert.True(recycledOnce);
            Assert.Equal(1, first.FireHits);
            Assert.Equal(0, recycled.FireHits);             // 本次 burst 的旧 collider 不打刚重生的新 life

            Harness.SimulateHit(arrowB, directB.Go);

            Assert.Equal(1, recycled.FireHits);             // 后续 burst 才按新 life 判定（同 volley 一次）
        }

        [Fact]
        public void FaultedCallback_LosesOnlyThatTarget_NoRetry_OthersStillHit()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe throwing = Harness.SpawnFoe(World, 0.25f);
            Foe after = Harness.SpawnFoe(World, 0.28f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            throwing.Damageable.OnReceive = _ => throw new System.InvalidOperationException("boom");

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(1, throwing.FireHits);             // 已提交一次，绝不重试
            Assert.Equal(1, after.FireHits);                // 后续独立目标不受影响
            Assert.Equal(1, PatchArcher_GreekImpact.StatDamageFaulted);
            Assert.Equal(1, PatchArcher_GreekImpact.StatDamage);
            Assert.Equal(1, direct.Damage);                 // 原生直接伤害不变
        }

        [Fact]
        public void MarkerUnavailable_SkipsOnlyThatTarget_NoPointerFallback()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe blocked = Harness.SpawnFoe(World, 0.24f);
            Harness.AddCollider(blocked, null, 0.24f, 0f, 0.01f, isTrigger: true, layer: Env.EnemiesLayer);
            Foe healthy = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            blocked.Go.RejectAdd = true;                    // marker 创建失败（注册/AddComponent 不可用）

            Harness.SimulateHit(arrow, direct.Go);

            Assert.Equal(0, blocked.FireHits);              // 显式放弃：不退回指针去重，也不假装命中
            Assert.True(PatchArcher_GreekImpact.StatLifeDegraded >= 1);
            Assert.Equal(1, healthy.FireHits);              // 其余目标照常各一次
            Assert.Equal(1, PatchArcher_GreekImpact.StatDamage);
            Assert.Equal(1, direct.Damage);                 // 原生直接伤害不受影响
        }

        [Fact]
        public void Marker_OnlyOnCheckedTargets_AndLeavesFlagsUntouched()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe near = Harness.SpawnFoe(World, 0.3f);
            Foe far = Harness.SpawnFoe(World, 1.0f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            Harness.SimulateHit(arrow, direct.Go);

            CombatTargetLifeMarker marker = near.Go.GetComponent<CombatTargetLifeMarker>();
            Assert.NotNull(marker);
            Assert.True(marker.Life > 0);
            Assert.Equal(HideFlags.None, marker.hideFlags);                   // 保持默认 flags（DontSave 会带额外 On/Off 语义）
            Assert.Equal(0, UnityEngine.Object.HideFlagsWrites);              // 目标 GO / 旧组件 / 自有 marker 的 flags 都未被写
            Assert.Null(far.Go.GetComponent<CombatTargetLifeMarker>());       // 圈外目标从未被检查
            Assert.Null(direct.Go.GetComponent<CombatTargetLifeMarker>());    // 直接命中被过滤，不重复 AoE
        }

        [Fact]
        public void InvalidatedStageEntry_DoesNotWasteTheLastSubmissionSlot()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f, extent: 0.02f);
            Foe[] old = new Foe[30];                                        // 账本已占 30（32 上限还剩 2）
            for (int i = 0; i < old.Length; i++)
            {
                old[i] = Harness.SpawnFoe(World, 0.02f + i * 0.004f, extent: 0.02f);
            }

            Volley volley = squad.OpenVolley();
            Arrow arrowA = volley.Fire();
            Arrow arrowB = volley.Fire();
            volley.Close();
            PlaceAt(arrowA, 0f);
            PlaceAt(arrowB, 0f);

            Harness.SimulateHit(arrowA, direct.Go);
            foreach (Foe foe in old) Assert.Equal(1, foe.FireHits);
            Assert.Equal(0, PatchArcher_GreekImpact.StatVolleyCap);

            Foe a = Harness.SpawnFoe(World, 0.16f, extent: 0.02f);           // 尾部的三个新目标
            Foe b = Harness.SpawnFoe(World, 0.18f, extent: 0.02f);
            Foe c = Harness.SpawnFoe(World, 0.20f, extent: 0.02f);
            bool moved = false;
            a.Damageable.OnReceive = _ =>
            {
                if (moved) return;
                moved = true;
                b.Colliders[0].Center = new Vector2(5f, 0f);                 // A 回调让 B 的提交前终检失效
            };

            Harness.SimulateHit(arrowB, direct.Go);

            Assert.True(moved);
            Assert.Equal(1, a.FireHits);
            Assert.Equal(0, b.FireHits);                                     // 终检失效：不提交、不记账、不占名额
            Assert.Equal(1, c.FireHits);                                     // C 仍拿到剩余的第 32 个名额
            foreach (Foe foe in old) Assert.Equal(1, foe.FireHits);
            Assert.Equal(0, PatchArcher_GreekImpact.StatVolleyCap);
        }

        [Fact]
        public void TargetMovedOutByEarlierCallback_SkippedThenNextArrowCanDamage()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe mover = Harness.SpawnFoe(World, 0.25f);      // 先提交，其回调把 victim 移出圈
            Foe victim = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrowA = volley.Fire();
            Arrow arrowB = volley.Fire();
            volley.Close();
            PlaceAt(arrowA, 0f);
            PlaceAt(arrowB, 0f);

            bool moved = false;
            mover.Damageable.OnReceive = _ =>
            {
                if (moved) return;
                moved = true;
                victim.Colliders[0].Center = new Vector2(5f, 0f);        // 移出爆发圈
            };

            Foe directB = Harness.SpawnFoe(World, 0f);
            Harness.SimulateHit(arrowA, direct.Go);

            Assert.True(moved);
            Assert.Equal(1, mover.FireHits);
            Assert.Equal(0, victim.FireHits);                // 本 burst 复核半径 → 跳过（且未记账）

            victim.Colliders[0].Center = new Vector2(0.3f, 0f);          // 恢复有效
            Harness.SimulateHit(arrowB, directB.Go);

            Assert.Equal(1, victim.FireHits);                // 下一支箭仍可命中恢复有效的同 life 目标
        }

        [Theory]
        [InlineData("friendly")]
        [InlineData("immune")]
        [InlineData("enemy-disabled")]
        [InlineData("damageable-disabled")]
        [InlineData("collider-disabled")]
        [InlineData("foreign-world")]
        public void TargetInvalidatedByEarlierCallback_NotDamaged(string mode)
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe mutator = Harness.SpawnFoe(World, 0.22f);    // 先提交，其回调让 victim 失效
            Foe victim = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrow = volley.Fire();
            volley.Close();
            PlaceAt(arrow, 0f);

            bool mutated = false;
            mutator.Damageable.OnReceive = _ =>
            {
                if (mutated) return;
                mutated = true;
                switch (mode)
                {
                    case "friendly": victim.Go.AddComponent<FriendlyTroll>(); break;
                    case "immune": victim.Damageable.Immune = true; break;
                    case "enemy-disabled": victim.Go.GetComponent<Enemy>().enabled = false; break;
                    case "damageable-disabled": victim.Damageable.enabled = false; break;
                    case "collider-disabled": victim.Colliders[0].enabled = false; break;
                    default: victim.Go.transform.parent = Env.NewWorld(2).transform; break;
                }
            };

            Harness.SimulateHit(arrow, direct.Go);

            Assert.True(mutated);
            Assert.Equal(1, mutator.FireHits);               // 先提交者已生效
            Assert.Equal(0, victim.FireHits);                // 提交前重跑完整既有过滤 → 失效目标不受伤
        }

        [Fact]
        public void AlreadyHitTargetsDoNotConsumeTheLastVolleySlot()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f, extent: 0.02f);
            Foe[] old = new Foe[PatchArcher_GreekImpact.MaxVolleyTargets - 1];       // 31 个已在 query 头部、先被结算
            for (int i = 0; i < old.Length; i++)
            {
                old[i] = Harness.SpawnFoe(World, 0.02f + i * 0.004f, extent: 0.02f);
            }

            Volley volley = squad.OpenVolley();
            Arrow arrowA = volley.Fire();
            Arrow arrowB = volley.Fire();
            volley.Close();
            PlaceAt(arrowA, 0f);
            PlaceAt(arrowB, 0f);

            Harness.SimulateHit(arrowA, direct.Go);
            foreach (Foe foe in old) Assert.Equal(1, foe.FireHits);                  // 账本已满 31（上限 32 还剩 1）
            Assert.Equal(0, PatchArcher_GreekImpact.StatVolleyCap);

            Foe fresh = Harness.SpawnFoe(World, 0.16f, extent: 0.02f);               // 排在 query 尾部的新目标
            int damageBefore = PatchArcher_GreekImpact.StatDamage;

            Harness.SimulateHit(arrowB, direct.Go);                                  // 同 volley 第二支箭

            Assert.Equal(damageBefore + 1, PatchArcher_GreekImpact.StatDamage);      // 只有尾部新目标被结算
            Assert.Equal(1, fresh.FireHits);                                         // 重复旧 collider 不挤掉最后一个额度
            foreach (Foe foe in old) Assert.Equal(1, foe.FireHits);                  // 旧目标不重复结算
            Assert.Equal(0, PatchArcher_GreekImpact.StatVolleyCap);
        }

        [Fact]
        public void MarkerObservationGap_FailsClosedUntilObservedGoCycle()
        {
            Squad squad = Harness.BuildSquad(World);
            Foe direct = Harness.SpawnFoe(World, 0f);
            Foe foe = Harness.SpawnFoe(World, 0.3f);
            Volley volley = squad.OpenVolley();
            Arrow arrowA = volley.Fire();
            Arrow arrowB = volley.Fire();
            Arrow arrowC = volley.Fire();
            volley.Close();
            PlaceAt(arrowA, 0f);
            PlaceAt(arrowB, 0f);
            PlaceAt(arrowC, 0f);

            Harness.SimulateHit(arrowA, direct.Go);
            Assert.Equal(1, foe.FireHits);

            // 单独禁用 marker（GO 仍活跃）→ 观察缺口；缺口内发生真实 GO 复用（disabled 组件收不到消息）
            CombatTargetLifeMarker marker = foe.Go.GetComponent<CombatTargetLifeMarker>();
            marker.enabled = false;
            UnityLifecycle.Invoke(marker, "OnDisable");
            foe.Go.SetActive(false);
            foe.Go.SetActive(true);
            marker.enabled = true;
            UnityLifecycle.Invoke(marker, "OnEnable");

            Foe directB = Harness.SpawnFoe(World, 0f);
            Harness.SimulateHit(arrowB, directB.Go);

            Assert.Equal(1, foe.FireHits);                   // 观察缺口 → fail-closed：不受伤、也不伪造新 life

            foe.Go.SetActive(false);                         // 现在 marker 已启用：确证完整停用/启用
            foe.Go.SetActive(true);
            Foe directC = Harness.SpawnFoe(World, 0f);
            Harness.SimulateHit(arrowC, directC.Go);

            Assert.Equal(2, foe.FireHits);                   // 恢复可靠后按新 life 正常结算
        }
    }
}
