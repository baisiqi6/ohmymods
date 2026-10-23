#!/usr/bin/env python3
"""Extract the actual pre/post/finalizer and CourtArea geometry methods verbatim.

Only access modifiers are widened for host calls. All control flow, identity checks and
validator calls stay product text; the host supplies native-boundary adapters.
"""
from pathlib import Path
import hashlib, json, sys

w = Path(sys.argv[1])
output = Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)


def block(text, marker):
    start = text.index(marker)
    brace = text.index('{', start)
    depth = 0
    i = brace
    while i < len(text):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
        i += 1
    raise SystemExit('unbalanced: ' + marker)


def method(path, marker, widen=True):
    text = (w / path).read_text()
    start = text.index(marker)
    snippet = text[start:start + 600]
    arrow = snippet.find('=>')
    brace = snippet.find('{')
    if arrow != -1 and (brace == -1 or arrow < brace):
        end = text.index(';', start + arrow)
        body = text[start:end + 1]
    else:
        body = block(text, marker)
    return body.replace('private static', 'internal static', 1) if widen else body


bootstrap = 'Patch_PalaceClockBootstrap.cs'
court = 'Patch_PalaceCourtArea.cs'
pop_class = block((w / bootstrap).read_text(), 'private sealed class ColdPopCall').replace(
    'private sealed class ColdPopCall', 'internal sealed class ColdPopCall', 1)

text = '#nullable enable annotations\nusing System;\nusing System.Collections.Generic;\nusing System.Security.Cryptography;\nusing System.Text;\n\nnamespace KingdomEnhancedMod;\n\n'
text += pop_class + '\n\n'
text += 'internal static partial class BootstrapProbe\n{\n'
text += method(bootstrap, '    private static void PreparePopPrefix(') + '\n'
text += method(bootstrap, '    private static bool TryGeometryLayout(') + '\n'
text += method(bootstrap, '    private static void PreparePopPostfix(') + '\n'
text += method(bootstrap, '    private static void PreparePopFinalizer(') + '\n'
text += '}\n\n'
text += 'internal static partial class PalaceCourtArea\n{\n'
text += method(court, '    internal static long BeginSavedGeometryCall(', widen=False) + '\n'
text += method(court, '    internal static void InvalidateColdGeometryForPopAttempt(', widen=False) + '\n'
text += method(court, '    internal static PalaceSavedGeometryCandidate CaptureSavedGeometry(', widen=False) + '\n'
text += method(court, '    internal static bool TryPublishSavedGeometry(', widen=False) + '\n'
text += method(court, '    private static bool TryAcceptColdGeometry(') + '\n'
text += method(court, '    private static bool LayoutIdentityMatches(') + '\n'
text += method(court, '    internal static bool TryGetLayout(') + '\n'
text += method(court, '    private static bool WorldIdentityMatches(') + '\n'
text += method(court, '    private static bool TripLayoutMatches(') + '\n'
text += '}\n'
(output / 'Extracted.cs').write_text(text)
(output / 'extraction.json').write_text(json.dumps({
    'source': {p: hashlib.sha256((w / p).read_bytes()).hexdigest() for p in (bootstrap, court, 'PalaceColdGeometry.cs')},
    'methods': ['PreparePopPrefix', 'TryGeometryLayout', 'PreparePopPostfix', 'PreparePopFinalizer',
                'BeginSavedGeometryCall', 'CaptureSavedGeometry', 'TryPublishSavedGeometry',
                'TryAcceptColdGeometry', 'TripLayoutMatches'],
}, indent=2) + '\n')
