using System;
using System.IO;
using System.Security.Cryptography;

namespace KingdomEnhancedMod;

// Pure copy elimination for the three canonical island fingerprints:
//   HeroRecruitmentFingerprint.Hash       (hero-island-objects-v2)
//   MusketeerArchive.IslandHash           (musketeer-island-objects-v2)
//   HeavyShieldSnapshotFingerprint.Hash   (heavy-shield-island-objects-v3)
//
// Each of them serializes the canonical island object into a local default MemoryStream,
// disposes the Utf8JsonWriter (complete buffer committed), then appends the whole thing to
// an IncrementalHash. `stream.ToArray()` clones the entire buffer first — one full canonical
// JSON per hash in LOH/Gen2 traffic; `GetBuffer()` exposes the same committed bytes without
// a copy. Both IncrementalHash.AppendData overloads consume identical byte sequences, so the
// digest is unchanged (proved by the source-linked tests in puretests/).
//
// Preconditions, exactly the three call sites as written:
//   * stream is `new MemoryStream()` (default constructor -> GetBuffer() is usable and
//     origin is 0; a non-exposable MemoryStream throws UnauthorizedAccessException —
//     fail-closed, and all three call sites are default-constructor streams. If a future
//     call site cannot expose its buffer, keep the old ToArray() copy there),
//   * the writer has been disposed so the buffer holds the complete canonical JSON,
//   * only the committed range is read: buffer[0 .. Length], never capacity (a reused or
//     pre-sized stream leaves stale bytes past Length),
//   * the span is consumed synchronously by the crypto call and never escapes.
// `checked` is pure defence: MemoryStream.Length is already an int, so it cannot overflow
// today; it keeps the helper honest if the stream type ever changes.
internal static class CanonicalHashBuffer
{
    internal static void AppendTo(IncrementalHash hash, MemoryStream stream)
    {
        hash.AppendData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
