// Program.cs — runs every shop-cleanup regression case sequentially (shared static state is
// reset per case; see Runner). Optional args filter cases by name substring, e.g.
// `dotnet run --project Regression.csproj -- backoff`.
namespace ShopCleanupTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            SlotCleanupTests.Register();
            MarkerTests.Register();
            return Runner.Run(args);
        }
    }
}
