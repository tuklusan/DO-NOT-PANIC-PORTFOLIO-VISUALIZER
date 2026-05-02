using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class FinanceNewsService
{
    private const string CacheFileName = "finance-news-cache.json";
    private const string DefaultFeedUrl = "https://finance.yahoo.com/news/rss";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _cachePath = Path.Combine(PathHelper.GetLocalDataDirectory(), CacheFileName);

    public async Task<IReadOnlyList<string>> GetHeadlinesAsync(
        HttpClient httpClient,
        string? feedUrl,
        int refreshMinutes,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        string requestUrl = NormalizeFeedUrl(feedUrl);
        TimeSpan refreshInterval = TimeSpan.FromMinutes(Math.Clamp(refreshMinutes, 5, 240));
        NewsHeadlineCache cache = await LoadCacheAsync(cancellationToken);
        if (!networkAvailable)
            return GetFallbackHeadlines(cache.Headlines);

        if (cache.FetchTimestampUtc != DateTimeOffset.MinValue &&
            string.Equals(cache.FeedUrl, requestUrl, StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.UtcNow - cache.FetchTimestampUtc < refreshInterval &&
            cache.Headlines.Count > 0)
        {
            return cache.Headlines;
        }

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            string xml = await response.Content.ReadAsStringAsync(cancellationToken);
            List<string> headlines = ParseHeadlines(xml);
            if (headlines.Count == 0)
                return GetFallbackHeadlines(cache.Headlines);

            NewsHeadlineCache refreshed = new()
            {
                FetchTimestampUtc = DateTimeOffset.UtcNow,
                FeedUrl = requestUrl,
                Headlines = headlines
            };

            await SaveCacheAsync(refreshed, cancellationToken);
            return refreshed.Headlines;
        }
        catch
        {
            return GetFallbackHeadlines(cache.Headlines);
        }
    }

    public IReadOnlyList<string> GetCachedHeadlines()
    {
        if (!File.Exists(_cachePath))
            return GetFallbackHeadlines([]);

        try
        {
            string json = File.ReadAllText(_cachePath);
            NewsHeadlineCache? cache = JsonSerializer.Deserialize<NewsHeadlineCache>(json, JsonOptions);
            return GetFallbackHeadlines(cache?.Headlines ?? []);
        }
        catch
        {
            return GetFallbackHeadlines([]);
        }
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

    private static IReadOnlyList<string> GetFallbackHeadlines(IReadOnlyList<string> cached)
        => cached.Count > 0
            ? cached
            : ["Waiting for Yahoo Finance headlines..."];

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
}

public sealed class NewsHeadlineCache
{
    public DateTimeOffset FetchTimestampUtc { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public List<string> Headlines { get; set; } = [];
}
