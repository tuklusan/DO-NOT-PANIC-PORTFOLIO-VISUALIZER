using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class FinanceNewsService
{
    internal const string ClosingQuoteHeadlinePrefix = "[[CLOSING_QUOTE]] ";
    private const string SummaryItemStartMarker = "[[ITEM]]";
    private const string SummaryItemEndMarker = "[[/ITEM]]";
    private const string SummaryProseSeparator = "---";
    private const string CacheFileName = "finance-news-cache.json";
    private const string DefaultFeedUrl = "https://finance.yahoo.com/news/rss";
    private const string DeepSeekApiUrl = "https://api.deepseek.com/chat/completions";
    private const string DeepSeekModel = "deepseek-v4-flash";
    private const string CnbcWorldFeedUrl = "https://www.cnbc.com/id/19832390/device/rss/rss.html";
    private const string BbcBusinessFeedUrl = "https://feeds.bbci.co.uk/news/business/rss.xml";
    private const string NytEconomyFeedUrl = "https://rss.nytimes.com/services/xml/rss/nyt/Economy.xml";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] SummarizedNewsFeedUrls =
    [
        CnbcWorldFeedUrl,
        BbcBusinessFeedUrl,
        NytEconomyFeedUrl
    ];
    private const string DouglasAdamsClosingQuote = "\"Nothing travels faster than the speed of light, with the possible exception of bad news, which obeys its own special laws.\"";
    private const string WilliamShakespeareClosingQuote = "\"All that glisters is not gold.\"";
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
        AppSettings settings,
        bool networkAvailable,
        CancellationToken cancellationToken = default)
    {
        NewsScrollerMode mode = settings.NewsScrollerMode;
        string requestUrl = NormalizeFeedUrl(settings.NewsFeedUrl);
        TimeSpan refreshInterval = GetRefreshInterval(mode, settings.NewsRefreshMinutes);
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
                _ => await FetchSummarizedFinancialNewsAsync(httpClient, settings, cancellationToken)
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

    private async Task<List<string>> FetchSummarizedFinancialNewsAsync(
        HttpClient httpClient,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        string apiKey = ResolveDeepSeekApiKey(settings.DeepSeekApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        SummarizedNewsContext context = await FetchSummarizedNewsContextAsync(httpClient, settings, cancellationToken);
        if (context.Headlines.Count == 0)
            return [];

        var payload = new
        {
            model = DeepSeekModel,
            temperature = 0.2,
            max_tokens = 900,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are given freshly fetched internet headlines. Rewrite only the supplied facts. Do not claim to have browsed the web yourself. Do not introduce, infer, update, correct, or embellish facts beyond the supplied text. Never include investment recommendations, stock-picking language, or advice about whether an asset is a buy, sell, or hold. Preserve a compact, display-friendly output. Use the exact marker format requested by the user prompt."
                },
                new
                {
                    role = "user",
                    content = BuildSummarizedNewsPrompt(settings.DeepSeekWritingStyle, context)
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

        List<string> summarizedItems = ParseSummarizedNewsItems(contentElement.GetString());
        if (summarizedItems.Count == 0)
            return [];

        summarizedItems.Add(BuildClosingQuoteHeadline(settings.DeepSeekWritingStyle));
        return summarizedItems;
    }

    private static async Task<SummarizedNewsContext> FetchSummarizedNewsContextAsync(
        HttpClient httpClient,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        List<string> mergedHeadlines = [];
        foreach (string feedUrl in SummarizedNewsFeedUrls)
        {
            try
            {
                List<string> feedHeadlines = await FetchRssHeadlinesAsync(httpClient, feedUrl, cancellationToken);
                foreach (string headline in feedHeadlines)
                {
                    if (mergedHeadlines.Contains(headline, StringComparer.OrdinalIgnoreCase))
                        continue;

                    mergedHeadlines.Add(headline);
                    if (mergedHeadlines.Count >= 10)
                        break;
                }
            }
            catch
            {
                // Ignore feed-specific failures and continue building a partial live context.
            }

            if (mergedHeadlines.Count >= 10)
                break;
        }

        return new SummarizedNewsContext(DateTimeOffset.UtcNow, mergedHeadlines);
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

    private static string BuildSummarizedNewsPrompt(DeepSeekWritingStyle writingStyle, SummarizedNewsContext context)
    {
        StringBuilder builder = new();
        builder.AppendLine("You are a dependable fiduciary and are presenting current financial news highlights to your customers.");
        builder.AppendLine(GetWritingStyleInstruction(writingStyle));
        builder.Append("Restyle this live financial snapshot captured at ");
        builder.Append(context.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'"));
        builder.AppendLine(" into short sequential items for the news scroller.");
        builder.AppendLine("For each item, write three short haiku-style lines first, then one compact Adams-style prose line using only the supplied facts.");
        builder.AppendLine("The haiku may sound bleak, officious, or absurdly bureaucratic in a Vogon-adjacent way, but it must still reflect the supplied facts.");
        builder.AppendLine("The prose line must remain recognizably Douglas Adams in tone and also use only the supplied facts.");
        builder.AppendLine("Only restyle the supplied facts. Do not add, remove, alter, correct, or infer factual content.");
        builder.AppendLine("Never include investment recommendations, stock-picking language, or advice about whether an asset is a buy, sell, or hold.");
        builder.AppendLine("Do not include any specific numerical values, prices, percentages, dates, or times unless the source headline itself makes the number essential to the item's meaning.");
        builder.AppendLine("Ignore soft feature stories, local consumer pieces, and duplicate headlines unless they clearly move global markets.");
        builder.AppendLine("Return between 4 and 6 items, using this exact machine-readable format and nothing else:");
        builder.AppendLine("[[ITEM]]");
        builder.AppendLine("haiku line 1");
        builder.AppendLine("haiku line 2");
        builder.AppendLine("haiku line 3");
        builder.AppendLine("---");
        builder.AppendLine("one compact prose line");
        builder.AppendLine("[[/ITEM]]");
        builder.AppendLine("Do not include titles, bullets, numbering, markdown, or any commentary outside those item blocks.");
        if (context.Headlines.Count > 0)
        {
            builder.AppendLine("Latest headlines:");
            foreach (string headline in context.Headlines.Take(8))
            {
                builder.Append("- ");
                builder.AppendLine(headline);
            }
        }

        builder.Append("Write only the item blocks and preserve the macro-financial meaning of the supplied headlines without adding fresh facts.");
        return builder.ToString();
    }

    internal static List<string> ParseSummarizedNewsItems(string? responseText)
    {
        string candidate = (responseText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return [];

        MatchCollection matches = Regex.Matches(
            candidate,
            $@"{Regex.Escape(SummaryItemStartMarker)}\s*(.*?)\s*{Regex.Escape(SummaryItemEndMarker)}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        List<string> items = [];
        foreach (Match match in matches)
        {
            string block = match.Groups[1].Value;
            string item = NormalizeSummarizedNewsItem(block);
            if (!string.IsNullOrWhiteSpace(item))
                items.Add(item);
        }

        return items;
    }

    private static string NormalizeSummarizedNewsItem(string? block)
    {
        if (string.IsNullOrWhiteSpace(block))
            return string.Empty;

        string normalizedBlock = block.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] parts = normalizedBlock.Split(
            [Environment.NewLine + SummaryProseSeparator + Environment.NewLine, "\n" + SummaryProseSeparator + "\n", SummaryProseSeparator],
            2,
            StringSplitOptions.None);

        string[] poeticLines = parts[0]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSummaryLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(3)
            .ToArray();

        if (poeticLines.Length == 0)
            return string.Empty;

        string prose = parts.Length > 1 ? NormalizeSummaryLine(parts[1]) : string.Empty;
        return string.IsNullOrWhiteSpace(prose)
            ? string.Join(Environment.NewLine, poeticLines)
            : string.Join(Environment.NewLine, poeticLines.Append(prose));
    }

    private static string NormalizeSummaryLine(string? text)
    {
        string candidate = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        candidate = Regex.Replace(candidate, @"[\u0000-\u001F\u007F]+", " ");
        candidate = Regex.Replace(candidate, @"\s+", " ");
        return candidate.Trim();
    }

    private static string GetWritingStyleInstruction(DeepSeekWritingStyle writingStyle)
        => writingStyle switch
        {
            DeepSeekWritingStyle.WilliamShakespeare => "You write in the style of William Shakespeare.",
            _ => "You write in the style of Douglas Adams."
        };

    private static string GetClosingQuotation(DeepSeekWritingStyle writingStyle)
        => writingStyle switch
        {
            DeepSeekWritingStyle.WilliamShakespeare => WilliamShakespeareClosingQuote,
            _ => DouglasAdamsClosingQuote
        };

    internal static string BuildClosingQuoteHeadline(DeepSeekWritingStyle writingStyle)
        => $"{ClosingQuoteHeadlinePrefix}{GetClosingQuotation(writingStyle)}";

    internal static bool TryParseSpecialHeadline(string headline, out string text, out bool isSupplemental)
    {
        text = (headline ?? string.Empty).Trim();
        isSupplemental = false;

        if (text.StartsWith(ClosingQuoteHeadlinePrefix, StringComparison.Ordinal))
        {
            text = text[ClosingQuoteHeadlinePrefix.Length..].Trim();
            isSupplemental = true;
        }

        return !string.IsNullOrWhiteSpace(text);
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
                ? ["Waiting for default finance headlines..."]
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

    private sealed record SummarizedNewsContext(
        DateTimeOffset CapturedAtUtc,
        IReadOnlyList<string> Headlines);
}

public sealed class NewsHeadlineCache
{
    public DateTimeOffset FetchTimestampUtc { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public string ModeKey { get; set; } = string.Empty;
    public List<string> Headlines { get; set; } = [];
}
