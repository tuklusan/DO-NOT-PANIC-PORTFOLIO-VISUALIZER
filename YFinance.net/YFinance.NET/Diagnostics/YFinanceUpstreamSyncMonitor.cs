using System.Net.Http.Headers;
using System.Text.Json;
using YFinance.NET.Config;

namespace YFinance.NET.Diagnostics;

public sealed class YFinanceUpstreamSyncMonitor : IDisposable
{
    private readonly YFinanceOptions _options;
    private readonly YFinanceTrace _trace;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public YFinanceUpstreamSyncMonitor(YFinanceOptions options, YFinanceTrace? trace = null, HttpClient? httpClient = null)
    {
        _options = options;
        _trace = trace ?? new YFinanceTrace(options.TraceSink);
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        // Do not clear headers on injected clients; tests may provide their own.
        // Product construction uses an owned client with only this diagnostic User-Agent.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("YFinance.NET-UpstreamSyncMonitor", "1.0"));
    }

    public Task RunPeriodicAsync(CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            if (!_options.EnableUpstreamSyncCheck)
            {
                TraceBaseline("UpstreamSyncCheckDisabled");
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckOnceAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await Task.Delay(GetSafeInterval(), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }, CancellationToken.None);

    public async Task CheckOnceAsync(CancellationToken cancellationToken = default)
    {
        TraceBaseline("UpstreamSyncCheckStart");
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GetSafeTimeout());
            UpstreamCommit latest = await FetchLatestCommitAsync(timeout.Token).ConfigureAwait(false);
            if (string.Equals(latest.Sha, YFinanceUpstreamSyncMetadata.ReviewedCommit, StringComparison.OrdinalIgnoreCase))
            {
                _trace.InfoState(
                    "YFinance.UpstreamSync",
                    "UpstreamSyncCurrent",
                    ("reviewed_commit", YFinanceUpstreamSyncMetadata.ReviewedCommit),
                    ("reviewed_commit_date", YFinanceUpstreamSyncMetadata.ReviewedCommitDate),
                    ("upstream_commit", latest.Sha),
                    ("upstream_commit_date", latest.CommitDate),
                    ("upstream_repository", YFinanceUpstreamSyncMetadata.UpstreamRepository));
                return;
            }

            _trace.WarnState(
                "YFinance.UpstreamSync",
                "UpstreamYFinanceNewerThanReviewed",
                ("reviewed_commit", YFinanceUpstreamSyncMetadata.ReviewedCommit),
                ("reviewed_commit_date", YFinanceUpstreamSyncMetadata.ReviewedCommitDate),
                ("reviewed_version", YFinanceUpstreamSyncMetadata.ReviewedVersion),
                ("reviewed_by_cr", YFinanceUpstreamSyncMetadata.ReviewedByCr),
                ("upstream_commit", latest.Sha),
                ("upstream_commit_date", latest.CommitDate),
                ("upstream_html_url", latest.HtmlUrl),
                ("upstream_repository", YFinanceUpstreamSyncMetadata.UpstreamRepository),
                ("check_interval_hours", Math.Round(GetSafeInterval().TotalHours, 2)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _trace.InfoState(
                "YFinance.UpstreamSync",
                "UpstreamSyncCheckUnavailable",
                ("reviewed_commit", YFinanceUpstreamSyncMetadata.ReviewedCommit),
                ("upstream_repository", YFinanceUpstreamSyncMetadata.UpstreamRepository),
                ("message", ex.Message));
        }
    }

    private void TraceBaseline(string eventName)
        => _trace.InfoState(
            "YFinance.UpstreamSync",
            eventName,
            ("reviewed_commit", YFinanceUpstreamSyncMetadata.ReviewedCommit),
            ("reviewed_commit_date", YFinanceUpstreamSyncMetadata.ReviewedCommitDate),
            ("reviewed_version", YFinanceUpstreamSyncMetadata.ReviewedVersion),
            ("reviewed_by_cr", YFinanceUpstreamSyncMetadata.ReviewedByCr),
            ("upstream_repository", YFinanceUpstreamSyncMetadata.UpstreamRepository),
            ("check_interval_hours", Math.Round(GetSafeInterval().TotalHours, 2)));

    private async Task<UpstreamCommit> FetchLatestCommitAsync(CancellationToken cancellationToken)
    {
        // Best-effort diagnostic only: GitHub may throttle anonymous requests.
        // The default interval is conservative and clamped by GetSafeInterval().
        using HttpResponseMessage response = await _httpClient.GetAsync(_options.UpstreamSyncCommitsApiUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement first = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array when document.RootElement.GetArrayLength() > 0 => document.RootElement[0],
            JsonValueKind.Object => document.RootElement,
            _ => throw new InvalidOperationException("GitHub commits response did not contain a commit object.")
        };

        string sha = first.TryGetProperty("sha", out JsonElement shaElement)
            ? shaElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(sha))
            throw new InvalidOperationException("GitHub commits response did not contain a SHA.");

        string? commitDate = null;
        if (first.TryGetProperty("commit", out JsonElement commit) &&
            commit.TryGetProperty("committer", out JsonElement committer) &&
            committer.TryGetProperty("date", out JsonElement date))
        {
            commitDate = date.GetString();
        }

        string? htmlUrl = first.TryGetProperty("html_url", out JsonElement htmlUrlElement)
            ? htmlUrlElement.GetString()
            : null;

        return new UpstreamCommit(sha, commitDate ?? string.Empty, htmlUrl ?? string.Empty);
    }

    private TimeSpan GetSafeInterval()
        => _options.UpstreamSyncCheckInterval < TimeSpan.FromHours(1)
            ? TimeSpan.FromHours(1)
            : _options.UpstreamSyncCheckInterval;

    private TimeSpan GetSafeTimeout()
        => _options.UpstreamSyncCheckTimeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(10)
            : _options.UpstreamSyncCheckTimeout;

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed record UpstreamCommit(string Sha, string CommitDate, string HtmlUrl);
}
