// Minimal data-only contract, so the production reader runs without loading Unity/IL2CPP.
public enum Season { Summer = 1, Autumn = 2, Winter = 4, Spring = 8 }
public class Director
{
    public int TotalDaysInReign, CurrentSeasonDay;
    public float currentTime;
    public CycleData.LandData LandCycleData;
}
namespace CycleData
{
    public class LandData { public List<YearData> yearData = new(); }
    public class YearData { public SeasonData springData, summerData, autumnData, winterData; }
    public class SeasonData { public Season season; public List<CycleData> cycleData = new(); }
    public class CycleData { public int numRegularDays, numBossDays, numRecoveryDays; }
}
