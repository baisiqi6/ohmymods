// Runtime-effects tests explicitly supply purchase receipts. Persistence and
// actual payment are tested in hero-recruitment / hero-shop, not simulated here.
using System.Collections.Generic;
namespace KingdomEnhancedMod;
internal static class HeroArcherTowerPolicy { internal static void Observe(Archer archer) { } }
internal static class HeroRecruitment
{
    private static readonly Dictionary<Archer, int> Purchases = new();
    internal static void Grant(Archer archer) => Purchases[archer] = (int)archer.side;
    internal static void Revoke(Archer archer) => Purchases.Remove(archer);
    internal static void Reset() => Purchases.Clear();
    internal static void Observe(Archer archer) { }
    internal static void OnEnable(Archer archer) { }
    internal static bool IsPurchased(Archer archer) => archer != null && Purchases.ContainsKey(archer);
    internal static int SeatSide(Archer archer) => archer != null && Purchases.TryGetValue(archer, out int side) ? side : 0;
}
