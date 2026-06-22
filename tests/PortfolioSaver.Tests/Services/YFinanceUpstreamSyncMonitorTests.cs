// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Net;
using System.Text.Json;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class YFinanceUpstreamSyncMonitorTests
{
    [Fact]
    public async Task CheckOnceAsync_WhenLatestCommitMatches_LogsCurrent()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => JsonResponse(
            "[{\"sha\":\"" + YFinanceUpstreamSyncMetadata.ReviewedCommit + "\",\"commit\":{\"committer\":{\"date\":\"2026-05-28T20:01:28Z\"}},\"html_url\":\"https://github.com/ranaroussi/yfinance/commit/" + YFinanceUpstreamSyncMetadata.ReviewedCommit + "\"}]")));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        Assert.Contains(trace.Events, e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckStart");
        Assert.Contains(trace.Events, e => e.Level == "INFO" && e.EventName == "UpstreamSyncCurrent");
        Assert.DoesNotContain(trace.Events, e => e.Level == "WARN" && e.EventName == "UpstreamYFinanceNewerThanReviewed");
    }

    [Fact]
    public async Task CheckOnceAsync_WhenGitHubReturnsSingleCommitObject_LogsCurrent()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => JsonResponse(
            "{\"sha\":\"" + YFinanceUpstreamSyncMetadata.ReviewedCommit + "\",\"commit\":{\"committer\":{\"date\":\"2026-05-28T20:01:28Z\"}},\"html_url\":\"https://github.com/ranaroussi/yfinance/commit/" + YFinanceUpstreamSyncMetadata.ReviewedCommit + "\"}")));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        Assert.Contains(trace.Events, e => e.Level == "INFO" && e.EventName == "UpstreamSyncCurrent");
    }

    [Fact]
    public async Task CheckOnceAsync_WhenLatestCommitDiffers_LogsWarningWithBothCommits()
    {
        const string latestCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => JsonResponse(
            "[{\"sha\":\"" + latestCommit + "\",\"commit\":{\"committer\":{\"date\":\"2026-06-12T12:00:00Z\"}},\"html_url\":\"https://github.com/ranaroussi/yfinance/commit/" + latestCommit + "\"}]")));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        RecordedEvent warning = Assert.Single(trace.Events.Where(e => e.Level == "WARN" && e.EventName == "UpstreamYFinanceNewerThanReviewed"));
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedCommit, warning.Fields["reviewed_commit"]);
        Assert.Equal(latestCommit, warning.Fields["upstream_commit"]);
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedVersion, warning.Fields["reviewed_version"]);
    }

    [Fact]
    public async Task CheckOnceAsync_WhenGitHubFails_LogsNonFatalUnavailable()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        RecordedEvent unavailable = Assert.Single(trace.Events.Where(e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckUnavailable"));
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedCommit, unavailable.Fields["reviewed_commit"]);
        Assert.Contains("Response status code", unavailable.Fields["message"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckOnceAsync_WhenGitHubResponseIsEmpty_LogsNonFatalUnavailable()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => JsonResponse("[]")));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        RecordedEvent unavailable = Assert.Single(trace.Events.Where(e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckUnavailable"));
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedCommit, unavailable.Fields["reviewed_commit"]);
    }

    [Fact]
    public async Task CheckOnceAsync_WhenGitHubResponseIsMalformed_LogsNonFatalUnavailable()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => JsonResponse("""{"broken":true}""")));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        RecordedEvent unavailable = Assert.Single(trace.Events.Where(e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckUnavailable"));
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedCommit, unavailable.Fields["reviewed_commit"]);
    }

    [Fact]
    public async Task CheckOnceAsync_WhenGitHubTimesOut_LogsNonFatalUnavailable()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return JsonResponse("[]");
        }));
        YFinanceOptions options = new()
        {
            TraceSink = trace,
            EnableUpstreamSyncCheck = true,
            UpstreamSyncCheckTimeout = TimeSpan.FromMilliseconds(10)
        };
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);

        await monitor.CheckOnceAsync();

        Assert.Contains(trace.Events, e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckUnavailable");
    }

    [Fact]
    public async Task RunPeriodicAsync_WhenDisabled_LogsDisabledAndDoesNotCallHttp()
    {
        RecordingTraceSink trace = new();
        using HttpClient client = new(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called when disabled.")));
        YFinanceOptions options = new()
        {
            TraceSink = trace,
            EnableUpstreamSyncCheck = false
        };
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await monitor.RunPeriodicAsync(cts.Token);

        Assert.Contains(trace.Events, e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckDisabled");
    }

    [Fact]
    public async Task RunPeriodicAsync_WhenCancelledDuringHttp_ExitsCleanly()
    {
        RecordingTraceSink trace = new();
        TaskCompletionSource requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HttpClient client = new(new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            requestStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return JsonResponse("[]");
        }));
        YFinanceOptions options = CreateOptions(trace);
        using YFinanceUpstreamSyncMonitor monitor = new(options, new YFinanceTrace(trace), client);
        using CancellationTokenSource cts = new();

        Task monitorTask = monitor.RunPeriodicAsync(cts.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await monitorTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(trace.Events, e => e.Level == "INFO" && e.EventName == "UpstreamSyncCheckStart");
    }

    private static YFinanceOptions CreateOptions(IYFinanceTraceSink trace)
        => new()
        {
            TraceSink = trace,
            EnableUpstreamSyncCheck = true,
            UpstreamSyncCheckInterval = TimeSpan.FromHours(24),
            UpstreamSyncCheckTimeout = TimeSpan.FromSeconds(5)
        };

    [Fact]
    public void UpstreamSyncMetadata_ConstantsMatchProjectJson()
    {
        string metadataPath = FindRepoFile("YFinance.net", "upstream-sync.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        JsonElement root = document.RootElement;

        Assert.Equal(YFinanceUpstreamSyncMetadata.UpstreamRepository, root.GetProperty("upstreamRepository").GetString());
        Assert.Equal(YFinanceUpstreamSyncMetadata.ForkRepository, root.GetProperty("forkRepository").GetString());
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedCommit, root.GetProperty("reviewedCommit").GetString());
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedCommitDate, root.GetProperty("reviewedCommitDate").GetString());
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedVersion, root.GetProperty("reviewedVersion").GetString());
        Assert.Equal(YFinanceUpstreamSyncMetadata.ReviewedByCr, root.GetProperty("reviewedByCr").GetString());
    }

    private static string FindRepoFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(relativeParts));
    }

    private static HttpResponseMessage JsonResponse(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = (request, _) => Task.FromResult(responder(request));

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }

    private sealed class RecordingTraceSink : IYFinanceTraceSink
    {
        public List<RecordedEvent> Events { get; } = [];

        public void InfoState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
            => Events.Add(new RecordedEvent("INFO", source, eventName, fields.ToDictionary(static field => field.Key, static field => field.Value)));

        public void WarnState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields)
            => Events.Add(new RecordedEvent("WARN", source, eventName, fields.ToDictionary(static field => field.Key, static field => field.Value)));

        public void ErrorState(string source, string eventName, IEnumerable<KeyValuePair<string, object?>> fields, Exception? exception = null)
            => Events.Add(new RecordedEvent("ERROR", source, eventName, fields.ToDictionary(static field => field.Key, static field => field.Value)));
    }

    private sealed record RecordedEvent(
        string Level,
        string Source,
        string EventName,
        IReadOnlyDictionary<string, object?> Fields);
}
