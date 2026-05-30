using PortfolioSaver.Core.Enums;
using PortfolioSaver.Shared.Diagnostics;
using YFinance.NET.Models;
using YFinance.NET.Protocol.Dtos;

namespace PortfolioSaver.Data.Services;

public sealed class YFinanceExchangeTimingService
{
    private const string YFinanceCalendarSource = "YFinance.NET market timing";

    public async Task<ExchangeCalendarSet> GetCalendarSetAsync(
        IReadOnlyList<ExchangeCalendarRequest> requests,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        ExchangeCalendarSet set = new()
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Source = YFinanceCalendarSource
        };

        if (!networkAvailable || requests.Count == 0)
            return set;

        foreach (ExchangeCalendarRequest request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.ExchangeSymbol))
                continue;

            string operationId = YFinanceRuntimeClientFactory.CreateOperationId("exchange-timing");
            try
            {
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "ExchangeTimingRequestStart",
                    [new("operation_id", operationId), new("city_key", request.CityKey), new("exchange_symbol", request.ExchangeSymbol)]);
                MarketTimingDto timing = await YFinanceRuntimeClientFactory
                    .RunSerializedAsync(
                        "exchange-timing",
                        operationId,
                        (client, token) => client.GetMarketTimingAsync(request.ExchangeSymbol, token),
                        cancellationToken)
                    .ConfigureAwait(false);
                ExchangeTradingCalendar? calendar = BuildFromTiming(request, timing);
                if (calendar is null)
                    continue;

                set.CalendarsByCityKey[calendar.CityKey] = calendar;
                TraceLog.InfoState(
                    "YFinanceUiBridge",
                    "ExchangeTimingRequestComplete",
                    [new("operation_id", operationId), new("city_key", request.CityKey), new("exchange_symbol", request.ExchangeSymbol), new("timezone", calendar.TimeZoneId)]);
            }
            catch (Exception ex)
            {
                TraceLog.WarnState(
                    "YFinanceUiBridge",
                    "ExchangeTimingRequestFailed",
                    [new("operation_id", operationId), new("city_key", request.CityKey), new("exchange_symbol", request.ExchangeSymbol), new("message", ex.Message)]);
            }
        }

        return set;
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

    private static ExchangeTradingCalendar? BuildFromTiming(ExchangeCalendarRequest request, MarketTimingDto? timing)
    {
        if (timing?.CurrentTradingPeriod is null)
            return null;

        return new ExchangeTradingCalendar
        {
            CityKey = request.CityKey,
            ExchangeCode = request.ExchangeCode,
            ExchangeName = request.ExchangeName,
            ExchangeSymbol = request.ExchangeSymbol,
            TimeZoneId = string.IsNullOrWhiteSpace(timing.ExchangeTimezoneName) ? request.TimeZoneId : timing.ExchangeTimezoneName,
            AlternateTimeZoneId = request.AlternateTimeZoneId,
            Source = YFinanceCalendarSource,
            RegularMarketTimeUtc = timing.RegularMarketTimeUtc,
            CurrentTradingPeriod = MapCurrentTradingPeriods(timing.CurrentTradingPeriod)
        };
    }

    private static CurrentTradingPeriods? MapCurrentTradingPeriods(CurrentTradingPeriodsDto? dto)
        => dto is null ? null : new CurrentTradingPeriods(MapTradingPeriod(dto.Pre), MapTradingPeriod(dto.Regular), MapTradingPeriod(dto.Post));

    private static TradingPeriodWindow? MapTradingPeriod(TradingPeriodWindowDto? dto)
        => dto is null ? null : new TradingPeriodWindow(dto.StartUtc, dto.EndUtc, null, dto.GmtOffsetSeconds);

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
}
