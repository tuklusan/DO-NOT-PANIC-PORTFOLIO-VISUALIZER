using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using YFinance.NET.Config;
using YFinance.NET.Exceptions;

namespace YFinance.NET.Transport;

public sealed class YahooSessionManager : IDisposable
{
    private static readonly Regex CrumbRegex = new("\"CrumbStore\":\\{\"crumb\":\"(?<crumb>[^\"]+)\"\\}", RegexOptions.Compiled);
    private readonly YFinanceOptions _options;
    private readonly CookieContainer _cookieContainer = new();
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private YahooSessionState? _cachedSession;

    public YahooSessionManager(YFinanceOptions? options = null)
    {
        _options = options ?? new YFinanceOptions();
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true
        };
        _httpClient = new HttpClient(_handler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<YahooSessionState> GetSessionAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        YahooSessionState? cached = _cachedSession;
        if (!forceRefresh && cached is not null && cached.IsValid(DateTimeOffset.UtcNow))
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _cachedSession;
            if (!forceRefresh && cached is not null && cached.IsValid(DateTimeOffset.UtcNow))
            {
                return cached;
            }

            YahooSessionState refreshed = await RefreshAsync(cancellationToken).ConfigureAwait(false);
            _cachedSession = refreshed;
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate() => _cachedSession = null;

    private async Task<YahooSessionState> RefreshAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage cookieRequest = new(HttpMethod.Get, _options.CookieBootstrapUri);
        using HttpResponseMessage cookieResponse = await _httpClient.SendAsync(cookieRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if ((int)cookieResponse.StatusCode == 429)
        {
            throw new YFinanceRateLimitException("Yahoo rate-limited cookie bootstrap.", 429);
        }

        string cookieHeader = BuildCookieHeader();
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            using HttpRequestMessage homeRequest = new(HttpMethod.Get, _options.FinanceHomeUri);
            using HttpResponseMessage homeResponse = await _httpClient.SendAsync(homeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)homeResponse.StatusCode == 429)
            {
                throw new YFinanceRateLimitException("Yahoo rate-limited finance home bootstrap.", 429);
            }
            cookieHeader = BuildCookieHeader();
        }

        using HttpRequestMessage crumbRequest = new(HttpMethod.Get, _options.CrumbUri);
        using HttpResponseMessage crumbResponse = await _httpClient.SendAsync(crumbRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if ((int)crumbResponse.StatusCode == 429)
        {
            throw new YFinanceRateLimitException("Yahoo rate-limited crumb bootstrap.", 429);
        }
        crumbResponse.EnsureSuccessStatusCode();
        string crumb = (await crumbResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();

        if (string.IsNullOrWhiteSpace(crumb) || crumb.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
        {
            throw new YFinanceApiException("Yahoo crumb bootstrap returned an invalid crumb.");
        }

        cookieHeader = BuildCookieHeader();
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            throw new YFinanceApiException("Yahoo session cookie header was empty after bootstrap.");
        }

        return new YahooSessionState(crumb, cookieHeader, DateTimeOffset.UtcNow.Add(_options.SessionTtl));
    }

    private string BuildCookieHeader()
    {
        IEnumerable<Cookie> cookies = _cookieContainer
            .GetCookies(_options.Query1BaseUri)
            .Cast<Cookie>()
            .Concat(_cookieContainer.GetCookies(_options.FinanceHomeUri).Cast<Cookie>())
            .GroupBy(static cookie => cookie.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last());

        return string.Join("; ", cookies.Where(static cookie => !string.IsNullOrWhiteSpace(cookie.Name) && !string.IsNullOrWhiteSpace(cookie.Value))
                                         .Select(static cookie => $"{cookie.Name}={cookie.Value}"));
    }

    public HttpClient HttpClient => _httpClient;

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
        _refreshLock.Dispose();
    }
}
