// Test double for the loader logger. The adapter sources compiled into this test project
// (FloatLayout, MobilePlayerConfig, EnemyStubs' Logger bridge) call MelonLogger.Msg/Warning/Error;
// the stub records the last line and the number of warnings so the settings-ready line and the
// non-finite load-boundary warning can be asserted without a loader runtime.
// Save-failure observability lives in the real loader (SaveToFile logs the exception itself),
// so it is proven by IL evidence, not by this stub.
namespace MelonLoader
{
    internal static class MelonLogger
    {
        internal static string LastMessage;
        internal static string LastWarning;
        internal static string LastError;
        internal static int WarningCalls;
        internal static void Msg(string message) => LastMessage = message;
        internal static void Warning(string message) { LastWarning = message; WarningCalls++; }
        internal static void Error(string message) => LastError = message;
    }
}
