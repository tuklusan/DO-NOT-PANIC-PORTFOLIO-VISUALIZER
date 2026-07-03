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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Media.Services;
using PortfolioSaver.Shared.Services;
using PortfolioSaver.Presentation.Services;
using PortfolioSaver.Shared.Helpers;
using Xunit;
using YFinance.NET.Caching;
using YFinance.NET.Client;
using YFinance.NET.Config;
using YFinance.NET.Exceptions;
using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Protocol.Constants;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Transport;
using YQuoteSnapshot = YFinance.NET.Models.QuoteSnapshot;
using YTickerInfo = YFinance.NET.Models.TickerInfo;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class Nb040BehaviorTests
{
    [Fact]
    public void YFinanceOptions_DefaultToTenMinuteCachesAndGenericUserAgent()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);

        try
        {
            YFinanceOptions options = new();

            Assert.Equal(TimeSpan.FromMinutes(10), options.DefaultCacheTtl);
            Assert.Equal(TimeSpan.FromMinutes(10), options.SummaryCacheTtl);
            Assert.Equal(TimeSpan.FromMinutes(10), options.PersistentMetadataCacheTtl);
            Assert.Equal(TimeSpan.FromSeconds(30), options.HttpTimeout);
            Assert.Equal("en-US", options.Language);
            Assert.Equal("US", options.Region);
            Assert.Equal(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    PathHelper.AppLocalDataFolderName,
                    "Caches",
                    "YFinance"),
                options.PersistentCacheRootPath);
            Assert.DoesNotContain("PortfolioSaver", options.UserAgent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Don't Panic", options.UserAgent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Visualiz", options.UserAgent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
        }
    }

    [Fact]
    public void YFinanceOptions_PersistentCacheRootHonorsProductLocalDataOverride()
    {
        string? previousProductOverride = Environment.GetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT");
        string? previousLocalOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT");
        string? previousLegacyOverride = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        string overrideRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", overrideRoot);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", null);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", null);

        try
        {
            YFinanceOptions options = new();

            Assert.Equal(Path.Combine(Path.GetFullPath(overrideRoot), "Caches", "YFinance"), options.PersistentCacheRootPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", previousProductOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_LOCALDATA_ROOT", previousLocalOverride);
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousLegacyOverride);
            if (Directory.Exists(overrideRoot))
                DeleteDirectoryWithRetry(overrideRoot);
        }
    }

    [Fact]
    public void YFinanceOptions_AddLocaleQueryParameters_DefaultsToUpstreamLocaleScope()
    {
        YFinanceOptions options = new();
        Dictionary<string, string?> query = new(StringComparer.OrdinalIgnoreCase)
        {
            ["symbols"] = "AAPL",
            ["formatted"] = "false"
        };

        options.AddLocaleQueryParameters(query);

        Assert.Equal("en-US", query["lang"]);
        Assert.Equal("US", query["region"]);
    }

    [Fact]
    public void YFinanceOptions_AddLocaleQueryParameters_RespectsCustomLocaleScope()
    {
        YFinanceOptions options = new()
        {
            Language = "en-GB",
            Region = "GB"
        };
        Dictionary<string, string?> query = new();

        options.AddLocaleQueryParameters(query);

        Assert.Equal("en-GB", query["lang"]);
        Assert.Equal("GB", query["region"]);
    }

    [Fact]
    public void HistoryParsing_ChartNull_ReturnsEmptyResponse()
    {
        using JsonDocument document = JsonDocument.Parse("""{"chart":null}""");

        YFinance.NET.Models.HistoryResponse response = HistoryService.ParseHistoryResponse("AAPL", DateTimeOffset.UtcNow, document.RootElement);

        Assert.Equal("AAPL", response.Symbol);
        Assert.Empty(response.Bars);
        Assert.Null(response.Metadata);
    }

    [Fact]
    public void HistoryParsing_ChartMissing_ThrowsYFinanceApiException()
    {
        using JsonDocument document = JsonDocument.Parse("""{"finance":{"result":[]}}""");

        YFinanceApiException exception = Assert.Throws<YFinanceApiException>(
            () => HistoryService.ParseHistoryResponse("AAPL", DateTimeOffset.UtcNow, document.RootElement));

        Assert.Contains("did not contain a chart node", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"chart":[]}""")]
    [InlineData("""{"chart":"not-a-chart"}""")]
    [InlineData("""{"chart":42}""")]
    public void HistoryParsing_ChartUnexpectedType_ThrowsYFinanceApiException(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        YFinanceApiException exception = Assert.Throws<YFinanceApiException>(
            () => HistoryService.ParseHistoryResponse("AAPL", DateTimeOffset.UtcNow, document.RootElement));

        Assert.Contains("instead of an object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoryParsing_ChartError_ThrowsYFinanceApiException()
    {
        using JsonDocument document = JsonDocument.Parse("""{"chart":{"error":{"description":"symbol not found"}}}""");

        YFinanceApiException exception = Assert.Throws<YFinanceApiException>(
            () => HistoryService.ParseHistoryResponse("AAPL", DateTimeOffset.UtcNow, document.RootElement));

        Assert.Contains("symbol not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketTimingParsing_ChartNull_ReturnsNull()
    {
        using JsonDocument document = JsonDocument.Parse("""{"chart":null}""");

        YFinance.NET.Models.MarketTimingSnapshot? snapshot = MarketTimingService.ParseMarketTiming("AAPL", document.RootElement);

        Assert.Null(snapshot);
    }

    [Fact]
    public void MarketTimingParsing_EmptyResult_ReturnsNull()
    {
        using JsonDocument document = JsonDocument.Parse("""{"chart":{"result":[]}}""");

        YFinance.NET.Models.MarketTimingSnapshot? snapshot = MarketTimingService.ParseMarketTiming("AAPL", document.RootElement);

        Assert.Null(snapshot);
    }

    [Fact]
    public void MarketTimingParsing_ChartMissing_ThrowsYFinanceApiException()
    {
        using JsonDocument document = JsonDocument.Parse("""{"finance":{"result":[]}}""");

        YFinanceApiException exception = Assert.Throws<YFinanceApiException>(
            () => MarketTimingService.ParseMarketTiming("AAPL", document.RootElement));

        Assert.Contains("did not contain a chart node", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"chart":[]}""")]
    [InlineData("""{"chart":"not-a-chart"}""")]
    [InlineData("""{"chart":42}""")]
    public void MarketTimingParsing_ChartUnexpectedType_ThrowsYFinanceApiException(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        YFinanceApiException exception = Assert.Throws<YFinanceApiException>(
            () => MarketTimingService.ParseMarketTiming("AAPL", document.RootElement));

        Assert.Contains("instead of an object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketTimingParsing_ChartError_ThrowsYFinanceApiException()
    {
        using JsonDocument document = JsonDocument.Parse("""{"chart":{"error":{"description":"symbol not found"}}}""");

        YFinanceApiException exception = Assert.Throws<YFinanceApiException>(
            () => MarketTimingService.ParseMarketTiming("AAPL", document.RootElement));

        Assert.Contains("symbol not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void YFinanceHttpDegradationPolicy_RefreshesSessionOnlyForAuthAndCrumbFailures()
    {
        Assert.True(YahooFinanceHttpClient.ShouldRefreshSession("anything", HttpStatusCode.Unauthorized));
        Assert.True(YahooFinanceHttpClient.ShouldRefreshSession("anything", HttpStatusCode.Forbidden));
        Assert.True(YahooFinanceHttpClient.ShouldRefreshSession("invalid cookie", HttpStatusCode.BadRequest));
        Assert.True(YahooFinanceHttpClient.ShouldRefreshSession("invalid crumb", HttpStatusCode.BadRequest));
        Assert.True(YahooFinanceHttpClient.ShouldRefreshSession("csrf token rejected", HttpStatusCode.BadRequest));

        Assert.False(YahooFinanceHttpClient.ShouldRefreshSession("not found", HttpStatusCode.NotFound));
        Assert.False(YahooFinanceHttpClient.ShouldRefreshSession("request timeout", HttpStatusCode.RequestTimeout));
        Assert.False(YahooFinanceHttpClient.ShouldRefreshSession("server exploded", HttpStatusCode.InternalServerError));
    }

    [Fact]
    public void YFinanceHttpDegradationPolicy_UsesRetryAfterOrExponentialBackoffForRateLimits()
    {
        using HttpResponseMessage explicitRetry = new(HttpStatusCode.TooManyRequests);
        explicitRetry.Headers.TryAddWithoutValidation("Retry-After", "7");

        using HttpResponseMessage implicitRetry = new(HttpStatusCode.TooManyRequests);
        using HttpResponseMessage invalidRetryAfter = new(HttpStatusCode.TooManyRequests);
        invalidRetryAfter.Headers.TryAddWithoutValidation("Retry-After", "not-a-number");

        Assert.Equal(TimeSpan.FromSeconds(7), YahooFinanceHttpClient.GetRetryDelay(explicitRetry, 0));
        Assert.Equal(TimeSpan.FromSeconds(2), YahooFinanceHttpClient.GetRetryDelay(implicitRetry, 0));
        Assert.Equal(TimeSpan.FromSeconds(4), YahooFinanceHttpClient.GetRetryDelay(implicitRetry, 1));
        Assert.Equal(TimeSpan.FromSeconds(8), YahooFinanceHttpClient.GetRetryDelay(implicitRetry, 2));
        Assert.Equal(TimeSpan.FromSeconds(4), YahooFinanceHttpClient.GetRetryDelay(invalidRetryAfter, 1));
    }

    [Fact]
    public void YFinanceServerErrorMapping_ClassifiesHttpFailuresForClientDegradation()
    {
        HttpRequestException rateLimited = new("rate limited", null, HttpStatusCode.TooManyRequests);
        HttpRequestException requestTimeout = new("timeout", null, HttpStatusCode.RequestTimeout);
        HttpRequestException serverUnavailable = new("server unavailable", null, HttpStatusCode.ServiceUnavailable);
        TimeoutException timeout = new("operation timeout");
        InvalidOperationException internalError = new("bad payload");

        Assert.Equal(ProtocolErrorCodes.UpstreamThrottled, YFinance.NET.Server.Hosting.YFinanceServerProgram.MapErrorCode(rateLimited));
        Assert.Equal(ProtocolErrorCodes.Timeout, YFinance.NET.Server.Hosting.YFinanceServerProgram.MapErrorCode(requestTimeout));
        Assert.Equal(ProtocolErrorCodes.UpstreamUnavailable, YFinance.NET.Server.Hosting.YFinanceServerProgram.MapErrorCode(serverUnavailable));
        Assert.Equal(ProtocolErrorCodes.Timeout, YFinance.NET.Server.Hosting.YFinanceServerProgram.MapErrorCode(timeout));
        Assert.Equal(ProtocolErrorCodes.InternalError, YFinance.NET.Server.Hosting.YFinanceServerProgram.MapErrorCode(internalError));

        Assert.True(YFinance.NET.Server.Hosting.YFinanceServerProgram.IsRetryable(rateLimited));
        Assert.True(YFinance.NET.Server.Hosting.YFinanceServerProgram.IsRetryable(requestTimeout));
        Assert.True(YFinance.NET.Server.Hosting.YFinanceServerProgram.IsRetryable(serverUnavailable));
        Assert.True(YFinance.NET.Server.Hosting.YFinanceServerProgram.IsRetryable(timeout));
        Assert.False(YFinance.NET.Server.Hosting.YFinanceServerProgram.IsRetryable(internalError));
    }

    [Fact]
    public void YFinanceQuoteParsing_TreatsMalformedNumericFieldsAsMissing()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Yahoo numeric strings are invariant/dot-decimal. Run under a
            // comma-decimal culture to prove QuoteService keeps that contract.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            using JsonDocument document = JsonDocument.Parse(
                """
                {
                  "symbol": "BADNUM",
                  "regularMarketPrice": null,
                  "regularMarketPreviousClose": "0",
                  "regularMarketChange": "NaN",
                  "regularMarketChangePercent": "Infinity",
                  "regularMarketOpen": "-123.45",
                  "regularMarketDayHigh": "999999999999999999999999999999999999999999",
                  "regularMarketDayLow": 0,
                  "regularMarketVolume": "not-a-number",
                  "averageVolume": "92233720368547758079223372036854775807"
                }
                """);

            YQuoteSnapshot quote = QuoteService.CreateSnapshot(document.RootElement, "BADNUM");

            Assert.Equal("BADNUM", quote.Symbol);
            Assert.Null(quote.RegularMarketPrice);
            Assert.Equal(0m, quote.RegularMarketPreviousClose);
            Assert.Null(quote.RegularMarketChange);
            Assert.Null(quote.RegularMarketChangePercent);
            Assert.Equal(-123.45m, quote.RegularMarketOpen);
            Assert.Null(quote.RegularMarketDayHigh);
            Assert.Equal(0m, quote.RegularMarketDayLow);
            Assert.Null(quote.RegularMarketVolume);
            Assert.Null(quote.AverageVolume);
            Assert.Null(quote.ComputedChangePercent);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
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
    public async Task YFinanceRuntimeClientFactory_SerializesSharedClientWork()
    {
        // This test verifies factory scheduling only; the client argument is intentionally unused.
        using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
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

        Assert.Equal([1, 2], results.Order());
        Assert.Equal(1, maxConcurrent);
        Assert.Equal(0, concurrent);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_FailedOperationRetiresClientAndAllowsFollowUp()
    {
        using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();

        Task<int> failing = YFinanceRuntimeClientFactory.RunSerializedAsync<int>(
            "test-failure",
            (_, _) => Task.FromException<int>(new InvalidOperationException("forced failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);

        int followUp = await YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-follow-up",
            (client, _) =>
            {
                AssertYFinanceClientNotDisposed(client);
                return Task.FromResult(1);
            });

        Assert.Equal(1, followUp);
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_RecoveryResetRetiresSharedClientAfterActiveOperationCompletes()
    {
        using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
        TaskCompletionSource<YFinanceServerClient> activeClientSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resetInvoked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> activeOperation = YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-reset-active",
            async (client, token) =>
            {
                activeClientSeen.SetResult(client);
                await resetInvoked.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                AssertYFinanceClientNotDisposed(client);
                return 1;
            });

        await activeClientSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        YFinanceRuntimeClientFactory.ResetConnectionStateForRecovery("unit-test");
        resetInvoked.SetResult();

        Assert.Equal(1, await activeOperation);
        int followUp = await YFinanceRuntimeClientFactory.RunSerializedAsync(
            "test-reset-follow-up",
            (_, _) => Task.FromResult(2));
        Assert.Equal(2, followUp);
    }

    [Fact]
    public void RuntimeQuoteRecoveryGate_RequiresThresholdAndHonorsCooldown()
    {
        RuntimeQuoteRecoveryGate gate = new(10, TimeSpan.FromSeconds(30));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.False(gate.TryEnter(9, now));
        Assert.True(gate.TryEnter(10, now));
        gate.MarkResetSucceeded(now);
        gate.Exit();

        Assert.False(gate.TryEnter(10, now.AddSeconds(29)));
        Assert.True(gate.TryEnter(10, now.AddSeconds(31)));
        gate.Exit();
    }

    [Fact]
    public void RuntimeQuoteRecoveryGate_AllowsOnlyOneConcurrentReset()
    {
        RuntimeQuoteRecoveryGate gate = new(10, TimeSpan.FromSeconds(30));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(gate.TryEnter(10, now));
        Assert.False(gate.TryEnter(10, now));

        gate.Exit();

        Assert.True(gate.TryEnter(10, now));
        gate.Exit();
    }

    [Fact]
    public async Task YFinanceRuntimeClientFactory_TestSuppressionIsIsolatedPerAsyncFlow()
    {
        Assert.False(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
        TaskCompletionSource suppressedFlowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSuppressedFlow = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task suppressedFlow = Task.Run(async () =>
        {
            using IDisposable serverBypass = YFinanceRuntimeClientFactory.SuppressServerStartupForTests();
            Assert.True(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
            suppressedFlowReady.SetResult();
            await releaseSuppressedFlow.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
        });

        await suppressedFlowReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);

        Task<bool> siblingFlow = Task.Run(() => YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
        bool siblingSuppressed = await siblingFlow.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(siblingSuppressed);

        releaseSuppressedFlow.SetResult();
        await suppressedFlow.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(YFinanceRuntimeClientFactory.IsServerStartupSuppressedForTests);
    }

    [Fact]
    public void DefaultHttpClients_ReuseSharedHandlers()
    {
        SocketsHttpHandler treasuryHandler = TreasuryYieldCurveQuoteProvider.SharedHttpHandlerForTests;
        Assert.Same(treasuryHandler, TreasuryYieldCurveQuoteProvider.SharedHttpHandlerForTests);
        Assert.Equal(TimeSpan.FromMinutes(5), treasuryHandler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), treasuryHandler.PooledConnectionIdleTimeout);

        SocketsHttpHandler probeHandler = InternetProbeService.SharedProbeHandlerForTests;
        Assert.Same(probeHandler, InternetProbeService.SharedProbeHandlerForTests);
        Assert.Same(InternetProbeService.SharedProbeClientForTests, InternetProbeService.SharedProbeClientForTests);
        Assert.Equal(Timeout.InfiniteTimeSpan, InternetProbeService.SharedProbeClientForTests.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(5), probeHandler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), probeHandler.PooledConnectionIdleTimeout);

        SocketsHttpHandler exchangeHandler = ExchangePhotoCacheService.DefaultHttpHandlerForTests;
        Assert.Same(exchangeHandler, ExchangePhotoCacheService.DefaultHttpHandlerForTests);
        Assert.Equal(TimeSpan.FromMinutes(5), exchangeHandler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(30), exchangeHandler.PooledConnectionIdleTimeout);
    }

    [Fact]
    public async Task RateLimitGuard_SerializesConcurrentCallers()
    {
        using RateLimitGuard guard = new();
        TimeSpan minimumInterval = TimeSpan.FromMilliseconds(75);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => guard.WaitIfNeededAsync(minimumInterval))));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(220), $"Expected shared guard to space concurrent callers, elapsed {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task RateLimitGuard_CancellationDuringDelayDoesNotLeakGate()
    {
        using RateLimitGuard guard = new();
        await guard.WaitIfNeededAsync(TimeSpan.Zero);
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guard.WaitIfNeededAsync(TimeSpan.FromSeconds(5), cts.Token));

        await guard.WaitIfNeededAsync(TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, guard.CurrentCountForTests);
    }

    [Fact]
    public async Task RateLimitGuard_CancellationBeforeGateAcquisitionDoesNotLeakGate()
    {
        using RateLimitGuard guard = new();
        await guard.WaitIfNeededAsync(TimeSpan.Zero);
        Task holder = guard.WaitIfNeededAsync(TimeSpan.FromMilliseconds(300));
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guard.WaitIfNeededAsync(TimeSpan.Zero, cts.Token));

        await holder.WaitAsync(TimeSpan.FromSeconds(1));
        await guard.WaitIfNeededAsync(TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, guard.CurrentCountForTests);
    }

    [Fact]
    public async Task RateLimitGuard_AlreadyCancelledTokenDoesNotAcquireGate()
    {
        using RateLimitGuard guard = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guard.WaitIfNeededAsync(TimeSpan.Zero, cts.Token));

        Assert.Equal(1, guard.CurrentCountForTests);
    }

    [Fact]
    public async Task RateLimitGuard_DisposesAfterCompletedWaits()
    {
        using RateLimitGuard guard = new();

        await guard.WaitIfNeededAsync(TimeSpan.Zero);
    }

    [Fact]
    public async Task TickerInfoService_ResolvesUncachedSummariesWithBoundedParallelism()
    {
        string cacheRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        int concurrent = 0;
        int maxConcurrent = 0;
        int enteredSummaries = 0;
        object concurrencySync = new();
        TaskCompletionSource fourSummariesEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseSummaries = new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            YFinanceOptions options = new() { PersistentCacheRootPath = cacheRoot };
            TickerInfoService service = new(
                (symbol, _) => Task.FromResult<YQuoteSnapshot?>(CreateYFinanceQuote(symbol)),
                (symbols, _) => Task.FromResult<IReadOnlyDictionary<string, YQuoteSnapshot>>(
                    symbols.ToDictionary(static symbol => symbol, CreateYFinanceQuote, StringComparer.Ordinal)),
                async (_, _, token) =>
                {
                    int now = Interlocked.Increment(ref concurrent);
                    lock (concurrencySync)
                    {
                        maxConcurrent = Math.Max(maxConcurrent, now);
                    }

                    if (Interlocked.Increment(ref enteredSummaries) == 4)
                        fourSummariesEntered.SetResult();

                    await releaseSummaries.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    Interlocked.Decrement(ref concurrent);
                    return null;
                },
                options);

            Task<IReadOnlyDictionary<string, YTickerInfo?>> infosTask = service.GetInfosAsync(["AAA", "BBB", "CCC", "DDD", "EEE", "FFF"]);

            await fourSummariesEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, maxConcurrent);
            releaseSummaries.SetResult();
            IReadOnlyDictionary<string, YTickerInfo?> infos = await infosTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(6, infos.Count);
            Assert.All(infos.Values, Assert.NotNull);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                DeleteDirectoryWithRetry(cacheRoot);
        }
    }

    [Fact]
    public async Task HybridHistoricalDataProvider_FetchesPendingSymbolsWithBoundedParallelism()
    {
        InMemoryHistoricalCacheService cache = new();
        int concurrent = 0;
        int maxConcurrent = 0;
        int enteredFetches = 0;
        object concurrencySync = new();
        TaskCompletionSource twoFetchesEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFetches = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HybridHistoricalDataProvider provider = new(
            cache,
            async (requestSymbol, _, _, _, _, token) =>
            {
                int now = Interlocked.Increment(ref concurrent);
                lock (concurrencySync)
                {
                    maxConcurrent = Math.Max(maxConcurrent, now);
                }

                if (Interlocked.Increment(ref enteredFetches) == 2)
                    twoFetchesEntered.SetResult();

                await releaseFetches.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                Interlocked.Decrement(ref concurrent);
                return CreateHistoryResponse(requestSymbol);
            });

        Task<IReadOnlyList<TickerHistorySnapshot>> historyTask = provider.GetHistoryAsync(
            ["AAA", "BBB", "CCC", "DDD", "EEE", "FFF"],
            30);

        await twoFetchesEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, maxConcurrent);
        releaseFetches.SetResult();
        IReadOnlyList<TickerHistorySnapshot> snapshots = await historyTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(6, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Single(snapshot.Points));
        Assert.Equal(2, maxConcurrent);
        Assert.Equal(6, cache.SavedCount);
    }

    [Fact]
    public async Task HybridHistoricalDataProvider_RetriesTransientHistoryFetch()
    {
        InMemoryHistoricalCacheService cache = new();
        int attempts = 0;
        List<string> operationIds = [];
        HybridHistoricalDataProvider provider = new(
            cache,
            (requestSymbol, _, _, _, operationId, _) =>
            {
                operationIds.Add(operationId);
                if (Interlocked.Increment(ref attempts) == 1)
                    return Task.FromException<HistoryResponseDto>(new HttpRequestException("transient"));

                return Task.FromResult(CreateHistoryResponse(requestSymbol));
            });

        IReadOnlyList<TickerHistorySnapshot> snapshots = await provider.GetHistoryAsync(["AAA"], 30);

        Assert.Equal(2, attempts);
        Assert.Equal(2, operationIds.Distinct(StringComparer.Ordinal).Count());
        TickerHistorySnapshot snapshot = Assert.Single(snapshots);
        Assert.Single(snapshot.Points);
        Assert.Equal(1, cache.SavedCount);
    }

    [Fact]
    public async Task HybridHistoricalDataProvider_FallsBackToStaleCacheWhenFetchFails()
    {
        InMemoryHistoricalCacheService cache = new();
        TickerHistorySnapshot stale = new()
        {
            Symbol = "AAA",
            LookbackDays = 30,
            FetchTimestampUtc = DateTimeOffset.UtcNow.AddDays(-2),
            Points = [new HistoricalPricePoint { TimestampUtc = DateTimeOffset.UtcNow.AddDays(-2), Close = 123m }]
        };
        cache.Seed(stale);
        HybridHistoricalDataProvider provider = new(
            cache,
            (_, _, _, _, _, _) => Task.FromException<HistoryResponseDto>(new InvalidOperationException("forced failure")),
            TimeSpan.FromMinutes(1));

        IReadOnlyList<TickerHistorySnapshot> snapshots = await provider.GetHistoryAsync(["AAA"], 30);

        TickerHistorySnapshot snapshot = Assert.Single(snapshots);
        Assert.Same(stale, snapshot);
    }

    [Fact]
    public void YFinanceMemoryTtlCache_ExpiresEntriesAndRemovesThem()
    {
        MemoryTtlCache<string> cache = new();
        cache.Set("quote:AAPL", "fresh", TimeSpan.FromMinutes(10));
        Assert.True(cache.TryGet("quote:AAPL", out string? fresh));
        Assert.Equal("fresh", fresh);

        // Zero TTL is the explicit contract for immediate expiration in this
        // small YFinance.NET cache wrapper.
        cache.Set("quote:AAPL", "expired", TimeSpan.Zero);

        Assert.False(cache.TryGet("quote:AAPL", out _));
    }

    [Fact]
    public async Task YFinancePersistentTtlCache_DoesNotReturnExpiredEntries()
    {
        string cacheRoot = Path.Combine(Path.GetTempPath(), "dnppv-yfinance-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // PersistentTtlCache is stateless over individual file operations and
            // does not implement IDisposable.
            PersistentTtlCache<string> cache = new(cacheRoot);

            await cache.SetAsync("history:AAPL", "fresh", TimeSpan.FromMinutes(10));
            Assert.Equal("fresh", await cache.GetAsync("history:AAPL"));

            await cache.SetAsync("history:AAPL", "expired", TimeSpan.Zero);

            Assert.Null(await cache.GetAsync("history:AAPL"));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
                DeleteDirectoryWithRetry(cacheRoot);
        }
    }

    [Fact]
    public void MarketDataCacheOwnership_RemainsInYFinanceNetWithoutLegacyQuoteCacheService()
    {
        // Deliberate architectural tripwire: app-level quote caching was removed
        // because YFinance.NET owns quote caching. A reintroduced type with this
        // name should force an explicit review.
        Assert.DoesNotContain("QuoteCacheService", typeof(YahooFinanceQuoteProvider).Assembly.GetTypes().Select(static type => type.Name));
        Assert.DoesNotContain("QuoteCacheService", typeof(StartupCoordinator).Assembly.GetTypes().Select(static type => type.Name));
    }

    [Fact]
    public void RuntimeQuoteSeedStore_PublishesAndConsumesQuotesOnce()
    {
        RuntimeQuoteSeedStore.ConsumeAll();
        RuntimeQuoteSeedStore.Publish(
        [
            new QuoteSnapshot { Symbol = "AAPL", Last = 190m, PreviousClose = 189m, FetchTimestampUtc = DateTimeOffset.UtcNow }
        ]);

        IReadOnlyDictionary<string, QuoteSnapshot> first = RuntimeQuoteSeedStore.ConsumeAll();
        IReadOnlyDictionary<string, QuoteSnapshot> second = RuntimeQuoteSeedStore.ConsumeAll();

        Assert.Single(first);
        Assert.True(first.ContainsKey("AAPL"));
        Assert.Empty(second);
    }

    [Fact]
    public void StartupCoordinator_NoLongerContainsDedicatedStartupWarmupPath()
    {
        string coordinatorPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs");
        string source = File.ReadAllText(Path.GetFullPath(coordinatorPath));

        Assert.DoesNotContain("WarmStartupYahooQuotesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupWarmupBatch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDedicatedYahooWarmupSymbols", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupCoordinator_DedicatedRuntimeRequestsUsePipelinedSingleSymbolQueue()
    {
        string coordinatorPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs");
        string source = File.ReadAllText(Path.GetFullPath(coordinatorPath));

        Assert.Contains(
            "private List<string> TakeSequentialRequestSymbols(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private const int SequentialQuotePipelineDepth = 4;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "QueueQuotePipelineRequests(orderedSymbols, yahooFinanceProvider, cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DrainCompletedQuotePipelineAsync(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetQuotesAsync([symbol], CancellationToken.None)", source, StringComparison.Ordinal);
        Assert.Contains(
            "yahooFinanceProvider.GetQuotesAsync([symbol], cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("SequentialQuoteCancelled", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsDedicatedYahooSymbolCoolingDown",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DataSourceSymbolEligibility.IsEligible(providerPlan.Kind, symbol)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartupCoordinator_QuotePipelineAccessIsSerializedAndDrainedBeforeAwait()
    {
        string coordinatorPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "StartupCoordinator.cs");
        string source = File.ReadAllText(Path.GetFullPath(coordinatorPath));

        Assert.Contains("private readonly object _pendingQuotePipelineGate = new();", source, StringComparison.Ordinal);
        Assert.Contains("lock (_pendingQuotePipelineGate)", source, StringComparison.Ordinal);
        Assert.Contains("List<PendingQuoteRequest> completedRequests = [];", source, StringComparison.Ordinal);
        Assert.Contains("completedRequests.Add(pending);", source, StringComparison.Ordinal);
        Assert.Contains("foreach (PendingQuoteRequest pending in completedRequests)", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<QuoteSnapshot> fetched = await pending.Task;", source, StringComparison.Ordinal);
        Assert.Contains("QuotePipelineSnapshot pipelineSnapshot = SnapshotQuotePipeline(orderedSymbols);", source, StringComparison.Ordinal);
        Assert.Contains("private QuotePipelineSnapshot SnapshotQuotePipeline(IReadOnlyList<string> orderedSymbols)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("orderedSymbols.Except(_pendingQuotePipeline.Keys", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupCoordinator_ConcurrentQuotePipelineDrainsClaimEachCompletedRequestOnce()
    {
        StartupCoordinator coordinator = new();
        ControlledQuoteProvider provider = new();
        string[] symbols = ["AAPL", "MSFT", "VOO", "QUAL"];

        MethodInfo queueMethod = typeof(StartupCoordinator).GetMethod(
            "QueueQuotePipelineRequests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo drainMethod = typeof(StartupCoordinator).GetMethod(
            "DrainCompletedQuotePipelineAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo pendingField = typeof(StartupCoordinator).GetField(
            "_pendingQuotePipeline",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        queueMethod.Invoke(coordinator, [symbols, provider, CancellationToken.None]);
        Assert.Equal(symbols.Length, provider.RequestedSymbols.Count);

        foreach (string symbol in symbols)
            provider.Complete(symbol, new QuoteSnapshot { Symbol = symbol, Last = 100m, FetchTimestampUtc = DateTimeOffset.UtcNow });

        const int workerCount = 8;
        // VM validation can run this gate while the build host is CPU-saturated; keep this roomy but bounded.
        TimeSpan rendezvousTimeout = TimeSpan.FromSeconds(30);
        Barrier? startBarrier = null;
        Task<(bool Rendezvous, int CompletedCount, int ResultCount)>[] drains;
        try
        {
            startBarrier = new Barrier(workerCount + 1);
            drains = Enumerable.Range(0, workerCount)
                .Select(_ => Task.Run(async () =>
                {
                    bool rendezvous = startBarrier.SignalAndWait(rendezvousTimeout);
                    Dictionary<string, QuoteSnapshot> results = new(StringComparer.OrdinalIgnoreCase);
                    object taskObject = drainMethod.Invoke(coordinator, [results])!;
                    await (Task)taskObject;
                    object drainResult = taskObject.GetType().GetProperty("Result")!.GetValue(taskObject)!;
                    int completedCount = (int)drainResult.GetType().GetProperty("CompletedCount")!.GetValue(drainResult)!;
                    return (rendezvous, completedCount, results.Count);
                }))
                .ToArray();

            bool mainRendezvous = startBarrier.SignalAndWait(rendezvousTimeout);

            (bool Rendezvous, int CompletedCount, int ResultCount)[] outcomes = await Task.WhenAll(drains);
            object pending = pendingField.GetValue(coordinator)!;
            int pendingCount = (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;

            Assert.True(mainRendezvous, "The main test thread should rendezvous with all concurrent drain workers.");
            Assert.All(outcomes, outcome => Assert.True(outcome.Rendezvous, "All concurrent drain workers should rendezvous before draining the pipeline."));
            Assert.Equal(symbols.Length, outcomes.Sum(outcome => outcome.CompletedCount));
            Assert.Equal(symbols.Length, outcomes.Sum(outcome => outcome.ResultCount));
            Assert.Single(outcomes.Where(outcome => outcome.CompletedCount > 0));
            Assert.Equal(0, pendingCount);
        }
        finally
        {
            startBarrier?.Dispose();
        }
    }

    [Fact]
    public async Task StartupCoordinator_QuotePipelineLeavesIncompleteRequestsForLaterDrain()
    {
        StartupCoordinator coordinator = new();
        ControlledQuoteProvider provider = new();
        string[] symbols = ["AAPL", "MSFT", "VOO", "QUAL"];

        MethodInfo queueMethod = typeof(StartupCoordinator).GetMethod(
            "QueueQuotePipelineRequests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo drainMethod = typeof(StartupCoordinator).GetMethod(
            "DrainCompletedQuotePipelineAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo pendingField = typeof(StartupCoordinator).GetField(
            "_pendingQuotePipeline",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        queueMethod.Invoke(coordinator, [symbols, provider, CancellationToken.None]);
        provider.Complete("AAPL", new QuoteSnapshot { Symbol = "AAPL", Last = 100m, FetchTimestampUtc = DateTimeOffset.UtcNow });
        provider.Complete("MSFT", new QuoteSnapshot { Symbol = "MSFT", Last = 200m, FetchTimestampUtc = DateTimeOffset.UtcNow });

        Dictionary<string, QuoteSnapshot> firstResults = new(StringComparer.OrdinalIgnoreCase);
        object firstTaskObject = drainMethod.Invoke(coordinator, [firstResults])!;
        await (Task)firstTaskObject;

        object pending = pendingField.GetValue(coordinator)!;
        int pendingAfterFirstDrain = (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;
        Assert.Equal(2, firstResults.Count);
        Assert.Equal(2, pendingAfterFirstDrain);

        provider.Complete("VOO", new QuoteSnapshot { Symbol = "VOO", Last = 300m, FetchTimestampUtc = DateTimeOffset.UtcNow });
        provider.Complete("QUAL", new QuoteSnapshot { Symbol = "QUAL", Last = 400m, FetchTimestampUtc = DateTimeOffset.UtcNow });

        Dictionary<string, QuoteSnapshot> secondResults = new(StringComparer.OrdinalIgnoreCase);
        object secondTaskObject = drainMethod.Invoke(coordinator, [secondResults])!;
        await (Task)secondTaskObject;

        int pendingAfterSecondDrain = (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;
        Assert.Equal(2, secondResults.Count);
        Assert.Equal(0, pendingAfterSecondDrain);
    }

    [Fact]
    public void VisualizerRefreshTimer_UsesOneSecondAsyncQuoteDispatchPath()
    {
        string controlPath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Controls", "VisualizerSceneControl.xaml.cs");
        string source = File.ReadAllText(Path.GetFullPath(controlPath));

        Assert.Contains(
            "_refreshTimer.Tick += (_, _) => DispatchNextRuntimeQuoteRequestSafe();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteDispatchInterval = TimeSpan.FromSeconds(1)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Runtime quotes intentionally use a fixed one-at-a-time transport cadence",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_refreshTimer.Interval = RuntimeQuoteDispatchInterval;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await RefreshSceneAsync(preserveLayout: false, fullAncillaryRefresh: true);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RefreshSceneAsync bypassed progressive quote scene because the async runtime quote loop owns ordinary quote cadence.",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Task<IReadOnlyList<QuoteSnapshot>> requestTask = _runtimeQuoteProvider.GetQuotesAsync([symbol], requestCancellation.Token);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dispatcher.InvokeAsync(() => ApplyCompletedRuntimeQuote(symbol, task))",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteRequestTimeout = TimeSpan.FromSeconds(15)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeTapeStructuralSyncInterval = TimeSpan.FromSeconds(5)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplyQuotesToDisplayedTapeItems(deltaQuotes.Values)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DateTimeOffset.UtcNow - _lastFullTapeSyncUtc > RuntimeTapeStructuralSyncInterval",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "PruneStaleRuntimeQuoteRequests();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteRequestTimedOut",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteLoopHeartbeat",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteTransportReset",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "YFinanceRuntimeClientFactory.ResetConnectionStateForRecovery",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteRequestCompletionIgnored",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "RuntimeQuoteDispatchSkipped",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (_inFlightQuoteRequests.Count > 0)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "TraceRuntimeQuoteDispatchSkippedIfDue(\"waiting_for_in_flight_request\")",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RunStartupWarmupAsync",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_refreshTimer.Tick += async (_, _) => await RefreshSceneAsync(preserveLayout: true, fullAncillaryRefresh: false);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_refreshTimer.Interval = TimeSpan.FromSeconds(GetRefreshSeconds())",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeQuoteInFlightTracker_PrunesTimedOutRequestsAndCancelsThem()
    {
        RuntimeQuoteInFlightTracker<IReadOnlyList<QuoteSnapshot>> tracker = new(StringComparer.OrdinalIgnoreCase);
        TaskCompletionSource<IReadOnlyList<QuoteSnapshot>> request = new();
        CancellationTokenSource cancellation = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        tracker.Add("VOO", request.Task, now - TimeSpan.FromSeconds(16), cancellation);

        IReadOnlyList<RuntimeQuoteTimedOutRequest<IReadOnlyList<QuoteSnapshot>>> timedOut =
            tracker.PruneStale(now, TimeSpan.FromSeconds(15));

        Assert.Single(timedOut);
        Assert.Equal("VOO", timedOut[0].Symbol);
        Assert.False(tracker.Contains("VOO"));
        Assert.Equal(0, tracker.Count);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void RuntimeQuoteInFlightTracker_IgnoresStaleCompletionAfterSymbolWasRequeued()
    {
        RuntimeQuoteInFlightTracker<IReadOnlyList<QuoteSnapshot>> tracker = new(StringComparer.OrdinalIgnoreCase);
        TaskCompletionSource<IReadOnlyList<QuoteSnapshot>> oldRequest = new();
        TaskCompletionSource<IReadOnlyList<QuoteSnapshot>> newRequest = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        tracker.Add("QUAL", oldRequest.Task, now - TimeSpan.FromSeconds(16), new CancellationTokenSource());
        tracker.PruneStale(now, TimeSpan.FromSeconds(15));
        tracker.Add("QUAL", newRequest.Task, now, new CancellationTokenSource());

        Assert.False(tracker.TryComplete("QUAL", oldRequest.Task, out _));
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.TryComplete("QUAL", newRequest.Task, out _));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void RuntimeQuoteInFlightTracker_CancelAndClearCancelsAllPendingRequests()
    {
        RuntimeQuoteInFlightTracker<IReadOnlyList<QuoteSnapshot>> tracker = new(StringComparer.OrdinalIgnoreCase);
        CancellationTokenSource firstCancellation = new();
        CancellationTokenSource secondCancellation = new();

        tracker.Add("VOO", Task.FromResult<IReadOnlyList<QuoteSnapshot>>([]), DateTimeOffset.UtcNow, firstCancellation);
        tracker.Add("QUAL", Task.FromResult<IReadOnlyList<QuoteSnapshot>>([]), DateTimeOffset.UtcNow, secondCancellation);

        tracker.CancelAndClear();

        Assert.Equal(0, tracker.Count);
        Assert.True(firstCancellation.IsCancellationRequested);
        Assert.True(secondCancellation.IsCancellationRequested);
    }

    [Fact]
    public void NtpTimeService_BoundsDnsAndHostTimeouts()
    {
        string servicePath = Path.Combine(
            GetRepoRoot(),
            "src", "PortfolioSaver.Presentation", "Services", "NtpTimeService.cs");
        string source = File.ReadAllText(Path.GetFullPath(servicePath));

        Assert.Contains("DnsTimeout", source, StringComparison.Ordinal);
        Assert.Contains("PerHostTimeout", source, StringComparison.Ordinal);
        Assert.Contains("ResolveHostAsync(host, hostTimeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("Dns.GetHostAddressesAsync(host, dnsTimeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("HostTimeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderBudgetLedgerService_SerializesConcurrentReservationsAndPersistsValidLedger()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string ledgerPath = Path.Combine(tempRoot, "provider-query-usage.json");
        try
        {
            ProviderBudgetLedgerService service = new(ledgerPath);
            DataSourcePolicySettings policy = new()
            {
                Kind = DataSourceKind.YahooFinance,
                MaxQueriesPerHour = 50,
                MaxQueriesPerDay = 50
            };
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            // Use one timestamp and a barrier so this exercises concurrent lock/persistence behavior,
            // not task scheduling order or reuse-interval logic.
            using Barrier startGate = new(participantCount: 9);
            Task<bool>[] reservationTasks = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    startGate.SignalAndWait();
                    return service.TryReserve(policy, 1, TimeSpan.Zero, nowUtc);
                }))
                .ToArray();
            startGate.SignalAndWait();
            bool[] reservations = await Task.WhenAll(reservationTasks);

            Assert.Equal(8, reservations.Count(result => result));
            string json = File.ReadAllText(ledgerPath);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonProperty entryProperty = Assert.Single(document.RootElement.GetProperty("Entries").EnumerateObject());
            Assert.Equal(8, entryProperty.Value.GetProperty("QueryTimestampsUtc").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, entryProperty.Value.GetProperty("CooldownUntilUtc").ValueKind);
            Assert.DoesNotContain(".tmp", string.Join('|', Directory.EnumerateFiles(tempRoot).Select(Path.GetFileName)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void ProviderBudgetLedgerService_SkipsStaleSnapshotWrites()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string ledgerPath = Path.Combine(tempRoot, "provider-query-usage.json");
        try
        {
            ProviderBudgetLedgerService service = new(ledgerPath);
            MethodInfo saveLedger = typeof(ProviderBudgetLedgerService).GetMethod("SaveLedger", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(ProviderBudgetLedgerService), "SaveLedger");

            object staleLedger = CreateProviderBudgetLedgerSnapshot(1);
            object latestLedger = CreateProviderBudgetLedgerSnapshot(2);

            saveLedger.Invoke(service, [latestLedger, 2L]);
            saveLedger.Invoke(service, [staleLedger, 1L]);

            string json = File.ReadAllText(ledgerPath);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonProperty entryProperty = Assert.Single(document.RootElement.GetProperty("Entries").EnumerateObject());
            Assert.Equal(2, entryProperty.Value.GetProperty("QueryTimestampsUtc").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path))
            return;

        IOException? lastIoException = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastIoException = ex;
                Thread.Sleep(100);
            }
        }

        if (lastIoException is not null)
            throw lastIoException;

        Directory.Delete(path, recursive: true);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root from test AppContext.BaseDirectory.");
    }

    private static object CreateProviderBudgetLedgerSnapshot(int timestampCount)
    {
        Type serviceType = typeof(ProviderBudgetLedgerService);
        Type ledgerType = serviceType.GetNestedType("ProviderBudgetLedger", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(nameof(ProviderBudgetLedgerService), "ProviderBudgetLedger");
        Type entryType = serviceType.GetNestedType("ProviderBudgetEntry", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(nameof(ProviderBudgetLedgerService), "ProviderBudgetEntry");
        object ledger = Activator.CreateInstance(ledgerType)
            ?? throw new InvalidOperationException("Could not create provider budget ledger.");
        object entry = Activator.CreateInstance(entryType)
            ?? throw new InvalidOperationException("Could not create provider budget entry.");

        PropertyInfo timestampsProperty = entryType.GetProperty("QueryTimestampsUtc")
            ?? throw new MissingMemberException("ProviderBudgetEntry", "QueryTimestampsUtc");
        timestampsProperty.SetValue(entry, Enumerable.Range(0, timestampCount)
            .Select(offset => DateTimeOffset.UtcNow.AddSeconds(offset))
            .ToList());

        PropertyInfo entriesProperty = ledgerType.GetProperty("Entries")
            ?? throw new MissingMemberException("ProviderBudgetLedger", "Entries");
        if (entriesProperty.GetValue(ledger) is not System.Collections.IDictionary entries)
            throw new InvalidOperationException("Provider budget entries dictionary was not available.");

        entries.Add(DataSourceKind.YahooFinance, entry);
        return ledger;
    }

    private static void AssertYFinanceClientNotDisposed(YFinanceServerClient client)
    {
        FieldInfo connectGateField = typeof(YFinanceServerClient).GetField(
            "_connectGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        SemaphoreSlim connectGate = (SemaphoreSlim)connectGateField.GetValue(client)!;

        Assert.True(connectGate.Wait(0));
        connectGate.Release();
    }

    private static HistoryResponseDto CreateHistoryResponse(string requestSymbol)
        => new(
            requestSymbol,
            [new HistoryBarDto(DateTimeOffset.UtcNow.AddDays(-1), 1m, 2m, 1m, 2m, 100)],
            new HistoryMetadataDto(null, null, "USD", "America/New_York", "EST", null, null, null),
            new CacheMetadataDto("test", 0, false));

    private static YQuoteSnapshot CreateYFinanceQuote(string symbol)
        => new(
            symbol,
            ShortName: symbol,
            LongName: symbol,
            DisplayName: symbol,
            Currency: "USD",
            Exchange: "TEST",
            ExchangeTimezoneName: "America/New_York",
            ExchangeTimezoneShortName: "EST",
            QuoteType: "EQUITY",
            MarketState: "REGULAR",
            RegularMarketPrice: 100m,
            RegularMarketPreviousClose: 99m,
            RegularMarketOpen: 99m,
            RegularMarketDayHigh: 101m,
            RegularMarketDayLow: 98m,
            RegularMarketChange: 1m,
            RegularMarketChangePercent: 1m,
            FiftyTwoWeekLow: 80m,
            FiftyTwoWeekHigh: 120m,
            FiftyDayAverage: 100m,
            TwoHundredDayAverage: 95m,
            RegularMarketVolume: 1000,
            AverageVolume: 1000,
            AverageVolume10Day: 1000,
            SharesOutstanding: 1000000,
            MarketCap: 100000000,
            TrailingPe: 20m,
            ForwardPe: 18m,
            Raw: JsonDocument.Parse("{}").RootElement.Clone());

    private sealed class ControlledQuoteProvider : IQuoteProvider
    {
        private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<QuoteSnapshot>>> _pending = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestedSymbols { get; } = [];

        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
        {
            string symbol = Assert.Single(symbols);
            RequestedSymbols.Add(symbol);
            TaskCompletionSource<IReadOnlyList<QuoteSnapshot>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(symbol, completion);
            return completion.Task;
        }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void Complete(string symbol, QuoteSnapshot quote)
            => _pending[symbol].SetResult([quote]);
    }

    private sealed class InMemoryHistoricalCacheService : IHistoricalCacheService
    {
        private readonly ConcurrentDictionary<string, TickerHistorySnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

        public int SavedCount => _snapshots.Count;

        public void Seed(TickerHistorySnapshot snapshot)
        {
            _snapshots[snapshot.Symbol] = snapshot;
        }

        public Task<TickerHistorySnapshot?> LoadAsync(string symbol, CancellationToken cancellationToken = default)
        {
            _snapshots.TryGetValue(symbol, out TickerHistorySnapshot? snapshot);
            return Task.FromResult(snapshot);
        }

        public Task SaveAsync(TickerHistorySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.Symbol] = snapshot;
            return Task.CompletedTask;
        }

        public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
