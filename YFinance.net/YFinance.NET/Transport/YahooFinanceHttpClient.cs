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
using YFinance.NET.Caching;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Exceptions;

namespace YFinance.NET.Transport;

public sealed class YahooFinanceHttpClient : IDisposable
{
    // This transport layer intentionally stays Yahoo-specific and upstream-shaped.
    // Do not spread request/session mechanics into unrelated app code.
    private readonly YFinanceOptions _options;
    private readonly YahooSessionManager _sessionManager;
    private readonly RequestThrottle _throttle;
    private readonly MemoryTtlCache<JsonDocument> _cache;
    private readonly YFinanceTrace _trace;

    public YahooFinanceHttpClient(YFinanceOptions? options = null, YFinanceTrace? trace = null)
    {
        _options = options ?? new YFinanceOptions();
        _trace = trace ?? new YFinanceTrace(_options.TraceSink);
        _sessionManager = new YahooSessionManager(_options, _trace);
        _throttle = new RequestThrottle(_options.MinimumRequestSpacing);
        _cache = new MemoryTtlCache<JsonDocument>();
    }

    public async Task<JsonDocument> GetJsonAsync(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query = null, CancellationToken cancellationToken = default)
        => await SendJsonAsync(relativeOrAbsoluteUrl, query, cancellationToken).ConfigureAwait(false);

    public async Task<JsonDocument> GetCachedJsonAsync(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query = null, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        string cacheKey = MemoryTtlCache<JsonDocument>.BuildKey(relativeOrAbsoluteUrl, BuildQueryKey(query));
        if (_cache.TryGet(cacheKey, out JsonDocument? cached) && cached is not null)
        {
            _trace.InfoState("YFinance.Http", "CachedJsonHit", ("path", relativeOrAbsoluteUrl), ("cache_key", cacheKey));
            return JsonDocument.Parse(cached.RootElement.GetRawText());
        }

        JsonDocument json = await SendJsonAsync(relativeOrAbsoluteUrl, query, cancellationToken).ConfigureAwait(false);
        _cache.Set(cacheKey, JsonDocument.Parse(json.RootElement.GetRawText()), ttl ?? _options.DefaultCacheTtl);
        _trace.InfoState("YFinance.Http", "CachedJsonStore", ("path", relativeOrAbsoluteUrl), ("cache_key", cacheKey), ("ttl_seconds", (ttl ?? _options.DefaultCacheTtl).TotalSeconds));
        return json;
    }

    private async Task<JsonDocument> SendJsonAsync(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            YahooSessionState session = await _sessionManager.GetSessionAsync(attempt > 0, cancellationToken).ConfigureAwait(false);
            Uri requestUri = BuildUri(relativeOrAbsoluteUrl, query, session.Crumb);
            Func<HttpRequestMessage> requestFactory = () => BuildRequest(requestUri, session);
            _trace.InfoState("YFinance.Http", "RequestStart", ("path", relativeOrAbsoluteUrl), ("attempt", attempt + 1), ("uri", requestUri.ToString()));

            using HttpResponseMessage response = await _sessionManager.SendAsync(requestFactory, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _trace.WarnState("YFinance.Http", "RequestRateLimited", ("path", relativeOrAbsoluteUrl), ("attempt", attempt + 1), ("status_code", 429));
                if (attempt >= _options.MaxRetries)
                {
                    throw new YFinanceRateLimitException("Yahoo returned HTTP 429 Too Many Requests.", 429);
                }

                await Task.Delay(GetRetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 400)
            {
                if (ShouldRefreshSession(content, response.StatusCode))
                {
                    _trace.WarnState("YFinance.Http", "SessionRefreshRequested", ("path", relativeOrAbsoluteUrl), ("status_code", (int)response.StatusCode), ("attempt", attempt + 1));
                    _sessionManager.Invalidate();
                    continue;
                }

                throw new YFinanceApiException($"Yahoo request failed with HTTP {(int)response.StatusCode}: {content}", (int)response.StatusCode);
            }

            if (LooksLikeConsentPayload(response, content))
            {
                _trace.WarnState("YFinance.Http", "ConsentPayloadDetected", ("path", relativeOrAbsoluteUrl), ("attempt", attempt + 1));
                _sessionManager.Invalidate();
                continue;
            }

            _trace.InfoState("YFinance.Http", "RequestComplete", ("path", relativeOrAbsoluteUrl), ("attempt", attempt + 1), ("status_code", (int)response.StatusCode), ("content_length", content.Length));
            return JsonDocument.Parse(content);
        }

        throw new YFinanceApiException("Yahoo request exhausted retry attempts.");
    }

    private HttpRequestMessage BuildRequest(Uri requestUri, YahooSessionState session)
    {
        HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("x-yahoo-request-id", Guid.NewGuid().ToString("N"));
        request.Headers.Referrer = _options.FinanceHomeUri;
        return request;
    }

    internal static bool ShouldRefreshSession(string body, HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        if (code is 401 or 403)
        {
            return true;
        }

        return body.Contains("invalid cookie", StringComparison.OrdinalIgnoreCase)
            || body.Contains("invalid crumb", StringComparison.OrdinalIgnoreCase)
            || (body.Contains("crumb", StringComparison.OrdinalIgnoreCase) && body.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            || body.Contains("csrf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeConsentPayload(HttpResponseMessage response, string body)
        => response.RequestMessage?.RequestUri?.Host.EndsWith("consent.yahoo.com", StringComparison.OrdinalIgnoreCase) == true
            || body.Contains("consent.yahoo.com", StringComparison.OrdinalIgnoreCase)
            || body.Contains("collectConsent", StringComparison.OrdinalIgnoreCase)
            || body.Contains("guce.yahoo.com", StringComparison.OrdinalIgnoreCase);

    internal static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values))
        {
            string? raw = values.FirstOrDefault();
            if (int.TryParse(raw, out int seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
    }

    private Uri BuildUri(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query, string crumb)
    {
        Uri baseUri = Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(_options.Query1BaseUri, relativeOrAbsoluteUrl);

        List<string> parameters = new();
        if (query is not null)
        {
            parameters.AddRange(query.Where(static pair => !string.IsNullOrWhiteSpace(pair.Value) && !pair.Key.Equals("crumb", StringComparison.OrdinalIgnoreCase))
                                     .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        }
        parameters.Add($"crumb={Uri.EscapeDataString(crumb)}");

        UriBuilder builder = new(baseUri);
        string existingQuery = builder.Query;
        string mergedQuery = string.Join("&", new[]
        {
            existingQuery.TrimStart('?'),
            string.Join("&", parameters)
        }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        builder.Query = mergedQuery;
        return builder.Uri;
    }

    private static string BuildQueryKey(IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("&", query.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                                         .Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }
}
