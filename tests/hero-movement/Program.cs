// worker 2026-09-15 英雄移动提速 slice 测试（review 修正后：认领式 actor 字段提升）。
//   直接链接 production il2cpp/HeroArcherMovement.cs，用替身 Unity/游戏类型、替身 Deadlands 探针，
//   覆盖：倍率/有限性、幂等重入、半写回执、CAS 归还（第三方写入保留）、归还失败 pending 重试、
//   Clear 不丢 pending、身份/销毁/容量边界、Deadlands 让位、有界证据日志。
//
// 运行：C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-movement/Tests.csproj
// 退出码 = 失败数（0 = 全过）。

using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

internal static class HeroMovementTests
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("math: scale", MathScale);
        Run("attach: ×1.5 and idempotent reentry", AttachIsIdempotent);
        Run("attach: half write keeps receipts", HalfWriteKeepsReceipts);
        Run("restore: CAS keeps third-party writes (old envelope counterexample)", ThirdPartyWritePreserved);
        Run("restore: failure keeps pending, retry next frame", RestoreFailureKeepsPending);
        Run("clear: restores all and keeps pending receipts", ClearKeepsPending);
        Run("identity: pointer/GO guards and destroyed actor", IdentityGuards);
        Run("identity: transient read failure keeps cleanup", IdentityFailureRetry);
        Run("identity: per-actor isolation", PerActorIsolation);
        Run("capacity: bounded table and bounded log", CapacityBound);
        Run("deadlands: yield matrix", DeadlandsYield);
        Run("logging: bounded evidence line", EvidenceLog);

        Console.WriteLine();
        Console.WriteLine("RESULT: " + _passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    // ============================================================
    // 用例
    // ============================================================

    private static void MathScale()
    {
        Check(HeroArcherMovement.TryScale(4f, out float s) && s == 6f, "scale: ×1.5");
        Check(HeroArcherMovement.TryScale(0.5f, out float half) && half == 0.75f, "scale: fractional");
        Check(!HeroArcherMovement.TryScale(0f, out float zero) && zero == 0f, "scale: zero rejected");
        Check(!HeroArcherMovement.TryScale(-2f, out float negative) && negative == -2f, "scale: negative rejected");
        Check(!HeroArcherMovement.TryScale(float.NaN, out float nan) && float.IsNaN(nan), "scale: NaN rejected");
        Check(!HeroArcherMovement.TryScale(float.PositiveInfinity, out float inf), "scale: +Inf rejected");
        Check(!HeroArcherMovement.TryScale(3.4e38f, out float overflow), "scale: overflow to non-finite rejected");
    }

    private static void AttachIsIdempotent()
    {
        Archer a = NewArcher();
        Check(HeroArcherMovement.Attach(a), "attach: claimed");
        Eq(6f, a.walkSpeed, "attach: walk ×1.5");
        Eq(9f, a.runSpeed, "attach: run ×1.5");
        Eq(1, HeroArcherMovement.ClaimedCount, "attach: one claimed entry");

        for (int i = 0; i < 100; i++) HeroArcherMovement.Attach(a);     // 重入/重复安装
        Eq(6f, a.walkSpeed, "reentry: walk never doubles");
        Eq(9f, a.runSpeed, "reentry: run never doubles");
        Eq(1, HeroArcherMovement.ClaimedCount, "reentry: still one entry");

        HeroArcherMovement.Restore(a);
        Eq(4f, a.walkSpeed, "restore: walk original");
        Eq(6f, a.runSpeed, "restore: run original");
        Eq(0, HeroArcherMovement.ClaimedCount, "restore: ownership released");
        Eq(0, HeroArcherMovement.PendingCleanupCount, "restore: nothing pending");
    }

    private static void HalfWriteKeepsReceipts()
    {
        Archer a = NewArcher();
        a.ThrowOnRunSpeedWrite = true;                                  // 第二个字段写入抛错
        Check(HeroArcherMovement.Attach(a), "half-write: walk still claimed");
        Eq(6f, a.walkSpeed, "half-write: walk ×1.5");
        Eq(6f, a.runSpeed, "half-write: run left native");

        HeroArcherMovement.Attach(a);                                   // 下一帧重试：绝不二次放大
        Eq(6f, a.walkSpeed, "half-write: walk stays ×1.5 once");
        Eq(6f, a.runSpeed, "half-write: run still native");

        a.ThrowOnRunSpeedWrite = false;
        Check(HeroArcherMovement.Attach(a), "half-write: run claimed on retry");
        Eq(9f, a.runSpeed, "half-write: run ×1.5 after retry");

        HeroArcherMovement.Restore(a);
        Eq(4f, a.walkSpeed, "half-write: walk restored");
        Eq(6f, a.runSpeed, "half-write: run restored");
        Eq(0, HeroArcherMovement.ClaimedCount, "half-write: no residual claim");
    }

    private static void ThirdPartyWritePreserved()
    {
        Archer a = NewArcher();
        Check(HeroArcherMovement.Attach(a), "cas: attached");
        Eq(9f, a.runSpeed, "cas: run ×1.5");

        // review 阻断项 2 的反例保留：另一个 writer 写 2.0（落在任何「提升后的 lerp 包络」之内）。
        // 认领式归还只做精确值比对，绝不做 ÷1.5 的推测还原 → 对方的值原样保留。
        a.runSpeed = 2f;
        HeroArcherMovement.Restore(a);
        Eq(2f, a.runSpeed, "cas: foreign run value preserved exactly");
        Eq(4f, a.walkSpeed, "cas: our walk field still restored");
        Eq(0, HeroArcherMovement.ClaimedCount, "cas: ownership released");

        // 再次认领会以现值为新基线（绝不把对方的 2.0 再乘一次旧基线）
        Check(HeroArcherMovement.Attach(a), "cas: re-claim after foreign write");
        Eq(3f, a.runSpeed, "cas: new baseline 2.0 → 3.0");
        Eq(6f, a.walkSpeed, "cas: walk re-claimed from original 4.0");
        HeroArcherMovement.Restore(a);
        Eq(2f, a.runSpeed, "cas: foreign baseline restored");
        Eq(4f, a.walkSpeed, "cas: walk baseline restored");
    }

    private static void RestoreFailureKeepsPending()
    {
        Archer a = NewArcher();
        Check(HeroArcherMovement.Attach(a), "pending: attached");

        a.ThrowOnWalkSpeedWrite = true;
        HeroArcherMovement.Restore(a);
        Eq(1, HeroArcherMovement.PendingCleanupCount, "pending: failed restore keeps receipt");
        Eq(6f, a.walkSpeed, "pending: failed write leaves the field");
        Eq(6f, a.runSpeed, "pending: the other field still restored");

        Check(!HeroArcherMovement.Attach(a), "pending: no re-boost until restored");
        Eq(6f, a.walkSpeed, "pending: still exactly ×1.5 once");

        a.ThrowOnWalkSpeedWrite = false;
        Check(!HeroArcherMovement.RetryCleanup(), "pending: retry completes (no pending left)");
        Eq(4f, a.walkSpeed, "pending: original restored on retry");
        Eq(0, HeroArcherMovement.PendingCleanupCount, "pending: registry clean");
        Eq(0, HeroArcherMovement.ClaimedCount, "pending: no residual claim");

        Check(HeroArcherMovement.Attach(a), "pending: fresh claim after clean restore");
        Eq(6f, a.walkSpeed, "pending: fresh claim ×1.5");
        HeroArcherMovement.Restore(a);
        Eq(4f, a.walkSpeed, "pending: final restore");
    }

    private static void ClearKeepsPending()
    {
        Archer a = NewArcher();
        Check(HeroArcherMovement.Attach(a), "clear: attached");

        a.ThrowOnWalkSpeedWrite = true;
        HeroArcherMovement.Restore(a);
        Eq(1, HeroArcherMovement.PendingCleanupCount, "clear: pending after failed restore");

        HeroArcherMovement.Clear();
        Eq(1, HeroArcherMovement.PendingCleanupCount, "clear: pending receipt kept (never dropped)");

        a.ThrowOnWalkSpeedWrite = false;
        Check(!HeroArcherMovement.RetryCleanup(), "clear: retry after Clear completes");
        Eq(4f, a.walkSpeed, "clear: original restored after Clear");
        Eq(0, HeroArcherMovement.ClaimedCount, "clear: nothing claimed");
    }

    private static void IdentityGuards()
    {
        Archer a = NewArcher();
        Check(HeroArcherMovement.Attach(a), "identity: attached");

        Archer impostor = NewArcher();                                  // 不同 GO/指针
        HeroArcherMovement.Restore(impostor);
        Eq(4f, impostor.walkSpeed, "identity: foreign actor untouched");
        Eq(6f, a.walkSpeed, "identity: unrelated restore never touches our claim");

        Archer unreadable = NewArcher();
        unreadable.Owner.ZeroInstanceId = true;                         // GO id 读不到 → fail-closed
        Check(!HeroArcherMovement.Attach(unreadable), "identity: unreadable GO id refuses attach");
        Eq(4f, unreadable.walkSpeed, "identity: unreadable actor untouched");

        // 对象已销毁（GO 脱链）：Restore 认不出它，义务由 Clear 兜底释放，绝不去猜别的 actor 的字段
        Archer gone = NewArcher();
        Check(HeroArcherMovement.Attach(gone), "identity: doomed actor attached");
        gone.Owner = null;
        HeroArcherMovement.Restore(gone);
        Eq(6f, a.walkSpeed, "identity: destroyed actor never writes another claim");

        HeroArcherMovement.Clear();
        Eq(0, HeroArcherMovement.ClaimedCount, "identity: Clear releases detached obligation");
        Eq(0, HeroArcherMovement.PendingCleanupCount, "identity: nothing pending after Clear");
        Eq(4f, a.walkSpeed, "identity: reachable claim restored by Clear");
        Eq(6f, gone.walkSpeed, "identity: destroyed actor's fields left alone (no blind write)");
    }

    private static void IdentityFailureRetry()
    {
        Archer a = NewArcher();
        Check(HeroArcherMovement.Attach(a), "identity failure: attach");
        a.Owner.ThrowInstanceId = true;
        HeroArcherMovement.Restore(a);
        Eq(1, HeroArcherMovement.PendingCleanupCount, "identity failure: retained unknown owner");
        Check(!HeroArcherMovement.Attach(a), "identity failure: no repeat boost");
        Eq(6f, a.walkSpeed, "identity failure: no mutation while unknown");
        a.Owner.ThrowInstanceId = false;
        HeroArcherMovement.RetryCleanup();
        Eq(4f, a.walkSpeed, "identity failure: walk baseline restored");
        Eq(6f, a.runSpeed, "identity failure: run baseline restored");
        Eq(0, HeroArcherMovement.PendingCleanupCount, "identity failure: completed");
    }

    private static void PerActorIsolation()
    {
        Archer hero = NewArcher();
        Archer other = NewArcher();
        Check(HeroArcherMovement.Attach(hero), "isolation: hero claimed");

        HeroArcherMovement.Reconcile(other);                            // 无关 actor 的独立路径
        Eq(6f, hero.walkSpeed, "isolation: hero claim unaffected");
        Eq(6f, other.walkSpeed, "isolation: other actor claims its own entry only");

        HeroArcherMovement.Restore(hero);
        Eq(4f, hero.walkSpeed, "isolation: hero restored");
        Eq(6f, other.walkSpeed, "isolation: other claim intact");
        HeroArcherMovement.Restore(other);
        Eq(4f, other.walkSpeed, "isolation: other restored to its own baseline");
    }

    private static void CapacityBound()
    {
        Archer h1 = NewArcher();
        Archer h2 = NewArcher();
        Archer h3 = NewArcher();
        Check(HeroArcherMovement.Attach(h1), "cap: first claimed");
        Check(HeroArcherMovement.Attach(h2), "cap: second claimed");
        Check(!HeroArcherMovement.Attach(h3), "cap: third refused");
        Eq(4f, h3.walkSpeed, "cap: third keeps native speeds");
        Eq(2, HeroArcherMovement.ClaimedCount, "cap: table stays bounded at 2");

        HeroArcherMovement.Attach(h3);
        HeroArcherMovement.Attach(h3);
        Eq(1, CountWarnings("[HeroMove] hero movement table full"), "cap: bounded log once");
        Eq(6f, h3.runSpeed, "cap: third run untouched");
    }

    private static void DeadlandsYield()
    {
        Archer a = NewArcher();
        PatchRoles_DeadlandsPowers.Enabled = true;
        PatchRoles_DeadlandsPowers.ByMover[a._mover.Pointer] = new PatchRoles_DeadlandsPowers.UnitRef { Archer = a };
        PatchRoles_DeadlandsPowers.MarkFollower(a);

        Check(HeroArcherMovement.DeadlandsCovers(a), "dl: covered by DL");
        HeroArcherMovement.Reconcile(a);
        Check(!HeroArcherMovement.Attach(a), "dl: attach refuses while covered");
        Eq(4f, a.walkSpeed, "dl: nothing claimed while covered");
        Eq(0, HeroArcherMovement.ClaimedCount, "dl: no entry while covered");

        PatchRoles_DeadlandsPowers.Enabled = false;
        HeroArcherMovement.Reconcile(a);
        Eq(6f, a.walkSpeed, "dl: claimed after DL off (walk)");
        Eq(9f, a.runSpeed, "dl: claimed after DL off (run)");

        PatchRoles_DeadlandsPowers.Enabled = true;
        HeroArcherMovement.Reconcile(a);
        Eq(4f, a.walkSpeed, "dl: yields back to DL (walk restored)");
        Eq(6f, a.runSpeed, "dl: yields back to DL (run restored)");
        Eq(0, HeroArcherMovement.ClaimedCount, "dl: no residual claim");

        PatchRoles_DeadlandsPowers.ThrowFromProbe = true;
        Check(HeroArcherMovement.DeadlandsCovers(a), "dl: probe failure yields (fail-closed)");
        HeroArcherMovement.Reconcile(a);
        Eq(4f, a.walkSpeed, "dl: probe failure claims nothing");
        HeroArcherMovement.Reconcile(a);
        Eq(1, CountWarnings("[HeroMove] deadlands probe failed"), "dl: bounded log once");

        // 单独注册的骑士 mover / 非本 actor 的注册都不得算作覆盖
        PatchRoles_DeadlandsPowers.ThrowFromProbe = false;
        PatchRoles_DeadlandsPowers.Enabled = true;
        PatchRoles_DeadlandsPowers.ByMover[a._mover.Pointer] = new PatchRoles_DeadlandsPowers.UnitRef { Knight = new object() };
        Check(!HeroArcherMovement.DeadlandsCovers(a), "dl: knight registration does not cover the hero");
        PatchRoles_DeadlandsPowers.ByMover[a._mover.Pointer] = new PatchRoles_DeadlandsPowers.UnitRef { Archer = NewArcher() };
        Check(!HeroArcherMovement.DeadlandsCovers(a), "dl: another actor's registration does not cover the hero");
        PatchRoles_DeadlandsPowers.ByMover[a._mover.Pointer] = new PatchRoles_DeadlandsPowers.UnitRef { Archer = a };
        Check(HeroArcherMovement.DeadlandsCovers(a), "dl: own registration + follower covers");
    }

    private static void EvidenceLog()
    {
        Archer a = NewArcher();
        HeroArcherMovement.Attach(a);
        Eq(1, CountInfos("[HeroMove] hero movement"), "evidence: first boost logged once");
        HeroArcherMovement.Attach(a);
        HeroArcherMovement.Restore(a);
        HeroArcherMovement.Attach(a);
        Eq(1, CountInfos("[HeroMove] hero movement"), "evidence: stays single across re-claims");
        Eq(0, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "evidence: nothing escalated to errors");
    }

    // ============================================================
    // 夹具
    // ============================================================

    private static Archer NewArcher()
    {
        GameObject go = new GameObject("archer");
        Archer archer = go.AddComponent<Archer>();
        archer.Pointer = new IntPtr(go.GetInstanceID() * 16 + 1);
        archer._mover = go.AddComponent<Mover>();
        archer._mover.Pointer = new IntPtr(go.GetInstanceID() * 16 + 2);
        return archer;
    }

    // ============================================================
    // 断言 / 复位
    // ============================================================

    private static void Run(string name, Action body)
    {
        Reset();
        try
        {
            body();
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + " threw: " + e.GetBaseException());
        }
    }

    private static void Reset()
    {
        HeroArcherMovement.ResetForTests();
        PatchRoles_DeadlandsPowers.Reset();
        KingdomEnhancedPlugin.Instance.LogSource.Infos.Clear();
        KingdomEnhancedPlugin.Instance.LogSource.Warnings.Clear();
        KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        else
        {
            _failed++;
            Console.WriteLine("FAIL " + name);
        }
    }

    private static void Eq<T>(T expected, T actual, string label)
        => Check(EqualityComparer<T>.Default.Equals(expected, actual),
            label + " (expected " + expected + ", got " + actual + ")");

    private static int CountWarnings(string prefix) => CountLines(KingdomEnhancedPlugin.Instance.LogSource.Warnings, prefix);

    private static int CountInfos(string prefix) => CountLines(KingdomEnhancedPlugin.Instance.LogSource.Infos, prefix);

    private static int CountLines(List<string> lines, string prefix)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i] != null && lines[i].StartsWith(prefix, StringComparison.Ordinal)) count++;
        return count;
    }
}
