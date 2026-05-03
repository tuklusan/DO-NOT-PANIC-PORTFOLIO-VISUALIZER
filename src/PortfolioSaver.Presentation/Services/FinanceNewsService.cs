using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class FinanceNewsService
{
    private const string CacheFileName = "finance-news-cache.json";
    private const string DefaultFeedUrl = "https://finance.yahoo.com/news/rss";
    private const string DeepSeekApiUrl = "https://api.deepseek.com/chat/completions";
    private const string DeepSeekModel = "deepseek-v4-flash";
    private const string SummarizedNewsPrompt = "Enable Web Search and Summarize the latest global financial news in one paragraph";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _cachePath;
    private readonly Func<string> _deepSeekApiKeyResolver;

    public FinanceNewsService(string? cachePath = null, Func<string>? deepSeekApiKeyResolver = null)
    {
        _cachePath = string.IsNullOrWhiteSpace(cachePath)
            ? Path.Combine(PathHelper.GetLocalDataDirectory(), CacheFileName)
            : cachePath;
        _deepSeekApiKeyResolver = deepSeekApiKeyResolver ?? (() => string.Empty);
    }

    public async Task<IReadOnlyList<string>> GetHeadlinesAsync(
        HttpClient httpClient,
        NewsScrollerMode mode,
        string? deepSeekApiKey,
        string? feedUrl,
        int refreshMinutes,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        string requestUrl = NormalizeFeedUrl(feedUrl);
        TimeSpan refreshInterval = GetRefreshInterval(mode, refreshMinutes);
        NewsHeadlineCache cache = await LoadCacheAsync(cancellationToken);
        IReadOnlyList<string> matchingCachedHeadlines =
            string.Equals(cache.ModeKey, GetModeKey(mode), StringComparison.OrdinalIgnoreCase)
                ? cache.Headlines
                : [];
        if (!networkAvailable)
            return GetFallbackHeadlines(mode, matchingCachedHeadlines);

        if (cache.FetchTimestampUtc != DateTimeOffset.MinValue &&
            string.Equals(cache.ModeKey, GetModeKey(mode), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(cache.FeedUrl, requestUrl, StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.UtcNow - cache.FetchTimestampUtc < refreshInterval &&
            cache.Headlines.Count > 0)
        {
            return cache.Headlines;
        }

        try
        {
            List<string> headlines = mode switch
            {
                NewsScrollerMode.RssFeed => await FetchRssHeadlinesAsync(httpClient, requestUrl, cancellationToken),
                _ => await FetchSummarizedFinancialNewsAsync(httpClient, ResolveDeepSeekApiKey(deepSeekApiKey), cancellationToken)
            };

            if (headlines.Count == 0)
                return GetFallbackHeadlines(mode, matchingCachedHeadlines);

            NewsHeadlineCache refreshed = new()
            {
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                FeedUrl = requestUrl,
                ModeKey = GetModeKey(mode),
                Headlines = headlines
            };

            await SaveCacheAsync(refreshed, cancellationToken);
            return refreshed.Headlines;
        }
        catch
        {
            return GetFallbackHeadlines(mode, matchingCachedHeadlines);
        }
    }

    public IReadOnlyList<string> GetCachedHeadlines(NewsScrollerMode mode)
    {
        if (!File.Exists(_cachePath))
            return GetFallbackHeadlines(mode, []);

        try
        {
            string json = File.ReadAllText(_cachePath);
            NewsHeadlineCache? cache = JsonSerializer.Deserialize<NewsHeadlineCache>(json, JsonOptions);
            IReadOnlyList<string> matchingHeadlines =
                cache is not null &&
                string.Equals(cache.ModeKey, GetModeKey(mode), StringComparison.OrdinalIgnoreCase)
                    ? cache.Headlines
                    : [];
            return GetFallbackHeadlines(mode, matchingHeadlines);
        }
        catch
        {
            return GetFallbackHeadlines(mode, []);
        }
    }

    private static async Task<List<string>> FetchRssHeadlinesAsync(
        HttpClient httpClient,
        string requestUrl,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        string xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseHeadlines(xml);
    }

    private static async Task<List<string>> FetchSummarizedFinancialNewsAsync(
        HttpClient httpClient,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        var payload = new
        {
            model = DeepSeekModel,
            temperature = 0.2,
            max_tokens = 500,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Respond with a single compact paragraph suitable for a financial news ticker. Do not use bullets or numbered lists."
                },
                new
                {
                    role = "user",
                    content = SummarizedNewsPrompt
                }
            }
        };

        using HttpRequestMessage request = new(HttpMethod.Post, DeepSeekApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("choices", out JsonElement choicesElement) ||
            choicesElement.ValueKind != JsonValueKind.Array ||
            choicesElement.GetArrayLength() == 0)
        {
            return [];
        }

        JsonElement firstChoice = choicesElement[0];
        if (!firstChoice.TryGetProperty("message", out JsonElement messageElement) ||
            !messageElement.TryGetProperty("content", out JsonElement contentElement))
        {
            return [];
        }

        string normalized = NormalizeSummaryText(contentElement.GetString());
        return string.IsNullOrWhiteSpace(normalized) ? [] : [normalized];
    }

    private static List<string> ParseHeadlines(string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        return document.Descendants("item")
            .Elements("title")
            .Select(element => (element.Value ?? string.Empty).Trim())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeSummaryText(string? text)
    {
        string candidate = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        candidate = Regex.Replace(candidate, @"\s+", " ");
        return candidate.Trim();
    }

    private string ResolveDeepSeekApiKey(string? explicitApiKey)
    {
        string candidate = (explicitApiKey ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate;

        return (_deepSeekApiKeyResolver() ?? string.Empty).Trim();
    }

    private static IReadOnlyList<string> GetFallbackHeadlines(NewsScrollerMode mode, IReadOnlyList<string> cached)
        => cached.Count > 0
            ? cached
            : mode == NewsScrollerMode.RssFeed
                ? ["Waiting for Yahoo Finance headlines..."]
                : ["Waiting for summarized financial news..."];

    private async Task<NewsHeadlineCache> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath))
            return new NewsHeadlineCache();

        await using FileStream stream = File.OpenRead(_cachePath);
        NewsHeadlineCache? cache = await JsonSerializer.DeserializeAsync<NewsHeadlineCache>(stream, JsonOptions, cancellationToken);
        return cache ?? new NewsHeadlineCache();
    }

    private async Task SaveCacheAsync(NewsHeadlineCache cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        await using FileStream stream = File.Create(_cachePath);
        await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken);
    }

    private static TimeSpan GetRefreshInterval(NewsScrollerMode mode, int refreshMinutes)
    {
        int minimumMinutes = mode == NewsScrollerMode.SummarizedFinancialNews
            ? Defaults.MinimumSummarizedNewsRefreshMinutes
            : Defaults.MinNewsRefreshMinutes;
        int clampedMinutes = Math.Clamp(refreshMinutes, minimumMinutes, Defaults.MaxNewsRefreshMinutes);
        return TimeSpan.FromMinutes(clampedMinutes);
    }

    private static string NormalizeFeedUrl(string? feedUrl)
    {
        string candidate = (feedUrl ?? string.Empty).Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri.ToString();
        }

        return DefaultFeedUrl;
    }

    private static string GetModeKey(NewsScrollerMode mode)
        => mode switch
        {
            NewsScrollerMode.RssFeed => "rss",
            _ => "summarized-financial-news"
        };
}

public sealed class NewsHeadlineCache
{
    public DateTimeOffset FetchTimestampUtc { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public string ModeKey { get; set; } = string.Empty;
    public List<string> Headlines { get; set; } = [];
}
