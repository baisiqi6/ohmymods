using System;
using System.IO;
using System.Security.Cryptography;

namespace KingdomEnhancedMod;

// Test-support copies so the production il2cpp/MusketeerArchive.cs compiles unmodified in this
// pure offline suite (same pattern as tests/musketeer-legacy-archive and the approved
// ledger-retirement-review repro). CanonicalHashBuffer links the production source; KingdomEnhancedPlugin/Logger only satisfy the
// bounded diagnostic logger used by MusketeerArchiveLog. No Unity, no game, no native calls.
internal sealed class KingdomEnhancedPlugin
{
    internal static KingdomEnhancedPlugin Instance => null;
    internal Logger LogSource => null;
}

internal sealed class Logger
{
    internal void LogInfo(object message) { }
    internal void LogWarning(object message) { }
    internal void LogError(object message) { }
}
