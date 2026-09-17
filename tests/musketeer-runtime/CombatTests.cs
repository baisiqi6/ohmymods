using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 射击契约：单一确定的射速权威、显式闸（一发一颗、合法周期不被吞）、最近有效命中、
    /// 友军/飞行不阻挡、Crusher 免疫消费、消费闩先于伤害（重入不二次命中）、
    /// 射程边界、物理饱和保守终止、弹丸池容量、贴图资产缺失 fail-closed。
    /// </summary>
    internal static class CombatTests
    {
        internal static void Run()
        {
            Case.Run("failed physics segment never skips through to a later enemy", () =>
            {
                Archer actor = ArmedMusketeer(out GameObject foe);
                MusketeerCombat.TryHandleShot(actor.ActiveArrowAttack, actor.gameObject);
                Physics2D.ThrowOnCast = true;
                MusketeerCombat.Tick(0.05f, true);
                Check.Equal(0, MusketeerCombat.LiveCount, "unknown segment consumed the projectile");
                Physics2D.ThrowOnCast = false;
                Fixture.QueueHit(foe, 0.5f);
                MusketeerCombat.Tick(0.05f, true);
                Check.Equal(0, foe.GetComponent<Damageable>().DamageLog.Count, "no delayed damage past unknown front collider");
            });
            Case.Run("cadence baseline = prep + interval mid + cooldown/E[N] (sustained mean)", CadenceBaseline);
            Case.Run("E[N]: exclusive max, equal min==max, N=1 back-compat, invalid rejected", ExpectedAttempts);
            Case.Run("cadence plan: absolute 2x writes from the prefab baseline only", CadencePlan);
            Case.Run("cadence plan refuses drifted live values (never derives from modified fields)", CadenceRefusesDrift);
            Case.Run("cadence gate has a frame-quantization tolerance", CadenceGateTolerance);
            Case.Run("ground classification whitelists ground types and denies squid/boss/unknown", GroundClassification);
            Case.Run("suppression ladder: off/unknown/unarmed pass through, armed always suppresses", SuppressionLadder);
            Case.Run("armed musketeer always suppresses even without a valid target", SuppressWithoutTarget);
            Case.Run("one gate one bullet (second native call in the window emits nothing)", OneShotOneBullet);
            Case.Run("a legitimate attempt right after the gate is never swallowed", LegitimateAttemptNotSwallowed);
            Case.Run("nearest valid foe wins regardless of callback order", NearestWins);
            Case.Run("friendly unit in front is skipped and does not block", FriendlySkipped);
            Case.Run("flying foe in front is denied and does not block", FlyingSkipped);
            Case.Run("Crusher !IsStunned consumes the bullet without damage", CrusherImmunity);
            Case.Run("consumed latch prevents double damage on reentrant tick", ReentrancyLatch);
            Case.Run("bullet stops at the exact range boundary and never damages beyond it", RangeBoundary);
            Case.Run("saturated casts terminate conservatively without damage", SaturatedCasts);
            Case.Run("bullet pool holds 40+ concurrent shots without losing any", ManyConcurrentBullets);
            Case.Run("missing bullet sprite fails visible construction (no invisible damage)", MissingSpriteFailsClosed);
        }

        private static void CadenceBaseline()
        {
            // 原生持续均值：prep + 区间中值 + max(0,cooldown-reduction)/E[N]
            Check.True(MusketeerCadence.TryDeriveOrdinarySeconds(0.5d, 1.0d, 1.0d, 0d, 1.5d, out double ordinary),
                "ordinary derives");
            Check.Near(2.1666667d, ordinary, 1e-6d, "prep + mid + cooldown/E[N] (E[N]=1.5)");
            Check.Near(4.3333333d, MusketeerCadence.TargetSeconds(ordinary), 1e-6d, "target = x2 (not 5s)");
            Check.True(MusketeerCadence.TryDeriveOrdinarySeconds(0.5d, 1.0d, 1.0d, 0d, 1d, out double single),
                "N=1 derives");
            Check.Near(2.5d, single, 1e-9d, "N=1 back-compat keeps the old single-shot figure");
            Check.True(MusketeerCadence.TryDeriveOrdinarySeconds(0.5d, 1.0d, 1.0d, 0.25d, 1.5d, out double reduced),
                "reduced derives");
            Check.Near(2.0d, reduced, 1e-9d, "0.5 + 1 + 0.75/1.5");
            Check.Near(1.0d, MusketeerCadence.IntervalMidpoint(new Vector2(0.5f, 1.5f)), 1e-6d, "interval midpoint");
            Check.False(MusketeerCadence.TryDeriveOrdinarySeconds(double.NaN, 1d, 1d, 0d, 1d, out _), "NaN rejected");
            Check.False(MusketeerCadence.TryDeriveOrdinarySeconds(-1d, 1d, 1d, 0d, 1d, out _), "negative rejected");
            Check.False(MusketeerCadence.TryDeriveOrdinarySeconds(0d, 0d, 0d, 0d, 1d, out _), "zero cycle rejected");
            Check.False(MusketeerCadence.TryDeriveOrdinarySeconds(0.5d, 1d, 1d, 0d, 0.5d, out _), "E[N] < 1 rejected");
        }

        private static void ExpectedAttempts()
        {
            Check.True(MusketeerCadence.TryExpectedAttempts(1, 3, out double exclusive), "range (1,3)");
            Check.Near(1.5d, exclusive, 1e-9d, "E[N] = (min+max-1)/2 when max > min (max exclusive)");
            Check.True(MusketeerCadence.TryExpectedAttempts(1, 1, out double one), "min == max == 1");
            Check.Near(1d, one, 1e-9d, "E[N] = min when max == min");
            Check.True(MusketeerCadence.TryExpectedAttempts(3, 3, out double equal), "min == max == 3");
            Check.Near(3d, equal, 1e-9d, "equal bounds do NOT average down to 2.5");
            Check.True(MusketeerCadence.TryExpectedAttempts(1, 2, out double pair), "range (1,2)");
            Check.Near(1d, pair, 1e-9d, "only one draw possible");
            Check.True(MusketeerCadence.TryExpectedAttempts(2, 5, out double wide), "range (2,5)");
            Check.Near(3d, wide, 1e-9d, "(2+5-1)/2");
            Check.False(MusketeerCadence.TryExpectedAttempts(0, 0, out _), "zero attempts rejected");
            Check.False(MusketeerCadence.TryExpectedAttempts(-1, 4, out _), "negative min rejected");
            Check.False(MusketeerCadence.TryExpectedAttempts(3, 2, out _), "reversed bounds rejected");
        }

        private static void CadencePlan()
        {
            var prefab = new MusketeerCadenceValues(0.5f, new Vector2(0.5f, 1.5f), 1f, 0f, 1, 3);
            Check.True(MusketeerCadence.TryPlan(prefab, prefab, out MusketeerCadencePlan plan, out string reason),
                "plan accepted for an unmodified instance (" + reason + ")");
            Check.Near(1.0d, plan.PrepWrite, 1e-5d, "prep x2");
            Check.Near(2.0d, plan.IntervalWrite.x, 1e-5d, "interval collapsed to midpoint x2 (min)");
            Check.Near(2.0d, plan.IntervalWrite.y, 1e-5d, "interval collapsed to midpoint x2 (max)");
            Check.Near(1.3333333d, plan.CooldownWrite, 1e-4d, "cooldown = 2*effective/E[N]");
            Check.Near(2.1666667d, plan.OrdinarySeconds, 1e-4d, "ordinary sustained mean");
            Check.Near(4.3333333d, plan.GateSeconds, 1e-4d, "gate = 2x ordinary sustained mean");
            // 写入周期必须精确等于闸（TryPlan 内部自检的不变量，这里对外复核一次）。
            double applied = plan.PrepWrite + plan.IntervalWrite.x + (plan.CooldownWrite - prefab.Reduction);
            Check.Near(plan.GateSeconds, applied, 1e-4d, "written cycle == gate");

            // N=1 向后兼容：E[N]=1 时冷却就是 ×2（旧公式）。
            var single = new MusketeerCadenceValues(0.5f, new Vector2(0.5f, 1.5f), 1f, 0f, 1, 1);
            Check.True(MusketeerCadence.TryPlan(single, single, out MusketeerCadencePlan planN1, out _), "N=1 plan");
            Check.Near(2.0d, planN1.CooldownWrite, 1e-5d, "N=1 keeps cooldown x2");
            Check.Near(5.0d, planN1.GateSeconds, 1e-5d, "N=1 gate = 2x2.5");

            var reduced = new MusketeerCadenceValues(0.5f, new Vector2(1f, 1f), 1f, 0.25f, 1, 3);
            Check.True(MusketeerCadence.TryPlan(reduced, reduced, out MusketeerCadencePlan plan2, out _), "reduced plan");
            Check.Near(1.25d, plan2.CooldownWrite, 1e-5d, "2*0.75/1.5 + 0.25");
            Check.Near(4.0d, plan2.GateSeconds, 1e-5d, "gate follows the amortized effective cooldown");
            Check.False(MusketeerCadence.TryPlan(new MusketeerCadenceValues(0f, new Vector2(0f, 0f), 0f, 0f, 1, 3),
                new MusketeerCadenceValues(0f, new Vector2(0f, 0f), 0f, 0f, 1, 3), out _, out string bad), "zero cycle refused");
            Check.Equal("baseline-invalid", bad, "refusal reason");

            // attempts 非法（0 次 / 逆序）→ 整包 refuse，绝不猜 E[N]。
            var invalid = new MusketeerCadenceValues(0.5f, new Vector2(0.5f, 1.5f), 1f, 0f, 0, 0);
            Check.False(MusketeerCadence.TryPlan(invalid, invalid, out _, out string attemptsReason), "invalid attempts refused");
            Check.Equal("attempts-invalid", attemptsReason, "attempts refusal reason");
        }

        private static void CadenceRefusesDrift()
        {
            var prefab = new MusketeerCadenceValues(0.5f, new Vector2(0.5f, 1.5f), 1f, 0f, 1, 3);
            var drifted = new MusketeerCadenceValues(0.9f, new Vector2(0.5f, 1.5f), 1f, 0f, 1, 3);
            Check.False(MusketeerCadence.TryPlan(prefab, drifted, out _, out string reason), "drifted live refused");
            Check.Equal("live-drift", reason, "drift reason");

            var driftedAttempts = new MusketeerCadenceValues(0.5f, new Vector2(0.5f, 1.5f), 1f, 0f, 1, 1);
            Check.False(MusketeerCadence.TryPlan(prefab, driftedAttempts, out _, out string attemptReason),
                "drifted attempts refused");
            Check.Equal("live-drift", attemptReason, "attempts drift reason");
        }

        private static void CadenceGateTolerance()
        {
            float deadline = MusketeerCadence.GateDeadline(10f, 5f);
            Check.Near(14.95d, deadline, 1e-4d, "deadline = now + gate - tolerance");
            Check.True(MusketeerCadence.GateToleranceSeconds > 0d, "tolerance is positive");
        }

        private static void GroundClassification()
        {
            Fixture.Reset();
            Check.True(MusketeerFoeFilter.IsGroundEnemyType(EnemyType.TrollWeak), "troll weak is ground");
            Check.True(MusketeerFoeFilter.IsGroundEnemyType(EnemyType.Crusher), "crusher is ground");
            Check.True(MusketeerFoeFilter.IsGroundEnemyType(EnemyType.Ogre), "ogre is ground");
            Check.False(MusketeerFoeFilter.IsGroundEnemyType(EnemyType.Squid), "squid denied");
            Check.False(MusketeerFoeFilter.IsGroundEnemyType(EnemyType.Boss), "boss denied (unverified)");
            Check.False(MusketeerFoeFilter.IsGroundEnemyType(EnemyType.GauntletBoss), "gauntlet boss denied");

            GameObject ground = Fixture.NewEnemy(EnemyType.TrollMedium);
            GameObject squid = Fixture.NewEnemy(addSquidComponent: true);
            GameObject structure = Fixture.NewEnemy(addEnemyComponent: false);
            Check.True(MusketeerFoeFilter.IsGroundClassified(ground), "ground enemy classified");
            Check.False(MusketeerFoeFilter.IsGroundClassified(squid), "squid component denied");
            Check.True(MusketeerFoeFilter.IsGroundClassified(structure), "static enemy structure has no flight");
        }

        private static void SuppressionLadder()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();

            Archer unmarked = Fixture.NewArcher();
            Check.False(MusketeerCombat.TryHandleShot(unmarked._arrowAttack, unmarked.gameObject),
                "unmarked archer passes through to the native arrow");

            Archer markedUnarmed = Fixture.NewArcher();
            MusketeerIdentity.Units.Add(markedUnarmed);
            Check.True(MusketeerRuntime.IsMusketeer(markedUnarmed), "identity is visible before the package applies");
            Check.False(MusketeerCombat.TryHandleShot(markedUnarmed._arrowAttack, markedUnarmed.gameObject),
                "unarmed musketeer passes through (no package, no fake suppression)");

            MusketeerRuntime.Tick();
            Check.True(MusketeerRuntime.IsArmedMusketeer(markedUnarmed), "package applied");
            Check.True(MusketeerCombat.TryHandleShot(markedUnarmed._arrowAttack, markedUnarmed.gameObject),
                "armed musketeer suppresses the native arrow");

            MusketeerAccess.EnabledValue = false;
            Check.False(MusketeerCombat.TryHandleShot(markedUnarmed._arrowAttack, markedUnarmed.gameObject),
                "feature off restores the native arrow");
        }

        private static void SuppressWithoutTarget()
        {
            Archer archer = ArmedMusketeer(out _);
            archer._shootingTarget = null;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject),
                "suppression is unconditional for an armed musketeer");
            Check.Equal(0, MusketeerCombat.LiveCount, "no bullet without a valid ground target");
        }

        private static void OneShotOneBullet()
        {
            Archer archer = ArmedMusketeer(out _);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "first call suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "one bullet per gate");

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "second call suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "no extra bullet inside the same gate window");

            Time.time = 4.28f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "still suppressed");
            Check.Equal(1, MusketeerCombat.LiveCount, "gate still closed just before the deadline (4.2833s)");

            Time.time = 4.29f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(2, MusketeerCombat.LiveCount, "gate open right after the deadline emits exactly one more bullet");
        }

        private static void LegitimateAttemptNotSwallowed()
        {
            Archer archer = ArmedMusketeer(out _);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            Check.Equal(1, MusketeerCombat.LiveCount, "first shot");

            // 原生确定周期恰为闸值：帧量化可能让它早到几十毫秒 —— 在容差内必须放行，绝不吞掉整周期。
            Time.time = 4.284f;   // deadline = 4.3333 - 0.05 = 4.2833
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(2, MusketeerCombat.LiveCount, "a legitimate doubled-cadence attempt is not swallowed");

            // 周期内第二次原生调用（同一 volley 的残留）仍然被压制成不发弹。
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            Check.Equal(2, MusketeerCombat.LiveCount, "duplicate native callback cannot create a duplicate bullet");
        }

        private static void NearestWins()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            GameObject near = Fixture.NewEnemy();
            GameObject far = target;
            Fixture.QueueHit(far, 1.4f);      // 与距离无关的顺序
            Fixture.QueueHit(near, 0.8f);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, near.GetComponent<Damageable>().DamageLog.Count, "nearest foe takes the hit");
            Check.Equal(0, far.GetComponent<Damageable>().DamageLog.Count, "no pierce through to the farther foe");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the first valid foe");
            Check.Equal("2:Arrow", near.GetComponent<Damageable>().DamageLog[0], "damage 2 from DamageSource.Arrow");
        }

        private static void FriendlySkipped()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            var friendlyGo = new GameObject("peasant");
            friendlyGo.layer = 11;   // Citizens
            friendlyGo.AddComponent<Collider2D>();
            friendlyGo.AddComponent<Character>();
            friendlyGo.AddComponent<Damageable>();
            Fixture.QueueHit(friendlyGo, 0.5f);
            Fixture.QueueHit(target, 1.0f);

            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, friendlyGo.GetComponent<Damageable>().DamageLog.Count, "friendly unit never takes the hit");
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count, "bullet continues to the foe behind");
        }

        private static void FlyingSkipped()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            GameObject squid = Fixture.NewEnemy(addSquidComponent: true);
            Fixture.QueueHit(squid, 0.5f);
            Fixture.QueueHit(target, 1.0f);

            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, squid.GetComponent<Damageable>().DamageLog.Count, "flying foe denied");
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count, "ground foe behind still hit");
        }

        private static void CrusherImmunity()
        {
            Archer archer = ArmedMusketeer(out _);
            GameObject crusher = NewCrusher(stunned: false);
            Fixture.QueueHit(crusher, 0.6f);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, crusher.GetComponent<Damageable>().DamageLog.Count, "!IsStunned crusher takes no damage");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet still consumed");

            Time.time = 5.0f;
            Physics2D.QueuedHits.Clear();
            GameObject stunned = NewCrusher(stunned: true);
            Fixture.QueueHit(stunned, 0.6f);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, stunned.GetComponent<Damageable>().DamageLog.Count, "stunned crusher takes damage");
        }

        private static void ReentrancyLatch()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Damageable damageable = target.GetComponent<Damageable>();
            damageable.OnReceiveDamage = (multiplier, damager, source) =>
            {
                // 伤害回调里重入推进：消费闩已生效，绝不可能二次命中。
                MusketeerCombat.Tick(0.016f, true);
            };
            Fixture.QueueHit(target, 0.6f);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, damageable.DamageLog.Count, "exactly one damage application");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet removed before the damage callback");
        }

        private static void RangeBoundary()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            Check.Equal(1, MusketeerCombat.LiveCount, "bullet in flight");

            // 30 u/s、射程 12 → 每次 0.05s 前进 1.5：7 次后 10.5（仍在飞）。
            for (int i = 0; i < 7; i++) MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, MusketeerCombat.LiveCount, "still alive at 10.5 / 12");

            // 第 8 次：本段 10.5→12.0（长 1.5）。距离 1.5 的命中 = 恰好射程边界 → 有效。
            Fixture.QueueHit(target, 1.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count, "a foe at the exact range boundary is hit");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed at the boundary");

            // 越界证据永远不被采信：段外命中直接跳过（不伤害、不阻挡）。
            Time.time = 5.0f;
            Physics2D.QueuedHits.Clear();
            GameObject beyond = Fixture.NewEnemy();
            Fixture.QueueHit(beyond, 9f);   // 远超本段长度（1.5）
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, beyond.GetComponent<Damageable>().DamageLog.Count, "out-of-segment hit never damages");
            Check.Equal(1, MusketeerCombat.LiveCount, "and never blocks the bullet");
        }

        private static void SaturatedCasts()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Physics2D.Saturate = true;
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, target.GetComponent<Damageable>().DamageLog.Count, "ambiguous first-hit proof never damages");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet terminated conservatively");
            Check.Equal(256, MusketeerCombat.HitCapacity, "buffer grew to the hard limit before giving up");
            Check.True(Physics2D.CastCount >= 4, "growth re-casts instead of guessing");

            Physics2D.Saturate = false;
            Physics2D.QueuedHits.Clear();
            Physics2D.CastCount = 0;
            Time.time = 5.0f;
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count, "normal casts keep working afterwards");
            Check.Equal(1, Physics2D.CastCount, "capacities are reused, not re-grown");
        }

        private static void ManyConcurrentBullets()
        {
            Archer archer = ArmedMusketeer(out _);
            for (int i = 0; i < 40; i++)
            {
                Time.time = i * 5f;
                Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed " + i);
            }
            Check.Equal(40, MusketeerCombat.LiveCount, "40 concurrent shots are all kept (no 16-bullet cap)");
            Check.Equal(0, MusketeerCombat.SkippedFullTableShots, "nothing skipped");
        }

        private static void MissingSpriteFailsClosed()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            MusketeerCombat.SetBulletSpriteForTests(null);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject),
                "the native arrow stays suppressed even without our bullet art");
            Check.Equal(0, MusketeerCombat.LiveCount, "no projectile is constructed without a visible sprite");
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, target.GetComponent<Damageable>().DamageLog.Count, "no invisible damage");
        }

        private static Archer ArmedMusketeer(out GameObject target)
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Archer archer = Fixture.ArmMusketeer();
            target = Fixture.NewEnemy();
            archer._shootingTarget = target;
            return archer;
        }

        private static GameObject NewCrusher(bool stunned)
        {
            var go = new GameObject("crusher");
            go.layer = Fixture.EnemiesLayer;
            go.SetActive(true);
            go.AddComponent<Damageable>();
            var crusher = go.AddComponent<Crusher>();
            crusher.Type = EnemyType.Crusher;
            crusher.IsStunned = stunned;
            return go;
        }
    }
}
