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
using System.Net.Http;
using System.Threading;

namespace PortfolioSaver.Shared.Services;

public sealed class InternetProbeService
{
    private static readonly SocketsHttpHandler SharedProbeHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
    };

    private static readonly HttpClient SharedProbeClient = new(SharedProbeHandler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    internal static SocketsHttpHandler SharedProbeHandlerForTests => SharedProbeHandler;
    internal static HttpClient SharedProbeClientForTests => SharedProbeClient;
    private static readonly string[] DefaultProbeUrls =
    [
        "https://www.msftconnecttest.com/connecttest.txt",
        "https://www.gstatic.com/generate_204"
    ];

    private readonly string[] _probeUrls;
    private readonly int _attempts;
    private readonly int _timeoutMilliseconds;
    private readonly TimeSpan _cacheDuration;
    private readonly Func<HttpMessageHandler>? _messageHandlerFactory;
    private readonly object _sync = new();

    private DateTimeOffset _lastProbeUtc = DateTimeOffset.MinValue;
    private bool _lastProbeResult;
    private Task<bool>? _inFlightProbe;

    public InternetProbeService(
        IEnumerable<string>? probeUrls = null,
        int attempts = 2,
        int timeoutMilliseconds = 1500,
        TimeSpan? cacheDuration = null,
        Func<HttpMessageHandler>? messageHandlerFactory = null)
    {
        _probeUrls = NormalizeProbeUrls(probeUrls);
        _attempts = Math.Max(1, attempts);
        _timeoutMilliseconds = Math.Clamp(timeoutMilliseconds, 250, 5000);
        _cacheDuration = cacheDuration ?? TimeSpan.FromSeconds(10);
        _messageHandlerFactory = messageHandlerFactory;
    }

    internal IReadOnlyList<string> ProbeUrlsForTests => _probeUrls;
    internal int AttemptsForTests => _attempts;

    internal void SetCacheForTests(DateTimeOffset lastProbeUtc, bool lastProbeResult)
    {
        lock (_sync)
        {
            _lastProbeUtc = lastProbeUtc;
            _lastProbeResult = lastProbeResult;
        }
    }

    public bool IsInternetAvailable()
        => IsInternetAvailableAsync().GetAwaiter().GetResult();

    public async Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken = default)
    {
        Task<bool> probeTask;
        lock (_sync)
        {
            if (DateTimeOffset.UtcNow - _lastProbeUtc <= _cacheDuration)
                return _lastProbeResult;

            if (_inFlightProbe is not null)
                probeTask = _inFlightProbe;
            else
            {
                probeTask = ProbeAndCacheAsync(cancellationToken);
                _inFlightProbe = probeTask;
            }
        }

        return await probeTask.ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
        lock (_sync)
            _lastProbeUtc = DateTimeOffset.MinValue;
    }

    private async Task<bool> ProbeAndCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool available = await ProbeInternetAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _lastProbeResult = available;
                _lastProbeUtc = DateTimeOffset.UtcNow;
                return _lastProbeResult;
            }
        }
        finally
        {
            lock (_sync)
                _inFlightProbe = null;
        }
    }

    private async Task<bool> ProbeInternetAsync(CancellationToken cancellationToken)
    {
        using HttpClient? disposableClient = _messageHandlerFactory is null ? null : CreateProbeClient();
        HttpClient client = disposableClient ?? SharedProbeClient;
        TimeSpan requestTimeout = TimeSpan.FromMilliseconds(_timeoutMilliseconds);

        for (int attempt = 0; attempt < _attempts; attempt++)
        {
            foreach (string probeUrl in _probeUrls)
            {
                if (await TryProbeUrlAsync(client, probeUrl, requestTimeout, cancellationToken).ConfigureAwait(false))
                    return true;
            }

            if (attempt < _attempts - 1)
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private HttpClient CreateProbeClient()
        => new(_messageHandlerFactory!())
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    private static async Task<bool> TryProbeUrlAsync(HttpClient client, string probeUrl, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(requestTimeout);
            using HttpRequestMessage request = new(HttpMethod.Get, probeUrl);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            int statusCode = (int)response.StatusCode;
            return statusCode >= 200 && statusCode < 500;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string[] NormalizeProbeUrls(IEnumerable<string>? probeUrls)
    {
        string[] normalized = (probeUrls ?? DefaultProbeUrls)
            .Select(url => (url ?? string.Empty).Trim())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Contains("://", StringComparison.Ordinal) ? url : $"https://{url}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length > 0 ? normalized : DefaultProbeUrls;
    }
}
