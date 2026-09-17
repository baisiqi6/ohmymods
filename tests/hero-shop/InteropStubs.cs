namespace KingdomEnhancedMod;
internal static class ModConfig { internal sealed class Entry { internal bool Value=false; } internal static Entry Enabled=new(), HeroArcherEnabled=new(); }
internal enum HeroShopSeatState { Unavailable, Available, Occupied, Reserved }
internal static class HeroRecruitment { internal static HeroShopSeatState GetShopSeatState(int side) => HeroShopSeatState.Unavailable; internal static bool HasFallenSeat(int side) => false; internal static bool CanPurchase => false; internal static string StatusText => "stub"; internal static bool TryPurchase(out string reason) { reason="stub"; return false; } }
internal class KingdomEnhancedPlugin { internal static KingdomEnhancedPlugin Instance=null; internal Log LogSource=null; }
internal class Log { internal void LogInfo(string message) { } internal void LogWarning(string message) { } }
