using System;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomCombatDamage.Tests
{
    /// <summary>
    /// 共享提交 helper 契约：同步单次调用原生 Damageable.ReceiveDamage；authority/有效存活只读门；
    /// 回调异常 → Faulted（不外抛、不重试、可能已部分生效）；Submitted 不代表 HP 必减。
    /// 实机运行结果以 Operator 的构建/测试为准（本文件只提供可执行回归）。
    /// </summary>
    public sealed class CombatDamageTests : CombatTestBase
    {
        [Fact]
        public void Submit_HealthyTarget_SubmitsOnceWithExactArguments()
        {
            Damageable target = NewTarget(out _);
            GameObject source = new GameObject("archer");

            CombatDamageResult result = CombatDamage.Submit(target, 2, source, DamageSource.Arrow);

            Assert.Equal(CombatDamageResult.Submitted, result);
            Assert.Equal(1, target.Calls);
            Assert.Equal(2, target.ReceivedTotal);
            Assert.Same(source, target.LastSource);                       // 提交参数原样交给原生
            Assert.Equal(DamageSource.Arrow, target.LastKind);
            Assert.Equal(1, CombatDamage.StatSubmitted);
            Assert.Equal(0, CombatDamage.StatFaulted);
        }

        [Fact]
        public void Submit_SubmittedDoesNotGuaranteeHpLoss()
        {
            Damageable target = NewTarget(out _);
            target.IgnoreDamage = true;                                    // 原生正常返回但不减 HP（护盾/无敌等）

            CombatDamageResult result = CombatDamage.Submit(target, 5, null, DamageSource.Fire);

            Assert.Equal(CombatDamageResult.Submitted, result);
            Assert.Equal(1, target.Calls);
            Assert.Equal(0, target.ReceivedTotal);                          // Submitted 只代表调用返回
        }

        [Theory]
        [InlineData("no-authority")]
        [InlineData("inactive-go")]
        [InlineData("disabled-damageable")]
        [InlineData("dead")]
        public void Submit_InvalidPrecondition_SkipsWithoutCallingNative(string mode)
        {
            Damageable target = NewTarget(out GameObject go);
            switch (mode)
            {
                case "no-authority": NetworkBigBoss.HasWorldAuth = false; break;
                case "inactive-go": go.SetActive(false); break;
                case "disabled-damageable": target.enabled = false; break;
                default: target.isDead = true; break;
            }

            CombatDamageResult result = CombatDamage.Submit(target, 1, null, DamageSource.Fire);

            Assert.Equal(CombatDamageResult.Skipped, result);
            Assert.Equal(0, target.Calls);                                  // 原生入口未被调用
            Assert.Equal(1, CombatDamage.StatSkipped);
        }

        [Fact]
        public void Submit_NullTarget_SkipsWithoutThrow()
        {
            CombatDamageResult result = CombatDamage.Submit(null, 1, null, DamageSource.Fire);
            Assert.Equal(CombatDamageResult.Skipped, result);
        }

        [Fact]
        public void Submit_CallbackThrows_FaultsWithoutRetryOrPropagation()
        {
            Damageable target = NewTarget(out _);
            target.OnReceive = _ => throw new InvalidOperationException("boom");

            CombatDamageResult result = CombatDamage.Submit(target, 1, null, DamageSource.Fire);

            Assert.Equal(CombatDamageResult.Faulted, result);
            Assert.Equal(1, target.Calls);                                  // 绝不重试可能已部分生效的调用
            Assert.Equal(1, target.ReceivedTotal);                          // 回调前已生效的部分保留
            Assert.Equal(1, CombatDamage.StatFaulted);
            Assert.Equal(0, CombatDamage.StatSubmitted);
        }

        [Fact]
        public void Submit_FaultedTarget_DoesNotStopFollowingIndependentTargets()
        {
            Damageable first = NewTarget(out _);
            Damageable second = NewTarget(out _);
            first.OnReceive = _ => throw new InvalidOperationException("boom");

            int submitted = 0, faulted = 0;
            foreach (Damageable target in new[] { first, second })
            {
                CombatDamageResult result = CombatDamage.Submit(target, 1, null, DamageSource.Fire);
                if (result == CombatDamageResult.Submitted) submitted++;
                else if (result == CombatDamageResult.Faulted) faulted++;
            }

            Assert.Equal(1, faulted);                                       // 一处异常只丢该目标
            Assert.Equal(1, submitted);                                     // 后续独立目标照常提交
            Assert.Equal(1, second.ReceivedTotal);
        }

        [Fact]
        public void Submit_RepeatedFaults_ReportIsBounded()
        {
            Damageable target = NewTarget(out _);
            target.OnReceive = _ => throw new InvalidOperationException("boom");

            for (int i = 0; i < 300; i++) CombatDamage.Submit(target, 1, null, DamageSource.Fire);

            Assert.Equal(300, CombatDamage.StatFaulted);
            int lines = KingdomEnhancedPlugin.Instance.LogSource.Lines.Count;
            Assert.InRange(lines, 1, 3);                                    // 首次 + 每 256 次，绝不逐次刷屏
        }

        [Fact]
        public void Submit_WithoutLogSink_StillReturnsFaulted()
        {
            Damageable target = NewTarget(out _);
            target.OnReceive = _ => throw new InvalidOperationException("boom");
            KingdomEnhancedPlugin.Instance = null;                          // 日志不可用不得影响提交语义

            CombatDamageResult result = CombatDamage.Submit(target, 1, null, DamageSource.Fire);

            Assert.Equal(CombatDamageResult.Faulted, result);
            Assert.Equal(1, target.Calls);
        }
    }
}
