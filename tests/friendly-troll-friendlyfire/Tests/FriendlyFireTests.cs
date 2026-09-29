using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace FriendlyTrollFriendlyFireTests
{
    /// <summary>
    /// issue #84 第1轮：红测夹具 + 无保护功能基线正例。
    /// 目标行为：已登记友好巨魔拒绝己方投石位4（BoulderFriendly），位8/位1 与其他位保持。
    /// 现状（未修复）：生产只写 invulnerable，从未碰 damagedBy → 前两条用例应为红。
    /// </summary>
    internal static class FriendlyFireTests
    {
        internal static void Run()
        {
            Case.Run("registered friendly rejects friendly-boulder bit4 [红]",
                RegisteredFriendlyRejectsFriendlyBoulderBit);
            Case.Run("registered friendly friendly-boulder hit does not reduce hp [红]",
                RegisteredFriendlyFriendlyBoulderHitDoesNotReduceHp);
            Case.Run("unregistered damageable still takes friendly boulder",
                UnregisteredDamageableStillTakesFriendlyBoulder);
            Case.Run("friendly without initial bit4 never gains it",
                FriendlyWithoutInitialBit4NeverGainsIt);
            Case.Run("disabled feature keeps mask and invulnerability",
                DisabledFeatureKeepsMaskAndInvulnerability);
            Case.Run("client peer keeps mask and invulnerability",
                ClientPeerKeepsMaskAndInvulnerability);
            Case.Run("reconcile and re-register are idempotent for mask",
                ReconcileAndReregisterAreIdempotentForMask);
            Case.Run("ResetAndDespawn prefix restores baseline and original mask",
                ResetAndDespawnPrefixRestoresBaselineAndOriginalMask);
            Case.Run("claim preserves other bits and damage sources",
                ClaimPreservesOtherBitsAndSources);
            Case.Run("repeated apply and release are idempotent",
                RepeatedApplyAndReleaseAreIdempotent);
            Case.Run("paused same world keeps owned protection",
                PausedSameWorldKeepsOwnedProtection);
            Case.Run("world exit restores owned bit",
                WorldExitRestoresOwnedBit);
            Case.Run("feature off restores only owned bit after external change",
                FeatureOffRestoresOnlyOwnedBitAfterExternalChange);
            Case.Run("component replacement restores old then protects new",
                ComponentReplacementRestoresOldThenProtectsNew);
            Case.Run("re-init boundary does not inherit old receipt",
                ReinitBoundaryDoesNotInheritOldReceipt);
            Case.Run("destroyed target is not written",
                DestroyedTargetIsNotWritten);
            Case.Run("inactive but alive target is still restored",
                InactiveButAliveTargetIsStillRestored);
            Case.Run("setter failure does not fake owned receipt",
                SetterFailureDoesNotFakeOwnedReceipt);
            Case.Run("coordinator disable restores owned bit",
                CoordinatorDisableRestoresOwnedBit);
            Case.Run("re-added bit4 is re-cleared without losing ownership [红]",
                ReAddedBit4IsReClearedWithoutLosingOwnership);
            Case.Run("deregister failure retries on same entry [红]",
                DeregisterFailureRetriesOnSameEntry);
            Case.Run("component replacement failure keeps old responsibility [红]",
                ComponentReplacementFailureKeepsOldResponsibility);
            Case.Run("pending removal does not reclaim after maintenance",
                PendingRemovalDoesNotReclaimAfterMaintenance);
            Case.Run("init boundary unknown failure abandons old receipt with log",
                InitBoundaryUnknownFailureAbandonsOldReceiptWithLog);
            Case.Run("same instance id new troll replaces pending removal in same register",
                SameInstanceIdNewTrollReplacesPendingRemovalInSameRegister);
            Case.Run("init boundary survives throwing log sink",
                InitBoundarySurvivesThrowingLogSink);
        }

        /// <summary>
        /// 红测核心：真实 RegisterFriendly 先把已登记目标 invulnerable 置 false，
        /// 但位4（己方投石）必须随之被拒绝；8/1 必须仍然通过。
        /// </summary>
        private static void RegisteredFriendlyRejectsFriendlyBoulderBit()
        {
            FriendlyUnit unit = Fixture.Friendly();

            Production.RegisterFriendly(unit.Troll);

            Check.False(unit.Damageable.invulnerable,
                "登记后己方投石窗口不应再靠 global invulnerable 兜底（现行代码成立）");
            Check.False(unit.Damageable.IsDamagedBy(DamageSource.BoulderFriendly),
                "已登记友好巨魔必须拒绝己方投石(位4)——修复目标，现行代码为红");
            Check.True(unit.Damageable.IsDamagedBy(DamageSource.BoulderEnemy),
                "敌方投石(位8)仍应可以伤害");
            Check.True(unit.Damageable.IsDamagedBy(DamageSource.Troll),
                "近战(位1)仍应可以伤害");
            Check.False(HasMaskBit(unit.Damageable.MaskWhenInvulnerabilityDisabled,
                    DamageSource.BoulderFriendly),
                "顺序探针：解除无敌瞬间位4必须已为0");
            Check.Equal(1, unit.Damageable.InvulnerableWrites, "解除无敌恰好发生一次");
        }

        /// <summary>
        /// 红测（离线 Boulder eligibility pipeline）：己方投石应先被 IsDamagedBy 拦下，
        /// 不提交 ReceiveDamage；敌伤/近战仍应受理扣血。
        /// </summary>
        private static void RegisteredFriendlyFriendlyBoulderHitDoesNotReduceHp()
        {
            FriendlyUnit unit = Fixture.Friendly(hitPoints: 100);

            Production.RegisterFriendly(unit.Troll);

            int health = unit.Damageable.hitPoints;
            // 模拟 actual 2.4 Boulder.HitObject：IsDamagedBy -> ReceiveDamage。
            BoulderPipeline.Hit(unit.Damageable, DamageSource.BoulderFriendly, 10,
                Fixture.NewObject());
            Check.Equal(health, unit.Damageable.hitPoints,
                "己方投石(位4)不应走到扣血——修复目标，现行代码为红");
            Check.True(unit.Damageable.RejectedSources.Contains(DamageSource.BoulderFriendly),
                "pipeline 应记录位4被拒——修复目标，现行代码为红");

            BoulderPipeline.Hit(unit.Damageable, DamageSource.BoulderEnemy, 10,
                Fixture.NewObject());
            Check.Equal(health - 10, unit.Damageable.hitPoints, "敌方投石(位8)仍应扣 HP");

            BoulderPipeline.Hit(unit.Damageable, DamageSource.Troll, 10, Fixture.NewObject());
            Check.Equal(health - 20, unit.Damageable.hitPoints, "近战(位1)仍应扣 HP");
        }

        /// <summary>基线正例：未登记目标（普通单位/普通可伤害对象）不受本修复影响。</summary>
        private static void UnregisteredDamageableStillTakesFriendlyBoulder()
        {
            Damageable target = Fixture.PlainDamageable(
                DamageSource.BoulderFriendly | DamageSource.BoulderEnemy | DamageSource.Troll,
                hitPoints: 50);

            Check.True(target.IsDamagedBy(DamageSource.BoulderFriendly),
                "未登记目标不应被剥夺己方投石受击资格");
            BoulderPipeline.Hit(target, DamageSource.BoulderFriendly, 10, Fixture.NewObject());
            Check.Equal(40, target.hitPoints, "未登记目标仍应正常扣 HP");
        }

        /// <summary>基线正例：位4初始为0的友好巨魔，登记/维护后必须保持0（不得凭空恢复）。</summary>
        private static void FriendlyWithoutInitialBit4NeverGainsIt()
        {
            FriendlyUnit unit = Fixture.Friendly(
                damagedBy: DamageSource.BoulderEnemy | DamageSource.Troll);

            Production.RegisterFriendly(unit.Troll);
            Check.False(unit.Damageable.IsDamagedBy(DamageSource.BoulderFriendly),
                "位4初始为0，登记后必须保持0");

            Production.ReconcileInvulnerability();
            Check.False(unit.Damageable.IsDamagedBy(DamageSource.BoulderFriendly),
                "维护重入后位4仍必须保持0");
            Check.Equal(0, unit.Damageable.DamagedByWriteAttempts,
                "原位4=0 时登记/维护不得写 mask");
        }

        /// <summary>基线正例：功能关闭时不撤位4、不关闭无敌（等同于不干预）。</summary>
        private static void DisabledFeatureKeepsMaskAndInvulnerability()
        {
            ModConfig.Enabled.Value = false;
            FriendlyUnit unit = Fixture.Friendly();

            Production.RegisterFriendly(unit.Troll);
            Production.ReconcileInvulnerability();

            Check.True(unit.Damageable.invulnerable,
                "功能关闭时不得改写 invulnerable");
            Check.True(HasMaskBit(unit.Damageable.damagedBy, DamageSource.BoulderFriendly),
                "功能关闭时 mask 位4必须保持开放（invulnerable=true 会掩盖 IsDamagedBy，改看原始 mask）");
            Check.Equal(0, unit.Damageable.DamagedByWriteAttempts,
                "功能关闭时不得写 mask");
        }

        /// <summary>基线正例：非 authority 端（客机）不写本地受保护状态。</summary>
        private static void ClientPeerKeepsMaskAndInvulnerability()
        {
            NetworkBigBoss.HasWorldAuth = false;
            FriendlyUnit unit = Fixture.Friendly();

            Production.RegisterFriendly(unit.Troll);
            Production.ReconcileInvulnerability();

            Check.True(unit.Damageable.invulnerable,
                "客机不得改写 invulnerable（等待主机同步）");
            Check.True(HasMaskBit(unit.Damageable.damagedBy, DamageSource.BoulderFriendly),
                "客机不得单方面撤 mask 位4（invulnerable=true 会掩盖 IsDamagedBy，改看原始 mask）");
            Check.Equal(0, unit.Damageable.DamagedByWriteAttempts,
                "客机不得写本地 mask");
        }

        /// <summary>基线正例：0.25s 维护与重复登记对 mask 幂等（修复后也不得反复改写/回加）。</summary>
        private static void ReconcileAndReregisterAreIdempotentForMask()
        {
            FriendlyUnit unit = Fixture.Friendly();

            Production.RegisterFriendly(unit.Troll);
            DamageSource afterRegister = unit.Damageable.damagedBy;
            Check.False(unit.Damageable.invulnerable, "登记后应关闭无敌");
            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "首次撤位只写一次");

            Production.ReconcileInvulnerability();
            Check.Equal(afterRegister, unit.Damageable.damagedBy,
                "维护重入不得改变 mask");

            Production.RegisterFriendly(unit.Troll);
            Production.ReconcileInvulnerability();
            Check.Equal(afterRegister, unit.Damageable.damagedBy,
                "重复登记不得改变 mask");
            Check.False(unit.Damageable.invulnerable, "重复登记后仍应关闭无敌");
        }

        /// <summary>
        /// 基线正例 + 归还边界：ResetAndDespawn 前缀必须还原 orig 状态：
        /// 原本拥有位4的归还位4，原本没有的保持没有。
        /// </summary>
        private static void ResetAndDespawnPrefixRestoresBaselineAndOriginalMask()
        {
            DamageSource original = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit withBit4 = Fixture.Friendly(damagedBy: original);

            Production.RegisterFriendly(withBit4.Troll);
            Production.ResetAndDespawnPrefix(withBit4.Troll);

            Check.True(withBit4.Damageable.invulnerable,
                "ResetAndDespawn 前缀应还原无敌基线");
            Check.Equal(original, withBit4.Damageable.damagedBy,
                "ResetAndDespawn 前缀应还原原始 mask（含位4）");

            DamageSource withoutBit4 = DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit noBit4 = Fixture.Friendly(damagedBy: withoutBit4);

            Production.RegisterFriendly(noBit4.Troll);
            Production.ResetAndDespawnPrefix(noBit4.Troll);

            Check.Equal(withoutBit4, noBit4.Damageable.damagedBy,
                "初始位4=0的单位归还后仍应为0");
        }

        /// <summary>撤位只动位4：其它位逐位保留，8/1 等来源仍通过（含 Fire 位）。</summary>
        private static void ClaimPreservesOtherBitsAndSources()
        {
            DamageSource baseline = DamageSource.Troll | DamageSource.Arrow
                | DamageSource.BoulderFriendly | DamageSource.BoulderEnemy
                | DamageSource.Knight | DamageSource.Fire;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);

            Production.RegisterFriendly(unit.Troll);

            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "只撤位4，其它位必须逐位保留");
            Check.False(unit.Damageable.IsDamagedBy(DamageSource.BoulderFriendly),
                "位4应被拒绝");
            Check.True(unit.Damageable.IsDamagedBy(DamageSource.BoulderEnemy),
                "敌方投石仍应通过（counter 设计保持）");
            Check.True(unit.Damageable.IsDamagedBy(DamageSource.Troll), "近战仍应通过");
            Check.True(unit.Damageable.IsDamagedBy(DamageSource.Fire), "Fire 位不受本修复影响");
            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "撤位只写一次");
        }

        /// <summary>重复维护/登记/重置都幂等：不重复写、不二次归还。</summary>
        private static void RepeatedApplyAndReleaseAreIdempotent()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);

            Production.RegisterFriendly(unit.Troll);
            DamageSource protectedMask = unit.Damageable.damagedBy;
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, protectedMask,
                "首次登记应撤位4");

            Production.ReconcileInvulnerability();
            Production.RegisterFriendly(unit.Troll);
            Production.ReconcileInvulnerability();
            Check.Equal(protectedMask, unit.Damageable.damagedBy, "重复应用不得改变 mask");
            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "重复应用不得重复写");

            Production.ResetAndDespawnPrefix(unit.Troll);
            Check.Equal(baseline, unit.Damageable.damagedBy, "重置应归还位4");
            Production.ResetAndDespawnPrefix(unit.Troll);
            Check.Equal(baseline, unit.Damageable.damagedBy, "已退出后再重置不得再写");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts,
                "总写入尝试=撤一次+还一次");
        }

        /// <summary>同 world 暂停保留责任；authority 丢失才归还，恢复后重新保护。</summary>
        private static void PausedSameWorldKeepsOwnedProtection()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);

            Production.RegisterFriendly(unit.Troll);
            FriendlyTrollPursuitCoordinator coordinator = Fixture.Coordinator();
            DamageSource protectedMask = unit.Damageable.damagedBy;

            Managers.Inst.game.state = Game.State.Menu;
            Production.TickPursuit(coordinator);
            Check.Equal(protectedMask, unit.Damageable.damagedBy, "暂停（Menu）不得归还位4");

            Managers.Inst.game.state = Game.State.Playing;
            Time.time = 100.1f;
            Production.TickPursuit(coordinator);
            Check.Equal(protectedMask, unit.Damageable.damagedBy,
                "恢复 Playing 未到 tick 仍须保持保护");

            Time.time = 200f;
            Production.TickPursuit(coordinator);
            Check.Equal(protectedMask, unit.Damageable.damagedBy, "正常 tick 后仍应保护");

            Time.timeScale = 0f;
            Production.TickPursuit(coordinator);
            Check.Equal(protectedMask, unit.Damageable.damagedBy,
                "timeScale=0 暂停不得归还位4");
            Time.timeScale = 1f;

            NetworkBigBoss.HasWorldAuth = false;
            Production.TickPursuit(coordinator);
            Check.Equal(baseline, unit.Damageable.damagedBy, "authority 丢失必须归还位4");

            NetworkBigBoss.HasWorldAuth = true;
            Time.time = 300f;
            Production.TickPursuit(coordinator);
            Check.Equal(protectedMask, unit.Damageable.damagedBy,
                "authority 恢复后应重新保护");
        }

        /// <summary>world 退出（换 world / world=null）归还自有位4。</summary>
        private static void WorldExitRestoresOwnedBit()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;

            FriendlyUnit first = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(first.Troll);
            FriendlyTrollPursuitCoordinator firstCoordinator = Fixture.Coordinator();

            Managers.Inst.world = Fixture.NewWorld();
            Production.TickPursuit(firstCoordinator);
            Check.Equal(baseline, first.Damageable.damagedBy, "world 换代必须归还位4");

            FriendlyUnit second = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(second.Troll);
            FriendlyTrollPursuitCoordinator secondCoordinator = Fixture.Coordinator();
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, second.Damageable.damagedBy,
                "新 world 内应重新保护");

            Managers.Inst.world = null;
            Production.TickPursuit(secondCoordinator);
            Check.Equal(baseline, second.Damageable.damagedBy, "world=null 必须归还位4");
        }

        /// <summary>功能关闭归还先于暂停/time 门；只还自有位4，外部改动的其它位原样保留。</summary>
        private static void FeatureOffRestoresOnlyOwnedBitAfterExternalChange()
        {
            DamageSource baseline = DamageSource.Troll | DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Fire;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);
            FriendlyTrollPursuitCoordinator coordinator = Fixture.Coordinator();

            unit.Damageable.SeedDamagedBy(unit.Damageable.damagedBy | DamageSource.Knight);
            Managers.Inst.game.state = Game.State.Menu;
            Time.time = 0f;
            ModConfig.Enabled.Value = false;

            Production.TickPursuit(coordinator);

            DamageSource expected = (baseline & ~DamageSource.BoulderFriendly)
                | DamageSource.Knight | DamageSource.BoulderFriendly;
            Check.Equal(expected, unit.Damageable.damagedBy,
                "只归还自有位4，外部改动的其它位原样保留");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts, "撤一次+还一次");
            Managers.Inst.game.state = Game.State.Playing;
            Time.time = 500f;
            Production.TickPursuit(coordinator);
            Check.True(unit.Damageable.invulnerable,
                "功能关闭并恢复 Playing 后必须保留旧生产的 invulnerability 基线恢复");
        }

        /// <summary>换 Damageable：先向仍存活的旧实例归还，再对新实例重新保护。</summary>
        private static void ComponentReplacementRestoresOldThenProtectsNew()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);

            Damageable old = unit.Damageable;
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, old.damagedBy,
                "旧 Damageable 首次登记应撤位4");

            unit.Object.RemoveComponent<Damageable>();
            Damageable replacement = unit.Object.AddComponent<Damageable>();
            replacement.invulnerable = true;
            replacement.isInvulnerableInitially = true;
            replacement.SeedDamagedBy(baseline);
            replacement.hitPoints = 100;

            Production.RegisterFriendly(unit.Troll);

            Check.Equal(baseline, old.damagedBy,
                "旧 Damageable 应归还位4（GetComponent 已指向新实例也不得拒绝）");
            Check.Equal(2, old.DamagedByWriteAttempts, "旧实例=撤一次+还一次");
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, replacement.damagedBy,
                "新 Damageable 应重新撤位4");
            Check.Equal(1, replacement.DamagedByWriteAttempts, "新实例撤位一次");
            Check.False(replacement.IsDamagedBy(DamageSource.BoulderFriendly),
                "新实例同样拒绝位4");
        }

        /// <summary>
        /// Init 新 life 边界：池复用同实例/同 pointer 不得继承旧 owned 回执；
        /// 新 life 原位4=0 时关闭功能不得凭空补位，含位4 时重新保护。
        /// </summary>
        private static void ReinitBoundaryDoesNotInheritOldReceipt()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;

            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "旧 life 应撤位4并拥有回执");

            Production.FriendlyInitPrefix(unit.Troll);
            Check.Equal(baseline, unit.Damageable.damagedBy,
                "Init 边界应向旧实例归还位4并清回执");

            unit.Damageable.SeedDamagedBy(DamageSource.BoulderEnemy | DamageSource.Troll);
            Production.FriendlyInitPostfix(unit.Troll);
            Check.Equal(DamageSource.BoulderEnemy | DamageSource.Troll,
                unit.Damageable.damagedBy, "新 life 原位4=0：登记不得补位");

            ModConfig.Enabled.Value = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal(DamageSource.BoulderEnemy | DamageSource.Troll,
                unit.Damageable.damagedBy, "旧回执不得泄漏：归还不得凭空加位4");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts,
                "总写入尝试=旧 life 撤+还，新 life 不写");

            ModConfig.Enabled.Value = true;
            FriendlyUnit fresh = Fixture.Friendly(damagedBy: baseline);
            Production.FriendlyInitPrefix(fresh.Troll);
            Production.FriendlyInitPostfix(fresh.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, fresh.Damageable.damagedBy,
                "新 life 原位4=1：Init 后应重新保护");
            Production.FriendlyInitPrefix(fresh.Troll);
            Check.Equal(baseline, fresh.Damageable.damagedBy,
                "再次 Init 边界应归还位4");
        }

        /// <summary>已毁对象不写（回执仍清空，等待 prune）。</summary>
        private static void DestroyedTargetIsNotWritten()
        {
            FriendlyUnit unit = Fixture.Friendly();
            Production.RegisterFriendly(unit.Troll);
            FriendlyTrollPursuitCoordinator coordinator = Fixture.Coordinator();
            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "登记撤位一次");

            unit.Object.MarkDestroyed();
            Production.DisableCoordinator(coordinator);

            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "已毁对象不得被写入");
        }

        /// <summary>inactive 但存活：归还不得依赖 activeInHierarchy。</summary>
        private static void InactiveButAliveTargetIsStillRestored()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);

            unit.Object.activeInHierarchy = false;
            Production.DeregisterFriendly(unit.Troll);

            Check.Equal(baseline, unit.Damageable.damagedBy,
                "inactive 但存活仍需归还位4");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts, "撤一次+还一次");
        }

        /// <summary>
        /// 写失败不伪造回执、不解除无敌；归还异常保留责任并可重试（人工故障注入）。
        /// </summary>
        private static void SetterFailureDoesNotFakeOwnedReceipt()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;

            // 撤位写入抛错：无回执，后续归还不得再写。
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            unit.Damageable.ThrowOnDamagedByWrite = true;
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline, unit.Damageable.damagedBy, "写失败时 mask 不变");
            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "只有失败的那次尝试");
            Check.True(unit.Damageable.invulnerable,
                "撤位失败时不得继续解除原生无敌并开放友伤窗口");
            Check.Equal(0, unit.Damageable.InvulnerableWrites,
                "顺序探针：撤位失败时不得调用解除无敌");

            unit.Damageable.ThrowOnDamagedByWrite = false;
            ModConfig.Enabled.Value = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal(baseline, unit.Damageable.damagedBy, "无成功接管则归还不得再写");
            Check.Equal(1, unit.Damageable.DamagedByWriteAttempts, "不伪造回执");

            // 人工故障注入（非实机复现）：归还写抛异常后不得丢责任；
            // setter 恢复后既有 Tick 必须再次归还成功（责任保留契约）。
            ModConfig.Enabled.Value = true;
            FriendlyUnit resumed = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(resumed.Troll);
            resumed.Damageable.ThrowOnDamagedByWrite = true;
            ModConfig.Enabled.Value = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, resumed.Damageable.damagedBy,
                "第一次归还失败：mask 不变（抛错未写）");
            resumed.Damageable.ThrowOnDamagedByWrite = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal(baseline, resumed.Damageable.damagedBy,
                "恢复后既有 Tick 必须再次归还成功（责任未丢）");
        }

        /// <summary>coordinator 退出（world 卸载 OnDisable）归还自有位4。</summary>
        private static void CoordinatorDisableRestoresOwnedBit()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);

            Production.DisableCoordinator(Fixture.Coordinator());

            Check.Equal(baseline, unit.Damageable.damagedBy, "coordinator 退出应归还位4");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts, "撤一次+还一次");
        }

        /// <summary>
        /// [红] 已 owned 后当前 mask 被外部重写为含位4：维护必须再次清除位4、
        /// 保留其它位，且不得在位4仍在时解除无敌；原 ownership 不重采
        /// （随后关闭功能仍只归还自有位4一次）。
        /// </summary>
        private static void ReAddedBit4IsReClearedWithoutLosingOwnership()
        {
            DamageSource baseline = DamageSource.BoulderFriendly | DamageSource.BoulderEnemy
                | DamageSource.Troll | DamageSource.Fire;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "登记应撤位4");

            // 外部重写：位4与另一个新位一起加回。
            unit.Damageable.SeedDamagedBy(unit.Damageable.damagedBy
                | DamageSource.BoulderFriendly | DamageSource.Knight);
            // 顺序探针：临时恢复无敌，维护只能在位4清掉后解除。
            unit.Damageable.invulnerable = true;

            Production.ReconcileInvulnerability();

            Check.Equal((baseline & ~DamageSource.BoulderFriendly) | DamageSource.Knight,
                unit.Damageable.damagedBy,
                "已owned后位4被重写：必须再次清除且其它位原样保留");
            Check.False(HasMaskBit(unit.Damageable.MaskWhenInvulnerabilityDisabled,
                    DamageSource.BoulderFriendly),
                "顺序探针：位4仍在时不得解除无敌");
            Check.False(unit.Damageable.invulnerable, "位4清掉后维护才解除无敌");

            ModConfig.Enabled.Value = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal((baseline & ~DamageSource.BoulderFriendly)
                    | DamageSource.Knight | DamageSource.BoulderFriendly,
                unit.Damageable.damagedBy,
                "原ownership仍在：关闭后归还自有位4且只还一次");
            Check.Equal(3, unit.Damageable.DamagedByWriteAttempts, "撤1+重清1+还1");
        }

        /// <summary>
        /// [红] 人工故障注入（非实机复现）：Deregister/Reset 归还写失败时不得丢 entry/责任；
        /// 同一入口第二次（setter 恢复后）必须归还成功。
        /// </summary>
        private static void DeregisterFailureRetriesOnSameEntry()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "登记应撤位4");

            unit.Damageable.ThrowOnDamagedByWrite = true;
            Production.ResetAndDespawnPrefix(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "第一次归还失败：mask 不变（抛错未写）");

            unit.Damageable.ThrowOnDamagedByWrite = false;
            Production.ResetAndDespawnPrefix(unit.Troll);
            Check.Equal(baseline, unit.Damageable.damagedBy,
                "同入口第二次必须归还成功（entry/责任未丢）");
            Check.Equal(3, unit.Damageable.DamagedByWriteAttempts, "撤1+失败1+成功1");
        }

        /// <summary>
        /// [红] 人工故障注入（非实机复现）：换组件时旧实例归还失败，不得丢旧绑定/责任，
        /// 也不得先保护新实例；下一次 Register 先修旧（归还成功）才保护新。
        /// </summary>
        private static void ComponentReplacementFailureKeepsOldResponsibility()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);

            Damageable old = unit.Damageable;
            unit.Object.RemoveComponent<Damageable>();
            Damageable replacement = unit.Object.AddComponent<Damageable>();
            replacement.invulnerable = true;
            replacement.isInvulnerableInitially = true;
            replacement.SeedDamagedBy(baseline);
            replacement.hitPoints = 100;

            old.ThrowOnDamagedByWrite = true;
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, old.damagedBy,
                "旧归还失败：旧 mask 不变（抛错未写）");
            Check.Equal(baseline, replacement.damagedBy,
                "旧责任未修完前不得保护新（不得撤新位4）");
            Check.Equal(0, replacement.DamagedByWriteAttempts,
                "旧责任未修完前不得写新组件");

            old.ThrowOnDamagedByWrite = false;
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline, old.damagedBy, "第二次 Register 必须先把旧位4归还");
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, replacement.damagedBy,
                "旧归还成功后才撤新位4");
            Check.Equal(1, replacement.DamagedByWriteAttempts, "新组件只撤一次");
            Check.Equal(3, old.DamagedByWriteAttempts, "旧实例=撤1+失败1+成功1");
        }

        /// <summary>
        /// enabled 下 Deregister 归还失败 → pending removal；既有 Tick/Prune 成功归还后
        /// 必须完成注销，且不再重新 claim 位4（自然维护可退场，不靠同入口重试）。
        /// </summary>
        private static void PendingRemovalDoesNotReclaimAfterMaintenance()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);
            FriendlyTrollPursuitCoordinator coordinator = Fixture.Coordinator();

            unit.Damageable.ThrowOnDamagedByWrite = true;
            Production.DeregisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "第一次归还失败：mask 不变（抛错未写）");

            unit.Damageable.ThrowOnDamagedByWrite = false;
            Time.time = 200f;
            Production.TickPursuit(coordinator);
            Check.Equal(baseline, unit.Damageable.damagedBy,
                "既有 Tick 维护必须重试归还成功");

            Time.time = 300f;
            Production.TickPursuit(coordinator);
            Check.Equal(baseline, unit.Damageable.damagedBy,
                "注销完成后不得重新 claim 位4");

            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "注销已自然退场：重新登记应再次保护");
            Check.Equal(4, unit.Damageable.DamagedByWriteAttempts,
                "撤1+失败1+成功1+重新登记1，无多余写");
        }

        /// <summary>
        /// 人工故障注入（非实机复现）：Init 边界归还未知失败 → 一次诊断、旧回执清空并移除；
        /// 新 life 位4=0 不得被补回；重复 unknown 事件日志有界（LogErrorOnce）。
        /// </summary>
        private static void InitBoundaryUnknownFailureAbandonsOldReceiptWithLog()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "旧 life 应撤位4");

            unit.Damageable.ThrowOnDamagedByWrite = true;
            Production.FriendlyInitPrefix(unit.Troll);
            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "归还失败不得写 mask");
            Check.Equal(1, CountInitBoundaryLogs(), "未知失败应恰有一次诊断");

            Production.FriendlyInitPrefix(unit.Troll);
            Check.Equal(1, CountInitBoundaryLogs(), "重复 unknown 事件日志保持有界");

            // 第二个不同对象独立经历 unknown restore failure：稳定 key 下仍只有一次诊断。
            FriendlyUnit second = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(second.Troll);
            second.Damageable.ThrowOnDamagedByWrite = true;
            Production.FriendlyInitPrefix(second.Troll);
            Check.Equal(1, CountInitBoundaryLogs(),
                "不同对象的 unknown 失败不得新增日志 key（bounded）");

            unit.Damageable.ThrowOnDamagedByWrite = false;
            unit.Damageable.SeedDamagedBy(DamageSource.BoulderEnemy | DamageSource.Troll);
            Production.FriendlyInitPostfix(unit.Troll);
            ModConfig.Enabled.Value = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal(DamageSource.BoulderEnemy | DamageSource.Troll,
                unit.Damageable.damagedBy, "新 life 位4=0：旧回执不得跨 life 补4");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts,
                "旧life撤1+失败1；新life不写");
        }

        /// <summary>
        /// 人工夹具（ID 复用）：同 InstanceID 不同 pointer 的新 Troll 替换旧 pending removal，
        /// 旧责任本次恢复成功 → 本次 Register 即登记/保护新实例（不依赖额外事件兜底）。
        /// </summary>
        private static void SameInstanceIdNewTrollReplacesPendingRemovalInSameRegister()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit old = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(old.Troll);

            old.Damageable.ThrowOnDamagedByWrite = true;
            Production.DeregisterFriendly(old.Troll);
            old.Damageable.ThrowOnDamagedByWrite = false;

            FriendlyUnit replacement = Fixture.Friendly(damagedBy: baseline);
            replacement.Troll.InstanceIdOverride = old.Troll.GetInstanceID();
            Production.RegisterFriendly(replacement.Troll);

            Check.Equal(baseline, old.Damageable.damagedBy,
                "旧责任本次恢复成功：旧位4应已归还");
            Check.Equal(3, old.Damageable.DamagedByWriteAttempts, "旧=撤1+失败1+成功归还1");
            Check.Equal(baseline & ~DamageSource.BoulderFriendly,
                replacement.Damageable.damagedBy,
                "同一 Register 内新实例应被登记并撤位4");
            Check.False(replacement.Damageable.IsDamagedBy(DamageSource.BoulderFriendly),
                "新实例拒绝位4");
            Check.Equal(1, replacement.Damageable.DamagedByWriteAttempts, "新实例撤位一次");
        }

        /// <summary>
        /// 人工故障注入（非实机复现）：归还 setter 抛错 + 日志 sink 抛错时，Init prefix
        /// 不抛、旧回执处置先完成且不跨 life（新 life 位4=0 不得被补回）。
        /// </summary>
        private static void InitBoundarySurvivesThrowingLogSink()
        {
            DamageSource baseline = DamageSource.BoulderFriendly
                | DamageSource.BoulderEnemy | DamageSource.Troll;
            FriendlyUnit unit = Fixture.Friendly(damagedBy: baseline);
            Production.RegisterFriendly(unit.Troll);

            unit.Damageable.ThrowOnDamagedByWrite = true;
            KingdomEnhancedPlugin.Instance.LogSource.ThrowOnLogError = true;
            Production.FriendlyInitPrefix(unit.Troll);
            KingdomEnhancedPlugin.Instance.LogSource.ThrowOnLogError = false;

            Check.Equal(baseline & ~DamageSource.BoulderFriendly, unit.Damageable.damagedBy,
                "归还失败不得写 mask");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts,
                "撤1+归还失败尝试1");

            unit.Damageable.ThrowOnDamagedByWrite = false;
            unit.Damageable.SeedDamagedBy(DamageSource.BoulderEnemy | DamageSource.Troll);
            Production.FriendlyInitPostfix(unit.Troll);
            ModConfig.Enabled.Value = false;
            Production.TickPursuit(Fixture.Coordinator());
            Check.Equal(DamageSource.BoulderEnemy | DamageSource.Troll,
                unit.Damageable.damagedBy, "旧回执不得跨 life 补位4");
            Check.Equal(2, unit.Damageable.DamagedByWriteAttempts,
                "旧life撤1+失败1；新life不写");
        }

        private static int CountInitBoundaryLogs()
        {
            List<string> lines = KingdomEnhancedPlugin.Instance.LogSource.Lines;
            int count = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("friendly init boundary")) count++;
            }
            return count;
        }

        private static bool HasMaskBit(DamageSource mask, DamageSource bit)
        {
            return (mask & bit) != 0;
        }
    }
}
