using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 根接线回归：驱动**生产** HeroArcherRuntime（stub 版），验证锁定/撤销/池复用/side 转移/资格排除/
/// 射程 CAS 与归还重试/视觉补挂载等端到端接线行为。
///   C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/runtime-stubs/HeroArcherRuntimeWiring.Tests.csproj
/// </summary>
internal static class Program
{
    private static int _passed;
    private static readonly List<string> Failures = new List<string>();

    private static int Main()
    {
        Reset(); ModConfig.HeroArcherEnabled.Value = true;
        Archer musketeer = NewArcher(Archer.Side.Left);
        MusketeerIdentity.Marked = musketeer;
        try
        {
            Step(musketeer);
            AssertFalse("musketeer cannot become hero", HeroArcherRuntime.IsHero(musketeer));
            AssertEqual("musketeer range not borrowed by hero", 8f, musketeer.shootRange);
        }
        finally { MusketeerIdentity.Marked = null; }
        RootScopeOffAndFailedClearRestore();
        UnpaidArchersNeverBecomeHeroes();
        PaidTemporarySuspensionDoesNotPromoteReplacement();
        PurchaseActivationChecksRealEffectSetup();
        ProjectileSetupFailureRestoresNative();
        EnableAfterNativeOnEnable();
        NoDoubleMultiplyAcrossFrames();
        HeroMovementSpeedClaim();
        EligibilityFlipRetiresBeforeTick();
        SideChangeWithoutTickRestoresThenRepromotes();
        PoolReuseDoesNotInheritHeroOrDoubleMultiply();
        ExclusionsBlockPromotion();
        EmbarkedBlocksPromotion();
        ThirdPartyStealRetiresAndBlocksLife();
        DisabledClearsAndRestores();
        RestoreWriteFailureKeepsClaimAndRetries();
        IsHeroFailsOnWorldChangeAndDisabled();
        IsCombatEligibleNeedsEnemyTarget();
        VisualReattachRetryAfterFailure();

        Console.WriteLine();
        if (Failures.Count == 0)
        {
            Console.WriteLine("ALL WIRING TESTS PASSED (" + _passed + " assertions)");
            return 0;
        }
        Console.WriteLine(Failures.Count + " FAILURE(S), " + _passed + " passed");
        for (int i = 0; i < Failures.Count; i++) Console.WriteLine("  - " + Failures[i]);
        return 1;
    }

    // ============================================================
    // 用例
    // ============================================================

    /// <summary>开关在原生 OnEnable 之后才打开：既有 Archer 首次 Observe 必须建 life 并当上英雄。</summary>
    private static void RootScopeOffAndFailedClearRestore()
    {
        Reset(); ModConfig.HeroArcherEnabled.Value=true; Archer a=NewArcher(Archer.Side.Left); Step(a);
        ArcherOptionsScope.IsActive=false; HeroArcherRuntime.Tick();
        AssertEqual("root off restores range",8f,a.shootRange); AssertFalse("root off revokes hero",HeroArcherRuntime.IsHero(a));
        ArcherOptionsScope.IsActive=true; Step(a); AssertEqual("root on reestablishes once",16f,a.shootRange);
        a.ThrowOnShootRangeWrite=true;ModConfig.HeroArcherEnabled.Value=false;HeroArcherRuntime.Tick();
        AssertFalse("failed clear still revokes",HeroArcherRuntime.IsHero(a));
        a.ThrowOnShootRangeWrite=false;HeroArcherRuntime.Tick();AssertEqual("failed clear retries original",8f,a.shootRange);
    }
    private static void ProjectileSetupFailureRestoresNative()
    {
        Reset();ModConfig.HeroArcherEnabled.Value=true;Archer a=NewArcher(Archer.Side.Left);
        HeroArcherRange.ApplyAllowed=false;Step(a);HeroArcherRange.ApplyAllowed=true;
        AssertFalse("failed projectile setup not hero",HeroArcherRuntime.IsHero(a));AssertEqual("failed projectile setup restores range",8f,a.shootRange);
    }

    private static void EnableAfterNativeOnEnable()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = false;
        Archer a = NewArcher(Archer.Side.Left);
        HeroArcherRuntime.OnEnable(a);           // 原生 OnEnable 发生在开关打开之前（不建账）
        ModConfig.HeroArcherEnabled.Value = true;

        Step(a);
        AssertTrue("enable-later: hero promoted", HeroArcherRuntime.IsHero(a));
        AssertEqual("enable-later: range x2", 16f, a.shootRange);
        AssertEqual("enable-later: visual applied", 1, HeroArcherVisuals.ApplyCount);
    }

    private static void NoDoubleMultiplyAcrossFrames()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        for (int i = 0; i < 5; i++) Step(a);
        AssertEqual("no-double: range stays x2 once", 16f, a.shootRange);
        AssertEqual("no-double: visual applied once", 1, HeroArcherVisuals.ApplyCount);
    }

    /// <summary>移动提速认领：当选 ×1.5（walk/run 各一次）、关闭/池复用先归还、普通弓手不动。</summary>
    private static void HeroMovementSpeedClaim()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        AssertEqual("move: native walk speed", 4f, a.walkSpeed);
        AssertEqual("move: native run speed", 6f, a.runSpeed);

        Step(a);
        AssertTrue("move: promoted", HeroArcherRuntime.IsHero(a));
        AssertEqual("move: walk ×1.5", 6f, a.walkSpeed);
        AssertEqual("move: run ×1.5", 9f, a.runSpeed);

        for (int i = 0; i < 5; i++) Step(a);
        AssertEqual("move: no double boost across frames", 6f, a.walkSpeed);
        AssertEqual("move: run stays ×1.5 once", 9f, a.runSpeed);

        Archer plain = NewArcher(Archer.Side.Right);
        HeroRecruitment.Revoke(plain);
        Step(plain);
        AssertEqual("move: unpaid archer walk untouched", 4f, plain.walkSpeed);
        AssertEqual("move: unpaid archer run untouched", 6f, plain.runSpeed);

        ModConfig.HeroArcherEnabled.Value = false;
        Step(a);
        AssertEqual("move: toggle off restores walk", 4f, a.walkSpeed);
        AssertEqual("move: toggle off restores run", 6f, a.runSpeed);

        ModConfig.HeroArcherEnabled.Value = true;
        Step(a);
        AssertEqual("move: re-promotion re-claims walk", 6f, a.walkSpeed);
        AssertEqual("move: re-promotion re-claims run", 9f, a.runSpeed);

        HeroRecruitment.Revoke(a);
        HeroArcherRuntime.OnEnable(a);            // 池复用：新 life 在归还之后
        AssertEqual("move: pool reuse restores walk before life bump", 4f, a.walkSpeed);
        AssertEqual("move: pool reuse restores run", 6f, a.runSpeed);
    }

    /// <summary>资格在两次 Tick 之间失效：Observe 当帧就要归还射程与视觉（不能等 Tick）。</summary>
    private static void EligibilityFlipRetiresBeforeTick()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        AssertEqual("flip: promoted", 16f, a.shootRange);

        a._attackMode = Archer.AttackMode.Melee;
        HeroArcherRuntime.Observe(a);            // 不 Tick
        AssertEqual("flip: range restored in observe", 8f, a.shootRange);
        AssertFalse("flip: not hero", HeroArcherRuntime.IsHero(a));
        AssertEqual("flip: visual removed", 1, HeroArcherVisuals.RemoveCount);

        a._attackMode = Archer.AttackMode.Ranged;
        Step(a);
        AssertEqual("flip: re-promoted", 16f, a.shootRange);
    }

    private static void SideChangeWithoutTickRestoresThenRepromotes()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        AssertEqual("side: promoted left", 16f, a.shootRange);

        a.side = Archer.Side.Right;
        HeroArcherRuntime.Observe(a);
        AssertEqual("side: purchased hero keeps range on native side change", 16f, a.shootRange);
        AssertTrue("side: purchased seat remains active", HeroArcherRuntime.IsHero(a));
        AssertEqual("side: original purchase seat remains left", -1, HeroRecruitment.SeatSide(a));

        Step(a);
        AssertEqual("side: re-promoted on new side", 16f, a.shootRange);
        AssertTrue("side: hero again", HeroArcherRuntime.IsHero(a));
    }

    private static void PoolReuseDoesNotInheritHeroOrDoubleMultiply()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Right);
        Step(a);
        AssertEqual("pool: promoted", 16f, a.shootRange);

        HeroRecruitment.Revoke(a);              // Ledger confirms old owner death; reused actor has no purchase.
        HeroArcherRuntime.OnEnable(a);           // 池复用：新 life 在归还之后
        AssertEqual("pool: range restored before life bump", 8f, a.shootRange);
        AssertFalse("pool: not hero in new life (before tick)", HeroArcherRuntime.IsHero(a));

        Step(a);
        AssertEqual("pool: new unpaid life remains native", 8f, a.shootRange);
        AssertEqual("pool: no inherited visual", 1, HeroArcherVisuals.ApplyCount);
    }

    private static void ExclusionsBlockPromotion()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;

        Archer tower = NewArcher(Archer.Side.Left);
        tower.inGuardSlot = true;
        Step(tower);
        AssertFalse("exclude: tower archer not hero", HeroArcherRuntime.IsHero(tower));

        Archer controlled = NewArcher(Archer.Side.Left);
        controlled.PlayerControlled = true;
        Step(controlled);
        AssertFalse("exclude: player-controlled not hero", HeroArcherRuntime.IsHero(controlled));

        Archer crossbow = NewArcher(Archer.Side.Left, crossbow: true);
        Step(crossbow);
        AssertFalse("exclude: crossbowman not hero", HeroArcherRuntime.IsHero(crossbow));

        Archer norse = NewArcher(Archer.Side.Left, norse: true);
        Step(norse);
        AssertFalse("exclude: norse melee follower not hero", HeroArcherRuntime.IsHero(norse));

        Archer inert = NewArcher(Archer.Side.Left);
        inert._character.inert = true;
        Step(inert);
        AssertFalse("exclude: inert not hero", HeroArcherRuntime.IsHero(inert));

        Archer dead = NewArcher(Archer.Side.Left);
        dead._damageable.isDead = true;
        Step(dead);
        AssertFalse("exclude: dead not hero", HeroArcherRuntime.IsHero(dead));
    }

    private static void EmbarkedBlocksPromotion()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        a._embarkee.IsEmbarked = true;
        Step(a);
        AssertFalse("exclude: embarked not hero", HeroArcherRuntime.IsHero(a));
        AssertEqual("exclude: range untouched", 8f, a.shootRange);

        a._embarkee.IsEmbarked = false;
        Step(a);
        AssertTrue("exclude: after disembark promoted", HeroArcherRuntime.IsHero(a));
    }

    /// <summary>第三方改写射程：尊重对方、撤销英雄位、本 life 不再参选（防抖动）。</summary>
    private static void ThirdPartyStealRetiresAndBlocksLife()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        AssertEqual("steal: promoted", 16f, a.shootRange);

        a.shootRange = 15f;                       // 第三方写入
        Step(a);
        AssertFalse("steal: retired", HeroArcherRuntime.IsHero(a));
        AssertEqual("steal: third-party value kept", 15f, a.shootRange);
        AssertEqual("steal: visual removed", 1, HeroArcherVisuals.RemoveCount);

        for (int i = 0; i < 3; i++) Step(a);
        AssertFalse("steal: same life stays out of election", HeroArcherRuntime.IsHero(a));
        AssertEqual("steal: no re-write", 15f, a.shootRange);

        HeroArcherRuntime.OnEnable(a);            // 新 life：解除封锁
        Step(a);
        AssertTrue("steal: new life re-promoted", HeroArcherRuntime.IsHero(a));
        AssertEqual("steal: new baseline is the live value", 30f, a.shootRange);
    }

    private static void DisabledClearsAndRestores()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Right);
        Step(a);
        AssertEqual("disable: promoted", 16f, a.shootRange);

        ModConfig.HeroArcherEnabled.Value = false;
        AssertFalse("disable: IsHero immediate false", HeroArcherRuntime.IsHero(a));
        Step(a);
        AssertEqual("disable: range restored", 8f, a.shootRange);
        AssertTrue("disable: visuals cleared", HeroArcherVisuals.ClearCount >= 1);
    }

    /// <summary>归还时 interop 写失败：回执必须保留，下一帧重试成功；期间绝不再当选。</summary>
    private static void RestoreWriteFailureKeepsClaimAndRetries()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Right);
        Step(a);
        AssertEqual("retry: promoted", 16f, a.shootRange);

        a.ThrowOnShootRangeWrite = true;
        a.gameObject.activeInHierarchy = false;   // 触发撤销路径
        Step(a);
        AssertEqual("retry: write failed so value stays boosted", 16f, a.shootRange);
        AssertFalse("retry: not re-promoted while claim pending", HeroArcherRuntime.IsHero(a));

        a.ThrowOnShootRangeWrite = false;
        Step(a);
        AssertEqual("retry: restored on retry", 8f, a.shootRange);
    }

    private static void IsHeroFailsOnWorldChangeAndDisabled()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        AssertTrue("world: hero while world matches", HeroArcherRuntime.IsHero(a));

        Managers.Inst.world = new World { gameLayer = new Transform(new IntPtr(0xABC)) };
        AssertFalse("world: hero false immediately after world change", HeroArcherRuntime.IsHero(a));
        HeroArcherRuntime.Observe(a);             // 只观察：撤销与归还当帧发生，不掺当选
        AssertEqual("world: range restored on world change", 8f, a.shootRange);
        AssertTrue("world: visuals removed", HeroArcherVisuals.RemoveCount >= 1);

        Step(a);
        AssertEqual("world: fresh promotion in the new world uses live baseline", 16f, a.shootRange);
    }

    private static void IsCombatEligibleNeedsEnemyTarget()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        AssertFalse("combat: no target → not eligible", HeroArcherRuntime.IsCombatEligible(a));

        GameObject enemy = new GameObject("enemy");
        enemy.layer = LayerMask.NameToLayer("Enemies");
        enemy.AddComponent(new Damageable { gameObject = enemy });
        a._shootingTarget = enemy;
        AssertTrue("combat: enemy target → eligible", HeroArcherRuntime.IsCombatEligible(a));

        GameObject wildlife = new GameObject("deer");
        wildlife.layer = LayerMask.NameToLayer("Enemies");
        wildlife.AddComponent(new Damageable { gameObject = wildlife });
        a._shootingTarget = wildlife;
        wildlife.name = "deer";                    // Wildlife tag 由 CompareTag stub 恒 false；此处只验证换目标仍可判定
        AssertTrue("combat: swapped target still evaluated", HeroArcherRuntime.IsCombatEligible(a));
    }

    /// <summary>视觉挂载缺失（原生一时隐藏/临时失败）：Tick 立刻补一次，之后的失败受 1s 冷却约束。</summary>
    private static void VisualReattachRetryAfterFailure()
    {
        Reset();
        ModConfig.HeroArcherEnabled.Value = true;
        Archer a = NewArcher(Archer.Side.Left);
        Step(a);
        AssertEqual("visual-retry: applied once", 1, HeroArcherVisuals.ApplyCount);

        HeroArcherVisuals.Visible.Remove(a.gameObject.GetInstanceID());   // 模拟 Apply 失败/原生临时隐藏
        Step(a);
        AssertEqual("visual-retry: retried on next tick", 2, HeroArcherVisuals.ApplyCount);
        AssertTrue("visual-retry: hero still hero", HeroArcherRuntime.IsHero(a));

        // 再失败一次：紧接着的一帧不再重试（冷却 1s），冷却过后才再试。
        HeroArcherVisuals.Visible.Remove(a.gameObject.GetInstanceID());
        Step(a);
        AssertEqual("visual-retry: throttled right after failure", 2, HeroArcherVisuals.ApplyCount);
        Time.time += 1.1f;
        Step(a);
        AssertEqual("visual-retry: retried after cooldown", 3, HeroArcherVisuals.ApplyCount);
    }

    // ============================================================
    // 工具
    // ============================================================

    private static void Reset()
    {
        ModConfig.HeroArcherEnabled.Value = false;
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsOnline = false;
        ArcherOptionsScope.IsActive = true;
        Managers.Inst.world = new World { gameLayer = new Transform(new IntPtr(0x100)) };
        PatchRoles_Crossbowman.CrossbowFlag = false;
        PatchRoles_Crossbowman.CrossbowTarget = null;
        PatchRoles_NorseSquad.NorseFlag = false;
        PatchRoles_NorseSquad.NorseTarget = null;
        Time.time = 10f;
        Time.frameCount = 1;
        HeroArcherRuntime.Clear();
        HeroRecruitment.Reset();
        HeroArcherVisuals.Reset();
    }

    private static Archer NewArcher(Archer.Side side, bool crossbow = false, bool norse = false)
    {
        GameObject go = new GameObject("archer");
        Archer archer = new Archer();
        archer.Init(go, new IntPtr(go.InstanceId * 16));
        archer.side = side;
        HeroRecruitment.Grant(archer); // Explicit boundary receipt for pre-existing effects tests.
        if (crossbow)
        {
            PatchRoles_Crossbowman.CrossbowFlag = true;
            PatchRoles_Crossbowman.CrossbowTarget = archer;
        }
        if (norse)
        {
            PatchRoles_NorseSquad.NorseFlag = true;
            PatchRoles_NorseSquad.NorseTarget = archer;
        }
        return archer;
    }

    private static void UnpaidArchersNeverBecomeHeroes()
    {
        Reset(); ModConfig.HeroArcherEnabled.Value = true;
        Archer archer = NewArcher(Archer.Side.Left);
        HeroRecruitment.Revoke(archer);
        Step(archer);
        AssertTrue("unpaid is recruitable", HeroArcherRuntime.IsRecruitable(archer));
        AssertFalse("toggle does not grant unpaid hero", HeroArcherRuntime.IsHero(archer));
        AssertEqual("unpaid retains range", 8f, archer.shootRange);
        AssertEqual("unpaid gets no custom visual", 0, HeroArcherVisuals.ApplyCount);
    }

    private static void PurchaseActivationChecksRealEffectSetup()
    {
        Reset(); ModConfig.HeroArcherEnabled.Value = true;
        Archer archer = NewArcher(Archer.Side.Left);
        HeroArcherRange.ApplyAllowed = false;
        AssertFalse("purchase fails if projectile setup fails", HeroArcherRuntime.TryActivatePurchased(archer));
        AssertFalse("failed setup leaves no hero effects", HeroArcherRuntime.IsHero(archer));
        AssertEqual("failed setup restores native range", 8f, archer.shootRange);
        HeroArcherRange.ApplyAllowed = true;
        Reset(); ModConfig.HeroArcherEnabled.Value = true;
        archer = NewArcher(Archer.Side.Left);
        AssertTrue("purchase succeeds after effects really activate", HeroArcherRuntime.TryActivatePurchased(archer));
        AssertTrue("successful purchase has hero visual", HeroArcherVisuals.HasVisual(archer));
        AssertEqual("successful purchase has hero range", 16f, archer.shootRange);
    }

    private static void PaidTemporarySuspensionDoesNotPromoteReplacement()
    {
        Reset(); ModConfig.HeroArcherEnabled.Value = true;
        Archer paid = NewArcher(Archer.Side.Left);
        Archer unpaid = NewArcher(Archer.Side.Left);
        HeroRecruitment.Revoke(unpaid);
        Step(paid); Step(unpaid);
        paid.inGuardSlot = true; Step(paid); Step(unpaid);
        AssertFalse("tower suspends paid effects", HeroArcherRuntime.IsHero(paid));
        AssertTrue("tower does not delete receipt", HeroRecruitment.IsPurchased(paid));
        AssertFalse("tower gives no free replacement", HeroArcherRuntime.IsHero(unpaid));
        ModConfig.HeroArcherEnabled.Value = false; Step(paid);
        AssertTrue("toggle off keeps purchase", HeroRecruitment.IsPurchased(paid));
        ModConfig.HeroArcherEnabled.Value = true; paid.inGuardSlot = false; Step(paid);
        AssertTrue("paid hero resumes effects", HeroArcherRuntime.IsHero(paid));
    }

    private static void Step(Archer archer)
    {
        Time.frameCount++;
        Time.time += 0.02f;
        HeroArcherRuntime.Observe(archer);
        HeroArcherRuntime.Tick();
    }

    private static void AssertTrue(string name, bool condition)
    {
        if (condition) { _passed++; return; }
        Failures.Add(name + ": expected true, actual false");
    }

    private static void AssertFalse(string name, bool condition)
    {
        if (!condition) { _passed++; return; }
        Failures.Add(name + ": expected false, actual true");
    }

    private static void AssertEqual(string name, int expected, int actual)
    {
        if (expected == actual) { _passed++; return; }
        Failures.Add(name + ": expected " + expected + ", actual " + actual);
    }

    private static void AssertEqual(string name, float expected, float actual)
    {
        if (Math.Abs(expected - actual) <= 1e-4f) { _passed++; return; }
        Failures.Add(name + ": expected " + expected + ", actual " + actual);
    }
}
