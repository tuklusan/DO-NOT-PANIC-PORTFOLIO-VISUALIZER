// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
namespace PortfolioSaver.Core.Services;

public sealed class NyseTradingCalendarSnapshot
{
    private static readonly TimeOnly DefaultRegularClose = new(16, 0);
    private static readonly TimeOnly DefaultEarlyClose = new(13, 0);

    public string Source { get; set; } = "Offline";
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;
    public HashSet<DateOnly> ClosedDates { get; } = [];
    public Dictionary<DateOnly, TimeOnly> EarlyCloseTimes { get; } = [];

    public bool IsHoliday(DateOnly date)
        => ClosedDates.Contains(date);

    public TimeOnly ResolveRegularClose(DateOnly date)
        => EarlyCloseTimes.TryGetValue(date, out TimeOnly earlyClose) ? earlyClose : DefaultRegularClose;

    public void Overlay(NyseTradingCalendarSnapshot? overlay)
    {
        if (overlay is null)
            return;

        foreach (DateOnly date in overlay.ClosedDates)
            ClosedDates.Add(date);

        foreach ((DateOnly date, TimeOnly closeTime) in overlay.EarlyCloseTimes)
            EarlyCloseTimes[date] = closeTime;

        if (!string.IsNullOrWhiteSpace(overlay.Source))
            Source = overlay.Source;

        if (overlay.GeneratedUtc > GeneratedUtc)
            GeneratedUtc = overlay.GeneratedUtc;
    }

    public static NyseTradingCalendarSnapshot CreateOfflineFallback(int startYear, int endYear)
    {
        if (endYear < startYear)
            (startYear, endYear) = (endYear, startYear);

        NyseTradingCalendarSnapshot snapshot = new()
        {
            Source = "Offline fallback rules",
            GeneratedUtc = DateTimeOffset.UtcNow
        };

        for (int year = startYear; year <= endYear; year++)
        {
            AddObservedFixedHoliday(snapshot.ClosedDates, new DateOnly(year, 1, 1));
            snapshot.ClosedDates.Add(NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3)); // MLK
            snapshot.ClosedDates.Add(NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3)); // Presidents
            snapshot.ClosedDates.Add(GetGoodFriday(year));
            snapshot.ClosedDates.Add(LastWeekdayOfMonth(year, 5, DayOfWeek.Monday)); // Memorial

            if (year >= 2022)
                AddObservedFixedHoliday(snapshot.ClosedDates, new DateOnly(year, 6, 19)); // Juneteenth

            AddObservedFixedHoliday(snapshot.ClosedDates, new DateOnly(year, 7, 4)); // Independence
            snapshot.ClosedDates.Add(NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1)); // Labor
            DateOnly thanksgiving = NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4);
            snapshot.ClosedDates.Add(thanksgiving);
            AddObservedFixedHoliday(snapshot.ClosedDates, new DateOnly(year, 12, 25)); // Christmas

            DateOnly dayAfterThanksgiving = thanksgiving.AddDays(1);
            if (IsWeekday(dayAfterThanksgiving) && !snapshot.ClosedDates.Contains(dayAfterThanksgiving))
                snapshot.EarlyCloseTimes[dayAfterThanksgiving] = DefaultEarlyClose;

            DateOnly christmasEve = new(year, 12, 24);
            if (IsWeekday(christmasEve) && !snapshot.ClosedDates.Contains(christmasEve))
                snapshot.EarlyCloseTimes[christmasEve] = DefaultEarlyClose;
        }

        return snapshot;
    }

    private static void AddObservedFixedHoliday(HashSet<DateOnly> closedDates, DateOnly actualDate)
    {
        DateOnly observed = actualDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => actualDate.AddDays(-1),
            DayOfWeek.Sunday => actualDate.AddDays(1),
            _ => actualDate
        };

        closedDates.Add(observed);
    }

    private static bool IsWeekday(DateOnly date)
        => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek weekday, int occurrence)
    {
        DateOnly date = new(year, month, 1);
        while (date.DayOfWeek != weekday)
            date = date.AddDays(1);

        return date.AddDays((occurrence - 1) * 7);
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek weekday)
    {
        DateOnly date = new(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != weekday)
            date = date.AddDays(-1);

        return date;
    }

    private static DateOnly GetGoodFriday(int year)
        => GetEasterSunday(year).AddDays(-2);

    private static DateOnly GetEasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
