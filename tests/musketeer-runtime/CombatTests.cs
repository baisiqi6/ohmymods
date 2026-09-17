using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 射击契约：单一确定的射速权威、显式闸（一发一颗、合法周期不被吞）、最近有效命中、
    /// 友军/飞行不阻挡、Crusher 免疫消费、消费闩先于伤害（重入不二次命中）、
    /// 射程/寿命边界与完整 dt（大帧不慢弹也不越界）、数组饱和升级完整 List（不再吞弹）、
    /// 记录池化清理与复用、共同 Submit 异常隔离、弹丸池容量、贴图资产缺失 fail-closed。
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
            Case.Run("muzzle origin carries the shared appearance scale; facing rules unchanged", MuzzleOriginCarriesAppearanceScale);
            Case.Run("nearest valid foe wins regardless of callback order", NearestWins);
            Case.Run("friendly unit in front is skipped and does not block", FriendlySkipped);
            Case.Run("flying foe in front is denied and does not block", FlyingSkipped);
            Case.Run("Crusher !IsStunned consumes the bullet without damage", CrusherImmunity);
            Case.Run("consumed latch prevents double damage on reentrant tick", ReentrancyLatch);
            Case.Run("bullet stops at the exact range boundary and never damages beyond it", RangeBoundary);
            Case.Run("array saturation escalates to the complete list and never swallows the hit", ArraySaturationEscalates);
            Case.Run("hits beyond the array cap: nearest valid foe at the list tail still wins", HugeHitListSelectsNearestAtTail);
            Case.Run("complete-list results are read by the returned count (stale tail is never re-consumed)", CompleteListUsesReturnedCount);
            Case.Run("complete-list failure consumes the bullet conservatively (never a fake hit)", CompleteListFailureConsumesWithoutFakeHit);
            Case.Run("the complete-list filter keeps legacy queriesHitTriggers/layer semantics", CompleteListKeepsLegacySemantics);
            Case.Run("pooled records clear old visual/source references and are reused", RecordsArePooledAndCleared);
            Case.Run("a native callback failure cannot stall other bullets and is never retried", NativeCallbackFailureNoRetry);
            Case.Run("a nested Tick inside a damage callback never double-advances any bullet", NestedTickNeverDoubleAdvances);
            Case.Run("multi-collider target with invalid front rows: nearest valid foe only, exactly once", MultiColliderAndFrontInvalids);
            Case.Run("a Crusher front row consumes the bullet without piercing to the foe behind", CrusherFrontBlocksTheFoeBehind);
            Case.Run("large dt sweeps the full segment: no wall-clock slowdown, no overshoot", LargeDeltaSweep);
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

        private static void MuzzleOriginCarriesAppearanceScale()
        {
            Archer archer = ArmedMusketeer(out _);
            MusketeerAtlas.PreparedMuzzleLocal(out float localX, out float localY);
            float scale = MusketeerAtlas.AppearanceScale;
            Vector3 root = archer.transform.position;

            Check.True(MusketeerCombat.TryComputeMuzzle(archer, out Vector2 origin, out float direction),
                "muzzle computes for a plain armed musketeer");
            Check.Near(root.x + localX * scale, origin.x, 1e-4d, "origin x = anchor x × shared appearance scale");
            Check.Near(root.y + localY * scale, origin.y, 1e-4d, "origin y = anchor y × shared appearance scale");
            Check.Near(1d, direction, 1e-6d, "facing right stays +1");

            archer._spriteRenderer.flipX = true;   // 原生 renderer 翻转：镜像 + 方向取反（原规则）
            Check.True(MusketeerCombat.TryComputeMuzzle(archer, out origin, out direction), "flipped muzzle computes");
            Check.Near(root.x - localX * scale, origin.x, 1e-4d, "renderer flipX mirrors the scaled anchor");
            Check.Near(-1d, direction, 1e-6d, "flipX keeps the original direction rule");

            archer._spriteRenderer.flipX = false;
            archer.transform.lossyScale = new Vector3(-1f, 1f, 1f);   // 父链朝向（左向）
            Check.True(MusketeerCombat.TryComputeMuzzle(archer, out origin, out direction), "left-facing muzzle computes");
            Check.Near(root.x - localX * scale, origin.x, 1e-4d, "root facing mirrors the scaled anchor");
            Check.Near(-1d, direction, 1e-6d, "root facing keeps the original direction rule");
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

        private static void ArraySaturationEscalates()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Physics2D.Saturate = true;                    // 数组每档都满 → 强制走完整 List（不再吞弹）
            Fixture.QueueHit(target, 0.5f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count,
                "array saturation escalates to the complete list: the near foe is still hit");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on complete evidence");
            Check.Equal(256, MusketeerCombat.HitCapacity, "the reusable array grew to the soft cap first");
            Check.True(Physics2D.CastCount >= 4, "array growth re-casts instead of swallowing damage");
            Check.Equal(1, Physics2D.ListCastCount, "the complete-list overload produced the authoritative results");
            Check.Equal(1, Il2CppSystem.Collections.Generic.List<RaycastHit2D>.CreatedForTests,
                "the result list is allocated once");

            // 第二次饱和：复用同一个 List（不逐次分配），仍然命中。
            Time.time = 5f;
            Physics2D.QueuedHits.Clear();
            Fixture.QueueHit(target, 0.5f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed again");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(2, Physics2D.ListCastCount, "the second saturated segment used the list overload again");
            Check.Equal(1, Il2CppSystem.Collections.Generic.List<RaycastHit2D>.CreatedForTests,
                "the same result list is reused (never re-allocated per cast)");
            Check.Equal(2, target.GetComponent<Damageable>().DamageLog.Count, "second bullet also lands");

            // 低密度常态：只走数组、不再碰 List，容量保留。
            Physics2D.Saturate = false;
            Physics2D.QueuedHits.Clear();
            Physics2D.CastCount = 0;
            Time.time = 10f;
            Fixture.QueueHit(target, 0.5f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, Physics2D.CastCount, "one array cast per segment in the common case");
            Check.Equal(2, Physics2D.ListCastCount, "no list call for a normal-density segment");
            Check.Equal(256, MusketeerCombat.HitCapacity, "the enlarged array is retained (never re-grown)");
            Check.Equal(3, target.GetComponent<Damageable>().DamageLog.Count, "normal casts still damage");
        }

        private static void HugeHitListSelectsNearestAtTail()
        {
            Archer archer = ArmedMusketeer(out _);
            GameObject near = Fixture.NewEnemy();
            for (int i = 0; i < 300; i++)
            {
                var decoy = new GameObject("decoy");
                decoy.layer = Fixture.EnemiesLayer;
                decoy.AddComponent<Collider2D>();     // 无 Damageable → 非法候选：不阻挡、不伤害
                Fixture.QueueHit(decoy, 1.2f);
            }
            Fixture.QueueHit(near, 0.5f);             // 近敌排在返回列表尾部

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, near.GetComponent<Damageable>().DamageLog.Count,
                "the nearest valid foe wins even at the tail of an unordered 300-hit result");
            Check.Equal(1, Physics2D.ListCastCount, "300 hits exceed the array soft cap → the complete list is authoritative");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed");
            Check.Equal(1, near.GetComponent<Damageable>().DamageLog.Count, "exactly one damage application for the whole result list");
        }

        private static void CompleteListUsesReturnedCount()
        {
            // 第一次饱和（真实 301 命中）：结果写入列表，陈旧尾巴里留一个 0.4 的近敌。
            Archer archer = ArmedMusketeer(out GameObject stale);
            for (int i = 0; i < 300; i++)
            {
                var decoy = new GameObject("decoy");
                decoy.layer = Fixture.EnemiesLayer;
                decoy.AddComponent<Collider2D>();
                Fixture.QueueHit(decoy, 1.2f);
            }
            Fixture.QueueHit(stale, 0.4f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, stale.GetComponent<Damageable>().DamageLog.Count, "the first complete-list result hits the stale foe");
            Check.Equal(1, Il2CppSystem.Collections.Generic.List<RaycastHit2D>.CreatedForTests, "one result list for the whole component");

            // 第二次只有 1 条结果（数组饱和 → 仍走 List）：List 未裁剪（Count 仍是 301），
            // 生产必须只读返回的 count，绝不能再消费陈旧尾巴里的近敌。
            Time.time = 5f;
            Physics2D.QueuedHits.Clear();
            Physics2D.Saturate = true;
            GameObject fresh = Fixture.NewEnemy();
            Fixture.QueueHit(fresh, 0.9f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, fresh.GetComponent<Damageable>().DamageLog.Count, "the fresh single result is used");
            Check.Equal(1, stale.GetComponent<Damageable>().DamageLog.Count, "the stale tail is never re-consumed");
            Check.Equal(2, Physics2D.ListCastCount, "the second saturated segment also used the list overload");
            Check.Equal(1, Il2CppSystem.Collections.Generic.List<RaycastHit2D>.CreatedForTests, "the same result list is reused");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed");
        }

        private static void CompleteListFailureConsumesWithoutFakeHit()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Physics2D.Saturate = true;                // 数组饱和 → 必须走 List
            Physics2D.ThrowOnListCast = true;         // List overload 失败（真实 AOT/icall 故障）
            Fixture.QueueHit(target, 0.5f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, target.GetComponent<Damageable>().DamageLog.Count,
                "a failed complete-list cast never fabricates a hit");
            Check.Equal(0, MusketeerCombat.LiveCount, "the bullet terminates conservatively");

            Physics2D.Saturate = false;
            Physics2D.ThrowOnListCast = false;
            Physics2D.QueuedHits.Clear();
            Time.time = 5f;
            Fixture.QueueHit(target, 0.5f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, target.GetComponent<Damageable>().DamageLog.Count, "normal casts keep working afterwards");
        }

        private static void CompleteListKeepsLegacySemantics()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Physics2D.Saturate = true;
            Physics2D.queriesHitTriggers = false;     // 旧 LinecastNonAlloc 会继承这个全局开关
            Fixture.QueueHit(target, 0.5f);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.False(Physics2D.LastListFilter.useTriggers,
                "the complete-list filter keeps the legacy queriesHitTriggers semantics");
            Check.True(Physics2D.LastListFilter.useLayerMask, "the layer mask is an explicit filter");
            int combatMask = (1 << Fixture.EnemiesLayer) | (1 << Fixture.WildlifeLayer);   // 鹿在 Wildlife 层
            Check.Equal(combatMask, Physics2D.LastListLayerMask, "enemies + wildlife layers are queried");
            Check.Equal(combatMask, Physics2D.LastLayerMask, "same layer mask as the array path");
            Check.False(Physics2D.LastListFilter.useDepth, "no depth filtering (legacy infinity bounds)");
        }

        private static void RecordsArePooledAndCleared()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerBullet record = MusketeerCombat.LiveRecordForTests(0);
            Check.True(record != null, "the live bullet exposes its record");
            Check.True(ReferenceEquals(record.ShooterRoot, archer.gameObject), "the record carries the shooter while live");

            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed");
            Check.False(record.Alive, "the released record is no longer alive");
            Check.True(record.Visual == null, "the old visual reference is cleared before pooling");
            Check.True(record.Renderer == null, "the old renderer reference is cleared");
            Check.True(record.ShooterRoot == null, "the old source/world reference is cleared (never re-consumed)");
            Check.Near(0d, record.Travelled, 1e-6d, "travel is cleared");
            Check.Near(0d, record.Age, 1e-6d, "age is cleared");

            Time.time = 5f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed again");
            MusketeerBullet reused = MusketeerCombat.LiveRecordForTests(0);
            Check.True(ReferenceEquals(record, reused), "the record instance is reused (no per-shot allocation)");
            Check.True(ReferenceEquals(reused.ShooterRoot, archer.gameObject), "the reused record carries the current shooter");
            Check.True(reused.Visual != null && reused.Visual.activeSelf, "the reused record has an active visual");
            Check.Equal(1, MusketeerCombat.LiveCount, "exactly one live record: a pooled record is never double-consumed");
        }

        private static void NativeCallbackFailureNoRetry()
        {
            // 原生伤害回调抛异常（真实 helper 会恰捕获一次 → Faulted）：同帧其它弹继续，且绝不重试。
            Archer archer = ArmedMusketeer(out GameObject target);
            Damageable damageable = target.GetComponent<Damageable>();
            damageable.OnReceiveDamage = (multiplier, damager, source) => throw new InvalidOperationException("native callback failed");
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "first shot suppressed");
            Time.time = 5f;
            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "second shot suppressed");
            Check.Equal(2, MusketeerCombat.LiveCount, "two bullets in flight");
            Fixture.QueueHit(target, 0.5f);

            bool escaped = false;
            try { MusketeerCombat.Tick(0.05f, true); }
            catch (Exception) { escaped = true; }
            Check.False(escaped, "a native callback exception never escapes Tick");
            Check.Equal(0, MusketeerCombat.LiveCount, "both bullets were consumed despite the failing callback");
            Check.Equal(2, damageable.DamageLog.Count,
                "exactly one native application per bullet: the second bullet still landed and nothing was re-applied");
            Check.Equal("2:Arrow", damageable.DamageLog[0], "damage stays 2 from DamageSource.Arrow");
        }

        private static void NestedTickNeverDoubleAdvances()
        {
            // 评审反例：B 在 index0（本帧未命中）、A 在 index1 命中；A 的伤害回调里嵌套 Tick(dt)
            // 并在回调中再租一发 C。生产 `_ticking` 门让嵌套 Tick 彻底 no-op：同一颗弹一帧只推进
            // 一次，回调期间新租的记录也不被嵌套 Tick / 本帧提前推进。
            Archer first = ArmedMusketeer(out GameObject target);
            Archer second = Fixture.NewArcher("second");
            MusketeerIdentity.Units.Add(second);
            MusketeerRuntime.Tick();                     // 装第二把枪（Time.deltaTime = 0 → 不推进任何弹）
            second._shootingTarget = target;

            Check.True(MusketeerCombat.TryHandleShot(first.ActiveArrowAttack, first.gameObject), "B fired");
            Time.time = 5f;
            Check.True(MusketeerCombat.TryHandleShot(first.ActiveArrowAttack, first.gameObject), "A fired");
            Check.Equal(2, MusketeerCombat.LiveCount, "B at index 0, A at index 1");

            Damageable damageable = target.GetComponent<Damageable>();
            damageable.OnReceiveDamage = (multiplier, damager, source) =>
            {
                Physics2D.QueuedHits.Clear();            // B 的后续段没有命中证据：B 必须存活到推进断言
                Check.True(MusketeerCombat.TryHandleShot(second.ActiveArrowAttack, second.gameObject),
                    "C fired inside the callback");
                MusketeerCombat.Tick(0.05f, true);       // 嵌套 Tick 必须 no-op
            };
            Fixture.QueueHit(target, 0.5f);
            MusketeerCombat.Tick(0.05f, true);

            Check.Equal(1, damageable.DamageLog.Count, "only A landed");
            Check.Equal(2, MusketeerCombat.LiveCount, "B is still flying and C was rented during the callback");
            MusketeerBullet b = MusketeerCombat.LiveRecordForTests(0);
            Check.True(b != null, "B is the first live record");
            Check.Near(1.5d, b.Travelled, 1e-4d,
                "B advanced exactly one frame step: the nested Tick did not pre-advance it");
            MusketeerBullet c = MusketeerCombat.LiveRecordForTests(1);
            Check.True(c != null && ReferenceEquals(c.ShooterRoot, second.gameObject),
                "C is the record rented inside the callback");
            Check.Near(0d, c.Travelled, 1e-4d, "a record rented inside the callback is never advanced by this frame");
        }

        private static void MultiColliderAndFrontInvalids()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            Damageable targetDamageable = target.GetComponent<Damageable>();

            var friendly = new GameObject("peasant");     // 前排友军：不阻挡、不伤害
            friendly.layer = 11;                          // Citizens
            friendly.AddComponent<Collider2D>();
            friendly.AddComponent<Character>();
            friendly.AddComponent<Damageable>();
            Fixture.QueueHit(friendly, 0.2f);

            GameObject dead = Fixture.NewEnemy();         // 前排已死地面敌人：不阻挡、绝不回调
            dead.GetComponent<Damageable>().isDead = true;
            Fixture.QueueHit(dead, 0.25f);

            GameObject squid = Fixture.NewEnemy(addSquidComponent: true);   // 前排飞行：不阻挡
            Fixture.QueueHit(squid, 0.3f);

            Collider2D first = target.AddComponent<Collider2D>();           // 同一个目标两个 collider
            Collider2D second = target.AddComponent<Collider2D>();
            Fixture.QueueHit(first, 0.5f);
            Fixture.QueueHit(second, 0.55f);

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, friendly.GetComponent<Damageable>().DamageLog.Count, "the friendly front row is skipped, not blocking");
            Check.Equal(0, dead.GetComponent<Damageable>().DamageLog.Count, "the dead front row never gets a callback");
            Check.Equal(0, squid.GetComponent<Damageable>().DamageLog.Count, "the flying front row is denied");
            Check.Equal(1, targetDamageable.DamageLog.Count, "the multi-collider target is hit exactly once, never per collider");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the nearest valid foe");
        }

        private static void CrusherFrontBlocksTheFoeBehind()
        {
            Archer archer = ArmedMusketeer(out GameObject target);
            GameObject crusher = NewCrusher(stunned: false);
            Fixture.QueueHit(crusher, 0.3f);
            Fixture.QueueHit(target, 0.6f);

            Check.True(MusketeerCombat.TryHandleShot(archer.ActiveArrowAttack, archer.gameObject), "suppressed");
            MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, crusher.GetComponent<Damageable>().DamageLog.Count, "the !IsStunned crusher consumes without damage");
            Check.Equal(0, target.GetComponent<Damageable>().DamageLog.Count, "the foe behind an immunity front is never hit (no pierce)");
            Check.Equal(0, MusketeerCombat.LiveCount, "bullet consumed on the front crusher");
        }

        private static void LargeDeltaSweep()
        {
            // 同一个墙钟时间：单帧 0.2s 与 4×0.05s 必须推进同样距离（旧代码把 dt 截断到 0.1 会慢一半）。
            Archer big = ArmedMusketeer(out _);
            Check.True(MusketeerCombat.TryHandleShot(big.ActiveArrowAttack, big.gameObject), "suppressed");
            MusketeerCombat.Tick(0.2f, true);
            Check.Equal(1, MusketeerCombat.LiveCount, "6.0 < 12: still flying after one 0.2s frame");
            Vector3 one = MusketeerCombat.VisualForTests(0).transform.position;

            Archer small = ArmedMusketeer(out _);
            Check.True(MusketeerCombat.TryHandleShot(small.ActiveArrowAttack, small.gameObject), "suppressed");
            for (int i = 0; i < 4; i++) MusketeerCombat.Tick(0.05f, true);
            Check.Equal(1, MusketeerCombat.LiveCount, "4×0.05s: same endpoint");
            Vector3 stepped = MusketeerCombat.VisualForTests(0).transform.position;
            Check.Near(one.x, stepped.x, 1e-4d, "full dt sweeps exactly as far as the same wall clock in small steps");
            Check.Near(one.y, stepped.y, 1e-4d, "same y");

            // 大 dt 端点/寿命钳制：0.5s 单帧绝不越过射程 12/寿命 0.4s，打不到 12.5 的敌人。
            Archer clamped = ArmedMusketeer(out _);
            GameObject beyond = Fixture.NewEnemy();
            Fixture.QueueHit(beyond, 12.5f);
            Check.True(MusketeerCombat.TryHandleShot(clamped.ActiveArrowAttack, clamped.gameObject), "suppressed");
            MusketeerCombat.Tick(0.5f, true);
            Check.Equal(0, beyond.GetComponent<Damageable>().DamageLog.Count, "a 0.5s frame never damages beyond range/lifetime");
            Check.Equal(0, MusketeerCombat.LiveCount, "the bullet terminates at the clamped endpoint");

            // 同一段墙钟时间的小步版本：一致地在射程界终止，同样不打 12.5。
            Archer normal = ArmedMusketeer(out _);
            GameObject beyond2 = Fixture.NewEnemy();
            Fixture.QueueHit(beyond2, 12.5f);
            Check.True(MusketeerCombat.TryHandleShot(normal.ActiveArrowAttack, normal.gameObject), "suppressed");
            for (int i = 0; i < 10; i++) MusketeerCombat.Tick(0.05f, true);
            Check.Equal(0, beyond2.GetComponent<Damageable>().DamageLog.Count, "normal frames agree: out-of-range is never hit");
            Check.Equal(0, MusketeerCombat.LiveCount, "normal frames also end at the range boundary");

            // 界内敌人：大 dt 与小步都必须命中一次（连续线段，无隧穿）。
            Archer bigHit = ArmedMusketeer(out GameObject inRangeBig);
            Fixture.QueueHit(inRangeBig, 11f);
            Check.True(MusketeerCombat.TryHandleShot(bigHit.ActiveArrowAttack, bigHit.gameObject), "suppressed");
            MusketeerCombat.Tick(0.5f, true);
            Check.Equal(1, inRangeBig.GetComponent<Damageable>().DamageLog.Count, "an 11.0 foe inside range is hit by a single 0.5s frame");
            Check.Equal(0, MusketeerCombat.LiveCount, "and the bullet is consumed");

            Archer stepHit = ArmedMusketeer(out GameObject inRangeStep);
            Check.True(MusketeerCombat.TryHandleShot(stepHit.ActiveArrowAttack, stepHit.gameObject), "suppressed");
            for (int i = 0; i < 10 && MusketeerCombat.LiveCount > 0; i++)
            {
                // Hit distances are relative to this sweep's origin, not the original muzzle.
                Physics2D.QueuedHits.Clear();
                Fixture.QueueHit(inRangeStep, 11f - MusketeerCombat.LiveRecordForTests(0).Travelled);
                MusketeerCombat.Tick(0.05f, true);
            }
            Check.Equal(1, inRangeStep.GetComponent<Damageable>().DamageLog.Count, "small steps land the same hit");
            Check.Equal(0, MusketeerCombat.LiveCount, "and consume it as well");
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
