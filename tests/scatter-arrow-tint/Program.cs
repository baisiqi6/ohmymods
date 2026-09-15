using System;
using System.Collections.Generic;
using System.Text;
using KingdomEnhancedMod;
using KingdomScatterTint.Tests;
using UnityEngine;

/// <summary>
/// ScatterArrowTint 的直接链接回归：真实生产源码（含真实 ArcherOptionsScope/OptionalQoLScope）编译进本程序集，
/// 只桩掉 Unity / 游戏类型 / Harmony 属性 / root 配置边界。
/// </summary>
internal static class Program
{
    private static int _checks;
    private static int _failed;

    private static void Case(string name, Action body)
    {
        _checks++;
        try
        {
            body();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    private static void Assert(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static string Bytes(byte[] bytes)
    {
        var text = new StringBuilder("[");
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(bytes[i]);
        }
        return text.Append(']').ToString();
    }

    private static void Main()
    {
        // ---------- payload 写入（发送侧格式） ----------

        Case("payload: no tint is strictly the native 1 byte", () =>
        {
            var f = new Fixture();
            byte[] perfect = f.PrepareAndCapturePayload(true, false);
            Assert(perfect.Length == 1 && perfect[0] == 1, "perfect=true must be [1], got " + Bytes(perfect));
            byte[] plain = f.PrepareAndCapturePayload(false, false);
            Assert(plain.Length == 1 && plain[0] == 0, "perfect=false must be [0], got " + Bytes(plain));
            Assert(ByteBuffer.PrepWriteCalls == 2, "writer must not reset the buffer itself");
        });

        Case("payload: tint appends magic 'SCT1' + version, total 6", () =>
        {
            var f = new Fixture();
            byte[] perfect = f.PrepareAndCapturePayload(true, true);
            Assert(perfect.Length == 6, "tinted payload must be 6 bytes, got " + Bytes(perfect));
            Assert(perfect[0] == 1, "perfect byte stays first and true");
            Assert(perfect[1] == (byte)'S' && perfect[2] == (byte)'C' && perfect[3] == (byte)'T'
                && perfect[4] == (byte)'1', "magic must be 'SCT1', got " + Bytes(perfect));
            Assert(perfect[5] == 1, "version must be 1");
            byte[] notPerfect = f.PrepareAndCapturePayload(false, true);
            Assert(notPerfect[0] == 0 && notPerfect.Length == 6, "perfect=false + tint must stay 6 bytes, got " + Bytes(notPerfect));
        });

        Case("payload: module never preps or sends anything", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            var softSim = new NetworkSoftSimulator();
            arrow.gameObject.AddComponentForTests(softSim);
            ScatterArrowTint.Apply(arrow);
            ScatterArrowTint.ResetArrow(arrow);
            ScatterArrowTint.Tick();
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            NetworkBigBoss.HasWorldAuth = false;
            f.DispatchReceive(arrow);
            Assert(ByteBuffer.PrepWriteCalls == 0, "module must not reset the write buffer");
            Assert(softSim.SendCount == 0, "module must not send on the softsim channel");
        });

        // ---------- Apply ----------

        Case("apply: extra arrow gets the gold mix with alpha kept", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "extra arrow should be tinted");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)),
                "written color must be the gold mix, got " + arrow._spriteRenderer.color);
            Assert(arrow._spriteRenderer.color.a == baseColor.a, "alpha must be preserved exactly");
            Assert(ScatterArrowTint.TrackedCount == 1, "one receipt must be registered");
        });

        Case("apply: untinted arrows are never touched without Apply", () =>
        {
            var f = new Fixture();
            var mainArrow = f.NewArrow();
            Color before = mainArrow._spriteRenderer.color;
            ScatterArrowTint.Tick();
            ScatterArrowTint.ResetArrow(mainArrow);
            Assert(Fixture.SameColor(mainArrow._spriteRenderer.color, before), "untouched arrow color must not change");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt may exist for an untouched arrow");
        });

        Case("apply: config off stops new tints (no write, no receipt)", () =>
        {
            var f = new Fixture();
            ModConfig.ArcherScatterEnabled.Value = false;
            var arrow = f.NewArrow();
            Color before = arrow._spriteRenderer.color;
            Assert(!ScatterArrowTint.Apply(arrow), "tinting must be refused when scatter is off");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, before), "refused arrow must not be written");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt may be created when refused");
        });

        Case("apply: no world context is fail-closed", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            f.Managers.world = null;
            Assert(!ScatterArrowTint.Apply(arrow), "no world -> refuse");
            f.Managers.world = f.World;
            BiomeHolder.Inst = null;
            Assert(!ScatterArrowTint.Apply(arrow), "no biome scope -> refuse");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt may be created");
        });

        Case("apply: an arrow outside the current layer is refused", () =>
        {
            var f = new Fixture();
            var foreign = f.NewArrow();
            f.Managers.world.gameLayer = f.NewLayer();            // 当前 world 换层：foreign 不在新 layer 下
            Color before = foreign._spriteRenderer.color;
            Assert(!ScatterArrowTint.Apply(foreign), "arrow from another layer/world must not be tinted");
            Assert(Fixture.SameColor(foreign._spriteRenderer.color, before), "no write");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt");
        });

        Case("apply: another arrow component on the same GO cannot inherit a receipt", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Color baseColor = renderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            renderer.ThrowOnColorWrite = true;
            ScatterArrowTint.ResetArrow(arrow);                   // 归还失败 → PendingRestore
            Assert(ScatterArrowTint.TrackedCount == 1, "pending receipt");

            var reused = new Arrow { gameObject = arrow.gameObject, transform = arrow.transform };
            int writes = renderer.ColorWriteCount;
            Assert(!ScatterArrowTint.Apply(reused), "同 GOid/ptr 的伪复用不得继承旧回执");
            Assert(renderer.ColorWriteCount == writes, "refusal must not write");
            Assert(ScatterArrowTint.TrackedCount == 1, "no second receipt");
            Assert(Fixture.SameColor(renderer.color, Fixture.ExpectedTint(baseColor)), "color untouched by the refusal");
        });

        Case("apply: repeat Apply neither re-tints nor overwrites external color", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Assert(ScatterArrowTint.Apply(arrow), "first apply tints");
            int writes = arrow._spriteRenderer.ColorWriteCount;
            Assert(ScatterArrowTint.Apply(arrow), "second apply reports already tinted");
            Assert(arrow._spriteRenderer.ColorWriteCount == writes, "second apply must not write again");

            var external = new Color(0.1f, 0.1f, 0.1f, 1f);
            arrow._spriteRenderer.color = external;
            Assert(!ScatterArrowTint.Apply(arrow), "externally recolored arrow is no longer ours");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, external), "external color must never be overwritten");
        });

        Case("apply: cap 128 refuses the 129th arrow and evicts nothing", () =>
        {
            var f = new Fixture();
            var first = f.NewArrow();
            Assert(ScatterArrowTint.Apply(first), "first arrow tints");
            for (int i = 1; i < ScatterArrowTint.Capacity; i++)
                Assert(ScatterArrowTint.Apply(f.NewArrow()), "arrow " + i + " should tint");

            var extra = f.NewArrow();
            Color extraBase = extra._spriteRenderer.color;
            Assert(!ScatterArrowTint.Apply(extra), "cap must refuse new tints");
            Assert(Fixture.SameColor(extra._spriteRenderer.color, extraBase), "refused arrow must not be written");
            Assert(ScatterArrowTint.TrackedCount == ScatterArrowTint.Capacity, "registry must stay at the cap");
            Assert(Fixture.SameColor(first._spriteRenderer.color, Fixture.ExpectedTint(Fixture.DefaultBase)),
                "existing receipts must not be evicted");
        });

        Case("apply: arrow without renderer is refused", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow(withRenderer: false);
            Assert(!ScatterArrowTint.Apply(arrow), "no renderer -> refuse");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt may be created");
        });

        // ---------- ResetArrow（OnEnable/池复用） ----------

        Case("reset: restores the base color and clears the receipt", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            ScatterArrowTint.ResetArrow(arrow);
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "base color must be restored");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be gone");
        });

        Case("reset: still restores after the feature config is turned off", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            ModConfig.ArcherScatterEnabled.Value = false;
            ScatterArrowTint.ResetArrow(arrow);
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "reset must ignore the switch");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be gone");
        });

        Case("reuse: pooled arrow tinted again after ResetArrow accumulates nothing", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "first life tints");
            ScatterArrowTint.ResetArrow(arrow);                       // OnEnable 前缀（池复用/新生命）
            Assert(ScatterArrowTint.Apply(arrow), "recycled arrow tints again");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)),
                "gold must be mixed from the base color, never gold-over-gold");
            Assert(ScatterArrowTint.TrackedCount == 1, "exactly one receipt per live tint");
            ScatterArrowTint.ResetArrow(arrow);
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "base restored");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be gone");
        });

        Case("reset: CAS never overwrites an external color", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            var external = new Color(1f, 0f, 0f, 1f);
            arrow._spriteRenderer.color = external;
            ScatterArrowTint.ResetArrow(arrow);
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, external), "external color must survive reset");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        Case("reset: a replaced renderer is never written", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer tinted = arrow._spriteRenderer;
            Color baseColor = tinted.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");

            var replacement = new SpriteRenderer { color = new Color(0f, 1f, 0f, 1f) };
            arrow.gameObject.AddComponentForTests(replacement);
            arrow._spriteRenderer = replacement;

            ScatterArrowTint.ResetArrow(arrow);
            Assert(Fixture.SameColor(replacement.color, new Color(0f, 1f, 0f, 1f)), "replacement renderer must not be written");
            Assert(Fixture.SameColor(tinted.color, baseColor), "the renderer we tinted is restored");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be gone");
        });

        Case("reset: destroyed renderer drops the receipt without writing", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            int writes = renderer.ColorWriteCount;
            renderer.DestroyForTests();
            ScatterArrowTint.ResetArrow(arrow);
            Assert(renderer.ColorWriteCount == writes, "no write after destroy");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        Case("reset: restores through the receipt's renderer when the Arrow component was replaced", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Color baseColor = renderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            var replacement = new Arrow { gameObject = arrow.gameObject, transform = arrow.transform };
            ScatterArrowTint.ResetArrow(replacement);             // 同 GO 的另一个 arrow 组件触发 OnEnable
            Assert(Fixture.SameColor(renderer.color, baseColor), "restore uses the receipt's renderer");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt resolved");
        });

        Case("scatter off keeps fired arrows gold until recycle", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            ModConfig.ArcherScatterEnabled.Value = false;
            int writes = arrow._spriteRenderer.ColorWriteCount;
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)),
                "already fired arrow keeps the tint while the switch is off");
            Assert(arrow._spriteRenderer.ColorWriteCount == writes, "no write while off");
            Assert(ScatterArrowTint.TrackedCount == 1, "receipt stays");
            arrow.gameObject.activeSelf = false;                  // 回收
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "recycle returns the base color");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt resolved");
        });

        // ---------- 写异常回执 ----------

        Case("write failure: receipt stays restore-only, Tick never re-tints", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Color baseColor = renderer.color;
            renderer.ThrowOnColorWrite = true;
            Assert(!ScatterArrowTint.Apply(arrow), "failed write must report false (sender sends no tint tag)");
            Assert(ScatterArrowTint.TrackedCount == 1, "receipt must be retained as PendingRestore");
            int writes = renderer.ColorWriteCount;

            Time.unscaledTime = 0.1f;
            ScatterArrowTint.Tick();
            Assert(renderer.ColorWriteCount == writes, "backoff must delay any retry");

            Time.unscaledTime = 0.6f;
            ScatterArrowTint.Tick();
            Assert(ScatterArrowTint.TrackedCount == 0, "nothing of ours landed -> receipt resolved by the restore path");
            Assert(renderer.ColorWriteCount == writes, "Tick must never re-tint");
            Assert(Fixture.SameColor(renderer.color, baseColor), "color stays base");

            renderer.ThrowOnColorWrite = false;
            Assert(ScatterArrowTint.Apply(arrow), "once resolved, a fresh Apply may tint again");
            Assert(Fixture.SameColor(renderer.color, Fixture.ExpectedTint(baseColor)), "fresh tint is mixed from the base");
        });

        Case("write landed then threw: pending receipt restores, repeat Apply is refused", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Color baseColor = renderer.color;
            renderer.ThrowOnColorWrite = true;
            renderer.WriteLandsThenThrows = true;
            Assert(!ScatterArrowTint.Apply(arrow), "throwing write reports false");
            Assert(Fixture.SameColor(renderer.color, Fixture.ExpectedTint(baseColor)), "write had landed");
            Assert(ScatterArrowTint.TrackedCount == 1, "receipt must be retained (color was left behind)");

            int writes = renderer.ColorWriteCount;
            Assert(!ScatterArrowTint.Apply(arrow), "pending restore must refuse a new tint");
            Assert(renderer.ColorWriteCount == writes, "refusal must not write");
            Assert(ScatterArrowTint.TrackedCount == 1, "refusal must not add or drop receipts");

            renderer.ThrowOnColorWrite = false;
            Time.unscaledTime = 0.6f;
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(renderer.color, baseColor), "restore puts the base color back");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be gone");
        });

        Case("reset failure: receipt survives, a new life cannot be re-tinted, Tick restores", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Color baseColor = renderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            renderer.ThrowOnColorWrite = true;
            ScatterArrowTint.ResetArrow(arrow);                  // OnEnable 前缀：归还失败
            Assert(ScatterArrowTint.TrackedCount == 1, "failed restore must keep the receipt");
            Assert(Fixture.SameColor(renderer.color, Fixture.ExpectedTint(baseColor)), "color still ours");

            int writes = renderer.ColorWriteCount;
            int reads = renderer.ColorReadCount;
            Assert(!ScatterArrowTint.Apply(arrow), "普通新 life 在待归还期间不得重染");
            Assert(renderer.ColorWriteCount == writes, "refusal must not write");
            Assert(renderer.ColorReadCount == reads, "refusal must not re-read the color as a new base");
            Assert(ScatterArrowTint.TrackedCount == 1, "receipt must stay pending");

            Time.unscaledTime = 0.1f;
            ScatterArrowTint.Tick();
            Assert(ScatterArrowTint.TrackedCount == 1, "backoff must not be bypassed by Tick");
            Assert(renderer.ColorReadCount == reads, "refused Apply must not have cleared the backoff");
            Assert(Fixture.SameColor(renderer.color, Fixture.ExpectedTint(baseColor)), "still waiting");

            renderer.ThrowOnColorWrite = false;
            Time.unscaledTime = 1f;
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(renderer.color, baseColor), "Tick retries restore only");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt resolved");
            Assert(renderer.ColorWriteCount == writes + 1, "exactly one restore write, never a re-tint");
        });

        Case("identity read failure is unknown: receipt kept, restores after recovery", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Color baseColor = renderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");

            renderer.gameObject.ThrowOnInstanceId = true;        // 临时身份读失败（未销毁）
            ScatterArrowTint.ResetArrow(arrow);
            Assert(ScatterArrowTint.TrackedCount == 1, "unknown identity must never drop the receipt");
            Assert(Fixture.SameColor(renderer.color, Fixture.ExpectedTint(baseColor)), "no write while unknown");

            renderer.gameObject.ThrowOnInstanceId = false;
            Time.unscaledTime = 1f;
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(renderer.color, baseColor), "restore after recovery");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt resolved");
        });

        // ---------- Tick 生命周期 ----------

        Case("tick: live arrows keep their tint", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            int writes = arrow._spriteRenderer.ColorWriteCount;
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(Fixture.DefaultBase)), "tint must stay");
            Assert(arrow._spriteRenderer.ColorWriteCount == writes, "live arrows must not be rewritten");
            Assert(ScatterArrowTint.TrackedCount == 1, "receipt must stay");
        });

        Case("tick: despawned (pool recycle) arrow is restored and dropped", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            arrow.gameObject.activeSelf = false;
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "pool recycle must give the base color back");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        Case("tick: world exit is restored and dropped", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            f.Managers.world = new World { gameLayer = f.NewLayer() };
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "world exit must give the base color back");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        Case("tick: layer-only switch inside the same world restores", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            f.Managers.world.gameLayer = f.NewLayer();            // 同一 world 指针，仅 layer 变化
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "layer change must give the base color back");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        Case("tick: unknown world context restores", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            BiomeHolder.Inst = null;                              // scope/world 不可得
            ScatterArrowTint.Tick();
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "unknown world must give the base color back");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        Case("tick: destroyed objects drop the receipt without writing", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            SpriteRenderer renderer = arrow._spriteRenderer;
            Assert(ScatterArrowTint.Apply(arrow), "tint");
            int writes = renderer.ColorWriteCount;
            arrow.gameObject.DestroyForTests();
            ScatterArrowTint.Tick();
            Assert(renderer.ColorWriteCount == writes, "destroyed object must never be written");
            Assert(ScatterArrowTint.TrackedCount == 0, "receipt must be dropped");
        });

        // ---------- 接收边界：原生 1 字节必须完全不动 ----------

        Case("receive: native 1-byte payload runs the original body untouched", () =>
        {
            var f = new Fixture();
            var arrow = f.NewArrow();
            f.LoadWire(new byte[] { 1 });
            Assert(f.DispatchReceive(arrow), "1-byte payload must fall through to native");
            Assert(arrow.Perfect, "native must honour perfect");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 1, "native consumed exactly 1 byte");
            Assert(ByteBuffer.OverreadWarnings == 0, "no overread");
            Assert(ScatterArrowTint.TrackedCount == 0, "native payload must never tint");

            f.LoadWire(new byte[] { 0 });
            Assert(f.DispatchReceive(arrow), "1-byte payload must fall through to native");
            Assert(f.NativeLengthErrors == 0, "native must not see a length error");
        });

        Case("receive: own 6 bytes on a client keeps perfect and tints", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "own payload must be taken over");
            Assert(f.NativeBodyRuns == 0 && f.NativeLengthErrors == 0, "native must be skipped, no length error");
            Assert(arrow.Perfect, "perfect byte must still be honoured");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 6, "exactly 6 bytes consumed");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)),
                "client extra arrow must be tinted");
            Assert(ScatterArrowTint.TrackedCount == 1, "one receipt");
        });

        Case("receive: own 6 bytes on the host never tints", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = true;
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "own payload must be taken over");
            Assert(arrow.Perfect, "perfect byte must still be honoured");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 6, "exactly 6 bytes consumed");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "host already owns the tint; no re-apply");
            Assert(ScatterArrowTint.TrackedCount == 0, "no client-side receipt on the host");
        });

        Case("receive: fire arrow gets no perfect but the packet is taken over", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow(fireArrow: true);
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "own payload must be taken over");
            Assert(!arrow.Perfect, "native early-exits for fire arrows; no perfect");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 6, "exactly 6 bytes consumed");
            Assert(ScatterArrowTint.TrackedCount == 1, "the extra arrow is ours; client tint still applies");
        });

        Case("receive: malformed own-format packets are handed to native unconsumed", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;

            byte[][] malformed =
            {
                new byte[] { 2, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 },          // perfect 非 0/1
                new byte[] { 1, (byte)'X', (byte)'C', (byte)'T', (byte)'1', 1 },          // magic 坏
                new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 2 },          // version 坏
                new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1' },             // 短包（5）
                new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1, 0 },       // 长包（7）
            };

            for (int i = 0; i < malformed.Length; i++)
            {
                var arrow = f.NewArrow();
                f.LoadWire(malformed[i]);
                int errorsBefore = f.NativeLengthErrors;
                Assert(f.DispatchReceive(arrow), "malformed payload #" + i + " must fall through to native");
                Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength,
                    "malformed payload #" + i + " must not be consumed");
                Assert(f.NativeLengthErrors == errorsBefore + 1,
                    "native keeps its own length error for #" + i);
                Assert(!arrow.Perfect, "malformed payload #" + i + " must not perfect");
                Assert(ScatterArrowTint.TrackedCount == 0, "malformed payload #" + i + " must not tint");
            }
        });

        Case("receive: transport peek failure falls back to native with the cursor intact", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow();
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            ByteBuffer.ThrowOnBufferAccess = true;
            Assert(f.DispatchReceive(arrow), "peek failure must fall back to native");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength, "cursor must be untouched");
            Assert(f.NativeLengthErrors == 1, "native sees its own 6-byte payload and keeps its message");
            Assert(ScatterArrowTint.TrackedCount == 0, "no tint on fallback");
        });

        Case("receive: PerfectShot failure after consume neither rewinds nor replays", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow();
            arrow.ThrowOnPerfectShot = true;
            Color baseColor = arrow._spriteRenderer.color;
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "taken over even though perfect threw");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 6, "cursor advanced exactly once");
            Assert(arrow.PerfectShotCalls == 1, "perfect is attempted exactly once");
            Assert(f.NativeLengthErrors == 0, "no native replay/error");
            Assert(TestLog.Count("PerfectShot failed") == 1, "failure must be isolated and logged once");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)),
                "tint still lands after a perfect failure");

            // 同一条 payload 不会被二次消费/二次回放：剩余 0 只能交回原生，游标绝不回退。
            int cursor = ByteBuffer.PollIndex();
            f.DispatchReceive(arrow);
            Assert(ByteBuffer.PollIndex() == cursor, "no second consumption of the same payload");
            Assert(arrow.PerfectShotCalls == 1, "no replayed perfect call");
        });

        Case("receive: unreadable isFireArrow keeps the packet consumed and never perfects", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow();
            arrow.ThrowOnIsFireArrowRead = true;
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "already-consumed packet must stay consumed");
            Assert(!arrow.Perfect, "unreadable fire flag must not perfect");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 6, "exactly 6 bytes consumed");
            Assert(f.NativeLengthErrors == 0, "no native replay");
            Assert(ScatterArrowTint.TrackedCount == 1, "client tint is independent of the perfect path");
        });

        Case("receive: client tints host-tagged extras even with local Scatter off", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            ModConfig.ArcherScatterEnabled.Value = false;         // 客机本地默认 false
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "own payload must be taken over");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)),
                "host-tagged extra must show gold on a client whose local switch is off");
            Assert(ScatterArrowTint.TrackedCount == 1, "client receipt");
        });

        Case("receive: client path still honours the master mod/world guards", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            ModConfig.Enabled.Value = false;                      // master 关：不染
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;
            f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
            Assert(!f.DispatchReceive(arrow), "own payload must still be taken over");
            Assert(arrow.Perfect, "perfect semantics survive the guard");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "no tint when the mod is off");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt when the mod is off");
        });

        Case("receive: read failure mid-consume never falls back to native", () =>
        {
            int[] failingCall = { 2, 6 };
            bool[] afterAdvance = { false, true };
            for (int i = 0; i < failingCall.Length; i++)
            {
                for (int j = 0; j < afterAdvance.Length; j++)
                {
                    var f = new Fixture();
                    NetworkBigBoss.HasWorldAuth = false;
                    var arrow = f.NewArrow();
                    f.LoadWire(new byte[] { 1, (byte)'S', (byte)'C', (byte)'T', (byte)'1', 1 });
                    ByteBuffer.ThrowOnReadCall = failingCall[i];
                    ByteBuffer.ThrowAfterAdvance = afterAdvance[j];

                    string label = "read #" + failingCall[i] + " after=" + afterAdvance[j];
                    Assert(!f.DispatchReceive(arrow), label + " must stay owned (never re-run native)");
                    Assert(f.NativeBodyRuns == 0 && f.NativeLengthErrors == 0, label + " must not run native");
                    int expected = Fixture.MotionPrefixLength + (afterAdvance[j] ? failingCall[i] : failingCall[i] - 1);
                    Assert(ByteBuffer.PollIndex() == expected, label + " cursor must move only by performed reads");
                    Assert(arrow.PerfectShotCalls == 0, label + " must not perfect");
                    Assert(ScatterArrowTint.TrackedCount == 0, label + " must not tint");
                }
            }
        });

        // ---------- 端到端：发送 → 线上 → 接收 ----------

        Case("end-to-end: tinted extra arrow survives the softsim motion prefix", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;

            byte[] payload = f.PrepareAndCapturePayload(true, true);
            f.LoadWire(payload);
            Assert(!f.DispatchReceive(arrow), "receiver must take over the own format");
            Assert(arrow.Perfect, "perfect survives the round trip");
            Assert(ByteBuffer.PollIndex() == Fixture.MotionPrefixLength + 6, "cursor ends right after the payload");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, Fixture.ExpectedTint(baseColor)), "tint survives the round trip");
        });

        Case("end-to-end: untinted payload keeps the native 1-byte semantics", () =>
        {
            var f = new Fixture();
            NetworkBigBoss.HasWorldAuth = false;
            var arrow = f.NewArrow();
            Color baseColor = arrow._spriteRenderer.color;

            byte[] payload = f.PrepareAndCapturePayload(true, false);
            Assert(payload.Length == 1, "no tint -> strictly 1 byte");
            f.LoadWire(payload);
            Assert(f.DispatchReceive(arrow), "1-byte payload stays with native");
            Assert(arrow.Perfect, "native perfect semantics");
            Assert(Fixture.SameColor(arrow._spriteRenderer.color, baseColor), "no tint without the tag");
            Assert(ScatterArrowTint.TrackedCount == 0, "no receipt");
        });

        // ---------- 钩子面契约 ----------

        Case("patch: single hook on Arrow.ReceiveInitialise with a bool prefix", () =>
        {
            PatchBridge.AssertPatchContract();
        });

        Console.WriteLine("RESULT checks=" + _checks + " failed=" + _failed);
        Environment.Exit(_failed == 0 ? 0 : 1);
    }
}
