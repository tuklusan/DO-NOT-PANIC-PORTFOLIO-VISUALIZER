using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Services;

public sealed class NewYorkMarketStatusService
{
    private static readonly TimeOnly PreMarketOpen = new(4, 0);
    private static readonly TimeOnly RegularOpen = new(9, 30);
    private static readonly TimeOnly AfterHoursClose = new(20, 0);

    private readonly MarketSessionResolver _sessionResolver = new();
    private readonly object _calendarLock = new();
    private NyseTradingCalendarSnapshot _calendarSnapshot;

    public NewYorkMarketStatusService(NyseTradingCalendarSnapshot? calendarSnapshot = null)
    {
        int year = DateTimeOffset.UtcNow.Year;
        _calendarSnapshot = calendarSnapshot ?? NyseTradingCalendarSnapshot.CreateOfflineFallback(year - 1, year + 2);
    }

    public void UpdateCalendarSnapshot(NyseTradingCalendarSnapshot? calendarSnapshot)
    {
        if (calendarSnapshot is null)
            return;

        lock (_calendarLock)
            _calendarSnapshot = calendarSnapshot;
    }

    public string FormatStatusLine(DateTimeOffset utcNow)
    {
        if (!TryResolveEasternNow(utcNow, out DateTimeOffset easternNow, out _))
            return "Market (New York): Unknown";

        NyseTradingCalendarSnapshot calendar = GetCalendarSnapshot();
        DateOnly easternDate = DateOnly.FromDateTime(easternNow.Date);
        TimeOnly regularClose = calendar.ResolveRegularClose(easternDate);
        bool tradingDay = IsTradingDay(easternDate, calendar);
        TimeOnly localTime = TimeOnly.FromDateTime(easternNow.DateTime);

        if (tradingDay && localTime >= RegularOpen && localTime < regularClose)
            return $"Market (New York): Regular | Closing in {FormatHoursAndMinutes(GetCountdownToClose(easternNow, calendar))}";

        if (tradingDay && localTime >= PreMarketOpen && localTime < RegularOpen)
            return $"Market (New York): Pre-Market | Opening in {FormatDaysHoursAndMinutes(GetCountdownToOpen(easternNow, calendar))}";

        if (tradingDay && localTime >= regularClose && localTime < AfterHoursClose)
            return $"Market (New York): After Hours | Opening in {FormatDaysHoursAndMinutes(GetCountdownToOpen(easternNow, calendar))}";

        MarketSession fallbackSession = _sessionResolver.Resolve(utcNow);
        if (fallbackSession == MarketSession.Unknown)
            return "Market (New York): Unknown";

        return $"Market (New York): Closed | Opening in {FormatDaysHoursAndMinutes(GetCountdownToOpen(easternNow, calendar))}";
    }

    private static TimeSpan GetCountdownToClose(DateTimeOffset easternNow, NyseTradingCalendarSnapshot calendar)
    {
        DateOnly easternDate = DateOnly.FromDateTime(easternNow.Date);
        TimeOnly closeTime = calendar.ResolveRegularClose(easternDate);
        DateTimeOffset close = BuildEasternDateTimeOffset(easternDate, closeTime);
        TimeSpan remaining = close - easternNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static TimeSpan GetCountdownToOpen(DateTimeOffset easternNow, NyseTradingCalendarSnapshot calendar)
    {
        DateOnly candidateDate = DateOnly.FromDateTime(easternNow.Date);
        DateTimeOffset open = BuildEasternDateTimeOffset(candidateDate, RegularOpen);

        if (easternNow.TimeOfDay < RegularOpen.ToTimeSpan() && IsTradingDay(candidateDate, calendar))
            return open - easternNow;

        do
        {
            candidateDate = candidateDate.AddDays(1);
        }
        while (!IsTradingDay(candidateDate, calendar));

        DateTimeOffset nextOpen = BuildEasternDateTimeOffset(candidateDate, RegularOpen);
        TimeSpan remaining = nextOpen - easternNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static DateTimeOffset BuildEasternDateTimeOffset(DateOnly date, TimeOnly time)
    {
        DateTime localDateTime = new(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        TimeZoneInfo eastern = ResolveEasternZone();
        TimeSpan offset = eastern.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private NyseTradingCalendarSnapshot GetCalendarSnapshot()
    {
        lock (_calendarLock)
            return _calendarSnapshot;
    }

    private static bool IsTradingDay(DateOnly date, NyseTradingCalendarSnapshot calendar)
        => IsWeekday(date) && !calendar.IsHoliday(date);

    private static bool IsWeekday(DateOnly date)
    {
        DayOfWeek day = date.DayOfWeek;
        return day is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
    }

    private static string FormatHoursAndMinutes(TimeSpan timeSpan)
    {
        if (timeSpan < TimeSpan.Zero)
            timeSpan = TimeSpan.Zero;

        int totalHours = (int)Math.Floor(timeSpan.TotalHours);
        return $"{totalHours:00}:{timeSpan.Minutes:00}";
    }

    private static string FormatDaysHoursAndMinutes(TimeSpan timeSpan)
    {
        if (timeSpan < TimeSpan.Zero)
            timeSpan = TimeSpan.Zero;

        int totalHours = (int)Math.Floor(timeSpan.TotalHours);
        int days = totalHours / 24;
        int hours = totalHours % 24;
        if (days <= 0)
            return $"{hours:00}h{timeSpan.Minutes:00}m";

        return $"{days:00}d{hours:00}h{timeSpan.Minutes:00}m";
    }

    private static bool TryResolveEasternNow(DateTimeOffset utcNow, out DateTimeOffset easternNow, out TimeZoneInfo eastern)
    {
        try
        {
            eastern = ResolveEasternZone();
            easternNow = TimeZoneInfo.ConvertTime(utcNow, eastern);
            return true;
        }
        catch
        {
            easternNow = utcNow;
            eastern = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static TimeZoneInfo ResolveEasternZone()
        => TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
}
