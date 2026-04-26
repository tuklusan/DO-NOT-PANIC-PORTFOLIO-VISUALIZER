using System.Net;
using System.Net.Http;
using System.Text;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ExchangeMarketCalendarIntegrationTests
{
    [Fact]
    public async Task GetCalendarSetAsync_UsesCachedCalendarWhenOffline()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string cachePath = Path.Combine(tempRoot, "market-calendars.json");

        await File.WriteAllTextAsync(
            cachePath,
            """
            {
              "generatedUtc": "2026-04-01T00:00:00Z",
              "source": "Cache",
              "exchanges": [
                {
                  "cityKey": "NewYork",
                  "exchangeCode": "NYSE",
                  "exchangeName": "NYSE",
                  "timeZoneId": "Eastern Standard Time",
                  "alternateTimeZoneId": "America/New_York",
                  "regularOpen": "09:30",
                  "regularClose": "16:00",
                  "closedDates": ["2026-07-04"],
                  "earlyCloseTimes": { "2026-11-27": "13:00" },
                  "source": "Cache"
                }
              ]
            }
            """,
            Encoding.UTF8);

        ExchangeMarketCalendarService service = new(cachePath);
        AppSettings settings = Defaults.CreateSettings();
        IReadOnlyList<ExchangeCalendarRequest> requests =
        [
            new ExchangeCalendarRequest
            {
                CityKey = "NewYork",
                ExchangeCode = "NYSE",
                ExchangeName = "NYSE",
                TimeZoneId = "Eastern Standard Time",
                AlternateTimeZoneId = "America/New_York"
            }
        ];

        ExchangeCalendarSet set = await service.GetCalendarSetAsync(settings, requests, networkAvailable: false);
        ExchangeTradingCalendar? calendar = set.TryGetByCityKey("NewYork");
        Assert.NotNull(calendar);

        Assert.Contains(new DateOnly(2026, 7, 4), calendar!.ClosedDates);
        Assert.True(calendar.EarlyCloseTimes.ContainsKey(new DateOnly(2026, 11, 27)));
    }

    [Fact]
    public async Task GetCalendarSetAsync_MergesLiveDataAndPersistsCacheWhenRefreshIsDue()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string cachePath = Path.Combine(tempRoot, "market-calendars.json");

        CalendarApiHandler handler = new();
        Func<TimeSpan, HttpClient> httpFactory = _ => new HttpClient(handler);
        ExchangeMarketCalendarService service = new(cachePath, httpFactory);
        AppSettings settings = Defaults.CreateSettings();
        settings.FinancialModelingPrepApiKey = "fmp-key";
        settings.EodhdApiKey = "eod-key";
        settings.MarketCalendarRefreshHours = 12;

        IReadOnlyList<ExchangeCalendarRequest> requests =
        [
            new ExchangeCalendarRequest
            {
                CityKey = "NewYork",
                ExchangeCode = "NYSE",
                ExchangeName = "NYSE",
                TimeZoneId = "Eastern Standard Time",
                AlternateTimeZoneId = "America/New_York"
            }
        ];

        ExchangeCalendarSet set = await service.GetCalendarSetAsync(settings, requests, networkAvailable: true);
        ExchangeTradingCalendar? calendar = set.TryGetByCityKey("NewYork");
        Assert.NotNull(calendar);

        Assert.True(handler.FmpHoursRequestCount >= 1);
        Assert.True(handler.FmpHolidayRequestCount >= 1);
        Assert.True(handler.EodDetailsRequestCount >= 1);
        Assert.Equal(new TimeOnly(9, 35), calendar!.RegularOpenLocal);
        Assert.Equal(new TimeOnly(16, 5), calendar.RegularCloseLocal);
        Assert.Contains(new DateOnly(2026, 12, 25), calendar.ClosedDates);
        Assert.Equal(new TimeOnly(13, 0), calendar.EarlyCloseTimes[new DateOnly(2026, 12, 24)]);
        Assert.True(File.Exists(cachePath));

        string cachedJson = await File.ReadAllTextAsync(cachePath);
        Assert.Contains("2026-12-25", cachedJson, StringComparison.Ordinal);
        Assert.Contains("13:00", cachedJson, StringComparison.Ordinal);
    }

    private sealed class CalendarApiHandler : HttpMessageHandler
    {
        public int FmpHoursRequestCount { get; private set; }
        public int FmpHolidayRequestCount { get; private set; }
        public int EodDetailsRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.Contains("all-exchange-market-hours", StringComparison.OrdinalIgnoreCase))
            {
                FmpHoursRequestCount++;
                return Task.FromResult(JsonResponse(
                    """
                    [
                      {
                        "exchange": "NYSE",
                        "open": "09:45",
                        "close": "16:15",
                        "timezone": "America/New_York"
                      }
                    ]
                    """));
            }

            if (url.Contains("holidays-by-exchange", StringComparison.OrdinalIgnoreCase))
            {
                FmpHolidayRequestCount++;
                return Task.FromResult(JsonResponse(
                    """
                    [
                      {
                        "date": "2026-12-25",
                        "status": "closed"
                      }
                    ]
                    """));
            }

            if (url.Contains("/api/exchange-details/", StringComparison.OrdinalIgnoreCase))
            {
                EodDetailsRequestCount++;
                return Task.FromResult(JsonResponse(
                    """
                    {
                      "TradingHours": {
                        "open": "09:35",
                        "close": "16:05"
                      },
                      "Timezone": "America/New_York",
                      "ExchangeHolidays": {
                        "2026-12-24": { "Close": "13:00" }
                      }
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string payload)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
    }
}
