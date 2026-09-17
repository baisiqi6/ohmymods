using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 原生 Archer 上的战斗包账本（评审修订版）：绝对 cadence 写入 + 幂等、CAS 归还、
    /// 射击决策临时过滤（可逆、回执重试、第三方接管让位）、容量对齐 4096、
    /// OnEnable prefix 的"只归还字段、销毁延后"与可重用、岗位门/槽位收口、功能关整体解除。
    /// </summary>
    internal static class RuntimeTests
    {
        internal static void Run()
        {
            Case.Run("apply writes absolute 2x cadence, clones the SO, boosts range", ApplyPackage);
            Case.Run("apply is idempotent (no double scaling)", ApplyIdempotent);
            Case.Run("enemy scanner keeps its native threats (ShouldFlee preserved)", NativeThreatsPreserved);
            Case.Run("shoot-decision reselects a ground foe instead of a flyer", GroundReselection);
            Case.Run("wildlife filter restore retries when the setter throws", WildlifeFilterRetry);
            Case.Run("a third-party wildlife filter takeover is never clobbered", WildlifeFilterTakeover);
            Case.Run("identity loss restores every ledger field and destroys the clone", StripRestores);
            Case.Run("third-party field values survive the strip (CAS)", ThirdPartyPreserved);
            Case.Run("shot gate is the doubled ordinary cycle with tolerance", GateClock);
            Case.Run("OnEnable prefix restores fields, defers destroy, then the entry is reusable", DeferredDestroyAndReuse);
            Case.Run("65+ musketeers all arm (no silent career cap)", ManyUnits);
            Case.Run("knight and tower jobs are blocked, other jobs stay native", JobGates);
            Case.Run("a slotted musketeer is evicted through native ExitGuardSlot", SlotEviction);
            Case.Run("feature off unwinds the whole package", FeatureOffUnwinds);
        }

        private static Archer PrepareArcher()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Archer archer = Fixture.NewArcher();
            MusketeerIdentity.Units.Add(archer);
            return archer;
        }

        private static void ApplyPackage()
        {
            Archer archer = PrepareArcher();
            ArrowAttack baseAttack = archer._arrowAttack;
            MusketeerRuntime.Tick();

            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "armed");
            Check.Near(1.0d, archer.shootPrepTime, 1e-5d, "prep x2");
            Check.Near(2.0d, archer._shootIntervalRange.x, 1e-5d, "interval collapsed to midpoint x2 (min)");
            Check.Near(2.0d, archer._shootIntervalRange.y, 1e-5d, "interval collapsed to midpoint x2 (max)");
            Check.Near(1.3333333d, archer.shootCooldownTime, 1e-4d, "cooldown = 2*effective/E[N] (E[N]=1.5)");
            Check.Equal(1, archer.minAttempts, "one attempt per volley");
            Check.Equal(1, archer.maxAttempts, "no random burst gate");
            Check.Near(0d, archer.perfectArrowProbability, 1e-6d, "no random perfect gate");
            Check.Near(12d, archer.shootRange, 1e-4d, "range = native prefab baseline 8 x 1.5");
            Check.Near(12d, archer._enemyScanner.range, 1e-4d, "scanner range");
            Check.Near(12d, archer._enemyScanner.rangeBehind, 1e-4d, "scanner rangeBehind");
            Check.False(archer._arrowAttack.Pointer == baseAttack.Pointer, "SO redirected to the clone");
            Check.True(archer.ActiveArrowAttack.Pointer == archer._arrowAttack.Pointer, "active arrow attack follows");
            Check.Near(8d * System.Math.Sqrt(1.5d), archer._arrowAttack._shotMagnitude, 1e-4d,
                "range envelope scales the SO magnitude (never gravity)");

            Scanner.ObjectCondition wildlife = archer._wildlifeScanner.additionalRequirements;
            Check.True(wildlife != null, "wildlife scanner filter installed (hunting suppressed)");
            Check.False(wildlife.Invoke(Fixture.NewEnemy(EnemyType.TrollWeak)), "hunting suppressed");
        }

        private static void ApplyIdempotent()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            Vector2 first = archer._shootIntervalRange;
            ArrowAttack clone = archer._arrowAttack;
            MusketeerRuntime.Tick();
            MusketeerRuntime.Tick();
            Check.True(archer._shootIntervalRange.x == first.x && archer._shootIntervalRange.y == first.y,
                "interval never scales twice");
            Check.True(archer._arrowAttack.Pointer == clone.Pointer, "clone reused, never re-cloned");
            Check.Near(1.3333333d, archer.shootCooldownTime, 1e-4d, "cooldown stays at the planned write");
        }

        private static void NativeThreatsPreserved()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            // 评审：敌人扫描器**不常驻**过滤——ShouldFlee 仍看得到飞行单位。
            Check.True(archer._enemyScanner.additionalRequirements == null,
                "the enemy scanner (used by ShouldFlee) keeps its original threat list");
        }

        private static void GroundReselection()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            GameObject squid = Fixture.NewEnemy(addSquidComponent: true);
            GameObject ground = Fixture.NewEnemy(EnemyType.TrollWeak);

            // 原生缓存候选：近处的飞行单位 + 后面的地面敌人（原生会先撞到飞行单位）。
            archer._enemyScanner.Cached.Clear();
            archer._enemyScanner.Cached.Add(squid);
            archer._enemyScanner.Cached.Add(ground);
            archer._shootingTarget = squid;
            bool result = true;
            MusketeerRuntime.Archer_ShouldShootEnemy_MusketeerGroundSelection_Patch.After(archer, ref result);
            Check.True(result, "the shot decision stays enabled");
            Check.True(archer._shootingTarget == ground, "flyer monopoly broken: the ground foe is selected");
            Check.True(archer._enemyScanner.additionalRequirements == null, "scanner predicates stay untouched");

            // 只有飞行候选：本帧不开火，且目标字段不残留无效值。
            archer._enemyScanner.Cached.Clear();
            archer._enemyScanner.Cached.Add(squid);
            archer._shootingTarget = squid;
            result = true;
            MusketeerRuntime.Archer_ShouldShootEnemy_MusketeerGroundSelection_Patch.After(archer, ref result);
            Check.False(result, "no ground candidate → no shot");
            Check.True(archer._shootingTarget == null, "invalid target cleared");

            // 原生本来就选中地面敌人：一个字节都不写。
            archer._shootingTarget = ground;
            result = true;
            MusketeerRuntime.Archer_ShouldShootEnemy_MusketeerGroundSelection_Patch.After(archer, ref result);
            Check.True(result && archer._shootingTarget == ground, "native ground selection untouched");

            // 非火铳手：完全不动。
            Archer plain = Fixture.NewArcher("plain");
            plain._shootingTarget = squid;
            result = true;
            MusketeerRuntime.Archer_ShouldShootEnemy_MusketeerGroundSelection_Patch.After(plain, ref result);
            Check.True(result && plain._shootingTarget == squid, "ordinary archers are never reselected");
        }

        private static void WildlifeFilterRetry()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements != null, "wildlife filter installed");

            // setter 写前抛异常：回执必须留在生产侧，下一帧继续归还（绝不假装没装过）。
            archer._wildlifeScanner.ThrowBeforeWriteOnSet = true;
            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements != null, "failed restore keeps the receipt");

            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements == null, "retry completes the restore");

            // 写后抛异常（2.4 setter 可能"写一半才抛"）：字段已归还，回执在下一帧无害结算。
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements != null, "filter reinstalled");
            archer._wildlifeScanner.ThrowAfterWriteOnSet = true;
            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements == null, "after-write failure still restored the field");
            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements == null, "stale receipt settles without writes");
        }

        private static void WildlifeFilterTakeover()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            Scanner.ObjectCondition foreign = new Scanner.ObjectCondition(_ => true);
            archer._wildlifeScanner.additionalRequirements = foreign;   // 第三方接管

            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements != null
                && archer._wildlifeScanner.additionalRequirements.Pointer == foreign.Pointer,
                "third-party filter is never clobbered (receipt dropped)");

            MusketeerRuntime.Tick();
            Check.True(archer._wildlifeScanner.additionalRequirements.Pointer == foreign.Pointer,
                "no further writes once the territory is foreign");
        }

        private static void StripRestores()
        {
            Archer archer = PrepareArcher();
            ArrowAttack baseAttack = archer._arrowAttack;
            MusketeerRuntime.Tick();
            ArrowAttack clone = archer._arrowAttack;
            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();

            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "disarmed");
            Check.Near(0.5d, archer.shootPrepTime, 1e-5d, "prep restored");
            Check.Near(0.5d, archer._shootIntervalRange.x, 1e-5d, "interval min restored");
            Check.Near(1.5d, archer._shootIntervalRange.y, 1e-5d, "interval max restored");
            Check.Near(1.0d, archer.shootCooldownTime, 1e-5d, "cooldown restored");
            Check.Equal(1, archer.minAttempts, "attempt min restored");
            Check.Equal(3, archer.maxAttempts, "attempt max restored");
            Check.Near(0.2d, archer.perfectArrowProbability, 1e-5d, "perfect probability restored");
            Check.Near(8d, archer.shootRange, 1e-4d, "range restored");
            Check.Near(8d, archer._enemyScanner.range, 1e-4d, "scanner range restored");
            Check.True(archer._arrowAttack.Pointer == baseAttack.Pointer, "base SO restored");
            Check.True(archer._enemyScanner.additionalRequirements == null, "no decision filter left behind");
            Check.True(archer._wildlifeScanner.additionalRequirements == null, "wildlife filter removed");
            Check.True(UnityEngine.Object.Destroyed.Contains(clone), "owned clone destroyed");
        }

        private static void ThirdPartyPreserved()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            archer.shootPrepTime = 9f;    // 第三方在我们归还前改写
            archer.minAttempts = 4;       // 第三方改过 attempts：CAS 不得覆盖
            MusketeerIdentity.Units.Clear();
            MusketeerRuntime.Tick();
            Check.Near(9d, archer.shootPrepTime, 1e-5d, "third-party prep value preserved");
            Check.Equal(4, archer.minAttempts, "third-party attempt count preserved (CAS)");
            Check.Near(0.5d, archer._shootIntervalRange.x, 1e-5d, "our interval still restored");
        }

        private static void GateClock()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            Check.True(MusketeerRuntime.GateOpen(archer), "gate starts open");
            float next = MusketeerRuntime.ConsumeShotGate(archer);
            Check.Near(4.2833333d, next, 1e-4d, "deadline = 2x sustained mean (4.3333s) minus the frame tolerance");
            Check.False(MusketeerRuntime.GateOpen(archer), "closed right after the shot");
            Time.time = 4.28f;
            Check.False(MusketeerRuntime.GateOpen(archer), "still closed just before the deadline");
            Time.time = 4.29f;
            Check.True(MusketeerRuntime.GateOpen(archer), "open at the deadline");
        }

        private static void DeferredDestroyAndReuse()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            ArrowAttack firstClone = archer._arrowAttack;
            UnityEngine.Object.Destroyed.Clear();
            MusketeerIdentity.Units.Clear();

            // OnEnable prefix 路径（可能处于物理回调）：只归还字段，绝不 Destroy。
            MusketeerRuntime.Archer_OnEnable_MusketeerPackage_Patch.Prefix(archer);
            Check.Near(0.5d, archer.shootPrepTime, 1e-5d, "fields restored in the prefix");
            Check.False(UnityEngine.Object.Destroyed.Contains(firstClone), "no destroy inside the callback path");

            MusketeerRuntime.Tick();
            Check.True(UnityEngine.Object.Destroyed.Contains(firstClone), "clone destroyed on the next tick");

            // 评审修复点：延后销毁后条目必须被释放，池化对象重新变成火铳手时可以重新装包。
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "re-marked instance re-applies the package");
            Check.Near(1.0d, archer.shootPrepTime, 1e-5d, "package re-applied after the deferred strip");
            Check.False(archer._arrowAttack.Pointer == firstClone.Pointer, "a fresh clone serves the new life");
        }

        private static void ManyUnits()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            var archers = new Archer[70];
            for (int i = 0; i < archers.Length; i++)
            {
                archers[i] = Fixture.NewArcher("archer" + i);
                MusketeerIdentity.Units.Add(archers[i]);
            }
            MusketeerRuntime.Tick();
            Check.True(MusketeerRuntime.IsArmedMusketeer(archers[0]), "first is armed");
            Check.True(MusketeerRuntime.IsArmedMusketeer(archers[64]), "65th is armed (no 64 cap)");
            Check.True(MusketeerRuntime.IsArmedMusketeer(archers[69]), "70th is armed");
            Check.True(MusketeerRuntime.StatusText.Contains("armed=70"), "status reports all 70: " + MusketeerRuntime.StatusText);
            for (int i = 0; i < archers.Length; i++)
            {
                Check.Near(12d, archers[i].shootRange, 1e-4d, "unit " + i + " got the package");
            }
        }

        private static void JobGates()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();

            var knightGo = new GameObject("knight");
            knightGo.AddComponent<Knight>();
            var otherGo = new GameObject("wall");
            var slot = new GameObject("slot").AddComponent<GuardSlot>();

            bool result = true;
            MusketeerRuntime.Archer_IsAvailableForJob_MusketeerExclusion_Patch.After(archer, knightGo, ref result);
            Check.False(result, "knight job blocked");
            result = true;
            MusketeerRuntime.Archer_IsAvailableForJob_MusketeerExclusion_Patch.After(archer, otherGo, ref result);
            Check.True(result, "other jobs stay native");
            Check.False(MusketeerRuntime.Archer_AssignJob_MusketeerExclusion_Patch.Before(archer, knightGo),
                "AssignJob prefix blocks knight jobs");
            Check.True(MusketeerRuntime.Archer_AssignJob_MusketeerExclusion_Patch.Before(archer, otherGo),
                "AssignJob prefix allows other jobs");
            Check.False(MusketeerRuntime.Archer_SetGuardSlot_MusketeerExclusion_Patch.Before(archer, slot),
                "SetGuardSlot blocked");
            Check.False(MusketeerRuntime.Archer_EnterGuardSlot_MusketeerExclusion_Patch.Before(archer, slot),
                "EnterGuardSlot blocked");

            Archer plain = Fixture.NewArcher("plain");
            result = true;
            MusketeerRuntime.Archer_IsAvailableForJob_MusketeerExclusion_Patch.After(plain, knightGo, ref result);
            Check.True(result, "ordinary archers are untouched");
        }

        private static void SlotEviction()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            archer.inGuardSlot = true;
            MusketeerRuntime.Tick();
            Check.True(archer.Calls.Contains("ExitGuardSlot"), "native ExitGuardSlot used for eviction");
            archer.inGuardSlot = false;
            int before = CountCalls(archer, "ExitGuardSlot");
            Time.unscaledTime = 2f;   // 越过重试间隔
            MusketeerRuntime.Tick();
            Check.Equal(before, CountCalls(archer, "ExitGuardSlot"), "no further eviction once out of the slot");
        }

        private static void FeatureOffUnwinds()
        {
            Archer archer = PrepareArcher();
            MusketeerRuntime.Tick();
            ArrowAttack clone = archer._arrowAttack;
            UnityEngine.Object.Destroyed.Clear();
            MusketeerAccess.EnabledValue = false;
            MusketeerRuntime.Tick();
            Check.Near(0.5d, archer.shootPrepTime, 1e-5d, "fields returned when the feature is off");
            Check.Near(8d, archer.shootRange, 1e-4d, "range returned when the feature is off");
            Check.True(UnityEngine.Object.Destroyed.Contains(clone), "clone destroyed when the feature is off");
            Check.True(archer._wildlifeScanner.additionalRequirements == null, "filters returned when the feature is off");
        }

        private static int CountCalls(Archer archer, string call)
        {
            int count = 0;
            for (int i = 0; i < archer.Calls.Count; i++)
            {
                if (archer.Calls[i] == call) count++;
            }
            return count;
        }
    }
}
