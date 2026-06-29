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
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using YFinance.NET.Config;
using YFinance.NET.Diagnostics;
using YFinance.NET.Exceptions;

namespace YFinance.NET.Transport;

public sealed class YahooSessionManager : IDisposable
{
    private static readonly Regex FormRegex = new("<form[^>]*action=[\"'](?<action>[^\"']+)[\"'][^>]*>(?<body>.*?)</form>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex InputRegex = new("<input[^>]*name=[\"'](?<name>[^\"']+)[\"'][^>]*?(?:value=[\"'](?<value>[^\"']*)[\"'])?[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // Cookie bootstrap, crumb lifecycle, and consent handling stay centralized
    // here to preserve a clean mapping back to upstream data.py responsibilities.
    private readonly YFinanceOptions _options;
    private readonly CookieContainer _cookieContainer = new();
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly YFinanceTrace _trace;
    private YahooSessionState? _cachedSession;

    public YahooSessionManager(YFinanceOptions? options = null, YFinanceTrace? trace = null)
    {
        _options = options ?? new YFinanceOptions();
        _trace = trace ?? new YFinanceTrace(_options.TraceSink);
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            AllowAutoRedirect = true
        };
        _httpClient = new HttpClient(_handler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
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

            _trace.InfoState("YFinance.Session", "SessionRefreshStart", ("forced", forceRefresh));
            YahooSessionState refreshed = await RefreshAsync(cancellationToken).ConfigureAwait(false);
            _cachedSession = refreshed;
            _trace.InfoState("YFinance.Session", "SessionRefreshComplete", ("expires_utc", refreshed.ExpiresUtc), ("cookie_length", refreshed.CookieHeader.Length));
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate() => _cachedSession = null;

    public Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
        => SendAsync(requestFactory, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    public async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, HttpCompletionOption completionOption, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.SendAsync(requestFactory(), completionOption, cancellationToken).ConfigureAwait(false);
        if (!IsConsentUrl(response.RequestMessage?.RequestUri))
        {
            return response;
        }

        _trace.WarnState("YFinance.Session", "ConsentRedirectDetected", ("request_uri", response.RequestMessage?.RequestUri?.ToString() ?? string.Empty));
        using (response)
        {
            using HttpResponseMessage consentResult = await AcceptConsentFormAsync(response, cancellationToken).ConfigureAwait(false);
            _trace.InfoState("YFinance.Session", "ConsentAcceptedReplay", ("status_code", (int)consentResult.StatusCode));
        }

        return await _httpClient.SendAsync(requestFactory(), completionOption, cancellationToken).ConfigureAwait(false);
    }

    private async Task<YahooSessionState> RefreshAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage cookieResponse = await SendSimpleGetAsync(_options.CookieBootstrapUri, cancellationToken).ConfigureAwait(false);
        if ((int)cookieResponse.StatusCode == 429)
        {
            _trace.WarnState("YFinance.Session", "CookieBootstrapRateLimited", ("uri", _options.CookieBootstrapUri.ToString()), ("status_code", 429));
            throw new YFinanceRateLimitException("Yahoo rate-limited cookie bootstrap.", 429);
        }

        string cookieHeader = BuildCookieHeader();
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            using HttpResponseMessage homeResponse = await SendSimpleGetAsync(_options.FinanceHomeUri, cancellationToken).ConfigureAwait(false);
            if ((int)homeResponse.StatusCode == 429)
            {
                _trace.WarnState("YFinance.Session", "FinanceHomeBootstrapRateLimited", ("uri", _options.FinanceHomeUri.ToString()), ("status_code", 429));
                throw new YFinanceRateLimitException("Yahoo rate-limited finance home bootstrap.", 429);
            }

            cookieHeader = BuildCookieHeader();
        }

        using HttpResponseMessage crumbResponse = await SendSimpleGetAsync(_options.CrumbUri, cancellationToken).ConfigureAwait(false);
        if ((int)crumbResponse.StatusCode == 429)
        {
            _trace.WarnState("YFinance.Session", "CrumbBootstrapRateLimited", ("uri", _options.CrumbUri.ToString()), ("status_code", 429));
            throw new YFinanceRateLimitException("Yahoo rate-limited crumb bootstrap.", 429);
        }

        crumbResponse.EnsureSuccessStatusCode();
        string crumb = (await crumbResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        if (string.IsNullOrWhiteSpace(crumb) || crumb.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) || crumb.Contains("<html>", StringComparison.OrdinalIgnoreCase))
        {
            _trace.ErrorState("YFinance.Session", "InvalidCrumb", null, ("crumb_preview", crumb));
            throw new YFinanceApiException("Yahoo crumb bootstrap returned an invalid crumb.");
        }

        cookieHeader = BuildCookieHeader();
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            throw new YFinanceApiException("Yahoo session cookie header was empty after bootstrap.");
        }

        return new YahooSessionState(crumb, cookieHeader, DateTimeOffset.UtcNow.Add(_options.SessionTtl));
    }

    private async Task<HttpResponseMessage> SendSimpleGetAsync(Uri uri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!IsConsentUrl(response.RequestMessage?.RequestUri))
        {
            return response;
        }

        try
        {
            return await AcceptConsentFormAsync(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<HttpResponseMessage> AcceptConsentFormAsync(HttpResponseMessage consentResponse, CancellationToken cancellationToken)
    {
        string html = await consentResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Match formMatch = FormRegex.Match(html);
        if (!formMatch.Success)
        {
            _trace.ErrorState("YFinance.Session", "ConsentFormMissing", null, ("request_uri", consentResponse.RequestMessage?.RequestUri?.ToString() ?? string.Empty));
            throw new YFinanceApiException("Yahoo redirected to a consent page, but the consent form could not be parsed.");
        }

        Uri baseUri = consentResponse.RequestMessage?.RequestUri ?? _options.FinanceHomeUri;
        Uri actionUri = new(baseUri, WebUtility.HtmlDecode(formMatch.Groups["action"].Value));
        Dictionary<string, string> formValues = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in InputRegex.Matches(formMatch.Groups["body"].Value))
        {
            string name = WebUtility.HtmlDecode(match.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string value = WebUtility.HtmlDecode(match.Groups["value"].Value ?? string.Empty);
            formValues[name] = value;
        }

        if (!formValues.Keys.Any(static key => key.Contains("agree", StringComparison.OrdinalIgnoreCase) || key.Contains("accept", StringComparison.OrdinalIgnoreCase)))
        {
            formValues["agree"] = "1";
        }

        using HttpRequestMessage request = new(HttpMethod.Post, actionUri)
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Referrer = baseUri;
        _trace.InfoState("YFinance.Session", "ConsentSubmit", ("action_uri", actionUri.ToString()), ("field_count", formValues.Count));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsConsentUrl(Uri? uri)
        => uri is not null && uri.Host.EndsWith("consent.yahoo.com", StringComparison.OrdinalIgnoreCase);

    private string BuildCookieHeader()
    {
        IEnumerable<Cookie> cookies = _cookieContainer
            .GetCookies(_options.Query1BaseUri)
            .Cast<Cookie>()
            .Concat(_cookieContainer.GetCookies(_options.Query2BaseUri).Cast<Cookie>())
            .Concat(_cookieContainer.GetCookies(_options.FinanceHomeUri).Cast<Cookie>())
            .Where(static cookie => !cookie.Domain.Contains("consent", StringComparison.OrdinalIgnoreCase))
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
