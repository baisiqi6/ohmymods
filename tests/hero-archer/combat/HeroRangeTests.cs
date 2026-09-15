using System;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// 英雄 1.25 射程：私有 SO 克隆（只放两个 magnitude）、活跃指针/火矢 buff 切换、
    /// CAS 归还（第三方改动不覆盖）、扫描器镜像与归还、上限 2 名、失败必须上报 false。
    /// 弹道数学与原生一致（Range = force²/-gravity），不在这里声称实机命中已验证。
    /// </summary>
    public class HeroRangeTests
    {
        private static (Fixture f, Archer archer, ArrowAttack normal, ArrowAttack fire) Armed(float shootRange = 8f)
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            ArrowAttack normal = new ArrowAttack { _shotMagnitude = 12f, _boostedShotMagnitude = 12f, _arrowGravity = -30f };
            ArrowAttack fire = new ArrowAttack { _shotMagnitude = 9f, _boostedShotMagnitude = 9f, _arrowGravity = -30f };
            archer._arrowAttack = normal;
            archer._fireArrowAttack = fire;
            archer.ActiveArrowAttack = normal;
            archer.shootRange = shootRange;
            archer._enemyScanner = new Scanner { range = shootRange, rangeBehind = shootRange };
            return (f, archer, normal, fire);
        }

        [Fact]
        public void Apply_ClonesTheAttackAndScalesOnlyTheTwoProjectileMagnitudes()
        {
            (_, Archer archer, ArrowAttack normal, _) = Armed();

            Assert.True(HeroArcherRange.Apply(archer));
            ArrowAttack clone = archer._arrowAttack;

            Assert.True(HeroArcherRange.IsApplied(archer));
            Assert.False(ReferenceEquals(normal, clone));
            Assert.Equal(12f * HeroArcherRange.SpeedFactor, clone._shotMagnitude, 4);
            Assert.Equal(12f * HeroArcherRange.SpeedFactor, clone._boostedShotMagnitude, 4);
            Assert.Equal(-30f, clone._arrowGravity, 4);                             // 弹道公式参数原样
            Assert.Equal(new Vector2(0.15f, 0.5f), clone._arrowOriginOffset);       // 出膛点原样
            Assert.Equal(12f, normal._shotMagnitude, 4);                            // 原资产绝不改
            Assert.Equal(12f, normal._boostedShotMagnitude, 4);
        }

        [Fact]
        public void Apply_RedirectsTheFireAttackAndKeepsTheActivePointerOnTheMatchingClone()
        {
            (_, Archer archer, ArrowAttack normal, ArrowAttack fire) = Armed();

            Assert.True(HeroArcherRange.Apply(archer));

            Assert.False(ReferenceEquals(fire, archer._fireArrowAttack));
            Assert.Equal(9f * HeroArcherRange.SpeedFactor, archer._fireArrowAttack._shotMagnitude, 4);
            Assert.Same(archer._arrowAttack, archer.ActiveArrowAttack);             // 原本指向普通箭 → 指向普通克隆
            Assert.Equal(9f, fire._shotMagnitude, 4);
            Assert.Equal(9f, fire._boostedShotMagnitude, 4);
        }

        [Fact]
        public void Apply_IsIdempotentAndNeverClonesItsOwnClone()
        {
            (_, Archer archer, _, _) = Armed();

            Assert.True(HeroArcherRange.Apply(archer));
            ArrowAttack first = archer._arrowAttack;
            float scaled = first._shotMagnitude;

            Assert.True(HeroArcherRange.Apply(archer));                             // 重复 Apply
            Assert.True(HeroArcherRange.Apply(archer));

            Assert.Same(first, archer._arrowAttack);                                // 不换克隆
            Assert.Equal(scaled, archer._arrowAttack._shotMagnitude, 4);            // 不二次 ×1.118
            Assert.Equal(1, HeroArcherRange.AppliedCount);
        }

        [Fact]
        public void NativeFireBuffSwitch_LandsOnTheClonedFireAttack()
        {
            (_, Archer archer, _, ArrowAttack fire) = Armed();
            Assert.True(HeroArcherRange.Apply(archer));

            archer.ActiveArrowAttack = archer._fireArrowAttack;                     // 模拟原生 FireAttacks buff 切换

            Assert.Equal(9f * HeroArcherRange.SpeedFactor, archer.ActiveArrowAttack._shotMagnitude, 4);
            Assert.True(archer.ActiveArrowAttack.Range > fire.Range);               // 原生 Range 判定也看到更远的射程
            Assert.Equal(-30f, archer.ActiveArrowAttack._arrowGravity, 4);
        }

        [Fact]
        public void Restore_ReturnsEveryFieldByCasAndFreesTheClones()
        {
            (_, Archer archer, ArrowAttack normal, ArrowAttack fire) = Armed();
            Assert.True(HeroArcherRange.Apply(archer));
            ArrowAttack normalClone = archer._arrowAttack;
            ArrowAttack fireClone = archer._fireArrowAttack;

            HeroArcherRange.Restore(archer);

            Assert.Same(normal, archer._arrowAttack);
            Assert.Same(fire, archer._fireArrowAttack);
            Assert.Same(normal, archer.ActiveArrowAttack);
            Assert.False(HeroArcherRange.IsApplied(archer));
            Assert.True(normalClone.DestroyedForTests, "normal clone destroyed");
            Assert.True(fireClone.DestroyedForTests, "fire clone destroyed");
        }

        [Fact]
        public void Restore_AfterABuffSwitchedActiveToTheFireClone_ReturnsTheBaseFireAttack()
        {
            (_, Archer archer, _, ArrowAttack fire) = Armed();
            Assert.True(HeroArcherRange.Apply(archer));
            ArrowAttack fireClone = archer._fireArrowAttack;
            archer.ActiveArrowAttack = fireClone;                                   // 英雄关闭前正在用火矢

            HeroArcherRange.Restore(archer);

            Assert.Same(fire, archer.ActiveArrowAttack);
            Assert.True(fireClone.DestroyedForTests, "no actor field keeps the clone alive");
        }

        [Fact]
        public void Restore_DoesNotOverwriteAThirdPartyBaseChange()
        {
            (_, Archer archer, _, ArrowAttack fire) = Armed();
            Assert.True(HeroArcherRange.Apply(archer));
            ArrowAttack intruder = new ArrowAttack { _shotMagnitude = 40f };
            archer._arrowAttack = intruder;                                         // 第三方换走了基线

            HeroArcherRange.Tick(archer);                                           // 观察到并放弃归还权
            HeroArcherRange.Restore(archer);

            Assert.Same(intruder, archer._arrowAttack);                             // 绝不覆盖别人的 SO
            Assert.Equal(40f, intruder._shotMagnitude, 4);
            Assert.Same(fire, archer._fireArrowAttack);                             // 另一字段照常归还
        }

        [Fact]
        public void ScannerMirror_FollowsShootRangeAndRestoresByCas()
        {
            (_, Archer archer, _, _) = Armed();
            Assert.True(HeroArcherRange.Apply(archer));
            Scanner scanner = archer._enemyScanner;
            Assert.Equal(8f, scanner.range, 4);
            Assert.Equal(8f, scanner.rangeBehind, 4);

            archer.shootRange = 10f;                                                // runtime 的 ×1.25 生效后
            HeroArcherRange.Tick(archer);
            Assert.Equal(10f, scanner.range, 4);
            Assert.Equal(10f, scanner.rangeBehind, 4);

            scanner.range = 7f;                                                     // 第三方改写这一个字段
            HeroArcherRange.Restore(archer);
            Assert.Equal(7f, scanner.range, 4);                                     // 不覆盖
            Assert.Equal(8f, scanner.rangeBehind, 4);                               // 仍是我们写的值 → 归还
        }

        [Fact]
        public void Tick_OnAnUnappliedActorChangesNothing()
        {
            (_, Archer archer, ArrowAttack normal, ArrowAttack fire) = Armed();

            HeroArcherRange.Tick(archer);
            HeroArcherRange.Restore(archer);

            Assert.Equal(12f, normal._shotMagnitude, 4);
            Assert.Equal(9f, fire._shotMagnitude, 4);
            Assert.Equal(0, HeroArcherRange.AppliedCount);
        }

        [Fact]
        public void Apply_WithoutABaseAttackReportsFailureSoTheOperatorCanRevoke()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();                                          // 没有 _arrowAttack

            Assert.False(HeroArcherRange.Apply(archer));
            Assert.False(HeroArcherRange.IsApplied(archer));
            Assert.Equal(0, HeroArcherRange.AppliedCount);
        }

        [Fact]
        public void TableIsBoundedToTwoHeroes()
        {
            (_, Archer first, _, _) = Armed();
            (_, Archer second, _, _) = Armed();
            (_, Archer third, _, _) = Armed();

            Assert.True(HeroArcherRange.Apply(first));
            Assert.True(HeroArcherRange.Apply(second));
            Assert.False(HeroArcherRange.Apply(third));                             // 上限 2：第三名保持原版射程
            Assert.Equal(2, HeroArcherRange.AppliedCount);
        }

        [Fact]
        public void Clear_RestoresEveryHeroAndFreesTheSlots()
        {
            (_, Archer first, ArrowAttack normalFirst, _) = Armed();
            (_, Archer second, ArrowAttack normalSecond, _) = Armed();
            HeroArcherRange.Apply(first);
            HeroArcherRange.Apply(second);

            HeroArcherRange.Clear();

            Assert.Same(normalFirst, first._arrowAttack);
            Assert.Same(normalSecond, second._arrowAttack);
            Assert.Equal(0, HeroArcherRange.AppliedCount);

            (_, Archer third, _, _) = Armed();
            Assert.True(HeroArcherRange.Apply(third));                              // 槽位已释放，可再次使用
        }


        // ---------------- 所有权 / 归还（修订 v3） ----------------

        private static (Archer first, Archer second) TwoWrappers(Fixture f)
        {
            Archer first = f.NewArcher();
            Archer second = first.gameObject.AddComponent<Archer>();   // 同一 GO 的第二个 wrapper
            second.SetPointerForTests(first.Pointer);                  // 同一 native 指针 + 同一 GOID
            return (first, second);
        }

        private static void Arm(Archer archer, float shootRange = 8f, bool scanner = true)
        {
            ArrowAttack normal = new ArrowAttack { _shotMagnitude = 12f, _boostedShotMagnitude = 12f, _arrowGravity = -30f };
            ArrowAttack fire = new ArrowAttack { _shotMagnitude = 9f, _boostedShotMagnitude = 9f, _arrowGravity = -30f };
            archer._arrowAttack = normal;
            archer._fireArrowAttack = fire;
            archer.ActiveArrowAttack = normal;
            archer.shootRange = shootRange;
            if (scanner) archer._enemyScanner = new Scanner { range = shootRange, rangeBehind = shootRange };
        }

        [Fact]
        public void WrapperIdentity_MatchesOnPointerAndGoIdNotOnReference()
        {
            Fixture f = new Fixture();
            (Archer first, Archer second) = TwoWrappers(f);
            ArrowAttack normal = new ArrowAttack { _shotMagnitude = 12f, _boostedShotMagnitude = 12f, _arrowGravity = -30f };
            first._arrowAttack = normal;
            first.ActiveArrowAttack = normal;
            first.shootRange = 8f;
            first._enemyScanner = new Scanner { range = 8f, rangeBehind = 8f };

            Assert.True(HeroArcherRange.Apply(first));
            Assert.True(HeroArcherRange.IsApplied(second));                 // 另一个 wrapper 认得同一个 actor
            Assert.False(ReferenceEquals(first, second));

            HeroArcherRange.Restore(second);                                // 通过另一个 wrapper 归还
            Assert.Same(normal, first._arrowAttack);
            Assert.False(HeroArcherRange.IsApplied(first));
            Assert.Equal(0, HeroArcherRange.PendingCleanupCount);
        }

        [Fact]
        public void Apply_FailsClosedWhenTheScannerIsMissing()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer, scanner: false);
            ArrowAttack normal = archer._arrowAttack;

            Assert.False(HeroArcherRange.Apply(archer));                    // 扫描器缺失 → 不宣称完整加成
            Assert.Same(normal, archer._arrowAttack);                       // 已回滚
            Assert.Equal(12f, normal._shotMagnitude, 4);
            Assert.Equal(0, HeroArcherRange.AppliedCount);
            Assert.Equal(0, HeroArcherRange.PendingCleanupCount);
            Assert.Equal(2, UnityEngine.Object.DestroyCalls);               // 两个克隆都已销毁，无孤儿
        }

        [Fact]
        public void Apply_WithNonPositiveMagnitudeFailsClosedWithoutTouchingTheBase()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            ArrowAttack normal = archer._arrowAttack;
            normal._shotMagnitude = 0f;

            Assert.False(HeroArcherRange.Apply(archer));
            Assert.Equal(0f, normal._shotMagnitude, 4);                     // 共享/基线资产一个字节都没被写
            Assert.Equal(0, UnityEngine.Object.DestroyCalls);               // 连克隆都没建
            Assert.Equal(0, HeroArcherRange.AppliedCount);
        }

        [Fact]
        public void Apply_FailureDuringScannerClaimDestroysTheOwnedClones()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            ArrowAttack normal = archer._arrowAttack;
            ArrowAttack fire = archer._fireArrowAttack;
            archer._enemyScanner.ThrowOnRangeWrite = true;                  // 克隆建好后的一步失败

            Assert.False(HeroArcherRange.Apply(archer));
            Assert.Equal(1, HeroArcherRange.PendingCleanupCount);           // 扫描器写入失败 → 保留回执重试
            Assert.Equal(0, HeroArcherRange.AppliedCount);

            archer._enemyScanner.ThrowOnRangeWrite = false;                 // 故障消失
            HeroArcherRange.RetryCleanup();

            Assert.Equal(0, HeroArcherRange.PendingCleanupCount);
            Assert.Equal(2, UnityEngine.Object.DestroyCalls);               // 刚建的两个自有克隆最终被销毁，无孤儿
            Assert.Same(normal, archer._arrowAttack);
            Assert.Same(fire, archer._fireArrowAttack);
        }

        [Fact]
        public void Restore_KeepsPendingWhenAWriteFailsThenRetryCompletes()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            Assert.True(HeroArcherRange.Apply(archer));
            ArrowAttack clone = archer._arrowAttack;
            ArrowAttack fireClone = archer._fireArrowAttack;
            archer._enemyScanner.ThrowOnRangeWrite = true;                  // 归还时扫描器写入失败
            int destroyedBefore = UnityEngine.Object.DestroyCalls;

            HeroArcherRange.Restore(archer);

            Assert.Equal(1, HeroArcherRange.PendingCleanupCount);           // 保留回执，绝不丢归还责任
            Assert.Equal(destroyedBefore, UnityEngine.Object.DestroyCalls); // 也绝不销毁可能还被引用的克隆

            archer._enemyScanner.ThrowOnRangeWrite = false;
            HeroArcherRange.RetryCleanup();

            Assert.Equal(0, HeroArcherRange.PendingCleanupCount);
            Assert.True(clone.DestroyedForTests);
            Assert.True(fireClone.DestroyedForTests);
            Assert.Equal(8f, archer._enemyScanner.range, 4);                // 扫描器按 CAS 归还
        }

        [Fact]
        public void RentDuringPendingDoesNotReuseTheSlotOrExceedTheCap()
        {
            Fixture f = new Fixture();
            Archer pending = f.NewArcher();
            Arm(pending);
            HeroArcherRange.Apply(pending);
            pending._enemyScanner.ThrowOnRangeWrite = true;                 // 制造真正的 pending（归还写失败）
            HeroArcherRange.Restore(pending);
            Assert.Equal(1, HeroArcherRange.PendingCleanupCount);

            Archer second = f.NewArcher();
            Arm(second);
            Archer third = f.NewArcher();
            Arm(third);

            Assert.True(HeroArcherRange.Apply(second));
            Assert.False(HeroArcherRange.Apply(third));                     // pending 条目不可复用 → 触顶 2
            Assert.Equal(1, HeroArcherRange.AppliedCount);

            pending._enemyScanner.ThrowOnRangeWrite = false;
            HeroArcherRange.RetryCleanup();
            HeroArcherRange.Restore(second);

            Assert.True(HeroArcherRange.Apply(third));                      // 归还完成后槽位才可再用
        }

        [Fact]
        public void Clear_KeepsPendingEntriesInsteadOfDroppingTheReceipt()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            HeroArcherRange.Apply(archer);
            ArrowAttack clone = archer._arrowAttack;
            archer._enemyScanner.ThrowOnRangeWrite = true;
            HeroArcherRange.Restore(archer);
            int destroyedBefore = UnityEngine.Object.DestroyCalls;

            HeroArcherRange.Clear();

            Assert.Equal(1, HeroArcherRange.PendingCleanupCount);           // Clear 不得丢弃 pending
            Assert.False(clone.DestroyedForTests);
            Assert.Equal(destroyedBefore, UnityEngine.Object.DestroyCalls);

            archer._enemyScanner.ThrowOnRangeWrite = false;
            HeroArcherRange.RetryCleanup();

            Assert.Equal(0, HeroArcherRange.PendingCleanupCount);
            Assert.True(clone.DestroyedForTests);
        }

        [Fact]
        public void FireAliasWithTheNormalAttackUsesASingleCloneForBothFields()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            ArrowAttack shared = archer._arrowAttack;
            archer._fireArrowAttack = shared;                               // 2.4 资产可以是同一个 SO
            archer.ActiveArrowAttack = shared;

            Assert.True(HeroArcherRange.Apply(archer));

            Assert.Same(archer._arrowAttack, archer._fireArrowAttack);      // 一个克隆服务两个字段
            Assert.Equal(12f * HeroArcherRange.SpeedFactor, archer._fireArrowAttack._shotMagnitude, 4);
            Assert.Equal(12f * HeroArcherRange.SpeedFactor, archer._fireArrowAttack._boostedShotMagnitude, 4);

            HeroArcherRange.Restore(archer);

            Assert.Same(shared, archer._arrowAttack);
            Assert.Same(shared, archer._fireArrowAttack);                   // 别名一起归还
            Assert.Equal(12f, shared._shotMagnitude, 4);
            Assert.Equal(1, UnityEngine.Object.DestroyCalls);               // 别名克隆只销毁一次
        }

        [Fact]
        public void ThirdPartyScannerWriteIsPreservedAndMarksOwnershipLost()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            Assert.True(HeroArcherRange.Apply(archer));
            Scanner scanner = archer._enemyScanner;
            Assert.True(HeroArcherRange.Tick(archer));                      // 所有权在位

            scanner.range = 7f;                                             // 第三方写入
            Assert.False(HeroArcherRange.Tick(archer));                     // 所有权丢失 → operator 应退役
            Assert.Equal(7f, scanner.range, 4);                             // 绝不覆盖第三方值
            Assert.Equal(8f, scanner.rangeBehind, 4);

            HeroArcherRange.Restore(archer);
            Assert.Equal(7f, scanner.range, 4);
        }

        [Fact]
        public void TickReportsOwnershipLossWhenTheBaseFieldIsReplaced()
        {
            Fixture f = new Fixture();
            Archer archer = f.NewArcher();
            Arm(archer);
            Assert.True(HeroArcherRange.Apply(archer));
            Assert.True(HeroArcherRange.Tick(archer));

            archer._arrowAttack = new ArrowAttack { _shotMagnitude = 20f };

            Assert.False(HeroArcherRange.Tick(archer));
        }

        [Fact]
        public void ScaledRange_ExtendsTheNativeEnvelopeByExactlyOnePointTwoFive()
        {
            float baseRange = 12f * 12f / 30f;                                      // Util.GetProjectileRange(12, -30)
            Assert.Equal(4.8f, baseRange, 4);
            Assert.Equal(baseRange * 1.25f, HeroArcherRange.ScaledRange(12f, -30f), 3);

            // 原生 BestShotInternal 末尾 ClampMagnitude(solution, magnitude)：目标 5.5 需要 v=sqrt(5.5*30)。
            float required = Mathf.Sqrt(5.5f * 30f);
            Assert.True(required > 12f, "base magnitude cannot reach 5.5 units");
            Assert.True(required <= 12f * HeroArcherRange.SpeedFactor, "hero clone can reach 5.5 units");
            Assert.True(5.5f <= HeroArcherRange.ScaledRange(12f, -30f), "target lies inside the hero envelope");
        }
    }
}
