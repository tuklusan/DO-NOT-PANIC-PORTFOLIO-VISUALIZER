using System.Globalization;
using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class ExchangeMarketCalendarService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, (TimeOnly Open, TimeOnly Close)> DefaultHoursByExchangeCode =
        new Dictionary<string, (TimeOnly Open, TimeOnly Close)>(StringComparer.OrdinalIgnoreCase)
        {
            ["NYSE"] = (new TimeOnly(9, 30), new TimeOnly(16, 0)),
            ["LSE"] = (new TimeOnly(8, 0), new TimeOnly(16, 30)),
            ["TSE"] = (new TimeOnly(9, 0), new TimeOnly(15, 0)),
            ["SSE"] = (new TimeOnly(9, 30), new TimeOnly(15, 0)),
            ["HKEX"] = (new TimeOnly(9, 30), new TimeOnly(16, 0)),
            ["NSE"] = (new TimeOnly(9, 15), new TimeOnly(15, 30)),
            ["XETRA"] = (new TimeOnly(9, 0), new TimeOnly(17, 30)),
            ["EPA"] = (new TimeOnly(9, 0), new TimeOnly(17, 30)),
            ["TSX"] = (new TimeOnly(9, 30), new TimeOnly(16, 0)),
            ["KRX"] = (new TimeOnly(9, 0), new TimeOnly(15, 30)),
            ["ASX"] = (new TimeOnly(10, 0), new TimeOnly(16, 0))
        };

    private readonly string _cachePath;

    public ExchangeMarketCalendarService(string? cachePath = null)
    {
        _cachePath = cachePath ?? Path.Combine(PathHelper.GetLocalDataDirectory(), "market-calendars.json");
    }

    public NyseTradingCalendarSnapshot LoadNyseSnapshotFromCacheOrOffline()
    {
        int year = DateTimeOffset.UtcNow.Year;
        ExchangeCalendarSet merged = BuildFallbackSet(
        [
            new ExchangeCalendarRequest
            {
                CityKey = "NewYork",
                ExchangeCode = "NYSE",
                ExchangeName = "NYSE",
                TimeZoneId = "Eastern Standard Time",
                AlternateTimeZoneId = "America/New_York"
            }
        ]);
        merged.Overlay(TryLoadCachedSet());
        return merged.BuildNyseSnapshot();
    }

    public async Task<ExchangeCalendarSet> GetCalendarSetAsync(
        AppSettings settings,
        IReadOnlyList<ExchangeCalendarRequest> requests,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        ExchangeCalendarSet merged = BuildFallbackSet(requests);
        ExchangeCalendarSet? cachedSet = TryLoadCachedSet();
        merged.Overlay(cachedSet);
        return merged;
    }

    public ExchangeCalendarStatus ResolveStatus(ExchangeTradingCalendar calendar, DateTimeOffset utcNow)
    {
        TimeZoneInfo zone = ResolveTimeZone(calendar.TimeZoneId, calendar.AlternateTimeZoneId);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(utcNow, zone);
        DateOnly localDate = DateOnly.FromDateTime(localNow.Date);
        TimeOnly localTime = TimeOnly.FromDateTime(localNow.DateTime);
        TimeOnly close = calendar.ResolveRegularClose(localDate);

        if (calendar.IsTradingDay(localDate) &&
            localTime >= calendar.RegularOpenLocal &&
            localTime < close)
        {
            DateTimeOffset closeAt = BuildLocalDateTimeOffset(localDate, close, zone);
            return new ExchangeCalendarStatus
            {
                IsOpen = true,
                Countdown = MaxZero(closeAt - localNow),
                CountdownTo = ExchangeCountdownTarget.Close
            };
        }

        TimeSpan untilOpen = GetCountdownToOpen(calendar, localNow, zone);
        return new ExchangeCalendarStatus
        {
            IsOpen = false,
            Countdown = untilOpen,
            CountdownTo = ExchangeCountdownTarget.Open
        };
    }

    public string FormatCompactStatus(ExchangeCalendarStatus status)
    {
        if (status.IsOpen)
            return $"OPEN {FormatHoursAndMinutes(status.Countdown)}";

        return $"CLOSED {FormatDaysHoursAndMinutes(status.Countdown)}";
    }

    private ExchangeCalendarSet BuildFallbackSet(IReadOnlyList<ExchangeCalendarRequest> requests)
    {
        int year = DateTimeOffset.UtcNow.Year;
        NyseTradingCalendarSnapshot nyseOffline = NyseTradingCalendarSnapshot.CreateOfflineFallback(year - 1, year + 2);

        ExchangeCalendarSet set = new()
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Source = "Offline fallback rules"
        };

        foreach (ExchangeCalendarRequest request in requests)
        {
            (TimeOnly open, TimeOnly close) = ResolveDefaultHours(request.ExchangeCode);
            ExchangeTradingCalendar calendar = new()
            {
                CityKey = request.CityKey,
                ExchangeCode = request.ExchangeCode,
                ExchangeName = request.ExchangeName,
                TimeZoneId = request.TimeZoneId,
                AlternateTimeZoneId = request.AlternateTimeZoneId,
                RegularOpenLocal = open,
                RegularCloseLocal = close,
                Source = "Offline fallback rules"
            };

            if (string.Equals(request.ExchangeCode, "NYSE", StringComparison.OrdinalIgnoreCase))
            {
                foreach (DateOnly date in nyseOffline.ClosedDates)
                    calendar.ClosedDates.Add(date);
                foreach ((DateOnly date, TimeOnly earlyClose) in nyseOffline.EarlyCloseTimes)
                    calendar.EarlyCloseTimes[date] = earlyClose;
            }

            set.CalendarsByCityKey[calendar.CityKey] = calendar;
        }

        return set;
    }

    private static (TimeOnly Open, TimeOnly Close) ResolveDefaultHours(string exchangeCode)
    {
        if (DefaultHoursByExchangeCode.TryGetValue(exchangeCode, out (TimeOnly Open, TimeOnly Close) hours))
            return hours;

        return (new TimeOnly(9, 30), new TimeOnly(16, 0));
    }

    private ExchangeCalendarSet? TryLoadCachedSet()
    {
        if (!File.Exists(_cachePath))
            return null;

        try
        {
            ExchangeCalendarCacheDto? cache = JsonSerializer.Deserialize<ExchangeCalendarCacheDto>(File.ReadAllText(_cachePath), JsonOptions);
            if (cache is null)
                return null;

            ExchangeCalendarSet set = new()
            {
                Source = string.IsNullOrWhiteSpace(cache.Source) ? "Cache" : cache.Source,
                GeneratedUtc = cache.GeneratedUtc
            };

            foreach (ExchangeCalendarDto dto in cache.Exchanges ?? [])
            {
                if (string.IsNullOrWhiteSpace(dto.CityKey))
                    continue;

                ExchangeTradingCalendar calendar = new()
                {
                    CityKey = dto.CityKey.Trim(),
                    ExchangeCode = (dto.ExchangeCode ?? string.Empty).Trim(),
                    ExchangeName = (dto.ExchangeName ?? string.Empty).Trim(),
                    TimeZoneId = (dto.TimeZoneId ?? string.Empty).Trim(),
                    AlternateTimeZoneId = (dto.AlternateTimeZoneId ?? string.Empty).Trim(),
                    Source = string.IsNullOrWhiteSpace(dto.Source) ? "Cache" : dto.Source
                };

                if (TryParseTimeOnly(dto.RegularOpen, out TimeOnly open))
                    calendar.RegularOpenLocal = open;
                if (TryParseTimeOnly(dto.RegularClose, out TimeOnly close))
                    calendar.RegularCloseLocal = close;

                foreach (string dateText in dto.ClosedDates ?? [])
                {
                    if (DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                        calendar.ClosedDates.Add(date);
                }

                if (dto.EarlyCloseTimes is not null)
                {
                    foreach ((string dateText, string closeText) in dto.EarlyCloseTimes)
                    {
                        if (!DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                            continue;
                        if (!TryParseTimeOnly(closeText, out TimeOnly earlyClose))
                            continue;

                        calendar.EarlyCloseTimes[date] = earlyClose;
                    }
                }

                set.CalendarsByCityKey[calendar.CityKey] = calendar;
            }

            return set;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveCacheAsync(ExchangeCalendarSet set, CancellationToken cancellationToken)
    {
        try
        {
            ExchangeCalendarCacheDto dto = new()
            {
                GeneratedUtc = set.GeneratedUtc,
                Source = set.Source,
                Exchanges = set.CalendarsByCityKey.Values.Select(calendar => new ExchangeCalendarDto
                {
                    CityKey = calendar.CityKey,
                    ExchangeCode = calendar.ExchangeCode,
                    ExchangeName = calendar.ExchangeName,
                    TimeZoneId = calendar.TimeZoneId,
                    AlternateTimeZoneId = calendar.AlternateTimeZoneId,
                    RegularOpen = calendar.RegularOpenLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
                    RegularClose = calendar.RegularCloseLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
                    ClosedDates = calendar.ClosedDates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(text => text, StringComparer.Ordinal).ToList(),
                    EarlyCloseTimes = calendar.EarlyCloseTimes.ToDictionary(
                        pair => pair.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        pair => pair.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
                        StringComparer.Ordinal),
                    Source = calendar.Source
                }).ToList()
            };

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            string? directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(_cachePath, json, cancellationToken);
        }
        catch
        {
        }
    }

    private static TimeSpan GetCountdownToOpen(ExchangeTradingCalendar calendar, DateTimeOffset localNow, TimeZoneInfo zone)
    {
        DateOnly candidateDate = DateOnly.FromDateTime(localNow.Date);
        DateTimeOffset open = BuildLocalDateTimeOffset(candidateDate, calendar.RegularOpenLocal, zone);

        if (localNow.TimeOfDay < calendar.RegularOpenLocal.ToTimeSpan() && calendar.IsTradingDay(candidateDate))
            return MaxZero(open - localNow);

        do
        {
            candidateDate = candidateDate.AddDays(1);
        }
        while (!calendar.IsTradingDay(candidateDate));

        DateTimeOffset nextOpen = BuildLocalDateTimeOffset(candidateDate, calendar.RegularOpenLocal, zone);
        return MaxZero(nextOpen - localNow);
    }

    private static DateTimeOffset BuildLocalDateTimeOffset(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        DateTime localDateTime = new(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        TimeSpan offset = zone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private static bool TryParseTimeOnly(string? value, out TimeOnly time)
    {
        if (TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            return true;

        return TimeOnly.TryParse(value, out time);
    }

    private static TimeSpan MaxZero(TimeSpan value)
        => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static string FormatHoursAndMinutes(TimeSpan timeSpan)
    {
        int totalHours = (int)Math.Floor(Math.Max(0, timeSpan.TotalHours));
        return $"{totalHours:00}:{Math.Max(0, timeSpan.Minutes):00}";
    }

    private static string FormatDaysHoursAndMinutes(TimeSpan timeSpan)
    {
        TimeSpan safe = timeSpan < TimeSpan.Zero ? TimeSpan.Zero : timeSpan;
        int totalHours = (int)Math.Floor(safe.TotalHours);
        int days = totalHours / 24;
        int hours = totalHours % 24;
        return $"{days:00}:{hours:00}:{safe.Minutes:00}";
    }

    private static TimeZoneInfo ResolveTimeZone(string? primaryId, string? secondaryId)
    {
        foreach (string? candidate in new[] { primaryId, secondaryId })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private sealed class ExchangeCalendarCacheDto
    {
        public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Source { get; set; } = string.Empty;
        public List<ExchangeCalendarDto> Exchanges { get; set; } = [];
    }

    private sealed class ExchangeCalendarDto
    {
        public string CityKey { get; set; } = string.Empty;
        public string ExchangeCode { get; set; } = string.Empty;
        public string ExchangeName { get; set; } = string.Empty;
        public string TimeZoneId { get; set; } = string.Empty;
        public string AlternateTimeZoneId { get; set; } = string.Empty;
        public string RegularOpen { get; set; } = "09:30";
        public string RegularClose { get; set; } = "16:00";
        public List<string> ClosedDates { get; set; } = [];
        public Dictionary<string, string> EarlyCloseTimes { get; set; } = new(StringComparer.Ordinal);
        public string Source { get; set; } = string.Empty;
    }
}

public sealed class ExchangeCalendarRequest
{
    public string CityKey { get; set; } = string.Empty;
    public string ExchangeCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string AlternateTimeZoneId { get; set; } = string.Empty;
}

public sealed class ExchangeCalendarSet
{
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.MinValue;
    public string Source { get; set; } = "Offline";
    public Dictionary<string, ExchangeTradingCalendar> CalendarsByCityKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Overlay(ExchangeCalendarSet? overlay)
    {
        if (overlay is null)
            return;

        foreach ((string cityKey, ExchangeTradingCalendar incoming) in overlay.CalendarsByCityKey)
        {
            if (!CalendarsByCityKey.TryGetValue(cityKey, out ExchangeTradingCalendar? current))
            {
                CalendarsByCityKey[cityKey] = incoming.Clone();
                continue;
            }

            current.RegularOpenLocal = incoming.RegularOpenLocal;
            current.RegularCloseLocal = incoming.RegularCloseLocal;
            current.TimeZoneId = string.IsNullOrWhiteSpace(incoming.TimeZoneId) ? current.TimeZoneId : incoming.TimeZoneId;
            current.AlternateTimeZoneId = string.IsNullOrWhiteSpace(incoming.AlternateTimeZoneId) ? current.AlternateTimeZoneId : incoming.AlternateTimeZoneId;
            current.ExchangeCode = string.IsNullOrWhiteSpace(incoming.ExchangeCode) ? current.ExchangeCode : incoming.ExchangeCode;
            current.ExchangeName = string.IsNullOrWhiteSpace(incoming.ExchangeName) ? current.ExchangeName : incoming.ExchangeName;
            foreach (DateOnly date in incoming.ClosedDates)
                current.ClosedDates.Add(date);
            foreach ((DateOnly date, TimeOnly close) in incoming.EarlyCloseTimes)
                current.EarlyCloseTimes[date] = close;
            current.Source = string.IsNullOrWhiteSpace(incoming.Source) ? current.Source : incoming.Source;
        }

        if (overlay.GeneratedUtc > GeneratedUtc)
            GeneratedUtc = overlay.GeneratedUtc;
        if (!string.IsNullOrWhiteSpace(overlay.Source))
            Source = overlay.Source;
    }

    public ExchangeTradingCalendar? TryGetByCityKey(string cityKey)
        => CalendarsByCityKey.TryGetValue(cityKey, out ExchangeTradingCalendar? calendar) ? calendar : null;

    public NyseTradingCalendarSnapshot BuildNyseSnapshot()
    {
        int year = DateTimeOffset.UtcNow.Year;
        NyseTradingCalendarSnapshot nyse = NyseTradingCalendarSnapshot.CreateOfflineFallback(year - 1, year + 2);
        ExchangeTradingCalendar? source = CalendarsByCityKey.Values.FirstOrDefault(calendar =>
            string.Equals(calendar.ExchangeCode, "NYSE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(calendar.CityKey, "NewYork", StringComparison.OrdinalIgnoreCase));

        if (source is null)
            return nyse;

        nyse.Source = source.Source;
        nyse.GeneratedUtc = GeneratedUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : GeneratedUtc;
        nyse.ClosedDates.Clear();
        nyse.EarlyCloseTimes.Clear();
        foreach (DateOnly date in source.ClosedDates)
            nyse.ClosedDates.Add(date);
        foreach ((DateOnly date, TimeOnly close) in source.EarlyCloseTimes)
            nyse.EarlyCloseTimes[date] = close;
        return nyse;
    }
}

public sealed class ExchangeTradingCalendar
{
    public string CityKey { get; set; } = string.Empty;
    public string ExchangeCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string AlternateTimeZoneId { get; set; } = string.Empty;
    public TimeOnly RegularOpenLocal { get; set; } = new(9, 30);
    public TimeOnly RegularCloseLocal { get; set; } = new(16, 0);
    public HashSet<DateOnly> ClosedDates { get; } = [];
    public Dictionary<DateOnly, TimeOnly> EarlyCloseTimes { get; } = [];
    public string Source { get; set; } = "Offline";

    public bool IsTradingDay(DateOnly date)
        => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
           !ClosedDates.Contains(date);

    public TimeOnly ResolveRegularClose(DateOnly date)
        => EarlyCloseTimes.TryGetValue(date, out TimeOnly earlyClose) ? earlyClose : RegularCloseLocal;

    public ExchangeTradingCalendar Clone()
    {
        ExchangeTradingCalendar copy = new()
        {
            CityKey = CityKey,
            ExchangeCode = ExchangeCode,
            ExchangeName = ExchangeName,
            TimeZoneId = TimeZoneId,
            AlternateTimeZoneId = AlternateTimeZoneId,
            RegularOpenLocal = RegularOpenLocal,
            RegularCloseLocal = RegularCloseLocal,
            Source = Source
        };

        foreach (DateOnly date in ClosedDates)
            copy.ClosedDates.Add(date);
        foreach ((DateOnly date, TimeOnly time) in EarlyCloseTimes)
            copy.EarlyCloseTimes[date] = time;
        return copy;
    }
}

public sealed class ExchangeCalendarStatus
{
    public bool IsOpen { get; set; }
    public TimeSpan Countdown { get; set; }
    public ExchangeCountdownTarget CountdownTo { get; set; }
}

public enum ExchangeCountdownTarget
{
    Open,
    Close
}

