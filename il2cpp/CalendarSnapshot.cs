using System;
using System.Collections.Generic;
using CycleData;

namespace KingdomEnhancedMod;

internal readonly struct CalendarSnapshot
{
    public readonly int TotalDay, Hour, SeasonDay, NextSeasonDay;
    public readonly Season CurrentSeason, NextSeason;
    public readonly float Progress;
    public readonly bool HasNextSeason;

    internal CalendarSnapshot(int totalDay, int hour, int seasonDay, int nextSeasonDay,
        Season currentSeason, Season nextSeason, float progress, bool hasNextSeason)
    {
        TotalDay = totalDay;
        Hour = hour;
        SeasonDay = seasonDay;
        NextSeasonDay = nextSeasonDay;
        CurrentSeason = currentSeason;
        NextSeason = nextSeason;
        Progress = progress;
        HasNextSeason = hasNextSeason;
    }
}

/// <summary>
/// Read-only calendar: finite authored years followed by repetitions of the last year,
/// matching LandData.GetYearDataForDay without its potentially unbounded while loop.
/// Zero-length seasons are skipped; malformed/oversized tables fail closed without logging.
/// </summary>
internal static class CalendarReader
{
    private const int MaxYears = 64;
    private const int MaxCyclesPerSeason = 64;
    private const int MaxTotalCycles = 4096;

    private readonly struct Segment
    {
        internal readonly long Start, End;
        internal readonly Season Season;
        internal Segment(long start, long end, Season season)
        {
            Start = start;
            End = end;
            Season = season;
        }
    }

    internal static bool TryRead(Director director, out CalendarSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            if (director == null) return false;
            int totalDay = director.TotalDaysInReign;
            int day = director.CurrentSeasonDay;
            float time = director.currentTime;
            if (totalDay < 1 || day < 1 || !float.IsFinite(time) || time < 0f || time > 24f)
                return false;

            LandData land = director.LandCycleData;
            if (land == null || land.yearData == null) return false;
            int yearCount = land.yearData.Count;
            if (yearCount < 1 || yearCount > MaxYears) return false;
            var segments = new List<Segment>(yearCount * 4);
            long cursor = 0;
            long repeatStart = 0;
            int repeatIndex = 0;
            int cyclesRead = 0;
            for (int i = 0; i < yearCount; i++)
            {
                YearData year = land.yearData[i];
                if (year == null) return false;
                long yearStart = cursor;
                if (i == yearCount - 1)
                {
                    repeatStart = cursor;
                    repeatIndex = segments.Count;
                }
                // The native year traversal uses this order, not enum numeric order.
                if (!Append(year.springData, segments, ref cursor, ref cyclesRead) ||
                    !Append(year.summerData, segments, ref cursor, ref cyclesRead) ||
                    !Append(year.autumnData, segments, ref cursor, ref cyclesRead) ||
                    !Append(year.winterData, segments, ref cursor, ref cyclesRead) ||
                    cursor == yearStart) return false;
            }

            long repeatLength = cursor - repeatStart;
            bool uniformRepeat = true;
            Season repeatSeason = segments[repeatIndex].Season;
            for (int i = repeatIndex + 1; i < segments.Count; i++)
                uniformRepeat &= segments[i].Season == repeatSeason;

            Segment current = Locate(day, segments, repeatIndex, repeatStart, repeatLength);
            long start = current.Start;
            // One-season final years represent an endless season, not a new season every year.
            if (uniformRepeat && start >= repeatStart) start = repeatStart;
            for (int i = 0; start > 0 && i <= segments.Count; i++)
            {
                Segment previous = Locate(start - 1, segments, repeatIndex, repeatStart, repeatLength);
                if (previous.Season != current.Season) break;
                start = previous.Start;
            }

            long end = current.End;
            Season next = current.Season;
            bool hasNext = false;
            for (int i = 0; i <= segments.Count; i++)
            {
                if (uniformRepeat && end >= repeatStart && current.Season == repeatSeason) break;
                Segment following = Locate(end, segments, repeatIndex, repeatStart, repeatLength);
                if (following.Season != current.Season)
                {
                    next = following.Season;
                    hasNext = true;
                    break;
                }
                end = following.End;
            }

            // Native schedule begins at zero, while the first playable day is one.
            long displayStart = Math.Max(1L, start);
            long seasonDay = day - displayStart + 1;
            long nextTotalDay = (long)totalDay + end - day;
            if (seasonDay < 1 || seasonDay > int.MaxValue) return false;
            if (hasNext && (nextTotalDay <= totalDay || nextTotalDay > int.MaxValue)) return false;
            float progress = hasNext
                ? (float)Math.Clamp((day - displayStart + time / 24d) / Math.Max(1, end - displayStart), 0d, 1d)
                : 0f;
            // Preserve the native hour; Director handles the exact-24 midnight rollover.
            int hour = (int)Math.Floor(time);
            snapshot = new CalendarSnapshot(totalDay, hour, (int)seasonDay,
                hasNext ? (int)nextTotalDay : 0, current.Season, next, progress, hasNext);
            return true;
        }
        catch
        {
            // Scene transitions can invalidate IL2CPP objects between property reads.
            return false;
        }
    }

    private static bool Append(SeasonData season, List<Segment> segments, ref long cursor, ref int cyclesRead)
    {
        if (season == null || season.cycleData == null) return false;
        if (season.season != Season.Spring && season.season != Season.Summer &&
            season.season != Season.Autumn && season.season != Season.Winter) return false;
        int count = season.cycleData.Count;
        if (count > MaxCyclesPerSeason || cyclesRead + count > MaxTotalCycles) return false;
        cyclesRead += count;
        long length = 0;
        for (int i = 0; i < count; i++)
        {
            CycleData.CycleData cycle = season.cycleData[i];
            if (cycle == null || cycle.numRegularDays < 0 || cycle.numBossDays < 0 || cycle.numRecoveryDays < 0)
                return false;
            length += (long)cycle.numRegularDays + cycle.numBossDays + cycle.numRecoveryDays;
            if (length > int.MaxValue) return false;
        }
        if (cursor + length > int.MaxValue) return false;
        if (length > 0) segments.Add(new Segment(cursor, cursor + length, season.season));
        cursor += length;
        return true;
    }

    private static Segment Locate(long day, List<Segment> segments, int repeatIndex,
        long repeatStart, long repeatLength)
    {
        long shift = day >= repeatStart ? ((day - repeatStart) / repeatLength) * repeatLength : 0;
        long relativeDay = day - shift;
        int first = day >= repeatStart ? repeatIndex : 0;
        for (int i = first; i < segments.Count; i++)
        {
            Segment segment = segments[i];
            if (relativeDay >= segment.Start && relativeDay < segment.End)
                return new Segment(segment.Start + shift, segment.End + shift, segment.Season);
        }
        throw new InvalidOperationException("Invalid calendar table");
    }
}
