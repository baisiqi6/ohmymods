// 死地随从录制·窄回归（issue-79 第一切片）。
// 只验证有界簿记语义：资格/单次选择、frame 去重、容量与时间边界、身份终止、
// 事件顺序与丢弃、停止后零读取、零写入、写盘失败只报一次。
// 这些 stub 不验证真实 Unity Animator 相位或 IL2CPP detour；现场录制由 Operator 负责。
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Il2CppInterop.Runtime.Injection;
using KingdomEnhancedMod;
using UnityEngine;

static partial class Program
{
    static int passed, failed;
    static readonly string TempRoot = Path.Combine(Path.GetTempPath(),
        "kec-follower-capture-tests-" + Environment.ProcessId);

    static void Eq<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(label + ": expected [" + expected + "], got [" + actual + "]");
    }

    static void True(bool value, string label)
    {
        if (!value) throw new Exception(label + ": expected true");
    }

    static void Contains(string text, string needle, string label)
    {
        if (text == null || text.IndexOf(needle, StringComparison.Ordinal) < 0)
            throw new Exception(label + ": missing [" + needle + "]");
    }

    static void Test(string name, Action action)
    {
        ResetWorld();
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message);
        }
    }

    // ============================================================
    // world / 对象夹具
    // ============================================================

    static Managers _managers;
    static World _world;
    static Transform _layer;

    static void ResetWorld()
    {
        DeadlandsFollowerCapture.ResetForTests();
        Probe.Reset();
        Time.time = 0f;
        Time.deltaTime = 0.02f;
        Time.realtimeSinceStartup = 0f;
        Time.timeScale = 1f;
        Time.frameCount = 0;
        CameraMarshaller.SetInst(null);
        CameraMarshaller.LastRequestedId = -1;
        ClassInjector.Registered = true;
        UnitScanCache.Archers = Array.Empty<Archer>();
        UnitScanCache.GetArchersCalls = 0;
        KingdomEnhancedPlugin.Instance.LogSource.Info.Clear();
        KingdomEnhancedPlugin.Instance.LogSource.Errors.Clear();
        Paths.ConfigPath = TempRoot;
        Directory.CreateDirectory(TempRoot);
        _world = new GameObject("world").AddComponent<World>();
        _layer = _world.gameObject.AddChild("layer").transform;
        _world.gameLayer = _layer;
        _managers = new Managers { world = _world };
        Managers.Inst = _managers;
    }

    static Knight NewKnight(int style)
    {
        var go = new GameObject("knight");
        go.transform.SetParent(_layer, false);
        var knight = go.AddComponent<Knight>();
        knight.Style = style;
        return knight;
    }

    static Archer NewArcher(Knight knight, float x, bool crossbow = false, bool norse = false,
        bool withBody = true, bool withAnimator = true, bool withMover = true, bool dead = false,
        bool withDamageable = true, Transform parent = null)
    {
        var go = new GameObject("archer");
        go.transform.SetParent(parent != null ? parent : _layer, false);
        go.transform.position = new Vector3(x, 0f, 0f);
        var archer = go.AddComponent<Archer>();
        archer._knight = knight;
        archer._mover = withMover ? go.AddComponent<Mover>() : null;
        archer._animator = withAnimator ? go.AddComponent<Animator>() : null;
        archer._damageable = withDamageable ? go.AddComponent<Damageable>() : null;
        if (archer._damageable != null) archer._damageable.isDead = dead;
        archer.Crossbow = crossbow;
        archer.Norse = norse;
        if (withBody) go.AddComponent<SpriteRenderer>();
        return archer;
    }

    /// <summary>唯一锚点来源 = 原生主视口 0（没有 Camera.main 路径可走）。</summary>
    static MainCamera NewViewport(float x)
    {
        var go = new GameObject("viewport");
        go.transform.position = new Vector3(x, 0f, 0f);
        var viewport = go.AddComponent<MainCamera>();
        CameraMarshaller.SetInst(CameraMarshaller.With(viewport));
        return viewport;
    }

    /// <summary>武装 + 关面板后的第一次 LateUpdate 绑定；返回被选中的随从。</summary>
    static Archer SetupRecording(bool withBody = true)
    {
        var knight = NewKnight(1);
        var archer = NewArcher(knight, 0f, withBody: withBody);
        NewViewport(0f);
        UnitScanCache.Archers = new[] { archer };
        DeadlandsFollowerCapture.ArmFromPanel();
        DeadlandsFollowerCapture.SampleLate(false);
        return archer;
    }

    static void Frame()
    {
        Time.frameCount++;
        DeadlandsFollowerCapture.SampleLate(false);
    }

    static void Frame(int count)
    {
        for (int i = 0; i < count; i++) Frame();
    }

    // ============================================================
    // CSV 读取
    // ============================================================

    static List<string> Section(string csv, string name)
    {
        var result = new List<string>();
        string[] lines = csv.Split('\n');
        bool inside = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.StartsWith("# "))
            {
                inside = line == "# " + name;
                continue;
            }
            if (!inside || line.Length == 0) continue;
            result.Add(line);
        }
        return result;
    }

    static string SummaryValue(string csv, string key)
    {
        foreach (string line in Section(csv, "summary"))
        {
            int eq = line.IndexOf('=');
            if (eq > 0 && line.Substring(0, eq) == key) return line.Substring(eq + 1);
        }
        return null;
    }

    static string Field(string line, int index)
    {
        string[] parts = line.Split(',');
        return index < parts.Length ? parts[index] : "";
    }

    /// <summary>按列名取样本行（列布局变化时不依赖硬编码下标）。</summary>
    static string SampleField(string csv, int row, string column)
    {
        var lines = Section(csv, "samples");
        if (lines.Count == 0) throw new Exception("no samples section");
        int index = Array.IndexOf(lines[0].Split(','), column);
        if (index < 0) throw new Exception("missing sample column [" + column + "]");
        return Field(lines[row + 1], index);
    }

    static string ReadCsv() => File.ReadAllText(DeadlandsFollowerCapture.LastOutputPath);

    // ============================================================
    // cases
    // ============================================================

    static void Main()
    {
        Test("selection: nearest eligible only; crossbow/norse/other-style excluded; single scan", () =>
        {
            var knight = NewKnight(1);
            var far = NewArcher(knight, -5f);
            var near = NewArcher(knight, 1f);
            NewViewport(0f);
            UnitScanCache.Archers = new[] { far, near, NewArcher(knight, 0.5f, crossbow: true), NewArcher(knight, 0.4f, norse: true), NewArcher(NewKnight(0), 0.3f) };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("", DeadlandsFollowerCapture.LastStopReason, "still recording after bind");
            Eq(1, UnitScanCache.GetArchersCalls, "single scan");
            _managers.world = new GameObject("world2").AddComponent<World>();
            Frame();
            Eq("world-changed", DeadlandsFollowerCapture.LastStopReason, "stop reason");
            string csv = ReadCsv();
            Contains(csv, "target_go=" + near.gameObject.GetInstanceID(), "selected nearest eligible");
            Contains(csv, "candidates=2", "eligible candidate count");
        });

        Test("no target: ends once, explains, never rescans", () =>
        {
            NewArcher(NewKnight(0), 1f);
            NewViewport(0f);
            UnitScanCache.Archers = new[] { NewArcher(NewKnight(0), 1f) };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("no-target", DeadlandsFollowerCapture.LastStopReason, "reason");
            Eq(1, UnitScanCache.GetArchersCalls, "scan count");
            Contains(ReadCsv(), "stop_reason=no-target", "csv reason");
            UnitScanCache.Archers = new[] { NewArcher(NewKnight(1), 0f) };
            Frame(3);
            Eq(1, UnitScanCache.GetArchersCalls, "no rescan");
            Eq("no-target", DeadlandsFollowerCapture.LastStopReason, "still finished");
        });

        Test("frame dedup + game-time and realtime caps", () =>
        {
            SetupRecording();
            Eq(0, DeadlandsFollowerCapture.SampleCount, "no sample while binding");
            DeadlandsFollowerCapture.SampleLate(false);
            Eq(1, DeadlandsFollowerCapture.SampleCount, "first sample");
            DeadlandsFollowerCapture.SampleLate(false);
            Eq(1, DeadlandsFollowerCapture.SampleCount, "same frame dedup");
            Frame();
            Eq(2, DeadlandsFollowerCapture.SampleCount, "next frame sample");
            Time.time = 9f;
            Frame();
            Eq("game-8s", DeadlandsFollowerCapture.LastStopReason, "game time cap");

            SetupRecording();
            Frame();
            Time.realtimeSinceStartup = 31f;
            Frame();
            Eq("real-30s", DeadlandsFollowerCapture.LastStopReason, "realtime cap");
        });

        Test("sample cap stops at 1024 and writes a bounded file", () =>
        {
            SetupRecording();
            Frame(1200);
            Eq("samples-1024", DeadlandsFollowerCapture.LastStopReason, "reason");
            string csv = ReadCsv();
            Eq("1024", SummaryValue(csv, "samples"), "sample count");
            Eq(1024, Section(csv, "samples").Count - 1, "csv sample rows");
            int reads = Probe.Reads;
            Frame(5);
            Eq(reads, Probe.Reads, "no reads after stop");
        });

        Test("event budget 64 with dropped counter, target-only", () =>
        {
            var archer = SetupRecording();
            var observer = archer.gameObject.AddComponent<DeadlandsAnimObserver>();
            observer.Archer = archer;
            var stranger = NewArcher(NewKnight(1), 20f);
            var strangerObserver = stranger.gameObject.AddComponent<DeadlandsAnimObserver>();
            strangerObserver.Archer = stranger;
            for (int i = 0; i < 70; i++)
                DeadlandsFollowerCapture.OnAnimatorSpeedWrite(archer._animator, observer, 777, 1f, 2f, false);
            DeadlandsFollowerCapture.OnAnimatorSpeedWrite(stranger._animator, strangerObserver, 888, 1f, 2f, false);
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Eq("64", SummaryValue(csv, "events"), "event cap");
            Eq("6", SummaryValue(csv, "dropped_events"), "dropped count");
            Eq(64, Section(csv, "events").Count - 1, "csv event rows");
        });

        Test("convert/style/controller event order recorded for the target only", () =>
        {
            var archer = SetupRecording();
            var stranger = NewArcher(NewKnight(1), 20f);
            DeadlandsFollowerCapture.OnConvertBefore(stranger, false);
            DeadlandsFollowerCapture.OnConvertBefore(archer, false);
            DeadlandsFollowerCapture.OnPostfixEntry(archer, false);
            DeadlandsFollowerCapture.OnStyleBefore(archer);
            DeadlandsFollowerCapture.OnControllerWriteBefore(archer);
            DeadlandsFollowerCapture.OnControllerWriteAfter(archer);
            DeadlandsFollowerCapture.OnStyleAfter(archer);
            DeadlandsFollowerCapture.OnControllerNoWrite(stranger);
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            var kinds = new List<string>();
            var eventLines = Section(csv, "events");
            for (int i = 1; i < eventLines.Count; i++) kinds.Add(Field(eventLines[i], 2));
            Eq("convert-before,postfix-entry,style-before,ctl-write-before,ctl-write-after,style-after",
                string.Join(",", kinds), "event order");
            Eq("1", SummaryValue(csv, "controller_writes"), "write count");
            Eq("0", SummaryValue(csv, "controller_calls_no_write"), "non-target no-write ignored");
        });

        Test("identity stops: inactive, team, style/marker/norse/dead, animator(x2), mover(x2), body(x3), world", () =>
        {
            var archer = SetupRecording();
            Frame();
            archer.gameObject.activeSelf = false;
            Frame();
            Eq("target-inactive", DeadlandsFollowerCapture.LastStopReason, "inactive");

            archer = SetupRecording();
            Frame();
            archer._knight = NewKnight(1);
            Frame();
            Eq("team-changed", DeadlandsFollowerCapture.LastStopReason, "team");

            archer = SetupRecording();
            Frame();
            archer._animator = new GameObject("anim2").AddComponent<Animator>();
            Frame();
            Eq("animator-changed", DeadlandsFollowerCapture.LastStopReason, "animator replaced");

            archer = SetupRecording();
            Frame();
            archer._animator = null;
            Frame();
            Eq("animator-changed", DeadlandsFollowerCapture.LastStopReason, "animator cleared");

            archer = SetupRecording();
            Frame();
            archer._mover = new GameObject("mover2").AddComponent<Mover>();
            Frame();
            Eq("mover-changed", DeadlandsFollowerCapture.LastStopReason, "mover");

            archer = SetupRecording();
            Frame();
            archer.gameObject.GetComponent<SpriteRenderer>().gameObject = null;
            Frame();
            Eq("body-changed", DeadlandsFollowerCapture.LastStopReason, "body nulled");

            archer = SetupRecording();
            Frame();
            archer.gameObject.Remove(archer.gameObject.GetComponent<SpriteRenderer>());
            archer.gameObject.AddComponent<SpriteRenderer>();
            Frame();
            Eq("body-changed", DeadlandsFollowerCapture.LastStopReason, "renderer replaced");

            archer = SetupRecording();
            Frame();
            UnityEngine.Object.Destroy(archer.gameObject.GetComponent<SpriteRenderer>());
            Frame();
            Eq("body-changed", DeadlandsFollowerCapture.LastStopReason, "renderer removed from animator GO");

            archer = SetupRecording();
            Frame();
            archer._mover = null;
            Frame();
            Eq("mover-changed", DeadlandsFollowerCapture.LastStopReason, "mover cleared");

            archer = SetupRecording();
            Frame();
            archer._knight.Style = 2;
            Frame();
            Eq("style-changed", DeadlandsFollowerCapture.LastStopReason, "style changed");

            archer = SetupRecording();
            Frame();
            archer.Crossbow = true;
            Frame();
            Eq("crossbow-changed", DeadlandsFollowerCapture.LastStopReason, "became crossbow marker");

            archer = SetupRecording();
            Frame();
            archer.Norse = true;
            Frame();
            Eq("norse-changed", DeadlandsFollowerCapture.LastStopReason, "became effective norse");

            archer = SetupRecording();
            Frame();
            archer._damageable.isDead = true;
            Frame();
            Eq("target-dead", DeadlandsFollowerCapture.LastStopReason, "died");

            SetupRecording();
            Frame();
            _managers.world = new GameObject("world3").AddComponent<World>();
            Frame();
            Eq("world-changed", DeadlandsFollowerCapture.LastStopReason, "world");
        });

        Test("raw normalizedTime lap is not a regression; same-state reset is", () =>
        {
            var archer = SetupRecording();
            int walk = Animator.StringToHash("Walk");
            archer._animator.SetState(walk, 0.90f); Frame();
            archer._animator.SetState(walk, 1.95f); Frame();
            archer._animator.SetState(walk, 2.01f); Frame();
            archer._animator.SetState(walk, 2.05f); Frame();
            archer._animator.SetState(walk, 0.10f); Frame();
            Eq(0, DeadlandsFollowerCapture.StateSwitches, "no state switches");
            Eq(1, DeadlandsFollowerCapture.PhaseBackJumps, "one reset detected");
            Time.time = 9f;
            Frame();
        });

        Test("recording performs no Unity writes; stopped entry reads nothing", () =>
        {
            SetupRecording();
            Probe.Reset();
            Frame(30);
            Eq(0, Probe.Writes, "no writes during recording");
            True(Probe.Reads > 0, "samples read state");
            Time.time = 9f;
            Frame();
            Eq(0, Probe.Writes, "no writes at finish");
            int reads = Probe.Reads;
            Frame(10);
            Eq(reads, Probe.Reads, "stopped entry only boolean early-out");
            Eq("game-8s", DeadlandsFollowerCapture.LastStopReason, "finished");
        });

        Test("write failure reports once, discards, no retry", () =>
        {
            SetupRecording();
            Frame();
            Paths.ConfigPath = "\0invalid";
            Time.time = 9f;
            Frame();
            Eq("game-8s", DeadlandsFollowerCapture.LastStopReason, "session ends anyway");
            Eq("", DeadlandsFollowerCapture.LastOutputPath, "no path");
            Eq(1, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "reported once");
            SetupRecording();
            Frame();
            Time.time = 20f;
            Frame();
            Eq(1, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "still once");
        });

        Test("selection skips invalid nearest candidates; all-invalid ends no-target", () =>
        {
            var knight = NewKnight(1);
            var farValid = NewArcher(knight, 3f);
            var childOnly = NewArcher(knight, 0.7f, withBody: false);
            childOnly.gameObject.AddChild("child").AddComponent<SpriteRenderer>();   // 非同 GO renderer：拒绝
            var otherLayer = new GameObject("other-layer").transform;
            NewViewport(0f);
            UnitScanCache.Archers = new[]
            {
                NewArcher(knight, 0.1f, dead: true),
                NewArcher(knight, 0.2f, withDamageable: false),
                NewArcher(knight, 0.3f, withAnimator: false),
                NewArcher(knight, 0.4f, withMover: false),
                NewArcher(knight, 0.5f, parent: otherLayer),
                childOnly,
                farValid,
            };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("", DeadlandsFollowerCapture.LastStopReason, "bound despite invalid nearer candidates");
            Frame();
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Contains(csv, "target_go=" + farValid.gameObject.GetInstanceID(), "nearest bindable selected");
            Contains(csv, "candidates=1", "only the bindable candidate counted");

            // 全部不合格 → no-target，且不重扫
            int scans = UnitScanCache.GetArchersCalls;
            UnitScanCache.Archers = new[]
            {
                NewArcher(knight, 0f, withBody: false),
                NewArcher(knight, 1f, dead: true),
            };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("no-target", DeadlandsFollowerCapture.LastStopReason, "no bindable target");
            Eq(scans + 1, UnitScanCache.GetArchersCalls, "one scan per session only");
            Contains(ReadCsv(), "candidates=0", "no candidate counted");
        });

        Test("primary viewport 0 is the only anchor; unavailable viewport ends before scan", () =>
        {
            var knight = NewKnight(1);
            var archer = NewArcher(knight, 1f);
            NewViewport(0f);
            UnitScanCache.Archers = new[] { archer };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("", DeadlandsFollowerCapture.LastStopReason, "recording via native viewport 0");
            Eq(0, CameraMarshaller.LastRequestedId, "only viewport 0 requested");
            Frame();
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Contains(csv, "anchor_kind=native-viewport-0", "csv anchor kind");
            Contains(csv, "anchor_x=0.00", "csv anchor x");
            Contains(csv, "target_go=" + archer.gameObject.GetInstanceID(), "target bound");

            // InstExists=false：一次收尾、零扫描、无 fallback
            DeadlandsFollowerCapture.ResetForTests();
            Probe.Reset();
            Time.time = 0f;
            UnitScanCache.GetArchersCalls = 0;
            CameraMarshaller.SetInst(null);
            UnitScanCache.Archers = new[] { archer };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("primary-viewport-unresolved", DeadlandsFollowerCapture.LastStopReason, "no inst");
            Eq(0, UnitScanCache.GetArchersCalls, "no scan without viewport");
            Contains(ReadCsv(), "stop_reason=primary-viewport-unresolved", "csv reason");
            Contains(ReadCsv(), "target_go=0", "no target bound");

            // Inst 存在但 viewport 0 缺失
            CameraMarshaller.SetInst(CameraMarshaller.With(null));
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("primary-viewport-unresolved", DeadlandsFollowerCapture.LastStopReason, "camera null");
            Eq(0, UnitScanCache.GetArchersCalls, "still no scan");

            // 相机 GO 未激活
            var viewport = NewViewport(0f);
            viewport.gameObject.activeSelf = false;
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("primary-viewport-unresolved", DeadlandsFollowerCapture.LastStopReason, "inactive camera");
            Eq(0, UnitScanCache.GetArchersCalls, "still no scan");

            // X 非有限
            viewport.gameObject.activeSelf = true;
            viewport.transform.position = new Vector3(float.NaN, 0f, 0f);
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("primary-viewport-unresolved", DeadlandsFollowerCapture.LastStopReason, "non-finite X");
            Eq(0, UnitScanCache.GetArchersCalls, "still no scan");
        });

        Test("observer type unregistered: recording runs; cached only via later event", () =>
        {
            ClassInjector.Registered = false;   // stub 的 GetComponent<DeadlandsAnimObserver> 此时会抛
            var archer = SetupRecording();
            Frame(3);
            Eq(3, DeadlandsFollowerCapture.SampleCount, "recorded without observer type");
            Eq(0, KingdomEnhancedPlugin.Instance.LogSource.Errors.Count, "no error");
            Eq(0, Probe.ClipInfoReads, "no clip reads for zero-hash state");
            ClassInjector.Registered = true;
            var observer = archer.gameObject.AddComponent<DeadlandsAnimObserver>();
            observer.Archer = archer;
            DeadlandsFollowerCapture.OnAnimatorSpeedWrite(archer._animator, observer, 333, 1f, 2f, false);
            Frame();
            Eq(4, DeadlandsFollowerCapture.SampleCount, "sample after event");
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Eq("1", SummaryValue(csv, "observer_samples"), "observer cached after event");
            var samples = Section(csv, "samples");
            Eq("1", SampleField(csv, samples.Count - 2, "observer"), "last sample has observer");
        });

        Test("same state under a new controller re-reads clip info", () =>
        {
            var archer = SetupRecording();
            int walk = Animator.StringToHash("Walk");
            archer._animator.SetController(new RuntimeAnimatorController("boardA"));
            archer._animator.Clips = new[] { new AnimatorClipInfo { clip = new AnimationClip("walkA", 1.25f) } };
            archer._animator.SetState(walk, 0.25f);
            Frame();
            archer._animator.SetController(new RuntimeAnimatorController("boardB"));
            archer._animator.Clips = new[] { new AnimatorClipInfo { clip = new AnimationClip("walkB", 0.75f) } };
            archer._animator.SetState(walk, 0.30f);
            Frame();
            Frame();
            Eq(2, Probe.ClipInfoReads, "one clip read per controller+state");
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Contains(csv, "walkA", "first combo name kept");
            Contains(csv, "walkB", "second combo name decoded");
        });

        Test("all 12 walk/run sprite names export without truncation", () =>
        {
            var archer = SetupRecording();
            var body = archer.gameObject.GetComponent<SpriteRenderer>();
            for (int i = 0; i < 12; i++)
            {
                body.sprite = new Sprite(i < 6 ? "walk_" + i : "run_" + i);
                Frame();
            }
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            for (int i = 0; i < 12; i++) Contains(csv, (i < 6 ? "walk_" : "run_") + i, "sprite name " + i);
            string spriteLine = SummaryValue(csv, "sprite_names");
            int bars = 0;
            foreach (char c in spriteLine) if (c == '|') bars++;
            Eq(11, bars, "all 12 entries separated");
            Eq("12", SummaryValue(csv, "distinct_sprites"), "distinct sprite count");
        });

        Test("renderer inactive on an active unit is recorded", () =>
        {
            var knight = NewKnight(1);
            var archer = NewArcher(knight, 0f, withBody: false);
            var child = archer.gameObject.AddChild("body-child");
            archer._animator = child.AddComponent<Animator>();
            child.AddComponent<SpriteRenderer>();
            NewViewport(0f);
            UnitScanCache.Archers = new[] { archer };
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Frame();
            child.activeSelf = false;
            Frame();
            Time.time = 9f;
            Frame();
            Eq("1", SummaryValue(ReadCsv(), "renderer_inactive_samples"), "inactive sample counted");
        });

        Test("pause consumes no samples; target and real-30s still checked", () =>
        {
            var archer = SetupRecording();
            Frame();
            Eq(1, DeadlandsFollowerCapture.SampleCount, "first sample");
            Time.deltaTime = 0f;
            Frame(10);
            Eq(1, DeadlandsFollowerCapture.SampleCount, "paused frames consume nothing");
            archer._knight.Style = 2;                        // 暂停期间仍每帧复核目标
            Frame();
            Eq("style-changed", DeadlandsFollowerCapture.LastStopReason, "target still checked while paused");

            Time.deltaTime = 0.02f;                          // 恢复帧步进，再进下一轮
            SetupRecording();
            Frame();
            Time.deltaTime = 0f;
            Frame(5);
            Eq(1, DeadlandsFollowerCapture.SampleCount, "paused again");
            Time.realtimeSinceStartup = 31f;                 // 暂停不影响真实 30 秒上限
            Frame();
            Eq("real-30s", DeadlandsFollowerCapture.LastStopReason, "real cap while paused");

            SetupRecording();
            Time.deltaTime = 0f;
            Frame(3);
            Eq(0, DeadlandsFollowerCapture.SampleCount, "no samples before resume");
            Time.deltaTime = 0.02f;
            Frame();
            Eq(1, DeadlandsFollowerCapture.SampleCount, "samples resume after unpause");
        });

        Test("OnDisable notification stops bound session without touching old cleanup", () =>
        {
            var archer = SetupRecording();
            var stranger = NewArcher(NewKnight(1), 20f);
            Frame();
            DeadlandsFollowerCapture.NotifyDisabled(stranger);          // 非目标：忽略
            Frame();
            Eq("", DeadlandsFollowerCapture.LastStopReason, "stranger disable ignored");
            Eq("", DeadlandsFollowerCapture.LastOutputPath, "notify writes nothing");

            DeadlandsFollowerCapture.NotifyDisabled(archer);
            Frame();
            Eq("target-disabled", DeadlandsFollowerCapture.LastStopReason, "notify stops next late update");
            Contains(ReadCsv(), "stop_reason=target-disabled", "csv reason");
            int reads = Probe.Reads;
            Frame(3);
            Eq(reads, Probe.Reads, "stopped after notify");

            // 同一帧 disable→enable：不依赖 activeInHierarchy，仍须停止
            var archer2 = SetupRecording();
            Frame();
            archer2.gameObject.activeSelf = false;
            DeadlandsFollowerCapture.NotifyDisabled(archer2);
            archer2.gameObject.activeSelf = true;
            Frame();
            Eq("target-disabled", DeadlandsFollowerCapture.LastStopReason, "disable→enable same frame stops");

            // 通知自身零副作用：null / 已结束会话都不抛、不写
            DeadlandsFollowerCapture.NotifyDisabled(null);
            DeadlandsFollowerCapture.NotifyDisabled(archer2);
        });

        Test("controller changes are separate from same-state phase backjumps", () =>
        {
            var archer = SetupRecording();
            int walk = Animator.StringToHash("Walk");
            archer._animator.SetController(new RuntimeAnimatorController("boardA"));
            archer._animator.SetState(walk, 2.0f);
            Frame();
            archer._animator.SetState(walk, 2.1f);
            Frame();
            Eq(0, DeadlandsFollowerCapture.PhaseBackJumps, "forward phase is not a backjump");

            archer._animator.SetController(new RuntimeAnimatorController("boardB"));
            archer._animator.SetState(walk, 0.1f);
            Frame();
            Eq(0, DeadlandsFollowerCapture.PhaseBackJumps, "cross-controller drop is not same-state");

            archer._animator.SetState(walk, 2.0f);
            Frame();
            archer._animator.SetState(walk, 0.2f);
            Frame();
            Eq(1, DeadlandsFollowerCapture.PhaseBackJumps, "same controller+state reset counted");
            Time.time = 9f;
            Frame();
            Eq("1", SummaryValue(ReadCsv(), "controller_changes"), "controller changes counted separately");
        });

        Test("CSV separates state_full/state_short/observer hash with state speed and animator enabled", () =>
        {
            var archer = SetupRecording();
            var observer = archer.gameObject.AddComponent<DeadlandsAnimObserver>();
            observer.Archer = archer;
            observer.AttackStateHash = 4242;
            DeadlandsFollowerCapture.OnAnimatorSpeedWrite(archer._animator, observer, 1, 1f, 1f, false);
            archer._animator.SetStateEx(1111, 2222, 0.5f);
            archer._animator.SetStateSpeed(1.25f, 0.5f);
            archer._animator.enabled = false;
            Frame();
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Eq("1111", SampleField(csv, 0, "state_full"), "full hash");
            Eq("2222", SampleField(csv, 0, "state_short"), "short hash exported");
            Eq("4242", SampleField(csv, 0, "attack_state"), "observer short hash stays separate");
            Eq("1.2500", SampleField(csv, 0, "state_speed"), "state speed");
            Eq("0.5000", SampleField(csv, 0, "state_speed_mult"), "state speed multiplier");
            Eq("0", SampleField(csv, 0, "animator_enabled"), "animator disabled exported");
        });

        Test("null sprite is not counted as distinct art; nulls are counted separately", () =>
        {
            var archer = SetupRecording();
            Frame();                                             // sprite null
            var body = archer.gameObject.GetComponent<SpriteRenderer>();
            body.sprite = new Sprite("walk_a");
            Frame();
            body.sprite = null;
            Frame();
            Time.time = 9f;
            Frame();
            string csv = ReadCsv();
            Eq("1", SummaryValue(csv, "distinct_sprites"), "only the real sprite");
            Eq("2", SummaryValue(csv, "null_sprite_samples"), "null samples counted");
        });

        Test("arm resets per-session anchor/candidate metadata; early failure shows no prior session", () =>
        {
            SetupRecording();
            Frame();
            Time.time = 9f;
            Frame();
            string firstPath = DeadlandsFollowerCapture.LastOutputPath;
            True(firstPath.Length > 0, "first session wrote a file");
            string firstCsv = ReadCsv();
            Contains(firstCsv, "candidates=1", "first session candidates");
            Contains(firstCsv, "anchor_x=0.00", "first session anchor");

            CameraMarshaller.SetInst(null);
            DeadlandsFollowerCapture.ArmFromPanel();
            DeadlandsFollowerCapture.SampleLate(false);
            Eq("primary-viewport-unresolved", DeadlandsFollowerCapture.LastStopReason, "early failure");
            string secondCsv = ReadCsv();
            True(secondCsv != firstCsv, "second csv is a new file");
            Contains(secondCsv, "candidates=0", "no stale candidates");
            Contains(secondCsv, "anchor_x=na", "no stale anchor");
            Contains(secondCsv, "target_go=0", "no stale target");
        });

        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
