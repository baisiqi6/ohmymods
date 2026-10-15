// Mod-side boundary fixtures for the source-linked ANDROID Banner suite.
// Same contract as NativeFixtures.cs: each member counts the access and throws.
using System;

namespace KingdomEnhancedMod
{
    internal static class HeavyShieldIdentity
    {
        internal sealed class Session { internal int Land; internal long World; }
        internal static int Reads;
        private static void Touch(string what)
        { Reads++; throw new InvalidOperationException("native read: HeavyShieldIdentity." + what); }
        internal static Session Current { get { Touch("Current"); return null; } }
        internal static bool ValidateCampaign(in HeavyShieldCampaignToken token) { Touch("ValidateCampaign"); return false; }
        internal static bool TryGetCampaign(out HeavyShieldCampaignToken token) { Touch("TryGetCampaign"); token = default; return false; }
    }

    internal static class HeavyShieldRuntime
    {
        internal static int Reads;
        internal static bool Enabled
        { get { Reads++; throw new InvalidOperationException("native read: HeavyShieldRuntime.Enabled"); } }
    }

    internal class KingdomEnhancedPlugin
    {
        internal static int LogCalls;
        internal static KingdomEnhancedPlugin Instance = new();
        internal Logger LogSource = new();
        internal class Logger
        {
            internal int Calls;
            internal void LogWarning(string message) { Calls++; LogCalls++; }
        }
    }
}
