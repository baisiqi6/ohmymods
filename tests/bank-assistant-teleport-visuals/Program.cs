// Focused regression for BankAssistantTeleportVisuals against the real shared teleport FX.
// Scope: presentation state machine only (two-ended stripes, reveal window, hard invalidation,
// repeat/jump churn, pool-recycle forget, fail-open paths, pause freeze). Wiring through the
// coordinator lives in tests/greek-bank-assistants-scope.
using System;
using System.Collections.Generic;
using System.Linq;
using KingdomEnhancedMod;
using UnityEngine;

static class Program
{
    static int _passed, _failed;

    static void Test(string label, Action body)
    {
        ResetWorld();
        try
        {
            body();
            _passed++;
            Console.WriteLine("PASS " + label);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + label + ": " + e.Message);
        }
    }

    static void Assert(bool value, string label)
    {
        if (!value) throw new Exception(label);
    }

    static void Eq(int expected, int actual, string label)
    {
        if (expected != actual) throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    static void Eq(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0005f)
            throw new Exception(label + ": " + actual + ", expected " + expected);
    }

    static GameObject NewActor(string name)
    {
        var actor = new GameObject(name);
        return actor;
    }

    /// <summary>Cleans presentation slots and drains the shared FX pool without calling Clear.</summary>
    static void ResetWorld()
    {
        for (int i = 0; i < 8; i++) BankAssistantTeleportVisuals.EndSlot(i);
        int guard = 0;
        while (CoinCourierTeleportFx.ActiveCount > 0 && guard++ < 16)
            CoinCourierTeleportFx.TickForFrame(1f, 900000 + guard);
        Time.time = 0f;
        Time.deltaTime = 0.02f;
        Time.frameCount++;
        NetworkBigBoss.IsOnline = false;
        LineRenderer.FailSetPosition = false;
        foreach (var item in GameObject.All) item.ForceNull = false;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
    }

    static List<LineRenderer> ActiveDashes()
        => GameObject.All
            .Where(g => g.name.StartsWith("KEM_CoinCourierTeleportDash", StringComparison.Ordinal)
                && g.activeInHierarchy)
            .SelectMany(g => g.Components.OfType<LineRenderer>())
            .ToList();

    static Vector3 DashCenter(LineRenderer line)
        => new Vector3(
            (line.Positions[0].x + line.Positions[3].x) / 2f,
            (line.Positions[0].y + line.Positions[3].y) / 2f,
            0f);

    /// <summary>真实宽度契约：widthCurve 两中键峰值 × widthMultiplier（生产曲线只建一次）。</summary>
    static float PeakWidth(LineRenderer line)
    {
        var keys = line.widthCurve.keys;
        return Math.Max(keys[1].value, keys[2].value) * line.widthMultiplier;
    }

    static int Main()
    {
        Test("one jump plays a stripe group at each end with the actor's sorting and scale", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID = 7;
            renderer.sortingOrder = 42;
            var from = new Vector3(2f, 0.5f, 0f);
            var to = new Vector3(7f, 0.5f, 0f);

            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, from, to), "presentation started");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "two effect groups");
            var dashes = ActiveDashes();
            Eq(32, dashes.Count, "sixteen dashes per end");
            Eq(16, dashes.Count(d => Math.Abs(DashCenter(d).x - 2f) <= 0.12f), "departure dashes at the origin");
            Eq(16, dashes.Count(d => Math.Abs(DashCenter(d).x - 7f) <= 0.12f), "destination dashes at the target");
            // 方向证据：同一时刻出发端（Departure）是满长起步，到达端（Arrival）还是短的入场起点。
            float fromSpan = dashes
                .Where(d => Math.Abs(DashCenter(d).x - 2f) <= 0.12f)
                .Sum(d => Math.Abs(d.Positions[3].x - d.Positions[0].x));
            float toSpan = dashes
                .Where(d => Math.Abs(DashCenter(d).x - 7f) <= 0.12f)
                .Sum(d => Math.Abs(d.Positions[3].x - d.Positions[0].x));
            Assert(toSpan < fromSpan * 0.8f,
                "the destination end starts short (arrival) while the origin plays the full-length departure");
            Assert(dashes.All(d => DashCenter(d).y > 0.5f && DashCenter(d).y < 1.25f), "dash rows stand above the foot anchor Y");
            Assert(dashes.All(d => d.sortingLayerID == 7 && d.sortingOrder == 42), "actor sorting passed to both ends");
            Assert(dashes.All(d => PeakWidth(d) >= 0.012f - 0.0001f && PeakWidth(d) <= 0.026f + 0.0001f),
                "every stripe stays inside the approved .012-.026 width band");
            Assert(!renderer.enabled, "actor hidden during the window");
            Assert(BankAssistantTeleportVisuals.IsWaiting(0), "waiting");
        });
        Test("stripes stand over the foot anchor and cover the body height", () =>
        {
            var actor = NewActor("assistant");
            actor.AddComponent<SpriteRenderer>();
            var from = new Vector3(2f, 2.3f, 0f);   // 空中脚锚：Y 不被投影落地
            var to = new Vector3(7f, 2.3f, 0f);

            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, from, to), "started");
            var dashes = ActiveDashes();
            Eq(32, dashes.Count, "sixteen dashes per end");
            foreach (float endX in new[] { 2f, 7f })
            {
                var rows = dashes
                    .Where(d => Math.Abs(DashCenter(d).x - endX) <= 0.12f)
                    .Select(d => DashCenter(d).y)
                    .OrderBy(y => y)
                    .ToList();
                Eq(16, rows.Count, "sixteen rows over end " + endX);
                Assert(rows[0] > 2.3f + 0.02f, "lowest row clears the foot anchor at " + endX);
                Assert(rows[15] - rows[0] > 0.5f, "rows span the body height at " + endX);
                Assert(rows[15] > 2.3f + 0.55f && rows[15] < 2.3f + 0.7f, "top row reaches near the fitted body top at " + endX);
            }
            Assert(dashes.All(d => Math.Abs(DashCenter(d).y) > 1f), "airborne rows are not projected to the ground");
            Assert(dashes.All(d => (d.Positions[3].x - d.Positions[0].x) / 2f <= 0.32f), "stripe half-lengths stay compact");
            Assert(dashes.All(d => d.Positions[0].z == 0f && d.Positions[3].z == 0f), "stripes keep the passed z");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "both groups still playing");
        });
        Test("original slots run horizontal stripes and new slots vertical, both ends matching", () =>
        {
            var cases = new (int Slot, bool Vertical)[] { (0, false), (3, false), (4, true), (7, true) };
            foreach (var item in cases)
            {
                var actor = NewActor("assistant-" + item.Slot);
                actor.AddComponent<SpriteRenderer>();
                var from = new Vector3(2f, 1.4f, 0f);
                var to = new Vector3(6f, 1.4f, 0f);
                Assert(BankAssistantTeleportVisuals.NotifyTeleport(item.Slot, actor, from, to),
                    "slot " + item.Slot + ": started");

                foreach (float endX in new[] { 2f, 6f })
                {
                    // 竖纹线心是 anchor.x + x 偏移（|偏移| ≤ .29），横纹线心是 anchor.x + 轻微中心错位（|错位| ≤ .08）。
                    var lines = ActiveDashes()
                        .Where(d => Math.Abs(DashCenter(d).x - endX) <= (item.Vertical ? 0.31f : 0.12f))
                        .ToList();
                    Eq(16, lines.Count, "sixteen stripes over end " + endX + " of slot " + item.Slot);
                    bool origin = endX < 4f;   // 2f = 出发端（Departure 满长起步），6f = 到达端（Arrival 短起步）
                    if (item.Vertical)
                    {
                        Assert(lines.All(d => Math.Abs(d.Positions[3].y - d.Positions[0].y)
                                > Math.Abs(d.Positions[3].x - d.Positions[0].x)),
                            "slot " + item.Slot + " end " + endX + ": stripes stay vertical-dominant");
                        Assert(lines.All(d => d.Positions[3].x > d.Positions[0].x && d.Positions[3].y > d.Positions[0].y
                                && Math.Abs(Math.Abs(d.Positions[3].x - d.Positions[0].x)
                                    - 0.10f * Math.Abs(d.Positions[3].y - d.Positions[0].y))
                                    <= 1e-3f * Math.Abs(d.Positions[3].y - d.Positions[0].y) + 1e-5f),
                            "slot " + item.Slot + " end " + endX + ": one uniform lean |dx|/|dy| = .10 (mutually parallel)");
                        Assert(lines.All(d => d.Positions[0].x >= endX - 0.31f && d.Positions[0].x <= endX + 0.31f),
                            "slot " + item.Slot + " end " + endX + ": stripes stay within the body width");
                        Assert(lines.All(d => d.Positions[0].y > 1.4f),
                            "slot " + item.Slot + " end " + endX + ": stripes start above the foot anchor");
                        float reach = lines.Max(d => d.Positions[3].y);
                        if (origin)
                            Assert(reach > 1.4f + 0.6f, "slot " + item.Slot + ": departure reaches the fitted body height");
                        else
                            Assert(reach < 1.4f + 0.8f * 0.9f, "slot " + item.Slot + ": arrival starts short before its anchor");
                    }
                    else
                    {
                        Assert(lines.All(d => Math.Abs(d.Positions[3].x - d.Positions[0].x)
                                > Math.Abs(d.Positions[3].y - d.Positions[0].y)),
                            "slot " + item.Slot + " end " + endX + ": stripes stay horizontal-dominant");
                        Assert(lines.All(d => d.Positions[3].x > d.Positions[0].x && d.Positions[3].y > d.Positions[0].y
                                && Math.Abs(Math.Abs(d.Positions[3].y - d.Positions[0].y)
                                    - 0.10f * Math.Abs(d.Positions[3].x - d.Positions[0].x))
                                    <= 1e-3f * Math.Abs(d.Positions[3].x - d.Positions[0].x) + 1e-5f),
                            "slot " + item.Slot + " end " + endX + ": one uniform rise |dy|/|dx| = .10 (mutually parallel)");
                        Assert(lines.Max(d => d.Positions[3].y) > 1.4f + 0.6f,
                            "slot " + item.Slot + " end " + endX + ": stripes reach the upper body");
                    }
                }
                // Arrival 增长到 .12 显形锚点后覆盖全身；出发端此时只剩残线（同风格两方向彼此独立）。
                CoinCourierTeleportFx.TickForFrame(0.12f, 30000 + item.Slot);
                var grown = ActiveDashes()
                    .Where(d => Math.Abs(DashCenter(d).x - 6f) <= (item.Vertical ? 0.31f : 0.12f)
                        && MathF.Max(d.startColor.a, d.endColor.a) > 0.05f)
                    .ToList();
                Assert(grown.Count >= 1, "slot " + item.Slot + ": arrival still visible at the anchor");
                if (item.Vertical)
                    Assert(grown.Max(d => d.Positions[3].y) > 1.4f + 0.6f,
                        "slot " + item.Slot + ": arrival reaches the fitted body height at its reveal anchor");
                else
                    Assert(grown.Max(d => d.Positions[3].y) > 1.4f + 0.6f,
                        "slot " + item.Slot + ": arrival reaches the upper body at its reveal anchor");
                Assert(BankAssistantTeleportVisuals.IsWaiting(item.Slot), "slot " + item.Slot + ": hidden window open");
                BankAssistantTeleportVisuals.EndSlot(item.Slot);
            }
            Eq(0, CoinCourierTeleportFx.ActiveCount, "all pinned pairs released");
        });
        Test("the actor is hidden for the reveal window and the stripes keep fading through it", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero, new Vector3(5f, 0f, 0f)), "started");

            Time.time = 0.119f;
            BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
            Assert(!renderer.enabled, "no reveal before the deadline");
            Assert(BankAssistantTeleportVisuals.IsWaiting(0), "still waiting at 0.119s");

            Time.time = 0.12f;
            BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
            Assert(renderer.enabled, "revealed at the deadline");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "wait released");
            Assert(BankAssistantTeleportVisuals.NeedsValidation(0), "revealed slot still owns its handles");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "stripes keep playing after the reveal");

            BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
            Assert(renderer.enabled, "post-reveal validation keeps visibility");
            BankAssistantTeleportVisuals.EndSlot(0);
            Assert(!BankAssistantTeleportVisuals.NeedsValidation(0), "record closed");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "revealed handles stay owned and cancellable");
        });
        Test("hard invalidation cancels the owned handles and restores the captured enabled value", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            var other = NewActor("other");

            bool Run(GameObject currentActor, bool reserved, bool cleanup, bool inLayer, bool deactivate)
            {
                if (deactivate) actor.SetActive(false);
                BankAssistantTeleportVisuals.ValidateSlot(0, currentActor, reserved, cleanup, inLayer);
                if (deactivate) actor.SetActive(true);
                return true;
            }

            var cases = new (string Label, Func<bool> Run)[]
            {
                ("actor null", () => Run(null, false, false, true, false)),
                ("pointer mismatch", () => Run(other, false, false, true, false)),
                ("inactive actor", () => Run(actor, false, false, true, true)),
                ("outside current layer", () => Run(actor, false, false, false, false)),
                ("reserved for restock", () => Run(actor, true, false, true, false)),
                ("cleanup pending", () => Run(actor, false, true, true, false)),
            };
            foreach (var item in cases)
            {
                Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor,
                    new Vector3(1f, 0f, 0f), new Vector3(4f, 0f, 0f)), item.Label + ": started");
                Assert(!renderer.enabled, item.Label + ": hidden");
                item.Run();
                Assert(renderer.enabled, item.Label + ": restored");
                Assert(!BankAssistantTeleportVisuals.IsWaiting(0), item.Label + ": released");
                Eq(0, CoinCourierTeleportFx.ActiveCount, item.Label + ": handles cancelled");
            }
        });
        Test("a revealed pair stays owned through the residual window and cancels without touching others", () =>
        {
            var a = NewActor("assistant-a");
            var rendererA = a.AddComponent<SpriteRenderer>();
            var b = NewActor("assistant-b");
            var rendererB = b.AddComponent<SpriteRenderer>();
            CoinCourierFxHandle foreign = CoinCourierTeleportFx.Begin(new Vector3(-5f, 0f, 0f), Color.white, 1f, 1, 1);
            Assert(foreign.IsValid, "foreign effect started");

            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, a, Vector3.zero, new Vector3(3f, 0f, 0f)), "a started");
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(1, b, Vector3.zero, new Vector3(3f, 0f, 0f)), "b started");
            Eq(5, CoinCourierTeleportFx.ActiveCount, "foreign + two pairs");

            Time.time = 0.12f;
            BankAssistantTeleportVisuals.ValidateSlot(0, a, false, false, true);
            BankAssistantTeleportVisuals.ValidateSlot(1, b, false, false, true);
            Assert(rendererA.enabled && rendererB.enabled, "both revealed");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0) && !BankAssistantTeleportVisuals.IsWaiting(1), "waits over");
            Eq(5, CoinCourierTeleportFx.ActiveCount, "stripes still playing in the residual window");

            Time.time = 0.18f;
            BankAssistantTeleportVisuals.EndSlot(0);
            Eq(3, CoinCourierTeleportFx.ActiveCount, "a's residual pair cancelled at .18s");
            Assert(rendererA.enabled, "a's renderer untouched by the residual cancel");
            BankAssistantTeleportVisuals.Forget(b.GetInstanceID());
            Eq(1, CoinCourierTeleportFx.ActiveCount, "b's residual pair cancelled by instance id");
            Assert(rendererB.enabled, "b's renderer untouched by the residual cancel");
            CoinCourierTeleportFx.Cancel(foreign);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "the foreign handle still owned its slot");
        });
        Test("hard cleanup after the reveal never rewrites the renderer", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            var cleanups = new (string Label, Action Run)[]
            {
                ("EndSlot", () => BankAssistantTeleportVisuals.EndSlot(0)),
                ("Forget", () => BankAssistantTeleportVisuals.Forget(actor.GetInstanceID())),
                ("reserved validation", () => BankAssistantTeleportVisuals.ValidateSlot(0, actor, true, false, true)),
            };
            foreach (var item in cleanups)
            {
                Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero,
                    new Vector3(3f, 0f, 0f)), item.Label + ": started");
                Time.time += 0.12f;
                BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
                Assert(renderer.enabled, item.Label + ": revealed");
                renderer.enabled = false;   // reveal 之后原生/他人改写
                Time.time += 0.06f;
                item.Run();
                Assert(!renderer.enabled, item.Label + ": post-reveal cleanup must not overwrite the renderer");
                Assert(!BankAssistantTeleportVisuals.IsWaiting(0), item.Label + ": released");
                Assert(!BankAssistantTeleportVisuals.NeedsValidation(0), item.Label + ": record closed");
                renderer.enabled = true;
            }
            Eq(0, CoinCourierTeleportFx.ActiveCount, "all owned pairs cancelled");
        });
        Test("a jump whose second end cannot be drawn stays visible, unwaited and cancels the first end", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            var groups = GameObject.All
                .Where(g => g.name == "KEM_CoinCourierTeleportFxEffect")
                .ToList();
            Assert(groups.Count >= 2, "pool groups exist");
            // 池组顺序即槽位顺序：除第一组外全部不可用（第一端成功、第二端失败）。
            foreach (var group in groups.Skip(1)) group.ForceNull = true;

            // 自证假设：第一端可建、第二端不可建。
            CoinCourierFxHandle probe = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0);
            Assert(probe.IsValid, "first pool group usable");
            Assert(!CoinCourierTeleportFx.Begin(Vector3.one, Color.white, 1f, 0, 0).IsValid, "next pool group unusable");
            CoinCourierTeleportFx.Cancel(probe);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "probe cleaned");

            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero,
                new Vector3(4f, 0f, 0f)), "partial pair rejected");
            Assert(renderer.enabled, "original enabled kept when only one end could be drawn");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "zero wait on a partial pair");
            Assert(!BankAssistantTeleportVisuals.NeedsValidation(0), "no record on a partial pair");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "the successful end was cancelled");

            foreach (var group in groups) group.ForceNull = false;
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero,
                new Vector3(4f, 0f, 0f)), "recovers with both ends");
            Assert(!renderer.enabled, "hidden only with a complete pair");
        });
        Test("an exception inside Begin fails open and is warned only by the FX owner", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            LineRenderer.FailSetPosition = true;
            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero,
                new Vector3(4f, 0f, 0f)), "exception path rejected");
            Assert(renderer.enabled, "not hidden on an exception");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "no wait on an exception");
            LineRenderer.FailSetPosition = false;

            var warnings = KingdomEnhancedPlugin.Instance.LogSource.Warnings;
            Eq(1, warnings.Count(w => w.Contains("[CoinCourierTeleportFx]")), "the FX owner warns exactly once");
            Eq(0, warnings.Count(w => w.Contains("[BankAssistantTeleportVisuals]")), "no duplicate warning from the caller");

            // 清掉异常路径留下的半成品槽并确认恢复正常。
            CoinCourierTeleportFx.TickForFrame(1f, 810001);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "half-built slots drained");
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero,
                new Vector3(4f, 0f, 0f)), "recovers with both ends");
        });
        Test("a repeated teleport cancels the previous pair and restores the value captured first", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero, new Vector3(3f, 0f, 0f)), "first");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "first pair");
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, new Vector3(3f, 0f, 0f), new Vector3(6f, 0f, 0f)), "second");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "the first pair was cancelled, not stacked");
            Assert(!renderer.enabled, "still hidden once");

            Time.time = 0.12f;
            BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
            Assert(renderer.enabled, "revealed after the second window");

            // 残影相重复传送：先取消上一对残影，再开始新一次。
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, new Vector3(6f, 0f, 0f), new Vector3(9f, 0f, 0f)), "third");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "residual pair cancelled before the new pair");
            Assert(!renderer.enabled, "hidden again");
            Time.time = 0.24f;
            BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
            Assert(renderer.enabled, "third window revealed");

            // 原生本就不可见的角色：恢复捕获的原值 false，绝不无条件写 true。
            var actor2 = NewActor("assistant-invisible");
            var renderer2 = actor2.AddComponent<SpriteRenderer>();
            renderer2.enabled = false;
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(1, actor2, Vector3.zero, new Vector3(2f, 0f, 0f)), "started");
            Time.time = 0.52f;
            BankAssistantTeleportVisuals.ValidateSlot(1, actor2, false, false, true);
            Assert(!renderer2.enabled, "captured original false is preserved");
        });
        Test("ending a slot never touches another owner's effect", () =>
        {
            var actor = NewActor("assistant");
            actor.AddComponent<SpriteRenderer>();
            CoinCourierFxHandle foreign = CoinCourierTeleportFx.Begin(new Vector3(-5f, 0f, 0f), Color.white, 1f, 3, 9);
            Assert(foreign.IsValid, "foreign effect started");
            Eq(1, CoinCourierTeleportFx.ActiveCount, "foreign slot");

            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero, new Vector3(1f, 0f, 0f)), "presentation");
            Eq(3, CoinCourierTeleportFx.ActiveCount, "assistant pair added");
            BankAssistantTeleportVisuals.EndSlot(0);
            Eq(1, CoinCourierTeleportFx.ActiveCount, "only the assistant's pair ended");
            CoinCourierTeleportFx.Cancel(foreign);
            Eq(0, CoinCourierTeleportFx.ActiveCount, "the foreign handle still owns its slot");
        });
        Test("pool recycling forgets only the matching instance", () =>
        {
            var a = NewActor("assistant-a");
            var rendererA = a.AddComponent<SpriteRenderer>();
            var b = NewActor("assistant-b");
            var rendererB = b.AddComponent<SpriteRenderer>();
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, a, Vector3.zero, new Vector3(1f, 0f, 0f)), "a hidden");
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(1, b, Vector3.zero, new Vector3(1f, 0f, 0f)), "b hidden");
            Eq(4, CoinCourierTeleportFx.ActiveCount, "both pairs alive");

            BankAssistantTeleportVisuals.Forget(a.GetInstanceID());
            Assert(rendererA.enabled, "matching actor restored");
            Assert(!rendererB.enabled, "other actor untouched");
            Eq(2, CoinCourierTeleportFx.ActiveCount, "only the matching handles cancelled");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "slot 0 cleared");
            Assert(BankAssistantTeleportVisuals.IsWaiting(1), "slot 1 retained");

            BankAssistantTeleportVisuals.Forget(999999);
            Assert(BankAssistantTeleportVisuals.IsWaiting(1), "unknown instance id is a no-op");
            BankAssistantTeleportVisuals.EndSlot(1);
            Assert(rendererB.enabled, "b restored by its own end");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "b handles cancelled");
        });
        Test("out-of-range slots are rejected without touching any state", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(-1, actor, Vector3.zero, Vector3.one), "negative index rejected");
            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(8, actor, Vector3.zero, Vector3.one), "index 8 rejected");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(-1) && !BankAssistantTeleportVisuals.IsWaiting(8), "no wait for invalid slots");
            BankAssistantTeleportVisuals.EndSlot(8);
            BankAssistantTeleportVisuals.ValidateSlot(8, actor, false, false, true);
            Assert(renderer.enabled, "renderer untouched");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "no effects");
        });
        Test("an actor without a root SpriteRenderer stays native-visible and plays nothing", () =>
        {
            var actor = NewActor("assistant-no-renderer");
            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero, new Vector3(3f, 0f, 0f)), "rejected");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "no effects");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "no wait");
        });
        Test("FX unavailability fails open: no hide, no wait, and it recovers", () =>
        {
            // 打满共享池的 18 组槽（8 助手双端 + 哥布林双端）：迫使后续 Begin 复用
            // 既有槽而不是新建根对象，这样“根/池不可用”才能注入到复用路径上。
            var actors = new GameObject[8];
            var renderers = new SpriteRenderer[8];
            for (int i = 0; i < 8; i++)
            {
                actors[i] = NewActor("assistant-" + i);
                renderers[i] = actors[i].AddComponent<SpriteRenderer>();
                Assert(BankAssistantTeleportVisuals.NotifyTeleport(i, actors[i], Vector3.zero,
                    new Vector3(4f + i, 0f, 0f)), "saturate " + i);
            }
            Eq(16, CoinCourierTeleportFx.ActiveCount, "eight assistant pairs fill sixteen groups");
            var depart = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
                CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Departure, 0.18f);
            var arrive = CoinCourierTeleportFx.Begin(Vector3.one, Color.white, 1f, 0, 0,
                CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Arrival, 0.18f);
            Assert(depart.IsValid && arrive.IsValid, "the courier pair takes the last two slots");
            Eq(18, CoinCourierTeleportFx.ActiveCount, "pool saturated at 18");
            BankAssistantTeleportVisuals.EndSlot(0);
            Assert(renderers[0].enabled, "saturated probe actor released");
            CoinCourierTeleportFx.Cancel(depart);
            CoinCourierTeleportFx.Cancel(arrive);
            Eq(14, CoinCourierTeleportFx.ActiveCount, "two assistant groups and the courier pair freed");

            var groups = GameObject.All
                .Where(g => g.name == "KEM_CoinCourierTeleportFxEffect")
                .ToList();
            Assert(groups.Count >= 8, "shared FX pool groups exist");
            foreach (var group in groups) group.ForceNull = true;   // 池组不可用：Begin 只能返回无效句柄

            int activeBefore = CoinCourierTeleportFx.ActiveCount;
            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(0, actors[0], Vector3.zero,
                new Vector3(2f, 0f, 0f)), "begin failed");
            Assert(renderers[0].enabled, "not hidden when FX are unavailable");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "no wait when FX are unavailable");
            Eq(activeBefore, CoinCourierTeleportFx.ActiveCount, "no effects added or replaced");

            foreach (var group in groups) group.ForceNull = false;
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actors[0], Vector3.zero,
                new Vector3(2f, 0f, 0f)), "recovers");
            Assert(!renderers[0].enabled, "hidden only with real effects");
            Assert(BankAssistantTeleportVisuals.IsWaiting(0), "waits only with real effects");
        });
        Test("online suppression keeps every presentation path native-visible", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            NetworkBigBoss.IsOnline = true;
            Assert(!BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero, new Vector3(4f, 0f, 0f)), "suppressed online");
            Eq(0, CoinCourierTeleportFx.ActiveCount, "zero FX online");
            Assert(renderer.enabled, "never hidden online");
            Assert(!BankAssistantTeleportVisuals.IsWaiting(0), "no delay online");
        });
        Test("a paused frame neither advances the stripes nor closes the window", () =>
        {
            var actor = NewActor("assistant");
            var renderer = actor.AddComponent<SpriteRenderer>();
            Assert(BankAssistantTeleportVisuals.NotifyTeleport(0, actor, Vector3.zero, new Vector3(2f, 0f, 0f)), "started");
            var dash = ActiveDashes().First();
            Vector3 before = dash.Positions[3];

            CoinCourierTeleportFx.TickForFrame(0f, 800001);
            Eq(2, CoinCourierTeleportFx.ActiveCount, "non-positive delta leaves effects alone");
            Assert(dash.Positions[3] == before, "geometry unchanged while paused");

            BankAssistantTeleportVisuals.ValidateSlot(0, actor, false, false, true);
            Assert(!renderer.enabled, "window stays open while game time does not advance");
        });
        Console.WriteLine(_passed + " passed, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }
}
