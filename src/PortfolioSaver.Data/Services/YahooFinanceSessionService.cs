using System.Net.Http;

namespace PortfolioSaver.Data.Services;

public sealed class YahooFinanceSessionService
{
    private static readonly Uri YahooHomeUri = new("https://finance.yahoo.com/");
    private static readonly Uri CrumbUri = new("https://query1.finance.yahoo.com/v1/test/getcrumb");
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(45);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static YahooSessionState? _cachedSession;

    private readonly HttpClient _httpClient;

    public YahooFinanceSessionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        YahooSessionState session = await GetSessionAsync(forceRefresh: false, cancellationToken);
        HttpResponseMessage response = await SendWithSessionAsync(url, session, cancellationToken);
        if (!await ShouldRefreshSessionAsync(response, cancellationToken))
            return response;

        response.Dispose();
        session = await GetSessionAsync(forceRefresh: true, cancellationToken);
        return await SendWithSessionAsync(url, session, cancellationToken);
    }

    public void Invalidate()
        => _cachedSession = null;

    private async Task<YahooSessionState> GetSessionAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        YahooSessionState? cached = _cachedSession;
        if (!forceRefresh && cached is not null && cached.IsValid(DateTimeOffset.UtcNow))
            return cached;

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            cached = _cachedSession;
            if (!forceRefresh && cached is not null && cached.IsValid(DateTimeOffset.UtcNow))
                return cached;

            YahooSessionState refreshed = await RefreshSessionAsync(cancellationToken);
            _cachedSession = refreshed;
            return refreshed;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<YahooSessionState> RefreshSessionAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> cookies = new(StringComparer.OrdinalIgnoreCase);

        using (HttpResponseMessage bootstrap = await _httpClient.GetAsync(YahooHomeUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            MergeCookies(cookies, bootstrap);
        }

        using HttpRequestMessage crumbRequest = new(HttpMethod.Get, CrumbUri);
        string bootstrapCookieHeader = BuildCookieHeader(cookies);
        if (!string.IsNullOrWhiteSpace(bootstrapCookieHeader))
            crumbRequest.Headers.TryAddWithoutValidation("Cookie", bootstrapCookieHeader);

        using HttpResponseMessage crumbResponse = await _httpClient.SendAsync(crumbRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        crumbResponse.EnsureSuccessStatusCode();
        MergeCookies(cookies, crumbResponse);

        string crumb = (await crumbResponse.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(crumb))
            throw new InvalidOperationException("Yahoo Finance session crumb was empty.");

        string cookieHeader = BuildCookieHeader(cookies);
        if (string.IsNullOrWhiteSpace(cookieHeader))
            throw new InvalidOperationException("Yahoo Finance session cookie was empty.");

        return new YahooSessionState(
            Crumb: crumb,
            CookieHeader: cookieHeader,
            ExpiresUtc: DateTimeOffset.UtcNow.Add(SessionTtl));
    }

    private async Task<bool> ShouldRefreshSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return false;

        int statusCode = (int)response.StatusCode;
        if (statusCode is 401 or 403)
            return true;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("invalid cookie", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("invalid crumb", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("crumb", StringComparison.OrdinalIgnoreCase) && body.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("csrf", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> SendWithSessionAsync(
        string url,
        YahooSessionState session,
        CancellationToken cancellationToken)
    {
        string authenticatedUrl = AppendQueryParameter(url, "crumb", session.Crumb);
        using HttpRequestMessage request = new(HttpMethod.Get, authenticatedUrl);
        request.Headers.TryAddWithoutValidation("Cookie", session.CookieHeader);
        request.Headers.TryAddWithoutValidation("x-csrf-token", session.Crumb);
        request.Headers.TryAddWithoutValidation("x-yahoo-request-id", Guid.NewGuid().ToString("N"));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static string AppendQueryParameter(string url, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(value))
            return url;

        if (url.Contains($"{key}=", StringComparison.OrdinalIgnoreCase))
            return url;

        char separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{key}={Uri.EscapeDataString(value)}";
    }

    private static void MergeCookies(IDictionary<string, string> cookies, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookieHeaders))
            return;

        foreach (string header in setCookieHeaders)
        {
            if (string.IsNullOrWhiteSpace(header))
                continue;

            string firstSegment = header.Split(';', 2)[0].Trim();
            int equalsIndex = firstSegment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= firstSegment.Length - 1)
                continue;

            string name = firstSegment[..equalsIndex].Trim();
            string value = firstSegment[(equalsIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                continue;

            cookies[name] = value;
        }
    }

    private static string BuildCookieHeader(IReadOnlyDictionary<string, string> cookies)
        => string.Join("; ", cookies
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key}={pair.Value}"));

    private sealed record YahooSessionState(string Crumb, string CookieHeader, DateTimeOffset ExpiresUtc)
    {
        public bool IsValid(DateTimeOffset utcNow)
            => !string.IsNullOrWhiteSpace(Crumb) &&
               !string.IsNullOrWhiteSpace(CookieHeader) &&
               utcNow < ExpiresUtc.Subtract(TimeSpan.FromMinutes(2));
    }
}
