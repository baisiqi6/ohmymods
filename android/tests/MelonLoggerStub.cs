// Test double for the loader logger. The adapter sources compiled into this test project
// (FloatLayout, MobilePlayerConfig) only call MelonLogger.Msg; the stub records the last line
// so the settings-ready and switch logging can be asserted without a loader runtime.
// Save-failure observability lives in the real loader (SaveToFile logs the exception itself),
// so it is proven by IL evidence, not by this stub.
namespace MelonLoader
{
    internal static class MelonLogger
    {
        internal static string LastMessage;
        internal static void Msg(string message) => LastMessage = message;
    }
}
