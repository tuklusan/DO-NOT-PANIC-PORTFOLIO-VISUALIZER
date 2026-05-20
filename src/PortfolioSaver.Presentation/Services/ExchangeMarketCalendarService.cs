using System.Globalization;
using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;
using YFinance.NET.Api;
using YFinance.NET.Models;

namespace PortfolioSaver.Screensaver.Services;

public sealed class ExchangeMarketCalendarService
{
    private const string YFinanceCalendarSource = "YFinance.NET chart metadata";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _cachePath;
    private readonly Func<YFinanceClient> _clientFactory;

    public ExchangeMarketCalendarService(string? cachePath = null, Func<YFinanceClient>? clientFactory = null)
    {
        _cachePath = cachePath ?? Path.Combine(PathHelper.GetLocalDataDirectory(), "market-calendars.json");
        _clientFactory = clientFactory ?? YFinanceRuntimeClientFactory.GetSharedClient;
    }

    public async Task<ExchangeCalendarSet> GetCalendarSetAsync(
        AppSettings settings,
        IReadOnlyList<ExchangeCalendarRequest> requests,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        ExchangeCalendarSet merged = TryLoadCachedSet() ?? new ExchangeCalendarSet
        {
            GeneratedUtc = DateTimeOffset.MinValue,
            Source = "YFinance cache"
        };

        if (!networkAvailable || requests.Count == 0)
            return merged;

        ExchangeCalendarSet live = new()
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Source = YFinanceCalendarSource
        };

        DateTimeOffset endUtc = DateTimeOffset.UtcNow.AddDays(5);
        DateTimeOffset startUtc = endUtc.AddDays(-10);
        YFinanceClient client = _clientFactory();
        foreach (ExchangeCalendarRequest request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.ExchangeSymbol))
                continue;

            try
            {
                string operationId = YFinanceRuntimeClientFactory.CreateOperationId("exchange-calendar");
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "ExchangeCalendarRequestStart",
                    [new("operation_id", operationId), new("city_key", request.CityKey), new("exchange_symbol", request.ExchangeSymbol)]);
                HistoryResponse response = await YFinanceRuntimeClientFactory
                    .RunSerializedAsync(
                        "exchange-calendar",
                        operationId,
                        (_, token) => client
                            .Ticker(request.ExchangeSymbol)
                            .GetHistoryResponseAsync(startUtc, endUtc, "1d", token),
                        cancellationToken)
                    .ConfigureAwait(false);
                ExchangeTradingCalendar? calendar = BuildFromHistoryMetadata(request, response.Metadata);
                if (calendar is not null)
                {
                    live.CalendarsByCityKey[calendar.CityKey] = calendar;
                    TraceLog.InfoState(
                        "YFinanceUiBridge",
                        "ExchangeCalendarRequestComplete",
                        [new("operation_id", operationId), new("city_key", request.CityKey), new("exchange_symbol", request.ExchangeSymbol), new("timezone", calendar.TimeZoneId)]);
                }
            }
            catch (Exception ex)
            {
                TraceLog.WarnState(
                    "YFinanceUiBridge",
                    "ExchangeCalendarRequestFailed",
                    [new("city_key", request.CityKey), new("exchange_symbol", request.ExchangeSymbol), new("message", ex.Message)]);
            }
        }

        if (live.CalendarsByCityKey.Count > 0)
        {
            merged.Overlay(live);
            await SaveCacheAsync(merged, cancellationToken).ConfigureAwait(false);
        }

        return merged;
    }

    public ExchangeCalendarStatus ResolveStatus(ExchangeTradingCalendar calendar, DateTimeOffset utcNow)
    {
        CurrentTradingPeriods? periods = calendar.CurrentTradingPeriod;
        if (periods is null)
        {
            return new ExchangeCalendarStatus
            {
                Session = MarketSession.Unknown,
                IsOpen = false,
                Countdown = TimeSpan.Zero,
                CountdownTo = ExchangeCountdownTarget.Unknown,
                HasCountdown = false
            };
        }

        if (IsActive(periods.Regular, utcNow))
        {
            return new ExchangeCalendarStatus
            {
                Session = MarketSession.Regular,
                IsOpen = true,
                Countdown = MaxZero(periods.Regular!.EndUtc - utcNow),
                CountdownTo = ExchangeCountdownTarget.Close,
                HasCountdown = true
            };
        }

        if (IsActive(periods.Pre, utcNow))
        {
            DateTimeOffset target = periods.Regular?.StartUtc ?? periods.Pre!.EndUtc;
            return new ExchangeCalendarStatus
            {
                Session = MarketSession.PreMarket,
                IsOpen = false,
                Countdown = MaxZero(target - utcNow),
                CountdownTo = ExchangeCountdownTarget.Open,
                HasCountdown = true
            };
        }

        if (IsActive(periods.Post, utcNow))
        {
            return new ExchangeCalendarStatus
            {
                Session = MarketSession.AfterHours,
                IsOpen = false,
                Countdown = MaxZero(periods.Post!.EndUtc - utcNow),
                CountdownTo = ExchangeCountdownTarget.SessionEnd,
                HasCountdown = true
            };
        }

        TradingPeriodWindow? nextPeriod = GetNextPeriod(periods, utcNow);
        if (nextPeriod is not null)
        {
            return new ExchangeCalendarStatus
            {
                Session = MarketSession.Closed,
                IsOpen = false,
                Countdown = MaxZero(nextPeriod.StartUtc - utcNow),
                CountdownTo = ExchangeCountdownTarget.Open,
                HasCountdown = true
            };
        }

        return new ExchangeCalendarStatus
        {
            Session = MarketSession.Closed,
            IsOpen = false,
            Countdown = TimeSpan.Zero,
            CountdownTo = ExchangeCountdownTarget.Unknown,
            HasCountdown = false
        };
    }

    public string FormatCompactStatus(ExchangeCalendarStatus status)
    {
        if (!status.HasCountdown)
            return status.Session switch
            {
                MarketSession.PreMarket => "PRE --",
                MarketSession.AfterHours => "POST --",
                MarketSession.Regular => "OPEN --",
                MarketSession.Closed => "CLOSED --",
                _ => "--"
            };

        return status.Session switch
        {
            MarketSession.PreMarket => $"PRE {FormatHoursAndMinutes(status.Countdown)}",
            MarketSession.AfterHours => $"POST {FormatHoursAndMinutes(status.Countdown)}",
            MarketSession.Regular => $"OPEN {FormatHoursAndMinutes(status.Countdown)}",
            MarketSession.Closed => $"CLOSED {FormatDaysHoursAndMinutes(status.Countdown)}",
            _ => $"-- {FormatHoursAndMinutes(status.Countdown)}"
        };
    }

    private static ExchangeTradingCalendar? BuildFromHistoryMetadata(ExchangeCalendarRequest request, HistoryMetadata? metadata)
    {
        if (metadata?.CurrentTradingPeriod is null)
            return null;

        return new ExchangeTradingCalendar
        {
            CityKey = request.CityKey,
            ExchangeCode = request.ExchangeCode,
            ExchangeName = request.ExchangeName,
            ExchangeSymbol = request.ExchangeSymbol,
            TimeZoneId = string.IsNullOrWhiteSpace(metadata.ExchangeTimezoneName) ? request.TimeZoneId : metadata.ExchangeTimezoneName,
            AlternateTimeZoneId = request.AlternateTimeZoneId,
            Source = YFinanceCalendarSource,
            RegularMarketTimeUtc = metadata.RegularMarketTimeUtc,
            CurrentTradingPeriod = metadata.CurrentTradingPeriod
        };
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
                Source = string.IsNullOrWhiteSpace(cache.Source) ? "YFinance cache" : cache.Source,
                GeneratedUtc = cache.GeneratedUtc
            };

            foreach (ExchangeCalendarDto dto in cache.Exchanges ?? [])
            {
                if (string.IsNullOrWhiteSpace(dto.CityKey))
                    continue;

                set.CalendarsByCityKey[dto.CityKey.Trim()] = new ExchangeTradingCalendar
                {
                    CityKey = dto.CityKey.Trim(),
                    ExchangeCode = (dto.ExchangeCode ?? string.Empty).Trim(),
                    ExchangeName = (dto.ExchangeName ?? string.Empty).Trim(),
                    ExchangeSymbol = (dto.ExchangeSymbol ?? string.Empty).Trim(),
                    TimeZoneId = (dto.TimeZoneId ?? string.Empty).Trim(),
                    AlternateTimeZoneId = (dto.AlternateTimeZoneId ?? string.Empty).Trim(),
                    Source = string.IsNullOrWhiteSpace(dto.Source) ? "YFinance cache" : dto.Source,
                    RegularMarketTimeUtc = dto.RegularMarketTimeUtc,
                    CurrentTradingPeriod = new CurrentTradingPeriods(
                        ParseTradingPeriodWindow(dto.PrePeriod),
                        ParseTradingPeriodWindow(dto.RegularPeriod),
                        ParseTradingPeriodWindow(dto.PostPeriod))
                };
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
                    ExchangeSymbol = calendar.ExchangeSymbol,
                    TimeZoneId = calendar.TimeZoneId,
                    AlternateTimeZoneId = calendar.AlternateTimeZoneId,
                    Source = calendar.Source,
                    RegularMarketTimeUtc = calendar.RegularMarketTimeUtc,
                    PrePeriod = ToDto(calendar.CurrentTradingPeriod?.Pre),
                    RegularPeriod = ToDto(calendar.CurrentTradingPeriod?.Regular),
                    PostPeriod = ToDto(calendar.CurrentTradingPeriod?.Post)
                }).ToList()
            };

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            string? directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(_cachePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static TradingPeriodWindow? ParseTradingPeriodWindow(TradingPeriodWindowDto? dto)
    {
        if (dto is null || dto.StartUtc == DateTimeOffset.MinValue || dto.EndUtc == DateTimeOffset.MinValue)
            return null;

        return new TradingPeriodWindow(dto.StartUtc, dto.EndUtc, dto.Timezone, dto.GmtOffsetSeconds);
    }

    private static TradingPeriodWindowDto? ToDto(TradingPeriodWindow? window)
        => window is null
            ? null
            : new TradingPeriodWindowDto
            {
                StartUtc = window.StartUtc,
                EndUtc = window.EndUtc,
                Timezone = window.Timezone ?? string.Empty,
                GmtOffsetSeconds = window.GmtOffsetSeconds
            };

    private static bool IsActive(TradingPeriodWindow? window, DateTimeOffset utcNow)
        => window is not null && utcNow >= window.StartUtc && utcNow < window.EndUtc;

    private static TradingPeriodWindow? GetNextPeriod(CurrentTradingPeriods periods, DateTimeOffset utcNow)
        => new[] { periods.Pre, periods.Regular, periods.Post }
            .Where(static period => period is not null)
            .Select(static period => period!)
            .Where(period => period.StartUtc > utcNow)
            .OrderBy(period => period.StartUtc)
            .FirstOrDefault();

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
        public string ExchangeSymbol { get; set; } = string.Empty;
        public string TimeZoneId { get; set; } = string.Empty;
        public string AlternateTimeZoneId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset? RegularMarketTimeUtc { get; set; }
        public TradingPeriodWindowDto? PrePeriod { get; set; }
        public TradingPeriodWindowDto? RegularPeriod { get; set; }
        public TradingPeriodWindowDto? PostPeriod { get; set; }
    }

    private sealed class TradingPeriodWindowDto
    {
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
        public string Timezone { get; set; } = string.Empty;
        public long? GmtOffsetSeconds { get; set; }
    }
}

public sealed class ExchangeCalendarRequest
{
    public string CityKey { get; set; } = string.Empty;
    public string ExchangeCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string AlternateTimeZoneId { get; set; } = string.Empty;
}

public sealed class ExchangeCalendarSet
{
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.MinValue;
    public string Source { get; set; } = "YFinance";
    public Dictionary<string, ExchangeTradingCalendar> CalendarsByCityKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Overlay(ExchangeCalendarSet? overlay)
    {
        if (overlay is null)
            return;

        foreach ((string cityKey, ExchangeTradingCalendar incoming) in overlay.CalendarsByCityKey)
            CalendarsByCityKey[cityKey] = incoming.Clone();

        if (overlay.GeneratedUtc > GeneratedUtc)
            GeneratedUtc = overlay.GeneratedUtc;
        if (!string.IsNullOrWhiteSpace(overlay.Source))
            Source = overlay.Source;
    }

    public ExchangeTradingCalendar? TryGetByCityKey(string cityKey)
        => CalendarsByCityKey.TryGetValue(cityKey, out ExchangeTradingCalendar? calendar) ? calendar : null;
}

public sealed class ExchangeTradingCalendar
{
    public string CityKey { get; set; } = string.Empty;
    public string ExchangeCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string AlternateTimeZoneId { get; set; } = string.Empty;
    public string Source { get; set; } = "YFinance";
    public DateTimeOffset? RegularMarketTimeUtc { get; set; }
    public CurrentTradingPeriods? CurrentTradingPeriod { get; set; }

    public ExchangeTradingCalendar Clone()
        => new()
        {
            CityKey = CityKey,
            ExchangeCode = ExchangeCode,
            ExchangeName = ExchangeName,
            ExchangeSymbol = ExchangeSymbol,
            TimeZoneId = TimeZoneId,
            AlternateTimeZoneId = AlternateTimeZoneId,
            Source = Source,
            RegularMarketTimeUtc = RegularMarketTimeUtc,
            CurrentTradingPeriod = CurrentTradingPeriod is null
                ? null
                : new CurrentTradingPeriods(
                    CurrentTradingPeriod.Pre,
                    CurrentTradingPeriod.Regular,
                    CurrentTradingPeriod.Post)
        };
}

public sealed class ExchangeCalendarStatus
{
    public MarketSession Session { get; set; } = MarketSession.Unknown;
    public bool IsOpen { get; set; }
    public TimeSpan Countdown { get; set; }
    public ExchangeCountdownTarget CountdownTo { get; set; } = ExchangeCountdownTarget.Unknown;
    public bool HasCountdown { get; set; }
}

public enum ExchangeCountdownTarget
{
    Unknown,
    Open,
    Close,
    SessionEnd
}
