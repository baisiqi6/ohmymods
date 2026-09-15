using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    /// <summary>Test double for BepInEx logging: captures everything the production module logs.</summary>
    public sealed class ManualLogSource
    {
        public readonly List<string> Messages = new List<string>();

        public void LogWarning(string message) { Messages.Add("W: " + message); }
        public void LogInfo(string message) { Messages.Add("I: " + message); }
        public void LogError(string message) { Messages.Add("E: " + message); }
    }

    /// <summary>Test double for the plugin singleton used by production logging.</summary>
    public sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance;

        public readonly ManualLogSource LogSource = new ManualLogSource();
    }

    internal static class MockLog
    {
        internal static ManualLogSource Source
        {
            get
            {
                if (KingdomEnhancedPlugin.Instance == null)
                {
                    KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
                }
                return KingdomEnhancedPlugin.Instance.LogSource;
            }
        }

        internal static void Clear() { Source.Messages.Clear(); }

        internal static int CountMatching(string fragment)
        {
            int count = 0;
            var messages = Source.Messages;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].IndexOf(fragment, System.StringComparison.Ordinal) >= 0) count++;
            }
            return count;
        }
    }
}
