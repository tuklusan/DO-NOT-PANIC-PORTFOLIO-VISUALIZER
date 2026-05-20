using System.Reflection;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Screensaver.Services;
using Xunit;
using YFinance.NET.Config;
using YFinance.NET.Transport;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class Nb040BehaviorTests
{
    [Fact]
    public void YFinanceOptions_DefaultToTenMinuteCachesAndGenericUserAgent()
    {
        YFinanceOptions options = new();

        Assert.Equal(TimeSpan.FromMinutes(10), options.DefaultCacheTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.SummaryCacheTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.PersistentMetadataCacheTtl);
        Assert.DoesNotContain("PortfolioSaver", options.UserAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Don't Panic", options.UserAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visualiz", options.UserAgent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void YFinanceRequestIdentifiers_DoNotContainApplicationBranding()
    {
        using YahooFinanceHttpClient client = new(new YFinanceOptions());
        MethodInfo buildRequest = typeof(YahooFinanceHttpClient).GetMethod(
            "BuildRequest",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("YahooFinanceHttpClient.BuildRequest not found.");

        Uri requestUri = new("https://query1.finance.yahoo.com/v7/finance/quote?symbols=AAPL");
        YahooSessionState session = new("crumb", "cookie=value", DateTimeOffset.UtcNow.AddMinutes(30));
        using var request = Assert.IsType<System.Net.Http.HttpRequestMessage>(buildRequest.Invoke(client, [requestUri, session]));

        Assert.True(request.Headers.TryGetValues("x-yahoo-request-id", out IEnumerable<string>? values));
        string requestId = Assert.Single(values);
        Assert.DoesNotContain("PortfolioSaver", requestId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Don't Panic", requestId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visualiz", requestId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_SerializesConcurrentClientWork()
    {
        int concurrent = 0;
        int maxConcurrent = 0;

        Task<int> first = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-a",
            async (_, token) =>
            {
                int now = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                await Task.Delay(150, token);
                Interlocked.Decrement(ref concurrent);
                return 1;
            });

        Task<int> second = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-b",
            async (_, token) =>
            {
                int now = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                await Task.Delay(150, token);
                Interlocked.Decrement(ref concurrent);
                return 2;
            });

        int[] results = await Task.WhenAll(first, second);

        Assert.Equal(new[] { 1, 2 }, results);
        Assert.Equal(1, maxConcurrent);
        Assert.Equal(0, concurrent);
    }

    [Fact]
    public void StartupWarmup_OrderIsMacrosThenGlobalExchangesThenPortfolio()
    {
        MethodInfo method = typeof(StartupCoordinator).GetMethod(
            "GetDedicatedYahooWarmupSymbols",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StartupCoordinator.GetDedicatedYahooWarmupSymbols not found.");

        AppSettings settings = new()
        {
            Groups =
            [
                new TickerGroup
                {
                    Name = "Tape 1",
                    Enabled = true,
                    Tickers =
                    [
                        new TickerItem { Symbol = "AAPL", DisplayName = "Apple", Enabled = true }
                    ]
                }
            ]
        };

        IReadOnlyList<string> symbols = Assert.IsAssignableFrom<IReadOnlyList<string>>(method.Invoke(null, [settings]));
        IReadOnlyList<string> macros = StartupCoordinator.GetMacroIndicatorSymbols();
        IReadOnlyList<string> exchanges = FloatingClockBuilder.GetWorldIndexSymbols()
            .Where(symbol => !macros.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(symbols.Take(macros.Count).SequenceEqual(macros, StringComparer.OrdinalIgnoreCase));
        Assert.True(symbols.Skip(macros.Count).Take(exchanges.Count).SequenceEqual(exchanges, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("AAPL", symbols, StringComparer.OrdinalIgnoreCase);
        Assert.True(symbols.ToList().FindIndex(symbol => string.Equals(symbol, "AAPL", StringComparison.OrdinalIgnoreCase)) >= macros.Count + exchanges.Count);
    }
}
