#if VOYAGE_SAVE_DIAGNOSTICS && !ANDROID
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Il2CppPersistentSet = Il2CppSystem.Collections.Generic.HashSet<Persistent>;

namespace KingdomEnhancedMod;

// Issue 281 / Slice A. Read-only observation of the existing Save/GetID/Apply boundaries while
// the game is actually SailingAway. It never calls GetID, ShouldPersist, DoPersistInstance,
// OnEnable, registration, repair or any writer; it only reads the exact native objects already
// supplied by those boundaries. All output is bounded and every read/format/log failure is
// swallowed here so it can never enter GetIdPatch's catch and turn into a capture conflict.
internal static class MusketeerVoyageSaveDiagnostics
{
    private const int MaxCaptures = 4;
    private const int MaxBeginCareers = 32;
    private const int MaxObservedRoots = 4096;
    private const int MaxCareerLines = 8;
    private const int MaxLinesPerCapture = 10;
    private const int MaxProcessLines = 40;
    private const int MaxLineChars = 1536;
    private const int MaxProcessBytes = 64 * 1024;
    private const int MaxReadFaults = 64;
    private const string LogPrefix = "[VoyageSaveDiag] ";
    private const string LoggerPrefix = "I: ";

    private static int _captures, _lines, _bytes, _dropped;
    private static bool _capReported;

    internal sealed class Capture
    {
        internal MusketeerPersistence.SaveCapture Save;
        internal int Seq;
        internal int Lines;
        internal int CareerLines;
        internal string BaselinePhase = "none";
        internal string ContextKey;
        internal bool HeaderWritten;
        internal bool EndWritten;
        internal bool CareerTruncated;
        internal bool ObservationTruncated;
        internal bool ObservationIncomplete;
        internal int ReadFaults, BindIncomplete, MapIncomplete;
        internal bool LineTruncated;
        internal bool MissingSeen;
        internal readonly Dictionary<Guid, Snapshot> Begin = new();
        internal readonly Dictionary<IntPtr, Observation> Observed = new();
    }

    internal sealed class Snapshot
    {
        internal Guid Id;
        internal int Kind;
        internal IntPtr Root;
        internal int GoId;
        internal long Life, World;
        internal IntPtr Persist, PersistRoot;
        internal bool PersistPointerKnown, PersistRootKnown;
        internal bool? PersistRootMatch;
        internal bool? Registered, PersistObject, PersistInactive, PersistInstance, Active;
        internal bool? ShouldPersist, Eligible;
    }

    internal sealed class Observation
    {
        internal int Calls, Empty, Long, Valid;
        internal int BindAttempted, BindRejected, Mapped;
        internal Guid MappedCareer;
    }

    private sealed class Registration
    {
        internal Il2CppPersistentSet Set;
        internal bool Failed;
    }

    // Process budget: only real SailingAway Save entries consume a capture. A fifth entry is
    // reported once when the process log still has room, then ignored.
    internal static Capture TryOpen(MusketeerPersistence.SaveCapture save)
    {
        try
        {
            if (save == null || !IsSailingAway()) return null;
            if (_captures >= MaxCaptures)
            {
                _dropped++;
                if (!_capReported && _lines < MaxProcessLines)
                {
                    _capReported = true;
                    Write("cap reason=process-captures max=" + MaxCaptures + " skipped=" + _dropped);
                }
                return null;
            }
            return new Capture { Save = save, Seq = ++_captures };
        }
        catch { return null; }
    }

    // Called once from Begin (explicit land) or from the first GetID source resolution (-1).
    // Samples at most 32 careers purely in memory; the first actual missing is emitted later.
    internal static void BeginSnapshot(Capture capture, string phase)
    {
        if (capture == null || capture.BaselinePhase != "none") return;
        try
        {
            capture.BaselinePhase = phase;
            capture.ContextKey = capture.Save?.ContextKey;
            WriteHeader(capture);
            var state = StateOf(capture.ContextKey);
            if (state == null) return;
            var registered = ReadRegistered();
            int sampled = 0;
            foreach (var career in state.Careers)
            {
                if (career == null) continue;
                if (sampled >= MaxBeginCareers) { capture.CareerTruncated = true; break; }
                var snapshot = Take(career, registered);
                if (!capture.Begin.ContainsKey(snapshot.Id)) capture.Begin.Add(snapshot.Id, snapshot);
                sampled++;
            }
        }
        catch { }
    }

    // The missing branch hands over the exact career before its original refusal/log/return.
    internal static void Missing(Capture capture, MusketeerIdentity.Career career)
    {
        if (capture == null || career == null) return;
        try
        {
            if (capture.MissingSeen) return;
            capture.MissingSeen = true;
            WriteHeader(capture);
            var apply = Take(career, ReadRegistered());
            bool baselineKnown = capture.Begin.TryGetValue(career.Id, out var begin);
            string baselinePhase = baselineKnown ? capture.BaselinePhase
                : capture.CareerTruncated ? "career-cap"
                : capture.BaselinePhase == "none" ? "none" : "career-missing";
            if (baselineKnown && capture.CareerLines < MaxCareerLines
                && Emit(capture, CareerLine(capture, begin, "begin", null)))
                capture.CareerLines++;
            if (capture.CareerLines < MaxCareerLines
                && Emit(capture, CareerLine(capture, apply, "apply",
                    " baselineKnown=" + B(baselineKnown) + " baselinePhase=" + baselinePhase
                    + ObservationText(capture, apply))))
                capture.CareerLines++;
        }
        catch { }
    }

    // Exact-root GetID postfix observation. Empty/over-long ids are classified, but they never
    // trigger an extra native call; a real id here is the only thing that can reach BoundAt.
    // The returned token belongs to this exact callback; bind/mapped consume it without any
    // second root lookup, so a later read fault cannot hide a callback that was already seen.
    internal static Observation ObserveGetId(Capture capture, Persistent persistent, string id)
    {
        if (capture == null) return null;
        try
        {
            if (persistent == null) { MarkReadFault(capture); return null; }
            IntPtr root;
            try
            {
                var go = persistent.gameObject;
                if (go == null) { MarkReadFault(capture); return null; }
                root = go.Pointer;
            }
            catch { MarkReadFault(capture); return null; }
            if (root == IntPtr.Zero) { MarkReadFault(capture); return null; }
            if (!capture.Observed.TryGetValue(root, out var observation))
            {
                if (capture.Observed.Count >= MaxObservedRoots) { capture.ObservationTruncated = true; return null; }
                observation = new Observation();
                capture.Observed.Add(root, observation);
            }
            observation.Calls++;
            if (string.IsNullOrEmpty(id)) { observation.Empty++; return observation; }
            if (id.Length > MusketeerArchive.MaxNativeIdLength) observation.Long++;
            else observation.Valid++;
            return observation;
        }
        catch { MarkReadFault(capture); return null; }
    }

    internal static void ObserveBind(Capture capture, Observation observation, MusketeerIdentity.Career career)
    {
        if (capture == null) return;
        try
        {
            if (observation == null)
            {
                if (capture.BindIncomplete < MaxReadFaults) capture.BindIncomplete++;
                return;
            }
            observation.BindAttempted++;
            if (career == null) observation.BindRejected++;
        }
        catch { }
    }

    internal static void ObserveMapped(Capture capture, Observation observation, MusketeerIdentity.Career career)
    {
        if (capture == null) return;
        try
        {
            if (observation == null)
            {
                if (capture.MapIncomplete < MaxReadFaults) capture.MapIncomplete++;
                return;
            }
            observation.Mapped++;
            if (career != null) observation.MappedCareer = career.Id;
        }
        catch { }
    }

    private static void MarkReadFault(Capture capture)
    {
        if (capture == null) return;
        capture.ObservationIncomplete = true;
        if (capture.ReadFaults < MaxReadFaults) capture.ReadFaults++;
    }

    // Always invoked by the existing Save finalizer. It emits the reserved end row even when the
    // original method threw or the capture never resolved a source.
    internal static void End(Capture capture, Exception error)
    {
        if (capture == null || capture.EndWritten) return;
        capture.EndWritten = true;
        try
        {
            WriteHeader(capture);
            var save = capture.Save;
            int calls = 0, empty = 0, longIds = 0, valid = 0, attempted = 0, rejected = 0, mapped = 0;
            foreach (var observation in capture.Observed.Values)
            {
                calls += observation.Calls; empty += observation.Empty; longIds += observation.Long;
                valid += observation.Valid; attempted += observation.BindAttempted;
                rejected += observation.BindRejected; mapped += observation.Mapped;
            }
            var truncated = new StringBuilder();
            if (capture.CareerTruncated) truncated.Append("career,");
            if (capture.ObservationTruncated) truncated.Append("observation,");
            if (capture.ObservationIncomplete) truncated.Append("incomplete,");
            if (capture.LineTruncated) truncated.Append("line,");
            string line = "save-end capture=" + capture.Seq
                + " applied=" + B(save != null && save.Applied)
                + " conflict=" + B(save != null && save.Conflict)
                + " missing=" + B(capture.MissingSeen)
                + " protected=" + B(save != null && save.Protected)
                + " ids=" + IdCount(save)
                + " observedRoots=" + capture.Observed.Count
                + " idCalls=" + calls + " idValid=" + valid + " idEmpty=" + empty + " idLong=" + longIds
                + " bindAttempted=" + attempted + " bindRejected=" + rejected + " mapped=" + mapped
                + " incomplete=" + B(capture.ObservationIncomplete)
                + " readFaults=" + capture.ReadFaults
                + " bindIncomplete=" + capture.BindIncomplete
                + " mapIncomplete=" + capture.MapIncomplete
                + " truncated=" + (truncated.Length == 0 ? "none" : truncated.ToString(0, truncated.Length - 1))
                + " dropped=" + _dropped
                + " exc=" + (error == null ? "none" : error.GetType().Name);
            if (capture.Lines < MaxLinesPerCapture)
            {
                if (Write(line)) capture.Lines++;
                else capture.LineTruncated = true;
            }
            else { capture.LineTruncated = true; _dropped++; }
        }
        catch { }
    }

    private static Snapshot Take(MusketeerIdentity.Career career, Registration registered)
    {
        var snapshot = new Snapshot { Id = career.Id, Kind = career.Kind };
        try
        {
            var slot = career.Slot;
            if (slot != null)
            {
                snapshot.Root = slot.Pointer; snapshot.GoId = slot.GoId;
                snapshot.Life = slot.Life; snapshot.World = slot.World;
            }
            if (snapshot.World == 0)
            {
                try { snapshot.World = career.State != null ? career.State.World : 0; } catch { }
            }
            GameObject go = null;
            try
            {
                go = career.Kind == MusketeerCareer.KindUnit
                    ? (career.Character != null ? career.Character.gameObject : null)
                    : (career.Tool != null ? career.Tool.gameObject : null);
            }
            catch { }
            Persistent persistent = null;
            try { persistent = go != null ? go.GetComponent<Persistent>() : null; } catch { }
            if (persistent != null)
            {
                try
                {
                    snapshot.Persist = persistent.Pointer;
                    snapshot.PersistPointerKnown = snapshot.Persist != IntPtr.Zero;
                }
                catch { }
                bool rootRead = false;
                IntPtr persistRoot = IntPtr.Zero;
                try
                {
                    var root = persistent.gameObject;
                    if (root != null) { persistRoot = root.Pointer; rootRead = true; }
                }
                catch { rootRead = false; }
                snapshot.PersistRoot = persistRoot;
                snapshot.PersistRootKnown = rootRead && persistRoot != IntPtr.Zero;
                // A missing/destroyed component or a failed read is unknown. Only a successful
                // read of two real pointers may prove match (true) or mismatch (false).
                if (snapshot.PersistRootKnown && snapshot.Root != IntPtr.Zero)
                    snapshot.PersistRootMatch = persistRoot == snapshot.Root;
            }
            if (go != null) snapshot.Active = ReadBool(() => go.activeInHierarchy);
            // Only the exact root's Persistent may speak for native eligibility. A component found
            // on a different object, missing, or unreadable stays unknown instead of guessing.
            if (snapshot.PersistRootMatch != true || persistent == null) return snapshot;
            snapshot.Registered = ReadContains(registered, persistent);
            snapshot.PersistObject = ReadBool(() => persistent.persistObject);
            snapshot.PersistInactive = ReadBool(() => persistent.persistInactive);
            snapshot.PersistInstance = ReadBool(() => persistent._persistInstance);
            if (snapshot.PersistObject.HasValue && snapshot.PersistInstance.HasValue
                && snapshot.Active.HasValue && snapshot.PersistInactive.HasValue)
                snapshot.ShouldPersist = snapshot.PersistObject.Value && snapshot.PersistInstance.Value
                    && (snapshot.Active.Value || snapshot.PersistInactive.Value);
            if (snapshot.Registered.HasValue && snapshot.ShouldPersist.HasValue)
                snapshot.Eligible = snapshot.Registered.Value && snapshot.ShouldPersist.Value;
        }
        catch { }
        return snapshot;
    }

    private static string CareerLine(Capture capture, Snapshot snapshot, string phase, string suffix)
        => "career=" + snapshot.Id.ToString("N") + " phase=" + phase + " capture=" + capture.Seq
            + " kind=" + KindText(snapshot.Kind)
            + " root=" + Hex(snapshot.Root) + " goid=" + snapshot.GoId
            + " life=" + snapshot.Life + " world=" + snapshot.World
            + " slot=" + (snapshot.Root == IntPtr.Zero ? "reserved" : Hex(snapshot.Root))
            + " persist=" + (snapshot.PersistPointerKnown ? Hex(snapshot.Persist) : "unknown")
            + " persistRoot=" + (snapshot.PersistRootKnown ? Hex(snapshot.PersistRoot) : "unknown")
            + " persistRootMatch=" + Tri(snapshot.PersistRootMatch)
            + " reg=" + Tri(snapshot.Registered) + " pobj=" + Tri(snapshot.PersistObject)
            + " pinactive=" + Tri(snapshot.PersistInactive) + " pinstance=" + Tri(snapshot.PersistInstance)
            + " active=" + Tri(snapshot.Active) + " should=" + Tri(snapshot.ShouldPersist)
            + " sourceEligible=" + Tri(snapshot.Eligible) + (suffix ?? "");

    private static string ObservationText(Capture capture, Snapshot snapshot)
    {
        try
        {
            if (snapshot.Root == IntPtr.Zero) return " getid=unknown:no-root";
            if (capture.Observed.TryGetValue(snapshot.Root, out var observation))
                return " getid=observed idCalls=" + observation.Calls + " idValid=" + observation.Valid
                    + " idEmpty=" + observation.Empty + " idLong=" + observation.Long
                    + " bindAttempted=" + observation.BindAttempted
                    + " bindRejected=" + observation.BindRejected
                    + (observation.BindAttempted > 0 ? " bindReason=unknown" : "")
                    + " mapped=" + observation.Mapped
                    + " mappedCareer=" + (observation.MappedCareer == Guid.Empty
                        ? "-" : observation.MappedCareer.ToString("N"));
            if (capture.ObservationIncomplete) return " getid=unknown:read-fault";
            if (capture.ObservationTruncated) return " getid=unknown:observation-budget";
            return " getid=not-observed";
        }
        catch { return " getid=unknown"; }
    }

    private static void WriteHeader(Capture capture)
    {
        if (capture == null || capture.HeaderWritten) return;
        capture.HeaderWritten = true;
        try
        {
            var save = capture.Save;
            var state = StateOf(capture.ContextKey);
            int careers = state != null ? state.Careers.Count : 0;
            string line = "save capture=" + capture.Seq + " phase=" + capture.BaselinePhase
                + " frame=" + Frame()
                + " campaign=" + (save != null ? save.Campaign : 0)
                + " land=" + (save != null ? save.Land : -1)
                + " challenge=" + (save != null ? save.Challenge : 0)
                + " global=" + Hex(save != null ? save.GlobalPointer : IntPtr.Zero)
                + " owner=" + Hex(save != null ? save.OwnerPointer : IntPtr.Zero)
                + " file=" + SafeText(save != null ? save.File : null, 48)
                + " island=" + IslandPointer(save)
                + " slot=" + (save != null ? save.Slot : -1)
                + " context=" + SafeText(capture.ContextKey, 80)
                + " epoch=" + SafeText(state != null ? state.Epoch : null, 40)
                + " parent=" + ParentSequence(save)
                + " protected=" + B(save != null && save.Protected)
                + " careers=" + careers;
            Emit(capture, line);
        }
        catch { }
    }

    private static Registration ReadRegistered()
    {
        try
        {
            var set = IslandSaveData.registeredPersistents;
            return new Registration { Set = set, Failed = set == null };
        }
        catch { return new Registration { Failed = true }; }
    }

    private static bool? ReadContains(Registration registered, Persistent persistent)
    {
        if (registered == null || registered.Failed || registered.Set == null) return null;
        try { return registered.Set.Contains(persistent); } catch { return null; }
    }

    private static bool? ReadBool(Func<bool> read)
    {
        try { return read(); } catch { return null; }
    }

    private static int IdCount(MusketeerPersistence.SaveCapture save)
    {
        try { return save != null ? save.DiagIdCount : -1; } catch { return -1; }
    }

    private static string IslandPointer(MusketeerPersistence.SaveCapture save)
    {
        try { return Hex(save != null && save.Island != null ? save.Island.Pointer : IntPtr.Zero); }
        catch { return "unknown"; }
    }

    private static string ParentSequence(MusketeerPersistence.SaveCapture save)
    {
        try
        {
            return save != null && save.Previous != null && save.Previous.Diag != null
                ? save.Previous.Diag.Seq.ToString() : "none";
        }
        catch { return "unknown"; }
    }

    private static MusketeerIdentity.IslandState StateOf(string contextKey)
    {
        if (contextKey == null) return null;
        return MusketeerIdentity.Islands.TryGetValue(contextKey, out var state) ? state : null;
    }

    private static bool IsSailingAway()
    {
        try
        {
            var managers = Managers.Inst;
            return managers != null && managers.game != null && managers.game.state == Game.State.SailingAway;
        }
        catch { return false; }
    }

    private static int Frame()
    {
        try { return Time.frameCount; } catch { return -1; }
    }

    private static bool Emit(Capture capture, string body)
    {
        if (capture == null) return false;
        if (capture.Lines >= MaxLinesPerCapture) { capture.LineTruncated = true; _dropped++; return false; }
        if (Write(body)) { capture.Lines++; return true; }
        capture.LineTruncated = true;
        return false;
    }

    private static bool Write(string body)
    {
        if (_lines >= MaxProcessLines) { _dropped++; return false; }
        string line = LogPrefix + body;
        int limit = MaxLineChars - LoggerPrefix.Length;
        if (line.Length > limit) line = line.Substring(0, limit - 8) + " trunc=1";
        int bytes = Encoding.UTF8.GetByteCount(LoggerPrefix + line) + 1;
        if (_bytes + bytes > MaxProcessBytes) { _dropped++; return false; }
        _lines++; _bytes += bytes;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo(line); }
        catch { }
        return true;
    }

    private static string KindText(int kind)
        => kind == MusketeerCareer.KindUnit ? "unit" : kind == MusketeerCareer.KindGun ? "gun" : "kind-" + kind;

    private static string B(bool value) => value ? "1" : "0";
    private static string Tri(bool? value) => value.HasValue ? (value.Value ? "1" : "0") : "unknown";
    private static string Hex(IntPtr value) => "0x" + value.ToInt64().ToString("X");

    private static string SafeText(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "unknown";
        var text = new StringBuilder(Math.Min(value.Length, max));
        foreach (char ch in value)
        {
            if (text.Length >= max) break;
            text.Append(char.IsControl(ch) ? '?' : ch);
        }
        return text.Length == 0 ? "unknown" : text.ToString();
    }
}
#endif
