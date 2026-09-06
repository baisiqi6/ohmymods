using KingdomEnhancedMod;
using CycleData;

int assertions = 0;
void Check(bool condition, string name)
{
    assertions++;
    if (!condition) throw new Exception(name);
}
SeasonData S(Season season, int days) => new()
{
    season = season, cycleData = new() { new() { numRegularDays = days } }
};
YearData Y(int spring, int summer, int autumn, int winter) => new()
{
    springData = S(Season.Spring, spring), summerData = S(Season.Summer, summer),
    autumnData = S(Season.Autumn, autumn), winterData = S(Season.Winter, winter)
};
Director D(int day, params YearData[] years) => new()
{
    TotalDaysInReign = day, CurrentSeasonDay = day, currentTime = 14.99f,
    LandCycleData = new() { yearData = years.ToList() }
};
CalendarSnapshot Read(Director director)
{
    Check(CalendarReader.TryRead(director, out var value), "valid snapshot");
    return value;
}

var first = Read(D(1, Y(16, 16, 16, 16)));
Check(first.TotalDay == 1 && first.SeasonDay == 1 && first.Hour == 14 &&
    first.NextSeasonDay == 16 && first.CurrentSeason == Season.Spring, "first playable day");
var boundary = Read(D(16, Y(16, 16, 16, 16)));
Check(boundary.CurrentSeason == Season.Summer && boundary.SeasonDay == 1 &&
    boundary.NextSeasonDay == 32, "exact boundary is new season day one");
var yearBoundary = Read(D(64, Y(16, 16, 16, 16)));
Check(yearBoundary.CurrentSeason == Season.Spring && yearBoundary.SeasonDay == 1 &&
    yearBoundary.NextSeasonDay == 80, "repeating year boundary");
var absent = Read(D(6, Y(5, 0, 9, 3)));
Check(absent.CurrentSeason == Season.Autumn && absent.SeasonDay == 2 && absent.NextSeasonDay == 14,
    "zero-length summer omitted");
var infinite = Read(D(1000000000, Y(7, 7, 7, 7), Y(0, 0, 0, 11)));
Check(infinite.CurrentSeason == Season.Winter && !infinite.HasNextSeason &&
    infinite.SeasonDay == 1000000000 - 21 + 1, "endless winter merges preceding winter");
Check(!CalendarReader.TryRead(D(200, Y(0, 0, 0, 0)), out _), "zero-length repeat fails closed");
Check(!CalendarReader.TryRead(D(1, Y(-1, 2, 3, 4)), out _), "negative cycle fails closed");
Check(!CalendarReader.TryRead(D(1, Y(int.MaxValue, 1, 0, 0)), out _), "overflow fails closed");
var invalid = D(1, Y(3, 3, 3, 3));
invalid.currentTime = float.NaN;
Check(!CalendarReader.TryRead(invalid, out _), "invalid hour fails closed");
invalid.currentTime = 0.9f;
Check(Read(invalid).Hour == 0, "hour floors after midnight");
invalid.currentTime = 23.999f;
Check(Read(invalid).Hour == 23, "hour floors before midnight");
invalid.currentTime = 24f;
Check(Read(invalid).Hour == 24, "exact native rollover remains native hour");
var shifted = D(6, Y(5, 3, 2, 4));
shifted.TotalDaysInReign = 106;
Check(Read(shifted).NextSeasonDay == 108, "schedule coordinate mapped to total day");

// Independent finite day-by-day oracle expands authored years and last-year repeats.
// This checks all boundaries and merged same-season runs without using reader formulas.
var random = new Random(809);
for (int scenario = 0; scenario < 80; scenario++)
{
    var years = new List<YearData>();
    var calendars = new List<List<Season>>();
    int yearCount = random.Next(1, 5);
    for (int y = 0; y < yearCount; y++)
    {
        int[] lengths = { random.Next(0, 8), random.Next(0, 8), random.Next(0, 8), random.Next(1, 8) };
        years.Add(Y(lengths[0], lengths[1], lengths[2], lengths[3]));
        Season[] names = { Season.Spring, Season.Summer, Season.Autumn, Season.Winter };
        var days = new List<Season>();
        for (int s = 0; s < 4; s++)
            for (int d = 0; d < lengths[s]; d++) days.Add(names[s]);
        calendars.Add(days);
    }
    var oracle = new List<Season>();
    foreach (var year in calendars) oracle.AddRange(year);
    while (oracle.Count < 1000) oracle.AddRange(calendars[^1]);
    for (int day = 1; day < 300; day++)
    {
        var actual = Read(D(day, years.ToArray()));
        int start = day;
        for (; start > 1 && oracle[start - 1] == oracle[day]; start--) { }
        int next = day + 1;
        for (; next < oracle.Count && oracle[next] == oracle[day]; next++) { }
        Check(actual.CurrentSeason == oracle[day] && actual.SeasonDay == day - start + 1,
            $"oracle day/current season {scenario}/{day}");
        Check(actual.HasNextSeason == (next < oracle.Count), $"oracle next present {scenario}/{day}");
        if (actual.HasNextSeason)
            Check(actual.NextSeasonDay == next && actual.NextSeason == oracle[next],
                $"oracle transition {scenario}/{day}");
    }
}
Console.WriteLine($"Calendar tests passed: {assertions} assertions.");
