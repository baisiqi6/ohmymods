using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// Issue281 B worker：登船降级 helper（<see cref="MusketeerRuntime.TryPrepareBoardingDemotion"/>）
    /// 的 source-linked 真实 Runtime 回归。装包走完整 PrepareArcher+Tick 路径，然后验证：
    /// 全部自有借用字段/视觉归还、不再 Armed、不重乘倍率、克隆销毁保持延迟（回调安全）且
    /// 不再被 native 字段引用（评审 verdict #3 的显式断言，不只信 bool）、foreign 接让位保留、
    /// 未知/无效 actor 拒绝、未装包安全、重复调用幂等、归还失败 false 且责任保留。
    /// </summary>
    internal static class BoardingDemotionTests
    {
        internal static void Run()
        {
            Case.Run("boarding demotion returns the whole own package and keeps destroy deferred",
                DemotionRestoresEverything);
            Case.Run("boarding demotion preserves third-party field takeovers", DemotionKeepsForeignValues);
            Case.Run("a failed demotion restore returns false and keeps the responsibility", DemotionFaultKeepsReceipt);
            Case.Run("unknown or dead actors are never demotable", DemotionRejectsUnknownActors);
            Case.Run("an actor without a package demotes safely", DemotionWithoutPackageIsSafe);
            Case.Run("demoting one musketeer never touches the others", DemotionLeavesOthersAlone);
            Case.Run("visual native-hide debt blocks demotion until a retry settles it",
                VisualNativeHideDebtBlocksAndRetrySettles);
            Case.Run("a pending visual debt never blocks or leaks to other actors", VisualDebtOtherActorsUntouched);
            Case.Run("an own-renderer hide write failure blocks demotion", OwnRendererHideFailureBlocks);
            Case.Run("an unreadable identity is unknown, never a proven empty ledger", LaterIdentityReadFailureBlocks);
            Case.Run("strict identity failure preserves a live Fire visual debt", StrictIdentityFaultKeepsHiddenDebt);
            Case.Run("strict native read failure preserves the hide receipt", StrictNativeReadFaultKeepsDebt);
            Case.Run("strict pending survives Sync and settles when the fault clears", StrictDebtSurvivesSyncAndSettlesOnClear);
            Case.Run("strict strip receipts survive a failed restore", ReceiptsSurviveFailedStripAndRetryRestores);
            Case.Run("a legacy pending receipt adopted for demotion stays strict", LegacyPendingBecomesStrict);
            Case.Run("an adopted legacy visual must prove its own renderer is hidden", LegacyOwnHideMustBeProven);
        }

        private static void DemotionRestoresEverything()
        {
            Archer archer = PrepareBoardedMusketeer(out ArrowAttack baseAttack, out ArrowAttack clone,
                out SpriteRenderer own);

            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(archer), "demotion prepared");

            // 自有借用字段全部 CAS 归还（与 StripRestores 同一组回执）。
            Check.Near(0.5d, archer.shootPrepTime, 1e-5d, "prep restored");
            Check.Near(0.5d, archer._shootIntervalRange.x, 1e-5d, "interval min restored");
            Check.Near(1.5d, archer._shootIntervalRange.y, 1e-5d, "interval max restored");
            Check.Near(1.0d, archer.shootCooldownTime, 1e-5d, "cooldown restored");
            Check.Equal(1, archer.minAttempts, "attempt min restored");
            Check.Equal(3, archer.maxAttempts, "attempt max restored");
            Check.Near(0.2d, archer.perfectArrowProbability, 1e-5d, "perfect probability restored");
            Check.Near(8d, archer.shootRange, 1e-4d, "range restored");
            Check.Near(8d, archer._enemyScanner.range, 1e-4d, "scanner range restored");
            Check.Near(8d, archer._enemyScanner.rangeBehind, 1e-4d, "scanner rangeBehind restored");
            Check.True(archer._arrowAttack.Pointer == baseAttack.Pointer, "base arrow SO restored");
            Check.True(archer.ActiveArrowAttack.Pointer == baseAttack.Pointer, "active attack restored");
            Check.True(archer._enemyScanner.additionalRequirements == null, "enemy scanner untouched");
            Check.True(archer._wildlifeScanner.additionalRequirements == null, "wildlife filter returned");

            // 不再 Armed（identity 名单未动：退职业是 A worker 在 true 之后做的）。
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "no longer armed");

            // 评审 verdict #3：native 字段不再引用 owned clone（显式断言，不只信 helper 的 bool）。
            Check.False(archer._arrowAttack.Pointer == clone.Pointer,
                "native _arrowAttack no longer references the owned clone");
            Check.False(archer.ActiveArrowAttack.Pointer == clone.Pointer,
                "native ActiveArrowAttack no longer references the owned clone");

            // 视觉回调安全撤出：自有视觉退场 + 原生恢复可见；销毁延迟到下一次 Sync/Tick。
            Check.False(MusketeerVisuals.HasVisual(archer), "own visual withdrawn");
            Check.False(archer._spriteRenderer.forceRenderingOff, "native renderer visible again");
            Check.False(UnityEngine.Object.Destroyed.Contains(clone),
                "clone destroy stays deferred (no Destroy inside the helper)");
            Check.False(UnityEngine.Object.Destroyed.Contains(own.gameObject),
                "own visual root destroy stays deferred");

            // 幂等：重复调用仍 true，不再改字段、不再 Destroy、不重乘倍率。
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(archer), "repeat call stays true");
            Check.Near(0.5d, archer.shootPrepTime, 1e-5d, "no re-scaling on the repeat call");
            Check.Near(8d, archer.shootRange, 1e-4d, "no multiplied range on the repeat call");
            Check.True(archer._arrowAttack.Pointer == baseAttack.Pointer, "no clone re-installed on repeat");

            // A worker 在 true 后退职业（这里以移出身份名单模拟）；下一帧 Tick 只收尾销毁，绝不重装包。
            MusketeerIdentity.Units.Remove(archer);
            MusketeerRuntime.Tick();
            Check.True(UnityEngine.Object.Destroyed.Contains(clone), "clone destroyed by the next-frame cleanup");
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "still not armed after the cleanup tick");
            Check.Near(0.5d, archer.shootPrepTime, 1e-5d, "package never re-applied (prep stays native)");
            Check.Near(8d, archer.shootRange, 1e-4d, "package never re-applied (range stays native)");
            Check.True(archer._arrowAttack.Pointer == baseAttack.Pointer, "base SO stays in place");

            // 视觉根在下一次 Sync（安全点）销毁。
            Sync();
            Check.True(UnityEngine.Object.Destroyed.Contains(own.gameObject), "own visual root retired by Sync");
        }

        private static void DemotionKeepsForeignValues()
        {
            Archer archer = PrepareBoardedMusketeer(out ArrowAttack baseAttack, out ArrowAttack clone, out _);
            archer.shootPrepTime = 9f;    // 第三方在归还前改写
            archer.minAttempts = 4;       // 第三方改过 attempts：CAS 不得覆盖
            Scanner.ObjectCondition foreign = new Scanner.ObjectCondition(_ => true);
            archer._wildlifeScanner.additionalRequirements = foreign;   // 第三方接管猎物过滤

            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(archer), "demotion prepared");
            Check.Near(9d, archer.shootPrepTime, 1e-5d, "third-party prep value preserved");
            Check.Equal(4, archer.minAttempts, "third-party attempt count preserved (CAS)");
            Check.True(archer._wildlifeScanner.additionalRequirements != null
                && archer._wildlifeScanner.additionalRequirements.Pointer == foreign.Pointer,
                "foreign wildlife filter is never clobbered (territory yielded)");
            Check.Near(0.5d, archer._shootIntervalRange.x, 1e-5d, "our interval still restored");
            Check.Near(8d, archer.shootRange, 1e-4d, "our range still restored");
            Check.True(archer._arrowAttack.Pointer == baseAttack.Pointer, "base SO restored");
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "disarmed");

            // 让位后的收尾照常：克隆下一帧销毁（无人再引用它）。
            MusketeerIdentity.Units.Remove(archer);
            MusketeerRuntime.Tick();
            Check.True(UnityEngine.Object.Destroyed.Contains(clone), "clone destroyed after a yielded demotion");
        }

        private static void DemotionFaultKeepsReceipt()
        {
            Archer archer = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out _);
            archer._wildlifeScanner.ThrowBeforeWriteOnSet = true;   // 既有故障注入边界：归还写失败

            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(archer),
                "an incomplete restore must not report the actor as demotable");
            Check.True(archer._wildlifeScanner.additionalRequirements != null,
                "the wildlife receipt is kept for the next retry");
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer),
                "meanwhile disarmed (native arrows are no longer suppressed)");

            // 归还失败 → A 不退职业（身份仍在）：下一帧重试完成归还结算（旧克隆销毁）后，
            // 包按计划值照常恢复，绝不留在"半普通"状态（filter 重装是全新装包，不是残留回执）。
            MusketeerRuntime.Tick();
            Check.True(UnityEngine.Object.Destroyed.Contains(clone),
                "the kept receipt settled on retry (old clone destroyed)");
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer),
                "the failed demotion keeps the musketeer package (identity untouched)");
            Check.Near(1.0d, archer.shootPrepTime, 1e-5d, "package re-applied at planned values");
            Check.False(archer._arrowAttack.Pointer == clone.Pointer, "a fresh clone serves the kept career");
        }

        private static void DemotionRejectsUnknownActors()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(null), "null actor rejected");

            Archer dead = Fixture.NewArcher("dead");
            UnityEngine.Object.Destroy(dead.gameObject);
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(dead), "destroyed actor rejected");
        }

        private static void DemotionWithoutPackageIsSafe()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();

            // 从未登记、从未装包的有效 actor：无借用责任，可以安全降级。
            Archer plain = Fixture.NewArcher("plain");
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(plain), "a valid package-less actor demotes safely");
            Check.Near(8d, plain.shootRange, 1e-4d, "native fields untouched");
            Check.True(plain._wildlifeScanner.additionalRequirements == null, "no filter installed either");

            // 已在身份名单但还没 Tick 装包：同样安全（表里没有它的条目）。
            Archer registered = Fixture.NewArcher("registered");
            MusketeerIdentity.Units.Add(registered);
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(registered),
                "a registered-but-not-yet-applied actor demotes safely");
            Check.False(MusketeerRuntime.IsArmedMusketeer(registered), "never armed");
            Check.Near(0.5d, registered.shootPrepTime, 1e-5d, "native fields untouched");
        }

        private static void DemotionLeavesOthersAlone()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Archer first = Fixture.NewArcher("first");
            MusketeerIdentity.Units.Add(first);
            Archer second = Fixture.NewArcher("second");
            MusketeerIdentity.Units.Add(second);
            MusketeerRuntime.Tick();
            ArrowAttack secondClone = second._arrowAttack;
            Check.True(MusketeerRuntime.IsArmedMusketeer(first) && MusketeerRuntime.IsArmedMusketeer(second),
                "both armed before demotion");

            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(first), "first demoted");
            Check.False(MusketeerRuntime.IsArmedMusketeer(first), "first disarmed");
            Check.Near(8d, first.shootRange, 1e-4d, "first range restored");
            Check.True(MusketeerRuntime.IsArmedMusketeer(second), "second stays armed");
            Check.Near(1.0d, second.shootPrepTime, 1e-5d, "second package intact");
            Check.Near(12d, second.shootRange, 1e-4d, "second range intact");
            Check.True(second._arrowAttack.Pointer == secondClone.Pointer, "second clone untouched");
            Check.True(second._wildlifeScanner.additionalRequirements != null, "second filter untouched");

            MusketeerIdentity.Units.Remove(first);
            MusketeerRuntime.Tick();   // first 的收尾销毁不影响 second
            Check.True(MusketeerRuntime.IsArmedMusketeer(second), "second survives first's cleanup tick");
            Check.True(second._arrowAttack.Pointer == secondClone.Pointer, "second clone still in place");
        }

        /// <summary>Astra P1：native 隐藏债务未结清必须 false；重试结清后 true 且无残留（root 销毁仍延后）。</summary>
        private static void VisualNativeHideDebtBlocksAndRetrySettles()
        {
            Archer archer = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out SpriteRenderer own);
            EnterFirePhase(archer);   // 真实 Fire 相位：native 此刻确实被自有视觉隐藏

            archer._spriteRenderer.ThrowOnForceRenderingOffSetAlways = true;   // 归还持续写失败
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(archer),
                "an unsettled native-hide debt must block demotion");
            Check.True(archer._spriteRenderer.forceRenderingOff, "native stays hidden while the debt stands");
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer),
                "the package is not stripped behind a blocked visual return");
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(archer),
                "a repeated call with the same debt stays false");

            archer._spriteRenderer.ThrowOnForceRenderingOffSetAlways = false;
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(archer),
                "retry after the fault clears settles the debt and succeeds");
            Check.False(archer._spriteRenderer.forceRenderingOff, "native renderer visible again (debt settled)");
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "disarmed once the visual return is proven");
            Check.False(archer._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced by the native fields");
            Check.False(UnityEngine.Object.Destroyed.Contains(own.gameObject),
                "root destroy stays deferred even after a settled debt");

            Sync();
            Check.True(UnityEngine.Object.Destroyed.Contains(own.gameObject), "root retired by the next Sync");
        }

        /// <summary>同 actor pending 重复仍 false；既有 Sync 路径也能结清；其它 actor 不受债务影响。</summary>
        private static void VisualDebtOtherActorsUntouched()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Sprite[] sprites = new Sprite[MusketeerAtlas.FrameCount];
            for (int i = 0; i < sprites.Length; i++) sprites[i] = new Sprite();
            MusketeerVisuals.SetAtlasForTests(sprites);
            Archer first = Fixture.NewArcher("first");
            MusketeerIdentity.Units.Add(first);
            Archer second = Fixture.NewArcher("second");
            MusketeerIdentity.Units.Add(second);
            MusketeerRuntime.Tick();
            Time.frameCount++;
            MusketeerRuntime.Tick();
            Check.True(MusketeerVisuals.HasVisual(first) && MusketeerVisuals.HasVisual(second), "both visuals live");
            EnterFirePhase(first);   // 只有 first 进真实隐藏相位

            first._spriteRenderer.ThrowOnForceRenderingOffSetAlways = true;
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(first), "first blocked by its own debt");
            Check.True(MusketeerVisuals.HasVisual(second), "second's visual untouched");
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(second),
                "second demotes normally while first is blocked");
            Check.False(second._spriteRenderer.forceRenderingOff, "second's native renderer never hidden by us");
            Check.Near(8d, second.shootRange, 1e-4d, "second's fields returned");

            // 既有 Sync 重试路径（非 helper）也能结清 first 的债务；随后 helper 对 first 变 true。
            first._spriteRenderer.ThrowOnForceRenderingOffSetAlways = false;
            Sync();
            Check.False(first._spriteRenderer.forceRenderingOff, "Sync settled first's native-hide debt");
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(first), "first demotes after the Sync settle");
            Check.False(MusketeerRuntime.IsArmedMusketeer(first), "first disarmed");
        }

        /// <summary>自有 renderer 隐藏写失败（读回不成立/异常）不得误报退场成功。</summary>
        private static void OwnRendererHideFailureBlocks()
        {
            Archer archer = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out SpriteRenderer own);
            EnterFirePhase(archer);   // own renderer 可见、native 被隐藏的真实开火相位
            Check.True(own.enabled, "own renderer visible during Fire");

            own.ThrowOnEnabledSet = true;   // 下一次写 enabled 抛异常
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(archer),
                "an own-renderer hide failure must block demotion");
            Check.True(MusketeerVisuals.HasVisual(archer), "the visual responsibility is kept (still live)");
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "package untouched while the visual gate fails");

            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(archer),
                "retry after the transient hide fault succeeds");
            Check.False(MusketeerVisuals.HasVisual(archer), "visual withdrawn");
            Check.False(archer._spriteRenderer.forceRenderingOff, "native visible");
            Check.False(MusketeerRuntime.IsArmedMusketeer(archer), "disarmed");
            Check.False(archer._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced");
            MusketeerIdentity.Units.Remove(archer);
            MusketeerRuntime.Tick();
            Check.True(UnityEngine.Object.Destroyed.Contains(clone), "clone settled by the next-frame cleanup");
        }

        /// <summary>
        /// Astra P2/strict：helper 全程**恰好一次**身份读；读异常 = 未知 → false（绝不折叠成"无包"），
        /// 故障确已触发（计数命中）且包原封不动；读数 pin 证明路径内没有任何隐藏的二次身份读。
        /// </summary>
        private static void LaterIdentityReadFailureBlocks()
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();

            // 无包 actor：helper 的唯一身份读抛异常 → false（不是"从未装包"）。
            Archer plain = Fixture.NewArcher("identity-fault");
            int plainTarget = plain.gameObject.IdReads + 1;
            plain.gameObject.ThrowIdReadAt = plainTarget;
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(plain),
                "an unreadable identity is unknown, not package-less");
            Check.True(plain.gameObject.IdReads == plainTarget,
                "the identity read fault actually fired (counter hit the target)");
            plain.gameObject.ThrowIdReadAt = 0;
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(plain), "readable again → safe");

            // 已装包 + 活动视觉 actor：唯一身份读抛异常 → false 且包/视觉原封不动。
            MusketeerVisuals.SetAtlasForTests(NewAtlas());
            Archer armed = Fixture.NewArcher("armed-fault");
            MusketeerIdentity.Units.Add(armed);
            MusketeerRuntime.Tick();
            Time.frameCount++;
            MusketeerRuntime.Tick();
            ArrowAttack armedClone = armed._arrowAttack;
            Check.True(MusketeerVisuals.HasVisual(armed), "visual live before the fault");
            int armedTarget = armed.gameObject.IdReads + 1;
            armed.gameObject.ThrowIdReadAt = armedTarget;
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(armed),
                "the single unreadable identity read must not claim demotion");
            Check.True(armed.gameObject.IdReads == armedTarget, "the fault fired at the helper's only read");
            Check.True(MusketeerRuntime.IsArmedMusketeer(armed), "package untouched (no half strip)");
            Check.Near(1.0d, armed.shootPrepTime, 1e-5d, "planned cadence still applied");
            Check.True(armed._arrowAttack.Pointer == armedClone.Pointer, "clone still installed");
            Check.True(MusketeerVisuals.HasVisual(armed), "visual untouched while blocked");

            armed.gameObject.ThrowIdReadAt = 0;
            int readsBefore = armed.gameObject.IdReads;
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(armed), "readable again → demotes");
            Check.False(armed._arrowAttack.Pointer == armedClone.Pointer, "clone returned after the readable retry");
            Check.True(armed.gameObject.IdReads == readsBefore + 1,
                "a clean full demotion (package + live visual + fire-ready) performs exactly ONE identity read");
        }

        /// <summary>strict：真实 Fire 隐藏下的身份读异常——债务不清、native 仍隐藏，读恢复后收敛。</summary>
        private static void StrictIdentityFaultKeepsHiddenDebt()
        {
            Archer actor = PrepareBoardedMusketeer(out ArrowAttack baseAttack, out ArrowAttack clone, out _);
            EnterFirePhase(actor);
            Check.True(actor._spriteRenderer.forceRenderingOff, "native hidden before the fault");

            int target = actor.gameObject.IdReads + 1;
            actor.gameObject.ThrowIdReadAt = target;
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "unknown identity with a hidden native renderer must block");
            Check.True(actor.gameObject.IdReads == target, "the fault fired at the helper's only read");
            Check.True(actor._spriteRenderer.forceRenderingOff, "native stays hidden (debt not mis-cleared)");
            Check.True(MusketeerVisuals.HasVisual(actor), "visual still live (untouched)");
            Check.True(MusketeerRuntime.IsArmedMusketeer(actor), "still armed (no half demotion)");

            actor.gameObject.ThrowIdReadAt = 0;
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "readable retry settles everything");
            Check.False(actor._spriteRenderer.forceRenderingOff, "native visible after the retry");
            Check.True(actor._arrowAttack.Pointer == baseAttack.Pointer, "base SO restored by the retry");
            Check.False(actor._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced");
            Check.False(MusketeerRuntime.IsArmedMusketeer(actor), "disarmed");
        }

        /// <summary>strict：native-hide 读异常 = 未知，不清账（读异常≠销毁）；恢复后重试收敛。</summary>
        private static void StrictNativeReadFaultKeepsDebt()
        {
            Archer actor = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out _);
            EnterFirePhase(actor);
            actor._spriteRenderer.ThrowOnForceRenderingOffGetAlways = true;
            int gets = actor._spriteRenderer.ForceRenderingOffGets;

            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "an unreadable native renderer state must block (unknown ≠ settled)");
            Check.True(actor._spriteRenderer.ForceRenderingOffGets > gets,
                "the read fault actually fired during the settle attempt");

            // Sync 重试期间故障持续：债务不丢（legacy/strict 都不得清账）。
            Sync();
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "the debt survives a Sync while the read fault persists");

            actor._spriteRenderer.ThrowOnForceRenderingOffGetAlways = false;
            Check.True(actor._spriteRenderer.forceRenderingOff,
                "nothing wrote the renderer while the state was unreadable (debt intact)");
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "readable retry settles the debt");
            Check.False(actor._spriteRenderer.forceRenderingOff, "native visible after the confirmed restore");
            Check.False(actor._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced");
            Check.False(MusketeerRuntime.IsArmedMusketeer(actor), "disarmed");
        }

        /// <summary>strict pending：Sync 反复故障不清账、BeforeReuse 不越过；清故障后 Sync 结清并收敛。</summary>
        private static void StrictDebtSurvivesSyncAndSettlesOnClear()
        {
            Archer actor = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out SpriteRenderer own);
            EnterFirePhase(actor);
            actor._spriteRenderer.ThrowOnForceRenderingOffSetAlways = true;

            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "write fault blocks the settle");
            Check.True(actor._spriteRenderer.forceRenderingOff, "native stays hidden (debt registered)");
            Sync();   // 故障持续下的 Sync 重试
            Check.True(actor._spriteRenderer.forceRenderingOff, "the strict debt survives the faulted Sync");
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "a repeated call with the same debt stays false");

            // BeforeReuse（OnEnable prefix 路径）不得越过未决 strict pending 清账。
            MusketeerRuntime.Archer_OnEnable_MusketeerPackage_Patch.Prefix(actor);
            Check.True(actor._spriteRenderer.forceRenderingOff, "BeforeReuse never crosses the pending receipt");
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "the debt still stands after the OnEnable prefix path");

            actor._spriteRenderer.ThrowOnForceRenderingOffSetAlways = false;
            Sync();   // 清故障后的 Sync 经 strict 分流结清
            Check.False(actor._spriteRenderer.forceRenderingOff, "the cleared Sync settles the strict debt");
            Check.True(UnityEngine.Object.Destroyed.Contains(own.gameObject), "root retired by that Sync");
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "helper true after the Sync settle");
            Check.False(actor._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced");
        }

        /// <summary>strict strip：归还失败保留全部回执，重试凭 CAS 回据完整恢复（含 clone 字段重定向）。</summary>
        private static void ReceiptsSurviveFailedStripAndRetryRestores()
        {
            Archer actor = PrepareBoardedMusketeer(out ArrowAttack baseAttack, out ArrowAttack clone, out _);
            EnterFirePhase(actor);
            actor._wildlifeScanner.ThrowBeforeWriteOnSet = true;   // 一次归还写失败（既有注入面）

            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "a failed restore must block and keep the receipts");
            Check.True(actor._wildlifeScanner.additionalRequirements != null, "wildlife receipt kept");
            Check.False(MusketeerRuntime.IsArmedMusketeer(actor), "disarmed meanwhile (no suppression)");

            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(actor),
                "the readable retry restores from the kept receipts");
            Check.True(actor._wildlifeScanner.additionalRequirements == null, "wildlife filter returned");
            Check.True(actor._arrowAttack.Pointer == baseAttack.Pointer,
                "clone redirect receipt survived and restored the base SO");
            Check.False(actor._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced");
            MusketeerIdentity.Units.Remove(actor);
            MusketeerRuntime.Tick();
            Check.True(UnityEngine.Object.Destroyed.Contains(clone), "clone settled by the next-frame cleanup");
        }

        private static void LegacyPendingBecomesStrict()
        {
            Archer actor = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out _);
            EnterFirePhase(actor);
            actor._spriteRenderer.ThrowOnForceRenderingOffSetAlways = true;
            MusketeerVisuals.BeforeReuse(actor);
            Check.False(MusketeerVisuals.HasVisual(actor), "legacy reset detached its own visual");
            Check.True(actor._spriteRenderer.forceRenderingOff, "the legacy receipt still owes native visibility");
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "adoption cannot settle the write fault");

            // A legacy deferred release must not mistake a failed identity read for a gone actor.
            actor.gameObject.ThrowIdReadAt = actor.gameObject.IdReads + 1;
            Sync();
            actor.gameObject.ThrowIdReadAt = 0;
            Check.True(actor._spriteRenderer.forceRenderingOff, "the faulted Sync did not restore visibility");
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "the adopted receipt survives that Sync");
            actor._spriteRenderer.ThrowOnForceRenderingOffSetAlways = false;
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "the readable retry settles the adopted receipt");
            Check.False(actor._spriteRenderer.forceRenderingOff, "native visibility really returned");
            Check.False(actor._arrowAttack.Pointer == clone.Pointer, "native no longer refers to the owned clone");
        }

        private static void LegacyOwnHideMustBeProven()
        {
            Archer actor = PrepareBoardedMusketeer(out _, out ArrowAttack clone, out SpriteRenderer own);
            EnterFirePhase(actor);
            own.ThrowOnEnabledSet = true;
            actor._spriteRenderer.ThrowOnForceRenderingOffSetAlways = true;
            MusketeerVisuals.BeforeReuse(actor);
            Check.True(own.enabled, "legacy hide really failed with a visible own renderer");
            Check.True(actor._spriteRenderer.forceRenderingOff, "legacy native-hide debt remains");
            actor._spriteRenderer.ThrowOnForceRenderingOffSetAlways = false;
            own.ThrowOnEnabledSet = true;
            Check.False(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "an unhidden pending visual cannot demote");
            Check.True(own.enabled, "the hide write fault really left the renderer visible");
            own.ThrowOnEnabledSet = true;
            Sync();
            Check.True(own.enabled, "the faulted Sync still owes its own-renderer hide");
            Check.True(MusketeerRuntime.TryPrepareBoardingDemotion(actor), "the next readable hide settles both visual duties");
            Check.False(own.enabled, "own renderer really hidden before success");
            Check.False(actor._spriteRenderer.forceRenderingOff, "native renderer really visible before success");
            Check.False(actor._arrowAttack.Pointer == clone.Pointer, "clone no longer referenced");
        }

        private static Sprite[] NewAtlas()
        {
            Sprite[] sprites = new Sprite[MusketeerAtlas.FrameCount];
            for (int i = 0; i < sprites.Length; i++) sprites[i] = new Sprite();
            return sprites;
        }

        // ---------- helpers ----------

        /// <summary>装好包 + 视觉的"已登船"火枪手（完整 Tick 路径，非双打桩）。</summary>
        private static Archer PrepareBoardedMusketeer(out ArrowAttack baseAttack, out ArrowAttack clone,
            out SpriteRenderer ownRenderer)
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            Sprite[] sprites = new Sprite[MusketeerAtlas.FrameCount];
            for (int i = 0; i < sprites.Length; i++) sprites[i] = new Sprite();
            MusketeerVisuals.SetAtlasForTests(sprites);

            Archer archer = Fixture.NewArcher("boarded");
            baseAttack = archer._arrowAttack;
            MusketeerIdentity.Units.Add(archer);
            MusketeerRuntime.Tick();          // 装包
            Time.frameCount++;
            MusketeerRuntime.Tick();          // 视觉随包建立
            clone = archer._arrowAttack;
            ownRenderer = FindOwn(archer);

            Check.True(MusketeerRuntime.IsArmedMusketeer(archer), "armed before demotion");
            Check.False(clone.Pointer == baseAttack.Pointer, "clone installed before demotion");
            Check.True(MusketeerVisuals.HasVisual(archer), "own visual live before demotion");
            Check.True(archer._wildlifeScanner.additionalRequirements != null, "wildlife filter installed");
            Check.True(ownRenderer != null, "own renderer found");
            return archer;
        }

        private static void Sync()
        {
            Time.frameCount++;
            Time.deltaTime = 0.05f;
            MusketeerVisuals.Sync();
        }

        /// <summary>进入真实 Fire 相位（与游戏开火中的火铳手一致）：native 此刻确实被自有视觉隐藏。</summary>
        private static void EnterFirePhase(Archer archer)
        {
            archer._shootingTarget = Fixture.NewEnemy(EnemyType.TrollWeak);
            archer._animator.State = new AnimatorStateInfo
            {
                shortNameHash = Animator.StringToHash("Stand"),
                normalizedTime = 0f,
                length = 0.5f,
            };
            Sync();
            Time.time = 0.05f;
            MusketeerVisuals.NotifyShot(archer, 4.3333333f);
            Sync();
            Check.True(archer._spriteRenderer.forceRenderingOff, "native hidden in the live Fire phase");
        }

        private static SpriteRenderer FindOwn(Archer archer)
        {
            Transform native = archer._spriteRenderer.transform;
            if (native == null) return null;
            for (int i = 0; i < native.Children.Count; i++)
            {
                Transform child = native.Children[i];
                if (child == null || child.gameObject == null) continue;
                SpriteRenderer renderer = child.gameObject.GetComponent<SpriteRenderer>();
                if (renderer != null) return renderer;
            }
            return null;
        }
    }
}
