using System.Net;
using System.Text.Json;
using YFinance.NET.Caching;
using YFinance.NET.Config;
using YFinance.NET.Exceptions;

namespace YFinance.NET.Transport;

public sealed class YahooFinanceHttpClient : IDisposable
{
    private readonly YFinanceOptions _options;
    private readonly YahooSessionManager _sessionManager;
    private readonly RequestThrottle _throttle;
    private readonly MemoryTtlCache<JsonDocument> _cache;

    public YahooFinanceHttpClient(YFinanceOptions? options = null)
    {
        _options = options ?? new YFinanceOptions();
        _sessionManager = new YahooSessionManager(_options);
        _throttle = new RequestThrottle(_options.MinimumRequestSpacing);
        _cache = new MemoryTtlCache<JsonDocument>();
    }

    public async Task<JsonDocument> GetJsonAsync(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query = null, CancellationToken cancellationToken = default)
    {
        return await SendJsonAsync(relativeOrAbsoluteUrl, query, allowCache: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonDocument> GetCachedJsonAsync(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query = null, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        string cacheKey = MemoryTtlCache<JsonDocument>.BuildKey(relativeOrAbsoluteUrl, BuildQueryKey(query));
        if (_cache.TryGet(cacheKey, out JsonDocument? cached) && cached is not null)
        {
            return JsonDocument.Parse(cached.RootElement.GetRawText());
        }

        JsonDocument json = await SendJsonAsync(relativeOrAbsoluteUrl, query, allowCache: false, cancellationToken).ConfigureAwait(false);
        _cache.Set(cacheKey, JsonDocument.Parse(json.RootElement.GetRawText()), ttl ?? _options.DefaultCacheTtl);
        return json;
    }

    private async Task<JsonDocument> SendJsonAsync(string relativeOrAbsoluteUrl, IReadOnlyDictionary<string, string?>? query, bool allowCache, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            YahooSessionState session = await _sessionManager.GetSessionAsync(attempt > 0, cancellationToken).ConfigureAwait(false);
            Uri requestUri = BuildUri(relativeOrAbsoluteUrl, query, session.Crumb);
            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
            request.Headers.TryAddWithoutValidation("x-csrf-token", session.Crumb);
            request.Headers.TryAddWithoutValidation("x-yahoo-request-id", Guid.NewGuid().ToString("N"));

            using HttpResponseMessage response = await _sessionManager.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw new YFinanceRateLimitException("Yahoo returned HTTP 429 Too Many Requests.", 429);
                }

                TimeSpan delay = GetRetryDelay(response, attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if ((int)response.StatusCode >= 400)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (ShouldRefreshSession(body, response.StatusCode))
                {
                    _sessionManager.Invalidate();
                    continue;
                }

                throw new YFinanceApiException($"Yahoo request failed with HTTP {(int)response.StatusCode}: {body}", (int)response.StatusCode);
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonDocument.Parse(content);
        }

        throw new YFinanceApiException("Yahoo request exhausted retry attempts.");
    }

    private static bool ShouldRefreshSession(string body, HttpStatusCode statusCode)
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

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
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
