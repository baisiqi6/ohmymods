// Teleport FX sequence suite (issue-104): compiles the REAL il2cpp/CoinCourierTeleportFx.cs
// against the coin-courier-visuals UnityEngine doubles.
//
// Scope: the 16-line style-owned/directional sequence contract — palette (gold / near-black),
// arrival growth peak exactly at the materialize anchor, departure dense->sparse monotonic
// thinning, strict phase staggering, direction/anchor validation, pool cap 18 + stale handles,
// pause/frame dedup, and two independent handles coexisting.
//
// `--export <dir>` drives the same production Begin/TickForFrame and dumps per-frame
// positions/colors/width/visibility to JSON for the private preview renderer; the renderer only
// draws that data, so the previews are code-driven, not re-authored curves. Neither mode proves
// real Unity rendering; the in-game pass owns that.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int passed, failed;

    private static int Main(string[] args)
    {
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        if (args.Length >= 2 && args[0] == "--export")
        {
            Exporter.Run(args[1], args.Length > 2 ? args[2] : null);
            return 0;
        }
        Test("table drives sixteen distinct stripes", TableDrivesSixteenStripes);
        Test("stripes share one uniform tilt and stay mutually parallel at every age", StripesShareOneUniformTilt);
        Test("body fit matches the visible character size and keeps the tilt", BodyFitMatchesCharacterSize);
        Test("packed native banker evidence yields a conservative body box", NativeEvidenceFallback);
        Test("horizontal palette stays clear gold and never white", HorizontalPaletteIsGold);
        Test("vertical palette stays near black, warm-tipped and ignores base rgb", VerticalPaletteIsNearBlack);
        Test("arrival rises to a full-length peak exactly at the anchor", ArrivalPeaksAtAnchor);
        Test("departure starts dense and long and thins monotonically", DepartureThinsMonotonically);
        Test("activation stagger is strict, deterministic and inside the anchor", StaggerIsStrictAndDeterministic);
        Test("direction and materialize time are validated before any slot is taken", ValidationRejectsBeforeAcquire);
        Test("pool caps at eighteen and stale handles stay inert", PoolCapAndStaleHandles);
        Test("pause and frame dedup freeze the sequence", PauseAndFrameDedup);
        Test("arrival begin never cancels the departure of the same owner", IndependentHandlesCoexist);
        Test("anchors twelve and eighteen shift the peak and thinning", AnchorDifference);
        Console.WriteLine("teleport-fx-sequence: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- harness

    private static void Test(string name, Action body)
    {
        Cleanup();
        try
        {
            body();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message);
        }
        finally
        {
            Cleanup();
        }
    }

    private static void Cleanup()
    {
        try { CoinCourierTeleportFx.Clear(); } catch { }
        GameObject.All.Clear();
        GameObject.ResetCounters();
        SpriteRenderer.SpriteWrites = 0;
        UnityEngine.Object.DestroyCalls = 0;
        Time.frameCount = 0;
    }

    private static void Check(bool condition, string why)
    {
        if (!condition) throw new Exception(why);
    }

    private static void Equal(int expected, int actual, string why)
    {
        if (expected != actual) throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Near(float expected, float actual, float tolerance, string why)
    {
        if (MathF.Abs(expected - actual) > tolerance)
            throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static LineRenderer Dash(int index)
    {
        var gameObject = GameObject.All.FirstOrDefault(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportDash" + index);
        if (gameObject == null) throw new Exception("dash " + index + " missing");
        return gameObject.GetComponent<LineRenderer>();
    }

    private static float Length(LineRenderer line)
    {
        var first = line.Positions[0];
        var last = line.Positions[3];
        float dx = last.x - first.x;
        float dy = last.y - first.y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float Alpha(LineRenderer line)
        => MathF.Max(line.startColor.a, line.endColor.a);

    private sealed class Frame
    {
        internal float[] Lengths;
        internal float[] Alphas;
        internal bool[] Enabled;
        internal Vector3[] First;
        internal Vector3[] Last;
        internal Color[] Start;
        internal Color[] End;
    }

    private static Frame Capture()
    {
        int count = CoinCourierTeleportFx.DashesPerEffect;
        var frame = new Frame
        {
            Lengths = new float[count],
            Alphas = new float[count],
            Enabled = new bool[count],
            First = new Vector3[count],
            Last = new Vector3[count],
            Start = new Color[count],
            End = new Color[count]
        };
        for (int i = 0; i < count; i++)
        {
            var line = Dash(i);
            frame.Lengths[i] = Length(line);
            frame.Alphas[i] = Alpha(line);
            frame.Enabled[i] = line.enabled;
            frame.First[i] = line.Positions[0];
            frame.Last[i] = line.Positions[3];
            frame.Start[i] = line.startColor;
            frame.End[i] = line.endColor;
        }
        return frame;
    }

    private static bool IsVisible(Frame frame, int index, float threshold = 0.02f)
        => frame.Enabled[index] && frame.Alphas[index] > threshold;

    private static int VisibleCount(Frame frame, float threshold = 0.02f)
        => Enumerable.Range(0, frame.Lengths.Length).Count(i => IsVisible(frame, i, threshold));

    private static float VisibleLength(Frame frame, float threshold = 0.02f)
    {
        float sum = 0f;
        for (int i = 0; i < frame.Lengths.Length; i++)
        {
            if (IsVisible(frame, i, threshold)) sum += frame.Lengths[i];
        }
        return sum;
    }

    private static float[] ReferenceFullLengths()
    {
        var handle = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        var lengths = new float[CoinCourierTeleportFx.DashesPerEffect];
        for (int i = 0; i < lengths.Length; i++) lengths[i] = Length(Dash(i));
        CoinCourierTeleportFx.Cancel(handle);
        return lengths;
    }

    private static void EnsurePoolDrained()
    {
        int guard = 0;
        while (CoinCourierTeleportFx.ActiveCount > 0 && guard++ < 32)
            CoinCourierTeleportFx.TickForFrame(1f, 900000 + guard);
    }

    // ---------------------------------------------------------------- tests

    private static void TableDrivesSixteenStripes()
    {
        Equal(16, CoinCourierTeleportFx.DashesPerEffect, "the single spec table pins sixteen stripes");
        Equal(18, CoinCourierTeleportFx.MaxConcurrent, "the group cap covers eight double-ended assistants plus the courier pair");
        var handle = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        Check(handle.IsValid, "begin returns a valid handle");
        var frame = Capture();
        Check(frame.Lengths.Distinct().Count() == 16, "all sixteen full lengths are distinct");
        Check(frame.First.Select(p => MathF.Round(p.y * 100000f)).Distinct().Count() == 16,
            "all sixteen horizontal rows are distinct");
    }

    /// <summary>
    /// 统一微斜契约（用户 2026-10-03 澄清）：任意年龄、两方向、两样式下，每线方向向量的正规化
    /// 比率恒为 TiltSlope（|dy|/|dx| = .10 ≈ 5.7°）；全组同向（右上）、任意两线正规化叉积 ≈ 0
    /// （互相平行）；H 以横向为主、V 以竖向为主。span 退化的帧不参与比率（不除零）。
    /// </summary>
    private static void StripesShareOneUniformTilt()
    {
        foreach (var style in new[] { CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportStyle.Vertical })
        {
            bool vertical = style == CoinCourierTeleportStyle.Vertical;
            foreach (var direction in new[] { CoinCourierTeleportDirection.Arrival, CoinCourierTeleportDirection.Departure })
            {
                CoinCourierTeleportFx.Clear();   // 每种组合从空池重建，Dash(i) 只指向本轮组
                CoinCourierTeleportFx.Begin(new Vector3(1.5f, 2.5f, 0f), Color.white, 1f, 0, 0,
                    style, direction, 0.12f);
                int frameId = 2000 + (int)style * 10 + (int)direction * 100;
                for (int step = 1; step <= 38; step++)
                {
                    CoinCourierTeleportFx.TickForFrame(1f / 120f, frameId++);
                    float age = step / 120f;
                    var reference = Dash(0);
                    float refDx = reference.Positions[3].x - reference.Positions[0].x;
                    float refDy = reference.Positions[3].y - reference.Positions[0].y;
                    float refLen = MathF.Sqrt(refDx * refDx + refDy * refDy);
                    Check(refLen > 1e-4f, style + "/" + direction + " reference span at " + age);
                    for (int i = 0; i < CoinCourierTeleportFx.DashesPerEffect; i++)
                    {
                        var line = Dash(i);
                        float dx = line.Positions[3].x - line.Positions[0].x;
                        float dy = line.Positions[3].y - line.Positions[0].y;
                        float span = MathF.Sqrt(dx * dx + dy * dy);
                        string why = style + "/" + direction + " stripe " + i + " at " + age;
                        if (vertical)
                        {
                            Check(MathF.Abs(dy) > 1e-4f, why + ": the column has a non-zero span");
                            Near(0.10f * MathF.Abs(dy), MathF.Abs(dx), 1e-3f * MathF.Abs(dy) + 1e-5f,
                                why + ": |dx|/|dy| = .10 (uniform lean, not axis 0)");
                            Check(MathF.Abs(dy) > MathF.Abs(dx), why + ": projection stays on the y axis");
                        }
                        else
                        {
                            Check(MathF.Abs(dx) > 1e-4f, why + ": the row has a non-zero span");
                            Near(0.10f * MathF.Abs(dx), MathF.Abs(dy), 1e-3f * MathF.Abs(dx) + 1e-5f,
                                why + ": |dy|/|dx| = .10 (uniform rise, not axis 0)");
                            Check(MathF.Abs(dx) > MathF.Abs(dy), why + ": projection stays on the x axis");
                        }
                        Check(dx > 0f && dy > 0f, why + ": every stripe leans the same way");
                        float cross = (dx / span) * (refDy / refLen) - (dy / span) * (refDx / refLen);
                        Near(0f, cross, 1e-5f, why + ": normalized directions stay mutually parallel (cross ~ 0)");
                    }
                }
            }
        }
    }

    /// <summary>构造可读身体 fixture：纹理内一个不透明矩形 + sprite rect/pivot/PPU + renderer 缩放。</summary>
    private static SpriteRenderer NewBodyRenderer(int texW, int texH, float pivotX, float pivotY, float ppu,
        int boxX, int boxY, int boxW, int boxH, float scaleX, float scaleY)
    {
        var go = new GameObject("body-fixture");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = new Sprite
        {
            texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                OpaqueAt = (x, y) => x >= boxX && x < boxX + boxW && y >= boxY && y < boxY + boxH
            },
            rect = new Rect(0f, 0f, texW, texH),
            pivot = new Vector2(pivotX, pivotY),
            pixelsPerUnit = ppu
        };
        go.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        return renderer;
    }

    private static void Envelope(Frame frame, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = float.MaxValue; maxX = float.MinValue; minY = float.MaxValue; maxY = float.MinValue;
        for (int i = 0; i < frame.First.Length; i++)
        {
            minX = MathF.Min(minX, MathF.Min(frame.First[i].x, frame.Last[i].x));
            maxX = MathF.Max(maxX, MathF.Max(frame.First[i].x, frame.Last[i].x));
            minY = MathF.Min(minY, MathF.Min(frame.First[i].y, frame.Last[i].y));
            maxY = MathF.Max(maxY, MathF.Max(frame.First[i].y, frame.Last[i].y));
        }
    }

    private static void AssertFittedEnvelope(SpriteRenderer renderer, CoinCourierTeleportStyle style,
        float bodyW, float bodyH, float centerX, float centerY, string why)
    {
        var handle = CoinCourierTeleportFx.Begin(Vector3.zero, new Color(0.95f, 0.82f, 0.42f, 0.85f), 1f, 0, 0,
            style, CoinCourierTeleportDirection.Departure, 0.12f, renderer);
        Check(handle.IsValid, why + ": fitted begin valid");
        var frame = Capture();
        Envelope(frame, out float minX, out float maxX, out float minY, out float maxY);
        float envW = maxX - minX;
        float envH = maxY - minY;
        Check(envW <= bodyW * 1.05f + 1e-4f, why + ": width envelope stays within 1.05 body (" + envW + " vs " + bodyW + ")");
        Check(envH <= bodyH * 1.05f + 1e-4f, why + ": height envelope stays within 1.05 body (" + envH + " vs " + bodyH + ")");
        float fillW = envW / bodyW;
        float fillH = envH / bodyH;
        Check(fillW >= 0.8f && fillH >= 0.8f, why + ": both axes fill the body (" + fillW + " / " + fillH + ")");
        Check(MathF.Abs(fillW - fillH) <= 0.1f, why + ": both axes fill the body comparably (" + fillW + " vs " + fillH + ")");
        Near(centerX, (minX + maxX) * 0.5f, 0.025f, why + ": x centered on the visible body");
        Near(centerY, (minY + maxY) * 0.5f, 0.03f, why + ": y centered on the visible body");
        // 斜率 .10、互相平行在任何身体尺寸下不变。
        for (int i = 0; i < frame.First.Length; i++)
        {
            float dx = frame.Last[i].x - frame.First[i].x;
            float dy = frame.Last[i].y - frame.First[i].y;
            if (style == CoinCourierTeleportStyle.Vertical)
                Near(0.10f * MathF.Abs(dy), MathF.Abs(dx), 1e-3f * MathF.Abs(dy) + 1e-5f, why + " stripe " + i + " lean .10");
            else
                Near(0.10f * MathF.Abs(dx), MathF.Abs(dy), 1e-3f * MathF.Abs(dx) + 1e-5f, why + " stripe " + i + " rise .10");
        }
        CoinCourierTeleportFx.Cancel(handle);
    }

    private static float CoinJourrierAnchor() => 0.12f;

    private static void BodyFitMatchesCharacterSize()
    {
        // —— Clerk 等价 fixture：可见框 16x32px @ PPU 47.627907、pivot(16,3)（与 PNG 离线 alpha bbox 一致）——
        var clerk = NewBodyRenderer(32, 40, 16f, 3f, 47.627907f, 8, 4, 16, 32, 1f, 1f);
        const float clerkPpu = 47.627907f;
        float clerkW = 16f / clerkPpu;
        float clerkH = 32f / clerkPpu;
        float clerkCy = (20f - 3f) / clerkPpu;      // 可见框中心 20px − pivot 3px
        AssertFittedEnvelope(clerk, CoinCourierTeleportStyle.Horizontal, clerkW, clerkH, 0f, clerkCy, "clerk H");
        AssertFittedEnvelope(clerk, CoinCourierTeleportStyle.Vertical, clerkW, clerkH, 0f, clerkCy, "clerk V");

        // —— Goblin fixture：33x32px @ PPU 32、pivot(28,12)、视觉 scale(.625,.65625) ——
        var goblin = NewBodyRenderer(56, 56, 28f, 12f, 32f, 11, 12, 33, 32, 0.625f, 0.65625f);
        float goblinW = 33f / 32f * 0.625f;
        float goblinH = 32f / 32f * 0.65625f;
        float goblinCx = (27.5f - 28f) / 32f * 0.625f;
        float goblinCy = (12f + 16f - 12f) / 32f * 0.65625f;
        AssertFittedEnvelope(goblin, CoinCourierTeleportStyle.Vertical, goblinW, goblinH, goblinCx, goblinCy, "goblin V");

        // flip 镜像不改变尺寸、只镜像中心偏移（用非对称框验证）。
        var asym = NewBodyRenderer(32, 40, 16f, 3f, 32f, 4, 4, 8, 32, 1f, 1f);
        float asymCy = (20f - 3f) / 32f;
        AssertFittedEnvelope(asym, CoinCourierTeleportStyle.Horizontal, 8f / 32f, 1f, -0.25f, asymCy, "asym no-flip");
        asym.flipX = true;
        AssertFittedEnvelope(asym, CoinCourierTeleportStyle.Horizontal, 8f / 32f, 1f, 0.25f, asymCy, "asym flipX mirrors the center");
        asym.flipX = false;

        // 缩放：同一 sprite 的 renderer scale .5 → 身体与世界包络一起减半。
        asym.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        AssertFittedEnvelope(asym, CoinCourierTeleportStyle.Horizontal, 8f / 32f * 0.5f, 0.5f,
            -0.125f, asymCy * 0.5f, "asym half scale");

        // —— 失败路径：全部拒收为无效 FX，且不消耗池槽 ——
        var oversized = NewBodyRenderer(512, 512, 16f, 3f, 32f, 4, 4, 8, 32, 1f, 1f);
        Check(!CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Horizontal,
            CoinCourierTeleportDirection.Departure, 0.12f, oversized).IsValid, "oversized rect refused");
        var transparent = NewBodyRenderer(32, 40, 16f, 3f, 32f, 4, 4, 0, 0, 1f, 1f);
        Check(!CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Horizontal,
            CoinCourierTeleportDirection.Departure, 0.12f, transparent).IsValid, "fully transparent sprite refused");
        var unreadable = NewBodyRenderer(32, 40, 16f, 3f, 32f, 4, 4, 8, 32, 1f, 1f);
        unreadable.sprite.texture.isReadable = false;
        Check(!CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Horizontal,
            CoinCourierTeleportDirection.Departure, 0.12f, unreadable).IsValid, "unreadable texture refused");
        var packed = NewBodyRenderer(32, 40, 16f, 3f, 32f, 4, 4, 8, 32, 1f, 1f);
        packed.sprite.packed = true;
        Check(!CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Horizontal,
            CoinCourierTeleportDirection.Departure, 0.12f, packed).IsValid, "packed sprite refused");
        var zeroScale = NewBodyRenderer(32, 40, 16f, 3f, 32f, 4, 4, 8, 32, 0f, 1f);
        Check(!CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Horizontal,
            CoinCourierTeleportDirection.Departure, 0.12f, zeroScale).IsValid, "zero scale refused");
        Equal(0, CoinCourierTeleportFx.ActiveCount, "size failures consume no pool slot");

        // —— 缓存：同一 sprite 只扫描一次；Tick 零扫描；Clear 释放缓存后可重扫 ——
        CoinCourierTeleportFx.Clear();
        Texture2D.GetPixelsCalls = 0;
        var h1 = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival, 0.12f, clerk);
        Check(h1.IsValid, "cache probe begin valid");
        Equal(1, Texture2D.GetPixelsCalls, "the same sprite is scanned once");
        CoinCourierTeleportFx.Cancel(h1);
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Departure, 0.12f, clerk);
        Equal(1, Texture2D.GetPixelsCalls, "a second begin with the same sprite does not rescan");
        for (int i = 0; i < 20; i++) CoinCourierTeleportFx.TickForFrame(1f / 60f, 5000 + i);
        Equal(1, Texture2D.GetPixelsCalls, "ticking never scans pixels");
        CoinCourierTeleportFx.Clear();
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Departure, 0.12f, clerk);
        Equal(2, Texture2D.GetPixelsCalls, "Clear releases the cache and the next begin rescans");
    }

    /// <summary>native 证据 fallback fixture：packed/unreadable 的 32×32/PPU32/pivot(16,0) 5~10 顶点紧 mesh。</summary>
    private static SpriteRenderer NewNativeRenderer(in NativeBankerFixture.Row row)
    {
        var go = new GameObject(row.Name);
        var renderer = go.AddComponent<SpriteRenderer>();
        int count = Math.Max(5, Math.Min(10, row.VertexCount));
        var vertices = new Vector2[count];
        vertices[0] = new Vector2(row.MinX, row.MinY);
        vertices[1] = new Vector2(row.MaxX, row.MinY);
        vertices[2] = new Vector2(row.MaxX, row.MaxY);
        vertices[3] = new Vector2(row.MinX, row.MaxY);
        for (int i = 4; i < count; i++) vertices[i] = new Vector2(row.MinX, row.MinY);
        renderer.sprite = new Sprite
        {
            name = row.Name,
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { isReadable = false },
            rect = new Rect(0f, 0f, 32f, 32f),
            pivot = new Vector2(16f, 0f),
            pixelsPerUnit = 32f,
            packed = true,
            vertices = vertices
        };
        return renderer;
    }

    private static void NativeEvidenceFallback()
    {
        // 全部 80 例：与 operator json 的**真实** crop alpha 尺寸比对（不再镜像实现常量）。
        foreach (var row in NativeBankerFixture.Rows)
        {
            var renderer = NewNativeRenderer(row);
            bool ok = CoinCourierTeleportBodyBox.TryResolve(renderer, out var box);
            Check(ok, row.Name + "/" + row.Role + ": evidence-gated packed native resolves");
            float widthPx = box.Width * 32f;
            Check(MathF.Abs(widthPx - row.AlphaWidthPx) <= 1f,
                row.Name + "/" + row.Role + ": width matches the real crop alpha width ("
                + widthPx + " vs " + row.AlphaWidthPx + ")");
            float heightPx = box.Height * 32f;
            Check(heightPx >= row.AlphaHeightPx - 0.01f && heightPx <= row.AlphaHeightPx + 2.01f,
                row.Name + "/" + row.Role + ": height is conservative (>= alpha, <= alpha+2px): "
                + heightPx + " vs " + row.AlphaHeightPx);
            // 脚底相对 pivot 行：实现底部不内收；与真实 alpha 底边（BottomPx）误差 ≤2px。
            float bottomPx = box.Center.y * 32f - heightPx * 0.5f;
            Check(MathF.Abs(bottomPx - row.BottomPx) <= 2.01f,
                row.Name + "/" + row.Role + ": foot stays within 2px of the real alpha bottom ("
                + bottomPx + " vs " + row.BottomPx + ")");
        }

        // 四个 role 代表（各 role 的 firstIdle，缺一即失败）跑完整 Begin fit：
        // Horizontal 为真实 caller 形状；Vertical 仅作 generic 额外覆盖（不冒充 caller）。
        foreach (string role in new[] { "banker", "bamboo", "deadlands", "norselands" })
        {
            NativeBankerFixture.Row row = default;
            bool found = false;
            foreach (var candidate in NativeBankerFixture.Rows)
            {
                if (candidate.Role == role && candidate.Clip.Contains("idle", StringComparison.Ordinal))
                {
                    row = candidate;
                    found = true;
                    break;
                }
            }
            Check(found, role + ": a firstIdle fixture row exists");
            if (!found) continue;
            var renderer = NewNativeRenderer(row);
            Check(CoinCourierTeleportBodyBox.TryResolve(renderer, out var box),
                role + ": resolves (" + row.Name + ")");
            foreach (var style in new[] { CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportStyle.Vertical })
            {
                string why = role + "/" + row.Name + "/" + style
                    + (style == CoinCourierTeleportStyle.Horizontal ? " (caller shape)" : " (generic coverage)");
                CoinCourierTeleportFx.Clear();
                var handle = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
                    style, CoinCourierTeleportDirection.Departure, 0.18f, renderer);
                Check(handle.IsValid, why + ": fitted begin valid");
                var frame = Capture();
                Envelope(frame, out float minX, out float maxX, out float minY, out float maxY);
                Check(maxX - minX <= box.Width * 1.05f + 1e-4f && maxY - minY <= box.Height * 1.05f + 1e-4f,
                    why + ": fitted envelope stays within 1.05 body");
                Check(maxX - minX >= box.Width * 0.8f && maxY - minY >= box.Height * 0.8f,
                    why + ": both axes fill the body");
                for (int i = 0; i < frame.First.Length; i++)
                {
                    float dx = frame.Last[i].x - frame.First[i].x;
                    float dy = frame.Last[i].y - frame.First[i].y;
                    float along = style == CoinCourierTeleportStyle.Horizontal ? MathF.Abs(dx) : MathF.Abs(dy);
                    float across = style == CoinCourierTeleportStyle.Horizontal ? MathF.Abs(dy) : MathF.Abs(dx);
                    Near(0.10f * along, across, 1e-3f * along + 1e-5f, why + " stripe " + i + " lean .10");
                }
                CoinCourierTeleportFx.Cancel(handle);
            }
        }

        // 同名不同 mesh：各自按自身 vertices 计算（缓存/资格门不按名字合并）。
        var wide = NewNativeRenderer(new NativeBankerFixture.Row("banker_idle_0", "banker", "banker_idle", 7, -0.3125f, 0f, 0.21875f, 0.8125f, 13, 24, 0f));
        var narrow = NewNativeRenderer(new NativeBankerFixture.Row("banker_idle_0", "banker", "banker_idle", 7, -0.25f, 0f, 0.125f, 0.75f, 12, 24, 0f));
        Check(CoinCourierTeleportBodyBox.TryResolve(wide, out CoinCourierTeleportBodyBox.Box wideBox), "wide native resolves");
        Check(CoinCourierTeleportBodyBox.TryResolve(narrow, out CoinCourierTeleportBodyBox.Box narrowBox), "narrow native resolves");
        Check(wideBox.Width > narrowBox.Width + 0.05f, "same-name natives keep their own mesh boxes");

        // 未知/不满足证据门：FullRect(4 顶点) native 拒收；非家族名 packed 拒收。
        var fullRect = NewNativeRenderer(new NativeBankerFixture.Row("banker_idle_0", "banker", "banker_idle", 7, -.3125f, 0f, .21875f, .8125f, 13, 24, 0f));
        fullRect.sprite.vertices = new[] { new Vector2(-.5f, 0f), new Vector2(.5f, 0f), new Vector2(.5f, 1f), new Vector2(-.5f, 1f) };
        Check(!CoinCourierTeleportBodyBox.TryResolve(fullRect, out _), "four-vertex FullRect native is refused");
        var foreign = NewNativeRenderer(new NativeBankerFixture.Row("idol_oracle_omphalos_2", "foreign", "idol_idle", 7, -.25f, 0f, .25f, .8f, 16, 24, 0f));
        foreign.sprite.packed = true;
        Check(!CoinCourierTeleportBodyBox.TryResolve(foreign, out _), "packed sprite outside the evidence family is refused");
    }

    private static void HorizontalPaletteIsGold()
    {
        var handle = CoinCourierTeleportFx.Begin(new Vector3(1f, 2f, 0f), Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival, 0.12f);
        Check(handle.IsValid, "arrival begin valid");
        CoinCourierTeleportFx.TickForFrame(0.12f, 7);   // 峰值：全部线 alpha 满
        var frame = Capture();
        for (int i = 0; i < frame.Start.Length; i++)
        {
            var start = frame.Start[i];
            var end = frame.End[i];
            Check(start.r >= 0.45f, "stripe " + i + " keeps a clear gold red channel");
            Check(start.r - start.g >= 0.06f && start.g - start.b >= 0.1f,
                "stripe " + i + " keeps the gold hue ordering r>g>b");
            Check(start.r - start.b >= 0.2f, "stripe " + i + " is clearly saturated gold, not grey/white");
            Check(!(start.r > 0.99f && start.g > 0.9f && start.b > 0.85f),
                "stripe " + i + " never reaches the old washed-white tone");
            Check(start.a > 0.3f && end.a > 0.1f, "stripe " + i + " is visible at its peak");
        }
    }

    private static void VerticalPaletteIsNearBlack()
    {
        bool Near(Color a, Color b) => MathF.Abs(a.r - b.r) < 1e-6f && MathF.Abs(a.g - b.g) < 1e-6f
            && MathF.Abs(a.b - b.b) < 1e-6f;

        var whiteBase = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Arrival, 0.18f);
        CoinCourierTeleportFx.TickForFrame(0.18f, 11);
        var white = Capture();
        CoinCourierTeleportFx.Cancel(whiteBase);
        EnsurePoolDrained();

        var magentaBase = CoinCourierTeleportFx.Begin(Vector3.zero, new Color(1f, 0f, 1f, 0.85f), 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Arrival, 0.18f);
        CoinCourierTeleportFx.TickForFrame(0.18f, 12);
        var magenta = Capture();
        CoinCourierTeleportFx.Cancel(magentaBase);
        EnsurePoolDrained();

        for (int i = 0; i < white.Start.Length; i++)
        {
            var start = white.Start[i];
            var end = white.End[i];
            float maxChannel = MathF.Max(start.r, MathF.Max(start.g, start.b));
            Check(maxChannel <= 0.2f, "stripe " + i + " body stays near black (max " + maxChannel + ")");
            Check(start.r >= start.b, "stripe " + i + " keeps the restrained warm tip ordering");
            Check(start.a > 0.3f, "stripe " + i + " stays high opacity near black");
            // 白色/任意底色的 Base RGB 不再参与混色：同一时点两组颜色逐分量一致。
            Check(Near(start, magenta.Start[i]) && Near(end, magenta.End[i]),
                "stripe " + i + " rgb is style-owned and never washed by the caller base");
        }
    }

    private static void ArrivalPeaksAtAnchor()
    {
        var full = ReferenceFullLengths();
        EnsurePoolDrained();
        const float anchor = 0.12f;
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival, anchor);
        float step = anchor / 40f;
        int frameId = 1;
        int previousCount = 0;
        float previousSum = 0f;
        for (int i = 1; i <= 40; i++)
        {
            CoinCourierTeleportFx.TickForFrame(step, frameId++);
            var sample = Capture();
            int count = VisibleCount(sample);
            float sum = VisibleLength(sample);
            Check(count >= previousCount, "arrival active count is non-decreasing before the anchor (step " + i + ")");
            Check(sum >= previousSum - 1e-4f, "arrival total length is non-decreasing before the anchor (step " + i + ")");
            if (i == 39)
            {
                for (int d = 0; d < sample.Lengths.Length; d++)
                    Check(sample.Lengths[d] < full[d] - 1e-3f, "stripe " + d + " is still growing right before the anchor");
            }
            previousCount = count;
            previousSum = sum;
        }
        var peak = Capture();
        Equal(16, VisibleCount(peak), "every stripe is visible at the anchor");
        for (int d = 0; d < peak.Lengths.Length; d++)
            Near(full[d], peak.Lengths[d], 1e-4f, "stripe " + d + " reaches its full length exactly at the anchor");
        Equal(16, VisibleCount(peak), "density peaks at the anchor");

        previousCount = VisibleCount(peak);
        previousSum = VisibleLength(peak);
        for (int i = 1; i <= 20; i++)
        {
            CoinCourierTeleportFx.TickForFrame(0.01f, frameId++);
            var sample = Capture();
            Check(VisibleCount(sample) <= previousCount, "arrival count is non-increasing after the anchor (step " + i + ")");
            Check(VisibleLength(sample) <= previousSum + 1e-4f, "arrival length is non-increasing after the anchor (step " + i + ")");
            previousCount = VisibleCount(sample);
            previousSum = VisibleLength(sample);
        }
        CoinCourierTeleportFx.TickForFrame(1f, frameId);
        Equal(0, CoinCourierTeleportFx.ActiveCount, "the arrival group releases at the lifetime end");
    }

    private static void DepartureThinsMonotonically()
    {
        var full = ReferenceFullLengths();
        EnsurePoolDrained();
        const float anchor = 0.18f;
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Departure, anchor);
        float step = anchor / 40f;
        int frameId = 1;
        // 起步锚点前 40 步采样：密度/长度全程单调不增。
        int previousCount = int.MaxValue;
        float previousSum = float.MaxValue;
        for (int i = 1; i <= 40; i++)
        {
            CoinCourierTeleportFx.TickForFrame(step, frameId++);
            var sample = Capture();
            int count = VisibleCount(sample);
            float sum = VisibleLength(sample);
            if (i >= 3)   // 前两帧只是统一的最短淡入（防硬闪），之后必须只减不增
            {
                Check(count <= previousCount, "departure count is non-increasing (step " + i + ")");
                Check(sum <= previousSum + 1e-4f, "departure total length is non-increasing (step " + i + ")");
            }
            if (i == 3)
            {
                Equal(16, count, "departure is dense immediately after the anti-flash fade-in");
                Check(sum >= full.Sum() * 0.95f, "departure starts at (near) full length sum");
            }
            previousCount = count;
            previousSum = sum;
        }
        var atAnchor = Capture();
        int survivors = VisibleCount(atAnchor);
        Check(survivors >= 1 && survivors <= 6, "few lines remain at the anchor, got " + survivors);
        for (int d = 0; d < atAnchor.Lengths.Length; d++)
        {
            if (atAnchor.Alphas[d] > 0.02f)
                Check(atAnchor.Lengths[d] <= full[d] * 0.62f + 1e-3f,
                    "surviving stripe " + d + " is short at the anchor");
        }
        previousCount = survivors;
        int guard = 0;
        while (CoinCourierTeleportFx.ActiveCount > 0 && guard++ < 40)
        {
            CoinCourierTeleportFx.TickForFrame(0.02f, frameId++);
            int count = VisibleCount(Capture());
            Check(count <= previousCount, "departure tail only thins (step " + guard + ")");
            previousCount = count;
        }
        Equal(0, CoinCourierTeleportFx.ActiveCount, "departure releases inside the lifetime");
    }

    private static void StaggerIsStrictAndDeterministic()
    {
        const float anchor = 0.12f;
        var onsets = SampleOnsets(anchor, 700);
        Check(onsets.All(o => o >= 0f), "every stripe activates inside the sampled window");
        var sorted = onsets.OrderBy(o => o).ToArray();
        Check(sorted.Distinct().Count() == sorted.Length, "every stripe activates at its own time");
        Check(sorted.Max() <= anchor * 0.82f, "the latest activation stays well before the anchor");
        for (int i = 1; i < sorted.Length; i++)
        {
            float gap = sorted[i] - sorted[i - 1];
            Check(gap >= 0.0028f && gap <= 0.0135f, "activation spacing " + i + " stays in the compressed window: " + gap);
        }

        EnsurePoolDrained();
        CoinCourierTeleportFx.Clear();   // 第二轮从空池重建，避免读到第一轮的组
        var again = SampleOnsets(anchor, 1400);
        for (int i = 0; i < Math.Min(onsets.Length, again.Length); i++)
            Near(onsets[i], again[i], 1e-4f, "onset " + i + " is deterministic across begins");
    }

    private static float[] SampleOnsets(float anchor, int firstFrameId)
    {
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Arrival, anchor);
        int count = CoinCourierTeleportFx.DashesPerEffect;
        var onsets = Enumerable.Repeat(-1f, count).ToArray();
        int frame = firstFrameId;
        float age = 0f;
        int guard = 0;
        while (age < anchor * 0.9f && guard++ < 600)
        {
            age += 0.001f;
            CoinCourierTeleportFx.TickForFrame(0.001f, frame++);
            for (int i = 0; i < count; i++)
            {
                if (onsets[i] >= 0f) continue;
                var line = Dash(i);
                if (line.startColor.a > 0f || line.endColor.a > 0f) onsets[i] = age;
            }
        }
        return onsets;
    }

    private static void ValidationRejectsBeforeAcquire()
    {
        var invalidStyle = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            (CoinCourierTeleportStyle)99, CoinCourierTeleportDirection.Arrival, 0.12f);
        Check(!invalidStyle.IsValid, "an unknown style is refused");
        var invalidDirection = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, (CoinCourierTeleportDirection)5, 0.12f);
        Check(!invalidDirection.IsValid, "an unknown direction is refused");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, 0f, -0.05f, 0.02f, 0.4f })
        {
            var handle = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
                CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival, bad);
            Check(!handle.IsValid, "materialize time " + bad + " is refused");
        }
        Equal(0, CoinCourierTeleportFx.ActiveCount, "no pool slot is consumed by rejected inputs");
        var valid = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival,
            CoinCourierTeleportFx.MaxAnchorSeconds);
        Check(valid.IsValid, "the maximum in-range anchor is accepted");
        Equal(1, CoinCourierTeleportFx.ActiveCount, "the pool still works after the refusals");
        CoinCourierTeleportFx.Cancel(valid);
    }

    private static void PoolCapAndStaleHandles()
    {
        var handles = new List<CoinCourierFxHandle>();
        for (int i = 0; i < CoinCourierTeleportFx.MaxConcurrent + 1; i++)
        {
            handles.Add(CoinCourierTeleportFx.Begin(new Vector3(i, 0f, 0f), Color.white, 1f, 0, 0,
                CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Departure, 0.12f));
            Check(handles[i].IsValid, "begin " + i + " valid");
        }
        Equal(CoinCourierTeleportFx.MaxConcurrent, CoinCourierTeleportFx.ActiveCount, "the pool is capped at 18 groups");
        CoinCourierTeleportFx.Cancel(handles[0]);
        Equal(CoinCourierTeleportFx.MaxConcurrent, CoinCourierTeleportFx.ActiveCount,
            "a recycled handle cannot cancel the new owner of its slot");
        CoinCourierTeleportFx.Clear();
        CoinCourierTeleportFx.Cancel(handles[handles.Count - 1]);
        Equal(0, CoinCourierTeleportFx.ActiveCount, "handles are inert after Clear");
        var fresh = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival, 0.12f);
        Check(fresh.IsValid, "the pool restarts after Clear");
        CoinCourierTeleportFx.Cancel(fresh);
    }

    private static void PauseAndFrameDedup()
    {
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Arrival, 0.12f);
        CoinCourierTeleportFx.TickForFrame(0.04f, 100);
        float before = Length(Dash(0));
        float alphaBefore = Alpha(Dash(0));
        CoinCourierTeleportFx.TickForFrame(0.04f, 100);
        Near(before, Length(Dash(0)), 1e-6f, "same frame does not double advance");
        CoinCourierTeleportFx.TickForFrame(0f, 101);
        CoinCourierTeleportFx.TickForFrame(float.NaN, 102);
        CoinCourierTeleportFx.TickForFrame(-1f, 103);
        Near(before, Length(Dash(0)), 1e-6f, "pause and invalid deltas change no geometry");
        Near(alphaBefore, Alpha(Dash(0)), 1e-6f, "pause and invalid deltas change no alpha");
        CoinCourierTeleportFx.TickForFrame(0.04f, 104);
        Check(Length(Dash(0)) > before, "the next real frame advances the effect");
    }

    private static void IndependentHandlesCoexist()
    {
        var departure = CoinCourierTeleportFx.Begin(new Vector3(-2f, 0f, 0f), Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Departure, 0.18f);
        var arrival = CoinCourierTeleportFx.Begin(new Vector3(2f, 0f, 0f), Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical, CoinCourierTeleportDirection.Arrival, 0.18f);
        Check(departure.IsValid && arrival.IsValid, "both handles valid");
        Equal(2, CoinCourierTeleportFx.ActiveCount, "departure and arrival are two independent groups");
        CoinCourierTeleportFx.TickForFrame(1f / 60f, 900);
        // 组按创建顺序：0 = departure（起步近满长且可见），1 = arrival（入场刚起步，明显更短）。
        var departureLine = DashOfGroup(0, 0);
        var arrivalLine = DashOfGroup(1, 0);
        float departLength = Length(departureLine);
        float arriveLength = Length(arrivalLine);
        Check(departureLine.enabled && Alpha(departureLine) > 0.05f,
            "the departure group is still playing after the arrival begin");
        Check(arriveLength < departLength * 0.6f,
            "the arrival group starts short: begin(to) never cancels or replaces begin(from)");

        CoinCourierTeleportFx.Cancel(arrival);
        Equal(1, CoinCourierTeleportFx.ActiveCount, "cancelling the arrival leaves the departure owned");
        Check(DashOfGroup(0, 0).enabled && Alpha(DashOfGroup(0, 0)) > 0.05f,
            "the departure group was not disabled by the arrival cancel");
        CoinCourierTeleportFx.Cancel(departure);
        Equal(0, CoinCourierTeleportFx.ActiveCount, "both handles cancel cleanly");
    }

    /// <summary>按效果组序访问真实 LineRenderer（同名 dash 跨组存在，必须按组定位）。</summary>
    private static LineRenderer DashOfGroup(int groupIndex, int dashIndex)
    {
        var groups = GameObject.All.Where(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportFxEffect").ToList();
        if (groupIndex >= groups.Count) throw new Exception("effect group " + groupIndex + " missing");
        var child = groups[groupIndex].transform.Children
            .First(t => t.gameObject.name == "KEM_CoinCourierTeleportDash" + dashIndex);
        return child.gameObject.GetComponent<LineRenderer>();
    }

    private static void AnchorDifference()
    {
        // .12 与 .18 的显形差异：峰值/收束基准整体后移；两个锚点都各自在自身锚点处达峰。
        foreach (float anchor in new[] { 0.12f, 0.18f })
        {
            EnsurePoolDrained();
            CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 0, 0,
                CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportDirection.Arrival, anchor);
            float step = anchor / 60f;
            int frameId = 500;
            float peakAt = -1f;
            float peakSum = 0f;
            float age = 0f;
            for (int i = 1; i <= 60; i++)
            {
                age += step;
                CoinCourierTeleportFx.TickForFrame(step, frameId++);
                var sample = Capture();
                float sum = VisibleLength(sample);
                if (sum > peakSum)
                {
                    peakSum = sum;
                    peakAt = age;
                }
            }
            Near(anchor, peakAt, step + 1e-4f, "the arrival peak lands on its own anchor " + anchor);
            Equal(16, VisibleCount(Capture()), "all stripes visible at the " + anchor + " peak");
        }
    }
}

/// <summary>
/// 真实生产帧导出：对每个 (style, direction, anchor) 组合执行生产 Begin + TickForFrame，
/// 按 60FPS 采样 20 帧，导出每条线的 4 点坐标 / start-end RGBA / widthCurve+multiplier /
/// visibility。渲染器只画这些数据（私有预览），不复写任何曲线。
/// </summary>
internal static class Exporter
{
    private sealed class RunSpec
    {
        internal string Id;
        internal string Char;
        internal CoinCourierTeleportStyle Style;
        internal CoinCourierTeleportDirection Direction;
        internal float Anchor;
        internal float Reveal;
        internal string Png;
        internal float[] Cell;
        internal float[] Pivot;
        internal float Ppu;
        internal float[] Scale;
        internal float[] Box;
        internal float[] Mesh;
        internal int VertexCount;
        internal string Name;
        internal bool ReferenceOnly;
        internal bool HasMesh;
    }

    private sealed class FixtureFile
    {
        public List<FixtureRun> runs { get; set; }
    }

    private sealed class FixtureRun
    {
        public string id { get; set; }
        public string char_ { get; set; }
        public string png { get; set; }
        public float[] cell { get; set; }
        public float[] pivot { get; set; }
        public float ppu { get; set; }
        public float[] scale { get; set; }
        public string style { get; set; }
        public string direction { get; set; }
        public float anchor { get; set; }
        public float[] box { get; set; }
        public float[] mesh { get; set; }
        public int vertexCount { get; set; }
        public string name { get; set; }
        public bool referenceOnly { get; set; }
    }

    internal static void Run(string directory, string fixturesPath = null)
    {
        Directory.CreateDirectory(directory);
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        var runs = new List<RunSpec>();
        if (!string.IsNullOrEmpty(fixturesPath) && File.Exists(fixturesPath))
        {
            // fixture 属性名带 char（C# 关键字）的别名映射。
            string raw = File.ReadAllText(fixturesPath).Replace("\"char\":", "\"char_\":");
            var file = JsonSerializer.Deserialize<FixtureFile>(raw);
            foreach (var run in file.runs)
            {
                runs.Add(new RunSpec
                {
                    Id = run.id,
                    Char = run.char_,
                    Style = Enum.Parse<CoinCourierTeleportStyle>(run.style),
                    Direction = Enum.Parse<CoinCourierTeleportDirection>(run.direction),
                    Anchor = run.anchor,
                    Reveal = run.direction == "Arrival" ? run.anchor : 0f,
                    Png = run.png,
                    Cell = run.cell,
                    Pivot = run.pivot,
                    Ppu = run.ppu,
                    Scale = run.scale,
                    Box = run.box,
                    Mesh = run.mesh,
                    VertexCount = run.vertexCount,
                    Name = run.name,
                    ReferenceOnly = run.referenceOnly,
                    HasMesh = run.mesh != null && run.mesh.Length == 4
                });
            }
        }
        else
        {
            runs.Add(new RunSpec { Id = "gold-h-arrival-12", Char = "template", Style = CoinCourierTeleportStyle.Horizontal, Direction = CoinCourierTeleportDirection.Arrival, Anchor = 0.12f, Reveal = 0.12f });
            runs.Add(new RunSpec { Id = "gold-h-departure-12", Char = "template", Style = CoinCourierTeleportStyle.Horizontal, Direction = CoinCourierTeleportDirection.Departure, Anchor = 0.12f });
            runs.Add(new RunSpec { Id = "black-v-arrival-12", Char = "template", Style = CoinCourierTeleportStyle.Vertical, Direction = CoinCourierTeleportDirection.Arrival, Anchor = 0.12f, Reveal = 0.12f });
            runs.Add(new RunSpec { Id = "black-v-departure-12", Char = "template", Style = CoinCourierTeleportStyle.Vertical, Direction = CoinCourierTeleportDirection.Departure, Anchor = 0.12f });
            runs.Add(new RunSpec { Id = "black-v-arrival-18", Char = "template", Style = CoinCourierTeleportStyle.Vertical, Direction = CoinCourierTeleportDirection.Arrival, Anchor = 0.18f, Reveal = 0.18f });
            runs.Add(new RunSpec { Id = "black-v-departure-18", Char = "template", Style = CoinCourierTeleportStyle.Vertical, Direction = CoinCourierTeleportDirection.Departure, Anchor = 0.18f });
        }
        var payload = new Dictionary<string, object>
        {
            ["meta"] = new Dictionary<string, object>
            {
                ["lifetime"] = CoinCourierTeleportFx.LifetimeSeconds,
                ["fps"] = 60,
                ["frames"] = 20,
                ["stripes"] = CoinCourierTeleportFx.DashesPerEffect,
                ["note"] = "code-driven export: real production Begin/TickForFrame against LineRenderer doubles; the preview renderer draws only this data"
            },
            ["runs"] = new List<object>()
        };
        var runList = (List<object>)payload["runs"];
        foreach (var spec in runs)
        {
            Cleanup();
            SpriteRenderer body = BuildBody(spec);
            Console.WriteLine("run " + spec.Id + " body=" + (body != null) + " resolve=" + (body != null && CoinCourierTeleportBodyBox.TryResolve(body, out _)));
            var lineWidths = new List<object>();
            CoinCourierTeleportFx.Begin(Vector3.zero, new Color(0.95f, 0.82f, 0.42f, 0.85f), 1f, 0, 0,
                spec.Style, spec.Direction, spec.Anchor, body);
            for (int i = 0; i < CoinCourierTeleportFx.DashesPerEffect; i++)
            {
                var line = Dash(i);
                lineWidths.Add(new
                {
                    keys = line.widthCurve.keys.Select(k => new { time = k.time, value = k.value }).ToArray()
                });
            }
            var frames = new List<object>();
            int frameId = 1000;
            for (int f = 0; f < 20; f++)
            {
                if (f > 0) CoinCourierTeleportFx.TickForFrame(1f / 60f, frameId++);
                var lines = new List<object>();
                for (int i = 0; i < CoinCourierTeleportFx.DashesPerEffect; i++)
                {
                    var line = Dash(i);
                    var p0 = line.Positions[0];
                    var p1 = line.Positions[1];
                    var p2 = line.Positions[2];
                    var p3 = line.Positions[3];
                    var s = line.startColor;
                    var e = line.endColor;
                    lines.Add(new
                    {
                        x = new[] { p0.x, p1.x, p2.x, p3.x },
                        y = new[] { p0.y, p1.y, p2.y, p3.y },
                        start = new[] { s.r, s.g, s.b, s.a },
                        end = new[] { e.r, e.g, e.b, e.a },
                        width = line.widthMultiplier,
                        enabled = line.enabled,
                        visible = line.enabled && (s.a > 0.002f || e.a > 0.002f)
                    });
                }
                frames.Add(new { age = f / 60f, lines });
            }
            object bodyInfo = null;
            if (body != null && CoinCourierTeleportBodyBox.TryResolve(body, out CoinCourierTeleportBodyBox.Box box))
            {
                bodyInfo = new
                {
                    width = box.Width,
                    height = box.Height,
                    cx = box.Center.x,
                    cy = box.Center.y
                };
            }
            runList.Add(new
            {
                id = spec.Id,
                charName = spec.Char,
                style = spec.Style.ToString(),
                direction = spec.Direction.ToString(),
                anchor = spec.Anchor,
                reveal = spec.Reveal,
                referenceOnly = spec.ReferenceOnly,
                png = spec.Png,
                cell = spec.Cell,
                pivot = spec.Pivot,
                ppu = spec.Ppu,
                scale = spec.Scale,
                box = spec.HasMesh ? null : spec.Box,
                mesh = spec.Mesh,
                body = bodyInfo,
                widths = lineWidths,
                frames
            });
            Cleanup();
        }
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        File.WriteAllText(Path.Combine(directory, "teleport-fx-frames.json"), json);
        Console.WriteLine("exported " + runs.Count + " runs to " + Path.Combine(directory, "teleport-fx-frames.json"));
    }

    private static SpriteRenderer BuildBody(RunSpec spec)
    {
        if (spec.HasMesh)
        {
            var go = new GameObject("fx-body-" + spec.Id);
            var renderer = go.AddComponent<SpriteRenderer>();
            int count = Math.Max(5, Math.Min(10, spec.VertexCount));
            var vertices = new Vector2[count];
            vertices[0] = new Vector2(spec.Mesh[0], spec.Mesh[1]);
            vertices[1] = new Vector2(spec.Mesh[2], spec.Mesh[1]);
            vertices[2] = new Vector2(spec.Mesh[2], spec.Mesh[3]);
            vertices[3] = new Vector2(spec.Mesh[0], spec.Mesh[3]);
            for (int i = 4; i < count; i++) vertices[i] = vertices[0];
            renderer.sprite = new Sprite
            {
                name = spec.Name,
                texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { isReadable = false },
                rect = new Rect(0f, 0f, 32f, 32f),
                pivot = new Vector2(spec.Pivot[0], spec.Pivot[1]),
                pixelsPerUnit = spec.Ppu,
                packed = true,
                vertices = vertices
            };
            go.transform.localScale = new Vector3(spec.Scale[0], spec.Scale[1], 1f);
            return renderer;
        }
        if (spec.Box == null || spec.Box.Length != 4) return null;
        var host = new GameObject("fx-body-" + spec.Id);
        var target = host.AddComponent<SpriteRenderer>();
        float[] cell = spec.Cell;
        float[] b = spec.Box;
        int width = (int)MathF.Ceiling(MathF.Max(cell[0] + cell[2], b[0] + b[2]));
        int height = (int)MathF.Ceiling(MathF.Max(cell[1] + cell[3], b[1] + b[3]));
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            OpaqueAt = (x, y) => x >= b[0] && x < b[0] + b[2] && y >= b[1] && y < b[1] + b[3],
        };
        target.sprite = new Sprite
        {
            rect = new Rect(cell[0], cell[1], cell[2], cell[3]),
            pivot = new Vector2(spec.Pivot[0], spec.Pivot[1]),
            pixelsPerUnit = spec.Ppu,
            texture = texture
        };
        host.transform.localScale = new Vector3(spec.Scale[0], spec.Scale[1], 1f);
        return target;
    }

    private static void Cleanup()
    {
        try { CoinCourierTeleportFx.Clear(); } catch { }
        GameObject.All.Clear();
        GameObject.ResetCounters();
        Time.frameCount = 0;
    }

    private static LineRenderer Dash(int index)
        => GameObject.All.First(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportDash" + index)
            .GetComponent<LineRenderer>();
}
