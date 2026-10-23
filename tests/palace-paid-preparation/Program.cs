using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KingdomEnhancedMod;

// H3 host regression: the production PrepareAtPlaying method, its proxy and the complete
// caller paid-preparation block are extracted verbatim at build time. Owner, native/store,
// cold-restore and readiness boundaries are explicit plain stubs. This suite proves the
// local outcome contract only; it does not run Unity, native code, real journal IO or the
// whole VisibleEntry.Update.
internal static class Program
{
    private const string FieldsStartMarker = "private static string? _hookOwner;";
    private const string FieldsEndMarker = "private static long _captureSequence;";
    private const string MethodMarker = "PrepareAtPlaying(Game game, Kingdom kingdom)";
    private const string ProxyMarker = "=> PalacePaidHandle.Engine.PrepareAtPlaying(game,kingdom);";
    private const string CallerMarker = "if (_paidPreparationGame != managers.game.Pointer)";
    private const string DeadlineMarker = "_deadline = Time.unscaledTime + 20f;";
    private const string ProbeStartMarker = "float now = Time.unscaledTime;";
    private const string ProbeEndMarker = "_nextProbe = now + 0.25f;";

    // Captured from the approved pre-fix outer Update before the H3 change. The repair may
    // not alter the existing 20s window or its 0.25s probe cadence.
    private const string DeadlineExpectedSha = "9d259457f400edd8c297192481274cac66d82f9a77897e2869b51e31153d60c5";
    private const int DeadlineExpectedChars = 48;
    private const string ProbeExpectedSha = "c6e2a16b685c5c222e2a983cad208e015cb999c557c58d9418ce5b4edc3d2fbe";
    private const int ProbeExpectedChars = 319;

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    // Each production source is read from disk exactly once. Extraction and every digest use
    // this raw-byte snapshot and its strict UTF-8 decoding, never a second disk read.
    private sealed class SourceSnapshot
    {
        internal readonly string Path;
        internal readonly byte[] Bytes;
        internal readonly string Text;
        internal readonly string Sha256;

        internal SourceSnapshot(string path)
        {
            Path = path;
            Bytes = File.ReadAllBytes(path);
            Text = StrictUtf8.GetString(Bytes);
            Sha256 = Sha(Bytes);
        }
    }

    private static readonly List<object> Rows = new List<object>();
    private static int Assertions;
    private static int FailedAssertions;
    private static int PassedCases;
    private static int FailedCases;
    private static int PointerSerial;
    private static string CurrentCase = "";
    private static string PaidPath;
    private static string VisiblePath;
    private static string MetadataPath;
    private static string GeneratedEnginePath;
    private static string GeneratedCallerPath;

    private static int Main()
    {
        SourceSnapshot paid = null, visible = null;
        try
        {
            ResolvePaths();
            paid = new SourceSnapshot(PaidPath);
            visible = new SourceSnapshot(VisiblePath);
            ValidateExtraction(paid, visible);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("HARD_FAIL compiled extraction identity: " + e.GetBaseException().Message);
            return 2;
        }

        RunAll();
        var summary = BuildSummary(paid, visible);
        Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        return FailedCases == 0 && FailedAssertions == 0 ? 0 : 1;
    }

    private static object BuildSummary(SourceSnapshot paid, SourceSnapshot visible)
    {
        string method = ExtractMethod(paid.Text);
        string proxy = ExtractProxy(paid.Text);
        string caller = ExtractCaller(visible.Text);
        string deadline = ExtractDeadline(visible.Text);
        string probe = ExtractProbe(visible.Text);
        return new
        {
            suite = "palace-paid-preparation",
            paidSourceSha256 = paid.Sha256,
            paidSourceBytes = paid.Bytes.Length,
            visibleSourceSha256 = visible.Sha256,
            visibleSourceBytes = visible.Bytes.Length,
            compiledIdentity = new
            {
                schema = PalacePaidPreparationDigests.Schema,
                metadataSha256 = PalacePaidPreparationDigests.MetadataSha256,
                metadataBytes = PalacePaidPreparationDigests.MetadataBytes,
                generatedEngineSha256 = PalacePaidPreparationDigests.GeneratedEngineSha256,
                generatedCallerSha256 = PalacePaidPreparationDigests.GeneratedCallerSha256
            },
            methods = new[]
            {
                new { name = "Engine.PrepareAtPlaying", sha256 = TextSha(method), chars = method.Length },
                new { name = "PalacePaidResponsibility.PrepareAtPlaying", sha256 = TextSha(proxy), chars = proxy.Length },
                new { name = "PalaceVisibleEntry.paidPrepareBlock", sha256 = TextSha(caller), chars = caller.Length }
            },
            windows = new[]
            {
                new { name = "deadline20s", sha256 = TextSha(deadline), chars = deadline.Length },
                new { name = "probe025s", sha256 = TextSha(probe), chars = probe.Length }
            },
            cases = PassedCases + FailedCases,
            passedCases = PassedCases,
            failedCases = FailedCases,
            assertions = Assertions,
            failedAssertions = FailedAssertions,
            results = Rows
        };
    }

    private static void ResolvePaths()
    {
        Dictionary<string, string> metadata = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);
        PaidPath = metadata["PaidSourcePath"];
        VisiblePath = metadata["VisibleSourcePath"];
        MetadataPath = metadata["PreparationExtractionMetadata"];
        GeneratedEnginePath = metadata["PreparationGeneratedEngine"];
        GeneratedCallerPath = metadata["PreparationGeneratedCaller"];
        foreach (string path in new[] { PaidPath, VisiblePath, MetadataPath, GeneratedEnginePath, GeneratedCallerPath })
            if (!File.Exists(path)) throw new FileNotFoundException("missing generated identity artifact", path);
    }

    private static void ValidateExtraction(SourceSnapshot paid, SourceSnapshot visible)
    {
        string fields = ExtractFields(paid.Text);
        string method = ExtractMethod(paid.Text);
        string proxy = ExtractProxy(paid.Text);
        string caller = ExtractCaller(visible.Text);
        string deadline = ExtractDeadline(visible.Text);
        string probe = ExtractProbe(visible.Text);

        byte[] metadataBytes = File.ReadAllBytes(MetadataPath);
        byte[] generatedEngine = File.ReadAllBytes(GeneratedEnginePath);
        byte[] generatedCaller = File.ReadAllBytes(GeneratedCallerPath);
        string metadataSha = Sha(metadataBytes);
        using JsonDocument document = JsonDocument.Parse(metadataBytes);
        JsonElement root = document.RootElement;

        Require(PalacePaidPreparationDigests.Schema == 2, "compiled digest schema is not 2");
        Require(root.GetProperty("schema").GetInt32() == PalacePaidPreparationDigests.Schema,
            "metadata schema differs from the compiled digest schema");

        RequireSource("paid", paid, PalacePaidPreparationDigests.PaidSourceSha256,
            PalacePaidPreparationDigests.PaidSourceBytes, root, "paidSourceSha256", "paidSourceBytes");
        RequireSource("visible", visible, PalacePaidPreparationDigests.VisibleSourceSha256,
            PalacePaidPreparationDigests.VisibleSourceBytes, root, "visibleSourceSha256", "visibleSourceBytes");

        RequireText("Engine.fields", fields, PalacePaidPreparationDigests.FieldsSha256,
            PalacePaidPreparationDigests.FieldsChars, root.GetProperty("fields"));

        List<JsonElement> methods = root.GetProperty("methods").EnumerateArray().ToList();
        Require(methods.Count == 3, "metadata must pin exactly three extracted regions");
        RequireText("Engine.PrepareAtPlaying", method, PalacePaidPreparationDigests.EngineMethodSha256,
            PalacePaidPreparationDigests.EngineMethodChars, Entry(methods, "Engine.PrepareAtPlaying"));
        RequireText("PalacePaidResponsibility.PrepareAtPlaying", proxy, PalacePaidPreparationDigests.ProxySha256,
            PalacePaidPreparationDigests.ProxyChars, Entry(methods, "PalacePaidResponsibility.PrepareAtPlaying"));
        RequireText("PalaceVisibleEntry.paidPrepareBlock", caller, PalacePaidPreparationDigests.CallerSha256,
            PalacePaidPreparationDigests.CallerChars, Entry(methods, "PalaceVisibleEntry.paidPrepareBlock"));

        List<JsonElement> windows = root.GetProperty("windows").EnumerateArray().ToList();
        Require(windows.Count == 2, "metadata must pin both readiness-window slices");
        RequireWindow("deadline20s", deadline, PalacePaidPreparationDigests.DeadlineSha256,
            PalacePaidPreparationDigests.DeadlineChars, DeadlineExpectedSha, DeadlineExpectedChars,
            Entry(windows, "deadline20s"));
        RequireWindow("probe025s", probe, PalacePaidPreparationDigests.ProbeSha256,
            PalacePaidPreparationDigests.ProbeChars, ProbeExpectedSha, ProbeExpectedChars,
            Entry(windows, "probe025s"));

        List<JsonElement> generated = root.GetProperty("generated").EnumerateArray().ToList();
        Require(generated.Count == 2, "metadata must pin both generated compilation inputs");
        RequireBytes("PalacePaidPreparation.Extracted.cs", generatedEngine,
            PalacePaidPreparationDigests.GeneratedEngineSha256, PalacePaidPreparationDigests.GeneratedEngineBytes,
            Entry(generated, "PalacePaidPreparation.Extracted.cs"));
        RequireBytes("PalacePaidPreparation.Caller.cs", generatedCaller,
            PalacePaidPreparationDigests.GeneratedCallerSha256, PalacePaidPreparationDigests.GeneratedCallerBytes,
            Entry(generated, "PalacePaidPreparation.Caller.cs"));

        Require(metadataSha == PalacePaidPreparationDigests.MetadataSha256,
            "metadata SHA differs from the compiled snapshot: current=" + metadataSha
            + " compiled=" + PalacePaidPreparationDigests.MetadataSha256);
        Require(metadataBytes.Length == PalacePaidPreparationDigests.MetadataBytes,
            "metadata byte length differs from the compiled snapshot");

        Console.WriteLine("identity paid=" + paid.Sha256 + "/" + paid.Bytes.Length
            + " visible=" + visible.Sha256 + "/" + visible.Bytes.Length
            + " fields=" + TextSha(fields)
            + " method=" + TextSha(method)
            + " proxy=" + TextSha(proxy)
            + " caller=" + TextSha(caller)
            + " deadline=" + TextSha(deadline) + "/" + deadline.Length
            + " probe=" + TextSha(probe) + "/" + probe.Length
            + " metadata=" + metadataSha + "/" + metadataBytes.Length
            + " engineGen=" + PalacePaidPreparationDigests.GeneratedEngineSha256
            + " callerGen=" + PalacePaidPreparationDigests.GeneratedCallerSha256);
    }

    private static JsonElement Entry(List<JsonElement> entries, string name)
    {
        JsonElement entry = entries.SingleOrDefault(e => e.GetProperty("name").GetString() == name);
        Require(entry.ValueKind != JsonValueKind.Undefined, "metadata missing entry " + name);
        return entry;
    }

    private static void RequireSource(string name, SourceSnapshot snapshot, string compiledSha, int compiledBytes,
        JsonElement root, string shaProperty, string bytesProperty)
    {
        Require(snapshot.Sha256 == compiledSha,
            name + " source SHA differs from the compiled snapshot: current=" + snapshot.Sha256 + " compiled=" + compiledSha);
        Require(snapshot.Bytes.Length == compiledBytes,
            name + " source byte length differs from the compiled snapshot");
        Require(snapshot.Sha256 == root.GetProperty(shaProperty).GetString(),
            name + " source SHA differs from current extraction metadata");
        Require(snapshot.Bytes.Length == root.GetProperty(bytesProperty).GetInt32(),
            name + " source byte length differs from current extraction metadata");
    }

    private static void RequireText(string name, string text, string compiledSha, int compiledChars, JsonElement metadata)
    {
        string sha = TextSha(text);
        Require(sha == compiledSha,
            name + " SHA differs from the compiled snapshot: current=" + sha + " compiled=" + compiledSha);
        Require(text.Length == compiledChars, name + " chars differ from the compiled snapshot");
        Require(sha == metadata.GetProperty("sha256").GetString(), name + " SHA differs from current extraction metadata");
        Require(text.Length == metadata.GetProperty("chars").GetInt32(),
            name + " chars differ from current extraction metadata");
    }

    private static void RequireWindow(string name, string text, string compiledSha, int compiledChars,
        string approvedSha, int approvedChars, JsonElement metadata)
    {
        RequireText(name, text, compiledSha, compiledChars, metadata);
        Require(TextSha(text) == approvedSha && text.Length == approvedChars,
            name + " is not the approved byte slice");
    }

    private static void RequireBytes(string name, byte[] bytes, string compiledSha, int compiledBytes, JsonElement metadata)
    {
        string sha = Sha(bytes);
        Require(sha == compiledSha,
            name + " SHA differs from the compiled snapshot: current=" + sha + " compiled=" + compiledSha);
        Require(bytes.Length == compiledBytes, name + " byte length differs from the compiled snapshot");
        Require(sha == metadata.GetProperty("sha256").GetString(), name + " SHA differs from current extraction metadata");
        Require(bytes.Length == metadata.GetProperty("bytes").GetInt32(),
            name + " byte length differs from current extraction metadata");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RunAll()
    {
        Case("red.ownerGuard_thenReady_sameGame_exactlyOneAttempt", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.OwnerReady = false;
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == IntPtr.Zero,
                "WaitingForOwner must not consume the per-game marker; actual=" + scene._paidPreparationGame.ToString("X"));
            Check(HostFixture.EnsureStoreCalls == 0 && HostFixture.StoreReadCalls == 0,
                "pure guard must not touch the store; ensure=" + HostFixture.EnsureStoreCalls + " read=" + HostFixture.StoreReadCalls);
            Check(HostFixture.ColdBuilt == 0 && HostFixture.ColdArrivalCalls == 0,
                "pure guard must not build or observe a cold handle");
            Check(scene.PostPaidPrepareReached == 0,
                "Waiting must return before the remaining readiness path");
            Check(scene.WaitCalls == 1 && scene.LastWaitReason == "paid preparation not ready",
                "Waiting must report one fixed concise reason; actual=" + scene.LastWaitReason);
            Check(HostFixture.TryOwnerCalls == 1, "first probe evaluates the owner guard once");
            Check(PalacePaidHandle.Engine.HostPreparedGame == IntPtr.Zero
                && PalacePaidHandle.Engine.HostPreparedOwner == null
                && PalacePaidHandle.Engine.HostPrepared == null
                && PalacePaidHandle.Engine.HostPrepareFault == "not-prepared",
                "pure guard must leave every preparation field untouched; fault=" + PalacePaidHandle.Engine.HostPrepareFault);

            HostFixture.OwnerReady = true;
            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer,
                "Attempted must consume the marker on the retry tick; actual=" + scene._paidPreparationGame.ToString("X"));
            Check(HostFixture.EnsureStoreCalls == 1 && HostFixture.StoreReadCalls == 1,
                "owner-ready retry must make exactly one store attempt; ensure=" + HostFixture.EnsureStoreCalls
                + " read=" + HostFixture.StoreReadCalls);
            Check(scene.PostPaidPrepareReached == 1,
                "Attempted must continue into the original caller path");

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 2,
                "same-game marker must prevent a third owner probe; probes=" + HostFixture.TryOwnerCalls);
            Check(HostFixture.StoreReadCalls == 1, "same-game marker must prevent a third store attempt");
        });

        Case("waiting.gameNotPlaying_thenPlaying", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            game.state = Game.State.Intro;
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == IntPtr.Zero, "notPlaying must not consume the marker");
            Check(HostFixture.StoreReadCalls == 0 && HostFixture.EnsureStoreCalls == 0,
                "notPlaying must not perform store IO");
            Check(scene.WaitCalls == 1 && scene.LastWaitReason == "paid preparation not ready",
                "notPlaying reports the fixed Waiting reason");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "not-prepared",
                "notPlaying leaves the sticky fault untouched");

            game.state = Game.State.Playing;
            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "Playing retry must consume the marker");
            Check(HostFixture.StoreReadCalls == 1 && HostFixture.EnsureStoreCalls == 1,
                "Playing retry must make exactly one store attempt");
            Check(HostFixture.TryOwnerCalls == 2, "each readiness tick evaluates the owner guard once");
        });

        Case("waiting.fullReadinessWindow_zeroSideEffects", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.OwnerReady = false;
            HostScene scene = new HostScene();

            // The real Update probes every 0.25s for at most 20s. This loop invokes the exact
            // caller block 80 times and then stops, as Update would once unscaledTime passes
            // _deadline. It does not execute the surrounding Update or a real clock.
            for (int i = 0; i < 80; i++) scene.Tick(managers);

            Check(scene.TickCalls == 80, "host invoked the caller block 80 times");
            Check(HostFixture.TryOwnerCalls == 80, "every Waiting tick re-evaluates the owner guard; probes=" + HostFixture.TryOwnerCalls);
            Check(scene._paidPreparationGame == IntPtr.Zero, "the bounded window never consumes the marker");
            Check(HostFixture.EnsureStoreCalls == 0 && HostFixture.StoreReadCalls == 0,
                "the bounded window performs zero store IO");
            Check(HostFixture.ColdBuilt == 0 && HostFixture.ColdArrivalCalls == 0,
                "the bounded window performs no cold-handle work");
            Check(scene.PostPaidPrepareReached == 0, "the bounded window never reaches the remaining readiness path");
            Check(scene.WaitCalls == 80 && scene.LastWaitReason == "paid preparation not ready",
                "every Waiting tick reports the same fixed reason");
            Check(PalacePaidHandle.Engine.HostPreparedGame == IntPtr.Zero
                && PalacePaidHandle.Engine.HostPreparedOwner == null
                && PalacePaidHandle.Engine.HostPrepared == null
                && PalacePaidHandle.Engine.HostPrepareFault == "not-prepared",
                "window exhaustion leaves no preparation state behind");
        });

        Case("attempt.success_markerBeforeCold_exactlyOnce", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom, 12);
            HostFixture.CanRestore = true;
            HostFixture.BuildColdOk = true;
            HostFixture.PreparedHandleAvailable = true;
            HostFixture.ColdStage = PalacePaidStage.DepartureClaimed;
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(HostFixture.EnsureStoreCalls == 1 && HostFixture.StoreReadCalls == 1,
                "successful preparation makes exactly one store attempt");
            Check(scene._paidPreparationGame == game.Pointer, "successful preparation consumes the marker");
            Check(HostFixture.ColdBuilt == 1 && HostFixture.BindCalls == 1 && PalacePaidHandle.Engine.HostHandle != null,
                "the extracted method still restores and binds the cold handle");
            Check(HostFixture.ColdArrivalCalls == 1,
                "the original cold-handle caller branch still runs");
            Check(HostFixture.ColdSawMarkerSet,
                "the per-game marker must be consumed before the cold-handle branch");
            Check(scene.PostPaidPrepareReached == 1, "Attempted continues after the paid block");

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1, "the same-game marker skips the engine before any owner probe");
            Check(HostFixture.StoreReadCalls == 1 && HostFixture.ColdArrivalCalls == 1,
                "the same game is never prepared twice");
        });

        Case("attempt.cachedSameOwner_noNewIO", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(HostFixture.StoreReadCalls == 1 && scene._paidPreparationGame == game.Pointer,
                "first attempt reads once and consumes the marker");

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1, "same caller instance skips before the engine on the marker");
            Check(HostFixture.StoreReadCalls == 1, "same caller instance never re-reads");

            HostScene second = new HostScene();
            second.Tick(managers);
            Check(second._paidPreparationGame == game.Pointer,
                "cached same-owner preparation must return Attempted so a fresh caller consumes the marker");
            Check(HostFixture.TryOwnerCalls == 2 && HostFixture.StoreReadCalls == 1 && HostFixture.EnsureStoreCalls == 1,
                "cached same-owner preparation performs no second store attempt");
        });

        Case("terminal.hooksUnverified_noRetry", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            PalacePaidHandle.Engine.HostInstalled = false;
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "hooks-unverified is Attempted and consumes the marker");
            Check(HostFixture.EnsureStoreCalls == 0 && HostFixture.StoreReadCalls == 0, "hooks-unverified performs no store IO");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "hooks-unverified",
                "hooks-unverified keeps its sticky fault; actual=" + PalacePaidHandle.Engine.HostPrepareFault);

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1 && HostFixture.StoreReadCalls == 0,
                "terminal hooks fault is never retried in the same game");
        });

        Case("terminal.storeFailure_noRetry", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.EnsureStoreOk = false;
            HostFixture.StoreReason = "fixture-store-fault";
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "store failure is Attempted and consumes the marker");
            Check(HostFixture.EnsureStoreCalls == 1 && HostFixture.StoreReadCalls == 0,
                "store failure is attempted exactly once with zero journal reads");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "fixture-store-fault",
                "store failure keeps its reason; actual=" + PalacePaidHandle.Engine.HostPrepareFault);

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1 && HostFixture.EnsureStoreCalls == 1,
                "store failure is never retried in the same game");
        });

        Case("terminal.journalHeld_noRetry", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.HasEnvelope = false;
            HostFixture.ReadStatus = PalacePaidStoreReadStatus.Held;
            HostFixture.ReadError = "fixture-held";
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "held journal is Attempted and consumes the marker");
            Check(HostFixture.StoreReadCalls == 1, "held journal is read exactly once");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "journal-held:fixture-held",
                "held journal keeps its sticky reason; actual=" + PalacePaidHandle.Engine.HostPrepareFault);

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1 && HostFixture.StoreReadCalls == 1,
                "held journal is never re-read in the same game");
        });

        Case("terminal.readException_noRetry", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.ThrowRead = true;
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "read exception is Attempted and consumes the marker");
            Check(HostFixture.StoreReadCalls == 1, "read exception happens on the single store attempt");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "prepare:InvalidOperationException",
                "read exception is recorded as the sticky prepare fault; actual=" + PalacePaidHandle.Engine.HostPrepareFault);

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1 && HostFixture.StoreReadCalls == 1,
                "read exception is never retried in the same game");
        });

        Case("terminal.coldRestoreFailure_noRetry", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.CanRestore = true;
            HostFixture.BuildColdOk = false;
            HostFixture.BuildColdReason = "fixture-cold-restore";
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "cold-restore failure is Attempted and consumes the marker");
            Check(HostFixture.StoreReadCalls == 1 && HostFixture.ColdBuilt == 1 && HostFixture.ColdArrivalCalls == 0,
                "cold-restore failure is attempted once and never enters the cold caller branch");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "source-restore:fixture-cold-restore",
                "cold-restore failure keeps its reason; actual=" + PalacePaidHandle.Engine.HostPrepareFault);

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1 && HostFixture.StoreReadCalls == 1 && HostFixture.ColdBuilt == 1,
                "cold-restore failure is never retried in the same game");
        });

        Case("terminal.ownerException_attemptedOnce", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.ThrowOwner = true;
            HostScene scene = new HostScene();

            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "pre-owner exception is Attempted and consumes the marker");
            Check(HostFixture.TryOwnerCalls == 1, "owner exception is thrown on the first attempt");
            Check(HostFixture.EnsureStoreCalls == 0 && HostFixture.StoreReadCalls == 0,
                "pre-owner exception performs no store IO");
            Check(PalacePaidHandle.Engine.HostPrepareFault == "prepare:InvalidOperationException",
                "pre-owner exception is sticky; actual=" + PalacePaidHandle.Engine.HostPrepareFault);

            scene.Tick(managers);
            Check(HostFixture.TryOwnerCalls == 1, "pre-owner exception is not rethrown on later ticks");
        });

        Case("guard.ownerDriftSameGame_failClosed", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostFixture.OwnerId = 1;
            HostScene scene = new HostScene();
            scene.Tick(managers);
            Check(HostFixture.StoreReadCalls == 1 && scene._paidPreparationGame == game.Pointer,
                "first owner prepares once");

            HostFixture.OwnerId = 2;
            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer, "same game keeps its consumed marker under owner drift");
            Check(HostFixture.TryOwnerCalls == 1 && HostFixture.StoreReadCalls == 1,
                "same-game owner drift fails closed with no second preparation");
        });

        Case("epoch.newGame_newAttempt", () =>
        {
            Reset();
            Game game; Kingdom kingdom;
            Managers managers = NewWorld(out game, out kingdom);
            HostScene scene = new HostScene();
            scene.Tick(managers);
            Check(scene._paidPreparationGame == game.Pointer && HostFixture.StoreReadCalls == 1,
                "first game prepares once");

            Game next = new Game { Pointer = NewPointer(), state = Game.State.Playing, currentLand = 1 };
            Kingdom nextKingdom = new Kingdom { Pointer = NewPointer(), playerOne = new Player() };
            Managers nextManagers = new Managers(next, nextKingdom);
            scene.Tick(nextManagers);
            Check(scene._paidPreparationGame == next.Pointer, "a new game pointer consumes its own marker");
            Check(HostFixture.TryOwnerCalls == 2 && HostFixture.StoreReadCalls == 2,
                "the unchanged different-epoch policy prepares the new game once");
        });
    }

    private static void Case(string name, Action body)
    {
        CurrentCase = name;
        int beforeAssertions = Assertions;
        int beforeFailures = FailedAssertions;
        try
        {
            body();
        }
        catch (Exception e)
        {
            Check(false, "unhandled exception: " + e);
        }
        bool passed = FailedAssertions == beforeFailures;
        if (passed) PassedCases++; else FailedCases++;
        Rows.Add(new { name, passed, assertions = Assertions - beforeAssertions, failed = FailedAssertions - beforeFailures });
        Console.WriteLine((passed ? "PASS " : "RED  ") + name);
    }

    private static void Check(bool condition, string message)
    {
        Assertions++;
        if (condition) return;
        FailedAssertions++;
        Console.WriteLine("FAIL " + CurrentCase + ": " + message);
    }

    private static void Reset()
    {
        HostFixture.Reset();
    }

    private static IntPtr NewPointer() => new IntPtr(++PointerSerial);

    private static Managers NewWorld(out Game game, out Kingdom kingdom, int land = 1)
    {
        game = new Game { Pointer = NewPointer(), state = Game.State.Playing, currentLand = land };
        kingdom = new Kingdom { Pointer = NewPointer(), playerOne = new Player() };
        return new Managers(game, kingdom);
    }

    private static int LineStart(string source, int index)
    {
        int newline = source.LastIndexOf('\n', index);
        return newline < 0 ? 0 : newline + 1;
    }

    private static int LineEnd(string source, int start)
    {
        int end = source.IndexOf('\n', start);
        if (end < 0) end = source.Length;
        if (end > start && source[end - 1] == '\r') end--;
        return end;
    }

    private static int FindLine(string source, string marker, bool excludeArrow)
    {
        int from = 0;
        while (true)
        {
            int index = source.IndexOf(marker, from, StringComparison.Ordinal);
            if (index < 0) throw new InvalidOperationException("Production marker missing: " + marker);
            int start = LineStart(source, index);
            int end = LineEnd(source, start);
            if (!excludeArrow || !source.Substring(start, end - start).Contains("=>", StringComparison.Ordinal))
                return start;
            from = end;
        }
    }

    private static string SliceLine(string source, int start) => source.Substring(start, LineEnd(source, start) - start);

    private static string ExtractFields(string source)
    {
        int start = FindLine(source, FieldsStartMarker, false);
        int end = LineEnd(source, FindLine(source, FieldsEndMarker, false));
        return source.Substring(start, end - start);
    }

    private static string ExtractMethod(string source)
    {
        int start = FindLine(source, MethodMarker, true);
        int open = FindCodeChar(source, start, '{');
        int end = BalancedEnd(source, open);
        return source.Substring(start, end - start + 1);
    }

    private static string ExtractProxy(string source) => SliceLine(source, FindLine(source, ProxyMarker, false));

    private static string ExtractCaller(string source)
    {
        int start = FindLine(source, CallerMarker, false);
        int open = FindCodeChar(source, start, '{');
        int end = BalancedEnd(source, open);
        return source.Substring(start, end - start + 1);
    }

    private static string ExtractDeadline(string source) => SliceLine(source, FindLine(source, DeadlineMarker, false));

    private static string ExtractProbe(string source)
    {
        int start = FindLine(source, ProbeStartMarker, false);
        int last = FindLine(source, ProbeEndMarker, false);
        int end = LineEnd(source, last);
        return source.Substring(start, end - start);
    }

    private static int FindCodeChar(string source, int start, char wanted)
    {
        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];
            if (!TrySkip(source, ref i, out bool isCode) || !isCode) continue;
            if (c == wanted) return i;
        }
        throw new InvalidOperationException("Code character not found: " + wanted);
    }

    private static int BalancedEnd(string source, int open)
    {
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (!TrySkip(source, ref i, out bool isCode) || !isCode) continue;
            char c = source[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        throw new InvalidOperationException("Unbalanced production block");
    }

    private static bool TrySkip(string source, ref int i, out bool isCode)
    {
        isCode = true;
        char c = source[i];
        if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
        {
            i = source.IndexOf('\n', i + 2);
            if (i < 0) i = source.Length;
            isCode = false;
            return false;
        }
        if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
        {
            int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (close < 0) throw new InvalidOperationException("Unterminated block comment");
            i = close + 1;
            isCode = false;
            return false;
        }
        if (c == '"' || c == '\'')
        {
            char quote = c;
            bool verbatim = quote == '"' && i > 0 && source[i - 1] == '@';
            i++;
            while (i < source.Length)
            {
                if (verbatim && source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                if (!verbatim && source[i] == '\\') { i += 2; continue; }
                if (source[i] == quote) break;
                i++;
            }
            if (i >= source.Length) throw new InvalidOperationException("Unterminated literal");
            isCode = false;
            return false;
        }
        return true;
    }

    private static string TextSha(string text) => Sha(StrictUtf8.GetBytes(text));

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
