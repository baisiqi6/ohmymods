// Bank-assistant atlas regression suite (managed doubles; no Unity runtime, no game, no PNG bytes).
// Verifies: the fixed rect/pivot/PPU table against the checked-in production metadata, the
// authored-phase frame math (Idle/Leisure; all 8 frames per action, wrapping, invalid phase),
// byte-decode prevalidation, the runtime commit contract (only sprite writes; native overwrite
// repaired; enabled/transform/animator untouched; life-scoped state with no stale carry-over),
// and — since the 2026-09-29 gait revision — the Walk/Run cadence clock: phase integrates
// dt*speed/strideWorld in double, freezes on pause/save/menu/waiting/unknown/zero-speed inputs,
// holds back on bad stride/scale, keeps one shared phase across Walk<->Run and speed changes,
// and resets per life. Passing this suite does not establish real rendering; the HTML preview
// and the in-game pass own that.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int passed, failed;
    private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static int Main()
    {
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        Test("metadata layout matches the production json", MetadataMatchesProductionJson);
        Test("pixels-per-unit reproduces the target stand height", StandHeightFromPpu);
        Test("metadata slot mapping and rect laws", SlotMappingAndRectLaws);
        Test("frame math covers every frame and wraps on authored duration", FrameMathCoversAndWraps);
        Test("frame math rejects invalid phases deterministically", FrameMathInvalidPhases);
        Test("decode builds 32 fixed sprites per style", DecodeBuildsFixedSprites);
        Test("decode is fail-closed on bad bytes, size, or empty art", DecodeFailClosed);
        Test("asset prevalidation fails closed without embedded resources", EnsureAssetsFailClosed);
        Test("tick writes the atlas frame from the native state clock", TickWritesNativeFrame);
        Test("tick repairs a native sprite overwrite and stays idempotent", TickRepairsOverwriteAndIdempotent);
        Test("tick never touches enabled, transform, or animator", TickWritesNothingElse);
        Test("tick ignores slots outside 4..7", TickIgnoresForeignSlots);
        Test("tick ignores inactive/template actors and clears their state", TickIgnoresInactive);
        Test("tick follows the real banker states only (no speed guessing)", TickResolvesWalkRun);
        Test("tick rejects unknown states and bad samples without writing", TickRejectsBadSamples);
        Test("pause/save/menu freeze writes and resume continues", FreezeGates);
        Test("scope gate clears instance state outside the Greek current layer", ScopeGateClearsState);
        Test("leisure requires permission, follows its clock, and yields to work", LeisurePermissionAndPreemption);
        Test("forget clears the life state, stale identity resets it", ForgetAndStaleIdentity);
        Test("atlas resources survive an unused-assets sweep before first use", SweepBeforeFirstUseKeepsAtlas);
        Test("atlas resources survive sweeps across action switches", SweepMidLifeKeepsAtlas);
        Test("partial decode failure releases every retained object", PartialFailureReleasesRetainedObjects);
        Test("frame diagnostics name slot, frame, style, and cause", FrameDiagnosticsNameContext);
        Test("first-apply evidence is bounded per style", FirstApplyEvidenceBoundedPerStyle);
        // 2026-09-29 gait cadence clock (issue-89 revision): external-invariant battery.
        Test("gait cycle period scales with speed and covers half cycles", GaitCyclePeriodScalesWithSpeed);
        Test("gait phase is fps-independent over the same distance", GaitFpsIndependence);
        Test("gait phase is continuous across speed and Walk/Run changes", GaitContinuityAcrossChanges);
        Test("gait authority and client speeds agree in game seconds", GaitAuthorityClientUnitNormalization);
        Test("gait stride scales with lossyScale.x including flips", GaitStrideScalesWithLossyScaleX);
        Test("gait invalid inputs freeze or hold back without guessing", GaitInvalidInputsFreezeOrHoldBack);
        Test("gait huge deltas keep the phase fraction", GaitHugeDeltaPreservesPhaseFraction);
        Test("gait phase resets per life and survives identity reuse", GaitPhaseResetsOnNewLife);
        Test("gait repairs native overwrites while walking", GaitRepairsNativeOverwriteWhileWalking);
        Test("gait maps phase boundaries to all eight frames", GaitMappingBoundaries);
        // Correction round (approved runtime refinement): direct reads + double normalization.
        Test("gait increments match stride/speed at contract speeds", GaitValidInputIncrements);
        Test("gait client normalization is double and capped at extremes", GaitExtremeClientNormalization);
        Test("gait read failure fails the whole frame once and resumes", GaitReadExceptionBoundary);

        Console.WriteLine("bank-assistant-atlas: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ harness

    private static void Test(string name, Action body)
    {
        ResetAll();
        try
        {
            body();
            passed++;
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    private static void Check(bool condition, string why)
    {
        if (!condition) throw new Exception(why);
    }

    private static void Equal(int expected, int actual, string why)
    {
        if (expected != actual) throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Equal(string expected, string actual, string why)
    {
        if (expected != actual) throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Near(double expected, double actual, double tolerance, string why)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void ResetAll()
    {
        SetStatic(typeof(BankAssistantAtlasVisuals), "_atlasState", 0);
        SetStatic(typeof(BankAssistantAtlasVisuals), "_sprites", null);
        SetStatic(typeof(BankAssistantAtlasVisuals), "_textures", null);
        ((IDictionary)GetStatic(typeof(BankAssistantAtlasVisuals), "States"))?.Clear();
        ((ICollection<string>)GetStatic(typeof(BankAssistantAtlasVisuals), "Warned"))?.Clear();
        ClearStaticCollection(typeof(BankAssistantAtlasVisuals), "FirstApplyLogged");
        ManualLogSource.Reset();
        GreekBankScope.IsActive = true;
        GreekBankScope.LayerOk = true;
        IslandSaveData.isSavingGame = false;
        Managers.Inst = new Managers { game = new Game() };
        Time.timeScale = 1f;
        GameObject.ResetCounters();
        Transform.ResetCounters();
        Behaviour.EnabledWrites = 0;
        SpriteRenderer.ResetCounters();
        Sprite.ResetCounters();
        Texture2D.ResetCounters();
        Animator.ResetCounters();
        UnityEngine.Object.DestroyCalls = 0;
        ImageConversion.Reset();
        Texture2D.FillAlpha = 0;
        Time.deltaTime = 0.1f;
        NetworkBigBoss.HasWorldAuth = true;
        BankAssistantTeleportVisuals.ResetWaiting();
    }

    // ------------------------------------------------------------ gait helpers

    private static object GaitStateOf(GameObject actor)
    {
        var states = (IDictionary)GetStatic(typeof(BankAssistantAtlasVisuals), "States");
        object state = states[actor.GetInstanceID()];
        if (state == null) throw new Exception("no visual state for instance " + actor.GetInstanceID());
        return state;
    }

    private static double GaitPhaseOf(GameObject actor)
    {
        var field = GaitStateOf(actor).GetType().GetField("GaitPhase",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new Exception("ActorState.GaitPhase field missing");
        return (double)field.GetValue(GaitStateOf(actor));
    }

    /// <summary>测试直接布置相位（映射/冻结类检查用），绕过推进公式。</summary>
    private static void SetGaitPhase(GameObject actor, double phase)
    {
        var field = GaitStateOf(actor).GetType().GetField("GaitPhase",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(GaitStateOf(actor), phase);
    }

    /// <summary>每周期世界步幅（生产数据契约）：stridePx / PPU × |lossyScale.x|。</summary>
    private static double StrideWorld(BankAssistantAtlasStyle style, BankAssistantAtlasAction action,
        float lossyScaleX = 1f)
    {
        float px = action == BankAssistantAtlasAction.Walk ? style.WalkStridePixels : style.RunStridePixels;
        // 与生产一致的双精度路径：(double)px / PPU × |lossyScale.x|
        return (double)px / style.PixelsPerUnit * Math.Abs((double)lossyScaleX);
    }

    private static void TickGait(GameObject actor, int slot, string state, float speed, float dt, int ticks)
    {
        Time.deltaTime = dt;
        for (int i = 0; i < ticks; i++)
        {
            SetNative(actor, state, 0.5f, 1f, speed);   // 相位由 gaitPhase 决定，native 相位仅确认状态
            BankAssistantAtlasVisuals.Tick(actor, slot, false);
        }
    }

    private static void SetStatic(Type type, string field, object value)
    {
        var info = type.GetField(field, StaticFlags);
        if (info == null) throw new Exception("reflection target missing: " + type.Name + "." + field);
        info.SetValue(null, value);
    }

    private static object GetStatic(Type type, string field)
    {
        var info = type.GetField(field, StaticFlags);
        if (info == null) throw new Exception("reflection target missing: " + type.Name + "." + field);
        return info.GetValue(null);
    }

    /// <summary>清空可选静态集合：红对照（旧源码）没有新增字段时跳过而不是崩。</summary>
    private static void ClearStaticCollection(Type type, string field)
    {
        object value = type.GetField(field, StaticFlags)?.GetValue(null);
        value?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(value, null);
    }

    private static IntPtr StatePointer(int instanceId)
    {
        var states = (IDictionary)GetStatic(typeof(BankAssistantAtlasVisuals), "States");
        object state = states[instanceId];
        if (state == null) throw new Exception("no visual state for instance " + instanceId);
        var field = state.GetType().GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new Exception("ActorState.Pointer field missing");
        return (IntPtr)field.GetValue(state);
    }

    private static GameObject MakeActor(int? forcedInstanceId = null, IntPtr? forcedPointer = null)
    {
        var actor = new GameObject("KEM_BankAssistant_test");
        if (forcedInstanceId.HasValue) actor.InstanceIdOverride = forcedInstanceId.Value;
        if (forcedPointer.HasValue) actor.Pointer = forcedPointer.Value;
        actor.AddComponent<SpriteRenderer>();
        actor.AddComponent<Animator>();
        return actor;
    }

    private static Animator AnimatorOf(GameObject actor) => actor.GetComponent<Animator>();
    private static SpriteRenderer RendererOf(GameObject actor) => actor.GetComponent<SpriteRenderer>();

    private static void SetNative(GameObject actor, string stateName, float normalizedTime, float length,
        float speed)
    {
        var animator = AnimatorOf(actor);
        animator.State = new AnimatorStateInfo
        {
            shortNameHash = Animator.StringToHash(stateName),
            normalizedTime = normalizedTime,
            length = length,
        };
        animator.Speed = speed;
    }

    /// <summary>解码四张图集（stub ImageConversion）并返回 sprites/textures，不装入模块缓存。</summary>
    private static Sprite[][] DecodeAllStyles(out Texture2D[] textures)
    {
        ImageConversion.Result = true;
        Texture2D.FillAlpha = 255;
        var styles = BankAssistantAtlasMetadata.Styles;
        var sprites = new Sprite[styles.Length][];
        textures = new Texture2D[styles.Length];
        var decode = typeof(BankAssistantAtlasVisuals).GetMethod("DecodeSheet", StaticFlags);
        Check(decode != null, "DecodeSheet must exist for direct linkage");
        for (int i = 0; i < styles.Length; i++)
        {
            // stub 的 PNG 尺寸模拟按风格配置（真实 bytes 的 IHDR/哈希由 metadata 测试独立覆盖）。
            ImageConversion.SimulatedWidth = styles[i].SheetWidth;
            ImageConversion.SimulatedHeight = styles[i].SheetHeight;
            object[] args = { styles[i], Array.Empty<byte>(), null, null };
            bool ok = (bool)decode.Invoke(null, args);
            Check(ok, "decode failed for style " + styles[i].Name);
            sprites[i] = (Sprite[])args[2];
            textures[i] = (Texture2D)args[3];
        }
        return sprites;
    }

    /// <summary>用 stub ImageConversion 解码四张图集并装入模块缓存（等价 EnsureAssets 成功路径）。</summary>
    private static Sprite[][] InstallAtlases()
    {
        Sprite[][] sprites = DecodeAllStyles(out Texture2D[] textures);
        SetStatic(typeof(BankAssistantAtlasVisuals), "_sprites", sprites);
        SetStatic(typeof(BankAssistantAtlasVisuals), "_textures", textures);
        SetStatic(typeof(BankAssistantAtlasVisuals), "_atlasState", 1);
        Check(BankAssistantAtlasVisuals.AssetsReady, "atlases installed");
        return sprites;
    }

    // ------------------------------------------------------------------ metadata

    private static JsonDocument LoadProductionMetadata()
    {
        string[] candidates =
        {
            Path.Combine("..", "..", "artifacts", "bank-assistant-skins-20260928", "production-metadata.json"),
            Path.Combine("artifacts", "bank-assistant-skins-20260928", "production-metadata.json"),
        };
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate));
        }
        throw new Exception("production-metadata.json not found (run from tests/bank-assistant-atlas)");
    }

    /// <summary>仓库相对路径 → 现存文件（与 metadata 相同的双候选：cwd = 仓库根或 tests/bank-assistant-atlas）。</summary>
    private static string ResolveRepoPath(string relative)
    {
        string[] candidates = { Path.Combine("..", "..", relative), relative };
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }
        throw new Exception("repo path not found: " + relative);
    }

    /// <summary>PNG IHDR 宽高（大端，字节 16..24）。只读头部，不解码像素。</summary>
    private static (int Width, int Height) PngHeaderDimensions(byte[] bytes)
    {
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Check(bytes.Length >= 24, "PNG too short for an IHDR header");
        for (int i = 0; i < signature.Length; i++) Check(bytes[i] == signature[i], "PNG signature byte " + i);
        Check(bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[14] == (byte)'D' && bytes[15] == (byte)'R',
            "first chunk must be IHDR");
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }

    private static void MetadataMatchesProductionJson()
    {
        using var doc = LoadProductionMetadata();
        JsonElement root = doc.RootElement;
        Equal("bank-assistant-atlas-production-metadata/2", root.GetProperty("schema").GetString(), "schema version");
        Near(32.0 / BankAssistantAtlasMetadata.TargetStandHeight, root.GetProperty("fixed_ppu").GetDouble(), 1e-9,
            "fixed rig ppu (32px / 0.671875)");
        Near(BankAssistantAtlasMetadata.TargetStandHeight, root.GetProperty("target_stand_height_world").GetDouble(),
            1e-9, "target stand height");

        JsonElement jsonStyles = root.GetProperty("styles");
        Equal(BankAssistantAtlasMetadata.Styles.Length, jsonStyles.GetArrayLength(), "style count");

        for (int s = 0; s < jsonStyles.GetArrayLength(); s++)
        {
            JsonElement js = jsonStyles[s];
            BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[s];
            string ctx = " style " + s;
            Equal(js.GetProperty("name").GetString(), style.Name, "style name" + ctx);
            Equal(js.GetProperty("slot").GetInt32(), 4 + s, "style slot" + ctx);
            Equal(js.GetProperty("resource").GetString(), style.ResourceName, "resource name" + ctx);

            int[] sheet = js.GetProperty("sheet").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int[] cell = js.GetProperty("cell").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int[] anchor = js.GetProperty("anchor").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            Equal(style.SheetWidth, sheet[0], "sheet width" + ctx);
            Equal(style.SheetHeight, sheet[1], "sheet height" + ctx);
            Equal(BankAssistantAtlasMetadata.Columns * cell[0], sheet[0], "8 grid columns tile the sheet" + ctx);
            Equal(BankAssistantAtlasMetadata.Rows * cell[1], sheet[1], "4 grid rows tile the sheet" + ctx);
            Near(js.GetProperty("ppu").GetDouble(), style.PixelsPerUnit, 1e-3, "ppu" + ctx);
            Equal(style.PivotYPixels, js.GetProperty("pivot_y_px").GetInt32(), "pivot y" + ctx);
            Equal(cell[1] - anchor[1], style.PivotYPixels, "pivot y = cell height - anchor y" + ctx);
            Equal(32, js.GetProperty("standing_body_px").GetInt32(), "standing body px" + ctx);

            // 生产资产 = 真实保存的 PNG bytes：SHA256 与 IHDR 尺寸必须与装配数据一致。
            // 只读头部/整字节，不解码像素（像素/帧 QA 由 Operator 的 PIL 证据负责）。
            string assetPath = ResolveRepoPath(js.GetProperty("asset").GetString());
            byte[] assetBytes = File.ReadAllBytes(assetPath);
            string assetHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(assetBytes))
                .ToLowerInvariant();
            Equal(js.GetProperty("asset_sha256").GetString(), assetHash, "asset sha256" + ctx);
            Equal(js.GetProperty("source_atlas_sha256").GetString(), assetHash,
                "asset is the byte-copied accepted source" + ctx);
            (int pngWidth, int pngHeight) = PngHeaderDimensions(assetBytes);
            Equal(style.SheetWidth, pngWidth, "PNG IHDR width" + ctx);
            Equal(style.SheetHeight, pngHeight, "PNG IHDR height" + ctx);

            JsonElement durations = js.GetProperty("durations_seconds");
            Near(style.IdleSeconds, durations.GetProperty("idle").GetDouble(), 1e-6, "idle duration" + ctx);
            Near(style.WalkSeconds, durations.GetProperty("walk").GetDouble(), 1e-6, "walk preview duration" + ctx);
            Near(style.RunSeconds, durations.GetProperty("run").GetDouble(), 1e-6, "run preview duration" + ctx);
            Near(style.LeisureSeconds, durations.GetProperty("leisure").GetDouble(), 1e-6, "leisure duration" + ctx);

            JsonElement strides = js.GetProperty("strides_px_per_full_cycle");
            Near(style.WalkStridePixels, strides.GetProperty("walk").GetDouble(), 1e-6, "walk stride" + ctx);
            Near(style.RunStridePixels, strides.GetProperty("run").GetDouble(), 1e-6, "run stride" + ctx);

            JsonElement pivotFrames = js.GetProperty("pivot_x_px_per_frame");
            Equal(32, pivotFrames.GetArrayLength(), "per-frame pivot count" + ctx);
            for (int f = 0; f < 32; f++)
            {
                Near(anchor[0], pivotFrames[f].GetDouble(), 1e-6, "fixed rig pivot x = anchor, frame " + f + ctx);
                Near(style.PivotXPixels[f], pivotFrames[f].GetDouble(), 1e-6,
                    "c# pivot matches json frame " + f + ctx);
            }

            JsonElement actions = js.GetProperty("actions");
            Equal(4, actions.GetArrayLength(), "action rows" + ctx);
            for (int a = 0; a < 4; a++)
            {
                JsonElement ja = actions[a];
                Equal(a, ja.GetProperty("row").GetInt32(), "action row index" + ctx);
                int rectY = ja.GetProperty("rect_y").GetInt32();
                Equal(cell[1], ja.GetProperty("rect_height").GetInt32(), "uniform rect height" + ctx);
                Equal(cell[0], ja.GetProperty("rect_width").GetInt32(), "uniform rect width" + ctx);
                Equal((BankAssistantAtlasMetadata.Rows - 1 - a) * cell[1], rectY, "grid rect y" + ctx);
                Equal(a * cell[1] + anchor[1], ja.GetProperty("ground_y_top").GetInt32(), "rig ground line" + ctx);
                Equal(anchor[1], ja.GetProperty("ground_y_top_in_cell").GetInt32(), "ground line in cell" + ctx);
                Equal(style.SheetHeight - (rectY + style.PivotYPixels), ja.GetProperty("ground_y_top").GetInt32(),
                    "pivot sits on the json ground line, action " + a + ctx);
                for (int k = 0; k < 8; k++)
                {
                    int frame = a * 8 + k;
                    Rect rect = style.Rects[frame];
                    Equal(rectY, (int)rect.y, "uniform rect y frame " + frame + ctx);
                    Equal(cell[0], (int)rect.width, "uniform rect width frame " + frame + ctx);
                    Equal(cell[1], (int)rect.height, "uniform rect height frame " + frame + ctx);
                }
            }

            JsonElement frames = js.GetProperty("frames");
            Equal(32, frames.GetArrayLength(), "frame count" + ctx);
            var rects = new Rect[32];
            var measuredBottom = new int[32];
            for (int f = 0; f < 32; f++)
            {
                JsonElement jf = frames[f];
                Equal(f, jf.GetProperty("f").GetInt32(), "frame index order" + ctx);
                int[] bbox = jf.GetProperty("bbox").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                int[] rect = jf.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()).ToArray();

                // 整格网格律：rect 必须等于 (col*cellW, (3-row)*cellH, cellW, cellH)，且与 C# 表一致。
                int row = f / BankAssistantAtlasMetadata.FramesPerAction;
                int column = f % BankAssistantAtlasMetadata.FramesPerAction;
                int[] expectedRect = { column * cell[0], (BankAssistantAtlasMetadata.Rows - 1 - row) * cell[1],
                    cell[0], cell[1] };
                for (int i = 0; i < 4; i++) Equal(expectedRect[i], rect[i], "grid rect[" + i + "] frame " + f + ctx);
                Rect actual = style.Rects[f];
                Equal(rect[0], (int)actual.x, "rect x frame " + f + ctx);
                Equal(rect[1], (int)actual.y, "rect y frame " + f + ctx);
                Equal(rect[2], (int)actual.width, "rect w frame " + f + ctx);
                Equal(rect[3], (int)actual.height, "rect h frame " + f + ctx);
                rects[f] = actual;

                // 实测 bbox（top-origin，右/下开区间）：完整主体在自己的格内、四周 >=2px 余量
                // （无格边截肢/跨格渗漏），绝不低于固定 rig 地面线（Run 腾空帧允许抬起）。
                int cellLeft = column * cell[0];
                int cellTop = row * cell[1];
                Check(bbox[2] > bbox[0] && bbox[3] > bbox[1], "bbox non-empty frame " + f + ctx);
                Check(bbox[0] >= cellLeft + 2 && bbox[2] <= cellLeft + cell[0] - 2,
                    "body stays horizontally inside its cell, frame " + f + ctx);
                Check(bbox[1] >= cellTop + 2 && bbox[3] <= cellTop + cell[1] - 2,
                    "body stays vertically inside its cell, frame " + f + ctx);
                Check(bbox[3] <= cellTop + anchor[1], "no body below the rig ground line, frame " + f + ctx);
                Equal(bbox[3], jf.GetProperty("bottom_y_top").GetInt32(), "bottom row echo frame " + f + ctx);
                measuredBottom[f] = bbox[3];
            }

            // 网格 rect 互不重叠（平铺整张 sheet）——保持旧“邻帧主体不被夹入”的覆盖。
            for (int f = 0; f < 32; f++)
            {
                for (int g = f + 1; g < 32; g++)
                {
                    Rect a = rects[f], b = rects[g];
                    bool overlap = a.x < b.x + b.width && b.x < a.x + a.width
                        && a.y < b.y + b.height && b.y < a.y + a.height;
                    Check(!overlap, "grid rects " + f + " and " + g + " do not overlap (" + style.Name + ")");
                }
            }

            // 每行动作必须有接地帧（脚底触及 rig 地面线）；Run 还必须保留腾空帧（脚底高于地面线）。
            for (int a = 0; a < 4; a++)
            {
                int groundLine = a * cell[1] + anchor[1];
                int maxBottom = int.MinValue, minBottom = int.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    int bottom = measuredBottom[a * 8 + k];
                    if (bottom > maxBottom) maxBottom = bottom;
                    if (bottom < minBottom) minBottom = bottom;
                }
                Equal(groundLine, maxBottom, "action row grounds at the rig line, action " + a + ctx);
                if (a == (int)BankAssistantAtlasAction.Run)
                    Check(minBottom < groundLine, "Run keeps an airborne frame, style " + s);
            }
        }
    }

    private static void StandHeightFromPpu()
    {
        using var doc = LoadProductionMetadata();
        double fixedPpu = 32.0 / BankAssistantAtlasMetadata.TargetStandHeight;
        foreach (JsonElement js in doc.RootElement.GetProperty("styles").EnumerateArray())
        {
            int standingBody = js.GetProperty("standing_body_px").GetInt32();
            Equal(32, standingBody, "fixed rig standing body height, style " + js.GetProperty("name").GetString());
            double ppu = js.GetProperty("ppu").GetDouble();
            Near(BankAssistantAtlasMetadata.TargetStandHeight, standingBody / ppu, 1e-6,
                "stand height from the fixed rig body height, style " + js.GetProperty("name").GetString());
            Near(fixedPpu, ppu, 1e-9, "one fixed rig ppu for all styles");
        }
        foreach (BankAssistantAtlasStyle style in BankAssistantAtlasMetadata.Styles)
            Near(fixedPpu, style.PixelsPerUnit, 1e-3, "fixed rig ppu in the C# table (" + style.Name + ")");
        // 2026-09-28 live feedback retarget: native Greek banker Idle 20px / PPU32 x existing Greek-path mod Y 1.075.
        Near(20.0 / 32.0 * 1.075, BankAssistantAtlasMetadata.TargetStandHeight, 1e-6, "target stand height constant");
    }

    private static void SlotMappingAndRectLaws()
    {
        Check(!BankAssistantAtlasMetadata.TryGetStyle(3, out _), "slot 3 is a native style");
        Check(!BankAssistantAtlasMetadata.TryGetStyle(8, out _), "slot 8 is out of range");
        string[] expected = { "KingdomEnhancedMod.BankAssistantClerk.png",
            "KingdomEnhancedMod.BankAssistantCaravan.png", "KingdomEnhancedMod.BankAssistantSteward.png",
            "KingdomEnhancedMod.BankAssistantVaultkeeper.png" };
        int[][] expectedCells = { new[] { 32, 40 }, new[] { 40, 40 }, new[] { 40, 40 }, new[] { 40, 40 } };
        float[][] expectedStrides = { new[] { 16f, 24f }, new[] { 16f, 24f }, new[] { 16f, 16f }, new[] { 8f, 12f } };
        for (int slot = 4; slot <= 7; slot++)
        {
            Check(BankAssistantAtlasMetadata.TryGetStyle(slot, out BankAssistantAtlasStyle style), "slot maps");
            Equal(expected[slot - 4], style.ResourceName, "resource order");
            Equal(32, style.Rects.Length, "32 rects");
            Equal(8, BankAssistantAtlasMetadata.FramesPerAction, "8 frames per action");
            Equal((int)BankAssistantAtlasAction.Walk, (int)BankAssistantAtlasStyle.ActionOfFrame(9), "frame 9 = walk");
            Equal((int)BankAssistantAtlasAction.Leisure, (int)BankAssistantAtlasStyle.ActionOfFrame(31),
                "frame 31 = leisure");

            int cellW = style.SheetWidth / BankAssistantAtlasMetadata.Columns;
            int cellH = style.SheetHeight / BankAssistantAtlasMetadata.Rows;
            Equal(expectedCells[slot - 4][0], cellW, "cell width " + style.Name);
            Equal(expectedCells[slot - 4][1], cellH, "cell height " + style.Name);
            for (int f = 0; f < 32; f++)
            {
                Rect rect = style.Rects[f];
                int row = f / 8, column = f % 8;
                Equal(column * cellW, (int)rect.x, "grid rect x frame " + f + " " + style.Name);
                Equal((3 - row) * cellH, (int)rect.y, "grid rect y frame " + f + " " + style.Name);
                Equal(cellW, (int)rect.width, "grid rect width frame " + f + " " + style.Name);
                Equal(cellH, (int)rect.height, "grid rect height frame " + f + " " + style.Name);
                Check(rect.x >= 0f && rect.y >= 0f && rect.x + rect.width <= style.SheetWidth
                    && rect.y + rect.height <= style.SheetHeight, "rect inside the sheet frame " + f);
                Near(style.PivotXPixels[f] / rect.width, BankAssistantAtlasMetadata.PivotOf(style, f).x, 1e-6,
                    "pivot x normalization");
                Check(style.PivotXPixels[f] >= 0f && style.PivotXPixels[f] <= rect.width,
                    "pivot x inside its rect");
                Near(style.PivotXPixels[0], style.PivotXPixels[f], 0.0, "pivot x is fixed across frames");
                Near(style.PivotYPixels / rect.height, BankAssistantAtlasMetadata.PivotOf(style, f).y, 1e-6,
                    "pivot y normalization");
            }
            // 2026-09-29 验收素材的 authored 时长与整周期步幅（生产标定值，不是 140/240 候选）。
            Near(1.2, style.IdleSeconds, 1e-6, "idle authored duration");
            Near(0.8, style.WalkSeconds, 1e-6, "walk authored preview duration");
            Near(0.56, style.RunSeconds, 1e-6, "run authored preview duration");
            Near(2.4, style.LeisureSeconds, 1e-6, "leisure authored duration");
            Near(expectedStrides[slot - 4][0], style.WalkStridePixels, 1e-6, "walk stride " + style.Name);
            Near(expectedStrides[slot - 4][1], style.RunStridePixels, 1e-6, "run stride " + style.Name);
        }
    }

    private static void FrameMathCoversAndWraps()
    {
        foreach (BankAssistantAtlasStyle style in BankAssistantAtlasMetadata.Styles)
        {
            foreach (BankAssistantAtlasAction action in Enum.GetValues<BankAssistantAtlasAction>())
            {
                float duration = style.DurationOf(action);
                var seen = new HashSet<int>();
                int previous = int.MinValue;
                int wraps = 0;
                for (int step = 0; step < 800; step++)
                {
                    float phase = step * duration / 100f;
                    int frame = BankAssistantAtlasMetadata.FrameInAction(style, action, phase);
                    Check(frame >= (int)action * 8 && frame < (int)action * 8 + 8,
                        "frame stays inside its action row");
                    seen.Add(frame);
                    if (previous != int.MinValue && frame == (int)action * 8 && previous > frame) wraps++;
                    previous = frame;
                }
                Equal(8, seen.Count, "phase sweep must reach every authored frame (" + style.Name + "/" + action + ")");
                Check(wraps >= 2, "phase sweep must wrap at least twice (authored duration)");
                Check(BankAssistantAtlasMetadata.FrameInAction(style, action, duration) == (int)action * 8,
                    "exactly one authored duration wraps to the first frame");
                float step2 = duration / 8f;
                Equal((int)action * 8 + 3, BankAssistantAtlasMetadata.FrameInAction(style, action, step2 * 3.5f),
                    "frame 3 spans its authored slot");
                Equal(BankAssistantAtlasMetadata.FrameInAction(style, action, 1.234f),
                    BankAssistantAtlasMetadata.FrameInAction(style, action, 1.234f), "phase mapping is idempotent");
            }
        }
    }

    private static void FrameMathInvalidPhases()
    {
        BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[0];
        foreach (BankAssistantAtlasAction action in Enum.GetValues<BankAssistantAtlasAction>())
        {
            int first = (int)action * 8;
            Equal(first, BankAssistantAtlasMetadata.FrameInAction(style, action, 0f), "zero phase = first frame");
            Equal(first, BankAssistantAtlasMetadata.FrameInAction(style, action, -1f), "negative phase = first frame");
            Equal(first, BankAssistantAtlasMetadata.FrameInAction(style, action, float.NaN), "NaN = first frame");
            Equal(first, BankAssistantAtlasMetadata.FrameInAction(style, action, float.PositiveInfinity),
                "+Inf = first frame");
            int huge = BankAssistantAtlasMetadata.FrameInAction(style, action, 1e9f);
            Check(huge >= first && huge < first + 8, "huge phase stays inside the row");
        }
    }

    // ------------------------------------------------------------------ decode

    private static void DecodeBuildsFixedSprites()
    {
        Sprite[][] all = InstallAtlases();
        SpriteRenderer.ResetCounters();
        for (int s = 0; s < all.Length; s++)
        {
            Equal(32, all[s].Length, "32 sprites per style");
            BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[s];
            for (int f = 0; f < 32; f++)
            {
                Sprite sprite = all[s][f];
                Equal((int)style.Rects[f].x, (int)sprite.rect.x, "sprite rect x " + s + "/" + f);
                Equal((int)style.Rects[f].y, (int)sprite.rect.y, "sprite rect y " + s + "/" + f);
                Equal((int)style.Rects[f].width, (int)sprite.rect.width, "sprite rect w " + s + "/" + f);
                Equal((int)style.Rects[f].height, (int)sprite.rect.height, "sprite rect h " + s + "/" + f);
                Near(BankAssistantAtlasMetadata.PivotOf(style, f).x, sprite.pivot.x, 1e-6, "sprite pivot x");
                Near(BankAssistantAtlasMetadata.PivotOf(style, f).y, sprite.pivot.y, 1e-6, "sprite pivot y");
                Near(style.PixelsPerUnit, sprite.pixelsPerUnit, 1e-3, "sprite ppu");
                Check(ReferenceEquals(all[s][0].texture, sprite.texture), "one texture per style");
            }
            Texture2D texture = all[s][0].texture;
            Equal((int)FilterMode.Point, (int)texture.filterMode, "point filtering");
            Equal((int)TextureWrapMode.Clamp, (int)texture.wrapMode, "clamped wrap");
            Equal(0, texture.anisoLevel, "no aniso");
        }
    }

    private static void DecodeFailClosed()
    {
        var decode = typeof(BankAssistantAtlasVisuals).GetMethod("DecodeSheet", StaticFlags);
        BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[0];

        // 加载失败
        ImageConversion.Result = false;
        object[] args = BuildDecodeArgs(style);
        Check(!(bool)decode.Invoke(null, args), "LoadImage false must fail");
        Check(args[2] == null && args[3] == null, "no sprites/texture on load failure");
        Equal(1, Texture2D.DestroyedCount, "failed texture released");

        // 尺寸不符
        Texture2D.ResetCounters();
        ImageConversion.Result = true;
        ImageConversion.SimulatedWidth = 100;
        ImageConversion.SimulatedHeight = 100;
        Texture2D.FillAlpha = 255;
        args = BuildDecodeArgs(style);
        Check(!(bool)decode.Invoke(null, args), "wrong dimensions must fail");
        Equal(1, Texture2D.DestroyedCount, "dimension failure releases the texture");

        // 全透明（错图/空图）
        Texture2D.ResetCounters();
        Sprite.ResetCounters();
        ImageConversion.SimulatedWidth = style.SheetWidth;
        ImageConversion.SimulatedHeight = style.SheetHeight;
        Texture2D.FillAlpha = 0;
        args = BuildDecodeArgs(style);
        Check(!(bool)decode.Invoke(null, args), "fully transparent sheet must fail");
        Equal(1, Texture2D.DestroyedCount, "empty sheet releases the texture");
        Equal(0, Sprite.CreateCalls, "no sprites created for an empty sheet");
    }

    private static object[] BuildDecodeArgs(BankAssistantAtlasStyle style)
        => new object[] { style, Array.Empty<byte>(), null, null };

    private static void EnsureAssetsFailClosed()
    {
        // 测试程序集没有嵌入 PNG：预检必须 fail-closed，且可重复调用不抛异常。
        Check(!BankAssistantAtlasVisuals.EnsureAssets(), "missing resources must fail closed");
        Check(!BankAssistantAtlasVisuals.AssetsReady, "assets not ready after failure");
        Check(!BankAssistantAtlasVisuals.EnsureAssets(), "failure is sticky");
    }

    // ------------------------------------------------------------------ runtime tick

    private static void TickWritesNativeFrame()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.32f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[0];
        int expected = BankAssistantAtlasMetadata.FrameInAction(style, BankAssistantAtlasAction.Idle, 0.32f);
        Equal(2, expected, "0.32s into a 1.2s idle cycle = frame 2");
        Check(ReferenceEquals(sprites[0][expected], RendererOf(actor).sprite), "idle frame written");
        Equal(1, BankAssistantAtlasVisuals.LiveStateCount, "one live visual state");

        // 走 / 跑（2026-09-29 步态修订）：native 状态仍决定动作行，帧改由 gaitPhase 步频积分
        // 选择（首帧相位 0 + 本帧 advance = dt*speed/strideWorld，含生产的 mod-1 语义）。期望帧按
        // 生产 stride 数据逐案计算，不再引用 native normalizedTime。
        actor = MakeActor();
        SetNative(actor, "Walk", 0.25f, 1f, 0.8f);
        BankAssistantAtlasVisuals.Tick(actor, 5, false);
        BankAssistantAtlasStyle walkStyle = BankAssistantAtlasMetadata.Styles[1];
        int walkStep = (int)Math.Floor(0.1 * 0.8 / StrideWorld(walkStyle, BankAssistantAtlasAction.Walk) % 1.0 * 8);
        Check(ReferenceEquals(sprites[1][8 + walkStep], RendererOf(actor).sprite),
            "walk frame written for a walk state (gait phase, not native phase)");

        actor = MakeActor();
        SetNative(actor, "Run", 0.24f, 1f, 3.2f);
        BankAssistantAtlasVisuals.Tick(actor, 7, false);
        BankAssistantAtlasStyle runStyle = BankAssistantAtlasMetadata.Styles[3];
        int runStep = (int)Math.Floor(0.1 * 3.2 / StrideWorld(runStyle, BankAssistantAtlasAction.Run) % 1.0 * 8);
        Check(ReferenceEquals(sprites[3][16 + runStep], RendererOf(actor).sprite),
            "run frame written for a run state (gait phase, not native phase)");
    }

    private static void TickRepairsOverwriteAndIdempotent()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.2f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Sprite ours = RendererOf(actor).sprite;
        Check(ours != null, "tick wrote a sprite");

        int writes = SpriteRenderer.SpriteWrites;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes, SpriteRenderer.SpriteWrites, "same frame is idempotent (no redundant write)");
        Check(ReferenceEquals(ours, RendererOf(actor).sprite), "sprite unchanged on repeat");

        // 原生 Animator 本帧覆写回原生皮：LateUpdate 必须按真实当前 sprite 重新贴回
        Sprite native = new Sprite { name = "native_banker_frame" };
        RendererOf(actor).sprite = native;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(ReferenceEquals(ours, RendererOf(actor).sprite), "native overwrite repaired from the real sprite");

        // 换帧时只写一次
        SetNative(actor, "Idle", 0.5f, 1f, 0f);
        writes = SpriteRenderer.SpriteWrites;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes + 1, SpriteRenderer.SpriteWrites, "frame change writes exactly once");
        Check(!ReferenceEquals(ours, RendererOf(actor).sprite), "frame actually advanced");
        Check(sprites[0].Contains(RendererOf(actor).sprite), "new frame belongs to the style atlas");
    }

    private static void TickWritesNothingElse()
    {
        InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Walk", 0.4f, 1f, 0.8f);
        int createCalls = Sprite.CreateCalls;
        int addCalls = GameObject.AddComponentCalls;
        for (int i = 0; i < 120; i++)
        {
            SetNative(actor, "Walk", 0.4f + i * 0.01f, 1f, 0.8f);
            BankAssistantAtlasVisuals.Tick(actor, 4, false);
        }
        Equal(0, Behaviour.EnabledWrites, "enabled is never written");
        Equal(0, Transform.PositionWrites, "position is never written");
        Equal(0, Transform.ScaleWrites, "scale is never written");
        Equal(0, Animator.SetFloatCalls, "animator parameters are never written");
        Equal(0, Animator.PlayCalls, "animator is never driven");
        Equal(createCalls, Sprite.CreateCalls, "no sprite created per frame");
        Equal(addCalls, GameObject.AddComponentCalls, "no component created per frame");
    }

    private static void FreezeGates()
    {
        InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Walk", 0.25f, 1f, 0.8f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        int writes = SpriteRenderer.SpriteWrites;
        SetNative(actor, "Walk", 0.5f, 1f, 0.8f);   // 原生本可推进到下一帧

        Time.timeScale = 0f;
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        Equal(writes, SpriteRenderer.SpriteWrites, "pause freezes sprite writes");
        Time.timeScale = 1f;

        IslandSaveData.isSavingGame = true;
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        Equal(writes, SpriteRenderer.SpriteWrites, "save freezes sprite writes");
        IslandSaveData.isSavingGame = false;

        Managers.Inst.game.state = Game.State.Menu;
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        Equal(writes, SpriteRenderer.SpriteWrites, "menu state freezes sprite writes");
        Managers.Inst.game.state = Game.State.Playing;

        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes + 1, SpriteRenderer.SpriteWrites, "resume writes again");
        Equal(1, BankAssistantAtlasVisuals.LiveStateCount, "state preserved across freeze (same world)");
    }

    private static void ScopeGateClearsState()
    {
        InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.2f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(1, BankAssistantAtlasVisuals.LiveStateCount, "state created inside the Greek layer");
        int writes = SpriteRenderer.SpriteWrites;

        GreekBankScope.LayerOk = false;
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        Equal(writes, SpriteRenderer.SpriteWrites, "out-of-layer actor receives no write");
        Equal(0, BankAssistantAtlasVisuals.LiveStateCount, "out-of-layer state cleared (no leisure carry-over)");

        GreekBankScope.LayerOk = true;
        GreekBankScope.IsActive = false;
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        Equal(0, BankAssistantAtlasVisuals.LiveStateCount, "inactive Greek scope cleared");
        Equal(writes, SpriteRenderer.SpriteWrites, "inactive Greek scope receives no write");
    }

    private static void TickIgnoresForeignSlots()
    {
        InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.1f, 1f, 0f);
        foreach (int slot in new[] { -1, 0, 3, 8, 99 })
        {
            BankAssistantAtlasVisuals.Tick(actor, slot, true);
        }
        Equal(0, SpriteRenderer.SpriteWrites, "native slots 0..3 and out-of-range slots untouched");
        Equal(0, BankAssistantAtlasVisuals.LiveStateCount, "no state kept for foreign slots");
    }

    private static void TickIgnoresInactive()
    {
        InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.1f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(1, BankAssistantAtlasVisuals.LiveStateCount, "active actor registered");
        actor.SetActive(false);
        int writes = SpriteRenderer.SpriteWrites;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes, SpriteRenderer.SpriteWrites, "inactive/template actor receives no write");
        Equal(0, BankAssistantAtlasVisuals.LiveStateCount, "inactive actor state cleared");
    }

    private static void TickResolvesWalkRun()
    {
        Sprite[][] sprites = InstallAtlases();
        // 实际 banker controller：Walk 就是 Walk（单 clip，不是混树），Speed 不参与分类；
        // 2026-09-29 步态修订：行仍由 native 状态决定，行内帧由 gaitPhase（速度→周期）决定。
        GameObject actor = MakeActor();
        SetNative(actor, "Walk", 0.25f, 1f, 3.2f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        BankAssistantAtlasStyle clerk = BankAssistantAtlasMetadata.Styles[0];
        int fastWalk = (int)Math.Floor(0.1 * 3.2 / StrideWorld(clerk, BankAssistantAtlasAction.Walk) * 8);
        Check(sprites[0][8 + fastWalk] == RendererOf(actor).sprite,
            "walk state stays on the walk strip even at run speed");

        actor = MakeActor();
        SetNative(actor, "Run", 0.3f, 0.75f, 0.8f);
        BankAssistantAtlasVisuals.Tick(actor, 5, false);
        BankAssistantAtlasStyle caravan = BankAssistantAtlasMetadata.Styles[1];
        int slowRun = (int)Math.Floor(0.1 * 0.8 / StrideWorld(caravan, BankAssistantAtlasAction.Run) * 8);
        Check(sprites[1][16 + slowRun] == RendererOf(actor).sprite,
            "run state stays on the run strip even at low speed");

        // 表外 state 不再按 Speed 猜测：不写 sprite，也不授予闲暇
        actor = MakeActor();
        SetNative(actor, "LocomotionBlend", 0.3f, 1f, 3.2f);
        int writes = SpriteRenderer.SpriteWrites;
        BankAssistantAtlasVisuals.Tick(actor, 6, true);
        Equal(writes, SpriteRenderer.SpriteWrites, "unknown state must not be guessed from Speed");
    }

    private static void TickRejectsBadSamples()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.32f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Sprite ours = RendererOf(actor).sprite;
        Check(ReferenceEquals(sprites[0][2], ours), "baseline frame written");
        int writes = SpriteRenderer.SpriteWrites;

        // 表外 state（例如 Ghost Die）：取消闲暇、不提交 sprite，不保留旧帧冒充有效采样
        var animator = AnimatorOf(actor);
        animator.State = new AnimatorStateInfo
        {
            shortNameHash = Animator.StringToHash("Ghost Die"),
            normalizedTime = 0.7f,
            length = 1f,
        };
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        Equal(writes, SpriteRenderer.SpriteWrites, "unknown state writes nothing");
        Check(ReferenceEquals(ours, RendererOf(actor).sprite), "existing sprite untouched (no guessing)");

        // length<=0 / NaN normalizedTime / 负相位：同样不写
        SetNative(actor, "Run", 0.4f, 0f, 3.2f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes, SpriteRenderer.SpriteWrites, "zero clip length writes nothing");
        var info = animator.State;
        info.length = 1f;
        info.normalizedTime = float.NaN;
        animator.State = info;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes, SpriteRenderer.SpriteWrites, "NaN normalizedTime writes nothing");
        info.normalizedTime = -0.5f;
        animator.State = info;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes, SpriteRenderer.SpriteWrites, "negative phase writes nothing");
        info.normalizedTime = float.PositiveInfinity;
        animator.State = info;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Equal(writes, SpriteRenderer.SpriteWrites, "infinite normalizedTime writes nothing");

        // 缺 Animator：不写
        actor = MakeActor();
        var animatorComponent = AnimatorOf(actor);
        actor.Components.Remove(animatorComponent);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(RendererOf(actor).sprite == null, "missing animator writes nothing");
    }

    private static void LeisurePermissionAndPreemption()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0f, 1f, 0f);
        Time.deltaTime = 0.1f;

        // 许可 + 站立：等待冷却后进入闲暇第四行
        bool sawLeisure = false;
        for (int i = 0; i < 300 && !sawLeisure; i++)
        {
            BankAssistantAtlasVisuals.Tick(actor, 4, true);
            Sprite sprite = RendererOf(actor).sprite;
            int index = Array.IndexOf(sprites[0], sprite);
            if (index >= 24) sawLeisure = true;
            else Check(index >= 0 && index < 8, "non-leisure idle frames stay on the idle strip");
        }
        Check(sawLeisure, "leisure plays after the stable-stand clock permits it");

        // 许可撤销：立即取消闲暇，回到原生相位（这里相位 0 → idle 首帧）
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(ReferenceEquals(sprites[0][0], RendererOf(actor).sprite), "permission withdrawn cancels leisure now");

        // 移动/工作抢占：即便许可为真，行走状态也不播闲暇
        SetNative(actor, "Walk", 0.2f, 1f, 0.8f);
        BankAssistantAtlasVisuals.Tick(actor, 4, true);
        int walkIndex = Array.IndexOf(sprites[0], RendererOf(actor).sprite);
        Check(walkIndex >= 8 && walkIndex < 16, "work/movement preempts leisure");
    }

    private static void ForgetAndStaleIdentity()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        SetNative(actor, "Run", 0.3f, 1f, 3.2f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(Array.IndexOf(sprites[0], RendererOf(actor).sprite) >= 16, "tracked actor is on the run strip");

        BankAssistantAtlasVisuals.Forget(actor);
        Equal(0, BankAssistantAtlasVisuals.LiveStateCount, "forget drops the life state");

        // 新 life：不复用旧帧/旧闲暇；有效 Idle 相位直接按 authored 映射提交
        SetNative(actor, "Idle", 0.32f, 1f, 0f);
        RendererOf(actor).sprite = null;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(ReferenceEquals(sprites[0][2], RendererOf(actor).sprite),
            "fresh life maps the native phase directly (no stale frame)");

        // 同一 instanceId 换对象（池/重建）：不得继承旧帧状态
        GameObject first = MakeActor(forcedInstanceId: 4242, forcedPointer: new IntPtr(4242));
        SetNative(first, "Run", 0.3f, 1f, 3.2f);
        BankAssistantAtlasVisuals.Tick(first, 4, false);
        Check(Array.IndexOf(sprites[0], RendererOf(first).sprite) >= 16, "first actor on run strip");

        GameObject second = MakeActor(forcedInstanceId: 4242, forcedPointer: new IntPtr(9999));
        SetNative(second, "Idle", 0.32f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(second, 4, false);
        Equal(9999, StatePointer(4242).ToInt32(), "same instance id with a new identity rebinds the state");
        Check(ReferenceEquals(sprites[0][2], RendererOf(second).sprite), "new identity renders its own frame");

        // 旧 life 的滞后 Forget（pointer 不同）不得清新 life 状态
        GameObject third = MakeActor(forcedInstanceId: 5151, forcedPointer: new IntPtr(5151));
        SetNative(third, "Idle", 0.2f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(third, 4, false);
        int tracked = BankAssistantAtlasVisuals.LiveStateCount;
        var stale = new GameObject("stale") { Pointer = new IntPtr(7777), InstanceIdOverride = 5151 };
        BankAssistantAtlasVisuals.Forget(stale);
        Equal(tracked, BankAssistantAtlasVisuals.LiveStateCount, "stale-pointer forget must not clear the new life");
    }

    // ------------------------------------------------------------------ native atlas lifetime

    /// <summary>
    /// 首用前卸载的契约检验：预检/解码成功之后、任何 actor 首次提交之前发生一次 unused-asset
    /// 卸载（原场景曾报告帧不可用；确切卸载者尚未验证）。模块自有缓存必须靠显式保留位让
    /// 4 纹理 / 128 sprite 全部原生存活，首次 Tick 才能提交图集帧而不是 held-back。
    /// </summary>
    private static void SweepBeforeFirstUseKeepsAtlas()
    {
        Sprite[][] sprites = InstallAtlases();
        int swept = UnityEngine.Resources.UnloadUnusedAssets();
        Equal(0, swept, "a flag-honoring sweep must collect none of the retained atlas objects");
        Equal(0, Texture2D.DestroyedCount, "the four atlas textures stay native-alive");
        Equal(0, Sprite.DestroyedCount, "all 128 atlas sprites stay native-alive");

        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.32f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(ReferenceEquals(sprites[0][2], RendererOf(actor).sprite),
            "first use after the sweep commits the decoded atlas frame");
        Check(!ManualLogSource.Warnings.Any(w => w.Contains("frame unavailable")),
            "no frame-unavailable diagnostic after a sweep that honored the retention flags");
    }

    /// <summary>闲/走/跑/闲暇切换之间穿插回收：缓存绝不因回收失去任何一帧。</summary>
    private static void SweepMidLifeKeepsAtlas()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        var probes = new[]
        {
            // Walk/Run 探针用 0 速（gaitPhase 不前进，行首帧确定），Idle 仍走 native 相位；
            // 2026-09-29 步态修订后 Walk/Run 帧不再映射 native 相位。
            new { State = "Idle", Slot = 4, Expected = 2 },
            new { State = "Walk", Slot = 5, Expected = 8 },
            new { State = "Run", Slot = 6, Expected = 16 },
        };
        foreach (var probe in probes)
        {
            Equal(0, UnityEngine.Resources.UnloadUnusedAssets(),
                "sweep before switching to " + probe.State + " collects nothing");
            SetNative(actor, probe.State, probe.State == "Idle" ? 0.32f : 0.25f, 1f, 0f);
            BankAssistantAtlasVisuals.Tick(actor, probe.Slot, false);
            Check(ReferenceEquals(sprites[probe.Slot - 4][probe.Expected], RendererOf(actor).sprite),
                probe.State + " still commits its atlas frame after a mid-life sweep");
        }

        // 行走推进中反复回收：帧仍持续来自图集（不因回收回退到原生皮/丢帧）
        for (int i = 0; i < 24; i++)
        {
            Equal(0, UnityEngine.Resources.UnloadUnusedAssets(), "walking sweep " + i + " collects nothing");
            SetNative(actor, "Walk", 0.5f, 1f, 1.6f);
            Time.deltaTime = 0.05f;
            BankAssistantAtlasVisuals.Tick(actor, 5, false);
            Check(sprites[1].Contains(RendererOf(actor).sprite),
                "walking frame " + i + " still belongs to the atlas after a sweep");
        }

        // 闲暇要跨数十帧推进，每次提交之间都回收一次也不得掉帧
        actor = MakeActor();
        SetNative(actor, "Idle", 0f, 1f, 0f);
        Time.deltaTime = 0.1f;
        bool sawLeisure = false;
        for (int i = 0; i < 400 && !sawLeisure; i++)
        {
            Equal(0, UnityEngine.Resources.UnloadUnusedAssets(), "leisure sweep " + i + " collects nothing");
            BankAssistantAtlasVisuals.Tick(actor, 4, true);
            int index = Array.IndexOf(sprites[0], RendererOf(actor).sprite);
            if (index >= 24) sawLeisure = true;
        }
        Check(sawLeisure, "leisure frames still commit under repeated sweeps");
        Equal(0, Texture2D.DestroyedCount, "no retained texture lost to the sweeps");
        Equal(0, Sprite.DestroyedCount, "no retained sprite lost to the sweeps");
    }

    /// <summary>部分失败（Sprite.Create 中途抛异常）时，已创建对象即使带保留位也必须被显式销毁。</summary>
    private static void PartialFailureReleasesRetainedObjects()
    {
        var decode = typeof(BankAssistantAtlasVisuals).GetMethod("DecodeSheet", StaticFlags);
        BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[0];
        ImageConversion.Result = true;
        Texture2D.FillAlpha = 255;
        ImageConversion.SimulatedWidth = style.SheetWidth;
        ImageConversion.SimulatedHeight = style.SheetHeight;
        Sprite.ThrowAtCreateIndex = 5;   // 前 4 个 sprite 创建成功，第 5 个注入失败
        object[] args = BuildDecodeArgs(style);
        Check(!(bool)decode.Invoke(null, args), "the injected Sprite.Create failure must fail the decode");
        Check(args[2] == null && args[3] == null, "no partial sprite/texture arrays escape");
        Equal(1, Texture2D.DestroyedCount, "the retained texture is explicitly destroyed on failure");
        Equal(4, Sprite.DestroyedCount, "every created sprite is explicitly destroyed even though retained");
        Equal(0, Texture2D.Created.Count(t => t != null), "no native-alive texture leaks from the failed decode");
        Equal(0, Sprite.Created.Count(s => s != null), "no native-alive sprite leaks from the failed decode");
    }

    /// <summary>frame-unavailable 必须点名原因（原生已销毁 / 托管表项缺失 / 坏索引）与 slot/frame/style。</summary>
    private static void FrameDiagnosticsNameContext()
    {
        Sprite[][] sprites = InstallAtlases();
        BankAssistantAtlasStyle style = BankAssistantAtlasMetadata.Styles[0];
        GameObject actor = MakeActor();
        SetNative(actor, "Idle", 0.32f, 1f, 0f);
        int frame = BankAssistantAtlasMetadata.FrameInAction(style, BankAssistantAtlasAction.Idle, 0.32f);
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Check(ReferenceEquals(sprites[0][frame], RendererOf(actor).sprite), "baseline frame committed");

        // 原生被销毁、代理仍在：Unity 运算符判 null，诊断必须区分出 native-destroyed
        SetNative(actor, "Idle", 0.6f, 1f, 0f);
        int nextFrame = BankAssistantAtlasMetadata.FrameInAction(style, BankAssistantAtlasAction.Idle, 0.6f);
        Check(nextFrame != frame, "probe must land on a different frame");
        UnityEngine.Object.Destroy(sprites[0][nextFrame]);
        ManualLogSource.Reset();
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        string warn = ManualLogSource.Warnings.LastOrDefault(w => w.Contains("frame unavailable"));
        Check(warn != null, "a destroyed frame must be reported");
        Check(warn.Contains("native-destroyed"), "cause must be named: " + warn);
        Check(warn.Contains("slot=4"), "slot must be named: " + warn);
        Check(warn.Contains("frame=" + nextFrame), "frame must be named: " + warn);
        Check(warn.Contains(style.Name), "style must be named: " + warn);

        // 托管表项真为 null（另一类原因）。2026-09-29 步态修订：Walk 帧来自 gaitPhase，
        // 直接布置相位 0.30（floor(0.30*8)=2 → 帧 10），0 速确保不再前进。
        ManualLogSource.Reset();
        sprites[1][10] = null;
        GameObject walker = MakeActor();
        SetNative(walker, "Walk", 0.25f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(walker, 5, false);   // 建立本 life 状态
        SetGaitPhase(walker, 0.30);
        BankAssistantAtlasVisuals.Tick(walker, 5, false);
        warn = ManualLogSource.Warnings.LastOrDefault(w => w.Contains("frame unavailable"));
        Check(warn != null && warn.Contains("managed-null"), "missing managed entry must be named: " + warn);
        Check(warn.Contains("slot=5") && warn.Contains("frame=10"), "context present: " + warn);

        // 坏索引（防御分支）：截断缓存后同一帧必须报 out of range，而不是笼统 unavailable
        ManualLogSource.Reset();
        SetStatic(typeof(BankAssistantAtlasVisuals), "_sprites",
            new[] { new Sprite[0], new Sprite[0], new Sprite[0], new Sprite[0] });
        GameObject bad = MakeActor();
        SetNative(bad, "Idle", 0.32f, 1f, 0f);
        BankAssistantAtlasVisuals.Tick(bad, 4, false);
        warn = ManualLogSource.Warnings.LastOrDefault(w => w.Contains("out of range"));
        Check(warn != null, "a truncated cache must be reported as an out-of-range index");
        Check(warn.Contains("slot=4") && warn.Contains("frame=" + frame) && warn.Contains(style.Name),
            "bad index context: " + warn);
    }

    /// <summary>首用回执必须每 style 恰一行（max 4/进程），后续帧与 actor 不重复刷。</summary>
    private static void FirstApplyEvidenceBoundedPerStyle()
    {
        InstallAtlases();
        var slots = new List<int>();
        for (int slot = 4; slot <= 7; slot++)
        {
            GameObject actor = MakeActor();
            SetNative(actor, "Idle", 0.32f, 1f, 0f);
            BankAssistantAtlasVisuals.Tick(actor, slot, false);
            Equal(1, SpriteRenderer.SpriteWrites - (slot - 4), "each style commits on first use");
            slots.Add(slot);
        }
        Equal(4, ManualLogSource.Infos.Count(i => i.Contains("first atlas apply")),
            "one first-apply line per new style");
        for (int i = 0; i < slots.Count; i++)
        {
            GameObject actor = MakeActor();
            SetNative(actor, "Walk", 0.3f, 1f, 0f);
            BankAssistantAtlasVisuals.Tick(actor, slots[i], false);
            SetNative(actor, "Run", 0.3f, 1f, 0f);
            BankAssistantAtlasVisuals.Tick(actor, slots[i], false);
        }
        Equal(4, ManualLogSource.Infos.Count(i => i.Contains("first atlas apply")),
            "first-apply evidence stays bounded after further ticks and actors");
    }

    // ------------------------------------------------------------------ gait cadence clock
    // 外部不变量优先：周期=步幅/速度、半周期对侧帧、同距离同相位、速度减半相位减半、
    // 帧边界 1/8 步进；不把实现公式当唯一期望。

    private static void GaitCyclePeriodScalesWithSpeed()
    {
        Sprite[][] sprites = InstallAtlases();
        BankAssistantAtlasStyle clerk = BankAssistantAtlasMetadata.Styles[0];
        float dt = 0.05f;
        foreach (BankAssistantAtlasAction action in new[] { BankAssistantAtlasAction.Walk, BankAssistantAtlasAction.Run })
        foreach (float speed in new[] { 0.8f, 1.6f, 3.2f })
        {
            double stride = StrideWorld(clerk, action);
            GameObject whole = MakeActor();
            TickGait(whole, 4, action.ToString(), speed, dt, (int)Math.Round(stride / speed / dt));
            double oneTick = dt * speed / stride;
            double m = GaitPhaseOf(whole) % 1.0;
            double distance = Math.Min(m, 1.0 - m);   // 周期不是整数 tick 数：按 mod-1 距离判定
            Check(distance <= oneTick + 1e-12,
                "full cycle returns phase to start (" + action + "@" + speed + "): " + m);
            // 半周期：相位过半（对侧支撑脚），帧落在 3..4（0.5 边界 ± 一个 tick）
            GameObject half = MakeActor();
            TickGait(half, 4, action.ToString(), speed, dt, (int)Math.Round(stride / speed / dt / 2));
            Near(0.5, GaitPhaseOf(half), oneTick + 1e-12, "half cycle reaches opposite foot (" + action + "@" + speed + ")");
            int halfFrame = Array.IndexOf(sprites[0], RendererOf(half).sprite);
            int first = (int)action * 8;
            // 半周期 ≈ 帧 3..4（对侧支撑脚）。tick 量化取整把显示帧最多挪 ±1
            // （2026-09-29 验收步幅下 run@3.2 每 tick ≈2.5 帧，round-to-nearest tick 落在帧 5）。
            Check(Math.Abs(halfFrame - (first + 4)) <= 1,
                "half cycle lands near the opposite foot frame, got " + halfFrame + " (" + action + "@" + speed + ")");
        }

        // 速度减半、时间不变 → 相位减半（同距离/同相位由 fps 独立性测试覆盖）。
        // 3 个 tick：验收步幅（clerk walk 16px）下两者都不足一整周期，可直接比较而不受 mod-1 回绕影响。
        GameObject fast = MakeActor();
        TickGait(fast, 4, "Walk", 1.6f, dt, 3);
        GameObject slow = MakeActor();
        TickGait(slow, 4, "Walk", 0.8f, dt, 3);
        Near(GaitPhaseOf(slow), GaitPhaseOf(fast) / 2.0, 1e-9, "halved speed halves the phase over the same time");
    }

    private static void GaitFpsIndependence()
    {
        InstallAtlases();
        GameObject sixty = MakeActor();
        TickGait(sixty, 4, "Walk", 1.6f, 1f / 60f, 120);   // 2.0s @60fps
        GameObject thirty = MakeActor();
        TickGait(thirty, 4, "Walk", 1.6f, 1f / 30f, 60);    // 2.0s @30fps
        GameObject fifteen = MakeActor();
        TickGait(fifteen, 4, "Walk", 1.6f, 1f / 15f, 30);   // 2.0s @15fps
        Near(GaitPhaseOf(sixty), GaitPhaseOf(thirty), 1e-8, "60fps and 30fps agree");
        Near(GaitPhaseOf(sixty), GaitPhaseOf(fifteen), 1e-8, "60fps and 15fps agree");
    }

    private static void GaitContinuityAcrossChanges()
    {
        Sprite[][] sprites = InstallAtlases();
        BankAssistantAtlasStyle clerk = BankAssistantAtlasMetadata.Styles[0];

        // Walk→Run：同一相位变量换行，不重置（0 速冻结在切换点读帧）
        GameObject actor = MakeActor();
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 3);
        double phase = GaitPhaseOf(actor);
        Check(phase > 0.0, "phase advanced before the switch");
        SetNative(actor, "Run", 0.5f, 1f, 0f);
        Time.deltaTime = 0.1f;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Near(phase, GaitPhaseOf(actor), 0.0, "Walk->Run keeps the same phase value");
        int runFrame = (int)Math.Floor(phase * 8);
        Check(ReferenceEquals(sprites[0][16 + runFrame], RendererOf(actor).sprite),
            "run row renders the SAME phase (no reset)");

        // Idle 插曲：冻结不归零
        SetNative(actor, "Idle", 0.3f, 1f, 0f);
        for (int i = 0; i < 5; i++) BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Near(phase, GaitPhaseOf(actor), 0.0, "idle freezes the phase without resetting it");

        // 速度跳变：相位只按物理上界前进（≤ dt×3.2/stride），不回零、不跳变。
        // dt=0.05 且只走 1 个前置 tick：验收步幅下一次 3.2 速 tick ≈0.48 周期，构造无回绕窗口。
        GameObject walker = MakeActor();
        TickGait(walker, 4, "Walk", 0.8f, 0.05f, 1);
        double before = GaitPhaseOf(walker);
        TickGait(walker, 4, "Walk", 3.2f, 0.05f, 1);
        double after = GaitPhaseOf(walker);
        double bound = 0.05 * 3.2 / StrideWorld(clerk, BankAssistantAtlasAction.Walk);
        Check(after > before && after - before <= bound + 1e-6,
            "speed change advances the shared phase within the physical bound");
    }

    private static void GaitAuthorityClientUnitNormalization()
    {
        InstallAtlases();
        GameObject host = MakeActor();
        Time.timeScale = 1f;
        TickGait(host, 4, "Walk", 1.6f, 0.1f, 5);           // authority Speed 直接是 game 秒速度
        double expected = GaitPhaseOf(host);

        // 客户端：PositionSync 补间写入 |dx|/unscaledElapsed = v_game × timeScale。
        // 单位契约：raw/timeScale 归一为 game 秒速度——同一 game-dt 下每个 tick 的推进
        // 与 authority 完全一致（ts 改变的是 raw 读数与真实帧时长，不是 game-dt 语义）。
        foreach (float ts in new[] { 0.5f, 1f, 2f })
        {
            NetworkBigBoss.HasWorldAuth = false;
            Time.timeScale = ts;
            GameObject client = MakeActor();
            TickGait(client, 4, "Walk", 1.6f * ts, 0.1f, 5);
            Near(expected, GaitPhaseOf(client), 1e-12, "client normalizes observed speed to game seconds (ts=" + ts + ")");
        }
        Time.timeScale = 1f;

        // 大跳变（传送残差）：展示上限 3.2 封顶，位移不塞进 phase
        NetworkBigBoss.HasWorldAuth = false;
        GameObject jumper = MakeActor();
        TickGait(jumper, 4, "Walk", 100f, 0.1f, 1);
        BankAssistantAtlasStyle clerk = BankAssistantAtlasMetadata.Styles[0];
        double bound = 0.1 * 3.2 / StrideWorld(clerk, BankAssistantAtlasAction.Walk);
        Check(GaitPhaseOf(jumper) > 0.1 * 3.1 / StrideWorld(clerk, BankAssistantAtlasAction.Walk) - 1e-12
            && GaitPhaseOf(jumper) <= bound + 1e-6,
            "client jump speed is capped at the max design step speed");
        NetworkBigBoss.HasWorldAuth = true;
    }

    private static void GaitStrideScalesWithLossyScaleX()
    {
        InstallAtlases();
        // 2 个 tick（step≈0.238/次，验收步幅）：基线 <0.5，×0.5 缩放探针 <1 不回绕。
        GameObject plain = MakeActor();
        TickGait(plain, 4, "Walk", 0.8f, 0.1f, 2);
        double basePhase = GaitPhaseOf(plain);
        Check(basePhase > 0.0 && basePhase < 0.5, "baseline phase in a safe range");

        GameObject big = MakeActor();
        big.transform.localScale = new Vector3(2f, 1f, 1f);
        TickGait(big, 4, "Walk", 0.8f, 0.1f, 2);
        Near(basePhase / 2.0, GaitPhaseOf(big), 1e-12, "double scale = double stride = half advance");

        GameObject small = MakeActor();
        small.transform.localScale = new Vector3(0.5f, 1f, 1f);
        TickGait(small, 4, "Walk", 0.8f, 0.1f, 2);
        Near(basePhase * 2.0, GaitPhaseOf(small), 1e-12, "half scale = half stride = double advance");

        GameObject flip = MakeActor();
        flip.transform.localScale = new Vector3(-1f, 1f, 1f);
        TickGait(flip, 4, "Walk", 0.8f, 0.1f, 2);
        Near(basePhase, GaitPhaseOf(flip), 1e-12, "mirrored scale keeps the same stride (abs)");

        // 父子合成：parent ×2 与 local ×0.5 抵消，等效 1
        GameObject composed = MakeActor();
        var parentObject = new GameObject("parent");
        parentObject.transform.localScale = new Vector3(2f, 1f, 1f);
        composed.transform.SetParent(parentObject.transform, false);
        composed.transform.localScale = new Vector3(0.5f, 1f, 1f);
        TickGait(composed, 4, "Walk", 0.8f, 0.1f, 2);
        Near(basePhase, GaitPhaseOf(composed), 1e-12, "composed lossy scale is what the stride uses");
    }

    private static void GaitInvalidInputsFreezeOrHoldBack()
    {
        Sprite[][] sprites = InstallAtlases();

        // dt=0：相位不动，帧照常提交（幂等）
        GameObject actor = MakeActor();
        TickGait(actor, 4, "Walk", 0.8f, 0f, 2);
        Near(0.0, GaitPhaseOf(actor), 0.0, "zero dt does not advance");
        Check(ReferenceEquals(sprites[0][8], RendererOf(actor).sprite), "zero dt still commits the phase frame");

        // 0 速：不推进（Walk 态站定仍显示当前步态帧）
        actor = MakeActor();
        TickGait(actor, 4, "Walk", 0f, 0.1f, 3);
        Near(0.0, GaitPhaseOf(actor), 0.0, "zero speed does not advance");

        // 非有限/负速度：冻结相位但仍提交当前帧
        actor = MakeActor();
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 1);
        double phase = GaitPhaseOf(actor);
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.8f })
        {
            TickGait(actor, 4, "Walk", bad, 0.1f, 1);
            Near(phase, GaitPhaseOf(actor), 0.0, "invalid speed freezes the phase (" + bad + ")");
        }
        Check(sprites[0].Contains(RendererOf(actor).sprite), "invalid speed still commits the frozen frame");

        // 负 dt：不推进
        TickGait(actor, 4, "Walk", 0.8f, -0.1f, 1);
        Near(phase, GaitPhaseOf(actor), 0.0, "negative dt does not advance");

        // 客户端 timeScale 非有限：无法归一 → 不推进
        NetworkBigBoss.HasWorldAuth = false;
        Time.timeScale = float.NaN;
        TickGait(actor, 4, "Walk", 1.6f, 0.1f, 1);
        Near(phase, GaitPhaseOf(actor), 0.0, "client non-finite timeScale cannot normalize speed");
        Time.timeScale = 1f;
        NetworkBigBoss.HasWorldAuth = true;

        // stride/scale 异常：本帧不提交、相位不动、诊断点名
        ManualLogSource.Reset();
        int writes = SpriteRenderer.SpriteWrites;
        actor.transform.localScale = new Vector3(0f, 1f, 1f);
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 1);
        Near(phase, GaitPhaseOf(actor), 0.0, "zero scale does not advance");
        Equal(writes, SpriteRenderer.SpriteWrites, "zero scale holds the sprite back");
        Check(ManualLogSource.Warnings.Any(w => w.Contains("gait stride/scale invalid")),
            "bad stride/scale must be diagnosed");
        actor.transform.localScale = new Vector3(float.NaN, 1f, 1f);
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 1);
        Near(phase, GaitPhaseOf(actor), 0.0, "non-finite scale does not advance");
        Equal(writes, SpriteRenderer.SpriteWrites, "non-finite scale holds the sprite back");

        // 传送等待门：冻结相位，帧仍提交
        actor = MakeActor();
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 2);
        double waiting = GaitPhaseOf(actor);
        BankAssistantTeleportVisuals.Waiting[4] = true;
        TickGait(actor, 4, "Walk", 1.6f, 0.1f, 3);
        Near(waiting, GaitPhaseOf(actor), 0.0, "teleport waiting freezes the phase");
        Check(sprites[0].Contains(RendererOf(actor).sprite), "waiting still commits the frozen frame");
        BankAssistantTeleportVisuals.Waiting[4] = false;
    }

    private static void GaitHugeDeltaPreservesPhaseFraction()
    {
        InstallAtlases();
        // 起点相位不同、同一巨大合法 dt：结果差 = 起点差（mod 1）——大 step 不吞小数
        GameObject zero = MakeActor();
        GameObject quarter = MakeActor();
        TickGait(zero, 4, "Walk", 3.2f, 0f, 1);       // 建立状态，0 速不动
        TickGait(quarter, 4, "Walk", 3.2f, 0f, 1);
        SetGaitPhase(quarter, 0.25);
        TickGait(zero, 4, "Walk", 3.2f, 600f, 1);     // 600s×3.2 速度 → 数千周期
        TickGait(quarter, 4, "Walk", 3.2f, 600f, 1);
        double p0 = GaitPhaseOf(zero);
        double p25 = GaitPhaseOf(quarter);
        Check(p0 >= 0.0 && p0 < 1.0 && p25 >= 0.0 && p25 < 1.0, "phase stays in [0,1) after huge deltas");
        Near((0.25 + p0) % 1.0, p25, 1e-9, "huge delta keeps the pre-existing phase fraction");
    }

    private static void GaitPhaseResetsOnNewLife()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 3);
        Check(GaitPhaseOf(actor) > 0.0, "phase advanced during the first life");

        // Forget → 新 life 相位从 0 开始
        BankAssistantAtlasVisuals.Forget(actor);
        TickGait(actor, 4, "Walk", 0f, 0.1f, 1);
        Near(0.0, GaitPhaseOf(actor), 0.0, "fresh life restarts the phase at zero");
        Check(ReferenceEquals(sprites[0][8], RendererOf(actor).sprite), "fresh life renders the first gait frame");

        // 换层（world changed）同样清状态：回来后相位归零
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 2);
        GreekBankScope.LayerOk = false;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        GreekBankScope.LayerOk = true;
        TickGait(actor, 4, "Walk", 0f, 0.1f, 1);
        Near(0.0, GaitPhaseOf(actor), 0.0, "leaving the layer drops the gait phase");

        // 池复用（同 instanceId 新对象）：同样不继承
        GameObject first = MakeActor(forcedInstanceId: 6464, forcedPointer: new IntPtr(6464));
        TickGait(first, 4, "Walk", 0.8f, 0.1f, 3);
        Check(GaitPhaseOf(first) > 0.0, "reused slot advanced in its first life");
        GameObject second = MakeActor(forcedInstanceId: 6464, forcedPointer: new IntPtr(777));
        TickGait(second, 4, "Walk", 0f, 0.1f, 1);
        Near(0.0, GaitPhaseOf(second), 0.0, "identity reuse starts a fresh phase");
    }

    private static void GaitRepairsNativeOverwriteWhileWalking()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        TickGait(actor, 4, "Walk", 1.6f, 0.1f, 2);
        int committed = Array.IndexOf(sprites[0], RendererOf(actor).sprite);
        Check(committed >= 8 && committed < 16, "walking commits a walk-row frame");

        Sprite native = new Sprite { name = "native_banker_walk" };
        RendererOf(actor).sprite = native;
        TickGait(actor, 4, "Walk", 1.6f, 0.1f, 1);
        Check(sprites[0].Contains(RendererOf(actor).sprite),
            "native overwrite is repaired while the gait advances");
        Check(Array.IndexOf(sprites[0], RendererOf(actor).sprite) != committed || GaitPhaseOf(actor) > 0.0,
            "the gait keeps advancing across the repair");
    }

    private static void GaitMappingBoundaries()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        TickGait(actor, 4, "Walk", 0f, 0.1f, 1);   // 建立状态；0 速冻结，相位完全由布置值决定
        double[] phases = { 0.0, 0.124999, 0.125, 0.3, 0.5, 0.7499999, 0.875, 0.9999 };
        int[] expectedFrames = { 0, 0, 1, 2, 4, 5, 7, 7 };
        for (int i = 0; i < phases.Length; i++)
        {
            SetGaitPhase(actor, phases[i]);
            BankAssistantAtlasVisuals.Tick(actor, 4, false);
            Check(ReferenceEquals(sprites[0][8 + expectedFrames[i]], RendererOf(actor).sprite),
                "phase " + phases[i] + " maps to frame " + expectedFrames[i]);
        }
    }

    // ------------------------------------------------------------ correction round

    /// <summary>有效输入等价（审裁值）：authority Speed 0.8/1.6 Walk、3.2 Run，dt=1/60，
    /// 2026-09-29 验收 clerk 步幅（walk 16 / run 24，PPU 32/0.671875）⇒ 每帧增量
    /// ≈0.039690 / 0.079380 / 0.105840；增量线性（第二帧=第一帧）。</summary>
    private static void GaitValidInputIncrements()
    {
        InstallAtlases();
        BankAssistantAtlasStyle clerk = BankAssistantAtlasMetadata.Styles[0];
        float dt = 1f / 60f;
        // 由验收素材整周期步幅换算的固定判据（dt×speed/(stridePx/PPU)，dt=1/60）。
        double[] verdictIncrements = { 0.039690, 0.079380, 0.105840 };
        var cases = new[]
        {
            new { Action = BankAssistantAtlasAction.Walk, Speed = 0.8f, V = 0 },
            new { Action = BankAssistantAtlasAction.Walk, Speed = 1.6f, V = 1 },
            new { Action = BankAssistantAtlasAction.Run, Speed = 3.2f, V = 2 },
        };
        foreach (var c in cases)
        {
            GameObject actor = MakeActor();
            TickGait(actor, 4, c.Action.ToString(), c.Speed, dt, 1);
            double first = GaitPhaseOf(actor);
            double expected = (double)dt * c.Speed / StrideWorld(clerk, c.Action);
            Near(expected, first, 1e-12, "increment = dt*speed/stride (" + c.Action + "@" + c.Speed + ")");
            Near(verdictIncrements[c.V], first, 1e-3, "increment matches the reviewer value (" + c.Action + "@" + c.Speed + ")");
            TickGait(actor, 4, c.Action.ToString(), c.Speed, dt, 1);
            Near(2.0 * first, GaitPhaseOf(actor), 1e-12, "second tick adds the same increment (linear)");
        }
    }

    /// <summary>客户端归一化反比缩放 + 极端 float 组合：speed=3e38、timeScale=1e-40 时
    /// (double)归一仍有限 → cap 3.2 推进（float 先除的实现会溢出成 inf 而冻结）。</summary>
    private static void GaitExtremeClientNormalization()
    {
        InstallAtlases();
        BankAssistantAtlasStyle clerk = BankAssistantAtlasMetadata.Styles[0];
        double strideWalk = StrideWorld(clerk, BankAssistantAtlasAction.Walk);
        NetworkBigBoss.HasWorldAuth = false;

        // 契约集：raw = v×ts，归一后每帧增量与 ts 无关（反比缩放）。期望按生产的双精度
        // 运算顺序计算：(double)dt × (double)v / stride（raw/ts 的除法因 ts 为 2 的幂而精确）。
        double expected = (double)0.1f * (double)1.6f / strideWalk;
        foreach (float ts in new[] { 0.5f, 1f, 2f })
        {
            Time.timeScale = ts;
            GameObject actor = MakeActor();
            TickGait(actor, 4, "Walk", 1.6f * ts, 0.1f, 1);
            Near(expected, GaitPhaseOf(actor), 1e-12,
                "per-tick increment inversely scales with timeScale (ts=" + ts + ")");
        }

        // 极端组合：float 域会先除溢出；double 归一 → 有限 → cap 3.2 推进
        Time.timeScale = 1e-40f;
        GameObject extreme = MakeActor();
        TickGait(extreme, 4, "Walk", 3e38f, 0.1f, 1);
        double cappedAdvance = (double)0.1f * (double)3.2f / strideWalk;
        Near(cappedAdvance, GaitPhaseOf(extreme), 1e-9,
            "extreme finite client values normalize in double and advance at the cap");

        Time.timeScale = 1f;
        NetworkBigBoss.HasWorldAuth = true;
    }

    /// <summary>读异常边界（GetFloat 注入抛出）：走既有外层 Tick catch——本帧无 sprite 写、
    /// 相位不动、诊断恰一条（WarnOnce 去重）；恢复帧沿既有相位继续，不重置不猜域。</summary>
    private static void GaitReadExceptionBoundary()
    {
        Sprite[][] sprites = InstallAtlases();
        GameObject actor = MakeActor();
        TickGait(actor, 4, "Walk", 0.8f, 0.1f, 2);
        double phase = GaitPhaseOf(actor);
        int writes = SpriteRenderer.SpriteWrites;

        ManualLogSource.Reset();
        Animator.GetFloatThrows = true;
        for (int i = 0; i < 3; i++)
        {
            SetNative(actor, "Walk", 0.5f, 1f, 0.8f);
            BankAssistantAtlasVisuals.Tick(actor, 4, false);
        }
        Animator.GetFloatThrows = false;
        Equal(writes, SpriteRenderer.SpriteWrites, "a read failure writes no sprite that frame");
        Near(phase, GaitPhaseOf(actor), 0.0, "a read failure leaves the phase untouched");
        Equal(1, ManualLogSource.Warnings.Count(w => w.Contains("tick failed")),
            "one deduplicated outer-catch diagnostic across three failing frames");

        // 恢复：沿既有相位继续（0 速冻结但提交当前相位帧）
        SetNative(actor, "Walk", 0.5f, 1f, 0f);
        Time.deltaTime = 0.1f;
        BankAssistantAtlasVisuals.Tick(actor, 4, false);
        Near(phase, GaitPhaseOf(actor), 0.0, "recovery resumes from the existing phase");
        Check(sprites[0].Contains(RendererOf(actor).sprite), "recovery commits the existing-phase frame");
    }
}
