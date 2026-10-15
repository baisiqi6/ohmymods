// Boundary for identity-only suites: stock/visual/native shop code is exercised separately
// by heavy-shield-shop-runtime and the isolated native candidate. This does not validate transit.
namespace KingdomEnhancedMod;
internal sealed class CustomShopStocks
{
    internal static readonly CustomShopStocks Musket = new(), Shield = new();
    internal string Fault => "disabled stock boundary";
    internal bool ReleaseAll() => true;
    internal bool ReleaseRoot(object root) => true;
    internal bool Capture(object career, object layer) => true;
}
